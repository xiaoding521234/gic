using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using GIC.Data;
using GIC.Framework;

namespace GIC.Battle
{
    /// <summary>
    /// 手牌拖拽分件（决策五十五：小丑牌式手牌打出，2026-10-07 用户拍板「当前只是点一下就算打出，非常low
    /// /手牌可以拖拽，拖拽时卡有轻微抖动，移动时卡也会有表现/可以放回到手牌堆任意位置（智能让位，
    /// 参考 DeckSwitchPanel）/拖拽时屏幕上方 75% 区域变为白色，拖入该区域松手=打出，牌留在松手位置不动
    /// /打出手牌并不意味着马上完成选择，仍可把那张手牌拖回手牌区反悔/一次只能打出一张」）。
    ///
    /// 交互总览：
    /// · 起拖仲裁——卡上拖拽不直接接管（手牌滚动壳 ScrollRect 仍要横向滑）：竖直上移越过「手牌拖拽上提阈值」
    ///   且竖向占优=提起卡（脱离滚动壳挂拖拽层）；横向越过且横移占优=转发给 ScrollRect（原滑动行为零回归）。
    /// · 提起表现——跟随指针微带平滑（快而跟手）、速度倾斜（移动方向反馈）+ 持续轻微抖动（正弦叠加）+
    ///   缩放抬起；拖入出牌区（顶部 75%，运行时白蒙层）卡再放大一档=「此处松手即打出」预告。
    /// · 手牌区（底部 25%）内拖动=实时智能让位（其他卡按插入位指数趋近让出/回填，基准槽位判定防振荡，
    ///   同 DeckSwitchPanel 纪律）；松手落位=插到该位置（显示序本地缓存，纯显示层不动协议）。
    /// · 出牌区松手=打出：卡留在松手位置悬浮→部署卡进部署瞄准（EnterDeployAim 复用既有链）/升命卡进
    ///   待确认态（EnterUpgradePending——返拍「拖入出牌区，不应该直接上交」：「完成选择」=确认上交）；
    ///   瞄准/待确认期卡仍悬浮，可再抓住拖回手牌区=反悔取消；提交成功（上交出战/升命确认）=卡同样飞回
    ///   手牌槽——**卡不消耗**（2026-10-07 返拍「不应该把我的卡销毁」，docs/01 卡可重复出战，无销毁路径）。
    /// · 一次只能拖/打一张（多点触控守卫）；物品/货币卡拖入出牌区=沿用「后续版本接入」提示并弹回手牌。
    /// </summary>
    public partial class BattleHud
    {
        // ==================== 参数（Inspector 中文段） ====================

        [Header("手牌拖拽（小丑牌式打出）")]
        [Tooltip("竖直上移多少屏幕像素判定为「提起卡」（横移占优则仍交手牌滚动）")]
        [SerializeField] private float 手牌拖拽上提阈值 = 14f;
        [Tooltip("卡提起后的缩放（相对原尺寸；手牌壳 0.7 继续成立——相对量勿写绝对值）")]
        [SerializeField] private float 手牌拖拽缩放 = 1.06f;
        [Tooltip("拖入出牌区时卡的追加缩放（相对原尺寸）")]
        [SerializeField] private float 出牌区命中缩放 = 1.08f;
        [Tooltip("出牌区下界（屏幕高度比例；上侧为出牌区=拍板「上方 75%」）")]
        [SerializeField] private float 出牌区下界比例 = 0.25f;
        [Tooltip("拖拽时出牌区白色蒙层不透明度")]
        [SerializeField] private float 出牌区不透明度 = 0.16f;
        [Tooltip("卡拖入出牌区时蒙层不透明度")]
        [SerializeField] private float 出牌区命中不透明度 = 0.3f;
        [Tooltip("移动倾斜：每（画布单位/秒）速度产生的度数")]
        [SerializeField] private float 拖拽倾斜系数 = 0.006f;
        [Tooltip("移动倾斜上限（度）")]
        [SerializeField] private float 拖拽最大倾斜角 = 10f;
        [Tooltip("拖拽中持续轻微抖动的幅度（度）")]
        [SerializeField] private float 拖拽抖动幅度 = 1.3f;
        [Tooltip("拖拽中抖动频率")]
        [SerializeField] private float 拖拽抖动频率 = 7f;
        [Tooltip("卡跟随指针的平滑速率（大=更跟手）")]
        [SerializeField] private float 拖拽跟随平滑 = 30f;
        [Tooltip("其他卡智能让位/回填的指数趋近速率")]
        [SerializeField] private float 手牌让位速度 = 14f;
        [Tooltip("松手后卡飞回手牌槽的时长")]
        [SerializeField] private float 手牌回槽时长 = 0.22f;
        [Tooltip("拖拽中指针接近手牌视口左右缘的自动滚动速度")]
        [SerializeField] private float 手牌拖拽滚动速度 = 900f;
        [Tooltip("自动滚动触发余量（屏幕像素）")]
        [SerializeField] private float 手牌拖拽滚动余量 = 60f;
        [Tooltip("点击卡的弹跳反馈缩放（打出=拖拽专属；点击仅余反馈）")]
        [SerializeField] private float 手牌点击弹跳缩放 = 1.05f;
        [Tooltip("点击弹跳时长")]
        [SerializeField] private float 手牌点击弹跳时长 = 0.16f;

