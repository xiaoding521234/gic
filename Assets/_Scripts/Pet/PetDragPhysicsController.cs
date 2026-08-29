using UnityEngine;
using UnityEngine.Serialization;

namespace GIC.Pet
{
    /// <summary>
    /// 桌宠拖拽物理（docs/19 §6.1）——纯模拟组件：不碰 Win32 与 Transform，
    /// 由 PetWindowController 每帧喂数据（光标）并应用输出（骨盆目标屏幕位 + 四肢摆动角）。
    /// 坐标=Win32 屏幕物理像素，y 向下为正。
    ///
    /// 模型（2026-08-26 深夜定案：刚体跟随+四肢摆动，用户三轮目检反馈收敛）：
    /// - 身体=刚体 1:1 直跟光标（eSheep 直移语义）：骨盆目标=抓取时骨盆位+光标位移——
    ///   无钟摆、无重力、无倾角弹簧（用户拍板"拖拽中不需要任何摆动，摆动的应当只有四肢"）。
    ///   刚体平移下按住的点天然钉在光标下（全模型点保持与光标的抓取时偏移），旧钟摆的
    ///   斗篷锚点随之失去意义已移除；旧挣扎/飞行/落地反应/倾角弹簧全链路删除。
    /// - 四肢摆动=每肢一条欠阻尼二阶角弹簧（跟拍/secondary motion 标准做法）：
    ///   目标角∝光标屏幕速度（恒速拖动=四肢滞后迎风倾斜；急停=回摆收敛；竖直下移=
    ///   四肢向身体两侧张开、上提=收拢——下落伪力的物理直觉）。手臂/腿频率增益微差去同步。
    ///   PetWindowController 在 LateUpdate 把摆动角以世界 Z 轴旋转叠加到肩/大腿骨上
    ///   （Animation 每帧重写骨骼姿势，本层每帧再叠加，不累积）。
    /// - 松手即停（2026-08-26 用户拍板）：骨盆速度清零、窗口停原地，收尾期间姿势基准与
    ///   四肢摆动平滑归零（收尾秒数），绝无甩出/飞行/弹跳。
    /// - 挣扎（2026-08-27 用户要求"轻微挣扎全身摆动"）：拖拽期全身小幅钟摆+扭动（绕骨盆枢轴，
    ///   窗口控制器叠加进根旋转）+四肢反相扑腾（叠加进取四肢摆动输出）。两频叠加防机械摆锤感；
    ///   包络渐入渐出（抓起渐入、收尾随剩余时间衰减），相位全程连续（再抓无跳变）。
    /// </summary>
    public class PetDragPhysicsController : MonoBehaviour
    {
        public enum DragPhase { none, 拖拽, 收尾 }

        [Header("四肢摆动（跟拍动态；身体刚体直跟无摆动）")]
        [Tooltip("四肢横摆增益（度 per px/s）：光标水平速度→四肢反向滞后摆角（恒速拖动时四肢像迎风倾斜）。参考：1500px/s×0.013≈20°")]
        [InspectorName("摆动增益")]
        [SerializeField] private float swingGain = 0.013f;
        [Tooltip("竖直外展增益（度 per px/s）：下移→四肢向身体两侧张开、上提→收拢（下落伪力直觉）")]
        [InspectorName("外展增益")]
        [SerializeField] private float abductGain = 0.010f;
        [Tooltip("竖直外展角上限（度，张开方向；收拢方向再乘内收占比更小）——独立于水平摆动上限：2026-08-26 目检\"下拖手脚摆开太大不美观\"后从 30° 收紧")]
        [InspectorName("外展上限")]
        [SerializeField] private float abductMax = 10f;
        [Tooltip("四肢摆动角上限（度）")]
        [InspectorName("摆动上限")]
        [SerializeField] private float swingMax = 30f;
        [Tooltip("四肢摆动弹簧频率（Hz，手臂基准；腿略高）——越大摆动越快")]
        [InspectorName("摆动频率")]
        [SerializeField] private float swingFreq = 2.8f;
        [Tooltip("摆动弹簧阻尼比：<1 欠阻尼（有回摆摆荡，四肢应有的活感）；1=临界无回摆；过大=死板")]
        [InspectorName("摆动阻尼比")]
        [SerializeField] private float swingDamping = 0.45f;

