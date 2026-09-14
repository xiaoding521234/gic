## Codely Structured Memories




### User
- [2026-09-11 23:12:13] [2026-09-11] [user] 用户 Tuanjie AI 订阅=个人版 Max（月积分 160,000；三层滚动上限 5h=8,000/周=40,000/月=160,000；闲时=每日除 11:00-12:00、14:00-18:00 外，积分消耗减半即 token 翻倍；订阅积分月度发放不结转，增值包积分 365 天有效不清零；**增值包是否计入 5h/周/月速率上限文档未写明**，2026-09-11 核对定价页上限表口径为"每个套餐…可使用的积分上限"，倾向计入但不确证，建议撞限想靠增值包续命时先问客服）。**Why:** Max=付费订阅用户，TJGenerators 的订阅路由/高成本门按付费用户处理；周上限 40,000 是最常撞的节流阀（2026-09-11 用户实证撞周上限）。**How to apply:** 大批量生成（视频/3D/Pro 图）前先估积分是否撞周上限；被上限卡住时建议挪闲时（消耗减半）；计费规则页=codely-docs.tuanjie.cn /subscription/pricing-details。
- [2026-09-13 18:26:52] [2026-09-13] [user] 输入类 UI 用户要求 IDE 式体验（2026-09-13 指令输入拍板原话「就像我在idea里写代码那样，能够tab自动补全一个词」）：文本输入凡有可枚举词汇域（指令/物品名/角色名）应配补全列表——输入即出、Tab 补当前词、↑↓ 切换选中、点击行补全。**How to apply:** 后续新输入类 UI 直接沿用 InputPopupDialog 命令模式（Show 的 suggester 参数 + CommandSystem.Suggest 或自定义提供器），勿做裸输入框。
- [2026-09-14 01:16:49] [user] 音乐选曲委托流程（2026-09-14 用户拍板原话「能不能我给你音乐名，然后你去下载，并且需要至少为ogg品质」+「不需要响度归一，因为除了至冬的音乐是解包的，其他的音乐都是从该平台上下载的」+「倾向于用QQ音乐」）：用户报曲名或截图清单（可带中文名+英文曲名+用途如「蒙德野外白天」）→ AI 从其常用站 qjjlb.quanjian.com.cn/musicdl **QQ 源 FLAC 无损优先**（网易 320k 仅 QQ 缺曲时兜底并注明）→ **原生响度直转**（平台曲目一律不归一；仅解包源才 loudnorm 对齐）→ libvorbis q8 落 GIC 的 Resources/Audios/Daytime|Night/{theme 前缀} → 编辑器脚本按前缀填 PositionConfig 曲池。**Why:** 用户委托 AI 全链路代办且在意品质与 QQ 源；归一否决因库内血统已统一为平台源。**How to apply:** 后续任何 BGM/曲池需求按项目 skill **gic-music-dl** 流程执行勿再问勿再归一（曲名核验→MusicBrainz 权威曲目表→QQ 限定词搜索→下载→编码→填池→验收全在该 skill）；站点 API 细节见 web-access skill site-patterns/qjjlb.quanjian.com.cn.md；FLAC 母带存 .codely-cli/webrefs/music-masters/。



### Feedback








- [2026-08-14 22:58:07] 用户授权 AI 主动维护项目 skill：完成阶段性工作或发现高频工作流/陷阱密集的系统时，可自行新增/更新 .codely-cli/skills/ 下的 skill，无需逐次确认。**How to apply:** 主动沉淀但避免与项目记忆重复——记忆记事实与决策（为何），skill 记操作流程与检查清单（怎么做）；不确定价值时先问。
















- [2026-09-14 20:37:15] 【桥/编辑器异常恢复】①编辑器没开/桥死心跳：拉起编辑器（D:\Tool\2022.3.62t11\Editor\Tuanjie.exe -projectpath 'D:\Tuanjie_editor\gic'）→ 等 Editor.log 出现 CompileScripts → unity_refresh 重连；心跳仍 stale 用最小化+还原焦点循环（SW_MINIMIZE=6→2s→SW_RESTORE=9+SetForegroundWindow）触发。②CLI 连不上桥【心跳版本错位已修复 2026-09-14，用户确认】：桥包版本更新后心跳路径错位已正常，Copy-Item 副本桥接方案仅适用旧桥 1.0.81（其心跳写 Temp/.com-unity-codely.json 而 CLI 读项目根同名文件→根文件冻结即永判未连接、编辑器重启无效，2026-09-10~12 两次实证）；新版本下桥连不上直接走①拉起编辑器+unity_refresh，勿再套用 Copy-Item。③编辑器 License 过期弹窗：点 Exit 重启编辑器自动续期；重启前先确认场景 dirty=false。④exec_runtime_script 结束 Play 会话/域重载后，桥推送 state=stale + custom_tools_reloaded 通知属**常态**（2026-09-12 P1-P2 连续多轮实证）：unity_refresh 重连即可继续，勿当故障走排查流程。








- [2026-08-29 11:01:45] [2026-08-29 12:10:00] [feedback] 通用规则权威文档=docs/20-项目规范.md（2026-08-29 用户拍板建立，c5a21db 已提交）：编码命名/序列化铁律/编辑器红线/数据入口/UI 与本地化/文档维护/术语表/角色文档规范收拢于此。**Why:** 规范原先只活在 CODELY.md 记忆（不随 git 分发、人类协作者不可见）且散落各文档。**How to apply:** ①新通用规则写进 docs/20 对应小节（一行规则+指针到 skill/docs/14，不复制细节）；②CODELY.md 记忆退回记事实与决策背景；③四类分流=工作流→skill、陷阱→docs/14、决策→设计文档、通用规则→docs/20；④新会话读文档顺序=docs/17（架构）→docs/20（规范）。
- [2026-08-29 16:41:22] [feedback] Skill 触发条件必须写得宽（2026-08-29 拍板"给所有 skill 触发条件改宽"并已全量执行）：description 要覆盖①口语化说法（用户说"改文案"而非"本地化"、"放个音乐"而非"BGM"）②症状式描述（"图加载不出来/点了没反应/为 null"）③兜底句"凡话题涉及X即激活，不确定时也激活"。**Why:** skill 激活=模型语义匹配且只看 description，措辞覆盖窄就漏激活（gic-localization 多次实证）。**How to apply:** 新建/修改任何 SKILL.md 按此三件套写触发条件；用户指出漏激活时先 activate 再干活。






