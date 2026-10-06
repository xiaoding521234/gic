## Codely Structured Memories







### User
- [2026-09-11 23:12:13] [2026-09-11] [user] 用户 Tuanjie AI 订阅=个人版 Max（月积分 160,000；三层滚动上限 5h=8,000/周=40,000/月=160,000；闲时=每日除 11:00-12:00、14:00-18:00 外，积分消耗减半即 token 翻倍；订阅积分月度发放不结转，增值包积分 365 天有效不清零；**增值包是否计入 5h/周/月速率上限文档未写明**，2026-09-11 核对定价页上限表口径为"每个套餐…可使用的积分上限"，倾向计入但不确证，建议撞限想靠增值包续命时先问客服）。**Why:** Max=付费订阅用户，TJGenerators 的订阅路由/高成本门按付费用户处理；周上限 40,000 是最常撞的节流阀（2026-09-11 用户实证撞周上限）。**How to apply:** 大批量生成（视频/3D/Pro 图）前先估积分是否撞周上限；被上限卡住时建议挪闲时（消耗减半）；计费规则页=codely-docs.tuanjie.cn /subscription/pricing-details。
- [2026-09-13 18:26:52] [2026-09-13] [user] 输入类 UI 用户要求 IDE 式体验（2026-09-13 指令输入拍板原话「就像我在idea里写代码那样，能够tab自动补全一个词」）：文本输入凡有可枚举词汇域（指令/物品名/角色名）应配补全列表——输入即出、Tab 补当前词、↑↓ 切换选中、点击行补全。**How to apply:** 后续新输入类 UI 直接沿用 InputPopupDialog 命令模式（Show 的 suggester 参数 + CommandSystem.Suggest 或自定义提供器），勿做裸输入框。
- [2026-09-26 20:20:22] 音乐选曲=用户委托流程（2026-09-14 拍板）：用户报曲名/截图清单，AI 下载落库**勿再问勿再归一**。全流程=**gic-music-dl skill**（QQ 源 FLAC 优先/原生响度直转/落库路径与曲池填写细节全在 skill 内）；站点 API=web-access skill site-patterns/qjjlb.quanjian.com.cn.md；FLAC 母带存 .codely-cli/webrefs/music-masters/。
- [2026-10-02 20:37:17] 【模型路由偏好（2026-10-02 用户拍板原话「当从事画面相关工作时，我倾向于使用kimi k3，并且其自带识图能力，可以自行识图。其它工作给glm 5.3，不可自行识图」）】画面/视觉相关工作（效果调优、看实际画面、截图自检观感）→ 用户倾向用 Kimi K3——自带识图能力，**可自行识图看实际画面**；其它工作 → GLM 5.3——**不可自行识图**。**How to apply:** 身为可识图模型（Kimi K3）会话时允许截图+视觉自检做画面迭代（最终验收仍交用户目检）；身为非视觉模型（GLM 5.3）会话时严禁自行截图识图判定画面——列目检清单请用户看屏幕回报（2026-08-10③/08-21 旧规对非视觉模型继续生效）。

### Feedback








- [2026-08-14 22:58:07] 用户授权 AI 主动维护项目 skill：完成阶段性工作或发现高频工作流/陷阱密集的系统时，可自行新增/更新 .codely-cli/skills/ 下的 skill，无需逐次确认。**How to apply:** 主动沉淀但避免与项目记忆重复——记忆记事实与决策（为何），skill 记操作流程与检查清单（怎么做）；不确定价值时先问。


























- [2026-08-29 11:01:45] [2026-08-29 12:10:00] [feedback] 通用规则权威文档=docs/20-项目规范.md（2026-08-29 用户拍板建立，c5a21db 已提交）：编码命名/序列化铁律/编辑器红线/数据入口/UI 与本地化/文档维护/术语表/角色文档规范收拢于此。**Why:** 规范原先只活在 CODELY.md 记忆（不随 git 分发、人类协作者不可见）且散落各文档。**How to apply:** ①新通用规则写进 docs/20 对应小节（一行规则+指针到 skill/docs/14，不复制细节）；②CODELY.md 记忆退回记事实与决策背景；③四类分流=工作流→skill、陷阱→docs/14、决策→设计文档、通用规则→docs/20；④新会话读文档顺序=docs/17（架构）→docs/20（规范）。
- [2026-08-29 16:41:22] [feedback] Skill 触发条件必须写得宽（2026-08-29 拍板"给所有 skill 触发条件改宽"并已全量执行）：description 要覆盖①口语化说法（用户说"改文案"而非"本地化"、"放个音乐"而非"BGM"）②症状式描述（"图加载不出来/点了没反应/为 null"）③兜底句"凡话题涉及X即激活，不确定时也激活"。**Why:** skill 激活=模型语义匹配且只看 description，措辞覆盖窄就漏激活（gic-localization 多次实证）。**How to apply:** 新建/修改任何 SKILL.md 按此三件套写触发条件；用户指出漏激活时先 activate 再干活。






- [2026-09-02 01:06:08] [feedback] 【现状判定三验法】（2026-09-02 战斗骨架复审实证）：据项目 docs 规划或宣称"断链/必炸"前必须三验：①全库搜调用方（区分死代码/活代码——UnitFactory.CreateUnitWithData 零调用=断链潜伏而非现行 bug，勿说"必报错"）②`git log --all --diff-filter=ADR` 查资产是否曾存在（NormalUnit 全历史零命中=计划名当常量写死的漂移，非改名遗漏）③资产实存+脚本 GUID 反查组件挂载（Unit.prefab 实挂齐 Unit+8 组件）。**Why:** 用户两次纠偏："该文档不可信，根据现在实际更新"+"再次复审避免误判"——docs/17 §7 战斗现状原记载与代码实际有出入（0 个技能子类/SkillConfig.asset 缺失/UnitFactory 路径断链均靠核验发现）。**How to apply:** 项目 docs 的"现状"章节只当索引起点，开工/下结论前以代码为准逐文件核验；"必炸"类结论尤其要先查调用方。


- [2026-09-12 14:24:31] 【编辑器脚本必须幂等】unity_refresh 恢复后编辑器脚本可能被重放执行（AddKey 型第二遍会新建同 key 重复条目、追加型会重复 append）——AI 直改本地化/资产的编辑器脚本一律带幂等守卫：加键前查重、追加文本前 Contains 判跳、全量重写型天然幂等；输出逐条报 exists/already/rewritten 便于核对实际生效遍数。细节 docs/14 §8.2.1。


- [2026-09-12 14:24:31] 【UI 图标风格=原神级极简】AI 生成 UI 图标验收对标原神：单一简单物体+单色细线（米白/暖金）+对称+大量留白+无填充，禁实心色块与复合元素起步；已否决方向=复合元素主体（法阵+星、剑盾交叉类）、两色实心剪影+负空间镂空；风格存疑先出 1 张试方向再批量。








- [2026-09-05 22:26:03] 【"看似冗余"的执行路径可能是隐式行为的载体，删除前必查副作用】（2026-09-05 用户报障实证）：LoadSaveData 首建档分支"少 return、建档后又把刚写的档读一遍"被我判为"无害但浪费的 bug"顺手修掉——实际它承载了"建档后 fall-through 顺带 SyncMissingCards 补全未拥有卡"的功能；删掉后 v11 重置当次会话背包缺未拥有角色卡（count=0 条目全无），用户立即发现。**Why:** 旧代码的怪写法常是历史行为的载体，"重构清理"前必须先问"它为什么这么写"；行为保持型重构 ≠ 顺手修 bug。**How to apply:** ①判定既有代码"冗余/bug"并打算顺手移除时，先证明它不承载功能（调用面/数据流推演），证不了就保留原样或单独提问；②改存档建档/重置路径必须过 gic-save-system skill 的"建档即补全"铁律；③回归修复已入 CreateNewSave（SyncMissingCards+SortAllCategories）。



- [2026-09-12 14:24:31] 【技能/角色图标素材一律取官方】（2026-09-09 用户拍板）任何图标需求先走本地游戏提取（gic-gi-extract skill）；Fandom/网络图库抓取链已弃用；AI 生成仅限官方无对应的项目自创技能且需用户点头（规则本体已入 docs/20 §2、落地流程=gic-gi-extract「角色技能图标落地流水线」）。


- [2026-09-12 14:24:31] 【识图 AI 不可作内容判定依据】（2026-09-10 用户拍板原话"不要相信识图的ai，经常不准确"）：多模态识图仅粗检参考，不得作"图案是什么/箭头是否残留/内容对不对"类结论证据；内容判定=像素级测量（连通域/diff/ASCII 渲染，工具箱=gic-gi-extract skill §4b）+用户目检；识图与像素数据冲突时信像素，最终以用户目检为准。
- [2026-09-12 18:14:38] [feedback] 【设计拍板会标准循环】（2026-09-12 战斗系统拍板会全程验证）：AI 先读文档+核代码 → 逐议题给分析/选项/推荐 → 编号清单列文末；用户逐条拍或质疑深挖（质疑常揭出真设计问题，如"同攻速为什么还要排序"→ 引出片内快照 vs 串行语义分水岭）；后续轮次把剩余项**浓缩表格复列**（已拍项划掉/移除），便于批量拍板；用户可整体授权（"剩余的拍板，按照你的建议决定"）→ 按推荐方案全量落档。落档一次完成：修订决策文档（docs/18）+ 建技术设计（docs/22）+ 缺口登记（docs/11）+ 更新导航/架构指针（docs/00/17）。**Why:** 零 ask_user 打断、三轮讨论产出完整技术设计，效率获用户认可。**How to apply:** GIC 后续大系统开稿（C 批次/新系统设计）沿用此循环。
- [2026-09-12 19:05:04] [2026-09-12] 【AI 功能设计对齐大厂框架】用户拍板"尽量参考大厂的ai，比如 Spring AI 等"——做 AI/检索/agent 类新功能时，先网检主流框架的组件划分（如 Spring AI RAG 的 Document/DocumentRetriever/ETL/Advisor 四件套），抽象对齐业界再做自研实现，并在代码注释/文档里写明映射关系。**Why:** 2026-09-12 派蒙知识库落地时用户明确要求（工具式检索即 Spring AI Advisor 的 agentic 等价物）。**How to apply:** 后续 AI 类新系统开稿时，设计段先给"业界框架组件↔本项目组件"对照表；与既有"先网检拿一手信源"铁律配合使用。
- [2026-09-26 20:20:11] 【UI 结构改动交付必附目检清单+报障活体诊断法】P2 面板化连续 4 回归实证（关闭残骸/入场瞬完成/闪现一帧/尖峰全屏闪烁，docs/14 §37+§38），P3 再添两例（祈愿按钮全消失=目标位池化二次缓存污染 §39、毛玻璃"反而更亮"=双层渲染顺序 §38b）：结构化断言全绿≠视觉正确——6 个回归全部通过 19~36 项断言，均靠用户目检发现。**Why:** 断言盲区客观存在（组件级查不出 Canvas 残骸、时序类查不出渲染闪烁、位置类查不出动画到不了位）。**How to apply:** ①UI 结构类改动交付时主动列目检清单（开有动画无闪烁/关彻底消失返回上级/反复开关正常/层级遮挡正确/关键控件在位），不等用户撞上；②**用户报障且编辑器还开着时，先用 exec_runtime_script 反射读取现场状态取证**（anchoredPosition/缓存目标位/层级顺序/激活态；活体取证技法合集=docs/14 §90）再推理——§39 靠"三个目标位恰为序列化位±200"一发实锤，远快于理论推演。**③现场修复授权（2026-09-14 三连轮实证：音乐无声踢活轮换链/退出无反应运行时补挂 EventSystem/拖拽失效关挡板 raycastTarget）**：用户报障时明示「游戏正在运行，你可以现场取证」——取证定位后**现场最小修复（写操作）也在此授权内**，用户三案全接受且同局继续玩；模式=只读取证→现场修解燃眉→永久修复落盘（编辑器在 Play 中 refresh 被阻）→用户退 Play 后 AI 编译验证+交复测清单。边界不变：主动测试/无报障的运行时验证仍交清单，勿以此条反推可自跑 harness（与全局「测试一律交用户」条目的分工：该条管主动测试，本条管报障响应）。


- [2026-09-13 01:35:13] 系统性碎片化债务的处置偏好=趁项目小做"业界最优"大重构，优先于最小收敛（2026-09-13 原话"我支持大重构，趁现在项目还小，采用业界最优方案"；手势层议题我列 A 不动/B 最小收敛/C 大一统三案并推荐 B，用户拍 C）。**Why:** 与 UI P1-P4 大重构、桌宠窗口体制照 VPet/eSheep 源码逐条重写一脉相承——趁债务未滚大一次性对齐业界。**How to apply:** 诊断出"多处手写同族机制"类碎片化时，方案清单把业界最优大重构给足权重（勿只推最小收敛）；三条护栏不变——既有已调参数保留覆写、调研先行拿一手信源、每阶段目检交用户。


- [2026-09-13 19:05:54] [2026-09-13] 【指令/控制台文案=开发者风格，不写玩家向描述】用户拍板（2026-09-13 原话"很多描述是不必要的，指令主要是面向开发者，而不是给广大玩家看"）：指令回执/错误/help 描述/补全 hint 一律短、干、事实化（如「原石 +100（持有 16100）」「未知物品: x」「开始自动抽卡 ×N」）；错误提示不再罗列全部合法值与教学句（补全与 help 本身就是发现手段）；派蒙的口吻由 LLM 转述层自加（工具描述写明"回执是开发者格式，请翻译成派蒙口吻"），回执层保持原始事实。**Why:** 指令系统定位=开发/作弊工具非玩家功能，冗余描述是噪声。**How to apply:** 后续新增指令、调试面板、控制台类工具的文案全按此基调；不要写"您可以…""支持…如…"式教程腔与拟人化语气词。
- [2026-09-13 22:58:06] 【存档零迁移政策·0.5.a 前】用户拍板（原话「无论是主游戏存档还是ai派蒙存档，都不需要有迁移，老版本存档直接删掉重建即可」）：0.5.a（正式落地版本号，届时用户主动告知）前，主存档与桌宠 pet.json 一律不写迁移——schema 语义变化=只升版本号+旧档删掉重建。**版本号改动权在用户（2026-09-13 追加拍板「规范加一条，ai不要修改版本号」）：AI 不自行修改 CURRENT_SAVE_VERSION/pet.json 版本门——升版=删所有旧档属破坏性决策，语义变化时列变更清单提请用户拍板升版**。主存档早已是重置式（2026-08-16 起，SaveManager「版本过低不迁移直接建档」）；pet.json 存量 v6→v8 链（PetPrefs.ParseAndMigrate：chatCiphers 分槽/quickMessages/你好）与主存档两处旗标迁移（MigrateFateItems/key 分槽）为历史代码，处置待拍板。**Why:** 开发期无真实玩家，旧档可丢，迁移=无谓工时；升版即删档故须用户把关。**How to apply:** 今后任何存档 schema 变更只升版本号+删档重建，勿写迁移链；升版本身也由用户拍板、AI 勿代改；已落档 docs/20 §1.6 + gic-save-system skill「版本策略」节。
- [2026-09-26 20:19:07] 【AI 编译验证自主权·编辑器没开/停在 Play 也一样】编译验证始终 AI 自行完成——编辑器没开也直接按「gic-editor-longtask skill」的编辑器/桥异常恢复流程拉起编辑器并核编译结果，全程无需询问用户；**编辑器停在 Play Mode 时自行 stop 退出再 refresh 编译验证**（2026-09-16 用户拍板原话「之后你可以自行退出play」）；运行时/Play 测试仍归用户。**Why:** 2026-09-13 用户确认原话「文档或记忆里应当记录有，你可以自行完成编译」。**How to apply:** 交付前编译验证是硬边界，编辑器关闭/桥死不是跳过编译或交给用户的理由，按流程自行恢复环境完成；本会话 23:2x 实证全流程可行。



- [2026-09-15 01:17:42] [feedback]【依据 docs 答来源/现状类问题必须整节通读，禁 offset 截窗起读】（2026-09-15 派蒙来源连环误答两轮实证）：用户问「派蒙模型来源于哪个网站」，我 read_file docs/19 时 offset=35 恰好切掉 L34 头行「当前生效：GI 官方模型（2026-08-24 落地，来自 models-resource 完整 rip，asset 328738）」，误锚到下文资产表的「模之屋原始 PMX 包」兜底行→首答错称模之屋；用户纠正「TMR 正是我当初下载派蒙的网站」后，我又拿「项目 PMX 包与 TMR 条目格式对不上」顶回去一轮；用户再纠「当前项目在用的不是MMD模型」才定位——答案在文档里 8-24 就写对了。**Why**：截窗漏读一行小节标题→整条证据链锚错；且与用户记忆冲突时未先回读全文就质疑用户。**How to apply**：①答「X 来源于哪/现状是什么/在哪」前，从节标题起完整读 docs 相关小节，勿用 offset 从中段起读；②用户纠正我的事实性答案且与文档记载冲突时，先重读文档原文核对，再决定是否质疑用户记忆。





- [2026-09-26 00:42:36] 【update_memory 精确匹配转录陷阱（2026-09-26 记忆整理实证）】转录 CODELY.md 既有条目文本做 update_memory 精确匹配时有两类 AI 无法自觉的错：①CJK 近形字误读——文件「效应/易伤」被我反复读写成「效果/易损」，肉眼复查多少遍都会自动纠错；②引号字形——弯引号 U+201C/201D 与直引号 U+0022 在条目间混用，目测无法分辨。症状=update_memory 报 Text not found 但看不出差异。**How to apply:** 批量删改记忆条目前，先把 oldText 落盘临时文件（write_file），用 PS 与 CODELY.md 逐字符比对（报分歧点码位：[int]$c[$i] vs [int]$f[$k]）确认 FULL FOUND 后再原样提交；rg -f 模式文件里含 + 的文本按正则解析会假阴性，验证一律 -F 固定字符串。
- [2026-09-26 22:12:37] 【TJGenerators MCP 上传/异步任务实操两坑（2026-09-26 实证）】①file_upload 预签名 PUT 偶发 403 AccessDenied（EC 0003-00000015/DetailErrCode 14006）——首张票据带/不带 Content-Type、禁 Expect 全被拒且时钟同步正常；**重调 file_upload 取新票立即 200，勿深调试 curl 参数**。②MCP 异步任务宿主并未自动拉起轮询（task_output 查 MCP task_id 报 Task not found），需把返回的 poll_command_powershell 落盘纯 ASCII .ps1 → run_shell_command 后台跑 → 退出后 check_task 一次拿结果（exit≠成败）。③任务产物 curl 下载偶发截断（-sf --retry 3 重下 + PIL load 校验完整性）。

- [2026-09-27 00:31:27] 【日辉多参考=画风锁第一张（2026-09-26/27 安柏七圣召唤画风重绘五版实证）】迭代链：v1=amber_glide+风格图双参考（人物像、画风被厚涂锚死）→v2=风格图唯一参考（画风对、人物跑偏）→v3/v4=人物锚「反复强化」循环（**画风纹丝不动——机制确认：日辉多参考的画风跟随第一张参考图，「内容跟A、画风跟B」执行不了，强化循环不收敛**）→v5=顺序对调（风格图第一+v1 第二，待目检）。风格锚点=扁平矢量/赛璐璐平涂硬边/细同色系描边/贴纸高光/糖果色（参考图=.codely/clipboard/clipboard-1790432863698.png，群像拼贴）；素材 v1~v5+对比图全在 .codely-cli/tmp/paperdoll_7star_style/。识图粗检再误报（v1 粗检称画风符合被目检否决，粗检不可信实证+2）。对策排序：换模型双参考试（qwen/混元，未实测）> 顺序对调 > v2 底+用户指认具体部位做定向局部重绘（唯一两轴可控路）。连锁：静态图若换画风定稿，32 帧动画需同画风重生成（或临时清 立牌动画帧 回静态）。
- [2026-09-27 22:16:03] 【美术交付禁程序拼合，必须纯 AI 整图】用户拍板（2026-09-27 原话「不要自己拼，让AI重新生成」）：游戏美术素材交付必须是纯 AI 生成的整图，**不得用「抠 AI 石子+羽化混边+程序随机摆位合成」类拼合产物交付**——即便碎片全部来自 AI 图、即便底色基调完全一致也要重画。与 2026-09-27 弧光贴图「不要程序生成，让AI生成图片」一脉相承（第二次实证，已从单案升格为通则）。**How to apply**：变体差异化/重排布/局部调整一律回生成工具重画（调 prompt 差异轴：体量/数量/形态）；程序后处理仅限像素级净化类（盘外清零/裁剪到内容框/降分辨率——既有已认可先例），不得改变图面内容构成。
- [2026-10-02 02:20:38] 【预留物一律不清除（2026-10-02 用户拍板原话「1.预留的东西都不应该清除」——审查/清理处置通则）】凡「有设计意图留位但暂无消费」的代码面（预留 API/组件/字段/枚举态/数据结构）一律不提删不清除——哪怕全项目零外部消费、哪怕没有任何文档引用（弱预留也保留）。**Why:** 力系统（ForceData/受力表）、Identity 修改器六死分支、Element setter、BoardType、UnitStatus 五预留态等连续两轮审查被报"死代码"，用户两轮拍板均为保留（第二轮直接升格为通则）；预留=未来玩法的占位，删了将来要原样写回。**How to apply:** 代码审查报告里"死代码/零消费"发现的处置只能三选一：①有文档/规则引用→判"未落地的预留"（表述与死遗留区分）；②无引用但属设计留位→判"预留"保留+挂账登记触发条件；③真正确认废弃（如已删除路径的残留）→才可提删且须用户拍板。**审查报告勿再把预留物列进"建议删除"清单**——gic-code-review skill「审查纪律与边界」节已同步。
- [2026-10-02 20:37:20] 【用户授权 AI 自行截图+识图看实际画面（2026-10-02 冻结表现调优时拍板，原话「你作为kimi k3，有识图能力，你可以自行看实际画面」；**同日追加拍板限定模型范围**：仅 Kimi K3 等自带识图的模型可自行识图，GLM 5.3 等非视觉模型不可——见 User 节「模型路由偏好」条目）】对 2026-08-10③/08-21「AI 不自行截图判定画面效果」规则的放宽：用户明确认可用截图+自身视觉能力做效果迭代自检（本轮冻结 shader 四轮截图调参已被接受）。注意与「识图 AI 不可作内容判定依据」（2026-09-10，图案内容判定须像素级测量+用户目检）区分：画面观感迭代自检可截图，内容/图案是什么的结论仍不可只信识图。最终验收目检仍交用户。
- [2026-10-03 13:05:56] [2026-10-03] 【桥脚本 PackAtlases 后内存句柄失效陷阱（2026-10-03 丘丘人批实证）】exec_editor_script 内 SpriteAtlasUtility.PackAtlases(全部图集, activeBuildTarget) 执行后，此前 LoadAssetAtPath/CreateInstance 拿到的 SkillConfig/Sprite 等对象引用会被销毁（报 "The object of type 'SkillConfig' has been destroyed"）——Pack 后不得再触碰旧句柄。**How to apply**：资产改动+SaveAssets 与回读验证拆成两个桥脚本提交（第二提交全新 LoadAssetAtPath），或 Pack 后全部重载再读。附：图集页 Texture2D（ASTC 压缩页 sactx-*）不可 GetPixels（ArgumentException not readable），sp.uv 逐像素读回在压缩页上不可行——退化验证=sp.textureRect（图集页真实区域，tight 打包区域≠256 方形属正常）+两 sprite 区域互异即可。
- [2026-10-03 22:16:52] 【AI 行为自动化观察 harness 已验证可用（2026-10-03 用户委托实测两局：8 回合抽样+28 回合打到核心分出胜负；技法全案=docs/14 §115）】用途：AI 决策类改动（三脑/评分参数）需要验证「眷属/伙伴行为是否正常」时，用户可委托 AI 自行开局自动对局观察（2026-10-03 拍板先例「替我开一局，自行打一把」——这是对 2026-09-13「测试交用户」规则的显式豁免：用户主动委托即可做，AI 不得自行发起）。核心链=LaunchSinglePlayer（P1 真人全 Pass 放权+P2 AI 配额脑）→反射 _familiarUnitActions 拿眷属真实决策→CompanionBrain.DecideAll 纯函数旁听伙伴决策（口径注记：P2 未交的近似输入）→回合末快照 diff→僵局检测（全场 HP 连续不变）。坑：Unit.Skills 元素是 BaseSkill（技能名走 RawData.skillID；编辑器侧 SkillConfig 数据体在 .data）；对称测试军双方同名单位按 unitId 聚合勿按 name；等待推进条件含 TurnNumber>turn；Temp/ 目录编辑器退出清空（全量日志要长存先拷出）。How to apply：AI 批次结构化行为验证默认走本 harness；画面观感类仍交用户目检。
- [2026-10-04 17:40:03] 【观察局 harness 纪律：进 Selecting 立即提交 Pass】（2026-10-04 用户提示原话「你似乎总是忘记，快速pass，这样就不用等选择阶段的时间」）AI 自动对局观察时每回合一进入 Selecting 就先把 P1 Pass 交掉——P1 先交后阶段只等 P2 的 ~0.8s 提交延迟，回合推进最快；且 Pass 已在，探针即使慢也只会阻塞主线程延迟开演、绝不触发 16-25s 自动 Pass 尾巴（选择时限=FirstTurn 25s/T2-6 16s/后缩至 8s，公式=TurnFlowController.GetSelectLimitSeconds）。探针反射调用放在 Pass 之后、整块保持同步零 await（同步块原子性=Resolve 协程无法中途插入），DecideAll 旁听放块尾（槽写在所有读取之后）。

### Project
















- [2026-09-12 14:24:31] Assets 清理定案（2026-08-16）：Material/Materials、Resources/Materials 三目录 8 个材质误删已 git 恢复，**勿再清理**（UIBlur/WishSmoke 在用，其余存疑也保留）；已删定案：Assets/Editor 空目录、Scenes/TestScene.scene、StreamingAssets/mc.mp4、Assets 根 6 个 CUsers* 垃圾文件。故意不做：NewAudioMixer、Art/Wish 与 WishArt 改名（Addressables 地址约定）；保留 GlowTest.scene+CardGlowTest.cs、兹白测试皮肤 zibai_skin_solid.png（UnitConfig 兹白 cards[1]，用户拍板不删勿当 bug 修）。**How to apply:** 不再动这批资产。










































































