## Codely Structured Memories




### User
- [2026-09-11 23:12:13] [2026-09-11] [user] 用户 Tuanjie AI 订阅=个人版 Max（月积分 160,000；三层滚动上限 5h=8,000/周=40,000/月=160,000；闲时=每日除 11:00-12:00、14:00-18:00 外，积分消耗减半即 token 翻倍；订阅积分月度发放不结转，增值包积分 365 天有效不清零；**增值包是否计入 5h/周/月速率上限文档未写明**，2026-09-11 核对定价页上限表口径为"每个套餐…可使用的积分上限"，倾向计入但不确证，建议撞限想靠增值包续命时先问客服）。**Why:** Max=付费订阅用户，TJGenerators 的订阅路由/高成本门按付费用户处理；周上限 40,000 是最常撞的节流阀（2026-09-11 用户实证撞周上限）。**How to apply:** 大批量生成（视频/3D/Pro 图）前先估积分是否撞周上限；被上限卡住时建议挪闲时（消耗减半）；计费规则页=codely-docs.tuanjie.cn /subscription/pricing-details。


### Feedback








- [2026-08-14 22:58:07] 用户授权 AI 主动维护项目 skill：完成阶段性工作或发现高频工作流/陷阱密集的系统时，可自行新增/更新 .codely-cli/skills/ 下的 skill，无需逐次确认。**How to apply:** 主动沉淀但避免与项目记忆重复——记忆记事实与决策（为何），skill 记操作流程与检查清单（怎么做）；不确定价值时先问。
















- [2026-09-12 23:21:29] 【桥/编辑器异常恢复】①编辑器没开/桥死心跳：拉起编辑器（D:\Tool\2022.3.62t11\Editor\Tuanjie.exe -projectpath 'D:\Tuanjie_editor\gic'）→ 等 Editor.log 出现 CompileScripts → unity_refresh 重连；心跳仍 stale 用最小化+还原焦点循环（SW_MINIMIZE=6→2s→SW_RESTORE=9+SetForegroundWindow）触发。②CLI 连不上桥第一排查=版本错位：桥包 1.0.81 心跳写 Temp/.com-unity-codely.json，CLI 读项目根同名文件→根文件冻结即永判未连接、**编辑器重启无效**（2026-09-12 复核仍错位：根文件停 9/10、Temp 每日新鲜）；排查序=netstat+对比两心跳文件时间戳→**Copy-Item Temp 版到项目根**做副本桥接（重启后 Temp 端口变即副本过期，需重新同步）→仍不行才考虑重启。③编辑器 License 过期弹窗：点 Exit 重启编辑器自动续期；重启前先确认场景 dirty=false。④exec_runtime_script 结束 Play 会话/域重载后，桥推送 state=stale + custom_tools_reloaded 通知属**常态**（2026-09-12 P1-P2 连续多轮实证）：unity_refresh 重连即可继续，勿当故障走排查流程。







- [2026-08-29 11:01:45] [2026-08-29 12:10:00] [feedback] 通用规则权威文档=docs/20-项目规范.md（2026-08-29 用户拍板建立，c5a21db 已提交）：编码命名/序列化铁律/编辑器红线/数据入口/UI 与本地化/文档维护/术语表/角色文档规范收拢于此。**Why:** 规范原先只活在 CODELY.md 记忆（不随 git 分发、人类协作者不可见）且散落各文档。**How to apply:** ①新通用规则写进 docs/20 对应小节（一行规则+指针到 skill/docs/14，不复制细节）；②CODELY.md 记忆退回记事实与决策背景；③四类分流=工作流→skill、陷阱→docs/14、决策→设计文档、通用规则→docs/20；④新会话读文档顺序=docs/17（架构）→docs/20（规范）。
- [2026-08-29 16:41:22] [feedback] Skill 触发条件必须写得宽（2026-08-29 拍板"给所有 skill 触发条件改宽"并已全量执行）：description 要覆盖①口语化说法（用户说"改文案"而非"本地化"、"放个音乐"而非"BGM"）②症状式描述（"图加载不出来/点了没反应/为 null"）③兜底句"凡话题涉及X即激活，不确定时也激活"。**Why:** skill 激活=模型语义匹配且只看 description，措辞覆盖窄就漏激活（gic-localization 多次实证）。**How to apply:** 新建/修改任何 SKILL.md 按此三件套写触发条件；用户指出漏激活时先 activate 再干活。






