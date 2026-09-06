using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GasNet;
using GasNet.Data;
using UnityEditor;
using UnityEngine;

namespace GasNet.Editor.Unity;

/// <summary>GE + Cue 目录编辑器的 IMGUI 主窗口。镜像 Blazor 的 Editor.razor 顶栏 + 标签页 +
/// 左栏（档案 / 目录文件 / 标签树 / 引用查询）+ 右栏（校验 + 详情）+ 底栏（日志）的布局。
/// 所有读写经 <see cref="CatalogDocument"/>/<see cref="CueDocument"/> → <c>GasNet.Data</c>，
/// 本窗口自身不写 JSON 序列化。</summary>
public sealed class GasNetEditorWindow : EditorWindow
{
    private readonly EditorProfile _profile = new();
    private readonly CatalogDocument _geDoc = new();
    private readonly CueDocument _cueDoc = new();
    private readonly EditorLogBuffer _log = new();

    private string _activeTab = "ge"; // "ge" | "cue"
    private string _assemblyPath = "";
    private string _filter = "";
    private string _newEntryName = "";
    private string _refMode = "tag"; // "tag" | "attribute"
    private string _refQuery = "";
    private List<string> _errors = new();
    private List<string> _newTags = new();

    private Vector2 _listScroll, _detailScroll, _logScroll, _tagTreeScroll, _refScroll;

    public static void ShowWindow()
    {
        var window = GetWindow<GasNetEditorWindow>("GasNet Editor");
        window.minSize = new Vector2(960, 600);
        window.Show();
    }

    private void OnEnable()
    {
        // 自动预填：游戏程序集用 Library/ScriptAssemblies/Assembly-CSharp.dll，目录 JSON 用 StreamingAssets/Data/。
        try
        {
            _assemblyPath = Path.GetFullPath("Library/ScriptAssemblies/Assembly-CSharp.dll");
            _geDoc.Path  = Path.Combine(Application.streamingAssetsPath, "Data", "BattleGE.json");
            _cueDoc.Path = Path.Combine(Application.streamingAssetsPath, "Data", "BattleCue.json");
        }
        catch { /* 极端环境下 Application.streamingAssetsPath 不可用——保持空，让用户手动输入 */ }

        // 把 Core 的日志管道接到 Unity 控制台
        GasNetLog.OnInfo  += line => _log.Info(line);
        GasNetLog.OnWarn  += line => _log.Warn(line);
        GasNetLog.OnError += line => _log.Error(line);

        // 试着加载已存在的 Assembly-CSharp（用户已打开过 Editor 的话就走完整预览）
        if (!string.IsNullOrEmpty(_assemblyPath) && File.Exists(_assemblyPath))
        {
            try
            {
                _profile.LoadAssembly(_assemblyPath);
                Refresh(trackUndo: false);
            }
            catch (Exception e) { _log.Warn($"自动加载档案失败：{e.Message}"); }
        }
    }

    private void OnDisable()
    {
        if (_geDoc.Dirty || _cueDoc.Dirty)
        {
            var proceed = EditorUtility.DisplayDialog("GasNet Editor",
                "有未保存的修改，关闭窗口将丢失。是否继续？", "关闭", "取消");
            if (!proceed) return; // 实际上 EditorWindow 在 OnDisable 后还会被销毁——这里只是给用户一个知会。
        }
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawTabs();
        EditorGUILayout.BeginHorizontal();
        DrawSidebar();
        DrawMain();
        EditorGUILayout.EndHorizontal();
        DrawLog();
    }

