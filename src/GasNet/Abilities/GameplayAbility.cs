namespace GasNet;

/// <summary>
/// An action or skill - equivalent to UE's <c>UGameplayAbility</c>. Subclass and override
/// <see cref="ActivateAbility"/>; always call <see cref="EndAbility"/> when done (doc 4.6).
/// One object plays two roles: the designer-authored "prototype" (like the CDO) and, when
/// instanced, a per-actor/per-execution runtime instance created via <see cref="Clone"/>.
/// </summary>
public abstract class GameplayAbility
{
    // ---------------- Designer configuration (doc 4.6.9, 4.6.7, 4.6.8) ----------------

    /// <summary>Tags this ability owns (identity; matched by other abilities' Cancel/Block containers).</summary>
    public GameplayTagContainer AbilityTags { get; set; } = new();

    /// <summary>On activation, cancels other active abilities whose AbilityTags match these.</summary>
    public GameplayTagContainer CancelAbilitiesWithTag { get; set; } = new();

    /// <summary>While active, other abilities whose AbilityTags match these cannot activate.</summary>
    public GameplayTagContainer BlockAbilitiesWithTag { get; set; } = new();

    /// <summary>Granted to the owner ASC while active (not replicated in UE).</summary>
    public GameplayTagContainer ActivationOwnedTags { get; set; } = new();

    /// <summary>Owner must have ALL of these to activate.</summary>
    public GameplayTagContainer ActivationRequiredTags { get; set; } = new();

    /// <summary>Owner must have NONE of these to activate.</summary>
    public GameplayTagContainer ActivationBlockedTags { get; set; } = new();

    /// <summary>Only evaluated when activated via gameplay event: the event's Instigator must satisfy these.</summary>
    public GameplayTagContainer SourceRequiredTags { get; set; } = new();
    public GameplayTagContainer SourceBlockedTags { get; set; } = new();

    /// <summary>Only evaluated when activated via gameplay event: the event's Target must satisfy these.</summary>
    public GameplayTagContainer TargetRequiredTags { get; set; } = new();
    public GameplayTagContainer TargetBlockedTags { get; set; } = new();

    public GameplayAbilityInstancingPolicy InstancingPolicy { get; set; } = GameplayAbilityInstancingPolicy.InstancedPerActor;
    /// <summary>Kept for parity with UE; GasNet is authoritative-only so this is informational.</summary>
    public GameplayAbilityNetExecutionPolicy NetExecutionPolicy { get; set; } = GameplayAbilityNetExecutionPolicy.ServerOnly;

    /// <summary>Passives: activate automatically when granted (OnAvatarSet, doc 4.6.4.1).</summary>
    public bool bActivateAbilityOnGranted { get; set; }

    /// <summary>Triggers: gameplay events or owner tag add/remove that auto-activate this ability (doc 4.6.4).</summary>
    public List<AbilityTriggerData> Triggers { get; } = [];

    public GameplayEffectDefinition? CostGameplayEffect { get; set; }
    public GameplayEffectDefinition? CooldownGameplayEffect { get; set; }

    /// <summary>Shared-cooldown-GE pattern: extra cooldown tags appended to the cooldown spec's DynamicGrantedTags (doc 4.5.15).</summary>
    public GameplayTagContainer CooldownTags { get; set; } = new();
    /// <summary>Shared-cooldown-GE pattern: duration injected via SetByCaller 'Data.Cooldown'.</summary>
    public float CooldownDuration { get; set; }

    // ---------------- Runtime state (set by the ASC on activation) ----------------

    public GameplayAbilityActorInfo CurrentActorInfo { get; private set; } = new();
    public GameplayAbilitySpecHandle CurrentSpecHandle { get; private set; }
    public GameplayAbilityActivationInfo CurrentActivationInfo { get; private set; } = new();
    public GameplayEventData? CurrentEventData { get; private set; }

    public bool IsActive { get; private set; }

    public AbilitySystemComponent? OwnerASC => CurrentActorInfo.AbilitySystemComponent;

    public float GetAbilityLevel() => FindSpec()?.Level ?? 1f;

    internal GameplayAbilitySpec? FindSpec() => OwnerASC?.FindAbilitySpec(CurrentSpecHandle);

    // ---------------- Lifecycle ----------------

