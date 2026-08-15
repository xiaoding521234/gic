

## Codely Structured Memories




### User
- [2026-07-27 14:07:54] User is experienced with Java backend application development (Spring Boot, etc.). When explaining Agent/AI concepts, use Java backend analogies (Controller/Service/Mapper, Spring patterns, etc.) instead of Python/AI-native analogies.

### Feedback

- [2026-08-01 00:22:21] GIC 项目不使用 asmdef（2026-08-01 决定）。尝试过拆分 GIC.Framework/GIC.UI/GIC.Battle/GIC.Editor，但 Framework↔UI↔Battle 间存在大量循环依赖（Framework 引用 UI 的 PopupManager/CardDetailView，Battle 引用 Framework 的 SaveCardData，PlayerManager 引用 Battle 的 Unit/TeamType），硬拆需引入大量接口。**Why:** 单人开发、176 文件、个人 Demo，asmdef 收益不足以抵消重构成本。**How to apply:** 保持单一程序集 + 命名空间（GIC.Framework/GIC.UI/GIC.Battle/GIC.Editor）做逻辑隔离即可，不主动推进 asmdef 拆分。

- [2026-08-02 23:43:50] 角色属性设计规范（2026-08-02）：属性命名参考 HP，不加 Max 前缀。如 baseEnergy（不是 baseMaxEnergy），StatType.Energy（不是 MaxEnergy）。baseEnergy 是基础值=上限，角色登场时当前元能为 0 是运行时逻辑，不在 UnitStats.Init 中处理——Init 只负责把 baseEnergy 设为基础值，和 HP 完全一致。**Why:** 用户指出 Energy 应参考 HP 的设计，HP 的 baseHP 既是基础值也是上限，当前血量在战斗中往下扣；Energy 同理，baseEnergy 既是基础值也是上限，当前元能在战斗中从 0 往上加。**How to apply:** 新增属性时不要加 Max 前缀，Init 中直接 SetBaseValue，运行时的当前值/上下限管理留给战斗系统。
- [2026-08-03 13:26:42] 通用设计规则应写在总设计文档中（如 docs/08-命座系统.md），不应写在个别角色文档里。**Why:** 用户纠正了 AI 试图在芭芭拉.md 中添加 0命概念说明的做法——角色文档只记录该角色的具体数据，通用规则属于总文档。**How to apply:** 涉及多角色的通用规则/概念，写入对应的总设计文档（如命座系统→08、行动系统→05），不要在角色文档中重复。


- [2026-08-09 20:02:41] 祈愿系统 UI 层级在场景中静态预设（WishDrawRoot 下的 CardTrack/CountdownBar/ResultContainer/FinalDisplay），未抽卡时 drawRoot 隐藏。**Why:** 用户要求祈愿 UI 不要代码动态生成，要提前在场景里建好便于编辑器调整布局。**How to apply:** 后续调整祈愿 UI 布局直接在 WishScreen 场景的 Canvas/WishDrawRoot 下改，不要在代码里用 new GameObject 创建。



- [2026-08-09 00:19:47] 技能参数非 Fixed 值是百分比，不是固定值。如"伤害 40 BasedOnAttack"= 40%×攻击力，不是 40 点固定伤害。**Why:** 用户纠正了 AI 评审时把百分比当固定值计算导致数值分析全部错误的问题。**How to apply:** 读取角色文档的技能参数表时，非 Fixed 基准的值一律按百分比理解（代码 SkillParam.GetDisplayValueText 中 value+"%"）。














