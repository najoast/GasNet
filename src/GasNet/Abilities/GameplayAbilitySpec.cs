namespace GasNet;

/// <summary>
/// Runtime state of one granted ability on an ASC — equivalent to UE's <c>FGameplayAbilitySpec</c>.
/// Holds the ability prototype, level, input binding and dynamic tags; the instance used for
/// activation is resolved per instancing policy.
/// </summary>
public sealed class GameplayAbilitySpec
{
    public GameplayAbility Ability { get; }
    public float Level { get; set; } = 1f;
    public int InputID { get; set; }
    public GameplayTagContainer DynamicAbilityTags { get; } = new();
    public GameplayAbilitySpecHandle Handle { get; } = new(++GameplayAbilitySpecHandle.Generator.Next);

    /// <summary>Arbitrary source object captured at grant time (e.g. the weapon that granted it).</summary>
    public object? SourceObject { get; set; }

    /// <summary>Instanced-per-actor instance (created lazily on first activation).</summary>
    public GameplayAbility? Instance { get; internal set; }

    public bool IsActive { get; internal set; }
    public bool InputPressed { get; internal set; }

    /// <summary>Ability tasks spawned by the current activation; cleared when the ability ends (doc §4.7).</summary>
    public List<AbilityTask> Tasks { get; } = [];

    /// <summary>When set (by a GE removal policy), the ASC clears this ability as soon as it ends.</summary>
    internal bool RemoveAbilityOnEnd { get; set; }

    /// <summary>Guards against double OnAvatarSet notifications.</summary>
    internal bool AvatarNotified { get; set; }

    public GameplayAbilitySpec(GameplayAbility ability, float level = 1f, int inputID = 0)
    {
        Ability = ability ?? throw new ArgumentNullException(nameof(ability));
        Level = level;
        InputID = inputID;
    }

    public GameplayAbilitySpec(Type abilityType, float level = 1f, int inputID = 0)
    {
        if (abilityType == null) throw new ArgumentNullException(nameof(abilityType));
        if (!typeof(GameplayAbility).IsAssignableFrom(abilityType))
            throw new ArgumentException($"{abilityType} is not a GameplayAbility.", nameof(abilityType));
        Ability = (GameplayAbility)Activator.CreateInstance(abilityType)!;
        Level = level;
        InputID = inputID;
    }

    internal void AddTask(AbilityTask task) => Tasks.Add(task);
    internal void RemoveTask(AbilityTask task) => Tasks.Remove(task);

    /// <summary>Ticks all valid tasks (snapshot: a task may end the ability or other tasks mid-loop).</summary>
    internal void TickTasks(float deltaTime)
    {
        foreach (var task in Tasks.ToArray())
            task.Tick(deltaTime);
    }

    /// <summary>Tears down every remaining task — called when the ability ends (doc §4.7).</summary>
    internal void EndAllTasks(AbilityTaskEndReason reason)
    {
        foreach (var task in Tasks.ToArray())
            task.End(reason);
    }

    internal void NotifyTasksInputPressed()
    {
        foreach (var task in Tasks.ToArray())
            task.NotifyInputPressed();
    }

    internal void NotifyTasksInputReleased()
    {
        foreach (var task in Tasks.ToArray())
            task.NotifyInputReleased();
    }

    public bool MatchesTags(GameplayTagContainer tags, bool exact = false) =>
        tags.IsEmpty || AllAbilityTags().MatchesAny(tags, exact);

    public GameplayTagContainer AllAbilityTags()
    {
        var tags = Ability.AbilityTags.Clone();
        tags.AddTags(DynamicAbilityTags);
        return tags;
    }

    /// <summary>The ability object that should run this activation, per instancing policy.</summary>
    internal GameplayAbility ResolveAbilityInstance()
    {
        switch (Ability.InstancingPolicy)
        {
            case GameplayAbilityInstancingPolicy.InstancedPerActor:
                return Instance ??= (GameplayAbility)Ability.Clone();
            case GameplayAbilityInstancingPolicy.InstancedPerExecution:
                return (GameplayAbility)Ability.Clone();
            default:
                return Ability; // NonInstanced: shared prototype, must remain stateless
        }
    }

    public override string ToString() => $"{Ability.GetType().Name} {Handle} (Lv {Level}){(IsActive ? " [active]" : "")}";
}
