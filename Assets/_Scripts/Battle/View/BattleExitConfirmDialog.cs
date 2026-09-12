using System;
using UnityEngine;
using UnityEngine.UI;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 退出战斗确认弹窗（2026-09-12 用户拍板：右键/ESC 不应直接退出，须确认）。
    /// 程序化灰盒 UI（无 prefab 可改，构建时设值为唯一路径；B6 正式战斗 HUD 时换 TextCombiner+本地化弹窗）。
    /// 挂 IClosable（注册后位于 closable 栈顶）：弹窗打开期间 ESC/右键 = 取消弹窗（不退出战斗）；
    /// 打开期间压 BattleExitConfirm 输入锁（冻结相机与棋盘交互）。
    /// </summary>
    public class BattleExitConfirmDialog : MonoBehaviour, IClosable
    {
        [Autowired] private InputManager _inputManager;

        private Action _onConfirm;
        private bool _destroying;

        public bool IsOpen => !_destroying && gameObject.activeInHierarchy;

        /// <summary>
        /// 显示退出确认弹窗；onConfirm 在用户确认后回调（弹窗自毁，回调负责真正退出）
        /// </summary>
        public static BattleExitConfirmDialog Show(Transform parent, string message, Action onConfirm)
        {
            var rootGo = new GameObject("BattleExitConfirmDialog");
            rootGo.transform.SetParent(parent, false);

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(rootGo.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGo.AddComponent<GraphicRaycaster>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);
            scaler.matchWidthOrHeight = 0.5f;

            // 全屏暗化遮罩（吃射线，阻断背后界面）
            var dimGo = new GameObject("Dim");
            var dimRect = dimGo.AddComponent<RectTransform>();
            dimRect.SetParent(canvasGo.transform, false);
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
            var dim = dimGo.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            dim.raycastTarget = true;

            // 居中面板
            var panelGo = new GameObject("Panel");
            var panelRect = panelGo.AddComponent<RectTransform>();
            panelRect.SetParent(canvasGo.transform, false);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(620f, 260f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.1f, 0.12f, 0.16f, 0.97f);

            var dialog = rootGo.AddComponent<BattleExitConfirmDialog>();
            dialog._onConfirm = onConfirm;
            dialog.BuildContent(panelRect, message);

            return dialog;
        }

        private void BuildContent(RectTransform panelRect, string message)
        {
            Wargame.Instance?.Context?.Inject(this);
            _inputManager?.RegisterClosable(this);
            InputLocks.Push(this, InputLockReason.BattleExitConfirm);

            // 消息文本（legacy Text：灰盒调试级，B6 换 TextCombiner）
            var msgGo = new GameObject("Message");
            var msgRect = msgGo.AddComponent<RectTransform>();
            msgRect.SetParent(panelRect, false);
            msgRect.anchorMin = new Vector2(0f, 0.5f);
            msgRect.anchorMax = new Vector2(1f, 1f);
            msgRect.offsetMin = new Vector2(24f, 24f);
            msgRect.offsetMax = new Vector2(-24f, -24f);
            var msgText = msgGo.AddComponent<Text>();
            msgText.text = message;
            msgText.alignment = TextAnchor.MiddleCenter;
            msgText.fontSize = 30;
            msgText.color = new Color(0.95f, 0.95f, 0.98f);
            msgText.font = GetLegacyFont();
            msgText.raycastTarget = false;

            // 按钮行
            var rowGo = new GameObject("Buttons");
            var rowRect = rowGo.AddComponent<RectTransform>();
            rowRect.SetParent(panelRect, false);
            rowRect.anchorMin = new Vector2(0f, 0f);
            rowRect.anchorMax = new Vector2(1f, 0.42f);
            rowRect.offsetMin = new Vector2(24f, 20f);
            rowRect.offsetMax = new Vector2(-24f, -16f);
            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 20f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childControlWidth = false;

            CreateButton(rowRect.transform, "确定退出", new Color(0.55f, 0.18f, 0.16f), 200f, ConfirmAndExit);
            CreateButton(rowRect.transform, "继续战斗", new Color(0.2f, 0.3f, 0.42f), 200f, Close);
        }

        private static Text CreateButton(Transform parent, string label, Color color, float width, Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, 60f);
            var image = go.AddComponent<Image>();
            image.color = color;
            var button = go.AddComponent<Button>();
            button.onClick.AddListener(() => onClick?.Invoke());

            var txtGo = new GameObject("Text");
            var txtRect = txtGo.AddComponent<RectTransform>();
            txtRect.SetParent(go.transform, false);
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = txtRect.offsetMax = Vector2.zero;
            var txt = txtGo.AddComponent<Text>();
            txt.text = label;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontSize = 24;
            txt.color = Color.white;
            txt.font = GetLegacyFont();
            txt.raycastTarget = false;
            return txt;
        }

        private static Font GetLegacyFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
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
