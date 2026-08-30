using GasNet;
using Godot;

namespace GodotDemo;

/// <summary>
/// 场景根：组装两个由 GasNet 驱动的单位，把输入与日志接到 ASC 上。
/// 空格 = 英雄攻击；敌人每 3 秒自动反击。
/// </summary>
public sealed partial class Main : Node2D
{
	private Hero _hero = null!;
	private Enemy _enemy = null!;
	private Label _log = null!;
	private readonly List<string> _lines = [];

	public override void _Ready()
	{
		_log = GetNode<Label>("Log");
		GasNetLog.OnWarn = GD.PushWarning; // 核心库日志 → Godot 输出面板
		BattleData.Load(); // GE 定义来自 Data/BattleGE.json（数据驱动层示例）

		AbilitySystemGlobals.Get().GameplayCueManager.RegisterNotify(new HitCueNotify());

		_hero = new Hero();
		AddChild(_hero);
		_enemy = new Enemy(_hero);
		AddChild(_enemy);
		_hero.CurrentTarget = _enemy;

		foreach (var actor in new GasActor[] { _hero, _enemy })
		{
			var watched = actor;
			watched.ASC
				.GetGameplayAttributeValueChangeDelegate(BattleAttributeSet.HealthAttr)
				.Handler += _ => OnHealthChanged(watched);
		}

		Log("GasNet demo started — press SPACE to attack.");
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		// 原始按键事件，无需在 Input Map 里配置。若改用动作（如自定义 "attack"），
		// 则需在 Input Map 添加映射，并把判断换成 @event.IsActionPressed("attack")。
		if (@event is InputEventKey { Pressed: true, Echo: false } key
			&& (key.Keycode == Key.Space || key.PhysicalKeycode == Key.Space)
			&& !_hero.IsDead && !_enemy.IsDead)
		{
			_hero.ASC.AbilityLocalInputPressed(1);
		}
	}

	private void OnHealthChanged(GasActor actor)
	{
		if (actor.IsDead)
			Log($"{actor.Name} is DEAD — {(actor == _hero ? "DEFEAT" : "VICTORY")}!");
		else
			Log($"{actor.Name} HP → {actor.Health:0.#}");
	}

	private void Log(string line)
	{
		_lines.Add(line);
		if (_lines.Count > 10)
			_lines.RemoveAt(0);
		_log.Text = string.Join("\n", _lines);
	}
}

public sealed partial class Hero : GasActor
{
	public override GasActor? CurrentTarget { get; set; }

	protected override void OnReady()
	{
		Name = "Hero";
		Position = new Vector2(250, 340);
		ASC.AddSet<BattleAttributeSet>();
		ASC.ApplyGameplayEffectToSelf(BattleData.InitStatsHero);
		ASC.GiveAbility(new GameplayAbilitySpec(new AttackAbility(), level: 1, inputID: 1));
	}

	protected override Color BodyColor => new("4aa3ff");
}

public sealed partial class Enemy : GasActor
{
	private float _autoAttackTimer = 3f;

	public Enemy(GasActor? target) => CurrentTarget = target;

	public override GasActor? CurrentTarget { get; set; }

		protected override void OnReady()
		{
			Name = "Enemy";
			Position = new Vector2(850, 340);
			ASC.AddSet<BattleAttributeSet>();
			ASC.ApplyGameplayEffectToSelf(BattleData.InitStatsEnemy);
			// 自动反击同样走输入路径，必须把能力绑到 InputID=1（此前缺省为 0，敌人从未反击成功）
			ASC.GiveAbility(new GameplayAbilitySpec(new AttackAbility(), level: 1, inputID: 1));
		}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (CurrentTarget is { IsDead: false } && IsDead == false && (_autoAttackTimer -= (float)delta) <= 0f)
		{
			_autoAttackTimer = 3f;
			ASC.AbilityLocalInputPressed(1);
		}
	}

	protected override Color BodyColor => new("ff5a5a");
}
