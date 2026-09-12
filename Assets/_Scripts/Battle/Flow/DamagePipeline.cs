using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 伤害请求（DamagePipeline 输入）
    /// </summary>
    public class DamageRequest
    {
        public Unit Attacker;
        public Unit Target;
        public int AttackPercent = 100;
        public int FlatDamage;
        public int Element;
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
    /// Phase hooks（B1 预留点位，B2-B4 接线）：
    /// 1. PreDamage：命中判定 / 元素反应预判（返回 false 取消本次伤害）
    /// 2. MultiplierHooks：乘区修改（Buff/技能/激化"+10"类插入，改 Request）
    /// 3. PostDamage：命中触发（获元能/吸血）+ 死亡判定（死亡统一在效应应用阶段判）
    /// </summary>
    public static class DamagePipeline
    {
        private static readonly List<Func<DamageRequest, bool>> _preDamageHooks = new List<Func<DamageRequest, bool>>();
        private static readonly List<Action<DamageRequest>> _multiplierHooks = new List<Action<DamageRequest>>();
        private static readonly List<Action<DamageRequest, DamageResult>> _postDamageHooks = new List<Action<DamageRequest, DamageResult>>();

        public static void RegisterPreDamage(Func<DamageRequest, bool> hook) => _preDamageHooks.Add(hook);
        public static void RegisterMultiplier(Action<DamageRequest> hook) => _multiplierHooks.Add(hook);
        public static void RegisterPostDamage(Action<DamageRequest, DamageResult> hook) => _postDamageHooks.Add(hook);

        public static void ClearHooks()
        {
            _preDamageHooks.Clear();
            _multiplierHooks.Clear();
            _postDamageHooks.Clear();
        }

        /// <summary>
        /// 计算最终伤害（只算不改状态；应用由 BattleSimState 统一做）
        /// </summary>
        public static DamageResult Calculate(DamageRequest request)
        {
            var result = new DamageResult();

            // Phase 1: PreDamage（命中判定/反应预判）
            foreach (var hook in _preDamageHooks)
            {
                if (!hook(request))
                {
                    result.Cancelled = true;
                    return result;
                }
            }

            // Phase 2: 乘区修改（Buff/技能插入增伤/免伤/百分比修改）
            foreach (var hook in _multiplierHooks)
                hook(request);

            var attackerStats = request.Attacker?.GetUnitComponent<UnitStats>();
            var targetStats = request.Target?.GetUnitComponent<UnitStats>();
            if (attackerStats == null || targetStats == null)
            {
                result.Cancelled = true;
                return result;
            }

            // 基础乘区：攻击力 × 攻击百分比 + 额外伤害
            float baseZone = attackerStats.Attack * (request.AttackPercent / 100f) + request.FlatDamage;

            // 增伤免伤乘区：(增伤 − 免伤)，百分比叠加
            float bonusZone = 1f + (attackerStats.DamageBonus - targetStats.DamageReduction) / 100f;
            if (bonusZone < 0f) bonusZone = 0f;

            // 易伤乘区：正防御除法减伤递减，负防御线性增伤（每点 1%）
            int defense = targetStats.Defense;
            float vulnerability = defense >= 0
                ? 100f / (100f + defense)
                : 1f - defense / 100f;

            result.FinalDamage = Mathf.Max(0, Mathf.RoundToInt(baseZone * bonusZone * vulnerability));

            // Phase 3: PostDamage（命中触发；死亡判定在效应应用阶段统一做）
            foreach (var hook in _postDamageHooks)
                hook(request, result);

            return result;
        }
    }
}
