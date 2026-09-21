using System;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

namespace JamKit
{
    /// <summary>
    /// Full-screen fade/flash overlay: one of the four blessed <see cref="Singleton{T}"/> services, provided
    /// wired (Canvas + <see cref="CanvasGroup"/> + <see cref="Image"/>) by the <see cref="Bootstrap"/> prefab.
    /// Runs on unscaled time so effects keep animating while the game is paused, and owns the
    /// <see cref="SceneLoader"/> fade so transitions are covered.
    /// </summary>
    /// <remarks>
    /// Extracted from NineTailsProject's <c>UIScreenEffects</c> and ported from DOTween to PrimeTween. The
    /// donor's hand-rolled singleton (its own <c>Instance</c> + <c>DontDestroyOnLoad</c>) is dropped in favour
    /// of the blessed <see cref="Singleton{T}"/>: persistence comes from the Bootstrap root's
    /// <c>DontDestroyOnLoad</c>, so <see cref="Singleton{T}"/>'s own <c>Dont Destroy On Load</c> flag stays off.
    /// <para/>
    /// Scene-transition fade: this service owns <see cref="SceneLoader.BeforeSceneLoad"/> (fade to opaque, then
    /// invoke the continuation that begins the load) and subscribes to <see cref="SceneLoader.AfterSceneLoad"/>
    /// (fade back in). Because the cover completes before the load begins, the hard cut is never seen.
    /// <para/>
    /// <b>Transition ownership (fixes a post-M3 review deadlock).</b> The scene-transition cover/uncover fade
    /// and ordinary ad-hoc effects (<see cref="Flash"/>, <see cref="FadeTo"/>, the flash presets, <see cref="Stop"/>)
    /// no longer share one <c>Sequence</c> field. A transition fade runs on its own private <see cref="_transitionSequence"/>
    /// and is never touched by an ad-hoc call, so an ad-hoc effect can no longer <c>Stop()</c> the transition's
    /// sequence out from under it and silently drop the <see cref="SceneLoader"/> continuation (PrimeTween's
    /// <c>Sequence.Stop()</c> is documented to ignore callbacks, so a shared sequence made this possible before).
    /// <para/>
    /// While a transition is covering the screen (<see cref="_transitionActive"/> true, set for the entire
    /// cover-to-uncover window, not just while a tween is actively running), every public ad-hoc entry point -
    /// <see cref="FadeTo"/>, <see cref="Flash"/> and its presets, and <see cref="Stop"/> - is a documented no-op:
    /// it logs a warning, invokes any caller-supplied <c>onComplete</c> immediately (so a caller waiting on that
    /// callback is never left hanging), and does not touch the overlay. The transition exclusively owns the
    /// shared <see cref="CanvasGroup"/>/<see cref="Image"/> for the duration of the cover; a queued or partial
    /// ad-hoc effect fighting the cover for the same properties would be visually undefined, so this is a hard
    /// rule, not a race. Symmetrically, when a transition cover begins it takes ownership from any in-flight
    /// ad-hoc effect: that effect's sequence is stopped (its own completion callback, if any, is not invoked -
    /// being cut short by a scene change is expected, ordinary behaviour, not a hidden broken state, since
    /// nothing program-critical depends on an ad-hoc flash's completion) and the overlay colour is reset to the
    /// authored default so the cover is never accidentally tinted by whatever effect it interrupted.
    /// <para/>
    /// <b>Exactly-once completion.</b> The private <see cref="RunTransitionFade"/> helper used by both transition
    /// phases guarantees its <c>onPhaseComplete</c> callback runs exactly once: either synchronously, immediately,
    /// if the overlay references are missing (so a broken/unwired overlay can never leave <see cref="SceneLoader"/>
    /// waiting forever on a continuation that will never arrive), or exactly once from the tween's own
    /// <c>OnComplete</c> otherwise. These two paths are mutually exclusive by construction (an early return before
    /// the sequence is ever created).
    /// </remarks>
    public class UIScreenEffects : Singleton<UIScreenEffects>
    {
        [Header("References")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _image;

        [Header("Scene Transition")]
        [Tooltip("Fade duration (seconds, unscaled) used to cover/uncover a SceneLoader transition.")]
        [SerializeField] private float _transitionFadeDuration = 0.35f;

        /// <summary>Sequence for ordinary ad-hoc effects (Flash/FadeTo callers outside the transition).</summary>
        private Sequence _currentSequence;

        /// <summary>Sequence exclusively owned by the scene-transition cover/uncover fade. Never touched by an ad-hoc call.</summary>
        private Sequence _transitionSequence;

        /// <summary>
        /// True for the entire scene-transition window: from the moment the cover fade starts until the
        /// uncover fade finishes (or immediately, if the overlay references are missing). Ad-hoc effect calls
        /// are rejected while this is true.
        /// </summary>
        private bool _transitionActive;

        private Color _defaultColor = Color.white;

        /// <summary>
        /// Called by <see cref="Bootstrap"/> in explicit order. Resolves the overlay references, starts fully
        /// transparent and non-blocking, and records the authored image colour as the restore colour.
        /// </summary>
        public void Initialize()
        {
            if (!_canvasGroup) _canvasGroup = GetComponent<CanvasGroup>();
            if (!_image) _image = GetComponent<Image>();

            if (_canvasGroup)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
            }

            if (_image) _defaultColor = _image.color;
        }

