namespace GasNet.UseCases;

/// <summary>Abilities shared by the scenarios. Config comes from the JSON catalog; logic is code.</summary>
public static class Shared
{
    /// <summary>Tap-to-activate: one input press, the ability runs to completion by itself.</summary>
    public class TapStrike : GameplayAbility
    {
        public Fighter? Target { get; set; }

        protected override void ActivateAbility()
        {
            if (Target is not null)
            {
                var spec = MakeAbilityEffectSpec(Host.Catalog.Get("Damage"));
                spec.SetSetByCallerMagnitude(Tags.DataDamage, 15f);
                ApplyGameplayEffectSpecToTarget(spec, Target.ASC);
                Console.WriteLine($"    [tap] strike lands → {Target} Health {Target.Health:0.#}");
            }
            EndAbility(wasCancelled: false);
        }
    }

    /// <summary>"While input active" (doc §4.6.2.1): WaitInputRelease ends it, EndAbility removes the buff.</summary>
    public class HoldGuard : GameplayAbility
    {
        private ActiveGameplayEffectHandle _buff;

        protected override void ActivateAbility()
        {
            _buff = OwnerASC!.ApplyGameplayEffectToSelf(Host.Catalog.Get("GuardUp")) ?? default;
            Console.WriteLine("    [hold] guard up (MoveSpeed x1.5), waiting for release...");
            AbilityTask_WaitInputRelease.WaitForInputRelease(this, onRelease: () =>
            {
                Console.WriteLine("    [hold] released → buff removed");
                EndAbility(wasCancelled: false);
            });
        }

        public override void EndAbility(bool wasCancelled)
        {
            if (_buff.IsValid && OwnerASC is { } asc)
            {
                asc.RemoveActiveGameplayEffect(_buff);
                _buff = default;
            }
            base.EndAbility(wasCancelled);
        }
    }

    /// <summary>
    /// Combo window: each hit re-arms a 1s WaitDelay (the window) plus WaitInputPress (the next
    /// step); pressing in time advances the combo, letting the window lapse ends it.
    /// </summary>
    public class ComboStrike : GameplayAbility
    {
        private AbilityTask_WaitDelay? _window;
        private int _combo;

        protected override void ActivateAbility()
        {
            _combo++;
            Console.WriteLine($"    [combo] hit {_combo} — waiting for the next press or the 1s window");
            _window = AbilityTask_WaitDelay.WaitForDelay(this, 1f, onFire: () =>
            {
                Console.WriteLine("    [combo] window lapsed → combo ends");
                EndAbility(wasCancelled: false);
            });
            AbilityTask_WaitInputPress.WaitForInputPress(this, onPress: () =>
            {
                _window!.End(AbilityTaskEndReason.Cancelled); // discard the old window, re-arm below
                ActivateAbility();
            });
        }
    }

    /// <summary>Channeled blast: a delayed payoff that hosts can interrupt (see the interruptor).</summary>
    public class ChannelBlast : GameplayAbility
    {
        public Fighter? Target { get; set; }

        public ChannelBlast() => AbilityTags.AddTag(Tags.AbilityChannel);

        protected override void ActivateAbility()
        {
            Console.WriteLine("    [channel] channeling the blast (2.5s)...");
            AbilityTask_WaitDelay.WaitForDelay(this, 2.5f, onFire: () =>
                {
                    if (Target is null)
                    {
                        EndAbility(wasCancelled: false);
                        return;
                    }
                    var spec = MakeAbilityEffectSpec(Host.Catalog.Get("Damage"));
                    spec.SetSetByCallerMagnitude(Tags.DataDamage, 30f);
                    ApplyGameplayEffectSpecToTarget(spec, Target.ASC);
                    Console.WriteLine($"    [channel] blast lands → {Target} Health {Target.Health:0.#}");
                    EndAbility(wasCancelled: false);
                },
                onCancelled: () =>
                    Console.WriteLine("    [channel] interrupted before the blast (task cancelled, no payoff)"));
        }
    }

    /// <summary>Interruptor: activates on the stun tag appearing and cancels channeled abilities.</summary>
    public class CancelChannelsOnStun : GameplayAbility
    {
        public CancelChannelsOnStun()
        {
            InstancingPolicy = GameplayAbilityInstancingPolicy.NonInstanced; // stateless (doc §4.6.7)
            Triggers.Add(new AbilityTriggerData
            {
                Tag = GameplayTag.RequestGameplayTag("State.Debuff.Stun"),
                TriggerSource = AbilityTriggerSource.TagAdded,
            });
        }

