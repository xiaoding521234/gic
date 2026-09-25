using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 伤害请求（DamagePipeline 输入）
    /// </summary>
    public class DamageRequest
    {
        public Unit Attacker;
        public Unit Target;

        /// <summary>攻击百分比（技能参数 Damage 标注基准 BasedOnAttack 时的换算载体——40 即 40%×攻击力；
        /// 技能类读参后直传即可。**若技能把伤害配成 Fixed 基准（固定点数），勿走本字段**——换算成
        /// 等效百分比或走 FlatDamage，否则固定伤害会被误当百分比（参数基准结算纪律，docs/20）</summary>
        public int AttackPercent = 100;

        /// <summary>额外固定伤害（加法区；Fixed 基准伤害/附加伤害走此）</summary>
        public int FlatDamage;
        public int Element;

        /// <summary>易伤乘区增量（元素反应提供：如 1 级融化 +0.5；同乘区加法并入，docs/18 决策五）</summary>
        public float VulnerabilityBonus;

        /// <summary>增伤乘区增量（元素反应提供：如 1 级蒸发 +0.5——与易伤分属两区，docs/06 §反应表）</summary>
        public float DamageBonusDelta;
    }

    /// <summary>
    /// 伤害结果
    /// </summary>
    public class DamageResult
    {
        public int FinalDamage;
        public bool Cancelled;
    }

    /// <summary>
    /// 伤害管线（docs/05 §5.6 核心公式）
    /// 最终伤害 = (攻击力 × 攻击百分比 + 额外伤害) × (增伤 − 免伤) × 易伤
    /// 易伤 = 防御 ≥ 0 ? 100/(100+防御) : 1 − 防御/100
    ///
    /// B1 预留的三段静态 hooks 已删除（2026-09-25 三轮审查 S9：全项目零注册零清理的死代码，
    /// 静态 List 跨对局有残留风险，且"以为反应走 PreDamage"的注释反而误导——元素反应实际走
    /// EffectCompiler 显式调 ElementReactionResolver.Preview。B8 需要命中拦截/乘区注入时，
    /// 按"带明确生命周期负责人"的方案重新设计，勿复活静态 hooks）。
    /// </summary>
    public static class DamagePipeline
    {
        /// <summary>
        /// 计算最终伤害（只算不改状态；应用由 BattleSimState 统一做）
        /// </summary>
        public static DamageResult Calculate(DamageRequest request)
        {
            var result = new DamageResult();

            var attackerStats = request.Attacker?.GetUnitComponent<UnitStats>();
            var targetStats = request.Target?.GetUnitComponent<UnitStats>();
            if (attackerStats == null || targetStats == null)
            {
                result.Cancelled = true;
                return result;
            }

            // 基础乘区：攻击力 × 攻击百分比 + 额外伤害
            float baseZone = attackerStats.Attack * (request.AttackPercent / 100f) + request.FlatDamage;

            // 增伤免伤乘区：(增伤 − 免伤)，百分比叠加；反应增伤（DamageBonusDelta，如蒸发）同乘区加法并入
            float bonusZone = 1f + (attackerStats.DamageBonus - targetStats.DamageReduction) / 100f
                + request.DamageBonusDelta;
            if (bonusZone < 0f) bonusZone = 0f;

            // 易伤乘区：正防御除法减伤递减，负防御线性增伤（每点 1%）；
            // 反应易伤（VulnerabilityBonus）同乘区加法并入（docs/18 决策五）
            int defense = targetStats.Defense;
            float vulnerability = (defense >= 0
                ? 100f / (100f + defense)
                : 1f - defense / 100f) + request.VulnerabilityBonus;

            result.FinalDamage = Mathf.Max(0, Mathf.RoundToInt(baseZone * bonusZone * vulnerability));

            return result;
        }
    }
}
