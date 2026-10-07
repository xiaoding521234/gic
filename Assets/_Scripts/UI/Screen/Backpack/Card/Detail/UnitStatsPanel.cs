using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using GIC.Data;
using GIC.Framework;
using GIC.Tool;
namespace GIC.UI
{


    /// <summary>
    /// 全部数据面板 — 全屏暗遮罩（垂直渐变）+ 分区斑马纹行列表（原神样式；无问号按钮、无加成拆分值）
    /// 由 UnitDetailPanel 打开。关闭三路：点数据块之外的遮罩（根 Button）/右键/ESC（IClosable 栈）
    /// 入场退场动画参考 DeckSwitchPanel：CanvasGroup 淡入淡出 + 行逐个上滑入场 + 入场期输入锁
    /// </summary>
    public class UnitStatsPanel : MonoBehaviour, IClosable
    {
        public ScrollRect 滚动区;
        public StatRowView[] 行列表; // 15 行：基础（生命/攻击/防御/元能上限/精通/幸运/理智）+ 进阶（攻速/移速/韧性/治疗效率/吸血）+ 战斗（部署消耗/攻击视野/迷雾视野）

        // 行图标（原神官方属性图标，2026-10-07 用户指定映射）；无官方对应项=null 槽位留空
        private static readonly string[] 行图标 =
        {
            "stat_hp", "stat_attack", "stat_defense", "stat_energy", "stat_mastery", "stat_luck", "stat_sanity",
            "stat_atkspeed", "stat_movespeed", "stat_tenacity", "stat_heal", "stat_lifesteal",
            null, null, null,
        };

        [Header("行底斑马纹")]
        [SerializeField, Range(0f, 1f)] private float 明条不透明度 = 0.75f;
        [SerializeField, Range(0f, 1f)] private float 暗条不透明度 = 0.40f;

        [Header("入场退场动画")]
        [InspectorName("淡入时长")] [SerializeField] private float 淡入时长 = 0.2f;
        [InspectorName("淡出时长")] [SerializeField] private float 淡出时长 = 0.2f;
        [InspectorName("行入场间隔（逐个出现）")] [SerializeField] private float 行入场间隔 = 0.028f;
        [InspectorName("行入场时长")] [SerializeField] private float 行入场时长 = 0.18f;
        [InspectorName("行入场上滑距离")] [SerializeField] private float 行入场上滑距离 = 30f;

        private UnitConfig.UnitData _raw;
        private CanvasGroup _canvasGroup;
        private Button _backdropButton;
        private Coroutine _fadeCoroutine;
        private Coroutine _entranceCoroutine;
        private bool _open;

        [Autowired] private InputManager inputManager;

        // 行入场快照（关闭/重开时立即收尾复位用）
        private RectTransform[] _rowRects;
        private CanvasGroup[] _rowGroups;
        private Vector2[] _rowTargets;

        private void Awake()
        {
            Wargame.Instance?.Context?.Inject(this);

            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            // 点数据块之外（遮罩本体）关闭：根 Button 与遮罩 Image 同体，行/视口内的点击命中子级射线不冒泡到根
            _backdropButton = GetComponent<Button>();
            if (_backdropButton == null) _backdropButton = gameObject.AddComponent<Button>();
            _backdropButton.transition = Selectable.Transition.None;
            _backdropButton.onClick.AddListener(Close);
        }

        private void OnDestroy()
        {
            // 兜底：协程被销毁中断时释放本面板持有的锁
            InputLocks.PopAll(this);
        }

        public void Open(UnitConfig.UnitData raw)
        {
            _raw = raw;
            if (_open)
            {
                RefreshRows(); // 已开时重复点击只刷新数据
                return;
            }
            _open = true;

            gameObject.SetActive(true);
            RefreshRows();
            // 本帧布局还没跑：立即强排再快照行位，否则入场目标抓到 prefab 序列化位（(0,0)）→ 收尾把行全叠死
            if (滚动区 != null && 滚动区.content != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)滚动区.content);
            PlayRowsEntrance();

            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;

            inputManager?.RegisterClosable(this); // ESC/右键关闭（IClosable 栈）
            InputLocks.Push(this, InputLockReason.PopupEntering);

            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeCoroutine(true));
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;

