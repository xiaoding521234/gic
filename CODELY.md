## Codely Structured Memories

### User


### Feedback








- [2026-08-14 22:58:07] 用户授权 AI 主动维护项目 skill：完成阶段性工作或发现高频工作流/陷阱密集的系统时，可自行新增/更新 .codely-cli/skills/ 下的 skill，无需逐次确认。**How to apply:** 主动沉淀但避免与项目记忆重复——记忆记事实与决策（为何），skill 记操作流程与检查清单（怎么做）；不确定价值时先问。













- [2026-08-28 01:23:14] [feedback] 【UI 组件克隆到 inactive 面板=Awake 不跑陷阱】（2026-08-28 派蒙形态下拉"点了没反应"实证）：克隆 UI 条目到**初始未激活**的分栏面板（SettingsScreen 各栏）时，组件的 Awake 从未执行——凡"Awake 缓存引用"模式（如 LocalizedDropdown.Awake 里 dropdown=GetComponent）全部静默失效：if(x==null) return 式守卫不报错，选项不重建/监听不挂/值不设=条目彻底假死。语言/帧率正常只因恰在初始激活的 Display 面板。**Why:** Unity 规则 inactive 物体不跑 Awake；克隆面板默认关闭。**How to apply:** ①新条目克隆到分栏面板后必须真实点击验证（显示对了≠回调通了）；②修法=引用改惰性属性（`Dd => dropdown != null ? dropdown : (dropdown = GetComponent<T>())`），已修 LocalizedDropdown（存量 bug 连原 Other 栏 petClose 一起治愈）。
- [2026-08-28 01:23:19] [feedback] DropdownSettingItem.Setup 的 defaultValue 框架语义=Initialize() 的显示值（非"新玩家默认"）——必须传当前存档值。传错则下拉打开即显示错项，用户点同一项 TMP_Dropdown 值不变不触发 onValueChanged=点了没反应（2026-08-27 派蒙形态首测踩坑）。另：Language/FrameRate/Resolution 全用 TextEntry(null, 静态文本)，PetForm 用 LocalizedString 是少数派——排查下拉问题时"参考帧率设置"做对照组很有效（用户教的方法：正常条目 vs 坏条目逐项 diff）。

- [2026-08-28 21:05:54] - [2026-08-28 21:06:00] [feedback] 桥包自动升级卡死恢复法（2026-08-28 实证）：cn.tuanjie.codely.bridge 自动更 1.0.77→1.0.78 后 .com-unity-codely.json 卡 "package_updating"/unity_port=-1/心跳停更，编辑器本身空闲无日志，ShowWindow 前置触发刷新无效——唯一恢复=优雅关闭编辑器（CloseMainWindow）再用 D:\Tool\2022.3.62t11\Editor\Tuanjie.exe -projectpath 重启，90s 内桥重新注册端口（ready）。**Why**: 新包 InitializeOnLoad 需要完整编辑器重启周期才写回配置。**How to apply**: 桥 reason=package_updating 且等待>2 分钟无恢复时直接重启编辑器（先确认场景 dirty=false）；勿反复 ShowWindow 空耗。
- [2026-08-28 23:42:41] - [2026-08-28 23:43:00] [feedback] 桌宠商业边界拍板（2026-08-28）：LLM 费用玩家自付——设置里提供 API Key 输入条目（玩家自己的 DeepSeek key），开发者不出钱不内嵌自己的 key；存档中 key 必须加密存储（防文本编辑器裸奔）。**Why:** 个人 Demo 分发场景，开发者代付 API 费不可持续；用户明确"让玩家自己输入自己的 key，而不是花费我的钱"。**How to apply:** 一切联网 AI 功能默认玩家自带 key+加密存档；接 DeepSeek（用户指定）；不要在代码/日志/异常栈出现明文 key。

