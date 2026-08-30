# GasNet 联网接入：服务端权威 + 前端纯表现

> 目标读者：未来把 GasNet 接进"服务端权威模拟、客户端只做表现"的网络游戏时的自己。
> 本文记录架构结论、桥接层的全部接缝（API 均已对照 `src/GasNet` 源码核实），以及
> 将来若要做客户端预测，缺口在哪、从哪开始。

## 核心结论

**前端不需要引用 GasNet。** GasNet 只存在于服务端进程；前端只消费自定义网络协议
（DTO + 消息），它没有 GasNet.dll，也不理解 tag 语义——tag 到它手里只是字符串。

成立的原因：GasNet 是**本地权威单线程模型，自身没有网络层**（README「有意偏差」第一条）。
网络同步这层活本来就要宿主做；正确的做法不是把 GasNet 搬到前端，而是在服务端订阅它的事件，
把"战斗世界发生了什么"翻译成自己的协议。这个翻译层（下称**桥接层**）就是联网接入的全部工作。

```
前端（表现进程）                  服务端（权威进程）
┌────────────────────┐           ┌──────────────────────────────────┐
│ VFX / SFX / UI      │          │  桥接层（宿主写）                  │
│ cue tag → 表现资源   │ ◄─ DTO ──│   ↑ 订阅事件 / 调用 API           │
│ 实体状态插值         │           │  GasNet ASC × N（权威战斗模拟）    │
│ （无 GasNet 依赖）   │ ─输入意图─►│   ↓ Tick(dt) 固定步长            │
└────────────────────┘           └──────────────────────────────────┘
```

## 桥接层要做的事

### 1. 出站：订阅事件，翻译成消息

| 要同步的状态 | GasNet 事件面 | 建议消息 |
|---|---|---|
| 属性当前值 | `asc.GetGameplayAttributeValueChangeDelegate(attr).Handler`，参数 `OnAttributeChangeData`（Old/New） | `AttrChanged(entityId, attrName, newValue)`；同一帧多次变化合并成最终值再发 |
| 标签增减 | `asc.RegisterGameplayTagEvent(tag, GameplayTagEventType.NewOrRemoved, (tag, count) => ...)` | `TagAdded / TagRemoved(entityId, tag)`；计数有意义的状态（层数）用 `AnyCountChange` |
| 活跃 GE 增删 | ASC 级事件 `OnGameplayEffectAppliedToSelf` / `OnAnyGameplayEffectRemoved`（带 `GameplayEffectRemovalReason`） | `EffectApplied / EffectRemoved(entityId, handle, defName, duration, remaining)`；`defName` 前端只需透传/查本地表 |
| 能力生命周期 | ASC 级事件 `OnAbilityActivated` / `OnAbilityFailed(spec, failTags)` / `OnAbilityEnded(spec, wasCancelled)` | `AbilityActivated / AbilityFailed / AbilityEnded(entityId, abilityName, ...)` |
| 表现事件（Cue） | 每个 ASC 上有现成事件 `OnGameplayCueAdded` / `OnGameplayCueRemoved` / `OnGameplayCueExecuted`（见下） | `CueExecuted / CueAdded / CueRemoved(entityId, cueTag, magnitude, instigatorId)` |
| 冷却/时长剩余 | `asc.GetActiveEffectsTimeRemainingAndDuration(query)`（查询型，定期或按需同步） | 并入 `EffectApplied` 消息或单独的周期同步 |

**Cue 的转发**：首选每个 ASC 上的三个现成事件——GE 成功应用时触发的 cue、
手动 `ExecuteGameplayCue/AddGameplayCue/RemoveGameplayCue` 都会走这里：

```csharp
foreach (var (entityId, asc) in _entities)   // 桥接层自己维护的 实体 → ASC 表
{
    asc.OnGameplayCueExecuted += (cueTag, p) => Broadcast(new CueMsg(
        CueEvent.Executed, entityId, cueTag.Name, p.Magnitude, EntityId.Of(p.Instigator)));
    asc.OnGameplayCueAdded   += (cueTag, p) => Broadcast(...);   // Duration/Infinite GE 开始
    asc.OnGameplayCueRemoved += (cueTag, p) => Broadcast(...);   // 结束
}
```

