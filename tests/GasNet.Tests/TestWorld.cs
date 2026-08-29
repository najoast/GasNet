using Xunit;

namespace GasNet.Tests;

/// <summary>Shared fixtures for the test suite.</summary>
public static class T
{
    public static GameplayTag Tag(string name) => GameplayTag.RequestGameplayTag(name);

    public static GameplayEffectDefinition InstantGE(GameplayAttribute attribute, GameplayModOp op, float magnitude)
    {
        var def = new GameplayEffectDefinition();
        def.DurationPolicy = GameplayEffectDurationType.Instant;
        def.AddModifier(attribute, op, new ScalableFloatMagnitude(magnitude));
        return def;
    }

    public static GameplayEffectDefinition DurationGE(GameplayAttribute attribute, GameplayModOp op,
        float magnitude, float duration, float period = 0f)
    {
        var def = new GameplayEffectDefinition();
        def.DurationPolicy = GameplayEffectDurationType.HasDuration;
        def.Duration = duration;
        def.Period = period;
        def.AddModifier(attribute, op, new ScalableFloatMagnitude(magnitude));
        return def;
    }
}

/// <summary>Minimal attribute set with doc-style clamps and a meta attribute.</summary>
public class TestAttributeSet : AttributeSet
{
    public GameplayAttributeData MaxHealth;
    public GameplayAttributeData Health;
    public GameplayAttributeData MaxMana;
    public GameplayAttributeData Mana;
    public GameplayAttributeData MoveSpeed;   // clamped 100..1000 (doc §4.4.5 sample)
    public GameplayAttributeData MetaDamage;
    public GameplayAttributeData TestA;       // derived-attribute target
    public GameplayAttributeData TestB;       // derived-attribute dependency
    public GameplayAttributeData TestC;

    private static GameplayAttribute A(string name) =>
        GameplayAttributeRegistry.TryGetAttribute(typeof(TestAttributeSet), name, out var attribute)
            ? attribute
            : throw new InvalidOperationException(name);

    public static readonly GameplayAttribute MaxHealthAttr = A("MaxHealth");
    public static readonly GameplayAttribute HealthAttr = A("Health");
    public static readonly GameplayAttribute MaxManaAttr = A("MaxMana");
    public static readonly GameplayAttribute ManaAttr = A("Mana");
    public static readonly GameplayAttribute MoveSpeedAttr = A("MoveSpeed");
    public static readonly GameplayAttribute MetaDamageAttr = A("MetaDamage");
    public static readonly GameplayAttribute TestAAttr = A("TestA");
    public static readonly GameplayAttribute TestBAttr = A("TestB");
    public static readonly GameplayAttribute TestCAttr = A("TestC");

    public override void PreAttributeChange(GameplayAttribute attribute, ref float newValue)
    {
        if (attribute == HealthAttr) newValue = Math.Clamp(newValue, 0f, MaxHealth.CurrentValue);
        if (attribute == ManaAttr) newValue = Math.Clamp(newValue, 0f, MaxMana.CurrentValue);
        if (attribute == MoveSpeedAttr) newValue = Math.Clamp(newValue, 100f, 1000f);
    }

    public override void PreAttributeBaseChange(GameplayAttribute attribute, ref float newValue)
    {
        if (attribute == HealthAttr) newValue = Math.Clamp(newValue, 0f, MaxHealth.BaseValue);
        if (attribute == ManaAttr) newValue = Math.Clamp(newValue, 0f, MaxMana.BaseValue);
    }

    /// <summary>Meta attribute redistribution (doc §4.3.3): Damage comes in, Health goes down.</summary>
    public override void PostGameplayEffectExecute(GameplayEffectModCallbackData data)
    {
        if (Owner is null)
            return;
        if (data.Attribute == MetaDamageAttr && data.EvaluatedMagnitude > 0)
        {
            float health = Owner.GetNumericAttribute(HealthAttr);
            Owner.SetNumericAttributeBase(HealthAttr, Math.Max(0f, health - data.EvaluatedMagnitude));
            Owner.SetNumericAttributeBase(MetaDamageAttr, 0f);
        }
    }
}

/// <summary>ASC + manual clock + attribute set, ready to use.</summary>
public sealed class TestWorld
{
    public ManualTimeSource Clock { get; } = new();
    public AbilitySystemComponent Source { get; }
    public AbilitySystemComponent Target { get; }

    public TestWorld()
    {
        Source = MakeASC("Source");
        Target = MakeASC("Target");
    }

    private AbilitySystemComponent MakeASC(string name)
    {
        var asc = new AbilitySystemComponent();
        var owner = new NamedActor(name, asc);
        asc.TimeSource = Clock; // shared manual clock drives both ASCs deterministically
        asc.InitAbilityActorInfo(owner, owner);
        asc.AddSet<TestAttributeSet>();
        asc.InitAttributeValue(TestAttributeSet.MaxHealthAttr, 100f);
        asc.InitAttributeValue(TestAttributeSet.HealthAttr, 100f);
        asc.InitAttributeValue(TestAttributeSet.MaxManaAttr, 50f);
        asc.InitAttributeValue(TestAttributeSet.ManaAttr, 50f);
        asc.InitAttributeValue(TestAttributeSet.MoveSpeedAttr, 500f);
        asc.InitAttributeValue(TestAttributeSet.TestAAttr, 10f);
        asc.InitAttributeValue(TestAttributeSet.TestBAttr, 5f);
        asc.InitAttributeValue(TestAttributeSet.TestCAttr, 2f);
        return asc;
    }

    /// <summary>Frame-stepped tick (0.25s) — mirrors how a real host ticks the ASC every frame.</summary>
    public void Tick(float dt)
    {
        float remaining = dt;
        while (remaining > 0f)
        {
            float step = Math.Min(0.25f, remaining);
            Clock.Advance(step);
            Source.Tick(step);
            Target.Tick(step);
            remaining -= step;
        }
    }
}

public sealed class NamedActor : IAbilitySystemInterface
{
    private readonly string _name;
    public AbilitySystemComponent ASC { get; }
    public AbilitySystemComponent? GetAbilitySystemComponent() => ASC;
    public NamedActor(string name, AbilitySystemComponent asc)
    {
        _name = name;
        ASC = asc;
    }
    public override string ToString() => _name;
}
