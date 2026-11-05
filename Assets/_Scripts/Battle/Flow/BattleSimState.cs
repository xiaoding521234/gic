using System;
using System.Collections.Generic;
using GIC.Data;
using GIC.Framework;
namespace GIC.Battle
{


    /// <summary>
    /// Host 侧战场状态访问门面（快照生成 / 效应应用 / unitId 注册 / 地形查询）。
    /// 逻辑 Unit = 现有 MonoBehaviour 组件容器（Host 场景实例化，docs/22 §4）。
    /// </summary>
    public class BattleSimState
    {
        public BattleMapData Map { get; }

        /// <summary>战斗种子（2026-10-02 幸运暴击批）：构造注入，模拟核心概率事件唯一随机源的
        /// 初始化种子——同 seed+同命令序列=同战局（回放/复现/联机确定性）；客户端零 roll 不消费</summary>
        public int Seed { get; }

        /// <summary>Host 概率事件 RNG（System.Random=种子可注入、与 Unity 帧无关）——模拟核心
        /// 唯一随机源；模拟核心禁直调 UnityEngine.Random（联机双端漂移/回放破坏），一切概率
        /// 判定走 RollChance 单出口</summary>
        private readonly System.Random _rng;

        /// <summary>已注册玩家ID（**注册序保序**——部署核心位置代理按注册序映射 spawnCenters、回合提交完成度判定用。
        /// 2026-09-23 审查 Y5：原 HashSet 迭代序 .NET 无保证，B7 换注册实现会静默错位部署基准）</summary>
        private readonly List<string> _playerIds = new List<string>();

        /// <summary>玩家资源（B1 只携带初始值）</summary>
        private readonly Dictionary<string, PlayerResourceState> _resources = new Dictionary<string, PlayerResourceState>();

        private readonly Dictionary<string, Unit> _units = new Dictionary<string, Unit>();
        private int _nextUnitId = 1;

        // ==================== 地脉维度（决策九十一，2026-10-25） ====================
        // 非主世界维度单位（当前=地脉灵魂；未来霜月等新维度单位同库，Unit.Dimension 区分归属维度）。
        // **独立注册表=维度隔离的结构实现**：主世界一切枚举域（AI 三脑/阻挡碰撞/威胁图/光环 tick/
        // 攻击目标/升命查找/核心查询）读 sim.Units——灵魂/相位单位天然不可见零过滤；效应应用与命令
        // 目标解析走 GetUnit 透明解析（unitId 双注册表联查）。地脉内交互（伤害/附着/治疗灵魂）由
        // 未来相位技能的维度感知域显式枚举本表。
        private readonly Dictionary<string, Unit> _crossDimensionUnits = new Dictionary<string, Unit>();

        /// <summary>尸体 unitId → 灵魂 unitId（灵魂↔尸体「同一实体的两半」系统级联动账本：
        /// 灵魂 HP 扣完→尸体随散；尸体被复苏→灵魂归体消散——TurnResolver 死亡分支/ReviveEffect 消费）</summary>
        private readonly Dictionary<string, string> _corpseSoulIds = new Dictionary<string, string>();

        /// <summary>非主世界维度单位（只读；当前=地脉灵魂。维度感知域/回合末维度规则/快照消费）</summary>
        public IReadOnlyDictionary<string, Unit> CrossDimensionUnits => _crossDimensionUnits;

        // ==================== 资源点（docs/47，2026-10-27 拍板「非地形非单位不占体积」） ====================
        // 独立注册表=独立实体拍板的结构实现（灵魂 _crossDimensionUnits 先例）：不进 _units（体积/
        // 阻挡/瞄准域天然零参与）、不进 TileType（docs/11 旧挂账「地形扩展」方向作废）。主世界专属
        // （地脉维度独立地形不演化资源点）。开局态从 Map.resourceNodes 复制（Map=开局协议快照恒
        // 静态，本注册表=运行时可变真值）；余量变化走 ResourceHarvest 命令镜像+回合头快照自愈。
        private readonly Dictionary<BattleCell, ResourceNodeState> _resourceNodes = new Dictionary<BattleCell, ResourceNodeState>();

        /// <summary>主世界资源点运行时视图（AI 采集候选/只读遍历；查询走 GetResourceNodeAt）</summary>
        public IReadOnlyCollection<ResourceNodeState> ResourceNodes => _resourceNodes.Values;

        /// <summary>查询格上资源点（无=返回 null；每格至多一个——BattleCell 唯一键）</summary>
        public ResourceNodeState GetResourceNodeAt(BattleCell cell)
        {
            return _resourceNodes.TryGetValue(cell, out var node) ? node : null;
        }

        /// <summary>
        /// 采集扣减（Host 权威单点）：余量-1、归零即从注册表移除（SC2 矿脉「耗尽从地图消失」拍板⑤）。
        /// 返回采集后剩余次数（-1=该格无资源点=未采集）。**调用方立即产出采集事实效应**
        /// （ResourceHarvestEffect+MoraGainEffect 等——余量在此已改，效应只承载事实勿重复扣）
        /// </summary>
        public int TryHarvestResourceNode(BattleCell cell)
        {
            if (!_resourceNodes.TryGetValue(cell, out var node) || node.remainingCharges <= 0) return -1;
            node.remainingCharges--;
            int remaining = node.remainingCharges;
            if (remaining <= 0) _resourceNodes.Remove(cell);
            return remaining;
        }

        /// <summary>地脉维度单位便捷枚举（Unit.Dimension==LeyLine 过滤——含灵魂与未来入地脉的相位单位）</summary>
        public IEnumerable<Unit> LeyLineUnits
        {
            get
            {
                foreach (var kv in _crossDimensionUnits)
                    if (kv.Value != null && kv.Value.Dimension == BattleDimension.LeyLine)
                        yield return kv.Value;
            }
        }

        /// <summary>曾注册核心账本（决策八十六批B 消散，2026-10-22，docs/45 §4）：playerId→
        /// 核心注册时 team。核心死亡走消散（_units 移除）后，「曾注册核心」记忆不再依赖尸体常驻——
        /// IsPlayerDefeated/TeamHasLivingCore/HasAnyRegisteredCore 的 anyCore 种子改读此账本
        /// （尸体在场的旧行为不受影响：账本覆盖已含在场核心）</summary>
        private readonly Dictionary<string, TeamType> _playerCoreTeam = new Dictionary<string, TeamType>();

        /// <summary>对局实例号（静态计数器自增——每局新 BattleSimState 恒新值；进程级静态 AI 注册表
        /// （AIStrategist/OpponentProfiler）的新战斗自清判据（2026-10-15 审查修正）：比「T1 重入」回合
        /// 判据可靠——上局仅观察到回合 1 的秒弃调试局会让回合判据失效（T1==T1 不触发）泄漏跨局陈旧态；
        /// 实例号恒新局必变、同局恒同，两种调用形态全覆盖。域重载随全部 static 一并归零=无跨重载歧义</summary>
        public readonly int InstanceId = ++_instanceCounter;
        private static int _instanceCounter;

        /// <summary>当前回合数（决策八十六批D，docs/45 §6——回合数真源：TurnResolver 每回合
        /// 开头写入；周期类效果（骑士团每 5 回合召唤）与 Unit.DeployedTurn 相对计数消费；0=回合开始前
        /// （开局 RegisterUnit 的单位部署回合计 0）。
        /// 即时制语义（决策九十五，docs/active/48 §2.3）：字段语义随回合机退役改为**逻辑秒**——
        /// RTSFlowController 每秒整拍同步写 CurrentTurn=CurrentSecond，周期类相对计数零改动兼容
        /// （每 1 秒走 1 拍=旧「每回合走 1」等价节拍，周期字段值按秒生效，P6 调参批集中校）</summary>
        public int CurrentTurn { get; set; }

        /// <summary>即时制 tick 真源（决策九十五，docs/active/48 §2）——RTSFlowController 每 tick 写入；
        /// 1 秒=20 tick（BattleMetrics.TicksPerSecond）</summary>
        public int CurrentTick { get; set; }

        /// <summary>当前逻辑秒（tick/20）</summary>
        public int CurrentSecond => CurrentTick / BattleMetrics.TicksPerSecond;

        /// <summary>片边界 poll 的即时行动队列（连携/契约类；B1 调试用）</summary>
        private readonly List<ActionData> _instantActionQueue = new List<ActionData>();

        /// <summary>Buff 注册表（全局注册序：回合结束效果按注册序结算，docs/22 §2）</summary>
        private readonly List<BaseBuff> _activeBuffs = new List<BaseBuff>();
        private int _buffSequence;

        public IReadOnlyList<BaseBuff> ActiveBuffs => _activeBuffs;

        public IReadOnlyDictionary<string, Unit> Units => _units;
        public IReadOnlyList<ActionData> InstantActionQueue => _instantActionQueue;

        public BattleSimState(BattleMapData map, int seed)
        {
            Map = map;
            Seed = seed;
            _rng = new System.Random(seed);
            // 资源点开局态复制（docs/47：Map=开局协议快照恒静态；本注册表=运行时可变真值）
            var nodes = map != null ? map.resourceNodes : null;
            if (nodes != null)
                foreach (var node in nodes)
                    if (node != null && node.remainingCharges > 0)
                        _resourceNodes[node.cell] = node.Clone();
        }

        /// <summary>概率判定单出口（2026-10-02 幸运暴击批）：percent 0~100（20 即 20%）；
        /// ≤0 恒否、≥100 恒是（负幸运=永不暴击、满幸运=必暴）。当前消费方=幸运暴击 roll
        /// （EffectCompiler.CompileElementalDamage：暴击率=攻击者幸运）；未来概率事件（核心宝箱
        /// 随机刷 docs/03 等）一律经此，勿在模拟核心散播第二随机源</summary>
        public bool RollChance(float percent)
        {
            if (percent <= 0f) return false;
            if (percent >= 100f) return true;
            return _rng.NextDouble() * 100.0 < percent;
        }