            StopEntrance();
            inputManager?.UnregisterClosable(this);
            // 淡入被打断时补释放（Pop 幂等，正常路径重复调用无害）
            InputLocks.Pop(this, InputLockReason.PopupEntering);

            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeCoroutine(false));
        }

        private IEnumerator FadeCoroutine(bool fadeIn)
        {
            float duration = fadeIn ? 淡入时长 : 淡出时长;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _canvasGroup.alpha = fadeIn ? elapsed / duration : 1f - (elapsed / duration);
                yield return null;
            }
            _canvasGroup.alpha = fadeIn ? 1f : 0f;
            _fadeCoroutine = null;

            if (fadeIn)
                InputLocks.Pop(this, InputLockReason.PopupEntering);
            else
                gameObject.SetActive(false);
        }

        // ==================== 行入场（逐个淡入+上滑，同卡组面板节奏） ====================

        private void PlayRowsEntrance()
        {
            if (行列表 == null || 行列表.Length == 0) return;

            if (_entranceCoroutine != null) StopCoroutine(_entranceCoroutine);

            // 快照布局位 → 全部压到底部半透明 → 依次浮回
            int n = 行列表.Length;
            if (_rowRects == null || _rowRects.Length != n)
            {
                _rowRects = new RectTransform[n];
                _rowGroups = new CanvasGroup[n];
                _rowTargets = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    _rowRects[i] = (RectTransform)行列表[i].transform;
                    _rowGroups[i] = 行列表[i].GetComponent<CanvasGroup>();
                    if (_rowGroups[i] == null) _rowGroups[i] = 行列表[i].gameObject.AddComponent<CanvasGroup>();
                }
            }
            for (int i = 0; i < n; i++)
            {
                _rowTargets[i] = _rowRects[i].anchoredPosition;
                _rowGroups[i].alpha = 0f;
                _rowRects[i].anchoredPosition = _rowTargets[i] + Vector2.down * 行入场上滑距离;
            }

            _entranceCoroutine = StartCoroutine(EntranceCoroutine());
        }

        private IEnumerator EntranceCoroutine()
        {
            int n = 行列表.Length;
            float total = 行入场间隔 * (n - 1) + 行入场时长;
            float t = 0f;
            while (t < total)
            {
                t += Time.deltaTime;
                for (int i = 0; i < n; i++)
                {
                    float local = Mathf.Clamp01((t - i * 行入场间隔) / 行入场时长);
                    _rowGroups[i].alpha = local;
                    _rowRects[i].anchoredPosition = Vector2.Lerp(_rowTargets[i] + Vector2.down * 行入场上滑距离, _rowTargets[i], local);
                }
                yield return null;
            }
            FinishEntrance();
        }

        /// <summary>入场收尾：全部行精确复位（也供关闭时立即打断用）</summary>
        private void StopEntrance()
        {
            if (_entranceCoroutine != null)
            {
                StopCoroutine(_entranceCoroutine);
                _entranceCoroutine = null;
            }
            FinishEntrance();
        }

        private void FinishEntrance()
        {
            if (_rowRects == null) return;
            for (int i = 0; i < _rowRects.Length; i++)
            {
                if (_rowRects[i] == null) continue;
                _rowGroups[i].alpha = 1f;
                _rowRects[i].anchoredPosition = _rowTargets[i];
            }
            _entranceCoroutine = null;
        }

        // ==================== 数据刷新 ====================

        private void RefreshRows()
        {
            if (_raw == null || 行列表 == null || 行列表.Length < 15) return;

            行列表[0].SetValue($"{_raw.GetEffectiveHP():N0}");
            行列表[1].SetValue($"{_raw.GetEffectiveAttack():N0}");
            行列表[2].SetValue($"{_raw.GetEffectiveDefense():N0}");
            行列表[3].SetValue($"{_raw.GetEffectiveEnergy():N0}"); // 元能上限（2026-10-07 用户拍板移入基础段防御力之后）
            行列表[4].SetValue($"{_raw.GetEffectiveMastery():N0}");
            行列表[5].SetValue($"{_raw.GetEffectiveLuck():N0}"); // 幸运/理智=平值（非百分比）
            行列表[6].SetValue($"{_raw.GetEffectiveSanity():N0}");
            行列表[7].SetValue($"{_raw.GetEffectiveAttackSpeed():N0}");
            行列表[8].SetValue($"{_raw.GetEffectiveMoveSpeed():N0}");
            行列表[9].SetValue($"{_raw.GetEffectiveTenacity():N0}");
            行列表[10].SetValue($"{_raw.GetEffectiveHealEfficiency():N0}%");
            行列表[11].SetValue($"{_raw.GetEffectiveLifeSteal():N0}%");
            行列表[12].SetValue($"{_raw.GetEffectiveDeployCost():N0}");
            行列表[13].SetValue($"{_raw.GetEffectiveAttackVision():N0}");
            行列表[14].SetValue($"{_raw.GetEffectiveFogVision():N0}");

            // 行图标=原神官方属性图标；行底圆角条明暗相间（斑马纹）
            for (int i = 0; i < 行列表.Length; i++)
            {
                行列表[i].SetIcon(i < 行图标.Length ? StatRowView.LoadStatIcon(行图标[i]) : null);
                行列表[i].SetStripAlpha(i % 2 == 0 ? 明条不透明度 : 暗条不透明度);
            }
        }
    }


}
