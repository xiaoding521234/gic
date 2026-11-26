using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Video;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 战斗场景控制器（独立根场景，Single 加载）：
    /// 消费 BattleLaunchConfig（联机界面开局）或进入双开调试模式（config 为 null 的调试直开）；
    /// 三形态装配（2026-10-11 联机批）：本地（Host 逻辑 + Client 播放器同进程）/ LAN host（Sim+AI 脑，
    /// 广播开战）/ LAN 客户端（同构模拟，本机演算表现）；AI 玩家自动上交行动。
    /// ESC/右键/结束按钮 → 退出确认弹窗（2026-09-12 用户拍板：不应直接退出）。
    /// 五阶段完整回合流程与正式 HUD 在 B5/B6 落地（回合制时代里程碑——回合机已于 2026-11-04 决策九十五退役，现为即时制 RTSFlowController 秒拍流程）。
    /// </summary>
    public class BattleScreen : ScreenBase
    {
        [Header("战斗组件（场景装配）")]
        [SerializeField] private BattleBoard _board;
        [SerializeField] private BattlePlayer _player;
        [SerializeField] private RTSFlowController _flow;
        [SerializeField] private BattleHud _hud;

        private BattleSession _session;
        private BattleExitConfirmDialog _exitDialog;
        private bool _networkBattle;
        private bool _isLanClient;

        // 战斗地图音乐：按绑定位置取 PositionConfig 昼夜曲池（2026-09-14）
        [Autowired] private PositionManager _positionManager;

        // 局内手牌构建（B6c）：读玩家存档当前卡组
        [Autowired] private GIC.Framework.SaveManager _saveManager;
        [Autowired] private GIC.Framework.CardManager _cardManager;

        // 联机批（2026-10-11）：卡组上报/战斗就绪交换走 RoomManager（B7 LAN）
        [Autowired] private GIC.Framework.RoomManager _roomManager;

        /// <summary>当前对局（调试/测试访问口）</summary>
        public BattleSession Session => _session;

        private void Start()
        {
            RegisterClosableSelf();
            StartCoroutine(AssembleRoutine());
        }

        private void Update()
        {
            if (_isLanClient && !isClosing && !Mirror.NetworkClient.isConnected)
                CloseDisconnectedBattle();
        }

        /// <summary>
        /// 装配协程（2026-09-13 分帧化，用户拍板"加载不阻塞"）：
        /// 建盘/立牌/面板等同步装配原为一帧 1.1s+ 尖峰（实测），加载页动画整帧冻结——
        /// 重活之间插帧，把单帧尖峰摊薄到多帧；Reveal 收尾不变（装配完毕才揭幕）。
        /// 中途异常协程中断 → SceneFadeOverlay 15s 安全兜底自动揭幕，不会卡死加载页。
        ///
        /// 2026-10-11 联机批改造：三形态装配——
        /// ①本地（调试直开/沙盒）：Host+Client 同进程（CreateLocal，旧 B1-B6 链路）；
        /// ②LAN host：CreateHosted（Sim/Flow/AI 脑），开局前等远端「战斗就绪」握手再 StartBattle 广播；
        /// ③LAN 客户端：绑定 HUD 和消息路由后发就绪 → 按 BattleStart 建盘后揭幕。
        /// 名册消费：房间队伍=局内阵营、房间选色=Roster（HUD/描环单源）、出生位置=地图出生区序号
        /// 落位（Random=按序自动分配未占用区）。开局只立协议核心（测试军 2026-10-11 退役）。
        /// </summary>
        private IEnumerator AssembleRoutine()
        {
            // 消费开局配置（consume-once）：null = 调试直开（双端手动）
            var launchConfig = BattleLaunchConfig.Take();
            _networkBattle = launchConfig?.IsNetworked == true;
            var playerSetups = launchConfig?.Players;
            if (playerSetups == null || playerSetups.Count == 0)
            {
                playerSetups = new List<BattlePlayerSetup>
                {
                    new BattlePlayerSetup { PlayerId = BattleDebugPlayerIds.P1, DisplayName = "玩家P1", IsAI = false },
                    new BattlePlayerSetup { PlayerId = BattleDebugPlayerIds.P2, DisplayName = "玩家P2", Team = TeamType.B },
                };
            }
            _isLanClient = _networkBattle && launchConfig.LocalPlayerId != PlayerID.Host;
            if (_isLanClient && !Mirror.NetworkClient.isConnected)
            {
                CloseDisconnectedBattle();
                yield break;
            }
            if (_networkBattle && !_isLanClient && !Mirror.NetworkServer.active)
            {
                AbortBattleStartup("房主服务已经停止");
                yield break;
            }

            // 地图：联机入口按配置名加载；调试直开用默认地图（各端资产本地同构）
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
            if (isClosing) yield break;

            // 组件兜底（场景装配缺失时补挂，便于直接打开场景调试）
            if (_player == null) _player = gameObject.AddComponent<BattlePlayer>();
            // 迷雾批（2026-10-17，docs/active/44）：开局配置透传迷雾模式（房间对战=Skirmish/任务=Mission/
            // 调试直开/沙盒=None）——建盘前注入 BattlePlayer（BattleStart 时装配迷雾控制器）。
            // F1 帧同步：客户端也跑 tick 状态机（同构 Sim）；单机/LAN host/LAN client 三形态统一
            if (_flow == null) _flow = gameObject.AddComponent<RTSFlowController>();
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

            _player.UseBoard(_board);
            _player.ConfigureFog(launchConfig?.FogMode ?? BattleFogMode.None,
                () => _session?.LocalPlayerId ?? BattleDebugPlayerIds.P1,
                pid => _session != null ? _session.GetTeamOf(pid) : TeamType.A);
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
            if (isClosing) yield break;

            // ==================== ①本地 / ②LAN host / ③LAN client 装配（F1 帧同步） ====================
            // 逻辑单位隐藏根（Host 侧逻辑对象；表现层为 UnitView；客户端 Sim 也挂此根）
            var logicGo = new GameObject("LogicUnits");
            logicGo.transform.SetParent(transform, false);

            // 战斗种子（配置带种用配置；调试直开随机生成——Host 生成后随 BattleStart 广播）
            int battleSeed = launchConfig != null ? launchConfig.Seed : UnityEngine.Random.Range(0, int.MaxValue);

            if (_networkBattle)
            {
                if (_isLanClient)
                {
                    // F1/F2：客户端也跑同构 Sim；Sim 在收到 BattleStart 后创建（seed/map/roster 随消息到达）
                    _session = BattleSession.CreateClientSim(_player, _flow, logicGo.transform,
                        new MirrorBattleTransport(false, launchConfig.MatchId), launchConfig);
                }
                else
                {
                    _session = BattleSession.CreateHosted(map, _player, _flow, logicGo.transform, launchConfig);
                }
            }
            else
            {
                _session = BattleSession.CreateLocal(map, _player, _flow, logicGo.transform, battleSeed,
                    launchConfig?.Players); // 调试直开 Players=null（无房间名册——色/队回落旧口径）
            }
            if (_session.IsHostSide)
                yield return AssembleHostRoutine(launchConfig, mapConfig, playerSetups, battleSeed);
            if (isClosing || !BindHud(GameObject.Find("BattleCamera")?.GetComponent<BattleCameraController>()))
                yield break;

            if (_isLanClient)
            {
                EventBusHub.Instance.Send(new ClientBattleReadyEvent { MatchId = launchConfig.MatchId });
                float deadline = Time.realtimeSinceStartup + 60f;
                while (!isClosing && _session.Phase == BattlePhase.Idle && _session.StartupError == null
                       && Mirror.NetworkClient.isConnected && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (isClosing) yield break;
                if (!Mirror.NetworkClient.isConnected)
                {
                    CloseDisconnectedBattle();
                    yield break;
                }
                if (_session.Phase == BattlePhase.Idle)
                {
                    AbortBattleStartup(_session.StartupError ?? "等待房主开战超时");
                    yield break;
                }
            }
            else
            {
                if (_networkBattle && !_roomManager.TryCompleteBattleExchange(launchConfig.MatchId))
                {
                    AbortBattleStartup("远端在开局装配期间掉线");
                    yield break;
                }
                _session.StartBattle();
            }

            StartBattleMusic(mapConfig);
            SceneFadeOverlay.Reveal(0.2f);
        }

        private IEnumerator AssembleHostRoutine(BattleLaunchConfig launchConfig, BattleMapConfig mapConfig,
            List<BattlePlayerSetup> playerSetups, int battleSeed)
        {
            // 迷雾账本模式注入（2026-10-17 追拍三「AI 也受雾限」）：Host 侧 AI 探索集与表现层同模式
            //（房间对战=Skirmish/任务=Mission/调试直开=None=AI 不受限）；客户端随开局消息注入
            _session.Sim.FogMode = launchConfig?.FogMode ?? BattleFogMode.None;

            // 名册注册：房间队伍=局内阵营（同队友军/异队敌对，引擎原生语义；2026-10-11 联机批——
            // 旧「按索引硬派 A/B」退役）
            foreach (var setup in playerSetups)
            {
                if (!_networkBattle) setup.ResolveRules();
                setup.RegisterIn(_session.Sim);
            }

            yield return null; // ── 分帧：会话就绪/立牌是重活起点 ──

            // 出生区分配（2026-10-11 联机批）：指定出生点按区落位、Random 按序自动分配未占用区
            var spawnCells = ResolveSpawnCells(mapConfig, playerSetups);

            // 协议核心（2026-09-29 拍板 Unit 化——第一个建筑，也是玩家基地）：每玩家一枚立于
            // 出生区中心格=胜负判据锚点（docs/02 §2：核心摧毁=战败）；SpawnDebugUnit=UnitFactory
            // 真实链，注册期 BattleSimState 登记核心、快照/客户端立牌自动成立；分帧立牌。
            // 开局仅立协议核心（测试军 2026-10-11 退役：正式出战走手牌部署 B6c）
            foreach (var setup in playerSetups)
            {
                if (_session.SpawnDebugUnit(UnitName.ProtocolCore, setup.PlayerId,
                    setup.Team, spawnCells[setup.PlayerId]) == null)
                {
                    AbortBattleStartup("协议核心未能生成");
                    yield break;
                }
                yield return null;
            }

            // 试招沙盒（2026-10-05 时轮编辑器「开一把试招」）：A=编辑单位（层级覆盖 5★ 魔神档全手操）、
            // B=木桩空座（零行动天然等效 Pass，站桩挨打）
            if (launchConfig != null && launchConfig.IsSandbox)
            {
                _session.Sim.SandboxAutoPassPlayerId = playerSetups[1].PlayerId; // 空座沙盒标记（即时制无选择提交，零行动天然等效 Pass；现役消费=BattleSession 玩家计数扣减）
                var allyCenter = spawnCells[playerSetups[0].PlayerId];
                // 试探单位：出生中心东 1 格（中心格已让位协议核心）；木桩同行再东 5 格=十字射程内同屏
                _session.SpawnSandboxUnit(launchConfig.SandboxAllyUnit, playerSetups[0].PlayerId, TeamType.A,
                    allyCenter + new BattleCell(1, 0));
                yield return null;
                _session.SpawnDebugUnit(launchConfig.SandboxDummyUnit, playerSetups[1].PlayerId, TeamType.B,
                    allyCenter + new BattleCell(6, 0));
            }

            if (_networkBattle)
            {
                float deadline = Time.realtimeSinceStartup + 25f;
                while (!_roomManager.IsBattleExchangeReady(launchConfig.MatchId, out bool failed))
                {
                    if (failed || Time.realtimeSinceStartup >= deadline)
                    {
                        AbortBattleStartup(failed ? "开局期间有玩家掉线" : "等待玩家上报卡组和就绪超时");
                        yield break;
                    }
                    yield return null;
                    if (isClosing) yield break;
                }
            }

            // 待机片卡组预热池（2026-10-16 部署黑窗根治批）：本局全部玩家手牌的角色并集——部署可能
            // ⊆ 手牌 ⊆ 卡组，开局一次暖全并集（手牌抽取不增角色，无需增量）。池=首帧模板库，部署
            // 时 Blit 拷贝零黑窗；池外角色（LAN 客户端不可见的敌方人类卡组）走静态立牌过渡兜底。
            IdleClipPrewarmPool.Clear(); // 上一局残留清空（模板 RT 释放）
            var prewarmUnits = new HashSet<GIC.Data.UnitName>();
            // 回放录制（2026-10-20 Replay harness 批）：初始手牌全量收集（含初始资源卡）——
            // 初始态终点快照式录制（卡组构建上游不录，卡组/存档变化不影响回放）
            Dictionary<string, List<HandCard>> replayHands = BattleRecorder.Enabled
                ? new Dictionary<string, List<HandCard>>() : null;
            var localOwnedUnitIds = _saveManager?.CurrentSave?.EnumerateOwnedUnitIds().ToList();
            foreach (var setup in playerSetups)
            {
                bool remoteHuman = _networkBattle && !setup.IsAI && setup.PlayerId != PlayerID.Host;
                _session.SetPurchaseUnlocks(setup.PlayerId, remoteHuman
                    ? _roomManager?.GetUploadedOwnedUnitIds(setup.PlayerId)
                    : localOwnedUnitIds);
                _session.Sim.RegisterHand(setup.PlayerId, new List<HandCard>());
                foreach (var handCard in BuildHandForPlayer(setup, remoteHuman))
                {
                    var card = handCard.AsCardId();
                    _session.Sim.GainCard(setup.PlayerId, card, handCard.count);
                    if (card.cardType == CardType.Unit)
                        prewarmUnits.Add((UnitName)card.value);
                }
                _session.Sim.GainCard(setup.PlayerId, new CardId(ItemName.Mora), BattleMetrics.InitialMora);
                _session.Sim.GainCard(setup.PlayerId, new CardId(ItemName.Stamina), BattleMetrics.InitialStamina);
                replayHands?.Add(setup.PlayerId, _session.Sim.GetHand(setup.PlayerId).ToList());
            }
            IdleClipPrewarmPool.Prewarm(this, CollectIdleClips(prewarmUnits));
            // 回放录制 header（沙盒局不录——沙盒单位生成未入录制面，回放会缺单位）
            if (launchConfig == null || !launchConfig.IsSandbox)
                BattleRecorder.CaptureSetup(battleSeed,
                    launchConfig != null ? launchConfig.MapConfigName : BattleLaunchConfig.DefaultMapName,
                    (int)(launchConfig?.FogMode ?? BattleFogMode.None), playerSetups, spawnCells, replayHands,
                    isNetworked: _session.IsNetworked);

            // AI 玩家配额脑（B6b 评分制→D 批次操控分层收窄：号令/操魔神/空过，docs/32 §6；
            // 眷属/伙伴自主脑在 Host 秒拍流程内联运行（FamiliarBrain/CompanionBrain），非挂件）。
            // 难度已从开局名册注册至本局模拟，脑只需装配人格；
            // 人格按名换资产（缺资产=脑内回落默认 Persona_经济家，见 PlayerQuotaBrain.Persona）
            foreach (var setup in playerSetups)
            {
                if (!setup.IsAI) continue;

                var brainGo = new GameObject($"AIBrain_{setup.PlayerId}");
                brainGo.transform.SetParent(transform, false);
                var brain = brainGo.AddComponent<PlayerQuotaBrain>();
                if (!string.IsNullOrEmpty(setup.AIPersona))
                    brain.人格原型 = Resources.Load<PersonaPack>($"Configs/AI/Persona_{setup.AIPersona}");
                brain.Bind(_session, setup.PlayerId);
                GICLog.Info($"[BattleScreen] AI 玩家就位：{setup.PlayerId}（{setup.DisplayName}，" +
                    $"难度={setup.AIDifficulty} 人格={setup.AIPersona}）");
            }
        }

        /// <summary>
        /// HUD 装配单出口（两形态共用）：prefab 兜底实例化 + 相机守卫（批9 复审②）+ Bind
        /// </summary>
        private bool BindHud(BattleCameraController cameraCtrl)
        {
            if (_hud == null)
            {
                var hudPrefab = Resources.Load<GameObject>("Prefabs/Battle/BattleHud");
                if (hudPrefab == null)
                {
                    GICLog.Error("[BattleScreen] BattleHud.prefab 未找到，HUD 不可用");
                    DoClose();
                    return false;
                }
                var hudGo = Instantiate(hudPrefab, transform, false);
                hudGo.name = "BattleHud";
                _hud = hudGo.GetComponent<BattleHud>();
            }
            // 批9 复审②：相机守卫补全——原 null 时静默传 HUD.Bind，板面点击/拖动跟随全链静默死
            if (cameraCtrl == null || _hud == null)
            {
                GICLog.Error("[BattleScreen] 场景缺少 BattleCamera/BattleCameraController，HUD 板面交互链不可用——无法开战");
                DoClose();
                return false;
            }
            _hud.Bind(_session, _board, cameraCtrl, Close);
            // N0 浏览暂停（2026-10-13 决策六十七）：战斗内「设置/退出」确认弹窗由本屏持有（每次 Show 新实例、
            // 关闭即自毁），开态经回调供 HUD 聚合——关闭路径多（确认/取消/ESC/点外）由 IsOpen 统一判定
            _hud.RegisterExternalBrowseSource(() => _exitDialog != null && _exitDialog.IsOpen);
            return true;
        }

        // ==================== 出生区分配（2026-10-11 联机批） ====================

        /// <summary>
        /// 名册→出生区中心格解析：指定出生点（Zone1..N）按区落位；Random 自动分配「离已占区最远」
        /// 的空区（对角开局惯例）。同区冲突由房间开局门校验拦截。地图无出生区时
        /// 回落对角双角（调试直开兜底）。
        /// </summary>
        private static Dictionary<string, BattleCell> ResolveSpawnCells(BattleMapConfig config,
            List<BattlePlayerSetup> playerSetups)
        {
            var result = new Dictionary<string, BattleCell>();
            int zoneCount = config.spawnZones != null ? config.spawnZones.Count : 0;

            if (zoneCount == 0)
            {
                // 无出生区地图（不应出现——开局门容量校验拦）：对角双角按序兜底
                for (int i = 0; i < playerSetups.Count; i++)
                    result[playerSetups[i].PlayerId] = i % 2 == 0 ? new BattleCell(2, 2) : new BattleCell(12, 12);
                return result;
            }

            // 先收全部指定占位（Random 不占）；再逐名册序为 Random 分配「离已占区最远」的空区
            // （RTS 惯例对角开局：两人=对角、四人=四角；同距取小序号）
            var used = new HashSet<int>();
            var usedCells = new List<BattleCell>();
            foreach (var s in playerSetups)
            {
                int z = (int)s.SpawnPosition;
                if (z > 0 && z <= zoneCount)
                {
                    used.Add(z);
                    usedCells.Add(config.spawnZones[z - 1].center);
                }
            }
            foreach (var setup in playerSetups)
            {
                int zone = (int)setup.SpawnPosition;
                if (zone <= 0 || zone > zoneCount)
                {
                    zone = PickFarthestFreeZone(config, used, usedCells);
                    used.Add(zone);
                    usedCells.Add(config.spawnZones[zone - 1].center);
                }
                result[setup.PlayerId] = config.spawnZones[zone - 1].center;
            }
            return result;
        }

        /// <summary>「离已占出生区最远」的空区（切比雪夫距离取最小值最大化；无参照=1 号区；
        /// 全占回落 1——容量门应拦此态）</summary>
        private static int PickFarthestFreeZone(BattleMapConfig config, HashSet<int> used, List<BattleCell> usedCells)
        {
            int zoneCount = config.spawnZones.Count;
            int best = 1; int bestDist = -1;
            for (int z = 1; z <= zoneCount; z++)
            {
                if (used.Contains(z)) continue;
                if (usedCells.Count == 0) return z; // 空场=首空区（1 号）
                int minDist = int.MaxValue;
                foreach (var c in usedCells)
                {
                    int d = Mathf.Max(Mathf.Abs(config.spawnZones[z - 1].center.x - c.x),
                                      Mathf.Abs(config.spawnZones[z - 1].center.y - c.y));
                    if (d < minDist) minDist = d;
                }
                if (minDist > bestDist) { bestDist = minDist; best = z; }
            }
            if (bestDist < 0) return 1; // 全占兜底
            return best;
        }

        // ==================== 卡组来源（2026-10-11 联机批） ====================

        /// <summary>AI 使用默认卡组，远端使用本局已上报的卡组（空表合法）。</summary>
        private IReadOnlyList<HandCard> BuildHandForPlayer(BattlePlayerSetup setup, bool remoteHuman)
        {
            if (setup.IsAI) return BattleDeckBuilder.BuildAIDefaultDeck();
            return remoteHuman ? _roomManager.GetUploadedDeck(setup.PlayerId) : BuildHandFromCurrentDeck();
        }

        /// <summary>待机片卡组预热池收集（2026-10-16 部署黑窗根治批）：角色并集 → 各自 立牌动画视频
        /// （null/缺配置跳过=该角色部署走池外静态立牌过渡兜底）</summary>
        private List<VideoClip> CollectIdleClips(HashSet<UnitName> units)
        {
            var unitConfig = Wargame.Instance?.Context?.Get<UnitConfig>();
            var result = new List<VideoClip>();
            if (unitConfig == null) return result;
            foreach (var unitName in units)
                if (unitConfig.TryGetUnitData(unitName, out var unitData) && unitData.立牌动画视频 != null)
                    result.Add(unitData.立牌动画视频);
            return result;
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
        /// 选池用**战斗独立时钟时段**（RTSFlowController.BattleTimePeriod，全局 TimeUtility 仅
        /// 未就绪兜底）。链式 PlayMusicWithInterval：播完 → 间隔 10s → 按当前战斗时段重选池 →
        /// 随机下一首（时段随战斗独立时钟推进自然切换，不打断在播曲目）。另订阅相位变化事件做
        /// 自愈重试：链曾因池空静默死亡时，新秒拍相位事件且真静默则重新起链。曲池为空仅警告不阻断。
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
                    // 时段跨界立即淡切（2026-09-22）：独立时钟每秒 +1 分钟（RTSFlowController 战斗时钟），
                    // 跨界瞬间=秒拍相位事件——在播曲目若还是旧时段选的，淡出换新池曲（冷却中未在播则留给
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
                GICLog.Warn($"[BattleScreen] 战斗地图 {mapConfig.mapName} 绑定位置 {mapConfig.position} 无 {period} 时段音乐，等待下一秒拍自愈重试");
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
            if (_battleMusicActive) StopBattleMusic();
            _session?.Dispose();
            BattleRecorder.EndSession(); // 回放录制收笔（Replay harness 批）——中途退出/终局统一兜底
            base.OnDestroy();
        }

        /// <summary>
        /// 从玩家存档当前卡组构建初始手牌获得清单（2026-10-11 联机批：逻辑抽入 BattleDeckBuilder
        /// 单源——CoopScreen 的 LAN 客户端卡组上报同链；本方法保留为 host 侧便捷入口）
        /// </summary>
        private List<HandCard> BuildHandFromCurrentDeck()
            => BattleDeckBuilder.BuildCurrentDeck(_saveManager, _cardManager);

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
        private void AbortBattleStartup(string reason)
        {
            GICLog.Error($"[BattleScreen] 开局中止：{reason}");
            DoClose();
            PopupManager.Instance?.ShowToast(reason);
        }

        private void CloseDisconnectedBattle()
        {
            GICLog.Warn("[BattleScreen] 与房主失联，退回大厅");
            DoClose();
            PopupManager.Instance?.ShowToast(new LocalizedString(TableName.PopupText.ToString(), "Coop_HostDisconnected"));
        }

        private void DoClose()
        {
            if (isClosing) return;
            isClosing = true;
            InputLocks.Push(this, InputLockReason.Closing);
            SceneFadeOverlay.Reveal(0.2f); // 地图配置失败直退路径：揭幕加载页（正常退出无黑场=no-op）
            StopBattleMusic(); // 轮换链停播（回大厅后位置曲由 MainHall 激活钩子复活）
            _session?.Dispose();
            if (_networkBattle && Mirror.NetworkManager.singleton is MyNetworkManager network)
                network.LeaveBattleRoom();
            StartCoroutine(CloseToMainHall());
        }

        private IEnumerator CloseToMainHall()
        {
            PopMusicSafe();
            yield return null;
            while (InputLocks.HasLock(InputLockReason.SceneTransition)) yield return null;
            InputLocks.Pop(this, InputLockReason.Closing);
            // 战斗为根场景，无 GoBack 历史 → 直接回大厅
            SceneType.MainHall.Load();
        }
    }
}
