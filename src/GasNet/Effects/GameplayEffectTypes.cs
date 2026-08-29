namespace GasNet;

/// <summary>GE duration policy (UE: <c>EGameplayModOp</c>/<c>EGameplayEffectDurationType</c>) — doc §4.5.1.</summary>
public enum GameplayEffectDurationType
{
    /// <summary>Permanent BaseValue change; no tags granted, not even for one frame; fires Executed cues.</summary>
    Instant,
    /// <summary>Temporary CurrentValue change; granted tags/cues added then removed on expiry.</summary>
    HasDuration,
    /// <summary>Temporary CurrentValue change that never expires on its own; must be removed manually.</summary>
    Infinite,
}

/// <summary>Modifier operation (UE: <c>EGameplayModOp</c>) — doc §4.5.4.</summary>
public enum GameplayModOp
{
    /// <summary>Additive (negative value subtracts).</summary>
    Add,
    /// <summary>Percentage-style multiply applied AFTER additions.</summary>
    Multiply,
    Divide,
    /// <summary>Overrides the final value; last applied wins.</summary>
    Override,
}

/// <summary>Modifier magnitude computation types (doc §4.5.4).</summary>
public enum GameplayEffectMagnitudeType
{
    ScalableFloat,
    AttributeBased,
    SetByCaller,
    CustomCalculationClass,
}

/// <summary>Which ASC an attribute is captured from (UE: <c>EGameplayEffectAttributeCaptureSource</c>).</summary>
public enum GameplayAttributeCaptureSource
{
    Source,
    Target,
}

/// <summary>Stacking modes (doc §4.5.5): by default each application adds an independent spec instance.</summary>
public enum GameplayEffectStackingType
{
    /// <summary>Every application adds a new, independent active GE instance.</summary>
    DoNotStack,
    /// <summary>One stack pool per Source ASC on the target; each source fills its own limit.</summary>
    AggregateBySource,
    /// <summary>One shared stack pool on the target; sources compete for the limit.</summary>
    AggregateByTarget,
}

public enum GameplayEffectStackingDurationPolicy
{
    /// <summary>Additional applications do not touch the remaining duration.</summary>
    NeverRefresh,
    /// <summary>Additional applications reset the remaining duration to the full duration.</summary>
    RefreshOnSuccessfulApplication,
}

public enum GameplayEffectStackingPeriodPolicy
{
    NeverReset,
    ResetOnSuccessfulApplication,
    /// <summary>When a stack is removed, reset the period.</summary>
    RefreshOnRemoved,
}

public enum GameplayEffectStackingExpiryPolicy
{
    /// <summary>On expiry the whole stack is removed.</summary>
    ClearEntireStack,
    /// <summary>On expiry one stack is consumed; the GE stays alive while stacks remain (duration re-armed).</summary>
    ClearSingleStackCount,
    /// <summary>Like ClearSingleStackCount, and the period (if any) is reset too.</summary>
    RefreshDuration,
}

/// <summary>What happens to abilities granted by a GE when that GE is removed (doc §4.5.6).</summary>
public enum GameplayEffectAbilityRemovalPolicy
{
    CancelAbilityImmediately,
    RemoveAbilityOnEnd,
    DoNothing,
}

/// <summary>GameplayCue events (doc §4.8.8, UE: <c>EGameplayCueEvent</c>).</summary>
public enum GameplayCueEvent
{
    /// <summary>Fired when a Duration/Infinite GE is added (cue enters "active" state).</summary>
    OnActive,
    /// <summary>Fired once right after OnActive (use the notify actor's own tick for per-frame FX).</summary>
    WhileActive,
    /// <summary>Fired when the cue is removed (GE removed / manual RemoveGameplayCue).</summary>
    Removed,
    /// <summary>Fired by Instant GEs and every periodic tick.</summary>
    Executed,
}

/// <summary>Tags describing why a GameplayEffect application failed.</summary>
public static class GameplayEffectFailTags
{
    public static readonly GameplayTag CustomRequirement = GameplayTag.RequestGameplayTag("GameplayEffect.Fail.CustomRequirement");
    public static readonly GameplayTag Immunity = GameplayTag.RequestGameplayTag("GameplayEffect.Fail.Immunity");
}
