# 技术陷阱与 Bug 修复记录

> **定位**：症状 → 根因 → 规范的可检索事故库。分流：操作工作流 → skill、技术陷阱 → 本文、设计决策 → 各设计文档、通用规则 → docs/20。
>
> **编号规则**：`## 数字.` 节号 = 永久 ID——代码注释、docs/13/15/19/20、skill、项目记忆均按节号引用，**永不重排、永不复用**。正文按主题分组物理排列，分组标题字母序 ≠ 节号序；新增陷阱取当前最大节号 +1，插入所属分组末尾。
>
> **条目结构**：现象 / 根因 / 修复与规范；与 skill 分工=操作流程在 skill，本文记陷阱本体与规范。

## 分类索引

| 分组 | 节 |
|------|-----|
| A · UI 与 UGUI（粒子 / 九切片 / 程序化 UI / 设置页 / 面板化） | §2 §4 §14（14.2–14.4）§22 §26 §27 §37 §38 |
| B · 文本 · TMP · 本地化 · DI | §5 §6 §18 |
| C · 音频 | §10 §11 |
| D · 渲染 · Shader · 大地图图形 | §8 §13 |
| E · 网络与异步回调 | §15 §19 |
| F · 构建与平台差异（编辑器 vs 真机 / PC vs 移动） | §1 §3 §20 §21 §12 |
| G · 编辑器工具与资产管线 | §7 §9 §23 §24 §25 |
| H · 桌宠 · Win32 · 双进程 | §16a §16 §17 |

## 症状速查

| 症状 | 节 |
|------|-----|
| 编辑器正常，导出/真机才异常 | §1 §3.1 §6.3 §20 §21 |
| UI 元素不可见（文字正常） | §14.2 §2.2 |
| 图片/画面被拉伸变形、压扁 | §21 §26 §14.2 |
| 点了没反应 / 选项假死 | §22 §2.2 §27 |
| 音乐断头 / 永久静音 / 某区域偏小 | §10 §11 |
| NRE 刷屏 | §14.3 §14.4 §16a §6.1 |
| 编辑器卡死 / 导入极慢 | §9 §8.2③ |
| Shader 解析报错（引号/中文） | §13 |
| 场景 git diff 出现锚点类序列化噪声 | §23 |
| 设置里有值但运行时读不到 | §16 §16a |
| 多方共用回调互相顶掉 | §19 |

## A. UI 与 UGUI（粒子 / 九切片 / 程序化 UI / 设置页）

## 2. Unity UI 技术陷阱

### 2.1 自定义 Shader 特效应使用 Graphic 子类而非 Image

Image 组件要求 shader 必须有 `_MainTex` 属性（否则报 warning），且需要 Sprite 才能生成 mesh；在 Mask/Stencil 环境下还有额外的参数注入问题。

**正确做法**：创建 `class XxxGraphic : Graphic` + `OnPopulateMesh` 直接生成 quad，基类自动处理 stencil 注入，彻底绕过这些问题。

### 2.2 Toggle transition=Fade 会控制 CanvasGroup.alpha

Toggle 组件的 `transition=Fade` 会控制 `CanvasGroup.alpha`，即使手动设 `alpha=1` 也会被 Toggle 覆盖为 0（当 `toggle.isOn=false` 时）。

- `toggle.interactable=false` **不能**阻止此行为
- 必须 `toggle.enabled=false` 才能完全禁用 Toggle 对 CanvasGroup.alpha 的控制

**正确做法**：任何不需要 Toggle 交互的对象用 `toggle.enabled=false` 而非 `toggle.interactable=false`。

---

## 4. ParticleSystem 技术陷阱

### 4.1 Canvas 中渲染需要 Screen Space Camera 模式

ParticleSystem 在 UGUI Canvas 中渲染需要 **Screen Space Camera** 模式。Screen Space Overlay 不经过任何相机，ParticleSystem 无法渲染。

**配置步骤**：
1. 创建 UICamera（Orthographic, cullingMask=UI layer）
2. `Canvas.renderMode = ScreenSpaceCamera`
3. 用子 Canvas + `overrideSorting` 做分层排序

### 4.2 prewarm=true 不在手动 Play() 时生效

`prewarm=true` 只在 `playOnAwake=true` 自动播放时生效，手动 `Play()` 不触发 prewarm。

**正确做法**（代码控制启停 + 初始预填充）：
```csharp
ps.Clear(true);
ps.Simulate(ps.main.duration, true, true);
ps.Play(true);
```

### 4.3 小尺寸粒子纹理不要用 DXT5 压缩

小尺寸粒子纹理（如 64×64）用 DXT5 压缩时 alpha 通道精度太低，导致径向渐变退化为方块。

**正确设置**：`TextureImporterCompression.Uncompressed`（RGBA32）+ `alphaIsTransparency=true` + `mipmapEnabled=false` + `FilterMode.Bilinear` + `WrapMode.Clamp`。

### 4.4 main.startColor 只影响新粒子

`main.startColor` 只影响后续发射的新粒子，已存活粒子颜色不变。切换颜色时需用 `GetParticles`/`SetParticles` 遍历修改存活粒子的 `startColor`。

```csharp
ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[128]; // 预分配复用避免 GC
int count = ps.GetParticles(buffer);
for (int i = 0; i < count; i++)
    buffer[i].startColor = newColor;
ps.SetParticles(buffer, count);
```

### 4.5 Velocity over Lifetime 三轴 curve mode 必须一致

X/Y/Z 三轴 curve mode 必须一致（同为 Constant 或同为 TwoConstants），否则报错 "Particle Velocity curves must all be in the same mode"。Z 轴不用也要设成相同 mode（如 `TwoConstants(0,0)`）。

### 4.6 最终渲染 alpha 是相乘关系

最终渲染 alpha = `main.startColor.a × colorOverLifetime.gradient.a`。两者相乘而非取其一。

例如设 `startColor.a=0.15` 且 gradient 中段 `a=0.15` 时，实际 alpha 仅 `0.0225`。

**规则**：透明度只在一处控制——
- 要么 startColor 控制固定透明度 + gradient 中段=1.0 做淡入淡出
- 要么 gradient 控制全程 + startColor.a=1.0
- 不要两处都设小值

### 4.7 Tuanjie 引擎 ParticleSystem API 差异

1. `startColor.mode` 用 `ParticleSystemGradientMode.Color`（不是 `ParticleSystemCurveMode.Color`）
2. `NoiseModule` 没有 `dampen` 属性（编译报 CS1061）
3. `noise.strength` 需通过 `.constant` 访问

---

## 14. 程序化 UI 锚点双陷阱（派蒙聊天气泡错位，2026-08-29）

### 现象
聊天输入条正确挂在派蒙模型脚底下方，回复气泡却出现在"派蒙右上方非常远"处。

### 根因（两个叠加）
1. **程序化新建 RectTransform 默认锚=画布中心**：`new GameObject("X", typeof(RectTransform))` 后不设 anchorMin/anchorMax，默认值是 (0.5,0.5) 中心锚——而代码按"左下原点画布绝对坐标"写 `anchoredPosition`（锚点提供器返回的世界投影就是这套坐标），中心锚下实际位置=画布中心+anchoredPosition，系统性偏移 **(+半屏宽, +半屏高)**，正好把气泡推到右上远处。同文件里输入条显式设了左下锚所以正常——一个设了一个没设，肉眼直接对比出差异。
2. **找本体骨 不过滤 MMD 兜底模型**：`PetHostBase.找本体骨` 只排除影子壳（_DropShadow/MMD_DropShadow），不过滤禁用留存的 MMD 兜底模型（Paimon_arm，日文骨名系）。查 `"頭"` 命中的是**不动的 MMD 头骨**而非 GI 本体（Bip001 系）——气泡锚点追踪错误目标。

### 规范
- 程序化建 UI 元素（气泡/输入条/图标等）必须在创建后**显式设 anchorMin=anchorMax**（本项目约定=左下锚 (0,0)，anchoredPosition 即画布绝对坐标），再设 pivot——绝不信默认锚。同批创建的元素锚约定必须一致，否则跟随逻辑混用两套坐标系。
- 按骨名找骨（找本体骨/transform.Find）先确认骨名属于**当前激活模型**的命名系：GI 官方模型=Bip001 系（骨盆 Bip001 Pelvis/头 Bip001 Head），MMD=日文系（全ての親/頭）；PetLookAtController 的头骨名走 Inspector 序列化值按模型切换配置，新代码找骨照此办理，勿硬编码另一个模型的骨名。

### 14.2 九切片素材结构审计（同日二测："能看见文字，但看不见 UI 本身"）
贴图层面第三因：**"九切片"素材实为整图+透明边距**。pet_chat_bubble_9slice.png 不透明 bbox 上下各留 66px/左右 48px 透明，按 border=56 切 3×3 后**四角+上下边条全是透明像素**（仅中心 82% 不透明）；而输入条高 56、气泡最小高 72 都 < 上下 border 和 112 → Sliced 中心行被压成零/负高 → 整个 UI 只画透明像素=不可见（TMP 文字是独立子物体不受影响）。修复=程序化生成规范九切片（128×128 纯白圆角矩形 border=24，白底供 Image.color 染色，角 78%/边条/中心 100% 不透明——78%=圆角裁切 π/4 理论值）；发送按钮矩形 < border×2 改 Simple 防退化。

### 规范（九切片素材审计）
- **导入九切片素材前按 border 切 3×3 分区测不透明率**：四角应≈100%-π/4（圆角裁切）、边条/中心≈100%；四角或边条≈0%=不是九切片结构（整图带透明边距），Sliced 会只画中心区——小矩形（高 < 上下 border 和）时整体不可见。TextureImporter 的 border 数值不会校验素材结构。
- 目标矩形任一边 < 对应 border×2 时 Sliced 退化（中心行零/负高）——小元素（按钮）用 Simple，或换更小 border 的素材。

### 14.3 TMP_InputField 拖拽选字 NRE（同日三测：控制台刷屏）
**TMP_InputField 拖拽选字在无 MainCamera 场景必炸（TMP 3.0.9）**：输入框内按住拖动 → 基类 OnDrag 启动 `MouseDragOutsideRect` 协程 → `ScreenPointToLocalPointInRectangle(textViewport, pos, eventData.pressEventCamera, ...)`——ScreenSpaceOverlay 画布下 pressEventCamera 恒 null → Unity 内部回退 `Camera.main.ScreenPointToRay` → GIC 纯 UI 场景（SettingsScreen 等）无 MainCamera tag 相机 = 每帧 NullReferenceException（源码 L1759 实证）。
**修复**：子类 `PetChatInputField` 覆写 OnDrag 为空（掐掉协程路径；代价=拖出矩形选字失效，单击定位/双击选词/Shift+方向键选区不受影响）。**勿给聊天画布配 MainCamera tag 相机**（DontDestroyOnLoad 常驻相机抢 Camera.main 是同族坑）；也勿把聊天画布改 ScreenSpaceCamera（会被游戏 Overlay UI 盖住，layering 语义反了）。
另：程序化 TMP 文本勿用"➤"等装饰符号——zh-cn SDF 无此字形（警告+显示方块），按钮文字用中文（"发送"）。

### 14.4 TMP_InputField 程序化构建赋值顺序（同批实证，2026-08-29）

**TMP_InputField 程序化构建赋值顺序**：TMP 3.0.9 的 fontAsset/pointSize setter（SetGlobalFontAsset/SetGlobalPointSize，源码 L4593/L4605）**无条件解引用 textComponent**（placeholder 有判空、textComponent 没有）——程序化建输入框赋值顺序必须 **textComponent → placeholder → fontAsset → pointSize**，反序必 NRE。

---

## 22. 设置页克隆下拉条目两坑（派蒙形态"点了没反应"，2026-08-27/28）

### 现象
设置页新增"派蒙形态"下拉：克隆现有条目后显示正常，但选择无任何反应；且首次打开显示的就是错项。语言/帧率/分辨率条目一切正常。

### 根因（两个独立坑）
1. **克隆到 inactive 面板的组件 Awake 从未执行**：SettingsScreen 各分栏面板初始未激活，Unity 规则=inactive 物体不跑 Awake——凡"Awake 缓存引用"模式（LocalizedDropdown.Awake 里 `dropdown = GetComponent`）全部静默失效：`if(x==null) return` 式守卫不报错，选项不重建/监听不挂/值不设=条目彻底假死。语言/帧率正常只因恰在初始激活的 Display 面板。
2. **DropdownSettingItem.Setup 的 defaultValue 框架语义=Initialize() 的显示值**（非"新玩家默认"）——必须传当前存档值；传错则下拉打开即显示错项，用户点同一项时 TMP_Dropdown 值不变、不触发 onValueChanged=点了没反应。

### 修复与规范
- 克隆到分栏面板的新条目必须**真实点击验证**（显示对了≠回调通了）。
- "Awake 缓存引用"改惰性属性：`Dd => dropdown != null ? dropdown : (dropdown = GetComponent<T>())`（已修 LocalizedDropdown，存量 bug 连原 Other 栏 petClose 一起治愈）。
- Setup 的 defaultValue 一律传当前存档值。
- 排查下拉问题时拿正常条目（如帧率设置）做对照组逐项 diff 很有效；Language/FrameRate/Resolution 全用 `TextEntry(null, 静态文本)` 是参考实现，PetForm 用 LocalizedString 是少数派。

---

## 26. Sliced 九切片 border 按原生像素渲染（AI 弹窗素材实证，2026-09-02）

### 现象
AI 生成的按钮素材（端头弧线深，border 上下合计 94px）接到 250×65 按钮上 Sliced 渲染破碎（边区重叠、中心消失）。

### 根因与解法
- **Sliced 的 border 按原生像素渲染，与显示矩形大小无关**：border 上下合计 > 显示高度时边区重叠、中心行压零。解法=Image.pixelsPerUnitMultiplier=2（2x 资产按 1x 比例渲染，border 渲染尺寸=border_px/multiplier，线条仍清晰）。
- **spriteBorder 必须 ≥ 角饰/圆角外沿**，否则角饰落入拉伸区变形。测法=各边每行/列首个不透明像素缩进的最大值+4px 余量（最大值天然覆盖圆角与角饰；中点行缩进=纯边线厚度）。
- 与 §14.2 联动：目标矩形任一边 < 对应 border×2 即 Sliced 退化。

### 规范
- 全流程（AI 生成→裁剪降采样→border 测量→导入器→prefab 接入→读回断言）见 gic-ui-9slice skill；素材中心必须纯色（中心区会被拉伸）

---

## 27. InputPopupDialog 必须实例化在非 UI 父级下（卡组改名/导入"点了没反应"，2026-09-06）

### 现象
卡组管理面板（DeckSwitchPanel）里点名称改名、点「导入密语」均无任何反应——无弹窗、无报错、无 toast；同一 InputPopupDialog 预制体在设置界面（玩家名/桌宠 key）一直正常。

### 根因
InputPopupDialog.prefab 的根节点是"**自带 Canvas（Overlay）+ scale=0 的占位节点**"：
- 实例化到 SettingsScreen 根 GO（纯 Transform、无 Canvas 父级）时，该 Canvas 成为**独立顶层画布**，其 RectTransform 被 Canvas 系统接管（scale 覆写为 1、铺满屏幕）→ 正常显示。
- 实例化到任何处于主 Canvas 层级内的父物体（本次的 DeckSwitchPanel）时它是**嵌套 Canvas**，RT 不被接管 → 保留序列化的 scale=0 → 整个弹窗缩成一个点，**不可见且无任何报错**——表现为"点了没反应"。

### 修复与规范
- 根含 Canvas 的弹窗预制体一律实例化到**无 Canvas 父级**（如各 Screen 的根 GO）：`Instantiate(inputPopupPrefab, screen.transform)`，禁止挂到面板/主 Canvas 层级内。DeckSwitchPanel.ShowInputDialog 已按此实现并在注释中说明。
- 该类"嵌套 Canvas + 特殊根序列化值"的预制体无报错失败极难排查：症状是 UI 完全静默时，先查实例化父级是否在 Canvas 层级内。

---

## 28. 本地化表双层错位历史事故：值挪了、Shared 层没挪（物品名错挂一月未察觉，2026-09-06 修复）

### 现象
2026-09-06 给 ItemName/ItemDescription 表加 AcquaintFate 键时发现目标 Id 1004 被 Magatama 占用，顺藤摸瓜查出 **2026-07-31 提交 06b8b5e 引入的历史事故**：该提交把**语言表（locale table）的值**按枚举值挪了位（勾玉→3001、蒲公英酒→5001、迪奥娜特调→5002），但 **Shared Data 层的 Id 没有同步 Remap**（仍是 1004/3001/3002 旧布局），旧值副本也没清——从此游戏内按 Shared Id 查表全部错挂：
- 蒲公英酒显示成"勾玉"、火晶体显示成"蒲公英酒"、水体显示成"迪奥娜特调"（zh/en/ja/zh-TW 四表同病）
- 火晶体/水晶体的值**彻底丢失**（它们的旧槽被上位物品的值覆盖）
- ItemDescription 的 **ru 表整表被日语文本覆盖**（另一条事故线）；ja 表也有 13 条英文残片
- **一个月无人察觉**——因为按 Key 查表的代码能跑、游戏不报错，错的只是显示内容

### 根因
Unity Localization 是**双层结构**：Shared Data（Key↔Id 映射）+ 各语言表（Id→值）。只挪语言表值不 Remap Shared Id = 两层各说各话。**运行时按 Key→SharedData→Id→语言表 取值**，显示跟着 Shared Id 走，语言表里"看起来对齐了枚举"的值全是错挂。

### 修复（2026-09-06，全量归位）
1. **Id 归位**：`WishFateItemsSetupTool.AlignLegacyItemIds`（依赖链从深到浅 RemapId：MysteryKey 6001→7001、AncientScroll 6002→7002、七晶体 500x→600x、蒲公英酒/迪奥娜特调 300x→500x、勾玉 1004→3001、AcquaintFate →1004）+ 每步三步法搬家（RemapId → 语言表旧 id 取值/RemoveEntry → AddEntry 新 id）
2. **值校对**：`WishFateCanonicalValues`（从 git 提交 **1129959，2026-07-29=事故前最后已知正确状态** 用脚本提取的标准值，`extract_canonical.ps1`）逐键比对，错值写回——修复 4 语言名 3 槽/表、描述表 zh-Hans 3、zh-TW 5、en 13、ja 13、ru 14（ru 全表日语→俄语复原）
3. 修复后 Id 与 ItemName 枚举值**全量对齐**，CSV 重导出

### 规范
- **改语言表 Id 一律动 Shared 层**（RemapId+三步法，见 gic-localization skill），任何"直接改语言表 m_Localized 挂点/值"的操作都跳过了 Key↔Id 中间层=埋雷
- **考古修复首选 git**：`git show <旧提交>:<表>.asset` 提取事故前正确值——比凭记忆重写可靠（本次火/水晶体值已从运行时任何表不可得，只有 git 里有）
- **大数 Id/半执行状态是历史事故的显影剂**：新加键时发现目标枚举 Id 被无关键占用（如 1004 被勾玉占）≠"该键本来就在这"——很可能是**错位链的头**，应全链排查（本次从 1 个占用提示挖出 11 键错位+3 值丢失+2 表被外语覆盖）
- 长期无人发现的显示类错值不报错：**改本地化后肉眼过一遍 CSV**（Key,Id,各语言值 横排对比）是最便宜的防线

---

## B. 文本 · TMP · 本地化 · DI

## 5. Unity Localization 陷阱

### 5.1 CSV 导入新增条目时 Id 自动分配问题

`AddKey(key)` 不带 Id 参数会自动分配一个大数字 Id（如 `286xxxxxxxx`），与枚举值不对齐。

**正确做法**：必须用 `AddKey(key, id)` 显式传入枚举值对应的 Id。

CSV 导入后检查 `SharedData.Entries` 中的 Id 是否与枚举值一致；不一致时先 `RemoveKey` 再 `AddKey(key, correctId)`，然后重新导入 CSV 设置各语言值。

---

## 6. DI / TMP 陷阱（P7 期间修复，2026-08-15）