- [2026-09-02 01:06:08] [feedback] 【现状判定三验法】（2026-09-02 战斗骨架复审实证）：据项目 docs 规划或宣称"断链/必炸"前必须三验：①全库搜调用方（区分死代码/活代码——UnitFactory.CreateUnitWithData 零调用=断链潜伏而非现行 bug，勿说"必报错"）②`git log --all --diff-filter=ADR` 查资产是否曾存在（NormalUnit 全历史零命中=计划名当常量写死的漂移，非改名遗漏）③资产实存+脚本 GUID 反查组件挂载（Unit.prefab 实挂齐 Unit+8 组件）。**Why:** 用户两次纠偏："该文档不可信，根据现在实际更新"+"再次复审避免误判"——docs/17 §7 战斗现状原记载与代码实际有出入（0 个技能子类/SkillConfig.asset 缺失/UnitFactory 路径断链均靠核验发现）。**How to apply:** 项目 docs 的"现状"章节只当索引起点，开工/下结论前以代码为准逐文件核验；"必炸"类结论尤其要先查调用方。


- [2026-09-12 14:24:31] 【编辑器脚本必须幂等】unity_refresh 恢复后编辑器脚本可能被重放执行（AddKey 型第二遍会新建同 key 重复条目、追加型会重复 append）——AI 直改本地化/资产的编辑器脚本一律带幂等守卫：加键前查重、追加文本前 Contains 判跳、全量重写型天然幂等；输出逐条报 exists/already/rewritten 便于核对实际生效遍数。细节 docs/14 §8.2.1。


- [2026-09-12 14:24:31] 【UI 图标风格=原神级极简】AI 生成 UI 图标验收对标原神：单一简单物体+单色细线（米白/暖金）+对称+大量留白+无填充，禁实心色块与复合元素起步；已否决方向=复合元素主体（法阵+星、剑盾交叉类）、两色实心剪影+负空间镂空；风格存疑先出 1 张试方向再批量。


- [2026-09-12 14:24:31] 【TJGenerators/AI 生成工具两坑】①generate_image 用 frontier/seedream_pro 等高成本模型不带 confirm_cost=true 直接调用会报 "[Error: Could not parse tool response]"——是高成本确认门解析失败的假象，非网络/参数问题；用户已明确要最强模型/同意成本时直接带 confirm_cost=true，勿无脑重试同参。②generate_* 返回的 poll_command_powershell 内 `function Try` 与 PowerShell 保留字 try 冲突，原样执行必解析失败——下发子代理执行前将 Try 函数改名（如 TryPoll），逻辑照抄。




- [2026-09-05 22:26:03] 【"看似冗余"的执行路径可能是隐式行为的载体，删除前必查副作用】（2026-09-05 用户报障实证）：LoadSaveData 首建档分支"少 return、建档后又把刚写的档读一遍"被我判为"无害但浪费的 bug"顺手修掉——实际它承载了"建档后 fall-through 顺带 SyncMissingCards 补全未拥有卡"的功能；删掉后 v11 重置当次会话背包缺未拥有角色卡（count=0 条目全无），用户立即发现。**Why:** 旧代码的怪写法常是历史行为的载体，"重构清理"前必须先问"它为什么这么写"；行为保持型重构 ≠ 顺手修 bug。**How to apply:** ①判定既有代码"冗余/bug"并打算顺手移除时，先证明它不承载功能（调用面/数据流推演），证不了就保留原样或单独提问；②改存档建档/重置路径必须过 gic-save-system skill 的"建档即补全"铁律；③回归修复已入 CreateNewSave（SyncMissingCards+SortAllCategories）。



- [2026-09-12 14:24:31] 【技能/角色图标素材一律取官方】（2026-09-09 用户拍板）任何图标需求先走本地游戏提取（gic-gi-extract skill）；Fandom/网络图库抓取链已弃用；AI 生成仅限官方无对应的项目自创技能且需用户点头（规则本体已入 docs/20 §2、落地流程=gic-gi-extract「角色技能图标落地流水线」）。