- [2026-08-09 23:04:14] GIC 文档术语区分（2026-08-09 确定）：**战场**=局内网格战棋地图（20×20~50×50，地形/迷雾/宝箱，文档 03）；**大地图**=局外导航地图（MapScreen 区域切换/锚点传送，文档 13）。**Why:** 之前文档中两种地图都叫"地图"导致混淆。**How to apply:** 写文档或讨论时严格区分，局内用"战场"，局外导航用"大地图"，不要混用"地图"。
- [2026-08-10 14:39:36] 目录名保持英文，不使用中文目录名。**Why:** 项目目录（Assets/docs/Packages 等）全为英文，混入中文目录不一致且可能遇编码问题。**How to apply:** 新建目录时用英文命名，不因中文术语而将目录改为中文。
- [2026-08-10 14:53:26] 角色文档属性规范（2026-08-10）：采用默认值的字段只写 auto，不写具体数值（不写 `auto (→50)`，只写 `auto`）。UnitConfig.asset 中对应填 -64（Unspecified 哨兵）。只有非默认值才写显式数字。**Why:** 用户要求简化文档，-64 运行时会自动派生正确值，无需在文档重复标注。**How to apply:** 编辑 docs/units/{name}.md 时，等于默认值的属性写 auto；UnitConfig.asset 中对应字段填 -64。默认值参考 UnitConfig.GetEffective*() 方法（defense=0, moveSpeed=3, sanity=50, luck=0, tenacity=0, mastery=0, lifesteal=0, healEfficiency=100, energy=10, visionRange=1）。
- [2026-08-14 11:31:36] UI 文本本地化必须统一使用 TextCombiner 组件（Tool/Component/TextCombiner.cs），不要直接用 `tmp.text = LocalizedString.GetLocalizedString()`。**Why:** 用户指出直接赋值在语言切换时不会自动刷新，TextCombiner 才有 StringChanged 事件订阅。**How to apply:** 所有 UI 文本（标题、按钮等）通过 TextCombiner.SetSingleEntry(new LocalizedString(table, key)) 设置；若目标 GameObject 上没有 TextCombiner，运行时用 GetComponent/AddComponent 自动补上；TMP_InputField placeholder 无法挂 TextCombiner，可例外用 GetLocalizedString() 同步解析。
- [2026-08-14 22:58:07] 用户授权 AI 主动维护项目 skill（2026-08-14）：完成阶段性工作或发现高频工作流/陷阱密集的系统时，可自行新增/更新 .codely-cli/skills/ 下的 skill，无需逐次确认。**Why:** 用户明确说"为了帮助你更好的开发本项目，你可以为自己更新修改现有skill或加新的skill"。**How to apply:** 主动沉淀但避免与项目记忆重复——记忆记事实与决策（为何这样做），skill 记操作流程与检查清单（怎么做）；不确定价值时先问。
- [2026-08-14 23:33:38] - [2026-08-14 23:10:00] 批量替换脚本处理日志类文件的自噬陷阱：P2 时替换脚本把 GICLog.cs 自己方法体的 Debug.Log( 也换成 GICLog.Info( 造成自我递归 StackOverflow。**Why:** GICLog.cs 在被扫描目录内，替换目标字符串恰好是其自身实现。**How to apply:** 任何全仓批量文本替换前必须把新写入的封装类文件加入排除列表；替换后要 grep 封装类内部确认实现未被污染（而不是只验证"调用零残留"）。

### Project





- [2026-08-14 10:15:01] "协议核心"是同时回合制卡牌战术战棋（原神IP，最多6人，LAN联机 via Mirror）。核心循环：祈愿解锁→局前选8卡→同时选1行动→攻速排序执行→摧毁核心掠夺→原石结算。7势力可混搭，命座0-3重复出战升命，战败=核心被摧毁→消散+观战。详细设计见 docs/ 目录。**Why:** 追踪演进中的游戏设计。**How to apply:** 实现战斗/势力/经济系统时参考 docs/。


- [2026-07-28 09:14:44] GIC (协议核心 game project) targets全平台互通 (cross-platform: PC + mobile + console). Project name "GIC" derived from directory D:\Tuanjie_editor\gic.
- [2026-07-28 09:16:21] GIC project: solo developer (1人), personal Demo/portfolio goal, NOT commercial launch. Target audience: both Genshin players and strategy/tactical gamers. **Why:** Solo dev with limited resources — scope must be drastically cut from GDD ambition. **How to apply:** Recommend 2-player 1v1 over 6-player, 2-3 factions over 7, hotseat/local before Mirror networking, vertical slice over breadth.


- [2026-08-02 14:07:01] Unit data docs at docs/units/ (moved from docs/unit-data.md on 2026-08-02): Each character has its own MD file (安柏.md, 凯亚.md, etc.). Field reference & template at docs/units/_模板与字段说明.md. AI reads these files to sync UnitConfig.asset / SkillName enum / SkillParamKey enum / localization tables. **Why:** eliminates manual multi-file editing when adding/modifying characters — edit one MD, AI applies changes everywhere. **How to apply:** user edits docs/units/{name}.md, asks AI to apply; AI updates enum + UnitConfig.asset + localization tables per gic-localization skill.


- [2026-08-14 10:15:16] GIC 战斗系统采用 StS 风格架构：ActionQueue（协程串行）+ DamagePipeline（同步，Phase hooks）。EventBusHub/LocalEventBus 仅用于 UI/网络层，战斗逻辑不走 EventBus。**Why:** 回合制=严格串行无并发，StS 验证了此模式。**How to apply:** 实现战斗系统时创建 ActionQueue + DamagePipeline 作为独立层。

- [2026-08-01 21:49:24] GIC 项目使用 ParrelSync 进行编辑器内联机测试（2026-08-01 安装）。位于 Packages/ParrelSync（从 umc 项目复制）。使用方法：菜单 ParrelSync/Preferences/Clone Manager → Clone current project → 两个 Editor 窗口都 Play，一个 Host 一个 Client。Clone 通过 Windows Junction 链接 Assets/Packages/Library，改代码实时同步。
- [2026-08-14 10:15:28] GIC 角色语音数据内嵌在 UnitConfig.UnitData.voices (UnitVoiceData)，不独立 ScriptableObject。复用 AudioManager.PlayVoice 通道。语音文件在 Assets/Resources/Audios/Voices/{UnitName}/，Editor 工具 Tools/UnitConfig/自动加载语音 按文件名前缀匹配自动填充。**How to apply:** 新增角色语音按命名规范放入对应目录，用工具自动加载。


- [2026-08-05 23:39:43] GIC 术语区分：【消散】（带方括号）= 玩家战败时该玩家所有单位从游戏中真正删除，不可恢复；【放逐】= 转移到另一个维度，单位仍然存在，可被特定技能召回（如丽莎爆发蔷薇的雷光可召回被放逐的标记单位并承受真伤惩罚）。Buff/造物过期用"消失"，不用"消散"。丽莎文档中"标记所属单位【消散】"是正确用法（指玩家战败）。凯亚/芭芭拉文档中的"棱消散/环消散"旧措辞应改为"消失"以避免与游戏机制混淆。

















- [2026-08-14 10:15:53] GIC 祈愿系统：WishPoolConfig(ScriptableObject) 定义卡池权重 → WishManager 负责货币+存档 → WishDrawController 控制 UI。祈愿机制=反应速度游戏（卡道滚动，射击命中获卡，不预抽）。重复角色卡转星辉（5★=50/4★=25/3★=15/2★=8/1★=3）。相遇之线：累计星辉每满20触发，下次射击金色逐级升星（独立升级权重）。UI 布局在场景 Canvas/WishDrawRoot 下静态预设。**How to apply:** 新增卡池创建 WishPoolConfig asset 拖到 CharacterEntry.pool；UI 在 WishScreen 场景改；货币显示在 WishScreen 根对象。







- [2026-08-09 11:35:34] GIC 构建配置（ProjectSettings.asset 可直接编辑）：测试用 Mono+ARMv7（快，约3分钟），发布用 IL2CPP+ARM64+Stripping Low（慢首次5-15分钟）。字段：scriptingBackend.Android(0=Mono,1=IL2CPP)、AndroidTargetArchitectures(1=ARMv7,2=ARM64)、managedStrippingLevel.Android(3=Low)。Android APK ~270MB vs Windows ~2GB 是正常的（APK ZIP压缩 + ASTC纹理 vs Windows 未压缩 + DXT纹理）。**Why:** 频繁切测试/发布配置。**How to apply:** 直接编辑 ProjectSettings.asset 改这三个字段，无需 execute_csharp_script（Tuanjie 引擎限制 EditorSettings/GraphicsSettings 类型不可用）。编辑器崩溃看 Editor.log：%USERPROFILE%\AppData\Local\Tuanjie\Editor\Editor.log；Windows 事件查看器查 Tuanjie.exe 异常码。
- [2026-08-10 00:49:23] GIC 快速导出 APK 工具：Tools/导出 APK/ 快速导出(Mono/ARMv7 测试) 或 正式导出(IL2CPP/ARM64 发布)。脚本在 Assets/_Scripts/Editor/Tool/QuickAPKBuilder.cs。**重要：构建前必须确保编辑器在 Android 平台**（脚本会自动切换）。**Why:** 不切换平台时 Addressables 会按 Windows 平台打包 bundle（DXT 纹理/Windows 路径），导致 Android 上黑屏+无声音+体积大 200MB（2026-08-10 排查）。脚本构建后不切回原平台，避免第二次纹理重导入。**How to apply:** 日常测试用"快速导出"，发布用"正式导出"。ADB 路径：D:\Tool\2022.3.62t11\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe。


- [2026-08-14 10:15:33] GIC 资源优化：BGM 用 Streaming loadType；Release 包抑制 Debug.Log；6 个 Sprite Atlas（UICommon/Icons/Avatars/NameCards/MapUI/WishUI，Assets/SpriteAtlases/）。Tuanjie SpriteAtlas API 陷阱：`atlas.Add(objs)` / `atlas.SetIncludeInBuild(true)` 是方法不是属性（在 UnityEditor.U2D.SpriteAtlasExtensions）。**How to apply:** 新增小 UI 贴图加入对应 Atlas；大贴图不入 Atlas。


- [2026-08-14 10:16:06] GIC 大资源加载：大贴图/背景/立绘通过 Addressables 按需加载（Assets/Art/ 下），Configs 和小 Prefabs 留在 Resources.Load。所有 Addressables 加载/释放统一走 AssetCache（Framework/AssetCache/，[Component] 注册到 DI），管理引用计数/去重/帧预算/优先级队列/Preload，不直接调用 Addressables.LoadAssetAsync。**How to apply:** 新增大贴图放入 Assets/Art/ 对应目录并标记 Addressable；代码中用 AssetCache.LoadAsync/Release；常驻资源用 Preload。




- [2026-08-14 10:15:42] GIC 地图系统配置驱动（非 Prefab）。MapConfig.asset（Resources/Configs/）存储各区域位置/尺寸+锚点列表（归一化坐标 0-1）。场景结构：MapScreen Canvas > MapRoot > Scroll View > Viewport > Content > MapImage + Anchors。**How to apply:** 新增/修改区域锚点在 MapConfig.asset Inspector 中编辑。

- [2026-08-14 12:42:59] GIC DI 架构采用 Spring Boot 风格 IoC 容器。ApplicationContext（Framework/DI/）= Spring ApplicationContext。纯 C# [Component] 类用构造器注入，MonoBehaviour 类用 [Autowired] 字段注入（Start/Awake 调 Context.Inject(this)）。[Configuration]+[Bean] 产出配置对象。Wargame.Context 获取容器，Context.Get<T>()=getBean。**Why:** 用户要求高度模仿 Spring Boot。**How to apply:** 新增纯 C# Manager 加 [Component]+构造器参数（在 Wargame.Init 按依赖顺序 Register）；配置产出加 [Configuration]+[Bean]；MonoBehaviour 加 [Autowired]+Context.Inject(this)；不要用 Wargame.Instance.XxxManager 取依赖。**IWargameManager 初始化陷阱**：`Wargame.Start()` 只调 `Context.PostConstruct()`（反射调 `[PostConstruct]` 方法），`IWargameManager.Start()` **从未被调用**。Manager 初始化逻辑必须放在 `[PostConstruct] public void Init()` 里，`Start()` 留空。2026-08-14 因 PlayerManager 初始化在 Start() 中导致事件处理器未注册，准备按钮失效。
- [2026-08-14 12:43:04] GIC 网络请求事件（ToggleReadyRequestEvent/SetTeamRequestEvent/SetColorRequestEvent/SetSpawnRequestEvent/KickPlayerRequestEvent/SetPlayerNameRequestEvent）必须设 `Immediate = true`。**Why:** Host 模式下 SendToHost → ReceiveNetworkEvent 将事件标记 Source=Network 入 LocalEventBus 队列，若 Immediate=false 则依赖 FixedUpdate.Tick() 逐帧处理，实测在 CoopScreen 场景中队列未被处理（可能被阻塞或竞态）。设 Immediate=true 走 ExecuteImmediate 同步执行，跳过队列。**How to apply:** 客户端→服务器的请求类事件（EventType.OnlyHost）一律 Immediate=true。
- [2026-08-14 13:30:20] GIC 祈愿系统于 2026-08-14 重构为状态机 + 预计算架构。WishFlowController（纯C#，State: Idle/Drawing/Revealing/Finished）的 PlanShot() 一次性预计算全部业务逻辑（Roll/Upgrade/AwardStarglitter/AddToInventory），返回 WishShotResult（含有序 WishRevealStep 列表）。WishDrawController.PlayStepsCoroutine() 按序播放动画，零业务调用。**Why:** 旧代码 6 个 bool 标志位管理流程时序，3 个 bug 全源于业务逻辑与动画协程交错。**How to apply:** 新增祈愿动画步骤时，在 WishFlowController.PlanShot 中预计算结果并加入 steps 列表，在 PlayStepsCoroutine 的 switch 中添加对应 case；不要在表现层调用 WishManager 方法。
- [2026-08-14 22:19:10] GIC 输入系统于 2026-08-14 三次重构为 (owner,reason) 精确配对锁。InputManager（[Component]，注入 SaveManager）核心：① Dictionary<KeyAction, List<KeyCode>> 绑定表（Update 轮询→OnActionTriggered 事件+内置动作注册表 Dictionary<KeyAction,Action>，新增内置动作用 RegisterBuiltinHandler，不改派发循环）② List<InputLockEntry(owner,reason)> 输入锁：Push 同(owner,reason)去重（协程重入无需补pop）、Pop 按(owner,reason)幂等移除（与栈顶无关）、PopAllInputLocks(owner) 在 OnDestroy 兜底（清到泄漏锁 LogWarning）、HasInputLock(reason) 精确查询 ③ List<IClosable> 可关闭 UI 栈（CloseTopUI 无 GoBack fallback）。调用点用静态门面 InputLocks.Push/Pop/PopAll(this, InputLockReason.Xxx)（InputLockReason 常量类在 Framework/InputLocks.cs）。KeyAction 枚举：CloseUI（ESC/鼠标右键）、Confirm（Space/Enter）。KeyBindingSettingItem：static KeyCode[] 缓存+anyKeyDown 前置门（避免每帧 Enum.GetValues 分配）、按键名本地化 UIText KeyName_* 19 key（GetKeyNameTableKey 映射，未映射回退枚举名）。重绑定陷阱保留：①点击后 SetSelectedGameObject(null) ②_skipFrame 跳帧 ③统一可绑定，再点取消。绑定存 PlayerSaveData.keyBindings。本次修复的真实泄漏 bug：CoopScreen.OnLeaveRoomClick push Closing 后无 pop（已删多余 push）、WishDrawController.StartWish 原石不足早退不 pop（push 移到扣费后）。IsTransitioning/LoadSceneWithConfig 守卫=HasInputLock(SceneTransition)（不再被入场动画等误触发）。
- [2026-08-14 23:06:01] - [2026-08-14 23:29:00] 代码优化系列工程启动：docs/16-代码优化计划.md 为总纲（P1止血→P2日志→P3 DI统一(a/b)→P4 EventBus→P5 GameScene→P6 PlayerManager→P7 ScreenBase→P8 Settings拆分→P9 Coop拆分→P10 Skill/Card视图解耦→P11 Editor基类→P12 Tool整理），战斗核心(ActionQueue/DamagePipeline/Buff)不在计划内。**Why:** 用户要求分批优化全部代码防返工，每次对话只做一个批次。**How to apply:** 每次对话先读计划文档取下一批次执行，完成后勾选状态；顺序尽量不乱（依赖关系）。摸底数据：204文件/31400行，UI 9980/Framework 7980/Data 4590/Editor 4105/Battle 2692/Tool 2099。
- [2026-08-14 23:10:40] - [2026-08-14 23:12:00] GIC 存档删档测试模式已安全化：开关在菜单 Tools/存档/删档测试模式（EditorPrefs "GIC.SaveManager.DeletionTestMode"，勾选状态持久化），打包版本恒为关闭。**Why:** 原 IS_DELETION_TEST_MODE=true 常量上线忘改会清空玩家存档。**How to apply:** 开发迭代需要每次启动删档时，编辑器里勾选该菜单；测试存档持久性时取消勾选。
- [2026-08-14 23:10:40] - [2026-08-14 23:12:30] GIC 存档库存变更统一入口：PlayerSaveData.AddOwnedUnit(card)/AddOwnedItem(card)（内部自动 RebuildOwnedCards 失效 ownedCards 缓存）。**Why:** 2026-08-14 修复 WishManager.AddStarglitter 首次新建星辉卡直接 ownedNormalItems.Add 漏调重建导致背包缓存过期的真 bug。**How to apply:** 新代码往 ownedUnits/ownedNormalItems 添加卡牌一律走这两个方法，不要直接 List.Add。
- [2026-08-14 23:48:57] GIC DI 注入模式规范（P3a/P3b 完成，全仓 Wargame.Instance.XxxManager 直达已清零，兼容属性已删）：①纯C#类构造注入 ②MonoBehaviour 场景单例（AudioManager/EventBusHub/NetworkEventBus）用**懒注入**——[Autowired] 字段+属性 getter 里 `if(null) Wargame.Instance?.Context?.Inject(this)`（因与 GameScene.Awake 顺序无保证）③运行时实例化的 prefab（Card/PopupDialog/InputPopupDialog）Awake 注入+使用点幂等补注入 ④prefab 上的设置项（KeyBindingSettingItem）懒获取注入（IM 属性模式）⑤partial class（WishScreenDraw）注入字段放 partial 内即可，注入调用放主文件 Awake ⑥静态门面（InputLocks）用后端注册制 ⑦数据对象静态入口（CardConfigResolver.Instance）⑧GameScene 是组合根，保留 Init/Start/Update 引导+Context 属性+自身 [Autowired]。**Why:** P3 确立的分级方案。**How to apply:** 新代码按此分级选模式；manager 级 Wargame.Instance.Xxx 直达禁止；Singleton<T> 基类仍被 Wargame 继承（删除属可选低收益清理）。
- [2026-08-15 00:06:33] GIC 祈愿立绘 Addressables 约定：4K 立绘放 Assets/Art/Wish/ 并标记 Addressable（WishArt 组），地址 = `WishArt/{unitName小写}`，CharacterPanelController 按此地址 AssetCache.LoadAsync。7 个卡池=7 张立绘（columbina/venti/zhongli/mavuika/ei/furina/nahida）全部已入库。**2026-08-15 修复 P3b 回归**：WishScreen 场景 7 个面板 prefab 实例中 6 个保存为未激活（activeSelf=False），Awake 不运行→[Autowired] 未注入→`_assetCache?.LoadAsync` 静默空转但 _currentAddress 已设置，FadeIn 永等 _spriteLoadDone→无立绘（唯哥伦比娅面板激活正常）。修复=AssetCacheRef 懒注入属性。**教训：场景中未激活的 MonoBehaviour 其 Awake 不运行，注入字段在激活前被外部调用必须用懒注入（服务定位器兜底）。How to apply:** 新增祈愿角色立绘按此路径+地址标记；改此类组件时检查激活时序。
- [2026-08-15 00:19:46] - [2026-08-15 00:19:30] GIC 场景系统现状（P5 后）：GameScene 仅三条存活路径——LoadSceneWithConfig（Single 场景）、PreloadScene+ActivatePreloadedScene（Additive 预加载，MainHallScreen.ExitToSceneAsync 用）、GoBack（历史栈返回，只记 Additive）。弹窗/轻提示统一走 PopupManager.Instance（静态入口，Awake 注册）；显示设置应用走 SettingsApplier.ApplyFromSave（静态类）。**Why:** P5 删除了 LoadNewRootScene/UnloadSceneAsync 全链/OnSceneWillUnloadEvent 等死路径（零调用者）。**How to apply:** 新场景跳转只用这三条路径；弹窗用 PopupManager.Instance.ShowToast/ShowModalPopup，不要走 GameScene。
- [2026-08-15 09:37:21] GIC 联机玩家管理层（P6 后，2026-08-15）：PlayerManager=纯数据层（名册 _allPlayers+SelfPlayerID+Register/Set 系列+查询，[Component] 无构造依赖，不再实现 IWargameManager）；RoomManager=流程层（[Component] 构造注入 SaveManager+PlayerManager：HandleServerConnect/Disconnect、11 个网络 Handler、GetNextAvailableColor 私有、OnKickedFromRoom、Cleanup）。事件归属：名册事件 OnPlayerCountChanged/OnPlayerInfoUpdated 在 PlayerManager，被踢事件在 RoomManager。**Why:** 拆解 god class，数据层供 EventBusHub/NetworkEventBus/Discovery 直接依赖而无需知道"房间"概念；单向依赖 RoomManager→PlayerManager。**How to apply:** 新增联机房间流程/网络请求 Handler 放 RoomManager；玩家数据字段/查询放 PlayerManager；MyNetworkManager 与 CoopScreen 注入双 Manager。Handler 常驻订阅（CanHandle 有 NetworkServer.active/Source==Network 守卫），断线不退订——2026-08-15 修复过 OnClientDisconnect 误调 Cleanup 导致同会话重连名册失联的 bug；RoomManager.Cleanup 仅作应用关闭钩子勿在断线时调用。IsOwnUnit/CanControlUnit 已删（零调用），战斗落地时在 Battle 层重建（UnitIdentity 已有 IsEnemy/IsAlly）。
- [2026-08-15 09:55:08] GIC Screen 层（P7 后，2026-08-15）：6 个弹层 Screen 全部继承 UI/Screen/ScreenBase（abstract，实现 IClosable）——Map/Backpack(partial×5)/Settings/Splash/Wish(partial×2)/Coop；MainHall 根场景不继承。基类提供：virtual Awake 注入（子类重写须 protected override + base.Awake()）、RegisterClosableSelf()、音乐幂等 Safe 系列、CloseScreen(Func<IEnumerator>) 标准关闭模板（防重入 isClosing + Closing 锁 + 音乐恢复 + 退场协程 + GoBack 收尾，退场协程内不要再 Pop/GoBack）、OnDestroy 四件套（UnregisterClosable+PopAll+UnsubscribeOwner+音乐 pop）。**Why:** 消除 6 屏重复样板；修复地图传送路径 BGM 音量永久压低与强卸载音乐泄漏两个真 bug。**How to apply:** 新弹层 Screen 按更新后的 gic-new-screen skill 骨架写（继承 ScreenBase，勿直接调 AudioManager Push/Pop——绕过标志位失去幂等）；EventBus 订阅用 owner 登记（基类兜底退订）；C# 事件（如 CoopScreen Bind/Unbind）仍手工配对。货币/物品计数统一走 PlayerSaveData.GetItemCount/AddItemCount/TryConsumeItem（P1 AddOwnedItem 同款数据层入口，勿再手写 ownedNormalItems 遍历）。场景跳转唯一入口 SceneType.Load()（P5 核实无散点）。
- [2026-08-15 10:24:03] GIC 两大工具级陷阱（2026-08-15 P7 自测发现并修复）：①.NET 反射 GetFields(NonPublic|Instance) 不返回基类 private 字段——ApplicationContext.Inject/Validate 已改 GetAutowiredFields 沿 BaseType 链 DeclaredOnly 扫描；基类放 [Autowired] 字段现在可正常注入（症状曾为全 Screen ESC/右键失灵）。②TextCombiner.ApplyFont 曾无条件覆盖 fontMaterial——现仅在字体真正变更时切换，场景材质变体（描边，如 zh-cn SDF.mat 黑0.15 / zh-cn SDF 1.mat 白0.08，用于祈愿面板名称/称号）得以保留。**Why:** 两个静默失效 bug 均由 P7 基类化/更早 P1 的 EnsureTextCombiner 触发暴露。**How to apply:** 新增基类 [Autowired] 字段无需特殊处理（容器已逐层扫描）；需要描边文本=同字体+变体材质；TextMesh Pro/ 目录 git 忽略（材质改动不入库，重装需备份），排查该目录时 rg 需 --no-ignore。详见 docs/14 第 6 节。

### Reference
- [2026-07-28 16:17:53] Localization CSV tool at Tools/Localization/CSV 导出导入 (created 2026-07-28). Exports/imports all 21 TableName localization tables to/from CSV ({TableName}.csv). CSV format: Key,Id,zh-Hans,zh-TW,en,ja,ru. RFC 4180 compliant (supports commas/quotes/newlines in values). Smart matching: Id first, then Key, then create new. **Why:** Unity Localization YAML is unicode-escaped and hard to batch-edit. **How to apply:** use this tool instead of Unity Localization Window for bulk edits; CSV folder defaults to Export/Localization.
- [2026-08-14 10:16:21] MC mod gichess（旧项目，Java/NeoForge）：源码 D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\（308文件），jar D:\Picture\gichess\my\wg-0.2.d。~20+角色，7元素18反应，蒙德延奏/纳塔夜魂已实现。**Why:** GIC 战斗系统 Unity 移植的架构参考。**How to apply:** 需要查旧 Java 实现时按路径阅读源码。


- [2026-08-10 09:28:39] 技术陷阱与 Bug 修复记录：详见 docs/14-技术陷阱与Bug修复记录.md（场景闪烁修复、Unity UI 陷阱、导出/构建陷阱、ParticleSystem 陷阱、Localization 陷阱）。
- [2026-08-14 10:48:22] GIC 本地化表编程操作 API（Tuanjie 引擎 Unity Localization）：获取集合 `LocalizationEditorSettings.GetStringTableCollections().FirstOrDefault(c => c.name == "UIText")`；添加 key 用 `sharedData.AddKey("Key")` 返回 SharedTableEntry（不是 AddEntry）；遍历 locale 表用 `collection.Tables` 中 `tableRef.asset as StringTable`，locale code 用 `st.LocaleIdentifier.Code`（不是 `locale.identifier`）；设值用 `table.AddEntry(entryId, value)` 新增或 `table.GetEntry(id).Value = value` 更新；保存用 `EditorUtility.SetDirty(sharedData)` + `AssetDatabase.SaveAssets()`。**How to apply:** 需编程批量操作本地化表时用此 API，避免反复试错（Locale API 与标准 Unity 略有不同）。

- [2026-08-14 22:58:07] - GIC 项目 skill 清单（.codely-cli/skills/）：gic-new-unit（角色文档→UnitConfig/本地化）、gic-localization（本地化 CSV + Agent 脚本直改表路径；GUI 工具完成时弹模态框会阻塞 bridge，Agent 用 execute_csharp_script 直改+同步重导出）、gic-input-system（输入锁/IClosable/按键绑定，指向 docs/15-输入系统.md）、gic-new-screen（新建 Screen/场景：SceneType 注册/场景文件陷阱 unity_scene save 会存错路径/BuildSettings 漏加真机黑屏/骨架代码/音频API速查）、gic-save-system（PlayerSaveData 字段初始化器/版本迁移/JsonUtility 限制；现状 CURRENT_SAVE_VERSION=1 但 UpgradeFromV1ToV2 已预留未启用）、git-commit-helper。其余系统由项目记忆覆盖（AssetCache/EventBus/DI/祈愿/大地图/APK 导出），未单独建 skill；战斗系统 ActionQueue+DamagePipeline 尚未实现，落地后再建 skill。Android 包名 com.HGAME.gic（ProjectSettings expectedBundleIdentifier），编辑器切平台可能被重置为默认值，提交前核对。

