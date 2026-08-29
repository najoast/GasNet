namespace GasNet;

public sealed partial class AbilitySystemComponent
{
    private readonly List<AttributeSet> _spawnedAttributes = [];
    private readonly Dictionary<GameplayAttribute, AttributeAggregator> _aggregators = new();
    private readonly Dictionary<GameplayAttribute, GameplayAttributeChangedEvent> _attributeEvents = new();
    private int _attributeRecalcDepth;

    public IReadOnlyList<AttributeSet> SpawnedAttributes => _spawnedAttributes;

    /// <summary>Registers a new attribute set (UE: <c>AddSet&lt;T&gt;</c>). Only one instance per set class is allowed (doc §4.4.2).</summary>
    public T AddSet<T>() where T : AttributeSet, new() => AddSetInstance(new T());

    public TSet AddSetInstance<TSet>(TSet set) where TSet : AttributeSet
    {
        if (_spawnedAttributes.FirstOrDefault(s => s.GetType() == set.GetType()) is { } existing)
        {
            GasNetLog.Warn($"An AttributeSet of type {set.GetType().Name} is already registered; attribute lookups take the first instance.");
            return (TSet)existing;
        }
        set.Owner = this;
        _spawnedAttributes.Add(set);
        return set;
    }

    public bool RemoveAttributeSet(AttributeSet set) => _spawnedAttributes.Remove(set);

    public T? GetSet<T>() where T : AttributeSet => _spawnedAttributes.OfType<T>().FirstOrDefault();

    public bool HasAttributeSetForAttribute(GameplayAttribute attribute) => FindSet(attribute) != null;

    private AttributeSet? FindSet(GameplayAttribute attribute) =>
        _spawnedAttributes.FirstOrDefault(s => attribute.AttributeSetType.IsInstanceOfType(s));

    // ---------------- Aggregators ----------------

    public AttributeAggregator GetOrCreateAggregator(GameplayAttribute attribute)
    {
        if (_aggregators.TryGetValue(attribute, out var aggregator))
            return aggregator;
        aggregator = new AttributeAggregator();
        _aggregators[attribute] = aggregator;
        FindSet(attribute)?.OnAttributeAggregatorCreated(attribute, aggregator);
        return aggregator;
    }

    internal bool TryGetAggregator(GameplayAttribute attribute, out AttributeAggregator aggregator) =>
        _aggregators.TryGetValue(attribute, out aggregator!);

    /// <summary>Capture-path evaluation: aggregates CurrentValue from mods WITHOUT running
    /// <see cref="AttributeSet.PreAttributeChange"/> — re-clamp inside MMCs/ExecCalcs (doc §4.5.11).</summary>
    public float EvaluateAttributeAggregated(GameplayAttribute attribute,
        GameplayTagContainer? sourceTagFilter = null, GameplayTagContainer? targetTagFilter = null)
    {
        var set = FindSet(attribute) ?? throw new InvalidOperationException(
            $"Cannot evaluate '{attribute}': no matching AttributeSet registered on this ASC.");
        return GetOrCreateAggregator(attribute).Evaluate(attribute.GetData(set).BaseValue, sourceTagFilter, targetTagFilter);
    }

    // ---------------- Numeric accessors (doc §9.4) ----------------

    public float GetNumericAttribute(GameplayAttribute attribute)
    {
        var set = RequireSet(attribute);
        return attribute.GetData(set).CurrentValue;
    }

    public float GetNumericAttributeBase(GameplayAttribute attribute)
    {
        var set = RequireSet(attribute);
        return attribute.GetData(set).BaseValue;
    }

    /// <summary>
    /// Sets a BaseValue directly. Existing active modifiers are NOT cleared and act on the new base (doc §9.4).
    /// </summary>
    public void SetNumericAttributeBase(GameplayAttribute attribute, float newBaseValue)
    {
        var set = RequireSet(attribute);
        set.PreAttributeBaseChange(attribute, ref newBaseValue);
        var data = attribute.GetData(set);
        data.BaseValue = newBaseValue;
        attribute.SetData(set, data);
        RecalculateAttributeCurrentValue(attribute, null, default);
    }

    /// <summary>Initializes both BaseValue and CurrentValue (engine: <c>Init*</c> accessors). No mods are active yet.</summary>
    public void InitAttributeValue(GameplayAttribute attribute, float value)
    {
        var set = RequireSet(attribute);
        attribute.SetData(set, new GameplayAttributeData(value, value));
    }

