// ==================== SettingsScreen.Pet.cs（派蒙设置：形态切换 + 连带关闭，docs/19 §6.4） ====================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using GIC.Framework;
using GIC.Tool; // TextEntry（同 Display/Account 各 partial 的 using 约定）

namespace GIC.UI
{
    public partial class SettingsScreen
    {
        /// <summary>派蒙形态枚举值（与 PlayerSaveData.petForm 对应：0=桌面版，1=游戏画面内版）</summary>
        private const int PET_FORM_DESKTOP = 0;
        private const int PET_FORM_INGAME = 1;

        /// <summary>当前平台是否可选桌面版（Win32 专属形态；安卓等平台恒游戏内版）</summary>
        public static bool 桌面版可用 =>
#if UNITY_STANDALONE_WIN
            true;
#else
            false;
#endif

        private void InitPetSettings()
        {
            InitPetFormSetting();
            InitPetCloseSetting();
        }

        /// <summary>派蒙形态：桌面版（仅 Windows）/ 游戏画面内版。切换即时生效（PetInGameHost 热切换；
        /// 桌面版在非 Windows 平台不进选项，读档侧对非法值钳为游戏内版）。</summary>
        private void InitPetFormSetting()
        {
            var options = new List<TextEntry>();
            int desktopIdx = -1, ingameIdx = -1;
            if (桌面版可用)
            {
                desktopIdx = options.Count;
                options.Add(new TextEntry(new LocalizedString("UIText", "PetFormDesktop"), ""));
            }
            ingameIdx = options.Count;
            options.Add(new TextEntry(new LocalizedString("UIText", "PetFormInGame"), ""));

            int current = 读取有效形态();
            int currentIndex = current == PET_FORM_DESKTOP ? desktopIdx : ingameIdx;

            petFormSetting.Setup("PetForm", options, ingameIdx, (index) =>
            {
                int form = index == desktopIdx ? PET_FORM_DESKTOP : PET_FORM_INGAME;
                _saveManager.CurrentSave.petForm = form;
                _saveManager.SaveGame();
                GIC.Pet.PetInGameHost.热切换形态(form);
            });
            petFormSetting.SetValue(currentIndex);
            petFormSetting.Initialize();
        }

        /// <summary>读档侧钳制：非 Windows 平台/非法值恒游戏内版（存 0 的老档在安卓上跑=钳 1）</summary>
        private int 读取有效形态()
        {
            int form = _saveManager.CurrentSave.petForm;
            if (form != PET_FORM_DESKTOP && form != PET_FORM_INGAME) form = PET_FORM_INGAME;
            if (form == PET_FORM_DESKTOP && !桌面版可用) form = PET_FORM_INGAME;
            return form;
        }

        /// <summary>关闭游戏连带关闭派蒙（仅桌面形态有意义——游戏内形态天然随进程销毁）。
        /// 选项与 PlayerSaveData.closePetOnExit 对应：0=开（随游戏退出），1=关（独立存活）。
        /// 2026-08-27 从"其它"栏挪入"派蒙"栏。</summary>
        private void InitPetCloseSetting()
        {
            var options = new List<TextEntry>
            {
                new TextEntry(new LocalizedString("UIText", "On"), ""),
                new TextEntry(new LocalizedString("UIText", "Off"), ""),
            };

            int current = _saveManager.CurrentSave.closePetOnExit ? 0 : 1;

            petCloseSetting.Setup("ClosePetOnExit", options, 0, (index) =>
            {
                _saveManager.CurrentSave.closePetOnExit = index == 0;
                _saveManager.SaveGame();
            });
            petCloseSetting.SetValue(current);
            petCloseSetting.Initialize();
        }
    }
}
