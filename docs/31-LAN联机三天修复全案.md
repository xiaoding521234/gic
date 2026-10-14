# 31 - LAN 联机「手机→host 方向全死」三天修复全案

> 2026-10-12 ~ 2026-10-15 凌晨，十六批迭代，七个独立根因层层剥开。
> 起点：双机能连、host→手机方向正常、手机→host 方向（名字上报/准备/切队/换色）全死、每次复测症状恒定不变。
> 终点：真机验证通过并完整对战一局。
> 本文是全案叙事与方法论沉淀；逐批细节见 docs/14 §151/§152 + CODELY.md LAN 各批条目 + `.codely-cli/HANDOFF-LAN交接.md`。

## 0. 一页结论

症状「手机加入房间显示玩家1、点准备/切队无反应」= 手机客户端上唯一场景网络物体 **NetworkEventBus（NEB）永远未激活** → `SendToHost → NetworkBehaviour.isServer → netIdentity null → NRE`。NEB 激活依赖一整条链，链上有**七个独立断点**，且互相掩盖：

| # | 断点 | 批次 | 修法 |
|---|---|---|---|
| 1 | Telepathy 事件重放死循环（OnConnected 链异常→TryDequeue 跳过→每帧重放） | 第八批 | 六事件转发包 try/catch（Mirror 内嵌补丁） |
| 2 | NEB 构建期失活属 Mirror 设计（NetworkScenePostProcess）曾误判为「构建毒」 | 第九/十批 | 判死「毒包」理论，回归设计行为 |
| 3 | 无玩家对象 → SetClientReady 的 identity 门跳过场景物体 replay | 第十一/十三批 | **占位玩家对象**（正道，见 §2.6） |
| 4 | Mirror static `NetworkClient.ready` 跨会话残留 → 就绪上报被吞 | 第十一批 | OnClientConnect base 前强制清零 |
| 5 | 编辑器 vs 构建端 sceneId 哈希失配（编辑器 Play 不打路径哈希） | 第十二批 | 编辑器 Awake 补打同一哈希 |
| 6 | 安全网抢跑：RebuildObservers 直发无 ObjectSpawnStarted 前置包→字典未建即查找失败；且 AddObserver 抢注→原生 replay 不再重发 | 第十四批 | **删除 OnServerReady 安全网**（与断点 3 的修复互相踩踏，终案移除） |
| 7 | Detecting Android SDK CDN 间歇停摆（构建侧，非运行时） | 构建插曲×2 | 杀 java→重触发（热缓存 ~90s） |

**核心教训**：六个运行时断点里，第十一批「手动补发」与第十三批「占位玩家」各自正确、叠加致命——**修复 Mirror 结构性缺口时应直接走框架正道（玩家对象），而非在旁路上堆补丁**；补丁与正道叠加时，旁路的副作用（抢跑+抢注幂等）会把正道也堵死。

## 1. 系统背景（读懂本案需要的前置）

### 1.1 GIC 的网络架构

- **传输**：Telepathy TCP（Mirror 内建；2026-10-13 从 kcp 切换，docs/14 §151）端口 30921；发现层 UDP 47777。
- **事件总线**：房间/大厅事件走 `EventBusHub → NetworkEventBus（NEB）→ Mirror NetworkEventMessage`；战斗消息走 `MirrorBattleTransport` 独立通道（不走 EventBusHub，铁律）。
- **NEB 是 Boot 场景里 NetworkManager 的子物体**，挂 NetworkIdentity——全项目唯一的场景网络物体（其余战斗单位走快照+命令流，不是 NetworkIdentity）。
- **双向不对称**（本案最重要线索）：host→手机广播走 `conn.Send`（不经 NEB）→ 一直正常；手机→host 必须走 `NEB.SendToHost`（isServer 判断需 netIdentity）→ NEB 死则全死。
- **无玩家对象**（autoCreatePlayer=0，2026-10-15 改 1）：本作战棋无「玩家棋子」概念——这个「非标准」形态正是断点 3/6 的土壤。

### 1.2 Mirror 场景物体的生命周期（本案的机制核心）

