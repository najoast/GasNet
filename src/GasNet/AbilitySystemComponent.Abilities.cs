namespace GasNet;

public sealed partial class AbilitySystemComponent
{
    /// <summary>All granted abilities (UE: <c>ActivatableAbilities</c>, doc §4.6.10).</summary>
    public List<GameplayAbilitySpec> GrantedAbilities { get; } = [];

    public GameplayAbilityActorInfo ActorInfo { get; private set; } = new();
    public object? OwnerActor => ActorInfo.Owner;
    public object? AvatarActor => ActorInfo.Avatar;

    public event Action<AbilitySystemComponent, GameplayAbilitySpec>? OnAbilityActivated;
    public event Action<AbilitySystemComponent, GameplayAbilitySpec?, GameplayTagContainer>? OnAbilityFailed;
    public event Action<GameplayAbilitySpec, bool>? OnAbilityEnded;

    /// <summary>
    /// Sets Owner/Avatar (UE: <c>InitAbilityActorInfo</c>, doc §4.1.2). Calls
    /// <see cref="GameplayAbility.OnAvatarSet"/> for every granted ability once.
    /// </summary>
    public void InitAbilityActorInfo(object? owner, object? avatar)
    {
        ActorInfo = new GameplayAbilityActorInfo().Init(owner, avatar, this);
        foreach (var spec in GrantedAbilities)
            NotifyAvatarSet(spec);
    }

    private void NotifyAvatarSet(GameplayAbilitySpec spec)
    {
        if (spec.AvatarNotified)
            return;
        spec.AvatarNotified = true;
        spec.Ability.OnAvatarSet(ActorInfo, spec);
    }

    // ---------------- Granting (doc §4.6.3) ----------------

    public GameplayAbilitySpecHandle GiveAbility(GameplayAbilitySpec spec)
    {
        GrantedAbilities.Add(spec);
        spec.SourceObject ??= AvatarActor;
        spec.Ability.OnGiveAbility(ActorInfo, spec);
        if (ActorInfo.Avatar != null)
            NotifyAvatarSet(spec); // may auto-activate passives (bActivateAbilityOnGranted)
        return spec.Handle;
    }

    public GameplayAbilitySpecHandle GiveAbility(Type abilityType, float level = 1f, int inputID = 0) =>
        GiveAbility(new GameplayAbilitySpec(abilityType, level, inputID));

    public GameplayAbilitySpecHandle GiveAbilityAndActivateOnce(GameplayAbilitySpec spec, GameplayEventData? eventData = null)
    {
        var handle = GiveAbility(spec);
        InternalTryActivateAbility(handle, eventData);
        return handle;
    }

    public void ClearAbility(GameplayAbilitySpecHandle handle)
    {
        var spec = FindAbilitySpec(handle);
        if (spec is null)
            return;
        if (spec.IsActive)
        {
            spec.RemoveAbilityOnEnd = true;
            return;
        }
        spec.Ability.OnRemoveAbility(ActorInfo, spec);
        GrantedAbilities.Remove(spec);
    }

    internal void SetRemoveAbilityOnEnd(GameplayAbilitySpecHandle handle)
    {
        if (FindAbilitySpec(handle) is { } spec)
            spec.RemoveAbilityOnEnd = true;
    }

    public GameplayAbilitySpec? FindAbilitySpec(GameplayAbilitySpecHandle handle) =>
        GrantedAbilities.FirstOrDefault(s => s.Handle == handle);

    public List<GameplayAbilitySpec> FindAbilitySpecsByType<TAbility>() where TAbility : GameplayAbility =>
        GrantedAbilities.Where(s => s.Ability.GetType() == typeof(TAbility)).ToList();

    /// <summary>Specs whose ability tags match ALL of <paramref name="tags"/> (doc §4.6.6).</summary>
    public List<GameplayAbilitySpec> GetActivatableGameplayAbilitySpecsByAllMatchingTags(
        GameplayTagContainer tags, bool onlyAbilitiesThatSatisfyTagRequirements = true)
    {
        var result = new List<GameplayAbilitySpec>();
        foreach (var spec in GrantedAbilities)
        {
            if (!spec.AllAbilityTags().HasAll(tags))
                continue;
            if (onlyAbilitiesThatSatisfyTagRequirements && !spec.Ability.AreTagRequirementsSatisfiable(ActorInfo))
                continue;
            result.Add(spec);
        }
        return result;
    }

