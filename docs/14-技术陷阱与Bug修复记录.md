# 技术陷阱与 Bug 修复记录

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

**规则**：需要描边/特殊配色的 TMP 文本 = 同字体 + 变体材质（放 `TextMesh Pro/Resources/Fonts & Materials/`，该目录 git 忽略，改材质不入库，重装环境需手动备份）。另注意 rg/搜索工具默认跳过 git 忽略目录，排查 TextMesh Pro/ 下资产时需加 `--no-ignore`。

### 6.3 引用相等判断在打包后误判字体变更（真机描边二次丢失，2026-08-16）

6.2 的修复（`font != currentFont` 引用比较）在编辑器正常、**导出 APK 后描边仍丢失**。根因：`zh-cn SDF.asset` 被三路引用——场景/Prefab 直引、`Resources/` 目录、Addressables（`Localization-Assets-Shared` 组，UIAssets 表 MainFont 按其 GUID 加载）。打包后玩家包内容与 Addressables bundle 各持一份实例；编辑器 Play 时 Addressables 走 AssetDatabase，两处为**同一实例**。于是 `textComponent.font != currentFont`：编辑器判"没变"（保留变体材质），真机判"变了"（font+fontMaterial 被覆盖回 `_OutlineWidth=0` 的 Atlas Material）——典型的"编辑器正常真机异常"源于 Addressables 实例重复。

**修复（两阶段）**：
- **止血**：引用比较改按逻辑身份（`font.name`），实例不同但同一字体资产时不切换、不动材质。
- **根治（同日）**：删除 TextCombiner 整条运行时字体加载链（fontTable/fontEntryKey/LoadFont/ApplyFont），并删除 UIAssets 资产表 5 个语言表的 MainFont 条目 + SharedData key（该表本来只有中文配了字体、且唯一消费者就是 TextCombiner——纯死代码路径）。字体只剩两条固有打包路径：场景/Prefab 直引（sharedassets）+ TMP Settings 默认字体（resources.assets，TMP 机制要求 defaultFontAsset 必须在 `TextMesh Pro/Resources/Fonts & Materials/`）。运行时再无代码触碰 font/fontMaterial，描边永不丢失，并省掉 Addressables bundle 里的一份字体。

**操作记录**：删表条目用 LocalizationEditorSettings API（`UnityEditor.Localization` 命名空间）；Addressables read-only 组 `Localization-Assets-Shared.asset` 的残留条目不会因表变更自动同步，需手删 m_Entries 中该条目块 + `AssetDatabase.ImportAsset(ForceUpdate)` 重载，再用 FindGroup 验证 entries 归零。

**规则**：凡"按资产引用相等做幂等判断"的代码，在 Addressables 项目里打包后都可能因实例重复而失效——要么按 GUID/名称等逻辑身份比较，要么保证资产只进一条加载路径。多语言字体切换若将来要做，重新设计时勿恢复"运行时整体赋 font+fontMaterial"的写法（应只换 fontAsset 并保留目标材质变体的映射）。

---

## 7. Tuanjie UITK 编辑器工具陷阱（P12b 期间，2026-08-16）

> 完整陷阱表与标准骨架见 skill `gic-editor-tool`；此处只记当次踩坑实录。

### 7.1 SerializedObject.GetIterator() 上直接 GetEndProperty() 触发 Assert

根级全字段遍历若写成 `var it = so.GetIterator(); var end = it.GetEndProperty(); while (it.NextVisible(true) && !EqualContents(it, end))`，Inspector 首帧即 Assert "Invalid iteration - (You need to call Next (true) on the first element)"。

**正解**：根级遍历不配 end——`bool enterChildren = true; while (it.NextVisible(enterChildren)) { enterChildren = false; ... }`。元素级子属性遍历（`element.Copy()` 后配 `element.GetEndProperty()`）则正常（ConfigEditorUITK.CreateList/编辑窗口均用此模式）。症状在"资产恰好被选中"时立即暴露，平时静默。

### 7.2 VisualElement.Bind() 扩展方法不可用（CS1061）

