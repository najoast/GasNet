using System.Text.Json;
using GasNet;
using GasNet.Data;

namespace GasNet.Editor;

/// <summary>
/// 编辑中的目录：有序的效果列表 + 脏标记 + 撤销/重做。写和读只经过 GasNet.Data 的
/// <see cref="GasNetDataWriter"/>/<see cref="GasNetDataLoader"/>，校验即"写出→读回"往返。
/// </summary>
public sealed class CatalogDocument
{
    public sealed class Entry
    {
        public string Name = "";
        /// <summary>名称在最近一次打开/保存文件时的值；null = 本会话新建。
        /// Name ≠ SavedName 表示改名未落盘，UI 据此提示目录外的旧引用（游戏代码按名称取效果）。</summary>
        public string? SavedName;
        public GameplayEffectDefinition Def = new();
    }

    public List<Entry> Effects { get; } = [];
    public string Path { get; set; } = "";
    public bool Dirty { get; set; }
    public int SelectedIndex { get; set; } = -1;

    /// <summary>打开/保存那一刻就在目录里的标签名。编辑中新输入的、不在这里的标签会被 UI
    /// 软提醒（可能是拼写错误——运行时注册模型会静默接受任何新标签）。</summary>
    public HashSet<string> FileTagNames { get; private set; } = new(StringComparer.Ordinal);

    private const int MaxUndoSteps = 100;
    private readonly LinkedList<List<Entry>> _undoStack = [];
    private readonly LinkedList<List<Entry>> _redoStack = [];
    private List<Entry>? _pending; // 最近一次提交后的状态快照；下一次编辑提交时移入撤销栈

    public Entry? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Effects.Count ? Effects[SelectedIndex] : null;

    public void MarkDirty() => Dirty = true;

    public Entry AddNew(string name)
    {
        var entry = new Entry { Name = name };
        Effects.Add(entry);
        Dirty = true;
        SelectedIndex = Effects.Count - 1;
        return entry;
    }

    /// <summary>深拷贝选中效果，插到它后面并选中；名字带 _Copy 后缀去重。</summary>
    public Entry? CloneSelected()
    {
        if (Selected is not { } source)
            return null;
        var clone = new Entry { Name = UniqueCopyName(source.Name), Def = CloneDef(source.Def) };
        Effects.Insert(SelectedIndex + 1, clone);
        SelectedIndex = Effects.IndexOf(clone);
        Dirty = true;
        return clone;
    }

