// ==================== SettingsScreen.Control.cs（操作设置：按键绑定） ====================
using UnityEngine;
using GIC.Framework;
using GIC.Data;
namespace GIC.UI
{


    public partial class SettingsScreen
    {
        private void InitControlSettings()
        {
            closeUIPrimaryKeyItem?.Setup(KeyAction.CloseUI, 0, "CloseUI");
            closeUIPrimaryKeyItem?.Initialize();

            closeUISecondaryKeyItem?.Setup(KeyAction.CloseUI, 1, "CloseUI");
            closeUISecondaryKeyItem?.Initialize();

            confirmPrimaryKeyItem?.Setup(KeyAction.Confirm, 0, "Confirm");
            confirmPrimaryKeyItem?.Initialize();

            confirmSecondaryKeyItem?.Setup(KeyAction.Confirm, 1, "Confirm");
            confirmSecondaryKeyItem?.Initialize();
        }
    }
}
