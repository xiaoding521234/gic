

## Codely Structured Memories




### User
- [2026-07-27 14:07:54] User is experienced with Java backend application development (Spring Boot, etc.). When explaining Agent/AI concepts, use Java backend analogies (Controller/Service/Mapper, Spring patterns, etc.) instead of Python/AI-native analogies.

### Feedback
- [2026-07-25 09:27:44] User prefers Chinese field names for Inspector-exposed serialized fields (e.g. 光柱颜色, 上升时间). When creating UI/effect components, use Chinese [SerializeField] names and [Header] labels instead of English. **Why:** User explicitly asked to change LightPillarEffect fields from English to Chinese for readability. **How to apply:** New MonoBehaviour components with Inspector-facing fields should use Chinese names by default.
- [2026-08-01 00:22:21] GIC 项目不使用 asmdef（2026-08-01 决定）。尝试过拆分 GIC.Framework/GIC.UI/GIC.Battle/GIC.Editor，但 Framework↔UI↔Battle 间存在大量循环依赖（Framework 引用 UI 的 PopupManager/CardDetailView，Battle 引用 Framework 的 SaveCardData，PlayerManager 引用 Battle 的 Unit/TeamType），硬拆需引入大量接口。**Why:** 单人开发、176 文件、个人 Demo，asmdef 收益不足以抵消重构成本。**How to apply:** 保持单一程序集 + 命名空间（GIC.Framework/GIC.UI/GIC.Battle/GIC.Editor）做逻辑隔离即可，不主动推进 asmdef 拆分。
- [2026-08-02 01:28:18] 用户偏好直接在 Unity 编辑器中修改 Prefab/场景的 UI 属性（如字体大小），而非在代码中运行时修改。**Why:** 用户明确指出"为什么不直接在编辑器里修改"，代码中修改 UI 属性不直观且难以维护。**How to apply:** 字体大小、颜色、布局等 UI 属性应直接在 Prefab 或场景中修改，不要在 MonoBehaviour 代码中通过 `tmp.fontSize = X` 设置。
- [2026-08-02 23:43:50] 角色属性设计规范（2026-08-02）：属性命名参考 HP，不加 Max 前缀。如 baseEnergy（不是 baseMaxEnergy），StatType.Energy（不是 MaxEnergy）。baseEnergy 是基础值=上限，角色登场时当前元能为 0 是运行时逻辑，不在 UnitStats.Init 中处理——Init 只负责把 baseEnergy 设为基础值，和 HP 完全一致。**Why:** 用户指出 Energy 应参考 HP 的设计，HP 的 baseHP 既是基础值也是上限，当前血量在战斗中往下扣；Energy 同理，baseEnergy 既是基础值也是上限，当前元能在战斗中从 0 往上加。**How to apply:** 新增属性时不要加 Max 前缀，Init 中直接 SetBaseValue，运行时的当前值/上下限管理留给战斗系统。
- [2026-08-03 13:26:42] 通用设计规则应写在总设计文档中（如 docs/08-命座系统.md），不应写在个别角色文档里。**Why:** 用户纠正了 AI 试图在芭芭拉.md 中添加 0命概念说明的做法——角色文档只记录该角色的具体数据，通用规则属于总文档。**How to apply:** 涉及多角色的通用规则/概念，写入对应的总设计文档（如命座系统→08、行动系统→05），不要在角色文档中重复。
- [2026-08-04 13:38:46] 用户偏好自己测试游戏效果，不需要 AI 进入 Play Mode 截图验证。**Why:** 用户明确说"你不需要截图，测试交给我"。**How to apply:** 完成代码改动后编译验证即可，不要主动进入 Play Mode 截图测试游戏画面效果。
- [2026-08-04 15:09:49] Unity UI 自定义 Shader 特效应使用自定义 Graphic 子类而非 Image 组件。**Why:** Image 组件要求 shader 必须有 `_MainTex` 属性（否则报 warning），且需要 Sprite 才能生成 mesh；在 Mask/Stencil 环境下还有额外的参数注入问题。改用 `class XxxGraphic : Graphic` + `OnPopulateMesh` 直接生成 quad，基类自动处理 stencil 注入，彻底绕过这些问题。**How to apply:** 需要在 UI 上叠加自定义 shader 效果时，创建 Graphic 子类而非用 Image + material。
- [2026-08-09 20:02:41] 祈愿系统 UI 层级在场景中静态预设（WishDrawRoot 下的 CardTrack/CountdownBar/ResultContainer/FinalDisplay），未抽卡时 drawRoot 隐藏。**Why:** 用户要求祈愿 UI 不要代码动态生成，要提前在场景里建好便于编辑器调整布局。**How to apply:** 后续调整祈愿 UI 布局直接在 WishScreen 场景的 Canvas/WishDrawRoot 下改，不要在代码里用 new GameObject 创建。


