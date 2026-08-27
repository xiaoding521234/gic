using System.Collections.Generic;
using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 动作切换惯性化层（2026-08-27）——Gears of War 4 惯性化算法（GDC 2018 "Inertialization:
    /// High-Performance Animation Transitions"，UE5 内置 Inertialization 节点同源）的移植，
    /// 数学直译自 portalmk2/InertializationForUnity（GoW SIGGRAPH 2017 supplemental 论文实现）。
    ///
    /// 替代 CrossFade 的动机（旧方案三个结构性缺陷，2026-08-24 实测顿挫全在过渡层）：
    ///   ① 线性权重混合仅 C0 连续——切换瞬间姿态速度跳变、收尾急停，读作"一顿一顿"；
    ///   ② 双 clip 逐帧求值加权平均——大姿态差过渡读作"浆糊"（四肢半路姿势+位置平均塌陷），
    ///     且过渡期动画求值成本翻倍；
    ///   ③ 过渡被打断时从头重混——快速连切永远停在中途混合态。
    /// 惯性化：切换瞬间硬切到新 clip（单 clip 求值），把"旧姿势相对新姿势的偏移+当前速度"
    /// 记为后处理项，五次多项式（位置/速度/加速度在 t=tf 处全零=C2 连续）平滑归零——
    /// 姿态从当前可见姿势带着速度连续滑入新动作，任何切点/打断点都丝滑。
    ///
    /// 窗口形态（2026-08-27 夜定案=固定系数多项式，GoW 论文原始形态）：Trigger 后首个
    /// LateUpdate（此时动画已采样新 clip 首帧=target）一次性捕获每骨偏移 x0 与初速度 v0
    /// （含起步拉引），窗口内按流逝时间 t 求值 x(t)=五次(x0,v0,tf,t)——系数全程恒定。
    /// 演化：v1 每帧重解（tf=剩余时间）→ 停顿（v0≈0 起步平台期）+ 尾段 tf→0 刚度爆炸急收；
    /// v2 每帧重解+每帧重注拉引 → 复利加速（0.6s 窗口 0.2s 耗尽、峰值 10.8°/帧，用户目检
    /// "仿佛瞬移"实证）。固定系数版：起步即刻有运动（拉引一次性烘进 v0）、匀速推进、C2
    /// 收尾——三项俱备，且中途打断=从当前输出重新捕获，天然无缝。
    /// 幅度自适应拉引（2026-08-27 目检"大姿态切换像瞬移"后补）：拉引初速度∝偏移量，全额 2.5 下
    /// 80° 偏移起步 340°/s、前 0.1s 冲掉 37%——大姿态差被抢跑式吞噬读作瞬移，小偏移却因运动量小
    /// 看不出问题（"一些瞬移一些正常"的成因）。修正=偏移 <10° 全额拉引（保平台期疗效）、
    /// ≥60° 退到 30%（大姿态差摊满窗口成平滑曲线），见 拉引倍率()。
    ///
    /// 分层：LateUpdate(-200) 在 Animation 求值后、视线/手指/拖拽物理等叠加层之前——
    /// 叠加层读到的是惯性化后的动画姿势，各自叠加语义不变；本层只对动画流的姿态连续性负责。
    /// 骨列表=全部 clip 曲线绑定骨并集（PetSceneSyncTool 幂等填充）：非动画骨（眼球本体等
    /// 叠加层地盘）必须排除——它们不被动画每帧重写，纳入会把叠加层的写入误读为偏移去衰减。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class PetInertializer : MonoBehaviour
    {
        [Tooltip("启用惯性化过渡（关=PetAnimSwapper 回退 CrossFade 线性混合，A/B 对比用）")]
        [SerializeField] private bool 启用 = true;
        [Tooltip("参与惯性化的骨（全部 clip 曲线绑定骨并集）——Tools/桌宠/同步 PaimonPet 动作列表 幂等填充，勿手改")]
        [SerializeField] private List<Transform> 骨列表 = new List<Transform>();
        [Tooltip("起步拉引系数：捕获时给偏移注入朝新姿势的额外初速度=系数×偏移/窗口时长（一次性烘进多项式）——消除 v0≈0 的起步平台期（动作收尾近静止切换读作停顿）。0=纯速度承接（GoW 原味）；2.5=起步加速度恰为 0（单调无过冲的数学最优）。幅度自适应：大偏移自动降倍率（见 拉引全额偏移度）")]
        [Range(0f, 5f)]
        [SerializeField] private float 起步拉引系数 = 2.5f;
        [Tooltip("幅度自适应拉引（2026-08-27 目检\"大姿态切换像瞬移\"根治）：骨偏移角低于此值用全额拉引——小姿态切换靠全额快起步治平台期")]
        [Range(0f, 90f)]
        [SerializeField] private float 拉引全额偏移度 = 10f;
        [Tooltip("骨偏移角达到/超过此值时拉引退到最低倍率——大姿态差本身运动可见，全额拉引以 v0∝偏移 的速度前 0.1s 冲掉 37%（80° 偏移≈340°/s）读作瞬移")]
        [Range(0f, 180f)]
        [SerializeField] private float 拉引衰减参考度 = 60f;
        [Tooltip("大偏移下的拉引倍率下限（0.3=80° 偏移起步速度从 340°/s 降到 102°/s，整段过渡摊满窗口成平滑曲线；0=纯 GoW 速度承接）")]
        [Range(0f, 1f)]
        [SerializeField] private float 拉引最低倍率 = 0.3f;
        [Tooltip("大偏移窗口秒（2026-08-27 二轮根治\"所有切换可见停顿\"）：骨偏移达到衰减参考度时，该骨过渡窗口收缩到此时长——大姿态差本就该快切（原神级 ~0.3s），实测抓起/松手偏移 86-94°，0.6s 慢窗读作\"悬一下才沉下去\"=停顿感；小偏移保持 动作过渡秒 慢混。")]
        [Range(0.1f, 1f)]
        [SerializeField] private float 大偏移窗口秒 = 0.3f;

        // 姿态输出流（与骨列表平行）：prev/curr 为本层上一帧输出，供中断时捕获速度
        private Vector3[] _prevPos, _currPos;
        private Quaternion[] _prevRot, _currRot;
        private bool _已初始化;

        // 窗口状态（固定系数多项式）
        private float _触发时刻 = -999f; // 捕获时刻（Time.time）；窗口=[触发时刻, 触发时刻+本次时长]
        private float _本次时长 = 0.6f;
        private bool _首帧待捕获;         // Trigger 已请求：下个 LateUpdate（新 clip 已采样）捕获系数
        // 捕获的系数（与骨列表平行）
        private Vector3[] _偏移方向;      // 位置偏移单位向量（捕获时刻）
        private float[] _位置x0, _位置v0; // 位置偏移模长/初速度
        private Vector3[] _旋转轴;        // 旋转偏移轴（捕获时刻）
        private float[] _旋转x0, _旋转v0; // 旋转偏移角（弧度）/初速度
        private bool[] _有效;             // 该骨偏移是否值得惯性化（微小偏移跳过=纯跟动画）
        private float[] _旋转tf, _位置tf; // 每骨窗口时长（幅度自适应：大偏移收缩到 大偏移窗口秒，≤本次时长）

        // 诊断（2026-08-27 "所有切换可见停顿"定位）：每次捕获输出一行偏移/速度统计 +
        // 最大偏移骨的闭式衰减采样（剩余@0.1/0.2/0.3s）——从 Player.log 直接读出
        // 每次切换的"慢起步/反向起步/窗口过慢"客观形态，不再盲猜。
        [Tooltip("打印惯性化捕获诊断（每次动作切换一行：最大骨偏移/捕获速度方向统计/衰减曲线采样）——切换手感问题定位用，平时关")]
        [SerializeField] private bool 打印诊断 = true;

        /// <summary>惯性化是否可用（PetAnimSwapper 据此选路径：惯性化 or CrossFade 兜底）</summary>
        public bool 启用惯性化 => 启用 && enabled && 骨列表 != null && 骨列表.Count > 0;

        void LateUpdate()
        {
            if (骨列表 == null || 骨列表.Count == 0) return;
            if (!_已初始化) 初始化();

            bool 过渡中 = _首帧待捕获 || Time.time < _触发时刻 + _本次时长;
            if (!过渡中)
            {
                // 被动跟踪：此时骨=动画本帧姿势（叠加层尚未写入），维护 prev/curr 供下次 Trigger 捕获速度
                for (int i = 0; i < 骨列表.Count; i++)
                {
                    var 骨 = 骨列表[i];
                    if (骨 == null) continue;
                    _prevPos[i] = _currPos[i];
                    _prevRot[i] = _currRot[i];
                    _currPos[i] = 骨.localPosition;
                    _currRot[i] = 骨.localRotation;
                }
                return;
            }

            if (_首帧待捕获) 捕获窗口系数();

            float t = Time.time - _触发时刻; // 首帧 ≈0：x(0)=x0，输出=旧姿势（位置连续）
            for (int i = 0; i < 骨列表.Count; i++)
            {
                var 骨 = 骨列表[i];
                if (骨 == null) continue;
                Vector3 targetPos = 骨.localPosition;     // 新 clip 本帧姿势
                Quaternion targetRot = 骨.localRotation;

                // 输出流维护（中断捕获的速度来源）+ 写回
                _prevPos[i] = _currPos[i];
                _prevRot[i] = _currRot[i];

                if (_有效[i])
                {
                    float offP = 五次衰减(_位置x0[i], _位置v0[i], _位置tf[i], t);
                    float offR = 五次衰减(_旋转x0[i], _旋转v0[i], _旋转tf[i], t);
                    Vector3 pos = targetPos + _偏移方向[i] * offP;
                    Quaternion rot = Quaternion.AngleAxis(offR * Mathf.Rad2Deg, _旋转轴[i]) * targetRot;
                    _currPos[i] = pos;
                    _currRot[i] = rot;
                    骨.localPosition = pos;
                    骨.localRotation = rot;
                }
                else
                {
                    // 偏移微小（同 clip 重播等）：纯跟动画，流照常跟踪
                    _currPos[i] = targetPos;
                    _currRot[i] = targetRot;
                }
            }
        }

        /// <summary>触发惯性过渡（PetAnimSwapper 硬切新 clip 时调用）：系数在下个 LateUpdate
        /// 捕获（需新 clip 首帧已采样为 target）。过渡中重复触发=从当前输出重新捕获（打断无缝）。</summary>
        public void Trigger(float blend秒)
        {
            if (骨列表 == null || 骨列表.Count == 0) return;
            if (!_已初始化) 初始化(); // 首次触发（如出场动画在第一帧 Update 期）：先快照当前姿势作基准
            _本次时长 = Mathf.Max(0.01f, blend秒);
            _首帧待捕获 = true;
        }

        /// <summary>捕获窗口系数：此刻骨=新 clip 首帧（Animation 在 Update 后 LateUpdate 前已采样）。
        /// x0=上一帧输出−target；v0=捕获速度（输出流）+起步拉引（一次性）。中断时同理：
        /// curr=当前输出、prev=上一帧输出 → 捕获的是实际运动速度，物理且连续。</summary>
        void 捕获窗口系数()
        {
            _首帧待捕获 = false;
            _触发时刻 = Time.time;
            float dt = Mathf.Max(1f / 120f, Time.deltaTime); // dt 钳制：帧尖峰时不放大捕获速度
            float tf = _本次时长;

            // 诊断统计（见 打印诊断 注释）
            float 诊_maxOff = 0f, 诊_sumOff = 0f; int 诊_cnt = 0, 诊_反向 = 0, 诊_正中 = 0;
            int 诊_maxIdx = -1;
            float 诊_sumV0 = 0f;

            for (int i = 0; i < 骨列表.Count; i++)
            {
                var 骨 = 骨列表[i];
                _有效[i] = false;
                if (骨 == null) continue;
                Vector3 targetPos = 骨.localPosition;
                Quaternion targetRot = 骨.localRotation;
                Vector3 currPos = _currPos[i];
                Quaternion currRot = _currRot[i];

                // 位置：偏移方向+模长，v0=捕获速度沿偏移方向投影−拉引
                Vector3 off = currPos - targetPos;
                float x0 = off.magnitude;
                if (x0 > 1e-6f)
                {
                    Vector3 dir = off / x0;
                    float vCap = Vector3.Dot(currPos - _prevPos[i], dir) / dt;
                    _偏移方向[i] = dir;
                    _位置x0[i] = x0;
                    _位置tf[i] = tf; // 位置偏移微小（gi_pos_center 后近恒定），窗口时长不敏感
                    _位置v0[i] = vCap - x0 * (起步拉引系数 / tf);
                    _有效[i] = true;
                }

                // 旋转：偏移=curr·target⁻¹ 轴角，v0=单帧实际增量 dq 在偏移轴上的投影−拉引
                Quaternion q0 = currRot * Quaternion.Inverse(targetRot);
                if (q0.w < 0f) q0 = new Quaternion(-q0.x, -q0.y, -q0.z, -q0.w); // 角∈[0,π]，轴表示稳定
                float w = Mathf.Clamp(q0.w, -1f, 1f);
                float rx0 = 2f * Mathf.Acos(w);
                if (rx0 > 1e-4f)
                {
                    float s = Mathf.Sqrt(Mathf.Max(0f, 1f - w * w));
                    Vector3 axis = new Vector3(q0.x, q0.y, q0.z) / s;

                    // 速度捕获（2026-08-27 修复抽搐，骨级诊断实证）：上一帧→当前帧的实际旋转增量
                    // dq 投影到偏移轴——有界且物理（=切换瞬间旧动画真实角速度，GoW 论文本义）。
                    // 勿改回 qn1 轴角投影 atan2：未做半球配对，四元数表示符号一翻即跳 ±2π，
                    // 发丝链（+HairS）过渡期逐帧巨值 v0 摆动 → 90° 级抽搐（已实证）。
                    Quaternion dq = currRot * Quaternion.Inverse(_prevRot[i]);
                    if (dq.w < 0f) dq = new Quaternion(-dq.x, -dq.y, -dq.z, -dq.w);
                    float dw = Mathf.Clamp(dq.w, -1f, 1f);
                    float dAng = 2f * Mathf.Acos(dw); // 单帧增量本就小，半球规范化后无 2π 卷绕
                    float v0 = 0f;
                    if (dAng > 1e-5f)
                    {
                        float ds = Mathf.Sqrt(Mathf.Max(0f, 1f - dw * dw));
                        Vector3 dAxis = new Vector3(dq.x, dq.y, dq.z) / ds;
                        v0 = Mathf.Clamp((dAng / dt) * Mathf.Sign(Vector3.Dot(dAxis, axis)), -30f, 30f);
                    }
                    // 幅度自适应（2026-08-27 二轮）：大偏移骨的窗口收缩到 大偏移窗口秒
                    //（诊断实证抓起/松手 86-94° 偏移 × 0.6s 慢窗="悬一下才沉下去"停顿感；
                    // 大姿态差本就该 ~0.3s 快切）。拉引倍率同曲线降低，且拉引分母用收缩后的
                    // 骨窗（等效把起步速度维持在"全额拉引在慢窗下"的量级而非放大）。
                    float 进度 = 衰减进度(rx0);
                    float 骨窗 = Mathf.Min(tf, Mathf.Lerp(tf, Mathf.Max(0.1f, 大偏移窗口秒), 进度));
                    float 拉引v = rx0 * (起步拉引系数 * Mathf.Lerp(1f, 拉引最低倍率, 进度) / 骨窗);
                    _旋转轴[i] = axis;
                    _旋转x0[i] = rx0;
                    _旋转tf[i] = 骨窗;
                    if (x0 <= 1e-6f) _位置tf[i] = 骨窗; // 位置通道无独立判定时随旋转骨窗
                    _旋转v0[i] = v0 - 拉引v;
                    _有效[i] = true;

                    if (rx0 > 1e-3f) // 诊断只统计可感偏移骨（>0.06°）
                    {
                        诊_cnt++; 诊_sumOff += rx0;
                        if (rx0 > 诊_maxOff) { 诊_maxOff = rx0; 诊_maxIdx = i; }
                        诊_sumV0 += v0;
                        if (v0 > 0f) 诊_反向++;       // 捕获速度仍朝远离目标方向（起步先反向走）
                        if (Mathf.Abs(v0 - 拉引v) < 0.05f) 诊_正中++;
                    }
                }
            }

            if (打印诊断 && 诊_cnt > 0)
            {
                float maxDeg = 诊_maxOff * Mathf.Rad2Deg;
                string decay = "";
                if (诊_maxIdx >= 0)
                {
                    float x0 = _旋转x0[诊_maxIdx], v0 = _旋转v0[诊_maxIdx];
                    float tfm = _旋转tf[诊_maxIdx];
                    // 闭式求值最大偏移骨的衰减曲线采样（剩余比例，按该骨自己的窗口）
                    decay = $" 窗{tfm:F2}s 剩余@0.1s={100f * 五次衰减(x0, v0, tfm, 0.1f) / x0:F0}%" +
                            $" @0.2s={100f * 五次衰减(x0, v0, tfm, 0.2f) / x0:F0}%" +
                            $" @0.3s={100f * 五次衰减(x0, v0, tfm, 0.3f) / x0:F0}%";
                }
                Debug.Log($"[PetInertia] 捕获 骨{诊_cnt}/{骨列表.Count} 最大偏移{maxDeg:F1}°({(诊_maxIdx >= 0 && 骨列表[诊_maxIdx] != null ? 骨列表[诊_maxIdx].name : "?")}) " +
                          $"平均{诊_sumOff * Mathf.Rad2Deg / 诊_cnt:F2}° 反向v0骨数={诊_反向} 捕获速度均值={诊_sumV0 * Mathf.Rad2Deg / 诊_cnt:F1}°/s 窗口={tf:F2}s{decay}");
            }
        }

        /// <summary>衰减进度（0=小偏移全额，1=达到衰减参考度）：拉引倍率与窗口收缩共用此曲线，
        /// 保证"小差异慢混全额拉引、大差异快切低拉引"的一致语义。</summary>
        float 衰减进度(float 偏移弧度)
        {
            float 满额 = 拉引全额偏移度 * Mathf.Deg2Rad;
            float 参考 = 拉引衰减参考度 * Mathf.Deg2Rad;
            if (偏移弧度 <= 满额 || 参考 <= 满额) return 0f;
            return Mathf.Clamp01((偏移弧度 - 满额) / (参考 - 满额));
        }

        void 初始化()
        {
            int n = 骨列表.Count;
            _prevPos = new Vector3[n]; _currPos = new Vector3[n];
            _prevRot = new Quaternion[n]; _currRot = new Quaternion[n];
            _偏移方向 = new Vector3[n];
            _位置x0 = new float[n]; _位置v0 = new float[n];
            _旋转轴 = new Vector3[n];
            _旋转x0 = new float[n]; _旋转v0 = new float[n];
            _旋转tf = new float[n]; _位置tf = new float[n];
            for (int i = 0; i < n; i++) { _旋转tf[i] = _本次时长; _位置tf[i] = _本次时长; }
            _有效 = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var 骨 = 骨列表[i];
                _prevPos[i] = _currPos[i] = 骨 != null ? 骨.localPosition : Vector3.zero;
                _prevRot[i] = _currRot[i] = 骨 != null ? 骨.localRotation : Quaternion.identity;
            }
            _已初始化 = true;
        }

        // ───────────── 惯性化数学（GoW4 五次多项式；角度一律弧度） ─────────────

        /// <summary>五次多项式衰减：x(0)=x0、x'(0)=v0、x''(0)=a0、x(tf)=x'(tf)=x''(tf)=0——
        /// 偏移带着当前速度与加速度连续滑到零（C2 连续，无急停）。v0 已朝目标运动且 1/5 行程
        /// 内可消化时自动收窄 tf（防过冲）；tf≈0 直接归零（多项式在 t=tf 恒为 0）。
        /// 固定系数形态下 x0/v0/tf 全程恒定 → 每次调用内部窄化结果一致，曲线稳定。</summary>
        static float 五次衰减(float x0, float v0, float tf, float t)
        {
            if (Mathf.Abs(v0) > 1e-9f)
            {
                float tf1 = -5f * x0 / v0;
                if (tf1 > 0f) tf = Mathf.Min(tf, tf1);
            }
            if (tf < 1e-5f) return 0f;
            t = Mathf.Min(t, tf);

            float tf2 = tf * tf, tf3 = tf2 * tf, tf4 = tf3 * tf, tf5 = tf4 * tf;
            float a0 = (-8f * v0 * tf - 20f * x0) / tf2;
            float A = -(a0 * tf2 + 6f * v0 * tf + 12f * x0) / (2f * tf5);
            float B = (3f * a0 * tf2 + 16f * v0 * tf + 30f * x0) / (2f * tf4);
            float C = -(3f * a0 * tf2 + 12f * v0 * tf + 20f * x0) / (2f * tf3);

            float t2 = t * t, t3 = t2 * t, t4 = t3 * t, t5 = t4 * t;
            return A * t5 + B * t4 + C * t3 + 0.5f * a0 * t2 + v0 * t + x0;
        }
    }
}
