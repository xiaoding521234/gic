using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;
using GIC.UI;

namespace GIC.Battle
{
    /// <summary>
    /// 快捷面板分件（2026-10-06 用户拍板「左边加上快捷面板，每行显示 角色头像+爆发技能+势力技能，
    /// 点击该行则选中该角色并丝滑移动相机使其居中；魔神优先排前面，伙伴其次，眷属无需放入；
    /// 需要支持上下滚动」）。
    /// 结构真源=BattleHud.prefab Slots/quick（QuickPanel→QuickScroll→QuickViewport→QuickContent
    /// 四层滚动壳，镜像手牌壳命名与参数——Elastic 0.1/灵敏度 30）；行=运行时建（QueueSlot 头像牌
    /// +Skill.prefab 图标实例×2——复用 SkillIconView 全链：InitWithData 元素色底/SetConditionDimmed
    /// 资源不足置暗/SetResourceProgress 底面水位〔决策四十八在面板行内同样生效〕）。
    /// 数据=RefreshFromSnapshot 驱动：成员/次序签名比对（含存亡）变化才重建行、其余就地刷新；
    /// 行点击走 SelectUnit 既有选中链 + BattleCameraController.FocusWorldPoint（0.5s 快进慢出
    /// 丝滑居中——决策四十七拖动跟随补间同款机件）。
    /// </summary>
    public partial class BattleHud
    {
        private RectTransform _quickContent;
        private ScrollRect _quickScroll;
        private GameObject _skillIconPrefab; // Skill.prefab（行内技能图标实例源，懒加载）
        private readonly List<QuickRow> _quickRows = new List<QuickRow>();
        private string _quickSignature; // 成员/次序签名（含存亡）——变化才重建行

        private const float 快捷行高 = 116f;
        private const float 快捷图标尺寸 = 88f; // 爆发/势力图标与头像同尺寸
        private const float 快捷头像横向偏移 = -100f;
        private const float 快捷爆发横向偏移 = 0f;
        private const float 快捷势力横向偏移 = 100f;
        private const float 快捷头像原生尺寸 = 64f; // QueueSlot 原生 64×64——缩放走 localScale（描环同比）

        /// <summary>快捷面板行（重建时整行销毁重铺；就地刷新只改内容不换件）</summary>
        private class QuickRow
        {
            public string unitId;
            public RectTransform rt;
            public CanvasGroup group; // 尸体行整体降透明
            public Image bg;         // 行底=选中高亮兼点击射线承接
            public Image avatar;
            public Image ring;
            public GameObject burstHost;
            public GameObject factionHost;
            public SkillIconView burstView;
            public SkillIconView factionView;
        }

        /// <summary>快捷面板壳寻址（Build 分件 ResolveHudReferences 调；结构真源=prefab，
        /// 代码只寻址——改摆位/尺寸在编辑器拖槽。**无背景**：2026-10-06 同日目检拍板
        /// 「该面板不需要背景」——QuickPanel 纯容器无 Image，行件（头像牌/技能键）直接浮在画面上）</summary>
        private void ResolveQuickPanel()
        {
            if (!_layoutByKey.TryGetValue("quick", out var def))
            {
                GICLog.Warn("[BattleHud] 布局槽缺失：quick——快捷面板不显示");
                return;
            }
            var shell = def.content.Find("QuickScroll");
            _quickScroll = shell?.GetComponent<ScrollRect>();
            _quickContent = shell != null ? shell.Find("QuickViewport/QuickContent") as RectTransform : null;
            if (_quickScroll == null || _quickContent == null)
                GICLog.Warn("[BattleHud] 快捷面板滚动壳不完整（需 QuickScroll(ScrollRect)/QuickViewport(RectMask2D)/QuickContent）");
        }

        /// <summary>快照驱动刷新（RefreshFromSnapshot 调）：收录→签名比对→重建/就地刷新</summary>
        private void RefreshQuickPanel(BattleSnapshot snapshot)
        {
            if (_quickContent == null) return;
            var included = CollectQuickUnits(snapshot);
            var signature = string.Join(",", included.ConvertAll(u => u.unitId + (u.isCorpse != 0 ? "*" : "")));
            if (signature != _quickSignature)
            {
                RebuildQuickRows(included);
                _quickSignature = signature;
            }
            RefreshQuickRowStates(included);
            UpdateQuickSelectionHighlight();
        }

