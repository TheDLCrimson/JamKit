using Alchemy.Inspector;
using KinematicCharacterController;
using UnityEngine;
using UnityEngine.UIElements;

namespace JamKit.KCC
{
    /// <summary>
    /// A KCC motor-driven first-person character controller: walk/crouch with exponential-decay
    /// velocity response, a stand/crouch/slide state machine with landing-slide velocity
    /// reprojection, air control, jump with coyote time and input buffering, and an
    /// overlap-checked uncrouch.
    /// </summary>
    /// <remarks>
    /// Requires the Kinematic Character Controller asset (paid, Asset Store). See INSTALL.md.
    /// Driven by <see cref="Player"/>; this component does not read input itself.
    /// </remarks>
    public class PlayerCharacter : MonoBehaviour, ICharacterController
    {
        [SerializeField] private KinematicCharacterMotor _motor;
        [SerializeField] private Transform _root;
        [SerializeField] private Transform _cameraTarget;

        [Title("Walk")]
        [SerializeField] private bool _walkEnabled = true;
        [ShowIf(nameof(_walkEnabled))]
        [SerializeField] private float _walkSpeed = 20f;
        [ShowIf(nameof(_walkEnabled))]
        [SerializeField] private float _walkResponse = 25f;

        [Title("Crouch")]
        [SerializeField] private bool _crouchEnabled = true;
        [ShowIf(nameof(_crouchEnabled))]
        [SerializeField] private float _crouchSpeed = 7f;
        [ShowIf(nameof(_crouchEnabled))]
        [SerializeField] private float _crouchResponse = 20f;
        [ShowIf(nameof(_crouchEnabled))]
        [SerializeField] private float _crouchHeight = 1f;
        [ShowIf(nameof(_crouchEnabled))]
        [SerializeField] private float _crouchHeightResponse = 15f;
        [ShowIf(nameof(_crouchEnabled))]
        [Range(0f, 1f)]
        [SerializeField] private float _crouchCameraTargetHeight = 0.7f;

        [Title("Stand")]
        [SerializeField] private float _standHeight = 2f;
        [Range(0f, 1f)]
        [SerializeField] private float _standCameraTargetHeight = 0.9f;

        [Title("Air Control")]
        [SerializeField] private bool _airControlEnabled = true;
        [ShowIf(nameof(_airControlEnabled))]
        [SerializeField] private float _airSpeed = 15f;
        [ShowIf(nameof(_airControlEnabled))]
        [SerializeField] private float _airAcceleration = 70f;

        [Title("Jump")]
        [SerializeField] private bool _jumpEnabled = true;
        [ShowIf(nameof(_jumpEnabled))]
        [SerializeField] private float _jumpSpeed = 20f;
        [ShowIf(nameof(_jumpEnabled))]
        [SerializeField] private float _coyoteTime = 0.2f;
        [ShowIf(nameof(_jumpEnabled))]
        [Range(0f, 1f)]
        [SerializeField] private float _jumpSustainGravity = 0.4f;
        [SerializeField] private float _gravity = -90f;
        
        [Title("Slide")]
        [Tooltip("Slide requires Crouch to be enabled; it is how the character enters the Slide stance.")]
        [EnableIf(nameof(_crouchEnabled))]
        [SerializeField] private bool _slideEnabled = true;
        [ShowIf(nameof(SlideEffectivelyEnabled))]
        [SerializeField] private float _slideStartSpeed = 25f;
        [ShowIf(nameof(SlideEffectivelyEnabled))]
        [SerializeField] private float _slideEndSpeed = 15f;
        [ShowIf(nameof(SlideEffectivelyEnabled))]
        [SerializeField] private float _slideFriction = 0.8f;
        [ShowIf(nameof(SlideEffectivelyEnabled))]
        [SerializeField] private float _slideSteerAcceleration = 5f;
        [ShowIf(nameof(SlideEffectivelyEnabled))]
        [SerializeField] private float _slideGravity = -90f;

        /// <summary>Slide additionally requires Crouch to be enabled, since Slide is entered from Crouch.</summary>
        private bool SlideEffectivelyEnabled => _crouchEnabled && _slideEnabled;

        private CharacterState _state;
        private CharacterState _lastState;
        private CharacterState _tempState;

        private Quaternion _requestedRotation;
        private Vector3 _requestedMovement;
        private bool _requestedJump;
        private bool _requestedSustainedJump;
        private bool _requestedCrouch;
        private bool _requestedCrouchInAir;

