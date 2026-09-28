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



- [2026-09-26 20:19:27] 【技能参数 baseType 结算层必须消费，勿只看展示拼接】（2026-09-23 用户纠偏原话「你弄错了含义！配置文件里的10，还要看基于类型。拼接后为10%移速」）：MoveDistance=10 标注 BasedOnMoveSpeed=**10%×移速换算**（安柏 50→5 格），非直读 10 格——我在 B-S1b 直读数值被纠偏。**Why**：value+baseType 是「数值+解释基准」二元组，展示层（SkillDescriptionBuilder 拼「10%移速」）与结算层（换算）必须同语义，只复刻展示而忽略基准即错。**How to apply**：baseType 语义对照（BasedOn*=按属性换算/Fixed=直读/Percent=语境百分比）与结算出口已沉淀 **gic-new-unit skill「customParams 的 baseType 语义」节**；实现技能数值必须核 docs/units 参数表基准列。同轮教训=**实现瞄准/方向/范围类逻辑前先读技能描述原文，勿按引擎能力（8 向步进）默认**（技能全员「十字方向其一」，首版误米字）。

- [2026-09-26 00:42:36] 【update_memory 精确匹配转录陷阱（2026-09-26 记忆整理实证）】转录 CODELY.md 既有条目文本做 update_memory 精确匹配时有两类 AI 无法自觉的错：①CJK 近形字误读——文件「效应/易伤」被我反复读写成「效果/易损」，肉眼复查多少遍都会自动纠错；②引号字形——弯引号 U+201C/201D 与直引号 U+0022 在条目间混用，目测无法分辨。症状=update_memory 报 Text not found 但看不出差异。**How to apply:** 批量删改记忆条目前，先把 oldText 落盘临时文件（write_file），用 PS 与 CODELY.md 逐字符比对（报分歧点码位：[int]$c[$i] vs [int]$f[$k]）确认 FULL FOUND 后再原样提交；rg -f 模式文件里含 + 的文本按正则解析会假阴性，验证一律 -F 固定字符串。
- [2026-09-26 22:12:37] 【TJGenerators MCP 上传/异步任务实操两坑（2026-09-26 实证）】①file_upload 预签名 PUT 偶发 403 AccessDenied（EC 0003-00000015/DetailErrCode 14006）——首张票据带/不带 Content-Type、禁 Expect 全被拒且时钟同步正常；**重调 file_upload 取新票立即 200，勿深调试 curl 参数**。②MCP 异步任务宿主并未自动拉起轮询（task_output 查 MCP task_id 报 Task not found），需把返回的 poll_command_powershell 落盘纯 ASCII .ps1 → run_shell_command 后台跑 → 退出后 check_task 一次拿结果（exit≠成败）。③任务产物 curl 下载偶发截断（-sf --retry 3 重下 + PIL load 校验完整性）。
- [2026-09-26 22:12:37] 【Tuanjie 1.9.3 TextureImporter 帧动画 sheet 切片 API 与标准 2022 分叉（2026-09-26 实证）】无 Grid 切片 API（spriteSheetType/spriteGridSize 不存在）、TextureImporterSettings 无 sprite 字段、无 ReadSettings(out)（只有 ReadTextureSettings(dest)）；**正解=ti.textureType=Sprite + ti.spriteImportMode=Multiple + ti.spritesheet=SpriteMetaData[] 手工填帧**：rect 原点=左下（图像顶行 r=0 映射 y=(总行数-1-r)×格高，勿按顶左原点写）、alignment 字段是 int（Center=0；把 SpriteAlignment 枚举赋它报 CS0266）、pivot(0.5,0.5)、spritePixelsPerUnit=100。取帧=AssetDatabase.LoadAllAssetsAtPath().OfType&lt;Sprite&gt;() 按名尾数字排序（_0.._15 字典序乱序）。收口时补录 docs/14。
- [2026-09-27 00:31:27] 【日辉多参考=画风锁第一张（2026-09-26/27 安柏七圣召唤画风重绘五版实证）】迭代链：v1=amber_glide+风格图双参考（人物像、画风被厚涂锚死）→v2=风格图唯一参考（画风对、人物跑偏）→v3/v4=人物锚「反复强化」循环（**画风纹丝不动——机制确认：日辉多参考的画风跟随第一张参考图，「内容跟A、画风跟B」执行不了，强化循环不收敛**）→v5=顺序对调（风格图第一+v1 第二，待目检）。风格锚点=扁平矢量/赛璐璐平涂硬边/细同色系描边/贴纸高光/糖果色（参考图=.codely/clipboard/clipboard-1790432863698.png，群像拼贴）；素材 v1~v5+对比图全在 .codely-cli/tmp/paperdoll_7star_style/。识图粗检再误报（v1 粗检称画风符合被目检否决，粗检不可信实证+2）。对策排序：换模型双参考试（qwen/混元，未实测）> 顺序对调 > v2 底+用户指认具体部位做定向局部重绘（唯一两轴可控路）。连锁：静态图若换画风定稿，32 帧动画需同画风重生成（或临时清 立牌动画帧 回静态）。
- [2026-09-27 22:16:03] 【美术交付禁程序拼合，必须纯 AI 整图】用户拍板（2026-09-27 原话「不要自己拼，让AI重新生成」）：游戏美术素材交付必须是纯 AI 生成的整图，**不得用「抠 AI 石子+羽化混边+程序随机摆位合成」类拼合产物交付**——即便碎片全部来自 AI 图、即便底色基调完全一致也要重画。与 2026-09-27 弧光贴图「不要程序生成，让AI生成图片」一脉相承（第二次实证，已从单案升格为通则）。**How to apply**：变体差异化/重排布/局部调整一律回生成工具重画（调 prompt 差异轴：体量/数量/形态）；程序后处理仅限像素级净化类（盘外清零/裁剪到内容框/降分辨率——既有已认可先例），不得改变图面内容构成。

### Project
















- [2026-09-12 14:24:31] Assets 清理定案（2026-08-16）：Material/Materials、Resources/Materials 三目录 8 个材质误删已 git 恢复，**勿再清理**（UIBlur/WishSmoke 在用，其余存疑也保留）；已删定案：Assets/Editor 空目录、Scenes/TestScene.scene、StreamingAssets/mc.mp4、Assets 根 6 个 CUsers* 垃圾文件。故意不做：NewAudioMixer、Art/Wish 与 WishArt 改名（Addressables 地址约定）；保留 GlowTest.scene+CardGlowTest.cs、兹白测试皮肤 zibai_skin_solid.png（UnitConfig 兹白 cards[1]，用户拍板不删勿当 bug 修）。**How to apply:** 不再动这批资产。










































































- [2026-09-26 20:20:15] 【搜索/文本判定铁律】search_file_content/glob/list_directory 受 .codelyignore+.gitignore 双层 ignore（Assets 的 png/prefab/asset/mat/wav、Mirror/kcp2k/Plugins 整目录、.codely-cli/.codely/Library/Temp/Logs/Export/Maps 等）——被任一层命中即**静默零命中/无名**，零命中≠不存在：查引用/资产内容/目录清单/skill 文件一律原生 `rg --no-ignore` 或 Get-ChildItem；喂多模态的 Assets 图片先复制到 .codely-cli/tmp；guid 判定一律 AssetPathToGUID/unity_asset get_info（meta guid 可能密文，文本反查假阴性，docs/14 §9）；.unity 与本地化表中文为 \uXXXX 转义，rg 中文直搜必零命中（搜转义形式或先解码，docs/20 §1.3；prefab 中文序列化字段名=大写 \uXXXX 转义、小写形式零命中）；.codely-cli/ 与 TextMesh Pro/ gitignored（skill/HANDOFF 勿提交；TMP 材质改动不入库，重装前备份）。细节 docs/14 §6.2。



- [2026-09-19 01:57:26] 手势输入层 P1-P4 已全量收官（2026-09-13；架构与 P4 勘定=docs/24 §7.11、速查=docs/17 手势层行、调研信源=.codely-cli/webrefs/gesture-input/）：PointerInputPump 归一→GestureHub（三门+first-accept-wins 仲裁，门可按面豁免）→识别器四件套（纯 C# 可离线断言）。**后续新交互面=实现 IGestureSurface 挂识别器（B6 格点点击=TapRecognizer 或 Drag OnSlop 的 OnTapCandidate；卡牌长按=LongPressRecognizer 首消费者）；DragRecognizer 要短点击必须显式传 emitShortTap:true（构造参默认 false，docs/14 §65）；阈值一律 GestureMetrics 勿散写。**




- [2026-09-23 00:04:38] [project] 用户会并行开多个 AI 会话在同一项目分工开发：**回合开始先读 .codely-cli/HANDOFF-并行AI协调.md**（各会话文件归属/状态/编辑器使用权，状态变化写回各自小节）。git status 里非我产生的改动/WIP 文件（尤其未跟踪新目录）属其他会话在制品——勿动勿清理勿"顺手修"，除非它把整个程序集编译堵死才做最小语法解锁并明确告知；编译错误可能来自其他会话 WIP，先归因再动手；refresh/构建等编辑器级操作动前先看协调板避免撞车，被取消后勿立即重试，问协调节奏。












- [2026-09-26 00:33:38] [project] 【地图音乐与星落湖遗留】蒙德野外曲池（day 9/night 7，QQ FLAC 原生直转）与战斗音乐轮换链已全量接线（清单=PositionConfig 配置资产）；战斗开局 6:00 落夜晚池（改原神式 6-19 边界待用户反馈）。**遗留：MapConfig 星落湖锚点仍占位 (70,28) 待取点器「锚点重定位」标定（标定前坐标是假的勿当实数据）——涉星落湖锚点的工作先核标定是否已完成。**



- [2026-09-19 01:57:27] [project] UnityInsight 索引系统档案（活锁 bug 已提交 Bug Hunter，证据包=.codely\有效bug活动\已提交\index-build-failure）：架构=CLI 侧 node 守护进程（unity-insight-cli.js serve --daemon）持有全部索引，**被动模式**——杀掉不自动重生、重生后须 Cowork GUI 发构建指令（编辑器 AI/ 菜单只有 Check Connections/Force Reload）；数据目录=<项目>\.codely-cli\UnityInsight\（构建中=index.db.tmp+WAL，ready 后写 index.current 指针）。活锁特征：first_build 磁盘写入 ~6 分钟后冻结、进程 4~5 核满负荷+RSS 狂涨至 7.5GB+零 I/O、index_building=true 永不翻转、GUI 无进度无报错；~\.codely-cli\crash-logs\exit-*.json（uptime<1s）=单实例锁握手记录属常态勿误判崩溃。处置=Stop-Process 杀 daemon+清 tmp 三件套。

- [2026-09-27 22:00:16] [project] Bug Hunter 提交活动：权威状态板=.codely\有效bug活动\00-新会话交接总览.md（涉提交/查已立包状态先读它）；活动规则与四件套流程=全局 skill codely-bughunter；**立包位置约定（9/27 用户拍板）=待交包一律放 .codely\有效bug活动\ 根，提交后移 已提交\、撤案移 不正确\（skill 已同步）**。**9/22 上周结算已了结：兑换码到账并已全部用完（分值待补记）；9/27 用户提交本周 3 件并自行归位已提交\**：①bughunter-context-157-percent（上下文显示 157%，真实 37%）②bughunter-video-cost-gate-threshold（视频生成>500 门槛逐单弹回，建议可配置阈值/开关）③bughunter-core-oom-deadair（core V8 堆 OOM 崩溃 code=3+回合死等 26 分钟+重试两连败；截图=弹窗现场含被吞回合 diff 残留+会话导出+JSONL；崩溃实例=最新版 2.1.1；core 崩溃不留 crash-logs，V8 原文只在 cowork [Core Error] 透传块，GC trace 首列 ms=进程 uptime，被吞消息可从 session JSONL 完整找回）。**待交两包（9/27 晚）**：①bughunter-credit-deduction-order（扣减顺序固定赠送>订阅>增值包，永久积分优先烧致订阅积分压队尾作废；建议可配置顺序或"先到期先扣"；截图/文档快照齐可直接交）②bughunter-resume-empty-memory（**会话续接失忆**：abort 迟滞 6 分钟的原会话后续接，新会话 forged_memory 快照 content=空串，对已验收的三条报障修复零记忆只能读盘反推；取证要点=forged_memory 空串在 584a5893 JSONL L9、initSession 14.5s 慢响应、当晚无 core 崩溃排除崩溃致失忆；**用户两次 /chat export 同场静默无产物**——大转录 13.2MB 可能是诱因；待拍失忆现场截图）。**待确认（用户两问未答）**：遗留三件（popup/CDN/积分误拒）网站侧是否已交+9/22 到账分值——决定两件新包本周交（5 件安全）还是留下周（若遗留已交则本周已满 6 件）。下周二 9/29 结算本周，兑换码一周内须核销。

















- [2026-09-19 01:57:27] [project] 加载页势力徽标动画模式已定稿（2026-09-15 蒙德落地）：连通域拆层=底图静止+动层运行时连续旋转（LoadingOverlayDriver「徽标分层表」，转速默认 -40°/s 可调；未配势力=整标缓转零破坏）；官方徽标非旋转对称处（六叶=镜像三对 53°/74° 交替的纯静态设计）做循环动画须均匀 60° 重排（用户拍板「不用像素级对齐，大致即可」——帧循环必有接缝，运行时连续旋转优于帧序列）。工具链=.codely-cli/tmp/mond-anim/（split_v3.js/rebuild_blades.js/preview.js）。**后续势力徽标动画沿用此模式。**


- [2026-09-27 01:56:24] 【AI 生成 sprite-sheet 实证结论】generate_sprite_animation 两次生成均未过像素验收（循环接缝 IoU 仅 0.603、静止区 7% 像素逐帧抖动、叶片形变）——**扩散模型无法同时满足「外框像素级静止+部件精确旋转+无缝循环」三条件**。**How to apply:** 「部件动而外框静」类动画需求直接走拆层/合成路线（见徽标分层条目），勿再尝试 AI 逐帧生成（静止参照类）；AI 生成适用于无静止参照的全新动效——2026-09-26 全身动实证（安柏飞行循环 16 帧，frontier_flare+参考图）：透明底原生/格边零污染/bbox ±3px，唯接缝 1.47×+尾部弱化+256²低清。**2026-09-27 再修订：战斗立牌 idle 动画已被视频路线取代**（绿幕 mp4+运行时 ChromaKey，回绕差分 0.72× 优于任何 sprite-sheet，显存恒定——见「B-S3 立牌动作段·视频路线」条+gic-paperdoll skill 立牌循环动画节）；**为战斗立牌生成动画勿再走 sprite-sheet**，本条仅作非立牌场景（UI 小动效/无视频消费方）备选；部件动而外框静仍走拆层。


















- [2026-09-19 01:58:22] 【派蒙语音现状与路由】游戏侧设计（三模式：关闭/本地侧车 127.0.0.1:9880/云端 MiniMax 自填 key；零进包；口型同步不做；覆盖=对话+抽卡反应+兜底全念、/指令回执不念）=docs/19 §6.5.11，P1a 已交付用户验证通过；**游戏外全档（模型谱系/训练/评测/侧车运维/yaml 绝对路径铁律/训练纪律三不/待办）=docs/27，语音话题先读它**。现状：用户拍板「先采用 A 组零训练优化，停止训练」（v4fullc e2 检查点保留，重启=train_pm_v4.py --skip-prep，A 组四件套全交付）；**等用户耳检 compare\paimon_compare_{reftable,grid,nbest}.wav 三份拍板**：情绪参考表哪些档采纳（游戏侧接线 paimon\refs_table.json）、n-best 守卫是否产品化；B 组（s1 补训/rank 升档）备选。侧车 9880 现挂 v4full e8 真权重、start_paimon_tts.bat 拉的是 v2 yaml（玩家默认=v2，勿混）。发布期议题=派蒙语音包可选组件安装器（4-6GB）或云 key，届时再拍。

- [2026-09-23 00:04:23] 【B5 判定体系已拍板】放弃队形→重叠格心+两态模型（执行阶段收拢重叠=特效锚格心、选择阶段自动散开复用展开布局）；格=交互粒度、**受击体=立牌真实大小圆柱**（底座圆盘可视化）与格脱钩；移动中单位连续插值位置可被中途命中（所见即所得）；接触判定与效果作用域解耦；纯瞬发仍按片初快照。规则本体已落档 docs/04/05/18+提交 eab2a20，实现要点=active/22 §11；B5 核心已落地（2026-09-19：圆柱受击体+投射物时间轴连续命中；圆柱直径调=BattleMetrics.UnitCylinderDiameter 一处）；圆柱容错直径、穿透模型待拍；剩余表现件（箭矢素材/天降视觉/震屏闪白/池化/立牌美术升级）挂起待拍板。**战斗表现层/判定任务一律按新体系实现，勿再按旧队形/格判定区方案写代码。**


- [2026-09-26 20:07:24] 【战斗 HUD v1 定案】设计稿=docs/designs/battle-hud-v1.html、拍板=docs/18 决策六+active/22 §13、操作流程=gic-battle-hud skill。布局=MOBA 范式（移动左下/爆发右下/战技左弧+延奏上弧/右上取消钮，布局参数全 [SerializeField] 中文命名）；立牌=饥荒式斜插卡片（顶部远离相机后仰，后仰角=俯角 55° 时面正对视线完全消压扁，BattlePlayer [SerializeField] 立牌后倾角可调）；技能详情面板=SkillDetailPanel.prefab 复用。**已被后续条目取代/推进（勿按本条旧描述实现）**：单位选择交互→C1 交互改版（任意单位可选中查看）；技能数据链→技能配置独立化（skills=SkillConfig 引用列表）；瞄准→点击式+拖动式 B4 轮盘均已落地（见各条目）。








- [2026-09-23 00:04:32] [project] 【战斗表现件三件套纪律】后续新表现件一律走 BattlePalette（配色收口，改全局战斗配色=BattlePalette.asset）/BattleViewFactory（世界 quad+Unlit 材质+世界 TMP 唯一出口，材质调用方持有+OnDestroy 释放）/BattleViewTween（补间收口，末帧保证 t=1），勿再手搓 quad/色值字面量/while 循环；TextMesh 全退役→世界 TMP（细节=docs/14 §63⑤+gic-battle-hud skill）。**四技能键全同构**（用户拍板「技能按钮应当统一，移动是特殊的技能」：数据驱动 skills[Move]，图标/名称随 normalMoveType 步行/飞行/两栖，移动专位左下）；HUD prefab 化已于 2026-09-22 收口。




- [2026-09-19 01:58:22] [project] SkillDetailView 点外关闭竞态已修（战斗实例设 `点外关闭=false`，点外收面板由 OnBoardTap 承接；教训=同一交互目标被两个系统响应必须一方显式让位，勿依赖帧内执行顺序——全案 docs/14 §64b）。**背包场景同款竞态（点源图标面板闪动重开）仍存在未修——用户未报障勿主动动。**

- [2026-09-23 00:04:32] [project] BattleHud 表驱动四键（SkillButtonDef+RegisterSkillButton 加键=加一行；AimMode 枚举已删）；2026-09-22 起结构 prefab 化（真源=Resources/Prefabs/Battle/BattleHud.prefab，契约=画布下 Slots/{key} 槽内首子级=控件）。**文件地图/加键 checklist/勿当 bug 修清单/活体取证时序全在 gic-battle-hud skill，战斗 HUD 话题先读它。**

- [2026-09-23 00:04:49] 【搜索铁律补遗（2026-09-20 复验）】search_file_content 的 glob=锚定于搜索根的 gitignore 式语义：含 / 的模式须从搜索根写起（从工作区根搜 Assets 下 UI 目录须 **/UI/**/*.cs）；glob 零命中先加 **/ 前缀或换 path 参数复核再下结论，勿直接判工具 bug。全项目 .cs 文件头模板 using 已清理（291 文件 867 条，2026-09-19，编译逐轮 0 错）——`using GIC.X` 逐文件检索可作依赖方向审计依据；**例外=17 个豁免文件（玩家侧 #if×16+CardGlowOverlay 混合换行）仍带模板头，审计到它们须核类型实际使用**。清理工具链四坑=docs/14 §66。






- [2026-09-27 14:15:45] 【战斗表现纸片人方案与素材路线定案（2026-09-20 用户拍板）】GI 动画提取判死后战斗表现=纸片人方案；GI 七圣卡面 Spine 素材路线被否决（卡面人物只有半身/坐姿场景图不完整）→纸片人素材走 AI 生成（安柏滑翔立牌终版=提交 ba3d847：v4 基版+AI 局部擦除中上多余第三翼，用户拍板局部擦除优于全新生成、v5 退役；站姿 v2 已作废（2026-09-27 用户拍板「作废，不要管它」）勿再引用）。生成工作流+立牌设计规则+局部擦除配方=gic-paperdoll skill，后续角色直接复用。Spine 技术管线已验证保留（skel 4.0 解析器+AtlasDump harness，坑清单在工具链内；工具链=.codely-cli/tmp/paperdoll_amber_gcg/ + webrefs/spine-paperdoll/），将来做 GI 素材骨骼动画可复用。**How to apply:** 战斗单位表现素材走 AI 生成立绘；GI 提取素材只做图标/头像/卡面类完整资源。





