namespace GasNet.Sample;

/// <summary>Console-logging cue handlers standing in for particles/audio (doc §4.8).</summary>
public class LogCueNotify_Static : GameplayCueNotify_Static
{
    private readonly string _label;

    public LogCueNotify_Static(string label) => _label = label;

    public override void OnExecute(GameplayCueParameters parameters) =>
        Console.WriteLine($"  [Cue:{_label}] EXECUTED on {Describe(parameters)} (mag {parameters.Magnitude:0.#})");

    private static string Describe(GameplayCueParameters p) =>
        (p.Target as CombatActor)?.Name ?? p.Target?.ToString() ?? "?";
}

public class LogCueNotify_Actor : GameplayCueNotify_Actor
{
    private readonly string _label;

    public LogCueNotify_Actor(string label) => _label = label;

    public override void OnActive(GameplayCueParameters parameters) =>
        Console.WriteLine($"  [Cue:{_label}] ADDED on {Describe(parameters)} (actor cue instance created)");

    public override void WhileActive(GameplayCueParameters parameters) { }

    public override void OnRemove(GameplayCueParameters parameters) =>
        Console.WriteLine($"  [Cue:{_label}] REMOVED from {Describe(parameters)} (actor cue instance destroyed)");

    private static string Describe(GameplayCueParameters p) =>
        (p.Target as CombatActor)?.Name ?? p.Target?.ToString() ?? "?";
}

/// <summary>
/// Base actor owning an ASC — the C# analogue of a Character implementing IAbilitySystemInterface.
/// </summary>
public abstract class CombatActor : IAbilitySystemInterface
{
    public string Name { get; }
    public AbilitySystemComponent ASC { get; } = new();

    public AbilitySystemComponent? GetAbilitySystemComponent() => ASC;

    protected CombatActor(string name)
    {
        Name = name;
        ASC.InitAbilityActorInfo(this, this);
        ASC.AddSet<SampleAttributeSet>();

        var set = ASC.GetSet<SampleAttributeSet>()!;
        ASC.GetGameplayAttributeValueChangeDelegate(SampleAttributeSet.HealthAttribute).Handler += OnHealthChanged;
        ASC.GetGameplayAttributeValueChangeDelegate(SampleAttributeSet.MoveSpeedAttribute).Handler += OnMoveSpeedChanged;
        ASC.RegisterGameplayTagEvent(ST.StateDead, GameplayTagEventType.NewOrRemoved, OnDeadTagChanged);
    }

    /// <summary>Initializes attributes via an Instant GE (Epic-recommended, doc §4.4.4).</summary>
    protected void InitAttributes(GameplayEffectDefinition initGE) =>
        ASC.ApplyGameplayEffectToSelf(initGE);

    public float Health => ASC.GetNumericAttribute(SampleAttributeSet.HealthAttribute);
    public float MoveSpeed => ASC.GetNumericAttribute(SampleAttributeSet.MoveSpeedAttribute);
    public bool IsDead => ASC.HasMatchingGameplayTag(ST.StateDead);

    private void OnHealthChanged(OnAttributeChangeData data) { /* logged by the attribute set */ }

    private void OnMoveSpeedChanged(OnAttributeChangeData data) =>
        Console.WriteLine($"  [Attr] {Name} MoveSpeed {data.OldValue:0.#} → {data.NewValue:0.#}");

    private void OnDeadTagChanged(GameplayTag tag, int newCount) { /* logged by the attribute set */ }
}

public sealed class Hero : CombatActor
{
    public Hero(string name) : base(name)
    {
        InitAttributes(SampleGE.HeroAttributes);
    }

    public void GrantStartupAbilities()
    {
        ASC.GiveAbility(new GameplayAbilitySpec(new GA_Fireball(), 1, inputID: 1));
        ASC.GiveAbility(new GameplayAbilitySpec(new GA_Sprint(), 1, inputID: 2));
        ASC.GiveAbility(new GameplayAbilitySpec(new GA_Jump(), 1, inputID: 3));
        ASC.GiveAbility(new GameplayAbilitySpec(new GA_PassiveArmorStacks(), 1)); // passive, auto-activates
    }
}

public sealed class Minion : CombatActor
{
    public Minion(string name) : base(name)
    {
        InitAttributes(SampleGE.MinionAttributes);
        ASC.GiveAbility(new GameplayAbilitySpec(new GA_CounterAttack(), 1));
    }
}
