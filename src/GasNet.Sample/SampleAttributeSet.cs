namespace GasNet.Sample;

/// <summary>All gameplay tags used by the sample project (registered lazily on first touch).</summary>
public static class ST
{
    private static GameplayTag Tag(string name) => GameplayTag.RequestGameplayTag(name);

    // ---- State / granted tags ----
    public static readonly GameplayTag StateDead = Tag("State.Dead");
    public static readonly GameplayTag StateStunned = Tag("State.Debuff.Stun");
    public static readonly GameplayTag StateSlowed = Tag("State.Debuff.Slow");
    public static readonly GameplayTag StateSprinting = Tag("State.Sprinting");

    // ---- Ability identity tags ----
    public static readonly GameplayTag AbilityAttack = Tag("Ability.Attack");
    public static readonly GameplayTag AbilityFireball = Tag("Ability.Attack.Fireball");
    public static readonly GameplayTag AbilityMovement = Tag("Ability.Movement");
    public static readonly GameplayTag AbilitySprint = Tag("Ability.Movement.Sprint");
    public static readonly GameplayTag AbilityJump = Tag("Ability.Movement.Jump");
    public static readonly GameplayTag AbilityDefense = Tag("Ability.Defense");

    // ---- Cooldown tags ----
    public static readonly GameplayTag CooldownFireball = Tag("Cooldown.Fireball");

    // ---- SetByCaller data tags ----
    public static readonly GameplayTag DataDamage = Tag("Data.Damage");
    public static readonly GameplayTag DataHeal = Tag("Data.Heal");

    // ---- Gameplay events ----
    public static readonly GameplayTag EventHit = Tag("Event.Hit");

    // ---- GE asset tags ----
    public static readonly GameplayTag EffectDamage = Tag("Effect.Damage");
    public static readonly GameplayTag EffectHeal = Tag("Effect.Heal");
    public static readonly GameplayTag EffectStun = Tag("Effect.Stun");
    public static readonly GameplayTag EffectArmorStack = Tag("Effect.ArmorStack");

    // ---- GameplayCues (mandatory "GameplayCue." root, doc §4.8.1) ----
    public static readonly GameplayTag CueFireballImpact = Tag("GameplayCue.Fireball.Impact");
    public static readonly GameplayTag CueHealTick = Tag("GameplayCue.Heal.Tick");
    public static readonly GameplayTag CueStun = Tag("GameplayCue.State.Stun");
    public static readonly GameplayTag CueSprint = Tag("GameplayCue.State.Sprint");
}

/// <summary>
/// The sample hero attribute set, mirroring GASDocumentation's <c>UGDAttributeSetBase</c>:
/// primary attributes, Max counterparts, and the <c>Damage</c>/<c>Heal</c> meta attributes (doc §4.3.3).
/// </summary>
public class SampleAttributeSet : AttributeSet
{
    // ---- Max counterparts FIRST so init-GE clamping sees them (doc §4.3.2: maxima are real attributes) ----
    public GameplayAttributeData MaxHealth;
    public GameplayAttributeData MaxMana;
    public GameplayAttributeData MaxStamina;

    public GameplayAttributeData Health;
    public GameplayAttributeData Mana;
    public GameplayAttributeData Stamina;
    public GameplayAttributeData AttackPower;
    public GameplayAttributeData Armor;
    public GameplayAttributeData MoveSpeed;

    // ---- Meta attributes (placeholders, never persisted, doc §4.3.3) ----
    public GameplayAttributeData MetaDamage;
    public GameplayAttributeData MetaHeal;

    private static GameplayAttribute A(string name) =>
        GameplayAttributeRegistry.TryGetAttribute(typeof(SampleAttributeSet), name, out var attribute)
            ? attribute
            : throw new InvalidOperationException("Missing attribute " + name);

    public static readonly GameplayAttribute HealthAttribute = A("Health");
    public static readonly GameplayAttribute MaxHealthAttribute = A("MaxHealth");
    public static readonly GameplayAttribute ManaAttribute = A("Mana");
    public static readonly GameplayAttribute MaxManaAttribute = A("MaxMana");
    public static readonly GameplayAttribute StaminaAttribute = A("Stamina");
    public static readonly GameplayAttribute MaxStaminaAttribute = A("MaxStamina");
    public static readonly GameplayAttribute AttackPowerAttribute = A("AttackPower");
    public static readonly GameplayAttribute ArmorAttribute = A("Armor");
    public static readonly GameplayAttribute MoveSpeedAttribute = A("MoveSpeed");
    public static readonly GameplayAttribute MetaDamageAttribute = A("MetaDamage");
    public static readonly GameplayAttribute MetaHealAttribute = A("MetaHeal");