        private float _timeSinceUngrounded;
        private float _timeSinceJumpRequest;
        private bool _ungroundedDueToJump;

        private Collider[] _uncrouchOverlapResults;

        /// <summary>
        /// Binds this controller to its motor. Call once, before the first update.
        /// </summary>
        public void Initialize()
        {
            _state.Stance = Stance.Stand;
            _lastState = _state;

            _uncrouchOverlapResults = new Collider[8];
            _motor.CharacterController = this;
        }

        /// <summary>
        /// Feeds one frame of input. Call from Update, before <see cref="UpdateBody"/>.
        /// </summary>
        public void UpdateInput(CharacterInput input)
        {
            _requestedRotation = input.Rotation;
            // Take the 2D input and make a 3D movement vector on the XZ plane
            _requestedMovement = new Vector3(input.Move.x, 0f, input.Move.y);
            // Clamp the length <= 1f to prevent diagonal movement from being faster
            // This way is better than normalized cuz it preserves analog stick strength
            _requestedMovement = Vector3.ClampMagnitude(_requestedMovement, 1f);
            // Orient the input so it is relative to the direction the player is looking
            _requestedMovement = input.Rotation * _requestedMovement;

            var wasRequestingJump = _requestedJump;
            // Ability-disabled jump requests are dropped at the source, so a toggle takes effect
            // immediately rather than waiting for the next fresh press.
            _requestedJump = _jumpEnabled && (_requestedJump || input.Jump);
            if (_requestedJump && !wasRequestingJump)
                _timeSinceJumpRequest = 0f;

            _requestedSustainedJump = _jumpEnabled && input.JumpSustain;

            var wasRequestedCrouch = _requestedCrouch;
            var toggledCrouch = input.Crouch switch
            {
                CrouchInput.Toggle => !_requestedCrouch,
                CrouchInput.None => _requestedCrouch,
                _ => _requestedCrouch
            };
            // Same immediate-effect rule as jump: disabling Crouch mid-crouch stands the
            // character back up via the normal uncrouch path in AfterCharacterUpdate.
            _requestedCrouch = _crouchEnabled && toggledCrouch;
            if (_requestedCrouch && !wasRequestedCrouch)
                _requestedCrouchInAir = !_state.Grounded;
            else if (!_requestedCrouch && wasRequestedCrouch)
                _requestedCrouchInAir = false;
        }

        /// <summary>
        /// Smooths the capsule/camera height toward the current stance. Call from Update.
        /// </summary>
        public void UpdateBody(float deltaTime)
        {
            var currentHeight = _motor.Capsule.height;
            var normalizedHeight = currentHeight / _standHeight;

            var cameraTargetHeight = currentHeight *
            (
                _state.Stance is Stance.Stand
                ? _standCameraTargetHeight
                : _crouchCameraTargetHeight
            );
            var rootTargetScale = new Vector3(1f, normalizedHeight, 1f);

            _cameraTarget.localPosition = MathUtil.Decay
            (
                _cameraTarget.localPosition,
                new Vector3(0f, cameraTargetHeight, 0f),
                _crouchHeightResponse,
                deltaTime
            );
            _root.localScale = MathUtil.Decay
            (
                _root.localScale,
                rootTargetScale,
                _crouchHeightResponse,
                deltaTime
            );
        }

