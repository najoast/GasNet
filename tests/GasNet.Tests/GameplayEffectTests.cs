using Xunit;

namespace GasNet.Tests;

public class GameplayEffectTests
{
    private static TestWorld NewWorld()
    {
        var world = new TestWorld();
        world.Target.ApplyGameplayEffectToSelf(new GameplayEffectDefinition()
            .With(policy: GameplayEffectDurationType.Instant)
            .AddModifier(TestAttributeSet.MaxHealthAttr, GameplayModOp.Override, new ScalableFloatMagnitude(100))
            .AddModifier(TestAttributeSet.HealthAttr, GameplayModOp.Override, new ScalableFloatMagnitude(100))
            .AddModifier(TestAttributeSet.ManaAttr, GameplayModOp.Override, new ScalableFloatMagnitude(50))
            .AddModifier(TestAttributeSet.MaxManaAttr, GameplayModOp.Override, new ScalableFloatMagnitude(50))
            .AddModifier(TestAttributeSet.MoveSpeedAttr, GameplayModOp.Override, new ScalableFloatMagnitude(500)));
        return world;
    }

    // ---------------- Apply/remove lifecycle (doc §4.5.2, §4.5.3) ----------------

    [Fact]
    public void Instant_GE_Does_Not_Enter_The_Active_List()
    {
        var world = NewWorld();
        world.Target.ApplyGameplayEffectToSelf(T.InstantGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, -5f));
        Assert.Empty(world.Target.ActiveGameplayEffects.All);
        Assert.Equal(95f, world.Target.GetNumericAttribute(TestAttributeSet.HealthAttr));
    }

    [Fact]
    public void Applied_And_Removed_Delegates_Fire()
    {
        var world = NewWorld();
        GameplayEffectSpec? appliedSpec = null;
        ActiveGameplayEffect? removed = null;
        world.Target.OnGameplayEffectAppliedToSelf += (_, spec, _) => appliedSpec = spec;
        world.Target.OnAnyGameplayEffectRemoved += (age, _) => removed = age;

        var handle = world.Target.ApplyGameplayEffectToSelf(T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 1f))!.Value;
        Assert.NotNull(appliedSpec);
        world.Target.RemoveActiveGameplayEffect(handle);
        Assert.NotNull(removed);
        Assert.Equal(100f, world.Target.GetNumericAttribute(TestAttributeSet.HealthAttr));
    }

    // ---------------- Granted tags (doc §4.5.7) ----------------

    [Fact]
    public void GrantedTags_Applied_While_Active_And_Removed_With_The_GE()
    {
        var world = NewWorld();
        var stunTag = T.Tag("State.Debuff.Stun");
        var def = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 2f);
        def.GrantedTags.AddTag(stunTag);

        var handle = world.Target.ApplyGameplayEffectToSelf(def)!.Value;
        Assert.True(world.Target.HasMatchingGameplayTag(stunTag));
        Assert.True(world.Target.HasMatchingGameplayTag(T.Tag("State"))); // hierarchical

        world.Target.RemoveActiveGameplayEffect(handle);
        Assert.False(world.Target.HasMatchingGameplayTag(stunTag));
    }

    [Fact]
    public void DynamicGrantedTags_And_DynamicAssetTags_Join_The_Def_Tags()
    {
        var world = NewWorld();
        var def = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 2f);
        def.GrantedTags.AddTag(T.Tag("A"));
        def.AssetTags.AddTag(T.Tag("Asset.1"));

        var spec = world.Target.MakeOutgoingSpec(def);
        spec.DynamicGrantedTags.AddTag(T.Tag("B"));
        spec.DynamicAssetTags.AddTag(T.Tag("Asset.2"));
        world.Target.ApplyGameplayEffectSpecToSelf(spec);

        Assert.True(world.Target.HasMatchingGameplayTag(T.Tag("A")));
        Assert.True(world.Target.HasMatchingGameplayTag(T.Tag("B")));

        var age = world.Target.GetActiveEffects(GameplayEffectQuery.MatchAssetTags(new GameplayTagContainer(T.Tag("Asset.2"))));
        Assert.Single(age); // dynamic asset tags are queryable
    }

    // ---------------- Application / target requirements & immunity (doc §4.5.7, §4.5.8) ----------------

    [Fact]
    public void ApplicationTagRequirements_Block_Without_Immunity_Delegate()
    {
        var world = NewWorld();
        var def = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 2f);
        def.ApplicationTagRequirements.RequiredTags.AddTag(T.Tag("State.Buffed"));

        bool immunityFired = false;
        world.Target.OnImmunityBlockGameplayEffect += (_, _, _) => immunityFired = true;

        Assert.Null(world.Target.ApplyGameplayEffectToSelf(def));
        Assert.False(immunityFired);
    }

    [Fact]
    public void TargetTagRequirements_Fail_As_Immunity_And_Fire_The_Delegate()
    {
        var world = NewWorld();
        var def = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 2f);
        def.TargetTagRequirements.IgnoredTags.AddTag(T.Tag("State.Protected"));

        GameplayEffectSpec? blockedSpec = null;
        world.Target.OnImmunityBlockGameplayEffect += (_, spec, _) => blockedSpec = spec;
        world.Target.AddLooseGameplayTag(T.Tag("State.Protected"));

        Assert.Null(world.Target.ApplyGameplayEffectToSelf(def));
        Assert.NotNull(blockedSpec);
    }

    [Fact]
    public void GrantedApplicationImmunityTags_Block_GEs_From_Tagged_Sources()
    {
        var world = NewWorld();
        var def = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 2f);
        def.GrantedApplicationImmunityTags.AddTag(T.Tag("Faction.Undead"));

        // Source carries the tag → blocked (source tags are captured at spec creation, doc §4.5.4.2).
        world.Source.AddLooseGameplayTag(T.Tag("Faction.Undead"));
        bool immunityFired = false;
        world.Target.OnImmunityBlockGameplayEffect += (_, _, _) => immunityFired = true;
        Assert.Null(world.Target.ApplyGameplayEffectSpecToTarget(world.Source.MakeOutgoingSpec(def), world.Target));
        Assert.True(immunityFired);

        // Source without the tag → applied.
        world.Source.RemoveLooseGameplayTag(T.Tag("Faction.Undead"));
        Assert.NotNull(world.Target.ApplyGameplayEffectSpecToTarget(world.Source.MakeOutgoingSpec(def), world.Target));
    }

    [Fact]
    public void CustomApplicationRequirement_Can_Deny_Application()
    {
        var world = NewWorld();
        var def = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 2f);
        def.CustomApplicationRequirements.Add(new AlwaysDeny());

        Assert.Null(world.Target.ApplyGameplayEffectToSelf(def));
    }

    private sealed class AlwaysDeny : GameplayEffectCustomApplicationRequirement
    {
        public override bool CanGameplayEffectApply(ActiveGameplayEffectsContainer container, GameplayEffectSpec spec) => false;
    }

    // ---------------- Ongoing tag requirements (doc §4.5.7) ----------------

    [Fact]
    public void OngoingTagRequirements_Toggle_The_GE_Off_And_Back_On()
    {
        var world = NewWorld();
        var powered = T.Tag("State.Powered");
        var def = T.DurationGE(TestAttributeSet.TestBAttr, GameplayModOp.Add, 40f, duration: 100f);
        def.OngoingTagRequirements.RequiredTags.AddTag(powered);

        world.Target.ApplyGameplayEffectToSelf(def);
        // Requirement unmet: applied but OFF — modifiers removed, GE stays.
        Assert.Equal(5f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr));
        Assert.Single(world.Target.ActiveGameplayEffects.All);
        Assert.True(world.Target.ActiveGameplayEffects.All[0].IsInactive);

        world.Target.AddLooseGameplayTag(powered); // requirement met → back ON
        Assert.Equal(45f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr));
        Assert.False(world.Target.ActiveGameplayEffects.All[0].IsInactive);

        world.Target.RemoveLooseGameplayTag(powered); // → OFF again
        Assert.Equal(5f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr));
    }

    // ---------------- RemoveGameplayEffectsWithTags (doc §4.5.7) ----------------

    [Fact]
    public void RemoveGameplayEffectsWithTags_Removes_Matching_GEs()
    {
        var world = NewWorld();
        var fire = T.Tag("Effect.Fire");

        var oldGE = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 100f);
        oldGE.AssetTags.AddTag(fire);
        world.Target.ApplyGameplayEffectToSelf(oldGE);

        var purgeGE = T.DurationGE(TestAttributeSet.ManaAttr, GameplayModOp.Add, 1f, 100f);
        purgeGE.RemoveGameplayEffectsWithTags.AddTag(fire);
        world.Target.ApplyGameplayEffectToSelf(purgeGE);

        Assert.Equal(0, world.Target.ActiveGameplayEffects.All.Count(age => age.Spec.GetAllAssetTags().HasTag(fire)));
        Assert.Single(world.Target.ActiveGameplayEffects.All); // the purge GE itself remains
    }

    [Fact]
    public void Instant_GE_Also_Processes_RemoveGameplayEffectsWithTags()
    {
        // Engine behavior: the removal pass runs on EVERY successful application, including
        // Instant ones (the "dispel potion" pattern). Regression: it used to be skipped for Instant.
        var world = NewWorld();
        var fire = T.Tag("Effect.Fire");

        var oldGE = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 100f);
        oldGE.AssetTags.AddTag(fire);
        world.Target.ApplyGameplayEffectToSelf(oldGE);

        var instantPurge = T.InstantGE(TestAttributeSet.ManaAttr, GameplayModOp.Add, 1f);
        instantPurge.RemoveGameplayEffectsWithTags.AddTag(fire);
        world.Target.ApplyGameplayEffectToSelf(instantPurge);

        Assert.Equal(0, world.Target.ActiveGameplayEffects.All.Count(age => age.Spec.GetAllAssetTags().HasTag(fire)));
        Assert.Empty(world.Target.ActiveGameplayEffects.All); // instant GEs never enter the active list
    }

    // ---------------- Stacking (doc §4.5.5) ----------------

    [Fact]
    public void Stacking_Increments_Count_And_Never_Exceeds_The_Limit()
    {
        var world = NewWorld();
        var def = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 10f);
        def.WithStacking(GameplayEffectStackingType.AggregateByTarget, stackLimit: 3);

        world.Target.ApplyGameplayEffectToSelf(def);
        world.Target.ApplyGameplayEffectToSelf(def);
        world.Target.ApplyGameplayEffectToSelf(def);
        world.Target.ApplyGameplayEffectToSelf(def); // beyond the limit: refused

        Assert.Single(world.Target.ActiveGameplayEffects.All); // one spec, not four
        Assert.Equal(3, world.Target.ActiveGameplayEffects.All[0].StackCount);
    }

    [Fact]
    public void StackDurationRefreshPolicy_Resets_Remaining_Duration()
    {
        var world = NewWorld();
        var def = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 5f);
        def.WithStacking(GameplayEffectStackingType.AggregateByTarget, stackLimit: 2);
        def.StackDurationRefreshPolicy = GameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication;

        world.Target.ApplyGameplayEffectToSelf(def);
        world.Tick(4f);
        float remainingBefore = world.Target.GetActiveEffectsTimeRemaining(GameplayEffectQuery.MatchDef(def));
        Assert.Equal(1f, remainingBefore, 2);

        world.Target.ApplyGameplayEffectToSelf(def); // refresh
        float remainingAfter = world.Target.GetActiveEffectsTimeRemaining(GameplayEffectQuery.MatchDef(def));
        Assert.Equal(5f, remainingAfter, 2);
    }

    [Fact]
    public void StackExpiryPolicy_ClearSingleStackCount_Consumes_One_Stack_Per_Expiry()
    {
        var world = NewWorld();
        var def = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 2f);
        def.WithStacking(GameplayEffectStackingType.AggregateByTarget, stackLimit: 3);
        def.StackExpiryPolicy = GameplayEffectStackingExpiryPolicy.ClearSingleStackCount;

        world.Target.ApplyGameplayEffectToSelf(def);
        world.Target.ApplyGameplayEffectToSelf(def);
        Assert.Equal(2, world.Target.ActiveGameplayEffects.All[0].StackCount);

        world.Tick(2.5f); // first expiry: one stack consumed, GE stays alive
        var age = world.Target.ActiveGameplayEffects.All.FirstOrDefault();
        Assert.NotNull(age);
        Assert.Equal(1, age!.StackCount);

        world.Tick(2.5f); // second expiry: last stack consumed → removed
        Assert.Empty(world.Target.ActiveGameplayEffects.All);
    }

    [Fact]
    public void DecrementStack_Removes_The_GE_When_The_Last_Stack_Is_Consumed()
    {
        var world = NewWorld();
        var def = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 100f);
        def.WithStacking(GameplayEffectStackingType.AggregateByTarget, stackLimit: 3);
        world.Target.ApplyGameplayEffectToSelf(def);
        world.Target.ApplyGameplayEffectToSelf(def);

        var handle = world.Target.ActiveGameplayEffects.All[0].Handle;
        Assert.True(world.Target.ActiveGameplayEffects.DecrementStack(handle));
        Assert.Equal(1, world.Target.ActiveGameplayEffects.All[0].StackCount);
        Assert.True(world.Target.ActiveGameplayEffects.DecrementStack(handle));
        Assert.Empty(world.Target.ActiveGameplayEffects.All);
    }

    // ---------------- Periodic effects (doc §4.5.1, §4.5.12) ----------------

    [Fact]
    public void Periodic_GE_Executes_Each_Period_As_An_Instant_GE()
    {
        var world = NewWorld();
        int executedCues = 0;
        world.Target.OnGameplayCueExecuted += (_, _) => executedCues++;

        var def = new GameplayEffectDefinition()
            .With(policy: GameplayEffectDurationType.HasDuration, duration: 3f, period: 1f)
            .AddModifier(TestAttributeSet.TestBAttr, GameplayModOp.Add, new ScalableFloatMagnitude(2f));
        def.GameplayCueTags.AddTag(T.Tag("GameplayCue.Test.Period"));

        world.Target.ApplyGameplayEffectToSelf(def);
        world.Tick(3.5f);

        // Frame-stepped ticking: ticks fire at t=1 and t=2; the tick due exactly at expiry (t=3)
        // is consumed by the expiry check, matching engine CheckDuration order.
        // Periodic ticks change BaseValue (treated like Instant GEs): 5 + 2*2 = 9.
        Assert.Equal(9f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr), 3);
        Assert.Equal(2, executedCues);
    }

    [Fact]
    public void Infinite_Periodic_Keeps_Ticking_Until_Removed()
    {
        var world = NewWorld();
        var def = new GameplayEffectDefinition()
            .With(policy: GameplayEffectDurationType.Infinite, period: 1f)
            .AddModifier(TestAttributeSet.TestBAttr, GameplayModOp.Add, new ScalableFloatMagnitude(2f));

        var handle = world.Target.ApplyGameplayEffectToSelf(def)!.Value;
        world.Tick(4.5f);
        Assert.Equal(4, world.Target.ActiveGameplayEffects.All[0].ExecutedPeriodCount);
        // BaseValue gets +2 per tick (periodic = instant-style): 5 + 2*4 = 13.
        Assert.Equal(13f, world.Target.GetNumericAttributeBase(TestAttributeSet.TestBAttr), 3);
        // CurrentValue additionally carries the live GE's own +2 aggregator mod.
        Assert.Equal(15f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr), 3);

        world.Target.RemoveActiveGameplayEffect(handle);
        world.Tick(5f);
        Assert.Equal(13f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr), 3); // mod removed → current == base, no more ticks
    }

    // ---------------- SetByCaller (doc §4.5.9.1) ----------------

    [Fact]
    public void SetByCaller_Delivers_Runtime_Magnitudes_And_Missing_Tags_Read_Zero()
    {
        var world = NewWorld();
        var def = T.InstantGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 0f);
        def.Modifiers.Clear();
        def.AddModifier(TestAttributeSet.HealthAttr, GameplayModOp.Add, new SetByCallerMagnitude(T.Tag("Data.Damage")));

        var spec = world.Target.MakeOutgoingSpec(def);
        spec.SetSetByCallerMagnitude(T.Tag("Data.Damage"), -20f);
        world.Target.ApplyGameplayEffectSpecToSelf(spec);
        Assert.Equal(80f, world.Target.GetNumericAttribute(TestAttributeSet.HealthAttr));

        // Missing pair: runtime error + 0 (doc §4.5.9.1 — dangerous for Divide!).
        var spec2 = world.Target.MakeOutgoingSpec(def);
        world.Target.ApplyGameplayEffectSpecToSelf(spec2);
        Assert.Equal(80f, world.Target.GetNumericAttribute(TestAttributeSet.HealthAttr));
    }

    // ---------------- Queries & time remaining (doc §4.5.15.1) ----------------

    [Fact]
    public void Queries_Find_Effects_By_Tags_And_Report_Time_Remaining()
    {
        var world = NewWorld();
        var def = T.DurationGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, 1f, 10f);
        def.GrantedTags.AddTag(T.Tag("Cooldown.Test"));
        world.Target.ApplyGameplayEffectToSelf(def);

        var query = GameplayEffectQuery.MatchAnyOwningTags(new GameplayTagContainer(T.Tag("Cooldown.Test")));
        Assert.Equal(1, world.Target.GetActiveGameplayEffectCount(query));

        world.Tick(3f);
        var (remaining, duration) = world.Target.GetActiveEffectsTimeRemainingAndDuration(query).First();
        Assert.Equal(7f, remaining, 2);
        Assert.Equal(10f, duration, 2);
    }

    // ---------------- Execution calculations (doc §4.5.12) ----------------

    [Fact]
    public void ExecutionCalculation_Can_Modify_Attributes_Via_Output()
    {
        var world = NewWorld();
        var def = new GameplayEffectDefinition()
            .With(policy: GameplayEffectDurationType.Instant)
            .WithExecutions(new DamageExec());
        def.AddModifier(TestAttributeSet.MetaDamageAttr, GameplayModOp.Add, new ScalableFloatMagnitude(0f));

        var spec = world.Target.MakeOutgoingSpec(def);
        spec.SetSetByCallerMagnitude(T.Tag("Data.Damage"), 30f);
        world.Target.ApplyGameplayEffectSpecToSelf(spec);

        // DamageExec: 30 raw - 0 armor captured (none registered here) = 30 → meta → health 100-30 = 70.
        Assert.Equal(70f, world.Target.GetNumericAttribute(TestAttributeSet.HealthAttr));
    }

    private sealed class DamageExec : GameplayEffectExecutionCalculation
    {
        public override void Execute(GameplayEffectExecutionParams p)
        {
            float damage = p.GetSetByCallerMagnitude(T.Tag("Data.Damage"));
            p.Output.AddOutputModifier(TestAttributeSet.MetaDamageAttr, GameplayModOp.Add, damage);
        }
    }
}
