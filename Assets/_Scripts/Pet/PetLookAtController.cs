using UnityEngine;
using UnityEngine.Serialization;

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
        [Tooltip("光标来源（PetWindow 上的宿主控制器）。2026-08-28 类型放宽为 PetHostBase 共用基类——场景引用按字段名保留不变")]
        [InspectorName("窗口控制器")]
        [SerializeField] private PetHostBase winController;
        [InspectorName("相机")]
        [Tooltip("空 = Camera.main")] [SerializeField] private Camera mainCamera;

        [Header("跟随范围")]
        [Tooltip("光标偏屏幕中心多少像素时顶满最大偏转角（全屏线性跟随的标尺）")]
        [InspectorName("屏幕最大偏移")]
        [SerializeField] private float screenMaxOffset = 700f;

        [Header("角度限制（度）")]
        [InspectorName("水平最大角")]
        [Tooltip("水平偏转上限")] [SerializeField] private float hMaxAngle = 30f;
        [InspectorName("垂直最大角")]
        [Tooltip("垂直偏转上限（抬头/低头同值）")] [SerializeField] private float vMaxAngle = 22f;
        [Tooltip("偏转由颈分担的比例，其余由头承担")] [Range(0f, 1f)]
        [InspectorName("颈分担")]
        [SerializeField] private float neckShare = 0.35f;

        [Header("眼球跟随")]
        [InspectorName("启用眼球")]
        [SerializeField] private bool enableEye = true;
        [Tooltip("眼球相对头部视线的追加偏转上限（度）")]
        [InspectorName("眼球最大角")]
        [SerializeField] private float eyeMaxAngle = 15f;

        [Header("平滑")]
        [Tooltip("头/颈指数阻尼速度（越大跟随越快）")]
        [InspectorName("头平滑速度")]
        [SerializeField] private float headSmoothSpeed = 8f;
        [Tooltip("眼球指数阻尼速度（眼球比头快，模拟真实扫视）")]
        [InspectorName("眼球平滑速度")]
        [SerializeField] private float eyeSmoothSpeed = 14f;
        [InspectorName("启用")]
        [Tooltip("关闭 = 完全走 clip 曲线")] [SerializeField] private bool enableLookAt = true;

        [Header("骨骼名（2026-08-29 默认值对齐 GI 官方模型骨架——模型基底已是 GI（Bip001/+EyeBone），旧 MMD 日文名默认值是遗留；曾致命名迁移丢场景值后查骨必失败、视线跟随全灭）")]
        [InspectorName("颈骨名")]
        [SerializeField] private string neckBoneName = "Bip001 Neck";
        [InspectorName("头骨名")]
        [SerializeField] private string headBoneName = "Bip001 Head";
        [InspectorName("左眼球骨名")]
        [SerializeField] private string leftEyeBoneName = "+EyeBone L A01";
        [InspectorName("右眼球骨名")]
        [SerializeField] private string rightEyeBoneName = "+EyeBone R A01";

        // 视线静默（出场/退场等仪式动作期间，2026-08-24）：头链完全交给 clip。
        // 恢复时平滑值可能已偏离——首帧先同步到当前骨骼姿态再叠加，防视线"瞬移归位"
        private bool _silent;
        private bool _justResumed; // 静默→恢复的过渡帧标记：重置平滑基准（动画动过头链，旧值失义），本帧不叠加
        private float _silenceFadeWeight;
        // 淡出速率（指数）：7/s ≈ 0.3s 收敛到 5% 以下——与头平滑速度同量级，读作"目光自然收回"
        private const float silenceFadeRate = 7f;
        // 静默进入淡出（2026-08-27）：进入静默≠瞬停——头链正带着最多水平30°+垂直22°的视线偏转，
        // 硬停写=单帧弹回（抓起/退场瞬间头部"啪"地跳变；惯性化让其余骨全平滑后这一下格外扎眼，
        // 2026-08-27 目检"一些切换像瞬移"的第二根因）。淡出期间冻结目标=零增量继续写骨
        // （平滑值向"无偏转的动画姿态"收敛，与惯性化过渡叠加仍然平滑），权重耗尽才真正交还 clip。

        /// <summary>仪式动作（出场/退场/拎起）期间暂停视线跟随：头/颈/眼球全部交给动画曲线。
        /// 进入时若视线层正在写骨则先淡出（约 0.3s 指数收敛，防头部视线偏转单帧弹回）；
        /// 恢复非静默时置过渡标记——首帧用当前骨骼姿态重置平滑基准，防视线瞬移归位。</summary>
        public void SetSilence(bool silent)
        {
            if (_silent == silent) return;
            _silent = silent;
            if (silent) _silenceFadeWeight = _initialized ? 1f : 0f; // 尚未写过视线（如出场前）则无偏转可淡出
            else _justResumed = true; // 解除静默：下一帧重置基准
        }

        // 骨引用（启动时按名查，MMD 日文名）
        private Transform _neckBone, _headBone, _eyeLBone, _eyeRBone;
        // 首帧姿态快照（世界空间增量法的基准：此刻视为"沿中性视线注视"）
        private Quaternion _refNeck, _refHead, _refEyeL, _refEyeR;
        // 眼球本地基线快照：目.L/目.R 在 clip 中无曲线、动画不覆写，以此为每帧重建基准
        private Quaternion _eyeBaselineL, _eyeBaselineR;
        private Quaternion _smoothNeck, _smoothHead, _smoothEyeL, _smoothEyeR;
        private bool _initialized;

        void Start()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            CacheBoneRefs();
            if (_headBone == null)
            {
                Debug.LogWarning("[PetLookAt] 未找到头骨（頭），视线跟随已禁用");
                enableLookAt = false;
                return;
            }
            if (winController == null && PetInGameHost.HostInterface == null)
            {
                Debug.LogWarning("[PetLookAt] 窗口控制器未接线，无法取光标位置");
            }
        }

        void CacheBoneRefs()
        {
            var all = GetComponentsInChildren<Transform>(true);
            System.Func<string, Transform> F = n =>
            {
                foreach (var t in all) if (t.name == n) return t;
                return null;
            };
            _neckBone = F(neckBoneName); _headBone = F(headBoneName);
            _eyeLBone = F(leftEyeBoneName); _eyeRBone = F(rightEyeBoneName);
        }

        void LateUpdate()
        {
            if (!enableLookAt || mainCamera == null) return;

            // 静默淡出期（进入静默后的短过渡）：输出 = Slerp(动画姿态, 平滑值, 权重)——
            // 权重 1 起步=纯平滑输出（与抓取前视线连续），权重→0=纯动画姿态；权重耗尽停写时
            // 输出已恒等于动画姿态，结构上无跳变（v1 绝对覆写平滑值：追赶滞后在停写瞬间一次性
            // 释放=目检"转身完成瞬间头角度突变"的根因，2026-08-27）
            if (_silent)
            {
                if (!_initialized || _silenceFadeWeight <= 0.01f) return; // 淡出完毕：头链完全交给 clip
                _silenceFadeWeight *= Mathf.Exp(-silenceFadeRate * Time.deltaTime);
                float w = _silenceFadeWeight;
                if (_neckBone != null) silenceFadeWriteBone(_neckBone, ref _smoothNeck, w, headSmoothSpeed);
                if (_headBone != null) silenceFadeWriteBone(_headBone, ref _smoothHead, w, headSmoothSpeed);
                if (enableEye)
                {
                    if (_eyeLBone != null) silenceFadeWriteBone(_eyeLBone, ref _smoothEyeL, w, eyeSmoothSpeed);
                    if (_eyeRBone != null) silenceFadeWriteBone(_eyeRBone, ref _smoothEyeR, w, eyeSmoothSpeed);
                }
                return;
            }

            // 静默恢复首帧：动画期间头链被 clip 驱动，旧平滑值失义——重置为当前姿态防瞬移，本帧不叠加
            if (_justResumed)
            {
                _justResumed = false;
                _smoothNeck = _neckBone != null ? _neckBone.rotation : Quaternion.identity;
                _smoothHead = _headBone.rotation;
                _smoothEyeL = _eyeLBone != null ? _eyeLBone.rotation : Quaternion.identity;
                _smoothEyeR = _eyeRBone != null ? _eyeRBone.rotation : Quaternion.identity;
                return;
            }

            // 首帧：动画采样完成后快照参考姿态（增量法基准），本帧不叠加
            if (!_initialized)
            {
                _refNeck = _neckBone != null ? _neckBone.rotation : Quaternion.identity;
                _refHead = _headBone.rotation;
                _refEyeL = _eyeLBone != null ? _eyeLBone.rotation : Quaternion.identity;
                _refEyeR = _eyeRBone != null ? _eyeRBone.rotation : Quaternion.identity;
                _eyeBaselineL = _eyeLBone != null ? _eyeLBone.localRotation : Quaternion.identity;
                _eyeBaselineR = _eyeRBone != null ? _eyeRBone.localRotation : Quaternion.identity;
                _smoothNeck = _refNeck; _smoothHead = _refHead; _smoothEyeL = _refEyeL; _smoothEyeR = _refEyeR;
                _initialized = true;
                return;
            }

            // 宿主分发（IPetHost，2026-08-27 批次 B）：桌面=窗口控制器字段（PetHostBase）；游戏内=PetInGameHost 注入
            var host = winController != null ? winController : (IPetHost)PetInGameHost.HostInterface;
            if (host == null || !host.TryGetCursorUnityScreenPos(out Vector2 sp)) return;

            // 归一化偏移（-1..1）基准=头骨屏幕投影（2026-08-25 修复：原以屏幕中心为基准——
            // 派蒙不在窗口中心（缩放改变屏幕占比+构图本就偏置），鼠标与眼睛水平时
            // sp.y-Screen.height*0.5≠0 → 恒定抬头/低头偏差。以头部实际屏幕位置为基准后
            // "看着眼睛"=零偏转，缩放/窗口位置无关）
            Vector3 headScreen = mainCamera.WorldToScreenPoint(_headBone.position);
            Vector2 refPoint = new Vector2(headScreen.x, headScreen.y);
            Vector2 offset = new Vector2(
                Mathf.Clamp((sp.x - refPoint.x) / screenMaxOffset, -1f, 1f),
                Mathf.Clamp((sp.y - refPoint.y) / screenMaxOffset, -1f, 1f));

            // 目标增量：绕世界 up(yaw)/right(pitch) 轴，与骨骼局部系无关
            // Euler 约定：+X = 向下（docs/14 §8.1），故抬头用 -pitch
            // yaw 取负：相机在 +Z 看 -Z 时，世界 yaw 正方向与屏幕水平镜像（光标在左应向左看）
            float yaw = -offset.x * hMaxAngle;
            float pitch = offset.y * vMaxAngle;
            Quaternion targetDelta = Quaternion.AngleAxis(yaw, Vector3.up)
                                * Quaternion.AngleAxis(-pitch, Vector3.right);

            // 先颈后头：父骨先应用，子骨后读取的世界旋转已含父级增量
            // 颈+头分担之和=1，总偏转恰为目标增量（v4 头叠加满份致 1.35× 过冲）
            Quaternion headAnimWorld = _headBone.rotation; // 应用前快照本帧动画姿态：眼球余量的测量基准
            if (_neckBone != null)
                applyBone(_neckBone, ref _smoothNeck, targetDelta, neckShare, headSmoothSpeed);
            applyBone(_headBone, ref _smoothHead, targetDelta, 1f - neckShare, headSmoothSpeed);

            // 眼球：余量 = inv(头链本帧实际叠加增量) × 目标增量——头未转满时眼球快速补足，
            // 头收敛后余量归零、眼球随动画回中（不反向顶眼白、不被待机摇晃带着自转）
            if (enableEye)
            {
                Quaternion headTurned = _headBone.rotation * Quaternion.Inverse(headAnimWorld);
                Quaternion eyeMargin = Quaternion.Inverse(headTurned) * targetDelta;
                eyeMargin.ToAngleAxis(out float eyeCorner, out Vector3 eyeAxis);
                if (eyeCorner > 180f) eyeCorner -= 360f;
                eyeCorner = Mathf.Clamp(eyeCorner, -eyeMaxAngle, eyeMaxAngle);
                if (eyeAxis.sqrMagnitude > 1e-6f)
                {
                    eyeAxis.Normalize();
                    Quaternion eyeDelta = Quaternion.AngleAxis(eyeCorner, eyeAxis);
                    if (_eyeLBone != null) applyEye(_eyeLBone, _eyeBaselineL, ref _smoothEyeL, eyeDelta);
                    if (_eyeRBone != null) applyEye(_eyeRBone, _eyeBaselineR, ref _smoothEyeR, eyeDelta);
                }
            }
        }

        /// <summary>在动画世界旋转上叠加目标增量（share=叠加比例），指数阻尼平滑</summary>
        void applyBone(Transform bone, ref Quaternion smooth, Quaternion targetDelta, float share, float smoothVel)
        {
            Quaternion delta = Quaternion.Slerp(Quaternion.identity, targetDelta, share);
            applyBoneDelta(bone, ref smooth, delta, smoothVel);
        }

        /// <summary>静默淡出写骨（2026-08-27 v2 无跳变版）：淡出终点=本帧动画姿态（含惯性化输出，
        /// 本层 LateUpdate 在其之后执行）——平滑值向它追赶（保留旧视线偏转的衰减惯性），但**输出
        /// 权重混合**：Slerp(动画姿态, 平滑, w)。w=1=纯平滑（抓取瞬间与视线输出连续）；w→0=纯动画
        /// 姿态——停写时输出已恒等于动画姿态，追赶滞后被权重缩到 1% 以下，结构上无跳变。
        /// 眼球同路径（终点=动画曲线姿态而非启动基线——GI 模型 +EyeBone 有 clip 曲线，淡到"中性"
        /// 会在停写时跳回曲线值）。</summary>
        void silenceFadeWriteBone(Transform bone, ref Quaternion smooth, float w, float catchUpRate)
        {
            Quaternion animPose = bone.rotation;
            float k = 1f - Mathf.Exp(-catchUpRate * Time.deltaTime);
            smooth = Quaternion.Slerp(smooth, animPose, k);
            bone.rotation = Quaternion.Slerp(animPose, smooth, w);
        }

        /// <summary>在动画世界旋转上叠加增量，指数阻尼平滑</summary>
        void applyBoneDelta(Transform bone, ref Quaternion smooth, Quaternion delta, float smoothVel)
        {
            Quaternion expected = delta * bone.rotation;
            float k = 1f - Mathf.Exp(-smoothVel * Time.deltaTime);
            smooth = Quaternion.Slerp(smooth, expected, k);
            bone.rotation = smooth;
        }

        /// <summary>眼球：本地基线重建中性姿态（父骨当前世界旋转 × 启动时本地快照）后叠加余量并平滑。
        /// 目骨在 clip 中无曲线、动画不覆写，不能在自身当前旋转上累积（会复利式自转）。</summary>
        void applyEye(Transform eye, Quaternion localBaseline, ref Quaternion smooth, Quaternion margin)
        {
            Quaternion expected = margin * (eye.parent.rotation * localBaseline);
            float k = 1f - Mathf.Exp(-eyeSmoothSpeed * Time.deltaTime);
            smooth = Quaternion.Slerp(smooth, expected, k);
            eye.rotation = smooth;
        }
    }
}
