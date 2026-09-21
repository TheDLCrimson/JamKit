using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// A physical trigger volume that sets a target node's state when a qualifying collider passes
    /// through. The kit's other proximity source, <see cref="Socket"/>, matches a Carryable id and
    /// always turns itself on; this one filters by physics layer instead and drives an arbitrary
    /// target with an arbitrary state, since "the door closes when the enemy walks through" needs
    /// neither a Carryable nor a node of its own - just an external SetState call.
    /// </summary>
    /// <remarks>v2 note: renamed from <c>SignalTrigger</c>, because it reads as a volume, which is what it is.</remarks>
    [AddComponentMenu("JamKit/Interaction/Trigger Zone")]
    [RequireComponent(typeof(Collider))]
    public sealed class TriggerZone : MonoBehaviour
    {
        [Tooltip("The node this trigger drives.")]
        [SerializeField] private SignalNode _target;

        [Tooltip("State applied to the target when a qualifying collider enters.")]
        [SerializeField] private bool _stateOnTrigger = true;

        [Tooltip("Colliders on these layers count as a trigger. Defaults to Everything.")]
        [SerializeField] private LayerMask _triggerLayers = ~0;

        [Tooltip("If true, only the first qualifying entry fires - every later one is ignored. If false, every qualifying entry re-applies the state.")]
        [SerializeField] private bool _once = true;

        /// <summary>The node this zone drives. Read by the validator.</summary>
        public SignalNode Target => _target;

        private bool _fired;

        private void Start()
        {
#if UNITY_EDITOR
            Collider trigger = GetComponent<Collider>();
            if (trigger != null && !trigger.isTrigger)
                Debug.LogWarning("[TriggerZone] " + name + " needs its Collider set to Is Trigger, or nothing will fire it.", this);
#endif
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_target == null || (_once && _fired))
                return;

            if ((_triggerLayers.value & (1 << other.gameObject.layer)) == 0)
                return;

            _fired = true;
            _target.SetState(_stateOnTrigger);
        }

        /// <summary>Code-built setup (demos, tests and runtime-authored rooms); in the editor set the serialized fields.</summary>
        public void Configure(SignalNode target, bool stateOnTrigger, LayerMask triggerLayers, bool once)
        {
            _target = target;
            _stateOnTrigger = stateOnTrigger;
            _triggerLayers = triggerLayers;
            _once = once;
        }

        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;
        }

        // A trigger volume has no renderer, so without this it is completely invisible in the scene
        // view unless it happens to be selected - you cannot see where it is, how big it is, or that
        // it exists at all. Always drawn faintly for that reason; selecting it (or turning on Show
        // Wiring) fills it in and draws the link to whatever it drives.
        private void OnDrawGizmos() => DrawZone(InteractionGizmos.ShowWiring);

        private void OnDrawGizmosSelected() => DrawZone(true);

        private void DrawZone(bool emphasised)
        {
            Color tint = new Color(0.8f, 0.45f, 0.95f);

            Matrix4x4 previous = Gizmos.matrix;
            Bounds local;
            if (TryLocalBounds(out local))
            {
                Gizmos.matrix = transform.localToWorldMatrix;

                if (emphasised)
                {
                    Gizmos.color = new Color(tint.r, tint.g, tint.b, 0.12f);
                    Gizmos.DrawCube(local.center, local.size);
                }

                Gizmos.color = new Color(tint.r, tint.g, tint.b, emphasised ? 0.9f : 0.35f);
                Gizmos.DrawWireCube(local.center, local.size);
                Gizmos.matrix = previous;
            }

            if (!emphasised || _target == null)
                return;

            Gizmos.color = tint;
            Gizmos.DrawLine(transform.position, _target.transform.position);
            Gizmos.DrawSphere(Vector3.Lerp(transform.position, _target.transform.position, 0.92f), 0.06f);
        }

        // Local-space so the box follows a rotated or scaled zone instead of drawing an axis-aligned
        // approximation of it. Sphere and capsule colliders are drawn as their bounding box, which is
        // honest enough for "here is roughly the volume" and avoids a per-collider-type gizmo zoo.
        private bool TryLocalBounds(out Bounds local)
        {
            local = default;

            BoxCollider box = GetComponent<BoxCollider>();
            if (box != null)
            {
                local = new Bounds(box.center, box.size);
                return true;
            }

            SphereCollider sphere = GetComponent<SphereCollider>();
            if (sphere != null)
            {
                local = new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f));
                return true;
            }

            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                float d = capsule.radius * 2f;
                Vector3 size = new Vector3(d, d, d);
                if (capsule.direction == 0) size.x = Mathf.Max(capsule.height, d);
                else if (capsule.direction == 1) size.y = Mathf.Max(capsule.height, d);
                else size.z = Mathf.Max(capsule.height, d);
                local = new Bounds(capsule.center, size);
                return true;
            }

            return false;
        }
    }
}
