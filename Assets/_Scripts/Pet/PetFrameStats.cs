using UnityEngine;

namespace GIC.Pet
{
    /// <summary>跨组件诊断标记（顿挫定位）：各子系统把最近一次动作时刻记在这里，
    /// PetFrameStats 在掉帧时刻回查"刚才发生了什么"做相关性分析。</summary>
    public static class PetDiag
    {
        public static float 上次蒙皮重烘 = -999f;  // PetWindowController BakeMesh
        public static float 上次眨眼 = -999f;      // PetBlinkController 眨眼开始
        public static float 上次GC = -999f;        // PetFrameStats 逐帧检测
    }

    /// <summary>
    /// 顿挫诊断遥测 v4（2026-08-24）：v1-v3 已证实——稳态 6ms/165fps 干净（钩子/重烘已修），
    /// 但"动作播放期间"出现 0.3-0.5s 的 15-35ms 掉帧串（用户目检：大摆动一顿一顿）。
    /// v4 = 针对性实验：逐阶段循环播放 Greet（大摆动）/Standby（微动）/Nod（小动作），
    /// 关掉其余全部子系统，直接测每档动画的每帧成本；再关动画采样、再空渲染，分层定位
    /// 成本在"曲线采样/CrossFade/蒙皮/渲染"哪一层。诊断完成后删除本组件即可。
    /// </summary>
    public class PetFrameStats : MonoBehaviour
    {
        [Tooltip("每阶段时长秒")] [SerializeField] private float 统计间隔秒 = 12f;
        [Tooltip("运行针对性实验（关=false 只做常规统计）")] [SerializeField] private bool 自动二分 = true;

        private float _t, _min = float.MaxValue, _max;
        private double _sum;
        private int _n;
        private int _gc0Base, _hitch15, _hitch30;
        private int _lastGc0;
        private long _heapBase;
        private int _phase = -1;
        private string _phaseName = "未开始";
        private float _warmup; // 阶段切换后 1s 预热不计入（CrossFade 尾巴/状态切换瞬态）

        // 实验对象（Start 缓存）
        private PetBehaviorController _behavior;
        private PetLookAtController _lookAt;
        private PaimonDropShadowController _dropShadow;
        private PetFingerPoseController _finger;
        private PetBlinkController _blink;
        private PetEmotionController _emotion;
        private Animation _anim;
        private Camera _cam;
        private int _camMaskBackup;

        // 2026-08-27 对齐 GI 直读路线：clip 名无 _MMD 后缀（旧 _MMD 名随 MMD 动画退出使用已不存在，
        // 按旧名播放会静默失败=实验组无效）。原 NodName 常量从未被引用，删。
        private static readonly string[] Phases =
        {
            "0基线全开(自然)",
            "1纯Greet循环(全关对照)",
            "2Greet+眨眼",
            "3Greet+视线",
            "4Greet+阴影",
            "5Greet↔Standby切换(测CrossFade)",
            "6恢复全开",
        };

        private const string GreetName = "Ani_NPC_Kanban_Paimon_Greet";
        private const string StandbyName = "Ani_NPC_Kanban_Paimon_Standby";
        private float _fadeTimer; // 阶段5：每 3s 在 Greet/Standby 间 CrossFade（模拟真实切换节奏）

        void Start()
        {
            var res = Screen.currentResolution;
            Debug.Log($"[PetFrameStats] 启动 vSyncCount={QualitySettings.vSyncCount} targetFrameRate={Application.targetFrameRate} " +
                      $"GCMode={UnityEngine.Scripting.GarbageCollector.GCMode} " +
                      $"refresh={(res.refreshRateRatio.denominator != 0 ? (float)res.refreshRateRatio.numerator / res.refreshRateRatio.denominator : 0f):F1}Hz " +
                      $"backbuffer={Screen.width}x{Screen.height}");

            _behavior = GetComponent<PetBehaviorController>();
            _lookAt = GetComponent<PetLookAtController>();
            _dropShadow = GetComponent<PaimonDropShadowController>();
            _finger = GetComponent<PetFingerPoseController>();
            _blink = GetComponent<PetBlinkController>();
            _emotion = GetComponent<PetEmotionController>();
            _anim = GetComponent<Animation>();
            _cam = Camera.main;
            if (_cam != null) _camMaskBackup = _cam.cullingMask;

            EnterPhase(0);
        }

        void EnterPhase(int p)
        {
            _phase = p;
            _phaseName = Phases[p];
            Debug.Log($"[PetFrameStats] ===== 进入阶段 {_phaseName} =====");
            ApplyPhase(p);
            _t = 0; _sum = 0; _n = 0; _min = float.MaxValue; _max = 0;
            _hitch15 = 0; _hitch30 = 0; _warmup = 1f;
            _gc0Base = System.GC.CollectionCount(0);
            _heapBase = System.GC.GetTotalMemory(false);
        }

