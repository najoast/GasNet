using GasNet.Data;
using Xunit;

namespace GasNet.Tests;

/// <summary>
/// Behavior-as-documentation for the JSON data layer: what the catalog format loads into,
/// and that load-time errors are loud and point at the offending field.
/// </summary>
public class GasNetDataTests
{
    private static GasNetDataLoadOptions Options() => new GasNetDataLoadOptions()
        .RegisterAttributeSet<TestAttributeSet>()
        .RegisterType<DataTestMMC>()
        .RegisterType<DataTestExecution>()
        .RegisterType<DataTestAbility>();

    // ------------------------------------------------------------------
    // End-to-end application (loaded defs behave like code-authored ones)
    // ------------------------------------------------------------------

    [Fact]
    public void Instant_SetByCaller_DamageRoundTrip()
    {
        var catalog = GasNetDataLoader.LoadCatalog("""
        {
          "effects": {
            "GE_Damage": {
              "durationPolicy": "Instant",
              "modifiers": [ { "attribute": "TestAttributeSet.Health", "op": "Add",
                               "magnitude": { "type": "setByCaller", "tag": "Data.Damage" } } ]
            }
          }
        }
        """, Options());

        var world = new TestWorld();
        var spec = world.Source.MakeOutgoingSpec(catalog.Get("GE_Damage"));
        spec.SetSetByCallerMagnitude(T.Tag("Data.Damage"), -14f);
        world.Source.ApplyGameplayEffectSpecToTarget(spec, world.Target);

        Assert.Equal(86f, world.Target.GetNumericAttributeBase(TestAttributeSet.HealthAttr));
        Assert.Equal(86f, world.Target.GetNumericAttribute(TestAttributeSet.HealthAttr));
    }

    [Fact]
    public void ScalableFloat_CoefficientAndAdditives_UEFinalFormula()
    {
        // final = ((base * coefficient) + preMultiplyAdditive) * postMultiplyAdditive = ((10*2)+1)*3 = 63
        var catalog = GasNetDataLoader.LoadCatalog("""
        {
          "effects": {
            "GE_Buff": {
              "modifiers": [ { "attribute": "TestAttributeSet.TestA", "op": "Override",
                               "magnitude": { "type": "scalableFloat", "value": 10,
                                              "coefficient": 2, "preMultiplyAdditive": 1,
                                              "postMultiplyAdditive": 3 } } ]
            }
          }
        }
        """, Options());

        var world = new TestWorld();
        world.Source.ApplyGameplayEffectToTarget(catalog.Get("GE_Buff"), 1f, world.Target);
        Assert.Equal(63f, world.Target.GetNumericAttributeBase(TestAttributeSet.TestAAttr));
    }

    [Fact]
    public void AttributeBased_CaptureSource_SelectedPerEntry()
    {
        var catalog = GasNetDataLoader.LoadCatalog("""
        {
          "effects": {
            "GE_FromSource": {
              "modifiers": [ { "attribute": "TestAttributeSet.TestA", "op": "Override",
                               "magnitude": { "type": "attributeBased", "attribute": "TestAttributeSet.TestB",
                                              "source": "Source", "snapshot": true } } ]
            },
            "GE_FromTarget": {
              "modifiers": [ { "attribute": "TestAttributeSet.TestC", "op": "Override",
                               "magnitude": { "type": "attributeBased", "attribute": "TestAttributeSet.TestB",
                                              "source": "Target" } } ]
            }
          }
        }
        """, Options());

        var world = new TestWorld();
        world.Source.SetNumericAttributeBase(TestAttributeSet.TestBAttr, 42f);

        world.Source.ApplyGameplayEffectToTarget(catalog.Get("GE_FromSource"), 1f, world.Target);
        Assert.Equal(42f, world.Target.GetNumericAttributeBase(TestAttributeSet.TestAAttr));

        world.Source.ApplyGameplayEffectToTarget(catalog.Get("GE_FromTarget"), 1f, world.Target);
        Assert.Equal(5f, world.Target.GetNumericAttributeBase(TestAttributeSet.TestCAttr));
    }

    [Fact]
    public void CustomCalculation_ResolvedFromRegistry()
    {
        var catalog = GasNetDataLoader.LoadCatalog("""
        {
          "effects": {
            "GE_Cost": {
              "modifiers": [ { "attribute": "TestAttributeSet.TestA", "op": "Add",
                               "magnitude": { "type": "customCalculation", "calculation": "DataTestMMC" } } ]
            }
          }
        }
        """, Options());

        var world = new TestWorld();
        world.Source.ApplyGameplayEffectToTarget(catalog.Get("GE_Cost"), 1f, world.Target);
        Assert.Equal(7f, world.Target.GetNumericAttributeBase(TestAttributeSet.TestAAttr)); // 10 + (-3)
    }

