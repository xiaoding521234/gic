## Codely Structured Memories







### User
- [2026-09-11 23:12:13] [2026-09-11] [user] 用户 Tuanjie AI 订阅=个人版 Max（月积分 160,000；三层滚动上限 5h=8,000/周=40,000/月=160,000；闲时=每日除 11:00-12:00、14:00-18:00 外，积分消耗减半即 token 翻倍；订阅积分月度发放不结转，增值包积分 365 天有效不清零；**增值包是否计入 5h/周/月速率上限文档未写明**，2026-09-11 核对定价页上限表口径为"每个套餐…可使用的积分上限"，倾向计入但不确证，建议撞限想靠增值包续命时先问客服）。**Why:** Max=付费订阅用户，TJGenerators 的订阅路由/高成本门按付费用户处理；周上限 40,000 是最常撞的节流阀（2026-09-11 用户实证撞周上限）。**How to apply:** 大批量生成（视频/3D/Pro 图）前先估积分是否撞周上限；被上限卡住时建议挪闲时（消耗减半）；计费规则页=codely-docs.tuanjie.cn /subscription/pricing-details。
- [2026-09-13 18:26:52] [2026-09-13] [user] 输入类 UI 用户要求 IDE 式体验（2026-09-13 指令输入拍板原话「就像我在idea里写代码那样，能够tab自动补全一个词」）：文本输入凡有可枚举词汇域（指令/物品名/角色名）应配补全列表——输入即出、Tab 补当前词、↑↓ 切换选中、点击行补全。**How to apply:** 后续新输入类 UI 直接沿用 InputPopupDialog 命令模式（Show 的 suggester 参数 + CommandSystem.Suggest 或自定义提供器），勿做裸输入框。
- [2026-09-19 01:57:26] 音乐选曲委托流程（2026-09-14 拍板）：用户报曲名/截图清单 → AI 从 qjjlb.quanjian.com.cn/musicdl **QQ 源 FLAC 无损优先**（网易 320k 仅缺曲兜底并注明）→ **原生响度直转不归一**（仅解包源才 loudnorm 对齐）→ libvorbis q8 落 Resources/Audios/Daytime|Night/{theme 前缀} → 按前缀填 PositionConfig 曲池。全流程按 **gic-music-dl skill** 执行勿再问勿再归一；站点 API=web-access skill site-patterns/qjjlb.quanjian.com.cn.md；FLAC 母带存 .codely-cli/webrefs/music-masters/。




### Feedback








- [2026-08-14 22:58:07] 用户授权 AI 主动维护项目 skill：完成阶段性工作或发现高频工作流/陷阱密集的系统时，可自行新增/更新 .codely-cli/skills/ 下的 skill，无需逐次确认。**How to apply:** 主动沉淀但避免与项目记忆重复——记忆记事实与决策（为何），skill 记操作流程与检查清单（怎么做）；不确定价值时先问。
















- [2026-09-19 01:57:26] 【桥/编辑器异常恢复（2026-09-18 按桥 1.0.85 合并定稿）】①编辑器没开/桥死：拉起编辑器（D:\Tool\2022.3.62t11\Editor\Tuanjie.exe -projectpath 'D:\Tuanjie_editor\gic'）→ 等心跳出现（**1.0.85 心跳={项目根}\Temp\.com-unity-codely.json**，冷启动约 1~2 分钟）→ unity_refresh 重连；心跳仍 stale 用最小化+还原焦点循环（SW_MINIMIZE=6→2s→SW_RESTORE=9+SetForegroundWindow）。②排查顺序=WMI 验编辑器进程身份（tuanjie.exe 可能是 Tuanjie Hub 启动器，须查 CommandLine 含 Editor\Tuanjie.exe -projectpath）→ 查 Temp 心跳新鲜度（netstat 端口 LISTENING 指向死 pid=幽灵端口，以心跳为准）→ unity_refresh；旧 Copy-Item 副本桥接方案已废勿再用。③编辑器启动首次编译的 error CS0006（@1.0.81 旧包路径）=PackageCache 残留过渡噪声——后段 CompileScripts 干净即无害。④编辑器 License 过期弹窗：点 Exit 重启自动续期（重启前确认场景 dirty=false）。⑤exec_runtime_script 结束 Play/域重载后桥推 state=stale + custom_tools_reloaded 属**常态**：unity_refresh 重连即可，勿当故障。









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
- [2026-09-14 01:39:44] 【UI 结构改动交付必附目检清单+报障活体诊断法】P2 面板化连续 4 回归实证（关闭残骸/入场瞬完成/闪现一帧/尖峰全屏闪烁，docs/14 §37+§38），P3 再添两例（祈愿按钮全消失=目标位池化二次缓存污染 §39、毛玻璃"反而更亮"=双层渲染顺序 §38b）：结构化断言全绿≠视觉正确——6 个回归全部通过 19~36 项断言，均靠用户目检发现。**Why:** 断言盲区客观存在（组件级查不出 Canvas 残骸、时序类查不出渲染闪烁、位置类查不出动画到不了位）。**How to apply:** ①UI 结构类改动交付时主动列目检清单（开有动画无闪烁/关彻底消失返回上级/反复开关正常/层级遮挡正确/关键控件在位），不等用户撞上；②**用户报障且编辑器还开着时，先用 exec_runtime_script 反射读取现场状态取证**（anchoredPosition/缓存目标位/层级顺序/激活态）再推理——§39 靠"三个目标位恰为序列化位±200"一发实锤，远快于理论推演。**③现场修复授权（2026-09-14 三连轮实证：音乐无声踢活轮换链/退出无反应运行时补挂 EventSystem/拖拽失效关挡板 raycastTarget）**：用户报障时明示「游戏正在运行，你可以现场取证」——取证定位后**现场最小修复（写操作）也在此授权内**，用户三案全接受且同局继续玩；模式=只读取证→现场修解燃眉→永久修复落盘（编辑器在 Play 中 refresh 被阻）→用户退 Play 后 AI 编译验证+交复测清单。边界不变：主动测试/无报障的运行时验证仍交清单，勿以此条反推可自跑 harness（与全局「测试一律交用户」条目的分工：该条管主动测试，本条管报障响应）。