- [2026-09-23 00:04:54] 【面板预热泵已提交（7902e0f，2026-09-21，用户验证通过）】UIManager.PrewarmLoop=等 Splash 就绪+30 帧开泵+根转场锁在途挂起/转场毕+60 帧再续；WishScreen 4K 立绘 Preload 提前至 Splash 期。**症状归因**：启动动画卡顿/进厅首开异常/跳过 Splash 失灵先查 docs/17 §5b 泵行。**PreloadRegistry 统一注册表候选仍未拍板且未登记 docs/11**——后续时序重排/B6 时再提请拍板。










- [2026-09-26 00:40:09] 【ElementAttach/Reaction 批已提交（4368e79，2026-09-22）】附着/反应命令接线全链（ReactionEffect 效应=SkillHitResolver.Hit 唯一产出点，覆盖瞬发/箭雨/投射物；客户端=附着小图标/冻结上色/解冻退色/融化特效挂点；伤害数字带反应名前缀仅 Melt/Vaporize）+对账=BattleEffectCommandAudit（漏发 Warn，未知效应类型默认报警=B6 扩效应安全网）+蒸发反应（Vaporize=3，+50%×级别并入增伤乘区与融化易伤分区）。**症状归因**：附着/反应不显示、蒸发没反应名、冻结延迟先查这批。**遗留**：超时=Pass 确认未拍板维持现状；画面件（箭矢素材/天降视觉/震屏闪白/SFX）挂起；**本地化 CSV 同步物（webrefs/genshin-unpack/gichess/my/csv/）是过时快照，12000 段从未进 CSV（全段欠账，批量导出再议）**。


- [2026-09-23 00:05:03] 【B6a 元能系统已提交（ff5d60d，2026-09-22）】元能口径（用户逐条拍板，已落档 docs/18 决策七）：移动 +10（被挡也算）/战技首次命中 +10（同片按目标去重，多次命中不叠加）/爆发消耗=技能 EnergyCost（SkillParamKey 9，门槛=消耗值非上限，勿硬编码）/上限=UnitConfig baseEnergy。实现=BattleSimState.ApplyEnergy+EnergyEffect→StatChange（metadata=StatKindEnergy）+SkillExecutor 通用能量门槛（EnergyCost>0 先查后扣，不足行动落空+Log）+HUD 爆发键 HasEnergyForSkill 置灰（快照权威刷新）；对账登记 EnergyEffect→StatChange；能量环视觉=B6d。**症状归因**：爆发放不出/能量不涨先查 SkillExecutor 门槛 Log 与 SkillParamKey 9 配置。

- [2026-09-26 00:33:38] 【低级单位与 AI 玩家脑架构（B6b，2026-09-22 落地；AI 脑 v1 优先级制已被 09-25 v2 评分制取代、八向原语已随 v2 退役删除）】低级单位决策=LowUnitBrain（1~2 星），Host 在 TurnFlowController.BeginSelectPhase 直接生成 _minorUnitActions——**不走玩家上交通道、不占每回合行动配额**；AI 玩家脑只操控 3~5 星 IsMajorUnit；共享原语=BattleHeuristics。










- [2026-09-26 00:41:45] 【战斗首轮审查已闭环（fad5c87，2026-09-23，用户整体授权按推荐全修，勿再重跑/勿当未决议题）】R1 手牌滚动壳四层烘入 BattleHud.prefab hand 槽（RebuildHandCards 退役建壳=寻址+卡条目，壳寻址在 ResolveMiscWidgets）；Y1 TurnResolver 命令产出收口 EmitSliceCommands 单出口（治疗命令发射序后移≈+0.36s 纯视觉）；Y2 部署补手牌成员校验（防 B7 凭空部署）；Y3 BuildUnitState 单一出口（快照与 Summon 同构）；Y4 StatKindMora 如实化（客户端暂不消费）；Y5 PlayerIds 改 List 保序；Y6/Y7 删死助手死字段；Y9 箭矢占位色入 BattlePalette；Y10 六处 Resources.Load<UnitConfig> 收口 DI 容器；Y11 玩家数<2 AI 补位守卫；docs/20 §1.1 Inspector 序列化字段中文直名例外收口。**UIText.asset=全项目 UI 文本主表（Unity Localization StringTableCollection 容器），严禁删除**——判 Localization 表是否为空必须数 Shared Data 的 m_Id，勿看 collection 主资产体积/内容（容器天然是小体积；陷阱=docs/14 §72+gic-localization skill 陷阱 7）。新陷阱 docs/14 §71=运行时 AddComponent 件勿烘焙进 prefab/私有嵌套类 MonoBehaviour 只限纯运行时（跨域重载断链成 missing script 死槽）。B7 同构债（BattleHud 硬编码 _myPlayerId/TopBar 直持 Flow/组合根 BattleScreen 兼任）仍开口，以 docs/11 为准。症状归因：手牌不显示→ResolveMiscWidgets 壳寻址 Warn+prefab HandCards 四层；部署被拒→手牌/落点/摩拉三段校验 Log。

- [2026-09-26 00:40:09] 【时轮系统（SkillTimeline）已立项并 B-S1+B-S1b+两轮用户纠偏落地（2026-09-23）】拍板链=「开始。学习 m969/EGamePlay 自研取名时轮」→ 三指令（箭矢间隔 0.15s/移动是特殊技能也用时轮/完成安柏全技能排除命座变奏）→ 纠偏①「安柏的移动怎么会是3格？怎么会是米字方向？」→ 纠偏②「配置文件里的10，还要看基于类型。拼接后为10%移速」。决策=docs/18 决策八（含移动十字/移动距离换算两条独立拍板条）、设计=docs/active/28。核心：①SkillTimelineAsset（七轨 clip+aimMode+totalTime）挂 SkillData.timeline（null=兜底），分工铁律=时间规格归时轮/数值归 SkillParamKey；②前摇=ProjectileEffect 发射偏移（首窗插值修正）+per-skill 投射物规格；③命令=launchMs+SkillCast+合并键含 launchMs；④移动技能化=移动同产 SkillCast（AddMoveCast）、**步数上限=MoveDistance 按基准换算 10%×移速**（SkillData.ResolveMoveDistance 单出口：BasedOnMoveSpeed=百分比×移速/Fixed=直读/缺省 10%；安柏 50→5 格、凯亚等 30→3 格；UnitState.moveSpeed 快照新字段 HUD/Host 同源、移速含 Buff）、**瞄准=十字四向**（全员描述「选择十字方向其一」，首版误米字）、Amber_FlyingChampion 时轮（totalTime=0 动态）；⑤延奏实装=安柏延奏（全图我方含自身、HUD 延奏我方格/契约敌方格、叠层+时长累加经 UnitStats StatModifier、协奏元能蒙德/自身+10 触发 Henka 框架位/非蒙德+20、数值单源=技能参数经 ApplyBuffEffect.BuffValue/StackLimit/DurationTurns）——技能类后随 B-1 原子库退役改纯配置（见 B-2c 条目）；资产=Resources/Configs/SkillTimelines/ 三件。**遗留**：B-S3 素材路线待拍板→客户端表现轨消费（SkillCast→时轮资产播 cue）接线；资源点系统（飞行采集依赖）；Host 方向校验/方向模式数据驱动化（docs/11；AI 斜向纪律已随 AI v2 销案）。EGamePlay 源码在 webrefs/skill-timeline-system/EGamePlay_src/。





- [2026-09-23 21:45:44] 【角色文档技能描述双版本格式（2026-09-23 起生效）】docs/units 各技能一律=**玩家版描述模板**（进本地化表）+**开发版（实现规格）**（瞄准/时间轴前后摇/投射物速度体积/判定口径/多段语义/元能消耗/音效事件；值必须标三口径之一：已实现值/「设计值」（时间轴系统落地目标）/「未实现」）；安柏.md 六技能已全部改版且随 B-S1b 实装更新现状（技能类注册数=4：安柏战技/箭雨/延奏百发百中/凯亚霜袭；安柏 #1 移动已技能化（无技能类——ActionType.Move 走 MoveExecutor+时轮 SkillCast）、#5 变奏/#6 命座仍 UnimplementedSkill 占位）；开发版规格**不进 SkillParamKey/本地化同步链**（已固化 _模板与字段说明.md「技能描述双版本」节 + gic-new-unit skill 防误同步条目）。新角色文档与改技能描述时按此双版本写，勿退回单版本。

- [2026-09-26 20:07:20] 【官方动画路线终局+AnimeStudio issue #124 回帖/PR 待拍板（2026-09-26 合并两旧条目）】路线终局（2026-09-23 深夜用户拍板原话「放弃自研管线，效果与期望许多处不一致」）：战斗表现维持纸片人（已落地不受影响），GI 动画提取不再为表现层候选、自研 muscle 管线不重启；教训=数据级验证与视觉判定分离（docs/14 §38b）——RootT.y↔m_ValueArrayDelta 逐位互证/FK 头高/摆幅>10° 只证明解码与求值数学正确，不保证目检达标。issue #124 追评口径（数据级事实、不 claim 视觉验证）：①我们 issue 归因写错需追评认错（m_ClipBindingConstant 4.3+ 正常读）；②humanoid muscle 无骨路径落 else 丢弃属实；③GI 双层架构（身体动画在共享 clip Ani_Avatar_<BodyType>_*）=回帖最大增量；④「几个月前还能导出身体骨」已三点代码级证伪（根快照 ee88e924 全树零 muscle 文件+301 全史零实现+现行 master 同构）；FBX 独立审计=Odette 五 take 各 138 物理装饰骨零身体骨（1242=138×9 曲线口径、1387=ACL 口径 138×10+7，勿混）。DBACL streamer=IntPtr.Zero bug 上游 master 仍在（AnimeStudio.Utility/ACL/ACL.cs:152），修法已验证（streamer=dbAligned+bulkOffset），可单开 issue/PR。英文追评草稿（4 点+PR 意向）已备——**是否提交回帖+是否开 PR 待用户拍板**；技术存档与证据链=webrefs/gi-animation-extraction/README（终局节+代差表，完整配方可重跑）。





- [2026-09-23 22:38:38] 【B-S2 时轮编辑器+技能独立化已提交推送（e9e34b6，2026-09-23，目检通过）——时轮系统 B-S1/B-S1b/B-S2 全闭环】会话 L 两次推送：d9e31d2（B-S1 判定侧+B-S1b 安柏全技能+移动技能化）+ e9e34b6（B-S2 编辑器+技能独立化）。B-S2=`Editor/Tool/SkillTimelineEditor.cs`（Tools/TG/时轮编辑器，UI Toolkit+ConfigEditorUITK）：七轨时间轴+标尺+clip 拖拽（0.05s 吸附/右缘调长）+按轨道属性面板+校验行+接线 UnitConfig 按钮+缩放滑条；编辑=直写对象+Undo+显式保存。**Tuanjie UITK 坑**：EnumField.newValue 装箱 System.Enum——转 int 用 Convert.ToInt32（CS0030）。遗留（docs/11）：B-S3 素材路线待拍板（SFX/箭矢/AttackUp 图标：AI 生成 vs 官方提取 vs 效果库）→拍板后客户端表现轨消费（SkillCast→时轮资产播 cue）接线；B-S2 增强（灰盒预览+scrub/SkillName 搜索下拉）；资源点系统（飞行采集）；AI 斜向移动纪律。

- [2026-09-23 22:38:19] 【技能配置独立化完成并已提交推送（e9e34b6，2026-09-23，目检通过）】SkillConfig 独立资产化：每技能一个 SO（`Resources/Configs/Skills/{SkillName 枚举名}.asset`，data=SkillData：skillID/icon/skillType/customParams/timeline）；UnitData.skills=List&lt;SkillConfig&gt; 引用列表（顺序=skillIndex 语义勿重排、空槽运行时告警）；Common_Walk 8 角色共享单资产（两变体运行时等价合并为带参数版）；消费点 9 文件全走 `.data` 解引用；UnitData 编辑窗选中技能=编辑资产本体。gic-new-unit skill 同步流程已重写（建资产+引用）。**此后加/改技能一律走独立资产，勿再往 UnitConfig 塞内嵌 SkillData**；改技能参数=改 Skills/{name}.asset（共享资产改一处全角色生效）。



- [2026-09-24 00:54:47] 【瞄准高亮推荐分色已落地（2026-09-23 分色拍板，09-24 视觉定稿+烘焙修正）】三态：可选且推荐（青蓝）/可选但不推荐（红）/不可选无提示；**色块视觉终版=青芯+内嵌黑边 quad，推荐色=原神 Hydro 系青蓝 #4CC2F1 (0.30,0.76,0.95,0.8)**（用户拍板"换个颜色，但不是白色和金色，按照原神风格"——迭代链白→金→白+黑边→青蓝；原神对话选项选中态同色系+尘歌壶"可放=蓝框/不可放=红框"既有语义）；底图=BattleViewFactory.AimCellTexture 运行时生成 128px 白芯+12px 黑边环（黑边 rgb=0 乘 tint 恒黑故两色共用一张），CreateAimCellMaterial 挂 _BaseMap。推荐口径=直线技能该方向能命中敌人（技能实例 WouldHitEnemyInDirection——BaseSkill 虚方法默认整线扫描，安柏战技覆写=投射物圆柱接触距离空间预判、凯亚霜袭覆写=DamageDistance 段、箭雨用默认；**新直线技能必须覆写保预判与结算同形态**）；移动=CanMoveEnterPreview 步进预判镜像 MovementResolver 判定链（被挡后该向余下格全不推荐）；单位指向与部署 v1 全推荐。**色值已烘入 BattlePalette.asset 真源**——踩坑教训=长跑编辑器里改脚本默认值对已加载资产实例无效（冻结陷阱=docs/14 §74），**后续调色改资产勿只改脚本**。半透明=CreateTransparentUnlitMaterial（URP Unlit 透明配方，不设 _Surface 时 alpha 被强制 1）。含尸体算命中=Host 同语义（是否排除尸体待反馈）。落档 docs/18 决策六（含 09-24 终版迭代链）+gic-battle-hud skill+active/22 §13。报"瞄准颜色不对/没有黑边/水面红了"先查这批。
- [2026-09-26 20:07:48] 【伤害数字原神式屏幕空间化已落地（2026-09-24 拍板「按照原神的做法」+目检调参，随 46d1022 已提交）】Battle/View/BattleDamageNumbers.cs：独立 Screen Space - Overlay 画布 sortingOrder 39（HUD 40 之下，数字不参与 3D 深度测试；**勿加 GraphicRaycaster——Overlay 射线器会挡 HUD 按钮**）+对象池；曲线=首帧爆大（初始停留比 2.5，目检后由 1.5 调大）→0.35s easeOutCubic 收缩，伤害→尺寸对数映射 f=Clamp(0.8+0.22·log10(max(1,|dmg|)), 0.8, 1.65)，段尾渐隐+上浮+随机偏移散布（±0.42 X / 0.3~0.65 Y / ±0.15 Z，目检后拍板值）；文本=治疗「+N」/伤害「N」无负号/反应「蒸发 N」；描边色入 BattlePalette（「伤害数字描边色」）。症状归因：数字被地形遮挡=旧世界 TMP 实现残留；数字先手从小放大=旧弹跳残留；尺寸恒定=映射未生效；大数字消失太快=时长映射未生效先查 magnitude 传值。**2026-09-26 追加**：①描边迁移 SDF 原生 _OutlineWidth 0.08（终值，原 0.15；条目独立材质实例 Entry.OutlineMat+OnDestroy 释放——UGUI Outline 固定像素式缩放视口下不可见，全项目清零=docs/14 §84）；②**停留时长映射（拍板「数值越大，停留时间越长」）**：total=Clamp(时长基准 0.95+时长对数系数 0.25×log10(max(1,|量值|)), 时长下限 0.95, 时长上限 2.2)——与尺寸映射同构（对数），伤害×10 多停 0.25s 封顶 2.2s；收缩段 0.35s/淡出比例 62% 不变=延长停留与渐隐段；旧固定「总时长」参数退役；参数=BattleDamageNumbers Inspector「伤害→停留时长映射」段（运行时组件改脚本默认即生效，无 prefab 冻结）。③**屏幕边缘夹取（拍板「屏幕里没有该伤害数字时，数字完整显示在屏幕边缘=战斗方向提醒」）**：投影点逐帧按文本实测半尺寸（TMP textBounds×弹跳缩放×画布缩放+屏幕边缘留白 12px）夹到视口内——屏外（相机平移/拉远）与贴边数字在最近边缘完整可见；相机背面投影（z<0）屏幕坐标镜像，先绕屏心翻回正确方位再夹取（勿当方向 bug 修）。




- [2026-09-26 00:38:45] 【头顶条原神式屏幕空间化已收官（2026-09-25 用户目检通过，提交 3404df3 已推送——与伤害数字批 46d1022 两批闭环）】新文件 Battle/View/BattleOverheadBars.cs：独立 Overlay 画布 sortingOrder 38（伤害数字 39 之下、HUD 40 之下），逐帧投影 UnitView.OverheadBarAnchor（含 55° 后仰偏移）；血条（**填充=队伍色**（09-25 拍板与底座同色，Palette 血条我方绿/敌方红不再被头顶条消费）+圆角药丸黑边框——底图=真实资产 Resources/UI/Battle/BarBg.png（黑边框 5px 环+白芯）/BarFill.png（纯白圆角），用户拍板「不要程序化生成」，美术出图直接覆盖文件；**填充必须内缩边框厚度（条高×5/24），同尺寸会整盖住底图边框环致黑边不可见**；UGUI Filled 必须赋 sprite 才裁切，空 sprite 时 fillAmount 被忽略条恒满=docs/14 §75）+原神分隔线默认 4 段）上→元能条（白色（元能蓝→Palette.元能条色改名+默认白）+分隔量子=10 与 B6a 获取粒度一致、段数上限 10）下→附着图标随血条左缘；无 GraphicRaycaster；元能条尸体隐藏。UnitView 公开 Hp/MaxHp/AllyHpBar/AttachedElement/OverheadBarAnchor getter；名字/Buff 徽章仍挂立牌倾斜组（「都应当斜」拍板未推翻这两条）；CreateView 注册+ClearViews 清 All（Summon 路径同走 CreateView 自动覆盖）。症状归因：头顶看不到血条=OverheadBars 层未建（CreateView 未注册）；元能条不显示=EnergyMax≤0 或尸体；条不贴头顶=OverheadBarAnchor。

- [2026-09-26 00:38:45] 【战斗二轮审查修复批+元能结算序返修已闭环（2026-09-25，f2c171a 已推送，勿再重跑）】R1 元能获取带来源类别+MergeEnergyEffects 键=目标+类别（五产出点全接）；S3 部署碰撞拍板「根据碰撞决定」（IsDeployCellValid：体积≤3 取最高级+阻挡互不阻挡+地形，HUD 部署分色镜像——部署 v1 全推荐作废）；S1 时轮编辑器接线 SetDirty 改被改 SkillConfig 资产本体；S2 编辑器同 kind 判定 clip>1 警告；S4 空 unitId Pass 分桶前剔除；S5 登记 docs/11 方向纪律④。**元能结算序返修拍板「先扣除，再加」**：ApplyEffects 按正负分桶先全部消耗后全部获取（跨行动同段也覆盖）+MergeEnergyEffects 发射序同步先扣后加——钳位资源增量不可交换 clamp(x+g)−c≠clamp(x−c)+g（docs/14 §77；同族未决=HP 伤害/治疗同段顺序，治疗技能落地时定）。核验实证（勿再查/勿误判）：UnitConfig 34 单位零空槽、安柏时轮三件全接、fad5c87 九项零回归、Lisa 无战技=设计（VioletArc 类型=Move）、丘丘人 skills=[] 回落 3（docs/18 已勘误）。症状归因：「元能终值少一段获取」→ApplyEffects 两段序；「客户端顺序与 Host 不一致」→MergeEnergyEffects 发射序；后续任何钳位资源（HP/体力）同段正负并存按 §77 先扣后加。


