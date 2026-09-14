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
- 测试场景 `Assets/Scenes/AmberTest.unity`：单模型 `AmberTMR` 实例 + `AmberTestPanel`（列表点击/↑↓ 切换，多 Animation 同步播——现单目标）
- 摆放：按旧实例包围盒高度自动对齐（旧 1.795m / 新 1.712m → 总缩放 ×1.10，底面与 XZ 中心对齐）

## 已知限制

- 材质 URP Lit 直出（无 GI 卡渲 ramp）；Body SMR 双材质槽 = [Hair, Body]
- **EyeStar 条带已隐藏（prefab 级，2026-09-15）**：星形瞳孔贴条几何大于虹膜，URP Lit 不透明渲染 = 四角星糊满双眼（与派蒙 2026-08-25 同案定案）；官方默认眼神无星形；接线保留，重新激活 EyeStar 节点即恢复
- TMR 骨架 = 86 核心骨（无 AO_/HitObject/Weapon 挂点），clips 中相应曲线不绑定（无害）；+HairB/+LegBagS 等物理骨动画为哈希路径不可读，呈静态（与旧版同限制）
- 本包仅默认服装（无皮肤版；皮肤版旧资产已随 AmberExtract 删除，需要时从 TMR 条目 513646 "Amber (100% Outrider)" 另下）