- [2026-09-13 01:35:13] 系统性碎片化债务的处置偏好=趁项目小做"业界最优"大重构，优先于最小收敛（2026-09-13 原话"我支持大重构，趁现在项目还小，采用业界最优方案"；手势层议题我列 A 不动/B 最小收敛/C 大一统三案并推荐 B，用户拍 C）。**Why:** 与 UI P1-P4 大重构、桌宠窗口体制照 VPet/eSheep 源码逐条重写一脉相承——趁债务未滚大一次性对齐业界。**How to apply:** 诊断出"多处手写同族机制"类碎片化时，方案清单把业界最优大重构给足权重（勿只推最小收敛）；三条护栏不变——既有已调参数保留覆写、调研先行拿一手信源、每阶段目检交用户。
- [2026-09-13 03:15:45] [feedback] 【UI 视效/动画配方跨屏统一优先于因地制宜变体】（2026-09-13 联机毛玻璃实证：我给 Coop 自创"tint 含变暗+整体 CanvasGroup 淡入"单层变体（注释里还标了"与背包双层配方的差异点"），用户目检打回原话"毛玻璃动画效果不对，看看设置界面和背包界面，应当统一"——根因=变暗随 blur 一起扫入+半透明"湿玻璃"感，与既有屏的双层配方（变暗瞬时+纯模糊实心扫入）观感割裂。**Why:** 既有屏已目检验收的观感=事实标准，自创变体即使"等价"也是割裂。**How to apply:** 新界面做动画/视效前先对照既有屏配方逐参核对（tint/BackDim/驱动量），能复用就复用、结构不同才做"变体"且变体须保观感恒等（§38b 视觉恒等式）；配方变体沉淀进 gic-new-screen skill 双层配方段。
- [2026-09-13 12:23:13] [feedback] 【找素材先查项目内已有资产，勿急着外部提取/裁剪】（2026-09-13 加载页素材实证：我准备从原神解包 AssetMap 找徽标+裁用户截图，用户两次纠正——"本项目已经有徽标资源了""看看祈愿里卡池界面，那里用了势力徽标"。**Why:** 项目素材地图：Assets/Resources/UI/Other/Faction/=势力徽标八套（蒙德/璃月/稻妻/须弥/枫丹/纳塔/至冬/坎瑞亚，祈愿卡池界面在用）；Element/Deep|Stroke/=七元素官方图标（ElementFactionConfig 引用）；Other/logo.png="原神棋"文字主视觉（Splash 启动屏用），勿与徽标混淆。**How to apply:** 任何图标/徽标/背景需求，先 ElementFactionConfig 与 Resources/UI 目录扫一遍+问用户，再考虑 gi-extract/截图裁剪；徽标类默认 Faction 目录。
- [2026-09-13 19:05:54] [2026-09-13] 【指令/控制台文案=开发者风格，不写玩家向描述】用户拍板（2026-09-13 原话"很多描述是不必要的，指令主要是面向开发者，而不是给广大玩家看"）：指令回执/错误/help 描述/补全 hint 一律短、干、事实化（如「原石 +100（持有 16100）」「未知物品: x」「开始自动抽卡 ×N」）；错误提示不再罗列全部合法值与教学句（补全与 help 本身就是发现手段）；派蒙的口吻由 LLM 转述层自加（工具描述写明"回执是开发者格式，请翻译成派蒙口吻"），回执层保持原始事实。**Why:** 指令系统定位=开发/作弊工具非玩家功能，冗余描述是噪声。**How to apply:** 后续新增指令、调试面板、控制台类工具的文案全按此基调；不要写"您可以…""支持…如…"式教程腔与拟人化语气词。
- [2026-09-13 22:58:06] 【存档零迁移政策·0.5.a 前】用户拍板（原话「无论是主游戏存档还是ai派蒙存档，都不需要有迁移，老版本存档直接删掉重建即可」）：0.5.a（正式落地版本号，届时用户主动告知）前，主存档与桌宠 pet.json 一律不写迁移——schema 语义变化=只升版本号+旧档删掉重建。**版本号改动权在用户（2026-09-13 追加拍板「规范加一条，ai不要修改版本号」）：AI 不自行修改 CURRENT_SAVE_VERSION/pet.json 版本门——升版=删所有旧档属破坏性决策，语义变化时列变更清单提请用户拍板升版**。主存档早已是重置式（2026-08-16 起，SaveManager「版本过低不迁移直接建档」）；pet.json 存量 v6→v8 链（PetPrefs.ParseAndMigrate：chatCiphers 分槽/quickMessages/你好）与主存档两处旗标迁移（MigrateFateItems/key 分槽）为历史代码，处置待拍板。**Why:** 开发期无真实玩家，旧档可丢，迁移=无谓工时；升版即删档故须用户把关。**How to apply:** 今后任何存档 schema 变更只升版本号+删档重建，勿写迁移链；升版本身也由用户拍板、AI 勿代改；已落档 docs/20 §1.6 + gic-save-system skill「版本策略」节。
- [2026-09-16 21:58:35] 【AI 编译验证自主权·编辑器没开/停在 Play 也一样】编译验证始终 AI 自行完成——编辑器没开也直接按「桥/编辑器异常恢复」①拉起编辑器→等 Editor.log CompileScripts→rg "error CS" 查错→同步心跳副本，全程无需询问用户；**编辑器停在 Play Mode 时自行 stop 退出再 refresh 编译验证**（2026-09-16 用户拍板原话「之后你可以自行退出play」）；运行时/Play 测试仍归用户。**Why:** 2026-09-13 用户确认原话「文档或记忆里应当记录有，你可以自行完成编译」。**How to apply:** 交付前编译验证是硬边界，编辑器关闭/桥死不是跳过编译或交给用户的理由，按流程自行恢复环境完成；本会话 23:2x 实证全流程可行。


