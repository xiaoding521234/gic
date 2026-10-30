# 安柏完整模型与官方动作重做设计

日期：2026-10-31。用户选择方案 A：保留完整官方模型，重做原生 Humanoid 肌肉动画与角色物理骨动画的双通道接入；完成后由 AI 自检动态画面，最终仍由用户目检验收。

## 目标与边界

在 `Assets/Scenes/AmberTest.unity` 中交付可逐条切换的安柏完整 3D 模型与首批官方动作。模型身体、四肢、手指必须参与动作，头发、裙摆和配饰继续由角色物理层驱动；不得出现旧版的身体静止、物理骨拉伸、明显比例失真或循环播放崩坏。

本批只改测试资产、`AmberTestPanel` 和必要的临时导出工具，不把战斗中的 2D 立牌替换为 3D 模型，不改战斗规则、UnitView、战斗 HUD 或其他角色。首批通过后不自动宣称全部 154 条动作已完成，后续扩展沿用同一数据管线。

## 已核验事实

- `Assets/Art/AmberTMR/AmberTMR.prefab` 含 5 个 SkinnedMeshRenderer、86 根骨骼、有效 Humanoid `AmberAvatarReal`，模型和材质可作为重做基线。
- 现有 Animator 有 153 条肌肉通道资产，但只有 13 条 `humanMotion=true`；其余主要是角色物理层，不是完整身体动画。
- 已从原始 `04161624.blk` 导出并扫描 1205 条动画 JSON，其中 355 条具有真实肌肉通道。安柏身体层与物理层有时同名，有时需要跨命名配对，不能继续仅靠同名匹配。
- 现有 `build_muscle_clips_v11.cjs` 能把 GI 源 JSON 与修复后的 ACL 数据重建为 Tuanjie 原生 v11 肌肉动画；其输入目录已被清理，需要恢复确定性导出步骤。
- 不采用旧 FBX 自研烘焙产物作为本批交付；旧资产仅保留作对照，不覆盖、不删除。

## 首批动作与配对

| 验收类别 | Animator 身体层 | Animation 物理层 |
|---|---|---|
| 常态待机 | `Ani_Avatar_Girl_Standby` | `Ani_Avatar_Girl_Bow_Ambor_Standby` |
| 弓箭待机 | `Ani_Avatar_Girl_Bow_WeaponStandby` | `Ani_Avatar_Girl_Bow_Ambor_WeaponStandby` |
| 跑步/冲刺循环 | `Ani_Avatar_Girl_RunBS`、`Ani_Avatar_Girl_SprintCycle+Mirror` | 安柏同族跑步物理层，按实机画面选定并固定 |
| 飞行 | `Ani_Avatar_Girl_FlyNormal` | `Ani_Avatar_Girl_Bow_Ambor_FlyNormal` |
| 弓箭攻击 | `Ani_Avatar_Girl_Bow_Ambor_Attack_05` | 同名角色物理层 |
| 元素战技 | `Ani_Avatar_Girl_Bow_Ambor_ElementalArt_Short` | 同名角色物理层 |
| 展示 | `Ani_Avatar_Girl_Bow_Ambor_Show_01` | 同名角色物理层 |

跑步物理层只从安柏现有官方物理 clip 中选择，不引入其他角色或武器类型的动作。若身体层与物理层时长不同，分别按源时长循环，不通过拉伸某一层强行等长；观感不接受时更换同族配对，而非掩盖问题。

## 实现结构

### 1. 可重复的原生肌肉动画导出

- 用 AnimeStudio CLI 导出目标 clip 的源 JSON，保留原始 `m_MuscleClip`、binding 和通道定义。
- 用现有 AnimHarness 的 DBACL streamer 修复版解压目标 clip，生成 `acl_values.f32` 与 `acl_times.f32`。
- 在 `.codely-cli/tmp` 的临时工具链补齐确定性转换，将上述数据写成 `build_muscle_clips_v11.cjs` 所需的 `srcjson/AnimationClip/<name>.json` 与 `decoded/<name>.json`。
- 重跑 v11 构建，保留厘米到米的位置换算、逐通道 `m_ValueArrayDelta` 和四元数合法化。测试场景采用原地播放：只去除 Root/Motion 的整体位移，保留 Hips 局部起伏与动作幅度，禁止再次把物理骨位置直接放大 100 倍。
- 新产物写入 `Assets/Art/AmberTMR/MuscleClips/Rebuilt/`，与旧 `MuscleClips/` 隔离；导入后逐条回读 `humanMotion`、时长、循环标记和源路径。

### 2. 测试面板的显式动作条目

`AmberTestPanel` 增加中文序列化动作条目：显示名、身体动画、物理动画、是否循环。首批条目直接在 Inspector 配好，不再要求两个通道同名，也不依赖把全部 clip 塞进 AnimatorController 才能发现。

保留原双通道播放方式：身体 clip 经 Animator + PlayableGraph 求值，角色物理 clip 经 legacy Animation 播放。切换时在同一帧重置两通道时间并播放；旧 PlayableGraph 必须经 `IsValid` 后销毁，避免重复点击产生空句柄或叠图。缺 clip 时明确报错并跳过该条目，不静默显示一个看似可用但身体静止的动作。

### 3. 模型与材质保护

不重建或替换已核验的 TMR 网格、骨骼和材质。保持 `AmberAvatarReal`、Animator enabled、`updateWhenOffscreen=true` 和 EyeStar 默认隐藏。任何模型比例、骨骼位置或材质变化都必须单独说明，不得借“修动作”改坏已通过的模型外观。

## 验证与画面自检

1. **编译与资产验证**：`unity_editor.refresh` 无新增 C# 错误；新肌肉 clip 均为有效 Humanoid 动作，面板所有引用非空；模型仍为 5 个蒙皮部件，材质无丢失。
2. **编辑器侧数据验证**：检查首末帧与 `m_ValueArrayDelta` 一致，位置单位不再出现百倍拉伸；循环 clip 首尾姿态差在可解释范围内。
3. **动态画面自检（用户已明确授权）**：进入 `AmberTest`，逐条触发首批动作并录制 Game View 短视频。直接检查视频中的身体/四肢参与、拉伸变形、手脚姿态、头发裙摆、比例、朝向、循环接缝和明显穿模；不以编译或 `humanMotion=true` 代替画面判断。
4. **返修原则**：先区分身体层、物理层和配对问题，再针对源数据或接线修复；不靠隐藏模型、缩小到看不清或静态截图掩盖动作缺陷。
5. **交付**：附上各动作自检结果与短视频，最终观感由用户目检确认。首批未通过的动作如实列出，不宣称全部完成。

## 非目标与回退

不做全场卡通渲染、表情系统、战斗 3D 化、AI 动作生成或全部 154 条动作的一次性接入。若原生肌肉路线在特定动作上出现数据级缺陷，只对该动作评估 harness FBX 烘焙兜底，不将整批悄悄切回旧路线。
