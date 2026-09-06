using System;
using System.Collections.Generic;
using GasNet.Data;

namespace GasNet;

/// <summary>
/// Bridges data-driven <see cref="CueDefinition"/>s into a live <see cref="GameplayCueManager"/>:
/// turns each definition into a <see cref="GameplayCueNotify"/> (Static or Actor per the
/// definition's <see cref="CueDefinition.IsStatic"/>) and registers it.
///
/// <para>Lives in <c>GasNet.Data</c> so the core library stays free of a back-reference to the
/// data assembly — the runtime is a one-way adapter. Engine hosts (Unity, Godot) call
/// <see cref="RegisterAll"/> once during startup after loading a <see cref="GasNetDataCueCatalog"/>.</para>
///
/// <para>Action exceptions are isolated: one broken action does not abort the rest of the cue
/// event. Exceptions are forwarded to <c>GasNetLog.OnError</c>.</para>
/// </summary>
public sealed class GameplayCueRuntime
{
    private readonly GameplayCueManager _manager;
    private readonly GasNetDataCueLoadOptions _options;
    private readonly List<GameplayCueNotify> _registered = [];

    public GameplayCueRuntime(GameplayCueManager manager, GasNetDataCueLoadOptions options)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The notifies built by <see cref="RegisterAll"/>, in registration order. Held so a
    /// future teardown method can unregister them; the current demo doesn't need this yet.</summary>
    public IReadOnlyList<GameplayCueNotify> RegisteredNotifies => _registered;

    /// <summary>Builds and registers a <see cref="GameplayCueNotify"/> for every cue in
    /// <paramref name="catalog"/>. Returns the same count as <see cref="RegisteredNotifies"/>.</summary>
    public int RegisterAll(GasNetDataCueCatalog catalog)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        foreach (var cue in catalog.Cues.Values)
        {
            var notify = BuildNotify(cue);
            _manager.RegisterNotify(notify);
            _registered.Add(notify);
        }
        return _registered.Count;
    }

    private GameplayCueNotify BuildNotify(CueDefinition cue)
    {
        if (!cue.Tag.IsValid)
            throw new GasNetDataException($"Cue runtime: cue '{cue.Name}' has an invalid tag.");
        if (!cue.Tag.MatchesTag(GameplayCueManager.CueRoot))
            throw new GasNetDataException(
                $"Cue runtime: cue '{cue.Name}' tag '{cue.Tag.Name}' is not under the '{GameplayCueManager.CueRoot.Name}.' root; " +
                "the GameplayCueManager will ignore it at dispatch time.");
        if (cue.Actions.Count == 0)
            throw new GasNetDataException(
                $"Cue runtime: cue '{cue.Name}' has no actions (the loader rejects empty lists, so this is a coding error).");

        return cue.IsStatic
            ? new StaticCueNotify(cue, _options, this)
            : new ActorCueNotify(cue, _options, this);
    }

    // ------------------------------------------------------------------
    // Dispatch
    // ------------------------------------------------------------------

    private void Dispatch(GameplayCueParameters parameters, GameplayCueEvent evt, IReadOnlyList<CueAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action.TriggerOn != evt) continue;
            try
            {
                action.Execute(parameters);
            }
            catch (Exception e)
            {
                GasNetLog.OnError?.Invoke(
                    $"Cue runtime: action '{action.GetType().Name}' (TriggerOn={action.TriggerOn}) threw: {e.Message}");
            }
        }
    }

    // ------------------------------------------------------------------
    // Generated notify classes
    // ------------------------------------------------------------------

    private sealed class StaticCueNotify : GameplayCueNotify_Static
    {
        private readonly CueDefinition _cue;
        private readonly GameplayCueRuntime _runtime;

        public StaticCueNotify(CueDefinition cue, GasNetDataCueLoadOptions options, GameplayCueRuntime runtime) : base()
        {
            // Suppress the empty-tag warning — we always have a valid tag here, validated by the loader.
            GameplayCueTags.AddTag(cue.Tag);
            _cue = cue;
            _runtime = runtime;
        }

        public override void OnExecute(GameplayCueParameters parameters) =>
            _runtime.Dispatch(parameters, GameplayCueEvent.Executed, _cue.Actions);
    }

    private sealed class ActorCueNotify : GameplayCueNotify_Actor
    {
        private readonly CueDefinition _cue;
        private readonly GameplayCueRuntime _runtime;

        public ActorCueNotify(CueDefinition cue, GasNetDataCueLoadOptions options, GameplayCueRuntime runtime) : base()
        {
            GameplayCueTags.AddTag(cue.Tag);
            _cue = cue;
            _runtime = runtime;
        }

        public override void OnActive(GameplayCueParameters parameters) =>
            _runtime.Dispatch(parameters, GameplayCueEvent.OnActive, _cue.Actions);

        public override void WhileActive(GameplayCueParameters parameters) =>
            _runtime.Dispatch(parameters, GameplayCueEvent.WhileActive, _cue.Actions);

        public override void OnRemove(GameplayCueParameters parameters) =>
            _runtime.Dispatch(parameters, GameplayCueEvent.Removed, _cue.Actions);

        public override void OnExecute(GameplayCueParameters parameters) =>
            _runtime.Dispatch(parameters, GameplayCueEvent.Executed, _cue.Actions);
    }
}