- [2026-09-15 01:17:42] [feedback]【依据 docs 答来源/现状类问题必须整节通读，禁 offset 截窗起读】（2026-09-15 派蒙来源连环误答两轮实证）：用户问「派蒙模型来源于哪个网站」，我 read_file docs/19 时 offset=35 恰好切掉 L34 头行「当前生效：GI 官方模型（2026-08-24 落地，来自 models-resource 完整 rip，asset 328738）」，误锚到下文资产表的「模之屋原始 PMX 包」兜底行→首答错称模之屋；用户纠正「TMR 正是我当初下载派蒙的网站」后，我又拿「项目 PMX 包与 TMR 条目格式对不上」顶回去一轮；用户再纠「当前项目在用的不是MMD模型」才定位——答案在文档里 8-24 就写对了。**Why**：截窗漏读一行小节标题→整条证据链锚错；且与用户记忆冲突时未先回读全文就质疑用户。**How to apply**：①答「X 来源于哪/现状是什么/在哪」前，从节标题起完整读 docs 相关小节，勿用 offset 从中段起读；②用户纠正我的事实性答案且与文档记载冲突时，先重读文档原文核对，再决定是否质疑用户记忆。
- [2026-09-20 00:16:57] 【四元数/骨架空间约定问题直接移植现成工具，勿手搓试错】（2026-09-20 用户叫停原话「这样修改会不断有新问题出现，找找有没有现成的工具」）：GI muscle→hips 旋转我手搓公式连错两版（倾斜→平躺），每次只修一个症状。**Why:** 骨骼旋转的空间约定（质心系 vs 节点系、Z-up vs Y-up bindpose、RootQ vs 本地Q）是多层嵌套的隐式约定，靠目检反馈逐个试错每轮只暴露下一层错误；成熟工具实现是自洽约定系，移植后问题收敛为「一个已定位的差异点」。**How to apply:** ①涉及骨骼旋转/坐标系的转换，先搜现成实现（Ruri.RipperHook 的 HumanoidToGeneric 模块=Unity muscle clip→generic 全流程，BA 的 HumanoidAnimationBaker 也是完整参照），核心求值逻辑整段移植、只剥离宿主依赖（AssetRipper 类型等），勿重写数学；②移植后若仍有输出差异，定位为「一个约定差异点」用数据诊断（FK 算理论值对比）一次修正，勿连环改公式；③搜现成工具的手法：GitHub code search 用 distinctive symbol 名（HumanoidAnimationBaker/AvatarMuscleReferential/HumanoidClipGenericizer 这类自造词一搜一个准），泛关键词（genshin animation blender）反而全是噪声。
- [2026-09-21 00:55:54] 【Unity 新资产目录入库必须显式 add 文件夹 meta】`git add Assets/Art/<新目录>` 只暂存目录内文件与子 meta，**父级的 `<新目录>.meta`（文件夹 guid）不在暂存范围**——2026-09-21 PaperDoll.meta 因此漏提交，amend 补救（前提=尚未 push；已 push 则需追加提交）。**How to apply:** 新建 Assets 子目录入库时提交清单三件套=目录+目录内文件（含各自 .meta）+父级目录的 <目录>.meta；提交前用 git status --porcelain 扫一遍是否残留未跟踪 .meta。

### Project
















- [2026-09-12 14:24:31] Assets 清理定案（2026-08-16）：Material/Materials、Resources/Materials 三目录 8 个材质误删已 git 恢复，**勿再清理**（UIBlur/WishSmoke 在用，其余存疑也保留）；已删定案：Assets/Editor 空目录、Scenes/TestScene.scene、StreamingAssets/mc.mp4、Assets 根 6 个 CUsers* 垃圾文件。故意不做：NewAudioMixer、Art/Wish 与 WishArt 改名（Addressables 地址约定）；保留 GlowTest.scene+CardGlowTest.cs、兹白测试皮肤 zibai_skin_solid.png（UnitConfig 兹白 cards[1]，用户拍板不删勿当 bug 修）。**How to apply:** 不再动这批资产。










































































- [2026-09-12 14:20:49] 【搜索/文本判定铁律】search_file_content/glob/list_directory 受 .codelyignore+.gitignore 双层 ignore（Assets 的 png/prefab/asset/mat/wav、Mirror/kcp2k/Plugins 整目录、.codely-cli/.codely/Library/Temp/Logs/Export/Maps 等）——被任一层命中即**静默零命中/无名**，零命中≠不存在：查引用/资产内容/目录清单/skill 文件一律原生 `rg --no-ignore` 或 Get-ChildItem；喂多模态的 Assets 图片先复制到 .codely-cli/tmp；guid 判定一律 AssetPathToGUID/unity_asset get_info（meta guid 可能密文，文本反查假阴性，docs/14 §9）；.unity 与本地化表中文为 \uXXXX 转义，rg 中文直搜必零命中（搜转义形式或先解码，docs/20 §1.3）；.codely-cli/ 与 TextMesh Pro/ gitignored（skill/HANDOFF 勿提交；TMP 材质改动不入库，重装前备份）。细节 docs/14 §6.2。
- [2026-09-12 19:05:04] [2026-09-12] 【exec_editor_script 桥脚本 Newtonsoft 撞名坑】桥内联脚本里用 `Newtonsoft.Json.Linq.JObject` 必报 CS0433（JObject 同时存在于 Newtonsoft.Json 与 Unity.Localization.ThirdParty.Editor 两程序集，脚本环境无法 extern alias）。**Why:** 2026-09-12 烘焙验证脚本实证。**How to apply:** 桥内联脚本一律不用 JObject.Parse/ JsonConvert——计数/取字段用 Regex.Match 原生字符串，或返回原始 JSON 字符串在 AI 侧解析；项目内 .cs 编译不受影响（只用一份）。
- [2026-09-19 01:57:26] 手势输入层 P1-P4 已全量收官（2026-09-13；架构与 P4 勘定=docs/24 §7.11、速查=docs/17 手势层行、调研信源=.codely-cli/webrefs/gesture-input/）：PointerInputPump 归一→GestureHub（三门+first-accept-wins 仲裁，门可按面豁免）→识别器四件套（纯 C# 可离线断言）。**后续新交互面=实现 IGestureSurface 挂识别器（B6 格点点击=TapRecognizer 或 Drag OnSlop 的 OnTapCandidate；卡牌长按=LongPressRecognizer 首消费者）；DragRecognizer 要短点击必须显式传 emitShortTap:true（构造参默认 false，docs/14 §65）；阈值一律 GestureMetrics 勿散写。**

