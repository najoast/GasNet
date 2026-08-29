# GasNet — 用 .NET 10 C# 实现的 GAS（Gameplay Ability System）

对 Unreal Engine GAS 插件（以 [GASDocumentation](https://github.com/tranek/GASDocumentation) 为规范来源）的 C# 移植。
目标不是复刻 UE 的 UObject/网络层，而是**逐条对应文档中的游戏逻辑语义**：标签层级、属性聚合数学、
GameplayEffect 生命周期、堆叠/免疫/持续标签、Ability 激活与提交、冷却/成本模式、GameplayCue 路由等。

```
GasNet.sln
├─ src/GasNet           核心库（无外部依赖，netstandard2.1 + net10.0）
├─ src/GasNet.Data      数据驱动层：JSON 目录 ↔ GameplayEffectDefinition（独立程序集，保持核心零依赖）
├─ src/GasNet.Sample    示例内容：属性集、GE 定义、能力、Cue（对应文档 §2 的示例工程）
├─ src/GasNet.Demo      可运行的战斗脚本演示（控制台 transcript）
├─ tools/GasNet.Editor  本地 Web 编辑器（Blazor Server）：可视化编辑 GE 目录 JSON
└─ tests/GasNet.Tests   102 个 xUnit 测试，把文档语义锁进断言
```

```bash
dotnet test GasNet.sln                          # 102/102 通过
dotnet run --project src/GasNet.Demo            # 10 幕战斗 transcript
dotnet run --project tools/GasNet.Editor        # 启动编辑器 → http://localhost:5177
```

## UE → GasNet 概念映射

| Unreal (GASDocumentation §) | GasNet (C#) | 说明 |
|---|---|---|
| `FGameplayTag` / `UGameplayTagsManager` (§4.2) | `GameplayTag` / `GameplayTagsManager` | 层级名注册为句柄；`MatchesTag` = 自身或祖先匹配 |
| `FGameplayTagContainer` | `GameplayTagContainer` | `HasTag` 层级匹配、`HasTagExact`、`HasAny/All/None` |
| `FGameplayTagCountContainer` | `GameplayTagCountContainer` | 计数表 + `RegisterGameplayTagEvent(NewOrRemoved/AnyCountChange)`（§4.2.1） |
| `FGameplayTagQuery` | `GameplayTagQuery` | All/Any/No 表达式树；空查询不匹配（UE 5.3+） |
| `IAbilitySystemInterface` | `IAbilitySystemInterface` | `GetAbilitySystemComponent()` |
| `UAttributeSet` / `FGameplayAttributeData` (§4.3/4.4) | `AttributeSet` / `GameplayAttributeData` | 公开字段 + 反射注册表替代 `ATTRIBUTE_ACCESSORS`/UPROPERTY |
| `FGameplayAttribute` | `GameplayAttribute` | `(SetType, FieldName)` 二元组，缓存 FieldInfo |
| `FAggregator` / `FAggregatorModChannel` (§4.5.4) | `AttributeAggregator` | 见下方聚合公式 |
| `FAggregatorEvaluateMetaData` (§4.4.7) | `AggregatorEvaluateMetaData` | `Default`、`MostNegativeMod_AllPositiveMods`、`OnlyStrongestSlow_AllOtherMods` |
| `UGameplayEffect`（蓝图子类） | `GameplayEffectDefinition` | 数据型"原型"对象 + 流式构建器；`Clone()` 充当 archetype 复制 |
| `FGameplayEffectSpec` (§4.5.9) | `GameplayEffectSpec` | Level/Duration/Period 覆写、DynamicGranted/AssetTags、SetByCaller |
| `MakeOutgoingSpec` / `ApplyGameplayEffectTo*` (§4.5.2) | `ASC.MakeOutgoingSpec` / `ApplyGameplayEffectToSelf/ToTarget` | 全部汇入 `ApplyGameplayEffectSpecToSelf` |
| `FActiveGameplayEffect` / `FActiveGameplayEffectsContainer` | `ActiveGameplayEffect` / `ActiveGameplayEffectsContainer` | 含 `OnRemoved/OnStackChanged/OnTimeChanged/OnPeriod` 事件集 |
| `FGameplayEffectModCallbackData` (§4.4.6) | `GameplayEffectModCallbackData` | `PostGameplayEffectExecute` 收到的数据 |
| MMC (§4.5.11) | `ModifierMagnitudeCalculation` + `CustomCalculationMagnitude` | `RelevantAttributesToCapture`、捕获绕过 PreAttributeChange |
| ExecCalc (§4.5.12) | `GameplayEffectExecutionCalculation` | 捕获定义、`OutExecutionOutput.AddOutputModifier`、`MarkGameplayCuesHandledManually` |
| 四种 ModifierMagnitude (§4.5.4) | `ScalableFloatMagnitude` / `AttributeBasedMagnitude` / `SetByCallerMagnitude` / `CustomCalculationMagnitude` | 系数公式 `((Mag*Coef)+Pre)*Post`；Snapshot 语义同文档表 |
| SetByCaller (§4.5.9.1) | `spec.Set/GetSetByCallerMagnitude(tag)` | 修饰符缺失对 → 记错误并返回 0（Divide 危险，同文档） |
| 堆叠 (§4.5.5) | `StackingType/StackLimitCount/Stack*Policy` | AggregateBySource/Target、DurationRefresh/PeriodReset/Expiry 三策略 |
| 授予技能 (§4.5.6) | `GrantedAbilityEntry` + `RemovalPolicy` | CancelImmediately / RemoveAbilityOnEnd / DoNothing |
| GE 标签五分类 (§4.5.7) | `AssetTags/GrantedTags/OngoingTagRequirements/ApplicationTagRequirements/TargetTagRequirements/RemoveGameplayEffectsWithTags` | 文档把 App/Target 合并，这里按引擎拆成四容器 + 免疫路径 |
| 免疫 (§4.5.8) | `GrantedApplicationImmunityTags` + `OnImmunityBlockGameplayEffect` | 另支持对 Spec 资产标签的免疫查询 |
| 自定义应用条件 (§4.5.13) | `GameplayEffectCustomApplicationRequirement` | 失败打 `GameplayEffect.Fail.CustomRequirement` 标签 |
| Cost GE (§4.5.14) | `CostGameplayEffect` + `CheckCost/ApplyCost/CommitCost` | MMC 读取能力实例的模式可用（`Context.AbilityInstance`） |
| Cooldown GE (§4.5.15) | `CooldownGameplayEffect` + `CooldownTags/CooldownDuration` | 共享冷却 GE 模式：DynamicGrantedTags + SetByCaller `Data.Cooldown`；`GetCooldownTags()` 并集；`GetActiveEffectsTimeRemainingAndDuration` |
| `UGameplayAbility` (§4.6) | `GameplayAbility` | `CanActivateAbility/TryActivate/CommitAbility/EndAbility/CancelAbility`、失败标签、`OnGiveAbility/OnAvatarSet/OnRemoveAbility` |
| Instancing Policy (§4.6.7) | `GameplayAbilityInstancingPolicy` | NonInstanced 用共享原型；PerActor 懒克隆并保持状态；PerExecution 每次激活新克隆 |
| Ability Tags (§4.6.9) | `AbilityTags/CancelAbilitiesWithTag/BlockAbilitiesWithTag/ActivationOwnedTags/Activation{Required,Blocked}Tags/Source*/Target*` | Source/Target 容器仅事件触发时评估 |
| Triggers / 事件 (§4.6.4, §4.6.11) | `Triggers` + `ASC.HandleGameplayEvent` / `GameplayAbilitySystemLibrary.SendGameplayEventToActor` | 支持 GameplayEvent / TagAdded / TagRemoved |
| Input (§4.6.2) | `AbilityLocalInputPressed/Released(inputID)` + `NotifyInputReleased` | "按住生效"型能力在 Release 时结束 |
| GameplayCue (§4.8) | `GameplayCueNotify_Static/_Actor` + `GameplayCueManager` | Instant/周期 → `Executed`；Duration/Infinite → `Added(OnActive+WhileActive)`/`Removed`；按 (target, tag) 管理实例、父级标签回溯查找 |
| `UAbilitySystemGlobals` (§4.9) | `AbilitySystemGlobals` | `InitGlobalData()` + 全局 CueManager |
| Ability Task (§4.7) | `GameplayAbility.OnAbilityTick(dt)` | 简化为能力级 Tick 钩子（见"偏差"） |

## 关键语义（与文档逐条对应）

**属性聚合数学（§4.5.4）**：`CurrentValue = ((Base + ΣAdd) * MultSum) / DivSum`，
其中 Multiply/Divide 是**偏置 1 的求和**：`Σ mags − N + 1`。文档的工作样例全部有测试锁定：
两个 1.5 相乘 → ×2.0（非 ×2.25）；两个 0.5 → ×0；1.1 与 0.5 → ×0.6；两个 5 → ×9。
Override 最后应用、最后写入者获胜。Instant GE / 周期跳对 BaseValue **逐条**应用操作符（引擎 ExecuteMod 语义），
Duration/Infinite GE 的修饰符进入聚合器作用于 CurrentValue。

**生命周期**：Instant → 永久改 BaseValue，不发标签、只发 `Executed` Cue；
Duration/Infinite → 改 CurrentValue、授予标签、`Added`/`Removed` Cue；
周期（Period>0）→ 每跳按 Instant 执行（改 BaseValue + `Executed` Cue）。
到期瞬间的那一跳被到期检查吞掉（与引擎 CheckDuration 先于周期执行的顺序一致）。

**元属性**（§4.3.3）：GE 只写 `MetaDamage/MetaHeal`，`AttributeSet.PostGameplayEffectExecute`
负责分发（护甲栈吸收、写入 Health、清零元属性）。

**捕获与派生属性**（§4.5.11/§4.3.5）：捕获经 ASC 聚合器现算、**绕过 PreAttributeChange**（需自行再钳制）；
非快照的目标侧捕获在依赖属性变化时自动重算（带深度上限防振荡）；源侧捕获仅当源=自身时自动重算，
其余场景用 `SetActiveGameplayEffectLevel` 手动重算。

**Cue 触发**（§4.8.2/§4.8.8）：仅在 GE 成功应用（未被标签/免疫拦截）时触发；
ExecCalc 可 `MarkGameplayCuesHandledManually()` 抑制。

## 与 UE 的有意偏差

- **无网络/预测层**：本地权威单线程模型。`NetExecutionPolicy` 保留为说明性枚举；无 Replication、PredictionKey、FastArray、RPC batching。
- **时间由宿主驱动**：ASC 不自带心跳，需周期调用 `ASC.Tick(dt)`；测试/回放注入 `ManualTimeSource`。
- **数据驱动为"JSON 目录 + 宿主注册"模型**（见下方"数据驱动"一节）：无 .ini/.udt 资产管线；标签仍是运行时注册（JSON 里的标签名首次出现时自动注册，不校验预先存在的标签表）；无曲线（`ScalableFloatMagnitude.ValuePerLevel` 代替）；无蓝图（逻辑类 ExecCalc/MMC/CAR/Ability 以"类型名 + 宿主注册"从数据中引用）。
- **AbilityTask 简化**：文档 §4.7 的任务系统用 `OnAbilityTick` 钩子 + C# 事件代替（`WaitTargetData`/蒙辰类任务不在范围内）。
- **堆叠修饰符不随层数翻倍**：与引擎一致——堆叠只增加 `StackCount` 并驱动刷新/过期策略；"每层 +X"请用 SetByCaller 或层变化事件（示例中的护甲栈以"存在即 +10、受击掉层"表达）。
- **乘/除为引擎默认求和**（非文档 §4.5.4.1 的引擎补丁版逐项相乘），并额外提供 `OnlyStrongestSlow_AllOtherMods` 限定器实现 §5.7 的"只取最强减速"。
- **AbilitySpec 无网络作用域锁**：单线程下用快照迭代代替 `ABILITYLIST_SCOPE_LOCK`。

## 快速上手

```csharp
var clock = new ManualTimeSource();
var asc = new AbilitySystemComponent { TimeSource = clock };
var actor = new MyActor(asc);              // 实现 IAbilitySystemInterface
asc.InitAbilityActorInfo(actor, actor);
asc.AddSet<MyAttributeSet>();
asc.ApplyGameplayEffectToSelf(MyGE.InitAttributes);          // Instant GE 初始化（§4.4.4）

asc.GiveAbility(new GameplayAbilitySpec(new MyAbility(), level: 1, inputID: 1));
asc.AbilityLocalInputPressed(1);

// 每帧：
clock.Advance(dt); asc.Tick(dt);

// 监听：
asc.GetGameplayAttributeValueChangeDelegate(MySet.HealthAttr).Handler += d => ...;
asc.RegisterGameplayTagEvent(MyTags.Stunned, GameplayTagEventType.NewOrRemoved, (tag, count) => ...);
```

示例工程（`GasNet.Sample`）复刻了文档 §2 的能力表：Fireball（MMC 成本 + SetByCaller 共享冷却 +
ExecCalc 伤害）、Sprint（Infinite GE + 每秒体力消耗、输入释放结束）、Jump（NonInstanced）、
Passive Armor Stacks（4 秒 1 层、上限 4、受击掉层）、CounterAttack（Event.Hit 触发），
以及眩晕、减速、叠加护甲、周期治疗等 GE 和静态/Actor 两类 Cue。

## 数据驱动（src/GasNet.Data）

对应 UE 的"GE 蓝图资产"：GameplayEffectDefinition 以 JSON 目录形式存盘，运行时由
`GasNetDataLoader.LoadCatalog(File.ReadAllText(path), options)` 加载成普通定义对象。
核心库保持零依赖——全部 JSON 管线在这个独立程序集里（netstandard2.1 + net10.0；
ns2.1 目标引用 System.Text.Json 包，net10.0 用内置实现）。读写对称：
`GasNetDataWriter.WriteCatalog(...)` 把（加载或编辑过的）定义写回同一种格式——
等于默认值的字段一律省略（干净的 diff），写入顺序即传入顺序。

```jsonc
{
  "effects": {
    "GE_Damage": {
      "durationPolicy": "Instant",                    // Instant | HasDuration | Infinite
      "duration": 5, "period": 1,                     // HasDuration/Infinite + period → 周期 GE
      "modifiers": [ {
        "attribute": "BattleAttributeSet.Health",     // "SetTypeName.AttributeName"
        "op": "Add",                                  // Add | Multiply | Divide | Override
        "magnitude": { "type": "setByCaller", "tag": "Data.Damage" }
        // 另有 scalableFloat(value,valuePerLevel)、attributeBased(attribute,source,snapshot,
        //   useBaseValue,sourceTagFilter,targetTagFilter)、customCalculation(calculation)；
        //   公共 UE 公式字段：coefficient(1)、preMultiplyAdditive(0)、postMultiplyAdditive(1)
      } ],
      "assetTags": [], "grantedTags": [], "cueTags": ["GameplayCue.Combat.Hit"],
      "applicationTagRequirements": { "require": [], "ignore": [] },
      "targetTagRequirements": {}, "ongoingTagRequirements": {},
      "removeGameplayEffectsWithTags": [], "grantedApplicationImmunityTags": [],
      "stacking": { "type": "AggregateByTarget", "limit": 4, "durationRefresh": "NeverRefresh",
                    "periodReset": "NeverReset", "expiry": "ClearEntireStack" },
      "grantedAbilities": [ { "ability": "BurnAbility", "inputId": 0,
                              "removalPolicy": "CancelAbilityImmediately" } ],
      "executions": [ { "calculation": "DamageExecution" } ],
      "customApplicationRequirements": [ { "requirement": "CanAffordMana" } ]
    }
  }
}
```

解析约定：

- **属性**以 `"SetTypeName.AttributeName"` 引用，宿主用 `options.RegisterAttributeSet<T>()`
  注册属性集类型，加载器经 `GameplayAttributeRegistry` 校验字段存在。
- **标签**直接写字符串，按核心库的运行时注册模型自动注册（见"有意偏差"）。
- **代码片段**（ExecCalc/MMC/CAR/授予能力）以类型名引用，宿主用 `options.RegisterType<T>()`
  注册——不做程序集扫描，天然兼容 IL2CPP/裁剪。ExecCalc/MMC/CAR 需要公共无参构造。
- **未知字段一律报错**（`GasNetDataException`，消息带效果名与字段路径）——数据格式的拼写错误必须大声失败；重复的效果名同理（JSON 对象键重复会静默后者覆盖前者）。
- 尚未支持从 JSON 表达：`GameplayTagQuery`（`GrantedApplicationImmunityQuery` / TagRequirements.TagQuery）。

可运行示例：[examples/GodotDemo](./examples/GodotDemo) 的 `Data/BattleGE.json`——把血量/攻击力改进 JSON
即可调参，无需重编译。

## 编辑器（tools/GasNet.Editor）

本地 Web 工具（Blazor Server，纯 .NET 无 JS 工具链），编辑上面的 JSON 目录：

```bash
dotnet run --project tools/GasNet.Editor    # → http://localhost:5177
```

- **档案**：反射加载游戏侧托管 DLL（GodotDemo 构建产物即可），自动发现 AttributeSet 与
  ExecCalc/MMC/CAR/能力类型，填出下拉框；仓库内运行会自动预填 GodotDemo 的路径。
  GasNet 系依赖强制回落到编辑器自身副本保证类型同一性；引用引擎类型（Godot Node 等）的
  类加载失败会被跳过。
- **编辑**：效果的全部数据字段（时长/周期、修饰符与四种幅度、标签、三类标签需求、免疫、
  堆叠、授予能力、ExecCalc/CAR）。
- **校验**：每次修改都"写出→真加载器读回"往返，加载器报什么编辑器就显示什么——
  游戏读不进的文件在编辑器里就存不出去（不会显示）。编辑器自身不序列化 JSON，
  读和写只走 GasNet.Data，格式变更只影响一处。

当前边界：不支持编辑 GameplayTagQuery、不能创建属性集/逻辑类（那些永远是代码）、
无撤销/重做与运行时连接。

## 接入 Unity / Godot

核心库多目标发布：**`netstandard2.1`**（Unity 2019.4+ / Unity 6 的 Mono 与 IL2CPP、Godot 4 C# 均可直接加载）
与 **`net10.0`**（桌面/服务器；本项目内 Sample/Demo/Tests 也用这个目标）。核心库不含任何引擎 API，
引擎侧的胶水（输入桥接、Cue 表现、资产导入）由接入方实现。

### Unity

1. `dotnet build src/GasNet -c Release`，把 `bin/Release/netstandard2.1/GasNet.dll` 放进 `Assets/Plugins/`
   （或用 NuGetForUnity 以包形式引入）。
2. IL2CPP 防裁剪：属性系统通过反射发现 `GameplayAttributeData` 字段，Managed Stripping Level 高时
   字段可能被裁掉。两种方式任选：
   - 给你的 AttributeSet 类打 `[UnityEngine.Scripting.Preserve]`；
   - 或在 `Assets/link.xml` 中保留本程序集与你的属性集：

     ```xml
     <linker>
       <assembly fullname="GasNet" preserve="all"/>
       <assembly fullname="Assembly-CSharp">
         <namespace fullname="YourGame" preserve="all"/>
       </assembly>
     </linker>
     ```

   若字段真被裁掉，`GameplayAttributeRegistry` 会打一条明确的警告日志（只打一次）。
3. 若使用数据驱动层，还需把 `GasNet.Data.dll`（及其 `System.Text.Json` 依赖）一并放入
   `Assets/Plugins/`，并在 link.xml 中保留 `GasNet.Data` 与 System.Text.Json 相关程序集。
4. 每帧驱动：在 `MonoBehaviour.Update()` 里调用 `asc.Tick(Time.deltaTime)`；时间源注入引擎时钟：

   ```csharp
   asc.TimeSource = new EngineTimeSource(); // 实现 ITimeSource，返回 Time.time
   GasNetLog.OnWarn = msg => Debug.LogWarning(msg);
   ```

### Godot 4 (C#)

1. 在游戏工程 `.csproj` 里 `<ProjectReference Include="...\src\GasNet\GasNet.csproj"/>` 直接引用，
   netstandard2.1 资产在 Godot 的 .NET 运行时（6/8）下原生可用；或同样以 DLL 方式放入项目。
2. 在 `_Process(double delta)` 中调用 `asc.Tick((float)delta)`；时间源可用
   `Time.GetTicksMsec() / 1000f` 实现 `ITimeSource`。
3. Cue 表现：继承 `GameplayCueNotify_Static/_Actor`，在事件回调里操作 `Node`/粒子/音频即可。

**可运行的示例**：[examples/GodotDemo](./examples/GodotDemo) —— 一个完整的 Godot 工程，
演示全部四个接缝（时间注入、Tick 驱动、节点持有 ASC、Cue 表现适配），空格攻击 + 敌人自动反击；
GE 定义由 `src/GasNet.Data` 从 `Data/BattleGE.json` 加载（数据驱动示例）。
`GasNet.Sample`、`GasNet.Demo`、`GasNet.Tests` 仍是 `net10.0`（宿主侧内容/工具，不随核心库进引擎）。
