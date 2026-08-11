

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

### Project





- [2026-08-05 23:39:59] Game design documents in docs/ directory. "协议核心" is a simultaneous-turn card-based tactical wargame (原神IP, up to 6 players, LAN multiplayer via Mirror). Core loop: 祈愿解锁→局前选8种卡→同时选1行动→攻速排序执行→摧毁核心掠夺→原石结算. Key design: (1) 摩拉 is sole deployment currency, 5/turn auto, initial 200; (2) 7 factions mixable (蒙德延奏/璃月契约/稻妻连携/挪德双祝福命座/纳塔夜魂/须弥智慧/枫丹芒荒); (3) 命座0-3, 重复出战升命; (4) 战败=协议核心被摧毁→消散+观战; (5) 建筑=科技树节点, 买卡费用=出战费一半; (6) 初始6体力+半透明迷雾+3×3石路出生点; (7) 七元素反应系统(docs/06). **Why:** Tracks evolving game design. **How to apply:** Reference when implementing battle system, faction mechanics, or economy; design is actively iterating.

- [2026-07-28 09:14:44] GIC (协议核心 game project) targets全平台互通 (cross-platform: PC + mobile + console). Project name "GIC" derived from directory D:\Tuanjie_editor\gic.
- [2026-07-28 09:16:21] GIC project: solo developer (1人), personal Demo/portfolio goal, NOT commercial launch. Target audience: both Genshin players and strategy/tactical gamers. **Why:** Solo dev with limited resources — scope must be drastically cut from GDD ambition. **How to apply:** Recommend 2-player 1v1 over 6-player, 2-3 factions over 7, hotseat/local before Mirror networking, vertical slice over breadth.


- [2026-08-02 14:07:01] Unit data docs at docs/units/ (moved from docs/unit-data.md on 2026-08-02): Each character has its own MD file (安柏.md, 凯亚.md, etc.). Field reference & template at docs/units/_模板与字段说明.md. AI reads these files to sync UnitConfig.asset / SkillName enum / SkillParamKey enum / localization tables. **Why:** eliminates manual multi-file editing when adding/modifying characters — edit one MD, AI applies changes everywhere. **How to apply:** user edits docs/units/{name}.md, asks AI to apply; AI updates enum + UnitConfig.asset + localization tables per gic-localization skill.


