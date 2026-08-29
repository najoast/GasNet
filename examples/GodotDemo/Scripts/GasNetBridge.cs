using GasNet;
using Godot;

namespace GodotDemo;

/// <summary>
/// 接缝 1 —— 时间：把 Godot 引擎时钟接进 GasNet 的 ITimeSource。
/// 所有 ASC 共享同一个引擎时间，保证时长/冷却一致。
/// </summary>
public sealed class GodotTimeSource : ITimeSource
{
	public float NowSeconds => Time.GetTicksMsec() / 1000f;
}

/// <summary>
/// 接缝 2 —— 宿主：任何 Node 都可以这样包一层成为 GAS 宿主。
/// 节点持有 ASC（对应 UE 里 Character 持有 ASC），每帧驱动 Tick，
/// _Draw 只负责把属性值画出来（表现层，与核心库无关）。
/// </summary>
public abstract partial class GasActor : Node2D, IAbilitySystemInterface
{
	public AbilitySystemComponent ASC { get; } = new();

	private float _flash;

	public AbilitySystemComponent? GetAbilitySystemComponent() => ASC;

	/// <summary>这个单位当前要打谁（由场景组织决定，核心库不关心）。</summary>
	public abstract GasActor? CurrentTarget { get; set; }

	public float Health => ASC.GetNumericAttribute(BattleAttributeSet.HealthAttr);
	public bool IsDead => Health <= 0f;

	public override void _Ready()
	{
		// 引擎时钟注入 + ActorInfo 初始化（Owner = Avatar = 本节点）
		ASC.TimeSource = new GodotTimeSource();
		ASC.InitAbilityActorInfo(this, this);
		OnReady();
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		// 接缝 3 —— 心跳：GasNet 不自带 Update，由宿主每帧驱动时长/周期/能力 Tick。
		ASC.Tick((float)delta);

		if (_flash > 0f)
		{
			_flash = Math.Max(0f, _flash - (float)delta);
			QueueRedraw();
		}
	}

	/// <summary>受击闪白，由 GameplayCue 触发（见 HitCueNotify）。</summary>
	public void Flash()
	{
		_flash = 0.15f;
		QueueRedraw();
	}

	public override void _Draw()
	{
		DrawCircle(Vector2.Zero, 26f, _flash > 0f ? Colors.White : BodyColor);
		DrawString(ThemeDB.FallbackFont, new Vector2(-44f, -38f),
			$"{Name}  {Health:0.#}", HorizontalAlignment.Left, -1, 15, Colors.White);
	}

	protected abstract void OnReady();
	protected abstract Color BodyColor { get; }
}

/// <summary>
/// 接缝 4 —— 表现：Cue 通知适配器。核心库只路由 GameplayCue 事件，
/// 粒子/音效/闪白都由引擎侧的 Notify 子类实现。
/// </summary>
public sealed class HitCueNotify : GameplayCueNotify_Static
{
	public HitCueNotify() => GameplayCueTags.AddTag(DemoTags.CueHit);

	public override void OnExecute(GameplayCueParameters parameters)
	{
		if (parameters.Target is GasActor actor)
		{
			actor.Flash();
			GD.Print($"[Cue] {actor.Name} hit! {parameters.Magnitude:0.#} HP ({actor.Health:0.#} left)");
		}
	}
}
