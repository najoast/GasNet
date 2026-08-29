using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GasNet.Data;

/// <summary>
/// Loads <see cref="GameplayEffectDefinition"/>s from a JSON catalog — the data-driven stand-in for
/// UE's GE blueprint assets. All JSON plumbing lives in this assembly so the core stays dependency-free.
///
/// <para>Catalog format: <code>
/// { "effects": {
///     "GE_Damage": {
///       "durationPolicy": "Instant",                       // Instant | HasDuration | Infinite
///       "duration": 5, "period": 1,
///       "modifiers": [ { "attribute": "BattleAttributeSet.Health", "op": "Add",
///                        "magnitude": { "type": "setByCaller", "tag": "Data.Damage" },
///                        "sourceTags": [], "targetTags": [] } ],
///       "executions": [ { "calculation": "DamageExecution" } ],
///       "customApplicationRequirements": [ { "requirement": "CanAffordMana" } ],
///       "assetTags": [], "grantedTags": [], "cueTags": [],
///       "applicationTagRequirements": { "require": [], "ignore": [] },
///       "targetTagRequirements": {}, "ongoingTagRequirements": {},
///       "removeGameplayEffectsWithTags": [], "grantedApplicationImmunityTags": [],
///       "stacking": { "type": "AggregateByTarget", "limit": 4, "durationRefresh": "NeverRefresh",
///                     "periodReset": "NeverReset", "expiry": "ClearEntireStack" },
///       "grantedAbilities": [ { "ability": "BurnAbility", "level": 1, "inputId": 0,
///                               "removalPolicy": "CancelAbilityImmediately" } ]
///     } } }
/// </code></para>
///
/// <para>Magnitude types: <c>scalableFloat</c> (value, valuePerLevel), <c>attributeBased</c>
/// (attribute, source, snapshot, useBaseValue, sourceTagFilter, targetTagFilter),
/// <c>setByCaller</c> (tag), <c>customCalculation</c> (calculation); all share the UE-style final
/// shaping fields <c>coefficient</c> (1), <c>preMultiplyAdditive</c> (0), <c>postMultiplyAdditive</c> (1).</para>
///
/// <para>Tag names are auto-registered (the library's runtime tag model). Attribute references are
/// "SetTypeName.AttributeName"; execution/MMM/CAR/ability references are names registered on
/// <see cref="GasNetDataLoadOptions"/>. Unknown fields are rejected — typos must be loud.</para>
/// </summary>
public static class GasNetDataLoader
{
    public static GasNetDataCatalog LoadCatalogFile(string path, GasNetDataLoadOptions? options = null) =>
        LoadCatalog(File.ReadAllText(path), options);

