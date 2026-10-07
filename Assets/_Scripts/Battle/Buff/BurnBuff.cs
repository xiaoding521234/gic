using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 燃烧（docs/06 元素与反应系统）：每回合结束受到〔Buff_Burn.asset 每回合伤害〕点火伤，
    /// 持续〔每级持续回合〕× 级别（数值单源=BuffConfig 资产，docs/active/39——调平衡改 Inspector 零代码）。
    /// 来源=火+草反应（B4 接）；B2 由调试技能附带施加验证 DoT 链路。
    /// 草元素延长机制（附着草→消耗草→延长 3 回合 × 层数）B4 随元素反应落地。
    /// 2026-10-01 统一拍板（docs/18 决策二十三）：火伤走统一元素伤害出口——吃防御/易伤乘区+
    /// 参与反应预判+附着火（docs/06 §6.6「0层终点不连锁」概念废除）。
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
            // DamagePipeline（吃目标防御/易伤）+反应预判+附着火（docs/06 §6.6「0层终点」废除）
            effects.Add(new PendingAuraHit(sourceId, ownerId, attacker, owner, ElementType.Pyro,
                new DamageRequest
                {
                    Attacker = attacker,
                    Target = owner,
                    Element = (int)ElementType.Pyro,
                    FlatDamage = Cfg.每回合伤害,
                }, 0f));
            return effects;
        }
    }

    /// <summary>
    /// Buff 工厂（单一 switch 分发，2026-10-02 执行阶段复审收口：原双 Create 重载合一——
    /// 封闭枚举 switch 即定式，不再挂「扩为注册表」的旧承诺）。
    /// value 通道（B-S1b）：技能参数经 ApplyBuffEffect.BuffValue 单源传入（如 AttackUp 的每层
    /// ATKBonus、寒冰之棱的初始层数），与技能参数表同源、勿在各 Buff 内硬编码默认值以外的取值来源。
    /// 参数型 Buff（AttackUp 族三件套）必须配齐 turns——缺持续回合=Warn+null 防御（与原三参重载
    /// 对参数型类型的 default 分支同语义；正常配置必带 Duration 参数）。
    /// 2026-10-07 Buff 配置化（docs/active/39）：构造前统一查 BuffConfig 注册表注入 Config——
    /// 缺资产/家族不符=Warn+null 防御（反应类无技能语境，资产即真源；StatBuff 族数值仍由
    /// 技能参数经 ctor 注入优先，config 只装元数据）。
    /// </summary>
    public static class BuffFactory
    {
        public static BaseBuff Create(BuffType type, int level, Unit source, int value = 0,
            int stackLimit = 0, int turns = 0)
        {
            var cfg = BuffConfig.OfType(type);
            if (cfg == null)
            {
                GICLog.Warn($"[BuffFactory] BuffType.{type} 缺配置资产" +
                            $"（Assets/Resources/Configs/Buffs/Buff_{type}.asset，docs/active/39）");
                return null;
            }
            switch (type)
            {
                case BuffType.Burn:
                    if (cfg is not BurnBuffConfig burnCfg) return FamilyMismatch(type, cfg);
                    return new BurnBuff(burnCfg, level) { source = source };
                case BuffType.Freeze:
                    if (cfg is not FreezeBuffConfig freezeCfg) return FamilyMismatch(type, cfg);
                    return new FreezeBuff(freezeCfg, level) { source = source };
                case BuffType.SongOfLife:
                    // 歌声之环（B-3 ②）：永久 1 层光环——参数通道不适用（恒 1 层/不计时）
                    if (cfg is not SongOfLifeBuffConfig songCfg) return FamilyMismatch(type, cfg);
                    return new SongOfLifeBuff(songCfg) { source = source };
                case BuffType.Icicle:
                    // 寒冰之棱（凛冽轮舞批）：value 通道=初始层数（ShardCount 经 paramKey 注入——
                    // 工厂按 Buff 类型解释 value 的既有先例同款，AttackUp=每层加成/寒冰之棱=初始层数）
                    if (cfg is not IcicleBuffConfig icicleCfg) return FamilyMismatch(type, cfg);
                    return new IcicleBuff(icicleCfg, Mathf.Max(1, value)) { source = source };
                case BuffType.AttackUp:
                case BuffType.MoveSpeedUp:
                case BuffType.DefenseDown:
                    // 2026-10-07 拍板「持续时间改为无限」：turns<0=永久声明（IsPermanent 同歌声之环/
                    // 寒冰之棱口径——不计时、Merge 保永久）；turns=0 才是缺参数配置错误
                    if (turns == 0)
                    {
                        GICLog.Warn($"[BuffFactory] 参数型 Buff {type} 缺持续回合（turns={turns}）——" +
                                    "检查 ApplyBuffEffect 是否配齐 Duration 参数（paramKey3）");
                        return null;
                    }
                    if (type == BuffType.AttackUp)
                        return new AttackUpBuff(level, value, stackLimit, turns) { source = source, Config = cfg };
                    if (type == BuffType.MoveSpeedUp)
                        return new MoveSpeedBuff(level, value, stackLimit, turns) { source = source, Config = cfg };
                    return new DefenseDownBuff(level, value, stackLimit, turns) { source = source, Config = cfg };
                default:
                    GICLog.Warn($"[BuffFactory] 未实现的 Buff 类型 {type}");
                    return null;
            }
        }

        /// <summary>配置资产家族与 BuffType 不符（如 Burn 类型挂了歌声之环的资产子类）——防御出口</summary>
        private static BaseBuff FamilyMismatch(BuffType type, BuffConfig cfg)
        {
            GICLog.Warn($"[BuffFactory] BuffType.{type} 配置资产家族不符（{cfg.name} 实为 {cfg.GetType().Name}）");
            return null;
        }
    }
}
