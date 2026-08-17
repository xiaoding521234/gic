

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
- [2026-08-16 11:16:48] [feedback] unity_asset delete 对含特殊字符文件名的资产会误报成功/失败（2026-08-16 实测：Assets 根 CUsers* 垃圾文件名含私有区字符 U+F03A 替代冒号——某工具把 `C:\Users\86139\skills.json` 当相对路径写入的产物；create_batch 报 deleted successfully 但文件仍在，单文件 delete 也在另一目录上报错误实际成功）。**Why:** Unity AssetDatabase 按 path 字符串解析，U+F03A 显示为冒号但路径匹配失败。**How to apply:** 删除后必须用 Get-ChildItem -Name 独立核验；遇到"按显示名路径访问不到但目录列表存在"的文件，用 `$_.Name.ToCharArray() | % { '{0:X4}' -f [int]$_ }` 查隐藏字符，再管道 Get-ChildItem | Remove-Item -Force 用 FileInfo 对象删除（别拼路径字符串）。
- [2026-08-17 12:20:10] [2026-08-17] Tuanjie UITK 编辑器窗口排版规范补充（用户反馈标定面板重叠后总结）：①内容多的窗口（多模式下拉/长说明）整面包 ScrollView + 设 minSize，防窗口不够高时控件挤压重叠；②DropdownField/EnumField 标签保持极短（1-2 字符，如 A/B），中文长标签在 SDF 游戏字体度量下横向溢出会压到右侧字段值——含义放上方说明 Label；③EditorWindow 的 BuildUI 必须幂等（开头 root.Clear()），任何"重建界面"调用点（保存后/模式切换后）都会叠加一份 UI（重影 bug 实修）。**Why:** 用户连续遇到重影与重叠两轮排版问题。**How to apply:** 新写/改 UITK 编辑器窗口时按此三条检查；已写入 gic-editor-tool skill 范畴但此条为版式经验，与 skill 陷阱表互补。
- [2026-08-17 21:15:03] [2026-08-17 21:30] [feedback] git checkout 回滚 Unity 资产后必须强制重载编辑器内已打开的场景（EditorSceneManager.OpenScene 同路径 Single），否则磁盘已还原但 SceneView 仍是旧画面——2026-08-17 用户在"看似已回滚实则场景未重载"的错位地图上做标定自测，点击内容全错，解出 unit=0.0059（图缩至 30%），二次返工。**Why:** OpenScene 才丢弃内存态；git 只动磁盘。**How to apply:** 任何对"编辑器中已打开场景文件"的 git 恢复操作，收尾必须 OpenScene 重载+断言现场（MapPlane pos/scale）。另：标定自测语义=点所选 A/B 锚点的绿圈中心应得恒等解（unit=0.02 图不动），不是点城市；点哪座城下拉必须选那座城的锚点，璃月当前无锚点。

### Project





- [2026-08-14 10:15:01] "协议核心"是同时回合制卡牌战术战棋（原神IP，最多6人，LAN联机 via Mirror）。核心循环：祈愿解锁→局前选8卡→同时选1行动→攻速排序执行→摧毁核心掠夺→原石结算。7势力可混搭，命座0-3重复出战升命，战败=核心被摧毁→消散+观战。详细设计见 docs/ 目录。**Why:** 追踪演进中的游戏设计。**How to apply:** 实现战斗/势力/经济系统时参考 docs/。


- [2026-08-16 19:13:23] GIC（协议核心）目标全平台互通（PC + 移动 + 主机）。

- [2026-07-28 09:16:21] GIC project: solo developer (1人), personal Demo/portfolio goal, NOT commercial launch. Target audience: both Genshin players and strategy/tactical gamers. **Why:** Solo dev with limited resources — scope must be drastically cut from GDD ambition. **How to apply:** Recommend 2-player 1v1 over 6-player, 2-3 factions over 7, hotseat/local before Mirror networking, vertical slice over breadth.


- [2026-08-02 14:07:01] Unit data docs at docs/units/ (moved from docs/unit-data.md on 2026-08-02): Each character has its own MD file (安柏.md, 凯亚.md, etc.). Field reference & template at docs/units/_模板与字段说明.md. AI reads these files to sync UnitConfig.asset / SkillName enum / SkillParamKey enum / localization tables. **Why:** eliminates manual multi-file editing when adding/modifying characters — edit one MD, AI applies changes everywhere. **How to apply:** user edits docs/units/{name}.md, asks AI to apply; AI updates enum + UnitConfig.asset + localization tables per gic-localization skill.


- [2026-08-16 19:13:28] GIC 战斗系统采用 StS 风格架构：ActionQueue（协程串行）+ DamagePipeline（同步，Phase hooks）。EventBusHub/LocalEventBus 仅用于 UI/网络层，战斗逻辑不走 EventBus。**Why:** 回合制=严格串行无并发，StS 验证了此模式。**How to apply:** 战斗批次 B1-B4 开工前先读 docs/18-战斗系统决策记录.md（2026-08-16 已拍板 Host 权威等决策）与 docs/16 末节批次表。