### 6.1 .NET 反射 GetFields 不返回基类 private 字段（DI 注入失效）

`Type.GetFields(NonPublic | Instance)` 只返回**本类型声明**的 private 字段，**基类的 private 字段不在结果中**。`ApplicationContext.Inject` 原实现按 `GetType().GetFields` 扫描——P7 把 `[Autowired] InputManager` 放进 `ScreenBase` 基类（private）后，所有 Screen 的注入静默失败（`_inputManager` 为 null），症状是全部界面 ESC/右键失灵但按钮点击正常。

**修复**：`GetAutowiredFields(type)` 沿 `t.BaseType` 链逐层 `DeclaredOnly` 扫描；`Inject` 与 `Validate` 都要走它。

**规则**：基类声明的 [Autowired] 字段依赖容器逐层扫描修复；同类字段无此问题。

### 6.2 TextCombiner.ApplyFont 覆盖场景材质变体（描边丢失）

`ApplyFont` 原实现无条件执行 `textComponent.fontMaterial = currentFont.material`，把场景中为单个文本配置的**材质变体**（如 `zh-cn SDF.mat` 黑描边 0.15 / `zh-cn SDF 1.mat` 白描边 0.08，用于祈愿面板名称与称号）覆盖回字体基础 Atlas Material——描边表现为"消失"。P1 把 CharacterPanelController 改用 EnsureTextCombiner 后该路径被激活。

**修复**：仅在 `textComponent.font != currentFont`（字体真正变更，如语言切换）时才同时切 font + fontMaterial；字体已一致时不动材质，保留场景变体。

**规则**：需要描边/特殊配色的 TMP 文本 = 同字体 + 变体材质（放 `TextMesh Pro/Resources/Fonts & Materials/`，该目录 git 忽略，改材质不入库，重装环境需手动备份）。另注意 rg/搜索工具默认跳过 git 忽略目录，排查 TextMesh Pro/ 下资产时需加 `--no-ignore`；Codely 搜索工具（search_file_content/glob/list_directory）ignore 层=.codelyignore**加**.gitignore 双层（.codelyignore 另吞 Assets 的 png/prefab/asset/mat/wav 与 Mirror/kcp2k/Plugins 整目录）——被任一层命中即**静默零命中/无名**，下"查无引用/文件不存在/skill 不存在"类结论前一律先想 ignore 层，改走原生 `rg --no-ignore` 或 Get-ChildItem；analyze_multimedia 对被忽略路径同样拒读（Assets 图片先复制到 .codely-cli/tmp 再传）。

### 6.3 引用相等判断在打包后误判字体变更（真机描边二次丢失，2026-08-16）

6.2 的修复（`font != currentFont` 引用比较）在编辑器正常、**导出 APK 后描边仍丢失**。根因：`zh-cn SDF.asset` 被三路引用——场景/Prefab 直引、`Resources/` 目录、Addressables（`Localization-Assets-Shared` 组，UIAssets 表 MainFont 按其 GUID 加载）。打包后玩家包内容与 Addressables bundle 各持一份实例；编辑器 Play 时 Addressables 走 AssetDatabase，两处为**同一实例**。于是 `textComponent.font != currentFont`：编辑器判"没变"（保留变体材质），真机判"变了"（font+fontMaterial 被覆盖回 `_OutlineWidth=0` 的 Atlas Material）——典型的"编辑器正常真机异常"源于 Addressables 实例重复。

**修复（两阶段）**：
- **止血**：引用比较改按逻辑身份（`font.name`），实例不同但同一字体资产时不切换、不动材质。
- **根治（同日）**：删除 TextCombiner 整条运行时字体加载链（fontTable/fontEntryKey/LoadFont/ApplyFont），并删除 UIAssets 资产表 5 个语言表的 MainFont 条目 + SharedData key（该表本来只有中文配了字体、且唯一消费者就是 TextCombiner——纯死代码路径）。字体只剩两条固有打包路径：场景/Prefab 直引（sharedassets）+ TMP Settings 默认字体（resources.assets，TMP 机制要求 defaultFontAsset 必须在 `TextMesh Pro/Resources/Fonts & Materials/`）。运行时再无代码触碰 font/fontMaterial，描边永不丢失，并省掉 Addressables bundle 里的一份字体。

**操作记录**：删表条目用 LocalizationEditorSettings API（`UnityEditor.Localization` 命名空间）；Addressables read-only 组 `Localization-Assets-Shared.asset` 的残留条目不会因表变更自动同步，需手删 m_Entries 中该条目块 + `AssetDatabase.ImportAsset(ForceUpdate)` 重载，再用 FindGroup 验证 entries 归零。

**规则**：凡"按资产引用相等做幂等判断"的代码，在 Addressables 项目里打包后都可能因实例重复而失效——要么按 GUID/名称等逻辑身份比较，要么保证资产只进一条加载路径。多语言字体切换若将来要做，重新设计时勿恢复"运行时整体赋 font+fontMaterial"的写法（应只换 fontAsset 并保留目标材质变体的映射）。

---

## 18. 本地化脚本 RemapId 静默失败后的错位写值（"输入条显示输入供应商"，2026-08-29）

### 现象
聊天输入条占位文本显示"对话模型供应商"（用户读作"输入供应商"）；错误/忙碌提示语也变成了供应商名。

### 根因
加键脚本的流程：`AddKey(key)`（拿大数 id）→ `RemapId(大数, 目标分段 id)` → 按目标 id 写值。**RemapId 在目标 id 已被占用时返回 False（静默失败）**，脚本未检查返回值继续按目标 id 写值——写进了**占用该 id 的既有键**（PetChatPlaceholder/PetChatNoKey/PetChatBusy/PetChatNotWired 四键被供应商名覆盖）。目标 id"看似空闲"是凭记忆拍的（9039~9043 实际已被聊天域键占用）。

### 修复与规范
- **AddKey 前必须真查表**：`Entries.Any(e => e.Id == 目标)` 确认空闲（"查该域现有最大序号"不能凭记忆）；**RemapId 返回值必须检查**，False=目标占用或 currentId 不存在，立刻停手换 id。
- 事故恢复：git diff 语言表 .asset 取原值（含 YAML 折行=\n 的多行值）→ 脚本直改恢复 → 重导出 CSV。
- 已把完整绕行流程与三坑（AddKey(key,id) NRE / 同 key 重复条目 / 半执行状态）记入 gic-localization skill。

---

## C. 音频

## 10. 弹层音乐 Push/Pop 在间隔冷却期打断播放链（2026-08-21）

### 现象
大厅 BGM（`PositionManager` 的 `PlayMusicWithInterval`，loop=false + intervalAfter=10s + onComplete 链式下一首）播完进入 10 秒冷却时进入祈愿等弹层场景，再退回大厅——大厅音乐**永久静音**，下一首再也不来。冷却期外进出弹层则一切正常。

### 根因
旧 `PushMusicState` 快照只有 `wasPlaying`（= `musicSource.isPlaying`）。冷却期 isPlaying=false，被误判为"暂停"存入快照；同时 `StopCurrentMusic()` 杀掉了挂起在间隔等待中的 `MusicCompletionCoroutine`——`onComplete`（链式下一首）的唯一触发者随协程一起死亡。Pop 恢复走 wasPlaying=false 分支：只设 clip 不 Play 也不重启协程 → 播放链就地断头。本质是**音乐生命周期状态只存在于匿名协程的挂起点里，不可快照**。

### 修复（MusicPhase 生命周期快照）
- `AudioManager` 新增 `intervalEndTime` 时间戳（Time.time，与 `Wait.Seconds` 同为缩放时间）：三个生命周期协程（Delayed/MusicLoop/MusicCompletion）进入间隔等待时写入、等待结束或 `StopCurrentMusic` 时清除。**不变量：intervalEndTime > now ⟺ 有存活协程正挂在间隔等待中**。
- `MusicState` 快照以 `MusicPhase` 枚举（None/DelayBefore/Playing/Paused/IntervalAfter）替代 wasPlaying，IntervalAfter/DelayBefore 额外存 `pendingInterval`（剩余冷却/延迟秒数，由时间戳算出）。
- Pop 恢复按阶段分发：IntervalAfter → 重启对应生命周期协程并传入剩余秒数（跳过"等播完"，直接等剩余冷却后触发 onComplete/下一轮循环）；DelayBefore → 等剩余延迟后起播。冷却中反复进出弹层可递归正确快照。
- 恢复时有日志 `恢复音乐冷却：xxx 剩余 Ns`，真机排查看这条即可确认链路复活。

### 规范
- 带间隔冷却的音乐（位置 BGM 链）一律走 `PlayMusicWithInterval`/`MusicTrack`，不要在业务层自管冷却计时——AudioManager 的 Push/Pop 快照已覆盖冷却期，自管反而脱离快照体系
- 动 `AudioManager` 生命周期协程时保持不变量：任何新增的间隔等待必须同步维护 `intervalEndTime`，任何终止路径必须走 `StopCurrentMusic`（它会清时间戳）

---

## 11. 游戏内提取音频与平台 OST 的响度鸿沟（至冬堡 BGM 偏小，2026-08-21）

### 现象
至冬堡位置 BGM 明显比那夏镇/雷波岛小声。Unity 侧无任何差异（同 AudioImporter 设置、同播放链路、AudioMixer 无分轨）。

### 根因
**音源响度标准不同**：其它区域音乐来自官方平台直接下载的 OST（发行母带标准，-11~-14 LUFS）；至冬堡音乐从原神游戏内提取（游戏内混音刻意压低留余量——音乐要与语音/音效/环境音共存，差 2~6 LUFS 是行业常态）。属于源文件本身的问题，Unity 侧无解。

辅助结论：
- **峰值归一 ≠ 响度归一**。第一轮把峰值拉到 -0.5 dB 后听感仍偏小——峰值只对齐"最响一瞬间"，感知响度看 LUFS。位置 BGM 批量响度对齐必须用 loudnorm（两遍法），不能只拉峰值。
- Unity AudioImporter 的 `normalize: 1` 对 Streaming loadType 的音频不生效（Unity 已知行为），别指望导入归一兜底。
- loudnorm 传 `linear=true`（纯增益）在"增益后超 TP 上限"时会**静默回退动态模式**（有轻微动态压缩）。位置 BGM 场景可接受（处理后 LRA 5~10 LU，与其它区域同档）。

### 修复记录（至冬堡 8 个 ogg）
从 git 原始版单遍 loudnorm 重做：白天目标 -11.5 LUFS（对齐那夏镇白天 -11.0~-12.1），夜晚 -13.5 LUFS（对齐那夏镇夜晚），TP=-1.0 dBTP。白天/夜晚分开定目标——昼夜音乐本就有响度差，统一拉到同一响度反而破坏节奏。

**教训**：响度处理前先确认音源血统。同项目混用"游戏内提取 + 平台下载"两种来源时，提取版必须做 loudnorm 对齐；且重编码只做一遍（多遍处理=多代有损叠加，需从原始版重来）。

### 规范
- 批量响度对齐：ffmpeg 两遍 loudnorm（先测 input_i/tp/lra/thresh 再带 measured_* 编码），目标值=参照组的 LUFS 均值
- 响度测量用 `-filter_complex ebur128`（取**最后一条**汇总值，非首条瞬时值）；PS5.1 下 ffmpeg stderr 会触发 NativeCommandError，脚本里别用 $ErrorActionPreference='Stop'
- 官方 OST 后续发布了就下载替换（同源同质），替换后重新校验 LUFS

---

## 29. 拆除期 AudioManager 先亡：Screen OnDestroy 链路 Pop 音乐抛 MissingReferenceException（2026-09-06 WishScreen 实证）

### 现象
停止 Play Mode（或打包版退出应用）时恰停在祈愿界面，控制台报：
```
MissingReferenceException: The object of type 'AudioManager' has been destroyed ...
  at UnityEngine.MonoBehaviour.StopCoroutine
  at AudioManager.StopCurrentMusic() (AudioManager.Music.cs:118)
  at AudioManager.PopMusicState() (AudioManager.MusicState.cs:144)
  at ScreenBase.PopMusicSafe() (ScreenBase.cs:65)
  at ScreenBase.OnDestroy() → WishScreen.OnDestroy()
```
**游玩全程声音正常**——纯退场拆除噪音，无功能损失。

### 根因
Unity 撤销对象的 OnDestroy 顺序**不保证**。`AudioManager` 是 DontDestroyOnLoad 场景单例，退场时它的原生对象可能先被销毁，而 WishScreen 的 OnDestroy 后跑；`ScreenBase.PopMusicSafe` 里的 `AudioManager.Instance` 是纯 C# 静态属性（Awake 注册、销毁后不清）——持有的是"Unity 假 null"包装（C# 层非 null），`Instance.Xxx()` 照常进方法体，直到 `StopCoroutine` 这类原生调用才炸。`?.` 与 `is null` 都防不住（只查 C# null，不走 Object 重载 ==）。

### 修复（2026-09-06）
`ScreenBase.PopMusicSafe` 在 `_musicPushed` 复位后加一行守卫：
```csharp
if (AudioManager.Instance == null) return;  // Unity 假 null 用 == 判（Object 重载）；管理器已亡即无需恢复
```
守卫放 Safe 层（ScreenBase）而非 AudioManager 内部：Safe 层本就是弹层 Screen 的生命周期防护收口，一处守卫覆盖全部继承 Screen（按 gic-audio 铁律禁止 Screen 直接调 Push/Pop，无第二条拆除期调用路径）。

### 规范
- **拆除期（OnDestroy）访问全局单例一律先 `Xxx.Instance == null` 守卫**（Unity 假 null 走 Object 重载 == 才判得死）；`?.` / `is null` / C# `== null` 全部无效
- 单例被销毁后不再恢复场景（DontDestroyOnLoad 只死于应用退场），守卫处直接放弃恢复即可，无泄漏风险
- 判"是不是 bug"先看时机：游玩中正常、仅停止/退出时炸 = 拆除顺序类噪音，修守卫而非修业务

---

## D. 渲染 · Shader · 大地图图形

## 8. 3D 相机俯角陷阱（大地图 3D 化，2026-08-16）

### 8.1 Quaternion.Euler 的俯仰符号：正 X = 向下俯视

想写"相机俯角 55° 斜俯视地面"，直觉写 `Quaternion.Euler(-pitch, 0, 0)` 是**错的**——那是 55° **仰视天空**。Unity 旋转约定：绕 X 正角度使 forward 转向 -Y（`Euler(90,0,0)` = 垂直向下看）。因此俯视相机应为 **`Euler(+pitch, 0, 0)`**，相机位置在注视点北侧：`pos = (focusX, height, focusZ - height/tan(pitch))`。

症状：MapCamera 只渲染出 SolidColor 深色背景（近似黑屏），Overlay Canvas 的 UI 正常显示；诊断手法 = `camera.ViewportPointToRay(0.5,0.5)` 检查 `direction.y` 是否为负（朝下）。

同源错误：面向相机的 billboard 面片（SpriteRenderer 直立面）倾角也是 `Euler(+pitch)`（法线才正对相机，图标屏幕直立），不是 `Euler(180-pitch)`。

**后续定稿**（用户不要 3D 透视效果）：最终相机为**正交垂直俯视**（`Euler(90,0,0)`，ortho size 5–45），锚点图标平铺在地图平面上（与 MapPlane 同旋转 `Euler(90,0,0)`），俯视观感等同 2D 平面地图。正交缩放锚点公式：`newFocus = g - (g - oldFocus) × (newSize / oldSize)`（屏幕偏移与尺寸成线性关系）。

### 8.2 Tuanjie 块压缩两大拦路虎：mipmap 开启即失效 + 巨图编辑器内重导入 OOM（2026-08-16，两轮定位）

大地图贴图 `all_map.jpg` 曾长期以**未压缩 RGBA32** 运行（16384×10533 时 658MB，10752×6912 时 756MB），进 MapScreen 场景同步解码上传 → 明显卡顿。

**第一轮修复**（外部缩图至 8064×5184）后加载恢复，但**根因判断不全**——当时归因于"高度非 4 倍数致 BC7 失效"。第二轮为提升清晰度升到 10752×6912（宽高均 4 倍数）后压缩再次失效，系统排查（多组对照实验）得出真正结论：

**① mipmap 开启 → 大图块压缩静默失效**。Tuanjie 1.9.3 中大图（如 4096+ Sprite）`mipmapEnabled=true` 时直接回退未压缩格式（RGB24/RGBA32），**无任何警告**；关闭后同尺寸立即 DXT1（8120×5220：→20MB；10752×6912：→71MB）。all_map 实验复现（同文件仅切 mip：RGBA32↔DXT1）。⚠️ 范围修正（2026-08-16 全项目排查 212 张后）：此阻断**并非对所有贴图生效**——17 张开 mip 的贴图中 16 张小图压缩正常，仅大图中招；此前"项目里 mip 贴图全部未压缩"的推论过度泛化，nodkrai/mondstadt 等区域图实为 mip 关闭状态下未压缩（见②b）。
- 取舍依据：正交相机缩放范围 8–15 内画面恒为放大显示（最远 1.27×），mipmap 本就无用，关掉零损失。
- **② 显式格式覆盖不可靠**：`format=BC7(25)` / `AutomaticCompressed(0)` 均被无视仍回退；必须 `textureFormat=-1（Automatic）+ textureCompression=Compressed` 才生效。平台 override 意义存疑，统一走 Default 平台最稳。
- **②b NPOT 尺寸（非 4 倍数）→ 块压缩拒绝（排查实锤的主要浪费源）**：任一边非 4 倍数即回退未压缩，mip 开关无关。6 张旧区域图（4096×3829/8192×5799 等，合计 505MB RGBA32）全因此未压缩——属旧 UGUI 方案零引用遗留，直接删除而非修复；在用待修（外部补齐 4 倍数即可压缩，约省 55MB）：Wish 背景 back.png 3199×1799(16.5MB)、logo 1716×1073(12.3MB)、元素图标 Deep/Stroke 801×801×14(34MB)。
- **③ 巨图编辑器内重导入会 OOM 崩编辑器**：Worker 解码 21504×13824 源图需一次性分配 1.19GB 连续内存 → Fatal Error（2026-08-16 实崩一次）。**缩源图必须在 Unity 外部做**（PowerShell System.Drawing，q92-95）；10752 档（解码 ~297MB）编辑器内安全。编辑器死后进程僵死，需循环 taskkill。源图备份 `Export/all_map_source_21504.jpg`。
- **④ PPU 手动补偿**：外部换文件后无钳制就无自动补偿，必须手改 meta（当前 10752 档 PPU=50，世界尺寸 215.04×138.24 恒定，锚点/相机参数不联动）。

**最终定稿**：10752×6912（0.5×源图，50px/世界单位）+ DXT1 无 mip + Sprite PPU50，71MB，清晰度比 8064 档（37.5px/单位）提升 33%。若将来要更极致，方向是原神式**切片+流式加载**（AssetCache 预载），个人 Demo 无必要。

### 8.2.1 大地图瓦片化落地（2026-08-18，16384 新图版）

2026-08-18 换 16384×13896 新图后回到未压缩 RGBA32（868MB）+ 进图卡顿，按 §8.2 方向落地瓦片化（曾于 2026-08-17 实现后被撤回，本次按用户拍板重做）：

- **结构**：MapPlane 只挂 1/8 预览图（2048×1736，注意**预览宽高也须 4 倍数**——1737 会被拒压缩回退 RGB24，实测踩过）；全图按 2048 网格切 8×7=56 片，每片含 4px 重叠边（防双线性接缝，边缘瓦片钳制），全部 DXT1（2052/2056 边长均可压缩）
- **单趟切图**：ffmpeg `filter_complex` 一次解码源图、56 个 crop 输出（每瓦片单独跑 ffmpeg 会重复解码 16K JPEG 56 次）；工具 `Tools/地图/生成地图瓦片`（MapTileBakeTool，幂等）
- **全图出构建**：从 MapAssets 组移除 all_map 条目 + 场景零引用（GetDependencies 验证）→ 构建不再含 868MB；取点器/标定工具按 AssetDatabase 路径用全图（编辑器专属）
- **场景保存守卫**：编辑器临时把全图换上 MapPlane 供目测（MapEditorFullRes），sceneSaving 回调先还原预览图再落盘——否则全图引用会写进场景 YAML 悄悄回构建
- **工具超时重试坑**：unity_menu 执行长工具（>330s）超时后桥会重试，本次工具被执行了 4 次——经 unity_menu 跑的长编辑器工具必须幂等（本次靠"先清后建 + CreateOrMoveEntry + 断言闭环"扛住）

