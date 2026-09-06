namespace GasNet;

/// <summary>
/// Global GAS config singleton — equivalent to UE's <c>UAbilitySystemGlobals</c>.
/// Call <see cref="InitGlobalData"/> once at startup (engine 4.24–5.2 requirement; idempotent here).
/// </summary>
public sealed class AbilitySystemGlobals
{
    private static AbilitySystemGlobals? _instance;
    public static AbilitySystemGlobals Get() => _instance ??= new AbilitySystemGlobals();

    private bool _globalDataInitialized;

    public GameplayCueManager GameplayCueManager { get; private set; } = new();

    /// <summary>
    /// When true, failed ability activations (tag/cost/cooldown rejections) are logged via
    /// <see cref="GasNetLog.Warn"/> — handy for debugging abilities that "silently do nothing".
    /// Off by default: failing activation is routine (cooldowns, blocked tags), not exceptional.
    /// </summary>
    public bool LogActivationFailures { get; set; }

    /// <summary>Initializes the tag manager and the global cue manager. Safe to call multiple times.</summary>
    public void InitGlobalData()
    {
        if (_globalDataInitialized)
            return;
        _globalDataInitialized = true;
        GameplayTagsManager.Default.RequestGameplayTag("GameplayCue"); // ensure the cue root exists
    }
}