- [2026-09-19 01:57:26] 【全屏 UI 排版=比例锚点方案】：子节点一律 y 比例锚点（anchorMin.y=anchorMax.y=从底百分比、anchoredPosition=0），不用固定 px——用户 Game 视口常非 16:9（CanvasScaler MatchWidth=0 时 Canvas 宽恒 2560、高随视口变），固定 px 会漂 2~3%。对齐参考图的方法=像素扫描块区间（非白像素按行分段）反推位置，识图百分比仅作参考。

- [2026-09-13 14:03:16] [2026-09-13 13:2x] 【已落地 docs/14 §46/§47】UGUI 层级重组三条陷阱（anchor 参照系不迁移/单边锚 pivot 轴心/inactive 不能 StartCoroutine）与协程宿主腰斩陷阱（嵌套 StartCoroutine 不转移宿主——清栈 SetActive(false) 会腰斩全链致锁泄漏）均已写入 docs/14 §46/§47。**How to apply：** 程序化 prefab 重构/转场协程编写前先读这两节。
- [2026-09-13 18:17:17] [project] 用户会并行开多个 AI 会话在同一项目分工开发（2026-09-13 实证：另一 AI 在写 Framework/Command 指令系统）。**How to apply:** ①git status 里非我产生的改动/WIP 文件（尤其未跟踪新目录）属其他会话在制品——勿动勿清理勿"顺手修"，除非它把整个程序集编译堵死才做最小语法解锁并明确告知；②编译错误可能来自其他会话 WIP，先归因再动手；③refresh/构建等编辑器级操作可能与另一会话冲突，用户可能取消——被取消后勿立即重试，问协调节奏。
- [2026-09-19 01:57:27] [project] 并行 AI 会话协调板=.codely-cli/HANDOFF-并行AI协调.md：多会话并行开发时**回合开始先读**该文件——记录各会话文件归属（勿改他方文件）、状态与编辑器使用权；状态变化写回各自小节；refresh/编译/构建等编辑器级操作动前先看对方状态避免撞车。


- [2026-09-13 18:26:49] [2026-09-13] 【Play Mode 断言动真实存档必须快照+恢复】exec_runtime_script 里测 give/card 等改存档指令时，编辑器 Play 用的就是玩家真实档（LocalLow/HGAME/gic/gic_save.json），删档测试模式是否启用勿假设（EditorPrefs 键名未核验）。**Why:** 2026-09-13 指令系统 38 断言若不恢复将永久污染 1600+ 原石、误发角色卡——靠"测试前 GetItemCount/ownedUnits count 快照 + finally ModifyNow 恢复（未拥有条目=Remove，count=0 复活态=还原 0）+ RebuildOwnedCards"保住原值。**How to apply:** 任何 runtime 断言触碰 SaveManager 前先搭快照/恢复骨架再写断言；TimeUtility 偏移同样测试尾 reset。
- [2026-09-13 18:31:11] [2026-09-13] 【exec_runtime_script 结束后编辑器留在 Play Mode】脚本正常完成后 Play 不自动退出（2026-09-13 两次实证：其后 unity_editor.refresh 报 refresh_blocked_in_play_mode）——需要编译/编辑器级操作时先手动 unity_editor stop 再 refresh。**How to apply:** runtime 断言跑完紧接要 refresh 时，直接先 stop 免一次失败调用；桥随后推 stale+custom_tools_reloaded 属常态，unity_refresh 重连即可。
- [2026-09-13 18:58:20] [2026-09-13] 【§39 活体诊断补遗：暂停现场须先 resume + DontDestroyOnLoad 盲区】用户暂停编辑器（isPaused=true）时报障，exec_runtime_script 握手直接 Operation timed out（错误不指向原因）——先 unity_editor resume 再跑取证脚本，读完停 Play 或按需恢复；且 unity_scene get_hierarchy 只见当前场景根，池化面板/常驻管理器在 DontDestroyOnLoad 的 GameScene/UIRoot 下看不见，须 FindObjectsByType(FindObjectsInactive.Include) 反射读 alpha/activeSelf/_isClosing 等。**Why:** 2026-09-13 半透明弹窗幽灵案实证——resume 后一发读到 alpha=0.292 冻结+_isClosing 实锤（陷阱本体已落 docs/14 §50）。**How to apply:** 用户报障"游戏已暂停可直接读取"类现场，按 resume→反射取证→stop 的顺序。
- [2026-09-13 19:56:28] [2026-09-13] 【exec_runtime_script harness 反射两坑】①懒建/构建后存在的私有字段（_inputField/_slashPanel 类）必须在触发构建之后**每断言点现取**（GetField().GetValue）——在 WireHost 前/首次输入前顶部缓存=null→NRE 或断言假失败（聊天框 slash harness 两版各踩一次，烧 3 轮排障）；②无参私有方法反射调用须 `Invoke(ctrl, null)`——传 `new object[]{null}` 直接报 parameter count mismatch。**Why:** harness 脚本时序 bug 极易误判成产品 bug，浪费整轮工具调用。**How to apply:** 起草 runtime harness 按"构建→现取→断言"顺序；断言失败先加 try/catch 打印完整内部堆栈定位归属（脚本 vs 产品），再修。

- [2026-09-13 23:31:42] 【prefab 中文序列化字段名=大写转义】prefab YAML 里中文序列化字段名以大写 hex 转义存储（如 GlassPanelAnimator 的模糊层字段="\u6A21\u7CCA\u5C42"）——rg 直搜中文字面与小写 "\u6a21" 形式均零命中，搜转义必须大写。**Why:** 2026-09-13 核验 CoopScreen.prefab 动画器接线时小写转义连搜三轮全空，改大写一发命中。**How to apply:** 文本反查 prefab 接线时，把 .cs 字段名转成大写 \uXXXX 再 rg -F --no-ignore 搜（穿透 .codelyignore/.gitignore 双层 ignore）。

- [2026-09-19 01:57:27] [project] 星落湖战斗音乐与解锁现状（2026-09-14 落定）：蒙德野外（MondstadtWilds=4）曲池已全量接线（day 9 曲+night 7 曲含星知晓的旧梦，全部 QQ FLAC 原生直转，清单在 PositionConfig 配置资产）；战斗音乐=轮换链（播完→10s→按战斗独立时钟重选池：初始 6:00、回合结束+20min、边界 8:00-20:00——**开局 6:00 落夜晚池**，改原神式 6-19 边界待用户反馈）；星落湖 isUnlocked=1 已解锁。**遗留：MapConfig 星落湖锚点仍占位 (70,28) 待用户取点器「锚点重定位」标定（标定前坐标是假的勿当实数据）——涉星落湖锚点的工作先核标定是否已完成。**

