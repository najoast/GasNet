using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GasNet;

namespace GasNet.Data;

/// <summary>
/// Loads <see cref="CueDefinition"/>s from a JSON catalog — the data-driven stand-in for hand-written
/// <c>GameplayCueNotify</c> subclasses. All JSON plumbing lives in this assembly so the core stays
/// dependency-free; engine-specific <see cref="CueAction"/> subclasses and their
/// <see cref="CueActionSchema"/> are registered through <see cref="GasNetDataCueLoadOptions"/>.
///
/// <para>Catalog format: <code>
/// { "cues": {
///     "Hit": {
///       "tag": "GameplayCue.Combat.Hit",     // must live under "GameplayCue."
///       "isStatic": true,                      // default: true (one-shot per Execute)
///       "actions": [
///         { "type": "FlashCueAction", "triggerOn": "OnExecute",
///           "durationMs": 150, "color": "#FFFFFF" },
///         { "type": "LogCueAction", "triggerOn": "OnExecute",
///           "format": "{target} hit! {magnitude:0.#} HP" }
///       ]
///     } } }
/// </code></para>
///
/// <para>Per-action extra fields are read by the <see cref="CueActionSchema"/> the host registered
/// for the type — the loader itself only knows <c>type</c>, <c>triggerOn</c>, and that the actions
/// array must be non-empty. Unknown <c>type</c>s are rejected with the registered type list;
/// unknown cue names / unknown fields are rejected the same way as the GE loader (typos must be loud).
/// Tag names are auto-registered (the library's runtime tag model); tags outside the
/// <c>GameplayCue.</c> root are warned about but still loaded.</para>
/// </summary>
public static class GasNetDataCueLoader
{
    public static GasNetDataCueCatalog LoadCueFile(string path, GasNetDataCueLoadOptions? options = null) =>
        LoadCues(File.ReadAllText(path), options);

