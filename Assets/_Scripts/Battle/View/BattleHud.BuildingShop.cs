using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GIC.Data;
using GIC.Framework;

namespace GIC.Battle
{
    /// <summary>
    /// 建筑购买面板分件（决策八十六批E，docs/active/45 §7.4——BattleHud partial）：选择阶段点击
    /// 我方存活建筑（含协议核心）=选中+购买面板弹出（敌方建筑/执行阶段点击=纯查看不弹）。
    /// 面板=可购买卡横排（Card.prefab 唯一形态复用+购买价角标覆写〔部署费角标字段填 PurchaseCostOf〕
    /// +未解锁/摩拉不足灰显）+**拖卡入手牌区（下方）=购买**（拍板原文「拖入下方」——客户端预检
    /// 解锁/摩拉→SubmitInstantAction BuyCard 即时通道→Host 结算→CardGain/ItemGain 广播→手牌镜像
    /// 即时新增；HoverTiltEffect 让位协议=拖起 Suspend/弹回 Resume）；点击卡=无动作（卡详情入口
    /// 后续批次挂账）。收口=点外/换选/进瞄准（ExitAiming 同路）/阶段流转（OnPhaseChanged）/
    /// 拖起手牌（EnterDeployAim 等 ClosePopup 同族）——全部经 CloseBuildingShop 单出口；
    /// 面板开态计入 BrowseUiOpen 聚合=单人局选择倒计时自动暂停（N0 同口径）。
    /// 卡行无滚动（当前建筑列表 ≤3 项；未来建筑增多时挂账扩展 ScrollRect）。
    /// </summary>
    public partial class BattleHud
    {
        private GameObject _shopRoot;
        private RectTransform _shopRow;
        private readonly List<(GameObject wrapper, Card card, CardId cardId, Image gray)> _shopCards
            = new List<(GameObject, Card, CardId, Image)>();
        private CardId _shopDragCard;     // 拖拽购买会话（拖起卡；None=无会话）
        private RectTransform _shopDragGhost; // 拖拽中的 wrapper 本体（跟随指针）

        private bool BuildingShopOpen => _shopRoot != null && _shopRoot.activeSelf;

        /// <summary>选择我方存活建筑=弹购买面板（SelectUnit 尾部调用；条件不满足静默=敌方纯查看）</summary>
        private void TryOpenBuildingShop(string unitId)
        {
            if (_session == null || _session.Phase != BattlePhase.Selecting) return;
            var snapshot = _session.Player?.LatestSnapshot;
            if (snapshot == null) return;
            UnitState buildingState = null;
            foreach (var u in snapshot.units)
                if (u.unitId == unitId) { buildingState = u; break; }
            if (buildingState == null || buildingState.isCorpse != 0) return;
            if (buildingState.playerId != _myPlayerId) return; // 仅我方建筑（2v2 队友建筑=纯查看）
            var unitData = _unitConfig != null ? _unitConfig.GetUnitData(ResolveUnitName(buildingState.unitName)) : null;
            if (unitData == null || unitData.unitType != UnitType.Building) return;
            if (unitData.可购买卡片 == null || unitData.可购买卡片.Count == 0) return; // 协议核心等无列表=纯查看
            OpenBuildingShop(unitData);
        }

        private static UnitName ResolveUnitName(string name)
        {
            return Enum.TryParse<UnitName>(name, out var parsed) ? parsed : UnitName.Amber;
        }

        /// <summary>建件+填充（幂等复用根；每开重建卡行——灰显/摩拉态随开面板与购买后刷新）。
        /// 末尾重估 ApplyStateVisibility：SelectUnit 的调用序在面板创建**之前**（当时 BuildingShopOpen
        /// 尚 false，手牌被 acting 态隐藏）——面板激活后重估才吃到「面板开=手牌显示」规则
        /// （2026-10-22 报障①返修：顺序 bug）</summary>
        private void OpenBuildingShop(UnitConfig.UnitData buildingData)
        {
            if (_shopRoot == null) BuildBuildingShop();
            _shopRoot.SetActive(true);
            RebuildShopCards(buildingData);
            ApplyStateVisibility(); // 面板开态重估（手牌显示）
        }

        /// <summary>购买成功事件桥（OnItemGained/OnUnitCardGained 订阅——签名兼容四参/三参两个事件）；
        /// 购买单位卡=手牌全量重建（OnItemGainedHandler.RebuildHandCards 同款口径——单位卡无实例
        /// 条目语义，签名变化直接重建；2026-10-22 报障②返修：首版漏接手牌重建=卡进了镜像不出 UI）+
        /// 面板灰显/摩拉态刷新</summary>
        private void OnShopPurchaseRefreshHandler(string playerId, int a, int b, int c)
        {
            if (playerId != _myPlayerId) return;
            RebuildHandCardsForPurchase();
            RefreshShopAfterPurchase();
        }

