#nullable enable

using System.Text.Json;
using GasNet;
using GasNet.Data;
using UnityEngine;

namespace UnityDemo.CueActions
{
    /// <summary>
    /// 引擎无关基类动作：在宿主（参数 Target）上调用受击闪白。
    /// 字段：
    ///   durationMs = 150         持续毫秒（默认 150ms = 现有 GasActor.Flash() 行为）
    ///   color       = "#FFFFFF"  16 进制 RGB；省略 = 保留原 sprite 颜色不变（只用 timer 强制刷新）
    ///   target      = "self"|"instigator"  默认可省 = self（cue 的 Target）
    /// </summary>
    public sealed class FlashCueAction : CueAction
    {
        public float DurationMs { get; set; } = 150f;
        public string? Color { get; set; }
        public string Target { get; set; } = "self";

        public override void Execute(GameplayCueParameters parameters)
        {
            var actor = ResolveActor(parameters);
            if (actor is null) return;

            actor.Flash(DurationMs, ParseColor(Color));
        }

        private static GasActor? ResolveActor(GameplayCueParameters parameters)
        {
            // Cue actions currently only know "self" (the cue target). Future hookup for
            // "instigator" can read parameters.Instigator when abilities stamp a real reference.
            return parameters.Target as GasActor;
        }

        private static Color ParseColor(string? hex)
        {
            if (string.IsNullOrEmpty(hex)) return new Color(1f, 1f, 1f, 1f);
            if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            GasNetLog.Warn($"FlashCueAction: invalid color '{hex}'; using white.");
            return new Color(1f, 1f, 1f, 1f);
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
                if (el.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.String)
                    self.Target = t.GetString() ?? "self";
            },
            Writer = (action, node) =>
            {
                var self = (FlashCueAction)action;
                if (self.DurationMs != 150f) node["durationMs"] = self.DurationMs;
                if (!string.IsNullOrEmpty(self.Color)) node["color"] = self.Color;
                if (self.Target != "self") node["target"] = self.Target;
            },
        };
    }
}
