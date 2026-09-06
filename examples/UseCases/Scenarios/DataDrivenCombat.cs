namespace GasNet.UseCases;

/// <summary>
/// Cost/cooldown/damage/burn definitions all come from the JSON catalog; the ability class only
/// wires them together. Also surfaces the on-cooldown rejection as a normal return value (the
/// debug-only <c>LogActivationFailures</c> switch is left off here so the demo output stays clean).
/// </summary>
public static class DataDrivenCombatScenario
{
    public static void Run()
    {
        var world = new SimWorld();
        var hero = world.Spawn("Hero");
        var dummy = world.Spawn("Dummy");

        var handle = hero.ASC.GiveAbility(new GameplayAbilitySpec(new Shared.CatalogFireball { Target = dummy }, 1));

        Console.WriteLine("  cast 1 (t=0): pays mana, lands damage + burn:");
        hero.ASC.TryActivateAbility(handle);
        Console.WriteLine($"    mana {hero.Mana:0.#} (cap {hero.ASC.GetNumericAttribute(BattleAttributeSet.MaxManaAttr):0.#}), Dummy Health {dummy.Health:0.#}");
        world.Run(1.2f);
        Console.WriteLine($"    after a burn tick: Dummy Health {dummy.Health:0.#}");

        Console.WriteLine("  cast 2 at t=1.2 → on cooldown, rejected (TryActivate returns false):");
        Console.WriteLine($"    activated: {hero.ASC.TryActivateAbility(handle)}");

        Console.WriteLine("  cast 3 after the 2s cooldown expires:");
        world.Run(1.8f);
        hero.ASC.TryActivateAbility(handle);
        Console.WriteLine($"    mana {hero.Mana:0.#}, Dummy Health {dummy.Health:0.#}");
    }
}