        /// <summary>阶段语义：v5 矩阵——1 全关对照；2-4 = Greet 循环 + 单独开一个嫌疑层；
        /// 5 = Greet↔Standby 每 3s CrossFade（复现真实切换节奏，测过渡成本）。</summary>
        void ApplyPhase(int p)
        {
            bool diagOn = p >= 1 && p <= 5; // 实验阶段：默认全关，按阶段单独开被测层
            if (_behavior != null) _behavior.enabled = !diagOn;
            if (_lookAt != null) _lookAt.enabled = !diagOn || p == 3;
            if (_dropShadow != null) _dropShadow.enabled = !diagOn || p == 4;
            if (_finger != null) _finger.enabled = !diagOn;
            if (_blink != null) _blink.enabled = !diagOn || p == 2;
            if (_emotion != null) _emotion.enabled = !diagOn;
            _fadeTimer = 0f;
            if (_anim != null)
            {
                if (p >= 1 && p <= 5)
                {
                    var st = _anim[GreetName];
                    if (st != null)
                    {
                        st.wrapMode = WrapMode.Loop;
                        _anim.Stop();
                        _anim.Play(GreetName); // 无 CrossFade 起步（阶段 5 内部自行触发切换）
                    }
                    else Debug.LogWarning($"[PetFrameStats] clip 未注册: {GreetName}");
                }
                else if (p == 6)
                {
                    var st = _anim[StandbyName];
                    if (st != null) { st.wrapMode = WrapMode.Loop; _anim.CrossFade(StandbyName, 0.2f); }
                }
            }
            if (_cam != null) _cam.cullingMask = _camMaskBackup;
        }

        void Update()
        {
            // 阶段 5：每 3s 在 Greet↔Standby 间 CrossFade（复现真实动作切换）
            if (_phase == 5 && _anim != null)
            {
                _fadeTimer += Time.deltaTime;
                if (_fadeTimer >= 3f)
                {
                    _fadeTimer = 0f;
                    var cur = _anim.IsPlaying(GreetName) ? StandbyName : GreetName;
                    var st = _anim[cur];
                    if (st != null) { st.wrapMode = WrapMode.Loop; _anim.CrossFade(cur, 0.2f); }
                }
            }

            if (自动二分 && _t >= 统计间隔秒 && _phase < Phases.Length - 1)
            {
                ReportPhase();
                EnterPhase(_phase + 1);
                return;
            }

            float dt = Time.unscaledDeltaTime;
            float ms = dt * 1000f;

            int gcNow = System.GC.CollectionCount(0);
            if (gcNow != _lastGc0) { _lastGc0 = gcNow; PetDiag.上次GC = Time.unscaledTime; }

            bool count = _warmup <= 0f; // 预热帧不计入统计
            if (_warmup > 0f) _warmup -= dt;

            _t += dt;
            if (count)
            {
                _n++; _sum += dt;
                if (dt < _min) _min = dt;
                if (dt > _max) _max = dt;
                if (ms >= 15f && ms < 30f) _hitch15++;
                else if (ms >= 30f) _hitch30++;
            }

            if (ms >= 15f)
            {
                float now = Time.unscaledTime;
                Debug.Log($"[PetFrameStats] HITCH t={now:F2} [{_phaseName}] dt={ms:F1}ms " +
                          $"gcΔ={(now - PetDiag.上次GC) * 1000:F0}ms 烘焙Δ={(now - PetDiag.上次蒙皮重烘) * 1000:F0}ms 眨眼Δ={(now - PetDiag.上次眨眼) * 1000:F0}ms");
            }

            if (_t >= 统计间隔秒 && !自动二分)
            {
                ReportPhase();
                _t = 0; _sum = 0; _n = 0; _min = float.MaxValue; _max = 0;
                _hitch15 = 0; _hitch30 = 0;
                _gc0Base = System.GC.CollectionCount(0);
                _heapBase = System.GC.GetTotalMemory(false);
            }
        }

        void ReportPhase()
        {
            int gc = System.GC.CollectionCount(0) - _gc0Base;
            long heapNow = System.GC.GetTotalMemory(false);
            float sec = Mathf.Max((float)_sum, 0.001f);
            Debug.Log($"[PetFrameStats] 阶段[{_phaseName}] {_n}f avg={1000.0 * _sum / Mathf.Max(_n, 1):F2}ms min={1000f * _min:F2}ms max={1000f * _max:F2}ms " +
                      $"掉帧(15-30ms)={_hitch15} (≥30ms)={_hitch30} GC={gc}次/{sec:F0}s 堆Δ={(heapNow - _heapBase) / 1024f:F0}KB 堆={heapNow / 1048576f:F1}MB");
        }
    }
}
