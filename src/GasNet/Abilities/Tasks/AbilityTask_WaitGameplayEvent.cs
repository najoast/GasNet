namespace GasNet;

/// <summary>
/// Fires when the owning ASC handles a matching gameplay event — UE's
/// <c>UAbilityTask_WaitGameplayEvent</c> (doc §4.6.11/§4.7). Matching is hierarchical: listening
/// for <c>Event.Hit</c> also completes on <c>Event.Hit.Fireball</c> unless <c>exactMatch</c> is set.
/// This task is also GasNet's stand-in for WaitTargetData: have the host raise e.g.
/// <c>GameplayEvent.TargetAcquired</c> with a <see cref="GameplayEventData"/> payload and complete
/// targeting through <see cref="OnEvent"/>.
/// </summary>
public sealed class AbilityTask_WaitGameplayEvent : AbilityTask
{
    private readonly GameplayTag _tag;
    private readonly bool _exact;
    private GameplayEventData? _pending;

    /// <summary>Fires with the event payload when a matching event arrives.</summary>
    public Action<GameplayEventData>? OnEvent { get; set; }

    /// <summary>Fires instead of <see cref="OnEvent"/> when the ability ends/cancels while waiting.</summary>
    public Action? OnCancelled { get; set; }

    private AbilityTask_WaitGameplayEvent(GameplayAbility ability, GameplayTag tag, bool exact)
        : base(ability)
    {
        _tag = tag;
        _exact = exact;
    }

    public static AbilityTask_WaitGameplayEvent? WaitForEvent(GameplayAbility ability, GameplayTag tag,
        Action<GameplayEventData>? onEvent = null, Action? onCancelled = null, bool exactMatch = false)
    {
        if (!tag.IsValid)
        {
            GasNetLog.Warn($"{ability.GetType().Name}.WaitForEvent: invalid event tag.");
            return null;
        }
        var task = ability.SpawnTask(new AbilityTask_WaitGameplayEvent(ability, tag, exactMatch));
        if (task is null)
            return null;
        task.OnEvent = onEvent;
        task.OnCancelled = onCancelled;
        return task;
    }

    protected override void OnActivate()
    {
        if (Ability.OwnerASC is { } asc)
            asc.OnGameplayEvent += HandleEvent;
        else
            GasNetLog.Warn($"{this}: no owner ASC; the task will never fire.");
    }

    private void HandleEvent(GameplayTag eventTag, GameplayEventData eventData)
    {
        if (!eventTag.MatchesTag(_tag, _exact))
            return;
        _pending = eventData;
        EndTask();
    }

    protected override void OnEnd(AbilityTaskEndReason reason)
    {
        if (Ability.OwnerASC is { } asc)
            asc.OnGameplayEvent -= HandleEvent;
        if (reason == AbilityTaskEndReason.Completed)
            OnEvent?.Invoke(_pending ?? new GameplayEventData { EventTag = _tag });
        else
            OnCancelled?.Invoke();
    }
}
