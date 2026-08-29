namespace GasNet;

/// <summary>
/// Tracks all active Duration/Infinite GameplayEffects on one ASC and applies their modifiers to
/// attribute aggregators — equivalent to UE's <c>FActiveGameplayEffectsContainer</c>.
/// Instant GEs are executed immediately and never enter this container.
/// </summary>
public sealed class ActiveGameplayEffectsContainer
{
    private readonly AbilitySystemComponent _owner;
    private readonly List<ActiveGameplayEffect> _effects = [];
    private readonly Dictionary<GameplayAttribute, List<(ActiveGameplayEffect effect, GameplayModifierInfo modifier)>> _dependencies = new();
    private readonly Queue<ActiveGameplayEffect> _deferredRemovals = new();
    private readonly Dictionary<ActiveGameplayEffectHandle, List<(GameplayAbilitySpecHandle spec, GameplayEffectAbilityRemovalPolicy policy)>> _grantedAbilities = new();

    private int _nextHandle = 1;
    private int _applicationOrder;
    private int _batchDepth;
    private int _refreshDepth;
    private bool _removalQueued;

    internal ActiveGameplayEffectsContainer(AbilitySystemComponent owner) => _owner = owner;

    /// <summary>The ASC these effects are active on.</summary>
    public AbilitySystemComponent Owner => _owner;

    public IReadOnlyList<ActiveGameplayEffect> All => _effects;

    public ITimeSource TimeSource => _owner.TimeSource;

    // ------------------------------------------------------------------
    // Application checks (doc §4.5.7, §4.5.8, §4.5.13)
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the tags describing why the spec cannot be applied (empty = can apply).
    /// Sets <paramref name="immunityBlocked"/> when the failure came from the immunity path
    /// (TargetTagRequirements / GrantedApplicationImmunityTags / immunity query).
    /// </summary>
    public GameplayTagContainer CanApplyGameplayEffectSpec(GameplayEffectSpec spec, out bool immunityBlocked)
    {
        immunityBlocked = false;
        var failTags = new GameplayTagContainer();
        var def = spec.Def;

        foreach (var car in def.CustomApplicationRequirements)
        {
            if (!car.CanGameplayEffectApply(this, spec))
                return new GameplayTagContainer(GameplayEffectFailTags.CustomRequirement);
        }

        var ownerTags = _owner.GetOwnedGameplayTagContainer();
        AddUnmetTags(def.ApplicationTagRequirements, ownerTags, failTags);

        if (!def.TargetTagRequirements.RequirementsMet(ownerTags))
        {
            immunityBlocked = true;
            AddUnmetTags(def.TargetTagRequirements, ownerTags, failTags);
        }

        if (def.GrantedApplicationImmunityTags.IsNotEmpty && spec.CapturedSourceTags.HasAny(def.GrantedApplicationImmunityTags))
        {
            immunityBlocked = true;
            failTags.AddTags(def.GrantedApplicationImmunityTags);
        }

        if (def.GrantedApplicationImmunityQuery is { } query)
        {
            var specTags = spec.GetAllAssetTags();
            specTags.AddTags(spec.GetAllGrantedTags());
            if (query.Matches(specTags))
            {
                immunityBlocked = true;
                failTags.AddTag(GameplayTag.RequestGameplayTag("GameplayEffect.Fail.Immunity"));
            }
        }

        return failTags;
    }

    private static void AddUnmetTags(GameplayTagRequirements requirements, GameplayTagContainer tags, GameplayTagContainer failTags)
    {
        foreach (var tag in requirements.RequiredTags.Tags)
            if (!tags.HasTag(tag))
                failTags.AddTag(tag);
        foreach (var tag in requirements.IgnoredTags.Tags)
            if (tags.HasTag(tag))
                failTags.AddTag(tag);
    }

    // ------------------------------------------------------------------
    // Applying (doc §4.5.2)
    // ------------------------------------------------------------------

