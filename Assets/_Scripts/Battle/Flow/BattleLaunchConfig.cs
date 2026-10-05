using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 开局玩家配置（B1：真人/AI 补位；B7 LAN 时经网络下发同一结构）
    /// </summary>
    [Serializable]
    public class BattlePlayerSetup
    {
        public string PlayerId;
        public string DisplayName;
        public bool IsAI;
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

        /// <summary>试招沙盒（2026-10-05 时轮编辑器「开一把试招」）：A 方=正在编辑时轮的持有单位
        /// （层级覆盖 5★魔神档=玩家全手操全部技能）；B 方=木桩空座（IsAI=false：配额脑不挂=木桩纯站桩，
        /// 选择阶段经 Sim.SandboxAutoPassPlayerId 即时自动 Pass 不拖节奏）；阵容走沙盒 spawn
        /// （BattleScreen：A 单位出生区偏东 1 格、木桩同行再东 5 格=十字射程内同屏）</summary>
        public bool IsSandbox;
        public UnitName SandboxAllyUnit;
        public UnitName SandboxDummyUnit;

        /// <summary>战斗种子（2026-10-02 幸运暴击批）：模拟核心概率事件唯一随机源的初始化种子
        /// （Host 端 BattleSimState 按 seed 建 System.Random，同 seed+同命令序列=同战局——回放/复现/
        /// 联机防漂移；客户端零 roll 不消费）。BuildSinglePlayer 自动随机生成；测试/复现可显式注入；
        /// 未经本工厂直接 new 的配置 Seed=0=确定性种子（合法，仅同输入对局暴击序列一致）。
        /// B7 LAN：Host 广播本配置即双端同 seed</summary>
        public int Seed;

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
        /// 单人开局配置（真人 + AI 补位对手；当前即可开一把；B7 换真人入座）。
        /// 只构造不触发加载——预载路径（CoopScreen 预载+激活）用 Prepare 写入后再激活场景。
        /// </summary>
        public static BattleLaunchConfig BuildSinglePlayer(string mapConfigName)
        {
            return new BattleLaunchConfig
            {
                MapConfigName = string.IsNullOrEmpty(mapConfigName) ? DefaultMapName : mapConfigName,
                // 种子用表现层 UnityEngine.Random 生成（开局壳层，不入模拟流；模拟核心禁该随机源）
                Seed = UnityEngine.Random.Range(0, int.MaxValue),
                Players = new List<BattlePlayerSetup>
                {
                    new BattlePlayerSetup
                    {
                        PlayerId = BattleDebugPlayerIds.P1,
                        DisplayName = "玩家",
                        IsAI = false,
                    },
                    new BattlePlayerSetup
                    {
                        PlayerId = BattleDebugPlayerIds.P2,
                        DisplayName = "AI 玩家",
                        IsAI = true,
                    },
                },
            };
        }

        /// <summary>以指定配置开战（同步启动加载协程）</summary>
        public static void LaunchSinglePlayer(string mapConfigName)
        {
            Launch(BuildSinglePlayer(mapConfigName));
        }

        /// <summary>试招沙盒开局配置（时轮编辑器「开一把试招」入口，2026-10-05）：P1 真人手操试探单位、
        /// P2 空座木桩（IsAI=false=配额脑不挂，站桩挨打；回合推进由沙盒即时自动 Pass 承接）</summary>
        public static BattleLaunchConfig BuildSandbox(UnitName ally, UnitName dummy)
        {
            return new BattleLaunchConfig
            {
                MapConfigName = DefaultMapName,
                Seed = UnityEngine.Random.Range(0, int.MaxValue),
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