- [2026-09-02 01:06:08] [feedback] 【现状判定三验法】（2026-09-02 战斗骨架复审实证）：据项目 docs 规划或宣称"断链/必炸"前必须三验：①全库搜调用方（区分死代码/活代码——UnitFactory.CreateUnitWithData 零调用=断链潜伏而非现行 bug，勿说"必报错"）②`git log --all --diff-filter=ADR` 查资产是否曾存在（NormalUnit 全历史零命中=计划名当常量写死的漂移，非改名遗漏）③资产实存+脚本 GUID 反查组件挂载（Unit.prefab 实挂齐 Unit+8 组件）。**Why:** 用户两次纠偏："该文档不可信，根据现在实际更新"+"再次复审避免误判"——docs/17 §7 战斗现状原记载与代码实际有出入（0 个技能子类/SkillConfig.asset 缺失/UnitFactory 路径断链均靠核验发现）。**How to apply:** 项目 docs 的"现状"章节只当索引起点，开工/下结论前以代码为准逐文件核验；"必炸"类结论尤其要先查调用方。


- [2026-09-12 14:24:31] 【编辑器脚本必须幂等】unity_refresh 恢复后编辑器脚本可能被重放执行（AddKey 型第二遍会新建同 key 重复条目、追加型会重复 append）——AI 直改本地化/资产的编辑器脚本一律带幂等守卫：加键前查重、追加文本前 Contains 判跳、全量重写型天然幂等；输出逐条报 exists/already/rewritten 便于核对实际生效遍数。细节 docs/14 §8.2.1。


- [2026-09-12 14:24:31] 【UI 图标风格=原神级极简】AI 生成 UI 图标验收对标原神：单一简单物体+单色细线（米白/暖金）+对称+大量留白+无填充，禁实心色块与复合元素起步；已否决方向=复合元素主体（法阵+星、剑盾交叉类）、两色实心剪影+负空间镂空；风格存疑先出 1 张试方向再批量。


- [2026-09-14 21:16:09] 【TJGenerators 两坑已修复（2026-09-14 版本更新实证）】①高成本确认门解析失败（2026-09-03 三次实证：generate_image 不带 confirm_cost 报 "[Error: Could not parse tool response]"）**已修复**：9/14 实测 seedream_pro 分层（估算 765≥门槛 500）正确返回费用确认提示、任务未建零消耗；同轮更新同时修复桥心跳错位。②poll_command_powershell 的 `function Try` 保留字冲突：9/14 实测新模板已无 Try 函数（纯 for 循环），旧坑过时。**新行为**：现单价下普通单图永不触发确认门（frontier 1K=75/2K=125/4K=175 均<500），门只在分层（17×45=765）等场景触发。





- [2026-09-05 22:26:03] 【"看似冗余"的执行路径可能是隐式行为的载体，删除前必查副作用】（2026-09-05 用户报障实证）：LoadSaveData 首建档分支"少 return、建档后又把刚写的档读一遍"被我判为"无害但浪费的 bug"顺手修掉——实际它承载了"建档后 fall-through 顺带 SyncMissingCards 补全未拥有卡"的功能；删掉后 v11 重置当次会话背包缺未拥有角色卡（count=0 条目全无），用户立即发现。**Why:** 旧代码的怪写法常是历史行为的载体，"重构清理"前必须先问"它为什么这么写"；行为保持型重构 ≠ 顺手修 bug。**How to apply:** ①判定既有代码"冗余/bug"并打算顺手移除时，先证明它不承载功能（调用面/数据流推演），证不了就保留原样或单独提问；②改存档建档/重置路径必须过 gic-save-system skill 的"建档即补全"铁律；③回归修复已入 CreateNewSave（SyncMissingCards+SortAllCategories）。



- [2026-09-12 14:24:31] 【技能/角色图标素材一律取官方】（2026-09-09 用户拍板）任何图标需求先走本地游戏提取（gic-gi-extract skill）；Fandom/网络图库抓取链已弃用；AI 生成仅限官方无对应的项目自创技能且需用户点头（规则本体已入 docs/20 §2、落地流程=gic-gi-extract「角色技能图标落地流水线」）。


- [2026-09-12 14:24:31] 【识图 AI 不可作内容判定依据】（2026-09-10 用户拍板原话"不要相信识图的ai，经常不准确"）：多模态识图仅粗检参考，不得作"图案是什么/箭头是否残留/内容对不对"类结论证据；内容判定=像素级测量（连通域/diff/ASCII 渲染，工具箱=gic-gi-extract skill §4b）+用户目检；识图与像素数据冲突时信像素，最终以用户目检为准。
- [2026-09-12 18:14:38] [feedback] 【设计拍板会标准循环】（2026-09-12 战斗系统拍板会全程验证）：AI 先读文档+核代码 → 逐议题给分析/选项/推荐 → 编号清单列文末；用户逐条拍或质疑深挖（质疑常揭出真设计问题，如"同攻速为什么还要排序"→ 引出片内快照 vs 串行语义分水岭）；后续轮次把剩余项**浓缩表格复列**（已拍项划掉/移除），便于批量拍板；用户可整体授权（"剩余的拍板，按照你的建议决定"）→ 按推荐方案全量落档。落档一次完成：修订决策文档（docs/18）+ 建技术设计（docs/22）+ 缺口登记（docs/11）+ 更新导航/架构指针（docs/00/17）。**Why:** 零 ask_user 打断、三轮讨论产出完整技术设计，效率获用户认可。**How to apply:** GIC 后续大系统开稿（C 批次/新系统设计）沿用此循环。
- [2026-09-12 19:05:04] [2026-09-12] 【AI 功能设计对齐大厂框架】用户拍板"尽量参考大厂的ai，比如 Spring AI 等"——做 AI/检索/agent 类新功能时，先网检主流框架的组件划分（如 Spring AI RAG 的 Document/DocumentRetriever/ETL/Advisor 四件套），抽象对齐业界再做自研实现，并在代码注释/文档里写明映射关系。**Why:** 2026-09-12 派蒙知识库落地时用户明确要求（工具式检索即 Spring AI Advisor 的 agentic 等价物）。**How to apply:** 后续 AI 类新系统开稿时，设计段先给"业界框架组件↔本项目组件"对照表；与既有"先网检拿一手信源"铁律配合使用。
- [2026-09-14 01:39:44] 【UI 结构改动交付必附目检清单+报障活体诊断法】P2 面板化连续 4 回归实证（关闭残骸/入场瞬完成/闪现一帧/尖峰全屏闪烁，docs/14 §37+§38），P3 再添两例（祈愿按钮全消失=目标位池化二次缓存污染 §39、毛玻璃"反而更亮"=双层渲染顺序 §38b）：结构化断言全绿≠视觉正确——6 个回归全部通过 19~36 项断言，均靠用户目检发现。**Why:** 断言盲区客观存在（组件级查不出 Canvas 残骸、时序类查不出渲染闪烁、位置类查不出动画到不了位）。**How to apply:** ①UI 结构类改动交付时主动列目检清单（开有动画无闪烁/关彻底消失返回上级/反复开关正常/层级遮挡正确/关键控件在位），不等用户撞上；②**用户报障且编辑器还开着时，先用 exec_runtime_script 反射读取现场状态取证**（anchoredPosition/缓存目标位/层级顺序/激活态）再推理——§39 靠"三个目标位恰为序列化位±200"一发实锤，远快于理论推演。**③现场修复授权（2026-09-14 三连轮实证：音乐无声踢活轮换链/退出无反应运行时补挂 EventSystem/拖拽失效关挡板 raycastTarget）**：用户报障时明示「游戏正在运行，你可以现场取证」——取证定位后**现场最小修复（写操作）也在此授权内**，用户三案全接受且同局继续玩；模式=只读取证→现场修解燃眉→永久修复落盘（编辑器在 Play 中 refresh 被阻）→用户退 Play 后 AI 编译验证+交复测清单。边界不变：主动测试/无报障的运行时验证仍交清单，勿以此条反推可自跑 harness（与全局「测试一律交用户」条目的分工：该条管主动测试，本条管报障响应）。

