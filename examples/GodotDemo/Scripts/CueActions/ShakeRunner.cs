using Godot;

namespace GodotDemo.CueActions;

/// <summary>
/// 简化版 2D 相机抖动：以 Camera2D 为目标，由 _Process 驱动衰减。
/// 多条 ShakeCueAction 同时触发时，新冲量叠加在已有 shake 上（不重启计时器）。
/// </summary>
public sealed partial class ShakeRunner : Camera2D
{
    private static ShakeRunner? _instance;
    private float _remaining;
    private float _intensity;
    private Vector2 _origin;

    public static void Shake(Camera2D cam, float intensity, float duration)
    {
        if (_instance is null)
        {
            _instance = new ShakeRunner { Name = "__ShakeRunner" };
            cam.GetTree().Root.CallDeferred(Node.MethodName.AddChild, _instance);
        }
        _instance.AttachTo(cam);
        _instance.AddImpulse(intensity, duration);
    }

    private Camera2D? _camera;

    private void AttachTo(Camera2D cam)
    {
        if (_camera == cam) return;
        _camera = cam;
        _origin = cam.Position;
    }

    private void AddImpulse(float intensity, float duration)
    {
        _intensity = Mathf.Max(_intensity, intensity);
        _remaining = Mathf.Max(_remaining, duration);
    }

    public override void _Process(double delta)
    {
        if (_camera is null || _remaining <= 0f) return;

        _remaining -= (float)delta;
        if (_remaining <= 0f)
        {
            _camera.Position = _origin;
            return;
        }

        var t = _remaining * _intensity;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _camera.Position = _origin + new Vector2(rng.RandfRange(-t, t), rng.RandfRange(-t, t));
    }
}
