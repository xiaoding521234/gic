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
- [2026-09-21 23:07:50] 【方向类改动目检清单必须显式含 4 斜向用例】（2026-09-21 撞墙弹回实证）：弹回首版方向换算误用十字归一映射 SkillHitResolver.DirectionToDelta，直向用例全过、斜向弹回方向变十字——由用户实测「凯亚斜走水面」才发现；我交付的 5 项目的检清单只写"选移动方向"没列斜向。**Why:** 8 向矩阵中斜向是独立实现分支（StepVector/DeltaToDirection 各自的斜向 case），直向通过≠斜向正确，方向 bug 极易只炸斜向。**How to apply:** 凡 Direction2D/方向换算/移动/瞄准/弹道类改动，目检清单必须显式列「四个斜角各一」用例；陷阱本体与映射选用规则已沉淀 docs/14 §69。
- [2026-09-23 21:37:43] 【技能参数 baseType 结算层必须消费，勿只看展示拼接】（2026-09-23 用户纠偏原话「你弄错了含义！配置文件里的10，还要看基于类型。拼接后为10%移速」）：MoveDistance=10 标注 BasedOnMoveSpeed 的真实含义=**10%×移速换算**（安柏 50→5 格、凯亚等 30→3 格），非直读 10 格——我在 B-S1b 直读了数值被纠偏。**Why**：SkillParam 的 value+baseType 是"数值+解释基准"二元组，展示层（SkillDescriptionBuilder 拼「10%移速」）与结算层（换算）必须同语义；只复刻展示而忽略基准即错。**How to apply**：读技能参数做数值结算前先看 baseType——BasedOn*（攻击/移速/生命…）=按对应属性换算、Fixed=直读、Percent=语境百分比；新参数设计时基准选错会在结算层埋雷，AI 实现技能数值时必须核 docs/units 参数表的基准列。同轮教训=移动瞄准米字 8 向（技能描述全员「十字方向其一」）——**实现瞄准/方向/范围类逻辑前先读技能描述原文，勿按引擎能力（8 向步进）默认**。

### Project
















- [2026-09-12 14:24:31] Assets 清理定案（2026-08-16）：Material/Materials、Resources/Materials 三目录 8 个材质误删已 git 恢复，**勿再清理**（UIBlur/WishSmoke 在用，其余存疑也保留）；已删定案：Assets/Editor 空目录、Scenes/TestScene.scene、StreamingAssets/mc.mp4、Assets 根 6 个 CUsers* 垃圾文件。故意不做：NewAudioMixer、Art/Wish 与 WishArt 改名（Addressables 地址约定）；保留 GlowTest.scene+CardGlowTest.cs、兹白测试皮肤 zibai_skin_solid.png（UnitConfig 兹白 cards[1]，用户拍板不删勿当 bug 修）。**How to apply:** 不再动这批资产。










































































- [2026-09-12 14:20:49] 【搜索/文本判定铁律】search_file_content/glob/list_directory 受 .codelyignore+.gitignore 双层 ignore（Assets 的 png/prefab/asset/mat/wav、Mirror/kcp2k/Plugins 整目录、.codely-cli/.codely/Library/Temp/Logs/Export/Maps 等）——被任一层命中即**静默零命中/无名**，零命中≠不存在：查引用/资产内容/目录清单/skill 文件一律原生 `rg --no-ignore` 或 Get-ChildItem；喂多模态的 Assets 图片先复制到 .codely-cli/tmp；guid 判定一律 AssetPathToGUID/unity_asset get_info（meta guid 可能密文，文本反查假阴性，docs/14 §9）；.unity 与本地化表中文为 \uXXXX 转义，rg 中文直搜必零命中（搜转义形式或先解码，docs/20 §1.3）；.codely-cli/ 与 TextMesh Pro/ gitignored（skill/HANDOFF 勿提交；TMP 材质改动不入库，重装前备份）。细节 docs/14 §6.2。
- [2026-09-23 00:04:38] 【exec_editor_script 桥脚本坑四条】①内联脚本用 Newtonsoft JObject 必报 CS0433（与 Unity.Localization.ThirdParty.Editor 撞名，脚本环境无法 extern alias）——计数/取字段用 Regex.Match 或返回原始 JSON 字符串在 AI 侧解析；②写本地化表的条目类型=嵌套类 SharedTableData.SharedTableEntry（裸名 SharedTableEntry CS0246、SharedTableData.TableEntry CS0426 均不可用）；③字符串内勿用反斜杠转义引号（JSON 传输层变字面反斜杠炸 CS1056），拼接一律用 "+" 运算；④写 zh-TW 表时 LocaleIdentifier.Code="zh-TW" 非 "zh-Hant"。②③④宜并入 gic-localization skill 陷阱节（下次相关会话顺手补录）。

- [2026-09-19 01:57:26] 手势输入层 P1-P4 已全量收官（2026-09-13；架构与 P4 勘定=docs/24 §7.11、速查=docs/17 手势层行、调研信源=.codely-cli/webrefs/gesture-input/）：PointerInputPump 归一→GestureHub（三门+first-accept-wins 仲裁，门可按面豁免）→识别器四件套（纯 C# 可离线断言）。**后续新交互面=实现 IGestureSurface 挂识别器（B6 格点点击=TapRecognizer 或 Drag OnSlop 的 OnTapCandidate；卡牌长按=LongPressRecognizer 首消费者）；DragRecognizer 要短点击必须显式传 emitShortTap:true（构造参默认 false，docs/14 §65）；阈值一律 GestureMetrics 勿散写。**

- [2026-09-19 01:57:26] 【全屏 UI 排版=比例锚点方案】：子节点一律 y 比例锚点（anchorMin.y=anchorMax.y=从底百分比、anchoredPosition=0），不用固定 px——用户 Game 视口常非 16:9（CanvasScaler MatchWidth=0 时 Canvas 宽恒 2560、高随视口变），固定 px 会漂 2~3%。对齐参考图的方法=像素扫描块区间（非白像素按行分段）反推位置，识图百分比仅作参考。

- [2026-09-13 14:03:16] [2026-09-13 13:2x] 【已落地 docs/14 §46/§47】UGUI 层级重组三条陷阱（anchor 参照系不迁移/单边锚 pivot 轴心/inactive 不能 StartCoroutine）与协程宿主腰斩陷阱（嵌套 StartCoroutine 不转移宿主——清栈 SetActive(false) 会腰斩全链致锁泄漏）均已写入 docs/14 §46/§47。**How to apply：** 程序化 prefab 重构/转场协程编写前先读这两节。
- [2026-09-23 00:04:38] [project] 用户会并行开多个 AI 会话在同一项目分工开发：**回合开始先读 .codely-cli/HANDOFF-并行AI协调.md**（各会话文件归属/状态/编辑器使用权，状态变化写回各自小节）。git status 里非我产生的改动/WIP 文件（尤其未跟踪新目录）属其他会话在制品——勿动勿清理勿"顺手修"，除非它把整个程序集编译堵死才做最小语法解锁并明确告知；编译错误可能来自其他会话 WIP，先归因再动手；refresh/构建等编辑器级操作动前先看协调板避免撞车，被取消后勿立即重试，问协调节奏。




- [2026-09-23 00:04:43] 【exec_runtime_script 运行时脚本坑五条（2026-09-13 实证）】①动真实存档的断言必须先快照+finally 恢复——编辑器 Play 用的就是玩家真实档（LocalLow/HGAME/gic/gic_save.json，删档测试模式是否启用勿假设）：GetItemCount/ownedUnits 快照→恢复（未拥有条目=Remove、count=0 复活态=还原 0）+RebuildOwnedCards；TimeUtility 偏移测试尾 reset。②脚本正常完成后 Play 不自动退出——紧接要 refresh/编译先手动 stop（桥随后推 stale+custom_tools_reloaded 属常态，unity_refresh 重连即可）。③用户暂停现场（isPaused）时握手直接 Operation timed out 不报原因——先 unity_editor resume 再取证。④池化面板/常驻管理器在 DontDestroyOnLoad 的 GameScene/UIRoot 下 get_hierarchy 看不见，须 FindObjectsByType(FindObjectsInactive.Include) 反射读。⑤懒建/构建后才存在的私有字段每断言点现取（顶部缓存=null→NRE 或假失败）；无参私有方法反射调用=Invoke(ctrl, null)，传 new object[]{null} 报 parameter count mismatch。断言失败先 try/catch 打印完整内部堆栈定位归属（脚本 vs 产品）。





- [2026-09-13 23:31:42] 【prefab 中文序列化字段名=大写转义】prefab YAML 里中文序列化字段名以大写 hex 转义存储（如 GlassPanelAnimator 的模糊层字段="\u6A21\u7CCA\u5C42"）——rg 直搜中文字面与小写 "\u6a21" 形式均零命中，搜转义必须大写。**Why:** 2026-09-13 核验 CoopScreen.prefab 动画器接线时小写转义连搜三轮全空，改大写一发命中。**How to apply:** 文本反查 prefab 接线时，把 .cs 字段名转成大写 \uXXXX 再 rg -F --no-ignore 搜（穿透 .codelyignore/.gitignore 双层 ignore）。

- [2026-09-23 00:04:49] [project] 星落湖 isUnlocked=1 已解锁；蒙德野外曲池（day 9/night 7，QQ FLAC 原生直转）与战斗音乐轮换链已全量接线（清单在 PositionConfig 配置资产）；战斗开局 6:00 落夜晚池（改原神式 6-19 边界待用户反馈）。**遗留：MapConfig 星落湖锚点仍占位 (70,28) 待取点器「锚点重定位」标定（标定前坐标是假的勿当实数据）——涉星落湖锚点的工作先核标定是否已完成。**


- [2026-09-19 01:57:27] [project] UnityInsight 索引系统档案（活锁 bug 已提交 Bug Hunter，证据包=.codely\有效bug活动\已提交\index-build-failure）：架构=CLI 侧 node 守护进程（unity-insight-cli.js serve --daemon）持有全部索引，**被动模式**——杀掉不自动重生、重生后须 Cowork GUI 发构建指令（编辑器 AI/ 菜单只有 Check Connections/Force Reload）；数据目录=<项目>\.codely-cli\UnityInsight\（构建中=index.db.tmp+WAL，ready 后写 index.current 指针）。活锁特征：first_build 磁盘写入 ~6 分钟后冻结、进程 4~5 核满负荷+RSS 狂涨至 7.5GB+零 I/O、index_building=true 永不翻转、GUI 无进度无报错；~\.codely-cli\crash-logs\exit-*.json（uptime<1s）=单实例锁握手记录属常态勿误判崩溃。处置=Stop-Process 杀 daemon+清 tmp 三件套。

