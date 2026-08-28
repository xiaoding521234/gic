## Codely Structured Memories

### User


### Feedback
- [2026-08-01 00:22:21] GIC 项目不使用 asmdef（2026-08-01 决定）。曾尝试拆分 Framework/UI/Battle/Editor，但相互间存在大量循环依赖，硬拆需引入大量接口。**Why:** 单人开发、个人 Demo，asmdef 收益不足以抵消重构成本。**How to apply:** 保持单一程序集 + 命名空间（GIC.Framework/GIC.UI/GIC.Battle/GIC.Editor）做逻辑隔离，不主动推进 asmdef 拆分。
- [2026-08-02 23:43:50] 角色属性命名参考 HP，不加 Max 前缀：baseEnergy（不是 baseMaxEnergy）、StatType.Energy。baseEnergy 是基础值=上限，角色登场当前元能为 0 是运行时逻辑，不在 UnitStats.Init 中处理。**Why:** 与 HP 设计完全一致——baseHP 既是基础值也是上限，当前血量战斗中往下扣；元能同理从 0 往上加。**How to apply:** 新增属性不加 Max 前缀，Init 中直接 SetBaseValue，运行时当前值管理留给战斗系统。
- [2026-08-03 13:26:42] 通用设计规则应写在总设计文档中（如 docs/08-命座系统.md），不应写在个别角色文档里。角色文档只记录该角色的具体数据。**How to apply:** 涉及多角色的通用规则/概念，写入对应总设计文档（命座系统→08、行动系统→05），不要在角色文档中重复。
- [2026-08-09 00:19:47] 技能参数非 Fixed 基准的值是百分比，不是固定值。"伤害 40 BasedOnAttack" = 40%×攻击力。**Why:** 用户纠正过 AI 把百分比当固定值导致数值分析全部错误。**How to apply:** 读取角色文档技能参数表时，非 Fixed 一律按百分比理解（代码 SkillParam.GetDisplayValueText 中 value+"%"）。
- [2026-08-09 23:04:14] GIC 术语区分：**战场**=局内网格战棋地图（文档 03）；**大地图**=局外导航地图（MapScreen 区域切换/锚点传送，文档 13）。**How to apply:** 写文档或讨论时严格区分，不要混用"地图"。
- [2026-08-10 14:39:36] 目录名保持英文，不使用中文目录名。**Why:** 项目目录全为英文，混入中文目录不一致且可能遇编码问题。**How to apply:** 新建目录一律英文命名。
- [2026-08-10 14:53:26] 角色文档属性规范：采用默认值的字段只写 auto，UnitConfig.asset 对应填 -64（Unspecified 哨兵），只有非默认值才写显式数字。默认值参考 UnitConfig.GetEffective*() 方法。**How to apply:** 编辑 docs/units/{name}.md 时按此执行。
- [2026-08-14 11:31:36] UI 文本本地化必须统一使用 TextCombiner 组件，不要 `tmp.text = GetLocalizedString()` 直赋（语言切换不刷新）。完整规则与例外（TMP_InputField placeholder）见 gic-new-screen skill UI 规范节。
- [2026-08-14 22:58:07] 用户授权 AI 主动维护项目 skill：完成阶段性工作或发现高频工作流/陷阱密集的系统时，可自行新增/更新 .codely-cli/skills/ 下的 skill，无需逐次确认。**How to apply:** 主动沉淀但避免与项目记忆重复——记忆记事实与决策（为何），skill 记操作流程与检查清单（怎么做）；不确定价值时先问。
- [2026-08-18 01:26:36] [feedback] git checkout 回滚 Unity 资产后必须强制重载编辑器内已打开的场景（EditorSceneManager.OpenScene 同路径 Single），否则磁盘已还原但 SceneView 仍是旧画面，易在错位现场二次返工。**Why:** OpenScene 才丢弃内存态，git 只动磁盘（2026-08-17 标定事故实证）。**How to apply:** 任何对"编辑器中已打开场景文件"的 git 恢复操作，收尾必须 OpenScene 重载+断言现场。



- [2026-08-26 01:23:30] [feedback] 项目文档维护规范（2026-08-26 用户指示"一些不必在文档里，一些可能不可信"后确立，docs/19 重写实证）：①文档只记"现在是什么+为什么"（现状定案/参数/资产位置/决策记录），过程流水与已废弃路线排查细节移交 git 历史——迁移式文档（每次改动往顶部堆一段新记录）几轮后必成 300+ 行流水账且新旧结论混杂不可信；②**可信度分级标注**：【实证】=有实测/源码核验、【目检】=用户确认过、未标注=当前实现记录；③被推翻的旧结论不删原文，原地加"❌已推翻+正确结论"标记（防后人翻 git 旧版再信）；④技术细节按职责分流：操作工作流进 skill、陷阱进 docs/14、设计决策进 docs——文档间不重复。**Why:** 用户发现 docs/19 存了大量已废弃路线的过程记录（GI 提取三轮反复）和后来被推翻的结论（基准高 18.38 幻影），文档越长越不可信。**How to apply:** 给 GIC 写任何 docs/ 文档时按此规范；新条目改对应小节而非顶部堆叠。

