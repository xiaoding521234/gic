using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{


    [RequireComponent(typeof(ScrollRect))]
    public class SimpleMapZoom : MonoBehaviour, IScrollHandler
    {
        [Header("缩放设置")]
        [SerializeField] private float minScale = 0.8f;
        [SerializeField] private float maxScale = 1.4f;
        [SerializeField] private float zoomSpeed = 0.1f;

        [Header("入场动画")]
        [SerializeField] private bool enableEntryAnimation = true;
        [SerializeField] private float animationDuration = 0.2f;
        [SerializeField] private float startScale = 1.2f;
        [SerializeField] private AnimationCurve animationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        private ScrollRect scrollRect;
        private RectTransform content;
        private RectTransform viewport;

        private float currentScale = 1f;
        private Coroutine entryAnimationCoroutine;

        private Vector2 contentOriginalPosition;

        private void Awake()
        {
            scrollRect = GetComponent<ScrollRect>();
            content = scrollRect.content;
            viewport = scrollRect.viewport;

            if (content != null)
            {
                contentOriginalPosition = content.anchoredPosition;
            }

            // 设置快进慢出的默认曲线
            ResetAnimationCurve();
        }

        private void Reset()
        {
            ResetAnimationCurve();
        }

        private void OnDestroy()
        {
            // 兜底：入场动画协程被销毁中断时释放本类持有的锁
            InputLocks.PopAll(this);
        }

        private void ResetAnimationCurve()
        {
            // 快进慢出曲线：开始陡峭，结束平缓
            Keyframe[] keys = new Keyframe[3];
            keys[0] = new Keyframe(0f, 0f, 0f, 2f);           // 开始时斜率大，快速进入
            keys[1] = new Keyframe(0.5f, 0.85f, 1f, 1f);      // 中间过渡
            keys[2] = new Keyframe(1f, 1f, 0f, 0f);           // 结束时斜率为0，缓慢停止

            animationCurve = new AnimationCurve(keys);
        }

        private void Start()
        {
            currentScale = 1f;

            if (enableEntryAnimation)
            {
                PlayEntryAnimation();
            }
        }

        #region 缩放逻辑

        public void OnScroll(PointerEventData eventData)
        {
            if (content == null || viewport == null) return;

            // 获取滚轮输入
            float scrollDelta = eventData.scrollDelta.y * zoomSpeed;
            if (Mathf.Approximately(scrollDelta, 0)) return;

            // 计算新的缩放值
            float newScale = Mathf.Clamp(currentScale + scrollDelta, minScale, maxScale);
            if (Mathf.Approximately(newScale, currentScale)) return;

            // 获取鼠标在屏幕上的位置
            Vector2 screenPoint = eventData.position;

            // 执行以屏幕点为中心的缩放
            ApplyZoom(newScale, screenPoint);
        }

        /// <summary>
        /// 应用缩放，以指定的屏幕点为中心
        /// </summary>
        private void ApplyZoom(float newScale, Vector2 screenPoint)
        {
            if (content == null || viewport == null) return;

            // 保存旧缩放值
            float oldScale = currentScale;
            currentScale = newScale;

            // 将屏幕点转换为 Viewport 的本地坐标
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport,
                screenPoint,
                null,
                out Vector2 localPointInViewport
            );

            // 将 Viewport 本地坐标转换为 Content 本地坐标
            Vector2 localPointInContent = content.InverseTransformPoint(viewport.TransformPoint(localPointInViewport));

            // 计算缩放前后的位置偏移
            Vector2 pivotOffset = localPointInContent * (newScale - oldScale);

            // 应用新缩放
            content.localScale = Vector3.one * newScale;

            // 调整位置，保持缩放中心点不变
            content.anchoredPosition -= pivotOffset;

            // 强制更新布局，让 ScrollRect 处理边界限制
            Canvas.ForceUpdateCanvases();

            // 清除滚动速度，防止惯性滑动干扰
            scrollRect.velocity = Vector2.zero;
        }

        #endregion

        #region 入场动画

        private void PlayEntryAnimation()
        {
            if (entryAnimationCoroutine != null)
                StopCoroutine(entryAnimationCoroutine);

            // Push 对同 (owner,reason) 去重 — 上一次动画未完成（协程被中断）也不会堆积重复锁
            InputLocks.Push(this, InputLockReason.MapEntering);
            entryAnimationCoroutine = StartCoroutine(EntryAnimationCoroutine());
        }

        private IEnumerator DelayedEntryAnimation()
        {
            yield return null; // 等待一帧
            yield return EntryAnimationCoroutine();
        }

        private IEnumerator EntryAnimationCoroutine()
        {
            // 🔑 关键修改：在动画开始时重新记录原始位置，而不是使用 Awake 中记录的值
            //Vector2 startPosition = content.anchoredPosition;
            Vector2 startPosition = contentOriginalPosition;

            // 第一步：重置到初始状态
            content.anchoredPosition = startPosition;
            currentScale = startScale;
            content.localScale = Vector3.one * startScale;

            // 🔑 等待两帧，确保 Canvas 完全布局完成
            yield return null;
            yield return null;

            // 强制更新布局
            //Canvas.ForceUpdateCanvases();

            // 找到当前屏幕中心点在 Content 上的位置
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport,
                screenCenter,
                null,
                out Vector2 viewportCenterLocal
            );

            // 记录屏幕中心在 Content 本地坐标系中的位置
            Vector2 fixedPivotPoint = content.InverseTransformPoint(viewport.TransformPoint(viewportCenterLocal));

            float elapsed = 0f;

            while (elapsed < animationDuration)
            {
                elapsed += Time.unscaledDeltaTime;

                float t = Mathf.Clamp01(elapsed / animationDuration);
                float curveValue = animationCurve.Evaluate(t);

                // 从 startScale 插值到 1
                float targetScale = Mathf.Lerp(startScale, 1f, curveValue);
                float oldScale = currentScale;
                currentScale = targetScale;

                // 使用固定的点作为缩放中心
                Vector2 pivotOffset = fixedPivotPoint * (targetScale - oldScale);

                // 应用变换
                content.localScale = Vector3.one * targetScale;
                content.anchoredPosition -= pivotOffset;

                Canvas.ForceUpdateCanvases();
                yield return null;
            }

            // 确保最终状态正确
            currentScale = 1f;
            content.localScale = Vector3.one;

            Canvas.ForceUpdateCanvases();
            scrollRect.velocity = Vector2.zero;

            entryAnimationCoroutine = null;
            InputLocks.Pop(this, InputLockReason.MapEntering);
        }


        #endregion

        #region 公共方法

        /// <summary>
        /// 重置地图到初始状态
        /// </summary>
        public void ResetMap()
        {
            if (content == null) return;

            // 停止动画
            if (entryAnimationCoroutine != null)
            {
                StopCoroutine(entryAnimationCoroutine);
                entryAnimationCoroutine = null;
                // 动画被中断 — 释放入场锁（幂等，已释放时为空操作）
                InputLocks.Pop(this, InputLockReason.MapEntering);
            }

            currentScale = 1f;
            content.localScale = Vector3.one;
            content.anchoredPosition = contentOriginalPosition;

            Canvas.ForceUpdateCanvases();
            scrollRect.velocity = Vector2.zero;
        }

        /// <summary>
        /// 重新播放入场动画
        /// </summary>
        public void ReplayEntryAnimation()
        {
            PlayEntryAnimation();
        }

        /// <summary>
        /// 入场动画是否正在播放
        /// </summary>
        public bool IsAnimating => entryAnimationCoroutine != null;

        /// <summary>
        /// 获取当前缩放值
        /// </summary>
        public float GetCurrentScale()
        {
            return currentScale;
        }

        /// <summary>
        /// 更新原始位置（切换区域后调用，使 ResetMap/入场动画使用新的 Content 位置）
        /// </summary>
        public void UpdateOriginalPosition()
        {
            if (content != null)
                contentOriginalPosition = content.anchoredPosition;
        }

        #endregion
    }
}

