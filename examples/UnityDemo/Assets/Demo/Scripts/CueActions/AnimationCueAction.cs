#nullable enable

using System.Text.Json;
using GasNet;
using GasNet.Data;
using UnityEngine;

namespace UnityDemo.CueActions
{
    /// <summary>
    /// 在目标的 Animator 上播放一个 AnimationClip 资源。
    /// 字段：
    ///   clipPath = "Assets/Anims/HitReact.anim"   Unity AnimationClip 资源路径
    ///   layer    = 0                              Animator 层索引
    ///   normalizedTime = 0                        起始时间（0..1）
    /// </summary>
    public sealed class AnimationCueAction : CueAction
    {
        public string ClipPath { get; set; } = "";
        public int Layer { get; set; }
        public float NormalizedTime { get; set; }

        public override void Execute(GameplayCueParameters parameters)
        {
            if (string.IsNullOrEmpty(ClipPath))
            {
                GasNetLog.Warn("AnimationCueAction: empty clipPath; skipping.");
                return;
            }
            var clip = Resources.Load<AnimationClip>(ClipPath);
            if (clip is null)
            {
                GasNetLog.Warn($"AnimationCueAction: clip '{ClipPath}' not loadable. " +
                               "Hint: place the clip in any Resources/ folder.");
                return;
            }
            if (parameters.Target is not Component target) return;
            var animator = target.GetComponentInChildren<Animator>();
            if (animator is null)
            {
                GasNetLog.Warn($"AnimationCueAction: no Animator on '{target.name}'; skipping.");
                return;
            }
            animator.Play(clip.name, Layer, NormalizedTime);
        }

        public static readonly CueActionSchema Schema = new()
        {
            Reader = (action, el) =>
            {
                var self = (AnimationCueAction)action;
                if (el.TryGetProperty("clipPath", out var p) && p.ValueKind == JsonValueKind.String)
                    self.ClipPath = p.GetString() ?? "";
                if (el.TryGetProperty("layer", out var l) && l.ValueKind == JsonValueKind.Number)
                    self.Layer = l.GetInt32();
                if (el.TryGetProperty("normalizedTime", out var n) && n.ValueKind == JsonValueKind.Number)
                    self.NormalizedTime = (float)n.GetDouble();
            },
            Writer = (action, node) =>
            {
                var self = (AnimationCueAction)action;
                node["clipPath"] = self.ClipPath;
                if (self.Layer != 0) node["layer"] = self.Layer;
                if (self.NormalizedTime != 0f) node["normalizedTime"] = self.NormalizedTime;
            },
        };
    }
}
