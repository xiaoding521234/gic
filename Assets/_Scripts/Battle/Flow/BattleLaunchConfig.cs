using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
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
        /// 单人开局：真人 + AI 补位对手（当前即可开一把；B7 换真人入座）
        /// </summary>
        public static void LaunchSinglePlayer(string mapConfigName)
        {
            Launch(new BattleLaunchConfig
            {
                MapConfigName = string.IsNullOrEmpty(mapConfigName) ? DefaultMapName : mapConfigName,
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
            });
        }
    }
}