- [2026-09-23 00:03:26] [project] Bug Hunter 提交活动：权威状态板=.codely\有效bug活动\00-新会话交接总览.md（涉提交/查已立包状态先读它）；活动规则与四件套流程=全局 skill codely-bughunter；2026-09-20 轮结算积分已到账、闭环。











- [2026-09-19 01:57:27] [project] 加载页势力徽标动画模式已定稿（2026-09-15 蒙德落地）：连通域拆层=底图静止+动层运行时连续旋转（LoadingOverlayDriver「徽标分层表」，转速默认 -40°/s 可调；未配势力=整标缓转零破坏）；官方徽标非旋转对称处（六叶=镜像三对 53°/74° 交替的纯静态设计）做循环动画须均匀 60° 重排（用户拍板「不用像素级对齐，大致即可」——帧循环必有接缝，运行时连续旋转优于帧序列）。工具链=.codely-cli/tmp/mond-anim/（split_v3.js/rebuild_blades.js/preview.js）。**后续势力徽标动画沿用此模式。**


- [2026-09-19 01:57:27] [project] 【AI 生成 sprite-sheet 实证结论】generate_sprite_animation 两次生成均未过像素验收（循环接缝 IoU 仅 0.603、静止区 7% 像素逐帧抖动、叶片形变）——**扩散模型无法同时满足「外框像素级静止+部件精确旋转+无缝循环」三条件**。**How to apply:** 「部件动而外框静」类动画需求直接走拆层/合成路线（见徽标分层条目），勿再尝试 AI 逐帧生成；AI 生成适用于无静止参照的全新动效。


- [2026-09-20 01:27:48] 【GI 动画提取：全世代身体骨均无法通过 AnimeStudio 直接导出（2026-09-20 终局修正）】**重大修正**：之前「新角色 ACL 直存 TRS 全解全出」结论错误——Odette FBX 的 138 paths 全部是物理/装饰骨（Breast/Hair/EyeBone/Bone_Sleeves 等），身体骨（Spine1/UpperArm/Thigh）无 m_LocalRotation 绑定。之前 bodyPaths=54 的统计误将「路径包含 Spine 关键词」当作「身体骨自身有动画」（实际是子孙物理骨的祖先路径含关键词）。**所有世代角色的身体骨都由 humanoid muscle 系统驱动，AnimeStudio ReadCurveData 的 else 分支全部丢弃**——不是代差，是同一种限制。新角色只是物理骨更多（138 vs 29）让人误以为更多数据被导出。**muscle 求值路线（Ruri 工具链）已走通但产出幅度不够**（呼吸级 ±1-2°）。**战斗表现需换方向**：Mixamo 重定向/纸片人/其他方案。技术成果保留=AnimHarness bake 模式+Ruri 工具链移植+DBACL 修复，见 webrefs/gi-animation-extraction/README + docs/14 §68。













- [2026-09-19 01:58:22] 【派蒙语音现状与路由】游戏侧设计（三模式：关闭/本地侧车 127.0.0.1:9880/云端 MiniMax 自填 key；零进包；口型同步不做；覆盖=对话+抽卡反应+兜底全念、/指令回执不念）=docs/19 §6.5.11，P1a 已交付用户验证通过；**游戏外全档（模型谱系/训练/评测/侧车运维/yaml 绝对路径铁律/训练纪律三不/待办）=docs/27，语音话题先读它**。现状：用户拍板「先采用 A 组零训练优化，停止训练」（v4fullc e2 检查点保留，重启=train_pm_v4.py --skip-prep，A 组四件套全交付）；**等用户耳检 compare\paimon_compare_{reftable,grid,nbest}.wav 三份拍板**：情绪参考表哪些档采纳（游戏侧接线 paimon\refs_table.json）、n-best 守卫是否产品化；B 组（s1 补训/rank 升档）备选。侧车 9880 现挂 v4full e8 真权重、start_paimon_tts.bat 拉的是 v2 yaml（玩家默认=v2，勿混）。发布期议题=派蒙语音包可选组件安装器（4-6GB）或云 key，届时再拍。

- [2026-09-23 00:04:23] 【B5 判定体系已拍板】放弃队形→重叠格心+两态模型（执行阶段收拢重叠=特效锚格心、选择阶段自动散开复用展开布局）；格=交互粒度、**受击体=立牌真实大小圆柱**（底座圆盘可视化）与格脱钩；移动中单位连续插值位置可被中途命中（所见即所得）；接触判定与效果作用域解耦；纯瞬发仍按片初快照。规则本体已落档 docs/04/05/18+提交 eab2a20，实现要点=active/22 §11；B5 核心已落地（2026-09-19：圆柱受击体+投射物时间轴连续命中；圆柱直径调=BattleMetrics.UnitCylinderDiameter 一处）；圆柱容错直径、穿透模型待拍；剩余表现件（箭矢素材/天降视觉/震屏闪白/池化/立牌美术升级）挂起待拍板。**战斗表现层/判定任务一律按新体系实现，勿再按旧队形/格判定区方案写代码。**


- [2026-09-23 00:04:23] 【战斗 HUD v1 定案】设计稿=docs/designs/battle-hud-v1.html、拍板=docs/18 决策六+active/22 §13、操作流程=gic-battle-hud skill。布局=MOBA 范式（移动左下/爆发右下/战技左弧+延奏上弧/右上取消钮，布局参数全 [SerializeField] 中文命名）；单位选择交互=点立牌选中→技能盘现+手牌藏（互斥）、点空白取消；立牌=饥荒式斜插卡片（顶部远离相机后仰，后仰角=俯角 55° 时面正对视线完全消压扁，BattlePlayer [SerializeField] 立牌后倾角可调）；技能区数据链=UnitData.skills→SkillData 按 SkillType 分拣+SkillDetailPanel.prefab 复用；**拖动式瞄准=B4 未实现（点击式三情况已全链实现）**。


- [2026-09-23 00:04:23] 【战斗旧批收官（B2 Buff/B4 技能首批/HUD 补全，2026-09-19）】现状/剩余=docs/17 §7+active/22 §10；待拍板项（超时=Pass/战斗 SFX/血条敌我色 B7 重定/DyedElement 出生附着语义）登记 docs/11；头顶信息与立牌同平面倾斜（用户拍板「都应当斜」）。**SkillFactory 未注册技能类→UnimplementedSkill 占位保 skillIndex 对齐（null 跳过会让后续索引整体前移错位）。**



- [2026-09-23 00:04:32] [project] 【战斗表现件三件套纪律】后续新表现件一律走 BattlePalette（配色收口，改全局战斗配色=BattlePalette.asset）/BattleViewFactory（世界 quad+Unlit 材质+世界 TMP 唯一出口，材质调用方持有+OnDestroy 释放）/BattleViewTween（补间收口，末帧保证 t=1），勿再手搓 quad/色值字面量/while 循环；TextMesh 全退役→世界 TMP（细节=docs/14 §63⑤+gic-battle-hud skill）。**四技能键全同构**（用户拍板「技能按钮应当统一，移动是特殊的技能」：数据驱动 skills[Move]，图标/名称随 normalMoveType 步行/飞行/两栖，移动专位左下）；HUD prefab 化已于 2026-09-22 收口。




- [2026-09-19 01:58:22] [project] SkillDetailView 点外关闭竞态已修（战斗实例设 `点外关闭=false`，点外收面板由 OnBoardTap 承接；教训=同一交互目标被两个系统响应必须一方显式让位，勿依赖帧内执行顺序——全案 docs/14 §64b）。**背包场景同款竞态（点源图标面板闪动重开）仍存在未修——用户未报障勿主动动。**

- [2026-09-23 00:04:32] [project] BattleHud 表驱动四键（SkillButtonDef+RegisterSkillButton 加键=加一行；AimMode 枚举已删）；2026-09-22 起结构 prefab 化（真源=Resources/Prefabs/Battle/BattleHud.prefab，契约=画布下 Slots/{key} 槽内首子级=控件）。**文件地图/加键 checklist/勿当 bug 修清单/活体取证时序全在 gic-battle-hud skill，战斗 HUD 话题先读它。**

- [2026-09-23 00:04:49] 【搜索铁律补遗（2026-09-20 复验）】search_file_content 的 glob=锚定于搜索根的 gitignore 式语义：含 / 的模式须从搜索根写起（从工作区根搜 Assets 下 UI 目录须 **/UI/**/*.cs）；glob 零命中先加 **/ 前缀或换 path 参数复核再下结论，勿直接判工具 bug。全项目 .cs 文件头模板 using 已清理（291 文件 867 条，2026-09-19，编译逐轮 0 错）——`using GIC.X` 逐文件检索可作依赖方向审计依据；**例外=17 个豁免文件（玩家侧 #if×16+CardGlowOverlay 混合换行）仍带模板头，审计到它们须核类型实际使用**。清理工具链四坑=docs/14 §66。



- [2026-09-23 00:04:23] 【战斗架构审查批+B3 归位已提交（7b8ea20/9482251/3953fd9+a7cf05c，2026-09-19）】主干健康（Host 权威+片级命令流与 active/22 一致）；效应→命令对账缺口已由 BattleEffectCommandAudit 补（2026-09-22）；B3 归位=CardManager/CardDeck/UnitManager 迁 Framework/Collection、Direction2D/TeamType 迁 Data/Battle，Data→Battle 反向依赖清零（依赖方向表=docs/17 §2）；残余债务（B7 同构债三处/组合根域内化）已登记 docs/11；DamagePipeline 静态 hooks 生命周期与快照纪律仍靠约定（ClearHooks 零调用方，2026-09-23 审查复核，效应 hooks 扩展批次一并处理）。


- [2026-09-23 00:04:49] 【战斗表现纸片人方案与素材路线定案（2026-09-20 用户拍板）】GI 动画提取判死后战斗表现=纸片人方案；GI 七圣卡面 Spine 素材路线被否决（卡面人物只有半身/坐姿场景图不完整）→纸片人素材走 AI 生成（安柏滑翔立牌终版=提交 ba3d847：v4 基版+AI 局部擦除中上多余第三翼，用户拍板局部擦除优于全新生成、v5 退役；站姿 v2 定基准）。生成工作流+立牌设计规则+局部擦除配方=gic-paperdoll skill，后续角色直接复用。Spine 技术管线已验证保留（skel 4.0 解析器+AtlasDump harness，坑清单在工具链内；工具链=.codely-cli/tmp/paperdoll_amber_gcg/ + webrefs/spine-paperdoll/），将来做 GI 素材骨骼动画可复用。**How to apply:** 战斗单位表现素材走 AI 生成立绘；GI 提取素材只做图标/头像/卡面类完整资源。



