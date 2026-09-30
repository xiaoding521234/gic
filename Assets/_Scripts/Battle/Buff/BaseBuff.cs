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

        /// <summary>永久 Buff（RemainingTurns&lt;0 标记——歌声之环类：不计时，持有者倒下才消失）；
        /// 回合递减与到期收集均跳过（TurnResolver/CollectExpiredBuffs 同判据）</summary>
        public bool IsPermanent => RemainingTurns < 0;

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
    }
}
