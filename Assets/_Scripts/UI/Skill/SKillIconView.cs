using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.UI
{


    public class SkillIconView : MonoBehaviour
    {
        public Image skillIcon;
        public Image skillCircle;
        public Image skillSelect;
        public Image skillBadge;
        public Toggle toggle;

        private ViewType viewType;
        private SkillConfig.SkillData skillData;
        private UnitConfig.UnitData unitData;
        private BaseSkill skill;

        public SkillDetailView skillDetailView;

        public void Awake()
        {
        
            toggle.onValueChanged.AddListener(OnToggleValueChanged);
            
            skillSelect.gameObject.SetActive(false);
            
        }

        public void InitWithData(SkillConfig.SkillData skillData, UnitConfig.UnitData unitData, ViewType viewType, SkillDetailView skillDetailView)
        {
            this.viewType = viewType;
            this.skillData = skillData;
            this.unitData = unitData;
            this.skillDetailView = skillDetailView;

            // 元素染色（2026-09-10 拍板：图案保持白色不染，底图染亮元素色；色值=配置文件）
            // 底图为白基底圆板，乘色后即完整元素亮色；无 unitData 回退物理灰防白底白图
            Color elementColor = unitData != null
                ? ElementFactionConfig.Instance.GetElementColor(unitData.selfElement)
                : ElementFactionConfig.Instance.GetElementColor(ElementType.Physical);
            if (skillIcon != null)
            {
                skillIcon.sprite = skillData.icon;
                skillIcon.color = Color.white;
            }
            if (skillBadge != null)
                skillBadge.color = elementColor;
                
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
            
            if (skillSelect != null)
            {
                skillSelect.gameObject.SetActive(false);
            }
        }

        public void InitWithSkill(BaseSkill skill, ViewType viewType, SkillDetailView skillDetailView)
        {
            this.viewType = viewType;
            this.skill = skill;
        }

        private void OnToggleValueChanged(bool isOn)
        {
            if(viewType == ViewType.OnlyDisplay)
            {
                return;
            }

            skillSelect.gameObject.SetActive(isOn);
            
            
            if (isOn && viewType == ViewType.Display)
            {
                skillDetailView.OpenPanel();
                skillDetailView.InitWithData(skillData, unitData,this);
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
            if (toggle != null)
                toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
        }

        
        public void SetSelected(bool isSelected)
        {
            toggle.isOn = isSelected;
        }
        
        public bool IsSelected()
        {
            return toggle.isOn;
        }
    }

    public static class SkillCircleColor
    {
        public static Color colorAvailable = "FF9800".FromHex();
        public static Color colorUnavailable = "ECE5D8".FromHex();
        public static Color colorPassive = "9C27B0".FromHex();
    }
}


