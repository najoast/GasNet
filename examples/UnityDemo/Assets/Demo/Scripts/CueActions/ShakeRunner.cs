#nullable enable

using System.Collections;
using GasNet;
using UnityEngine;

namespace UnityDemo.CueActions
{
    /// <summary>
    /// 简化版相机抖动：MonoBehaviour 协程驱动衰减。
    /// 多条 ShakeCueAction 同时触发时，新冲量叠加在已有 shake 上（不重启协程）。
    /// </summary>
    public sealed class ShakeRunner : MonoBehaviour
    {
        private static ShakeRunner? _instance;
        private float _remaining;
        private float _intensity;
        private Vector3 _origin;

        public static void Shake(Camera cam, float intensity, float duration)
        {
            if (_instance is null)
            {
                var go = new GameObject("__ShakeRunner");
                _instance = go.AddComponent<ShakeRunner>();
            }
            _instance.AttachTo(cam);
            _instance.AddImpulse(intensity, duration);
        }

        private Camera? _camera;

        private void AttachTo(Camera cam)
        {
            if (_camera == cam) return;
            _camera = cam;
            _origin = cam.transform.localPosition;
        }

        private void AddImpulse(float intensity, float duration)
        {
            // 累加强度（不重置时间），多条 cue 叠加更猛烈。
            _intensity = Mathf.Max(_intensity, intensity);
            _remaining = Mathf.Max(_remaining, duration);
        }

        private void Update()
        {
            if (_camera is null || _remaining <= 0f) return;

            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                _camera.transform.localPosition = _origin;
                return;
            }

            var t = _remaining * _intensity;
            _camera.transform.localPosition = _origin + (Vector3)(Random.insideUnitSphere * t);
        }
    }
}
