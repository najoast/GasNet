using System.Text.Json;
using GasNet;
using GasNet.Data;
using GasNet.Editor;
using Xunit;

namespace GasNet.Editor.Tests;

/// <summary>编辑器 cue 文档层的行为锁定：文件往返、改名跟踪、克隆深拷贝、撤销/重做。
/// 镜像 <see cref="CatalogDocumentTests"/>，覆盖 cue 专属语义（action 深拷贝、tag 根软提醒）。</summary>
public class CueDocumentTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "GasNetEditorTests", Guid.NewGuid().ToString("N"));
    private readonly CueDocument _document = new();

    public CueDocumentTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathOf(string name) => Path.Combine(_directory, name);

    private GasNetDataCueLoadOptions Options() =>
        new GasNetDataCueLoadOptions()
            .RegisterActionType<EditorTestFlashCueAction>(EditorTestFlashCueAction.Schema)
            .RegisterActionType<EditorTestLogCueAction>(EditorTestLogCueAction.Schema);

    private const string CatalogJson = """
    {
      "cues": {
        "Hit": {
          "tag": "GameplayCue.Combat.Hit",
          "actions": [
            { "type": "EditorTestFlashCueAction", "durationMs": 150, "color": "#FF0000" },
            { "type": "EditorTestLogCueAction",   "format": "{target} hit" }
          ]
        },
        "Buff": {
          "tag": "GameplayCue.Combat.Buff",
          "isStatic": false,
          "actions": [
            { "type": "EditorTestLogCueAction", "triggerOn": "OnActive", "format": "buff up" }
          ]
        }
      }
    }
    """;

    private void OpenCatalog(string name = "catalog.json")
    {
        File.WriteAllText(PathOf(name), CatalogJson);
        _document.Open(PathOf(name), Options());
    }

    [Fact]
    public void Open_PreservesFileOrder_AndRecordsSavedNames()
    {
        OpenCatalog();

        Assert.Equal(["Hit", "Buff"], _document.Cues.Select(c => c.Name));
        Assert.All(_document.Cues, c => Assert.Equal(c.Name, c.SavedName));
        Assert.False(_document.Dirty);
        Assert.Contains("GameplayCue.Combat.Hit", _document.FileTagNames);
        Assert.Contains("GameplayCue.Combat.Buff", _document.FileTagNames);
    }

    [Fact]
    public void Open_DuplicateKeys_AreRejected()
    {
        File.WriteAllText(PathOf("dup.json"), """
        {
          "cues": {
            "Hit": { "tag": "GameplayCue.A", "actions": [ { "type": "EditorTestFlashCueAction" } ] },
            "Hit": { "tag": "GameplayCue.B", "actions": [ { "type": "EditorTestFlashCueAction" } ] }
          }
        }
        """);
        Assert.Throws<GasNetDataException>(() => _document.Open(PathOf("dup.json"), Options()));
    }

    [Fact]
    public void Rename_TracksSavedName_UntilSaved()
    {
        OpenCatalog();

        var entry = _document.Cues[0];
        entry.Name = "Hit_V2";
        Assert.NotEqual(entry.Name, entry.SavedName);

        _document.Save(PathOf("catalog.json"), Options());
        Assert.Equal(entry.Name, entry.SavedName);
        Assert.False(_document.Dirty);

        var reopened = new CueDocument();
        reopened.Open(PathOf("catalog.json"), Options());
        Assert.Equal("Hit_V2", reopened.Cues[0].Name);
    }

    [Fact]
    public void Validate_Reports_EmptyAndDuplicateNames()
    {
        OpenCatalog();

        _document.Cues[1].Name = "";
        Assert.Contains(_document.Validate(Options()), e => e.Contains("Cue 名不能为空"));

        _document.Cues[1].Name = _document.Cues[0].Name;
        Assert.Contains(_document.Validate(Options()), e => e.Contains("Cue 名重复"));
    }

    [Fact]
    public void Validate_RoundTripsThroughLoader()
    {
        OpenCatalog();
        _document.Cues[0].Def.Tag = GameplayTagsManager.Default.RequestGameplayTag("GameplayCue.Combat.HitRenamed");

        var errors = _document.Validate(Options());
        Assert.Empty(errors);

        // Save + reopen must yield the renamed tag
        _document.Save(PathOf("catalog.json"), Options());
        var reopened = new CueDocument();
        reopened.Open(PathOf("catalog.json"), Options());
        Assert.Equal("GameplayCue.Combat.HitRenamed", reopened.Cues[0].Def.Tag.Name);
    }

    [Fact]
    public void Clone_CopiesActionsDeeply_WithUniqueName()
    {
        OpenCatalog();
        _document.SelectedIndex = 0;

        var clone = _document.CloneSelected();
        Assert.NotNull(clone);
        Assert.Equal("Hit_Copy", clone!.Name);
        Assert.Null(clone.SavedName); // 新条目没有"旧引用"

        // 改原件不得影响克隆——CueDocument 必须真独立（动作列表里每条 action 也是独立引用）
        var originalFlash = (EditorTestFlashCueAction)_document.Cues[0].Def.Actions[0];
        var clonedFlash = (EditorTestFlashCueAction)clone.Def.Actions[0];
        Assert.NotSame(originalFlash, clonedFlash);

        originalFlash.DurationMs = 999f;
        originalFlash.Color = "#000000";
        Assert.Equal(150f, clonedFlash.DurationMs);
        Assert.Equal("#FF0000", clonedFlash.Color);

        _document.SelectedIndex = 0;
        Assert.Equal("Hit_Copy2", _document.CloneSelected()!.Name);
    }

    [Fact]
    public void UndoRedo_RestoresEdits_AndResetsOnOpen()
    {
        OpenCatalog();

        _document.Cues[0].Def.Tag = GameplayTagsManager.Default.RequestGameplayTag("GameplayCue.Combat.HitV2");
        _document.CommitSnapshot(); // 编辑器 Refresh 在每个提交点调用

        Assert.True(_document.CanUndo);
        _document.Undo();
        Assert.Equal("GameplayCue.Combat.Hit", _document.Cues[0].Def.Tag.Name);
        Assert.True(_document.CanRedo);

        _document.Redo();
        Assert.Equal("GameplayCue.Combat.HitV2", _document.Cues[0].Def.Tag.Name);

        _document.Open(PathOf("catalog.json"), Options());
        Assert.False(_document.CanUndo);
        Assert.False(_document.CanRedo);
    }

    [Fact]
    public void AddAndRemove_TrackSelectionAndDirty()
    {
        OpenCatalog();

        Assert.False(_document.Dirty);
        _document.AddNew("Custom");
        Assert.True(_document.Dirty);
        Assert.Equal("Custom", _document.Selected!.Name);
        Assert.Equal(_document.Cues.Count - 1, _document.SelectedIndex);

        _document.RemoveAt(_document.SelectedIndex);
        Assert.DoesNotContain(_document.Cues, c => c.Name == "Custom");
        Assert.True(_document.Dirty); // 删除也算编辑
    }
}