---

## 13. Tuanjie ShaderLab 属性解析器不支持属性值引号/中文（2026-08-22 实测）

### 现象
新写的角色 shader 导入后 `ShaderUtil.ShaderHasError=True`，`shader.name` 为空串、`isSupported=False`，控制台报 `Parse error: syntax error, unexpected $undefined, expecting TVAL_ID or TVAL_VARREF`，报错行指向 `[Header(...)]` 属性行。且该错误**不一定**实时出现在控制台（shader 解析错误可能只在 ShaderUtil API / 强制导入时冒出，`start_compilation_pipeline` 后的 console 读取可能读不到）——判定 shader 是否编译失败要用 `ShaderUtil.ShaderHasError(shader)`，不能只看 console。

### 根因（最小 shader 二分实测）
Tuanjie 1.9.3（类 2022.3）的 ShaderLab 属性块解析器对 MaterialPropertyDrawer 属性值的支持残缺：
- `[Header("Quoted Text")]` 引号字符串 → **Parse error**（标准 Unity 支持）
- `[Header(中文标题)]` 无引号中文 → **Parse error**
- `[Header(ASCII)]` 无引号 ASCII ✓
- 属性**显示名**（`_Prop ("中文显示名", Float)`）里的中文/引号 → ✓ 正常（中文注释也正常）

### 规范
- Tuanjie 下写 shader：属性值一律无引号 ASCII（`[Header(Albedo)]`），中文名放显示名里（`_Prop ("中文", Float)`）
- 外部写 .shader 文件后验证：`AssetDatabase.ImportAsset(ForceUpdate)` + `ShaderUtil.ShaderHasError` + `GetShaderMessages`（能拿到精确行号），比 console 可靠

---

## E. 网络与异步回调

## 15. Unity Mono 的 HttpClient SSE 假流式（派蒙聊天整局卡住后一次性出全文，2026-08-29）

### 现象
发送对话后无流式打字机效果，整个游戏卡住一会后一次性出现完整回复。

### 根因
`HttpClient.SendAsync(..., ResponseHeadersRead)` + `HttpContent.ReadAsStreamAsync` + `StreamReader.ReadLineAsync` 在 **Unity Mono/.NET Standard 2.1** 上是假流式：ReadAsStreamAsync 沿 .NET Framework 血统**整体缓冲响应**（MS 文档明示：Task 在表示内容的流全部读完后才完成），SSE 增量无法逐块送达——连接建立后所有 `data:` 行一次性到达。叠加 `StreamReader.EndOfStream` 的同步读可能落在主线程上，体感=卡顿后整段弹出。

### 修复与规范
- **Unity 内消费 SSE/流式响应一律用 `UnityWebRequest` + `DownloadHandlerScript` 子类**（官方文档：下载在 worker 线程，`ReceiveData(byte[] data, int dataLength)` 回调在主线程被逐块调用）——重写字节→字符→行三层：跨块 UTF-8 多字节序列用**持久 Decoder** 续解（直接 `Encoding.UTF8.GetString(data)` 在中文被块边界切开时产生替换字符）；行按 `\n` 切并容错剥 `\r`。
- **超时自制看门狗**：`UnityWebRequest.timeout` 语义含糊（"未收到响应则中止"——长流式会被腰斩），恒置 0；改为协程每帧查 `lastDataAt`（首字节超时=Inspector 超时秒，流开始后 90s 无数据才算卡死→Abort）。
- HTTP 错误响应体（非 SSE、无 `data:` 前缀）累积在 rawBody 供收尾提取 `error.message`——DownloadHandlerScript 不区分响应类型，行级解析器按前缀分流即可。
- 中途半截 JSON（断流截断）在行解析层 try-catch 静默跳过——容错优先于严格报错。
- **解码缓冲容量铁律（2026-08-30 "打开界面后报错 chars 溢出"实证）**：`DownloadHandlerScript` 预分配**字节**缓冲 16KB ≠ 可以只配 4K **字符**缓冲——单次 `ReceiveData` 回调最多 16KB 字节，而 UTF-8 解码输出字符数上限=输入字节数（纯 ASCII 1:1，多字节只缩不涨），流快时多个 TCP 段合并成一次大回调 → `Decoder.GetChars` 直接抛 `The output char buffer is too small` → Unity 回 0 给 curl → `Curl error 23`，整请求崩。修法：char 缓冲 ≥ 字节缓冲 + 挂起余量（16400），外加 `GetCharCount` 先数后解的防御性兜底（数含 Decoder 挂起字节，永不抛；不够临时扩，宁分配不崩溃）。**任何"字节缓冲→字符缓冲"转换都要按 1 byte : 1 char 上限配容量**。

---

## 19. 聊天流式回调三方互踩与 AbortActive 假静默（"反应文本从此全灭+对话气泡闪假错"，2026-08-31）

### 现象
①AI 抽卡反应只有动作没有 LLM 话语（fallback 模板兜底掩盖了症状）；②用户发消息瞬间偶尔气泡闪一条"哎呀…Request aborted"。两症状同根。

### 根因
PetChatClient 的流式回调是**公共 Action 字段**（onContentDelta/onError/onComplete），UI 层（对话 Send）与 PetReactionConsumer（反应生成）轮流接线：
- **互踩**：两方都用 `=` 整体赋值——后接的一方顶掉先接一方的处理器；
- **假静默**：`AbortActive()` 注释写"静默：不触发 onError"，实现却是设共享布尔 `_watchdogAbort=true`——新请求的 `RequestStream` 会立刻把它重置 false，被 Abort 的旧请求下一帧收尾读到 false 走 ConnectionError 分支照样触发 onError（此时回调已被新请求方重接=对话气泡闪假错）；而真正依赖"被中止请求不再回调"来复位反应侧 `_generating` 标志的路径彻底失效 → **_generating 永久卡 true，此后反应文本生成全灭**。

### 修复与规范
- **AbortActive 真静默**：按请求引用标记（`_silentlyAborted = _activeRequest`），finishRequest 里 `req == _silentlyAborted` 的收尾不触发任何回调——"要报错的看门狗路径"与"要静默的主动中止路径"彻底分流。
- **回调接线三铁律**：①任何一方只 `-= 自己持引用的处理器` 后 `+=` 新的，**禁止 `=` 整体赋值/置 null**（会顶掉同链上他方处理器）；②"靠回调复位状态"的组件必须有**兜底复位通道**（本例=UI Send 发起对话前广播 `chatStreamTakingOver` 事件，反应侧收到即复位 _generating+摘自己的处理器）；③新消费者挂载前要摘链上可能存在的上一方处理器（防增量双重消费=双重打字）。
- 次要修复同批：pet_react.jsonl 续号解析的 `break` 写在 try/catch 之外——注释宣称"坏行继续往前找"实际不执行，坏尾行时 `_seq` 归 0、重启后新事件被消费基线当旧事件跳过。**教训：声明式注释（"继续找"）必须与命令式代码（break 位置）对得上，改控制流时连注释一起核对。**

---

## F. 构建与平台差异（编辑器 vs 真机 / PC vs 移动）

## 1. 场景切换关闭时全屏闪烁（Build-only）

### 现象

从背包、设置、联机场景关闭返回大厅时，屏幕有瞬间全屏闪烁。
从地图、祈愿场景关闭正常，无闪烁。

**关键特征**：编辑器中不闪，仅 Build（导出后）闪。

### 根因

`MainHallScreen.UpdateBackgroundAsync` 在每次返回大厅时都执行 `Addressables.Release` 释放旧背景精灵，再异步重新加载同一张精灵。

返回大厅的流程：
1. 场景卸载 → `OnSceneActivated` → `ResetAndPlayEnterAnimation` → `UpdateBackground`
2. `UpdateBackgroundAsync` 先 `Addressables.Release(_bgHandle)` 释放旧精灵
3. 异步 `LoadAssetAsync` 加载新精灵（至少跨越 1 帧）
4. 加载完成前，`backgroundRenderer.sprite` 仍指向已释放的纹理

**释放后到加载完成前的空窗期**，SpriteRenderer 渲染空白 → 相机显示 Skybox/清屏色 → **全屏闪烁**。

### 为什么编辑器不闪

编辑器中 Addressables 缓存命中，释放后纹理仍有效（引用计数未归零或 GPU 资源未回收）。

### 为什么 Build 闪

Build 中 GPU 资源回收更积极，释放后纹理立即失效。

### 为什么只有背包/设置/联机闪

三个场景都有毛玻璃（`UIBlurCapture` + `UI/Blur` shader），地图和祈愿没有。排查过程中一度以为是毛玻璃材质切换导致，但实际根因是背景精灵的重载空窗期。毛玻璃是干扰项。

### 修复

在 `MainHallScreen.UpdateBackground` 中增加 `_lastBgAddress` 缓存：

```csharp
private string _lastBgAddress;

private void UpdateBackground(PositionName position)
{
    // ... 计算 address ...

    // 地址相同且精灵已加载 → 跳过重载，避免 Release 后到 Load 完成前的纹理空窗期
    if (address == _lastBgAddress && backgroundRenderer.sprite != null)
        return;

    _lastBgAddress = address;
    StartCoroutine(UpdateBackgroundAsync(address));
}
```

返回同一位置时地址不变（位置没变、时段没变），直接跳过 Release+Load 循环，精灵纹理持续有效，无空窗期。

### 排查过程中的弯路

1. **假设毛玻璃材质切换**：修改 `UIBlurCapture.OnDisable` 多次（SetAlpha/enabled/material 恢复），均无效。
2. **假设事件通知延迟**：在 `GoBackCoroutine` 卸载前发 `OnSceneWillUnloadEvent`，无效。
3. **假设异步卸载残留帧**：在 `GoBackCoroutine` 中直接 `SetActive(false)` 毛玻璃，无效。
4. **最终定位**：`UpdateBackgroundAsync` 的 Release+Load 空窗期是 Build-only 的帧时序问题，与毛玻璃无关。

---

## 3. 导出/构建相关陷阱

### 3.1 "编辑器正常但导出崩溃"排查模式

**第一步**：读 Player.log 和 Crash 报告：
- Player.log：`%USERPROFILE%\AppData\LocalLow\{Company}\{Product}\Player.log`
- Crash 报告：`%USERPROFILE%\AppData\Local\Temp\{Company}\{Product}\Crashes`

**最常见原因**：

1. **Shader 被 stripping 剥离**：`Shader.Find()` 引用的自定义 shader 未加入 `GraphicsSettings → Always Included Shaders`，导出时被剥离。堆栈含 `Canvas:GetDefaultCanvasMaterial` 或 `material null`。
2. **Resources.Load 路径下资源未打包**：检查 `Assets/Resources` 目录。
3. **`#if UNITY_EDITOR` 代码块在导出后消失**：导致逻辑缺失。

**预防措施**：
- 项目中所有 `Shader.Find()` 调用的 shader 必须在 Always Included Shaders 中
- 代码中 `Shader.Find` 返回值必须 null check
- 避免运行时访问 `Graphic.materialForRendering` / `defaultMaterial`

### 3.2 URP Particles/Unlit shader blend 模式被重置

URP/Tuanjie 引擎下 `Universal Render Pipeline/Particles/Unlit` shader 的 blend 模式属性（`_SrcBlend`/`_DstBlend`）会被 shader GUI 在 `AssetDatabase.Refresh` 时重置。关键字 `_BLENDMODE_ADDITIVE` 会落入 `m_InvalidKeywords` 而非 `m_ValidKeywords`，即使手动编辑 .mat 文件也会被重新导入覆盖。

**正确做法**：不要用 URP 内置 Particles/Unlit shader 的材质属性控制 blend 模式，创建自定义 shader 在 SubShader 中硬编码 `Blend SrcAlpha One`，彻底绕过 shader GUI 重置问题。

**注意**：`SetFloat` 不生效时用 `SetInt`（这些属性存储在 `m_Ints` map 中）。

### 3.3 unity_scene save 工具保存路径错误

`unity_scene save` 工具会保存到错误路径（`Assets/{name}.scene` 而非 `Assets/Scenes/{name}.unity`）。GIC 项目所有场景都用 `.unity` 扩展名，Build Settings 也引用 `.unity` 路径。

**正确做法**：保存场景时用 `execute_csharp_script` 调用 `EditorSceneManager.SaveScene(scene, "Assets/Scenes/{name}.unity")`，不要依赖 `unity_scene save` 工具。如果已生成了 `.scene` 文件，用 `AssetDatabase.MoveAsset` 改回 `.unity`。

---

## 20. 触摸→鼠标模拟把第二根手指当右键（"手机双指刚放上就关闭界面"，2026-09-02）

### 现象
真机双指捏合缩放地图，第二根手指落下的瞬间界面被关闭（表现为"刚放上就触发取消"）。

### 根因
Unity legacy Input 的**触摸→鼠标模拟**是官方行为：`Input.simulateMouseWithTouches` 文档原文 "a two-finger tap will be equal to a right-button mouse click"——第二根手指按下 = `GetKeyDown(KeyCode.Mouse1)` 为真。而 InputManager 把 `Mouse1` 绑定为 CloseUI 动作，派发循环立刻关掉顶层界面。

### 修复与规范
- InputManager.IsAnyKeyDown：`Input.touchCount > 0` 时忽略所有 `KeyCode.Mouse0~Mouse6` 绑定键——模拟出来的鼠标键不是真实按键输入，触摸交互一律走 EventSystem/各控制器 HandleTouch；PC 真鼠标 touchCount 恒 0 不受影响。
- **勿用 `Input.simulateMouseWithTouches = false` 全局关**：游戏内派蒙拖拽轮询 `Input.GetMouseButton(0)` 依赖该模拟（移动端单指拖派蒙的输入源），全局关会断它的输入。
- 移动端关闭界面正路=安卓返回键（ESC 映射），Mouse1 绑定保留给 PC 右键。

---

## 21. Android 运行时 Screen.SetResolution 宽高比失配拉伸（"真机画面严重压扁"，2026-09-02）

### 现象
APK 真机运行画面整体被压扁（横屏画面被纵向压平）。

### 根因
`SettingsApplier.ApplyDefaultFullscreen` 启动时无条件 `Screen.SetResolution(native.w, native.h, FullScreenWindow)`，其中 native 取 `Screen.resolutions` 末项、**空表回退 1920×1080**。这是桌面逻辑（防桌宠子进程写坏注册表窗口尺寸）：手机面板是**竖屏原生**而游戏横屏锁定，Android 上 `Screen.resolutions` 报告的分辨率与横屏画面宽高比失配（空表/末项非本机时更甚），失配分辨率被拉伸铺满整屏 = 压扁。运行时 SetResolution 与显示器宽高比不一致时部分平台直接拉伸（Unity 不会替你保持像素纵横比）。

### 修复与规范
- 移动端（`Application.isMobilePlatform`）**一律不调 Screen.SetResolution**：启动读档（含窗口化分支）、无存档兜底、设置界面分辨率下拉回调三处全守卫——分辨率/窗口化是桌面概念，移动端画面恒原生全屏。
- 桌面专属逻辑跨平台复用前先想"手机上这个 API 语义还成立吗"（同类前科：桌宠注册表窗口尺寸）。

---

## 12. Tuanjie 包名双存储与切平台重置（2026-08-21 根治）

### 现象
ProjectSettings.asset 的包名（expectedBundleIdentifier）在编辑器切平台后被重置为模板默认值 `com.DefaultCompany.2DProject`，长期靠提交前人肉核对（曾混入一次提交后手动还原）。git 历史佐证：07-25 正常 → 08-09（引入 QuickAPKBuilder、首次切 Android 构建）被重置 → 之后反复人肉改回。

### 根因
Tuanjie 包名存储两处：
- `expectedBundleIdentifier`（≈ `PlayerSettings.bundleIdentifier` 反射属性，public；Inspector 的 Package Name 写这里）。**也是 applicationIdentifier 映射表缺条目平台的有效包名回退源**——这解释了为什么 Android 条目缺失时 APK 包名依然正确。
- `applicationIdentifier` 按平台映射表（Unity 标准）。

本项目映射表自 2D 模板创建起只有 `Standalone: com.DefaultCompany.2DProject` 一条（Android 条目缺失）。切回 Standalone 平台时引擎用 `map[Standalone]`（模板默认值）同步 expectedBundleIdentifier → 正确包名被污染，回退源随之失效。

### 根治（三层防线）
1. **消灭污染源**：`PlayerSettings.SetApplicationIdentifier` 把映射表 Android + Standalone 两平台条目都写为 `com.HGAME.gic`。切任何已知平台，同步源都是正确值。
2. **启动自愈守卫**：`Editor/Tool/PackageNameGuard.cs`（[InitializeOnLoad] + delayCall）——校验 expectedBundleIdentifier 与映射表两平台条目，漂移即自动修复 + SaveAssets + 告警（SessionState 去重防刷屏）。覆盖未知的引擎写入路径。
3. **构建前断言**：QuickAPKBuilder 切平台后强制校验 Android 包名，异常即修——保证 APK 产物正确。

### 规范
- 以后改包名：改 `PackageNameGuard.CorrectPackageName` 常量 + 菜单 Tools/包名校验/立即校验并修复；不要再手动改 Inspector 后依赖记忆核对
- 该守卫只认常量一个包名；若未来多平台不同包名需求，守卫需按平台拆常量
- 旧的"提交前核对 ProjectSettings 包名"纪律可退役；若见 `[PackageNameGuard] 包名被重置为 xxx` 告警，说明存在新写入路径，看告警值即可定位来源

---

## G. 编辑器工具与资产管线

## 7. Tuanjie UITK 编辑器工具陷阱（P12b 期间，2026-08-16）

> 完整陷阱表与标准骨架见 skill `gic-editor-tool`；此处只记当次踩坑实录。

### 7.1 SerializedObject.GetIterator() 上直接 GetEndProperty() 触发 Assert

根级全字段遍历若写成 `var it = so.GetIterator(); var end = it.GetEndProperty(); while (it.NextVisible(true) && !EqualContents(it, end))`，Inspector 首帧即 Assert "Invalid iteration - (You need to call Next (true) on the first element)"。

**正解**：根级遍历不配 end——`bool enterChildren = true; while (it.NextVisible(enterChildren)) { enterChildren = false; ... }`。元素级子属性遍历（`element.Copy()` 后配 `element.GetEndProperty()`）则正常（ConfigEditorUITK.CreateList/编辑窗口均用此模式）。症状在"资产恰好被选中"时立即暴露，平时静默。

### 7.2 VisualElement.Bind() 扩展方法不可用（CS1061）

Tuanjie 中 `Bind()`/`PropertyField` 的 UITK 绑定扩展在 **UnityEditor.UIElements** 命名空间（标准 Unity 同款但 IDE 默认 using 不会带上）。新写 Inspector/窗口报 CS1061 时补 `using UnityEditor.UIElements;` 即可。

### 7.3 编辑器窗口换游戏字体

`root.style.unityFontDefinition = FontDefinition.FromFont(font)` 在根元素设一次即可全树继承（UITK 字体继承）。字体源文件用游戏 TMP 字体对应的 ttf（`TextMesh Pro/Resources/Fonts & Materials/zh-cn.ttf`，即 zh-cn SDF 的 m_SourceFontFile）；LoadAssetAtPath 失败时静默保持默认字体，勿因字体缺失抛错。封装：`ConfigEditorUITK.ApplyGameFont(root)`。

---

## 9. 桥接脚本僵尸循环：on-demand 导入占死主线程（2026-08-21）

### 现象
编辑器"导入卡死"假象：主线程忙碌 15+ 分钟无响应，但 Editor.log 仍在持续滚动——同一批 8 个至冬堡 ogg（同 GUID）被反复 on-demand 导入。看似"慢"，实为死循环。

