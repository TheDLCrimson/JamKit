using System.Collections;
using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>How a <see cref="Switch"/> responds to being interacted with.</summary>
    public enum SwitchMode
    {
        /// <summary>Each interaction flips the state. The default: buttons, levers, breakers.</summary>
        Toggle = 0,

        /// <summary>Each interaction turns on briefly then off again. Pairs with an output set to Pulse.</summary>
        Momentary = 1,

        /// <summary>Turns on once and stays on. Later interactions do nothing.</summary>
        Once = 2,
    }

    /// <summary>
    /// The player-facing source node: a button, lever, breaker or terminal. Turns interactions into
    /// signal state.
    /// </summary>
    /// <remarks>
    /// v2 note: this is the merge of the old <c>SwitchInteractable</c> (which held an id and raised
    /// an activation event) and <c>Switchable</c> (which translated that event into signal state).
    /// Stacking one of each was the kit's headline authoring friction - two components and a
    /// same-GameObject reference wiring an object to itself, purely so the two halves could talk.
    /// The verb parameter is gone with them: <c>Activate("on"/"off"/"toggle")</c> existed so a
    /// replayed tape could re-fire a switch idempotently, and replay was cut, so <see cref="Mode"/>
    /// alone now spans everything the rooms actually used.
    /// </remarks>
    [AddComponentMenu("JamKit/Interaction/Switch")]
    public sealed class Switch : SignalNode, IInteractable
    {
        [Tooltip("Verb shown in the interaction prompt. The key name is added by the player's Interactor, so write \"Open Door\", not \"[E] Open Door\".")]
        [SerializeField] private string _label = "Use";

        [Tooltip("How interactions map to state.")]
        [SerializeField] private SwitchMode _mode = SwitchMode.Toggle;

        [Tooltip("Seconds this switch must be held. Negative uses the Interactor's default, which is the right answer for almost everything.")]
        [SerializeField] private float _holdSecondsOverride = -1f;

        [Tooltip("How long Momentary stays on before releasing.")]
        [SerializeField] private float _momentaryDuration = 0.35f;

        /// <inheritdoc />
        public string Label => _label;

        /// <inheritdoc />
        public float HoldSecondsOverride => _holdSecondsOverride;

        /// <inheritdoc />
        public string BlockedMessage => _blockedBy != null ? _blockedBy.BlockedMessage : string.Empty;

        /// <inheritdoc />
        public bool IsAvailable => isActiveAndEnabled;

        /// <summary>How interactions map to state.</summary>
        public SwitchMode Mode => _mode;

        private Coroutine _momentary;
        private BlockedBy _blockedBy;

        protected override void Awake()
        {
            base.Awake();
            _blockedBy = GetComponent<BlockedBy>();
        }

        // Registration is O(1) here and lets the Interactor scan a ready candidate list instead of
        // running a per-frame OverlapSphere. The arbitration itself lives on the Interactor.
        private void OnEnable() => Interactor.Register(this);

        private void OnDisable()
        {
            Interactor.Unregister(this);
            _momentary = null;
        }

        /// <summary>
        /// Applies one interaction according to <see cref="SwitchMode"/>. Safe to call directly from
        /// a cheat button or a test; the Interactor calls this after its hold completes.
        /// </summary>
        public void Interact(object source)
        {
            if (Blocked)
                return;

            switch (_mode)
            {
                case SwitchMode.Toggle:
                    Toggle();
                    break;

                case SwitchMode.Once:
                    SetState(true);
                    break;

                case SwitchMode.Momentary:
                    if (_momentary != null)
                        StopCoroutine(_momentary);
                    if (isActiveAndEnabled)
                        _momentary = StartCoroutine(MomentaryPress());
                    break;
            }
        }

        /// <summary>Code-built setup (demos, tests and runtime-authored rooms); in the editor set the serialized fields.</summary>
        public void Configure(string label, SwitchMode mode, float momentaryDuration = 0.35f, float holdSecondsOverride = -1f)
        {
            _label = label;
            _mode = mode;
            _momentaryDuration = momentaryDuration;
            _holdSecondsOverride = holdSecondsOverride;
        }

        private IEnumerator MomentaryPress()
        {
            SetState(true);
            yield return new WaitForSeconds(_momentaryDuration);
            SetState(false);
            _momentary = null;
        }

        // Deliberately NOT [RequireComponent(typeof(Collider))]: Collider is abstract, so Unity
        // cannot add one and the attribute throws instead of helping. It would also force a
        // BoxCollider onto a switch whose art wants a MeshCollider. A fresh switch gets a solid box
        // here, and the validator reports any switch that ends up with no collider at all.
        //
        // The Interactor resolves by proximity, so the collider is not what makes a switch
        // detectable - it is what stops the player walking through a button. A switch authored as a
        // trigger is almost always a mistake (the author meant TriggerZone), so a fresh one is solid.
        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col == null)
            {
                gameObject.AddComponent<BoxCollider>();
                return;
            }
            if (col.isTrigger)
                col.isTrigger = false;
        }
    }
}
