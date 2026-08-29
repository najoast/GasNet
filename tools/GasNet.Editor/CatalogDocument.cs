using System.Text.Json;
using GasNet;
using GasNet.Data;

namespace GasNet.Editor;

/// <summary>
/// 编辑中的目录：有序的效果列表 + 脏标记。写和读只经过 GasNet.Data 的
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
    }

    public void Save(string path)
    {
        File.WriteAllText(path, GasNetDataWriter.WriteCatalog(
            Effects.Select(entry => new KeyValuePair<string, GameplayEffectDefinition>(entry.Name, entry.Def))));
        Path = path;
        Dirty = false;
        foreach (var entry in Effects)
            entry.SavedName = entry.Name; // 改名已落盘，撤销"旧引用"提示
    }

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
