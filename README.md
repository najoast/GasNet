# GasNet — 用 .NET 10 C# 实现的 GAS（Gameplay Ability System）

对 Unreal Engine GAS 插件（以 [GASDocumentation](https://github.com/tranek/GASDocumentation) 为规范来源）的 C# 移植。
目标不是复刻 UE 的 UObject/网络层，而是**逐条对应文档中的游戏逻辑语义**：标签层级、属性聚合数学、
GameplayEffect 生命周期、堆叠/免疫/持续标签、Ability 激活与提交、冷却/成本模式、GameplayCue 路由等。

```
GasNet.sln
├─ src/GasNet           核心库（无外部依赖，net10.0）
├─ src/GasNet.Sample    示例内容：属性集、GE 定义、能力、Cue（对应文档 §2 的示例工程）
├─ src/GasNet.Demo      可运行的战斗脚本演示（控制台 transcript）
└─ tests/GasNet.Tests   74 个 xUnit 测试，把文档语义锁进断言
```

```bash
dotnet test GasNet.sln          # 74/74 通过
dotnet run --project src/GasNet.Demo
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
- **数据驱动层省略**：无 .ini 标签表（运行时注册）、无 DataTable 曲线（`ScalableFloatMagnitude.ValuePerLevel` 代替）、无蓝图（用流式构建器/C# 子类）。
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