- [2026-09-13 01:35:13] 系统性碎片化债务的处置偏好=趁项目小做"业界最优"大重构，优先于最小收敛（2026-09-13 原话"我支持大重构，趁现在项目还小，采用业界最优方案"；手势层议题我列 A 不动/B 最小收敛/C 大一统三案并推荐 B，用户拍 C）。**Why:** 与 UI P1-P4 大重构、桌宠窗口体制照 VPet/eSheep 源码逐条重写一脉相承——趁债务未滚大一次性对齐业界。**How to apply:** 诊断出"多处手写同族机制"类碎片化时，方案清单把业界最优大重构给足权重（勿只推最小收敛）；三条护栏不变——既有已调参数保留覆写、调研先行拿一手信源、每阶段目检交用户。
- [2026-09-13 03:15:45] [feedback] 【UI 视效/动画配方跨屏统一优先于因地制宜变体】（2026-09-13 联机毛玻璃实证：我给 Coop 自创"tint 含变暗+整体 CanvasGroup 淡入"单层变体（注释里还标了"与背包双层配方的差异点"），用户目检打回原话"毛玻璃动画效果不对，看看设置界面和背包界面，应当统一"——根因=变暗随 blur 一起扫入+半透明"湿玻璃"感，与既有屏的双层配方（变暗瞬时+纯模糊实心扫入）观感割裂。**Why:** 既有屏已目检验收的观感=事实标准，自创变体即使"等价"也是割裂。**How to apply:** 新界面做动画/视效前先对照既有屏配方逐参核对（tint/BackDim/驱动量），能复用就复用、结构不同才做"变体"且变体须保观感恒等（§38b 视觉恒等式）；配方变体沉淀进 gic-new-screen skill 双层配方段。
- [2026-09-13 12:23:13] [feedback] 【找素材先查项目内已有资产，勿急着外部提取/裁剪】（2026-09-13 加载页素材实证：我准备从原神解包 AssetMap 找徽标+裁用户截图，用户两次纠正——"本项目已经有徽标资源了""看看祈愿里卡池界面，那里用了势力徽标"。**Why:** 项目素材地图：Assets/Resources/UI/Other/Faction/=势力徽标八套（蒙德/璃月/稻妻/须弥/枫丹/纳塔/至冬/坎瑞亚，祈愿卡池界面在用）；Element/Deep|Stroke/=七元素官方图标（ElementFactionConfig 引用）；Other/logo.png="原神棋"文字主视觉（Splash 启动屏用），勿与徽标混淆。**How to apply:** 任何图标/徽标/背景需求，先 ElementFactionConfig 与 Resources/UI 目录扫一遍+问用户，再考虑 gi-extract/截图裁剪；徽标类默认 Faction 目录。
- [2026-09-13 19:05:54] [2026-09-13] 【指令/控制台文案=开发者风格，不写玩家向描述】用户拍板（2026-09-13 原话"很多描述是不必要的，指令主要是面向开发者，而不是给广大玩家看"）：指令回执/错误/help 描述/补全 hint 一律短、干、事实化（如「原石 +100（持有 16100）」「未知物品: x」「开始自动抽卡 ×N」）；错误提示不再罗列全部合法值与教学句（补全与 help 本身就是发现手段）；派蒙的口吻由 LLM 转述层自加（工具描述写明"回执是开发者格式，请翻译成派蒙口吻"），回执层保持原始事实。**Why:** 指令系统定位=开发/作弊工具非玩家功能，冗余描述是噪声。**How to apply:** 后续新增指令、调试面板、控制台类工具的文案全按此基调；不要写"您可以…""支持…如…"式教程腔与拟人化语气词。
- [2026-09-13 22:58:06] 【存档零迁移政策·0.5.a 前】用户拍板（原话「无论是主游戏存档还是ai派蒙存档，都不需要有迁移，老版本存档直接删掉重建即可」）：0.5.a（正式落地版本号，届时用户主动告知）前，主存档与桌宠 pet.json 一律不写迁移——schema 语义变化=只升版本号+旧档删掉重建。**版本号改动权在用户（2026-09-13 追加拍板「规范加一条，ai不要修改版本号」）：AI 不自行修改 CURRENT_SAVE_VERSION/pet.json 版本门——升版=删所有旧档属破坏性决策，语义变化时列变更清单提请用户拍板升版**。主存档早已是重置式（2026-08-16 起，SaveManager「版本过低不迁移直接建档」）；pet.json 存量 v6→v8 链（PetPrefs.ParseAndMigrate：chatCiphers 分槽/quickMessages/你好）与主存档两处旗标迁移（MigrateFateItems/key 分槽）为历史代码，处置待拍板。**Why:** 开发期无真实玩家，旧档可丢，迁移=无谓工时；升版即删档故须用户把关。**How to apply:** 今后任何存档 schema 变更只升版本号+删档重建，勿写迁移链；升版本身也由用户拍板、AI 勿代改；已落档 docs/20 §1.6 + gic-save-system skill「版本策略」节。
- [2026-09-14 00:09:18] 【AI 编译验证自主权·编辑器没开也一样】编译验证始终 AI 自行完成——编辑器没开也直接按「桥/编辑器异常恢复」①拉起编辑器→等 Editor.log CompileScripts→rg "error CS" 查错→同步心跳副本，全程无需询问用户；运行时/Play 测试仍归用户。**Why:** 2026-09-13 用户确认原话「文档或记忆里应当记录有，你可以自行完成编译」。**How to apply:** 交付前编译验证是硬边界，编辑器关闭/桥死不是跳过编译或交给用户的理由，按流程自行恢复环境完成；本会话 23:2x 实证全流程可行。
- [2026-09-15 00:18:46] 【GI 角色网格提取判定铁律】（2026-09-14~15 安柏实证，用户目检识破首版装错角色）：网格 bundle 归属必须用「角色骨架路径全集 CRC32 覆盖率」验证（正确 bundle 对角色 rig=100%，错的角色=45~62%）——blk 混装多角色时按导出批次/贴图占用评分猜归属必翻车（装成了荧影/柯莱系）。正确 bundle 常与 Ani_Avatar_* 动画混装同一 blk（--names 内名匹配不到裸名 Body/Face）。另：GI 贴图 alpha≠真透明（Body_Diffuse alpha 99.7%=0 但 RGB 有数据）、UV 与网格不直接映射（shader 内变换）→ naive UV 评分法失效；URP Lit 纯黑三坑=_BaseColor 未显式设白+SMR culling bounds 错+方向光从背面打（修法：_BaseColor 白+updateWhenOffscreen=true+光转正面+ambient Flat 白+_EMISSION 35% 兜底）。操作全流程已入 gic-gi-extract skill「完整角色模型+动画提取」节+docs/14 §55。
- [2026-09-15 01:17:42] [feedback]【依据 docs 答来源/现状类问题必须整节通读，禁 offset 截窗起读】（2026-09-15 派蒙来源连环误答两轮实证）：用户问「派蒙模型来源于哪个网站」，我 read_file docs/19 时 offset=35 恰好切掉 L34 头行「当前生效：GI 官方模型（2026-08-24 落地，来自 models-resource 完整 rip，asset 328738）」，误锚到下文资产表的「模之屋原始 PMX 包」兜底行→首答错称模之屋；用户纠正「TMR 正是我当初下载派蒙的网站」后，我又拿「项目 PMX 包与 TMR 条目格式对不上」顶回去一轮；用户再纠「当前项目在用的不是MMD模型」才定位——答案在文档里 8-24 就写对了。**Why**：截窗漏读一行小节标题→整条证据链锚错；且与用户记忆冲突时未先回读全文就质疑用户。**How to apply**：①答「X 来源于哪/现状是什么/在哪」前，从节标题起完整读 docs 相关小节，勿用 offset 从中段起读；②用户纠正我的事实性答案且与文档记载冲突时，先重读文档原文核对，再决定是否质疑用户记忆。