    public static GasNetDataCueCatalog LoadCues(string json, GasNetDataCueLoadOptions? options = null)
    {
        options ??= new GasNetDataCueLoadOptions();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new GasNetDataException($"Cue JSON parse error: {e.Message}", e);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new GasNetDataException("Cue catalog root must be a JSON object.");
            RejectUnknownFields("Cue catalog root", root, "cues");

            if (!TryTake(root, "cues", out var cues) || cues.ValueKind != JsonValueKind.Object)
                throw new GasNetDataException("Cue catalog root must contain a 'cues' object.");

            var result = new Dictionary<string, CueDefinition>(StringComparer.Ordinal);
            foreach (var cue in cues.EnumerateObject())
            {
                // Mirrors the GE loader: JSON technically allows repeated object keys; the dictionary
                // would silently take the last one. The catalog must be strict.
                if (result.ContainsKey(cue.Name))
                    throw new GasNetDataException(
                        $"Duplicate cue name '{cue.Name}' in the catalog. " +
                        "JSON objects must not repeat keys; the last occurrence would silently win.");
                if (cue.Value.ValueKind != JsonValueKind.Object)
                    throw new GasNetDataException($"Cue '{cue.Name}': expected an object.");
                result[cue.Name] = ParseCue(cue.Name, cue.Value, options);
            }
            return new GasNetDataCueCatalog(result);
        }
    }

    // ------------------------------------------------------------------
    // Cue
    // ------------------------------------------------------------------

    private static readonly string[] CueFields = ["tag", "isStatic", "actions"];

    private static CueDefinition ParseCue(string name, JsonElement el, GasNetDataCueLoadOptions o)
    {
        var ctx = $"Cue '{name}'";
        RejectUnknownFields(ctx, el, CueFields);

        var def = new CueDefinition { Name = name };
        if (!TryTake(el, "tag", out var tagEl))
            throw new GasNetDataException($"{ctx}: needs a 'tag' (under the 'GameplayCue.' root).");
        var tagName = GetString($"{ctx}.tag", tagEl);
        if (!tagName.StartsWith("GameplayCue.", StringComparison.Ordinal))
            GasNetLog.Warn($"GasNet.Data: cue '{name}' tag '{tagName}' is not under the 'GameplayCue.' root; " +
                           "the GameplayCueManager will ignore it at runtime.");
        def.Tag = GameplayTag.RequestGameplayTag(tagName, warnIfNotFound: false);

        def.IsStatic = !TryTake(el, "isStatic", out var stEl) || GetBool($"{ctx}.isStatic", stEl); // default true (Static = the natural hit shape)

        if (!TryTake(el, "actions", out var actionsEl) || actionsEl.ValueKind != JsonValueKind.Array)
            throw new GasNetDataException($"{ctx}: needs an 'actions' array (at least one).");

        int i = 0;
        foreach (var item in ExpectArray($"{ctx}.actions", actionsEl))
        {
            def.Actions.Add(ParseAction($"{ctx}.actions[{i++}]", item, o, def.IsStatic));
        }
        if (def.Actions.Count == 0)
            throw new GasNetDataException($"{ctx}: 'actions' must contain at least one entry.");
        return def;
    }

    // ------------------------------------------------------------------
    // Action
    // ------------------------------------------------------------------

    private static CueAction ParseAction(string ctx, JsonElement el, GasNetDataCueLoadOptions o, bool cueIsStatic)
    {
        if (el.ValueKind != JsonValueKind.Object)
            throw new GasNetDataException($"{ctx}: expected an object.");

        // The schema may know additional fields; collect them as "extra" so we can hand the
        // (post-consumption) element to the schema's reader without re-checking. The base loader
        // owns only 'type' and 'triggerOn' — anything else is the schema's responsibility to either
        // read or reject. (Schemas that want strict validation do it themselves; the
        // action-level unknown-field check is the same one CueEditorView will do at edit time.)
        if (!TryTake(el, "type", out var typeEl))
            throw new GasNetDataException($"{ctx}: actions need a 'type' (a registered CueAction subclass). " +
                $"Registered types: [{string.Join(", ", o.ActionTypes.Keys)}].");
        var typeName = GetString($"{ctx}.type", typeEl);

        if (!o.ActionTypes.TryGetValue(typeName, out var type))
            throw new GasNetDataException(
                $"{ctx}: cue action type '{typeName}' is not registered on the load options. " +
                $"Registered types: [{string.Join(", ", o.ActionTypes.Keys)}].");

        CueAction action;
        try
        {
            action = (CueAction)Activator.CreateInstance(type)!;
        }
        catch (Exception e)
        {
            throw new GasNetDataException($"{ctx}: failed to construct '{type.FullName}': {e.Message}", e);
        }

        var defaultTrigger = cueIsStatic ? GameplayCueEvent.Executed : GameplayCueEvent.OnActive;
        action.TriggerOn = TryTake(el, "triggerOn", out var trEl)
            ? ParseEnum<GameplayCueEvent>($"{ctx}.triggerOn", trEl)
            : defaultTrigger;

        if (o.GetSchema(typeName) is { } schema)
        {
            try
            {
                schema.Reader(action, el);
            }
            catch (GasNetDataException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new GasNetDataException(
                    $"{ctx}: action type '{typeName}' schema failed: {e.Message}", e);
            }
        }
        return action;
    }

    // ------------------------------------------------------------------
    // JSON plumbing
    // ------------------------------------------------------------------

    private static void RejectUnknownFields(string ctx, JsonElement el, params string[] known)
    {
        if (el.ValueKind != JsonValueKind.Object)
            throw new GasNetDataException($"{ctx}: expected an object.");
        foreach (var property in el.EnumerateObject())
        {
            if (!known.Contains(property.Name))
                throw new GasNetDataException(
                    $"{ctx}: unknown field '{property.Name}'. Valid fields: {string.Join(", ", known)}.");
        }
    }

    private static IEnumerable<JsonElement> ExpectArray(string ctx, JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array)
            throw new GasNetDataException($"{ctx}: expected an array.");
        return el.EnumerateArray();
    }

    private static bool TryTake(JsonElement obj, string field, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(field, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.Null)
            return false;
        value = el;
        return true;
    }

    private static string GetString(string ctx, JsonElement el) => el.ValueKind == JsonValueKind.String
        ? el.GetString()!
        : throw new GasNetDataException($"{ctx}: expected a string, got {el.ValueKind}.");

    private static bool GetBool(string ctx, JsonElement el) => el.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? el.GetBoolean()
        : throw new GasNetDataException($"{ctx}: expected true or false, got {el.ValueKind}.");

    private static TEnum ParseEnum<TEnum>(string ctx, JsonElement el) where TEnum : struct, Enum
    {
        var text = el.ValueKind == JsonValueKind.String ? el.GetString() : null;
        if (text is not null && Enum.TryParse(text, ignoreCase: true, out TEnum value))
            return value;
        throw new GasNetDataException(
            $"{ctx}: '{el.GetRawText()}' is not a valid {typeof(TEnum).Name}. Valid: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}.");
    }
}
