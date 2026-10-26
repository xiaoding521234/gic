using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GIC.Data;

namespace GIC.Battle
{
    /// <summary>
    /// 底部进度线分件（2026-10-26 拍板「最底部加一条进度线，参考祈愿界面，不再需要显示倒计时」）：
    /// 贴画布底缘的全宽细线（底轨=半透明黑，填充=暖金）——
    /// · 选择阶段：填充=剩余时间/本回合时限，随倒计时**逐渐缩短**（超时自动确认链路不变）；
    ///   剩余低于告急秒数=填充转伤害红+不透明度闪烁（原倒计时「红色+字号脉动」告急的进度线形态）。
    /// · 执行阶段：填充=已播片数/本回合预计片数，随片播放**逐渐变长**（回合末片播完恰满格——
    ///   下一回合选择阶段又从满格起步收缩，相位循环无缝衔接）。
    /// 样式参考祈愿界面倒计时条（WishScreen CountdownBar：居中 pivot 填充按 localScale.x 缩放=两端对称收放）。
    ///
    /// 退役链（同日拍板）：顶部倒计时数字槽（Slots/countdown+CountdownText）随本件整体退役——
    /// prefab 槽已删、AllLayoutKeys 清键、TopBar 接线与 UpdateCountdownUrgency 删除（queue 槽退役同款配方，
    /// 存量布局方案旧 countdown 键自然跳过）；选择时限逻辑（TurnFlowController 表+超时自动完成选择）不动。
    ///
    /// 片数估计口径（执行阶段分母）：TurnPlan=分桶后的攻速片（部署段/物品段已先行到达、回合末段恒+1）——
    /// 预告到达时 分母=已播片数+分桶数+1；片边界即时段（连携队列）会超估（填充提前满格后保持，可接受）；
    /// 购买迷你段 sliceIndex=-1 不占片序不计数。本地 host 视图与 LAN 客户端经 SegmentReceived 同源。
    /// </summary>
    public partial class BattleHud
    {
        [Header("底部进度线（2026-10-26：替代倒计时数字）")]
        [Tooltip("进度线厚度（画布单位）——贴画布底缘的全宽细线")]
        [SerializeField] private float 进度线厚度 = 14f;
        [Tooltip("底轨颜色（祈愿倒计时条同款半透明黑）")]
        [SerializeField] private Color 进度线底轨色 = new Color(0f, 0f, 0f, 0.5f);
        [Tooltip("填充不透明度（填充色=BattlePalette 暖金；告急转伤害红）")]
        [SerializeField, Range(0.1f, 1f)] private float 进度线填充不透明度 = 0.8f;
        [Tooltip("告急阈值（秒）——选择阶段剩余时间低于此值=填充变红闪烁（原倒计时告急口径 5s）")]
        [SerializeField] private float 进度线告急秒数 = 5f;
        [Tooltip("告急闪烁频率（次/秒；原倒计时脉动频率口径 1.5）")]
        [SerializeField] private float 进度线告急闪烁频率 = 1.5f;
        [Tooltip("填充分数平滑速率（1/s，越大越跟手；选择阶段连续收缩/执行阶段逐片推进的补间）")]
        [SerializeField] private float 进度线平滑速率 = 8f;

        private RectTransform _phaseLineRoot;
        private RectTransform _phaseLineFillRect;
        private UnityEngine.UI.Image _phaseLineFillImage;

        // 执行阶段片计数（SegmentReceived/TurnPlanUpdated 两事件维护；按回合自清）
        private int _phaseLineTurn = -1;
        private int _phaseLinePlayed;   // 本回合已播片数（sliceIndex>=0）
        private int _phaseLineTotal;    // 本回合预计片数（预告到达时定）
        private float _phaseLineFill;   // 当前平滑填充分数 [0,1]

        // ==================== 构建（运行时懒建，随画布销毁；非布局槽——贴底固定件无拖拽/缩放语义） ====================