    /// <summary>
    /// Applies a spec to this ASC. Instant GEs execute immediately and return an invalid handle.
    /// Returns the handle of the new (or refreshed) active effect, or null when blocked.
    /// </summary>
    public ActiveGameplayEffectHandle? ApplyGameplayEffectSpec(GameplayEffectSpec spec)
    {
        var failTags = CanApplyGameplayEffectSpec(spec, out bool immunityBlocked);
        if (immunityBlocked)
        {
            _owner.InvokeOnImmunityBlockGameplayEffect(spec, failTags);
            return null;
        }
        if (!failTags.IsEmpty)
            return null;

        spec.Context.TargetAbilitySystemComponent ??= _owner;
        spec.CapturedTargetTags.Clear();
        spec.CapturedTargetTags.AddTags(_owner.GetOwnedGameplayTagContainer());

        if (spec.Def.IsInstant)
        {
            ExecuteActiveEffectsFrom(spec, isPeriodicTick: false);
            // The engine processes RemoveGameplayEffectsWithTags on every successful application,
            // Instant included (the "dispel potion" pattern): execute first, then remove.
            RunRemoveGameplayEffectsWithTags(spec);
            return null;
        }

        float now = TimeSource.NowSeconds;

        // Stacking (doc §4.5.5)
        if (spec.Def.StackingType != GameplayEffectStackingType.DoNotStack &&
            TryStackOnExistingEffect(spec, now, out var existingHandle))
        {
            RunRemoveGameplayEffectsWithTags(spec);
            return existingHandle;
        }

        var age = CreateActiveEffect(spec, now);
        _effects.Add(age);

        RunRemoveGameplayEffectsWithTags(spec);
        GrantAbilitiesFrom(age);

        if (!age.IsInactive)
            ActivateEffect(age);

        _owner.FireGameplayEffectApplied(spec, age);
        return age.Handle;
    }

    private ActiveGameplayEffect CreateActiveEffect(GameplayEffectSpec spec, float now)
    {
        var age = new ActiveGameplayEffect
        {
            Handle = new ActiveGameplayEffectHandle(_nextHandle++, _owner),
            Spec = spec,
            StartTime = now,
            Duration = spec.Def.IsInfinite ? 0f : ResolveSpecDuration(spec),
            Period = Math.Max(0f, spec.Period),
        };
        if (age.IsPeriodic)
            age.NextExecuteTime = now + age.Period;
        age.TargetTagsAtApplication.AddTags(spec.CapturedTargetTags);
        age.IsInactive = !spec.Def.OngoingTagRequirements.RequirementsMet(_owner.GetOwnedGameplayTagContainer());
        return age;
    }

    /// <summary>Duration GEs whose spec.Duration was left at 0 fall back to a SetByCaller 'Data.Cooldown'
    /// value (the shared-cooldown-GE pattern, doc §4.5.15).</summary>
    private static float ResolveSpecDuration(GameplayEffectSpec spec)
    {
        if (spec.Duration > 0f)
            return spec.Duration;
        var dataTag = GameplayAbilityCooldownTags.DataCooldown;
        if (spec.HasSetByCallerMagnitude(dataTag))
            return Math.Max(0f, spec.GetSetByCallerMagnitude(dataTag, warnIfNotFound: false, defaultIfNotFound: 0f));
        return 0f;
    }