- [2026-08-08 21:48:15] Toggle 组件的 transition=Fade 会控制 CanvasGroup.alpha，即使手动设 alpha=1 也会被 Toggle 覆覆为 0（当 toggle.isOn=false 时）。`toggle.interactable=false` 不能阻止此行为，必须 `toggle.enabled=false` 才能完全禁用 Toggle 对 CanvasGroup.alpha 的控制。**Why:** 祈愿卡道卡用 `toggle.interactable=false` 后卡片背景仍半透明，用户排查发现是 Toggle Fade transition 导致 CanvasGroup.alpha 被设为 0。**How to apply:** 任何不需要 Toggle 交互的 Card（如 OnlyDisplay 状态）用 `toggle.enabled=false` 而非 `toggle.interactable=false`。
- [2026-08-09 00:19:47] 技能参数非 Fixed 值是百分比，不是固定值。如"伤害 40 BasedOnAttack"= 40%×攻击力，不是 40 点固定伤害。**Why:** 用户纠正了 AI 评审时把百分比当固定值计算导致数值分析全部错误的问题。**How to apply:** 读取角色文档的技能参数表时，非 Fixed 基准的值一律按百分比理解（代码 SkillParam.GetDisplayValueText 中 value+"%"）。
- [2026-08-09 01:51:41] Unity Localization CSV 导入新增条目时，AddKey(key) 不带 Id 参数会自动分配一个大数字 Id（如 286xxxxxxxx），与枚举值不对齐。必须用 AddKey(key, id) 显式传入枚举值对应的 Id。**Why:** 2026-08-08 批量补齐 Jean/Hilichurl/UnitTag 缺失本地化时，CSV 导入后 Id 全部变成自动分配值，运行时按 Id 查找会失败。**How to apply:** CSV 导入后检查 SharedData.Entries 中的 Id 是否与枚举值一致；不一致时先 RemoveKey 再 AddKey(key, correctId)，然后重新导入 CSV 设置各语言值。
- [2026-08-09 02:39:35] "编辑器正常但导出崩溃"排查模式：第一步读 Player.log（%USERPROFILE%\AppData\LocalLow\{Company}\{Product}\Player.log）和 Crash 报告（%USERPROFILE%\AppData\Local\Temp\{Company}\{Product}\Crashes）。最常见原因：①Shader.Find() 引用的自定义 shader 未加入 GraphicsSettings→Always Included Shaders，导出时被剥离（堆栈含 Canvas:GetDefaultCanvasMaterial 或 material null）②Resources.Load 路径下资源未打包（检查 Assets/Resources 目录）③#if UNITY_EDITOR 代码块在导出后消失导致逻辑缺失。**Why:** 2026-08-09 祈愿系统导出后崩溃，根因是 9 个自定义 shader 被剥离导致 Canvas.GetDefaultCanvasMaterial 原生崩溃。**How to apply:** 项目中所有 Shader.Find() 调用的 shader 必须在 Always Included Shaders 中（编辑 GraphicsSettings.asset 或用 SerializedObject 方式）；代码中 Shader.Find 返回值必须 null check；避免运行时访问 Graphic.materialForRendering/defaultMaterial。
- [2026-08-09 21:24:08] URP/Tuanjie 引擎下 `Universal Render Pipeline/Particles/Unlit` shader 的 blend 模式属性（_SrcBlend/_DstBlend）会被 shader GUI 在 AssetDatabase.Refresh 时重置。关键字 `_BLENDMODE_ADDITIVE` 会落入 `m_InvalidKeywords` 而非 `m_ValidKeywords`，即使手动编辑 .mat 文件也会被重新导入覆盖。**Why:** 2026-08-09 祈愿粒子材质设 Additive blend 后反复被重置为 Alpha blend，导致粒子显示为方形。**How to apply:** 不要用 URP 内置 Particles/Unlit shader 的材质属性控制 blend 模式，创建自定义 shader（如 `Wish/ParticleAdditive`）在 SubShader 中硬编码 `Blend SrcAlpha One`，彻底绕过 shader GUI 重置问题。`SetFloat` 不生效时用 `SetInt`（这些属性存储在 m_Ints map 中）。
- [2026-08-09 21:24:44] ParticleSystem 在 UGUI Canvas 中渲染需要 Screen Space Camera 模式（Screen Space Overlay 不经过任何相机，ParticleSystem 无法渲染）。需要：①创建 UICamera（Orthographic, cullingMask=UI layer）②Canvas.renderMode=ScreenSpaceCamera ③用子 Canvas + overrideSorting 做分层排序（如 BackgroundLayer sortingOrder=50 < ParticleSystem 75 < 主 UI 100）。**Why:** 2026-08-09 祈愿氛围特效从手动 Image 粒子迁移到 ParticleSystem 时，Overlay Canvas 下粒子完全不显示。**How to apply:** 任何需要 ParticleSystem 在 UI 层渲染的场景，Canvas 必须切为 Screen Space Camera 模式。
- [2026-08-09 22:02:45] ParticleSystem `prewarm=true` 只在 `playOnAwake=true` 自动播放时生效，手动 `Play()` 不触发 prewarm。正确做法：`ps.Clear(true); ps.Simulate(ps.main.duration, true, true); ps.Play(true);` 手动模拟一个完整周期预填充粒子。**Why:** 2026-08-09 祈愿粒子设了 prewarm=true 但 playOnAwake=false，结果画面初始无粒子全部从右侧飘入；最初以为只需设 playOnAwake=true，但实际需要 Simulate 手动预热才能在代码控制启停的同时预填充。**How to apply:** 需要代码控制启停且初始预填充粒子时，用 `Simulate(duration, true, true)` + `Play()`，不依赖 prewarm 标志。