- [2026-09-21 00:29:53] 【AI 出图模型规则两条（安柏纸片人实证 2026-09-20~21）】①Seedream 5.0 Pro（260628）出高还原 IP 角色必被输出侧版权审查拦截（OutputImageSensitiveContentDetected，与 prompt 措辞无关，两次实证，失败不扣积分）——GI 角色素材用标准 seedream 或 frontier_sunburst，勿选 Pro。②日辉原生透明底=fal 后端须传 **is_segmentation:true**（服务端映射 background:transparent+nativeSegmentation:true）；传自定义 background:"transparent" 无效（键不出现，出白底）。透明底固有 alpha 画像=背景约 60% alpha=0、主体 129-254 桶无 255 全实像素（中心约 253），v2 即如此非缺陷；seedream 的透明底是另一路径（segmented/ 后处理抠图 URL）。**How to apply:** 后续角色素材（凯亚/芭芭拉等）原生透明底一律 is_segmentation:true；查生成用模型/底色=chats/<会话>.jsonl 任务 JSON type 字段+下载后读 PixelFormat。
- [2026-09-23 00:04:54] 【面板预热泵已提交（7902e0f，2026-09-21，用户验证通过）】UIManager.PrewarmLoop=等 Splash 就绪+30 帧开泵+根转场锁在途挂起/转场毕+60 帧再续；WishScreen 4K 立绘 Preload 提前至 Splash 期。**症状归因**：启动动画卡顿/进厅首开异常/跳过 Splash 失灵先查 docs/17 §5b 泵行。**PreloadRegistry 统一注册表候选仍未拍板且未登记 docs/11**——后续时序重排/B6 时再提请拍板。

- [2026-09-23 00:04:32] 【2026-09-22 四批已提交推送（d4c0b14/a062b24/d539420/9e80ff4/4368e79，目检通过）】①斜向撞墙弹回方向修复（MovementResolver.StepVector 转 public，View 播放与 Host 步进同源；陷阱=docs/14 §69）；②HUD 自定义布局系统：14 件可拖可缩（LayoutSlot 归一锚点）+编辑模式（常驻「布局」入口/交互门控/SelectTimerPaused 冻结/金框/缩放滑条/三方案工具栏——宽度表驱动）+主存档 settings 分区持久化（三槽+activeHudLayout 纯新增不升版）；③HUD/退出弹窗 prefab 化（真源=Resources/Prefabs/Battle/BattleHud.prefab+BattleExitConfirmDialog.prefab，迁移工具 Editor/Tool/BattleHudPrefabMigration；BattleHud.Build.cs 退役为寻址接线；运行时 Palette 活色覆盖烘焙兜底）；④战斗相机缩放近端 12→4（远端 60）+昼夜音乐跨界立即淡切（AudioManager SwitchMusicWithFade+playbackEpoch 世代校验+「时段切换淡变」字段默认 1s；PositionManager 2s 轮询广播跨界/BattleScreen 回合结束跨界淡切）。待拍板（已登记 docs/11）：大厅背景随自然跨界切换（不要则退订 MainHallScreen handler）、淡变时长观感。**症状归因**：布局/弹回/HUD 结构/相机缩放/音乐跨界类问题先查这四批；细节=gic-battle-hud+gic-audio skill+active/22 §13。





- [2026-09-23 00:04:32] 【UGUI 烘焙/迁移锚点坑两条（2026-09-22 实证，已修复随批提交）】①程序化烘焙的容器节点必须显式设拉伸锚点（anchorMin(0,0)/anchorMax(1,1)/sizeDelta(0,0)）——Slots 容器曾按 new GameObject 默认 RectTransform 烘焙成 100×100，全部 UI 挤屏中心；②子件坐标原点约定（中心 vs 左缘）必须与锚点匹配——宽度表驱动生成的对称 ±x 坐标只能配中心锚，布局工具栏 13 子件曾配左中锚整体偏出条外/屏外。排查法=GetWorldCorners 算占屏比（全件聚集中心=父容器零尺寸；坐标对称 ± 却配非对称锚=②类指纹）。


- [2026-09-23 00:04:54] 【ElementAttach/Reaction 批已提交（4368e79，2026-09-22）】docs/11 协议节两项销案：①附着/反应命令接线全链（ReactionEffect 效应=SkillHitResolver.Hit 唯一产出点，覆盖瞬发/箭雨/投射物；客户端=UnitView 附着小图标/冻结冰色即时上色/解冻即时退色/融化留特效挂点）+Damage 命令带 reactionKind、伤害数字带反应名前缀（仅 Melt/Vaporize，格式"{反应名} -40"）；②对账机制=BattleEffectCommandAudit（Flow/ 下，漏发 Warn，未知效应类型默认报警=B6 扩效应安全网）；③蒸发反应补全（ReactionKindVaporize=3，增伤+50%×级别并入增伤乘区，与融化易伤分区）+UIText 12028/12029 五语言。**症状归因**：附着/反应不显示、蒸发没反应名、冻结延迟先查这批。**遗留**：出生附着语义与超时 Pass 确认两项未拍板维持现状；画面相关（箭矢素材/天降视觉/震屏闪白/SFX/纸片人素材线）挂起；本地化 CSV 同步物（webrefs/genshin-unpack/gichess/my/csv/）是过时快照，12000 段从未进 CSV（全段欠账，批量导出再议）。

- [2026-09-23 00:05:03] 【B6a 元能系统已提交（ff5d60d，2026-09-22）】元能口径（用户逐条拍板，已落档 docs/18 决策七）：移动 +10（被挡也算）/战技首次命中 +10（同片按目标去重，多次命中不叠加）/爆发消耗=技能 EnergyCost（SkillParamKey 9，门槛=消耗值非上限，勿硬编码）/上限=UnitConfig baseEnergy。实现=BattleSimState.ApplyEnergy+EnergyEffect→StatChange（metadata=StatKindEnergy）+SkillExecutor 通用能量门槛（EnergyCost>0 先查后扣，不足行动落空+Log）+HUD 爆发键 HasEnergyForSkill 置灰（快照权威刷新）；对账登记 EnergyEffect→StatChange；能量环视觉=B6d。**症状归因**：爆发放不出/能量不涨先查 SkillExecutor 门槛 Log 与 SkillParamKey 9 配置。

- [2026-09-23 00:05:03] 【B6b 四阶段已提交（fb80dfc，2026-09-22，目检通过）】低级单位决策=LowUnitBrain（1~2 星 v1 极简三档：战技直线方向有敌格→打/否则朝最近敌移动 1 步/无敌人或不可动=缺席；Host 在 TurnFlowController.BeginSelectPhase 直接生成 _minorUnitActions，**不走玩家上交通道、不占每回合行动配额**）；AI 玩家脑=AIDebugBrain 重构（只操控 3~5 星 IsMajorUnit；v1 优先级=战技可命中→战技/元能≥EnergyCost→爆发朝最近敌/否则移动朝最近敌/无单位→Pass）；共享原语=BattleHeuristics（IsMinor/IsMajor、切比雪夫最近敌 unitId 升序、八向 Delta、FindSkillIndex、FindLineSkillDirection 八向扫描 24 格虚空终止）；测试军=双方各加 1 星丘丘人（出生区 ±1 格，skills 空=纯移动档）。

- [2026-09-23 00:05:13] 【B6c-2 手牌滑动已提交（随 807eda0，2026-09-22，目检通过）】结构=HandCards 外框（pivot 底边中点防沉屏）→ScrollRect（horizontal+Elastic+透明命中层）→RectMask2D→Content（anchor/pivot=中上，窄于视口初始居中+回弹归位）→wrapper（Button+透明 Image——**纯 Button 无 Graphic 点击不可达坑**）→Card.prefab 原生 160×240 勿 stretch；content 宽恒=行宽+左右边距 120（勿夹视口宽=零滚程；Elastic=1 张卡也能滑、卡牌居中）；卡列表签名比对缓存跳重建。四坑全档 docs/14 §70+gic-battle-hud skill 勿当 bug 修清单；UI 排位类 bug 用 GetWorldCorners 取证。B6 剩余=B6d 执行阶段 HUD 变化+能量环视觉+B6c 遗留（正式卡面 polish/Battle_TipAimDeploy 提示键/物品卡使用装备链/建筑卡/AI 出战决策）；**B6 完整度缺口=体力消耗检查未接线（SkillExecutor 只查元能没查体力，每回合发放 5 也未做）——下一步优先候选**。

- [2026-09-23 00:05:13] 【B6c-3 手牌完整卡组投影已提交（随 807eda0，2026-09-22）】用户拍板：初始手牌=**完全**的当前卡组（此前只筛 Unit 卡）。协议 handUnits(List&lt;int&gt;)→handCards(List&lt;CardId&gt; 带 cardType)；BuildHandFromCurrentDeck 改走 decks[currentDeck].Cards 与收藏卡组同源同排序（角色前物品后/SortOrder/星级），空卡组回退丘丘人×2；物品卡走 ItemCardViewStrategy（数量=备战数 min(存档持有 GetItemCount, maxPrepareCount 备战上限)，用户拍板例「100 体力、备战上限 60→带入 60」）、无部署费角标（仅角色卡有）、点击=SetTip("Battle_TipItemCardPending") 不进部署链；UIText 12030 Battle_TipItemCardPending 五语言。**症状归因**：物品卡不显示/点物品卡进部署瞄准/手牌顺序不对先查这批（手牌投影目检需卡组含物品卡）；物品卡使用/装备链、建筑卡、AI 出战决策仍为 B6c 遗留。