        private void OnShopPurchaseRefreshHandler(string playerId, int a, int b)
        {
            if (playerId != _myPlayerId) return;
            RebuildHandCardsForPurchase();
            RefreshShopAfterPurchase();
        }

        /// <summary>购买后手牌重建（读本地快照——ApplyCardGainToLocalHand 已并入条目；
        /// OnItemGainedHandler 全量重建同款）</summary>
        private void RebuildHandCardsForPurchase()
        {
            var snapshot = _session?.Player?.LatestSnapshot;
            if (snapshot == null) return;
            PlayerResourceState myRes = null;
            foreach (var r in snapshot.resources)
                if (r.playerId == _myPlayerId) { myRes = r; break; }
            if (myRes != null) RebuildHandCards(myRes);
        }

        /// <summary>面板收口单出口（DeselectUnit/OnPhaseChanged/ExitAiming 同族调用；幂等）</summary>
        private void CloseBuildingShop()
        {
            CancelShopDrag();
            if (_shopRoot != null) _shopRoot.SetActive(false);
        }

        private void BuildBuildingShop()
        {
            _shopRoot = new GameObject("BuildingShopPanel");
            // 挂画布根（HandPlayZone 运行时建件同款先例）：**勿挂 BattleHud 组件根**——prefab 根=
            // 3px 左边缘竖条（(0,0)-(0,1) 锚），挂其下 anchor(0.5,0.5) 会把面板画到屏幕外（首版实证）；
            // 画布引用=_canvas 字段单源（CanvasRect/手牌热区同源——GetComponentInParent<Canvas> 在
            // BattleHud 实例上实测返回 null，勿再用）
            Transform canvasTransform = _canvas != null ? _canvas.transform : null;
            if (canvasTransform == null)
            {
                var found = GetComponentInParent<Canvas>();
                if (found != null) canvasTransform = found.transform;
            }
            if (canvasTransform == null)
            {
                GICLog.Warn("[BuildingShop] 画布引用缺失，面板不建（Bind 前点击防御）");
                return;
            }
            _shopRoot.transform.SetParent(canvasTransform, false);
            _shopRoot.transform.SetAsLastSibling(); // 顶栏/手牌之上（同画布后画的在上）
            var rootRt = _shopRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.anchoredPosition = new Vector2(0f, 60f);
            rootRt.sizeDelta = new Vector2(860f, 300f);

            // 深底面板（纯色 Image——无资产依赖；后续素材批可换九切片资产）
            var bg = _shopRoot.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.07f, 0.08f, 0.11f, 0.94f);
            bg.raycastTarget = true; // 点面板内不穿到棋盘（点外关闭由棋盘点击路径换选/取消选中承担）

            // 卡行（横排居中；无滚动——当前建筑列表 ≤3）
            var rowGo = new GameObject("ShopRow");
            rowGo.transform.SetParent(_shopRoot.transform, false);
            _shopRow = rowGo.AddComponent<RectTransform>();
            _shopRow.anchorMin = _shopRow.anchorMax = new Vector2(0.5f, 0.5f);
            _shopRow.anchoredPosition = Vector2.zero;
            _shopRow.sizeDelta = new Vector2(820f, 260f);
        }