        [Header("收尾")]
        [Tooltip("松手后收尾时长（秒）：窗口冻结原地，四肢摆动在此期间弹簧归零（姿势基准由窗口控制器同期平滑归零）")]
        [InspectorName("收尾秒数")]
        [SerializeField] private float settleSec = 0.5f;

        [Header("挣扎（2026-08-27：拖拽期轻微全身摆动）")]
        [Tooltip("全身挣扎侧摆幅（度，绕屏幕平面法线）：身体绕骨盆小幅钟摆晃（头/四肢随之反侧摆动），读作\"被拎着挣动\"。0=关闭")]
        [InspectorName("挣扎摆幅")]
        [SerializeField] private float struggleSwing = 2.5f;
        [Tooltip("全身挣扎扭幅（度，绕竖直轴小幅扭动——肩部左右拧）")]
        [InspectorName("挣扎扭幅")]
        [SerializeField] private float struggleTwist = 4f;
        [Tooltip("挣扎主频率（Hz）：1-2Hz 读作轻挣，过高读作高频发抖")]
        [InspectorName("挣扎频率")]
        [SerializeField] private float struggleFreq = 1.3f;
        [Tooltip("次级频率占比（×主频）：两频叠加出非周期感（纯正弦=机械摆锤感）；0=纯正弦")]
        [InspectorName("挣扎次级频率占比")]
        [SerializeField] private float struggleSecondaryRatio = 0.53f;
        [Tooltip("挣扎渐入秒：抓起后包络从 0 升到 1 的时长（瞬间满幅读作受惊抽搐，渐入读作开始挣动）")]
        [InspectorName("挣扎渐入秒")]
        [SerializeField] private float struggleFadeInSec = 0.4f;
        [Tooltip("四肢挣扎附加摆幅（度，叠加在跟拍弹簧之上）：左右肢反相=对称扑腾感。0=四肢只留跟拍摆动")]
        [InspectorName("四肢挣扎摆幅")]
        [SerializeField] private float limbStruggleSwing = 6f;

        // ---- 运行时状态 ----
        public DragPhase phase { get; private set; } = DragPhase.none;
        public bool IsActive => phase != DragPhase.none;

        private Vector2 baseCursor;   // 抓取时光标（物理像素）
        private Vector2 basePelvis;   // 抓取时骨盆屏幕位
        private Vector2 prevCursor;   // 光标速度计算用
        private Vector2 smoothVel;   // 低通后的光标速度 px/s（τ=50ms 防轮询毛刺）
        private Vector2 pelvisTarget;   // 刚体直跟目标（收尾冻结）
        private float settleDeadline = -10f;

        // 四肢弹簧：索引 0=左臂 1=右臂 2=左腿 3=右腿（与 PetWindowController.四肢骨名 顺序一致）
        private readonly float[] swingAngle = new float[4];
        private readonly float[] swingVel = new float[4];
        // 手臂增益大频率低（长摆）、腿增益小频率高（短摆）——参数微差防四肢同步的机械感
        private static readonly float[] limbGainScale = { 1f, 1f, 0.75f, 0.75f };
        private static readonly float[] limbFreqScale = { 0.92f, 1.0f, 1.12f, 1.24f };
        // 外展方向：左肢 -1 / 右肢 +1（下移时四肢向身体两侧张开=左右肢反向摆开）
        private static readonly float[] abductDir = { -1f, 1f, -1f, 1f };
        // 外展项内收方向限幅占比（上提收拢时手臂横摆过躯干会穿模，内收限 35%）
        private const float AdductionRatio = 0.35f;

