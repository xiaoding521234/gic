# 联机房间正式化与 LAN 真人对战技术设计（2026-10-11 联机批）

> 状态：已落地收官（编译 0 错；编辑器回读断言 21/21 PASS；LAN 真机复测通过——2026-10-23 用户确认）。
> 拍板=docs/18 决策六十五；本批=用户工作令七条（房间设置真实进局内/RA2 式地图预览/开局门/增加AI玩家/测试军退役/双真人 LAN 对战/联机 UI 美化）。
> 交付时按推荐方案执行的六项拍板：AI 卡组=host 当前卡组；真人卡组=各自存档卡组上传；FirstMap 出生区扩 4 点（四角）；开局限两方（A/B）；战后房间解散；测试军全路径退役（沙盒除外）。
> 状态补充：**已收官现状文档**（2026-10-23 整理迁根区）——§6「已知边界与后续增强」的能力路线已展开为 docs/active/43（N0~N6 路线图，其 N1 动工前置=本批 LAN 双真机复测，现已通过）；联机规则细则指针=docs/20 §1.2。

## 1. 房间数据层正式化

- **AI 玩家**：`PlayerInfo.IsAI` 新增网络同步字段。登记=host 经「增加AI玩家」按钮 → `AddAIPlayerRequestEvent`(OnlyHost) → `RoomManager.AddAIPlayer()`：ID=`AI_N`（与 connectionId 数字串天然不撞）、队伍=自动平衡（人数较少队，同数取 A）、颜色=首个未占用色、**恒就绪**（不占就绪门）、无连接（`connectionToClient=null`）。移除=点 AI 行名字（Kick 链对空连接既有守卫，同一路径复用）。
- **颜色自动分配改良**：旧「按人数取模」在玩家换色后必撞色 → 改扫首个未占用色，全满回落取模。
- **进房自动平衡队伍**：`GetBalancedTeam()`=人数较少队伍（同数取 A）——host 在 A 时第二人自动落 B，开局门「异队≥2」无需手动改队即满足。
- **地图选择权威化**：`RoomManager.RoomMapConfigName` 服务器单源；host 下拉选图 → `SetMapRequestEvent`(OnlyHost) → 广播 `UpdateRoomInfoEvent`(All) → 各端同步下拉+预览（`CoopScreen.OnRoomMapChangedHandler`）。新客户端进房时服务器定向补推当前图（HandleServerConnect）。下拉仅 host 可交互；`GetSelectedMapConfig()` 一律按权威名解析（下拉本地索引仅为显示）。
- **出生位置重键**：`SpawnZone.playerId`（写死 P1/P2）退役 → `zoneIndex`（出生区序号 1 起，列表序即序号）；`SpawnPositionType` 收敛为 `Random=0 + Zone1..Zone8`（数值=区号）。FirstMap 出生区 2→4（四角 (2,2)/(12,2)/(2,12)/(12,12)，另两角补 3×3 石路同款视觉；迁移工具=`Editor/Tool/CoopRoomMapZoneMigration.cs` 幂等备查）。玩家行出生下拉选项数=当前地图出生区数。

## 2. 开局门（ValidateStartConditions 重写）

校验序=容量（人数≤出生区数）→出生点冲突（Random 不参与）→颜色冲突→**队伍门**（≥2 玩家且 ≥2 个不同队伍，弹 `Coop_NeedTwoTeams`；>2 队防御性拦弹 `Coop_TeamLimit`——队伍下拉仅 A/B，正常不可达）→全员就绪（AI 恒就绪）。
**AI 自动补位退役**：`BuildSinglePlayer`（P1 真人+P2 AI）与 BattleScreen「<2 人补 AI」守卫全删——单人开局须先在房间加 AI 玩家。

## 3. 名册真实进局内（BattleLaunchConfig 带 Roster）

- `BattlePlayerSetup` 扩展 `Team/Color/SpawnPosition`；唯一正式工厂 `BuildFromRoster(mapName, players)`（房间 PlayerManager 全量投影，含 AI）。
- BattleScreen 消费：`Sim.SetPlayerTeam(pid, setup.Team)`（房间小队=局内阵营，引擎按异队=敌对原生语义；旧「按索引硬派 A/B」退役）；N 玩家全员注册（旧「>2 截断」删除）；**开局只立协议核心**（每玩家一枚立出生区中心；`SpawnDebugUnitsRoutine` 测试军全路径退役——正式出战走手牌部署 B6c；沙盒路径保留）。
- **出生区分配**（`ResolveSpawnCells`）：指定出生点按区落位；Random 自动分配「离已占区最远」空区（切比雪夫 min-dist 最大化——两人=对角开局 RTS 惯例；同距取小序号）。
- **颜色进局内**：`BattleSession.Roster` 双端同构；`BattlePlayerColors.Resolve(session,…)` Roster 优先（LAN 客户端无 Sim 也成立）、无条目回落 BattlePalette 两表；HUD `MyTeam` 改走 session 门面。