        // ==================== 数据结构 ====================

        /// <summary>手牌卡槽条目（重建时随 RebuildHandCards 全量重建；列表序=显示序）</summary>
        private class HandCardSlot
        {
            public string key;        // 条目键（cardType:value，与手牌签名同形）
            public RectTransform rt;  // wrapper（点击/拖拽接收层）
            public UnityEngine.UI.Button btn;
            public bool isUnit;
            public int unitValue;     // UnitName 枚举值（isUnit 时有效）
            public Vector3 baseScale; // 手牌壳内原始 localScale（回槽还原用——壳 0.7×槽缩放）
        }

        /// <summary>一次拖拽会话状态</summary>
        private class HandCardDragState
        {
            public HandCardSlot slot;
            public RectTransform rt;
            public bool regrab;              // 打出态再拖（部署瞄准中抓回=反悔/挪位）
            public bool lifted;              // 已提起（挂拖拽层）
            public bool forwardedToScroll;  // 横移仲裁=转交 ScrollRect 滑动
            public Vector2 liftPointer;      // 提起时刻指针（屏幕 px）
            public Vector2 followAnchored;   // 提起时刻卡的 anchoredPosition（拖拽层局部）
            public Vector2 followTarget;     // 指针换算的目标位
            public Vector2 lastTarget;       // 上帧目标（速度倾斜用）
            public bool inPlayZone;          // 指针在出牌区（顶部）
            public int fromIndex;             // 提起时的显示位
            public int insertIndex;           // 当前提入位（手牌区内）
            public Vector3 baseScale;
            public float tilt;                // 当前平滑倾斜（度）
            public float liftTime;            // 提起时刻（抖动相位）
        }

        /// <summary>手牌拖拽事件转发件（wrapper 上承载——Button 为 Selectable 非 IDragHandler 宿主，
        /// 同 SkillDragForwarder 思路；转发到 BattleHud 分件方法，卡上下文经闭包捕获）</summary>
        private class HandCardDragForwarder : MonoBehaviour,
            IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public Action<PointerEventData> onBeginDrag;
            public Action<PointerEventData> onDrag;
            public Action<PointerEventData> onEndDrag;

            public void OnBeginDrag(PointerEventData e) => onBeginDrag?.Invoke(e);
            public void OnDrag(PointerEventData e) => onDrag?.Invoke(e);
            public void OnEndDrag(PointerEventData e) => onEndDrag?.Invoke(e);
        }

        // ==================== 状态字段 ====================

        /// <summary>手牌卡槽列表（显示序；RebuildHandCards 重建维护）</summary>
        private readonly List<HandCardSlot> _handCardSlots = new List<HandCardSlot>();

        /// <summary>手牌显示序缓存（条目键列表——拖拽重排纯显示层，不动协议 handCards；
        /// RebuildHandCards 按此序铺卡，签名重建后新条目按协议序追加到尾部）</summary>
        private readonly List<string> _handDisplayOrder = new List<string>();

        private HandCardDragState _handDrag;          // 进行中的拖拽（一次至多一张）
        private HandCardSlot _playedHandCard;         // 已打出悬浮中的手牌（部署瞄准/升命待确认期；上交成功/取消统一飞回手牌槽）
        private bool _upgradePending;                 // 升命卡打出待确认态（决策五十五返拍「拖入出牌区，不应该直接上交」——完成选择=确认、拖回/点空白/取消钮=反悔）
        private float _handSuppressClickUntil;        // 拖拽尾巴点击抑制（松手同帧 Button 补发 onClick）
        private RectTransform _handDragLayer;         // 拖拽浮层（画布最顶，运行时懒建）
        private RectTransform _handPlayZone;          // 出牌区白蒙层（画布最底之上，运行时懒建）
        private UnityEngine.UI.Image _handPlayZoneImage;
        private UnityEngine.UI.Image _handPlayZoneLineImage; // 下界线
        private float _handPlayZoneAlpha;             // 当前蒙层 alpha（平滑趋近）
        private Coroutine _handFlyRoutine;            // 回槽飞行（同刻至多一段）
        private HandCardSlot _handFlySlot;            // 飞行中的卡（rebuild/打断收口用）
        private Coroutine _handBounceRoutine;          // 点击弹跳反馈

        /// <summary>手牌卡规格（与 RebuildHandCards 同源；卡宽/间距改动两处一起动）</summary>
        private const float 手牌卡宽 = 160f;
        private const float 手牌卡间距 = 18f;

        /// <summary>手牌槽位公式：卡排相对 HandContent 中心对称（i=显示位序）</summary>
        private static float HandSlotX(int i, int count) => (i - (count - 1) * 0.5f) * (手牌卡宽 + 手牌卡间距);

