using System;
using System.Collections.Generic;

namespace GasNet.Data;

/// <summary>
/// Registries used while resolving references inside a data catalog. The data format only stores
/// names — the host supplies the actual types, which keeps the loader friendly to AOT/trimming
/// (nothing is discovered by scanning assemblies).
/// </summary>
public sealed class GasNetDataLoadOptions
{
    private readonly Dictionary<string, Type> _attributeSets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal);

    /// <summary>Attribute set types referenced as <c>"SetTypeName.AttributeName"</c> in JSON.</summary>
    public IReadOnlyDictionary<string, Type> AttributeSets => _attributeSets;

    /// <summary>Code-backed pieces referenced by name: execution calculations, MMCs, custom
    /// application requirements and granted ability classes.</summary>
    public IReadOnlyDictionary<string, Type> Types => _types;

    /// <summary>Registers an attribute set under its simple name and full name.</summary>
    public GasNetDataLoadOptions RegisterAttributeSet<T>() where T : AttributeSet
    {
        RegisterNamed(_attributeSets, typeof(T), name: null);
        return this;
    }

    /// <summary>Registers a type under <paramref name="name"/> (default: its simple name, full name
    /// also accepted when no explicit name is given).</summary>
    public GasNetDataLoadOptions RegisterType<T>(string? name = null) => RegisterType(typeof(T), name);

    public GasNetDataLoadOptions RegisterType(Type type, string? name = null)
    {
        RegisterNamed(_types, type, name);
        return this;
    }

    private static void RegisterNamed(Dictionary<string, Type> map, Type type, string? name)
    {
        map[name ?? type.Name] = type;
        if (name is null && type.FullName is { } fullName)
            map.TryAdd(fullName, type);
    }
}