        /// <summary>逻辑单位隐藏根（装配时设置；部署等运行时生成单位挂此——纯逻辑容器勿落场景根，BattleSession._logicRoot 同源）</summary>
        public UnityEngine.Transform LogicRoot { get; set; }

        /// <summary>试招沙盒空座玩家（2026-10-05 时轮编辑器「开一把试招」）：该玩家每回合选择阶段
        /// 开始即自动上交 Pass（不等时限超时——木桩空座无真人无配额脑，TurnFlow.BeginSelectPhase 消费；
        /// null=常规对局零行为）</summary>
        public string SandboxAutoPassPlayerId;

        // ==================== 玩家注册 ====================

        public void RegisterPlayer(string playerId)
        {
            if (!_playerIds.Contains(playerId))
                _playerIds.Add(playerId); // 注册序即 spawnCenters 映射序（List 保序）
            if (!_resources.ContainsKey(playerId))
            {
                // 资源池 0 起步（2026-09-25 拍板「获得卡片=手牌构建唯一入口」）——开局初始 200/60
                // 不在此初始化，由装配期 GainCard 统一获得（编没编货币卡都送、落在牌上）；
                // BattleMetrics.InitialMora/InitialStamina 仍为口径常量（装配处引用）
                _resources[playerId] = new PlayerResourceState
                {
                    playerId = playerId,
                    mora = 0,
                    stamina = 0,
                    handCardCount = 0,
                    deckCardCount = 0,
                };
            }
        }

        public IReadOnlyList<string> PlayerIds => _playerIds;

        // ==================== 局内手牌与资源（2026-09-25 拍板：获得卡片=手牌构建唯一入口，完全统一） ====================

        private readonly Dictionary<string, List<HandCard>> _hands = new Dictionary<string, List<HandCard>>();

        /// <summary>注册玩家手牌（开局=空表起步，一切牌经 GainCard 获得）</summary>
        public void RegisterHand(string playerId, List<HandCard> handCards)
        {
            _hands[playerId] = handCards ?? new List<HandCard>();
            if (_resources.TryGetValue(playerId, out var res))
                res.handCardCount = _hands[playerId].Count;
        }

        public IReadOnlyList<HandCard> GetHand(string playerId)
        {
            return _hands.TryGetValue(playerId, out var hand) ? hand : null;
        }

        /// <summary>手牌条目列表（未注册手牌时建空表——GainCard 可先于 RegisterHand 安全调用）</summary>
        private List<HandCard> HandList(string playerId)
        {
            if (!_hands.TryGetValue(playerId, out var hand))
            {
                hand = new List<HandCard>();
                _hands[playerId] = hand;
            }
            return hand;
        }

        /// <summary>摩拉查询（无注册返回 0）</summary>
        public int GetMora(string playerId)
        {
            return _resources.TryGetValue(playerId, out var res) ? res.mora : 0;
        }

        /// <summary>体力查询（无注册返回 0）</summary>
        public int GetStamina(string playerId)
        {
            return _resources.TryGetValue(playerId, out var res) ? res.stamina : 0;
        }

        /// <summary>摩拉消耗（不足返回 false 且不改动；B6c 部署扣费。扣减走 ApplyMoraDelta——
        /// 池写自动同步摩拉牌条目存亡）</summary>
        public bool TrySpendMora(string playerId, int cost)
        {
            if (cost <= 0) return true;
            if (!_resources.TryGetValue(playerId, out var res) || res.mora < cost) return false;
            ApplyMoraDelta(playerId, -cost);
            return true;
        }

        /// <summary>体力是否够一次配额行动（B6d；消耗量收口 BattleMetrics.StaminaCostPerAction）</summary>
        public bool HasEnoughStamina(string playerId, int cost)
        {
            return cost <= 0 || GetStamina(playerId) >= cost;
        }

        /// <summary>摩拉增量（货币物品牌堆张数变化：回合结束发放/部署扣费/开局获得共用入口）。
        /// 池写后自动同步摩拉牌手牌条目存亡（SyncCurrencyEntry——获得与失去对称）</summary>
        public void ApplyMoraDelta(string playerId, int delta)
        {
            if (!_resources.TryGetValue(playerId, out var res)) return;
            res.mora = Math.Max(0, res.mora + delta);
            SyncCurrencyEntry(playerId, ItemName.Mora, res.mora);
        }

        /// <summary>体力增量（货币物品牌堆张数变化：回合结束发放/行动消耗/开局获得共用入口）。
        /// 池写后自动同步体力牌手牌条目存亡（SyncCurrencyEntry——获得与失去对称）</summary>
        public void ApplyStaminaDelta(string playerId, int delta)
        {
            if (!_resources.TryGetValue(playerId, out var res)) return;
            res.stamina = Math.Max(0, res.stamina + delta);
            SyncCurrencyEntry(playerId, ItemName.Stamina, res.stamina);
        }

        // ==================== 获得卡片 / 失去卡片（2026-09-25 拍板：手牌构建唯一入口，获得/失去对称） ====================

        /// <summary>
        /// 获得卡片（公用方法，用户拍板原语义：「如果手牌已经有该卡，则加数量，如果没有，
        /// 则加上这个卡」）。手牌构建/开局初始量/货币获得的统一入口——普通卡（角色/物品）数量=条目
        /// 真源；货币物品牌（摩拉/体力）数量走资源池（池即牌堆张数）。装配期调用（快照前）无需产
        /// 命令；局内获得（掠夺等）届时由调用方负责命令产出。
        /// **装备实例耐久（决策八十一「舍弃堆叠数量，统一采用耐久方案」）**：装备类物品（Weapon/
        /// Accessory）条目按耐久分实例——durability≥0=携带实例耐久（掠夺物保留）；-1（默认）=新鲜卡
        /// 解析配置 Durability 满值；非装备物品恒 0。同物同耐久合并数量、异耐久分行目。
        /// </summary>
        public void GainCard(string playerId, CardId cardId, int count, int durability = -1)
        {
            if (string.IsNullOrEmpty(playerId) || cardId.value <= 0 || count <= 0) return;

            // 装备实例耐久解析：-1=新鲜（配置满值——卡组投影/命座授予共用）；≥0=掠夺携带耐久
            int entryDurability = durability;
            if (durability < 0 && cardId.cardType == CardType.Item)
            {
                entryDurability = 0;
                var itemData = Wargame.Instance?.Context?.Get<ItemConfig>()?.GetItemData(cardId.AsItemName());
                if (itemData != null
                    && (itemData.subType == ItemSubType.Weapon || itemData.subType == ItemSubType.Accessory))
                    entryDurability = itemData.GetInt(nameof(SkillParamKey.Durability), 0);
            }

            var entry = FindHandEntry(playerId, cardId, entryDurability);
            if (entry != null && !IsCurrencyCard(cardId))
            {
                entry.count += count; // 普通卡：有则加数量（装备=同物同耐久实例合并）
                return;
            }

            if (IsCurrencyCard(cardId))
            {
                // 货币：池写（池>0 时 SyncCurrencyEntry 自动加卡——有则不重复加，无则加上这个卡）
                if (cardId.AsItemName() == ItemName.Mora) ApplyMoraDelta(playerId, count);
                else ApplyStaminaDelta(playerId, count);
                return;
            }

            // 普通卡：没有则加上这个卡
            var hand = HandList(playerId);
            var newCard = new HandCard(cardId, count);
            newCard.durability = entryDurability;
            hand.Add(newCard);
            if (_resources.TryGetValue(playerId, out var res))
                res.handCardCount = hand.Count;
        }

        /// <summary>
        /// 失去卡片（获得的对偶，用户拍板「失去数量时，同理」）：有则减数量、
        /// 减至零移除卡。普通卡=条目真源；货币牌=池减（空堆时条目移除，再发放经池自动复活——
        /// 与获得完全对称）。数量不足返回 false 且不改动。使用/装备消耗与掠夺由后续批次接线。
        /// **装备实例匹配（决策八十一）**：durability 参数=手牌条目实例耐久（装备按 (物,耐久) 分条目；
        /// 非装备恒 0——默认值兼容既有调用方）。
        /// </summary>
        public bool LoseCard(string playerId, CardId cardId, int count, int durability = 0)
        {
            if (count <= 0) return true;

            if (IsCurrencyCard(cardId))
            {
                int pool = cardId.AsItemName() == ItemName.Mora ? GetMora(playerId) : GetStamina(playerId);
                if (pool < count) return false;
                if (cardId.AsItemName() == ItemName.Mora) ApplyMoraDelta(playerId, -count);
                else ApplyStaminaDelta(playerId, -count);
                return true;
            }

            var entry = FindHandEntry(playerId, cardId, durability);
            if (entry == null || entry.count < count) return false;
            entry.count -= count;
            if (entry.count <= 0)
            {
                var hand = HandList(playerId);
                hand.Remove(entry);
                if (_resources.TryGetValue(playerId, out var res))
                    res.handCardCount = hand.Count;
            }
            return true;
        }

        private static bool IsCurrencyCard(CardId cardId)
        {
            return cardId.cardType == CardType.Item
                && (cardId.AsItemName() == ItemName.Mora || cardId.AsItemName() == ItemName.Stamina);
        }