- [2026-08-01 21:49:24] GIC 项目使用 ParrelSync 进行编辑器内联机测试（2026-08-01 安装）。位于 Packages/ParrelSync（从 umc 项目复制）。使用方法：菜单 ParrelSync/Preferences/Clone Manager → Clone current project → 两个 Editor 窗口都 Play，一个 Host 一个 Client。Clone 通过 Windows Junction 链接 Assets/Packages/Library，改代码实时同步。
- [2026-08-14 10:15:28] GIC 角色语音数据内嵌在 UnitConfig.UnitData.voices (UnitVoiceData)，不独立 ScriptableObject。复用 AudioManager.PlayVoice 通道。语音文件在 Assets/Resources/Audios/Voices/{UnitName}/，Editor 工具 Tools/UnitConfig/自动加载语音 按文件名前缀匹配自动填充。**How to apply:** 新增角色语音按命名规范放入对应目录，用工具自动加载。


- [2026-08-15 21:43:55] GIC 术语区分：【消散】（带方括号）= 玩家战败时该玩家所有单位从游戏中真正删除，不可恢复；【放逐】= 转移到另一个维度，单位仍然存在，可被特定技能召回（如丽莎爆发蔷薇的雷光）。Buff/造物过期用"消失"，不用"消散"。**Why:** 消散是核心战败机制，措辞混用会造成设计歧义（旧文档中曾混用）。**How to apply:** 写角色技能/设计文档时严格按此区分。


















- [2026-08-15 21:42:58] GIC 祈愿系统（2026-08-14 状态机重构后）：WishPoolConfig(SO) 定义卡池权重 → WishManager 货币+存档 → WishFlowController（纯C#，State: Idle/Drawing/Revealing/Finished）PlanShot() 一次性预计算全部业务（Roll/升星/星辉/入库）返回 WishShotResult（有序 WishRevealStep 列表）→ WishDrawController.PlayStepsCoroutine() 纯动画播放零业务调用。机制：反应速度游戏（卡道滚动射击命中获卡，不预抽）；重复卡转星辉（5★=50/4★=25/3★=15/2★=8/1★=3）；相遇之线=累计星辉每满20下次射击金色逐级升星。UI 层级在 WishScreen 场景 Canvas/WishDrawRoot 下静态预设（未抽卡时隐藏）。**Why:** 旧代码 6 个 bool 标志位管理时序，3 个 bug 全源于业务逻辑与动画协程交错。**How to apply:** 新增卡池建 WishPoolConfig asset 拖到 CharacterEntry.pool；新增动画步骤在 PlanShot 预计算加 steps + PlayStepsCoroutine 加 case，不要在表现层调 WishManager；UI 布局在场景里改，货币显示在 WishScreen 根对象。








- [2026-08-09 11:35:34] GIC 构建配置（ProjectSettings.asset 可直接编辑）：测试用 Mono+ARMv7（快，约3分钟），发布用 IL2CPP+ARM64+Stripping Low（慢首次5-15分钟）。字段：scriptingBackend.Android(0=Mono,1=IL2CPP)、AndroidTargetArchitectures(1=ARMv7,2=ARM64)、managedStrippingLevel.Android(3=Low)。Android APK ~270MB vs Windows ~2GB 是正常的（APK ZIP压缩 + ASTC纹理 vs Windows 未压缩 + DXT纹理）。**Why:** 频繁切测试/发布配置。**How to apply:** 直接编辑 ProjectSettings.asset 改这三个字段，无需 execute_csharp_script（Tuanjie 引擎限制 EditorSettings/GraphicsSettings 类型不可用）。编辑器崩溃看 Editor.log：%USERPROFILE%\AppData\Local\Tuanjie\Editor\Editor.log；Windows 事件查看器查 Tuanjie.exe 异常码。
- [2026-08-10 00:49:23] GIC 快速导出 APK 工具：Tools/导出 APK/ 快速导出(Mono/ARMv7 测试) 或 正式导出(IL2CPP/ARM64 发布)。脚本在 Assets/_Scripts/Editor/Tool/QuickAPKBuilder.cs。**重要：构建前必须确保编辑器在 Android 平台**（脚本会自动切换）。**Why:** 不切换平台时 Addressables 会按 Windows 平台打包 bundle（DXT 纹理/Windows 路径），导致 Android 上黑屏+无声音+体积大 200MB（2026-08-10 排查）。脚本构建后不切回原平台，避免第二次纹理重导入。**How to apply:** 日常测试用"快速导出"，发布用"正式导出"。ADB 路径：D:\Tool\2022.3.62t11\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe。


- [2026-08-14 10:15:33] GIC 资源优化：BGM 用 Streaming loadType；Release 包抑制 Debug.Log；6 个 Sprite Atlas（UICommon/Icons/Avatars/NameCards/MapUI/WishUI，Assets/SpriteAtlases/）。Tuanjie SpriteAtlas API 陷阱：`atlas.Add(objs)` / `atlas.SetIncludeInBuild(true)` 是方法不是属性（在 UnityEditor.U2D.SpriteAtlasExtensions）。**How to apply:** 新增小 UI 贴图加入对应 Atlas；大贴图不入 Atlas。


