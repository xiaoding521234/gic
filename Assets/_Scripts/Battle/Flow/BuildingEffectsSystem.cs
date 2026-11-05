using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 建筑效果系统（2026-10-26 fix-batch2 ③收口：TurnResolver 内联建筑段迁出——加第 N 种建筑
    /// 效果 = BuildingEffectKind 枚举 + UnitData 字段 + 本类一处 switch + BuildingEffectDescription
    /// 表键；AI 态势分档按 kind 默认兜底、UnitDetailPanel 走描述表，均不用动）。实例随 TurnResolver
    /// 每局创建——无静态跨局状态。
    /// 发射序铁律：本类只搬家不改序（回合末效果=泡湿后/ApplyEffects 前收集；元能引擎过阈=加能落地拍
    /// 收集、EmitSliceCommands 尾〔对账前〕发射；命令序与原内联版逐位一致——回放基线 PASS 锚）。
    /// </summary>
    public class BuildingEffectsSystem
    {
        private readonly BattleSimState _sim;

        /// <summary>骑士团总部元能引擎过阈待召队列（2026-10-24 决策八十六追记四）：(建筑 unitId, 过阈
        /// 获能命中时刻)——ApplyEffects 加能落地同拍入队（状态层已扣消耗额），DrainPendingSummons
        /// 统一发射消费命令+召唤命令（RegisterUnit 只在发射拍执行，防应用循环内枚举突变——
        /// CollectTurnEndEffects「先快照再处理」同族纪律）</summary>
        private readonly List<(string buildingId, float hitSeconds)> _pendingSummons
            = new List<(string buildingId, float hitSeconds)>();

        public BuildingEffectsSystem(BattleSimState sim) { _sim = sim; }

        /// <summary>建筑回合末效果收集（决策八十六批D，docs/45 §1.1/§6）：只结算存活建筑
        /// （消散即移除天然；多座同种独立结算——两座餐馆=+10）。旗帜=半径内我方非建筑单位（含倒下
        /// 角色，拍板 #8）各+元能（EnergyEffect 建筑类别，合并键含来源建筑——两座旗帜对同一目标各
        /// +1 不互吞）；餐馆=己方玩家+摩拉（StatChange 直产）；骑士团=以部署回合为 0、每过 效果周期
        /// 回合再召 1 名（最近可部署格顺延；部署当回合末 diff=0 跳过——部署段已即召）；教堂=我方存活
        /// HP&lt;效果阈值% 单位（含建筑，拍板 #9）治疗 效果数值%×本建筑攻击（末点截断通则）；
        /// 总部=回合末自充 效果数值 元能（过阈即召由加能落地拍判定）。
        /// **先快照再处理**：周期召唤会 RegisterUnit 突变 _units，快照后枚举安全</summary>
        public void CollectTurnEndEffects(int turnNumber, List<BattleEffect> effects, List<BattleCommand> extraCommands)
        {
            var buildings = new List<(string id, Unit unit, UnitConfig.UnitData data, UnitIdentity identity)>();
            foreach (var kv in _sim.SortedUnits())
            {
                var unit = kv.Value;
                if (unit == null || BattleSimState.IsDead(unit)) continue;
                var data = unit.RawData;
                if (data == null || data.unitType != UnitType.Building || data.建筑效果 == BuildingEffectKind.None) continue;
                var identity = unit.GetUnitComponent<UnitIdentity>();
                if (identity == null) continue;
                buildings.Add((kv.Key, unit, data, identity));
            }

            foreach (var building in buildings)
            {
                var data = building.data;
                switch (data.建筑效果)
                {
                    case BuildingEffectKind.EnergyAura:
                    {
                        if (data.效果半径 <= 0 || data.效果数值 <= 0) break;
                        var center = _sim.GetPosition(building.unit);
                        foreach (var tkv in _sim.SortedUnits())
                        {
                            var target = tkv.Value;
                            if (target == null) continue;
                            var targetIdentity = target.GetUnitComponent<UnitIdentity>();
                            if (targetIdentity == null || targetIdentity.Team != building.identity.Team) continue; // 我方（Team 域，歌声之环同口径）
                            var targetData = target.RawData;
                            if (targetData != null && targetData.unitType == UnitType.Building) continue; // 非建筑（拍板 #8）
                            // 倒下角色照常（拍板 #8 含倒下；眷属/建筑无尸体=自然缺席）
                            long auraR = (long)data.效果半径 * CellMetrics.CellSizeCm; // 欧氏平方（决策九十五迁移：切比雪夫→欧氏）
                            if (_sim.GetPosition(target).SquaredEuclideanCmTo(center) > auraR * auraR) continue;
                            effects.Add(new EnergyEffect(tkv.Key, data.效果数值, EnergyEffect.CategoryBuildingGain)
                            {
                                SourceUnitId = building.id,
                            });
                        }
                        break;
                    }
                    case BuildingEffectKind.MoraIncome:
                    {
                        if (data.效果数值 <= 0) break;
                        // 凌晨批议题⑥天理经济作弊：建筑摩拉收入同乘难度倍率（MoraIncomePercentOf 单源，
                        // 与回合末发放同口径；掠夺/卡牌消耗不复用=作弊只在「收入」语义内）
                        int moraGain = data.效果数值
                            * AIPlayerConfig.MoraIncomePercentOf(building.identity.OwnerPlayerID) / 100;
                        _sim.ApplyMoraDelta(building.identity.OwnerPlayerID, moraGain);
                        extraCommands.Add(BattleCommand.StatChange(building.identity.OwnerPlayerID, 0, 0,
                            BattleCommand.StatKindMora, moraGain));
                        break;
                    }
                    case BuildingEffectKind.PeriodicSummon:
                    {
                        if (data.效果周期 <= 0) break;
                        // P6 残留审计 C3（docs/active/49）：效果结算只在效果拍（每 2 秒）发生，旧
                        // 「diff % 效果周期 == 0」在周期为奇数时永远对不齐（5 秒周期实际变 10 秒）——
                        // 改为「距上次成功召唤 ≥ 周期」的单调判定（首次=部署时刻），周期语义恢复为秒。
                        int lastSummonSecond = building.unit.LastPeriodicSummonSecond > 0
                            ? building.unit.LastPeriodicSummonSecond
                            : building.unit.DeployedTurn;
                        if (turnNumber - lastSummonSecond < data.效果周期) break;
                        if (DeployUnitExecutor.TrySummonAtNearestCell(_sim, building.identity.OwnerPlayerID,
                                data.召唤单位, _sim.GetPosition(building.unit), out var summonCommand))
                        {
                            building.unit.LastPeriodicSummonSecond = turnNumber;
                            extraCommands.Add(summonCommand);
                        }
                        break;
                    }
                    case BuildingEffectKind.EnergySummon:
                    {
                        // 元能引擎自充（2026-10-24 追记四，骑士团总部）：回合末自获 效果数值 元能——
                        // EnergyEffect 自身为目标（建筑类别获能，合并键含来源=多座总部各自各跳）；
                        // 过阈即召由 ApplyEffects 落地拍统一判定（外部充能〔歌声之环 tick〕同拍同权）
                        if (data.效果数值 <= 0) break;
                        effects.Add(new EnergyEffect(building.id, data.效果数值, EnergyEffect.CategoryBuildingGain)
                        {
                            SourceUnitId = building.id,
                        });
                        break;
                    }
                    case BuildingEffectKind.HealWounded:
                    {
                        if (data.效果数值 <= 0) break;
                        int healAmount = data.效果数值 * data.GetEffectiveAttack() / 100; // 末点截断（整数除法）
                        if (healAmount <= 0) break;
                        foreach (var tkv in _sim.SortedUnits())
                        {
                            var target = tkv.Value;
                            if (target == null || BattleSimState.IsDead(target)) continue; // 存活（拍板 #9）
                            var targetIdentity = target.GetUnitComponent<UnitIdentity>();
                            if (targetIdentity == null || targetIdentity.Team != building.identity.Team) continue;
                            var stats = target.GetUnitComponent<UnitStats>();
                            if (stats == null) continue;
                            var hp = stats.GetStatStruct(StatType.HP);
                            if (hp.Max <= 0 || hp.Value * 100 >= data.效果阈值 * hp.Max) continue; // 低于阈值才奶
                            effects.Add(new HealEffect(building.id, tkv.Key, healAmount));
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>骑士团总部元能引擎过阈收集（2026-10-24 决策八十六追记四）：加能落地同拍判定——过阈即耗
        ///（状态层立刻扣 效果阈值，不留「过阈未耗」窗口）+入队待召；消费命令与召唤命令由
        /// EmitSliceCommands 尾统一发射（命令序=全部获能命令之后：客户端能量条先涨后扣再出骑士，
        /// 因果读得懂）。外部充能（歌声之环 tick/自充）与未来中途给能同拍触发——建筑效果不必
        /// 等回合末（用户拍板「注意建筑的效果不一定是在回合结束才生效了」）</summary>
        public void CollectEnergySummon(Unit unit, float hitSeconds)
        {
            if (unit == null) return;
            var data = unit.RawData;
            if (data == null || data.unitType != UnitType.Building
                || data.建筑效果 != BuildingEffectKind.EnergySummon) return;
            if (data.效果阈值 <= 0 || data.召唤单位 == UnitName.Amber) return; // 召唤缺省防误配（部署即召同款守卫）
            var stats = unit.GetUnitComponent<UnitStats>();
            if (stats == null) return;
            var energy = stats.GetStatStruct(StatType.Energy);
            if (energy.Value < data.效果阈值) return;
            energy.Add(-data.效果阈值); // 过阈即耗（触发线=消耗额单字段）
            stats.SetStatStruct(StatType.Energy, energy);
            string buildingId = unit.GetUnitComponent<UnitIdentity>()?.UnitID;
            if (!string.IsNullOrEmpty(buildingId))
                _pendingSummons.Add((buildingId, hitSeconds));
        }

        /// <summary>建筑元能引擎过阈召唤发射（决策八十六追记四）：ApplyEffects 落地拍收集的过阈事实——
        /// 逐建筑发射 消费命令（-效果阈值；带过阈获能同拍时刻，客户端能量条先涨后扣）+ 召唤命令
        ///（TrySummonAtNearestCell 锚=建筑自身格，最近可部署格顺延）；同段死亡的建筑=召唤取消；
        /// 落点无合法格（棋盘占满）=召唤落空、能量已耗（边缘自担，与「立刻」语义一致）。
        /// RegisterUnit 只在此发射拍执行——应用循环内不突变 _units（枚举安全，
        /// CollectTurnEndEffects「先快照再处理」同族纪律）。返回过阈事实供对账（2026-10-25 复审批：
        /// 原排 audit 后=审计失明，现先于对账执行+事实凭据回传）</summary>
        public List<BattleEffect> DrainPendingSummons(Segment segment, int sliceIndex)
        {
            var facts = new List<BattleEffect>();
            if (_pendingSummons.Count == 0) return facts;
            foreach (var (buildingId, hitSeconds) in _pendingSummons)
            {
                var building = _sim.GetUnit(buildingId);
                if (building == null || BattleSimState.IsDead(building)) continue; // 同段死亡=取消
                var data = building.RawData;
                if (data == null || data.召唤单位 == UnitName.Amber) continue; // 召唤缺省防误配
                var identity = building.GetUnitComponent<UnitIdentity>();
                if (identity == null) continue;
                var consume = BattleCommand.StatChange(buildingId, sliceIndex, segment.commands.Count,
                    BattleCommand.StatKindEnergy, -data.效果阈值);
                TurnResolver.ApplyHitMs(consume, hitSeconds);
                segment.commands.Add(consume);
                facts.Add(new BuildingSummonFactEffect(buildingId)); // 过阈事实（对账凭据——消费命令必产）
                if (DeployUnitExecutor.TrySummonAtNearestCell(_sim, identity.OwnerPlayerID, data.召唤单位,
                        _sim.GetPosition(building), out var summonCommand))
                {
                    summonCommand.sliceIndex = sliceIndex;
                    summonCommand.indexInSlice = segment.commands.Count;
                    segment.commands.Add(summonCommand);
                }
            }
            _pendingSummons.Clear();
            return facts;
        }
    }
}