Tuanjie 中 `Bind()`/`PropertyField` 的 UITK 绑定扩展在 **UnityEditor.UIElements** 命名空间（标准 Unity 同款但 IDE 默认 using 不会带上）。新写 Inspector/窗口报 CS1061 时补 `using UnityEditor.UIElements;` 即可。

### 7.3 编辑器窗口换游戏字体

`root.style.unityFontDefinition = FontDefinition.FromFont(font)` 在根元素设一次即可全树继承（UITK 字体继承）。字体源文件用游戏 TMP 字体对应的 ttf（`TextMesh Pro/Resources/Fonts & Materials/zh-cn.ttf`，即 zh-cn SDF 的 m_SourceFontFile）；LoadAssetAtPath 失败时静默保持默认字体，勿因字体缺失抛错。封装：`ConfigEditorUITK.ApplyGameFont(root)`。

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

## 14. 程序化 UI 锚点双陷阱（派蒙聊天气泡错位，2026-08-29）

### 现象
聊天输入条正确挂在派蒙模型脚底下方，回复气泡却出现在"派蒙右上方非常远"处。

### 根因（两个叠加）
1. **程序化新建 RectTransform 默认锚=画布中心**：`new GameObject("X", typeof(RectTransform))` 后不设 anchorMin/anchorMax，默认值是 (0.5,0.5) 中心锚——而代码按"左下原点画布绝对坐标"写 `anchoredPosition`（锚点提供器返回的世界投影就是这套坐标），中心锚下实际位置=画布中心+anchoredPosition，系统性偏移 **(+半屏宽, +半屏高)**，正好把气泡推到右上远处。同文件里输入条显式设了左下锚所以正常——一个设了一个没设，肉眼直接对比出差异。
2. **找本体骨 不过滤 MMD 兜底模型**：`PetHostBase.找本体骨` 只排除影子壳（_DropShadow/MMD_DropShadow），不过滤禁用留存的 MMD 兜底模型（Paimon_arm，日文骨名系）。查 `"頭"` 命中的是**不动的 MMD 头骨**而非 GI 本体（Bip001 系）——气泡锚点追踪错误目标。

### 规范
- 程序化建 UI 元素（气泡/输入条/图标等）必须在创建后**显式设 anchorMin=anchorMax**（本项目约定=左下锚 (0,0)，anchoredPosition 即画布绝对坐标），再设 pivot——绝不信默认锚。同批创建的元素锚约定必须一致，否则跟随逻辑混用两套坐标系。
- 按骨名找骨（找本体骨/transform.Find）先确认骨名属于**当前激活模型**的命名系：GI 官方模型=Bip001 系（骨盆 Bip001 Pelvis/头 Bip001 Head），MMD=日文系（全ての親/頭）；PetLookAtController 的头骨名走 Inspector 序列化值按模型切换配置，新代码找骨照此办理，勿硬编码另一个模型的骨名。

### 追加根因（同日二测："能看见文字，但看不见 UI 本身"）
贴图层面第三因：**"九切片"素材实为整图+透明边距**。pet_chat_bubble_9slice.png 不透明 bbox 上下各留 66px/左右 48px 透明，按 border=56 切 3×3 后**四角+上下边条全是透明像素**（仅中心 82% 不透明）；而输入条高 56、气泡最小高 72 都 < 上下 border 和 112 → Sliced 中心行被压成零/负高 → 整个 UI 只画透明像素=不可见（TMP 文字是独立子物体不受影响）。修复=程序化生成规范九切片（128×128 纯白圆角矩形 border=24，白底供 Image.color 染色，角 78%/边条/中心 100% 不透明——78%=圆角裁切 π/4 理论值）；发送按钮矩形 < border×2 改 Simple 防退化。

### 规范（九切片素材审计）
- **导入九切片素材前按 border 切 3×3 分区测不透明率**：四角应≈100%-π/4（圆角裁切）、边条/中心≈100%；四角或边条≈0%=不是九切片结构（整图带透明边距），Sliced 会只画中心区——小矩形（高 < 上下 border 和）时整体不可见。TextureImporter 的 border 数值不会校验素材结构。
- 目标矩形任一边 < 对应 border×2 时 Sliced 退化（中心行零/负高）——小元素（按钮）用 Simple，或换更小 border 的素材。