    private bool TryStackOnExistingEffect(GameplayEffectSpec spec, float now, out ActiveGameplayEffectHandle handle)
    {
        handle = default;
        var def = spec.Def;
        var source = spec.Context.InstigatorAbilitySystemComponent;

        foreach (var age in _effects)
        {
            if (!ReferenceEquals(age.Spec.Def, def))
                continue;
            if (def.StackingType == GameplayEffectStackingType.AggregateBySource &&
                !ReferenceEquals(age.Spec.Context.InstigatorAbilitySystemComponent, source))
                continue;

            // Found the stack pool this application belongs to.
            if (age.StackCount < def.StackLimitCount)
            {
                int old = age.StackCount;
                age.StackCount = old + 1;
                age.BroadcastStackChanged(old, age.StackCount);
            }
            // At the stack limit the application is refused, but duration/period policies still run.

            if (def.StackDurationRefreshPolicy == GameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication && age.HasDuration)
            {
                age.StartTime = now;
                age.Duration = Math.Max(0f, spec.Duration);
                age.BroadcastTimeChanged(age.StartTime, age.Duration);
            }
            if (def.StackPeriodResetPolicy == GameplayEffectStackingPeriodPolicy.ResetOnSuccessfulApplication && age.IsPeriodic)
                age.NextExecuteTime = now + age.Period;

            handle = age.Handle;
            return true;
        }
        return false;
    }

    private void RunRemoveGameplayEffectsWithTags(GameplayEffectSpec spec)
    {
        var removeTags = spec.Def.RemoveGameplayEffectsWithTags;
        if (removeTags.IsEmpty)
            return;

        BeginBatch();
        try
        {
            foreach (var age in _effects.ToArray())
            {
                if (age.Spec.GetAllAssetTags().HasAny(removeTags) || age.Spec.GetAllGrantedTags().HasAny(removeTags))
                    RemoveActiveEffectInternal(age, GameplayEffectRemovalReason.Replaced);
            }
        }
        finally { EndBatch(); }
    }

    private void GrantAbilitiesFrom(ActiveGameplayEffect age)
    {
        if (age.Spec.Def.GrantedAbilities.Count == 0)
            return;

        var list = new List<(GameplayAbilitySpecHandle, GameplayEffectAbilityRemovalPolicy)>();
        foreach (var entry in age.Spec.Def.GrantedAbilities)
        {
            var spec = new GameplayAbilitySpec(entry.AbilityType, entry.Level, entry.InputID)
            {
                SourceObject = age.Spec.Context.SourceObject,
            };
            var handle = _owner.GiveAbility(spec);
            if (handle.IsValid)
                list.Add((handle, entry.RemovalPolicy));
        }
        _grantedAbilities[age.Handle] = list;
    }

    private void RevokeGrantedAbilities(ActiveGameplayEffect age)
    {
        if (!_grantedAbilities.Remove(age.Handle, out var list))
            return;
        foreach (var (specHandle, policy) in list)
        {
            switch (policy)
            {
                case GameplayEffectAbilityRemovalPolicy.CancelAbilityImmediately:
                    _owner.CancelAbility(specHandle);
                    _owner.ClearAbility(specHandle);
                    break;
                case GameplayEffectAbilityRemovalPolicy.RemoveAbilityOnEnd:
                    _owner.SetRemoveAbilityOnEnd(specHandle);
                    break;
                case GameplayEffectAbilityRemovalPolicy.DoNothing:
                    break;
            }
        }
    }

    // ------------------------------------------------------------------
    // Modifier plumbing into aggregators
    // ------------------------------------------------------------------

    private void ActivateEffect(ActiveGameplayEffect age)
    {
        _owner.AddTagsToOwned(age.Spec.GetAllGrantedTags());
        AddModsToAggregators(age);
        _owner.FireCueTagsFromSpec(age.Spec, GameplayCueEvent.OnActive);
        _owner.FireCueTagsFromSpec(age.Spec, GameplayCueEvent.WhileActive);
    }

    private void DeactivateEffect(ActiveGameplayEffect age)
    {
        _owner.RemoveTagsFromOwned(age.Spec.GetAllGrantedTags());
        RemoveModsFromAggregators(age);
    }