- [2026-09-12 14:24:31] 【识图 AI 不可作内容判定依据】（2026-09-10 用户拍板原话"不要相信识图的ai，经常不准确"）：多模态识图仅粗检参考，不得作"图案是什么/箭头是否残留/内容对不对"类结论证据；内容判定=像素级测量（连通域/diff/ASCII 渲染，工具箱=gic-gi-extract skill §4b）+用户目检；识图与像素数据冲突时信像素，最终以用户目检为准。
- [2026-09-12 18:14:38] [feedback] 【设计拍板会标准循环】（2026-09-12 战斗系统拍板会全程验证）：AI 先读文档+核代码 → 逐议题给分析/选项/推荐 → 编号清单列文末；用户逐条拍或质疑深挖（质疑常揭出真设计问题，如"同攻速为什么还要排序"→ 引出片内快照 vs 串行语义分水岭）；后续轮次把剩余项**浓缩表格复列**（已拍项划掉/移除），便于批量拍板；用户可整体授权（"剩余的拍板，按照你的建议决定"）→ 按推荐方案全量落档。落档一次完成：修订决策文档（docs/18）+ 建技术设计（docs/22）+ 缺口登记（docs/11）+ 更新导航/架构指针（docs/00/17）。**Why:** 零 ask_user 打断、三轮讨论产出完整技术设计，效率获用户认可。**How to apply:** GIC 后续大系统开稿（C 批次/新系统设计）沿用此循环。
- [2026-09-12 19:05:04] [2026-09-12] 【AI 功能设计对齐大厂框架】用户拍板"尽量参考大厂的ai，比如 Spring AI 等"——做 AI/检索/agent 类新功能时，先网检主流框架的组件划分（如 Spring AI RAG 的 Document/DocumentRetriever/ETL/Advisor 四件套），抽象对齐业界再做自研实现，并在代码注释/文档里写明映射关系。**Why:** 2026-09-12 派蒙知识库落地时用户明确要求（工具式检索即 Spring AI Advisor 的 agentic 等价物）。**How to apply:** 后续 AI 类新系统开稿时，设计段先给"业界框架组件↔本项目组件"对照表；与既有"先网检拿一手信源"铁律配合使用。
- [2026-09-13 00:18:02] 【UI 结构改动交付必附目检清单+报障活体诊断法】P2 面板化连续 4 回归实证（关闭残骸/入场瞬完成/闪现一帧/尖峰全屏闪烁，docs/14 §37+§38），P3 再添两例（祈愿按钮全消失=目标位池化二次缓存污染 §39、毛玻璃"反而更亮"=双层渲染顺序 §38b）：结构化断言全绿≠视觉正确——6 个回归全部通过 19~36 项断言，均靠用户目检发现。**Why:** 断言盲区客观存在（组件级查不出 Canvas 残骸、时序类查不出渲染闪烁、位置类查不出动画到不了位）。**How to apply:** ①UI 结构类改动交付时主动列目检清单（开有动画无闪烁/关彻底消失返回上级/反复开关正常/层级遮挡正确/关键控件在位），不等用户撞上；②**用户报障且编辑器还开着时，先用 exec_runtime_script 反射读取现场状态取证**（anchoredPosition/缓存目标位/层级顺序/激活态）再推理——§39 靠"三个目标位恰为序列化位±200"一发实锤，远快于理论推演。
- [2026-09-13 01:35:13] 系统性碎片化债务的处置偏好=趁项目小做"业界最优"大重构，优先于最小收敛（2026-09-13 原话"我支持大重构，趁现在项目还小，采用业界最优方案"；手势层议题我列 A 不动/B 最小收敛/C 大一统三案并推荐 B，用户拍 C）。**Why:** 与 UI P1-P4 大重构、桌宠窗口体制照 VPet/eSheep 源码逐条重写一脉相承——趁债务未滚大一次性对齐业界。**How to apply:** 诊断出"多处手写同族机制"类碎片化时，方案清单把业界最优大重构给足权重（勿只推最小收敛）；三条护栏不变——既有已调参数保留覆写、调研先行拿一手信源、每阶段目检交用户。

### Project
















- [2026-09-12 14:24:31] Assets 清理定案（2026-08-16）：Material/Materials、Resources/Materials 三目录 8 个材质误删已 git 恢复，**勿再清理**（UIBlur/WishSmoke 在用，其余存疑也保留）；已删定案：Assets/Editor 空目录、Scenes/TestScene.scene、StreamingAssets/mc.mp4、Assets 根 6 个 CUsers* 垃圾文件。故意不做：NewAudioMixer、Art/Wish 与 WishArt 改名（Addressables 地址约定）；保留 GlowTest.scene+CardGlowTest.cs、兹白测试皮肤 zibai_skin_solid.png（UnitConfig 兹白 cards[1]，用户拍板不删勿当 bug 修）。**How to apply:** 不再动这批资产。










































































