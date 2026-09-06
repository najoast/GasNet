using System;
using System.Linq;
using GasNet;
using UnityEditor;
using UnityEngine;

namespace GasNet.Editor.Unity;

/// <summary>单条 <see cref="GameplayEffectModifierMagnitude"/> 的 IMGUI 表单。
/// 镜像 Blazor 的 MagnitudeEditor.razor：4 种 magnitude 子类型 + 三段 shaping 系数。
/// 切换类型时通过 <paramref name="replace"/> 由调用方决定是新建还是保留原值——与 Blazor
/// 版本同款"先校验 profile，再决定是否真的换"。</summary>
internal static class MagnitudeEditorView
{
    private const string TypeName_ScalableFloat      = "scalableFloat";
    private const string TypeName_AttributeBased    = "attributeBased";
    private const string TypeName_SetByCaller       = "setByCaller";
    private const string TypeName_CustomCalculation = "customCalculation";

    public static void Render(GameplayEffectModifierMagnitude? value, EditorProfile profile,
                              Action<GameplayEffectModifierMagnitude> replace, Action changed)
    {
        if (value is null) { EditorGUILayout.HelpBox("Magnitude 为空。", MessageType.Warning); return; }

        var currentType = TypeNameOf(value);
        var options = new[] { TypeName_ScalableFloat, TypeName_AttributeBased, TypeName_SetByCaller, TypeName_CustomCalculation };
        var pickedIndex = EditorGUILayout.Popup("类型", Array.IndexOf(options, currentType), options);
        if (pickedIndex < 0) pickedIndex = 0;
        var picked = options[pickedIndex];
        if (picked != currentType)
        {
            if (picked == TypeName_AttributeBased && profile.Attributes.Count == 0)
            {
                EditorGUILayout.HelpBox("需要先加载至少一个含 GameplayAttributeData 字段的 AttributeSet。", MessageType.Warning);
            }
            else if (picked == TypeName_CustomCalculation && profile.Magnitudes.Count == 0)
            {
                EditorGUILayout.HelpBox("未发现任何 ModifierMagnitudeCalculation 子类。", MessageType.Warning);
            }
            else
            {
                replace(NewMagnitude(picked, value, profile));
                changed();
                return; // 递归：下次 OnGUI 重新画，新值
            }
        }

        DrawTypeSpecific(value, profile, changed);
        EditorGUILayout.Space();
        DrawShaping(value, changed);
    }

    private static string TypeNameOf(GameplayEffectModifierMagnitude value) => value switch
    {
        ScalableFloatMagnitude      => TypeName_ScalableFloat,
        AttributeBasedMagnitude     => TypeName_AttributeBased,
        SetByCallerMagnitude        => TypeName_SetByCaller,
        CustomCalculationMagnitude  => TypeName_CustomCalculation,
        _ => TypeName_ScalableFloat,
    };

    private static GameplayEffectModifierMagnitude NewMagnitude(string typeName,
        GameplayEffectModifierMagnitude previous, EditorProfile profile) => typeName switch
    {
        TypeName_ScalableFloat => new ScalableFloatMagnitude
        {
            // 保留 shaping 系数——这是用户已经在另一面板里调过的值
            Coefficient = previous.Coefficient,
            PreMultiplyAdditive = previous.PreMultiplyAdditive,
            PostMultiplyAdditive = previous.PostMultiplyAdditive,
        },
        TypeName_AttributeBased => new AttributeBasedMagnitude
        {
            Capture = profile.Attributes.Count > 0
                ? new AttributeCaptureDefinition
                {
                    Attribute = profile.Attributes[0].Attribute,
                    CaptureSource = GasNet.GameplayAttributeCaptureSource.Source,
                }
                : default,
            Coefficient = previous.Coefficient,
            PreMultiplyAdditive = previous.PreMultiplyAdditive,
            PostMultiplyAdditive = previous.PostMultiplyAdditive,
        },
        TypeName_SetByCaller => new SetByCallerMagnitude
        {
            DataTag = default,
            Coefficient = previous.Coefficient,
            PreMultiplyAdditive = previous.PreMultiplyAdditive,
            PostMultiplyAdditive = previous.PostMultiplyAdditive,
        },
        TypeName_CustomCalculation => new CustomCalculationMagnitude
        {
            // 切换到 customCalculation 时要求有 MMC 可选
            Calculation = profile.Magnitudes.Count > 0
                ? (ModifierMagnitudeCalculation)Activator.CreateInstance(profile.Magnitudes[0])!
                : null!,
            Coefficient = previous.Coefficient,
            PreMultiplyAdditive = previous.PreMultiplyAdditive,
            PostMultiplyAdditive = previous.PostMultiplyAdditive,
        },
        _ => new ScalableFloatMagnitude(),
    };