    internal void AddModsToAggregators(ActiveGameplayEffect age)
    {
        var spec = age.Spec;
        foreach (var modifier in spec.Def.Modifiers)
        {
            if (!ModifierRequirementsMet(modifier, spec))
                continue;

            float magnitude = modifier.Magnitude.Evaluate(spec);
            var aggregator = _owner.GetOrCreateAggregator(modifier.Attribute);
            aggregator.AddMod(modifier.ModifierOp, new AggregatorMod
            {
                Magnitude = magnitude,
                Source = age.Handle,
                ApplicationOrder = ++_applicationOrder,
                SourceTags = spec.CapturedSourceTags.Clone(),
            });
            RegisterDependencies(age, modifier);

            _owner.RecalculateAttributeCurrentValue(modifier.Attribute, spec, age.Handle);
        }
    }

    internal void RemoveModsFromAggregators(ActiveGameplayEffect age)
    {
        var touched = new HashSet<GameplayAttribute>();
        foreach (var modifier in age.Spec.Def.Modifiers)
        {
            if (_owner.TryGetAggregator(modifier.Attribute, out var aggregator) && aggregator.RemoveModsFrom(age.Handle))
                touched.Add(modifier.Attribute);
        }
        UnregisterDependencies(age);
        foreach (var attribute in touched)
            _owner.RecalculateAttributeCurrentValue(attribute, age.Spec, age.Handle);
    }

    private bool ModifierRequirementsMet(GameplayModifierInfo modifier, GameplayEffectSpec spec)
    {
        // Application-time only checks (doc §4.5.4.2); for periodic effects this was evaluated on
        // first application and is intentionally not re-checked per tick.
        if (modifier.SourceTags.IsNotEmpty &&
            (!spec.CapturedSourceTags.HasAll(modifier.SourceTags) || spec.CapturedSourceTags.HasAny(modifier.SourceTags)))
            return false;
        if (modifier.TargetTags.IsNotEmpty &&
            (!spec.CapturedTargetTags.HasAll(modifier.TargetTags) || spec.CapturedTargetTags.HasAny(modifier.TargetTags)))
            return false;
        return true;
    }

    private void RegisterDependencies(ActiveGameplayEffect age, GameplayModifierInfo modifier)
    {
        foreach (var capture in GetModifierCaptures(modifier))
        {
            bool relevant =
                capture.CaptureSource == GameplayAttributeCaptureSource.Target ||
                (capture.CaptureSource == GameplayAttributeCaptureSource.Source &&
                 ReferenceEquals(age.Spec.Context.InstigatorAbilitySystemComponent, _owner));
            if (!relevant || capture.Snapshot)
                continue;

            if (!_dependencies.TryGetValue(capture.Attribute, out var list))
            {
                list = [];
                _dependencies[capture.Attribute] = list;
            }
            if (!list.Contains((age, modifier)))
                list.Add((age, modifier));
        }
    }

    private void UnregisterDependencies(ActiveGameplayEffect age)
    {
        foreach (var key in _dependencies.Keys.ToArray())
        {
            _dependencies[key].RemoveAll(e => ReferenceEquals(e.effect, age));
            if (_dependencies[key].Count == 0)
                _dependencies.Remove(key);
        }
    }

    private static IEnumerable<AttributeCaptureDefinition> GetModifierCaptures(GameplayModifierInfo modifier) =>
        modifier.Magnitude switch
        {
            AttributeBasedMagnitude ab => [ab.Capture],
            CustomCalculationMagnitude cc => cc.Calculation.RelevantAttributesToCapture,
            _ => [],
        };

    /// <summary>
    /// Recaptures all non-snapshot modifiers of <paramref name="age"/> and re-applies them.
    /// Called when a dependency attribute changed or the effect level changed
    /// (equivalent to <c>SetActiveGameplayEffectLevel</c>, doc §4.5.1).
    /// </summary>
    internal void RefreshEffectModifiers(ActiveGameplayEffect age)
    {
        if (_refreshDepth >= 8)
        {
            GasNetLog.Warn($"Attribute modifier refresh recursion exceeded 8 levels on '{age.Spec}'; stopping to avoid oscillation.");
            return;
        }
        _refreshDepth++;
        try
        {
            RemoveModsFromAggregators(age);
            if (!age.IsInactive)
                AddModsToAggregators(age);
        }
        finally { _refreshDepth--; }
    }

