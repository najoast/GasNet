using Xunit;

namespace GasNet.Tests;

public class AttributeTests
{
    private static GameplayEffectDefinition InitGE() => new GameplayEffectDefinition()
        .With(policy: GameplayEffectDurationType.Instant)
        .AddModifier(TestAttributeSet.MaxHealthAttr, GameplayModOp.Override, new ScalableFloatMagnitude(100))
        .AddModifier(TestAttributeSet.HealthAttr, GameplayModOp.Override, new ScalableFloatMagnitude(100))
        .AddModifier(TestAttributeSet.MoveSpeedAttr, GameplayModOp.Override, new ScalableFloatMagnitude(500))
        .AddModifier(TestAttributeSet.ManaAttr, GameplayModOp.Override, new ScalableFloatMagnitude(50))
        .AddModifier(TestAttributeSet.MaxManaAttr, GameplayModOp.Override, new ScalableFloatMagnitude(50))
        .AddModifier(TestAttributeSet.TestAAttr, GameplayModOp.Override, new ScalableFloatMagnitude(10))
        .AddModifier(TestAttributeSet.TestBAttr, GameplayModOp.Override, new ScalableFloatMagnitude(5))
        .AddModifier(TestAttributeSet.TestCAttr, GameplayModOp.Override, new ScalableFloatMagnitude(2));

    private static TestWorld NewWorld()
    {
        var world = new TestWorld();
        world.Target.ApplyGameplayEffectToSelf(InitGE());
        return world;
    }

    [Fact]
    public void Instant_GE_Changes_BaseValue_Permanently()
    {
        var world = NewWorld();
        world.Target.ApplyGameplayEffectToSelf(T.InstantGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, -25f));