- [2026-08-29 00:07:44] [feedback] 【本地化 .asset 禁手编 YAML+UIText 统一分段编号】（2026-08-28 事故+拍板）：①Node 脚本手编 UIText Shared Data 的 m_Id 撞出重复 Id（PetApiKey/PetApiKeyNotSet 同 id）→ Localization 反序列化器整表失败 → 五语言表全空壳=全游戏"未在UIText找到翻译"；修复链=git checkout 还原+逐文件 ImportAsset(ForceUpdate) 清内存态（只还原磁盘不清内存=坏态复活）+走 API 加键。②UIText Id 规范拍板=**统一续编分段编号**（1000通用/2000联机/3000稀有度/4000区域/5000颜色/6000地图/7000元素/8000移动/9000设置/10000祈愿；新键=域内最大序号+1，AddKey(key, id) 带参）；存量大数键（KeyGenerator 自动分配）不动；RemapId 只改 SharedData 不跟表——需"RemapId+各表删旧加新+清孤儿"三步。**How to apply:** 改 UIText 一律走 gic-localization skill 的 API 路径（已更新含全部细节）。

- [2026-08-28 14:15:30] [feedback] 【DontDestroyOnLoad 实例的 Camera tag 抢占 Camera.main】（2026-08-28 游戏内派蒙致大厅背景缩到极小实证）：PaimonInGameRoot.prefab 的 PreviewCamera 遗留 tag=MainCamera，常驻 DontDestroyOnLoad 实例进入主场景后 `Camera.main` 可能返回它而非场景 Main Camera；BackgroundParallax3D.FitToScreen 用 `Camera.main.orthographicSize` 算铺屏缩放，派蒙相机 orthoSize=0.7 vs 大厅相机 5.0→背景缩到 ~1/7=极小。**Why:** Unity `Camera.main` 在多个 MainCamera tag 时返回顺序未定义，编辑器与构建可能不同。**How to apply:** 任何 DontDestroyOnLoad 实例上的 Camera 必须改 tag=Untagged（运行时加 CompareTag 安全带防 prefab 回滚）；排查"构建里背景/UI 异常但编辑器正常"先查 Camera.main 被谁抢了。




- [2026-08-28 01:23:14] [feedback] 【UI 组件克隆到 inactive 面板=Awake 不跑陷阱】（2026-08-28 派蒙形态下拉"点了没反应"实证）：克隆 UI 条目到**初始未激活**的分栏面板（SettingsScreen 各栏）时，组件的 Awake 从未执行——凡"Awake 缓存引用"模式（如 LocalizedDropdown.Awake 里 dropdown=GetComponent）全部静默失效：if(x==null) return 式守卫不报错，选项不重建/监听不挂/值不设=条目彻底假死。语言/帧率正常只因恰在初始激活的 Display 面板。**Why:** Unity 规则 inactive 物体不跑 Awake；克隆面板默认关闭。**How to apply:** ①新条目克隆到分栏面板后必须真实点击验证（显示对了≠回调通了）；②修法=引用改惰性属性（`Dd => dropdown != null ? dropdown : (dropdown = GetComponent<T>())`），已修 LocalizedDropdown（存量 bug 连原 Other 栏 petClose 一起治愈）。
- [2026-08-28 01:23:19] [feedback] DropdownSettingItem.Setup 的 defaultValue 框架语义=Initialize() 的显示值（非"新玩家默认"）——必须传当前存档值。传错则下拉打开即显示错项，用户点同一项 TMP_Dropdown 值不变不触发 onValueChanged=点了没反应（2026-08-27 派蒙形态首测踩坑）。另：Language/FrameRate/Resolution 全用 TextEntry(null, 静态文本)，PetForm 用 LocalizedString 是少数派——排查下拉问题时"参考帧率设置"做对照组很有效（用户教的方法：正常条目 vs 坏条目逐项 diff）。
- [2026-08-28 14:15:33] - [2026-08-28 14:15:00] [feedback] 【ScreenBlurRendererFeature 必须按 targetTexture==null 过滤相机】（2026-08-28 游戏内派蒙开启后毛玻璃黑屏实证）：URP ScriptableRendererFeature 对所有 Game 类型相机跑——RT 相机（派蒙 PreviewCamera）的输出（模型+透明黑背景）模糊后覆盖全局 `_ScreenBlurTex`，UIBlur 面板全采样它=黑屏。**How to apply:** 任何给全局 Shader 纹理赋值的 RendererFeature 都要按 `renderingData.cameraData.targetTexture == null` 过滤（只屏幕相机参与，RT 相机跳过）。
- [2026-08-28 21:05:54] - [2026-08-28 21:06:00] [feedback] 桥包自动升级卡死恢复法（2026-08-28 实证）：cn.tuanjie.codely.bridge 自动更 1.0.77→1.0.78 后 .com-unity-codely.json 卡 "package_updating"/unity_port=-1/心跳停更，编辑器本身空闲无日志，ShowWindow 前置触发刷新无效——唯一恢复=优雅关闭编辑器（CloseMainWindow）再用 D:\Tool\2022.3.62t11\Editor\Tuanjie.exe -projectpath 重启，90s 内桥重新注册端口（ready）。**Why**: 新包 InitializeOnLoad 需要完整编辑器重启周期才写回配置。**How to apply**: 桥 reason=package_updating 且等待>2 分钟无恢复时直接重启编辑器（先确认场景 dirty=false）；勿反复 ShowWindow 空耗。
- [2026-08-28 23:42:41] - [2026-08-28 23:43:00] [feedback] 桌宠商业边界拍板（2026-08-28）：LLM 费用玩家自付——设置里提供 API Key 输入条目（玩家自己的 DeepSeek key），开发者不出钱不内嵌自己的 key；存档中 key 必须加密存储（防文本编辑器裸奔）。**Why:** 个人 Demo 分发场景，开发者代付 API 费不可持续；用户明确"让玩家自己输入自己的 key，而不是花费我的钱"。**How to apply:** 一切联网 AI 功能默认玩家自带 key+加密存档；接 DeepSeek（用户指定）；不要在代码/日志/异常栈出现明文 key。
- [2026-08-29 00:07:51] - [2026-08-27 14:11:10] [feedback] Unity 改代码默认值≠运行生效：字段一旦序列化进场景/Prefab，场景值恒优先于代码默认值——"改了默认值构建后行为没变"时第一嫌疑就是场景旧值（2026-08-27 拖拽阈值 6→3 两轮构建无变化实证，写入场景序列化值后立即生效）。**Why:** Unity 反序列化规则：场景有值用场景值，代码默认值仅在字段首次序列化时进场景。**How to apply:** 改任何 Inspector 暴露字段的代码默认值时，同步用 SerializedObject 写场景值（或确认场景本无该字段）；排查"参数没生效"先查场景 YAML（中文键搜 \uXXXX 转义形式）。

