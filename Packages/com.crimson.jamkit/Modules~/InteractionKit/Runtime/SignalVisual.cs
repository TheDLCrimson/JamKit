using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// Tints a renderer by a node's state, so a switch, breaker or socket reads as on or off at a
    /// glance without opening the inspector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately NOT a <see cref="SignalNode"/>. It observes one instead, defaulting to a node on
    /// its own GameObject, so adding it to a switch just works with nothing to wire. Making it a node
    /// would give it a state of its own, which would then have to be driven by wiring the switch's
    /// Outputs to a component sitting on that same switch - an object wired to itself, which is
    /// precisely the friction the v2 merge exists to remove.
    /// </para>
    /// <para>
    /// v2 note: promoted from the demo tier, where it was <c>SignalIndicator</c>. Being able to see
    /// which breaker is on is not a demo concern - it is the difference between a wired room you can
    /// debug by looking at it and one you debug by clicking through the hierarchy.
    /// </para>
    /// <para>
    /// Uses a MaterialPropertyBlock rather than touching <c>renderer.material</c>, which would
    /// instantiate a copy of the material per object and leak it. That also means it never mutates
    /// the shared material asset.
    /// </para>
    /// </remarks>
    [AddComponentMenu("JamKit/Interaction/Signal Visual")]
    public sealed class SignalVisual : MonoBehaviour
    {
        [Tooltip("Node whose state is shown. Empty = a SignalNode on this GameObject.")]
        [SerializeField] private SignalNode _source;

        [Tooltip("Renderer to tint. Empty = a Renderer on this GameObject.")]
        [SerializeField] private Renderer _renderer;

        [Tooltip("Colour while the source is off.")]
        [SerializeField] private Color _offColor = new Color(0.28f, 0.28f, 0.32f);

        [Tooltip("Colour while the source is on.")]
        [SerializeField] private Color _onColor = new Color(0.25f, 0.95f, 0.4f);

        [Tooltip("Emission strength applied alongside the tint, so the on state also glows under URP. Zero disables the emission write entirely.")]
        [SerializeField] private float _emission = 2f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>The node being shown. Read by the validator.</summary>
        public SignalNode Source => _source;

        private MaterialPropertyBlock _block;
        private bool _subscribed;

        private void Awake() => Resolve();

        private void OnEnable()
        {
            Resolve();
            Subscribe();
            Apply();
        }

        private void OnDisable() => Unsubscribe();

        /// <summary>Code-built setup (demos, tests and runtime-authored rooms); in the editor set the serialized fields.</summary>
        public void Configure(SignalNode source, Renderer target, Color offColor, Color onColor, float emission = 2f)
        {
            Unsubscribe();
            _source = source;
            _renderer = target;
            _offColor = offColor;
            _onColor = onColor;
            _emission = emission;
            // Resolve after assigning, so passing null means the same thing here as leaving the
            // field empty in the inspector: fall back to a node on this GameObject.
            Resolve();
            if (isActiveAndEnabled)
                Subscribe();
            Apply();
        }

        private void Resolve()
        {
            if (_source == null)
                _source = GetComponent<SignalNode>();
            if (_renderer == null)
                _renderer = GetComponent<Renderer>();
        }

        // Flag-guarded so Configure before OnEnable cannot leave a double subscription behind.
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
            // The == null check covers a destroyed renderer too (Unity's lifetime check), which is
            // what a teardown order that kills the renderer before this component produces. Without
            // it, GetPropertyBlock throws MissingReferenceException during scene teardown.
            if (_renderer == null)
                return;

            bool on = _source != null && _source.State;

            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);

            Color tint = on ? _onColor : _offColor;
            // Both names, so this works on URP/Lit (_BaseColor) and older or unlit shaders (_Color)
            // without asking the author which pipeline their material happens to use.
            _block.SetColor(BaseColorId, tint);
            _block.SetColor(ColorId, tint);

            if (_emission > 0f)
                _block.SetColor(EmissionColorId, on ? tint * _emission : Color.black);

            _renderer.SetPropertyBlock(_block);
        }

#if UNITY_EDITOR
        // Editing the colours should show up immediately, including outside play mode.
        private void OnValidate()
        {
            Resolve();
            Apply();
        }
#endif
    }
}
