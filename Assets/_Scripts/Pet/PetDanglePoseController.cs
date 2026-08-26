using UnityEngine;

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
        public class 垂落骨
        {
            [Tooltip("骨骼全名（Bip001 层级）")] public string 骨名;
            [Tooltip("下垂强度 0-1（1=完全转向世界下方）")] [Range(0f, 1f)] public float 下垂权重 = 1f;
            [Tooltip("飞行扑腾的相位差（弧度，四肢错开=扑腾感）")] [Range(0f, 6.28f)] public float 扑腾相位;
        }

        [Header("下垂骨表（默认覆盖四肢+手脚）")]
        [SerializeField] private 垂落骨[] 下垂骨表 =
        {
            new 垂落骨 { 骨名 = "Bip001 L UpperArm", 下垂权重 = 0.9f, 扑腾相位 = 0f },
            new 垂落骨 { 骨名 = "Bip001 R UpperArm", 下垂权重 = 0.9f, 扑腾相位 = 3.14f },
            new 垂落骨 { 骨名 = "Bip001 L Forearm", 下垂权重 = 0.8f, 扑腾相位 = 3.14f },
            new 垂落骨 { 骨名 = "Bip001 R Forearm", 下垂权重 = 0.8f, 扑腾相位 = 0f },
            new 垂落骨 { 骨名 = "Bip001 L Hand", 下垂权重 = 0.55f, 扑腾相位 = 1.57f },
            new 垂落骨 { 骨名 = "Bip001 R Hand", 下垂权重 = 0.55f, 扑腾相位 = 4.71f },
            new 垂落骨 { 骨名 = "Bip001 L Thigh", 下垂权重 = 0.75f, 扑腾相位 = 1.57f },
            new 垂落骨 { 骨名 = "Bip001 R Thigh", 下垂权重 = 0.75f, 扑腾相位 = 4.71f },
            new 垂落骨 { 骨名 = "Bip001 L Calf", 下垂权重 = 0.65f, 扑腾相位 = 0f },
            new 垂落骨 { 骨名 = "Bip001 R Calf", 下垂权重 = 0.65f, 扑腾相位 = 3.14f },
            new 垂落骨 { 骨名 = "Bip001 L Foot", 下垂权重 = 0.45f, 扑腾相位 = 3.14f },
            new 垂落骨 { 骨名 = "Bip001 R Foot", 下垂权重 = 0.45f, 扑腾相位 = 0f },
        };

        [Header("融合与扑腾")]
        [Tooltip("拎起姿势淡入/淡出速度（每秒指数趋近率）")]
        [SerializeField] private float 融合速度 = 12f;
        [Tooltip("飞行扑腾频率（Hz）")]
        [SerializeField] private float 扑腾频率 = 5f;
        [Tooltip("飞行扑腾幅度：下垂量在此深度内振荡（0.35=下垂量在 65%~100%+35% 间摆）")]
        [Range(0f, 1f)] [SerializeField] private float 扑腾幅度 = 0.35f;

        [Tooltip("总开关（关闭=完全走 clip 曲线）")] [SerializeField] private bool 启用 = true;

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
            缓存骨引用();
            if (bones == null)
            {
                enabled = false;
                return;
            }
        }

        private void 缓存骨引用()
        {
            if (下垂骨表 == null || 下垂骨表.Length == 0) return;
            var all = GetComponentsInChildren<Transform>(true);
            bones = new Transform[下垂骨表.Length];
            childDirs = new Transform[下垂骨表.Length];
            lastOffsets = new Quaternion[下垂骨表.Length];
            int found = 0;
            for (int i = 0; i < 下垂骨表.Length; i++)
            {
                lastOffsets[i] = Quaternion.identity;
                foreach (var t in all)
                {
                    if (t.name != 下垂骨表[i].骨名) continue;
                    bool 影子下 = false;
                    for (var p = t.parent; p != null && !影子下; p = p.parent)
                        if (p.name.Contains("DropShadow")) 影子下 = true;
                    if (影子下) continue;
                    bones[i] = t;
                    childDirs[i] = t.childCount > 0 ? t.GetChild(0) : null; // 骨向=第一子骨方向（上臂→前臂等）
                    found++;
                    break;
                }
            }
            if (found < 下垂骨表.Length)
                Debug.LogWarning($"[PetDangle] 骨表 {found}/{下垂骨表.Length} 命中（未命中骨跳过，检查骨名）");
        }

        private void OnDisable()
        {
            还原全部偏移();
        }

        private void OnDestroy()
        {
            还原全部偏移();
        }

        /// <summary>撤销全部已应用偏移，骨骼回纯动画值（禁用/销毁时兜底，防姿势残留）</summary>
        private void 还原全部偏移()
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
            if (!启用 || bones == null) return;

            // 姿势淡入/淡出（指数趋近；关闭时归零后偏移恒 identity=零成本）
            float k = 1f - Mathf.Exp(-融合速度 * Time.deltaTime);
            blend = Mathf.Lerp(blend, active ? 1f : 0f, k);
            if (!active && blend < 0.005f)
            {
                blend = 0f;
                还原全部偏移();
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
                float w = 下垂骨表[i].下垂权重 * blend;
                if (flail)
                {
                    w *= 1f + 扑腾幅度 * Mathf.Sin(t * 扑腾频率 * Mathf.PI * 2f + 下垂骨表[i].扑腾相位);
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
