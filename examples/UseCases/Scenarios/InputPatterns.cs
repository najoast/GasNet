namespace GasNet.UseCases;

/// <summary>The three input shapes (doc §4.6.2): tap, hold, and a combo window.</summary>
public static class InputPatternsScenario
{
    public static void Run()
    {
        var world = new SimWorld();
        var hero = world.Spawn("Hero");
        var dummy = world.Spawn("Dummy");

        // --- tap: one press, the ability completes on its own
        hero.ASC.GiveAbility(new GameplayAbilitySpec(new Shared.TapStrike { Target = dummy }, 1, inputID: 1));
        hero.ASC.AbilityLocalInputPressed(1);
        Console.WriteLine($"  after tap: Dummy Health {dummy.Health:0.#}");

        // --- hold: press activates, release ends (WaitInputRelease)
        hero.ASC.GiveAbility(new GameplayAbilitySpec(new Shared.HoldGuard(), 1, inputID: 2));
        hero.ASC.AbilityLocalInputPressed(2);
        world.Run(1f);
        float held = hero.ASC.GetNumericAttribute(BattleAttributeSet.MoveSpeedAttr);
        hero.ASC.AbilityLocalInputReleased(2);
        Console.WriteLine($"  hold: speed while held {held:0.#} → after release {hero.ASC.GetNumericAttribute(BattleAttributeSet.MoveSpeedAttr):0.#}");

        // --- combo: presses inside the 1s window chain hits; a lapsed window ends the ability
        hero.ASC.GiveAbility(new GameplayAbilitySpec(new Shared.ComboStrike(), 1, inputID: 3));
        Console.WriteLine("  press, then a second press 0.5s later (inside the window):");
        hero.ASC.AbilityLocalInputPressed(3);
        hero.ASC.AbilityLocalInputReleased(3);
        world.Run(0.5f);
        hero.ASC.AbilityLocalInputPressed(3);
        world.Run(1.5f); // let the re-armed window lapse
        Console.WriteLine("  (a second combo would start fresh on the next press)");
    }
}
