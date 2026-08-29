namespace GasNet;

/// <summary>
/// Output of an execution calculation — modifiers applied as Instant changes to the target's
/// BaseValues (UE: <c>FGameplayEffectExecutionCalculation::OutExecutionOutput</c>).
/// </summary>
public sealed class ExecutionOutput
{
    private readonly List<(GameplayAttribute Attribute, GameplayModOp Op, float Magnitude)> _modifiers = [];

    public IReadOnlyList<(GameplayAttribute Attribute, GameplayModOp Op, float Magnitude)> Modifiers => _modifiers;

    public bool GameplayCuesHandledManually { get; private set; }

    public void AddOutputModifier(GameplayAttribute attribute, GameplayModOp op, float magnitude) =>
        _modifiers.Add((attribute, op, magnitude));

    /// <summary>Suppresses the engine's automatic Executed cues for this execution (doc §4.8.6).</summary>
    public void MarkGameplayCuesHandledManually() => GameplayCuesHandledManually = true;
}

/// <summary>Parameters handed to an execution calculation (UE: <c>FGameplayEffectExecutionParameters</c>).</summary>
public sealed class GameplayEffectExecutionParams
{
    public required GameplayEffectSpec Spec { get; init; }
    public required AbilitySystemComponent? Source { get; init; }
    public required AbilitySystemComponent? Target { get; init; }
    public required AggregatorEvaluateParameters EvaluateParameters { get; init; }
    public required ExecutionOutput Output { get; init; }

    /// <summary>Reads a captured attribute (register via the calc's <see cref="GameplayEffectExecutionCalculation.RelevantAttributesToCapture"/>).</summary>
    public float AttemptCalculateCapturedAttributeMagnitude(AttributeCaptureDefinition capture) =>
        GameplayEffectCaptureEvaluator.CaptureAttributeMagnitude(Spec, capture);

    public float AttemptCalculateCapturedAttributeMagnitude(GameplayAttribute attribute, GameplayAttributeCaptureSource source, bool snapshot = false) =>
        AttemptCalculateCapturedAttributeMagnitude(new AttributeCaptureDefinition(attribute, source, snapshot));

    public float GetSetByCallerMagnitude(GameplayTag tag, bool warnIfNotFound = false, float defaultIfNotFound = 0f) =>
        Spec.GetSetByCallerMagnitude(tag, warnIfNotFound, defaultIfNotFound);
}

/// <summary>
/// The most powerful attribute-change mechanism: can modify multiple attributes and do anything.
/// Usable with Instant and Periodic GEs only; not predictable (doc §4.5.12).
/// </summary>
public abstract class GameplayEffectExecutionCalculation
{
    public List<AttributeCaptureDefinition> RelevantAttributesToCapture { get; } = [];

    protected void AddCapture(GameplayAttribute attribute, GameplayAttributeCaptureSource source, bool snapshot = false) =>
        RelevantAttributesToCapture.Add(new AttributeCaptureDefinition(attribute, source, snapshot));

    public abstract void Execute(GameplayEffectExecutionParams executionParams);
}