    [Fact]
    public void ExecutionCalculation_LoadedAndExecuted()
    {
        var catalog = GasNetDataLoader.LoadCatalog("""
        {
          "effects": {
            "GE_Exec": {
              "modifiers": [],
              "executions": [ { "calculation": "DataTestExecution" } ]
            }
          }
        }
        """, Options());

        var world = new TestWorld();
        var spec = world.Source.MakeOutgoingSpec(catalog.Get("GE_Exec"));
        spec.SetSetByCallerMagnitude(T.Tag("Data.TestDamage"), 4f);
        world.Source.ApplyGameplayEffectSpecToTarget(spec, world.Target);
        Assert.Equal(1f, world.Target.GetNumericAttributeBase(TestAttributeSet.TestBAttr)); // 5 - 4
    }

    [Fact]
    public void DurationPeriodicAndExpiry_FromData()
    {
        // Duration 1s / period 0.5s: executes at 0.5s; the tick due at the 1.0s expiry is
        // swallowed by CheckDuration first (doc-consistent, see README deviations).
        var catalog = GasNetDataLoader.LoadCatalog("""
        {
          "effects": {
            "GE_Burn": {
              "durationPolicy": "HasDuration", "duration": 1, "period": 0.5,
              "modifiers": [ { "attribute": "TestAttributeSet.TestB", "op": "Add",
                               "magnitude": { "type": "scalableFloat", "value": 5 } } ]
            }
          }
        }
        """, Options());

        var world = new TestWorld();
        world.Source.ApplyGameplayEffectToTarget(catalog.Get("GE_Burn"), 1f, world.Target);
        world.Tick(1.1f);

        Assert.Equal(10f, world.Target.GetNumericAttributeBase(TestAttributeSet.TestBAttr));
        Assert.Empty(world.Target.Active.All);
    }

    [Fact]
    public void MultipleModifiers_ExecuteInListOrder()
    {
        var catalog = GasNetDataLoader.LoadCatalog("""
        {
          "effects": {
            "GE_Init": {
              "modifiers": [
                { "attribute": "TestAttributeSet.MaxHealth", "op": "Override", "magnitude": { "type": "scalableFloat", "value": 40 } },
                { "attribute": "TestAttributeSet.Health", "op": "Override", "magnitude": { "type": "scalableFloat", "value": 40 } },
                { "attribute": "TestAttributeSet.Health", "op": "Add", "magnitude": { "type": "scalableFloat", "value": -5 } }
              ]
            }
          }
        }
        """, Options());

        var world = new TestWorld();
        world.Source.ApplyGameplayEffectToTarget(catalog.Get("GE_Init"), 1f, world.Target);
        Assert.Equal(40f, world.Target.GetNumericAttributeBase(TestAttributeSet.MaxHealthAttr));
        Assert.Equal(35f, world.Target.GetNumericAttributeBase(TestAttributeSet.HealthAttr));
    }

    // ------------------------------------------------------------------
    // Tags, stacking, granted abilities, cues
    // ------------------------------------------------------------------

    [Fact]
    public void Tags_GrantedRemovedByOtherEffect_ApplicationBlocked()
    {
        var catalog = GasNetDataLoader.LoadCatalog("""
        {
          "effects": {
            "GE_Burn": {
              "durationPolicy": "HasDuration", "duration": 10,
              "assetTags": ["Effect.DataBurn"],
              "grantedTags": ["State.DataBurning"]
            },
            "GE_DispelBurn": {
              "removeGameplayEffectsWithTags": ["State.DataBurning"]
            },
            "GE_Heal": {
              "applicationTagRequirements": { "ignore": ["State.DataShielded"] },
              "modifiers": [ { "attribute": "TestAttributeSet.TestB", "op": "Add",
                               "magnitude": { "type": "scalableFloat", "value": 1 } } ]
            }
          }
        }
        """, Options());

        var world = new TestWorld();

        world.Source.ApplyGameplayEffectToTarget(catalog.Get("GE_Burn"), 1f, world.Target);
        Assert.True(world.Target.HasMatchingGameplayTag(T.Tag("State.DataBurning")));
        Assert.Single(world.Target.Active.All);

        world.Source.ApplyGameplayEffectToTarget(catalog.Get("GE_DispelBurn"), 1f, world.Target);
        Assert.Empty(world.Target.Active.All);
        Assert.False(world.Target.HasMatchingGameplayTag(T.Tag("State.DataBurning")));

        world.Source.ApplyGameplayEffectToTarget(catalog.Get("GE_Heal"), 1f, world.Target);
        Assert.Equal(6f, world.Target.GetNumericAttributeBase(TestAttributeSet.TestBAttr));

        // Shielded targets reject GE_Heal by application requirement.
        world.Target.AddLooseGameplayTag(T.Tag("State.DataShielded"));
        Assert.Null(world.Source.ApplyGameplayEffectToTarget(catalog.Get("GE_Heal"), 1f, world.Target));
    }

