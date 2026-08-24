using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 派蒙视线跟随（docs/19 §3.4）——头/颈/眼球分层看向鼠标，LateUpdate 在 clip 基础姿势上叠加。
    ///
    /// 分层叠加（对齐手指/眨眼层的约定）：
    ///   clip 曲线 = 基础姿势；本组件 = 视线叠加层。世界空间增量法：不重设骨骼姿态，
    ///   只在动画写入的世界旋转上叠加旋转增量——动画微动（呼吸/摇晃）完整保留。
    ///
    /// 骨骼链：首（分担部分偏转）→ 頭（承担余量）→ 目.L/目.R（相对头部视线的余量快跟随）。
    /// 父骨先应用、子骨后读取世界旋转，子骨增量自动包含父级已叠加的部分。
    ///
    /// 鼠标来源：PetWindowController.TryGetCursorUnityScreenPos（Win32 全局轮询，窗口无焦点/
    /// 穿透也能追踪；光标在窗口外同样有效，派蒙可追到屏幕任意角落）。
    ///
    /// 角度映射（v5，2026-08-23）：
    ///   偏转量 = 光标相对屏幕中心的归一化偏移 × 最大角，绕世界 up/right 轴叠加到骨骼世界旋转上。
    ///   全屏线性跟随（光标远近连续变化）；yaw/pitch 独立映射但同源于连续屏幕坐标，头顶无翻转奇点。
    ///   相机须在派蒙正面（-Z 侧看 +Z），派蒙面向相机——对齐主流桌宠（DesktopMate/Shimeji）。
    ///   颈/头分担之和恒为 1（颈 颈分担 + 头 1-颈分担）——v4 头叠加满份致总偏转 1.35× 过冲。
    ///   眼球（本地基线重建 + 余量）：clip 中目.L/目.R 本体无曲线（曲线在 目戻/_dummy/_shadow
    ///   子骨上）、动画不覆写、上帧叠加会残留——v3/v4 在残留上每帧累积增量致复利式自转
    ///   只剩眼白（鼠标不动也转）。现每帧用启动时本地基线重建中性姿态（父骨已应用旋转
    ///   × 本地基线快照），再叠加余量：余量 = inv(头链本帧实际叠加增量) × 目标增量
    ///   （基准=本帧动画姿态，不取首帧快照——v4 首帧快照会把待机摇晃漏进眼球反向补偿）。
    ///   头未转满时眼球快速补足（≤眼球最大角），头收敛后余量归零、眼球随动画回中。
    /// </summary>
    public class PetLookAtController : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("光标来源（PetWindow 上的 PetWindowController）")]
        [SerializeField] private PetWindowController 窗口控制器;
        [Tooltip("空 = Camera.main")] [SerializeField] private Camera 相机;

        [Header("跟随范围")]
        [Tooltip("光标偏屏幕中心多少像素时顶满最大偏转角（全屏线性跟随的标尺）")]
        [SerializeField] private float 屏幕最大偏移 = 700f;

        [Header("角度限制（度）")]
        [Tooltip("水平偏转上限")] [SerializeField] private float 水平最大角 = 30f;
        [Tooltip("垂直偏转上限（抬头/低头同值）")] [SerializeField] private float 垂直最大角 = 22f;
        [Tooltip("偏转由颈分担的比例，其余由头承担")] [Range(0f, 1f)]
        [SerializeField] private float 颈分担 = 0.35f;

        [Header("眼球跟随")]
        [SerializeField] private bool 启用眼球 = true;
        [Tooltip("眼球相对头部视线的追加偏转上限（度）")]
        [SerializeField] private float 眼球最大角 = 15f;

        [Header("平滑")]
        [Tooltip("头/颈指数阻尼速度（越大跟随越快）")]
        [SerializeField] private float 头平滑速度 = 8f;
        [Tooltip("眼球指数阻尼速度（眼球比头快，模拟真实扫视）")]
        [SerializeField] private float 眼球平滑速度 = 14f;
        [Tooltip("关闭 = 完全走 clip 曲线")] [SerializeField] private bool 启用 = true;

        // 视线静默（出场/退场等仪式动作期间，2026-08-24）：LateUpdate 整体跳过，头链完全交给 clip。
        // 恢复时平滑值可能已偏离——首帧先同步到当前骨骼姿态再叠加，防视线"瞬移归位"
        private bool _静默;
        private bool _刚恢复; // 静默→恢复的过渡帧标记：重置平滑基准（动画动过头链，旧值失义），本帧不叠加

        /// <summary>仪式动作（出场/退场）期间暂停视线跟随：头/颈/眼球全部交给动画曲线。
        /// 恢复非静默时置过渡标记——首帧用当前骨骼姿态重置平滑基准，防视线瞬移归位。</summary>
        public void Set视线静默(bool 静默)
        {
            if (_静默 == 静默) return;
            _静默 = 静默;
            if (!_静默) _刚恢复 = true; // 解除静默：下一帧重置基准
        }

        // 骨引用（启动时按名查，MMD 日文名）
        private Transform _首, _頭, _目L, _目R;
        // 首帧姿态快照（世界空间增量法的基准：此刻视为"沿中性视线注视"）
        private Quaternion _参考首, _参考头, _参考目L, _参考目R;
        // 眼球本地基线快照：目.L/目.R 在 clip 中无曲线、动画不覆写，以此为每帧重建基准
        private Quaternion _目本地基线L, _目本地基线R;
        private Quaternion _平滑首, _平滑头, _平滑目L, _平滑目R;
        private bool _已初始化;

        void Start()
        {
            if (相机 == null) 相机 = Camera.main;
            缓存骨引用();
            if (_頭 == null)
            {
                Debug.LogWarning("[PetLookAt] 未找到头骨（頭），视线跟随已禁用");
                enabled = false;
                return;
            }
            if (窗口控制器 == null)
            {
                Debug.LogWarning("[PetLookAt] 窗口控制器未接线，无法取光标位置");
            }
        }

        void 缓存骨引用()
        {
            var all = GetComponentsInChildren<Transform>(true);
            System.Func<string, Transform> F = n =>
            {
                foreach (var t in all) if (t.name == n) return t;
                return null;
            };
            _首 = F("首"); _頭 = F("頭");
            _目L = F("目.L"); _目R = F("目.R");
        }

        void LateUpdate()
        {
            if (!启用 || 相机 == null || _静默) return;

            // 静默恢复首帧：动画期间头链被 clip 驱动，旧平滑值失义——重置为当前姿态防瞬移，本帧不叠加
            if (_刚恢复)
            {
                _刚恢复 = false;
                _平滑首 = _首 != null ? _首.rotation : Quaternion.identity;
                _平滑头 = _頭.rotation;
                _平滑目L = _目L != null ? _目L.rotation : Quaternion.identity;
                _平滑目R = _目R != null ? _目R.rotation : Quaternion.identity;
                return;
            }

            // 首帧：动画采样完成后快照参考姿态（增量法基准），本帧不叠加
            if (!_已初始化)
            {
                _参考首 = _首 != null ? _首.rotation : Quaternion.identity;
                _参考头 = _頭.rotation;
                _参考目L = _目L != null ? _目L.rotation : Quaternion.identity;
                _参考目R = _目R != null ? _目R.rotation : Quaternion.identity;
                _目本地基线L = _目L != null ? _目L.localRotation : Quaternion.identity;
                _目本地基线R = _目R != null ? _目R.localRotation : Quaternion.identity;
                _平滑首 = _参考首; _平滑头 = _参考头; _平滑目L = _参考目L; _平滑目R = _参考目R;
                _已初始化 = true;
                return;
            }

            if (窗口控制器 == null || !窗口控制器.TryGetCursorUnityScreenPos(out Vector2 sp)) return;

            // 归一化屏幕偏移（-1..1）：光标在屏幕中心=0，到 屏幕最大偏移=±1
            Vector2 偏移 = new Vector2(
                Mathf.Clamp((sp.x - Screen.width * 0.5f) / 屏幕最大偏移, -1f, 1f),
                Mathf.Clamp((sp.y - Screen.height * 0.5f) / 屏幕最大偏移, -1f, 1f));

            // 目标增量：绕世界 up(yaw)/right(pitch) 轴，与骨骼局部系无关
            // Euler 约定：+X = 向下（docs/14 §8.1），故抬头用 -pitch
            // yaw 取负：相机在 +Z 看 -Z 时，世界 yaw 正方向与屏幕水平镜像（光标在左应向左看）
            float yaw = -偏移.x * 水平最大角;
            float pitch = 偏移.y * 垂直最大角;
            Quaternion 目标增量 = Quaternion.AngleAxis(yaw, Vector3.up)
                                * Quaternion.AngleAxis(-pitch, Vector3.right);

            // 先颈后头：父骨先应用，子骨后读取的世界旋转已含父级增量
            // 颈+头分担之和=1，总偏转恰为目标增量（v4 头叠加满份致 1.35× 过冲）
            Quaternion 头动画世界 = _頭.rotation; // 应用前快照本帧动画姿态：眼球余量的测量基准
            if (_首 != null)
                应用骨(_首, ref _平滑首, 目标增量, 颈分担, 头平滑速度);
            应用骨(_頭, ref _平滑头, 目标增量, 1f - 颈分担, 头平滑速度);

            // 眼球：余量 = inv(头链本帧实际叠加增量) × 目标增量——头未转满时眼球快速补足，
            // 头收敛后余量归零、眼球随动画回中（不反向顶眼白、不被待机摇晃带着自转）
            if (启用眼球)
            {
                Quaternion 头已转 = _頭.rotation * Quaternion.Inverse(头动画世界);
                Quaternion 眼余量 = Quaternion.Inverse(头已转) * 目标增量;
                眼余量.ToAngleAxis(out float 眼角, out Vector3 眼轴);
                if (眼角 > 180f) 眼角 -= 360f;
                眼角 = Mathf.Clamp(眼角, -眼球最大角, 眼球最大角);
                if (眼轴.sqrMagnitude > 1e-6f)
                {
                    眼轴.Normalize();
                    Quaternion 眼增量 = Quaternion.AngleAxis(眼角, 眼轴);
                    if (_目L != null) 应用眼球(_目L, _目本地基线L, ref _平滑目L, 眼增量);
                    if (_目R != null) 应用眼球(_目R, _目本地基线R, ref _平滑目R, 眼增量);
                }
            }
        }

        /// <summary>在动画世界旋转上叠加目标增量（share=叠加比例），指数阻尼平滑</summary>
        void 应用骨(Transform 骨, ref Quaternion 平滑, Quaternion 目标增量, float share, float 平滑速度)
        {
            Quaternion 增量 = Quaternion.Slerp(Quaternion.identity, 目标增量, share);
            应用骨增量(骨, ref 平滑, 增量, 平滑速度);
        }

        /// <summary>在动画世界旋转上叠加增量，指数阻尼平滑</summary>
        void 应用骨增量(Transform 骨, ref Quaternion 平滑, Quaternion 增量, float 平滑速度)
        {
            Quaternion 期望 = 增量 * 骨.rotation;
            float k = 1f - Mathf.Exp(-平滑速度 * Time.deltaTime);
            平滑 = Quaternion.Slerp(平滑, 期望, k);
            骨.rotation = 平滑;
        }

        /// <summary>眼球：本地基线重建中性姿态（父骨当前世界旋转 × 启动时本地快照）后叠加余量并平滑。
        /// 目骨在 clip 中无曲线、动画不覆写，不能在自身当前旋转上累积（会复利式自转）。</summary>
        void 应用眼球(Transform 眼, Quaternion 本地基线, ref Quaternion 平滑, Quaternion 余量)
        {
            Quaternion 期望 = 余量 * (眼.parent.rotation * 本地基线);
            float k = 1f - Mathf.Exp(-眼球平滑速度 * Time.deltaTime);
            平滑 = Quaternion.Slerp(平滑, 期望, k);
            眼.rotation = 平滑;
        }
    }
}
