## Codely Structured Memories

### User


### Feedback








- [2026-08-14 22:58:07] 用户授权 AI 主动维护项目 skill：完成阶段性工作或发现高频工作流/陷阱密集的系统时，可自行新增/更新 .codely-cli/skills/ 下的 skill，无需逐次确认。**How to apply:** 主动沉淀但避免与项目记忆重复——记忆记事实与决策（为何），skill 记操作流程与检查清单（怎么做）；不确定价值时先问。
















- [2026-09-06 20:06:34] 【桥/编辑器异常恢复四则】①桥死心跳（编辑器没开，unity_refresh 拒连）：残留无窗口 tuanjie 进程是许可/Hub 后台件勿误判——自救=拉起编辑器（D:\Tool\2022.3.62t11\Editor\Tuanjie.exe -projectpath 'D:\Tuanjie_editor\gic'）→ Editor.log 出现 CompileScripts → unity_refresh 重连；心跳仍 stale 时可靠触发=最小化+还原焦点循环（SW_MINIMIZE=6 → 2s → SW_RESTORE=9 + SetForegroundWindow），1 分钟内编译+自愈脚本跑完。②【CLI 连不上桥第一排查律（2026-09-06 根因实锤，取代 8/28 旧结论）】桥包 1.0.81（9/4 22:42 实装）心跳文件挪到 **Temp/.com-unity-codely.json**（PortManager.cs 证据=桥唯一写入处），CLI（Cowork 9/2 版 codely.exe）仍读**项目根**同名文件（unityTcpProjectGeneration.js 源码实锤）→ 项目根文件冻结在 package_updating/-1 即 CLI 永判未连接，**编辑器重启无效**（重启后 Temp 端口变化，CLI 还是读不到）。症状：Cowork 界面编辑器未连接、AI 工具集无任何 unity_*/execute_custom_tool 桥工具、但 netstat 有 Tuanjie 监听端口+Temp 心跳 ready。**恢复=Copy-Item Temp\.com-unity-codely.json → 项目根**（副本桥接），验证=TCP 50576 握手返回 WELCOME UNITY-TCP FRAMING=1 或下回合桥工具回归；编辑器重启后端口变副本即过期，需重新同步（用户说一声即可）。8/28 的"重启编辑器恢复法"只适用老桥包写读同位置时代，勿再用。长期修复=等 Cowork/CLI 出配套版本或向官方报版本错位 bug。③桥包自动升级卡死（2026-08-28 实证）重启恢复法已并入②失效说明。④编辑器 License 过期弹窗：点 Exit 重启编辑器即自动续期。**How to apply:** 排查顺序=①netstat+Temp 心跳看桥活没活 ②读项目根文件对比（-1/心跳停更即版本错位）③副本桥接 ④编辑器重启不动辄做（对 9/4 后的版本错位无效）；重启前仍先确认场景 dirty=false。



- [2026-08-28 23:42:41] - [2026-08-28 23:43:00] [feedback] 桌宠商业边界拍板（2026-08-28）：LLM 费用玩家自付——设置里提供 API Key 输入条目（玩家自己的 DeepSeek key），开发者不出钱不内嵌自己的 key；存档中 key 必须加密存储（防文本编辑器裸奔）。**Why:** 个人 Demo 分发场景，开发者代付 API 费不可持续；用户明确"让玩家自己输入自己的 key，而不是花费我的钱"。**How to apply:** 一切联网 AI 功能默认玩家自带 key+加密存档；接 DeepSeek（用户指定）；不要在代码/日志/异常栈出现明文 key。

- [2026-08-29 11:01:45] [2026-08-29 12:10:00] [feedback] 通用规则权威文档=docs/20-项目规范.md（2026-08-29 用户拍板建立，c5a21db 已提交）：编码命名/序列化铁律/编辑器红线/数据入口/UI 与本地化/文档维护/术语表/角色文档规范收拢于此。**Why:** 规范原先只活在 CODELY.md 记忆（不随 git 分发、人类协作者不可见）且散落各文档。**How to apply:** ①新通用规则写进 docs/20 对应小节（一行规则+指针到 skill/docs/14，不复制细节）；②CODELY.md 记忆退回记事实与决策背景；③四类分流=工作流→skill、陷阱→docs/14、决策→设计文档、通用规则→docs/20；④新会话读文档顺序=docs/17（架构）→docs/20（规范）。
- [2026-08-29 16:41:22] [feedback] Skill 触发条件必须写得宽（2026-08-29 拍板"给所有 skill 触发条件改宽"并已全量执行）：description 要覆盖①口语化说法（用户说"改文案"而非"本地化"、"放个音乐"而非"BGM"）②症状式描述（"图加载不出来/点了没反应/为 null"）③兜底句"凡话题涉及X即激活，不确定时也激活"。**Why:** skill 激活=模型语义匹配且只看 description，措辞覆盖窄就漏激活（gic-localization 多次实证）。**How to apply:** 新建/修改任何 SKILL.md 按此三件套写触发条件；用户指出漏激活时先 activate 再干活。


- [2026-09-02 19:56:12] [feedback] 【replace 批量替换陷阱】（四次实证）：① `_静默` replace_all 会把 `_静默淡出权重` 变成 `_silent淡出权重`（前缀包含）——改完必须搜残留混合名；② `坐线` 24 处替换把注释里的中文概念词也改了；③（2026-09-01 祈愿批）**加前缀类重命名的分步陷阱**：先单独改声明为 `_xxx`，再对旧名 `xxx` 做 replace_all——旧名是新名子串，已改好的声明被再改一遍成 `__xxx`（CS0103 15 处才暴露）；④（2026-09-02 移速批）**尾换行不对称陷阱**：old_string 尾带换行而 new_string 不带 → 相邻两行被并成一行（`baseMoveSpeed: 5` + 换行 → `50` 无换行，产出 "baseMoveSpeed: 50    baseLuck: -64" 合并行，YAML 险炸，靠逐行读回发现）。**How to apply**：批量替换前先确认无"以它为前缀/包含它的更长标识符"；补 `_` 前缀重命名必须声明+引用**一次性** replace_all（旧全名→新全名），绝不分两步；行内字段替换 old/new **均不带换行**（如 `字段: -1` 锚定字段名防误伤），跨行块替换两边换行严格对称；改完一批立刻编译，批量改 .asset 后另用 `:值 {2,}\S` 型正则扫合并行，跨文件 rename 漏一处 CS0103 直接暴露。

