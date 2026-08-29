namespace GasNet.Sample;

/// <summary>
/// All sample GameplayEffect definitions. Like UE Blueprint GE subclasses, these are data-only
/// "archetype" instances shared by every spec created from them.
/// </summary>
public static class SampleGE
{
    /// <summary>Init attributes with an Instant GE (Epic-recommended init, doc §4.4.4) using Override ops.</summary>
    public static readonly GameplayEffectDefinition HeroAttributes = new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.Instant)
        .AddModifier(SampleAttributeSet.MaxHealthAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(100))
        .AddModifier(SampleAttributeSet.MaxManaAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(100))
        .AddModifier(SampleAttributeSet.MaxStaminaAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(100))
        .AddModifier(SampleAttributeSet.HealthAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(100))
        .AddModifier(SampleAttributeSet.ManaAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(100))
        .AddModifier(SampleAttributeSet.StaminaAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(100))
        .AddModifier(SampleAttributeSet.AttackPowerAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(10))
        .AddModifier(SampleAttributeSet.ArmorAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(5))
        .AddModifier(SampleAttributeSet.MoveSpeedAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(500));

    public static readonly GameplayEffectDefinition MinionAttributes = new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.Instant)
        .AddModifier(SampleAttributeSet.MaxHealthAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(60))
        .AddModifier(SampleAttributeSet.HealthAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(60))
        .AddModifier(SampleAttributeSet.ArmorAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(2))
        .AddModifier(SampleAttributeSet.MoveSpeedAttribute, GameplayModOp.Override, new ScalableFloatMagnitude(350));

    /// <summary>Instant damage via an ExecutionCalculation: SetByCaller damage mitigated by captured Armor (doc §4.5.12).</summary>
    public static readonly GameplayEffectDefinition Damage = new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.Instant)
        .WithAssetTags(ST.EffectDamage)
        .WithCueTags(ST.CueFireballImpact)
        .WithExecutions(new DamageExecution());

    /// <summary>Heal-over-time: Duration GE with Period; every tick behaves like an Instant GE (doc §4.5.1).</summary>
    public static readonly GameplayEffectDefinition HealOverTime = new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.HasDuration, duration: 5f, period: 1f)
        .WithAssetTags(ST.EffectHeal)
        .WithCueTags(ST.CueHealTick)
        .AddModifier(SampleAttributeSet.MetaHealAttribute, GameplayModOp.Add,
            new SetByCallerMagnitude(ST.DataHeal));

    /// <summary>Stun: Duration GE granting State.Debuff.Stun + an actor cue (doc §4.5.7).</summary>
    public static readonly GameplayEffectDefinition Stun = new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.HasDuration, duration: 2f)
        .WithAssetTags(ST.EffectStun)
        .WithGrantedTags(ST.StateStunned)
        .WithCueTags(ST.CueStun);

    /// <summary>Sprint buff: Infinite GE, percentage via Multiply (applies AFTER additions, doc §4.5.4).</summary>
    public static readonly GameplayEffectDefinition Sprint = new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.Infinite)
        .WithGrantedTags(ST.StateSprinting)
        .WithCueTags(ST.CueSprint)
        .AddModifier(SampleAttributeSet.MoveSpeedAttribute, GameplayModOp.Multiply, new ScalableFloatMagnitude(1.5f));

    /// <summary>Slow debuff: Duration GE with a 0.5 Multiply — pairs with MostNegativeMod_AllPositiveMods.</summary>
    public static readonly GameplayEffectDefinition Slow = new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.HasDuration, duration: 6f)
        .WithGrantedTags(ST.StateSlowed)
        .AddModifier(SampleAttributeSet.MoveSpeedAttribute, GameplayModOp.Multiply, new ScalableFloatMagnitude(0.5f));

    /// <summary>A lighter slow (0.7) — with the MostNegative qualifier only the 0.5 slow applies.</summary>
    public static readonly GameplayEffectDefinition SlowHeavy = new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.HasDuration, duration: 6f)
        .WithGrantedTags(ST.StateSlowed)
        .AddModifier(SampleAttributeSet.MoveSpeedAttribute, GameplayModOp.Multiply, new ScalableFloatMagnitude(0.7f));

    /// <summary>Stacking armor GE: infinite, +10 Armor per stack, limit 4 (doc §4.5.5).</summary>
    public static readonly GameplayEffectDefinition ArmorStack = new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.Infinite)
        .WithAssetTags(ST.EffectArmorStack)
        .WithStacking(GameplayEffectStackingType.AggregateByTarget, stackLimit: 4)
        .AddModifier(SampleAttributeSet.ArmorAttribute, GameplayModOp.Add, new ScalableFloatMagnitude(10));

    /// <summary>Fireball cost: Instant GE, magnitude from an MMC reading the ability (doc §4.5.14),
    /// plus a CAR enforcing affordability (doc §4.5.13).</summary>
    public static readonly GameplayEffectDefinition FireballCost = new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.Instant)
        .WithCustomApplicationRequirements(new CanAffordManaRequirement())
        .AddModifier(SampleAttributeSet.ManaAttribute, GameplayModOp.Add,
            new CustomCalculationMagnitude(new FireballCostMMC()));

    /// <summary>Shared-cooldown GE: SetByCaller duration + unique Cooldown tag (doc §4.5.15).</summary>
    public static readonly GameplayEffectDefinition FireballCooldown = new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.HasDuration, duration: 3f)
        .WithGrantedTags(ST.CooldownFireball);

    /// <summary>Sprint stamina drain: Instant GE applied every second while sprinting.</summary>
    public static readonly GameplayEffectDefinition StaminaDrain = new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.Instant)
        .AddModifier(SampleAttributeSet.StaminaAttribute, GameplayModOp.Add, new ScalableFloatMagnitude(-5));
}