```
构建期（BuildPipeline / 编辑器进 Play）:
  NetworkScenePostProcess [PostProcessScene]
    → SetSceneIdSceneHashPartInternal()   # sceneId 高 32 位 = 场景路径哈希
    → gameObject.SetActive(false)         # 场景网络物体强制失活（设计行为）
运行时（host）:
  StartHost → SpawnObjects → 失活场景物体复活 + Spawn 进 spawned 字典
运行时（纯客户端）:
  Ready() → ReadyMessage → 服务端 replay
    → ObjectSpawnStarted → 客户端 PrepareToSpawnSceneObjects（建 spawnableObjects 字典）
    → 逐物体 SpawnMessage → SpawnSceneObject(sceneId) → 字典查到 → SetActive(true) 激活
    → ObjectSpawnFinished
  物体激活 → NetworkBehaviour.OnStartClient → 我方名字上报（NEB.OnStartClient → OnNetworkSceneObjectsReady）
```

任何一环断掉，客户端 NEB 永远失活 → `netIdentity == null` → 一切 `SendToHost` NRE。

## 2. 十六批演进史（每批：症状 → 证据 → 修复 → 验证）

### 2.1 第八批之前：kcp 时代的四个断点（前情，docs/14 §151）

手机做 TCP 服务端可被加入、笔记本→手机方向 UDP 回包被 vivo OriginOS 状态型中间盒拦截（「魔法序列」实锤）等 → 切 Telepathy TCP。

### 2.2 第八批：Telepathy 事件重放死循环

- **症状**：手机卡「正在加入」40 秒，OnClientConnect 每帧 17ms 匀速触发（lan_debug 死亡指纹）。
- **根因**：Telepathy Client.Tick 三段式 Peek→Invoke→TryDequeue；OnConnected 处理链抛异常 → TryDequeue 跳过 → 同一事件每帧重放。
- **修法**：TelepathyTransport 六个事件转发全部包 try/catch（`SafeClientHookInvoke`，Mirror 内嵌补丁）；`LanDebugFileLog.InstallGlobalHook`（Application.logMessageReceived 抓 Error/Exception 落盘——vivo 锁死 logcat，文件日志是手机端唯一取证通道）；15s 加入超时保险。

### 2.3 第九/十批：「构建毒」理论大弯路

- **症状**：APK 内 NEB GO `m_IsActive=0x00` → 一度判定「构建管线被污染」（AssetDB/bee 缓存/il2cpp 增量状态三层排查，删双库全冷重建）。
- **勘正**：`m_IsActive=0` 是 **Mirror 的设计行为**——`NetworkScenePostProcess` 对一切场景 NetworkIdentity 物体 SetActive(false)（「spawnable scene objects are force disabled until actually spawned」）。当时真正未修的 bug 是客户端缺 `NetworkClient.Ready()`+缺 Ready 后的处理链。
- **教训**：字节级取证（UnityPy 解 APK level0）只有对上**框架设计语义**才能得出正确结论；单看字节位会构造出华丽但错误的理论。

### 2.4 第十一批（上）：stale ready 残留

- **症状**：`NetworkClient is already ready. It shouldn't be called twice.`——且**冷启动复测仍有**（曾误判杀 App 可解，被用户复测证伪）。
- **根因**：Mirror 的 `public static bool ready` 跨会话残留（同进程当过 host 后再 join、断线重连均不清零）→ base.OnClientConnect 内 `if (!ready) Ready()` 跳过 → ReadyMessage 从未发出。
- **修法**：OnClientConnect 在 base 之前 `if (!NetworkServer.active && NetworkClient.ready) NetworkClient.ready = false;`。
- **同批勘正**：就绪上报本来就由 base.OnClientConnect 完成（无 onlineScene → clientLoadedScene=false → base 自调 Ready）——第九批手写的显式 `NetworkClient.Ready()` 是重复调用（报 already ready 噪音），已移除。

### 2.5 第十一批（下）：无玩家对象的 identity 门

- **症状**：ReadyMessage 发出后手机依然收不到 NEB 的 spawn 消息。
- **根因（代码级）**：`NetworkServer.SetClientReady` 只在 `conn.identity != null` 时才 `SpawnObserversForConnection`——Mirror 假设 replay 按玩家位置建立观察者；无玩家连接永不 replay。
- **当时修法**：覆写 `OnServerReady`：base 后对全体 spawned 调 `NetworkServer.RebuildObservers(ni, true)`（公有 API，AddObserver 幂等）——**此修复正确但埋下断点 6 的种子**。
- **网检印证**：上游 issue #4083 确认 identity 门是设计行为；issue #3540 记录同族问题。

### 2.6 第十三批：占位玩家对象（正道）

