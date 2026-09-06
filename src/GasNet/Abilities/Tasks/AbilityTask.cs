namespace GasNet;

/// <summary>Why a task ended (UE ends a task when its ability ends, doc §4.7).</summary>
public enum AbilityTaskEndReason
{
    /// <summary>The task's own condition was met (it called <see cref="AbilityTask.EndTask"/>).</summary>
    Completed,
    /// <summary>The ability was cancelled while the task was pending (doc §4.6.5).</summary>
    Cancelled,
    /// <summary>The ability ended on its own while the task was still pending.</summary>
    AbilityEnded,
}

/// <summary>
/// A pending sub-action of one active ability — GasNet's take on UE's <c>UAbilityTask</c> (doc §4.7).
/// Tasks tick with the owning ASC (<see cref="AbilitySystemComponent.Tick"/>) and are torn down
/// automatically when their ability ends. Spawn tasks from inside <c>ActivateAbility</c> via the
/// static factory on each concrete task (UE: the static Create functions); see
/// <see cref="GameplayAbility.SpawnTask{TTask}"/> for the hosting rules — NonInstanced abilities
/// cannot host tasks because the shared prototype must stay stateless (doc §4.6.7).
/// </summary>
public abstract class AbilityTask
{
    /// <summary>The ability that spawned this task.</summary>
    public GameplayAbility Ability { get; }

    /// <summary>False once the task has ended; ended tasks never tick or fire again.</summary>
    public bool IsValid { get; private set; }

    /// <summary>Fired on every end, after the task-specific end delegate (UE: <c>OnDestroy</c>).</summary>
    public event Action<AbilityTask, AbilityTaskEndReason>? TaskEnded;

    internal GameplayAbilitySpec? Spec { get; private set; }

    protected AbilityTask(GameplayAbility ability)
    {
        Ability = ability ?? throw new ArgumentNullException(nameof(ability));
    }

    /// <summary>Ends the task as completed — its success delegate fires.</summary>
    public void EndTask() => End(AbilityTaskEndReason.Completed);

    /// <summary>Ends the task for any reason and fires its end delegates; safe to call twice.</summary>
    public void End(AbilityTaskEndReason reason)
    {
        if (!IsValid)
            return;
        IsValid = false;
        Spec?.RemoveTask(this);
        OnEnd(reason);
        TaskEnded?.Invoke(this, reason);
    }

    /// <summary>Task-specific teardown: fire the success/cancelled delegate, detach subscriptions.</summary>
    protected abstract void OnEnd(AbilityTaskEndReason reason);

    /// <summary>Called once after the task is registered on the active ability.</summary>
    protected virtual void OnActivate() { }

    /// <summary>Frame update while valid, driven by <see cref="AbilitySystemComponent.Tick"/>.</summary>
    protected virtual void OnTick(float deltaTime) { }

    /// <summary>The owning ability's bound input (spec InputID) was pressed (doc §4.6.2).</summary>
    protected virtual void OnInputPressed() { }

    /// <summary>The owning ability's bound input was released (doc §4.6.2).</summary>
    protected virtual void OnInputReleased() { }

    internal void Activate()
    {
        IsValid = true;
        OnActivate();
    }

    internal void Tick(float deltaTime)
    {
        if (IsValid)
            OnTick(deltaTime);
    }

    internal void NotifyInputPressed() => OnInputPressed();
    internal void NotifyInputReleased() => OnInputReleased();

    internal void Attach(GameplayAbilitySpec spec) => Spec = spec;

    public override string ToString() =>
        $"{GetType().Name} on {Ability.GetType().Name}{(IsValid ? "" : " [ended]")}";
}