        /// <summary>
        /// This is called when the motor wants to know what its velocity should be right now
        /// </summary>
        public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
        {
            // Reset the acceleration
            _state.Acceleration = Vector3.zero;

            // If on ground
            if (_motor.GroundingStatus.IsStableOnGround)
            {
                _timeSinceUngrounded = 0f;
                _ungroundedDueToJump = false;

                // Snap the requested movement direction to the angle of the surface the character is walking on
                var groundedMovement = _motor.GetDirectionTangentToSurface
                (
                    direction: _requestedMovement,
                    surfaceNormal: _motor.GroundingStatus.GroundNormal
                ) * _requestedMovement.magnitude;

                // Start Sliding
                {
                    var moving = groundedMovement.sqrMagnitude > 0f;
                    var crouching = _state.Stance is Stance.Crouch;
                    var wasStanding = _lastState.Stance is Stance.Stand;
                    var wasInAir = !_lastState.Grounded;

                    if (SlideEffectivelyEnabled && moving && crouching && (wasStanding || wasInAir))
                    {
                        _state.Stance = Stance.Slide;

                        // When landing on stable ground the character motor project the velocity onto a flat ground plane
                        // See: KinematicCharacterMotor.HandleVelocityProjection()
                        // This is normally good, because under normal circumstances the player shouldn't slide when landing on the ground
                        // In  this case, we WANT the player to slide.
                        // Reproject the last frames (falling) velocity onto the ground normal to slide
                        if (wasInAir)
                        {
                            currentVelocity = Vector3.ProjectOnPlane
                            (
                                _lastState.Velocity,
                                _motor.GroundingStatus.GroundNormal
                            );
                        }

                        var effectiveSlideStartSpeed = _slideStartSpeed;
                        if (!_lastState.Grounded && !_requestedCrouchInAir)
                        {
                            effectiveSlideStartSpeed = 0f;
                        }

                        var slideSpeed = Mathf.Max(effectiveSlideStartSpeed, currentVelocity.magnitude);
                        currentVelocity = _motor.GetDirectionTangentToSurface
                        (
                            direction: currentVelocity,
                            surfaceNormal: _motor.GroundingStatus.GroundNormal
                        ) * slideSpeed;

#if UNITY_EDITOR
                        Debug.DrawRay(transform.position, currentVelocity, Color.green, 5f);
#endif
                    }
                }

                // Move
                if (_state.Stance is Stance.Stand or Stance.Crouch)
                {
                    // Calculate speed and acceleration for the current stance
                    var moveEnabled = _state.Stance is Stance.Stand ? _walkEnabled : _crouchEnabled;
                    var speed = _state.Stance is Stance.Stand ? _walkSpeed : _crouchSpeed;
                    var response = _state.Stance is Stance.Stand ? _walkResponse : _crouchResponse;

                    // Smoothly move along the ground in that direction, decaying to a stop if the
                    // current stance's move ability is disabled
                    var targetVelocity = moveEnabled ? groundedMovement * speed : Vector3.zero;
                    var moveVelocity = MathUtil.Decay
                    (
                        currentVelocity,
                        targetVelocity,
                        response,
                        deltaTime
                    );

                    _state.Acceleration = (moveVelocity - currentVelocity) / deltaTime;

                    currentVelocity = moveVelocity;
                }
                // Continue sliding
                else
                {
                    // Apply friction
                    currentVelocity -= currentVelocity * (_slideFriction * deltaTime);

                    // Slope
                    {
                        var force = Vector3.ProjectOnPlane
                        (
                            vector: -_motor.CharacterUp,
                            planeNormal: _motor.GroundingStatus.GroundNormal
                        ) * _slideGravity;

                        currentVelocity -= force * deltaTime;
                    }

                    // Steer
                    {
                        // Current velocity
                        var currentSpeed = currentVelocity.magnitude;
                        var targetVelocity = groundedMovement * currentSpeed;
                        var steerVelocity = currentVelocity;
                        var steerForce = (targetVelocity - steerVelocity) * _slideSteerAcceleration * deltaTime;
                        // Add steer force but clamp to the current speed so speed does not increase
                        steerVelocity += steerForce;
                        steerVelocity = Vector3.ClampMagnitude(steerVelocity, currentSpeed);

                        _state.Acceleration = (steerVelocity - currentVelocity) / deltaTime;

                        currentVelocity = steerVelocity;
                    }

                    // Stop sliding
                    if (currentVelocity.magnitude < _slideEndSpeed)
                        _state.Stance = Stance.Crouch;
                }

            }
            // In air
            else
            {
                _timeSinceUngrounded += deltaTime;

                // Move
                if (_airControlEnabled && _requestedMovement.sqrMagnitude > 0f)
                {
                    // Requested movement projected onto movement plane (magnitude preserved)
                    var planarMovement = Vector3.ProjectOnPlane
                    (
                        _requestedMovement,
                        _motor.CharacterUp
                    ) * _requestedMovement.magnitude;

                    // Current velocity on movement plane
                    var currentPlanarVelocity = Vector3.ProjectOnPlane
                    (
                        currentVelocity,
                        _motor.CharacterUp
                    );

                    // Calculate movement force, will be changed depending on current velocity.
                    var movementForce = planarMovement * _airAcceleration * deltaTime;

                    // If moving slower than the max air speed, treat movementForce as a simple steering force
                    if (currentPlanarVelocity.magnitude < _airSpeed)
                    {
                        // Add the movement force to the current velocity for a target velocity
                        var targetPlanarVelocity = currentPlanarVelocity + movementForce;

                        // Clamp the target velocity to the max air speed
                        targetPlanarVelocity = Vector3.ClampMagnitude(targetPlanarVelocity, _airSpeed);

                        // Steer to the target velocity
                        movementForce = targetPlanarVelocity - currentPlanarVelocity;
                    }
                    // Otherwise, nerf the movement force when it is in the direction of the current planar velocity
                    // to prevent acceleratiing further beyond the max air speed
                    else if (Vector3.Dot(currentPlanarVelocity, movementForce) > 0f)
                    {
                        var constrainedMovementForce = Vector3.ProjectOnPlane
                        (
                            movementForce,
                            currentPlanarVelocity.normalized
                        );

                        movementForce = constrainedMovementForce;
                    }

                    // Prevent air-climbing steep slopes
                    if (_motor.GroundingStatus.FoundAnyGround)
                    {
                        // If moving in the same direction as resultant velocity
                        if (Vector3.Dot(movementForce, currentVelocity + movementForce) > 0f)
                        {
                            // Calculate obstruction normal
                            var obstructionNormal = Vector3.Cross
                            (
                                _motor.CharacterUp,
                                Vector3.Cross
                                (
                                    _motor.CharacterUp,
                                    _motor.GroundingStatus.GroundNormal
                                )
                            ).normalized;

                            // Project movement force onto obstruction plane
                            movementForce = Vector3.ProjectOnPlane(movementForce, obstructionNormal);
                        }
                    }

                    currentVelocity += movementForce;
                }

                // Gravity
                var effectiveGravity = _gravity;
                var verticalSpeed = Vector3.Dot(currentVelocity, _motor.CharacterUp);
                if (_requestedSustainedJump && verticalSpeed > 0f)
                    effectiveGravity *= _jumpSustainGravity;

                currentVelocity += _motor.CharacterUp * effectiveGravity * deltaTime;
            }

            if (_requestedJump)
            {
                var grounded = _motor.GroundingStatus.IsStableOnGround;
                var canCoyoteJump = _timeSinceUngrounded < _coyoteTime && !_ungroundedDueToJump;

                if (grounded || canCoyoteJump)
                {
                    _requestedJump = false; // Unset jump request
                    _requestedCrouch = false; // Unset crouch request
                    _requestedCrouchInAir = false;

                    // Unstick the character from the ground
                    _motor.ForceUnground(time: 0.1f);
                    _ungroundedDueToJump = true;

                    // Set a minimum vertical speed to the jump speed
                    var currentVerticalSpeed = Vector3.Dot(currentVelocity, _motor.CharacterUp);
                    var targetVerticalSpeed = Mathf.Max(currentVerticalSpeed, _jumpSpeed);
                    // Add the difference in current and target vertical speed to the character's velocity
                    currentVelocity += _motor.CharacterUp * (targetVerticalSpeed - currentVerticalSpeed);
                }
                else
                {
                    _timeSinceJumpRequest += deltaTime;

                    // Defer the jump request until coyote time has passed
                    var canJumpLater = _timeSinceJumpRequest < _coyoteTime;
                    _requestedJump = canJumpLater;
                }

            }
        }

