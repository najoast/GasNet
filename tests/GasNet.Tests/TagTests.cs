using Xunit;

namespace GasNet.Tests;

public class TagTests
{
    [Fact]
    public void Tags_AreHierarchical_AndRegistered()
    {
        var stun = T.Tag("State.Debuff.Stun");
        Assert.Equal("State.Debuff.Stun", stun.Name);
        Assert.True(stun.IsValid);
        Assert.Equal("State.Debuff", stun.RequestDirectParent().Name);
    }

    [Fact]
    public void Tag_MatchesTag_ChecksAncestors()
    {
        var deep = T.Tag("A.B.C");
        Assert.True(deep.MatchesTag(T.Tag("A.B.C")));
        Assert.True(deep.MatchesTag(T.Tag("A.B")));   // deeper implies shallower
        Assert.True(deep.MatchesTag(T.Tag("A")));
        Assert.False(deep.MatchesTag(T.Tag("A.B.C.D")));
        Assert.False(deep.MatchesTag(T.Tag("A.B.X"))); // sibling segment must not match
        Assert.True(deep.MatchesTag(T.Tag("A.B.C"), exact: true));
        Assert.False(deep.MatchesTag(T.Tag("A.B"), exact: true));
    }

    [Fact]
    public void Container_HasTag_IsHierarchical()
    {
        var container = new GameplayTagContainer(T.Tag("Ability.Attack.Fireball"));
        Assert.True(container.HasTag(T.Tag("Ability.Attack")));
        Assert.True(container.HasTag(T.Tag("Ability.Attack.Fireball")));
        Assert.False(container.HasTag(T.Tag("Ability.Attack.Fireball.Ultimate")));
        Assert.True(container.HasTagExact(T.Tag("Ability.Attack.Fireball")));
        Assert.False(container.HasTagExact(T.Tag("Ability.Attack")));
    }

    [Fact]
    public void Container_HasAny_HasAll_HasNone()
    {
        var container = new GameplayTagContainer(T.Tag("A.B"), T.Tag("C.D"));
        var any = new GameplayTagContainer(T.Tag("C.D"), T.Tag("X.Y"));
        var all = new GameplayTagContainer(T.Tag("C"), T.Tag("A"));
        var none = new GameplayTagContainer(T.Tag("Z"));

        Assert.True(container.HasAny(any));
        Assert.True(container.HasAll(all));  // hierarchical: C.D implies C, A.B implies A
        Assert.False(container.HasAllExact(all));
        Assert.True(container.HasNone(none));
    }

    [Fact]
    public void TagRequirements_RequiredAll_IgnoredAny()
    {
        var requirements = new GameplayTagRequirements();
        requirements.RequiredTags.AddTag(T.Tag("A"));
        requirements.IgnoredTags.AddTag(T.Tag("B"));

        Assert.True(requirements.RequirementsMet(new GameplayTagContainer(T.Tag("A.B")))); // A implied by A.B
        Assert.False(requirements.RequirementsMet(new GameplayTagContainer(T.Tag("A"), T.Tag("B"))));
        Assert.False(requirements.RequirementsMet(new GameplayTagContainer()));
    }

    [Fact]
    public void TagQuery_Composes_And_EmptyNeverMatches()
    {
        var all = GameplayTagQuery.AllTags(new GameplayTagContainer(T.Tag("A")), exact: false);
        var noB = GameplayTagQuery.NoTags(new GameplayTagContainer(T.Tag("B")));
        var combined = GameplayTagQuery.All(all, noB);

        Assert.True(combined.Matches(new GameplayTagContainer(T.Tag("A.C"))));
        Assert.False(combined.Matches(new GameplayTagContainer(T.Tag("A"), T.Tag("B"))));
        Assert.False(combined.Matches(new GameplayTagContainer()));
        Assert.False(GameplayTagQuery.Empty.Matches(new GameplayTagContainer(T.Tag("A")))); // empty query: false (UE 5.3+)
    }

    [Fact]
    public void CountContainer_Counts_Events_And_Hierarchy()
    {
        var container = new GameplayTagCountContainer();
        var stun = T.Tag("State.Debuff.Stun");
        int stunCountNotifications = 0;
        int anyNotifications = 0;

        using (container.RegisterGameplayTagEvent(stun, GameplayTagEventType.AnyCountChange, (_, _) => stunCountNotifications++))
        {
            container.AnyTagCountChanged += (_, _) => anyNotifications++;

            container.AddTag(stun);
            container.AddTag(stun);
            Assert.Equal(2, container.GetTagCount(stun));

            container.RemoveTag(stun);
            Assert.Equal(1, container.GetTagCount(stun));
            Assert.Equal(3, stunCountNotifications); // AnyCountChange fires on EVERY change: +1, +1, -1
        }

        container.RemoveTag(stun);
        Assert.Equal(3, stunCountNotifications); // unchanged: the subscription was disposed, no further notifications
        Assert.False(container.HasTag(stun));

        // NewOrRemoved only fires on 0<->n transitions
        int transitions = 0;
        using var _ = container.RegisterGameplayTagEvent(stun, GameplayTagEventType.NewOrRemoved, (_, _) => transitions++);
        container.AddTag(stun);
        container.AddTag(stun);
        container.RemoveTag(stun);
        container.RemoveTag(stun);
        Assert.Equal(2, transitions);
    }

    [Fact]
    public void CountContainer_HasTag_RespectsCountZero()
    {
        var container = new GameplayTagCountContainer();
        var tag = T.Tag("X.Y");
        container.AddTag(tag);
        container.SetTagCount(tag, 0); // present-with-zero must read as absent (doc §4.2)
        Assert.False(container.HasTag(tag));
        Assert.Equal(0, container.GetTagCount(tag));
    }
}