- [2026-08-14 10:16:06] GIC 大资源加载：大贴图/背景/立绘通过 Addressables 按需加载（Assets/Art/ 下），Configs 和小 Prefabs 留在 Resources.Load。所有 Addressables 加载/释放统一走 AssetCache（Framework/AssetCache/，[Component] 注册到 DI），管理引用计数/去重/帧预算/优先级队列/Preload，不直接调用 Addressables.LoadAssetAsync。**How to apply:** 新增大贴图放入 Assets/Art/ 对应目录并标记 Addressable；代码中用 AssetCache.LoadAsync/Release；常驻资源用 Preload。








- [2026-08-14 12:43:04] GIC 网络请求事件（ToggleReadyRequestEvent/SetTeamRequestEvent/SetColorRequestEvent/SetSpawnRequestEvent/KickPlayerRequestEvent/SetPlayerNameRequestEvent）必须设 `Immediate = true`。**Why:** Host 模式下 SendToHost → ReceiveNetworkEvent 将事件标记 Source=Network 入 LocalEventBus 队列，若 Immediate=false 则依赖 FixedUpdate.Tick() 逐帧处理，实测在 CoopScreen 场景中队列未被处理（可能被阻塞或竞态）。设 Immediate=true 走 ExecuteImmediate 同步执行，跳过队列。**How to apply:** 客户端→服务器的请求类事件（EventType.OnlyHost）一律 Immediate=true。

- [2026-08-15 21:43:50] GIC 输入系统于 2026-08-14 三次重构为 (owner,reason) 精确配对锁：InputManager（[Component]，注入 SaveManager）持有 KeyAction 绑定表 + InputLockEntry 锁表 + IClosable UI 栈。调用点统一走静态门面 InputLocks.Push/Pop/PopAll(this, InputLockReason.Xxx)；Push 同(owner,reason)去重、Pop 幂等移除（与栈顶无关）、PopAllInputLocks(owner) 在 OnDestroy 兜底清泄漏；HasInputLock(reason) 精确查询，场景切换守卫=HasInputLock(SceneTransition)。绑定存 PlayerSaveData.keyBindings。**Why:** 历史上多次输入冻结/锁泄漏（CoopScreen 退房多余 push、祈愿原石不足早退不 pop、批量替换自噬等）。**How to apply:** 输入锁/IClosable/按键重绑定的完整操作流程与检查清单见 gic-input-system skill 和 docs/15-输入系统.md，新代码勿绕过 InputLocks 门面直接操作 InputManager。






- [2026-08-14 23:10:40] - [2026-08-14 23:12:00] GIC 存档删档测试模式已安全化：开关在菜单 Tools/存档/删档测试模式（EditorPrefs "GIC.SaveManager.DeletionTestMode"，勾选状态持久化），打包版本恒为关闭。**Why:** 原 IS_DELETION_TEST_MODE=true 常量上线忘改会清空玩家存档。**How to apply:** 开发迭代需要每次启动删档时，编辑器里勾选该菜单；测试存档持久性时取消勾选。
- [2026-08-15 21:44:35] - [2026-08-15 09:55] GIC 存档数据层统一入口：卡牌库存 PlayerSaveData.AddOwnedUnit(card)/AddOwnedItem(card)（内部自动 RebuildOwnedCards 失效缓存）；货币/物品计数 GetItemCount/AddItemCount/TryConsumeItem。**Why:** 2026-08-14 修复 WishManager.AddStarglitter 直接 ownedNormalItems.Add 漏调重建导致背包缓存过期的真 bug。**How to apply:** 新代码一律走这些入口，不要直接 ownedUnits/ownedNormalItems.Add 或手写 ownedNormalItems 遍历。

- [2026-08-15 21:43:22] GIC DI 注入模式规范（Spring Boot 风格 IoC，ApplicationContext=Spring ApplicationContext，[Component]/[Configuration]+[Bean] 注册，Wargame.Context 获取容器；P3a/P3b 完成后全仓 Wargame.Instance.XxxManager 直达已清零）：①纯C#类构造注入（Wargame.Init 按依赖顺序 Register）②MonoBehaviour 场景单例（AudioManager/EventBusHub/NetworkEventBus）懒注入——[Autowired] 字段+属性 getter 里 `if(null) Wargame.Instance?.Context?.Inject(this)`（与 GameScene.Awake 顺序无保证）③运行时实例化 prefab（Card/PopupDialog/InputPopupDialog）Awake 注入+使用点幂等补注入 ④prefab 设置项（KeyBindingSettingItem）懒获取注入 ⑤partial class 注入字段放 partial 内、注入调用放主文件 Awake ⑥静态门面（InputLocks）后端注册制 ⑦数据对象静态入口（CardConfigResolver.Instance）⑧GameScene 是组合根（保留 Init/Start/Update 引导+Context 属性+自身 [Autowired]）。**IWargameManager 陷阱：Wargame.Start() 只调 Context.PostConstruct()，IWargameManager.Start() 从未被调用——Manager 初始化必须放 [PostConstruct] public void Init()，Start() 留空。Why:** 用户要求高度模仿 Spring Boot；P3 确立分级方案。**How to apply:** 新代码按分级选模式；manager 级 Wargame.Instance.Xxx 直达禁止；Singleton<T> 基类仍被 Wargame 继承（删除属可选低收益清理）。

