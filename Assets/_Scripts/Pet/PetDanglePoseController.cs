using UnityEngine;
using UnityEngine.Serialization;

namespace GIC.Pet
{
    /// <summary>
    /// 【已弃用留库 2026-08-26】拎起姿势层 v1（程序化四肢垂落叠加，docs/19 §6.1）。
    /// 已被 v3 主流桌宠式方案取代：专用 Drag01 垂落动画 clip（四肢常量垂落+躯干待机微动），
    /// 见 PetBehaviorController.拎起动作名。本文件不再挂任何场景物体（PaimonPet 场景组件已移除），
    /// 保留供姿势算法参考（世界空间 FromToRotation 骨向→世界下方，与 Bip001 局部轴向无关；
    /// undo 记账叠加模式与 PetLookAtController 同哲学）。
    ///
    /// 原始设计：被抓住时四肢/手脚向世界下方垂落（身体整体倾角由 PetWindowController 根旋转承担，
    /// 本层只管四肢）；拖拽期=四肢下垂；飞行期=下垂量按相位振荡（空中扑腾挣扎）。
    /// 弃用原因：与 clip 曲线每帧叠加易生顿挫、调参面大；主流桌宠（VPet/eSheep/Shimeji）实为
    /// 专用"被提起"动画（静态帧或短循环），clip 等价物更简单且与 CrossFade/情绪层天然兼容。
    /// </summary>
    public class PetDanglePoseController : MonoBehaviour
    {
        [System.Serializable]
        public class DangleBone
        {
            [InspectorName("骨名")]
            [Tooltip("骨骼全名（Bip001 层级）")] public string boneName;
            [InspectorName("下垂权重")]
            [Tooltip("下垂强度 0-1（1=完全转向世界下方）")] [Range(0f, 1f)] public float dangleWeight = 1f;
            [InspectorName("扑腾相位")]
            [Tooltip("飞行扑腾的相位差（弧度，四肢错开=扑腾感）")] [Range(0f, 6.28f)] public float flapPhase;
        }

        [Header("下垂骨表（默认覆盖四肢+手脚）")]
        [InspectorName("下垂骨表")]
        [SerializeField] private DangleBone[] dangleBones =
        {
            new DangleBone { boneName = "Bip001 L UpperArm", dangleWeight = 0.9f, flapPhase = 0f },
            new DangleBone { boneName = "Bip001 R UpperArm", dangleWeight = 0.9f, flapPhase = 3.14f },
            new DangleBone { boneName = "Bip001 L Forearm", dangleWeight = 0.8f, flapPhase = 3.14f },
            new DangleBone { boneName = "Bip001 R Forearm", dangleWeight = 0.8f, flapPhase = 0f },
            new DangleBone { boneName = "Bip001 L Hand", dangleWeight = 0.55f, flapPhase = 1.57f },
            new DangleBone { boneName = "Bip001 R Hand", dangleWeight = 0.55f, flapPhase = 4.71f },
            new DangleBone { boneName = "Bip001 L Thigh", dangleWeight = 0.75f, flapPhase = 1.57f },
            new DangleBone { boneName = "Bip001 R Thigh", dangleWeight = 0.75f, flapPhase = 4.71f },
            new DangleBone { boneName = "Bip001 L Calf", dangleWeight = 0.65f, flapPhase = 0f },
            new DangleBone { boneName = "Bip001 R Calf", dangleWeight = 0.65f, flapPhase = 3.14f },
            new DangleBone { boneName = "Bip001 L Foot", dangleWeight = 0.45f, flapPhase = 3.14f },
            new DangleBone { boneName = "Bip001 R Foot", dangleWeight = 0.45f, flapPhase = 0f },
        };

        [Header("融合与扑腾")]
        [Tooltip("拎起姿势淡入/淡出速度（每秒指数趋近率）")]
        [InspectorName("融合速度")]
        [SerializeField] private float blendSpeed = 12f;
        [Tooltip("飞行扑腾频率（Hz）")]
        [InspectorName("扑腾频率")]
        [SerializeField] private float flapFreq = 5f;
        [Tooltip("飞行扑腾幅度：下垂量在此深度内振荡（0.35=下垂量在 65%~100%+35% 间摆）")]
        [InspectorName("扑腾幅度")]
        [Range(0f, 1f)] [SerializeField] private float flapAmp = 0.35f;

