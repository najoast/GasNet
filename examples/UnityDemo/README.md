# GasNet Unity Demo

最小 Unity 接入示例：一个英雄和一个敌人，全部战斗逻辑跑在 GasNet 上。
与 [GodotDemo](../GodotDemo) 一一对应——同样的四个接缝、同一份 GE 目录 JSON。

## 运行

1. 安装 **Unity 6 LTS**（`ProjectSettings/ProjectVersion.txt` 钉在 6000.0.x；你的补丁版本不同时，
   Unity Hub 会提示升级/降级工程，确认即可）。
2. 用 Unity Hub 打开本目录（`examples/UnityDemo`），等待首次导入（会生成 Library）。
3. 打开 `Assets/Demo/Main.unity`，按 ▶ Play。**空格键** = 英雄攻击；敌人每 3 秒自动反击；
   有人死亡时画面提示 VICTORY / DEFEAT，日志同步打进 Console（`[Cue]` 行来自 GameplayCue）。

无需 NuGet、无需装包：`Assets/Plugins/` 里提交了预构建的 **netstandard2.1** GasNet DLL——
这正是核心库要支持 ns2.1 的原因（Unity 的 Mono/IL2CPP 运行时可直接加载）。

## 重新生成 DLL

改了 `src/` 里的核心代码后运行（publish 会传递构建并带上 System.Text.Json 依赖闭包）：

```powershell
powershell -ExecutionPolicy Bypass -File setup.ps1
```

## 这个示例演示的四个接缝

| 接缝 | 位置 | 说明 |
|---|---|---|
| 时间 | `GasActor.cs` `UnityTimeSource` | `ITimeSource` 包一层 `Time.time`，所有 ASC 共享引擎时钟 |
| 宿主 | `GasActor.cs` `GasActor` | MonoBehaviour 持有 ASC；`Start` 里 `InitAbilityActorInfo`，`Update` 里 `ASC.Tick(Time.deltaTime)` |
| 内容 | `BattleContent.cs` | AttributeSet / Ability 全是普通 C# 类，零引擎依赖 |
| 表现 | `GasActor.cs` `HitCueNotify` | 继承 `GameplayCueNotify_Static`，把 `GameplayCue.Combat.Hit` 事件翻译成受击闪白 + Console 日志 |

`Scripts/Main.cs` 负责场景组装：注册 Cue、把属性变化接到屏上日志面板、把空格键映射到
`ASC.AbilityLocalInputPressed(1)`；敌人自动反击走的也是输入路径（能力绑定 `inputID: 1`）。
单位 GameObject 与圆形 sprite 全部运行时生成——工程里没有任何美术资产。

注意 Unity 的脚本文件名规则：MonoBehaviour 子类必须放在同名文件里（`GasActor.cs`/`Hero.cs`/
`Enemy.cs`/`Main.cs`），非组件类（`BattleContent.cs`、`HitCueNotify` 等）则不受限。

## 数据驱动的 GE

三个 GE 定义不在代码里，而在 [`Assets/StreamingAssets/Data/BattleGE.json`](Assets/StreamingAssets/Data/BattleGE.json)，
由 `src/GasNet.Data` 在 `BattleData.Load()` 时经 `GasNetDataLoader` 加载（内容与 GodotDemo 的
`Data/BattleGE.json` 相同）。改 JSON 里的数值（血量/攻击力）即可调参，无需重编译 C# 代码。
字段格式见根 README 的"数据驱动"一节。

## IL2CPP / 托管代码裁剪

工程自带 `Assets/link.xml`：保留 `GasNet` / `GasNet.Data` / `System.Text.Json` 整程序集，
以及 `Assembly-CSharp` 的 `UnityDemo` 命名空间（属性反射依赖 `GameplayAttributeData` 字段名）。
详见根 README"接入 Unity"。

## 说明

- 本示例独立于 `GasNet.sln`；Unity 会为整个工程生成自己的 `.csproj`/`.sln`（已在 `.gitignore` 忽略）。
- 演示脚本按 Unity 6 的编译器能力书写：C# 9（块命名空间、显式 usings）、逐文件 `#nullable enable`。
- 仓库内没有 Unity 环境，脚本按"UnityEngine API 桩 + C# 9 + 真实 GasNet DLL"离线编译验证过，
  并用桩 headless 跑通了完整战斗流（JSON 加载 → 攻击/反击 → 血量数值断言）。