- [2026-08-15 00:06:33] GIC 祈愿立绘 Addressables 约定：4K 立绘放 Assets/Art/Wish/ 并标记 Addressable（WishArt 组），地址 = `WishArt/{unitName小写}`，CharacterPanelController 按此地址 AssetCache.LoadAsync。7 个卡池=7 张立绘（columbina/venti/zhongli/mavuika/ei/furina/nahida）全部已入库。**2026-08-15 修复 P3b 回归**：WishScreen 场景 7 个面板 prefab 实例中 6 个保存为未激活（activeSelf=False），Awake 不运行→[Autowired] 未注入→`_assetCache?.LoadAsync` 静默空转但 _currentAddress 已设置，FadeIn 永等 _spriteLoadDone→无立绘（唯哥伦比娅面板激活正常）。修复=AssetCacheRef 懒注入属性。**教训：场景中未激活的 MonoBehaviour 其 Awake 不运行，注入字段在激活前被外部调用必须用懒注入（服务定位器兜底）。How to apply:** 新增祈愿角色立绘按此路径+地址标记；改此类组件时检查激活时序。
- [2026-08-15 00:19:46] - [2026-08-15 00:19:30] GIC 场景系统现状（P5 后）：GameScene 仅三条存活路径——LoadSceneWithConfig（Single 场景）、PreloadScene+ActivatePreloadedScene（Additive 预加载，MainHallScreen.ExitToSceneAsync 用）、GoBack（历史栈返回，只记 Additive）。弹窗/轻提示统一走 PopupManager.Instance（静态入口，Awake 注册）；显示设置应用走 SettingsApplier.ApplyFromSave（静态类）。**Why:** P5 删除了 LoadNewRootScene/UnloadSceneAsync 全链/OnSceneWillUnloadEvent 等死路径（零调用者）。**How to apply:** 新场景跳转只用这三条路径；弹窗用 PopupManager.Instance.ShowToast/ShowModalPopup，不要走 GameScene。
- [2026-08-15 09:37:21] GIC 联机玩家管理层（P6 后，2026-08-15）：PlayerManager=纯数据层（名册 _allPlayers+SelfPlayerID+Register/Set 系列+查询，[Component] 无构造依赖，不再实现 IWargameManager）；RoomManager=流程层（[Component] 构造注入 SaveManager+PlayerManager：HandleServerConnect/Disconnect、11 个网络 Handler、GetNextAvailableColor 私有、OnKickedFromRoom、Cleanup）。事件归属：名册事件 OnPlayerCountChanged/OnPlayerInfoUpdated 在 PlayerManager，被踢事件在 RoomManager。**Why:** 拆解 god class，数据层供 EventBusHub/NetworkEventBus/Discovery 直接依赖而无需知道"房间"概念；单向依赖 RoomManager→PlayerManager。**How to apply:** 新增联机房间流程/网络请求 Handler 放 RoomManager；玩家数据字段/查询放 PlayerManager；MyNetworkManager 与 CoopScreen 注入双 Manager。Handler 常驻订阅（CanHandle 有 NetworkServer.active/Source==Network 守卫），断线不退订——2026-08-15 修复过 OnClientDisconnect 误调 Cleanup 导致同会话重连名册失联的 bug；RoomManager.Cleanup 仅作应用关闭钩子勿在断线时调用。IsOwnUnit/CanControlUnit 已删（零调用），战斗落地时在 Battle 层重建（UnitIdentity 已有 IsEnemy/IsAlly）。
- [2026-08-15 21:44:43] GIC Screen 层（P7 后，2026-08-15）：6 个弹层 Screen 全部继承 UI/Screen/ScreenBase（abstract，实现 IClosable）——Map/Backpack(partial×5)/Settings/Splash/Wish(partial×2)/Coop；MainHall 根场景不继承。基类提供：virtual Awake 注入（子类重写须 protected override + base.Awake()）、RegisterClosableSelf()、音乐幂等 Safe 系列、CloseScreen(Func<IEnumerator>) 标准关闭模板（防重入 isClosing + Closing 锁 + 音乐恢复 + 退场协程 + GoBack 收尾，退场协程内不要再 Pop/GoBack）、OnDestroy 四件套（UnregisterClosable+PopAll+UnsubscribeOwner+音乐 pop）。**Why:** 消除 6 屏重复样板；修复地图传送路径 BGM 音量永久压低与强卸载音乐泄漏两个真 bug。**How to apply:** 新弹层 Screen 按 gic-new-screen skill 骨架写（继承 ScreenBase，勿直接调 AudioManager Push/Pop——绕过标志位失去幂等）；EventBus 订阅用 owner 登记（基类兜底退订）；C# 事件（如 CoopScreen Bind/Unbind）仍手工配对；场景跳转唯一入口 SceneType.Load()（P5 核实无散点）。

