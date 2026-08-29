using System.Text.Json;
using GasNet.Data;
using Xunit;

namespace GasNet.Tests;

/// <summary>
/// The writer is the read side's mirror: write → load → write must be an identity, defaults must
/// be omitted, and input ordering must be preserved. These tests keep the JSON format single-sourced.
/// </summary>
public class GasNetDataWriterTests
{
    private static GasNetDataLoadOptions Options() => new GasNetDataLoadOptions()
        .RegisterAttributeSet<TestAttributeSet>()
        .RegisterType<DataTestMMC>()
        .RegisterType<DataTestExecution>()
        .RegisterType<DataTestAbility>();

    private static string RoundTrip(string json, GasNetDataLoadOptions? options = null)
    {
        var catalog = GasNetDataLoader.LoadCatalog(json, options ?? Options());
        var written = GasNetDataWriter.WriteCatalog(catalog.Effects);
        var reloaded = GasNetDataLoader.LoadCatalog(written, options ?? Options());
        Assert.Equal(written, GasNetDataWriter.WriteCatalog(reloaded.Effects));
        return written;
    }

    private static GameplayEffectDefinition CatalogEffect(string json, string name, GasNetDataLoadOptions? options = null) =>
        GasNetDataLoader.LoadCatalog(json, options ?? Options()).Get(name);

    [Fact]
    public void Minimal_Instant_FloatModifier_OmitsAllDefaults()
    {
        var def = new GameplayEffectDefinition();
        def.AddModifier(TestAttributeSet.TestAAttr, GameplayModOp.Add, new ScalableFloatMagnitude(5f));

        var json = GasNetDataWriter.WriteCatalog([new("GE", def)]);

        var root = JsonDocument.Parse(json).RootElement;
        var effect = root.GetProperty("effects").GetProperty("GE");
        Assert.False(effect.TryGetProperty("durationPolicy", out _));   // Instant is the default
        Assert.False(effect.TryGetProperty("period", out _));
        var modifier = effect.GetProperty("modifiers")[0];
        Assert.False(modifier.TryGetProperty("op", out _));             // Add is the default
        Assert.False(modifier.GetProperty("magnitude").TryGetProperty("coefficient", out _));
        Assert.Equal("TestAttributeSet.TestA", modifier.GetProperty("attribute").GetString());
        Assert.Equal(5f, modifier.GetProperty("magnitude").GetProperty("value").GetSingle());
    }

    [Fact]
    public void KitchenSink_RoundTrip_IsStable_AndSemantic()
    {
        const string json = """
        {
          "effects": {
            "GE_Full": {
              "durationPolicy": "HasDuration", "duration": 5.5, "period": 1.5,
              "modifiers": [
                { "attribute": "TestAttributeSet.Health", "op": "Multiply",
                  "magnitude": { "type": "scalableFloat", "value": -14, "valuePerLevel": 2,
                                 "coefficient": 2, "preMultiplyAdditive": 1, "postMultiplyAdditive": 3 },
                  "sourceTags": ["Source.A"], "targetTags": ["Target.A"] },
                { "attribute": "TestAttributeSet.TestB", "op": "Override",
                  "magnitude": { "type": "attributeBased", "attribute": "TestAttributeSet.TestA",
                                 "source": "Source", "snapshot": true, "useBaseValue": true,
                                 "sourceTagFilter": ["F.Src"], "targetTagFilter": ["F.Tgt"] },
                  "targetTags": ["Target.B"] },
                { "attribute": "TestAttributeSet.TestC", "op": "Add",
                  "magnitude": { "type": "setByCaller", "tag": "Data.Test" } },
                { "attribute": "TestAttributeSet.Mana", "op": "Add",
                  "magnitude": { "type": "customCalculation", "calculation": "DataTestMMC" } }
              ],
              "assetTags": ["Effect.A", "Effect.B"],
              "grantedTags": ["State.X"],
              "cueTags": ["GameplayCue.T"],
              "applicationTagRequirements": { "require": ["App.R"], "ignore": ["App.I"] },
              "targetTagRequirements": { "ignore": ["Tgt.I"] },
              "ongoingTagRequirements": { "require": ["Ong.R"] },
              "removeGameplayEffectsWithTags": ["State.Old"],
              "grantedApplicationImmunityTags": ["Effect.Bad"],
              "stacking": { "type": "AggregateBySource", "limit": 3,
                            "durationRefresh": "RefreshOnSuccessfulApplication",
                            "periodReset": "ResetOnSuccessfulApplication",
                            "expiry": "RefreshDuration" },
              "grantedAbilities": [ { "ability": "DataTestAbility", "level": 2, "inputId": 7,
                                      "removalPolicy": "RemoveAbilityOnEnd" } ],
              "executions": [ { "calculation": "DataTestExecution" } ],
              "customApplicationRequirements": [ { "requirement": "DenyCAR" } ]
            }
          }
        }
        """;

        var options = Options().RegisterType(typeof(DenyCAR));
        var written = RoundTrip(json, options);
        var def = CatalogEffect(written, "GE_Full", options);

        Assert.Equal(GameplayEffectDurationType.HasDuration, def.DurationPolicy);
        Assert.Equal(5.5f, def.Duration);
        Assert.Equal(1.5f, def.Period);
        Assert.Equal(4, def.Modifiers.Count);
        Assert.IsType<ScalableFloatMagnitude>(def.Modifiers[0].Magnitude);
        Assert.Equal(2f, ((ScalableFloatMagnitude)def.Modifiers[0].Magnitude).ValuePerLevel);
        Assert.Equal(3f, def.Modifiers[0].Magnitude.PostMultiplyAdditive);
        var based = Assert.IsType<AttributeBasedMagnitude>(def.Modifiers[1].Magnitude);
        Assert.Equal(GameplayAttributeCaptureSource.Source, based.Capture.CaptureSource);
        Assert.True(based.Capture.Snapshot);
        Assert.IsType<SetByCallerMagnitude>(def.Modifiers[2].Magnitude);
        Assert.IsType<CustomCalculationMagnitude>(def.Modifiers[3].Magnitude);
        Assert.Equal(typeof(DataTestMMC), ((CustomCalculationMagnitude)def.Modifiers[3].Magnitude).Calculation.GetType());
        Assert.Equal("State.X", def.GrantedTags.Tags.Single().Name);
        Assert.Equal("App.R", def.ApplicationTagRequirements.RequiredTags.Tags.Single().Name);
        Assert.Equal("App.I", def.ApplicationTagRequirements.IgnoredTags.Tags.Single().Name);
        Assert.Equal("Tgt.I", def.TargetTagRequirements.IgnoredTags.Tags.Single().Name);
        Assert.True(def.TargetTagRequirements.RequiredTags.IsEmpty);
        Assert.Equal(GameplayEffectStackingType.AggregateBySource, def.StackingType);
        Assert.Equal(3, def.StackLimitCount);
        Assert.Equal(GameplayEffectStackingExpiryPolicy.RefreshDuration, def.StackExpiryPolicy);
        var ability = Assert.Single(def.GrantedAbilities);
        Assert.Equal(typeof(DataTestAbility), ability.AbilityType);
        Assert.Equal(7, ability.InputID);
        Assert.Equal(GameplayEffectAbilityRemovalPolicy.RemoveAbilityOnEnd, ability.RemovalPolicy);
        Assert.IsType<DataTestExecution>(Assert.Single(def.Executions));
        Assert.IsType<DenyCAR>(Assert.Single(def.CustomApplicationRequirements));
    }

