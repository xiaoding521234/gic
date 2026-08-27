using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 【GI 模型上死代码（2026-08-25 查证，留库备查）】本组件按 MMD 日文骨名（親指０.L 等）找骨，
    /// GI 官方骨架是 Bip001 名——全部找不到，SetPose/应用姿态实际零效果；GI 路线手指姿态走 clip 曲线。
    /// 勿把本层（或 PetAnimSwapper 对它的调用）当成手指会动的原因。恢复 MMD 模型时本层随之复活。
    ///
    /// 桌宠手指姿态控制器（2026-08-23 v18.2 分层叠加）——LateUpdate 在 clip 基础姿势上叠加姿态层。
    ///
    /// 分层叠加（对齐 2026 主流 hand pose layer）：
    ///   clip 曲线 = 基础姿势（方案 B 管线 v17 已把 GI 逐帧手指数据写入，Standby 有微动）；
    ///   本组件 = 姿态叠加层（LateUpdate 在 clip 写入后叠加弯曲增量，权重 0-1 平滑过渡）。
    ///   权重 0 = 纯 clip（Standby 微动自然呈现）；权重 >0 = clip 基础上叠加姿态（Anger 握拳）。
    ///   叠加层与 clip 共存，权重过渡时手指自然从 clip 姿势滑向目标姿态。
    ///
    /// 弯曲轴约定（2026-08-23 实测）：
    ///   四指第 1/2 节 = 局部 X+；第 3 节 = 局部 X-（坐标系翻）；拇指 = 局部 Y+（屈曲+内收混合）。
    /// 左右手镜像（局部系自带），无需区分。
    ///
    /// 接 PetAnimSwapper.Play 调用 SetPose(clip名) 切换目标。
    /// </summary>
    public class PetFingerPoseController : MonoBehaviour
    {
        [System.Serializable]
        public class 手指姿态
        {
            [Tooltip("姿态叠加权重（0=纯 clip，1=完全叠加）")] public float 权重 = 1f;
            [Tooltip("拇指根节（親指０）屈曲°")] public float 拇指根 = 5f;
            [Tooltip("拇指中节（親指１）屈曲°")] public float 拇指中 = 8f;
            [Tooltip("拇指末节（親指２）屈曲°")] public float 拇指末 = 5f;
            [Tooltip("食指根节（人指１）屈曲°")] public float 食指根 = 5f;
            [Tooltip("食指中节（人指２）屈曲°")] public float 食指中 = 10f;
            [Tooltip("食指末节（人指３）屈曲°")] public float 食指末 = 8f;
            [Tooltip("中指根节（中指１）屈曲°")] public float 中指根 = 8f;
            [Tooltip("中指中节（中指２）屈曲°")] public float 中指中 = 12f;
            [Tooltip("中指末节（中指３）屈曲°")] public float 中指末 = 8f;
            [Tooltip("无名指根节（薬指１）屈曲°")] public float 无名指根 = 10f;
            [Tooltip("无名指中节（薬指２）屈曲°")] public float 无名指中 = 15f;
            [Tooltip("无名指末节（薬指３）屈曲°")] public float 无名指末 = 10f;
            [Tooltip("小指根节（小指１）屈曲°")] public float 小指根 = 12f;
            [Tooltip("小指中节（小指２）屈曲°")] public float 小指中 = 18f;
            [Tooltip("小指末节（小指３）屈曲°")] public float 小指末 = 12f;
        }

        [Header("姿态参数集（按 clip 名匹配）")]
        [SerializeField] private 手指姿态 生气姿态 = new 手指姿态
        {
            权重 = 1f,
            拇指根 = 30f, 拇指中 = 45f, 拇指末 = 30f,
            食指根 = 70f, 食指中 = 80f, 食指末 = 60f,
            中指根 = 75f, 中指中 = 85f, 中指末 = 65f,
            无名指根 = 80f, 无名指中 = 90f, 无名指末 = 70f,
            小指根 = 85f, 小指中 = 95f, 小指末 = 75f,
        };

        [Header("过渡")]
        [Tooltip("姿态切换平滑过渡秒数")] [SerializeField] private float 过渡秒 = 0.25f;
        [Tooltip("是否启用手手指姿态层（关=完全走 clip 曲线）")] [SerializeField] private bool 启用 = true;

        // 运行时状态
        private 手指姿态 _当前姿态;      // 插值后的实时姿态
        private 手指姿态 _目标姿态;
        private float _过渡进度 = 1f;   // 1=已到目标
        private 手指姿态 _过渡起点;
        private float _当前权重 = 0f;   // v18.2：叠加权重（0=纯 clip，1=完全叠加）

        // 骨引用（启动时按名查）
        private Transform _亲指０L, _亲指１L, _亲指２L, _亲指０R, _亲指１R, _亲指２R;
        private Transform _人指１L, _人指２L, _人指３L, _人指１R, _人指２R, _人指３R;
        private Transform _中指１L, _中指２L, _中指３L, _中指１R, _中指２R, _中指３R;
        private Transform _薬指１L, _薬指２L, _薬指３L, _薬指１R, _薬指２R, _薬指３R;
        private Transform _小指１L, _小指２L, _小指３L, _小指１R, _小指２R, _小指３R;

        void Start()
        {
            _当前姿态 = new 手指姿态();
            _目标姿态 = new 手指姿态();
            _过渡起点 = new 手指姿态();
            _当前权重 = 0f;
            缓存骨引用();
        }

        void 缓存骨引用()
        {
            var all = GetComponentsInChildren<Transform>(true);
            System.Func<string, Transform> F = n =>
            {
                foreach (var t in all) if (t.name == n) return t;
                return null;
            };
            _亲指０L = F("親指０.L"); _亲指１L = F("親指１.L"); _亲指２L = F("親指２.L");
            _亲指０R = F("親指０.R"); _亲指１R = F("親指１.R"); _亲指２R = F("親指２.R");
            _人指１L = F("人指１.L"); _人指２L = F("人指２.L"); _人指３L = F("人指３.L");
            _人指１R = F("人指１.R"); _人指２R = F("人指２.R"); _人指３R = F("人指３.R");
            _中指１L = F("中指１.L"); _中指２L = F("中指２.L"); _中指３L = F("中指３.L");
            _中指１R = F("中指１.R"); _中指２R = F("中指２.R"); _中指３R = F("中指３.R");
            _薬指１L = F("薬指１.L"); _薬指２L = F("薬指２.L"); _薬指３L = F("薬指３.L");
            _薬指１R = F("薬指１.R"); _薬指２R = F("薬指２.R"); _薬指３R = F("薬指３.R");
            _小指１L = F("小指１.L"); _小指２L = F("小指２.L"); _小指３L = F("小指３.L");
            _小指１R = F("小指１.R"); _小指２R = F("小指２.R"); _小指３R = F("小指３.R");
        }

        /// <summary>PetAnimSwapper.Play 调用：按 clip 名切换姿态</summary>
        public void SetPose(string clipName)
        {
            if (!启用) return;
            _过渡起点 = _当前姿态;
            _过渡起点.权重 = _当前权重;
            if (clipName.Contains("Anger"))
            {
                _目标姿态 = 生气姿态;
            }
            else
            {
                // Standby/其他 clip 纯走源数据（叠加权重渐隐到 0）
                _目标姿态 = new 手指姿态();
                _目标姿态.权重 = 0f;
            }
            _过渡进度 = 0f;
        }

        void LateUpdate()
        {
            if (!启用) return;
            if (_过渡进度 < 1f)
            {
                _过渡进度 = Mathf.Min(1f, _过渡进度 + Time.deltaTime / Mathf.Max(0.001f, 过渡秒));
                插值姿态(_过渡起点, _目标姿态, _过渡进度, _当前姿态);
                _当前权重 = Mathf.Lerp(_过渡起点.权重, _目标姿态.权重, _过渡进度);
            }
            else
            {
                _当前权重 = _目标姿态.权重;
            }
            if (_当前权重 > 0.001f)
                应用姿态(_当前姿态, _当前权重);
        }

        static void 插值姿态(手指姿态 a, 手指姿态 b, float t, 手指姿态 o)
        {
            o.拇指根 = Mathf.LerpAngle(a.拇指根, b.拇指根, t); o.拇指中 = Mathf.LerpAngle(a.拇指中, b.拇指中, t); o.拇指末 = Mathf.LerpAngle(a.拇指末, b.拇指末, t);
            o.食指根 = Mathf.LerpAngle(a.食指根, b.食指根, t); o.食指中 = Mathf.LerpAngle(a.食指中, b.食指中, t); o.食指末 = Mathf.LerpAngle(a.食指末, b.食指末, t);
            o.中指根 = Mathf.LerpAngle(a.中指根, b.中指根, t); o.中指中 = Mathf.LerpAngle(a.中指中, b.中指中, t); o.中指末 = Mathf.LerpAngle(a.中指末, b.中指末, t);
            o.无名指根 = Mathf.LerpAngle(a.无名指根, b.无名指根, t); o.无名指中 = Mathf.LerpAngle(a.无名指中, b.无名指中, t); o.无名指末 = Mathf.LerpAngle(a.无名指末, b.无名指末, t);
            o.小指根 = Mathf.LerpAngle(a.小指根, b.小指根, t); o.小指中 = Mathf.LerpAngle(a.小指中, b.小指中, t); o.小指末 = Mathf.LerpAngle(a.小指末, b.小指末, t);
        }

        void 应用姿态(手指姿态 p, float weight)
        {
            // v18.2 分层叠加：在 clip 基础姿势上叠加弯曲增量（权重缩放）
            // 四指：第 1/2 节绕 X+，第 3 节绕 X-（局部系翻转）
            // 拇指：绕 Y+（屈曲+内收混合轴）
            应用骨(_亲指０L, _亲指１L, _亲指２L, p.拇指根, p.拇指中, p.拇指末, true, weight);
            应用骨(_亲指０R, _亲指１R, _亲指２R, p.拇指根, p.拇指中, p.拇指末, true, weight);
            应用骨(_人指１L, _人指２L, _人指３L, p.食指根, p.食指中, p.食指末, false, weight);
            应用骨(_人指１R, _人指２R, _人指３R, p.食指根, p.食指中, p.食指末, false, weight);
            应用骨(_中指１L, _中指２L, _中指３L, p.中指根, p.中指中, p.中指末, false, weight);
            应用骨(_中指１R, _中指２R, _中指３R, p.中指根, p.中指中, p.中指末, false, weight);
            应用骨(_薬指１L, _薬指２L, _薬指３L, p.无名指根, p.无名指中, p.无名指末, false, weight);
            应用骨(_薬指１R, _薬指２R, _薬指３R, p.无名指根, p.无名指中, p.无名指末, false, weight);
            应用骨(_小指１L, _小指２L, _小指３L, p.小指根, p.小指中, p.小指末, false, weight);
            应用骨(_小指１R, _小指２R, _小指３R, p.小指根, p.小指中, p.小指末, false, weight);
        }

        void 应用骨(Transform 根, Transform 中, Transform 末, float 根角, float 中角, float 末角, bool 是拇指, float weight)
        {
            // 弯曲轴：四指 X+/X+/X-；拇指 Y+/Y+/Y+
            Vector3 轴根 = 是拇指 ? Vector3.up : Vector3.right;
            Vector3 轴中 = 是拇指 ? Vector3.up : Vector3.right;
            Vector3 轴末 = 是拇指 ? Vector3.up : Vector3.left; // 四指末节局部系翻 180°

            // 叠加：在 clip 写入的 localRotation 基础上，按权重叠加弯曲增量
            if (根 != null) 根.localRotation = 根.localRotation * Quaternion.AngleAxis(根角 * weight, 轴根);
            if (中 != null) 中.localRotation = 中.localRotation * Quaternion.AngleAxis(中角 * weight, 轴中);
            if (末 != null) 末.localRotation = 末.localRotation * Quaternion.AngleAxis(末角 * weight, 轴末);
        }
    }
}
