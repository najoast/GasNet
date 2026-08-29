namespace GasNet;

public enum GameplayAbilityInstancingPolicy
{
    /// <summary>Runs on the shared prototype; no per-activation state allowed (UE: Non-Instanced).</summary>
    NonInstanced,
    /// <summary>One instance per ASC, reused across activations; state persists between activations (most common).</summary>
    InstancedPerActor,
    /// <summary>A fresh instance per activation; state resets automatically (slower).</summary>
    InstancedPerExecution,
}

public enum GameplayAbilityNetExecutionPolicy
{
    /// <summary>Runs only on the owning client.</summary>
    LocalOnly,
    /// <summary>Runs on owning client first, server corrects.</summary>
    LocalPredicted,
    /// <summary>Runs only on the server (typical for passives and single-player).</summary>
    ServerOnly,
    /// <summary>Server runs first, then owner.</summary>
    ServerInitiated,
}

public enum AbilityTriggerSource
{
    /// <summary>Fired by <see cref="AbilitySystemComponent.HandleGameplayEvent"/>.</summary>
    GameplayEvent,
    /// <summary>Fired when the owner ASC gains the tag.</summary>
    TagAdded,
    /// <summary>Fired when the owner ASC loses the tag.</summary>
    TagRemoved,
}

public sealed class AbilityTriggerData
{
    public required GameplayTag Tag { get; set; }
    public required AbilityTriggerSource TriggerSource { get; set; }
}

/// <summary>Handle to a granted ability spec on an ASC (UE: <c>FGameplayAbilitySpecHandle</c>).</summary>
public readonly struct GameplayAbilitySpecHandle : IEquatable<GameplayAbilitySpecHandle>
{
    public int Handle { get; }
    internal GameplayAbilitySpecHandle(int handle) => Handle = handle;
    public bool IsValid => Handle != 0;
    public static GameplayAbilitySpecHandle None => default;
    public bool Equals(GameplayAbilitySpecHandle other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is GameplayAbilitySpecHandle other && Equals(other);
    public override int GetHashCode() => Handle;
    public override string ToString() => $"Spec:{Handle}";
    public static bool operator ==(GameplayAbilitySpecHandle a, GameplayAbilitySpecHandle b) => a.Equals(b);
    public static bool operator !=(GameplayAbilitySpecHandle a, GameplayAbilitySpecHandle b) => !a.Equals(b);

    internal static class Generator
    {
        public static int Next;
    }
}

/// <summary>Who owns/animates the ability (UE: <c>FGameplayAbilityActorInfo</c>).</summary>
public sealed class GameplayAbilityActorInfo
{
    public object? Owner { get; set; }
    public object? Avatar { get; set; }
    public AbilitySystemComponent? AbilitySystemComponent { get; set; }

    public GameplayAbilityActorInfo Init(object? owner, object? avatar, AbilitySystemComponent? asc)
    {
        Owner = owner;
        Avatar = avatar;
        AbilitySystemComponent = asc;
        return this;
    }
}

public enum GameplayAbilityActivationMode
{
    Authority,
    NonAuthority,
    Predicting,
}

public sealed class GameplayAbilityActivationInfo
{
    public GameplayAbilityActivationMode ActivationMode { get; set; } = GameplayAbilityActivationMode.Authority;
}

/// <summary>
/// Activation-failure tags (doc §4.6.4.2, engine ini names under "Activation.Fail.*").
/// </summary>
public static class GameplayAbilityFailTags
{
    public static readonly GameplayTag TagsBlocked = GameplayTag.RequestGameplayTag("Activation.Fail.BlockedByTags");
    public static readonly GameplayTag TagsMissing = GameplayTag.RequestGameplayTag("Activation.Fail.MissingTags");
    public static readonly GameplayTag Cost = GameplayTag.RequestGameplayTag("Activation.Fail.CantAffordCost");
    public static readonly GameplayTag Cooldown = GameplayTag.RequestGameplayTag("Activation.Fail.OnCooldown");
    public static readonly GameplayTag Networking = GameplayTag.RequestGameplayTag("Activation.Fail.Networking");
    public static readonly GameplayTag IsDead = GameplayTag.RequestGameplayTag("Activation.Fail.IsDead");
}

/// <summary>The SetByCaller tag used by the shared-cooldown-GE pattern (doc §4.5.15).</summary>
public static class GameplayAbilityCooldownTags
{
    public static readonly GameplayTag DataCooldown = GameplayTag.RequestGameplayTag("Data.Cooldown");
}
