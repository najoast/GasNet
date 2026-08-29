# GasNet.Editor — GameplayEffect 目录编辑器

[GasNet](../../README.md) 的本地 Web 编辑器（Blazor Server，纯 .NET，无 JS 工具链）：
可视化编辑 `src/GasNet.Data` 的 JSON 效果目录（GameplayEffect 定义，格式见主 README
「数据驱动」一节），供 Godot/Unity 宿主运行时加载。

```bash
dotnet run --project tools/GasNet.Editor   # → http://localhost:5177
```

## 设计立场：编辑器不发明任何格式知识

GE 目录字段多、约束隐蔽（属性名必须已注册、代码片段必须是已注册类型、未知字段大声报错），
手写 JSON 极易拼错。编辑器的对策不是自带一套校验规则，而是把正确性外包给数据管线本身：

- **读写只经 GasNet.Data**。编辑器内没有一行 JSON 序列化代码：打开走
  `GasNetDataLoader`，保存走 `GasNetDataWriter`。格式变更只改一处，编辑器自动跟上。
- **校验 = 写出 → 真加载器读回的往返**。每次修改都全量执行（`CatalogDocument.Validate`），
  加载器报什么编辑器显示什么——游戏读不进的文件在这里就存不出去。
- **下拉框数据源来自游戏 DLL（"档案"）**。反射加载游戏侧托管程序集，发现 AttributeSet 与
  ExecCalc/MMC/CAR/能力类型；这些类型既填下拉框，也是校验时 `GasNetDataLoadOptions`
  的注册表。

## 使用流程

1. **加载档案**：填游戏托管 DLL 路径。仓库内运行会自动预填 GodotDemo 构建产物
   （`examples/GodotDemo/.godot/mono/temp/bin/Debug/GodotDemo.dll`）与目录文件
   （`examples/GodotDemo/Data/BattleGE.json`）。
2. **打开目录**：效果列表按 JSON 出现顺序显示，左侧可增删效果、查看已注册标签树。
3. **编辑**：右侧改字段，顶部校验横幅实时显示往返结果，未保存时列表区有脏标记。
4. **保存**：写回原路径。

界面分区：左栏 = 档案 / 目录文件 / 标签树；右栏 = 校验横幅 / 效果编辑器 / GasNetLog
日志面板（核心库的 Warn/Error 进这里，控制台同时保留）。支持明暗主题切换。

## 代码地图

| 文件 | 职责 |
|---|---|
| `Program.cs` | Blazor Server 启动；`GasNetLog.OnWarn/OnError` 接入日志面板 |
| `CatalogDocument.cs` | 编辑中的文档：有序效果列表 + 脏标记 + Open/Save/Validate（往返校验在此） |
| `EditorProfile.cs` | 档案：反射加载游戏 DLL，发现属性集与可引用类型；构建 `GasNetDataLoadOptions` |
| `Ui.cs` | 标签文本 ↔ 容器；InvariantCulture 的数字/枚举解析 |
| `EditorLogBuffer.cs` | 线程安全日志环形缓冲（60 行） |
| `Components/Pages/Editor.razor` | 主页面：三步流程、效果列表、标签树、校验横幅、日志；所有编辑汇入 `Refresh()` |
| `Components/EffectEditor.razor` | 单个效果全部字段：基本 / 修饰符 / 执行计算与自定义应用条件 / 标签 / 标签需求 / 驱散与免疫 / 堆叠 / 授予能力 |
| `Components/MagnitudeEditor.razor` | 四种修饰符幅度（scalableFloat / attributeBased / setByCaller / customCalculation） |
| `Components/ThemeToggle.razor` | 明暗主题切换 |

## 硬性规则（改代码前必读）

1. **不得在编辑器里写任何 JSON 序列化/字段级校验代码**。新字段的读写只经 `GasNet.Data`；
   那边还不支持就先改那边。想加校验规则，加到 `GasNetDataLoader`——手写在编辑器里
   必然与加载器漂移。
2. **类型同一性**：`EditorProfile` 的 `AssemblyLoadContext.Resolving` 对 `GasNet` /
   `GasNet.Data` / `System.Text.Json` 返回 null（回落编辑器自身副本）。游戏目录若自带
   GasNet.dll 副本，直接加载会产生两套不相等的 `Type`，所有 `IsAssignableFrom` 判定失效。
   动加载逻辑时保持这一点。
3. **数字解析一律 InvariantCulture**（`Ui.F` / `Ui.I` / `Ui.Enum`），别用依赖当前
   区域设置的 `float.Parse`。
4. **所有编辑事件汇入 `Editor.razor` 的 `Refresh()`**（标脏 + 往返校验 + 标签树更新），
   新 UI 控件不要各自刷新。
5. **档案加载不用 collectible ALC**：旧档案的 Type 可能仍被已打开目录里的定义引用；
   重复加载让旧上下文闲置即可，这是有意的开发工具取舍。

## 常见任务

**给 GE 定义加一个新数据字段**：

1. `src/GasNet`：`GameplayEffectDefinition` 加属性（若尚无）。
2. `src/GasNet.Data`：`GasNetDataLoader` 读取 + `GasNetDataWriter` 写出 + 往返测试；
   注意未知字段报错路径不受影响。
3. `Components/EffectEditor.razor`：加一段 UI，编辑事件回调 `Changed`（汇入 `Refresh()`）。

**加一类可引用的代码类型**（下拉框支持新的 calculation 基类）：
`EditorProfile.LoadAssembly` 的类型分派 → `BuildOptions` 的注册循环 →
`EffectEditor.razor` 的下拉框。

## 有意边界（不要"顺手"实现）

- 不编辑 `GameplayTagQuery`（`GasNet.Data` 尚未支持从 JSON 表达，主 README「数据驱动」
  一节有记录）。
- 不创建属性集 / ExecCalc / MMC / CAR / 能力——那些永远是代码，编辑器只引用。
- 无撤销/重做、无与运行中游戏的连接、无多文档同时打开。
- 标签不做预注册校验：核心库是运行时注册模型，JSON 里的新标签首用即自动注册；
  左侧标签树只是"当前已注册标签"的展示，不是校验白名单。
- 引用宿主引擎类型（Godot `Node`、Unity 组件）的类无法反射加载，档案加载时按
  `ReflectionTypeLoadException` 跳过并记日志，这是预期行为不是 bug。