    /// <summary>Called by the ASC when the attribute <paramref name="attribute"/> changed on this ASC.</summary>
    internal void UpdateModifiersDependentOn(GameplayAttribute attribute)
    {
        if (!_dependencies.TryGetValue(attribute, out var list) || list.Count == 0)
            return;

        if (_refreshDepth > 0)
            return; // already inside a refresh triggered by this chain

        foreach (var (age, _) in list.ToArray())
        {
            if (_effects.Contains(age))
                RefreshEffectModifiers(age);
        }
    }

    /// <summary>Re-evaluates a modifier's magnitude at the current time (doc §4.5.11 manual recalc recipe).</summary>
    public void SetActiveGameplayEffectLevel(ActiveGameplayEffectHandle handle, float newLevel)
    {
        var age = GetActiveGameplayEffect(handle);
        if (age is null)
            return;
        age.Spec.Level = newLevel;
        RefreshEffectModifiers(age);
    }

    // ------------------------------------------------------------------
    // Instant / periodic execution ("anything with the word Execute", doc §4.5.12)
    // ------------------------------------------------------------------

    internal void ExecuteActiveEffectsFrom(GameplayEffectSpec spec, bool isPeriodicTick)
    {
        foreach (var modifier in spec.Def.Modifiers)
        {
            if (!ModifierRequirementsMet(modifier, spec))
                continue;
            float magnitude = modifier.Magnitude.Evaluate(spec);
            _owner.ExecuteInstantModifier(spec, modifier, magnitude);
        }

        bool cuesHandledManually = false;
        foreach (var execution in spec.Def.Executions)
        {
            var output = new ExecutionOutput();
            var parameters = GameplayEffectCaptureEvaluator.BuildEvaluateParameters(spec);
            execution.Execute(new GameplayEffectExecutionParams
            {
                Spec = spec,
                Source = spec.Context.InstigatorAbilitySystemComponent,
                Target = _owner,
                EvaluateParameters = parameters,
                Output = output,
            });
            cuesHandledManually |= output.GameplayCuesHandledManually;
            foreach (var (attribute, op, magnitude) in output.Modifiers)
                _owner.ExecuteInstantModifier(spec, MakeSyntheticModifier(attribute, op), magnitude);
        }

        if (!cuesHandledManually)
            _owner.FireCueTagsFromSpec(spec, GameplayCueEvent.Executed);
    }

    private static GameplayModifierInfo MakeSyntheticModifier(GameplayAttribute attribute, GameplayModOp op) => new()
    {
        Attribute = attribute,
        ModifierOp = op,
        Magnitude = new ScalableFloatMagnitude(0f),
    };

    // ------------------------------------------------------------------
    // Removal & expiry (doc §4.5.3, §4.5.5)
    // ------------------------------------------------------------------

    public bool RemoveActiveGameplayEffect(ActiveGameplayEffectHandle handle)
    {
        var age = GetActiveGameplayEffect(handle);
        if (age is null)
            return false;
        RemoveActiveEffectInternal(age, GameplayEffectRemovalReason.Manual);
        FlushDeferredRemovals();
        return true;
    }

    /// <summary>Removes every active effect matching <paramref name="query"/>. Returns the number removed.</summary>
    public int RemoveActiveEffects(GameplayEffectQuery query)
    {
        int removed = 0;
        BeginBatch();
        try
        {
            foreach (var age in _effects.ToArray())
            {
                if (query.Matches(age))
                {
                    RemoveActiveEffectInternal(age, GameplayEffectRemovalReason.Manual);
                    removed++;
                }
            }
        }
        finally { EndBatch(); }
        return removed;
    }

