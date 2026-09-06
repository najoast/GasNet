using System.Text.Json;
using GasNet;
using GasNet.Data;

namespace GasNet.Editor;

/// <summary>
/// 编辑中的 cue 目录：有序的 <see cref="CueDefinition"/> 列表 + 脏标记 + 撤销/重做。
/// 写和读只经过 <see cref="GasNetDataCueWriter"/>/<see cref="GasNetDataCueLoader"/>，校验即
/// "写出→读回"往返——和 <see cref="CatalogDocument"/> 的硬性规则一致：编辑器自身不写 JSON 序列化。
/// </summary>
public sealed class CueDocument
{
    public sealed class Entry
    {
        public string Name = "";
        /// <summary>名称在最近一次打开/保存文件时的值；null = 本会话新建。
        /// Name ≠ SavedName 表示改名未落盘，UI 据此提示目录外的旧引用（cue 引用按 cue 名查找）。</summary>
        public string? SavedName;
        public CueDefinition Def = new();
    }

    public List<Entry> Cues { get; } = [];
    public string Path { get; set; } = "";
    public bool Dirty { get; set; }
    public int SelectedIndex { get; set; } = -1;

    /// <summary>打开/保存那一刻就在目录里的标签名。编辑中新输入的、不在这里的标签会被 UI
    /// 软提醒（可能是拼写错误——运行时注册模型会静默接受任何新标签）。</summary>
    public HashSet<string> FileTagNames { get; private set; } = new(StringComparer.Ordinal);

    private const int MaxUndoSteps = 100;
    private readonly LinkedList<List<Entry>> _undoStack = [];
    private readonly LinkedList<List<Entry>> _redoStack = [];
    private List<Entry>? _pending;

    public Entry? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Cues.Count ? Cues[SelectedIndex] : null;

    public void MarkDirty() => Dirty = true;

    public Entry AddNew(string name)
    {
        var entry = new Entry { Name = name };
        Cues.Add(entry);
        Dirty = true;
        SelectedIndex = Cues.Count - 1;
        return entry;
    }

    public Entry? CloneSelected()
    {
        if (Selected is not { } source)
            return null;
        var clone = new Entry { Name = UniqueCopyName(source.Name), Def = CloneDef(source.Def) };
        Cues.Insert(SelectedIndex + 1, clone);
        SelectedIndex = Cues.IndexOf(clone);
        Dirty = true;
        return clone;
    }