- [2026-09-19 01:57:27] [project] UnityInsight 索引系统档案（活锁 bug 已提交 Bug Hunter，证据包=.codely\有效bug活动\已提交\index-build-failure）：架构=CLI 侧 node 守护进程（unity-insight-cli.js serve --daemon）持有全部索引，**被动模式**——杀掉不自动重生、重生后须 Cowork GUI 发构建指令（编辑器 AI/ 菜单只有 Check Connections/Force Reload）；数据目录=<项目>\.codely-cli\UnityInsight\（构建中=index.db.tmp+WAL，ready 后写 index.current 指针）。活锁特征：first_build 磁盘写入 ~6 分钟后冻结、进程 4~5 核满负荷+RSS 狂涨至 7.5GB+零 I/O、index_building=true 永不翻转、GUI 无进度无报错；~\.codely-cli\crash-logs\exit-*.json（uptime<1s）=单实例锁握手记录属常态勿误判崩溃。处置=Stop-Process 杀 daemon+清 tmp 三件套。

- [2026-09-20 20:13:55] [project] Bug Hunter 证据包与提交状态（快照；**权威状态板=.codely\有效bug活动\00-新会话交接总览.md，新会话先读它，本条只当指针**）：2026-09-20 20:15 快照——本周（一9/14~日9/20）累计提交 **11 件**（9/14 七件 + 9/20 四件），名义 33,000 超 2 万周上限、超出部分结算以周二公布为准；**不正确\ 撤案 2 件**：core日志跨日不滚动（翻案=core 按 UTC 日滚动，本地 08:00 切文件，教训=判日志机制先看行内时间戳时区+找边界对）+ 增值包速率上限口径未写明（用户判定不交）。**已立包待交 3 件（9/20 晚）**：popup-pipe-chars（OS 通知弹窗内容渲染为竖线串"|||..."，日志侧内容正常、7 次发送时刻与报障吻合，差弹窗截图）+ image-link-cdn-not-local（AI 生成图给 CDN 链接不给本地 file:/// 链接，体验缺陷，无需截图可交）+ credit-false-insufficient（同批并行两张生成一过一拒，报"需 165 积分"但用户实测 5h 剩~7000/周剩~2 万；积分拒绝日志零留痕，差余额页截图）。周二（9/22）结算后提醒用户查邮箱、兑换码一周内核销。活动规则与四件套流程=全局 skill codely-bughunter。









- [2026-09-19 01:57:27] [project] 加载页势力徽标动画模式已定稿（2026-09-15 蒙德落地）：连通域拆层=底图静止+动层运行时连续旋转（LoadingOverlayDriver「徽标分层表」，转速默认 -40°/s 可调；未配势力=整标缓转零破坏）；官方徽标非旋转对称处（六叶=镜像三对 53°/74° 交替的纯静态设计）做循环动画须均匀 60° 重排（用户拍板「不用像素级对齐，大致即可」——帧循环必有接缝，运行时连续旋转优于帧序列）。工具链=.codely-cli/tmp/mond-anim/（split_v3.js/rebuild_blades.js/preview.js）。**后续势力徽标动画沿用此模式。**


- [2026-09-19 01:57:27] [project] 【AI 生成 sprite-sheet 实证结论】generate_sprite_animation 两次生成均未过像素验收（循环接缝 IoU 仅 0.603、静止区 7% 像素逐帧抖动、叶片形变）——**扩散模型无法同时满足「外框像素级静止+部件精确旋转+无缝循环」三条件**。**How to apply:** 「部件动而外框静」类动画需求直接走拆层/合成路线（见徽标分层条目），勿再尝试 AI 逐帧生成；AI 生成适用于无静止参照的全新动效。


- [2026-09-20 01:27:48] 【GI 动画提取：全世代身体骨均无法通过 AnimeStudio 直接导出（2026-09-20 终局修正）】**重大修正**：之前「新角色 ACL 直存 TRS 全解全出」结论错误——Odette FBX 的 138 paths 全部是物理/装饰骨（Breast/Hair/EyeBone/Bone_Sleeves 等），身体骨（Spine1/UpperArm/Thigh）无 m_LocalRotation 绑定。之前 bodyPaths=54 的统计误将「路径包含 Spine 关键词」当作「身体骨自身有动画」（实际是子孙物理骨的祖先路径含关键词）。**所有世代角色的身体骨都由 humanoid muscle 系统驱动，AnimeStudio ReadCurveData 的 else 分支全部丢弃**——不是代差，是同一种限制。新角色只是物理骨更多（138 vs 29）让人误以为更多数据被导出。**muscle 求值路线（Ruri 工具链）已走通但产出幅度不够**（呼吸级 ±1-2°）。**战斗表现需换方向**：Mixamo 重定向/纸片人/其他方案。技术成果保留=AnimHarness bake 模式+Ruri 工具链移植+DBACL 修复，见 webrefs/gi-animation-extraction/README + docs/14 §68。













- [2026-09-19 01:58:22] 【派蒙语音现状与路由】游戏侧设计（三模式：关闭/本地侧车 127.0.0.1:9880/云端 MiniMax 自填 key；零进包；口型同步不做；覆盖=对话+抽卡反应+兜底全念、/指令回执不念）=docs/19 §6.5.11，P1a 已交付用户验证通过；**游戏外全档（模型谱系/训练/评测/侧车运维/yaml 绝对路径铁律/训练纪律三不/待办）=docs/27，语音话题先读它**。现状：用户拍板「先采用 A 组零训练优化，停止训练」（v4fullc e2 检查点保留，重启=train_pm_v4.py --skip-prep，A 组四件套全交付）；**等用户耳检 compare\paimon_compare_{reftable,grid,nbest}.wav 三份拍板**：情绪参考表哪些档采纳（游戏侧接线 paimon\refs_table.json）、n-best 守卫是否产品化；B 组（s1 补训/rank 升档）备选。侧车 9880 现挂 v4full e8 真权重、start_paimon_tts.bat 拉的是 v2 yaml（玩家默认=v2，勿混）。发布期议题=派蒙语音包可选组件安装器（4-6GB）或云 key，届时再拍。