    /// <summary>Consumes one stack of a stacked GE (e.g. damage removing an armor stack).
    /// Returns true when a stack was consumed; the GE is removed when the last stack goes.</summary>
    public bool DecrementStack(ActiveGameplayEffectHandle handle)
    {
        var age = GetActiveGameplayEffect(handle);
        if (age is null || age.StackCount <= 0)
            return false;
        ConsumeOneStack(age, GameplayEffectRemovalReason.Manual);
        return true;
    }

    private void ConsumeOneStack(ActiveGameplayEffect age, GameplayEffectRemovalReason reason)
    {
        int old = age.StackCount;
        age.StackCount = Math.Max(0, old - 1);
        age.BroadcastStackChanged(old, age.StackCount);
        if (age.StackCount == 0)
            RemoveActiveEffectInternal(age, reason);
    }

    private void RemoveActiveEffectInternal(ActiveGameplayEffect age, GameplayEffectRemovalReason reason)
    {
        if (_batchDepth > 0)
        {
            _deferredRemovals.Enqueue(age);
            _removalQueued = true;
            return;
        }

        if (!_effects.Remove(age))
            return;

        if (!age.IsInactive)
            DeactivateEffect(age);
        RevokeGrantedAbilities(age);
        UnregisterDependencies(age);

        // Cue Removed only ever fires for Duration/Infinite GEs (they are the only ones Added).
        _owner.FireCueTagsFromSpec(age.Spec, GameplayCueEvent.Removed);

        age.BroadcastRemoved();
        _owner.FireAnyGameplayEffectRemoved(age, reason);
    }

    private void BeginBatch()
    {
        _batchDepth++;
    }

    private void EndBatch()
    {
        _batchDepth--;
        if (_batchDepth == 0 && _removalQueued)
            FlushDeferredRemovals();
    }

    private void FlushDeferredRemovals()
    {
        if (_batchDepth > 0 || !_removalQueued)
            return;
        _removalQueued = false;
        while (_deferredRemovals.TryDequeue(out var age))
            RemoveActiveEffectInternal(age, GameplayEffectRemovalReason.Manual);
    }

    /// <summary>Expires due Duration GEs; consumes stacks per the stack expiry policy (doc §4.5.5).</summary>
    public void CheckDuration(float now)
    {
        BeginBatch();
        try
        {
            foreach (var age in _effects.ToArray())
            {
                if (!age.HasDuration || age.Duration <= 0f)
                    continue;
                if (now < age.StartTime + age.Duration)
                    continue;

                if (age.StackCount > 1)
                {
                    switch (age.Spec.Def.StackExpiryPolicy)
                    {
                        case GameplayEffectStackingExpiryPolicy.ClearEntireStack:
                            RemoveActiveEffectInternal(age, GameplayEffectRemovalReason.Expired);
                            break;

                        case GameplayEffectStackingExpiryPolicy.ClearSingleStackCount:
                        case GameplayEffectStackingExpiryPolicy.RefreshDuration:
                        {
                            int old = age.StackCount;
                            age.StackCount = old - 1;
                            age.BroadcastStackChanged(old, age.StackCount);
                            if (age.StackCount <= 0)
                            {
                                RemoveActiveEffectInternal(age, GameplayEffectRemovalReason.StackExpired);
                            }
                            else
                            {
                                age.StartTime = now;
                                age.Duration = Math.Max(0f, age.Spec.Duration);
                                if (age.Spec.Def.StackExpiryPolicy == GameplayEffectStackingExpiryPolicy.RefreshDuration && age.IsPeriodic)
                                    age.NextExecuteTime = now + age.Period;
                                age.BroadcastTimeChanged(age.StartTime, age.Duration);
                            }
                            break;
                        }
                    }
                }
                else
                {
                    RemoveActiveEffectInternal(age, GameplayEffectRemovalReason.Expired);
                }
            }
        }
        finally { EndBatch(); }
    }

