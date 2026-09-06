using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using GasNet;

namespace GasNet.Data;

/// <summary>
/// Per-action-type read/write hooks. Lets a host project (Unity, Godot, or a custom tool) attach a
/// schema to a <see cref="CueAction"/> subclass so the loader can parse its JSON fields and the
/// writer can emit them. Built-in actions (flash/shake/log/vfx/animation/audio) are not registered
/// here — the engine demos do that explicitly, with schemas that know the action's fields.
///
/// <para>The default schema (registered when no <c>Configure</c> delegate is provided) writes
/// <c>{ "type": "X", "triggerOn": "..." }</c> with no extra fields. Custom actions only need a
/// schema when they carry JSON-described parameters.</para>
/// </summary>
public sealed class CueActionSchema
{
    /// <summary>Populates the action instance from a JSON element. The element has already been
    /// type-validated and the <c>type</c>/<c>triggerOn</c> fields have been consumed by the loader;
    /// implementations read the remaining fields and throw <see cref="GasNetDataException"/> on
    /// problems (the loader wraps it with the action's JSON path context).</summary>
    public Action<CueAction, JsonElement> Reader { get; set; } = (_, _) => { };

    /// <summary>Adds the action's extra fields to a JSON object. The <c>type</c> and
    /// <c>triggerOn</c> fields are written by the loader; the schema only adds what it knows about.
    /// Omit defaults to keep diffs small (mirror the GE writer's "fields equal to their default are
    /// omitted" rule).</summary>
    public Action<CueAction, JsonObject> Writer { get; set; } = (_, _) => { };
}