    /// <summary>Called once on the granted instance when the ability is given to an ASC (doc 4.6.3).</summary>
    public virtual void OnGiveAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec) { }

    /// <summary>Called on the primary instance when the ability is removed (UE 4.24+ <c>OnRemoveAbility</c>).</summary>
    public virtual void OnRemoveAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec) { }

    /// <summary>Called when the ASC's avatar is set; the recommended place to auto-activate passives (doc 4.6.4.1).</summary>
    public virtual void OnAvatarSet(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
    {
        if (bActivateAbilityOnGranted && actorInfo.AbilitySystemComponent is { } asc)
            asc.TryActivateAbility(spec.Handle, bAllowRemoteActivation: false);
    }

    /// <summary>Full pre-flight check (tag requirements, cost, cooldown, instancing) - doc 4.6.4.</summary>
    public virtual bool CanActivateAbility(GameplayAbilitySpecHandle handle, GameplayAbilityActorInfo actorInfo,
        GameplayEventData? triggerEventData, out GameplayTagContainer failureTags)
    {
        failureTags = new GameplayTagContainer();
        if (!DoesAbilitySatisfyTagRequirements(actorInfo, triggerEventData, out failureTags))
            return false;
        if (!CheckCost(handle, actorInfo))
        {
            failureTags.AddTag(GameplayAbilityFailTags.Cost);
            return false;
        }
        if (!CheckCooldown(handle, actorInfo))
        {
            failureTags.AddTag(GameplayAbilityFailTags.Cooldown);
            return false;
        }
        return true;
    }

    /// <summary>Activation-required/owned/blocked tags, other abilities' blockers, and event source/target requirements (doc 4.6.9).</summary>
    protected virtual bool DoesAbilitySatisfyTagRequirements(GameplayAbilityActorInfo actorInfo,
        GameplayEventData? triggerEventData, out GameplayTagContainer failureTags)
    {
        failureTags = new GameplayTagContainer();
        var asc = actorInfo.AbilitySystemComponent;
        if (asc is null)
            return true;

        var ownedTags = asc.GetOwnedGameplayTagContainer();
        if (!ownedTags.HasAll(ActivationRequiredTags))
        {
            failureTags.AddTag(GameplayAbilityFailTags.TagsMissing);
            foreach (var tag in ActivationRequiredTags.Tags)
                if (!ownedTags.HasTag(tag))
                    failureTags.AddTag(tag);
        }
        if (ownedTags.HasAny(ActivationBlockedTags))
        {
            failureTags.AddTag(GameplayAbilityFailTags.TagsBlocked);
            foreach (var tag in ActivationBlockedTags.Tags)
                if (ownedTags.HasTag(tag))
                    failureTags.AddTag(tag);
        }

        // Other active abilities blocking this one via their BlockAbilitiesWithTag.
        foreach (var spec in asc.GrantedAbilities)
        {
            if (!spec.IsActive || ReferenceEquals(spec.Ability, this))
                continue;
            if (spec.Ability.BlockAbilitiesWithTag.HasAny(AbilityTags))
            {
                failureTags.AddTag(GameplayAbilityFailTags.TagsBlocked);
                failureTags.AddTags(spec.Ability.BlockAbilitiesWithTag);
            }
        }

        if (triggerEventData != null)
        {
            var sourceASC = ResolveEventASC(triggerEventData.Instigator);
            var targetASC = ResolveEventASC(triggerEventData.Target);
            if (sourceASC != null)
            {
                var sourceTags = sourceASC.GetOwnedGameplayTagContainer();
                if (!sourceTags.HasAll(SourceRequiredTags) || sourceTags.HasAny(SourceBlockedTags))
                    failureTags.AddTag(GameplayAbilityFailTags.TagsBlocked);
            }
            if (targetASC != null)
            {
                var targetTags = targetASC.GetOwnedGameplayTagContainer();
                if (!targetTags.HasAll(TargetRequiredTags) || targetTags.HasAny(TargetBlockedTags))
                    failureTags.AddTag(GameplayAbilityFailTags.TagsBlocked);
            }
        }

        return failureTags.IsEmpty;
    }

    private static AbilitySystemComponent? ResolveEventASC(object? actor) =>
        (actor as IAbilitySystemInterface)?.GetAbilitySystemComponent();

    /// <summary>Called by the ASC after all checks passed. Override for input/class-activated abilities.</summary>
    protected virtual void ActivateAbility()
    {
        if (CurrentEventData != null)
            ActivateAbilityFromEvent(CurrentEventData);
        else
            GasNetLog.Error($"{GetType().Name}.ActivateAbility called without overriding it and without event data. Ending ability.");
        EndAbility(wasCancelled: false);
    }

    /// <summary>Override for event-triggered abilities (UE: the "ActivateAbilityFromEvent" node, doc 4.6.4).</summary>
    protected virtual void ActivateAbilityFromEvent(GameplayEventData eventData) => ActivateAbility();

    /// <summary>
    /// Pay cost + start cooldown (doc 4.6.12). Re-checks both as a last chance to fail;
    /// on failure the caller should end the ability (cancelled).
    /// </summary>
    public virtual bool CommitAbility()
    {
        if (!CommitCheck())
            return false;
        CommitCost();
        CommitCooldown();
        return true;
    }

    public virtual bool CommitCheck() => CheckCost(CurrentSpecHandle, CurrentActorInfo) && CheckCooldown(CurrentSpecHandle, CurrentActorInfo);

    public virtual void CommitCost() => ApplyCost(GetCostGameplayEffect());

    public virtual void CommitCooldown() => ApplyCooldown();

    public virtual GameplayEffectDefinition? GetCostGameplayEffect() => CostGameplayEffect;
    public virtual GameplayEffectDefinition? GetCooldownGameplayEffect() => CooldownGameplayEffect;

    protected virtual bool CheckCost(GameplayAbilitySpecHandle handle, GameplayAbilityActorInfo actorInfo)
    {
        var costGE = GetCostGameplayEffect();
        var asc = actorInfo.AbilitySystemComponent;
        if (costGE is null || asc is null)
            return true;
        float level = asc.FindAbilitySpec(handle)?.Level ?? 1f;
        return asc.CanApplyGameplayEffectSpec(MakeAbilityEffectSpecFor(costGE, asc, level, handle));
    }

    protected virtual void ApplyCost(GameplayEffectDefinition? costGE)
    {
        if (costGE is null || OwnerASC is null)
            return;
        OwnerASC.ApplyGameplayEffectSpecToSelf(MakeAbilityEffectSpecFor(costGE, OwnerASC, GetAbilityLevel(), CurrentSpecHandle));
    }

    /// <summary>Cooldown check: looks for the union of cooldown tags on the owner (the GE itself is never checked, doc 4.5.15).</summary>
    protected virtual bool CheckCooldown(GameplayAbilitySpecHandle handle, GameplayAbilityActorInfo actorInfo)
    {
        var cooldownTags = GetCooldownTags();
        if (cooldownTags.IsEmpty || actorInfo.AbilitySystemComponent is null)
            return true;
        return !actorInfo.AbilitySystemComponent.HasAnyTags(cooldownTags);
    }

    /// <summary>Union of the cooldown GE's granted tags and this ability's CooldownTags (doc 4.5.15).</summary>
    public virtual GameplayTagContainer GetCooldownTags()
    {
        var tags = GetCooldownGameplayEffect()?.GrantedTags.Clone() ?? new GameplayTagContainer();
        tags.AddTags(CooldownTags);
        return tags;
    }

    protected virtual void ApplyCooldown()
    {
        var cooldownGE = GetCooldownGameplayEffect();
        if (cooldownGE is null || OwnerASC is null)
            return;

        var spec = MakeAbilityEffectSpecFor(cooldownGE, OwnerASC, GetAbilityLevel(), CurrentSpecHandle);
        if (CooldownTags.IsNotEmpty)
        {
            // Shared-cooldown-GE pattern (doc 4.5.15): inject the cooldown tags and the
            // per-ability duration as both the spec duration and the SetByCaller value.
            spec.DynamicGrantedTags.AddTags(CooldownTags);
            float duration = CooldownDuration > 0f ? CooldownDuration : cooldownGE.Duration;
            if (duration > 0f)
            {
                spec.Duration = duration;
                spec.SetSetByCallerMagnitude(GameplayAbilityCooldownTags.DataCooldown, duration);
            }
        }
        OwnerASC.ApplyGameplayEffectSpecToSelf(spec);
    }

    /// <summary>Terminates the ability (completion or cancellation). Always call this (doc 4.6.4).</summary>
    public virtual void EndAbility(bool wasCancelled)
    {
        if (!IsActive)
        {
            GasNetLog.Warn($"{GetType().Name}.EndAbility called while not active.");
            return;
        }
        OwnerASC?.EndAbilityInternal(this, wasCancelled);
    }

    /// <summary>Cancels the ability (EndAbility with wasCancelled=true, doc 4.6.5).</summary>
    public virtual void CancelAbility() => EndAbility(wasCancelled: true);

    /// <summary>Ability tick driven by <see cref="AbilitySystemComponent.Tick"/> (GasNet's stand-in for AbilityTasks).</summary>
    public virtual void OnAbilityTick(float deltaTime) { }

    /// <summary>Input arrived while this ability is already active (doc 4.6.2.1 pattern).</summary>
    public virtual void NotifyInputPressed() { }

    /// <summary>Bound input released while active - "while input active" abilities end here.</summary>
    public virtual void NotifyInputReleased() { }

    internal bool AreTagRequirementsSatisfiable(GameplayAbilityActorInfo actorInfo) =>
        DoesAbilitySatisfyTagRequirements(actorInfo, null, out _);

    /// <summary>Whether this ability should respond to the given gameplay event (doc 4.6.4).</summary>
    public virtual bool ShouldAbilityRespondToEvent(GameplayTag eventTag, GameplayEventData eventData) => true;

    // ---------------- Failure handlers (doc 4.6.4.2) ----------------

    protected virtual void OnActivationFailed(GameplayTagContainer failureTags)
    {
        if (failureTags.HasTag(GameplayAbilityFailTags.TagsBlocked) || failureTags.HasTag(GameplayAbilityFailTags.TagsMissing))
            ActivateAbilityFail_TagsBlockedOrMissing();
        if (failureTags.HasTag(GameplayAbilityFailTags.Cost))
            ActivateAbilityFail_Cost();
        if (failureTags.HasTag(GameplayAbilityFailTags.Cooldown))
            ActivateAbilityFail_Cooldown();
    }

    protected virtual void ActivateAbilityFail_TagsBlockedOrMissing() { }
    protected virtual void ActivateAbilityFail_Cost() { }
    protected virtual void ActivateAbilityFail_Cooldown() { }

    // ---------------- Internal plumbing (called by ASC) ----------------

    internal void CallActivate(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpecHandle handle,
        GameplayAbilityActivationInfo activationInfo, GameplayEventData? eventData)
    {
        CurrentActorInfo = actorInfo;
        CurrentSpecHandle = handle;
        CurrentActivationInfo = activationInfo;
        CurrentEventData = eventData;
        IsActive = true;
        PreActivate(actorInfo, handle);
        if (eventData != null)
            ActivateAbilityFromEvent(eventData);
        else
            ActivateAbility();
    }

    /// <summary>Boilerplate init before ActivateAbility (UE: <c>PreActivate</c>).</summary>
    protected virtual void PreActivate(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpecHandle handle) { }

    internal void HandleActivationFailed(GameplayTagContainer failureTags) => OnActivationFailed(failureTags);

    internal void NotifyEnded(bool wasCancelled) => IsActive = false;

    // ---------------- Helpers for subclasses ----------------

    /// <summary>Builds a spec from the ability, post-activation (uses the current actor info).</summary>
    protected GameplayEffectSpec MakeAbilityEffectSpec(GameplayEffectDefinition def) =>
        MakeAbilityEffectSpecFor(def, OwnerASC ?? throw new InvalidOperationException("Ability has no owner ASC."),
            GetAbilityLevel(), CurrentSpecHandle);

    /// <summary>
    /// Builds a spec for this ability on an explicit ASC - usable during CanActivateAbility
    /// (before CurrentActorInfo is set), mirroring the engine passing ActorInfo into CheckCost.
    /// </summary>
    protected GameplayEffectSpec MakeAbilityEffectSpecFor(GameplayEffectDefinition def,
        AbilitySystemComponent asc, float level, GameplayAbilitySpecHandle handle)
    {
        var context = asc.MakeEffectContext(sourceObject: asc.FindAbilitySpec(handle)?.SourceObject);
        context.AbilityInstance = this;
        context.AbilitySpecHandle = handle;
        return asc.MakeOutgoingSpec(def, level, context);
    }

    /// <summary>Applies a spec to the owner (UE: <c>ApplyGameplayEffectSpecToOwner</c>).</summary>
    protected ActiveGameplayEffectHandle? ApplyGameplayEffectSpecToOwner(GameplayEffectSpec spec) =>
        OwnerASC?.ApplyGameplayEffectSpecToSelf(spec);

    /// <summary>Applies a spec to another ASC (UE: <c>ApplyGameplayEffectSpecToTarget</c>).</summary>
    protected ActiveGameplayEffectHandle? ApplyGameplayEffectSpecToTarget(GameplayEffectSpec spec, AbilitySystemComponent target) =>
        target.ApplyGameplayEffectSpecToSelf(spec);

    /// <summary>Creates a shallow copy - per-actor/per-execution instancing clones the prototype.</summary>
    public virtual GameplayAbility Clone() => (GameplayAbility)MemberwiseClone();
}
