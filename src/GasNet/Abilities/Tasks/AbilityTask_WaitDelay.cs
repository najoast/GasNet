namespace GasNet;

/// <summary>
/// Fires after a fixed amount of ability time — UE's <c>UAbilityTask_WaitDelay</c> (doc §4.7).
/// Time accumulates from <see cref="AbilitySystemComponent.Tick"/> deltas, so it is deterministic
/// under a <see cref="ManualTimeSource"/> and pauses when the host stops ticking. Chain
/// <see cref="WaitForDelay"/> calls for repeating timers instead of hand-written accumulators in
/// <see cref="GameplayAbility.OnAbilityTick"/>.
/// </summary>
public sealed class AbilityTask_WaitDelay : AbilityTask
{
    private readonly float _time;
    private float _elapsed;

    /// <summary>Fires when the delay elapses; not fired if the ability ends first.</summary>
    public Action? OnFire { get; set; }

    /// <summary>Fires instead of <see cref="OnFire"/> when the ability ends/cancels while waiting.</summary>
    public Action? OnCancelled { get; set; }

    private AbilityTask_WaitDelay(GameplayAbility ability, float time) : base(ability) => _time = time;

    /// <summary>Creates and registers the task on <paramref name="ability"/> (UE: <c>WaitDelay</c>).</summary>
    public static AbilityTask_WaitDelay? WaitForDelay(GameplayAbility ability, float time,
        Action? onFire = null, Action? onCancelled = null)
    {
        var task = ability.SpawnTask(new AbilityTask_WaitDelay(ability, time));
        if (task is null)
            return null;
        task.OnFire = onFire;
        task.OnCancelled = onCancelled;
        return task;
    }

    protected override void OnTick(float deltaTime)
    {
        _elapsed += deltaTime;
        if (_elapsed >= _time)
            EndTask();
    }

    protected override void OnEnd(AbilityTaskEndReason reason)
    {
        if (reason == AbilityTaskEndReason.Completed)
            OnFire?.Invoke();
        else
            OnCancelled?.Invoke();
    }
}
