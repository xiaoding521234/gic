using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.UI;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// 卡组管理面板（DeckSwitchPanel）的单行视图（v3 运行时实例化，预制体=Resources/Prefabs/Backpack/DeckRow.prefab）：
    /// 拖拽把手 + 编号 + 名称（点击改名）+ 8 张完整卡牌预览容器 + 复制/粘贴/导出/删除按钮 + 当前卡组高亮框。
    /// 行身份不落在行上——面板按"容器下标=显示顺序=deckOrder 下标"实时解析。
    /// 手势分工：行背景点击=选中当前卡组；拖拽把手（DeckRowDragHandle）=拖拽排序（把手同时消耗点击，不触发行选中）。
    /// 当前卡组高亮切换=shader 过渡动画（docs/18 决策五十二）：新行金色从左往右填充、旧行金色从左往右退去，
    /// 前沿双层噪声扰动非竖直线+指数扫光带（UI/DeckRowHighlight.shader）；非动画路径 SetCurrent 瞬时切换视觉恒等。
    /// **高亮框=DeckRowHighlightGraphic（自绘 Graphic）**——实证（2026-10-07）：无 sprite 的 Image 换自定义 shader
    /// 材质后渲染不上屏（A/B 像素对照皆无金，自绘 Graphic 有金）；先例 CardLightBandEffect 同样自绘绕开 Image。
    /// 预览卡牌由面板通过共享 CardPool 填充（previewRoot 只做容器），本类不持有卡牌。
    /// </summary>
    public class DeckRowView : MonoBehaviour, IPointerClickHandler
    {
        [InspectorName("编号文本")] public TextMeshProUGUI numberText;
        [InspectorName("名称文本")] public TextMeshProUGUI nameText;
        [InspectorName("改名按钮（覆盖在名称上）")] public Button nameButton;
        [InspectorName("行背景")] public Image rowBackground;
        [InspectorName("当前卡组高亮框")] public Graphic currentHighlight;
        [InspectorName("拖拽浮层（拖动时的视觉反馈）")] public Image dragOverlay;
        [InspectorName("拖拽把手")] public DeckRowDragHandle dragHandle;
        [InspectorName("卡牌预览容器")] public Transform previewRoot;
        [InspectorName("复制按钮")] public Button copyButton;
        [InspectorName("粘贴按钮")] public Button pasteButton;
        [InspectorName("导出密语按钮")] public Button exportButton;
        [InspectorName("删除按钮")] public Button deleteButton;
        [InspectorName("高亮过渡材质（shader=UI/DeckRowHighlight，动画时实例化）")] public Material highlightMaterial;

        /// <summary>所属面板（Init 时注入，按钮与把手向它转发事件）</summary>
        public DeckSwitchPanel Panel { get; private set; }

        /// <summary>显示位（=容器内下标，显示编号-1）；卡组 Id 由面板经 deckOrder 解析</summary>
        public int DisplayIndex => transform.GetSiblingIndex();

        /// <summary>当前是否处于选中高亮态（供面板捕获"切换前的旧高亮行"）</summary>
        public bool IsCurrentHighlight => currentHighlight != null && currentHighlight.gameObject.activeSelf;

        private TextCombiner _numberCombiner;
        private TextCombiner _nameCombiner;
        private Material _highlightMat;      // 高亮 shader 材质实例（per-row 进度互不干扰；行销毁时释放）
        private Coroutine _highlightAnim;
        private const float ProgressFull = 1.5f; // 稳态满格（前沿远出右缘=纯实底、扫光自然归零）

        public void Init(DeckSwitchPanel panel)
        {
            Panel = panel;

            nameButton?.onClick.AddListener(() => Panel?.OnRenameClicked(this));
            copyButton?.onClick.AddListener(() => Panel?.OnCopyClicked(this));
            pasteButton?.onClick.AddListener(() => Panel?.OnPasteClicked(this));
            exportButton?.onClick.AddListener(() => Panel?.OnExportClicked(this));
            deleteButton?.onClick.AddListener(() => Panel?.OnDeleteClicked(this));
            dragHandle?.Init(this);
        }

        // ==================== 刷新（由面板驱动） ====================

        /// <summary>显示编号（拖拽/重排/增删后调用）</summary>
        public void SetNumber(int displayNumber)
        {
            EnsureCombiners();
            _numberCombiner.SetSingleEntry(displayNumber.ToString());
        }

        /// <summary>显示名称：自定义名优先，空 = 本地化默认"卡组N"</summary>
        public void SetName(string customName, LocalizedString defaultName)
        {
            EnsureCombiners();
            if (!string.IsNullOrEmpty(customName))
                _nameCombiner.SetSingleEntry(customName);
            else
                _nameCombiner.SetSingleEntry(defaultName);
        }

        /// <summary>当前卡组高亮（瞬时显隐切换，样式在预制体里调）；动画播放中不覆盖材质进度</summary>
        public void SetCurrent(bool isCurrent)
        {
            if (currentHighlight == null) return;
            currentHighlight.gameObject.SetActive(isCurrent);
            if (_highlightAnim == null && _highlightMat != null)
                _highlightMat.SetFloat("_Progress", isCurrent ? ProgressFull : 0f);
        }

        // ==================== 高亮过渡动画（docs/18 决策五十二：金色从左往右填充/退去） ====================

        /// <summary>新选中行：金色从左往右填充（前沿噪声扰动+扫光），播完留稳态满格实底</summary>
        public void PlayHighlightFill(float duration)
        {
            PlayHighlightProgress(erase: false, duration, hideOnFinish: false);
        }

        /// <summary>取消选中行：金色从左往右退去（前沿噪声扰动+扫光），播完隐藏高亮框。
        /// 调用方保证该行切换前处于高亮态——RefreshAllRows 的瞬时 SetCurrent(false) 可能已先关掉高亮框，此处重开从全金起退，勿加 activeSelf 守卫拦截</summary>
        public void PlayHighlightRetreat(float duration)
        {
            PlayHighlightProgress(erase: true, duration, hideOnFinish: true);
        }

        private void PlayHighlightProgress(bool erase, float duration, bool hideOnFinish)
        {
            if (currentHighlight == null || highlightMaterial == null) return;
            if (_highlightAnim != null) StopCoroutine(_highlightAnim);

            var mat = EnsureHighlightMaterial();
            currentHighlight.gameObject.SetActive(true); // 旧行被瞬时 SetCurrent(false) 关过：retreat 要重开从全金起退
            mat.SetFloat("_Erase", erase ? 1f : 0f);
            mat.SetFloat("_Seed", Random.value * 100f); // 每次播报边界形状各异，避免机械重复感
            _highlightAnim = StartCoroutine(HighlightProgressRoutine(mat, duration, hideOnFinish));
        }

        /// <summary>驱动 _Progress 0→(1+噪声幅度)：线性扫过（unscaled——弹窗在游戏暂停下也播）</summary>
        private IEnumerator HighlightProgressRoutine(Material mat, float duration, bool hideOnFinish)
        {
            const float progressEnd = 1.2f; // >1+噪声最大负向幅度：保证前沿扫过全行
            mat.SetFloat("_Progress", 0f);
            if (duration <= 0f) duration = 0.01f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                mat.SetFloat("_Progress", Mathf.Lerp(0f, progressEnd, Mathf.Clamp01(t / duration)));
                yield return null;
            }
            _highlightAnim = null;
            if (hideOnFinish)
            {
                currentHighlight.gameObject.SetActive(false);
                mat.SetFloat("_Progress", 0f);
            }
            else
            {
                mat.SetFloat("_Progress", ProgressFull);
            }
        }

        private Material EnsureHighlightMaterial()
        {
            if (_highlightMat == null)
            {
                _highlightMat = Instantiate(highlightMaterial);
                currentHighlight.material = _highlightMat;
            }
            return _highlightMat;
        }

        /// <summary>拖拽中视觉反馈（浮层显隐；置顶换序由面板负责）</summary>
        public void SetDragState(bool dragging)
        {
            if (dragOverlay != null)
                dragOverlay.gameObject.SetActive(dragging);
        }

        // ==================== 手势（点击选中；拖拽排序走把手转发面板） ====================

        /// <summary>行背景点击 = 选中该卡组为当前卡组（面板保持打开）；把手/按钮区域的点击不会到达这里</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            Panel?.OnRowSelected(this);
        }

        public void OnDestroy()
        {
            // 实例化行随 RebuildRows 销毁：只清自身监听（面板侧列表随重建同步）
            nameButton?.onClick.RemoveAllListeners();
            copyButton?.onClick.RemoveAllListeners();
            pasteButton?.onClick.RemoveAllListeners();
            exportButton?.onClick.RemoveAllListeners();
            deleteButton?.onClick.RemoveAllListeners();
            if (_highlightMat != null) Destroy(_highlightMat); // 材质实例随行释放防泄漏
        }

        private void EnsureCombiners()
        {
            if (_numberCombiner == null && numberText != null)
                _numberCombiner = numberText.GetComponent<TextCombiner>() ?? numberText.gameObject.AddComponent<TextCombiner>();
            if (_nameCombiner == null && nameText != null)
                _nameCombiner = nameText.GetComponent<TextCombiner>() ?? nameText.gameObject.AddComponent<TextCombiner>();
        }
    }
}
