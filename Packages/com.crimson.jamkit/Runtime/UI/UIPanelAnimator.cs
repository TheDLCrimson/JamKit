using PrimeTween;
using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Reusable fade + scale show/hide animation for a UI panel, driven by <see cref="Show"/> / <see cref="Hide"/>
    /// / <see cref="Toggle"/>. Toggling is caller-driven (no built-in key polling).
    /// </summary>
    /// <remarks>
    /// Generalized from FurnishMyHome's <c>MenuUI</c> tween recipe. The donor's <c>isOpen</c> desync (state was
    /// written both synchronously and again in the tween's <c>OnComplete</c>, so spamming the toggle mid-animation
    /// left <c>isOpen</c> out of sync with what was on screen) is fixed here: <see cref="_isOpen"/> is the single
    /// authoritative flag, set synchronously at the start of each transition; any in-flight sequence is stopped
    /// before a new one starts; and the tween's completion callback never touches <see cref="_isOpen"/> - it only
    /// deactivates the panel object after a hide, which a subsequent <see cref="Show"/> pre-empts by stopping the
    /// sequence (PrimeTween does not fire <c>OnComplete</c> on <c>Stop</c>).
    /// </remarks>
    public class UIPanelAnimator : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("CanvasGroup faded between 0 and 1.")]
        [SerializeField] private CanvasGroup _canvasGroup;

        [Tooltip("Panel root that is scaled and (optionally) deactivated. Defaults to this transform.")]
        [SerializeField] private RectTransform _panel;

        [Header("Animation")]
        [SerializeField] private float _duration = 0.3f;

        [Tooltip("Scale of the panel while hidden (it animates to 1 while showing).")]
        [SerializeField] private float _hiddenScale = 0.95f;

        [Tooltip("Use unscaled time so the panel animates even while the game is paused.")]
        [SerializeField] private bool _useUnscaledTime = true;

        [Tooltip("Deactivate the panel GameObject once a hide animation finishes.")]
        [SerializeField] private bool _deactivateOnHide = true;

        [Tooltip("Start hidden (deactivated, transparent) on Awake.")]
        [SerializeField] private bool _startHidden = true;

        private bool _isOpen;
        private Sequence _sequence;

        /// <summary>Authoritative open/opening state. Flipped synchronously by <see cref="Show"/>/<see cref="Hide"/>.</summary>
        public bool IsOpen => _isOpen;

        private void Awake()
        {
            if (!_canvasGroup) _canvasGroup = GetComponent<CanvasGroup>();
            if (!_panel) _panel = transform as RectTransform;

            if (_startHidden) ApplyHiddenImmediate();
        }

        /// <summary>Animate the panel in. No-op if it is already open (guards against toggle spam).</summary>
        public void Show()
        {
            if (_isOpen) return;
            _isOpen = true;

            if (_sequence.isAlive) _sequence.Stop();

            if (_panel) _panel.gameObject.SetActive(true);
            if (_panel) _panel.localScale = Vector3.one * _hiddenScale;
            if (_canvasGroup)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }

            _sequence = Sequence.Create(useUnscaledTime: _useUnscaledTime);
            if (_canvasGroup) _sequence.Group(Tween.Alpha(_canvasGroup, 1f, _duration, Ease.OutQuad));
            if (_panel) _sequence.Group(Tween.Scale(_panel, 1f, _duration, Ease.OutBack));
        }

        /// <summary>Animate the panel out. No-op if it is already closed (guards against toggle spam).</summary>
        public void Hide()
        {
            if (!_isOpen) return;
            _isOpen = false;

            if (_sequence.isAlive) _sequence.Stop();

            if (_canvasGroup)
            {
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }

            _sequence = Sequence.Create(useUnscaledTime: _useUnscaledTime);
            if (_canvasGroup) _sequence.Group(Tween.Alpha(_canvasGroup, 0f, _duration, Ease.InQuad));
            if (_panel) _sequence.Group(Tween.Scale(_panel, _hiddenScale, _duration, Ease.InBack));

            // View-only side effect; never touches _isOpen. A Show() before this fires stops the sequence,
            // and PrimeTween does not invoke OnComplete on Stop, so the panel is not wrongly deactivated.
            if (_deactivateOnHide && _panel)
            {
                _sequence.OnComplete(() => _panel.gameObject.SetActive(false));
            }
        }

        /// <summary>Show if closed, hide if open.</summary>
        public void Toggle()
        {
            if (_isOpen) Hide();
            else Show();
        }

        private void ApplyHiddenImmediate()
        {
            _isOpen = false;
            if (_sequence.isAlive) _sequence.Stop();

            if (_canvasGroup)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }

            if (_panel)
            {
                _panel.localScale = Vector3.one * _hiddenScale;
                if (_deactivateOnHide) _panel.gameObject.SetActive(false);
            }
        }
    }
}