        /// <summary>收录口径（拍板）：己方单位 ∧ 魔神/伙伴（**眷属不入**）∧ 非协议核心（纯查看键无技能行）；
        /// 排序=魔神前/伙伴后 → 活体前/尸体垫底 → 快照序稳定（成员死亡只在同层级内沉底，其余行不动）</summary>
        private List<UnitState> CollectQuickUnits(BattleSnapshot snapshot)
        {
            var result = new List<UnitState>();
            if (snapshot?.units == null) return result;
            var ranked = new List<(UnitState u, int tierRank, int alive, int idx)>();
            int idx = 0;
            foreach (var u in snapshot.units)
            {
                if (u == null || u.playerId != _myPlayerId) continue;
                var data = TryGetUnitData(u.unitName);
                if (data == null || data.unitType == UnitType.Building) continue; // 协议核心=纯查看且无爆发/势力技能
                var tier = TierOfUnit(u);
                if (tier != UnitTier.Archon && tier != UnitTier.Companion) continue; // 眷属不入（拍板）
                ranked.Add((u, tier == UnitTier.Archon ? 0 : 1, u.isCorpse != 0 ? 1 : 0, idx++));
            }
            return ranked.OrderBy(t => t.tierRank).ThenBy(t => t.alive).ThenBy(t => t.idx)
                .Select(t => t.u).ToList();
        }

        /// <summary>重建行（签名变化时；先清后铺）——行=透明射线底 Button + QueueSlot 头像 + Skill 图标×2</summary>
        private void RebuildQuickRows(List<UnitState> units)
        {
            foreach (var row in _quickRows)
                if (row.rt != null) Destroy(row.rt.gameObject);
            _quickRows.Clear();
            if (units.Count == 0) return;

            if (_avatarTilePrefab == null)
                _avatarTilePrefab = Resources.Load<GameObject>("Prefabs/Battle/QueueSlot");
            if (_avatarTilePrefab == null)
            {
                GICLog.Warn("[BattleHud] QueueSlot.prefab 未找到——快捷面板不显示");
                return;
            }
            if (_skillIconPrefab == null)
                _skillIconPrefab = Resources.Load<GameObject>("Prefabs/UI/Skill/Skill");
            if (_skillIconPrefab == null)
            {
                GICLog.Warn("[BattleHud] Skill.prefab 未找到（Prefabs/UI/Skill/Skill）——快捷面板不显示");
                return;
            }

            foreach (var u in units)
            {
                var data = TryGetUnitData(u.unitName);
                if (data == null) continue;

                var rowGo = new GameObject("Quick_" + u.unitId, typeof(RectTransform), typeof(Image), typeof(Button));
                var rt = (RectTransform)rowGo.transform;
                rt.SetParent(_quickContent, false);
                var le = rowGo.AddComponent<LayoutElement>();
                le.minHeight = 快捷行高;
                le.preferredHeight = 快捷行高;
                le.flexibleHeight = 0f;
                var bg = rowGo.GetComponent<Image>();
                bg.color = Color.clear; // 选中高亮时改色（UpdateQuickSelectionHighlight）
                bg.raycastTarget = true; // 行点击射线承接（覆盖整行）
                var button = rowGo.GetComponent<Button>();
                button.transition = Selectable.Transition.None; // 底图=高亮载体，勿被 tint 覆盖
                var group = rowGo.AddComponent<CanvasGroup>();
                var unitId = u.unitId;
                button.onClick.AddListener(() => OnQuickRowClicked(unitId));

                // 头像牌（QueueSlot 复用——Plate/AvatarMask/Avatar/Ring 契约，raycastTarget 全关）
                var tile = Instantiate(_avatarTilePrefab, rt, false);
                tile.name = "Avatar";
                var tileRt = (RectTransform)tile.transform;
                tileRt.anchorMin = tileRt.anchorMax = tileRt.pivot = new Vector2(0.5f, 0.5f);
                tileRt.anchoredPosition = new Vector2(快捷头像横向偏移, 0f);
                tileRt.localScale = Vector3.one * (快捷图标尺寸 / 快捷头像原生尺寸);

                var burstView = AttachQuickSkillIcon(rt, "Burst", 快捷爆发横向偏移);
                var factionView = AttachQuickSkillIcon(rt, "Faction", 快捷势力横向偏移);

                _quickRows.Add(new QuickRow
                {
                    unitId = u.unitId,
                    rt = rt,
                    group = group,
                    bg = bg,
                    avatar = tileRt.Find("AvatarMask/Avatar")?.GetComponent<Image>(),
                    ring = tileRt.Find("Ring")?.GetComponent<Image>(),
                    burstHost = burstView != null ? burstView.gameObject : null,
                    factionHost = factionView != null ? factionView.gameObject : null,
                    burstView = burstView,
                    factionView = factionView,
                });
            }
        }

