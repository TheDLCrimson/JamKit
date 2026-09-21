using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JamKit
{
    /// <summary>
    /// A modal "blocker" stack: push/pop instantiated panels, each declaring whether it pauses gameplay, hides
    /// the HUD, and responds to the UI Cancel action. When a blocker is removed the stack rescans the remaining
    /// blockers before restoring HUD/pause, so nested menus behave correctly.
    /// </summary>
    /// <remarks>
    /// Extracted from NineTailsProject's <c>UIManager</c> (renamed). Two donor couplings are fixed here:
    /// <list type="bullet">
    /// <item>Pause is a real injected callback (<see cref="OnPauseGameplay"/> / <see cref="OnResumeGameplay"/>)
    /// that defaults to JamKit's blessed <see cref="GameState"/> service instead of the donor's hard
    /// <c>GameManager.Instance</c> dependency.</item>
    /// <item>Dismissal uses the new Input System UI <c>Cancel</c> action (assign <see cref="_cancelAction"/>)
    /// rather than the donor's legacy <c>Input.GetKeyDown(KeyCode.Escape)</c>.</item>
    /// </list>
    /// A blocker revealed again after the one above it closes receives <see cref="UIBlockerBase.OnResume"/>,
    /// not <see cref="UIBlockerBase.OnOpen"/> (donor re-fired OnOpen).
    /// </remarks>
    /// <remarks>
    /// Requires a <see cref="RectTransform"/> on this object: blockers are UI prefabs instantiated under
    /// <see cref="_blockerContainer"/>, and a plain <see cref="Transform"/> ancestor breaks their anchoring
    /// (Unity has no parent rect to resolve against). <see cref="RequireComponent"/> also covers the
    /// auto-created container fallback in <see cref="Awake"/>, which stretches to fill this rect.
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public class UIBlockerStack : Singleton<UIBlockerStack>
    {
        [Header("HUD Configuration")]
        [SerializeField] private GameObject _hudLayer;

        [Header("Blocker Configuration")]
        [Tooltip("Parent for instantiated blocker panels. Auto-created under this object if unassigned.")]
        [SerializeField] private Transform _blockerContainer;

        [Tooltip("Blocker opened by the Cancel action when the stack is empty (e.g. the pause menu). Optional.")]
        [SerializeField] private UIBlockerBase _escapeBlocker;

        [Header("Input")]
        [Tooltip("UI/Cancel action from the shared input actions asset. Closes the top blocker (or opens the escape blocker).")]
        [SerializeField] private InputActionReference _cancelAction;

        private readonly Stack<UIBlockerBase> _blockerStack = new Stack<UIBlockerBase>();
        private bool _isHUDVisible = true;

        private Action _onPauseGameplay;
        private Action _onResumeGameplay;

        /// <summary>
        /// Invoked when a blocker requests gameplay pause. Defaults to <see cref="GameState"/>.Pause; assign to
        /// route pause somewhere else. <see cref="GameState.Pause"/> is idempotent, so repeated calls are safe.
        /// </summary>
        public Action OnPauseGameplay
        {
            get => _onPauseGameplay;
            set => _onPauseGameplay = value;
        }

        /// <summary>Invoked when the last pause-requesting blocker closes. Defaults to <see cref="GameState"/>.Resume.</summary>
        public Action OnResumeGameplay
        {
            get => _onResumeGameplay;
            set => _onResumeGameplay = value;
        }

        protected override void Awake()
        {
            base.Awake();

            if (Instance != this)
            {
                // base.Awake() found an existing instance and has scheduled this duplicate's GameObject for
                // destruction - skip the rest of setup so a doomed duplicate never allocates a throwaway
                // BlockerContainer.
                return;
            }

            // Default the pause callbacks to the blessed GameState service. These run at open/close time
            // (Start or later), so GameState.Instance is resolved by then.
            _onPauseGameplay ??= DefaultPause;
            _onResumeGameplay ??= DefaultResume;

            if (_blockerContainer == null)
            {
                GameObject containerObj = new GameObject("BlockerContainer", typeof(RectTransform));
                RectTransform containerRect = (RectTransform)containerObj.transform;
                containerRect.SetParent(transform, worldPositionStays: false);
                containerRect.anchorMin = Vector2.zero;
                containerRect.anchorMax = Vector2.one;
                containerRect.offsetMin = Vector2.zero;
                containerRect.offsetMax = Vector2.zero;
                _blockerContainer = containerRect;
            }
        }

        private void OnEnable()
        {
            if (_cancelAction != null && _cancelAction.action != null)
            {
                _cancelAction.action.performed += OnCancelPerformed;
                _cancelAction.action.Enable();
            }
        }

        private void OnDisable()
        {
            if (_cancelAction != null && _cancelAction.action != null)
            {
                _cancelAction.action.performed -= OnCancelPerformed;
            }
        }

        private void Update()
        {
            if (_blockerStack.Count > 0)
            {
                _blockerStack.Peek().OnUpdate();
            }
        }

        private static void DefaultPause()
        {
            if (GameState.Instance) GameState.Instance.Pause();
        }

        private static void DefaultResume()
        {
            if (GameState.Instance) GameState.Instance.Resume();
        }

        private void OnCancelPerformed(InputAction.CallbackContext ctx) => HandleCancel();

        private void HandleCancel()
        {
            if (_blockerStack.Count == 0)
            {
                if (_escapeBlocker) OpenBlocker(_escapeBlocker);
                return;
            }

            if (_blockerStack.Peek().RespondToEscape) CloseTopBlocker();
        }

        /// <summary>Instantiate <paramref name="blocker"/>, push it, and make it the active top of the stack.</summary>
        public bool OpenBlocker(UIBlockerBase blocker)
        {
            if (!blocker) return false;

            UIBlockerBase blockerInstance = Instantiate(blocker, _blockerContainer);
            blockerInstance.name = blocker.name; // strip the "(Clone)" suffix
            blockerInstance.gameObject.SetActive(false);

            if (_blockerStack.Count > 0)
            {
                _blockerStack.Peek().OnPause();
            }

            _blockerStack.Push(blockerInstance);

            ApplyBlockerBehaviors(blockerInstance);

            blockerInstance.gameObject.SetActive(true);
            blockerInstance.OnOpen();

            return true;
        }

        /// <summary>Close and destroy the topmost blocker, restoring HUD/pause and resuming the new top.</summary>
        public bool CloseTopBlocker()
        {
            if (!CloseTopBlockerCore()) return false;

            RevealNewTopIfAny();
            return true;
        }

        /// <summary>
        /// Close <paramref name="blocker"/> and every blocker above it. Only the blocker that ends up on top
        /// once the whole range is closed receives <see cref="UIBlockerBase.OnResume"/> - an intermediate
        /// blocker being closed in the same call never does, since it is never actually revealed to the
        /// player (see the M6 friction log for the spurious-OnResume defect this fixes).
        /// </summary>
        public bool CloseBlocker(UIBlockerBase blocker)
        {
            if (!_blockerStack.Contains(blocker))
            {
                Debug.LogWarning($"UIBlockerStack.CloseBlocker: {blocker.name} is not in the stack.");
                return false;
            }

            while (_blockerStack.Count > 0 && _blockerStack.Peek() != blocker)
            {
                CloseTopBlockerCore(); // intermediate close: never revealed, so no OnResume here.
            }

            if (_blockerStack.Count > 0 && _blockerStack.Peek() == blocker)
            {
                CloseTopBlocker(); // final close: reveals whatever remains beneath, exactly once.
                return true;
            }

            return false;
        }

        // Pop-and-destroy the top blocker and restore its HUD/pause contribution, without touching whatever
        // becomes the new top. Shared by CloseTopBlocker (which reveals the new top) and CloseBlocker's
        // intermediate closes (which must not, since that "new top" is closed again immediately after).
        private bool CloseTopBlockerCore()
        {
            if (_blockerStack.Count == 0)
            {
                Debug.LogWarning("UIBlockerStack.CloseTopBlocker: no blocker to close.");
                return false;
            }

            UIBlockerBase blocker = _blockerStack.Pop();

            blocker.OnClose();
            blocker.gameObject.SetActive(false);
            Destroy(blocker.gameObject);

            RestoreBlockerBehaviors(blocker);

            return true;
        }

        private void RevealNewTopIfAny()
        {
            if (_blockerStack.Count == 0) return;

            UIBlockerBase newTop = _blockerStack.Peek();
            ApplyBlockerBehaviors(newTop);
            newTop.OnResume(); // distinct from OnOpen: this blocker is being revealed again
        }

        /// <summary>Close every blocker in the stack.</summary>
        public void CloseAllBlockers()
        {
            while (_blockerStack.Count > 0)
            {
                CloseTopBlocker();
            }
        }

        /// <summary>Number of blockers currently in the stack.</summary>
        public int GetBlockerCount() => _blockerStack.Count;

        /// <summary>True if <paramref name="blocker"/> is anywhere in the stack.</summary>
        public bool IsBlockerActive(UIBlockerBase blocker) => _blockerStack.Contains(blocker);

        /// <summary>True if <paramref name="blocker"/> is the topmost blocker.</summary>
        public bool IsBlockerTop(UIBlockerBase blocker) => _blockerStack.Count > 0 && _blockerStack.Peek() == blocker;

        private void ApplyBlockerBehaviors(UIBlockerBase blocker)
        {
            if (blocker.HideHUD) SetHUDVisibility(false);
            if (blocker.PauseGameplay) _onPauseGameplay?.Invoke();
        }

        private void RestoreBlockerBehaviors(UIBlockerBase blocker)
        {
            bool shouldHideHUD = false;
            bool shouldPauseGameplay = false;

            foreach (UIBlockerBase remaining in _blockerStack)
            {
                if (remaining.HideHUD) shouldHideHUD = true;
                if (remaining.PauseGameplay) shouldPauseGameplay = true;
            }

            if (!shouldHideHUD && blocker.HideHUD) SetHUDVisibility(true);
            if (!shouldPauseGameplay && blocker.PauseGameplay) _onResumeGameplay?.Invoke();
        }

        private void SetHUDVisibility(bool visible)
        {
            if (_hudLayer)
            {
                _hudLayer.SetActive(visible);
                _isHUDVisible = visible;
            }
        }

        /// <summary>Manually set HUD visibility (for special cases outside the stack).</summary>
        public void SetHUDVisible(bool visible) => SetHUDVisibility(visible);

        /// <summary>Current HUD visibility state.</summary>
        public bool IsHUDVisible() => _isHUDVisible;
    }
}