- [2026-08-16 19:13:18] .NET 反射陷阱：GetFields(NonPublic|Instance) 不返回基类 private 字段——ApplicationContext.Inject/Validate 已改 GetAutowiredFields 沿 BaseType 链 DeclaredOnly 扫描，基类 [Autowired] 字段可正常注入（症状曾为全 Screen ESC/右键失灵）。**How to apply:** 新增基类 [Autowired] 字段无需特殊处理。另：需要描边文本=同字体+变体材质（变体材质挂在场景/prefab 上，运行时无代码动 fontMaterial）；TextMesh Pro/ 目录 git 忽略（材质改动不入库，重装需备份），排查该目录时 rg 需 --no-ignore。详见 docs/14 第 6 节。

- [2026-08-15 21:44:04] GIC 场景脚本接线与深拷贝铁律（P10 三次返工教训）：①跨场景预设含场景级 override 子物体的面板必须 Object.Instantiate(场景实例) 深拷贝（prefab 源实例不含场景 override）；静态预设实例保存为未激活时，运行时使用前必须显式 SetActive(true)（旧 Instantiate 克隆天生激活态，换静态实例最易漏）。②不要用 PrefabUtility.Set/GetPropertyModifications 往返清理——SetPropertyModifications 整体替换修改列表，会静默丢掉同批刚赋的新引用 mod（曾致保存后字段 null、点链接 NPE）；赋值用 SerializedObject.FindProperty().objectReferenceValue=xxx + ApplyModifiedPropertiesWithoutUndo()。③脚本改完场景必须"保存→CloseScene→重开→断言字段非 null"闭环。**编辑器安全红线：execute_csharp_script 禁止 LoadAssetAtPath 整 prefab 后递归遍历层级、禁止 Play 模式下跑重反射诊断（曾两次卡死主线程 330s，重启恢复）。How to apply:** 凡脚本改场景/克隆场景实例/写编辑器诊断脚本均按此执行。

- [2026-08-16 19:13:12] GIC Editor 工具方向定为 UITK（P11 重写并经用户验证）：3 个 Config 编辑器+4 个工具窗口已 UITK 化，公共层 Editor/ConfigEditorUITK.cs（CreateList：ListView bindingPath+根 Bind；ConfigInspectorBase；ElementDataEditorWindowBase；视觉组件含 SetBorderRadius——Tuanjie IStyle 无 borderRadius 简写）。ElementDrawer 双模式（OnGUI+CreatePropertyGUI），Editor 日志走 GICLog。保存语义：窗口修改实时生效（Ctrl+Z 可撤销），「保存到磁盘」按钮落盘。**How to apply:** 新写 Editor 工具一律 UITK 并复用 ConfigEditorUITK；Tuanjie UITK API 差异与深坑见另两条记忆。


- [2026-08-15 21:18:41] [2026-08-15] Tuanjie 1.9.3 UITK Editor API 差异（P11 实测踩坑）：①EditorWindow.CreateGUI 非虚方法（override 报 CS0115），是按名调用的魔法方法——声明 `protected void CreateGUI()` 不加 override；且 GetWindow 复用已打开窗口时 CreateGUI 不会再触发，须 rootVisualElement.Clear()+手动重建 UI；②PropertyDrawer.CreatePropertyGUI 签名为单参数（SerializedProperty，无 GUIContent）；③UIElements.Cursor 无 MouseCursor 构造函数（分栏拖拽的 resize 光标样式不可设，纯外观损失）；④VisualElement.Bind()/PropertyField 在 UnityEditor.UIElements 命名空间，需 using。**How to apply:** 新写 UITK 编辑器窗口/PropertyDrawer 直接按此模式，避开 CS0115/CS1729/CS0246 反复试错。


