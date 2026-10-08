# 技术陷阱与 Bug 修复记录

> **定位**：症状 → 根因 → 规范的可检索事故库。分流：操作工作流 → skill、技术陷阱 → 本文、设计决策 → 各设计文档、通用规则 → docs/20。
>
> **编号规则**：`## 数字.` 节号 = 永久 ID——代码注释、docs/13/15/19/20、skill、项目记忆均按节号引用，**永不重排、永不复用**。正文按主题分组物理排列，分组标题字母序 ≠ 节号序；新增陷阱取当前最大节号 +1，插入所属分组末尾。
>
> **条目结构**：现象 / 根因 / 修复与规范；与 skill 分工=操作流程在 skill，本文记陷阱本体与规范。

## 分类索引

| 分组 | 节 |
|------|-----|
| A · UI 与 UGUI（粒子 / 九切片 / 程序化 UI / 设置页 / 面板化 / 拖拽 / 点击） | §2 §4 §14（14.2–14.4）§22 §26 §27 §37 §38 §39–§50 §54 §62–§65 §70 §75 §82 §84 §85 §87 §88 §96 §97 §103 §127–§130 §132 |
| B · 文本 · TMP · 本地化 · DI | §5 §6 §18 §48 §72 §83 |
| C · 音频 | §10 §11 §53 |
| D · 渲染 · Shader · 大地图图形 | §8 §13 §89 §92–§95 §98 §108 §109 §111 §112 §123 |
| E · 网络与异步回调 | §15 §19 |
| F · 构建与平台差异（编辑器 vs 真机 / PC vs 移动） | §1 §3 §20 §21 §12 §51 §52 §99 |
| G · 编辑器工具与资产管线 | §7 §9 §23 §24 §25 §49 §55–§61 §66–§68 §71 §73 §74 §90 §91 §100–§102 §104 §113 §118–§122 §124 |
| H · 桌宠 · Win32 · 双进程 | §16a §16 §17 §36 |
| I · 战斗系统判定与 AI（2026-09-21 起新增，未归位） | §69 §76–§81 §86 §105–§107 §110 §114–§117 §125 §126 §131 |

## 症状速查

| 症状 | 节 |
|------|-----|
| 编辑器正常，导出/真机才异常 | §1 §3.1 §6.3 §20 §21 §51 §52 |
| UI 元素不可见（文字正常） | §14.2 §2.2 §127 §130 |
| 图片/画面被拉伸变形、压扁 | §21 §26 §14.2 §92 |
| 点了没反应 / 选项假死 | §22 §2.2 §27 §54 §64 §65 §82 §86 §87 §103 §128② |
| 音乐断头 / 永久静音 / 某区域偏小 | §10 §11 §53 |
| NRE 刷屏 | §14.3 §14.4 §16a §6.1 §49 §71 |
| 编辑器卡死 / 导入极慢 | §9 §8.2③ §90 |
| Shader 解析报错（引号/中文） | §13 §122 |
| 场景 git diff 出现锚点类序列化噪声 | §23 |
| 设置里有值但运行时读不到 | §16 §16a §74 §101 §104 |
| 多方共用回调互相顶掉 | §19 |
| 按钮全消失 / 面板幽灵 | §39 §50 |
| 拖拽松手误触点击 / 拖拽布局坍缩 | §103 §129 |
| Mask 下全部子级隐形 | §127 |
| 自定义 shader 材质渲染不上屏 | §130 |
| 编译炸（插值字符串/匿名委托） | §48 §51 |
| 构建被静默吃掉 / 包版本剔包 | §52 §99 |
| 肌肉动画零驱动 / 身体不动 | §58–§61 §68 |
| 投射物/命中判定异常 | §69 §76 §80 §86 §106 §107 |
| AI 移动偏航 / 驻位早退 | §110 §114 §116 |
| 冻结/霜化视觉异常 | §108 §109 §111 §112 |
| 双指缩放失效 | §117 |
| 编辑器预览与运行时错位 | §118 §120 §122 |
| 首用黑屏（视频解码器） | §121 |
| 透明排序幻觉（renderQueue） | §123 |
| 协程钉 Y 轴漂移 | §126 |

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

**规则**：需要描边/特殊配色的 TMP 文本 = 同字体 + 变体材质（放 `TextMesh Pro/Resources/Fonts & Materials/`，该目录 git 忽略，改材质不入库，重装环境需手动备份）。另注意 rg/搜索工具默认跳过 git 忽略目录，排查 TextMesh Pro/ 下资产时需加 `--no-ignore`；Codely 搜索工具（search_file_content/glob/list_directory）ignore 层=.codelyignore**加**.gitignore 双层（.codelyignore 另吞 Assets 的 png/prefab/asset/mat/wav 与 Mirror/kcp2k/Plugins 整目录）——被任一层命中即**静默零命中/无名**，下"查无引用/文件不存在/skill 不存在"类结论前一律先想 ignore 层，改走原生 `rg --no-ignore` 或 Get-ChildItem；analyze_multimedia 对被忽略路径同样拒读（Assets 图片先复制到 .codely-cli/tmp 再传）。另：**prefab 中文序列化字段名以大写 \uXXXX 转义存储**（如 GlassPanelAnimator 的模糊层字段=`\u6A21\u7CCA\u5C42`）——rg 直搜中文字面与小写 `\u6a21` 形式均零命中，文本反查 prefab 接线时把 .cs 字段名转成大写 \uXXXX 再 `rg -F --no-ignore` 搜；场景 YAML 中文同理（docs/20 §1.3）。

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

**2026-09-21 泵时序重排（方案 B）**：开泵条件自"MainHall 就绪+60 帧"改为"Splash 就绪+30 帧"——Instantiate 摊进启动动画期，进大厅即全暖（进厅 3 秒内开面板不再吃冷开尖峰）；泵未完遇根转场挂起（SceneTransition 锁检测，Single 加载/预载激活/Additive 根切换全路径覆盖、失败路径不漏 Pop），转场毕再顺延 60 帧避开大厅入场动画头；渲染态两帧窗补 blocksRaycasts=false（CanvasGroup alpha=0 **仍拦截射线**——UGUI 陷阱，隐形面板可能吃掉 Splash 的跳过点击）；桌宠形态直载 PaimonPet 等不到 Splash=泵挂起，与旧版等 MainHall 同款安全。

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
- **2026-10-06 追拍「所有期望显示但实际缺少时都渲染此图代替」=全内容位点接入**——已接清单：技能图标（SkillIconView.InitWithData 中央单点，含战斗键/背包/详情）+移动键图标（BattleHud.ApplyMoveButton）+立牌纸片人（UnitView.Create——Ensure 经 bounds 换算拉伸到标准立牌高）+Buff 徽章（UnitView.RebuildBuffBadges——未知类型照常显示占位徽章）+附着图标（BattleOverheadBars）+头像牌（执行预览 CreateEntry/拖动瞄准头像盘 BuildDragAvatarGrid）+卡面（UnitCardViewStrategy 立绘既有/ItemCardViewStrategy 物品图/CardViewStrategyBase.ApplySkinTo 皮肤切换）+物品详情（ItemDetailPanel）+物品计数条（ItemCounterChip.InitItem——空图标不再隐藏图标位）+祈愿角色面板元素/势力图标（CharacterPanelController）。**不经守卫**：程序化 UI 镀层（拖动盘贴图/弧光贴图/光柱/聊天气泡=自带降级链）+既有专属降级（箭矢素材缺失=白光条 Warn，勿当 bug 修）。**新内容图显示点接入纪律**：赋值处走 Assign/Ensure（勿裸 `.sprite=`）——全局扫描方案仍是禁区

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

**⑥Mesh 生命周期补遗（2026-09-28 批6 复审收口，①的同族盲区）**：`Destroy(GameObject)` 同样**不销毁**运行时 `new Mesh()`（①只写了材质，mesh 是同型独立资产——Destroy 物体只解除引用不释放）。批6 实证三处：BattleViewFactory.CreateDisc 原每单位 new 一个同构 mesh（8~34 份/局）+BattleBoard 水面焊接面/BattleGrassDecor 草簇层每建盘 new mesh，全无持有者释放、全靠场景卸载自动回收兜底（泄漏窗口=对局期间）。修法两型：**同构 mesh=工厂静态共享单实例**（disc 收口——共享根治免释放，同 ProjectileSprite/AimCellTexture 缓存模式）；**每盘独有 mesh=创建方登记显式释放**（水面 mesh 登记 BattleBoard._runtimeMeshes、Build 重建/OnDestroy 销毁；草簇 mesh 挂组件 OnDestroy 销毁）。**规则：运行时 new 出的原生资产（Material/Mesh/Texture/RenderTexture）必须有持有者——共享缓存或显式 Destroy 二选一，勿依赖场景卸载自动回收。**

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

**成果定案**：安柏（1.0 代）=物理骨（头发/裙摆/腿带）摆动正常 + 主体（躯干/手臂/重心）完全静止——老角色 muscle binding 丢弃的目检级实锤；Odette（6.x 代）=衣服等摆动正常、人形正确——新角色全链路可用实锤。代差与管线细节=.codely-cli/webrefs/gi-animation-extraction/README.md。**（2026-09-19 深夜修正：本行"muscle binding 丢弃"系误诊——身体动画根本不在该 clip（在共享基础 clip），且当时看到的"只有物理骨"部分受 §68 的 DBACL streamer 缺陷影响；真相与全链路见 §68。）**

## 68. GI 老角色身体动画「三重误诊」终局：身体动画在共享基础 clip（不在角色 clip）+ AnimeStudio DBACL `streamer=NULL` 丢 database 精修（真根因）+ m_IndexArray 通道映射其实全对——muscle 烘焙全链路打通（2026-09-19 深夜实证）

**背景**：§56-61 五路全灭后重启信源（§67），交接蓝图 Job16 自研 muscle 烘焙。本轮三个关键事实逐一实证，全部推翻此前假设：

1. **身体动画不在角色专属 clip**——`Ani_Avatar_Girl_Bow_Ambor_Standby` 的 ACL=28 物理骨×(Q4+T3+S3)+7 个空 Motion 槽（m_IndexArray 前 7 指 280..286，其余 193 全 -1），**压根没有 muscle 数据**。身体动画在**共享基础 clip** `Ani_Avatar_Girl_Standby`（全员女孩共用）：200 通道布局（Motion7+Root7+Limbs28+Muscles55+Fingers40+TDoF63），ia[0..136]→ACL 170..306 全有真数据，TDOF 仅 LeftHand.z/RightHand.z（307/308）。游戏按「共享身体层 + 角色物理层」分 clip 合成。**给老角色找身体动画先找 `Ani_Avatar_<BodyType>_*` 共享 clip，别在角色 clip 里挖。**

2. **AnimeStudio DBACL 的 database 分流从未生效（真根因）**——C# 包装 `DBACL.DecompressTracks(data, db, out, out)` 内部 `streamer = IntPtr.Zero`（源码注释自认 *"m_databaseData doesn't seem to be used. For now"*）。C++ 端 `debug_database_streamer::is_initialized() = (size==0 || ptr!=nullptr)`——低档 bulk 7106 字节 + NULL 指针 → database_context 初始化失败 → 静默退化为 tier-0 粗解压：**Root/Motion 曲线读成全零**（精修样本全在 DB bulk）、**muscle 值呈 2-3bit 量化混沌抖动**（帧间 ±0.3 跳变，曾被误读为"布局错乱"）。修复=harness 直接 P/Invoke，`streamer = dbAligned + bulkOffset`；bulk 偏移=DB 头布局推算：`raw(8)+dbheader(56)+chunkdesc(8×(nChunks0+nChunks1))+clipmeta(8×numClips)`。修复后 RootT.y 动画 0.571→0.945 与 CrouchToStandby 的 vad 起止值**逐位精确互证**。GI 的 DB=tag `0xAC11DB01`、bulk inline、`bulk_data_offset=-1`（mihoyo 不用它，须按布局推算）。

3. **m_IndexArray 通道映射从头到尾是对的**——`ia[ch]=ACL 标量轨道索引`、`-1=无数据`；binding 表 Animator 条目的 `attribute` 即通道号。此前"数值跨通道复现/混沌"全是缺陷 2 的伪影。`m_ValueArrayDelta[0..13]=[片头值,片尾值]`（Motion+Root 通道），是天然的逐 clip 对账锚点。

**muscle 求值数学**（axes 在 `Human.m_Skeleton` 的 AxesArray——**GI 的 AvatarSkeleton.m_AxesArray 是空的（axesCount=0），必须去 Human 骨架取**，48 节点 46 组 preQ/postQ/sgn/min/max，min/max 为弧度）：
- `angle_d = sgn_d × m_d × (m_d≥0 ? max_d : -min_d)`，`q = preQ ⊗ SwingTwist ⊗ conj(postQ)`；
- `SwingTwist(ax,ay,az) = normalize(tx, ty+tx·tz, tz-tx·ty, 1)`，`t?=tan(angle?/2)`（Unity 半角正切合成）；
- 通道→(骨,axis) 表=Ruri.RipperHook MuscleDofTable 同构（FrontBack→axis2 等，勿按通道序硬排）；手指经 `Human.m_LeftHand/m_RightHand.m_HandBoneIndex`（15/手，近/中/远三连）。
- FK 验证链：参照根位+肌肉求值 → 头 1.36m、手垂身侧（y≈0.87）、双脚落地、逐帧微摆 ✓。

**一手信源**（网检铁律产物）：`ZM-Kimu/Blue-Archive-Asset-Downloader` 的 `HumanoidAnimationBaker.cs`（求值数学+twist 分配）、`FractalTools/Ruri.RipperHook` 的 `AvatarMuscleReferential.cs/MuscleBone.cs`（同数学+Root 质心补偿）、AnimeStudio 自带 `MuscleHelper.cs`（200 通道布局权威表）、DBACL 源码 `AnimeStudio.ACL/AnimeStudio.ACL.DB/dllmain.cpp`。

**烘焙产物与验证**：AnimHarness `bake` 模式 → 单 FBX 双 clip（body：17 TRS 轨+45 muscle 轨=59 骨骼路径 278 绑定；phys：28 物理骨 TRS）→ Tuanjie 导入 **body-bone 路径 0→58**（此前旧 FBX=0）→ AnimLookTest 场景 x=0 新实例（Animation 自动循环 body，phys 挂 layer1）。同骨冲突时 TRS 区优先于 muscle 求值（手指/扭转骨走 TRS）。

**遗留**：①twist 重分配未做（armTwist=1/foreArmTwist=0.35——BA 与 Ruri 对"=1"的语义相反，idle 的 twist 值小，待目检判定是否需要）；②Root/Motion→hips 质心补偿（Ruri BodyTransform）未做——idle 用参照根位足够，locomotion clip 需补；③上游 issue #124 的前提（"binding 丢弃"）已被本轮证伪，追评/关闭待拍板。

## 69. 复用十字归一映射当 8 向用——SkillHitResolver.DirectionToDelta 斜向被归一到主轴（2026-09-21 撞墙弹回方向变十字实证）

**症状**：斜向移动被挡的撞墙弹回，探出/弹回方向却是十字正交方向（凯亚斜走水面，用户目检报障）。

**根因**：`SkillHitResolver.DirectionToDelta` 注释明写「十字方向→格增量（斜向输入归一到主轴；投射物=十字方向其一）」——Right/UpRight/DownRight 共用返回 (1,0)、Left/UpLeft/DownLeft 共用 (-1,0)，是直线投射物技能的专用归一映射。撞墙弹回首版误复用它换算 Direction2D（8 向），斜向全被吞成主轴。

**修法**：`MovementResolver.StepVector`（8 向步进权威映射，Host 移动结算同一份）转 public，View 播放同源复用——「Host 判定与播放同源」与 BattleMetrics 同哲学；弹回幅度=未归一步向量×比例（斜向朝下一格格心等比例 45%，与移动插值一步一整格口径一致）。

**How to apply**：Direction2D→格增量换算按场景选映射——8 向移动/步进语义一律 `MovementResolver.StepVector`；只有直线投射物（HUD 提交前已 SnapToCardinal 十字归一）才用 `DirectionToDelta`；新增换算点先读目标函数注释语义，勿按方法名就近取用。

## 70. 程序化 UGUI 三连坑：单点锚+默认中心 pivot 的 anchoredPosition 语义、纯 Button 无 Graphic 点击不可达、复用现成卡 prefab 勿 stretch 变形（2026-09-22 B6c 手牌四轮目检实证）

**症状**（手牌区程序化构建，同批连续四报）：①卡排只露一点、大半沉到屏幕下方；②卡被纵向压扁；③无法左右滑动（含"1 张卡也要能滑"的产品要求）；④卡排不居中贴视口左侧。另有隐藏 bug：wrapper 纯 Button 无 Graphic，点击实际不可达（用户目检停在外观阶段未暴露）。

**根因**：①单点锚 (0.5,0) 配**默认中心 pivot** 时，`anchoredPosition.y` 语义=rect **中心**到锚点距离，而非直觉的"底边到锚点"——212 高的卡中心被压在锚点上，大半沉出屏（活体取证 GetWorldCorners 实锤卡 minY=-188）；②卡被 stretch 到 158×212 而 Card.prefab 原生 160×240（2:3），比例变形；③ScrollRect 用 Clamped 且 content 宽在"不溢出"分支被夹成=视口宽→**零滚程、拖不动**；且产品语义要"任何卡数可拖"=必须 Elastic 弹性回弹+content 恒=行宽+边距（窄于视口也有拖程）；④content anchor=(0,1) 左上→窄于视口时整体贴左。隐藏 bug：UGUI 点击需 raycast 目标——"卡内 raycast 全关防拦截"后 wrapper 自身无任何 Graphic=按不到。

**修法**：①wrapper/container 一律显式设 pivot（底/顶边中点），y 偏移语义对齐；②复用现成卡 prefab 保持原生 rect（居中锚+原生 sizeDelta），布局容器包原生尺寸；③ScrollRect.movementType=Elastic + content 宽恒=行宽+左右边距（勿夹视口宽）+ content anchor/pivot=中上（窄于视口初始居中、Elastic 回弹归位居中；宽于视口滚动/clamp 基于 bounds 与锚点无关照常滚）；④wrapper 加 alpha=0 的 Image（raycastTarget=true）+ Button.targetGraphic 指向它。

**取证教训**：UI 排位类报障先跑 exec_runtime_script 拿 RectTransform 链的 GetWorldCorners 世界坐标算占屏比（本批两次立功：沉屏与后续验证），勿对着代码空推锚点数学；"点击全死"类问题查 raycast 靶链（无任何 Graphic 的容器=黑洞）。

**How to apply**：程序化建 UGUI 容器必带三件套自查——pivot 与语义对齐、复用 prefab 不变形、ScrollRect 要可拖（Elastic+content 恒不等视口宽）；命中层=透明 Image 显式挂。

## 71. 私有嵌套类 MonoBehaviour 烘焙进 prefab = 域重载后 missing script（静默潜伏且阻断后续 prefab 保存，2026-09-23 实证）

**症状**：`PrefabUtility.SaveAsPrefabAsset`（LoadPrefabContents 路线）保存 BattleHud.prefab 被拒——"You are trying to save a Prefab with a missing script…GameObject 'burst_SkillIcon'（×4 技能图标节点）"。但运行时 HUD 完全正常（图标/按钮全在），且资产此前可正常保存过。

**根因**：`SkillClickForwarder` 定义为 BattleHud 的**私有嵌套类** MonoBehaviour（`private class SkillClickForwarder : MonoBehaviour`），2026-09-22 迁移工具从运行时实例烘焙 prefab 时把它一并烤了进去（工具只剥了 LayoutDragHandler，漏剥这件）。嵌套类 MonoBehaviour 的 m_Script fileID 非 11500000 而是 hash 计算值——**同会话内可能解析、跨域重载后断链成 missing script**。运行时零症状是因为 `ResolveSkillButtons` 每实例 `GetComponent==null → AddComponent` 重挂（设计本意=运行时件，委托不序列化），断链件变成死槽；但 Unity 在下次 SaveAsPrefabAsset 时校验拒绝。

**修法**：`GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go)` 官方 API 剥除缺失槽（配 `GetMonoBehavioursWithMissingScriptCount` 复查=0 再存）；剥后保存校验全过（2026-09-23：剥 4 槽+烘手牌壳一次通过）。

**教训**：①**私有嵌套类 MonoBehaviour 一律不得烘焙进 prefab**——要么顶层类，要么迁移工具确保剥除全部运行时 AddComponent 件（校验清单别只列已知场景引用件，按"运行时 AddComponent 的全部类型"核对）；②诊断 missing script 勿信运行时正常就跳过——`GetMonoBehavioursWithMissingScriptCount` 在资产层与 LoadPrefabContents 层各扫一遍，prefab 保存被拒时先查这个；③需要转发件/桥接件时优先顶层类文件（Unity fileID 稳定），嵌套写法只在纯运行时场景安全。

## 72. Localization 表集合主资产 {表名}.asset 误判"空壳表"险遭删除——容器无条目、条目在 Shared Data（2026-09-23 两轮审查连环误判实证）

**症状**：全量审查与复审两轮把 `Assets/Localization/UIText.asset`（943 字节）定性为"0 条目空壳表，建议删除"（复审时用户追问"是什么/能否安全删除"才逼出完整核验，撤回前差一步执行删除）。

