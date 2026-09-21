using System;
using JamKit.InteractionKit;
using JamKit.KCC;
using UnityEngine;
using UnityEngine.InputSystem;

// PlayerCarry: Portal-style physics pick up / hold / drop.
//
// Game-side on purpose, and it is the one piece of the interaction layer that did NOT move into the
// module. A carry is direct rigidbody physics against a specific character controller - the block
// below calls into the KCC motor so a grabbed prop leaves its ground probes - and a module that
// referenced the KCC module would break the rule that modules never reference each other. So the
// kit states what it needs through ICarrier and this supplies it.
//
// The held object is moved by velocity toward a carry point in front of the view, never parented.
// Parenting is what makes naive carries shove props through walls: a parented transform teleports
// and skips collision entirely. Velocity-following keeps the prop a real physics body, so it
// bumps door frames and stacks on surfaces like the player expects.
//
// Put this on the player root (the KccPlayer prefab root) and leave _carryOrigin empty to pick up
// Camera.main at Start.
//
// Known KCC caveat: the character motor sweeps its capsule with physics queries, and Unity's
// Physics.IgnoreCollision does not apply to queries. So a prop held low enough to overlap the
// capsule can still block the player's own movement. _minCarryHeight clamps the carry point above
// the capsule base to keep that out of normal play; do not remove it without re-testing looking
// straight down while carrying.
public class PlayerCarry : MonoBehaviour, ICarrier
{
    [Tooltip("Where the carried object is held, and the origin of the pickup probe. Empty = Camera.main at Start.")]
    [SerializeField] private Transform _carryOrigin;

    [Tooltip("Action that grabs when empty-handed and drops when carrying. Bind it to Gameplay/Carry. Empty falls back to the F key.")]
    [SerializeField] private InputActionReference _carryAction;

    [Header("Pickup")]
    [Tooltip("How far ahead the pickup probe reaches.")]
    [SerializeField] private float _pickupRange = 2.8f;
    [Tooltip("Radius of the pickup probe. Wider is more forgiving to aim with.")]
    [SerializeField] private float _pickupRadius = 0.3f;
    [Tooltip("Layers that can be picked up.")]
    [SerializeField] private LayerMask _pickupLayers = ~0;
    [Tooltip("Rigidbodies heavier than this refuse to be picked up.")]
    [SerializeField] private float _maxCarryMass = 25f;
    [Tooltip("Aim-assist reach. When the aim ray misses a prop and lands on floor/wall/empty air, grab the nearest carryable within this radius of the looked-at point, so a small object on the floor does not demand a steep, precise downward aim (or a crouch).")]
    [SerializeField] private float _grabAssistRadius = 0.7f;

    [Header("Carry")]
    [Tooltip("Distance in front of the carry origin the object is held at.")]
    [SerializeField] private float _carryDistance = 1.7f;
    [Tooltip("Lowest the carry point may sit relative to the carry origin. Looking down lowers the held prop to here, so it must reach far enough below eye level to place a prop into a low socket (e.g. a keyhole near the floor) while standing. The motor collider-ignore already keeps a low-held prop from disturbing the character, so this can sit well below the capsule.")]
    [SerializeField] private float _minCarryHeight = -0.9f;
    [Tooltip("How hard the object is pulled toward the carry point. Higher is stiffer and less floaty.")]
    [SerializeField] private float _followStrength = 14f;
    [Tooltip("Speed cap while following, so a snapped-back prop does not become a projectile. Must be high enough for the prop to swing around the player during a fast turn inside the break grace window.")]
    [SerializeField] private float _maxCarrySpeed = 10f;
    [Tooltip("If the object ends up further than this from the carry point (wedged behind geometry) it is released.")]
    [SerializeField] private float _breakDistance = 2.5f;
    [Tooltip("How long the object must stay beyond the break distance before it is actually released. Without this, a fast turn releases it mid-swing.")]
    [SerializeField] private float _breakGraceSeconds = 0.6f;
    [Tooltip("Keep the object's orientation locked to the view while carried.")]
    [SerializeField] private bool _rotateWithView = true;
    [Tooltip("Exponential-decay rate the carried object's rotation closes on the view-locked target with.")]
    [SerializeField] private float _rotateResponse = 12f;
    [Tooltip("Linear/angular damping applied while carried, so the object settles instead of wobbling.")]
    [SerializeField] private float _carryDamping = 8f;