    [Fact]
    public void Stacking_ConfiguredFromData()
    {
        var catalog = GasNetDataLoader.LoadCatalog("""
        {
          "effects": {
            "GE_ArmorStack": {
              "durationPolicy": "Infinite",
              "stacking": { "type": "AggregateByTarget", "limit": 2, "expiry": "RefreshDuration" },
              "modifiers": [ { "attribute": "TestAttributeSet.TestB", "op": "Add",
                               "magnitude": { "type": "scalableFloat", "value": 2 } } ]
            }
          }
        }
        """, Options());

        var def = catalog.Get("GE_ArmorStack");
        Assert.Equal(GameplayEffectStackingType.AggregateByTarget, def.StackingType);
        Assert.Equal(2, def.StackLimitCount);
        Assert.Equal(GameplayEffectStackingExpiryPolicy.RefreshDuration, def.StackExpiryPolicy);

        var world = new TestWorld();
        world.Source.ApplyGameplayEffectToTarget(def, 1f, world.Target);
        world.Source.ApplyGameplayEffectToTarget(def, 1f, world.Target);
        var age = Assert.Single(world.Target.Active.All);
        Assert.Equal(2, age.StackCount);
        // Stacked GE modifiers apply once (engine-consistent; see README deviations).
        Assert.Equal(7f, world.Target.GetNumericAttribute(TestAttributeSet.TestBAttr));
    }

    [Fact]
    public void GrantedAbilities_TypesResolved_GrantAndRevoke()
    {
        var catalog = GasNetDataLoader.LoadCatalog("""
        {
          "effects": {
            "GE_Grant": {
              "durationPolicy": "Infinite",
              "grantedAbilities": [ { "ability": "DataTestAbility", "inputId": 3,
                                      "removalPolicy": "RemoveAbilityOnEnd" } ]
            },
            "GE_GrantDefault": {
              "durationPolicy": "Infinite",
              "grantedAbilities": [ { "ability": "DataTestAbility", "inputId": 3 } ]
            }
          }
        }
        """, Options());

        var entry = Assert.Single(catalog.Get("GE_Grant").GrantedAbilities);
        Assert.Equal(typeof(DataTestAbility), entry.AbilityType);
        Assert.Equal(3, entry.InputID);
        Assert.Equal(GameplayEffectAbilityRemovalPolicy.RemoveAbilityOnEnd, entry.RemovalPolicy);

        // Default policy: the granted ability is revoked the moment its GE goes away.
        var world = new TestWorld();
        var handle = world.Source.ApplyGameplayEffectToTarget(catalog.Get("GE_GrantDefault"), 1f, world.Target);
        Assert.Single(world.Target.FindAbilitySpecsByType<DataTestAbility>());

        world.Target.RemoveActiveGameplayEffect(handle!.Value);
        Assert.Empty(world.Target.FindAbilitySpecsByType<DataTestAbility>());
    }

    [Fact]
    public void CueTags_AutoFireExecutedOnTarget()
    {
        var catalog = GasNetDataLoader.LoadCatalog("""
        {
          "effects": {
            "GE_Hit": {
              "cueTags": ["GameplayCue.Character.DataHit"],
              "modifiers": [ { "attribute": "TestAttributeSet.TestB", "op": "Override",
                               "magnitude": { "type": "scalableFloat", "value": 1 } } ]
            }
          }
        }
        """, Options());

        var world = new TestWorld();
        GameplayTag? fired = null;
        world.Target.OnGameplayCueExecuted += (tag, _) => fired = tag;

        world.Source.ApplyGameplayEffectToTarget(catalog.Get("GE_Hit"), 1f, world.Target);
        Assert.Equal(1f, world.Target.GetNumericAttributeBase(TestAttributeSet.TestBAttr));
        Assert.Equal(T.Tag("GameplayCue.Character.DataHit"), fired);
    }

    // ------------------------------------------------------------------
    // Load-time errors are loud and point at the field
    // ------------------------------------------------------------------