    private static void DrawTypeSpecific(GameplayEffectModifierMagnitude value, EditorProfile profile, Action changed)
    {
        switch (value)
        {
            case ScalableFloatMagnitude s:
            {
                var v = EditorGUILayout.FloatField("Value", s.Value);
                if (v != s.Value) { s.Value = v; changed(); }
                var vpl = EditorGUILayout.FloatField("ValuePerLevel", s.ValuePerLevel);
                if (vpl != s.ValuePerLevel) { s.ValuePerLevel = vpl; changed(); }
                break;
            }
            case AttributeBasedMagnitude a:
            {
                DrawAttributeCapture(a, profile, changed);
                var sourceTags = EditorGUILayout.TextField("SourceTagFilter", Ui.ToText(a.SourceTagFilter));
                if (sourceTags != Ui.ToText(a.SourceTagFilter))
                {
                    Ui.FillTags(a.SourceTagFilter, sourceTags);
                    changed();
                }
                var targetTags = EditorGUILayout.TextField("TargetTagFilter", Ui.ToText(a.TargetTagFilter));
                if (targetTags != Ui.ToText(a.TargetTagFilter))
                {
                    Ui.FillTags(a.TargetTagFilter, targetTags);
                    changed();
                }
                break;
            }
            case SetByCallerMagnitude sb:
            {
                var dataTag = EditorGUILayout.TextField("DataTag", sb.DataTag.IsValid ? sb.DataTag.Name : "");
                var parsed = Ui.ParseTag(dataTag);
                if (!parsed.Equals(sb.DataTag)) { sb.DataTag = parsed; changed(); }
                break;
            }
            case CustomCalculationMagnitude cc:
            {
                var names = profile.Magnitudes.Select(t => t.Name).ToArray();
                var currentName = cc.Calculation?.GetType().Name ?? "";
                var currentIndex = Array.IndexOf(names, currentName);
                var pickedIndex = EditorGUILayout.Popup("Calculation", currentIndex, names);
                if (pickedIndex >= 0 && pickedIndex < profile.Magnitudes.Count && pickedIndex != currentIndex)
                {
                    cc.Calculation = (ModifierMagnitudeCalculation)Activator.CreateInstance(profile.Magnitudes[pickedIndex])!;
                    changed();
                }
                break;
            }
        }
    }

    private static void DrawAttributeCapture(AttributeBasedMagnitude a, EditorProfile profile, Action changed)
    {
        // 复制 schema 写法：列表项是 (Key, GameplayAttribute)
        var names = profile.Attributes.Select(at => at.Key).ToArray();
        var currentKey = a.Capture.Attribute.AttributeSetType is { } setType
            ? $"{setType.Name}.{a.Capture.Attribute.Name}"
            : "";
        var currentIndex = Array.IndexOf(names, currentKey);
        var pickedIndex = EditorGUILayout.Popup("Source Attribute", currentIndex, names);
        if (pickedIndex >= 0 && pickedIndex < profile.Attributes.Count && pickedIndex != currentIndex)
        {
            var picked = profile.Attributes[pickedIndex].Attribute;
            a.Capture = a.Capture with { Attribute = picked };
            changed();
        }
        var source = (GasNet.GameplayAttributeCaptureSource)EditorGUILayout.EnumPopup("CaptureSource", a.Capture.CaptureSource);
        if (source != a.Capture.CaptureSource) { a.Capture = a.Capture with { CaptureSource = source }; changed(); }
        var snapshot = EditorGUILayout.Toggle("Snapshot", a.Capture.Snapshot);
        if (snapshot != a.Capture.Snapshot) { a.Capture = a.Capture with { Snapshot = snapshot }; changed(); }
        var useBase = EditorGUILayout.Toggle("UseBaseValue", a.Capture.UseBaseValue);
        if (useBase != a.Capture.UseBaseValue) { a.Capture = a.Capture with { UseBaseValue = useBase }; changed(); }
    }

    private static void DrawShaping(GameplayEffectModifierMagnitude value, Action changed)
    {
        EditorGUILayout.LabelField("Shaping (最终 = ((基值 × 系数) + 前置加) × 后置乘)", EditorStyles.miniBoldLabel);
        var c = EditorGUILayout.FloatField("Coefficient", value.Coefficient);
        if (c != value.Coefficient) { value.Coefficient = c; changed(); }
        var pre = EditorGUILayout.FloatField("PreMultiplyAdditive", value.PreMultiplyAdditive);
        if (pre != value.PreMultiplyAdditive) { value.PreMultiplyAdditive = pre; changed(); }
        var post = EditorGUILayout.FloatField("PostMultiplyAdditive", value.PostMultiplyAdditive);
        if (post != value.PostMultiplyAdditive) { value.PostMultiplyAdditive = post; changed(); }
    }
}