### 根因
上个会话经 execute_csharp_script 跑的 [FillBGM] 位置 BGM 填充脚本：磁盘上 8 个新 ogg 尚未入库，脚本内"补导缺失音频 → 二次填充"循环未收敛，重跑 34 轮。每轮对每个 ogg 的加载都触发一次 on-demand 导入 + 全量 Asset Pipeline Refresh（~2.5s），单文件 ~12s——主线程被无限占据。与 §8.2.1 的"桥超时自动重试"同族不同源：这次是**脚本内循环**（Phase 1 日志仅出现 1 次、Phase 2 日志出现 34 次可证），更隐蔽——日志持续推进、CPU 缓涨，容易误判为"导入慢"而一直等。

### 诊断通道（主线程卡死时唯一可用）
- `unity_job list`：走后台线程，主线程僵尸也能响应（用于排除桥自身挂起）
- Editor.log 尾部分析：时间戳是否推进 + 内容是否重复（**同 GUID 反复导入 = 循环**）
- `Get-Process` 间隔几秒采样两次 CPU：区分"真死"（CPU 不动）与"慢/循环"（CPU 缓涨）
- **读 Editor.log 必须 `-Encoding UTF8`**：PowerShell 默认按 GBK 读出乱码，会误判日志内容

### 恢复流程（已验证，磁盘成果无损）
1. `Get-CimInstance Win32_Process` 按命令行锁定 `-projectPath` 指向本项目的**主编辑器** PID（勿杀 ImportWorker/Hub/Licensing，它们随主进程自动退出）
2. `Stop-Process -Force` 杀主编辑器
3. 重启编辑器：Library 中已完成的导入与已保存的 .asset 修改**全部保留**（本次 8 个 ogg 已入库、PositionConfig 填充已落盘，重启后直接可用）
4. 落盘验证用 `git diff`（本次 clip 引用 27→35，GUID 与导入日志逐一吻合）；**不要用文本 GUID 检索**——Tuanjie 加密 meta GUID 机制下 meta 里是密文，必然误判"零引用"

### 规范（防复发）
- execute_csharp_script 脚本**禁止在循环里触发 on-demand 导入**（加载未入库资产）。大量新资产先让编辑器自然 Refresh 一次性入库，填充脚本只读写已导入资产、单轮跑完
- 填充/修复类脚本必须可收敛：跑完即返回并断言结果，不写"未成功就重试"的循环——失败原因（如本次"匹配不到文件"）应打日志退出，由人决定下一步

---

## 23. SaveAsPrefabAsset 往返污染场景序列化（PaimonInGameRoot.prefab 化实证，2026-08-27）

### 现象
把场景物体收进临时公共根 → `SaveAsPrefabAsset` → 还原层级后，场景 git diff 出现 327 行 RectTransform 序列化噪声（m_AnchorMin 0.5→0、SizeDelta 100→0 之类）。

### 根因
"收进临时父物体→还原"的 Parent/Reparent 往返本身会改写 RectTransform 锚点/尺寸序列化值，即使代码逻辑未动。

### 修复与规范
- 任何"临时改层级→存 prefab→还原"操作后，git diff 场景必须逐类检查：非零 diff 且全为锚点/SizeDelta 类行=直接 `git checkout` 场景 + OpenScene 强制重载（磁盘还原≠编辑器内存态，同 gic-pet skill 铁律 6）+ 抽查字段值确认无损。
- 噪声混进提交会污染历史（值级 diff 也会淹没真正的改动）。

### 23a. 迁移防丢值三则（2026-08-29 命名迁移丢值事故，已修复）

1. "改默认值+重保存"路径会顶替一切场景值≠代码默认值的字段——迁移/批量改字段后必须 diff **值**而不只看键名；排查"早期正常现在坏"回归同法（工具 `.codely-cli/tmp/value_diff.ps1` 按块比值序列）。
2. 删除 FormerlySerializedAs 前必须按资产全量扫描旧键（Resources/ 下 prefab 不在场景重存范围，会成孤儿键丢引用）。
3. 编辑器工具引用运行时字段必须跟字段名走（字段改名后中文 FindProperty 静默断裂）。

---

## 24. Tuanjie SpriteAtlas API：Add/SetIncludeInBuild 是方法不是属性（2026-08-29）

`atlas.Add(objs)` / `atlas.SetIncludeInBuild(true)` 是 `UnityEditor.U2D.SpriteAtlasExtensions` 扩展方法，不是可赋值属性——编辑器脚本按属性直觉写法会编译失败。Atlas 清单与入图规则见 docs/20 §1.4。

---

## 25. .asset 外部编辑两陷阱（2026-09-02 安柏对齐实证）

### 25.1 内存滞留：外部改 .asset 后编辑器读回旧对象
编辑器运行中用外部文本替换改 .asset 后，LoadAssetAtPath 读回可能仍是**旧对象**：AssetDatabase.Refresh() / ImportAsset(ForceUpdate) / wait_for_idle 均未必触发重载。

**唯一可靠同步**：内存对象直接改（序列化属性/字段）+ EditorUtility.SetDirty + AssetDatabase.SaveAssets 写穿——磁盘对、内存旧时此法一并同步，幂等安全。

### 25.2 技能参数双编号：资产 key ≠ 本地化表 Id
UnitConfig.asset 的 customParams `key: N` 用 **SkillParamKey 枚举值**（15=C2MoveSpeed、36=C2EnergyLimit、37=StackLimit），与 SkillParamName 本地化表 entry Id 是**两套历史编号**（表里 C2MoveSpeed=13）——改资产 key 前必查 SkillParamKey.cs 枚举值，勿拿本地化表 id 当资产键（实证：StackLimit 误写 key:15，15 实为 C2MoveSpeed，读回 GetInt(StackLimit)=-1 抓出）。

### 规范
- 外部改 .asset 后一律编辑器读回断言（GetInt 默认值 -1 探测）；读回与磁盘不符时勿再信 Refresh，直接内存改+SaveAssets

---

## H. 桌宠 · Win32 · 双进程

## 16a. GetActiveWindow 后台启动返回 0——主游戏拉起桌宠的句柄竞态（"快速跳过启动动画后派蒙报错"，2026-08-30）

### 现象
主游戏启动期（用户按键快速跳过 Splash）拉起桌宠进程后，桌宠报 `未取到窗口句柄，窗口改造失败`，随后 `PetChatUIController.Update` 每帧 NullReferenceException 刷屏（单会话 9.6 万条）；桌宠显示为带边框普通窗口（无透明/置顶）。

### 根因（两个叠加）
1. **`GetActiveWindow()` 语义误用**：它返回*调用线程消息队列的激活窗口*——桌宠进程在后台被拉起、焦点一直在主游戏窗口（用户正按键跳动画）时，桌宠自己的窗口从未被激活 → 返回 IntPtr.Zero。这是颗从立项起就存在的隐形雷：手动启动桌宠（窗口天然获得焦点）永远不会踩到，"主游戏拉起+用户抢焦点"才触发。
2. **Start() 提前 return 吞掉后续接线**：旧代码句柄失败就 return——把与 Win32 窗口毫无依赖关系的 `接聊天()` 一起吞了（聊天只依赖 Unity Canvas）→ 聊天 UI 无 Canvas → Update 裸炸。**Start 里的 return 会跳过其后全部初始化——可选功能必须放 return 之前或独立 try-catch**（宿主 Awake 防泄漏同款教训）。

### 修复与规范
- 句柄获取三级化（`PetWindowController.acquireWindow`）：①`GetActiveWindow`（有焦点最快）②**`EnumWindows` 按进程 PID+可见+标题=产品名找本进程主窗口（焦点无关，根治手段——Unity 播放器主窗口标题=Application.productName）**③窗口创建晚于 Start 的竞态→协程每帧重试 5s。
- `接聊天()` 挪到窗口改造之后但**不受其失败影响**：窗口改造整体失败=普通带边框窗口+聊天照常（降级而非瘫痪）。
- `PetChatUIController.Update` 开头 `_canvas == null` 防御 return——宿主接线前恒静默。
- **任何"取自己窗口句柄"的需求勿用 GetActiveWindow**（焦点依赖）；EnumWindows 按 PID 匹配是标准做法（VPet 同款思路）。

---

## 16. pet.json 密文双进程持久化两雷（"重启后设置里有 key 对话报未设置"，2026-08-29）

### 现象
导出构建后设置界面显示已设 key（主存档 petApiKeyCipher 有密文），但对话报未设置；重新点开 key 弹窗确认一次后才正常。

### 根因（两个叠加）
1. **编辑器会话 `PetPrefs.Save()` 恒跳过**（`#if !UNITY_EDITOR` 门）：设置写入密文走 `PetPrefs.Load().chatCipher = x; Save()`——编辑器里调试时密文从未落盘；且与构建共享 persistentDataPath 的场景下（编辑器设 key→构建版测试）构建版读到的 pet.json 里根本没有密文。
2. **跨进程陈旧缓存整体覆写**：桌面桌宠进程与主进程共享 pet.json（双进程铁律的共享通道）。桌面进程先启动→缓存了无密文的档→用户在主进程设置 key→桌面进程一次滚轮缩放触发防抖 Save()=**进程内陈旧缓存整体覆写 pet.json，密文被抹**。

### 修复与规范
- **"设置类"字段（密文/供应商索引）与"易变状态"（缩放/位置）走不同通道**：设置类经 `WriteChatCipher/WriteChatProvider` 磁盘读改写（保留其它字段含另一进程刚落的缩放）且**编辑器也生效**（无 #if 门——key 是玩家设置不是易变桌宠状态）；易变状态路径 `Save()` 落盘前**设置类字段一律取磁盘现值**（陈旧缓存防抹除合并）。
- **读侧同样直读磁盘**（`ReadChatCipher/ReadChatProvider`）：跨进程新鲜（另一进程刚写入立即可见）+ 天然"改 key 即生效"；聊天是低频操作，每请求一次小文件读+AES 解密开销可忽略。
- 排查"设置里有 X 但运行时说没有"类问题：先分清**两个存档域**（主存档 vs pet.json）与**两个进程**——显示走主存档、桌面进程读 pet.json，中间靠设置界面同步；同步点断在哪一环（编辑器跳过/缓存覆写/读缓存）用"删 pet.json 后只设 key 不做其它操作再查文件"定位。

---

## 17. 聊天输入期冻结物理收尾=永久拎起姿势+视线失联（2026-08-29 两形态同坑）

### 现象
①派蒙头不再跟踪鼠标；②拖拽后放下反应还没播就单击开对话→永远卡在被拖拽后的姿势。两症状同根。

### 根因
宿主拖拽轮询的"聊天输入期"分支整体早退（防输入焦点误触拖拽），**把物理收尾轮询也一并跳过**：`dragPhysics.IsActive`（松手后的四肢弹簧收尾期）永真 → 行为层 Update 恒走 `dragPhysicsPhase`（Drag01 拎起动画循环 + `Set视线静默(true)`）→ 永久拎起姿势 + 视线层静默（头不跟鼠标）。输入条若常开（发送后保留），冻结无限期。

### 修复与规范
- **帧序铁律：物理推进（松手/收尾）在交互轮询里无条件先行，聊天输入门控只拦"新拖拽起手/单击判定"**——两形态（PetInGameHostController.dragPollFrame / PetWindowController.DragAndClickFrame）同构重排。
- 桌面版同坑在补聊天时一并规避（物理块上移到门控前）；今后任何"输入期冻结交互"的新分支都不得包裹物理收尾帧。

---

## 30. TMP 字体"看似子集实为全库"——勿把烘焙字形表当字体文件覆盖度；图集多页开关才是 tofu 真根因（2026-09-07 实证 + 当日纠错）

### 现象
派蒙聊天回复大量"口"形 tofu（哪/儿、嗯/哈 等口语字重灾区）。初诊误判"zh-cn.ttf 按游戏文案子集化、集外字无字形"——**误诊过程本身是第一坑**。

### 根因（文件级 cmap 纠错后）
- zh-cn.ttf（SDK_SC_Web，MD5 与原神安装 MiHoYoSDKRes\...\font\zh-cn.ttf 逐字节一致）是 **11MB 大字库**，文件级 cmap 口语字（哪儿嗯哈①…）**全有**
- 真根因：zh-cn SDF 资产 **isMultiAtlasTexturesEnabled=False** + pointSize=90 下 1569 字已把单张 4096² 图集烘焙到极限 → 运行时 Dynamic 补字必失败 → 渲染 tofu
- **诊断铁律：TMP_FontAsset.HasCharacter 测的是烘焙字形表，Font.HasCharacter 测的才是字体文件 cmap——两层不可混淆**（此前"缺 56 字"测了前者层，错判字体死刑）

### 修复与规范（2026-09-08）
- 开 isMultiAtlasTexturesEnabled + 预烘 77 个对话高频字（字形 1646、图集 2 页）——Dynamic 补字恢复工作，tofu 根治
- **建 Dynamic 字体资产必开多图集**（pointSize=90 的单张 4096² 约 900~1500 字即满，全量烘焙+动态补充是必炸组合）
- 人设去特殊符号（v2.1）保留为附带防御：模型会模仿 system prompt 的符号风格，①「」 类即使能兜底显示也破坏观感，禁令依然成立
- 项目字体全量统一 zh-cn SDF（2026-09-08 用户拍板）：InputPopupDialog/CoopScreen 原 SourceHanSans SDF 引用（m_fontAsset+m_sharedMaterial 双形态）已切至 zh-cn SDF；SourceHanSans SDF 资产留库无引用

---

## 31. 反射拿 Unity 字段判空必须 as 成具体类型——object 层 .NET null 比较认不出 fake null（2026-09-09 命座图标接线两次反转实证）

### 现象
编辑器脚本反射遍历 UnitConfig 技能条目给空 icon 赋值：目标条目明明是"未接线"（icon 序列化为 fileID:0），守卫 `iconF.GetValue(s) == null` 却恒为 false → 赋值分支永不执行（wired=0 静默失败）；同一批数据的另一个诊断脚本用 `GetValue(s) as Sprite; if (icon == null)` 却正确判空——两个脚本对同一字段得出相反结论。

### 根因
Unity 的 `UnityEngine.Object == null` 是**重载运算符**：missing/空引用反序列化产物（fake null，fileID:0/0 guid 引用）在 Unity 比较下等于 null。但反射 `FieldInfo.GetValue()` 返回 `object`，`object == null` 走 .NET 引用比较——fake null 是一个真实存在的伪装对象，引用非空 → 判"有值"。

### 规范
- 反射取 Unity Object 字段后**立即 `as Sprite`/`as GameObject` 等具体类型再判空**（具体类型的 == 编译绑定为 Unity 重载，fake null 正确识别）
- 遍历判断"字段是否为空引用"的诊断脚本同理；`GetValue` 结果直接 `??`/`== null`（object 语境）判 Unity 字段 = 误判
- 本项目 icon 空引用即 fileID:0 形态（fake null），任何"批量给空引用赋值"的桥脚本都先按本条自查守卫写法

---

## 32. Prefab 实例引用编辑不持久化 + flag 文件自愈协议（2026-09-05 背包页签迁移两次实证）

### 现象
对场景中 prefab 实例的组件做 C# 赋值（sprite/color/GO 改名）+ SaveScene，部分修改不持久化（新增对象与字段覆写可持久化，m_Sprite/m_Color/m_Name 赋值丢失）——疑与 Additive 加载后 prefab 资产先被 SaveAsPrefabAsset 重建的同步时序有关。

### 对策
1. 实例级修引用优先**内容替换法**——不动 override，直接换被引用文件内容（guid/meta 不变，零风险）。
2. 必须动实例 override 时走 YAML 手术：备份→删块→删 PrefabInstance 修改项→校验（scene-local 无 guid 的 fileID 引用必须可解析到块；跨文件 fileID: 21300000/11500000/100100000 等子资产引用合法，勿计为悬空）→留 flag 自愈让 Unity 序列化器最终验证。
3. 校验 dangling 时区分场景内引用与跨文件子资产引用，否则 700+ 误报。

### flag 文件+InitializeOnLoad 自愈协议（无 execute_custom_tool 时的批量场景编辑通道）
写 `.codely-cli/tmp/<ToolName>.run.flag` → 编辑器下次刷新自动执行并写 result.txt；用户全屏应用时 SetForegroundWindow 偷不到焦点、refresh 不触发，flag 留到下次刷新自愈；**Play 期不消费 flag**（AutoRunHook 守卫，退场后下次刷新自愈）；编辑器窗口标题 "gic - 场景名" ≠ Play 状态判据；**编译失败时旧程序集钩子仍会消费 flag 跑旧版**——重跑前确认编译通过。

---

## 33. 祈愿事件必须延一帧发（AutoRunner/PlayerObserver 漏第一发，2026-09-07 实证）

WishDrawController 祈愿期事件（如 `OnIntertwinedAimStart`）要给 AutoRunner/PlayerObserver 消费时，必须**延一帧经 InputCoroutine 发**——两者都在 StartWish 返回后才挂订阅，StartWish 内同步发会漏第一发。

**规则**：事件订阅方晚于触发方挂载时（同帧构造顺序），跨对象事件一律延一帧发。派蒙祈愿反应扩展规则表见 docs/19 §6.5；机制细节见 docs/12 §6.7。

---

## 34. AI 生成分段产物（is_segmentation）URL 404 NoSuchKey——白底图+本地泛洪抠图绕过（2026-09-12 实证）

generate_image 走 `is_segmentation=true`，任务 completed 但产物落在 `aigc/segmented/*.png`，直链 GET 永远 `NoSuchKey`（两次任务+间隔重试+files/ 路径探测全 404）——服务端分段上传链路坏，非本地网络问题。

**绕过**：改出**纯白底**图（is_segmentation=false，提示词强调"completely pure flat white background, no shadow"），本地像素级抠图（Unity 编辑器脚本 Texture2D.GetPixels32 + 从四条边 BFS 泛洪近白连通域置 alpha 0，只清与边框连通的白色保住物体内部高光，再按内容 bbox 裁剪）。实装于战斗草簇透明图：Assets/Art/Battle/Textures/battle_grass_tuft_raw.jpg → battle_grass_tuft.png。

**规则**：需要透明底的 AI 素材一律白底生成+本地泛洪抠图，不再走 is_segmentation 分段链路；抠图属像素级客观处理（非识图判定），可信。

---

## 35. 编辑器 Play 测试会被 Boot 启动链异步屠场——必须等链走到 MainHall 再加载被测场景（2026-09-12 实证）

编辑器活动场景=Boot 时进 Play，Boot→SplashScreen→MainHall 生产启动链自动异步推进，每步 Single 加载会**销毁一切已加载场景**。桥脚本里 LoadSceneAsync(被测场景, Additive) 后若被测逻辑耗时超过启动链（约 3-5s），被测场景会被链的 Single 加载屠掉——表现为"对象已销毁但仍访问"（StartCoroutine 抛 The object has been destroyed）。

**规则**：exec_runtime_script 驱动场景级测试时，先等 `MainHall` 出现在已加载场景清单（生产栈就位、链停止推进），再注入配置/加载被测场景；对已捕获引用先做 Unity 假 null 判活。战斗开局端到端测试即按此模式（BattleLaunchConfig.Prepare 注入 → Additive 加载 BattleScreen → Close 验证回 MainHall）。

---

## 36. 桌宠构建无 Addressables 运行数据——触 Localization 必报错链；桌宠进程换血必须验 StartTime（2026-09-12 实证）

**症状**：桌宠构建产物（Builds/PetSpike）启动即报错链：`Invalid path in TextDataProvider: .../StreamingAssets/aa/settings.json` → `No Location found for Key=Locale` → `SelectedLocale is null. Could not load table.`。功能无损（全部 fallback 兜底），纯日志噪音——自 2026-08-28 聊天 UI 落地起一直存在，2026-09-12 用户拉日志才发现。

**根因**：PetSpikeBuildTool 只 BuildPlayer 不做 Addressables 构建 → 产物无 aa/settings.json；PetChatUIController.GetLocalizedText（placeholder/busy/notWired 三键）在 WireChat 建 UI 时同步调 LocalizationSettings.GetStringDatabase() → 触发 Unity Localization 自动初始化 → Addressables 引导必败 → 报错链。