- [2026-09-26 20:20:15] 【搜索/文本判定铁律】search_file_content/glob/list_directory 受 .codelyignore+.gitignore 双层 ignore（Assets 的 png/prefab/asset/mat/wav、Mirror/kcp2k/Plugins 整目录、.codely-cli/.codely/Library/Temp/Logs/Export/Maps 等）——被任一层命中即**静默零命中/无名**，零命中≠不存在：查引用/资产内容/目录清单/skill 文件一律原生 `rg --no-ignore` 或 Get-ChildItem；喂多模态的 Assets 图片先复制到 .codely-cli/tmp；guid 判定一律 AssetPathToGUID/unity_asset get_info（meta guid 可能密文，文本反查假阴性，docs/14 §9）；.unity 与本地化表中文为 \uXXXX 转义，rg 中文直搜必零命中（搜转义形式或先解码，docs/20 §1.3；prefab 中文序列化字段名=大写 \uXXXX 转义、小写形式零命中）；.codely-cli/ 与 TextMesh Pro/ gitignored（skill/HANDOFF 勿提交；TMP 材质改动不入库，重装前备份）。细节 docs/14 §6.2。








- [2026-09-23 00:04:38] [project] 用户会并行开多个 AI 会话在同一项目分工开发：**回合开始先读 .codely-cli/HANDOFF-并行AI协调.md**（各会话文件归属/状态/编辑器使用权，状态变化写回各自小节）。git status 里非我产生的改动/WIP 文件（尤其未跟踪新目录）属其他会话在制品——勿动勿清理勿"顺手修"，除非它把整个程序集编译堵死才做最小语法解锁并明确告知；编译错误可能来自其他会话 WIP，先归因再动手；refresh/构建等编辑器级操作动前先看协调板避免撞车，被取消后勿立即重试，问协调节奏。












- [2026-09-26 00:33:38] [project] 【地图音乐与星落湖遗留】蒙德野外曲池（day 9/night 7，QQ FLAC 原生直转）与战斗音乐轮换链已全量接线（清单=PositionConfig 配置资产）；战斗开局 6:00 落夜晚池（改原神式 6-19 边界待用户反馈）。**遗留：MapConfig 星落湖锚点仍占位 (70,28) 待取点器「锚点重定位」标定（标定前坐标是假的勿当实数据）——涉星落湖锚点的工作先核标定是否已完成。**



- [2026-09-19 01:57:27] [project] UnityInsight 索引系统档案（活锁 bug 已提交 Bug Hunter，证据包=.codely\有效bug活动\已提交\index-build-failure）：架构=CLI 侧 node 守护进程（unity-insight-cli.js serve --daemon）持有全部索引，**被动模式**——杀掉不自动重生、重生后须 Cowork GUI 发构建指令（编辑器 AI/ 菜单只有 Check Connections/Force Reload）；数据目录=<项目>\.codely-cli\UnityInsight\（构建中=index.db.tmp+WAL，ready 后写 index.current 指针）。活锁特征：first_build 磁盘写入 ~6 分钟后冻结、进程 4~5 核满负荷+RSS 狂涨至 7.5GB+零 I/O、index_building=true 永不翻转、GUI 无进度无报错；~\.codely-cli\crash-logs\exit-*.json（uptime<1s）=单实例锁握手记录属常态勿误判崩溃。处置=Stop-Process 杀 daemon+清 tmp 三件套。

- [2026-10-04 23:48:19] [project] Bug Hunter 提交活动：权威状态板=.codely\有效bug活动\00-新会话交接总览.md（涉提交/查已立包状态先读它）；活动规则与四件套流程=全局 skill codely-bughunter；立包位置约定=待交包放 .codely\有效bug活动\ 根、提交后移 已提交\、撤案移 不正确\。**9/28~10/4 周 7 包（credit-deduction-order/resume-empty-memory/loop-recovery/model-identity/crash-3221226505/link-click/phantom-diff-2482）已全部移入 已提交\（10/4 20:3x 核实待交区已空，用户自行处理，网站侧以用户操作为准）；10/6 结算、兑换码一周内须核销**。**10/4 新立五包（待交区根，下周 10/5~10/11 配额候选）**：①tool-name-did-you-mean（功能建议：未注册工具名报错缺近似名提示——search_file 62 例自 2026-08-01 跨版本、read_shell_command 系 6 例等；误诊影响案例=df574ba6 会话曾把它误判成「-A/-B 炸调用」假 bug 写入记忆、同日复验勘误；bugSession=该会话 JSONL 已拷入+摘录+10-04 日志，截图拍摘录文件）②presign-403-first-ticket（行为异常偶发：file_upload 首票 PUT 403 TOS AccessDenied EC 0003-00000015/DetailErrCode 14006，9/26 21:58；重取票据即 200、时钟正常；10-04 复验首票 200 未复现；9/26 会话 JSONL+日志+摘录齐，截图拍 403 原文摘录）③shell-stderr-as-core-error（体验缺陷：PS CLIXML 进度 stderr 被误标 [ERROR][CoreProcess][Core Error] 落盘，本周 7 例 9/28~9/30；9/30 09:34 例伴随 stdoutClosed→core+hub 自动重启；9/30 日志+摘录齐，**bugSession=2026-10-04 20:39 取证会话导出已入包（另两包同文件作补充件）**）。④pet-drag-size-inconsistent（功能建议·Cowork 桌宠）：拖动像素松鼠左右移动渲染尺寸不一致——同 DPI 锚定（BUG 红字两帧恒等 11×21px）下两帧松鼠主体 448×588 vs 420×540px（宽-6.7%/高-8.9%），建议拖动全程锁定渲染尺寸。⑤pet-pixel-blurry-upscale（体验缺陷·Cowork 桌宠）：像素风松鼠放大后插值模糊——扫描线实锤 1px 渐变过渡带（轮廓从背景到身体 9~14 个 1px 渐变步=双线性放大非最近邻），建议 Point 采样（Unity Filter Mode=Point/GDI+ NearestNeighbor/WPF BitmapScalingMode）或整数倍缩放。两包截图/描述/像素测量/当日日志齐（测量工具链=.codely-cli\tmp\pet-bug\），bugSession=chat-export-current-2026-10-04-23-47-44-657.md 已拷入两包（四件套齐可直接提交）。判死新增：search_file_content -A/-B 炸调用=**误诊**（模型误发工具名 JSONL 实锤，全局记忆条目已勘误）。候选池挂账：MCP 异步任务轮询不自动托管（9/26 观察，下次 MCP 异步任务顺带核验）、crash-logs 握手噪声文件（弱）。导出拦截 toast=设计性行为勿当发现打包（10/3 用户确认）。





























- [2026-09-19 01:57:27] [project] 加载页势力徽标动画模式已定稿（2026-09-15 蒙德落地）：连通域拆层=底图静止+动层运行时连续旋转（LoadingOverlayDriver「徽标分层表」，转速默认 -40°/s 可调；未配势力=整标缓转零破坏）；官方徽标非旋转对称处（六叶=镜像三对 53°/74° 交替的纯静态设计）做循环动画须均匀 60° 重排（用户拍板「不用像素级对齐，大致即可」——帧循环必有接缝，运行时连续旋转优于帧序列）。工具链=.codely-cli/tmp/mond-anim/（split_v3.js/rebuild_blades.js/preview.js）。**后续势力徽标动画沿用此模式。**





















- [2026-09-19 01:58:22] 【派蒙语音现状与路由】游戏侧设计（三模式：关闭/本地侧车 127.0.0.1:9880/云端 MiniMax 自填 key；零进包；口型同步不做；覆盖=对话+抽卡反应+兜底全念、/指令回执不念）=docs/19 §6.5.11，P1a 已交付用户验证通过；**游戏外全档（模型谱系/训练/评测/侧车运维/yaml 绝对路径铁律/训练纪律三不/待办）=docs/27，语音话题先读它**。现状：用户拍板「先采用 A 组零训练优化，停止训练」（v4fullc e2 检查点保留，重启=train_pm_v4.py --skip-prep，A 组四件套全交付）；**等用户耳检 compare\paimon_compare_{reftable,grid,nbest}.wav 三份拍板**：情绪参考表哪些档采纳（游戏侧接线 paimon\refs_table.json）、n-best 守卫是否产品化；B 组（s1 补训/rank 升档）备选。侧车 9880 现挂 v4full e8 真权重、start_paimon_tts.bat 拉的是 v2 yaml（玩家默认=v2，勿混）。发布期议题=派蒙语音包可选组件安装器（4-6GB）或云 key，届时再拍。


















- [2026-09-19 01:58:22] [project] SkillDetailView 点外关闭竞态已修（战斗实例设 `点外关闭=false`，点外收面板由 OnBoardTap 承接；教训=同一交互目标被两个系统响应必须一方显式让位，勿依赖帧内执行顺序——全案 docs/14 §64b）。**背包场景同款竞态（点源图标面板闪动重开）仍存在未修——用户未报障勿主动动。**



- [2026-09-23 00:04:49] 【搜索铁律补遗（2026-09-20 复验）】search_file_content 的 glob=锚定于搜索根的 gitignore 式语义：含 / 的模式须从搜索根写起（从工作区根搜 Assets 下 UI 目录须 **/UI/**/*.cs）；glob 零命中先加 **/ 前缀或换 path 参数复核再下结论，勿直接判工具 bug。全项目 .cs 文件头模板 using 已清理（291 文件 867 条，2026-09-19，编译逐轮 0 错）——`using GIC.X` 逐文件检索可作依赖方向审计依据；**例外=17 个豁免文件（玩家侧 #if×16+CardGlowOverlay 混合换行）仍带模板头，审计到它们须核类型实际使用**。清理工具链四坑=docs/14 §66。






- [2026-09-27 14:15:45] 【战斗表现纸片人方案与素材路线定案（2026-09-20 用户拍板）】GI 动画提取判死后战斗表现=纸片人方案；GI 七圣卡面 Spine 素材路线被否决（卡面人物只有半身/坐姿场景图不完整）→纸片人素材走 AI 生成（安柏滑翔立牌终版=提交 ba3d847：v4 基版+AI 局部擦除中上多余第三翼，用户拍板局部擦除优于全新生成、v5 退役；站姿 v2 已作废（2026-09-27 用户拍板「作废，不要管它」）勿再引用）。生成工作流+立牌设计规则+局部擦除配方=gic-paperdoll skill，后续角色直接复用。Spine 技术管线已验证保留（skel 4.0 解析器+AtlasDump harness，坑清单在工具链内；工具链=.codely-cli/tmp/paperdoll_amber_gcg/ + webrefs/spine-paperdoll/），将来做 GI 素材骨骼动画可复用。**How to apply:** 战斗单位表现素材走 AI 生成立绘；GI 提取素材只做图标/头像/卡面类完整资源。





- [2026-09-23 00:04:54] 【面板预热泵已提交（7902e0f，2026-09-21，用户验证通过）】UIManager.PrewarmLoop=等 Splash 就绪+30 帧开泵+根转场锁在途挂起/转场毕+60 帧再续；WishScreen 4K 立绘 Preload 提前至 Splash 期。**症状归因**：启动动画卡顿/进厅首开异常/跳过 Splash 失灵先查 docs/17 §5b 泵行。**PreloadRegistry 统一注册表候选仍未拍板且未登记 docs/11**——后续时序重排/B6 时再提请拍板。




































- [2026-10-01 11:40:26] 【AnimeStudio issue #124 回帖/PR 待用户拍板（2026-09-26 起）】官方动画提取路线终局=放弃自研管线、战斗表现维持纸片人（教训=数据级验证≠视觉判定，docs/14 §38b）。英文追评草稿已备（4 点：归因认错/muscle 无骨路径 else 丢弃属实/GI 双层架构增量/「几个月前能导」三点证伪）；DBACL streamer IntPtr.Zero bug 修法已验证（streamer=dbAligned+bulkOffset）可单开 issue/PR——**是否提交回帖+是否开 PR 待拍板**；证据链=webrefs/gi-animation-extraction/README。（2026-09-26 合并两旧条目）】路线终局（2026-09-23 深夜用户拍板原话「放弃自研管线，效果与期望许多处不一致」）：战斗表现维持纸片人（已落地不受影响），GI 动画提取不再为表现层候选、自研 muscle 管线不重启；教训=数据级验证与视觉判定分离（docs/14 §38b）——RootT.y↔m_ValueArrayDelta 逐位互证/FK 头高/摆幅>10° 只证明解码与求值数学正确，不保证目检达标。issue #124 追评口径（数据级事实、不 claim 视觉验证）：①我们 issue 归因写错需追评认错（m_ClipBindingConstant 4.3+ 正常读）；②humanoid muscle 无骨路径落 else 丢弃属实；③GI 双层架构（身体动画在共享 clip Ani_Avatar_<BodyType>_*）=回帖最大增量；④「几个月前还能导出身体骨」已三点代码级证伪（根快照 ee88e924 全树零 muscle 文件+301 全史零实现+现行 master 同构）；FBX 独立审计=Odette 五 take 各 138 物理装饰骨零身体骨（1242=138×9 曲线口径、1387=ACL 口径 138×10+7，勿混）。DBACL streamer=IntPtr.Zero bug 上游 master 仍在（AnimeStudio.Utility/ACL/ACL.cs:152），修法已验证（streamer=dbAligned+bulkOffset），可单开 issue/PR。英文追评草稿（4 点+PR 意向）已备——**是否提交回帖+是否开 PR 待用户拍板**；技术存档与证据链=webrefs/gi-animation-extraction/README（终局节+代差表，完整配方可重跑）。
























































- [2026-09-26 21:06:34] 【git quotepath 转义陷阱（2026-09-26 GIC 实证）】git diff --name-only 对非 ASCII 路径输出带引号+八进制转义（形如 "docs/00-\346\246\202\350\277\260.md"）——PS 拿它 Join-Path/ReadAllText 必炸 Illegal characters in path（且异常后变量残留 null 会产出假「pureLF」结果，极误导）。正解=git -c core.quotepath=false diff --name-only（原始 UTF-8 路径）；本仓文件名全中文，凡 shell 管道消费 git 路径输出一律加 quotepath=false。同类坑：rg --files 与 Get-ChildItem 输出不受影响可直接用。



















- [2026-10-01 11:40:26] 【战斗系统全量复审已收官（2026-09-30，批1~批9 九批全部完成）】总判=零结构级缺陷（Host 权威+快照+命令流的回合制定式架构、单出口/对账纪律、三道防线全部经行业对标；**勿再提议换架构/换 ECS，勿把旧审查条目当未决议题重报**）。批8 遗留拍板三项（占位 Warn 去重/配置重复键防线等）+批9 顺手项挂 docs/11 待拍板；各批修复与收口明细=docs/18 决策十四~十七+docs/11。（2026-09-27 用户立项；**批1~批9 九批全部完成，2026-09-30 复审收官**）】用户拍板：review 战斗系统+关联全部分批进行（每次对话一批，「开始/继续」按地图取下一批）、**不采信任何旧 review 记录**、逐处对比行业成熟做法（每批输出行业对比+🔴🟡🟢 报告，格式=gic-code-review skill；旧记忆「三轮审查已闭环（fad5c87/f2c171a/ce335f3）勿再重跑」指那批修复不拦本复审）。**全工程总判：九批次零结构级缺陷**——架构（Host 权威+快照+命令流=回合制定式正确）、纪律（单出口/对账/校验行/配色收口）、防线（Host 校验/编辑期校验/镜像安全网）三层全部经得起行业对标。各批结论：批1 模拟核心+协议（0🔴8🟡 修复落地）/批2 技能链+原子库（0🔴5🟡 落地）/批3 Buff+经济（1🔴=Card 族跨层→B 认账豁免；5🟡 ②④⑤已收口）/批4 移动投射物（0🔴1🟡 A 勘正落地）/批5 AI 双脑（0🔴7🟡）/批6 表现层（0🔴5🟡 全修+连携协议稿 active/31）/批7 HUD 交互（0🔴7🟡 全处置+落格 toast 两拍追拍）/批8 配置+工厂+编辑器（0🔴4🟡 **仅拍板 #4 相机守卫部分未做**，#1 占位 Warn 去重/#2 SkillFactory 死重三 API+反射空扫（注册数=0 实证）/#3 配置重复键 BuildCache Warn+编辑器校验行——**三项挂 docs/11 待拍板**）/批9 组合根+联机横切（0🔴2🟡 **已修**：①LocalBattleTransport 每消息 Debug.Log 删——序列化验证执行保留；②BattleScreen 相机守卫补全 Error+DoClose）。批9 就绪度结论：B7 前置架构（IBattleTransport 抽象+本地 JSON wire 直通+即时通道五道 Host 防线）高于行业同期，剩余开口全为协议补丁级（per-client ack/回执/过期语义，docs/11 在册）。批7 落地明细+批3/批4 收口明细见前条记录。**批8 报告遗留拍板三项+批9 顺手项（冗余 using×2/Awake 空体/OnValidate 空体/SerializeField 冗余/行为档案注释勘正）均未批未做**——后续会话按 docs/11 挂账或再提请拍板。收尾提交面（2026-09-30）：批7 修复批 26 文件（12 Localization+10 cs+4 docs）+批9 修复 2 cs（LocalBattleTransport/BattleScreen）；gic-battle-hud skill 两条目已同步（落格即提示）；工作区前批遗留 CODELY.md/zh-cn SDF.asset 未提交件随批处理。





- [2026-09-27 22:57:41] 【伤害/治疗取整终版=末点截断（2026-09-27 深夜用户拍板「对于最终的小数点，比如最终的伤害，最终的治疗，舍弃小数点」——承接同日修复批第⑩项勘正）】DamagePipeline.Calculate 最终伤害与 EffectCompiler.ResolveHealAmount 最终治疗 RoundToInt→FloorToInt：中途全 float、末点单次截断（舍弃小数），勿在中途取整。净效果：基于 maxHp 的伤害/治疗值与修复前 int 截断口径**完全一致**（修复批交付时说的「特定血量±1」收回）；唯一相对修复前的变化=攻击百分比伤害的小数舍去（如 67.65→67 而非 68）。同批补 TileType.None=0 显式枚举成员（地形批 dc9d41e 入库解锁撞车后补上，GetTile/CharToTile 全改引）；复检已通过、编译 0 错 0 警、修复批+追加批仍未提交等拍板。How to apply：后续新数值出口（护盾/反伤等）一律同口径——float 计算到底、末点单次 FloorToInt；勿用 RoundToInt。

- [2026-10-01 11:40:26] 【统一消耗模型 C-1~C-2 已落地（2026-09-28 用户验证通过，b4f914f）】技能消耗全量数据驱动：SkillData.costs（CostKind=元能/体力/摩拉/Item×ItemName/AnyItem+subType）+ResourceGate 检查/扣减唯一出口（先全查后全扣；低级单位豁免玩家资源、元能不豁免；「任意X」=AnyItem+subType 跨同类凑足、原子支付、货币卡恒不匹配；CollectAnyItems 物化列表勿改回惰性枚举——扣减循环内 LoseCard 移除条目会炸）。存量 25 资产已迁移、StaminaGate/GetStaminaCost 旧链退役；**EnergyCost 参数保留仅作描述渲染单源勿删**（运行时真源=costs，漂移由 UnitConfigEditor 校验行拦截）；21/34 单位无 Move 条目=BattleMetrics 常量兜底主路径；AI 双脑/HUD 门槛全走 costs 感知。C-3 首个真实消耗技能随角色批（docs/11）；话题先读 docs/active/30+docs/18 决策十五。（2026-09-27 深夜用户拍板「统一消耗模型」，批3 复审产物+用户设计输入驱动）】起因链：用户「未来技能不一定消耗摩拉或体力，甚至可以是消耗酒、苹果等食物」+批3 发现消耗管道四种资源四种写法。设计=**docs/active/30**（新）、拍板=docs/18 决策十五、登记=docs/11 战场与经济节。核心：SkillData.costs 数据驱动 (资源,数量) 列表（CostKind 元能/体力/摩拉/Item×ItemName）+ResourceGate 统一门（收编 StaminaGate）+MoraSpend/ItemConsume 新效应+ItemConsume 新命令（客户端手牌镜像分流+角标即时刷新）+HUD 置灰单源化（预判/结算同形）+双轨迁移（存量技能回落零行为变化）。门槛语义：不足=落空不扣/先全查后全扣/低级单位豁免推广到 Mora·Item（元能不豁免）。批次 C-1 管道→C-2 存量迁移（EnergyCost/体力分档/移动常量→costs 资产+回落退役）→C-3 首个真实消耗技能（随角色批）。**随批裁决：货币身份三处硬编码=永久两件套不单源化**（ItemSubType.Currency=背包分页语义≠战斗池化身份；原石 maxPrepareCount=100 反例否决「Currency&&maxPrepare>0」派生式；「表层是卡、底层是账户」模型自洽，批3 🟡#3 销案）。How to apply：用户说 C-1/开始实施即按 active/30 施工；UseItem 物品使用行动=B8 收编勿另起管道；GainCardEffect 获得对偶随 C-3。




















- [2026-10-01 11:02:09] 【B-S4 立牌动作序列·当前状态（2026-09-29 收官）】安柏待机=amber_idle_loop（v7b 终版：77 帧/3.21s/零贴边/回绕 0.53×）、移动=amber_fly_loop，均已接线入库；素材统一朝右+朝向镜像翻转规则=gic-paperdoll skill。**剩余=安柏战技/爆发立牌动画（用户拍板暂缓待启动，配方=gic-paperdoll skill 直接复刻；此遗留未登记 docs/11，勿当已完结）**；另凯亚 idle 循环动画=可选待拍板（B-S3 绿幕视频路线）。（2026-09-29 用户目检「验证通过。收尾」；随批提交）】v7b 终版即前条（77 帧/3.21s/零贴边/回绕 0.53×、无特效提示词版）；提交面=amber_idle.png+meta/amber_idle_loop.mp4+meta/UnitConfig 接线/docs 14§91连带二/17/18+CODELY.md。遗留：B-S4 序列下一件=战技/爆发动画（待用户启动，配方=gic-paperdoll skill 三件套直接复刻）；amber_fly_loop 留库=「移动中」素材待 B-S4a 动作轨接线（docs/11 已登记项）；8 次生成累计 3000 积分。





















- [2026-10-01 11:40:26] 【角色数值引用铁律（2026-09-30 芭芭拉实证）】引用星级/血量/属性前必查 docs/units 文档头或 UnitConfig——勿拿原神原设稀有度代入（芭芭拉原设 4★、本项目 3★200 血）。歌声之环治疗=5% 施加者最大生命×层数（勿再做各自/目标基准，docs/18）；tick 伤害基准=施加者攻击力（观感确认点：不符改 SongOfLifeBuff attacker source→owner 一行）。（2026-09-30 拍板「原本为10点固定值，但这无法成长」+用户纠偏「不是各自！而是基于芭芭拉自己的最大生命值」——**勿再做各自/目标基准**）】SongOfLifeBuff.HealPerTurn=10 退役→HealPercentPerTurn=5（% 施加者芭芭拉自身最大生命=BasedOnMaxHealth 施法者口径）；实现=OnTurnEnd 循环外预计算 heal=Mathf.FloorToInt(attackerStats.GetStatStruct(StatType.HP).Max×5/100f)×层数（attacker=source??owner 与伤害同基准单位；float 末点截断同 ResolveHealAmount 口径；heal>0 才发 HealEffect；3命×层数语义不变；全部我方目标同值）；RelatedDescription 歌声之环五语言=「治疗…5%施加者最大生命值 / 5% of the applier's Max HP / 付与者の最大HPの5% / 5% макс. запаса здоровья наложившего」+CSV 重导出+芭芭拉.md 歌声之环节与现状行同步。数值（芭芭拉 3★200 血——**教训：星级/血量引用前必查 docs/units 文档头或 UnitConfig，勿拿原神原设稀有度代入**，芭芭拉原设 4★、本项目 3★）：全员治疗 10/层〔200×5%=10，与旧平值完全恒等〕、3命 2 层=20；伤害/元能/附着/倒下消失语义不变。**B-3/B8 条目与文档中「治疗 10」口径以本条为准（取代同日早前「各自」错误版）。**





- [2026-09-30 23:54:10] 【战斗相机初始缩放=用户实测舒适值（2026-09-30 拍板「我已经暂停游戏，当前缩放是最舒服的缩放，把这个值作为初始默认」）】BattleScreen.unity 的 BattleCamera 摆位由 (0,29,−20)≈视轴 35.23 改为 (0,16.50061,−11.55385)=视轴距离 20.14353（俯角 55°、注视原点不变）。初始缩放唯一真源=场景摆位——BattleCameraController.InitFromTransform 在 Awake 从 transform 推导 focus/distance，**代码字段 _distance=35f 是被覆盖的死值，调初始缩放改场景相机摆位勿改代码字段**。活体取证流程（可复用）：用户暂停态 exec_runtime_script 读 CurrentDistance（本例 20.14353，_targetDistance 同值=已收敛）→ stop play（值已取到，2026-09-16 拍板可自行退出）→ additive 开场景 SetPositionAndRotation(−forward×D, Euler(55,0,0)) → SaveScene → 重开回读断言（屏心射线交 y=0 于原点=距离即 D）。随棋盘 15×15 批未提交。


