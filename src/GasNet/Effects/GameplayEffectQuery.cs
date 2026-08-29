namespace GasNet;

/// <summary>
/// Query for finding active GameplayEffects (UE: <c>FGameplayEffectQuery</c>). All set criteria are AND-ed.
/// </summary>
public sealed class GameplayEffectQuery
{
    /// <summary>Match GEs whose GRANTED (owning) tags contain all/any of these (UE: <c>MakeQuery_MatchAnyOwningTags</c>).</summary>
    public GameplayTagContainer? OwningTags { get; set; }
    public GameplayTagContainer? AssetTags { get; set; }
    public GameplayEffectDefinition? Def { get; set; }
    public Type? DefType { get; set; }
    public ActiveGameplayEffectHandle? Handle { get; set; }
    /// <summary>Match GEs applied from a source ASC with any of these tags.</summary>
    public GameplayTagContainer? SourceTags { get; set; }
    public Func<ActiveGameplayEffect, bool>? Custom { get; set; }

    public static GameplayEffectQuery MatchAnyOwningTags(GameplayTagContainer tags) => new() { OwningTags = tags };
    public static GameplayEffectQuery MatchAllOwningTags(GameplayTagContainer tags) => new() { OwningTags = tags };
    public static GameplayEffectQuery MatchAssetTags(GameplayTagContainer tags) => new() { AssetTags = tags };
    public static GameplayEffectQuery MatchDef(GameplayEffectDefinition def) => new() { Def = def };
    public static GameplayEffectQuery MatchDefType<TDef>() where TDef : GameplayEffectDefinition => new() { DefType = typeof(TDef) };
    public static GameplayEffectQuery MatchHandle(ActiveGameplayEffectHandle handle) => new() { Handle = handle };

    public bool Matches(ActiveGameplayEffect age)
    {
        if (Handle is { } handle && !(age.Handle == handle))
            return false;
        if (Def != null && !ReferenceEquals(age.Spec.Def, Def))
            return false;
        if (DefType != null && !DefType.IsInstanceOfType(age.Spec.Def))
            return false;
        if (OwningTags is { IsNotEmpty: true } && !age.Spec.GetAllGrantedTags().HasAny(OwningTags))
            return false;
        if (AssetTags is { IsNotEmpty: true } && !age.Spec.GetAllAssetTags().HasAny(AssetTags))
            return false;
        if (SourceTags is { IsNotEmpty: true } && !age.Spec.CapturedSourceTags.HasAny(SourceTags))
            return false;
        if (Custom != null && !Custom(age))
            return false;
        return true;
    }

    public bool IsEmpty =>
        (OwningTags is null || OwningTags.IsEmpty)
        && (AssetTags is null || AssetTags.IsEmpty)
        && Def is null && DefType is null && Handle is null
        && (SourceTags is null || SourceTags.IsEmpty)
        && Custom is null;
}

/// <summary>Handle to a live GameplayEffect in a target's active container (UE: <c>FActiveGameplayEffectHandle</c>).</summary>
public readonly struct ActiveGameplayEffectHandle : IEquatable<ActiveGameplayEffectHandle>
{
    public int Handle { get; }
    public AbilitySystemComponent? Owner { get; }

    internal ActiveGameplayEffectHandle(int handle, AbilitySystemComponent owner)
    {
        Handle = handle;
        Owner = owner;
    }

    public bool IsValid => Handle != 0 && Owner != null;

    public bool Equals(ActiveGameplayEffectHandle other) => Handle == other.Handle && ReferenceEquals(Owner, other.Owner);
    public override bool Equals(object? obj) => obj is ActiveGameplayEffectHandle other && Equals(other);
    public override int GetHashCode() => Handle;
    public override string ToString() => $"AGE:{Handle}";
    public static bool operator ==(ActiveGameplayEffectHandle a, ActiveGameplayEffectHandle b) => a.Equals(b);
    public static bool operator !=(ActiveGameplayEffectHandle a, ActiveGameplayEffectHandle b) => !a.Equals(b);
}
