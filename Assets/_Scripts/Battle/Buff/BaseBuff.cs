using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// Buff 基类（B2 实体化，docs/active/22 §10）。
    /// 回合制计时（docs/04 §4.4）：只在每次回合结束时计数减一，上 buff 的当回合结束即减——
    /// 现实秒计时（连携窗等）只走执行阶段时轴，与本类无关。
    /// 回合结束效果按**注册序**结算（docs/active/22 §2）：ApplicationIndex 由 BattleSimState 分配。
    /// </summary>
    public abstract class BaseBuff
    {
        public Unit owner;
        public Unit source;
        public int value;

        /// <summary>所属战场门面（BattleSimState.ApplyBuff 注册时注入；回合结束效果需要战场查询的
        /// Buff 消费——如歌声之环按持有者位置枚举半径内单位。单局生命周期，勿跨对局复用）</summary>
        public BattleSimState Sim;

        /// <summary>永久 Buff（RemainingTurns&lt;0 标记——歌声之环/寒冰之棱类：不计时）；
        /// 回合递减与到期收集均跳过（TurnResolver/CollectExpiredBuffs 同判据），
        /// 消失通道=持有者倒下（RemoveOnHolderDeath）或碎裂/驱散即时移除（BattleSimState.RemoveBuff）</summary>
        public bool IsPermanent => RemainingTurns < 0;

        /// <summary>是否已达叠层上限（AI 脑增益候选排除用——重施加 Merge 无增益时不占行动）；
        /// 默认否=不限/不适用，按命座成长的叠层型 Buff 覆写（寒冰之棱）</summary>
        public virtual bool IsAtStackCap() => false;

        /// <summary>持有者倒下时是否随之移除（默认否——尸体保留 Buff 属通则，如 BurnBuff 烧尸体；
        /// 歌声之环类光环覆写 true，EmitSliceCommands 死亡循环随 Death 命令后发 RemoveBuff）</summary>
        public virtual bool RemoveOnHolderDeath => false;

        /// <summary>协议类型标识（命令流/快照/客户端图标映射）</summary>
        public abstract BuffType Type { get; }

        /// <summary>级别（如燃烧持续 3 回合 × 级别，docs/06）</summary>
        public int Level = 1;

        /// <summary>回合制剩余回合数（回合结束减一，含上 buff 当回合）</summary>
        public int RemainingTurns = 1;

        /// <summary>全局注册序（回合结束效果结算顺序）</summary>
        public int ApplicationIndex;

        /// <summary>
        /// 回合结束效果（按注册序调用；返回效应由 TurnResolver 统一应用并产出命令）
        /// </summary>
        public abstract List<BattleEffect> OnTurnEnd();

        /// <summary>挂载回调（BattleSimState.ApplyBuff 注册后调用；如冻结写入 UnitStatus）</summary>
        public virtual void OnApplied() { }

        /// <summary>移除回调（到期/驱散；如冻结解除 UnitStatus）</summary>
        public virtual void OnRemoved() { }

        /// <summary>
        /// 同类重复施加的合并规则。默认 = 时长累加 + 级别取大
        /// （与燃烧"被附着草延长 3 回合 × 层数"同构，docs/06；B4 反应批次按需覆写）。
        /// </summary>
        public virtual void Merge(BaseBuff newer)
        {
            RemainingTurns += newer.RemainingTurns;
            Level = Mathf.Max(Level, newer.Level);
        }

        /// <summary>施加者命座等级+其 Talent 命座技能数据（参数载体型命座——歌声之环/寒冰之棱的
        /// C 系参数读取单出口，2026-09-30 歌声之环批立、凛冽轮舞批上收基类防第三份复制）；
        /// 无施加者回落持有者自身（跨命座语义仍正确——持有者升命只影响自己光环的数值）</summary>
        protected (int level, SkillConfig.SkillData talent) SourceConstellation()
        {
            var s = source != null ? source : owner;
            if (s == null) return (0, null);
            foreach (var skill in s.Skills)
            {
                var data = skill?.RawData;
                if (data == null || data.skillType != SkillType.Talent) continue;
                return (s.ConstellationLevel, data);
            }
            return (s.ConstellationLevel, null);
        }

        // ==================== 数值基准/几何原语（2026-10-02 执行阶段复审收口） ====================
        // 光环/DoT 族 OnTurnEnd 的样板件单源——Burn/歌声之环/寒冰之棱三处逐字重复的
        // 施加者回落与 id 推导收拢；新光环/DoT Buff（火环/岩环等）落地时勿再手抄。

        /// <summary>数值基准单位（施加者优先——跨命座语义+亡佚兜底；无施加者回落持有者）。
        /// 与 SourceConstellation 同款回落口径；伤害归属/治疗基准/吸血换算的攻击者统一走此口</summary>
        protected Unit AttackerOf() => source != null ? source : owner;

        /// <summary>AttackerOf() 的 unitId 形态（identity 缺失回落 fallbackId——通常传持有者 id）</summary>
        protected string AttackerIdOf(string fallbackId)
            => AttackerOf().GetUnitComponent<UnitIdentity>()?.UnitID ?? fallbackId;
    }
}
