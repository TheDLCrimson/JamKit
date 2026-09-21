using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// The player's single input and arbitration authority for interaction. One per player rig.
    ///
    /// Owns the key, the hold window, the range, and the decision about which object a keypress
    /// acts on. World objects own only what is true about themselves - their label, and whether
    /// they are refusing right now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v2 note: this replaces <c>HoldToInteract</c>, which lived on every interactable and carried
    /// 15 serialized fields, twelve of which held the same value on every object in the project.
    /// Changing the interact key meant touching every scene. It also ran the nearest-wins check
    /// from the object side, so N objects each scanned N peers every frame; here one component
    /// scans the registry once.
    /// </para>
    /// <para>
    /// The hold is not a delay for its own sake. It is the commitment window: the player has to
    /// judge when there is enough space to stop moving and commit, which is what makes a pursuing
    /// threat matter. A Switch may lengthen it, but the default lives here.
    /// </para>
    /// </remarks>
    [AddComponentMenu("JamKit/Interaction/Interactor")]
    [DisallowMultipleComponent]
    public sealed class Interactor : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Action that holds to interact. Bind it to Gameplay/Interact. Empty falls back to the E key, so the component still works in a scene with no input asset wired.")]
        [SerializeField] private InputActionReference _interactAction;

        [Tooltip("Seconds the action must be held to commit. A Switch can override this per object. 0 fires instantly on press.")]
        [SerializeField] private float _holdSeconds = 1.2f;

        [Header("Aim")]
        [Tooltip("Whose proximity is measured, and the origin of the aim. Empty = Camera.main at Start (the common first-person case).")]
        [SerializeField] private Transform _origin;

        [Tooltip("An interactable must be within this distance of the origin to be offered.")]
        [SerializeField] private float _range = 2.5f;

        /// <summary>Whose proximity is measured; set at runtime when the player or possessed body changes.</summary>
        public Transform Origin { get { return _origin; } set { _origin = value; } }

        /// <summary>Master gate: while false nothing is offered and no hold can start. Default true.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// The one interactable currently offered, or null. Presentation reads this rather than
        /// probing again, so the prompt and the hover outline physically cannot disagree about
        /// which object is live.
        /// </summary>
        public IInteractable Current { get; private set; }

        /// <summary>True while a hold is in progress (started, not yet committed or cancelled).</summary>
        public bool IsHolding { get; private set; }

        /// <summary>Hold completion in 0..1. Zero when not holding.</summary>
        public float Progress
        {
            get
            {
                float duration = CurrentHoldSeconds();
                return duration <= 0f ? 0f : Mathf.Clamp01(_elapsed / duration);
            }
        }

        /// <summary>Display name of the interact binding, e.g. "E". The prompt shows this, so the label can never name a key that is not the one being polled.</summary>
        public string KeyDisplay { get; private set; } = "E";

        /// <summary>Fired when a hold begins.</summary>
        public event Action HoldStarted;

        /// <summary>Fired when a hold is abandoned before committing (released, or walked away).</summary>
        public event Action HoldCanceled;

        /// <summary>Fired after the hold completes and the target has been interacted with.</summary>
        public event Action<IInteractable> HoldCompleted;

        // Every enabled interactable, so the scan has a candidate set without a per-frame
        // OverlapSphere. Registration stays on the object side (it is O(1) on enable/disable and
        // preserves the old allocation profile); the SCAN is what moved here, which is the part
        // that was N-squared when every object ran it for itself.
        private static readonly List<IInteractable> _registered = new List<IInteractable>();

        private float _elapsed;
        // Set on commit and cleared on release, so holding the key down does not re-fire forever.
        private bool _consumed;
        // Last device seen driving the action, so the prompt can name that device's binding.
        private InputDevice _lastDevice;

        /// <summary>Called by an interactable when it becomes available. Safe to call twice.</summary>
        public static void Register(IInteractable interactable)
        {
            if (interactable != null && !_registered.Contains(interactable))
                _registered.Add(interactable);
        }

        /// <summary>Called by an interactable when it goes away. Safe to call for one never registered.</summary>
        public static void Unregister(IInteractable interactable) => _registered.Remove(interactable);

        private void OnEnable()
        {
            if (_interactAction != null && _interactAction.action != null)
            {
                _interactAction.action.Enable();
                KeyDisplay = ResolveKeyDisplay(_lastDevice);
            }
        }

        private void OnDisable()
        {
            if (IsHolding)
                Cancel();
            Current = null;
        }

        private void Start()
        {
            if (_origin == null && Camera.main != null)
                _origin = Camera.main.transform;
        }

        private void Update()
        {
            Current = Enabled ? ResolveNearest() : null;

            bool pressed = ReadPressed();
            if (!pressed)
                _consumed = false;

            TrackDevice();

            bool canHold = Current != null && !Current.Blocked;
            bool wantHold = canHold && pressed && !_consumed;

            if (wantHold)
            {
                if (!IsHolding)
                {
                    IsHolding = true;
                    _elapsed = 0f;
                    HoldStarted?.Invoke();
                }

                _elapsed += Time.deltaTime;
                if (_elapsed >= CurrentHoldSeconds())
                    Commit();
            }
            else if (IsHolding)
            {
                Cancel();
            }
        }

        /// <summary>Code-built setup (demos, tests and runtime-authored rooms); in the editor set the serialized fields.</summary>
        public void Configure(Transform origin, float range, float holdSeconds)
        {
            _origin = origin;
            _range = range;
            _holdSeconds = holdSeconds;
        }

        // Nearest in-range candidate, ties broken on hash code so exactly one wins even when the
        // player is equidistant between two. Determinism matters: an unstable winner makes the
        // prompt flicker between two objects, which reads as a bug and is miserable to reproduce.
        private IInteractable ResolveNearest()
        {
            if (_origin == null)
                return null;

            Vector3 from = _origin.position;
            IInteractable best = null;
            float bestDistance = float.MaxValue;
            int bestId = 0;

            for (int i = 0; i < _registered.Count; i++)
            {
                IInteractable candidate = _registered[i];
                // A destroyed MonoBehaviour compares null through the Unity lifetime check even
                // while the interface handle is non-null, so both tests are needed.
                if (candidate == null || candidate is UnityEngine.Object o && o == null)
                    continue;
                if (!candidate.IsAvailable)
                    continue;

                float distance = Vector3.Distance(from, candidate.transform.position);
                if (distance > _range)
                    continue;

                int id = candidate.transform.GetInstanceID();
                if (distance < bestDistance || (Mathf.Approximately(distance, bestDistance) && id < bestId))
                {
                    best = candidate;
                    bestDistance = distance;
                    bestId = id;
                }
            }

            return best;
        }

        private float CurrentHoldSeconds()
        {
            if (Current == null)
                return _holdSeconds;
            float over = Current.HoldSecondsOverride;
            return over >= 0f ? over : _holdSeconds;
        }

        // Only recomputes the display when the player actually switches device, so the string work
        // happens on a controller pick-up rather than every frame.
        private void TrackDevice()
        {
            InputAction action = _interactAction != null ? _interactAction.action : null;
            InputControl control = action != null ? action.activeControl : null;
            if (control == null || control.device == _lastDevice)
                return;

            _lastDevice = control.device;
            KeyDisplay = ResolveKeyDisplay(_lastDevice);
        }

        private bool ReadPressed()
        {
            if (_interactAction != null && _interactAction.action != null)
                return _interactAction.action.IsPressed();

            // Fallback so a rig with no action wired still works in a fresh scene. The prompt reads
            // KeyDisplay, so it names whichever of the two is actually being polled.
            Keyboard kb = Keyboard.current;
            return kb != null && kb.eKey.isPressed;
        }

        // Picks ONE binding to name, for the device actually in use. The parameterless
        // GetBindingDisplayString concatenates every binding, so an action bound to both keyboard
        // and gamepad renders as "E | X" and the prompt reads "[hold E | X] Open Door". Naming the
        // device you are holding is the whole point of reading the glyph from the binding.
        private string ResolveKeyDisplay(InputDevice device)
        {
            InputAction action = _interactAction != null ? _interactAction.action : null;
            if (action == null || action.bindings.Count == 0)
                return "E";

            try
            {
                int chosen = FirstBindingForDevice(action, device);
                string display = action.GetBindingDisplayString(
                    chosen, InputBinding.DisplayStringOptions.DontIncludeInteractions);
                return string.IsNullOrEmpty(display) ? "E" : display;
            }
            catch (Exception)
            {
                // Throws on a composite with no resolved control, which is normal before any device
                // is present. The key name is cosmetic; never break the rig for it.
                return "E";
            }
        }

        // First non-composite binding matching the device's layout, else the first binding at all.
        private static int FirstBindingForDevice(InputAction action, InputDevice device)
        {
            int firstSimple = -1;

            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite || binding.isPartOfComposite)
                    continue;

                if (firstSimple < 0)
                    firstSimple = i;

                if (device == null)
                    continue;

                string layout = InputControlPath.TryGetDeviceLayout(binding.effectivePath);
                if (layout != null && InputSystem.IsFirstLayoutBasedOnSecond(device.layout, layout))
                    return i;
            }

            return firstSimple < 0 ? 0 : firstSimple;
        }

        private void Commit()
        {
            IInteractable target = Current;
            IsHolding = false;
            _elapsed = 0f;
            _consumed = true;

            if (target != null)
            {
                target.Interact(this);
                HoldCompleted?.Invoke(target);
            }
        }

        private void Cancel()
        {
            IsHolding = false;
            _elapsed = 0f;
            HoldCanceled?.Invoke();
        }

        private void OnDrawGizmosSelected()
        {
            if (!InteractionGizmos.ShowWiring)
                return;

            Transform from = _origin != null ? _origin : transform;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.45f);
            Gizmos.DrawWireSphere(from.position, _range);
        }
    }
}