- [2026-09-23 00:37:46] 【战斗系统全量审查已交付待拍板（2026-09-23 凌晨，基线=807eda0 工作区干净）】按 gic-code-review 九维跑完 ~100 文件 10.7k 行（Flow/View/Skill/Unit/Data.Battle 全读+rg 机械审计），总评=主干健康（三层时间模型/枚举序确定性/材质补间收口纪律执行良好），报告+9 项拍板清单已交用户、**用户已整体授权「开始」并按推荐方案全量修复提交（fad5c87，2026-09-23 目检通过）——审查条目闭环，勿再重跑或当未决议题处置**。核心发现：🔴R1=手牌滚动壳（HandCards 外框→ScrollRect→Viewport→Content）程序化构建落在 prefab 化拍板之后（BattleHud.RebuildHandCards 内，壳应上移 BattleHud.prefab hand 槽、卡条目留运行时）；🟡 按屎山增长率排序：Y1 TurnResolver 三段命令产出块三处复制粘贴（ResolveSlice/ResolveTurnEnd/ResolveInstantAction，turnEnd 段 Damage 已漂移缺 hitPoint/reactionKind，建议提取 EmitSliceCommands 单出口）；Y2 DeployUnitExecutor 缺"卡在手牌"校验（sim.GetHand 全项目零调用，B7 LAN 前必修）；Y3 UnitState 两处手写构造（TakeSnapshot 16 字段 vs DeployUnitExecutor 9 字段→Summon 建 view 登场回合附着图标/元能缓存显示偏差，建议 BattleSimState.BuildUnitState 单出口）；Y4 StatChange(StatKindMora) 客户端零消费（仅产出无消费，HUD 摩拉 chip 等下回合快照）；Y5 BattleSimState.GetCorePosition 依赖 HashSet 迭代序（注释自称同序，.NET 无保证，B7 前换 List 保序）；Y6 UnitGridPosition/TileGridPosition 曼哈顿死助手零调用且与切比雪夫拍板冲突（删或改）；Y7 BaseSkill 死字段 energyCost/moraCost/staminaCost+注释接线块引用不存在的 SkillData 字段（真实消耗走 SkillParamKey.EnergyCost）；Y8 体力/摩拉经济闭环未接线（行动不耗体力、回合不发放，HUD chip 显示假恒定 60/200，docs/11 未登记）；Y9 BattlePlayer.cs:264 箭矢光条色值字面量绕 BattlePalette；Y10 Resources.Load<UnitConfig> 六处旁路 ConfigManager（[Bean] GetUnitConfig 已存在）；Y11 BattleScreen SpawnDebugUnitsRoutine playerSetups[1] 无守卫；Y12 docs/20 §1.1"英文名+InspectorName"与战斗侧中文序列化字段实践（HUD v1 用户拍板过全中文）冲突，需修订 docs/20 承认例外或反向迁移。🟢 UIText.asset 定性已推翻（2026-09-23 复核实证）：它=Unity Localization 的 StringTableCollection 表集合主资产（容器仅持 SharedData+五语言表引用，943 字节是容器正常体积；Shared Data 实存 247 键，战斗键 12028 在 zh-Hans 表 L1000 实存）——**全项目 UI 文本主表，严禁删除**。原"空壳表建议删除"系两次误判（今晨全量审查+复审报告，只看容器没看 Shared Data 条目）；教训=判断 Localization 表是否为空必须数 Shared Data 的 m_Id，绝不能看 collection 主资产体积/内容（collection 天然是小容器）。此陷阱已落 docs/14 §72 + gic-localization skill 陷阱节第 7 条（2026-09-23）；12028/12030 五语言已验证齐备；已登记债全部复核仍开口（B7 同构债三处实证：BattleHud.cs 硬编码 _myPlayerId=P1/TopBar 读 Sim.Map.faction/直持 Flow；组合根仍由 BattleScreen 兼任；DamagePipeline ClearHooks 零调用方；出生附着仍 Dyed=Self；Battle_TipAimDeploy 专属键未建）。Why：审查结论不落档即随会话丢失，重跑成本 88 次工具调用。How to apply：用户拍板后按选项开修（优先组合=随 Y1 同批做 Y3）；修复产生的拍板/陷阱按四类分流落 docs（陷阱→14、拍板→18、缺口→11、通用规则→20）。



- [2026-09-23 00:29:36] 【战斗审查修复批已落地并提交（fad5c87，2026-09-23，目检通过——用户 9 项整体授权「开始」按推荐方案全量修，编译 0 错 0 警）】用户对 00:02:41 审查条目的 9 项拍板清单整体授权「开始」=按推荐方案全量开修，全部落地：🔴R1 手牌滚动壳四层（HandCards→HandScroll→HandViewport→HandContent）烘入 BattleHud.prefab hand 槽（值同 B6c-2 运行时构建，回读校验 12/12），RebuildHandCards 退役建壳=寻址+卡条目（壳寻址在 ResolveMiscWidgets）；Y1 TurnResolver 三段命令产出收口 EmitSliceCommands 单出口（turnEnd 段 Damage 补齐 hitPoint/reactionKind 漂移；**治疗命令发射序变化**：turnEnd 段从 Damage 后移至元能后，客户端 stagger 约 +0.36s 纯视觉）；Y2 DeployUnitExecutor 补手牌成员校验（Host 权威，防 B7 凭空部署）；Y3 BattleSimState.BuildUnitState 单一出口（快照与 Summon 同构，登场回合附着/元能/防御不再缺字段）；Y4 StatKindMora 注释如实化（客户端暂不消费，B6d/B7 决定消费端）；Y5 PlayerIds 改 List 保序（GetCorePosition 用 IndexOf）；Y6 删曼哈顿死助手（UnitGridPosition/TileGridPosition 各 3 个方法）；Y7 删 BaseSkill 死字段 energyCost/moraCost/staminaCost+注释接线块；Y9 BattlePalette 加「箭矢占位色」字段（新序列化字段走脚本默认值，资产重存前无序列化值）；Y10 六处 Resources.Load&lt;UnitConfig&gt; 收口 DI 容器（MonoBehaviour=[Autowired] UnitConfig、普通类=Context.Get&lt;UnitConfig&gt;，BattlePlayer.Bind 加 Inject）；Y11 BattleScreen 玩家数&lt;2 AI 补位守卫；🟢 deadTargets 死变量/Summon 工厂参数占位误用清理。**docs 同步**：docs/11（销案元能获取量+登记经济闭环 B6d 优先候选+测试军退役时机；BattleScreen 固定测试军仍在）；docs/20 §1.1（Inspector 序列化字段中文直名例外收口——战斗域与存量英文+InspectorName 并存皆合法、同文件勿混用）；docs/active/22 §13 加 2026-09-23 增量节；gic-battle-hud skill 文件地图手牌行更新。**新陷阱落档 docs/14 §71**：私有嵌套类 MonoBehaviour（SkillClickForwarder）被 2026-09-22 迁移工具漏剥烘焙进 prefab，跨域重载断链成 missing script 死槽（运行时每实例重挂无症状）→ 阻断 SaveAsPrefabAsset（本次烘焙首报）；修法=GameObjectUtility.RemoveMonoBehavioursWithMissingScript 剥 4 槽（保存后复查=0）；教训=运行时 AddComponent 件一律勿烘焙/嵌套类只限纯运行时。改动清单=14 .cs+1 prefab+4 docs（BattleHud.prefab.meta 已在库无新增文件）；提交时一并 add。**目检清单已交用户**（手牌视觉/滑动回归、部署链+登场单位显示、技能反应链回归、AI 行动）。How to apply：用户目检通过后提交（建议拆两 commit：审查修复批+prefab 壳批或合一均可）；报"手牌不显示"先查 ResolveMiscWidgets 壳寻址 Warn 与 prefab HandCards 四层；报"部署被拒"先看新校验 Log（手牌无 X/落点/摩拉三段）。
- [2026-09-23 21:38:11] 【时轮系统（SkillTimeline）已立项并 B-S1+B-S1b+两轮用户纠偏落地（2026-09-23）】拍板链=「开始。学习 m969/EGamePlay 自研取名时轮」→ 三指令（箭矢间隔 0.15s/移动是特殊技能也用时轮/完成安柏全技能排除命座变奏）→ 纠偏①「安柏的移动怎么会是3格？怎么会是米字方向？」→ 纠偏②「配置文件里的10，还要看基于类型。拼接后为10%移速」。决策=docs/18 决策八（含移动十字/移动距离换算两条独立拍板条）、设计=docs/active/28。核心：①SkillTimelineAsset（七轨 clip+aimMode+totalTime）挂 SkillData.timeline（null=兜底），分工铁律=时间规格归时轮/数值归 SkillParamKey；②前摇=ProjectileEffect 发射偏移（首窗插值修正）+per-skill 投射物规格；③命令=launchMs+SkillCast+合并键含 launchMs；④移动技能化=移动同产 SkillCast（AddMoveCast）、**步数上限=MoveDistance 按基准换算 10%×移速**（SkillData.ResolveMoveDistance 单出口：BasedOnMoveSpeed=百分比×移速/Fixed=直读/缺省 10%；安柏 50→5 格、凯亚等 30→3 格；UnitState.moveSpeed 快照新字段 HUD/Host 同源、移速含 Buff）、**瞄准=十字四向**（全员描述「选择十字方向其一」，首版误米字）、Amber_FlyingChampion 时轮（totalTime=0 动态）；⑤延奏实装=AmberSharpshooterSkill+AttackUpBuff（全图我方含自身、HUD 修正延奏我方格/契约敌方格、叠层+时长累加经 UnitStats StatModifier、协奏元能蒙德/自身+10 触发 Henka 框架位/非蒙德+20、数值单源=技能参数经 ApplyBuffEffect.BuffValue/StackLimit/DurationTurns）；资产=Resources/Configs/SkillTimelines/ 三件。**遗留**：B-S2 编辑器/B-S3 素材（SFX/动作/箭矢/AttackUp 图标待拍板）/资源点系统（飞行采集依赖）/AI 斜向移动纪律+Host 方向校验+方向模式数据驱动化（docs/11）。EGamePlay 源码在 webrefs/skill-timeline-system/EGamePlay_src/。




- [2026-09-23 21:45:44] 【角色文档技能描述双版本格式（2026-09-23 起生效）】docs/units 各技能一律=**玩家版描述模板**（进本地化表）+**开发版（实现规格）**（瞄准/时间轴前后摇/投射物速度体积/判定口径/多段语义/元能消耗/音效事件；值必须标三口径之一：已实现值/「设计值」（时间轴系统落地目标）/「未实现」）；安柏.md 六技能已全部改版且随 B-S1b 实装更新现状（技能类注册数=4：安柏战技/箭雨/延奏百发百中/凯亚霜袭；安柏 #1 移动已技能化（无技能类——ActionType.Move 走 MoveExecutor+时轮 SkillCast）、#5 变奏/#6 命座仍 UnimplementedSkill 占位）；开发版规格**不进 SkillParamKey/本地化同步链**（已固化 _模板与字段说明.md「技能描述双版本」节 + gic-new-unit skill 防误同步条目）。新角色文档与改技能描述时按此双版本写，勿退回单版本。

