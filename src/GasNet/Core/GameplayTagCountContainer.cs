namespace GasNet;

/// <summary>How a tag event handler is invoked (UE: <c>EGameplayTagEventType</c>).</summary>
public enum GameplayTagEventType
{
    /// <summary>Fires only when the tag's count transitions between 0 and non-zero.</summary>
    NewOrRemoved,
    /// <summary>Fires on every change of the tag's count.</summary>
    AnyCountChange,
}

/// <summary>Subscription handle returned by <see cref="GameplayTagCountContainer.RegisterGameplayTagEvent"/>.</summary>
public sealed class GameplayTagEventRegistration : IDisposable
{
    private GameplayTagCountContainer? _container;
    private readonly GameplayTag _tag;
    private readonly GameplayTagEventType _eventType;
    private readonly Action<GameplayTag, int> _handler;

    internal GameplayTagEventRegistration(GameplayTagCountContainer container, GameplayTag tag,
        GameplayTagEventType eventType, Action<GameplayTag, int> handler)
    {
        _container = container;
        _tag = tag;
        _eventType = eventType;
        _handler = handler;
    }

    public void Dispose()
    {
        if (_container is { } container)
        {
            container.UnregisterGameplayTagEvent(_tag, _eventType, _handler);
            _container = null;
        }
    }
}

/// <summary>
/// Count-based storage of gameplay tags on an object — equivalent to UE's
/// <c>FGameplayTagCountContainer</c>. A tag "exists" only while its count is &gt; 0.
/// Both GE-granted tags and loose (non-replicated, manually managed) tags share this map.
/// </summary>
public sealed class GameplayTagCountContainer
{
    private readonly Dictionary<GameplayTag, int> _tagCounts = [];
    private readonly Dictionary<GameplayTag, List<(GameplayTagEventType type, Action<GameplayTag, int> handler)>> _events = [];

    /// <summary>Fires after any tag's count changed (any tag, any change).</summary>
    public event Action<GameplayTag, int>? AnyTagCountChanged;

    public int GetTagCount(GameplayTag tag) => _tagCounts.TryGetValue(tag, out int count) ? count : 0;

    /// <summary>Hierarchical check: the tag is present with count &gt; 0, or a descendant of it is.</summary>
    public bool HasTag(GameplayTag tag) => _tagCounts.Any(kv => kv.Value > 0 && kv.Key.MatchesTag(tag));

    public bool HasTagExact(GameplayTag tag) => GetTagCount(tag) > 0;

    public bool HasAnyTags(GameplayTagContainer tags) => tags != null && tags.Tags.Any(HasTag);
    public bool HasAllTags(GameplayTagContainer tags) => tags == null || tags.Tags.All(HasTag);
    public bool HasNoTags(GameplayTagContainer tags) => tags == null || !tags.Tags.Any(HasTag);

    public bool RequirementsMet(GameplayTagRequirements requirements) =>
        requirements.IsEmpty || requirements.RequirementsMet(GetAggregatedTags());

    /// <summary>Snapshot of all present tags as a container.</summary>
    public GameplayTagContainer GetAggregatedTags()
    {
        var result = new GameplayTagContainer();
        foreach (var kv in _tagCounts)
            if (kv.Value > 0)
                result.AddTag(kv.Key);
        return result;
    }

    public void AddTag(GameplayTag tag, int count = 1)
    {
        if (!tag.IsValid || count <= 0)
            return;
        SetTagCount(tag, GetTagCount(tag) + count);
    }

    public void AddTags(GameplayTagContainer tags, int count = 1)
    {
        if (tags is null) return;
        foreach (var t in tags.Tags)
            AddTag(t, count);
    }

    /// <summary>Removes <paramref name="count"/> instances; clamps at zero. Fires removal events when count hits 0.</summary>
    public void RemoveTag(GameplayTag tag, int count = 1)
    {
        if (!tag.IsValid || count <= 0)
            return;
        int current = GetTagCount(tag);
        SetTagCount(tag, Math.Max(0, current - count));
    }

    public void RemoveTags(GameplayTagContainer tags, int count = 1)
    {
        if (tags is null) return;
        foreach (var t in tags.Tags)
            RemoveTag(t, count);
    }

    public void SetTagCount(GameplayTag tag, int newCount)
    {
        if (!tag.IsValid)
            return;

        int old = GetTagCount(tag);
        newCount = Math.Max(0, newCount);
        if (newCount == old)
            return;

        if (newCount == 0)
            _tagCounts.Remove(tag);
        else
            _tagCounts[tag] = newCount;

        bool transition = (old == 0) != (newCount == 0);
        if (_events.TryGetValue(tag, out var handlers))
        {
            foreach (var (type, handler) in handlers)
                if (type == GameplayTagEventType.AnyCountChange || transition)
                    handler(tag, newCount);
        }
        AnyTagCountChanged?.Invoke(tag, newCount);
    }

    public GameplayTagEventRegistration RegisterGameplayTagEvent(GameplayTag tag, GameplayTagEventType eventType,
        Action<GameplayTag, int> handler)
    {
        if (!_events.TryGetValue(tag, out var list))
        {
            list = [];
            _events[tag] = list;
        }
        list.Add((eventType, handler));
        return new GameplayTagEventRegistration(this, tag, eventType, handler);
    }

    public void UnregisterGameplayTagEvent(GameplayTag tag, GameplayTagEventType eventType, Action<GameplayTag, int> handler)
    {
        if (_events.TryGetValue(tag, out var list))
            list.RemoveAll(e => e.type == eventType && e.handler == handler);
    }
}
