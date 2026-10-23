using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 燃烧（docs/06 元素与反应系统）：每回合结束受到〔Buff_Burn.asset 每回合伤害〕点火伤，
    /// 持续〔每级持续回合〕× 级别（数值单源=BuffConfig 资产，docs/39——调平衡改 Inspector 零代码）。
    /// 来源=火+草反应（B4 接）；B2 由调试技能附带施加验证 DoT 链路。
    /// 草元素延长机制（附着草→消耗草→延长 3 回合 × 层数）B4 随元素反应落地。
    /// **0 层终点语义（2026-10-15 决策七十四修订决策二十三）**：燃烧火伤 tick=0 层火伤——
    /// 不反应、不附着（docs/06 §6.2「0层=纯伤害无附着、不反应」），但**仍视为火伤**：吃
    /// 防御/易伤/增伤乘区、可暴击、触发「造成火伤」类效果（区别只在反应与附着）。
    /// ~~2026-10-01 决策二十三「tick 统一反应+附着」~~就燃烧回退——0 层字段（elementLayers）铺路后
    /// 多层技能标注时再走全链。
    /// </summary>
    public class BurnBuff : BaseBuff
    {
        private BurnBuffConfig Cfg => (BurnBuffConfig)Config;

        public override BuffType Type => BuffType.Burn;

        public BurnBuff(BurnBuffConfig config, int level = 1)
        {
            Config = config;
            Level = Mathf.Max(1, level);
            RemainingTurns = Cfg.每级持续回合 * Level;
        }

        public override List<BattleEffect> OnTurnEnd()
        {
            var effects = new List<BattleEffect>();
            if (owner == null || Sim == null) return effects;
            string ownerId = owner.GetUnitComponent<UnitIdentity>()?.UnitID;
            if (ownerId == null) return effects;

            // 伤害归属=施加者（快照/命令流的 attacker），无施加者信息时归目标自身——AttackerOf 单源
            var attacker = AttackerOf();
            string sourceId = AttackerIdOf(ownerId);
            // 只声明命中（PendingAuraHit，拍 0）：回合末交错管道统一结算——火伤平直值走
            // DamagePipeline（吃目标防御/易伤），**Layers=0 层=不反应不附着**（决策七十四 0 层终点语义）
            effects.Add(new PendingAuraHit(sourceId, ownerId, attacker, owner, ElementType.Pyro,
                new DamageRequest
                {
                    Attacker = attacker,
                    Target = owner,
                    Element = (int)ElementType.Pyro,
                    FlatDamage = Cfg.每回合伤害,
                }, 0f, attach: true, layers: 0));
            return effects;
        }
    }
}