- [2026-09-23 23:18:36] 【AnimeStudio issue #124 维护者回复核验+英文追评草稿已备待拍板（2026-09-23）】Escartem 2026-09-21 回复逐条核验（当前 master 源码级）：①「ConvertValueArrayToGenericBinding 对 GI 是死代码、m_ClipBindingConstant 4.3+ 正常读」=对（L904 `?? 兜底` 实锤），我们 issue 归因写错、追评认错；②humanoid muscle 标量无骨路径落 else 丢弃=与我们 ★★ dump 一致；③其「Ambor_Standby 含 muscle 值」半对——该 clip m_IndexArray 193×-1+7 空 Root 槽**不含** muscle 数据（身体在共享 clip `Ani_Avatar_<BodyType>_*`），双层架构情报维护者不知道=回帖最大增量；④其「新世代 avatar=generic rig、Odette 完整导出」与我们 09-20 复审冲突（138 物理骨×10+7=1387 曲线、身体骨零 TRS 绑定；其误读与我们 09-19 白天同款）——回帖礼貌反问资产名。全仓库提交史零 muscle/humanoid/retarget 实现→「几个月前还能导出」不成立（能导的一直是=模型/NPC TRS/ZZZ qvvf/GI 物理层）。**2026-09-23 晚复审修正（原口径两处错）**：①建仓非 2025-10——repo created_at=2025-02-16、根提交「uploaded updated studio」（快照式上传 RazTools Studio 代码，非 GitHub fork：fork:false）；②全史=301 条非 ~200——原扫只覆盖 2025-10-08 后的 200 条、漏 2025-02-17~10-08 的 101 条（commits-3/4.json 已补），补扫后严格词 muscle|humanoid|retarget|mecanim 仍零命中、动画提交全在解码层（fix AnimationClip for genshin 2025-03-22/ZZZ/ACLDenseClip/anim loading），结论存活但表述须改；③animestudio-issue124/search-*.json 全是 404（代码搜索 API 失败产物）不可当「零命中」证据引。同轮 FBX 独立审计（amber-anim-test/fbx_anim_audit2.cjs，FBX 7300 二进制解析器）：Odette 导出 FBX 五 take 各 138 物理装饰骨（Bone_Skirt/Wing/JigBuns/Shape_Spine 等）**零身体骨**——66 根 Bip001 人形骨全在骨架但零动画绑定；1242 曲线/take=138×9（R 出 3 通道），1387=clip ACL 口径 138×10+7 空 Root，两口径并存勿混。**23:1x 追问补验（「几个月前还能导」证据链升级为三点代码级覆盖）**：根提交 ee88e924（2025-02-16「uploaded updated studio」）全树 366 文件=AssetStudio 系快照，零 muscle 命名文件，其 ModelConverter.cs 与现行同构（终端 else curveIndex++ 丢弃、仅 m_MuscleClip 容器访问，root-tree.json/ModelConverter_root.cs 留档）；Perfare/AssetStudio master 284 文件同样零 muscle 实现→README 09-19「可抄 Perfare muscle 转换」已勘误（参照不存在，真实参照=BA/Ruri）；旧本地版 1ccfbc16=2026-08-12 构建（正当维护者所说「几个月前」窗口），09-19 已实测 CLI 转换=只出物理骨与新版一致。至此「从未有过 muscle 求值」=根快照+301 全史+现行 master 三点代码级覆盖+窗口内实测+维护者自认未实现，回归假说（ACL rework 弄坏曾能用的身体导出）压至极低。**DBACL streamer=IntPtr.Zero bug 当前 master 仍在**（AnimeStudio.Utility/ACL/ACL.cs:152-160，"For now" 注释原文）——修法已验证（streamer=dbAligned+bulkOffset，RootT.y 与 m_ValueArrayDelta 逐位互证），建议单开 issue、可 PR。英文追评草稿（4 点+PR 意向）已交用户；gi-animation-extraction README 代差表已加作废补正注。**遗留假设待验**：09-20「muscle 产出幅度不够（呼吸级 ±1-2°）」判定的被测 clip 疑为 Girl_Standby（本就是呼吸级 idle）——用大动作共享 clip 重跑 AnimHarness bake 一次可定论；若系误判则官方身体动画全量可回收。


- [2026-09-23 20:55:26] 【「muscle 产出幅度不够」误判实锤已复现推翻（2026-09-23，用户指令「看看能不能复现之前的问题」）】用修复版 AnimHarness 重跑 dump+烘焙，同口径振幅分析（工具=amber-anim-test/amplitude_check.cjs）：CrouchToStandby=116/137 通道有动画、19/21 骨骼 >10°（右小腿 110.7°/左大腿 101.6°）、FK 头高 1.30→1.96m（66cm 蹲→站，Hips ΔY 与 RootT.y 0.571→0.945 逐位吻合）、烘焙重跑 BAKE_OK（17 TRS+46 muscle 轨×44 帧）；Girl_Standby=114 通道有动画但全为 idle 量级（身体骨 3~7.6°、头高浮动 7mm）=呼吸级 idle 的正确输出。22:56 旧 dump 与重跑逐位一致（streamer 修复在 22:39 坏 dump 与 22:56 间已生效，00:31 只是 hips 补偿时间戳）。**结论：2026-09-20 01:27 的「幅度不够」判定系误判——被测 clip 本就是呼吸级 idle，muscle 路线自始至终没坏，官方 GI 动画全量可回收**；「换方向」拍板的数据前提不成立（纸片人已落地不受影响，重启与否待用户拍板）。已同步 webrefs/gi-animation-extraction/README.md「幅度终审」节；issue #124 回帖可加一句 big-motion verified。**遗留**：Root/Motion→hips 质心补偿（locomotion 用）与 twist 重分配仍为待做；bow 体型身体基础 idle=Ani_Avatar_Girl_Bow_Standby（04161624）可作更有动感的待机备选。
- [2026-09-23 22:38:38] 【B-S2 时轮编辑器+技能独立化已提交推送（e9e34b6，2026-09-23，目检通过）——时轮系统 B-S1/B-S1b/B-S2 全闭环】会话 L 两次推送：d9e31d2（B-S1 判定侧+B-S1b 安柏全技能+移动技能化）+ e9e34b6（B-S2 编辑器+技能独立化）。B-S2=`Editor/Tool/SkillTimelineEditor.cs`（Tools/TG/时轮编辑器，UI Toolkit+ConfigEditorUITK）：七轨时间轴+标尺+clip 拖拽（0.05s 吸附/右缘调长）+按轨道属性面板+校验行+接线 UnitConfig 按钮+缩放滑条；编辑=直写对象+Undo+显式保存。**Tuanjie UITK 坑**：EnumField.newValue 装箱 System.Enum——转 int 用 Convert.ToInt32（CS0030）。遗留（docs/11）：B-S3 素材路线待拍板（SFX/箭矢/AttackUp 图标：AI 生成 vs 官方提取 vs 效果库）→拍板后客户端表现轨消费（SkillCast→时轮资产播 cue）接线；B-S2 增强（灰盒预览+scrub/SkillName 搜索下拉）；资源点系统（飞行采集）；AI 斜向移动纪律。

- [2026-09-23 22:38:19] 【技能配置独立化完成并已提交推送（e9e34b6，2026-09-23，目检通过）】SkillConfig 独立资产化：每技能一个 SO（`Resources/Configs/Skills/{SkillName 枚举名}.asset`，data=SkillData：skillID/icon/skillType/customParams/timeline）；UnitData.skills=List&lt;SkillConfig&gt; 引用列表（顺序=skillIndex 语义勿重排、空槽运行时告警）；Common_Walk 8 角色共享单资产（两变体运行时等价合并为带参数版）；消费点 9 文件全走 `.data` 解引用；UnitData 编辑窗选中技能=编辑资产本体。gic-new-unit skill 同步流程已重写（建资产+引用）。**此后加/改技能一律走独立资产，勿再往 UnitConfig 塞内嵌 SkillData**；改技能参数=改 Skills/{name}.asset（共享资产改一处全角色生效）。
- [2026-09-24 19:16:54] 【AnimLookTest muscle 目检载体重建（2026-09-23）】bake FBX 两份已落 Assets/Art/AmberAnimTest/（Ani_Avatar_Girl_Standby_bake.fbx=09-19 idle 版、Ani_Avatar_Girl_CrouchToStandby_bake.fbx=09-23 重验大动作版）；四实例（AmberBake_Crouch x=0/AmberBake_Idle x=3.2/Amber_Harness_FBX x=-1.6/Odette x=1.6）clip 已全部导入器级 loop 化+组件 wrapMode=Loop（坑=docs/14 §73：AnimationState 层设置不序列化）；Idle 实例 phys clip 因 layer 无法序列化、无脚本不会自动播（预期非 bug）。**目检结论（2026-09-23 深夜）=不通过：效果与期望许多处不一致 → 用户拍板放弃自研管线，路线终局见同日终局条目+README 终局节。bake 实例+FBX 已拍板入库存档（a89c3ff，2026-09-24 推送）。**

