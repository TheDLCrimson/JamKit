using PrimeTween;
using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// A bobbing (optionally spinning and pulsing) marker driven by PrimeTween, with smooth reposition helpers
    /// that preserve the bob. Call <see cref="StartAnimation"/> to begin.
    /// </summary>
    /// <remarks>
    /// Extracted essentially verbatim from CarGoesAround's <c>FloatingCursor</c> (the audit flagged it as a
    /// polished, dependency-free drop-in); only namespaced and renamed to the package field conventions.
    /// </remarks>
    public class FloatingCursor : MonoBehaviour
    {
        [Header("Bobbing Animation Settings")]
        [SerializeField] private float _bobbingHeight = 0.5f;
        [SerializeField] private float _bobbingDuration = 1.5f;
        [SerializeField] private Ease _bobbingEase = Ease.InOutSine;

        [Header("Rotation")]
        [SerializeField] private bool _enableRotation = false;
        [SerializeField] private float _rotationSpeed = 45f; // degrees per second

        [Header("Scaling Pulse")]
        [SerializeField] private bool _enableScalePulse = false;
        [SerializeField] private float _pulseScale = 1.2f;
        [SerializeField] private float _pulseDuration = 0.8f;

        private Vector3 _originalPosition;
        private Vector3 _originalScale;
        private Tween _bobbingTween;
        private Tween _rotationTween;
        private Tween _scaleTween;

        /// <summary>Store the current transform as the base pose and start the enabled animations.</summary>
        public void StartAnimation()
        {
            _originalPosition = transform.position;
            _originalScale = transform.localScale;

            StartBobbingAnimation();

            if (_enableRotation) StartRotationAnimation();
            if (_enableScalePulse) StartScalePulseAnimation();
        }

        private void StartBobbingAnimation()
        {
            Vector3 targetPosition = _originalPosition + Vector3.up * _bobbingHeight;

            if (_bobbingTween.isAlive) _bobbingTween.Stop();

            _bobbingTween = Tween.Position(transform, targetPosition, _bobbingDuration, _bobbingEase, cycles: -1, CycleMode.Yoyo);
        }

        private void StartRotationAnimation()
        {
            if (_rotationTween.isAlive) _rotationTween.Stop();

            _rotationTween = Tween.Rotation(transform,
                transform.rotation * Quaternion.Euler(0, 360f, 0),
                360f / _rotationSpeed,
                Ease.Linear, cycles: -1);
        }

        private void StartScalePulseAnimation()
        {
            if (_scaleTween.isAlive) _scaleTween.Stop();

            Vector3 targetScale = _originalScale * _pulseScale;
            _scaleTween = Tween.Scale(transform, targetScale, _pulseDuration, Ease.InOutQuad, cycles: -1, CycleMode.Yoyo);
        }

        /// <summary>Change the bob height and restart the bob.</summary>
        public void SetBobbingIntensity(float intensity)
        {
            if (_bobbingTween.isAlive) _bobbingTween.Stop();
            _bobbingHeight = intensity;
            StartBobbingAnimation();
        }

        /// <summary>Change the bob speed (higher = faster) and restart the bob.</summary>
        public void SetBobbingSpeed(float speed)
        {
            if (_bobbingTween.isAlive) _bobbingTween.Stop();
            _bobbingDuration = 1f / speed;
            StartBobbingAnimation();
        }

        /// <summary>Stop every animation and snap back to the stored base pose.</summary>
        public void StopAllAnimations()
        {
            if (_bobbingTween.isAlive) _bobbingTween.Stop();
            if (_rotationTween.isAlive) _rotationTween.Stop();
            if (_scaleTween.isAlive) _scaleTween.Stop();

            transform.position = _originalPosition;
            transform.localScale = _originalScale;
        }

        private void OnDestroy()
        {
            if (_bobbingTween.isAlive) _bobbingTween.Stop();
            if (_rotationTween.isAlive) _rotationTween.Stop();
            if (_scaleTween.isAlive) _scaleTween.Stop();
        }

        /// <summary>Smoothly move to a new position, then continue bobbing from there.</summary>
        public void UpdateCursorPosition(Vector3 newPosition)
        {
            _originalPosition = newPosition;

            if (_bobbingTween.isAlive) _bobbingTween.Stop();

            Tween.Position(transform, newPosition, 0.1f, Ease.OutQuad).OnComplete(StartBobbingAnimation);
        }

        /// <summary>Instantly move to a new position while preserving the current bob offset.</summary>
        public void UpdateCursorPositionInstant(Vector3 newPosition)
        {
            Vector3 currentOffset = transform.position - _originalPosition;
            _originalPosition = newPosition;
            transform.position = _originalPosition + currentOffset;
        }
    }
}
