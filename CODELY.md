## Codely Structured Memories







### User
- [2026-09-11 23:12:13] [2026-09-11] [user] 用户 Tuanjie AI 订阅=个人版 Max（月积分 160,000；三层滚动上限 5h=8,000/周=40,000/月=160,000；闲时=每日除 11:00-12:00、14:00-18:00 外，积分消耗减半即 token 翻倍；订阅积分月度发放不结转，增值包积分 365 天有效不清零；**增值包是否计入 5h/周/月速率上限文档未写明**，2026-09-11 核对定价页上限表口径为"每个套餐…可使用的积分上限"，倾向计入但不确证，建议撞限想靠增值包续命时先问客服）。**Why:** Max=付费订阅用户，TJGenerators 的订阅路由/高成本门按付费用户处理；周上限 40,000 是最常撞的节流阀（2026-09-11 用户实证撞周上限）。**How to apply:** 大批量生成（视频/3D/Pro 图）前先估积分是否撞周上限；被上限卡住时建议挪闲时（消耗减半）；计费规则页=codely-docs.tuanjie.cn /subscription/pricing-details。
- [2026-09-13 18:26:52] [2026-09-13] [user] 输入类 UI 用户要求 IDE 式体验（2026-09-13 指令输入拍板原话「就像我在idea里写代码那样，能够tab自动补全一个词」）：文本输入凡有可枚举词汇域（指令/物品名/角色名）应配补全列表——输入即出、Tab 补当前词、↑↓ 切换选中、点击行补全。**How to apply:** 后续新输入类 UI 直接沿用 InputPopupDialog 命令模式（Show 的 suggester 参数 + CommandSystem.Suggest 或自定义提供器），勿做裸输入框。
- [2026-09-26 20:20:22] 音乐选曲=用户委托流程（2026-09-14 拍板）：用户报曲名/截图清单，AI 下载落库**勿再问勿再归一**。全流程=**gic-music-dl skill**（QQ 源 FLAC 优先/原生响度直转/落库路径与曲池填写细节全在 skill 内）；站点 API=web-access skill site-patterns/qjjlb.quanjian.com.cn.md；FLAC 母带存 .codely-cli/webrefs/music-masters/。





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
- [2026-10-02 20:11:54] 【用户授权 AI 自行截图+识图看实际画面（2026-10-02 冻结表现调优时拍板，原话「你作为kimi k3，有识图能力，你可以自行看实际画面」）】对 2026-08-10③/08-21「AI 不自行截图判定画面效果」规则的放宽：用户明确认可用截图+自身视觉能力做效果迭代自检（本轮冻结 shader 四轮截图调参已被接受）。注意与「识图 AI 不可作内容判定依据」（2026-09-10，图案内容判定须像素级测量+用户目检）区分：画面观感迭代自检可截图，内容/图案是什么的结论仍不可只信识图。最终验收目检仍交用户。

### Project
















- [2026-09-12 14:24:31] Assets 清理定案（2026-08-16）：Material/Materials、Resources/Materials 三目录 8 个材质误删已 git 恢复，**勿再清理**（UIBlur/WishSmoke 在用，其余存疑也保留）；已删定案：Assets/Editor 空目录、Scenes/TestScene.scene、StreamingAssets/mc.mp4、Assets 根 6 个 CUsers* 垃圾文件。故意不做：NewAudioMixer、Art/Wish 与 WishArt 改名（Addressables 地址约定）；保留 GlowTest.scene+CardGlowTest.cs、兹白测试皮肤 zibai_skin_solid.png（UnitConfig 兹白 cards[1]，用户拍板不删勿当 bug 修）。**How to apply:** 不再动这批资产。










































































- [2026-09-26 20:20:15] 【搜索/文本判定铁律】search_file_content/glob/list_directory 受 .codelyignore+.gitignore 双层 ignore（Assets 的 png/prefab/asset/mat/wav、Mirror/kcp2k/Plugins 整目录、.codely-cli/.codely/Library/Temp/Logs/Export/Maps 等）——被任一层命中即**静默零命中/无名**，零命中≠不存在：查引用/资产内容/目录清单/skill 文件一律原生 `rg --no-ignore` 或 Get-ChildItem；喂多模态的 Assets 图片先复制到 .codely-cli/tmp；guid 判定一律 AssetPathToGUID/unity_asset get_info（meta guid 可能密文，文本反查假阴性，docs/14 §9）；.unity 与本地化表中文为 \uXXXX 转义，rg 中文直搜必零命中（搜转义形式或先解码，docs/20 §1.3；prefab 中文序列化字段名=大写 \uXXXX 转义、小写形式零命中）；.codely-cli/ 与 TextMesh Pro/ gitignored（skill/HANDOFF 勿提交；TMP 材质改动不入库，重装前备份）。细节 docs/14 §6.2。