        /// <summary>条目键（与手牌签名同形：cardType:value）</summary>
        private static string HandEntryKey(HandCard h) => h.cardType + ":" + h.value;

        // ==================== 重建接线（RebuildHandCards 消费） ====================

        /// <summary>按显示序缓存整理条目（RebuildHandCards 铺卡前调用）：缓存命中的键按缓存序在前、
        /// 新条目按协议序追加；显示序=纯本地（协议 handCards 顺序不动）</summary>
        private List<HandCard> OrderHandEntriesForDisplay(List<HandCard> entries)
        {
            var result = new List<HandCard>(entries.Count);
            if (_handDisplayOrder.Count > 0)
            {
                var byKey = new Dictionary<string, HandCard>();
                foreach (var e in entries)
                {
                    var k = HandEntryKey(e);
                    if (!byKey.ContainsKey(k)) byKey[k] = e; // 同键条目协议上不存在，防御去重
                }
                foreach (var key in _handDisplayOrder)
                    if (byKey.TryGetValue(key, out var e))
                    {
                        result.Add(e);
                        byKey.Remove(key);
                    }
                foreach (var e in byKey.Values) result.Add(e); // 剩余=新获得条目，协议序殿后
                return result;
            }
            result.AddRange(entries);
            return result;
        }

        /// <summary>重建后写回显示序缓存（=本次实际铺出的键序；顺带剪除已不存在的键）</summary>
        private void WriteBackHandDisplayOrder()
        {
            _handDisplayOrder.Clear();
            foreach (var s in _handCardSlots) _handDisplayOrder.Add(s.key);
        }

        /// <summary>卡条目接线（RebuildHandCards 每张建好的 wrapper 调）：登记槽位+挂拖拽转发件+CanvasGroup</summary>
        private void AttachHandCardInteraction(GameObject wrapperGo, string key, bool isUnit, int unitValue)
        {
            var slot = new HandCardSlot
            {
                key = key,
                rt = (RectTransform)wrapperGo.transform,
                btn = wrapperGo.GetComponent<UnityEngine.UI.Button>(),
                isUnit = isUnit,
                unitValue = unitValue,
                baseScale = wrapperGo.transform.localScale,
            };
            _handCardSlots.Add(slot);
            var fwd = wrapperGo.AddComponent<HandCardDragForwarder>();
            fwd.onBeginDrag = e => OnHandCardBeginDrag(slot, e);
            fwd.onDrag = e => OnHandCardDrag(slot, e);
            fwd.onEndDrag = e => OnHandCardEndDrag(slot, e);
            if (wrapperGo.GetComponent<CanvasGroup>() == null) wrapperGo.AddComponent<CanvasGroup>();
        }

        /// <summary>点击卡（打出=拖拽专属，点击仅余反馈）：弹跳示意「可拖拽」；物品/货币卡顺带沿用提示</summary>
        private void OnHandCardClicked(string key)
        {
            if (Time.unscaledTime < _handSuppressClickUntil) return; // 拖拽/松手尾巴点击吞掉
            HandCardSlot slot = null;
            foreach (var s in _handCardSlots)
                if (s.key == key) { slot = s; break; }
            if (slot == null || slot.rt == null) return;
            if (_handBounceRoutine != null) StopCoroutine(_handBounceRoutine);
            _handBounceRoutine = StartCoroutine(BounceHandCardRoutine(slot));
            if (!slot.isUnit) SetTip("Battle_TipItemCardPending"); // 物品卡使用后续批次接入（原点击口径保留）
        }

        /// <summary>点击弹跳：缩放脉冲一圈（sin 曲线），示意可抓取拖拽</summary>
        private IEnumerator BounceHandCardRoutine(HandCardSlot slot)
        {
            var rt = slot.rt;
            if (rt == null) yield break;
            var baseScale = slot.baseScale;
            float t = 0f;
            while (t < 手牌点击弹跳时长 && rt != null)
            {
                t += Time.unscaledDeltaTime;
                float x = Mathf.Clamp01(t / 手牌点击弹跳时长);
                float pulse = Mathf.Sin(x * Mathf.PI); // 0→1→0
                rt.localScale = baseScale * (1f + (手牌点击弹跳缩放 - 1f) * pulse);
                yield return null;
            }
            if (rt != null) rt.localScale = baseScale;
            _handBounceRoutine = null;
        }

        // ==================== 浮层与出牌区（运行时懒建，画布子级随画布销毁） ====================