## 4. 房间 UI（RA2 式预览 + AI 按钮 + 美化）

- **地图预览面板**（房间右上，`MapPreviewPanel`）：程序化地形小图（`BattleMapConfig.rows` 字符→Texture2D 平涂、Point 过滤 RA2 像素风、按图缓存）+ 标题条（复用 RoomBoardText=地图名，暖金 #D3BC8E）+ 出生点标记层（编号圆点；被选走染玩家色——「房间选的出生位置/颜色」可视化）。组件=`UI/Screen/Coop/BattleMapPreviewUI.cs`（prefab 直挂、[SerializeField 中文参数]；方形适配=组件自算居中方形，地形图与标记层逐像素对齐）。
- **增加AI玩家按钮**：房间底部（StartButton 右侧，克隆其结构=项目按钮素材+BoxShadow），仅 host 可见可点；点击→登记一名 AI；点 AI 行名字=移除。
- **美化**：表头条 #C9FFC364 薄荷绿残留→深底 #080E14C8+金字；地图下拉 Unity 内置 UISprite→项目 Background 切片+暖白；PlayerRow.prefab 九处下拉/按钮体换项目切片、字色统一深墨 #323232。

## 5. LAN 真人对战（B7：IBattleTransport-Mirror）

### 5.1 传输层
- `BattleNetMessage : NetworkMessage`（struct——Mirror RegisterHandler 泛型约束；type+json 与 LocalBattleTransport 信封同构）+ `MirrorBattleTransport : IBattleTransport`（GIC.Battle，`Battle/Flow/`）：
  - HostSend=逐远端连接 Send + **host 本机视图直投**（host 也是观众/玩家；跳过 connectionId=0 本地连接防双投）；
  - ClientSend=host 本机直调 host 路由 / 远端 NetworkClient.Send；
  - `RegisterHostHandler` 携带发送方标识（ack 按连接计数用）；
  - Mirror 处理器注册走 ReplaceHandler（再开局重注册不叠加）；**早到队列**（客户端路由未注册前收到的 Host 下行先入队≤64，注册时冲队保序——开局握手门控之外的双保险）。
- 战斗铁律保持：独立 Mirror 消息类型，不走 EventBusHub。

### 5.2 开战广播与两端装配
- Host 点开局（校验过）：`ResetBattleExchange()` → 广播 `StartBattleEvent{map,seed,players}`（All+Immediate）——**不拆网络**（连接贯穿战斗）。
- 各端（含 host 本机回环）统一 `CoopScreen.OnStartBattleReceived`：客户端先回 `ClientDeckEvent`（本机存档当前卡组，`BattleDeckBuilder.BuildCurrentDeck` 与 BattleScreen 手牌构建同源）→ 全端同一转场（Cover→SwitchRootScene(BattleScreen)）。
- **BattleScreen 三形态装配**：①本地（调试直开/沙盒/host+纯 AI）=`CreateLocal`（旧链路）；②LAN host=`CreateHosted`（Sim/Flow/AI 脑+Mirror 传输+期望 ack 方数注入）；③LAN 客户端=**`CreateClient`（仅 BattlePlayer 表现层，Sim/Flow=null**，场景序列化的 TurnFlowController 引用作废置空——防相位门面误读死物）。判据=`launchConfig.IsNetworked && !NetworkServer.active`。
- **就绪握手**（Host 永不跑在客户端前面）：客户端 HUD 绑定完成 → `ClientBattleReadyEvent`(OnlyHost) → host `WaitForRemoteBattleReady` 收齐（15s 超时快进）才 `StartBattle()` 广播 BattleStart。
- **卡组来源**（2026-10-12 追记拍板「AI 玩家暂时都用 安柏，凯亚，芭芭拉，体力，摩拉」）：本机真人=本机存档；LAN 远端真人=上报卡组（5s 等待超时回落 host 卡组）；**AI 玩家=固定默认卡组**（`BattleDeckBuilder.BuildAIDefaultDeck`：三伙伴各 1+体力/摩拉——取代旧「AI=host 当前卡组」口径）。**AI 出牌**=配额脑部署分支（`PlayerQuotaBrain.TryBuildDeploy` 部署优先于号令/操魔神：合法落点单源 `IsDeployCellValid`+摩拉够 `GetEffectiveDeployCost`+**同名不在场〔含尸体——升命语义守卫，docs/05 §5.4〕**→出战；三伙伴全上场后归位号令/Pass）。

