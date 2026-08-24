using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 桌宠行为层（2026-08-24，docs/19 §3.3 待机动作设计落地）：
    /// 待机循环之上叠加事件驱动动作——出场（进程启动播 Appear 后回待机）；
    /// 光标在派蒙旁停留 → 打招呼一次；久待机 → 随机小动作轮换；
    /// 退场（双击退出时播 Disappear，播完才真正关进程，由 PetWindowController 回调执行）。
    /// 播放统一走 PetAnimSwapper（情绪映射/手指姿态/CrossFade 过渡收敛在那一层），
    /// 本组件只管"何时播什么"。
    /// 接近判定：模型世界包围盒投影到屏幕矩形，扩边距后含光标即算"在旁边"——
    /// 光标在窗口外也可判定（坐标来自 PetWindowController 的全局轮询）。
    /// </summary>
    public class PetBehaviorController : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private PetAnimSwapper 动作播放器;
        [SerializeField] private PetWindowController 窗口控制器;
        [SerializeField] private PetBlinkController 眨眼控制器; // 单次动作期间静默（morph 重评估与动作叠加互相放大顿挫）
        [SerializeField] private PetLookAtController 视线控制器; // 出场/退场期间静默（仪式动作头链全交 clip，2026-08-24）
        [Tooltip("空 = Camera.main")] [SerializeField] private Camera 相机;
        [Tooltip("空 = 自动找非影子壳的蒙皮渲染器（用包围盒做接近判定）")] [SerializeField] private SkinnedMeshRenderer 蒙皮渲染器;

        [Header("待机与打招呼")]
        [SerializeField] private string 待机动作名 = "Ani_NPC_Kanban_Paimon_Standby_MMD";
        [SerializeField] private string 打招呼动作名 = "Ani_NPC_Kanban_Paimon_Greet_MMD";
        [Tooltip("光标距模型包围盒多少屏幕像素内算\"在旁边\"")] [SerializeField] private float 触发边距像素 = 90f;
        [Tooltip("光标停留多久触发打招呼")] [SerializeField] private float 触发停留秒 = 1.2f;
        [Tooltip("两次打招呼的最小间隔秒")] [SerializeField] private float 打招呼冷却秒 = 45f;

        [Header("随机小动作")]
        [SerializeField] private bool 启用随机小动作 = true;
        [Tooltip("随机轮换的单次动作（完整 clip 名，可增删）")]
        [SerializeField] private string[] 随机小动作列表 =
        {
            "Ani_NPC_Kanban_Paimon_Nod01_MMD",
            "Ani_NPC_Kanban_Paimon_ShakeHead01_MMD",
            "Ani_NPC_Kanban_Paimon_Sneer01_MMD",
            "Ani_NPC_Kanban_Paimon_Clap01_MMD",
            "Ani_NPC_Kanban_Paimon_Show_1_MMD",
            "Ani_NPC_Kanban_Paimon_Show_2_MMD",
            "Ani_NPC_Kanban_Paimon_Show_3_MMD",
            "Ani_NPC_Kanban_Paimon_Show_4_MMD",
        };
        [Tooltip("随机小动作的间隔范围（秒）")] [SerializeField] private Vector2 小动作间隔秒 = new Vector2(25f, 55f);

        [Header("出场/退场")]
        [Tooltip("进程启动后播的出场动画（完整 clip 名，空=直接待机）——首帧在 PetAnimSwapper 预热完成后播放，播完回待机")]
        [SerializeField] private string 出场动画名 = "Ani_NPC_Kanban_Paimon_Appear_MMD";
        [Tooltip("双击退出时播的退场动画（完整 clip 名，空=立即退出）——播完才真正退出进程")]
        [SerializeField] private string 退场动画名 = "Ani_NPC_Kanban_Paimon_Disappear_MMD";

        // 运行时状态
        private bool _单次进行中;
        private string _当前单次名;
        private float _单次开始;
        private float _接近计时;
        private float _上次打招呼 = -999f;
        private float _下次小动作时刻;
        private readonly Vector3[] _包围盒角点 = new Vector3[8];
        private bool _出场未播 = true;      // 首帧播出场动画（所有 Start 完成后=预热已回待机）
        private bool _退场中;                // 退场动画进行中：屏蔽一切行为触发
        private System.Action _退场完成回调; // 退场动画播完执行（进程关闭，由窗口控制器注入）
        private bool _仪式静默中;            // 出场/退场动画期间：眨眼+视线层静默（收尾统一解除）

        void Start()
        {
            if (相机 == null) 相机 = Camera.main;
            if (蒙皮渲染器 == null)
            {
                var shadowLayer = LayerMask.NameToLayer("PaimonShadow");
                foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (!smr.name.Contains("_DropShadow") && smr.gameObject.layer != shadowLayer)
                    { 蒙皮渲染器 = smr; break; }
            }
            _下次小动作时刻 = Time.time + Random.Range(小动作间隔秒.x, 小动作间隔秒.y);
        }

        void Update()
        {
            if (动作播放器 == null || 窗口控制器 == null || 相机 == null) return;

            // 退场收尾：退场动画播完 → 交回退出回调（进程关闭由 PetWindowController 执行）。
            // 编辑器下 Application.Quit 无效——退完落回 _单次进行中 分支自然回待机，可反复目检
            if (_退场中)
            {
                if (Time.time - _单次开始 > 0.25f && !动作播放器.是否在播(_当前单次名))
                {
                    _退场中 = false;
                    var cb = _退场完成回调;
                    _退场完成回调 = null;
                    cb?.Invoke();
                }
                return;
            }

            // 出场：首帧（全部 Start 已完成 = PetAnimSwapper 预热已回待机）播出场动画；
            // 播完走下方 _单次进行中 分支自然回待机；无出场动画则直接进正常待机逻辑。
            // 仪式静默（眨眼+视线）由 单次进行中 收尾统一解除
            if (_出场未播)
            {
                _出场未播 = false;
                if (动作播放器.动作存在(出场动画名))
                {
                    置仪式静默(true);
                    播单次(出场动画名);
                }
            }

            if (_单次进行中)
            {
                // 单次动作播完 → 回待机循环（0.25s 宽限防 CrossFade 起始帧误判结束）
                if (Time.time - _单次开始 > 0.25f && !动作播放器.是否在播(_当前单次名))
                {
                    _单次进行中 = false;
                    if (_仪式静默中) 置仪式静默(false); // 仪式（出场）静默解除；普通单次动作本来就没静默视线
                    else 眨眼控制器?.Set静默(false);
                    窗口控制器.暂停命中烘焙 = false;
                    动作播放器.Play(待机动作名);
                }
                return; // 单次动作进行中不叠新触发
            }

            bool 接近 = 检测光标接近();

            if (接近 && Time.time - _上次打招呼 >= 打招呼冷却秒)
            {
                _接近计时 += Time.deltaTime;
                if (_接近计时 >= 触发停留秒)
                {
                    播单次(打招呼动作名);
                    _上次打招呼 = Time.time;
                    _接近计时 = 0f;
                    return;
                }
            }
            else
            {
                _接近计时 = 0f;
            }

            // 随机小动作：光标不在旁边时才轮换（在旁时留给打招呼/视线跟随，避免动作打架）
            if (启用随机小动作 && !接近 && 随机小动作列表.Length > 0 && Time.time >= _下次小动作时刻)
            {
                播单次(随机小动作列表[Random.Range(0, 随机小动作列表.Length)]);
                _下次小动作时刻 = Time.time + Random.Range(小动作间隔秒.x, 小动作间隔秒.y);
            }
        }

        void 播单次(string 动作名)
        {
            _当前单次名 = 动作名;
            _单次进行中 = true;
            _单次开始 = Time.time;
            眨眼控制器?.Set静默(true);
            窗口控制器.暂停命中烘焙 = true; // 动作期间停 MeshCollider 重烘（烘焙=掉帧尖峰，2026-08-24）
            动作播放器.PlayOnce(动作名);
        }

        /// <summary>出场/退场等仪式动作的静默组合：眨眼+视线跟随一起停（头链完全交给动画曲线）</summary>
        void 置仪式静默(bool 静默)
        {
            _仪式静默中 = 静默;
            眨眼控制器?.Set静默(静默);
            视线控制器?.Set视线静默(静默);
        }

        /// <summary>请求退场：播退场动画，播完执行回调（返回 false = 无退场动画可用，调用方直接退出）。
        /// 退场期间本组件屏蔽一切行为触发（打招呼/小动作/视线判定）。重复请求（退场中再双击）不重播。</summary>
        public bool 请求退场(System.Action 退场完成回调)
        {
            if (_退场中) return true; // 已在退场流程，等待当前动画收尾
            if (动作播放器 == null || !动作播放器.动作存在(退场动画名)) return false;
            置仪式静默(true); // 仪式静默持续到进程退出（收尾回调后无后续帧）
            播单次(退场动画名);
            _退场中 = true;
            _退场完成回调 = 退场完成回调;
            return true;
        }

        /// <summary>模型世界包围盒 → 屏幕矩形（扩边距）→ 是否含光标</summary>
        bool 检测光标接近()
        {
            if (蒙皮渲染器 == null || 窗口控制器.正在拖拽) return false;
            if (!窗口控制器.TryGetCursorUnityScreenPos(out Vector2 sp)) return false;

            var b = 蒙皮渲染器.bounds;
            int i = 0;
            for (int xi = 0; xi < 2; xi++)
                for (int yi = 0; yi < 2; yi++)
                    for (int zi = 0; zi < 2; zi++)
                        _包围盒角点[i++] = new Vector3(
                            xi == 0 ? b.min.x : b.max.x,
                            yi == 0 ? b.min.y : b.max.y,
                            zi == 0 ? b.min.z : b.max.z);

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var c in _包围盒角点)
            {
                Vector3 p = 相机.WorldToScreenPoint(c);
                if (p.z <= 0f) return true; // 包围盒跨到相机后（透视边缘，理论不发生）保守视为接近
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
            }
            return sp.x >= minX - 触发边距像素 && sp.x <= maxX + 触发边距像素
                && sp.y >= minY - 触发边距像素 && sp.y <= maxY + 触发边距像素;
        }
    }
}