        [Tooltip("总开关（关闭=完全走 clip 曲线）")] [InspectorName("启用")] [SerializeField] private bool enableDangle = true;

        private Transform[] bones;
        private Transform[] childDirs;   // 各骨的"骨向"参照子骨（UpperArm→Forearm 等）
        private Quaternion[] lastOffsets; // 上帧应用的世界偏移（undo 记账）
        private float blend;             // 0=纯动画 1=完全拎起
        private bool active;
        private bool flail;

        /// <summary>被拎起中（拖拽钟摆期）</summary>
        public void Set拎起(bool on) { active = on; }
        /// <summary>空中扑腾（飞行期四肢振荡）</summary>
        public void Set扑腾(bool on) { flail = on; }

        private void Start()
        {
            CacheBoneRefs();
            if (bones == null)
            {
                enableDangle = false;
                return;
            }
        }

        private void CacheBoneRefs()
        {
            if (dangleBones == null || dangleBones.Length == 0) return;
            var all = GetComponentsInChildren<Transform>(true);
            bones = new Transform[dangleBones.Length];
            childDirs = new Transform[dangleBones.Length];
            lastOffsets = new Quaternion[dangleBones.Length];
            int found = 0;
            for (int i = 0; i < dangleBones.Length; i++)
            {
                lastOffsets[i] = Quaternion.identity;
                foreach (var t in all)
                {
                    if (t.name != dangleBones[i].boneName) continue;
                    bool shadowBelow = false;
                    for (var p = t.parent; p != null && !shadowBelow; p = p.parent)
                        if (p.name.Contains("DropShadow")) shadowBelow = true;
                    if (shadowBelow) continue;
                    bones[i] = t;
                    childDirs[i] = t.childCount > 0 ? t.GetChild(0) : null; // 骨向=第一子骨方向（上臂→前臂等）
                    found++;
                    break;
                }
            }
            if (found < dangleBones.Length)
                Debug.LogWarning($"[PetDangle] 骨表 {found}/{dangleBones.Length} 命中（未命中骨跳过，检查骨名）");
        }

        private void OnDisable()
        {
            ResetAllOffsets();
        }

        private void OnDestroy()
        {
            ResetAllOffsets();
        }

        /// <summary>撤销全部已应用偏移，骨骼回纯动画值（禁用/销毁时兜底，防姿势残留）</summary>
        private void ResetAllOffsets()
        {
            if (bones == null) return;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || lastOffsets[i] == Quaternion.identity) continue;
                bones[i].rotation = Quaternion.Inverse(lastOffsets[i]) * bones[i].rotation;
                lastOffsets[i] = Quaternion.identity;
            }
        }

        private void LateUpdate()
        {
            if (!enableDangle || bones == null) return;

            // 姿势淡入/淡出（指数趋近；关闭时归零后偏移恒 identity=零成本）
            float k = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
            blend = Mathf.Lerp(blend, active ? 1f : 0f, k);
            if (!active && blend < 0.005f)
            {
                blend = 0f;
                ResetAllOffsets();
                return;
            }

            // Pass 1：撤销上帧偏移（还原纯动画值）+ 快照骨向（子骨位置此刻=动画位）
            var dirs = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                if (lastOffsets[i] != Quaternion.identity)
                {
                    bones[i].rotation = Quaternion.Inverse(lastOffsets[i]) * bones[i].rotation;
                    lastOffsets[i] = Quaternion.identity;
                }
                if (childDirs[i] != null)
                {
                    Vector3 d = childDirs[i].position - bones[i].position;
                    if (d.sqrMagnitude > 1e-10f) dirs[i] = d.normalized;
                }
            }

            // Pass 2：叠加新偏移（世界空间 FromToRotation 骨向→世界下方；方向不可用则跳过）
            float t = Time.time;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || dirs[i] == Vector3.zero) continue;
                float w = dangleBones[i].dangleWeight * blend;
                if (flail)
                {
                    w *= 1f + flapAmp * Mathf.Sin(t * flapFreq * Mathf.PI * 2f + dangleBones[i].flapPhase);
                    w = Mathf.Clamp(w, 0f, 1f);
                }
                if (w <= 0.001f) continue;
                Quaternion q = Quaternion.FromToRotation(dirs[i], Vector3.down);
                Quaternion off = Quaternion.Slerp(Quaternion.identity, q, w);
                bones[i].rotation = off * bones[i].rotation;
                lastOffsets[i] = off;
            }
        }
    }
}