- [2026-09-23 00:04:38] [project] 用户会并行开多个 AI 会话在同一项目分工开发：**回合开始先读 .codely-cli/HANDOFF-并行AI协调.md**（各会话文件归属/状态/编辑器使用权，状态变化写回各自小节）。git status 里非我产生的改动/WIP 文件（尤其未跟踪新目录）属其他会话在制品——勿动勿清理勿"顺手修"，除非它把整个程序集编译堵死才做最小语法解锁并明确告知；编译错误可能来自其他会话 WIP，先归因再动手；refresh/构建等编辑器级操作动前先看协调板避免撞车，被取消后勿立即重试，问协调节奏。












- [2026-09-26 00:33:38] [project] 【地图音乐与星落湖遗留】蒙德野外曲池（day 9/night 7，QQ FLAC 原生直转）与战斗音乐轮换链已全量接线（清单=PositionConfig 配置资产）；战斗开局 6:00 落夜晚池（改原神式 6-19 边界待用户反馈）。**遗留：MapConfig 星落湖锚点仍占位 (70,28) 待取点器「锚点重定位」标定（标定前坐标是假的勿当实数据）——涉星落湖锚点的工作先核标定是否已完成。**



- [2026-09-19 01:57:27] [project] UnityInsight 索引系统档案（活锁 bug 已提交 Bug Hunter，证据包=.codely\有效bug活动\已提交\index-build-failure）：架构=CLI 侧 node 守护进程（unity-insight-cli.js serve --daemon）持有全部索引，**被动模式**——杀掉不自动重生、重生后须 Cowork GUI 发构建指令（编辑器 AI/ 菜单只有 Check Connections/Force Reload）；数据目录=<项目>\.codely-cli\UnityInsight\（构建中=index.db.tmp+WAL，ready 后写 index.current 指针）。活锁特征：first_build 磁盘写入 ~6 分钟后冻结、进程 4~5 核满负荷+RSS 狂涨至 7.5GB+零 I/O、index_building=true 永不翻转、GUI 无进度无报错；~\.codely-cli\crash-logs\exit-*.json（uptime<1s）=单实例锁握手记录属常态勿误判崩溃。处置=Stop-Process 杀 daemon+清 tmp 三件套。

- [2026-09-29 10:48:17] [project] Bug Hunter 提交活动：权威状态板=.codely\有效bug活动\00-新会话交接总览.md（涉提交/查已立包状态先读它）；活动规则与四件套流程=全局 skill codely-bughunter；**立包位置约定（9/27 用户拍板）=待交包一律放 .codely\有效bug活动\ 根，提交后移 已提交\、撤案移 不正确\（skill 已同步）**。9/22 与 9/29 两轮结算均已了结（9/29 用户确认：上周 6 件全提交、积分已到账）。**待交三包（本周 9/28~10/4 全新配额）**：①bughunter-credit-deduction-order（扣减顺序固定赠送>订阅>增值包，永久积分优先烧致月底清零的订阅积分压队尾作废；建议可配置顺序或"先到期先扣"；四件套齐可直接交）②bughunter-resume-empty-memory（续接失忆：forged_memory 快照空串，被吞消息可从 session JSONL 找回；**用户 9/29 拍板"翻篇不追"，包保留备查**）③bughunter-loop-recovery-loses-user-msg（**9/29 新案：模型输出退化循环 3 分 44 秒（UI 大量重复输出）→content_probe 循环检测拦截→恢复回复完全不知道触发循环的用户消息（6 条拍板）——消息连 JSONL 都未写入、被 User 角色的系统警告顶替=输入静默丢弃**；取证要点=llm/streamChat 10:36:58 提交记录与转录 L304→L307（系统警告）→L308（恢复回复等拍板）之间的用户消息缺失；待拍现场截图）。下周二 10/6 结算本周，兑换码一周内须核销。


















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
- [2026-10-02 20:11:54] 【冻结霜化二版「原神奶白霜」已落地（2026-10-02，承接同日 19:32 返修链条目；AI 截图自检四轮 v2→v4 定稿，编译 0 错 0 警，未提交待用户目检）】参考=用户提供原神实机冻结截图（.codely/clipboard/clipboard-1790941782435.png，遗迹守卫）；像素实测定方向：原神冻结=强去饱和(饱和度中位0.20)+整体提亮(明度中位0.76)+灰蓝白霜化(机体均值RGB≈(145,164,175))+底部冰雾(淡蓝(180,211,242) 色相210°)。FrozenFrost.cginc 重构为五层：①重映射=原色强去饱和0.8+提亮0.3 再与色板五五混合+**明度 gamma 上抬 pow(saturate(lum*1.5),0.65)**（深色衣物也结白霜——线性映射下凯亚深蓝衣仍读深灰蓝的根因修复）；①b白霜斑0.55（中频噪声雾凇）；②边缘霜光加强1.6+半透冰体不变；②c冰雾新增（脚部淡蓝雾、高度二次衰减、噪声漂移、**alpha 溢出轮廓=缭绕感**、雾带下锚外放1/4雾高）；③晶点不变。暗部霜色改 (0.58,0.68,0.76)（旧深冰蓝(0.42,0.63,0.85)饱和度0.51太高是「蓝冰非白霜」根因）。新参数全部 Inspector 可调、FrozenSpriteTest.mat 已同步。

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