    private string UniqueCopyName(string baseName)
    {
        for (var i = 1; ; i++)
        {
            var candidate = i == 1 ? $"{baseName}_Copy" : $"{baseName}_Copy{i}";
            if (Effects.All(entry => entry.Name != candidate))
                return candidate;
        }
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Effects.Count)
            return;
        Effects.RemoveAt(index);
        SelectedIndex = Math.Min(SelectedIndex, Effects.Count - 1);
        Dirty = true;
    }

    public void Open(string path, GasNetDataLoadOptions options)
    {
        var json = File.ReadAllText(path);
        var catalog = GasNetDataLoader.LoadCatalog(json, options);

        Effects.Clear();
        // LoadCatalog 返回 Dictionary（无序）；按 JSON 文件里的出现顺序重排，保持编辑视图与文件一致。
        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.GetProperty("effects").EnumerateObject())
            Effects.Add(new Entry { Name = property.Name, SavedName = property.Name, Def = catalog.Effects[property.Name] });

        Path = path;
        Dirty = false;
        SelectedIndex = Effects.Count > 0 ? 0 : -1;
        FileTagNames = [.. CatalogSearch.AllTagOccurrences(Effects).Select(o => o.Tag.Name)];
        ResetHistory(CloneEntries(Effects));
    }

    public void Save(string path)
    {
        File.WriteAllText(path, GasNetDataWriter.WriteCatalog(
            Effects.Select(entry => new KeyValuePair<string, GameplayEffectDefinition>(entry.Name, entry.Def))));
        Path = path;
        Dirty = false;
        foreach (var entry in Effects)
            entry.SavedName = entry.Name; // 改名已落盘，撤销"旧引用"提示
        FileTagNames = [.. CatalogSearch.AllTagOccurrences(Effects).Select(o => o.Tag.Name)];
        ResetHistory(CloneEntries(Effects));
    }

    // ------------------------------------------------------------------
    // 撤销 / 重做
    // ------------------------------------------------------------------

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// 编辑提交后调用（编辑器把所有编辑都汇入同一个 Refresh，这里在每个提交点惰性 push 一次）：
    /// 把上次快照以来的状态移入撤销栈，并拍下新快照。打开/保存后调用会记录一个无操作快照，
    /// 由调用方用 trackUndo=false 跳过。
    /// </summary>
    public void CommitSnapshot()
    {
        if (_pending is not null)
        {
            _undoStack.AddFirst(_pending);
            if (_undoStack.Count > MaxUndoSteps)
                _undoStack.RemoveLast();
            _redoStack.Clear();
        }
        _pending = CloneEntries(Effects);
    }

    public void Undo()
    {
        if (_undoStack.First is not { } node)
            return;
        if (_pending is not null)
            _redoStack.AddFirst(_pending);
        _undoStack.RemoveFirst();
        RestoreFrom(node.Value);
        _pending = node.Value; // 移交所有权；恢复进 Effects 的是独立深拷贝
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
        Effects.Clear();
        Effects.AddRange(CloneEntries(snapshot));
        SelectedIndex = Math.Min(SelectedIndex, Effects.Count - 1);
        if (SelectedIndex < 0 && Effects.Count > 0)
            SelectedIndex = 0;
        Dirty = true;
    }

    // ------------------------------------------------------------------
    // 深拷贝
    // ------------------------------------------------------------------

    private static List<Entry> CloneEntries(IEnumerable<Entry> entries) =>
        [.. entries.Select(entry => new Entry { Name = entry.Name, SavedName = entry.SavedName, Def = CloneDef(entry.Def) })];

    /// <summary>
    /// 深拷贝定义。核心库 Clone() 只复制容器与列表，修饰符条目、三类标签需求与授予能力条目
    /// 仍是共享引用——编辑器快照/克隆要求真正独立，这里补齐。ExecCalc/MMC/CAR 与幅度里的
    /// calculation 实例有意共享：UI 只整只替换、从不修改其内部。
    /// </summary>
    private static GameplayEffectDefinition CloneDef(GameplayEffectDefinition def)
    {
        var clone = def.Clone();
        clone.OngoingTagRequirements = CloneRequirements(def.OngoingTagRequirements);
        clone.ApplicationTagRequirements = CloneRequirements(def.ApplicationTagRequirements);
        clone.TargetTagRequirements = CloneRequirements(def.TargetTagRequirements);
        clone.Modifiers = [.. def.Modifiers.Select(CloneModifier)];
        clone.GrantedAbilities = [.. def.GrantedAbilities.Select(g => new GrantedAbilityEntry
        {
            AbilityType = g.AbilityType,
            Level = g.Level,
            InputID = g.InputID,
            RemovalPolicy = g.RemovalPolicy,
        })];
        return clone;
    }

    private static GameplayTagRequirements CloneRequirements(GameplayTagRequirements requirements)
    {
        var clone = new GameplayTagRequirements { TagQuery = requirements.TagQuery };
        clone.RequiredTags.AddTags(requirements.RequiredTags);
        clone.IgnoredTags.AddTags(requirements.IgnoredTags);
        return clone;
    }

    private static GameplayModifierInfo CloneModifier(GameplayModifierInfo modifier) => new()
    {
        Attribute = modifier.Attribute, // readonly struct
        ModifierOp = modifier.ModifierOp,
        Magnitude = CloneMagnitude(modifier.Magnitude),
        SourceTags = modifier.SourceTags.Clone(),
        TargetTags = modifier.TargetTags.Clone(),
    };

    private static GameplayEffectModifierMagnitude CloneMagnitude(GameplayEffectModifierMagnitude magnitude) => magnitude switch
    {
        ScalableFloatMagnitude scalable => WithShaping(
            new ScalableFloatMagnitude { Value = scalable.Value, ValuePerLevel = scalable.ValuePerLevel }, scalable),
        AttributeBasedMagnitude based => WithShaping(new AttributeBasedMagnitude
        {
            Capture = based.Capture, // record，不可变
            UseBaseValue = based.UseBaseValue,
            SourceTagFilter = based.SourceTagFilter?.Clone(),
            TargetTagFilter = based.TargetTagFilter?.Clone(),
        }, based),
        SetByCallerMagnitude setByCaller => WithShaping(new SetByCallerMagnitude(setByCaller.DataTag), setByCaller),
        CustomCalculationMagnitude custom => WithShaping(new CustomCalculationMagnitude(custom.Calculation), custom),
        _ => throw new NotSupportedException($"未知的幅度类型 '{magnitude.GetType().Name}'。"),
    };

    private static T WithShaping<T>(T clone, GameplayEffectModifierMagnitude source)
        where T : GameplayEffectModifierMagnitude
    {
        clone.Coefficient = source.Coefficient;
        clone.PreMultiplyAdditive = source.PreMultiplyAdditive;
        clone.PostMultiplyAdditive = source.PostMultiplyAdditive;
        return clone;
    }

    // ------------------------------------------------------------------
    // 校验
    // ------------------------------------------------------------------

    /// <summary>全量往返校验：写出→真加载器读回。返回错误列表（空 = 通过）。</summary>
    public List<string> Validate(GasNetDataLoadOptions options)
    {
        var errors = new List<string>();
        if (Effects.Any(entry => entry.Name.Length == 0))
            errors.Add("效果名不能为空。");
        if (Effects.GroupBy(e => e.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } dup)
            errors.Add($"效果名重复：'{dup.Key}'。");

        try
        {
            var json = GasNetDataWriter.WriteCatalog(
                Effects.Select(entry => new KeyValuePair<string, GameplayEffectDefinition>(entry.Name, entry.Def)));
            var reloaded = GasNetDataLoader.LoadCatalog(json, options);
            foreach (var entry in Effects)
                if (!reloaded.Effects.ContainsKey(entry.Name))
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
