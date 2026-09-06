namespace GasNet;

/// <summary>
/// Fires when the ability's bound input (the spec's InputID) is pressed while the task is pending —
/// UE's <c>UAbilityTask_WaitInputPress</c> (doc §4.6.2/§4.7). Input arrives via
/// <see cref="AbilitySystemComponent.AbilityLocalInputPressed(int)"/>; a press that activated the
/// ability is not seen by tasks spawned during that same activation (they wait for the next one).
/// </summary>
public sealed class AbilityTask_WaitInputPress : AbilityTask
{
    /// <summary>Fires when the input is pressed.</summary>
    public Action? OnPress { get; set; }

    /// <summary>Fires instead of <see cref="OnPress"/> when the ability ends/cancels while waiting.</summary>
    public Action? OnCancelled { get; set; }

    private AbilityTask_WaitInputPress(GameplayAbility ability) : base(ability) { }

    public static AbilityTask_WaitInputPress? WaitForInputPress(GameplayAbility ability,
        Action? onPress = null, Action? onCancelled = null)
    {
        var task = ability.SpawnTask(new AbilityTask_WaitInputPress(ability));
        if (task is null)
            return null;
        task.OnPress = onPress;
        task.OnCancelled = onCancelled;
        return task;
    }

    protected override void OnInputPressed() => EndTask();

    protected override void OnEnd(AbilityTaskEndReason reason)
    {
        if (reason == AbilityTaskEndReason.Completed)
            OnPress?.Invoke();
        else
            OnCancelled?.Invoke();
    }
}

/// <summary>
/// Fires when the ability's bound input is released — UE's <c>UAbilityTask_WaitInputRelease</c>.
/// The "while input active" pattern (doc §4.6.2.1): spawn this in <c>ActivateAbility</c> and end
/// the ability from <see cref="OnRelease"/> instead of overriding <c>NotifyInputReleased</c>.
/// </summary>
public sealed class AbilityTask_WaitInputRelease : AbilityTask
{
    /// <summary>Fires when the input is released.</summary>
    public Action? OnRelease { get; set; }

    /// <summary>Fires instead of <see cref="OnRelease"/> when the ability ends/cancels while waiting.</summary>
    public Action? OnCancelled { get; set; }

    private AbilityTask_WaitInputRelease(GameplayAbility ability) : base(ability) { }

    public static AbilityTask_WaitInputRelease? WaitForInputRelease(GameplayAbility ability,
        Action? onRelease = null, Action? onCancelled = null)
    {
        var task = ability.SpawnTask(new AbilityTask_WaitInputRelease(ability));
        if (task is null)
            return null;
        task.OnRelease = onRelease;
        task.OnCancelled = onCancelled;
        return task;
    }

    protected override void OnInputReleased() => EndTask();

    protected override void OnEnd(AbilityTaskEndReason reason)
    {
        if (reason == AbilityTaskEndReason.Completed)
            OnRelease?.Invoke();
        else
            OnCancelled?.Invoke();
    }
}
