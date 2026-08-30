#nullable enable

using System;
using System.IO;
using GasNet;
using GasNet.Data;
using UnityEngine;

namespace UnityDemo
{
    /// <summary>示例用标签：SetByCaller 数据标签 + Cue 标签（必须挂在 GameplayCue. 根下）。</summary>
    public static class DemoTags
    {
        public static readonly GameplayTag DataDamage = GameplayTag.RequestGameplayTag("Data.Damage");
        public static readonly GameplayTag CueHit = GameplayTag.RequestGameplayTag("GameplayCue.Combat.Hit");
    }

    /// <summary>
    /// 属性集：公开的 GameplayAttributeData 字段会被核心库自动注册（反射），
    /// 对应 UE 的 UAttributeSet 子类 + ATTRIBUTE_ACCESSORS。
    /// IL2CPP 下字段名依赖 link.xml 保留（见 Assets/link.xml 与根 README"接入 Unity"）。
    /// </summary>
    public class BattleAttributeSet : AttributeSet
    {
        public GameplayAttributeData MaxHealth;
        public GameplayAttributeData Health;
        public GameplayAttributeData AttackPower;

        private static GameplayAttribute A(string name)
        {
            return GameplayAttributeRegistry.TryGetAttribute(typeof(BattleAttributeSet), name, out var attribute)
                ? attribute
                : throw new InvalidOperationException($"Missing attribute '{name}'.");
        }

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

    /// <summary>
    /// 数据驱动的 GE 定义（Assets/StreamingAssets/Data/BattleGE.json），对应 UE 的 GE 资产——
    /// 数值策划改 JSON 即可调参，无需重编译。属性引用格式 "SetTypeName.AttributeName"，
    /// 标签字符串按核心库的运行时注册模型自动注册。
    /// </summary>
    public static class BattleData
    {
        public static GameplayEffectDefinition InitStatsHero = null!;
        public static GameplayEffectDefinition InitStatsEnemy = null!;
        public static GameplayEffectDefinition Damage = null!;

        public static void Load()
        {
            var options = new GasNetDataLoadOptions().RegisterAttributeSet<BattleAttributeSet>();
            var path = Path.Combine(Application.streamingAssetsPath, "Data", "BattleGE.json");
            var catalog = GasNetDataLoader.LoadCatalogFile(path, options);
            InitStatsHero = catalog.Get("GE_InitStats_Hero");
            InitStatsEnemy = catalog.Get("GE_InitStats_Enemy");
            Damage = catalog.Get("GE_Damage");
        }
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

            var spec = MakeAbilityEffectSpec(BattleData.Damage);
            // Damage 是对 Health 的 Add 修饰符：Add 直接改 BaseValue，伤害必须传负数（正数 = 治疗）
            spec.SetSetByCallerMagnitude(DemoTags.DataDamage,
                -OwnerASC!.GetNumericAttribute(BattleAttributeSet.AttackPowerAttr));
            ApplyGameplayEffectSpecToTarget(spec, target.ASC);

            EndAbility(wasCancelled: false);
        }
    }
}
