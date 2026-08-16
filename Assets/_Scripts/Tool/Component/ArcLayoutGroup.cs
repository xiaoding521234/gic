using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using GIC.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
namespace GIC.Tool
{


    /// <summary>
    /// UI 弧形布局组件 - 支持圆形、扇形、椭圆弧
    /// 在编辑器中修改参数即可实时预览效果
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ArcLayoutGroup : MonoBehaviour
    {
        [Header("布局形状")]
        public LayoutShape shape = LayoutShape.Arc;
        
        [Header("弧形参数")]
        [Range(0, 360)]
        public float arcAngle = 180f;           // 圆弧角度（0-360）
        
        [Range(-360, 360)]
        public float startAngle = -90f;          // 起始角度（-90° = 12点钟方向）
        
        public float radius = 200f;              // 半径
        
        [Header("椭圆参数（可选）")]
        public bool useEllipse = false;
        public float xRadius = 200f;
        public float yRadius = 100f;
        
        [Header("间距")]
        public float spacing = 0f;               // 额外间距补偿
        public bool autoSpacing = false;         // 自动计算间距
        
        [Header("旋转")]
        public bool rotateItems = true;           // 旋转物体沿切线方向
        public float rotationOffset = 0f;         // 旋转角度偏移
        
        [Header("排序")]
        public bool reverseOrder = false;         // 反转顺序
        public ArrangementDirection direction = ArrangementDirection.Clockwise;
        
        [Header("动画")]
        public bool useAnimation = false;
        public float animationDuration = 0.5f;
        public AnimationCurve animationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        
        private RectTransform rectTransform;
        private List<RectTransform> childRects = new List<RectTransform>();
        private bool isAnimating = false;
        
        public enum LayoutShape
        {
            Arc,        // 弧形
            Circle,     // 圆形
            SemiCircle, // 半圆形
            Quarter,    // 四分之一圆
            Spiral      // 螺旋形
        }
        
        public enum ArrangementDirection
        {
            Clockwise,      // 顺时针
            CounterClockwise // 逆时针
        }
        
        void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            
            // 编辑器模式下也初始化子物体列表
    #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                RefreshChildList();
            }
    #endif
        }
        
        void Start()
        {
            // 仅在运行时自动排列，编辑器模式下通过 OnValidate 处理
            if (Application.isPlaying)
            {
                ArrangeChildren();
            }
        }
        
        /// <summary>
        /// 排列所有子物体
        /// </summary>
        public void ArrangeChildren()
        {
            RefreshChildList();
            
            if (childRects.Count == 0) return;
            
            float actualArcAngle = GetActualArcAngle();
            float angleStep = actualArcAngle / Mathf.Max(1, childRects.Count - (shape == LayoutShape.Circle ? 0 : 1));
            
            // 计算实际半径
            float actualRadius = radius;
            float actualXRadius = useEllipse ? xRadius : radius;
            float actualYRadius = useEllipse ? yRadius : radius;
            
            // 自动计算间距
            if (autoSpacing && childRects.Count > 0)
            {
                float firstChildWidth = childRects[0].rect.width;
                float totalWidth = firstChildWidth * childRects.Count;
                float circumference = 2 * Mathf.PI * actualRadius;
                spacing = (circumference / childRects.Count) - firstChildWidth;
                spacing = Mathf.Max(0, spacing);
            }
            
            for (int i = 0; i < childRects.Count; i++)
            {
                int index = reverseOrder ? childRects.Count - 1 - i : i;
                RectTransform child = childRects[index];
                if (child == null) continue;
                
                // 计算角度
                float angle = GetAngleForIndex(i, angleStep, actualArcAngle);
                float rad = angle * Mathf.Deg2Rad;
                
                // 计算位置
                Vector2 position;
                if (shape == LayoutShape.Spiral)
                {
                    float spiralRadius = actualRadius * (1 - (float)i / childRects.Count);
                    float x = Mathf.Cos(rad) * spiralRadius;
                    float y = Mathf.Sin(rad) * spiralRadius;
                    position = new Vector2(x, y);
                }
                else
                {
                    float x = Mathf.Cos(rad) * actualXRadius;
                    float y = Mathf.Sin(rad) * actualYRadius;
                    position = new Vector2(x, y);
                }
                
                // 应用动画或直接设置位置
                if (useAnimation && Application.isPlaying && !isAnimating)
                {
                    StartCoroutine(AnimatePosition(child, child.anchoredPosition, position, i));
                }
                else
                {
                    child.anchoredPosition = position;
                }
                
                // 旋转物体
                if (rotateItems)
                {
                    float rotation = angle + rotationOffset;
                    if (direction == ArrangementDirection.CounterClockwise)
                        rotation = -rotation;
                    child.localRotation = Quaternion.Euler(0, 0, rotation);
                }
            }
        }
        
        /// <summary>
        /// 获取实际圆弧角度（根据形状）
        /// </summary>
        private float GetActualArcAngle()
        {
            switch (shape)
            {
                case LayoutShape.Circle:
                    return 360f;
                case LayoutShape.SemiCircle:
                    return 180f;
                case LayoutShape.Quarter:
                    return 90f;
                case LayoutShape.Spiral:
                    return 360f * 2; // 螺旋两圈
                default:
                    return arcAngle;
            }
        }
        
        /// <summary>
        /// 获取当前索引的角度
        /// </summary>
        private float GetAngleForIndex(int index, float angleStep, float totalAngle)
        {
            float angle;
            
            if (shape == LayoutShape.Circle)
            {
                // 圆形：均匀分布
                angle = startAngle + angleStep * index;
            }
            else
            {
                // 弧形：从起点到终点
                angle = startAngle + angleStep * index;
            }
            
            // 处理方向
            if (direction == ArrangementDirection.CounterClockwise)
            {
                angle = startAngle - angleStep * index;
            }
            
            return angle;
        }
        
        /// <summary>
        /// 刷新子物体列表
        /// </summary>
        private void RefreshChildList()
        {
            childRects.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child is RectTransform rect)
                {
                    childRects.Add(rect);
                }
            }
        }
        
        /// <summary>
        /// 添加新子物体并自动排列
        /// </summary>
        public void AddChild(RectTransform child)
        {
            child.SetParent(rectTransform);
            childRects.Add(child);
            ArrangeChildren();
        }
        
        /// <summary>
        /// 移除子物体并重新排列
        /// </summary>
        public void RemoveChild(RectTransform child)
        {
            childRects.Remove(child);
            child.SetParent(null);
            ArrangeChildren();
        }
        
        /// <summary>
        /// 动态设置半径
        /// </summary>
        public void SetRadius(float newRadius)
        {
            radius = newRadius;
            ArrangeChildren();
        }
        
        /// <summary>
        /// 动态设置角度
        /// </summary>
        public void SetArcAngle(float newAngle)
        {
            arcAngle = newAngle;
            ArrangeChildren();
        }
        
        private System.Collections.IEnumerator AnimatePosition(RectTransform target, Vector2 from, Vector2 to, int index)
        {
            isAnimating = true;
            float elapsed = 0f;
            
            // 错开动画开始时间
            float delay = index * 0.05f;
            yield return Wait.Seconds(delay);
            
            while (elapsed < animationDuration)
            {
                float t = animationCurve.Evaluate(elapsed / animationDuration);
                target.anchoredPosition = Vector2.Lerp(from, to, t);
                elapsed += Time.deltaTime;
                yield return null;
            }
            
            target.anchoredPosition = to;
            isAnimating = false;
        }
        
    #if UNITY_EDITOR
        /// <summary>
        /// 在编辑器中修改参数时自动调用
        /// </summary>
        void OnValidate()
        {
            if (Application.isPlaying) return;
            
            // 确保在编辑器模式下能立即获取 RectTransform
            if (rectTransform == null)
                rectTransform = GetComponent<RectTransform>();
            
            // 使用延迟调用避免 Unity 的警告，同时保证立即响应
            if (!Application.isPlaying && this != null && rectTransform != null)
            {
                // 移除之前的延迟调用，避免重复
                UnityEditor.EditorApplication.delayCall -= ArrangeChildrenInEditor;
                UnityEditor.EditorApplication.delayCall += ArrangeChildrenInEditor;
            }
        }
        
        /// <summary>
        /// 编辑器模式下排列子物体
        /// </summary>
        private void ArrangeChildrenInEditor()
        {
            // 再次检查对象是否还存在（防止对象被删除后执行）
            if (this == null) return;
            
            // 在编辑器中强制记录撤销操作，支持 Ctrl+Z
            UnityEditor.Undo.RecordObject(this, "Arc Layout Changed");
            
            RefreshChildList();
            
            if (childRects.Count == 0) return;
            
            float actualArcAngle = GetActualArcAngle();
            float angleStep = actualArcAngle / Mathf.Max(1, childRects.Count - (shape == LayoutShape.Circle ? 0 : 1));
            
            // 计算实际半径
            float actualRadius = radius;
            float actualXRadius = useEllipse ? xRadius : radius;
            float actualYRadius = useEllipse ? yRadius : radius;
            
            // 自动计算间距
            if (autoSpacing && childRects.Count > 0)
            {
                float firstChildWidth = childRects[0].rect.width;
                spacing = Mathf.Max(0, (2 * Mathf.PI * actualRadius / childRects.Count) - firstChildWidth);
            }
            
            for (int i = 0; i < childRects.Count; i++)
            {
                int index = reverseOrder ? childRects.Count - 1 - i : i;
                RectTransform child = childRects[index];
                if (child == null) continue;
                
                // 记录子物体的变换，支持撤销
                UnityEditor.Undo.RecordObject(child, "Arc Child Moved");
                
                // 计算角度
                float angle = GetAngleForIndex(i, angleStep, actualArcAngle);
                float rad = angle * Mathf.Deg2Rad;
                
                // 计算位置
                Vector2 position;
                if (shape == LayoutShape.Spiral)
                {
                    float spiralRadius = actualRadius * (1 - (float)i / childRects.Count);
                    float x = Mathf.Cos(rad) * spiralRadius;
                    float y = Mathf.Sin(rad) * spiralRadius;
                    position = new Vector2(x, y);
                }
                else
                {
                    float x = Mathf.Cos(rad) * actualXRadius;
                    float y = Mathf.Sin(rad) * actualYRadius;
                    position = new Vector2(x, y);
                }
                
                child.anchoredPosition = position;
                
                // 旋转物体
                if (rotateItems)
                {
                    float rotation = angle + rotationOffset;
                    if (direction == ArrangementDirection.CounterClockwise)
                        rotation = -rotation;
                    child.localRotation = Quaternion.Euler(0, 0, rotation);
                }
            }
            
            // 标记场景为已修改
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
            );
        }
    #endif
    }
}


