using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// Makes a node refuse interaction while another node says it should.
    /// "This door needs power", "this terminal needs the network back up".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reference points from the gated object to its source, never the other way round. That
    /// inversion is what removes the old <c>PoweredInteractable</c>'s single-gated-target limit:
    /// twenty powered devices is twenty of these, and the generator holds no list at all.
    /// </para>
    /// <para>
    /// v2 note: this component is why <c>PoweredInteractable</c> is gone rather than ported. It did
    /// the same job pointing the other way, and one concept with two components is exactly the
    /// friction this merge exists to remove.
    /// </para>
    /// <para>
    /// <see cref="SignalNode.Blocked"/> is advisory - this raises the flag, but
    /// <see cref="Interactor"/> is what actually refuses the hold.
    /// </para>
    /// </remarks>
    [AddComponentMenu("JamKit/Interaction/Blocked By")]
    public sealed class BlockedBy : MonoBehaviour
    {
        [Tooltip("Node that decides availability. Empty = never blocked.")]
        [SerializeField] private SignalNode _source;

        [Tooltip("Off (default): blocked while the source is OFF, e.g. requires power. On: blocked while the source is ON.")]
        [SerializeField] private bool _blockWhenSourceOn;

        [Tooltip("Shown by the interaction prompt while blocked. Leave empty to show nothing.")]
        [SerializeField] private string _blockedMessage = "Locked";

        [Tooltip("Node to gate. Empty = a SignalNode on this GameObject.")]
        [SerializeField] private SignalNode _target;

        /// <summary>Message the prompt shows while blocked.</summary>
        public string BlockedMessage => _blockedMessage;

        /// <summary>Node that decides availability. Read by the validator.</summary>
        public SignalNode Source => _source;

        // Flag-guarded so Configure before OnEnable cannot leave a double subscription behind.
        private bool _subscribed;

        private void Awake()
        {
            if (_target == null)
                _target = GetComponent<SignalNode>();
        }

        private void OnEnable()
        {
            Subscribe();
            Apply();
        }

        private void OnDisable() => Unsubscribe();

        /// <summary>Code-built setup (demos, tests and runtime-authored rooms); in the editor set the serialized fields.</summary>
        public void Configure(SignalNode source, SignalNode target, string blockedMessage, bool blockWhenSourceOn)
        {
            Unsubscribe();
            _source = source;
            _target = target;
            _blockedMessage = blockedMessage;
            _blockWhenSourceOn = blockWhenSourceOn;
            if (isActiveAndEnabled)
                Subscribe();
            Apply();
        }

        private void Subscribe()
        {
            if (_subscribed || _source == null)
                return;
            _source.StateChanged += OnSourceChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _source == null)
                return;
            _source.StateChanged -= OnSourceChanged;
            _subscribed = false;
        }

        private void OnSourceChanged(bool state) => Apply();

        private void Apply()
        {
            if (_target == null)
                return;

            bool blocked = _source != null && (_blockWhenSourceOn ? _source.State : !_source.State);
            _target.SetBlocked(blocked);
        }

        private void OnDrawGizmosSelected()
        {
            if (!InteractionGizmos.ShowWiring || _source == null)
                return;

            Gizmos.color = new Color(0.95f, 0.35f, 0.3f);
            Gizmos.DrawLine(transform.position, _source.transform.position);
        }
    }
}