**根因**：Unity Localization 的 StringTableCollection 主资产（`m_Group: String Table`）**天然是小容器**——本体只持 `m_SharedTableData` + `m_Tables`（各语言表）两组引用，条目不存在它身上，943 字节是正常体积。条目真落点=`{表名} Shared Data.asset`（键注册表，UIText 实存 247 键：通用 1000 段+战斗 12000 段）与 `{表名}_{locale}.asset` 语言表（战斗键 12028 在 UIText_zh-Hans L1000 实存）。只看主资产体积/内容判"空壳"→ 若照做=全项目 UI 文本五语言全灭（TextCombiner 全部取键失败）。且 gic-localization skill 陷阱节第 3 条早记载"{表名}.asset=表清单序列化落点"，审查结论与 skill 既有知识矛盾却未交叉验证。

**验证法**：判一张 Localization 表是否为空，必须：①数 Shared Data 条目——`rg -c --no-ignore -F 'm_Id:' 'Assets/Localization/{表名} Shared Data.asset'`（零才是空）；②抽一个已知键验语言表实存（如 `rg -n -F '12028' 'UIText_zh-Hans.asset'`）；③删除类建议加 GUID 反查双验（collection 主资产按表名经 LocalizationSettings/Addressables 寻址，GUID 零外部引用≠无用，勿以引用计数判活）。

**教训**：①collection 主资产、场景、prefab 的"体积小/内容少"都不是判废依据——先搞清该资产类型在引擎里的结构性职责再下结论；②删除资产类建议在提出前必须完成条目计数+消费方核验，缺证即不得写"建议删除"；③审查/分析结论与项目 skill 记载冲突时先读 skill 再落笔（skill 是踩坑沉淀，比单次审查快扫可靠）。

## 73. Legacy Animation 的 state 级设置（wrapMode/layer/weight）是纯运行时属性、不随场景序列化——循环必须落在导入器 loopTime 或组件序列化层（2026-09-23 AnimLookTest「全员静止」报障实证）

**症状**：AnimLookTest 四个 Animation 实例（含 09-19 已目检过的两个旧实例）进 Play 后用户报「所有角色都静止」；活体取证：timeScale=1、帧正常推进、四组件 enabled=true，但 **isPlaying 全部 False、stateTime 全部 0**。

**根因**：编辑态对 `AnimationState` 赋的 `wrapMode=Loop`（以及 layer=1/weight）是**纯运行时属性，场景保存时不序列化**——保存重载后全部蒸发，state 回落 clip 导入默认（Once）→ Play 后首轮 0.7~2s 播完即停、之后永远静止；09-19 的目检恰好发生在首轮内，掩盖了缺口。叠加既有事实：Legacy Animation 的 playAutomatically 只自动播**默认 clip**（其余 state 无脚本永不自动播）。

**修法（持久层）**：①循环落 FBX 导入器——`ModelImporter.clipAnimations`（为空则从子资产 clip 手工建表：name/takeName/firstFrame=0/lastFrame=clip.length×frameRate）逐条设 `loopTime=true + wrapMode=Loop` 后 `SaveAndReimport`；②双保险落组件序列化字段：`Animation.wrapMode=Loop + playAutomatically=true + cullingType=AlwaysAnimate`（这三样会存盘）；③reimport 后复查 state/默认 clip 接线（clip 引用 fileID 稳定，不丢）。

**How to apply**：给场景接 Legacy Animation 目检载体，循环一律走导入器/组件序列化层，**勿在编辑态对 AnimationState 赋值当持久配置（白做）**；「播一遍就停」「layer1 不动」先查序列化层；多 clip 同播（body+phys 双层合成）必须脚本驱动，Animation 组件无脚本做不到。

## 74. 新增序列化字段的脚本默认值改动，对运行中编辑器里已加载资产实例**无效**——资产文件未变则跨域重载保留内存实例（2026-09-23/24 瞄准分色三轮调色实证）

**症状**：BattlePalette 新增瞄准分色两字段后连续三次改脚本默认值（白 0.45 → 白 0.8 → 金 (1,0.8,0.35,0.8)），每次 refresh 编译 0 错，用户目检始终看到首版白 0.45；用户怀疑"被序列化了"，但 rg 资产文件零命中（文件里确实没有这两个字段）——活体取证 `BattlePalette.Instance` 读出 (1,1,1,0.45)，而 `ScriptableObject.CreateInstance` 的脚本默认值已是金色，**两者分叉**。

**根因**：Unity 对**文件未变更**的已加载资产，域重载时保留内存实例（serialized-data 缓存往返），**不重跑字段初始化器**。新字段首次域重载时被物化成当时的默认值（白 0.45），此后脚本默认值再怎么改，内存实例都冻结在首版值——「资产重存前走脚本默认值」（§Y9 战斗审查条目）只对**重启编辑器/强制重导入**成立，对长跑编辑器会话不成立。且危险潜伏：任何 `AssetDatabase.SaveAssets`（编辑器保存、某工具顺手保存）都会把冻结值烘进文件，把脏值变成"真源"。

**修法**：把拍板值显式写入资产并保存（`EditorUtility.SetDirty` + `SaveAssets` + `ImportAsset` 回读验证）——资产文件成为真源，编辑器内调色从此走文件。**勿只改脚本默认值指望已加载资产跟上**。

**How to apply**：给运行中项目的既有资 serialized 资产加新字段后需要调值时，一律「脚本默认值+资产值」两处同步（或直接写资产）；症状指纹=改默认值编译通过但运行值不变、资产文件缺字段而 Instance 值≠脚本默认；排查用活体对比三读：磁盘资产值 / `Instance` 值 / `CreateInstance` 临时实例值（=脚本默认值），三者分叉即中此坑。

## 75. UGUI Image 空 sprite + Type=Filled 时 fillAmount 被忽略、条恒满——Filled 必须赋 sprite（2026-09-25 头顶条目检实锤）

**症状**：程序化头顶血条/元能条 `Image.type = Filled` + `fillAmount = 血量比` 每帧写入，运行表现=血条受伤不减、元能条初始恒满；代码逻辑、数据源（UnitView.Hp）全对，fillAmount 也写进去了。

**根因**：UGUI `Image` 不带 sprite 时走「简单矩形 Graphic」渲染路径，`Filled` 裁切只作用于 sprite UV——空 sprite 时 `fillAmount` 被静默忽略，永远渲染整条。

**修法**：赋一个运行时程序化生成的纯白 sprite（本案=BattleViewFactory 的 BarBgSprite/BarFillSprite，圆角+黑边框烘进贴图），fillAmount 立刻生效。连锁收益：圆角+黑边也顺手解决（撤四角偏移重投的 Outline 组件——细边在条形上糊，烘边框才 crisply）。

**How to apply**：程序化 UGUI 进度条/血条一律「白 sprite + Filled」，勿裸 Image；症状指纹=fillAmount 正确写入但条恒满。同批还有一条链式引用坑：显隐容器用 `transform.parent.parent.gameObject` 错链到条目根（差点把整条头顶条都关掉）——容器引用用字段显式存。

## 76. 战斗命令合并键粒度必须 ≥ 效应语义来源粒度——"去重"型合并吞掉跨来源命令（2026-09-25 二轮审查 R1）

**症状**：安柏延奏**自己**时客户端元能不减反增（应净 −10，命令只发了 +10）；被协奏者同片自身移动 +10 与协奏 +10/+20 只见一条。Host 状态全量应用正确、下回合快照自愈——**BattleEffectCommandAudit 查不出**（存在性检查对"合并语义"天然盲区：合并丢条 ≠ 漏发整类命令）。

**根因**：MergeDamageEffects/MergeHealEffects 合并语义=**数值相加**（丢条无损）；而 MergeEnergyEffects 语义=**去重取首**（B6a"战技多命中只获一次"）。消耗/协奏获能/移动获能与命中获能共用"目标"这一个键——不同语义来源挤进同一去重键，取首条即静默丢结算事实。

**修法**：EnergyEffect 加来源类别（MoveGain/SkillHitGain/EnsoGain/Cost），合并键=目标+类别——同类别取首（保 B6a 单行动多命中去重）、跨类别各发一条。

**How to apply**：新 BattleEffect 接入命令发射时先问"这个合并键会把谁吞掉"——合并语义是求和可粗键；是去重必须把去重范围（语义来源）编进键。症状归因：客户端某数值"错了但下回合快照自动对"=命令合并丢条类，先查对应 Merge 方法。

## 77. 带上限钳位的资源，"获取"先于"消耗"应用会被钳位吞掉——同段正负增量必须先扣后加（2026-09-25 自协奏实证）

**症状**：安柏元能 30（=上限）延奏自己（消耗 20+协奏获取 10，期望终值 20），实际终值 **10**；元能 20 时同样只剩 0 而非 10。

**根因**：SkillExecutor 的效应序=技能效应在前（+10 协奏）、消耗尾补在后（−20）——ApplyEffects 按列表序应用：+10 先到被 baseEnergy 上限钳位吞掉（30+10→40 钳回 30，**增量静默丢失**），再 −20 → 10。钳位资源（RangedInt）的增量不可交换：`clamp(x+g)−c ≠ clamp(x−c)+g`（当 x+g>max 时左式丢失 g）。

**修法**：元能两段应用——ApplyEffects 把 EnergyEffect 按正负分桶、**先全部消耗后全部获取**（跨行动同段同目标也覆盖：A 爆发 −30 与 B 协奏 A +10 同片时不再依赖行动枚举序）；命令发射序（MergeEnergyEffects 输出）同步先扣后加，客户端增量顺序与 Host 状态一致。拍板=docs/18 决策七"结算序"条（用户原话「应当先扣除，再加」）。

**How to apply**：任何带钳位的资源增量（元能/HP/体力）同段正负并存时，先问应用顺序——消耗先行是安全序（获取后置可吃到钳位余量）；症状指纹=期望 `x−c+g` 实得 `x−c`（获取整个被吞）。同族未决：HP 的伤害/治疗同段顺序未拍板（当前按效应列表序），治疗技能落地时一并定。

## 78. [Autowired] 依赖字段的初始化必须放在 Context.Inject 之后——"寻址阶段"拿到的注入字段是 null（2026-09-25 左上角只有数字无图标实证）

**症状**：战斗 HUD 左上角摩拉/体力计数条只显示数字、图标不渲染（数字走另条刷新链不受影响——症状=组件一半工作一半不工作）。

**根因**：`BattleHud.Bind` 顺序=`ResolveHudReferences()`（寻址+初始化）在前、`Wargame.Instance.Context.Inject(this)` 在后——寻址阶段顺手调 `InitItem(_itemConfig, ...)`，此时 `[Autowired] ItemConfig _itemConfig` **尚未注入=null**；`ItemCounterChip.InitItem` 对 null 配置走 `SetIcon(null)`，而 `SetIcon` 内 `icon.gameObject.SetActive(sprite != null)` **把图标节点主动隐藏**（后续注入完成也不会再亮——SetActive(false) 是持久状态非缺图）。

**修法**：依赖注入字段的初始化挪到 Inject 之后（`InitMyResourceChipIcons()` 在 `Context.Inject(this)` 后调用）；加注入失败防御 Warn（null 时显式报"ItemConfig 未注入"而非静默半渲染）。

**How to apply**：任何 `[Autowired]` 字段的消费点（初始化/寻址回调）逐个核对调用时序——**"寻址（Resolve）与注入（Inject）分离"的 UI 装配模式**下，寻址阶段只存引用、不做依赖消费；初始化动作集中放注入后。症状指纹=组件部分功能缺失且无报错（null 防御路径静默吞掉）；SetIcon 类方法对 null 入参 SetActive(false) 属"防御性隐藏"——消费方应在调用前判空并 Log，勿让初始化时序错误伪装成资产缺失。

## 79. 效果原子的 targetFilter 是"谁受益"的声明位——OnHit 分支漏实现 Caster + 迁移脚本误配 Target，战技获能 +10 发给了被命中的敌人（2026-09-25 用户报障实证）

**症状**：安柏战技命中敌方凯亚，安柏元能不涨（B6a 拍板「战技命中 +10 元能」完全不生效）；实际 +10 被加在敌方凯亚头上（打谁谁充能）。

**根因**（两层叠加+一个隐藏坑）：
① `SkillEffectConfig.targetFilter` 是效果原子"谁受益"的声明位，但 `EffectCompiler.CompileOnHit` 只实现了「命中目标（默认）」与「CasterRadiusAllies 群体」两分支——**targetFilter=Caster 在 OnHit 路径从未被消费**，恒走命中目标；
② B-1 迁移脚本把三战技获能原子（安柏双矢/凯亚霜袭）targetFilter 配成 0=Target——语义=受益者是被命中的敌人；芭芭拉水之浅唱则**整个获能原子漏配**（effects 只有伤害/附着/治疗）；
③ 同链路隐藏坑：`TurnResolver.ApplyEffects` 对元能效应**逐条累加**（两发箭矢=两条 +10 → 状态 +20），而命令层 `MergeEnergyEffects` 按 (目标,类别) 去重只发 +10——**状态与命令口径不同步**，修好①②后此坑立即显形（客户端显示 +10、Host 状态 +20，下回合快照才自愈）。

**修法**：`EffectCompiler.CompileOnHit` 补 targetFilter=Caster 分支（受益者=施法者/行动者）；三战技资产获能原子 targetFilter 0→1 + 水之浅唱补配 OnHit[EnergyGain Caster value 10]；`SkillHitResolver` 旧技能兜底的隐式获能原子同步 Caster；`ApplyEffects` 元能两段应用按 **(目标,类别) 去重**——与 MergeEnergyEffects 命令合并口径恒等（B6a「多次命中只获一次」由此在状态层真正成立）。

**How to apply**：①新效果原子落地时先核 targetFilter 全部分支是否被编译器消费——枚举里存在≠管线里生效（本次 Caster 枚举值在 OnCast 有分支、OnHit 没有，静默回落默认分支）；②迁移脚本批量生成原子时逐原子核对"谁受益"语义（B6a 口径文档写的是**行动者**，脚本按默认值 Target 落盘=受益者漂移）；③凡命令层有"去重/合并"口径，状态应用层必须同口径——否则状态与显示背离、快照自愈掩盖累积误差（症状指纹=命令显示量 ≠ 下回合快照量）。

## 80. 命中类效果的命令必须携带命中时刻——只有 Damage 带 launchMs，其余命中产物（元能/治疗）片头即跳（2026-09-25 用户报障「使用了战技立刻获得元能」）

**症状**：安柏战技命中敌方凯亚——箭矢延迟起飞、飞行、落地弹伤害数字全按时序走，但施法者元能在**片播放起点**就 +10（前摇都还没播完）。

**根因**：B-S1 时轮只给 Damage/消散命令带了 launchMs（发射时刻），命中链产出的其余效应命令（StatChange 元能/Heal）**没有任何时序元数据**——客户端在命令枚举处理时同步应用（`ApplyEnergyDelta` 立即执行）；而 Host 侧其实已算出精确命中时刻（ProjectileResolver 接触判定 hitT），只是没往下传。

**修法**：命中时刻全链透传——ProjectileResolver 把接触时刻 hitT 作 hitSeconds 传入 Hit()/CompileOnHit()/CompileAtom；EnergyEffect/HealEffect 加 HitSeconds；EmitSliceCommands 发元能/治疗命令时命令 launchMs 字段填命中毫秒（union 载荷复用为"应用时刻"，0=立即）；客户端 StatChange(元能)/Heal 分支 launchMs>0 → 协程到点再应用（与箭矢落地同步）。

**How to apply**：①命中类效果（获能/治疗/未来 OnVanish 等）新增命令映射时**必须**携带命中时刻——Host 已算出的时序数据（hitT）勿半路丢弃，只给 Damage 独享时序=B-S1 的半截工程；②union 命令字段跨类型复用时同步改 Header 注释明确各类型语义（launchMs=发射延迟 vs 应用时刻）；③客户端同步应用的命令处理器（非协程）加新字段时先问"这条命令有没有时序语义"——症状指纹=某数值在片头瞬跳而对应视觉事件（箭矢/移动）还没发生。

## 81. 敌我判定 playerId 口径在 1v1 下恒等价 team，9 处混用带病存活到 2v2 必炸（2026-09-25 三轮审查 C2）

**症状**：无（潜伏）——1v1 对局 playerId≠恒等价 TeamType≠，任何敌方筛选/我方目标域/投射物命中集合都"看起来对"；一旦 2v2（P1+P3 vs P2+P4），队友会被当敌人打进命中集合、协奏 Buff 发不到队友、延奏目标域漏队友单位。

**根因**：两种语义混用同一比较——**操控权/资源归属**（谁的单位、谁的手牌）与**阵营判定**（敌方/我军）在 1v1 下恰好同构，写的时候无感知。9 处混用：ProjectileResolver 敌方筛选、EffectCompiler（Contract 校验+AllAllies+CasterRadiusAllies+WouldHitProjectile）、SkillHitResolver.FindEnemiesAt、BattleHeuristics.FindNearestEnemy、AIDebugBrain 两处、BattleHud（瞄准/预览/队列色）。多处注释已自我承认"1v1 暂代，B7 换 team 字段"——正确原语 UnitIdentity.IsSameTeam 一直存在但消费方没用。

**修法**：口径原则单源化——**阵营判定=TeamType、操控权/资源归属=playerId，勿混用**；预判链签名统一 casterPlayerId→casterTeam（FindEnemiesAt/BaseSkill.WouldHitEnemyInDirection/EffectCompiler.WouldHit*/BattleHeuristics.PreviewLineTargets 等）；HUD 单位查找拆 UnitSide.Mine（操控权）/MyTeam/Enemy（阵营）三向。BattleHud.CreateView 队伍色绝对映射（A=我方色）保留=B7 分端 viewer 重定议题（已有登记）。

**How to apply**：新增敌方筛选/我军目标域代码一律 TeamType 比较（快照 UnitState.team 字段已带，sim.GetTeamOf(playerId) 查队伍）；自查指纹=`playerId !=`/`OwnerPlayerID !=` 出现在"敌人/我方"语义处（合法保留处=操控权与资源归属：上交归属校验/CollectMajurs/chip 刷新/手牌归属）。

## 82. 「轻提示」通道选错——提示条 SetTip 文字切换太隐晦，用户实测看不见（2026-09-26 报障返修）

**症状**：用户选敌方单位→移动瞄准→点可选格提交，预期弹「这不是你的角色」轻提示——实际"未看见弹窗"。Console 无错误、逻辑拦截本身生效（Host 校验链正常）。

**根因**：通道选错——首版用 HUD 提示条 SetTip（Battle_Tip* 键，与「选择移动方向」同一根文字条）：纯文字原地切换无入场动画无弹窗感，玩家注意力在棋盘上根本不会注意到条上文字变了。项目轻弹窗正主=**PopupManager.ShowToast（顶部滑入、可堆叠去重、Wish_NoPrimogem/Deck_Full 同款）**，且 toast 文案表=**PopupText**（非 UIText 战斗段）——首版连表也写错了。

**修法**：提交拦截改 ShowBattleToast→PopupManager.Instance.ShowToast(new LocalizedString(TableName.PopupText, key))；键迁移 Battle_NotYourUnit/Battle_MinorUnit 落 PopupText 表（UIText 旧键 12033/12034 删净）；PopupManager 常驻根断链降级 Warn。

**How to apply**：**需要玩家"看见并理解"的即时反馈（拦截/失败/不可用）一律 PopupManager.ShowToast**；提示条（Battle_Tip*）只承载"当前该做什么"的持续引导文案；toast 文案键落 PopupText 表、提示条文案键落 UIText 战斗段——通道与表一一对应勿混。

## 83. 本地化桥脚本：StringTableCollection 类型不可达+StringTable 无 SetValue——本地化写入必须走 SharedTableData+语言表资产直连（2026-09-26 实证）

**症状**：exec_editor_script 写本地化表——`using UnityEngine.Localization.Tables` 后用 `StringTableCollection` 类型报 CS0246（类型不存在）；改 `StringTable.SetValue(id, value)` 报 CS1061（成员不存在）。

**根因**：桥脚本 Roslyn 环境引用集不含 StringTableCollection 所在定义（编译不可达）；Tuanjie 的 Localization 版本 StringTable 无 SetValue 方法——表内条目写值 API=AddEntry(id, value)（新条目）或 TableEntry.Value 直赋（已有条目）；另 LocaleIdentifier 是非可空值类型（`!= null` 直接 CS0037）。

**修法**（本地化写入标准姿势）：①FindAssets("t:SharedTableData") 按资产名（"UIText Shared Data.asset"/"PopupText Shared Data.asset"）加载共享数据；②FindAssets("t:StringTable") 按表名前缀（UIText_/PopupText_）加载各语言表；③加键=shared.GetEntry(key) 查重→shared.AddKey(key, id)（指定 id 重载）；④写值=table.GetEntry(id)==null ? table.AddEntry(id, value) : entry.Value = value；⑤SetDirty(shared+各表)+SaveAssets+回读核验。

**How to apply**：见 gic-localization skill 陷阱节（同日补录）；各表 id 体系独立——UIText 用手编 12000 分段，PopupText 走 Unity 自动全局大 id（取表内最大+1 续排，勿照搬 12000 段）。

## 84. TMP 大字描边：UGUI Outline 固定像素式在缩放视口下不可见——大字一律用 SDF 着色器原生描边（2026-09-26 报障返修）

