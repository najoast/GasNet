namespace GasNet;

/// <summary>A single modifier contribution inside an aggregator channel (engine: <c>FAggregatorMod</c>).</summary>
public sealed class AggregatorMod
{
    public required float Magnitude { get; init; }
    public required ActiveGameplayEffectHandle Source { get; init; }
    /// <summary>Monotonic application counter used for Override "last applied wins".</summary>
    public required int ApplicationOrder { get; init; }
    /// <summary>Source ASC tags captured at spec creation (for capture filters).</summary>
    public GameplayTagContainer SourceTags { get; init; } = new();
    /// <summary>Target ASC tags captured at application (for capture filters).</summary>
    public GameplayTagContainer TargetTags { get; } = new();
}

/// <summary>
/// Per-attribute modifier qualification policy (doc §4.4.7, engine <c>FAggregatorEvaluateMetaData</c>).
/// Non-qualifying mods stay registered but are excluded from evaluation and can re-qualify later.
/// </summary>
public sealed class AggregatorEvaluateMetaData
{
    /// <summary>Receives all candidate mods of one channel and returns the qualifying subset.</summary>
    public required Func<IReadOnlyList<AggregatorMod>, IReadOnlyList<AggregatorMod>> Qualifier { get; init; }

    public static AggregatorEvaluateMetaData Default { get; } = new()
    {
        Qualifier = static mods => mods,
    };

    /// <summary>Paragon-style move-speed policy: all positive mods qualify, only the single most-negative qualifies.</summary>
    public static AggregatorEvaluateMetaData MostNegativeMod_AllPositiveMods { get; } = new()
    {
        Qualifier = static mods =>
        {
            if (mods.Count == 0)
                return mods;
            float mostNegative = float.MaxValue;
            bool anyNegative = false;
            foreach (var m in mods)
            {
                if (m.Magnitude < 0)
                {
                    anyNegative = true;
                    if (m.Magnitude < mostNegative)
                        mostNegative = m.Magnitude;
                }
            }
            if (!anyNegative)
                return mods;
            var result = new List<AggregatorMod>();
            foreach (var m in mods)
            {
                if (m.Magnitude >= 0 || m.Magnitude == mostNegative)
                    result.Add(m);
            }
            return result;
        },
    };

    /// <summary>
    /// Slow-stacking policy for multiplier-based slows (doc §5.7 "only the greatest magnitude applies"):
    /// modifiers of magnitude &gt;= 1 all qualify; among weakening modifiers (&lt; 1) only the single
    /// strongest (lowest) qualifies. Use for Multiply/Divide move-speed slows; for Add-type negative
    /// slows use <see cref="MostNegativeMod_AllPositiveMods"/>.
    /// </summary>
    public static AggregatorEvaluateMetaData OnlyStrongestSlow_AllOtherMods { get; } = new()
    {
        Qualifier = static mods =>
        {
            if (mods.Count == 0)
                return mods;
            float strongestSlow = 1f;
            bool anySlow = false;
            foreach (var m in mods)
            {
                if (m.Magnitude < 1f)
                {
                    anySlow = true;
                    if (m.Magnitude < strongestSlow)
                        strongestSlow = m.Magnitude;
                }
            }
            if (!anySlow)
                return mods;
            var result = new List<AggregatorMod>();
            foreach (var m in mods)
            {
                if (m.Magnitude >= 1f || m.Magnitude == strongestSlow)
                    result.Add(m);
            }
            return result;
        },
    };
}

/// <summary>
/// Aggregates all modifiers from active GameplayEffects for one attribute — equivalent to UE's
/// <c>FAggregator</c>. Evaluation formula (doc §4.5.4):
/// <c>((Base + ΣAdd) * MultSum) / DivSum</c>, where MultSum/DivSum are bias-1 sums
/// (<c>Σ mags − N + 1</c>, i.e. two 1.5 multipliers give ×2.0, not ×2.25); Override mods applied
/// last, with the last-applied taking precedence.
/// </summary>
public sealed class AttributeAggregator
{
    private readonly List<AggregatorMod>[] _mods = new List<AggregatorMod>[4];

    public AttributeAggregator()
    {
        for (int i = 0; i < _mods.Length; i++)
            _mods[i] = [];
    }

    public AggregatorEvaluateMetaData EvaluationMetaData { get; set; } = AggregatorEvaluateMetaData.Default;

