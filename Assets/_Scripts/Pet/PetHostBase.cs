using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 派蒙宿主共用基类（2026-08-28 共用化重构，docs/19 §6.4 复用边界）：桌面版（PetWindowController，
    /// Win32 窗口）与游戏画面内版（PetInGameHostController，全屏 RT 画中画）的共用层——
    /// 拖拽拎起姿势应用（根旋转+绕骨盆枢轴补偿+挣扎摆扭）、四肢摆动叠加、命中网格一次性烘焙、
    /// 缩放平滑、找骨/找本体蒙皮辅助。宿主差异（光标来源/移动"窗口"的方式/缩放上限语义/持久化/坐定语义）
    /// 以抽象成员与虚方法留在子类。
    ///
    /// 序列化字段迁移说明：拖拽物理/骨盆骨名/四肢骨名/拎起横躺角/拎起转身角/横躺融合速度/烘焙恢复宽限秒/
    /// 缩放最小/缩放最大/初始缩放倍率/缩放平滑速度/缩放步进 原为两控制器各自的同名同默认值字段——
    /// Unity 按字段名序列化（与声明类无关），移入基类同名保留：场景（PaimonPet.unity）与
    /// prefab（PaimonInGameRoot.prefab）的已接线引用与调参值不丢（改名才需要 FormerlySerializedAs）。
    /// </summary>
    public abstract class PetHostBase : MonoBehaviour, IPetHost
    {
        [Header("拖拽物理（docs/19 §6.1 刚体跟随+四肢摆动；纯模拟在 PetDragPhysicsController）")]
        [Tooltip("拖拽物理模拟组件（纯数学：刚体直跟+四肢跟拍弹簧+挣扎，松手即停），根旋转/四肢骨应用由宿主执行")]
        [SerializeField] protected PetDragPhysicsController 拖拽物理;

        [Header("拖拽锚点（两形态同骨名）")]
        [Tooltip("拖拽物理的锚点骨名（骨盆目标投影与拎起姿势旋转枢轴基准）")]
        [SerializeField] protected string 骨盆骨名 = "Bip001 Pelvis";
        [Tooltip("四肢摆动的驱动骨名（拖拽跟拍弹簧），顺序=[左臂,右臂,左腿,右腿]——物理组件按此索引分配增益/频率（手臂摆幅大频率低、腿相反）。取本体骨架骨（自动排除影子壳）")]
        [SerializeField] protected string[] 四肢骨名 = { "Bip001 L UpperArm", "Bip001 R UpperArm", "Bip001 L Thigh", "Bip001 R Thigh" };

        [Header("拎起姿势（Drag01 瘫软式+3/4 偏左转身）")]
        [Tooltip("被拎起时身体姿势基准角（度）：0=直立垂落（主流桌宠被提起姿态，配 Drag01 专用拎起动画），90=头朝左横躺（旧仓鼠式）。松手收尾自动平滑归零")]
        [SerializeField] protected float 拎起横躺角 = 0f;
        [Tooltip("被拎起时身体绕竖直轴转身角（度）：45=3/4 偏左（桌面版 v6 用户参考图），0=正对玩家。松手收尾自动平滑归零")]
        [SerializeField] protected float 拎起转身角 = 45f;
        [Tooltip("横躺角/转身角淡入淡出速度（每秒指数趋近率）——抓起转过去/松手转回来的快慢")]
        [SerializeField] protected float 横躺融合速度 = 7f;

        [Header("命中")]
        [Tooltip("交互结束后延迟多久补烘一次（秒）：交互期（动作/拖拽）姿势变了，结束后补烘一次更新碰撞体；快速连续交互顺延，永不打扰正在交互的用户")]
        [SerializeField] protected float 烘焙恢复宽限秒 = 2f;

        [Header("缩放")]
        [Tooltip("缩放倍率下限")] [SerializeField] protected float 缩放最小 = 0.4f;
        [Tooltip("缩放倍率上限（桌面版按工作区 95% 钳制；游戏内版按最大屏高占比动态钳制）")] [SerializeField] protected float 缩放最大 = 2f;
        [Tooltip("无存档时的初始缩放倍率（有存档用存档值）")] [SerializeField] protected float 初始缩放倍率 = 0.7f;
        [Tooltip("缩放平滑过渡速度：每秒指数趋近速率，越大越跟手；0=瞬达无平滑")] [SerializeField] protected float 缩放平滑速度 = 12f;
        [Tooltip("每格滚轮的缩放步进（乘法），越小越精细")] [SerializeField] protected float 缩放步进 = 1.05f;

        [Tooltip("派蒙模型根（游戏内 prefab 接线；桌面场景未接线=运行时找场景内 Paimon）")]
        [SerializeField] protected Transform paimon根;

        // ---- 共用运行时状态 ----
        protected SkinnedMeshRenderer 蒙皮渲染器;
        protected MeshCollider 命中网格碰撞体;
        protected Mesh 烘焙网格;
        protected Transform _骨盆;            // 拖拽物理锚点骨（本体骨架，排除影子壳）
        protected Transform[] _四肢骨;        // 四肢摆动驱动骨（与 PetDragPhysicsController 索引约定一致）
        protected Quaternion _拖拽基准旋转 = Quaternion.identity; // 拖拽物理期根旋转基准（收尾中被再抓不重取，防旋转叠加）
        protected Vector3 _拖拽基准根位置;    // 桌面版收口精确还原根位置用（桌面模型位置本不被拖拽改，窗口在动）
        protected float _当前横躺角;          // 平滑中的拎起姿势基准角（度）
        protected float _当前转身角;          // 平滑中的拎起转身角（度）
        protected float 目标缩放 = 1f;        // 滚轮缩放的目标倍率（持久化存这个值）
        protected float 显示缩放 = 1f;        // 实际应用倍率（每帧向目标指数平滑趋近）
        protected float 初始缩放;             // 模型根基准 localScale.x（子类按各自画布语义折算）
        protected float 有效缩放最大 = 2f;    // 钳制后的实际上限（桌面=工作区预算；游戏内=屏高占比，烘焙后重算）
        protected float 烘焙恢复时刻 = -10f;
        protected bool _上帧烘焙被暂停;
        protected bool _烘焙待补;

        /// <summary>日志前缀（子类按形态标注）</summary>
        protected virtual string 日志标签 => "[PetHost]";

        // ---- IPetHost 共用实现 ----

        /// <summary>暂停命中网格重烘（行为层在单次动作期间置真）——一次性烘焙机制的开关，两形态同款</summary>
        public bool 暂停命中烘焙 { get; set; }

        /// <summary>物理交互进行中（拖拽跟随/收尾归零）——行为层压制触发用</summary>
        public bool 物理交互中 => 拖拽物理 != null && 拖拽物理.交互中;

        /// <summary>最近一次拖拽时长（秒，松手时冻结；-1=无）——行为层放下反应分档用</summary>
        public float 拖拽秒 => 拖拽物理 != null ? 拖拽物理.本次拖拽时长 : -1f;

        /// <summary>命中网格的世界包围盒（行为层接近判定用）：MeshCollider（BakeMesh 烘的真实蒙皮网格+
        /// 与 SMR 同 transform，PhysX 世界包围盒正确）。勿用 SMR.bounds：GI 模型的它漏一层缩放
        /// （世界 42 单位 vs 可见 0.6，×100 错误），投影恒跨相机平面→接近判定恒 false（2026-08-26 根治）。
        /// 碰撞体未就绪时返回 false。</summary>
        public bool TryGet命中世界包围盒(out Bounds bounds)
        {
            bounds = default;
            if (命中网格碰撞体 == null || 命中网格碰撞体.sharedMesh == null) return false;
            bounds = 命中网格碰撞体.bounds;
            return true;
        }

        public abstract bool 正在拖拽 { get; }
        public abstract bool TryGet光标Unity屏幕位置(out Vector2 unityScreenPos);
        public abstract bool 坐定中 { get; }
        public abstract string 坐姿动作 { get; }

        // ---- 找骨/找蒙皮（排除影子壳 _DropShadow / MMD_DropShadow 下的同名骨拷贝） ----

        protected Transform 找本体骨(string boneName)
        {
            if (paimon根 == null) return null;
            foreach (var t in paimon根.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != boneName) continue;
                bool 影子下 = false;
                for (var p = t.parent; p != null && !影子下; p = p.parent)
                    if (p.name == "_DropShadow" || p.name == "MMD_DropShadow") 影子下 = true;
                if (影子下) continue;
                return t;
            }
            return null;
        }

        /// <summary>取本体蒙皮渲染器（排除影子壳/PaimonShadow 层）——两形态同款兜底查找</summary>
        protected SkinnedMeshRenderer 找本体蒙皮渲染器()
        {
            var shadowLayer = LayerMask.NameToLayer("PaimonShadow");
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!smr.name.Contains("_DropShadow") && smr.gameObject.layer != shadowLayer)
                    return smr;
            return null;
        }

        /// <summary>接线拖拽骨骼（骨盆+四肢）。缺失肢不摆动（告警不阻断）。</summary>
        protected void 找拖拽骨骼()
        {
            if (paimon根 == null) return;
            _骨盆 = 找本体骨(骨盆骨名);
            _四肢骨 = new Transform[四肢骨名 != null ? 四肢骨名.Length : 0];
            int 找到 = 0;
            for (int i = 0; i < _四肢骨.Length; i++)
            {
                _四肢骨[i] = 找本体骨(四肢骨名[i]);
                if (_四肢骨[i] != null) 找到++;
            }
            if (找到 < _四肢骨.Length)
                Debug.LogWarning($"{日志标签} 四肢摆动骨缺失 {_四肢骨.Length - 找到}/{_四肢骨.Length}（缺失肢不摆动）");
        }

        // ---- 命中代理与一次性补烘（docs/19 §6.4：两形态像素级命中同配方） ----

        /// <summary>像素级命中代理：BakeMesh 烘蒙皮网格 + 同 transform MeshCollider（useScale=true
        /// 输出在 SMR 局部空间，命中节点须与 SMR 同 transform）。快烹饪：只吃 1 条 raycast/帧，
        /// UseFastMidphase 换取 cook 尖峰显著缩短（全量 cook 15-22ms 是动作切换停顿根因之一）。</summary>
        protected void 建命中代理()
        {
            if (蒙皮渲染器 == null) return;
            var hitGo = new GameObject("_HitMeshProxy");
            hitGo.transform.SetParent(蒙皮渲染器.transform.parent, false);
            hitGo.transform.localPosition = 蒙皮渲染器.transform.localPosition;
            hitGo.transform.localRotation = 蒙皮渲染器.transform.localRotation;
            hitGo.transform.localScale = 蒙皮渲染器.transform.localScale;
            命中网格碰撞体 = hitGo.AddComponent<MeshCollider>();
            命中网格碰撞体.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;
            烘焙网格 = new Mesh();
            蒙皮渲染器.BakeMesh(烘焙网格, true);
            命中网格碰撞体.sharedMesh = 烘焙网格;
        }

        /// <summary>一次性补烘（2026-08-27 终案）：周期性重烘全删——交互期（动作/拖拽）姿势变了，
        /// 解除+宽限期到后补烘一次；快速连续交互不断顺延（碰撞体保持旧壳，±2-3° 姿态差=几像素无损）。
        /// 启动时 建命中代理 已烘一次。</summary>
        protected void 补烘命中网格帧()
        {
            bool 烘焙被暂停 = 暂停命中烘焙 || 物理交互中;
            if (烘焙被暂停 && !_上帧烘焙被暂停) _烘焙待补 = true;        // 进入交互：姿势要变了
            if (_上帧烘焙被暂停 && !烘焙被暂停)
                烘焙恢复时刻 = Time.unscaledTime + Mathf.Max(0f, 烘焙恢复宽限秒); // 解除：宽限后补
            _上帧烘焙被暂停 = 烘焙被暂停;
            if (_烘焙待补 && !烘焙被暂停 && Time.unscaledTime >= 烘焙恢复时刻
                && 蒙皮渲染器 != null && 命中网格碰撞体 != null)
            {
                蒙皮渲染器.BakeMesh(烘焙网格, true);
                命中网格碰撞体.sharedMesh = null; // 强制碰撞体刷新
                命中网格碰撞体.sharedMesh = 烘焙网格;
                _烘焙待补 = false;
                PetDiag.上次蒙皮重烘 = Time.unscaledTime; // 顿挫诊断标记（PetFrameStats 回查）
                烘焙完成后();
            }
        }

        /// <summary>补烘完成钩子（游戏内版重算动态缩放上限——烘焙姿势变→模型高变→上限跟随）</summary>
        protected virtual void 烘焙完成后() { }

        // ---- 拎起姿势应用（docs/19 §6.1：两形态视觉等价——桌面版靠移窗钉骨盆，游戏内版移根） ----

        /// <summary>拖拽起手快照根旋转基准（收尾中被再抓不重取，防旋转叠加——两形态同语义）。
        /// include位置=桌面版附加快照根位置（收口精确还原）；游戏内版模型位置=拖拽结果不还原。</summary>
        protected void 快照拖拽基准(bool include位置)
        {
            if (物理交互中 || paimon根 == null) return;
            _拖拽基准旋转 = paimon根.localRotation;
            if (include位置) _拖拽基准根位置 = paimon根.position;
        }

        /// <summary>拎起姿势角平滑+根旋转应用（桌面版 应用物理帧 的旋转部分与游戏内版同一段，2026-08-28 合一）：
        /// 拖拽期→拎起横躺角/拎起转身角，收尾期→0 平滑归零；+挣扎摆/扭（绕骨盆枢轴）；
        /// 旋转后补偿根平移把骨盆钉回旋转前世界位（动画微动保留，仅抵消旋转带来的位移）。</summary>
        protected void 拎起姿势角帧(bool 拖拽期)
        {
            if (_骨盆 == null || paimon根 == null || 拖拽物理 == null) return;
            float 横躺目标 = 拖拽期 ? 拎起横躺角 : 0f;
            float 转身目标 = 拖拽期 ? 拎起转身角 : 0f;
            float k = 1f - Mathf.Exp(-横躺融合速度 * Time.unscaledDeltaTime);
            _当前横躺角 = Mathf.Lerp(_当前横躺角, 横躺目标, k);
            _当前转身角 = Mathf.Lerp(_当前转身角, 转身目标, k);

            float 挣摆 = 拖拽物理.当前挣扎摆角;
            float 挣扭 = 拖拽物理.当前挣扎扭角;
            Vector3 骨盆旋转前 = _骨盆.position;
            paimon根.localRotation = Quaternion.AngleAxis(_当前横躺角 + 挣摆, Vector3.forward)
                                   * Quaternion.AngleAxis(_当前转身角 + 挣扭, Vector3.up)
                                   * _拖拽基准旋转;
            Vector3 位移 = 骨盆旋转前 - _骨盆.position;
            if (位移.sqrMagnitude > 1e-10f) paimon根.position += 位移;
        }

        /// <summary>四肢摆动叠加（Animation 每帧重写骨骼姿势后在其上叠加一次，不累积）：
        /// 物理组件输出的摆动角以世界 Z 轴（屏幕平面法线）旋转叠加到四肢根骨（肩/大腿）。
        /// **Transform.rotation 就是世界旋转**（localRotation 才是父系量）——直接前置乘 AngleAxis 即正确，
        /// 勿做父系共轭换算（曾致左右手反向摆）。交互结束后停止应用，动画自然覆盖残留。
        /// 由子类 LateUpdate 调用。</summary>
        protected void 四肢摆动应用帧()
        {
            if (拖拽物理 == null || !拖拽物理.交互中 || _四肢骨 == null) return;
            for (int i = 0; i < _四肢骨.Length; i++)
            {
                var 骨 = _四肢骨[i];
                if (骨 == null) continue;
                拖拽物理.取四肢摆动(i, out float 摆动角);
                if (摆动角 != 0f)
                    骨.rotation = Quaternion.AngleAxis(摆动角, Vector3.forward) * 骨.rotation;
            }
        }

        /// <summary>物理交互收口的旋转归位（默认=游戏内语义：模型位置是拖拽结果要保留，只按骨盆
        /// 枢轴补偿把旋转归回基准）；桌面版 override=旋转+位置精确还原基准（桌面模型位置本不动，窗口在动）。</summary>
        protected virtual void 拖拽收口归位()
        {
            if (paimon根 != null && _骨盆 != null)
            {
                Vector3 骨盆旋转前 = _骨盆.position;
                paimon根.localRotation = _拖拽基准旋转;
                Vector3 位移 = 骨盆旋转前 - _骨盆.position;
                if (位移.sqrMagnitude > 1e-10f) paimon根.position += 位移;
            }
            _当前横躺角 = 0f;
            _当前转身角 = 0f;
        }

        // ---- 缩放（两形态同款：只改目标倍率+平滑应用模型 localScale） ----

        /// <summary>缩放平滑过渡帧：显示缩放向目标指数趋近，只改模型 localScale（桌面版窗口尺寸恒定
        /// 防闪烁；游戏内版恒整屏画布）。速度=0 时步进=1（瞬达）。</summary>
        protected void 缩放平滑帧()
        {
            if (Mathf.Approximately(显示缩放, 目标缩放)) return;
            float 步进 = 缩放平滑速度 <= 0f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime * 缩放平滑速度);
            显示缩放 += (目标缩放 - 显示缩放) * 步进;
            if (Mathf.Abs(目标缩放 - 显示缩放) < 0.0005f) 显示缩放 = 目标缩放;
            应用模型缩放();
        }

        /// <summary>把显示缩放叠乘基准缩放应用到 Paimon 根 localScale</summary>
        protected void 应用模型缩放()
        {
            if (paimon根 == null) return;
            float s = 初始缩放 * 显示缩放;
            paimon根.localScale = new Vector3(s, s, s);
        }
    }
}