- [2026-08-09 21:26:42] 小尺寸粒子纹理（如 64×64）不要用 DXT5 压缩，alpha 通道精度太低导致径向渐变退化为方块。必须用 TextureImporterCompression.Uncompressed（RGBA32）。**Why:** 2026-08-09 SoftCircle.png 被 Tuanjie 引擎自动压缩为 DXT5，alpha 渐变精度丢失，粒子看起来像带圆形内核的方块而非柔和圆形。**How to apply:** 程序生成的小粒子纹理（径向渐变、柔光等）一律设为 Uncompressed + alphaIsTransparency=true + mipmapEnabled=false + FilterMode.Bilinear + WrapMode.Clamp。
- [2026-08-09 22:30:29] ParticleSystem `main.startColor` 只影响后续发射的新粒子，已存活粒子颜色不变。切换颜色时需用 `GetParticles`/`SetParticles` 遍历修改存活粒子的 `startColor`。预分配 `ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[128]` 复用避免 GC。**Why:** 2026-08-09 祈愿切卡池时已生成粒子颜色不变化。**How to apply:** 任何需要实时改变所有粒子颜色的场景，不能只设 main.startColor，必须同时 GetParticles/SetParticles。

- [2026-08-09 22:30:49] ParticleSystem Velocity over Lifetime 的 X/Y/Z 三轴 curve mode 必须一致（同为 Constant 或同为 TwoConstants），否则报错 "Particle Velocity curves must all be in the same mode"。Z 轴不用也要设成相同 mode（如 TwoConstants(0,0)）。**How to apply:** 设置 velocityOverLifetime 时三轴统一 mode。

- [2026-08-09 22:30:54] ParticleSystem 最终渲染 alpha = `main.startColor.a × colorOverLifetime.gradient.a`。两者相乘而非取其一。设 startColor.a=0.15 且 gradient 中段 a=0.15 时实际 alpha 仅 0.0225。**Why:** 2026-08-09 雾气粒子太淡，排查发现 startColor 和 gradient alpha 相乘导致实际值远低于预期。**How to apply:** 透明度只在一处控制——要么 startColor 控制固定透明度 + gradient 中段=1.0 做淡入淡出，要么 gradient 控制全程 + startColor.a=1.0。不要两处都设小值。

