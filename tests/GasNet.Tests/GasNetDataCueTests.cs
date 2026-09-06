using System.Text.Json;
using System.Text.Json.Nodes;
using GasNet;
using GasNet.Data;
using Xunit;

namespace GasNet.Tests;

/// <summary>
/// Behavior-as-documentation for the cue data layer: what the catalog format loads into, and that
/// load-time errors are loud and point at the offending field. Round-trip identity (load → write →
/// load → write) is the canonical stability test, mirroring <see cref="GasNetDataWriterTests"/>.
/// </summary>
public class GasNetDataCueTests
{
    private static GasNetDataCueLoadOptions Options() => new GasNetDataCueLoadOptions()
        .RegisterActionType<FlashCueAction>(FlashCueAction.Schema)
        .RegisterActionType<LogCueAction>(LogCueAction.Schema)
        .RegisterActionType<PlainCueAction>(); // no schema: round-trips with no extra fields

    private static string RoundTrip(string json, GasNetDataCueLoadOptions? options = null)
    {
        var catalog = GasNetDataCueLoader.LoadCues(json, options ?? Options());
        var written = GasNetDataCueWriter.WriteCatalog(catalog.Cues, options ?? Options());
        var reloaded = GasNetDataCueLoader.LoadCues(written, options ?? Options());
        var rewritten = GasNetDataCueWriter.WriteCatalog(reloaded.Cues, options ?? Options());
        Assert.Equal(written, rewritten);
        return written;
    }

    private static void LoadThrows(string json, string expectedFragment, GasNetDataCueLoadOptions? options = null)
    {
        var exception = Assert.Throws<GasNetDataException>(
            () => GasNetDataCueLoader.LoadCues(json, options ?? Options()));
        Assert.Contains(expectedFragment, exception.Message);
    }

    // ------------------------------------------------------------------
    // Round-trip & defaults
    // ------------------------------------------------------------------

    [Fact]
    public void Minimal_StaticCue_OmitsIsStaticAndDefaultTrigger()
    {
        var def = new CueDefinition
        {
            Name = "Hit",
            Tag = GameplayTag.RequestGameplayTag("GameplayCue.Combat.Hit"),
            IsStatic = true,
            Actions = [new LogCueAction { TriggerOn = GameplayCueEvent.Executed, Format = "x" }],
        };

        // Schemas must be passed at write time too — the data layer only knows the schema-defined
        // fields if you tell it about them. Without options, only 'type' and 'triggerOn' are
        // emitted (mirrors how the loader needs the same options to read them back).
        var json = GasNetDataCueWriter.WriteCatalog([new("Hit", def)], Options());
        var root = JsonDocument.Parse(json).RootElement;
        var cue = root.GetProperty("cues").GetProperty("Hit");
        Assert.False(cue.TryGetProperty("isStatic", out _));  // true is the default; omit
        var action = cue.GetProperty("actions")[0];
        Assert.False(action.TryGetProperty("triggerOn", out _)); // Static default is Executed
        Assert.Equal("LogCueAction", action.GetProperty("type").GetString());
        Assert.Equal("x", action.GetProperty("format").GetString());
    }

    [Fact]
    public void ActorCue_EmitsIsStaticFalse_AndOnActiveDefault()
    {
        var def = new CueDefinition
        {
            Name = "Buff",
            Tag = GameplayTag.RequestGameplayTag("GameplayCue.State.Buff"),
            IsStatic = false,
            Actions = [new PlainCueAction { TriggerOn = GameplayCueEvent.OnActive }],
        };

        var json = GasNetDataCueWriter.WriteCatalog([new("Buff", def)]);
        var cue = JsonDocument.Parse(json).RootElement.GetProperty("cues").GetProperty("Buff");
        Assert.True(cue.TryGetProperty("isStatic", out var ist) && ist.GetBoolean() == false);
        Assert.False(cue.GetProperty("actions")[0].TryGetProperty("triggerOn", out _));
    }