        /// <summary>
        /// This is called when the motor wants to know what its rotation should be right now
        /// </summary>
        public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
        {
            /* Update the character's rotation to face in the same direction
            as the requested rotation (camera rotation) */

            /* We do NOT want the character to pitch up and down, so the direction the character
            looks should always be "flatten" */

            /* This is done by projecting a vector pointing in the same direction that
            the player is looking onto a flat ground plane */

            var forward = Vector3.ProjectOnPlane
            (
                _requestedRotation * Vector3.forward,
                _motor.CharacterUp
            ); // This can be modified

            if (forward != Vector3.zero)
                currentRotation = Quaternion.LookRotation(forward, _motor.CharacterUp);
        }

        /// <summary>
        /// This is called before the motor does anything
        /// </summary>
        public void BeforeCharacterUpdate(float deltaTime)
        {
            _tempState = _state;
            // Crouch
            if (_requestedCrouch && _state.Stance is Stance.Stand)
            {
                _state.Stance = Stance.Crouch;
                _motor.SetCapsuleDimensions
                (
                    radius: _motor.Capsule.radius,
                    height: _crouchHeight,
                    yOffset: _crouchHeight * 0.5f
                );
            }
        }

        /// <summary>
        /// This is called after the motor has finished its ground probing, but before PhysicsMover/Velocity/etc.... handling
        /// </summary>
        public void PostGroundingUpdate(float deltaTime)
        {
            if (!_motor.GroundingStatus.IsStableOnGround && _state.Stance is Stance.Slide)
                _state.Stance = Stance.Crouch;
        }

