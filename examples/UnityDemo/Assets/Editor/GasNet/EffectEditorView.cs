using System;
using System.Linq;
using GasNet;
using GasNet.Data;
using UnityEditor;
using UnityEngine;

namespace GasNet.Editor.Unity;

/// <summary>单个 <see cref="GameplayEffectDefinition"/> 的 IMGUI 表单。
/// 镜像 Blazor 的 EffectEditor.razor：基本/修饰符/执行/应用条件/标签/标签需求/堆叠/授予能力
/// 九块面板全部画出来。每处编辑都通过 <paramref name="changed"/> 回调通知上层 Refresh 重新校验。</summary>
internal static class EffectEditorView
{
    public static void Render(CatalogDocument.Entry entry, EditorProfile profile, Action changed)
    {
        var def = entry.Def;

        // ---- 头部 + 改名 ----
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("effect", EditorStyles.boldLabel, GUILayout.Width(50));
        var newName = EditorGUILayout.TextField(entry.Name);
        if (newName != entry.Name) { entry.Name = newName.Trim(); changed(); }
        var subtitle = def.DurationPolicy.ToString();
        if (def.IsPeriodic) subtitle += $" · 每 {def.Period:0.##}s";
        EditorGUILayout.LabelField(subtitle, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
        if (entry.SavedName is { } saved && saved.Length > 0 && saved != entry.Name)
        {
            EditorGUILayout.HelpBox($"⚠ 已重命名 '{saved}' → '{entry.Name}'：保存只更新目录内的键，目录外的旧引用不会自动更新。", MessageType.Warning);
        }

        // ---- 基本 ----
        EditorGUILayout.LabelField("基本", EditorStyles.boldLabel);
        var policy = (GameplayEffectDurationType)EditorGUILayout.EnumPopup("时长策略", def.DurationPolicy);
        if (policy != def.DurationPolicy) { def.DurationPolicy = policy; changed(); }
        if (def.HasDuration)
        {
            var d = EditorGUILayout.FloatField("时长 (s)", def.Duration);
            if (d != def.Duration) { def.Duration = d; changed(); }
        }
        var p = EditorGUILayout.FloatField("周期 (s)", def.Period);
        if (p != def.Period) { def.Period = p; changed(); }
        if (def.IsInstant && def.Period > 0f)
            EditorGUILayout.HelpBox("Instant 不支持周期。", MessageType.Info);

        DrawModifiers(def, profile, changed);
        DrawExecutions(def, profile, changed);
        DrawRequirements(def, profile, changed);
        DrawTags(def, changed);
        DrawTagRequirements(def, changed);
        DrawRemoveAndImmunity(def, changed);
        DrawStacking(def, changed);
        DrawGrantedAbilities(def, profile, changed);
        DrawJsonPreview(entry);
    }

    // ===================================================================
    // 修饰符
    // ===================================================================
    private static void DrawModifiers(GameplayEffectDefinition def, EditorProfile profile, Action changed)
    {
        EditorGUILayout.LabelField($"修饰符 ({def.Modifiers.Count})", EditorStyles.boldLabel);
        for (var i = 0; i < def.Modifiers.Count; i++)
        {
            var mod = def.Modifiers[i];
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            // 属性下拉
            var attrKeys = profile.Attributes.Select(a => a.Key).ToArray();
            var currentKey = AttributeKey(mod.Attribute);
            var currentIndex = Array.IndexOf(attrKeys, currentKey);
            if (currentIndex < 0)
            {
                // 保留旧键（profile 刷新前可能不识别）
                var extended = new string[attrKeys.Length + 1];
                Array.Copy(attrKeys, extended, attrKeys.Length);
                extended[attrKeys.Length] = currentKey;
                attrKeys = extended;
                currentIndex = attrKeys.Length - 1;
            }
            var pickedIndex = EditorGUILayout.Popup("属性", currentIndex, attrKeys);
            if (pickedIndex != currentIndex && pickedIndex >= 0 && pickedIndex < profile.Attributes.Count)
            {
                mod.Attribute = profile.Attributes[pickedIndex].Attribute;
                changed();
            }
            else if (pickedIndex != currentIndex && pickedIndex >= profile.Attributes.Count)
            {
                // 用户从"未加载"重新选回未知键——保持原状
            }
            // 操作
            var op = (GameplayModOp)EditorGUILayout.EnumPopup("操作", mod.ModifierOp);
            if (op != mod.ModifierOp) { mod.ModifierOp = op; changed(); }
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                def.Modifiers.RemoveAt(i); changed();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            // magnitude
            MagnitudeEditorView.Render(mod.Magnitude, profile,
                next => { mod.Magnitude = next; changed(); }, changed);

            // 标签过滤
            var srcText = EditorGUILayout.TextField("SourceTags", Ui.ToText(mod.SourceTags));
            if (srcText != Ui.ToText(mod.SourceTags)) { mod.SourceTags = Ui.ParseTags(srcText) ?? new GameplayTagContainer(); changed(); }
            var tgtText = EditorGUILayout.TextField("TargetTags", Ui.ToText(mod.TargetTags));
            if (tgtText != Ui.ToText(mod.TargetTags)) { mod.TargetTags = Ui.ParseTags(tgtText) ?? new GameplayTagContainer(); changed(); }
            EditorGUILayout.EndVertical();
        }
        if (GUILayout.Button("＋ 修饰符", GUILayout.Width(120)))
        {
            if (profile.Attributes.Count > 0)
            {
                var first = profile.Attributes[0].Attribute;
                def.Modifiers.Add(new GameplayModifierInfo { Attribute = first, Magnitude = new ScalableFloatMagnitude() });
                changed();
            }
        }
    }

    // ===================================================================
    // 执行
    // ===================================================================
    private static void DrawExecutions(GameplayEffectDefinition def, EditorProfile profile, Action changed)
    {
        EditorGUILayout.LabelField($"执行计算 ({def.Executions.Count})", EditorStyles.boldLabel);
        for (var i = 0; i < def.Executions.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            var names = profile.Executions.Select(t => t.Name).ToArray();
            var currentName = def.Executions[i].GetType().Name;
            var currentIndex = Array.IndexOf(names, currentName);
            if (currentIndex < 0)
            {
                var extended = new string[names.Length + 1];
                Array.Copy(names, extended, names.Length);
                extended[names.Length] = currentName;
                names = extended;
                currentIndex = names.Length - 1;
            }
            var pickedIndex = EditorGUILayout.Popup(currentIndex, names);
            if (pickedIndex != currentIndex && pickedIndex >= 0 && pickedIndex < profile.Executions.Count)
            {
                def.Executions[i] = (GameplayEffectExecutionCalculation)Activator.CreateInstance(profile.Executions[pickedIndex])!;
                changed();
            }
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                def.Executions.RemoveAt(i); changed();
                EditorGUILayout.EndHorizontal();
                return;
            }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("＋ 执行", GUILayout.Width(80)) && profile.Executions.Count > 0)
        {
            def.Executions.Add((GameplayEffectExecutionCalculation)Activator.CreateInstance(profile.Executions[0])!);
            changed();
        }
    }