- [2026-08-29 11:01:45] [2026-08-29 12:10:00] [feedback] 通用规则权威文档=docs/20-项目规范.md（2026-08-29 用户拍板建立，c5a21db 已提交）：编码命名/序列化铁律/编辑器红线/数据入口/UI 与本地化/文档维护/术语表/角色文档规范收拢于此。**Why:** 规范原先只活在 CODELY.md 记忆（不随 git 分发、人类协作者不可见）且散落各文档。**How to apply:** ①新通用规则写进 docs/20 对应小节（一行规则+指针到 skill/docs/14，不复制细节）；②CODELY.md 记忆退回记事实与决策背景；③四类分流=工作流→skill、陷阱→docs/14、决策→设计文档、通用规则→docs/20；④新会话读文档顺序=docs/17（架构）→docs/20（规范）。
- [2026-08-29 16:41:22] [feedback] Skill 触发条件必须写得宽（2026-08-29 拍板"给所有 skill 触发条件改宽"并已全量执行）：description 要覆盖①口语化说法（用户说"改文案"而非"本地化"、"放个音乐"而非"BGM"）②症状式描述（"图加载不出来/点了没反应/为 null"）③兜底句"凡话题涉及X即激活，不确定时也激活"。**Why:** skill 激活=模型语义匹配且只看 description，措辞覆盖窄就漏激活（gic-localization 多次实证）。**How to apply:** 新建/修改任何 SKILL.md 按此三件套写触发条件；用户指出漏激活时先 activate 再干活。

### Project
- [2026-08-16 19:13:23] GIC"协议核心"：同时回合制卡牌战术战棋（原神IP，最多6人，LAN联机 via Mirror）。核心循环：祈愿解锁→局前选8卡→同时选1行动→攻速排序执行→摧毁核心掠夺→原石结算。7势力可混搭，命座0-3重复出战升命。**定位**：单人开发的个人 Demo/作品集，非商业上线；目标全平台互通（PC+移动+主机）。**Why:** solo dev 资源有限，scope 必须从 GDD 雄心大幅裁剪。**How to apply:** 实现战斗/势力/经济系统时参考 docs/；优先 2人1v1 而非 6 人、2-3 势力而非 7、垂直切片优先于铺量。




- [2026-08-15 21:42:58] GIC 祈愿系统三层架构（2026-08-14 状态机重构后）：WishPoolConfig(SO) 卡池权重 → WishManager 货币+存档 → WishFlowController（纯C#，State: Idle/Drawing/Revealing/Finished）PlanShot() 一次性预计算全部业务返回 WishShotResult（有序 WishRevealStep 列表）→ WishDrawController.PlayStepsCoroutine() 纯动画播放零业务调用。**Why:** 旧代码 6 个 bool 标志位管时序，3 个 bug 全源于业务逻辑与动画协程交错。**How to apply:** 新增卡池建 WishPoolConfig asset 拖到 CharacterEntry.pool；新增动画步骤在 PlanShot 加 step + PlayStepsCoroutine 加 case，不要在表现层调 WishManager；UI 布局在 WishScreen 场景 Canvas/WishDrawRoot 下改；机制数值见 docs/12。
- [2026-08-10 00:49:23] GIC 构建/APK：测试用 Tools/导出 APK/快速导出（Mono+ARMv7，约3分钟），发布用正式导出（IL2CPP+ARM64+Stripping Low）。脚本 Assets/_Scripts/Editor/Tool/QuickAPKBuilder.cs 自动切 Android 平台——**必须切**，否则 Addressables 按 Windows 打包（DXT/Windows 路径）→ 真机黑屏无声音+体积大 200MB；构建后不切回原平台。也可直编 ProjectSettings.asset 三字段：scriptingBackend.Android(0=Mono,1=IL2CPP)、AndroidTargetArchitectures(1=ARMv7,2=ARM64)、managedStrippingLevel.Android(3=Low)。APK ~270MB vs Windows ~2GB 正常。ADB：D:\Tool\2022.3.62t11\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe；编辑器崩溃看 %USERPROFILE%\AppData\Local\Tuanjie\Editor\Editor.log。









- [2026-08-16 19:13:35] [project] Assets 清理定案（2026-08-16）：Material/Materials/Resources/Materials 三目录 8 个材质曾误删已全部 git 恢复，**勿再清理**（UIBlur/WishSmoke 确认在用，其余存疑也保留）；已删定案：Assets/Editor 空目录、Scenes/TestScene.scene、StreamingAssets/mc.mp4、Assets/AI/PRJ_SUMMARY.md、Assets 根 6 个 CUsers* 垃圾文件。故意不做：NewAudioMixer、Art/Wish 与 WishArt 改名（Addressables 地址约定）；保留 GlowTest.scene+CardGlowTest.cs。**How to apply:** 不再动这批资产。


