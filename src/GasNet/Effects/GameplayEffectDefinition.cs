namespace GasNet;

/// <summary>A single modifier on a GE definition (UE: <c>FGameplayModifierInfo</c>).</summary>
public sealed class GameplayModifierInfo
{
    public required GameplayAttribute Attribute { get; set; }
    public GameplayModOp ModifierOp { get; set; } = GameplayModOp.Add;
    public required GameplayEffectModifierMagnitude Magnitude { get; set; }

    /// <summary>Application-time tag requirements on the SOURCE (doc §4.5.4.2).</summary>
    public GameplayTagContainer SourceTags { get; set; } = new();
    /// <summary>Application-time tag requirements on the TARGET (doc §4.5.4.2). Evaluated on first application only (incl. periodic).</summary>
    public GameplayTagContainer TargetTags { get; set; } = new();
}

/// <summary>An ability granted while a Duration/Infinite GE is active (doc §4.5.6).</summary>
public sealed class GrantedAbilityEntry
{
    public required Type AbilityType { get; set; }
    public float Level { get; set; } = 1f;
    public int InputID { get; set; }
    public GameplayEffectAbilityRemovalPolicy RemovalPolicy { get; set; } = GameplayEffectAbilityRemovalPolicy.CancelAbilityImmediately;
}

/// <summary>Advanced apply/deny hook (doc §4.5.13, UE: <c>UGameplayEffectCustomApplicationRequirement</c>).</summary>
public abstract class GameplayEffectCustomApplicationRequirement
{
    public abstract bool CanGameplayEffectApply(ActiveGameplayEffectsContainer activeEffectsContainer, GameplayEffectSpec spec);
}

/// <summary>
/// Data-only definition of a GameplayEffect — equivalent to UE's <c>UGameplayEffect</c> subclass
/// (the "CDO"/archetype). Spec instances customize level/duration/SetByCallers at runtime.
/// </summary>
public class GameplayEffectDefinition
{
    // ---- Duration / periodicity (doc §4.5.1, §4.5.12) ----
    public GameplayEffectDurationType DurationPolicy { get; set; } = GameplayEffectDurationType.Instant;
    public float Duration { get; set; }
    /// <summary>&gt; 0 on Duration/Infinite GEs makes the effect periodic; each tick executes like an Instant GE.</summary>
    public float Period { get; set; }

    // ---- Changes (doc §4.5.4, §4.5.12) ----
    public List<GameplayModifierInfo> Modifiers { get; set; } = [];
    /// <summary>Executions (Instant/Periodic only — "anything with the word Execute", doc §4.5.12).</summary>
    public List<GameplayEffectExecutionCalculation> Executions { get; set; } = [];

    // ---- Tags (doc §4.5.7) ----
    /// <summary>Describe the GE; matched by <see cref="RemoveGameplayEffectsWithTags"/> and queries.</summary>
    public GameplayTagContainer AssetTags { get; set; } = new();
    /// <summary>Granted to the target ASC while the GE is active; removed with the GE. Duration/Infinite only.</summary>
    public GameplayTagContainer GrantedTags { get; set; } = new();
    /// <summary>Cue tags (must live under "GameplayCue.") auto-fired on successful application.</summary>
    public GameplayTagContainer GameplayCueTags { get; set; } = new();
    /// <summary>After application, failing these turns the GE "off" (mods + tags removed but GE stays); met again → back on.</summary>
    public GameplayTagRequirements OngoingTagRequirements { get; set; } = new();
    /// <summary>Checked against the TARGET's tags; failing blocks application.</summary>
    public GameplayTagRequirements ApplicationTagRequirements { get; set; } = new();
    /// <summary>Checked against the TARGET's tags; failing counts as IMMUNITY (fires the immunity delegate + FX).</summary>
    public GameplayTagRequirements TargetTagRequirements { get; set; } = new();
    /// <summary>On successful application, removes target GEs whose Asset OR Granted tags match any of these.</summary>
    public GameplayTagContainer RemoveGameplayEffectsWithTags { get; set; } = new();

    // ---- Immunity (doc §4.5.8) ----
    /// <summary>Blocks GEs whose SOURCE ASC (incl. source ability's AbilityTags) has any of these tags.</summary>
    public GameplayTagContainer GrantedApplicationImmunityTags { get; set; } = new();
    /// <summary>Blocks GEs whose spec asset+granted tags match this query.</summary>
    public GameplayTagQuery? GrantedApplicationImmunityQuery { get; set; }

