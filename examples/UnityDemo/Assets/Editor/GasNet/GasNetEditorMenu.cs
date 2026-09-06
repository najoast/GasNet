using UnityEditor;

namespace GasNet.Editor.Unity;

/// <summary>"GasNet → Open Editor" 菜单项：拉起 GE + Cue 目录编辑器。
/// 该 EditorWindow 复用 GasNet.Editor.Core 的纯 C# 文档/反射层，单一数据源；与 Blazor
/// 编辑器同一份 CatalogDocument / CueDocument / EditorProfile。</summary>
internal static class GasNetEditorMenu
{
    [MenuItem("GasNet/Open Editor")]
    public static void Open() => GasNetEditorWindow.ShowWindow();
}