### Project
- [2026-08-16 19:13:23] GIC"协议核心"：同时回合制卡牌战术战棋（原神IP，最多6人，LAN联机 via Mirror）。核心循环：祈愿解锁→局前选8卡→同时选1行动→攻速排序执行→摧毁核心掠夺→原石结算。7势力可混搭，命座0-3重复出战升命。**定位**：单人开发的个人 Demo/作品集，非商业上线；目标全平台互通（PC+移动+主机）。**Why:** solo dev 资源有限，scope 必须从 GDD 雄心大幅裁剪。**How to apply:** 实现战斗/势力/经济系统时参考 docs/；优先 2人1v1 而非 6 人、2-3 势力而非 7、垂直切片优先于铺量。
- [2026-08-16 19:13:28] GIC 战斗系统采用 StS 风格架构：ActionQueue（协程串行）+ DamagePipeline（同步，Phase hooks）。EventBusHub/LocalEventBus 仅用于 UI/网络层，战斗逻辑不走 EventBus。**Why:** 回合制=严格串行无并发，StS 验证了此模式。**How to apply:** 战斗批次 B1-B4 开工前先读 docs/18-战斗系统决策记录.md（Host 权威等已拍板）与 docs/16 末节批次表。
- [2026-08-01 21:49:24] GIC 用 ParrelSync 做编辑器内联机测试：菜单 ParrelSync/Preferences/Clone Manager → Clone current project → 两个 Editor 都 Play，一个 Host 一个 Client。Clone 通过 Windows Junction 链接 Assets/Packages/Library，改代码实时同步。
- [2026-08-14 10:15:28] GIC 角色语音数据内嵌在 UnitConfig.UnitData.voices (UnitVoiceData)，不独立 ScriptableObject，复用 AudioManager.PlayVoice 通道。语音文件在 Assets/Resources/Audios/Voices/{UnitName}/，Editor 工具 Tools/UnitConfig/自动加载语音 按文件名前缀匹配自动填充。**How to apply:** 新增角色语音按命名规范放入对应目录，用工具自动加载。
- [2026-08-15 21:43:55] GIC 术语区分：【消散】= 玩家战败时该玩家所有单位真正删除不可恢复；【放逐】= 转移到另一维度，仍可被特定技能召回（如丽莎爆发蔷薇的雷光）。Buff/造物过期用"消失"，不用"消散"。**How to apply:** 写角色技能/设计文档时严格按此区分。
- [2026-08-15 21:42:58] GIC 祈愿系统三层架构（2026-08-14 状态机重构后）：WishPoolConfig(SO) 卡池权重 → WishManager 货币+存档 → WishFlowController（纯C#，State: Idle/Drawing/Revealing/Finished）PlanShot() 一次性预计算全部业务返回 WishShotResult（有序 WishRevealStep 列表）→ WishDrawController.PlayStepsCoroutine() 纯动画播放零业务调用。**Why:** 旧代码 6 个 bool 标志位管时序，3 个 bug 全源于业务逻辑与动画协程交错。**How to apply:** 新增卡池建 WishPoolConfig asset 拖到 CharacterEntry.pool；新增动画步骤在 PlanShot 加 step + PlayStepsCoroutine 加 case，不要在表现层调 WishManager；UI 布局在 WishScreen 场景 Canvas/WishDrawRoot 下改；机制数值见 docs/12。
- [2026-08-10 00:49:23] GIC 构建/APK：测试用 Tools/导出 APK/快速导出（Mono+ARMv7，约3分钟），发布用正式导出（IL2CPP+ARM64+Stripping Low）。脚本 Assets/_Scripts/Editor/Tool/QuickAPKBuilder.cs 自动切 Android 平台——**必须切**，否则 Addressables 按 Windows 打包（DXT/Windows 路径）→ 真机黑屏无声音+体积大 200MB；构建后不切回原平台。也可直编 ProjectSettings.asset 三字段：scriptingBackend.Android(0=Mono,1=IL2CPP)、AndroidTargetArchitectures(1=ARMv7,2=ARM64)、managedStrippingLevel.Android(3=Low)。APK ~270MB vs Windows ~2GB 正常。ADB：D:\Tool\2022.3.62t11\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe；编辑器崩溃看 %USERPROFILE%\AppData\Local\Tuanjie\Editor\Editor.log。
- [2026-08-14 10:15:33] GIC 资源优化：BGM 用 Streaming loadType；Release 包抑制 Debug.Log；6 个 Sprite Atlas（UICommon/Icons/Avatars/NameCards/MapUI/WishUI，Assets/SpriteAtlases/）。Tuanjie SpriteAtlas API 陷阱：`atlas.Add(objs)` / `atlas.SetIncludeInBuild(true)` 是方法不是属性（UnityEditor.U2D.SpriteAtlasExtensions）。**How to apply:** 新增小 UI 贴图加入对应 Atlas，大贴图不入 Atlas。
- [2026-08-14 23:10:40] GIC 存档删档测试模式已安全化：开关在菜单 Tools/存档/删档测试模式（EditorPrefs "GIC.SaveManager.DeletionTestMode"，持久化），打包版本恒为关闭。**How to apply:** 开发迭代需每次启动删档时勾选；测试存档持久性时取消勾选。
- [2026-08-15 21:44:35] GIC 存档数据层统一入口：卡牌库存 PlayerSaveData.AddOwnedUnit(card)/AddOwnedItem(card)（内部自动 RebuildOwnedCards 失效缓存）；货币/物品计数 GetItemCount/AddItemCount/TryConsumeItem。**Why:** 曾修复 WishManager.AddStarglitter 直接 ownedNormalItems.Add 漏调重建导致背包缓存过期的真 bug。**How to apply:** 新代码一律走这些入口，不要直接 ownedUnits/ownedNormalItems.Add 或手写遍历。
- [2026-08-15 00:19:46] GIC 场景跳转仅三条存活路径：LoadSceneWithConfig（Single）、PreloadScene+ActivatePreloadedScene（Additive 预加载）、GoBack（历史栈，只记 Additive）。弹窗/轻提示统一走 PopupManager.Instance.ShowToast/ShowModalPopup；显示设置应用走 SettingsApplier.ApplyFromSave。**How to apply:** 新场景跳转只用这三条路径+SceneType.Load()；弹窗不要走 GameScene。新建 Screen 流程见 gic-new-screen skill。
- [2026-08-15 09:37:21] GIC 联机管理层（P6 拆解后）：PlayerManager=纯数据层（名册+SelfPlayerID+Register/Set 系列+查询，[Component] 无构造依赖）；RoomManager=流程层（[Component] 构造注入 SaveManager+PlayerManager：连接/断开、11 个网络 Handler、OnKickedFromRoom、Cleanup）。单向依赖 RoomManager→PlayerManager。**How to apply:** 新增房间流程/网络 Handler 放 RoomManager，玩家数据字段/查询放 PlayerManager；网络 Handler 常驻订阅不退订（CanHandle 守卫，见 gic-eventbus skill）；RoomManager.Cleanup 仅作应用关闭钩子，勿在断线时调用（曾致同会话重连名册失联）。
- [2026-08-18 01:11:50] GIC 场景脚本接线与深拷贝铁律（P10 三次返工教训）：①跨场景预设含场景级 override 子物体的面板必须 Object.Instantiate(场景实例) 深拷贝；静态预设实例保存为未激活时，运行时使用前必须显式 SetActive(true)。②不要用 PrefabUtility.Set/GetPropertyModifications 往返清理——SetPropertyModifications 整体替换修改列表会静默丢新引用 mod；赋值用 SerializedObject.FindProperty().objectReferenceValue=xxx + ApplyModifiedPropertiesWithoutUndo()。③脚本改完场景必须"保存→CloseScene→重开→断言字段非 null"闭环。**编辑器安全红线：execute_csharp_script 禁止 LoadAssetAtPath 整 prefab 后递归遍历层级、禁止 Play 模式下跑重反射诊断、禁止 GetWindow 泛型参数传 null（均曾卡死主线程 330s）。**
- [2026-08-16 19:13:35] [project] Assets 清理定案（2026-08-16）：Material/Materials/Resources/Materials 三目录 8 个材质曾误删已全部 git 恢复，**勿再清理**（UIBlur/WishSmoke 确认在用，其余存疑也保留）；已删定案：Assets/Editor 空目录、Scenes/TestScene.scene、StreamingAssets/mc.mp4、Assets/AI/PRJ_SUMMARY.md、Assets 根 6 个 CUsers* 垃圾文件。故意不做：NewAudioMixer、Art/Wish 与 WishArt 改名（Addressables 地址约定）；保留 GlowTest.scene+CardGlowTest.cs。**How to apply:** 不再动这批资产。
- [2026-08-16 11:21:24] [project] Tuanjie 1.9.3 加密资产 GUID 机制（删错材质事故实证）：部分资产（tag:yousandi.cn 新建）meta 里的 base64 guid 是**密文**，真实 hex GUID 不出现在 meta 文本中，场景/Prefab 引用的是真实 hex GUID。**Why:** 文本 GUID 审计（meta 提取 guid 全文检索）对加密资产必然误判"零引用"。**How to apply:** 判定资产是否被引用禁止用文本检索，用 unity_asset get_info 查真实 GUID（或 AssetDatabase.GetDependencies）；真实 hex GUID 可再对场景文本 Select-String 确认。

