using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;

namespace GIC.Battle
{
    /// <summary>
    /// 执行阶段多行行动预览（2026-09-29 拍板落地，取代退役的 TopBar 攻速队列条）：
    /// 执行阶段开始（相位 Resolving + 行动预告 TurnPlan 到达）→ 中下方居中显示「谁将按什么顺序行动」。
    /// **行=攻速 10 点区间**（0~9/10~19/…降序）——同区间全部单位横排一行（Host 仍按精确攻速分片结算，
    /// 预览区间分组是 HUD 侧归组，两套互不干扰）；行最左/最右显示区间下界/上界（13、17 两个单位同行
    /// 左 10 右 19，拍板②+③）。稳态两行（拍板⑤）：当前行（下一行动，正常大小）+即将行（下方更小更暗）。
    /// 行进：攻速行动片开始播放（OnSegmentPlaying）命中当前行区间 → 停留 → 淡出上移出场 →
    /// 下方行丝滑上移补位 → 新即将行底部淡入；同区间后续片不再触发（行已离场）。
    /// 单位块=PreviewEntry.prefab（内嵌 QueueSlot 头像牌+右侧技能图标）；头像描环按**归属玩家**染色
    /// （拍板④：BattlePlayerColors 本端视角蓝/绿/红/紫）。**行底=半透明胶囊黑背景**（2026-09-29 追拍：
    /// 复用 ItemCounterChip 的 primogem_capsule_bg Sliced (0,0,0,0.5)——行 prefab BG 节点）。
    /// 全部视觉=prefab（拍板「勿程序化拼接」——观感改 prefab 勿改代码），组件只建行实例+填数据+驱动动画
    /// （BattleViewTween 补间，末帧保证 t=1）。部署段/回合结束段/即时行动块不驱动行进（事件侧已过滤）。
    /// </summary>
    public class BattleExecutionPreview : MonoBehaviour
    {
        [Header("Prefab 引用（视觉契约——改观感改 prefab 勿改代码）")]
        [Tooltip("行预制体（ExecutionPreviewRow：HorizontalLayoutGroup+BandMin/BandMax 区间标签）")]
        [SerializeField] private RectTransform 行预制体;
        [Tooltip("单位条目预制体（PreviewEntry：内嵌 QueueSlot 头像牌+右侧技能图标）")]
        [SerializeField] private RectTransform 条目预制体;
        [Tooltip("行挂载容器（Rows）")]
        [SerializeField] private RectTransform 行容器;

        [Header("布局")]
        [SerializeField] private float 行间距 = 88f;
        [SerializeField] private float 即将行缩放 = 0.8f;
        [SerializeField, Range(0f, 1f)] private float 即将行暗度 = 0.55f;

        [Header("动画节奏（秒）")]
        [SerializeField] private float 入场时长 = 0.35f;
        [Tooltip("行开始行动后停留多久再出场（2026-09-29 追拍「描环也不用转金」后=纯停留；描环玩家色全程保持）")]
        [UnityEngine.Serialization.FormerlySerializedAs("高亮停留时长")]
        [SerializeField] private float 停留时长 = 0.4f;
        [SerializeField] private float 出场时长 = 0.35f;
        [SerializeField] private float 行进时长 = 0.3f;
        [SerializeField] private float 收场时长 = 0.25f;

        /// <summary>单位配置（DI 容器注入——Init 时自注入，与 BattlePlayer.Bind 同模式；只在
        /// OnTurnPlan 建行时消费，注入时序天然安全）</summary>
        [Autowired] private UnitConfig _unitConfig;

        private BattleSession _session;
        private string _myPlayerId;
        private bool _shown;
        private Coroutine _dismissRoutine;

        private class PreviewRow
        {
            public RectTransform root;
            public CanvasGroup group;
            public int bandLow;                                   // 区间下界（floor(攻速/10)×10）
            public int entryCount;                                // 条目数（0=空行不建；描环转金已随 2026-09-29 追拍退役，无逐条目视觉引用）
            public Coroutine advanceRoutine;                      // 停留→出场协程（防重入）
            public Coroutine tweenRoutine;                        // 槽位补间协程（新补间前先停）
        }

        /// <summary>装配（Bind 链调用；自注入后订阅三事件，OnDestroy 对称退订）</summary>
        public void Init(BattleSession session, string myPlayerId)
        {
            _session = session;
            _myPlayerId = myPlayerId;
            Wargame.Instance?.Context?.Inject(this); // [Autowired] UnitConfig（BattlePlayer.Bind 同模式）
            _session.Flow.OnPhaseChanged += OnPhaseChanged;
            _session.Player.TurnPlanUpdated += OnTurnPlan;
            _session.Player.OnSegmentPlaying += OnSegmentPlaying;
        }