        private void OnEnable()
        {
            // Single-owner cover hook: fade to opaque, then run SceneLoader's continuation to begin the load.
            SceneLoader.BeforeSceneLoad = HandleBeforeSceneLoad;
            SceneLoader.AfterSceneLoad += HandleAfterSceneLoad;
        }

        private void OnDisable()
        {
            // Only relinquish the single-owner hook if we still own it.
            if (SceneLoader.BeforeSceneLoad == HandleBeforeSceneLoad) SceneLoader.BeforeSceneLoad = null;
            SceneLoader.AfterSceneLoad -= HandleAfterSceneLoad;
        }

        private void HandleBeforeSceneLoad(Action onCovered)
        {
            _transitionActive = true;

            // The transition takes exclusive ownership of the overlay from here. Stop any in-flight ad-hoc
            // effect (its completion callback, if any, is intentionally not invoked - see remarks) and reset
            // the colour so the cover is never tinted by whatever it interrupted.
            if (_currentSequence.isAlive) _currentSequence.Stop();
            if (_image) _image.color = _defaultColor;

            RunTransitionFade(1f, _transitionFadeDuration, onCovered);
        }

        private void HandleAfterSceneLoad()
        {
            RunTransitionFade(0f, _transitionFadeDuration, () => _transitionActive = false);
        }

        /// <summary>
        /// Runs the transition-owned cover (<paramref name="targetAlpha"/> 1) or uncover (0) fade on
        /// <see cref="_transitionSequence"/>, exclusive of any ad-hoc effect. Guarantees
        /// <paramref name="onPhaseComplete"/> runs exactly once: immediately if the overlay references are
        /// missing (never leaves <see cref="SceneLoader"/> waiting on a continuation that will never come), or
        /// once from the tween's own completion otherwise.
        /// </summary>
        private void RunTransitionFade(float targetAlpha, float duration, Action onPhaseComplete)
        {
            if (_transitionSequence.isAlive) _transitionSequence.Stop();

            if (!_canvasGroup || !_image)
            {
                onPhaseComplete?.Invoke();
                return;
            }

            if (targetAlpha > 0f) _canvasGroup.blocksRaycasts = true;

            Sequence sequence = Sequence.Create(useUnscaledTime: true);
            sequence.Chain(Tween.Alpha(_canvasGroup, targetAlpha, duration, useUnscaledTime: true));
            sequence.OnComplete(() =>
            {
                if (targetAlpha <= 0f) _canvasGroup.blocksRaycasts = false;
                onPhaseComplete?.Invoke();
            });

            _transitionSequence = sequence;
        }

        #region Core Methods

        /// <summary>
        /// Fade to a target alpha with optional colour change; runs on unscaled time. No-op while a scene
        /// transition is covering the screen (see class remarks): logs a warning, invokes
        /// <paramref name="onComplete"/> immediately, and leaves the overlay untouched.
        /// </summary>
        public Sequence FadeTo(float targetAlpha, float duration, Color? color = null, float effectStartDelay = 0f, float effectEndDelay = 0f, bool restoreColor = false, Action onComplete = null)
        {
            if (_transitionActive)
            {
                Debug.LogWarning("UIScreenEffects.FadeTo ignored - a scene transition is covering the screen.");
                onComplete?.Invoke();
                return default;
            }

            if (_currentSequence.isAlive) _currentSequence.Stop();

            if (!_canvasGroup || !_image)
            {
                onComplete?.Invoke();
                return default;
            }

            if (color.HasValue) _image.color = color.Value;

            if (targetAlpha > 0f) _canvasGroup.blocksRaycasts = true;

            Sequence sequence = Sequence.Create(useUnscaledTime: true);

            if (effectStartDelay > 0f) sequence.ChainDelay(effectStartDelay);

            sequence.Chain(Tween.Alpha(_canvasGroup, targetAlpha, duration, useUnscaledTime: true));

            if (effectEndDelay > 0f) sequence.ChainDelay(effectEndDelay);

            sequence.OnComplete(() =>
            {
                if (targetAlpha <= 0f) _canvasGroup.blocksRaycasts = false;
                if (restoreColor && color.HasValue) _image.color = _defaultColor;
                onComplete?.Invoke();
            });

            _currentSequence = sequence;
            return _currentSequence;
        }

