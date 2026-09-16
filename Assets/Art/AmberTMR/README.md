# AmberTMR — 安柏 TMR 官方提取模型（现行）

2026-09-15 落地，**取代已删除的 AmberExtract**（用户拍板）。来源 = The Models Resource
[asset/328742](https://www.models-resource.com/pc_computer/genshinimpact/asset/328742/)
（Playable Characters (Pyro) - Amber，2021-12-24 上传，gabrielvict / Adverse56）。
用户下载的原始 zip 存档：`D:\Tool\AmberModel\TMR\`（29 项：dae×2 未入库）。

## 内容

| 路径 | 说明 |
|---|---|
| `Amber.fbx` | 官方直提模型：93 节点 = `Avatar_Girl_Bow_Ambor`→`Bip001` 86 骨 + 5 个 SMR（Body 10596 vert / Brow / EyeStar / Face / Face_Eye），面部 morph 47 个 |
| `AmberTMR.prefab` | 配好的模型 prefab：材质对齐 + updateWhenOffscreen + Animation（154 clips，默认 `Ani_Avatar_Girl_Bow_Ambor_Standby`，playAutomatically） |
| `Materials/` | `M_TMR_Body/Face/Hair`（复制旧版配方：URP Lit 不透明、_Cull 0 双面、_Smoothness 0、白 BaseColor，贴图换 TMR 版）+ `M_TestGround`（测试地面，原属 AmberExtract） |
| `Textures/` | 20 张 GI 原名贴图（Body/Face/Hair 的 Diffuse+Lightmap+ShadowRamp、Avatar_Girl_Tex_FaceLightmap×2、Face_Shadow×3、MetalMap；Lightmap/ShadowRamp 等未接入材质，同旧版惯例） |
| `Clips/` | **154 条官方动画**（2026-09-14 AnimeStudio 本地解包自提的 legacy .anim，自 AmberExtract 整体搬入，GUID 不变） |
| `Objects/BaronBunny/` | 兔兔伯爵 fbx + 贴图（技能物件，已入库未接场景） |

## 接线要点

- **Animation 挂在 `Avatar_Girl_Bow_Ambor` 骨架节点**（非 prefab 根）：clip 曲线路径为 `Bip001/...`，挂点选此节点 29/30 命中 + 1 条空 path（空 path=挂点自身变换）= 全兼容；挂 prefab 根则全不中
- **Animator（AmberTestController+AmberAvatar）同挂骨架节点，必须 enabled**：disabled 时肌肉通道 PlayableGraph 照常 IsPlaying=true 但人形肌肉求值静默不写骨骼（2026-09-16 定案，docs/14 §58；prefab 已开 `m_Enabled: 1` + AmberTestPanel Awake 自愈）
- 测试场景 `Assets/Scenes/AmberTest.unity`：单模型 `AmberTMR` 实例 + `AmberTestPanel`（列表点击/↑↓ 切换，多 Animation 同步播——现单目标）
- 摆放：按旧实例包围盒高度自动对齐（旧 1.795m / 新 1.712m → 总缩放 ×1.10，底面与 XZ 中心对齐）

## 已知限制

- 材质 URP Lit 直出（无 GI 卡渲 ramp）；Body SMR 双材质槽 = [Hair, Body]
- **EyeStar 条带已隐藏（prefab 级，2026-09-15）**：星形瞳孔贴条几何大于虹膜，URP Lit 不透明渲染 = 四角星糊满双眼（与派蒙 2026-08-25 同案定案）；官方默认眼神无星形；接线保留，重新激活 EyeStar 节点即恢复
- TMR 骨架 = 86 核心骨（无 AO_/HitObject/Weapon 挂点），clips 中相应曲线不绑定（无害）。**2026-09-15 勘误（变形根因定案）**：物理骨曲线其实是可读 TRS 且在 TMR 上照常播放；压缩丢失的是**主体骨骼**动画（Avatar 动画=Unity Humanoid 肌肉格式，AnimeStudio 只导出常量姿态），且 clip 位置曲线为 GI 厘米制、TMR 骨架米制（×100 失配）→ 物理骨被甩百倍远+蒙皮拉扯=播放时"严重拉伸变形"
- **2026-09-16 定案闭环（三连环收官，docs/14 §57/§58/§59）**：①炸帆=legacy POS ×100 未换算——批量 ÷100 修 150 文件；②点击 NRE=animator.playableGraph 无效句柄——IsValid 守卫；③身体零驱动三因叠加=Animator disabled（已修）+ 肌肉 clip 须为 **m_MuscleClip 密集流形态**（MuscleClips 已全量换 v11 版，GUID 不动）+ **AmberAvatar（FBX 导入器 autoGenerate）参照位姿是理想化废数据**（已换 `AmberAvatarReal.asset`=BuildHumanAvatar 真实 rest 重建，运行时实证 v11 clip armΔ=63.3°）。运行时肌肉求值不认 m_FloatCurves 可编辑形态（编辑器视图专用）；「测试骨架可、安柏无效」旧悬案全部由此三因解释。播放架构=双通道（Animator 放肌肉 clip + Animation 放 legacy 物理骨 TRS，同名同播）
- 本包仅默认服装（无皮肤版；皮肤版旧资产已随 AmberExtract 删除，需要时从 TMR 条目 513646 "Amber (100% Outrider)" 另下）
