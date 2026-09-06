namespace GasNet.UseCases;

/// <summary>
/// A channeled payoff (WaitDelay), a TagAdded-triggered interruptor that cancels it, and the
/// task-level OnCancelled consequence — then a clean recast once the stun expires.
/// </summary>
public static class ChannelAndInterruptScenario
{
    public static void Run()
    {
        var world = new SimWorld();
        var hero = world.Spawn("Hero");
        var dummy = world.Spawn("Dummy");

        var channel = new Shared.ChannelBlast { Target = dummy };
        var handle = hero.ASC.GiveAbility(new GameplayAbilitySpec(channel, 1));
        hero.ASC.GiveAbility(new GameplayAbilitySpec(new Shared.CancelChannelsOnStun(), 1));

        Console.WriteLine("  cast channel; a stun lands 1s into the 2.5s channel:");
        hero.ASC.TryActivateAbility(handle);
        world.Run(1f);
        hero.ASC.ApplyGameplayEffectToSelf(Host.Catalog.Get("Stun"));
        world.Run(0.5f); // stun tag → trigger → channel cancelled (see output)

        Console.WriteLine("  stun expires after 2s, recast completes:");
        world.Run(2f);
        hero.ASC.TryActivateAbility(handle);
        world.Run(3f); // blast lands at 2.5s
        Console.WriteLine($"  Dummy Health {dummy.Health:0.#} (took the 30 blast)");
    }
}