`GameplayCueEvent` 的 `WhileActive` 是持续型每帧事件，ASC 事件面**不暴露它**——正好，
不要逐帧转发：前端收到 `CueAdded` 后本地循环播放，收到 `CueRemoved` 停止。

如果想在全局一处兜住所有实体的 cue（而不是逐 ASC 订阅），也可以给
`AbilitySystemGlobals.Get().GameplayCueManager` 注册一个 `GameplayCueTags = { "GameplayCue" }`
（根 tag）的转发型静态 notify：`FindNotify` 按精确 tag 优先、沿父级回溯查找，
根 tag notify 能兜住所有 cue，且不会抢走真实按 tag 注册的 notify（精确层级先命中）。

### 2. 入站：输入意图 → ASC 调用

- 前端发 `InputPressed(inputId)` / `InputReleased(inputId)` → 服务端调
  `asc.AbilityLocalInputPressed(inputId)` / `AbilityLocalInputReleased`（"按住生效"型能力依赖 Release）。
- 目标选择：前端发目标实体 ID，服务端解析成 ASC 引用再
  `ApplyGameplayEffectToTarget` / 构造带目标的 `GameplayEffectContext`。
- **信任边界**：前端消息只是"意图"。一切校验——`CanActivateAbility`、成本、冷却、
  标签需求、免疫——都在服务端由 GasNet 完成；失败通过 `OnAbilityFailed` 事件回发给前端。

### 3. 上线快照（进房 / 断线重连）

新前端接入时需要一次性下发完整权威状态。建议**桥接层自己维护一份镜像状态**：
所有出站消息在发送的同时落到本地镜像，快照直接从镜像序列化，而不是反向枚举 GasNet 内部容器。
镜像同时就是断线重连和调试回放的基础。

仍需从 GasNet 现查的只有活跃 GE 列表：
`asc.GetActiveEffects(query)` + `asc.GetActiveEffectsTimeRemainingAndDuration(query)`
（含每条 GE 的剩余时长/总时长，前端进度条要用）。进行中的 cue 前端自己维护
`CueAdded` 集合即可，重连时服务端重发或前端清空重建。

### 4. 时间与 Tick

- 服务端**固定步长**驱动：`clock.Advance(dt); asc.Tick(dt);`（建议 20–30 Hz）。
  GasNet 不自带动心跳，忘记 Tick 就没有战斗——这同时也是天然的消息 flush 边界：
  每个 tick 结束后把本帧攒下的出站消息一次性发走。
- 把 tick 序号写进消息头，用于前端排序与将来的对时。
- **移动/位置同步完全不在 GasNet 范围内**——GasNet 只管属性/标签/能力/ cue，
  实体位移、寻路、命中判定的空间部分由宿主的网络层自己解决。

## Tag 与表现的映射策略

前端把 tag 当字符串，映射到表现资源有三种做法：

1. **约定表（最简单）**：前端硬编码 `cueTag → VFX/SFX` 映射，改动需要前端发版；
2. **数据表下发（推荐）**：服务端把映射表（JSON）随版本下发，改表现不用发版；
3. **前端引 GasNet 只为 tag 层级匹配**：不推荐——为一个 `MatchesTag` 引整个库不值。
   若确实需要祖先匹配语义，在服务端解析成精确 tag 列表再发，保持前端零依赖。

注意 GasNet 的 `HasTag` 是**祖先匹配**（`GameplayCue.Combat.Hit` 命中 `GameplayCue.Combat` 的查询）。
桥接层发出去的要么是精确 tag（前端精确比对），要么在服务端把匹配规则解析完再下发。

## 对象引用 ↔ 实体 ID

