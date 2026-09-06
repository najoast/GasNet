#nullable enable

using System.Text.Json;
using GasNet;
using GasNet.Data;
using UnityEngine;

namespace UnityDemo.CueActions
{
    /// <summary>
    /// 简化的相机抖动：把 <see cref="Camera.main"/> 位置加上随机偏移。
    /// 字段：
    ///   intensity   = 0.4   0..1，振幅（米）
    ///   durationMs  = 80    持续毫秒
    /// </summary>
    public sealed class ShakeCueAction : CueAction
    {
        public float Intensity { get; set; } = 0.4f;
        public float DurationMs { get; set; } = 80f;

        public override void Execute(GameplayCueParameters parameters)
        {
            var cam = Camera.main;
            if (cam is null)
            {
                GasNetLog.Warn("ShakeCueAction: Camera.main is null; skipping.");
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
}
