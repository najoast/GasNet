# UseCases

`GasNet.UseCases` 是一个独立的 console 项目（`net10.0`），用一组**自包含的场景**演示 GasNet 暴露的所有主要能力。每个场景在自己的 `SimWorld` 里跑，自己造角色、装能力、tick 固定时钟——所以既可以当作"用例文档"读，又可以当作"实战压力测试"跑。

它的目标不是替代 `tests/GasNet.Tests` 的单元断言，而是补一个维度：**把多个特性组合起来跑出可读输出**——能力任务串联、Tag 触发的取消、宿主事件做目标、数据驱动的法术配置、网络桥接的 DTO 流。这种"组合场景"在单元测试里很难写得直观。

## 运行

```bash
cd examples/UseCases
dotnet run
```

按需过滤：

```bash
dotnet run -- Channel           # 只跑 ChannelAndInterrupt
dotnet run -- Bridge            # 只跑 NetworkBridgeSimulation
```

退出码 = 失败场景数（0 = 全通过）。运行时会向 `stderr` 写数据加载期的 `[GasNet][Warn]`（例如纯授 tag 的 cooldown GE），不影响退出码——`dotnet run` 在 PowerShell 之外识别为非错误。

## 用例矩阵

| # | 场景 | 演示什么 | 关键能力 |
|---|------|----------|----------|
| 1 | `InputPatterns` | tap / hold / combo 三种输入节奏 | `AbilityTask_WaitInput` + 链式 `WaitDelay` + NonInstanced 不能 spawn 任务 |
| 2 | `ChannelAndInterrupt` | 引导法术 + TagAdded 触发取消 | `CancelAbilitiesWithTag` + `WaitDelay` 的 `onCancelled` 回调 |
| 3 | `TargetingViaHostEvent` | 等宿主返回目标数据 | `WaitGameplayEvent` 配合宿主 `SendGameplayEvent`——`WaitTargetData` 的最小替代 |
| 4 | `DataDrivenCombat` | 全部数值配置走 JSON，能力类只连线 | `GasNetDataCatalog` + Cooldown/Stun/Burn 的 `HasDuration` + `TryActivateAbility` 返回值 |
| 5 | `NetworkBridgeSimulation` | `docs/networking.md` 的 server-only 架构在代码里 | `OnAttributeChanged`/`OnTagAdded`/`OnGameplayEffectApplied`/`OnAbilityActivated` → DTO |

## 项目结构

```
examples/UseCases/
├── UseCases.csproj          # 引用 GasNet + GasNet.Data, 拷贝 Data/** 到输出
├── Program.cs               # 5 个场景注册入口
├── UseCaseRunner.cs         # 通用 runner（带名字过滤 + 失败计数）
├── Host.cs                  # SimWorld + Fighter + BattleAttributeSet + Tags
├── SharedAbilities.cs       # 8 个跨场景复用的 GameplayAbility 子类
├── Data/
│   └── Catalog.json         # 8 个 GameplayEffect 定义（Cost/Cooldown/Damage/Burn/Heal/...）
└── Scenarios/
    ├── InputPatterns.cs
    ├── ChannelAndInterrupt.cs
    ├── TargetingViaHostEvent.cs
    ├── DataDrivenCombat.cs
    └── NetworkBridgeSimulation.cs
```

`Host` 是"一个真实游戏会写的东西"的最小样本：

- `SimWorld` 提供固定步长的 `ManualTimeSource`——每个 fighter ASC 都引用同一个 clock，输出确定。
- `BattleAttributeSet` 用 meta-attribute 模式（`MetaDamage` 接收伤害，PostGameplayEffectExecute 把它扣到 `Health`）。
- `Fighter` 实现 `IAbilitySystemInterface`，`Fighter.Health` / `Fighter.Mana` 走 `GetNumericAttribute`，所以场景脚本读到的就是当前生效值。

## 作为回归测试

CI 跑它当作 smoke test（参见 `.github/workflows/ci.yml` 的 `examples` job）。本地修改了任何 `AbilityTask*` / `ActiveGameplayEffects` / `AbilitySystemComponent.*` 后：

```bash
cd examples/UseCases
dotnet run 2>&1 | findstr /C:"finished" /C:"FAILED"
```

如果行数从 `=== all 5 use case(s) finished ===` 变了，那条变更就要再看一眼。

## 已知 noise

`Data/Catalog.json` 里 `FireballCooldown` 和 `Stun` 是纯授 tag 的 GE（没有 modifier 没有 execution），加载时会触发一次 `[GasNet][Warn] effect 'X' has no modifiers and no executions; it will change nothing.`——这是 loader 的提示，不是错误。语义上这是合法用法（UE 里大量 cooldown GE 就这样），仅保留为 warning。

## 跟单元测试的关系

| 维度 | `tests/GasNet.Tests` | `examples/UseCases` |
|------|----------------------|---------------------|
| 粒度 | 单个 API / 一条代码路径 | 多个 API 串联成一个可读场景 |
| 输出 | xUnit pass/fail | 带注释的 console 文本 |
| 时钟 | `ManualTimeSource` 控制 | 同上，但脚本更直观 |
| 适合 | CI 必跑 | 文档 + smoke + 现场调试 |

两者互补，不互相替代。
