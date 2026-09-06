using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GasNet;
using GasNet.Data;
using UnityEditor;
using UnityEngine;

namespace GasNet.Editor.Unity;

/// <summary>单个 <see cref="CueDefinition"/> 的 IMGUI 表单。镜像 Blazor 的 CueEditor.razor：
/// tag + isStatic + actions 列表（每条 action 按公共可读写 scalar 属性反射出 UI 控件）。
/// 任何修改通过 <paramref name="changed"/> 通知上层 Refresh 重新校验。</summary>
internal static class CueEditorView
{
    private static bool _jsonPreviewFoldout;

    public static void Render(CueDocument.Entry entry, EditorProfile profile, Action changed)
    {
        var def = entry.Def;

        // 头部 + 改名
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("cue", EditorStyles.boldLabel, GUILayout.Width(30));
        var newName = EditorGUILayout.TextField(entry.Name);
        if (newName != entry.Name) { entry.Name = newName.Trim(); changed(); }
        EditorGUILayout.LabelField(def.IsStatic ? "Static · 一次性 Execute" : "Actor · 生命周期 OnActive/WhileActive/Removed", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
        if (entry.SavedName is { } saved && saved.Length > 0 && saved != entry.Name)
            EditorGUILayout.HelpBox($"⚠ 已重命名 '{saved}' → '{entry.Name}'：保存只更新目录内的键，目录外的旧引用不会自动更新。", MessageType.Warning);

        // 基本
        EditorGUILayout.LabelField("基本", EditorStyles.boldLabel);
        var tagText = EditorGUILayout.TextField("Tag", def.Tag.IsValid ? def.Tag.Name : "");
        var parsed = Ui.ParseTag(tagText);
        if (!parsed.Equals(def.Tag)) { def.Tag = parsed; changed(); }
        if (TagIsOffRoot(def))
            EditorGUILayout.HelpBox("⚠ 必须在 'GameplayCue.' 根下，运行时会被 GameplayCueManager 忽略。", MessageType.Warning);
        var isStatic = EditorGUILayout.Toggle("isStatic", def.IsStatic);
        if (isStatic != def.IsStatic) { def.IsStatic = isStatic; changed(); }

        // 动作
        EditorGUILayout.LabelField($"动作 ({def.Actions.Count})", EditorStyles.boldLabel);
        for (var i = 0; i < def.Actions.Count; i++)
        {
            var action = def.Actions[i];
            var props = ScalarProperties(action);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            // type 下拉
            var names = profile.CueActions.Select(c => c.ActionType.Name).ToArray();
            var currentName = action.GetType().Name;
            var currentIndex = Array.IndexOf(names, currentName);
            if (currentIndex < 0)
            {
                var extended = new string[names.Length + 1];
                Array.Copy(names, extended, names.Length);
                extended[names.Length] = $"{currentName}（未加载）";
                names = extended;
                currentIndex = names.Length - 1;
            }
            var pickedIndex = EditorGUILayout.Popup("type", currentIndex, names);
            if (pickedIndex != currentIndex && pickedIndex >= 0 && pickedIndex < profile.CueActions.Count)
            {
                var newType = profile.CueActions[pickedIndex].ActionType;
                if (Activator.CreateInstance(newType) is CueAction replacement)
                {
                    replacement.TriggerOn = action.TriggerOn;
                    def.Actions[i] = replacement;
                    changed();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                continue;
            }
            // triggerOn 下拉
            var triggerNames = Enum.GetNames(typeof(GameplayCueEvent));
            var triggerIndex = Array.IndexOf(triggerNames, action.TriggerOn.ToString());
            if (triggerIndex < 0) triggerIndex = 0;
            var pickedTrigger = EditorGUILayout.Popup("triggerOn", triggerIndex, triggerNames);
            if (pickedTrigger != triggerIndex)
            {
                action.TriggerOn = Ui.Enum<GameplayCueEvent>(triggerNames[pickedTrigger]);
                changed();
            }
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                def.Actions.RemoveAt(i); changed();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                continue;
            }
            EditorGUILayout.EndHorizontal();

            // per-type scalar 属性
            foreach (var prop in props)
            {
                var current = prop.GetValue(action);
                object? next = null;
                if (prop.PropertyType == typeof(string))
                {
                    var text = (string?)current ?? "";
                    var edited = EditorGUILayout.TextField(prop.Name, text);
                    if (edited != text) { prop.SetValue(action, edited); changed(); }
                }
                else if (prop.PropertyType == typeof(bool))
                {
                    var b = (bool)(current ?? false);
                    var edited = EditorGUILayout.Toggle(prop.Name, b);
                    if (edited != b) { prop.SetValue(action, edited); changed(); }
                }
                else if (prop.PropertyType == typeof(float))
                {
                    var f = (float)(current ?? 0f);
                    var edited = EditorGUILayout.FloatField(prop.Name, f);
                    if (!Mathf.Approximately(edited, f)) { prop.SetValue(action, edited); changed(); }
                }
                else if (prop.PropertyType == typeof(int))
                {
                    var n = (int)(current ?? 0);
                    var edited = EditorGUILayout.IntField(prop.Name, n);
                    if (edited != n) { prop.SetValue(action, edited); changed(); }
                }
                else
                {
                    EditorGUILayout.LabelField($"{prop.Name}（不支持的属性类型 {prop.PropertyType.Name}——schema 的 Writer 仍会写出/读回，编辑器当前不提供可视化）", EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndVertical();
        }
        if (GUILayout.Button("＋ 动作", GUILayout.Width(80)) && profile.CueActions.Count > 0)
        {
            var firstType = profile.CueActions[0].ActionType;
            if (Activator.CreateInstance(firstType) is CueAction a)
            {
                a.TriggerOn = def.IsStatic ? GameplayCueEvent.Executed : GameplayCueEvent.OnActive;
                def.Actions.Add(a);
                changed();
            }
        }

        // JSON 预览
        EditorGUILayout.Space();
        _jsonPreviewFoldout = EditorGUILayout.Foldout(_jsonPreviewFoldout, "JSON 预览（保存时的写出形态，只读）", true);
        if (_jsonPreviewFoldout)
        {
            var json = GasNetDataCueWriter.WriteCatalog(
                [new KeyValuePair<string, CueDefinition>(entry.Name, entry.Def)],
                profile.BuildCueOptions());
            EditorGUILayout.TextArea(json, GUILayout.MinHeight(140));
        }
    }

    private static bool TagIsOffRoot(CueDefinition def) =>
        def.Tag.IsValid
        && !def.Tag.Name.StartsWith("GameplayCue.", StringComparison.Ordinal);

    private static PropertyInfo[] ScalarProperties(CueAction action) =>
        action.GetType().GetProperties()
            .Where(p => p.GetMethod is not null && p.SetMethod is not null)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.Name != nameof(CueAction.TriggerOn))
            .Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(bool)
                     || p.PropertyType == typeof(float) || p.PropertyType == typeof(int))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();
}