// ----------------------------------------------------------------------
// 测试本地 cue action 类型——不与 GasNet.Tests 的 fixture 共享，避免跨程序集强耦合。
// Schema 形态与 FlashCueAction / LogCueAction 同款；只是类名加 Editor 前缀。
// ----------------------------------------------------------------------

public sealed class EditorTestFlashCueAction : CueAction
{
    public float DurationMs { get; set; }
    public string Color { get; set; } = "#FFFFFF";

    public override void Execute(GameplayCueParameters parameters) { }

    public static readonly CueActionSchema Schema = new()
    {
        Reader = (action, el) =>
        {
            var self = (EditorTestFlashCueAction)action;
            if (el.TryGetProperty("durationMs", out var d) && d.ValueKind == JsonValueKind.Number)
                self.DurationMs = d.GetSingle();
            if (el.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String)
                self.Color = c.GetString()!;
        },
        Writer = (action, node) =>
        {
            var self = (EditorTestFlashCueAction)action;
            if (self.DurationMs != 0f) node["durationMs"] = self.DurationMs;
            if (self.Color != "#FFFFFF") node["color"] = self.Color;
        },
    };
}

public sealed class EditorTestLogCueAction : CueAction
{
    public string Format { get; set; } = "";

    public override void Execute(GameplayCueParameters parameters) { }

    public static readonly CueActionSchema Schema = new()
    {
        Reader = (action, el) =>
        {
            var self = (EditorTestLogCueAction)action;
            if (el.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String)
                self.Format = f.GetString()!;
        },
        Writer = (action, node) =>
        {
            var self = (EditorTestLogCueAction)action;
            if (self.Format.Length != 0) node["format"] = self.Format;
        },
    };
}