- [2026-08-09 22:30:58] Tuanjie 引擎 ParticleSystem API 差异：①`startColor.mode` 用 `ParticleSystemGradientMode.Color`（不是 `ParticleSystemCurveMode.Color`）②`NoiseModule` 没有 `dampen` 属性（编译报 CS1061）③`noise.strength` 需通过 `.constant` 访问。**How to apply:** 在 Tuanjie 引擎写 ParticleSystem 脚本时注意这些 API 名称差异。
- [2026-08-09 23:04:14] GIC 文档术语区分（2026-08-09 确定）：**战场**=局内网格战棋地图（20×20~50×50，地形/迷雾/宝箱，文档 03）；**大地图**=局外导航地图（MapScreen 区域切换/锚点传送，文档 13）。**Why:** 之前文档中两种地图都叫"地图"导致混淆。**How to apply:** 写文档或讨论时严格区分，局内用"战场"，局外导航用"大地图"，不要混用"地图"。
- [2026-08-10 00:25:19] Unity UI 关闭闪烁问题（Build-only）：从背包/设置/联机关闭返回大厅时全屏闪烁，编辑器不闪。根因是 MainHallScreen.UpdateBackgroundAsync 每次返回都 Addressables.Release 旧背景精灵再异步重载同一张，Release 到 Load 完成间的空窗期 SpriteRenderer 渲染空白→闪烁。修复：加 _lastBgAddress 缓存，地址相同且精灵已加载时跳过重载。毛玻璃 UIBlurCapture 是干扰项非根因。**Why:** 2026-08-10 排查，多次误判为毛玻璃材质切换/异步卸载残留帧，实际是 Addressables Release+Load 空窗期。**How to apply:** Build-only 闪烁优先检查异步资源加载的 Release→Load 空窗期，编辑器因缓存命中不暴露此问题。


### Project





- [2026-08-05 23:39:59] Game design documents in docs/ directory. "协议核心" is a simultaneous-turn card-based tactical wargame (原神IP, up to 6 players, LAN multiplayer via Mirror). Core loop: 祈愿解锁→局前选8种卡→同时选1行动→攻速排序执行→摧毁核心掠夺→原石结算. Key design: (1) 摩拉 is sole deployment currency, 5/turn auto, initial 200; (2) 7 factions mixable (蒙德延奏/璃月契约/稻妻连携/挪德双祝福命座/纳塔夜魂/须弥智慧/枫丹芒荒); (3) 命座0-3, 重复出战升命; (4) 战败=协议核心被摧毁→消散+观战; (5) 建筑=科技树节点, 买卡费用=出战费一半; (6) 初始6体力+半透明迷雾+3×3石路出生点; (7) 七元素反应系统(docs/06). **Why:** Tracks evolving game design. **How to apply:** Reference when implementing battle system, faction mechanics, or economy; design is actively iterating.

- [2026-07-28 09:14:44] GIC (协议核心 game project) targets全平台互通 (cross-platform: PC + mobile + console). Project name "GIC" derived from directory D:\Tuanjie_editor\gic.
- [2026-07-28 09:16:21] GIC project: solo developer (1人), personal Demo/portfolio goal, NOT commercial launch. Target audience: both Genshin players and strategy/tactical gamers. **Why:** Solo dev with limited resources — scope must be drastically cut from GDD ambition. **How to apply:** Recommend 2-player 1v1 over 6-player, 2-3 factions over 7, hotseat/local before Mirror networking, vertical slice over breadth.


- [2026-08-02 14:07:01] Unit data docs at docs/units/ (moved from docs/unit-data.md on 2026-08-02): Each character has its own MD file (安柏.md, 凯亚.md, etc.). Field reference & template at docs/units/_模板与字段说明.md. AI reads these files to sync UnitConfig.asset / SkillName enum / SkillParamKey enum / localization tables. **Why:** eliminates manual multi-file editing when adding/modifying characters — edit one MD, AI applies changes everywhere. **How to apply:** user edits docs/units/{name}.md, asks AI to apply; AI updates enum + UnitConfig.asset + localization tables per gic-localization skill.