    /// <summary>Executes periodic effects whose next tick is due (each tick behaves like an Instant GE, doc §4.5.1).</summary>
    public void TickPeriodicEffects(float now)
    {
        BeginBatch();
        try
        {
            foreach (var age in _effects.ToArray())
            {
                if (!age.IsPeriodic || age.IsInactive)
                    continue;

                float lifetimeEnd = age.HasDuration && age.Duration > 0 ? age.StartTime + age.Duration : float.MaxValue;
                while (age.NextExecuteTime <= now && age.NextExecuteTime <= lifetimeEnd)
                {
                    age.ExecutedPeriodCount++;
                    ExecuteActiveEffectsFrom(age.Spec, isPeriodicTick: true);
                    age.BroadcastPeriod();
                    age.NextExecuteTime += age.Period;
                }
            }
        }
        finally { EndBatch(); }
    }

    // ------------------------------------------------------------------
    // Ongoing tag requirements (doc §4.5.7)
    // ------------------------------------------------------------------

    /// <summary>Re-evaluates Ongoing Tag Requirements for every active effect (called on tag changes).</summary>
    public void UpdateOngoingTagRequirements()
    {
        BeginBatch();
        try
        {
            foreach (var age in _effects.ToArray())
            {
                var requirements = age.Spec.Def.OngoingTagRequirements;
                if (requirements.IsEmpty)
                    continue;

                bool shouldBeInactive = !requirements.RequirementsMet(_owner.GetOwnedGameplayTagContainer());
                if (shouldBeInactive == age.IsInactive)
                    continue;

                age.IsInactive = shouldBeInactive;
                if (shouldBeInactive)
                    DeactivateEffect(age);
                else
                    ActivateEffect(age);
                age.BroadcastOngoingRequirementsChanged(!shouldBeInactive);
            }
        }
        finally { EndBatch(); }
    }

    // ------------------------------------------------------------------
    // Queries (doc §4.5.15.1 and friends)
    // ------------------------------------------------------------------

    public ActiveGameplayEffect? GetActiveGameplayEffect(ActiveGameplayEffectHandle handle) =>
        handle.IsValid ? _effects.FirstOrDefault(e => e.Handle == handle) : null;

    public int GetGameplayEffectCount(GameplayEffectQuery query)
    {
        if (query.IsEmpty)
            return _effects.Count;
        return _effects.Count(query.Matches);
    }

    public int GetGameplayEffectCount(GameplayEffectDefinition def) =>
        _effects.Count(e => ReferenceEquals(e.Spec.Def, def));

    public List<ActiveGameplayEffect> GetActiveEffectsWithAllTags(GameplayTagContainer tags)
    {
        var result = new List<ActiveGameplayEffect>();
        foreach (var age in _effects)
            if (age.Spec.GetAllGrantedTags().HasAll(tags))
                result.Add(age);
        return result;
    }

    public List<ActiveGameplayEffect> GetActiveEffects(GameplayEffectQuery query) =>
        _effects.Where(query.Matches).ToList();

    public IEnumerable<(float timeRemaining, float duration)> GetActiveEffectsTimeRemainingAndDuration(GameplayEffectQuery query)
    {
        float now = TimeSource.NowSeconds;
        foreach (var age in _effects)
        {
            if (!query.Matches(age))
                continue;
            float duration = age.HasDuration ? age.Duration : 0f;
            yield return (age.GetTimeRemaining(now), duration);
        }
    }

    public float GetActiveEffectsTimeRemaining(GameplayEffectQuery query) =>
        GetActiveEffectsTimeRemainingAndDuration(query).Select(t => t.timeRemaining).DefaultIfEmpty(0f).Max();

    public void ClearAllEffects()
    {
        BeginBatch();
        try
        {
            foreach (var age in _effects.ToArray())
                RemoveActiveEffectInternal(age, GameplayEffectRemovalReason.Manual);
        }
        finally { EndBatch(); }
    }
}
