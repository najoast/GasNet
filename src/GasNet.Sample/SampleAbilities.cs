namespace GasNet.Sample;

/// <summary>Fireball: InstancedPerActor attack with an MMC cost, shared SetByCaller cooldown, and a damage GE applied to the target.</summary>
public class GA_Fireball : GameplayAbility
{
    public float ManaCost { get; set; } = 15f;
    public float BaseDamage { get; set; } = 25f;
    /// <summary>Default target when the ability is activated by input instead of by event.</summary>
    public CombatActor? Target { get; set; }

    public GA_Fireball()
    {
        AbilityTags.AddTags(new GameplayTagContainer(ST.AbilityAttack, ST.AbilityFireball));
        InstancingPolicy = GameplayAbilityInstancingPolicy.InstancedPerActor;
        NetExecutionPolicy = GameplayAbilityNetExecutionPolicy.ServerOnly;
        // Stunned or dead casters fail with Activation.Fail.BlockedByTags (doc 4.6.4.2).
        ActivationBlockedTags.AddTags(new GameplayTagContainer(ST.StateStunned, ST.StateDead));

        CostGameplayEffect = SampleGE.FireballCost;
        CooldownGameplayEffect = SampleGE.FireballCooldown;
        CooldownTags.AddTags(new GameplayTagContainer(ST.CooldownFireball));
        CooldownDuration = 3f;
    }

    protected override void ActivateAbility()
    {
        if (!CommitAbility())
        {
            Console.WriteLine("  [GA] Fireball commit failed (cost or cooldown) - ending cancelled.");
            EndAbility(wasCancelled: true);
            return;
        }

        var targetASC = ResolveTarget();
        if (targetASC is null)
        {
            EndAbility(wasCancelled: false);
            return;
        }

        var damageSpec = MakeAbilityEffectSpec(SampleGE.Damage);
        damageSpec.SetSetByCallerMagnitude(ST.DataDamage, BaseDamage * GetAbilityLevel());
        ApplyGameplayEffectSpecToTarget(damageSpec, targetASC);
        Console.WriteLine($"  [GA] Fireball casts at {TargetName(targetASC)} for {BaseDamage * GetAbilityLevel():0.#} raw damage.");

        // Notify the target's ASC about the hit - abilities with an Event.Hit trigger (e.g. a
        // counter-attack) will activate from this gameplay event (doc 4.6.4 / 4.6.11).
        targetASC.HandleGameplayEvent(ST.EventHit, new GameplayEventData
        {
            EventTag = ST.EventHit,
            Instigator = CurrentActorInfo.Owner,
            Target = targetASC.AvatarActor,
            EventMagnitude = BaseDamage * GetAbilityLevel(),
        });

        EndAbility(wasCancelled: false);
    }

    private AbilitySystemComponent? ResolveTarget() =>
        CurrentEventData?.Target is CombatActor eventTarget
            ? eventTarget.ASC
            : Target?.ASC;

    private static string TargetName(AbilitySystemComponent asc) =>
        (asc.AvatarActor as CombatActor)?.Name ?? "?";
}

/// <summary>
/// Sprint: applied as an Infinite GE on activation; drains stamina every second; ends on input
/// release or when stamina runs out (sample project: "constantly draining stamina to sprint").
/// Built entirely from ability tasks: a WaitInputRelease ends the ability, and a chained
/// WaitDelay replaces the hand-written per-second accumulator.
/// </summary>
public class GA_Sprint : GameplayAbility
{
    private ActiveGameplayEffectHandle _sprintEffect;

    public GA_Sprint()
    {
        AbilityTags.AddTag(ST.AbilitySprint);
        InstancingPolicy = GameplayAbilityInstancingPolicy.InstancedPerActor;
    }

    protected override void ActivateAbility()
    {
        _sprintEffect = OwnerASC!.ApplyGameplayEffectToSelf(SampleGE.Sprint) ?? default;
        Console.WriteLine("  [GA] Sprint started (MoveSpeed x1.5).");

        ScheduleDrain();
        AbilityTask_WaitInputRelease.WaitForInputRelease(this,
            onRelease: () =>
            {
                Console.WriteLine("  [GA] Sprint input released.");
                EndAbility(wasCancelled: false);
            });
    }