- [2026-08-16 00:08:14] [2026-08-15] GIC P11 UITK 深坑实录（Sprite 预览功能调试三轮结论）：①Tuanjie UITK PropertyField **不路由** CustomPropertyDrawer.CreatePropertyGUI（drawer 仅 IMGUI 上下文生效），自定义字段控件必须在自己窗口的构建点显式分发（ConfigEditorUITK.CreateField：PPtr<$Sprite> 判 type、Sprite 数组判 arrayElementType=="PPtr<$Sprite>"，实测可命中）②UITK ObjectField 点名称=Ping 资产而非打开选择器，UITK Image/背景图渲染不可靠③终局方案：Sprite 字段整行用 IMGUIContainer+EditorGUILayout.ObjectField（单击弹选择器+同帧预览），预览 GUI.DrawTextureWithTexCoords 按 textureRect UV 裁切+宽高比适配（防图集误显整图/防变形）④IMGUI ObjectField 传 GUILayout.Height(64) 会启用右侧自带大预览造成双图——字段用 singleLineHeight 手工 GetRect 布局⑤IMGUIContainer 无 GUILayout 内容时在居中父容器内会被算 0 宽（画了看不见），须 absolute 填充或用有内容的 handler。**How to apply:** Tuanjie UITK 里做对象选择+图片预览一律走 IMGUI 行混合方案，不再尝试纯 UITK。
- [2026-08-17 00:00:55] - [2026-08-16 10:15] GIC 编辑器工具规范（2026-08-16 确立）：①Config 类批量工具一律放对应 Config 的 Inspector 底部，不再建独立 EditorWindow 或 Tools 菜单项；Tools 菜单只留全局工具（场景/编辑器启动/Localization/存档/导出 APK/TMP/丢失脚本/URP/地图三件套：标定+取点器）。②编辑器窗口字体统一走 ConfigEditorUITK.ApplyGameFont(root)；新窗口/Inspector 必须调用。③Tuanjie UITK 陷阱：so.GetIterator() 上直接 GetEndProperty() 会 Assert "Invalid iteration"。**新增陷阱（2026-08-17 定论）**：④UITK 中文字体正解=TextCore.FontAsset.CreateFontAsset(zh-cn.ttf)+FontDefinition.FromSDFFont——FromFont(动态ttf) 在域重载后 TextCore 动态图集失联刷 MissingReferenceException（单次重绘 4 条）；FromSDFFont 只收 TextCore.FontAsset，TMP_FontAsset 类型不兼容不能直接传。⑤SceneView in2DMode 会强制水平相机——3D 场景工具必须先 sv.in2DMode=false 再 LookAtDirect 俯视，否则用户"看不见地图"（MapPickPointTool.FrameMap 已内置）。⑥execute_csharp_script 禁止 GetWindow 泛型参数传 null（曾致主线程死循环卡死编辑器 330s，靠重启恢复）。

- [2026-08-16 19:13:35] [project] Assets 清理定案（2026-08-16）：Material/Materials/Resources/Materials 三目录 8 个材质曾误删已全部 git 恢复，**勿再清理**（UIBlur/WishSmoke 确认在用，其余 5 个存疑也一并保留）；已删定案：Assets/Editor 空目录、Scenes/TestScene.scene、StreamingAssets/mc.mp4（在用 mc_16x10.mp4）、Assets/AI/PRJ_SUMMARY.md、Assets 根 6 个 CUsers* 垃圾文件。故意不做：NewAudioMixer、Art/Wish 与 WishArt 改名（Addressables 地址约定）；保留 GlowTest.scene+CardGlowTest.cs。**How to apply:** 不再动这批资产；SpriteAtlas 压缩格式提示为原有建议性警告。



- [2026-08-16 11:21:24] [project] Tuanjie 1.9.3 加密资产 GUID 机制（2026-08-16 删错材质事故实证）：部分资产（tag:yousandi.cn 新建）meta 里的 base64 guid 是**密文**，资产真实 hex GUID 不出现在 meta 文本中——场景/Prefab 引用的是真实 hex GUID（如 UIBlur.meta 写 base64 但 AssetDatabase 报 6a85668ad20282f4aa90aa58828cdc43，场景正是引用这个 hex）。**Why:** 文本 GUID 审计（meta 提取 guid 全文检索）对加密资产必然误判"零引用"——2026-08-16 据此删了 UIBlur/WishSmoke 等 8 个材质，用户反馈毛玻璃+祈愿烟雾立即失效，git checkout HEAD 恢复后验证引用链完好。**How to apply:** 判定资产是否被引用禁止用文本检索，用 unity_asset get_info 查真实 GUID（或 execute_csharp_script 走 AssetDatabase.GetDependencies，edit mode 安全）；真实 hex GUID 可再对场景文本 Select-String 确认。

