using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Tool;
using GIC.UI;

namespace GIC.Battle
{
    /// <summary>
    /// 被动技能盘分件（2026-10-07 用户拍板「技能盘上除了显示主动，也要显示被动技能（比如变奏，天赋），
    /// 被动技能显示在屏幕中间下方右一点，且尺寸为主动技能的 70%（该尺寸应当是自定义布局的尺寸）」）。
    /// 结构真源=BattleHud.prefab Slots/passive（槽内首子级=PassivePanel 纯容器——无背景，同 QuickPanel
    /// 「该面板不需要背景」拍板口径；图标=运行时建 Skill.prefab 实例，同快捷面板行内图标建法）。
    /// 收录口径=UnitData.skills 全部非主动技能（SkillType.IsActive()==false：变奏/天赋/连携/智慧，
    /// 现有 12/35 单位 0~2 条）；显隐=随选中态（ApplyStateVisibility「passive」键同技能盘成组）；
    /// 协议核心=纯查看无技能盘拍板 → 图标清空（空容器无背景=不可见）。
    /// 点击=开技能详情面板（被动无瞄准语义——InitWithData+自适应摆图标旁，同技能键详情模式链；
    /// 再点同图标=收起；敌方查看态同技能键拍板口径全开无置灰）。
    /// 布局=「passive」槽（AllLayoutKeys 注册、可拖可缩、方案随主存档持久化）；默认锚点 prefab 烘焙
    /// (0.58,0.24)=屏幕中间下方右一点；图标基准尺寸 154=主动技能键 220×70%（拍板值——
    /// 槽 scale 即自定义布局的尺寸缩放入口，基准值勿在代码改 220 关联）。
    /// </summary>
    public partial class BattleHud
    {
        private RectTransform _passiveContent;  // passive 槽内 PassivePanel 容器（图标父级）
        private string _passiveSignature;       // 选中单位+被动数签名（RebuildHandCards 防抖纪律同款）
        private PassiveSkillIcon _passivePopupSource; // 详情面板当前源（同图标再点=收起，异图标=换内容）

        /// <summary>被动技能图标条目（重建时整列销毁重铺）</summary>
        private class PassiveSkillIcon
        {
            public SkillConfig.SkillData data;
            public RectTransform rt;
            public SkillIconView view;
        }

        private readonly List<PassiveSkillIcon> _passiveIcons = new List<PassiveSkillIcon>();
        private readonly List<GameObject> _passivePlaceholders = new List<GameObject>(); // 布局编辑模式占位（无选中单位时槽可见可拖）

        /// <summary>被动图标基准尺寸（画布单位）=主动技能键 220 的 70%（拍板「尺寸为主动技能的70%」；
        /// 调整体量改槽 scale（自定义布局缩放）或此基准值，勿按运行时键位读值——会与槽缩放叠乘</summary>
        private const float 被动图标尺寸 = 154f;
        /// <summary>被动图标横向间距（画布单位；行居中铺开，两端对称）</summary>
        private const float 被动图标间距 = 12f;

        /// <summary>passive 槽寻址（Build 分件 ResolveHudReferences 调；图标运行时建——容器无需壳层组件）</summary>
        private void ResolvePassivePanel()
        {
            if (!_layoutByKey.TryGetValue("passive", out var def))
            {
                GICLog.Warn("[BattleHud] 布局槽缺失：passive——被动技能不显示");
                return;
            }
            _passiveContent = def.content;
        }

        /// <summary>被动技能盘刷新（RefreshSkillButtons 调；快照每回合多次驱动——签名比对防抖）：
        /// 建筑纯查看/无被动 → 清空（空容器不可见）；有被动 → 签名变化才重建图标列</summary>
        private void RefreshPassivePanel(UnitConfig.UnitData unitData)
        {
            if (_passiveContent == null) return; // 槽缺失已 Warn

            var passive = unitData != null && unitData.unitType != UnitType.Building
                ? unitData.skills?.Where(s => s != null && s.data != null && !s.data.skillType.IsActive())
                    .Select(s => s.data).ToList()
                : null;
            if (passive == null || passive.Count == 0)
            {
                if (_passiveSignature != null) // 从有到无才清一次（防逐帧清）
                {
                    ClearPassiveIcons();
                    _passiveSignature = null;
                }
                return;
            }

            string signature = unitData.unitName + ":" + passive.Count;
            if (signature != _passiveSignature)
            {
                RebuildPassiveIcons(passive, unitData);
                _passiveSignature = signature;
            }
        }