- [2026-07-29 22:56:01] GIC battle system architecture decision (2026-07-29): Will use StS-style "Action Queue (coroutine, serial) + DamagePipeline (sync, Phase hooks)" pattern. EventBusHub/LocalEventBus retained for UI/network layer only — battle logic does NOT go through EventBus. Key mappings: AbstractGameAction→BattleAction(IEnumerator Execute()), addToBot→ActionQueue.Enqueue(), DamageInfo.applyPowers→DamagePipeline.Process(), AbstractPower hooks→IDamageHook+DamagePhase enum (PreDamage/Calculate/PostDamage/OnDeath), isDone→yield return. No R/D two-phase needed (sync pipeline makes it unnecessary vs Java mod's frame-driven queue). **Why:** User confirmed turn-based = strict serial, no concurrency. StS validates this exact pattern. **How to apply:** When implementing battle system, create ActionQueue + DamagePipeline as separate layer from existing EventBus.
- [2026-08-01 21:49:24] GIC 项目使用 ParrelSync 进行编辑器内联机测试（2026-08-01 安装）。位于 Packages/ParrelSync（从 umc 项目复制）。使用方法：菜单 ParrelSync/Preferences/Clone Manager → Clone current project → 两个 Editor 窗口都 Play，一个 Host 一个 Client。Clone 通过 Windows Junction 链接 Assets/Packages/Library，改代码实时同步。
- [2026-08-09 20:02:13] GIC 角色语音架构：语音数据内嵌在 UnitConfig.UnitData.voices (UnitVoiceData)，不独立 ScriptableObject。UnitVoiceData 包含 6 组通用 AudioClipRandom (onGoWar/onChooseHighHP/onChooseLowHP/onHitLight/onHitHeavy/onDie) + SkillVoiceEntry[] (按 SkillName 索引)。AudioClipRandom 已有权重+不连续重复。复用 AudioManager.PlayVoice 通道。语音文件在 Assets/Resources/Audios/Voices/{UnitName}/，命名: go_war_0.wav, choose_high_hp_0.wav, hit_light_0.wav, die_0.wav, {skillname_snake}_0.wav。Editor 工具 Tools/UnitConfig/自动加载语音 按文件名前缀匹配自动填充。

- [2026-08-05 23:39:43] GIC 术语区分：【消散】（带方括号）= 玩家战败时该玩家所有单位从游戏中真正删除，不可恢复；【放逐】= 转移到另一个维度，单位仍然存在，可被特定技能召回（如丽莎爆发蔷薇的雷光可召回被放逐的标记单位并承受真伤惩罚）。Buff/造物过期用"消失"，不用"消散"。丽莎文档中"标记所属单位【消散】"是正确用法（指玩家战败）。凯亚/芭芭拉文档中的"棱消散/环消散"旧措辞应改为"消失"以避免与游戏机制混淆。

















- [2026-08-09 20:02:45] GIC 祈愿系统架构：WishPoolConfig(ScriptableObject) 定义卡池角色/物品/概率 → WishManager 负责货币消耗+写入存档 → WishDrawController 控制 UI 动画。祈愿机制=反应速度游戏：卡道滚动出卡，玩家射击命中哪张卡获得哪张卡（不预抽），通过 Card.saveCardData 读取结果写入存档。卡池绑定：CharacterEntry.pool 直接引用卡池 asset，pool 为 null 则提示"卡池未开放"。卡池 asset 命名 WishPool_{势力英文名}.asset。**How to apply:** 新增卡池创建 WishPoolConfig asset 并拖到对应角色的 CharacterEntry.pool；UI 布局在场景 Canvas/WishDrawRoot 下改。


- [2026-08-09 11:35:34] GIC 构建配置（ProjectSettings.asset 可直接编辑）：测试用 Mono+ARMv7（快，约3分钟），发布用 IL2CPP+ARM64+Stripping Low（慢首次5-15分钟）。字段：scriptingBackend.Android(0=Mono,1=IL2CPP)、AndroidTargetArchitectures(1=ARMv7,2=ARM64)、managedStrippingLevel.Android(3=Low)。Android APK ~270MB vs Windows ~2GB 是正常的（APK ZIP压缩 + ASTC纹理 vs Windows 未压缩 + DXT纹理）。**Why:** 频繁切测试/发布配置。**How to apply:** 直接编辑 ProjectSettings.asset 改这三个字段，无需 execute_csharp_script（Tuanjie 引擎限制 EditorSettings/GraphicsSettings 类型不可用）。编辑器崩溃看 Editor.log：%USERPROFILE%\AppData\Local\Tuanjie\Editor\Editor.log；Windows 事件查看器查 Tuanjie.exe 异常码。
- [2026-08-10 00:49:23] GIC 快速导出 APK 工具：Tools/导出 APK/ 快速导出(Mono/ARMv7 测试) 或 正式导出(IL2CPP/ARM64 发布)。脚本在 Assets/_Scripts/Editor/Tool/QuickAPKBuilder.cs。**重要：构建前必须确保编辑器在 Android 平台**（脚本会自动切换）。**Why:** 不切换平台时 Addressables 会按 Windows 平台打包 bundle（DXT 纹理/Windows 路径），导致 Android 上黑屏+无声音+体积大 200MB（2026-08-10 排查）。脚本构建后不切回原平台，避免第二次纹理重导入。**How to apply:** 日常测试用"快速导出"，发布用"正式导出"。ADB 路径：D:\Tool\2022.3.62t11\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe。


- [2026-08-09 20:01:16] GIC 资源优化约定：BGM 用 Streaming loadType；Release 包用 `#if !UNITY_EDITOR Debug.unityLogger.filterLogType = LogType.Warning #endif` 抑制 Debug.Log；6 个 Sprite Atlas（UICommon/Icons/Avatars/NameCards/MapUI/WishUI，路径 Assets/SpriteAtlases/）。Tuanjie 引擎 SpriteAtlas API：扩展方法在 UnityEditor.U2D.SpriteAtlasExtensions，`atlas.Add(objs)` / `atlas.SetIncludeInBuild(true)` 是方法不是属性；textureType=8 是 Sprite。**How to apply:** 新增小 UI 贴图加入对应 Sprite Atlas；大贴图（4096²角色立绘/地图/背景）不入 Atlas。

- [2026-08-09 20:01:58] GIC 大资源加载架构：大贴图/背景/立绘通过 Addressables 按需加载（Assets/Art/ 下 WishArt/{name}、PositionBack/{Region}/{name}、Map/Textures），Configs 和小 Prefabs 留在 Resources.Load。Addressables 加载用 LoadAssetAsync 协程+OnDestroy Release。**How to apply:** 新增大贴图/背景/立绘放入 Assets/Art/ 对应目录并标记 Addressable；Configs 和小 Prefabs 留在 Resources。


- [2026-08-09 20:02:06] GIC 地图系统架构：MapConfig 配置驱动（非 Prefab）。MapConfig.asset（Resources/Configs/）存储各区域的 mapImageAnchoredPosition/contentAnchoredPosition/contentSizeDelta + 锚点列表。锚点使用归一化坐标(0-1)，0=图片左上，1=图片右下，图片尺寸变化自动适配。场景中静态结构：MapScreen.unity Canvas 下 MapRoot > Scroll View(SimpleMapZoom) > Viewport > Content > MapImage + Anchors。ShowRegion 读 Config 更新位置+创建锚点。**How to apply:** 新增/修改区域锚点在 MapConfig.asset Inspector 中编辑。


### Reference
- [2026-07-28 16:17:53] Localization CSV tool at Tools/Localization/CSV 导出导入 (created 2026-07-28). Exports/imports all 21 TableName localization tables to/from CSV ({TableName}.csv). CSV format: Key,Id,zh-Hans,zh-TW,en,ja,ru. RFC 4180 compliant (supports commas/quotes/newlines in values). Smart matching: Id first, then Key, then create new. **Why:** Unity Localization YAML is unicode-escaped and hard to batch-edit. **How to apply:** use this tool instead of Unity Localization Window for bulk edits; CSV folder defaults to Export/Localization.
- [2026-07-29 17:22:57] MC mod gichess (old project, Java/NeoForge): Java source at D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\ (308 files). Extracted jar at D:\Picture\gichess\my\wg-0.2.d. Architecture: EventBus (frame-driven queue, owner-order + priority sorting, LOCK animation blocks queue), SkillExecutionE.R/D two-phase events, DamageS.DEFFAULT (synchronous damage pipeline: element attach → reaction loop → formula → shield → HP → DamageA animation), AnimationManager (dual-channel: concurrent + serial-locked), ChessBoard.post() sets event order = firstRoles+secondRoles+board. ~20+ characters (Amber/Barbara/Kaeya/Lisa/Klee/Jean/Citlali/HaborymBoss etc), 7 element types + 18 reactions, Mondstadt延奏/Natlan夜魂 implemented. **Why:** validates GIC core gameplay and provides battle system architecture reference for Unity port. **How to apply:** reference for Unity系分 — map EventBus→EventBusHub(with owner ordering), LOCK animation→coroutine yield, DamageS→DamageCalculator(synchronous), AnimationManager→Unity Animator+VFX, SkillExecutionE.R/D→ActionRequest/ActionResult events.
- [2026-08-09 20:02:27] GIC MCP Server: Located at .codely-cli/mcp/gic-mcp-server/src/index.js. Provides 6 tools: query_units, query_skills, query_items, query_localization, query_unit_data, query_docs. Parses Unity YAML .asset files (UnitConfig.asset, ItemConfig.asset) and docs/ markdown. Configured in .codely-cli/settings.json under mcpServers.gic. Enum mappings: WeaponType starts at 0 (Claymore), FactionType uses large values (Celestia=1001, Nodkrai=2001, Mondstadt=3001, Liyue=4001...).