public static class GameplayEffectDefinitionExtensions
{
    // Fluent GE authoring helpers live in the core library (GasNet.GameplayEffectDefinitionBuilder).
}

/// <summary>
/// Damage ExecutionCalculation: reads SetByCaller "Data.Damage", captures the target's Armor
/// (re-clamp not needed here), applies a 10% minimum bleed-through (doc §4.5.12 pattern).
/// </summary>
public sealed class DamageExecution : GameplayEffectExecutionCalculation
{
    public DamageExecution()
    {
        AddCapture(SampleAttributeSet.ArmorAttribute, GameplayAttributeCaptureSource.Target, snapshot: false);
    }

    public override void Execute(GameplayEffectExecutionParams p)
    {
        float damage = p.GetSetByCallerMagnitude(ST.DataDamage, warnIfNotFound: true, defaultIfNotFound: 0f);
        float armor = p.AttemptCalculateCapturedAttributeMagnitude(SampleAttributeSet.ArmorAttribute,
            GameplayAttributeCaptureSource.Target);

        float mitigated = MathF.Max(damage * 0.1f, damage - armor);
        p.Output.AddOutputModifier(SampleAttributeSet.MetaDamageAttribute, GameplayModOp.Add, mitigated);
    }
}

/// <summary>Cost MMC reading the cost value off the ability instance (doc §4.5.14 technique 1).</summary>
public sealed class FireballCostMMC : ModifierMagnitudeCalculation
{
    public override float CalculateBaseMagnitude(GameplayEffectSpec spec) =>
        -(GetAbilityInstance<GA_Fireball>(spec)?.ManaCost ?? 15f);
}

/// <summary>
/// Custom Application Requirement: the target must be able to AFFORD the cost (doc §4.5.13 use case).
/// </summary>
public sealed class CanAffordManaRequirement : GameplayEffectCustomApplicationRequirement
{
    public override bool CanGameplayEffectApply(ActiveGameplayEffectsContainer activeEffectsContainer, GameplayEffectSpec spec)
    {
        var asc = activeEffectsContainer.Owner;
        var mana = SampleAttributeSet.ManaAttribute;
        if (!asc.HasAttributeSetForAttribute(mana))
            return true;
        float cost = spec.Context?.AbilityInstance is GA_Fireball fireball ? fireball.ManaCost : 15f;
        return asc.GetNumericAttribute(mana) >= cost;
    }
}