- **用户拍板**：「既然缺少玩家，能不能补上玩家？」——正是 Mirror 的设计正道。
- **修法**：`Resources/Prefabs/Network/NetworkPlayer.prefab`（空 GameObject+NetworkIdentity，无渲染无逻辑不可见）+ Boot.unity `playerPrefab`/`autoCreatePlayer=1` + `OnServerAddPlayer` override（base+DontDestroyOnLoad 防切场景悬空）。
- **生效链**：客户端 base.OnClientConnect 自动 Ready+AddPlayer → 服务端 AddPlayerForConnection 赋 conn.identity → SetClientReady 门开 → **原生** SpawnObserversForConnection 以正确时序 replay。

### 2.7 第十二批：编辑器↔构建端 sceneId 哈希失配

- **症状**（01:09 会话日志铁证）：`Spawn scene object not found for B97EAECEC3AF1A49`——spawn 消息已到手机（服务端链全部打通），但客户端字典查无此键。
- **字节级取证**：UnityPy 解 APK level0 → NEB sceneId=**0xB97EAECEC3AF1A49**（低 32=C3AF1A49 静态部分=Boot.unity 序列化值 3283032649；高 32=B97EAECE=路径哈希）；编辑器活体探针 → live NEB sceneId=**0x00000000C3AF1A49**（高 32 位=0）。
- **根因**：sceneId 高 32 位由构建期 [PostProcessScene] 烤入；**本机 Tuanjie 编辑器进 Play 不执行该步** → 编辑器 host 发无哈希键、构建客户端字典带哈希键 → 永不匹配。**解释了「从第一晚起症状恒定不变」**。
- **网检印证**：Mirror issue #2519=同款双报错+「编辑器↔独立端互连必炸」的镜像案例。
- **修法**：MyNetworkManager.Awake 在 `base.Awake()`（DDOL 移场景、scene.path 失效）之前、`#if UNITY_EDITOR` 内对全部 `Utils.IsSceneObject` 的 NI 补打 `SetSceneIdSceneHashPartInternal()`（public 幂等；构建内已烤好正确值勿动）。编辑器 Console 的「编辑器场景哈希补打: NetworkEventBus sceneId=0x...」行=生效铁证。

### 2.8 第十四批（上）：SceneCensus 普查埋点

- 前两批修复后仍失败 → 不再推理，给 Mirror 内嵌加普查：SpawnSceneObject 查找失败瞬间 dump 客户端全部 NetworkIdentity 实况（sceneId/netId/act/hide/scene）+ spawnableObjects 字典全部键值 → lan_debug 落盘。
- **普查结果（01:22 会话，一锤定音）**：`spawnableObjects.Count=0`，而 NEB 明明在内存且全部过滤条件通过——**不是过滤问题，是时序**。

### 2.9 第十四批（终案）：安全网抢跑——两个修复互相踩踏

- **根因链**：`OnServerReady` 安全网（第十一批）的 `RebuildObservers→AddObserver→ShowForConnection` **抢在原生 replay 之前直发** NEB（不带 ObjectSpawnStarted 前置包）→ 客户端字典未建立 → 查找失败；且 AddObserver 已把连接**抢注**进 NEB.observers → 原生 replay（占位玩家触发）循环到 NEB 时**幂等 return 不再重发** → NEB 的 spawn 消息一生只发一次、死在字典建立前 → 永不激活。
- **终案**：整体删除 OnServerReady override，让占位玩家的原生时序独占。**验证通过，完整对战一局。**

## 3. 修复后的最终形态（勿当 bug 修）

- **占位玩家对象**：每个真人连接一个不可见空 NetworkIdentity；DDOL 随局存在；断线自动销毁。名字与名册仍走 PlayerManager 的 connectionId 字典，与玩家对象体系正交。
- **NEB 激活链**：Ready+AddPlayer（base 自动）→ 服务端原生 replay → ObjectSpawnStarted（建字典）→ NEB spawn（键=带哈希 sceneId，必中）→ 激活 → OnStartClient → 名字上报。
- **编辑器哈希补打行**：进 Play Console 必现「编辑器场景哈希补打: NetworkEventBus sceneId=0xB97EAECEC3AF1A49」——**这行消失=修复失效，先查本节**。
- **[SceneCensus] 普查补丁**：保留在 NetworkClient.SpawnSceneObject 内（失败时 dump 全客户端 NI 实况）——任何未来 spawn 类问题直接读此行。
- **Mirror 内嵌补丁清单（gitignored，换机/重装 Mirror 会丢）**：①TelepathyTransport.SafeClientHookInvoke 六事件防重放；②KcpPeer 握手期 dead_link 豁免；③KcpServer IPv4 ConnReset；④Transport.TryBuildValidUri IDN 兜底；⑤NetworkClient.SpawnSceneObject SceneCensus 普查。
- **日志埋点**：`[OnClientConnect] ... ready=X` / `[OnNetworkSceneObjectsReady]`（手机 lan_debug.log；LanDebugFileLog 全局钩子抓全部 Error/Exception 落盘）。