    // ---------------- Activation (doc §4.6.4) ----------------

    /// <summary>Runs the full activation check without activating (UE: <c>CanActivateAbility</c>).</summary>
    public bool CanActivateAbility(GameplayAbilitySpecHandle handle)
    {
        var spec = FindAbilitySpec(handle);
        if (spec is null || spec.IsActive)
            return false;
        return spec.ResolveAbilityInstance().CanActivateAbility(handle,
            new GameplayAbilityActorInfo().Init(OwnerActor, AvatarActor, this), null, out _);
    }

    public bool TryActivateAbility(GameplayAbilitySpecHandle handle, bool bAllowRemoteActivation = true) =>
        InternalTryActivateAbility(handle, null);

    public bool TryActivateAbilitiesByTag(GameplayTagContainer tags)
    {
        bool anyActivated = false;
        foreach (var spec in GrantedAbilities.ToArray())
        {
            if (spec.IsActive || !spec.MatchesTags(tags))
                continue;
            anyActivated |= InternalTryActivateAbility(spec.Handle, null);
        }
        return anyActivated;
    }

    public bool TryActivateAbilityByClass(Type abilityType)
    {
        var spec = GrantedAbilities.FirstOrDefault(s => !s.IsActive && abilityType.IsInstanceOfType(s.Ability));
        return spec != null && InternalTryActivateAbility(spec.Handle, null);
    }

    public bool TryActivateAbilityByClass<TAbility>() where TAbility : GameplayAbility =>
        TryActivateAbilityByClass(typeof(TAbility));

    private bool InternalTryActivateAbility(GameplayAbilitySpecHandle handle, GameplayEventData? eventData)
    {
        var spec = FindAbilitySpec(handle);
        if (spec is null)
        {
            GasNetLog.Warn($"TryActivateAbility: spec {handle} not found.");
            return false;
        }
        if (spec.IsActive)
        {
            GasNetLog.Warn($"TryActivateAbility: {spec} is already active.");
            return false;
        }

        var abilityObj = spec.ResolveAbilityInstance();
        var actorInfo = new GameplayAbilityActorInfo().Init(OwnerActor, AvatarActor, this);

        if (!abilityObj.CanActivateAbility(handle, actorInfo, eventData, out var failureTags))
        {
            abilityObj.HandleActivationFailed(failureTags);
            OnAbilityFailed?.Invoke(this, spec, failureTags);
            return false;
        }

        // CancelAbilitiesWithTag fires after the checks pass (engine: InternalTryActivateAbility).
        if (abilityObj.CancelAbilitiesWithTag.IsNotEmpty)
            CancelAbilities(abilityObj.CancelAbilitiesWithTag, withoutTags: null, ignore: spec);

        spec.IsActive = true;
        AddTagsToOwned(abilityObj.ActivationOwnedTags);
        OnAbilityActivated?.Invoke(this, spec);
        abilityObj.CallActivate(actorInfo, handle, new GameplayAbilityActivationInfo(), eventData);
        return true;
    }

    internal void EndAbilityInternal(GameplayAbility ability, bool wasCancelled)
    {
        ability.NotifyEnded(wasCancelled);
        var spec = FindAbilitySpec(ability.CurrentSpecHandle);
        if (spec is null || !spec.IsActive)
            return;

        spec.IsActive = false;
        RemoveTagsFromOwned(ability.ActivationOwnedTags);
        if (spec.Ability.InstancingPolicy == GameplayAbilityInstancingPolicy.InstancedPerExecution)
            spec.Instance = null;

        OnAbilityEnded?.Invoke(spec, wasCancelled);
        if (spec.RemoveAbilityOnEnd)
        {
            spec.RemoveAbilityOnEnd = false;
            ClearAbility(spec.Handle);
        }
    }

    // ---------------- Cancelling (doc §4.6.5) ----------------