**症状**：倒计时数字（61.2pt）加 UGUI Outline（effectDistance 3,-3）用户目检"未看出有描边"。

**根因**：UGUI Outline=复制网格 4 副本对角偏移染色——粗细是**固定画布像素**且只有 4 个对角方向（大字笔画间有断缝）；画布 2560 宽在用户视口按 scaleFactor 缩放（如 1280 窗口=0.5），3px 描边只剩 ~1.5 屏幕像素——视觉归零。小字号（伤害数字 ~40pt 配 2.2px）同样受缩放压缩，只是未被告障。

**修法**：TMP 文字描边用 SDF 着色器原生 `_OutlineWidth`/`_OutlineColor`（宽度相对字形、随缩放恒定、连续无断缝）——**运行时独立材质实例**（`new Material(fontSharedMaterial)` + SetColor/SetFloat + 赋 `tmp.fontMaterial`），勿直接改共享字体材质（全项目文字共用会全员描边）；实例调用方 OnDestroy 释放（TMP 不自销，§63 生命周期纪律）。zh-cn SDF=TextMeshPro/Distance Field 全属性在；图集 4096 已到上限不会重建（材质实例 _MainTex 引用不失效）；若字体材质无 _OutlineWidth 属性（变体 shader）应 Warn 降级。

**How to apply**：新 UI 需要文字描边（尤其大字/要跨分辨率恒定可见）直接 SDF 原生描边；UGUI Outline 只适合"同屏固定像素量的轻投影/小字"场景。倒计时实现=BattleHud.TopBar.cs（_countdownOutlineMat），宽度=BattleHud Inspector「倒计时描边宽度」0.08 可调（迭代链 0.3→0.22→0.15→0.08，2026-09-26 用户三拍「调细」——0.08 已按 §74 流程重烘 prefab）；**伤害数字已同法迁移（2026-09-26 拍板「让伤害数字也用」）**=BattleDamageNumbers（池条目独立材质实例 Entry.OutlineMat+OnDestroy 统一释放，宽度=Inspector「描边宽度」0.08 同口径可调，迭代链 0.22→0.15→0.08）——全项目 UGUI Outline 文字描边已清零，勿再加回。**§74 冻结陷阱复发注记（同日）**：BattleHud 组件挂 prefab——「倒计时描边宽度」0.22/0.15 两改脚本默认值均未生效（prefab 烘焙/缓存值恒 0.3，用户目检"两次调细没变化"即此），修复=LoadPrefabContents+反射 SetValue+SaveAsPrefabAsset 烘 0.15 进 prefab；**改"挂 prefab 组件"的 [SerializeField] 默认值后必须同步烘 prefab**（运行时 AddComponent 的组件如 BattleDamageNumbers 才吃脚本默认值）。另证（探针）：祈愿界面卡池文字描边本就是 SDF 原生——zh-cn SDF 共享材质本体 _OutlineWidth=0.15 黑（角色名 128.7pt）、zh-cn SDF 1 预设 0.08 白（称号 150pt），与倒计时/伤害数字同族无需迁移（2026-09-26 起倒计时/伤害数字终值 0.08 与称号预设同宽）。

## 85. 贴图可见边距陷阱：同 sizeDelta 拼装的多张贴图可见缘天然错位——「X 比 Y 小」类报障先实测贴图可见半径，勿当设计拍板执行（2026-09-26 拖动圆盘实证）

**症状**：拖动瞄准大圆盘的半透明阴影可见缘比金色描环明显小一圈（约 17%），用户报障「半透明阴影比圆环小一点，这是不对的」。首轮误读为「再缩小阴影」的设计拍板（×0.92 收缩比）反向放大了错误，用户澄清后回滚修正。

**根因**：`disc.png`（TabGlyphs 实心盘）的可见圆缘只占纹理半宽 **0.830**（四周大片透明边距），`circle.png`（Skills 细环）的描环线贴纹理外缘 **0.998**——两张贴图同 sizeDelta 摆放时，可见缘天然错位 ~17%（340 名义半径下阴影只显示到 ~282）。同尺寸≠同可见尺寸。

**取证**：Unity 侧 editor 脚本 RenderTexture.ReadPixels 扫中心行 alpha 带（外缘比例=0.830/0.998 实测）。注意 **System.Drawing 读该 PNG 的 alpha 恒 0**（格式/预乘误读不可信）——PNG 像素取证一律走 Unity 导入后的纹理实测（与「识图 AI 不可作内容判定依据」同族：视觉差异数字必须实测）。

**修法**：实心盘纹理（大圆盘阴影+小圆盘）×`实心盘贴图补偿` 1.202（=0.998/0.830）放大，可见缘贴齐描环线/名义半径；多出的透明边距被描环盖住不可见。常量在 BattleHud.cs（实心盘贴图补偿），换贴图资产按实测重算。

**How to apply**：①用户报「X 比 Y 小/不对/错位」类视觉差异，先像素级量各贴图**可见缘比例**再动手——透明边距是素材常态，勿直接当"缩小/放大"设计拍板执行；②多张贴图拼装对齐按可见缘而非 sizeDelta；③补偿系数=目标可见缘比例÷本贴图可见缘比例，写注释留实测数字。

## 86. 板面点击视差：拾取交平面用地块底面 y=0，而玩家视觉点击面是地块顶面——俯角下交点向远端漂 0.2~0.6 格，点推荐格上半部被解析进邻格「判空白」（2026-09-26 报障「点到推荐格无反应」，用户直觉「和角度有关」正确）

**症状**：点击式与拖拽式瞄准，明明鼠标点在推荐高亮格上却无反应、被当成点空白（瞄准态被取消回选中态）；时好时坏且**位置相关**——越靠屏幕上方的格越容易坏、点格下半部基本正常；拉近视角后更糟。单位选中同族（点某格上缘选中了屏幕上方那格的单位）。

**根因**：拾取链 `BattleHud.OnBoardTap → BattleCameraController.TryGetBoardPoint` 把指针射线交在 `Plane(Vector3.up, Vector3.zero)`＝**地块底面 y=0**（注释自书「地块底面」）；而玩家看到并点击的是**地块顶面**（草顶 `_tileTopHeight`=0.5、水顶 0.36，瞄准高亮 quad＝表面+0.03，`BattleBoard.GetSurfaceHeight`）。射线穿过 0.5 高的视觉面后再坠 0.5 才达 y=0，交点沿视线**向远端（屏幕上方）漂 h/tan(射线俯角)**：屏幕中心射线（俯角 55°）≈0.37 格；**透视相机越靠屏幕上方射线越平（俯角越小）漂移越大**——默认视轴距离 35 下棋盘远端格 ≈0.55 格、近端格仅 ≈0.19 格（故「有时候」坏）。解析进邻格后：邻格不在 `_aimCells` → 走「点空白」分支 `ExitAiming()`。点击式点格与拖拽式松手后「点格改待定」共用 OnBoardTap 一条链，故两式同坏；拖动圆盘的盘位→方向/距离解析是画布空间，不经板面拾取，无此问题。俯角 90°（正俯视）漂移=0——纯角度问题，与用户直觉一致。

**修法**：取格拾取改**两遍收敛**（`BattleHud.TryPickBoardCell`）：①射线先交「地块顶面高度平面」（`BattleBoard.TileTopHeight` 新公开 getter）初判格；②初判格为水面（表面更低）时按其真实表面高度再交一次收敛（表面高度仅两档，二次即稳；水/草交界 ≈0.1 格窄带内取水面格，视觉本就含糊）。新增 `BattleCameraController.TryGetPlanePoint(screenPos, planeHeight, out point)`；`TryGetBoardPoint`（y=0）保留给相机平移抓取——grab/current 同在 y=0 面、差值恒定，平移手感不受视差影响，勿顺手改。

**How to apply**：①棋盘/地面类点击拾取的交平面必须取**玩家视觉点击面的实际高度**（地块顶面/水面），勿用抽象 y=0 底面；漂移公式=h/tan(射线俯角)，透视相机下屏幕各处俯角不同、视线越平漂越大；②高度多档地形用「先交最高面初判→按格查真实表面高→再交一次」两遍收敛，勿硬编码单一平面；③报「点到了却没反应/判空白」且位置相关（越远越坏、格下半部正常）＝本陷阱指纹。

## 87. 「拖回键区取消」用键槽矩形命中 vs 瞄准轮盘距离带几何相吃——短拖（1 格）松手必被判空放（2026-09-26 拖动瞄准报障「只拖最近的1格松开判定我空放」）

**症状**：拖动式瞄准只拖最近 1 格（如移动）松手被判空放/取消——拖动中金色待定明明已实时显示，松手即消失。

**根因**：松手取消判定 `ReleaseOverDiscOrCancel` 用**原始指针**对「技能盘任一键槽矩形」做命中（拍板本意=王者荣耀「拖回键区取消」+防微拖误触）；而距离转盘把全臂程映射进大圆盘半径 340px——臂 5 格时「第 1 格」盘距带=0~102px，**整带都在键槽 220×220（半宽 110px）矩形内**：短拖松手指针必然还压在起手键槽上→判「拖回键区」→ExitAiming；右侧键簇（skill 槽心 (1971,302)↔burst (2253,302)，矩形仅隔 39.6px）连「第 2 格」带（102~170px）也被邻键矩形盖住。几何实测=editor 脚本 PrefabUtility.LoadPrefabContents dump 槽位（画布 2560×1440；move (179,504)/cancel (2406,1123)）。

**修法**：取消区与瞄准带解耦——**键槽矩形不再作松手取消判定**（ReleaseOverDiscOrCancel 只留取消钮）；「防微拖/拖回取消」由**键心死区**承接：盘位距轮心<死区半径（=小圆盘半径 56——小盘压着键心=手指未真离键，可视判据）时瞄准解析返回 null，松手走 !pending 取消；金色实时反馈即取消预告（盘拖回键心金色即隐）。k 映射同步改**死区缘=第 1 格、盘缘=最远格**（盘距越过死区后的比例×臂长），保证死区不吞格带。

**How to apply**：①「拖回 X 取消」类防误触判定先量取消区与合法操作区的**几何重叠**——整块 UI 矩形当取消区时，映射进该区域的合法选择全被吃掉；②轮盘/摇杆类内环距离带必然贴着起手键，取消区取「键心小死区」而非「键矩形」；③报「短拖/轻拖被判取消」先 dump 控件几何（槽位/尺寸/映射带）算重叠，勿先怀疑输入层。

## 88. 轮盘瞄准的判定基准之争——三轮收敛终版=「盘=标尺」（判定相对盘心+小盘不出盘+盘入屏）；位移基准/盘缘分母/盘位镜像三版均被用户否决（2026-09-26 拖动瞄准自适应位三轮迭代）

**症状（设计三轮迭代）**：用户五拍「大圆盘应当自适应位置，保证玩家能拖动到大圆盘上任何位置」→ 首版盘位镜像（小盘=圆心+位移×预算归一半径，不贴鼠标）被二拍打回「小圆盘应当始终在鼠标位置」→ 二版（小盘贴鼠标+输入=「按下点→指针」位移+步数分母=按下点到盘缘行程）被三拍定稿「1.拖动的格子判定应当是相对于大圆盘中心 2.小圆盘不可超出大圆盘（需确保技能按钮不会超出屏幕）」。

**根因**：圆心自适应挪位后，「小盘贴鼠标」「盘缘=最远格」「起手无预选」三者不再天然成立——不同基准选择各救一头、丢另一头：位移基准（按下点→指针）起手干净、方向直觉，但盘上位置≠瞄准真值（盘缘谎报，靠盘缘分母修补仍被否）；绝对基准（相对盘心）盘=标尺全真，但键位≠盘心时起手即有初始待定（按键在盘上的偏移直接成初始瞄准值）。**用户两轮否决的取舍方向=盘上位置必须真、贴鼠标必须真；起手预选为接受的代价。**

**终版（三拍定稿）**：①大圆盘圆心=键心沿两轴 clamp 夹进画布〔盘半径+小盘半径+边距〕（盘不超屏）；②小盘=指针贴身且**夹在盘内**（盘心距≤盘半径，拖出盘范围时贴盘缘=该方向拖满）；③**格子判定基准=盘心**（盘心→小盘位移定十字方向+盘距比例定步数，盘心死区缘=第 1 格、盘缘=最远格）；④键位≠盘心时起手即有初始待定（移动鼠标=连续改选）＝预期行为勿当 bug 修；⑤拖回盘心死区=取消预告（金色即隐）；⑥技能按钮整件不出屏（布局拖拽夹边从中心 2%~98% 收紧为整件+16px——三拍括注核验发现原钳制只夹中心、半宽件可挂出屏外 ~59px）。

**How to apply**：①轮盘类交互收到「判定相对盘心」类需求时按字面实现绝对基准，勿自作聪明改成位移基准——标尺真值优先于起手干净；起手预选的代价要在交付说明里明示；②「跟随件贴输入设备」（小盘贴鼠标）是用户底线，任何归一化/镜像/预算补偿做在解析层，勿动跟随件位置（两版镜像均一轮被否）；③「确保 X 不超屏」类括注先核现有钳制的实际口径（中心夹≠整件夹），再收紧。

## 89. 地形格间缝隙两连：地块 mesh 0.96 见方（几何缝）+ 每格自带半透明水面（渲染缝）——半透明面片分格必显缝，合并焊接单 Mesh 才是终解（2026-09-26 战斗地形无缝批）

**症状**：用户报「战斗地图的地形之间不要有缝隙」。第一轮修完（地块 mesh 边缘顶点 XZ ±0.48→±0.5）草地正确，但水面「不完整，能看到缝隙」。

**根因两层**：
①**几何缝**：TileGrass/TileWater/TileStone 三 mesh 主体盒 XZ 只有 0.96×0.96，而 BattleBoard 按 1 格间距摆放——每块四周各留 0.02，相邻块间 0.04 宽可见缝（meta GUID 密文陷阱：文本反查 GUID 假阴性，须 AssetDatabase.GUIDToAssetPath）。修法=边缘顶点扩到 ±0.5 严丝合缝；草叶装饰顶点（内位）不动、Y 全不动（站位高度口径 0.5/0.36 不变）。
②**渲染缝（半透明固有）**：不透明地形（草/石）几何扩满后无缝；但水面材质 BattleWaterFlow 是 **Transparent 队列 + Blend SrcAlpha OneMinusSrcAlpha + ZWrite Off** 的半透明面片，每格自带顶面时格界必有缝、**不可在分格方案内修复**：(a) 相邻面片在格界 MSAA/像素覆盖双重混合，混合色≠单层色→格界显浅色网格线；(b) 每格 UV 0..1 采样整张贴图，battle_water 上下缘不无缝（上缘均值 0.170 vs 下缘 0.243、角点 0.067~0.086 远暗于中心 0.231）→格界图案跳变显暗缝线；(c) 顶点波浪虽在共享边同位移不裂（wave=f(worldPos)，重合角点同值），救不了 。

**修法（水面=整片焊接单 Mesh）**：BattleBoard.BuildWaterSurface——所有水/沼格合并为单 Mesh，**相邻格共享角点焊接成同顶点**（波浪位移天然连续、覆盖无格界），UV=格角世界坐标（贴图 Repeat 每格重复一次=与旧同密度，流动图案跨格连续，顺带消灭 (b)）；水面材质取水地块 prefab 材质槽 1 携带（其顶面 submesh 1 已退役为空三角、槽位保留专作材质载体，避免场景接线）；WaterSurface 挂 Tiles 根下随建盘幂等重建，阴影关。星落湖 80 水格实证：320 未焊顶点→108 焊接角点、160 三角全上向法线、编辑模式建盘断言全绿。

**第三轮（波浪动态露缝）**：合并水面后用户再报「随水面动态变化仍见缝隙出现」。根因=**水格方块自身的墙**：每格水 tile 原为闭口盒（四壁 0..0.36），退役顶面时墙未动——顶点波浪把水面压到 0.36 之下（±0.03 波谷）时**墙顶边穿出水面**（全湖每格界两堵共面墙=泥土色网格线随波谷动态浮现）；且透过半透明水面恒能看到内壁网格（约 14% 透射）。修法=TileWater.mesh submesh 0（四壁+底面）一并退役为空（0/0/2=只剩 0.30 湖床 quad）——岸线挡视线由相邻草/石地块的墙负责（水墙本就与之共面冗余）；水下只剩连续湖床，穿墙源与透射网格同灭。附带=水面顶点细分（BattleBoard「水面细分」默认 4=顶点间距 0.25 格，1392 焊接角点/2560 三角）：每格 4 顶点时顶点波浪呈格状折痕（波长≈2.7 格仅 11 采样），细分让波面平滑可调。

**第四轮（贴片到水面下）**：水面上单位用移动瞄准，推荐高亮「到水面下了」。根因=**表面贴片抬升量低于波峰带**：瞄准高亮 quad=表面+0.03、选中金色圆盘=表面+0.024，而波浪水面高至 表面+波峰带（波场 sin+0.5·sin 峰值 1.5×_WaveAmplitude=0.03）——波峰 ≥ 贴片刻贴片被半透明水面叠画盖过（同透明队列按距离排序，贴片更远先画→水面叠上=「水面下」观感）。修法=BattleBoard.GetDecalHeight(cell, lift) **表面贴片高度单出口**：表面+抬升量，水/沼格再加 GetWaterWaveMaxHeight()（实读水面材质 _WaveAmplitude，将来调波浪幅度自动跟随；改 shader 波场公式须同步 1.5 峰值系数）——两调用点（瞄准高亮 0.03/选中标记 0.024）全走单出口。断言：水格贴片 0.42>波峰 0.39、草/石格 0.53 不受波峰带串扰。

**第五轮（底座圆盘被浪穿模，拍板「底座圆盘也改」）**：第四轮修贴片后用户拍板把同族的**水上单位底座圆盘**一并处理——单位站位=名义水面（0.36），波浪（±0.03）穿盘而过。修法=BattleBoard 新增 **GetVisualSurfaceHeight(cell) 视觉表面高度单出口**：水/沼格=名义表面+波峰带+0.01 贴面余量（0.40），**CellToWorld/ContinuousCellToWorld/GetDecalHeight 全部改走它**——单位立牌/底座圆盘（局部 +0.02→0.42）/投射物路径/贴片在水面上一致抬过波峰带；**GetSurfaceHeight（拾取 §86 两遍收敛水面格第二交、Host 口径）保持名义 0.36 不动**——波峰带纯视觉、拾取平面落波带中值最优。断言：水格 CellToWorld=0.40>波峰 0.39、连续版同高、草格 0.5/0.53 全不变。水上单位=飞行/两栖（步行不可入水），悬停 0.04 于名义水线上方观感成立。

**第六轮（远端贴片仍到水面下=透明排序，高度修复后残余）**：第五轮后用户再报「安柏向北方瞄准，第一格正常，后四格在水面下」并授权暂停现场取证。活体取证实锤：**贴片高度全对**（水格 0.43/草格 0.53 恒高于波峰），真根因=**单 Mesh 水面的透明排序按整物体包围盒中心**：Unity 同队列（3000）透明件按「renderer 包围盒中心→相机距离」远者先画，整片水面一个 renderer（湖心距相机 13.86）——比湖心远的贴片（北臂 12_10~12_13 距 14.15~16.21）先画、被半透明水面叠画盖过=「水面下」；比湖心近的第一格 12_9（13.54）后画正常。**角度/位置相关**=此前验证恰在近侧未暴露。修法=水面 shader Queue 降 `Transparent-1`（2999，BattleWaterFlow.shader Tags 一行）：一切 3000 标准透明件（瞄准贴片/立牌 sprite/选中金盘/箭矢）恒后画于水面之上；水下无其它透明物（湖床 2000 不透明写深度），零副作用。**与⑧互补**：不动贴片队列（保贴片 vs 立牌距离排序），降大面积透明表面队列。现场取证方法论再证：暂停态先 resume 单帧解握手（§坑③），反射脆弱时按名寻址（AimHighlight_{x}_{y} 自带格坐标）+全公开 API（BattleBoard）零反射取证；Camera.main 在战斗场景为 null（相机未挂 MainCamera tag，立牌 billboard 同款坑），取场景 Camera 实例。

