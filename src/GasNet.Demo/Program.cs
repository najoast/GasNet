using GasNet;
using GasNet.Sample;

namespace GasNet.Demo;

/// <summary>
/// A scripted battle demonstrating the full GAS feature set from GASDocumentation:
/// attribute init GEs, aggregation math, stacking GEs, duration/periodic effects, tag blocking,
/// SetByCaller cooldowns, event-triggered abilities, cues and death.
/// </summary>
public static class Program
{
    private static readonly ManualTimeSource Clock = new();

    private static Hero _hero = null!;
    private static Minion _minion = null!;

    public static void Main()
    {
        Console.WriteLine("=== GasNet demo: a GASDocumentation-flavored battle ===\n");

        var cueManager = AbilitySystemGlobals.Get().GameplayCueManager;
        cueManager.RegisterNotify(new LogCueNotify_Static("Fireball.Impact") { GameplayCueTags = new GameplayTagContainer(ST.CueFireballImpact) });
        cueManager.RegisterNotify(new LogCueNotify_Static("Heal.Tick") { GameplayCueTags = new GameplayTagContainer(ST.CueHealTick) });
        cueManager.RegisterNotify(new LogCueNotify_Actor("State.Stun") { GameplayCueTags = new GameplayTagContainer(ST.CueStun) });
        cueManager.RegisterNotify(new LogCueNotify_Actor("State.Sprint") { GameplayCueTags = new GameplayTagContainer(ST.CueSprint) });

        _hero = new Hero("Hero");
        _minion = new Minion("Minion");
        _hero.ASC.TimeSource = Clock;
        _minion.ASC.TimeSource = Clock;
        _hero.GrantStartupAbilities();

        _hero.ASC.OnAbilityFailed += static (_, spec, tags) =>
            Console.WriteLine($"  [GA] Activation of {spec?.Ability.GetType().Name} FAILED: [{tags}]");

        Section("1. Instant GE: damage via ExecutionCalculation (SetByCaller + Armor capture)");
        CastFireball();
        LogStats();

        Section("2. Aggregator: two slows on MoveSpeed with OnlyStrongestSlow_AllOtherMods (doc §5.7)");
        _hero.ASC.ApplyGameplayEffectToSelf(SampleGE.Slow);       // Multiply 0.5
        _hero.ASC.ApplyGameplayEffectToSelf(SampleGE.SlowHeavy);  // Multiply 0.7
        Console.WriteLine($"  MoveSpeed with 0.5 and 0.7 multipliers: {_hero.MoveSpeed} " +
                          "(only the strongest slow qualifies: 500 × 0.5 = 250; multipliers use bias-1 sums, doc §4.5.4)");

        Section("3. Duration GE expiry (manual clock + ASC.Tick)");
        Step(6.5f); // both slows last 6s
        Console.WriteLine($"  After 6.5s the slows expired → MoveSpeed back to {_hero.MoveSpeed}");

        Section("4. Stun blocks activation (ActivationBlockedTags) and auto-expires");
        _minion.ASC.ApplyGameplayEffectToTarget(SampleGE.Stun, 1f, _hero.ASC);
        Console.WriteLine("  Hero stunned: trying to cast fireball...");
        CastFireball();
        Step(2);
        Console.WriteLine("  Stun expired at t=2; casting again:");
        CastFireball();
        LogStats();

        Section("5. Passive armor stacks (+1 stack/4s, max 4); each hit consumes one stack");
        Step(16);
        Console.WriteLine($"  Armor now: {_hero.ASC.GetNumericAttribute(SampleAttributeSet.ArmorAttribute):0.#} " +
                          "(base 5 + one +10 mod - stacked GEs apply their modifiers ONCE; the stack count drives\n" +
                          "  the absorb mechanic and duration policies, exactly like UE's stack mode, doc §4.5.5)");

        Section("6. Manual gameplay event (SendGameplayEventToActor) → minion CounterAttack");
        GameplayAbilitySystemLibrary.SendGameplayEventToActor(_minion, ST.EventHit, new GameplayEventData
        {
            EventTag = ST.EventHit,
            Instigator = _hero,
            Target = _minion,
            EventMagnitude = 20f,
        });
        LogStats();

        Section("7. Periodic heal-over-time (Duration 5s, Period 1s - each tick acts like an Instant GE)");
        var heal = _hero.ASC.MakeOutgoingSpec(SampleGE.HealOverTime);
        heal.SetSetByCallerMagnitude(ST.DataHeal, 6f);
        _hero.ASC.ApplyGameplayEffectSpecToSelf(heal);
        Step(5.5f);
        Console.WriteLine("  (4 ticks fire inside the 5s window; the tick due exactly at expiry is consumed by it, matching engine CheckDuration order)");
        LogStats();

        Section("8. Sprint: input press → Infinite GE + stamina drain; input release → removed");
        _hero.ASC.AbilityLocalInputPressed(2);
        Step(3);
        Console.WriteLine($"  Stamina after 3s sprint: {_hero.ASC.GetNumericAttribute(SampleAttributeSet.StaminaAttribute):0.#}; MoveSpeed {_hero.MoveSpeed}");
        _hero.ASC.AbilityLocalInputReleased(2);
        Step(0.5f);
        Console.WriteLine($"  After release: MoveSpeed {_hero.MoveSpeed}");

        Section("9. Kill the minion — fireballs until State.Dead (loose tag)");
        int cast = 0;
        while (!_minion.IsDead && cast++ < 10)
        {
            if (!CastFireball())
                break;
            Step(3.1f); // wait out the cooldown
        }
        LogStats();

        Section("10. Non-instanced Jump + final mana");
        _hero.ASC.AbilityLocalInputPressed(3);
        Console.WriteLine($"  Hero mana: {_hero.ASC.GetNumericAttribute(SampleAttributeSet.ManaAttribute):0.#}");

        Console.WriteLine("\n=== demo finished ===");
    }