        private RectTransform EnsurePhaseLine()
        {
            if (_phaseLineRoot != null) return _phaseLineRoot;
            if (_canvas == null) return null;
            var canvasRt = (RectTransform)_canvas.transform;
            var root = new GameObject("PhaseLine", typeof(RectTransform));
            root.transform.SetParent(canvasRt, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 进度线厚度);

            var rail = new GameObject("Rail", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            rail.transform.SetParent(rt, false);
            var railRt = (RectTransform)rail.transform;
            railRt.anchorMin = Vector2.zero;
            railRt.anchorMax = Vector2.one;
            railRt.offsetMin = railRt.offsetMax = Vector2.zero;
            var railImg = rail.GetComponent<UnityEngine.UI.Image>();
            railImg.color = 进度线底轨色;
            railImg.raycastTarget = false;

            // 填充=祈愿 CountdownFill 同款：全拉伸+居中 pivot，按 localScale.x 对称收放
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            fill.transform.SetParent(rt, false);
            var fillRt = (RectTransform)fill.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.pivot = new Vector2(0.5f, 0.5f);
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            _phaseLineFillImage = fill.GetComponent<UnityEngine.UI.Image>();
            _phaseLineFillImage.color = Palette.暖金;
            _phaseLineFillImage.raycastTarget = false;
            _phaseLineFillRect = fillRt;

            rt.SetSiblingIndex(0); // 画布最底——不罩任何 HUD 控件（HandPlayZone 同位，二者无重叠互不干扰）
            root.SetActive(false);
            _phaseLineRoot = rt;
            return _phaseLineRoot;
        }

        // ==================== 每帧驱动（Update 尾调用——原倒计时数字块的接替者） ====================

        private void UpdatePhaseLine()
        {
            if (_session == null) return;
            var root = EnsurePhaseLine();
            if (root == null) return;

            var phase = _session.Phase;
            bool selecting = phase == BattlePhase.Selecting && _session.SelectRemainingSeconds >= 0f;
            bool resolving = phase == BattlePhase.Resolving;
            bool show = selecting || resolving;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show) return;

            float target;
            bool urgent = false;
            if (selecting)
            {
                // 分数=剩余/本回合时限（时限表双端同源静态查表——快照回合号即当前回合）
                float remaining = _session.SelectRemainingSeconds;
                int turn = _session.Player != null && _session.Player.LatestSnapshot != null
                    ? _session.Player.LatestSnapshot.turnNumber : 1;
                float limit = TurnFlowController.GetSelectLimitSeconds(Mathf.Max(1, turn));
                target = limit > 0f ? Mathf.Clamp01(remaining / limit) : 1f;
                urgent = remaining < 进度线告急秒数;
            }
            else
            {
                target = _phaseLineTotal > 0
                    ? Mathf.Clamp01(_phaseLinePlayed / (float)_phaseLineTotal) : 0f;
            }

            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
                _phaseLineFill = Mathf.Lerp(_phaseLineFill, target,
                    1f - Mathf.Exp(-dt * Mathf.Max(1f, 进度线平滑速率)));

            if (_phaseLineFillRect != null && _phaseLineFillImage != null)
            {
                _phaseLineFillRect.localScale = new Vector3(Mathf.Max(0f, _phaseLineFill), 1f, 1f);
                Color baseCol = urgent ? Palette.伤害红 : Palette.暖金;
                float alpha = 进度线填充不透明度;
                if (urgent)
                {
                    // 告急闪烁：0.45~1.0 倍基准不透明度往返（原倒计时红色脉动的进度线形态）
                    float wave = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * 进度线告急闪烁频率 * Time.unscaledTime);
                    alpha *= 0.45f + 0.55f * wave;
                }
                _phaseLineFillImage.color = new Color(baseCol.r, baseCol.g, baseCol.b, alpha);
            }
        }

        // ==================== 片计数事件（Bind 订阅 / OnDestroy 退订） ====================

        /// <summary>片到达（全类型含部署/回合末段；购买迷你段 sliceIndex=-1 不占片序跳过）——按回合自清+计数</summary>
        private void OnPhaseLineSegment(Segment segment)
        {
            if (segment == null || segment.sliceIndex < 0) return;
            ResetPhaseLineTurnIfChanged(segment.turnNumber);
            _phaseLinePlayed++;
        }

        /// <summary>行动预告到达（分桶后的攻速片）：分母=已播片数（部署/物品段）+分桶数+1（回合末段）</summary>
        private void OnPhaseLineTurnPlan(TurnPlanMessage message)
        {
            if (message == null) return;
            ResetPhaseLineTurnIfChanged(message.turnNumber);
            var speeds = new HashSet<int>();
            if (message.entries != null)
                foreach (var e in message.entries)
                    if (e != null) speeds.Add(e.attackSpeed);
            _phaseLineTotal = _phaseLinePlayed + speeds.Count + 1;
        }

        private void ResetPhaseLineTurnIfChanged(int turn)
        {
            if (_phaseLineTurn == turn) return;
            _phaseLineTurn = turn;
            _phaseLinePlayed = 0;
            _phaseLineTotal = 0;
        }
    }
}
