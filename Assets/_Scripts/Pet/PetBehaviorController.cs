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
        [Tooltip("宿主（桌面场景=PetWindowController 接线；游戏内形态字段为 null 走 PetInGameHost.宿主接口）。2026-08-28 类型放宽为 PetHostBase 共用基类——场景引用按字段名保留不变")]
        [SerializeField] private PetHostBase 窗口控制器;
        [SerializeField] private PetBlinkController 眨眼控制器; // 单次动作期间静默（morph 重评估与动作叠加互相放大顿挫）
        [SerializeField] private PetLookAtController 视线控制器; // 出场/退场期间静默（仪式动作头链全交 clip，2026-08-24）
        [SerializeField] private PetEmotionController 情绪控制器;   // 拎起期的慌张表情（直接下发，不经动作映射）
        [Tooltip("边缘坐控制器（2026-08-27 坐窗口顶边/任务栏横框）：空=禁用边缘坐，松手行为回退纯待机")] [SerializeField] private PetEdgeSitController 边坐控制器;
        [Tooltip("被拎起时播的专用动作（Drag01 垂落姿势：四肢常量垂落+躯干保留待机微动，loop 播放；四肢摆动由拖拽物理跟拍弹簧叠加）")] [SerializeField] private string 拎起动作名 = "Ani_NPC_Kanban_Paimon_Drag01";
        [Tooltip("空 = Camera.main")] [SerializeField] private Camera 相机;
        [Tooltip("空 = 自动找非影子壳的蒙皮渲染器（用包围盒做接近判定）")] [SerializeField] private SkinnedMeshRenderer 蒙皮渲染器;

        [Header("待机与打招呼")]
        [SerializeField] private string 待机动作名 = "Ani_NPC_Kanban_Paimon_Standby";
        [SerializeField] private string 打招呼动作名 = "Ani_NPC_Kanban_Paimon_Greet";
        [Tooltip("光标距模型包围盒多少屏幕像素内算\"在旁边\"")] [SerializeField] private float 触发边距像素 = 90f;
        [Tooltip("被拎起时下的表情（情绪表名；空=不表表情）")] [SerializeField] private string 拎起情绪名 = "Confuse";
        [Tooltip("光标停留多久触发打招呼")] [SerializeField] private float 触发停留秒 = 1.2f;
        [Tooltip("两次打招呼的最小间隔秒")] [SerializeField] private float 打招呼冷却秒 = 45f;

        [Header("拖拽放下反应（2026-08-27：按时长分档）")]
        [Tooltip("轻反应阈值（秒）：拖够此时长放下播害羞——被拎了一会儿的不好意思")]
        [SerializeField] private float 拖拽轻反应阈值秒 = 3f;
        [Tooltip("轻反应动作名（完整 clip 名；空=该档跳过）")]
        [SerializeField] private string 拖拽轻反应动作名 = "Ani_NPC_Kanban_Paimon_Shy01AS";
        [Tooltip("生气阈值（秒）：拖太久放下播生气")]
        [SerializeField] private float 拖拽生气阈值秒 = 7.5f;
        [Tooltip("生气反应动作名（完整 clip 名；空=该档跳过）")]
        [SerializeField] private string 拖拽生气动作名 = "Ani_NPC_Kanban_Paimon_Anger";

        [Header("随机小动作")]
        [SerializeField] private bool 启用随机小动作 = true;
        [Tooltip("随机轮换的单次动作（完整 clip 名，可增删）")]
        [SerializeField] private string[] 随机小动作列表 =
        {
            "Ani_NPC_Kanban_Paimon_Nod01",
            "Ani_NPC_Kanban_Paimon_ShakeHead01",
            "Ani_NPC_Kanban_Paimon_Sneer01",
            "Ani_NPC_Kanban_Paimon_Clap01",
            "Ani_NPC_Kanban_Paimon_Show_1",
            "Ani_NPC_Kanban_Paimon_Show_2",
            "Ani_NPC_Kanban_Paimon_Show_3",
            "Ani_NPC_Kanban_Paimon_Show_4",
        };
        [Tooltip("随机小动作的间隔范围（秒）")] [SerializeField] private Vector2 小动作间隔秒 = new Vector2(25f, 55f);

        [Header("出场/退场")]
        [Tooltip("进程启动后播的出场动画（完整 clip 名，空=直接待机）——首帧在 PetAnimSwapper 预热完成后播放，播完回待机")]
        [SerializeField] private string 出场动画名 = "Ani_NPC_Kanban_Paimon_Appear";
        [Tooltip("双击退出时播的退场动画（完整 clip 名，空=立即退出）——播完才真正退出进程")]
        [SerializeField] private string 退场动画名 = "Ani_NPC_Kanban_Paimon_Disappear";

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
        private bool _退场完成;              // 退场动画播完（回调已发）：冻结一切行为保持退场末帧，直到进程退出/实例销毁
        private System.Action _退场完成回调; // 退场动画播完执行（进程关闭，由窗口控制器注入）
        private bool _仪式静默中;            // 出场/退场动画期间：眨眼+视线层静默（收尾统一解除）
        private bool _拎起视线静默中;        // 拖拽物理期视线静默（2026-08-26：拎起 KO 垂落头被视线层拉向光标=目检"抬头看抓取点"根因）
        private bool _拎起情绪开着;          // 拖拽物理期的拎起表情状态（边沿检测用）
        private bool _上帧物理中;             // 物理交互结束边沿：回待机/清拎起表情

        /// <summary>当前待机动作：坐定中=坐姿（行为层视角的"待机"随坐定切换），
        /// 否则=站立待机。打招呼/小动作/放下反应播完都回到这里——坐定期间穿插的单次动作自然回坐姿。
        /// 2026-08-28：经 IPetHost 接口分发——桌面=边坐控制器；游戏内=屏幕坐定。</summary>
        private string 当前待机动作
        {
            get
            {
                var host = 宿主;
                if (host != null && host.坐定中 && !string.IsNullOrEmpty(host.坐姿动作)) return host.坐姿动作;
                return 待机动作名;
            }
        }

        /// <summary>坐定中——2026-08-27 用户拍板：坐下就纯坐，打招呼/随机小动作一律不触发。
        /// 桌面=边坐控制器；游戏内=屏幕坐定（经 IPetHost 分发）。</summary>
        private bool 坐定中
        {
            get
            {
                var host = 宿主;
                return host != null && host.坐定中;
            }
        }

        /// <summary>边缘坐掉落中（桌面版锚定窗口消失重力下坠）——期间压制打招呼/小动作新触发。
        /// 游戏内形态无掉落概念，恒 false。</summary>
        private bool 边坐掉落中 => 边坐控制器 != null && 边坐控制器.掉落中;

        /// <summary>宿主分发（IPetHost，docs/19 §6.4 批次 B）：桌面形态=序列化字段 窗口控制器（PetHostBase）；
        /// 游戏内形态=PetInGameHost 注入的宿主。字段保留宿主基类型（场景引用零风险），运行时按接线取。</summary>
        private IPetHost 宿主 => 窗口控制器 != null ? 窗口控制器 : PetInGameHost.宿主接口;

        /// <summary>退场流程中（动画播放期+播完冻结期）——宿主层据此冻结拖拽/滚轮交互
        /// （桌面版 PetWindowController.已请求退出 的同款守卫语义，2026-08-28 游戏内形态补齐）</summary>
        public bool 退场中 => _退场中 || _退场完成;

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
            // 退场已播完：冻结一切行为，保持 Disappear 末帧直到进程退出/实例销毁
            //（2026-08-28 游戏内热切换"播完就杀"配套——旧逻辑落回单次动作分支会切回待机"站起来"，
            // 销毁前多停留数秒且姿势穿帮；编辑器下退完保持末帧，重新 Play 可反复目检）
            if (_退场完成) return;

            // 守卫按宿主判空（游戏内形态窗口控制器字段为 null，宿主由 PetInGameHost 注入——
            // 2026-08-28 修复：原按 窗口控制器==null 早退，游戏内形态行为层全瘫只剩待机）
            var host = 宿主;
            if (动作播放器 == null || host == null || 相机 == null) return;

            // 退场收尾：退场动画播完 → 交回退出回调（进程关闭由 PetWindowController 执行）。
            if (_退场中)
            {
                if (Time.time - _单次开始 > 0.25f && !动作播放器.是否在播(_当前单次名))
                {
                    _退场中 = false;
                    _退场完成 = true;
                    var cb = _退场完成回调;
                    _退场完成回调 = null;
                    cb?.Invoke();
                }
                return;
            }

            bool 物理中 = 宿主.物理交互中;
            if (物理中)
            {
                拖拽物理期处理();
                return;
            }
            if (_上帧物理中) 物理结束帧();

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
                单次动作帧();
                return; // 单次动作进行中不叠新触发
            }

            bool 接近 = 检测光标接近();

            // 坐定中不打招呼（2026-08-27 拍板：坐下就纯坐）
            if (接近 && !边坐掉落中 && !坐定中 && Time.time - _上次打招呼 >= 打招呼冷却秒)
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

            // 随机小动作：光标不在旁边且无物理交互（拖拽/收尾）时才轮换——在旁时留给打招呼/
            // 视线跟随；物理交互期（拎起/收尾）不叠新动作，保持拖拽体验纯粹（2026-08-26）；
            // 边坐掉落中/坐定中同样压制（空中别穿插单次动作；坐下就纯坐——2026-08-27 拍板）
            if (启用随机小动作 && !接近 && !边坐掉落中 && !坐定中 && !宿主.物理交互中 && 随机小动作列表.Length > 0 && Time.time >= _下次小动作时刻)
            {
                播单次(随机小动作列表[Random.Range(0, 随机小动作列表.Length)]);
                _下次小动作时刻 = Time.time + Random.Range(小动作间隔秒.x, 小动作间隔秒.y);
            }
        }

        /// <summary>拖拽物理期处理（拎起动画 v3，2026-08-26 用户拍板改主流桌宠式）：播专用 Drag01 垂落动画
        /// （业界 VPet/eSheep 被提起专用动画的 clip 等价物：四肢常量垂落、躯干保留待机微动、
        /// 无程序化骨骼叠加）+ 拖拽物理四肢跟拍弹簧摆动；慌张表情直发情绪层。
        /// 沿革：v1 程序化四肢垂落叠加层（PetDanglePoseController 已弃用留库）→ v2 Sleep01
        /// 躺姿+根旋转 90° 横躺（仓鼠式）→ v3 专用垂落动画。退场优先于物理（退场中抓住：
        /// 物理甩归甩，姿势保持退场动画，几秒后进程退出）。</summary>
        void 拖拽物理期处理()
        {
            if (_单次进行中)
            {
                // 被物理打断的单次动作：恢复其设置的标志（收尾分支不走了）
                _单次进行中 = false;
                if (_仪式静默中) 置仪式静默(false); // 出场动画被打断
                else 眨眼控制器?.Set静默(false);
                宿主.暂停命中烘焙 = false;
            }
            if (!string.IsNullOrEmpty(拎起动作名) && 动作播放器.动作存在(拎起动作名) && !动作播放器.是否在播(拎起动作名))
                动作播放器.Play(拎起动作名); // loop 播放（Play 会先下发映射情绪，如 Sleep→Sleepy）
            // 拎起期视线静默（2026-08-26）：光标钉在抓点上，视线层会把 Drag01 垂落的头持续
            // 拉向光标（KO 瘫软读成"抬头看抓取点"）——头链完全交给 Drag01 曲线；解除时
            // Set视线静默(false) 内部有 _刚恢复 重同步，头会平滑回到视线跟随而非瞬移
            if (!_拎起视线静默中)
            {
                _拎起视线静默中 = true;
                视线控制器?.Set视线静默(true);
            }
            if (!_拎起情绪开着)
            {
                _拎起情绪开着 = true;
                // 拎起情绪在动作映射情绪之后下发（覆盖 Sleepy）——慌张感优先
                if (情绪控制器 != null) 情绪控制器.SetEmotion(拎起情绪名);
            }
            _上帧物理中 = true;
        }

        /// <summary>物理刚结束帧：清拎起表情+视线恢复 → 放下反应分档 / 边坐接管 / 回待机</summary>
        void 物理结束帧()
        {
            // 物理刚结束：清拎起表情 + 回待机（CrossFade 从拎起动作平滑过渡）
            _上帧物理中 = false;
            if (_拎起视线静默中)
            {
                _拎起视线静默中 = false;
                视线控制器?.Set视线静默(false); // 内部 _刚恢复 重同步平滑基准，头平滑回到视线跟随
            }
            if (_拎起情绪开着)
            {
                _拎起情绪开着 = false;
                if (情绪控制器 != null && !_单次进行中) 情绪控制器.SetEmotion("");
            }
            // 拖拽放下反应（2026-08-27）：按本次拖拽时长分档——短拖（<轻阈值）无反应直接回待机
            // （快速挪位置不打扰）；中档（≥轻阈值）害羞；长拖（≥生气阈值）生气。
            // 播单次含情绪映射（Anger→生气表情/Shy→害羞）+动作期烘焙暂停，播完自然回待机。
            float 拖了秒 = 宿主.拖拽秒;
            string 放下反应 = null;
            if (拖了秒 >= 拖拽生气阈值秒 && 动作播放器.动作存在(拖拽生气动作名))
                放下反应 = 拖拽生气动作名;
            else if (拖了秒 >= 拖拽轻反应阈值秒 && 动作播放器.动作存在(拖拽轻反应动作名))
                放下反应 = 拖拽轻反应动作名;
            if (!string.IsNullOrEmpty(放下反应))
                播单次(放下反应);
            else if (边坐控制器 != null && 边坐控制器.评估吸附并坐())
            {
                // 桌面版边坐接管：松手位置附近有横框（窗口顶边/底边/任务栏顶），已磁吸落座播坐姿动画
            }
            else if (!坐定中 && !_单次进行中 && 动作播放器.动作存在(当前待机动作))
            {
                // 未被边坐/屏幕坐接管 → 回待机。游戏内形态的屏幕边坐在宿主控制器物理收口里评估
                // （坐定中=已接管播了坐姿）；原代码这里按 `宿主 is PetInGameHostController` 空跳过，
                // 游戏内松手后 Drag01 循环永不切回=保持被拖拽姿势（2026-08-28 用户报障根因，已合一）
                动作播放器.Play(当前待机动作);
            }
        }

        /// <summary>单次动作收尾帧：播完（或 CrossFade 路径的尾段提前过渡）→ 解除静默/烘焙暂停，
        /// 回当前待机动作。</summary>
        void 单次动作帧()
        {
            if (Time.time - _单次开始 > 0.25f)
            {
                bool finished = !动作播放器.是否在播(_当前单次名);
                // 尾段提前过渡（2026-08-25）：仅 CrossFade 路径需要（消除"播完定格→再淡入"）。
                // 惯性化路径（2026-08-27）不截尾：速度承接使任意切点无缝，完整播完保留作者收尾
                //（0.6s 过渡下截尾窗口 0.54s，截断动作会读成"没收尾就切走"）。
                float rem = 动作播放器.剩余秒(_当前单次名);
                bool tailBlend = !动作播放器.惯性化启用 && !finished && !_仪式静默中 && rem > 0f && rem <= 动作播放器.过渡秒 * 0.9f;
                if (finished || tailBlend)
                {
                    _单次进行中 = false;
                    if (_仪式静默中) 置仪式静默(false); // 仪式（出场）静默解除；普通单次动作本来就没静默视线
                    else 眨眼控制器?.Set静默(false);
                    宿主.暂停命中烘焙 = false;
                    动作播放器.Play(当前待机动作);
                }
            }
        }

        void 播单次(string 动作名)
        {
            _当前单次名 = 动作名;
            _单次进行中 = true;
            _单次开始 = Time.time;
            眨眼控制器?.Set静默(true);
            宿主.暂停命中烘焙 = true; // 动作期间停 MeshCollider 重烘（烘焙=掉帧尖峰，2026-08-24）
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

        /// <summary>模型世界包围盒 → 屏幕矩形（扩边距）→ 是否含光标。
        /// 包围盒来源=窗口控制器的命中网格碰撞体（BakeMesh 烘的真实蒙皮网格，世界包围盒正确）；
        /// 旧用 SMR.bounds 是 ×100 垃圾值（GI 模型漏一层缩放，42 单位 vs 可见 0.6），投影恒跨相机平面
        /// → 判定恒 false → 招手永不触发（2026-08-26 根治）。</summary>
        bool 检测光标接近()
        {
            if (宿主.正在拖拽) return false;
            if (!宿主.TryGet光标Unity屏幕位置(out Vector2 sp)) return false;

            Bounds b;
            if (宿主.TryGet命中世界包围盒(out b))
            {
                // 命中网格路径（首选）：真实蒙皮世界包围盒
            }
            else if (蒙皮渲染器 != null)
            {
                b = 蒙皮渲染器.bounds; // 兜底（碰撞体未就绪的首帧）
            }
            else return false;

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
                if (p.z <= 0f) return false; // 包围盒跨到相机后=异常状态（纵深位移残余等），不视为接近
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
            }
            return sp.x >= minX - 触发边距像素 && sp.x <= maxX + 触发边距像素
                && sp.y >= minY - 触发边距像素 && sp.y <= maxY + 触发边距像素;
        }
    }
}