    private static int HeroArmorStacks() =>
        _hero.ASC.ActiveGameplayEffects.GetGameplayEffectCount(SampleGE.ArmorStack) > 0
            ? _hero.ASC.GetActiveEffects(GameplayEffectQuery.MatchDef(SampleGE.ArmorStack))[0].StackCount
            : 0;

    private static bool CastFireball()
    {
        var fireball = (GA_Fireball)_hero.ASC.GrantedAbilities.First(s => s.Ability is GA_Fireball).Ability;
        fireball.Target = _minion;
        bool activated = _hero.ASC.TryActivateAbilitiesByTag(new GameplayTagContainer(ST.AbilityFireball));
        if (!activated)
            Console.WriteLine("  [Demo] Fireball did NOT activate (tags/cost/cooldown — see failure line above).");
        return activated;
    }

    private static void Step(float seconds)
    {
        // Fixed-step the whole simulation: advance the clock, tick every ASC.
        const float frame = 0.25f;
        float remaining = seconds;
        while (remaining > 0)
        {
            float dt = Math.Min(frame, remaining);
            Clock.Advance(dt);
            _hero.ASC.Tick(dt);
            _minion.ASC.Tick(dt);
            remaining -= dt;
        }
    }

    private static int _sectionIndex;
    private static void Section(string title)
    {
        Console.WriteLine($"\n--- {_sectionIndex + 1}. {title} " + new string('-', Math.Max(3, 55 - title.Length)));
        _sectionIndex++;
    }

    private static void LogStats()
    {
        Console.WriteLine($"  [Stats] Hero: HP {_hero.Health:0.#} | MoveSpeed {_hero.MoveSpeed:0.#} | " +
                          $"Armor {_hero.ASC.GetNumericAttribute(SampleAttributeSet.ArmorAttribute):0.#} | " +
                          $"Mana {_hero.ASC.GetNumericAttribute(SampleAttributeSet.ManaAttribute):0.#} | " +
                          $"Stamina {_hero.ASC.GetNumericAttribute(SampleAttributeSet.StaminaAttribute):0.#} | " +
                          $"Tags [{_hero.ASC.GetOwnedGameplayTagContainer()}]");
        Console.WriteLine($"  [Stats] Minion: HP {_minion.Health:0.#} | Tags [{_minion.ASC.GetOwnedGameplayTagContainer()}]");
    }
}