        /// <summary>行内技能图标（Skill.prefab 实例=宿主本体）：同 rect 88×88；**SelectButton 必拆**——
        /// 否则图标区域点击被键内 Button 吞掉，行点击失效（行 Button 是唯一入口）</summary>
        private SkillIconView AttachQuickSkillIcon(RectTransform rowRt, string nodeName, float x)
        {
            var inst = Instantiate(_skillIconPrefab, rowRt, false);
            inst.name = nodeName;
            var iconRt = (RectTransform)inst.transform;
            iconRt.anchorMin = iconRt.anchorMax = iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.anchoredPosition = new Vector2(x, 0f);
            iconRt.sizeDelta = new Vector2(快捷图标尺寸, 快捷图标尺寸);

            var view = inst.GetComponent<SkillIconView>();
            var selectButton = inst.GetComponent<SelectButton>();
            if (selectButton != null)
            {
                view.selectButton = null; // 行点击唯一入口=行 Button；键内 SelectButton 吞点击必拆
                Destroy(selectButton);
            }
            return view;
        }

        /// <summary>就地刷新（签名不变时；序=CollectQuickUnits 同序）——头像/描环/存亡透明度/技能图标链</summary>
        private void RefreshQuickRowStates(List<UnitState> units)
        {
            for (int i = 0; i < _quickRows.Count && i < units.Count; i++)
            {
                var row = _quickRows[i];
                var u = units[i];
                var data = TryGetUnitData(u.unitName);
                if (data == null) continue;
                bool dead = u.isCorpse != 0;
                row.group.alpha = dead ? 0.55f : 1f; // 尸体行整体降透明（头像另有尸体灰染）
                if (row.avatar != null)
                {
                    MissingImageGuard.Assign(row.avatar, data.avatar); // 头像缺失兜底（2026-10-06 全位点接入）
                    row.avatar.color = dead ? Palette.立牌尸体灰 : Color.white; // 尸体灰染（拖动头像盘同款）
                }
                if (row.ring != null) // 描环=归属玩家色（执行预览/拖动头像盘同口径）
                {
                    var pc = BattlePlayerColors.Resolve(_session.Sim, _myPlayerId, u.playerId);
                    row.ring.color = new Color(pc.r, pc.g, pc.b, 0.8f);
                }
                var burst = FindQuickSkill(data, SkillType.Burst);
                var faction = FindQuickFactionSkill(data);
                RefreshQuickSkillIcon(row.burstHost, row.burstView, burst, data, u);
                RefreshQuickSkillIcon(row.factionHost, row.factionView, faction, data, u);
            }
        }

        /// <summary>单枚技能图标刷新：InitWithData（元素色底+主动环）→ 资源不足置暗 → 底面水位
        /// （决策四十八链在面板行内复用——魔神/伙伴均按各自元能/玩家池就地判定）；
        /// 单位无该技能=隐藏占位不显示</summary>
        private void RefreshQuickSkillIcon(GameObject host, SkillIconView view,
            SkillConfig.SkillData skill, UnitConfig.UnitData unitData, UnitState u)
        {
            bool has = skill != null;
            if (host != null && host.activeSelf != has) host.SetActive(has);
            if (!has || view == null) return;
            view.InitWithData(skill, unitData, ViewType.OnlyDisplay, null);
            view.SetConditionDimmed(!UnitHasSkillResources(skill, u), 资源不足变暗系数);
            view.SetResourceProgress(UnitSkillResourceProgress(skill, u));
        }

