using UnityEngine;
using UnityEngine.UI;
using GIC.Battle;
using GIC.Data;
using GIC.Tool;
namespace GIC.UI
{


    public class SkillIconView : MonoBehaviour
    {
        public Image skillIcon;
        public Image skillCircle;
        public Image skillBadge;
        public SelectButton selectButton;

        private OrbitBeamsUi _selectBeams; // 选中态两束元素色环绕弧光（打钩图 2026-09-27 全项目退役，运行时建件勿入 prefab）
        private Color _elementColor = Color.white;

        // 使用条件不足置暗的三图基准色（SetConditionDimmed 用——InitWithData 每次刷新时缓存）
        private Color _baseIconColor = Color.white;
        private Color _baseBadgeColor = Color.white;
        private Color _baseCircleColor = SkillCircleColor.colorAvailable;

        private ViewType viewType;
        private SkillConfig.SkillData skillData;
        private UnitConfig.UnitData unitData;

        public SkillDetailView skillDetailView;

        public void Awake()
        {
            if (selectButton != null)
                selectButton.onSelectedChanged.AddListener(OnSelectedChanged);
        }

        public void InitWithData(SkillConfig.SkillData skillData, UnitConfig.UnitData unitData, ViewType viewType, SkillDetailView skillDetailView)
        {
            this.viewType = viewType;
            this.skillData = skillData;
            this.unitData = unitData;
            this.skillDetailView = skillDetailView;

            // 元素染色（2026-09-10 拍板：图案保持白色不染，底图染亮元素色；色值=配置文件。
            // 2026-09-10 晚曾试"图案染提亮元素色"，用户目检后取消——勿再主动提案）
            // 底图为白基底圆板，乘色后即完整元素亮色；无 unitData 回退物理灰防白底白图
            Color elementColor = unitData != null
                ? ElementFactionConfig.Instance.GetElementColor(unitData.selfElement)
                : ElementFactionConfig.Instance.GetElementColor(ElementType.Physical);
            if (skillIcon != null)
            {
                MissingImageGuard.Assign(skillIcon, skillData.icon); // 技能图标缺失兜底（2026-10-06 拍板全位点接入）
                skillIcon.color = Color.white;
            }
            if (skillBadge != null)
                skillBadge.color = elementColor;
            _elementColor = elementColor; // 选中弧光同元素色（与战斗 BattleHud.SelectedElementColor 同源）

            if (skillData.skillType.IsActive())
            {
                if (skillCircle != null)
                    skillCircle.color = SkillCircleColor.colorAvailable;
            }
            else
            {
                if (skillCircle != null)
                    skillCircle.color = SkillCircleColor.colorPassive;
            }

            // 基准色缓存：InitWithData 每次刷新都重写三图颜色=基准重置点，置暗在该点之上叠加
            //（战斗 HUD 刷新序恒为 InitWithData → SetConditionDimmed）
            _baseIconColor = skillIcon != null ? skillIcon.color : Color.white;
            _baseBadgeColor = skillBadge != null ? skillBadge.color : Color.white;
            _baseCircleColor = skillCircle != null ? skillCircle.color : SkillCircleColor.colorAvailable;
        }

        /// <summary>使用条件不足整体置暗（2026-10-04 战斗 HUD 拍板「不止图标，包括底面、圆环」）：
        /// 图标/底板/色环三图基准色统一乘暗系数——RGB 乘、alpha 不动（变暗非变透明）；与层级门控
        /// CanvasGroup 半透明为正交机制，两源同键命中时视觉叠加（拍板「两者可以同时叠加」）。
        /// 须在 InitWithData 之后调用（基准色随刷新重写入）；背包等非战斗消费方不调用恒为亮态。</summary>
        public void SetConditionDimmed(bool dimmed, float factor)
        {
            factor = Mathf.Clamp01(factor);
            float f = dimmed ? factor : 1f;
            if (skillIcon != null) skillIcon.color = Darken(_baseIconColor, f);
            if (skillBadge != null) skillBadge.color = Darken(_baseBadgeColor, f);
            if (skillCircle != null) skillCircle.color = Darken(_baseCircleColor, f);
        }

        /// <summary>RGB 乘暗系数、alpha 保持（Color 运算符连 alpha 一起乘=变透明，非本拍板语义）</summary>
        private static Color Darken(Color c, float factor) =>
            new Color(c.r * factor, c.g * factor, c.b * factor, c.a);

        /// <summary>选中态=两束元素色环绕弧光（2026-09-27 拍板全项目统一：打钩图退役、
        /// 战斗/背包同表现；战斗屏 OnlyDisplay 不走此链——由 BattleHud.SetAimSelectRing 驱动同款弧光。
        /// Toggle→SelectButton 改版同日：视觉订阅 onSelectedChanged 状态事件）</summary>
        private void OnSelectedChanged(bool isSelected)
        {
            if(viewType == ViewType.OnlyDisplay)
            {
                return;
            }

            SetSelectBeams(isSelected);

            if (isSelected && viewType == ViewType.Display)
            {
                skillDetailView.OpenPanel();
                skillDetailView.InitWithData(skillData, unitData,this);
            }
        }

        /// <summary>选中弧光亮/灭（元素色=InitWithData 染底图同色；首次点亮懒建）</summary>
        private void SetSelectBeams(bool on)
        {
            if (!on)
            {
                if (_selectBeams != null) _selectBeams.gameObject.SetActive(false);
                return;
            }
            if (_selectBeams == null)
                _selectBeams = OrbitBeamsUi.Create((RectTransform)transform);
            if (_selectBeams != null)
            {
                _selectBeams.SetColor(_elementColor);
                _selectBeams.gameObject.SetActive(true);
            }
        }

        public void OnReselect()
        {
            if(viewType == ViewType.OnlyDisplay)
            {
                return;
            }
        }

        private void OnDestroy()
        {
            if (selectButton != null)
                selectButton.onSelectedChanged.RemoveListener(OnSelectedChanged);
        }


        public void SetSelected(bool isSelected)
        {
            if (selectButton != null)
                selectButton.SetSelected(isSelected);
        }

        public bool IsSelected()
        {
            return selectButton != null && selectButton.Selected;
        }
    }

    /// <summary>技能圈三色（主动橙/不可用米白/被动紫）=SkillIconView 组件级固有视觉（背包/战斗同色），
    /// 非战斗域全局配色——刻意不入 BattlePalette（本组件在 UI 层，读 Battle 域资产=反向依赖）。
    /// 归属随 B7 域内化（SkillIconView 归属定案）再统一裁决，已登记 docs/11。</summary>
    public static class SkillCircleColor
    {
        public static Color colorAvailable = "FF9800".FromHex();
        public static Color colorUnavailable = "ECE5D8".FromHex();
        public static Color colorPassive = "9C27B0".FromHex();
    }
}
