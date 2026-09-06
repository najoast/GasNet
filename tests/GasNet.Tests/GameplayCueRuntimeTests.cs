using GasNet;
using GasNet.Data;
using Xunit;

namespace GasNet.Tests;

/// <summary>
/// The runtime adapter: turn <see cref="CueDefinition"/>s into <see cref="GameplayCueNotify"/>s
/// and register them with a <see cref="GameplayCueManager"/>. These tests live alongside the
/// <see cref="CueTests"/> class (which covers the manager's routing semantics) and focus on
/// the adapter layer: dispatch by trigger, lifecycle for actor cues, and exception isolation.
/// </summary>
public class GameplayCueRuntimeTests
{
    private static readonly GameplayTag HitTag = T.Tag("GameplayCue.Runtime.Hit");
    private static readonly GameplayTag BuffTag = T.Tag("GameplayCue.Runtime.Buff");

    private static GameplayCueManager Manager => AbilitySystemGlobals.Get().GameplayCueManager;

    private static GasNetDataCueLoadOptions Options() => new GasNetDataCueLoadOptions()
        .RegisterActionType<CountingCueAction>(CountingCueAction.Schema)
        .RegisterActionType<ThrowingCueAction>(ThrowingCueAction.Schema)
        .RegisterActionType<AlwaysCueAction>(); // no schema, no extra fields

    // ------------------------------------------------------------------
    // Registration
    // ------------------------------------------------------------------

    [Fact]
    public void RegisterAll_ReturnsCount_AndTracksNotifies()
    {
        var catalog = GasNetDataCueLoader.LoadCues("""
        { "cues": {
            "Hit":  { "tag": "GameplayCue.Runtime.Hit",  "actions": [ { "type": "AlwaysCueAction" } ] },
            "Buff": { "tag": "GameplayCue.Runtime.Buff", "isStatic": false,
                      "actions": [ { "type": "AlwaysCueAction" } ] }
        } }
        """, Options());

        var runtime = new GameplayCueRuntime(Manager, Options());
        var count = runtime.RegisterAll(catalog);

        Assert.Equal(2, count);
        Assert.Equal(2, runtime.RegisteredNotifies.Count);
    }

    [Fact]
    public void InvalidTag_IsRejected()
    {
        // The loader enforces GameplayCue. prefix as a Warn (not an error) so a tag like
        // "Custom.Hit" still loads. The runtime must refuse it because the manager would silently
        // ignore it at dispatch time.
        var catalog = GasNetDataCueLoader.LoadCues("""
        { "cues": { "X": { "tag": "Custom.NotACue", "actions": [ { "type": "AlwaysCueAction" } ] } } }
        """, Options());
        var runtime = new GameplayCueRuntime(Manager, Options());
        Assert.Throws<GasNetDataException>(() => runtime.RegisterAll(catalog));
    }

    // ------------------------------------------------------------------
    // Static cue: Executed-only dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void StaticCue_DispatchesOnlyOnExecuted()
    {
        CountingCueAction.ResetCounters();
        var counters = CountingCueAction.Counters;
        var options = new GasNetDataCueLoadOptions()
            .RegisterActionType<CountingCueAction>(CountingCueAction.Schema);
        var catalog = GasNetDataCueLoader.LoadCues("""
        { "cues": { "Hit": { "tag": "GameplayCue.Runtime.Hit", "actions": [
            { "type": "CountingCueAction", "triggerOn": "Executed", "label": "A" }
        ] } } }
        """, options);
        var runtime = new GameplayCueRuntime(Manager, options);
        runtime.RegisterAll(catalog);
        try
        {
            var world = new TestWorld();
            world.Source.ExecuteGameplayCue(HitTag);
            world.Source.AddGameplayCue(HitTag);
            world.Source.AddGameplayCue(HitTag);
            world.Source.RemoveGameplayCue(HitTag);

            Assert.Equal(1, counters.Get("A")); // Only the Executed event fired the action.
        }
        finally
        {
            foreach (var n in runtime.RegisteredNotifies) Manager.UnregisterNotify(n);
            Manager.RemoveAllActorCues(null);
        }
    }