        // 挣扎（全身轻微摆动，2026-08-27）：相位持续推进（收尾被再抓=相位连续无跳变），
        // 包络控制幅度进出（拖拽期渐入 / 收尾期随剩余时间线性衰减）
        private float strugglePhaseMain, strugglePhaseSecondary, strugglePhaseTwist;
        private readonly float[] struggleLimbPhase = new float[4];
        private readonly float[] struggleLimbSwing = new float[4];
        private float struggleEnvelope;

        /// <summary>全身挣扎侧摆角（度，绕世界 Z 轴：正=头向屏幕右侧摆）——两频叠加防机械感</summary>
        public float CurrentStruggleSwing =>
            (Mathf.Sin(strugglePhaseMain) + 0.6f * Mathf.Sin(strugglePhaseSecondary)) / 1.6f * struggleSwing * struggleEnvelope;

        /// <summary>全身挣扎扭角（度，绕竖直轴：正=肩部向左拧）——第三频率（×1.37）与主次级错开</summary>
        public float CurrentStruggleTwist =>
            (Mathf.Sin(strugglePhaseTwist) + 0.5f * Mathf.Sin(strugglePhaseSecondary + 1.1f)) / 1.5f * struggleTwist * struggleEnvelope;

        /// <summary>挣扎一步：相位推进 + 包络（拖拽期渐入）+ 四肢挣扎角求值（左右肢反相=扑腾）。
        /// 收尾期由调用方先按剩余时间压包络再调本方法（拖拽期=false 不升包络）。</summary>
        private void StepStruggle(float dt, bool isDragging)
        {
            if (isDragging)
                struggleEnvelope = Mathf.Min(1f, struggleEnvelope + dt / Mathf.Max(0.01f, struggleFadeInSec));
            strugglePhaseMain += struggleFreq * Mathf.PI * 2f * dt;
            strugglePhaseSecondary += struggleFreq * struggleSecondaryRatio * Mathf.PI * 2f * dt;
            strugglePhaseTwist += struggleFreq * 1.37f * Mathf.PI * 2f * dt;
            for (int i = 0; i < 4; i++)
            {
                struggleLimbPhase[i] += struggleFreq * limbFreqScale[i] * Mathf.PI * 2f * dt;
                struggleLimbSwing[i] = Mathf.Sin(struggleLimbPhase[i]) * abductDir[i] * limbStruggleSwing * limbGainScale[i] * struggleEnvelope;
            }
        }

        /// <summary>骨盆目标屏幕位（物理像素）——窗口控制器据此定位窗口</summary>
        public Vector2 CurrentPelvisScreen => pelvisTarget;

        // 拖拽时长（2026-08-27 放下反应分档用）：拖拽期实时更新，松手时冻结
        private float dragStartAt = -999f;

        /// <summary>本次拖拽时长（秒）：拖拽期=实时值，松手时冻结（再抓重置）。
        /// -1=无最近拖拽。行为层在物理收尾结束的边沿读它做放下反应分档（短拖无反应/中档害羞/长拖生气）。</summary>
        public float LastDragDuration { get; private set; } = -1f;

        /// <summary>拖拽起手：快照光标与骨盆屏幕位（刚体 1:1 直跟基准）。摆角/摆速保留当前值
        /// （收尾中被再抓=四肢连续体）。</summary>
        public void BeginDrag(Vector2 cursorScreen, Vector2 pelvisScreen)
        {
            phase = DragPhase.拖拽;
            baseCursor = cursorScreen;
            basePelvis = pelvisScreen;
            prevCursor = cursorScreen;
            smoothVel = Vector2.zero;
            pelvisTarget = pelvisScreen;
            settleDeadline = -10f;
            dragStartAt = Time.unscaledTime;
        }