- [2026-09-19 01:58:22] 【B5 判定体系已拍板】放弃队形→重叠格心+两态模型（执行阶段收拢重叠=特效锚格心、选择阶段自动散开复用展开布局）；格=交互粒度、**受击体=立牌真实大小圆柱**（底座圆盘可视化）与格脱钩；移动中单位连续插值位置可被中途命中（所见即所得）；接触判定与效果作用域解耦；纯瞬发仍按片初快照。**B5 开工先读 active/22 §11 修订版**（规则本体已落档 docs/04/05/18+提交 eab2a20）；代码仍是队形旧实现，B5 落地时改（RefreshAllFormations 阶段切换驱动/GetFormationOffset 复用/圆柱受击体/命中点连续坐标载体勿双端各自推算）；圆柱容错直径、穿透模型 B4/B5 落地时拍。**战斗表现层/判定任务一律按新体系实现，勿再按旧队形/格判定区方案写代码。**

- [2026-09-19 01:58:22] 【战斗 HUD v1 定案】设计稿=docs/designs/battle-hud-v1.html、拍板=docs/18 决策六+active/22 §13、操作流程=gic-battle-hud skill。布局=MOBA 范式（移动左下/爆发右下/战技左弧+延奏上弧/右上取消钮，布局参数全 [SerializeField] 中文命名）；单位选择交互=点立牌选中→技能盘现+手牌藏（互斥）、点空白取消；立牌=饥荒式斜插卡片（顶部远离相机后仰，后仰角=俯角 55° 时面正对视线完全消压扁，BattlePlayer [SerializeField] 立牌后倾角可调）；技能区数据链=UnitData.skills→SkillData 按 SkillType 分拣+SkillDetailPanel.prefab 复用；**拖动式瞄准=B4 未实现（点击式三情况已全链实现）**；灰盒 BattleDebugPanel 已删。

- [2026-09-19 01:58:22] 【战斗三班收官：HUD 补全+灰盒撤除+B2 Buff+B4 技能首批】现状/剩余/简化口径=docs/17 §7 已落地增量行+active/22 §10，新会话先读；待拍板项（超时=Pass/战斗 SFX 资产/血条敌我色 B7 重定/DyedElement 出生附着语义）登记 docs/11；头顶信息与立牌同平面倾斜（用户拍板「都应当斜」）。**SkillFactory 未注册类→UnimplementedSkill 占位保 skillIndex 对齐（null 跳过会让后续索引整体前移错位）**。B4 目检链（交用户）：凯亚战技打芭芭拉→冻结 2 回合（冰色+行动落空+冰图标+2）；安柏先火附丽莎→凯亚再打→融化大伤害；安柏战技=光条飞行后命中；爆发=整线多目标。提交时 BattleHud.cs.meta+新文件 .meta 一并 git add。


- [2026-09-19 01:58:22] [project] 【战斗表现件三件套纪律】后续新表现件一律走 BattlePalette（配色收口，改全局战斗配色=BattlePalette.asset）/BattleViewFactory（世界 quad+Unlit 材质+世界 TMP 唯一出口，材质调用方持有+OnDestroy 释放）/BattleViewTween（补间收口，末帧保证 t=1），勿再手搓 quad/色值字面量/while 循环；TextMesh 全退役→世界 TMP（细节=docs/14 §63⑤+gic-battle-hud skill）。**四技能键全同构**（用户拍板「技能按钮应当统一，移动是特殊的技能」：数据驱动 skills[Move]，图标/名称随 normalMoveType 步行/飞行/两栖，移动专位左下）。待拍板遗留：#7 HUD prefab 化（建议与 B6 执行阶段 HUD 变化/入退场动画同批）。



- [2026-09-19 01:58:22] [project] SkillDetailView 点外关闭竞态已修（战斗实例设 `点外关闭=false`，点外收面板由 OnBoardTap 承接；教训=同一交互目标被两个系统响应必须一方显式让位，勿依赖帧内执行顺序——全案 docs/14 §64b）。**背包场景同款竞态（点源图标面板闪动重开）仍存在未修——用户未报障勿主动动。**

- [2026-09-19 01:58:22] [project] BattleHud 结构已收敛：表驱动四键（SkillButtonDef+RegisterSkillButton 加键=加一行；AimMode 枚举已删）+partial 三分件（主/TopBar/Build）——**文件地图/加键 checklist/勿当 bug 修清单/活体取证时序全在 gic-battle-hud skill，战斗 HUD 话题先读它**；受控复现基线逐项一致（战技 22 格/移动 24 格/面板开合/取消钮）。
- [2026-09-20 02:43:41] 【搜索铁律补遗③（2026-09-20 复验修正）+ 模板 using 已清理】search_file_content 的 glob=锚定于搜索根的 gitignore 式语义：含 / 的模式须从搜索根写起（从工作区根搜 Assets 下 UI 目录须 **/UI/**/*.cs）——9/19 所记「UI/**/*.cs 静默零命中」实为锚定语义误解非工具 bug，**已判死勿再当 bug-hunter 候选提交**（9/20 复验：**/*.cs、**/*.{cs}、*.cs 全正常命中）。**How to apply:** 目录范围仍优先 path 参数逐目录搜；glob 零命中先加 **/ 前缀或换 path 复核再下结论。~~全项目 .cs 文件头批量模板 using 使 grep 依赖审计失效~~ → **已修复（2026-09-19 13:0x）**：三轮语义分析清理 291 文件 867 条，Unity 编译逐轮 0 错——`using GIC.X` 逐文件检索恢复可用作依赖方向审计依据（清理后实证：Battle→UI 仅 Card 策略族 5+BattleHud 复用族 2 全真实、Tool→Battle 清零、Framework→Battle 仅 3 处网络/池真实引用）；**例外=17 个豁免文件**（玩家侧 #if×16+CardGlowOverlay 混合换行）仍带模板头，审计到它们仍须核类型实际使用。清理工具链四坑+安全网套路=docs/14 §66。


