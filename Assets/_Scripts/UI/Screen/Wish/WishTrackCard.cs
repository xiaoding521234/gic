using UnityEngine;

namespace GIC.UI
{
    /// <summary>
    /// 卡道上移动的单张卡牌
    /// </summary>
    public class WishTrackCard : MonoBehaviour
    {
        private RectTransform _rect;
        private Vector2 _startPos;
        private Vector2 _targetPos;
        private float _duration;
        private float _elapsed;
        private bool _paused;

        public void Init(Vector2 startPos, Vector2 targetPos, float duration, float initialProgress = 0f)
        {
            _rect = GetComponent<RectTransform>();
            _startPos = startPos;
            _targetPos = targetPos;
            _duration = duration;
            _elapsed = initialProgress * duration;
            _paused = false;
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
        }

        private void Update()
        {
            if (_rect == null || _paused) return;
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            _rect.anchoredPosition = Vector2.Lerp(_startPos, _targetPos, t);

            if (t >= 1f)
                gameObject.SetActive(false);
        }
    }
}