## 4. 方法论沉淀（本案泛化价值）

### 4.1 手机端取证通道（vivo Android 16）

- logcat 对应用日志完全锁死（--pid 也空）；**唯一通道**=`adb pull /sdcard/Android/data/com.HGAME.gic/files/lan_debug.log`（游戏内 LanDebugFileLog 主动落盘）。
- **Debug.LogError 才被全局钩子捕获**（Warning 不捕获）——埋点用 LogError。
- adb 工具链：`.codely-cli/tmp/platform-tools/extract/platform-tools/adb.exe`；`/proc/PID/stat` 第 22 字段+`/proc/uptime` 可算进程真实年龄（判「是否真的冷启动过」）。

### 4.2 判读陷阱（全部实证踩过）

1. **编辑器 -logFile 惰性 flush**：mtime 滞后≠静默——验证日志流活性=主动 Debug.Log 一行落盘再查；别据 mtime 断言「编辑器没收到连接」。
2. **persistentDataPath 混流**：编辑器与 PC 构建同写 `LocalLow\HGAME\gic\lan_debug.log`——多实例混写，判读先分清哪行是谁写的。
3. **PC 构建 Player.log**：GICLog.Info 被 [Conditional] 剥光+惰性 flush——mtime 不可当退出时刻（##utp MemoryLeaks 报告的 time 字段才是）。
4. **编辑器↔构建比对法**：sceneId/资产值要字节级比对（UnityPy 解 APK level0；typetree 读 MonoBehaviour 失败时按「搜低 4 字节小端序+读后 4 位」兜底）；活体值 `ToString("X")` 的位数即判据（8 位=高位 0，16 位=带哈希）。
5. **进程身份三件套**：Time.time（Play 会话年龄）+ WMI CreationDate + Get-NetTCPConnection OwningProcess——「今晚的 host 到底是谁」这类问题先钉死身份再推理。
6. **vivo 划掉后台≠杀进程**。
7. **api.github.com 代理共享出口撞限流时直连反而通**（issue 正文直连拉取实证）。

### 4.3 构建侧配方

- **「Detecting Android SDK」停摆**：sdkmanager（java）直连 dl.google.com 中国 CDN（180.163.x.x）间歇坏边缘——CPU 冻结+连接 Established 停摆即此症。**处置**：杀 java → 构建判 FAIL（Build failure 弹窗）→ 重触发 ScheduleReleaseBuild（脚本/IL2CPP/player data 全热缓存，~90 秒出包）。**勿**永久给 sdkmanager.bat 加 SOCKS 代理（会把构建硬依赖 Clash 常开）。
- **判构建真伪**：APK mtime > 调度时刻 + console 的 `[QuickBuild] RESULT OK` 标记（console 是内存态可靠；构建期间 unity_console 会因主线程忙超时，改用文件侧监控）。
- **判「无玩家缺口」类问题的思路**：对照框架的标准生命周期——**「本作的非常规形态（无玩家对象）撞上框架默认假设」时，首选回归框架正道，而非旁路补丁**。

### 4.4 多批修复互相踩踏的自检

修复 A（旁路补发）与修复 B（正道）解决同一缺口时，验证 A 的副作用清单：抢跑（时序）？抢注（幂等阻断）？状态污染（static 残留）？——本案 2.9 即 A 抢跑+抢注杀死 B 的实证。

## 5. 关联文档

- docs/14 §151（kcp→Telepathy 演进+前三批根因）、§152（本 saga 浓缩节）
- docs/18 决策六十五（复测清单回填记录）
- `.codely-cli/HANDOFF-LAN交接.md`（逐批交接全文，gitignored）
- CODELY.md LAN 各批条目（机器记忆）