- [2026-09-01 23:01:44] [feedback] 派蒙双形态功能对齐是持续要求（2026-09-01 三次实证："两版需要一致同步"+"游戏内派蒙也需要"+"难道这些逻辑没有复用？"）：新交互/新功能默认桌面版与游戏内版两形态同款实现，仅形态本质差异（Win32 窗口/IPC）允许单侧。**深一层教训（手势分叉实证）：交互逻辑必须下沉共用层（如 PetHostBase 连击状态机），不许两份各自实现**——桌面版"双击退出"时代的延迟开对话残留在游戏内版"立即开"上分叉，正是两份实现的漂移产物。**How to apply:** 给派蒙加交互或改行为时：①先看 PetHostBase 能否承载（手势/状态机一律共用层）；②确属单侧的触发动作（IPC/直调）留宿主；③同轮实现两形态并同轮重建验证；④改一侧交互前先查另一侧是否有同构逻辑，有则一并下沉。
- [2026-09-02 01:06:08] [feedback] 【现状判定三验法】（2026-09-02 战斗骨架复审实证）：据项目 docs 规划或宣称"断链/必炸"前必须三验：①全库搜调用方（区分死代码/活代码——UnitFactory.CreateUnitWithData 零调用=断链潜伏而非现行 bug，勿说"必报错"）②`git log --all --diff-filter=ADR` 查资产是否曾存在（NormalUnit 全历史零命中=计划名当常量写死的漂移，非改名遗漏）③资产实存+脚本 GUID 反查组件挂载（Unit.prefab 实挂齐 Unit+8 组件）。**Why:** 用户两次纠偏："该文档不可信，根据现在实际更新"+"再次复审避免误判"——docs/17 §7 战斗现状原记载与代码实际有出入（0 个技能子类/SkillConfig.asset 缺失/UnitFactory 路径断链均靠核验发现）。**How to apply:** 项目 docs 的"现状"章节只当索引起点，开工/下结论前以代码为准逐文件核验；"必炸"类结论尤其要先查调用方。
- [2026-09-02 22:50:19] [2026-09-02 20:15:00] [feedback] 【.asset 外部编辑两陷阱】已迁 docs/14 §25（2026-09-02 安柏对齐实证）：①内存滞留——外部改 .asset 后唯一可靠同步=内存对象直改+SetDirty+SaveAssets 写穿；②技能参数双编号——customParams key 用 SkillParamKey 枚举值，勿拿本地化表 id 当资产键。细节查 docs/14 §25。

- [2026-09-02 20:59:46] [2026-09-02 20:15:30] [feedback] 【桥重放脚本必须幂等】（2026-09-02 两次实证，安柏本地化批）：unity_refresh 恢复后编辑器脚本可能被重放执行——AddKey 型脚本第二遍会新建同 key 重复条目、字符串追加型会重复 append。**How to apply**：AI 直改本地化/资产的编辑器脚本一律带幂等守卫：加键前 `Entries.Where(e=>e.Key==key)` 查重、追加文本前 `Value.Contains("{新占位符}")` 已含即跳过、全量重写型天然幂等；脚本输出逐条报 exists/already/rewritten 便于核对实际生效遍数。
- [2026-09-02 22:31:13] [2026-09-02 22:23] [feedback] 【AI 素材 9 切片两陷阱】已沉淀为 gic-ui-9slice skill（.codely-cli/skills/gic-ui-9slice/SKILL.md，2026-09-02 弹窗素材实证）：①Sliced border 按原生像素渲染，超显示矩形用 pixelsPerUnitMultiplier；②spriteBorder ≥ 角饰（max 缩进法测量）。细节/代码模板/流程七步一律查 skill，本条只留指针。**How to apply:** AI生成UI素材→9切片落地类需求先读该 skill。
- [2026-09-03 09:02:13] [feedback] 【UI 图标风格品味标准=原神级极简】（2026-09-03 背包标签图标三轮实证）：用户对 AI 生成 UI 图标的验收标准对标原神 UI。已否决两方向：①复合元素主体（法阵+星、剑盾交叉、币堆+浮雕——元素堆叠即繁复）②两色实心剪影+负空间镂空（用户自提交批次，仍评"不如原神简约"：分量重、米白暗紫对比生硬、缺线条感）。正解方向=单色细线描图标（米白/暖金、对称、大量留白、无填充）；网检佐证：站酷《浅谈〈原神〉美术风格与UI视觉包装》——原神主界面 ICON 扁平、对称式、细线元素。**Why:** 每轮方向错误浪费 5 个生成额度+等待时间，三轮才逼近目标。**How to apply:** UI 图标生成 prompt 一律按"单一简单物体+单色细线+留白"基线起步，禁用实心色块与复合元素起步；风格存疑时先出 1 张试方向再批量。
- [2026-09-03 22:16:49] [feedback] 【加密 meta guid 假阴性】（2026-09-03 两实证：弹窗按钮反查/背包图标接线核验）：Tuanjie 部分资产 meta 的 guid 行本身是密文（XH4f… base64 形态），与场景/prefab 存的明文 guid 不同形——从 meta 文本提 guid 再 rg 场景必零命中（两次误判"未接线"，实为完好）；.codelyignore 记忆里"rg --no-ignore 反查 guid"只适用于明文 meta。**How to apply:** guid 引用判定一律 AssetDatabase.AssetPathToGUID / unity_asset get_info 取真 guid 后比对（docs/20 §1.4 本有此规，勿再绕过）；文本 rg 命中可作"存在"旁证，零命中不可作"不存在"证据。
- [2026-09-03 23:34:10] 【frontier 高成本确认门假报错】generate_image 用 provider=frontier/seedream_pro 等高成本模型**不带 confirm_cost=true 直接调用，返回 "[Error: Could not parse tool response]"**（2026-09-03 三次实证，同参加 confirm_cost=true 立即成功）——是高成本确认门响应解析失败的假象，不是网络/参数问题。**How to apply:** 用户已明确要求最强模型/同意成本时直接带 confirm_cost=true；再遇此报错先想成本确认门，勿无脑重试同参。
- [2026-09-03 23:34:10] 【TJGenerators 轮询命令 Windows 必炸坑】generate_* 工具返回的 poll_command_powershell 内 `function Try` 与 PowerShell 保留字 try 冲突，原样执行必解析失败（2026-09-03 实证，改名 TryPoll 即正常）。**How to apply:** 把轮询命令下发给子代理执行前，将 Try 函数改名（如 TryPoll），逻辑照抄。
- [2026-09-05 00:52:46] 【Prefab 实例引用编辑不持久化陷阱】（2026-09-05 背包页签迁移两次实证）：对场景中 prefab 实例的组件做 C# 赋值（sprite/color/GO 改名）+ SaveScene，部分修改未持久化（新增对象与字段覆写 selectDisc 持久化了，但 m_Sprite/m_Color/m_Name 赋值全部丢失）——疑与 Additive 加载后 prefab 资产先被 SaveAsPrefabAsset 重建的同步时序有关。**How to apply:** ①实例级修引用优先"内容替换法"：不动 override，直接换被引用文件的内容（guid/meta 不变，零风险）；②必须动实例 override 时走 YAML 手术：备份→删块→删 PI 修改项→校验（scene-local 无 guid 的 fileID 引用必须可解析到块；跨文件 fileID: 21300000/11500000/100100000 等子资产引用是合法的，勿计为悬空）→留 flag 自愈让 Unity 序列化器最终验证；③校验 dangling 时区分场景内引用与跨文件子资产引用，否则 700+ 误报。
- [2026-09-05 22:02:54] Windows PowerShell 5.1 不支持 && / || 链式操作符（PS7 才有）——run_shell_command 组合命令用 `;` 分隔或拆开调用，报 "At line:1 char:17" 解析错即此坑（2026-09-05 实证 `git diff --stat && git diff` 失败）。**Why:** 与已记全局的 PS5.1 编码陷阱同源（本机 shell 为 PS5.1）。**How to apply:** 本机 shell 组合命令一律避免 &&/||；此条为项目域记录，若需跨项目生效可迁入全局 PS5.1 陷阱条目（迁全局需用户确认）。
- [2026-09-05 22:26:03] 【"看似冗余"的执行路径可能是隐式行为的载体，删除前必查副作用】（2026-09-05 用户报障实证）：LoadSaveData 首建档分支"少 return、建档后又把刚写的档读一遍"被我判为"无害但浪费的 bug"顺手修掉——实际它承载了"建档后 fall-through 顺带 SyncMissingCards 补全未拥有卡"的功能；删掉后 v11 重置当次会话背包缺未拥有角色卡（count=0 条目全无），用户立即发现。**Why:** 旧代码的怪写法常是历史行为的载体，"重构清理"前必须先问"它为什么这么写"；行为保持型重构 ≠ 顺手修 bug。**How to apply:** ①判定既有代码"冗余/bug"并打算顺手移除时，先证明它不承载功能（调用面/数据流推演），证不了就保留原样或单独提问；②改存档建档/重置路径必须过 gic-save-system skill 的"建档即补全"铁律；③回归修复已入 CreateNewSave（SyncMissingCards+SortAllCategories）。
- [2026-09-07 21:39:41] [feedback] 【PS5.1 中文弯引号也是字符串定界符】（2026-09-07 提交批两次实证）：PowerShell 解析器把 U+201C/U+201D（“ ”）当双引号定界符——含弯引号的中文文案放进 PS 双引号字符串必炸解析器（Unexpected token），\" 转义同样无效。**Why:** 与已记的 && / || 陷阱同源（PS tokenizer 特性）；git-commit-helper 的 -F 落盘铁律只防了 -m 直接传参，用 PS 字符串数组拼消息体再 WriteAllText 仍会踩。**How to apply:** 含引号/特殊符号的中文长文案一律 write_file 工具落盘（无 BOM UTF-8）再供 shell/git 消费，不经 PS 字符串字面量；文案内确需引号用「」；本条与 PS && / || 条目同属本机 PS5.1 陷阱族，跨项目生效可迁全局（迁入需用户确认）。

