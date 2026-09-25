using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
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

        // 局内手牌构建（B6c）：读玩家存档当前卡组
        [Autowired] private GIC.Framework.SaveManager _saveManager;
        [Autowired] private GIC.Framework.CardManager _cardManager;

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
            if (playerSetups.Count < 2)
            {
                // 单玩家配置守卫（2026-09-23 审查 Y11）：装配链按双玩家假定（队伍对位 A/B、
                // SpawnDebugUnitsRoutine 双方各一），不足即 AI 补位——同 LaunchSinglePlayer 语义。
                // B7 LAN 网络下发配置可能只带 1 人，届时此守卫兜底。
                GICLog.Warn("[BattleScreen] 玩家数 <2，AI 补位");
                playerSetups.Add(new BattlePlayerSetup
                {
                    PlayerId = playerSetups.Count > 0 && playerSetups[0].PlayerId == BattleDebugPlayerIds.P2
                        ? BattleDebugPlayerIds.P1
                        : BattleDebugPlayerIds.P2,
                    DisplayName = "AI 玩家",
                    IsAI = true,
                });
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
            _session.Sim.LogicRoot = logicGo.transform; // 部署等运行时生成单位挂同根（B6c）
            for (int i = 0; i < playerSetups.Count; i++)
            {
                var setup = playerSetups[i];
                _session.RegisterDebugPlayer(setup.PlayerId);
                // 队伍：双端对位 A/B（B6c 部署阵营判定用；B7 联机按房主分配重定）
                _session.Sim.SetPlayerTeam(setup.PlayerId, i == 0 ? TeamType.A : TeamType.B);
            }

            yield return null; // ── 分帧：会话就绪/立牌是重活起点 ──

            // B1 固定测试军逐个立牌（prefab 实例化+依赖资产首载的尖峰摊薄到每单位一帧）
            yield return StartCoroutine(SpawnDebugUnitsRoutine(mapConfig, playerSetups));

            // 局内手牌（2026-09-25 拍板「获得卡片=手牌构建唯一入口，获得/失去对称」）：
            // 空表起步→初始卡组按顺序逐张获得（数量=备战数）→开局送初始资源 200 摩拉+60 体力
            // （编没编货币卡都送，落在牌上）。双方同用玩家存档当前卡组（对称测试；
            // AI 出战决策后续接 AI 脑）；卡不消耗可重复出战
            foreach (var setup in playerSetups)
            {
                _session.Sim.RegisterHand(setup.PlayerId, new List<HandCard>());
                foreach (var handCard in BuildHandFromCurrentDeck())
                    _session.Sim.GainCard(setup.PlayerId, handCard.AsCardId(), handCard.count);
                _session.Sim.GainCard(setup.PlayerId, new CardId(ItemName.Mora), BattleMetrics.InitialMora);
                _session.Sim.GainCard(setup.PlayerId, new CardId(ItemName.Stamina), BattleMetrics.InitialStamina);
            }

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

            // 正式战斗 HUD（B6 提前启动，docs/18 决策六；2026-09-22 prefab 化——结构=BattleHud.prefab 编辑器维护）
            if (_hud == null)
            {
                var hudPrefab = Resources.Load<GameObject>("Prefabs/Battle/BattleHud");
                if (hudPrefab == null)
                {
                    GICLog.Error("[BattleScreen] BattleHud.prefab 未找到，HUD 不可用");
                    DoClose();
                    yield break;
                }
                var hudGo = Instantiate(hudPrefab, transform, false);
                hudGo.name = "BattleHud";
                _hud = hudGo.GetComponent<BattleHud>();
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

        // 当前在播/待播战斗曲的选池时段（跨界判定用：与 BattleTimePeriod 不符且在播→立即淡切，
        // 2026-09-22）。间隔冷却中不插手——下一次选曲自然换池（≤10s）
        private TimePeriod? _currentTrackPeriod;

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
                    // 时段跨界立即淡切（2026-09-22）：独立时钟在回合结束 +20 分钟，跨界瞬间=片循环
                    // 完毕进选择阶段——在播曲目若还是旧时段选的，淡出换新池曲（冷却中未在播则留给
                    // 下一次选曲自然换池）
                    var battlePeriod = _flow.BattleTimePeriod;
                    if (audio.IsMusicPlaying() &&
                        _currentTrackPeriod.HasValue && _currentTrackPeriod.Value != battlePeriod)
                    {
                        GICLog.Info($"[BattleScreen] 战斗时段跨界: {_currentTrackPeriod} → {battlePeriod}，淡切战斗曲");
                        PlayNextBattleTrack(mapConfig, periodSwitch: true);
                        return;
                    }
                    // 真静默才重试：在播/间隔冷却中（clip 仍挂 source）链是活的，不插手
                    if (audio.IsMusicPlaying() || audio.GetCurrentMusicClip() != null) return;
                    PlayNextBattleTrack(mapConfig);
                };
                _flow.OnPhaseChanged += _onPhaseChangedForMusic;
            }
            PlayNextBattleTrack(mapConfig);
        }

        /// <param name="periodSwitch">true=昼夜跨界立即淡切（旧曲淡出→新池曲淡入，2026-09-22）；
        /// 默认 false=常规选曲（硬切起播，链式轮换语义不变）</param>
        private void PlayNextBattleTrack(BattleMapConfig mapConfig, bool periodSwitch = false)
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

            _currentTrackPeriod = period;
            var onComplete = new Action(() =>
            {
                if (!_battleMusicActive) return; // 退出已停链：不再续播
                PlayNextBattleTrack(mapConfig);
            });

            if (periodSwitch)
            {
                var audio = AudioManager.Instance;
                audio.SwitchMusicWithFade(
                    new AudioManager.MusicTrack(clip, MusicType.Battle,
                        intervalAfter: BattleMusicIntervalSeconds, loop: false,
                        fadeInTime: audio.PeriodSwitchFadeInSeconds,
                        onComplete: onComplete),
                    audio.PeriodSwitchFadeOutSeconds);
            }
            else
            {
                AudioManager.Instance.PlayMusicWithInterval(
                    clip, MusicType.Battle,
                    intervalAfter: BattleMusicIntervalSeconds,
                    loop: false,
                    onComplete: onComplete);
            }
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
            _currentTrackPeriod = null;
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
        /// 固定测试军（2026-09-25 用户指令「双方场上各 1 个芭芭拉、安柏、凯亚、丘丘人」）：
        /// 双方**对称镜像阵容**——各 1 安柏/凯亚/芭芭拉（3 星高级单位=双方操控面对称）+
        /// 1 丘丘人（1 星=低级单位自主决策实测对象，docs/04 §4.1）；丽莎移出测试军。
        /// 落点=出生区中心与三个镜像偏移位（3×3 出生区内：先手 center/(+1,0)/(0,+1)/(+1,1)、
        /// 后手 center/(-1,0)/(0,-1)/(-1,-1)，点位对称保证双方接敌距离一致）。
        /// 出生点来自地图配置的玩家出生区；正式出战队列 B6c 已落地（卡组手牌仍可部署，测试军=预铺场）。
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
            _session.SpawnDebugUnit(UnitName.Barbara, first.PlayerId, TeamType.A, firstCenter + new BattleCell(0, 1));
            yield return null;
            _session.SpawnDebugUnit(UnitName.Hilichurl, first.PlayerId, TeamType.A, firstCenter + new BattleCell(1, 1));
            yield return null;

            _session.SpawnDebugUnit(UnitName.Amber, second.PlayerId, TeamType.B, secondCenter);
            yield return null;
            _session.SpawnDebugUnit(UnitName.Kaeya, second.PlayerId, TeamType.B, secondCenter + new BattleCell(-1, 0));
            yield return null;
            _session.SpawnDebugUnit(UnitName.Barbara, second.PlayerId, TeamType.B, secondCenter + new BattleCell(0, -1));
            yield return null;
            _session.SpawnDebugUnit(UnitName.Hilichurl, second.PlayerId, TeamType.B, secondCenter + new BattleCell(-1, -1));
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
        /// 从玩家存档当前卡组构建初始手牌获得清单（2026-09-25 拍板：初始卡组按顺序经 GainCard 获得）：
        /// 每卡携带获得数量（角色=1、物品=备战数 min(存档持有, maxPrepareCount 备战上限)——
        /// 如背包 100 体力牌、备战上限 60 → 获得 60）；**货币物品牌（摩拉/体力）跳过**——
        /// 其开局量统一走「送 200 摩拉+60 体力」（编没编都送，勿双发）；空卡组回退丘丘人×2。
        /// 走 CardManager 卡组视图=与收藏卡组界面同源同排序（角色前物品后、SortOrder、星级）。
        /// </summary>
        private List<HandCard> BuildHandFromCurrentDeck()
        {
            var result = new List<HandCard>();
            var save = _saveManager?.CurrentSave;
            if (save != null)
            {
                int currentDeck = save.progress.currentDeck;
                var decks = _cardManager?.decks;
                if (decks != null && currentDeck >= 0 && currentDeck < decks.Length)
                {
                    foreach (var card in decks[currentDeck].Cards)
                    {
                        // 货币牌开局量统一由装配处的 GainCard(Mora/Stamina, 初始值) 获得
                        if (card.id.cardType == CardType.Item && card.id.AsItemName().IsCurrencyItem()) continue;
                        int count = 1;
                        if (card.id.cardType == CardType.Item)
                        {
                            var itemData = CardConfigResolver.Instance?.ItemConfig?.GetItemData(card.id.AsItemName());
                            count = Mathf.Min(
                                _saveManager?.CurrentSave?.GetItemCount(card.id.AsItemName()) ?? 0,
                                itemData?.maxPrepareCount ?? 0);
                        }
                        result.Add(new HandCard(card.id, count));
                    }
                }
            }
            if (result.Count == 0)
            {
                result.Add(new HandCard(new CardId(UnitName.Hilichurl), 1));
                result.Add(new HandCard(new CardId(UnitName.Hilichurl), 1));
            }
            return result;
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
            _exitDialog = BattleExitConfirmDialog.Show(transform, DoClose); // 文案=弹窗内部本地化键（2026-09-22 转正）
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