        /// <summary>
        /// Flash the screen a number of times with an optional colour; runs on unscaled time. No-op while a
        /// scene transition is covering the screen (see class remarks): logs a warning, invokes
        /// <paramref name="onComplete"/> immediately, and leaves the overlay untouched.
        /// </summary>
        public Sequence Flash(int flashCount, float flashDuration, float maxAlpha = 1f, Color? color = null, float effectStartDelay = 0f, float effectEndDelay = 0f, bool restoreColor = true, Action onComplete = null)
        {
            if (_transitionActive)
            {
                Debug.LogWarning("UIScreenEffects.Flash ignored - a scene transition is covering the screen.");
                onComplete?.Invoke();
                return default;
            }

            if (_currentSequence.isAlive) _currentSequence.Stop();

            if (!_canvasGroup || !_image)
            {
                onComplete?.Invoke();
                return default;
            }

            if (color.HasValue) _image.color = color.Value;

            _canvasGroup.blocksRaycasts = true;

            Sequence sequence = Sequence.Create(useUnscaledTime: true);

            if (effectStartDelay > 0f) sequence.ChainDelay(effectStartDelay);

            float halfDuration = flashDuration / 2f;

            for (int i = 0; i < flashCount; i++)
            {
                sequence.Chain(Tween.Alpha(_canvasGroup, maxAlpha, halfDuration, useUnscaledTime: true));
                sequence.Chain(Tween.Alpha(_canvasGroup, 0f, halfDuration, useUnscaledTime: true));
            }

            if (effectEndDelay > 0f) sequence.ChainDelay(effectEndDelay);

            sequence.OnComplete(() =>
            {
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.alpha = 0f;
                if (restoreColor) _image.color = _defaultColor;
                onComplete?.Invoke();
            });

            _currentSequence = sequence;
            return _currentSequence;
        }

        #endregion

        #region Fade Helpers

        /// <summary>Fade to full opacity (cover the screen).</summary>
        public Sequence FadeOut(float duration, Color? color = null, float effectStartDelay = 0f, float effectEndDelay = 0f, Action onComplete = null)
        {
            return FadeTo(1f, duration, color, effectStartDelay, effectEndDelay, false, onComplete);
        }

        /// <summary>Fade to transparent (uncover the screen).</summary>
        public Sequence FadeIn(float duration, bool restoreColor = false, float effectStartDelay = 0f, float effectEndDelay = 0f, Action onComplete = null)
        {
            return FadeTo(0f, duration, null, effectStartDelay, effectEndDelay, restoreColor, onComplete);
        }

        /// <summary>Fade to full opacity with a specific colour.</summary>
        public Sequence FadeOutTo(Color color, float duration, float effectStartDelay = 0f, float effectEndDelay = 0f, Action onComplete = null)
        {
            return FadeTo(1f, duration, color, effectStartDelay, effectEndDelay, false, onComplete);
        }

        #endregion

        #region Flash Presets

        /// <summary>Quick single flash with a custom colour.</summary>
        public Sequence QuickFlash(Color? color = null, Action onComplete = null) => Flash(1, 0.2f, 1f, color, 0f, 0f, true, onComplete);

        /// <summary>Damage flash - red.</summary>
        public Sequence DamageFlash(Action onComplete = null) => Flash(1, 0.15f, 0.6f, Color.red, 0f, 0f, true, onComplete);

        /// <summary>Heal flash - green.</summary>
        public Sequence HealFlash(Action onComplete = null) => Flash(1, 0.2f, 0.5f, Color.green, 0f, 0f, true, onComplete);

        /// <summary>Warning flash - yellow, twice.</summary>
        public Sequence WarningFlash(Action onComplete = null) => Flash(2, 0.2f, 0.7f, Color.yellow, 0f, 0f, true, onComplete);

        /// <summary>Critical flash - intense red, three times.</summary>
        public Sequence CriticalFlash(Action onComplete = null) => Flash(3, 0.15f, 0.8f, new Color(1f, 0f, 0f), 0f, 0f, true, onComplete);

        /// <summary>Stun flash - white, rapid, four times.</summary>
        public Sequence StunFlash(Action onComplete = null) => Flash(4, 0.1f, 0.9f, Color.white, 0f, 0f, true, onComplete);

        #endregion

        #region Utility

        /// <summary>Set the overlay colour and record it as the restore colour.</summary>
        public void SetColor(Color color)
        {
            if (_image)
            {
                _image.color = color;
                _defaultColor = color;
            }
        }

        /// <summary>Get the current overlay colour.</summary>
        public Color GetColor() => _image ? _image.color : _defaultColor;

        /// <summary>
        /// Immediately stop any active ad-hoc effect, optionally resetting to fully transparent. No-op while a
        /// scene transition is covering the screen (see class remarks): logs a warning and leaves the overlay
        /// untouched, since forcing the shared alpha/raycast state here would visually corrupt the in-progress
        /// cover.
        /// </summary>
        public void Stop(bool resetAlpha = true)
        {
            if (_transitionActive)
            {
                Debug.LogWarning("UIScreenEffects.Stop ignored - a scene transition is covering the screen.");
                return;
            }

            if (_currentSequence.isAlive) _currentSequence.Stop();

            if (resetAlpha && _canvasGroup)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
            }
        }

        #endregion
    }
}