    public IReadOnlyList<AggregatorMod> GetMods(GameplayModOp op) => _mods[(int)op];

    public void AddMod(GameplayModOp op, AggregatorMod mod) => _mods[(int)op].Add(mod);

    /// <summary>Removes every mod contributed by <paramref name="handle"/>. Returns true if anything was removed.</summary>
    public bool RemoveModsFrom(ActiveGameplayEffectHandle handle)
    {
        bool removed = false;
        for (int i = 0; i < _mods.Length; i++)
            removed |= _mods[i].RemoveAll(m => m.Source == handle) > 0;
        return removed;
    }

    public bool HasModsFrom(ActiveGameplayEffectHandle handle) =>
        _mods.Any(channel => channel.Any(m => m.Source == handle));

    /// <summary>Replaces the magnitude of every mod sourced from <paramref name="handle"/> (level/recalc updates).</summary>
    public bool UpdateModMagnitudes(ActiveGameplayEffectHandle handle, Func<GameplayModOp, float, float> remap)
    {
        bool changed = false;
        for (int i = 0; i < _mods.Length; i++)
        {
            for (int j = 0; j < _mods[i].Count; j++)
            {
                if (_mods[i][j].Source != handle)
                    continue;
                var old = _mods[i][j];
                float newMagnitude = remap((GameplayModOp)i, old.Magnitude);
                if (!float.IsNaN(newMagnitude) && Math.Abs(newMagnitude - old.Magnitude) > 1e-6f)
                {
                    _mods[i][j] = new AggregatorMod
                    {
                        Magnitude = newMagnitude,
                        Source = old.Source,
                        ApplicationOrder = old.ApplicationOrder,
                        SourceTags = old.SourceTags,
                    };
                    changed = true;
                }
            }
        }
        return changed;
    }

    private float SumChannel(GameplayModOp op, float bias,
        GameplayTagContainer? sourceTagFilter, GameplayTagContainer? targetTagFilter)
    {
        var channel = _mods[(int)op];
        List<AggregatorMod>? filtered = null;
        if (sourceTagFilter is { IsNotEmpty: true } || targetTagFilter is { IsNotEmpty: true })
        {
            filtered = [];
            foreach (var m in channel)
            {
                if (sourceTagFilter is { IsNotEmpty: true } && !m.SourceTags.HasAny(sourceTagFilter))
                    continue; // mod's source had NONE of the filter tags → excluded
                if (targetTagFilter is { IsNotEmpty: true } && !m.TargetTags.HasAny(targetTagFilter))
                    continue;
                filtered.Add(m);
            }
        }
        var qualifying = (filtered ?? channel).Count == 0
            ? (IReadOnlyList<AggregatorMod>)[]
            : EvaluationMetaData.Qualifier(filtered ?? channel);

        if (qualifying.Count == 0)
            return bias;

        // Bias-1 sum: Sum = Bias; for each mod Sum += (Mag - Bias). Add uses bias 0 (plain sum);
        // Multiply/Divide use bias 1 (Σ mags − N + 1), matching engine SumMods.
        float sum = bias;
        foreach (var m in qualifying)
            sum += m.Magnitude - bias;
        return sum;
    }

    /// <summary>
    /// Evaluates CurrentValue from <paramref name="baseValue"/> plus all qualifying modifiers.
    /// Optionally applies source/target tag filters (used by attribute captures,
    /// doc §4.5.4.2). Division by zero is guarded by skipping the division.
    /// </summary>
    public float Evaluate(float baseValue, GameplayTagContainer? sourceTagFilter = null, GameplayTagContainer? targetTagFilter = null)
    {
        float additive = SumChannel(GameplayModOp.Add, 0f, sourceTagFilter, targetTagFilter);
        float multiplicitive = SumChannel(GameplayModOp.Multiply, 1f, sourceTagFilter, targetTagFilter);
        float division = SumChannel(GameplayModOp.Divide, 1f, sourceTagFilter, targetTagFilter);

        float value = (baseValue + additive) * multiplicitive;
        if (division != 1f)
            value /= division;

        // Override mods apply last; last applied takes precedence.
        var overrides = _mods[(int)GameplayModOp.Override];
        if (overrides.Count > 0)
        {
            var last = overrides[0];
            for (int i = 1; i < overrides.Count; i++)
                if (overrides[i].ApplicationOrder >= last.ApplicationOrder)
                    last = overrides[i];
            value = last.Magnitude;
        }
        return value;
    }
}
