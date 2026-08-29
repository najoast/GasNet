using System.Reflection;
using System.Runtime.Loader;
using GasNet;
using GasNet.Data;

namespace GasNet.Editor;

/// <summary>
/// "档案"：把游戏侧的托管程序集反射加载进来，发现 AttributeSet（属性下拉框）与可从 JSON
/// 引用的代码片段类型——ExecCalc / MMC / CAR / GameplayAbility（类型下拉框），并据此构建
/// 加载/校验用的 <see cref="GasNetDataLoadOptions"/>。
///
/// <para>类型同一性：游戏目录里若带有自己的 GasNet.dll 副本，直接加载会产生两套不相等的
/// Type（IsAssignableFrom 全部失效）。因此解析 GasNet/GasNet.Data 时返回 null 回落到编辑器
/// 自身副本，其余依赖才从游戏目录探测；引用宿主引擎类型（Godot Node / Unity 组件）的类会
/// 加载失败，按 ReflectionTypeLoadException 跳过并记录日志。</para>
/// </summary>
public sealed class EditorProfile
{
    private readonly Dictionary<string, GameplayAttribute> _attributeByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _typeByName = new(StringComparer.Ordinal);

    public List<string> Log { get; } = [];
    public List<(string Key, GameplayAttribute Attribute)> Attributes { get; } = [];
    public List<Type> Executions { get; } = [];
    public List<Type> Magnitudes { get; } = [];
    public List<Type> Requirements { get; } = [];
    public List<Type> Abilities { get; } = [];

    public string? LoadedAssembly { get; private set; }
    public bool HasProfile => LoadedAssembly is not null;
    public int AttributeSetCount => _attributeByKey.Values.Select(a => a.AttributeSetType).Distinct().Count();
    public int CalculableCount => Executions.Count + Magnitudes.Count + Requirements.Count + Abilities.Count;

    public void LoadAssembly(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"找不到程序集 '{fullPath}'。", fullPath);

        var gameDirectory = Path.GetDirectoryName(fullPath)!;
        // 不用 collectible：旧档案的 Type 可能仍被目录里的定义引用；重复加载旧的上下文随之闲置，开发工具可接受。
        var context = new AssemblyLoadContext($"GasNetEditor:{fullPath}");
        context.Resolving += (alc, assemblyName) =>
        {
            if (assemblyName.Name is "GasNet" or "GasNet.Data" or "System.Text.Json")
                return null; // 保持与编辑器相同的类型同一性
            var probe = Path.Combine(gameDirectory, assemblyName.Name + ".dll");
            return File.Exists(probe) ? alc.LoadFromAssemblyPath(probe) : null;
        };

        Assembly assembly;
        try
        {
            assembly = context.LoadFromAssemblyPath(fullPath);
        }
        catch (BadImageFormatException e)
        {
            throw new InvalidOperationException(
                $"'{fullPath}' 无法加载：架构或运行时不兼容（如 netstandard/framework 差异）。{e.Message}");
        }

        Attributes.Clear();
        Executions.Clear();
        Magnitudes.Clear();
        Requirements.Clear();
        Abilities.Clear();
        Log.Clear();
        _attributeByKey.Clear();
        _typeByName.Clear();

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = [.. e.Types.Where(t => t is not null).Select(t => t!)];
            var skipped = e.Types.Count(t => t is null);
            Log.Add($"跳过 {skipped} 个无法加载的类型（通常是引用宿主引擎类型的类，如 Godot Node / Unity 组件）。");
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || type.IsInterface || !type.IsVisible)
                continue;
            if (typeof(AttributeSet).IsAssignableFrom(type))
            {
                foreach (var attribute in GameplayAttributeRegistry.GetAttributes(type))
                {
                    Attributes.Add(($"{type.Name}.{attribute.Name}", attribute));
                    _attributeByKey[$"{type.Name}.{attribute.Name}"] = attribute;
                }
            }
            else if (typeof(GameplayEffectExecutionCalculation).IsAssignableFrom(type)) { Executions.Add(type); _typeByName[type.Name] = type; }
            else if (typeof(ModifierMagnitudeCalculation).IsAssignableFrom(type)) { Magnitudes.Add(type); _typeByName[type.Name] = type; }
            else if (typeof(GameplayEffectCustomApplicationRequirement).IsAssignableFrom(type)) { Requirements.Add(type); _typeByName[type.Name] = type; }
            else if (typeof(GameplayAbility).IsAssignableFrom(type)) { Abilities.Add(type); _typeByName[type.Name] = type; }
        }

        LoadedAssembly = fullPath;
        Log.Add($"已加载 {assembly.GetName().Name}（属性 {Attributes.Count} 个，可引用类型 {CalculableCount} 个）。" +
                "重新加载档案后建议重新打开目录文件。");
    }

    public GameplayAttribute? FindAttribute(string? key) =>
        key is not null && _attributeByKey.TryGetValue(key, out var attribute) ? attribute : null;

    public Type? FindType(string? name) =>
        name is not null && _typeByName.TryGetValue(name, out var type) ? type : null;

    public object? Instantiate(string? name) =>
        FindType(name) is { } type ? Activator.CreateInstance(type) : null;

    /// <summary>构建加载/校验选项：档案里发现的所有属性集与类型按简单名注册（与 Writer 输出的引用名一致）。</summary>
    public GasNetDataLoadOptions BuildOptions()
    {
        var options = new GasNetDataLoadOptions();
        foreach (var group in Attributes.GroupBy(a => a.Attribute.AttributeSetType))
            options.RegisterAttributeSet(group.Key);
        foreach (var type in Executions.Concat(Magnitudes).Concat(Requirements).Concat(Abilities))
            options.RegisterType(type);
        return options;
    }
}
