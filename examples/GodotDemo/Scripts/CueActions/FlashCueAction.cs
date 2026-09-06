using System.Text.Json;
using GasNet;
using GasNet.Data;
using Godot;

namespace GodotDemo.CueActions;

/// <summary>
/// 在目标 <see cref="GasActor"/> 上调用 <c>Flash(durationMs, color)</c>。
/// 字段：
///   durationMs = 150     持续毫秒
///   color      = "#FFFFFF"  16 进制 RGB；省略 = 白色
/// </summary>
public sealed class FlashCueAction : CueAction
{
    public float DurationMs { get; set; } = 150f;
    public string? Color { get; set; }

    public override void Execute(GameplayCueParameters parameters)
    {
        if (parameters.Target is not GasActor actor) return;
        actor.Flash(DurationMs, ParseColor(Color));
    }

    private static Color ParseColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return Colors.White;
        // Godot's Color has a (string) constructor that throws on bad input; no TryParse.
        try
        {
            return new Color(hex);
        }
        catch
        {
            GasNetLog.Warn($"FlashCueAction: invalid color '{hex}'; using white.");
            return Colors.White;
        }
    }

    public static readonly CueActionSchema Schema = new()
    {
        Reader = (action, el) =>
        {
            var self = (FlashCueAction)action;
            if (el.TryGetProperty("durationMs", out var d) && d.ValueKind == JsonValueKind.Number)
                self.DurationMs = (float)d.GetDouble();
            if (el.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String)
                self.Color = c.GetString();
        },
        Writer = (action, node) =>
        {
            var self = (FlashCueAction)action;
            if (self.DurationMs != 150f) node["durationMs"] = self.DurationMs;
            if (!string.IsNullOrEmpty(self.Color)) node["color"] = self.Color;
        },
    };
}
