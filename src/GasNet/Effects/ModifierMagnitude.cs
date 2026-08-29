namespace GasNet;

/// <summary>Identifies an attribute to capture for MMCs/ExecCalcs/AttributeBased magnitudes (doc §4.5.11).</summary>
public sealed record AttributeCaptureDefinition(
    GameplayAttribute Attribute,
    GameplayAttributeCaptureSource CaptureSource,
    bool Snapshot);

/// <summary>Carries source/target ASC references and their captured tags into magnitude evaluation (UE: <c>FAggregatorEvaluateParameters</c>).</summary>
public sealed class AggregatorEvaluateParameters
{
    public AbilitySystemComponent? Source { get; set; }
    public AbilitySystemComponent? Target { get; set; }
    public GameplayTagContainer SourceTags { get; set; } = new();
    public GameplayTagContainer TargetTags { get; set; } = new();
}

/// <summary>Base class of all modifier magnitudes (UE: <c>FGameplayEffectModifierMagnitude</c>).</summary>
public abstract class GameplayEffectModifierMagnitude
{
    /// <summary>Final = ((Base * Coefficient) + PreMultiplyAdditive) * PostMultiplyAdditive.</summary>
    public float Coefficient { get; set; } = 1f;
    public float PreMultiplyAdditive { get; set; }
    public float PostMultiplyAdditive { get; set; } = 1f;

    public abstract GameplayEffectMagnitudeType Type { get; }

    /// <summary>The raw magnitude before coefficient/post-processing.</summary>
    public abstract float GetBaseMagnitude(GameplayEffectSpec spec);

    public float Evaluate(GameplayEffectSpec spec)
    {
        float magnitude = GetBaseMagnitude(spec);
        magnitude = (magnitude * Coefficient) + PreMultiplyAdditive;
        return magnitude * PostMultiplyAdditive;
    }
}

/// <summary>A constant (optionally level-scaled) value (UE: <c>FScalableFloat</c>).</summary>
public sealed class ScalableFloatMagnitude : GameplayEffectModifierMagnitude
{
    public float Value { get; set; }
    /// <summary>Value added per level (curve stand-in for the UE data-table curve).</summary>
    public float ValuePerLevel { get; set; }

    public ScalableFloatMagnitude() { }
    public ScalableFloatMagnitude(float value) => Value = value;

    public override GameplayEffectMagnitudeType Type => GameplayEffectMagnitudeType.ScalableFloat;

    public override float GetBaseMagnitude(GameplayEffectSpec spec) => Value + ValuePerLevel * (spec.Level - 1);
}

/// <summary>
/// Magnitude read from an attribute on the Source or Target ASC (doc §4.5.4).
/// Snapshot=Yes + Source captures at spec CREATION; Snapshot=Yes + Target captures at APPLICATION;
/// non-snapshot captures at application and auto-updates while the GE is active.
/// </summary>
public sealed class AttributeBasedMagnitude : GameplayEffectModifierMagnitude
{
    public required AttributeCaptureDefinition Capture { get; set; }

    /// <summary>When set, only mods whose source tags match at least one of these contribute to the captured value (doc §4.5.4.2).</summary>
    public GameplayTagContainer? SourceTagFilter { get; set; }
    public GameplayTagContainer? TargetTagFilter { get; set; }

    public bool UseBaseValue { get; set; }

    public override GameplayEffectMagnitudeType Type => GameplayEffectMagnitudeType.AttributeBased;

    public override float GetBaseMagnitude(GameplayEffectSpec spec) =>
        GameplayEffectCaptureEvaluator.CaptureAttributeMagnitude(
            spec, Capture, UseBaseValue, SourceTagFilter, TargetTagFilter);
}

/// <summary>
/// Magnitude delivered at runtime via the spec's SetByCaller map (doc §4.5.9.1).
/// As a modifier this must be declared ahead of time; a missing pair logs an error and returns 0.
/// </summary>
public sealed class SetByCallerMagnitude : GameplayEffectModifierMagnitude
{
    public GameplayTag DataTag { get; set; }

    public SetByCallerMagnitude() { }
    public SetByCallerMagnitude(GameplayTag dataTag) => DataTag = dataTag;

    public override GameplayEffectMagnitudeType Type => GameplayEffectMagnitudeType.SetByCaller;

    public override float GetBaseMagnitude(GameplayEffectSpec spec) =>
        spec.GetSetByCallerMagnitude(DataTag, warnIfNotFound: true, defaultIfNotFound: 0f);
}

/// <summary>Magnitude computed by a <see cref="ModifierMagnitudeCalculation"/> (doc §4.5.11).</summary>
public sealed class CustomCalculationMagnitude : GameplayEffectModifierMagnitude
{
    public ModifierMagnitudeCalculation Calculation { get; set; } = default!;

    public CustomCalculationMagnitude() { }
    public CustomCalculationMagnitude(ModifierMagnitudeCalculation calculation) => Calculation = calculation;

    public override GameplayEffectMagnitudeType Type => GameplayEffectMagnitudeType.CustomCalculationClass;

    public override float GetBaseMagnitude(GameplayEffectSpec spec) => Calculation.CalculateBaseMagnitude(spec);
}
