using System.Collections.Concurrent;
using System.Reflection;

namespace GasNet;

/// <summary>
/// Base/Current value pair for a single attribute — equivalent to UE's <c>FGameplayAttributeData</c>.
/// BaseValue is permanent (changed by Instant GEs); CurrentValue = BaseValue evaluated with all
/// active Duration/Infinite modifiers.
/// </summary>
public struct GameplayAttributeData
{
    public float BaseValue;
    public float CurrentValue;

    public GameplayAttributeData(float baseValue)
    {
        BaseValue = baseValue;
        CurrentValue = baseValue;
    }

    public GameplayAttributeData(float baseValue, float currentValue)
    {
        BaseValue = baseValue;
        CurrentValue = currentValue;
    }
}

/// <summary>
/// Identifies a single attribute: its <see cref="AttributeSet"/> type plus field name.
/// Equivalent to UE's <c>FGameplayAttribute</c> (property name is internally "SetClass.PropName").
/// </summary>
public readonly struct GameplayAttribute : IEquatable<GameplayAttribute>
{
    public Type AttributeSetType { get; }
    public string Name { get; }

    public GameplayAttribute(Type attributeSetType, string name)
    {
        AttributeSetType = attributeSetType;
        Name = name;
    }

    public bool IsValid => AttributeSetType != null && !string.IsNullOrEmpty(Name);

    internal FieldInfo? Field => GameplayAttributeRegistry.TryGetField(this);

    /// <summary>Reads the raw <see cref="GameplayAttributeData"/> from a set instance.</summary>
    public GameplayAttributeData GetData(AttributeSet set)
    {
        var field = GameplayAttributeRegistry.TryGetField(this)
            ?? throw new ArgumentException($"Attribute '{this}' not found on {set.GetType().Name}.");
        return (GameplayAttributeData)field.GetValue(set)!;
    }

    public void SetData(AttributeSet set, GameplayAttributeData data)
    {
        var field = GameplayAttributeRegistry.TryGetField(this)
            ?? throw new ArgumentException($"Attribute '{this}' not found on {set.GetType().Name}.");
        field.SetValue(set, data);
    }

    public bool Equals(GameplayAttribute other) =>
        AttributeSetType == other.AttributeSetType && string.Equals(Name, other.Name, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is GameplayAttribute other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(AttributeSetType, Name, StringComparison.Ordinal);
    public override string ToString() => AttributeSetType == null ? "<invalid>" : $"{AttributeSetType.Name}.{Name}";

    public static bool operator ==(GameplayAttribute a, GameplayAttribute b) => a.Equals(b);
    public static bool operator !=(GameplayAttribute a, GameplayAttribute b) => !a.Equals(b);
}

/// <summary>
/// Reflects <see cref="GameplayAttributeData"/> fields on <see cref="AttributeSet"/> subclasses.
/// This replaces UE's <c>ATTRIBUTE_ACCESSORS</c> macro + UPROPERTY reflection.
/// </summary>
public static class GameplayAttributeRegistry
{
    private static readonly ConcurrentDictionary<Type, Dictionary<string, FieldInfo>> Cache = new();
    private static readonly ConcurrentDictionary<Type, byte> _emptyMapWarned = new();

    public static IReadOnlyList<GameplayAttribute> GetAttributes(Type attributeSetType)
    {
        var fields = GetFieldMap(attributeSetType);
        return fields.Values.Select(f => new GameplayAttribute(attributeSetType, f.Name)).ToArray();
    }

    public static bool TryGetAttribute(Type attributeSetType, string attributeName, out GameplayAttribute attribute)
    {
        if (GetFieldMap(attributeSetType).ContainsKey(attributeName))
        {
            attribute = new GameplayAttribute(attributeSetType, attributeName);
            return true;
        }
        attribute = default;
        return false;
    }

    internal static FieldInfo? TryGetField(GameplayAttribute attribute) =>
        GetFieldMap(attribute.AttributeSetType).TryGetValue(attribute.Name, out var field) ? field : null;

    internal static Dictionary<string, FieldInfo> GetFieldMap(Type attributeSetType)
    {
        return Cache.GetOrAdd(attributeSetType, static type =>
        {
            var map = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            for (var t = type; t != null && typeof(AttributeSet).IsAssignableFrom(t); t = t.BaseType)
            {
                foreach (var field in t.GetFields(flags))
                {
                    if (field.FieldType == typeof(GameplayAttributeData) && !map.ContainsKey(field.Name))
                        map[field.Name] = field;
                }
            }

            if (map.Count == 0 && _emptyMapWarned.TryAdd(type, 0))
            {
                // Fine for a genuinely empty set; otherwise the fields were most likely removed
                // by the AOT linker (Unity IL2CPP managed stripping) — see README "Unity / Godot 接入".
                GasNetLog.Warn(
                    $"AttributeSet '{type.FullName}' exposed no GameplayAttributeData fields via reflection. " +
                    "Either it defines no attributes, or the fields were stripped by the AOT linker (Unity IL2CPP " +
                    "managed stripping). Preserve them with [Preserve]/link.xml or register the fields explicitly.");
            }
            return map;
        });
    }
}