        protected override void ActivateAbility()
        {
            OwnerASC!.CancelAbilities(new GameplayTagContainer(Tags.AbilityChannel));
            Console.WriteLine("    [interrupt] stun arrived → channeled abilities cancelled");
            EndAbility(wasCancelled: false);
        }
    }

    /// <summary>
    /// WaitTargetData stand-in (see README 偏差): the ability waits for a host-raised targeting
    /// event whose payload carries the chosen target.
    /// </summary>
    public class GuidedBolt : GameplayAbility
    {
        protected override void ActivateAbility()
        {
            var damage = Host.Catalog.Get("Damage");
            Console.WriteLine("    [bolt] in flight, waiting for the host to finish aiming...");
            AbilityTask_WaitGameplayEvent.WaitForEvent(this, Tags.EventTargetAcquired,
                onEvent: data =>
                {
                    if (data.Target is not Fighter target)
                    {
                        EndAbility(wasCancelled: true);
                        return;
                    }
                    var spec = MakeAbilityEffectSpec(damage);
                    spec.SetSetByCallerMagnitude(Tags.DataDamage, 18f);
                    ApplyGameplayEffectSpecToTarget(spec, target.ASC);
                    Console.WriteLine($"    [bolt] hits {target} → Health {target.Health:0.#}");
                    EndAbility(wasCancelled: false);
                },
                onCancelled: () => Console.WriteLine("    [bolt] aim cancelled → bolt fizzles"));
        }
    }

    /// <summary>Cost/cooldown/damage/burn definitions all come from the JSON catalog.</summary>
    public class CatalogFireball : GameplayAbility
    {
        public Fighter? Target { get; set; }

        public CatalogFireball()
        {
            AbilityTags.AddTag(Tags.AbilityAttack);
            CostGameplayEffect = Host.Catalog.Get("FireballCost");
            CooldownGameplayEffect = Host.Catalog.Get("FireballCooldown");
        }

        protected override void ActivateAbility()
        {
            if (!CommitAbility())
            {
                EndAbility(wasCancelled: true);
                return;
            }
            if (Target is null)
            {
                EndAbility(wasCancelled: false);
                return;
            }
            var damage = MakeAbilityEffectSpec(Host.Catalog.Get("Damage"));
            damage.SetSetByCallerMagnitude(Tags.DataDamage, 12f);
            ApplyGameplayEffectSpecToTarget(damage, Target.ASC);
            ApplyGameplayEffectSpecToTarget(MakeAbilityEffectSpec(Host.Catalog.Get("Burn")), Target.ASC);
            Console.WriteLine($"    [fireball] {Target} takes 12 and starts burning");

            // Notify the target so Event.Hit-triggered abilities (e.g. counter-attack) can react.
            Target.ASC.HandleGameplayEvent(Tags.EventHit, new GameplayEventData
            {
                EventTag = Tags.EventHit,
                Instigator = CurrentActorInfo.Owner,
                Target = Target,
                EventMagnitude = 12f,
            });
            EndAbility(wasCancelled: false);
        }
    }

    /// <summary>Event-triggered: activates from Event.Hit and returns half the damage.</summary>
    public class CounterAttack : GameplayAbility
    {
        public CounterAttack()
        {
            Triggers.Add(new AbilityTriggerData { Tag = Tags.EventHit, TriggerSource = AbilityTriggerSource.GameplayEvent });
        }

        public override bool ShouldAbilityRespondToEvent(GameplayTag eventTag, GameplayEventData eventData) =>
            eventTag == Tags.EventHit && eventData.EventMagnitude > 0f;

        protected override void ActivateAbilityFromEvent(GameplayEventData eventData)
        {
            if (eventData.Instigator is Fighter attacker)
            {
                var spec = MakeAbilityEffectSpec(Host.Catalog.Get("Damage"));
                spec.SetSetByCallerMagnitude(Tags.DataDamage, eventData.EventMagnitude * 0.5f);
                ApplyGameplayEffectSpecToTarget(spec, attacker.ASC);
                Console.WriteLine($"    [counter] {OwnerASC?.AvatarActor} returns {eventData.EventMagnitude * 0.5f:0.#} to {attacker}");
            }
            EndAbility(wasCancelled: false);
        }
    }
}
