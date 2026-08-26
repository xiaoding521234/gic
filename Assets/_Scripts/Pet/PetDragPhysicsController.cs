using UnityEngine;

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
    /// </summary>
    public class PetDragPhysicsController : MonoBehaviour
    {
        public enum 交互阶段 { 无, 拖拽, 收尾 }

        [Header("四肢摆动（跟拍动态；身体刚体直跟无摆动）")]
        [Tooltip("四肢横摆增益（度 per px/s）：光标水平速度→四肢反向滞后摆角（恒速拖动时四肢像迎风倾斜）。参考：1500px/s×0.013≈20°")]
        [SerializeField] private float 摆动增益 = 0.013f;
        [Tooltip("竖直外展增益（度 per px/s）：下移→四肢向身体两侧张开、上提→收拢（下落伪力直觉）")]
        [SerializeField] private float 外展增益 = 0.010f;
        [Tooltip("竖直外展角上限（度，张开方向；收拢方向再乘内收占比更小）——独立于水平摆动上限：2026-08-26 目检\"下拖手脚摆开太大不美观\"后从 30° 收紧")]
        [SerializeField] private float 外展上限 = 10f;
        [Tooltip("四肢摆动角上限（度）")]
        [SerializeField] private float 摆动上限 = 30f;
        [Tooltip("四肢摆动弹簧频率（Hz，手臂基准；腿略高）——越大摆动越快")]
        [SerializeField] private float 摆动频率 = 2.8f;
        [Tooltip("摆动弹簧阻尼比：<1 欠阻尼（有回摆摆荡，四肢应有的活感）；1=临界无回摆；过大=死板")]
        [SerializeField] private float 摆动阻尼比 = 0.45f;

        [Header("收尾")]
        [Tooltip("松手后收尾时长（秒）：窗口冻结原地，四肢摆动在此期间弹簧归零（姿势基准由窗口控制器同期平滑归零）")]
        [SerializeField] private float 收尾秒数 = 0.5f;

        // ---- 运行时状态 ----
        public 交互阶段 阶段 { get; private set; } = 交互阶段.无;
        public bool 交互中 => 阶段 != 交互阶段.无;

        private Vector2 基准光标;   // 抓取时光标（物理像素）
        private Vector2 基准骨盆;   // 抓取时骨盆屏幕位
        private Vector2 上帧光标;   // 光标速度计算用
        private Vector2 平滑速度;   // 低通后的光标速度 px/s（τ=50ms 防轮询毛刺）
        private Vector2 骨盆目标;   // 刚体直跟目标（收尾冻结）
        private float 收尾截止时刻 = -10f;

        // 四肢弹簧：索引 0=左臂 1=右臂 2=左腿 3=右腿（与 PetWindowController.四肢骨名 顺序一致）
        private readonly float[] 摆角 = new float[4];
        private readonly float[] 摆速 = new float[4];
        // 手臂增益大频率低（长摆）、腿增益小频率高（短摆）——参数微差防四肢同步的机械感
        private static readonly float[] 肢增益倍率 = { 1f, 1f, 0.75f, 0.75f };
        private static readonly float[] 肢频率倍率 = { 0.92f, 1.0f, 1.12f, 1.24f };
        // 外展方向：左肢 -1 / 右肢 +1（下移时四肢向身体两侧张开=左右肢反向摆开）
        private static readonly float[] 外展方向 = { -1f, 1f, -1f, 1f };
        // 外展项内收方向限幅占比（上提收拢时手臂横摆过躯干会穿模，内收限 35%）
        private const float 内收占比 = 0.35f;

        /// <summary>骨盆目标屏幕位（物理像素）——窗口控制器据此定位窗口</summary>
        public Vector2 当前骨盆屏幕 => 骨盆目标;

        /// <summary>拖拽起手：快照光标与骨盆屏幕位（刚体 1:1 直跟基准）。摆角/摆速保留当前值
        /// （收尾中被再抓=四肢连续体）。</summary>
        public void 开始拖拽(Vector2 光标屏幕, Vector2 骨盆屏幕)
        {
            阶段 = 交互阶段.拖拽;
            基准光标 = 光标屏幕;
            基准骨盆 = 骨盆屏幕;
            上帧光标 = 光标屏幕;
            平滑速度 = Vector2.zero;
            骨盆目标 = 骨盆屏幕;
            收尾截止时刻 = -10f;
        }

        /// <summary>拖拽一步：骨盆目标=基准骨盆+光标位移（刚体直跟，零摆动零滞后）；
        /// 光标速度（低通）驱动四肢摆动弹簧目标。</summary>
        public void 每帧拖拽(Vector2 光标屏幕, float dt)
        {
            if (阶段 != 交互阶段.拖拽 || dt <= 0f) return;
            dt = Mathf.Min(dt, 0.05f);

            // 光标速度低通（τ=50ms）：GetCursorPos 逐帧原始差分有毛刺，直接怼弹簧会高频抖
            Vector2 原始速度 = (光标屏幕 - 上帧光标) / dt;
            float k = 1f - Mathf.Exp(-dt / 0.05f);
            平滑速度 = Vector2.Lerp(平滑速度, 原始速度, k);
            上帧光标 = 光标屏幕;

            骨盆目标 = 基准骨盆 + (光标屏幕 - 基准光标); // 刚体 1:1

            // 四肢弹簧目标：水平滞后（迎风倾斜）+ 竖直外展（下落张开/上提收拢）。
            // 滞后符号=目检标定（2026-08-26 两轮）：应用层为真世界 Z 轴旋转，负角实测四肢向屏幕
            // 右甩（旧 -vx 版"往右拖四肢全往右摆"被否决）——取 +vx 使往右拖时四肢向左甩（惯性滞后）。
            float 外展 = Mathf.Clamp(平滑速度.y * 外展增益, -外展上限 * 内收占比, 外展上限);
            for (int i = 0; i < 4; i++)
            {
                float 滞后 = 平滑速度.x * 摆动增益;
                float 目标 = Mathf.Clamp(肢增益倍率[i] * (滞后 + 外展方向[i] * 外展), -摆动上限, 摆动上限);
                步进弹簧(i, dt, 目标);
            }
        }

        /// <summary>松手即停（2026-08-26 用户拍板）：骨盆目标冻结原地，转收尾——四肢摆动弹簧
        /// 归零，绝无甩出/飞行/弹跳。</summary>
        public void 松手()
        {
            if (阶段 != 交互阶段.拖拽) return;
            阶段 = 交互阶段.收尾;
            收尾截止时刻 = Time.unscaledTime + 收尾秒数;
        }

        /// <summary>收尾一步（四肢摆动弹簧归零，窗口冻结不再由物理驱动）。
        /// 返回 false=收尾完成，交互结束。</summary>
        public bool 每帧收尾(float dt)
        {
            if (阶段 != 交互阶段.收尾 || dt <= 0f) return false;
            dt = Mathf.Min(dt, 0.05f);
            for (int i = 0; i < 4; i++) 步进弹簧(i, dt, 0f);
            if (Time.unscaledTime >= 收尾截止时刻)
            {
                for (int i = 0; i < 4; i++) { 摆角[i] = 0f; 摆速[i] = 0f; }
                阶段 = 交互阶段.无;
                return false;
            }
            return true;
        }

        /// <summary>取四肢摆动角（度，绕世界 Z 轴：正=四肢末端向屏幕右摆）。索引 0=左臂 1=右臂 2=左腿 3=右腿。</summary>
        public void 取四肢摆动(int 索引, out float 摆动角)
        {
            摆动角 = (uint)索引 < 4 ? 摆角[索引] : 0f;
        }

        /// <summary>欠阻尼二阶角弹簧（跟拍核心）：目标角由光标速度连续驱动，欠阻尼比（默认 0.45）
        /// 给出"滞后→回摆→收敛"的四肢活感。</summary>
        private void 步进弹簧(int i, float dt, float 目标)
        {
            float w = 摆动频率 * 肢频率倍率[i] * Mathf.PI * 2f;
            摆速[i] += (w * w * (目标 - 摆角[i]) - 2f * 摆动阻尼比 * w * 摆速[i]) * dt;
            摆角[i] += 摆速[i] * dt;
        }
    }
}