        private RectTransform EnsureHandDragLayer()
        {
            if (_handDragLayer != null) return _handDragLayer;
            var canvasRt = (RectTransform)_canvas.transform;
            var go = new GameObject("HandDragLayer", typeof(RectTransform));
            go.transform.SetParent(canvasRt, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling(); // 拖出的卡浮于一切 HUD 之上
            _handDragLayer = rt;
            return rt;
        }

        /// <summary>出牌区白蒙层（拍板「拖拽时屏幕上方 75% 区域变为白色」）：锚 (0,下界)~(1,1)；
        /// SetSiblingIndex(0)=画布最底——只罩棋盘观感，HUD 控件（快捷面板/完成选择钮等）全在其上</summary>
        private RectTransform EnsureHandPlayZone()
        {
            if (_handPlayZone != null) return _handPlayZone;
            var canvasRt = (RectTransform)_canvas.transform;
            var go = new GameObject("HandPlayZone", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(canvasRt, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 出牌区下界比例);
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = false; // 纯视觉蒙层，勿拦射线
            _handPlayZoneImage = img;
            // 下界线（3px，蒙层淡入时同步浮现——「松手即打出」的空间边界）
            var lineGo = new GameObject("Boundary", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            lineGo.transform.SetParent(rt, false);
            var lineRt = (RectTransform)lineGo.transform;
            lineRt.anchorMin = new Vector2(0f, 0f);
            lineRt.anchorMax = new Vector2(1f, 0f);
            lineRt.pivot = new Vector2(0.5f, 0.5f);
            lineRt.anchoredPosition = new Vector2(0f, 2f);
            lineRt.sizeDelta = new Vector2(0f, 3f);
            var lineImg = lineGo.GetComponent<UnityEngine.UI.Image>();
            lineImg.color = new Color(1f, 1f, 1f, 0f);
            lineImg.raycastTarget = false;
            _handPlayZoneLineImage = lineImg;
            rt.SetSiblingIndex(0);
            go.SetActive(false);
            _handPlayZone = rt;
            return rt;
        }

        /// <summary>指针是否在出牌区（顶部；Overlay 画布局部 y 越过下界线）</summary>
        private bool PointerInPlayZone(Vector2 screenPos)
        {
            var canvasRt = _canvas != null ? _canvas.transform as RectTransform : null;
            if (canvasRt == null || canvasRt.rect.height <= 0f) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenPos, null, out var local))
                return false;
            return local.y > canvasRt.rect.height * (出牌区下界比例 - 0.5f);
        }

        /// <summary>蒙层 alpha 平滑（拖拽中=按命中/未命中两档；结束=淡出归零并隐藏）——Update 常驻调用</summary>
        private void UpdateHandPlayZoneAlpha()
        {
            if (_handPlayZoneImage == null) return;
            bool dragging = _handDrag != null && _handDrag.lifted && !_handDrag.forwardedToScroll;
            float target = dragging ? (_handDrag.inPlayZone ? 出牌区命中不透明度 : 出牌区不透明度) : 0f;
            if (!dragging && _handPlayZoneAlpha < 0.005f && target <= 0f)
            {
                if (_handPlayZone.gameObject.activeSelf) _handPlayZone.gameObject.SetActive(false);
                return;
            }
            _handPlayZoneAlpha = Mathf.Lerp(_handPlayZoneAlpha, target,
                1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
            _handPlayZoneImage.color = new Color(1f, 1f, 1f, _handPlayZoneAlpha);
            if (_handPlayZoneLineImage != null)
                _handPlayZoneLineImage.color = new Color(1f, 1f, 1f,
                    Mathf.Clamp01(_handPlayZoneAlpha * 2.6f) * 0.45f);
        }

        // ==================== 拖拽生命周期 ====================

        /// <summary>起拖：只登记会话不立即接管——上提/滑动仲裁在 OnDrag 里判（阈值外的小位移不动卡）。
        /// 手牌仅 Idle 态可见可拖；打出态再拖（regrab）=瞄准中抓回反悔/挪位，仅打出的那张可抓</summary>
        private void OnHandCardBeginDrag(HandCardSlot slot, PointerEventData e)
        {
            if (_layoutEditing) return;
            if (_session == null || _session.Flow == null || _session.Flow.Phase != BattlePhase.Selecting) return;
            if (_handDrag != null) return; // 一次只能拖一张（多点触控守卫，拍板「一次只能打出一张」）
            bool regrab = _state == HudState.Aiming && _playedHandCard == slot;
            if (!regrab && _state != HudState.Idle) return; // 手牌仅手牌态可拖

            var st = new HandCardDragState
            {
                slot = slot,
                rt = slot.rt,
                regrab = regrab,
                fromIndex = _handCardSlots.IndexOf(slot),
            };
            if (st.fromIndex < 0) return;
            st.liftPointer = e.position; // 仲裁起点（竖直上移/横移位移从这里起算）
            _handDrag = st;

            if (regrab)
            {
                // 打出态再拖：卡本就悬浮在拖拽层——直接进入跟随（无需上提仲裁）
                st.lifted = true;
                st.baseScale = slot.baseScale;
                st.followAnchored = st.rt.anchoredPosition;
                st.liftPointer = e.position;
                st.followTarget = st.followAnchored;
                st.lastTarget = st.followTarget;
                st.liftTime = Time.unscaledTime;
                ShowHandPlayZone(true);
            }
        }

        /// <summary>拖拽中：未提起=方向仲裁（上移=提起/横移=转交滚动）；已提起=喂跟随目标+区判定+让位序</summary>
        private void OnHandCardDrag(HandCardSlot slot, PointerEventData e)
        {
            var st = _handDrag;
            if (st == null || st.slot != slot) return;

            if (st.forwardedToScroll)
            {
                if (_handScroll != null) _handScroll.OnDrag(e);
                return;
            }
            if (!st.lifted)
            {
                Vector2 d = e.position - st.liftPointer;
                // 横移占优且越阈值=手牌滑动意图——整段手势转交 ScrollRect（原滚动行为零回归）
                if (Mathf.Abs(d.x) >= 手牌拖拽上提阈值 && Mathf.Abs(d.x) > Mathf.Abs(d.y))
                {
                    st.forwardedToScroll = true;
                    if (_handScroll != null)
                    {
                        _handScroll.OnBeginDrag(e);
                        _handScroll.OnDrag(e);
                    }
                    return;
                }
                // 竖直上移越阈值且竖向占优=提起（向上=朝出牌区，与打出手势同向）
                if (d.y >= 手牌拖拽上提阈值 && Mathf.Abs(d.y) >= Mathf.Abs(d.x) * 0.7f)
                    LiftHandCard(st, e.position);
                return;
            }

            // 已提起：指针位移换算成拖拽层局部目标（抓取点保持——卡不跳到指针中心）
            float sf = _canvas != null ? _canvas.scaleFactor : 1f;
            if (sf <= 0f) sf = 1f;
            st.followTarget = st.followAnchored + (e.position - st.liftPointer) / sf;
            st.inPlayZone = PointerInPlayZone(e.position);
            if (!st.regrab && !st.inPlayZone)
            {
                st.insertIndex = ComputeHandInsertIndex(e.position, st.fromIndex);
                AutoScrollHandWhileDrag(e.position);
            }
        }

        /// <summary>提起卡：脱离滚动壳挂拖拽层（世界位保持）+出牌区蒙层浮现+弹出缩放起步</summary>
        private void LiftHandCard(HandCardDragState st, Vector2 pointer)
        {
            var rt = st.rt;
            if (rt == null) { _handDrag = null; return; }
            st.lifted = true;
            st.baseScale = rt.localScale; // 壳 0.7×槽缩放的世界量（layer 无缩放，localScale 原样成立）
            EnsureHandDragLayer();
            rt.SetParent(_handDragLayer, true); // 世界位保持——提起瞬间零跳变
            st.followAnchored = rt.anchoredPosition;
            st.liftPointer = pointer;
            st.followTarget = st.followAnchored;
            st.lastTarget = st.followTarget;
            st.liftTime = Time.unscaledTime;
            st.insertIndex = st.fromIndex;
            st.inPlayZone = PointerInPlayZone(pointer);
            ShowHandPlayZone(true);
            if (_handBounceRoutine != null) { StopCoroutine(_handBounceRoutine); _handBounceRoutine = null; }
            if (_handScroll != null) _handScroll.StopMovement(); // 提起即断惯性（滚动壳不再处理本手势）
        }

        /// <summary>松手：滚动转交=收滚动；未提起=无操作（点击弹跳走 onClick）；提起=按区分派
        /// （出牌区=打出/手牌区=插位落牌；regrab 手牌区=反悔取消、出牌区=卡挪到新松手位）</summary>
        private void OnHandCardEndDrag(HandCardSlot slot, PointerEventData e)
        {
            var st = _handDrag;
            if (st == null || st.slot != slot) return;
            _handDrag = null;
            if (st.forwardedToScroll)
            {
                if (_handScroll != null) _handScroll.OnEndDrag(e);
                return;
            }
            if (!st.lifted) return;
            _handSuppressClickUntil = Time.unscaledTime + 0.3f; // 吞松手尾巴 onClick

            if (st.regrab)
            {
                if (!PointerInPlayZone(e.position)) ExitAiming(); // 拖回手牌区=反悔（ExitAiming→卡飞回手牌槽）
                // else：卡留在新松手位（仍为打出态，部署瞄准继续）
                return;
            }

            st.inPlayZone = PointerInPlayZone(e.position);
            if (st.inPlayZone) HandleHandCardPlayed(slot);
            else DropHandCardIntoHand(st, e.position);
        }

        /// <summary>手牌区内当前插入位：指针（HandContent 局部 x）越过其他卡**基准槽中心**的个数
        /// ——刻意用基准位而非实时位（让位中实时位随动画振荡，DeckSwitchPanel 同纪律）</summary>
        private int ComputeHandInsertIndex(Vector2 screenPos, int fromIndex)
        {
            if (_handContent == null) return fromIndex;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_handContent, screenPos, null, out var local))
                return fromIndex;
            int count = _handCardSlots.Count;
            int k = 0;
            for (int j = 0; j < count; j++)
            {
                if (j == fromIndex) continue;
                if (HandSlotX(j, count) < local.x) k++;
            }
            return k;
        }