- [2026-09-25 18:12:05] 【技能效果原子库 B-2c 凯亚/芭芭拉技能链已落地（2026-09-25，5c14003，编译 0 错 0 警，目检清单已交）——本条目承接并取代同日「B-1/B-2 已落地」旧条目】推送状态：**五提交全部已推送**（2026-09-25 会话末 cb4c48d..5c14003 直连推成——推送挂起已解除，HANDOFF 板会话 N 收官核验）。B-2c=五技能纯配置实装（效果原子批量实证、零新技能类）：凯亚延奏=OnCast[MoveSpeedUp(10/5/3)+协奏元能+触发变奏]、凯亚变奏=OnCast[治疗自身 50%攻]、芭芭拉延奏=OnCast[治疗被协者 15%maxHp]、芭芭拉变奏=OnCast[治疗我方全体 8%各自maxHp]、芭芭拉战技水之浅唱=OnHit[伤 10%施法者maxHp+水附着+治疗施法者半径1内我方 15%各自maxHp]+时轮 LineProjectile maxRange=5。代码增量：新 MoveSpeedUp Buff（BuffType=4+MoveSpeedBuff BaseFlat——移速含 Buff 直连移动距离换算链）；筛选器 AllAllies/CasterRadiusAllies（radiusKey 引参数表键防双源）；Damage 原子基准分流（BasedOnMaxHealth=施法者生命上限→FlatDamage 加法区）；治疗换算出口 ResolveHealAmount（BasedOnMaxHealth=被治疗者各自/BasedOnAttack=施法者——docs/11 裸 int 坑销案，护盾类仍开口）。**教训（自纠）**：ResolveCastTargets 重构时曾把蒙德筛选写成恒真（`||true`+丢条件）致协奏规则失效——大方法重构动筛选条件必须逐 case 核对。**语义选择（目检确认点）**：治疗基于最大生命=被治疗者各自 maxHp、伤害 BasedOnMaxHealth=施法者 maxHp——观感不符则一行换算改动。B-3 剩余（docs/11）=凛冽轮舞（召唤系待拍板）/闪耀奇迹（复苏语义待拍板）/OnVanish（随资源点批）/条件计数器（B8）/形状库（第 4 角色实锁）；漂移点=霜袭/水之浅唱判定距离收口 clip.maxRange、参数表留描述渲染。How to apply：加技能先试 effects 组合、新形制才扩 EffectCompiler；筛选器重构逐 case 核对。

- [2026-09-26 00:40:09] 【B6d 体力/摩拉经济闭环已收官（2026-09-25，c2bbcff 已推送，目检通过）】拍板=docs/18 决策十：①体力/摩拉=玩家持有物品牌（ItemName.Stamina/Mora，图标走 ItemConfig 单源）；②回合结束段发放 +5/+5（第 1 回合初始 60/200）；③消耗=移动/战技/爆发各 10（StaminaGate：星级≥3 才扣、1~2 星豁免、延奏/契约 0、不足落空同元能；口径单源=GetStaminaCost）；④**玩家资源命令 targetUnitId=玩家 ID——BattlePlayer.StatChange 必须先按 metadata 分流（Mora/Stamina→OnResourceDelta）再查 _views，否则被单位查表静默丢弃（StatChange 工厂 unitId 双语义陷阱）**；⑤左上角=**ItemCounterChip 公用组件**（"物品×数量"显示需求一律复用勿再手搓；myinfo 槽内 MoraChip/StaminaChip 两枚实例，寻址契约=槽内同名节点）；⑥enemyinfo 敌方信息块已移除（用户拍板不显示敌方资源；协议仍含双方数据，B8 再议）；⑦AI 玩家体力同链结算但脑不感知（枯竭期空过属预期）；⑧技能/移动键体力置灰（HasStaminaForSkill）。**手牌货币卡四轮定稿（用户拍板原话「获得卡：如果手牌已经有该卡，则加数量，如果没有，则加上这个卡。失去数量时，同理」——"注入"等特殊机制说法被否）**：HandCard 条目 count=一等属性（物品/角色=局内真源、货币=资源池镜像）；GainCard/LoseCard 公用对偶（有则加数量/无则加卡；减至零移除、不足 false 不动；使用/装备/掠夺消费端后续批接线）；摩拉/体力卡可编入备战卡组（maxPrepareCount=100/60；不可入组货币仅星辉/相遇之缘/纠缠之缘=NonDeckableCurrencyItems——决策七旧注勘误仅指缘/星辉）；货币牌数量=PlayerResourceState 池（堆>0 必有卡/空堆移除/再发放自动复活，SyncCurrencyEntry 收口）；开局统一送 200 摩拉+60 体力（编没编货币卡都送勿双发、货币条目卡组读取跳过、空卡组回退丘丘人×2）；客户端渲染=HandCard.count 真源（签名=卡 id 列表数量变化不重建、货币角标经 RefreshHandCurrencyCards 动态刷）；部署扣费 TrySpendMora→ApplyMoraDelta、回合发放直产命令同链。**时序陷阱（docs/14 §78）：[Autowired] 依赖的初始化永远放 Inject 之后——寻址/注入分离**。症状归因：体力不扣→StaminaGate Log；左上角无数字/图标→myinfo 槽寻址 Warn+InitItem 时序；摩拉部署不即时减→ApplyMoraDelta 事件链。

- [2026-09-26 00:41:02] 【AI 玩家 v2 评分制+对称测试军已落地（2026-09-25，61249c9 已推送）】拍板=docs/18 决策十一。AIDebugBrain v2=全候选评分制（攻击=CanCast/元能/体力三门槛+十字四向逐向预判+伤害/斩杀(80)/战技获能(12)评分；延奏=蒙德或自身+治疗缺口半量门槛/增益未满层溢价15/变奏链12/协奏元能4估值；移动=十字逼近+进射击线加分8+近敌接敌加分；Pass 兜底；评分常量收口类头部，调手感改常量）；LowUnitBrain=十字方向纪律收口；BattleHeuristics 扩容（CrossDirections 唯一方向域/PreviewLineTargets 镜像 EffectCompiler 两分支（LineProjectile=首停格含尸体截停/LineBurst=clip.maxRange 整线）/EstimatePerTargetDamage 按基准/IsMondstadtOrSelfUnit/FindAttackDirection/BestCrossApproachDirection；八向 DeltaToDirection/DeltaOf/FindLineSkillDirection/HasLivingEnemyAt 退役删除；FindSkillIndex 改遍历 unit.Skills 实例=与 SkillExecutor 同源索引）；BattleScreen 测试军=双方各 1 安柏/凯亚/芭芭拉/丘丘人（镜像位，丽莎移出）。方向纪律①④销案 docs/11（②Host 方向校验/③数据驱动化仍开口）；霜袭编译漂移与掠夺摩拉两项已随修法 A 批销案。症状归因：AI 斜向移动/爆发斜向空放应已消灭（旧症先查本批）；AI 周期性空过=体力枯竭期攒体力（60 初始+5/回合 vs 10/行动，第 7 回合起隔回合行动属预期非卡死）；延奏目标恒蒙德或自身=效果原子产出保证。

- [2026-09-26 20:07:35] 【战技获能两轮返修已落地（targetFilter 编译分支+命中时刻时序，随 AI v2 批 61249c9 已推送）】用户两轮报障：①「安柏战技命中敌方凯亚没加元能」②「使用了战技立刻获得元能」→拍板「应当是命中时才给」。第一轮根因（docs/14 §79）：EffectCompiler.CompileOnHit 从未消费 targetFilter=Caster 分支+迁移脚本把安柏双矢/凯亚霜袭获能原子 targetFilter 误配 0=Target（+10 元能发给了被命中的敌人）、芭芭拉水之浅唱整个获能原子漏配；隐藏坑=ApplyEffects 元能逐条累加 vs 命令层 MergeEnergyEffects 去重（两发箭矢状态 +20/命令 +10 背离）。修法=CompileOnHit 补 Caster 分支（受益者=施法者/行动者=B6a 口径）+三资产 targetFilter 改 1/补配 OnHit[EnergyGain Caster value 10]+SkillHitResolver 兜底原子同步+ApplyEffects 按 (目标,类别) 去重与命令口径恒等。第二轮根因（docs/14 §80）：B-S1 时轮只给 Damage/消散命令带 launchMs 时序，命中链其余产物（StatChange 元能/Heal）零时序——客户端同步应用=片头瞬跳；Host 的 ProjectileResolver 已算出精确 hitT 但没往下传。修法=命中时刻全链透传：hitT→SkillHitResolver.Hit/CompileOnHit/CompileAtom（hitSeconds 参数）→EnergyEffect/HealEffect.HitSeconds→元能/治疗命令 launchMs 复用为应用时刻（union 载荷，0=立即）→客户端 StatChange(元能)/Heal 分支 launchMs>0 走协程到点应用（PlayEnergyDeltaCoroutine）；同片多命中去重取最早；MergeHealEffects 保留 HitSeconds；0=立即语义保持（移动获能/协奏/消耗/回合发放/OnCast 治疗）。全案=docs/14 §79/§80；落档=docs/18 决策九返修条+决策七元能「命中时刻」条+docs/17 §7 返修行。How to apply：①新效果原子落地先核 targetFilter 全分支被编译器消费（枚举存在≠管线生效）；②迁移脚本批量生成原子逐个核对谁受益；③命令层有去重/合并口径时状态应用层必须同口径（症状指纹=命令显示量≠下回合快照量）；④命中类效果（获能/治疗/未来 OnVanish）新增命令映射必须携带命中时刻——Host 已算出的 hitT 勿半路丢弃；⑤union 命令字段跨类型复用须同步改 Header 注释（launchMs=发射延迟 vs 应用时刻）；症状指纹=某数值片头瞬跳而对应视觉事件（箭矢/移动）未发生→查该命令时序字段。「战技命中不涨元能/敌人被打了反而充能」先查 targetFilter 与 CompileOnHit 分支。


- [2026-09-26 00:41:02] 【霜袭漂移修法 A+掠夺摩拉原子已落地（2026-09-25，56e520d 已推送，docs/11 两项销案）】拍板=docs/18 决策九「B-3 首扩+修法 A」。①EffectCompiler.CompileJudgment 分流改「Burst 型 ∨ 配 LineBurst clip」都走整线迸发段编译——霜袭（Normal+LineBurst clip maxRange=2）回归设计=前方 2 格内敌人整线全中、瞬发无投射物视觉（白光条退役），预判/结算/HUD 瞄准三方对齐。②SkillEffectKind.MoraPlunder=7（首个资源类原子，护栏"不能组合表达才扩枚举"首触发）：OnHit 每命中一个敌人掠夺其**所属玩家**摩拉池→施法者玩家池；实际量=min(参数, 被掠夺方池)（**池空抢不到=实现取值，观感确认点**）；双 StatChange(Mora) 命令带命中时刻+对账登记（AppliedGain=0 零命令=非漏发）；霜袭资产 paramKey=MoraPlunder 75 防双源；B-2 编辑器 kind 7 显字段。How to apply：加资源类技能效果（掠夺/给予摩拉）=配 kind 7 原子零代码；契约类「不足掠夺钳 0」（docs/11 璃月条）复用同口径。


- [2026-09-26 00:41:02] 【三轮审查修复批已落地并提交推送（ce335f3，2026-09-26，勿把 C1/C2 当新发现重报）】**业界对比拍板：主干选型=回合制定式（Host 权威+JSON 命令流+ack 门控，对标炉石/chess.com 类服务器权威状态同步；lockstep 属 RTS/MOBA 勿混用）——勿再提议换架构/换 ECS**；同片双箭矢同目标双反应=快照并发设计取舍非 bug。用户拍板「开始。死亡不需要清理任何东西。登场无附着。」：S1 死亡清理**不做**（BurnBuff 烧尸体飘伤害数字=接受行为勿当 bug 修）；4A 登场无附着（UnitElement 初始 Physical）。修复：①C1 双行动防线（交互改版见 C1 条目）；②C2 敌我判定 TeamType 口径 9 处收口（预判链签名 casterPlayerId→casterTeam；**操控权与资源归属保留 playerId——CollectMajors/上交归属校验/资源 chip 等处 playerId 比较是正确语义勿再改**）；③S2 TriggerSkill 防环（_triggerDepth，MaxTriggerChainDepth=3）；④S3 即时行动补归属+星级校验；⑤S4 治疗合并键补命中毫秒；⑥S5 闪色恢复尾巴拆协程不 gate ack+治疗不再 FlashHit 闪红；⑦S6 InitSkills 空槽占位 UnimplementedSkill（配置序=实例序恒等）+CanCast 前移到元能/体力门槛前；⑧S7 StatBuff 基类（新文件 Buff/StatBuff.cs）；⑨S8 蒙德判定单出口=BattleHeuristics.IsMondstadtUnitName；⑩S9 DamagePipeline hooks 删除；⑪S10 全灭软停（协议 BattleOver=9+BattleOverMessage(winnerTeam)+CheckBattleOver；**顺序=先 OnPhaseChanged 再 HostSend 防 Tip 被覆盖**+HUD 胜负 Tip）；⑫Battle_Victory=12031/Battle_Defeat=12032 五语言。**桥脚本本地化写入坑：桥环境 StringTableCollection 类型不可达——走 SharedTableData+各 StringTable 资产直连（AddEntry/TableEntry.Value），LocaleIdentifier 非可空勿比 null**。症状归因：「伤害数字飘尸体」=拍板预期行为勿修；「队友被当敌人」=2v2 C2 已收口；「点了技能白扣体力」=CanCast 前移已修；全灭后回合停+胜负 Tip=S10 新行为。


- [2026-09-26 00:19:31] 【C1 交互改版落地并已提交（随审查修复批 ce335f3 推送，2026-09-26 用户拍板+验证通过）】新交互=**选中/瞄准全开放，拦截移到提交时轻提示**：①任意单位可选中（含敌人/低级/同格敌方——同格己方优先），技能盘+技能详情+进瞄准全开放（查看敌人攻击范围/延奏目标域）；②SubmitAim 提交防线——非己方单位 toast「Battle_NotYourUnit」、己方 1~2 星 toast「Battle_MinorUnit」（**2026-09-26 报障返修：通道=PopupManager.ShowToast 顶部滑入轻弹窗（PopupText 表键，Wish_NoPrimogem 同款）——首版 SetTip 提示条文字切换太隐晦用户实测看不见；UIText 的 Battle_Tip* 12033/12034 已删净，toast 键落 PopupText 表 Id 跟随其全局 id 体系**），拦截后**保持瞄准态**继续查看（取消钮退出），不上交+GICLog.Info；③配套：查看态（敌人/低级）技能键**不置灰**（IsSelectedControllable 单源判定——己方高级单位才有元能/体力门槛置灰）；ComputeAimCells Enso/Contract 改用**选中单位队伍**（sel.team——选中敌方延奏时目标域=敌方全体、契约=我方全体，查看视角正确）；FindUnitAt 的 majorsOnly 参数已删。**Host 侧 TurnFlowController.OnSubmitAction+HandleInstantSubmit 校验保留不变**（双保险，B7 LAN 绕 UI 也进不来）。Why：查看需求（直观看到攻击范围）与操控权限分离——选中≠操控；轻提示通道=toast 非提示条（用户实测反馈）。How to apply：报「点了没反应/按钮灰着进不了瞄准」类障先查 IsSelectedControllable 与置灰条件；报「轻弹窗看不见」查 PopupManager.Instance 常驻链与 PopupText 键；前日修复批记忆中「HUD FindUnitAt majorsOnly 选中只认高级单位」描述已过时以本条为准。
- [2026-09-26 20:07:51] 【战斗 HUD 瞄准待定制+完成选择按钮已落地（2026-09-26，编译 0 错 0 警，复检清单已交，已随 de0154b 提交推送）】拍板=docs/18 决策六「瞄准提交制」条+docs/active/22 §13 2026-09-26 增量。①完成选择按钮：BattleHud.prefab Slots/confirm（祈愿 Marketplace 同款 Resources/UI/Wish/UI/button.png，默认 (0.63,0.95) 290×75；AllLayoutKeys 登记；显隐=Update 轮询同 countdown）——点击语义：Aiming+金格待定→SubmitAim；否则 **Pass 完成本回合选择**（用户原话「无论自己是否选择了」）；**多人提前开演=各真人玩家各交一份行动（含 Pass）走既有 TryBeginResolve 收齐判定，零新协议**（AI 脑自动上交）；后交覆盖先交保留=确认后再瞄准再确认可改行动。②瞄准待定制：点可选格不再立即提交——**金色待定**（BattlePalette.瞄准已选色新字段已烘资产 (0.96,0.79,0.27,0.85)；_aimQuadByCell 换 sharedMaterial 勿用 renderer.material）；可点其它格变更（重复点同格=保持）、点空白=取消回选中态原行为；提交唯一入口=完成选择按钮（部署瞄准同制）；防线 toast 拦截后保持瞄准态与待定。本地化=新键 12033 Battle_ConfirmSelect（完成选择/完成選擇/Confirm/決定/Готово）+12010/12011/12014 五语言改写（顺带销掉 12010 旧"8方向/3步"过时描述）。症状归因：点格没提交=待定制非 bug；完成选择点了没反应先查 Selecting 阶段门与 confirm 槽寻址 Warn；手牌沉半张不回弹先查指针热区余量/隐藏态/BattleHud Inspector「手牌下沉」段参数。**追加（2026-09-26 同日四连拍板定稿）**：①「倒计时结束时应当相当于按下了完成选择按钮（统一复用链路）」→TurnFlowController 新事件 OnSelectTimerExpired(turn) 在超时 Pass 兜底填充前同步触发，HUD **无条件**复用 OnConfirmButtonClicked；②追问「为什么不完全统一」→收掉首版"无待定不经 HUD"保护特例；③**「确认行动后就应当定死了」→确认即定死**：HUD _actionConfirmed 成功上交后置位（按钮置灰/再按无操作/超时自动按下同款无操作=已确认行动天然保留）；防线 toast 拦截（SubmitAim 返 false）不定死；OnPhaseChanged(Selecting) 每回合复位；**Host OnSubmitAction 对已交玩家重复上交一律忽略（旧「后交覆盖先交」废除，B7 LAN 亦防）**；④**「完成选择按钮应当居中屏幕」**：confirm 槽 (0.5,0.95) 屏幕正中；**顶部终版节奏（再拍板「执行预览移右上方、倒计时移屏幕中间」）**：回合 (0.99)→confirm (0.95)→时钟 (0.91)→countdown (0.5,0.85 屏幕中间=queue 空出的正中位)→queue (0.81,0.85 右上方，内容右缘≈2288 与设置钮错行、与瞄准态取消钮 x≥2308 留隙)；**启用过自定义布局方案的存档会把槽钉回旧位，需重存方案或恢复默认**；⑤**「手牌默认下沉一半，鼠标接近才上移」**：UpdateHandHover 每帧轮询（热区=HandCards 矩形外扩上探 100/侧探 60），只写 HandCards.anchoredPosition 勿动槽锚点；沉量=升起态底缘+120×槽缩放动态测量（视口/槽位/缩放自适应）；首帧直接落沉态。⑥**倒计时观感（拍板「3倍+黑描边；<5秒红色脉动」；返修「未看出有描边」；两拍「调细」→0.15；冻结陷阱复发→烘 prefab 修复；「伤害数字也用」→同法迁移）**：fontSize 20.4→61.2 烘 prefab（槽 300×104）；**黑描边=SDF 着色器原生 _OutlineWidth 0.08 终值（迭代链 0.3→0.22→0.15→0.08）**（TopBar 运行时独立材质实例 _countdownOutlineMat，OnDestroy 释放）——**陷阱=docs/14 §84：TMP 文字描边一律 SDF 原生（UGUI Outline 固定像素式缩放视口下不可见+对角断缝）**；**§74 冻结陷阱复发教训（已补 §84 注记）：0.22/0.15 两改脚本默认值未生效（prefab 烘焙值恒 0.3，用户目检"没变化"）——改"挂 prefab 组件"的 [SerializeField] 默认值后必须 LoadPrefabContents+反射 SetValue+SaveAsPrefabAsset 烘 prefab（0.15 已烘）**；**伤害数字已同法迁移**（BattleDamageNumbers 运行时组件不受冻结影响；池条目 Entry.OutlineMat+OnDestroy 释放，宽度 0.15 同口径）；**祈愿卡池文字描边探针实证=zh-cn SDF 共享材质本体 0.15 黑（角色名）+zh-cn SDF 1 预设 0.08 白（称号），本就是 SDF 原生无需迁移**；<倒计时告急秒数(5)=伤害红+基准字号 Cos 呼吸脉动（±12%、1.5Hz，UpdateCountdownUrgency 于 Update 早退前调保还原）；调参=BattleHud Inspector「倒计时」段四中文参数。特例反复教训：设计冲突优先找"机制自洽"解（定死）而非堆补丁特例。