    [Tooltip("Colliders the carried object should not physically collide with. Empty = every collider under this object's root at Start.")]
    [SerializeField] private Collider[] _ignoredColliders;

    [Tooltip("The KCC character, so a held prop is also removed from the motor's ground probes. Empty = found under this object's root at Start.")]
    [SerializeField] private PlayerCharacter _character;

    /// <summary>True while an object is held.</summary>
    public bool IsCarrying => _held != null;

    /// <summary>The held rigidbody, or null.</summary>
    public Rigidbody Held => _held;

    /// <summary>The prop a grab would pick up right now, or null. Presentation reads this; it is refreshed each frame while empty-handed.</summary>
    public Rigidbody HoverTarget { get; private set; }

    /// <summary>Display name of the grab/drop binding. Exposed so a prompt names the binding actually polled, never a copy of it.</summary>
    public string CarryKeyDisplay { get; private set; } = "F";

    /// <summary>Fired when an object is picked up.</summary>
    public event Action<Rigidbody> PickedUp;

    /// <summary>Fired when an object is released, whether by the player or by the break distance.</summary>
    public event Action<Rigidbody> Dropped;

    private Rigidbody _held;
    private Collider[] _heldColliders;
    private Quaternion _grabLocalRotation;
    private float _beyondBreakTime;

    // Cached so the object is handed back exactly as it was found.
    private bool _cachedUseGravity;
    private float _cachedLinearDamping;
    private float _cachedAngularDamping;
    private RigidbodyInterpolation _cachedInterpolation;
    private CollisionDetectionMode _cachedCollisionDetection;

    private void OnEnable()
    {
        if (_carryAction != null && _carryAction.action != null)
        {
            _carryAction.action.Enable();
            CarryKeyDisplay = ResolveKeyDisplay();
        }
    }

    private void Start()
    {
        if (_carryOrigin == null && Camera.main != null)
            _carryOrigin = Camera.main.transform;

        if (_ignoredColliders == null || _ignoredColliders.Length == 0)
            _ignoredColliders = transform.root.GetComponentsInChildren<Collider>();

        if (_character == null)
            _character = transform.root.GetComponentInChildren<PlayerCharacter>();
    }

    private void Update()
    {
        // Refresh what a grab would target, so a hover highlight can show it. Cleared while carrying.
        HoverTarget = IsCarrying ? null : ProbeForPickup();

        if (!ReadPressedThisFrame())
            return;

        if (IsCarrying)
            Drop();
        else
            TryPickUp();
    }

    private void FixedUpdate()
    {
        if (!IsCarrying || _carryOrigin == null)
            return;

        Vector3 target = CarryPoint();
        Vector3 delta = target - _held.position;

        // Wedged behind geometry: let go rather than fight the solver or yank it through a wall.
        // But only once it has STAYED out of reach - a fast turn swings the carry point several
        // metres around the player in a single frame, and treating that instant as "wedged"
        // drops the prop mid-spin, which is what a 180 flick used to do.
        if (delta.magnitude > _breakDistance)
        {
            _beyondBreakTime += Time.fixedDeltaTime;
            if (_beyondBreakTime >= _breakGraceSeconds)
            {
                Drop();
                return;
            }
        }
        else
        {
            _beyondBreakTime = 0f;
        }

        _held.linearVelocity = Vector3.ClampMagnitude(delta * _followStrength, _maxCarrySpeed);

        if (_rotateWithView)
        {
            _held.angularVelocity = Vector3.zero;
            Quaternion targetRotation = _carryOrigin.rotation * _grabLocalRotation;
            _held.MoveRotation(Quaternion.Slerp(
                _held.rotation,
                targetRotation,
                JamKit.MathUtil.Decay(_rotateResponse, Time.fixedDeltaTime)));
        }
    }

