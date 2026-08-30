#nullable enable

using System.Collections.Generic;
using GasNet;
using UnityEngine;

namespace UnityDemo
{
    /// <summary>
    /// 场景根：组装两个由 GasNet 驱动的单位，把输入与日志接到 ASC 上。
    /// 空格 = 英雄攻击；敌人每 3 秒自动反击。单位与圆形 sprite 都在运行时生成——
    /// 本工程不需要任何美术资产。
    /// </summary>
    public sealed class Main : MonoBehaviour
    {
        private Hero _hero = null!;
        private Enemy _enemy = null!;
        private readonly List<string> _lines = new List<string>();
        private GUIStyle? _logStyle;
        private GUIStyle? _bannerStyle;

        private void Start()
        {
            GasNetLog.OnWarn = Debug.LogWarning; // 核心库日志 → Unity Console
            BattleData.Load();                   // GE 定义来自 StreamingAssets/Data/BattleGE.json（数据驱动层示例）

            AbilitySystemGlobals.Get().GameplayCueManager.RegisterNotify(new HitCueNotify());

            var units = new GameObject("Units");
            _hero = CreateUnit<Hero>(units.transform, "Hero", new Vector3(-3.5f, 0f, 0f));
            _enemy = CreateUnit<Enemy>(units.transform, "Enemy", new Vector3(3.5f, 0f, 0f));
            _hero.CurrentTarget = _enemy;
            _enemy.CurrentTarget = _hero;

            foreach (var actor in new GasActor[] { _hero, _enemy })
            {
                var watched = actor;
                watched.ASC
                    .GetGameplayAttributeValueChangeDelegate(BattleAttributeSet.HealthAttr)
                    .Handler += _ => OnHealthChanged(watched);
            }

            Log("GasNet demo started — press SPACE to attack.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space) && !_hero.IsDead && !_enemy.IsDead)
                _hero.ASC.AbilityLocalInputPressed(1);
        }

        private static T CreateUnit<T>(Transform parent, string unitName, Vector3 position) where T : GasActor
        {
            var go = new GameObject(unitName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.AddComponent<T>();
        }

        private void OnHealthChanged(GasActor actor)
        {
            if (actor.IsDead)
                Log($"{actor.name} is DEAD — {(actor == _hero ? "DEFEAT" : "VICTORY")}!");
            else
                Log($"{actor.name} HP → {actor.Health:0.#}");
        }

        private void Log(string line)
        {
            _lines.Add(line);
            if (_lines.Count > 10)
                _lines.RemoveAt(0);
        }

        private void OnGUI()
        {
            if (_logStyle == null)
            {
                _logStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
                _logStyle.normal.textColor = Color.white;
                _bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
                _bannerStyle.normal.textColor = Color.white;
            }

            GUI.Label(new Rect(12f, 6f, 900f, 28f),
                "SPACE = attack · enemy auto-attacks every 3s · details in Console", _logStyle);

            const float lineHeight = 22f;
            var boxHeight = _lines.Count * lineHeight + 16f;
            var boxY = Screen.height - boxHeight - 10f;
            GUI.Box(new Rect(8f, boxY, 640f, boxHeight), GUIContent.none);
            for (var i = 0; i < _lines.Count; i++)
                GUI.Label(new Rect(16f, boxY + 8f + i * lineHeight, 620f, lineHeight), _lines[i], _logStyle);

            if (_bannerStyle != null && (_hero.IsDead || _enemy.IsDead))
            {
                GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, 60f),
                    _hero.IsDead ? "DEFEAT" : "VICTORY", _bannerStyle);
            }
        }
    }
}
