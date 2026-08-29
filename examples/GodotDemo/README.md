# GasNet Godot Demo

最小 Godot 4 接入示例：一个英雄和一个敌人，全部战斗逻辑跑在 GasNet 上。

## 运行

1. 安装 **Godot 4.4+（.NET 版）**。
2. 用 Godot 打开本目录的 `project.godot`，等待首次 C# 导入/构建完成。
3. 按 `F5` 运行。**空格键** = 英雄攻击；敌人每 3 秒自动反击；有人死亡时日志面板提示 VICTORY / DEFEAT。

无需安装额外 NuGet 包：工程通过 `<ProjectReference>` 引用 `src/GasNet`，
restore 时自动选用其 **netstandard2.1** 产物（Godot 的 net8.0 消费端与 net10.0 不兼容，
多目标里最近的兼容项就是 ns2.1——这正是核心库要支持它的原因）。

## 这个示例演示的四个接缝

| 接缝 | 位置 | 说明 |
|---|---|---|
| 时间 | `GasNetBridge.cs` `GodotTimeSource` | `ITimeSource` 包一层 `Time.GetTicksMsec()`，所有 ASC 共享引擎时钟 |
| 宿主 | `GasNetBridge.cs` `GasActor` | Node 持有 ASC；`_Ready` 里 `InitAbilityActorInfo`，`_Process` 里 `ASC.Tick(dt)` |
| 内容 | `BattleContent.cs` | AttributeSet / Ability 全是普通 C# 类，零引擎依赖 |
| 表现 | `GasNetBridge.cs` `HitCueNotify` | 继承 `GameplayCueNotify_Static`，把 `GameplayCue.Combat.Hit` 事件翻译成闪白 + 日志 |

`Scripts/Main.cs` 负责场景组装：注册 Cue、把属性变化接到日志 Label、把空格键映射到
`ASC.AbilityLocalInputPressed(1)`。

## 数据驱动的 GE

三个 GE 定义不在代码里，而在 [`Data/BattleGE.json`](Data/BattleGE.json)，由 `src/GasNet.Data`
在 `BattleData.Load()` 时加载（`GasNetDataLoader`）。属性以 `"SetTypeName.AttributeName"` 引用、
标签按运行时注册模型自动注册；改 JSON 里的数值（血量/攻击力）即可调参，无需重编译 C# 代码。
字段格式见根 README 的"数据驱动"一节。

## 说明

- 本示例独立于 `GasNet.sln`（避免主解决方案构建依赖 Godot SDK）；用 Godot 编辑器或
  `dotnet build examples/GodotDemo` 单独构建。
- 本工程是 net8.0（Godot 4.4 的 C# 目标）；如你的 Godot 版本不同，改 `GodotDemo.csproj` 里的
  `TargetFramework` 与 `Godot.NET.Sdk` 版本即可。