        /// <summary>
        /// This is called after the motor has finished everything in its update
        /// </summary>
        public void AfterCharacterUpdate(float deltaTime)
        {
            // Uncrouch
            if (!_requestedCrouch && _state.Stance is not Stance.Stand)
            {
                // Tentatively uncrouch
                _motor.SetCapsuleDimensions
                (
                    radius: _motor.Capsule.radius,
                    height: _standHeight,
                    yOffset: _standHeight * 0.5f
                );

                // Check if the character overlaps with anything
                var overlapResult = _motor.CharacterOverlap
                (
                    _motor.TransientPosition,
                    _motor.TransientRotation,
                    _uncrouchOverlapResults,
                    _motor.CollidableLayers,
                    QueryTriggerInteraction.Ignore
                );

                if (overlapResult > 0)
                {
                    //Re-crouch
                    _requestedCrouch = true;
                    _motor.SetCapsuleDimensions
                    (
                        radius: _motor.Capsule.radius,
                        height: _crouchHeight,
                        yOffset: _crouchHeight * 0.5f
                    );
                }
                else
                {
                    _state.Stance = Stance.Stand;
                }
            }
            // Update state to reflect relevant motor properties
            _state.Grounded = _motor.GroundingStatus.IsStableOnGround;
            _state.Velocity = _motor.Velocity;
            // And update the _lastState to store the character state snapshot
            // taken at the beginning of this character update
            _lastState = _tempState;

        }

        /// <summary>
        /// This is called when the motor's ground probing detects a ground hit
        /// </summary>
        public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) { }

        /// <summary>
        /// This is called when the motor's movement logic detects a hit
        /// </summary>
        public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) { }

        // Colliders the motor must not see in its queries. Empty in the common case, and the
        // Count check below keeps the per-query cost at zero when nothing is ignored.
        private readonly System.Collections.Generic.HashSet<Collider> _ignoredForQueries =
            new System.Collections.Generic.HashSet<Collider>();

        /// <summary>
        /// Excludes a collider from the motor's own physics QUERIES, or restores it.
        /// </summary>
        /// <remarks>
        /// This exists for carried props. <c>Physics.IgnoreCollision</c> only stops the rigidbody
        /// solver; it does not apply to queries, and the motor ground-probes with queries. So a prop
        /// picked up while the player stands on it still reads as ground, and the player rides it
        /// upward in a feedback loop - the "carry elevator". A carry implementation calls this on
        /// grab and again on release.
        /// </remarks>
        public void SetColliderIgnored(Collider collider, bool ignored)
        {
            if (collider == null)
                return;
            if (ignored)
                _ignoredForQueries.Add(collider);
            else
                _ignoredForQueries.Remove(collider);
        }

        /// <summary>
        /// This is called after when the motor wants to know if the collider can be collided with (or if we just go through it)
        /// </summary>
        public bool IsColliderValidForCollisions(Collider coll) =>
            _ignoredForQueries.Count == 0 || !_ignoredForQueries.Contains(coll);

        /// <summary>
        /// This is called when the character detects discrete collisions (collisions that don't result from the motor's capsuleCasts when moving)
        /// </summary>
        public void OnDiscreteCollisionDetected(Collider hitCollider) { }

        /// <summary>
        /// This is called after every move hit, to give you an opportunity to modify the HitStabilityReport to your liking
        /// </summary>
        public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport) { }

        /// <summary>
        /// The transform the camera should follow. Sits at eye height and moves with the stance.
        /// </summary>
        public Transform GetCameraTarget() => _cameraTarget;

        /// <summary>
        /// The character's motion state as of the most recent character update.
        /// </summary>
        public CharacterState GetState() => _state;

        /// <summary>
        /// The character's motion state as of the previous character update.
        /// </summary>
        public CharacterState GetLastState() => _lastState;

        /// <summary>
        /// Teleports the character, bypassing the motor's collision sweep.
        /// </summary>
        public void SetPosition(Vector3 position, bool killVelocity = true)
        {
            _motor.SetPosition(position);
            if (killVelocity)
                _motor.BaseVelocity = Vector3.zero;
        }
    }
}
