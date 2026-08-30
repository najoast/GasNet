#nullable enable

using GasNet;
using UnityEngine;

namespace UnityDemo
{
    /// <summary>
    /// 接缝 1 —— 时间：把 Unity 引擎时钟接进 GasNet 的 ITimeSource。
    /// 所有 ASC 共享同一个引擎时间，保证时长/冷却一致。
    /// </summary>
    public sealed class UnityTimeSource : ITimeSource
    {
        public float NowSeconds => Time.time;
    }

    /// <summary>
    /// 接缝 2 —— 宿主：任何 MonoBehaviour 都可以这样包一层成为 GAS 宿主。
    /// 组件持有 ASC（对应 UE 里 Character 持有 ASC），每帧驱动 Tick，
    /// OnGUI 只负责把名字和血量画出来（表现层，与核心库无关）。
    /// </summary>
    public abstract class GasActor : MonoBehaviour, IAbilitySystemInterface
    {
        public AbilitySystemComponent ASC { get; } = new AbilitySystemComponent();

        private float _flash;
        private SpriteRenderer _body = null!;
        private GUIStyle? _labelStyle;
        private static Sprite? _circleSprite;

        public AbilitySystemComponent? GetAbilitySystemComponent() => ASC;

        /// <summary>这个单位当前要打谁（由场景组织决定，核心库不关心）。</summary>
        public abstract GasActor? CurrentTarget { get; set; }

        public float Health => ASC.GetNumericAttribute(BattleAttributeSet.HealthAttr);
        public bool IsDead => Health <= 0f;

        protected abstract void OnReady();
        protected abstract Color BodyColor { get; }

        protected virtual void Awake()
        {
            _body = gameObject.AddComponent<SpriteRenderer>();
            _body.sprite = GetOrCreateCircleSprite();
        }

        protected virtual void Start()
        {
            // 引擎时钟注入 + ActorInfo 初始化（Owner = Avatar = 本组件）
            ASC.TimeSource = new UnityTimeSource();
            ASC.InitAbilityActorInfo(this, this);
            OnReady();
        }

        protected virtual void Update()
        {
            // 接缝 3 —— 心跳：GasNet 不自带 Update，由宿主每帧驱动时长/周期/能力 Tick。
            ASC.Tick(Time.deltaTime);

            if (_flash > 0f)
                _flash = Mathf.Max(0f, _flash - Time.deltaTime);

            _body.color = _flash > 0f
                ? Color.white
                : IsDead ? new Color(0.3f, 0.3f, 0.3f) : BodyColor;
        }

        /// <summary>受击闪白，由 GameplayCue 触发（见 HitCueNotify）。</summary>
        public void Flash()
        {
            _flash = 0.15f;
        }

        private void OnGUI()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    alignment = TextAnchor.MiddleCenter,
                };
                _labelStyle.normal.textColor = Color.white;
            }

            var cam = Camera.main;
            if (cam is null)
                return;

            var screen = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.6f);
            if (screen.z < 0f)
                return;

            var label = $"{name}  {Health:0.#}";
            var size = _labelStyle.CalcSize(new GUIContent(label));
            var y = Screen.height - screen.y - size.y * 0.5f;
            GUI.Label(new Rect(screen.x - size.x * 0.5f, y, size.x, size.y), label, _labelStyle);
        }

        // 运行时生成圆形 sprite：演示工程里没有任何美术资产
        private static Sprite GetOrCreateCircleSprite()
        {
            if (_circleSprite == null)
                _circleSprite = BuildCircleSprite();
            return _circleSprite!;
        }

        private static Sprite BuildCircleSprite()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            var radius = size * 0.5f - 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    pixels[y * size + x] = dx * dx + dy * dy <= radius * radius
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(0, 0, 0, 0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            // pixelsPerUnit = size / 4 → 圆的直径为 4 个世界单位
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size / 4f);
            sprite.name = "GasActorCircle";
            return sprite;
        }
    }

    /// <summary>
    /// 接缝 4 —— 表现：Cue 通知适配器。核心库只路由 GameplayCue 事件，
    /// 粒子/音效/闪白都由引擎侧的 Notify 子类实现。
    /// </summary>
    public sealed class HitCueNotify : GameplayCueNotify_Static
    {
        public HitCueNotify()
        {
            GameplayCueTags.AddTag(DemoTags.CueHit);
        }

        public override void OnExecute(GameplayCueParameters parameters)
        {
            if (parameters.Target is GasActor actor)
            {
                actor.Flash();
                Debug.Log($"[Cue] {actor.name} hit! {parameters.Magnitude:0.#} HP ({actor.Health:0.#} left)");
            }
        }
    }
}
