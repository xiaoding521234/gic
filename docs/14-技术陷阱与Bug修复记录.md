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