- [2026-09-19 18:50:54] 【战斗系统全量架构审查+修复+B3 归位（2026-09-19；View 层深检补遗已交付：表现件纪律全绿、BattlePlayer 播放链=祈愿标杆达标样本）】总评：主干健康——Host 权威+片级命令流+三层时间模型与 docs/active/22 逐条一致，B7 换 Mirror 路径可信；系统性风险=效应→命令缺对账机制。**审查批已修并提交推送（用户验证通过）**：7b8ea20（片内 Heal 命令补发+MergeHealEffects；删 SkillContext/死 API Execute）、9482251（模板 using 语义清理 867 条，grep 依赖审计恢复可用；工具链四坑=docs/14 §66）。**B3 归位批已提交（2026-09-19：3953fd9 归位+a7cf05c .gitignore）**：①CardManager/CardDeck/DeckCodeCodec/UnitManager 迁 Framework/Collection（.meta 成对保 GUID、命名空间不变）+SkillManager 空壳删（Wargame 双处摘除）——Framework/Battle 目录消亡；②Direction2D/ForceType(+Extensions) 迁 Data/Battle/Protocol、TeamType 从 UnitIdentity 抽出迁 Data/Battle/Unit（命名空间全改 GIC.Data）——**Data→Battle 反向依赖清零（rg 实证）**；③SkillFactory None→占位（索引对齐）；④docs/17 §2 依赖方向表补记 UI→Battle、docs/11 登记同片合并口径；.gitignore 加 /chat-export-*。**仍登记未修**：docs/11 项（效应→命令对账机制 B6 前/ElementAttach 接线/同片合并口径拍板/B7 同构债三处/组合根域内化/ExitDialog B6）+ DamagePipeline 静态 hooks 生命周期（B2/B4 接线时）、快照纪律靠约定（hooks 接线批次一并处理）。
- [2026-09-19 18:50:57] 【战斗 B5 连续判定核心已落地（2026-09-19 晚，编译 0 错，未提交未目检）】投射物命中=接触立牌圆柱之时+读命中时刻连续插值位置（移动中可中途射中）：ProjectileResolver 时间轴分段求交（Host 在同片移动展开后判定；平局 unitID 升序；虚空截断/24 格消散产 Effect 命令）；命中点/消散点=千分定点 hitX/hitY 随命令下发（客户端零推算）；BattleMetrics 常量收口（投射物 8 格/秒/移动 0.18s/格/圆柱直径 0.42，Host 与播放同源）；底座 Quad→圆盘=判定可视化。**行为变化**：贴脸同格敌 t=0 即命中（旧不打同格）；投射物播放与移动同 t=0 起跑；箭雨/霜袭仍纯瞬发按片初快照（不变）。**Why**: docs/active/22 §11 基线+docs/18 决策二 2026-09-17 拍板的实现落地。**How to apply**: 后续战斗表现/判定话题先读 docs/17 §7 B5 增量行+active/22 §11 实现要点；圆柱直径调=改 BattleMetrics.UnitCylinderDiameter 一处；B5 剩余=箭矢素材/天降视觉/震屏闪白/池化/立牌美术升级（待拍板）。
- [2026-09-21 01:13:52] 【战斗表现纸片人方案与素材路线定案】（2026-09-20 用户拍板）GI 动画提取判死后战斗表现=纸片人方案；**GI 七圣卡面 Spine 素材路线被否决**——用户发现卡面人物只有半身/坐姿场景图不完整（"角色只有半身，因此我们仍然需要自己用ai生成"），纸片人素材改 AI 生成（安柏滑翔立牌 2026-09-21 终版定稿=提交 ba3d847：**v4 基版+AI 局部擦除中上多余第三翼**——用户拍板「其它完全不改动」局部擦除优于全新生成，v5 重新生成版退役；擦除配方=gic-paperdoll skill「局部擦除/修订」节；站姿 v2 定基准；生成工作流+立牌设计规则已沉淀 .codely-cli/skills/gic-paperdoll/，后续角色直接复用）。Spine 技术管线已验证保留：skel 4.0 解析器（spine-csharp 4.0.64 移植，573191/573191 EXACT，关键坑=大端 float/bezier 曲线每通道 4 float/strings 表+ReadStringRef/deform 帧 time2 先于 curve byte）、AtlasDump harness（MonoBehaviour 原始字节 dump，PPtr 布局=4B fileID+align4+8B pathID，atlas 数据=纯文本内嵌）——工具链存 .codely-cli/tmp/paperdoll_amber_gcg/ + webrefs/spine-paperdoll/，将来做 GI 素材骨骼动画可复用。**How to apply:** 战斗单位表现素材走 AI 生成立绘；GI 提取素材只做图标/头像/卡面类完整资源。


- [2026-09-21 00:29:53] 【AI 出图模型规则两条（安柏纸片人实证 2026-09-20~21）】①Seedream 5.0 Pro（260628）出高还原 IP 角色必被输出侧版权审查拦截（OutputImageSensitiveContentDetected，与 prompt 措辞无关，两次实证，失败不扣积分）——GI 角色素材用标准 seedream 或 frontier_sunburst，勿选 Pro。②日辉原生透明底=fal 后端须传 **is_segmentation:true**（服务端映射 background:transparent+nativeSegmentation:true）；传自定义 background:"transparent" 无效（键不出现，出白底）。透明底固有 alpha 画像=背景约 60% alpha=0、主体 129-254 桶无 255 全实像素（中心约 253），v2 即如此非缺陷；seedream 的透明底是另一路径（segmented/ 后处理抠图 URL）。**How to apply:** 后续角色素材（凯亚/芭芭拉等）原生透明底一律 is_segmentation:true；查生成用模型/底色=chats/<会话>.jsonl 任务 JSON type 字段+下载后读 PixelFormat。




### Reference
- [2026-09-16 20:01:18] MC mod gichess（旧项目，Java/NeoForge）：源码 D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\（308文件），jar D:\Tuanjie_editor\gic\.codely-cli\webrefs\genshin-unpack\gichess\my\wg-0.2.d（2026-09-05 随 gichess 77.78GB GI 解包归档整体迁入 webrefs/genshin-unpack/，原 D:\Picture\gichess）。~20+角色，7元素18反应，蒙德延奏/纳塔夜魂已实现。**Why:** GIC 战斗系统 Unity 移植的架构参考。**How to apply:** 仅作战斗系统架构参考；**旧 mod 资产一律不再用（2026-09-16 用户拍板「旧 gichess mod 不要再用」：播报员/派蒙语音 wav、模型、贴图等一切提取物都不再作为 GIC 素材来源，含 TTS 音色克隆样本；2026-09-14 已拍音频/曲目不翻旧 mod，音乐素材由用户自行网找）**。需要查旧 Java 实现时按路径阅读源码。



