// ==================== GlassPanelAnimator.cs（毛玻璃面板动画公共组件） ====================
// 配方唯一承载（2026-09-13 用户拍板提取：三屏手写同一配方漂移实证——Coop 单层变体打回）：
// · 毛玻璃双层配方（docs/14 §38b）：模糊层 fill 顶→底扫入/底→顶扫出；变暗层 BackDim=结构约定
//   （面板首子节点或 Canvas 内紧贴模糊层之上），瞬时起落，不经本组件驱动；
// · 内容元素分向滑入滑出（显式条目方向，或按位置自动分上/下）+ 可选内容组淡入淡出；
// · 纪律内建：§39 目标位缓存幂等（Awake 自动）、§37 ② 首帧跳过计时、退场起始值现场捕获。
// 宿主 Screen 负责锁与 isClosing 守卫（ScreenBase.PlayGlassEnter 包装）——本组件只管动画本身。
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace GIC.UI
{


    /// <summary>
    /// 毛玻璃面板动画器。挂载约定：
    /// · 通用模式（Backpack/Settings）：挂面板根，模糊层引用指向 Canvas 下的 BackPanel Image，
    ///   内容元素用显式条目（自动收集关闭）；
    /// · 面板自身即模糊层模式（Coop 多视图）：挂各视图面板自身，模糊层可空（自动取自身
    ///   UIBlurCapture），开自动收集（容器直接子节点，排除 BackDim）+ 额外元素（如面板外共享按钮）。
    /// </summary>
    [DisallowMultipleComponent]
    public class GlassPanelAnimator : MonoBehaviour
    {
        public enum 滑入方向 { 按位置自动, 从上, 从下, 从左, 从右 }

        [Serializable]
        public class 内容元素条目
        {
            [Tooltip("参与滑入滑出的元素")]
            public RectTransform 元素;
            [Tooltip("滑入方向；按位置自动=上半屏从上、下半屏从下（激活时现算）")]
            public 滑入方向 方向 = 滑入方向.按位置自动;
            [Tooltip("滑入偏移；0=用默认内容偏移")]
            public float 偏移 = 0f;
        }

        [Header("毛玻璃层")]
        [Tooltip("UIBlur 的 Image（fill 驱动扫入扫出）；空=自动取子树首个 UIBlurCapture")]
        public Image 模糊层;

        [Header("内容元素")]
        [Tooltip("显式元素条目（通用模式）；空列表配合自动收集使用")]
        public List<内容元素条目> 内容元素 = new();
        [Tooltip("自动收集内容容器的直接子节点（排除 BackDim）——面板自身即模糊层的多视图模式用")]
        public bool 自动收集内容 = false;
        [Tooltip("自动收集的容器；空=自身")]
        public RectTransform 内容容器;
        [Tooltip("自动收集之外的补充条目（如两视图面板外的共享返回钮）")]
        public List<内容元素条目> 额外元素 = new();

        [Header("内容淡入组（纯淡入淡出，不滑动）")]
        [Tooltip("入场淡入+退场淡出的 CanvasGroup；空=无")]
        public List<CanvasGroup> 内容淡入组 = new();
        [Tooltip("True=入场随之淡入（Settings 中央区）；False=入场保持不透明、仅退场淡出（Backpack 卡区）")]
        public bool 淡入组入场淡入 = true;

        [Header("参数")]
        public float 滑动时长 = 0.25f;
        public AnimationCurve 滑动曲线 = new AnimationCurve(
            new Keyframe(0, 0, 2f, 2f),
            new Keyframe(1, 1, 0f, 0f)
        );
        public float 默认内容偏移 = 60f;

        // ── 运行时缓存（§39 幂等：唯一合法缓存点=Awake 预热实例化，二次缓存会污染目标位） ──
        private bool _targetsCached = false;
        private Image _blur;
        private readonly List<RectTransform> _rects = new();
        private readonly List<Vector2> _targets = new();
        private readonly List<Vector2> _dirs = new();       // 每轮 SetEntryOffsets/ExitRoutine 现算
        private readonly List<float> _offsets = new();
        private readonly List<滑入方向> _rawDirs = new();

        private void Awake()
        {
            CacheTargets();
        }

        /// <summary>目标位缓存（幂等）：元素=序列化锚位；模糊层解析引用。Awake 自动调用。</summary>
        public void CacheTargets()
        {
            if (_targetsCached) return;
            _targetsCached = true;

            if (模糊层 != null) _blur = 模糊层;
            else
            {
                var cap = GetComponentInChildren<GIC.UI.UIBlurCapture>(true);
                if (cap != null) _blur = cap.GetComponent<Image>();
            }

            foreach (var e in 内容元素) AddElem(e);
            if (自动收集内容)
            {
                var container = 内容容器 != null ? 内容容器 : (RectTransform)transform;
                foreach (Transform child in container)
                {
                    if (child is not RectTransform rt) continue;
                    if (rt.name == "BackDim") continue; // 变暗层瞬时起落不滑动（§38b）
                    AddElem(new 内容元素条目 { 元素 = rt }); // 方向=按位置自动，偏移=默认
                }
            }
            foreach (var e in 额外元素) AddElem(e);
        }

        private void AddElem(内容元素条目 e)
        {
            if (e == null || e.元素 == null) return;
            if (_rects.Contains(e.元素)) return; // 幂等：共享元素被多来源重复登记时去重
            _rects.Add(e.元素);
            _targets.Add(e.元素.anchoredPosition);
            _rawDirs.Add(e.方向);
            _offsets.Add(e.偏移 > 0f ? e.偏移 : 默认内容偏移);
        }

        /// <summary>入场起始态（宿主 OnShow 同帧调用，防首帧闪现 §37 ①）：fill=0 + 元素移偏移位。</summary>
        public void SetEntryOffsets()
        {
            CacheTargets();
            ResolveDirs();
            if (_blur != null) _blur.fillAmount = 0f;
            for (int i = 0; i < _rects.Count; i++)
                _rects[i].anchoredPosition = _targets[i] + _dirs[i] * _offsets[i];
            foreach (var g in 内容淡入组)
                if (g != null) g.alpha = 淡入组入场淡入 ? 0f : 1f;
        }

        /// <summary>静止完成态（状态切换/中断复位用）：fill=1 + 元素归位 + 淡入组恢复。</summary>
        public void SnapToRest()
        {
            CacheTargets();
            ResolveDirs();
            if (_blur != null) _blur.fillAmount = 1f;
            for (int i = 0; i < _rects.Count; i++)
                _rects[i].anchoredPosition = _targets[i];
            foreach (var g in 内容淡入组)
                if (g != null) g.alpha = 1f;
        }

        /// <summary>
        /// 入场协程（§37 ② 首帧跳过计时；自身失活即跳出——isClosing 守卫由宿主包装承担）。
        /// </summary>
        public IEnumerator EnterRoutine()
        {
            CacheTargets();
            ResolveDirs();

            // 首帧跳过计时（§37 ②）：打开帧 deltaTime 会吃到激活尖峰帧时长（常 >动画时长）
            yield return null;

            float elapsed = 0f;
            while (elapsed < 滑动时长)
            {
                if (!gameObject.activeInHierarchy) yield break; // 面板被切走/入池：交由宿主复位

                elapsed += Time.deltaTime;
                float t = 滑动曲线.Evaluate(elapsed / 滑动时长);

                // 毛玻璃顶→底扫入（双层配方：变暗瞬时由 BackDim 承担，此处纯模糊实心扫）
                if (_blur != null) _blur.fillAmount = t;
                for (int i = 0; i < _rects.Count; i++)
                    _rects[i].anchoredPosition =
                        _targets[i] + _dirs[i] * _offsets[i] * (1f - t);
                if (淡入组入场淡入)
                    foreach (var g in 内容淡入组)
                        if (g != null) g.alpha = t;

                yield return null;
            }

            SnapToRest();
        }

        /// <summary>
        /// 退场协程：毛玻璃底→顶扫出（与入场镜像）+ 元素滑出 + 淡入组淡出。
        /// 起始值全部现场捕获——入场被秒关打断时从半途平滑续退无跳变。
        /// 时长系数：屏级退场 0.7（既有三屏惯例）；状态切换退场可传更小值（如 0.5）。
        /// </summary>
        public IEnumerator ExitRoutine(float 时长系数 = 0.7f)
        {
            CacheTargets();
            ResolveDirs();

            float duration = Mathf.Max(0.01f, 滑动时长 * 时长系数);
            float startFill = _blur != null ? _blur.fillAmount : 1f;
            var startPos = new Vector2[_rects.Count];
            for (int i = 0; i < _rects.Count; i++) startPos[i] = _rects[i].anchoredPosition;
            var groupStart = new float[内容淡入组.Count];
            for (int i = 0; i < 内容淡入组.Count; i++)
                groupStart[i] = 内容淡入组[i] != null ? 内容淡入组[i].alpha : 1f;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = 滑动曲线.Evaluate(elapsed / duration);

                if (_blur != null) _blur.fillAmount = Mathf.Lerp(startFill, 0f, t);
                for (int i = 0; i < _rects.Count; i++)
                    _rects[i].anchoredPosition = Vector2.Lerp(
                        startPos[i], _targets[i] + _dirs[i] * _offsets[i], t);
                for (int i = 0; i < 内容淡入组.Count; i++)
                    if (内容淡入组[i] != null)
                        内容淡入组[i].alpha = Mathf.Lerp(groupStart[i], 0f, t);

                yield return null;
            }

            if (_blur != null) _blur.fillAmount = 0f;
            foreach (var g in 内容淡入组)
                if (g != null) g.alpha = 0f;
        }

        /// <summary>方向解析（幂等重算）："按位置自动"以激活时世界 Y 为权威（Awake 布局未定）</summary>
        private void ResolveDirs()
        {
            if (_dirs.Count != _rects.Count)
            {
                _dirs.Clear();
                for (int i = 0; i < _rects.Count; i++) _dirs.Add(Vector2.zero);
            }
            // 参照中心：面板根挂载模式（Backpack/Settings）根是纯 Transform 非 RectTransform——
            // 须 null 安全回退（其"按位置自动"方向不使用；Coop 模式组件在 RectTransform 面板上）
            float centerY;
            if (内容容器 != null) centerY = 内容容器.position.y;
            else if (transform is RectTransform rtSelf) centerY = rtSelf.position.y;
            else
            {
                var canvas = GetComponentInChildren<Canvas>(true);
                centerY = canvas != null ? ((RectTransform)canvas.transform).position.y : 0f;
            }
            for (int i = 0; i < _rects.Count; i++)
            {
                _dirs[i] = _rawDirs[i] switch
                {
                    滑入方向.从上 => Vector2.up,
                    滑入方向.从下 => Vector2.down,
                    滑入方向.从左 => Vector2.left,
                    滑入方向.从右 => Vector2.right,
                    _ => _rects[i].position.y >= centerY ? Vector2.up : Vector2.down,
                };
            }
        }
    }
}
