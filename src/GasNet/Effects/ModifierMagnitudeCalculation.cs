namespace GasNet;

/// <summary>
/// Evaluates attribute captures — equivalent to UE's <c>FGameplayEffectAttributeCaptureSpec</c>.
/// Captured values are recomputed from the owning ASC's aggregators, which BYPASSES
/// <see cref="AttributeSet.PreAttributeChange"/>; re-clamp inside calcs if needed (doc §4.5.11).
/// </summary>
public static class GameplayEffectCaptureEvaluator
{
    public static AbilitySystemComponent? ResolveASC(GameplayEffectSpec spec, GameplayAttributeCaptureSource source)
    {
        var context = spec.Context;
        return source == GameplayAttributeCaptureSource.Source
            ? context?.InstigatorAbilitySystemComponent
            : context?.TargetAbilitySystemComponent;
    }

    public static GameplayTagContainer GetSourceTags(GameplayEffectSpec spec) => spec.CapturedSourceTags;
    public static GameplayTagContainer GetTargetTags(GameplayEffectSpec spec) => spec.CapturedTargetTags;

    public static AggregatorEvaluateParameters BuildEvaluateParameters(GameplayEffectSpec spec) => new()
    {
        Source = spec.Context?.InstigatorAbilitySystemComponent,
        Target = spec.Context?.TargetAbilitySystemComponent,
        SourceTags = GetSourceTags(spec),
        TargetTags = GetTargetTags(spec),
    };

    /// <summary>Captures an attribute's aggregated value. Unregistered captures throw (matches engine's "missing Spec" error).</summary>
    public static float CaptureAttributeMagnitude(
        GameplayEffectSpec spec,
        AttributeCaptureDefinition capture,
        bool useBaseValue = false,
        GameplayTagContainer? sourceTagFilter = null,
        GameplayTagContainer? targetTagFilter = null)
    {
        var asc = ResolveASC(spec, capture.CaptureSource)
            ?? throw new InvalidOperationException(
                $"Cannot capture '{capture.Attribute}': no {(capture.CaptureSource == GameplayAttributeCaptureSource.Source ? "source" : "target")} ASC in the effect context.");

        if (useBaseValue)
            return asc.GetNumericAttributeBase(capture.Attribute);

        return asc.EvaluateAttributeAggregated(capture.Attribute, sourceTagFilter, targetTagFilter);
    }
}

/// <summary>
/// Custom magnitude calculation class (doc §4.5.11). Predictable; usable with any duration policy.
/// Register captures via <see cref="RelevantAttributesToCapture"/>.
/// </summary>
public abstract class ModifierMagnitudeCalculation
{
    public List<AttributeCaptureDefinition> RelevantAttributesToCapture { get; } = [];

    public abstract float CalculateBaseMagnitude(GameplayEffectSpec spec);

    protected void AddCapture(GameplayAttribute attribute, GameplayAttributeCaptureSource source, bool snapshot = false) =>
        RelevantAttributesToCapture.Add(new AttributeCaptureDefinition(attribute, source, snapshot));

    protected float GetCapturedAttributeMagnitude(AttributeCaptureDefinition capture, GameplayEffectSpec spec) =>
        GameplayEffectCaptureEvaluator.CaptureAttributeMagnitude(spec, capture);

    protected GameplayTagContainer GetSourceTags(GameplayEffectSpec spec) => GameplayEffectCaptureEvaluator.GetSourceTags(spec);
    protected GameplayTagContainer GetTargetTags(GameplayEffectSpec spec) => GameplayEffectCaptureEvaluator.GetTargetTags(spec);

    protected float GetSetByCallerMagnitude(GameplayEffectSpec spec, GameplayTag tag, float defaultIfNotFound = 0f) =>
        spec.GetSetByCallerMagnitude(tag, warnIfNotFound: false, defaultIfNotFound: defaultIfNotFound);

    /// <summary>The ability that created this spec, if any (cost/cooldown MMC pattern, doc §4.5.14).</summary>
    protected T? GetAbilityInstance<T>(GameplayEffectSpec spec) where T : GameplayAbility =>
        spec.Context?.AbilityInstance as T;
}