- [2026-08-16 15:45:02] [2026-08-16] GIC 真机描边丢失已根治（docs/14 §6.3）：zh-cn SDF.asset 三路引用（场景直引+TMP Settings 默认字体+Addressables UIAssets MainFont），打包后 Addressables 份与场景份是不同实例，TextCombiner.ApplyFont 引用比较误判字体变更→fontMaterial 覆盖回 _OutlineWidth=0。**根治=删除 TextCombiner 运行时字体加载链（LoadFont/ApplyFont/fontTable/fontEntryKey 全删）+ 删 UIAssets 表 5 语言 MainFont 条目 + 手清 Addressables Localization-Assets-Shared 组残留条目（read-only 组不随表变更自动同步，改后须 ImportAsset(ForceUpdate) 重载）**。字体仅剩场景直引+TMP 默认字体两条固有路径，运行时无代码动 font/fontMaterial。7 个祈愿面板共用 VentiPanel.prefab（描边材质在源 prefab 上：名称黑0.15+称号白0.08）。**Why:** UIAssets 表只有中文配过字体、唯一消费者是 TextCombiner，属死代码路径。**How to apply:** 勿恢复"运行时整体赋 font+fontMaterial"写法；将来做多语言字体切换时只换 fontAsset 并保留材质变体映射。
- [2026-08-16 21:03:14] [2026-08-16 20:50] GIC 贴图压缩排查定论（docs/14 §8.2 已修正）：Tuanjie 1.9.3 块压缩两个独立阻断——①mip 开启对**大图**（4096+ Sprite）静默回退未压缩（小图 16/17 压缩正常，勿再泛化）；②任一边非 4 倍数拒绝压缩（mip 无关，旧区域图 505MB 全因此）。修复套路：textureFormat=-1 Automatic+Compressed、关 mip（大图 UI 用不到）、外部补齐 4 倍数、巨图外部缩（编辑器内重导入 OOM）。**在用待修清单（约省 55MB）：Wish back.png 3199×1799、logo.png 1716×1073、元素图标 Deep/Stroke 801×801×14**——外部缩到 4 倍数+Automatic 即可，尚未执行待用户点头。6 张旧区域图等零引用资产删除即消失不修。锚点恒定视觉尺寸已实现：MapScreen.UpdateAnchorConstantScale（相机尺寸/基准8 比例缩放根节点，热区同步恒定）。**Why:** 大地图 3D 化后续优化。**How to apply:** 修贴图按 docs/14 §8.2 套路；锚点尺寸基准调 MapCameraController.默认视野尺寸。
- [2026-08-18 00:48:31] [2026-08-16] 大地图改原神式（用户明确要求对齐原神）：锚点全图常驻（SpawnAllAnchors）、区域按钮只动相机、区域切换平滑滑移（fromMaxZoom 区分初次打开）。**坐标系统=固定世界坐标系 + 垂直画布（2026-08-17 晚定稿并提交 0163533/2488cae/0d3491d，Unity 2D 约定）**：地图平铺 XY 竖直平面（z=0），rotation 全 identity，相机朝 +Z；**关键迁移教训：垂直画布与旧俯视 XZ 轴向同向，z 直接作 y（恒等迁移）——首轮误做 y=−z 镜像致 Play 黑屏**；mapOrigin（(−107.52,69.12)）+ worldUnitsPerPixel（0.02）标定摆图；射线守卫=不平行且 t>0（勿按相机朝向假设方向符号）。**工具已收敛（用户拍板）**：Tools/地图 只剩「坐标取点器」；MapCalibrationTool 纯逻辑类（ApplyFromPickTool，无 MenuItem），应用按钮在取点器面板——工作流面板闭环：模式→取点→保存→应用→目测；恒等解自检=点 A/B 绿圈中心应解得 unit 与当前 MapConfig 一致（图不动，勿硬记具体数值）；B 点硬约束投影吸附约束线；Undo 全链；锚点视觉大小 Anchor3D.prefab 根缩放 0.7。**瓦片化（大厂方案）已完整实现又按用户要求全量撤回（2026-08-17 深夜，未提交一行）**：MapTileLayer（视野动态加载瓦片走 AssetCache）+ MapTileSlicer（ffmpeg 切图入 MapTiles Addressable 组）+ MapConfig 双模式——方案验证全 PASS（24 瓦片入库、几何 PASS）后被撤回；用户只说"错误，撤回"未说明原因，**重做前必须先问清哪里不对**（可能顾虑：复杂度/Addressables 打包/或只是暂不需要）。30K 新图超 GPU 单纹理 16K 上限时此方案仍是唯一出路。**Why:** 用户以原神+大厂实践为基准。**How to apply:** 换图=覆盖 all_map.jpg→取点器标定模式点两下→保存→面板应用→目测；瓦片化代码在对话历史 2026-08-17 22:54-23:05 段完整存在，重新落地前先确认用户顾虑；新代码勿再引入 XZ/Euler(90)/按朝向假设射线符号。
- [2026-08-18 00:48:31] [2026-08-18] 新版大地图已换图+标定完成（提交 d1a7988）：16384×13896 未压缩导入，MapConfig origin=(-108.59,114.94) unit=0.013348（旧 0.02×0.667），锚点数据未动，用户目测确认对齐。**2026-08-18 标定事故**：首轮标定解出 unit=0.5（25 倍偏差）被静默保存+应用——根因=下拉所选 A/B 锚点与图上实际点击的两处地点不对应，且 MapPickPointTool 声明的方向校验常量从未被使用。**Why:** 已补两道防呆（方向偏差>3°拒解算、unit 比值超±3倍拒写入）+修正文案（绿圈=锚点应在位置，点击处=城市实际位置）。**How to apply:** 换图标定时严守"下拉 A/B 与点击两处一一对应"；防呆拒绝时按提示核对，勿绕过校验硬填值。

### Reference
- [2026-07-28 16:17:53] Localization CSV tool at Tools/Localization/CSV 导出导入 (created 2026-07-28). Exports/imports all 21 TableName localization tables to/from CSV ({TableName}.csv). CSV format: Key,Id,zh-Hans,zh-TW,en,ja,ru. RFC 4180 compliant (supports commas/quotes/newlines in values). Smart matching: Id first, then Key, then create new. **Why:** Unity Localization YAML is unicode-escaped and hard to batch-edit. **How to apply:** use this tool instead of Unity Localization Window for bulk edits; CSV folder defaults to Export/Localization.
- [2026-08-14 10:16:21] MC mod gichess（旧项目，Java/NeoForge）：源码 D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\（308文件），jar D:\Picture\gichess\my\wg-0.2.d。~20+角色，7元素18反应，蒙德延奏/纳塔夜魂已实现。**Why:** GIC 战斗系统 Unity 移植的架构参考。**How to apply:** 需要查旧 Java 实现时按路径阅读源码。


