using System.Globalization;
using GasNet;

namespace GasNet.Editor;

/// <summary>编辑器 UI 的小工具：标签文本 ↔ 容器、数字/枚举解析（InvariantCulture）。</summary>
public static class Ui
{
    /// <summary>容器 → 空格分隔的标签名（null 或空容器 → 空字符串）。</summary>
    public static string ToText(GameplayTagContainer? container) =>
        container is null ? "" : string.Join(" ", container.Tags.Select(t => t.Name));

    /// <summary>文本 → 容器；无有效标签返回 null（调用方以此区分"未设置"）。</summary>
    public static GameplayTagContainer? ParseTags(string? text)
    {
        var names = Split(text);
        if (names.Length == 0)
            return null;
        var container = new GameplayTagContainer();
        foreach (var name in names)
            container.AddTag(GameplayTag.RequestGameplayTag(name, warnIfNotFound: false));
        return container;
    }

    /// <summary>文本 → 已注册的单个标签（无效输入 → None）。</summary>
    public static GameplayTag ParseTag(string? text) =>
        Split(text) is { Length: 1 } names
            ? GameplayTag.RequestGameplayTag(names[0], warnIfNotFound: false)
            : GameplayTag.None;

    /// <summary>清空后填入（用于 get-only 的 Requirements 内部容器）。</summary>
    public static void FillTags(GameplayTagContainer target, string? text)
    {
        target.Clear();
        if (ParseTags(text) is { } parsed)
            target.AddTags(parsed);
    }

    private static string[] Split(string? text) =>
        (text ?? "").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static float F(string? text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;

    public static int I(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public static T Enum<T>(string? text) where T : struct, Enum =>
        System.Enum.TryParse(text, ignoreCase: true, out T value) ? value : default;
}