### Project
- [2026-08-16 19:13:23] GIC"协议核心"：同时回合制卡牌战术战棋（原神IP，最多6人，LAN联机 via Mirror）。核心循环：祈愿解锁→局前选8卡→同时选1行动→攻速排序执行→摧毁核心掠夺→原石结算。7势力可混搭，命座0-3重复出战升命。**定位**：单人开发的个人 Demo/作品集，非商业上线；目标全平台互通（PC+移动+主机）。**Why:** solo dev 资源有限，scope 必须从 GDD 雄心大幅裁剪。**How to apply:** 实现战斗/势力/经济系统时参考 docs/；优先 2人1v1 而非 6 人、2-3 势力而非 7、垂直切片优先于铺量。




- [2026-08-15 21:42:58] GIC 祈愿系统三层架构（2026-08-14 状态机重构后）：WishPoolConfig(SO) 卡池权重 → WishManager 货币+存档 → WishFlowController（纯C#，State: Idle/Drawing/Revealing/Finished）PlanShot() 一次性预计算全部业务返回 WishShotResult（有序 WishRevealStep 列表）→ WishDrawController.PlayStepsCoroutine() 纯动画播放零业务调用。**Why:** 旧代码 6 个 bool 标志位管时序，3 个 bug 全源于业务逻辑与动画协程交错。**How to apply:** 新增卡池建 WishPoolConfig asset 拖到 CharacterEntry.pool；新增动画步骤在 PlanShot 加 step + PlayStepsCoroutine 加 case，不要在表现层调 WishManager；UI 布局在 WishScreen 场景 Canvas/WishDrawRoot 下改；机制数值见 docs/12。
- [2026-08-10 00:49:23] GIC 构建/APK：测试用 Tools/导出 APK/快速导出（Mono+ARMv7，约3分钟），发布用正式导出（IL2CPP+ARM64+Stripping Low）。脚本 Assets/_Scripts/Editor/Tool/QuickAPKBuilder.cs 自动切 Android 平台——**必须切**，否则 Addressables 按 Windows 打包（DXT/Windows 路径）→ 真机黑屏无声音+体积大 200MB；构建后不切回原平台。也可直编 ProjectSettings.asset 三字段：scriptingBackend.Android(0=Mono,1=IL2CPP)、AndroidTargetArchitectures(1=ARMv7,2=ARM64)、managedStrippingLevel.Android(3=Low)。APK ~270MB vs Windows ~2GB 正常。ADB：D:\Tool\2022.3.62t11\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe；编辑器崩溃看 %USERPROFILE%\AppData\Local\Tuanjie\Editor\Editor.log。









- [2026-08-16 19:13:35] [project] Assets 清理定案（2026-08-16）：Material/Materials/Resources/Materials 三目录 8 个材质曾误删已全部 git 恢复，**勿再清理**（UIBlur/WishSmoke 确认在用，其余存疑也保留）；已删定案：Assets/Editor 空目录、Scenes/TestScene.scene、StreamingAssets/mc.mp4、Assets/AI/PRJ_SUMMARY.md、Assets 根 6 个 CUsers* 垃圾文件。故意不做：NewAudioMixer、Art/Wish 与 WishArt 改名（Addressables 地址约定）；保留 GlowTest.scene+CardGlowTest.cs。**How to apply:** 不再动这批资产。