- [2026-09-26 20:07:40] 【拖动式瞄准 B4 已落地·轮盘化终版（2026-09-26，编译 0 错 0 警，复检清单已交，已随 bfd32a7 提交推送）】**五轮用户纠偏收敛**：①「不是拖出到格子上，而是和王者荣耀一样的释放技能方式」（=手势：拖向定方向，勿按指针落点吸附）②「一次选择1个格子，松手后不应立即完成选择」（=单格待定+松手不提交，整臂金色+松手立即释放方案被打回）③「技能上大圆盘+手指小圆盘、圆盘不可超出屏幕边缘、金格在屏幕外时屏幕丝滑移过去」④「大圆盘太小了，小圆盘不应该超出大圆盘范围，选中格子的精确性应当限制在大圆盘范围里」⑤「半透明阴影比圆环小一点」→ 误读为「缩小阴影」拍板（×0.92）被用户澄清打回（原话「这是不对的」——**实为显示瑕疵报障**：disc.png 实心盘可见缘只占纹理半宽 0.830（四周透明边距）、circle.png 描环线贴纹理外缘 0.998，同尺寸下阴影可见缘天然内缩 ~17%；修正=实心盘纹理（大圆盘阴影+小圆盘）×`实心盘贴图补偿` 1.202（=0.998/0.830）放大使可见缘贴齐描环线/名义半径、多出透明边距被描环盖住；**教训=用户报「X 比 Y 小/不对」类视觉差异先查贴图资产可见边距，勿当缩小拍板执行**）。终版语义：技能键按下拖过 UGUI 阈值→进瞄准态（共用点击式全链）→**大圆盘=方向+距离转盘**（锚技能键圆心、半径 340 可调；方向型步数 k=盘距占大圆盘半径比例×臂长四舍五入钳 1..臂长——盘缘=该方向最远可选格、近心=第 1 格，选中格精确性全在盘内）→**小圆盘双夹取**（轮盘界=盘心不超大圆盘半径+屏幕界=盘缘不出屏；**盘位 _dragDiscLocal=瞄准解析唯一输入**，方向由「轮心→小盘」画布位移定——相机 yaw 恒 0 画布轴向=世界轴向，不再反投影）→**指向型（延奏/契约）改拖向选目标**（候选目标格屏幕方向与拖向夹角最小且≤拖动瞄准指向锥角 60° 者锁定；同方向多目标不可分辨=已知局限，精确选择走点击式点格）→**松手=留待定不提交**（确认唯一入口=完成选择按钮）→拖回技能盘任一键/取消钮松手=取消；**金格出屏跟随**=BattleCameraController.SetDragFollowTarget+Update 指数滑移（出视口 40px 才动、金格居中、入屏即停不回弹；跟随中瞄准不重判）。实现=BattleHud.cs OnSkillButtonDragBegin/Drag/End+UpdateDragAimPreview/ComputeDragAimCellFromWheel/FindNearestAimCellByWheelDirection+UpdateDragWheel（双夹取）+ReleaseOverDiscOrCancel+EnsureDragWheel（raycastTarget 全关）+BattleCameraController.SetDragFollowTarget/TryProjectToScreen；SkillDragForwarder（Build 分件）。旧参数 拖动瞄准每步距离/吸附半径/阴影收缩比 已删。参数=BattleHud Inspector（指向锥角 60/大圆盘半径 340/小圆盘半径 56/圆盘屏幕边距 16；实心盘贴图补偿 1.202=代码常量，换贴图按实测重算）+BattleCameraController Inspector（跟随速度 8/跟随边距 40）。落档=docs/18 决策六（含第⑤轮澄清）+17 §7+active/22+gic-battle-hud skill；HANDOFF 板会话 S（七改演化链）。待拍板遗留：手牌卡拖动部署（ScrollRect 让位）未做；盘尺寸/锥角/跟随手感待用户实测调参；指向型拖向选目标若不合意可改"拖动仅查看、选择走点击"。

- [2026-09-26 15:25:14] 【板面点击视差拾取已修复（2026-09-26 报障「点到推荐格判空白」用户直觉「和角度有关」正确，已随 58ff41e 提交推送）】根因=拾取射线交 y=0 地块底面（TryGetBoardPoint 旧注释自书「地块底面」）而玩家视觉点击面=地块顶面（草 0.5/水 0.36，瞄准高亮 quad=表面+0.03）——55° 俯角交点沿视线向远端漂 h/tan(射线俯角)≈0.2~0.6 格（透视相机越靠屏幕上方射线越平漂得越多，远处格≈0.55、近处≈0.19——故「有时候」坏、点格下半部正常），推荐格上半部点击解析进邻格→不在 _aimCells→ExitAiming 判空白；点击式点格与拖拽式点格改待定共用 OnBoardTap 同坏，拖动圆盘盘位解析=画布空间不受影响。修法=BattleHud.TryPickBoardCell 两遍收敛（BattleBoard.TileTopHeight 平面初判格+水面格按 GetSurfaceHeight 再交一次）；TryGetPlanePoint 在 BattleCameraController；**平移抓取仍走 y=0 平面（grab/current 同面差值恒定）勿顺手改**。陷阱全文=docs/14 §86。症状归因：「点到了却没反应/判空白」且位置相关（远处格坏、格下半部正常）先查本条；后续任何地面类点击拾取交平面必须取玩家视觉面实际高度，勿抽象 y=0。

- [2026-09-26 15:25:16] 【拖动瞄准短拖 1 格松手判空放已修复（2026-09-26 报障「只拖最近的1格（比如移动）松开判定我空放」，已随 58ff41e 提交推送）】根因=松手取消判定 ReleaseOverDiscOrCancel 用原始指针对「技能盘任一键槽矩形」命中——键槽 220×220（半宽 110px，实测 move 槽心 (179,504)/skill (1971,302)/burst (2253,302) 画布 2560×1440）几何上盖住「第 1 格」整条盘距带（臂 5 格时 1 格带=0~102px），短拖松手指针必压在起手键槽内被判「拖回键区」ExitAiming；右侧键簇矩形仅隔 39.6px 连 2 格带也被盖。修法（BattleHud.cs）：①键心死区=小圆盘半径 56（盘距轮心<56=未真离键，方向型/指向型解析都返回 null——防微拖/拖回取消由死区承接，小盘压键心=可视判据，盘拖回键心金色即隐）；②k 映射改「死区缘=第 1 格、盘缘=最远格」（盘距越过死区后的比例×臂长）；③ReleaseOverDiscOrCancel 删键槽矩形循环只留取消钮。**「拖回技能盘任一键松手=取消」旧拍板语义已收窄为「键心死区+取消钮」（spec 变更已明示用户待认可，勿当回归修回）**。陷阱=docs/14 §87。症状归因：「拖动式短拖被判空放/取消」先查死区与取消区几何；后续轮盘/摇杆类控件取消区取键心小死区勿用整块键矩形。

- [2026-09-26 15:25:20] 【大圆盘自适应位·三轮终版已落地（2026-09-26 五拍「大圆盘应当自适应位置」→六拍「让自己不会超出屏幕，小圆盘始终在鼠标位置」→七拍「拖动的格子判定应当是相对于大圆盘中心」+八拍「小圆盘不可超出大圆盘（需确保技能按钮不会超出屏幕）」，已随 72ac916 提交推送）】终版=「盘=标尺」：①ShowDragWheel 圆心=键心沿两轴 clamp 夹进画布〔盘半径 340+小盘半径 56+屏幕边距 16〕——盘不超屏；②小圆盘=指针贴身且**夹在盘内**（UpdateDragWheel 盘心距≤盘半径，拖出盘范围贴盘缘=该方向拖满）；③**格子判定基准=盘心**（两解析函数对 _dragWheelCenterLocal 取差：盘心→小盘位移定十字方向、盘距越过盘心死区后的比例×臂长定步数、分母=固定盘半径——小盘夹在盘内故固定分母即可满程）；④指向型候选方向也相对盘心；⑤`_dragGrabLocal` 位移基准已删除。**起手即有初始待定=新预期**（键位≠盘心时按键在盘上的偏移直接成初始瞄准值、移动鼠标=连续改选——move 键起手≈西向 2~3 格；待用户实测认可）；拖回盘心死区=取消（金色即隐）；⑥布局夹边收紧=BattleHud.Layout.ClampSlotToCanvas 从「中心 2%~98%」改「整件+16px 边距」（八拍括注核验：原钳制只夹中心、半宽件可挂出屏外 ~59px；画布装不下整件回退中心夹）。**三轮教训（docs/14 §88）：位移基准/盘缘分母/盘位镜像三版均被用户否——「判定相对盘心」按字面实现绝对基准，标尺真值优先；跟随件贴输入设备是底线，归一化勿做在跟随件位置上**。落档=docs/18 决策六（⑦⑧拍板条）+17 §7+active/22+skill 八拍链。症状归因：「起手就有金色」=预期勿修；「小盘出盘/盘挂出屏/靠边键拖不满」=误改先核本条与 §88。

- [2026-09-26 16:06:45] 【描边 0.08+飞行单位与我方互不阻挡已落地（2026-09-26 拖动式三轮终版同日用户验证通过后拍板，已随 72ac916 提交推送）】①描边 0.15→0.08（倒计时+伤害数字）：倒计时=BattleHud prefab 烘焙值已按 §74 流程重烘 0.08（脚本默认同步 0.08，迭代链 0.3→0.22→0.15→0.08——与祈愿面板称号预设 0.08 白同宽）；伤害数字=BattleDamageNumbers 脚本默认 0.08（运行时组件无冻结）。②飞行单位与我方互不阻挡（拍板链：「修改单位配置，所有飞行单位，与我方互不阻挡」→「只改配置文件就能所有地方都响应，确保维护性」→「不需要有穿不穿友方这个字段，只要这个单位 互不阻挡 字段为 true，无论是他穿其它友军，还是友军穿他，都不阻挡」）：**终版=单字段双向** `UnitConfig.与友方互不阻挡`（中文名，默认 false；MovementResolver.CanEnter/DeployUnitExecutor/BattleHud 两预览四处同读；被挡⇔占据者挡友方 且 双方都没开该字段）——开 true 即双向豁免（不挡友方进它的格+自己穿友方）。5 个 Fly 单位（Paimon/Columbina/Amber/Venti/Mavuika）已置 true，其 blockAllies 还原默认 true（该字段独立覆盖占据者侧）；**低级单位也全部置 true（2026-09-26 同日拍板「低级单位也全部设为互不阻挡」——1~2 星 Hilichurl/PyroAxeHilichurlBrute，纯配置零代码=单字段化后首批实证）**。**两版中间方案均被否勿修回**：①代码写死 normalMoveType=Fly（违反配置单源）；②blockAllies+ignoreAllyBlocking 两字段拆分（违反「一个语义一个字段」——均记 docs/18 教训条+docs/20 §1.6 通用规则）。敌方阻挡（blockEnemies/blockedByEnemies）与体积绝对层（≤3）不变。规则本体=docs/05 §5.3。症状归因：飞行单位移动/部署推荐仍因友方占据而红格→查四处是否同读该字段；「飞行单位能穿过我方但穿不过 3 体积格」=预期勿修。后续新增飞行单位记得 与友方互不阻挡=true（单字段）。
- [2026-09-26 20:07:45] 【低级单位 AI v3 绕行+v4 换目标巡逻+丘丘人配装已落地（2026-09-26 报障链「不会绕路卡湖边」→「走了两步后再也不走了」→拍板「当自己的任何攻击都无法打到时，换目标巡逻」+「给丘丘人使用凯亚的移动和战技」，已随 3b274e9 提交推送）】①v3 绕行=移动档改 BFS 最短路首步（BattleHeuristics.FindApproachFirstStep：通行=地形按移动者常态类型；阻挡=存活+尸体单位格，互不阻挡开者友方格放行；十字域不变；每回合重算走一步）。②二轮根因=goal 豁免漏洞（目标集含敌格∪全部邻格且 BFS 豁免——被占邻格/水格被指进，结算弹回=原地不动）→修法=目标集只收「可通行且无阻挡占据」的敌十字邻格（敌格剔除），BFS 展开零豁免。③v4 换目标巡逻=攻击档打不了时按距离升序逐敌试逼近步（新原语 FindEnemiesByDistance；FindNearestEnemy 改为其首元素，等距 unitId 升序铁律不变）——某敌首步=0（贴身已到/不可达）自动换下一个，全场无逼近步才缺席，不再站桩；两目标间可能来回踱步=巡逻感（拍板接受）。④Hilichurl 配装=skills 0→2=[Common_Walk（Move）, Kaeya_Frostgnaw（Normal 霜袭）]——**技能独立化后 skills=List&lt;SkillConfig&gt; 引用，跨单位配技能=引用复制零拷贝**（Common_Walk 共享单资产；霜袭掠夺/获能原子全继承=丘丘命中我方+5 摩拉）；进 2 格射程即开打，站桩场景大半消解。落档=docs/18 AI 双脑条 v3/v4 段。症状归因：停湖边=旧直行；原地弹回=goal 豁免已修；两目标间踱步=v4 预期。**同族开口**：AI 玩家脑 EvaluateMove 直行逼近高级单位停湖边——需「BFS 取最长直线段」设计待拍板。

- [2026-09-26 19:59:38] 【战斗地形无缝批六轮已落地并全部提交（2026-09-26 会话 T；前五轮 ef9d8cd、第六轮水面队列随 4a9d6c6，均已用户验证）】①几何缝→mesh 扩满 1×1；②半透明分格必显缝→BuildWaterSurface 整片焊接单 Mesh；③波浪穿墙→TileWater 只剩湖床（0/0/2）；④贴片下潜→GetDecalHeight 单出口；⑤底座被浪穿→GetVisualSurfaceHeight 视觉表面单出口（CellToWorld/ContinuousCellToWorld/GetDecalHeight 全走它；GetSurfaceHeight=拾取/Host 名义面勿掺波峰带）；⑥远端贴片仍下潜=单 Mesh 水面透明排序按整物体包围盒中心（比湖心远的贴片先画被叠盖、角度相关）→BattleWaterFlow.shader 队列 `Transparent-1`(2999)，一切 3000 透明件恒画于水面之上、贴片队列勿动（保 vs 立牌排序）。**勿修清单：水块材质槽 1=水面材质载体、勿恢复分格顶面/格子墙、贴片高度走 GetDecalHeight/站位走 CellToWorld、水面队列 2999 勿改回 3000、未来地图水贴虚空边界需补裙边**。全案=docs/14 §89（六轮，How to apply ①~⑪）。**取证三坑**：暂停态 resume 单帧解握手；反射脆弱改按名寻址（AimHighlight_{x}_{y}）零反射；战斗场景 Camera.main=null（相机未挂 MainCamera tag）。症状归因：地形缝/水面格界缝/波浪泥土线/贴片水面下（先查高度链再查排序——角度相关遮挡=排序指纹⑪）/水上单位被浪穿/水块空 submesh 先查 §89。  【会话 T 尾批四件已提交推送（2026-09-26，4a9d6c6）】①水面队列 2999（§89 第六轮，已用户验证）；②**完成选择快捷键**：复用 KeyAction.Confirm（默认 空格/回车）订阅 OnActionTriggered→OnConfirmButtonClicked 零差异同链；BattleHud.Update **每帧清 EventSystem 选中=防 UGUI 把 Space/Enter 当 Submit 双触发，勿删**（docs/18 决策六⑨+gic-input-system skill 陷阱 4）；③**血条量子刻度**（拍板「每50段，上限10段」取代固定 4 段）：每 50 血一格段数 ceil(MaxHp/50) 钳 10、线落 50k/MaxHp；血条/元能条共用 BuildTicks 量子画法（元能与旧均分数学恒等）；勿"修"回固定段数（docs/18+gic-battle-hud skill）；④**星级血量回落（拍板「1星统一 100、2星统一 150」）**：GetHPByStarLevel 1★100/2★150（3~5★ 不变 200/300/600，全单位 baseHP 均 Unspecified 走回落）——勿当 bug 修回 150/200。**推送新实证：github 直连被断的时段拉起 Clash Nyanpasu（D:\Tool\Clash Nyanpasu.exe，GUI 启动即带 mihomo）→代理通即推成，未切节点；直连 reset+超时三把后勿再硬试**。
- [2026-09-26 21:13:00] 【docs 全量整理批已收官（2026-09-26 会话 U，28 文件 +255/−136，提交 69277fc+ad52c13 已推送）】四区审计（4 代理全文核读 44 文件）后全库整理完成：①docs/17 §7 从批次流水堆叠重构为「已落地系统速查表」（15 行表+空缺清单+铁律，62.6KB→22.2KB）——**旧记忆/会话记录中对 §7 行级的引用已失效，读 §7 以速查表为准**；②docs/11 已贯彻其自身存档制（正文只留开口项，14 条新销案入文末存档）；③凯亚/芭芭拉 units 文档已转双版本格式并同步 B-2c 实装（旧格式仅剩 丽莎/诺艾尔/琴/行秋——拍板=随各自技能实装批次逐个转，勿批量预转）；④active/22「后交覆盖先交」矛盾、18/22 B6c/d 状态、29 五处待拍板已全修；docs/00 重复块已删、补 active28/29+designs 导航；**docs/14 §90（先前会话记忆沉淀）已随 69277fc 一并收口入库**；gic-battle-hud skill 两处 B-1 前旧预判纪律已同步（标准 kind 无需覆写=工厂强制同形）。其余拍板均按推荐落定（无操作）：概念提案盘点表保持原样、12 §10.4 决策清单保留。