        /// <summary>货币牌手牌条目存亡同步（池写后调用）：堆里有牌（池>0）→手牌必有该卡；
        /// 空堆（池=0）→手牌移除该卡。发放/消耗/部署扣费/开局获得一切池写自动走此同步——
        /// 货币牌的"加上这个卡/移除这个卡"无特判，与普通卡获得/失去语义对称。</summary>
        private void SyncCurrencyEntry(string playerId, ItemName item, int pool)
        {
            if (item != ItemName.Mora && item != ItemName.Stamina) return;
            var hand = HandList(playerId);
            CardId id = new CardId(item);
            var entry = FindHandEntry(playerId, id);
            if (pool > 0)
            {
                if (entry == null)
                {
                    hand.Add(new HandCard(id, pool));
                    if (_resources.TryGetValue(playerId, out var res))
                        res.handCardCount = hand.Count;
                }
                else
                {
                    // 2026-09-27 复审修复：已有条目同步池值——快照序列化虽会重映射（IsCurrency 分支），
                    // 但 in-sim 消费方读条目 count 会拿到陈旧值（潜伏双源），此处与快照双保险
                    entry.count = pool;
                }
            }
            else if (entry != null)
            {
                hand.Remove(entry);
                if (_resources.TryGetValue(playerId, out var res))
                    res.handCardCount = hand.Count;
            }
        }

        private HandCard FindHandEntry(string playerId, CardId cardId, int durability = 0)
        {
            if (!_hands.TryGetValue(playerId, out var hand)) return null;
            foreach (var entry in hand)
            {
                if ((CardType)entry.cardType == cardId.cardType && entry.value == cardId.value
                    && entry.durability == durability)
                    return entry;
            }
            return null;
        }

        /// <summary>玩家核心位置（B6c 部署落点判定基准）。核心 tag 化（2026-10-13）：真源=该玩家
        /// 存活的核心单位位置（多核心取 unitId 升序首个；核心免疫强制位移、永不移动）；
        /// 全部核心被击倒（战败已成立）或无核心（装配异常防御）回落出生区中心代理
        /// （spawnCenters 与 PlayerIds 注册序同序）</summary>
        public BattleCell GetCorePosition(string playerId)
        {
            var core = GetLivingCore(playerId);
            if (core != null) return GetPosition(core);
            int index = _playerIds.IndexOf(playerId);
            if (index >= 0 && Map.spawnCenters != null && index < Map.spawnCenters.Count)
                return Map.spawnCenters[index];
            return BattleCell.zero;
        }

        /// <summary>玩家队伍（部署新单位的阵营；装配时注册，P1=A/P2=B）</summary>
        private readonly Dictionary<string, TeamType> _playerTeams = new Dictionary<string, TeamType>();

        public void SetPlayerTeam(string playerId, TeamType team)
        {
            _playerTeams[playerId] = team;
        }

        public TeamType GetTeamOf(string playerId)
        {
            return _playerTeams.TryGetValue(playerId, out var team) ? team : TeamType.A;
        }

        // ==================== 迷雾账本（2026-10-17 追拍三「AI 也受雾限」，docs/active/44 §5b） ====================
        // Host 权威探索集：按队伍记已探索格——**仅 AI 三脑/威胁图消费**（玩家侧迷雾=客户端表现层
        // BattleFog 独立计算，两端同源口径=config 探索视野基值）；结算真值（EffectCompiler/执行器）**永不
        // 消费**——雾只限 AI 决策输入，不改变命中/移动结算。

        /// <summary>迷雾模式（装配期注入，BattleScreen：房间对战=Skirmish/任务=Mission/调试直开与沙盒
        /// =None）。None=AI 不受雾限（全知，观察局/沙盒零回归）</summary>
        public BattleFogMode FogMode { get; set; } = BattleFogMode.None;

        private Dictionary<TeamType, bool[]> _fogExplored;

        /// <summary>全量幂等揭雾（单调有进无出）：**每 AI 节拍（RTSFlow.RunAiBeat，0.5s）AI 决策前**调——
        /// 各队伍以存活单位（含协议核心等建筑；尸体不探索）为心、探索视野
        /// （UnitStats.VisionRange 终值=config 基值+1命 StatBoost，与快照 exploreVision 同源——追拍六
        /// 「两侧统一升档」；stats 缺失回落 RawData 基值）揭雾——**P5 视野 cm 化：视野值=厘米，
        /// 揭雾半径=÷CellSizeCm 回格域方块**（与客户端 BattleFog.EvaluateReveal 同形）。即时制无回合边界
        /// 全量同步=每拍增量揭雾，决策时态恒新鲜</summary>
        public void RevealFog()
        {
            if (FogMode == BattleFogMode.None || Map == null) return;
            if (_fogExplored == null) _fogExplored = new Dictionary<TeamType, bool[]>();
            foreach (var kv in _units)
            {
                var unit = kv.Value;
                var identity = unit?.GetUnitComponent<UnitIdentity>();
                if (identity == null || IsDead(unit)) continue;
                if (!_fogExplored.TryGetValue(identity.Team, out var explored))
                {
                    explored = new bool[Map.width * Map.height];
                    _fogExplored[identity.Team] = explored;
                }
                // 探索视野真源=UnitStats.VisionRange 终值（config 基值+1命 StatBoost——与快照 exploreVision
                // 同源，追拍六「两侧统一升档」）；stats 缺失回落 RawData 基值。
                // 决策九十五 P5 视野 cm 化：值域=厘米——揭雾格半径=÷CellSizeCm（两侧与客户端 BattleFog 同口径）
                var stats = unit.GetUnitComponent<UnitStats>();
                int radiusCm = stats != null ? stats.VisionRange
                    : (unit.RawData != null ? unit.RawData.GetEffectiveExploreVision() : CellMetrics.CellSizeCm);
                int radius = radiusCm / CellMetrics.CellSizeCm;
                if (radius <= 0) continue;
                var pos = GetPosition(unit);
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int y = pos.y + dy;
                    if (y < 0 || y >= Map.height) continue;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int x = pos.x + dx;
                        if (x < 0 || x >= Map.width) continue;
                        explored[y * Map.width + x] = true;
                    }
                }
            }