- [2026-09-02 00:39:14] GIC 大地图 v7.0 基线：110 片母版原生像素瓦片（Export/all_map_source.jpg 21900×18576）+全链路统一 SourcePixel×unit 换算，换图错位已根治；跳非当前区域首跳冷加载糊 1-3 秒——**用户已拍板保持现状**（全预载≈220MB / 原神式 LOD 均否决，勿再提案）。**How to apply:** 换图/标定/新区域先读 gic-map skill；状态快照 .codely-cli/HANDOFF-大地图.md；细节 docs/13 §9.1、docs/14 §8.2.1。


- [2026-08-27 14:50:02] TextMesh Pro/ 目录被 git 忽略（材质改动不入库，重装 TMP 时需备份该目录），排查它时 rg/Select-String 需 --no-ignore 才能搜到。










- [2026-09-02 00:39:17] 命名规范（2026-08-29 迁移执行完毕）：规则本体=docs/20 §1.1（标识符英文+[InspectorName 中文]；Data 层枚举成员仍中文待专项）。**FormerlySerializedAs 约束**：CoopScreen 5 处旧键仍在 CoopScreen.unity=必留勿删（删则丢数据）；WishPoolConfig 6 处旧键已消失=冗余。PetPrefs pet.json DTO 键已英文化（旧测试存档需重输 key）。






- [2026-09-02 19:56:17] Unity YAML/本地化中文 \uXXXX 转义陷阱+改名工作流：.unity 里中文字段名序列化为 \uXXXX 转义键；Localization 字符串表 *.asset 的 m_Localized 中文值同样转义（2026-09-02 实证：直搜"体力/元能"零命中）——rg/Select-String 按中文直搜这两类文件必零命中，要么搜转义形式、要么 PowerShell 解码（\uXXXX 与 \xNN 混合）后过滤；场景迁移验证要么按 fileID 搜；判引用完整用 SerializedObject.FindProperty(中文名).objectReferenceValue。**改名铁律**：序列化字段改名必挂 [FormerlySerializedAs("旧名")]，验证链=编译管线→FindProperty 断言引用非 null→保存场景固化新名→OpenScene 重开二次断言→磁盘 YAML 确认旧名消失（转义形式）。**How to apply:** 本项目 Inspector 中文字段遍布，任何字段改名都走此链；查/改本地化文案先想转义。





- [2026-09-02 00:39:20] 桌宠构建/动画两实测事实：①BuildPlayer 可成功覆盖正在运行的桌宠 exe（构建前不必杀旧进程，启动前必须杀）；②legacy Animation.Play() 播完的 Once clip 重播 time 正确归零从头播——排查切换类动画 bug 勿再疑"重播冻结在结尾姿势"。构建协议细节见 gic-pet / gic-editor-longtask skill。









- [2026-08-27 14:49:34] 桌宠动作切换/丝滑度优化已暂停（2026-08-27 用户两次拍板"暂时不修改了"；已提交 8f909b3，"动作期间内"不顺畅遗留未修）——后续会话勿主动重开此题；用户重开时先开 PetInertializer.打印诊断 看 Player.log 捕获行，数据说话（细节见 gic-pet skill 铁律 10）。

- [2026-08-27 14:49:40] .codely-cli/ 与 .codely/ 在 .gitignore 中——skill/HANDOFF 等文件不进 git，属本地持久（勿尝试提交它们）；只有 docs/ 与 Assets/ 等变更进 git。







- [2026-08-29 11:19:24] RoomManager.Cleanup 仅作应用关闭钩子，勿在断线时调用（曾致同会话重连名册失联）。联机分层规则（PlayerManager 数据层/RoomManager 流程层、网络 Handler 常驻订阅不退订）已收拢 docs/20 §1.2。
- [2026-08-29 11:19:28] 场景脚本接线深拷贝铁律（P10 三次返工教训）：跨场景预设含场景级 override 子物体的面板必须 Object.Instantiate(场景实例) 深拷贝；静态预设实例保存为未激活时，运行时使用前必须显式 SetActive(true)。其余（PrefabUtility 禁用/SerializedObject 赋值写法/保存重开断言闭环/编辑器红线）已收拢 docs/20 §1.3、§1.5。
- [2026-08-29 11:19:31] 桌面版任务栏置顶守卫（2026-08-28 目检通过，a7f4320）：0.5s 查 Z 序上方 8 步内 Shell_TrayWnd/Shell_SecondaryTrayWnd→重挂 HWND_TOPMOST——只对任务栏触发，不打置顶战争；根因=点击任务栏激活被抬进 topmost 链我们之上（VPet 无此守卫属场景未暴露）。游戏内坐参数与 y=0 视口陷阱已记 docs/19 §6.4。


- [2026-09-02 00:39:24] 【迁移防丢值铁律】（2026-08-29 命名迁移丢值事故教训，已修复）：①"改默认值+重保存"路径会顶替一切场景值≠代码默认值的字段——迁移/批量改字段后必须 diff 值而不只看键名（排查"早期正常现在坏"回归同法：先做迁移前后场景值级 diff，工具 .codely-cli/tmp/value_diff.ps1 按块比值序列）；②删除 FormerlySerializedAs 前必须按资产全量扫描旧键（Resources/ 下 prefab 不在场景重存范围，会成孤儿键丢引用）；③编辑器工具引用运行时字段必须跟字段名走（改名后中文 FindProperty 静默断裂）。











- [2026-09-02 00:39:27] 派蒙架构审查治理（2026-08-31）：报告+执行状态表在 .codely-cli/HANDOFF-派蒙审查.md；批 1-7 摘要见 docs/19 决策表 2026-08-31 行（P0 修复/死代码删除/全量英文化/pet.json v5 迁移/对话装配下沉 PetHostBase/性能健壮性/行为层解耦）。方案B 工具家族拍板保留不删。剩余待拍板：游戏内阴影门控、DevKey 吊销；大文件 partial 拆分未做（收益低）。