        private void OnDestroy()
        {
            if (_session == null) return;
            if (_session.Flow != null) _session.Flow.OnPhaseChanged -= OnPhaseChanged;
            if (_session.Player != null)
            {
                _session.Player.TurnPlanUpdated -= OnTurnPlan;
                _session.Player.OnSegmentPlaying -= OnSegmentPlaying;
            }
        }

        // ==================== 事件驱动 ====================

        /// <summary>相位离开执行（回选择/战斗结束）→ 收场淡出（执行开始由 TurnPlan 侧触发显示）</summary>
        private void OnPhaseChanged(BattlePhase phase, int turn)
        {
            if (phase == BattlePhase.Resolving) return;
            Dismiss();
        }

        /// <summary>行动预告到达（执行阶段开始）：按攻速 10 点区间建行 + 入场</summary>
        private void OnTurnPlan(TurnPlanMessage message)
        {
            if (_session == null || _session.Flow == null) return;
            if (_session.Flow.Phase != BattlePhase.Resolving) return; // 相位先行、预告后到（Host 下发序）
            if (行预制体 == null || 条目预制体 == null || 行容器 == null)
            {
                GICLog.Warn("[ExecutionPreview] prefab 引用缺失（行/条目/容器）——执行预览不显示");
                return;
            }
            if (_dismissRoutine != null) { StopCoroutine(_dismissRoutine); _dismissRoutine = null; }
            Build(message);
            if (_rows.Count > 0) Show();
        }

        /// <summary>攻速行动片开始播放：命中当前行区间 → 行进（停留→淡出出场→补位）。
        /// 快片连发防积压：先收敛进行中的行进（Flush），再判定新区间——防吞下一区间触发</summary>
        private void OnSegmentPlaying(int sliceAttackSpeed)
        {
            if (!_shown || _rows.Count == 0) return;
            FlushAdvance();
            if (_rows.Count == 0) return;
            int band = BandLowOf(sliceAttackSpeed);
            if (_rows[0].bandLow != band) return; // 同区间后续片（首片已触发行进，行不在场）
            var row = _rows[0];
            row.advanceRoutine = StartCoroutine(AdvanceRoutine(row));
        }

        // ==================== 建行 ====================

        private readonly List<PreviewRow> _rows = new List<PreviewRow>();

        private static int BandLowOf(int attackSpeed) => Mathf.FloorToInt(attackSpeed / 10f) * 10;

        private void Build(TurnPlanMessage message)
        {
            ClearRows();
            var groups = message.entries
                .GroupBy(e => BandLowOf(e.attackSpeed))
                .OrderByDescending(g => g.Key); // 攻速降序=行动顺序
            foreach (var g in groups)
            {
                var rowGo = Instantiate(行预制体.gameObject, 行容器, false);
                rowGo.name = $"PreviewRow_{g.Key}_{g.Key + 9}";
                var rt = (RectTransform)rowGo.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); // 行容器中心锚——横向居中
                rt.pivot = new Vector2(0.5f, 0.5f);
                var row = new PreviewRow
                {
                    root = rt,
                    group = rowGo.GetComponent<CanvasGroup>(),
                    bandLow = g.Key,
                };
                var bandMin = rt.Find("BandMin")?.GetComponent<TMP_Text>();
                var bandMax = rt.Find("BandMax")?.GetComponent<TMP_Text>();
                if (bandMin != null) bandMin.text = g.Key.ToString();
                if (bandMax != null) bandMax.text = (g.Key + 9).ToString();
                if (row.group == null) GICLog.Warn("[ExecutionPreview] 行预制体缺 CanvasGroup");

                foreach (var e in g)
                {
                    if (CreateEntry(rt, e)) row.entryCount++;
                }
                if (row.entryCount == 0) { Destroy(rowGo); continue; } // 空行不建（条目全失败）
                _rows.Add(row);
            }
        }

