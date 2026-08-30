#nullable enable

using GasNet;
using UnityEngine;

namespace UnityDemo
{
    /// <summary>英雄：属性初始化 + 攻击能力（InputID=1），由 Main 把空格键接进来。</summary>
    public sealed class Hero : GasActor
    {
        public override GasActor? CurrentTarget { get; set; }

        protected override void OnReady()
        {
            ASC.AddSet<BattleAttributeSet>();
            ASC.ApplyGameplayEffectToSelf(BattleData.InitStatsHero);
            ASC.GiveAbility(new GameplayAbilitySpec(new AttackAbility(), level: 1, inputID: 1));
        }

        protected override Color BodyColor => new Color(0.29f, 0.64f, 1f);
    }
}