- [2026-10-01 20:20:05] 【凛冽轮舞 buff 型实装+复测五拍板+减防取证返修已落地（2026-10-01 用户拍板「凯亚爆发实际并不是召唤，与歌声之环类似，都是buff」+复测拍板①碎裂即时②减防10层12回合③施放获得2层确认④多层逐层各弹错峰0.15s⑤元能也逐层各跳且补元能数字弹出；未提交待复测）】凯亚爆发=自施放 Buff 型零召唤系依赖（落档 docs/18 决策十九+凯亚.md+芭芭拉.md+docs/11 销案；编译 0 CS 错全绿）。①**无目标自施放爆发新形态**=时轮 aimMode=None（SkillData.IsSelfCast 三消费方=Host CompileSkill 跳过判定编译/HUD IsLineSkill 排除+瞄准域=自身格/CompanionBrain ScoreSelfCastBurst 自身增益档 45）；初始层数=ShardCount 经 ApplyBuff value 通道注入。②**寒冰之棱 BuffType.Icicle=6**（永久+倒下仍生效+碎裂即时触发〔拍板①〕——元能增益落地后 IcicleBuff.TryShatter：存活且元能严格大于 50%→逐层治疗 30% 施加者攻+RemoveBuff 即时注销；tick=回合末半径内敌逐层 20% 攻冰伤含尸体）。③**多层=逐层各弹一次（拍板④+⑤）**：BattleMetrics.BuffLayerStaggerSeconds=0.15f——寒冰之棱 tick 伤害（launchMs）/碎裂回血（HitSeconds）/歌声之环伤害/治疗/**元能（拍板⑤「元能条也逐层各跳，每次+10」）**逐层独立弹、2命减防逐层各施加；**元能逐层可行=EnergyEffect.MergeKey() 新单源方法**（ApplyEffects+MergeEnergyEffects 双消费）——BuffTickGain 类别键并入层时刻，其余类别维持纯(目标,类别)（**战技多命中 B6a「多次命中只获一次」去重勿破**——其 HitSeconds=真实命中时刻，并入键会让两发箭矢各自成键）；**理智/附着=总额单发**（Sanity 命令按目标去重会吞逐层尾条、附着覆盖幂等——勿逐层）。④**元能获取数字弹出=已接线暂不弹（拍板⑤「目前少了元能数字弹出」曾启用；**同日拍板翻转「不要整个删掉，只是暂时决定不弹」**——实现+两处调用全保留，开关=BattlePlayer [SerializeField]「元能获取弹数字」默认 false，勾选即恢复；恢复改开关勿改代码勿删链）**：SpawnEnergyNumber（Palette.元能条色单源、复用 BattleDamageNumbers 屏幕空间层、随机偏移防重叠；消耗不弹；即时/launchMs 到点两路径）——覆盖移动+10/战技命中+10/协奏/Buff tick 一切正增量。⑤凯亚 C2/C3 命座激活（2命叠层上限+1+DefenseDown=7 减防 10 层 12 回合叠时长、3命上限 4+半径 2；SourceConstellation 上收 BaseBuff）。⑥**冰棱 tick 减防取证返修（2026-10-01 报障「3命凯亚寒冰之棱伤害一直 8，减防看似未生效」+现场取证授权）**：暂停态活体取证实锤——DefenseDown 完全生效（敌 Hilichurl/Amber 挂 level=4〔-20 防〕turns=47=12×4−1 ✓逐层施加与时长累加正常；敌方全体基础防御=0〔baseDefense=-64=auto 哨兵、星级回落 0〕）、凯亚 C3 攻 40 → 每层 8=floor(40×20%) 数字本身对；**恒 8 根因=tick 平直值不进 DamagePipeline 不吃防御/易伤（Burn 族口径）→ 减防对 tick 天然无效**。返修=tick 改走 DamagePipeline（AttackPercent=20，吃目标防御/易伤乘区；不经反应预览=维持不反应不附着；凯亚 1命吸血数据位全工程零消费无管线副作用）——敌防 -20 时每层 8→9.6→9。**歌声之环 tick 同改走 DamagePipeline（2026-10-01 拍板「这些伤害都应该统一」）——技能型光环 tick 全统一吃防御/易伤；Burn=元素反应 DoT 维持平直值（反应族是否统一待拍板已问）**。⑦**吸血结算已实装（2026-10-01 用户问「当前吸血还没实际生效吗」——实证此前仅数据位全工程零消费）**：挂点=TurnResolver.ApplyEffects DamageEffect 应用分支——攻击者存活且 LifeSteal%>0 → 回血=FloorToInt(实际伤害×LifeSteal%)，**一切伤害源统一结算**（战技/箭矢/爆发/tick/燃烧）；不经治疗效率；状态主循环内联+HealEffect 循环后并入 effects=命令载体（**foreach 内 List.Add 会炸枚举器——先收后并**）；凯亚 1命 C1LifeSteal(+50%) 正式生效。⑧**治疗效率对一切回血生效+施法者侧双乘区（2026-10-01 拍板「治疗效率应当对所有的回血生效，无论是吸血还是被治疗」+追加「发起治疗者也应当乘治疗效率，例如+50%的芭芭拉治疗其它角色；但自己治疗自己不乘两次」——推翻此前「吸血不经治疗效率」临时口径）**：双侧乘区单出口=**EffectCompiler.ApplyHealEfficiency(caster, target)**——施法者效率×受疗者效率/100（默认 100 恒等/负钳 0/整数地板；**施法者==受疗者只乘一次**=self 双乘防线：150% 效率自奶=×150% 非 ×225%）——四消费方：被治疗〔ResolveHealAmount 末段〕/吸血〔施=受=攻击者自身单次〕/歌声之环 tick 治疗〔施=施加者芭芭拉、受=各我方各自〕/冰棱碎裂回血〔施=施加者、受=持有者〕；**凯亚 1命 C1HealEfficiency 语义扩为双向**（受疗+施法加成，冷血之剑自疗=self 单次 ×150%）；docs/20 §5.1 通用规则已录（新增回血点一律调单出口勿手抄）。⑨**1命吸血报障取证返修+Percent 双语义统一（2026-10-01 实战报「1命凯亚攻击后无吸血」+暂停态取证→同日拍板「为什么不用 Percent？应当全部统一」推翻首版 Fixed 方案）**：根因=**Percent→BasePercent（基值×(1+50%)）对基值 0 的 LifeSteal 恒 0×1.5=0**（修改器表实挂、终值 0；同配置 HealEfficiency 基值 100→150 侥幸生效=「提升X」在百分比属性上天然双解）。**终版=Percent 双语义按属性族统一分流**：ConstellationApplier 新增 **PercentPanelStats 收口集**（HealEfficiency/LifeSteal——百分比面板属性〔0~100 效率刻度〕的 Percent=**+X 个百分点**〔BaseFlat，GI 命座口径〕；表外点数属性〔移速/攻速〕Percent=相对提升 BasePercent ×(1+X%)）；资产 **C1LifeSteal 改回 Percent 与 C1HealEfficiency 统一**——配参数一律写 Percent、属性族分流自动裁决勿按基值挑 baseType（docs/20 §5.1 通用规则+docs/14 §105 已重写：旧「0 基值配 Fixed」结论作废；未来暴击/暴伤/充能类落地时入 PercentPanelStats 表）。1命凯亚吸血终值：50%×实伤×治疗效率 150%（self 单次）=75% 实伤回血。**取证方法论**：暂停态 FindObjectsOfType<GIC.Battle.Unit>() 直读 Host 逻辑体（本地对局同进程）——Buffs 实态/UnitStats 终值/Unit.ConstellationModifiers 修改器表（stat/type/value 直接看挂了什么）——「修改器已挂但终值不变」指纹=先查 0 基值×PercentPanelStats 表；RawData baseXxx=auto 哨兵值（-64）勿当真实值、真实基础=星级回落。**首版 MarkedForRemoval 已删（勿找）**；AI 已满层 IsAtStackCap 不重施；玩家不可手操凯亚爆发=伙伴门控预期。**症状归因**：凯亚放不了爆发=伙伴门控；点爆发只亮自己格=自施放预期；碎裂在元能跳变同拍非回合末=拍板①预期；2 层 tick 对单敌连弹两个 20% 数字=拍板④预期；**被减防敌人冰棱 tick 数字>未减防者（-20 防 9 vs 0 防 8）=返修后预期**；3命环 2 层=伤害/治疗/元能各弹两次（元能每次+10）；一切元能获取弹+N 白字=新系统预期嫌吵再收窄；2命敌身上冰图标=减防+附着并存；施放无专属视觉=B-S3 素材挂起。**BattlePlayer.cs/BattlePalette.cs 与并行会话决策二十（伤害数字配色）同文件并存**——两批 hunks 互不触碰，收尾提交时注意协调归属。










- [2026-10-01 12:05:25] 【伤害数字配色=元素色+反应独特色（2026-10-01 用户拍板「数字的颜色应该取决于元素的颜色，并且造成蒸发，融化等反应时需要有独特颜色（而不只是火元素颜色或水元素颜色），这样就与原神一致」）】普通伤害数字=伤害元素色（单源 ElementFactionConfig.GetElementColor，与箭矢染色同源——物理灰白/火红/水蓝/冰浅蓝）；蒸发/融化反应命中=独特混合色非两元素色（BattlePalette 新增 反应蒸发色=品红〔火红×水蓝〕/反应融化色=暖粉〔火红×冰浅蓝〕，Inspector 可调）；冻结仍按命中元素色（与「冻结数字不带名」同口径）；治疗=治疗绿不变。实现=BattlePlayer.PlayDamageCoroutine 增 element 参（Damage 命令 metadata 元素载荷本就在）+ResolveDamageNumberColor 单出口；伤害红退役为倒计时告急+兜底。落档 docs/18 决策二十+gic-battle-hud skill 已同步（勿"修"回统一红）。观感确认点：各元素数字与元素色一致、反应数字=品红/暖粉。
- [2026-10-01 13:41:41] 【伤害数字配色网检勘正已落地（2026-10-01 用户复测否决首版「观感和原神里不一致，去网络搜索看看」→ 网检后二次落地，取代同日 12:05 品红/暖粉首版）】①根因=首版数字复用 ElementFactionConfig 元素**主题色**（图标色）偏深偏饱和，而原神数字=**亮彩霓虹风、显著亮于主题色**（火=橙 #FF9B00 非红 #EF5350 为主感差异）。②落定：BattlePalette 新增 **伤害数字元素色** 九字段独立段（物理白 #FFFFFF/火橙 #FF9B00/水亮青 #33CCFF/冰冰青白 #99FFFF/雷淡紫 #E19BFF/风薄荷 #66FFCC/岩淡金 #FFCC66/草黄绿 #BAFF37/光淡暖金白 GIC 自定）——**数字色与元素主题色（图标/箭矢用）两套语义勿再混用**，ElementFactionConfig 不动；反应色勘正：品红/暖粉（AI 自创无信源）→ **蒸发/融化=反应金 #FFCC66**（原神两放大反应同金色、靠名前缀区分）；治疗绿→原神黄绿 #BCFF37。③基准=社区原神复刻（raueyhs/Baity Minecraft mod fancydmgsplash 色系，调研档案=webrefs/damage-number-colors/NOTES.md）——中英文 wiki 均不记载屏幕数字颜色；非官方文档值，全部 Inspector 可调。④网检方法论沉淀：Reddit/Bing/zendesk/Fandom 网页全被 CF 拦（Fandom api.php 例外但页面不记载数字色）、DDG html 端点=curl 友好搜索路线（会限流）、GitHub 代码搜索（CredMan token）是挖"屏幕色值"一手源的有效姿势（搜 genshin damage ocr/inRange）。与并行会话（凛冽轮舞批）BattlePlayer.cs/BattlePalette.cs 同文件并存已核。观感确认点：各元素数字=亮彩（火橙非火红）、反应数字=金色、治疗=黄绿。
- [2026-10-01 19:53:20] 【伤害数字三反应互异色终版落地（2026-10-01 用户二次拍板「不对，原神里反应都有自己的颜色」推翻同日"蒸发/融化=同金 #FFCC66"方案；取代前条网检勘正的反应金结论）】证据=biligame 官方 wiki 反应图标像素采样（webrefs/damage-number-colors/NOTES.md+icon_*.png）：**原神每个反应图标=该反应两母元素的双色调**（蒸发=火橙#CA6841×水青#51CAF1、融化=火橙×冰青#448E99、冻结=冰蓝#4BABC4×深蓝、超载=火橙×雷紫、超导=冰青×雷紫；超载/超导与 Baity #FF809B/#B4B4FF 互证=图标混合逻辑）。落地=双色调直混（火×水/火×冰近似互补）落紫灰带且撞雷淡紫无区分度 → 按母元素冷暖拆分三反应互异色：**蒸发=亮品红/蒸汽粉 #FF66D9（BattlePalette.反应蒸发色）、融化=珊瑚红 #FF6A5A（反应融化色）、冻结=冰晶蓝 #5A8CFF（反应冻结色新字段）**——冻结数字仍不带名但带反应色（旧控制反应口径只涉名不涉色，勿当回归修）；ResolveDamageNumberColor 反应分支改 switch 三 case。色相=图标推导值非官方文档值，Inspector 可调。元素数字色板/治疗黄绿不受本勘正影响（前条结论仍有效）。观感确认点：三反应三色互异、不与元素数字撞色（蒸发品红 vs 融化珊瑚 vs 冻结深冰蓝）。
- [2026-10-01 20:04:23] 【反应名三反应全带名+冻结本地化键落地（2026-10-01 用户三次拍板「反应名都应该加上」——冻结不再例外；编译 0 错）】反应名前缀旧口径「仅增伤反应（融化/蒸发）有名、冻结控制反应不带名」作废——ReactionNameOf 增 Freeze 分支，三反应数字全带名+带反应色（名色分离：控制反应无伤害加成的数值语义不变）。本地化新键 `Battle_ReactionFreeze`=UIText **12035**（战斗域续编，段内最大 12034+1）五语言：冻结/凍結/Frozen/凍結/Заморозка（官方术语名词式，与 Melt/Плавление 对齐）；桥脚本加键+写值+码位级回读核验+CSV 重导出（Export/Localization/UIText.csv 列序=Key,Id,zh-Hans,en,ja,zh-TW,ru——zh-TW 在 ja 后）。**凍字形陷阱（本次实证，CJK 码位写入版）**：繁体「凍」=U+51CD（Unihan 三验：10 画/冫部 8 画/def=freeze；zh-Wiki 凍結地球·凍結線词条同码位）；**U+51DD=「凝」（16 画）、U+51BB=简体「冻」（7 画）**——凭记忆写 \u 转义先错 51DD（凝結）被控制台渲染暴露，Unihan 笔画数+wiki 词条码位双路实证定案。**How to apply：CJK 字符码位不确定时勿凭记忆写 \u 转义——查 Unihan（unicode.org/cgi-bin/GetUnihanData.pl?codepoint=XX，看 kTotalStrokes/kRSUnicode/kDefinition）或 wiki 词条 JSON 的 \u 码位，写完码位级回读核验**（肉眼复查多少遍都会自动纠错，与 2026-09-26 读侧陷阱条同源——读侧近形字误读、写侧码位凭记忆，防的是同一个 AI 盲区）。
- [2026-10-01 21:20:29] 【双蒸发修复已落地待复测（2026-10-01 实战报障「敌人只有一层水元素，安柏战技射箭两次都造成蒸发」）】根因=反应预判只读片前快照的 dyedElement、消耗在效应统一应用才落状态——同片多命中（逐发独立反应，决策八）重复消耗同一附着。修法=**片内附着编译视图**：BattleSimState 编译窗口括号（Begin/EndCompileDyeView，TurnResolver 片段+即时段紧贴 TakeSnapshot/ProjectileResolver.Resolve）+GetCompileDye 视图优先回落快照（未开启=旧行为）+EffectCompiler Damage 原子反应即消耗/AttachElement 原子覆盖推进；真实状态仍由 AttachElementEffect 统一应用写入。编译 0 错；全案与「消耗性资源编译视图 vs 并发基石快照」分类法=docs/14 §106；规则与文档勘正=docs/06 §6.3+active/22 §B5③+三处头注释。未提交待用户复测（六项：双箭仅首发蒸发/水图标变火/箭雨仅首段/火附着再吃水弹正常蒸发/冻结不回归/获能仍+10 一次）。已知简化=反应后来袭元素残留附着（「覆盖=消耗」单值模型既定行为，多层 1:1 待层数批次，用户若要「反应后无残留」属设计变更再拍板）。
- [2026-10-01 21:35:52] 【所见即所得（WYSIWYG）拍板链+投射物到达序两遍法已落地（2026-10-01 用户拍板「应当始终遵循所见即所得」，承接双蒸发修复同日）】①决策二十一（docs/18）：附着消耗/反应判定按**命中时刻序**推进，枚举序只保留给同刻并发（真同时互杀快照语义不变）；连携软窗开启时 Host 演算不得越过未决窗口（active/31 §3.3 旧「片边界插入起步+A′」作废，定案=中断-再续：命中时刻子段化+窗口门控 WaitCondition+插入块重结算——安柏双箭场景箭1开窗→连携附着冰→箭2 融化）；软窗/硬停分型判据改为「是否需玩家输入才能继续」，稻妻.md 规则本体无「演算照走」字样无冲突。②已实装（编译 0 错，未提交待复测）：ProjectileResolver **两遍法**——同片全部投射物先求交收集 (hitT,施法者 unitId,产出序) 排序后逐发编译，双单位多箭按到达顺序消耗（安柏近+甘雨远=A1→G1→A2→G2，距离差>1.2 格时自然退化为 A1→A2→G1→G2——按真实 hitT 自适应）；求交彼此独立，先求交不改单发命中结果只改编译序。③**剩余两个已知非到达序角落（勿当 bug 修，R-1 子段化收口）**：LineBurst（箭雨/霜袭即时整线）仍先于一切投射物编译；攻速不同=不同片按攻速整片序（既有拍板=攻速优先度，片头错峰演出一致，设计如此）。④双蒸发修复的编译视图机制保留=子段内同刻并发的基础设施，零返工。⑤R-1 已并入「子段拆分+窗口门控」（B7 前定稿落地，active/31 §6），遗留拍板=窗口期在途投射物表现 A/B+稻妻触发条件表缺位。
- [2026-10-02 00:01:18] 【吸血表现链三连修+决策二十二已落地（2026-10-01，承接 WYSIWYG 拍板链同日；22:41 终拍勘正；**用户实战验证通过，随批入库**）】起因=用户问「吸血是造成伤害后立刻加血吗」审出表现层瑕疵，实战复测连环报障后三轮拍板（首拍「B」逐笔各弹→二拍「不要错峰，立即响应」→**终拍「修改吸血，同源同刻合并」**）。三连修+终版：①**§107 主修**：DamageEffect 增 HitSeconds 通道（投射物=接触 hitT/tick=LaunchMs 兜底），吸血 HealEffect 继承母伤害命中时刻——修「+N 片头瞬弹早于箭落地」；②**§107 追记**：凯亚霜袭=LineBurst 非投射物（launchMs=0 直击按命令 stagger 槽弹、Host 无从携带该时刻）→ 客户端段内绑定表（`_segmentLastDirectDamageDelay`+`_segmentDirectDamageBeats` 节拍配对队列，段首清空），自疗（actor==target）绑定直击节拍同帧弹——修「伤害 40 先出 +30 慢一拍」；③**决策二十二终版**：~~逐笔各弹不合并~~ **同源同刻合并**（起因=双环 +20 合并案例复盘：芭芭拉闪耀奇迹送环→送出环的 source=芭芭拉→环 tick 治疗同源同刻撞合并键并 +20，用户确认合并才是正确口径并推及吸血——IsLifesteal 豁免分支删除，吸血与普通治疗同走 (来源,目标,毫秒) 键：霜袭双敌=两 40+单「吸血 +60」随首击同帧；异源恒不并；合并副本保留 IsLifesteal=前缀标记）+**立即响应**（无错峰维持）+名前缀「吸血 +N」（Heal 命令 metadata=HealKindLifesteal，色仍治疗绿）；本地化键 **Battle_LifeSteal=UIText 12036**（战斗域现最大，后续续编从 12037 起）五语言=吸血/吸血/Lifesteal/吸血/Вампиризм。全案=docs/14 §107（含追记+两族分工判别法「Host 显式毫秒 vs 客户端命令序节拍」）+docs/18 决策二十二（三轮拍板史+终版）。**闪耀奇迹送环=设计行为非 bug**（condition 分叉：尸体→复苏/活体→送环，docs/18 B-3②）。复测清单全过（霜袭双敌/单敌同帧带前缀/双环合并+20/普通治疗裸 +N/总回血量不变）。

- [2026-10-02 00:01:26] 【决策二十三统一+决策二十四逐拍交错+决策二十五反应零残留（2026-10-01 四连拍板「全部统一」+「理智+1每层」+「回合末同时进行」+「1层冰和1层火反应后应当什么都不剩」；**用户实战验证通过，随批入库**）】**决策二十三（元素伤害全链统一）**：CompileElementalDamage 统一出口（反应预判〔读片内编译视图〕→DamagePipeline 全乘区→反应事实+冻结+1:1 消耗）——四消费方=正常命中/环 tick/冰棱 tick/燃烧 DoT；燃烧 0层终点废除（docs/06 §6.6）；理智逐层（命令按目标合并总值）；tick 吃吸血；GetCompileDye 活态兜底。**决策二十四（回合末=同时进行，终版=逐拍交错）**：buff OnTurnEnd 只声明命中（新效应类 **PendingAuraHit**：拍时刻/施法者/目标/元素/Request〔null=纯附着〕），ResolveAuraHits 按 (拍时刻,施法者 unitId,目标 unitId) 排序过共享编译视图逐个结算——同拍并列枚举序、每层命中=一次完整元素应用、注册序失去先手权；~~首算 3+3 层双环=每回合末 5 次融化~~ **决策二十五后=3 次**。**决策二十五（反应零残留——对齐 docs/06 §6.3，终止「附着覆盖近似消耗」）**：①CompileElementalDamage 返回是否反应——CompileOnHit 以 reactedHits 表对已反应目标跳过 AttachElement 原子、交错管道反应层跳过层附着；**无反应命中才附着（覆盖）**；②ApplyEffects 新增 ReactionEffect 分支→BattleSimState.ClearDye（真实状态显式清——不再寄生在附着覆盖上）；③纯附着事件也过反应预判（异元素同样互耗零残留）；④Reaction 命令带命中时刻 launchMs（ReactionEffect 增 HitSeconds 通道），客户端到点清附着图标（与伤害数字同时刻）；⑤场景定案：3+3 层火冰双环=**每回合末 3 次融化**（先拍环每拍附着、后拍环每拍融化消耗零残留）、终态=先拍环元素常驻；安柏双箭打一层水=箭1「蒸发」（零残留）+箭2 平直并附着火（火来自第二发）；冻结=水冰互耗零残留仅控制生效。**教训：BattleEffect.cs 插入新类锚点必须含完整类头尾（本会话事故吃掉 ProjectileEffect 类体，git checkout 94f9a92 恢复重插）**。复测清单全过（反应图标到点消失/双环交错每拍至多一枚融化/双箭终态火来自第二发/回归项全绿）。
- [2026-10-02 00:34:41] 【执行阶段代码复审已收官+复用收口批次已落地（2026-10-02，随用户"按推荐做"授权，编译 0 错 0 警、未提交）】全链 22 文件复审=零 Critical；🟡 八项中 #1~#8 已落地 7 项（#3 段结算骨架收口挂 R-1 子段化同期勿单独做）。**已收口原语勿再报"重复"、新消费方勿再手抄**：碰撞判定链=MovementResolver.PassesVolumeLimit/PassesBlockingRules（移动进入/部署落点同源，体积上限=BattleMetrics.MaxTileVolume=3）；光环/DoT 施加者回落=BaseBuff.AttackerOf/AttackerIdOf；逐层错峰拍=BattleMetrics.LayerBeatSeconds(layer)；切比雪夫=BattleCell.ChebyshevTo；效应命中时刻注入=TurnResolver.ApplyHitMs（5 分支）；客户端到点换算=BattlePlayer.LaunchDelayOf；吸血节拍配对=BattlePlayer.DirectDamageBeats 单表（§107 游标耗尽回落末拍）；BuffFactory 已单一 Create（参数型缺 turns=Warn+null 防御）；SegmentAckTimeoutSeconds 已入 BattleMetrics。审查纪律沿用：方法级复杂度（ApplyEffects/EmitSliceCommands/PlaySegmentCoroutine 三大分发方法）勿再提议拆分（docs/23 D13 拍板维持）；审查报告全文在 2026-10-02 会话。改动面 12 文件 +190/−155（BattleMetrics/BattleCell/MovementResolver/ActionExecutors/BaseBuff/BurnBuff/SongOfLifeBuff/IcicleBuff/BattleSimState/BattleHeuristics/TurnResolver/BattlePlayer）。
- [2026-10-02 02:19:06] 【装备组件骨架+力系统保留+修改器暂不统一（2026-10-02 用户四条拍板，编译 0 错、Unit.prefab 引用存活、未提交待验收）】①UnitInventory 空壳退役→**UnitEquipment 武器与配件装备组件骨架**（文件重命名+meta guid 保留=Unit.prefab〔Resources/Prefabs/Units/〕组件引用存活实核；槽位=武器恒1+配件 max(1,星级-1) 按 docs/05 §5.2 表；装备/卸下/查询状态 API 全备、**行动链零接线**随装备批——挂账 docs/11 战场与经济节：EquipItem 执行器+词条 schema（ItemConfig 扩，加属性走 StatModifier/被动走 B-3/资源走池）+装备可视化三件待接）。②**力系统保留拍板**：ForceData/UnitMoveable._activeForces 受力表/MoveableModifier=**未落地的预留非死遗留**——规则真源 docs/05 §5.3「移动是推力驱动、每次移动=施加 ForceData」+docs/08 §8.3 挪德月矩力同走；现役普通移动=ActionData 平铺（direction/moveMagnitude=ForceData 字段展开、docs/active/22 §3.1 漂移已勘正）；强制位移族（击退/牵引/跳跃/瞬移）规则齐备（建筑免疫/牵引无视阻挡受体积上限）、代码仅 MovementResolver Pull 豁免一处预埋——**勿再提删、勿再当死代码报**。③**单位修改器统一=暂不统一**（五套异构：StatModifier 活〔StatBuff/命座消费〕/IdentityModifier 7型只活 Team/MoveableModifier 预留/Status+ElementModifier 批3⑤已删先例；不统一理由=语义异构叠算vs覆盖+Stats 活链 31 处回归面+无需求不造轮子；**触发条件=首个「按源批量撤销」需求**时 StatModifier 加 Source 字段最小改造，挂 docs/11 技能与判定节）。④组件必要性复审结论存档：Unit 组件化架构必要（87 处 GetUnitComponent 消费）；**UnitIdentity 修改器六死分支+配套属性（换武器/部署降价/改势力）/UnitElement 两 setter（SetSelfElement/SetDyedElement）/UnitGridPosition BoardType 棋盘切换（含 Tile 侧同款）=2026-10-02 用户拍板「属于预留，不删」——勿再提删、勿再当死代码重报**（与力系统同归预留档，但无文档引用=弱预留，将来对应玩法〔策反/打折/换武器类型/多棋盘〕立项时再裁）。
- [2026-10-02 19:17:41] 【2026-10-02 战斗表现两批：回血数字差异化+冻结霜化 shader（未提交待复测）】①回血数字（吸血/治疗）三差异化已落地：不爆裂（全程恒定停留尺寸）+上浮更高（回血上浮像素 100 vs 伤害 60）+更小（回血尺寸倍率 0.75 乘尺寸映射）——BattleDamageNumbers.Spawn 增 isHeal 参数（元能数字未动仍伤害曲线）；用户目检前两项通过，尺寸 0.75 未单独复测。②冻结表现终版=shader 霜化（2026-10-02 用户拍板原话「原神应该是用了特别的技术，可能是shader」——**首版 AI 冰壳贴图罩立牌被否决已全拆勿回退**）：FrozenFrost.cginc 数学单源（霜色重映射保明度结构：暗部→深冰蓝/亮部→霜白 + 边缘霜光 2D 菲涅尔（边缘实中间透 alpha=max(alpha, rim*0.9)）+ 晶体闪烁（hash 晶胞锐脉冲）+ 冻结遮罩世界系脚→头噪声扰动蔓延 0.45s/解冻 0.3s）+FrozenSprite.shader（sprite 路径=共享材质+MPB，Shader.Find"GIC/Battle/FrozenSprite"）+ChromaKeyVideo.shader 扩展 _FrozenAmount（视频路径 per-unit 材质直写）；UnitView.SetFrozenVisual→FrostRoutine 双路径驱动、冻结 tint 置白（配色归 shader）、shader 缺失回落纯冰色 tint；2D 显式舍弃折射与顶点膨胀；网检一手信源=CSDN《Unity Shader 冰冻效果实现：原理、代码与优化》。**未提交待用户目检**。③GraphicsSettings 登记新坑（团结引擎）：Always Included Shaders 追加条目必须同步把 m_LengthOfAlwaysIncludedShadersInInspector 计数 +1（漏改=清单截断）；shader .meta 的 guid 行是密文，GraphicsSettings.asset 须写明文 guid——明文用 AssetDatabase.AssetPathToGUID 经 exec_editor_script 取。
- [2026-10-02 19:32:14] 【2026-10-02 冻结霜化返修链+FrozenTest 验证场（承接同日 shader 方案条目；仍未提交待用户最终目检）】①**FrozenTest.unity=冻结验证场**（Assets/Scenes/，凯亚立牌=真实战斗接线 kaeya_stand×2/敌方主色盘/55° 战斗同构相机；霜化材质资产 Materials/FrozenSpriteTest.mat 静态序列化 amount=1——**编辑模式打开场景即见结冰态无需 Play**，_FrozenAmount 可 Inspector 拖半冻中间态；Play=FrozenTestPanel（DevTest/）F 键冻结↔解冻重播蔓延）。UnitView.ApplyFrost 补静态霜化起步守卫（材质已是霜化 shader 不误当原材质，amount=0 直通=与默认材质视觉恒等）。②**底座盘不变色拍板**（用户原话「底座盘不要变」）：SetFrozenVisual 的盘色切换已删、盘恒队伍色（勿"修"回）。③晶点返修链（用户两轮）：矩形亮块（floor 整格判定）→格内随机圆心径向衰减圆点→「加大3倍」密度 56→18（晶胞 3.1 倍=点同步 3 倍、数量 1/3）。④冰体观感返修（用户报障「延伸出的部分应半透明、当前全不透明；冰颜色不合适」）：冰体内部 alpha 收向 _FrostAlpha 0.78（透出底面=通透感，首版反而 +0.08 加实是根因）+轮廓外延半透冰缘 ~4 texel（四邻域形状 alpha，_FrostFringeAlpha 0.38，视频路径形状=四邻域各自抠色非 tex alpha）+冰色留 20% 原色（整替换 0.92→0.8；全替换+高饱和深端=信源「蓝色塑料袋」坑）+深冰蓝提亮去饱和 (0.42,0.63,0.85)。⑤**shader 迭代坑**：改 .cginc 后 unity_shader.compile 报 SourceAssetDB modification time 告警=增量库时间戳未刷新——refresh 后重 compile 即净（两轮实证）；shader Properties 改默认值后**已存材质资产的序列化值仍覆盖新默认**（FrozenSpriteTest.mat 深冰色须同步 SetColor，新属性 float 在 shader 未导入前 SetFloat 静默失败无日志——先 refresh 再写材质）。
- [2026-10-02 20:35:41] 【冻结霜化二版「原神奶白霜」已落地并已入库（2026-10-02，承接同日 19:32 返修链条目；AI 截图自检四轮 v2→v4 定稿；**已提交 fe38095 并推送**——含同日 19:17/19:32 两批全链：FrozenFrost.cginc/FrozenSprite/ChromaKeyVideo/FrozenTest 场景/FrozenTestPanel/FrozenSpriteTest.mat/GraphicsSettings/回血数字三差异化；编译 0 错 0 警，尺寸 ×0.75 与冰雾动态目检仍待用户实战复测）】参考=用户提供原神实机冻结截图（.codely/clipboard/clipboard-1790941782435.png，遗迹守卫）；像素实测定方向：原神冻结=强去饱和(饱和度中位0.20)+整体提亮(明度中位0.76)+灰蓝白霜化(机体均值RGB≈(145,164,175))+底部冰雾(淡蓝(180,211,242) 色相210°)。FrozenFrost.cginc 重构为五层：①重映射=原色强去饱和0.8+提亮0.3 再与色板五五混合+**明度 gamma 上抬 pow(saturate(lum*1.5),0.65)**（深色衣物也结白霜——线性映射下凯亚深蓝衣仍读深灰蓝的根因修复）；①b白霜斑0.55（中频噪声雾凇）；②边缘霜光加强1.6+半透冰体不变；②c冰雾新增（脚部淡蓝雾、高度二次衰减、噪声漂移、**alpha 溢出轮廓=缭绕感**、雾带下锚外放1/4雾高）；③晶点不变。暗部霜色改 (0.58,0.68,0.76)（旧深冰蓝(0.42,0.63,0.85)饱和度0.51太高是「蓝冰非白霜」根因）。新参数全部 Inspector 可调。落档=docs/18 决策二十六+docs/14 §108/§109+docs/17 表现层行+FrozenTest 场景行+gic-battle-hud skill（勿当 bug 修冻结四类+回血差异化+盘色勘正三处）。
- [2026-10-02 21:09:08] 【幸运暴击+战斗种子已落地并入库（2026-10-02，docs/18 决策二十七；**用户实战验证通过**〔安柏临时幸运 50 实测暴击率/!后缀/×1.2 尺寸目检全过，测试值已恢复 auto〕）】①规则=幸运=暴击率（20点=20%、负恒不暴、默认0=无暴击）、理智=暴击效果独立第四乘区 ×(1+理智/100)（默认理智50=×1.5 天然 GI 同款，UnitConfig.GetEffectiveSanity 回落50；UnitConfig.asset 全 35 单位 auto=-64 与 docs/units「理智: auto」一致已核验）、所有伤害可暴（判定收在 CompileElementalDamage 唯一编译出口=命中/tick/DoT 全覆盖零旁路，治疗不暴）。②种子基础设施=BattleLaunchConfig.Seed（BuildSinglePlayer 随机生成/B7 广播双端同种）→BattleSimState(map,seed) System.Random+RollChance(percent) 单出口（≤0恒否/≥100恒是；**模拟核心禁直调 UnityEngine.Random**——联机漂移/回放破坏纪律；同 seed+同命令序列=同战局）；客户端零 roll、表现层随机不受约束维持现状。③命令链=DamageEffect.IsCrit（MergeDamageEffects 同键合并口径=任一段暴即标暴）→BattleCommand.crit（对旧回放 JSON 向后兼容缺省0）→PlayDamageCoroutine→BattleDamageNumbers 暴击尺寸倍率（SerializeField 默认 1.2〔同日拍板 1.4→1.2〕、色不变 GI 式更大同色、**尾缀「!」**〔同日拍板——反应名前缀共存如"蒸发 40!"，治疗不暴恒无后缀〕）。④AI 评分/执行预览暂不感知暴击（非暴击期望值——对称随机增益，精确期望接入随后续 AI 批）。⑤未来概率事件（核心宝箱随机刷 docs/03 等）一律经 RollChance 勿散播第二随机源。文档=docs/05 §5.6 公式+暴击乘区节、docs/17 Flow 行、gic-battle-hud skill 勿当bug修清单（暴击条）。
- [2026-10-03 03:11:40] 【AI 强化批：CR-Move 皇室战争式伙伴移动+挂机三连修+暴击期望已落地（2026-10-02，docs/18 决策二十八+决策二十七④+docs/14 §110，编译 0 CS 错、未提交待复测→随收尾提交）】①**CR-Move 三件套**：驻位基准=攻击射程单源（新原语 BattleHeuristics.AttackRangeOf——与 PreviewLineTargets 射程两分支完全同口径，clip.maxRange **缺省 24**：安柏箭矢/箭雨=全图狙击、凯亚霜袭 2、芭芭拉水球 5；偏好交战距离退役覆写位 0=自动，**全 35 单位资产迁移清 0**）；射程内无线=**轴对齐直飞主档**（TryOfferAxisAlignStep：沿横/纵轴朝目标直飞、步数=轴差钳移速+直线地形永不越行/列、对满轴=下回合开火；两轴择优序=对满+线>对满>线>进展、轴差小者优先、同级横轴先）；1 格探针降边缘兜底（已对轴线被尸体/虚空截断的侧移换线）→失败换下一锚→**真无解兜底走满**（全锚耗尽朝首锚 BFS 走满直线≤移速）。②**挂机三连修**（报障三轮收敛，全案 docs/14 §110）：驻位早退绑定 per-unit 候选存在性（ScoreAttackCandidates→bool，**勿用 tracker.Best 判定——配额脑共享 tracker**）+对齐失败换锚+真无解兜底；眷属脑无此坑。③**暴击期望接入估值**：EstimatePerTargetDamage ×(10000+max(0,幸运)×理智)/10000——确定性期望非 roll、全员幸运 0 恒等；斩杀判定转期望口径；执行预览仍非暴击值（玩家向口径待拍板）。**挂机误报主源=体力经济**：魔神操作 10/回合挤占伙伴池（5/个）——「伙伴轮休」先查左上角体力非 bug（实测池 15 每回合仅 1 伙伴能动）。复测清单已交付（轴对齐一回合直飞/凯亚 2 格外/驻位连射/轮休）。


- [2026-10-02 21:30:55] 【水之浅唱命中绑定拍板 A（2026-10-02）】治疗维持 OnHit 语义——水球必须实际接触命中敌方立牌（含尸体=完全算判定）才触发治疗，**脱靶=零治疗**；对照原神芭芭拉 E「施放即治疗」的 OnCast 化提案（B）已被用户否决**勿再提**。全技能四原子（伤害 10% maxHp/水附着/治疗 15% maxHp 半径 1/+10 理智）均 OnHit 同口径。**推论：AI 治疗估值挂敌人线预判（无敌人线不施放）=与该设计自洽的预期行为非缺陷**——此前 AI 强化清单「支援型无敌人线纯治疗」项就此销案。落档=docs/units/蒙德/芭芭拉.md 水之浅唱治疗行拍板注记。
- [2026-10-03 12:45:41] 【冻结霜化三返修已落地（2026-10-03，docs/14 §111/§112，编译 0 错、已随批入库）】①视频路径绿幕被冻成整幅霜块=FrostApply 的 rim/冰雾对零 alpha 背景整幅 max 抬升——**形状域门** shapePresent=step(0.003, baseAlpha) 修复（形状外仅 fringe ±4 texel 轮廓外冰缘；sprite 路径 Tight mesh 裁掉透明区从未暴露=跨路径共享 shader 数学的测试盲区）；②**蔓延方向写反**（原式 g 随高度递增+g 大先冻=恒头先冻，与「脚往头」设计相反；用户报「有时反向疑似和朝向有关」——朝向=纯 X 镜像对世界 Y 梯度零影响、属误报）——FrostMask g 倒序从脚往头，**解冻随之=自上而下退冰（头先化）=预期**；③蔓延时长 0.45→0.9s（UnitView 代码默认值+FrozenTest 序列化值+docs 17/18 三处同步，解冻 0.3s 不变）。④教训：**定格态/静态截图验证不了时序方向类语义**（四轮截图自检全是 amount=1 定格全漏过）——含蔓延/生长语义的效果验收清单必须含动态观察项；注释与实现冲突=bug 信号。skill 已同步（gic-battle-hud 冻结条目⑤⑥⑦）。复测清单已交：绿幕全透明/脚先冻/解冻头先化/方向与朝向无关。
- [2026-10-03 17:11:53] [2026-10-03] 【丘丘人正式落地（2026-10-03，编译 0 CS 错，未提交随收尾批次）】Hilichurl=10002 坎瑞亚 1★眷属正式化：单手剑（原误配弓已勘正）/移速 20/元能 50/物理/坎瑞亚（原本已是）；tags 勘正=DifficultyBeginner+StableDamage+FrontlinePush（转正遗留清单「唯一标签疑似误配」项清账）；技能=[Common_Walk, 挥棒(Hilichurl_Swing=10002001，战技 100%攻物伤前方1格+命中获能10/costs体力层级0), 全力挥棒(Hilichurl_PowerSwing=10002002，爆发 150%攻物伤+消耗50元能满槽放)]——**借用凯亚霜袭的引用已移除**（SkillHitResolver「丘丘人借霜袭=冰箭」注释已勘正为历史例证）；时轮=LineBurst maxRange=1 ×2；图标=用户指定（2026-10-03）：战技 hilichurl_swing←MonsterSkill_S_Hili_01、爆发 hilichurl_power_swing←Skill_S_Freminet_02（角色技能库借图，语义选用已拍板口径）；**256 归一终版**（同日用户先指示「升 512」落地后自纠「是我记错了规范，恢复256」——512 为误记非新规范，skill/docs/20 的 256 规范维持不动；恢复=库内 256 源图直接内容替换，无二次 AI 重绘）；guid/meta 不变内容替换+ForceUpdate 重导入+PackAtlases 全链绿。**同批 128 漏网补漏（2026-10-03 审计发现 5 张技能图标从未超分=128 原图：barbara_water_serenade/jean_dandelion_breeze/jean_follow_the_wind/jean_gale_blade/lisa_violet_arc——Upscayl digital-art-4x 128→512 降采样 256 批量补齐〔skill §3b 设计路径〕；终态=Skills 目录技能图标 41 张全 256 归一，仅剩 bottom 926/circle 1024/select 512 三张 UI 辅助元素不适用图标规范；5 张 AI 超分图标观感待用户目检）**；本地化五语言+码位核验+CSV 重导出全过；角色文档=docs/units/坎瑞亚/丘丘人.md（坎瑞亚目录首个）。**遗留待拍板**：①爆发已接眷属脑（2026-10-03 用户拍板「扩眷属脑会放爆发」已落地=docs/18 决策二十九：FamiliarBrain v5 爆发档**满槽即放优先于战技**，TryCastAttack 战技/爆发共用抽提〔FindSkillIndex+CanCast+ResourceGate 元能照查+FindAttackDirection 十字预判〕；方向攻击型可消费、单位指向/自施放型无方向域不消费——眷属现无此类）；②技能名与爆发消耗 50 满槽口径待确认；③纸片人立牌素材未做（avatar 回落）。**同日眷属移动拍板（2026-10-03 用户报障「还是一次只走1格，移速20根本没发挥出来」=docs/18 决策三十）**：FamiliarBrain v6 退役「蠕动 1 步」硬编码，moveMagnitude=MoveExecutor.MaxMoveDistance（10%×移速含 Buff，与玩家移动/HUD 同口径）——丘丘人 20→**2 格/回合**、火斧暴徒（无 Move 技能回落 3）→3 格；减速 Buff 从此对眷属移动也生效；移速被压到 0=缺席。**同日二轮返修 v6.1（报障「来回左右移动持续多回合」=docs/14 §114）**：v6 首版「首步方向×N 直线飞」越过 BFS 路径拐点**与攻击位本身**（goal=敌邻格即攻击位）→对角目标左右乒乓、1 格直线射程打不中故永不收敛；修法=新原语 **BattleHeuristics.FindApproachStraightSteps**（多源 BFS 自攻击位集求 dist 场+沿首向只踏 dist 严格递减格+踏上攻击位即停=直线前缀，每回合距离单调递减数学上不可能振荡）+BuildApproachField 抽提两原语共用口径；教训=多格移动不得拿「首步方向」近似「沿路径走」，验收必含对角目标场景。**症状归因**：测试军丘丘人不再只蠕动、会主动挥棒=本批预期；其伤害数字物理灰白非冰蓝=预期。





- [2026-10-03 13:06:00] [2026-10-03] 【UnitConfig.asset 枚举数组=十六进制 blob（小端 int 连排）】Unity YAML 对枚举/基元数组字段序列化为内联 hex blob 而非块列表：`factions: 11270000`=单元素 [0x00002711=10001=Khaenriah]、`tags: 71170000`=[0x1771=6001=DifficultyBeginner]；多元素=8 hex 字符/元素连排（如 [6001,3003,3006]=71170000BB0B0000BE0B0000）；空数组=字段名后空值（`factions:`）。**Why:** 文本审计 UnitConfig 时按十进制直读必错位。**How to apply:** shell 层 rg/Select-String 审计单位势力/标签时按小端 hex 解码；改值走编辑器脚本（AssetDatabase）勿手编 YAML。2026-10-03 丘丘人批用它判读 factions=坎瑞亚、tags=入门——与编辑器回读互证。
- [2026-10-03 18:36:26] [2026-10-03] 【视野字段拆分+眷属目标序核心锚定（2026-10-03 拍板=docs/18 决策三十一，编译 0 错 0 警，未提交）】①UnitData.visionRange（旧单字段默认 1 无消费方）拆为 **攻击视野**（Unspecified 回落 5=眷属 AI 追击感知半径；安柏=24 已写资产；**不入 Stats**——眷属直读 config，未来 Buff 需求出现再入 StatType 预留）+ **迷雾视野**（Unspecified 回落 2=战争迷雾破雾半径预留；**接 StatType.VisionRange 基值管线**——安柏 1命「视野提升」休眠数据位随管线落位=提升迷雾视野）；PetKnowledgeBaker 同步双字段。②FamiliarBrain 移动档目标序 v7=**核心锚定 CR 式**：敌方协议核心（IsBuilding）恒为目标无视野门槛；非核心敌须「攻击视野内（切比雪夫≤攻击视野）且**严格**近于核心」才追（同距核心优先）；核心已破=退化为视野内纯距离序；攻击档射程即感知边界不另设视野门。**Why:** 用户拍板原话「眷属应当总是向着协议核心进攻，除非攻击视野内有其它敌人，才会去追（需要比协议核心更近才行，同距离时，优先协议核心），不在攻击视野则不关心（就像皇室战争的单位一样）」。**How to apply:** 涉眷属追击行为/新单位视野配置/迷雾系统立项时按此口径；伙伴/魔神脑暂不消费攻击视野（CR-Move 驻位基准仍=AttackRangeOf 攻击射程单源）；观感确认点=无近敌直线攻城不绕路/视野外守军被无视/同距核心优先。
- [2026-10-03 20:28:40] 【三脑增强 E 批次全落地（2026-10-03 立项+三批完成；设计真源=docs/active/33，拍板=docs/18 决策三十二）】用户四条拍板：①意图预告不做→纳西妲（未来魔神）读心预留（可见敌方玩家鼠标位置+敌方 AI 单位意图——鼠标=B7 输入广播、AI 意图=意图广播新协议，纳西妲立项时查 active/33 §6）；②伙伴脑加强规划层+协调层；③配额脑对手建模；④眷属脑**维持 CR 式可预测=拍板特性**（勿再当差距项报）。方案全按推荐轻量形态（否决 GOAP/HTN 全套/前瞻模拟/真 PIMC/显式任务分配器）。**E-1 配额脑对手建模**：OpponentThreatModel.cs（「对手如我」先验：敌眷属=FamiliarBrain.DecideOne 复用〔public 化〕、敌伙伴/魔神=骨架复用→威胁图四表=承伤/威胁源/火线格/将死者）+骨架 threat 可选参数（反威胁分 cap 25/移动避险扣 12 钳 1/号令救命分 30）——信息边界红线=纯推导不读 _familiarUnitActions/_pendingActions、全链零 roll。**E-2 伙伴脑协调层**：TeamIntentBoard 决策序内意图板（与 committed 虚拟池同构）——伤害溢出去重（remaining=target.hp−前位声明）+治疗去重（EnumerateSkillHeals 单源）+集火跟随（档案「集火权重」新字段默认 0 全单位零迁移、每档+3 低于战技获能、首批安柏/凯亚=5 已写 UnitConfig.asset）。**E-3 伙伴脑规划层**：移动锚延续槽 _lastMoveAnchors（CR 式 target lock：锁定敌插队敌序首、只锁敌方锚、锚死/不可达自动解锁、DecideAll turn≤1 自清零接线——治多锚贪心抖动）+反应预期分（快照附着×SkillHitResolver.SkillElementOf 新公共单源〔从 ResolveProjectileElement 抽提〕经 ElementReactionResolver.Preview 预判，增伤反应加折减伤害/2、冻结不计——凯亚挂冰→安柏跟火自动连招）+防御折减 MitigatedDamage（DamagePipeline 易伤乘区轻量镜像，评分与声明共用——修对高防目标虚高盲区）。**E-4 收口**：active/33 §3/§4 落地细节已补全+决策三十二落档+docs/11 技能与判定区登记六项遗留（威胁图喂伙伴脑/集火全单位配置成本/预测失准二阶/ApplyBuff 号令救命增益/纳西妲协议/复测清单）+docs/17 三脑行指针。骨架级修正（折减/反应分）配额脑操魔神同享受益=预期改进非回归；眷属脑零影响。**全部未提交待用户复测**（复测清单=决策三十二观感确认点+docs/11 E 批次条⑥——复测通过前勿提交）。
- [2026-10-03 21:40:05] 【支援型 AI 评分架构重构 F 批次全落地（2026-10-03 立项+四批完成；设计真源=docs/active/34，拍板=docs/18 决策三十三，遗留=docs/11 F 批条）】起因链=用户问「芭芭拉会主动前往前线用光环奶队友吗」→核验出驻位错配（治疗半径 1 vs 驻位 5 格）→用户拍板重构→业界一手全文（Ellie TLOU/Gears Tactics〔WorldState=TeamIntentBoard 同构验证〕/Dual-Utility）→拍板四项按推荐。总判=**不换范式**：重构支援型评分组织方式，骨架/确定性/意图板/锚延续/威胁图全保留；红线=**输出型零回归**。**F-1**：SupportRadiusOf 新原语（技能集最大治疗半径，芭芭拉=1）伤员锚贴身分流+威胁图接支援型（在场才 Build 全队共享、输出型恒 null）+自保维度（SupportDangerRadius 2/扣 15×档案「自保权重」默认 0，芭芭拉=1）。**F-2**：TryOfferSupportPosition 站位评分器——有伤员**全接管移动档**（候选=当前格+十字射线直线可达格〔推力直线语义〕，四维=奶程 20/超出每格−6+开火线 8−火线 12−自保；最优=当前格→驻位内化；无伤员回退锚循环；F-1 伤员锚分流被结构性取代成防御路径勿删）。**F-3**：门槛扩 **Henka**（自主评估域拍板；Enso/Contract 仍玩家域）+**无伤害纯治疗技能直接 Offer 分支**（perTarget≤0&&healValue>0，方向 Up 占位——修十字预判对无伤害技能恒无线零候选缺陷，VitalityBurst AllAllies 治疗 8 接通）+**预治疗救命档**（EstimateSkillHealValue 逐受治者判 DoomedAllies +RescueScore=30 **上提共享单源**，配额脑 EnsoRescueScore 退役改引用）。**F-4 收口**：决策三十三+docs/11 F 批条（光环覆盖价值待观察/伤员动态切换震荡观察/E+F 合并复测）+docs/17 指针。**E+F 全部未提交待合并复测**（同文件 CompanionBrain 无归因拆分——清单=决策三十二/三十三条+active/34 §6；芭芭拉完整行为链=贴身 1 格站伤员旁安全奶位→站位后治疗估值激活→将死队友优先奶→攒满送光环/复苏优先，复测通过后一并收尾提交）。
- [2026-10-03 22:23:46] 【AnimeStudio #124 PR 时机成熟+草稿已备（2026-10-03，取代 09-26/10-01 两条「回帖/PR 待拍板」记载——追评实已于 09-23 以 xiaoding521234 发出、四点全发）】维护者 Escartem 2026-09-30 回复「feel free to open a pr and I'll merge after I checked everything」=明确邀请 PR；上游 master bug 原样在（ACL.cs L153 streamer=IntPtr.Zero，文件 sha f6a2cc12，2026-10-03 实核）；xiaoding521234 无 AnimeStudio fork（待建）。修法终版=harness Program.cs DbBulkOffset（bulkOffset=64+8×(nChunks0+nChunks1+numClips)，字段偏移 16/20/28；tag 0xAC11DB01@8 字节序待执行期 dump 实核）；原生语义已核 dllmain.cpp L123-127（streamer 参数=inline bulk 起始指针，medium 前 low 后对齐 4）；PR 补丁+标题+描述+发送流程草稿=webrefs/gi-animation-extraction/PR-DBACL-draft.md；发送方案=fork+Git Data API 免 clone（api.github.com 直连，token 走 CredMan P/Invoke）；ZZZ 分支不碰（未验证）；PR 文案刻意避开 fixes 关键字防 #124 被自动关闭（humanoid retarget 仍是未实现议题）。待用户拍板：是否开 PR、草稿过目方式。
- [2026-10-03 22:34:05] 【AnimeStudio PR 复检全绿+补丁 v2（2026-10-03 深夜，承接同日 PR 草稿条目——用户暂缓提交要求复检，复检完成）】三层复验全过：①源码级（上游 vendored acl 逐文件）：tag 枚举 compressed_database=0xac11db01（buffer_tag.h:53）；布局=[raw_buffer_header 8B{size,hash}][database_header 56B（tag@8 首字段）][chunk desc 8B×n][clip meta 8B×m]，字段偏移 numChunks0@16/[1]@20/numClips@28 与 misc_packed@14 bit0=is_inline 全部源码推导命中；is_initialized=(size==0||ptr!=nullptr)（debug_database_streamer.h:64）；initialize 四参版显式检查双 streamer 否则 return false（database.impl.h L222-228）→dllmain L146 门控。②实数据级（harness 重跑+六份历史 dump 对拍，工具=verify124/verify_all.js）：实测 tag/哨兵-1/尺寸 0+7106 全命中；公式 64+8×(n0+n1)+8×clips=88 与 raw_buffer_header.size@0=88 双路互证（clip_metadata_offset@36=64 亦自洽）；CrouchToStandby RootT.y=0.5710830092430115→0.9451086521148682==vad[8]==m_StartX/m_StopX 逐位一致；Girl_Standby 第二 clip 同口径过；09-19/09-23/10-03 三跑 db blob+解压值逐字节一致；22:35 修复前 dump 实证 tier-0 回落垃圾指纹（Ambor_Standby 槽 280-286=0/denormal/FLT_MAX/NaN，与 issue 追评表述吻合）。③补丁 v2 两处加固：is_inline=1 时不动（零回归）+ bulk 越界防护（bulkOffset+align4(medium)+low<=db.Length）+ long 算术防 uint 溢出；「falls back to tier-0」措辞校准为「contexts initialized without the database」。GI db blob 拼装链已核（AnimationClip.cs GIACLClip.Read：资产内结构段+resS 流 append+AlignStream，尾部 padding≤3B 即 7196-7194=2 实证）。PR 草稿已更新 v2（PR-DBACL-draft.md）；仍待拍板是否发。
- [2026-10-03 22:41:08] 【AnimeStudio PR #129 已发出（2026-10-03 深夜闭环，承接复检全绿条目——用户拍板「发」并要求注明 AI 完成）】https://github.com/Escartem/AnimeStudio/pull/129 「Pass the inline database bulk pointer to ACL.DecompressTracks instead of IntPtr.Zero」：state=open、单文件 AnimeStudio.Utility/ACL/ACL.cs +46/-2、mergeable=True（发送后实核）；正文含 GLM 5.3 (Zhipu AI) 完成说明（用户明确要求披露「使用的是GLM 5.3完成」，pr_body.md 落款节）；刻意不含 fixes 关键字（#124 不会被自动关闭）。发送方式=全程 GitHub API 免 clone（先例可复用）：token=CredMan P/Invoke 读 git:https://github.com（40 字符 gho_，进程内用勿打印）→ POST /forks（default_branch_only，fork=xiaoding521234/AnimeStudio 已建）→ 等分支可读 → Git Data API 链 blob→tree(base_tree=fork master tree)→commit(parents=fork master HEAD db860e1f)→ref refs/heads/fix/dbacl-database-streamer（commit d28ca57）→ POST /pulls（maintainer_can_modify=true）。工具链与产物存档=.codely-cli/tmp/verify124/（send_pr.ps1/pr_body.md/ACL_master.cs=已打补丁版/pr_created.json/pr_state.json/pr_files.json）；PR-DBACL-draft.md（webrefs/gi-animation-extraction/）为提案档案，正文终稿以 pr_body.md 为准。上游修复版 harness（AnimHarnessProj）与两块 GI blk 留盘可复跑。维护者已表态「I'll merge after I checked everything」——静候其 review，无需催。
- [2026-10-03 23:54:21] 【G 批光环位置价值+支援形态切换已收官入库（2026-10-03 深夜提交 9597557 已推送 f620e08..9597557；设计真源=docs/active/35，拍板=docs/18 决策三十四）】用户设计输入三条：①芭芭拉光环给周围敌挂水 0 体力适合顶前线配合凯亚冻结/安柏蒸发 ②「闪耀奇迹=发放非转移」勘正（结算侧本来干净；评分侧修「已持有排除」→「已满层排除 IsAtStackCap」）③大招复苏保险→「稳定复活=生存问题非站位问题」（复苏无限程）。落地：G-1 AuraRadiusOf（Buff is-pattern 白名单，SongOfLifeBuff.Radius 是 **static**、命座加成在 Buff 内部→基础值保守感知）+光环敌覆盖分（每敌+6×档案「光环贴敌权重」九字段）+G-2 SupportStanceOf 双门控形态因子（血线<60% 或预期承伤≥50%hp→保险：贴敌归零+自保×3+后撤梯度+2/格）+G-3 发放语义修正+G-5 三修终版：①**锚循环直线前缀化** ApproachStraightPrefix（§114 同族防歪——探针实证环湖地图 B 芭恰为最近敌+E-3 锁延续+绕湖南岸 BFS 首步 Left 被直线化执行成纯西行滑边）②前瞻公式勘误 (Base−dist)×PerStep（旧斜率式 5 格外恒 0）③**站位评分器三态接管**（伤员锚/光环贴敌〔无伤员+有光环：自体假锚+光环前瞻主导〕/保险后撤）+woundedIsSelf dist 恒 0（治疗半径随施法者走）。**终验局全绿**：28 回合 A 方首次获胜、A 芭行为弧线五阶段完整（驻奶→上前线→贴敌挂水距敌 8→1→破线后撤 1→9→自奶重返）。**验证方法论已沉淀 docs/14 §115 增补**：seed 对战局无效（幸运 0 时 roll 零消费——换 seed 战局逐位一致≠代码没生效）+反射探针法（行为归因标准工具）。**【交接已闭环】芭芭拉行为实机复测=docs/11 首条已销案（2026-10-03 深夜 AK 会话复测通过）**：28 回合 A 方胜与基线一致；弧线①驻奶②上前线③贴敌挂水实征、④「后撤」=B 前线全灭距离跳变非主动后撤（与基线同成因；贴敌归零门控生效）、⑤未达（血线未回 60% 保险锁死，T24 被 B Amber 全图狙击站桩奶死——评分制理性解〔移动避险无差分〕，嫌观感差调自保权重 0.3→0.6）；亮点=T17 满槽 ShiningMiracle 复苏 T9 阵亡凯亚；另观察 T15 决策 Move(Left1) 结算未动（单次待观察）。观察日志=.codely-cli/tmp/barbara-gwatch/gwatch-235227.log（长存）。调参旋钮=UnitConfig.asset 行为档案（光环贴敌权重 1/自保权重 0.3）+CompanionBrain 常量血线门 60。工作区干净，E/F/G 三批全部入库。
- [2026-10-04 12:16:57] 【H 批近战先锋伴随 H-1~H-4 四连拍板已全落地（2026-10-04 用户四条原话①「协助凯亚这样的近战单位，开局就进攻」②「先锋交战时优先上前贴脸发光环挂水、光环同时覆盖敌+先锋」③「主动跑去先锋攻击视野内的敌让光环覆盖尽量多的敌，覆盖先锋为其次，视野外敌不关心——攻击视野非迷雾视野」④「不在先锋 1 格内就立刻跟上——开局凯亚走了四格远才跟」；设计真源=docs/active/36，拍板=docs/18 决策三十五；编译 0 错，**未提交待用户实机复测**）】①四态接管=先锋伴随>光环贴敌>保险后撤（2026-10-04 同日追加拍板「伤员不用管了，只关心先锋即可」：随军支援〔先锋伴随权重>0〕伤员锚退役——FindMostWoundedAlly 仅未配先锋伴随的支援型调用+锚循环前置伤员锚跳过=纯敌锚，伤员锚 F 批语义保留给未配单位数据分流；技能域奶谁/救命档/增益发放/就位让位门控未动；落档 docs/active/36 §8+决策三十五补记）+武器身份先锋判定（FindMeleeVanguard——刻意不用 AttackRangeOf 动态过滤）；②绕行铁律=TryOfferSupportPosition 返回 bool+fallback=FindApproachStraightSteps（BFS dist 场）——ApproachStraightPrefix 切比判据对平行绕行段恒 0 前缀=废（§115 坑⑧）；③H-2 就位=ShouldTakeAuraPosition 单源（救命/满槽复苏门控让位）+罩敌格豁免严格大于+Offer 托底 95；④H-3 视野敌群=CollectVisionEnemies（GetEffectiveAttackVision 回落 5 眷属 v7 同口径）全链透传 visionEnemies（光环敌覆盖分只数视野内敌×2 倍率+前瞻只朝视野内敌+就位豁免触发域=视野集非空；伤员/光环贴敌/保险恒 null 零回归）——生效实证 probe-h3d T4 窗口决策=Move Left 1；⑤**H-4 追随外推**=`_vanguardTrack`（static+turn≤1 自清）按先锋上回合位移向量把追逐锚前移=当前位置+位移（迎头追逐治 WEGO 决策滞后一拍；先锋驻位位移 0=退化真实位；换先锋回落）+TryOfferSupportPosition 锚格参数化（伤员传真实位等价）——复测 h6：T2 距 1 预判启动+T5-T6 移速用满 3 格（芭移速 3>凯亚 2）+T7 贴上距 1 全程保持（T7-T21 恒距凯亚 1），「走了四格才跟」消除。**症状归因**：她站距敌 2-4 持续奶/水球不动=凯亚被集火常驻将死集→救命门控让位（两全位非 bug，active/36 §5 待拍板）；Move 决策后位置未变=落点被占执行层停格（T2/T19 老观察项待复现）。日志=gwatch-h2/h4/h5/h6+probe-002033/h3d。

- [2026-10-04 01:38:04] 【伙伴脑讨论 I 批次全落地+凯亚移速坑⑧返修（2026-10-04 用户拍板三句：全玩家行动入板/最优质量形态/凯亚报障；设计真源=docs/active/37，拍板=docs/18 决策三十六，编译 0 错、未提交待复测）】①现状勘正：伙伴脑阶段三本就感知玩家选择（playerActions 传入选号令跳过+体力预留）——用户例「体力20魔神耗10剩10」伙伴脑已知；缺的是价值分配与内容感知，本批补齐。②I-A 两遍法：DecideAll 重构=申报遍（全员裸分 board=null 满池，_bidPhase static 申报段标志抑制槽写/板声明）→装包遍（Team 枚举升序分组建 pool：cost=0 白给+cost>0 score/cost 交叉相乘降序 BidValueComparer+unitId 决胜；packed 预算承诺/committed 定稿累计双计数）→执行遍（获名额者按装包序带正式板重评分定稿——名额分配依据=裸分、执行依据=板后协调分分离，E-2 去重语义全保留）；CandidateTracker 加 BestScore。③I-B 玩家行动入板：Skill（魔神战技爆发+伙伴号令）经 DeclareIntent 同口径声明（魔神打残敌伙伴自动补刀/足杀目标转火/集火权重对魔神生效）+Move/DeployUnit 落点占位（"deploy:"+playerId 合成 key+HasTile 防御）——玩家选择已定稿=非隐私；三表按目标单位 id 键控、id 空间按队分割=跨队天然零命中无需分板。④I-C 跟随分配：DeclaredVanguardFollow+FindMeleeVanguard 比较键前置跟随者数升序（板 null=原语义零回归）；伴随选定先锋即声明。⑤I-D 移动意图占位：DeclaredMovement（乐观落点口径）→BuildApproachField 新 reservedCells 参数单点接入（全 BFS 消费方自动感知；BoardReservedCells 物化排除自身）。⑥**坑⑧返修=凯亚「开局只走1格移速30没发挥」根因**：伙伴锚循环直线前缀化用 ApproachStraightPrefix（切比判据）——集团拥挤 BFS 首步=侧向绕行、到锚切比不严格递减=前缀恒1格（§115 坑⑧ H 批只回填了支援伴随路径、输出型主路径漏修）；修=换 FindApproachStraightSteps（dist 场判据沿最短路实格推进）+direction 改用 dist 场首向；旧原语退役保留+标注勿新增消费方。⑦CompanionBrain.cs 加了 using System.Linq（头模板清理后缺，GroupBy 需要）。⑧H 批 gwatch 观察项「Move 决策后位置未变=执行层停格」与 I-D 声明乐观落点同族——I-D 让后位避让前位声明落点后应收敛。复测清单八项在决策三十六条目尾+docs/11 I 批条①；与 E/F/H 批未提交件同批协调提交。⑨**首轮实测返修（2026-10-04「凯亚第一回合走 3 格 ✓ 但芭芭拉没立刻跟上」=WEGO 快照滞后首拍窗口：T1 她决策时凯亚未结算仍贴伴=驻位无候选→申报 null→无名额→缺席；外推槽 T1 空救不了）——追随直连三修**：a)追逐锚直连声明落点（H-4 段 vanguardPos 优先=board.DeclaredMovement[先锋 id]，申报遍读申报落点/执行遍读正式声明，有声明跳过外推防双外推）；b)申报遍拆两轮（输出型先申报纯独立视角、支援型后申报消费「申报落点板」bidBoard——申报表=意图第一次广播；跟随者独立视角原则修正为「消费先锋申报」）；c)执行序支援型稳定殿后（grants 按 Team→isSupport 后置，稳定保持装包价值序——先锋先正式声明、跟随者后消费）。预期=T1 凯亚申报走 3 格→芭芭拉同回合追随 3 格跟上；残留观察=执行重评改先锋行动时名额浪费（下回合自愈）+H-3 视野敌集仍快照中心。全案=docs/active/37 §4b+docs/18 决策三十六返修段；编译 0 错，仍未提交待复测。
- [2026-10-04 13:08:26] 【H-3b 覆盖提升接管已落地（2026-10-04 现场报障返修，用户原话「芭芭拉的站位非常不好，当前它的位置，光环只能覆盖1个敌人，明明再往右走1格，就可以覆盖两个敌人+我方凯亚了」；docs/active/36 §9+决策三十五补记；编译 0 错 0 警，未提交待复测）】根因=就位判定二值口径（罩到 ≥1 视野敌即「已就位」）——H-3「尽可能覆盖多的敌」的 95 配额托底只在罩 0 敌档存在，罩 1 敌时移动分≈45 恒输技能分 50-90=站桩放技能不挪（暂停态探针实锤：(1,13) 罩 1 视野敌、邻格 (2,13) 罩 2 视野敌+先锋奶程内）。修法=TryOfferSupportPosition 射线扫描跟踪「视野敌覆盖数严格多于当前格」的覆盖最优格（覆盖数主导、并列取评分高者）——存在即无条件接管移动目标+Offer 托底 SupportAuraTakePositionScore=95（斩杀 +80×激进度恒赢）；驻位=无严格更优覆盖格（并列不动防震荡）。**让位口径变更：覆盖提升不再受将死集让位**（「伤员不用管了只关心先锋」拍板——挪进先锋治疗半径本身即先锋照顾，站桩处奶程外奶不到先锋=实证）；仅保留「尸体+满槽复苏」让位。仅随军语境（visionEnemies 非空）生效，伤员/光环/保险三态零回归；H-2 二值就位档在伴随语境被覆盖最优格自然细化。取证方法论：暂停态反射 CompanionBrain 私有原语（FindMeleeVanguard/CollectVisionEnemies/ShouldTakeAuraPosition/SupportStanceOf）+十字邻格覆盖数对比表一发定位——「该动没动」类归因标准姿势。**注意：本次 refresh 已退出用户暂停中的对局（turn 8 现场丢失）——涉局取证后改代码前应先告知用户 refresh 会丢对局。**
- [2026-10-04 13:23:07] 【J 批伙伴推核心与凯亚协同已落地（2026-10-04 用户拍板「伙伴应当去推核心，攻击视野内没有其它敌人时」+冻结协同分/爆发语境评分/贴脸驻位按推荐、存活门控不做；设计真源=docs/active/38，拍板=docs/18 决策三十七；编译 0 CS 错，未提交待复测）】凯亚四断点返修：①J-1 冻结协同分=反应预览补冻结分支（ReactionType==ReactionKindFreeze→+FreezeControlScore 25——旧口径控制反应恒 0 分，芭芭拉挂水后霜袭优先冻结；25<斩杀 80 不压斩杀）；②J-2 凛冽轮舞语境评分=ScoreSelfCastBurst 加 sim+新原语 BattleHeuristics.AuraRadiusOfBuffType（施放前 Buff 未上身，AuraRadiusOf 查不到半径）——光环半径内每存活敌 +SelfCastBurstPerEnemyScore 15（贴敌群 45+15N 先放爆发再攻击；**空场施放=预载非浪费**——寒冰之棱永久，赶路期照常放，此为对原提案的语义修正）；③J-3 爆发态贴脸=锚循环敌锚驻位收紧到 AuraRadiusOf（凯亚爆发后 2→1，冰棱半径 1）+收紧就位移动 Offer 托底 95 压攻击档（一次性就位成本、斩杀恒赢）——冰棱 tick 统一出口=贴脸后挂水敌每回合末冻结链（J-1+J-3=双芭凯联动闭环）；④J-4 伙伴推核心=自身攻击视野内无普通敌单位（IsBuilding 不算「其它敌人」）→ 敌协议核心插队锚序首直奔推进、视野内有敌=最近敌锚序零变化（与眷属 v7 核心永恒目标的刻意差异——伙伴=战术层 E-2/E-3 全保留）；核心插在 E-3 lock 之后=无视野敌时优先于上回合锁定。附带面：歌声之环持有者 fallback 路径同样收紧驻位=光环贴敌语义一致的预期泛化；配额脑/眷属脑零变化。前批提交=f3c8b86（伤员锚退役+H-3b）；H+I 批=另一会话先收 0e02c8d。
- [2026-10-04 14:15:46] 【J 批观察局四刀迭代已落地（2026-10-04 用户委托「自行开一局观察凯亚芭芭拉、自行迭代」；全案=docs/active/38 §8+决策三十七迭代段；编译 0 CS 错，与 J 批同未提交待复测）】三局净观察+T6/T15 反射探针归因，四刀返修（全 CompanionBrain）：①J-3 托底带限制——贴脸 95 只在锚距≤原攻击射程生效，旧版任意锚距托 95=凯亚弃贴脸敌群奔 9 格外锁定的 B 核（探针 attack52 vs move95 实锤）；②保险血线门补近敌判定（SupportStanceOf threat=null 路径）——hp<60% 且近敌≤InsuranceDangerRadius(5) 才保险，无近敌+低血=安全区随军（实证她在 7 格无敌区因 49% 血驻场 15 回合、凯亚前排单打至死）；③追赶托底 VanguardChaseFloorScore=60——评分器移动 Offer 与 BFS 兜底两路同享（旧评分器裸分 40 被尸体线自奶 42 两分压死，T15 探针实锤；60 压常规单体技能、低于群奶/救命 75-90 与就位 95）；④穿占虚 Offer 修复（G 批「占据格跳过续评」保守近似勘正）——评分器射线遇敌/尸体/未开互不阻挡友军=硬阻断 break，仅互不阻挡友军格可穿（实证被 U11 尸体挡 (3,13) 零位移白走多回合+元能+10）。终局全绿：A 方 36T 拆 B 核获胜、芭芭拉满血存活归队东线、T33 冻结链在东线复现（**死凯亚冰棱倒下仍生效+她环挂水**）、T21/29 两次满槽复苏安柏/凯亚。残留观察待拍板：先锋切换（凯亚死→游走丘丘人）锚翻转振荡 6 回合（追赶首步 hysteresis 候选）；U12 型 600 血远程 7 格狙杀不在保险近敌半径 5 内（深修需同伴流接 E-1 威胁图）。harness 三坑已录 docs/14 §115 坑⑩⑪：旁听 DecideAll 污染 B 侧决策（A 侧安全）；调试军 UnitID=U1~U12 不含角色名按 id 硬编码；脚本宿主 File.WriteAllText 裁剪用 StreamWriter。
- [2026-10-04 14:33:36] 【unit 文档三段式结构改版（2026-10-04 用户拍板「技能的开发版描述不需要写历史改进直接写现状；历史演进放文档最末尾；在此之前加 AI 行为节，AI 行为演进同样放最末尾；安柏/凯亚/芭芭拉/丘丘人按最新 unit 文档改进」）】四文档已全量重写落地（安柏/凯亚/芭芭拉/丘丘人）：技能/Buff 节=纯现状（历史改进/拍板过程/日期注记全剥离）→ `## AI 行为`（现状完整决策链：层级/脑/行为档案/移动评分要点/操控边界——芭芭拉含随军三态+H-3b+追赶托底+保险近敌门、凯亚含先锋身份+J1-J4、安柏含 CR-Move 全图狙击、丘丘人含核心锚定+移速单源+爆发档）→ `## 历史演进`（技能演进+AI 行为演进时间线收尾）。**玩家版描述模板行与参数表行经 HEAD 对比零漂移验证（四文件 drift=0）——本地化/参数同步源未受影响**。规范落三处：docs/units/_模板与字段说明.md（开发版书写规则+结构约定）、docs/20 §5（一行规则+指针）、gic-new-unit skill（三段式+同步义务：大改 AI 行为后必须同步 AI 行为节+历史演进追加条目）。安柏命座玩家版标签顺带从 (zh-Hans) 对齐模板口径 (zh-Hans, 命座)。
- [2026-10-04 18:09:47] 【决策三十八+三十九已落地：支援评分器平局改判深格+BFS 直线段择优+穿占虚 Offer 全收口（2026-10-04；docs/18 决策三十八/三十九+docs/14 §116；编译 0 CS 错、8 回合回归局逐位一致、未提交）】决策三十八：奶程梯度切比雪夫×十字移动几何错配（锚横向偏移≥2→平坦区同分→「严格大于」判给最短格=系统性少走 1 格，T3 探针实锤 Up run2/run3 同 26 分）——修①=TryOfferSupportPosition 同分且同向取 run 更深格；修②=FindApproachStraightSteps 四向各算严格递减直线段取最长（旧=十字序首个递减向，凯亚 T3 Right 1 撞水实证）。决策三十九（用户暂停态报障「安柏反复撞敌芭尸体」T36 取证）：**穿占虚 Offer 族**=地形-only run 计数循环 offer 落点含不可停格（TryOfferAxisAlignStep 轴对齐+真无解兜底漏网，J 批四刀④当时只修了支援射线）——修=两处 run 循环补 IsCellOccupiedForStep+CanPassThroughCell（敌/尸体/未开互不阻挡友军硬断、互不阻挡友军可穿不可停、offer=实格可停）；同型循环已全收口（FindApproachStraightSteps/TryOfferAlignmentStep 本就感知、ApproachStraightPrefix 退役豁免）。验证：T3 芭 2→3、距 4→3→1 三回合追上；凯亚 Right1→Up2；安柏撞尸场景修复后改道西进（飞行单位越水可达）。教训二犯：修检查缺陷类原语病灶须 rg 全部同型循环。**观察局 harness 纪律已记 docs/14 §115⑫：进 Selecting 立刻交 Pass+探针同步块零 await+DecideAll 放块尾；自动 Pass 本体正常（失焦=编辑器挂起倒计时冻结非 bug）；飞行单位地形图按 Walk 渲染失真，取证按移动者本人 forceType 判**。证据日志=.codely-cli/tmp/gwatch-bb-chase/ 五份。
- [2026-10-04 19:02:05] 【HUD 三微调=决策四十（2026-10-04 三句拍板：①「执行预览的位置需要下移，让第二行只与底部边界有一点间距即可。执行预览也需要支持自定义布局」②「把技能瞄准时的矩形盘，缩小30%」③「AI自主决定的技能，需要不透明度为50%」；编译 0 CS 错、未提交待目检）】①执行预览**烘焙进 BattleHud.prefab 新 Slots/preview 槽**（弃运行时 Instantiate；首子级全拉伸=布局契约、DragPlate/SelectFrame 自 confirm 克隆、槽锚 (0.5,0.0672)=第二行〔即将行〕底距画布底 24px〔几何=行间距88/行高72/缩放0.8→行底偏移72.8+24〕、sizeDelta 1600×160）；AllLayoutKeys 注册 preview（存量布局方案缺键=默认位）；**布局编辑模式占位行**=Show/HideLayoutPlaceholder（选择阶段无行不可见无从拖——两行纯行壳，真实行在场早退）；prefab 槽为末位子级=渲染于其余槽之上（旧 SetAsLastSibling 顶层语义）。②大盘半边 340→238（prefab 序列化值+代码默认双改；小盘 56/边距 16 不动）；**填充内缩改随半边等比比例 11/340**——两素材角弧差随盘径线性缩放（sprite 拉伸到盘尺寸），内缩常量不随盘径调会复现四角唇口/空洞（比例化后盘径再调零漂移）。③门控键 alpha 0.55→0.5（ApplyTierGateVisual 单点）。**坑**：改 HUD 槽结构用 PrefabUtility.LoadPrefabContents+InstantiatePrefab(asset, root.scene)（InstantiatePrefab 两参 Scene 版——单参会落到活动场景而非 contents 场景）；画布非 BattleHud.prefab 根（根 2 子级、Canvas 在子级）——寻址须 GetComponentInChildren 兜底；read back 断言须同款寻址。skill/docs17/18/active22/HANDOFF 已同步。【决策四十④手牌缩小 30%+⑤下沉热区暂停（2026-10-04 拍板「把手牌的大小，缩小30%」+追拍「再收：上探和侧探都改为30」+拍板「不需要自动降下了，暂时移除这个功能」；编译 0 CS 错、未提交）】**HandCards 壳烘焙 localScale (0.7,0.7,1)**（整壳等比：卡 160×240/间距/字号/点击区/滚动壳同比——**勿在代码改 160×240 常量**：Card.prefab 内部排版/字号不随 sizeDelta 缩会炸；pivot=底边中点=卡底线不动向上收缩）；UpdateHandHover 下沉换算补乘壳缩放 handScale（半卡画布量=120×槽缩放×壳缩放——「沉半张」比例逐位不变；热区世界角测量天然含壳缩放）。**热区余量终值=侧探 30/上探 30**（原 60/100；改热区=BattleHud Inspector 两字段）。**⑤下沉热区暂停=新开关「手牌自动下沉」[SerializeField] bool 默认 false+prefab 烘焙显式 false**——false=UpdateHandHover 门控直 return 手牌恒升起态（含归零残留偏移守卫）；true=恢复自动下沉/接近上移，链路与参数全保留——**恢复只勾开关勿重写链路；报「手牌不下沉了」=拍板暂停预期勿当 bug 修**。
- [2026-10-04 19:05:14] 【手机端双指缩放修复（2026-10-04 报障「无法在大地图，战斗地图上两指缩放」；全案=docs/14 §117+docs/24 §4.4 双指晋升规则；编译 0 错、离线合成断言 12 项全过，未提交待手机复测）】根因三处：①GestureHub 第二指 Began 在 CancelSinglePointerGestures 后仍全量投递——刚取消复位的 Immediate 拖拽把第二指当全新序列重新宣胜、挤掉等第二指的 Pinch（大地图捏合全灭根因；桌面单指针+P1 合成断言直喂识别器均测不到该 hub 调度路径）②OnRecognizerWon 单指宣胜 fail 全部识别器=捏合存亡隐性依赖识别器列表顺序 ③战场捏合从未接线（docs/24 §5 原表 Battle 只有 Drag，手机无滚轮=战场缩放整个不存在，缺件非回归）。修法=DeliverToMultiPointer（第二指 Began 只喂 MaxPointers≥2）+单指宣胜豁免多指+BattleCameraController 补 PinchRecognizer（中心缩放口径同滚轮 2026-09-12 拍板、直写距离同步目标不污染平滑链）。**验证方法论：hub 层也可离线合成断言（反射 Dispatch 注入事件流）——纯 C# 组件断言勿只直喂子识别器（跳过调度逻辑=盲区）**。Battle 捏合锚定口径=屏幕中心（大地图=锚定捏合中点）；若用户要战场也锚定中点=一行改动待拍板。
- [2026-10-04 19:21:23] 【游戏内派蒙手机端尺寸适配（2026-10-04 用户拍板「手机端派蒙初始大小需要调为1.5倍，且缩放下限需要更高，否则太小难以双指缩放」；同日上一批双指缩放 hub 修复已用户验证通过；编译 0 错、未提交）】PetInGameHostController 新增「手机端缩放适配」两字段：手机初始倍率=1.5（无存档时替代 initScaleFactor=0.7）+手机缩放下限=0.75（Awake 单点抬基类 scaleMin，一处覆盖 restorePrefs/滚轮/捏合/有效上限守卫全部 clamp 位）；Application.isMobilePlatform 门控，桌面/编辑器零变化；PaimonInGameRoot.prefab 序列化值不动（scaleMin=0.4/initScaleFactor=0.7）。**存档语义（2026-10-04 用户拍板「当前不是已经让存档不要迁移，直接重建吗？正在快速迭代开发期」=零迁移政策适用）**：手机端存档值低于新下限 0.75=旧语义产物，restorePrefs 直接当无档重建起步 1.5（不迁移、不钳着沿用——新语义下限起不可能再存出更低值=判据自洽）；≥0.75 的存档值=新语义期真实偏好照常沿用；桌面存档零影响。调参入口=PaimonInGameRoot prefab Inspector 手机端缩放适配段。落档 docs/19 §6.4 缩放行。
- [2026-10-04 19:30:55] 【快速迭代期存档=每次启动直接重建（2026-10-04 用户拍板「应当无论怎么样，每次启动游戏都直接重建，除非我要求快速迭代期结束，才改」——推翻「同版沿用/仅旧版重建」语义直至迭代期结束；编译 0 错 0 新警、未提交）】两开关：①主存档=SaveManager.快速迭代期每次启动重建（static readonly=true——LoadSaveData 开头早退直接 CreateNewSave，跳过三级读档链+版本门；旧档文件保留被首存覆盖，devTestItems 每次建档重发；**勿改回 const——const=true 把读档链判不可达报 CS0162**）②pet.json=PetPrefs.快速迭代期每次启动重建（const=true——ReadDiskSave 进程首读一次性 RebuildForIteration：易变状态缩放/位置清零回默认，**设置类字段照搬旧档保留**=chatCiphers/chatProvider/quickMessages/quickSeeded/voice* 全档，API key 密文/快捷消息/语音配置属用户环境配置非测试状态；有旧档立即固化落盘）。**迭代期结束=用户宣布后两开关改回 false**（版本门/读档链代码原样保留）。附带语义：游戏内派蒙「旧档 ingameScale<0.75 当无档」分支从此先被启动重建短路（不冲突保留）；桌面 pet.exe 启动同样触发重建（桌面缩放/位置回默认、key 保留）。落档=docs/20 §1.6+docs/17 存档行+gic-save-system skill 版本策略节。
- [2026-10-04 20:19:50] 【2026-10-04 三批落地未提交：存档初始值/轻提示顶边/HUD 置灰⑥⑦（编译 0 错）】①主存档初始值批：卡组1（deckId 0）改名「蒙德新手卡组」+构筑=安柏/凯亚/芭芭拉/体力/摩拉（摩拉 1001 加 decks[0]）+defaultDeck 1→0——InitialSaveConfig 新增 initialDeckNames（deckId+name 条目式）字段+SaveManager.ApplyInitialData 建档时写入（越界/空名跳过、超长截断同 SetDeckName 口径）+SaveSystemSetupTool 重建路径同步；**改初始卡组名/初始选中走该资产字段勿改代码**（快速迭代期每次启动重建=下次启动即生效）；gic-save-system skill「初始存货数据源」节已补 initialDeckNames 指引（2026-10-04 收尾）。②轻提示顶部越界已修：PopupManager.ComputeToastPosition 旧中心定位（y=H×0.5−ratio·H）在内容高>10% 屏高时顶半越界（暂停态实测 170.42/1375.98→越界 17.86px）——改顶边定位（顶边距屏顶=ratio·H），任意内容高恒不越界；修复后回读断言 topMargin=+68.96=5% 屏高 PASS。③战斗 HUD 置灰=docs/18 决策四十⑥⑦（同日拍板「AI自主的技能，不透明度为45」+「未满足使用条件的技能，整个变暗（不止是图标，包括底面，圆环）。两者可以同时叠加」）：ApplyTierGateVisual 0.45（迭代链 0.55→0.5→0.45）+SkillIconView.SetConditionDimmed 三图（图标/底板/圆环）基准色统一乘 BattleHud Inspector「资源不足变暗系数」默认 0.6（首版 0.45 用户目检「太暗」调亮；RGB 乘 alpha 不动=变暗非变透明）+资源门槛扩展到己方眷属/伙伴 AI 域键（不拦 interactable=信息层，与 45% 半透明同键叠加；敌方查看态不灰）+战斗四键 UGUI disabled tint 置恒等白（Build 分件，防 Icon 双压暗，hover/pressed 保留）——细节全案=docs/18 决策四十⑥⑦+gic-battle-hud skill 置灰行/勿当 bug 修清单（均已同步）。①②已断言验证，③待用户复测；三批+前批决策四十①~⑤同在工作区未提交，收尾提交时注意归属协调。
- [2026-10-05 20:40:23] 【gic-paperdoll skill 实体勘误（2026-10-04 当时不存在；**2026-10-05 勘正：现已实际存在，本条「勿再找 skill」作废**）】2026-10-04 全库实证时 SKILL.md 未落盘；2026-10-05 实证 .codely-cli/skills/gic-paperdoll/SKILL.md 已存在（后续会话补建，含 2026-10-05 移动片 per-skill 接线段同步）——**配方真源现=该 skill 本体**（生成工作流/接线步骤/素材清单/循环片裁剪配方全在内）；旧备用指针（skill 缺失期的替代路径，现已退居二线）：docs/18 决策记录 L266-270+docs/14 §1579+工具链 .codely-cli/tmp/amber_idle_anim/（make_greenscreen.py/extract_idle.py/wrap_scan_v3.py）与 paperdoll_seedance/extract_build.py。后续角色动画复刻直接读 gic-paperdoll skill。

- [2026-10-04 20:32:31] 【B-S4c 安柏战技/爆发动作轨进行中（2026-10-04；编译 0 错、素材待目检、未提交）】①**消费链已接线**：SkillConfig.SkillData 新增 VideoClip 动作视频 字段（技能资产持有，null=待机照播）+UnitView.PlayActionVideo（一次性动作片从头播、isLooping=false、loopPointReached 播完自动回待机循环+随机相位；同片每次施放重播不幂等；尺寸不符 RT 硬防跳过）+SetMoveAnimation 补 isLooping=true（防动作片 isLooping=false 残留致移动片播一遍即停）+BattlePlayer SkillCast 分支消费（fire-and-forget 同 Vfx 口径，playbackSpeed 传 _playbackSpeed、回 idle 归 1）。**设计要点：动作视频内不画箭矢（箭矢表现归投射物/箭雨系统，画了双叠）**。②素材两段已生成（H3-Max first_frame=idle_gs_frame_v6.png 768²/5s/各 375 积分，绿幕抠净 0.81-0.83 与 idle 同档）：工具链=.codely-cli/tmp/amber_action_anim/（extract_action.py 动作版验收/edge_sides.py 贴边侧取证/poll 脚本）。**H3-Max 一次性动作片节奏陷阱（与 idle 循环片不同族）**：无时间窗约束时模型把动作拖满 5s——战技释放动作在 f52-60/f79-83（≈2.2s/3.3s），时轮 0.95s 窗口装不下「拉弓+两次释放+收势」；爆发底部贴边 52 帧（f72-105 连续）+右缘 27 帧。**重 roll 方案（待拍板）=prompt 追加硬时间窗「整套动作在最初 1.5 秒内完成，之后保持静止悬停」**。时轮对齐目标：裁剪段发射时刻对齐 startTime 0.30s、段长≈totalTime（战技 0.95s/爆发 1.25s）；裁剪配方=-ss 输入端 seek+ -frames:v+ -an+bt709 双写+-crf 16。落地名=Assets/Art/PaperDoll/amber_double_shot.mp4+amber_arrow_rain.mp4+SkillConfig.asset 挂引用。
- [2026-10-04 20:58:10] 【B-S4c 迭代 R2~R4 教训+箭矢色板落地（2026-10-04 晚；承接同日 B-S4c 条目）】①**箭矢元素色板已落地**（编译 0 错）：BattlePalette 新增「箭矢元素色」九字段段（结构同伤害数字色板先例——主题色≠表现色三次实证：箭矢原染主题火红 #EF5350 偏粉不被读作红）+BattlePlayer.ResolveArrowTint 单源（命中箭 metadata/消散箭 reactionKind/箭雨箭三路同源）+火箭矢=#F23829 饱和正红 Inspector 可调，其余元素=主题色同值；数字色板 Header「箭矢用 ElementFactionConfig」表述已勘正。②**乘法染色黑像素陷阱（通用）**：SpriteRenderer.color 是乘法染色，黑像素 (0,0,0)×任何 tint=恒黑——ArrowBolt 素材黑描边占 29% 像素致「箭全身红」不可达；修=素材暗像素 (r,g,b<70) 提亮到中灰 96（染后=亮红杆+深红边全身同色系；所有 tint 染色素材的暗部必须用中灰勿用纯黑）。③**H3-Max 动作片 prompt 工程三坑（R1~R4 四 roll 实证）**：复原指令「1.5s 后保持这一悬停姿态」被模型读成「冻结当时持弓姿态」（R2 复原消失根因）——复原指令必须显式锚定首帧「回到与首帧一模一样的姿态」；「两次连射一气呵成」模型始终给不出 1 秒内第二次松弦（运动先验=射箭是庄重单发，四 roll 全部第二次隔 1~2s）；弓身份每 roll 概率漂移（R1 变/R3 没变/R4 变）——「悬挂弓→拉满」状态迁移=模型重绘弓的最大窗口。④**瞄准态 first_frame 方案进行中**：日辉单参考 amber_idle.png+is_segmentation 生成 amber_aim_pose.png（1024² 透明、留白 41/33/43/39、零绿色素，.codely-cli/tmp/amber_action_anim/）——动作片从瞄准态起手消除拿起迁移，只留末尾放下反向迁移（R3 实证该段弓不变形）；待用户目检弓身份后合成绿幕 roll R5。
- [2026-10-05 12:21:03] 【B-S4c 安柏战技动作片已完整落地（2026-10-05；编译 0 错 0 警；未提交随批；爆发待目检接入）】十 roll 定稿配方（后续角色动作片直接模板）：**H3-Max 768P+16:9 宽幅 1344×768（H3 基础版 2K 画风掉档被用户否决「改回用max」——画质>分辨率）+first_last_frame 双锚定待机图+弓外观全描述锚定+位置稳定与动作舒展分开表述+「两次松弦观众能明确数出」+特效封禁含「松弦瞬间无发光箭矢光点」+16:9 宽幅自由区解越界（R6 四向 83 帧贴边→R10 五帧）**。**落地三件套**：①素材=Assets/Art/PaperDoll/amber_double_shot.mp4（1344×768、**1.50s=R10 完整版 setpts=PTS/3.4 加速**——用户否决剪辑版〔「只射了一次没回到待机」=裁剪丢动作链〕拍板「完整版加速压 1.5 秒」：完整前摇+两次连射+后摇全保留且连射间隔自动收紧，第二箭松弦恰好对齐 0.30s 发射事件）；②**UnitView 双 RT 机制**（宽幅片原生尺寸独立 _actionRt 渲染=零裁剪零缩放，quad scale×缩放补偿=idle 主体高/动作片主体高〔安柏 1.29〕令视觉大小恒等；OnActionVideoFinished/SetMoveAnimation 两路经 RestoreIdleVideoSurface 回主 RT+原比例；固定裁窗方案对动作片失效已证——R10 主体活动域 1088px>768 且 f27 单帧主体 984px 超 768）；③**SkillConfig.SkillData.动作视频+动作片缩放补偿 字段**，BattlePlayer SkillCast 分支消费（三参传法）。**时轮节拍对齐视频（2026-10-05 拍板「前摇/两发间隙/后摇对齐视频节奏」）**：hitInterval 0.15→0.39（第二发 0.69s）、recover 0.91~1.50、totalTime 1.50。**发射时刻改拍（2026-10-05 晚用户拍板「为了对齐动画，把射箭改到0.26」+「根据动画，第二发在0.91」）**：首发 0.30→0.26（判定/松弦音效 bow_release/前摇段终点三处联动、瞬时事件 endTime 同步=起点）、**连发间隔 0.39→0.65**（两发=0.26/0.91 随动画松弦时刻）、后摇 0.59s；安柏.md 时间轴/判定/音效三行+历史演进已同步，校准预览判定线实时可见（0.26/0.91）。编码配方：setpts 变速管线后仍需 -color_primaries/trc/colorspace bt709 + **-movflags +write_colr**（VUI+容器双写）；Unity 导入首 refresh 偶发 WMF 色彩告警=一次性噪声（二次 refresh 干净勿误修）。箭矢全身红已落地（BattlePalette 箭矢元素色板火=#F23829+ResolveArrowTint 三路+ArrowBolt 黑描边提亮中灰 96）。**爆发（arrowrain 四连射完整版）已生成待目检**（out_ar_r1/，技术面贴边 17 帧/复原 2.0 同 R10 档；用户拍板先接战技）——通过后同管线接入：3.4x 加速落地+时轮 4 段（0.30+间隔 0.39×4?）节拍对齐。


- [2026-10-05 10:43:29] 【时轮编辑器动作片校准系统已落地（2026-10-05，编译 0 错、回读断言全绿、未提交待目检；同日易用性重排二批+叠影修复批）】用户需求=时轮系统加强：可编辑动作片播放速度/缩放/位置微调+实时预览，保证观感统一与代码行为对齐（安柏战技二连射）。落地三件：①SkillData 新增 动作片播放速度（0/负=按1；运行时=战斗回放速度×倍率）+动作片位置偏移（Vector2 世界单位，播放期间偏移/播完恢复）；②BattlePlayer SkillCast 分支传 _playbackSpeed×校准速度+偏移；UnitView.PlayActionVideo 四参（含偏移）+缓存 quad 基准位；**顺手修复既有朝向 bug**：PlayActionVideo 覆盖 localScale 丢朝向 sign（向左施放动作片恒朝右）+RestoreIdleVideoSurface 恢复 baseScale 丢 sign（播完立牌翻回朝右）——两处均带 _faceLeft sign；③时轮编辑器（Tools/TG/时轮编辑器，minSize 1040×720）**两栏工作区布局（易用性重排）**：左=时间轴+选中 clip 面板（编辑动线就近）、右=校准列固定宽 540（flexShrink=0 防压缩；窗口残留窄尺寸时 Open 自动拉宽）——竖排结构：技能反查（timeline 引用→skillID 名兜底）→富文本信息卡（对齐状态绿✓/黄偏差/红超出富文本色）→大画布 508×290（CalibCanvasW/H 常量）→播放行（播放/重播/◀1帧/1帧▶四键横排+时刻 Label 实时刷新）→参数区（CalibSliderRow 三段式=标签64+Slider flexGrow+数值框62；Slider 拖动连续生效不记 Undo（防撤爆），数值框输入记 Undo+越界回钳）→按钮行（自动对齐总时长/保存）。预览引擎=双 VideoPlayer（idle 循环播+动作片一次性）→ ChromaKeyVideo Blit 抠色 → IMGUI 固定 rect 画布（**Blit 前必 GL.Clear 目标 RT**——ChromaKey 材质 Blend SrcAlpha OneMinusSrcAlpha，不清零则逐帧叠影=用户报障「同位置完全不透明叠加」实锤；画布禁用 GUILayoutUtility.GetRect=高频重绘下 rect 逐移叠排）。播完自然停=显式 Pause（Tuanjie WMF isPlaying 怪态）+标志翻转（frame>=frameCount-1 判据）；末帧再播=frame=0 回零后 Play（播完态 Play 续播 0.04s 观感无反应根因）。窗口销毁/收起 teardown 全资源。**既有缺陷顺修**：Open() 菜单路径 Selection 载入 _asset 后原本不刷新（GetWindow 的 CreateGUI 先跑、赋值后空载）——补 LoadAsset(selected) 调 RefreshAll。

- [2026-10-05 10:33:37] 【Edit Mode VideoPlayer 编辑器预览五坑（2026-10-05 时轮校准预览实证，Tuanjie 1.9.3）】①**编辑器模式 VideoPlayer 可播放**（VideoPlayer→RT→ChromaKey Blit→IMGUI 画布路线全程可用），但**编辑器失焦时 WMF 泵深度冻结**——不止不推进：frame/time 的 getter 恒 -1/0、setter 静默失效（prepared=True 也瘫），EditorPrefs InteractionMode=1(No Throttling)+EditorApplication.QueuePlayerLoopUpdate 都救不动；恢复前台即续播、API 复活。**含 frame/time 判据的逻辑只能前台目检验证，CLI 反射断言在失焦下必假阴**；"预览不动了/第二次播放无反应"先查编辑器是否前台；②**窗口构建上下文（CreateGUI/RefreshAll 链）中调 Play() 请求可能被引擎丢弃**（实证 frame 恒 -1 不起播）——正解=EditorApplication.update 里自愈重试：isPlaying=false 且 frame<0（从未起播判据）时再 Play()，update 正常上下文必生效；③**isLooping=false 播完 isPlaying 不恒翻 False**（Tuanjie WMF 怪态：卡末帧 isPlaying 可恒 True）——播完判据用 frame>=frameCount-1 双通道（|| !isPlaying），勿只信 isPlaying；**自然停处理必须显式 Pause()**（怪态 isPlaying=True 下不 Pause 则下次 Play() 是 no-op=「第二次播放无反应」根因）；**播放按钮从末帧重播必须先 frame=0 再 Play**（播完态 Play 从当前位续播——1.5s 片只续 0.04s 观感无反应）；④**paused 态赋值 frame/time=seek 并渲染**（逐帧步进用：Pause 后 frame±1 即可；unprepared 态 setter 静默无效）。⑤**IMGUIContainer 内 GUILayoutUtility.GetRect 在视频播放高频重绘下 rect 逐次下移**——每帧多画一份内容竖直叠排（用户报障「很多个安柏」）；预览画布一律固定 rect=new Rect(0,0,w,h) 不走 GUILayout。另：EditorWindow 私有字段（无 [SerializeField]）域重载即丢——选中资产载入路径必须在赋值后显式刷新（LoadAsset 模式）；Tuanjie UITK IMGUIContainer 无 Repaint()——用 EditorWindow.Repaint() 窗口级重绘连带画布。
- [2026-10-05 12:17:48] 【时轮编辑器 2026-10-05 易用性三批收尾（编译 0 错；clip 面板/窗口收起/钳制三修复均已反射断言验证，未提交随批）】①**clip 面板窄栏适配**：左栏化后字段行加 flexWrap、拍板一行 2 个（起点|终点 / 连发间隔 / 弹速|判定直径 / 射程|提示），FloatFieldOf 宽 170+**flexShrink=0**（一行 3 个时标签占满 130 声明宽、输入框被 shrink 压成「(」全不可见——用户截图实锤）；hint 文本 whiteSpace=Normal 折行；panelScroll maxHeight 240。**UITK 反射断言陷阱**：resolvedStyle.width 读到的是声明宽（130），实际渲染被 shrink 压缩不可见——**断言 UI 可见性勿只信 resolvedStyle.width，要 flexShrink=0 消除压缩源**。②**「启用预览」关闭塌缩事故**：section width=0+display=Flex 时标题/开关溢出挂窗口右缘成竖条（用户报障「全部塌缩到右边」取证实锤）——开关所在节勿用 width=0 收起，改为**收窄条 230**（标题+开关必须可见可重开）；width 收起仅配合 display=None 可用且开关须在节外。③**外部改资产必须 LoadAsset 刷新窗口**（AI 工作流铁律）：时轴 clip 块=RebuildTimeline 构建时快照，直接改资产不触发重绘——用户在旧快照上拖拽会把 clip 拖歪（startTime 回旧位+右缘拉到 24.05 实证）；AI 改资产后必调 LoadAsset(资产) 走全刷新。④**时刻钳制拍板（用户拍板「优化」）**：拖拽 Move=宽度保持平移、起点钳 [0,totalTime−宽度]；Resize 终点钳 [起点+Snap, totalTime]；面板起点/终点输入、新建 clip 同钳（ClipTimeCeiling=totalTime，≤0 移动类动态时长约定不设限）；总时长改小存量超界 clip 不自动缩（校验行警告兜底，防静默改数据）。DragState 补 startWidth 字段。
- [2026-10-05 13:23:36] 【试招沙盒系统已落地（2026-10-05，时轮编辑器「开一把试招」按钮；编译 0 错、BuildSandbox/反查/温迪在库静态断言全绿、未提交待实战复测）】用户需求=时轮编辑器加「立刻开一把」：进战斗、带正在编辑的单位、玩家手操全部技能（无 AI 操控）、对面放温迪木桩。落地七件：①**Unit.TierOverrideStars（[NonSerialized] int，-1=无）**——操控层级覆盖位：TierOf（唯一换算出口）读它优先——**仅改操控分档（5=魔神档玩家全手操），数值回落公式仍读原星=数值原味**；Unit 运行时实例零资产污染随战斗回收（否决过两案：改 UnitConfig 资产星级=污染+需还原台账；克隆 UnitData=普通类浅拷贝要反射且 JsonUtility 会断 Unity 引用）；②BattleLaunchConfig +IsSandbox/SandboxAllyUnit/SandboxDummyUnit + **BuildSandbox(ally,dummy)**：P2 **IsAI=false 空座**（勿改 true——AI 配额脑会操温迪反击=不是木桩）；③BattleSimState.SandboxAutoPassPlayerId（空座玩家 id）；**③b UnitState.tier 字段（快照承载操控层级，2026-10-05 战技被拦修复）**：Host BuildUnitState 从 TierOf 填（覆盖随快照进客户端）；**HUD 全部层级门控消费点改读快照 tier（0=未填回落原星级）**——SelectedUnitTier/IsSelectedPlayerDomain/GetSelectionBlockToastKey（Battle_Autonomous「伙伴技能自主」拦截源）/HasSkillResources 体力镜像/fallbackStamina 五处——**教训：Unit 实例上的覆盖位只活 Host 侧，客户端 HUD 只见快照——跨端覆盖类字段必须经快照传递，否则 UI 门控按原数据拦截（用户实测战技点不动+「伙伴技能自主」提示）**；Host 校验第二道（TurnFlow TierOf）本就单源无需改；④**TurnFlowController.BeginSelectPhase 沙盒即时 Pass**：空座方选择阶段一开即交 Pass（不等 8~25s 超时兜底——空座无真人无配额脑回合推进全靠它）；⑤BattleScreen AssembleRoutine 沙盒分支：Sim 标记+A 方 SpawnSandboxUnit（层级覆盖 5★）落出生中心东 1 格（中心格=协议核心）、B 方温迪 SpawnDebugUnit 落同 row 东 6 格（**同行=十字直线射程内+同屏**；温迪 5★ 魔神档无自主脑=纯站桩）；协议核心照放（温迪打死战斗不结束、B 核心在 B 区）、配额脑循环 `!IsAI continue` 天然不挂空座；⑥BattleSession.SpawnSandboxUnit=SpawnDebugUnit+GetUnit 改 TierOverrideStars；⑦**时轮编辑器「开一把试招」**：FindPreviewSkill 反查持有者→Play 中直接 Launch；**Edit 中 EditorPrefs 惨透（域重载安全）+Boot 场景兜底（SaveCurrentModifiedScenesIfUserWantsTo 先问询未保存改动）+EnterPlaymode+[InitializeOnLoadMethod] playModeStateChanged 钩子（勿挂窗口实例——EnterPlaymode 域重载后窗口 OnEnable 与 EnteredPlayMode 事件顺序无保证）→等 MainHall sceneLoaded→**轮询 InputLocks 无 SceneTransition 锁**再 Launch**（**时序勘正 2026-10-05 二修**：一修「等 MainHall sceneLoaded+delayCall 直 Launch」实测仍停大厅——sceneLoaded 时 Boot→大厅的**转场揭幕动画还在播（SceneTransition 输入锁未释放）**，GameScene.LoadSceneWithConfig 开头的锁分支 Warn「场景正在切换中」静默 return 把 Launch 丢弃（console 栈实锤）；正解=轮询等锁释放（转场完）再 Launch，30s 超时兜底+退 Play/超时清 prefs 防幽灵启动。**教训：场景切换类启动要同时等「目标场景加载完」+「SceneTransition 锁释放」两个条件，只等前者必被锁拦截**）；ExitingPlayMode 清理 sceneLoaded/update 双钩子防泄漏）。资源语义：魔神操作体力 10/次（60=6 操作/回合）、元能战技命中+10 攒 50 放爆发=自然循环，暂无沙盒资源豁免（不够用再拍）。复测点：按钮→进战斗→安柏手操战技打温迪、温迪全程站桩、回合不空等、退出战斗资产星级无变化——**全链复测通过（2026-10-05 用户「验证通过」）+试招沙盒三连报障修复（启动双条件/层级快照）后已随批入库：提交 c9182ae 已推送（543e06f..c9182ae，2026-10-05 收尾批，含 B-S4c 尾巴动作片素材+箭矢色板）**；收尾沉淀=docs/18 决策四十一+docs/14 §118/§119+docs/17 时轮行+gic-battle-hud/gic-editor-tool skill+HANDOFF 板。
- [2026-10-05 13:52:18] 【箭矢视觉高度校准链已落地（2026-10-05 用户拍板「调整射出去的箭矢的高度，对齐动画里的箭矢位置」；编译 0 CS 错；**合并副本漏抄返修已落地未提交**——首版实战报障「调整了箭矢高度开一把没变化」：磁盘值 0.32 已存好但 **TurnResolver.MergeDamageEffects 合并副本 new DamageEffect 重建对象漏抄 ProjectileHeightY**→每发箭高恒丢回落默认 0.45；返修=副本初始化器补抄）】数据链五件=SkillTimelineClip.projectileHeight（时轮判定轨载荷族——速度/直径/射程/箭高四规格归时轮；0=默认）→ ProjectileEffect.Height（EffectCompiler 构造透传）→ ProjectileResolver 命中产物 DamageEffect.ProjectileHeightY 补写+消散 vanishCmd.arrowHeightY → BattleCommand.arrowHeightY（千分 int；0=回落默认——旧回放向后兼容同 crit 先例）→ BattlePlayer 命中/消散两投射物协程起飞/落点同高（原两处硬编码 0.45 改读命令）；默认收口=BattleMetrics.ArrowFlightHeight 0.45；编辑入口=时轮编辑器判定 clip 载荷行「箭高」字段。**纯视觉参数——判定圆柱与世界高度无关零逻辑影响**。**教训（通则）：BattleEffect 效应类新增字段必须排查全部「副本重建/合并」点抄字段——MergeDamageEffects/吸收构造器等任何 new 副本处漏抄=字段静默丢零回归**。安柏校准参考：立牌高 1.1 格、动作片弓位≈立牌 60%~75%（用户已校准 0.32 存盘）。**悬浮锚定返修·二连（2026-10-05 三报→四报「仍然没变化」取证实锤坐标系链：CellToWorld.y=格面表面高度〔安柏场景=0.5〕、UnitView 根 y=格面〔悬浮不在此！〕、悬浮在 AvatarTilt 子物体 localPosition.y=hoverHeight〔安柏 0.5→立牌底世界 1.0、弓位≈1.64〕——一修读 casterView.transform.position.y=只到格面漏 hover 0.5=箭仍从脚下飞出）**：终版=**UnitView.AvatarBaseWorldY 单出口（AvatarTilt 世界 y，无倾斜组回落根 y）**，命中/消散两路同源 casterRootY=AvatarBaseWorldY+arrowY——预览贴地校准值实战直接复用（立牌相对高度恒等：预览 0.32=贴地立牌 29% 处，实战=悬浮立牌 29% 处）；地面单位（hover≈0）行为近旧零回归。**发射点视差返修（2026-10-05 五报「几乎对了，但拖动摄像机位置箭矢就不对齐」——纸片人面片几何固有：立牌=55° 后仰 2D 面片 billboard 随相机转，动画弓位是**面片上一点**投影随视角动；箭起点若为固定世界点只在默认视角与面片内容重合、视角一移即错开）**：终版=**UnitView.ProjectileOriginWorld(localHeight)——发射点挂立牌面内局部系**（AvatarTilt.TransformPoint(0, 箭高, -0.06)：z 略前于牌面防穿模）——立牌转=发射点与弓同步同视差，任意相机位对齐；**to.y=from.y 水平直线**（命中/消散两路同构）；飞行段保持世界系正常视差。AvatarBaseWorldY 保留为公共贴牌基准 API。**通则：贴 2D 面片内容的特效起点勿用固定世界点——挂面片局部系（TransformPoint）随 billboard 同视差；世界点只在单一视角下「看起来对」。**竖直+投影补偿终版（2026-10-05 七报「箭矢斜着飞」——面内爬升点的 sin55° 水平前伸≈半格让起点凸在立牌前方，东射轨迹屏幕斜线；**纸片人几何两难：面内点贴弓但带前伸=轨迹斜、格心点轨迹正但屏幕错位**；破局=**投影补偿**：55° 俯角相机下竖直线屏幕压缩 cos55°——竖直 v 的屏幕高≈v/cos55°——**起点取格心 xz+世界竖直 y，屏幕投影天然落到面片弓位**〔0.37 竖直屏幕≈0.64=弓〕）**：终版三件=①ProjectileOriginWorld 改义：xz=立牌中心、y=基座+竖直（去 TransformPoint 面内爬升/去 z 偏移）；②箭高字段语义=**世界竖直高（相对立牌底含悬浮）**；③预览水平线画在帧内「竖直÷cos55°」位置——用户贴弓操作不变、读数自动=竖直值。**资产值已代换算：0.87（面内）→0.50（竖直）屏幕投影位置不变**。**通则终版：斜面立牌上的贴片对齐=格心 xz+竖直高+靠相机俯角投影补偿贴屏，勿沿面片爬升取点（前伸破坏轨迹）；竖直→屏幕的换算 v/cos(俯角) 是这类校准的通用钥匙**。数学推导实锤：发射时立牌播宽幅动作片〔quad 面内高=显示高 1.1×comp 1.29=1.42〕，弓在帧内≈45%→**面内 0.64**；把「竖直标尺读数 0.32」直接当 TransformPoint 面内值=差立牌屏高 29%=「完全偏」；取证时弓位吻合是 idle 尺寸巧合——**取证几何要按发射时刻的 quad 状态算勿拿静止态**）**：终版=箭高字段语义统一「立牌面内高」（tiltGroup 局部 y——屏幕视觉高=面内长〔视线垂直面片〕，TransformPoint 零换算）；预览水平线改**帧内标尺**（帧底=动作片摆放底边含位置偏移、帧高=idleH×comp 与动作片同 rect、帧内比例=箭高/(1.1×comp)）——水平线贴帧上弓位的读数=实战面内值所见即所得；**用户需重校一次箭高（旧 0.32 竖直标尺→面内≈0.64）**。**通则：2D 面片动画上点的对齐值=面内坐标非世界竖直；校准工具的水平线标尺必须与对齐内容（帧）同 rect，跨标尺读数必错**。**取证方法论：暂停态反射调私有静态（MergeDamageEffects 喂带新字段的 DamageEffect）可直接验证 Play 域代码版本与字段是否存活——比截命令流轻**。
- [2026-10-05 19:07:41] 【箭矢共面+箭高十轮定案（2026-10-05，**取代**「箭矢视觉高度校准链」长条目内八轮/九轮「终版」结论；**用户实机验证通过，已提交推送 46cef9b=c9182ae..46cef9b**）】①用户两报：时轮预览线贴弓（0.87）但实机箭明显偏离期望+「箭矢和立牌不在一个平面上」。②暂停态取证实锤双根因：(a) 时轮预览帧**底边锚定** vs 运行时 UnitView quad **中心锚定**（帧实机低 0.16）+pxPerWorld 比例尺 2 倍错（idleH/0.55 应为 /1.1=0.55×avatarScale2）=校准预览系统性说谎，0.87 是错误标尺读数；(b) 八轮「格心 xz 起飞+SolveEqualScreenHeight 屏幕解算」把箭放格心深度，立牌面在同高度前移 sin55°×面内高≈0.5 格=不共面。③十轮定案已落地：**箭全程贴立牌面飞**（起点=ProjectileOriginWorld 面内弓位点本体、终点 z 同加面前伸量=两端同面同深→东西向屏幕水平且与一切同俯仰立牌共面；SolveEqualScreenHeight 已删）；**箭高=绝对面内高**（立牌底含悬浮起算、预览线同语义**不随帧动**——帧随 comp/偏移移动后需重贴弓）；预览帧改中心锚定同构。④Amber_DoubleShot 箭高 0.87→**0.61**（ffmpeg 抽两发射击帧+掩码行计数像素测量动画箭位于帧底起 58.94%→0.606；两发共用一条 LineProjectile clip 的 hitInterval=单值天然管两发）。陷阱=docs/14 §120、勿当 bug 修=gic-battle-hud **判定教训：对齐类编辑器预览的摆放数学（锚定/比例尺/基准位）必须与运行时逐式同构否则必说谎；对齐参数验收值一律像素测量（抽帧+掩码计数）勿肉眼估读；暂停态取证可以对「在途箭 world y」与「各候选语义解算值」逐位对账一发定位**。
- [2026-10-05 19:19:04] 【per-unit 专属弹射物体制已落地（2026-10-05 拍板「根据现有的安柏实机美术，让ai生成单独的箭矢，此后每位伙伴角色都单独定制弹射物，通用染色箭矢保留给眷属」=docs/18 决策四十二；编译 0 错 0 警、未提交待用户目检）】数据位=UnitData.专属弹射物(Sprite)+专属弹射物缩放（乘 BattlePlayer「箭矢长度」bounds 归一化，1=同通用箭）；消费链=BattlePlayer._customProjectiles（CreateView 建场登记/ClearViews 同清）→CreateProjectileVisual 单源（command.actorUnitId 查表）：已配=专属素材+**白染**（美术即最终色，勿再元素染色——「安柏箭不是色板火红」是定案勿"修"）、未配=通用 ArrowBolt+箭矢色板染色（眷属/未定制路径零回归）；命中/消散/箭雨三路全消费（PlayRainArrowCoroutine 加参透传）。首件=安柏 amber_arrow.png（Resources/UI/Battle/；SeeDream 生成+安柏立牌画风参考锁风格；像素验收四角透明/长宽比 5.45/PCA 校正 -0.94°→0.01°/火焰橙集中右端=箭头朝右；裁内容框+长轴 512=ArrowBolt 同导入规格 PPU100/中心 pivot/maxSize512，屏上 0.49×0.086 格）。**后续伙伴定制弹射物（芭芭拉水球=下一位候选）=AI 生成素材+配 UnitData 两字段即生效零代码；大小调参走专属弹射物缩放勿改全局「箭矢长度」**。安柏.md/docs17/18/gic-battle-hud skill 已同步。
- [2026-10-05 20:40:29] 【序列帧立牌动画路线已全库移除（2026-10-05 拍板原话「序列帧是以前尝试时遗留下的，全部移除，统一用视频或静态立牌兜底」=docs/18 决策四十三；编译 0 CS 错；同日先移 立牌动画帧率 字段、用户再拍全拆）】移除面=UnitData.立牌动画帧（Sprite[]）+帧率（先前已移）两字段+UnitView 序列帧播放机（_idleFrames/_idleFps/_idleIndex/_idleTimer+Update 换帧循环+Create 签名参数+hasIdle 分支）+BattlePlayer.CreateView 建场管线。**立牌动画兜底链从此二态：立牌动画视频→静态立牌（立牌图/头像）**；无视频单位 Update 零逐帧事务。拆前实证=35 单位帧数组全零（勿再说 36——曾笔误已勘正）、消费方仅 BattlePlayer/UnitView 两文件；UnitConfig.asset 已重存清孤儿行（裸中文+转义双复验零残留）。**注意**：①LoadingOverlayDriver 徽标序列帧=独立在用系统（加载页势力徽标），与立牌无关勿误伤；②gic-paperdoll skill 现已实际存在（2026-10-04「实体不存在」记忆过期作废），其序列帧条目已同步为「已全库移除」；③同日遗留待拍板=移动动画校准方案 A——**已拍板并落地（同日决策四十四「应当尽可能统一」推翻方案 A）：勿再按方案 A（单位级三字段+校准列卡片）施工**，定案=移动片 per-skill 统一（迁 Move 技能动作视频+PlayActionVideo loop 通道，见下一条记忆）。

- [2026-10-05 19:57:47] 【移动动画已 per-skill 统一（2026-10-05 决策四十四，用户拍板「应当尽可能统一」承接「移动难道不也是个技能吗」；编译 0 错 0 警；未提交待复测）】①移动循环片从 UnitData.移动动画视频（**字段已删**）迁至 **Move 型技能 SkillData.动作视频**（amber_fly_loop→Amber_FlyingChampion——全库唯一在用移动片=安柏；安柏的 Move 条目本就是专属资产非共享 Common_Walk，14 单位有 Move 条目其中 5 条专属）；②消费链=SkillCast 分支按 skillType==Move 派发：Move=SetMoveVideo 登记循环片+三校准参、其余=PlayActionVideo 一次性；SetMoveAnimation 走 **PlayActionVideo loop 通道**（同机件双 RT+缩放补偿+位置偏移+速度=回放速度×校准倍率——**快进不再脚滑**〔旧恒 1x 隐性缺陷顺修〕；循环+随机相位+isLooping 守卫不自动回切）；SwitchToIdleLoop 两路共用收口+_moveLoopActive 精确归位；无登记片=待机照播零回归；③时轮编辑器对 Move 技能可直接校准（预览 isLooping=true、信息卡不比总时长改观感提示）——**勿再找单位级移动动画字段，新角色移动片=配 Move 技能资产动作视频**。④编辑器同日因 PackageManager 内部 bug（AssetStoreDownloadManager.OnBeforeSerialize NRE 升级 0x40000015 致命退出、栈全引擎侧非工程代码）崩溃过一次，重启即恢复；若再撞同款崩溃=清 Asset Store 缓存/关其窗口，勿往工程代码归因。
- [2026-10-05 20:26:02] 【动作片建场预热已落地（2026-10-05 决策四十五，用户报障「每次首次使用移动或飞行时安柏这块会黑屏，应当让资源提前加载」；编译 0 错；未提交待复测）】根因=**动作片 RT 新分配恒黑+VideoPlayer 首开解码 ~100-300ms 才落首帧**——非资产未加载（战斗视频是 Config 直引用随 UnitConfig/SkillConfig 常驻；AssetCache/Addressables 分工管大贴图/立绘、且预载 VideoClip 也预热不了解码器——用户提的「统一 redis」对本症状不适用，已在交付说明）。修法=**建场预热**（项目既有 Prewarm 先例=面板预热泵）：CreateView 收集本单位全部技能动作视频（含 Move 移动循环片，dedupe、仅视频路径单位）→UnitView.PrewarmActionClips=per-clip RT 池+临时 VideoPlayer Prepare() 预解码首帧渲入池（5s 超时兜底回落旧行为）；PlayActionVideo/loop 通道按片取 RT——预热命中=带首帧零黑屏。**RT 单槽 _actionRt 已改 Dictionary<VideoClip,RenderTexture> 池**（顺带消除 move 768²/战技 1344×768 交替播的每次重建；OnDestroy 池随单位释放）。安柏建场预热=fly_loop+double_shot 两片。后续新角色动作片自动进预热（零代码）。
- [2026-10-05 21:23:12] 【团结引擎编辑器静默崩溃证据包已立（2026-10-05 21:25；bughunter-tuanjie-editor-silent-crash，待交区根，本周 10/5~10/11 配额候选，四件套+崩溃报告三件齐可直接提交；截图拍包内 证据摘录.md；提交表单产品归属写引擎 Tuanjie Editor 1.9.3；本周候选合计 6 件接近 2 万积分帽）】**根因勘正（取代 10-05 19:57 条目④「NRE 升级崩溃」归因）**：真死因=后台线程 64MB 分配失败（`Could not allocate memory: System out of memory!`，MemoryLabel: TempOverflow，Vector.h:72）→ 引擎在后台线程尝试弹致命对话框被自己取消（`Cancelling DisplayDialog because it was run from a thread that is not the main thread`）→ **零提示静默退出 0x40000015**（dmp 解析=主动 RaiseException 非访问违例）；`StartBugReporterOnCrash: 1` 未生效（零弹窗零 Bug Reporter）；崩溃发生在用户手动 Play 实测中（MainHall 加载+3200×2000 背景视频开播后 ~1 分钟）。**AssetStoreDownloadManager.OnBeforeSerialize NRE=伴随缺陷非直接死因**（每次 Refresh/域重载复现共 4 次实录，重启后消失；与崩溃线程 udnSetPath 符号同子系统、相关性非因果）。诱因画像=编辑器实例 10-03 18:32 启动**连续运行 ~49h**+Play 大 RT+16GB 机器（页面文件峰值 5.2GB）=系统级提交耗尽，环境因素非断言引擎泄漏。**AI 工作流症状与诊断路径（复用）**：编辑器死=exec_editor_script 报 `Unity TCP socket ended`+unity_refresh 报**空错误**（无法区分桥忙/已死）→ 查进程身份（`tuanjie.exe` 可能是 Hub 启动器，须 WMI 验 CommandLine 含 `Editor\Tuanjie.exe -projectpath`）→ Editor.log 尾部找 crash handler 消息 → `%LOCALAPPDATA%\Tuanjie\Editor\Crashes\` 取 dmp/快照 → 重启即恢复。教训：跨天不关的编辑器长会话在 16GB 机器有提交耗尽静默崩风险；%TEMP% 崩溃目录重启会被清空——硬证据（crash.dmp/parsed.txt/Editor.log 摘录）已拷入包内长存。权威状态板=.codely\有效bug活动\00-新会话交接总览.md（已同步）。
- [2026-10-05 23:54:19] 【点击选中与瞄准体验升级=决策四十六（2026-10-05，编译 0 错+两 shader compile 绿+QueueSlot 契约自检过，未提交待用户复测）】用户拍板：①立牌/地面/底座圆盘同规可点+选中立牌描边（色=队伍色=与底座同源）+同格多单位重复点击轮换；②指定单位型瞄准同格点击轮换待定目标（延奏=施法者队伍存活/契约=敌队存活/单位指向爆发=含尸体）+拖动瞄准盘内候选头像阵列（复用 QueueSlot，4×4 起超 16 缩 5×5，悬停=格金高亮+目标描边、头像上松手=指定该角色，未命中回落锥角锁定）。核心实现：立牌命中=屏幕空间面片四角点内测试（PickPaperdollUnit 优先于平面取格——悬空 55° 面片平面取格失准=docs/14 §122）；描边=sprite 路径复用 FrozenSprite 直通态（_FrozenAmount=0 渲染恒等 §92，免新 shader/免 GraphicsSettings 登记）+视频路径 ChromaKeyVideo 内建块（形状外 8 方向环采样）；UnitView.ApplyAvatarMaterial=材质选择单点（frost>outline>默认，_avatarOriginalMaterial 捕获退役）；目标数据位 _pendingTargetUnitId（提交优先消费、ResolvePendingTarget 对账、非指定型恒 null）；CollectAimTargets=瞄准域单源（ComputeAimCells 三分支合一消费同源）。**勿当 bug 修**：点已选中立牌=轮换到下一单位（拍板语义）；描边材质=FrozenSprite 是刻意复用；候选超 16 头像缩小=拍板；瞄准态点不可选立牌=退回选中态。新陷阱=docs/14 §122（ShaderLab [Header] 属性连字符炸解析器+unity_shader.compile 对未 refresh 的磁盘改动报陈旧错误——先 refresh 再 compile）。落档=docs/18 决策四十六+docs/17 交互行+gic-battle-hud skill。
- [2026-10-06 00:34:29] 【决策四十六返修·尸体立牌可点（2026-10-06，编译 0 错，未提交随批）】用户报障「似乎无法轮换尸体」——根因=PickPaperdollUnit 沿用旧语义直接排除了尸体，而尸体立牌同为悬空面片：复苏瞄准点尸体立牌会视差回落身后格（误退瞄准/选不到尸体）。修法三件：①PickPaperdollUnit 尸体不排除（悬空面片必须命中其格）；②OnBoardTap 按状态分流——选中态尸体 precise 落空走地面语义（FindUnitAt 排尸体旧语义不变：点尸体格=选格上活体/取消选中）、瞄准态保留 precise；③HandleAimTap 增 precise 域校验（复苏点尸体立牌=直选尸体〔同格活体+尸体时点尸立牌=尸、点活立牌=活〕；延奏/契约点同格尸体立牌=域外视同未点按格选首候选——防域外 precise 污染目标）。**选中态尸体仍不可选**（旧语义维持，用户未要求打开）。docs/18 决策四十六①+gic-battle-hud skill 已同步。
- [2026-10-06 00:41:36] 【决策四十六返修二·尸体可点选查看（2026-10-06 追拍「尸体也能点选查看」，编译 0 错，未提交随批）】推翻上一条「选中态尸体仍不可选（旧语义维持）」——尸体与活体同规可选：①OnBoardTap 选中态尸体 precise 不再落空；②UnitsAtCellOrdered 尸体入轮换序**垫底**（己方活体→队友活体→敌方活体→尸体；活体优先=点尸体格默认选活体、活体轮尽轮到尸体；点尸体格不再=取消选中）；③RefreshFromSnapshot 清选中防线放宽=仅单位从快照消失才清（旧「isCorpse 即清」会吃掉刚选的尸体）。选中尸体=查看语义（技能盘/详情/描边/弧光全开，提交防线+Host 权威校验兜底同敌方查看态）。docs/18 决策四十六③+gic-battle-hud skill 已同步。
- [2026-10-06 00:57:59] 【决策四十六返修三·拖动头像盘取消语义（2026-10-06 追拍「松手没在头像上=视为取消」，编译 0 错，未提交随批）】推翻首版「未命中头像回落锥角锁定」——指定单位型拖动瞄准**头像阵列命中=唯一选择途径**：未命中头像=无有效瞄准（拖动中金格即隐、目标描边灭），松手直接取消（走既有 !pending→ExitAiming 链零新增判定）。FindNearestAimCellByWheelDirection 指定单位型不再消费（方法保留给自施放单格锁定/方向型锥角锥定不变）。docs/18 决策四十六④+勿当 bug 修 e) 条+gic-battle-hud skill（勿当 bug 修④+文件地图行）已同步。
- [2026-10-06 01:05:58] 【缺图兜底全内容位点接入（2026-10-06 拍板「所有期望显示但实际缺少时都渲染 missing_image 代替，允许强制拉伸为期望的大小」；编译 0 错、守卫 Ensure/Assign 行为断言+占位图加载自检过，未提交随批）】基础设施=既有 GIC.UI.MissingImageGuard（2026-09-13 方案二：调用点显式接入，全局扫描=禁区 docs/14 §40）。本批新接点位：SkillIconView.InitWithData（技能图标中央单点，含战斗键/背包/详情）+BattleHud.ApplyMoveButton 移动图标+UnitView.Create 立牌纸片人（Ensure+bounds 换算拉伸）+UnitView.RebuildBuffBadges（未知 Buff 类型照常显示占位徽章）+BattleOverheadBars 附着图标+BattleExecutionPreview.CreateEntry 头像/技能图标+BattleHud.BuildDragAvatarGrid 头像盘+ItemCardViewStrategy 物品图+CardViewStrategyBase.ApplySkinTo 皮肤切换+ItemDetailPanel+ItemCounterChip.InitItem（空图标不再隐藏图标位）+CharacterPanelController 元素/势力图标。既有已接（不变）：UnitCardViewStrategy 立绘/UnitDetailPanel 名片/祈愿立绘×2。**不经守卫**：程序化 UI 镀层（拖动盘/弧光/光柱/聊天气泡自带降级）+箭矢白光条专属降级。新内容图点位纪律=赋值处 Assign/Ensure 勿裸 .sprite=（已入 docs/20 §2 一行规则+docs/14 §40 清单）。
- [2026-10-06 01:13:34] 【技能按钮圆形遮罩（2026-10-06 拍板「技能按钮预制体缺少遮罩，应当像角色头像那样做遮罩处理」；结构断言 17/17 全过、refresh 0 错，未提交随批）】Skill 结构升级=Badge（圆底板）→**IconMask**（Mask 组件+showMaskGraphic=false+同 rect，遮罩 sprite=按钮自己的 badge 圆板——裁剪即按钮圆面；RelatedPanel 关联小图标无 Badge→回落 DragWheelFill〔头像牌同款〕）→Icon（拉满遮罩内）→Circle（环渲染序不变）。占位图/方形图裁成圆面，裸图案图标视觉零变化。**改动面=共享 Skill.prefab+全部内嵌拷贝 17 处**（BattleHud×10〔四键+BattleSkillDetail Icon+RelatedPanel×5〕/SkillDetailPanel×1/BackpackScreen×5；其余 LINKED 实例随共享编辑自动覆盖）；手术=exec_editor_script 幂等批改（LoadPrefabContents+SaveAsPrefabAsset，Icon 占原 sibling 位保渲染序）。寻址零影响=全库无按 Skill/Icon 路径 Find 的代码（SkillIconView 字段引用在手术中保留，回读断言过）。**注意：RelatedPanel 关联小图标本就无 Badge（skillBadge=null）非手术破坏**——断言须允许 badge 空。gic-battle-hud skill 文件地图已同步（IconMask=结构契约勿乱改）。
- [2026-10-06 01:21:04] 【底座盘沉水修复=透明排序定裁（2026-10-06 用户报障「角色到水面上时底部圆盘低于水面」；编译 0 错，未提交随批）】几何推演全对（盘=视觉表面〔0.36+波峰 0.03+余量 0.01〕+0.02=0.42>波峰 0.39）——真因=**Unity 透明排序 sortingOrder 支配 renderQueue**（共面双 quad 像素回读实验实锤：queue3000/order-1 vs queue2999/order0 → order0 后画）：焊接水面 order 0 整片盖住盘(-1)=沉水观感；旧「盘 queue3000>水 2999 故盘在水面之上」认知（§92/UnitView 注释遗留）被推翻，此前未察觉=极少有单位真站水格（飞行跨水决策三十九后才常见）。修法=BattleBoard.BuildWaterSurface 焊接水面 `sortingOrder=-2`（恒最低透明层：水面<盘 -1<瞄准贴片 0<立牌 10<箭矢 12）——勿"修"回 0。**通则：跨 renderer 排透明序一律显式 sortingOrder 勿依赖 queue 相对大小；「贴片沉水下」类报障先分层几何高度 vs 画家序**。落档=docs/14 §123+UnitView 注释勘正+gic-battle-hud skill 底座盘条目。
- [2026-10-06 01:25:20] 【底座盘不透明黑描边（2026-10-06 拍板「继续优化：底部圆盘需要加上不透明黑色描边」；编译 0 错+像素回读断言三 PASS〔中心盘色/外露环纯黑 a=1/环外不遮〕，未提交随批）】实现=BattleViewFactory.CreateRing（程序化双圈带 mesh：内径 0.43/外径 0.57 归一化、全单位共享单实例）+DiscOutlineMaterial（共享不透明黑材质，Geometry 队列+Cull Off 常驻勿 Destroy）；UnitView 盘缘正下方 0.001 垫环（y 0.019 vs 盘 0.020）——**环内半被半透明盘盖住压暗过渡、外半露纯黑**（外露宽=盘径 7%，核心 0.8 大盘自动加粗）；尸体压扁随盘同形（SetCorpseVisual 同步 _baseDiscOutline）。**排序正交**：环=opaque+深度写入→天然画于水面/一切透明件之上（环更近相机、透明件其像素处 ZTest 淘汰），不依赖 sortingOrder 体系——描边在水格上同样实心。落档=docs/18 决策四十六⑤（含盘沉水修复=-2 水面同条目）+gic-battle-hud skill 底座盘条目（勿"修"回无环/改透明材质）。
- [2026-10-06 01:31:49] 【盘描边带宽砍半（2026-10-06 用户目检拍板「太粗了，砍一半」；编译 0 错+像素带宽断言 15px@1024px/单位=3.5% ✓）】CreateRing 环带内 0.43/外 0.57 → **内 0.465/外 0.535**（外露黑边=盘径 7%→**3.5%**：标准盘 0.0147 世界、核心 0.8 盘 0.028）；mesh 常量改即生效（工厂静态懒建、域重载后重建）。教训：像素测宽的"暗带扫描"阈值须全通道判定——只测 r/g 会把深蓝背景 (0,0,0.5) 误当黑带（本批首测误报 CHECK，dump 实证黑带 x∈[471,486] 正确）。docs/18 决策四十六⑤+gic-battle-hud skill 已同步 3.5% 口径。
- [2026-10-06 01:36:47] 【头顶条微调（2026-10-06 拍板「能量条紧贴血条不要有间隙+尸体同显血条和能量条」；编译 0 错，未提交随批）】①BattleOverheadBars.条间距 4→0（元能条紧贴血条正下方；两 BarBg 黑边框相贴=略粗分隔线属边框非间隙；运行时建件吃代码默认、Inspector 可调）；②showEnergy 撤 IsCorpse 条件（showEnergy=EnergyMax>0）——**旧「尸体隐藏元能条」预期作废**（gic-battle-hud skill 头顶条条目已勘正）：尸血条恒显=空条（Hp=0）、尸元能条=死亡残留值。落档=docs/18 决策四十六⑥+skill 勘正。
- [2026-10-06 01:44:35] 【头顶条重叠深度排序（2026-10-06 暂停态报障「我方安柏在下面、敌方芭芭拉上面 1 格，芭芭拉的条盖住安柏的条」；取证实锤+编译 0 错，未提交随批）】根因=Overlay 同级条目 painter 序=sibling 序，而 sibling=**建场注册序**（快照单位序）与空间无关——取证实证：U3 安柏（近 5.49）与 U10 芭芭拉（远 5.89）条锚点屏幕 y 仅差 1px（条区完全重叠），芭芭拉 sibling=9>安柏 2 后画盖住。修=BattleOverheadBars.SortItemsByDepth（Update 尾）：按相机距离排 sibling——**远者先画（底层）、近者后画（顶层）**，近处（屏幕下方）单位的条覆盖远处单位，随相机平移/缩放实时重排；顺序稳定零写（校验后才 SetSiblingIndex）+比较器实例复用零逐帧分配（首版 order 数组+静态比较器设计绕了弯，终版=List.Sort(IComparer) 直排）。**通则：Overlay 画布内多实例条的遮挡关系必须显式排序（sibling 序=画序），勿依赖注册序——凡"屏幕空间层跟随多个世界对象"的 UI 都有同款重叠问题**（伤害数字层 39 天然在上不受影响；头顶条内部自洽）。落档=docs/18 决策四十六⑦+gic-battle-hud skill 头顶条条目。
- [2026-10-06 01:48:06] 【决策四十六批收官（2026-10-06 用户「验证通过。收尾」）】战斗交互与表现批全部用户实机验证通过并提交推送——本条落地后，本会话前述各条记忆的「未提交待复测/未提交随批」尾注全部作废（以 git log 决策四十六提交为准）。批内十项：立牌点击/选中描边/同格轮换（含尸体可点选）/拖动头像盘（松手取消语义）/缺图兜底全位点/技能按钮圆形遮罩/盘沉水修复（sortingOrder 支配 renderQueue 实证 §123）/盘黑描边 3.5%/头顶条贴条+尸显条+重叠深度排序（近者覆盖远者）。
- [2026-10-06 10:52:05] 【决策四十七：拖动瞄准相机跟随三段改造（2026-10-06 用户拍板「快进慢出 0.5s+1 格余量+单位指向恒居中 MOBA 式/出屏瞬移+取消重置」；编译 0 错，未提交待复测）】取代 2026-09-26 旧指数趋近跟随。①方向型（centerMode=false）：金格出屏（余量 1 格=金格中心距视口边缘不足 1 格屏幕像素，按目标点实测 1 格像素换算）才发起 0.5s EaseOutCubic 补间把格移屏心、在屏内不动；②单位指向型（centerMode=true）：选中单位恒居中——在屏内→0.5s 补间居中、出屏→瞬移居中；③取消（松手无待定/取消钮上松手/拖动中点取消钮）→CancelDragFollow 瞬移回拖动起始注视点（会话首喂点快照 _dragFollowStartFocus）；松手留待定/提交=相机留位不重置。补间机制：目标变化 from 当前位重启（连续拖格=连续平滑追）、同目标续跑不重置、进行中不掐断。API：SetDragFollowTarget(worldPoint, centerMode)——HUD 喂点带 IsCurrentAimUnitTargeted 分流；旧字段「拖动瞄准跟随速度 8/跟随边距 40px」退役删除。改动=BattleCameraController.cs（三段跟随+补间+取消）+BattleHud.cs（喂点/两取消点）。落档=docs/18 决策四十七+gic-battle-hud skill 拖动瞄准行。复测点：①移动拖向屏边远格→相机快进慢出 0.5s 移过去（格距边缘 1 格内提前触发）②延奏拖到头像→相机 0.5s 平滑把该单位居中；拖到屏外单位→瞬移居中③拖回死区松手/取消钮→相机瞬回拖动开始位；松手留待定→相机留在拖到位。
- [2026-10-06 10:58:47] 【决策四十七④：点击式瞄准悬停格高亮（2026-10-06 拍板「点击式瞄准时鼠标停留在可选格子上时该格子也需要高亮」；编译 0 错，未提交随批）】瞄准格材质升级**三态单点 RefreshCellVisual**：待定金>悬停提亮（BattlePalette 新字段「瞄准悬停色」默认白 0.45——BattlePalette.asset 需烘值，改色走资产勿只改脚本默认〔§74 冻结陷阱〕）>推荐/不推荐；任何格材质变化（待定变更/悬停进出/还原）走单点重算，旧 RestorePendingCellMaterial 直赋散写退役。悬停检测=UpdateAimHover Update 轮询（Aiming 态且非拖动会话——拖动有金格/头像反馈指针语义不同；取格=TryPickBoardCell 视差修正同口径）；金格恒优先（悬停在待定格上仍金色）；悬停态随 ExitAiming 同清+材质 OnDestroy 释放。落档=docs/18 决策四十七④+gic-battle-hud skill 瞄准分色条目。复测点：点击式瞄准鼠标扫过可选格=提亮跟随、停在待定格上=金色不变、拖动瞄准时无悬停提亮。
- [2026-10-06 12:20:16] 【决策四十七返修：取消重置须覆盖全部取消路径（2026-10-06 当日报障「我取消技能后没有重置摄像机位置」；编译 0 错，未提交随批）】首版漏了两条路径且快照生命周期有误：①漏「松手留待定后点瞄准态点空白」与「瞄准态点空白」（OnBoardTap Aiming 分支）；②漏 OnCancelButtonClicked 无 wasDrag 情形（松手后 _dragAiming 已 false）；③根因=相机快照在喂 null 时被清——**修=快照独立于跟随会话（喂 null 不清，仅 Cancel/End 清）+ 全部取消路径挂 CancelDragFollow + 提交成功/阶段流转挂 EndDragFollowSession（清快照不重置防误重置）**。通则：「取消重置」类功能的路径覆盖必须全量枚举（含松手后准会话收口后的二次取消），勿只挂拖动会话内的两条；「预留态对象」跨喂点生命周期跟随功能语义而非输入事件。docs/18 决策四十七③已返修勘正+gic-battle-hud skill 拖动瞄准行同步。复测点：拖动瞄准松手留待定→点空白取消=相机回拖动前；瞄准中点取消钮=回； SubmitAim 成功=相机留位不回。
- [2026-10-06 12:33:25] 【CameraContext 语境相机解析单源已落地（2026-10-06 用户拍板方案 A「是不是应该加一个管理中心更好」→选项 A 最小收口；编译 0 错+三场景 Anchor 断言 PASS，未提交随批）】**定案：不建全量管理中心**（Map/Battle 相机控制器语义异构自治良好、输入面已统一 GestureHub、Cinemachine 对本项目负收益——WEGO 专属相机行为重写成本>收益）；唯一实锤痛点=相机引用解析族（历史两坑：战斗立牌朝向需显式指定/Pet tag 抢占把大厅背景缩爆 0.13 倍）。①新文件 Assets/_Scripts/Framework/CameraContext.cs：static CameraContext（Resolve()=栈顶注册相机、空栈回落 Camera.main 恒等旧行为；Push/Pop 按引用移除防 OnEnable/OnDisable 乱序；惰性清 destroyed）+ CameraContextAnchor 自登记组件（OnEnable 入栈/OnDisable 出栈——additive 弹层嵌套天然栈序：大厅恒栈底/地图弹层压顶/战斗 Single 独占）。②**tag 事实**：MainHall/Splash 相机=MainCamera tag；BattleScreen/MapScreen 相机=Untagged——旧 Camera.main 在战斗 Single 语境=null、地图弹层=大厅相机（全靠显式引用硬扛）。③12 处 Camera.main 兜底调用点全改 Resolve()（BattlePlayer×4+Tooltip 勘正/BattleDamageNumbers/BattleOverheadBars/BackgroundParallax3D/PaimonDropShadow/PetBehavior/PetLookAt/PetFrameStats/PetWindowController）+8 文件补 using GIC.Framework——**Pet 系行为恒等论证**：桌宠独立场景无注册→兜底 Camera.main 不变；游戏内=大厅相机不变；唯一行为变化=战斗语境兜底从 null/大厅相机变战斗相机（修复）。④三主相机已挂 Anchor（编辑器场景编辑）；Splash/测试场/桌宠场景不挂（兜底已正确）。⑤**规则**：新代码相机引用一律 CameraContext.Resolve() 勿裸 Camera.main（docs/20 §1.2 已录+docs/17 §3 语境相机行）。**⑥新坑=docs/14 §124：exec_editor_script 同帧 OpenScene 真重开+AddComponent+SaveScene 静默丢组件（SaveScene 返回 True≠写盘；AddComponent 后 isDirty=False 即中坑；活动场景 no-op 重开不受影响）——挂载与保存拆两次脚本调用**。边界：BattleCameraController/MapCameraController 零触碰；决策四十七 WIP（BattleCameraController staged/BattleHud/BattlePalette/docs18）与批同工作区未提交，收尾提交时注意归属。复测点：大厅视差正常/地图弹层开相机交互正常/战斗立牌朝向与伤害数字头顶条投影正常/桌宠不受影响。
- [2026-10-06 12:50:46] 【决策四十七返修二·拖动瞄准取消后相机被拉回（2026-10-06 当日报障「取消技能后相机瞬移回原位，但又被拉过去了，疑似协程没清理」；编译 0 错，未提交随批）】根因≠协程（补间=Update 状态机非协程，CancelDragFollow 本就清了 _followTweenActive）——真根因=**取消没清喂点目标 _dragFollowWorld**：HUD.Update 每帧恒喂 SetDragFollowTarget（退出瞄准后喂 null 看似天然收口），但取消回调（onEndDrag/onClick）与同帧喂点竞争（喂点先于取消）时，CancelDragFollow 瞬移+停补间后相机 Update 跟随块仍读到本帧喂的旧格→重新发起补间（同目标续跑守卫挡不住——active 已被清）；下一帧喂 null 已晚——**进行中的补间只看 _followTweenActive 不看目标位**，跑完 0.5s 把相机拉回旧格。修=CancelDragFollow+EndDragFollowSession 均加 _dragFollowWorld=null（目标位+补间双清；End 同族隐患一并收口——提交帧同款竞态会把留位相机拉走）。通则（docs/14 §125）：「每帧恒喂点」系统的取消动作必须清喂点目标（勿指望下一帧喂 null 兜底）；症状指纹「重置生效（瞬移可见）→又被持续力拉回」=重置动作与残留驱动并存，先查哪个状态位还在驱动。docs/18 决策四十七③二返段已补。复测点：拖动中/松手留待定/点空白/取消钮四路取消后相机稳回起始位不再回拉；提交成功后相机留位不被拉走。
- [2026-10-06 12:55:20] 【2026-10-06 会话收口（已提交推送 cc4279d=57ebb55..cc4279d，代理通走代理一把推成；直连 github 443 被 SNI reset 属常态）】单提交收口三批：①决策四十七批（拖动瞄准相机三段改造+④悬停高亮——他方会话落地本批收口）②决策四十七二返（取消竞态：Cancel/End 补清 _dragFollowWorld，docs/14 §125）③CameraContext 语境相机解析单源（方案 A：12 调用点改 Resolve()+三场景挂 Anchor+docs/17/20 规则+docs/14 §124 编辑器同帧挂载丢组件坑）。**本会话前述各条目「未提交随批/未提交待复测」尾注全部作废（以 cc4279d 为准）**。复测清单已交用户：四路取消后相机稳回起始位不回拉/提交后留位不拉走/相机跟随表现不变；大厅视差/地图弹层/战斗投影/桌宠四点零变化。工作区干净、编辑器 Edit Mode、协调板无需新增小节（决策四十七会话未登记板=其遗留由本收口条目交代）。

### Reference
- [2026-09-16 20:01:18] MC mod gichess（旧项目，Java/NeoForge）：源码 D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\（308文件），jar D:\Tuanjie_editor\gic\.codely-cli\webrefs\genshin-unpack\gichess\my\wg-0.2.d（2026-09-05 随 gichess 77.78GB GI 解包归档整体迁入 webrefs/genshin-unpack/，原 D:\Picture\gichess）。~20+角色，7元素18反应，蒙德延奏/纳塔夜魂已实现。**Why:** GIC 战斗系统 Unity 移植的架构参考。**How to apply:** 仅作战斗系统架构参考；**旧 mod 资产一律不再用（2026-09-16 用户拍板「旧 gichess mod 不要再用」：播报员/派蒙语音 wav、模型、贴图等一切提取物都不再作为 GIC 素材来源，含 TTS 音色克隆样本；2026-09-14 已拍音频/曲目不翻旧 mod，音乐素材由用户自行网找）**。需要查旧 Java 实现时按路径阅读源码。



- [2026-08-10 09:28:39] 技术陷阱与 Bug 修复记录：详见 docs/14-技术陷阱与Bug修复记录.md。
- [2026-09-19 01:58:22] GIC 文档体系：**docs/17-代码架构指南.md=新会话入口文档**（目录结构/核心系统速查/场景清单/配置资产/工作流速查/战斗规划/环境备忘），开工先读它再按需深入。地图：00-13 玩法设计与大地图（07=07-势力机制/ 目录）、14 技术陷阱、15 输入系统、18 战斗决策、19 AI派蒙、20 项目规范（规范权威）、21 商业化与发布边界、23 UI架构重构、24 手势输入层重构、25 指令系统、26 GG系统、27 派蒙语音TTS；子目录=07-势力机制/、active/（22-战斗系统技术设计）、designs/（设计稿）、units/（角色文档）、archive/（16 优化计划已归档）。**How to apply:** 新系统落地/结构性变化后更新 docs/17 对应小节保持时效；UI 面板制纪律=docs/14 §37-39b+gic-new-screen skill（P1-P4 已收官）。**已否决的"故意不做"勿再提**：PopupManager 折叠进 UIManager、god-class Presenter 级拆分（依据 docs/23 §6 P4 行）。






- [2026-08-17 10:32:10] 本机 ffmpeg：D:\Tool\FormatFactory\ffmpeg.exe（7.1 完整构建）。PATH 里没有 ffmpeg，媒体处理直接用此路径。已用它重编码 mc_16x10.mp4 消 VideoPlayer WMF 双告警（baseline profile 消时间戳；VUI+容器双写 bt709 消色彩；x264-params 与 -color_* 需同时给）。






- [2026-09-12 14:24:31] 惯性化参考源码：.codely-cli/webrefs/animation-transitions/InertializationForUnity/（InertiaAlgorithm.cs=五次多项式+四元数/向量数学；上游 github.com/portalmk2/InertializationForUnity，MIT；一手信源=GDC 2018 talk "Inertialization: High-Performance Animation Transitions in 'Gears of War 4'"）。本项目 PetInertializer.cs 已大幅分叉（固定系数多项式+帧间 dq 速度捕获防 ±2π 抽搐+起步拉引），原源码仅作数学对照用。GitHub 拉源码法（api.github.com contents 端点 base64 解码落盘）见 gic-webrefs skill。**How to apply:** 改惯性化数学/移植其它 GoW 技术时先读这份源码对照。

- [2026-09-26 00:41:02] GIC 远程仓库：https://github.com/xiaoding521234/gic（2026-09-09 用户拍板转公开；首推 ~900MB pack 含美术资产）。push 策略=先查 Clash 健康复用直连/代理（2026-09-25 起直连多把推成实证；权威流程=gic-wrapup skill 收尾节），api.github.com 直连稳定；GitHub token 获取法见全局 git-pr skill（P/Invoke CredRead，仅进程内使用勿打印）。**SSH 备用推送通道（2026-09-22 探明未启用）**：https 443 被 SNI reset 时 ssh.github.com:443 与 22 端口仍通，本机已有 ~/.ssh/id_ed25519（注释 gic-push-20260922），启用需 GitHub 网页 Settings→SSH keys 手动加 id_ed25519.pub 后 push 改 ssh://git@ssh.github.com:443/xiaoding521234/gic.git。GitHub >50MB 文件仅告警、100MiB 硬限拒推（zh-cn SDF.asset=TMP 动态图集、每补字近整份新增 blob，唯一持续增长大文件；**2026-09-19 推送实测已 64.83MB 超 50MB 告警线（推成），距 100MiB 硬限余量收窄，LFS/filter-repo 决策窗口临近**；**2026-09-21 再入 43.5MB 派蒙待机 anim 文本资产（dd0d818，编辑器重序列化产物——filter-repo 瘦身时可判废弃重生成）+ 1.3MB 纸片人 png，体积压力加重，瘦身决策宜提前**）；2026-09-11 实测服务端 size 907.4MB，瘦身路径=filter-repo（DevKey 清洗有先例）+LFS。secret scanning+push protection 已开（私有个人仓该 API 返回 422，须先转公开再开）。**2026-09-07 DevKey 清洗（filter-branch）重写过 8/30 后的提交 SHA**——docs/skill 引用的旧 SHA 已失效，git show 失败按提交信息 grep 重定位。





- [2026-09-11 23:09:27] [2026-09-11] [reference] codely-docs.tuanjie.cn 文档站抓取法（2026-09-11 实证）：web_fetch 对该站深层路径会回错内容（FAQ 壳/相邻页缓存），正确姿势=curl.exe -sL 直拉原始 HTML（PS5.1 的 curl 别名是 Invoke-WebRequest，必须写 curl.exe；URL 会 301 补尾斜杠），正文在 <article>…</article> 内，PS 抽取去标签后可能残留 NUL 字节导致 read_file 报 binary，需 Replace([char]0,' ')。订阅/积分规则三页：/subscription/pricing-details（套餐价格+积分上限表+增值包）、/subscription/credits-description（积分消耗顺序+LLM/多模态单价表）、/subscription/plan-and-billing（升降级/发票/增购）。


- [2026-09-19 01:58:22] [reference] GI 官方提取模型站点（2026-09-15 定案：**用户指定站=The Models Resource**，models-resource.com/pc_computer/genshinimpact/，访问经验=web-access skill site-patterns/www.models-resource.com.md）：①TMR=最正统 rip 库（779 资产；CF 拦 curl 需浏览器、search 端点 404、手风琴需 CDP eval）②GameBanana（3DMigoto mod 形态）③Nexus Mods ④GIMI 生态（GitHub+Discord，最大提取/移植社区）⑤Sketchfab（骨骼保留参差+DMCA 下架风险）⑥Open3DLab 系（自定义绑定非官方骨架）。**关键判定：官方动作兼容=模型须保留 GI 原始骨架（Bip001 骨名/路径）——第三方站常被重绑定不兼容，套官方 .anim 最稳是本地解包自提（gic-gi-extract 流程）**。中文圈无稳定官方提取站（模之屋/44mmd=MMD）。派蒙来源=TMR asset/328738（docs/19 §2.1 头行已记载；MMD 兜底包=模之屋线，docs/19「模之屋原始 PMX 包」行无误勿改）。

- [2026-09-25 23:23:56] 【EGamePlay 参照仓库正主勘误】docs/17 与 docs/active/29 所引 "qq362946661/EGamePlay" 已 404 失效；正主=github.com/m969/EGamePlay（2026-09-25 api.github.com 实证：2380★、MIT、fork 545、2026-09 仍活跃推送，topics=buff/skill/unity；另有 m969/AOGame=基于 ET 的续作 120★）。本地源码快照在 .codely-cli/webrefs/skill-timeline-system/EGamePlay_src/（拍平文件，AbilityEffect=EcsEntity+运行时组件化 EffectDamageComponent 等——与 GIC EffectCompiler 编译期展开的对照基准，2026-09-25 审查已核：GIC 选择更贴回合制）。引用时按 m969/EGamePlay 写。
- [2026-09-27 00:31:27] 【多模态积分单价快照（2026-09-27 抓 codely-docs/subscription/credits-description；模型档位=价格信号）】图/张：frontier 家族（日辉/耀斑）1K=75 全站最贵（4K=175）；Seedream Pro 1K=45（2K=180，IP 角色被版权拦截）；混元3.0=60；Qwen 1K=35（2K=60/4K=100）；Seedream lite=35；frontier_lite 25~40。sprite_sequence=150/次（16 帧固定）；图片超分=10/次；Qwen 分层=25/次；Pro 分层额外每张 45/90。视频 5s：Seedance2.5@720p=1890 > 2.0@720p=1245 > 2.0fast=750 > H3-2K=750 > H3-Max@768P=375 ≈ H3@768P=375 > Wan3.0=225 > Seedance mini=250 > HappyHorse=150。3D：Tripo P2 图生=1000 > P2 文生=850 > P1 图生=300 > P1 文生=250；Rodin/混元3.1=150。**How to apply：模型选型/强度对比/成本预估先查此快照勿重复抓页；价格随上游变动、隔久需重抓核对。**
- [2026-10-03 19:43:55] 【2026 游戏单位 AI 脑业界调研结论+一手信源（2026-10-03 三路网检，支撑 E 批次选型）】①2026 最先进 AI 脑=分层混合架构：BT 反射+GOAP/HTN 规划回潮（标杆=GDC 2025 KCD2 GOAP+MBT gdcvault.com/play/1035576、Bitpart HTN 多智能体 gdcvault.com/play/1035557）+utility 工程化+端侧 SLM 只做队友不做敌人（PUBG Ally 快慢分离=2026 标志架构）+窄域 designer-first RL（FC26 守门员已量产）；②商业战术层全部=打分制零搜索——Into the Breach 全文档（作者公开 archive.org/details/into-the-breach-ai：枚举×固定权重×top-k 随机，89 Meta 分，"玩家总以为 AI 做的比实际多"）、XCOM2=BT+ini 数据驱动（sterlingvix.github.io/xcom2ai）、botbowl 学术史=脚本 bot 长期碾压 ML（github.com/njustesen/botbowl）；③WEGO 同时回合制：Frozen Synapse=信息对称让简单 AI 成立（拟人>最优）、学术标准武器=PIMC（OpenSpiel 有参考实现）但生产零落地；④一手幻灯片全文 4 份存 %TEMP%\ai_research\（L4D Director/Halo3 Objectives「plinko」/HTN GameAIPro Ch12/GDC2024 自杀小队四层行为栈 Activity→Group→Agent→Actuation）——临时目录重启会清，需要长存时拷入 .codely-cli/webrefs/；⑤对马岛「incentive-based AI」与 Gears HTN 均未找到一手证据（子代理 5 轮检索证伪，引用时勿当定论）。