### 5.3 客户端无 Flow 的表现适配
- `SnapshotMessage` 增 `selectLimitSeconds`（host 选择阶段头随快照下发）。
- `BattlePlayer` 客户端相位派生（`_flow==null` 的端才生效，host 端零污染）：OnSnapshot→Selecting+本地倒数、OnSegment→Resolving、OnBattleOver→Finished；`ClientPhaseChanged/ClientSelectTimerExpired` 事件 + 本地倒数归零自动完成选择（host 权威超时兜底，重复提交 host 已定死忽略）。两态立牌模型由派生相位驱动。
- `BattleSession` 相位门面（HUD 唯一订阅口）：`OnPhaseChanged/OnSelectTimerExpired/Phase/SelectRemainingSeconds/BattleTimeMinutes`（host=Flow 权威，client=消息派生/快照回合推导 06:00+20×(turn-1)）+`SelectTimerPaused`。HUD 全部 `_session.Flow` 引用改走门面（相位/倒计时/时钟/选择态判断/布局编辑冻结——View 层 Flow 引用清零）。
- `_myPlayerId`=`PlayerManager.SelfPlayerID`（房间分配 connectionId；本地双开/调试直开=Offline 回落 P1）。

### 5.4 ack 按发送方计数
`TurnFlowController._ackedKeys` 改 `key→HashSet<发送方>`（"local"/connectionId）；`HasSegmentAck`=ack 方数≥`ExpectedAckerCount`（host 注入 `1+MirrorBattleTransport.RemoteClientCount()`；本地默认恒 1）。超时快进兜底保留。

### 5.5 掉线处置（v1 最小集）
- 客户端掉线：host 名册移除；其单位照常吃超时自动 Pass（既有兜底）。
- Host 掉线/Host 先退房：客户端 `MyNetworkManager.OnClientDisconnect` 钩子——战斗场景中直接回大厅+`Coop_HostDisconnected` toast（勿加载页遮盖——MainHall 无 Reveal 收尾会黑屏 15s）。
- **战后房间解散**（v1 拍板）：战后回大厅 CoopScreen.OnShow 统一拆线；房间保持/连打=后续可选增强。

## 6. 已知边界与后续增强

- 快照含双方手牌数据（HUD 只显示自己的——网络层信息隐藏留待 B7 硬化）。
- 断线重连（快照全量机制天然支持）未做；观战端未做。
- 2v2 结构就位（N 玩家注册/出生区 4 点/胜负判定多人口径既有）但实测以 1v1 为主——2v2 复测后如有问题另批。
- 早到队列容量 64（BattleStart+快照+首片量级充裕）；若未来单回合消息超量再评估。

## 7. 本地化

UIText 2006 `Coop_AddAIPlayer`（增加AI玩家）/2007 `Coop_RemoveAI`（移除）；PopupText `Coop_NeedTwoTeams`/`Coop_TeamLimit`/`Coop_HostDisconnected`（自动大 id 续排）。五语言+域重载复验+CSV 重导（列序=既有表头：UIText=zh-Hans,en,ja,zh-TW,ru；PopupText=zh-Hans,zh-TW,en,ja,ru）。

**AI 难度/人格词本地化（2026-10-13 挂账销案——选择器批 ×12+2 前缀标签）**：UIText 2008~2011 `AI_Difficulty_Apprentice/Rival/Grandmaster/Providence`（学徒/劲敌/棋圣/天理）+ 2012~2019 `AI_Persona_狂战士/守城者/经济家/指挥官/赌徒/交涉家/背叛者/侦察家`（**人格键=资产名=数据身份零漂移**——新人格=加资产+同键名加表项零代码）+ 2020/2021 `AI_Difficulty_Label`/`AI_Persona_Label`（难度/人格下拉前缀——超出 ×12 口径补齐：不本地化前缀则非中文玩家看到混合语言）。显示接线=PlayerRowView.LocalizeDifficulty/LocalizePersona（**表缺键回落中文兜底；人格数组=数据身份不再是显示文案**；「AI 玩家N」玩家名仍=运行时内容串先例不本地化）。五语言+域重载复验+zh-TW/ja 码位核验全过（ja 済=6E08 与 zh-TW 濟=6FDF、ja 渉=6E89 与 涉=6D89 两处易错字形分野正确）+CSV 重导（286 行）。

## 8. 复测清单（已执行完毕——复测通过 2026-10-23 用户确认；清单本体见 docs/18 决策六十五条目尾）
