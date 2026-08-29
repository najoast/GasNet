namespace GasNet;

/// <summary>
/// A runtime instantiation of a <see cref="GameplayEffectDefinition"/> — equivalent to UE's
/// <c>FGameplayEffectSpec</c>. Created via <see cref="AbilitySystemComponent.MakeOutgoingSpec"/>;
/// freely customizable before application (doc §4.5.9).
/// </summary>
public sealed class GameplayEffectSpec
{
    public GameplayEffectDefinition Def { get; }
    public float Level { get; set; }
    /// <summary>Effective duration in seconds; defaults to the def's, may be overridden per spec.</summary>
    public float Duration { get; set; }
    /// <summary>Effective period; defaults to the def's.</summary>
    public float Period { get; set; }

    public GameplayEffectContext Context { get; }

    /// <summary>Extra granted tags delivered in addition to <see cref="GameplayEffectDefinition.GrantedTags"/>.</summary>
    public GameplayTagContainer DynamicGrantedTags { get; } = new();
    /// <summary>Extra asset tags in addition to the def's asset tags.</summary>
    public GameplayTagContainer DynamicAssetTags { get; } = new();

    private readonly Dictionary<GameplayTag, float> _setByCallerTagMagnitudes = [];

    /// <summary>Read-only view of the SetByCaller map (used by cue parameter filling).</summary>
    public IReadOnlyDictionary<GameplayTag, float> SetByCallerMagnitudes => _setByCallerTagMagnitudes;

    /// <summary>Source ASC tags captured when the spec was CREATED (doc §4.5.4.2).</summary>
    public GameplayTagContainer CapturedSourceTags { get; } = new();
    /// <summary>Target ASC tags captured when the spec was APPLIED/executed.</summary>
    public GameplayTagContainer CapturedTargetTags { get; } = new();

    internal GameplayEffectSpec(GameplayEffectDefinition def, float level, GameplayEffectContext context)
    {
        Def = def;
        Level = level;
        Context = context;
        Duration = def.Duration;
        Period = def.Period;
    }

    public void SetSetByCallerMagnitude(GameplayTag dataTag, float magnitude)
    {
        if (!dataTag.IsValid)
            throw new ArgumentException("SetByCaller tag must be valid.");
        _setByCallerTagMagnitudes[dataTag] = magnitude;
    }

    public float GetSetByCallerMagnitude(GameplayTag dataTag, bool warnIfNotFound = true, float defaultIfNotFound = 0f)
    {
        if (_setByCallerTagMagnitudes.TryGetValue(dataTag, out float value))
            return value;
        if (warnIfNotFound)
            GasNetLog.Error($"SetByCaller magnitude for tag '{dataTag.Name}' not found on spec of '{Def.GetType().Name}'. Returning default {defaultIfNotFound}. (Dangerous for Divide modifiers!)");
        return defaultIfNotFound;
    }

    public bool HasSetByCallerMagnitude(GameplayTag dataTag) => _setByCallerTagMagnitudes.ContainsKey(dataTag);

    public GameplayTagContainer GetAllGrantedTags()
    {
        var tags = Def.GrantedTags.Clone();
        tags.AddTags(DynamicGrantedTags);
        return tags;
    }

    public GameplayTagContainer GetAllAssetTags()
    {
        var tags = Def.AssetTags.Clone();
        tags.AddTags(DynamicAssetTags);
        return tags;
    }

    public GameplayTagContainer GetAllCueTags() => Def.GameplayCueTags.Clone();

    public override string ToString() => $"{Def.GetType().Name} (Lv {Level})";
}