**How to apply**：①新增地形/水面类地块先量 mesh 实际尺寸 vs 格间距（编辑器读 bounds/顶点范围），勿信"应该是一格"；②**半透明表面分格渲染必显格界**（双重混合+UV 跳变双源，均不可在分格内修补）——整片表面需求一律合并焊接单 Mesh+世界坐标 UV，勿试图靠扩边/微重叠（重叠=更宽的双重混合带）；③**水下勿留格子墙**：闭口盒墙顶会随波浪下探穿出水面显动态缝——水下只有连续湖床、岸壁挡视线交给相邻地形块；④贴图 Repeat+世界 UV 的图案密度=每 1 世界单位重复一次，改密度调 _MainTex_ST 而非改 UV 写法；⑤顶点位移类波浪按波长 1/8~1/4 细分采样密度（每格 4 顶点不够），共享角点必须焊接成同顶点防撕裂；⑥退役 submesh 用 SetTriangles(空数组, i) 保留材质槽位比删 submesh+改 renderer 数组更稳（材质槽可作跨对象材质载体）；⑦**动态表面上的贴片（高亮/标记/选中圈）高度一律走 GetDecalHeight 单出口**，勿手写 GetSurfaceHeight+常数——抬升量必须清过波峰带，否则波峰经过即被水面盖过；⑧透明队列内「远处先画」规则=同队列贴片与半透明表面的遮挡关系由几何高度决定——**贴片队列勿硬抬**（破坏与立牌 sprite 的距离排序），遮挡问题靠⑩降表面队列解；⑨**动态表面上的站位视觉件（单位/投射物/贴片）高度一律走 GetVisualSurfaceHeight（贴片经 GetDecalHeight 间接走它）**，与拾取/Host 名义表面（GetSurfaceHeight）分离——视觉波峰带与判定几何解耦，勿把波峰带写进 GetSurfaceHeight（拾取第二交平面会漂）；⑩**单一大面积半透明表面（水面/雾面/大面积玻璃）渲染队列降到 Transparent-1（2999）**：合并单 Mesh 后透明排序按整物体中心算，远端小透明件必被后画盖过且角度相关难复现——凡「表面下大片透明板+板上方大量小透明件」结构，一律把板的队列压到标准透明件之下，小件队列保持 3000 不动；⑪**同队列两个透明件遮挡 bug 先算「各自包围盒中心→相机距离」再下结论**——与几何高度无关的「时隐时现/角度相关」遮挡=排序指纹。

---

## 90. 桥脚本环境与运行时取证坑（exec_editor_script / exec_runtime_script；2026-09-13/23 实证合集，2026-09-26 自 CODELY.md 记忆并入）

**exec_editor_script（Edit Mode 内联脚本）**：

- 内联脚本用 Newtonsoft `JObject` 必报 CS0433（与 Unity.Localization.ThirdParty.Editor 撞名，脚本环境无法 extern alias）——计数/取字段改用 `Regex.Match`，或返回原始 JSON 字符串在 AI 侧解析；本地化表桥脚本写入姿势/条目类型/字符串转义/zh-TW code 四坑=gic-localization skill「桥脚本」两节 + §83。
- 桥脚本环境引用集不含 UnityEngine.VideoModule——`VideoClip` 类型直接 CS0246；mp4 接线以 `AssetDatabase.LoadAssetAtPath<Object>` 加载 + 反射读写目标字段（实例运行时仍是 VideoClip，仅静态类型不可达），width/height/length 元数据经 GetProperty 反射读（2026-09-27 B-S3 视频接线实证）。

**exec_runtime_script（Play Mode 活体取证）**：

- **动真实存档的断言必须先快照+finally 恢复**——编辑器 Play 用的就是玩家真实档（LocalLow/HGAME/gic/gic_save.json，删档测试模式是否启用勿假设）：GetItemCount/ownedUnits 快照→恢复（未拥有条目=Remove、count=0 复活态=还原 0）+RebuildOwnedCards；TimeUtility 偏移测试尾 reset。
- 脚本正常完成后 Play 不自动退出——紧接要 refresh/编译先手动 stop（桥随后推 state=stale+custom_tools_reloaded 属常态，unity_refresh 重连即可）。
- 用户暂停现场（isPaused）时握手直接 Operation timed out 不报原因——先 unity_editor resume 再取证。
- 池化面板/常驻管理器在 DontDestroyOnLoad 的 GameScene/UIRoot 下 get_hierarchy 看不见，须 `FindObjectsByType(FindObjectsInactive.Include)` 反射读。
- 懒建/构建后才存在的私有字段每断言点现取（顶部缓存=null→NRE 或假失败）；无参私有方法反射调用=Invoke(ctrl, null)，传 new object[]{null} 报 parameter count mismatch。
- 断言失败先 try/catch 打印完整内部堆栈定位归属（脚本 vs 产品代码）；活体取证时序纪律（超时污染/暂停态/按名寻址/Camera.main=null）见 §64b/§39/§89。

## 91. 视频资产内容替换：Play 态 VideoPlayer 握文件句柄锁死覆盖 + 管道过滤掩盖 ffmpeg 失败（2026-09-27 实证）

**症状**：对已入库且被 VideoClip 引用的 mp4 做内容替换（`ffmpeg -y` 覆盖）「流程看似成功但覆盖没生效」——探测发现游戏内文件仍是旧内容。
**根因**：①编辑器停在 Play（含暂停态）时，VideoPlayer/WMF 解码器握着 mp4 文件句柄，ffmpeg 打开输出报 Permission denied；②`ffmpeg ... 2>&1 | Select-String "frame="` 过滤掉了错误行，且 PS 命令链 `;` 串联时整体 exit code=最后一个命令的——ffmpeg 失败被后续 Copy-Item 成功掩盖。
**修法/纪律**：①改写被 VideoPlayer 消费的 mp4 前先 `unity_editor stop` 退出 Play（既有授权 2026-09-16「之后你可以自行退出play」）；②覆盖后必做**探测回读**（ffprobe duration/帧数 vs 预期值）确认新内容落盘，勿信命令链 exit code；③要看 ffmpeg 报错时用 `Select-Object -Last N` 看尾部全文，勿 Select-String 过滤。
**连带**：落盘后还需 `unity_editor refresh` 重导入（VideoClip 缓存的 length/width/height 不会自动刷新）+编辑器回读断言（桥环境 VideoClip 类型不可达→Object+反射，§90）。
**连带二（2026-09-28 待机循环首裁翻车）**：循环段裁剪勿用 `select='between(n,a,b)'` 过滤器+`-r`——select 保留原 PTS（输出首帧时间戳仍在 1.4s 处），`-r` 会用重复帧填补 seek 空洞（dup=34 → 产物前段冻结假循环，编码器看似正常）；正解=`-ss a/fps` 输入端 seek（时间戳归零）+`-frames:v k`。**穿帮指纹=验证脚本把重复帧当完美周期：检出「最优循环」bbox 恒定/零漂移/接缝 0——三项全绿即中此坑勿信**；全案=gic-paperdoll skill 生成配方第 4 步。

## 92. 立牌「陷地」归因两连反转 + 归一化机制凭 meta 推断翻车：机制结论必须活体实测，素材/渲染/几何三层归因以用户目检+像素数据双定（2026-09-27 立牌缩放/离地批实证）

**症状**：①凯亚全身立牌「脚有一点陷入地下」；②立牌高度对比分析中「sprite 路线按 tight bounds 归一、视频路线按整帧归一=基准分叉」的机制结论写出后需推翻。
**根因**：①两次归因反转——首版归因=素材底部 alpha 16~102 渐隐行压地面线混色（像素实证存在但**非主因**），用户目检纠偏「原因不是画图，而是底部圆盘」：实体不透明盘面 y=0.52 高过补偿前脚底 0.507（盘面在世界高度上确实「高于」脚底 0.013），脚站盘心=「栽进坑」观感——**素材侧像素证据齐全也可能不是主因，渲染/几何层归因用户一眼就能定**；②`spriteMeshType: 1`(Tight) 被想当然推出「bounds=alpha 裁切」，活体实测 sprite.bounds=**整画布**（608×1088→Extents 3.04/5.44 精确等于半宽半高）——Tuanjie 该构建 bounds 恒为整 rect，两条立牌路线归一基准其实相同。
**修法/纪律**：①归因结论落档前必须过活体实测（exec_runtime_script 读 renderer.bounds/sprite.bounds/transform 链）——尤其「X 路线 vs Y 路线基准不同」类机制断言，一行回读就能定案；②报障排查顺序=先问渲染/几何层（盘/排序/深度）再查素材像素——素材证据易得易误导（渐隐行真实存在且诱导归因）；③补偿值先现场运行态调参目检（AvatarTilt.localPosition 即改即看、退 Play 回滚零风险），确认值再烘资产；④YAML 里序列化字段名带双引号（`"\u79BB..."`），rg 模式须含引号+用无反斜杠的十六进制尾段（`5EA6": 0.5`）定位值，全字段名正则在 PS 双引号转义链下不稳定（Select-String 直印行更稳）。
**连带**：盘半透明后所有写盘颜色的路径须过单出口保 alpha（WithDiscAlpha——SetFrozenVisual 直接 sharedMaterial.color=满色会复辟实体观感）；盘透明化排序=sortingOrder -1（恒在贴片 0/立牌 10/箭矢 12 之下、水面 2999 之上，与 §89 第六轮水面队列互不冲突）。

## 93. Tuanjie 1.9.3 贴图 maxTextureSize 三通道写不生效：实际尺寸由**活动平台块**（Standalone overridden=1）拍板——TJGenerators 生成贴图瘦身必写平台块（2026-09-27 OrbitBeamArc 落盘实证）

**症状**：TJGenerators 生成的 2K 贴图瘦身到 512——`ti.maxTextureSize=512`、`TextureImporterSettings.maxTextureSize=512`（经 `ReadTextureSettings/SetTextureSettings` 通道）、`GetDefaultPlatformTextureSettings()` 写 DefaultTexturePlatform 块（overridden=1）三条路全写了，`SaveAndReimport` 后 `tex.width` 仍 2048。
**根因**：生成器写入的 meta 里 **Standalone 平台块 overridden=1 且 maxTextureSize=2048**——导入管线取「活动 build target（Standalone）的平台块」，overridden=1 时平台块压过一切顶层/Default 块设置（meta 顶层 `maxTextureSizeSet: 0` 也印证顶层 maxTextureSize 只是摆设）。DefaultTexturePlatform 块只在活动平台**未** override 时兜底。
**修法/纪律**：改生成贴图尺寸一律 `ti.GetPlatformTextureSettings("Standalone").maxTextureSize=N; ti.SetPlatformTextureSettings(...)`（或先查 `EditorUserBuildSettings.activeBuildTarget` 对应平台名），写完 SaveAndReimport 回读 `tex.width` 断言；顶层属性/Settings 结构/Default 块三通道全不生效属 Tuanjie 1.9.3 分叉行为（与 §91 TextureImporter 帧动画分叉同族）。**2026-09-28 补**：`TextureImporter.spriteAlignment` 在 Tuanjie 1.9.3 亦不存在（CS1061；标准 2022 为 int 属性）——`spriteImportMode=Single` 默认即居中无须设置；TextureImporter API 面分叉族又一实证，桥脚本遇「标准 2022 应有」的导入器属性先按分叉怀疑勿硬写。
**连带**：①平台块写入后 `sprite.pixelsPerUnit` 读回漂移（meta `spritePixelsToUnits=100` 正确、Sprite 对象缓存 25）——PPU 仅 SpriteRenderer/SetNativeSize 消费，UGUI Image（显式 sizeDelta）与直用纹理的 quad 材质均不受影响，勿为它反复重导；②生成贴图落 Resources 记得 `AssetDatabase.MoveAsset`（保 GUID）+ 烘 Sprite/Clamp/关 mipmap——History 目录为未跟踪草稿区，废稿 `DeleteAsset` 清掉防误提交；③AI 生成光弧类「同心几何」素材必须**像素实测**环半径占比/弧跨/对称度再烘进代码常量（本轮两弧 41°×2/对径 178°/占比 0.661，识图不可信=既有拍板）。

## 94. AI 生成发光贴图自带边缘暗杂线：亮核心杂点检测漏检低 alpha 杂质——落库必做「盘外清零」防线（2026-09-27 OrbitBeamArc v2 彗尾返修实证）

**症状**：AI 生成黑底发光贴图（亮度提取 alpha 落库）用户目检见底部一条多余横线；此前质检脚本却报 stray=0「无杂点」。
**根因**：①生成图最底边 y2042~2045 整条暗线（alpha≈10~30%、全宽）——质检只扫 alpha≥128 亮核心像素且径向界外判定，**暗线（低 alpha）全数漏检**；②生成器产物常带四边边缘杂线/噪点（本轮顶边另有 12px/行小噪点），与黑底噪声（提取 FLOOR≈18 已压）不同属结构脏物。
**修法/纪律**：①发光贴图落库前一律**盘外清零**：保留 r≤K×半画布（K=内容最远径占比×安全系数，本轮 0.83，环内容最远 0.78）、盘外 alpha=0——一条规则清掉四边所有边缘杂线；②杂点检测勿只盯亮核心：暗线/暗条才是生成器常见产物，最低限度补「行/列像素数剖面」扫描（单行像素数异常=横线指纹）；③净化后复测弧带几何（环占比/带宽/弧跨）应零漂移——盘外内容本就在测量分位之外，有漂移=清错内容。
**连带**：黑底生成→亮度提取 alpha→RGB 纯白化是发光贴图落库通用路线（segmentation 硬抠会砍光晕软边勿用）；AI 生成图另可能带「设计外的淡环」类生成器语义表达（本轮 alpha≈20% 满环淡圆环，保留属设计拍板非脏点）——报障先区分「结构脏物 vs 生成器语义内容」。

## 95. Unity 左手系正 yaw 与 2D 正 z 旋向相反：同贴图同取正角速，UGUI 版头前尾后、世界平铺 quad 版头尾倒置（2026-09-27 OrbitBeamsWorld 彗尾方向返修实证）

**症状**：同一段彗尾贴图（头亮尾渐隐、头在角度高端）——UGUI Image 版 +z 旋转目检头前尾后正确；平铺地面 quad（Euler(90,yaw,0)）+yaw 同号旋转后目检「旋转方向反了」（尾在前）。
**根因**：**Unity 左手系绕 +Y 的正角旋转在角量意义上是 atan2(Z,X) 递减（俯视顺时针）**，与 2D/UGUI 绕 +Z 正角旋转（数学逆时针、角度递增）方向相反；贴图 UV 映射不镜像（quad 本地 +X→世界 X、+Y→世界 Z，atan2(Z,X)=贴图数学角），「头在角度高端」在两种驱动下必然一头一尾。
**修法/纪律**：①世界平铺 quad 旋转贴图类效果（光弧/箭头/指针）角速度默认取**负值**对齐 2D 语义（OrbitBeamsWorld.环绕角速度：方向修正取负；大小=按钮版同速拍板，迭代 −150→**−240**），或驱动处 Euler(90,-angle,0)；②同贴图双消费方（UI+世界）方向语义冲突时改单值符号、勿镜像贴图（共享贴图镜像会弄坏另一消费方）；③quad 经 Rx(90) 后法线 -Z→+Y 朝上可见（Unity Quad 法线=-Z，勿按 +Z 推）。
**连带**：头尾方向可像素判定=弧段两端分半算平均 alpha，亮端=头（角度高端）——驱动方向定符号前先跑该检测，勿靠目测试错。

## 96. 同基类组件置换迁移三坑：单 Selectable 强制 / Tuanjie 无 CopyFromSerializedProperty / prefab 组件不在根 GO（2026-09-27 Toggle→SelectButton 七 prefab 迁移实证）

**症状**：编辑器脚本把全项目 Toggle 置换为自研 SelectButton（同基类 Selectable 派生）时连续三坑：①`AddComponent<SelectButton>` 静默失败（日志「Can't add 'SelectButton' to X because a 'Toggle' is already added! A GameObject can only contain one 'Selectable' component」），返回值 null 继续赋值→NRE，且 7 资产全部同错；②`SerializedProperty.CopyFromSerializedProperty` 编译报 CS1061（标准 2022.3 有、Tuanjie 1.9.3 分叉无此 API）；③迁移脚本 `root.GetComponent<T>()` 取屏组件落空——BackpackScreen 组件不在 prefab 根 GO 上（挂在子 GO「BackpackScreen」上，编辑期摆位坐标），字段重接全被跳过。
**根因**：①Unity 对 Selectable 派生类强制单 Selectable（连瞬时共存都不许——菜单层的限制就是引擎层硬约束）；②Tuanjie 序列化 API 面与标准版分叉（与 §91 TextureImporter、§93 maxTextureSize 同族的版本分叉行为）；③prefab 根 GO≠逻辑组件宿主是该屏的历史结构（多根/子 GO 摆位），迁移脚本按"根组件"惯性写就漏。
**修法/纪律**：①同基类组件置换一律走「**快照（公共属性直读）→DestroyImmediate 旧件→新建/沿用继承件→回填**」四步，绝不依赖新旧共存；Selectable 基类配置用公共属性直拷（targetGraphic/colors/spriteState/animationTriggers/transition/navigation/interactable，两派生类同基类布局一致），自有私有字段用 SerializedObject.FindProperty 按字段名写；②跨对象拷序列化配置优先公共属性/手写字段映射，勿押 CopyFromSerializedProperty；③迁移/重接脚本枚举组件一律 `GetComponentsInChildren<T>(true)` 而非 `root.GetComponent`；嵌套实例先转资产本体再转父 prefab（父内 dangling 覆盖块回落继承态），字段按同 GO/向上寻组结构重接并输出逐条报告。
**连带**：①嵌套实例的组件块在资产迁移后变 dangling/added 形态仍会被 GetComponentsInChildren 枚举到——正是置换回填的输入，销毁后父 prefab 记录正确回落；②零登记 SelectionGroup+对象池组合：**凡池化成员先归还后重建，重建前 ClearSelection**（按钮侧 Group 清了组侧 Current 仍持旧实例，同实例复用命中 Select 的 Current==item no-op——进背包初始选中返修实证，docs/18 决策十三返修条）；③诊断只读脚本对 inactive 资产对象 `GetComponentInParent<T>()` 无参版本因 active 链全 false 恒空——须手写 transform.parent 链逐级 GetComponent 或带 includeInactive:true 的数组版。

## 97. 缓存复用特效件的显/隐配对：收起走 SetActive(false) 后，复用路径必须显式 SetActive(true)——「首建即 active」掩盖漏激活（2026-09-27 底座弧光「第二次选中起隐形」报障实证）

**症状**：选中角色立牌底座的两束队伍色弧光「不见了」——**首次选中正常显示**，取消选中后再选任何单位永远隐形；编译零错、Console 零警（纯逻辑断链，目检才可见）。
**根因**：OrbitBeamsWorld 为战斗期缓存复用件（`_discOrbit` 只在 null 时 Create）；new GameObject 首建默认 active，HideSelectMarker 把它 `SetActive(false)` 收起，而 ShowSelectMarker 复用路径只调 `Setup(位置, 色)`——**Setup 内无激活**。首建即 active 的天然状态掩盖了「再显须重激活」的缺口：会话 Y 首次验证（只选中一次）通过后潜伏，多轮选中/取消后才暴露。
**修法/纪律**：①特效件显/隐必须在同一驱动点**成对**（范式=SetAimSelectRing(true/false) 两分支各写一次 SetActive）；②「懒建+缓存复用」件再显一律显式激活，勿依赖首建默认态；③显隐类复用件审查口诀=沿「创建/收起/复用」三条路径各核一遍激活写法。
**连带**：同库扫一遍同模式无同族（OrbitBeamsUi=SetAimSelectRing 成对 ✓、SkillIconView=onSelectedChanged 成对 ✓、拖动圆盘/详情面板/RelatedPanel 三件均显↔隐成对 ✓）。

## 98. 地形 mesh 顶面 UV 反向（image-top 落南缘）与批量改顶点的双翻垃圾态；草簇格内偏移基准点错认（2026-09-27 地形贴图返修实证）

**症状**：AI 生成的地块顶面贴图（草地/石砖）在游戏里「上方内容朝南」；返修翻转 UV 时又产出半翻垃圾态（部分顶点翻、部分不翻）；草簇装饰整体 +0.5 对角漂移、55 簇漂进水面。
**根因三连**：①TileGrass/TileStone 顶面 UV 与世界方位不对应——image 坐标系 top 行落在世界南缘；②mesh 顶点被**三角形索引重复引用**，逐顶点循环翻转时同一顶点被处理两次（正翻+反翻=不翻），且去重不彻底时双翻与漏翻并存；③草簇 root 公式把「格内随机偏移 ox」错当「格心偏移」使用（+0.5 对角位移）。
**修法**：①顶面 UV **按位置重建**而非翻转：`u=x+0.5（东）、v=z+0.5（北）`——image-top 恒=北，未来换贴图免预处理；侧/底 submesh 未动。②mesh 批量改顶点属性**必先按位置索引去重或直接用位置重建法**（位置→属性的纯函数重写天然幂等自愈，无「翻过再翻」态）。③格内偏移公式统一 `root = x + ox − halfWidth` 基准（ox 为格内偏移、半宽居中），改后归属断言 off=0 全绿。
**连带（绕向）**：草簇立牌式 billboard 的正面判定=Unity 左手裁剪空间下 **cross(b−a, c−a) 朝观察者=顶点顺时针=front**（与 OpenGL 右手惯例相反）；写绕向断言必须**读存储的三角形序**算 cross，勿按固定边序假定。
**How to apply**：顶面类贴图方位一律以 u=x+0.5 / v=z+0.5 为准；任何 mesh 顶点批改走「位置重建」优先于「逐顶点翻转/平移」；格内装饰偏移先核基准点是格心还是格角（±0.5 漂移指纹即此坑）。

## 99. manifest 包版本≠编辑器 builtin 版本：在线重解析撞网络瞬断即剔包清缓存——钉回 builtin 同版后离线免疫（2026-09-28「编辑器报错」ugui 缓存丢失实证）

