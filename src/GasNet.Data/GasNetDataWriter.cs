using System.Text.Json;
using System.Text.Json.Nodes;

namespace GasNet.Data;

/// <summary>
/// The write side of the data layer: turns loaded/edited definitions back into the catalog JSON
/// format documented on <see cref="GasNetDataLoader"/>. The editor never serializes on its own —
/// read AND write both live here so the format stays single-sourced.
///
/// <para>Writing rules: fields equal to their default are omitted (small diffs); effect order is
/// the iteration order of the input; attribute references use "SetTypeName.AttributeName" (simple
/// set name); code-backed references (executions/MMM/CAR/granted abilities) use the instance's
/// simple type name — register the types under the same names on
/// <see cref="GasNetDataLoadOptions"/> to read the file back.</para>
/// </summary>
public static class GasNetDataWriter
{
    /// <summary>Serializes a catalog. <paramref name="effects"/> must have unique keys.</summary>
    public static string WriteCatalog(IEnumerable<KeyValuePair<string, GameplayEffectDefinition>> effects)
    {
        var effectNodes = new JsonObject();
        foreach (var (name, def) in effects)
        {
            if (def is null)
                throw new GasNetDataException($"Writer: effect '{name}' is null.");
            effectNodes[name] = WriteEffect(def);
        }
        return new JsonObject { ["effects"] = effectNodes }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject WriteEffect(GameplayEffectDefinition def)
    {
        var node = new JsonObject();

        if (def.DurationPolicy != GameplayEffectDurationType.Instant)
            node["durationPolicy"] = def.DurationPolicy.ToString();
        if (def.Duration != 0f) node["duration"] = def.Duration;
        if (def.Period != 0f) node["period"] = def.Period;

        if (def.Modifiers.Count > 0)
        {
            var modifiers = new JsonArray();
            foreach (var modifier in def.Modifiers)
                modifiers.Add(WriteModifier(modifier));
            node["modifiers"] = modifiers;
        }

        if (def.Executions.Count > 0)
        {
            var executions = new JsonArray();
            foreach (var execution in def.Executions)
                executions.Add(new JsonObject { ["calculation"] = execution.GetType().Name });
            node["executions"] = executions;
        }

        if (def.CustomApplicationRequirements.Count > 0)
        {
            var requirements = new JsonArray();
            foreach (var requirement in def.CustomApplicationRequirements)
                requirements.Add(new JsonObject { ["requirement"] = requirement.GetType().Name });
            node["customApplicationRequirements"] = requirements;
        }

        if (!def.AssetTags.IsEmpty) node["assetTags"] = WriteTags(def.AssetTags);
        if (!def.GrantedTags.IsEmpty) node["grantedTags"] = WriteTags(def.GrantedTags);
        if (!def.GameplayCueTags.IsEmpty) node["cueTags"] = WriteTags(def.GameplayCueTags);

        if (WriteRequirements(def.ApplicationTagRequirements) is { } appReq) node["applicationTagRequirements"] = appReq;
        if (WriteRequirements(def.TargetTagRequirements) is { } targetReq) node["targetTagRequirements"] = targetReq;
        if (WriteRequirements(def.OngoingTagRequirements) is { } ongoingReq) node["ongoingTagRequirements"] = ongoingReq;

        if (!def.RemoveGameplayEffectsWithTags.IsEmpty)
            node["removeGameplayEffectsWithTags"] = WriteTags(def.RemoveGameplayEffectsWithTags);
        if (!def.GrantedApplicationImmunityTags.IsEmpty)
            node["grantedApplicationImmunityTags"] = WriteTags(def.GrantedApplicationImmunityTags);

        if (WriteStacking(def) is { } stacking) node["stacking"] = stacking;

        if (def.GrantedAbilities.Count > 0)
        {
            var abilities = new JsonArray();
            foreach (var entry in def.GrantedAbilities)
            {
                var abilityNode = new JsonObject { ["ability"] = entry.AbilityType.Name };
                if (entry.Level != 1f) abilityNode["level"] = entry.Level;
                if (entry.InputID != 0) abilityNode["inputId"] = entry.InputID;
                if (entry.RemovalPolicy != GameplayEffectAbilityRemovalPolicy.CancelAbilityImmediately)
                    abilityNode["removalPolicy"] = entry.RemovalPolicy.ToString();
                abilities.Add(abilityNode);
            }
            node["grantedAbilities"] = abilities;
        }

        return node;
    }

    private static JsonObject WriteModifier(GameplayModifierInfo modifier)
    {
        var node = new JsonObject
        {
            ["attribute"] = $"{modifier.Attribute.AttributeSetType.Name}.{modifier.Attribute.Name}",
            ["magnitude"] = WriteMagnitude(modifier.Magnitude),
        };
        if (modifier.ModifierOp != GameplayModOp.Add)
            node["op"] = modifier.ModifierOp.ToString();
        if (!modifier.SourceTags.IsEmpty) node["sourceTags"] = WriteTags(modifier.SourceTags);
        if (!modifier.TargetTags.IsEmpty) node["targetTags"] = WriteTags(modifier.TargetTags);
        return node;
    }

    private static JsonObject WriteMagnitude(GameplayEffectModifierMagnitude magnitude)
    {
        var node = magnitude switch
        {
            ScalableFloatMagnitude scalableFloat => WriteScalableFloatMagnitude(scalableFloat),
            AttributeBasedMagnitude attributeBased => WriteAttributeBasedMagnitude(attributeBased),
            SetByCallerMagnitude setByCaller => new JsonObject { ["type"] = "setByCaller", ["tag"] = setByCaller.DataTag.Name },
            CustomCalculationMagnitude custom => new JsonObject
            {
                ["type"] = "customCalculation",
                ["calculation"] = custom.Calculation.GetType().Name,
            },
            _ => throw new GasNetDataException(
                $"Writer: unsupported magnitude type '{magnitude.GetType().Name}'."),
        };

        // Shared final shaping fields (base magnitude class), omitted when at their defaults.
        if (magnitude.Coefficient != 1f) node["coefficient"] = magnitude.Coefficient;
        if (magnitude.PreMultiplyAdditive != 0f) node["preMultiplyAdditive"] = magnitude.PreMultiplyAdditive;
        if (magnitude.PostMultiplyAdditive != 1f) node["postMultiplyAdditive"] = magnitude.PostMultiplyAdditive;
        return node;
    }

    private static JsonObject WriteScalableFloatMagnitude(ScalableFloatMagnitude scalable)
    {
        var node = new JsonObject { ["type"] = "scalableFloat" };
        if (scalable.Value != 0f) node["value"] = scalable.Value;
        if (scalable.ValuePerLevel != 0f) node["valuePerLevel"] = scalable.ValuePerLevel;
        return node;
    }

    private static JsonObject WriteAttributeBasedMagnitude(AttributeBasedMagnitude based)
    {
        var node = new JsonObject
        {
            ["type"] = "attributeBased",
            ["attribute"] = $"{based.Capture.Attribute.AttributeSetType.Name}.{based.Capture.Attribute.Name}",
        };
        if (based.Capture.CaptureSource != GameplayAttributeCaptureSource.Target)
            node["source"] = based.Capture.CaptureSource.ToString();
        if (based.Capture.Snapshot) node["snapshot"] = true;
        if (based.UseBaseValue) node["useBaseValue"] = true;
        if (based.SourceTagFilter is { IsEmpty: false }) node["sourceTagFilter"] = WriteTags(based.SourceTagFilter);
        if (based.TargetTagFilter is { IsEmpty: false }) node["targetTagFilter"] = WriteTags(based.TargetTagFilter);
        return node;
    }

    private static JsonObject? WriteRequirements(GameplayTagRequirements requirements)
    {
        if (requirements.RequiredTags.IsEmpty && requirements.IgnoredTags.IsEmpty)
            return null;
        var node = new JsonObject();
        if (!requirements.RequiredTags.IsEmpty) node["require"] = WriteTags(requirements.RequiredTags);
        if (!requirements.IgnoredTags.IsEmpty) node["ignore"] = WriteTags(requirements.IgnoredTags);
        return node;
    }

    private static JsonObject? WriteStacking(GameplayEffectDefinition def)
    {
        if (def.StackingType == GameplayEffectStackingType.DoNotStack)
            return null;
        var node = new JsonObject { ["type"] = def.StackingType.ToString() };
        if (def.StackLimitCount != 1) node["limit"] = def.StackLimitCount;
        if (def.StackDurationRefreshPolicy != GameplayEffectStackingDurationPolicy.NeverRefresh)
            node["durationRefresh"] = def.StackDurationRefreshPolicy.ToString();
        if (def.StackPeriodResetPolicy != GameplayEffectStackingPeriodPolicy.NeverReset)
            node["periodReset"] = def.StackPeriodResetPolicy.ToString();
        if (def.StackExpiryPolicy != GameplayEffectStackingExpiryPolicy.ClearEntireStack)
            node["expiry"] = def.StackExpiryPolicy.ToString();
        return node;
    }

    private static JsonArray WriteTags(GameplayTagContainer tags)
    {
        var array = new JsonArray();
        foreach (var tag in tags.Tags)
            array.Add(tag.Name);
        return array;
    }
}