**修复**：GetLocalizedText 首行加 `if (PetMode.Enabled) return fallback;`——桌宠进程直接走 fallback 文案不碰取表；游戏内形态（编辑器/主进程，有 Addressables 数据）照常本地化。PetWishAutoRunner.Localize/CardName 无需守卫（仅主进程执行路径）。验证=新进程 Player.log 报错链归零（error lines: 0）。

**连坐陷阱——进程换血假阳性**：杀旧进程+Start-Process 后用 `(Get-Process gic) -ne $null` 判"成功"是**假验证**：Stop-Process 可能静默失败（旧进程存活），新实例被 PetSingleInstance 互斥（"桌上已有派蒙，本实例退出"）立即退出——True 来自旧进程。**规则**：换血三步=①杀后验证 `gic 进程数=0` ②启动后验证**新进程 Id/StartTime=当下** ③读日志证据时先核栈帧行号与当前代码一致（本次靠 PetChatUIController.cs:525≠新代码 529 识破日志来自旧构建）。

---

## 37. 面板化三连陷阱：Instantiate 尖峰帧吞入场动画 + 销毁必须打面板根 + 起始态必须 OnShow 同帧设（2026-09-12 实证，P2 回归用户目检发现 ×2）

**现象**：①面板打开无入场动画（瞬间完成），关闭退场动画正常；②退场动画播完后界面仍残留在屏幕上；③（同日第二次回归）点击打开时屏幕闪现一帧完整界面，随后才从偏移位滑入。

**根因①**：面板制下 `Resources.Load + Instantiate` 同步发生在打开帧（大尖峰帧）；Start 在下一帧执行，入场协程首轮 `elapsed += Time.deltaTime` 取到**尖峰帧时长**（常 >0.2s 动画时长）→ while 直接跳出=入场瞬完成。旧场景制为异步分帧加载，Start 跑在正常帧上无此问题。

**根因②**：面板 prefab 结构=`面板根{Canvas, ScreenBase 脚本子物体}`，弹出分支 `Destroy(entry.Instance.gameObject)` 只销毁了脚本子物体——Canvas（UI 内容）残留在层级容器下继续渲染。组件级断言（FindObjectsByType 计数=0）全绿但视觉残骸——**断言盲区实证**。

**根因③**：prefab 序列化态=完成态；Instantiate 帧 OnEnable/OnShow 同步跑、**Start 在下一帧帧首**，只把"设偏移+alpha=0"放 Start → 首帧渲染出完成态=闪现一帧。场景制 Start 先于首帧可见渲染，无此问题；WishScreen 的"Awake 设偏移"即本纪律既有先例。

**规范**：
- 面板入场动画协程**首帧 `yield return null`** 再开始计时（skill gic-new-screen 已入纪律；后续迁移屏同配方）
- 面板销毁以 `ScreenUnit.PanelRoot`（OpenPanel 实例化时记录）为准；回退路径沿层级**上溯到层级容器为止**，禁用 `transform.root`（会走到 GameScene DontDestroyOnLoad 场景根=灾难性误删）
- **入场起始态必须在 OnShow（实例化同帧）设置**（CacheAnimationPositions+SetEntryOffsets；PlayEnterAnimation 保留幂等兜底）
- 面板级冒烟断言必须含"层级容器 childCount"维度（开=1/关=0）与"动画真在播"时点断言（开面板 100ms 时 Entering 锁仍持有）——组件级断言不足以证明视觉正确

---

## 38. 同步 Instantiate 尖峰帧 → 全屏闪烁（间歇性）；面板池化三件套（2026-09-12 实证，冷热实测）

**现象**：面板打开瞬间整个画面闪烁一下，**间歇性**（有时正常）。

**实测取证**（逐帧 deltaTime 采样）：冷打开帧 **285.9ms**（基线 6-24ms）；热重开仅 38.1ms——**冷/热缓存交替=间歇性的谜底**。长帧期间背景视频/视差层跳帧=用户可见的全屏闪烁。

**修复演进（三件套，全部进 UIManager PanelHost）**：
1. **池化**：面板关闭不再 Destroy 而是 SetActive(false) 入池；打开复用池中实例（重开 11.6ms）。**池化连坐 bug：`isClosing` 防重入标志入池后未复位 → 重开面板永远关不掉**——RaiseShow 必须复位。
2. **渲染态预热**：只暖物体（inactive Instantiate）首开仍 157.4ms——TMP 网格/字形图集/贴图上传发生在**首次可见渲染**；预热须 alpha=0 激活两帧走完整渲染管线再入池 → 首开 74.4ms。
3. **摊开成本**：预热泵在 MainHall 就绪后逐屏实例化（每屏间隔 30 帧），尖峰摊进大厅空闲帧。

**规范**：
- 面板打开类"闪一下/卡一下"问题先做逐帧 deltaTime 取证（冷热对比），勿凭猜测修
- 面板池化后生命周期变化：**Start 只跑一次**——每开一次的动作（音乐 push/入场动画/选中态/值刷新）必须挪 OnShow，一次性 wiring 落 OnInit；`isClosing` 由 ScreenBase.RaiseShow 统一复位
- 可关闭注册随池化改为每开一次（ScreenBase.RaiseShow 自动 RegisterClosableSelf，幂等）+ OnDisable 注销（隐藏面板不再接走 ESC）
- **池化锁保险丝必须下移 OnDisable**（秒关锁泄漏实证）：入池 SetActive(false) 会打断动画协程，入场动画持有的 Entering 锁随协程死亡而悬空——原 OnDestroy 四件套的 PopAll 对永不销毁的池化面板不再触发；ScreenBase.OnDisable 统一 PopAll（场景销毁路径双触发，幂等），各屏入场协程同时补 isClosing 提前跳出（纪律③落地）
- 冒烟断言同步升级：关闭断言改"活跃子物体=0"（池实例以隐藏态留在容器下，childCount 含隐藏不再归零）；二次关闭断言必须含（isClosing 复位回归）；**秒开秒关×3 轮锁零泄漏**（快关打断入场动画的回归模式）

---

## 38b. UIBlur 毛玻璃双层的渲染顺序陷阱（2026-09-12 实证，"画面反而更亮了"）

**现象**：毛玻璃拆双层（变暗层+纯模糊层）后，界面比拆层前**更亮**——变暗层失效。

**根因**：UIBlur shader 片元输出 `col.a = 1.0`（Alpha 混合下=**完全替换**其下画面）。遮暗层 BackDim 若排在模糊层 BackPanel **之下**，模糊层渲染时会把遮暗成果整个替换成"纯模糊无变暗"——等效于把 tint 丢了。

**规范**：双层顺序=**BackPanel（模糊，先渲染）→ BackDim（变暗，后叠上）→ 内容面板**；"blur×50%+黑50%（shader 内 lerp）≡ blur 全亮+黑 50% 叠加"的恒等式**只有遮暗层在上时成立**。skill 毛玻璃双层配方已写死顺序不可反。

---

## 39. 池化生命周期陷阱：动画目标位缓存必须幂等（P3 祈愿实证，按钮全消失）

**现象**：祈愿面板化后打开，大立绘/势力图标/介绍可见，但**抽卡按钮、关闭按钮、左侧切换卡池按钮全部消失**。

**根因**：池化时序下 `CachePanelPositions()` 被二次执行——Awake（预热实例化）缓存正确目标位并把面板移到偏移位；首次打开的 OnShow 里又调了一次缓存，此时面板在偏移位 → **偏移位（±panelSlideDistance）被覆写成"目标位"** → 入场动画 lerp 到"目标位"=三滑动面板永远停在屏幕外。立绘等独立 Fade 物体不属滑动面板故不受影响——症状分界线即根因指纹。

**取证方法**：用户报障后直接用 exec_runtime_script 反射读取当前 anchoredPosition 与缓存目标位——三个"目标"恰好是序列化位 ±200（=滑入距离），一键实锤。

**规范**：
- 动画目标位缓存的**唯一合法执行点是 Awake（预热实例化）**，OnShow 只做 SetPanelsToStartOffset（消费缓存），**严禁再缓存**
- 所有 CacheXxxPositions 类方法必须带 `_xxxCached` 幂等守卫（Settings 的 animationsCached 是正例；Wish 缺守卫 + OnShow 重复调用 = 双错齐踩）
- **位置断言是面板冒烟的必备维度**（§37 断言盲区补充第二例）：只断言 alpha/锁/容器数量查不出"动画到不了位"——必须反射断言 `当前 anchoredPosition == 缓存目标位` + 跨开关目标位零漂移
- 池化迁移 checklist 新增：OnShow 内每个方法调用逐个问"这个是消费缓存还是生产缓存？生产缓存的必须挪 Awake 或加守卫"

**§39b 连坐第二例（同日实证，"重开叠加两个卡池"）**：FadeOut 类协程被入池 SetActive(false) 硬杀时，**尾部的收尾动作（SetAlpha(0)+SetActive(false)）永不执行**——面板残留"半透明+activeSelf=True"，重开后叠在新选中面板上（用户序列：切到 Furina→关闭→重开=双卡池叠加）。运行时取证 `[5]act=True a=0.04` 一发实锤。**规范**：所有 Fade/Switch 类"协程尾部收尾"的显示物，OnShow 必须提供强制复位（CharacterPanelController.ResetHidden=StopAllCoroutines+SetAlpha(0)+SetActive(false)，WishScreen.OnShow 遍历全量归零）——收尾语义不能只依赖协程跑完，必须可被 OnShow 幂等重建。

---

## 40. "全局扫描断链图片并替换"方案不可行——序列化 null 即 fake-null（2026-09-13 缺失图兜底实证）

**现象**：为做"缺失图片兜底"设计全局守卫：面板打开时扫描子树，检测"断链 sprite"（fake null：`!ReferenceEquals(s,null) && s==null`）并替换为兜底图。一跑冒烟，设置面板 **11 处合法纯色块 Image 被误伤**（Dropdown 模板 Item Background×4、Slider Handle×4、BackDim 遮暗层）——全部被换成人脸兜底图。

**根因**：**prefab 序列化保存的 sprite=null 字段，加载后就是 fake-null 对象**（§31 fake-null 判定法反面印证：空引用反序列化产物="真实存在的伪装对象"）——与真正的断链引用（fileID 指向丢失资产）在运行时**完全不可区分**。本项目大量 UI 形态就是"故意无图纯色块"（靠 color 显色），全局扫描无差别替换必炸。

**规范**：
- 缺失图兜底只能**调用点显式接入**（MissingImageGuard.Assign/Ensure）：在"应当有图"的赋值处（立绘/名片/图标加载回调）显式调用——为空即兜底+拉伸填满（preserveAspect=false）；"故意无图"的纯色块不经过守卫，天然免疫
- **禁止**再做任何形式的"扫描-替换断链图"全局方案（含编辑器批量工具）；审计类需求只能做"报告不改动"
- 兜底资产放 Resources（同步加载保障）：`Resources/UI/missing_image`（548x533，用户指定的醒目图）

---

## 41. 手势识别器新特性必须带"未就绪输入"负路径断言——默认 struct 字段会产出垃圾基准（2026-09-13 P2 回归实证）

**现象**：P2 Map 迁移后用户目检报"拖拽被判定为缩放"——单鼠标拖地图全程变成缩放，pan 失效。

**根因**（两个叠加）：①PinchRecognizer 未限制指针类型，把鼠标(id=-1)追踪为第一指；②P2 新加的"晚起手"重试在 `State==Possible` 的 Moved 分支无条件调 `TryBegin()`——此时 `_id1` 从未追踪，**`_p1` 还是 struct 默认值 `(0,0)`**，`Distance(鼠标位,(0,0))`≈900px 轻松越过 1px 门槛 → 单指针"捏合"宣胜，仲裁杀掉 Drag，缩放比按"到屏幕原点的距离"疯变。36 组合成断言全绿没拦住：**"晚起手"只测了双指路径，没测单指在屏的负路径**。

**规范**：
- 识别器新增判定分支时，必须补"**输入未就绪**"负路径断言（追踪指针数不足/字段默认值参与计算）——struct 默认值（Vector2.zero 等）不会报错，只会产出**貌似合法的垃圾数值**
- 多指手势（Pinch 类）限定 `PointerKind.Touch` 才追踪（鼠标不参与捏合=原 Map/pet 语义；桌面档混指捏合从未是需求）
- 判定函数入口加"就绪铁闸"（`if (_id1 == int.MinValue) return;`）双保险——即使调用点写错也不自起手
- 测试断言的**求值时序**要在事件序列各阶段分开取值（本次 R6 把中途断言放在发完 Ended 后求值，又造成一次假失败）

**修复**（`PinchRecognizer.cs`）：`e.Kind != Touch` 一律不追踪 + 晚起手分支加 `_id1 != int.MinValue` 前置 + `TryBegin` 就绪铁闸；负路径断言 R1（单指移动不自起手）/R2（鼠标零追踪）入回归集。

---

## 42. RectTransform.rect 与 anchoredPosition 空间错位——贴屏边时桌宠输入条/气泡被钳进画布中带（2026-09-13 P4 目检实证，§39 活体诊断法）

**现象**：游戏内派蒙拖到画面边缘后单击，输入框出现在屏幕中部（x≈1297）而非模型脚底；派蒙在屏幕中部时输入条也偏左 ~290px（轻微未被察觉）。

**诊断**（exec_runtime_script 反射取证，游戏运行中）：锚点链全部正确（ChatFootAnchor=(2796,443) 跟随模型，输入条 y 精确=foot.y-20 ✓），但输入条 x=1297——**现场复算 Clamp(foot.x=2796, min, max)=1297 逐位复现 bug 本体**：min/max 来自 `(_canvas.transform as RectTransform).rect`——根画布 pivot 恒居中，rect 是**枢轴中心局部空间**（xMin=-1587, xMax=+1587, center=0）；而锚点/anchoredPosition 是**左下锚绝对空间**（0..3174）。中心空间钳左下空间锚点：屏中(1587)→钳到 1297 偏左 290px；贴右缘(2796)→钳到 1297=屏幕正中。气泡钳制同病。**为 2026-09-12 桌面边缘自适应引入的预存 bug**（非 P4 回归——in-game 路径当年"假设天然正确"未测贴边）。

**规范**：
- 避让/钳制 bounds 必须与被钳对象**同空间**：uGUI 里 anchoredPosition 是锚定空间，画布自身在锚空间恒为 `Rect(0,0,w,h)`；`RectTransform.rect` 只在"看局部尺寸"时安全（width/height 恒对），**取 xMin/xMax/center 参与跨对象运算前先想清楚空间**
- 探针复算是实锤利器：现场复现 Clamp 公式→与实际值逐位吻合→根因自证（§39 方法论第二次胜利）
- 修复在源头归一（PetChatUIController.Update 产 canvasRect 处），不在各钳制点打补丁；桌面 override 契约本就是左下空间（入参只取宽高），源头归一后两形态一致

---

## 43. 预热守卫横跨两帧窗口：用户 Open 的面板被连带跳过注册 + 冒烟工作流两条（2026-09-13 联机动画冒烟实证）

**现象**：MainHall 就绪后 ~1.5s 打开联机面板，Console 报 `[UIManager] Coop 打开后未自动入栈（OnEnable 注册链异常）`——面板开了但 `IsOpen=false`：ESC/GoBack 对它失效整个会话、Single 加载自动入池也漏掉它（僵尸面板悬浮在战场之上）。冒烟的连带 FAIL 全部源于此。

**根因**：`UIManager._prewarming` 是**全局布尔**，旧写法从 Instantiate 一直持到渲染态预热结束（含两个 `yield return null`）——泵实例的 OnEnable 只发生在 **Instantiate 同步瞬间**（prefab 根默认 active，之后的 `SetActive(true)` 是 no-op），而两帧 yield 窗口期间**用户代码照常运行**：用户 `Open` 走"池空回退同步实例化"→ 新面板的 OnEnable 撞上仍为 true 的全局标志 → 注册被连带跳过；且此刻泵还没把自建实例放进 `_panelPool` → 产生双实例（用户的一份未注册成僵尸，泵的一份闲置在池里）。

**规范**：
- **注册跳过守卫只覆盖 Instantiate 原子窗口**（set 后、首个 yield 前清）：同步调用期间用户代码不可能穿插，守卫精确命中泵实例唯一的 OnEnable；两帧渲染态预热窗口必须放开（泵实例此后无新 OnEnable，用户 Open 照常注册）。
- "预热期跳过注册链"类全局标志，写之前先问：**这个窗口里还会发生谁的 OnEnable/OnDisable？** 含 yield 的窗口 ≠ 原子窗口。
- 面板冒烟必含 `IsOpen(面板)` 断言——"实例激活"≠"已入栈"，未入栈的面板一切栈语义（ESC/GoBack/自动入池）都静默失效。

**冒烟工作流两条（同日实证）**：
- **exec_runtime_script 完成后编辑器留在 Play 模式**——连续两个 runtime 脚本会在**同一 Play 会话**里跑（面板栈/网络连接/场景全部残留：第二个脚本的"等 MainHall"瞬过、`Open` 报"已打开，重复忽略"）。需要干净会话时先 `unity_editor stop` 再跑下一个。
- 锁类断言若 FAIL，**先反射倾倒 InputManager._inputLocks（Owner+Reason）再推理**——本日 3 个假 FAIL 靠锁清单直接排除了"代码泄漏"假设，实际是断言时机（锁在动画完成帧稍后弹出，`fill>=1` 即断言太早）；AnimationCurve 尾段缓出时"断言间隔帧数跳变"是曲线特性不是卡顿。

---

## 44. 切换动画进行中的同态直刷竞态——离房无退场+列表闪现重扫（2026-09-13 用户目检实证）

**现象**：联机界面退出房间时房间页**瞬间消失无退场动画**；切到列表页时列表**整屏闪现后被拉回起始态重扫一遍**（用户报"抖动"）。

**根因**：离房路径对 `ReturnToDiscovery` **双触达**——`OnLeaveRoomClick` 里 `_network.LeaveRoom()`（StopHost 同步触发 `OnClientDisconnected` 回调→`ReturnToDiscovery` ①）之后紧接直调 `ReturnToDiscovery()` ②。①启动切换动画（旧面板扫出→刷新→新面板扫入），②以**同态**再次进入 `SetRoomState`——同态走"即时 RefreshUI"分支，在动画进行中 `SetActive` 切换 + `SnapAllPanelsToRest` 复位半途动画：房间页被瞬间隐藏（退场腰斩）、列表页先被复位成完成态整屏可见（闪现）、约 0.125s 后又被动画的 `SetEntryOffsets` 拉回起始态重新扫入。旧场景制时代无动画，同态双刷无害；引入切换动画后即刻可见。

**修复**（CoopScreen.RoomFlow.SetRoomState + Animation.SwitchPanelRoutine）：
- `SetRoomState` 首行吞掉"**同态且切换动画进行中**"的刷新（`oldState == newState && _switchRoutine != null → return`）——运行中动画的 applyState 会在正确时序收口本次状态（SetActive/内容/按钮文案一个不漏）；无动画进行时同态照常即时刷新
- `SwitchPanelRoutine` 的 `finally` 里 `_switchRoutine = null`——句柄即"进行中"标志，完成/被中断都归位

**规范**：
- 状态机带切换动画后，**同态重复刷新必须与动画互斥**：要么吞掉（由动画收口），要么让动画响应——绝不能让第二条路径直刷 SetActive/复位
- "双触达"型状态入口（网络回调+直调并行）是竞态重灾区——给状态机引入动画前先枚举全部触达路径
- 冒烟断言要判**病灶帧**（如"两面板同时激活且新面板满 fill"——修复前必现、修复后不可能），而非笼统的 fill 峰值（入场正常完成态/曲线尾段 ≥0.999 会造成假阳性，本日两轮假 FAIL 实证）；逐帧时序断言（激活时刻先后、首帧 fill 值）比瞬时值断言可靠

---