- [2026-09-12 14:20:49] 【搜索/文本判定铁律】search_file_content/glob/list_directory 受 .codelyignore+.gitignore 双层 ignore（Assets 的 png/prefab/asset/mat/wav、Mirror/kcp2k/Plugins 整目录、.codely-cli/.codely/Library/Temp/Logs/Export/Maps 等）——被任一层命中即**静默零命中/无名**，零命中≠不存在：查引用/资产内容/目录清单/skill 文件一律原生 `rg --no-ignore` 或 Get-ChildItem；喂多模态的 Assets 图片先复制到 .codely-cli/tmp；guid 判定一律 AssetPathToGUID/unity_asset get_info（meta guid 可能密文，文本反查假阴性，docs/14 §9）；.unity 与本地化表中文为 \uXXXX 转义，rg 中文直搜必零命中（搜转义形式或先解码，docs/20 §1.3）；.codely-cli/ 与 TextMesh Pro/ gitignored（skill/HANDOFF 勿提交；TMP 材质改动不入库，重装前备份）。细节 docs/14 §6.2。
- [2026-09-12 19:05:04] [2026-09-12] 【exec_editor_script 桥脚本 Newtonsoft 撞名坑】桥内联脚本里用 `Newtonsoft.Json.Linq.JObject` 必报 CS0433（JObject 同时存在于 Newtonsoft.Json 与 Unity.Localization.ThirdParty.Editor 两程序集，脚本环境无法 extern alias）。**Why:** 2026-09-12 烘焙验证脚本实证。**How to apply:** 桥内联脚本一律不用 JObject.Parse/ JsonConvert——计数/取字段用 Regex.Match 原生字符串，或返回原始 JSON 字符串在 AI 侧解析；项目内 .cs 编译不受影响（只用一份）。
- [2026-09-13 02:03:21] 手势输入层大重构进行中：**P1 已落地（11e66f9）+ P2 Map 迁移已落地（9b1e92e，2026-09-13）**——P1=GestureMetrics（全局统一常量，用户拍板推翻"保留覆写"：触摸/鼠标双档 slop 24/4px、150ms 按住升级入表）+识别器四件套+GestureHub/PointerInputPump（Wargame 管线）；P2=MapCameraController 迁移为 surface（Drag Immediate+ShortTap 复合/Pinch；HandleMouse/HandleTouch 双轨与 clickMoveThreshold=12 删除；AnyPointerBegan 事件保抓停语义；Pinch 补中点参数+晚起手）。断言 36 组全绿。**P3 Battle+点击继续×4+SkillDetailView 改名/P4 游戏内桌宠待做**，蓝图=docs/24 §6。Win32 桌宠独立=桌面进程跳过 Wargame 的架构必然。调研信源=.codely-cli/webrefs/gesture-input/（notes/gesture-system-research_2026-09-13.md，动手前先读）。**Why:** 指针手势碎片化——点击/拖拽判别手写 4 处两套、IsPointerOverUI 撞名 3 份、点击继续×4、鼠标/触屏双轨；用户拍板趁项目小做业界最优大重构+阈值全局统一。**How to apply:** 实施以 docs/24 §6 阶段计划为准，每阶段一 commit+目检清单交用户；阶段完成更新本条与 docs/17 手势层行。



### Reference
- [2026-09-05 20:04:54] MC mod gichess（旧项目，Java/NeoForge）：源码 D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\（308文件），jar D:\Tuanjie_editor\gic\.codely-cli\webrefs\genshin-unpack\gichess\my\wg-0.2.d（2026-09-05 随 gichess 77.78GB GI 解包归档整体迁入 webrefs/genshin-unpack/，原 D:\Picture\gichess）。~20+角色，7元素18反应，蒙德延奏/纳塔夜魂已实现。**Why:** GIC 战斗系统 Unity 移植的架构参考。**How to apply:** 需要查旧 Java 实现时按路径阅读源码。

