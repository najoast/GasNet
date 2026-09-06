namespace GasNet.UseCases;

/// <summary>
/// docs/networking.md as executable code: the server runs GasNet; a bridge subscribes to its
/// events and prints plain protocol lines ("what the client would receive"). The client side
/// only sends input intents — it never touches GasNet.
/// </summary>
public static class NetworkBridgeSimulationScenario
{
    public static void Run()
    {
        var world = new SimWorld();
        var hero = world.Spawn("Hero");
        var brute = world.Spawn("Brute");

        var bridge = new Bridge();
        bridge.Watch("hero", hero);
        bridge.Watch("brute", brute);

        hero.ASC.GiveAbility(new GameplayAbilitySpec(new Shared.CatalogFireball { Target = brute }, 1, inputID: 1));
        brute.ASC.GiveAbility(new GameplayAbilitySpec(new Shared.CounterAttack(), 1));

        Console.WriteLine("  client sends an input intent (server-side only):");
        bridge.FromClient("hero", inputId: 1);

        Console.WriteLine("  server ticks 3.5s (burn ticks, counter-attack, cooldown):");
        world.Run(3.5f);
        Console.WriteLine($"  state check — Hero {hero.Health:0.#}, Brute {brute.Health:0.#}");
    }

    /// <summary>Protocol bridge: GasNet events → DTO lines. No GasNet type crosses this line.</summary>
    private sealed class Bridge
    {
        private readonly Dictionary<string, Fighter> _entities = [];

        public void Watch(string entityId, Fighter fighter)
        {
            _entities[entityId] = fighter;

            fighter.ASC.GetGameplayAttributeValueChangeDelegate(BattleAttributeSet.HealthAttr).Handler += d =>
                Emit(entityId, $"AttrChanged(Health, {d.NewValue:0.#})");

            fighter.ASC.RegisterGameplayTagEvent(Tags.StateDead, GameplayTagEventType.NewOrRemoved, (tag, count) =>
                Emit(entityId, count > 0 ? "TagAdded(State.Dead)" : "TagRemoved(State.Dead)"));

            fighter.ASC.OnGameplayEffectAppliedToSelf += (_, spec, _) =>
                Emit(entityId, $"EffectApplied(policy={spec.Def.DurationPolicy}"
                    + (spec.Def.GrantedTags.IsNotEmpty ? $", grants [{spec.Def.GrantedTags}]" : "") + ")");

            fighter.ASC.OnAnyGameplayEffectRemoved += (_, _) => Emit(entityId, "EffectRemoved");

            fighter.ASC.OnAbilityActivated += (_, spec) => Emit(entityId, $"AbilityActivated({spec.Ability.GetType().Name})");
            fighter.ASC.OnAbilityEnded += (spec, _) => Emit(entityId, $"AbilityEnded({spec.Ability.GetType().Name})");
        }

        /// <summary>The client's only channel: an input intent the server routes into the ASC.</summary>
        public void FromClient(string entityId, int inputId)
        {
            Console.WriteLine($"    ← client | {entityId}: InputPressed({inputId})");
            _entities[entityId].ASC.AbilityLocalInputPressed(inputId);
        }

        private static void Emit(string entityId, string message) =>
            Console.WriteLine($"    → client | {entityId}: {message}");
    }
}
