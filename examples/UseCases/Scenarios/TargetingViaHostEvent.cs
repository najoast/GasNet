namespace GasNet.UseCases;

/// <summary>
/// The README-documented WaitTargetData stand-in: the ability waits for a gameplay event the
/// host raises when aiming finishes; the event payload carries the chosen target.
/// </summary>
public static class TargetingViaHostEventScenario
{
    public static void Run()
    {
        var world = new SimWorld();
        var hero = world.Spawn("Hero");
        var goblin = world.Spawn("Goblin");
        var wisp = world.Spawn("Wisp");

        var handle = hero.ASC.GiveAbility(new GameplayAbilitySpec(new Shared.GuidedBolt(), 1));

        Console.WriteLine("  cast, host aims for 0.75s, then picks the goblin:");
        hero.ASC.TryActivateAbility(handle);
        world.Run(0.75f);
        hero.ASC.HandleGameplayEvent(Tags.EventTargetAcquired, new GameplayEventData
        {
            EventTag = Tags.EventTargetAcquired,
            Target = goblin,
        });
        Console.WriteLine($"  → Goblin Health {goblin.Health:0.#}, Wisp untouched at {wisp.Health:0.#}");

        Console.WriteLine("  cast again, but cancel while still aiming:");
        hero.ASC.TryActivateAbility(handle);
        hero.ASC.CancelAbility(handle);
        Console.WriteLine($"  → Goblin Health unchanged at {goblin.Health:0.#}");
    }
}