    public bool CancelAbility(GameplayAbilitySpecHandle handle)
    {
        var spec = FindAbilitySpec(handle);
        if (spec is null || !spec.IsActive)
            return false;
        (spec.Instance ?? spec.Ability).CancelAbility();
        return true;
    }

    /// <summary>Cancels active abilities by prototype (UE: <c>CancelAbility(UGameplayAbility*)</c>).</summary>
    public bool CancelAbility(GameplayAbility ability)
    {
        bool any = false;
        foreach (var spec in GrantedAbilities.ToArray())
        {
            if (!spec.IsActive)
                continue;
            if (ReferenceEquals(spec.Ability, ability) || ReferenceEquals(spec.Instance, ability) ||
                spec.Ability.GetType() == ability.GetType())
            {
                any |= CancelAbility(spec.Handle);
            }
        }
        return any;
    }

    public void CancelAbilities(GameplayTagContainer? withTags, GameplayTagContainer? withoutTags = null, GameplayAbilitySpec? ignore = null)
    {
        foreach (var spec in GrantedAbilities.ToArray())
        {
            if (ReferenceEquals(spec, ignore) || !spec.IsActive)
                continue;
            if (withTags is { IsNotEmpty: true } && !spec.AllAbilityTags().HasAny(withTags))
                continue;
            if (withoutTags is { IsNotEmpty: true } && spec.AllAbilityTags().HasAny(withoutTags))
                continue;
            CancelAbility(spec.Handle);
        }
    }

    public void CancelAllAbilities(GameplayAbilitySpec? ignore = null) => CancelAbilities(null, null, ignore);

    // ---------------- Input (doc §4.6.2) ----------------

    /// <summary>Presses an input bound by InputID: activates matching specs (UE: <c>AbilityLocalInputPressed</c>).</summary>
    public void AbilityLocalInputPressed(int inputID)
    {
        foreach (var spec in GrantedAbilities.ToArray())
        {
            if (spec.InputID != inputID)
                continue;
            spec.InputPressed = true;
            if (!spec.IsActive)
                TryActivateAbility(spec.Handle);
            else
                (spec.Instance ?? spec.Ability).NotifyInputPressed();
        }
    }

    public void AbilityLocalInputReleased(int inputID)
    {
        foreach (var spec in GrantedAbilities.ToArray())
        {
            if (spec.InputID != inputID)
                continue;
            spec.InputPressed = false;
            if (spec.IsActive)
                (spec.Instance ?? spec.Ability).NotifyInputReleased();
        }
    }

    // ---------------- Gameplay events & tag triggers (doc §4.6.4, §4.6.11) ----------------

    /// <summary>
    /// Routes a gameplay event to granted abilities with a matching GameplayEvent trigger
    /// (UE: <c>HandleGameplayEvent</c> / <c>SendGameplayEventToActor</c>).
    /// </summary>
    public void HandleGameplayEvent(GameplayTag eventTag, GameplayEventData? eventData = null)
    {
        eventData ??= new GameplayEventData { EventTag = eventTag };
        foreach (var spec in GrantedAbilities.ToArray())
        {
            if (spec.IsActive)
                continue;
            if (!spec.Ability.Triggers.Any(t => t.TriggerSource == AbilityTriggerSource.GameplayEvent && t.Tag == eventTag))
                continue;
            if (!spec.Ability.ShouldAbilityRespondToEvent(eventTag, eventData))
                continue;
            InternalTryActivateAbility(spec.Handle, eventData);
        }
    }

    private void ProcessTagTriggers(GameplayTag tag, int newCount)
    {
        foreach (var spec in GrantedAbilities.ToArray())
        {
            if (spec.IsActive)
                continue;
            foreach (var trigger in spec.Ability.Triggers)
            {
                bool shouldFire = trigger.Tag == tag && trigger.TriggerSource switch
                {
                    AbilityTriggerSource.TagAdded => newCount > 0,
                    AbilityTriggerSource.TagRemoved => newCount == 0,
                    _ => false,
                };
                if (shouldFire)
                {
                    TryActivateAbility(spec.Handle);
                    break;
                }
            }
        }
    }
}