- [2026-09-23 22:55:44] 【官方动画路线终局：目检后放弃自研 muscle 管线（2026-09-23 深夜用户拍板原话「放弃自研管线，效果与期望许多处不一致」）】路线关闭：战斗表现维持纸片人（已落地不受影响），官方 GI 动画提取不再为表现层候选；20:55 幅度终审的「重启与否待拍板」就此了结=不重启。**数据级验证与视觉判定分离**（docs/14 §38b 教训再现）：RootT.y↔m_ValueArrayDelta 逐位互证/FK 头高/摆幅>10° 只证明解码与求值数学正确，不保证目检达标；目检失败部位未归因（未知是否=twist 重分配/hips 质心补偿两已知未做件所致）。技术成果转存档（README gi-animation-extraction 已加终局节=完整配方可重跑）：DBACL streamer 修复（bug 独立成立、上游 PR 意向不受影响）、muscle 求值移植、AnimHarness 工具链。**issue #124 追评口径修正**：不 claim big-motion/视觉验证，仍成立的数据级事实=RootT.y 与 vad 逐位互证+四点核验（CRC 认错/双层架构/Odette 138 路零身体骨/301 全史零实现）+DBACL streamer PR 提议；是否提交仍待拍板。
- [2026-09-24 00:54:47] 【瞄准高亮推荐分色已落地（2026-09-23 分色拍板，09-24 视觉定稿+烘焙修正）】三态：可选且推荐（青蓝）/可选但不推荐（红）/不可选无提示；**色块视觉终版=青芯+内嵌黑边 quad，推荐色=原神 Hydro 系青蓝 #4CC2F1 (0.30,0.76,0.95,0.8)**（用户拍板"换个颜色，但不是白色和金色，按照原神风格"——迭代链白→金→白+黑边→青蓝；原神对话选项选中态同色系+尘歌壶"可放=蓝框/不可放=红框"既有语义）；底图=BattleViewFactory.AimCellTexture 运行时生成 128px 白芯+12px 黑边环（黑边 rgb=0 乘 tint 恒黑故两色共用一张），CreateAimCellMaterial 挂 _BaseMap。推荐口径=直线技能该方向能命中敌人（技能实例 WouldHitEnemyInDirection——BaseSkill 虚方法默认整线扫描，安柏战技覆写=投射物圆柱接触距离空间预判、凯亚霜袭覆写=DamageDistance 段、箭雨用默认；**新直线技能必须覆写保预判与结算同形态**）；移动=CanMoveEnterPreview 步进预判镜像 MovementResolver 判定链（被挡后该向余下格全不推荐）；单位指向与部署 v1 全推荐。**色值已烘入 BattlePalette.asset 真源**——踩坑教训=长跑编辑器里改脚本默认值对已加载资产实例无效（冻结陷阱=docs/14 §74），**后续调色改资产勿只改脚本**。半透明=CreateTransparentUnlitMaterial（URP Unlit 透明配方，不设 _Surface 时 alpha 被强制 1）。含尸体算命中=Host 同语义（是否排除尸体待反馈）。落档 docs/18 决策六（含 09-24 终版迭代链）+gic-battle-hud skill+active/22 §13。报"瞄准颜色不对/没有黑边/水面红了"先查这批。
- [2026-09-24 21:35:14] 【伤害数字原神式屏幕空间化已落地待目检（2026-09-24，用户拍板「按照原神的做法」+反应前缀保留为GIC特色+伤害不带减号+不做聚合）】实现=新文件 Battle/View/BattleDamageNumbers.cs：独立 Screen Space - Overlay 画布（sortingOrder 39 于战斗 HUD 40 之下，数字不参与 3D 深度测试→永不遮挡/任何角度正面；勿加 GraphicRaycaster——Overlay 射线器会挡 HUD 按钮）+对象池；曲线=首帧爆大尺寸(初始停留比 2.5——2026-09-24 目检后用户拍板由 1.5 调大)→0.35s easeOutCubic 收缩到停留尺寸，伤害→尺寸映射 f=Clamp(0.8+0.22·log10(max(1,|dmg|)), 0.8, 1.65) 初值与终值同乘（对数防大数字占屏），段尾渐隐+上浮 60 参考像素；BattlePlayer.PlayDamageCoroutine 改调 Spawn（文本=治疗「+N」/伤害「N」无负号/反应「蒸发 N」，随机偏移加大散布(±0.42 X / 0.3~0.65 Y / ±0.15 Z，2026-09-24 目检后拍板)，闪红节律 0.8s 不变）；BattlePalette 加「伤害数字描边色」(0.04,0.03,0.03,0.85)（新字段走脚本默认值，未烘资产——与箭矢占位色同先例，将来调色改资产）；旧世界 TMP 数字实体与起手放大弹跳曲线已退役。编译 0 错 0 警。症状归因：数字被地形遮挡=旧实现残留；数字先手从小放大=旧弹跳残留；尺寸恒定=映射未生效。**How to apply：** 战斗表现层报障先查 layer 是否按拍板落地（gic-battle-hud skill 文件地图已加行、勿修清单已更）；拍板落 docs/18 决策六；待用户目检后随批提交（工作区有他会话 WIP 勿带入）。
- [2026-09-25 11:57:39] 【头顶条原神式屏幕空间化已收官（2026-09-25 用户目检通过，提交 3404df3 已推送——与伤害数字批 46d1022 两批闭环）】新文件 Battle/View/BattleOverheadBars.cs：独立 Overlay 画布 sortingOrder 38（伤害数字 39 之下、HUD 40 之下），逐帧投影 UnitView.OverheadBarAnchor（含 55° 后仰偏移）；结构=血条（**填充=队伍色**（09-25 目检拍板：与底座同色，首版敌我绿/红被否——Palette 血条我方绿/敌方红不再被头顶条消费）+**圆角药丸形+黑边框**（底图=真实资产 Assets/Resources/UI/Battle/BarBg.png（黑边框 5px 环+白芯）/BarFill.png（纯白圆角），09-25 拍板「不要程序化生成」——运行时零生成只做 Resources.Load，美术出图直接覆盖文件；**填充必须内缩边框厚度（条高×5/24），同尺寸会整盖住底图边框环致黑边不可见**=首版"描边不可见"归因；UGUI Filled 必须赋 sprite 才裁切，空 sprite 时 fillAmount 被忽略条恒满=docs/14 §75）+原神分隔线默认 4 段）上→元能条（**白色**（09-25 拍板，元能蓝→Palette.元能条色改名+默认白）+分隔量子=10 与 B6a 获取粒度一致、段数上限 10）下→附着图标随血条左缘；无 GraphicRaycaster。UnitView 数据化：撤世界血条 quad 与附着 SpriteRenderer，公开 Hp/MaxHp/AllyHpBar/AttachedElement/OverheadBarAnchor getter，名字/Buff 徽章仍挂立牌倾斜组（「都应当斜」拍板未推翻这两条）；BattlePlayer CreateView 注册+ClearViews 清 All（Summon 路径同走 CreateView 自动覆盖）；元能条尸体隐藏。编译 0 错 0 警，**未目检未提交**。症状归因：头顶看不到血条=OverheadBars 层未建（CreateView 未注册）；元能条不显示=EnergyMax≤0 或尸体；条不贴头顶=OverheadBarAnchor。**【收尾 skill 新规 2026-09-25 拍板】**gic-wrapup skill 已改：收尾最后一步=**提交+推送（勿再问）**——清单排除他方 WIP、消息草稿落盘 -F 提交、push=先查 Clash 健康复用直连/代理（直连被 reset 属瞬态抖动连试 2~3 把，2026-09-25 第三把推成实证）。**How to apply：** 后续会话凡用户说收尾，走完沉淀后直接提交推送并回报提交号。
- [2026-09-25 13:14:12] 【战斗系统二轮审查已全量修复+元能结算序返修落地（2026-09-25，审查成本 75 工具调用；用户目检通过全部清单后报障返修一项）】**未提交**——元能结算序返修目检通过后说「收尾」走提交+推送（工作区含他会话 WIP CODELY.md/manifest 勿带入）。修复批清单（编译 0 错 0 證、目检通过）：R1=EnergyEffect 来源类别四常量+MergeEnergyEffects 键=目标+类别（五产出点全接类别）；S3=部署碰撞判定链（IsDeployCellValid 带新签名 deployData：体积≤3 最高级+阻挡互不阻挡+地形，用户拍板「根据碰撞决定」）+HUD CanDeployEnterPreview 部署分色镜像（部署 v1 全推荐作废）；S1=时轮编辑器接线改 SetDirty 被改 SkillConfig 资产（独立化遗留）；S2=编辑器同 kind 判定 clip>1 警告；S4=空 unitId Pass 分桶前剔除；S5=登记 docs/11 方向纪律④（AI 爆发斜向必空）；🟢清理=时轮编辑器死方法+allyHpBar 死链（Palette 血条两色字段留 B7）。**返修（同日晚）**：用户报障「安柏 30 元能延奏自己终值 10 应为 20」——根因=效应列表序协奏 +10 先于消耗 −20 应用，+10 被 baseEnergy 上限钳位吞掉（30+10→钳 30→−20=10）；修复=**元能两段应用**（TurnResolver.ApplyEffects 按正负分桶、先全部消耗后全部获取，跨行动同段也覆盖）+MergeEnergyEffects 命令发射序同步先扣后加（客户端增量与 Host 同序）——拍板=「先扣除，再加」落 docs/18 决策七结算序条、陷阱=docs/14 §77（钳位资源增量不可交换 clamp(x+g)−c≠clamp(x−c)+g；同族未决=HP 伤害/治疗同段顺序未拍板，治疗技能落地时定）。核验实证（勿再查/勿误判）：UnitConfig 34 单位零空槽、安柏时轮三件全接、fad5c87 九项零回归、Lisa 无战技=设计（VioletArc 类型=Move）、丘丘人 skills=[] 回落 3（docs/18 已勘误）。Why：返修与拍板不落档即丢。How to apply：症状归因「元能终值少一段获取」查 ApplyEffects 两段序；「客户端顺序与 Host 不一致」查 MergeEnergyEffects 发射序；后续任何钳位资源（HP/体力）同段正负并存按 §77 先扣后加。
- [2026-09-25 16:09:18] 【技能效果原子库 B-1/B-2 已落地目检通过 B-3 按需（2026-09-25，编译 0 错 0 警；提交 f2c171a/cb4c48d 已推送，**B-2 91db536 本地落袋推送挂起**——直连三把 reset、Clash 未开，用户开 Clash 补推）】设计=docs/active/29、拍板=docs/18 决策九（全 [x]「全按推荐」）、现状=docs/17 §7、剩余=docs/11。B-1（cb4c48d，目检通过）：SkillEffectConfig.cs（union 原子 OnCast/OnHit/OnVanish×六 kind+蒙德筛选器，paramKey 防双源）+EffectCompiler.cs（编译期展开成 BattleEffect——应用/合并/对账/命令全链零改；判定轨编译/TriggerSkill 链泛化 TriggerHenka/预判工厂按 kind 派发强制同形）+ConfiguredSkill.cs（SkillFactory 三层分流）+Hit=编译派发器（旧三件套=隐式默认原子）+四技能类退役删除+资产迁移（幂等脚本+回读过，凯亚霜袭新建时轮 LineBurst maxRange=2）。B-2（91db536）：UnitConfigEditor 效果原子编辑区——SelectSkill 排除 effects 默认绘制+CreateList 效果列表（行=kind 摘要/badge=触发短名）+选中卡按 kind 显字段（全建+显隐切换，kind/trigger 回调同步）+校验行（空=占位提示/判定 clip 无 OnHit=空打警告/OnHit 无 clip=兜底提示）；UITK 坑复用（清单见 gic-editor-tool）。**战技获能=配置差异（B6a 数据化）；加新技能=配 effects+时轮零代码**。B-3 按需=OnVanish（随资源点批）/TriggerSkill 条件计数器（B8 命座）/形状库扩容（第 4 角色实锁）。漂移点=霜袭判定距离收口 clip.maxRange、参数表 DamageDistance 留描述（改距离改 clip）。Why：B 批实施与状态不落档即丢。How to apply：加技能先试 effects 组合、新形制才扩 EffectCompiler；改霜袭距离改 clip；91db536 推送=开 Clash 后 push origin master。
- [2026-09-25 18:12:05] 【技能效果原子库 B-2c 凯亚/芭芭拉技能链已落地（2026-09-25，5c14003，编译 0 错 0 警，目检清单已交）——本条目承接并取代同日「B-1/B-2 已落地」旧条目】推送状态：**五提交全部已推送**（2026-09-25 会话末 cb4c48d..5c14003 直连推成——推送挂起已解除，HANDOFF 板会话 N 收官核验）。B-2c=五技能纯配置实装（效果原子批量实证、零新技能类）：凯亚延奏=OnCast[MoveSpeedUp(10/5/3)+协奏元能+触发变奏]、凯亚变奏=OnCast[治疗自身 50%攻]、芭芭拉延奏=OnCast[治疗被协者 15%maxHp]、芭芭拉变奏=OnCast[治疗我方全体 8%各自maxHp]、芭芭拉战技水之浅唱=OnHit[伤 10%施法者maxHp+水附着+治疗施法者半径1内我方 15%各自maxHp]+时轮 LineProjectile maxRange=5。代码增量：新 MoveSpeedUp Buff（BuffType=4+MoveSpeedBuff BaseFlat——移速含 Buff 直连移动距离换算链）；筛选器 AllAllies/CasterRadiusAllies（radiusKey 引参数表键防双源）；Damage 原子基准分流（BasedOnMaxHealth=施法者生命上限→FlatDamage 加法区）；治疗换算出口 ResolveHealAmount（BasedOnMaxHealth=被治疗者各自/BasedOnAttack=施法者——docs/11 裸 int 坑销案，护盾类仍开口）。**教训（自纠）**：ResolveCastTargets 重构时曾把蒙德筛选写成恒真（`||true`+丢条件）致协奏规则失效——大方法重构动筛选条件必须逐 case 核对。**语义选择（目检确认点）**：治疗基于最大生命=被治疗者各自 maxHp、伤害 BasedOnMaxHealth=施法者 maxHp——观感不符则一行换算改动。B-3 剩余（docs/11）=凛冽轮舞（召唤系待拍板）/闪耀奇迹（复苏语义待拍板）/OnVanish（随资源点批）/条件计数器（B8）/形状库（第 4 角色实锁）；漂移点=霜袭/水之浅唱判定距离收口 clip.maxRange、参数表留描述渲染。How to apply：加技能先试 effects 组合、新形制才扩 EffectCompiler；筛选器重构逐 case 核对。

