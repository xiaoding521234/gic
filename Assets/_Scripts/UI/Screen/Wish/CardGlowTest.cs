using UnityEngine;
using UnityEngine.UI;

namespace GIC.UI
{
    /// <summary>
    /// 测试 CardGlowOverlay 效果。
    /// 自动在屏幕中央创建一张卡牌 + 叠加层，按 ↑/↓ 调节亮度，空格切换自动循环。
    /// </summary>
    public class CardGlowTest : MonoBehaviour
    {
        private CardGlowOverlay _overlay;
        private float _intensity;
        private bool _autoCycle = true;

        private const float MaxIntensity = 3f;
        private const float Speed = 1.5f;

        private void Start()
        {
            // Canvas
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            // 卡牌容器
            var cardGo = new GameObject("TestCard", typeof(RectTransform), typeof(Image));
            cardGo.transform.SetParent(canvasGo.transform, false);
            var cardRect = cardGo.GetComponent<RectTransform>();
            cardRect.sizeDelta = new Vector2(300, 420);
            cardRect.anchoredPosition = Vector2.zero;

            var cardImg = cardGo.GetComponent<Image>();
            cardImg.color = new Color(0.15f, 0.22f, 0.38f, 1f);
            cardImg.raycastTarget = false;

            // 卡牌标题（方便辨认）
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(cardRect, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(280, 60);
            labelRect.anchoredPosition = Vector2.zero;
            var label = labelGo.GetComponent<Text>();
            label.text = "↑↓ 调亮度\n空格 自动循环";
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.fontSize = 28;
            label.raycastTarget = false;

            // 创建叠加层
            _overlay = CardGlowOverlay.Create(cardRect);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                _autoCycle = !_autoCycle;

            if (_autoCycle)
            {
                _intensity = Mathf.PingPong(Time.time * Speed, MaxIntensity);
            }
            else
            {
                if (Input.GetKey(KeyCode.UpArrow))
                    _intensity = Mathf.Min(_intensity + Time.deltaTime * Speed * 2f, MaxIntensity);
                if (Input.GetKey(KeyCode.DownArrow))
                    _intensity = Mathf.Max(_intensity - Time.deltaTime * Speed * 2f, 0f);
            }

            if (_overlay != null)
                _overlay.SetGlow(new Color(1f, 0.95f, 0.6f, 1f), _intensity);
        }

        private void OnGUI()
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 32;
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(20, 20, 500, 40), $"Intensity: {_intensity:F2}  | 自动: {_autoCycle}", style);
        }
    }
}
