#nullable enable

using GasNet;
using UnityEngine;

namespace UnityDemo
{
    /// <summary>敌人：属性初始化 + 攻击能力，每 3 秒自动反击一次。</summary>
    public sealed class Enemy : GasActor
    {
        private float _autoAttackTimer = 3f;

        public override GasActor? CurrentTarget { get; set; }

        protected override void OnReady()
        {
            ASC.AddSet<BattleAttributeSet>();
            ASC.ApplyGameplayEffectToSelf(BattleData.InitStatsEnemy);
            // 自动反击同样走输入路径，必须把能力绑到 InputID=1
            ASC.GiveAbility(new GameplayAbilitySpec(new AttackAbility(), level: 1, inputID: 1));
        }

        protected override void Update()
        {
            base.Update();
            if (CurrentTarget is { IsDead: false } && !IsDead && (_autoAttackTimer -= Time.deltaTime) <= 0f)
            {
                _autoAttackTimer = 3f;
                ASC.AbilityLocalInputPressed(1);
            }
        }

        protected override Color BodyColor => new Color(1f, 0.35f, 0.35f);
    }
}