- [2026-09-25 19:56:20] 【B6d 体力/摩拉经济闭环已收官（2026-09-25，编译 0 错 0 警，**提交 c2bbcff 已推送**——直连一把推成 5c14003..c2bbcff；复检清单已交用户）】拍板=docs/18 决策十+销案 docs/11 经济闭环条目：①体力/摩拉=玩家持有物品牌（ItemName.Stamina/Mora，PlayerResourceState 语义=持牌数，图标走 ItemConfig 单源）；②回合结束段发放 +5/+5（第 1 回合=初始值 60/200——实现取值，改回合开始发放是一行事）；③消耗=移动/战技/爆发各 10（StaminaGate（ActionExecutors）：星级≥3 才扣、低级单位 1~2 星豁免、延奏/契约 0、不足落空同元能；口径单源=BattleSimState.GetStaminaCost）；④**玩家资源命令 targetUnitId=玩家 ID——BattlePlayer.StatChange 必须先按 metadata 分流（Mora/Stamina→OnResourceDelta 事件）再查 _views**，否则被单位查表静默丢弃（StatChange 工厂 unitId 字段双语义陷阱）；⑤左上角显示=**ItemCounterChip 公用组件**（新：Assets/_Scripts/UI/Common/ + Resources/Prefabs/UI/ItemCounterChip.prefab——"物品×数量"显示需求一律复用勿再手搓；抽自祈愿界面货币显示公用化，WishScreen 原地组件化视觉零漂移；BattleHud myinfo 槽内 MoraChip/StaminaChip 两枚实例，寻址契约=槽内同名节点）；⑥**enemyinfo 敌方信息块整体移除**（2026-09-25 用户拍板「不需要显示敌人的资源等信息」——prefab 删槽+AllLayoutKeys 删键+TopBar PlayerBlock 退役，存量布局方案旧键自然跳过；协议 resources 仍含双方数据只是不显示，B8 敌方显示再议）；⑦AI 玩家 P2 体力同链结算但脑不感知体力（12 行动回合后每回合落空空过=AI 智力后续批次）；⑧技能/移动键体力置灰（HasStaminaForSkill）。症状归因：体力不扣→StaminaGate Log；回合发放不显示→ResolveTurnEnd 段首直产命令+BattlePlayer 分流；左上角无数字→myinfo 槽 MoraChip/StaminaChip 寻址 Warn+InitItem 图标链；摩拉部署后不即时减→ApplyMyResourceDelta 事件链。目检清单 8 项+2 实现取值确认已交用户。**2026-09-25 报障返修两项（编译 0 错 0 警，待复检）**：①左上角只有数字无图标——根因=ResolveHudResources 寻址阶段跑在 Bind 的 Context.Inject 之前，_itemConfig=null 时 ItemCounterChip.InitItem 的 SetIcon(null) 会 SetActive(false) 隐藏图标节点；修法=InitMyResourceChipIcons 挪到注入后调用（**时序陷阱：[Autowired] 依赖的初始化永远放 Inject 之后——寻址/注入分离**）；②手牌货币卡两轮收敛——**正确口径（2026-09-25 用户拍板）**：「体力/摩拉=手牌（物品牌）」=摩拉/体力卡**可编入备战卡组**（ItemConfig maxPrepareCount=100/60，不可入组的货币仅星辉/相遇之缘/纠缠之缘=NonDeckableCurrencyItems——决策七"货币类 maxPrepareCount=0 不可入组"旧注勘误仅指缘/星辉），**开局系统往手牌送 200 摩拉+60 体力（落在牌上=角标初始值）**；手牌渲染=卡组投影原条目（**勿尾部恒附加**——首版附加两张致卡组带牌玩家出现两份重复报障实证），货币卡数量角标=局内持有非备战数、RefreshHandCurrencyCards 挂资源刷新链随发放/消耗动态刷新（重 Init=池化既有用法），点击=物品同款提示不进部署链；**四轮定稿（2026-09-25 用户拍板原话「获得卡：如果手牌已经有该卡，则加数量，如果没有，则加上这个卡。失去数量时，同理」——"注入"等特殊机制说法被否）**：协议新 HandCard 条目类（cardType/value/count——**数量是一等属性**；物品/角色条目 count=局内真源、货币条目 count=资源池镜像快照时映射）；`BattleSimState.GainCard`（有则加数量/无则加卡）/`LoseCard`（减数量/减至零移除卡、不足 false 不动）公用对偶方法（使用/装备/掠夺后续批接线）；**货币牌数量=资源池**（PlayerResourceState.mora/stamina 即牌堆张数——池写自动同步条目存亡：堆>0 必有卡、空堆移除、再发放自动复活=与普通卡获得/失去对称零特判，SyncCurrencyEntry 收口）；开局=空表起步→卡组按顺序逐张获得（count=备战数、同种多张堆叠一条目、空卡组回退丘丘人×2=count2 一条目、**货币条目卡组读取跳过**——开局量统一「送 200 摩拉+60 体力」编没编都送勿双发）；部署扣费 TrySpendMora→ApplyMoraDelta（池写含同步）、回合发放直产命令同链；客户端渲染=HandCard.count 真源（物品角标不再显示层拼备战数）、签名=卡 id 列表（数量变化不重建、存亡变化自然反映）、货币角标经 RefreshHandCurrencyCards 随资源命令链动态刷；RegisterPlayer 池 0 起步。
- [2026-09-25 20:32:50] 【AI 玩家 v2 评分制+对称测试军已落地（2026-09-25，编译 0 错 0 警，目检清单已交，**未提交**——目检通过后说「收尾」走提交+推送，勿带入他会话 CODELY.md/Packages manifest×2）】拍板链=用户指令「强化AI玩家，让它更聪明，并且初始，双方场上各1个芭芭拉，安柏，凯亚，丘丘人」→docs/18 决策十一。实现：AIDebugBrain v2=全候选评分制（攻击技能 CanCast/元能/体力三门槛+十字四向逐向预判 WouldHitEnemyInDirection+伤害/斩杀(80)/战技获能(12)评分；延奏=蒙德或自身目标+治疗缺口半量门槛/增益未满层溢价15/变奏链12/协奏元能4估值；移动=十字逼近+走位进射击线加分8+近敌接敌加分；Pass 兜底；评分常量收口类头部，调手感改常量）；LowUnitBrain=十字方向纪律收口（战技预判走技能实例+移动主轴逼近）；BattleHeuristics 扩容（CrossDirections 唯一方向域/PreviewLineTargets 镜像 EffectCompiler 两分支（LineProjectile=首停格含尸体截停/LineBurst=clip.maxRange 整线）/EstimatePerTargetDamage（Damage×DamageCount 按基准：BasedOnMaxHealth=施法者 maxHp）/IsMondstadtOrSelfUnit/FindAttackDirection/BestCrossApproachDirection；八向 DeltaToDirection/DeltaOf/FindLineSkillDirection/HasLivingEnemyAt 退役删除；FindSkillIndex 改遍历 unit.Skills 实例=与 SkillExecutor 同源索引）；BattleScreen 测试军=双方各 1 安柏/凯亚/芭芭拉/丘丘人（镜像位 center/(±1,0)/(0,±1)/(±1,±1)，丽莎移出）。**方向纪律①④销案 docs/11**（②Host 方向校验/③数据驱动化仍开口）。**随批登记 docs/11 两项待拍板**：①霜袭 Normal+LineBurst 编译漂移（EffectCompiler.CompileJudgment 只对 Burst 型消费 LineBurst clip——霜袭 Normal+kind2 clip 走无时轮兜底=24 格首停 ProjectileEffect，vs 预判工厂/HUD 瞄准/文档的 2 格距离段；修法 A=编译对 Normal 也消费 LineBurst/B=改时轮约定资产 kind=1，AI v2 未动判定层只登记）；②霜袭掠夺摩拉 MoraPlunder(75)=5 未接线（SkillEffectKind 无 Mora 类原子）。How to apply：症状归因——AI 斜向移动/爆发斜向空放应已消灭（旧症先查本批）；AI 周期性空过=体力枯竭期攒体力（60 初始+5/回合 vs 10/行动，第 7 回合起隔回合行动属预期非卡死）；延奏目标恒蒙德或自身=效果原子产出保证。
- [2026-09-25 21:06:26] 【战技获能 targetFilter 返修已落地（2026-09-25 用户报障「安柏战技命中敌方凯亚没加元能」，编译 0 错 0 警，复检清单已交，随 AI v2 批未提交）】根因三层（docs/14 §79 全案）：①EffectCompiler.CompileOnHit 从未消费 targetFilter=Caster 分支（OnHit 恒落"命中目标"）；②B-1 迁移脚本把安柏双矢/凯亚霜袭获能原子 targetFilter 误配 0=Target（+10 元能发给了被命中的敌人——打谁谁充能）、芭芭拉水之浅唱整个获能原子漏配；③隐藏坑=TurnResolver.ApplyEffects 元能逐条累加 vs 命令层 MergeEnergyEffects 去重（两发箭矢状态 +20/命令 +10 背离）。修法：CompileOnHit 补 Caster 分支（受益者=施法者/行动者=B6a 口径）+三资产 targetFilter 改 1/补配 OnHit[EnergyGain Caster value 10]+SkillHitResolver 兜底原子同步+ApplyEffects 按 (目标,类别) 去重与命令口径恒等。返修记录=docs/18 决策九返修条+docs/17 §7 返修行。How to apply：①新效果原子落地先核 targetFilter 全分支被编译器消费（枚举存在≠管线生效）；②迁移脚本批量生成原子逐个核对"谁受益"；③命令层有去重/合并口径时状态应用层必须同口径（症状指纹=命令显示量≠下回合快照量）；「战技命中不涨元能/敌人被打了反而充能」类报障先查 targetFilter 与 CompileOnHit 分支。
- [2026-09-25 21:18:37] 【战技获能命中时刻返修已落地（2026-09-25 第二轮报障「使用了战技立刻获得元能」→用户拍板「应当是命中时才给」，编译 0 错 0 警，复检清单已交，随 AI v2 批未提交）】根因（docs/14 §80）：B-S1 时轮只给 Damage/消散命令带了 launchMs 时序，命中链其余产物（StatChange 元能/Heal）零时序元数据——客户端命令枚举时同步应用=片头即跳（前摇未播完元能已 +10）；而 Host 的 ProjectileResolver 接触判定其实已算出精确 hitT，只是没往下传。修法=命中时刻全链透传：hitT → SkillHitResolver.Hit/EffectCompiler.CompileOnHit/CompileAtom 新增 hitSeconds 参数 → EnergyEffect/HealEffect.HitSeconds 字段 → 元能/治疗命令 launchMs 复用为"应用时刻"（union 载荷，0=立即）→ 客户端 BattlePlayer StatChange(元能)/Heal 分支 launchMs>0 走协程到点应用（PlayEnergyDeltaCoroutine，与箭矢落地同步）；同片多命中去重取首条=最早命中；MergeHealEffects 副本保留 HitSeconds；0=立即语义保持（移动获能/协奏/消耗/回合发放/OnCast 治疗）。落档：docs/18 决策七元能获取口径「命中时刻」条+docs/17 §7 返修行。How to apply：①命中类效果（获能/治疗/未来 OnVanish）新增命令映射必须携带命中时刻——Host 已算出的 hitT 勿半路丢弃（只给 Damage 独享时序=B-S1 半截工程）；②union 命令字段跨类型复用同步改 Header 注释（launchMs=发射延迟 vs 应用时刻）；③症状指纹=某数值片头瞬跳而对应视觉事件（箭矢/移动）未发生→查该命令有无时序字段被消费。