- [2026-09-01 21:04:05] [project] 米哈游级代码质量审查体系已建（2026-09-01 拍板"米哈游级代码质量+优秀架构设计"）：gic-code-review skill（.codely-cli/skills/gic-code-review/SKILL.md，七维 checklist：A架构依赖最高优先→序列化→UI本地化→数据存档→通用质量→文件位置→docs/14雷区对照）+ docs/20 §1.7 新文件位置决策表（编辑器/运行时→归属层判定+文件名=类名+一文件一类）。**Why:** 审查与项目规范此前脱节（通用 code-review skill 不带 GIC 铁律），用户要大厂标准。**How to apply:** "审查/review/检查代码/架构"类需求先激活 gic-code-review skill；大功能/阶段完成后主动建议跑审查；报告 Critical/Suggestion 必须带 docs/20 小节或 docs/14 编号出处，无出处意见标"通用经验"；审查只报告不代改（用户说修才改）。
- [2026-09-02 22:31:39] [2026-09-02 22:23] [project] 弹窗素材体系拆分定案（2026-09-02）：轻提示=ToastDialog.prefab（bottom.png 原横条，Boot.unity 的 PopupManager.toastPrefab 已指向）；中弹窗=PopupDialog.prefab（模态消息）+InputPopupDialog.prefab（输入框），面板/按钮素材在 Assets/Resources/UI/Popup/。**Why:** 原先模态与 toast 共用一个 prefab（toastPrefab 为空时兜底 popupPrefab），直接换中弹窗素材会连带改 toast 外观。**How to apply:** 改弹窗/轻提示外观前先分清两 prefab；弹窗类素材生成与 9 切片落地走 gic-ui-9slice skill（内含现状索引）。

- [2026-09-03 23:34:10] [2026-09-02 22:25] [project] 【.codelyignore 搜索陷阱】本项目 .codelyignore 屏蔽 Assets/**/*.png、*.prefab、*.asset、*.mat、*.wav 等——search_file_content/glob 在这些路径**静默零命中**（极易误判"无引用/文件不存在"），查 guid 引用/资产内容前一律 shell `rg --no-ignore`；read_file 直接读不受影响；analyze_multimedia 的 absolute_path 参数也走同一 ignore 校验（2026-09-03 实证：Assets 下 png 报 ignored by .codelyignore），分析 Assets 下图片先复制到 .codely-cli/tmp 再传。**How to apply:** 引用排查（素材 guid、prefab 反查）先想路径是否被 ignore，被 ignore 就走 rg；多模态分析被 ignore 的资产先复制出来。

