using GasNet;
using GasNet.Data;
using GasNet.Editor;
using Xunit;

namespace GasNet.Editor.Tests;

/// <summary>编辑器文档层的行为锁定：文件往返、改名跟踪、克隆深拷贝、撤销/重做。</summary>
public class CatalogDocumentTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "GasNetEditorTests", Guid.NewGuid().ToString("N"));
    private readonly CatalogDocument _document = new();

    public CatalogDocumentTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathOf(string name) => Path.Combine(_directory, name);

    private GasNetDataLoadOptions Options() =>
        new GasNetDataLoadOptions().RegisterAttributeSet<EditorTestAttributeSet>();

    private const string CatalogJson = """
    {
      "effects": {
        "GE_Damage": {
          "durationPolicy": "Instant",
          "modifiers": [ { "attribute": "EditorTestAttributeSet.Health", "op": "Add",
                           "magnitude": { "type": "scalableFloat", "value": -10 } } ]
        },
        "GE_Slow": { "durationPolicy": "Infinite", "grantedTags": ["Stun.Blocked"] }
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

        Assert.Equal(["GE_Damage", "GE_Slow"], _document.Effects.Select(e => e.Name));
        Assert.All(_document.Effects, e => Assert.Equal(e.Name, e.SavedName));
        Assert.False(_document.Dirty);
        Assert.Contains("Stun.Blocked", _document.FileTagNames);
    }

    [Fact]
    public void Open_DuplicateKeys_AreRejected()
    {
        File.WriteAllText(PathOf("dup.json"), """
        { "effects": { "GE_X": { "durationPolicy": "Instant" }, "GE_X": { "durationPolicy": "Infinite" } } }
        """);
        Assert.Throws<GasNetDataException>(() => _document.Open(PathOf("dup.json"), Options()));
    }

    [Fact]
    public void Rename_TracksSavedName_UntilSaved()
    {
        OpenCatalog();

        var entry = _document.Effects[0];
        entry.Name = "GE_Damage_V2";
        Assert.NotEqual(entry.Name, entry.SavedName); // UI 据此显示"旧引用不更新"提示

        _document.Save(PathOf("catalog.json"));
        Assert.Equal(entry.Name, entry.SavedName);
        Assert.False(_document.Dirty);

        var reopened = new CatalogDocument();
        reopened.Open(PathOf("catalog.json"), Options());
        Assert.Equal("GE_Damage_V2", reopened.Effects[0].Name);
    }

    [Fact]
    public void Validate_Reports_EmptyAndDuplicateNames()
    {
        OpenCatalog();

        _document.Effects[1].Name = "";
        Assert.Contains(_document.Validate(Options()), e => e.Contains("效果名不能为空"));

        _document.Effects[1].Name = _document.Effects[0].Name;
        Assert.Contains(_document.Validate(Options()), e => e.Contains("效果名重复"));
    }

    [Fact]
    public void Clone_CopiesDeeply_WithUniqueName()
    {
        OpenCatalog();
        _document.SelectedIndex = 0;

        var clone = _document.CloneSelected();
        Assert.NotNull(clone);
        Assert.Equal("GE_Damage_Copy", clone!.Name);
        Assert.Null(clone.SavedName); // 新条目没有"旧引用"

        // 改原件不得影响克隆——核心库 Clone() 是浅拷贝（修饰符条目共享引用），编辑器必须真独立
        _document.Effects[0].Def.Duration = 9f;
        ((ScalableFloatMagnitude)_document.Effects[0].Def.Modifiers[0].Magnitude).Value = -999f;
        Assert.Equal(0f, clone.Def.Duration);
        Assert.Equal(-10f, ((ScalableFloatMagnitude)clone.Def.Modifiers[0].Magnitude).Value);

        _document.SelectedIndex = 0;
        Assert.Equal("GE_Damage_Copy2", _document.CloneSelected()!.Name);
    }

    [Fact]
    public void UndoRedo_RestoresEdits_AndResetsOnOpen()
    {
        OpenCatalog();

        _document.Effects[0].Def.Duration = 7f;
        _document.CommitSnapshot(); // 编辑器 Refresh 在每个提交点调用

        Assert.True(_document.CanUndo);
        _document.Undo();
        Assert.Equal(0f, _document.Effects[0].Def.Duration);
        Assert.True(_document.CanRedo);

        _document.Redo();
        Assert.Equal(7f, _document.Effects[0].Def.Duration);

        _document.Open(PathOf("catalog.json"), Options());
        Assert.False(_document.CanUndo);
        Assert.False(_document.CanRedo);
    }
}

/// <summary>反查扫描：层级语义与运行时 HasTag 一致，非法查询不抛。</summary>
public class CatalogSearchTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "GasNetEditorTests", Guid.NewGuid().ToString("N"));
    private readonly CatalogDocument _document = new();

    public CatalogSearchTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void FindTagReferences_MatchesHierarchically()
    {
        File.WriteAllText(Path.Combine(_directory, "catalog.json"), """
        {
          "effects": {
            "GE_Slow": { "durationPolicy": "Infinite", "grantedTags": ["Stun.Blocked"] },
            "GE_Hit": { "durationPolicy": "Instant", "assetTags": ["Combat.Hit"] }
          }
        }
        """);
        _document.Open(Path.Combine(_directory, "catalog.json"),
            new GasNetDataLoadOptions().RegisterAttributeSet<EditorTestAttributeSet>());

        var hits = CatalogSearch.FindTagReferences(_document.Effects, "Stun");
        Assert.Contains(hits, h => h.Effect == "GE_Slow" && h.Field == "grantedTags" && h.Tag.Name == "Stun.Blocked");
        Assert.DoesNotContain(hits, h => h.Effect == "GE_Hit");

        Assert.Empty(CatalogSearch.FindTagReferences(_document.Effects, "Nothing.Here"));
        Assert.Empty(CatalogSearch.FindTagReferences(_document.Effects, "Bad..Name")); // 非法名 → 无结果，不抛
    }

    [Fact]
    public void FindAttributeReferences_FindsModifiersAndCaptures()
    {
        File.WriteAllText(Path.Combine(_directory, "catalog.json"), """
        {
          "effects": {
            "GE_A": { "durationPolicy": "Instant", "modifiers": [ { "attribute": "EditorTestAttributeSet.Health", "op": "Add",
                      "magnitude": { "type": "attributeBased", "attribute": "EditorTestAttributeSet.Mana" } } ] }
          }
        }
        """);
        _document.Open(Path.Combine(_directory, "catalog.json"),
            new GasNetDataLoadOptions().RegisterAttributeSet<EditorTestAttributeSet>());

        var hits = CatalogSearch.FindAttributeReferences(_document.Effects, "EditorTestAttributeSet.Health");
        Assert.Contains(hits, h => h.Field == "modifiers[0].attribute");

        var captures = CatalogSearch.FindAttributeReferences(_document.Effects, "EditorTestAttributeSet.Mana");
        Assert.Contains(captures, h => h.Field == "modifiers[0].magnitude.attribute");
    }
}
