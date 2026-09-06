using GasNet.Data;

namespace GasNet.UseCases;

/// <summary>Tags shared across the use-case scenarios (registered on first use).</summary>
public static class Tags
{
    public static readonly GameplayTag AbilityAttack = GameplayTag.RequestGameplayTag("Ability.Attack");
    public static readonly GameplayTag AbilityChannel = GameplayTag.RequestGameplayTag("Ability.Channel");
    public static readonly GameplayTag StateDead = GameplayTag.RequestGameplayTag("State.Dead");
    public static readonly GameplayTag EventHit = GameplayTag.RequestGameplayTag("Event.Hit");
    public static readonly GameplayTag EventTargetAcquired = GameplayTag.RequestGameplayTag("GameplayEvent.TargetAcquired");
    public static readonly GameplayTag DataDamage = GameplayTag.RequestGameplayTag("Data.Damage");
}

/// <summary>JSON catalog shared by the scenarios — the "content is data" half of a real game.</summary>
public static class Host
{
    private static GasNetDataCatalog? _catalog;

    public static GasNetDataCatalog Catalog =>
        _catalog ??= GasNetDataLoader.LoadCatalogFile(
            Path.Combine(AppContext.BaseDirectory, "Data", "Catalog.json"),
            new GasNetDataLoadOptions().RegisterAttributeSet<BattleAttributeSet>());
}

/// <summary>
/// Host-side attribute set (a real game defines its own). Includes the meta-attribute pattern
/// (doc §4.3.3): damage GEs write MetaDamage, PostGameplayEffectExecute redistributes to Health.
/// </summary>
public class BattleAttributeSet : AttributeSet
{
    public GameplayAttributeData MaxHealth;
    public GameplayAttributeData Health;
    public GameplayAttributeData MaxMana;
    public GameplayAttributeData Mana;
    public GameplayAttributeData Stamina;
    public GameplayAttributeData MoveSpeed;
    public GameplayAttributeData MetaDamage;

    private static GameplayAttribute A(string name) =>
        GameplayAttributeRegistry.TryGetAttribute(typeof(BattleAttributeSet), name, out var attribute)
            ? attribute
            : throw new InvalidOperationException(name);

    public static readonly GameplayAttribute MaxHealthAttr = A("MaxHealth");
    public static readonly GameplayAttribute HealthAttr = A("Health");
    public static readonly GameplayAttribute MaxManaAttr = A("MaxMana");
    public static readonly GameplayAttribute ManaAttr = A("Mana");
    public static readonly GameplayAttribute StaminaAttr = A("Stamina");
    public static readonly GameplayAttribute MoveSpeedAttr = A("MoveSpeed");
    public static readonly GameplayAttribute MetaDamageAttr = A("MetaDamage");

    public override void PreAttributeChange(GameplayAttribute attribute, ref float newValue)
    {
        if (attribute == HealthAttr) newValue = Math.Clamp(newValue, 0f, MaxHealth.CurrentValue);
        if (attribute == ManaAttr) newValue = Math.Clamp(newValue, 0f, MaxMana.CurrentValue);
        if (attribute == StaminaAttr) newValue = Math.Clamp(newValue, 0f, 1000f);
    }

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

/// <summary>Anything that owns an ASC (UE: IAbilitySystemInterface).</summary>
public sealed class Fighter : IAbilitySystemInterface
{
    public string Name { get; }
    public AbilitySystemComponent ASC { get; }

    public Fighter(string name, SimWorld world)
    {
        Name = name;
        ASC = new AbilitySystemComponent { TimeSource = world.Clock };
        ASC.InitAbilityActorInfo(this, this);
        ASC.AddSet<BattleAttributeSet>();
        ASC.InitAttributeValue(BattleAttributeSet.MaxHealthAttr, 100f);
        ASC.InitAttributeValue(BattleAttributeSet.HealthAttr, 100f);
        ASC.InitAttributeValue(BattleAttributeSet.MaxManaAttr, 50f);
        ASC.InitAttributeValue(BattleAttributeSet.ManaAttr, 50f);
        ASC.InitAttributeValue(BattleAttributeSet.StaminaAttr, 100f);
        ASC.InitAttributeValue(BattleAttributeSet.MoveSpeedAttr, 400f);
    }

    public AbilitySystemComponent? GetAbilitySystemComponent() => ASC;

    public float Health => ASC.GetNumericAttribute(BattleAttributeSet.HealthAttr);
    public float Mana => ASC.GetNumericAttribute(BattleAttributeSet.ManaAttr);
    public override string ToString() => Name;
}

/// <summary>Fixed-step manual-clock world — the deterministic server loop from docs/networking.md.</summary>
public sealed class SimWorld
{
    private readonly List<Fighter> _fighters = [];

    public ManualTimeSource Clock { get; } = new();
    public IReadOnlyList<Fighter> Fighters => _fighters;

    public Fighter Spawn(string name)
    {
        var fighter = new Fighter(name, this);
        _fighters.Add(fighter);
        return fighter;
    }

    /// <summary>Advances every ASC in fixed 0.25s steps for <paramref name="seconds"/>.</summary>
    public void Run(float seconds, float step = 0.25f)
    {
        float remaining = seconds;
        while (remaining > 0f)
        {
            float dt = Math.Min(step, remaining);
            Clock.Advance(dt);
            foreach (var fighter in _fighters)
                fighter.ASC.Tick(dt);
            remaining -= dt;
        }
    }
}
