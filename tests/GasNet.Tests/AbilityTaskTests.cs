using Xunit;

namespace GasNet.Tests;

/// <summary>AbilityTask lifecycle semantics (doc §4.7): ticking, teardown, input/event routing.</summary>
/// <para>
/// Note on test doubles: InstancedPerActor activations run on a clone of the prototype, so any
/// state recorded by the running instance goes to static fields (same pattern as AbilityTests'
/// CountingAbility), and task factories must be handed the running instance, not the prototype.
/// </para>
public class AbilityTaskTests
{
    // ---------------- WaitDelay (doc §4.7) ----------------

    [Fact]
    public void WaitDelay_Fires_After_Time_Exactly_Once()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ability = new TaskHostAbility();
        int fired = 0;
        ability.Body = self => AbilityTask_WaitDelay.WaitForDelay(self, 1f, onFire: () => fired++);
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));
        asc.TryActivateAbility(handle);

        world.Tick(0.5f);
        Assert.Equal(0, fired);
        world.Tick(0.5f);
        Assert.Equal(1, fired);
        world.Tick(2f);
        Assert.Equal(1, fired); // one-shot: no repeat
    }

    [Fact]
    public void Chained_WaitDelay_Repeats_Until_Ability_Ends()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ability = new ChainingDelayAbility();
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));
        ChainingDelayAbility.Fires = 0;
        asc.TryActivateAbility(handle);

        world.Tick(0.99f);
        Assert.Equal(0, ChainingDelayAbility.Fires);
        world.Tick(0.02f);
        Assert.Equal(1, ChainingDelayAbility.Fires); // ~1s
        world.Tick(1f);
        Assert.Equal(2, ChainingDelayAbility.Fires);
        world.Tick(1f);
        Assert.Equal(3, ChainingDelayAbility.Fires);

        asc.CancelAbility(handle);
        world.Tick(5f);
        Assert.Equal(3, ChainingDelayAbility.Fires); // cancelled chain stops
    }

    [Fact]
    public void WaitDelay_Does_Not_Fire_When_Ability_Is_Cancelled()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ability = new TaskHostAbility();
        bool fired = false, cancelled = false;
        ability.Body = self => AbilityTask_WaitDelay.WaitForDelay(self, 10f,
            onFire: () => fired = true, onCancelled: () => cancelled = true);
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));
        asc.TryActivateAbility(handle);

        world.Tick(0.25f);
        asc.CancelAbility(handle);

        Assert.False(fired);
        Assert.True(cancelled);
        Assert.Empty(asc.FindAbilitySpec(handle)!.Tasks); // torn down with the ability
    }

    [Fact]
    public void WaitDelay_Pending_Task_Ends_With_AbilityEnded_Reason_On_Natural_End()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ability = new EndAfterSpawnAbility();
        AbilityTaskEndReason? reason = null;
        bool fired = false;
        ability.Body = self =>
        {
            var task = AbilityTask_WaitDelay.WaitForDelay(self, 10f, onFire: () => fired = true);
            task!.TaskEnded += (_, r) => reason = r;
        };
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));
        asc.TryActivateAbility(handle); // ends itself right after spawning

        Assert.Equal(AbilityTaskEndReason.AbilityEnded, reason);
        world.Tick(20f);
        Assert.False(fired); // pending task died with the ability
    }

    // ---------------- WaitInputPress / WaitInputRelease (doc §4.6.2, §4.7) ----------------

    [Fact]
    public void WaitInputPress_Fires_On_Next_Press_Of_Bound_Input()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ability = new TaskHostAbility();
        bool pressed = false;
        ability.Body = self => AbilityTask_WaitInputPress.WaitForInputPress(self, onPress: () => pressed = true);
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1, inputID: 3));
        asc.TryActivateAbility(handle);

        asc.AbilityLocalInputPressed(1); // different binding
        Assert.False(pressed);

        asc.AbilityLocalInputPressed(3);
        Assert.True(pressed);
    }

    [Fact]
    public void WaitInputRelease_Completes_The_WhileInputActive_Pattern()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ability = new HoldAbility();
        HoldAbility.Released = false;
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1, inputID: 2));

        asc.AbilityLocalInputPressed(2); // activates the ability
        Assert.True(asc.FindAbilitySpec(handle)!.IsActive);
        Assert.False(HoldAbility.Released);

        asc.AbilityLocalInputReleased(2);
        Assert.True(HoldAbility.Released);
        Assert.False(asc.FindAbilitySpec(handle)!.IsActive); // ended from the task callback
    }

    // ---------------- WaitGameplayEvent (doc §4.6.11, §4.7) ----------------

    [Fact]
    public void WaitGameplayEvent_Fires_With_Payload_And_Stops_Listening()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ability = new TaskHostAbility();
        int events = 0;
        float lastMagnitude = 0f;
        ability.Body = self => AbilityTask_WaitGameplayEvent.WaitForEvent(self, T.Tag("Event.Hit"),
            onEvent: data => { events++; lastMagnitude = data.EventMagnitude; });
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));
        asc.TryActivateAbility(handle);

        asc.HandleGameplayEvent(T.Tag("Event.Hit"), new GameplayEventData { EventMagnitude = 7f });
        Assert.Equal(1, events);
        Assert.Equal(7f, lastMagnitude);

        asc.HandleGameplayEvent(T.Tag("Event.Hit"), new GameplayEventData { EventMagnitude = 9f });
        Assert.Equal(1, events); // one-shot: unsubscribed after completing
        _ = handle;
    }

    [Fact]
    public void WaitGameplayEvent_Matches_Descendant_Tags_Unless_Exact()
    {
        var world = new TestWorld();
        var asc = world.Target;

        // Hierarchical: listening for Event.Hit catches Event.Hit.Spell.
        var broad = new TaskHostAbility();
        bool broadFired = false;
        broad.Body = self => AbilityTask_WaitGameplayEvent.WaitForEvent(self, T.Tag("Event.Hit"),
            onEvent: _ => broadFired = true);
        var broadHandle = asc.GiveAbility(new GameplayAbilitySpec(broad, 1));
        asc.TryActivateAbility(broadHandle);
        asc.HandleGameplayEvent(T.Tag("Event.Hit.Spell"));
        Assert.True(broadFired);

        // Exact: Event.Hit does not match Event.Hit.Spell.
        var exact = new TaskHostAbility();
        int exactFired = 0;
        exact.Body = self => AbilityTask_WaitGameplayEvent.WaitForEvent(self, T.Tag("Event.Hit"),
            onEvent: _ => exactFired++, exactMatch: true);
        var exactHandle = asc.GiveAbility(new GameplayAbilitySpec(exact, 1));
        asc.TryActivateAbility(exactHandle);
        asc.HandleGameplayEvent(T.Tag("Event.Hit.Spell"));
        Assert.Equal(0, exactFired);
        asc.HandleGameplayEvent(T.Tag("Event.Hit"));
        Assert.Equal(1, exactFired);
    }

    [Fact]
    public void WaitGameplayEvent_Stops_Listening_When_Ability_Ends()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ability = new TaskHostAbility();
        bool fired = false;
        ability.Body = self => AbilityTask_WaitGameplayEvent.WaitForEvent(self, T.Tag("Event.Hit"),
            onEvent: _ => fired = true);
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));
        asc.TryActivateAbility(handle);

        asc.CancelAbility(handle);
        asc.HandleGameplayEvent(T.Tag("Event.Hit"), new GameplayEventData { EventMagnitude = 1f });
        Assert.False(fired); // unsubscribed during teardown
    }

    // ---------------- Hosting rules (GameplayAbility.SpawnTask) ----------------

    [Fact]
    public void SpawnTask_Fails_For_NonInstanced_Ability()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ability = new NonInstancedTaskAbility();
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));

        List<string> warns = CaptureWarns();
        try
        {
            Assert.True(asc.TryActivateAbility(handle)); // activation itself is fine
        }
        finally
        {
            RestoreWarns();
        }
        Assert.Null(ability.SpawnResult); // NonInstanced runs on the prototype, which cannot host tasks
        Assert.Contains(warns, w => w.Contains("NonInstanced"));
    }

    [Fact]
    public void SpawnTask_Fails_Outside_Activation()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ability = new SpawnOnGrantAbility();
        asc.GiveAbility(new GameplayAbilitySpec(ability, 1));

        Assert.Null(ability.SpawnResult); // OnGiveAbility: not active yet
    }

    // ---------------- Interaction with ability lifecycle ----------------

    [Fact]
    public void Task_Callback_Can_End_The_Ability_Without_Reentrancy()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ability = new TaskHostAbility();
        int fired = 0;
        ability.Body = self => AbilityTask_WaitDelay.WaitForDelay(self, 0.5f, onFire: () =>
        {
            fired++;
            self.EndAbility(wasCancelled: false);
        });
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));
        asc.TryActivateAbility(handle);

        world.Tick(0.5f);
        Assert.Equal(1, fired);
        Assert.False(asc.FindAbilitySpec(handle)!.IsActive);
        Assert.Empty(asc.FindAbilitySpec(handle)!.Tasks);
    }

    [Fact]
    public void InstancedPerExecution_Ability_Starts_With_Clean_Tasks_Each_Activation()
    {
        var world = new TestWorld();
        var asc = world.Target;
        var ability = new CountingDelayAbility { InstancingPolicy = GameplayAbilityInstancingPolicy.InstancedPerExecution };
        var handle = asc.GiveAbility(new GameplayAbilitySpec(ability, 1));
        CountingDelayAbility.Fires = 0;

        asc.TryActivateAbility(handle);
        var spec = asc.FindAbilitySpec(handle)!;
        Assert.Single(spec.Tasks);

        world.Tick(1f); // task fires and ends the ability from its callback
        Assert.Equal(1, CountingDelayAbility.Fires);
        Assert.False(spec.IsActive);
        Assert.Empty(spec.Tasks);

        asc.TryActivateAbility(handle); // fresh instance, fresh task
        Assert.Single(spec.Tasks);

        world.Tick(1f);
        Assert.Equal(2, CountingDelayAbility.Fires); // only the new task fired
        Assert.Empty(spec.Tasks);                    // one-shot completed and removed
    }

    // ---------------- Test doubles ----------------

    private static List<string> CaptureWarns()
    {
        var warns = new List<string>();
        _previousWarnHandler = GasNetLog.OnWarn;
        GasNetLog.OnWarn = warns.Add;
        return warns;
    }

    private static void RestoreWarns() => GasNetLog.OnWarn = _previousWarnHandler;

    private static Action<string>? _previousWarnHandler;

    /// <summary>Runs a caller-provided body on the RUNNING instance (clone), not the prototype.</summary>
    private sealed class TaskHostAbility : GameplayAbility
    {
        public Action<GameplayAbility>? Body;
        protected override void ActivateAbility() => Body?.Invoke(this);
    }

    private sealed class EndAfterSpawnAbility : GameplayAbility
    {
        public Action<GameplayAbility>? Body;
        protected override void ActivateAbility()
        {
            Body?.Invoke(this);
            EndAbility(wasCancelled: false);
        }
    }

    /// <summary>Self-rearming 1s delay chain — the Sample's Sprint drain / passive stack pattern.</summary>
    private sealed class ChainingDelayAbility : GameplayAbility
    {
        public static int Fires;
        protected override void ActivateAbility() => ScheduleNext();

        private void ScheduleNext() =>
            AbilityTask_WaitDelay.WaitForDelay(this, 1f, onFire: () =>
            {
                Fires++;
                ScheduleNext();
            });
    }

    private sealed class HoldAbility : GameplayAbility
    {
        public static bool Released;
        protected override void ActivateAbility() =>
            AbilityTask_WaitInputRelease.WaitForInputRelease(this, onRelease: () =>
            {
                Released = true;
                EndAbility(wasCancelled: false);
            });
    }

    private sealed class NonInstancedTaskAbility : GameplayAbility
    {
        public AbilityTask? SpawnResult;

        public NonInstancedTaskAbility() => InstancingPolicy = GameplayAbilityInstancingPolicy.NonInstanced;

        protected override void ActivateAbility()
        {
            SpawnResult = SpawnTask(new DummyTask(this));
            EndAbility(wasCancelled: false);
        }
    }

    private sealed class SpawnOnGrantAbility : GameplayAbility
    {
        public AbilityTask? SpawnResult;

        public override void OnGiveAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec) =>
            SpawnResult = SpawnTask(new DummyTask(this));

        protected override void ActivateAbility() => EndAbility(wasCancelled: false);
    }

    private sealed class CountingDelayAbility : GameplayAbility
    {
        public static int Fires;
        protected override void ActivateAbility() =>
            AbilityTask_WaitDelay.WaitForDelay(this, 1f, onFire: () =>
            {
                Fires++;
                EndAbility(wasCancelled: false);
            });
    }

    /// <summary>Minimal concrete task for hosting-rule tests (built-ins have private ctors + factories).</summary>
    private sealed class DummyTask : AbilityTask
    {
        public DummyTask(GameplayAbility ability) : base(ability) { }
        protected override void OnEnd(AbilityTaskEndReason reason) { }
    }
}