- [2026-09-26 21:06:34] 【git quotepath 转义陷阱（2026-09-26 GIC 实证）】git diff --name-only 对非 ASCII 路径输出带引号+八进制转义（形如 "docs/00-\346\246\202\350\277\260.md"）——PS 拿它 Join-Path/ReadAllText 必炸 Illegal characters in path（且异常后变量残留 null 会产出假「pureLF」结果，极误导）。正解=git -c core.quotepath=false diff --name-only（原始 UTF-8 路径）；本仓文件名全中文，凡 shell 管道消费 git 路径输出一律加 quotepath=false。同类坑：rg --files 与 Get-ChildItem 输出不受影响可直接用。
- [2026-09-27 12:02:43] 【B-S3 立牌动作段·视频路线已闭环（提交 66a9eda+追加批；内容终版=H3-Max v2）】用户拍板「不用序列帧（太占内存），使用透明底视频形式」——**立牌循环动画=绿幕 mp4+运行时 ChromaKey 抠色**（真 alpha WebM/VP9-YUVA 不押注）：实现=UnitData.立牌动画视频（VideoClip，优先级高于立牌动画帧，都缺=静态兜底）→UnitView 视频路径（VideoPlayer→RT ARGB32 sRGB→ChromaKeyVideo shader 运行时抠色，阈值与离线管线同参=excess 平滑带 20~45/255+暗部门 g>60/255+despill；随机相位=vp.time；冻结/尸体 Pause 停摆；errorReceived 回落静态立牌；RT/材质 OnDestroy 释放，显存恒定与帧数无关）；BattleViewFactory.CreateChromaKeyMaterial 唯一出口；shader 登记 Always Included Shaders 防剥离；三张序列帧 sheet 已删（tmp 存档）。**内容终版=MiniMax-H3-Max v2**（36 帧/1.5s/768²，amber_fly_loop.mp4 同路径同 GUID 内容替换）：四模型同题 A/B（Seedance 2.5/2.0+H3/H3-Max，共 2370 积分）——几何指标 Seedance 家族全优（方幅/零裁切/接缝 2.5 与 2.0 同级 0.41×；2.0 动作幅度大 2 倍），MiniMax 动作过大（漂移 14~16%）+贴边、H3 方幅跟随失败（1344×768 横幅）、H3-Max v1 接缝 1.31× 游戏内可见顿挫（用户报障）→**v2 重生成加「明确循环节拍」提示词（周期约 2.5s+任意相邻 2.5s 片段完全相同）→接缝 1.31×→0.16×、贴边 26/48→9/36**，用户目检通过定稿；Seedance 2.5 版 tmp 备份（amber_fly_loop_seedance25_backup.mp4）可一键还原。**方法论：客观指标推荐 Seedance 家族、内容/画风终判归用户目检；MiniMax 循环补救配方=明确循环节拍提示词**。落档=docs/18 决策八两条+active/28 §8+17 §7+14 §90/§91（桥脚本 VideoClip CS0246→Object+反射；Play 态 VideoPlayer 握 mp4 句柄锁覆盖→内容替换前必 stop Play+探测回读勿信管道 exit code）。工具链=.codely-cli/tmp/paperdoll_seedance/（extract_build.py 抽帧抠底循环检测+各版原片/预览 GIF；桌面对比材料 amber_video_test/ 看后可删）。症状归因：安柏立牌不动→立牌动画视频+OnVideoError 日志；安柏显示绿块→ChromaKey 阈值/shader 剥离；循环顿挫→回绕差分超 1×（管线验收线）；其余单位静态=未配置属预期。
- [2026-09-27 14:17:35] 【立牌朝向规则补拍+凯亚立牌 v1 待目检（2026-09-27）】①朝向新拍板：人物不可完全面向右边（完全面向一侧=只能看见半边脸，禁止）；面部须大部分可见（双眼区域），合格样板=amber_glide 定稿——已入 gic-paperdoll skill「立牌设计规则」节。②凯亚全身立牌已定稿入库（2026-09-27 用户目检「满意」一次过）：Assets/Art/PaperDoll/kaeya_stand.png=并行会话生成版并已接线 UnitConfig 凯亚「立牌图」（本会话同题并行的另一份 kaeya_v1.png 留 tmp 未用）；配方=日辉双参考第一张 amber_glide 锁画风/朝向+第二张 UI/Cards/kaeya.png（800×1200 白底半身人物锚够用；NameCards/kaeya.png 无人物勿用作参考）；608×1088 原生透明。像素判定经验：a>0 bbox 贴右/下缘是低 alpha 光晕非裁切，判定 bbox 必须带 alpha 阈值（≥16 后四边留白为正=零裁切）。遗留可选项=凯亚 idle 循环动画（B-S3 绿幕视频路线）待拍板。
- [2026-09-27 15:39:49] 【全身立牌缩放 2.5→2 已拍板落地+立牌归一化基准实测（2026-09-27，机制已修正）】拍板「统一从 2.5 改为 2」：显示高=0.55×2×UnitData.额外缩放=1.1 格；改动=BattlePlayer「全身立牌放大倍数」脚本默认（BattleScreen 运行时 AddComponent、全场景/prefab 零序列化覆盖——改脚本默认即生效，无 §74 冻结陷阱）；docs/17 §7 速查行+gic-paperdoll skill 已同步。**归一化基准（2026-09-27 活体实测修正：sprite.bounds=整画布 608×1088 实证，sprite 与视频两条路线归一基准相同=全画布，勿再写「tight bounds 分叉」）**：立牌高度差纯来自画布内人物占比——站姿 9:16 全身图人物占画布 99% → 显示高≈归一高（现 1.1 格）；安柏视频主体仅占帧 69~87%（随循环姿势波动）→ 恒矮 20~30%（0.76~0.96 格）。头大小基准：凯亚头 0.17 格（头 165px/头身比 6.5，正常偏敦实——「凯亚画太高」不成立）；安柏视频头 0.25 格（近景构图，头反而大 45%）。**UnitData 新增 per-unit 字段（2026-09-27）：离地高度（倾斜组整体上浮，安柏=0.5 飞行悬浮；凯亚=0.05=底部渐隐行陷地观感补偿——素材底缘 alpha 16~102 渐隐行压地面线混色致「脚陷入」观感，几何实测不陷=格面+0.007，修法=离地微调勿动素材；底座圆盘留地=受击圆柱可视化）+ 额外缩放（乘全局倍数上，全员统一 1.0 微调闸门）；UnitView.Create 新参 hoverHeight、BattlePlayer 组合传参；**2026-09-27 陷地归因修正（用户拍板）+盘半透明收官（用户「验证通过」）**：主因=底座实体盘（盘面 y=0.52 高过补偿前脚底 0.507，脚站盘心=「栽进坑」观感）非渐隐行；修法 A 已落地=盘半透明投影感（UnitView 底座盘不透明度 **终值 0.7**（0.4→0.7 用户拍板）+WithDiscAlpha 单出口保 alpha+sortingOrder=-1；受击圆柱判定语义不变）；备选 B 下压盘高度未采用（贴地块面 z-fight 风险）。**配套离地基线：全部贴地单位离地统一 0.05（安柏 0.5 飞行语义不变；UnitData.离地高度 字段默认值已同步改 0.05——新单位自动带补偿，真实贴地需显式设 0；脚底过盘面余量 0.037）**。排查工具链=tmp/headcmp/（kaeya_feet.py 底部 alpha+headcmp 头部分割；Python 无 numpy 须 -u）。**被否决方向：地形格子 1.5×（探索完未实施被用户改向立牌缩放，勿再提议）。审计工具链=.codely-cli/tmp/headcmp/（PIL HSV 掩模+连通域+quantize 聚类头部分割+底部 alpha 分布取证 kaeya_feet.py；本机 Python 无 numpy、长脚本必须 -u——stdout 块缓冲 5 分钟零输出会被 shell 工具自动杀）。
- [2026-09-27 17:30:38] 【选中提示特效化·两束环绕圆弧光（2026-09-27 拍板+实现，编译 0 错 0 警，未提交待用户目检）】技能按钮瞄准态打钩图（SkillIconView.skillSelect/select.png）退役——改 OrbitBeamsUi 两束**元素色**圆弧光（色=ElementFactionConfig.GetElementColor(selfElement) 同源；物理单位=灰光）；立牌选中=OrbitBeamsWorld 两束**队伍主色**圆弧光绕底座圆盘（A=我方蓝/B=敌方红，选中敌方=红光=预期；贴片高度 GetDecalHeight+0.045 恒波峰带/金盘上）；脚下金盘保留。**同日追拍三连：「不要程序生成，让AI生成图片，光束应当是圆弧形」+「应当是两个小段白光弧段，二者对称」+「生成的不合适。两段对称小弧应当环绕旋转时，能够紧紧贴着圆盘」**——贴图=AI 生成 `Resources/UI/Battle/OrbitBeamArc.png`（SeeDream 2K 生成→导入 512/Clamp/关 mipmap；像素实测=两弧各跨 41°、中心 57°/235° 对径 178°、环覆盖 7%、纯白、环半径占比 **0.661** 烘两组件默认；程序化 OrbitBeamTexture 已删勿再程序生成）；驱动=整图绕环绕中心旋转（弧与画布同心天然保圆：UI=Image 居心旋转、世界=quad 居盘心 Euler(90,yaw,0)）；**三追拍「紧贴」**：轨道从远轨（首版底座 0.36/按钮 0.62）改**弧带内缘贴紧被环绕物缘**——底座环绕半径 **0.23**（贴盘缘 0.21）、按钮 **0.55×短边**（贴按钮圈缘 0.5）；贴紧值由贴图弧带半宽实测（≈8.8%×环半径）换算，换贴图须重算。**SeeDream 弧长控制差——「40 度」指令出 128~130° 长弧、参考图改短也无效，激进措辞「less than one eighth of the circle」才出 41° 小弧（生成 4 版，v1 单弧/v2v3 长弧废稿已删）**。**Tuanjie 贴图尺寸坑=docs/14 §93：maxTextureSize 顶层属性/TextureImporterSettings/Default 块三通道全不生效，必须写活动平台块（生成器把 Standalone overridden=1 maxSize=2048 写死为实效源）**；PPU 读回漂移属分叉怪癖不影响消费方。BattleHud.prefab 零改动。症状归因：「选中怎么没打钩了」=拍板预期勿修回 skillSelect；调观感=两组件 Inspector（环绕半径=紧贴拍板值 0.23/0.55、角速度、光弧半径占比——换贴图须按像素实测重算）。拍板=docs/18 决策六条+docs/17 §7 行；操作=gic-battle-hud skill；HANDOFF 板=会话 Y。**同日延续批收官**：贴图换 v2 彗尾版+盘外净化、金盘退役、打钩全项目退役（Select 节点四 prefab 删净）、世界版角速取负四连返修后用户验证通过（「验证通过，收尾」）；本条的 0.661/0.55/0.23/41°/金盘保留/打钩仅战斗退役等数值与条款均已被同日后续条目取代，终态以 docs/17 §7 行与 docs/18 收官条为准。

- [2026-09-27 17:20:44] 【弧光束贴图换 v2 彗尾版（2026-09-27 目检初版后拍板）】用户拍板「重新生成弧光束」→看图三方向（加粗同款/彗尾/长弧）→「使用这个v2 彗尾」。同路径替换 OrbitBeamArc.png（2048² 黑底文生图→**亮度提取 alpha→纯白化**——发光类贴图 AI 落库通用法，segmentation 硬抠会砍光晕；meta/导入设置不动=512/Clamp/关 mipmap 回读过验）；实测：对径 178°、含尾迹单弧 ~64°（亮核 ~35°）、环占比 0.692、内缘占比 0.602；头在角度高端=UI 逆时针旋转下头前尾后（方向像素验证，无需镜像）。OrbitBeams.cs 默认折算：占比 0.661→0.692、按钮 0.55→0.575、底座 0.23→0.242（贴紧口径=可见弧带内缘贴缘，旧 8.8% 半宽口径作废）。**世界版尾迹方向已修（2026-09-27 目检实证盘上尾在前；根因=Unity 左手系正 yaw 与 2D 正 z 旋向相反，环绕角速度 150→−150 修正=头前尾后，勿修回正值）**。工具链与三版原图=.codely-cli/tmp/orbit_arc_regen/（旧贴图备份 current.png、对比图 compare.png）。

- [2026-09-27 17:02:02] 【AI 生成发光贴图落库防线：边缘杂线盘外清零（2026-09-27 弧光束返修实证）】v2 彗尾贴图用户目检报「外面有一条多余的横线」=生成图最底边 y2042~2045 整条暗线（alpha 10~30% 全宽；**初版质检只扫 alpha≥128 亮核心，暗线漏检**）。修法=净化后同路径重落库：保留 r≤0.83×半画布、盘外清零（环内容最远 0.78），四边杂线一次清尽、弧带几何复测 0.692/0.575/0.242 不变零代码改动。**How to apply：AI 生成发光类贴图落库前必做盘外清零防线；杂点检测勿只盯亮核心（暗线/暗条是生成器常见产物）**。贴图另含满环淡圆环（alpha≈20%）属生成器「沿正圆轨道」表达，保留待用户观感拍板。对照图=tmp/orbit_arc_regen/clean_junk_map.png（红=已删像素）。
- [2026-09-27 17:15:11] 【选中金盘退役+打钩全项目退役（2026-09-27 弧光验证通过后两连拍板）】①金盘退役：用户拍板「脚下的圆盘不要替换，就额外加上此两束彗尾弧光即可」——选中单位脚下金盘标记退役（早批「金盘保留不动」条款废除）：单位自带底座圆盘本体不动，选中标记=仅 OrbitBeamsWorld 两束队伍色彗尾弧光（贴紧 0.242/高度 +0.045 不变）；金盘全链删除=BattleHud._selectMarker/_selectMarkerMaterial 字段+OnDestroy 行+Show/HideSelectMarker 金盘行+Build.CreateSelectMarker 方法及调用（0.55 quad×高亮金）。②打钩全项目退役（同日再拍板「应当全项目统一」）：SkillIconView skillSelect 四触点全删（字段/Awake/InitWithData/OnToggle）；非战斗屏选中反馈=同款 OrbitBeamsUi 两束元素色弧光（InitWithData 元素色同源、懒建随 toggle 熄灭）；Select 节点从 Skill/BattleHud/BackpackScreen/SkillDetailPanel 四 prefab 删净 18 处（BattleHud 10 键=内嵌拷贝非嵌套实例）；select.png 无引用保留未删。症状归因：选中脚下没金盘/背包图标选中没打钩=拍板预期勿修回；背包图标选中无弧光=查 SkillIconView.SetSelectBeams；选中无任何标记=查 OrbitBeamsWorld/_discOrbit 链。
- [2026-09-27 18:32:50] 【自研选中组件 SelectButton/SelectionGroup 已全量落地（2026-09-27 拍板「自研一个类似 toggle 的组件」→「开始」全量授权，未提交）】UI/Common/SelectButton.cs（: Button，FGUI GButton 语义对齐：选中模式 Common/Check/Radio、点按改变选中、初始选中、所属选中组、onSelectedChanged）+ SelectionGroup.cs（零登记互斥组，只持 Current）。**消费铁律：动作订阅 onClick、视觉订阅 onSelectedChanged；全项目勿再用 UGUI Toggle/ToggleGroup（TMP_Dropdown 内部件除外）**。迁移面=7 prefab（Skill/Card/InventoryCategoryButton/CardDetailPanel/SkillDetailPanel/BackpackScreen/BattleHud）40 Toggle+7 ToggleGroup 全清零；战斗四键点按改选中=false（选中态 BattleHud 状态机驱动）；Card isDeckPanelMode 改属性（置位同步关点按选中）；SkillClickForwarder 退役删除（onClick 直连，Button.Press 内置 IsInteractable 门——置灰自动拦点击，点击处手动检查删除、拖拽转发处保留）；BackpackScreen.cardSelectionGroup/UnitDetailPanel.selectionGroup 已接。**Why:** Toggle 勾选视觉全项目闲置（graphic 全空）、无 onClick 边沿语义、战斗屏被转发件架空。**陷阱三则**：①Unity 强制单 Selectable——Toggle 在场 AddComponent<Button 系>被拒，迁移须「快照→销毁→新建→回填」；②Tuanjie 无 SerializedProperty.CopyFromSerializedProperty——跨组件拷 Selectable 配置用公共属性直拷（targetGraphic/colors/spriteState/animationTriggers/transition/navigation/interactable）；③**prefab 组件不一定挂在根 GO**（BackpackScreen 组件在子 GO「BackpackScreen」上）——迁移重接须 GetComponentsInChildren 而非 root.GetComponent。既有状态勿当 bug：TabContainer 3 tab（UnitButton/NormalButton/ValuableButton）未接组=迁移前 m_Group 即 0、EventBus 同步链兜底互斥。落档=docs/18 决策十三+docs/17 §3+gic-battle-hud skill；HANDOFF 板=会话 Z。症状归因：选中无弧光/多 tab 同亮/技能键点不动先查本条与组件字段接线。
- [2026-09-27 19:05:54] 【SelectButton 改版返修：进背包初始选中第一张卡（2026-09-27 用户报障，两缺口已修）】①开详情迁 onClick 边沿后初始选中只亮图不开详情——修法=Card.OpenDetailView() 公共出口+背包 spawn 首卡 SetSelected(true)+OpenDetailView()（旧行为=选中+详情自动开）；②**池化组悬空引用坑：Release 只清按钮侧 Group，SelectionGroup.Current 仍持旧实例——同实例复用再当首卡命中 Select() 的 Current==item no-op，二次进背包连视觉都不亮；配套纪律=凡池化成员先归还后重建，重建前 ClearSelection(false)（RefreshCardList 已接）**。症状归因：「进背包没默认选中第一张卡/详情不自动开」查这两处；后续任何 SelectionGroup+对象池组合照此纪律接线。**2026-09-27 追加批（已随小批提交）：背包 3 个未接组 tab（UnitButton/NormalButton/ValuableButton，迁移前 m_Group 即 0）已按用户拍板「接上」统一接入 TopPanel 组（8/8 接组）——「遗留待拍板项」销案。**
- [2026-09-27 19:58:30] 【技能键点击循环+详情面板自适应摆位+拖动键挪盘心已落地（2026-09-27 拍板，未提交待目检）】拍板原话「第一次点击为瞄准模式，第二次点击为详情模式，再点击则又再次切换回瞄准模式（循环切换）」「弹出的详情面板应当出现在该技能的旁边（不得遮挡该技能按钮，不得超出屏幕，需要灵活的自适应位置）」「当选中了一个技能时，再点击其它技能，则切换到新点击的技能」「拖动式使用技能时，临时把技能按钮移动到新出现的大圆盘中间位置」。实现全在 BattleHud.cs 主分件：①OnSkillButtonClicked 重写（原「首点开详情/再点同键进瞄准」反转；详情模式=瞄准+面板并开，高亮/金格不动；_popupDef 死字段已删，同键判定走 _aimDef==def）②ExitAiming 统一收口 ClosePopup（点非可选格/取消/确认/超时/阶段切换全路径不残留）③PositionSkillPopupBesideButton 摆位算法=源键四侧按键位定偏好序（下半=上侧先/右半=左侧先）×三对齐候选→夹画布→源键硬避让（键必须保持可点）+其余技能键/取消/完成选择软避让→零交叠最早候选、软交叠最少者次之、键位夹画布兜底；落位=面板中心位移（TransformVector 两跳，锚点/缩放无关；RelatedPanel=面板子件随动）④MoveDragButtonToWheelCenter/RestoreDragMovedButton（盘心仍按原键心 clamp 算，控件挪盘心只写 anchoredPosition 槽零接触，HideDragWheel 单点还原）。prefab 零改动（新 [SerializeField]「技能详情面板」段=间距 24/边距 16 走脚本默认）。落档=docs/18 决策六新条+17 §7+active/22+gic-battle-hud skill；HANDOFF=会话 AA。症状归因：「首点不开详情了/面板不在屏幕右侧了」=拍板预期；「面板盖住其它技能键」=软避让躲不开的预期（源键恒不盖）；「松手后技能键没回槽位」才是 bug。**追加批（同日拍板「技能名字上移（最好能一半盖住技能按钮），并且使用本项目已经有的描边效果」+两轮返修「太高了，下移一点」+「爆发技能不要比别的技能大，改为一致」）**：四键 Name 标签落在按钮下半区（**终值 anchoredPosition.y=+54**，标签带局部 [−56,−94]、中心 −75；迭代链：首版负值 −36/−47 下坠「间隔更远」→+74/+85「太高」→+54 终值），描边=SDF 原生 0.08 黑（新材质 `Resources/UI/Battle/zh-cn SDF Black Outline 0.08.mat`——复制 zh-cn SDF 1 白预设改黑，**放入库目录勿放 Assets/TextMesh Pro/=gitignored 不入库、prefab 引用他机 missing**）；**TMP_UGUI 材质序列化字段=m_sharedMaterial（非 m_fontSharedMaterial，猜错会 NRE）**；**burst 槽 264→220 与其余三键等大**（推翻 2026-09-18「爆发放大 1.2 倍」拍板；布局存档只存锚点+scale 不存尺寸，任何方案生效）。改动仅 BattleHud.prefab 内嵌拷贝，共享 Skill.prefab 未动（背包技能名不受影响）。**返修教训（「看起来间隔的更远了」）：Name 锚点=(0.5,0) 控件底边锚+pivot=(0.5,1) 标签顶边基准——anchoredPosition.y 为正才进按钮，公式=槽高/4+标签高/2−下移量；首版把「下半区中点的局部坐标」直接当 anchoredPosition 写=锚点体系换算勿拿局部坐标直填；HUD=BattleScreen 运行时 Resources.Load 实例化（场景无烘焙实例，改 prefab 即改「场景里的」，每局生效）。**返修三笔（2026-09-27 报障「选中角色立牌时，底座的旋转的彗星不见了」）：OrbitBeamsWorld 复用件二次激活缺失——_discOrbit 只在 null 时 Create（首建即 active 掩盖缺口）、HideSelectMarker 收起后 ShowSelectMarker 复用路径只 Setup 不激活→第二次选中起永远隐形（会话 Y 只验证首选中而潜伏）；修法=Setup 后显式 SetActive(true)（显/隐须同驱动点成对，docs/14 §97 新条：懒建+缓存复用件再显一律显式激活，审查口诀=沿创建/收起/复用三路径各核激活）；同库扫描无同族（OrbitBeamsUi/SkillIconView/拖动圆盘/详情面板/RelatedPanel 全成对）。**返修四（2026-09-27 拍板「转圈速度应当与技能的转圈速度一致」）：OrbitBeamsWorld 环绕角速度 −150→−240——大小=按钮版 OrbitBeamsUi 240，负号（左手系 yaw 方向修正=头前尾后）保留勿动；运行时建件改脚本默认即生效；docs/18 弧光条+docs/14 §95+skill 文件地图行已同步终值。****
- [2026-09-27 20:54:10] 【战斗地形贴图批次已落地（2026-09-27，未提交待目检）】原神截图参考日辉生成：草地/土地/石砖/圆石顶面各×3+水面×1+草簇×3（Assets/Art/Battle/Textures/battle_*，草簇已裁剪到 alpha 内容框）；TileVisualsConfig.asset（Resources/Configs）单源=顶面变体（BattleBoard.ApplyTopVariant 每格确定性哈希随机选材质槽 1，雪/沙/冰共用草地变体）+草簇装饰参数（每格 3~5 簇/高 0.18~0.30/后倾 55°/yaw 抖动 12°）；BattleGrassDecor（View/ 新文件）=合并单 Mesh 立牌式草簇（billboard+绕底边后倾，submesh=变体），BattlePlayer 建盘后 BuildGrassDecor(_viewCamera)，复用 GIC/Battle/GrassSway 顶点摆动（未来风向/力度钩子=_SwayStrength/_SwaySpeed 材质参数）；TileGrass 旧烘焙交叉面草簇 submesh2 退役为空（10/2/0），旧贴图旧材质仅退役未删；TileType 新增 Dirt=7(土地)/Cobblestone=8(圆石)+地图字符 D/C，DirtTile/CobbleTile prefab（TileStone.mesh 结构，BattleBoard 两新字段+TileVisualsConfig 已接进 BattleScreen.unity）；BattleWaterFlow pattern 改亮度采样+新「贴图色强度」_TexColorBlend=0.6 混入贴图原色（队列 2999 未动）；FirstMap 中心岛 9 格试铺 D/C 供目检（移动语义同草地零玩法影响，保留与否待拍板）。立体感路线=烘焙明暗（提示词画 bevel/AO/高光，原神同款）；法线贴图无消费方（全 Unlit）、POM 真视差已调研待拍板（55° 俯角收益最大、需高度图+自写 shader+格界处理）。TJGenerators 补充：任务完成后 status 端点只回瘦响应，imageUrls 只在轮询完成时刻响应体或 check_task 可得——合并轮询脚本须在完成时刻打印响应体。素材总览+工作链=.codely-cli/tmp/terrain_gen/（terrain_contact_sheet.png）。症状归因：草地整片重复→查 config 变体数组接线；草簇不动→BattleGrassSway_V* 材质/_SwayStrength；水面颜色不对→_TexColorBlend；进战斗 16 贴图/15 材质/2 prefab/1 配置 SO 均 guid 密文勿文本反查。
- [2026-09-27 21:29:49] 【地形批次返修三轮已修复（2026-09-27，未提交待复测）】①顶面贴图朝南根因=TileGrass/TileStone 顶面 UV 反向（image-top 落南缘）——修根按位置重建（u=x+0.5、v=z+0.5，image-top=北），未来换贴图免预处理；教训：mesh 批量改 UV 顶点必先去重（三角形索引重复引用致双翻=半翻垃圾态，位置重建法自愈）。②草簇错位根因=格内偏移错当格心偏移（+0.5 对角位移、55 簇漂水面）——root=x+ox-halfWidth 修复，归属断言 off=0。③圆石变体雷同（v1↔v2 像素差仅 6.0）——统一「同风格同密度仅石块排布不同」提示词重生成（两两差 13.7/11.8、缝隙密度一致），同名替换零接线。**引擎级结论：Unity 左手裁剪空间下 front=顶点顺时针=cross(b-a,c-a) 朝观察者（与 OpenGL 右手惯例相反）**——草簇绕向按此修正为正面朝相机；写绕向断言勿用固定边序算 cross（要读存储三角形序）。另：MCP 状态端点完成后恒瘦响应（BODY 亦无 imageUrls，完成时刻也一样）——URL 只能走 check_task；轮询=状态查询非事件监听（任务先完成也只多等一轮首查，不会丢通知）。
- [2026-09-27 22:51:22] 【土地贴图变体 SUB 同族微差批已收官（2026-09-27 深夜用户目检「满意」+随地形批提交 dc9d41e 已推送 01006de..dc9d41e）】battle_dirt_top_v1/v2/v3.png=SUB 批终版：三张**同规格**——~20/24/22 颗 1/25 格宽（~41px）小灰褐石、圆润+微扁混合、**自然随机散布**（无排线/无堆团）；各挂不同柔和底参考去相关（dirtNew_v1 / dirtFeat_v1（=dirtNew_v1 底再生成）/ dirtNew_v3b）。像素验证：石子位置跨变体 NN 57~63px（彻底独立）、全图两两差 5.0~8.2（小差异带）、石子面积 1.7/3.0/2.4% 同档。**随地形批整批提交 dc9d41e**（85 文件：16 贴图+15 材质+Dirt/Cobble prefab+TileVisualsConfig+BattleBoard 变体轮换+草簇层+docs 14 §98/17 §7；复审会的批1/批2 修复文件按其 HANDOFF 归属已全部排除）。**生成配方已沉淀 gic-terrain-texgen skill**（同规格+不同柔和底参考+清除指令+日辉四推论+验收管线表，未来璃月/稻妻地形直接复用）。返修链教训留档：dirtNew 收敛→dirtStone 结构化被否→程序拼合被用户否决→dirtNat 体量轴被否（紧邻铺一片观感差必须小）→dirtSubtle 终版；dirt 累计 675 积分。症状归因：土地变体问题先查本条与 skill。