    public static GasNetDataCatalog LoadCatalog(string json, GasNetDataLoadOptions? options = null)
    {
        options ??= new GasNetDataLoadOptions();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new GasNetDataException($"JSON parse error: {e.Message}", e);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new GasNetDataException("Catalog root must be a JSON object.");
            RejectUnknownFields("Catalog root", root, "effects");

            if (!TryTake(root, "effects", out var effects) || effects.ValueKind != JsonValueKind.Object)
                throw new GasNetDataException("Catalog root must contain an 'effects' object.");

            var result = new Dictionary<string, GameplayEffectDefinition>(StringComparer.Ordinal);
            foreach (var effect in effects.EnumerateObject())
            {
                if (effect.Value.ValueKind != JsonValueKind.Object)
                    throw new GasNetDataException($"Effect '{effect.Name}': expected an object.");
                result[effect.Name] = ParseEffect(effect.Name, effect.Value, options);
            }
            return new GasNetDataCatalog(result);
        }
    }

    // ------------------------------------------------------------------
    // Effect
    // ------------------------------------------------------------------

    private static readonly string[] EffectFields =
    [
        "durationPolicy", "duration", "period",
        "modifiers", "executions", "customApplicationRequirements",
        "assetTags", "grantedTags", "cueTags",
        "applicationTagRequirements", "targetTagRequirements", "ongoingTagRequirements",
        "removeGameplayEffectsWithTags", "grantedApplicationImmunityTags",
        "stacking", "grantedAbilities",
    ];

    private static GameplayEffectDefinition ParseEffect(string name, JsonElement el, GasNetDataLoadOptions o)
    {
        var ctx = $"Effect '{name}'";
        RejectUnknownFields(ctx, el, EffectFields);

        var def = new GameplayEffectDefinition();
        if (TryTake(el, "durationPolicy", out var e))
            def.DurationPolicy = ParseEnum<GameplayEffectDurationType>($"{ctx}.durationPolicy", e);
        if (TryTake(el, "duration", out e)) def.Duration = GetFloat($"{ctx}.duration", e);
        if (TryTake(el, "period", out e)) def.Period = GetFloat($"{ctx}.period", e);

        if (TryTake(el, "modifiers", out var modifiers))
        {
            int i = 0;
            foreach (var item in ExpectArray($"{ctx}.modifiers", modifiers))
                def.Modifiers.Add(ParseModifier($"{ctx}.modifiers[{i++}]", item, o));
        }

        if (TryTake(el, "executions", out var executions))
        {
            int i = 0;
            foreach (var item in ExpectArray($"{ctx}.executions", executions))
                def.Executions.Add(ParseNamedEntry<GameplayEffectExecutionCalculation>(
                    $"{ctx}.executions[{i++}]", item, "calculation", o));
        }

        if (TryTake(el, "customApplicationRequirements", out var cars))
        {
            int i = 0;
            foreach (var item in ExpectArray($"{ctx}.customApplicationRequirements", cars))
                def.CustomApplicationRequirements.Add(ParseNamedEntry<GameplayEffectCustomApplicationRequirement>(
                    $"{ctx}.customApplicationRequirements[{i++}]", item, "requirement", o));
        }

        if (TryTake(el, "assetTags", out var tags)) def.AssetTags = ParseTagContainer($"{ctx}.assetTags", tags);
        if (TryTake(el, "grantedTags", out tags)) def.GrantedTags = ParseTagContainer($"{ctx}.grantedTags", tags);
        if (TryTake(el, "cueTags", out tags))
        {
            def.GameplayCueTags = ParseTagContainer($"{ctx}.cueTags", tags, warnOutsideCueRoot: true);
        }

        if (TryTake(el, "applicationTagRequirements", out var req))
            ParseTagRequirements($"{ctx}.applicationTagRequirements", req, def.ApplicationTagRequirements);
        if (TryTake(el, "targetTagRequirements", out req))
            ParseTagRequirements($"{ctx}.targetTagRequirements", req, def.TargetTagRequirements);
        if (TryTake(el, "ongoingTagRequirements", out req))
            ParseTagRequirements($"{ctx}.ongoingTagRequirements", req, def.OngoingTagRequirements);

        if (TryTake(el, "removeGameplayEffectsWithTags", out tags))
            def.RemoveGameplayEffectsWithTags = ParseTagContainer($"{ctx}.removeGameplayEffectsWithTags", tags);
        if (TryTake(el, "grantedApplicationImmunityTags", out tags))
            def.GrantedApplicationImmunityTags = ParseTagContainer($"{ctx}.grantedApplicationImmunityTags", tags);

        if (TryTake(el, "stacking", out var stacking))
            ParseStacking($"{ctx}.stacking", stacking, def);

        if (TryTake(el, "grantedAbilities", out var abilities))
        {
            int i = 0;
            foreach (var item in ExpectArray($"{ctx}.grantedAbilities", abilities))
                def.GrantedAbilities.Add(ParseGrantedAbility($"{ctx}.grantedAbilities[{i++}]", item, o));
        }

        if (def.Modifiers.Count == 0 && def.Executions.Count == 0)
            GasNetLog.Warn($"GasNet.Data: effect '{name}' has no modifiers and no executions; it will change nothing.");
        return def;
    }

    // ------------------------------------------------------------------
    // Modifier + magnitude
    // ------------------------------------------------------------------

    private static readonly string[] ModifierFields = ["attribute", "op", "magnitude", "sourceTags", "targetTags"];

    private static GameplayModifierInfo ParseModifier(string ctx, JsonElement el, GasNetDataLoadOptions o)
    {
        RejectUnknownFields(ctx, el, ModifierFields);
        if (!TryTake(el, "attribute", out var attrEl))
            throw new GasNetDataException($"{ctx}: modifiers need an 'attribute'.");
        if (!TryTake(el, "magnitude", out var magEl))
            throw new GasNetDataException($"{ctx}: modifiers need a 'magnitude'.");

        return new GameplayModifierInfo
        {
            Attribute = ParseAttribute($"{ctx}.attribute", GetString($"{ctx}.attribute", attrEl), o),
            ModifierOp = TryTake(el, "op", out var opEl)
                ? ParseEnum<GameplayModOp>($"{ctx}.op", opEl)
                : GameplayModOp.Add,
            Magnitude = ParseMagnitude($"{ctx}.magnitude", magEl, o),
            SourceTags = TryTake(el, "sourceTags", out var st) ? ParseTagContainer($"{ctx}.sourceTags", st) : new(),
            TargetTags = TryTake(el, "targetTags", out var tt) ? ParseTagContainer($"{ctx}.targetTags", tt) : new(),
        };
    }

    private static readonly string[] MagnitudeFields =
    [
        "type", "value", "valuePerLevel", "attribute", "source", "snapshot", "useBaseValue",
        "sourceTagFilter", "targetTagFilter", "tag", "calculation",
        "coefficient", "preMultiplyAdditive", "postMultiplyAdditive",
    ];

    private static readonly string[] MagnitudeTypeNames = ["scalableFloat", "attributeBased", "setByCaller", "customCalculation"];

    private static GameplayEffectModifierMagnitude ParseMagnitude(string ctx, JsonElement el, GasNetDataLoadOptions o)
    {
        RejectUnknownFields(ctx, el, MagnitudeFields);
        if (!TryTake(el, "type", out var typeEl))
            throw new GasNetDataException($"{ctx}: magnitudes need a 'type'. Valid: {string.Join(", ", MagnitudeTypeNames)}.");
        var type = GetString($"{ctx}.type", typeEl);

        GameplayEffectModifierMagnitude magnitude = type switch
        {
            "scalableFloat" => new ScalableFloatMagnitude
            {
                Value = TryTake(el, "value", out var v) ? GetFloat($"{ctx}.value", v) : 0f,
                ValuePerLevel = TryTake(el, "valuePerLevel", out var vpl) ? GetFloat($"{ctx}.valuePerLevel", vpl) : 0f,
            },
            "attributeBased" => ParseAttributeBasedMagnitude(ctx, el, o),
            "setByCaller" => new SetByCallerMagnitude
            {
                DataTag = TryTake(el, "tag", out var tagEl)
                    ? ParseTag($"{ctx}.tag", tagEl)
                    : throw new GasNetDataException($"{ctx}: setByCaller magnitudes need a 'tag'."),
            },
            "customCalculation" => ParseCustomCalculationMagnitude(ctx, el, o),
            _ => throw new GasNetDataException(
                $"{ctx}: unknown magnitude type '{type}'. Valid: {string.Join(", ", MagnitudeTypeNames)}."),
        };

        // Shared final shaping (base magnitude class fields, doc §4.5.4):
        // final = ((base * coefficient) + preMultiplyAdditive) * postMultiplyAdditive
        if (TryTake(el, "coefficient", out var c)) magnitude.Coefficient = GetFloat($"{ctx}.coefficient", c);
        if (TryTake(el, "preMultiplyAdditive", out c)) magnitude.PreMultiplyAdditive = GetFloat($"{ctx}.preMultiplyAdditive", c);
        if (TryTake(el, "postMultiplyAdditive", out c)) magnitude.PostMultiplyAdditive = GetFloat($"{ctx}.postMultiplyAdditive", c);
        return magnitude;
    }

    /// <summary>The magnitude element is already field-validated by <see cref="ParseMagnitude"/> (it carries
    /// 'type' etc.), so this resolves straight from the 'calculation' name without re-checking fields.</summary>
    private static CustomCalculationMagnitude ParseCustomCalculationMagnitude(string ctx, JsonElement el, GasNetDataLoadOptions o)
    {
        if (!TryTake(el, "calculation", out var calcEl))
            throw new GasNetDataException($"{ctx}: customCalculation magnitudes need a 'calculation'.");
        return new CustomCalculationMagnitude
        {
            Calculation = ResolveAndConstruct<ModifierMagnitudeCalculation>(ctx, GetString($"{ctx}.calculation", calcEl), o),
        };
    }

    private static AttributeBasedMagnitude ParseAttributeBasedMagnitude(string ctx, JsonElement el, GasNetDataLoadOptions o)
    {
        if (!TryTake(el, "attribute", out var attrEl))
            throw new GasNetDataException($"{ctx}: attributeBased magnitudes need an 'attribute'.");
        var capture = new AttributeCaptureDefinition(
            ParseAttribute($"{ctx}.attribute", GetString($"{ctx}.attribute", attrEl), o),
            TryTake(el, "source", out var srcEl)
                ? ParseEnum<GameplayAttributeCaptureSource>($"{ctx}.source", srcEl)
                : GameplayAttributeCaptureSource.Target,
            TryTake(el, "snapshot", out var snapEl) && GetBool($"{ctx}.snapshot", snapEl));
        return new AttributeBasedMagnitude
        {
            Capture = capture,
            UseBaseValue = TryTake(el, "useBaseValue", out var bvEl) && GetBool($"{ctx}.useBaseValue", bvEl),
            SourceTagFilter = TryTake(el, "sourceTagFilter", out var sf) ? ParseTagContainer($"{ctx}.sourceTagFilter", sf) : null,
            TargetTagFilter = TryTake(el, "targetTagFilter", out var tf) ? ParseTagContainer($"{ctx}.targetTagFilter", tf) : null,
        };
    }

    // ------------------------------------------------------------------
    // Stacking / granted abilities
    // ------------------------------------------------------------------

    private static readonly string[] StackingFields = ["type", "limit", "durationRefresh", "periodReset", "expiry"];

    private static void ParseStacking(string ctx, JsonElement el, GameplayEffectDefinition def)
    {
        RejectUnknownFields(ctx, el, StackingFields);
        if (!TryTake(el, "type", out var typeEl))
            throw new GasNetDataException(
                $"{ctx}: stacking needs a 'type'. Valid: {string.Join(", ", Enum.GetNames(typeof(GameplayEffectStackingType)))}.");
        def.StackingType = ParseEnum<GameplayEffectStackingType>($"{ctx}.type", typeEl);
        if (TryTake(el, "limit", out var limitEl)) def.StackLimitCount = GetInt($"{ctx}.limit", limitEl);
        if (TryTake(el, "durationRefresh", out var drEl))
            def.StackDurationRefreshPolicy = ParseEnum<GameplayEffectStackingDurationPolicy>($"{ctx}.durationRefresh", drEl);
        if (TryTake(el, "periodReset", out var prEl))
            def.StackPeriodResetPolicy = ParseEnum<GameplayEffectStackingPeriodPolicy>($"{ctx}.periodReset", prEl);
        if (TryTake(el, "expiry", out var exEl))
            def.StackExpiryPolicy = ParseEnum<GameplayEffectStackingExpiryPolicy>($"{ctx}.expiry", exEl);
    }

    private static readonly string[] GrantedAbilityFields = ["ability", "level", "inputId", "removalPolicy"];

    private static GrantedAbilityEntry ParseGrantedAbility(string ctx, JsonElement el, GasNetDataLoadOptions o)
    {
        RejectUnknownFields(ctx, el, GrantedAbilityFields);
        if (!TryTake(el, "ability", out var abilityEl))
            throw new GasNetDataException($"{ctx}: grantedAbilities need an 'ability' (a registered GameplayAbility type name).");
        return new GrantedAbilityEntry
        {
            // Resolved to a Type only — instantiation stays with GameplayAbilitySpec, like GiveAbility(Type).
            AbilityType = ResolveType<GameplayAbility>($"{ctx}.ability", GetString($"{ctx}.ability", abilityEl), o),
            Level = TryTake(el, "level", out var levelEl) ? GetFloat($"{ctx}.level", levelEl) : 1f,
            InputID = TryTake(el, "inputId", out var inputEl) ? GetInt($"{ctx}.inputId", inputEl) : 0,
            RemovalPolicy = TryTake(el, "removalPolicy", out var policyEl)
                ? ParseEnum<GameplayEffectAbilityRemovalPolicy>($"{ctx}.removalPolicy", policyEl)
                : GameplayEffectAbilityRemovalPolicy.CancelAbilityImmediately,
        };
    }

    // ------------------------------------------------------------------
    // Tags
    // ------------------------------------------------------------------

    private static GameplayTagContainer ParseTagContainer(string ctx, JsonElement el, bool warnOutsideCueRoot = false)
    {
        var container = new GameplayTagContainer();
        int i = 0;
        foreach (var item in ExpectArray(ctx, el))
        {
            var name = GetString($"{ctx}[{i}]", item);
            if (warnOutsideCueRoot && !name.StartsWith("GameplayCue.", StringComparison.Ordinal))
                GasNetLog.Warn($"GasNet.Data: cue tag '{name}' ({ctx}[{i}]) is not under the 'GameplayCue.' root; " +
                               "the GameplayCueManager will ignore it.");
            container.AddTag(ParseTag($"{ctx}[{i}]", item, name));
            i++;
        }
        return container;
    }

    private static GameplayTag ParseTag(string ctx, JsonElement item, string? name = null)
    {
        name ??= GetString(ctx, item);
        try
        {
            // Auto-registers unknown tags — the library's runtime tag model (see README deviations).
            return GameplayTag.RequestGameplayTag(name, warnIfNotFound: false);
        }
        catch (ArgumentException e)
        {
            throw new GasNetDataException($"{ctx}: {e.Message}");
        }
    }

    private static void ParseTagRequirements(string ctx, JsonElement el, GameplayTagRequirements into)
    {
        RejectUnknownFields(ctx, el, "require", "ignore");
        if (TryTake(el, "require", out var require))
        {
            foreach (var tag in ExpectArray($"{ctx}.require", require))
                into.RequiredTags.AddTag(ParseTag($"{ctx}.require", tag));
        }
        if (TryTake(el, "ignore", out var ignore))
        {
            foreach (var tag in ExpectArray($"{ctx}.ignore", ignore))
                into.IgnoredTags.AddTag(ParseTag($"{ctx}.ignore", tag));
        }
    }

    // ------------------------------------------------------------------
    // Attribute + type resolution
    // ------------------------------------------------------------------

    private static GameplayAttribute ParseAttribute(string ctx, string reference, GasNetDataLoadOptions o)
    {
        int dot = reference.LastIndexOf('.');
        if (dot <= 0)
            throw new GasNetDataException(
                $"{ctx}: attribute reference '{reference}' must be 'SetTypeName.AttributeName'.");
        var setKey = reference[..dot];
        var attributeName = reference[(dot + 1)..];

        if (!o.AttributeSets.TryGetValue(setKey, out var setType))
            throw new GasNetDataException(
                $"{ctx}: attribute set '{setKey}' is not registered on the load options. " +
                $"Registered sets: [{string.Join(", ", o.AttributeSets.Keys)}].");
        if (!GameplayAttributeRegistry.TryGetAttribute(setType, attributeName, out var attribute))
        {
            var names = GameplayAttributeRegistry.GetAttributes(setType).Select(a => a.Name);
            throw new GasNetDataException(
                $"{ctx}: '{attributeName}' is not a GameplayAttributeData field on '{setKey}'. Found: [{string.Join(", ", names)}].");
        }
        return attribute;
    }

    /// <summary>Resolves a registered type by name and constructs it (public parameterless ctor required).</summary>
    private static T ResolveAndConstruct<T>(string ctx, string name, GasNetDataLoadOptions o) where T : class
    {
        var type = ResolveType<T>(ctx, name, o);
        try
        {
            return (T)Activator.CreateInstance(type)!;
        }
        catch (Exception e) when (e is not GasNetDataException)
        {
            throw new GasNetDataException($"{ctx}: failed to construct '{type.FullName}': {e.Message}", e);
        }
    }

    /// <summary>Parses an object whose only field names a registered type: {"calculation": "X"} / {"requirement": "X"}.</summary>
    private static T ParseNamedEntry<T>(string ctx, JsonElement el, string fieldName, GasNetDataLoadOptions o) where T : class
    {
        RejectUnknownFields(ctx, el, fieldName);
        if (!TryTake(el, fieldName, out var fieldEl))
            throw new GasNetDataException($"{ctx}: needs a '{fieldName}' (a registered type name).");
        return ResolveAndConstruct<T>(ctx, GetString($"{ctx}.{fieldName}", fieldEl), o);
    }

    private static Type ResolveType<TBase>(string ctx, string name, GasNetDataLoadOptions o) where TBase : class
    {
        if (!o.Types.TryGetValue(name, out var type))
            throw new GasNetDataException(
                $"{ctx}: type '{name}' is not registered on the load options. Registered types: [{string.Join(", ", o.Types.Keys)}].");
        if (!typeof(TBase).IsAssignableFrom(type))
            throw new GasNetDataException(
                $"{ctx}: registered type '{name}' is '{type.Name}', which is not a '{typeof(TBase).Name}'.");
        return type;
    }

    // ------------------------------------------------------------------
    // JSON plumbing
    // ------------------------------------------------------------------

    private static void RejectUnknownFields(string ctx, JsonElement el, params string[] known)
    {
        if (el.ValueKind != JsonValueKind.Object)
            throw new GasNetDataException($"{ctx}: expected an object.");
        foreach (var property in el.EnumerateObject())
        {
            if (!known.Contains(property.Name))
                throw new GasNetDataException(
                    $"{ctx}: unknown field '{property.Name}'. Valid fields: {string.Join(", ", known)}.");
        }
    }

    private static IEnumerable<JsonElement> ExpectArray(string ctx, JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array)
            throw new GasNetDataException($"{ctx}: expected an array.");
        return el.EnumerateArray();
    }

    private static bool TryTake(JsonElement obj, string field, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(field, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.Null)
            return false; // nulls are treated as "field absent" (editor round-trip friendliness)
        value = el;
        return true;
    }

    private static string GetString(string ctx, JsonElement el) => el.ValueKind == JsonValueKind.String
        ? el.GetString()!
        : throw new GasNetDataException($"{ctx}: expected a string, got {el.ValueKind}.");

    private static float GetFloat(string ctx, JsonElement el) => el.ValueKind == JsonValueKind.Number
        ? el.GetSingle()
        : throw new GasNetDataException($"{ctx}: expected a number, got {el.ValueKind}.");

    private static int GetInt(string ctx, JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Number)
            throw new GasNetDataException($"{ctx}: expected a number, got {el.ValueKind}.");
        if (el.TryGetInt32(out var value))
            return value;
        throw new GasNetDataException($"{ctx}: expected an integer, got '{el.GetRawText()}'.");
    }

    private static bool GetBool(string ctx, JsonElement el) => el.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? el.GetBoolean()
        : throw new GasNetDataException($"{ctx}: expected true or false, got {el.ValueKind}.");

    private static TEnum ParseEnum<TEnum>(string ctx, JsonElement el) where TEnum : struct, Enum
    {
        var text = el.ValueKind == JsonValueKind.String ? el.GetString() : null;
        if (text is not null && Enum.TryParse(text, ignoreCase: true, out TEnum value))
            return value;
        throw new GasNetDataException(
            $"{ctx}: '{el.GetRawText()}' is not a valid {typeof(TEnum).Name}. Valid: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}.");
    }
}
