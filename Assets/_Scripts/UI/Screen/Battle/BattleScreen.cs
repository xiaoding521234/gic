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
        [SerializeField] private BattleHud _hud;

        private BattleSession _session;
        private BattleExitConfirmDialog _exitDialog;

        // 战斗地图音乐：按绑定位置取 PositionConfig 昼夜曲池（2026-09-14）
        [Autowired] private PositionManager _positionManager;

        /// <summary>当前对局（调试/测试访问口）</summary>
        public BattleSession Session => _session;

        protected override void Awake()
        {
            base.Awake();
        }
        private void Start()
        {
            RegisterClosableSelf();
            StartCoroutine(AssembleRoutine());
        }

        /// <summary>
        /// 装配协程（2026-09-13 分帧化，用户拍板"加载不阻塞"）：
        /// 建盘/立牌/面板等同步装配原为一帧 1.1s+ 尖峰（实测），加载页动画整帧冻结——
        /// 重活之间插帧，把单帧尖峰摊薄到多帧；Reveal 收尾不变（装配完毕才揭幕）。
        /// 中途异常协程中断 → SceneFadeOverlay 15s 安全兜底自动揭幕，不会卡死加载页。
        /// </summary>
        private IEnumerator AssembleRoutine()
        {
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
                yield break;
            }
            var map = mapConfig.BuildData();
            if (map == null)
            {
                GICLog.Error("[BattleScreen] 战场地图配置非法，无法开战");
                DoClose();
                yield break;
            }

            yield return null; // ── 分帧：地图数据就绪/会话创建是重活起点 ──

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
                    yield break;
                }
            }

            // RTS 相机控制器（滚轮缩放/WASD/中键拖拽，2026-09-12 用户拍板；幂等补挂）
            var cameraGo = GameObject.Find("BattleCamera");
            if (cameraGo != null && cameraGo.GetComponent<BattleCameraController>() == null)
                cameraGo.AddComponent<BattleCameraController>();

            // EventSystem 兜底（2026-09-14 退出弹窗按钮全死实锤）：EventSystem 活在大厅场景，
            // 随旧根卸载/Single 加载清场消失——战斗场景缺它时全部 UI 按钮点击无法投递
            // （棋盘/相机走 3D 物理射线不受影响，弹窗与 HUD 按钮全瘫）。幂等补挂；
            // 不 DontDestroyOnLoad——退出回大厅随场景卸载消亡，由大厅场景自己的 ES 接管
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            yield return null; // ── 分帧：兜底/建会话分开（单帧 304ms 实测拆半，2026-09-13） ──

            // 逻辑单位隐藏根（Host 侧逻辑对象；表现层为 UnitView）
            var logicGo = new GameObject("LogicUnits");
            logicGo.transform.SetParent(transform, false);

            _session = BattleSession.CreateLocal(map, _player, _flow, logicGo.transform);
            foreach (var setup in playerSetups)
                _session.RegisterDebugPlayer(setup.PlayerId);

            yield return null; // ── 分帧：会话就绪/立牌是重活起点 ──

            // B1 固定测试军逐个立牌（prefab 实例化+依赖资产首载的尖峰摊薄到每单位一帧）
            yield return StartCoroutine(SpawnDebugUnitsRoutine(mapConfig, playerSetups));

            // AI 玩家大脑（B1 固定脚本占位：攻击最近敌人；B6 换启发式）
            foreach (var setup in playerSetups)
            {
                if (!setup.IsAI) continue;

                var brainGo = new GameObject($"AIBrain_{setup.PlayerId}");
                brainGo.transform.SetParent(transform, false);
                brainGo.AddComponent<AIDebugBrain>().Bind(_session, setup.PlayerId);
                GICLog.Info($"[BattleScreen] AI 玩家就位：{setup.PlayerId}（{setup.DisplayName}）");
            }

            yield return null; // ── 分帧：HUD 绑定/开局前让渲染喘一口气 ──

            // 正式战斗 HUD（B6 提前启动，docs/18 决策六；灰盒调试面板 2026-09-18 用户拍板撤除）
            if (_hud == null)
            {
                var hudGo = new GameObject("BattleHud");
                hudGo.transform.SetParent(transform, false);
                _hud = hudGo.AddComponent<BattleHud>();
            }
            var cameraCtrl = GameObject.Find("BattleCamera")?.GetComponent<BattleCameraController>();
            _hud.Bind(_session, _board, cameraCtrl, Close);

            _session.StartBattle();

            // 正式进入战斗地图：启动音乐轮换链（2026-09-14 用户拍板，揭幕同时起曲）
            StartBattleMusic(mapConfig);

            // 联机入口开局经加载页转场而来（SceneFadeOverlay.Cover）：装配尖峰已分帧摊薄，
            // 装配完毕揭幕；调试直开无黑场时为纯 no-op
            SceneFadeOverlay.Reveal(0.2f);
        }

        // ==================== 战斗地图音乐（轮换链，参考大厅位置曲模式） ====================

        // 轮换间隔取大厅位置曲同款节奏（PositionManager.MUSIC_INTERVAL=10s；战斗侧独立常量，
        // 不与大厅耦合，2026-09-14 用户拍板"播完轮换参考当前大厅背景音乐"）
        private const float BattleMusicIntervalSeconds = 10f;

        // 链路生死标志：轮换链的 onComplete 闭包与协程都跑在 AudioManager 上，退出战斗后须防续播
        private bool _battleMusicActive;
        private Action<BattlePhase, int> _onPhaseChangedForMusic;

        /// <summary>
        /// 启动战斗地图音乐轮换链：按 BattleMapConfig.position → PositionConfig 昼夜曲池取曲，
        /// 选池用**战斗独立时钟时段**（TurnFlowController.BattleTimePeriod，全局 TimeUtility 仅
        /// 未就绪兜底）。链式 PlayMusicWithInterval：播完 → 间隔 10s → 按当前战斗时段重选池 →
        /// 随机下一首（时段随回合推进自然切换，不打断在播曲目）。另订阅回合状态机阶段变化做
        /// 自愈重试：链曾因池空静默死亡时，新回合开始且真静默则重新起链。曲池为空仅警告不阻断。
        /// </summary>
        private void StartBattleMusic(BattleMapConfig mapConfig)
        {
            _battleMusicActive = true;
            if (_flow != null)
            {
                _onPhaseChangedForMusic = (phase, _) =>
                {
                    if (!_battleMusicActive || phase != BattlePhase.Selecting) return;
                    var audio = AudioManager.Instance;
                    if (audio == null) return;
                    // 真静默才重试：在播/间隔冷却中（clip 仍挂 source）链是活的，不插手
                    if (audio.IsMusicPlaying() || audio.GetCurrentMusicClip() != null) return;
                    PlayNextBattleTrack(mapConfig);
                };
                _flow.OnPhaseChanged += _onPhaseChangedForMusic;
            }
            PlayNextBattleTrack(mapConfig);
        }

        private void PlayNextBattleTrack(BattleMapConfig mapConfig)
        {
            var positionData = _positionManager?.GetPositionData(mapConfig.position);
            if (positionData == null)
            {
                GICLog.Warn($"[BattleScreen] 战斗地图 {mapConfig.mapName} 绑定位置 {mapConfig.position} 未配置 PositionData，战斗无音乐");
                return;
            }

            var period = _flow != null ? _flow.BattleTimePeriod : TimeUtility.GetCurrentTimePeriod();
            var clip = (period == TimePeriod.Daytime ? positionData.dayAudios : positionData.nightAudios)
                ?.GetRandomClip();
            if (clip == null)
            {
                GICLog.Warn($"[BattleScreen] 战斗地图 {mapConfig.mapName} 绑定位置 {mapConfig.position} 无 {period} 时段音乐，等待下回合自愈重试");
                return;
            }

            AudioManager.Instance.PlayMusicWithInterval(
                clip, MusicType.Battle,
                intervalAfter: BattleMusicIntervalSeconds,
                loop: false,
                onComplete: () =>
                {
                    if (!_battleMusicActive) return; // 退出已停链：不再续播
                    PlayNextBattleTrack(mapConfig);
                });
        }

        /// <summary>
        /// 停止轮换链（退出战斗全路径 + OnDestroy 兜底）。
        /// 硬切不淡出：战斗退出无加载页遮盖；且 FadeOutMusicCoroutine 不可重入——若淡出中途
        /// 新曲起播（回大厅复活链 fadeIn=0），残留淡出协程会在结束时 Stop+清 clip 误杀新曲。
        /// 回大厅后位置曲由 PositionManager 的 MainHall 激活钩子复活。
        /// </summary>
        private void StopBattleMusic()
        {
            _battleMusicActive = false;
            if (_flow != null && _onPhaseChangedForMusic != null)
            {
                _flow.OnPhaseChanged -= _onPhaseChangedForMusic;
                _onPhaseChangedForMusic = null;
            }
            if (AudioManager.Instance == null) return; // 拆除期 AudioManager 可能已亡（docs/14 §29）
            AudioManager.Instance.StopMusic();
        }

        protected override void OnDestroy()
        {
            StopBattleMusic();
            base.OnDestroy();
        }

        /// <summary>
        /// B1 固定测试军：先手方 安柏+凯亚 / 后手方 丽莎+芭芭拉（全蒙德，决策五）。
        /// 出生点来自地图配置的玩家出生区；正式出战队列 B6 落地。
        /// 分帧协程：逐单位 yield（单帧 1.1s 立牌尖峰摊薄，2026-09-13）。
        /// </summary>
        private IEnumerator SpawnDebugUnitsRoutine(BattleMapConfig mapConfig, List<BattlePlayerSetup> playerSetups)
        {
            var first = playerSetups[0];
            var second = playerSetups[1];

            var firstCenter = FindSpawnCenter(mapConfig, first.PlayerId, new BattleCell(3, 3));
            var secondCenter = FindSpawnCenter(mapConfig, second.PlayerId, new BattleCell(16, 16));

            _session.SpawnDebugUnit(UnitName.Amber, first.PlayerId, TeamType.A, firstCenter);
            yield return null;
            _session.SpawnDebugUnit(UnitName.Kaeya, first.PlayerId, TeamType.A, firstCenter + new BattleCell(1, 0));
            yield return null;
            _session.SpawnDebugUnit(UnitName.Lisa, second.PlayerId, TeamType.B, secondCenter);
            yield return null;
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
            SceneFadeOverlay.Reveal(0.2f); // 地图配置失败直退路径：揭幕加载页（正常退出无黑场=no-op）
            StopBattleMusic(); // 轮换链停播（回大厅后位置曲由 MainHall 激活钩子复活）
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
