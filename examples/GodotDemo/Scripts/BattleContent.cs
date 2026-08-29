using GasNet;

namespace GodotDemo;

/// <summary>示例用标签：SetByCaller 数据标签 + Cue 标签（必须挂在 GameplayCue. 根下）。</summary>
public static class DemoTags
{
	public static readonly GameplayTag DataDamage = GameplayTag.RequestGameplayTag("Data.Damage");
	public static readonly GameplayTag CueHit = GameplayTag.RequestGameplayTag("GameplayCue.Combat.Hit");
}

/// <summary>
/// 属性集：公开的 GameplayAttributeData 字段会被核心库自动注册（反射），
/// 对应 UE 的 UAttributeSet 子类 + ATTRIBUTE_ACCESSORS。
/// </summary>
public class BattleAttributeSet : AttributeSet
{
	public GameplayAttributeData MaxHealth;
	public GameplayAttributeData Health;
	public GameplayAttributeData AttackPower;

	private static GameplayAttribute A(string name) =>
		GameplayAttributeRegistry.TryGetAttribute(typeof(BattleAttributeSet), name, out var attribute)
			? attribute
			: throw new InvalidOperationException($"Missing attribute '{name}'.");

	public static readonly GameplayAttribute HealthAttr = A("Health");
	public static readonly GameplayAttribute MaxHealthAttr = A("MaxHealth");
	public static readonly GameplayAttribute AttackPowerAttr = A("AttackPower");

	// 钳制 CurrentValue（每次查询生效，不改修饰符本身——文档 §4.4.5）
	public override void PreAttributeChange(GameplayAttribute attribute, ref float newValue)
	{
		if (attribute == HealthAttr)
			newValue = Math.Clamp(newValue, 0f, MaxHealth.CurrentValue);
	}

	// 钳制 BaseValue（Instant GE 直接写 Base，防止血量越钳越负）
	public override void PreAttributeBaseChange(GameplayAttribute attribute, ref float newValue)
	{
		if (attribute == HealthAttr)
			newValue = Math.Clamp(newValue, 0f, MaxHealth.BaseValue);
	}
}

/// <summary>GE 定义：纯数据原型，对应 UE 的蓝图 GE 子类。</summary>
public static class BattleGE
{
	/// <summary>初始化属性：Epic 推荐用 Instant GE（文档 §4.4.4）。</summary>
	public static GameplayEffectDefinition MakeInitStats(float maxHealth, float attackPower) =>
		new GameplayEffectDefinition()
			.With(policy: GameplayEffectDurationType.Instant)
			.AddModifier(BattleAttributeSet.MaxHealthAttr, GameplayModOp.Override, new ScalableFloatMagnitude(maxHealth))
			.AddModifier(BattleAttributeSet.HealthAttr, GameplayModOp.Override, new ScalableFloatMagnitude(maxHealth))
			.AddModifier(BattleAttributeSet.AttackPowerAttr, GameplayModOp.Override, new ScalableFloatMagnitude(attackPower));

	/// <summary>瞬发伤害：数值由 SetByCaller 在运行时携带；成功命中自动触发 Executed Cue。</summary>
	public static readonly GameplayEffectDefinition Damage = new GameplayEffectDefinition()
		.With(policy: GameplayEffectDurationType.Instant)
		.WithCueTags(DemoTags.CueHit)
		.AddModifier(BattleAttributeSet.HealthAttr, GameplayModOp.Add, new SetByCallerMagnitude(DemoTags.DataDamage));
}

/// <summary>
/// 一个最普通的攻击能力：从攻击者属性读攻击力 → SetByCaller → 对目标 ASC 应用伤害 GE。
/// InputID=1 绑定，由 ASC.AbilityLocalInputPressed(1) 激活。
/// </summary>
public sealed class AttackAbility : GameplayAbility
{
	protected override void ActivateAbility()
	{
		if (CurrentActorInfo.Avatar is not GasActor attacker
			|| attacker.CurrentTarget is not { IsDead: false } target)
		{
			EndAbility(wasCancelled: false);
			return;
		}

		var spec = MakeAbilityEffectSpec(BattleGE.Damage);
		// Damage 是对 Health 的 Add 修饰符：Add 直接改 BaseValue，伤害必须传负数（正数 = 治疗）
		spec.SetSetByCallerMagnitude(DemoTags.DataDamage,
			-OwnerASC!.GetNumericAttribute(BattleAttributeSet.AttackPowerAttr));
		ApplyGameplayEffectSpecToTarget(spec, target.ASC);

		EndAbility(wasCancelled: false);
	}
}
