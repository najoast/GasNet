using System;
using System.Collections.Generic;
using GasNet;

namespace GasNet.Data;

/// <summary>
/// Registries used while resolving <see cref="CueAction"/> references inside a cue catalog. The data
/// format only stores type names — the host supplies the actual <see cref="CueAction"/> subclasses,
/// and (optionally) a <see cref="CueActionSchema"/> that describes the JSON fields each action
/// carries. Parallel to <see cref="GasNetDataLoadOptions"/>, kept separate so the cue catalog can be
/// loaded independently of the GE catalog.
/// </summary>
public sealed class GasNetDataCueLoadOptions
{
    private readonly Dictionary<string, Type> _actionTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CueActionSchema> _actionSchemas = new(StringComparer.Ordinal);

    /// <summary>Registered <see cref="CueAction"/> subclasses, keyed by their simple type name
    /// (and full name, when no explicit alias is given).</summary>
    public IReadOnlyDictionary<string, Type> ActionTypes => _actionTypes;

    /// <summary>Registers a <see cref="CueAction"/> subclass. The JSON <c>"type"</c> field of an
    /// action entry must match one of the registered names. Without a schema, the action
    /// round-trips with no extra fields (just <c>type</c> + <c>triggerOn</c>).</summary>
    public GasNetDataCueLoadOptions RegisterActionType<T>(string? name = null) where T : CueAction =>
        RegisterActionType(typeof(T), null, name);

    public GasNetDataCueLoadOptions RegisterActionType<T>(CueActionSchema schema, string? name = null) where T : CueAction =>
        RegisterActionType(typeof(T), schema, name);

    public GasNetDataCueLoadOptions RegisterActionType(Type type, string? name = null) =>
        RegisterActionType(type, null, name);

    public GasNetDataCueLoadOptions RegisterActionType(Type type, CueActionSchema? schema, string? name = null)
    {
        if (!typeof(CueAction).IsAssignableFrom(type))
            throw new ArgumentException(
                $"Type '{type.FullName}' is not a CueAction subclass.", nameof(type));
        if (type.IsAbstract || type.IsInterface)
            throw new ArgumentException(
                $"Type '{type.FullName}' is abstract; only concrete CueAction subclasses can be registered.", nameof(type));

        var key = name ?? type.Name;
        _actionTypes[key] = type;
        if (schema is not null) _actionSchemas[key] = schema;
        if (name is null && type.FullName is { } fullName)
        {
            _actionTypes.TryAdd(fullName, type);
            if (schema is not null) _actionSchemas.TryAdd(fullName, schema);
        }
        return this;
    }

    internal CueActionSchema? GetSchema(string key) =>
        _actionSchemas.TryGetValue(key, out var s) ? s : null;
}
