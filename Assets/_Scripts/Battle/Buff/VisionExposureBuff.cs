using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 视野暴露（安柏变奏「全面侦查」，2026-10-17 迷雾批追拍七实装——docs/18 决策七十九追记七）：
    /// 标记型状态——持有者对**施加者阵营**解除隐身并无法隐身（Host=BattleSimState.IsUnitVisibleTo
    /// 豁免；客户端=BattleFog 强制现身+IsUnitVisible 规划期同享），所在格迷雾被施加者阵营破除
    /// （Host=RevealFog 暴露段+客户端 EvaluateReveal 随行揭雾 trail；**非全队公开**——施加者阵营
    /// 独享，2v2 队友共享/敌方无感）。无属性修改、无效果拍结算；时长=Buff_VisionExposure 资产
    /// （持续秒数 3）。重施加=Merge 时长取大（冻结同款刷新语义，非累加）。
    /// </summary>
    public class VisionExposureBuff : BaseBuff
    {
        private VisionExposureBuffConfig Cfg => (VisionExposureBuffConfig)Config;

        public override BuffType Type => BuffType.VisionExposed;

        public VisionExposureBuff(VisionExposureBuffConfig config)
        {
            Config = config;
            Level = 1;
            RemainingSeconds = Cfg.持续秒数;
        }

        private static readonly List<BattleEffect> _empty = new List<BattleEffect>();

        /// <summary>标记型：无效果拍结算（可见性/揭雾由两侧迷雾消费方按 Buff 在场态逐帧/逐阶段判定）</summary>
        public override List<BattleEffect> OnEffectBeat() => _empty;

        /// <summary>重施加=时长刷新取大（冻结同款；默认的时长累加不适用标记型）</summary>
        public override void Merge(BaseBuff newer)
        {
            RemainingSeconds = Mathf.Max(RemainingSeconds, newer.RemainingSeconds);
        }
    }
}