- [2026-09-03 00:39:06] [project] UI 素材分辨率基准（2026-09-03 普查实证，186 PNG）：全项目按钮素材 500~512px 一档（MainHall 导航钮 512×512×10、back/normal_button 500×135、弹窗钮 500×103）；Canvas 参考分辨率 2560×1440（仅 PaimonPet 960×540）；medium_popup 511×512 对显示 ~900×500 属偏低档。纹理 raw 预算大头：Art/Wish 470MB（8 张 4096px）、Art/PositionBack 172MB（3200px）、UI/Cards 107MB（30 张 800×1200）；弹窗族 4 张仅 2.1MB 占 0.2%。**How to apply:** 弹窗/按钮素材保持 500~512 一档即可，勿超标也勿低于显示尺寸；内存优化议题优先看 Wish/PositionBack/Cards，不是弹窗族。
- [2026-09-05 00:18:44] 【背包页签图标接线映射（2026-09-03 解析铁证；2026-09-04 原神截图复刻；2026-09-05 对齐原神分层架构）】8 页签→文件族映射不变（角色0→unit_button/造物1→creation/建筑2→building/装备3→equipment/消耗品4→normal/材料5→valuable/货币6→currency/任务7→quest），场景 GameObject 名错位陷阱与加密 meta 陷阱不变。**现行架构（原神式）**：每页签 1 张白 glyph（TabGlyphs/glyph_1..9.png，g2 纹章备用）+ 公共圆盘 disc.png；NormalIcon=glyph×灰蓝(0.647,0.627,0.616)，SelectIcon 已拆为 SelectDisc(盘×米白(0.914,0.886,0.839))+SelectGlyph(glyph×深蓝灰(0.353,0.384,0.447)) 两层，ItemCategoryView 新增 selectDisc/selectGlyph 字段（PopLayers 同步缩放动画）；旧 16 张两图制 PNG 无人引用但保留备回滚。**Why:** 用户要求对齐原神"单 glyph+公共盘+运行时染色"架构。**How to apply:** 换图标=换 glyph PNG 或调三色 Image.color，勿再烘焙颜色进图；迁移/批改场景用 TabGlyphMigrationTool（MenuItem Tools/TG/背包页签图标重构迁移，幂等），无 execute_custom_tool 时用"flag 文件+InitializeOnLoad 自愈"协议：写 .codely-cli/tmp/TabGlyphMigration.run.flag → 编辑器下次刷新自动执行并写 result.txt（用户全屏应用时 SetForegroundWindow 偷不到焦点，refresh 不触发，flag 会留到下次刷新自愈）。
- [2026-09-05 19:03:43] [project] 网络调研资料统一库 webrefs（2026-09-05 建立）：所有网络下载的源码/论文/文章/搜索元数据统一入 .codely-cli/webrefs/（按主题子目录，索引=webrefs/README.md），禁止散放 tmp/。**Why:** 用户拍板"把所有来源于网络搜索的知识放到一个单独文件夹里，并写skill说明流程"；此前 InertializationForUnity/VPet/eSheep 源码散落 tmp/ 难寻。**How to apply:** 任何网络调研产出按 gic-webrefs skill 流程入库+登记索引；桌宠"动作过渡丝滑"议题（2026-09-05 用户重开，对标原神无感知切换）的开源方案调研在 animation-transitions/ 子目录；**2026-09-05 当日已完成实测数据采集**（printDiag 常开，Player.log 14 捕获：回待机近零成本、贵在待机→动作进入）并建实施 HANDOFF=.codely-cli/HANDOFF-动作过渡丝滑.md（P0 A/B 场景值实验定位快慢主诉→P1 骨分组→P2 内容侧衔接），下个有桥会话照 HANDOFF 跑闭环，勿再"先开诊断"。
- [2026-09-05 22:21:21] 存档系统 2026-09-05 全面优化落地（版本 11）：分区重组（progress/settings/pet 嵌套）+ Modify/ModifyNow 统一变更入口（全库迁移完毕，合法直调仅剩 GameScene 退出/后台兜底与祈愿流程末）+ 初始存货外置 InitialSaveConfig SO + 写盘失败 OnSaveFailedEvent→toast；细节在 gic-save-system skill 与 docs/17，勿在记忆重复。**存档格式外部契约两处（v12+ 改格式必查）**：①PetEditorAutoLauncher.ReadClosePetOnExit 磁盘探针（已复用真实 PlayerSaveData 类反序列化，防探针漂移——勿再建影子 DTO）②pet.json 双通道写（SettingsScreen.Pet 的 WriteChatCipher/WriteChatProvider 同步）。**初始化自愈已完成（2026-09-05 22:19 result.txt 全 created）**：InitialSaveConfig.asset 已建、Save_WriteFailed 键已入表（zh-Hans/zh-TW=简中值、en/ja/ru=英值兜底——正式翻译走 CSV 流程补）；若见"InitialSaveConfig 未加载"报错，先查资产在不在，重建走 Tools/TG/存档系统初始化配置（幂等），勿当 bug 修。
- [2026-09-06 00:29:04] [project] 背包卡组管理 2026-09-05 重构落地（王者荣耀式，同日 v2/v3 迭代）：底部 Buttons 容器内长条按钮（**v3=原神"品质顺序"下拉条样式：Wish/UI/button.png 米白胶囊 640×80 九切片 border=37 + 左侧粗深墨蓝 #3C4A5C 文字 + 右侧程序化▼三角（Resources/UI/Backpack/dropdown_triangle.png，工具生成）**）+ Canvas 根下 DeckSwitchPanel。动态卡组 1~30（Min=1/Max=30/Default=7 常量在 CardManager），行由 DeckRow.prefab 运行时实例化、ScrollView 滚动；预览=完整 Card 共享 CardPool（OnlyDisplay 0.6）；**v3 拖拽=拖动行挂 dragLayer（面板顶层、不受遮罩/滚动影响、全程跟指针+边缘自动滚），拖把手触发，松手提交换位**；**v3 打开面板行逐个淡入上滑**（rowEntranceInterval 0.028s，行 CanvasGroup，拖拽/关闭时 StopEntrance 复位）。数据：SaveProgress.deckNames（""=本地化默认"卡组N"）+ deckOrder 排列表，deckId 稠密恒等（删除时全量重映射 inDecks/order/currentDeck 切后继），deckNames.Count=卡组数事实源；CardManager.decks 自愈属性。密语=DeckCodeCodec "GIC1."+Base64URL（varint ≤8 张），导出直入剪贴板、**导入弹窗自动预填剪贴板密语**、导入目标=当前卡组。**v3 关键陷阱（已入 docs/14 §27）：InputPopupDialog 根=自带 Canvas+scale0 占位节点，必须实例化到非 UI 父级（screen.transform）——挂主 Canvas 层级内=嵌套 Canvas 不接管=不可见静默失败（v2 改名/导入双"没反应"根因）**。本地化：UIText 11000 段（11001-11010=Deck_*），PopupText 6-15。迁移工具=Tools/TG/卡组管理面板迁移（DeckBarMigrationTool，幂等 flag 自愈；坑：编译失败时旧程序集钩子仍会消费 flag 跑旧版——重跑前确认编译通过）；CSV 已同步。
- [2026-09-06 00:59:17] [project] 卡组管理 v4 拍板（2026-09-06，接 v3 条目）：①卡组名上限 10 字=CardManager.MaxDeckNameLength（SetDeckName 截断防御+改名弹窗 maxChars=10）②改名弹窗预填"当前显示名"（自定义名或默认"卡组N"，DeckSwitchPanel.ResolveDisplayName）——不再预填空串 ③切换条/行内文字一律不加粗（fontStyle=0）——背包页其它按钮文字（分类切换"角色"等）均为普通字重，粗体是 v3 误加已移除 ④10 字上限下的布局收紧：条上名 40 号（10×40=400≤420 单行）、行内名 36 号/宽 370（360≤370 单行）。
- [2026-09-06 01:49:33] [project] 卡组管理 v5 收尾拍板（2026-09-06）：①选中卡组行=整行金色实底（无贴图纯色块铺满整行，色值调暗版 (0.75,0.62,0.32)——首版 (1,0.84,0.43) 用户嫌亮；旧圆角框素材方案因 ppuMult 设反（0.32 应为 ~3）渲染成六边形已废弃，rounded_chess_frame 引用已从工具移除）；②ScrollView 顶部 offsetMax=-140 给右上关闭按钮让位（-96 时行列表会盖按钮）。**flag 自愈协议缺陷已修（通用陷阱）**：工具原先"先删 flag 再执行"——OpenScene 在 Play 模式被引擎禁止，Play 中触发的运行失败会吃掉 flag 永不重试；已在 AutoRunHook 加 `EditorApplication.isPlayingOrWillChangePlaymode` 守卫（Play 期不消费 flag，退场后下次刷新自愈）。推论：编辑器窗口标题 "gic - 场景名" ≠ Play 状态判据（编辑模式开 Boot 也叫 gic - Boot），Play 判定看日志 HandlePlayExit 痕迹或直接试跑看 OpenScene 异常。
- [2026-09-07 21:04:43] 祈愿命运之缘物品化落地（2026-09-06）：相遇之缘(ItemName.AcquaintFate=1004)/纠缠之缘(1002)为背包物品，射击自动消耗（纠缠优先→纠缠之线粉线/相遇之线金线，都没有→白命运之线）；每满 20 累计星辉赠 1 相遇之缘、每满 200 赠 1 纠缠之缘（阈值常量在 SaveProgress，AddStarglitter 按跨越档位差值发放），两物品每份数量均 1；v11 旧档读档自动补偿（PlayerSaveData.MigrateFateItems：未用相遇余量→相遇之缘、满200档→纠缠之缘）。同日修复本地化 ItemName/ItemDescription 双层错位历史事故（2026-07-31 起：语言表值挪枚举位但 Shared 层未 Remap，蒲公英酒显示成"勾玉"一个月，详见 docs/14 §28），两表 Id 已与枚举全量对齐、CSV 已重导出——**勿用 2026-09-06 之前导出的旧 CSV 导入（旧 Id 布局会错挂）**。**Why:** 用户拍板的祈愿系统改版；加键时发现 1004 被勾玉占用顺藤查出历史事故。**How to apply:** 改祈愿/物品本地化前先读 docs/12 §4.3/§4.1.1 与 gic-localization skill 双层结构铁律；新档初始 0 纠缠之缘/0 相遇之缘（2026-09-06 用户拍板，InitialSaveConfig.asset 与 SaveSystemSetupTool 重建数据已同步为 0），命运之缘全靠星辉里程碑获得；**星辉/相遇之缘/纠缠之缘 maxPrepareCount=0 不可入卡组（同日拍板，遗留 inDecks 由 SaveProgress.EnsureValid 不变式清理）**；**纠缠之线差异化（同日拍板）：升级必升 1-4 次（权重 10/40/40/10——2026-09-07 拍板调整，WishPoolConfig.intertwinedUpgrade*Weight，相遇之线保持 0-4 档 40/30/20/7/3）+ 射击倒计时 12 秒（WishDrawController.GetCurrentShotTimeLimit/intertwinedShotTimeLimit）**。