- [2026-08-27 14:49:55] GIC 大地图 v7.0 基线：110 片母版原生像素瓦片（Export/all_map_source.jpg 21900×18576）+ 全链路统一 SourcePixel×unit 换算（unit=0.0099860），换图错位已根治；跳非当前区域首跳冷加载糊 1-3 秒——**用户已拍板保持现状**（全预载≈220MB / 原神式 LOD 均否决）。**How to apply:** 换图/标定/新区域先读 gic-map skill；状态快照见 .codely-cli/HANDOFF-大地图.md；细节 docs/13 §9.1、docs/14 §8.2.1。

- [2026-08-27 14:50:02] TextMesh Pro/ 目录被 git 忽略（材质改动不入库，重装 TMP 时需备份该目录），排查它时 rg/Select-String 需 --no-ignore 才能搜到。










- [2026-08-27 14:50:08] 桌宠命名规范第2批待用户拍板：方法命名规则（推荐 A=保持现状——Unity 生命周期+被 onClick 持久绑定/跨组件 API 用英文、领域流程用中文；含 PaimonDropShadowController 整文件英文方法的处置）。第1批（日志标签 [PetSpike]→[PetWindow]、PetAnimSwapper 序列化字段中文化）已落地。**How to apply:** 用户答复前不主动改方法名；若拍板 A，新代码按此隐性分层执行。

