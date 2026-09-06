using System.Text;
using System.Text.Json;
using GasNet;
using GasNet.Data;
using Godot;

namespace GodotDemo.CueActions;

/// <summary>
/// 把 cue 事件写到 Godot 输出面板。
/// 字段：
///   level  = "Info" | "Warn" | "Error"   默认 "Info"
///   format = "[Cue] {tag} {target} hit! {magnitude:0.#} HP"
/// 占位符解析（不引入模板引擎）：失败的占位符保留原样。
/// </summary>
public sealed class LogCueAction : CueAction
{
    public string Level { get; set; } = "Info";
    public string Format { get; set; } = "[Cue] {target}";

    public override void Execute(GameplayCueParameters parameters)
    {
        var msg = Resolve(Format, parameters);
        switch (Level)
        {
            case "Warn": GD.PushWarning(msg); break;
            case "Error": GD.PushError(msg); break;
            default: GD.Print(msg); break;
        }
    }

    private static string Resolve(string format, GameplayCueParameters p)
    {
        var sb = new StringBuilder(format);
        // {tag} 占位符未实现：GameplayEffectDefinition 没有 Name 字段，cue 的来源 tag 也不
        // 单独暴露（要拿的话走 AggregatedCueTags.First()，本 demo 用不到）。
        Replace(sb, "{magnitude:0.#}", p.Magnitude.ToString("0.#"));
        Replace(sb, "{magnitude}", p.Magnitude.ToString("0.##"));
        Replace(sb, "{target}", p.Target?.ToString() ?? "(no-target)");
        Replace(sb, "{instigator}", p.Instigator?.ToString() ?? "(no-instigator)");
        return sb.ToString();
    }

    private static void Replace(StringBuilder sb, string token, string value)
    {
        var s = sb.ToString();
        var idx = s.IndexOf(token, System.StringComparison.Ordinal);
        if (idx < 0) return;
        sb.Remove(idx, token.Length);
        sb.Insert(idx, value);
    }

    public static readonly CueActionSchema Schema = new()
    {
        Reader = (action, el) =>
        {
            var self = (LogCueAction)action;
            if (el.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.String)
                self.Level = l.GetString() ?? "Info";
            if (el.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String)
                self.Format = f.GetString() ?? self.Format;
        },
        Writer = (action, node) =>
        {
            var self = (LogCueAction)action;
            if (self.Level != "Info") node["level"] = self.Level;
            if (self.Format != "[Cue] {target}") node["format"] = self.Format;
        },
    };
}