        /// <summary>重建卡行（开面板/购买成功后刷新共用）</summary>
        private void RebuildShopCards(UnitConfig.UnitData buildingData)
        {
            foreach (var entry in _shopCards)
                if (entry.wrapper != null) Destroy(entry.wrapper);
            _shopCards.Clear();

            if (_handCardPrefab == null)
                _handCardPrefab = Resources.Load<GameObject>("Prefabs/Backpack/Card");
            if (_handCardPrefab == null) { GICLog.Warn("[BuildingShop] Card.prefab 未找到"); return; }

            var itemConfig = CardConfigResolver.Instance?.ItemConfig;
            var snapshot = _session?.Player?.LatestSnapshot;
            int myMora = GetMyMora(snapshot);
            int count = buildingData.可购买卡片.Count;
            const float cardW = 150f, cardH = 225f, gap = 24f;
            float totalW = count * cardW + (count - 1) * gap;
            for (int i = 0; i < count; i++)
            {
                var cardId = buildingData.可购买卡片[i];
                int cost = cardId.PurchaseCostOf(_unitConfig, itemConfig);
                bool locked = IsShopCardLocked(cardId);
                bool noMora = myMora < cost;

                var wrapperGo = new GameObject($"Shop_{cardId}");
                wrapperGo.transform.SetParent(_shopRow, false);
                var wrapperRt = wrapperGo.AddComponent<RectTransform>();
                wrapperRt.pivot = new Vector2(0.5f, 0.5f);
                wrapperRt.anchoredPosition = new Vector2(-totalW / 2f + cardW / 2f + i * (cardW + gap), 0f);
                wrapperRt.sizeDelta = new Vector2(cardW, cardH);

                var hit = wrapperGo.AddComponent<UnityEngine.UI.Image>();
                hit.color = Color.clear;
                hit.raycastTarget = true;

                var cardGo = Instantiate(_handCardPrefab, wrapperGo.transform, false);
                var cardRt = cardGo.GetComponent<RectTransform>();
                cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
                cardRt.anchoredPosition = Vector2.zero;
                cardRt.localScale = Vector3.one * (cardW / 160f); // 等比缩（160×240 原生）
                foreach (var graphic in cardGo.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                    graphic.raycastTarget = false; // 卡内关射线防拦截（手牌同口径）

                var card = cardGo.GetComponent<Card>();
                if (card != null)
                {
                    var saveData = new GIC.Framework.SaveCardData();
                    if (cardId.cardType == CardType.Unit) saveData.SaveUnit(cardId.AsUnitName(), 1);
                    else saveData.SaveItem(cardId.AsItemName(), 1);
                    card.SetViewType(GIC.UI.ViewType.OnlyDisplay);
                    card.Init(saveData, null);
                    // 购买价角标覆写（Card 部署费角标字段：单位卡 Init 已填部署价——覆写为购买价
                    // =部署价一半；物品卡角标被 UnitCardViewStrategy 隐藏，此处显式开+填）
                    if (card.部署费角标 != null) card.部署费角标.SetActive(cardId.cardType == CardType.Unit);
                    if (card.部署费数字 != null) card.部署费数字.text = cost.ToString();
                }

                // 灰显遮罩（未解锁/摩拉不足——点击/拖拽全拦；面板刷新时重评）
                var grayGo = new GameObject("GrayOverlay");
                grayGo.transform.SetParent(wrapperGo.transform, false);
                var grayRt = grayGo.AddComponent<RectTransform>();
                grayRt.anchorMin = grayRt.anchorMax = Vector2.zero;
                grayRt.offsetMin = Vector2.zero;
                grayRt.offsetMax = Vector2.zero;
                var gray = grayGo.AddComponent<UnityEngine.UI.Image>();
                gray.color = new Color(0f, 0f, 0f, 0.55f);
                gray.raycastTarget = locked || noMora; // 可买态不拦射线（拖拽走 wrapper）

                var dragHandler = wrapperGo.AddComponent<ShopCardDragHandler>();
                dragHandler.Setup(this, cardId, locked || noMora, locked, noMora);

                _shopCards.Add((wrapperGo, card, cardId, gray));
            }
        }

        /// <summary>客户端解锁预检（购买面板灰显口径；Host BuyCardExecutor 权威兜底同源）</summary>
        private bool IsShopCardLocked(CardId cardId)
        {
            if (cardId.cardType != CardType.Unit) return false;
            var saveManager = Wargame.Instance?.Context?.Get<SaveManager>();
            var ownedUnits = saveManager?.CurrentSave?.progress?.ownedUnits;
            if (ownedUnits == null) return false;
            foreach (var owned in ownedUnits)
            {
                if (owned == null || owned.id.cardType != CardType.Unit) continue;
                if (owned.id.value == cardId.value) return false;
            }
            return true;
        }

        private int GetMyMora(BattleSnapshot snapshot)
        {
            if (snapshot == null) return 0;
            foreach (var r in snapshot.resources)
                if (r.playerId == _myPlayerId) return r.mora;
            return 0;
        }

        /// <summary>购买上交（ShopCardDragHandler 拖入手牌区松手调用——客户端预检+toast）</summary>
        private void SubmitShopPurchase(CardId cardId)
        {
            if (_session == null) return;
            var snapshot = _session.Player?.LatestSnapshot;
            var itemConfig = CardConfigResolver.Instance?.ItemConfig;
            int cost = cardId.PurchaseCostOf(_unitConfig, itemConfig);
            if (IsShopCardLocked(cardId))
            {
                GICLog.Info($"[BuildingShop] 购买拦截（未解锁）：{cardId}");
                ShowBattleToast("Battle_PurchaseLocked");
                return;
            }
            var myMora = GetMyMora(snapshot);
            if (myMora < cost)
            {
                GICLog.Info($"[BuildingShop] 购买拦截（摩拉 {myMora}/{cost}）：{cardId}");
                ShowBattleToast("Battle_PurchaseNoMora");
                return;
            }
            GICLog.Info($"[BuildingShop] 购买上交：{cardId}（{cost} 摩拉）");
            _session.SubmitInstantAction(new ActionData
            {
                playerId = _myPlayerId,
                actionType = ActionType.BuyCard,
                buyCardType = (int)cardId.cardType,
                buyCardValue = cardId.value,
            });
            // 购买结果经 CardGain/ItemGain 命令回来（OnItemGained/OnUnitCardGained → 面板灰显/摩拉态刷新）
        }

        /// <summary>购买成功后刷新面板灰显/摩拉（事件订阅处调；面板未开静默）</summary>
        private void RefreshShopAfterPurchase()
        {
            if (!BuildingShopOpen) return;
            var snapshot = _session?.Player?.LatestSnapshot;
            if (snapshot == null) return;
            // 刷新当前面板：重评灰显（摩拉扣减/已购新卡不影响解锁态；重建=最简刷新口径）
            var unitData = GetCurrentShopBuilding(snapshot);
            if (unitData != null) RebuildShopCards(unitData);
        }

        private UnitConfig.UnitData GetCurrentShopBuilding(BattleSnapshot snapshot)
        {
            if (_selectedUnitId == null) return null;
            foreach (var u in snapshot.units)
            {
                if (u.unitId != _selectedUnitId) continue;
                var data = _unitConfig != null ? _unitConfig.GetUnitData(ResolveUnitName(u.unitName)) : null;
                return data;
            }
            return null;
        }

        // ==================== 拖拽（拖入手牌区=购买）====================

        private void CancelShopDrag()
        {
            if (_shopDragGhost != null)
            {
                var handler = _shopDragGhost.GetComponent<ShopCardDragHandler>();
                if (handler != null) handler.ResetPosition();
            }
            _shopDragCard = default;
            _shopDragGhost = null;
        }

        /// <summary>面板卡拖拽承载（IBeginDrag/IDrag/IEndDrag——拖入手牌区松手=购买；拖回面板=反悔弹回）</summary>
        private class ShopCardDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            private BattleHud _hud;
            private CardId _cardId;
            private bool _blocked; // 灰显（未解锁/摩拉不足）——拖起即拦+toast
            private bool _locked;
            private bool _noMora;
            private Vector2 _homePos;
            private RectTransform _rt;
            private bool _dragging;

            public void Setup(BattleHud hud, CardId cardId, bool blocked, bool locked, bool noMora)
            {
                _hud = hud;
                _cardId = cardId;
                _blocked = blocked;
                _locked = locked;
                _noMora = noMora;
                _rt = (RectTransform)transform;
                _homePos = _rt.anchoredPosition;
            }

            public void ResetPosition()
            {
                if (_rt != null) _rt.anchoredPosition = _homePos;
                _dragging = false;
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (_blocked)
                {
                    BattleHud.ShowBattleToast(_locked ? "Battle_PurchaseLocked" : "Battle_PurchaseNoMora");
                    return;
                }
                _dragging = true;
                _hud._shopDragCard = _cardId;
                _hud._shopDragGhost = _rt;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (!_dragging) return;
                _rt.anchoredPosition += eventData.delta / GetCanvasScale();
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (!_dragging) return;
                _dragging = false;
                _hud._shopDragCard = default;
                _hud._shopDragGhost = null;
                // 手牌热区内松手=购买（IsPointerNearHand 手牌拖拽同源热区）；否则弹回原位
                bool inHandZone = _hud.IsPointerNearHand(_hud.CanvasRect);
                GICLog.Info($"[BuildingShop] 拖拽松手：{_cardId} → {(inHandZone ? "手牌区（购买）" : "区外（弹回）")}");
                ResetPosition();
                if (inHandZone)
                    _hud.SubmitShopPurchase(_cardId);
            }

            /// <summary>Canvas 缩放补偿（ eventData.delta 为屏幕像素；恒近似=Canvas scale，
            /// 简化为 GetCanvasScale 倒数——观感级跟随无需精确）</summary>
            private float GetCanvasScale()
            {
                var canvas = GetComponentInParent<Canvas>();
                return canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            }
        }
    }
}