        /// <summary>拖拽中指针接近手牌视口左右缘自动滚动（把远处槽位拖进来；DeckSwitchPanel 同款）</summary>
        private void AutoScrollHandWhileDrag(Vector2 screenPos)
        {
            if (_handScroll == null || _handScroll.viewport == null) return;
            var vp = (RectTransform)_handScroll.viewport;
            var corners = new Vector3[4];
            vp.GetWorldCorners(corners);
            float left = float.MaxValue, right = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float x = RectTransformUtility.WorldToScreenPoint(null, corners[i]).x;
                if (x < left) left = x;
                if (x > right) right = x;
            }
            if (screenPos.x < left + 手牌拖拽滚动余量)
                _handScroll.velocity = new Vector2(手牌拖拽滚动速度, 0f);   // 左缘 → 看更靠前的卡
            else if (screenPos.x > right - 手牌拖拽滚动余量)
                _handScroll.velocity = new Vector2(-手牌拖拽滚动速度, 0f);   // 右缘 → 看更靠后的卡
        }

        /// <summary>拖拽会话显示（拖起/regrab 时调；隐藏由 UpdateHandPlayZoneAlpha 淡出自理）</summary>
        private void ShowHandPlayZone(bool show)
        {
            if (!show) return;
            var rt = EnsureHandPlayZone();
            if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
        }

        // ==================== 每帧驱动（Update 调） ====================

        /// <summary>拖拽手感主循环：跟随平滑+速度倾斜+轻微抖动+命中缩放+其他卡智能让位/回填。
        /// 手感类动画走 unscaled 时间（战斗暂停变速不影响拖拽观感）</summary>
        private void UpdateHandCardDrag()
        {
            UpdateHandPlayZoneAlpha();
            var st = _handDrag;
            if (st == null || !st.lifted || st.forwardedToScroll) return;
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            // 跟随平滑（快而跟手的微滞后）
            float follow = 1f - Mathf.Exp(-dt * Mathf.Max(1f, 拖拽跟随平滑));
            st.rt.anchoredPosition = Vector2.Lerp(st.rt.anchoredPosition, st.followTarget, follow);

            // 速度倾斜（移动方向反馈）：横向速度→左右倾，指数趋近+回正
            Vector2 vel = (st.followTarget - st.lastTarget) / dt;
            st.lastTarget = st.followTarget;
            float tiltTarget = Mathf.Clamp(-vel.x * 拖拽倾斜系数, -拖拽最大倾斜角, 拖拽最大倾斜角);
            st.tilt = Mathf.Lerp(st.tilt, tiltTarget, 1f - Mathf.Exp(-dt * 12f));

            // 持续轻微抖动（双正弦叠加=有机感；幅度小=「轻微」拍板口径）
            float t = Time.unscaledTime - st.liftTime;
            float wobble = Mathf.Sin(t * 拖拽抖动频率) * 拖拽抖动幅度
                         + Mathf.Sin(t * 拖拽抖动频率 * 0.63f + 2.1f) * 拖拽抖动幅度 * 0.5f;
            st.rt.localEulerAngles = new Vector3(0f, 0f, st.tilt + wobble);

            // 命中缩放（出牌区里再放大一档=「此处松手即打出」预告）
            float scaleTarget = st.inPlayZone ? 出牌区命中缩放 : 手牌拖拽缩放;
            st.rt.localScale = Vector3.Lerp(st.rt.localScale, st.baseScale * scaleTarget,
                1f - Mathf.Exp(-dt * 10f));

            // 其他卡智能让位：手牌区内=按当前插入位让出（拖动行插到目标位后自己所在槽的基准位）；
            // 出牌区/regrab=回基准位（打出未提交期间手牌隐藏，序不变，回槽位恒等）
            if (!st.regrab && !st.inPlayZone) ApplyHandYield(st, st.insertIndex);
            else ApplyHandYield(st, -1); // -1=基准位回填
        }

        /// <summary>其他卡让位/回填：insertIndex<0=各回基准槽；否则=按「拖动行插到该位后」重排的槽位趋近。
        /// 位置=槽位公式直算（手牌卡非布局组件驱动，公式即基准——与 RebuildHandCards 同源）</summary>
        private void ApplyHandYield(HandCardDragState st, int insertIndex)
        {
            int count = _handCardSlots.Count;
            float rate = 1f - Mathf.Exp(-Time.unscaledDeltaTime * Mathf.Max(1f, 手牌让位速度));
            for (int j = 0; j < count; j++)
            {
                var s = _handCardSlots[j];
                if (s == st.slot || s.rt == null) continue;
                int slotIdx = j;
                if (insertIndex >= 0)
                {
                    int after = j - (j > st.fromIndex ? 1 : 0);     // 先移除拖动行后的序
                    slotIdx = after + (after >= insertIndex ? 1 : 0); // 再在插入位插回后的序
                }
                var target = new Vector2(HandSlotX(slotIdx, count), -10f);
                s.rt.anchoredPosition = Vector2.Lerp(s.rt.anchoredPosition, target, rate);
            }
        }

        // ==================== 落位 / 打出 / 反悔 ====================

        /// <summary>手牌区内松手=插到当前位置（智能让位的终点）：更新显示序（槽列表+缓存）+飞回槽位收口</summary>
        private void DropHandCardIntoHand(HandCardDragState st, Vector2 screenPos)
        {
            int k = ComputeHandInsertIndex(screenPos, st.fromIndex);
            int count = _handCardSlots.Count;
            // 显示序变更：槽列表重排 + 键缓存同步（其他卡已在让位动画中各就各位，零跳变）
            _handCardSlots.RemoveAt(st.fromIndex);
            _handCardSlots.Insert(k, st.slot);
            WriteBackHandDisplayOrder();
            int newIndex = _handCardSlots.IndexOf(st.slot);
            FlyHandCardToSlot(st.slot, st.baseScale, HandSlotX(newIndex, count));
        }

        /// <summary>打出路由（出牌区松手）：定死拦截→弹回；物品/货币→提示+弹回；
        /// 角色卡→TryDeployOrUpgrade（升命=EnterUpgradePending 待确认态——完成选择才上交；
        /// 否则=进部署瞄准，卡留在松手位悬浮）</summary>
        private void HandleHandCardPlayed(HandCardSlot slot)
        {
            if (slot == null || slot.rt == null) return;
            if (_actionConfirmed)
            {
                ShowBattleToast("Battle_ActionConfirmed");
                FlyHandCardToSlot(slot, slot.baseScale, HandSlotX(_handCardSlots.IndexOf(slot), _handCardSlots.Count));
                return;
            }
            if (!slot.isUnit)
            {
                SetTip("Battle_TipItemCardPending"); // 物品卡使用后续批次接入（原点击口径）
                FlyHandCardToSlot(slot, slot.baseScale, HandSlotX(_handCardSlots.IndexOf(slot), _handCardSlots.Count));
                return;
            }
            TryDeployOrUpgrade(slot.unitValue, slot);
        }

        /// <summary>卡飞回手牌槽（上交成功/反悔取消/物品弹回/定死弹回共用——**卡不消耗**，2026-10-07
        /// 返拍「不应该把我的卡销毁」：提交与取消同一收口飞回，无销毁路径）：飞行期间保持拖拽层
        /// 置顶，到位回容器精确落槽零跳变；_playedHandCard 若为本卡一并清（打出态解除）</summary>
        private void FlyHandCardToSlot(HandCardSlot slot, Vector3 baseScale, float slotX)
        {
            if (slot == null || slot.rt == null) return;
            if (_playedHandCard == slot) _playedHandCard = null;
            if (_handFlyRoutine != null) StopCoroutine(_handFlyRoutine);
            _handFlySlot = slot;
            _handFlyRoutine = StartCoroutine(FlyHandCardRoutine(slot, baseScale, slotX));
        }

        /// <summary>飞行协程（回槽）：到位回 HandContent 精确落槽（与飞行终点零跳变）、缩放/旋转还原</summary>
        private IEnumerator FlyHandCardRoutine(HandCardSlot slot, Vector3 baseScale, float slotX)
        {
            var rt = slot.rt;
            if (rt == null) { _handFlyRoutine = null; _handFlySlot = null; yield break; }
            Vector3 fromPos = rt.position;
            Vector3 fromScale = rt.localScale;
            Quaternion fromRot = rt.localRotation;
            // 槽位世界位=HandContent 局部（anchorRef+anchoredPosition）换算（DeckSwitchPanel 同法；
            // anchorRef 必须按 wrapper 实际 anchor 算——运行时与 prefab 可能不一致的错位教训）
            Vector3 endPos = _handContent.TransformPoint((Vector3)(CalcHandCardAnchorRefLocal(rt) + new Vector2(slotX, -10f)));

            float t = 0f;
            while (t < 手牌回槽时长 && rt != null)
            {
                t += Time.unscaledDeltaTime;
                float x = Mathf.Clamp01(t / 手牌回槽时长);
                float ease = 1f - (1f - x) * (1f - x) * (1f - x); // EaseOutCubic 快出慢收
                rt.position = Vector3.LerpUnclamped(fromPos, endPos, ease);
                rt.localScale = Vector3.LerpUnclamped(fromScale, baseScale, ease);
                rt.localRotation = Quaternion.Slerp(fromRot, Quaternion.identity, ease);
                yield return null;
            }

            _handFlyRoutine = null;
            _handFlySlot = null;
            if (rt == null) yield break;
            // 回槽收口：回容器+精确落槽+缩放旋转还原+显示位对齐 sibling
            rt.SetParent(_handContent, false);
            rt.anchoredPosition = new Vector2(slotX, -10f);
            rt.localScale = baseScale;
            rt.localRotation = Quaternion.identity;
            var group = rt.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = 1f;
            int idx = _handCardSlots.IndexOf(slot);
            if (idx >= 0) rt.SetSiblingIndex(idx);
        }

        /// <summary>wrapper anchor 参考点在 HandContent 局部空间的位置（局部空间以 pivot 为原点，
        /// 与 anchoredPosition 同基准——DeckSwitchPanel CalcAnchorRefLocal 同法）</summary>
        private Vector2 CalcHandCardAnchorRefLocal(RectTransform wrapperRt)
        {
            Rect parent = _handContent.rect;
            Vector2 aMin = new Vector2(
                Mathf.Lerp(parent.xMin, parent.xMax, wrapperRt.anchorMin.x),
                Mathf.Lerp(parent.yMin, parent.yMax, wrapperRt.anchorMin.y));
            Vector2 aMax = new Vector2(
                Mathf.Lerp(parent.xMin, parent.xMax, wrapperRt.anchorMax.x),
                Mathf.Lerp(parent.yMin, parent.yMax, wrapperRt.anchorMax.y));
            return (aMin + aMax) * 0.5f;
        }

        // ==================== 打出态登记 / 收口 ====================

        /// <summary>打出态登记（TryDeployOrUpgrade 部署分支调）：卡悬浮在松手位，部署瞄准期随取随拖</summary>
        private void SetPlayedHandCard(HandCardSlot slot) => _playedHandCard = slot;

        /// <summary>升命卡打出=待确认态（决策五十五返拍「拖入出牌区，不应该直接上交」）：
        /// 卡留在松手位悬浮、手牌隐藏、取消钮现——与部署瞄准同构的反悔窗，仅无落格可选
        /// （aimCells 空——棋盘点任意格都走「点非可选格=取消」弹回）。「完成选择」=确认上交
        /// （SubmitPlayedUpgrade）；拖回手牌区/点空白/取消钮/阶段切换=反悔取消（ExitAiming
        /// wasDeployAim 分支统一收口）；倒计时归零=超时自动按完成选择同链路自动确认升命。
        /// _deployAimUnit 复用部署瞄准的会话载体（ExitAiming 收口/Notify 定死提示同链）</summary>
        private void EnterUpgradePending(int unitNameValue, HandCardSlot slot)
        {
            if (_state != HudState.Idle) return;
            SetPlayedHandCard(slot);
            _deployAimUnit = unitNameValue;
            _upgradePending = true;
            _state = HudState.Aiming;
            _aimDef = null;
            _pendingAimCell = null; // 新会话待定清零（防陈旧提交）
            ClosePopup();
            _aimCells.Clear();
            _aimRecommendedCells.Clear();
            ApplyStateVisibility(); // Aiming 态：取消钮现、手牌藏（打出卡在拖拽层不受影响）
            SetTip("Battle_TipAimUpgrade"); // UIText 12037：确认/反悔双路提示
        }

        /// <summary>升命待确认=确认上交（OnConfirmButtonClicked 升命分支调）：上交+定死+ExitAiming
        /// 统一收口态机——打出卡随收口飞回手牌槽（**卡不消耗**，2026-10-07 返拍「不应该把我的卡销毁」）</summary>
        private void SubmitPlayedUpgrade()
        {
            int unitValue = _deployAimUnit;
            _upgradePending = false;
            var action = new ActionData
            {
                playerId = _myPlayerId,
                actionType = ActionType.DeployUnit,
                deployUnitName = unitValue,
            };
            _session.SubmitAction(action);
            GICLog.Info($"[BattleHud] {_myPlayerId} 上交：升命 {(UnitName)unitValue}");
            _actionConfirmed = true; // 确认即定死（与 SubmitAim 部署同款——Host 已交忽略双保险）
            ExitAiming(); // 打出卡飞回手牌槽（卡不消耗）
            SetTip("Battle_TipSubmitted");
        }

        /// <summary>拖拽中断收口（相位流转/进布局编辑/rebuild 前）：普通拖拽瞬回原槽（序不变）；
        /// regrab 中断交给 ExitAiming 的回槽飞行；飞行中的消耗段须自理销毁（已脱离按钮列表）</summary>
        private void CancelHandCardDrag()
        {
            var st = _handDrag;
            _handDrag = null;
            if (st != null && st.lifted && !st.forwardedToScroll && !st.regrab && st.rt != null)
            {
                var rt = st.rt;
                rt.SetParent(_handContent, false);
                rt.anchoredPosition = new Vector2(HandSlotX(st.fromIndex, _handCardSlots.Count), -10f);
                rt.localScale = st.baseScale;
                rt.localRotation = Quaternion.identity;
            }
            if (_handBounceRoutine != null) { StopCoroutine(_handBounceRoutine); _handBounceRoutine = null; }
        }

        /// <summary>rebuild 前收口（RebuildHandCards 顶部调——重建即将销毁全部 wrapper）：
        /// 拖拽中断；回槽飞行中止（wrapper 仍在按钮列表，重建循环随销——引用清空防悬空）；打出态引用清空</summary>
        private void SettleHandCardsOnRebuild()
        {
            CancelHandCardDrag();
            if (_handFlyRoutine != null)
            {
                StopCoroutine(_handFlyRoutine);
                _handFlyRoutine = null;
                _handFlySlot = null;
            }
            _playedHandCard = null; // 打出悬浮卡随重建销毁（仅相位流转语境会出现，销毁即视觉收场）
        }
    }
}