## 45. 全屏 UI prefab 根 RectTransform 零尺寸——子节点"屏幕中心原点"坐标全体错位（2026-09-13 联机加载页布局实证）

**现象**：原神式加载页（LoadingOverlay.prefab）词条文案与七元素图标全部挤在画面顶部/顶边之外，徽标却在中下——与设计（徽标中央、词条其下、元素行底部横贯线）完全不符。

**根因**：prefab 根节点的 RectTransform 为**零尺寸**（anchorMin=anchorMax=(0,0)、sizeDelta=(0,0)、pivot=(0,0)，锚在父画布左下角一点）——程序化摆子节点时按"屏幕中心为原点"给的 anchoredPosition（TipTitle y=468、元素行 y=634 等）全部相对这个 0×0 参照系定位，整体错位。`Instantiate(prefab, uiRoot.transform, false)` **不会自动纠正根 RectTransform 的锚点/尺寸**——prefab 里是什么就是什么。

**规范**：
- 新建全屏覆盖类 UI prefab（加载页/结算页/过场黑幕等），**根 RectTransform 必须显式设全屏 stretch**：anchorMin=(0,0)、anchorMax=(1,1)、sizeDelta=(0,0)、anchoredPosition=(0,0)——脚本创建同理（`GameObject.AddComponent<RectTransform>()` 的默认值就是零尺寸锚点）
- 诊断"子节点群体错位"先读根节点 RectTransform（anchor/size/pivot），不要先怀疑子节点坐标值本身
- 交付 UI 布局类改动时，读回断言应包含根节点锚点（本次靠 dump 全树 RectTransform 一次定位）

---

## 46. 程序化重组 UI 层级的三条 UGUI 陷阱（2026-09-13 加载页逐像素填充实证）

**①子节点 SetParent 进新容器时 anchor 参照系不迁移**：原相对画布的锚（如 y=0.06="从底 6%"）移进 56px 高的 BottomBar 层后变成"距层底 3.4px"——整行图标被挤出层底边缘。**迁移层级必须逐节点重设层内锚**（y=0.5 居中等）。

**②单边锚点节点的 pivot 必须与锚边一致**：动态宽度节点锚父左缘（anchorMin.x=anchorMax.x=0）时 pivot 默认 (0.5,0.5) 会让宽度增长以**中心**为轴对称外扩——RectMask2D 裁剪窗口（LitClip）一半跑到界外（实测 rect[-332,+332]）。**左缘锚定取 pivot=(0,0.5)**。

**③inactive 对象不能 StartCoroutine**：`d.StartCoroutine(...)` 在 `d.gameObject.SetActive(false)` 状态下直接报 Error（Coroutine couldn't be started）——预热协程（激活两帧再隐藏）必须先 SetActive(true) 再 StartCoroutine。

**规范**：程序化 prefab 重构脚本默认带"迁移后逐节点重锚+pivot 复核"读回断言；预热型协程先激活后启动。

---

## 47. 协程嵌套宿主被清栈禁用腰斩——根切换转场的锁泄漏（2026-09-13 Additive 卸载拆分实证）

**现象**：开局转场 `yield return StartCoroutine(GameScene.SwitchRootScene(...))` 写在 CoopScreen 协程里，而 SwitchRootScene 中途 `PoolAllForRootSwitch` 会 `SetActive(false)` 清栈面板——**宿主面板一被禁用，挂它上面的整个协程链（含 StartCoroutine 嵌套的 SwitchRootScene）当场腰斩**：旧根 UnloadAsync 永不执行（场景残留）+ SceneTransition 锁永不释放（后续退战被"场景正在切换中"拦截）。

**根因**：Unity 协程的生命周期=宿主 MonoBehaviour 的 active 状态；`StartCoroutine` 嵌套不改变宿主归属（谁 StartCoroutine 挂谁身上）。注释里写"嵌套协程宿主=GameScene 不受影响"是**意图**而非**事实**——实现必须显式 `GameScene.Instance.StartCoroutine(...)` 才真正挂到 GameScene。

**规范**：
- 转场/流程类协程**宿主选择是架构决策**：凡是会"干掉某些对象"的流程（清栈/入池/切场景），协程必须挂在流程中不会被禁用的宿主（GameScene/UIManager 等持久对象）上——`target.StartCoroutine` 显式指定
- 嵌套 `yield return StartCoroutine(other)` 只表达"等它跑完"，**不转移宿主**；宿主死则全链死
- 审查清单：协程内调用任何 SetActive(false)/入池/切场景的代码路径时，检查本协程宿主是否在受害名单里

---

## 48. C# 插值字符串孔内三元条件的冒号被当格式说明符——整程序集编译炸（2026-09-13 指令系统实证，并行会话协同修复）

**现象**：`GICLog.Info($"... {result.ok ? "✓" : "✗"} ...")` 直接编译失败，且报错形态是连续多条 CS1003 `',' expected` 指向后续行——极具误导性（看似换行/引号问题，实为前一行的孔内冒号）。当时堵死整个程序集，并行的另一 AI 会话做了最小语法修复（括号包裹）后才恢复。

**根因**：插值字符串的孔 `{表达式}` 内，**顶层的 `:` 分隔段被解析为格式说明符起点**（`{值:格式}` 语法）——三元条件表达式的冒号恰在孔顶层，编译器把 `✗` 当格式串吃掉后孔结构错乱。属语言解析规则而非风格问题；含内嵌字符串字面量的三元尤其易踩（孔内引号本身合法，掩盖了真凶位置）。

**规范**：
- 插值孔内写含冒号的表达式（三元 `?:`、字典索引、具名参数等）一律**括号包裹**：`{(result.ok ? "✓" : "✗")}`——括号使冒号退出孔顶层
- 疑似无关的连续 CS1003 串报错，先查同文件插值字符串的孔内是否有裸冒号
- 多会话并行时，他方对冲突文件做过的最小语法修复勿"顺手撤回"——修复本身就是对方在协调板上留的标记（语义零变化时保留）

---

## 49. 新增 MonoBehaviour 类经编辑器脚本写入 prefab——m_Script 被静默序列化为 {fileID: 0}，运行时 missing script 连刷（2026-09-13 指令补全实弹目检实证）

**现象**：编辑器脚本（`LoadPrefabContents` + `AddComponent<新类>` + `SaveAsPrefabAsset`）给 InputPopupDialog.prefab 写入建议行模板，创建与保存时**零告警**；运行时每次 `Instantiate` 报 "The referenced script on this Behaviour (Game Object 'SuggestRowTemplate') is missing!" 逐行连刷，`GetComponent<类型>()` 返回 null → 行构建/关闭两路 NRE。

**根因**：prefab 里该组件 `m_Script: {fileID: 0}`——**当轮新编译的类型**在 prefab contents 序列化时 FileID 解析不出（TypeCache/脚本注册未稳），保存器静默写 0。连锁三坑：①带 missing script 的 prefab 被 `SaveAsPrefabAsset` 拒存（报错不落盘，修复动作全白做）；②`SerializedProperty.DeleteArrayElementAtIndex` 删 m_Component 元素被拒（"It is not allowed to modify the data property"）；③Tuanjie 分支**没有**标准 Unity 2020+ 的 `GameObjectUtility.RemoveMonoBehavioursWithMissingScripts`（CS0117）。

**规范**：
- 判定：`rg --no-ignore -n "m_Script: \{fileID: 0\}" <prefab>` 一发实锤（prefab 被 .gitignore 吞，rg 须 --no-ignore；注意排除 m_CorrespondingSourceObject 等合法 0 值字段，专搜 m_Script）
- **要被 prefab/场景序列化引用的新建 MonoBehaviour 一律类名=文件名独立成文件**（主类 FileID=11500000 恒可解析；共享 .cs 的次要类走类名哈希 FileID，新类型+当轮写入易踩 0）——新建后不要当轮就跑写入脚本，先 refresh 让类型注册稳
- 修复 missing script：删不掉单个坏组件时**销毁整个 GameObject 重建**（missing script 随 GO 消失，绕过拒存），重建后重接 InputPopupDialog 等外部引用
- **保存后必须回读验证**：`SaveAsPrefabAsset` 返回 void 不报写入结果——`LoadPrefabContents` 重开 + `GetComponent<类型>` 非 null 才算落盘成功
- 纯静态断言测不到 Instantiate 路径：本例 44 条 Suggest/指令断言全绿，仍漏此雷——**带 prefab 实例化的真实弹窗测试是 UI 类功能的必要验收环**

---

## 50. 池化面板内的弹窗协程被入池腰斩——重激活带回"半透明幽灵"（2026-09-13 指令跳祈愿后回设置实证，§39b 第三例；活体诊断=暂停现场反射读 alpha）

**现象**：设置页指令弹窗输 `wish` 回车 → 派蒙自动抽卡导航**关闭设置面板（池化 SetActive(false)）**→ 弹窗 HideCoroutine（0.2s 淡出）被硬杀，alpha 冻结 0.292+_isClosing=true+active；抽完卡回设置（面板池化取出 SetActive(true)）→ 半透明弹窗残骸随面板复活。

**根因**：§39b 同族——Fade 类协程尾部收尾（alpha=0+SetActive(false)）被宿主池化硬杀永不执行；**弹窗作为池化面板的运行时子物体，协程宿主=面板的激活状态**（§46），面板入池=全体子物体协程腰斩。取证=§39 活体诊断法：用户暂停现场，resume 后反射读 canvasGroup.alpha/activeSelf/_isClosing 一发实锤（暂停态 exec_runtime_script 握手超时，须先 resume）。

**规范**：
- **池化宿主内的 Fade/开关类弹窗必须 OnEnable 自愈复位**（§39b "OnShow 强制复位"纪律的子物体版）：`if (_isClosing || _shown) ForceClosedState()`（alpha 归零+SetActive(false)+PopAll(owner)+UnregisterClosable，全幂等）——收尾语义不得依赖协程跑完。自愈覆盖三场景：淡出中途被池化（半透明残骸）、**全开态被池化**（派蒙导航强切、更糟：全透明+可交互挡输入）、淡入中途被池化（entering 锁泄漏，靠 PopAll 兜底释放）
- **自愈判据标记必须 Show 末尾置位**：`_shown=true` 若在 Show 开头置，`Show` 内的 `SetActive(true)` 会触发 OnEnable → 自愈把刚开的弹窗当场自毁（释放刚 Push 的 entering 锁、协程在 inactive 上启动报错——本例修复中实证）；**判据用运行时标记而非序列化属性**（新实例首次 OnEnable 恒 false 无动作）
- 协程完成即置空引用：ShowCoroutine 末尾 `_currentCoroutine=null`——防"完成后引用残留"被 Hide 误判为淡入中被中断而**重复 Pop entering 锁**（弹掉别人的锁）
- 弹窗宿主在池化面板内时（SettingsScreen.InputPopup/DeckSwitchPanel 同类风险），此类自愈比"宿主 OnShow 逐个复位子弹窗"更内聚：弹窗自己管自己的收尾幂等

---

## 51. `#if !UNITY_EDITOR` 块内"匿名委托捕获 out 参数"——编辑器编译恒绿、桌宠构建才炸 CS1628；伴随 Bee 玩家编译首轮陈旧文件清单的 CS0103 串扰（2026-09-13 快捷消息 GameWindowState 实证）

**现象**：编辑器 refresh 编译 0 错，ScheduleBuild 桌宠构建 `RESULT FAIL errors=2`。Editor.log 错误两组混排：`GameWindowState.cs(144,17): error CS1628: Cannot use ref, out, or in parameter 'hwnd' inside an anonymous method...`（真实错误），以及大量 `error CS0103: The name 'PetQuickMessages'/'PetLocalCommands'/'GameWindowState' does not exist`（引用三个**新建 .cs** 类型的文件全体报"类型不存在"）。

**根因**：
- **CS1628**：`TryGetOwnWindow(out IntPtr hwnd)` 内 `EnumWindows(delegate ... { hwnd = h; })`——匿名委托捕获 out 参数是 C# 硬错误；该段在 `#if UNITY_STANDALONE_WIN && !UNITY_EDITOR` 内，**编辑器永远不编译这段**（铁律 7 应验：#if 块编辑器零验证，构建期才暴露）。
- **CS0103 串扰**：BuildPlayer 的 Bee 玩家编译**首轮使用了陈旧的 PlayerDataCache 文件清单**（不含当轮新建的 .cs）→ 引用新类型的存量文件全体 CS0103；重导入后文件清单自愈，后续编译轮次才编译到新文件（真实错误 CS1628 在更晚的日志行）。日志按行号交错混排极易误判成"新文件丢了"。

**规范**：
- **`#if !UNITY_EDITOR` 块内禁止匿名委托/lambda 捕获方法签名里的 out/ref/in 参数**——经局部变量带出再赋值（`IntPtr found=Zero; lambda 内 found=h; 方法尾 hwnd=found`）；写 Win32 块时按"编辑器不编译"自检一遍闭包捕获。
- **构建失败先按"错误组"分层归因再动手**：CS0103 批量指向**本批新建类型的引用方**=PlayerDataCache 文件清单陈旧的串扰（自愈型，勿追"文件不存在"）；同日志里的 CS1628/CS0xxx 语法级错误才是真凶。修复真错误后对新建 .cs 逐个 `AssetDatabase.ImportAsset(ForceUpdate)` + 重跑 ScheduleBuild（其 isCompiling 守卫自然衔接）即可，无需重建整个导入链。
- 判据口径不变：建成与否只认 `[PetSpikeBuild] RESULT` 行（本例 `RESULT FAIL errors=2` 即真失败，与"RESULT FAIL errors=0"的编译竞态假失败区分开）。

---

## 52. ScheduleBuild 排队的构建被"编译引发的域重载"静默吃掉——无 START 无 RESULT 直接蒸发（2026-09-13 快捷消息重建实证）

**现象**：`ScheduleBuild()` 正常返回，但 Editor.log 永不出新的 `[PetSpikeBuild] START`/`RESULT`（START 计数不涨），8 分钟轮询空等。前情：调度前刚对改动 .cs 做过 `ImportAsset(ForceUpdate)`（修 §51 后的重建）。

**根因**：ImportAsset 触发**异步脚本编译** → ScheduleBuild 已把 `EnqueueBuild` 挂上 `EditorApplication.update` 且 `_已排队=true` → 编译完成的**域重载把静态状态与旧域 update 回调一并清掉**（`_已排队` 归零、委托蒸发）→ EnqueueBuild 永不执行。`BuildInternal` 的 isCompiling 守卫防的是"构建先跑、编译没落地"，防不了"回调本身被重载消灭"——调度发生在编译落点之前就必中招。

**规范**：
- **改过 .cs（或调过 ImportAsset）后再 ScheduleBuild：先确认编译/域重载已落地**（桥推 custom_tools_reloaded/stale 通知=刚重载过）再调度；**调度后 ~30s 内 Editor.log 必须出现 `[PetSpikeBuild] START`**——不出=已蒸发，直接重调（守卫位随域重载归零，无需手动清）。
- 判据链分层：RESULT 行=建成判据（铁律 1 不变）；**START 计数=调度是否真开跑的第一信号**，排队后先看 START 再去等 RESULT。

---

## 53. 残留的 FadeOutMusicCoroutine 在到期时"Stop+清 clip"误杀刚起播的新曲——开局淡出与 PreWarm 转场 <0.5s 竞态致战斗无声（2026-09-14 战斗音乐轮换链实证，§39 活体诊断）

**现象**：联机开局进战斗图无音乐；Console 零异常零警告（链路各守卫都不触发），`curType=Battle` 证明战斗曲确实起播过、`musicSource.clip=null`+音量已复位=1 证明被淡出协程到期收尾杀掉；轮换链的 `MusicCompletionCoroutine` 见 `clip != track.clip` 静默 yield break——**无声死亡无任何日志**。

**根因**：`StopMusic(0.5f)` 淡出协程只被 `StartFadeInMusic`（fadeIn>0 路径）终止，`StartMusicPlayback` 的**非淡入路径（fadeIn=0）不杀它**——淡出继续按自己时长跑，到期执行 `Stop()+clip=null+音量复位`，把期间起播的新曲当"自己人"收尾。旧版战斗曲带 fadeIn 0.5s 恰好顺手取消了残废淡出所以从未暴露；轮换链改 fadeIn=0（对齐大厅位置曲语义）后，PreWarm 把开局转场压到 <0.5s，残废淡出反杀战斗曲——竞态窗口从"不可能"变"必现"。`MusicCompletionCoroutine` 的 clip 一致性检查是守卫不是杀手：被外协程 Stop 后它只退场不续链，无声即"链死"。

**规范**：
- **`StartMusicPlayback` 已补齐"起播先杀残留淡出协程+音量归位"堵点**（2026-09-14）：任何新曲起播（PlayMusic 全家族）都会终止 musicFadeCoroutine——这是系统性修复，同族保护 PositionManager 回大厅复活链等一切"淡出后新曲可能提前起播"的路径。
- 淡入/淡出协程自然结束时 `musicFadeCoroutine = null`（防字段持已完成协程的假引用误导活体取证——本例诊断时该字段非空但协程早已结束）。
- **排障判据**：新配音乐链"零日志但无声"→ 反射读 `currentMusicType`（证明 PlayMusic 跑过）+ `musicSource.clip`（null=被外部 Stop）+ 音量（1=淡出收尾已执行）；三者组合即本陷阱指纹。
- 勿给链式轮换曲加 fadeIn 来"绕过"——fadeIn 只是碰巧取消残废协程的副作用，堵点修复才是根治。

---

## 54. 战斗场景缺 EventSystem——UI 按钮全瘫但 3D 交互正常（2026-09-14 退出弹窗按钮"点了没反应"实证，§39 活体取证第二例）

**现象**：打完对局点退出→确认弹窗正常弹出（全屏遮罩+面板都在、IsOpen=true），点「确定退出/继续战斗」无任何反应；isClosing=False 证明确认回调从未执行。局内棋盘/相机交互一直正常（掩盖了问题——用户全程没用过 UI 按钮）。

**根因**：EventSystem 活在大厅场景里；Additive 根切换（SwitchRootScene 卸载旧根）或 Single 加载清场后战斗场景**没有自己的 EventSystem**——uGUI 的 Button.onClick 靠 EventSystem 投递，缺它则**一切 UI 按钮点击蒸发**（GraphicRaycaster 挂了也没用）；棋盘交互走 BattleCameraController 的 3D 物理射线，不依赖 EventSystem，故对局玩得起来。ESC/右键走 InputManager 轮询+closable 栈也不受影响（弹窗能被 ESC 打开正因此）。

**规范**：
- **BattleScreen.AssembleRoutine 已幂等补挂**（`EventSystem.current == null` 时 new GameObject + EventSystem + StandaloneInputModule；项目 activeInputHandler=0 纯旧版 Input Manager）；**不 DontDestroyOnLoad**——回大厅随场景卸载消亡，大厅场景自己的 ES 接管，防双 ES。
- **排障指纹**：UI 按钮"点了没反应"+ 3D 点击正常 + 弹窗渲染完好 → 先查 `EventSystem.current == null`。
- **新根场景/独立场景清单须核 EventSystem**：凡从"带 ES 的场景"切到"无 ES 的场景"（根切换/Single 加载/直接打开调试），UI 即全瘫——独立场景要么场景内自备 ES，要么装配期兜底（本例模式）。
- **§54b 连锁案（同日实锤）**：补挂 ES 后**拖拽平移随之瘫痪**——BattleDebugPanel 的全屏透明 InputBlocker（防穿透挡板）在无 ES 时代是死代码，ES 一活它全屏吃射线，GestureHub 门2 把每次按下都判"按在 UI 上"（`BypassUIGate=false` 面整面收不到指针），拖拽识别器饿死；滚轮缩放走 Update 直读 Input 不进手势系统故幸存。**修复=删 InputBlocker**（独立根场景无底层界面共存，穿透防御已是 GestureHub 门2 职责；面板本体区域照常吃射线）。**教训：给场景补 ES 属"激活一切隐性 UI 死代码"的操作，须连带审计既有全屏 raycast 挡板**——探针法（EventSystem.RaycastAll 三点位）可实证。

