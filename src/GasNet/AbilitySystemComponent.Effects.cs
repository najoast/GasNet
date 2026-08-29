namespace GasNet;

public sealed partial class AbilitySystemComponent
{
    public ActiveGameplayEffectsContainer Active => ActiveGameplayEffects; // alias, kept short for call sites

    // ---------------- Delegates (doc §4.5.2, §4.5.3, §4.5.8) ----------------

    /// <summary>Fires when a Duration/Infinite GE was successfully applied to this ASC (UE: <c>OnActiveGameplayEffectAddedDelegateToSelf</c>).</summary>
    public event Action<AbilitySystemComponent, GameplayEffectSpec, ActiveGameplayEffect>? OnGameplayEffectAppliedToSelf;

    /// <summary>Fires when any active GE left the container (UE: <c>OnAnyGameplayEffectRemovedDelegate</c>).</summary>
    public event Action<ActiveGameplayEffect, GameplayEffectRemovalReason>? OnAnyGameplayEffectRemoved;

    /// <summary>Fires when a GE application was blocked by immunity (doc §4.5.8).</summary>
    public event Action<AbilitySystemComponent, GameplayEffectSpec, GameplayTagContainer>? OnImmunityBlockGameplayEffect;

    internal void FireGameplayEffectApplied(GameplayEffectSpec spec, ActiveGameplayEffect age) =>
        OnGameplayEffectAppliedToSelf?.Invoke(this, spec, age);

    internal void FireAnyGameplayEffectRemoved(ActiveGameplayEffect age, GameplayEffectRemovalReason reason) =>
        OnAnyGameplayEffectRemoved?.Invoke(age, reason);

    internal void InvokeOnImmunityBlockGameplayEffect(GameplayEffectSpec spec, GameplayTagContainer failTags) =>
        OnImmunityBlockGameplayEffect?.Invoke(this, spec, failTags);

    // ---------------- Spec creation (doc §4.5.9) ----------------

    public GameplayEffectContext MakeEffectContext(object? sourceObject = null) => new()
    {
        Instigator = OwnerActor,
        EffectCauser = AvatarActor,
        SourceObject = sourceObject,
        InstigatorAbilitySystemComponent = this,
    };

    /// <summary>Creates a spec from a GE definition; source tags are captured at creation (doc §4.5.4.2).</summary>
    public GameplayEffectSpec MakeOutgoingSpec(GameplayEffectDefinition def, float level = 1f, GameplayEffectContext? context = null)
    {
        context ??= MakeEffectContext();
        var spec = new GameplayEffectSpec(def, level, context);
        if (context.InstigatorAbilitySystemComponent is { } source)
            spec.CapturedSourceTags.AddTags(source.GetOwnedGameplayTagContainer());
        return spec;
    }

    // ---------------- Applying (doc §4.5.2) ----------------

    public ActiveGameplayEffectHandle? ApplyGameplayEffectToSelf(GameplayEffectDefinition def, float level = 1f, GameplayEffectContext? context = null) =>
        ApplyGameplayEffectSpecToSelf(MakeOutgoingSpec(def, level, context));

    /// <summary>All apply functions funnel here, like UE's <c>ApplyGameplayEffectSpecToSelf</c>.</summary>
    public ActiveGameplayEffectHandle? ApplyGameplayEffectSpecToSelf(GameplayEffectSpec spec) =>
        ActiveGameplayEffects.ApplyGameplayEffectSpec(spec);

    public ActiveGameplayEffectHandle? ApplyGameplayEffectToTarget(GameplayEffectDefinition def, float level,
        AbilitySystemComponent target, GameplayEffectContext? context = null) =>
        ApplyGameplayEffectSpecToTarget(MakeOutgoingSpec(def, level, context ?? MakeEffectContext()), target);

    public ActiveGameplayEffectHandle? ApplyGameplayEffectSpecToTarget(GameplayEffectSpec spec, AbilitySystemComponent target) =>
        target.ActiveGameplayEffects.ApplyGameplayEffectSpec(spec);

    /// <summary>Full application check without applying (cost checks use this).</summary>
    public bool CanApplyGameplayEffectSpec(GameplayEffectSpec spec)
    {
        var failTags = ActiveGameplayEffects.CanApplyGameplayEffectSpec(spec, out bool immunityBlocked);
        return failTags.IsEmpty && !immunityBlocked;
    }

    // ---------------- Removing (doc §4.5.3) ----------------

    public bool RemoveActiveGameplayEffect(ActiveGameplayEffectHandle handle) =>
        ActiveGameplayEffects.RemoveActiveGameplayEffect(handle);

    /// <summary>Removes every active effect matching the query; returns how many were removed.</summary>
    public int RemoveActiveGameplayEffects(GameplayEffectQuery query) =>
        ActiveGameplayEffects.RemoveActiveEffects(query);

    public void RemoveAllActiveGameplayEffects() => ActiveGameplayEffects.ClearAllEffects();

    // ---------------- Queries ----------------

    public List<ActiveGameplayEffect> GetActiveEffectsWithAllTags(GameplayTagContainer tags) =>
        ActiveGameplayEffects.GetActiveEffectsWithAllTags(tags);

    public List<ActiveGameplayEffect> GetActiveEffects(GameplayEffectQuery query) =>
        ActiveGameplayEffects.GetActiveEffects(query);

    public int GetActiveGameplayEffectCount(GameplayEffectDefinition def) =>
        ActiveGameplayEffects.GetGameplayEffectCount(def);

    public int GetActiveGameplayEffectCount(GameplayEffectQuery query) =>
        ActiveGameplayEffects.GetGameplayEffectCount(query);

    public bool HasAnyMatchingGameplayEffects(GameplayEffectQuery query) =>
        GetActiveGameplayEffectCount(query) > 0;

    /// <summary>(timeRemaining, duration) pairs of matching effects (doc §4.5.15.1).</summary>
    public IEnumerable<(float timeRemaining, float duration)> GetActiveEffectsTimeRemainingAndDuration(GameplayEffectQuery query) =>
        ActiveGameplayEffects.GetActiveEffectsTimeRemainingAndDuration(query);

    public float GetActiveEffectsTimeRemaining(GameplayEffectQuery query) =>
        ActiveGameplayEffects.GetActiveEffectsTimeRemaining(query);
}