    // ===================================================================
    // 应用条件（CAR）
    // ===================================================================
    private static void DrawRequirements(GameplayEffectDefinition def, EditorProfile profile, Action changed)
    {
        EditorGUILayout.LabelField($"应用条件 ({def.CustomApplicationRequirements.Count})", EditorStyles.boldLabel);
        for (var i = 0; i < def.CustomApplicationRequirements.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            var names = profile.Requirements.Select(t => t.Name).ToArray();
            var currentName = def.CustomApplicationRequirements[i].GetType().Name;
            var currentIndex = Array.IndexOf(names, currentName);
            if (currentIndex < 0)
            {
                var extended = new string[names.Length + 1];
                Array.Copy(names, extended, names.Length);
                extended[names.Length] = currentName;
                names = extended;
                currentIndex = names.Length - 1;
            }
            var pickedIndex = EditorGUILayout.Popup(currentIndex, names);
            if (pickedIndex != currentIndex && pickedIndex >= 0 && pickedIndex < profile.Requirements.Count)
            {
                def.CustomApplicationRequirements[i] = (GameplayEffectCustomApplicationRequirement)Activator.CreateInstance(profile.Requirements[pickedIndex])!;
                changed();
            }
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                def.CustomApplicationRequirements.RemoveAt(i); changed();
                EditorGUILayout.EndHorizontal();
                return;
            }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("＋ 应用条件", GUILayout.Width(100)) && profile.Requirements.Count > 0)
        {
            def.CustomApplicationRequirements.Add((GameplayEffectCustomApplicationRequirement)Activator.CreateInstance(profile.Requirements[0])!);
            changed();
        }
    }

    // ===================================================================
    // 标签（3 个容器）
    // ===================================================================
    private static void DrawTags(GameplayEffectDefinition def, Action changed)
    {
        EditorGUILayout.LabelField("标签", EditorStyles.boldLabel);
        DrawTagField("AssetTags", def.AssetTags, changed);
        DrawTagField("GrantedTags", def.GrantedTags, changed);
        DrawTagField("CueTags", def.GameplayCueTags, changed);
    }

    private static void DrawTagField(string label, GameplayTagContainer container, Action changed)
    {
        var text = EditorGUILayout.TextField(label, Ui.ToText(container));
        if (text != Ui.ToText(container)) { container.Clear(); Ui.FillTags(container, text); changed(); }
    }

    // ===================================================================
    // 标签需求（6 个）
    // ===================================================================
    private static void DrawTagRequirements(GameplayEffectDefinition def, Action changed)
    {
        EditorGUILayout.LabelField("标签需求", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");
        DrawRequirementPair("Application.Require", def.ApplicationTagRequirements.RequireTags, changed);
        DrawRequirementPair("Application.Ignore", def.ApplicationTagRequirements.IgnoreTags, changed);
        DrawRequirementPair("Target.Require",     def.TargetTagRequirements.RequireTags, changed);
        DrawRequirementPair("Target.Ignore",     def.TargetTagRequirements.IgnoreTags, changed);
        DrawRequirementPair("Ongoing.Require",   def.OngoingTagRequirements.RequireTags, changed);
        DrawRequirementPair("Ongoing.Ignore",   def.OngoingTagRequirements.IgnoreTags, changed);
        EditorGUILayout.EndVertical();
    }

    private static void DrawRequirementPair(string label, GameplayTagContainer container, Action changed)
    {
        var text = EditorGUILayout.TextField(label, Ui.ToText(container));
        if (text != Ui.ToText(container)) { Ui.FillTags(container, text); changed(); }
    }

    // ===================================================================
    // 驱散 + 免疫
    // ===================================================================
    private static void DrawRemoveAndImmunity(GameplayEffectDefinition def, Action changed)
    {
        EditorGUILayout.LabelField("驱散与免疫", EditorStyles.boldLabel);
        DrawTagField("RemoveGameplayEffectsWithTags", def.RemoveGameplayEffectsWithTags, changed);
        DrawTagField("GrantedApplicationImmunityTags", def.GrantedApplicationImmunityTags, changed);
    }

    // ===================================================================
    // 堆叠
    // ===================================================================
    private static void DrawStacking(GameplayEffectDefinition def, Action changed)
    {
        EditorGUILayout.LabelField("堆叠", EditorStyles.boldLabel);
        var st = (GameplayEffectStackingType)EditorGUILayout.EnumPopup("类型", def.StackingType);
        if (st != def.StackingType) { def.StackingType = st; changed(); }
        if (def.StackingType == GameplayEffectStackingType.DoNotStack) return;
        var lim = EditorGUILayout.IntField("StackLimitCount", def.StackLimitCount);
        if (lim != def.StackLimitCount) { def.StackLimitCount = lim; changed(); }
        var drp = (GameplayEffectStackingDurationPolicy)EditorGUILayout.EnumPopup("DurationRefresh", def.StackDurationRefreshPolicy);
        if (drp != def.StackDurationRefreshPolicy) { def.StackDurationRefreshPolicy = drp; changed(); }
        var prp = (GameplayEffectStackingPeriodPolicy)EditorGUILayout.EnumPopup("PeriodReset", def.StackPeriodResetPolicy);
        if (prp != def.StackPeriodResetPolicy) { def.StackPeriodResetPolicy = prp; changed(); }
        var exp = (GameplayEffectStackingExpiryPolicy)EditorGUILayout.EnumPopup("Expiry", def.StackExpiryPolicy);
        if (exp != def.StackExpiryPolicy) { def.StackExpiryPolicy = exp; changed(); }
    }

    // ===================================================================
    // 授予能力
    // ===================================================================
    private static void DrawGrantedAbilities(GameplayEffectDefinition def, EditorProfile profile, Action changed)
    {
        EditorGUILayout.LabelField($"授予能力 ({def.GrantedAbilities.Count})", EditorStyles.boldLabel);
        for (var i = 0; i < def.GrantedAbilities.Count; i++)
        {
            var ga = def.GrantedAbilities[i];
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            var names = profile.Abilities.Select(t => t.Name).ToArray();
            var currentName = ga.AbilityType.Name;
            var currentIndex = Array.IndexOf(names, currentName);
            if (currentIndex < 0)
            {
                var extended = new string[names.Length + 1];
                Array.Copy(names, extended, names.Length);
                extended[names.Length] = currentName;
                names = extended;
                currentIndex = names.Length - 1;
            }
            var pickedIndex = EditorGUILayout.Popup("能力", currentIndex, names);
            if (pickedIndex != currentIndex && pickedIndex >= 0 && pickedIndex < profile.Abilities.Count)
            {
                ga.AbilityType = profile.Abilities[pickedIndex];
                changed();
            }
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                def.GrantedAbilities.RemoveAt(i); changed();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();
            var lvl = EditorGUILayout.FloatField("Level", ga.Level);
            if (lvl != ga.Level) { ga.Level = lvl; changed(); }
            var input = EditorGUILayout.IntField("InputID", ga.InputID);
            if (input != ga.InputID) { ga.InputID = input; changed(); }
            var rp = (GameplayEffectAbilityRemovalPolicy)EditorGUILayout.EnumPopup("RemovalPolicy", ga.RemovalPolicy);
            if (rp != ga.RemovalPolicy) { ga.RemovalPolicy = rp; changed(); }
            EditorGUILayout.EndVertical();
        }
        if (GUILayout.Button("＋ 授予能力", GUILayout.Width(100)) && profile.Abilities.Count > 0)
        {
            def.GrantedAbilities.Add(new GrantedAbilityEntry { AbilityType = profile.Abilities[0] });
            changed();
        }
    }

    // ===================================================================
    // JSON 预览
    // ===================================================================
    private static bool _jsonPreviewFoldout;
    private static void DrawJsonPreview(CatalogDocument.Entry entry)
    {
        EditorGUILayout.Space();
        _jsonPreviewFoldout = EditorGUILayout.Foldout(_jsonPreviewFoldout, "JSON 预览（保存时的写出形态，只读）", true);
        if (_jsonPreviewFoldout)
        {
            var json = GasNetDataWriter.WriteCatalog(
                [new KeyValuePair<string, GameplayEffectDefinition>(entry.Name, entry.Def)]);
            EditorGUILayout.TextArea(json, GUILayout.MinHeight(160));
        }
    }

    private static string AttributeKey(GameplayAttribute attr) =>
        attr.AttributeSetType is { } t ? $"{t.Name}.{attr.Name}" : "";
}