    private string UniqueCopyName(string baseName)
    {
        for (var i = 1; ; i++)
        {
            var candidate = i == 1 ? $"{baseName}_Copy" : $"{baseName}_Copy{i}";
            if (Cues.All(entry => entry.Name != candidate))
                return candidate;
        }
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Cues.Count)
            return;
        Cues.RemoveAt(index);
        SelectedIndex = Math.Min(SelectedIndex, Cues.Count - 1);
        Dirty = true;
    }

    public void Open(string path, GasNetDataCueLoadOptions options)
    {
        var json = File.ReadAllText(path);
        var catalog = GasNetDataCueLoader.LoadCues(json, options);

        Cues.Clear();
        // LoadCues returns a Dictionary (no order); re-walk the JSON to keep the editor view
        // aligned with the file.
        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.GetProperty("cues").EnumerateObject())
        {
            Cues.Add(new Entry
            {
                Name = property.Name,
                SavedName = property.Name,
                Def = catalog.Cues[property.Name],
            });
        }

        Path = path;
        Dirty = false;
        SelectedIndex = Cues.Count > 0 ? 0 : -1;
        FileTagNames = [.. Cues.Where(c => c.Def.Tag.IsValid).Select(c => c.Def.Tag.Name)];
        ResetHistory(CloneEntries(Cues));
    }

    public void Save(string path, GasNetDataCueLoadOptions options)
    {
        File.WriteAllText(path, GasNetDataCueWriter.WriteCatalog(
            Cues.Select(entry => new KeyValuePair<string, CueDefinition>(entry.Name, entry.Def)),
            options));
        Path = path;
        Dirty = false;
        foreach (var entry in Cues)
            entry.SavedName = entry.Name;
        FileTagNames = [.. Cues.Where(c => c.Def.Tag.IsValid).Select(c => c.Def.Tag.Name)];
        ResetHistory(CloneEntries(Cues));
    }

    // ------------------------------------------------------------------
    // 撤销 / 重做
    // ------------------------------------------------------------------

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void CommitSnapshot()
    {
        if (_pending is not null)
        {
            _undoStack.AddFirst(_pending);
            if (_undoStack.Count > MaxUndoSteps)
                _undoStack.RemoveLast();
            _redoStack.Clear();
        }
        _pending = CloneEntries(Cues);
    }

    public void Undo()
    {
        if (_undoStack.First is not { } node)
            return;
        if (_pending is not null)
            _redoStack.AddFirst(_pending);
        _undoStack.RemoveFirst();
        RestoreFrom(node.Value);
        _pending = node.Value;
    }

    public void Redo()
    {
        if (_redoStack.First is not { } node)
            return;
        if (_pending is not null)
            _undoStack.AddFirst(_pending);
        _redoStack.RemoveFirst();
        RestoreFrom(node.Value);
        _pending = node.Value;
    }

    private void ResetHistory(List<Entry> baseline)
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _pending = baseline;
    }

    private void RestoreFrom(List<Entry> snapshot)
    {
        Cues.Clear();
        Cues.AddRange(CloneEntries(snapshot));
        SelectedIndex = Math.Min(SelectedIndex, Cues.Count - 1);
        if (SelectedIndex < 0 && Cues.Count > 0)
            SelectedIndex = 0;
        Dirty = true;
    }

    // ------------------------------------------------------------------
    // 深拷贝
    // ------------------------------------------------------------------

    private static List<Entry> CloneEntries(IEnumerable<Entry> entries) =>
        [.. entries.Select(entry => new Entry { Name = entry.Name, SavedName = entry.SavedName, Def = CloneDef(entry.Def) })];

    /// <summary>深拷贝 cue 定义。Tag 是结构体（值类型），Actions 列表是引用——深拷贝每条 action
    /// 的公共可写属性，避免撤销栈里相邻快照共享引用。</summary>
    private static CueDefinition CloneDef(CueDefinition source)
    {
        var clone = new CueDefinition
        {
            Name = source.Name,
            Tag = source.Tag,
            IsStatic = source.IsStatic,
            Actions = [.. source.Actions.Select(CloneAction)],
        };
        return clone;
    }

    private static CueAction CloneAction(CueAction source)
    {
        var clone = (CueAction)Activator.CreateInstance(source.GetType())!;
        clone.TriggerOn = source.TriggerOn;
        // Mirror public read/write scalar/collection properties. The schema's Writer will only
        // emit the same set on write (it knows the JSON field names), so this stays consistent.
        foreach (var prop in source.GetType().GetProperties())
        {
            if (prop.GetMethod is null || prop.SetMethod is null) continue;
            if (prop.Name == nameof(CueAction.TriggerOn)) continue;
            if (prop.GetIndexParameters().Length > 0) continue;
            var value = prop.GetValue(source);
            if (value is null) continue;
            // Skip non-scalar lists/maps — the action subclasses only carry scalar properties
            // by convention, and the schema is responsible for emitting them in the right shape.
            if (value is string || value.GetType().IsPrimitive || value is GameplayTag || value is bool)
                prop.SetValue(clone, value);
        }
        return clone;
    }

    // ------------------------------------------------------------------
    // 校验
    // ------------------------------------------------------------------

    /// <summary>全量往返校验：写出→真加载器读回。返回错误列表（空 = 通过）。</summary>
    public List<string> Validate(GasNetDataCueLoadOptions options)
    {
        var errors = new List<string>();
        if (Cues.Any(entry => entry.Name.Length == 0))
            errors.Add("Cue 名不能为空。");
        if (Cues.GroupBy(e => e.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } dup)
            errors.Add($"Cue 名重复：'{dup.Key}'。");

        try
        {
            var json = GasNetDataCueWriter.WriteCatalog(
                Cues.Select(entry => new KeyValuePair<string, CueDefinition>(entry.Name, entry.Def)),
                options);
            var reloaded = GasNetDataCueLoader.LoadCues(json, options);
            foreach (var entry in Cues)
                if (!reloaded.Cues.ContainsKey(entry.Name))
                    errors.Add($"内部错误：'{entry.Name}' 写出后无法读回。");
        }
        catch (GasNetDataException exception)
        {
            errors.Add(exception.Message);
        }
        catch (Exception exception)
        {
            errors.Add(exception.Message);
        }
        return errors;
    }
}