        /// <summary>拖拽一步：骨盆目标=基准骨盆+光标位移（刚体直跟，零摆动零滞后）；
        /// 光标速度（低通）驱动四肢摆动弹簧目标。</summary>
        public void DragFrame(Vector2 cursorScreen, float dt)
        {
            if (phase != DragPhase.拖拽 || dt <= 0f) return;
            dt = Mathf.Min(dt, 0.05f);

            // 光标速度低通（τ=50ms）：GetCursorPos 逐帧原始差分有毛刺，直接怼弹簧会高频抖
            Vector2 rawSpeed = (cursorScreen - prevCursor) / dt;
            float k = 1f - Mathf.Exp(-dt / 0.05f);
            smoothVel = Vector2.Lerp(smoothVel, rawSpeed, k);
            prevCursor = cursorScreen;

            pelvisTarget = basePelvis + (cursorScreen - baseCursor); // 刚体 1:1
            LastDragDuration = Time.unscaledTime - dragStartAt; // 拖拽期实时

            // 四肢弹簧目标：水平滞后（迎风倾斜）+ 竖直外展（下落张开/上提收拢）。
            // 滞后符号=目检标定（2026-08-26 两轮）：应用层为真世界 Z 轴旋转，负角实测四肢向屏幕
            // 右甩（旧 -vx 版"往右拖四肢全往右摆"被否决）——取 +vx 使往右拖时四肢向左甩（惯性滞后）。
            float 外展 = Mathf.Clamp(smoothVel.y * abductGain, -abductMax * AdductionRatio, abductMax);
            for (int i = 0; i < 4; i++)
            {
                float 滞后 = smoothVel.x * swingGain;
                float target = Mathf.Clamp(limbGainScale[i] * (滞后 + abductDir[i] * 外展), -swingMax, swingMax);
                StepSpring(i, dt, target);
            }
            StepStruggle(dt, true); // 全身轻微挣扎（包络渐入）
        }

        /// <summary>松手即停（2026-08-26 用户拍板）：骨盆目标冻结原地，转收尾——四肢摆动弹簧
        /// 归零，绝无甩出/飞行/弹跳。</summary>
        public void Release()
        {
            if (phase != DragPhase.拖拽) return;
            phase = DragPhase.收尾;
            settleDeadline = Time.unscaledTime + settleSec;
            LastDragDuration = Time.unscaledTime - dragStartAt; // 冻结——行为层放下反应分档用
        }

        /// <summary>收尾一步（四肢摆动弹簧归零，窗口冻结不再由物理驱动）。
        /// 返回 false=收尾完成，交互结束。</summary>
        public bool SettleFrame(float dt)
        {
            if (phase != DragPhase.收尾 || dt <= 0f) return false;
            dt = Mathf.Min(dt, 0.05f);
            for (int i = 0; i < 4; i++) StepSpring(i, dt, 0f);
            // 挣扎包络随剩余时间线性衰减（相位继续推进=晃着停下，不是急刹）
            struggleEnvelope = Mathf.Max(0f, settleDeadline - Time.unscaledTime) / Mathf.Max(0.01f, settleSec);
            StepStruggle(dt, false);
            if (Time.unscaledTime >= settleDeadline)
            {
                for (int i = 0; i < 4; i++) { swingAngle[i] = 0f; swingVel[i] = 0f; }
                struggleEnvelope = 0f;
                for (int i = 0; i < 4; i++) struggleLimbSwing[i] = 0f;
                phase = DragPhase.none;
                return false;
            }
            return true;
        }

        /// <summary>取四肢摆动角（度，绕世界 Z 轴：正=四肢末端向屏幕右摆）。索引 0=左臂 1=右臂 2=左腿 3=右腿。
        /// 返回=跟拍弹簧角+挣扎附加角。</summary>
        public void GetLimbSwing(int index, out float swingOut)
        {
            swingOut = (uint)index < 4 ? swingAngle[index] + struggleLimbSwing[index] : 0f;
        }

        /// <summary>欠阻尼二阶角弹簧（跟拍核心）：目标角由光标速度连续驱动，欠阻尼比（默认 0.45）
        /// 给出"滞后→回摆→收敛"的四肢活感。</summary>
        private void StepSpring(int i, float dt, float target)
        {
            float w = swingFreq * limbFreqScale[i] * Mathf.PI * 2f;
            swingVel[i] += (w * w * (target - swingAngle[i]) - 2f * swingDamping * w * swingVel[i]) * dt;
            swingAngle[i] += swingVel[i] * dt;
        }
    }
}