- [2026-09-27 22:39:47] 【日辉单参考锚定机制（2026-09-27 土地变体四轮实证，二三条已修正）】frontier_sunburst 单参考生成=**底面近乎复现参考**（多张同参考生成后与参考全图差仅 2~3、彼此共享同底同云斑——共享底反而是优点：相邻格零色块跳变），但指令只有效驱动前景内容物（石子布局/体量指令全部准确执行）。推论：①用同参考做变体集=变体间差异只剩前景物，弱指令（「仅排布不同」）会被模型先验收敛吃掉——dirtNew 批两两差 8~11.5 用户仍判「几乎一样」；②体量/数量轴指令有效但**仅适用于非相邻展示的素材**（图标族/立牌族）；**相邻连片铺的地形变体集禁用体量轴**（大卵石 vs 碎砾让相邻格读成不同地形=拼布感，用户原话「观感差必须小」）——正确配方=**三张同规格前景物（同尺寸/同形态/近同数量）+各挂不同柔和底参考**（同族底图再生成件如 dirtFeat_v1 可作参考：保底面族一致又与内容去相关）+「清除参考原有石子后自然随机散布」指令，位置独立抽（实证跨变体石子 NN 57~63px、全图差 5~8=小差异带）；③结构化布局指令（排成线/堆成堆）执行虽准但用户判「不自然/故意」，弃用；④「清除参考原有石子」指令对稀疏参考有效（石子面积 <5%）、对密集参考失效（dirtNew_v2 18.1% 旧石子残留叠加）——变体参考必须选干净稀疏底。适用：未来任何日辉参考生成变体集（璃月/稻妻地形、立牌、图标族）按①~④。

- [2026-09-27 22:36:19] 【战斗系统全量复审（2026-09-27 用户立项，进行中）】用户拍板：review 战斗系统+关联全部分批进行（每次对话一批，用户可说「开始」连批）、**不采信任何旧 review 记录**、逐处对比行业成熟做法（原话「过于庞大，分批次review」「不要相信之前的review记录」「比较每一处我们的做法与行业成熟做法」）。批1（模拟核心+协议主干）✅批2（技能执行链+效果原子库）✅：**均无🔴**。批1 8🟡≈B7 前置协议健壮性（ack 单集合待 per-client/SubmitAction 无回执/turnNumber 未校验）+SyncCurrencyEntry 不刷新已有货币条目 count（快照重映射掩盖的潜伏陷阱）+效应→命令合并键四套散点建议收口成表+Host 演算零 RNG 实证可重放但无 replay/harness 变现（白捡质量资产）。批2 5🟡=**体积规则双源**（Unit.cs L22 与 DeployUnitExecutor.IsDeployCellValid 各写一份「Building?2:1」，建议 UnitData.GetVolume() 单出口）+Enso/Contract 目标失效仍扣体力元能（与「门槛不足不扣」不自洽，待拍板 A=维持推荐/B=Hearthstone 整行动取消）+EffectCompiler.CompileAtom 无 default Warn+原子 paramKey 引用缺参静默 0（建议 B-2 编辑器补校验行）+治疗/maxHP 伤害 int 截断=双取整点（建议 float 化末点单取整）。**两批待拍板均未答**（replay harness 立项/货币 count 一行修/🟡登记 docs/11/体积双源/扣费口径/原子安全网/数值 float 化/批3 是否开始）。剩余批次地图：批3 Buff+经济/手牌/部署→批4 移动/投射物/时轮→批5 AI 双脑→批6 表现层→批7 HUD 交互→批8 配置资产+工厂+编辑器→批9 组合根+联机横切（每批输出行业对比+🔴🟡🟢 报告，skill=gic-code-review 格式）。Why：跨会话多对话工程，进度与待拍板需跨会话跟踪。How to apply：用户说「开始/继续」即按地图取下一批；旧记忆「三轮审查已闭环（fad5c87/f2c171a/ce335f3）勿再重跑」指那批修复不拦本轮用户明令的复审，复审发现均为新增项未重报旧修。
- [2026-09-27 22:48:12] 【战斗复审批1/批2 修复批已落地（2026-09-27 深夜，承接同日「战斗系统全量复审」条目）】用户拍板「按照你的建议优化批 1 批 2。注意复检避免误判」→ 10 项修复全落地+编译 0 错 0 警（未提交待用户复检，与地形批未提交件并存勿混提交）：①货币条目 count 同步（SyncCurrencyEntry）②OnSubmitAction turnNumber 校验③_pendingAckKey 残字段清理④合并策略表注释（TurnResolver）⑤BattleCommand 字段矩阵⑥GetTile 越界 0 哨兵⑦UnitData.GetVolume() 体积单出口（双源收口）⑧CompileAtom default Warn⑨UnitConfigEditor 原子参数引用校验行⑩伤害/治疗换算 float 化末点单取整。**扣费口径拍板 A=维持**（目标失效=行动已使用，体力元能照扣——与「门槛不足不扣」并行口径，docs/18 决策十四）；docs/11 新登记七条（per-client ack/SubmitResult 回执（B7）、replay harness 立项（批9 细化）、互灭空转（B8）、即时行动过期、0 步移动、预判首 clip、规模红线）。**复检截获误判**：FromStorageString 回退项撤回（地图解析走 CharToTile 已按虚空+Warn，TileType.FromStorageString=零调用死代码）；TileType None=0 显式成员顺延（文件由地形批持有未提交改动，GetTile 用 0 哨兵先行）。存量校验全绿：43 SkillConfig/24 原子/0 缺参（编辑器脚本全扫）。行为变化仅一处：maxHp 基准伤害/治疗截断→四舍五入（特定血量±1），其余零行为变化。How to apply：下一批=批3（Buff+经济/手牌/部署深读）等用户「开始/继续」；本批文件清单=HANDOFF 板会话 AC 节。
- [2026-09-27 22:57:41] 【伤害/治疗取整终版=末点截断（2026-09-27 深夜用户拍板「对于最终的小数点，比如最终的伤害，最终的治疗，舍弃小数点」——承接同日修复批第⑩项勘正）】DamagePipeline.Calculate 最终伤害与 EffectCompiler.ResolveHealAmount 最终治疗 RoundToInt→FloorToInt：中途全 float、末点单次截断（舍弃小数），勿在中途取整。净效果：基于 maxHp 的伤害/治疗值与修复前 int 截断口径**完全一致**（修复批交付时说的「特定血量±1」收回）；唯一相对修复前的变化=攻击百分比伤害的小数舍去（如 67.65→67 而非 68）。同批补 TileType.None=0 显式枚举成员（地形批 dc9d41e 入库解锁撞车后补上，GetTile/CharToTile 全改引）；复检已通过、编译 0 错 0 警、修复批+追加批仍未提交等拍板。How to apply：后续新数值出口（护盾/反伤等）一律同口径——float 计算到底、末点单次 FloorToInt；勿用 RoundToInt。
- [2026-09-27 23:25:24] 【战斗全量复审批3 已完成（2026-09-27 深夜，承接复审条目）】批3=Buff 族+经济/手牌/部署（24 文件亲读+6 组交叉验证，全为新发现、前三轮审查未覆盖）：**1🔴**=Card 族跨层缠绕（Framework/Pool/CardPool.cs 引用 Battle.Card+UI.CardDetailView=基础层反向依赖踩 docs/17 §2 红线；Battle/Card/Card.cs→UI.CardDetailView=Battle→UI 反向；BackpackScreen/DeckSwitchPanel(UI)→Card 桥未在既定桥清单——根因=Card 族本质是收藏/卡组展示控件却归 Battle 域；推荐 A=Card 族+CardPool 迁 UI 层 / B=docs/17 认账豁免，待拍板）。**🟡×5**：①皮肤切换真缺陷=CardDetailView L132+CardViewStrategyBase.ApplySkinTo 直写 saveCardData.skin 绕过 SaveManager.Modify 标脏（全项目其余 20+ 处存档写全走 Modify 实证；换肤后退出可能不落盘，修法=调用点包 Modify 一行级）；②货币身份三处硬编码（HandCard.IsCurrency/BattleSimState.IsCurrencyCard/SyncCurrencyEntry 各写一份 Mora||Stamina，单源化=ItemConfig.isCurrencyPool 字段）；③属性管线 UnitStats.GetFinalStat 用 RoundToInt vs 伤害/治疗新拍板 FloorToInt=两套取整口径并存待拍板；④UnitStatus/UnitElement 各一套 StatusModifier/ElementModifier 修改器系统全项目零调用=死路径（活路径=SetStatus/Dye 直写 base）待裁决删或留注；⑤其余备忘=ItemCardView 缺 MissingImageGuard/StatBuff duration=0 配置校验缺口/Card.SetCount 死代码/ItemParam 字符串键（技能域已迁枚举物品域未跟进）/Burn tick 不附着火=B4 拍/BuffState 不带 value 靠快照终值自愈/UnitInventory 空壳。**做得好**：StatBuff 基类收口（第三个属性 Buff 3 行即成）/FreezeBuff Merge 取长覆写/池化复位纪律/CardLightBandEffect 材质生命周期（§63 满分）。**待拍板 1~6**（Card 族迁移 A·B / 皮肤 Modify 修 / 货币单源化时机 / 属性取整口径 / 死修改器裁决 / 批4 开始）均未答。How to apply：用户答「按照你的建议优化」类指令时按本条清单执行；复审下一批=批4（移动/投射物/时轮判定：MovementResolver/ProjectileResolver/SkillTimelineAsset 消费链），批5~9 地图不变。
- [2026-09-27 23:53:36] 【统一消耗模型已立项（2026-09-27 深夜用户拍板「统一消耗模型」，批3 复审产物+用户设计输入驱动）】起因链：用户「未来技能不一定消耗摩拉或体力，甚至可以是消耗酒、苹果等食物」+批3 发现消耗管道四种资源四种写法。设计=**docs/active/30**（新）、拍板=docs/18 决策十五、登记=docs/11 战场与经济节。核心：SkillData.costs 数据驱动 (资源,数量) 列表（CostKind 元能/体力/摩拉/Item×ItemName）+ResourceGate 统一门（收编 StaminaGate）+MoraSpend/ItemConsume 新效应+ItemConsume 新命令（客户端手牌镜像分流+角标即时刷新）+HUD 置灰单源化（预判/结算同形）+双轨迁移（存量技能回落零行为变化）。门槛语义：不足=落空不扣/先全查后全扣/低级单位豁免推广到 Mora·Item（元能不豁免）。批次 C-1 管道→C-2 存量迁移（EnergyCost/体力分档/移动常量→costs 资产+回落退役）→C-3 首个真实消耗技能（随角色批）。**随批裁决：货币身份三处硬编码=永久两件套不单源化**（ItemSubType.Currency=背包分页语义≠战斗池化身份；原石 maxPrepareCount=100 反例否决「Currency&&maxPrepare>0」派生式；「表层是卡、底层是账户」模型自洽，批3 🟡#3 销案）。How to apply：用户说 C-1/开始实施即按 active/30 施工；UseItem 物品使用行动=B8 收编勿另起管道；GainCardEffect 获得对偶随 C-3。
- [2026-09-28 00:00:55] 【统一消耗模型 C-1 管道已落地（2026-09-28 00:0x，承接同日立项条目；未提交待复检）】按 docs/active/30 §3 施工完成，编译 0 错 0 警：新文件 SkillCostEntry.cs（CostKind=元能/体力/摩拉/Item×ItemName+条目类，挂 SkillData.costs）+ResourceGate.cs（统一门 Has/Charge/HasAll/ChargeAll——低级单位豁免玩家资源、元能不豁免；StaminaGate 迁移期并存 C-2 退役）；MoraSpendEffect/ItemConsumeEffect（AppliedAmount 回填口径同 MoraPlunder——不足额零命令非漏发）+BattleCommandType.ItemConsume=15（metadata=itemName/value=amount，矩阵行已补）；SkillExecutor 消耗段双轨（costs 非空=先全查后全扣；空=回落旧链元能参数+体力分档+尾加元能消耗——**元能消耗效应位置从尾加改为 Charge 时登记，ApplyEffects 按 (目标,类别) 去重故结果恒等**）；TurnResolver 应用/发射/审计三处登记；客户端=BattlePlayer.OnItemConsumed 事件+本地 handCards 镜像扣减（减尽移除）+BattleHud _handItemCards 角标即时刷新（RefreshCurrencyCard 重 Init 链复用，条目减尽残牌一回合快照重建收）+HasSkillResources 置灰单源化（costs 镜像 ResourceGate.Has+双轨回落 HasEnergyForSkill∧HasStaminaForSkill）+HasHandItem。**存量断言：43 SkillConfig costs 全空=全走回落行为零变化（编辑器回读实证）**；新能力零消费方（C-2 存量迁移 EnergyCost/体力分档/移动常量→costs 资产批改+回落退役；C-3 首个真实消耗技能随角色批）。HANDOFF 板=会话 AC 复活节。How to apply：验证 C-1 用测试配置（给某技能配 costs=[Item 苹果×3] 走一遍门槛/扣减/角标/对账）；「统一消耗模型」话题先读 docs/active/30。
- [2026-09-28 00:21:09] 【统一消耗模型 AnyItem 语义已实装（2026-09-28，用户纠偏「不能因为现在饮品只有蒲公英酒而写死，要求的是任意饮品」——同类任意匹配从触发点后置改为立即实装）】CostKind.AnyItem=5+subType 字段（「任意饮品」=kind=AnyItem+subType=Drink，配具体物品=设计错误）。**语义定版**：①跨同类条目凑足（3 蒲公英酒+2 果汁可付「任意饮品×4」）②原子性——防御路径未足额=GainCard 回滚已扣部分零命令（支付要么全额要么不支付）③确定性=手牌列表序逐条扣零随机④货币卡不可被 AnyItem 匹配（LoseCard(货币)=动资源池属语义错误；subType=Currency 恒不匹配+Warn）。实现分布：ResourceGate（CountAnyItems 聚合/CollectAnyItems **物化列表——勿改回惰性枚举，扣减循环内 LoseCard 移除条目=集合修改异常**，本批实装中亲踩后修正）/ItemConsumeEffect 双模式+Consumed 明细（命令逐条发+审计按明细查）/HUD HasHandAnyItem 聚合镜像。验收用例已重配：Kaeya_Frostgnaw costs=[Stamina×10, AnyItem 饮品×1]（回读断言过，临时验收后撤销）。Why：用户对「任意」的语义要求=子类型匹配非具体物品——数据表现状（唯一饮品）不得反过来绑架声明语义。How to apply：技能配消耗时「任意 X」一律 AnyItem+subType；新子类型物品（第二饮品/食物）落地后 AnyItem 自动覆盖零改动。
- [2026-09-28 00:44:30] 【统一消耗模型 C-2 存量迁移完成（2026-09-28 00:3x，承接 C-1 条目；C-1+AnyItem 已用户验证通过；未提交）】资产批改：25 资产（Stamina+19=全 Normal/Burst/Move 技能；Energy+13=全 EnergyCost>0）——覆盖断言全绿（配额技能 20 个/EnergyCost>0 全 0 缺）。旧路径退役：GetStaminaCost/GetEnergyCost/StaminaGate/移动常量直读/HUD 双查全删——**ResourceGate 为消耗检查/登记唯一出口**；移动路径=MoveExecutor.GetMoveCosts（**21/34 单位无 Move 条目=常量兜底是主路径**——BattleMetrics.StaminaCostPerAction 保留为兜底真源）；AI 双脑门槛改 costs 感知（AIDebugBrain×4+LowUnitBrain×1，AI 不感知遗留销案——AI 凯亚无酒不再上交霜袭）；HUD HasSkillResources 简化（costs 空=免费技能）+HasEnergyForSkill/HasStaminaForSkill 删+ApplyMoveButton 单源化（move!=null?HasSkillResources(move):常量）；UnitConfigEditor 校验行补 costs 守卫（缺 Stamina/EnergyCost 漂移/AnyItem 配 Currency/Item 未指定/数量≤0）。**关键保真**：EnergyCost 参数保留（描述渲染单源，勿删）——运行时真源=costs，漂移由校验行拦截；临时验收条目（凯亚 AnyItem 饮品）已撤销；迁移按旧口径逐资产镜像=回归零行为变化。How to apply：以后加消耗=配 SkillData.costs 条目零代码；C-3 首个真实消耗技能随角色批；「统一消耗模型」话题先读 docs/active/30+docs/18 决策十五。
- [2026-09-28 12:43:50] 【战斗全量复审批4 已完成（2026-09-28 深夜，会话 AC 交接前最后一批；承接复审条目）】批4=移动/投射物/连续命中判定（MovementResolver+ProjectileResolver 全文精读，纯 review 零代码改动）：**✅ 通过，零代码缺陷**。swept-circle 分段二次求交数学正确（launch 窗口 t0=max(t,launch)+段中 frac 插值两坑全处理）；「位置读命中时刻/状态读片前快照」同片并发基石忠实实现；消散点千分定点+预判 k−0.5 同口径；hitT 透传链头（几何来源）确认无漂移；枚举序铁律遵守。**唯一发现（🟡 待拍板）**：MovementResolver 类头「相向对穿=互相穿过」语义越界——roundStart 快照判定下默认阻挡配置（BlockAllies/BlockEnemies=true）对穿=互弹双作废，仅互不阻挡对/BlockAllies=false 真穿过；待拍板 A=勘正注释+docs/05 §5.3【推荐】/B=改实现真对穿。🟢：hitT==voidT 判消散微边缘；性能规模备忘。**C-1/C-2+AnyItem 已提交推送 b4f914f（1588b98..b4f914f，用户验证通过）**；批3 拍板项①Card 族迁移②皮肤 Modify④属性取整⑤死修改器仍挂起未答。How to apply：下一会话入口=①批4 待拍板项②批3 挂起项③**批5 复审（AI 双脑——注意 C-2 已把 AI 门槛改为 ResourceGate.HasAll costs 感知，批5 按新形态审）**④批6~9 地图不变（HANDOFF 板会话 AC 节=完整交接）。
- [2026-09-28 12:57:38] 【战斗全量复审批5 已完成+拍板项已落地（2026-09-28，承接复审条目；未提交）】批5=AI 双脑（AIDebugBrain v2/LowUnitBrain v3·v4/BattleHeuristics+五处门槛接线）**✅ 通过零 🔴**、7 🟡。C-2 新形态确认=AI 门槛全量 costs 感知（五处 EvaluateAttackSkills/Enso/Move/WouldHaveFiringLineFrom/LowUnitBrain 全走 ResourceGate.HasAll 与 SkillExecutor 同函数——「AI 不感知消耗」销案实证）。7🟡 与处置：①Contract 无 AI 消费档（Host EffectCompiler.CompileSkill L43 分支已就绪；Xingqiu_RaindeepGate=唯一契约资产 effects 空=占位休眠——**随行秋实装批接 AI 档**，已登记 docs/11）②FindApproachFirstStep BFS=MovementResolver.CanEnter **保守近似非同口径**（只查移动者侧 flag；CanEnter=占据者.BlockAllies+双侧 flag 才挡+体积绝对层；现役低级单位全开 flag=零实害；**注释已勘正**，首个不开 flag 低级单位落地须同步双侧）③clip-less Normal 预判/结算形态分叉（Host CompileJudgment 兜底=首停投射物 vs 预判家族=整线 24；4 资产全占位休眠；已并入 docs/11 预判首 clip 行+建议 UnitConfigEditor 校验行「Normal+effects 非空+无 clip→Warn」）④PreviewLineTargets=预判工厂手工镜像（clips[0]/整线/首停三分支逐行同构双源——L50 扩展时三处同步或下沉共享原语）⑤攻击 OnHit/OnCast 附属价值零估值+Normal 平加获能 12 分假设（霜袭掠夺/水之浅唱治疗不计分）⑥威胁/生存感知缺失（MoveLineUpScore 反鼓励进敌射击线）⑦EvaluateEnsoSkills allies 循环内重建+门槛四连块两处重复。⑤⑥已登记 docs/18 决策十一「v3 强化备选清单」小节（威胁惩罚+原子估值泛化，**待用户拍板「开始」才动**）；⑦顺手项未做。行业结论=同时制下 utility 评分制是正解（对手行动不可知→minimax/MCTS 无意义；对标 FE/XCOM/ITB 有 threat map=⑥差距源）；AI 不作弊/同接口同防线/确定性三红线全满足。已知开口复核仍在册：EvaluateMove majors 直行停湖边（docs/18 决策十一 v3 段）+预判首 clip+Host 方向校验②③+AI 出战（docs/11）+AI 脑挂载 UI 层（B7）。编译 0 错 0 警。How to apply：用户「继续」=批6 表现层（BattlePlayer/UnitView/BattleBoard/BattleViewFactory/BattleDamageNumbers/BattleOverheadBars）；批4 对穿语义+批3 挂起①②④⑤仍待拍板；本批 3 文件（docs/11+docs/18+BattleHeuristics 注释）未提交等收尾。
- [2026-09-28 13:24:07] 【战斗全量复审批6 已完成+用户架构定性纠正+连携协议设计稿已立项（2026-09-28，承接复审条目；未提交）】批6=表现层 12 件（HUD 四分件归批7）✅ 通过零 🔴、5 🟡。**用户纠正定性（重要）**：原话「视图层=纯命令回放架构这样不好，因为本项目更接近MOBA，比如稻妻的连携」——查证稻妻.md：连携=执行阶段播放中 2 秒现实窗玩家介入（勾玉+连携按钮+阶段推进等待）+璃月契约时停 3 秒同族=**客户端最终形态=互动回放非被动回放**；active/22 §2 本就写「对标王者荣耀/原神服务器权威」——「纯命令回放」表述收回；**主干不动**（2026-09-26 拍板维持，钩子早已在：SubmitInstantAction=5 全链四道校验+Segment.insertedInstantAction 播放位+Pause/Resume=7/8 预留+WaitConditions 设计在册）。5🟡 已全修：①Mesh 生命周期（§63⑥ 新条款：运行时 new 的原生资产必须有持有者——CreateDisc 静态共享单 mesh（segments 参数收进常量）/水面 mesh 登记 BattleBoard._runtimeMeshes/草簇 mesh 挂组件 OnDestroy）②sortingOrder 收口 BattleMetrics 新段 7 常量（-1/1/10/11/12/38/39；0=瞄准贴片隐式默认、40=HUD prefab、2999=水面 shader 队列）8 处改引③_formationOffsets 死存储删净④OverheadBars 类头注释勘正（程序化→真实资产 BarBg/BarFill.png）⑤规模红线补 per-unit RT（768²≈2.25MB/单位）+队形分配注记。**连携/契约互动窗口协议设计稿=docs/active/31（新，待拍板 5 项：时钟权威 B=client 计时+过期上报+Host 硬上限推荐/插入粒度=连携软窗片边界插入+契约硬停 Pause-Resume 分型推荐/输入锁暂停计时推荐/R-1 协议管道 B7 前定稿推荐/勾玉=按钮角标推荐；批次 R-1 协议→R-2 勾玉状态→R-3 窗口 UI→R-4 稻妻首单位）**；docs/11 新登记行+即时行动过期语义行将由 R-1 勘正销案（现行「滞留下回合」口径废除为「窗口外一律丢弃」）；docs/00 导航补 30+31（30 此前漏补）。行业结论修正=执行阶段按设计是 MOBA 型互动表现（离散点击+时限窗，非持续操作流——命令流+插入块可表达，无需 lockstep/预测）。编译 0 CS 错（唯一 console 条目=编辑器 AssetStoreDownloadManager 序列化噪声，非工程代码）。How to apply：用户「继续」=批7 HUD 交互；连携 31 稿 5 项待拍板（B7 前 R-1 必须定稿）；批4 对穿语义+批3 挂起①②④⑤仍开口；本批约 14 文件未提交等收尾。
- [2026-09-28 19:39:35] 【拖动瞄准大盘圆角矩形化已落地已提交推送 40d82ba（拆分提交：docs/18 仅本批 hunk 先行入库，AD 批5/6 与 ugui 修复随后由本会话收尾代收口 6cc7924/d76bd37）；承接 2026-09-26 拖动式 B4 条目）】拍板=用户原话「你只需要把拖动瞄准时，出现的大圆盘，改为圆角矩形盘，因为本项目的战斗场地是格子的」+描环重生成后拍板「用第一次生成的描环即可」。实现：①大盘素材=AI 生成 Resources/UI/Battle/DragWheelFill.png（实心填充）+DragWheelRing.png（细描环），管线=黑底文生图→亮度阈值 128→纯白化→裁剪到内容框（补偿恒 1.0）；小盘仍=disc.png×1.202。②**填充件内缩 11px 绘制**（新常量 大盘填充内缩）：两 AI 素材角弧不同（对角有效半径@680 填充≈56/描环带≈88），平齐绘制四角填充缘突出金框线外 ~9px（对角像素探针实测；识图不可靠、两次手算不一致后以像素探针定案）；内缩后唇口/空洞双零（全角度 0.1° 步进扫描，干净窗口 10~14 取中）——**换贴图必须重扫**（sweep.py=tmp/wheel_rect/）。③UpdateDragWheel 小盘夹取=两轴 Clamp（|x|,|y|≤盘半边），径向圆形夹取退役；十字步数/死区/指向锥/自适应位/挪键全不变。④字段 拖动瞄准大圆盘半径→拖动瞄准大盘半边（FormerlySerializedAs 迁移+prefab 重存新键，值 340 保真）。已知差异=新描环线宽 2.1%（680 下 14px）比旧圆形环 5.4%（36px）薄（SeeDream 定量控制偏差，v2 加粗版跑成 1.72 宽高比横条被弃）。circle.png 战斗消费方退役（TopBar 帧点保留）。落档=docs/18 决策六【】块+docs/17 速查行+gic-battle-hud skill 两行；HANDOFF 板=会话 AE。症状归因：盘角有暗唇/填充突出金框→查内缩 11 与 sweep；小盘斜向能到更远=矩形夹取预期。


