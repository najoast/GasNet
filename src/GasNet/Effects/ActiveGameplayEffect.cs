namespace GasNet;

/// <summary>Why an active GE left the container.</summary>
public enum GameplayEffectRemovalReason
{
    /// <summary>Removed manually (RemoveActiveGameplayEffect).</summary>
    Manual,
    /// <summary>Duration elapsed naturally.</summary>
    Expired,
    /// <summary>A stack was consumed by expiry (ClearSingleStackCount / RefreshDuration policies).</summary>
    StackExpired,
    /// <summary>Removed by another GE's <c>RemoveGameplayEffectsWithTags</c> or by granted-tag immunity.</summary>
    Replaced,
}

/// <summary>
/// One successfully-applied GameplayEffect tracked by an ASC (UE: <c>FActiveGameplayEffect</c>).
/// Instant GEs never become active effects.
/// </summary>
public sealed class ActiveGameplayEffect
{
    public required ActiveGameplayEffectHandle Handle { get; init; }
    public required GameplayEffectSpec Spec { get; set; }

    /// <summary>World time (seconds) when the effect started.</summary>
    public float StartTime { get; internal set; }
    /// <summary>Effective duration; &lt;= 0 for Infinite GEs.</summary>
    public float Duration { get; internal set; }

    public float Period { get; internal set; }
    internal float NextExecuteTime { get; set; }
    public int ExecutedPeriodCount { get; internal set; }

    public int StackCount { get; internal set; } = 1;

    /// <summary>True while Ongoing Tag Requirements are unmet: the GE stays in the container but its
    /// modifiers and granted tags are removed (doc §4.5.7).</summary>
    public bool IsInactive { get; internal set; }

    /// <summary>ASC tags at application time — used as the TargetTags of this GE's mods (capture filters).</summary>
    public GameplayTagContainer TargetTagsAtApplication { get; } = new();

    public bool IsPeriodic => Period > 0f;
    public bool IsInfinite => Spec.Def.IsInfinite;
    public bool HasDuration => Spec.Def.HasDuration;

    public float GetTimeRemaining(float nowSeconds) =>
        Duration > 0 ? Math.Max(0f, StartTime + Duration - nowSeconds) : float.PositiveInfinity;

    // ---- Events (UE: FActiveGameplayEffect::EventSet) ----
    public event Action<ActiveGameplayEffect>? OnRemoved;
    public event Action<ActiveGameplayEffect, int, int>? OnStackChanged;
    public event Action<ActiveGameplayEffect, float, float>? OnTimeChanged;
    public event Action<ActiveGameplayEffect>? OnPeriod;
    public event Action<ActiveGameplayEffect, bool>? OnOngoingRequirementsChanged;

    internal void BroadcastRemoved() => OnRemoved?.Invoke(this);
    internal void BroadcastStackChanged(int oldCount, int newCount) => OnStackChanged?.Invoke(this, oldCount, newCount);
    internal void BroadcastTimeChanged(float start, float duration) => OnTimeChanged?.Invoke(this, start, duration);
    internal void BroadcastPeriod() => OnPeriod?.Invoke(this);
    internal void BroadcastOngoingRequirementsChanged(bool activeNow) => OnOngoingRequirementsChanged?.Invoke(this, activeNow);

    public override string ToString() => $"{Spec} stacks={StackCount}{(IsInactive ? " [inactive]" : "")}";
}
