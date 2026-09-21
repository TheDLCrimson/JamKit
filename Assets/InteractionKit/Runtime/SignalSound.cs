using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// Plays a one-shot sound when a node's state changes: one clip for the change to true, one for
    /// the change to false. Drop it on a <see cref="Door"/> for open/close sounds with no glue -
    /// either clip can be left empty if that transition should stay silent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Like <see cref="SignalVisual"/>, this observes a node rather than being one. A feedback
    /// component that carried its own state would have to be driven by wiring the door's Outputs to
    /// a component on that same door, and an object wired to itself is the exact friction v2 removes.
    /// </para>
    /// <para>
    /// Routing: assign <c>_source</c> for positional 3D audio (set its Output to the project's SFX
    /// mixer group so it still obeys the saved SFX volume), or leave it empty to fall back to
    /// <see cref="AudioService.PlaySfx"/>, which is correctly routed but non-positional.
    /// </para>
    /// <para>
    /// That fallback exists because JamKit's AudioService is frozen at v1.0 and exposes only a 2D
    /// <c>PlaySfx</c>. GMTK 2026 used a jam-time <c>PlaySfxAtPosition</c> that was never upstreamed;
    /// adding it here would mean editing the frozen package, so the choice is handed to the author.
    /// If a positional overload is ever upstreamed, this collapses back to one line.
    /// </para>
    /// </remarks>
    [AddComponentMenu("JamKit/Interaction/Signal Sound")]
    public sealed class SignalSound : MonoBehaviour
    {
        [Tooltip("Node whose changes are sounded. Empty = a SignalNode on this GameObject.")]
        [SerializeField] private SignalNode _node;

        [Tooltip("Played when the state changes to true (e.g. a Door opening).")]
        [SerializeField] private AudioClip _onClip;

        [Tooltip("Played when the state changes to false (e.g. a Door closing).")]
        [SerializeField] private AudioClip _offClip;

        [Range(0f, 1f)]
        [SerializeField] private float _volume = 1f;

        [Tooltip("Optional AudioSource for positional playback. Route its Output to the SFX mixer group so it still obeys the saved SFX volume. Empty = play through AudioService (correctly routed, but not positional).")]
        [SerializeField] private AudioSource _source;

        /// <summary>The node being sounded. Read by the validator.</summary>
        public SignalNode Node => _node;

        private bool _subscribed;

        private void Awake()
        {
            if (_node == null)
                _node = GetComponent<SignalNode>();
        }

        private void OnEnable()
        {
            if (_node == null)
                _node = GetComponent<SignalNode>();
            Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        /// <summary>Code-built setup (demos, tests and runtime-authored rooms); in the editor set the serialized fields.</summary>
        public void Configure(SignalNode node, AudioClip onClip, AudioClip offClip, float volume = 1f)
        {
            Unsubscribe();
            _node = node;
            _onClip = onClip;
            _offClip = offClip;
            _volume = volume;
            // Passing null means the same thing as leaving the field empty in the inspector.
            if (_node == null)
                _node = GetComponent<SignalNode>();
            if (isActiveAndEnabled)
                Subscribe();
        }

        // Flag-guarded so Configure before OnEnable cannot leave a double subscription behind.
        private void Subscribe()
        {
            if (_subscribed || _node == null)
                return;
            _node.StateChanged += OnStateChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _node == null)
                return;
            _node.StateChanged -= OnStateChanged;
            _subscribed = false;
        }

        private void OnStateChanged(bool state)
        {
            AudioClip clip = state ? _onClip : _offClip;
            if (clip == null)
                return;

            if (_source != null)
            {
                _source.PlayOneShot(clip, _volume);
                return;
            }

            // Null-guarded because a node can change state before Bootstrap has finished waking the
            // services - a Gate evaluates in its first Update, which can beat service init in a
            // scene loaded directly from the Editor.
            if (AudioService.Instance != null)
                AudioService.Instance.PlaySfx(clip, _volume);
        }
    }
}
