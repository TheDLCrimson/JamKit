using UnityEngine;
using UnityEngine.InputSystem;

namespace JamKit.InteractionKit.Demo
{
    /// <summary>
    /// A deliberately minimal player rig for the showcase scene: WASD plus mouse look, and a simple
    /// physics carry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists so the wiring module can be opened and played with no other module in the
    /// project. It is NOT the shipping carry - the real one is <c>PlayerCarry</c> in
    /// <c>Assets/_Project</c>, which carries five hand-found fixes and talks to the KCC motor. Use
    /// that one in a real game; this is here so the demo scene stands alone.
    /// </para>
    /// <para>
    /// It implementing <see cref="ICarrier"/> in about forty lines is also the point: the seam is
    /// small enough that any project can satisfy it without adopting the shipping carry.
    /// </para>
    /// <para>
    /// The capsule and kinematic Rigidbody are not decoration. Unity only raises OnTriggerEnter when
    /// both parties have a Collider and at least one has a Rigidbody, so a player that is just a
    /// transform can walk through a <see cref="TriggerZone"/> and nothing fires. Any real rig
    /// (a CharacterController, the KCC module) already satisfies this; a hand-built one may not.
    /// </para>
    /// </remarks>
    [AddComponentMenu("")]
    [RequireComponent(typeof(Interactor))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DemoRig : MonoBehaviour, ICarrier
    {
        [SerializeField] private float _moveSpeed = 4f;
        [SerializeField] private float _lookSensitivity = 0.12f;
        [SerializeField] private float _carryDistance = 1.8f;
        [SerializeField] private float _followStrength = 12f;
        [SerializeField] private float _maxCarryMass = 25f;
        [SerializeField] private float _pickupRange = 3f;

        /// <inheritdoc />
        public bool IsCarrying => _held != null;

        /// <inheritdoc />
        public Rigidbody Held => _held;

        /// <inheritdoc />
        public Rigidbody HoverTarget { get; private set; }

        /// <inheritdoc />
        public string CarryKeyDisplay => "F";

        private Rigidbody _held;
        private Camera _camera;
        private float _pitch;
        private bool _cachedGravity;

        private void Awake()
        {
            // Moved by transform, so the body must not be simulated - but it must exist, or no
            // trigger volume will ever see this rig.
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var capsule = GetComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
            // The rig's origin sits at eye level, so the capsule hangs below it.
            capsule.center = new Vector3(0f, -0.9f, 0f);
        }

        private void Start()
        {
            _camera = GetComponentInChildren<Camera>();
            Cursor.lockState = CursorLockMode.Locked;
        }

        private void OnDisable()
        {
            if (IsCarrying)
                Drop();
            Cursor.lockState = CursorLockMode.None;
        }

        private void Update()
        {
            Look();
            Move();

            HoverTarget = IsCarrying ? null : Probe();

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame)
            {
                if (IsCarrying)
                    Drop();
                else
                    TryPickUp();
            }

            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None
                    : CursorLockMode.Locked;
        }

        private void FixedUpdate()
        {
            if (!IsCarrying || _camera == null)
                return;

            Vector3 target = _camera.transform.position + _camera.transform.forward * _carryDistance;
            _held.linearVelocity = Vector3.ClampMagnitude((target - _held.position) * _followStrength, 10f);
        }

        /// <inheritdoc />
        public void Drop()
        {
            if (!IsCarrying)
                return;

            _held.useGravity = _cachedGravity;
            _held.linearVelocity = Vector3.zero;
            _held.angularVelocity = Vector3.zero;
            _held = null;
        }

        private void TryPickUp()
        {
            Rigidbody body = Probe();
            if (body == null)
                return;

            Carryable carryable = body.GetComponent<Carryable>();
            if (carryable != null && carryable.Holder != null)
            {
                if (!carryable.Holder.Removable)
                    return;
                carryable.Holder.Eject();
            }

            if (body.isKinematic || body.mass > _maxCarryMass)
                return;

            _held = body;
            _cachedGravity = body.useGravity;
            body.useGravity = false;
        }

        private Rigidbody Probe()
        {
            if (_camera == null)
                return null;

            if (!Physics.SphereCast(_camera.transform.position, 0.25f, _camera.transform.forward,
                    out RaycastHit hit, _pickupRange, ~0, QueryTriggerInteraction.Ignore))
                return null;

            Rigidbody body = hit.rigidbody;
            if (body == null || body.isKinematic || body.mass > _maxCarryMass)
                return null;

            // Honours the author-facing tickbox, exactly as the shipping carry does: an unticked
            // Carryable refuses, and a prop with no Carryable at all is still grabbable.
            Carryable carryable = body.GetComponent<Carryable>();
            return carryable == null || carryable.CanBeCarried ? body : null;
        }

        private void Look()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || _camera == null || Cursor.lockState != CursorLockMode.Locked)
                return;

            Vector2 delta = mouse.delta.ReadValue() * _lookSensitivity;
            transform.Rotate(Vector3.up, delta.x, Space.World);
            _pitch = Mathf.Clamp(_pitch - delta.y, -85f, 85f);
            _camera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void Move()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;

            Vector3 input = Vector3.zero;
            if (kb.wKey.isPressed) input.z += 1f;
            if (kb.sKey.isPressed) input.z -= 1f;
            if (kb.dKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed) input.x -= 1f;

            if (input.sqrMagnitude < 0.01f)
                return;

            Vector3 world = transform.TransformDirection(input.normalized);
            world.y = 0f;
            transform.position += world.normalized * (_moveSpeed * Time.deltaTime);
        }
    }
}