- [2026-09-28 19:24:43] 【编辑器 ugui 包缓存丢失已修复（2026-09-28 报障「编辑器报错」）】症状=全项目几百条 CS0246（EventSystem/PointerEventData/Text 找不到）+CS2001 ugui 源文件找不到。根因=manifest 把 com.unity.ugui 写成 2.0.0 而 builtin 实为 1.0.0（71 包中唯一 manifest≠builtin 者也是唯一挂掉的）→ 在线重解析撞 packages.tuanjie.cn ECONNRESET → ugui 被剔除+PackageCache 目录被清 → 全下游级联。修法=manifest+lock 钉回 1.0.0（builtin 同版=纯离线解析零网络依赖，同 umc 项目写法）+重启；验证=解析 4.62s/72 包、UnityEngine.UI.dll+Assembly-CSharp.dll 编译成、后段零 error CS、Boot 场景正常。**How to apply**：①manifest/lock 的 ugui 1.0.0 修复混在工具链漂移（bridge 47f31100+ai.generators）里未提交，收尾随漂移一并提交、勿回退 2.0.0；②包缓存类报错先查 PackageCache 目录在不在+manifest 版本是否=builtin 版本（全案=docs/14 §99）；③重启后首轮编译几百条 CS 过渡噪声（包物化前）属预期，判据=后段 CompileScripts 干净+ScriptAssemblies 产物新；④GUIStateObj 刷屏=import 期噪声自停、Curl 35/licensing 反序列化错=Clash MITM 干扰编辑器 HTTPS 无害。
- [2026-09-28 22:57:41] 【安柏箭矢正式素材已落地（2026-09-28，v3 目检通过定稿；已提交推送 9e5f6ae）】拍板=方案 A 单图「屏幕平行布告板+屏幕平面内旋转」+AI 生成+元素色动态染色+四向全长（尾迹不做）。素材=Resources/UI/Battle/ArrowBolt.png（**v4 描边版 2026-09-28 追拍板「应当加上描边，让AI根据现在的加强描边」**：日辉按 v3 参考重生成=纯白箭身+粗深描边环绕全剪影（厚度≈杆粗一半），管线改绿幕抠底+亮度对比归一（箭身→纯白、描边→纯黑灰阶保 AA）——tint 乘法染色下描边恒黑不随元素色、箭身=精确元素色；512×121→512×102 同路径同 GUID 零接线、bounds 归一长度恒 0.49，导入回读 Sprite/Single/PPU100 全过；v1 光痕判废→v2 卡通粗箭判丑→v3 无描版→v4 加粗描边待目检）。实现=BattlePlayer.CreateProjectileVisual（cam.transform.rotation*Euler(0,0,屏幕投影角)；「箭矢长度」字段 0.49 格（0.7 目检拍板调小 30%，2026-09-28）按 bounds 归一；素材缺失回退白色光条占位）；元素色单源=命中箭 Damage.metadata（伤害元素）+消散箭 Effect.reactionKind（新双语义复用载荷，Host SkillHitResolver.ResolveProjectileElement=OnHit 首个 Damage 原子/Physical 回落施法者元素——丘丘人借凯亚霜袭=冰箭非物理灰）。症状归因：箭矢不显示→ArrowSprite 加载 Warn+回退光条；方向不对→屏幕投影角链；颜色不对→metadata/reactionKind 双路；改箭长=BattlePlayer「箭矢长度」脚本默认（运行时组件无冻结）。落档 docs/18 决策八箭矢条+docs/14 §93 补 Tuanjie 无 TextureImporter.spriteAlignment（Single 默认居中勿硬写）。

- [2026-09-28 22:48:23] 【安柏箭雨动画已落地（2026-09-28 晚，未提交待目检）】=时轮表现轨首个消费者（B-S3 ②）：SkillCast 命令→约定路径 Resources/Configs/Skills/{SkillName} 加载 SkillConfig→timeline→特效轨 cueName 开关台（Amber_ArrowRain.asset 新增 arrow_rain clip 0.30~0.75s；判定轨 LineBurst=节拍/格集单源：0.3+0.15×段、整线 step≥1 虚空截断同 CompileLineBurstSegment 口径、段数=DamageCount=4）。视觉=每段每格 2 支技能元素色箭（复用 ArrowBolt，from=落点上方沿相机右向侧移=屏幕恒斜落「箭雨落下倾斜角」默认 30°——2026-09-28 返修拍板「不应该竖直落下，应当是斜着落下」，落点/落地时刻不变）自 2.2 格高处斜坠、格内散布 ±0.3、逐箭错峰、落地与伤害数字同拍、落地沿箭轴压入插土（箭雨入土深度 0.15 格，屏幕布告板入土段被地形深度裁掉=尖插表面、尾翘起；水面格透水可见属预期）→原地滞留 3 秒（2026-09-28 拍板「箭落地后，应当插在表面3秒」）→淡出；参数=BattlePlayer Inspector「箭雨天降」段 11 字段（+箭雨落下倾斜角+箭雨入土深度/入土时长）。元素色=SkillCast.reactionKind **第三复用语义**=技能元素（Host ResolveProjectileElement 回填——命中箭/消散箭/箭雨三路同源）。连带：直击 Damage 数字 launchMs>0 到点再弹（对齐 Heal 分支；霜袭 startTime=0 零变化——仅箭雨 4 波数字从 stagger 改段时刻齐波）。fire-and-forget 不 gate 片 ack（S5 装饰尾巴口径）。症状归因：箭雨不落→查 Vfx cueName 与 SkillCast 分支；落太密/太慢→「箭雨天降」段参数；数字与落箭不同拍→直击 launchMs 节拍链。前批箭矢素材已被并行收尾批提交并重绘为 v3 原神风细长箭（9e5f6ae），本批消费同资产自动跟随。
- [2026-09-28 23:44:39] 【B-S4 立牌动作体系三条拍板（2026-09-28）+安柏待机立牌生成中】①素材统一朝右=项目级规范（立牌/特效等所有 AI 素材一律朝右，反向运行时水平镜像不生成第二份）；②安柏飞行循环 amber_fly_loop=「移动中」动作定调免重生成，缺口=待机/战技/爆发，序列=待机静态立牌目检→待机循环动画→战技/爆发；③立牌朝向随行动方向翻转：方向∈{上,左上,左,左下}→朝左镜像，其余→朝右（触发=战技/爆发射向+移动方向；运行时镜像接线=B-S4a 未落地）。规模化定调=全量视频路线（动作轨 Action=1 存而未接线、SkillCast=14 现成、同屏 6~10 路解码<250MB；骨骼动画与 AI 素材定调冲突不切）。安柏待机立牌迭代（2026-09-28 晚，进行中）：**待机姿态=按单位移动类型走（用户纠偏「安柏是飞行单位，不应该是站着的」）**——飞行单位=空中悬浮待机（双火翼展开+square_hd 方幅防裁翼），步行单位=站姿 portrait_16_9；站姿 v1（收翼）判废。悬浮 v2（Cards/amber.png 人物锚）目检报头顶飘带×3→**日辉局部擦除两连败：两次"仅擦除"指令均整图重绘（diff 21.8% 全区漂移）——2026-09-21 amber_glide 擦除一次过未复现，日辉擦除配方稳定性存疑（下次试 seedream 或降画幅）**→用户改向=**原神游戏内模型截图做人物锚**（用户自截 clipboard，正面全身、官方无翼需补翼）：配方=amber_glide 第一张（画风+火翼锚）+模型截图第二张（人物/服装锚；显式禁抄其正面站姿/空手/装饰背景）→v4 粗检达标待目检（唯一偏差=躯干偏正面约30°未到60°）。**像素核验方法论：原生透明图 bbox 判定必须用 alpha>16 阈值——getbbox() 把 1~16 级幽灵残影当触边误报裁切（v4 实证：α>0 报 L0/B0 触边，阈值后真实留白 14/41/16/33 全正；skill 固有画像条）**。中间产物+四行对比图=.codely-cli/tmp/arrow_outline/。落档 docs/18 立牌动作体系条+待机配方条+gic-paperdoll skill 朝向/待机姿态规则节。
- [2026-09-28 23:57:52] 【安柏待机立牌+待机循环动画已落地（2026-09-28，B-S4 序列前两件；未提交待目检）】用户拍板「这个作为安柏的待机立牌，接下来生成待机循环动画」：①amber_idle.png=v4 模型截图锚版（日辉双参考：amber_glide 锁画风+原神游戏内模型截图锁人物）目检定稿入库，UnitConfig 安柏 立牌图 amber_glide→amber_idle（视频失败回落立牌自此与视频同姿态）；②amber_idle_loop.mp4=H3-Max first_frame+循环节拍同配方（375 积分），78 帧/3.25s/768²=2 个 1.625s 悬浮周期，回绕差分 2.83=相邻均值 0.60×（优于 fly_loop 0.72），接线 立牌动画视频 槽（**fly_loop 退役留库=移动中素材，B-S4a 动作轨接线**）；③已知瑕疵=扇翼峰值帧右翼梢/下浮低点贴画布缘细条（约半数帧，像素取证=本体色非绿渣，同类 fly_loop 9/36）——目检不过则绿幕首帧横向占比降 0.80 重生成；④新坑=ffmpeg select 保留原 PTS 配 -r 用重复帧填 seek 空洞（dup=34 前段冻结）——循环段裁剪一律 -ss 输入端 seek，穿帮指纹=验证脚本检出 bbox 恒定/零漂移/接缝 0。工具链=tmp/amber_idle_anim/。How to apply：后续角色待机动画复刻本链（绿幕首帧 0.90/0.92 占比→H3-Max→extract_idle.py 验证→-ss 裁剪→接线）；安柏剩余动作=战技/爆发（B-S4 序列）。
- [2026-09-29 00:22:19] 【安柏待机循环动画 v3 终版已落库（2026-09-28 深夜，取代同日 v1 条目数字；未提交待目检）】v1（0.90 占比）回绕 0.13× 极优但翼梢贴边被用户实战目检否决→v2（0.80 占比+「翼尖不触碰边缘」提示词）翼梢仍越 76px 且回绕 2.66× 判死→**v3 终版（0.80 占比+回退 v1 原版提示词）：60 帧/2.5s/768²，零贴边（bbox 全程距缘 ≥31px），回绕差分 d(段末,首)=2.49 落相邻帧差带 [1.30,3.58] 内（0.70× 最大相邻步）**；同路径同 GUID 内容替换已入库（接线不变，refresh 0 错）。三条新方法论：①翼展 excursion **不随主体缩小等比缩**（614 宽仍越 76px）——绿幕首帧占比 0.80 起步、余量按代际绝对幅度留；②**提示词勿加边缘类空间约束**（实证破坏循环节拍遵循 2.66×）；③循环段选择须用**回绕口径** d(段末,首) 全段扫描（≠extract 周期对口径 d(i,i+k)；工具=tmp/amber_idle_anim/wrap_scan_v3.py）——extract 报 seam 高先扫回绕口径再判死。累计 1125 积分（375×3）；v1 回退件=tmp/amber_idle_anim/amber_idle_loop_v1_backup.mp4。目检不过升级选项：H3-Max 再 roll（375 方差彩票）/Seedance 2.5（1890 历史几何最优 0.41×+零贴边）。前条 v1 数字（78 帧/3.25s/回绕 2.83）作废以本条为准。
- [2026-09-29 00:31:39] 【安柏待机循环 v4/v5 两 roll 均失败停手（2026-09-29，承接 v3 条目；游戏内仍=v3）】v3 用户目检否决（「不贴边但顿挫感明显」）→拍板「Max 再 roll」：v4（0.80 同配方）回绕 0.18× 极优但翼弦横穿画布缘（触边行 run 216~560px=内容越界非收尖，**缩放补救不可行**；翼展开与好循环同簇=周期动作一部分，零贴边段全垃圾 5.2×）；v5（余量 0.72=107px）模型自放大主体（552→768 宽）+回绕 1.04× 双劣。**结论：H3-Max 待机=双彩票——循环缝 5 roll 中 2 好（0.13×/0.18×）皆越界、包络 3/5 坏，加余量防不住模型自改主体大小**。累计 1875 积分；待拍板：Seedance 2.5 一发 1890（历史 0.41×+零贴边；内容画风未目检过）/并行双 roll 750/回退静态立牌（清视频槽回落 amber_idle）/维持 v3。新工具=wingtip_probe.py（翼尖 run 长定性：短 run=收尖可缩放救、长 run=弦切判死）。
- [2026-09-29 00:39:27] 【安柏待机循环 v6 左移构图 roll：包络过线循环不过（2026-09-29，承接 v4/v5 条目；游戏内仍=v3）】用户观察「动画中左侧余量较多」拍板左移重生成→v6=0.76+左移 40px 非对称首帧（右余量 133px 供右翼、左 52px）：零贴边（bbox 最宽 740）=**非对称构图有效**；但回绕最优段 3.16/1.39×=v3 同档不过线不落库。**六 roll 全景定论：H3-Max 待机「好循环↔大翼」强绑定**——循环好签（v1 0.13×/v4 0.18×）全部翼越界（v4 级翼展开画布需求 ~800px）；包络干净签（v3/v6）循环全在 1.2~1.4×；非对称构图只能接住中量翼签。累计 2250 积分。待拍板：并行双 roll 750（v6 构架）/Seedance 2.5 1890/回退静态/维持 v3。
- [2026-09-29 00:47:44] 【安柏待机循环动画 v7b 终版已落库（2026-09-29，承接 v6 条目；未提交待目检）】用户拍板 A=并行双 roll+「v6 加了火焰特效，不要任何特效」→ v7=v6 构架（0.76+左移 40 非对称）+特效封禁提示词（画面只有角色本身，火焰翼仅作翅膀造型平涂不喷射）：v7a 1.92× 平庸；**v7b 中签=77 帧/3.21s/768²，零贴边（bbox 最宽 676 右余 92px）、回绕 1.11=0.53× 均值/0.37× 最大（低于 p25——比 v3 否决档 2.49 好 2.2×，循环点近无感）**；同路径同 GUID 已入库（接线不变，refresh 0 错）。累计 3000 积分（8 次生成）。**H3-Max 待机动画解法三件套沉淀：①非对称构图（右向 excursion 77px+/左向 ≤31px → 左移 40 补右翼余量）②特效封禁提示词（内容类约束不伤循环节拍、翼动作收敛）③并行双 roll（中签率翻倍，v7 一把平庸一把中签）**；约束分型铁律：空间类约束有害（v2 边缘指令 2.66×）、内容类约束无害（v7 特效封禁双签零贴边）。目检不过再议（Seedance 2.5 备选 1890）。
- [2026-09-29 00:51:11] 【安柏待机立牌+待机循环动画收官（2026-09-29 用户目检「验证通过。收尾」；随批提交）】v7b 终版即前条（77 帧/3.21s/零贴边/回绕 0.53×、无特效提示词版）；提交面=amber_idle.png+meta/amber_idle_loop.mp4+meta/UnitConfig 接线/docs 14§91连带二/17/18+CODELY.md。遗留：B-S4 序列下一件=战技/爆发动画（待用户启动，配方=gic-paperdoll skill 三件套直接复刻）；amber_fly_loop 留库=「移动中」素材待 B-S4a 动作轨接线（docs/11 已登记项）；8 次生成累计 3000 积分。
- [2026-09-29 01:05:01] 【B-S4a 移动态接线已落地（2026-09-29 拍板「其它动画先不做，现在正式化安柏的待机动画和移动动画」；未提交待目检）】新增 UnitData.移动动画视频 字段（null=待机常驻旧行为）+UnitView.SetMoveAnimation（幂等换片+随机相位；冻结/尸体由 Update 停摆接管）+BattlePlayer.PlayMoveCoroutine 移动片起止驱动（try/finally 不滞留移动态；被挡弹回段含在内）；安柏接线=移动 amber_fly_loop（退役留库件归位零重生成）+待机 amber_idle_loop——移动期间常驻播待机悬浮的问题消除。**⚠ BattlePlayer.cs 混改：本批 hunks+会话 AF 箭雨批未提交 hunks 同文件——收尾提交须 hunk 手术拆分或用户拍板合并**。遗留：方向镜像（拍板③）未接——朝左移动播朝右素材待后续批；ChromaKey 键色参数化仍开口。测试交用户（移动播滑翔/停步回待机/被挡弹回/步行单位无变化）。
- [2026-09-29 01:13:51] 【B-S4a 方向镜像已落地（2026-09-29 拍板③「根据移动方向改变立牌朝向：{上,左上,左,左下}→朝左、其它朝右（素材单份复用）；朝左走完待机也朝左；新加朝向字段；后续可能出背后刺杀技能」；未提交待目检）】UnitView.SetFacing/FaceLeft=朝向运行时字段（负 localScale.x 只翻 sprite+视频 quad，名字/Buff/底座不翻；行动后保持=待机延续；背刺技能届时读 FaceLeft、权威态随 B7 进快照）；BattlePlayer.IsLeftFacing 单出口（Direction2D Up/UpLeft/Left/DownLeft）两触点=PlayMoveCoroutine 片头+SkillCast 分支（移动与施放都随方向转向）；**ChromaKeyVideo.shader 补 Cull Off——负 scale 镜像翻绕向会被默认 Cull Back 剔成隐形（立牌恒面向相机无背面，零开销）**。测试交用户（左右移动镜像/待机延续/战技转向/文字不镜像/凯亚静态立牌同镜像）。
- [2026-09-29 01:25:29] 【拍板③方向镜像首测报障已修复（2026-09-29 凌晨；承接方向镜像条目；未提交待复测）】用户「看起来没有任何变化，另外使用战技或爆发也要会改变朝向」+现场取证授权。活体取证链：机制 ✓（反射合成 Move 命令调用 PlayMoveCoroutine 实证运行域方法体带钩子且翻转）→ 控制台施放日志交叉对照 → **U1 末次行动=向左移动但 faceLeft 快照=False 实锤**→ 根因=**Move 命令 direction 字段只被挡时填充**（TurnResolver 发射成功移动传 0）——段内时序 SkillCast(移动技能,方向=左)先正确转向左、紧随 Move(0→朝右)立即重置回右，全程看不到朝左。修法=TurnResolver 恒传 mover.Direction（被挡语义不变）+BattleCommand 字段矩阵 Move 行勘正。战技/爆发转向=SkillCast 分支钩子本就覆盖（日志实证 HiddenStrength/Sharpshooter/DoubleShot 施放均带真实方向），随修复一并生效。取证方法论：**朝向类报障=「末次行动方向 vs faceLeft 快照」交叉控制台施放日志**。How to apply：消费命令字段先核「恒填还是分支填」——发射侧分支传 0 是隐形重置源（字段矩阵勿只看类型行）。