- [2026-09-07 20:16:26] [project] 纠缠之线表现 v2（2026-09-07 拍板"叠加不改"，取代 9/6 粉色替换版）：相遇揭示表现（金色抖动发光/星级色光柱/边缘泛光）对纠缠**完全保留**，差异化只做叠加——瞄准期（12 秒）圆环风暴（画面中心出生→easeOut 迅速放大 140→2200px+顺/逆时针自旋 240°/s±30%，生成间隔 0.55→0.12s 在 3 秒爬到峰值后保持，开火停发自然淡出；RingStormCoroutine 单协程仿星辉雨模式）+ 瞄准暗幕（背景图 back.png 偏亮，Additive 环需压暗底，VeilAlpha 0.5 可关）+ 倒计时染粉/进度条线色呼吸（保留）；开火双线光矢/全屏粉闪、氛围粒子粉色脉冲（保留）；**v1 粉色发光/抖动倍率/粉色光柱/升级冲击环已全部回退**（StarVisualConfig 三字段已删、SO 孤儿键已清）。素材=Resources/UI/Wish/intertwined_ring_0/1.jpg（AI 生成参考 back.png 科幻装置风格，黑底+Additive 自动隐形；ring_1 左上角杂点已对称裁边 250px 修复）。细节 docs/12 §6.7。**音效拍板=默认不做**：shootSFX/cardHitSFX 场景未赋值，全祈愿射击/命中无声是已知缺口非 bug，勿误判修"没声音"；要做时 generate_sound_effect 生成射击/命中/纠缠变体三条+场景接线+响度自检。
- [2026-09-07 21:39:41] [project] 纠缠射击派蒙瞄准反应（2026-09-07 拍板+用户验证通过，d2bc1d6 已提交）：新增 WishDrawController.OnIntertwinedAimStart 瞄准首帧事件（参数=1-based 发数；**故意延一帧经 InputCoroutine 发**——AutoRunner/PlayerObserver 都在 StartWish 返回后才挂订阅，同步发会漏第一发）；派蒙代抽/玩家手抽两路径同款 Domagic 动作+话语（描述主语框架"你/旅行者拿出纠缠之缘"区分+IsAutoDraw 防串场照旧）；AI 自动抽卡在 12 秒纠缠仪式上多等 4 秒（intertwinedAutoExtraWaitSec，随机点击窗口 0.4~1.35s→4.4~5.35s）；UIText 新键 PetWishIntertwinedAim(9063)/PetWishPlayerIntertwinedAim(9064) 五语言、CSV 已同步重导出。**How to apply:** 派蒙祈愿新反应类型按 PetWishAutoRunner 反应规则表扩展（docs/19 §6.5）；文案改值走 gic-localization 直改表/CSV 流程。
- [2026-09-07 22:55:35] [project] DevKey 已于 2026-09-07 首推前清洗出 git 历史（filter-branch index-filter）：2026-08-30（36605fe）及之后的提交 SHA 全部重写，8/30 前的 SHA 不变——docs/skill 里引用的 8/30 之后的旧 SHA（如 1129959）已失效，git show 失败时按提交信息 grep 重定位。PetApiKeyCrypto.DevKey 同日改本地文件制：项目根 devkey.txt（gitignored，本机已就位）→ GIC_DEVKEY 环境变量 → 空；key 从未泄出，无需供应商吊销。**How to apply:** 删档测试后聊天 key 为空先查项目根 devkey.txt 在不在；勿以任何明文形式把 key 写回代码或提交。
- [2026-09-08 00:27:19] [project] 派蒙对话人设 v2.2 + 字体 tofu 根治（2026-09-07~08 收口）：人设=身份锚定"派蒙本人非AI"+九条说话规则+三句示例语料+显式禁特殊符号（模型会模仿 system prompt 符号风格，①「」 禁令保留）+**欸嘿降频（v2.2，用户实测"过多"；示例语料改哼哼开头是主要降频手段）**。**tofu 根因当日纠错（初诊是错的）**：zh-cn.ttf（SDK_SC_Web，MD5 与原神安装 MiHoYoSDKRes\...\font\zh-cn.ttf 逐字节一致——用户从原神提取的全库）文件级 cmap 口语字全有；真根因=zh-cn SDF 多图集关+单张 4096² 烘焙 1569 字已满，Dynamic 补字必败。**诊断铁律：TMP_FontAsset.HasCharacter=烘焙字形表，Font.HasCharacter=字体文件 cmap，两层勿混；Dynamic 字体资产建时必开多图集**。修复=开多图集+预烘 77 口语字（1646 字形/2 页）；**项目字体全量统一 zh-cn SDF（用户拍板，原神提取款）**：InputPopupDialog/CoopScreen 原 SourceHanSans 引用已切至 zh-cn；zh-cn SDF.asset 已 `git add -f` 首次入库（.gitignore 吞 TextMesh Pro 目录坑复发，64.7MB 超 GitHub 50MB 告警线）。**操作流程已沉淀为 skill gic-pet-chat**（人设三处同步/口癖调频/对话日志通道/tofu 排查/构建验证闭环）；字体史细节 docs/14 §30、人设构成 docs/19 §6.5.1。
- [2026-09-08 19:17:30] [project] 三势力机制+商业化分析定盘（2026-09-08 会话，高级策划视角产出，仅存对话未落 docs）：**商业化结论=带原神IP形态可行性为零**（角色名/术语/7元素18反应命名/GI解包字体+RigMesh+MMD模型+OST 全侵权面，无"安全商业化"路径；去IP后机制本体——同时回合+攻速错峰+契约谈判+命座——原创可独立成立，fork 换皮留作核心循环验证后的选项，勿现在做）；三条纪律=全渠道不收费（打赏与项目解绑）/启动页加"非官方同人，与miHoYo无关"免责声明/解包资产永不入发布渠道。**三势力定位**：璃月契约=拉新钩+观赏性之王（对人谈判品类独一份，1v1 切片只能展示三成潜力）、稻妻连携=留存引擎（操作爽，但延迟敏感是全平台互通最弱一环）、蒙德延奏=门槛垫底石（机制最成熟、与攻速管线咬合最漂亮，但存在感靠变奏可视化——B5 表现层建议把"三势力视听签名"列验收）；纯/混骨架成立（元能/摩拉/勾玉三资源轴不互抢，延奏给非蒙德20元能=显式混搭激励，变奏深度=纯色激励）。**待拍板/缺口清单**：璃月"任意1契约解除→全部解除"连坐规则意图、勾玉归属（玩家级/角色级）、连携在攻速管线中的插入次序、契约对1-2星AI单位与尸体的规则、稻妻触发条件表+衍生玩法（docs/08 稻妻条目缺位）、时停×连携窗×攻速延迟三种时间侵入叠加优先级、6人局契约次数限流、蒙德-only切片验不了混搭验收目标（B8前补行秋+1璃月最小集或该验收改期C批次）。**Why:** B批次开工与C批次排期的前置输入，商业化问题一次论证定盘避免反复。**How to apply:** 做璃月/稻妻落地或补 spec 前先按此清单核对；商业化/上架/收费类问题直接引用结论勿重新论证。

