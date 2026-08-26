using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 桌宠拖拽物理（docs/19 §6.1 抓斗篷钟摆·仓鼠式拎起）——纯模拟组件：
    /// 不碰 Win32 与 Transform，由 PetWindowController 每帧喂数据（光标/地面/边界）并应用输出
    /// （骨盆目标屏幕位 + 身体倾角）。坐标=Win32 屏幕物理像素，y 向下为正；倾角右倾为正
    /// （窗口控制器以 -倾角 绕 Z 轴映射模型旋转：相机在 -Z 侧看 +Z，屏幕 x=+X 世界）。
    ///
    /// 模型（2026-08-26 实现，先网检后设计：Shimeji Dragged/Thrown 二分 + PBD 鼠标绳标准做法）：
    /// - 拖拽=鼠标钉住抓点（斗篷）的不可伸长绳单摆：骨盆为质量点，重力+空气阻尼+挣扎力半隐式
    ///   积分；绳拉紧时投影回圆（PBD 距离约束）并重导速度为纯切向——快速画圈即绕光标甩转；
    ///   绳松弛（手低于骨盆）时自由落体直到重新拉紧。身体倾角=竖直基准+水平速度微倾的欠阻尼
    ///   二阶弹簧（Desktop Mate 式被拎起，2026-08-26 v5 用户目检否决旧"绳向对齐"后定案——
    ///   旧法在光标持于体侧时身体转到 ±90° 朝光标，读作"被吸过去"；绳摆位置动力学不变）。
    /// - 松手二分（Shimeji 同款）：速度≥阈值=真甩出→飞行（重力弹道+地面弹跳+贴地摩擦+虚拟屏
    ///   侧墙反弹防丢+超时兜底）；低于=原地收尾（保留"随意摆放"的既有 UX，不强制落到任务栏）。
    /// - 挣扎=拖拽期间周期性横向正弦爆发力+倾角摆动分量（爆发频率取摆固有频率的 ~2 倍，
    ///   远离共振防越摆越大）。
    /// - 重力按抓取时模型屏幕像素密度换算（px/米）：摆动节奏是派蒙自身米制（ω=√(g/L) 的 L
    ///   用派蒙自己的米），任何缩放下手感一致。
    /// </summary>
    public class PetDragPhysicsController : MonoBehaviour
    {
        public enum 交互阶段 { 无, 拖拽, 飞行, 收尾 }

        [Header("钟摆（拖拽中）")]
        [Tooltip("重力倍率：1=按派蒙真实体型换算的屏幕像素重力（0.94m 高→约 3600px/s²），>1 下坠更快更重")]
        [SerializeField] private float 重力倍率 = 1f;
        [Tooltip("拖拽中空气阻尼（速度每秒指数衰减率）：越大摆动停得越快，0=几乎永摆")]
        [SerializeField] private float 空气阻尼 = 0.8f;
        [Tooltip("绳长倍率：1=光标到骨盆的实际距离（抓哪是哪）；>1 更飘逸摆幅更大")]
        [SerializeField] private float 绳长倍率 = 1f;
        [Tooltip("最小绳长像素（防点在骨盆正上的退化零摆）")]
        [SerializeField] private float 最小绳长像素 = 30f;

        [Header("松手与飞行")]
        [Tooltip("松手速度阈值（px/s）：≥此值判为甩出（飞行+落地弹跳）；低于=原地放回（保留随意摆放）")]
        [SerializeField] private float 松手起飞速度阈值 = 700f;
        [Tooltip("飞行空气阻尼（速度每秒指数衰减率）")]
        [SerializeField] private float 飞行阻尼 = 1.2f;
        [Tooltip("弹跳恢复系数：0=落地即停，1=完全弹性")]
        [SerializeField] private float 弹跳恢复系数 = 0.38f;
        [Tooltip("落地反弹时水平速度保留比例")]
        [SerializeField] private float 落地水平摩擦 = 0.65f;
        [Tooltip("贴地滑动的水平速度每秒衰减率")]
        [SerializeField] private float 地面滑动摩擦 = 5f;
        [Tooltip("速度低于此值（px/s）视为停稳，结束飞行")]
        [SerializeField] private float 停稳速度阈值 = 60f;
        [Tooltip("飞行强制结束超时（秒，兜底异常状态不停稳）")]
        [SerializeField] private float 飞行超时秒 = 8f;
        [Tooltip("松手瞬间速度上限（px/s，防极速甩动把骨盆速度泵到离谱）")]
        [SerializeField] private float 最大飞行初速 = 6000f;

        [Header("挣扎")]
        [Tooltip("拖拽期间派蒙会周期性小幅挣扎（横向正弦爆发力+身体摆动）")]
        [SerializeField] private bool 启用挣扎 = true;
        [Tooltip("挣扎力度：爆发期峰值横向加速度（px/s²）。参考：绳 150px 时 800≈10px 摆幅")]
        [SerializeField] private float 挣扎力度 = 800f;
        [Tooltip("挣扎带动的身体摆角（度，峰值）")]
        [SerializeField] private float 挣扎摆角 = 6f;
        [Tooltip("挣扎爆发的间隔秒数范围（每次爆发 0.3~0.55s）")]
        [SerializeField] private Vector2 挣扎间隔秒 = new Vector2(0.5f, 1.2f);

        [Header("倾角弹簧（身体对齐绳方向/归正的动态）")]
        [Tooltip("倾角弹簧频率（Hz）：越大身体跟随绳向/归正越快")]
        [SerializeField] private float 倾角弹簧频率 = 3.2f;
        [Tooltip("倾角弹簧阻尼比：<1 欠阻尼（有回弹 jiggle），1=临界阻尼，>1 过阻尼")]
        [SerializeField] private float 倾角弹簧阻尼比 = 0.55f;
        [Tooltip("绳松弛/飞行时按水平速度倾身：度 per px/s")]
        [SerializeField] private float 飞行倾身系数 = 0.012f;
        [Tooltip("飞行倾身上限（度）")]
        [SerializeField] private float 飞行倾身上限 = 30f;
        [Tooltip("拖拽中身体最大倾角（度）：竖直基准+按拖动方向微倾（Desktop Mate 式被拎起）；旧绳向对齐在光标持于体侧时把身体转到 ±90° 朝光标（读作'被吸过去'），2026-08-26 用户目检否决）")]
        [SerializeField] private float 拖拽倾身上限 = 15f;

        // ---- 运行时状态 ----
        public 交互阶段 阶段 { get; private set; } = 交互阶段.无;
        public bool 交互中 => 阶段 != 交互阶段.无;
        public bool 飞行中 => 阶段 == 交互阶段.飞行;

        private Vector2 pivot;   // 抓点=光标（物理像素）
        private Vector2 bob;     // 骨盆目标屏幕位（质量点）
        private Vector2 vel;     // 速度 px/s
        private float 绳长;
        private float 重力px;    // px/s²（y 向下为正）
        private float 模型半宽;  // 侧墙反弹收边（物理像素）
        private float 脚底偏移;  // 骨盆→脚底像素距离（y 向下为正，地面接触判定）
        private float 飞行截止时刻 = -10f;
        private float 落地冲击;  // 本次飞行最大落地冲击速度（px/s，物理交互收口时读）

        private float 倾角;      // 度，右倾为正（可解缠绕超出 ±180）
        private float 角速度;    // 度/s

        // 挣扎爆发
        private bool 爆发中;
        private float 爆发计时;
        private float 爆发相位;
        private float 爆发频率;
        private int 爆发方向;

        /// <summary>骨盆目标屏幕位（物理像素）——窗口控制器据此定位窗口</summary>
        public Vector2 当前骨盆屏幕 => bob;
        /// <summary>身体倾角（度，右倾为正）——窗口控制器以 -倾角 绕 Z 轴应用</summary>
        public float 当前倾角 => 倾角;
        /// <summary>本次飞行最大落地冲击速度（px/s；下一次开始拖拽时清零）</summary>
        public float 落地冲击速度 => 落地冲击;

        /// <summary>拖拽起手。每米像素=抓取时模型屏幕像素密度（重力换算）；模型半宽/骨盆到脚底
        /// 用于飞行阶段侧墙收边与地面接触判定。倾角/角速度保留当前值（收尾中被再抓=连续体）。</summary>
        public void 开始拖拽(Vector2 光标屏幕, Vector2 骨盆屏幕, float 每米像素, float 模型半宽像素, float 骨盆到脚底像素)
        {
            阶段 = 交互阶段.拖拽;
            pivot = 光标屏幕;
            bob = 骨盆屏幕;
            vel = Vector2.zero;
            模型半宽 = Mathf.Max(10f, 模型半宽像素);
            脚底偏移 = Mathf.Max(10f, 骨盆到脚底像素);
            绳长 = Mathf.Max(Vector2.Distance(pivot, bob) * 绳长倍率, 最小绳长像素);
            重力px = 9.81f * Mathf.Max(1f, 每米像素) * 重力倍率;
            飞行截止时刻 = -10f;
            落地冲击 = 0f;
            爆发中 = false;
            爆发计时 = Random.Range(0.4f, 0.9f); // 首次挣扎稍等一下
        }

        /// <summary>拖拽一步：pivot=光标。绳约束（拉紧投影+速度重导）+ 挣扎 + 倾角弹簧。</summary>
        public void 每帧拖拽(Vector2 光标屏幕, float dt)
        {
            if (阶段 != 交互阶段.拖拽 || dt <= 0f) return;
            dt = Mathf.Min(dt, 0.05f);
            pivot = 光标屏幕;

            // 挣扎爆发力（横向正弦）+ 倾角摆动分量
            步进挣扎(dt, out Vector2 挣扎加速度, out float 挣扎角分量);

            // 半隐式欧拉积分 + 空气阻尼（能量上限防 PBD 约束能量泵）
            vel += (挣扎加速度 + new Vector2(0f, 重力px)) * dt;
            vel *= Mathf.Exp(-空气阻尼 * dt);
            const float 速度上限 = 9000f;
            if (vel.sqrMagnitude > 速度上限 * 速度上限) vel = vel.normalized * 速度上限;

            Vector2 投影前 = bob;
            bob += vel * dt;

            // 不可伸长绳：拉紧才投影（绳不能推）——投影后重导速度=纯切向（摆动）
            Vector2 d = bob - pivot;
            float dist = d.magnitude;
            if (dist > 绳长 && dist > 0.0001f)
            {
                bob = pivot + d / dist * 绳长;
                vel = (bob - 投影前) / dt;
            }

            // 倾角目标（2026-08-26 v5 用户目检否决绳向对齐后的 Desktop Mate 式竖直基准）：
            // 旧"拉紧=绳方向"在光标持于体侧/胸高时把身体转到 ±90° 朝光标，深垂落姿势被整体
            // 抵消、读作"被吸过去"而非被拎起；改为竖直基准+按水平速度微倾（拖动方向空气阻力
            // 感）。绳摆动力学（骨盆/窗口摆动）不受影响，仅身体不再随绳翻转。
            float 目标倾角 = Mathf.Clamp(vel.x * 飞行倾身系数, -拖拽倾身上限, 拖拽倾身上限);
            目标倾角 += 挣扎角分量;
            步进倾角弹簧(dt, 目标倾角);
        }

        /// <summary>松手：速度≥阈值=甩出（飞行）；否则原地收尾（保留随意摆放 UX）。</summary>
        public void 松手()
        {
            if (阶段 != 交互阶段.拖拽) return;
            if (vel.magnitude >= 松手起飞速度阈值)
            {
                阶段 = 交互阶段.飞行;
                if (vel.magnitude > 最大飞行初速) vel = vel.normalized * 最大飞行初速;
                飞行截止时刻 = Time.unscaledTime + 飞行超时秒;
            }
            else
            {
                阶段 = 交互阶段.收尾;
            }
        }

        /// <summary>飞行一步。返回 false=已停稳/超时转收尾。地面Y/左右界=Win32 物理像素
        /// （地面=当前显示器工作区底，侧墙=虚拟屏左右界）。</summary>
        public bool 每帧飞行(float dt, float 地面Y, float 左界, float 右界)
        {
            if (阶段 != 交互阶段.飞行 || dt <= 0f) return false;
            dt = Mathf.Min(dt, 0.05f);

            vel.y += 重力px * dt;
            vel *= Mathf.Exp(-飞行阻尼 * dt);
            bob += vel * dt;

            // 地面（脚底触地）：反弹衰减或贴地滑停
            bool 贴地 = false;
            if (bob.y + 脚底偏移 >= 地面Y)
            {
                bob.y = 地面Y - 脚底偏移;
                if (Mathf.Abs(vel.y) > 停稳速度阈值)
                {
                    落地冲击 = Mathf.Max(落地冲击, Mathf.Abs(vel.y));
                    vel.y = -vel.y * 弹跳恢复系数;
                    vel.x *= 落地水平摩擦;
                }
                else
                {
                    vel.y = 0f;
                    贴地 = true;
                }
            }
            if (贴地)
            {
                vel.x *= Mathf.Exp(-地面滑动摩擦 * dt);
                if (vel.magnitude < 停稳速度阈值 && Mathf.Abs(归一化角(倾角)) < 15f)
                {
                    阶段 = 交互阶段.收尾;
                    return false;
                }
            }

            // 虚拟屏左右墙反弹（按模型半宽收边：甩出桌面防丢；无天花板=上抛必回落）
            if (左界 < 右界)
            {
                float l = 左界 + 模型半宽, r = 右界 - 模型半宽;
                if (bob.x < l) { bob.x = l; vel.x = Mathf.Abs(vel.x) * 弹跳恢复系数; }
                else if (bob.x > r) { bob.x = r; vel.x = -Mathf.Abs(vel.x) * 弹跳恢复系数; }
            }

            // 倾角目标：空中=按水平速度倾身（风阻感），贴地=归正
            float 目标 = 贴地 ? 0f : Mathf.Clamp(vel.x * 飞行倾身系数, -飞行倾身上限, 飞行倾身上限);
            步进倾角弹簧(dt, 目标);

            if (Time.unscaledTime > 飞行截止时刻) // 超时兜底
            {
                阶段 = 交互阶段.收尾;
                return false;
            }
            return true;
        }

        /// <summary>收尾一步（倾角弹簧归零，窗口不再由物理驱动）。返回 false=已完全归位，交互结束。</summary>
        public bool 每帧收尾(float dt)
        {
            if (阶段 != 交互阶段.收尾 || dt <= 0f) return false;
            步进倾角弹簧(Mathf.Min(dt, 0.05f), 0f);
            if (Mathf.Abs(归一化角(倾角)) < 0.5f && Mathf.Abs(角速度) < 3f)
            {
                倾角 = 0f;
                角速度 = 0f;
                阶段 = 交互阶段.无;
                return false;
            }
            return true;
        }

        /// <summary>挣扎爆发状态机：爆发（0.3~0.55s 正弦力）与间隔交替。
        /// 爆发频率取摆固有频率的 ~2 倍（1.5~2.5Hz，绳 80~300px 的固有频率 0.5~1.1Hz）——
        /// 靠近共振会越摆越大失控，过高频则被摆惯性滤掉看不见。</summary>
        private void 步进挣扎(float dt, out Vector2 加速度, out float 角分量)
        {
            加速度 = Vector2.zero;
            角分量 = 0f;
            if (!启用挣扎) return;
            爆发计时 -= dt;
            if (爆发计时 <= 0f)
            {
                if (爆发中)
                {
                    爆发中 = false;
                    爆发计时 = Random.Range(挣扎间隔秒.x, 挣扎间隔秒.y);
                }
                else
                {
                    爆发中 = true;
                    爆发计时 = Random.Range(0.3f, 0.55f);
                    爆发相位 = 0f;
                    爆发频率 = Random.Range(1.5f, 2.5f);
                    爆发方向 = Random.value < 0.5f ? -1 : 1;
                }
            }
            if (爆发中)
            {
                爆发相位 += dt;
                float 振荡 = Mathf.Sin(爆发相位 * 爆发频率 * Mathf.PI * 2f);
                加速度 = new Vector2(爆发方向 * 挣扎力度 * 振荡, 0f);
                角分量 = 爆发方向 * 挣扎摆角 * 振荡;
            }
        }

        /// <summary>欠阻尼二阶角弹簧：目标取与当前倾角的最短路径（±180° 倒挂附近不绕远路）。</summary>
        private void 步进倾角弹簧(float dt, float 目标)
        {
            float delta = 归一化角(目标 - 倾角);
            float w = 倾角弹簧频率 * Mathf.PI * 2f;
            角速度 += (w * w * delta - 2f * 倾角弹簧阻尼比 * w * 角速度) * dt;
            倾角 += 角速度 * dt;
        }

        /// <summary>角度归一到 (-180,180]</summary>
        private static float 归一化角(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            else if (a < -180f) a += 360f;
            return a;
        }
    }
}