- [2026-08-10 09:28:39] 技术陷阱与 Bug 修复记录：详见 docs/14-技术陷阱与Bug修复记录.md（场景闪烁修复、Unity UI 陷阱、导出/构建陷阱、ParticleSystem 陷阱、Localization 陷阱）。
- [2026-08-14 10:48:22] GIC 本地化表编程操作 API（Tuanjie 引擎 Unity Localization）：获取集合 `LocalizationEditorSettings.GetStringTableCollections().FirstOrDefault(c => c.name == "UIText")`；添加 key 用 `sharedData.AddKey("Key")` 返回 SharedTableEntry（不是 AddEntry）；遍历 locale 表用 `collection.Tables` 中 `tableRef.asset as StringTable`，locale code 用 `st.LocaleIdentifier.Code`（不是 `locale.identifier`）；设值用 `table.AddEntry(entryId, value)` 新增或 `table.GetEntry(id).Value = value` 更新；保存用 `EditorUtility.SetDirty(sharedData)` + `AssetDatabase.SaveAssets()`。**How to apply:** 需编程批量操作本地化表时用此 API，避免反复试错（Locale API 与标准 Unity 略有不同）。

- [2026-08-16 10:42:39] - GIC 存档版本策略改为**重置式**（2026-08-16）：CURRENT_SAVE_VERSION=3，LoadSaveData 解析后发现 saveVersion < CURRENT 直接 CreateNewSave()（删旧档开新档），无迁移链（UpgradeFromV1ToV2 已删）；ApplySaveCompatibility 只留"存档高于游戏版本"警告。**Why:** 开发期无真实玩家，语义变更直接升版重置比维护迁移链省事（用户明确要求"版本号小于3直接删老存档创新存档，无需迁移"）。**How to apply:** 存档字段语义变化时只递增 CURRENT_SAVE_VERSION；将来上线需保玩家数据再在 LoadSaveData 版本分支处补迁移链。同步已更新 gic-save-system skill。
- [2026-08-16 10:58:59] GIC 文档体系：**docs/17-代码架构指南.md 是新会话入口文档**（2026-08-16 新建——目录结构/核心系统速查表/场景清单/配置资产/常用工作流速查/战斗规划/环境备忘），开工先读它再按需深入；docs/00-12 玩法设计、13 大地图、14 技术陷阱（已补第 7 节 Tuanjie UITK）、15 输入系统、16 优化史（收官）。**Why:** 此前只有玩法文档和专项记录，缺代码地图，AI 冷启动要重新探索。**How to apply:** 新系统落地/结构性变化后更新 docs/17 对应小节保持时效。
- [2026-08-16 10:59:13] - [2026-08-16 11:05] - GIC 项目 skill 清单（.codely-cli/skills/，2026-08-16 扩至 11 个）：gic-new-unit（角色文档→UnitConfig/本地化）、gic-localization（本地化 CSV）、gic-input-system（输入锁/IClosable/按键绑定）、gic-new-screen（新建 Screen/场景）、gic-save-system（存档/重置式版本策略）、git-commit-helper、gic-di-injection（DI 注入分级/Manager 生命周期）、gic-editor-tool（UITK 工具/Tuanjie 陷阱表）、gic-audio（AudioManager/ScreenBase Safe）、gic-eventbus（事件订阅 owner 制/Immediate/网络 Handler 常驻）、gic-assetcache（LoadAsync/Release/Preload/地址约定/防过期回调模板）。未建 skill：祈愿（记忆覆盖三层架构）、大地图/APK 导出（记忆覆盖操作要点）、战斗系统（未实现，落地后建）。Android 包名 com.HGAME.gic，编辑器切平台可能被重置，提交前核对。
- [2026-08-17 10:32:10] [2026-08-17] 本机 ffmpeg：D:\Tool\FormatFactory\ffmpeg.exe（7.1 完整构建，含 trace_headers/ffprobe 不存在）。PATH 里没有 ffmpeg，媒体处理直接用此路径。已用它重编码 Assets/StreamingAssets/mc_16x10.mp4（Unity VideoPlayer WMF 双告警修复：baseline profile 消时间戳告警，VUI+容器双写 bt709 消色彩告警，x264-params 与 -color_* 需同时给才能两处都写全）。
- [2026-08-17 23:07:26] [2026-08-17 23:15] 大地图交接文档：.codely-cli/HANDOFF-大地图.md（2026-08-17 深夜写）——含当前基线（0d3491d）、用户即将换新图（含至冬）的完整工作流、瓦片化被撤回未决事项（重做前必问原因）、旧待拍板清单、五条血泪铁律。新对话做大地图任务先读它。