- [2026-08-27 14:45:48] Unity 场景 YAML 中文序列化字段名陷阱+改名工作流：.unity 里中文字段名序列化为 \uXXXX 转义键——rg/Select-String 按中文键搜场景文件必零命中，验证迁移要么搜转义形式、要么按 fileID 搜；判引用完整用 SerializedObject.FindProperty(中文名).objectReferenceValue。**改名铁律**：序列化字段改名必挂 [FormerlySerializedAs("旧名")]，验证链=编译管线→FindProperty 断言引用非 null→保存场景固化新名→OpenScene 重开二次断言→磁盘 YAML 确认旧名消失（转义形式）。**How to apply:** 本项目 Inspector 中文字段遍布，任何字段改名都走此链。




- [2026-08-27 23:05:38] 桌宠构建/编辑器实测事实：①编辑器没开则桥死心跳（unity_refresh 拒连）——自救链=拉起编辑器（实测路径 D:\Tool\2022.3.62t11\Editor\Tuanjie.exe -projectpath 'D:\Tuanjie_editor\gic'，可最小化后台起）→ Editor.log 出现 CompileScripts → unity_refresh 重连；重启后心跳仍 stale 时 PowerShell ShowWindow 前置编辑器窗口触发资产刷新即恢复（2026-08-27 实证）；残留无窗口 tuanjie 进程是许可/Hub 后台件，勿误判。②BuildPlayer 可成功覆盖正在运行的桌宠 exe（构建前不必杀旧进程，启动前必须杀）。③legacy Animation.Play() 播完的 Once clip 重播 time 正确归零从头播——排查切换类动画 bug 勿再疑"重播冻结在结尾姿势"此路。**How to apply:** 闭环遇心跳停先查进程再用编辑器路径拉起；构建协议细节见 gic-pet / gic-editor-longtask skill。








- [2026-08-27 14:49:34] 桌宠动作切换/丝滑度优化已暂停（2026-08-27 用户两次拍板"暂时不修改了"；已提交 8f909b3，"动作期间内"不顺畅遗留未修）——后续会话勿主动重开此题；用户重开时先开 PetInertializer.打印诊断 看 Player.log 捕获行，数据说话（细节见 gic-pet skill 铁律 10）。

- [2026-08-27 14:49:40] .codely-cli/ 与 .codely/ 在 .gitignore 中——skill/HANDOFF 等文件不进 git，属本地持久（勿尝试提交它们）；只有 docs/ 与 Assets/ 等变更进 git。
- [2026-08-27 21:31:10] [project] 桌宠边缘坐（2026-08-27 目检通过已提交）：PetEdgeSitController 挂 PetWindow——松手磁吸坐窗口顶边/任务栏（上120/下60/水平160px）；判定基准=骨盆；窗口顶边用 DWMWA_EXTENDED_FRAME_BOUNDS 可见帧；锚定窗口消失 eSheep 式重力掉落；坐定纯坐；坐姿下缩放=切站立重坐。坐姿=合成 SitUpright（原 SitLoop 定性"生病坐姿"弃用）。eSheep 一手源码快照 .codely-cli/tmp/esheep_formpet.cs。设计细节 docs/19 §6.2+gic-pet skill 已同步。

