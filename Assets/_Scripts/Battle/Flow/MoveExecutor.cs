using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 移动行动执行器：构建移动者状态，实际结算由 MovementResolver 同片同步逐步展开。
    /// 移动是特殊的技能（2026-09-23 B-S1b 拍板）：步数上限=移动技能条目 MoveDistance 参数
    /// （skills[0]·Move 型，数据驱动与 HUD 瞄准同源）；无移动技能/无参数回落 3（旧默认）。
    /// </summary>
    public static class MoveExecutor
    {
        public static MoveActionState BuildMover(BattleSimState sim, ActionData action)
        {
            var unit = sim.GetUnit(action.unitId);
            if (unit == null) return null;
            if (BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit)) return null;

            var mover = new MoveActionState
            {
                UnitId = action.unitId,
                Unit = unit,
                Direction = action.direction,
                RemainingSteps = Math.Min(Math.Max(0, action.moveMagnitude), MaxMoveDistance(unit)),
            };
            return mover;
        }

        /// <summary>
        /// 移动步数上限（Host 权威收口）：移动技能 MoveDistance 按基准换算（2026-09-23 用户拍板
        /// 「拼接后为10%移速」——BasedOnMoveSpeed=百分比×当前移速，SkillData.ResolveMoveDistance；
        /// 2026-11-07 移速基线改 50 后：安柏 60→6 格、其余 50→5 格）；移速读 UnitStats 当前值
        /// （含 Buff——减速缩短移动距离）。
        /// </summary>
        public static int MaxMoveDistance(Unit unit)
        {
            if (unit?.Skills != null)
            {
                foreach (var skill in unit.Skills)
                {
                    if (skill?.RawData?.skillType != SkillType.Move) continue;
                    int moveSpeed = unit.GetUnitComponent<UnitStats>()?.MoveSpeed ?? BattleMetrics.DefaultMoveSpeedCm;
                    return skill.RawData.ResolveMoveDistance(moveSpeed);
                }
            }
            return 3; // 无移动技能条目=旧默认回落
        }

        /// <summary>移动消耗声明（统一消耗模型 C-2，docs/30）：移动技能条目的 costs（C-2 起全移动
        /// 技能资产已配 Stamina 条目）——**无移动技能条目单位走常量兜底**（配额行动 10 体力，规则真源
        /// =BattleMetrics.StaminaCostPerAction；2026-09-28 盘点 21/34 单位无 Move 条目=兜底是主路径非边角）。
        /// 返回共享只读实例（HasAll/ChargeAll 只读消费，调用方勿改）。
        /// 有 Move 条目但未声明消耗=免费移动（costs 空=无消耗语义，数据即事实）</summary>
        public static List<SkillCostEntry> GetMoveCosts(Unit unit)
        {
            if (unit?.Skills != null)
            {
                foreach (var skill in unit.Skills)
                {
                    if (skill?.RawData?.skillType != SkillType.Move) continue;
                    return skill.RawData.HasCosts ? skill.RawData.costs : null;
                }
            }
            return FallbackMoveCosts;
        }

        private static readonly List<SkillCostEntry> FallbackMoveCosts = new()
        {
            new SkillCostEntry { kind = CostKind.Stamina, amount = BattleMetrics.StaminaCostPerAction }
        };

        // ==================== 途经采集（docs/47 资源点批，2026-10-27；docs/11 旧挂账销案） ====================

        /// <summary>途经采集参数（移动技能条目数据驱动——安柏飞行冠军 CollectCount=3/
        /// CollectStaminaGain=5/CollectEnergyGain=5；0 次=无途经采集〔默认，普通移动零行为变化〕）。
        /// 参数只配在 Move 条目上=未来采集型移动技能配参即生效零代码</summary>
        public readonly struct CollectParams
        {
            public readonly int count;          // 每次移动至多采集次数（CollectCount）
            public readonly int energyGain;     // 每次采集元能（CollectEnergyGain）
            public readonly int staminaGain;    // 每次采集体力（CollectStaminaGain）

            public CollectParams(int count, int energyGain, int staminaGain)
            {
                this.count = count;
                this.energyGain = energyGain;
                this.staminaGain = staminaGain;
            }
        }

        /// <summary>读移动者移动技能条目的采集参数（与 MaxMoveDistance/GetMoveCosts 同源条目遍历；
        /// 无 Move 条目/无 CollectCount 参数=零采集）</summary>
        public static CollectParams CollectParamsOf(Unit unit)
        {
            if (unit?.Skills != null)
            {
                foreach (var skill in unit.Skills)
                {
                    var data = skill?.RawData;
                    if (data == null || data.skillType != SkillType.Move) continue;
                    int count = data.GetInt(SkillParamKey.CollectCount, 0);
                    if (count <= 0) return default;
                    return new CollectParams(count,
                        data.GetInt(SkillParamKey.CollectEnergyGain, 0),
                        data.GetInt(SkillParamKey.CollectStaminaGain, 0));
                }
            }
            return default;
        }
    }
}