        /// <summary>单位技能表取指定类型首条（无= null）</summary>
        private static SkillConfig.SkillData FindQuickSkill(UnitConfig.UnitData unitData, SkillType type)
        {
            return unitData.skills?.FirstOrDefault(s => s != null && s.data != null && s.data.skillType == type)?.data;
        }

        /// <summary>势力技能取条目：延奏优先、回落契约（契约键尚无 HUD 入口与 AI 档〔docs/11 挂账〕
        /// ——面板与现存 enso 键同口径，契约实装批后自然带上）</summary>
        private static SkillConfig.SkillData FindQuickFactionSkill(UnitConfig.UnitData unitData)
        {
            return FindQuickSkill(unitData, SkillType.Enso) ?? FindQuickSkill(unitData, SkillType.Contract);
        }

        /// <summary>行点击（拍板语义）：选中该角色（走 SelectUnit 既有链——退出瞄准/状态机/
        /// 技能盘/描边/弧光全原路）+ 相机丝滑居中其所在格（位置读现快照防陈旧——就地刷新不换行件）</summary>
        private void OnQuickRowClicked(string unitId)
        {
            if (_layoutEditing) return; // 编辑模式点击=槽选中等布局编辑语义（拖拽板在顶承射线，此处双保险）
            var snapshot = _session?.Player?.LatestSnapshot;
            var u = snapshot?.units.FirstOrDefault(x => x.unitId == unitId);
            if (u == null) return; // 单位已消失（收口竞态）——行随下次签名重建
            SelectUnit(unitId);
            if (_camera != null && _board != null)
                _camera.FocusWorldPoint(_board.CellToWorld(u.position));
        }

        /// <summary>选中行高亮（SelectUnit/DeselectUnit/快照刷新三处驱动；选中=高亮金低透明底）</summary>
        private void UpdateQuickSelectionHighlight()
        {
            var gold = Palette.高亮金;
            foreach (var row in _quickRows)
            {
                if (row.bg == null) continue;
                row.bg.color = row.unitId == _selectedUnitId
                    ? new Color(gold.r, gold.g, gold.b, 0.25f)
                    : Color.clear;
            }
        }

        // ==================== 按单位的资源判定（面板行口径——与选中键口径同形不同源） ====================

        /// <summary>指定单位对该技能的资源门槛（与 HasSkillResources 同形——元能=该单位快照值、
        /// 体力=该单位层级换算、摩拉/物品=本端缓存；面板行就地判各自单位）</summary>
        private bool UnitHasSkillResources(SkillConfig.SkillData skillData, UnitState unit)
        {
            if (skillData == null || !skillData.HasCosts) return true;
            foreach (var cost in skillData.costs)
            {
                if (cost == null || cost.amount <= 0) continue;
                if (cost.kind == CostKind.Stamina)
                {
                    int amount = unit != null
                        ? UnitTierHelper.StaminaCostOf(TierOfUnit(unit))
                        : cost.amount; // 无状态兜底按声明值（理论不可达）
                    if (amount > 0 && _myStamina < amount) return false;
                    continue;
                }
                if (UnitHeldAmount(cost, unit) < cost.amount) return false;
            }
            return true;
        }

        /// <summary>指定单位的技能资源进度（决策四十八口径——非体力消耗 min(持有/需求) 钳 0~1、
        /// 无非体力消耗=1）：元能=该单位快照 energy，玩家池资源与选中键共 HeldPoolAmount 单源</summary>
        private float UnitSkillResourceProgress(SkillConfig.SkillData skillData, UnitState unit)
        {
            if (skillData == null || !skillData.HasCosts) return 1f;
            float progress = 1f;
            bool any = false;
            foreach (var cost in skillData.costs)
            {
                if (cost == null || cost.amount <= 0 || cost.kind == CostKind.Stamina) continue;
                any = true;
                progress = Mathf.Min(progress, UnitHeldAmount(cost, unit) / (float)cost.amount);
            }
            return any ? Mathf.Clamp01(progress) : 1f;
        }

        /// <summary>消耗条目持有量（单位口径）：元能=单位快照值，其余走 HeldPoolAmount 单源</summary>
        private int UnitHeldAmount(SkillCostEntry cost, UnitState unit)
        {
            if (cost.kind == CostKind.Energy) return unit != null ? unit.energy : 0;
            return HeldPoolAmount(cost);
        }
    }
}
