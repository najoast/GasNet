#nullable enable

using System.Text.Json;
using GasNet;
using GasNet.Data;
using UnityEngine;

namespace UnityDemo.CueActions
{
    /// <summary>
    /// 在目标位置播一个 AudioClip。
    /// 字段：
    ///   clipPath     = "Assets/Audio/Hit.wav"      Unity AudioClip 资源路径
    ///   volume       = 1.0                         0..1
    ///   setByCaller  = "Data.Damage"               可选：把 spec 的 SetByCaller 数值当作音量倍率
    ///   spatialBlend = 0                           0 = 2D，1 = 3D
    /// </summary>
    public sealed class AudioCueAction : CueAction
    {
        public string ClipPath { get; set; } = "";
        public float Volume { get; set; } = 1f;
        public string? SetByCaller { get; set; }
        public float SpatialBlend { get; set; }

        public override void Execute(GameplayCueParameters parameters)
        {
            if (string.IsNullOrEmpty(ClipPath))
            {
                GasNetLog.Warn("AudioCueAction: empty clipPath; skipping.");
                return;
            }
            var clip = Resources.Load<AudioClip>(ClipPath);
            if (clip is null)
            {
                GasNetLog.Warn($"AudioCueAction: clip '{ClipPath}' not loadable. " +
                               "Hint: place the clip in any Resources/ folder.");
                return;
            }
            var pos = (parameters.Target as Component)?.transform.position ?? Vector3.zero;
            var v = Volume;
            if (!string.IsNullOrEmpty(SetByCaller) && parameters.EffectSpec is not null)
            {
                var tag = GameplayTag.RequestGameplayTag(SetByCaller!);
                if (tag.IsValid && parameters.EffectSpec.HasSetByCallerMagnitude(tag))
                    v *= parameters.EffectSpec.GetSetByCallerMagnitude(tag, warnIfNotFound: false);
            }
            AudioSource.PlayClipAtPoint(clip, pos, v);
        }

        public static readonly CueActionSchema Schema = new()
        {
            Reader = (action, el) =>
            {
                var self = (AudioCueAction)action;
                if (el.TryGetProperty("clipPath", out var p) && p.ValueKind == JsonValueKind.String)
                    self.ClipPath = p.GetString() ?? "";
                if (el.TryGetProperty("volume", out var v) && v.ValueKind == JsonValueKind.Number)
                    self.Volume = (float)v.GetDouble();
                if (el.TryGetProperty("setByCaller", out var s) && s.ValueKind == JsonValueKind.String)
                    self.SetByCaller = s.GetString();
                if (el.TryGetProperty("spatialBlend", out var b) && b.ValueKind == JsonValueKind.Number)
                    self.SpatialBlend = (float)b.GetDouble();
            },
            Writer = (action, node) =>
            {
                var self = (AudioCueAction)action;
                node["clipPath"] = self.ClipPath;
                if (self.Volume != 1f) node["volume"] = self.Volume;
                if (!string.IsNullOrEmpty(self.SetByCaller)) node["setByCaller"] = self.SetByCaller;
                if (self.SpatialBlend != 0f) node["spatialBlend"] = self.SpatialBlend;
            },
        };
    }
}