    // One-shot delay task per drain tick; re-arm until out of stamina (doc §4.7 task chaining).
    private void ScheduleDrain()
    {
        AbilityTask_WaitDelay.WaitForDelay(this, 1f, onFire: () =>
        {
            OwnerASC!.ApplyGameplayEffectToSelf(SampleGE.StaminaDrain);
            if (OwnerASC.GetNumericAttribute(SampleAttributeSet.StaminaAttribute) <= 0f)
            {
                Console.WriteLine("  [GA] Sprint ended: out of stamina.");
                EndAbility(wasCancelled: true);
                return;
            }
            ScheduleDrain();
        });
    }

    public override void EndAbility(bool wasCancelled)
    {
        if (_sprintEffect.IsValid && OwnerASC is { } asc)
        {
            asc.RemoveActiveGameplayEffect(_sprintEffect);
            _sprintEffect = default;
        }
        base.EndAbility(wasCancelled);
    }
}

/// <summary>Jump: Non-Instanced - runs on the shared prototype, no state allowed (doc 4.6.7).</summary>
public class GA_Jump : GameplayAbility
{
    public GA_Jump()
    {
        AbilityTags.AddTag(ST.AbilityJump);
        InstancingPolicy = GameplayAbilityInstancingPolicy.NonInstanced;
    }

    protected override void ActivateAbility()
    {
        Console.WriteLine("  [GA] Jump! (non-instanced: executed on the shared prototype)");
        EndAbility(wasCancelled: false);
    }
}

/// <summary>
/// Passive: auto-activates on grant (bActivateAbilityOnGranted, doc 4.6.4.1) and adds one armor
/// stack every 4 seconds, up to 4 (sample project "Passive Armor Stacks"). The repeating timer is
/// a self-rearming WaitDelay chain; the passive stays active, so the chain never ends on its own.
/// </summary>
public class GA_PassiveArmorStacks : GameplayAbility
{
    public GA_PassiveArmorStacks()
    {
        AbilityTags.AddTag(ST.AbilityDefense);
        InstancingPolicy = GameplayAbilityInstancingPolicy.InstancedPerActor;
        bActivateAbilityOnGranted = true;
    }

    protected override void ActivateAbility()
    {
        Console.WriteLine("  [GA] Passive Armor Stacks online (+1 stack / 4s, max 4).");
        ScheduleNextStack();
    }

    private void ScheduleNextStack()
    {
        AbilityTask_WaitDelay.WaitForDelay(this, 4f, onFire: () =>
        {
            // Re-applying a stacking GE increments the stack count up to StackLimitCount (doc 4.5.5).
            OwnerASC!.ApplyGameplayEffectToSelf(SampleGE.ArmorStack);
            Console.WriteLine($"  [GA] Passive granted an armor stack (now {CurrentStackCount()}/4).");
            ScheduleNextStack();
        });
    }

    private int CurrentStackCount()
    {
        var effects = OwnerASC!.GetActiveEffects(GameplayEffectQuery.MatchDef(SampleGE.ArmorStack));
        return effects.Count > 0 ? effects[0].StackCount : 0;
    }
}

/// <summary>
/// Triggered ability: activates from the "Event.Hit" gameplay event and counter-attacks the
/// instigator for half the received damage (doc 4.6.4 event activation).
/// </summary>
public class GA_CounterAttack : GameplayAbility
{
    public GA_CounterAttack()
    {
        AbilityTags.AddTag(ST.AbilityAttack);
        InstancingPolicy = GameplayAbilityInstancingPolicy.InstancedPerExecution;
        Triggers.Add(new AbilityTriggerData { Tag = ST.EventHit, TriggerSource = AbilityTriggerSource.GameplayEvent });
        ActivationBlockedTags.AddTags(new GameplayTagContainer(ST.StateStunned, ST.StateDead));
    }

    public override bool ShouldAbilityRespondToEvent(GameplayTag eventTag, GameplayEventData eventData) =>
        eventTag == ST.EventHit && eventData.EventMagnitude > 0f;

    protected override void ActivateAbilityFromEvent(GameplayEventData eventData)
    {
        var instigatorASC = AbilitySystemComponent.FindASC(eventData.Instigator);
        if (instigatorASC is null)
        {
            EndAbility(wasCancelled: false);
            return;
        }

        float counterDamage = eventData.EventMagnitude * 0.5f;
        var spec = MakeAbilityEffectSpec(SampleGE.Damage);
        spec.SetSetByCallerMagnitude(ST.DataDamage, counterDamage);
        ApplyGameplayEffectSpecToTarget(spec, instigatorASC);
        Console.WriteLine($"  [GA] CounterAttack! Deals {counterDamage:0.#} back to the attacker.");
        EndAbility(wasCancelled: false);
    }
}