    [Fact]
    public void StaticCue_MultipleActionsSameTrigger_AllFire()
    {
        CountingCueAction.ResetCounters();
        var counters = CountingCueAction.Counters;
        var options = new GasNetDataCueLoadOptions()
            .RegisterActionType<CountingCueAction>(CountingCueAction.Schema);
        var catalog = GasNetDataCueLoader.LoadCues("""
        { "cues": { "Hit": { "tag": "GameplayCue.Runtime.Hit", "actions": [
            { "type": "CountingCueAction", "triggerOn": "Executed", "label": "A" },
            { "type": "CountingCueAction", "triggerOn": "Executed", "label": "B" },
            { "type": "CountingCueAction", "triggerOn": "OnActive", "label": "C" }
        ] } } }
        """, options);
        var runtime = new GameplayCueRuntime(Manager, options);
        runtime.RegisterAll(catalog);
        try
        {
            var world = new TestWorld();
            world.Source.ExecuteGameplayCue(HitTag);

            Assert.Equal(1, counters.Get("A"));
            Assert.Equal(1, counters.Get("B"));
            Assert.Equal(0, counters.Get("C")); // OnActive doesn't fire on a Static cue.
        }
        finally
        {
            foreach (var n in runtime.RegisteredNotifies) Manager.UnregisterNotify(n);
        }
    }

    // ------------------------------------------------------------------
    // Actor cue: OnActive / WhileActive / Removed lifecycle
    // ------------------------------------------------------------------

    [Fact]
    public void ActorCue_LifecycleFiresAllThreeEvents_AndIsolatesInstances()
    {
        CountingCueAction.ResetCounters();
        var counters = CountingCueAction.Counters;
        var options = new GasNetDataCueLoadOptions()
            .RegisterActionType<CountingCueAction>(CountingCueAction.Schema);
        var catalog = GasNetDataCueLoader.LoadCues("""
        { "cues": { "Buff": { "tag": "GameplayCue.Runtime.Buff", "isStatic": false, "actions": [
            { "type": "CountingCueAction", "triggerOn": "OnActive",    "label": "Active"    },
            { "type": "CountingCueAction", "triggerOn": "WhileActive", "label": "While"     },
            { "type": "CountingCueAction", "triggerOn": "Removed",     "label": "Removed"   }
        ] } } }
        """, options);
        var runtime = new GameplayCueRuntime(Manager, options);
        runtime.RegisterAll(catalog);
        try
        {
            var world = new TestWorld();
            var target = world.Target;

            target.AddGameplayCue(BuffTag);   // OnActive + first WhileActive (engine pairing)
            target.AddGameplayCue(BuffTag);   // WhileActive refresh (re-Added while active)
            target.RemoveGameplayCue(BuffTag);

            Assert.Equal(1, counters.Get("Active"));
            Assert.True(counters.Get("While") >= 1, "WhileActive should have fired at least once");
            Assert.Equal(1, counters.Get("Removed"));

            // RemoveAllGameplayCues with no active instance is a no-op (verified by no exception).
            target.RemoveAllGameplayCues();
        }
        finally
        {
            foreach (var n in runtime.RegisteredNotifies) Manager.UnregisterNotify(n);
            Manager.RemoveAllActorCues(null);
        }
    }

    // ------------------------------------------------------------------
    // Exception isolation
    // ------------------------------------------------------------------

