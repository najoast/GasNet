namespace GasNet;

/// <summary>
/// Who created a GameplayEffectSpec and why — equivalent to UE's <c>FGameplayEffectContext</c>.
/// Subclass it to ferry arbitrary data between MMCs, ExecCalcs, AttributeSets and Cues (doc §4.5.10).
/// </summary>
public class GameplayEffectContext
{
    public object? Instigator { get; set; }
    public object? EffectCauser { get; set; }
    /// <summary>Arbitrary "source object" slot (e.g. the projectile).</summary>
    public object? SourceObject { get; set; }

    public AbilitySystemComponent? InstigatorAbilitySystemComponent { get; set; }
    /// <summary>Set at application time by the receiving ASC (its own reference).</summary>
    public AbilitySystemComponent? TargetAbilitySystemComponent { get; set; }

    public GameplayAbility? AbilityInstance { get; set; }
    public GameplayAbilitySpecHandle AbilitySpecHandle { get; set; }

    public GameplayEffectContext Clone() => (GameplayEffectContext)MemberwiseClone();
}