- [2026-08-27 16:13:28] [project] 桌宠边缘坐二轮修正（2026-08-27）：①判定基准骨盆（屁股）非脚线（用户目检"腿沉入窗下"）——PetWindowController.TryGet接触点屏幕位置 用 Bip001 Pelvis 投影；落座后 坐定贴正秒=1.2s 逐帧钉骨盆（站→坐过渡平滑）。②坐定中纯坐（用户拍板）：打招呼/小动作全压制（边坐坐定中）。③SitLoop 情绪 Sleepy 已清空（默认脸）。④磁吸范围调大：上120/下60/水平160（序列化值）。⑤SitLoop 是"生病坐姿"，候选库=out_kanban 过场动画（59 条 Ani_Cs_），大腿抬角曲线分析（sit_find.cjs/sit_windows.cjs 在 .codely-cli/tmp/）筛出 C01/C02/C04/C09/C10 已导入注册（C09 疑似坐姿晃腿 85°±30°），等用户编辑器测试面板挑选后剪循环段替换。 [project] GI 过场动画（Ani_Cs_）导入陷阱：m_Legacy:0 会被 PetSceneSyncTool 的 c.legacy 过滤器静默跳过（同步日志"注册108"看着正常实际没进）——Cs 系导入必须先 node 把 m_Legacy 0→1 再 ImportAsset(ForceUpdate) 再跑同步。
- [2026-08-27 21:31:10] [project] SitUpright 直坐动画（2026-08-27 v3 目检通过，生成器 .codely-cli/tmp/make_sit_upright.cjs）：上身=Standby 重采样到 SitLoop 122键时间轴（2.017s 统一循环）；腿=SitLoop 原样；手=SitLoop 手势（上臂世界取向 Δ 补偿，链=根⊗骨盆⊗脊⊗脊1⊗锁骨全链，漏锁骨差7°）；骨盆 y=0.214 不变。重生成链：node 生成器→哈希修复菜单→gi_tangent_batch→gi_pos_center→ImportAsset→SyncInternal→构建。 [project] GI .anim 解析两大坑（2026-08-27 踩坑实证）：①path 是 YAML 折叠多行标量——"path:...Clavicle/Bip001"+下一行"      L UpperArm"(6空格无冒号)折叠为完整路径，行级解析器必须折续行否则 65 块按截断路径去重塌成 28 块（Unity 本身解析正常，宠物一直没坏）；②切线批处理后键=serializedVersion:3 格式（9行/键含 tangentMode/weightedMode/weights），重建块按 v2 单行键定位必失败（旧键不删新键追加=244键）。另 PS5.1 内联 node -e 转义坑再现（$ 需 \\$），一律写 .cjs 文件跑。
- [2026-08-27 21:56:22] [project] Pet 运行时代码（Assets/_Scripts/Pet/）故意用 Debug.Log 而非 GICLog.Info（2026-08-27 代码质量梳理时定案）：桌宠是 Development 构建，[PetAnim]/[PetInertia]/[PetWindow] 等 Player.log 行是既定诊断方法论（skill 铁律 10），GICLog.Info 的 [Conditional] 会在构建版编译期删除这些行=破坏诊断链。**Why:** P2 曾做过"日志全仓统一 GICLog"，Pet 代码是后来写的、有意豁免。**How to apply:** 勿对 Pet 运行时做 GICLog 一致性替换；确要静默需改用 GIC_LOG 宏方案并同步 gic-pet skill。另：2026-08-27 已做质量梳理——PetWin32 共享互操作（新窗口子系统一律从这里取声明）、PetMeshQuery、巨型 Update 拆分（纯搬移零行为变化），细节见 gic-pet skill 核心定位节。
- [2026-08-27 23:04:07] [project] Unity prefab 化往返污染陷阱（2026-08-27 PaimonInGameRoot.prefab 化实证）：SaveAsPrefabAsset 前把场景物体收进临时公共根、完成后再还原——还原后场景仍出现 327 行 RectTransform 锚点/SizeDelta 序列化噪声（m_AnchorMin 0.5→0、SizeDelta 100→0 之类，Parent/Reparent 往返触发）；处置=git checkout 场景 + OpenScene 强制重载（铁律 6）+ 抽查中文字段值确认无损。**Why:** 噪声会混进提交污染历史。**How to apply:** 任何"临时改层级→存 prefab→还原"操作后，git diff 场景必须逐类检查，非零 diff 且全为锚点类行=直接还原重载。