- [2026-08-10 09:28:39] 技术陷阱与 Bug 修复记录：详见 docs/14-技术陷阱与Bug修复记录.md。
- [2026-09-13 00:42:25] GIC 文档体系：**docs/17-代码架构指南.md 是新会话入口文档**（目录结构/核心系统速查表/场景清单/配置资产/常用工作流速查/战斗规划/环境备忘），开工先读它再按需深入；docs/00-12 玩法设计、13 大地图、14 技术陷阱、15 输入系统、16 优化史、18 战斗决策、19 AI派蒙、20 项目规范（规范权威）、21 商业化与发布边界、23 UI架构重构技术设计。**How to apply:** 新系统落地/结构性变化后更新 docs/17 对应小节保持时效。**2026-09-13 更新：UI 重构 P1-P4 已全量收官**（15 提交：UIManager 统一栈+池化面板，四屏 prefab 化，场景只剩 Boot/Splash/MainHall/Battle/Map+PaimonPet；面板制纪律全部沉淀 docs/14 §37-39b 与 gic-new-screen skill）。**两条已否决的"故意不做"勿再提**：PopupManager 折叠进 UIManager（专科管理器职责分离+回归风险不成比）、god-class Presenter 级拆分（partial 已按职责分文件，逻辑久经考验，拆分回归风险＞组织收益）——依据见 docs/23 §6 P4 行。





- [2026-08-17 10:32:10] 本机 ffmpeg：D:\Tool\FormatFactory\ffmpeg.exe（7.1 完整构建）。PATH 里没有 ffmpeg，媒体处理直接用此路径。已用它重编码 mc_16x10.mp4 消 VideoPlayer WMF 双告警（baseline profile 消时间戳；VUI+容器双写 bt709 消色彩；x264-params 与 -color_* 需同时给）。






- [2026-09-12 14:24:31] 惯性化参考源码：.codely-cli/webrefs/animation-transitions/InertializationForUnity/（InertiaAlgorithm.cs=五次多项式+四元数/向量数学；上游 github.com/portalmk2/InertializationForUnity，MIT；一手信源=GDC 2018 talk "Inertialization: High-Performance Animation Transitions in 'Gears of War 4'"）。本项目 PetInertializer.cs 已大幅分叉（固定系数多项式+帧间 dq 速度捕获防 ±2π 抽搐+起步拉引），原源码仅作数学对照用。GitHub 拉源码法（api.github.com contents 端点 base64 解码落盘）见 gic-webrefs skill。**How to apply:** 改惯性化数学/移植其它 GoW 技术时先读这份源码对照。

- [2026-09-12 14:24:31] GIC 远程仓库：https://github.com/xiaoding521234/gic（2026-09-09 用户拍板转公开；首推 ~900MB pack 含美术资产）。push/pull 走 Clash 代理 127.0.0.1:7890，api.github.com 直连；GitHub token 获取法见全局 git-pr skill（P/Invoke CredRead，仅进程内使用勿打印）。GitHub >50MB 文件仅告警、100MiB 硬限拒推（zh-cn SDF.asset=TMP 动态图集、每补字近整份新增 blob，唯一持续增长大文件）；2026-09-11 实测服务端 size 907.4MB，瘦身路径=filter-repo（DevKey 清洗有先例）+LFS。secret scanning+push protection 已开（私有个人仓该 API 返回 422，须先转公开再开）。**2026-09-07 DevKey 清洗（filter-branch）重写过 8/30 后的提交 SHA**——docs/skill 引用的旧 SHA 已失效，git show 失败按提交信息 grep 重定位。

- [2026-09-11 23:09:27] [2026-09-11] [reference] codely-docs.tuanjie.cn 文档站抓取法（2026-09-11 实证）：web_fetch 对该站深层路径会回错内容（FAQ 壳/相邻页缓存），正确姿势=curl.exe -sL 直拉原始 HTML（PS5.1 的 curl 别名是 Invoke-WebRequest，必须写 curl.exe；URL 会 301 补尾斜杠），正文在 <article>…</article> 内，PS 抽取去标签后可能残留 NUL 字节导致 read_file 报 binary，需 Replace([char]0,' ')。订阅/积分规则三页：/subscription/pricing-details（套餐价格+积分上限表+增值包）、/subscription/credits-description（积分消耗顺序+LLM/多模态单价表）、/subscription/plan-and-billing（升降级/发票/增购）。