    private AttributeSet RequireSet(GameplayAttribute attribute) => FindSet(attribute) ?? throw new InvalidOperationException(
        $"No AttributeSet for '{attribute}' registered on this ASC. Call AddSet<{attribute.AttributeSetType.Name}>() first.");

    // ---------------- Change events (doc §4.3.4) ----------------

    /// <summary>Subscribes to changes of one attribute's CurrentValue (UE: <c>GetGameplayAttributeValueChangeDelegate</c>).</summary>
    public GameplayAttributeChangedEvent GetGameplayAttributeValueChangeDelegate(GameplayAttribute attribute)
    {
        if (!_attributeEvents.TryGetValue(attribute, out var evt))
        {
            evt = new GameplayAttributeChangedEvent();
            _attributeEvents[attribute] = evt;
        }
        return evt;
    }

    // ---------------- Value pipeline ----------------

    /// <summary>
    /// Recomputes CurrentValue from BaseValue through the aggregator, applies
    /// <see cref="AttributeSet.PreAttributeChange"/> clamping, fires the change event, then
    /// re-evaluates modifiers that captured this attribute (derived attributes, doc §4.3.5).
    /// </summary>
    internal void RecalculateAttributeCurrentValue(GameplayAttribute attribute, GameplayEffectSpec? spec, ActiveGameplayEffectHandle handle)
    {
        var set = FindSet(attribute);
        if (set is null)
            return;

        var data = attribute.GetData(set);
        float newValue = GetOrCreateAggregator(attribute).Evaluate(data.BaseValue);
        set.PreAttributeChange(attribute, ref newValue);

        if (Math.Abs(newValue - data.CurrentValue) <= 1e-6f)
            return;

        float oldValue = data.CurrentValue;
        data.CurrentValue = newValue;
        attribute.SetData(set, data);

        set.PostAttributeChange(attribute, oldValue, newValue);
        GetGameplayAttributeValueChangeDelegate(attribute).Invoke(new OnAttributeChangeData
        {
            Attribute = attribute,
            OldValue = oldValue,
            NewValue = newValue,
            EffectSpec = spec,
            EffectHandle = handle,
        });

        if (_attributeRecalcDepth >= 16)
        {
            GasNetLog.Warn($"Attribute dependency recalculation exceeded 16 levels at '{attribute}'; stopping to avoid infinite loops.");
            return;
        }
        _attributeRecalcDepth++;
        try
        {
            ActiveGameplayEffects.UpdateModifiersDependentOn(attribute);
        }
        finally { _attributeRecalcDepth--; }
    }

    /// <summary>
    /// Applies one Instant-style modifier to a BaseValue (Instant GEs and periodic ticks; doc §4.3.2),
    /// then runs <see cref="AttributeSet.PostGameplayEffectExecute"/> and recomputes CurrentValue.
    /// </summary>
    internal void ExecuteInstantModifier(GameplayEffectSpec spec, GameplayModifierInfo modifier, float magnitude)
    {
        var set = FindSet(modifier.Attribute);
        if (set is null)
        {
            GasNetLog.Warn($"Instant modifier target '{modifier.Attribute}' has no AttributeSet on this ASC; skipped.");
            return;
        }

        var data = modifier.Attribute.GetData(set);
        float newBase = ApplyModifierOp(data.BaseValue, modifier.ModifierOp, magnitude);
        set.PreAttributeBaseChange(modifier.Attribute, ref newBase);
        data.BaseValue = newBase;
        modifier.Attribute.SetData(set, data);

        set.PostGameplayEffectExecute(new GameplayEffectModCallbackData
        {
            EffectSpec = spec,
            Modifier = modifier,
            Attribute = modifier.Attribute,
            EvaluatedMagnitude = magnitude,
        });

        RecalculateAttributeCurrentValue(modifier.Attribute, spec, default);
    }

    /// <summary>Instant execution applies ops directly to BaseValue, one modifier at a time (engine ExecuteMod).</summary>
    private static float ApplyModifierOp(float value, GameplayModOp op, float magnitude) => op switch
    {
        GameplayModOp.Add => value + magnitude,
        GameplayModOp.Multiply => value * magnitude,
        GameplayModOp.Divide => magnitude != 0f ? value / magnitude : value,
        GameplayModOp.Override => magnitude,
        _ => value,
    };
}