- [2026-07-29 22:56:01] GIC battle system architecture decision (2026-07-29): Will use StS-style "Action Queue (coroutine, serial) + DamagePipeline (sync, Phase hooks)" pattern. EventBusHub/LocalEventBus retained for UI/network layer only — battle logic does NOT go through EventBus. Key mappings: AbstractGameAction→BattleAction(IEnumerator Execute()), addToBot→ActionQueue.Enqueue(), DamageInfo.applyPowers→DamagePipeline.Process(), AbstractPower hooks→IDamageHook+DamagePhase enum (PreDamage/Calculate/PostDamage/OnDeath), isDone→yield return. No R/D two-phase needed (sync pipeline makes it unnecessary vs Java mod's frame-driven queue). **Why:** User confirmed turn-based = strict serial, no concurrency. StS validates this exact pattern. **How to apply:** When implementing battle system, create ActionQueue + DamagePipeline as separate layer from existing EventBus.
- [2026-08-01 21:49:24] GIC 项目使用 ParrelSync 进行编辑器内联机测试（2026-08-01 安装）。位于 Packages/ParrelSync（从 umc 项目复制）。使用方法：菜单 ParrelSync/Preferences/Clone Manager → Clone current project → 两个 Editor 窗口都 Play，一个 Host 一个 Client。Clone 通过 Windows Junction 链接 Assets/Packages/Library，改代码实时同步。
- [2026-08-09 20:02:13] GIC 角色语音架构：语音数据内嵌在 UnitConfig.UnitData.voices (UnitVoiceData)，不独立 ScriptableObject。UnitVoiceData 包含 6 组通用 AudioClipRandom (onGoWar/onChooseHighHP/onChooseLowHP/onHitLight/onHitHeavy/onDie) + SkillVoiceEntry[] (按 SkillName 索引)。AudioClipRandom 已有权重+不连续重复。复用 AudioManager.PlayVoice 通道。语音文件在 Assets/Resources/Audios/Voices/{UnitName}/，命名: go_war_0.wav, choose_high_hp_0.wav, hit_light_0.wav, die_0.wav, {skillname_snake}_0.wav。Editor 工具 Tools/UnitConfig/自动加载语音 按文件名前缀匹配自动填充。

- [2026-08-05 23:39:43] GIC 术语区分：【消散】（带方括号）= 玩家战败时该玩家所有单位从游戏中真正删除，不可恢复；【放逐】= 转移到另一个维度，单位仍然存在，可被特定技能召回（如丽莎爆发蔷薇的雷光可召回被放逐的标记单位并承受真伤惩罚）。Buff/造物过期用"消失"，不用"消散"。丽莎文档中"标记所属单位【消散】"是正确用法（指玩家战败）。凯亚/芭芭拉文档中的"棱消散/环消散"旧措辞应改为"消失"以避免与游戏机制混淆。

















- [2026-08-11 00:13:39] GIC 祈愿系统架构：WishPoolConfig(ScriptableObject) 定义卡池角色/物品/权重（star5Weight~star1Weight + unitWeight/itemWeight，概率=权重/总权重，无需凑满100） → WishManager 负责货币消耗+写入存档 → WishDrawController 控制 UI 动画。祈愿机制=反应速度游戏：卡道滚动出卡，玩家射击命中哪张卡获得哪张卡（不预抽），通过 Card.saveCardData 读取结果写入存档。卡池绑定：CharacterEntry.pool 直接引用卡池 asset，pool 为 null 则提示"卡池未开放"。卡池 asset 命名 WishPool_{势力英文名}.asset。当前权重：1/4/10/30/55（5★→1★），角色物品各50。星辉(Starglitter, ItemName=1006)为4★货币物品，祈愿可获得，初始数量0。重复角色卡转星辉（5★=50/4★=25/3★=15/2★=8/1★=3），count>0判重复，count=0为新获得。相遇之线：PlayerSaveData.starglitterEarned（累计获取量，只增不减，与可消费的星辉余额解耦）每满20触发1次，encounterUsed（持久化）记录已用次数。下次射击变金色相遇之线，射中卡先Roll提升次数，逐级星级提升（RollStarLevel映射：5★→升4级...1★→升0级）。中间星级卡只做视觉变换+判重复发星辉（AwardStarglitterIfDuplicate，不写入存档），只有最终星级卡才AddResultToInventory写入存档。相遇射击冷却固定(baseShotCooldown)不随星级变化，_isEncounterAnimating标志阻断射击+冻结冷却条直到动画完成，避免泄露提升次数。抖动+变亮动画强度和持续时间随星级递增。动画完成后走HoldThenFly（与普通射击一致）。星辉雨用对象池+单协程批量管理（List<StarglitterDropData> struct）。**How to apply:** 新增卡池创建 WishPoolConfig asset 并拖到对应角色的 CharacterEntry.pool；UI 布局在场景 Canvas/WishDrawRoot 下改；货币显示在 WishScreen 根对象的 WishScreen（partial class WishScreenDraw）上，TopPanel/CurrencyContainer 下 PrimogemDisplay/StarglitterDisplay；WishDrawController 挂在 Canvas 上。





- [2026-08-09 11:35:34] GIC 构建配置（ProjectSettings.asset 可直接编辑）：测试用 Mono+ARMv7（快，约3分钟），发布用 IL2CPP+ARM64+Stripping Low（慢首次5-15分钟）。字段：scriptingBackend.Android(0=Mono,1=IL2CPP)、AndroidTargetArchitectures(1=ARMv7,2=ARM64)、managedStrippingLevel.Android(3=Low)。Android APK ~270MB vs Windows ~2GB 是正常的（APK ZIP压缩 + ASTC纹理 vs Windows 未压缩 + DXT纹理）。**Why:** 频繁切测试/发布配置。**How to apply:** 直接编辑 ProjectSettings.asset 改这三个字段，无需 execute_csharp_script（Tuanjie 引擎限制 EditorSettings/GraphicsSettings 类型不可用）。编辑器崩溃看 Editor.log：%USERPROFILE%\AppData\Local\Tuanjie\Editor\Editor.log；Windows 事件查看器查 Tuanjie.exe 异常码。
- [2026-08-10 00:49:23] GIC 快速导出 APK 工具：Tools/导出 APK/ 快速导出(Mono/ARMv7 测试) 或 正式导出(IL2CPP/ARM64 发布)。脚本在 Assets/_Scripts/Editor/Tool/QuickAPKBuilder.cs。**重要：构建前必须确保编辑器在 Android 平台**（脚本会自动切换）。**Why:** 不切换平台时 Addressables 会按 Windows 平台打包 bundle（DXT 纹理/Windows 路径），导致 Android 上黑屏+无声音+体积大 200MB（2026-08-10 排查）。脚本构建后不切回原平台，避免第二次纹理重导入。**How to apply:** 日常测试用"快速导出"，发布用"正式导出"。ADB 路径：D:\Tool\2022.3.62t11\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe。


- [2026-08-09 20:01:16] GIC 资源优化约定：BGM 用 Streaming loadType；Release 包用 `#if !UNITY_EDITOR Debug.unityLogger.filterLogType = LogType.Warning #endif` 抑制 Debug.Log；6 个 Sprite Atlas（UICommon/Icons/Avatars/NameCards/MapUI/WishUI，路径 Assets/SpriteAtlases/）。Tuanjie 引擎 SpriteAtlas API：扩展方法在 UnityEditor.U2D.SpriteAtlasExtensions，`atlas.Add(objs)` / `atlas.SetIncludeInBuild(true)` 是方法不是属性；textureType=8 是 Sprite。**How to apply:** 新增小 UI 贴图加入对应 Sprite Atlas；大贴图（4096²角色立绘/地图/背景）不入 Atlas。

- [2026-08-09 20:01:58] GIC 大资源加载架构：大贴图/背景/立绘通过 Addressables 按需加载（Assets/Art/ 下 WishArt/{name}、PositionBack/{Region}/{name}、Map/Textures），Configs 和小 Prefabs 留在 Resources.Load。Addressables 加载用 LoadAssetAsync 协程+OnDestroy Release。**How to apply:** 新增大贴图/背景/立绘放入 Assets/Art/ 对应目录并标记 Addressable；Configs 和小 Prefabs 留在 Resources。


- [2026-08-09 20:02:06] GIC 地图系统架构：MapConfig 配置驱动（非 Prefab）。MapConfig.asset（Resources/Configs/）存储各区域的 mapImageAnchoredPosition/contentAnchoredPosition/contentSizeDelta + 锚点列表。锚点使用归一化坐标(0-1)，0=图片左上，1=图片右下，图片尺寸变化自动适配。场景中静态结构：MapScreen.unity Canvas 下 MapRoot > Scroll View(SimpleMapZoom) > Viewport > Content > MapImage + Anchors。ShowRegion 读 Config 更新位置+创建锚点。**How to apply:** 新增/修改区域锚点在 MapConfig.asset Inspector 中编辑。


### Reference
- [2026-07-28 16:17:53] Localization CSV tool at Tools/Localization/CSV 导出导入 (created 2026-07-28). Exports/imports all 21 TableName localization tables to/from CSV ({TableName}.csv). CSV format: Key,Id,zh-Hans,zh-TW,en,ja,ru. RFC 4180 compliant (supports commas/quotes/newlines in values). Smart matching: Id first, then Key, then create new. **Why:** Unity Localization YAML is unicode-escaped and hard to batch-edit. **How to apply:** use this tool instead of Unity Localization Window for bulk edits; CSV folder defaults to Export/Localization.
- [2026-07-29 17:22:57] MC mod gichess (old project, Java/NeoForge): Java source at D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\ (308 files). Extracted jar at D:\Picture\gichess\my\wg-0.2.d. Architecture: EventBus (frame-driven queue, owner-order + priority sorting, LOCK animation blocks queue), SkillExecutionE.R/D two-phase events, DamageS.DEFFAULT (synchronous damage pipeline: element attach → reaction loop → formula → shield → HP → DamageA animation), AnimationManager (dual-channel: concurrent + serial-locked), ChessBoard.post() sets event order = firstRoles+secondRoles+board. ~20+ characters (Amber/Barbara/Kaeya/Lisa/Klee/Jean/Citlali/HaborymBoss etc), 7 element types + 18 reactions, Mondstadt延奏/Natlan夜魂 implemented. **Why:** validates GIC core gameplay and provides battle system architecture reference for Unity port. **How to apply:** reference for Unity系分 — map EventBus→EventBusHub(with owner ordering), LOCK animation→coroutine yield, DamageS→DamageCalculator(synchronous), AnimationManager→Unity Animator+VFX, SkillExecutionE.R/D→ActionRequest/ActionResult events.

- [2026-08-10 09:28:39] 技术陷阱与 Bug 修复记录：详见 docs/14-技术陷阱与Bug修复记录.md（场景闪烁修复、Unity UI 陷阱、导出/构建陷阱、ParticleSystem 陷阱、Localization 陷阱）。