- [2026-08-10 09:28:39] 技术陷阱与 Bug 修复记录：详见 docs/14-技术陷阱与Bug修复记录.md。
- [2026-09-19 01:58:22] GIC 文档体系：**docs/17-代码架构指南.md=新会话入口文档**（目录结构/核心系统速查/场景清单/配置资产/工作流速查/战斗规划/环境备忘），开工先读它再按需深入。地图：00-13 玩法设计与大地图（07=07-势力机制/ 目录）、14 技术陷阱、15 输入系统、18 战斗决策、19 AI派蒙、20 项目规范（规范权威）、21 商业化与发布边界、23 UI架构重构、24 手势输入层重构、25 指令系统、26 GG系统、27 派蒙语音TTS；子目录=07-势力机制/、active/（22-战斗系统技术设计）、designs/（设计稿）、units/（角色文档）、archive/（16 优化计划已归档）。**How to apply:** 新系统落地/结构性变化后更新 docs/17 对应小节保持时效；UI 面板制纪律=docs/14 §37-39b+gic-new-screen skill（P1-P4 已收官）。**已否决的"故意不做"勿再提**：PopupManager 折叠进 UIManager、god-class Presenter 级拆分（依据 docs/23 §6 P4 行）。






- [2026-08-17 10:32:10] 本机 ffmpeg：D:\Tool\FormatFactory\ffmpeg.exe（7.1 完整构建）。PATH 里没有 ffmpeg，媒体处理直接用此路径。已用它重编码 mc_16x10.mp4 消 VideoPlayer WMF 双告警（baseline profile 消时间戳；VUI+容器双写 bt709 消色彩；x264-params 与 -color_* 需同时给）。






- [2026-09-12 14:24:31] 惯性化参考源码：.codely-cli/webrefs/animation-transitions/InertializationForUnity/（InertiaAlgorithm.cs=五次多项式+四元数/向量数学；上游 github.com/portalmk2/InertializationForUnity，MIT；一手信源=GDC 2018 talk "Inertialization: High-Performance Animation Transitions in 'Gears of War 4'"）。本项目 PetInertializer.cs 已大幅分叉（固定系数多项式+帧间 dq 速度捕获防 ±2π 抽搐+起步拉引），原源码仅作数学对照用。GitHub 拉源码法（api.github.com contents 端点 base64 解码落盘）见 gic-webrefs skill。**How to apply:** 改惯性化数学/移植其它 GoW 技术时先读这份源码对照。

- [2026-09-21 00:55:54] GIC 远程仓库：https://github.com/xiaoding521234/gic（2026-09-09 用户拍板转公开；首推 ~900MB pack 含美术资产）。push/pull 走 Clash 代理 127.0.0.1:7890，api.github.com 直连；GitHub token 获取法见全局 git-pr skill（P/Invoke CredRead，仅进程内使用勿打印）。GitHub >50MB 文件仅告警、100MiB 硬限拒推（zh-cn SDF.asset=TMP 动态图集、每补字近整份新增 blob，唯一持续增长大文件；**2026-09-19 推送实测已 64.83MB 超 50MB 告警线（推成），距 100MiB 硬限余量收窄，LFS/filter-repo 决策窗口临近**；**2026-09-21 再入 43.5MB 派蒙待机 anim 文本资产（dd0d818，编辑器重序列化产物——filter-repo 瘦身时可判废弃重生成）+ 1.3MB 纸片人 png，体积压力加重，瘦身决策宜提前**）；2026-09-11 实测服务端 size 907.4MB，瘦身路径=filter-repo（DevKey 清洗有先例）+LFS。secret scanning+push protection 已开（私有个人仓该 API 返回 422，须先转公开再开）。**2026-09-07 DevKey 清洗（filter-branch）重写过 8/30 后的提交 SHA**——docs/skill 引用的旧 SHA 已失效，git show 失败按提交信息 grep 重定位。



- [2026-09-11 23:09:27] [2026-09-11] [reference] codely-docs.tuanjie.cn 文档站抓取法（2026-09-11 实证）：web_fetch 对该站深层路径会回错内容（FAQ 壳/相邻页缓存），正确姿势=curl.exe -sL 直拉原始 HTML（PS5.1 的 curl 别名是 Invoke-WebRequest，必须写 curl.exe；URL 会 301 补尾斜杠），正文在 <article>…</article> 内，PS 抽取去标签后可能残留 NUL 字节导致 read_file 报 binary，需 Replace([char]0,' ')。订阅/积分规则三页：/subscription/pricing-details（套餐价格+积分上限表+增值包）、/subscription/credits-description（积分消耗顺序+LLM/多模态单价表）、/subscription/plan-and-billing（升降级/发票/增购）。


- [2026-09-19 01:58:22] [reference] GI 官方提取模型站点（2026-09-15 定案：**用户指定站=The Models Resource**，models-resource.com/pc_computer/genshinimpact/，访问经验=web-access skill site-patterns/www.models-resource.com.md）：①TMR=最正统 rip 库（779 资产；CF 拦 curl 需浏览器、search 端点 404、手风琴需 CDP eval）②GameBanana（3DMigoto mod 形态）③Nexus Mods ④GIMI 生态（GitHub+Discord，最大提取/移植社区）⑤Sketchfab（骨骼保留参差+DMCA 下架风险）⑥Open3DLab 系（自定义绑定非官方骨架）。**关键判定：官方动作兼容=模型须保留 GI 原始骨架（Bip001 骨名/路径）——第三方站常被重绑定不兼容，套官方 .anim 最稳是本地解包自提（gic-gi-extract 流程）**。中文圈无稳定官方提取站（模之屋/44mmd=MMD）。派蒙来源=TMR asset/328738（docs/19 §2.1 头行已记载；MMD 兜底包=模之屋线，docs/19「模之屋原始 PMX 包」行无误勿改）。