    [Fact]
    public void DoNotStack_AndDefaultPolicies_AreOmitted()
    {
        var def = new GameplayEffectDefinition();
        def.AddModifier(TestAttributeSet.TestAAttr, GameplayModOp.Add, new ScalableFloatMagnitude(1f));

        var json = GasNetDataWriter.WriteCatalog([new("GE", def)]);
        Assert.False(JsonDocument.Parse(json).RootElement
            .GetProperty("effects").GetProperty("GE").TryGetProperty("stacking", out _));
    }

    [Fact]
    public void EffectOrder_IsPreserved()
    {
        var first = new GameplayEffectDefinition();
        var second = new GameplayEffectDefinition();
        first.AddModifier(TestAttributeSet.TestAAttr, GameplayModOp.Add, new ScalableFloatMagnitude(1f));
        second.AddModifier(TestAttributeSet.TestAAttr, GameplayModOp.Add, new ScalableFloatMagnitude(2f));

        var json = GasNetDataWriter.WriteCatalog([new("Z_First", first), new("A_Second", second)]);
        var names = JsonDocument.Parse(json).RootElement
            .GetProperty("effects").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["Z_First", "A_Second"], names);
    }

    [Fact]
    public void GodotDemoCatalog_RoundTrips_Stable()
    {
        // The real-world consumer: the demo's hand-written catalog must survive write→load→write.
        const string json = """
        {
          "effects": {
            "GE_InitStats_Hero": {
              "durationPolicy": "Instant",
              "modifiers": [
                { "attribute": "TestAttributeSet.MaxHealth", "op": "Override", "magnitude": { "type": "scalableFloat", "value": 100 } },
                { "attribute": "TestAttributeSet.Health", "op": "Override", "magnitude": { "type": "scalableFloat", "value": 100 } }
              ]
            },
            "GE_Damage": {
              "cueTags": ["GameplayCue.Combat.Hit"],
              "modifiers": [
                { "attribute": "TestAttributeSet.Health", "op": "Add", "magnitude": { "type": "setByCaller", "tag": "Data.Damage" } }
              ]
            }
          }
        }
        """;
        RoundTrip(json);
    }

    [Fact]
    public void NullEffect_IsRejected()
    {
        var pairs = new List<KeyValuePair<string, GameplayEffectDefinition>> { new("GE", null!) };
        Assert.Throws<GasNetDataException>(() => GasNetDataWriter.WriteCatalog(pairs));
    }
}

/// <summary>CAR that always denies — registered under its own name in tests.</summary>
public sealed class DenyCAR : GameplayEffectCustomApplicationRequirement
{
    public override bool CanGameplayEffectApply(ActiveGameplayEffectsContainer container, GameplayEffectSpec spec) => false;
}
