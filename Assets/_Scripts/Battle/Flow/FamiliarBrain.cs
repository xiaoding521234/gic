using System.Collections.Generic;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 眷属决策器（B6b 起，docs/04 §4.1 五阶段第一阶段；D 批次 2026-09-29 术语迁移改名：
    /// 低级单位→眷属、LowUnitBrain→FamiliarBrain，docs/32 §6）：1~2 星眷属单位无论归属
    /// 都自主决定本回合行动（表现类似 MOBA 小兵），决策先于玩家选择完成、不感知玩家本回合选择；
    /// 本阶段只决策不结算。
    /// v2（2026-09-25 方向纪律收口）：战技预判/移动方向一律十字四向（docs/18 决策八「十字方向其一」
    /// ——八向上交在技能侧被归一主轴斜向必空放，docs/11 方向纪律①④）；预判走技能实例
    /// WouldHitEnemyInDirection（与 HUD 瞄准推荐/结算形态同源）+ 存活目标校验（纯尸体线不浪费行动）。
    /// v3（2026-09-26 报障「不会绕路卡湖边」）：移动档改 BFS 最短路首步（FindApproachFirstStep）——
    /// 直行被湖/虚空挡时拐弯绕行，每回合重算走一步沿最短路逼近；小兵蠕动 1 步/回合语义不变。
    /// v4（2026-09-26 拍板「当自己的任何攻击都无法打到时，换目标巡逻」）：攻击档打不了时按距离
    /// 升序逐敌试逼近步（FindEnemiesByDistance）——贴身已到/不可达的目标自动跳过换下一个，
    /// 不再对着打不了的目标站桩；全部敌都无逼近步才缺席。
    /// v5（2026-10-03 拍板「扩眷属脑会放爆发」）：新增爆发档——满槽即放、优先于战技（元能门槛经
    /// ResourceGate 元能照查；方向攻击型爆发与战技走同一十字预判口 WouldHitEnemyInDirection；
    /// 单位指向/无目标自施放型无方向域不消费——眷属现无此类，出现时再扩）。
    /// v6（2026-10-03 报障「移速 20 根本没发挥出来」）：移动步数退役「小兵蠕动 1 步/回合」硬编码
    /// ——改读 MoveExecutor.MaxMoveDistance（MoveDistance×移速单源换算=10%×移速，含 Buff 减速，
    /// 与玩家移动/HUD 同口径）；遇阻由 MovementResolver 结算截停。
    /// v6.1（2026-10-03 同日报障「来回左右移动持续多回合」）：步进改走 BFS 最短路「直线前缀」
    /// （FindApproachStraightSteps——只踏距离严格递减格、踏上攻击位即停）；「首步方向×N 直线飞」
    /// 越过拐点/攻击位=振荡根源，每回合距离单调递减后数学上不可能振荡。
    /// v7（2026-10-03 拍板「眷属总是向协议核心进攻，除非攻击视野内有其它敌人…同距离时优先协议
    /// 核心，不在攻击视野则不关心——就像皇室战争的单位一样」）：移动档目标序改**核心锚定**——
    /// 核心无视野门槛恒为目标；非核心敌须「攻击视野内（Unspecified 回落 5）且严格近于核心」才追
    /// （UnitData.攻击视野 字段）；核心已破=退化为视野内纯距离序。攻击档（战技/爆发）射程即感知
    /// 边界，不另设视野门。
    /// v9（2026-11-04 决策九十五 P5 连续空间移植，蓝图 §6）：十字方向域/格 BFS 全退役——攻击档=
    /// **任意角瞄准**（FindAttackAim：可见敌距离序逐敌试瞄，aimX/aimZ 千分向量上交）；移动档=
    /// **驻位点直交**（StandoffTargetCm 把敌锚拉攻击射程得目标点，targetX/targetZ 上交——A*+steering
    /// 全权交执行层 RTSMovementSystem）；距离=欧氏 cm（战术视距/核心锚定/滞回门槛全 cm 域）；
    /// 可达性预判=PathReachable（被地形/尸体围死跳过）；已进射程=驻位即最优（CD 期站桩等，勿对锚抖动）。
    /// 纯读 BattleSimState 由 Host 在选择阶段头生成（docs/18 决策一），行动与玩家行动合并进执行阶段。
    /// 决策档：爆发可命中（满槽）→ 爆发朝敌 → 战技可命中→打 → 否则按「核心锚定视野序」逐敌
    /// 沿最短路按移速步进 → 全部敌打不了也走不近=缺席。
    /// 眷属不配行为档案（启发式维持=层级特色，也是数量安全阀——场上眷属多，评分制算量敏感，
    /// docs/32 §6.1）。
    /// </summary>
    public static class FamiliarBrain
    {
        /// <summary>核心锚滞回锁（凌晨批议题③，2026-10-24 2v2 观察挂账「双核心等距线上眷属锚定
        /// 小步游移」）：familiar unitId → 锁定的敌方核心 unitId。static 生命周期=进程——DecideAll
        /// 里 turn≤1 自清（与 CompanionBrain E-3 同款零接线）。根治机制：纯距离序在绕行段
        /// 「到核 A 距离翻转超过到核 B」时逐回合换核=推进方向反复横跳；锁+门槛（另一核心须严格近
        /// 「锚点滞回门槛」格才接管=TPS CurrentHidepoint 惯性复核先例）使方向在等距邻域内稳定。
        /// 预测器复用（OpponentThreatModel.DecideOne 直跑）：锁定规则为一步收敛不动点（锁内
        /// 延续/显著更优换锁后，同输入重跑恒同输出）——预测与权威 DecideAll 同回合幂等。</summary>
        private static readonly Dictionary<string, string> _coreLocks = new Dictionary<string, string>();

        /// <summary>静态槽跨局自清判据（sim 实例号——2026-10-25 复审批修复，OpponentProfiler.EnsureFresh 同款）</summary>
        private static int _lastSimInstance = -1;

        /// <summary>
        /// 全部眷属单位的本回合行动（存活的；不可动者缺席=无行动——Pass 无结算意义不出命令）
        /// </summary>
        public static List<ActionData> DecideAll(BattleSimState sim, int turnNumber)
        {
            // 决策快照（纯读，与选择阶段广播同源状态）——技能预判的唯一状态源
            var snapshot = sim.TakeSnapshot(turnNumber);
            // 新战斗自清（2026-10-25 复审批修复：sim 实例号判据——旧 turn<=1 对秒弃调试局/回放失效；
            // docs/18 2026-10-15 四定案②：新静态 AI 注册表一律 InstanceId）
            if (sim.InstanceId != _lastSimInstance)
            {
                _coreLocks.Clear();
                _lastSimInstance = sim.InstanceId;
            }

            var actions = new List<ActionData>();
            foreach (var kv in sim.SortedUnits())
            {
                var unit = kv.Value;
                if (BattleHeuristics.IsBuilding(unit)) continue; // 建筑不参与任何行动（含未来低星建筑）
                if (!BattleHeuristics.IsFamiliar(unit)) continue;
                if (BattleSimState.IsDead(unit) || !BattleSimState.CanAct(unit)) continue;

                var action = DecideOne(sim, snapshot, kv.Key, unit, turnNumber);
                if (action != null)
                    actions.Add(action);
            }
            return actions;
        }

        /// <summary>
        /// 单个眷属单位决策：①爆发可命中且消耗门槛过（满槽即放，v5 2026-10-03 拍板）→ 爆发朝敌；
        /// ②战技可命中（十字向预判有存活敌）→ 战技朝敌；
        /// ③否则按距离升序逐敌试 BFS 最短路首步、按移速步进（v3 绕行+v4 换目标巡逻+v6 移速步数
        /// 单源——某敌贴身已到/不可达即换下一个目标，被挡由 MovementResolver 结算截回——被挡也算
        /// 已使用，眷属无体力配额=层级表 0 档）；
        /// ④无敌人/全场敌都无逼近步 → 缺席。
        /// v8（2026-10-10 拍板「可达性预判」）：移动档先算逼近步再判视野/距离——不可达敌（水上/
        /// 虚空/被围）直接跳过，避免「追一个永远打不到的目标，却忘了攻城」的傻站行为。
        /// E-1 对手建模复用（docs/archive/33 §2.1）：public 即预测器入口——启发式纯函数（同快照
        /// 恒同输出），配额脑对敌方眷属的预测=本方法原样直跑（「行为可被玩家轻易预测」决策三十一
        /// 特性的 AI 侧对偶——AI 玩家同样按可预测模型推演敌方眷属）
        /// </summary>
        public static ActionData DecideOne(BattleSimState sim, BattleSnapshot snapshot,
            string unitId, Unit unit, int turnNumber)
        {
            var identity = unit.GetUnitComponent<UnitIdentity>();
            if (identity == null) return null;

            // 爆发档（v5，2026-10-03 拍板「扩眷属脑会放爆发」）：满槽即放、优先于战技——单次伤害
            // 更高+倾倒元能（攒满即放勿囤积，同凛冽轮舞 AI 口径）；方向攻击型爆发与战技走同一
            // 十字预判口（单位指向/无目标自施放型无方向域不消费——眷属现无此类）
            var burstAction = TryCastAttack(sim, snapshot, unit, identity, unitId, turnNumber, SkillType.Burst);
            if (burstAction != null) return burstAction;

            // 战技（无该型技能=自动跳过此档；占位技能不可施放同理）
            var skillAction = TryCastAttack(sim, snapshot, unit, identity, unitId, turnNumber, SkillType.Normal);
            if (skillAction != null) return skillAction;

            // 朝敌驻位推进——v9（P5 连续版）：步数上限/直线段/十字方向全退役——移动=「选目标点」：
            // 每拍对锚敌计算 StandoffTargetCm 驻位点（敌锚拉攻击射程；射程 0=贴身接触，执行层碰撞圆
            // 停于接触前），targetX/targetZ 直交；速度与绕行全权交执行层（RTSMovementSystem A*+steering
            // 100cm/s tick 推进）；移速被压到 0=缺席不空耗（v6 语义保留）。
            // v7 目标序不变（核心锚定+视野门+严格近核）；v4 换目标巡逻不变（不可达敌 PathReachable 跳过）；
            // 已进射程=驻位即最优（CD 期站桩等下一拍，勿对锚抖动）。
            var liveStats = unit.GetUnitComponent<UnitStats>();
            if (liveStats == null || liveStats.MoveSpeed <= 0)
                return null; // 移速被压到 0（重减速）：缺席不空耗
            var enemies = BattleHeuristics.FindEnemiesByDistance(sim, unit);
            Unit core = null; // 敌方核心=tag 化（2026-10-13）：核心标签即永恒目标（协议核心/未来世界树同识别）
            var cores = new List<Unit>();
            foreach (var e in enemies)
                if (BattleHeuristics.IsCore(e)) cores.Add(e);
            if (cores.Count > 0)
            {
                core = cores[0]; // 距离序+unitId 决胜（FindEnemiesByDistance 既有确定性序）
                if (cores.Count > 1)
                {
                    // 核心锚滞回锁（议题③）：双核心场（2v2）——现锁存活且在门槛内延续，
                    // 另一核心须严格近「锚点滞回门槛」cm 才接管；锁失效（死亡/不可感知）自然重锁最近
                    if (_coreLocks.TryGetValue(unitId, out var lockedCoreId))
                    {
                        var lockedCore = sim.GetUnit(lockedCoreId);
                        if (lockedCore != null && !BattleSimState.IsDead(lockedCore) && cores.Contains(lockedCore))
                        {
                            int lockedDist = BattleHeuristics.DistanceCm(sim, unit, lockedCore); // 欧氏 cm（P5）
                            int nearestDist = BattleHeuristics.DistanceCm(sim, unit, cores[0]);
                            if (lockedDist <= nearestDist + AITuningConfig.Instance.锚点滞回门槛) // cm 域（P5 资产 ×100）
                                core = lockedCore; // 门槛内延续锁定（抗等距邻域距离翻转游移）
                        }
                    }
                    if (sim.TryGetUnitId(core, out var chosenCoreId))
                        _coreLocks[unitId] = chosenCoreId; // 重锁当前选择（无锁首回合即锁最近）
                }
            }
            int coreDist = core != null ? BattleHeuristics.DistanceCm(sim, unit, core) : int.MaxValue;
            int attackVision = BattleHeuristics.TacticalSightOf(unit); // 战术视距单源（cm——P5）
            int attackRangeCm = BattleHeuristics.AttackRangeCmOf(sim, unit, identity.OwnerPlayerID);

            foreach (var target in enemies)
            {
                if (target != core)
                {
                    int targetDist = BattleHeuristics.DistanceCm(sim, unit, target);
                    if (targetDist > attackVision) continue; // 攻击视野外：不关心（CR 式感知）
                    if (targetDist >= coreDist) continue;   // 不严格近于核心：不追（同距核心优先）
                }
                // 已进射程（敌圆边可及=dist ≤ 射程+敌半径，与命中几何同口径）：驻位即最优，不产移动
                //（战技 CD 期站桩等下一拍——对锚抖动防线；核心/其它敌同判）
                int distToTarget = BattleHeuristics.DistanceCm(sim, unit, target);
                if (distToTarget <= attackRangeCm + BattleHeuristics.UnitRadiusCm(target)) continue;

                // v8 可达性预判（连续版）：驻位点不可达（被地形/尸体围死）跳过——防「追一个
                // 永远打不到的目标，却忘了攻城」的傻站行为。核心也走同一口（防御性统一）
                var dest = BattleHeuristics.StandoffTargetCm(sim, unit, target, attackRangeCm);
                if (!BattleHeuristics.PathReachable(sim, unit, dest.x, dest.z)) continue;

                return new ActionData
                {
                    playerId = identity.OwnerPlayerID,
                    unitId = unitId,
                    actionType = ActionType.Move,
                    targetX = dest.x,
                    targetZ = dest.z,
                    turnNumber = turnNumber,
                };
            }
            return null; // 全场敌都不可达/已进射程无需动（或移速被压到 0）：缺席
        }

        /// <summary>
        /// 攻击技能施放尝试（战技/爆发共用，v5 爆发档抽提）：类型槽定位（FindSkillIndex——与
        /// SkillExecutor.GetSkill 同源索引）+ CanCast（占位技能不可施放）+ 消耗门槛（统一消耗模型
        /// C-2：costs 单源镜像 ResourceGate.HasAll——眷属豁免玩家资源、元能照查，与 Host 同口径）
        /// + **P5 任意角瞄准口**（FindAttackAim：可见敌距离序逐敌试瞄取首个可命中的 aimX/aimZ 千分
        /// 向量——取代十字向 FindAttackDirection；存活目标校验内建=纯尸体线不浪费行动）
        /// </summary>
        private static ActionData TryCastAttack(BattleSimState sim, BattleSnapshot snapshot,
            Unit unit, UnitIdentity identity, string unitId, int turnNumber, SkillType skillType)
        {
            int skillIndex = BattleHeuristics.FindSkillIndex(unit, skillType);
            if (skillIndex < 0) return null;
            var skill = unit.Skills[skillIndex];
            if (skill == null || !skill.CanCast(unit)) return null;
            // 攻击冷却门槛（决策九十五 P3，蓝图 §6）：CD 中不施放（眷属挥棒/全力挥棒同享 Normal 3 秒——
            // 即时制攻击节流；上交层 Host gate 兜底，此处防 0.5 秒节拍空决策）
            if (sim.IsSkillOnCooldown(unitId, skill.RawData.skillType)) return null;
            // AI 施放节流（P5-D）：无 CD 技能按 AITuning「AI 重施最短间隔 tick」节流（眷属现役全为
            // CD 攻击族=零影响，防线与伙伴脑/配额脑统一）
            if (BattleHeuristics.AiCastThrottled(sim, unitId, skillIndex)) return null;
            if (!ResourceGate.HasAll(sim, unit, identity.OwnerPlayerID, skill.RawData?.costs, out _)) return null;
            if (!BattleHeuristics.FindAttackAim(sim, snapshot, unit, skill, out var aimX, out var aimZ, null))
                return null;
            return new ActionData
            {
                playerId = identity.OwnerPlayerID,
                unitId = unitId,
                actionType = ActionType.Skill,
                skillIndex = skillIndex,
                aimX = aimX,
                aimZ = aimZ,
                turnNumber = turnNumber,
            };
        }
    }
}
