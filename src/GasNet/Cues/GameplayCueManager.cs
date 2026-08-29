namespace GasNet;

/// <summary>
/// Parameters routed with every cue event (UE: <c>FGameplayCueParameters</c>). Auto-filled from
/// GameplayEffect specs; manual triggers fill what they need.
/// </summary>
public sealed class GameplayCueParameters
{
    public GameplayTagContainer AggregatedSourceTags { get; set; } = new();
    public GameplayTagContainer AggregatedTargetTags { get; set; } = new();
    public GameplayTagContainer AggregatedCueTags { get; set; } = new();

    public object? Instigator { get; set; }
    public object? EffectCauser { get; set; }
    public object? SourceObject { get; set; }
    /// <summary>The target the cue plays on.</summary>
    public object? Target { get; set; }

    public GameplayEffectContext? EffectContext { get; set; }
    public GameplayEffectSpec? EffectSpec { get; set; }

    public float GameplayEffectLevel { get; set; }
    public float AbilityLevel { get; set; }
    public float Magnitude { get; set; }

    public static GameplayCueParameters FromSpec(GameplayEffectSpec spec, AbilitySystemComponent target)
    {
        var context = spec.Context;
        float magnitude = 0f;
        foreach (var kv in spec.SetByCallerMagnitudes)
        {
            magnitude = kv.Value;
            break;
        }
        return new GameplayCueParameters
        {
            AggregatedSourceTags = spec.CapturedSourceTags.Clone(),
            AggregatedTargetTags = spec.CapturedTargetTags.Clone(),
            AggregatedCueTags = spec.GetAllCueTags(),
            Instigator = context?.Instigator,
            EffectCauser = context?.EffectCauser,
            SourceObject = context?.SourceObject,
            Target = target?.AvatarActor ?? target?.OwnerActor,
            EffectContext = context,
            EffectSpec = spec,
            GameplayEffectLevel = spec.Level,
            AbilityLevel = context?.AbilityInstance?.GetAbilityLevel() ?? spec.Level,
            Magnitude = magnitude,
        };
    }
}

/// <summary>
/// A cue handler — equivalent to UE's <c>GameplayCueNotify_Static</c> (shared handler, no per-target
/// state) and <c>GameplayCueNotify_Actor</c> (one instance per (target, tag), created on OnActive and
/// destroyed on Removed). Override the events you need (doc §4.8).
/// </summary>
public abstract class GameplayCueNotify
{
    /// <summary>Tags this notify handles; must live under the "GameplayCue." root.</summary>
    public GameplayTagContainer GameplayCueTags { get; set; } = new();

    /// <summary>Static notifies are shared handlers; actor notifies get one instance per target+tag.</summary>
    public virtual bool IsStatic => true;

    public virtual void OnExecute(GameplayCueParameters parameters) { }
    public virtual void OnActive(GameplayCueParameters parameters) { }
    public virtual void WhileActive(GameplayCueParameters parameters) { }
    public virtual void OnRemove(GameplayCueParameters parameters) { }

    public virtual GameplayCueNotify CreateInstance() => (GameplayCueNotify)MemberwiseClone();
}

/// <summary>Marks a notify as static (one-shot FX executed on a shared handler).</summary>
public abstract class GameplayCueNotify_Static : GameplayCueNotify
{
    public override bool IsStatic => true;
}

/// <summary>Marks a notify as an actor cue (looping/over-time FX with add/remove lifecycle).</summary>
public abstract class GameplayCueNotify_Actor : GameplayCueNotify
{
    public override bool IsStatic => false;
}

/// <summary>
/// Routes cue events to registered notifies — equivalent to UE's <c>UGameplayCueManager</c>.
/// Event tags are matched against registered tags exactly, then up the tag hierarchy.
/// </summary>
public sealed class GameplayCueManager
{
    private readonly List<GameplayCueNotify> _notifies = [];
    private readonly Dictionary<(object target, GameplayTag tag), GameplayCueNotify> _activeActorNotifies = [];