### Reference
- [2026-09-16 20:01:18] MC mod gichess（旧项目，Java/NeoForge）：源码 D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\（308文件），jar D:\Tuanjie_editor\gic\.codely-cli\webrefs\genshin-unpack\gichess\my\wg-0.2.d（2026-09-05 随 gichess 77.78GB GI 解包归档整体迁入 webrefs/genshin-unpack/，原 D:\Picture\gichess）。~20+角色，7元素18反应，蒙德延奏/纳塔夜魂已实现。**Why:** GIC 战斗系统 Unity 移植的架构参考。**How to apply:** 仅作战斗系统架构参考；**旧 mod 资产一律不再用（2026-09-16 用户拍板「旧 gichess mod 不要再用」：播报员/派蒙语音 wav、模型、贴图等一切提取物都不再作为 GIC 素材来源，含 TTS 音色克隆样本；2026-09-14 已拍音频/曲目不翻旧 mod，音乐素材由用户自行网找）**。需要查旧 Java 实现时按路径阅读源码。



- [2026-08-10 09:28:39] 技术陷阱与 Bug 修复记录：详见 docs/14-技术陷阱与Bug修复记录.md。
- [2026-09-19 01:58:22] GIC 文档体系：**docs/17-代码架构指南.md=新会话入口文档**（目录结构/核心系统速查/场景清单/配置资产/工作流速查/战斗规划/环境备忘），开工先读它再按需深入。地图：00-13 玩法设计与大地图（07=07-势力机制/ 目录）、14 技术陷阱、15 输入系统、18 战斗决策、19 AI派蒙、20 项目规范（规范权威）、21 商业化与发布边界、23 UI架构重构、24 手势输入层重构、25 指令系统、26 GG系统、27 派蒙语音TTS；子目录=07-势力机制/、active/（22-战斗系统技术设计）、designs/（设计稿）、units/（角色文档）、archive/（16 优化计划已归档）。**How to apply:** 新系统落地/结构性变化后更新 docs/17 对应小节保持时效；UI 面板制纪律=docs/14 §37-39b+gic-new-screen skill（P1-P4 已收官）。**已否决的"故意不做"勿再提**：PopupManager 折叠进 UIManager、god-class Presenter 级拆分（依据 docs/23 §6 P4 行）。






- [2026-08-17 10:32:10] 本机 ffmpeg：D:\Tool\FormatFactory\ffmpeg.exe（7.1 完整构建）。PATH 里没有 ffmpeg，媒体处理直接用此路径。已用它重编码 mc_16x10.mp4 消 VideoPlayer WMF 双告警（baseline profile 消时间戳；VUI+容器双写 bt709 消色彩；x264-params 与 -color_* 需同时给）。






- [2026-09-12 14:24:31] 惯性化参考源码：.codely-cli/webrefs/animation-transitions/InertializationForUnity/（InertiaAlgorithm.cs=五次多项式+四元数/向量数学；上游 github.com/portalmk2/InertializationForUnity，MIT；一手信源=GDC 2018 talk "Inertialization: High-Performance Animation Transitions in 'Gears of War 4'"）。本项目 PetInertializer.cs 已大幅分叉（固定系数多项式+帧间 dq 速度捕获防 ±2π 抽搐+起步拉引），原源码仅作数学对照用。GitHub 拉源码法（api.github.com contents 端点 base64 解码落盘）见 gic-webrefs skill。**How to apply:** 改惯性化数学/移植其它 GoW 技术时先读这份源码对照。

- [2026-09-26 00:41:02] GIC 远程仓库：https://github.com/xiaoding521234/gic（2026-09-09 用户拍板转公开；首推 ~900MB pack 含美术资产）。push 策略=先查 Clash 健康复用直连/代理（2026-09-25 起直连多把推成实证；权威流程=gic-wrapup skill 收尾节），api.github.com 直连稳定；GitHub token 获取法见全局 git-pr skill（P/Invoke CredRead，仅进程内使用勿打印）。**SSH 备用推送通道（2026-09-22 探明未启用）**：https 443 被 SNI reset 时 ssh.github.com:443 与 22 端口仍通，本机已有 ~/.ssh/id_ed25519（注释 gic-push-20260922），启用需 GitHub 网页 Settings→SSH keys 手动加 id_ed25519.pub 后 push 改 ssh://git@ssh.github.com:443/xiaoding521234/gic.git。GitHub >50MB 文件仅告警、100MiB 硬限拒推（zh-cn SDF.asset=TMP 动态图集、每补字近整份新增 blob，唯一持续增长大文件；**2026-09-19 推送实测已 64.83MB 超 50MB 告警线（推成），距 100MiB 硬限余量收窄，LFS/filter-repo 决策窗口临近**；**2026-09-21 再入 43.5MB 派蒙待机 anim 文本资产（dd0d818，编辑器重序列化产物——filter-repo 瘦身时可判废弃重生成）+ 1.3MB 纸片人 png，体积压力加重，瘦身决策宜提前**）；2026-09-11 实测服务端 size 907.4MB，瘦身路径=filter-repo（DevKey 清洗有先例）+LFS。secret scanning+push protection 已开（私有个人仓该 API 返回 422，须先转公开再开）。**2026-09-07 DevKey 清洗（filter-branch）重写过 8/30 后的提交 SHA**——docs/skill 引用的旧 SHA 已失效，git show 失败按提交信息 grep 重定位。





- [2026-09-11 23:09:27] [2026-09-11] [reference] codely-docs.tuanjie.cn 文档站抓取法（2026-09-11 实证）：web_fetch 对该站深层路径会回错内容（FAQ 壳/相邻页缓存），正确姿势=curl.exe -sL 直拉原始 HTML（PS5.1 的 curl 别名是 Invoke-WebRequest，必须写 curl.exe；URL 会 301 补尾斜杠），正文在 <article>…</article> 内，PS 抽取去标签后可能残留 NUL 字节导致 read_file 报 binary，需 Replace([char]0,' ')。订阅/积分规则三页：/subscription/pricing-details（套餐价格+积分上限表+增值包）、/subscription/credits-description（积分消耗顺序+LLM/多模态单价表）、/subscription/plan-and-billing（升降级/发票/增购）。


- [2026-09-19 01:58:22] [reference] GI 官方提取模型站点（2026-09-15 定案：**用户指定站=The Models Resource**，models-resource.com/pc_computer/genshinimpact/，访问经验=web-access skill site-patterns/www.models-resource.com.md）：①TMR=最正统 rip 库（779 资产；CF 拦 curl 需浏览器、search 端点 404、手风琴需 CDP eval）②GameBanana（3DMigoto mod 形态）③Nexus Mods ④GIMI 生态（GitHub+Discord，最大提取/移植社区）⑤Sketchfab（骨骼保留参差+DMCA 下架风险）⑥Open3DLab 系（自定义绑定非官方骨架）。**关键判定：官方动作兼容=模型须保留 GI 原始骨架（Bip001 骨名/路径）——第三方站常被重绑定不兼容，套官方 .anim 最稳是本地解包自提（gic-gi-extract 流程）**。中文圈无稳定官方提取站（模之屋/44mmd=MMD）。派蒙来源=TMR asset/328738（docs/19 §2.1 头行已记载；MMD 兜底包=模之屋线，docs/19「模之屋原始 PMX 包」行无误勿改）。
- [2026-09-25 22:16:58] 【Kingdom Rush Genesis (KR6) 存档结构与改档工具链（2026-09-25 首次改档实证）】游戏=LÖVE(LuaJIT) 融合 exe，安装于 D:\Game3\Kingdom.Rush.Genesis.v1.00\。①存档位置=%APPDATA%\kingdom_rush_genesis\（v1.00 正式版；kingdom_rush_6_demo=旧 demo 遗档勿混），slot_1.lua 纯文本 Lua 表。②结构（反编译游戏自身代码证实）：levels[关卡1..19] = { ["stars"]=战役星数(0-3, 地图显示值, 有值=已通关), [mode id]=通关难度 }——mode id: 1=GAME_MODE_CAMPAIGN, 3=HEROIC, 5=IRON, 7+=no_heroes/blitz/kr1 等；星数=剩余生命≥18→3星/≥6→2星/否则1星；progression.last_stars=奖励处理游标（勿手改——游戏实时 count_stars=Σstars+star_counting_modes 每模式+stars_per_mode，奖励层=(last_stars, 当前总数] 自动弹领后自更新）；改 stars 后 last_stars 留旧值即可自愈；**英雄/法术等级=xp 换算**：hero_xp_thresholds={1000,4500,10500,19000,30000,45000,64000,88000,120000} 满级10=120000（已全12英雄实改）；powers_xp_thresholds={6000,16000,36000,72000,120000} 满级6=120000（已9法术实改）；技能/升级解锁=skills+upgrades_trees 另算勿与等级混。**模式 id 实值（constants.lua 反编译定案，2026-09-25 修正旧推断）**：CAMPAIGN=1, HEROIC=2, IRON=3, ENDLESS=4, NO_HEROES=5, EXTRA_HEROES=6, BLITZ=7, KR1=8；demo 存档 [1][3][5][7]=campaign/iron/no_heroes/blitz ✓。**解锁机制定案（全链反编译实证）**：①英雄：hero_data_free 全员 available_at_stage=3→通第2关全解锁；iap 英雄靠 is_content_stage_unlocked 的 exceptions 分支——Steam 版 is_premium() 恒返回 true+exceptions={"dlcs"}→**全解锁**；②塔/法术无 available_at_stage→is_content_stage_unlocked 恒 true；③**存档不存在任何 unlocked 字段**（demo trainer 与官方 unlock_all 作弊均不写）；④塔/法术/英雄的「锁」表现=progression_rewards 星数奖励未达标（premium 表：miners 30星/rhodes 40星/musketeers 42星/alchemist 46星/soaring_shop 48星/tree 50星/light_priestess 52星/myriath 54星/forger 56星/ignus 58星/impossible 难度 60星/thunder_zapper 62星/sniper 64星/illiana 66星/wintersongs_wrath 68星/oni 70星/aspect_of_sol 72星/drakkan 80星/ashbite 84星）——**解锁全部=把 count_stars 拉满+last_stars 留低值让奖励链自动弹出领取**；⑤is_stage_completed = levels[i] 存在且 #>0（# 只算数组键，{} 或纯字符串键表=未通关）；⑥**levels[70]={stars=72} 假条目方案**（已验证消费方全安全：count_stars pairs+ i<80 守卫专门容忍高键值/特殊关 99、9000 才被排除；unlock_ranges/get_slot_progress/achievements 均直接索引 1..18；ipairs 在 nil 处停）——2026-09-25 已落地 27+72=99 星。③改档铁律：改前 Get-Process 验游戏未跑（退出时会整档重写覆盖）；备份 slot_1.lua.bak-日期。④工具链（留档 .codely-cli/tmp/kr_extract/）：extract.js=Node 从融合 exe 尾部 zip 提取源码（找 PK\x05\x06 EOCD）；游戏脚本=LuaJIT 字节码（头 ESC "LJ"）——unluac(Java) 无用，ljd 有效（Dr-MTN/luajit-decompiler 已下载于 ljd_drmtn/，Python 3.12 已装 %LOCALAPPDATA%\Programs\Python\Python312\，用法 main.py -f in.lua -o out.lua -c）；字符串常量 rg 可直接搜字节码；游戏自带 cheat_completed_level/unlock_all 作弊代码在 sequels/game_gui_cheats_map.lua（正式版疑被门控）。⑤GitHub raw 被墙且 Clash 未开时，api.github.com 仍直连可用、jsdelivr CDN（cdn.jsdelivr.net/gh/user/repo@branch/path）可替代 raw 下载文件（实证 2026-09-25）。
- [2026-09-25 23:23:56] 【EGamePlay 参照仓库正主勘误】docs/17 与 docs/active/29 所引 "qq362946661/EGamePlay" 已 404 失效；正主=github.com/m969/EGamePlay（2026-09-25 api.github.com 实证：2380★、MIT、fork 545、2026-09 仍活跃推送，topics=buff/skill/unity；另有 m969/AOGame=基于 ET 的续作 120★）。本地源码快照在 .codely-cli/webrefs/skill-timeline-system/EGamePlay_src/（拍平文件，AbilityEffect=EcsEntity+运行时组件化 EffectDamageComponent 等——与 GIC EffectCompiler 编译期展开的对照基准，2026-09-25 审查已核：GIC 选择更贴回合制）。引用时按 m969/EGamePlay 写。
- [2026-09-27 00:31:27] 【多模态积分单价快照（2026-09-27 抓 codely-docs/subscription/credits-description；模型档位=价格信号）】图/张：frontier 家族（日辉/耀斑）1K=75 全站最贵（4K=175）；Seedream Pro 1K=45（2K=180，IP 角色被版权拦截）；混元3.0=60；Qwen 1K=35（2K=60/4K=100）；Seedream lite=35；frontier_lite 25~40。sprite_sequence=150/次（16 帧固定）；图片超分=10/次；Qwen 分层=25/次；Pro 分层额外每张 45/90。视频 5s：Seedance2.5@720p=1890 > 2.0@720p=1245 > 2.0fast=750 > H3-2K=750 > H3-Max@768P=375 ≈ H3@768P=375 > Wan3.0=225 > Seedance mini=250 > HappyHorse=150。3D：Tripo P2 图生=1000 > P2 文生=850 > P1 图生=300 > P1 文生=250；Rodin/混元3.1=150。**How to apply：模型选型/强度对比/成本预估先查此快照勿重复抓页；价格随上游变动、隔久需重抓核对。**