- [2026-08-27 14:49:55] GIC 大地图 v7.0 基线：110 片母版原生像素瓦片（Export/all_map_source.jpg 21900×18576）+ 全链路统一 SourcePixel×unit 换算（unit=0.0099860），换图错位已根治；跳非当前区域首跳冷加载糊 1-3 秒——**用户已拍板保持现状**（全预载≈220MB / 原神式 LOD 均否决）。**How to apply:** 换图/标定/新区域先读 gic-map skill；状态快照见 .codely-cli/HANDOFF-大地图.md；细节 docs/13 §9.1、docs/14 §8.2.1。

- [2026-08-27 14:50:02] TextMesh Pro/ 目录被 git 忽略（材质改动不入库，重装 TMP 时需备份该目录），排查它时 rg/Select-String 需 --no-ignore 才能搜到。










- [2026-08-29 14:03:31] [2026-08-29 11:30:00] 命名规范最终拍板并执行完毕（2026-08-29 用户："统一用英文，除非不得不用中文" + "字段都统一用英文，加上 InspectorName 表现为中文"）：所有标识符统一英文；序列化字段=英文名+[InspectorName("中文原名")]。存量中文标识符全部迁移完成（含 4 个遗漏字段 目标动画/惯性化器/气泡贴图/输入底贴图 已补齐），场景/prefab 已重保存（旧中文键→英文键），FormerlySerializedAs 已全部删除。历史遗留英文 FormerlySerializedAs 保留不动：WishPoolConfig 6 处（旧键已消失=冗余）+ CoopScreen 5 处（旧键仍在 CoopScreen.unity，删则丢数据）。规则本体=docs/20 §1.1。**遗留**：PetPrefs pet.json DTO 字段已英文化（JsonUtility 键变，旧测试存档重输 key）；Data 层枚举成员（SkillName.派蒙/FactionType.蒙德 等）保持中文待专项。





- [2026-08-27 14:45:48] Unity 场景 YAML 中文序列化字段名陷阱+改名工作流：.unity 里中文字段名序列化为 \uXXXX 转义键——rg/Select-String 按中文键搜场景文件必零命中，验证迁移要么搜转义形式、要么按 fileID 搜；判引用完整用 SerializedObject.FindProperty(中文名).objectReferenceValue。**改名铁律**：序列化字段改名必挂 [FormerlySerializedAs("旧名")]，验证链=编译管线→FindProperty 断言引用非 null→保存场景固化新名→OpenScene 重开二次断言→磁盘 YAML 确认旧名消失（转义形式）。**How to apply:** 本项目 Inspector 中文字段遍布，任何字段改名都走此链。




- [2026-08-27 23:05:38] 桌宠构建/编辑器实测事实：①编辑器没开则桥死心跳（unity_refresh 拒连）——自救链=拉起编辑器（实测路径 D:\Tool\2022.3.62t11\Editor\Tuanjie.exe -projectpath 'D:\Tuanjie_editor\gic'，可最小化后台起）→ Editor.log 出现 CompileScripts → unity_refresh 重连；重启后心跳仍 stale 时 PowerShell ShowWindow 前置编辑器窗口触发资产刷新即恢复（2026-08-27 实证）；残留无窗口 tuanjie 进程是许可/Hub 后台件，勿误判。②BuildPlayer 可成功覆盖正在运行的桌宠 exe（构建前不必杀旧进程，启动前必须杀）。③legacy Animation.Play() 播完的 Once clip 重播 time 正确归零从头播——排查切换类动画 bug 勿再疑"重播冻结在结尾姿势"此路。**How to apply:** 闭环遇心跳停先查进程再用编辑器路径拉起；构建协议细节见 gic-pet / gic-editor-longtask skill。








- [2026-08-27 14:49:34] 桌宠动作切换/丝滑度优化已暂停（2026-08-27 用户两次拍板"暂时不修改了"；已提交 8f909b3，"动作期间内"不顺畅遗留未修）——后续会话勿主动重开此题；用户重开时先开 PetInertializer.打印诊断 看 Player.log 捕获行，数据说话（细节见 gic-pet skill 铁律 10）。

- [2026-08-27 14:49:40] .codely-cli/ 与 .codely/ 在 .gitignore 中——skill/HANDOFF 等文件不进 git，属本地持久（勿尝试提交它们）；只有 docs/ 与 Assets/ 等变更进 git。





