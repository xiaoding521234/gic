// ==================== SettingsScreen.Account.cs（账户设置 + 输入弹窗） ====================
using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
namespace GIC.UI
{


    public partial class SettingsScreen
    {
        private InputPopupDialog inputPopupInstance;
        /// <summary>上次执行成功的指令（回显：设置行值 + 弹窗预填，便于小改重跑）</summary>
        private string _lastCommand = "";

        private void InitAccountSettings()
        {
            var saveManager = _saveManager;
            string playerName = saveManager.CurrentSave.playerName;

            playerNameSetting.Setup("Name", playerName,
                onClick: () =>
                {
                    // 点击时实时读存档——闭包捕获 Init 时的局部变量会回显改名前的旧值（PetApiKey 同款）
                    ShowInputPanel(playerNameSetting, saveManager.CurrentSave.playerName, (newName) =>
                    {
                        saveManager.Modify(s => s.playerName = newName);   // 统一变更入口（2026-09-05 Modify 迁移）
                        playerNameSetting.UpdateValue(newName);
                    });
                },
                onValueConfirmed: null
            );
            playerNameSetting.Initialize();

            // 指令入口（2026-09-13 指令系统统一）：与 AI 派蒙 run_command 共用 CommandSystem；
            // 指令模式弹窗带 IDE 式补全（Tab 补词/方向键/点击，见 InputPopupDialog.CommandSuggest）
            commandSetting.Setup("Command", _lastCommand,
                onClick: () =>
                {
                    ShowInputPanel(commandSetting, _lastCommand, (newCommand) =>
                    {
                        var result = CommandSystem.Execute(newCommand);
                        PopupManager.Instance?.ShowToast(result.message);
                        if (result.ok)
                        {
                            _lastCommand = newCommand;   // 仅成功回显（失败保留上次，便于纠正重跑）
                            commandSetting.UpdateValue(_lastCommand);
                        }
                    }, null, 64, CommandSystem.Suggest);
                },
                onValueConfirmed: null,
                placeholderKey: "Input"
            );
            commandSetting.Initialize();
        }

        private void ShowInputPanel(ButtonSettingItem settingItem, string currentValue, Action<string> callback)
        {
            ShowInputPanel(settingItem, currentValue, callback, null);
        }

        /// <summary>带自定义标题键（空=按设置项推断——玩家名/指令的既有键）；maxChars=输入弹窗字符上限（0=沿用弹窗默认）；
        /// suggester 非空=指令模式（IDE 式补全）</summary>
        private void ShowInputPanel(ButtonSettingItem settingItem, string currentValue, Action<string> callback, string titleKey, int maxChars = 0,
            Func<string, List<CommandSuggestion>> suggester = null)
        {
            if (inputPopupPrefab == null) return;

            if (inputPopupInstance == null)
            {
                inputPopupInstance = Instantiate(inputPopupPrefab, transform);
            }

            if (string.IsNullOrEmpty(titleKey))
                titleKey = settingItem == playerNameSetting ? "ModifyName" : "InputCommand";
            inputPopupInstance.Show(titleKey, currentValue, callback, maxChars, suggester);
        }
    }
}