        /// <summary>单位条目：QueueSlot 头像牌（头像/描环玩家色）+右侧技能图标（2026-09-29 追拍：
        /// 头像下攻速数字退役——区间界已由行两端标签表达，QueueSlot.prefab Speed 节点已删）。
        /// 条目插在 BandMax 之前（HLG 顺序=BandMin, 条目…, BandMax）。返回 false=数据全缺（条目隐藏）</summary>
        private bool CreateEntry(RectTransform rowRt, TurnPlanEntry e)
        {
            var go = Instantiate(条目预制体.gameObject, rowRt, false);
            var rt = (RectTransform)go.transform;
            rt.SetSiblingIndex(rowRt.childCount - 2); // 排在 BandMax 前
            var ring = rt.Find("Slot/Ring")?.GetComponent<Image>();
            var avatar = rt.Find("Slot/AvatarMask/Avatar")?.GetComponent<Image>();
            var icon = rt.Find("SkillIcon")?.GetComponent<Image>();

            // 头像/名字走快照 → UnitConfig；攻速只作分区间用（BandLowOf），块上不再显示数字
            var unit = _session.Player.LatestSnapshot?.units.FirstOrDefault(u => u.unitId == e.unitId);
            var data = unit != null && _unitConfig != null
                && Enum.TryParse<UnitName>(unit.unitName, out var name)
                && _unitConfig.TryGetUnitData(name, out var d) ? d : null;
            if (data == null)
            {
                Destroy(go); // 快照无此单位/配置缺失=无效条目，勿留空牌
                return false;
            }
            if (avatar != null) avatar.sprite = data.avatar;
            if (icon != null) icon.sprite = ResolveSkillIcon(data, e);
            if (ring != null)
            {
                var baseColor = BattlePlayerColors.Resolve(_session.Sim, _myPlayerId, e.playerId);
                ring.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.8f);
            }
            return true;
        }

        /// <summary>技能图标解析：skillIndex 优先（上交语义同源）；漂移/越界兜底按 actionType 分拣
        /// （移动=Move 技能/战技=Normal/爆发=Burst）</summary>
        private static Sprite ResolveSkillIcon(UnitConfig.UnitData data, TurnPlanEntry e)
        {
            if (data?.skills == null || data.skills.Count == 0) return null;
            var type = (ActionType)e.actionType;
            var skill = e.skillIndex >= 0 && e.skillIndex < data.skills.Count
                ? data.skills[e.skillIndex]?.data : null;
            if (skill == null)
            {
                var want = type == ActionType.Move ? SkillType.Move
                    : type == ActionType.Burst ? SkillType.Burst : SkillType.Normal;
                skill = data.skills.FirstOrDefault(s => s != null && s.data.skillType == want)?.data;
            }
            return skill?.icon;
        }

        // ==================== 行进动画 ====================

        private float SlotY(int index) => 行间距 * (0.5f - index);
        private float SlotAlpha(int index) => index == 0 ? 1f : (index == 1 ? 即将行暗度 : 0f);
        private float SlotScale(int index) => index == 0 ? 1f : 即将行缩放;

        private void ApplyRowState(PreviewRow row, Vector2 pos, float scale, float alpha)
        {
            if (row.root == null) return;
            row.root.anchoredPosition = pos;
            row.root.localScale = new Vector3(scale, scale, 1f);
            if (row.group != null) row.group.alpha = alpha;
        }

        private void StartRowTween(PreviewRow row, Vector2 pos, float scale, float alpha, float duration)
        {
            if (row.tweenRoutine != null) StopCoroutine(row.tweenRoutine);
            row.tweenRoutine = StartCoroutine(TweenRowRoutine(row, pos, scale, alpha, duration));
        }

        private IEnumerator TweenRowRoutine(PreviewRow row, Vector2 pos, float scale, float alpha, float duration)
        {
            if (duration <= 0f)
            {
                ApplyRowState(row, pos, scale, alpha);
                row.tweenRoutine = null;
                yield break;
            }
            var rt = row.root;
            var fromPos = rt.anchoredPosition;
            float fromScale = rt.localScale.x;
            float fromAlpha = row.group != null ? row.group.alpha : 1f;
            yield return BattleViewTween.Over(duration, t =>
            {
                float e = EaseOutCubic(t);
                if (rt == null) return;
                rt.anchoredPosition = Vector2.LerpUnclamped(fromPos, pos, e);
                var s = Mathf.LerpUnclamped(fromScale, scale, e);
                rt.localScale = new Vector3(s, s, 1f);
                if (row.group != null) row.group.alpha = Mathf.LerpUnclamped(fromAlpha, alpha, e);
            });
            row.tweenRoutine = null;
        }

        /// <summary>入场：当前/即将行自下方淡入，预备行（≥2）隐藏待命</summary>
        private void Show()
        {
            _shown = true;
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var target = new Vector2(0f, SlotY(i));
                if (i <= 1)
                {
                    ApplyRowState(row, target + Vector2.down * 18f, SlotScale(i), 0f);
                    StartRowTween(row, target, SlotScale(i), SlotAlpha(i), 入场时长);
                }
                else ApplyRowState(row, target, SlotScale(i), 0f);
            }
        }

        /// <summary>行进：当前行停留（片在板面同步演出）→ 淡出上移出场 → 删行 → 下方行补位。
        /// 描环不转金（2026-09-29 追拍「描环也不用转金」——玩家色描环全程保持，行离场即反馈）</summary>
        private IEnumerator AdvanceRoutine(PreviewRow row)
        {
            // ① 停留（行动在板面同步演出——行停留片刻再离场，玩家色描环保持不变）
            if (row.group != null) row.group.alpha = 1f;
            if (停留时长 > 0f) yield return new WaitForSeconds(停留时长);
            if (!_rows.Contains(row)) yield break; // 收场竞态（Dismiss 已清）

            // ② 淡出上移出场
            if (row.tweenRoutine != null) { StopCoroutine(row.tweenRoutine); row.tweenRoutine = null; }
            var rt = row.root;
            var exitFrom = rt.anchoredPosition;
            var exitTo = exitFrom + Vector2.up * (行间距 * 0.55f);
            yield return BattleViewTween.Over(出场时长, t =>
            {
                float e = EaseOutCubic(t);
                if (rt == null) return;
                rt.anchoredPosition = Vector2.LerpUnclamped(exitFrom, exitTo, e);
                if (row.group != null) row.group.alpha = Mathf.LerpUnclamped(1f, 0f, e);
            });

            // ③ 删行 + 补位（新即将行随补位淡入）
            RemoveRow(row);
            ShiftRows(行进时长);
            if (_rows.Count == 0) _shown = false; // 全部行播完——预览自然清空（下回合重 Build）
        }

        private void ShiftRows(float duration)
        {
            for (int i = 0; i < _rows.Count; i++)
                StartRowTween(_rows[i], new Vector2(0f, SlotY(i)), SlotScale(i), SlotAlpha(i), duration);
        }

        /// <summary>收敛进行中的行进（快片连发防积压/防吞下一区间触发）：立即删行+全行快照到槽位。
        /// 无进行中行进则不动——勿打断入场/补位补间（首片快到时入场动画照常走完）</summary>
        private void FlushAdvance()
        {
            var advancing = _rows.FirstOrDefault(r => r.advanceRoutine != null);
            if (advancing == null) return;
            if (advancing.advanceRoutine != null) StopCoroutine(advancing.advanceRoutine);
            RemoveRow(advancing);
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.tweenRoutine != null) { StopCoroutine(row.tweenRoutine); row.tweenRoutine = null; }
                ApplyRowState(row, new Vector2(0f, SlotY(i)), SlotScale(i), SlotAlpha(i));
            }
            if (_rows.Count == 0) _shown = false;
        }

        private void RemoveRow(PreviewRow row)
        {
            _rows.Remove(row);
            if (row.root != null) Destroy(row.root.gameObject);
        }

        // ==================== 收场 ====================

        /// <summary>相位离开执行/战斗结束：全行淡出清场（不挡胜负 Tip——纯 UI 层）</summary>
        private void Dismiss()
        {
            if (!_shown && _rows.Count == 0) return;
            _shown = false;
            foreach (var row in _rows)
            {
                if (row.advanceRoutine != null) { StopCoroutine(row.advanceRoutine); row.advanceRoutine = null; }
                if (row.tweenRoutine != null) { StopCoroutine(row.tweenRoutine); row.tweenRoutine = null; }
            }
            if (_dismissRoutine != null) StopCoroutine(_dismissRoutine);
            _dismissRoutine = StartCoroutine(DismissRoutine(收场时长));
        }

        private IEnumerator DismissRoutine(float duration)
        {
            var rows = _rows.ToList();
            var fromAlphas = rows.Select(r => r.group != null ? r.group.alpha : 1f).ToList();
            yield return BattleViewTween.Over(duration, t =>
            {
                float e = EaseOutCubic(t);
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i].group != null)
                        rows[i].group.alpha = Mathf.LerpUnclamped(fromAlphas[i], 0f, e);
            });
            ClearRows();
            _dismissRoutine = null;
        }

        private void ClearRows()
        {
            foreach (var row in _rows)
            {
                if (row.advanceRoutine != null) StopCoroutine(row.advanceRoutine);
                if (row.tweenRoutine != null) StopCoroutine(row.tweenRoutine);
                if (row.root != null) Destroy(row.root.gameObject);
            }
            _rows.Clear();
        }

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    }
}