- [2026-08-27 23:04:07] [project] Unity prefab 化往返污染陷阱（2026-08-27 PaimonInGameRoot.prefab 化实证）：SaveAsPrefabAsset 前把场景物体收进临时公共根、完成后再还原——还原后场景仍出现 327 行 RectTransform 锚点/SizeDelta 序列化噪声（m_AnchorMin 0.5→0、SizeDelta 100→0 之类，Parent/Reparent 往返触发）；处置=git checkout 场景 + OpenScene 强制重载（铁律 6）+ 抽查中文字段值确认无损。**Why:** 噪声会混进提交污染历史。**How to apply:** 任何"临时改层级→存 prefab→还原"操作后，git diff 场景必须逐类检查，非零 diff 且全为锚点类行=直接还原重载。
- [2026-08-29 11:19:20] Tuanjie SpriteAtlas API 陷阱：`atlas.Add(objs)` / `atlas.SetIncludeInBuild(true)` 是方法不是属性（UnityEditor.U2D.SpriteAtlasExtensions）。Atlas 清单与入图规则见 docs/20 §1.4。
- [2026-08-29 11:19:24] RoomManager.Cleanup 仅作应用关闭钩子，勿在断线时调用（曾致同会话重连名册失联）。联机分层规则（PlayerManager 数据层/RoomManager 流程层、网络 Handler 常驻订阅不退订）已收拢 docs/20 §1.2。
- [2026-08-29 11:19:28] 场景脚本接线深拷贝铁律（P10 三次返工教训）：跨场景预设含场景级 override 子物体的面板必须 Object.Instantiate(场景实例) 深拷贝；静态预设实例保存为未激活时，运行时使用前必须显式 SetActive(true)。其余（PrefabUtility 禁用/SerializedObject 赋值写法/保存重开断言闭环/编辑器红线）已收拢 docs/20 §1.3、§1.5。
- [2026-08-29 11:19:31] 桌面版任务栏置顶守卫（2026-08-28 目检通过，a7f4320）：0.5s 查 Z 序上方 8 步内 Shell_TrayWnd/Shell_SecondaryTrayWnd→重挂 HWND_TOPMOST——只对任务栏触发，不打置顶战争；根因=点击任务栏激活被抬进 topmost 链我们之上（VPet 无此守卫属场景未暴露）。游戏内坐参数与 y=0 视口陷阱已记 docs/19 §6.4。
- [2026-08-29 11:19:35] 【TMP_InputField 程序化构建赋值顺序陷阱】（2026-08-29 派蒙聊天 UI 两连 NRE 实证）：TMP 3.0.9 的 fontAsset/pointSize setter（SetGlobalFontAsset/SetGlobalPointSize，源码 L4593/L4605）无条件解引用 textComponent（placeholder 有判空、textComponent 没有）——程序化建输入框赋值顺序必须 textComponent→placeholder→fontAsset→pointSize。**How to apply:** 程序化 TMP 构建照此顺序；派蒙对话系统架构/密钥链路见 docs/19 §6.5，聊天 UI 其余陷阱（锚点/九切片/拖拽 NRE）见 docs/14 §14。
- [2026-08-29 19:12:21] [project] 游戏内派蒙"点不了拖不动"已修（2026-08-29）：根因=d3149aa 引入的 SendBtn 构建代码在已有 Image 的物体上再 AddComponent<TextMeshProUGUI>（一 GameObject 一 Graphic）→失败返回 null→NRE 穿透 PetInGameHostController.Awake→**Unity 官方行为：Awake 抛异常=禁用组件**→Update（全部输入轮询）停摆。修复=①文字挪 SendBtn 子物体 Label（TMP+TextCombiner 同挂 Label）②接聊天() 加 try-catch（聊天失败只禁聊天组件，宿主交互永不陪葬）。**How to apply:** 程序化 UI 文字一律挂子物体；宿主 Awake 内可选功能调用必须 try-catch 防泄漏。同日实证：exec_runtime_script 进 Play（Boot 全游戏启动）卡死编辑器主线程（CPU 满烧+日志零增长+桥任务堆 28 个）→唯一恢复=强杀重启编辑器；纯 UI 构建验证用编辑模式直调 WireHost 即可（3 秒），已记入 gic-pet skill。

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
