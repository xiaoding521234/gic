// ==================== SettingsScreen.Battle.cs（战斗设置：血条颜色——2026-10-15 拍板
// 「设置界面新加一个类别战斗，里面加一条设置，血条颜色，提供两个选项：1.玩家色（即当前）2.三色
// （参照王者荣耀：自己绿/队友蓝/敌红，作为默认设置）」；仅影响血条填充色，底座颜色等不动） ====================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using GIC.Data;
using GIC.Tool;
namespace GIC.UI
{


    public partial class SettingsScreen
    {
        private void InitBattleSettings()
        {
            InitHpBarColorSetting();
        }

        /// <summary>血条颜色双选项（2026-10-15 拍板）：
        /// 显示序=拍板列举序〔1.玩家色 2.三色〕；存档值=HpBarColorMode 枚举 int
        /// 〔0=三色（默认——参照王者荣耀：自己的单位绿/队友全蓝/敌人全红，玩家不用记忆哪些颜色
        /// 是队友；JsonUtility 缺字段反序列化为 0=老档自动落默认档）〕，1=玩家色（归属玩家色）。
        /// 仅影响 BattleOverheadBars 血条填充色——底座圆盘/描边/弧光链路不动；
        /// 设置界面不可在战斗中打开（战斗内设置入口=退出确认弹窗），改动下一场生效</summary>
        private void InitHpBarColorSetting()
        {
            var options = new List<TextEntry>
            {
                new TextEntry(new LocalizedString("UIText", "HpBarColorPlayerColor"), ""),    // 玩家色（显示序 0）
                new TextEntry(new LocalizedString("UIText", "HpBarColorTriColor"), ""),       // 三色（显示序 1）
            };

            // 存档枚举值 → 显示序映射（三色=默认=序 1，玩家色=序 0）
            int saved = _saveManager.CurrentSave.settings.hpBarColorMode;
            int displayIndex = saved == (int)HpBarColorMode.PlayerColor ? 0 : 1;

            hpBarColorSetting.Setup("HpBarColor", options, displayIndex, (index) =>
            {
                // 显示序 → 存档枚举值（统一变更入口，2026-09-05 Modify 迁移口径）
                _saveManager.Modify(s => s.settings.hpBarColorMode =
                    index == 0 ? (int)HpBarColorMode.PlayerColor : (int)HpBarColorMode.ThreeColor);
            });
            hpBarColorSetting.Initialize();
        }
    }
}
