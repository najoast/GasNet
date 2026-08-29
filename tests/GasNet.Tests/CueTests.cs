using Xunit;

namespace GasNet.Tests;

public class CueTests
{
    private static readonly GameplayTag CueTag = T.Tag("GameplayCue.Test.Buff");

    private static TestWorld NewWorld()
    {
        var world = new TestWorld();
        world.Target.ApplyGameplayEffectToSelf(new GameplayEffectDefinition()
            .With(policy: GameplayEffectDurationType.Instant)
            .AddModifier(TestAttributeSet.MaxHealthAttr, GameplayModOp.Override, new ScalableFloatMagnitude(100))
            .AddModifier(TestAttributeSet.HealthAttr, GameplayModOp.Override, new ScalableFloatMagnitude(100)));
        return world;
    }

    private static GameplayCueManager Manager => AbilitySystemGlobals.Get().GameplayCueManager;

    // ---------------- GE-driven cues (doc §4.8.2, §4.8.8) ----------------

    [Fact]
    public void Instant_GE_Fires_Executed_Cue_Only()
    {
        var world = NewWorld();
        int added = 0, executed = 0;
        world.Target.OnGameplayCueAdded += (_, _) => added++;
        world.Target.OnGameplayCueExecuted += (_, _) => executed++;

        var def = T.InstantGE(TestAttributeSet.TestBAttr, GameplayModOp.Add, 1f);
        def.GameplayCueTags.AddTag(CueTag);
        world.Target.ApplyGameplayEffectToSelf(def);

        Assert.Equal(1, executed);
        Assert.Equal(0, added); // Instant GEs never fire Added/Removed (no tags, no active state)
    }

    [Fact]
    public void Duration_GE_Fires_Added_Then_Removed()
    {
        var world = NewWorld();
        int added = 0, removed = 0, executed = 0;
        world.Target.OnGameplayCueAdded += (_, _) => added++;
        world.Target.OnGameplayCueRemoved += (_, _) => removed++;
        world.Target.OnGameplayCueExecuted += (_, _) => executed++;

        var def = T.DurationGE(TestAttributeSet.TestBAttr, GameplayModOp.Add, 1f, duration: 2f);
        def.GameplayCueTags.AddTag(CueTag);
        var handle = world.Target.ApplyGameplayEffectToSelf(def)!.Value;

        Assert.Equal(1, added);
        Assert.Equal(0, executed);

        world.Target.RemoveActiveGameplayEffect(handle);
        Assert.Equal(1, removed);
    }

    [Fact]
    public void Periodic_Tick_Fires_Executed_Cue()
    {
        var world = NewWorld();
        int executed = 0;
        world.Target.OnGameplayCueExecuted += (_, _) => executed++;

        var def = new GameplayEffectDefinition()
            .With(policy: GameplayEffectDurationType.HasDuration, duration: 2f, period: 1f)
            .AddModifier(TestAttributeSet.TestBAttr, GameplayModOp.Add, new ScalableFloatMagnitude(1f));
        def.GameplayCueTags.AddTag(CueTag);
        world.Target.ApplyGameplayEffectToSelf(def);

        world.Tick(2.5f);
        Assert.Equal(1, executed); // tick at t=1 fires; the t=2 tick is consumed by expiry
    }

    [Fact]
    public void ExecCalc_Can_Mark_Cues_Handled_Manually()
    {
        var world = NewWorld();
        int executed = 0;
        world.Target.OnGameplayCueExecuted += (_, _) => executed++;

        var def = new GameplayEffectDefinition()
            .With(policy: GameplayEffectDurationType.Instant)
            .WithCueTags(CueTag)
            .WithExecutions(new SilentExec());
        world.Target.ApplyGameplayEffectToSelf(def);

        Assert.Equal(0, executed); // doc §4.8.6: OutExecutionOutput.MarkGameplayCuesHandledManually()
    }

    private sealed class SilentExec : GameplayEffectExecutionCalculation
    {
        public override void Execute(GameplayEffectExecutionParams p) => p.Output.MarkGameplayCuesHandledManually();
    }

    // ---------------- Cue manager & notify lifecycle (doc §4.8.1) ----------------

    [Fact]
    public void Actor_Cue_Notify_Manages_One_Instance_Per_Target_And_Tag()
    {
        var notify = new CountingActorCue { GameplayCueTags = new GameplayTagContainer(CueTag) };
        Manager.RegisterNotify(notify);
        try
        {
            var world = NewWorld();
            CountingActorCue.Instances = 0;
            CountingActorCue.ActiveCalls = 0;
            CountingActorCue.RemoveCalls = 0;

            world.Target.AddGameplayCue(CueTag); // OnActive + WhileActive
            world.Target.AddGameplayCue(CueTag); // re-add while active: deduped, no new instance
            Assert.Equal(1, CountingActorCue.Instances);
            Assert.Equal(1, CountingActorCue.ActiveCalls);

            world.Target.RemoveGameplayCue(CueTag);
            Assert.Equal(1, CountingActorCue.RemoveCalls);

            world.Target.AddGameplayCue(CueTag); // a fresh instance is created after removal
            Assert.Equal(2, CountingActorCue.Instances);
        }
        finally
        {
            Manager.UnregisterNotify(notify);
            Manager.RemoveAllActorCues(null);
        }
    }

    [Fact]
    public void Static_Cue_Notify_Receives_Execute()
    {
        var notify = new CountingStaticCue { GameplayCueTags = new GameplayTagContainer(CueTag) };
        Manager.RegisterNotify(notify);
        try
        {
            var world = NewWorld();
            world.Target.ExecuteGameplayCue(CueTag);
            Assert.Equal(1, notify.ExecuteCalls);
        }
        finally
        {
            Manager.UnregisterNotify(notify);
        }
    }

    [Fact]
    public void Cue_Tag_Walks_Up_Parents_To_Find_The_Notify()
    {
        // Notify registered on "GameplayCue.Test" — an event for "GameplayCue.Test.Buff" finds it.
        var notify = new CountingStaticCue { GameplayCueTags = new GameplayTagContainer(T.Tag("GameplayCue.Test")) };
        Manager.RegisterNotify(notify);
        try
        {
            var world = NewWorld();
            world.Target.ExecuteGameplayCue(CueTag);
            Assert.Equal(1, notify.ExecuteCalls);
        }
        finally
        {
            Manager.UnregisterNotify(notify);
        }
    }

    [Fact]
    public void SuppressGameplayCues_Disables_Routing()
    {
        var world = NewWorld();
        world.Target.SuppressGameplayCues = true;
        int executed = 0;
        world.Target.OnGameplayCueExecuted += (_, _) => executed++;

        world.Target.ExecuteGameplayCue(CueTag);
        Assert.Equal(0, executed);
    }

    // ---------------- doubles ----------------

    private sealed class CountingActorCue : GameplayCueNotify_Actor
    {
        // Static counters: events are invoked on the per-target INSTANCE, not the registered prototype.
        public static int Instances;
        public static int ActiveCalls;
        public static int RemoveCalls;

        public override GameplayCueNotify CreateInstance() =>
            new CountingActorCue { GameplayCueTags = GameplayCueTags };

        public override void OnActive(GameplayCueParameters parameters)
        {
            Instances++;
            ActiveCalls++;
        }

        public override void OnRemove(GameplayCueParameters parameters) => RemoveCalls++;
    }

    private sealed class CountingStaticCue : GameplayCueNotify_Static
    {
        public int ExecuteCalls;
        public override void OnExecute(GameplayCueParameters parameters) => ExecuteCalls++;
    }
}
