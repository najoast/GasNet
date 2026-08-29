namespace GasNet;

/// <summary>
/// World-time source used by the ASC to evaluate gameplay effect durations, cooldowns and ability timers.
/// Tests and offline hosts inject a <see cref="ManualTimeSource"/>; live games use <see cref="RealTimeSource"/>.
/// </summary>
public interface ITimeSource
{
    float NowSeconds { get; }
}

public sealed class RealTimeSource : ITimeSource
{
    public static RealTimeSource Instance { get; } = new();
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
    public float NowSeconds => (float)_stopwatch.Elapsed.TotalSeconds;
}

/// <summary>Deterministic clock: advance manually (ideal for tests and fixed-step simulations).</summary>
public sealed class ManualTimeSource : ITimeSource
{
    private float _now;
    public float NowSeconds => _now;
    public void Advance(float seconds) => _now += Math.Max(0f, seconds);
    public void Set(float seconds) => _now = seconds;
}

/// <summary>Anything that owns an ASC implements this (UE: <c>IAbilitySystemInterface</c>).</summary>
public interface IAbilitySystemInterface
{
    AbilitySystemComponent? GetAbilitySystemComponent();
}

/// <summary>Anything that exposes owned gameplay tags (UE: <c>IGameplayTagAssetInterface</c>).</summary>
public interface IGameplayTagAssetInterface
{
    void GetOwnedGameplayTags(GameplayTagContainer tagContainer);
    bool HasMatchingGameplayTag(GameplayTag tag);
}

/// <summary>Payload sent with a gameplay event (UE: <c>FGameplayEventData</c>).</summary>
public sealed class GameplayEventData
{
    public GameplayTag EventTag { get; set; }
    public object? Instigator { get; set; }
    public object? Target { get; set; }
    public object? OptionalObject { get; set; }
    public object? OptionalTarget { get; set; }
    public float EventMagnitude { get; set; }
    public GameplayEffectContext? Context { get; set; }
    public GameplayTagContainer InstigatorTags { get; } = new();
    public GameplayTagContainer TargetTags { get; } = new();
    /// <summary>Nested events (UE: <c>EventDatas</c> array for chaining, e.g. montage → impact).</summary>
    public List<GameplayEventData> EventDatas { get; } = [];
}

/// <summary>Static helpers mirroring <c>UAbilitySystemBlueprintLibrary</c>.</summary>
public static class GameplayAbilitySystemLibrary
{
    /// <summary>Sends a gameplay event to an actor's ASC (UE: <c>SendGameplayEventToActor</c>, doc §4.6.11).</summary>
    public static void SendGameplayEventToActor(object? actor, GameplayTag eventTag, GameplayEventData? eventData = null)
    {
        var asc = AbilitySystemComponent.FindASC(actor)
            ?? throw new InvalidOperationException($"Actor '{actor}' does not implement IAbilitySystemInterface.");
        asc.HandleGameplayEvent(eventTag, eventData);
    }
}
