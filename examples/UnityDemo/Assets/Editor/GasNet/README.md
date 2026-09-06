# GasNet Unity EditorWindow

GE + Cue 目录编辑器的 Unity 集成入口。读写只经过 `GasNet.Data`，编辑器自身不写 JSON 序列化。

## 打开

菜单 `GasNet → Open Editor`，或 `Window → GasNet Editor`。

## 首次使用

打开后窗口会自动预填三个路径：

- **游戏程序集**：`Library/ScriptAssemblies/Assembly-CSharp.dll`（你的游戏代码编译产物）
- **GE 目录**：`Assets/StreamingAssets/Data/BattleGE.json`
- **Cue 目录**：`Assets/StreamingAssets/Data/BattleCue.json`

如果 `Assembly-CSharp.dll` 已经存在，窗口会**自动加载**档案并尝试打开两份 JSON。失败的话：

1. 检查左栏第 1 区的「档案」路径，必要时用 "..." 或手动改成其它 DLL（如某个 asmdef 的输出）。
2. 点击「加载」。
3. 改完路径后用第 2 区的「打开」按钮读取 JSON。

## 编辑流程

- 左栏选条目 → 右栏编辑 → 「● 未保存」变红。
- 「保存」写回原路径；写之前会跑一次往返校验（写出 → 真加载器读回）。
- 「撤销/重做」走文档内建的栈（不是 Unity 的 Undo 系统）；与 Blazor 编辑器共享同一份 `CatalogDocument` / `CueDocument`。
- 「JSON 预览」是只读的折叠面板，等同于当前内容保存到磁盘时的形态——预览 = 落盘。

## 已知边界

- **没有 GameplayTagQuery 编辑器**：只支持 `GameplayTagContainer` 的纯文本编辑（空格/逗号/分号分隔）。
- **没有主题/搜索/反查**：IMGUI "bare editor"，不是 Blazor UI 的功能齐全版。
- **不支持同时打开多文件**：每个 tab（GE / Cue）只挂一个路径。
- **撤销栈不跨文档**：GE 与 Cue 各自独立。
- **没有 diff/preview**：保存前只会跑"写出→读回"校验，不会做语义 diff。

## 构建前提

`GasNet.Editor.Core.dll` 必须已复制到 `Assets/Plugins/`，否则 `Assembly-CSharp-Editor` 找不到 `GasNet.Editor.Core` 类型、EditorWindow 打开就报 `TypeLoadException`。

复制的两种方式：

1. **跑 `setup.ps1`**：脚本里有一段 `Copy-Item` 会把 `tools\GasNet.Editor.Core\bin\Debug\net10.0\GasNet.Editor.Core.dll` 拷到 `Assets\Plugins\GasNet.Editor.Core.dll`。最省心。
2. **手动**：

   ```powershell
   dotnet build tools\GasNet.Editor.Core\GasNet.Editor.Core.csproj
   Copy-Item tools\GasNet.Editor.Core\bin\Debug\net10.0\GasNet.Editor.Core.dll `
             examples\UnityDemo\Assets\Plugins\GasNet.Editor.Core.dll -Force
   ```

复制后第一次打开 Unity，Editor 会自动生成 `GasNet.Editor.Core.dll.meta`（持久 GUID）—— 不要手动写。

## 文件清单

```
Assets/Editor/GasNet/
  GasNet.Editor.asmdef          includePlatforms=Editor；引用 GasNet / GasNet.Data / GasNet.Editor.Core
  GasNetEditorMenu.cs           [MenuItem("GasNet/Open Editor")]
  GasNetEditorWindow.cs         主窗口：顶栏 / 标签 / 左栏（档案 + 目录 + 标签树 + 引用查询） / 右栏（校验 + 详情） / 底栏（日志）
  EffectEditorView.cs           单个 GameplayEffectDefinition 的 9 块面板
  CueEditorView.cs              单个 CueDefinition 的表单 + 反射出 action 的 scalar 属性
  MagnitudeEditorView.cs        4 种 magnitude + shaping 系数的子表单
```

## 数据流（与 Blazor 编辑器共用一份 Core）

```
┌──────────────────────┐         ┌──────────────────────┐
│  GasNetEditorWindow  │  ───→   │  GasNet.Editor.Core  │
│  (Unity IMGUI)       │         │  (纯 C# 库)          │
└──────────────────────┘         │                      │
                                 │  CatalogDocument     │
┌──────────────────────┐  ───→   │  CueDocument         │
│  Editor.razor        │         │  EditorProfile       │
│  (Blazor Server)     │         │  CatalogSearch       │
└──────────────────────┘         │  EditorLogBuffer     │
                                 │  Ui                  │
                                 └─────────┬────────────┘
                                           │
                                           ▼
                                 ┌──────────────────────┐
                                 │  GasNet.Data         │
                                 │  Loader + Writer     │
                                 └──────────────────────┘
```

Blazor 编辑器改 schema / 校验 / 撤销语义时，Unity EditorWindow 自动同步——Core 是单一数据源。