### Project
















- [2026-09-12 14:24:31] Assets 清理定案（2026-08-16）：Material/Materials、Resources/Materials 三目录 8 个材质误删已 git 恢复，**勿再清理**（UIBlur/WishSmoke 在用，其余存疑也保留）；已删定案：Assets/Editor 空目录、Scenes/TestScene.scene、StreamingAssets/mc.mp4、Assets 根 6 个 CUsers* 垃圾文件。故意不做：NewAudioMixer、Art/Wish 与 WishArt 改名（Addressables 地址约定）；保留 GlowTest.scene+CardGlowTest.cs、兹白测试皮肤 zibai_skin_solid.png（UnitConfig 兹白 cards[1]，用户拍板不删勿当 bug 修）。**How to apply:** 不再动这批资产。










































































- [2026-09-12 14:20:49] 【搜索/文本判定铁律】search_file_content/glob/list_directory 受 .codelyignore+.gitignore 双层 ignore（Assets 的 png/prefab/asset/mat/wav、Mirror/kcp2k/Plugins 整目录、.codely-cli/.codely/Library/Temp/Logs/Export/Maps 等）——被任一层命中即**静默零命中/无名**，零命中≠不存在：查引用/资产内容/目录清单/skill 文件一律原生 `rg --no-ignore` 或 Get-ChildItem；喂多模态的 Assets 图片先复制到 .codely-cli/tmp；guid 判定一律 AssetPathToGUID/unity_asset get_info（meta guid 可能密文，文本反查假阴性，docs/14 §9）；.unity 与本地化表中文为 \uXXXX 转义，rg 中文直搜必零命中（搜转义形式或先解码，docs/20 §1.3）；.codely-cli/ 与 TextMesh Pro/ gitignored（skill/HANDOFF 勿提交；TMP 材质改动不入库，重装前备份）。细节 docs/14 §6.2。
- [2026-09-12 19:05:04] [2026-09-12] 【exec_editor_script 桥脚本 Newtonsoft 撞名坑】桥内联脚本里用 `Newtonsoft.Json.Linq.JObject` 必报 CS0433（JObject 同时存在于 Newtonsoft.Json 与 Unity.Localization.ThirdParty.Editor 两程序集，脚本环境无法 extern alias）。**Why:** 2026-09-12 烘焙验证脚本实证。**How to apply:** 桥内联脚本一律不用 JObject.Parse/ JsonConvert——计数/取字段用 Regex.Match 原生字符串，或返回原始 JSON 字符串在 AI 侧解析；项目内 .cs 编译不受影响（只用一份）。
- [2026-09-13 02:29:30] 手势输入层大重构 **P1-P4 全量收官（2026-09-13，提交链 11e66f9→9b1e92e→761a111 修复→ec007a6→02b0422）**：PointerInputPump 归一→GestureHub（三门+first-accept-wins 仲裁，门1/门2 可按面豁免）→识别器四件套（纯 C# 可离线断言）→Map/Battle/游戏内桌宠三面接入；GestureMetrics 全局统一常量（双档 slop 24/4px、150ms、450ms、300ms）；WaitForAnyTap 统一点击继续；SkillDetailView 改名 IsPointerOverRect。**P4 落地勘定（docs/24 §7.11）**：桌宠三连击链保持按下驱动（PetHostBase.CountClickChain 跨进程共享，TapRecognizer 抬起计数不适用）、捏合/滚轮留桌宠本面轮询（跨指针准入+方向相反）、surface 能力位 BypassUIGate/IgnoresInputLocks=旧行为编码化。Win32 桌宠独立=桌面进程跳过 Wargame 的架构必然。P4 用户目检若报手感问题→§39 活体诊断法（exec_runtime_script 反射读现场）。调研信源=.codely-cli/webrefs/gesture-input/（notes/gesture-system-research_2026-09-13.md）。**Why:** 指针手势碎片化——判别手写 4 处两套/撞名 3 份/点击继续×4/双轨；用户拍板趁项目小做业界最优大重构+阈值全局统一。**How to apply:** 后续新交互面=实现 IGestureSurface 挂识别器（B6 点击=Battle 面加 TapRecognizer 或 Drag OnSlop 的 OnTapCandidate）；卡牌长按=LongPressRecognizer 首消费者；阈值勿散写，一律 GestureMetrics。
- [2026-09-13 12:45:46] 【加载页布局=比例锚点方案】（2026-09-13 期望图对齐迭代实证）：全屏 UI 排版类（加载页等）子节点一律用 y 比例锚点（anchorMin.y=anchorMax.y=从底百分比、anchoredPosition=0），不用固定 px 坐标——用户 Game 视口常非 16:9（实测 3183×1667，CanvasScaler MatchWidth=0 时 Canvas 宽恒 2560、高随视口变），固定 px 会漂 2~3%。对齐用户参考图的方法=像素扫描块区间（非白像素按行分段）反推位置，期望块位：徽标中心 45.6%/词条标题 82.5%/正文 86%·88.5%/元素行+横线 94%，字号按行距反推 40/28（原 52/38 塞不进期望区间）。**How to apply：** 后续全屏 UI 排版沿用比例锚点；对齐参考图先像素扫描（识图百分比仅作参考）。
- [2026-09-13 14:03:16] [2026-09-13 13:2x] 【已落地 docs/14 §46/§47】UGUI 层级重组三条陷阱（anchor 参照系不迁移/单边锚 pivot 轴心/inactive 不能 StartCoroutine）与协程宿主腰斩陷阱（嵌套 StartCoroutine 不转移宿主——清栈 SetActive(false) 会腰斩全链致锁泄漏）均已写入 docs/14 §46/§47。**How to apply：** 程序化 prefab 重构/转场协程编写前先读这两节。
- [2026-09-13 18:17:17] [project] 用户会并行开多个 AI 会话在同一项目分工开发（2026-09-13 实证：另一 AI 在写 Framework/Command 指令系统）。**How to apply:** ①git status 里非我产生的改动/WIP 文件（尤其未跟踪新目录）属其他会话在制品——勿动勿清理勿"顺手修"，除非它把整个程序集编译堵死才做最小语法解锁并明确告知；②编译错误可能来自其他会话 WIP，先归因再动手；③refresh/构建等编辑器级操作可能与另一会话冲突，用户可能取消——被取消后勿立即重试，问协调节奏。
- [2026-09-13 18:25:03] [project] 并行 AI 会话协调板=.codely-cli/HANDOFF-并行AI协调.md（2026-09-13 建立）：多会话并行开发时**回合开始先读**该文件——记录各会话文件归属（勿改他方文件）、状态与编辑器使用权；状态变化写回各自小节；refresh/编译/构建等编辑器级操作动前先看对方状态避免撞车。当前并行分工会话A=派蒙对话key分槽、会话B=Framework/Command 指令系统。
- [2026-09-13 18:26:45] [2026-09-13] 【C# 插值字符串陷阱】插值孔内三元/条件表达式的冒号会被解析为格式说明符起点——`$"[CommandSystem] {result.ok ? "✓" : "✗"} …"` 直接编译炸（2026-09-13 我写 CommandSystem 时实证，堵死整个程序集、并行会话 A 括号修复）；正确写法=括号包裹 `{(result.ok ? "✓" : "✗")}`。**Why:** Roslyn 在插值孔顶层扫描 `:` 分隔格式段，条件表达式的冒号在顶层即中招。**How to apply:** 写含条件/空合并等带冒号表达式的插值字符串一律先括号；此陷阱属 docs/14 类别（下批整理时补入），代码内括号勿顺手去掉。
- [2026-09-13 18:26:49] [2026-09-13] 【Play Mode 断言动真实存档必须快照+恢复】exec_runtime_script 里测 give/card 等改存档指令时，编辑器 Play 用的就是玩家真实档（LocalLow/HGAME/gic/gic_save.json），删档测试模式是否启用勿假设（EditorPrefs 键名未核验）。**Why:** 2026-09-13 指令系统 38 断言若不恢复将永久污染 1600+ 原石、误发角色卡——靠"测试前 GetItemCount/ownedUnits count 快照 + finally ModifyNow 恢复（未拥有条目=Remove，count=0 复活态=还原 0）+ RebuildOwnedCards"保住原值。**How to apply:** 任何 runtime 断言触碰 SaveManager 前先搭快照/恢复骨架再写断言；TimeUtility 偏移同样测试尾 reset。
- [2026-09-13 18:31:11] [2026-09-13] 【exec_runtime_script 结束后编辑器留在 Play Mode】脚本正常完成后 Play 不自动退出（2026-09-13 两次实证：其后 unity_editor.refresh 报 refresh_blocked_in_play_mode）——需要编译/编辑器级操作时先手动 unity_editor stop 再 refresh。**How to apply:** runtime 断言跑完紧接要 refresh 时，直接先 stop 免一次失败调用；桥随后推 stale+custom_tools_reloaded 属常态，unity_refresh 重连即可。
- [2026-09-13 18:58:20] [2026-09-13] 【§39 活体诊断补遗：暂停现场须先 resume + DontDestroyOnLoad 盲区】用户暂停编辑器（isPaused=true）时报障，exec_runtime_script 握手直接 Operation timed out（错误不指向原因）——先 unity_editor resume 再跑取证脚本，读完停 Play 或按需恢复；且 unity_scene get_hierarchy 只见当前场景根，池化面板/常驻管理器在 DontDestroyOnLoad 的 GameScene/UIRoot 下看不见，须 FindObjectsByType(FindObjectsInactive.Include) 反射读 alpha/activeSelf/_isClosing 等。**Why:** 2026-09-13 半透明弹窗幽灵案实证——resume 后一发读到 alpha=0.292 冻结+_isClosing 实锤（陷阱本体已落 docs/14 §50）。**How to apply:** 用户报障"游戏已暂停可直接读取"类现场，按 resume→反射取证→stop 的顺序。
- [2026-09-13 19:56:28] [2026-09-13] 【exec_runtime_script harness 反射两坑】①懒建/构建后存在的私有字段（_inputField/_slashPanel 类）必须在触发构建之后**每断言点现取**（GetField().GetValue）——在 WireHost 前/首次输入前顶部缓存=null→NRE 或断言假失败（聊天框 slash harness 两版各踩一次，烧 3 轮排障）；②无参私有方法反射调用须 `Invoke(ctrl, null)`——传 `new object[]{null}` 直接报 parameter count mismatch。**Why:** harness 脚本时序 bug 极易误判成产品 bug，浪费整轮工具调用。**How to apply:** 起草 runtime harness 按"构建→现取→断言"顺序；断言失败先加 try/catch 打印完整内部堆栈定位归属（脚本 vs 产品），再修。
- [2026-09-13 23:31:42] 【编辑器在跑判定两陷阱】①Get-Process -Name Tuanjie 命中的 tuanjie.exe 可能是 Tuanjie Hub 启动器（WMI CommandLine=...\Tuanjie Cowork\hub\tuanjie.exe）而非编辑器——判编辑器真在跑必须查 CommandLine 含 Editor\Tuanjie.exe -projectpath；②编辑器关闭后 netstat 桥端口仍 LISTENING 且指向已死 pid（tasklist 查无此 pid）=幽灵端口，端口监听≠桥活着，以 Temp 心跳时间戳为准。**Why:** 2026-09-13 桥握手超时排查中连环误判（把 Hub 当编辑器、把幽灵端口当活桥），WMI 验明后才发现编辑器 23:05 已关。**How to apply:** 桥连不上先 WMI 验进程身份+心跳新鲜度，再走「桥/编辑器异常恢复」流程拉起编辑器。
- [2026-09-13 23:31:42] 【prefab 中文序列化字段名=大写转义】prefab YAML 里中文序列化字段名以大写 hex 转义存储（如 GlassPanelAnimator 的模糊层字段="\u6A21\u7CCA\u5C42"）——rg 直搜中文字面与小写 "\u6a21" 形式均零命中，搜转义必须大写。**Why:** 2026-09-13 核验 CoopScreen.prefab 动画器接线时小写转义连搜三轮全空，改大写一发命中。**How to apply:** 文本反查 prefab 接线时，把 .cs 字段名转成大写 \uXXXX 再 rg -F --no-ignore 搜（穿透 .codelyignore/.gitignore 双层 ignore）。
- [2026-09-13 23:31:42] 【联机列表页背景板静态拍板】背景板（ServerListPanel/Background/BackgroundImage 视差背板）不参与入场/退场/列表↔房间切换动画，随面板静态起落——由 GlassPanelAnimator 自动收集按名排除 BackDim/Background 结构层实现，其余内容元素照常滑入滑出。**Why:** 2026-09-13 用户拍板原话「房间列表界面的背景板不需要有入场退场动画」。**How to apply:** 勿把该按名排除当 bug 修回滑动组；新界面背景板默认同样静态处理；规则已入 gic-new-screen skill §3 双层配方变体行。
- [2026-09-14 19:10:43] [project] 星落湖战斗音乐（2026-09-14 全链路接线完毕+两次拍板落定）：蒙德野外（MondstadtWilds=4）曲池 day 9 曲（daytime_0~8=旅人的暂歇/见惯的风景/平原的低语/风洗的群山/不散的魂灵/烈日之残响/希望的新一天/希望之旅/久住往昔）+ night 7 曲（night_0~6=情不自禁/饰金的夜色/月亮处盗来的歌/一段回忆/静候未来/月照的荒野/**星知晓的旧梦**——2026-09-14 拍板入夜池，QQ FLAC 母带 starlit_past 已直转 night_6.ogg），全部 QQ FLAC 无损原生直转（母带存 webrefs/music-masters/）。战斗音乐=轮换链（播完→10s→按战斗独立时钟时段重选池：初始 6:00、回合结束+20min、边界 8:00-20:00——**开局 6:00 落夜晚池**，改原神式 6-19 边界待用户后续反馈）。**星落湖 isUnlocked=1 已解锁**（2026-09-14 拍板，锚点白显可传送）。唯一遗留：MapConfig 星落湖锚点仍占位 (70,28) 待用户取点器「锚点重定位」标定（标定前坐标是假的勿当实数据）。**How to apply:** 涉星落湖锚点的工作先核标定是否已完成。
- [2026-09-14 20:28:54] [2026-09-14] UnityInsight 索引系统架构与活锁 bug（已提交 Bug Hunter，等官方周二结算）：架构=CLI 侧 node 守护进程（lib/bundle/unity-insight-cli.js serve --daemon --project <项目>）持有全部索引；数据目录=<项目>\.codely-cli\UnityInsight\（构建中=index.db.tmp+WAL，发布后写 index.current 指针→ready）；**serve 模式被动**——杀掉后不自动重生、手动重生后也不自动构建，须 Cowork GUI 发构建指令（编辑器菜单无此入口，AI/ 菜单只有 Check Connections/Force Reload）。活锁 bug 特征（2026-09-14 19:56 复现实证）：first_build 写入 ~6.3 分钟（tmp 230MB+WAL 121MB）后磁盘写入冻结，进程转 4~5 核满负荷+RSS 300MB→7.5GB 狂涨+零 I/O，index_building=true 永不翻转、GUI 无任何进度/报错/超时。~\.codely-cli\crash-logs\exit-*.json（uptime<1s、exitCode 0）=daemon 单实例锁握手记录，全天常态勿误判为崩溃。处置已做：杀 daemon（Stop-Process）+清 tmp 三件套；可复现性重试待用户 GUI 触发后监控。Cowork CLI=1.0.0-release.57（Node v24.3.0）。
- [2026-09-14 21:58:08] [2026-09-14] Bug Hunter 权威证据包位置=.codely\有效bug活动\（用户 21:5x 重组，旧 .codely\bughunter-* 已整体移入勿再引用）：已提交\=index-build-failure、activity-page-log-path、cron-oneshot-no-fire（3 件落地）；待提交=jobject-assembly-conflict（截图已拍）、plan-path-error（21:41 提交遇 503 待重试）、runtime-script-playmode-remains、playmode-real-save-suggestion；另有「点击提交bug按钮后出错」=提交接口 503 bug（PUT /api/bughunt/question/6aa7f9a7…/publish 返 503 "Connect backend is temporarily unavailable"，question 已建未发布、页面无可见提示仅控制台可见；2026-09-14 21:57 六件齐）。**同日复验判死勿再打包**：高成本确认门解析失败、poll Try 保留字冲突、AfterAgent 空注入误触发（今日注入回合零 hook 触发+零 SKIP 日志）——三者均已被 9/14 CLI 更新修复；暂停态握手超时不可复现（暂停中 531ms 正常握手）。周额度：已交 3=9000，待提交 5 件全落=24000＞2 万——本周最多再落 3 件，runtime+real-save 建议留下周。
- [2026-09-15 01:44:54] [2026-09-14] 安柏完整模型+动画提取交付（2026-09-15 已被 TMR 版取代）：原产物 Amber_Base/Amber_Wic.prefab+154 动画已按用户拍板删除——**现行=Assets/Art/AmberTMR/**（用户下载的 TMR asset/328742 直提包：Amber.fbx 93 节点/5 SMR/47 morph + AmberTMR.prefab 材质动作全接好；154 Clips 整体搬至 AmberTMR/Clips GUID 不变；原始 zip 存档 D:\Tool\AmberModel\TMR\；TMR 无皮肤版）。管线全链路仍有效：操作=gic-gi-extract skill「完整角色模型+动画提取」节、陷阱原理=docs/14 §55（Mesh JSON 含权重、骨名哈希=CRC32(路径)、bindpose 藏逆矩阵、跨网格按变体归一）；物理骨链动画压缩哈希路径不可读（静态兜底）。**How to apply:** 后续角色完整模型提取仍走 skill 流程；安柏模型相关一律用 AmberTMR；物理骨链动画解法（YAML 直读压缩曲线+几何推断父骨）为潜在后续任务。

- [2026-09-15 01:42:01] [2026-09-15] 加载页势力徽标分层动画系统已落地（蒙德方案已拍板定稿：2026-09-15 用户验收预览后拍板只用分层方案，AI 逐帧等其它路线产物已删）：LoadingOverlayDriver 新增「徽标分层表」（配了的势力=底图静止+动层旋转，动层转速默认 -40°/s 可调；未配势力维持原整标缓转零破坏）。蒙德资产= mond_stadt 层拆 `Assets/Resources/UI/Other/Faction/mondstadt_frame.png`（官方原像素：外框/阶梯顶/侧耳/尖饰/毂环/键槽十字，含 4 处叶尖×框线交叉的沿边行走插值重建）+ `mondstadt_windmill.png`（取官方单片翼形叶绕毂心 (511.5,474.4) 精确 60° 复制六叶，等大 25.1K±0.3%，叶尖穿框穿插保留）；prefab Emblem 下 Windmill 子节点（240×240，轴心偏移 (-0.12,+8.81) 对准毂心）。**关键拍板（2026-09-15 用户）**：官方六叶为镜像三对、53°/74° 间距交替（纯静态美观设计），转起来露馅——用户拍板「不用像素级对齐原徽标，大致即可」→均匀 60° 重排。**Why:** 非旋转对称图形做循环动画，运行时连续旋转优于帧序列（帧循环必有接缝）。**How to apply:** 后续势力徽标动画沿用此模式（连通域拆层+均匀重排，拆层/重排脚本存 .codely-cli/tmp/mond-anim/：split_v3.js、rebuild_blades.js、preview.js=带色调预览 GIF）。

- [2026-09-15 01:42:05] [2026-09-15] 【AI 生成 sprite-sheet 实证结论（蒙德徽标风车案例）】generate_sprite_animation（frontier-game-design）两次生成均未过像素验收：①初版 33.7% 近白不透明像素（设计漂移）；②加强 prompt 后色彩还原（90.1% 精确薄荷绿）但循环接缝 IoU 仅 0.603、静止区 7% 像素逐帧抖动、叶片形变——**扩散模型无法同时满足「外框像素级静止+部件精确旋转+无缝循环」三条件**。**Why:** 用户要求「让 AI 生成循环动画」，实测失败后转官方素材确定性合成（无损拆层+运行时旋转），用户验收方向。**How to apply:** 「部件动而外框静」类动画需求直接走拆层/合成路线，勿再尝试 AI 逐帧生成；AI 生成适用于无静止参照的全新动效。两次失败产物已按用户 2026-09-15 拍板删除，仅留拆层工具链（split_v3/rebuild_blades/preview）与成品图层。


### Reference
- [2026-09-14 00:36:55] MC mod gichess（旧项目，Java/NeoForge）：源码 D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\（308文件），jar D:\Tuanjie_editor\gic\.codely-cli\webrefs\genshin-unpack\gichess\my\wg-0.2.d（2026-09-05 随 gichess 77.78GB GI 解包归档整体迁入 webrefs/genshin-unpack/，原 D:\Picture\gichess）。~20+角色，7元素18反应，蒙德延奏/纳塔夜魂已实现。**Why:** GIC 战斗系统 Unity 移植的架构参考。**How to apply:** 仅作战斗系统架构参考；音频/曲目选择勿再翻查旧 mod（2026-09-14 用户拍板「旧mod不必再看」，音乐素材由用户自行网找）。需要查旧 Java 实现时按路径阅读源码。


- [2026-08-10 09:28:39] 技术陷阱与 Bug 修复记录：详见 docs/14-技术陷阱与Bug修复记录.md。
- [2026-09-13 00:42:25] GIC 文档体系：**docs/17-代码架构指南.md 是新会话入口文档**（目录结构/核心系统速查表/场景清单/配置资产/常用工作流速查/战斗规划/环境备忘），开工先读它再按需深入；docs/00-12 玩法设计、13 大地图、14 技术陷阱、15 输入系统、16 优化史、18 战斗决策、19 AI派蒙、20 项目规范（规范权威）、21 商业化与发布边界、23 UI架构重构技术设计。**How to apply:** 新系统落地/结构性变化后更新 docs/17 对应小节保持时效。**2026-09-13 更新：UI 重构 P1-P4 已全量收官**（15 提交：UIManager 统一栈+池化面板，四屏 prefab 化，场景只剩 Boot/Splash/MainHall/Battle/Map+PaimonPet；面板制纪律全部沉淀 docs/14 §37-39b 与 gic-new-screen skill）。**两条已否决的"故意不做"勿再提**：PopupManager 折叠进 UIManager（专科管理器职责分离+回归风险不成比）、god-class Presenter 级拆分（partial 已按职责分文件，逻辑久经考验，拆分回归风险＞组织收益）——依据见 docs/23 §6 P4 行。





- [2026-08-17 10:32:10] 本机 ffmpeg：D:\Tool\FormatFactory\ffmpeg.exe（7.1 完整构建）。PATH 里没有 ffmpeg，媒体处理直接用此路径。已用它重编码 mc_16x10.mp4 消 VideoPlayer WMF 双告警（baseline profile 消时间戳；VUI+容器双写 bt709 消色彩；x264-params 与 -color_* 需同时给）。






- [2026-09-12 14:24:31] 惯性化参考源码：.codely-cli/webrefs/animation-transitions/InertializationForUnity/（InertiaAlgorithm.cs=五次多项式+四元数/向量数学；上游 github.com/portalmk2/InertializationForUnity，MIT；一手信源=GDC 2018 talk "Inertialization: High-Performance Animation Transitions in 'Gears of War 4'"）。本项目 PetInertializer.cs 已大幅分叉（固定系数多项式+帧间 dq 速度捕获防 ±2π 抽搐+起步拉引），原源码仅作数学对照用。GitHub 拉源码法（api.github.com contents 端点 base64 解码落盘）见 gic-webrefs skill。**How to apply:** 改惯性化数学/移植其它 GoW 技术时先读这份源码对照。

- [2026-09-12 14:24:31] GIC 远程仓库：https://github.com/xiaoding521234/gic（2026-09-09 用户拍板转公开；首推 ~900MB pack 含美术资产）。push/pull 走 Clash 代理 127.0.0.1:7890，api.github.com 直连；GitHub token 获取法见全局 git-pr skill（P/Invoke CredRead，仅进程内使用勿打印）。GitHub >50MB 文件仅告警、100MiB 硬限拒推（zh-cn SDF.asset=TMP 动态图集、每补字近整份新增 blob，唯一持续增长大文件）；2026-09-11 实测服务端 size 907.4MB，瘦身路径=filter-repo（DevKey 清洗有先例）+LFS。secret scanning+push protection 已开（私有个人仓该 API 返回 422，须先转公开再开）。**2026-09-07 DevKey 清洗（filter-branch）重写过 8/30 后的提交 SHA**——docs/skill 引用的旧 SHA 已失效，git show 失败按提交信息 grep 重定位。

- [2026-09-11 23:09:27] [2026-09-11] [reference] codely-docs.tuanjie.cn 文档站抓取法（2026-09-11 实证）：web_fetch 对该站深层路径会回错内容（FAQ 壳/相邻页缓存），正确姿势=curl.exe -sL 直拉原始 HTML（PS5.1 的 curl 别名是 Invoke-WebRequest，必须写 curl.exe；URL 会 301 补尾斜杠），正文在 <article>…</article> 内，PS 抽取去标签后可能残留 NUL 字节导致 read_file 报 binary，需 Replace([char]0,' ')。订阅/积分规则三页：/subscription/pricing-details（套餐价格+积分上限表+增值包）、/subscription/credits-description（积分消耗顺序+LLM/多模态单价表）、/subscription/plan-and-billing（升降级/发票/增购）。
- [2026-09-13 23:01:57] GIC 加载页进度旋律·信源与工具链（2026-09-13 六轮迭代实证）：**现行=原曲直截段（蒙德，已验收定稿）**——用户拍板"许多地方不像原曲，直接用原曲截取旋律并加工尾部"：**官方音源库=本地 GI 解包转换版 `.codely-cli/webrefs/genshin-unpack/gichess/audio/bgmwav/bgm`**（bgmogg 同名 ogg；16 曲=旧 gichess mod 精选非全量，璃月/稻妻等不在内），截《蒙德城繁忙的午后》0→2.05s（3.1s 初版被用户砍短"大概 2 秒左右"）尾 0.4s 淡出=mondstadt_city_riff.wav 单条目播放、填充时长 1.55s 与音乐同收；**未用试验素材已清删**（长笛/吉他采样 wav+prefab 备份，用户拍板"不使用的东西删掉"；采样重建链路脚本保留在 .codely-cli/tmp：decode_notes.js/make_mis_notes.ps1/fetch_guitar.ps1/fetch_samples.ps1）。**保真规则：音频"像不像原曲"类需求优先找官方音频直截，重建方案保真度不足**（音符采样分层 21 条目被用户否决"许多地方不像原曲"——长笛+吉他 Philharmonia 采样分层机制与素材保留未删，prefab 备份 .codely-cli/tmp/LoadingOverlay.prefab.notes21.bak 拷回即回退；纯合成钢片琴更早被否）。重建链路（备用）：乐谱源 pianoletternotes.blogspot.com（ASCII piano-roll 字母谱 RH/LH 分行≈26 列栅格；Clash 代理+curl.exe 拉 DDG html 端点再直连，r.jina.ai 匿名 401，站本体 301 自指坑=去 www 直访），解码脚本 .codely-cli/tmp/decode_notes.js；乐器采样=Philharmonia CC-BY 镜像 github.com/skratchdot/philharmonia-samples（长笛/单簧管 `{inst}_{音高}_05_{力度}_normal.mp3`（长笛有 mf、单簧管只 f）、吉他 `{inst}_{音高}_very-long_{力度}_normal.mp3`（mandolin 无 F3 以下低音）；处理=去头部静音+峰值归一 -6dBFS+尾淡出）；机制=同格点多条目同帧全奏+每音符音量倍率（乐器编制跟随原曲配器）。**Why:** 后续势力加载旋律：有官方音频→直截（保真天花板）；无→重建链路。**How to apply:** 新势力=先查 bgmwav 音源库与用户，无则字母谱解乐句→采样分层→prefab 表加条目；机制与选段规则（≤3s、85% 卡点三段制）见 gic-audio skill 加载页进度旋律节。
- [2026-09-14 20:28:49] [2026-09-14] Codely Bug Hunter 捉虫活动（codely.tuanjie.cn/bug-hunter，登录墙后、公开渠道无说明）：每个有效 Bug=3000 积分（能力失效/行为异常/体验缺陷三档+功能建议均计分，拿不准也先交），周上限 2 万，周榜前三额外 +3/2/1 万，每周二结算公布（非实时），兑换码发邮箱一周内须核销。提交四件套=①概述+详细描述 ②截图 ③当天日志（真实路径=%USERPROFILE%\.codely\logs\{core,cowork,hubcore}-YYYY-MM-DD.log，活动页写的「项目根 ./codely/logs」与实际不符）④Session 导出（CLI 内敲斜杠命令 /chat export，**必须带斜杠**，裸 "export" 会被当普通消息）。**首个 bug 已提交（2026-09-14，UnityInsight 索引构建活锁，最高档能力失效，证据包=.codely\bughunter-index-build-failure\）**；提交流程已沉淀全局 skill codely-bughunter（证据包打包+活体取证六步套路）。
- [2026-09-15 01:17:44] [reference] GI 官方提取模型（非MMD）知名站点清单（2026-09-15 网检验证）+【用户拍板：GI 模型获取指定站=The Models Resource】（原话「The Models Resource这个网站正是我当初下载派蒙的那个网站，就用这个」）：①The Models Resource（models-resource.com/pc_computer/genshinimpact/，779 资产按角色/元素/皮肤/武器/NPC/敌人分类，最正统 rip 库，CF 拦 curl 需浏览器；站内 search 端点 404、分区条目手风琴需 CDP eval 展开提取，访问经验=web-access skill site-patterns/www.models-resource.com.md）②GameBanana GI（gamebanana.com/games/6476，~2000 mod，3DMigoto 形态）③Nexus Mods（nexusmods.com/genshinimpact）④GIMI 生态（github.com/SilentNightSound/GI-Model-Importer + Discord discord.gg/gR2Ts6ApP7，最大 GI 提取/移植社区，模型库在 Discord 频道）⑤Sketchfab（搜 genshin impact 大量可下载直提角色，骨骼保留参差+DMCA 下架风险）⑥Open3DLab/SmutBase/SFMLab（Blender rig 移植，多为自定义绑定非官方骨架，NSFW 向）。**关键判定**：官方动作兼容=模型须保留 GI 原始骨架（Bip001 骨名/路径）——第三方站下载常被重绑定（VRM/XPS/GIMI Blender 移植全不兼容），落地 Unity 套官方 .anim 最稳仍是本地解包自提（gic-gi-extract 流程）。中文圈无稳定官方提取站（模之屋/44mmd=MMD、getanimate.app=VRM 转制），分享走 B站网盘。**派蒙来源（2026-09-15 定案）**：现行 GI 官方模型 Paimon.fbx（1.84MB 含网格+49 morph）=TMR asset/328738 直提包（Adverse56 上传 2021-12，dae/fbx/png 45 项）——**docs/19 §2.1 头行 2026-08-24 起已记载「来自 models-resource 完整 rip」**，我 09-15 首答 offset 截窗漏读该行误答模之屋、被用户两次纠正（详见 feedback 条）；206 条 Ani_NPC_Kanban_Paimon_*.anim 与 33 张 NPC_Kanban_Paimon_Tex_* 贴图随包/自提混装（AnimeStudio 侧 fbx 仅 128KB Animator 骨架件，与项目 1.84MB 网格件可区分）；MMD 兜底包（神帝宇 PMX+使用规则.txt）=模之屋线，docs/19「模之屋原始 PMX 包」行无误勿改。