    /// <summary>Clamps CurrentValue (per-query only, doc §4.4.5).</summary>
    public override void PreAttributeChange(GameplayAttribute attribute, ref float newValue)
    {
        switch (attribute.Name)
        {
            case "Health": newValue = Clamp(newValue, 0, MaxHealth.CurrentValue); break;
            case "Mana": newValue = Clamp(newValue, 0, MaxMana.CurrentValue); break;
            case "Stamina": newValue = Clamp(newValue, 0, MaxStamina.CurrentValue); break;
            case "MoveSpeed": newValue = Clamp(newValue, 100, 1000); break; // doc §4.4.5 sample clamp
        }
    }

    /// <summary>Clamps BaseValue.</summary>
    public override void PreAttributeBaseChange(GameplayAttribute attribute, ref float newValue)
    {
        switch (attribute.Name)
        {
            case "Health": newValue = Clamp(newValue, 0, MaxHealth.BaseValue); break;
            case "Mana": newValue = Clamp(newValue, 0, MaxMana.BaseValue); break;
            case "Stamina": newValue = Clamp(newValue, 0, MaxStamina.BaseValue); break;
        }
    }

    /// <summary>MoveSpeed uses the multiplier-slow qualifier: only the strongest slow applies (doc §5.7 / §4.4.7).</summary>
    public override void OnAttributeAggregatorCreated(GameplayAttribute attribute, AttributeAggregator aggregator)
    {
        base.OnAttributeAggregatorCreated(attribute, aggregator);
        if (attribute == MoveSpeedAttribute)
            aggregator.EvaluationMetaData = AggregatorEvaluateMetaData.OnlyStrongestSlow_AllOtherMods;
    }

    /// <summary>
    /// Fires ONLY after Instant GEs changed a BaseValue (doc §4.4.6). This is where meta attributes
    /// are redistributed: Damage → Health, Heal → Health.
    /// </summary>
    public override void PostGameplayEffectExecute(GameplayEffectModCallbackData data)
    {
        if (Owner is null)
            return;

        switch (data.Attribute.Name)
        {
            case "MetaDamage" when data.EvaluatedMagnitude > 0:
            {
                float damage = data.EvaluatedMagnitude;

                // Passive armor stacks: damage received consumes one stack (sample project §2).
                var stacks = Owner.ActiveGameplayEffects.GetActiveEffects(GameplayEffectQuery.MatchDef(SampleGE.ArmorStack));
                if (stacks.Count > 0)
                {
                    var stackAGE = stacks[0];
                    Owner.ActiveGameplayEffects.DecrementStack(stackAGE.Handle);
                    Console.WriteLine($"  [Set] {OwnerName()} armor stack absorbed the hit (stacks left: {stackAGE.StackCount})");
                }

                float newHealth = Math.Max(0, GetHealth() - damage);
                Owner.SetNumericAttributeBase(HealthAttribute, newHealth);
                Console.WriteLine($"  [Set] {OwnerName()} takes {damage:0.#} damage → Health {newHealth:0.#}");

                Owner.SetNumericAttributeBase(MetaDamageAttribute, 0); // meta: no persistence
                if (newHealth <= 0)
                {
                    Owner.AddLooseGameplayTag(ST.StateDead); // loose tag pattern, doc §4.2
                    Console.WriteLine($"  [Set] {OwnerName()} is DEAD (granted loose tag State.Dead)");
                }
                break;
            }

            case "MetaHeal" when data.EvaluatedMagnitude > 0:
            {
                float heal = data.EvaluatedMagnitude;
                float newHealth = Math.Min(MaxHealth.BaseValue, GetHealth() + heal);
                Owner.SetNumericAttributeBase(HealthAttribute, newHealth);
                Console.WriteLine($"  [Set] {OwnerName()} heals {heal:0.#} → Health {newHealth:0.#}");
                Owner.SetNumericAttributeBase(MetaHealAttribute, 0);
                break;
            }
        }
    }

    private float GetHealth() => Owner!.GetNumericAttribute(HealthAttribute);

    private static float Clamp(float v, float min, float max) => Math.Clamp(v, min, float.IsFinite(max) ? max : v);

    private string OwnerName() => (Owner?.AvatarActor as CombatActor)?.Name ?? Owner?.OwnerActor?.ToString() ?? "?";
}
