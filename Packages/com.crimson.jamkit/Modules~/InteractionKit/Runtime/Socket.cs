using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// A socket that turns on when the right prop is put into it. Battery housings, keyholes,
    /// pedestals, pressure sockets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Insertion is proximity-based: bring a matching <see cref="Carryable"/> into the trigger and
    /// it seats. If the player is holding it, the carrier is told to let go first - that handoff is
    /// mandatory, because a carry implementation keeps writing velocity to whatever it holds and
    /// would fight the seated pose, then release it anyway once break distance elapsed.
    /// </para>
    /// <para>
    /// A key in a keyhole is the same object as a battery in a housing, which is why this kit has
    /// no separate lock component at all. <see cref="Removable"/> is the primitive a passcode or
    /// combination puzzle is built from.
    /// </para>
    /// <para>
    /// v2 note: renamed from <c>Receptacle</c>, and the direct <c>PlayerCarry</c> field is now the
    /// <see cref="ICarrier"/> seam. That field was the kit's only dependency on game code and the
    /// single reason it could not be a module.
    /// </para>
    /// </remarks>
    [AddComponentMenu("JamKit/Interaction/Socket")]
    public sealed class Socket : SignalNode
    {
        [Tooltip("Carryable id this socket accepts. Must match exactly.")]
        [SerializeField] private string _accepts = "";

        [Tooltip("Where a seated prop is placed. Empty = this object's transform.")]
        [SerializeField] private Transform _snapAnchor;

        [Tooltip("If true, the player can pull the seated prop back out. If false, once seated the prop is locked in for good - a commit.")]
        [SerializeField] private bool _removable;

        [Tooltip("Hide the prop once it seats, so the socket swallows it. For a key that is spent on " +
                 "use, where leaving the prop stuck to the socket reads worse than it simply going in.")]
        [SerializeField] private bool _hideWhenSeated;

        /// <summary>The currently seated prop, or null.</summary>
        public Carryable Seated => _seated;

        /// <summary>True if the player may take the seated prop back out.</summary>
        public bool Removable => _removable;

        /// <summary>Carryable id this socket accepts. Read by the validator.</summary>
        public string Accepts => _accepts;

        private Carryable _seated;
        private ICarrier _carrier;

        // After an eject, the prop is still inside the trigger for a moment; without this cooldown
        // OnTriggerStay would immediately re-seat it and the player could never pull it out.
        private float _acceptCooldownUntil;

        private void Start()
        {
#if UNITY_EDITOR
            Collider trigger = GetComponent<Collider>();
            if (trigger == null || !trigger.isTrigger)
                Debug.LogWarning("[Socket] " + name + " needs a Collider with Is Trigger enabled, or nothing can be inserted.", this);
#endif
        }

        private void OnTriggerEnter(Collider other) => TryAccept(other, true);

        private void OnTriggerStay(Collider other) => TryAccept(other, false);

        /// <summary>Code-built setup (demos, tests and runtime-authored rooms); in the editor set the serialized fields.</summary>
        public void Configure(string accepts, Transform snapAnchor, bool removable)
        {
            _accepts = accepts;
            _snapAnchor = snapAnchor;
            _removable = removable;
        }

        /// <summary>
        /// Releases the seated prop back into normal physics and clears the socket. The carry
        /// implementation calls this when the player pulls a prop out of a removable socket; the
        /// brief cooldown stops the still-overlapping prop from snapping straight back in.
        /// </summary>
        public void Eject()
        {
            if (_seated == null)
                return;

            _seated.Holder = null;
            _seated.gameObject.SetActive(true);
            _seated.transform.SetParent(null, true);

            Rigidbody body = _seated.GetComponentInParent<Rigidbody>();
            if (body != null)
                body.isKinematic = false;

            _seated = null;
            _acceptCooldownUntil = Time.time + 0.6f;
            SetState(false);
        }

        private void TryAccept(Collider other, bool warnOnMismatch)
        {
            if (_seated != null || Time.time < _acceptCooldownUntil)
                return;

            Carryable candidate = other.GetComponentInParent<Carryable>();
            if (candidate == null)
                return;

            if (candidate.Id != _accepts)
            {
#if UNITY_EDITOR
                if (warnOnMismatch)
                    Debug.LogWarning("[Socket] " + name + " accepts \"" + _accepts + "\" but got \"" + candidate.Id + "\" from " + candidate.name + ".", this);
#endif
                return;
            }

            Seat(candidate);
        }

        // Resolved on demand rather than at Start. Rooms are separate scenes, so the player may not
        // exist yet when a room loads, and a serialized reference to a player in another scene is
        // something Unity refuses to store at all. Only searches while the field is still empty.
        private ICarrier ResolveCarrier()
        {
            if (_carrier == null || (_carrier is Object obj && obj == null))
            {
                _carrier = null;
                MonoBehaviour[] all = Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is ICarrier carrier)
                    {
                        _carrier = carrier;
                        break;
                    }
                }
            }
            return _carrier;
        }

        private void Seat(Carryable prop)
        {
            Rigidbody body = prop.GetComponentInParent<Rigidbody>();

            // Take it off the player first. Drop() restores gravity, damping and collision, so
            // anything set before this call would be overwritten by it.
            ICarrier carrier = ResolveCarrier();
            if (carrier != null && carrier.IsCarrying && body != null && carrier.Held == body)
                carrier.Drop();

            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            Transform anchor = _snapAnchor != null ? _snapAnchor : transform;
            Transform t = prop.transform;
            t.SetParent(anchor, true);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;

            _seated = prop;
            prop.Holder = this;

            // Deliberately here rather than on a SignalRelay wired to this node's output: a relay's
            // UnityEvent is also re-invoked by SignalNode.OnValidate on play-mode entry, which would
            // hide the prop before the player ever reached it. Inside Seat it can only run on a
            // real insertion.
            if (_hideWhenSeated)
                prop.gameObject.SetActive(false);

            SetState(true);
        }

        // Not [RequireComponent(typeof(Collider))] - Collider is abstract and the attribute throws
        // rather than adding one. A fresh socket gets a trigger box; the validator reports any that
        // ends up without one, which is the failure where nothing can ever be inserted.
        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col == null)
                col = gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
        }

        protected override void DrawNodeGizmos()
        {
            Transform anchor = _snapAnchor != null ? _snapAnchor : transform;
            Gizmos.color = _state ? new Color(0.2f, 0.95f, 0.35f) : new Color(0.95f, 0.55f, 0.2f);
            Gizmos.DrawWireCube(anchor.position, Vector3.one * 0.25f);
        }
    }
}
