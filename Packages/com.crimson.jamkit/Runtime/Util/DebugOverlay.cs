// The entire overlay - type, state, and IMGUI - is compiled only in the Editor and in development builds, so
// it can never ship in a release player (the CarGoesAround failure mode was debug OnGUI compiled into a
// shipping build). Callers of AddButton must guard their calls with the same directive.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JamKit
{
    /// <summary>
    /// Toggleable on-screen debug overlay: FPS, current <see cref="GameState"/> phase, EventBus subscriber
    /// counts, and one-line cheat buttons registered via <see cref="AddButton"/>. Editor / development builds
    /// only.
    /// </summary>
    /// <remarks>
    /// Pattern source: NineTailsProject's <c>LevelManager.OnGUI</c> overlay, standardized and made
    /// ship-safe by the surrounding <c>#if UNITY_EDITOR || DEVELOPMENT_BUILD</c>. The toggle uses the new Input
    /// System (<see cref="Keyboard"/>) because the project's Active Input Handling is Input System-only, so a
    /// legacy <c>Input.GetKeyDown</c> would throw.
    /// </remarks>
    public class DebugOverlay : MonoBehaviour
    {
        [Tooltip("Key that toggles the overlay (new Input System Key enum).")]
        [SerializeField] private Key _toggleKey = Key.F1;

        [SerializeField] private bool _visibleOnStart = true;

        // Rate for the FPS readout's exponential smoothing (MathUtil.Decay), chosen to feel similar to the
        // fixed-factor Lerp this replaced at a typical 60fps frame, while staying correct at any frame rate.
        private const float FpsSmoothingRate = 6f;

        private static readonly List<CheatButton> _buttons = new List<CheatButton>();

        private bool _visible;
        private float _fps;
        private GUIStyle _labelStyle;

        private readonly struct CheatButton
        {
            public readonly string Label;
            public readonly Action Action;
            public CheatButton(string label, Action action) { Label = label; Action = action; }
        }

        /// <summary>
        /// Register a cheat button shown on the overlay. Call sites must be guarded with
        /// <c>#if UNITY_EDITOR || DEVELOPMENT_BUILD</c> since this type does not exist in a release build.
        /// </summary>
        public static void AddButton(string label, Action action)
        {
            if (action == null) return;
            _buttons.Add(new CheatButton(label, action));
        }

        /// <summary>Remove all registered cheat buttons.</summary>
        public static void ClearButtons() => _buttons.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Static list survives a Play session when Enter Play Mode has "Reload Domain" disabled.
            _buttons.Clear();
        }

        private void Awake() => _visible = _visibleOnStart;

        private void Update()
        {
            _fps = MathUtil.Decay(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f), FpsSmoothingRate, Time.unscaledDeltaTime);

            if (Keyboard.current != null && Keyboard.current[_toggleKey].wasPressedThisFrame)
            {
                _visible = !_visible;
            }
        }

        private void OnGUI()
        {
            if (!_visible) return;

            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            const float width = 320f;
            GUILayout.BeginArea(new Rect(10f, 10f, width, Screen.height - 20f), GUI.skin.box);

            GUILayout.Label($"FPS: {_fps:0}", _labelStyle);

            string phase = GameState.Instance ? GameState.Instance.CurrentPhase.ToString() : "n/a";
            GUILayout.Label($"GameState: {phase}", _labelStyle);

            GUILayout.Label($"EventBus: {EventBus.TotalSubscriberCount()} subs / {EventBus.ActiveEventTypeCount} types", _labelStyle);

            if (_buttons.Count > 0)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Cheats", _labelStyle);
                foreach (CheatButton button in _buttons)
                {
                    if (GUILayout.Button(button.Label)) button.Action?.Invoke();
                }
            }

            GUILayout.EndArea();
        }
    }
}
#endif
