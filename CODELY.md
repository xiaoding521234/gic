## Codely Structured Memories

### User


### Feedback








- [2026-08-14 22:58:07] 用户授权 AI 主动维护项目 skill：完成阶段性工作或发现高频工作流/陷阱密集的系统时，可自行新增/更新 .codely-cli/skills/ 下的 skill，无需逐次确认。**How to apply:** 主动沉淀但避免与项目记忆重复——记忆记事实与决策（为何），skill 记操作流程与检查清单（怎么做）；不确定价值时先问。
















- [2026-09-02 00:38:40] 【桥/编辑器异常恢复三则】①桥死心跳（编辑器没开，unity_refresh 拒连）：残留无窗口 tuanjie 进程是许可/Hub 后台件勿误判——自救=拉起编辑器（D:\Tool\2022.3.62t11\Editor\Tuanjie.exe -projectpath 'D:\Tuanjie_editor\gic'，可最小化后台起）→ Editor.log 出现 CompileScripts → unity_refresh 重连；重启后心跳仍 stale 时 PowerShell ShowWindow 前置编辑器窗口触发资产刷新即恢复（2026-08-27 实证）。②桥包自动升级卡死（2026-08-28 实证：cn.tuanjie.codely.bridge 自动更后 .com-unity-codely.json 卡 "package_updating"/unity_port=-1/心跳停更，编辑器空闲无日志，ShowWindow 无效）——唯一恢复=优雅关闭编辑器（CloseMainWindow）再重启，90s 内桥重新注册端口（新包 InitializeOnLoad 需完整编辑器重启周期才写回配置）。③编辑器 License 过期弹窗：点 Exit 重启编辑器即自动续期。**How to apply:** ②等待>2 分钟无恢复直接重启；重启前先确认场景 dirty=false；勿反复 ShowWindow 空耗。

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


### Reference
- [2026-08-14 10:16:21] MC mod gichess（旧项目，Java/NeoForge）：源码 D:\Game\mod\wg-template-1.21.4\src\main\java\com\wg\gichess\（308文件），jar D:\Picture\gichess\my\wg-0.2.d。~20+角色，7元素18反应，蒙德延奏/纳塔夜魂已实现。**Why:** GIC 战斗系统 Unity 移植的架构参考。**How to apply:** 需要查旧 Java 实现时按路径阅读源码。
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

