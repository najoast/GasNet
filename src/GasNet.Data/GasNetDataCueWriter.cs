using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using GasNet;

namespace GasNet.Data;

/// <summary>
/// The write side of the cue data layer: turns a loaded/edited <see cref="CueDefinition"/> set back
/// into the catalog JSON format documented on <see cref="GasNetDataCueLoader"/>. The Blazor and
/// Unity editors never serialize cues on their own — read and write both live here so the format
/// stays single-sourced, mirroring <see cref="GasNetDataWriter"/> for effects.
///
/// <para>Writing rules: <c>isStatic</c> = true is the engine default and is omitted on write
/// (Static is the natural choice for hit-style one-shots — see
/// <see cref="GameplayCueNotify.IsStatic"/>); <c>triggerOn</c> equal to the per-cue-type default is
/// omitted (Static → <c>Executed</c>; Actor → <c>OnActive</c>); per-action extra fields are
/// produced by the <see cref="CueActionSchema"/> the host registered, so the data layer can stay
/// engine-agnostic. Action order is preserved as-is.</para>
/// </summary>
public static class GasNetDataCueWriter
{
    /// <summary>Serializes a cue catalog. <paramref name="cues"/> must have unique keys.</summary>
    public static string WriteCatalog(IEnumerable<KeyValuePair<string, CueDefinition>> cues, GasNetDataCueLoadOptions? options = null)
    {
        options ??= new GasNetDataCueLoadOptions();
        var cueNodes = new JsonObject();
        foreach (var (name, def) in cues)
        {
            if (def is null)
                throw new GasNetDataException($"Cue writer: cue '{name}' is null.");
            if (def.Actions is null || def.Actions.Count == 0)
                throw new GasNetDataException(
                    $"Cue writer: cue '{name}' has no actions; the loader rejects empty action lists, so a loaded definition should never reach this branch.");
            cueNodes[name] = WriteCue(def, options);
        }
        return new JsonObject { ["cues"] = cueNodes }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject WriteCue(CueDefinition def, GasNetDataCueLoadOptions options)
    {
        var node = new JsonObject();
        if (!def.Tag.IsValid)
            throw new GasNetDataException($"Cue writer: cue '{def.Name}' has an invalid tag.");
        node["tag"] = def.Tag.Name;
        if (!def.IsStatic) node["isStatic"] = false; // true is the default; omit.

        var actions = new JsonArray();
        var defaultTrigger = def.IsStatic ? GameplayCueEvent.Executed : GameplayCueEvent.OnActive;
        foreach (var action in def.Actions)
        {
            if (action is null)
                throw new GasNetDataException($"Cue writer: cue '{def.Name}' has a null action.");
            actions.Add(WriteAction(def.Name, action, defaultTrigger, options));
        }
        node["actions"] = actions;
        return node;
    }

    private static JsonObject WriteAction(string cueName, CueAction action, GameplayCueEvent defaultTrigger, GasNetDataCueLoadOptions options)
    {
        var typeName = action.GetType().Name;
        var node = new JsonObject { ["type"] = typeName };
        if (action.TriggerOn != defaultTrigger)
            node["triggerOn"] = action.TriggerOn.ToString();

        if (options.GetSchema(typeName) is { } schema)
        {
            try
            {
                schema.Writer(action, node);
            }
            catch (GasNetDataException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new GasNetDataException(
                    $"Cue writer: cue '{cueName}' action '{typeName}' schema failed: {e.Message}", e);
            }
        }
        return node;
    }
}