**症状**：编辑器打开即全项目几百条编译错误——CS0246（`EventSystem`/`PointerEventData`/`Text`/`UnityEngine.UI` 找不到）遍布 visualscripting/render-pipelines.core/Mirror/codely.bridge/Assets，伴 CS2001「PackageCache\com.unity.ugui@1.0.0 源文件找不到」。
**根因**：manifest 把 `com.unity.ugui` 写成 **2.0.0**，而编辑器 builtin 实为 **1.0.0**（`Editor\Data\Resources\PackageManager\BuiltInPackages\com.unity.ugui\package.json`）——71 包中唯一 manifest 版本≠builtin 版本者，也唯一需要走网络验证；当日在线重解析（155s）撞 packages.tuanjie.cn **ECONNRESET** 瞬断 →「Project has invalid dependencies」→ ugui 被从注册表剔除（72→71）+ PackageCache\com.unity.ugui@1.0.0 目录被清 → UGUI 程序集编译不出 → 全下游级联。昨日离线解析（6.82s）不触发此链。
**修法**：manifest+packages-lock 把 ugui 钉回 **1.0.0**（=builtin 同版、所有依赖方要求版本、同编辑器 umc 项目正常写法、昨日实际工作状态）→ 重启编辑器 → 解析 4.62s 纯离线 72 包全注册、ugui builtin 本地物化（695 文件）零网络依赖；UnityEngine.UI.dll/Assembly-CSharp.dll 编译成、后段零 error CS。
**How to apply**：①包缓存类报错先查两件事：`Library\PackageCache\` 该包目录是否还在、manifest 版本是否=编辑器 builtin 版本（builtin 查 `BuiltInPackages\包名\package.json`）——版本不匹配=每次解析挂网络=网络抖动即剔包清缓存；②钉版与 builtin 一致后该包纯离线解析，免疫网络瞬断；③**重启后首轮编译几百条 CS 过渡噪声（包物化完成前的首 pass）属预期**，判据=后段 CompileScripts 干净+ScriptAssemblies 产物时间戳新（本例 310 条错误全在日志 757~3809 行，5362 行 CompileScripts 干净收尾）；④GUIStateObj 刷屏=启动 import 期噪声自停；licensing ProductNameResponse 反序列化错/Curl 35 证书错=Clash 代理 MITM 干扰编辑器 HTTPS，无害。

## 100. 嵌套 prefab 链接断言：嵌套实例根的 GetCorrespondingObjectFromSource 返回「外层资产内嵌副本」而非源资产——须两级源链解析；资产上的嵌套子不是场景实例（2026-09-29 PreviewEntry 烘焙实证）

**症状**：编辑器脚本断言「PreviewEntry 内 Slot=QueueSlot 嵌套 prefab 实例」连续两轮失败——`GetNearestPrefabInstanceRoot(嵌套子)` 正常返回嵌套实例根，但 `GetCorrespondingObjectFromSource(嵌套实例根)` 返回 name=Slot、path=**PreviewEntry.prefab**（外层资产）而非 QueueSlot.prefab，`AssetDatabase.GetAssetPath(场景实例)` 更是恒空串，两法都判失败——实际嵌套链接完好。
**根因**：Unity 嵌套 prefab 语义=外层资产内部保存一份内嵌副本，嵌套实例的 corresponding source 指向**内嵌副本**（属外层资产），不是 QueueSlot 顶层资产；场景实例非资产，GetAssetPath 恒空。要拿到 QueueSlot.prefab 须对内嵌副本**再解一层**：`GetCorrespondingObjectFromSource(GetCorrespondingObjectFromSource(实例根))` → QueueSlot@QueueSlot.prefab（本例两级链实测命中）。另：在 prefab 资产本体上直接调 GetNearestPrefabInstanceRoot 也不可靠（资产上的嵌套子不是实例），断言须先 `InstantiatePrefab` 到场景再验。
**How to apply**：①断言嵌套 prefab 链接=场景实例上取 nearest root → **两级** GetCorrespondingObjectFromSource → 比对源资产 path；②勿用 `AssetDatabase.GetAssetPath(场景实例)` 判资产归属（恒空）；③烘焙脚本里嵌套子改名要在 SaveAsPrefabAsset **前**（改名后保存才携带）；④嵌套链接的真实红利=改源资产（如 QueueSlot.prefab 删 Speed 节点）自动传导全部嵌套方（PreviewEntry 实证），后续改嵌套件内结构优先改源资产。

## 101. Unity 序列化自动实例化：`[Serializable]` 类字段「null=未配置」判据会被资产重序列化击穿——编辑器脚本加字段后首次 SaveAssets 给全员补默认实例（2026-09-29 D 批次行为档案实证）

**症状**：D-4 伙伴行为档案（UnitConfig.UnitData 新增 `[Serializable] class CompanionProfile 行为档案` 字段，设计「null=中性档案兜底」）——首个配置脚本跑「档案 null 才写值」守卫时全员报 `EXISTS (skip)`、profiles written=0，但回读显示**全部 35 个单位**（含本不该配档案的 Paimon/Traveler/魔神）档案都已非 null 且值=中性默认——脚本根本没写过它们。
**根因**：Unity 对 `[Serializable]` **普通类字段**（非 `[SerializeReference]`）在资产反序列化/重序列化时**自动 new 默认实例**——往 UnitConfig.cs 加字段 → unity_editor.refresh 触发 UnitConfig.asset 重导入 → 资产序列化器给每个 UnitData 补 `行为档案: {默认值}` 实例 → 期间任何一次 `AssetDatabase.SaveAssets()`（哪怕来自无关的本地化脚本）就把默认实例**落盘**。此后 null 判据恒 false：「null=未配置」语义被序列化器偷偷改成「默认实例=未配置」。
**How to apply**：①给 `UnitData` 这类 `[Serializable]` 嵌套类新增字段时，**勿以 null 做「未配置」语义判据**——「中性默认」要么与字段类型默认值一致（全 1 权重/枚举首值=实现语义上就是中性，null 判断仅作防御性兜底），要么用哨兵值字段（如 `bool 档案启用=false`）；②首次配置脚本=**无条件覆写**目标条目值（幂等），勿用「非 null 即 skip」守卫；③编辑器脚本改了含新增序列化字段的 .cs 后，任何一次 SaveAssets 都会把自动实例化产物写进资产——**预期如此，勿回滚资产当 bug**；④抽查非目标单位确认默认值语义（本例 Traveler 距离1/激进度1/输出型=中性 ✓ 即健康态，非污染）。

## 102. 桥编辑器脚本四坑：旧程序集引用新字段=CS1061、本地化写入 API 实名、YAML 枚举=数字反查、dataCache 回读伪象（2026-09-29 协议核心批实证）

**症状**：①给 .cs 新增 `UnitData.受击圆柱直径` 字段后立即跑 exec_editor_script 引用该字段→CS1061「does not contain a definition」，而源码明明已写、编译器没说谎；②本地化写入脚本按旧记「SharedTableData+StringTable 直连 AddEntry」硬编码→SharedTableData 无 AddEntry，再吃一发 CS1061；③改完 UnitConfig.asset 用文本搜「ProtocolCore」验证落盘→零命中，误判「没写进去」；④脚本内 `unitDataList.Add` 新条目后 `GetUnitData` 回读→False，误判写入失败。
**根因**：①桥的 exec_editor_script 编译在**当前已编译程序集**上——.cs 改动未经 unity_editor.refresh 编译进 Assembly-CSharp 前，桥脚本引用不到新成员（源文件≠运行域）；②SharedTableData 建 key 的 API=`GetId(key, addNewKey:true)`/`AddKey`，AddEntry 只存在于 StringTable（`AddEntry(long keyId, string value)`）；③Unity YAML 把枚举序列化为**数字**（`unitName: 11001`），中文序列化字段名=\uXXXX 转义（.asset 同 prefab）——按字符串名反查恒假阴性；④UnitConfig.GetUnitData 走 BuildCache 的 dataCache——脚本内新增条目后缓存不刷新，回读断言查的是旧缓存。
**How to apply**：①桥脚本要消费「本会话刚加的字段」→ **先 unity_editor.refresh 再跑脚本**（refresh 顺带完成编译验证，一举两得）；②本地化写入实名 API：`long id = shared.GetId(key, true)`（不存在即建）→ `table.GetEntry(id)` 判重 → `table.AddEntry(id, value)` 或 `entry.Value=`；**UnitName/UnitTitle/UnitDescription 的 SharedData 已按枚举预播种全部 key（id=枚举值）且多为空值——写入=覆写而非新建**，勿被「已存在」报告吓退；③YAML 落盘核验按**序列化形态**查：枚举查数字（`unitName: 11001`）、中文字段查 \uXXXX 转义形态，勿查 C# 侧写法；④脚本内新增条目后的回读断言先 `config.BuildCache()`（或直接查 unitDataList）——「条目=False」先怀疑缓存伪象再怀疑写入失败。

## 103. UGUI 拖拽松手补发点击：拖动中挪动被按压控件（键挪盘心）→抬起补发 onClick，「松手=留待定」被「同键再点」误开详情面板（2026-09-30 拖动瞄准报障实证）

**症状**：拖动式瞄准在很近处松手（金格=第 1 格显示正常，已越过盘心死区）→不落格，反而打开该技能的详情面板；远拖（指针出键矩形）松手一切正常。
**根因**（StandaloneInputModule.cs 1.0.0 源码级）：①**拖动不取消点击资格**——`eligibleForClick` 仅在 `pointerPress != pointerDrag`（拖拽处理器在父级 GO）时被 ProcessDrag 清零；SkillDragForwarder 与 SelectButton 同挂 def.content 同一 GO → `pointerDrag == pointerPress` → 整个拖动过程点击资格恒在；②**抬起判定=按压/抬起命中同一 IPointerClickHandler**（`pointerClick == GetEventHandler<IPointerClickHandler>(currentOverGo) && eligibleForClick`）——B4「拖动键挪盘心」把控件临时挪到盘心，近距松手时抬手指针仍压在键矩形内（键半宽 110 > 盘心死区 56，第 1 格盘距带整条被键盖住）→抬起命中同一键→onClick 补发；③**点击先于 endDrag 执行**（ReleaseMouse/ProcessMousePress 抬起分支：先 click 后 endDrag）→尾巴点击在 `_dragAiming` 清零**之前**到达 OnSkillButtonClicked，命中「Aiming 同键=瞄准↔详情」循环分支误开面板（随后 endDrag 的落格链照跑，观感=落格没成、面板弹开）。
**How to apply**：①「拖动松手不应触发点击」类需求=**吞尾巴点击**：点击入口先 `if (_dragAiming || Time.frameCount == _dragAimEndFrame) return;`——会话旗（click 先到时仍在位）+同帧戳（endDrag 先到的引擎序差异兜底）双保险；两判据都不会误吞真点击（拖动会话中指针全程被按住不可能有新按压，真按压不可能与松手同帧）；②勿依赖「拖出阈值自动取消点击」——UGUI 拖拽从不清 eligibleForClick（仅 press≠drag 父子分裂才清）；③**拖动中把控件挪到指针下方（摇杆底座类 UI）=自造「抬起飞点击」——凡挪被按压件必配吞点击防线**；④松手判定与补发点击是两条独立链，排障勿假设「松手链正确=无点击」（本例 ReleaseOverCancelButton/死区链本就正确，症结全在补发点击链）。

## 104. 磁盘直改资产后编辑器内存实例陈旧：不 refresh 就回读断言=读旧数据误判「修法无效」（2026-10-01 棋盘缩图批实证）

**症状**：write_file 直改 BattleMap_FirstMap.asset 字符行（修一行湖形错位）后，立即 exec_editor_script 加载资产跑逐格断言——仍报同样 4 格不对称，修法看似无效；rg 核对磁盘文件内容却已是修好的。隔一轮 unity_editor.refresh 后同脚本复跑，断言全绿。
**根因**：AssetDatabase 不感知外部工具对磁盘文件的改写——已加载进内存的资产实例保持旧值；未经 refresh/导入就直接 `AssetDatabase.LoadAssetAtPath` 拿到的是**陈旧实例**（首次写入后有 refresh、二修没有，恰好首断言读旧值暴露）。
**How to apply**：①磁盘直改 .asset/.prefab/.unity 后，回读断言前必先 unity_editor.refresh（或桥脚本内先 `AssetDatabase.ImportAsset(path)` 再 Load）；②「改了没生效」类回读异常，先核改后是否 refresh 过，再怀疑修法本身；③断言输出与磁盘文本（rg/Get-Content）不符=陈旧实例指纹，直接定位本坑。

## 105. Percent 基准对 0 基值属性恒 0：「提升 X」语义歧义——**已按属性族统一 Percent 双语义**（百分比面板属性=+X 个百分点/点数属性=相对提升；2026-10-01 凯亚 1命吸血报障实证+同日拍板「应当全部统一」）

**症状**：1命凯亚攻击敌人后无吸血回血（用户暂停态现场取证授权）。取证实锤：命座修改器表两条全挂上（`LifeSteal type=BasePercent value=50`），但终值 `LifeSteal=0`、`HealEfficiency=150`——同配置一生效一无效。
**根因**：**Percent 基准=BasePercent 修改器（基值×(1+value/100)），对基值 0 的属性数学上恒 0**（0×1.5=0）。「吸血提升50」的本意是 0→50 个百分点（绝对加值），配 Percent 相对提升后无效；治疗效率基值 100 所以 100→150 侥幸生效。本质=「提升X%」在百分比数值属性上天然双解（+X 百分点 vs ×(1+X%)），非代码 bug。
**终版修法（2026-10-01 同日用户拍板「为什么不用 Percent？应当全部统一」——推翻首版「0 基值配 Fixed」局部方案）**：**Percent 双语义按属性族统一分流**（ConstellationApplier.PercentPanelStats 收口）——百分比面板属性（治疗效率/吸血等基值 0~100 效率刻度）的 Percent=**+X 个百分点**（BaseFlat，GI 命座口径：治疗加成/吸血「提升X%」=绝对百分点加值）；点数属性（移速/攻速）的 Percent=相对提升（BasePercent ×(1+X%)）。资产 C1LifeSteal 配 Percent 与 C1HealEfficiency 口径统一；未来暴击/暴伤/充能效率类百分比面板属性落地时入 PercentPanelStats 表。
**How to apply**：①命座 StatBoost 配参数：一切「提升」类一律配 Percent（统一口径），属性族分流由 ConstellationApplier 自动裁决；②新增百分比面板属性（0~100 效率刻度类）→ 加进 PercentPanelStats，否则 0 基值踩恒 0；③「修改器已挂但终值不变」的取证指纹=先查 0 基值×PercentPanelStats 表；④旧结论「0 基值属性禁配 Percent」作废——统一后 Percent 全场景可配。

## 106. 片前快照附着只读=同片多命中重复消耗同一附着：一层水吃两发火箭双蒸发（2026-10-01 实战报障实证）

**症状**：敌人只有一层水附着，安柏战技一箭双丘丘的两发箭矢**都**触发蒸发反应（伤害数字双「蒸发 N」+反应色）。
**根因**：**反应预判只读片前快照的 dyedElement，而附着消耗/覆盖在效应统一应用才落状态**——同片（ResolveSlice）内两发箭的命中编译全部对着同一份快照，第二发仍见水；「消耗被反应附着」对编译序不可见。决策八推翻 B4 简化①后逐发独立反应成立，但序贯消耗从未接上（HP/Buff 读快照=同片并发基石是对的，附着是**消耗性资源**不该只读快照——1 层水被两个消费者各消耗一次，docs/06 §6.3 1比1语义被打破）。
**修法（片内附着编译视图）**：BattleSimState 增编译期工作副本（BeginCompileDyeView/EndCompileDyeView 由 TurnResolver 片段+即时段紧贴 TakeSnapshot/ProjectileResolver.Resolve 包裹；GetCompileDye 视图优先回落快照=未开启旧行为）——Damage 原子反应预判改读视图，反应发生即 SetCompileDye(Physical)（消耗）；AttachElement 原子 SetCompileDye(来袭元素)（覆盖）。真实状态仍由 AttachElementEffect 统一应用写入，视图只服务反应预判、不落持久状态。效果：第一发蒸发消耗水→第二发见火=同元素不反应只附着（终态与统一应用一致）；跨行动链（同片安柏火+凯亚冰接力）也随枚举序正确接力（后手见火=融化）。
**How to apply**：①新增「读片前快照」类状态前先分类：**消耗性资源**（附着/可数次数类）编译期须随消耗推进视图，**并发基石类**（HP/Buff 存在性）维持只读快照；②新技能多段命中（箭雨 4 段/时轮逐发）自动获得序贯消耗，勿再各处特判；③视图生命周期铁律=只在编译窗口开启，效应应用/命令发射期必须已关闭（EndCompileDyeView 在 ProjectileResolver.Resolve 后）——否则视图会当第二真源漂移；④三层附着（多层消耗强化反应级别）落地时视图须升级为「元素+层数」而非单值。

## 107. 派生效应不继承母效应命中时刻：吸血 +N 绿字片头瞬弹、早于箭矢落地（2026-10-01 WYSIWYG 审计发现）

**症状**：吸血（2026-10-01 实装）的表现时序——箭矢还在飞，攻击者 +N 绿字已在片播放起点弹出，"先见回血、后见掉血"，违反「造成伤害后立刻加血」的所见即所得。
**根因**：**DamageEffect 不携带命中时刻**（只有 LaunchMs=发射时刻；客户端伤害弹出时刻自推导=launch+飞行时长），而 HealEffect 有 HitSeconds 通道（0=立即）——吸血 HealEffect 构造时未填 → launchMs=0 → 客户端按命令 stagger 片头瞬弹。伤害与治疗两套时刻通道不对称，派生效应（吸血）无从继承母效应（伤害）的命中时刻。
**修法**：①DamageEffect 增 `HitSeconds` 字段（投射物=接触 hitT；瞬发/整线迸发=段时刻与 LaunchMs 同值；tick=0 节拍随 LaunchMs 错峰；命令层不携带=仅供同片效应派生）；②CompileAtom 构造带入 hitSeconds；③MergeDamageEffects 合并副本保留（同命中点/反应取首条口径）；④吸血 HealEffect `HitSeconds = damage.HitSeconds>0 ? damage.HitSeconds : damage.LaunchMs/1000f`——投射物与箭落地同拍弹、tick 与错峰节拍同拍、瞬发双 0=立即原口径。逻辑层不变：Host 仍伤害应用同趟立即回血（ApplyEffects 内联）。
**How to apply**：①新增「由既有效应派生」的效果（吸血/未来反伤/受击触发类）时，**应用时刻必须从母效应继承**——投射物场景母效应只有 LaunchMs 不够用（客户端弹出时刻=自推导的落地时刻），需要 HitSeconds 通道；②两套时刻通道语义=Damage 命令带 LaunchMs（客户端自推导弹出时刻）、Heal/StatChange 命令带「应用时刻」launchMs（到点应用）——派生效应对齐的是**后者**；③tick 类错峰用 LaunchMs 不用 HitSeconds 的存量约定保持——兜底分支已覆盖，勿改 IcicleBuff/SongOfLifeBuff 构造。

**追记（同日实战复测报障「伤害数字 40 先出，+30 慢了一拍才出」——凯亚战技=霜袭是 LineBurst 整线迸发非投射物）**：HitSeconds 修复只覆盖**显式时刻**路径；launchMs=0 的直击（霜袭等 startTime=0 技能与无时轮兜底）按**命令 stagger 旧节拍**弹（每命令 +0.12s 槽）——治疗命令在发射序里晚于伤害 3~4 槽，+N 吃自己的槽=慢 ~0.4s；该时刻只存在于客户端、Host 无从随命令携带。修法=**客户端段内绑定**：`_segmentLastDirectDamageDelay`（actor→delay）段首清空，直击 launchMs=0 时记录本方弹出延迟；Heal 分支 launchMs=0 且 actor==target（吸血自疗签名）→ 绑定本方最后一击节拍**同帧弹**；多目标多伤绑最后一击（0.12s 槽内观感同步）。投射物自疗（hitT>0）仍走 Host 显式 launchMs 路径；零前摇+贴脸投射物 corner（hitT=0）走 stagger 兜底——当前角色池无此组合，撞上再议。**How to apply 补**：新增依赖「客户端 stagger 槽时刻」的表现时，同段绑定表模式可复用（记录方+绑定方两处，段首清空铁律）；判别「该时刻 Host 能否表达」=问"它是 Host 计算的显式毫秒还是客户端命令序节拍"，后者才需要绑定。

## 108. GraphicsSettings 手改 YAML 登记 shader 两坑：Always Included 计数器不同步=清单截断、meta guid 密文≠资产 guid（2026-10-02 冻结霜化批实证）

**症状**：FrozenSprite.shader 手改 `ProjectSettings/GraphicsSettings.asset` 追加 Always Included Shaders 条目后不生效/清单被截断。
**根因**：①该资产有 `m_LengthOfAlwaysIncludedShadersInInspector` 计数器字段，追加条目必须同步 +1，漏改=Inspector 按旧计数截断清单，新条目静默不显示；②shader 的 `.meta` 里 guid 行是**密文**（非真实资产 guid），`GraphicsSettings.asset` 里必须写**明文 guid**——从 meta 文本里抄=错 guid。
**修法/How to apply**：手改 GraphicsSettings.asset 时计数器与条目数同步核对；shader 明文 guid 一律 `AssetDatabase.AssetPathToGUID` 经编辑器脚本取（顺带可核 meta 密文陷阱勿文本反查，§9 同理）。

## 109. shader 迭代两坑：改 .cginc 后直接 compile 报 SourceAssetDB 时间戳告警、改默认值不追改已存材质（2026-10-02 冻结霜化多轮迭代实证）

**症状**：①改 FrozenFrost.cginc 后 `unity_shader.compile` 报 SourceAssetDB modification time 告警/结果不更新；②shader Properties 改了默认值，场景里仍显示旧效果（FrozenSpriteTest.mat 实测）。
**根因**：①增量库时间戳未刷新——编辑器还没 import 新版 cginc，compile 用的是旧缓存；②**材质资产序列化值覆盖 shader 新默认**——已存 .mat 里每个属性都有序列化副本，改 shader 默认值不影响存量材质；且**新属性在 shader 未导入前 SetFloat 静默失败无日志**。
**How to apply**：①改 .cginc/.shader 后先 `refresh`（或编辑器脚本 ForceSynchronousImport）再 compile，告警即净（两轮实证）；②改默认值后同步刷新所有已存材质（SetColor/SetFloat+SetDirty+SaveAssets，**写完回读断言**——静默失败防线）；新属性必须先 import 再写材质。

## 110. AI 移动骨架「驻位早退」隐含假设未验证：对角错位目标=伙伴永久挂机（2026-10-02 报障「伙伴安柏击杀敌方安柏后不攻城」）

**症状**：伙伴击杀最后一个敌方单位后与协议核心成**对角相邻**（切比雪夫 1）——攻击候选零（十字攻击线打不到对角格）+ 移动候选零（`steps=distance−偏好交战距离=1−1=0` 触发驻位早退）→ 每回合缺席站桩、永久挂机不攻城。
**根因**：`CompanionBrain.ScoreMoveCandidate` 的 `if (steps <= 0) return`（驻位即最优位）建立在「近了就能打」的隐含假设上，**未验证本单位是否真有攻击线/行动候选**——假设在对角错位、costs 门槛、射程豁口等场景全部失效；眷属脑无此坑（BFS 目标=敌格邻格，对角自然一步对齐——v4 换目标巡逻语义兜底）。
**修法**：攻击档回报 per-unit 候选存在性（ScoreAttackCandidates→bool，Offer 调用即算——**勿用 tracker.Best 判定：配额脑共享 tracker 跨单位取全场最优，Best 非空≠本单位有行动**）；steps≤0 且有候选=驻位（原语义不变），无候选=降级**对齐走位**（TryOfferAlignmentStep：BFS 首步→十字枚举序逐向试 1 格，落点地形可行+无占据+`WouldHaveFiringLineFrom` 开火线三查全过才动——纯挪动无增益不白耗体力）。
**二轮返修（同日「连续 3 回合挂机」报障，暂停态活体取证回合 19 实锤）**：首版修复对齐失败后直接 `return` 保持缺席——漏了**「首锚驻位无效应换下一锚」**。现场指纹：安柏 (9,7) 零攻击候选，首锚=核心 (12,12) 恰在偏好交战距离 5 边界 `steps=5−5=0`，四向 1 格对齐探针全换不来开火线（核心斜向远、其余敌在左上远处）→ return=永久挂机；而第二锚 Venti 距离 6 `steps=1` 本可 BFS 逼近。终版=对齐失败 `continue` 换下一锚（成功仍立即收）。
**How to apply**：改共享评分骨架时，「驻位/不动」类早退必须绑定「本单位有可用行动」的显式验证，隐含假设（近=能打/满=能放）逐个审；**降级/兜底路径失败后先问「换目标/换锚是否还有解」再 return**——首候选的无解≠全场无解；配额脑与伙伴脑共用骨架，tracker 共享性差异（全场最优 vs 单位最优）是骨架内判定最容易漂移的点。

## 111. 霜化 rim/冰雾对零 alpha 背景整幅抬升：视频路径绿幕被冻成实心霜块（2026-10-03 报障「循环动画把绿幕也算进去冰冻」）

**症状**：静态立牌（sprite 路径）冻结表现正确；循环动画立牌（ChromaKeyVideo 视频路径）冻结时整幅视频四边形（绿幕区）一起结霜，抠色背景不再透明。
**根因**：`FrostApply` 共享数学把 `FrostRim(baseAlpha)` 读作边缘带，但 `FrostRim(0)=1`——baseAlpha=0 的纯背景（绿幕/透明 padding）被误判成「边缘」，`col.a=max(col.a, rim*0.95*mask)` 抬到 0.95 不透明、冰雾 `max(col.a, mist*…)` 再叠——背景整幅变实心霜块。sprite 路径 Tight mesh 裁掉透明区从未暴露；视频路径是覆盖整帧的裸四边形，绿幕片元全中招。
**修法**：`FrostApply` 增「形状域门」`shapePresent=step(0.003, baseAlpha)`——rim 与冰雾两条 max alpha 抬升只在形状内生效；形状外仅保留 fringe 项自带的 ±4 texel 轮廓外冰缘（有意设计的冰壳外延，域天然有限）。霜色重映射/白霜斑/晶点只改 rgb，对 alpha=0 像素经 SrcAlpha 混合天然不可见，无需门。
**How to apply**：后续往 `FrostApply` 加新视觉层时，凡动 alpha 通道的项一律过形状域门（或自带有限域如 fringe）——「边缘带」类数学（菲涅尔/边缘光）对 alpha=0 的返回值恒在最大档，不能裸用于背景在场的路径；跨路径共享 shader 数学时，两条路径「背景片元是否存在」的差异（Tight mesh 裁剪 vs 整幅四边形）是默认测试盲区，霜化类验收须两条路径各过一遍。

## 112. 冻结蔓延遮罩梯度方向写反：`g` 随高度递增+「g 大先冻」=恒从头往脚扫，与「从脚往头」设计相反（2026-10-03 报障「霜化并不总是从脚蔓延到头，有时反向，疑似和朝向有关」）

**症状**：实战冻结时白霜从头顶先起、自上而下蔓延——与设计（docs/18 决策二十六「冻结遮罩从脚往头噪声扰动蔓延」）相反；蔓延仅 0.45s 且噪声扰动，观感时序不稳，用户归因到朝向（实际朝向=纯 X 镜像 `localScale.x=-1`，对世界系 Y 梯度零影响，误报）。
**根因**：`FrostMask` 的 `g=saturate((worldY−FootY+0.15r)/1.3r)` 随高度**递增**（头 g≈0.885、脚 g≈0.115），冻结条件 `g+amount≥1` 让 **g 最大者先过阈值=头先冻**——梯度定义与冻结条件组合后方向恒反。四轮截图自检全是 amount=1 定格态、FrozenTest 静态材质也是定格，方向类 bug 在定格验证下零暴露，动态蔓延首次实战目检才现形；且 cginc 内冰雾注释「脚部先冻→冰雾先起，与蔓延同向」与实现自相矛盾——注释与代码冲突时信注释查代码。
**修法**：`g` 倒序=按「离头顶距离」归一（`g=1−saturate(...)`，头顶 g=0.115、脚 g=0.885，0 缓冲带随倒序移到头顶上方——amount=1 时头顶也满）；冻结条件不变，脚部先过阈值=从脚往头。解冻随之自上而下退冰（头先解冻、脚下残冰最后化）。
**How to apply**：①「按 X 梯度显现」类遮罩，写完先代入两端点值手推「amount 微增时谁先过阈值」再定格验证；②定格态/静态截图**验证不了过程方向**——含时序语义（蔓延/生长/退避）的效果，验收清单必须含一次动态观察项；③注释与实现冲突=bug 信号，勿当注释写错糊弄过去。

## 113. 桥脚本内 PackAtlases 后对象句柄失效：同提交回读必炸 destroyed（2026-10-03 丘丘人图标批实证）

**症状**：exec_editor_script 单提交完成「改资产 → SaveAssets → SpriteAtlasUtility.PackAtlases(全部图集, activeBuildTarget) → 回读验证」，在回读段炸 `The object of type 'SkillConfig' has been destroyed but you are still trying to access it`——改动实际已 SaveAssets 落盘，崩的只是验证段，易误判为改动失败。
**根因**：PackAtlases 触发资产库整理，**此前 LoadAssetAtPath/CreateInstance 拿到的内存对象引用被销毁**（SkillConfig/Sprite 等均中招），脚本内继续持有旧句柄访问 .name 即崩；同款风险存在于任何「重操作（Pack/Import/Refresh）后触碰先前句柄」的脚本。
**修法**：①改动提交与回读验证**拆成两个 exec_editor_script 提交**——第二提交全新 LoadAssetAtPath 从磁盘加载（本例实证：第二提交全链回读全绿）；②或 Pack 后全部重载再读。附：图集页 Texture2D（ASTC 压缩页 sactx-*）不可 GetPixels（ArgumentException not readable）——skill 记载的 sp.uv 逐像素读回在压缩页上不可行时，退化验证=sp.textureRect（图集页真实区域，tight 打包区域≠256 方形属正常）+两 sprite 区域互异即可。
**How to apply**：写「重操作+回读」型编辑器脚本时默认双提交结构；单提交必须 Pack 后重载；回读报 destroyed 先怀疑句柄失效，勿怀疑改动未落盘（SaveAssets 先于 Pack 已持久化）。

## 114. 多格移动拿「BFS 首步方向」直线飞的越点偏航：对角目标左右振荡永不收敛（2026-10-03 报障「丘丘人有时候会来回左右移动，持续很多个回合」）

**症状**：眷属（丘丘人，移速 20→2 格/回合）实战中与目标成对角关系后，每回合左右往返移动、持续多回合不收敛、也不进攻击档。
**根因**：v6 多格移动拿 `FindApproachFirstStep`（只返回最短路的**首步方向**）×步数直线飞——两个越点：①**越过路径拐点**：最短路 L 形（右1+上1）时直线飞 2 格被甩离路径；②**越过目标集攻击位**：BFS goal=敌格十字邻格（就是攻击位置），直线步进飞过不停留。落点与目标重新成对角→下回合 BFS 首步指向回程→左右乒乓；挥棒射程=前方 1 格直线、对角永打不中→攻击档永不触发，振荡无自然终点。旧 magnitude=1 时代每步=BFS 精确首步（沿最短路走 1 格），无此问题——**多格化把「沿路径走」近似成「首向直线飞」即引入振荡**。
**修法**：新原语 `BattleHeuristics.FindApproachStraightSteps`（多源 BFS 自攻击位集反向扩散求「到攻击位步数场 dist」；首向=十字序首个 dist 严格递减邻格；**直线前缀**=沿首向逐格、只踏 dist 严格递减的最短路格、踏上 dist=0 攻击位即停不越过）——每回合 dist 单调递减，数学上不可能振荡；顺带抽提 `BuildApproachField` 共用逼近场构建（与 FindApproachFirstStep 同口径防双份漂移）。
**How to apply**：①「方向×步数」类多格移动**不得**拿首步方向近似「沿路径走」——拐点与终点都会被越过；要么直线前缀逐格校验（本修法），要么按路径分段；②多格移动 AI 的验收清单必须含**对角目标/拐弯路径**场景（直线追逐场景测不出振荡）；③「每回合重算」不是收敛保证——只有「每回合距离/势能单调递减」才是。

## 115. AI 单位行为观察 harness：反射眷属真实决策+评分制纯函数旁听+全 Pass 放权对局（2026-10-03 E+F 批复测实证，28 回合自动打到核心分出胜负）

**场景**：AI 决策类改动（三脑增强/支援型重构）需要验证「眷属/伙伴行为是否正常」——纯编译验证只证不炸，行为正确性要么交用户目检（慢、难归因单回合决策），要么自动对局观察。
**技法**（exec_runtime_script 全链，Play 自动进入）：
1. **开局**=`BattleLaunchConfig.LaunchSinglePlayer(null)`（P1 真人+P2 AI 配额脑就位；绕过主界面直开战斗场景——BattleScreen 有装配兜底链可裸启）；
2. **放权观察**=真人位每回合 `SubmitAction(Pass)`——自主军团（眷属+伙伴）照常运转=最干净的观察模式；
3. **眷属真实决策**=反射 `TurnFlowController._familiarUnitActions`（private 字段，BeginSelectPhase 头已定——Selecting 期间可读，与旁听同输入可互证确定性）；
4. **伙伴决策旁听**=直接调 `CompanionBrain.DecideAll(sim, turn, [P1Pass])`——评分制是纯函数（同快照恒同输出），旁听安全；**旁听口径注记**：真实伙伴脑跑在收齐全部玩家选择后（含 P2 行动），旁听时 P2 未交（0.8s 延迟）=少 P2 输入的近似，体力预留维度可能有偏差，行为方向观察够用；
5. **回合推进等待**=轮询 `flow.Phase`（Selecting→交 Pass→Resolving 片循环+ack→下一回合 Selecting）——Play 下客户端自动 ack，单回合 ~10s；**等待条件必须含 `TurnNumber > turn`**（Phase 回 Selecting 且回合号推进才算本回合完）；
6. **僵局检测**=全场总 HP 连续 N 回合不变即终止（防双方自奶打不死的死局）。
**坑（本会话实证）**：
- ①`Unit.Skills[i]` 是 BaseSkill 非 SkillConfig——`.name` 不存在，技能名经 `Skills[i].RawData.skillID`；编辑器侧同理 SkillConfig 数据体在 `.data` 字段（SkillConfig.name ✓/skillType 在 data 上）——直接猜成员名必炸，Repl 反射查真实签名；
- ②**双方同名单位撞 key**：对称测试军双方各一 Amber/Kaeya——按 unitName 聚合（死亡登记/统计）会双方混淆，按 unitId 分、展示层才映射 name；死亡登记记得报后从存活集移除（否则每回合重复报）；
- ③exec_runtime_script 的 Task<string> 轮询等待用 Task.Delay（不占主线程）——别用 Thread.Sleep；
- ④超时预算：单回合 ~10s 结算+1.6s 选择，28 回合实测 200s；timeoutSeconds 按「回合数×12s」给。
- **⑤seed 对战局无效（2026-10-03 G 批实证）**：全员幸运 0 时 `RollChance` 判定 ≤0 恒否**从不消费 RNG**——战局 100% 由确定性评分+AI 决定，**换 seed 战局逐位一致≠代码没生效**（三局同构假象的根源）；要验证行为变化，改的是评分/决策代码本身，别怀疑 seed。
- **⑥反射探针法（行为归因的标准工具，G 批三轮归因全靠它）**：exec_runtime_script 直接反射调 private 评分方法（`typeof(CompanionBrain).GetMethod("ScoreSupportCell"/"ScoreMoveCandidate", NonPublic|Static)`+Activator 建 CandidateTracker）dump 逐格评分明细/档案实值/锚延续槽（_lastMoveAnchors static 字段）/地形图（HasTile×IsPassable 逐格渲染 15×15）——比快照对比纸上推演高效一个量级（环湖 BFS 走歪/伤员自身奶程锁两案均一发定位）。**探针调用陷阱**：调 ScoreMoveCandidate 要先跑 ScoreAttackCandidates 拿真实 hasActionCandidate（传 false 会改变驻位分支行为）。
**How to apply**：AI 决策类批次的**结构化行为验证**默认走本 harness（非视觉：决策序列+快照 diff 足够判断行为正确性——画面观感仍交用户）；全量逐回合日志落盘（`File.WriteAllText`+UTF8 无 BOM）防返回截断——注意 Unity 的 `Temp/` 目录**编辑器退出时清空**，长存拷出。本节 harness 完整可抄范本=2026-10-03 会话 exec_runtime_script（战斗 AI 观察两局：8 回合抽样局+28 回合完整局）。
- **⑦Play 中再 Launch=拿不到新局（2026-10-04 H 批实证）**：上一局 Play 未 stop 时再调 `LaunchSinglePlayer`，场景 Single 重载不生效（120s 找不到新装配 session），而 `FindObjectOfType<BattleScreen>()` 秒回**旧局**（Phase=Finished）——旧 session 通过一切等待条件、for 循环 335ms 假返回。**harness/探针连跑前必须先 `unity_editor stop`**；判新局的可靠指纹=`Phase==Idle && TurnNumber==0`（装配中）而非"找到了 session"。
- **⑧伙伴 `ApproachStraightPrefix` 切比判据 vs 眷属 `FindApproachStraightSteps` BFS dist 判据（H 批伴随返修实锤）**：两者都是「BFS 首步直线前缀防歪」但判据不同——前者按**到目标切比雪夫距离**严格递减截断，**平行绕行段（沿湖岸平移、切比距离不减）恒 0 前缀=该锚直接废**；后者按**多源 BFS dist 场步数**递减（平行段 BFS 步数照减=可走，眷属 v6.1 绕湖实测通过）。**「绕行到目标旁」的场景一律用 FindApproachStraightSteps（goals=目标十字邻格集），切比判据只适合直线可直达的防歪截断**。H 批 T4 探针现场：朝先锋方向全湖、直线射线只剩平行格（评分同分不动）+BFS 首步 Left 方向正确但切比前缀 0 步=双重卡死；换 FindApproachStraightSteps 后 T4-T5 两回合绕行到伴随位。**2026-10-04 I 批回填补遗（教训=同族修复须全消费方扫描）**：H 批返修时只改了支援伴随路径，**锚循环（输出型主路径）的同一消费点漏改**——集团拥挤场景 BFS 首步=侧移绕行、切比不递减=前缀恒 1 格=「凯亚开局只走 1 格、移速 30 没发挥」报障（距 H 批仅隔一天）。锚循环本批已换 FindApproachStraightSteps，`ApproachStraightPrefix` 全生产消费方清零=退役保留+标注勿新增消费方。**判据：修一个判据型原语的缺陷时，rg 它的全部调用点逐一评估同病灶**——只改报障路径=给下一个消费方埋雷。
- **⑨探针取评分现场的完整姿势**（一轮定位）：反射 private static 评分原语（`ScoreSupportCell`/`FindApproachFirstStep`/`IsCellOccupiedForStep`/`ThreatPenaltyAt`，威胁图用 `OpponentThreatModel.Build` 现场重建）对关键候选格逐格 dump 总分+火线扣分，配射线逐格「通/占/距」表——此前推演半小时猜错方向，探针一发实锤根因（平行线+同分+严格大于=不动）。
- **⑩旁听 DecideAll 污染 B 侧决策（2026-10-04 J 批实证）**：Selecting 期反射调 `CompanionBrain.DecideAll(sim, turn, [P1Pass])` 旁听——A 侧（真人全 Pass 输入完整）决策与真实一致；**B 侧因旁听少了 P2 玩家行动=配额池计算漂移→B 侧行为改变**（j3 旁听局 B 芭位置 vs 净观察局漂移 1 格）。旁听局适合归因 A 侧；对照净观察局（只提交 Pass 不旁听）拿 B 侧真实行为。旁听的槽写入（_lastMoveAnchors 等）与真实执行同回合覆写=无害。
- **⑪观察脚本三坑（2026-10-04 J 批实证）**：①调试军单位 UnitID=U1~U12 **不含角色名**——按 UnitID 硬编码识别（U4=凯亚 U5=芭芭拉），IndexOf("aeya") 恒 false（汇总行静默丢失）；②脚本宿主 `System.IO.File.WriteAllText` **被裁剪**（CS0117）——写文件用 `StreamWriter(path,false,new UTF8Encoding(false))`；③旁听后留下的活局若停在 Selecting，探针接管窗口有限——Selecting 有超时自动 Pass，接管脚本要即到即探。
- **⑫快速 Pass 纪律+探针同步块（2026-10-04 用户拍板「快速pass，这样就不用等选择阶段的时间」）**：观察局每回合一进 Selecting **立刻先交 P1 Pass**——P1 先交后阶段只等 P2 的提交延迟（阶段收齐即开演，TryBeginResolve 不等计时器）；探针反射调用全部放 Pass **之后**且整块**同步零 await**（主线程原子性=Resolve 协程/计时协程无法中途插入，探针天然安全），DecideAll 旁听放块尾（槽写在所有读取之后）。反面实证（同日 probe 局）：探针块塞在选择窗内+await 分段=块跨越 Selecting→Resolving 边界，同帧双读出现位置自相矛盾（alive 列表与 Snap 不一致）——探针数据整段作废。另：自动 Pass 本体正常（T15→T19 连续自动推进实测）——编辑器失焦时 Play 更新挂起、倒计时冻结=观感「不动」非 bug；选择时限公式=FirstTurn 25s/T2-6 16s/后每回合 −0.5s 钳 8s（TurnFlowController.GetSelectLimitSeconds）。

## 116. 评分距离度量与移动几何错配：切比雪夫平坦区+严格大于平局=系统性少走（2026-10-04 芭芭拉追随三局实锤，docs/18 决策三十八）

**场景**：评分维度用**切比雪夫距离**（奶程梯度到锚/治疗半径）而移动是**十字直线**——锚在单位非行进轴上横向偏移 ≥2 时，沿射线方向的切比距离先降后平（y差缩到=x差处冻结，行进不再缩短切比），该维分数进入**平坦区逐格同分**；叠加选格循环「严格大于才换格」（平局判给先找到的更短格）→ **每回合系统性少走 1 格**（移速预算静默丢弃）。芭芭拉随军追随：先锋凯亚 3 格/回合 vs 她有效 2 格/回合=距离永不收敛（用户报障「离凯亚较远仍只走 2 格、30 移速没发挥」）。同族变体：入半径平局（run_k 与 run_k+1 都在治疗半径内=同 20 分→取短）、全平坦（三格同 20 分→取 run1）。
**探针定位法（手推两轮失败后一发实锤）**：反射 `ScoreSupportCell` 对射线候选格逐格打分 + `TryOfferSupportPosition`（CandidateTracker 公有可 new）按假设锚（声明落点/外推/当前）分别复刻 offer——T3 现场 Up run2=26 / run3=26 **精确平局**且复刻 offer=Move Up mag=2 与真实行为逐位吻合。教训：评分用与移动几何不一致的距离度量时，先逐格打分验证「分数沿移动路径单调」假设——平坦区=平局=行为由平局规则决定。
**修法**：选格循环 `score > bestScore` 扩为「**同分且同向且 run 更长**也换格」（同向限制=跨向平局语义不变、auraBest/coverBest 跟踪器不触碰=最小面）。
**同族第二病灶（寻路）**：`FindApproachStraightSteps` 旧版首方向=十字序**首个** dist 递减邻格、不比较四向直线段长度——选中向 1 步即拐点/撞地形时富余预算整段丢弃（凯亚 T3 实证：Right 递减段 1 步撞水、Up 向更长，枚举序却取 Right=整回合只走 1 格）；修=四向各算严格递减直线段取**最长**（同长保持十字序；每格仍严格递减=单调收敛防振荡性质保持）。
**边界（非病勿修）**：绕行拐点处的剩余步丢弃（Left 2 后第 3 步无处直走）=单方向移动（推力直线）语义固有，L 形移动=游戏模型级变更。
**判据**：①修「格间无差别」类缺陷时，同步审计全部选格/选向循环的平局语义（严格大于 vs 最深 vs 最长）；②距离度量与移动几何必须同构（切比雪夫配八向、或给十字移动用轴向度量/单调递减分），否则平坦区平局交给 tie-break 拍板——而 tie-break 必须显式设计（本轮「严格大于」即隐性拍板给了最短格）。
**补记（同日二犯·穿占虚 Offer 族，T36 安柏撞尸实锤，docs/18 决策三十九）**：**地形-only run 计数循环**（只查 HasTile/IsPassable、不查占据）= offer 落点/路径含不可停格=虚假目标——执行器撞占停格零位移、下回合同决策=**永久撞尸循环**（安柏 E-3 锁温迪、Up 对齐 offer Up5 首格即敌芭尸体）。全收口清单：`TryOfferAxisAlignStep`（轴对齐）+真无解兜底走满（本批修）；支援射线（J 批四刀④已修）；`FindApproachStraightSteps`/`TryOfferAlignmentStep` 本就占位感知；`ApproachStraightPrefix`=退役零消费方豁免。**判据升级（§115 坑⑧同族二犯）：修「检查缺陷」类原语病灶时，rg 全部同型循环**——本次=`for s<=cap/maxSteps` 地形-only 走行族——只修报障路径=同型循环给下一个场景埋雷。另：飞行单位（Amber_FlyingChampion，NormalMoveType=Fly）越水可走——按 Walk 渲染地形图对飞行单位失真，取证时按移动者本人 forceType 逐格判。

## 117. hub 双指晋升路径：第二指 Began 全量投递让刚取消的 Immediate 拖拽同帧复活、挤掉等第二指的捏合（2026-10-04 手机端报障「无法在大地图，战斗地图上两指缩放」）

**场景**：真机双指捏合永远起不了手，第二指落下反而触发一次全新的单指拖拽（相机乱飘）。桌面鼠标单指针永远不走该路径；P1 合成事件流断言**直喂识别器**绕过了 hub Dispatch——双指晋升+仲裁从未被端到端验证，两层测试盲区叠加把 bug 藏到上手机才暴露。
**根因**：GestureHub.Dispatch 对第二指 Began 先 `CancelSinglePointerGestures`（双指取代单指），但同一事件随后仍全量 Deliver——刚被 ForceCancel 复位回 Idle 的 `DragRecognizer(Immediate)` 把第二指当**全新序列**重新 SetBegan 宣胜，仲裁把正等第二指的 PinchRecognizer（_id0 已登记 f1）ForceFail 清簿记 → 捏合死在 Possible、f2 变单指拖图。次要耦合：`OnRecognizerWon` 单指宣胜 fail 全部识别器（含多指），Map 靠 [Drag,Pinch] 列表顺序侥幸存活（drag 宣胜瞬间 pinch 尚 Idle=ForceFail 空操作）——识别器集合顺序隐性决定捏合存亡。
**修法**：①第二指 Began 改走 `DeliverToMultiPointer`（只喂 MaxPointers≥2 的识别器）——单指识别器被取消后不得被同帧复活；第二指后续 Moved/Ended 仍全量投递（Idle 识别器对陌生指针自然忽略）；②`OnRecognizerWon` 单指宣胜豁免多指识别器（多指晋升由 hub「双指取代单指」规则专管，与列表顺序无关）。修后离线反射 Dispatch 打合成流断言 12 项全过（主链 8 项/反序面 2 项/鼠标零回归 2 项——hub 层也可合成断言，不止识别器）。
**判据**：①「取消型投递」与「同帧新事件」共存的调度点必须自查：被取消的接收方会不会把新事件当全新序列复活？②纯 C# 组件的合成断言尽量从 hub/组合根层注入事件流——直喂子组件会跳过调度逻辑制造盲区（P1 直喂识别器=本陷阱潜伏三周的代价）；③「某平台某功能不可用」类报障先确认功能**是否接线**再查回归——战场捏合此前从未接线（docs/24 §5 原表 Battle 只有 Drag），手机无滚轮=战场缩放整个不存在，属缺件非回归。
**附**：BattleCameraController 同批补 PinchRecognizer（捏合=中心缩放、注视点不动——与滚轮 2026-09-12 拍板「不锚定指针」同口径，不像大地图锚定捏合中点；直写实际距离并同步目标，不污染滚轮平滑链）。
## 118. Edit Mode VideoPlayer 编辑器预览五坑：失焦深度冻结/构建上下文 Play 丢/播完态怪/Blend Blit 叠影/GUILayout 逐次叠排（2026-10-05 时轮校准预览批实证）
**场景**：时轮编辑器内嵌动作片校准预览（双 VideoPlayer→RT→ChromaKey Blit→IMGUI 画布）。第一轮「播放放出但每帧叠加多个安柏」；第二轮「第二次播放无反应」；CLI 自动验证多次假阴——全部指向编辑器模式 VideoPlayer 的环境依赖行为。
**根因五连**：①**编辑器失焦=WMF 泵深度冻结**——不止不推进：frame/time 的 getter 恒 -1/0、setter 静默失效（prepared=True 也瘫），InteractionMode=1+QueuePlayerLoopUpdate 都救不动；恢复前台即续播、API 复活。**含 frame/time 判据的逻辑只能前台目检**，CLI 反射断言在失焦下必假阴；②**窗口构建上下文（CreateGUI/RefreshAll 链）中调 Play() 请求可能被引擎丢弃**（frame 恒 -1 不起播）——正解=EditorApplication.update 里自愈重试：isPlaying=false 且 frame<0（从未起播判据）时再 Play()；③**isLooping=false 播完 isPlaying 不恒翻 False**（Tuanjie WMF 怪态：卡末帧 isPlaying 可恒 True）——播完判据用 frame>=frameCount-1 双通道；**自然停处理必须显式 Pause()**（怪态下不 Pause 则下次 Play() 是 no-op=「第二次播放无反应」根因之一）；**播放按钮从末帧重播必须先 frame=0**（播完态 Play 从当前位续播——1.5s 片只续 0.04s 观感无反应=根因之二）；④**带 Blend 语句的透明队列 shader 拿去 Graphics.Blit 到不清零 RT=逐帧叠影**——ChromaKeyVideo 的 Blend SrcAlpha OneMinusSrcAlpha 让源帧按 alpha 混合叠加到目标已有内容（绿幕透明区盖不掉旧帧主体→「同位置完全不透明叠加像没删上一帧」）；战斗无此问题：该材质只挂 quad 混到每帧清屏的屏幕、VideoPlayer→RT 是覆盖式渲染——**Blit 前必须 GL.Clear 目标 RT**；⑤**IMGUIContainer 内 GUILayoutUtility.GetRect 在视频播放高频重绘下 rect 逐次下移**（每帧多画一份内容竖直叠排）——预览画布一律固定 rect，勿走 GUILayout。
**修法**：update 自愈重试（②）+frame>=frameCount-1 播完分流+显式 Pause+末帧回零（③）+BlitPreview 前 GL.Clear(false,true,Color.clear)（④）+固定坐标画布（⑤）。
**判据**：①编辑器侧视频预览的验证计划先确认前台依赖——用户目检为主，CLI 断言只做非 frame 判据项；②共享材质第一次被用于新渲染路径（quad→Blit）时检查 Blend 语句对新路径的语义；③IMGUIContainer 里反复重绘的自绘区域用固定 rect。
**附**：EditorWindow 私有字段（无 [SerializeField]）域重载即丢——外部（AI 脚本）改资产后必须显式调窗口刷新方法（LoadAsset 模式），否则 UI 显示构建时快照与真实数据脱节（用户在旧快照上拖拽把 clip 拖歪实证）；Tuanjie UITK IMGUIContainer 无 Repaint()——用 EditorWindow.Repaint() 窗口级重绘连带画布。

## 119. 场景切换类启动双条件+跨端覆盖字段必须进快照（2026-10-05 试招沙盒批实证）
**场景**：时轮编辑器「开一把试招」按钮：Edit 态点→EnterPlaymode→自动进战斗。三连报障：①直接 EnteredPlaymode+delayCall Launch→停在大厅；②等 MainHall sceneLoaded+delayCall Launch→仍停大厅；③进战斗后战技点不动+提示「伙伴技能自主」。
**根因**：①进 Play 初期 Boot 期框架初始化/Splash 流程在跑，过早 Launch 失败/与开机流程竞速；②MainHall sceneLoaded 时**Boot→大厅的转场揭幕动画还在播（SceneTransition 输入锁未释放）**——GameScene.LoadSceneWithConfig 开头 `if (InputLocks.HasLock(SceneTransition)) { Warn("场景正在切换中"); return; }` 把 Launch 静默丢弃（console 栈实锤）；③Unit 实例上新增的操控层级覆盖位（TierOverrideStars）只活 Host 侧——客户端 HUD 只见快照，层级门控 5 处按原始星级判定=3★ 伙伴档拦截战技。
**修法**：①+②=场景切换类启动必须**双条件齐备**：目标场景 sceneLoaded 完成+**轮询 InputLocks 无 SceneTransition 锁**（转场揭幕结束）再 Launch（30s 超时兜底+退 Play 清理排队标记防幽灵启动）；③=层级随快照走：UnitState 新增 tier 字段（Host BuildUnitState 从 TierOf 填充），HUD 全部层级门控消费点改读快照 tier（0=未填回落原星级换算）。
**判据**：①切场景 API 有前置锁/状态检查时，调用方必须等到锁释放态而非只等场景加载事件；②**跨端（Host→客户端）需要感知的覆盖/修改类字段必须经快照传递**——Unit 实例字段客户端不可见，UI 门控按原始数据拦截的报障先查快照缺字段。

## 120. 编辑器预览与运行时摆放数学必须同构：底边锚定 vs 中心锚定+比例尺 2 倍错=校准工具系统性说谎（2026-10-05 箭高报障「预览线贴弓、实机箭过头顶」+「箭和立牌不在一个平面」）
**场景**：时轮编辑器箭高线校准到 0.87 贴弓（预览里完全对齐），实机箭矢却从弓上方 0.26 格飞（帽顶 vs 动画箭脸部）；同日另一报障「箭矢和自己立牌不在一个平面上」。
**根因**：双错位叠加——①时轮预览帧摆放按**底边锚定**（帧底=位置偏移），运行时 UnitView.PlayActionVideo quad 是**中心锚定**（中心=基准位 0.55+偏移、缩放绕中心，帧底面内=0.55+offset−0.55×comp），实机帧比预览低 0.16；②预览比例尺 pxPerWorld=idleH/0.55（把立牌全高当 0.55，实际=0.55×avatarScale 2=1.1）=偏移量被 2 倍缩放。同一 0.87：预览贴弓、实机过顶。③「不共面」根因=八轮「格心 xz 起飞+屏幕解算 y」把箭放在格心深度，立牌面在该高度前移 sin55°×面内高≈0.5 格——箭全程在立牌前方半格飞。
**修法**：①预览与运行时严格同构——帧改中心锚定（帧底面内=0.55+offset.y−0.55×comp）、pxPerWorld=idleH/1.1；②箭高线改**绝对面内高**（地面线起 arrowY×pxPerWorld，不随帧动），与运行时 ProjectileOriginWorld 消费语义 1:1；③箭矢改**全程贴立牌面飞**：起点=ProjectileOriginWorld 面内弓位点本体，终点 z 同加面前伸量——两端同面同深（六报斜线根因=当年「面内起点+格心终点」两端深度差；两端同深后东西向屏幕水平），SolveEqualScreenHeight 屏幕解算退役删除；④Amber_DoubleShot 箭高 0.87→0.61（ffmpeg 抽两发射击帧+行计数像素测量动画箭帧内 58.94%→面内 0.606；两发共用一条 LineProjectile clip 的 hitInterval，单值天然管两发）。
**判据**：①**校准/对齐类编辑器预览的摆放数学必须与运行时逐式同构**——锚定方式/比例尺/基准位任一不一致，预览就在系统性说谎，用户在错误标尺上读出的值实机必错；②2D 面片上的贴面特效：面内起点+同深终点=共面且不斜，「面内起点+格心终点」混合必斜；③对齐类参数的验收值用像素测量定（抽帧+掩码行计数），勿靠肉眼估读。

## 121. 首用黑屏≠资产未加载：VideoClip 常驻但解码器首开 ~100-300ms+新 RT 恒黑（2026-10-05 决策四十五，用户报障「首次移动/飞行安柏黑屏」）

**场景**：安柏首次移动（fly 循环片）/首次战技施放（double_shot）时立牌黑屏一瞬，同局第二次起正常；用户直觉判定「资源没提前加载，该走统一预载（AssetCache）」。
**根因**：两层拆解后真因与直觉不同——①视频**资产**（VideoClip 对象）是 Config 直引用，随 UnitConfig/SkillConfig 加载即常驻，不存在「首次加载」；②黑的是**解码器首开**：VideoPlayer 对新 clip 首次 Play 要开文件+建解复用+解码首帧（WMF ~100-300ms），期间素材 quad 显示的是刚分配的动作片 RT（**新 RenderTexture 恒黑**）——待机片共主 RT 换片无此象（RT 留上一帧），动作片双 RT 路线每片独立 RT 才显形。
**修法**：建场预热——CreateView 收集本单位全部技能动作视频（含 Move 移动片）→ UnitView.PrewarmActionClips：per-clip RT 池+临时 VideoPlayer `Prepare()` 预解码**首帧渲入池**（5s 超时兜底回落旧行为）；PlayActionVideo 按片取 RT=预热命中带首帧零黑屏。RT 单槽→Dictionary 池化顺带消除异尺寸片交替播的重建浪费。
**判据**：①**「AssetCache/Addressables 预载」治不了解码器**——它管资产驻留（大贴图/立绘域），VideoPlayer 首开延迟只能靠 Prepare 预热或预渲首帧；诊断「首次卡/黑」先分层：资产驻留（Config 直引用=已驻留）vs 解码首开 vs RT 初始内容；②**新分配 RenderTexture 恒黑**是常被忽略的第三因素——凡「换 RT 渲染」的切换，切换瞬间显示的都是 RT 旧内容/黑，预渲一帧入池即可无缝；③预热协程必带超时兜底（Prepare 可能挂死，预热失败应回落旧行为而非卡建场）。

## 122. 悬空面片点击视差（平面取格对 2D 立牌失准）+ ShaderLab `[Header]` 属性连字符解析崩（2026-10-05 决策四十六批实证）

**场景**：①用户点角色立牌（尤其上半身）经常选不中/选到身后格——立牌是 55° 后仰的 2D 面片，屏幕上悬空覆盖身后格；②给 shader Properties 加 `[Header(Selected Outline 2026-10-05)]` 装饰行后 `unity_shader.compile` 报 `Parse error: syntax error, unexpected $undefined, expecting TVAL_ID or TVAL_VARREF`。
**根因**：①OnBoardTap 用「射线交 y=顶面平面→取格」拾取，对**悬空面片**天然失准——点击落在面片上半部时视线早已越过自身格（俯角下漂移 h/tan(俯角) 与 §86 地块顶面视差同族，但面片悬空高度更高、漂移更大格数）；②ShaderLab 属性抽屉 `[Header(text)]` 的 text 含连字符/特殊符号会炸解析器（property drawer 属性语法远比 C# Attribute 窄）。
**修法**：①**屏幕空间面片命中**——立牌面片四角（视频 quad 实时 transform/静态 localBounds）→ 各角 `WorldToScreenPoint` → 凸四边形点内测试（全 cross 同侧 ±1px 容差，任一 winding）→ 多面片重叠取离相机最近者；命中则点击目标=该单位+其所在格，未命中回落平面取格。**凡「点击 2D 面片状物体（立牌/布告板/卡片）」的拾取勿走地面平面取格**——面片视觉格与判定格天然错位，屏幕四边形测试零物理成本且透视正确；②shader Properties 分区用普通注释，`[Header]` 只用不带特殊字符的单词。
**判据**：①「点击立牌无反应/选错人」先查拾取是平面取格还是面片命中（平面取格对面片=系统性错位，越靠面片顶部错得越远）；②shader 解析错误行号落在 Properties 块且报 `$undefined/TVAL_ID`——先查装饰性属性（[Header]/[Space]/[Enum] 等）里是否混了连字符、括号、冒号；③**unity_shader.compile 对刚改的 .shader 报错可能是 SourceAssetDB 陈旧态**（磁盘改动未经 refresh 导入）——先 `unity_editor.refresh` 再 compile 才是干净判据（本批实证：同错误 refresh 后消失，与 §109 .cginc 同族）。

## 123. 透明排序实证：sortingOrder 支配 renderQueue——「queue 2999<3000 所以先画」是幻觉（2026-10-06 底座盘「沉水」报障根因）

**现象**：飞行单位站水格上，底座圆盘看起来沉到水面之下（被水波盖住冲刷）——但几何推演全对：盘=视觉表面（水面 0.36+波峰 1.5×振幅 0.02+余量 0.01）+0.02=0.42，恒高出波峰（0.39）0.03。
**根因**：**Unity 透明渲染序=SortingLayer → SortingOrder → renderQueue → 距离**（order 支配 queue；像素回读实验实锤：A=queue3000/order-1/红 vs B=queue2999/order0/绿 共面叠放→渲染结果纯绿）。焊接水面 MeshRenderer sortingOrder=0（默认）> 盘 sortingOrder=-1 → **水面后画、整片盖在盘上**（含波峰高光），盘被水冲刷=「沉水」观感。旧认知「盘 queue3000>水 2999 故盘画于水面之上」（UnitView/§92 注释遗留）从排序规则上就不成立——此前无人察觉只因极少有单位真正站上水格（飞行跨水=决策三十九后才常见）。
**修法**：焊接水面 `meshRenderer.sortingOrder = -2`（BattleBoard.BuildWaterSurface）——水面恒为最低透明层（画于盘 -1 之下、瞄准贴片 0/立牌 10/箭矢 12 之上），显式实现 2999 队列的本意；勿"修"回 0。
**判据**：①**凡跨 renderer 排透明序一律显式用 sortingOrder 排，勿依赖 renderQueue 相对大小**（queue 只在 order 相同时才参与比较——本实验把 3000 vs 2999 的"先后"直觉直接推翻）；②「贴片沉到水面下」类报障先分层：几何高度（视觉表面含波峰带 §89）vs 画家序（order 对比）——两者都可独立致"沉水"观感；③共面双 quad+像素回读=排序规则的最小定裁实验（勿凭文档/记忆断言排序规则）。

## 124. exec_editor_script 同帧「OpenScene 真重开+AddComponent+SaveScene」静默丢组件——挂载与保存必须拆两次脚本调用（2026-10-06 CameraContextAnchor 三场景挂载实证）

**现象**：编辑器脚本循环处理三场景（OpenScene(Single)→主相机 AddComponent→MarkSceneDirty→SaveScene），三个场景全部报保存成功（SaveScene 返回 True、保存后内存里组件在），但重开 MainHall/MapScreen 组件消失；唯 BattleScreen 存住——它当时是编辑器活动场景，OpenScene(同路径) 实为 no-op（场景未真重载）。
**根因**：团结引擎 1.9.3 下 **OpenScene 真重开后、同一脚本调用内 AddComponent 的组件未进场景序列化集**：AddComponent 后 `scene.isDirty=False`（组件添加不自动标脏），MarkSceneDirty+SaveScene 虽返回 True 但写盘内容不含该组件（单场景最小复现：挂→存→重读 count=0 实锤）。活动场景（no-op 重开）路径正常——场景处于稳定加载态时 AddComponent 可正常序列化。
**修法**：**挂载与保存拆成两次 exec_editor_script 调用（跨帧）**——第一次：OpenScene+AddComponent（组件留在内存场景，不保存）；第二次：EditorUtility.SetDirty(组件)+MarkSceneDirty+SaveScene+重开断言。三场景（MainHall/BattleScreen/MapScreen 挂 CameraContextAnchor）两轮全部 PASS。
**判据**：①编辑器脚本给「真重开的场景」AddComponent 后必须**重开断言**落盘（SaveScene 返回 True ≠ 组件已写盘）；②AddComponent 后 `scene.isDirty` 读到 False 即中此坑（正常应自动标脏）；③对活动场景（OpenScene no-op）同帧挂存不受影响——同批三场景一成一败两丢的指纹即此差异；④prefab 实例身份不是本案因素（三相机均非 prefab 实例仍复现）——勿先往 prefab override 方向排查。

## 125. 「每帧恒喂点」系统的取消竞态：取消必须清目标位而非只停动画——进行中的补间不再看喂点，只看激活位（2026-10-06 拖动瞄准取消后相机「瞬移回原位又被拉过去」报障根因）

**现象**：拖动瞄准中取消技能，相机可见地瞬移回瞄准开始位，随后又被平滑拉回取消前的目标位（用户初判「协程没清理干净」——实际是 Update 状态机+补间残留，病灶同族）。
**根因**：喂点方（HUD.Update）**每帧恒调 `SetDragFollowTarget`**（拖动中喂金格、退出后喂 null——「退出后喂 null」看似天然收口）；取消方（onEndDrag/onClick 回调）调 `CancelDragFollow` 瞬移+停补间 `_followTweenActive=false`，**但没清喂点目标 `_dragFollowWorld`**。当取消回调与同帧 HUD 喂点竞争（喂点先于取消）：取消后相机 Update 的跟随块读到的目标仍是本帧喂的旧格 → 重新发起补间（`BeginFollowTween`；同目标续跑守卫挡不住——active 已被取消清掉）；下一帧 HUD 喂 null 时，**进行中的补间只看 `_followTweenActive` 不看目标位**，完整跑完 0.5s 把相机拉回旧格。
**修法**：`CancelDragFollow`/`EndDragFollowSession` 均加 `_dragFollowWorld = null`——取消/终结=**目标位+补间双清**（End 同族隐患一并收口：提交帧同款竞态会把留位相机拉走）。
**判据**：①「取消重置」类功能在「每帧恒喂点」系统里，取消动作必须**清喂点目标**（消除同帧竞争窗口），不能只停动画位、指望下一帧喂 null 兜底；②进行中补间/动画**只看激活位**——喂点方喂 null 停不了已激活的补间，收口责任必须在取消侧一次做全；③症状指纹「重置生效了（瞬移可见）→又被持续力拉回去」=重置动作与残留驱动并存——先查哪个状态位还在驱动（目标位/激活位逐个对账），勿只查协程。

## 126. 多帧协程钉世界坐标 Y vs 并行动画的父级：子件本地 Y 永久漂移——「只动 X」也必须写完世界位后把本地 Y 锚回（2026-10-06 背包选中线「跑到标签上面」报障根因）

**现象**：背包顶部标签的共享选中线渲染在标签上缘（用户暂停报障「线条跑到标签上面去了」）。取证：线本地 anchoredPos.y=-30.37，prefab 序列化值=-130.34（贴 TopPanel 底缘、标签下方——**全部 git 历史恒为该值，非资产问题**）；世界 Y=1402.03=静止位+偏移 100×1.25−0.04。
**根因**：`SlideLineCoroutine`/`SnapSelectLineTo` 滑线时 `position = (x, 自身世界Y)` **钉死世界 Y**；而 GlassPanelAnimator 的入场/退场动画把父级 TopPanel 当内容元素移动（从上偏移 100，0.2s）。开面板后入场动画期间点了标签 → 滑线每帧把线钉在父级位移态的世界高度 → 父级滑回静止位时线没跟上 → **线的本地 Y 永久漂移 +100**（-130.34→-30.34，正好压到标签上缘）。池化面板实例不重建、无任何代码再写本地 Y → 漂移全程驻留（与 §39 目标位缓存污染同族：表现层错位的真因都在「世界位与本地位在父级运动中脱钩」）。
**修法**：滑线收口为唯一写入口 `SetLineWorldX(worldX)`——世界位写 X 后**立即把 anchoredPosition.y 锚回写前的本地值**（X 走世界坐标对齐标签，Y 恒为本地不变量）；snap 与协程逐帧/末帧全部走它。
**判据**：①多帧动画写子件世界坐标时，先问「父级此刻会动吗」——毛玻璃入场/退场（GlassPanelAnimator 内容元素）就是会动的父级；「固定 Y」写法在父级静止时恒对、与父级动画重叠时必漂；②症状指纹「子件相对父级永久错位、且错位量恰=父级动画偏移量」=世界位钉死与父级动画脱钩，**按偏移量对账可一发定位**（本次 +100=TopPanel 从上偏移）；③池化面板里这类漂移不会自愈——「只在开面板头 0.2s 出现过一次」的错位会驻留整个会话，报障时刻的现场值≠触发时刻；④取证时本地值与 prefab 序列化值对不上、git 全历史又是恒定值 → 必是运行时写坏，直接找全部写该 transform 的代码路径。

## §127 Viewport Mask 遮罩图形 alpha=0 → Mask 下全部子级隐形（2026-10-07 背包全部数据面板实证）

**症状**：ScrollRect 的 Viewport（Image+Mask）之下全部内容（15 行/3 分区标题/文字）一像素不出——Mask 之外的兄弟节点（全屏暗遮罩底图、× 按钮）正常出画，用户只见半透明背景；对象层检查全绿（activeInHierarchy=True/组件 enabled/crAlpha=1/GraphicRegistry 在表/世界四角正确/材质 Default UI/同 Canvas/无 CanvasGroup 遮蔽），registered 却不渲染。

**根因**：Mask 的模板（stencil）写入依赖**遮罩图形实际出画**——`Image.color.alpha=0` 的遮罩图形在这版 ugui（com.unity.ugui 1.0.0）不写 stencil → 其全部子级模板测试恒失败 → 整块内容隐形。桥脚本程序化建 Viewport 时随手 `color=(1,1,1,0)` 埋雷；**全项目 20+ 正常 Viewport 全为 alpha=1，此为唯一孤例**。

**修法**：Viewport Image alpha 置 1（Mask showMaskGraphic=false 保证遮罩底图自身不可见、观感不变）。

**取证教训五条**：
①「父级图形渲染、整棵子树隐形」类报障，先查子树内有无 Mask/RectMask2D，再查 Mask 遮罩图形的 alpha——对象层一切正常时这就是第一嫌疑；
②**二分探针法**定位层级诅咒：运行时在被怀疑父级的直下（Mask 外）与深层（Mask 内）各造一个纯色 Image 探针+录屏像素判定——Mask 外 42298 红像素渲染/Mask 内零渲染=Mask 诅咒实锤（本坑最终定位手段）；
③**像素测量必须 Y 翻转**：世界坐标 Y 向上、截图 Y 向下，canvasY→captureY=(H−Y)×scale——漏翻转=全程量镜像区域，本坑排查曾因此连环误诊一小时（把 Mask 子级隐形误判为「渲染序反转/父遮罩吞子树」并做了多轮无效现场修复）；
④识图 VLM 对录屏的结构描述不可靠（同一录像既报「有全屏暗遮罩」又漏报 15 行列表）——内容判定以受控像素测量+用户目检为准；
⑤exec_runtime_script 会**自动恢复暂停中的 Play**（与 exec_editor_script 退出 Play 相对）——暂停态取证后游戏处于运行态，后续录屏对的是移动靶；取证前先向用户声明后果。

## §128 背包数据面板美化批三陷阱：入场动画快照时机 / UGUI 点击冒泡 / 程序化渐变方向（2026-10-07 会话 AW 实证）

**①入场动画快照 VLG 子级布局位前必须强制排布局**
症状：全部数据面板首次打开 15 行全部叠死在同一位置（用户报「排版非常不正常」）。
根因：`SetActive(true)` 同帧内入场协程快照 `anchoredPosition`——VerticalLayoutGroup 本帧还没跑，快照抓到的是 prefab 序列化位（全 (0,0)）；动画收尾 `FinishEntrance` 把行「复位」到快照目标=全叠死。
修法：快照前 `LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)scrollRect.content)`。
参照物：DeckSwitchPanel 无此坑全靠其刷新链尾部 `SyncScrollbarSize` 内置的强制布局——**移植别处的动画模式必须连它的布局前置一起搬**，只搬协程必踩。

**②UGUI 点击沿 transform 向上冒泡——「点数据块之外关闭」的根 Button 会接住块内点击**
症状：面板根 Button 与遮罩 Image 同体做「点外关闭」，点击数据行也触发关闭。
根因：想当然认为「行 Image 拦住射线=父级按钮不触发」——实际 ExecuteEvents.ExecuteHierarchy 自命中对象**向上冒泡到首个 IPointerClickHandler**，根 Button 正是那个处理器。
修法：新通用组件 `UI/Common/PointerClickEater`（空 IPointerClickHandler）挂 ScrollView——块内点击（行/行间/标题）在 ScrollView 层被消费，根 Button 只接块外遮罩点击。**凡「点外关闭+根 Button」结构必配挡板**；或像 DeckSwitchPanel 用独立 backdrop 子物体（非内容区祖先）。

**③程序化生成贴图方向必须像素回读验证，勿按构造想当然**
症状：panel_fade 垂直渐变「上暗下浅」交付后实际上面更浅。
根因：生成循环按 y=0 当「顶行」写最暗——**Unity Texture2D 原点在左下角，y=0 是底部**。
修法：翻转重生成（底→顶），并加 ImageConversion.LoadImage 读首行/末行 alpha 断言方向。**教训与 §127③同族：一切含空间/方向语义的程序化产物，验证必须落像素回读，「我按 X 顺序写的」不是证据。**

## §129 DeckSwitchPanel 拖拽实时让位两陷阱：禁布局组件须连 CSF 一并冻结 / 运行时 anchor 勿按 prefab 资产假设（2026-10-07 切卡组面板拖拽重排实证）

**①禁用 LayoutGroup 期间同对象上的 ContentSizeFitter 必须一并禁用——否则 content 高度坍缩**
症状：拖拽重排（让位动画接管行布局，临时 `VLG.enabled=false`）后，把列表滚到中下部再拖最底行，列表「瞬间滚动到最顶上」；未滚动状态下拖任意行无异常。
根因：`Rows` 上是 VLG+ContentSizeFitter（verticalFit=PreferredSize）组合——**CSF 的高度来源正是 VLG（ILayoutGroup 的子项 preferred 总高）**。VLG 被禁用后 CSF 在下一次布局周期拿不到尺寸来源，把 content 高度坍缩为 0；ScrollRect 检测 content 尺寸骤变，把 `verticalNormalizedPosition` 钳回顶（列表在未滚动态时 normalized 本=顶，塌缩无视觉变化——所以只在「滚下去再拖」时显形，极易漏测）。
修法：接管布局时**双禁**（VLG+CSF 同禁=content 高度冻结、rect 不重算，行 anchoredPosition 由动画驱动不受 rect 影响），恢复时双恢复；恢复后布局重排结果与让位终点一致零跳变。活体断言：拖底行 content 高度 Δ=0、滚动位 Δ=0。
通则：**凡运行时禁用布局组件做手动接管，同对象上的尺寸适配器（CSF）必须一并冻结**——两组件是「排布+量高」一体链，禁一留一必产连锁。

**②运行时 RectTransform 的 anchor 可能与 prefab 资产值不一致——换算基准必须按实例实读**
症状：拖拽让位首版三症状同源：拖到两卡组之间无让位、松手行飞向屏幕右下角、随后瞬移回原位。
根因：`anchorRef`（行 anchor 参考点在父局部空间的位置，anchoredPosition↔局部坐标换算的基准）按 `rowPrefab` 资产的 anchor=(0.5,0.5) 计算，而**运行时行实例 anchor 实测=(0,1)**（左上参考）——Instantiate 后立即读仍是资产值 (0.5,0.5)、漂移发生在其后某环节（真凶未定位，两轮活体取证确认漂移事实），参考点整体错位 (半宽,-半高)：目标插入位判定恒 0（无让位）+落位飞行终点指向右下（飞右下角）+FinishDropIn 按恒 0 的 target 落回原槽（瞬移回原位）。
修法：**换算基准按拖动行实例的实时 anchor 快照**（`CalcAnchorRefLocal(dragRow.transform)`），勿按 prefab 资产假设。活体验证指纹：`anchorRef+basePos[i]` 应逐位还原每行 pivot 的局部坐标（`InverseTransformPoint`），全行 match=基准正确。
连带（同批）：拖动行挂 dragLayer 后其余兄弟 siblingIndex 前移，用 `DisplayIndex`（=GetSiblingIndex）做判定/让位索引会与基准位快照错位——改用列表序（`_rows.IndexOf`，拖拽中恒定）。

## §130 UGUI 自定义 shader 材质渲染不上屏：_MainTex 无绑定采样恒黑压平全部输出（2026-10-07 切卡组高亮 shader 实证）

**症状**：给 UGUI 元素（Image/Graphic）换上自定义 shader 材质后，元素渲染**恒透明/完全不上屏**（shader 编译 0 错、材质参数正确、数据层正常驱动），而同元素用内置 UI/Default 材质+tint 时渲染正常。
根因：**UGUI 自定义材质不自动绑定 _MainTex**（无 sprite 的 Image 不绑；CanvasRenderer 对自定义材质也无内置白纹理兜底）——shader 里 `tex2D(_MainTex, uv)` 采样恒黑、`tex.a=0`，若 fragment 把 alpha 乘上 tex 采样值（如 `alpha *= tex.a` 或 `baseAlpha = mask * tex.a`），**一切输出被压成全透明**。数据层（材质 SetFloat 轨迹）完全正常，纯渲染静默失败，极难从日志发现。
修法：**自定义 UI shader 的 fragment 勿采样 _MainTex/勿让 alpha 依赖纹理采样**（纯色/程序化特效矩形无 sprite 形状诉求）——先例 CardLightBand（UI/CardLightBand）的 frag 正是如此不采 mainTex 所以工作。若确需 sprite 形状：给 Image 赋一个确定存在的 sprite（如 `Sprite.Create(Texture2D.whiteTexture,…)`）保证 _MainTex 有绑定。
**更强替代**：直接**用自绘 Graphic 替代 Image**（`class X : Graphic` 手动 OnPopulateMesh 四顶点 UV 0,0→1,1，先例=CardLightBandEffect 的 LightBandGraphic，作者注释「不依赖 Sprite」）——本实证中「Image 换 shader 材质」不出金而「同材质挂自绘 Graphic」出金（A/B 像素对照）。配套组件=Assets/_Scripts/UI/Screen/Backpack/DeckRowHighlightGraphic.cs。
**验证方法论沉淀（URP+ScreenSpaceOverlay 下 UI 动画的客观取证）**：
- `Texture2D.ReadPixels(backbuffer)` 在 URP+Overlay 下**不可靠**（读空/读错帧）——像素取证勿用；
- **可靠路径=Game View MP4 录制（bridge record_game_view，引擎渲染真值）→ ffmpeg 抽帧 → python/PIL 逐帧像素统计**（金色判据 r>120 && r-b>40，按 y 行带聚合找目标区域）；
- **注意 Overlay canvas 的世界坐标≠屏幕像素坐标**（本实证中行世界 y=-372~-160 负值、屏幕 y=世界+720）——`WorldToScreenPoint(null,…)` 与 `ScreenPointToLocalPointInRectangle` 恒等往返自洽（相对判定/拖拽不受影响），但**绝对像素 rect 换算会差半屏偏移**——像素分析先用「已知可见元素的对照带」自校验坐标映射（docs/14 §127③ 同族教训）；
- 分层排查顺序实证有效：①unity_shader.preview（Edit Mode 直接渲染材质，is_error_pink+逐帧 props override——本次靠它实锤「满格输出全透明」）②AB 对照（同材质不同挂载方式同帧同录）③MP4 像素分析。

## §131 Buff 配置化批三陷阱：ScriptableObject 子类必须同名文件 / 桥脚本元组循环赋值不落盘 / Resources 清单延迟+注册表静态缓存（2026-10-07 BuffConfig 批实证）

**① ScriptableObject 子类必须各自同名文件（硬规则）**：多个 `BuffConfig` 派生类写在同一 `.cs` 文件里时，只有「文件名==类名」的基类拿到脚本资产关联——其余子类 `AssetDatabase.CreateAsset` 时报 `No script asset for XXXConfig. Check that the definition is in a file of the same name`，落盘的 `.asset` 里 **m_Script 断链**（fileID 0），域重载后 `LoadAssetAtPath<子类>` 返回 null=资产死文件。本批 BuffConfig 基类+四个子类（Burn/Freeze/SongOfLife/IcicleBuffConfig）首版共文件全部炸，拆五个同名文件后全绿。**How to apply：新建 ScriptableObject 派生配置类一律一文件一类**；桥脚本见到该 Warn 即拆文件重建资产（断链资产删了重造，SaveAssets 救不回）。

**② exec_editor_script 的 foreach 元组数组循环内赋值不落盘（机制未定位，规避即可）**：同一段桥脚本里，显式块写的四张资产字段全部正确落盘，而 `foreach (var pair in new[] { ("名", 枚举值), … }) { cfg.字段 = pair.Item2; EditorUtility.SetDirty(cfg); }` 循环写的三张资产**字段落盘恒为默认值 0**（内存读回正常、SaveAssets 后磁盘 YAML 仍是 0）。机制未深挖（疑与脚本宿主对 ValueTuple 元素求值/闭包有关），实证两次。**How to apply：桥脚本批量建资产用逐资产显式块**，勿用元组数组 foreach 做字段写入；写完落盘后 `Get-Content` 磁盘 YAML 对账字段值（勿只信内存读回——同域内存对象读回会掩盖落盘失败）。

**③ Resources.LoadAll 清单延迟 + 注册表静态缓存跨脚本调用残留（编辑器侧验证陷阱）**：`Resources.LoadAll<T>` 在编辑器里走 Resources 清单，**AssetDatabase.SaveAssets 写盘 ≠ 清单即时更新**（表现为同域内前一脚本 LoadAll 缺刚保存的资产、隔一脚本又能查到=debounced refresh 时序）；而自定义静态注册表（`_loaded` 单次守卫）会把第一次（可能残缺）的结果**缓存在整个域会话里**——后续所有脚本调用都复用脏表，域重载才重建。症状指纹：`OfType(X)` 恒 MISSING 但直接 `LoadAssetAtPath` 正常、`LoadAll` 直查又能见。**How to apply：编辑器侧验证「注册表类静态缓存」时，每次断言前先 refresh（域重载清静态）再跑断言脚本；运行时（真机/构建）无此问题——启动即新域新清单。**

## §132 关联面板 RuleMode 天生布局塌陷：VLG childControlHeight 接管无 ILayoutElement 子级=恒 0 高 / 暂停态无布局 pass / 同 UI 三拷贝两种形态（2026-10-07 决策五十六 buff link 首开实锤）

**症状**：技能描述点 Buff link → 关联面板只有「相关效果」标题+名字，描述区不可见、整体塌陷（用户报障「没有详情介绍，布局也是乱的」）。文案/本地化全绿（TMP 已拿到正确文本）——纯布局问题。
**根因①（天生缺陷）**：RuleModeContainer 挂 VerticalLayoutGroup `childControlHeight=1`+ContentSizeFitter(Preferred)，而描述区 Scroll View 只有 ScrollRect **没有任何 ILayoutElement** → `LayoutUtility.GetPreferredHeight`=0 → VLG 分 0 高（**prefab 序列化的 769 高被 VLG 接管压 0**）；容器 CSF 又按子级 preferred 收缩 → 整链塌到 ~94px。修法=容器 CSF→**Unconstrained**（锚点已拉伸=拉满面板）+ 描述区补 **LayoutElement(minH=240, flexibleHeight=1)** 弹性吃满剩余 + 分隔线 LE prefH=4。
**根因②（暴露盲区）**：该容器 `m_IsActive=0` 常驻关闭——**从未被真实点开过**，链接系统上线多日无人撞上；决策五十六 buff link 首个真实用例才暴露。**How to apply：「常驻 inactive 的 UI 分支」=结构性审查盲区，link/弹窗类 UI 交付必附「每个分支真点一遍」目检项。**
**取证坑三件**：①**暂停态游戏没有逐帧布局 pass**——单次 `ForceRebuildLayoutImmediate` 会留中间态（Name/Content 高度归 0），须 TMP `SetLayoutDirty`+多轮重建才收敛；运行态一帧自愈，暂停态取证勿把中间态当新 bug。②**停用 CSF 不清它写过的补偿 sizeDelta**（Preferred 时代写入的负偏移留在 RectTransform）——停用后须手动复位 `sizeDelta=0` 才真拉满。③**同 UI 三处拷贝两种形态**：BackpackScreen 内嵌 SkillDetailPanel=**链接实例**（源 prefab 手术自动继承，0 变更属正常）；BattleHud 内嵌=**烘焙副本**（须逐份手术）——批量手术脚本按「值不符才写」幂等跑三处即可兼容两种。
**附**：RelatedDescription 模板化后数值金色高亮走 `SkillDescriptionBuilder.BuildRelated` 双通道（延奏类=技能参数/行为族=BuffConfig.关联名反查资产——docs/18 决策五十六）；「数值藏在文案里 baked」与「配置单源」冲突的场合照此模板化。