`GameplayCueParameters` 与 `GameplayEffectContext` 里的 `Instigator / EffectCauser /
SourceObject / Target` 都是 `object?`（宿主塞自己的 actor 对象）。发网络消息时必须换成实体 ID。
两种做法任选：

- 桥接层维护 `Dictionary<object, EntityId>` 注册表（对象创建/销毁时登记）；
- 或约定服务端传给 `MakeEffectContext` 的 source 一律是"带 ID 的包装对象"，直接读 ID。

这也是将来做预测时的**第一个硬障碍**（见下节）：这些活对象引用不可序列化。

## 固有代价：延迟与缓解

纯表现前端意味着所有反馈都要等 RTT：按技能 → 服务器确认 → 表现，弱网下 100–300 ms 明显可见。
在不引 GasNet、不做完整预测的前提下，低成本的缓解手段：

- **乐观起手表现**：前端按下键立刻播起手动画/音效，收到 `AbilityFailed`（带失败标签）时
  回滚 UI 状态并播失败反馈；
- **倒计时本地插值**：冷却/蓝量以服务器下发的值为准，前端本地平滑倒数，不做逐帧同步；
- **位移前端先行**：本地位移模拟 + 服务器定期校正（这与 GasNet 无关，属于宿主网络层）。

注意：`GameplayAbilityNetExecutionPolicy` 枚举在 GasNet 里**只是说明性的**，没有任何网络语义，
不要基于它写分支逻辑。

## 将来的客户端预测：何时需要、缺口在哪

**触发条件**：乐观表现不足以救手感（快节奏动作/竞技），需要"按键立刻有表现且大概率正确"。

**形态**：前端也跑一个 ASC（同一套 AttributeSet 与能力定义），本地先行模拟，
服务器权威结果到达后对齐或回滚重放。届时前端才真正引用 GasNet——它是 netstandard2.1，
Unity/Godot 的前端运行时都能加载。

**现状可复用的**：

- 核心库零引擎依赖、ns2.1 可进前端运行时；
- 数据驱动层（`GasNet.Data`）让两端共享同一份 GE JSON 目录，预测端与权威端行为一致；
- `ITimeSource` 可注入——前端可以喂一个"服务器时间估计 + 本地推进"的预测时钟。

**现状的缺口（全部要宿主自研，评估工作量时别漏）**：

1. **无 PredictionKey / 依赖排序**：GE 应用没有幂等/去重机制，"预测应用 + 服务器确认后不重复应用"
   的逻辑要自己建（UE 里这正是 PredictionKey 体系干的活）；
2. **状态不可直接序列化**：`GameplayEffectSpec` / `GameplayEffectContext` 持有活对象引用
   （Instigator 等 `object?`）、事件订阅挂在容器上——回滚 = 从上一个权威快照**重建**前端 ASC
   状态再重放输入，而不是反序列化覆盖。需要宿主实现 ASC 状态的导出/重建接口；
3. **无时钟对齐辅助**：RTT 估计、时戳平滑是宿主的活。

**建议路径**（按成本递增）：

1. 先用纯服务端权威上线，观察延迟是否真的伤手感；
2. 伤手感先做上节的"乐观表现 + 服务器校正"——成本低一个数量级，解决大部分观感问题；
3. 确认需要真预测，再考虑给 GasNet 加最小的预测钩子（ASC 状态导出/重建、GE 应用序列号），
   并在 `GasNet.Tests` 里用 `ManualTimeSource` 锁定"预测-确认-回滚"语义。此为一个大投入里程碑，
   交接记录（`docs/handoff/260829-113055-handoff.md` §5）对此有同样的结论。

## 相关文档

- [README.md](../README.md)「有意偏差」——本文的架构前提（无网络/预测层、时间由宿主驱动）；
- [handoff/260829-113055-handoff.md](handoff/260829-113055-handoff.md) §5——预测层设计的历史结论
  （Spec 不可序列化是已知约束）；
- 数据格式：README「数据驱动（src/GasNet.Data）」——两端共享 GE 定义的方式。