    public static readonly GameplayTag CueRoot = GameplayTag.RequestGameplayTag("GameplayCue");

    public IReadOnlyList<GameplayCueNotify> RegisteredNotifies => _notifies;

    /// <summary>Registers a notify for each of its <see cref="GameplayCueNotify.GameplayCueTags"/>.</summary>
    public void RegisterNotify(GameplayCueNotify notify)
    {
        if (notify.GameplayCueTags.IsEmpty)
            GasNetLog.Warn($"Cue notify '{notify.GetType().Name}' registered without any GameplayCue.* tags.");
        _notifies.Add(notify);
    }

    public bool UnregisterNotify(GameplayCueNotify notify) => _notifies.Remove(notify);

    public void HandleGameplayCue(object? target, GameplayTag cueTag, GameplayCueEvent evt, GameplayCueParameters parameters)
    {
        if (!cueTag.IsValid || !cueTag.MatchesTag(CueRoot))
        {
            GasNetLog.Warn($"GameplayCue tag '{cueTag.Name}' must be under '{CueRoot.Name}.*'. Ignoring.");
            return;
        }
        parameters.Target ??= target;

        var (notify, matchedTag) = FindNotify(cueTag);
        if (notify is null)
            return;

        if (notify.IsStatic)
        {
            InvokeEvent(notify, evt, parameters);
            return;
        }

        // Actor notify: manage one instance per (target, tag).
        var key = (target ?? (object)this, matchedTag);
        switch (evt)
        {
            case GameplayCueEvent.OnActive:
            {
                if (_activeActorNotifies.TryGetValue(key, out var existing))
                {
                    existing.WhileActive(parameters); // re-added while active: just refresh
                    return;
                }
                var instance = notify.CreateInstance();
                _activeActorNotifies[key] = instance;
                instance.OnActive(parameters);
                instance.WhileActive(parameters);
                break;
            }
            case GameplayCueEvent.Removed:
            {
                if (_activeActorNotifies.Remove(key, out var instance))
                    instance.OnRemove(parameters);
                break;
            }
            case GameplayCueEvent.WhileActive:
                if (_activeActorNotifies.TryGetValue(key, out var active))
                    active.WhileActive(parameters);
                break;
            case GameplayCueEvent.Executed:
                if (_activeActorNotifies.TryGetValue(key, out var actor))
                    actor.OnExecute(parameters);
                else
                    notify.CreateInstance().OnExecute(parameters);
                break;
        }
    }

    /// <summary>Destroys every actor-cue instance for <paramref name="target"/>.</summary>
    public void RemoveAllActorCues(object? target)
    {
        if (target is null) return;
        foreach (var key in _activeActorNotifies.Keys.Where(k => ReferenceEquals(k.target, target)).ToArray())
        {
            if (_activeActorNotifies.Remove(key, out var instance))
                instance.OnRemove(new GameplayCueParameters { Target = target });
        }
    }

    private (GameplayCueNotify? notify, GameplayTag matchedTag) FindNotify(GameplayTag cueTag)
    {
        // Exact match first, then walk up the parents (engine behavior for cue tag lookups).
        for (var tag = cueTag; tag.IsValid; tag = tag.RequestDirectParent())
        {
            foreach (var notify in _notifies)
                if (notify.GameplayCueTags.HasTagExact(tag))
                    return (notify, tag);
        }
        return (null, GameplayTag.None);
    }

    private static void InvokeEvent(GameplayCueNotify notify, GameplayCueEvent evt, GameplayCueParameters parameters)
    {
        switch (evt)
        {
            case GameplayCueEvent.OnActive: notify.OnActive(parameters); break;
            case GameplayCueEvent.WhileActive: notify.WhileActive(parameters); break;
            case GameplayCueEvent.Removed: notify.OnRemove(parameters); break;
            case GameplayCueEvent.Executed: notify.OnExecute(parameters); break;
        }
    }
}