### Reference
- [2026-09-05 20:04:54] MC mod gichess（旧项目，Java/NeoForge）：源码 D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\（308文件），jar D:\Tuanjie_editor\gic\.codely-cli\webrefs\genshin-unpack\gichess\my\wg-0.2.d（2026-09-05 随 gichess 77.78GB GI 解包归档整体迁入 webrefs/genshin-unpack/，原 D:\Picture\gichess）。~20+角色，7元素18反应，蒙德延奏/纳塔夜魂已实现。**Why:** GIC 战斗系统 Unity 移植的架构参考。**How to apply:** 需要查旧 Java 实现时按路径阅读源码。

- [2026-08-10 09:28:39] 技术陷阱与 Bug 修复记录：详见 docs/14-技术陷阱与Bug修复记录.md。
- [2026-08-16 10:58:59] GIC 文档体系：**docs/17-代码架构指南.md 是新会话入口文档**（目录结构/核心系统速查表/场景清单/配置资产/常用工作流速查/战斗规划/环境备忘），开工先读它再按需深入；docs/00-12 玩法设计、13 大地图、14 技术陷阱、15 输入系统、16 优化史、18 战斗决策。**How to apply:** 新系统落地/结构性变化后更新 docs/17 对应小节保持时效。
- [2026-09-02 00:39:31] GIC 未建 skill 的系统：祈愿（记忆覆盖三层架构）、APK 导出（记忆覆盖操作要点）、战斗系统（未实现，落地后建 skill）。

- [2026-08-17 10:32:10] 本机 ffmpeg：D:\Tool\FormatFactory\ffmpeg.exe（7.1 完整构建）。PATH 里没有 ffmpeg，媒体处理直接用此路径。已用它重编码 mc_16x10.mp4 消 VideoPlayer WMF 双告警（baseline profile 消时间戳；VUI+容器双写 bt709 消色彩；x264-params 与 -color_* 需同时给）。
- [2026-08-18 01:26:41] 大地图资料分两处：操作工作流与铁律在 gic-map skill（.codely-cli/skills/gic-map/SKILL.md）；.codely-cli/HANDOFF-大地图.md 只留状态快照+待拍板清单。做大地图任务先读 skill。
- [2026-08-23 23:20:00] 派蒙桌宠资料分两处：操作工作流与铁律在 gic-pet skill（.codely-cli/skills/gic-pet/SKILL.md，修复→构建→启动→用户目检闭环、建成判据只信 Editor.log RESULT 行、构建竞态守卫勿删、双进程注册表/存档隔离、编辑器红线）；设计/架构/子系统状态见 docs/19-AI派蒙.md。做桌宠任务先读 skill。
- [2026-09-02 00:39:33] [reference] 音频响度体系（docs/14 §11 + gic-audio skill 响度规范节）：位置 BGM 响度基线白天 ≈-11.5 LUFS / 夜晚 ≈-13.5 LUFS；平台下载 OST 与游戏内提取音源天然差 2~6 LUFS，提取版入库前必须两遍 loudnorm 对齐；峰值归一≠响度归一。音频系统问题（音乐冷却/Push/Pop/响度）先读 gic-audio skill。

- [2026-08-21 19:05:00] [reference] AI派蒙桌宠资产与工具链位置（细节见 docs/19）：模之屋 PMX 原包 D:\Tool\PaimonModel\（**定案为唯一模型基底**）；原神本体 D:\Game\Genshin Impact\Genshin Impact Game\；AnimeStudio 提取工具 D:\Tool\AnimeStudio\（AssetMap maps\gi70.json + 看板动作 out_kanban\）——**提取路线已放弃**（GI 网格反优化无解、动作与 MMD 骨骼不通），仅备查不再投入；项目入库 Assets/Art/PaimonPet/（MMD 模型在用；24 个 GI 动作弃用备查）；PMX→FBX=Blender 4.2.3+mmd_tools v4.5.13（VMD 动作也走此链）。**How to apply:** 做桌宠任务先读 docs/19；动画来源在 VMD/手 K/AI 中选，不再研究 GI 提取。
- [2026-08-26 23:43:45] [reference] 编辑器长任务（构建/烘焙/批量导入 >1分钟）一律走异步协议：Schedule 入口毫秒级返回+后台 PowerShell 基线计数轮询 Editor.log、期间零桥调用——详见 skill gic-editor-longtask（2026-08-26 定案）。现有入口：PetSpikeBuildTool.ScheduleBuild。做任何编辑器耗时操作前先读该 skill。
- [2026-09-05 18:34:30] [reference] 惯性化参考源码位置：D:\Tuanjie_editor\gic\.codely-cli\webrefs\animation-transitions\InertializationForUnity\（2026-09-05 自 tmp/ 迁入统一网络资料库 webrefs，总索引=webrefs/README.md）（InertiaAlgorithm.cs=五次多项式+四元数/向量惯性化数学、PostInertializer.cs=Animator 后处理用法、PostInertializerTransitionProfile.cs、README.md）。上游 github.com/portalmk2/InertializationForUnity（MIT，GoW4 SIGGRAPH 2017 supplemental 论文直译实现）；一手信源=GDC 2018 talk "Inertialization: High-Performance Animation Transitions in 'Gears of War 4'"（gdcvault.com/play/1025331）+ GoW4 SIGGRAPH 2017 supplemental PDF。本项目实现 PetInertializer.cs 已大幅分叉（固定系数多项式形态+帧间 dq 速度捕获防 ±2π 抽搐+起步拉引），原源码仅作数学对照用。GitHub 拉源码方法（2026-08-27 实测）：api.github.com 直连可用但 archive zip/git clone 被重置——走 API contents 端点取 base64 解码落盘。**How to apply:** 改惯性化数学/移植其它 GoW 技术时先读这份源码对照。
- [2026-09-07 22:55:35] [reference] GIC 远程仓库：https://github.com/xiaoding521234/gic（私有，2026-09-07 首推全量 190 提交 ~900MB pack 含美术资产）。push/pull 走 Clash 代理 127.0.0.1:7890；GitHub API 操作用 cmd.exe "type _cred.txt | git credential fill" 取 gho_ token（本机无 gh CLI），账号 xiaoding521234。GitHub 对 >50MB 文件仅告警、100MiB 硬限拒推——Paimon_GIRigMesh.asset 97.9MiB 逼近上限，资产再增大需 LFS 或拆仓。
