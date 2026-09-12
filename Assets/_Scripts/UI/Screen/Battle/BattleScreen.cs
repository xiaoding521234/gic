using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 战斗场景控制器（独立根场景，Single 加载）：
    /// 消费 BattleLaunchConfig（联机界面开局）或进入双开调试模式（config 为 null 的调试直开）；
    /// 装配本地 BattleSession（Host 逻辑 + Client 播放器同进程），AI 玩家自动上交行动。
    /// ESC/右键/结束按钮 → 退出确认弹窗（2026-09-12 用户拍板：不应直接退出）。
    /// 五阶段完整回合流程与正式 HUD 在 B5/B6 落地。
    /// </summary>
    public class BattleScreen : ScreenBase
    {
        [Header("战斗组件（场景装配）")]
        [SerializeField] private BattleBoard _board;
        [SerializeField] private BattlePlayer _player;
        [SerializeField] private TurnFlowController _flow;
        [SerializeField] private BattleDebugPanel _debugPanel;

        private BattleSession _session;
        private BattleExitConfirmDialog _exitDialog;

        /// <summary>当前对局（调试/测试访问口）</summary>
        public BattleSession Session => _session;

        protected override void Awake()
        {
            base.Awake();
        }

        private void Start()
        {
            RegisterClosableSelf();

            // 消费开局配置（consume-once）：null = 调试直开（双端手动）
            var launchConfig = BattleLaunchConfig.Take();
            var playerSetups = launchConfig?.Players;
            if (playerSetups == null || playerSetups.Count == 0)
            {
                playerSetups = new List<BattlePlayerSetup>
                {
                    new BattlePlayerSetup { PlayerId = BattleDebugPlayerIds.P1, DisplayName = "玩家P1", IsAI = false },
                    new BattlePlayerSetup { PlayerId = BattleDebugPlayerIds.P2, DisplayName = "玩家P2", IsAI = false },
                };
            }
            if (playerSetups.Count > 2)
            {
                GICLog.Warn("[BattleScreen] B1 只支持 2 方对局，多余玩家忽略");
                playerSetups = playerSetups.Take(2).ToList();
            }

            // 地图：联机入口按配置名加载；调试直开用默认地图
            var mapConfig = Resources.Load<BattleMapConfig>("Configs/" +
                (launchConfig != null ? launchConfig.MapConfigName : BattleLaunchConfig.DefaultMapName));
            if (mapConfig == null)
            {
                GICLog.Error("[BattleScreen] 未找到战斗地图配置，无法开战");
                DoClose();
                return;
            }
            var map = mapConfig.BuildData();
            if (map == null)
            {
                GICLog.Error("[BattleScreen] 战场地图配置非法，无法开战");
                DoClose();
                return;
            }

            // 组件兜底（场景装配缺失时补挂，便于直接打开场景调试）
            if (_player == null) _player = gameObject.AddComponent<BattlePlayer>();
            if (_flow == null) _flow = gameObject.AddComponent<TurnFlowController>();
            if (_board == null)
            {
                _board = GetComponentInChildren<BattleBoard>();
                if (_board == null)
                {
                    GICLog.Error("[BattleScreen] 场景缺少 BattleBoard，无法建盘");
                    DoClose();
                    return;
                }
            }
            if (_debugPanel == null)
                _debugPanel = GetComponentInChildren<BattleDebugPanel>();
            if (_debugPanel == null)
            {
                // 程序化 UI 无 prefab 可改，运行时自建为唯一路径（调试工具不入包）
                var panelGo = new GameObject("BattleDebugPanel");
                panelGo.transform.SetParent(transform, false);
                _debugPanel = panelGo.AddComponent<BattleDebugPanel>();
            }

            // RTS 相机控制器（滚轮缩放/WASD/中键拖拽，2026-09-12 用户拍板；幂等补挂）
            var cameraGo = GameObject.Find("BattleCamera");
            if (cameraGo != null && cameraGo.GetComponent<BattleCameraController>() == null)
                cameraGo.AddComponent<BattleCameraController>();

            // 逻辑单位隐藏根（Host 侧逻辑对象；表现层为 UnitView）
            var logicGo = new GameObject("LogicUnits");
            logicGo.transform.SetParent(transform, false);

            _session = BattleSession.CreateLocal(map, _player, _flow, logicGo.transform);
            foreach (var setup in playerSetups)
                _session.RegisterDebugPlayer(setup.PlayerId);

            SpawnDebugUnits(mapConfig, playerSetups);

            // AI 玩家大脑（B1 固定脚本占位：攻击最近敌人；B6 换启发式）
            foreach (var setup in playerSetups)
            {
                if (!setup.IsAI) continue;
                var brainGo = new GameObject($"AIBrain_{setup.PlayerId}");
                brainGo.transform.SetParent(transform, false);
                brainGo.AddComponent<AIDebugBrain>().Bind(_session, setup.PlayerId);
                GICLog.Info($"[BattleScreen] AI 玩家就位：{setup.PlayerId}（{setup.DisplayName}）");
            }

            // 调试面板只给真人玩家建操作块
            var manualIds = playerSetups.Where(p => !p.IsAI).Select(p => p.PlayerId).ToList();
            _debugPanel.Bind(_session, Close, manualIds);

            _session.StartBattle();
        }

        /// <summary>
        /// B1 固定测试军：先手方 安柏+凯亚 / 后手方 丽莎+芭芭拉（全蒙德，决策五）。
        /// 出生点来自地图配置的玩家出生区；正式出战队列 B6 落地。
        /// </summary>
        private void SpawnDebugUnits(BattleMapConfig mapConfig, List<BattlePlayerSetup> playerSetups)
        {
            var first = playerSetups[0];
            var second = playerSetups[1];

            var firstCenter = FindSpawnCenter(mapConfig, first.PlayerId, new BattleCell(3, 3));
            var secondCenter = FindSpawnCenter(mapConfig, second.PlayerId, new BattleCell(16, 16));

            _session.SpawnDebugUnit(UnitName.Amber, first.PlayerId, TeamType.A, firstCenter);
            _session.SpawnDebugUnit(UnitName.Kaeya, first.PlayerId, TeamType.A, firstCenter + new BattleCell(1, 0));
            _session.SpawnDebugUnit(UnitName.Lisa, second.PlayerId, TeamType.B, secondCenter);
            _session.SpawnDebugUnit(UnitName.Barbara, second.PlayerId, TeamType.B, secondCenter + new BattleCell(-1, 0));
        }

        private static BattleCell FindSpawnCenter(BattleMapConfig config, string playerId, BattleCell fallback)
        {
            if (config.spawnZones != null)
            {
                foreach (var zone in config.spawnZones)
                    if (zone.playerId == playerId) return zone.center;
            }
            return fallback;
        }

        /// <summary>
        /// 关闭请求（ESC/右键/结束战斗按钮）→ 退出确认弹窗，确认后才真正退出
        /// （2026-09-12 用户拍板：战斗中右键不应直接退出）。
        /// 弹窗挂 IClosable 位于栈顶：弹窗期间再按 ESC/右键 = 取消弹窗。
        /// </summary>
        public override void Close()
        {
            if (isClosing) return;
            if (_exitDialog != null && _exitDialog.IsOpen) return; // 已在确认中（防御）
            _exitDialog = BattleExitConfirmDialog.Show(transform, "确定退出战斗？当前对局将结束并回到大厅。", DoClose);
        }

        /// <summary>
        /// 真正退出（确认回调/配置异常直退共用）：独立根场景无 GoBack 历史，直接回大厅。
        /// </summary>
        private void DoClose()
        {
            if (isClosing) return;
            isClosing = true;
            InputLocks.Push(this, InputLockReason.Closing);
            _session?.StopBattle();
            StartCoroutine(CloseToMainHall());
        }

        private IEnumerator CloseToMainHall()
        {
            PopMusicSafe();
            yield return null;
            InputLocks.Pop(this, InputLockReason.Closing);
            // 战斗为根场景，无 GoBack 历史 → 直接回大厅
            SceneType.MainHall.Load();
        }
    }
}