    private static void LoadThrows(string json, string expectedFragment, GasNetDataLoadOptions? options = null)
    {
        var exception = Assert.Throws<GasNetDataException>(
            () => GasNetDataLoader.LoadCatalog(json, options ?? Options()));
        Assert.Contains(expectedFragment, exception.Message);
    }

    [Fact]
    public void UnknownAttributeSet_IsRejected() => LoadThrows("""
        { "effects": { "GE_X": { "modifiers": [ { "attribute": "NoSuchSet.Health",
            "magnitude": { "type": "scalableFloat", "value": 1 } } ] } } }
        """, "attribute set 'NoSuchSet' is not registered");

    [Fact]
    public void UnknownAttributeName_IsRejected() => LoadThrows("""
        { "effects": { "GE_X": { "modifiers": [ { "attribute": "TestAttributeSet.NoSuchAttr",
            "magnitude": { "type": "scalableFloat", "value": 1 } } ] } } }
        """, "'NoSuchAttr' is not a GameplayAttributeData field");

    [Fact]
    public void UnknownMagnitudeType_IsRejected() => LoadThrows("""
        { "effects": { "GE_X": { "modifiers": [ { "attribute": "TestAttributeSet.TestB",
            "magnitude": { "type": "bogus" } } ] } } }
        """, "unknown magnitude type 'bogus'");

    [Fact]
    public void UnregisteredCalculationType_IsRejected() => LoadThrows("""
        { "effects": { "GE_X": { "modifiers": [ { "attribute": "TestAttributeSet.TestB",
            "magnitude": { "type": "customCalculation", "calculation": "Missing" } } ] } } }
        """, "type 'Missing' is not registered");

    [Fact]
    public void WrongBaseType_IsRejected() => LoadThrows("""
        { "effects": { "GE_X": { "modifiers": [ { "attribute": "TestAttributeSet.TestB",
            "magnitude": { "type": "customCalculation", "calculation": "NotACalc" } } ] } } }
        """, "is not a 'ModifierMagnitudeCalculation'",
        new GasNetDataLoadOptions().RegisterAttributeSet<TestAttributeSet>().RegisterType(typeof(object), "NotACalc"));

    [Fact]
    public void UnknownField_IsRejected() => LoadThrows(
        """ { "effects": { "GE_X": { "durationPoliccy": "Instant" } } } """,
        "unknown field 'durationPoliccy'");

    [Fact]
    public void DuplicateEffectName_IsRejected() => LoadThrows("""
        { "effects": {
            "GE_X": { "durationPolicy": "Instant" },
            "GE_X": { "durationPolicy": "Infinite" }
        } }
        """, "Duplicate effect name 'GE_X'");

    [Fact]
    public void InvalidEnumValue_IsRejected() => LoadThrows(
        """ { "effects": { "GE_X": { "durationPolicy": "Sometimes" } } } """,
        "is not a valid GameplayEffectDurationType");

    [Fact]
    public void MalformedTagName_IsRejected() => LoadThrows(
        """ { "effects": { "GE_X": { "assetTags": ["A..B"] } } } """,
        "Invalid gameplay tag name 'A..B'");

    [Fact]
    public void MalformedJson_IsRejected() => LoadThrows("{ \"effects\": ", "JSON parse error");

    [Fact]
    public void CatalogGet_UnknownName_ThrowsWithLoadedNames()
    {
        var catalog = GasNetDataLoader.LoadCatalog(
            """ { "effects": { "GE_Known": {} } } """, Options());
        var exception = Assert.Throws<GasNetDataException>(() => catalog.Get("GE_Other"));
        Assert.Contains("No effect named 'GE_Other'", exception.Message);
        Assert.Contains("GE_Known", exception.Message);
    }
}

/// <summary>MMC returning a fixed -3, registered by name from JSON.</summary>
public sealed class DataTestMMC : ModifierMagnitudeCalculation
{
    public override float CalculateBaseMagnitude(GameplayEffectSpec spec) => -3f;
}

/// <summary>Execution calc: SetByCaller "Data.TestDamage" damage onto TestB.</summary>
public sealed class DataTestExecution : GameplayEffectExecutionCalculation
{
    public override void Execute(GameplayEffectExecutionParams p)
    {
        float damage = p.GetSetByCallerMagnitude(T.Tag("Data.TestDamage"));
        p.Output.AddOutputModifier(TestAttributeSet.TestBAttr, GameplayModOp.Add, -damage);
    }
}

/// <summary>Minimal instantiable ability, registered by name from JSON.</summary>
public sealed class DataTestAbility : GameplayAbility
{
    protected override void ActivateAbility() => EndAbility(wasCancelled: false);
}
