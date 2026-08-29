namespace GasNet;

/// <summary>
/// Container of unique gameplay tags — equivalent to UE's <c>FGameplayTagContainer</c>.
/// <see cref="HasTag"/> uses hierarchical (implied) matching: holding <c>A.B</c> satisfies a query for <c>A</c>.
/// </summary>
public sealed class GameplayTagContainer
{
    private List<GameplayTag> _tags = [];

    public GameplayTagContainer() { }

    public GameplayTagContainer(params GameplayTag[] tags) : this((IEnumerable<GameplayTag>)tags) { }

    public GameplayTagContainer(IEnumerable<GameplayTag> tags)
    {
        foreach (var t in tags)
            AddTag(t);
    }

    public bool IsEmpty => _tags.Count == 0;
    public bool IsNotEmpty => _tags.Count > 0;
    public int Count => _tags.Count;

    public IReadOnlyList<GameplayTag> Tags => _tags;

    public void AddTag(GameplayTag tag)
    {
        if (!tag.IsValid || ContainsTagExact(tag))
            return;
        _tags.Add(tag);
        _tags.Sort(static (a, b) => a.Handle.CompareTo(b.Handle));
    }

    public void AddTags(GameplayTagContainer other)
    {
        if (other is null) return;
        foreach (var t in other._tags)
            AddTag(t);
    }

    public void RemoveTag(GameplayTag tag) => _tags.Remove(tag);

    public void RemoveTags(GameplayTagContainer other)
    {
        if (other is null) return;
        foreach (var t in other._tags)
            _tags.Remove(t);
    }

    public void Clear() => _tags.Clear();

    public bool ContainsTagExact(GameplayTag tag) => _tags.Contains(tag);

    /// <summary>Hierarchical match: true if any contained tag equals <paramref name="tag"/> or is its descendant.</summary>
    public bool HasTag(GameplayTag tag) => _tags.Any(t => t.MatchesTag(tag));

    public bool HasTagExact(GameplayTag tag) => ContainsTagExact(tag);

    public bool HasAny(GameplayTagContainer other) => other != null && other._tags.Any(HasTag);
    public bool HasAnyExact(GameplayTagContainer other) => other != null && other._tags.Any(ContainsTagExact);
    public bool HasAll(GameplayTagContainer other) => other == null || other._tags.All(HasTag);
    public bool HasAllExact(GameplayTagContainer other) => other == null || other._tags.All(ContainsTagExact);

    /// <summary>True when none of <paramref name="other"/>'s tags match (hierarchically).</summary>
    public bool HasNone(GameplayTagContainer other) => other == null || !other._tags.Any(HasTag);

    public bool MatchesAny(GameplayTagContainer other, bool exact = false) => exact ? HasAnyExact(other) : HasAny(other);
    public bool MatchesAll(GameplayTagContainer other, bool exact = false) => exact ? HasAllExact(other) : HasAll(other);

    public GameplayTagContainer Clone() => new(_tags);

    public IEnumerator<GameplayTag> GetEnumerator() => _tags.GetEnumerator();

    public override string ToString() => _tags.Count == 0 ? "(empty)" : string.Join(", ", _tags.Select(t => t.Name));

    /// <summary>Returns the union of this container and <paramref name="other"/> in a new container.</summary>
    public GameplayTagContainer Union(GameplayTagContainer other)
    {
        var result = Clone();
        result.AddTags(other);
        return result;
    }
}

/// <summary>
/// Required = ALL must be present, Ignored (= blocked) = NONE may be present.
/// Equivalent to UE's <c>FGameplayTagRequirements</c>. Used for ability activation requirements,
/// GE application/target requirements and GE ongoing requirements.
/// </summary>
public sealed class GameplayTagRequirements
{
    public GameplayTagContainer RequiredTags { get; } = new();
    public GameplayTagContainer IgnoredTags { get; } = new();

    /// <summary>Optional advanced query evaluated in addition to the tag containers (UE 5.3+).</summary>
    public GameplayTagQuery? TagQuery { get; set; }

    public bool IsEmpty => RequiredTags.IsEmpty && IgnoredTags.IsEmpty && TagQuery is null;

    public bool RequirementsMet(GameplayTagContainer tags) =>
        tags.HasAll(RequiredTags)
        && !tags.HasAny(IgnoredTags)
        && (TagQuery is null || TagQuery.Matches(tags));

    public override string ToString()
    {
        var parts = new List<string>();
        if (RequiredTags.IsNotEmpty) parts.Add("all of [" + RequiredTags + "]");
        if (IgnoredTags.IsNotEmpty) parts.Add("none of [" + IgnoredTags + "]");
        if (TagQuery != null) parts.Add("query");
        return parts.Count == 0 ? "(none)" : string.Join(" && ", parts);
    }
}

/// <summary>
/// Simplified port of UE's <c>FGameplayTagQuery</c>: an expression tree over tag containers.
/// Mirrors the doc note that an empty query does NOT match.
/// </summary>
public sealed class GameplayTagQuery
{
    private abstract class Node
    {
        public abstract bool Eval(GameplayTagContainer tags);
    }

    private sealed class AllNode(List<Node> children) : Node
    {
        public override bool Eval(GameplayTagContainer tags) => children.All(c => c.Eval(tags));
    }

    private sealed class AnyNode(List<Node> children) : Node
    {
        public override bool Eval(GameplayTagContainer tags) => children.Any(c => c.Eval(tags));
    }

    private sealed class NoNode(List<Node> children) : Node
    {
        public override bool Eval(GameplayTagContainer tags) => !children.Any(c => c.Eval(tags));
    }

    private sealed class TagNode(GameplayTagContainer tags, bool all, bool exact) : Node
    {
        public override bool Eval(GameplayTagContainer tags2) =>
            all ? tags2.MatchesAll(tags, exact) : tags2.MatchesAny(tags, exact);
    }

    private readonly Node? _root;

    private GameplayTagQuery(Node root) => _root = root;

    private GameplayTagQuery() => _root = null;

    /// <summary>An empty query, which never matches (UE 5.3+ semantics).</summary>
    public static GameplayTagQuery Empty { get; } = new();

    /// <summary>An empty query never matches (matches UE 5.3+ behavior).</summary>
    public bool Matches(GameplayTagContainer tags) => _root != null && _root.Eval(tags);

    public static GameplayTagQuery AllTags(GameplayTagContainer tags, bool exact = false) => new(new AllNode([new TagNode(tags, all: true, exact)]));
    public static GameplayTagQuery AnyTags(GameplayTagContainer tags, bool exact = false) => new(new AnyNode([new TagNode(tags, all: false, exact)]));
    public static GameplayTagQuery NoTags(GameplayTagContainer tags, bool exact = false) => new(new NoNode([new TagNode(tags, all: false, exact)]));

    public static GameplayTagQuery All(params GameplayTagQuery[] subQueries) => new(new AllNode(subQueries.Select(q => q._root!).ToList()));
    public static GameplayTagQuery Any(params GameplayTagQuery[] subQueries) => new(new AnyNode(subQueries.Select(q => q._root!).ToList()));
    public static GameplayTagQuery NoMatch(params GameplayTagQuery[] subQueries) => new(new NoNode(subQueries.Select(q => q._root!).ToList()));
}