## 55. GI 角色完整模型提取五坑：Animator→FBX 永远纯骨架、骨名哈希=CRC32(路径)、FBX 骨位置塌缩、bindpose 跨网格空间不一致、压缩哈希路径曲线不可读（2026-09-14 安柏全模型重建实证）

**现象**：想从本地原神 blocks 提取角色完整蒙皮模型（像 gpt astra 演示那样），Animator 导出的 12 个 FBX 全是纯骨架无网格；Mesh OBJ 导出丢权重；骨架挂骨盲配全错。

**根因与解法**（全部实测）：
1. **Animator→FBX 永远拿不到网格**：GI 角色 prefab 不含 SkinnedMeshRenderer（运行时装配），网格在独立 mesh bundle 里（Container 区分），`--containers` 参数会因无 container 资产 ArgumentNullException 崩——**唯一出路 `--export_type JSON` 导 Mesh**（m_Skin/m_BindPose/m_BoneNameHashes/m_SubMeshes 全在，OBJ 只给几何）。
2. **m_BoneNameHashes = CRC32(骨骼完整路径)**（从 Bip001 起的 `Bip001/Bip001 Pelvis/...`，与 .anim path 同构）——fnv/murmur/djb2 全不是；裸名也不对，必须路径。**同一哈希空间的另一证据：.anim 里的数字路径（path: 1117758078）就是同一 CRC32**。
3. **骨架 FBX 骨骼局部位置全部塌缩同一点**（HANDOFF-派蒙动画重定向早有记载）——真实绑定姿势**藏在 mesh 的 m_BindPose 里**（`bp⁻¹`=骨骼矩阵），骨架绑定姿势要靠 bindpose 逆推重建；Bip001 自身无绑定矩阵，直接子骨须按"父骨当前矩阵"逐层下推（Bip001 归零处理）。
4. **各网格 bindpose 空间不一致**：`bp = M⁻¹·S_m`（S_m=该网格 SMR 节点矩阵），同一骨骼在不同网格的 bindpose 差一个常量变换——**共享骨对比可解出 X=S_ref⁻¹·S_m，全部 `bp·X⁻¹` 归一到同一空间**（实测 spread=0 验证一致后冲突归零）。**必须按变体（本体/皮肤）分别归一**，跨变体混合必炸。
5. **FBX 缺物理骨链**（裙摆/头发等，hashMap 62/246 命中即可见）：动画曲线里它们是**压缩哈希路径**，Unity 导入后 GetCurveBindings 拿不到（派蒙重定向管线当年判"死路弃用"同类）——静态兜底骨（平铺挂 modelRoot、local=bindpose）保蒙皮精确（实测全部件 skinErr=0.00000），代价是这些链不参与动画。

**验收**：蒙皮正确性可纯数学终验——逐顶点 `Σw·(boneWorld·bp)·v ≈ v`，安柏 21 个部件全部 0.00000。视觉仍交用户目检。

**坑 6 · 导入 URP Lit 后模型整体纯黑三因（2026-09-14~15 安柏实证，2026-09-19 自项目记忆补录）**：①`_BaseColor` 未显式设白（URP Lit 默认非白）——必须显式 `(1,1,1,1)`；②SMR culling bounds 错——`updateWhenOffscreen=true`；③方向光从背面打——光转正面；兜底=ambient Flat 白、仍暗加 `_EMISSION` 35% 补光。另：GI 贴图 alpha≠真透明（Body_Diffuse alpha 99.7%=0 但 RGB 有数据）、UV 与网格不直接映射（GI shader 内变换）→ naive UV 评分法失效。gic-gi-extract skill「TMR 提取包导入路线」所指「§55 纯黑坑」即本条。

**资产**：自提版 `Assets/Art/AmberExtract/` 已于 2026-09-15 按用户拍板删除，现行=**`Assets/Art/AmberTMR/`**（TMR 直提包，154 Clips 在 `AmberTMR/Clips` GUID 不变）；提取脚本 `.codely-cli/tmp/ambor/`；操作流程已入 `gic-gi-extract` skill「完整角色模型+动画提取」节。

## 56. GI Avatar 动画=Humanoid 肌肉压缩格式：AnimeStudio .anim 导出丢主体动画 + 厘米/米制位置失配=「严重拉伸变形」（2026-09-15 安柏 TMR 实证）

**现象**：安柏 154 条官方动画在 AmberTMR 上播放严重拉伸变形；同链路派蒙 199 条 NPC 动画正常。用户怀疑「提取出的动作不是安柏的」。

**结论**：动作确属安柏本人（154 条全名 `Ani_Avatar_Girl_Bow_Ambor_*`，源 blk 04161624）；问题在**存储格式与单位**，不在归属。

**勘误**（推翻 §55 坑 5 与 skill 旧记载的「物理骨=压缩哈希不可读」——归因记反了）：
- 物理骨（+HairB/+EarS/+Breast/+LegBagS/+PelvisTwist/手指/Weapon 挂点）曲线=**可读 TRS**，是 clip 里的「通用附加绑定」，Attack_05 中 80 条真动画全在此；
- **主体骨骼（Biped 核心）才是压缩部分**：GI Avatar 动画=标准 Unity **Humanoid 肌肉 clip**（m_Compressed=true、m_MuscleClip、密集数据流式存 blk 同包 .resS；`--export_type JSON` dump 可见 m_DenseClip 空 + `archive:/...resS`）；**NPC 动画=普通 TRS**——这就是「派蒙正常 / 安柏残废」的分水岭。

**根因链（全实证）**：
1. AnimeStudio Convert 导出 .anim：肌肉绑定只写出**常量值**（Attack_05 265 曲线 185 条常量，含全部 ~40 肌肉 + RootT/Q + 手脚 IK 曲线），密集身体动画未解码导出 → 主体全程单一姿态（人偶）；肌肉 float 曲线属性名（Left Arm Down-Up 等）legacy Animation 也播不动。
2. 物理/手指 TRS 曲线在 TMR 骨架上照常播放（骨骼同源、旋转量级一致）——配上静止主体=配件乱甩。
3. **位置曲线单位失配（拉伸主因）**：clip 位置值=GI 厘米制（+HairB L B01 常量 -0.126），TMR 骨架=米制（同骨 rest -0.0013），×100 失配 → 播放时物理骨被甩到约百倍远处，蒙皮随骨拉出=严重拉伸变形。
4. 次要：WeaponL/R、AO_、HitObject 路径 TMR 无对应节点不绑定（无害）；`path_3559852561` 等 3 个字面量路径不在 132 骨路径表内（binding 表 43/47 命中，哈希空间=CRC32 无误）。

**验证工具**（`.codely-cli/tmp/ambor/`）：`curve_stats.cjs` 曲线方差统计（区分真动画/常量）、`reverse_hash.cjs` CRC32 反查；源头结构用 CLI `--export_type JSON` dump AnimationClip。

**修复方向（2026-09-15 待拍板）**：A=止血（位置曲线 ×0.01 或剥离，主体保持常量姿态）；B=网检社区 GI 动画解码器（AssetRipper streamed clip 支持 / GIMI-Blender 导入器）后离线烘 TRS；C=自研解码 resS 肌肉数据+复刻 Unity 肌肉求值；D=重建真 humanoid clip + Animator 重定向。**通则：AnimeStudio 导出的 Ani_Avatar_* clip 一律不得当完整动画使用；Ani_NPC_* 可直读。**（落地定案见 §57）

## 57. 方案B 首次 Play 验证「头发炸帆」：legacy 通道位置曲线从未做单位换算，被双通道机制每帧覆盖到正确值上（2026-09-16 实证，§56 位置失配的最终闭环）

**现象**：方案B（解码 ACL→可编辑肌肉 clip）双通道首验：Play 后**身体/腿/手臂/躯干完好未破坏**（骨架比例正常——单帧截图只能证明主体没炸，肌肉是否真正驱动身体运动需目检确认），但头发从颈部锚点炸成巨大帆状面片、棕色长条拉出画面外——只炸物理骨区域，主体完好。

**架构背景**（AmberTestPanel 双通道）：Animator+PlayableGraph 放新肌肉 clip（MuscleClips/，153 条），Animation 组件放 legacy clip（Clips/，154 条，物理骨 TRS 完整变化动画）。两通道**同时驱动同一批 42 根物理骨**。

**根因**：build_editable.cjs 建新肌肉 clip 时已正确把位置 ÷100（与 TMR 骨架本征单位吻合），**但 legacy clip 的位置曲线从未换算**（保留 AnimeStudio 导出的 GI 原始量级，×100）。Play 时 legacy Animation 求值在 Animator 之后，每帧用 ×100 位置**覆盖**掉 Animator 写入的正确值 → 物理骨甩百倍远。下半身正常=legacy POS 曲线只含头发/耳/胸/腿袋/裙摆骨，不含腿骨。

**定案证据（三源收敛）**：TMR prefab 骨架 rest（+HairB L B01 = -0.00126）≈ 新肌肉 clip POS（-0.00126）= legacy POS ÷100（-0.126→-0.00126）；且 Attack_05 legacy 全局 POS max=1.15 与新 clip max=0.0115 严格 ÷100 对应（两套独立来源交叉验证）。单位本质：TMR FBX 骨骼局部偏移 = GI 动画值的 ×0.01。

**修复**：`fix_legacy_pos.cjs`（`.codely-cli/tmp/ambor/`）批量把 154 个 legacy clip 的 m_PositionCurves 值与斜率 ×0.01（带 maxAbs<0.05 幂等跳过守卫防二次缩放；只动 Position 段，Rotation/Scale/Float 不碰）——150 文件修复，4 个 StandbyIK/WeaponStandbyIK clip 无位置曲线天然免修；git diff 逐行核验+编辑器 AnimationUtility 回读 firstKey 确认生效。

**遗留（已知未修）**：legacy clip 内仍混 ~150 条肌肉 FLT 曲线（classID 95 在 legacy Animation 通道不绑定、播放无害），内含解码垃圾值（RightFootQ.y=-5.15、LeftHand.Ring.1 Stretched=±1e+37）——新肌肉 clip 的肌肉段大概率同污（同一解码流），Unity 肌肉钳制兜底，目检时关注左手无名指即可。若后续把物理骨完整 TRS 烘进新肌肉 clip（当前只有首帧常量快照），legacy 通道可整体退役。

**通则**：双通道驱动同一批骨骼时，求值顺序=覆盖关系（legacy Animation 后于 Animator），两通道数据单位必须各自对齐目标骨架；GI 角色管线换算因子=位置 ÷100（斜率同缩），旋转/缩放不动。

## 58. Animator 组件 disabled 时自建 PlayableGraph 照常播放、人形肌肉求值静默不写骨骼——「图在播但身体零驱动」先查 enabled 位（2026-09-16 安柏肌肉通道取证定案）

**现象**：AmberTestPanel 双通道播放，头发/配饰（legacy Animation 通道）正常动，身体（Animator+肌肉 clip 通道）完全静止，连点 13 条动作全部如此、零报错。

**取证**（§39 活体诊断）：自建 PlayableGraph `IsValid=True IsPlaying=True outs=1`、clip `isHumanMotion=True`、`avatar=AmberAvatar` `ctrl=AmberTestController` 全在——一切看似正常，唯独 **`Animator.enabled=False`**；0.7s 采样：arm/spine 旋转增量 0.00°、hair 4.82°。prefab 序列化位 `m_Enabled: 0`。

**根因**：AnimationPlayableOutput 目标 Animator 处于 disabled 时，图照常评估但人形肌肉写回被静默跳过，无任何报错。2026-09-16 早前「编辑器 PlayableGraph Evaluate 肌肉写回不稳定（测试骨架可、安柏无效）」的真身即此——测试骨架的 Animator 开着、安柏 prefab 的关着，与编辑器域无关。

**修复**：prefab Animator `m_Enabled: 0→1` + 面板 Awake 自愈守卫（`!animator.enabled` 则置真）。

**通则**：Playable 驱动人形角色「图在播、骨骼零写回」时，先查 **Animator.enabled / cullingMode / avatar** 三件，再怀疑数据格式；`IsPlaying()==true` 不代表输出在生效。

## 59. 「身体零驱动」终极定案（三因叠加）：运行时肌肉求值只认 m_MuscleClip 密集流 + 导入器 autoGenerate 对非标准 T-pose 写出理想化废参照位姿——isValid/isHuman 全绿照样零输出（2026-09-16 安柏三连环收官实证）

**现象接 §58**：Animator.enabled 修好后身体依然零驱动。隔离实验（停面板+禁 legacy Animation+自建图单播）无效，armΔ 恒 0.00°。

**2×2 差分定案**（clip 形态 × rig，运行时采样）：手写 **m_MuscleClip（v11）版 Attack_05 → TestRig armΔ=48.99°（在动！）→ Amber armΔ=0.00°**；真 Unity 生成的 **ExtractedTestAction（m_FloatCurves 可编辑形态）→ TestRig armΔ=0.41°（近零）**——连亲儿子 rig 都不动，铁证：**运行时肌肉求值只读 m_MuscleClip 密集流，m_FloatCurves classID95 仅为编辑器可编辑视图**。09-16 早前「手写 m_MuscleClip 被无视」是被 disabled Animator 污染的误判——v11 形态才是运行时正解。

**Amber 接收侧根因（对照取证）**：AmberAvatar=FBX 导入器 autoGenerate 产物（meta `human:[]`+`skeleton:[]`+`autoGenerateAvatarMappingIfUnspecified:1`），ForceUpdate 重导入复现同结果——**GI 骨架的自然站立 rest 不被识别为标准 T-pose，自动装配把 desc.skeleton 写成理想化双足位姿（76/93 骨与 FBX 真实 rest 不符，UpperArm 实际 -23° 被写成恒等）**；TestRig（mixamo 标准 T-pose）捕获正确（21/21 全匹配）。**Avatar.isValid=true、isHuman=true、52 映射正确、limit 全 useDefaultValues——一切体检全绿，唯独参照位姿是废的，运行时静默零输出**。层级 ×100 缩放、双通道干扰、limit 退化三个假设全部被判别实验排除。

**一发实锤**：运行时用 FBX 真实 rest 重建 Avatar（AvatarBuilder.BuildHumanAvatar+52 映射原样拷贝+实时层级抓 SkeletonBone）挂上 Animator → **同一 v11 clip armΔ=63.30°**。

**落地修复**：①`AmberAvatarReal.asset`（BuildHumanAvatar 产物）接入 prefab Animator；②153 个 MuscleClips 同名覆盖为 v11 m_MuscleClip 形态（GUID 不动、控制器引用不断）；③双通道保留（legacy 提供物理骨完整动画）。

**通则**：①手搓人形肌肉 clip 必须写成 m_MuscleClip 密集流形态（v11），m_FloatCurves 形态编辑器看得见、运行时不动；②「导入器 autoGenerate 的 Avatar + 非标准 T-pose 骨架」= 废参照位姿陷阱——肌肉管线对不上时用「desc.skeleton vs FBX 资产 rest 逐骨比对」体检（healthy capture 应全匹配）；③判别套路=2×2 差分（换 clip 形态 × 换 rig）+运行时重建 Avatar 实验，比理论推演快十倍；④Unity 系体检（isValid/isHuman）查不出参照位姿废——API 的绿≠数据绿。

## 60. 肌肉 clip 运行时求值的真正门闩=m_ClipBindingConstant 私有哈希格式（Tuanjie 特有）——手写 YAML 与编辑器 API 双双无法构造，五路全灭（2026-09-16 安柏身体动画最终卡点定案）

**§59 勘误**：「运行时不读 m_FloatCurves」结论**半错**——NativeRef（Tuanjie 导入器生成的原生肌肉 clip 经 Instantiate 拷贝为独立 .anim=m_FloatCurves 形态）在 Amber 官方 Avatar 上 in-play armΔ=26.43°（复测稳定）→ **m_FloatCurves 形态运行时可以被求值**，前提是 clip 携带正确的绑定结构。当时 ExtractedTestAction 的 0.41° 实为该动画末段的小幅运动+姿势切换跳变（改进采样法「播放中连续变化」后区分出真驱动）。

**真门闩（SerializedObject 取证）**：①原生 clip 的 `m_ClipBindingConstant.genericBindings`=130 条、path 字段=**Integer 哈希**（非字符串路径）、attribute=负数哈希枚举（-993..-864）、customType=8——**Tuanjie 私有序列化格式**；②手写 v11 的 binding 段（标准 Unity 格式 path 字符串+attribute 1/2/4）**反序列化读出 n=0**——整段被静默丢弃 → 任何曲线（骨 TRS+肌肉）都无绑定 → 求值零输出。物理骨动画看似在播实为 legacy 通道的功劳。③`AnimationUtility.SetEditorCurve` API 重建（547 条曲线写入成功、引擎自建 253 条 binding）——**肌肉仍不求值**：SetEditorCurve 过程会清掉 m_MuscleClip 段（嫁接模板实验证实 muscleClipSize 消失）→ 丢失肌肉求值上下文。④AnimationMode.SampleAnimationClip 编辑器采样同样不求值手写肌肉曲线（编辑器/运行时同源）。⑤root 元数据（m_StartX/m_MotionStartX/m_StopX/m_AverageSpeed+dense 流 Motion/Root 列 410-423）走 m_MuscleClip 元数据通道**可以被读**（实测 head.y 随其值变化：-3.56 未修时下沉 3.3m）——单位=GI 引擎厘米，置零/÷100 修正后 head.y=1.208~1.93 正常。

**五路全灭清单**：手写 v11 m_MuscleClip（binding 读不进）→手写 m_FloatCurves（同因）→SetEditorCurve 重建（清 m_MuscleClip）→嫁接模板+API 覆写（同因）→AnimationMode 离线烘焙（编辑器也不求值手写曲线）。**唯一被求值的形态=ModelImporter 从 FBX 导入生成**（原生结构）——肌肉→TRS 的求值数学在引擎 native 侧，无法离线复刻，鸡生蛋死结。

**当前可用成果**：Amber 官方参照 Avatar（AmberAvatarReal，官方 T-pose 旋转+官方位置÷100）下，模型姿势正常（不再拧碎）、高度正常（不下沉不飞天）、legacy 通道头发/配饰动画完整——身体肌肉动画待后续路线。

**后续路线选项**：A=网检社区 GI 动画烘焙工具（Genshin Blender 插件系生态已逆向肌肉→TRS 数学，烘成 FBX 后走 ModelImporter 导入）；B=自研肌肉→TRS 数学（AnimationJob 每帧按 Avatar muscle limits 计算，52 骨×3 肌肉轴约定需逆向）；C=Bug Hunter 提交（Tuanjie 手写肌肉 clip 的私有 binding 格式无文档+SetEditorCurve 破坏 m_MuscleClip，能力失效级素材）。

**通则**：①Tuanjie 序列化格式≠标准 Unity YAML——手写资产先过「SerializedObject 读回验证」关（读出 n=0=格式被静默丢）；②「armDelta 类单点测量」必须用「播放中连续变化」采样法，姿势切换跳变会伪装成驱动；③引擎求值链=数据段×绑定段×求值上下文三件套，缺一静默零输出。

## 61. 路线A网检+「嫁接求值」终局：社区无轮子、嫁接目检失败——手搓肌肉管线正式放弃（2026-09-16 用户拍板）

**路线A网检结论**（详见 webrefs/genshin-anim-bake/ 登记）：①MonkeyAss-byte/AnimeStudio-ACL-Fix=哈希路径还原+完整 ACL 解码，肌肉值只输出 Floats 曲线、**无肌肉→TRS 求值**；②UniVRM/UniGLFT 对肌肉绑定 NotImplemented 跳过；③上游 issue #85「ZZZ FBX 导出完美」真相=ZZZ 数据本体是 TRS 轨（qvvf），GI 主体=肌肉标量不可类比；④AssetRipper 无 glTF 动画导出；⑤GI 官方 dump 的 m_Human.m_Handles 为空（无肌肉轴捷径）。**社区无现成轮子成立**。