    private void OnDisable()
    {
        // Never leave a prop weightless and non-colliding because the player was torn down mid-carry.
        if (IsCarrying)
            Drop();
    }

    /// <summary>Probes ahead and picks up the best eligible rigidbody. No-op if nothing qualifies.</summary>
    public void TryPickUp()
    {
        if (_carryOrigin == null || IsCarrying)
            return;

        Rigidbody body = ProbeForPickup();
        if (body == null)
            return;

        // If the prop is seated in a socket, pull it out first (if that socket allows it). Eject
        // returns the body to normal dynamic physics, so the mass/kinematic checks below still apply.
        Carryable carryable = body.GetComponent<Carryable>();
        if (carryable != null && carryable.Holder != null)
        {
            if (!carryable.Holder.Removable)
                return;
            carryable.Holder.Eject();
        }

        if (body.isKinematic || body.mass > _maxCarryMass)
            return;

        Attach(body);
    }

    // Resolves what a grab would pick up. A precise forward spherecast wins outright when it lands on
    // a real prop, so aiming straight at something always works. When it instead lands on floor/wall
    // or empty air - the case that made a small object on the ground demand a steep, precise look -
    // it falls back to the nearest carryable around the point actually being looked at.
    private Rigidbody ProbeForPickup()
    {
        if (_carryOrigin == null)
            return null;

        Vector3 origin = _carryOrigin.position;
        Vector3 forward = _carryOrigin.forward;

        RaycastHit hit;
        bool found = Physics.SphereCast(
            origin, _pickupRadius, forward, out hit,
            _pickupRange, _pickupLayers, QueryTriggerInteraction.Ignore);

        if (found && hit.rigidbody != null && !IsOwnCollider(hit.collider) && IsGrabbable(hit.rigidbody))
            return hit.rigidbody;

        Vector3 aimPoint = found ? hit.point : origin + forward * _pickupRange;
        return NearestGrabbable(aimPoint, origin);
    }