- [2026-08-28 14:53:45] [project] 游戏内派蒙持续迭代中（docs/19 §6.4）：①2026-08-28 下午共用化重构——抽 PetHostBase 基类（拎起姿势应用/四肢摆动/命中烘焙/缩放平滑/共享序列化字段；实证 [SerializeField] 字段按名移入基类场景/Prefab 引用与调参值全存活），用户拍板"两形态能共用则共用"为持久设计原则，后续新宿主功能优先落基类；②三修复：游戏内松手回待机（物理结束帧空分支漏切）、退场播完当帧销毁（behavior.enabled 永真空等 4s 陷阱→退场完成回调+行为层冻结末帧+宿主冻结交互）、桌面边坐补窗口底边（身体在窗前腿垂窗下，掉落仍只落顶边）。**等用户复测**：底边坐、游戏内松手/退场、共用化回归（桌面拖拽/缩放/边坐）。已知边界：游戏失焦 Input 冻结=拖拽/视线停住。
- [2026-08-28 21:06:38] - [2026-08-28 21:20:00] [project] 游戏内派蒙坐三轮修正+任务栏遮挡修复（2026-08-28 全部目检通过已提交）：a7f4320（坐参数对齐桌面版+坐标轴反转修复+置顶守卫）、1006438（PetHostBase 共用化+下边框坐+三修复）。要点：①游戏内坐=磁吸上120/下60px 像素语义+坐线=屏底上方4.5%≈任务栏顶+贴正1.2s（prefab 序列化值已写）；②Unity 视口 y=0 是屏底（教训：屏幕归一化判定先对齐 y 轴方向，误写 1-偏移曾致拖底不坐/拖顶瞬移）；③桌面版置顶守卫=0.5s 查 Z 序上方 8 步内 Shell_TrayWnd/Shell_SecondaryTrayWnd→重挂 HWND_TOPMOST（只对任务栏触发不打置顶战争；根因=点击任务栏激活被抬进 topmost 链我们之上；VPet 无此守卫属场景未暴露）。
- [2026-08-28 23:06:31] - [2026-08-28 23:10:00] [project] 派蒙对话系统开工（2026-08-28 晚批次）：①游戏内坐定缩放重坐已移植（坐定中滚轮→切站立→稳定0.3s→重坐贴正，待目检）；②agent 框架网检拍板=方案A 自研薄层（C# HttpClient 直连 DeepSeek OpenAI 兼容端点；**模型名换代：deepseek-chat/reasoner 已于 2026-07-24 停用，现用 deepseek-v4-flash**；SSE 三通道解析（reasoning/content/tool_calls 分片拼接）；strict 模式走 /beta 端点；记忆=memory_update 工具（Mem0 式）+稳定人设前缀吃缓存；拒绝 SK/MAF/MEAI——net8+/System.Text.Json 与 Unity .NET Standard 2.1+IL2CPP 冲突无先例）；③核心代码已落：Assets/_Scripts/Pet/Chat/DeepSeekClient.cs（SSE 流式+工具调用）+PaimonChatSession.cs（人设+滑窗历史+长期记忆 JSON 持久化），编译零错误；④原神风格九切片气泡素材已生成入库 Assets/Art/PaimonPet/UI/（ChatBubbleReply 1070x381 border55 + ChatBubbleInput 1061x202 border52/44；加工链=frontier 图生图→process_bubble2.cjs 黑转透明+包围盒裁切+中心色泛洪填平→TextureImporter 设 border；frontier 输出的"透明"实为纯黑不透明 RGBA(0,0,0,255)，alpha 包围盒裁切前必须先黑转透明）。**待用户拍板**：气泡风格目检、框架确认、DEEPSEEK_API_KEY 环境变量、坐定缩放目检。剩余：游戏内/桌面版对话 UI（共用素材）+首批游戏指令工具接线。
- [2026-08-28 23:42:33] - [2026-08-28 23:45:00] [project] 派蒙 LLM 对话功能开工（docs/19 §6.5，6 任务批次）：①缩放重坐移植✅待目检（坐定中滚轮→站立→0.3s 稳定→原水平位重坐）；②agent 框架网检拍板=方案A 自研薄层直连 DeepSeek（C# HttpClient+SSE+FC JSON）——C# 框架（SK/MAF/MEAI/LangChain.NET）依赖 System.Text.Json+net8/10 与 Unity .NET Standard 2.1+IL2CPP 正面冲突不可嵌；**DeepSeek 已换代：deepseek-chat/reasoner 2026-07-24 停用，现用 deepseek-v4-flash（默认）/v4-pro**，FC 全支持含 /beta strict 模式，1M 上下文，晚间半价≈¥0.004/轮；记忆=滑窗+FC 式 memory_update 工具（Mem0 模式）+稳定人设前缀吃缓存，不上向量库；Spring AI（Java sidecar）用户问过已否决（架构错配）；③气泡素材✅=Assets/Art/PaimonPet/ChatUI/pet_chat_bubble_9slice.png（768×256 九切片 border=56 FullRect，seedream 生成两轮+Node 抠黑转透明，尾巴星形装饰后续贴图层）；④API Key 设置✅=设置派蒙栏 ButtonSettingItem+InputPopupDialog 输入，AES-128-CBC+设备指纹派生密钥加密存 PlayerSaveData.petApiKeyCipher（PetApiKeyCrypto，篡改/跨机器按未设置处理；防呆不防专家边界已注），五语言键已加；待做：DeepSeek 接入层（任务6，先行）+游戏内/桌面版对话 UI（任务4/5，复用气泡）。