        var set = world.Target.GetSet<TestAttributeSet>()!;
        Assert.Equal(75f, set.Health.BaseValue);
        Assert.Equal(75f, set.Health.CurrentValue);
    }

    [Fact]
    public void Duration_GE_Changes_Only_CurrentValue_And_Reverts_On_Expiry()
    {
        // Doc §4.3.2: 600 base + 50 buff → 650, back to 600 on expiry.
        var world = NewWorld();
        world.Target.ApplyGameplayEffectToSelf(T.DurationGE(TestAttributeSet.TestBAttr, GameplayModOp.Add, 50f, duration: 5f));
        Assert.Equal(55f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr));

        world.Tick(5.5f);
        Assert.Equal(5f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr));

        var set = world.Target.GetSet<TestAttributeSet>()!;
        Assert.Equal(5f, set.TestB.BaseValue); // untouched by the duration GE
    }

    [Fact]
    public void Infinite_GE_Applies_Until_Manually_Removed()
    {
        var world = NewWorld();
        var def = new GameplayEffectDefinition();
        def.DurationPolicy = GameplayEffectDurationType.Infinite;
        def.AddModifier(TestAttributeSet.TestBAttr, GameplayModOp.Add, new ScalableFloatMagnitude(30f));

        var handle = world.Target.ApplyGameplayEffectToSelf(def)!.Value;
        world.Tick(60f); // infinite GEs never expire on their own
        Assert.Equal(35f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr));

        world.Target.RemoveActiveGameplayEffect(handle);
        Assert.Equal(5f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr));
    }

    [Fact]
    public void PreAttributeChange_Clamps_PerQuery_Without_Touching_Mods()
    {
        var world = NewWorld();
        world.Target.ApplyGameplayEffectToSelf(T.DurationGE(TestAttributeSet.MoveSpeedAttr, GameplayModOp.Multiply, 10f, duration: 5f));

        // 500 * 10 = 5000 raw, clamped to 1000 by the fixture's PreAttributeChange.
        Assert.Equal(1000f, world.Target.GetNumericAttribute(TestAttributeSet.MoveSpeedAttr));

        // Captures bypass PreAttributeChange (doc §4.5.11) and must re-clamp themselves.
        float captured = world.Target.EvaluateAttributeAggregated(TestAttributeSet.MoveSpeedAttr);
        Assert.Equal(5000f, captured, 3);
    }

    [Fact]
    public void SetNumericAttributeBase_Keeps_Active_Modifiers()
    {
        // Doc §9.4: existing modifiers are NOT cleared and act on the new base.
        var world = NewWorld();
        world.Target.ApplyGameplayEffectToSelf(T.DurationGE(TestAttributeSet.TestBAttr, GameplayModOp.Multiply, 2f, duration: 10f));
        Assert.Equal(10f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr));

        world.Target.SetNumericAttributeBase(TestAttributeSet.TestBAttr, 30f);
        Assert.Equal(60f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr));
    }

    [Fact]
    public void Instant_Execution_Applies_Ops_In_Modifier_Order()
    {
        // Instant GEs apply each op directly to BaseValue sequentially (engine ExecuteMod).
        var world = NewWorld();
        var def = new GameplayEffectDefinition()
            .With(policy: GameplayEffectDurationType.Instant)
            .AddModifier(TestAttributeSet.TestBAttr, GameplayModOp.Add, new ScalableFloatMagnitude(50f))
            .AddModifier(TestAttributeSet.TestBAttr, GameplayModOp.Divide, new ScalableFloatMagnitude(3f));
        world.Target.ApplyGameplayEffectToSelf(def);

        // (5 + 50) / 3 = 18.333.
        Assert.Equal(18.333f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr), 3);
    }

    [Fact]
    public void Attribute_Change_Delegate_Reports_Old_And_New()
    {
        var world = NewWorld();
        (float old, float new_) changes = default;
        world.Target.GetGameplayAttributeValueChangeDelegate(TestAttributeSet.HealthAttr).Handler += d => changes = (d.OldValue, d.NewValue);

        world.Target.ApplyGameplayEffectToSelf(T.InstantGE(TestAttributeSet.HealthAttr, GameplayModOp.Add, -10f));
        Assert.Equal((100f, 90f), changes);
    }

    [Fact]
    public void Meta_Attribute_Flows_Through_PostGameplayEffectExecute()
    {
        var world = NewWorld();
        world.Target.ApplyGameplayEffectToSelf(T.InstantGE(TestAttributeSet.MetaDamageAttr, GameplayModOp.Add, 40f));

        var set = world.Target.GetSet<TestAttributeSet>()!;
        Assert.Equal(60f, set.Health.CurrentValue); // redistributed in PostGameplayEffectExecute
        Assert.Equal(0f, set.MetaDamage.BaseValue); // meta attributes do not persist (doc §4.3.3)
    }

    [Fact]
    public void Derived_Attribute_Recalculates_When_Dependency_Changes()
    {
        // Doc §4.3.5: an Infinite GE with attribute-based modifiers auto-recalculates.
        var world = NewWorld();
        var def = new GameplayEffectDefinition();
        def.DurationPolicy = GameplayEffectDurationType.Infinite;
        def.AddModifier(TestAttributeSet.TestAAttr, GameplayModOp.Add, new AttributeBasedMagnitude
        {
            Capture = new AttributeCaptureDefinition(TestAttributeSet.TestBAttr, GameplayAttributeCaptureSource.Target, Snapshot: false),
        });
        world.Target.ApplyGameplayEffectToSelf(def);
        Assert.Equal(15f, world.Target.GetNumericAttribute(TestAttributeSet.TestAAttr)); // 10 + 5

        world.Target.SetNumericAttributeBase(TestAttributeSet.TestBAttr, 20f);
        Assert.Equal(30f, world.Target.GetNumericAttribute(TestAttributeSet.TestAAttr)); // 10 + 20 (auto-recalc)
    }

    [Fact]
    public void AttributeBased_Magnitude_Can_Use_BaseValue_And_Coefficients()
    {
        var world = NewWorld();
        var def = new GameplayEffectDefinition();
        def.DurationPolicy = GameplayEffectDurationType.Instant;
        def.AddModifier(TestAttributeSet.TestAAttr, GameplayModOp.Add, new AttributeBasedMagnitude
        {
            Capture = new AttributeCaptureDefinition(TestAttributeSet.TestBAttr, GameplayAttributeCaptureSource.Target, Snapshot: false),
            UseBaseValue = true,
            Coefficient = 2f,
            PostMultiplyAdditive = 3f,
        });
        world.Target.ApplyGameplayEffectToSelf(def);

        // (5 * 2) * 3 = 30 → 10 + 30 = 40.
        Assert.Equal(40f, world.Target.GetNumericAttribute(TestAttributeSet.TestAAttr), 3);
    }

    [Fact]
    public void MMC_Can_Read_SetByCaller_And_Spec_Tags()
    {
        var world = NewWorld();
        var fireTag = T.Tag("Element.Fire");
        var mmc = new TestMMC(fireTag);
        var def = new GameplayEffectDefinition();
        def.DurationPolicy = GameplayEffectDurationType.Instant;
        def.AddModifier(TestAttributeSet.TestAAttr, GameplayModOp.Add, new CustomCalculationMagnitude(mmc));
        def.AssetTags.AddTag(fireTag);

        var spec = world.Target.MakeOutgoingSpec(def);
        spec.SetSetByCallerMagnitude(T.Tag("Data.Bonus"), 7f);
        world.Target.ApplyGameplayEffectSpecToSelf(spec);

        // MMC returns SetByCaller 7 + (fire tag present ? 5 : 0) = 12 → 10 + 12 = 22.
        Assert.Equal(22f, world.Target.GetNumericAttribute(TestAttributeSet.TestAAttr), 3);
        Assert.True(mmc.WasCalled);
    }

    private sealed class TestMMC(GameplayTag fireTag) : ModifierMagnitudeCalculation
    {
        public bool WasCalled { get; private set; }

        public override float CalculateBaseMagnitude(GameplayEffectSpec spec)
        {
            WasCalled = true;
            float bonus = GetSetByCallerMagnitude(spec, T.Tag("Data.Bonus"));
            float elementBonus = spec.GetAllAssetTags().HasTag(fireTag) ? 5f : 0f;
            return bonus + elementBonus;
        }
    }
}
