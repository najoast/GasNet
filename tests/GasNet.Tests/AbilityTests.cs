using Xunit;

namespace GasNet.Tests;

public class AbilityTests
{
    // ---------------- Grant / activate / end lifecycle (doc §4.6.3, §4.6.4) ----------------

    [Fact]
    public void Grant_Activate_End_Lifecycle_Fires_Events()
    {
        var world = new TestWorld();
        var asc = world.Target;

        GameplayAbilitySpec? activated = null, ended = null;
        bool? endedCancelled = null;
        asc.OnAbilityActivated += (_, spec) => activated = spec;
        asc.OnAbilityEnded += (spec, cancelled) => { ended = spec; endedCancelled = cancelled; };

        var spec = new GameplayAbilitySpec(new InstantEndAbility(), 1);
        var handle = asc.GiveAbility(spec);

        Assert.True(asc.TryActivateAbility(handle));
        Assert.NotNull(activated);
        Assert.NotNull(ended);
        Assert.False(endedCancelled);
        Assert.False(spec.IsActive); // ended synchronously
    }

    [Fact]
    public void ActivationOwnedTags_Granted_While_Active_And_Removed_On_End()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ownedTag = T.Tag("State.AbilityActive");

        var ability = new LongRunningAbility();
        ability.ActivationOwnedTags.AddTag(ownedTag);
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));

        Assert.False(asc.HasMatchingGameplayTag(ownedTag));
        Assert.True(asc.TryActivateAbility(handle));
        Assert.True(asc.HasMatchingGameplayTag(ownedTag));

        asc.CancelAllAbilities();
        Assert.False(asc.HasMatchingGameplayTag(ownedTag));
    }

    // ---------------- Tag requirements (doc §4.6.9) ----------------

    [Fact]
    public void ActivationBlockedTags_Fail_While_Tag_Is_Present()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var stunTag = T.Tag("State.Debuff.Stun");

        var ability = new InstantEndAbility();
        ability.ActivationBlockedTags.AddTag(stunTag);
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));

        asc.AddLooseGameplayTag(stunTag);
        Assert.False(asc.TryActivateAbility(handle));

        asc.RemoveLooseGameplayTag(stunTag);
        Assert.True(asc.TryActivateAbility(handle));
    }

    [Fact]
    public void ActivationRequiredTags_Fail_When_Missing()
    {
        var world = new TestWorld();
        var asc = world.Target;

        var ability = new InstantEndAbility();
        ability.ActivationRequiredTags.AddTag(T.Tag("State.Buffed"));
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));

        Assert.False(asc.TryActivateAbility(handle));
        asc.AddLooseGameplayTag(T.Tag("State.Buffed"));
        Assert.True(asc.TryActivateAbility(handle));
    }

    [Fact]
    public void BlockAbilitiesWithTag_Blocks_Other_Abilities_While_Active()
    {
        var world = new TestWorld();
        var asc = world.Target;

        var blocker = new LongRunningAbility();
        blocker.BlockAbilitiesWithTag.AddTag(T.Tag("Ability.Attack"));
        var blockerHandle = asc.GiveAbility(new GameplayAbilitySpec(blocker, 1));
        Assert.True(asc.TryActivateAbility(blockerHandle));

        var attack = new InstantEndAbility();
        attack.AbilityTags.AddTag(T.Tag("Ability.Attack"));
        var attackHandle = asc.GiveAbility(new GameplayAbilitySpec(attack, 1));

        Assert.False(asc.TryActivateAbility(attackHandle)); // blocked by the active ability

        asc.CancelAllAbilities();
        Assert.True(asc.TryActivateAbility(attackHandle));
    }

    [Fact]
    public void CancelAbilitiesWithTag_Cancels_Others_On_Activation()
    {
        var world = new TestWorld();
        var asc = world.Target;

        var victim = new LongRunningAbility();
        victim.AbilityTags.AddTag(T.Tag("Ability.Channel"));
        var victimHandle = asc.GiveAbility(new GameplayAbilitySpec(victim, 1));
        Assert.True(asc.TryActivateAbility(victimHandle));

        var caster = new InstantEndAbility();
        caster.CancelAbilitiesWithTag.AddTag(T.Tag("Ability.Channel"));
        asc.GiveAbility(new GameplayAbilitySpec(caster, 1));
        Assert.True(asc.TryActivateAbilityByClass<InstantEndAbility>());

        Assert.False(victim.IsActive); // cancelled by the new activation
    }

    // ---------------- Cost & cooldown (doc §4.5.14, §4.5.15, §4.6.12) ----------------

    [Fact]
    public void CommitAbility_Pays_Cost_And_Starts_SetByCaller_Cooldown()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var cooldownTag = T.Tag("Cooldown.Fireball");

        var cooldownDef = new GameplayEffectDefinition()
            .With(policy: GameplayEffectDurationType.HasDuration, duration: 3f);
        cooldownDef.GrantedTags.AddTag(cooldownTag);

        var ability = new CommittingAbility
        {
            CostGameplayEffect = T.InstantGE(TestAttributeSet.ManaAttr, GameplayModOp.Add, -15f),
            CooldownGameplayEffect = cooldownDef,
            CooldownDuration = 2f,
        };
        ability.CooldownTags.AddTag(cooldownTag);

        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));

        Assert.True(asc.TryActivateAbility(handle));
        Assert.Equal(35f, asc.GetNumericAttribute(TestAttributeSet.ManaAttr)); // 50 - 15
        Assert.True(asc.HasMatchingGameplayTag(cooldownTag));

        var query = GameplayEffectQuery.MatchAnyOwningTags(new GameplayTagContainer(cooldownTag));
        var (remaining, duration) = asc.GetActiveEffectsTimeRemainingAndDuration(query).First();
        Assert.Equal(2f, remaining, 2); // ability CooldownDuration injected via SetByCaller 'Data.Cooldown'
        Assert.Equal(2f, duration, 2);

        // On cooldown → blocked with Activation.Fail.OnCooldown (doc §4.6.4.2).
        GameplayTagContainer? failureTags = null;
        asc.OnAbilityFailed += (_, _, tags) => failureTags = tags;
        Assert.False(asc.TryActivateAbility(handle));
        Assert.NotNull(failureTags);
        Assert.True(failureTags!.HasTag(GameplayAbilityFailTags.Cooldown));

        world.Tick(2.5f);
        Assert.False(asc.HasMatchingGameplayTag(cooldownTag)); // expired
        Assert.True(asc.TryActivateAbility(handle));
        Assert.Equal(20f, asc.GetNumericAttribute(TestAttributeSet.ManaAttr));
    }

    [Fact]
    public void Unaffordable_Cost_Fails_With_Cost_Fail_Tag_And_Pays_Nothing()
    {
        var world = new TestWorld();
        var asc = world.Target;
        asc.SetNumericAttributeBase(TestAttributeSet.ManaAttr, 5f); // poor

        var costDef = T.InstantGE(TestAttributeSet.ManaAttr, GameplayModOp.Add, -15f);
        costDef.CustomApplicationRequirements.Add(new ManaAffordabilityCAR(TestAttributeSet.ManaAttr));

        var ability = new InstantEndAbility { CostGameplayEffect = costDef };
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));

        Assert.False(asc.CanActivateAbility(handle));
        Assert.False(asc.TryActivateAbility(handle));
        Assert.Equal(5f, asc.GetNumericAttribute(TestAttributeSet.ManaAttr));
    }

    private sealed class ManaAffordabilityCAR(GameplayAttribute manaAttr) : GameplayEffectCustomApplicationRequirement
    {
        public override bool CanGameplayEffectApply(ActiveGameplayEffectsContainer container, GameplayEffectSpec spec) =>
            container.Owner.GetNumericAttribute(manaAttr) >= 15f;
    }

    // ---------------- Instancing policies (doc §4.6.7) ----------------

    [Fact]
    public void InstancedPerExecution_Creates_A_Fresh_Instance_Each_Activation()
    {
        var world = new TestWorld();
        var asc = world.Target;
        CountingAbility.Executions = 0;
        CountingAbility.LastInstanceActivationCount = 0;

        var ability = new CountingAbility { InstancingPolicy = GameplayAbilityInstancingPolicy.InstancedPerExecution };
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));

        asc.TryActivateAbility(handle);
        asc.TryActivateAbility(handle);

        Assert.Equal(2, CountingAbility.Executions);              // two instances ran
        Assert.Equal(1, CountingAbility.LastInstanceActivationCount); // the second had fresh state
    }

    [Fact]
    public void InstancedPerActor_Persists_State_Between_Activations()
    {
        var world = new TestWorld();
        var asc = world.Target;
        CountingAbility.Executions = 0;
        CountingAbility.LastInstanceActivationCount = 0;

        var ability = new CountingAbility { InstancingPolicy = GameplayAbilityInstancingPolicy.InstancedPerActor };
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));

        asc.TryActivateAbility(handle);
        asc.TryActivateAbility(handle);

        Assert.Equal(2, CountingAbility.LastInstanceActivationCount); // same instance, state kept
    }

    // ---------------- Passives (doc §4.6.4.1) ----------------

    [Fact]
    public void Passive_Ability_Auto_Activates_On_Grant()
    {
        var world = new TestWorld();
        var asc = world.Target;
        PassiveAbility.Runs = 0;

        asc.GiveAbility(new GameplayAbilitySpec(new PassiveAbility(), 1));
        Assert.Equal(1, PassiveAbility.Runs); // bActivateAbilityOnGranted → activated on grant
    }

    // ---------------- Triggers (doc §4.6.4, §4.6.11) ----------------

    [Fact]
    public void GameplayEvent_Trigger_Activates_With_EventData()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var eventTag = T.Tag("Event.Hit");
        EventAbility.LastMagnitude = 0f;

        var ability = new EventAbility();
        ability.Triggers.Add(new AbilityTriggerData { Tag = eventTag, TriggerSource = AbilityTriggerSource.GameplayEvent });
        asc.GiveAbility(new GameplayAbilitySpec(ability, 1));

        asc.HandleGameplayEvent(eventTag, new GameplayEventData { EventTag = eventTag, EventMagnitude = 42f });
        Assert.Equal(42f, EventAbility.LastMagnitude);
    }

    [Fact]
    public void TagAdded_Trigger_Activates_When_Tag_Is_Gained()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var tag = T.Tag("State.Enraged");

        var ability = new InstantEndAbility();
        ability.Triggers.Add(new AbilityTriggerData { Tag = tag, TriggerSource = AbilityTriggerSource.TagAdded });
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));

        bool activated = false;
        asc.OnAbilityActivated += (_, _) => activated = true;
        asc.AddLooseGameplayTag(tag);

        Assert.True(activated);
        _ = handle;
    }

    // ---------------- Cancel & removal (doc §4.6.5) ----------------

    [Fact]
    public void CancelAbility_Reports_WasCancelled_And_ClearAbility_Removes_It()
    {
        var world = new TestWorld();
        var asc = world.Target;

        var ability = new LongRunningAbility();
        var spec = new GameplayAbilitySpec(ability, 1);
        var handle = asc.GiveAbility(spec);
        asc.TryActivateAbility(handle);

        bool? cancelled = null;
        asc.OnAbilityEnded += (_, wasCancelled) => cancelled = wasCancelled;

        Assert.True(asc.CancelAbility(handle));
        Assert.True(cancelled);
        Assert.False(ability.IsActive);

        asc.ClearAbility(handle); // inactive again → removable
        Assert.DoesNotContain(spec, asc.GrantedAbilities);
    }

    [Fact]
    public void GE_Granted_Ability_Is_Removed_When_The_Granting_GE_Expires()
    {
        var world = new TestWorld();
        var asc = world.Target;

        var def = T.DurationGE(TestAttributeSet.TestBAttr, GameplayModOp.Add, 1f, duration: 1f);
        def.GrantedAbilities.Add(new GrantedAbilityEntry
        {
            AbilityType = typeof(LongRunningAbility),
            RemovalPolicy = GameplayEffectAbilityRemovalPolicy.CancelAbilityImmediately,
        });

        asc.ApplyGameplayEffectToSelf(def);
        var grantedSpec = asc.GrantedAbilities.Single(s => s.Ability is LongRunningAbility);
        Assert.Contains(grantedSpec, asc.GrantedAbilities);

        world.Tick(1.5f); // GE expires → granted ability is removed per policy (doc §4.5.6)
        Assert.DoesNotContain(grantedSpec, asc.GrantedAbilities);
    }

    // ---------------- Test doubles ----------------

    private sealed class InstantEndAbility : GameplayAbility
    {
        protected override void ActivateAbility() => EndAbility(wasCancelled: false);
    }

    /// <summary>Stand-in for abilities that pay their cost/cooldown on activation (doc §4.6.12).</summary>
    private sealed class CommittingAbility : GameplayAbility
    {
        protected override void ActivateAbility()
        {
            if (!CommitAbility())
            {
                EndAbility(wasCancelled: true);
                return;
            }
            EndAbility(wasCancelled: false);
        }
    }

    private sealed class LongRunningAbility : GameplayAbility
    {
        protected override void ActivateAbility() { /* stays active until cancelled */ }
    }

    private sealed class CountingAbility : GameplayAbility
    {
        public static int Executions;
        public static int LastInstanceActivationCount;

        public int ActivationCount;

        protected override void ActivateAbility()
        {
            Executions++;
            LastInstanceActivationCount = ++ActivationCount;
            EndAbility(wasCancelled: false);
        }
    }

    private sealed class PassiveAbility : GameplayAbility
    {
        public static int Runs;

        public PassiveAbility()
        {
            bActivateAbilityOnGranted = true;
        }

        protected override void ActivateAbility()
        {
            Runs++;
            EndAbility(wasCancelled: false);
        }
    }

    private sealed class EventAbility : GameplayAbility
    {
        public static float LastMagnitude;

        protected override void ActivateAbilityFromEvent(GameplayEventData eventData)
        {
            LastMagnitude = eventData.EventMagnitude;
            EndAbility(wasCancelled: false);
        }
    }
}