### Reference
- [2026-08-14 10:16:21] MC mod gichess（旧项目，Java/NeoForge）：源码 D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\（308文件），jar D:\Picture\gichess\my\wg-0.2.d。~20+角色，7元素18反应，蒙德延奏/纳塔夜魂已实现。**Why:** GIC 战斗系统 Unity 移植的架构参考。**How to apply:** 需要查旧 Java 实现时按路径阅读源码。
- [2026-08-10 09:28:39] 技术陷阱与 Bug 修复记录：详见 docs/14-技术陷阱与Bug修复记录.md。
- [2026-08-16 10:58:59] GIC 文档体系：**docs/17-代码架构指南.md 是新会话入口文档**（目录结构/核心系统速查表/场景清单/配置资产/常用工作流速查/战斗规划/环境备忘），开工先读它再按需深入；docs/00-12 玩法设计、13 大地图、14 技术陷阱、15 输入系统、16 优化史、18 战斗决策。**How to apply:** 新系统落地/结构性变化后更新 docs/17 对应小节保持时效。
- [2026-08-18 01:26:24] GIC 未建 skill 的系统：祈愿（记忆覆盖三层架构）、APK 导出（记忆覆盖操作要点）、战斗系统（未实现，落地后建）——大地图已于 2026-08-18 建 gic-map skill。Android 包名 com.HGAME.gic；切平台重置问题已由 PackageNameGuard 启动守卫根治（2026-08-21，docs/14 §12），改包名改守卫常量+菜单立即校验，无需提交前人工核对。
- [2026-08-17 10:32:10] 本机 ffmpeg：D:\Tool\FormatFactory\ffmpeg.exe（7.1 完整构建）。PATH 里没有 ffmpeg，媒体处理直接用此路径。已用它重编码 mc_16x10.mp4 消 VideoPlayer WMF 双告警（baseline profile 消时间戳；VUI+容器双写 bt709 消色彩；x264-params 与 -color_* 需同时给）。
- [2026-08-18 01:26:41] 大地图资料分两处：操作工作流与铁律在 gic-map skill（.codely-cli/skills/gic-map/SKILL.md）；.codely-cli/HANDOFF-大地图.md 只留状态快照+待拍板清单。做大地图任务先读 skill。
- [2026-08-23 23:20:00] 派蒙桌宠资料分两处：操作工作流与铁律在 gic-pet skill（.codely-cli/skills/gic-pet/SKILL.md，修复→构建→启动→用户目检闭环、建成判据只信 Editor.log RESULT 行、构建竞态守卫勿删、双进程注册表/存档隔离、编辑器红线）；设计/架构/子系统状态见 docs/19-AI派蒙.md。做桌宠任务先读 skill。
- [2026-08-21 12:10:00] [reference] 音频响度体系（docs/14 §11 + gic-audio skill 响度规范节）：位置 BGM 响度基线白天 ≈-11.5 LUFS / 夜晚 ≈-13.5 LUFS；平台下载 OST 与游戏内提取音源天然差 2~6 LUFS，提取版入库前必须两遍 loudnorm 对齐（ffmpeg 本机路径 D:\Tool\FormatFactory\ffmpeg.exe）；峰值归一≠响度归一。音频系统问题（音乐冷却/Push/Pop/响度）先读 gic-audio skill。
- [2026-08-21 19:05:00] [reference] AI派蒙桌宠资产与工具链位置（细节见 docs/19）：模之屋 PMX 原包 D:\Tool\PaimonModel\（**定案为唯一模型基底**）；原神本体 D:\Game\Genshin Impact\Genshin Impact Game\；AnimeStudio 提取工具 D:\Tool\AnimeStudio\（AssetMap maps\gi70.json + 看板动作 out_kanban\）——**提取路线已放弃**（GI 网格反优化无解、动作与 MMD 骨骼不通），仅备查不再投入；项目入库 Assets/Art/PaimonPet/（MMD 模型在用；24 个 GI 动作弃用备查）；PMX→FBX=Blender 4.2.3+mmd_tools v4.5.13（VMD 动作也走此链）。**How to apply:** 做桌宠任务先读 docs/19；动画来源在 VMD/手 K/AI 中选，不再研究 GI 提取。
- [2026-08-26 23:43:45] [reference] 编辑器长任务（构建/烘焙/批量导入 >1分钟）一律走异步协议：Schedule 入口毫秒级返回+后台 PowerShell 基线计数轮询 Editor.log、期间零桥调用——详见 skill gic-editor-longtask（2026-08-26 定案）。现有入口：PetSpikeBuildTool.ScheduleBuild。做任何编辑器耗时操作前先读该 skill。
- [2026-08-27 10:40:17] [reference] 惯性化参考源码位置：D:\Tuanjie_editor\gic\.codely-cli\tmp\InertializationForUnity\（InertiaAlgorithm.cs=五次多项式+四元数/向量惯性化数学、PostInertializer.cs=Animator 后处理用法、PostInertializerTransitionProfile.cs、README.md）。上游 github.com/portalmk2/InertializationForUnity（MIT，GoW4 SIGGRAPH 2017 supplemental 论文直译实现）；一手信源=GDC 2018 talk "Inertialization: High-Performance Animation Transitions in 'Gears of War 4'"（gdcvault.com/play/1025331）+ GoW4 SIGGRAPH 2017 supplemental PDF。本项目实现 PetInertializer.cs 已大幅分叉（固定系数多项式形态+帧间 dq 速度捕获防 ±2π 抽搐+起步拉引），原源码仅作数学对照用。GitHub 拉源码方法（2026-08-27 实测）：api.github.com 直连可用但 archive zip/git clone 被重置——走 API contents 端点取 base64 解码落盘。**How to apply:** 改惯性化数学/移植其它 GoW 技术时先读这份源码对照。
