using Xunit;

namespace GasNet.Tests;

/// <summary>
/// Locks the aggregator math to the engine formulas documented in GASDocumentation §4.5.4/§4.5.4.1:
/// CurrentValue = ((Base + ΣAdd) * MultSum) / DivSum with bias-1 sums, then last Override wins.
/// </summary>
public class AggregatorTests
{
    private static AttributeAggregator NewAggregator(params (GameplayModOp op, float magnitude)[] mods)
    {
        var aggregator = new AttributeAggregator();
        int order = 0;
        foreach (var (op, magnitude) in mods)
        {
            aggregator.AddMod(op, new AggregatorMod
            {
                Magnitude = magnitude,
                Source = default,
                ApplicationOrder = ++order,
            });
        }
        return aggregator;
    }

    [Fact]
    public void Additive_Mods_Sum_Then_Add_To_Base()
    {
        var aggregator = NewAggregator((GameplayModOp.Add, 50f), (GameplayModOp.Add, -20f));
        Assert.Equal(130f, aggregator.Evaluate(100f), 3);
    }

    [Fact]
    public void Multiply_Mods_Use_Bias1_Sum_Two_1_5_Give_x2_Not_x2_25()
    {
        // Doc §4.5.4.1: "two 1.5 Multiply mods yield x2.0 (not x2.25)":
        // Sum = 1 + (1.5-1) + (1.5-1) = 2.0
        var aggregator = NewAggregator((GameplayModOp.Multiply, 1.5f), (GameplayModOp.Multiply, 1.5f));
        Assert.Equal(200f, aggregator.Evaluate(100f), 3);
    }

    [Fact]
    public void Multiply_Mods_Two_0_5_Give_Zero_Like_The_Doc_Example()
    {
        // Doc §4.5.4.1: "Multiply 0.5, 0.5 → 0" (multiple values < 1 are outside the valid design range).
        var aggregator = NewAggregator((GameplayModOp.Multiply, 0.5f), (GameplayModOp.Multiply, 0.5f));
        Assert.Equal(0f, aggregator.Evaluate(100f), 3);
    }

    [Fact]
    public void Multiply_Mods_1_1_And_0_5_Give_0_6_Like_The_Doc_Example()
    {
        var aggregator = NewAggregator((GameplayModOp.Multiply, 1.1f), (GameplayModOp.Multiply, 0.5f));
        Assert.Equal(60f, aggregator.Evaluate(100f), 3);
    }

    [Fact]
    public void Multiply_Mods_Two_5_Give_9_Not_10()
    {
        var aggregator = NewAggregator((GameplayModOp.Multiply, 5f), (GameplayModOp.Multiply, 5f));
        Assert.Equal(900f, aggregator.Evaluate(100f), 3);
    }

    [Fact]
    public void Evaluation_Order_Is_Add_Then_Multiply_Then_Divide()
    {
        // ((100 + 10) * 2) / 4 = 55 — additions happen before percentage changes (doc §4.5.4).
        var aggregator = NewAggregator(
            (GameplayModOp.Add, 10f),
            (GameplayModOp.Multiply, 2f),
            (GameplayModOp.Divide, 4f));
        Assert.Equal(55f, aggregator.Evaluate(100f), 3);
    }

    [Fact]
    public void Divide_Uses_Bias1_Sum_Too()
    {
        // Two divide-by-2 mods: DivSum = 1 + (2-1) + (2-1) = 3 → 300/3 = 100.
        var aggregator = NewAggregator((GameplayModOp.Divide, 2f), (GameplayModOp.Divide, 2f));
        Assert.Equal(100f, aggregator.Evaluate(300f), 3);
    }

    [Fact]
    public void Override_Wins_And_Last_Applied_Takes_Precedence()
    {
        var aggregator = NewAggregator(
            (GameplayModOp.Override, 42f),
            (GameplayModOp.Add, 999f),
            (GameplayModOp.Override, 7f));
        Assert.Equal(7f, aggregator.Evaluate(100f), 3);
    }

    [Fact]
    public void Empty_Channels_Are_Neutral()
    {
        var aggregator = NewAggregator();
        Assert.Equal(123f, aggregator.Evaluate(123f), 3);
    }

    [Fact]
    public void RemoveModsFrom_Restores_Previous_Value()
    {
        var aggregator = new AttributeAggregator();
        var handle = default(ActiveGameplayEffectHandle);
        aggregator.AddMod(GameplayModOp.Multiply, new AggregatorMod { Magnitude = 1.5f, Source = handle, ApplicationOrder = 1 });
        Assert.Equal(150f, aggregator.Evaluate(100f), 3);
        aggregator.RemoveModsFrom(handle);
        Assert.Equal(100f, aggregator.Evaluate(100f), 3);
    }

    [Fact]
    public void MostNegativeModQualifier_Keeps_All_Positive_And_Single_Most_Negative()
    {
        var aggregator = NewAggregator((GameplayModOp.Add, -30f), (GameplayModOp.Add, -10f), (GameplayModOp.Add, 5f));
        aggregator.EvaluationMetaData = AggregatorEvaluateMetaData.MostNegativeMod_AllPositiveMods;
        // -30 qualifies (most negative), -10 does not, +5 does: 100 - 30 + 5 = 75.
        Assert.Equal(75f, aggregator.Evaluate(100f), 3);
    }

    [Fact]
    public void OnlyStrongestSlowQualifier_Keeps_Single_Strongest_Slow()
    {
        var aggregator = NewAggregator((GameplayModOp.Multiply, 0.5f), (GameplayModOp.Multiply, 0.7f), (GameplayModOp.Multiply, 1.5f));
        aggregator.EvaluationMetaData = AggregatorEvaluateMetaData.OnlyStrongestSlow_AllOtherMods;
        // 0.5 qualifies (strongest slow), 0.7 does not, 1.5 does:
        // bias-1 sum = 1 + (0.5-1) + (1.5-1) = 1.0 → 100 * 1.0 = 100 (NOT 0.5*1.5 — see doc §4.5.4).
        Assert.Equal(100f, aggregator.Evaluate(100f), 3);
    }

    [Fact]
    public void Capture_Tag_Filters_Exclude_Missing_Mods()
    {
        // Doc §4.5.4.2: AttributeBased captures can filter contributing mods by source/target tags.
        var fromFire = new AggregatorMod
        {
            Magnitude = 20f,
            Source = default,
            ApplicationOrder = 1,
            SourceTags = new GameplayTagContainer(T.Tag("Element.Fire")),
        };
        var fromIce = new AggregatorMod
        {
            Magnitude = 50f,
            Source = default,
            ApplicationOrder = 2,
            SourceTags = new GameplayTagContainer(T.Tag("Element.Ice")),
        };
        var aggregator = new AttributeAggregator();
        aggregator.AddMod(GameplayModOp.Add, fromFire);
        aggregator.AddMod(GameplayModOp.Add, fromIce);

        Assert.Equal(170f, aggregator.Evaluate(100f), 3); // unfiltered: both add

        float filtered = aggregator.Evaluate(100f, sourceTagFilter: new GameplayTagContainer(T.Tag("Element.Fire")));
        Assert.Equal(120f, filtered, 3); // only the Fire mod qualifies
    }
}