    // ---- Stacking (doc §4.5.5) ----
    public GameplayEffectStackingType StackingType { get; set; } = GameplayEffectStackingType.DoNotStack;
    public int StackLimitCount { get; set; } = 1;
    public GameplayEffectStackingDurationPolicy StackDurationRefreshPolicy { get; set; } = GameplayEffectStackingDurationPolicy.NeverRefresh;
    public GameplayEffectStackingPeriodPolicy StackPeriodResetPolicy { get; set; } = GameplayEffectStackingPeriodPolicy.NeverReset;
    public GameplayEffectStackingExpiryPolicy StackExpiryPolicy { get; set; } = GameplayEffectStackingExpiryPolicy.ClearEntireStack;

    // ---- Granted abilities (doc §4.5.6) ----
    public List<GrantedAbilityEntry> GrantedAbilities { get; set; } = [];

    // ---- Custom application requirements (doc §4.5.13) ----
    public List<GameplayEffectCustomApplicationRequirement> CustomApplicationRequirements { get; set; } = [];

    public bool IsInstant => DurationPolicy == GameplayEffectDurationType.Instant;
    public bool IsPeriodic => !IsInstant && Period > 0f;
    public bool IsInfinite => DurationPolicy == GameplayEffectDurationType.Infinite;
    public bool HasDuration => DurationPolicy == GameplayEffectDurationType.HasDuration;

    /// <summary>Creates an independent copy (useful as a template for dynamic GEs).</summary>
    public GameplayEffectDefinition Clone()
    {
        var clone = (GameplayEffectDefinition)MemberwiseClone();
        clone.AssetTags = AssetTags.Clone();
        clone.GrantedTags = GrantedTags.Clone();
        clone.GameplayCueTags = GameplayCueTags.Clone();
        clone.RemoveGameplayEffectsWithTags = RemoveGameplayEffectsWithTags.Clone();
        clone.GrantedApplicationImmunityTags = GrantedApplicationImmunityTags.Clone();
        clone.Modifiers = [.. Modifiers];
        clone.Executions = [.. Executions];
        clone.GrantedAbilities = [.. GrantedAbilities];
        clone.CustomApplicationRequirements = [.. CustomApplicationRequirements];
        return clone;
    }
}

/// <summary>Fluent builder helpers for authoring GameplayEffect definitions in code (stands in for Blueprint-authored GE classes).</summary>
public static class GameplayEffectDefinitionBuilder
{
    public static GameplayEffectDefinition With(this GameplayEffectDefinition def,
        GameplayEffectDurationType policy = GameplayEffectDurationType.Instant,
        float duration = 0f, float period = 0f)
    {
        def.DurationPolicy = policy;
        def.Duration = duration;
        def.Period = period;
        return def;
    }

    public static GameplayEffectDefinition WithAssetTags(this GameplayEffectDefinition def, params GameplayTag[] tags)
    {
        foreach (var t in tags) def.AssetTags.AddTag(t);
        return def;
    }

    public static GameplayEffectDefinition WithGrantedTags(this GameplayEffectDefinition def, params GameplayTag[] tags)
    {
        foreach (var t in tags) def.GrantedTags.AddTag(t);
        return def;
    }

    public static GameplayEffectDefinition WithCueTags(this GameplayEffectDefinition def, params GameplayTag[] tags)
    {
        foreach (var t in tags) def.GameplayCueTags.AddTag(t);
        return def;
    }

    public static GameplayEffectDefinition WithStacking(this GameplayEffectDefinition def,
        GameplayEffectStackingType type, int stackLimit)
    {
        def.StackingType = type;
        def.StackLimitCount = stackLimit;
        return def;
    }

    public static GameplayEffectDefinition WithExecutions(this GameplayEffectDefinition def, params GameplayEffectExecutionCalculation[] executions)
    {
        def.Executions.AddRange(executions);
        return def;
    }

    public static GameplayEffectDefinition WithCustomApplicationRequirements(this GameplayEffectDefinition def, params GameplayEffectCustomApplicationRequirement[] requirements)
    {
        def.CustomApplicationRequirements.AddRange(requirements);
        return def;
    }

    public static GameplayEffectDefinition AddModifier(this GameplayEffectDefinition def,
        GameplayAttribute attribute, GameplayModOp op, GameplayEffectModifierMagnitude magnitude)
    {
        def.Modifiers.Add(new GameplayModifierInfo { Attribute = attribute, ModifierOp = op, Magnitude = magnitude });
        return def;
    }
}