    [Fact]
    public void KitchenSink_RoundTrip_IsStable_AndSemantic()
    {
        const string json = """
        {
          "cues": {
            "Hit": {
              "tag": "GameplayCue.Combat.Hit",
              "actions": [
                { "type": "FlashCueAction", "triggerOn": "Executed", "durationMs": 150, "color": "#FFFFFF" },
                { "type": "LogCueAction", "triggerOn": "Executed", "format": "{target} hit! {magnitude:0.#} HP" }
              ]
            },
            "Buff": {
              "tag": "GameplayCue.State.Buff",
              "isStatic": false,
              "actions": [
                { "type": "PlainCueAction", "triggerOn": "OnActive" },
                { "type": "FlashCueAction", "triggerOn": "Removed", "durationMs": 300, "color": "#FF0000" }
              ]
            }
          }
        }
        """;

        var written = RoundTrip(json);
        var catalog = GasNetDataCueLoader.LoadCues(written, Options());

        var hit = catalog.Get("Hit");
        Assert.True(hit.IsStatic);
        Assert.Equal("GameplayCue.Combat.Hit", hit.Tag.Name);
        Assert.Equal(2, hit.Actions.Count);
        var hitFlash = Assert.IsType<FlashCueAction>(hit.Actions[0]);
        Assert.Equal(GameplayCueEvent.Executed, hitFlash.TriggerOn);
        Assert.Equal(150f, hitFlash.DurationMs);
        Assert.Equal("#FFFFFF", hitFlash.Color);
        var hitLog = Assert.IsType<LogCueAction>(hit.Actions[1]);
        Assert.Equal("{target} hit! {magnitude:0.#} HP", hitLog.Format);

        var buff = catalog.Get("Buff");
        Assert.False(buff.IsStatic);
        Assert.IsType<PlainCueAction>(buff.Actions[0]);
        Assert.Equal(GameplayCueEvent.OnActive, buff.Actions[0].TriggerOn);
        var buffFlash = Assert.IsType<FlashCueAction>(buff.Actions[1]);
        Assert.Equal(GameplayCueEvent.Removed, buffFlash.TriggerOn);
        Assert.Equal("#FF0000", buffFlash.Color);
    }

    [Fact]
    public void CueOrder_IsPreserved()
    {
        var a = new CueDefinition { Name = "A", Tag = T.Tag("GameplayCue.A"), Actions = [new PlainCueAction()] };
        var b = new CueDefinition { Name = "B", Tag = T.Tag("GameplayCue.B"), Actions = [new PlainCueAction()] };

        var json = GasNetDataCueWriter.WriteCatalog([new("Z_First", a), new("A_Second", b)]);
        var names = JsonDocument.Parse(json).RootElement
            .GetProperty("cues").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["Z_First", "A_Second"], names);
    }

    // ------------------------------------------------------------------
    // Load-time errors are loud and point at the field
    // ------------------------------------------------------------------

    [Fact]
    public void UnknownActionType_IsRejected() => LoadThrows("""
        { "cues": { "Hit": { "tag": "GameplayCue.X", "actions": [
            { "type": "DoesNotExist" }
        ] } } }
        """, "cue action type 'DoesNotExist' is not registered");

    [Fact]
    public void UnknownCueField_IsRejected() => LoadThrows("""
        { "cues": { "Hit": { "tag": "GameplayCue.X", "actionz": [] } } }
        """, "unknown field 'actionz'");

    [Fact]
    public void ActionWithUnknownField_ForSchemedType_IsAcceptedAsExtraField()
    {
        // The base loader only knows 'type'/'triggerOn'; the schema owns the rest of the format.
        // Fields the schema doesn't read are still passed through (the schema can choose to
        // strict-validate or be permissive). This is the contract that lets Unity add
        // asset-reference fields to vfx/audio actions without changing the loader.
        var options = new GasNetDataCueLoadOptions()
            .RegisterActionType<FlashCueAction>(FlashCueAction.Schema);
        var catalog = GasNetDataCueLoader.LoadCues("""
            { "cues": { "Hit": { "tag": "GameplayCue.X", "actions": [
                { "type": "FlashCueAction", "durationMs": 100, "futureField": "ignored" }
            ] } } }
            """, options);
        var flash = Assert.IsType<FlashCueAction>(catalog.Get("Hit").Actions[0]);
        Assert.Equal(100f, flash.DurationMs);
    }

    [Fact]
    public void MissingTag_IsRejected() => LoadThrows("""
        { "cues": { "Hit": { "actions": [
            { "type": "PlainCueAction" }
        ] } } }
        """, "needs a 'tag'");

    [Fact]
    public void MissingActions_IsRejected() => LoadThrows("""
        { "cues": { "Hit": { "tag": "GameplayCue.X" } } }
        """, "needs an 'actions' array");

    [Fact]
    public void EmptyActions_IsRejected() => LoadThrows("""
        { "cues": { "Hit": { "tag": "GameplayCue.X", "actions": [] } } }
        """, "must contain at least one");

    [Fact]
    public void MissingActionType_IsRejected() => LoadThrows("""
        { "cues": { "Hit": { "tag": "GameplayCue.X", "actions": [
            { "triggerOn": "Executed" }
        ] } } }
        """, "actions need a 'type'");

    [Fact]
    public void InvalidTriggerOn_IsRejected() => LoadThrows("""
        { "cues": { "Hit": { "tag": "GameplayCue.X", "actions": [
            { "type": "PlainCueAction", "triggerOn": "Sometime" }
        ] } } }
        """, "is not a valid GameplayCueEvent");

    [Fact]
    public void InvalidIsStatic_IsRejected() => LoadThrows("""
        { "cues": { "Hit": { "tag": "GameplayCue.X", "isStatic": "yes", "actions": [
            { "type": "PlainCueAction" }
        ] } } }
        """, "expected true or false");

    [Fact]
    public void DuplicateCueName_IsRejected() => LoadThrows("""
        { "cues": {
            "Hit": { "tag": "GameplayCue.X", "actions": [ { "type": "PlainCueAction" } ] },
            "Hit": { "tag": "GameplayCue.Y", "actions": [ { "type": "PlainCueAction" } ] }
        } }
        """, "Duplicate cue name 'Hit'");

    [Fact]
    public void MalformedJson_IsRejected() => LoadThrows("{ \"cues\": ", "Cue JSON parse error");

    [Fact]
    public void MissingCuesRoot_IsRejected() => LoadThrows(
        """ { "cuesX": {} } """, "unknown field 'cuesX'");

    [Fact]
    public void CatalogGet_UnknownName_ThrowsWithLoadedNames()
    {
        var catalog = GasNetDataCueLoader.LoadCues(
            """ { "cues": { "Hit": { "tag": "GameplayCue.X", "actions": [ { "type": "PlainCueAction" } ] } } } """,
            Options());
        var exception = Assert.Throws<GasNetDataException>(() => catalog.Get("Miss"));
        Assert.Contains("No cue named 'Miss'", exception.Message);
        Assert.Contains("Hit", exception.Message);
    }

    [Fact]
    public void SchemaReader_IsInvoked_AndSchemaWriter_ProducesExpectedShape()
    {
        // White-box probe: a schema can both read custom fields and write them. Catches accidental
        // loss of the schema wiring if someone refactors the loader/writer later.
        const string json = """
        { "cues": { "Hit": { "tag": "GameplayCue.X", "actions": [
            { "type": "FlashCueAction", "durationMs": 250, "color": "#00FF00" }
        ] } } }
        """;
        var options = Options();
        var catalog = GasNetDataCueLoader.LoadCues(json, options);
        var flash = Assert.IsType<FlashCueAction>(catalog.Get("Hit").Actions[0]);

        // Schema's reader populated the typed fields.
        Assert.Equal(250f, flash.DurationMs);
        Assert.Equal("#00FF00", flash.Color);

        // Schema's writer emits them back, with default trigger omitted.
        var written = GasNetDataCueWriter.WriteCatalog(catalog.Cues, options);
        var action = JsonDocument.Parse(written).RootElement
            .GetProperty("cues").GetProperty("Hit").GetProperty("actions")[0];
        Assert.Equal(250f, action.GetProperty("durationMs").GetSingle());
        Assert.Equal("#00FF00", action.GetProperty("color").GetString());
    }
}

