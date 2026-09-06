#nullable enable

using System.Text.Json;
using GasNet;
using GasNet.Data;
using UnityEngine;

namespace UnityDemo.CueActions
{
    /// <summary>
    /// 在目标位置实例化一个 prefab 表现（VFX/粒子/弹道等）。
    /// 字段：
    ///   prefabPath = "Assets/VFX/HitImpact.prefab"   Unity 资源路径（必须以 Assets/ 开头）
    ///   parent     = "self"|"world"                  默认 "self"（attach 到目标）
    ///   durationMs = 0                              0 = 永久；> 0 = 倒计时销毁
    /// </summary>
    public sealed class VfxCueAction : CueAction
    {
        public string PrefabPath { get; set; } = "";
        public string Parent { get; set; } = "self";
        public float DurationMs { get; set; }

        public override void Execute(GameplayCueParameters parameters)
        {
            if (string.IsNullOrEmpty(PrefabPath))
            {
                GasNetLog.Warn("VfxCueAction: empty prefabPath; skipping.");
                return;
            }
            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab is null)
            {
                GasNetLog.Warn($"VfxCueAction: prefab '{PrefabPath}' not loadable. " +
                               "Hint: place the prefab in any Resources/ folder, " +
                               "or call AssetDatabase.LoadAssetAtPath in Editor code.");
                return;
            }
            var target = parameters.Target as Component;
            var parent = Parent == "self" && target is not null ? target.transform : null;
            var instance = Object.Instantiate(prefab, parent);
            if (DurationMs > 0f) Object.Destroy(instance, DurationMs * 0.001f);
        }

        public static readonly CueActionSchema Schema = new()
        {
            Reader = (action, el) =>
            {
                var self = (VfxCueAction)action;
                if (el.TryGetProperty("prefabPath", out var p) && p.ValueKind == JsonValueKind.String)
                    self.PrefabPath = p.GetString() ?? "";
                if (el.TryGetProperty("parent", out var pa) && pa.ValueKind == JsonValueKind.String)
                    self.Parent = pa.GetString() ?? "self";
                if (el.TryGetProperty("durationMs", out var d) && d.ValueKind == JsonValueKind.Number)
                    self.DurationMs = (float)d.GetDouble();
            },
            Writer = (action, node) =>
            {
                var self = (VfxCueAction)action;
                node["prefabPath"] = self.PrefabPath;
                if (self.Parent != "self") node["parent"] = self.Parent;
                if (self.DurationMs > 0f) node["durationMs"] = self.DurationMs;
            },
        };
    }
}