    // Nearest eligible carryable to the looked-at point, but only one with a clear line from the eye,
    // so the overlap can never reach a prop through a wall the aim ray stopped against.
    private Rigidbody NearestGrabbable(Vector3 point, Vector3 origin)
    {
        Collider[] candidates = Physics.OverlapSphere(
            point, _grabAssistRadius, _pickupLayers, QueryTriggerInteraction.Ignore);

        Rigidbody best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < candidates.Length; i++)
        {
            Collider col = candidates[i];
            Rigidbody rb = col.attachedRigidbody;
            if (rb == null || rb.isKinematic || rb.mass > _maxCarryMass)
                continue;
            if (IsOwnCollider(col) || !IsGrabbable(rb))
                continue;

            float sqr = (col.ClosestPoint(point) - point).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;
            if (!HasLineOfSight(origin, col))
                continue;

            best = rb;
            bestSqr = sqr;
        }
        return best;
    }

    // A prop with no Carryable at all is still grabbable if its physics qualify, so existing scenes
    // do not break and scattered debris still behaves. Only an explicit unticked Carryable refuses.
    private static bool IsGrabbable(Rigidbody body)
    {
        if (body == null)
            return false;
        Carryable carryable = body.GetComponent<Carryable>();
        return carryable == null || carryable.CanBeCarried;
    }

    // Rejects a candidate the aim cannot actually see, so grab-assist never reaches through a wall.
    private bool HasLineOfSight(Vector3 origin, Collider target)
    {
        RaycastHit hit;
        if (!Physics.Linecast(origin, target.bounds.center, out hit, _pickupLayers, QueryTriggerInteraction.Ignore))
            return true;
        return hit.collider == target
            || hit.collider.attachedRigidbody == target.attachedRigidbody
            || IsOwnCollider(hit.collider);
    }

    private bool IsOwnCollider(Collider col)
    {
        if (col == null || _ignoredColliders == null)
            return false;
        for (int i = 0; i < _ignoredColliders.Length; i++)
            if (_ignoredColliders[i] == col)
                return true;
        return false;
    }

    /// <summary>Releases the held object at rest, so it sets down instead of being thrown.</summary>
    public void Drop()
    {
        if (!IsCarrying)
            return;

        Rigidbody body = _held;

        body.useGravity = _cachedUseGravity;
        body.linearDamping = _cachedLinearDamping;
        body.angularDamping = _cachedAngularDamping;
        body.interpolation = _cachedInterpolation;
        body.collisionDetectionMode = _cachedCollisionDetection;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        SetCollisionIgnored(false);

        _held = null;
        _heldColliders = null;

        Dropped?.Invoke(body);
    }

    private void Attach(Rigidbody body)
    {
        _held = body;
        _heldColliders = body.GetComponentsInChildren<Collider>();
        _beyondBreakTime = 0f;

        _cachedUseGravity = body.useGravity;
        _cachedLinearDamping = body.linearDamping;
        _cachedAngularDamping = body.angularDamping;
        _cachedInterpolation = body.interpolation;
        _cachedCollisionDetection = body.collisionDetectionMode;

        body.useGravity = false;
        body.linearDamping = _carryDamping;
        body.angularDamping = _carryDamping;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        // Continuous so a fast-moving held prop does not tunnel through a door frame or the floor.
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        SetCollisionIgnored(true);

        _grabLocalRotation = Quaternion.Inverse(_carryOrigin.rotation) * body.rotation;

        PickedUp?.Invoke(body);
    }

    private Vector3 CarryPoint()
    {
        Vector3 point = _carryOrigin.position + _carryOrigin.forward * _carryDistance;
        float lowest = _carryOrigin.position.y + _minCarryHeight;
        if (point.y < lowest)
            point.y = lowest;
        return point;
    }

    private void SetCollisionIgnored(bool ignored)
    {
        if (_heldColliders == null)
            return;

        for (int i = 0; i < _heldColliders.Length; i++)
        {
            Collider held = _heldColliders[i];
            if (held == null)
                continue;

            // Also pull the held collider out of the KCC motor's queries. Physics.IgnoreCollision
            // below only stops the rigidbody solver; the motor's ground probe still sees it, so a
            // prop grabbed while standing on it would read as ground and lift the player upward.
            if (_character != null)
                _character.SetColliderIgnored(held, ignored);

            if (_ignoredColliders == null)
                continue;

            for (int j = 0; j < _ignoredColliders.Length; j++)
            {
                Collider other = _ignoredColliders[j];
                if (other == null)
                    continue;
                Physics.IgnoreCollision(held, other, ignored);
            }
        }
    }

    private bool ReadPressedThisFrame()
    {
        if (_carryAction != null && _carryAction.action != null)
            return _carryAction.action.WasPressedThisFrame();

        // Fallback so a rig with no action wired still works in a fresh scene. CarryKeyDisplay
        // stays "F", so the prompt names whichever of the two is actually being polled.
        Keyboard kb = Keyboard.current;
        return kb != null && kb.fKey.wasPressedThisFrame;
    }

    private string ResolveKeyDisplay()
    {
        InputAction action = _carryAction != null ? _carryAction.action : null;
        if (action == null || action.bindings.Count == 0)
            return "F";

        try
        {
            string display = action.GetBindingDisplayString(
                InputBinding.DisplayStringOptions.DontIncludeInteractions);
            return string.IsNullOrEmpty(display) ? "F" : display;
        }
        catch (Exception)
        {
            // Throws on a composite with no resolved control, which is normal before any device is
            // present. The key name is cosmetic; never break the rig for it.
            return "F";
        }
    }

    /// <summary>Code-built setup (demo and runtime-authored rooms); in the editor set the serialized fields.</summary>
    public void Configure(Transform carryOrigin, Collider[] ignoredColliders)
    {
        _carryOrigin = carryOrigin;
        _ignoredColliders = ignoredColliders;
    }
}