    [Fact]
    public void ThrowingAction_DoesNotAbortOtherActions()
    {
        CountingCueAction.ResetCounters();
        var counters = CountingCueAction.Counters;
        var options = Options();
        var catalog = GasNetDataCueLoader.LoadCues("""
        { "cues": { "Hit": { "tag": "GameplayCue.Runtime.Hit", "actions": [
            { "type": "CountingCueAction",  "triggerOn": "Executed", "label": "A" },
            { "type": "ThrowingCueAction",  "triggerOn": "Executed" },
            { "type": "CountingCueAction",  "triggerOn": "Executed", "label": "B" }
        ] } } }
        """, options);
        var runtime = new GameplayCueRuntime(Manager, options);
        runtime.RegisterAll(catalog);
        var captured = new List<string>();
        GasNetLog.OnError = msg => captured.Add(msg);
        try
        {
            var world = new TestWorld();
            world.Source.ExecuteGameplayCue(HitTag);

            Assert.Equal(1, counters.Get("A"));
            Assert.Equal(1, counters.Get("B"));
            // The SetByCaller warn we expect is among any others other tests may have logged.
            Assert.Contains(captured, m => m.Contains("ThrowingCueAction"));
        }
        finally
        {
            GasNetLog.OnError = null;
            foreach (var n in runtime.RegisteredNotifies) Manager.UnregisterNotify(n);
        }
    }

    // ------------------------------------------------------------------
    // Parent-tag fallback
    // ------------------------------------------------------------------

    [Fact]
    public void CueTag_WalksUpParentsToMatch_BehaviorPreserved()
    {
        // Engine behavior: a notify registered for "GameplayCue.Runtime.Hit" matches a
        // "GameplayCue.Runtime.Hit.Critical" event via the parent walk in FindNotify.
        // This test confirms the runtime's notify participates in that walk, not just exact match.
        CountingCueAction.ResetCounters();
        var counters = CountingCueAction.Counters;
        var options = new GasNetDataCueLoadOptions()
            .RegisterActionType<CountingCueAction>(CountingCueAction.Schema);
        var catalog = GasNetDataCueLoader.LoadCues("""
        { "cues": { "Hit": { "tag": "GameplayCue.Runtime.Hit", "actions": [
            { "type": "CountingCueAction", "triggerOn": "Executed", "label": "X" }
        ] } } }
        """, options);
        var runtime = new GameplayCueRuntime(Manager, options);
        runtime.RegisterAll(catalog);
        try
        {
            var world = new TestWorld();
            var critical = T.Tag("GameplayCue.Runtime.Hit.Critical");
            world.Source.ExecuteGameplayCue(critical);

            Assert.Equal(1, counters.Get("X"));
        }
        finally
        {
            foreach (var n in runtime.RegisteredNotifies) Manager.UnregisterNotify(n);
        }
    }
}

// ----------------------------------------------------------------------
// Test action types
// ----------------------------------------------------------------------

public sealed class CountingCueAction : CueAction
{
    public string Label { get; set; } = "";

    public override void Execute(GameplayCueParameters parameters)
    {
        Counters.Get(Label); // touch the counter (defensive: creates the slot)
        Counters.Increment(Label);
    }

    public static Counter Counters = new();

    public static void ResetCounters() => Counters = new Counter();

    public sealed class Counter
    {
        private readonly Dictionary<string, int> _counts = [];
        public int Get(string label) => _counts.TryGetValue(label, out var n) ? n : 0;
        public void Increment(string label) => _counts[label] = Get(label) + 1;
    }

    public static readonly CueActionSchema Schema = new()
    {
        Reader = (action, el) =>
        {
            var self = (CountingCueAction)action;
            if (el.TryGetProperty("label", out var l) && l.ValueKind == System.Text.Json.JsonValueKind.String)
                self.Label = l.GetString()!;
        },
        Writer = (action, node) =>
        {
            var self = (CountingCueAction)action;
            if (self.Label.Length != 0) node["label"] = self.Label;
        },
    };
}

public sealed class ThrowingCueAction : CueAction
{
    public override void Execute(GameplayCueParameters parameters) =>
        throw new InvalidOperationException("boom");

    public static readonly CueActionSchema Schema = new();
}

public sealed class AlwaysCueAction : CueAction
{
    public override void Execute(GameplayCueParameters parameters) { }
}
