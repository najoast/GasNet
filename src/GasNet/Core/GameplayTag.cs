namespace GasNet;

/// <summary>
/// Global registry of gameplay tags — the C# equivalent of UE's <c>UGameplayTagsManager</c>
/// plus <c>DefaultGameplayTags.ini</c>. Tags are registered by hierarchical name
/// ("Parent.Child.Grandchild") and referenced by lightweight handles.
/// </summary>
public sealed class GameplayTagsManager
{
    public static GameplayTagsManager Default { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<string, GameplayTag> _tagByName = new(StringComparer.Ordinal);
    private readonly List<string> _names = ["<None>"]; // handle 0 = None

    /// <summary>Registers (or returns the already-registered) tag for <paramref name="name"/>.</summary>
    public GameplayTag RequestGameplayTag(string name, bool warnIfNotFound = true, bool registerIfNotFound = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            return GameplayTag.None;

        lock (_lock)
        {
            if (_tagByName.TryGetValue(name, out var existing))
                return existing;

            if (!registerIfNotFound)
            {
                if (warnIfNotFound)
                    GasNetLog.Warn($"GameplayTag '{name}' is not registered.");
                return GameplayTag.None;
            }

            ValidateName(name);
            var tag = new GameplayTag(_names.Count);
            _names.Add(name);
            _tagByName.Add(name, tag);
            return tag;
        }
    }

    public string GetTagName(GameplayTag tag) => tag.Handle > 0 ? _names[tag.Handle] : string.Empty;

    /// <summary>All registered tags (excluding None), in registration order.</summary>
    public IReadOnlyList<GameplayTag> GetAllTags()
    {
        lock (_lock)
        {
            var result = new List<GameplayTag>(_names.Count - 1);
            for (int i = 1; i < _names.Count; i++)
                result.Add(new GameplayTag(i));
            return result;
        }
    }

    private static void ValidateName(string name)
    {
        var segments = name.Split('.');
        if (segments.Any(s => s.Length == 0))
            throw new ArgumentException($"Invalid gameplay tag name '{name}': empty segments are not allowed.");
    }
}

/// <summary>
/// A hierarchical gameplay tag ("State.Debuff.Stun"). A tag implies all of its ancestors:
/// a container holding <c>A.B.C</c> answers <c>HasTag(A.B)</c> with true.
/// Struct equal to UE's <c>FGameplayTag</c>.
/// </summary>
public readonly struct GameplayTag : IEquatable<GameplayTag>
{
    internal int Handle { get; }

    internal GameplayTag(int handle) => Handle = handle;

    public static GameplayTag None => default;

    public bool IsValid => Handle != 0;

    public string Name => Handle != 0 ? GameplayTagsManager.Default.GetTagName(this) : string.Empty;

    public static GameplayTag RequestGameplayTag(string name, bool warnIfNotFound = true) =>
        GameplayTagsManager.Default.RequestGameplayTag(name, warnIfNotFound);

    /// <summary>True if this tag equals <paramref name="other"/> or is a strict descendant of it.</summary>
    public bool MatchesTag(GameplayTag other, bool exact = false)
    {
        if (Handle == other.Handle)
            return true;
        return !exact && IsDescendantOf(other);
    }

    /// <summary>True if this tag is a strict descendant of <paramref name="ancestor"/>.</summary>
    public bool IsDescendantOf(GameplayTag ancestor)
    {
        if (Handle == 0 || ancestor.Handle == 0 || Handle == ancestor.Handle)
            return false;
        var myName = Name;
        var ancestorName = ancestor.Name;
        return myName.Length > ancestorName.Length
            && myName.StartsWith(ancestorName, StringComparison.Ordinal)
            && myName[ancestorName.Length] == '.';
    }

    public GameplayTag RequestDirectParent()
    {
        var name = Name;
        int dot = name.LastIndexOf('.');
        return dot < 0 ? GameplayTag.None : RequestGameplayTag(name[..dot], warnIfNotFound: false);
    }

    public bool Equals(GameplayTag other) => Handle == other.Handle;
    public override bool Equals(object? obj) => obj is GameplayTag other && Equals(other);
    public override int GetHashCode() => Handle;
    public override string ToString() => Name;

    public static bool operator ==(GameplayTag a, GameplayTag b) => a.Handle == b.Handle;
    public static bool operator !=(GameplayTag a, GameplayTag b) => a.Handle != b.Handle;
}

/// <summary>Central warning/error sink so the library stays UI-free.</summary>
public static class GasNetLog
{
    public static Action<string>? OnWarn { get; set; } = static msg => Console.Error.WriteLine("[GasNet][Warn] " + msg);
    public static Action<string>? OnError { get; set; } = static msg => Console.Error.WriteLine("[GasNet][Error] " + msg);

    public static void Warn(string message) => OnWarn?.Invoke(message);
    public static void Error(string message) => OnError?.Invoke(message);
}
