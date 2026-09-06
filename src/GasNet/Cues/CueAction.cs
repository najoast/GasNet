namespace GasNet;

/// <summary>
/// Base class for a single effect that a data-driven cue can fire. Each instance corresponds to
/// one entry in a cue's <c>actions</c> array; <see cref="TriggerOn"/> decides which event the cue
/// manager dispatches it on. Engine-specific subclasses (Unity VFX/SFX, Godot resources, etc.) live
/// alongside their host demo project — this assembly stays engine-agnostic.
///
/// <para>Actions are constructed by the data loader (via <see cref="System.Activator.CreateInstance"/>)
/// and dispatched by <c>GameplayCueRuntime</c>. Throwing from <see cref="Execute"/> is allowed and
/// is logged but does not abort other actions on the same cue event.</para>
/// </summary>
public abstract class CueAction
{
    /// <summary>Which cue event routes this action. Must be set before the runtime dispatches it.</summary>
    public GameplayCueEvent TriggerOn { get; set; }

    /// <summary>Engine-side side effect. Implementations should be quick and non-throwing where
    /// practical — the runtime catches exceptions and forwards them to <c>GasNetLog.OnError</c>.</summary>
    public abstract void Execute(GameplayCueParameters parameters);
}