    // ===================================================================
    // 顶栏 / 标签页
    // ===================================================================
    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("GasNet Editor", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("GE + Cue 目录编辑器 · 读写只经 GasNet.Data", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        if (_geDoc.Dirty || _cueDoc.Dirty)
            EditorGUILayout.LabelField("● 未保存", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTabs()
    {
        EditorGUILayout.BeginHorizontal();
        var geStyle  = _activeTab == "ge"  ? EditorStyles.toolbarButton : EditorStyles.miniButton;
        var cueStyle = _activeTab == "cue" ? EditorStyles.toolbarButton : EditorStyles.miniButton;
        if (GUILayout.Button("GameplayEffect 目录", geStyle, GUILayout.Width(160))) _activeTab = "ge";
        if (GUILayout.Button("GameplayCue 目录",     cueStyle, GUILayout.Width(160))) _activeTab = "cue";
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    // ===================================================================
    // 左栏
    // ===================================================================
    private void DrawSidebar()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(320));
        DrawSection1Profile();
        if (_activeTab == "ge") DrawSection2GE();
        else                    DrawSection2Cue();
        DrawTagTree();
        DrawReferenceLookup();
        EditorGUILayout.EndVertical();
    }

    private void DrawSection1Profile()
    {
        EditorGUILayout.LabelField("1 · 游戏程序集（档案）", EditorStyles.boldLabel);
        _assemblyPath = EditorGUILayout.TextField(_assemblyPath);
        if (GUILayout.Button("加载")) LoadProfile();
        if (_profile.HasProfile)
        {
            EditorGUILayout.LabelField($"✔ {_profile.AttributeSetCount} 个属性集 · {_profile.CalculableCount} 个可引用类型（含 {_profile.CueActions.Count} 个 cue 动作）", EditorStyles.miniLabel);
            if (_profile.IsStale())
                EditorGUILayout.HelpBox("⚠ 档案文件已变更（游戏重新编译过）——下拉框与校验依据可能过期，请重新加载。", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox("反射加载游戏 DLL，自动发现属性集与 ExecCalc/MMC/CAR/能力/CueAction 类型。", MessageType.Info);
        }
        foreach (var line in _profile.Log)
            EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
    }

    private void DrawSection2GE()
    {
        EditorGUILayout.LabelField("2 · 目录文件", EditorStyles.boldLabel);
        _geDoc.Path = EditorGUILayout.TextField(_geDoc.Path);
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(_geDoc.Path.Length == 0 || !_profile.HasProfile))
            if (GUILayout.Button("打开")) OpenCatalog();
        using (new EditorGUI.DisabledScope(_geDoc.Path.Length == 0 || _geDoc.Effects.Count == 0))
            if (GUILayout.Button("保存")) SaveCatalog();
        using (new EditorGUI.DisabledScope(!_geDoc.CanUndo))
            if (GUILayout.Button("↩ 撤销")) { _geDoc.Undo(); Refresh(trackUndo: false); }
        using (new EditorGUI.DisabledScope(!_geDoc.CanRedo))
            if (GUILayout.Button("↪ 重做")) { _geDoc.Redo(); Refresh(trackUndo: false); }
        EditorGUILayout.EndHorizontal();
        if (_geDoc.Dirty) EditorGUILayout.LabelField("● 未保存", EditorStyles.miniLabel);

        _filter = EditorGUILayout.TextField(_filter);

        _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(260));
        for (var i = 0; i < _geDoc.Effects.Count; i++)
        {
            if (!string.IsNullOrEmpty(_filter) && _geDoc.Effects[i].Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var prev = GUI.backgroundColor;
            if (i == _geDoc.SelectedIndex) GUI.backgroundColor = new Color(0.5f, 0.7f, 1f);
            if (GUILayout.Button(_geDoc.Effects[i].Name, EditorStyles.miniButton)) _geDoc.SelectedIndex = i;
            GUI.backgroundColor = prev;
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        _newEntryName = EditorGUILayout.TextField(_newEntryName);
        if (GUILayout.Button("＋", GUILayout.Width(28))) AddEntry();
        using (new EditorGUI.DisabledScope(_geDoc.Selected is null))
        {
            if (GUILayout.Button("⧉", GUILayout.Width(28))) CloneSelected();
            if (GUILayout.Button("✕", GUILayout.Width(28))) DeleteSelected();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSection2Cue()
    {
        EditorGUILayout.LabelField("2 · 目录文件", EditorStyles.boldLabel);
        _cueDoc.Path = EditorGUILayout.TextField(_cueDoc.Path);
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(_cueDoc.Path.Length == 0 || !_profile.HasProfile))
            if (GUILayout.Button("打开")) OpenCueCatalog();
        using (new EditorGUI.DisabledScope(_cueDoc.Path.Length == 0 || _cueDoc.Cues.Count == 0))
            if (GUILayout.Button("保存")) SaveCueCatalog();
        using (new EditorGUI.DisabledScope(!_cueDoc.CanUndo))
            if (GUILayout.Button("↩ 撤销")) { _cueDoc.Undo(); Refresh(trackUndo: false); }
        using (new EditorGUI.DisabledScope(!_cueDoc.CanRedo))
            if (GUILayout.Button("↪ 重做")) { _cueDoc.Redo(); Refresh(trackUndo: false); }
        EditorGUILayout.EndHorizontal();
        if (_cueDoc.Dirty) EditorGUILayout.LabelField("● 未保存", EditorStyles.miniLabel);

        _filter = EditorGUILayout.TextField(_filter);

        _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(260));
        for (var i = 0; i < _cueDoc.Cues.Count; i++)
        {
            if (!string.IsNullOrEmpty(_filter) && _cueDoc.Cues[i].Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var prev = GUI.backgroundColor;
            if (i == _cueDoc.SelectedIndex) GUI.backgroundColor = new Color(0.5f, 0.7f, 1f);
            if (GUILayout.Button(_cueDoc.Cues[i].Name, EditorStyles.miniButton)) _cueDoc.SelectedIndex = i;
            GUI.backgroundColor = prev;
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        _newEntryName = EditorGUILayout.TextField(_newEntryName);
        if (GUILayout.Button("＋", GUILayout.Width(28))) AddCue();
        using (new EditorGUI.DisabledScope(_cueDoc.Selected is null))
        {
            if (GUILayout.Button("⧉", GUILayout.Width(28))) CloneCue();
            if (GUILayout.Button("✕", GUILayout.Width(28))) DeleteCue();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTagTree()
    {
        EditorGUILayout.LabelField("标签树（已注册）", EditorStyles.boldLabel);
        _tagTreeScroll = EditorGUILayout.BeginScrollView(_tagTreeScroll, GUILayout.MinHeight(60), GUILayout.MaxHeight(160));
        foreach (var name in GameplayTagsManager.Default.GetAllTags().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal))
        {
            var depth = name.Count(c => c == '.');
            EditorGUILayout.LabelField(new GUIContent(name, name), EditorStyles.miniLabel,
                GUILayout.Width(180 + depth * 12));
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawReferenceLookup()
    {
        EditorGUILayout.LabelField("引用查询", EditorStyles.boldLabel);
        var mode = _refMode == "tag" ? "按标签" : "按属性";
        var picked = EditorGUILayout.Popup("Mode", _refMode == "tag" ? 0 : 1, new[] { "按标签", "按属性" });
        _refMode = picked == 0 ? "tag" : "attribute";
        _refQuery = EditorGUILayout.TextField(_refQuery);

        _refScroll = EditorGUILayout.BeginScrollView(_refScroll, GUILayout.MinHeight(60), GUILayout.MaxHeight(160));
        if (_activeTab == "ge")
        {
            var hits = _refMode == "tag"
                ? SafeFind(() => CatalogSearch.FindTagReferences(_geDoc.Effects, _refQuery))
                : SafeFind(() => CatalogSearch.FindAttributeReferences(_geDoc.Effects, _refQuery));
            foreach (var h in hits)
            {
                if (GUILayout.Button($"{h.Effect}  ·  {h.Field}", EditorStyles.miniButton))
                {
                    var idx = _geDoc.Effects.FindIndex(e => e.Name == h.Effect);
                    if (idx >= 0) _geDoc.SelectedIndex = idx;
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private static List<T> SafeFind<T>(Func<List<T>> f)
    {
        try { return f(); } catch { return new(); }
    }

    // ===================================================================
    // 右栏（详情）
    // ===================================================================
    private void DrawMain()
    {
        EditorGUILayout.BeginVertical();
        DrawValidation();
        _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
        if (_activeTab == "ge")
        {
            if (_geDoc.Selected is { } entry) EffectEditorView.Render(entry, _profile, () => Refresh(trackUndo: true));
            else EditorGUILayout.HelpBox("左侧选择或新建一个效果。", MessageType.Info);
        }
        else
        {
            if (_cueDoc.Selected is { } entry) CueEditorView.Render(entry, _profile, () => Refresh(trackUndo: true));
            else EditorGUILayout.HelpBox("左侧选择或新建一个 cue。", MessageType.Info);
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawValidation()
    {
        if (!_profile.HasProfile)
        {
            EditorGUILayout.HelpBox("未加载游戏程序集——无法往返校验，先完成第 1 步。", MessageType.Info);
            return;
        }
        if (_errors.Count == 0)
        {
            var msg = _activeTab == "ge"
                ? $"✔ 目录往返校验通过（{_geDoc.Effects.Count} 个效果）"
                : $"✔ 目录往返校验通过（{_cueDoc.Cues.Count} 个 cue）";
            EditorGUILayout.HelpBox(msg, MessageType.Info);
        }
        else
        {
            foreach (var e in _errors)
                EditorGUILayout.HelpBox(e, MessageType.Error);
        }
        if (_newTags.Count > 0)
            EditorGUILayout.HelpBox("⚠ 新标签（原文件里没有，确认不是拼写错误；GAS 会静默注册任何新标签）：" + string.Join("、", _newTags), MessageType.Warning);
    }

    // ===================================================================
    // 底栏（日志）
    // ===================================================================
    private void DrawLog()
    {
        EditorGUILayout.LabelField("日志（GasNetLog）", EditorStyles.boldLabel);
        _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.MinHeight(60), GUILayout.MaxHeight(100));
        var prev = GUI.skin.label.fontStyle;
        GUI.skin.label.fontStyle = FontStyle.Normal;
        foreach (var line in _log.Snapshot())
            EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
        GUI.skin.label.fontStyle = prev;
        EditorGUILayout.EndScrollView();
    }

    // ===================================================================
    // 操作
    // ===================================================================
    private void LoadProfile()
    {
        try { _profile.LoadAssembly(_assemblyPath); }
        catch (Exception e) { _log.Warn($"档案加载失败：{e.Message}"); }
        Refresh(trackUndo: false);
    }

    private void OpenCatalog()
    {
        if (_geDoc.Dirty && !EditorUtility.DisplayDialog("GasNet Editor", "有未保存的修改，放弃并打开新目录？", "放弃", "取消")) return;
        try
        {
            _geDoc.Open(_geDoc.Path, _profile.BuildOptions());
            _errors.Clear();
        }
        catch (Exception e) { _log.Error($"打开失败：{e.Message}"); }
        Refresh(trackUndo: false);
    }

    private void SaveCatalog()
    {
        if (_errors.Count > 0 && !EditorUtility.DisplayDialog("GasNet Editor",
                $"目录有 {_errors.Count} 个校验错误，仍要保存？", "仍然保存", "取消")) return;
        try { _geDoc.Save(_geDoc.Path, _profile.BuildOptions()); _errors.Clear(); }
        catch (Exception e) { _log.Error($"保存失败：{e.Message}"); }
        Refresh(trackUndo: false);
    }

    private void OpenCueCatalog()
    {
        if (_cueDoc.Dirty && !EditorUtility.DisplayDialog("GasNet Editor", "有未保存的修改，放弃并打开新目录？", "放弃", "取消")) return;
        try
        {
            _cueDoc.Open(_cueDoc.Path, _profile.BuildCueOptions());
            _errors.Clear();
        }
        catch (Exception e) { _log.Error($"打开失败：{e.Message}"); }
        Refresh(trackUndo: false);
    }

    private void SaveCueCatalog()
    {
        if (_errors.Count > 0 && !EditorUtility.DisplayDialog("GasNet Editor",
                $"目录有 {_errors.Count} 个校验错误，仍要保存？", "仍然保存", "取消")) return;
        try { _cueDoc.Save(_cueDoc.Path, _profile.BuildCueOptions()); _errors.Clear(); }
        catch (Exception e) { _log.Error($"保存失败：{e.Message}"); }
        Refresh(trackUndo: false);
    }

    private void AddEntry()
    {
        var name = string.IsNullOrWhiteSpace(_newEntryName) ? "NewEffect" : _newEntryName.Trim();
        _geDoc.AddNew(name); _newEntryName = "";
        Refresh(trackUndo: true);
    }

    private void AddCue()
    {
        var name = string.IsNullOrWhiteSpace(_newEntryName) ? "NewCue" : _newEntryName.Trim();
        _cueDoc.AddNew(name); _newEntryName = "";
        Refresh(trackUndo: true);
    }

    private void CloneSelected()
    {
        if (_geDoc.CloneSelected() is null) return;
        Refresh(trackUndo: true);
    }

    private void CloneCue()
    {
        if (_cueDoc.CloneSelected() is null) return;
        Refresh(trackUndo: true);
    }

    private void DeleteSelected()
    {
        if (_geDoc.SelectedIndex < 0) return;
        _geDoc.RemoveAt(_geDoc.SelectedIndex);
        Refresh(trackUndo: true);
    }

    private void DeleteCue()
    {
        if (_cueDoc.SelectedIndex < 0) return;
        _cueDoc.RemoveAt(_cueDoc.SelectedIndex);
        Refresh(trackUndo: true);
    }

    // ===================================================================
    // Refresh：算校验 + 算新标签 + 触发 UI 重绘
    // ===================================================================
    private void Refresh(bool trackUndo)
    {
        if (_activeTab == "ge")
        {
            if (trackUndo) _geDoc.CommitSnapshot();
            _geDoc.MarkDirty();
            _errors = _profile.HasProfile ? _geDoc.Validate(_profile.BuildOptions())
                                           : new List<string> { "未加载游戏程序集——无法往返校验。" };
            _newTags = CatalogSearch.AllTagOccurrences(_geDoc.Effects)
                .Select(o => o.Tag.Name)
                .Where(n => !_geDoc.FileTagNames.Contains(n))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }
        else
        {
            if (trackUndo) _cueDoc.CommitSnapshot();
            _cueDoc.MarkDirty();
            _errors = _profile.HasProfile ? _cueDoc.Validate(_profile.BuildCueOptions())
                                           : new List<string> { "未加载游戏程序集——无法往返校验。" };
            _newTags = _cueDoc.Cues
                .Where(c => c.Def.Tag.IsValid)
                .Select(c => c.Def.Tag.Name)
                .Where(n => !_cueDoc.FileTagNames.Contains(n))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }
        Repaint();
    }
}