**嫁接求值实验**（不装标准 Unity 的最后尝试）：Tuanjie 亲生成的原生 clip（TestRig|TestAction）Instantiate 拷贝=可独立播放的肌肉 clip（在 Amber 官方 Avatar 上驱动 26~62°）→ **用它当结构容器，逐条 SetEditorCurve 替换肌肉曲线值为我们解码的数据** → 运行时实证 armΔ=22°/spineΔ=7°（求值链通）——但用户目检**姿势非常糟糕**（官方参照位姿与 GI 官方肌肉空间的差异、52 映射肌肉范围用 Unity 默认值而非 GI 原生范围、以及 TestAction 容器的 root 语义残留），**放弃**。

**终局状态（可交付）**：①安柏=正常姿势站立（AmberAvatarReal 官方参照 Avatar）+头发/配饰 legacy 动画完整；②153 条解码肌肉数据（JSON）+全套陷阱知识（§56-60）留档，未来任何方案（官方工具/更强逆向）的输入；③探针实验资产已清理（MuscleClipProbe 仅留 TestRig.fbx 对照）。

**通则**：①「能驱动」≠「驱动正确」——肌肉空间的参照位姿/范围/轴向三者必须与数据原生语义对齐，错位输出的是乱舞而非报错；②视觉验收失败时，姿势级偏差与数据级错误要先分离（本例=官方 T-pose 参照 vs GI 肌肉空间的系统性错位，非数据错误）；③重度逆向任务（引擎私有格式+官方肌肉空间数学）的投入应在早期用「最小目检样张」验证可行性，而非在数据链上层层推进后再交视觉验收——本例的教训是样张出得太晚（嫁接样张早出可省 3 轮格式战争）。

## 62. 战斗世界层程序化视觉三件套：Resources.Load 路径相对最内层 Resources 根 + Quad/TMP 法线全 -Z + TMP 默认字体链（2026-09-18 HUD 补全实证）

**①Resources.Load 路径陷阱**：`Assets/TextMesh Pro/Resources/Fonts & Materials/zh-cn SDF.asset` 的 Resources 路径 = `Fonts & Materials/zh-cn SDF`——相对**最内层 Resources 文件夹**，不带外层目录前缀；写全 `TextMesh Pro/Fonts & Materials/...` 静默 null（Resources.Load 失败不报错）。判定：Resources.Load 返回 null 先查路径层级。

**②世界层朝向速查（billboard 场景）**：Unity **Quad 基元法线 = (0,0,-1)**（非 +Z）；TMP 3D 网格法线亦 -Z；billboard root 用 `LookRotation(camera.forward)` 时其 +Z 指向**远离相机**——故「root 子物体 identity 摆放即正对相机」（Quad/MeshRenderer/TMP 全适用，UnitView 血条/单位名实证）；平铺地面 = `Euler(90,0,0)`（法线 +Y，底座/高亮/选中标记既有模式）。编辑模式探法线：`mesh.normals` 取均值即可，勿凭记忆赌朝向。

**③程序化 TMP 中文字体链**：TMP Settings 的 defaultFontAsset = zh-cn SDF（TMPChineseFont skill 已配）——程序化 `TextMeshProUGUI`/世界空间 `TextMeshPro` 默认即中文安全；显式 `Resources.Load<TMP_FontAsset>("Fonts & Materials/zh-cn SDF")` 更稳（世界空间 TMP 无 UGUI 字体链兜底的场合）。程序化 UGUI TMP 与 TextCombiner 同物体 = 规范路径（docs/20 §2）。

## 63. 战斗世界层材质生命周期 + Toggle.interactable 拦不住 IPointerClickHandler（2026-09-18 代码审查实证，BattleHud）

**①运行时 new Material 泄漏（sharedMaterial 模式）**：`Destroy(GameObject)` **不销毁**运行时 `new Material(...)` 的材质（材质是独立资产，随 renderer 销毁只解除引用不释放）——曾 `ShowAimHighlights` 每格 new 一个材质、`ClearHighlights` 只销 quad，反复进出瞄准态每局累积 ~24 材质/次。修法：**同类同色件用单实例懒建缓存共享**（`GetAimHighlightMaterial()`），组件 `OnDestroy` 里 `Destroy(_xxMaterial)` 释放（BattleHud 瞄准高亮+选中盘已修）。同族提醒：UnitView 每单位 3 材质（底座/血条bg/fill）有界暂容忍——B5 表现批次建世界层 quad 工厂时一并收口（一次建、一次释放）。

**②IPointerClickHandler 不受 Toggle.interactable 拦截**：UGUI 事件执行对同物体**全部兼容 handler** 生效，`interactable=false` 只拦 Selectable 自身的内部响应——为绕"Toggle/Button 单 Selectable 限制"挂的 `SkillClickForwarder`（IPointerClickHandler）**照常收点击**，置灰防线被穿透（与 §2.2"interactable≠禁用"同族）。修法：**防线收口在处理端**——`OnSkillButtonClicked` 入口显式查 `Toggle.interactable` 不通过即 return，勿依赖 UGUI 拦截。

**③审查核验免修项（勿再误报）**：`Shader.Find("Universal Render Pipeline/Unlit")`（GUID `650dd9526735d5b46b79224bc6e94025`）**已在** GraphicsSettings→Always Included Shaders 清单内，构建剥离风险不成立（§3/§53 类问题的反例——报前先核清单）。

**④设计拍板（勿当 bug 修）**：`GetSelectedSkillData` 尾部 `?? skills.FirstOrDefault()` fallback 语义**保留**——3004 号角色设计即无战技、主要靠移动（2026-09-18 用户拍板），无对应类型技能时技能盘按钮显示首个技能是**预期行为**，勿"修复"成隐藏/置灰。

**⑤统一化批次落地（2026-09-18 同日晚，审查债务 #3-#8 一次收口）**：战斗表现层统一件三件套——**BattlePalette**（`Data/Battle/BattlePalette.cs`+`Resources/Configs/BattlePalette.asset`，ConfigManager [Bean]+PostConstruct 注入，ElementFactionConfig.Instance 同款；配色字面量 BattleHud/BattlePlayer/UnitView 三文件收口，队伍色=底座/队列框/accent 同源）；**BattleViewFactory**（`Battle/View/`，世界 quad+Unlit 材质+世界 TMP 字体的唯一出口——材质生命周期归调用方持有+OnDestroy 释放；Shader.Find 收口一处）；**BattleViewTween**（elapsed-while 手写循环收口，末帧保证 t=1）。按钮建法 4→3（取消钮并入 MakeActionButton，labelCentered 参数；统一按压反馈补到设置/取消钮；Skill.prefab+SkillClickForwarder 保留=Toggle/Button 单 Selectable 限制的必要绕行）。**TextMesh 全数退役**（伤害数字/Buff 回合角标→世界 TMP；换算 `世界高≈fontSize×scale×0.1`，scale=原 characterSize 保等高）。可见变化三处：队列框/accent/取消钮敌红 (0.78,0.36,0.31)→(1,0.35,0.3) 对齐底座队伍色；数字字形 Arial→zh-cn SDF；设置/取消钮有 hover/press 反馈。**追加（同日用户拍板"技能按钮应当统一，包括移动——移动是特殊的技能"）**：移动按钮并入 Skill.prefab 建法（四键全同构，事件通道统一走 SkillClickForwarder）；数据链 skills[Move]（UnitConfig 各角色 Move 条目 skillID=Common_Walk/icon=walk.png 均已配，无技能角色跳过染色不隐藏）；图标+名称随 normalMoveType 数据驱动（walk/fly/amphibious→Common_Walk/Fly/Amphibious 的 SkillName 表现成键，图标=InitWithData 后覆盖为对应现成图）；移动瞄准选中环接入 SetAimSelectRing（EnterAiming(Move,"move")）。可见变化：移动钮从暗底盘变角色元素色底+主动橙环（与技能盘同款），名称从恒「移动」变随单位「步行/飞行/两栖」。

## 64. 战技/爆发/延奏点击「无反应」=详情面板开而不渲染——跨场景复用拉伸锚 prefab 的点锚化陷阱三连（2026-09-18 活体实证）

**症状**：战技/爆发/延奏点击无反应（移动正常——移动不走详情面板）；反射直调 OnSkillButtonClicked 全通、真点击链（RaycastAll+ExecuteHierarchy）也全通、面板 activeSelf=True——**一切逻辑正常但视觉零反馈**。

**根因三层（BattleHud.BuildSkillPopup 自 HUD v1 起潜伏）**：
①**skillDetailPanel 字段引用的就是 prefab 根本身**（非子物体），其原锚=全拉伸 (0,0)-(1,1)+负 sizeDelta 边距 (-1807.67,0)（背包左侧全高栏设计）——只点锚化 (1,0.5) 不落尺寸 → **负宽零高**（活体实测 rect=(-1807.67,0)），面板 SetActive(true)/滑入动画照跑但 CanvasRenderer 不出 mesh=「开而不渲染」。
②**RelatedPanel 是面板本体的子物体**（锚参照=面板 rect 非画布——由活体落点反推实锤），同病：y 拉伸负边距点锚化后高 -337、位超画布右缘。
③**设计宽实时捕获竞态**：BuildUi 时 CanvasScaler 尚未应用，canvasRect.rect.width=裸屏宽（实测 3174）→ designWidth=1366 超设计值；修法=以 `scaler.referenceResolution.x`（2560）为捕获基准（拉伸语义宽=参考宽+sizeDelta.x≈752）。

**修法**（BuildSkillPopup 重写）：点锚化前按拉伸语义捕获设计宽→显式落 `sizeDelta=(designWidth, 详情面板高度[新 SerializeField=900])`；关联面板挂**面板左缘锚** (0,0.5)+pivot(1,0.5) 向左展开（勿按画布参照系摆位）；滑入目标经新 API `SkillDetailView.RepositionRelatedPanel` 重定（否则滑向 prefab 原场景接线值）。

**取证三教训**：
a) **ScreenSpaceOverlay 画布的世界坐标=屏幕像素**，画布局部=(世界−画布中心)/scaleFactor——直接除 scale 会把正确的 (608,0) 误算成「落点超画布」；
b) **活体取证防选择阶段 25s 超时污染**——超时自动 Pass→DeselectUnit 隐藏技能盘→射线「零命中」/activeInHierarchy=False 全是污染样本（§39 排障时序：SelectUnit 后**立刻**取证，勿跨多轮工具往返）；
c) 静默 return 链全通+真点击链全通时，转向**视觉层**查「开而不渲染」：dump RectTransform（负宽/零高=锚点体系错配的指纹）。

## 64b. 「面板开着再点同键」永远进不了瞄准——SkillDetailView 点外关闭与 UGUI 点击派发的同帧竞态（2026-09-18 活体实锤）

**症状**：面板开着再点同一技能按钮，面板闪一下又开着（永远走"重开"分支，进不了瞄准）；点面板外的棋盘空白也会先被抢关（OnBoardTap 读到 PopupOpen=false，误走"取消选中"分支）。

**根因**：SkillDetailView.Update 的点外关闭用 `Input.GetMouseButtonDown(0)` 在**按下帧**判定"指针在面板外"立即 `ClosePanel()`——而 UGUI 的 PointerClick 在**抬起帧**才派发给点击目标（按钮转发件）。时序：按下帧关面板 → 抬起帧 OnSkillButtonClicked 读到 PopupOpen=false → 永远走 ShowSkillPopup 重开。背包场景同款竞态一直存在（点源图标=面板闪一下重开），从未被注意。

**曾试方案（勿再走）**：pendingOutsideClose 延迟一帧关闭——抬起帧若 SkillDetailView.Update 先于 EventSystem 执行，仍会在按钮点击派发前关面板，**竞态只是换了个位置**，依赖脚本执行顺序（未设 Script Execution Order）不可靠。

**终案（零竞态）**：SkillDetailView 加 `public bool 点外关闭 = true`（默认开保背包原行为），战斗 HUD 实例化时设 **false**——按钮点击与面板自动关闭彻底解耦；战斗的"点外收面板"由 BattleHud.OnBoardTap 已有分支承接（点棋盘=收面板保持选中）。**判定原则：同一交互目标（按钮）同时被两个系统响应（自动关闭+按钮点击）时，必须一方显式让位，勿依赖帧内执行顺序。**

**顺带统一（2026-09-18 用户拍板"移动按钮也不应当直接瞄准"）**：OnMoveButtonClicked 删除，四键（移动/战技/爆发/延奏）全走 OnSkillButtonClicked 点击式三情况（①开面板②再点同键进瞄准③换点切内容），移动唯一差异=AimMode（瞄准按移动语义结算）；GetSelectedSkillData 补 move 分拣（否则移动面板会显示战技数据）；瞄准态点任何按钮=无操作（退出走取消钮/点非可选格）。

**结构收敛 A+B（2026-09-18 深夜，质量评估后拍板）**：①**表驱动四键（A）**——`SkillButtonDef{key,type,rect,view,nameText}`+`RegisterSkillButton` 注册（`BuildSkillButtons` 里加键=加一行），全类 buttonKey 字符串 switch 清零、转发闭包直捕 def 零查找、**AimMode 枚举删除**（瞄准语义由 `def.type==Move` 承载）、置灰语义泛化全键（原延奏特例）；加键成本从"改 8 处"降为"加 1 行"。②**partial 拆分（B）**——BattleHud.cs 主（状态机/技能按钮交互/数据链/高亮）+ BattleHud.TopBar.cs（顶栏/队列/信息块）+ BattleHud.Build.cs（程序化构建/按钮注册/面板接线），修改热区按职责归位。受控复现基线逐项一致（战技 22 格/移动 24 格/面板开合/瞄准链/取消钮全同）。

## 65. 手势面要点击必须显式传 `emitShortTap: true`——DragRecognizer 短点击发射是构造参、默认 false（2026-09-18 战斗 HUD 点立牌无反应实证，2026-09-19 补录）

**现象**：Battle 面接入手势层后点立牌无反应（HUD OnBoardTap 点击链全不触发）；同构的 Map 面正常。

**根因**：`public DragRecognizer(DragBeginMode mode, bool emitShortTap = false)`——「抬起时总位移 < slop 伴发点击」（libGDX/Android tap+pan 同体模式）是**可选构造参，默认关闭**；Immediate 拖拽面不显式传 `emitShortTap: true` 就永远不发短点击，无报错无日志。

**规范**：任何手势面需要"点一下"语义（格点点击/单位选择/点击继续），构造识别器时必须显式传 `emitShortTap: true`（`BattleCameraController`/`MapCameraController` 均已带且留有注释指针，新增面照传）。短点击两通道分工：Immediate 面走 `OnShortTap`、升级式早退走 `OnTapCandidate`（docs/24 §7.11 勘定）。与 §63② 同族教训：能力位默认静默关闭，症状=整条点击链零触发。

## 66. 编辑器脚本 Roslyn 批量语义分析四坑 + 模板 using 全量清理成果（2026-09-19 战斗审查修复批次实证）

**场景**：用 exec_editor_script 挂 Roslyn 做全项目"未用 using"清理（_Scripts 361 文件三轮共删 291 文件 867 条，逐轮 refresh 编译 0 错）。桥脚本宿主的 Roslyn 有四坑：

1. **命名空间是桥内部化前缀**：脚本里 `using Microsoft.CodeAnalysis.*` 必全 CS0246——桥包把 Roslyn/Newtonsoft 内部化为 `Codely.Microsoft.CodeAnalysis.*`（栈帧里 ScriptOptions 类型名即证）。写 `Codely.Microsoft.CodeAnalysis.CSharp` 等前缀版本即可用全套 API。
2. **API 表面差异**：内部化 `CSharpCompilationOptions` 无 `allowUnsafeEnabled` 命名参（构造只收 OutputKind）；`Compilation.GetDiagnostics(tree)` 不可用（重载只收 CancellationToken）——逐树诊断用 `compilation.GetSemanticModel(tree).GetDiagnostics()`。
3. **手解码假门**：`new UTF8Encoding(false).GetString(bytes)` 自解全部文件时，逐树诊断报出 241 文件假编译错（同源码改用 `File.ReadAllText` 全量解析=0 错、五文件抽检全 0 错）；根因未完全实锤但差异仅在解码路径。规范：编辑器脚本批量语义分析**一律 File.ReadAllText**（自动编码检测）+ `\0`/`\uFFFD` 双门跳过异常文件。
4. **写回保真**：删除行前 BOM 字节嗅探（EF BB BF）决定 `UTF8Encoding(bom)` 与否；按文件实测保 LF/CRLF（混合换行整只跳过）；git autocrlf 对 LF 工作副本告警"LF will be replaced by CRLF"属常态无害。

**清理安全网**（可复用套路）：①语义分析只删"行文本 Trim 后完全等于 using 指令"的行；②诊断门=逐树 0 错才动文件（CS0433/CS0104 Newtonsoft 歧义族豁免，歧义符号走 CandidateSymbols 双命名空间保守标记）；③含玩家侧 `#if`（!UNITY_EDITOR/UNITY_STANDALONE_WIN/DEVELOPMENT_BUILD）与混合换行文件整只豁免；④每轮后 refresh 编译验证（Unity 真编译器是最终裁判）。

**成果**：`using GIC.X` 逐文件 grep **恢复可用作依赖方向审计**——Battle 层 using GIC.UI 58→7（全真实：Card 显示策略族 5 + BattleHud 复用 SkillDetailView 族 2）；Data 层 using GIC.Battle 41→5；Framework 33→3；Tool 9→0。**17 个豁免文件仍带模板头**（玩家侧 #if×16 + CardGlowOverlay.cs 混合换行），审计到它们仍须核类型实际使用。**顺带实锤 Data→Battle 真反向边**（比人工审查更准）：`Direction2D`（ActionData）/`ForceType`（BattleMapData/UnitConfig）/`TeamType`（PlayerInfo/PlayerNetworkEvents）三个值类型放错层（住 Battle/Unit/Component 但属协议词汇）——B3 归位材料（挪 Data/Battle+改命名空间，Battle 引用方为合法方向）。

## 67. GI 动画目检场景黑屏三因 + 编辑器 SMR bounds 塌缩误判误删 + TMR 嫁接单位失配柱子（2026-09-19 安柏动画验证批次实证）

**场景**：AnimLookTest 目检场景验收 harness 导出的 GI 动画 FBX（安柏 1.0 老角色 vs Odette 6.x 新角色，白模+Animation 自动循环播放）。

1. **黑屏三因**（一次排障逐个撞上）：
   - `EditorSceneManager.NewScene(DefaultGameObjects)` 创建的场景 **lights=0**（URP 下无灯+无环境光=纯黑）——新场景一律显式补 Directional Light；
   - SMR 未开 `updateWhenOffscreen` → 蒙皮网格被视锥剔除误杀（§55 纯黑坑同族）；
   - 外部 FBX 实例的材质若是 Built-in shader → URP 下渲染异常——一律换 URP Lit 白模材质。

2. **编辑器非 Play 状态 `smr.bounds`/骨骼 transform 塌缩 ≠ 模型坏**（误判误删实证）：SMR 蒙皮网格由 bindpose 烘焙渲染，编辑器下读 `smr.localBounds`/`bones[i].position` 得到接近零的塌缩值（worldBounds size 0.02 级），但**渲染人形完全正确**（用户选中轮廓确认）——按编辑器读数判"骨架塌缩"并删实例是**误判**，Play 后一切正常。规范：模型好坏以渲染/用户目检为准，编辑器 bounds 读数只作参考。

3. **TMR 模型 + harness clip 嫁接 = 柱子**：harness 导出的 clip 位置曲线是 GI 骨架**厘米制**、TMR 骨架**米制**（§56 已知坑），跨源嫁接播放时物理骨被甩约百倍远、蒙皮拉成柱状。规范：**harness/AnimeStudio 产出的动画只能在同源 FBX 白模上目检**（FBX 内网格+骨架+动画单位自洽）。

**成果定案**：安柏（1.0 代）=物理骨（头发/裙摆/腿带）摆动正常 + 主体（躯干/手臂/重心）完全静止——老角色 muscle binding 丢弃的目检级实锤；Odette（6.x 代）=衣服等摆动正常、人形正确——新角色全链路可用实锤。代差与管线细节=.codely-cli/webrefs/gi-animation-extraction/README.md。