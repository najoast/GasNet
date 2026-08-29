namespace GasNet;

/// <summary>
/// Data passed to <see cref="AttributeSet.PostGameplayEffectExecute"/> after an Instant GE (or
/// periodic tick) modified a BaseValue — equivalent to UE's <c>FGameplayEffectModCallbackData</c>.
/// </summary>
public sealed class GameplayEffectModCallbackData
{
    public required GameplayEffectSpec EffectSpec { get; init; }
    public required GameplayModifierInfo Modifier { get; init; }
    public required GameplayAttribute Attribute { get; init; }
    /// <summary>The evaluated magnitude that was applied to the BaseValue.</summary>
    public float EvaluatedMagnitude { get; init; }
}

/// <summary>Data broadcast on attribute value changes (UE: <c>FOnAttributeChangeData</c>).</summary>
public sealed class OnAttributeChangeData
{
    public required GameplayAttribute Attribute { get; init; }
    public float OldValue { get; init; }
    public float NewValue { get; init; }
    /// <summary>The GE that caused the change, when the change came from a GameplayEffect; null otherwise (server-side info).</summary>
    public GameplayEffectSpec? EffectSpec { get; init; }
    public ActiveGameplayEffectHandle EffectHandle { get; init; }
}

/// <summary>Per-attribute change event (returned by <see cref="AbilitySystemComponent.GetGameplayAttributeValueChangeDelegate"/>).</summary>
public sealed class GameplayAttributeChangedEvent
{
    private Action<OnAttributeChangeData>? _invocation;

    public event Action<OnAttributeChangeData> Handler
    {
        add => _invocation += value;
        remove => _invocation -= value;
    }

    internal void Invoke(OnAttributeChangeData data) => _invocation?.Invoke(data);
}

/// <summary>
/// Base class for attribute sets — equivalent to UE's <c>UAttributeSet</c>.
/// Attributes are public fields of type <see cref="GameplayAttributeData"/> and are auto-registered.
/// </summary>
public abstract class AttributeSet
{
    /// <summary>Set by the ASC when the set is registered.</summary>
    public AbilitySystemComponent? Owner { get; internal set; }

    public IReadOnlyList<GameplayAttribute> Attributes =>
        GameplayAttributeRegistry.GetAttributes(GetType());

    public GameplayAttribute GetAttribute(string name)
    {
        if (!GameplayAttributeRegistry.TryGetAttribute(GetType(), name, out var attribute))
            throw new ArgumentException($"'{GetType().Name}' has no attribute named '{name}'.");
        return attribute;
    }

    /// <summary>Clamp hook for <b>CurrentValue</b> changes. Only clamps the queried value; never permanently
    /// modifies the underlying modifiers (doc §4.4.5). Use the attribute-change delegate for gameplay reactions.</summary>
    public virtual void PreAttributeChange(GameplayAttribute attribute, ref float newValue) { }

    /// <summary>Clamp hook for <b>BaseValue</b> changes (engine's <c>PreAttributeBaseChange</c>).</summary>
    public virtual void PreAttributeBaseChange(GameplayAttribute attribute, ref float newValue) { }

    /// <summary>Fires after <see cref="PreAttributeChange"/> when CurrentValue actually changed.</summary>
    public virtual void PostAttributeChange(GameplayAttribute attribute, float oldValue, float newValue) { }

    /// <summary>
    /// Fires ONLY after an Instant GE (or a periodic tick) changed a BaseValue (doc §4.4.6).
    /// The intended place to redistribute meta attributes (e.g. Damage → Health) and to clamp
    /// BaseValues against Max counterparts.
    /// </summary>
    public virtual void PostGameplayEffectExecute(GameplayEffectModCallbackData data) { }

    /// <summary>Per-attribute aggregator customization hook (doc §4.4.7), e.g. most-negative-mod qualifiers.</summary>
    public virtual void OnAttributeAggregatorCreated(GameplayAttribute attribute, AttributeAggregator aggregator) { }
}