// ----------------------------------------------------------------------
// Test action types + their schemas (live in the test assembly).
// ----------------------------------------------------------------------

public sealed class FlashCueAction : CueAction
{
    public float DurationMs { get; set; }
    public string Color { get; set; } = "#FFFFFF";

    public override void Execute(GameplayCueParameters parameters) { }

    public static readonly CueActionSchema Schema = new()
    {
        Reader = (action, el) =>
        {
            var self = (FlashCueAction)action;
            if (el.TryGetProperty("durationMs", out var d))
                self.DurationMs = d.ValueKind == JsonValueKind.Number ? d.GetSingle() : 0f;
            if (el.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String)
                self.Color = c.GetString()!;
        },
        Writer = (action, node) =>
        {
            var self = (FlashCueAction)action;
            if (self.DurationMs != 0f) node["durationMs"] = self.DurationMs;
            if (self.Color != "#FFFFFF") node["color"] = self.Color;
        },
    };
}

public sealed class LogCueAction : CueAction
{
    public string Format { get; set; } = "";

    public override void Execute(GameplayCueParameters parameters) { }

    public static readonly CueActionSchema Schema = new()
    {
        Reader = (action, el) =>
        {
            var self = (LogCueAction)action;
            if (el.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String)
                self.Format = f.GetString()!;
        },
        Writer = (action, node) =>
        {
            var self = (LogCueAction)action;
            if (self.Format.Length != 0) node["format"] = self.Format;
        },
    };
}

/// <summary>A cue action that carries no extra fields; exercises the "no schema" branch.</summary>
public sealed class PlainCueAction : CueAction
{
    public override void Execute(GameplayCueParameters parameters) { }
}