### 追加根因（同日三测：控制台 NRE 刷屏）
**TMP_InputField 拖拽选字在无 MainCamera 场景必炸（TMP 3.0.9）**：输入框内按住拖动 → 基类 OnDrag 启动 `MouseDragOutsideRect` 协程 → `ScreenPointToLocalPointInRectangle(textViewport, pos, eventData.pressEventCamera, ...)`——ScreenSpaceOverlay 画布下 pressEventCamera 恒 null → Unity 内部回退 `Camera.main.ScreenPointToRay` → GIC 纯 UI 场景（SettingsScreen 等）无 MainCamera tag 相机 = 每帧 NullReferenceException（源码 L1759 实证）。
**修复**：子类 `PetChatInputField` 覆写 OnDrag 为空（掐掉协程路径；代价=拖出矩形选字失效，单击定位/双击选词/Shift+方向键选区不受影响）。**勿给聊天画布配 MainCamera tag 相机**（DontDestroyOnLoad 相机抢 Camera.main 是另一坑，见 §6.4 教训）；也勿把聊天画布改 ScreenSpaceCamera（会被游戏 Overlay UI 盖住，layering 语义反了）。
另：程序化 TMP 文本勿用"➤"等装饰符号——zh-cn SDF 无此字形（警告+显示方块），按钮文字用中文（"发送"）。

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

## 17. 聊天输入期冻结物理收尾=永久拎起姿势+视线失联（2026-08-29 两形态同坑）

### 现象
①派蒙头不再跟踪鼠标；②拖拽后放下反应还没播就单击开对话→永远卡在被拖拽后的姿势。两症状同根。

### 根因
宿主拖拽轮询的"聊天输入期"分支整体早退（防输入焦点误触拖拽），**把物理收尾轮询也一并跳过**：`dragPhysics.IsActive`（松手后的四肢弹簧收尾期）永真 → 行为层 Update 恒走 `dragPhysicsPhase`（Drag01 拎起动画循环 + `Set视线静默(true)`）→ 永久拎起姿势 + 视线层静默（头不跟鼠标）。输入条若常开（发送后保留），冻结无限期。

### 修复与规范
- **帧序铁律：物理推进（松手/收尾）在交互轮询里无条件先行，聊天输入门控只拦"新拖拽起手/单击判定"**——两形态（PetInGameHostController.dragPollFrame / PetWindowController.DragAndClickFrame）同构重排。
- 桌面版同坑在补聊天时一并规避（物理块上移到门控前）；今后任何"输入期冻结交互"的新分支都不得包裹物理收尾帧。

## 18. 本地化脚本 RemapId 静默失败后的错位写值（"输入条显示输入供应商"，2026-08-29）

### 现象
聊天输入条占位文本显示"对话模型供应商"（用户读作"输入供应商"）；错误/忙碌提示语也变成了供应商名。

### 根因
加键脚本的流程：`AddKey(key)`（拿大数 id）→ `RemapId(大数, 目标分段 id)` → 按目标 id 写值。**RemapId 在目标 id 已被占用时返回 False（静默失败）**，脚本未检查返回值继续按目标 id 写值——写进了**占用该 id 的既有键**（PetChatPlaceholder/PetChatNoKey/PetChatBusy/PetChatNotWired 四键被供应商名覆盖）。目标 id"看似空闲"是凭记忆拍的（9039~9043 实际已被聊天域键占用）。

### 修复与规范
- **AddKey 前必须真查表**：`Entries.Any(e => e.Id == 目标)` 确认空闲（"查该域现有最大序号"不能凭记忆）；**RemapId 返回值必须检查**，False=目标占用或 currentId 不存在，立刻停手换 id。
- 事故恢复：git diff 语言表 .asset 取原值（含 YAML 折行=\n 的多行值）→ 脚本直改恢复 → 重导出 CSV。
- 已把完整绕行流程与三坑（AddKey(key,id) NRE / 同 key 重复条目 / 半执行状态）记入 gic-localization skill。