        /// <summary>重建图标列（先清后铺；同快捷面板行内图标建法——Skill.prefab 实例+SkillIconView 全链，
        /// 被动色环/元素色底由 InitWithData 现有链接管；无资源门槛/层级门控=恒亮恒可点，查看态同拍板口径）</summary>
        private void RebuildPassiveIcons(List<SkillConfig.SkillData> passive, UnitConfig.UnitData unitData)
        {
            ClearPassiveIcons();
            if (_skillIconPrefab == null)
                _skillIconPrefab = Resources.Load<GameObject>("Prefabs/UI/Skill/Skill");
            if (_skillIconPrefab == null)
            {
                GICLog.Warn("[BattleHud] Skill.prefab 未找到（Prefabs/UI/Skill/Skill）——被动技能不显示");
                return;
            }

            int n = passive.Count;
            float pitch = 被动图标尺寸 + 被动图标间距;
            for (int i = 0; i < n; i++)
            {
                var data = passive[i];
                var inst = Instantiate(_skillIconPrefab, _passiveContent, false);
                inst.name = "Passive_" + data.skillID;
                var rt = (RectTransform)inst.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2((i - (n - 1) * 0.5f) * pitch, 0f); // 行居中
                rt.sizeDelta = new Vector2(被动图标尺寸, 被动图标尺寸);

                var view = inst.GetComponent<SkillIconView>();
                view.InitWithData(data, unitData, ViewType.OnlyDisplay, _skillDetailView);
                var icon = new PassiveSkillIcon { data = data, rt = rt, view = view };
                if (view.selectButton != null)
                {
                    // 点击直连开详情面板（onClick 含 Button.Press 置灰门——被动恒 interactable）；
                    // 勿挂 SkillDragForwarder：被动无瞄准语义
                    view.selectButton.onClick.AddListener(() => OnPassiveIconClicked(icon));
                }
                _passiveIcons.Add(icon);
            }
        }

        private void ClearPassiveIcons()
        {
            if (_passivePopupSource != null && _passiveIcons.Contains(_passivePopupSource))
                _passivePopupSource = null; // 源图标随重建销毁——引用防悬挂
            foreach (var icon in _passiveIcons)
                if (icon.rt != null) Destroy(icon.rt.gameObject);
            _passiveIcons.Clear();
        }

        /// <summary>被动图标点击：详情面板开/收（同键循环语义——面板开着且源=本图标=收起，
        /// 否则换内容摆本图标旁；瞄准态不扰动——被动不进 EnterAiming/ExitAiming 状态机）</summary>
        private void OnPassiveIconClicked(PassiveSkillIcon icon)
        {
            if (icon == null || _layoutEditing) return; // 编辑期点击让位给布局编辑（拖拽板在控件之上，双保险）
            if (PopupOpen && _passivePopupSource == icon) ClosePopup();
            else ShowPassiveSkillPopup(icon);
        }

        /// <summary>开被动技能详情（详情模式同链：InitWithData 先行 → 自适应摆图标旁 → 滑入）</summary>
        private void ShowPassiveSkillPopup(PassiveSkillIcon icon)
        {
            if (_skillDetailView == null || icon?.data == null || icon.rt == null) return;
            var unitData = GetSelectedUnitData();
            if (unitData == null) return;

            _passivePopupSource = icon;
            _skillDetailView.skillDetailPanel.SetActive(true);
            _skillDetailView.InitWithData(icon.data, unitData, null);
            PositionSkillPopupBesideKey(icon.rt);
            _skillDetailView.OpenPanel();
        }

        // ==================== 布局编辑模式占位（preview 槽 ShowLayoutPlaceholder 同款先例） ====================

        /// <summary>编辑态占位图标（无选中单位时被动盘为空=不可见无从拖——建两枚无数据幽灵键显形；
        /// 真图标在场则跳过；幂等——已有占位早退）</summary>
        private void ShowPassiveLayoutPlaceholder()
        {
            if (_passiveContent == null || _passiveIcons.Count > 0 || _passivePlaceholders.Count > 0) return;
            if (_skillIconPrefab == null)
                _skillIconPrefab = Resources.Load<GameObject>("Prefabs/UI/Skill/Skill");
            if (_skillIconPrefab == null) return;

            float pitch = 被动图标尺寸 + 被动图标间距;
            for (int i = 0; i < 2; i++) // 现有全员至多 2 条被动（变奏+天赋），占位按此铺
            {
                var inst = Instantiate(_skillIconPrefab, _passiveContent, false);
                inst.name = "LayoutPlaceholder";
                var rt = (RectTransform)inst.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2((i - 0.5f) * pitch, 0f);
                rt.sizeDelta = new Vector2(被动图标尺寸, 被动图标尺寸);
                var selectButton = inst.GetComponent<SelectButton>();
                if (selectButton != null) Destroy(selectButton); // 占位不可点
                _passivePlaceholders.Add(inst);
            }
        }

        /// <summary>收编辑占位（真行/占位互斥——真图标在场时占位本来就没建）</summary>
        private void HidePassiveLayoutPlaceholder()
        {
            foreach (var p in _passivePlaceholders)
                if (p != null) Destroy(p);
            _passivePlaceholders.Clear();
        }
    }
}
