using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 开局玩家配置（房间名册 → 战斗场景的传递载体；2026-10-11 联机批带队伍/颜色/出生点——
    /// 房间选择真实进局内）。
    /// </summary>
    [Serializable]
    public class BattlePlayerSetup
    {
        public string PlayerId;
        public string DisplayName;
        public bool IsAI;

        /// <summary>AI 玩家难度档（2026-10-12 选择器批：房间 AI 行点选→PlayerInfo→BuildFromRoster 透传）</summary>
        public AIDifficulty AIDifficulty = AIDifficulty.Grandmaster;

        /// <summary>AI 玩家人格原型名（Resources/Configs/AI/Persona_{名}——BattleScreen 按名换资产）</summary>
        public string AIPersona = "经济家";

        /// <summary>队伍（房间小队=局内阵营：同队友军/异队敌对，引擎原生语义）</summary>
        public TeamType Team = TeamType.A;

        /// <summary>玩家颜色（房间选色=局内玩家色：HUD 队营色/预览标记等）</summary>
        public PlayerColor Color = PlayerColor.Red;

        /// <summary>出生位置（Random=开局按序自动分配未占用出生区；否则=地图出生区序号）</summary>
        public SpawnPositionType SpawnPosition = SpawnPositionType.Random;
    }

    /// <summary>
    /// 开局配置（联机界面 → 战斗场景的传递载体）。
    /// 静态 Current 模式（consume-once）：BattleScreen 读取后即清空，避免污染下次入口。
    /// B7 LAN 时：Host 广播本配置 → 各端加载同一战斗场景（docs/18 决策一：Host 权威）。
    /// </summary>
    public class BattleLaunchConfig
    {
        public const string DefaultMapName = "BattleMap_FirstMap";

        public string MapConfigName = DefaultMapName;
        public List<BattlePlayerSetup> Players = new List<BattlePlayerSetup>();

        /// <summary>LAN 对局标记（B7，2026-10-11 联机批）：true=经 StartBattleEvent 广播而来——
        /// 各端据此选装配形态（host=CreateHosted / 客户端=CreateClient），且开局转场不拆网络连接；
        /// 本地直开（调试/沙盒/纯 AI 对战）false=CreateLocal 旧链路</summary>
        public bool IsNetworked;

        /// <summary>试招沙盒（2026-10-05 时轮编辑器「开一把试招」）：A 方=正在编辑时轮的持有单位
        /// （层级覆盖 5★魔神档=玩家全手操全部技能）；B 方=木桩空座（IsAI=false：配额脑不挂=木桩纯站桩，
        /// 选择阶段经 Sim.SandboxAutoPassPlayerId 即时自动 Pass 不拖节奏）；阵容走沙盒 spawn
        /// （BattleScreen：A 单位出生区偏东 1 格、木桩同行再东 5 格=十字射程内同屏）</summary>
        public bool IsSandbox;
        public UnitName SandboxAllyUnit;
        public UnitName SandboxDummyUnit;

        /// <summary>战斗种子（2026-10-02 幸运暴击批）：模拟核心概率事件唯一随机源的初始化种子
        /// （Host 端 BattleSimState 按 seed 建 DeterministicRng〔F0-4：xorshift32，状态可序列化〕，同 seed+同命令序列=同战局——回放/复现/
        /// 联机防漂移；客户端零 roll 不消费）。BuildFromRoster 自动随机生成；测试/复现可显式注入；
        /// 未经本工厂直接 new 的配置 Seed=0=确定性种子（合法，仅同输入对局暴击序列一致）。
        /// B7 LAN：Host 广播本配置即双端同 seed</summary>
        public int Seed;

        /// <summary>迷雾模式（2026-10-17 迷雾批，docs/active/44）：对局级表现层配置——
        /// Skirmish=对战迷雾（房间对战默认）、Mission=任务迷雾（混沌盖）、None=沙盒/观察局。
        /// 默认 None：直接 new（观察 harness/调试）不开雾；BuildFromRoster 置 Skirmish；
        /// 任务系统（后续批）置 Mission；LAN 经 StartBattleEvent.FogMode 双端同模式</summary>
        public BattleFogMode FogMode = BattleFogMode.None;

        public static BattleLaunchConfig Current { get; private set; }

        /// <summary>
        /// 读取并清空（consume-once）；无配置返回 null（= 双开调试入口）
        /// </summary>
        public static BattleLaunchConfig Take()
        {
            var config = Current;
            Current = null;
            return config;
        }

        /// <summary>
        /// 只写入配置不触发场景加载（测试注入 / B7 网络端各收到配置后自行加载时使用）
        /// </summary>
        public static void Prepare(BattleLaunchConfig config)
        {
            Current = config;
        }

        /// <summary>
        /// 以指定配置开战（加载战斗场景）
        /// </summary>
        public static void Launch(BattleLaunchConfig config)
        {
            Prepare(config);
            SceneType.BattleScreen.Load();
        }

        /// <summary>
        /// 从房间名册构建开局配置（2026-10-11 联机批唯一正式工厂；BuildSinglePlayer/AI 自动补位已退役——
        /// 单人开局=房间内手动「增加AI玩家」）。只构造不触发加载——CoopScreen 开局转场用 Prepare 写入。
        /// </summary>
        public static BattleLaunchConfig BuildFromRoster(string mapConfigName, IEnumerable<PlayerInfo> players)
        {
            var config = new BattleLaunchConfig
            {
                MapConfigName = string.IsNullOrEmpty(mapConfigName) ? DefaultMapName : mapConfigName,
                // 种子用表现层 UnityEngine.Random 生成（开局壳层，不入模拟流；模拟核心禁该随机源）
                Seed = UnityEngine.Random.Range(0, int.MaxValue), // DETERMINISM-ALLOW: 开局种子（Host 生成，随 BattleStart 广播；不入模拟流）
                // 迷雾批（2026-10-17）：房间对战默认对战迷雾（用户拍板「开房间对战时，不用记忆地图」）
                FogMode = BattleFogMode.Skirmish,
                Players = new List<BattlePlayerSetup>(),
            };
            foreach (var p in players)
            {
                config.Players.Add(new BattlePlayerSetup
                {
                    PlayerId = p.PlayerID,
                    DisplayName = p.PlayerName,
                    IsAI = p.IsAI,
                    AIDifficulty = p.AIDifficulty,
                    AIPersona = p.AIPersona,
                    Team = p.Team,
                    Color = p.Color,
                    SpawnPosition = p.SpawnPosition,
                });
            }
            return config;
        }

        /// <summary>试招沙盒开局配置（时轮编辑器「开一把试招」入口，2026-10-05）：P1 真人手操试探单位、
        /// P2 空座木桩（IsAI=false=配额脑不挂，站桩挨打；回合推进由沙盒即时自动 Pass 承接）</summary>
        public static BattleLaunchConfig BuildSandbox(UnitName ally, UnitName dummy)
        {
            return new BattleLaunchConfig
            {
                MapConfigName = DefaultMapName,
                Seed = UnityEngine.Random.Range(0, int.MaxValue), // DETERMINISM-ALLOW: 开局种子（Host 生成，随 BattleStart 广播；不入模拟流）
                IsSandbox = true,
                SandboxAllyUnit = ally,
                SandboxDummyUnit = dummy,
                Players = new List<BattlePlayerSetup>
                {
                    new BattlePlayerSetup
                    {
                        PlayerId = BattleDebugPlayerIds.P1,
                        DisplayName = "试招",
                        IsAI = false,
                    },
                    new BattlePlayerSetup
                    {
                        PlayerId = BattleDebugPlayerIds.P2,
                        DisplayName = "木桩",
                        IsAI = false, // 空座：不挂配额脑=木桩不还手（勿改 true——AI 会操温迪反击）
                    },
                },
            };
        }
    }
}