            // 视野暴露（安柏变奏「全面侦查」，追拍七）：持有者所在格揭给施加者阵营（**非全队公开**
            // ——与客户端 BattleFog 随行揭雾同口径；BeginSelectPhase 全量同步已覆盖决策时点）
            foreach (var kv in _units)
            {
                var unit = kv.Value;
                if (unit == null || IsDead(unit)) continue;
                foreach (var b in unit.Buffs)
                {
                    if (b == null || b.Type != BuffType.VisionExposed) continue;
                    var srcIdentity = b.source?.GetUnitComponent<UnitIdentity>();
                    if (srcIdentity == null) continue;
                    var cell = GetPosition(unit);
                    if (!_fogExplored.TryGetValue(srcIdentity.Team, out var explored))
                    {
                        explored = new bool[Map.width * Map.height];
                        _fogExplored[srcIdentity.Team] = explored;
                    }
                    if (Map.InBounds(cell.x, cell.y))
                        explored[cell.y * Map.width + cell.x] = true;
                    break;
                }
            }
        }

        /// <summary>该格是否已被该队伍探索（None 恒 true；越界恒 true——边角格不参与雾语义）</summary>
        public bool IsFogExplored(TeamType team, BattleCell cell)
        {
            if (FogMode == BattleFogMode.None || Map == null) return true;
            if (!Map.InBounds(cell.x, cell.y)) return true;
            return _fogExplored != null && _fogExplored.TryGetValue(team, out var explored)
                && explored[cell.y * Map.width + cell.x];
        }

        /// <summary>按队伍批量揭雾（安柏 2命「爆发破雾」Host 消费，docs/active/44 §9-②）：把格集并入
        /// 该队探索账本（AI 雾限同享），返回**新探索格数**（供破雾计元能——每 C2FogEnergyThreshold 格
        /// +C2FogEnergyGain）；单调有进无出、幂等，与 RevealFog 同账本。None/越界直通返回 0</summary>
        public int RevealFogCells(TeamType team, List<BattleCell> cells)
        {
            if (FogMode == BattleFogMode.None || Map == null || cells == null || cells.Count == 0) return 0;
            if (_fogExplored == null) _fogExplored = new Dictionary<TeamType, bool[]>();
            if (!_fogExplored.TryGetValue(team, out var explored))
            {
                explored = new bool[Map.width * Map.height];
                _fogExplored[team] = explored;
            }
            int added = 0;
            foreach (var c in cells)
            {
                if (!Map.InBounds(c.x, c.y)) continue;
                int idx = c.y * Map.width + c.x;
                if (explored[idx]) continue;
                explored[idx] = true;
                added++;
            }
            return added;
        }

        /// <summary>AI 迷雾可见性单出口（追拍三拍板「AI 也受雾限，但自带透视协议核心的位置，否则无法
        /// 自主攻城」）：同队恒可见；**敌方核心（Core tag）恒可见**（攻城锚/终局判据/战力核心 HP 全靠它
        /// ——眷属 v7 核心锚定零回归）；其余敌方=所在格已被该队探索。消费方=AI 三脑/威胁图/战略层；
        /// 结算真值勿调（雾限决策不限结算）</summary>
        public bool IsUnitVisibleTo(TeamType viewerTeam, Unit enemy)
        {
            if (enemy == null || FogMode == BattleFogMode.None) return true;
            var identity = enemy.GetUnitComponent<UnitIdentity>();
            if (identity == null || identity.Team == viewerTeam) return true;
            if (BattleHeuristics.IsCore(enemy)) return true; // 核心透视（拍板）
            if (IsExposed(enemy)) return true; // 视野暴露（追拍七；追拍八改口径）：解除隐身=**所有人可见**
            return IsFogExplored(viewerTeam, GetPosition(enemy));
        }

        /// <summary>AI 迷雾可见性（快照态重载——BattleHeuristics.PredictAimTargets 等 UnitState 域消费）：
        /// 同队/None/核心（经 unitId 反查活体）恒可见；其余=所在格已探索</summary>
        public bool IsEnemyStateVisibleTo(TeamType viewerTeam, UnitState enemyState)
        {
            if (enemyState == null || FogMode == BattleFogMode.None) return true;
            if ((TeamType)enemyState.team == viewerTeam) return true;
            var unit = GetUnit(enemyState.unitId);
            if (unit != null && BattleHeuristics.IsCore(unit)) return true; // 核心透视（拍板）
            if (IsExposedState(enemyState)) return true; // 视野暴露（追拍八）：所有人可见
            return IsFogExplored(viewerTeam, enemyState.position);
        }

        /// <summary>该单位是否处于视野暴露（安柏变奏「全面侦查」，追拍七）：解除隐身并无法隐身——
        /// **追拍八拍板「解除隐身应当是清除掉这个buff，因此是所有人可见」**：隐匿态清除=对全部队伍
        /// 可见（不按施加者阵营过滤；将来实装真·隐身 Buff 时 OnApplied 顺手驱散之——数据钩子注记）；
        /// 尸体持有者同判（Buff 族 RemoveOnHolderDeath=false，暴露不因倒下消失）。
        /// 所在格揭雾仍=施加者阵营独享（RevealFog 暴露段，非本判定）</summary>
        public static bool IsExposed(Unit unit)
        {
            if (unit == null || unit.Buffs == null) return false;
            foreach (var b in unit.Buffs)
                if (b != null && b.Type == BuffType.VisionExposed) return true;
            return false;
        }

        /// <summary>快照态视野暴露判定（IsEnemyStateVisibleTo 用）：BuffState.type 携带即真（追拍八：
        /// 所有人可见——不查 source 阵营）</summary>
        private static bool IsExposedState(UnitState state)
        {
            if (state == null || state.buffs == null) return false;
            foreach (var b in state.buffs)
                if (b != null && b.type == (int)BuffType.VisionExposed) return true;
            return false;
        }

        // ==================== 单位注册 ====================

        /// <summary>
        /// 注册逻辑单位（分配 unitId 并写入身份/位置）
        /// </summary>
        public string RegisterUnit(Unit unit, string playerId, TeamType team, BattleCell position)
        {
            string unitId = $"U{_nextUnitId++}";
            unit.GetUnitComponent<UnitIdentity>()?.SetIdentity(unitId, playerId, team);
            unit.GetUnitComponent<UnitGridPosition>()?.SetPosition(BoardType.MainWorld, position.ToVector2Int());
            _units[unitId] = unit;
            // 核心身份已 tag 化（2026-10-13 拍板「所有持 核心 标签的单位全部被击倒才判战败」）——
            // 胜负族改动态查询 Core tag，无注册表：未来世界树等新核心单位=UnitConfig 配 [核心] tag
            // 零代码加入（旧 UnitName.ProtocolCore 单核心登记表已退役）
            // 曾注册核心账本（决策八十六批B 消散，2026-10-22）：核心死亡走消散（_units 移除）后
            // 「曾注册核心」记忆不再依赖尸体常驻——IsPlayerDefeated/TeamHasLivingCore 的 anyCore 种子
            // 改读此账本（批次登记时记 team，供队伍判负口径）；同一玩家多核心=幂等覆写同 team
            if (BattleHeuristics.IsCore(unit))
                _playerCoreTeam[playerId] = team;
            // 部署回合计数（决策八十六批D：周期类建筑效果以部署回合为 0 相对计数——骑士团
            // 「部署后每过 5 回合」；开局单位（RegisterUnit 于回合 0 前）计 0）
            unit.DeployedTurn = CurrentTurn;
            // 命座登场 Buff 授予（B8 批：0命固有被动的 Buff 形态——芭芭拉「获得歌声之环」，
            // OnDeploy[ApplyBuff] 原子；随后的 BuildUnitState 携带 buffs，客户端零额外命令）
            ConstellationApplier.ApplyDeployBuffs(this, unit);
            return unitId;
        }

        /// <summary>移除逻辑单位（决策八十六批B 消散专用，docs/45 §4）：_units 移除+GameObject
        /// 销毁——死亡后不留尸体（建筑+眷属）；Buff 注销由调用方（TurnResolver 死亡分支）先行完成，
        /// 附着/组件状态随 GO 销毁同殁。格子占用/碰撞/升命同名查找/鞭尸 tick 全随之天然消失。
        /// **勿用于其它场景**（现役单位移除无消费者——复活/驱逐等未来需求须另立语义；
        /// 灵魂/非主维度单位移除走 RemoveSoul/RemoveLinkedSoul）</summary>
        public void RemoveUnit(Unit unit)
        {
            if (unit == null) return;
            var unitId = unit.GetUnitComponent<UnitIdentity>()?.UnitID;
            if (unitId != null && _units.TryGetValue(unitId, out var registered) && registered == unit)
                _units.Remove(unitId);
            DestroyUnitObject(unit);
        }

        /// <summary>GO 销毁单点（RemoveUnit/RemoveSoul 共用）：LogicRoot 下的单位物体（含全部组件状态
        /// ——附着/冻结/装备计数器等）。双域销毁（2026-10-24 回放回归实锤）：ReplayRunner 编辑器域
        /// headless 重放同走本链——Object.Destroy 编辑模式非法（警告+不销毁=场景对象泄漏，一次完整局
        /// 回放 34 条）；Play 域维持 Destroy 勿改（DestroyImmediate 在播放态会打断协程/在途引用），
        /// 编辑域用 DestroyImmediate。</summary>
        private static void DestroyUnitObject(Unit unit)
        {
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(unit.gameObject);
            else UnityEngine.Object.DestroyImmediate(unit.gameObject);
        }

        // ==================== 地脉维度（决策九十一）：灵魂注册/移除/链接 ====================

        /// <summary>注册地脉灵魂（TurnResolver 死亡分支调用——留尸体单位倒下时同位生成）：
        /// unitId 与主世界共用同一自增计数器（回放确定性：同输入同命令流=同 unitId 序列）；
        /// 灵魂不进 _units（维度隔离结构实现）、不跑 OnDeploy 登场 Buff（灵魂无 Buff 拍板）。
        /// DeployedTurn=死亡当回合（回合末衰减首回合豁免判据——完整 10 回合驻留）</summary>
        public string RegisterSoul(Unit soul, string playerId, TeamType team, BattleCell position, string corpseUnitId)
        {
            string unitId = $"U{_nextUnitId++}";
            soul.GetUnitComponent<UnitIdentity>()?.SetIdentity(unitId, playerId, team);
            soul.GetUnitComponent<UnitGridPosition>()?.SetPosition(BoardType.MainWorld, position.ToVector2Int());
            soul.IsSoul = true;
            soul.LinkedCorpseUnitId = corpseUnitId;
            soul.Dimension = BattleDimension.LeyLine;
            _crossDimensionUnits[unitId] = soul;
            _corpseSoulIds[corpseUnitId] = unitId;
            soul.DeployedTurn = CurrentTurn;
            return unitId;
        }

        /// <summary>移除灵魂（死亡循环「灵魂+尸体双双消散」分支用）：跨维度注册表+尸体链接账本
        /// 双清+GO 销毁；灵魂本体无 Buff 无装备（基础属性拍板），无残留清理</summary>
        public void RemoveSoul(Unit soul)
        {
            if (soul == null) return;
            var unitId = soul.GetUnitComponent<UnitIdentity>()?.UnitID;
            if (unitId != null && _crossDimensionUnits.TryGetValue(unitId, out var registered) && registered == soul)
            {
                _crossDimensionUnits.Remove(unitId);
                if (soul.LinkedCorpseUnitId != null
                    && _corpseSoulIds.TryGetValue(soul.LinkedCorpseUnitId, out var linkedId) && linkedId == unitId)
                    _corpseSoulIds.Remove(soul.LinkedCorpseUnitId);
            }
            DestroyUnitObject(soul);
        }

        /// <summary>尸体所链灵魂（无链接/已消散返回 null——复活门判据+归体消散消费）</summary>
        public Unit GetLinkedSoul(string corpseUnitId)
        {
            return corpseUnitId != null && _corpseSoulIds.TryGetValue(corpseUnitId, out var soulId)
                && _crossDimensionUnits.TryGetValue(soulId, out var soul)
                    ? soul : null;
        }

        /// <summary>移除尸体所链灵魂（复苏归体——ReviveEffect 应用层调用）：返回被移除灵魂 unitId
        /// （命令层 Death(dissolve) 发射用；无链接=返回 null 零动作）</summary>
        public string RemoveLinkedSoul(string corpseUnitId)
        {
            if (corpseUnitId == null || !_corpseSoulIds.TryGetValue(corpseUnitId, out var soulId))
                return null;
            _corpseSoulIds.Remove(corpseUnitId);
            if (_crossDimensionUnits.TryGetValue(soulId, out var soul))
            {
                _crossDimensionUnits.Remove(soulId);
                DestroyUnitObject(soul);
            }
            return soulId;
        }

        /// <summary>unitId 解析（**维度透明**：主世界 _units 优先、跨维度注册表兜底——效应应用/
        /// 命令目标解析按 id 找灵魂与找主世界单位同口径；枚举域不透明=维度隔离的结构前提）</summary>
        public Unit GetUnit(string unitId)
        {
            if (unitId == null) return null;
            if (_units.TryGetValue(unitId, out var unit)) return unit;
            return _crossDimensionUnits.TryGetValue(unitId, out var soul) ? soul : null;
        }

        public bool TryGetUnitId(Unit unit, out string unitId)
        {
            unitId = unit?.GetUnitComponent<UnitIdentity>()?.UnitID;
            return unitId != null && (_units.ContainsKey(unitId) || _crossDimensionUnits.ContainsKey(unitId));
        }

        // ==================== 存活/状态判定 ====================

        public static bool IsDead(Unit unit)
        {
            return unit?.GetUnitComponent<UnitStatus>()?.IsDead ?? true;
        }

        public static bool CanAct(Unit unit)
        {
            var status = unit?.GetUnitComponent<UnitStatus>();
            return status != null && status.CanAct;
        }

        /// <summary>该队伍是否还有存活单位（旧全灭软停检测——核心判据落地后仅作装配异常防御回落，
        /// 正常对局胜负唯一判据=协议核心，TurnFlowController.CheckBattleOver 消费）</summary>
        public bool HasLivingUnits(TeamType team)
        {
            foreach (var kv in _units)
            {
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id != null && id.Team == team && !IsDead(kv.Value))
                    return true;
            }
            return false;
        }

        // ==================== 核心（2026-09-29 Unit 化 → 2026-10-13 拍板 tag 化：所有持 核心 标签
        // 的单位=胜负判据单位——同一玩家全部核心被击倒才判战败；动态查询无注册表，
        // 未来世界树等新核心单位=UnitConfig 配 [核心] tag 零代码加入） ====================

        /// <summary>该玩家的全部核心单位（含尸体；按 unitId 升序稳定枚举）</summary>
        public List<Unit> GetCoreUnits(string playerId)
        {
            var result = new List<Unit>();
            foreach (var kv in _units)
            {
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID != playerId) continue;
                if (BattleHeuristics.IsCore(kv.Value)) result.Add(kv.Value);
            }
            return result;
        }

        /// <summary>玩家的存活核心（无存活核心返回 null；多核心取 unitId 升序首个——
        /// 与 GetCorePosition 同源语义：核心全灭后部署基准回落出生区中心代理）</summary>
        public Unit GetLivingCore(string playerId)
        {
            Unit first = null;
            string firstId = null;
            foreach (var kv in _units)
            {
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID != playerId) continue;
                if (!BattleHeuristics.IsCore(kv.Value) || IsDead(kv.Value)) continue;
                if (first == null || string.Compare(kv.Key, firstId, StringComparison.Ordinal) < 0)
                {
                    first = kv.Value;
                    firstId = kv.Key;
                }
            }
            return first;
        }

        /// <summary>该玩家是否仍有存活核心（无任何核心单位=视作存活，避免装配异常秒判负）</summary>
        public bool HasLivingCore(string playerId)
        {
            foreach (var kv in _units)
            {
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID != playerId) continue;
                if (BattleHeuristics.IsCore(kv.Value) && !IsDead(kv.Value)) return true;
            }
            return false;
        }

        /// <summary>该玩家是否战败成立（2026-10-13 拍板「所有持 核心 标签的单位全部被击倒才判
        /// 战败」）：曾注册核心单位 且 当前无存活核心；从未有核心=未败（装配异常防御口径）。
        /// 「曾注册」种子=核心账本（消散批 2026-10-22：核心死亡后 _units 移除，尸体不再常驻——
        /// 账本保证判负记忆不丢；多核心时代账本+在场存活双查同口径）</summary>
        public bool IsPlayerDefeated(string playerId)
        {
            bool anyCore = _playerCoreTeam.ContainsKey(playerId);
            foreach (var kv in _units)
            {
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id == null || id.OwnerPlayerID != playerId) continue;
                if (!BattleHeuristics.IsCore(kv.Value)) continue;
                anyCore = true;
                if (!IsDead(kv.Value)) return false; // 仍有存活核心=未败
            }
            return anyCore; // 有过核心且无存活=败；从未有核心=未败
        }

        /// <summary>全对局是否存在任何核心单位（装配异常判定：无核心对局回落旧全灭口径）；
        /// 账本种子（消散批：核心全灭移除后仍返回 true——「对局曾有核心」是装配期事实）</summary>
        public bool HasAnyRegisteredCore()
        {
            if (_playerCoreTeam.Count > 0) return true;
            foreach (var kv in _units)
                if (BattleHeuristics.IsCore(kv.Value)) return true;
            return false;
        }

        /// <summary>任一玩家战败成立（单玩家口径查询——曾注册核心且当前无存活核心）。
        /// ⚠️ 勿再用作回合截断/终止判据：多人口径下单玩家战败≠战斗终局（2v2 队友核心仍在），
        /// 且核心一倒它对余生每回合恒真——旧片末检查用它导致「此后每回合只结算首个攻速片、
        /// 速度 30/10 档/即时队列/回合末段全停摆」的跛行态（2026-10-24 实战报障，docs/14 §171；
        /// TurnResolver 片末检查已改用 IsBattleDecided 队伍口径）。保留作按玩家战败状态查询。</summary>
        public bool AnyCoreDestroyed()
        {
            var seen = new HashSet<string>();
            foreach (var kv in _units)
            {
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id == null || !seen.Add(id.OwnerPlayerID)) continue;
                if (IsPlayerDefeated(id.OwnerPlayerID)) return true;
            }
            return false;
        }

        /// <summary>战斗是否已分出胜负（片末检查判据——与 TurnFlowController.CheckBattleOver
        /// 广播口径严格同构：有登记核心 且 恰好一队仍有存活核心）。1v1 与旧「任一玩家战败」口径
        /// 逐点等价（每队恰一人）；2v2 中单玩家核心倒下≠终局（队友核心仍在=未分胜负→剩余片照常
        /// 结算）；双方核心同回合互灭=零队存活=未「分出」→不截断（平局回合照常走完，B8 结算口径
        /// ——避免互灭态再次落入每回合截断）。</summary>
        public bool IsBattleDecided()
        {
            if (!HasAnyRegisteredCore()) return false;
            var seenTeams = new HashSet<TeamType>();
            int aliveTeams = 0;
            foreach (var pid in PlayerIds)
            {
                var team = GetTeamOf(pid);
                if (!seenTeams.Add(team)) continue;
                if (TeamHasLivingCore(team))
                {
                    aliveTeams++;
                    if (aliveTeams > 1) return false; // 两队核心都存活：战斗继续
                }
            }
            return aliveTeams == 1; // 恰好一队存活=胜负已分；零队（互灭平局）=不截断
        }

        /// <summary>队伍是否仍有存活的核心（多人：队内任一玩家的核心存活=该队未败；
        /// 队内无任何核心单位=视作未败——防御口径；队败=该队全部玩家的核心全灭）。
        /// 「队内曾有核心」种子=核心账本（消散批 2026-10-22：队内核心全部消散移除后 anyCore
        /// 仍由账本成立→正确判队败；在场存活判定维持迭代 _units）</summary>
        public bool TeamHasLivingCore(TeamType team)
        {
            bool anyCore = false;
            foreach (var kv in _playerCoreTeam)
                if (kv.Value == team) { anyCore = true; break; }
            foreach (var kv in _units)
            {
                var id = kv.Value.GetUnitComponent<UnitIdentity>();
                if (id == null || id.Team != team || !BattleHeuristics.IsCore(kv.Value)) continue;
                anyCore = true;
                if (!IsDead(kv.Value)) return true; // 队内任一玩家有存活核心=未败
            }
            return !anyCore;
        }

        // ==================== 位置查询 ====================

        public BattleCell GetPosition(Unit unit)
        {
            var pos = unit.GetUnitComponent<UnitGridPosition>();
            return pos != null ? new BattleCell(pos.Position) : BattleCell.zero;
        }

        public BattleCell GetPosition(string unitId)
        {
            var unit = GetUnit(unitId);
            return unit != null ? GetPosition(unit) : BattleCell.zero;
        }

        public void SetPosition(Unit unit, BattleCell cell)
        {
            unit.GetUnitComponent<UnitGridPosition>()?.SetPosition(BoardType.MainWorld, cell.ToVector2Int());
        }

        // ==================== 连续坐标（决策九十五 P2，蓝图 §3.1：格→连续双轨） ====================

        /// <summary>连续位置（厘米，真源——UnitGridPosition.PositionCm 门面；格坐标 GetPosition 由其派生）</summary>
        public UnityEngine.Vector2Int GetPosCm(Unit unit)
            => unit?.GetUnitComponent<UnitGridPosition>()?.PositionCm ?? UnityEngine.Vector2Int.zero;

        /// <summary>连续位置写入（tick 移动推进每步调用——格缓存自动派生，全链 GetPosition 零改动兼容）</summary>
        public void SetPosCm(Unit unit, int xCm, int zCm)
            => unit?.GetUnitComponent<UnitGridPosition>()?.SetPositionCm(xCm, zCm);

        /// <summary>即时制 tick 移动注册表（RTSMovementSystem 推进/移除；RTSFlow 每 tick 调 AdvanceTick）</summary>
        private readonly List<RTSMoveState> _activeMovers = new List<RTSMoveState>();

        public IReadOnlyList<RTSMoveState> ActiveMovers => _activeMovers;

        /// <summary>tick 移动注册表写入（TurnResolver 启动移动时 Add；推进器内部 Remove）</summary>
        public List<RTSMoveState> ActiveMoversInternal => _activeMovers;

        // ==================== 攻击冷却（决策九十五 P3，蓝图 §4.3——战技 3 秒/爆发 5 秒） ====================
        // 纪元制（到期 tick 记账，非逐 tick 递减——零每帧遍历、回放由命令流派生天然确定）：
        // Normal 型（战技+AI 普攻条目同享）→攻击 CD；Burst 型→爆发 CD；其余类型（延奏/契约/势力/
        // 变奏/移动/命座）无 CD。单位死亡不清条目（量=单位数上限，无泄漏面；unitId 不复用）。

        /// <summary>战技 CD 就绪 tick（=可再次施放的最早 tick；缺省 0=就绪）</summary>
        private readonly Dictionary<string, int> _attackCdReadyTick = new Dictionary<string, int>();

        /// <summary>爆发 CD 就绪 tick（同上；元能门槛仍走 costs 资源链=双 gate）</summary>
        private readonly Dictionary<string, int> _burstCdReadyTick = new Dictionary<string, int>();

        /// <summary>该技能类型对单位是否处于 CD 中（Normal→攻击 CD；Burst→爆发 CD；其余类型恒 false）</summary>
        public bool IsSkillOnCooldown(string unitId, GIC.Data.SkillType skillType)
        {
            if (skillType == GIC.Data.SkillType.Burst)
                return SkillCdRemainTicks(unitId, skillType) > 0;
            if (skillType == GIC.Data.SkillType.Normal)
                return SkillCdRemainTicks(unitId, skillType) > 0;
            return false;
        }

        /// <summary>技能 CD 剩余 tick（0=就绪；非法/无 CD 类型恒 0）</summary>
        public int SkillCdRemainTicks(string unitId, GIC.Data.SkillType skillType)
        {
            var table = skillType == GIC.Data.SkillType.Burst ? _burstCdReadyTick : _attackCdReadyTick;
            if (skillType != GIC.Data.SkillType.Burst && skillType != GIC.Data.SkillType.Normal) return 0;
            if (!table.TryGetValue(unitId, out int readyTick)) return 0;
            int remain = readyTick - CurrentTick;
            return remain > 0 ? remain : 0;
        }

        /// <summary>消耗技能 CD（门槛全过后调用——与资源扣减同拍；写就绪 tick 纪元）</summary>
        public void ConsumeSkillCooldown(string unitId, GIC.Data.SkillType skillType)
        {
            int cdTicks = skillType == GIC.Data.SkillType.Burst
                ? BattleMetrics.BurstCooldownTicks
                : BattleMetrics.CasterCooldownTicks;
            if (skillType != GIC.Data.SkillType.Burst && skillType != GIC.Data.SkillType.Normal) return;
            var table = skillType == GIC.Data.SkillType.Burst ? _burstCdReadyTick : _attackCdReadyTick;
            table[unitId] = CurrentTick + cdTicks;
        }

        /// <summary>
        /// 获取某格上的全部单位（含尸体——尸体保留碰撞/体积，docs/05 §5.4）
        /// </summary>
        public List<Unit> GetUnitsAt(BattleCell cell)
        {
            var result = new List<Unit>();
            foreach (var kv in _units)
            {
                if (GetPosition(kv.Value).Equals(cell))
                    result.Add(kv.Value);
            }
            return result;
        }

        /// <summary>
        /// 某格现有单位体积之和（可排除指定单位）
        /// </summary>
        public int GetVolumeAt(BattleCell cell, Unit exclude = null)
        {
            int total = 0;
            foreach (var unit in GetUnitsAt(cell))
            {
                if (unit != exclude)
                    total += unit.Volume;
            }
            return total;
        }

        // ==================== 效应应用 ====================

        /// <summary>
        /// 应用伤害（HP 扣减，RangedInt 自动钳 0）
        /// </summary>
        public void ApplyDamage(Unit target, int amount)
        {
            var stats = target.GetUnitComponent<UnitStats>();
            if (stats == null) return;
            var hp = stats.GetStatStruct(StatType.HP);
            hp.Subtract(amount);
            stats.SetStatStruct(StatType.HP, hp);
        }

        /// <summary>
        /// 应用治疗
        /// </summary>
        public void ApplyHeal(Unit target, int amount)
        {
            var stats = target.GetUnitComponent<UnitStats>();
            if (stats == null) return;
            var hp = stats.GetStatStruct(StatType.HP);
            hp.Add(amount);
            stats.SetStatStruct(StatType.HP, hp);
        }

        /// <summary>
        /// 应用元能变化（B6a：正=获取——移动+10/战技至少1次命中+10；负=技能消耗。
        /// 上限=UnitConfig baseEnergy（GetEffectiveEnergy）+命座容量加成（SetStatRange 直扩）；消耗值=技能条目 EnergyCost。
        /// 溢出转移（B8 命座批，安柏1命）：delta>0 且目标带 EnergyOverflowPassive 且溢出——自身只留
        /// 装得下的部分，溢出转移给切比雪夫最近未满元能我方存活角色（同距 unitId 升序；全满则无效果）；
        /// 接收方经 out 回传给调用方（TurnResolver 转移效应命令），本层不做二次转移（单跳）。
        /// </summary>
        public void ApplyEnergy(Unit target, int delta)
        {
            ApplyEnergy(target, delta, out _, out _);
        }

        /// <summary>带溢出转移的元能应用（见上——overflowReceiverId/overflowAmount 回传转移事实）。
        /// **元能通则（决策八十六批D 拍板 #8，docs/45 §5；2026-10-24 追记四修订）**：一切**加元能**
        /// 对**无元能上限**的建筑一律无效（能量应用段统一过滤单源；建筑无元能条 baseEnergy=0）；
        /// **有元能上限的建筑（骑士团总部 EnergySummon 元能引擎 baseEnergy=120）照常受能**——
        /// 芭芭拉歌声之环 tick 等外部充能由此接入（推翻旧「一律无效」口径）；对倒下角色照常生效
        /// （tick 含尸体先例维持——眷属/建筑无尸体，倒下=伙伴/魔神）。负增量（消耗）不受此滤
        /// ——建筑无技能无消耗路径，防御性放行。**溢出转移候选域维持排除建筑**（安柏1命单跳
        /// 接收方=「角色」设计未变，且转移写入不经本方法直改 stats——留排除免直改旁路）</summary>
        public void ApplyEnergy(Unit target, int delta, out string overflowReceiverId, out int overflowAmount)
        {
            overflowReceiverId = null;
            overflowAmount = 0;
            if (target == null) return;
            var stats = target.GetUnitComponent<UnitStats>();
            if (stats == null) return;
            var energy = stats.GetStatStruct(StatType.Energy);
            if (delta > 0 && BattleHeuristics.IsBuilding(target) && energy.Max <= 0)
                return; // 拍板 #8 修订版：加元能对「无元能上限」建筑不生效；元能建筑（骑士团总部）照常受能

            if (delta > 0 && target.EnergyOverflowPassive)
            {
                int room = energy.Max - energy.Value;
                if (delta > room) // 溢出：自身留满，余量转移（room≤0=已满全额转移）
                {
                    overflowAmount = delta - System.Math.Max(0, room);
                    if (room > 0)
                    {
                        energy.Add(room);
                        stats.SetStatStruct(StatType.Energy, energy);
                    }
                    var ally = FindNearestNonFullEnergyAlly(target);
                    if (ally != null && TryGetUnitId(ally, out var allyId))
                    {
                        overflowReceiverId = allyId;
                        var allyStats = ally.GetUnitComponent<UnitStats>();
                        var allyEnergy = allyStats.GetStatStruct(StatType.Energy);
                        allyEnergy.Add(overflowAmount); // 单跳：接收方不再转移
                        allyStats.SetStatStruct(StatType.Energy, allyEnergy);
                    }
                    else overflowAmount = 0; // 我方全满则无效果（安柏1命描述口径）
                    return;
                }
            }

            energy.Add(delta);
            stats.SetStatStruct(StatType.Energy, energy);
        }

        /// <summary>理智应用（2026-09-30 歌声之环批）：RangedInt Add 自动钳 -300~300（到上限自然停涨）；
        /// 玩法消费方随未来理智机制批接线，本批打通 配置→运行时→结算→命令→快照 全链</summary>
        public void ApplySanity(Unit target, int delta)
        {
            if (target == null || delta == 0) return;
            var stats = target.GetUnitComponent<UnitStats>();
            if (stats == null) return;
            var sanity = stats.GetStatStruct(StatType.Sanity);
            sanity.Add(delta);
            stats.SetStatStruct(StatType.Sanity, sanity);
        }

        /// <summary>欧氏距离最近、元能未满的我方存活角色（不含自身；同距 unitId 升序——
        /// 安柏1命溢出转移的接收方判定；尸体不算「角色」；**建筑排除（决策八十六拍板 #8——
        /// 溢出转移候选域=非建筑单位，自然排除〔建筑 baseEnergy=0 恒「满」〕之外显式单源）**）。
        /// 决策九十五欧氏迁移（蓝图 §3.1）：切比雪夫→平方欧氏（全整数零开方，确定性纪律）</summary>
        private Unit FindNearestNonFullEnergyAlly(Unit self)
        {
            var selfPos = GetPosition(self);
            var selfIdentity = self.GetUnitComponent<UnitIdentity>();
            if (selfIdentity == null) return null;
            Unit best = null;
            long bestDist = long.MaxValue;
            string bestId = null;
            foreach (var kv in _units)
            {
                var candidate = kv.Value;
                if (candidate == self || BattleSimState.IsDead(candidate)) continue;
                if (BattleHeuristics.IsBuilding(candidate)) continue; // 拍板 #8：候选域排除建筑
                var identity = candidate.GetUnitComponent<UnitIdentity>();
                if (identity == null || identity.Team != selfIdentity.Team) continue;
                var stats = candidate.GetUnitComponent<UnitStats>();
                if (stats == null) continue;
                var energy = stats.GetStatStruct(StatType.Energy);
                if (energy.Value >= energy.Max) continue; // 已满不接收
                long dist = GetPosition(candidate).SquaredEuclideanCmTo(selfPos); // 欧氏平方单源（决策九十五迁移）
                if (dist < bestDist || (dist == bestDist && bestId != null
                    && string.CompareOrdinal(kv.Key, bestId) < 0))
                {
                    best = candidate;
                    bestDist = dist;
                    bestId = kv.Key;
                }
            }
            return best;
        }

        /// <summary>元能是否够施放（门槛=技能消耗值而非上限——延奏类不满即可放）。
        /// C-2 起消耗声明单源=SkillData.costs（ResourceGate 消费；本判定为元能条目底层原语）</summary>
        public static bool HasEnoughEnergy(Unit unit, int energyCost)
        {
            if (energyCost <= 0) return true;
            var stats = unit?.GetUnitComponent<UnitStats>();
            return stats != null && stats.Energy >= energyCost;
        }

        /// <summary>
        /// 死亡判定并转尸体态（HP≤0 → Dead；属性/碰撞/体积/势力全保留，docs/05 §5.4）
        /// </summary>
        public List<Unit> ResolveDeaths(IEnumerable<Unit> candidates)
        {
            var dead = new List<Unit>();
            foreach (var unit in candidates)
            {
                if (IsDead(unit)) continue;
                var stats = unit.GetUnitComponent<UnitStats>();
                if (stats != null && stats.HP <= 0)
                {
                    unit.GetUnitComponent<UnitStatus>()?.SetStatus(StatusType.Dead, true);
                    dead.Add(unit);
                }
            }
            return dead;
        }

        // ==================== Buff（B2） ====================

        /// <summary>
        /// 施加 Buff：**同具名 buff（Config 资产相同）已存在 → 合并**（叠层+时长累加，docs/06 燃烧
        /// 延长同构）；**异资产同族共存**（决策五十七「资产即身份」——安柏加攻+班尼特加攻可同时
        /// 存在，用户拍板「不同名即可叠加」）；新施加 → 记入全局注册表（回合结束效果按注册序，
        /// docs/22 §2）→ OnApplied 生命周期回调
        /// </summary>
        public void ApplyBuff(Unit target, BaseBuff buff, Unit source = null)
        {
            if (target == null || buff == null) return;
            buff.source = source;
            buff.Sim = this; // 战场门面注入（回合结束效果需战场查询的 Buff 消费——如歌声之环半径枚举）

            // 身份键=Config 资产引用（决策五十七）；Config 双缺防御回落按类型（理论不可达——工厂全注入）
            var existing = buff.Config != null
                ? target.Buffs.Find(b => b.Config == buff.Config)
                : target.Buffs.Find(b => b.Type == buff.Type);
            if (existing != null)
            {
                existing.Merge(buff);
                return;
            }

            buff.ApplicationIndex = _buffSequence++;
            target.AddBuff(buff); // Unit.AddBuff 置 owner
            _activeBuffs.Add(buff);
            buff.OnApplied(); // 如冻结写入 UnitStatus（B4）
        }

        /// <summary>移除 Buff（到期/驱散）：OnRemoved 生命周期回调 + 同步清注册表</summary>
        public void RemoveBuff(Unit target, BaseBuff buff)
        {
            buff.OnRemoved(); // 如冻结解除 UnitStatus（幂等：重复移除无害）
            target?.RemoveBuff(buff);
            _activeBuffs.Remove(buff);
        }

        /// <summary>元素附着（效应应用阶段执行；覆盖=消耗被反应附着，docs/06）——层数默认 1</summary>
        public void AttachElement(Unit target, ElementType element)
        {
            AttachElement(target, element, 1);
        }

        /// <summary>元素附着（带层数——docs/06 §6.2 多层附着，决策七十四铺路；反应消耗后的
        /// 「多余层仍附着」经此落状态）</summary>
        public void AttachElement(Unit target, ElementType element, int layers)
        {
            target?.GetUnitComponent<UnitElement>()?.Dye(element, layers);
        }

        /// <summary>附着侧反应消耗（决策七十四：按层消耗——剩余层仍附着，等层=清附着零残留）</summary>
        public void ConsumeDyeLayers(Unit target, int consumedLayers)
        {
            var unitElement = target?.GetUnitComponent<UnitElement>();
            if (unitElement == null) return;
            if (unitElement.DyedElement == ElementType.Physical) return; // 无附着（防御）
            int left = unitElement.DyedLayers - consumedLayers;
            if (left > 0) unitElement.Dye(unitElement.DyedElement, left);
            else unitElement.ClearDye();
        }

        /// <summary>查冻结 Buff 实例（无=null——冻结层消耗/回合读取共用；冻结判重保证单实例）</summary>
        public FreezeBuff GetFreezeBuff(string unitId)
        {
            var unit = GetUnit(unitId);
            if (unit == null) return null;
            foreach (var buff in unit.Buffs)
                if (buff is FreezeBuff freeze) return freeze;
            return null;
        }

        /// <summary>清除附着（反应消耗的真实状态落点——决策二十五：反应 1:1 双方全消耗、等层
        /// 零残留；反应命中不再发 AttachElementEffect，清除由 ReactionEffect 应用分支执行）</summary>
        public void ClearDye(Unit target)
        {
            target?.GetUnitComponent<UnitElement>()?.ClearDye();
        }

        /// <summary>按注册序结算后的到期收集（RemainingSeconds≤0；永久 Buff（RemainingSeconds&lt;0）不计时
        /// 不收集——歌声之环/寒冰之棱类消失走 RemoveOnHolderDeath 倒下移除或碎裂即时移除
        /// （BattleSimState.RemoveBuff——寒冰之棱 TryShatter 直接注销）</summary>
        public List<BaseBuff> CollectExpiredBuffs()
        {
            var expired = new List<BaseBuff>();
            foreach (var buff in _activeBuffs)
                if (!buff.IsPermanent && buff.RemainingSeconds <= 0) expired.Add(buff);
            return expired;
        }

        // ==================== 即时行动队列 ====================

        public void EnqueueInstantAction(ActionData action)
        {
            _instantActionQueue.Add(action);
        }

        public ActionData DequeueInstantAction()
        {
            if (_instantActionQueue.Count == 0) return null;
            var action = _instantActionQueue[0];
            _instantActionQueue.RemoveAt(0);
            return action;
        }

        // ==================== 快照 ====================

        /// <summary>UnitState 单一构造出口（2026-09-23 审查 Y3：快照与 Summon 命令共用——
        /// 此前 DeployUnitExecutor 手写 9 字段的部分状态，登场回合附着图标/元能/防御等客户端显示偏差，下回合快照才自愈）</summary>
        public UnitState BuildUnitState(string unitId, Unit unit)
        {
            var identity = unit.GetUnitComponent<UnitIdentity>();
            var stats = unit.GetUnitComponent<UnitStats>();
            var element = unit.GetUnitComponent<UnitElement>();
            var status = unit.GetUnitComponent<UnitStatus>();

            var state = new UnitState
            {
                unitId = unitId,
                unitName = identity?.UnitName.ToString() ?? "",
                playerId = identity?.OwnerPlayerID ?? "",
                team = (int)(identity?.Team ?? TeamType.A),
                position = GetPosition(unit),
                hp = stats?.HP ?? 0,
                maxHp = stats?.GetStatStruct(StatType.HP).Max ?? 0,
                attack = stats?.Attack ?? 0,
                defense = stats?.Defense ?? 0,
                attackSpeed = stats?.AttackSpeed ?? 0,
                moveSpeed = stats?.MoveSpeed ?? 0,
                dyedElement = (int)(element?.DyedElement ?? ElementType.Physical),
                dyedLayers = element?.DyedLayers ?? 0, // 多层附着（决策七十四铺路——客户端暂不消费，Host 编译视图初值用）
                isCorpse = status != null && status.IsDead ? 1 : 0,
                isFrozen = status != null && status.IsFrozen ? 1 : 0,
                isSoul = unit.IsSoul ? 1 : 0, // 灵魂标记（决策九十一）：客户端建幽灵立牌+地脉视角专属显示
                dimension = (int)unit.Dimension, // 所在维度（决策九十一）：0=主世界，1=地脉；非主维度单位不入主世界交互域（快照消费方过滤）
                volume = unit.Volume,
                energy = stats?.Energy ?? 0,
                maxEnergy = stats?.GetStatStruct(StatType.Energy).Max ?? 0,
                sanity = stats?.Sanity ?? 0,
                cylinderDiameter = unit.RawData?.受击圆柱直径 ?? 0f,
                constellation = unit.ConstellationLevel, // 命座（B8 批：展示/升命门控真源）
                tier = (int)BattleHeuristics.TierOf(unit), // 操控层级单源（TierOverrideStars 覆盖随此进快照——客户端门控同源）
                exploreVision = stats?.VisionRange ?? 0, // 探索视野真源（迷雾批追拍六：stats 终值=config 基值+1命 StatBoost；0=未填→客户端回落 config 链）
                posX = GetPosCm(unit).x,   // 连续位置（决策九十五 P3，蓝图 §8）：弹道求交/欧氏域真值源
                posZ = GetPosCm(unit).y,
                attackCdRemainTicks = SkillCdRemainTicks(unitId, GIC.Data.SkillType.Normal), // 攻击 CD 剩余（HUD CD 转圈 P4 接线）
                burstCdRemainTicks = SkillCdRemainTicks(unitId, GIC.Data.SkillType.Burst),   // 爆发 CD 剩余
            };
            // 装备态（装备批 2026-10-18）：武器+配件槽位表（含耐久）——客户端血条右侧图标行+瞄准域
            //「有空槽」预判的数据源；无 UnitEquipment 组件（异常）=空表
            var equipment = unit.GetUnitComponent<UnitEquipment>();
            if (equipment != null)
            {
                state.weaponItem = equipment.Weapon.HasValue ? (int)equipment.Weapon.Value : 0;
                state.weaponDurability = equipment.WeaponDurability;
                for (int i = 0; i < equipment.AccessorySlotCount; i++)
                {
                    var accessory = equipment.Accessories[i];
                    state.accessoryItems.Add(accessory.HasValue ? (int)accessory.Value : 0);
                    state.accessoryDurability.Add(equipment.GetAccessoryDurability(i));
                }
            }
            foreach (var buff in unit.Buffs)
                state.buffs.Add(new BuffState
                {
                    type = (int)buff.Type,
                    buffKey = buff.Config != null ? buff.Config.name : "", // 具名身份（决策五十七——客户端徽章匹配/图标解析按 key）
                    level = buff.Level,
                    remainingTurns = buff.RemainingSeconds,
                    // 来源（2026-10-06 头顶 Buff 图标批）：来源单位 id（客户端描环按其玩家色）+
                    // 来源技能 id（客户端图标取 SkillConfig.icon；反应类 Buff=0 回落元素图标）
                    sourceUnitId = buff.source != null
                        ? buff.source.GetUnitComponent<UnitIdentity>()?.UnitID ?? "" : "",
                    sourceSkillId = buff.SourceSkillId,
                });
            return state;
        }

        public BattleSnapshot TakeSnapshot(int turnNumber)
        {
            var snapshot = new BattleSnapshot { turnNumber = turnNumber };

            foreach (var kv in _units)
                snapshot.units.Add(BuildUnitState(kv.Key, kv.Value));

            // 跨维度单位（决策九十一）：灵魂随快照携带（客户端建幽灵立牌+地脉视角显示；注册序
            // 确定性=回放同构）。**快照消费方注意**：snapshot.units 自此含非主维度单位——主世界
            // 交互域（伤害/治疗/附着/AI 候选）一律加 isSoul==0 过滤（维度隔离的主动侧，
            // EffectCompiler/ProjectileResolver/SkillHitResolver/CompanionBrain 等已过滤）
            foreach (var kv in _crossDimensionUnits)
                snapshot.units.Add(BuildUnitState(kv.Key, kv.Value));

            foreach (var kv in _resources)
            {
                var res = kv.Value;
                var snapshotRes = new PlayerResourceState
                {
                    playerId = res.playerId,
                    mora = res.mora,
                    stamina = res.stamina,
                    handCardCount = res.handCardCount,
                    deckCardCount = res.deckCardCount,
                };
                if (_hands.TryGetValue(kv.Key, out var hand))
                {
                    // 货币条目 count=资源池镜像（真源=池）；普通条目 count=局内真源
                    foreach (var entry in hand)
                    {
                        var copy = new HandCard(entry.AsCardId(), entry.count);
                        copy.durability = entry.durability; // 装备实例耐久（决策八十一——构造器不拷字段，
                                                             // 上批漏拷=客户端快照条目恒 0 → equipDurability
                                                             // 上交 0 → Host 实例匹配拒绝=「打出装备没反应」根因）
                        if (copy.IsCurrency)
                            copy.count = copy.AsItemName() == ItemName.Mora ? res.mora : res.stamina;
                        snapshotRes.handCards.Add(copy);
                    }
                }
                snapshot.resources.Add(snapshotRes);
            }

            // 资源点权威态（docs/47）：回合头整表覆盖自愈（客户端表现层对齐+AI 采集候选消费域）；
            // 注册序=BattleMapConfig 解析序（构造时复制保序）——回放确定性同构
            foreach (var node in _resourceNodes.Values)
                snapshot.resourceNodes.Add(node.Clone());

            return snapshot;
        }

        // ==================== 片内附着编译视图（2026-10-01 双蒸发修复） ====================
        // 病灶：反应预览只读片前快照附着，而消耗/覆盖在效应统一应用才落状态——同片多次命中
        // （安柏一箭双丘丘）后续命中仍见旧附着，一层水双蒸发（docs/06 §6.3 1比1消耗被打破）。
        // 视图=编译期工作副本：初值回落快照，反应消耗/新附着随编译序即时推进；TurnResolver
        // 片/即时段编译期以 Begin/End 包裹（命中编译全部走这两处）。真实状态仍由
        // AttachElementEffect 统一应用写入——视图只服务反应预判，不落任何持久状态。
        // 2026-10-15 决策七十四扩两维：①附着层数（多层 1:1 消耗/剩余层仍附着——值扩 (元素,层数)）；
        // ②冻结回合视图（冻结 Buff 第二冰源——同片多命中逐发消耗冻结回合，ArrowRain 4 发 vs
        // 2 回合冻结=发1融2→1/发2融1→0，与双蒸发修复同机制防一发吃穿全部）。

        /// <summary>片内附着编译视图（unitId → (附着元素, 层数)；null=未开启，读取回落快照=旧行为）</summary>
        private Dictionary<string, (ElementType element, int layers)> _compileDyeView;

        /// <summary>片内冻结回合消耗视图（unitId → 已消耗回合累计；真实剩余=活态冻结回合−累计。
        /// 只记消耗不记全量：编译期冻结回合无其它写入点〔回合递减在回合结束段〕，活态即初值）</summary>
        private Dictionary<string, int> _compileFreezeView;

        /// <summary>开启编译视图（片/即时段编译期入口调用，紧跟 TakeSnapshot 之后）</summary>
        public void BeginCompileDyeView()
        {
            _compileDyeView = new Dictionary<string, (ElementType, int)>();
            _compileFreezeView = new Dictionary<string, int>();
        }

        /// <summary>关闭编译视图（片内全部命中编译完成后调用）</summary>
        public void EndCompileDyeView()
        {
            _compileDyeView = null;
            _compileFreezeView = null;
        }

        /// <summary>编译期附着读取：视图优先，未命中回退片前快照态；无快照（回合结束段 tick——
        /// 2026-10-01 tick 统一拍板）回退**活态**（染色写入仍走效应统一应用，活态在该编译时点=段初真值）</summary>
        public ElementType GetCompileDye(string unitId, UnitState snapshotState)
        {
            if (_compileDyeView != null && _compileDyeView.TryGetValue(unitId, out var dye))
                return dye.element;
            if (snapshotState != null) return (ElementType)snapshotState.dyedElement;
            var unit = GetUnit(unitId);
            return unit != null
                ? (unit.GetUnitComponent<UnitElement>()?.DyedElement ?? ElementType.Physical)
                : ElementType.Physical;
        }

        /// <summary>编译期附着层数读取（决策七十四：与 GetCompileDye 同回退链——视图→快照→活态；
        /// 快照旧端/旧回放缺省 0 且元素非 Physical 时归一为 1〔docs/06 §6.2「大部分附着为1层」〕）</summary>
        public int GetCompileDyeLayers(string unitId, UnitState snapshotState)
        {
            if (_compileDyeView != null && _compileDyeView.TryGetValue(unitId, out var dye))
                return dye.layers;
            if (snapshotState != null)
                return NormalizeLayers((ElementType)snapshotState.dyedElement, snapshotState.dyedLayers);
            var unit = GetUnit(unitId);
            if (unit == null) return 0;
            var unitElement = unit.GetUnitComponent<UnitElement>();
            return unitElement == null ? 0
                : NormalizeLayers(unitElement.DyedElement, unitElement.DyedLayers);
        }

        private static int NormalizeLayers(ElementType element, int layers)
            => element == ElementType.Physical ? 0 : (layers <= 0 ? 1 : layers);

        /// <summary>编译期附着推进：反应消耗=Physical（等层零残留，docs/06 §6.3）；
        /// AttachElement 覆盖=来袭元素（带剩余层——多层消耗后「多余的元素仍然附着」）</summary>
        public void SetCompileDye(string unitId, ElementType element)
        {
            SetCompileDye(unitId, element, element == ElementType.Physical ? 0 : 1);
        }

        /// <summary>编译期附着推进（带层数）</summary>
        public void SetCompileDye(string unitId, ElementType element, int layers)
        {
            if (_compileDyeView != null)
                _compileDyeView[unitId] = (element, element == ElementType.Physical ? 0 : UnityEngine.Mathf.Max(1, layers));
        }

        // ==================== 冻结回合编译视图（决策七十四「冻结层=第二冰源」） ====================

        /// <summary>编译期冻结剩余回合读取：活态冻结 Buff 回合 − 本片已消耗累计（视图未开启=活态直读，
        /// 回合结束段 tick 亦然）；无冻结 Buff=0</summary>
        public int GetCompileFreezeTurns(string unitId)
        {
            var freeze = GetFreezeBuff(unitId);
            int turns = freeze != null ? freeze.RemainingSeconds : 0;
            if (_compileFreezeView != null && _compileFreezeView.TryGetValue(unitId, out var consumed))
                turns -= consumed;
            return UnityEngine.Mathf.Max(0, turns);
        }

        /// <summary>编译期冻结回合消耗推进（同片后续命中见已扣减的冻结回合）</summary>
        public void ConsumeCompileFreeze(string unitId, int turns)
        {
            if (_compileFreezeView == null || turns <= 0) return;
            _compileFreezeView.TryGetValue(unitId, out var consumed);
            _compileFreezeView[unitId] = consumed + turns;
        }
    }
}
