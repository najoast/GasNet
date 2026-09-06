using System.Text.Json;
using GasNet;
using GasNet.Data;
using Godot;

namespace GodotDemo.CueActions;

/// <summary>
/// 简化版相机抖动：把 <c>Camera2D</c> 位置加上随机偏移（2D 场景）。
/// 字段：
///   intensity  = 0.4   0..1，振幅（像素）
///   durationMs = 80    持续毫秒
/// </summary>
public sealed class ShakeCueAction : CueAction
{
    public float Intensity { get; set; } = 0.4f;
    public float DurationMs { get; set; } = 80f;

    public override void Execute(GameplayCueParameters parameters)
    {
        // 找场景里第一个 Camera2D；Godot 4 场景节点没有硬性要求 "main camera" 的概念。
        var cam = (parameters.Target as Node)?.GetTree()?.Root?.FindChild("Camera2D", recursive: true, owned: false) as Camera2D;
        if (cam is null)
        {
            GasNetLog.Warn("ShakeCueAction: no Camera2D in the scene; skipping.");
            return;
        }
        ShakeRunner.Shake(cam, Intensity, DurationMs * 0.001f);
    }

    public static readonly CueActionSchema Schema = new()
    {
        Reader = (action, el) =>
        {
            var self = (ShakeCueAction)action;
            if (el.TryGetProperty("intensity", out var i) && i.ValueKind == JsonValueKind.Number)
                self.Intensity = (float)i.GetDouble();
            if (el.TryGetProperty("durationMs", out var d) && d.ValueKind == JsonValueKind.Number)
                self.DurationMs = (float)d.GetDouble();
        },
        Writer = (action, node) =>
        {
            var self = (ShakeCueAction)action;
            if (self.Intensity != 0.4f) node["intensity"] = self.Intensity;
            if (self.DurationMs != 80f) node["durationMs"] = self.DurationMs;
        },
    };
}