### Reference
- [2026-09-16 20:01:18] MC mod gichess（旧项目，Java/NeoForge）：源码 D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\（308文件），jar D:\Tuanjie_editor\gic\.codely-cli\webrefs\genshin-unpack\gichess\my\wg-0.2.d（2026-09-05 随 gichess 77.78GB GI 解包归档整体迁入 webrefs/genshin-unpack/，原 D:\Picture\gichess）。~20+角色，7元素18反应，蒙德延奏/纳塔夜魂已实现。**Why:** GIC 战斗系统 Unity 移植的架构参考。**How to apply:** 仅作战斗系统架构参考；**旧 mod 资产一律不再用（2026-09-16 用户拍板「旧 gichess mod 不要再用」：播报员/派蒙语音 wav、模型、贴图等一切提取物都不再作为 GIC 素材来源，含 TTS 音色克隆样本；2026-09-14 已拍音频/曲目不翻旧 mod，音乐素材由用户自行网找）**。需要查旧 Java 实现时按路径阅读源码。



- [2026-08-10 09:28:39] 技术陷阱与 Bug 修复记录：详见 docs/14-技术陷阱与Bug修复记录.md。
- [2026-09-19 01:58:22] GIC 文档体系：**docs/17-代码架构指南.md=新会话入口文档**（目录结构/核心系统速查/场景清单/配置资产/工作流速查/战斗规划/环境备忘），开工先读它再按需深入。地图：00-13 玩法设计与大地图（07=07-势力机制/ 目录）、14 技术陷阱、15 输入系统、18 战斗决策、19 AI派蒙、20 项目规范（规范权威）、21 商业化与发布边界、23 UI架构重构、24 手势输入层重构、25 指令系统、26 GG系统、27 派蒙语音TTS；子目录=07-势力机制/、active/（22-战斗系统技术设计）、designs/（设计稿）、units/（角色文档）、archive/（16 优化计划已归档）。**How to apply:** 新系统落地/结构性变化后更新 docs/17 对应小节保持时效；UI 面板制纪律=docs/14 §37-39b+gic-new-screen skill（P1-P4 已收官）。**已否决的"故意不做"勿再提**：PopupManager 折叠进 UIManager、god-class Presenter 级拆分（依据 docs/23 §6 P4 行）。






- [2026-08-17 10:32:10] 本机 ffmpeg：D:\Tool\FormatFactory\ffmpeg.exe（7.1 完整构建）。PATH 里没有 ffmpeg，媒体处理直接用此路径。已用它重编码 mc_16x10.mp4 消 VideoPlayer WMF 双告警（baseline profile 消时间戳；VUI+容器双写 bt709 消色彩；x264-params 与 -color_* 需同时给）。






- [2026-09-12 14:24:31] 惯性化参考源码：.codely-cli/webrefs/animation-transitions/InertializationForUnity/（InertiaAlgorithm.cs=五次多项式+四元数/向量数学；上游 github.com/portalmk2/InertializationForUnity，MIT；一手信源=GDC 2018 talk "Inertialization: High-Performance Animation Transitions in 'Gears of War 4'"）。本项目 PetInertializer.cs 已大幅分叉（固定系数多项式+帧间 dq 速度捕获防 ±2π 抽搐+起步拉引），原源码仅作数学对照用。GitHub 拉源码法（api.github.com contents 端点 base64 解码落盘）见 gic-webrefs skill。**How to apply:** 改惯性化数学/移植其它 GoW 技术时先读这份源码对照。

- [2026-09-23 00:05:18] GIC 远程仓库：https://github.com/xiaoding521234/gic（2026-09-09 用户拍板转公开；首推 ~900MB pack 含美术资产）。push/pull 走 Clash 代理 127.0.0.1:7890，api.github.com 直连；GitHub token 获取法见全局 git-pr skill（P/Invoke CredRead，仅进程内使用勿打印）。**SSH 备用推送通道（2026-09-22 探明未启用）**：https 443 被 SNI reset 时 ssh.github.com:443 与 22 端口仍通，本机已有 ~/.ssh/id_ed25519（注释 gic-push-20260922），启用需 GitHub 网页 Settings→SSH keys 手动加 id_ed25519.pub 后 push 改 ssh://git@ssh.github.com:443/xiaoding521234/gic.git。GitHub >50MB 文件仅告警、100MiB 硬限拒推（zh-cn SDF.asset=TMP 动态图集、每补字近整份新增 blob，唯一持续增长大文件；**2026-09-19 推送实测已 64.83MB 超 50MB 告警线（推成），距 100MiB 硬限余量收窄，LFS/filter-repo 决策窗口临近**；**2026-09-21 再入 43.5MB 派蒙待机 anim 文本资产（dd0d818，编辑器重序列化产物——filter-repo 瘦身时可判废弃重生成）+ 1.3MB 纸片人 png，体积压力加重，瘦身决策宜提前**）；2026-09-11 实测服务端 size 907.4MB，瘦身路径=filter-repo（DevKey 清洗有先例）+LFS。secret scanning+push protection 已开（私有个人仓该 API 返回 422，须先转公开再开）。**2026-09-07 DevKey 清洗（filter-branch）重写过 8/30 后的提交 SHA**——docs/skill 引用的旧 SHA 已失效，git show 失败按提交信息 grep 重定位。




- [2026-09-11 23:09:27] [2026-09-11] [reference] codely-docs.tuanjie.cn 文档站抓取法（2026-09-11 实证）：web_fetch 对该站深层路径会回错内容（FAQ 壳/相邻页缓存），正确姿势=curl.exe -sL 直拉原始 HTML（PS5.1 的 curl 别名是 Invoke-WebRequest，必须写 curl.exe；URL 会 301 补尾斜杠），正文在 <article>…</article> 内，PS 抽取去标签后可能残留 NUL 字节导致 read_file 报 binary，需 Replace([char]0,' ')。订阅/积分规则三页：/subscription/pricing-details（套餐价格+积分上限表+增值包）、/subscription/credits-description（积分消耗顺序+LLM/多模态单价表）、/subscription/plan-and-billing（升降级/发票/增购）。


- [2026-09-19 01:58:22] [reference] GI 官方提取模型站点（2026-09-15 定案：**用户指定站=The Models Resource**，models-resource.com/pc_computer/genshinimpact/，访问经验=web-access skill site-patterns/www.models-resource.com.md）：①TMR=最正统 rip 库（779 资产；CF 拦 curl 需浏览器、search 端点 404、手风琴需 CDP eval）②GameBanana（3DMigoto mod 形态）③Nexus Mods ④GIMI 生态（GitHub+Discord，最大提取/移植社区）⑤Sketchfab（骨骼保留参差+DMCA 下架风险）⑥Open3DLab 系（自定义绑定非官方骨架）。**关键判定：官方动作兼容=模型须保留 GI 原始骨架（Bip001 骨名/路径）——第三方站常被重绑定不兼容，套官方 .anim 最稳是本地解包自提（gic-gi-extract 流程）**。中文圈无稳定官方提取站（模之屋/44mmd=MMD）。派蒙来源=TMR asset/328738（docs/19 §2.1 头行已记载；MMD 兜底包=模之屋线，docs/19「模之屋原始 PMX 包」行无误勿改）。


