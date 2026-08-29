namespace GasNet;

/// <summary>
/// The heart of the system — equivalent to UE's <c>UAbilitySystemComponent</c>. Owns the tag
/// counts, active GameplayEffects, granted abilities, attribute sets and cue routing for one actor.
/// Hosts must call <see cref="Tick"/> regularly (the engine ticks GEs from the world tick).
/// </summary>
public sealed partial class AbilitySystemComponent : IGameplayTagAssetInterface
{
    /// <summary>Count-based tag storage: GE-granted tags + loose (manually managed) tags (doc §4.2).</summary>
    public GameplayTagCountContainer Tags { get; } = new();

    public ActiveGameplayEffectsContainer ActiveGameplayEffects { get; }

    /// <summary>World-time source; inject a <see cref="ManualTimeSource"/> for deterministic simulation.</summary>
    public ITimeSource TimeSource { get; set; } = RealTimeSource.Instance;

    public AbilitySystemComponent()
    {
        ActiveGameplayEffects = new ActiveGameplayEffectsContainer(this);
        Tags.AnyTagCountChanged += OnAnyTagCountChanged;
        AbilitySystemGlobals.Get().InitGlobalData();
    }

    private void OnAnyTagCountChanged(GameplayTag tag, int newCount)
    {
        ActiveGameplayEffects.UpdateOngoingTagRequirements();
        ProcessTagTriggers(tag, newCount);
    }

    /// <summary>Advances durations, periodic effect ticks and active ability ticks. Call every frame.</summary>
    public void Tick(float deltaTime)
    {
        float now = TimeSource.NowSeconds;
        ActiveGameplayEffects.CheckDuration(now);
        ActiveGameplayEffects.TickPeriodicEffects(now);

        foreach (var spec in GrantedAbilities.ToArray())
        {
            if (!spec.IsActive)
                continue;
            var instance = spec.Instance ?? (spec.Ability.InstancingPolicy == GameplayAbilityInstancingPolicy.NonInstanced ? spec.Ability : null);
            instance?.OnAbilityTick(deltaTime);
        }
    }

    // ---------------- Tags (doc §4.2) ----------------

    public GameplayTagContainer GetOwnedGameplayTagContainer() => Tags.GetAggregatedTags();

    /// <summary>Fills <paramref name="tagContainer"/> with all owned tags (UE: <c>IGameplayTagAssetInterface</c>).</summary>
    public void GetOwnedGameplayTags(GameplayTagContainer tagContainer)
    {
        tagContainer.Clear();
        tagContainer.AddTags(Tags.GetAggregatedTags());
    }

    /// <summary>Hierarchical check: owned tags contain the tag or any descendant of it.</summary>
    public bool HasMatchingGameplayTag(GameplayTag tag) => Tags.HasTag(tag);

    public bool HasAllMatchingGameplayTags(GameplayTagContainer tags) => Tags.HasAllTags(tags);
    public bool HasAnyTags(GameplayTagContainer tags) => Tags.HasAnyTags(tags);
    public bool HasNoMatchingGameplayTags(GameplayTagContainer tags) => Tags.HasNoTags(tags);

    internal void AddTagsToOwned(GameplayTagContainer tags) => Tags.AddTags(tags);
    internal void RemoveTagsFromOwned(GameplayTagContainer tags) => Tags.RemoveTags(tags);

    /// <summary>Loose tags do not come from GEs; manage them manually (doc §4.2, e.g. State.Dead).</summary>
    public void AddLooseGameplayTag(GameplayTag tag, int count = 1) => Tags.AddTag(tag, count);
    public void RemoveLooseGameplayTag(GameplayTag tag, int count = 1) => Tags.RemoveTag(tag, count);
    public void SetLooseGameplayTagCount(GameplayTag tag, int count) => Tags.SetTagCount(tag, count);
    public int GetLooseGameplayTagCount(GameplayTag tag) => Tags.GetTagCount(tag);

    /// <summary>Subscribes to tag count events (UE: <c>RegisterGameplayTagEvent</c>, doc §4.2.1).</summary>
    public GameplayTagEventRegistration RegisterGameplayTagEvent(GameplayTag tag, GameplayTagEventType eventType,
        Action<GameplayTag, int> handler) => Tags.RegisterGameplayTagEvent(tag, eventType, handler);

    /// <summary>Resolves the ASC of an actor implementing <see cref="IAbilitySystemInterface"/>.</summary>
    public static AbilitySystemComponent? FindASC(object? actor) =>
        (actor as IAbilitySystemInterface)?.GetAbilitySystemComponent();
}
