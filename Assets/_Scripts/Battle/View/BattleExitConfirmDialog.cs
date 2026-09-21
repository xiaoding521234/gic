using System;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 退出战斗确认弹窗（2026-09-12 用户拍板：右键/ESC 不应直接退出，须确认）。
    /// 2026-09-22 全项目统一批次转正 + prefab 化：结构=Resources/Prefabs/Battle/BattleExitConfirmDialog.prefab
    /// （一次性迁移工具烘焙；文案=TextCombiner 本地化键 Battle_ExitConfirmMsg/Confirm/Continue、
    /// 配色=BattlePalette 收口——docs/11 登记项收口）；运行时只实例化+注册+活色。
    /// 挂 IClosable（注册后位于 closable 栈顶）：弹窗打开期间 ESC/右键 = 取消弹窗（不退出战斗）；
    /// 打开期间压 BattleExitConfirm 输入锁（冻结相机与棋盘交互）。
    /// </summary>
    public class BattleExitConfirmDialog : MonoBehaviour, IClosable
    {
        private static BattlePalette Palette => BattlePalette.Instance;

        [Autowired] private InputManager _inputManager;

        private Action _onConfirm;
        private bool _destroying;

        public bool IsOpen => !_destroying && gameObject.activeInHierarchy;

        /// <summary>
        /// 显示退出确认弹窗；onConfirm 在用户确认后回调（弹窗自毁，回调负责真正退出）。
        /// 2026-09-22 prefab 化：结构=Resources/Prefabs/Battle/BattleExitConfirmDialog.prefab
        /// （迁移工具烘焙；文案/配色已在 prefab 内转正），本方法只实例化+运行时注册。
        /// </summary>
        public static BattleExitConfirmDialog Show(Transform parent, Action onConfirm)
        {
            var prefab = Resources.Load<GameObject>("Prefabs/Battle/BattleExitConfirmDialog");
            if (prefab == null)
            {
                GICLog.Error("[BattleExitConfirmDialog] prefab 未找到（Resources/Prefabs/Battle/BattleExitConfirmDialog）");
                return null;
            }
            var rootGo = Instantiate(prefab, parent, false);
            rootGo.name = "BattleExitConfirmDialog";
            var dialog = rootGo.GetComponent<BattleExitConfirmDialog>();
            if (dialog != null) dialog.Init(onConfirm);
            return dialog;
        }

        /// <summary>运行时注册（注入/可关闭栈/输入锁）+ Palette 活色（烘焙色仅兜底）；与结构装配分离</summary>
        private void Init(Action onConfirm)
        {
            _onConfirm = onConfirm;
            Wargame.Instance?.Context?.Inject(this);
            _inputManager?.RegisterClosable(this);
            InputLocks.Push(this, InputLockReason.BattleExitConfirm);

            // Palette 活色（改资产随下次弹出生效）
            var panel = transform.Find("Canvas/Panel")?.GetComponent<Image>();
            if (panel != null) panel.color = Palette.按钮底盘;
            var msg = transform.Find("Canvas/Panel/Message")?.GetComponent<TMPro.TextMeshProUGUI>();
            if (msg != null) msg.color = Palette.文字米白;
            RecolorButton("Confirm", Palette.敌方主色);
            RecolorButton("Continue", Palette.我方主色);
        }

        private void RecolorButton(string name, Color baseColor)
        {
            var btn = transform.Find($"Canvas/Panel/Buttons/Btn_{name}")?.GetComponent<Image>();
            if (btn != null) btn.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.55f);
            var txt = transform.Find($"Canvas/Panel/Buttons/Btn_{name}/Text")?.GetComponent<TMPro.TextMeshProUGUI>();
            if (txt != null) txt.color = Palette.文字米白;
        }

        /// <summary>确认退出：先自毁弹窗（释放锁/注销），再回调真正退出</summary>
        public void ConfirmAndExit()
        {
            if (_destroying) return;
            var cb = _onConfirm;
            _onConfirm = null;
            Close();          // 自毁（含锁释放/注销）
            cb?.Invoke();     // 回调真正退出（Destroy 延迟到帧末，不影响回调内逻辑）
        }

        /// <summary>取消（继续战斗）；也是 IClosable 的 ESC/右键路径</summary>
        public void Close()
        {
            if (_destroying) return;
            _destroying = true;
            _inputManager?.UnregisterClosable(this);
            InputLocks.PopAll(this);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            _inputManager?.UnregisterClosable(this);
            InputLocks.PopAll(this);
        }
    }
}
