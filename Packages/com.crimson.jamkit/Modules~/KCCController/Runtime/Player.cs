using UnityEngine;
using UnityEngine.InputSystem;

namespace JamKit.KCC
{
    /// <summary>
    /// The composition root for the KCC first-person player: wires input to the character and
    /// camera, and ticks them in an explicit, deterministic order (input and body in Update,
    /// camera and procedural motion in LateUpdate).
    /// </summary>
    /// <remarks>
    /// Input comes from <see cref="InputActionReference"/>s pointing at the project's shared
    /// actions asset (JamActions, Gameplay map). This module ships no actions asset of its own;
    /// see INSTALL.md for the required actions.
    /// </remarks>
    public class Player : MonoBehaviour
    {
        [SerializeField] private PlayerCharacter _playerCharacter;
        [SerializeField] private PlayerCamera _playerCamera;
        [Space]
        [Tooltip("Optional. JamKit procedural camera motion; leave empty to run without it.")]
        [SerializeField] private CameraSpring _cameraSpring;
        [Tooltip("Optional. JamKit procedural camera motion; leave empty to run without it.")]
        [SerializeField] private CameraLean _cameraLean;
        [Tooltip("Scales CameraLean's base strength while sliding. CameraLean's default base is 0.075, " +
                 "so 2.667 yields the 0.2 slide strength this controller is tuned around.")]
        [SerializeField] private float _slideLeanMultiplier = 2.6666667f;
        [Space]
        [Tooltip("Gameplay/Move (Vector2)")]
        [SerializeField] private InputActionReference _moveAction;
        [Tooltip("Gameplay/Look (Vector2)")]
        [SerializeField] private InputActionReference _lookAction;
        [Tooltip("Gameplay/Jump (Button)")]
        [SerializeField] private InputActionReference _jumpAction;
        [Tooltip("Gameplay/Crouch (Button)")]
        [SerializeField] private InputActionReference _crouchAction;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;

            if (_playerCharacter == null || _playerCamera == null)
            {
                Debug.LogError($"{nameof(Player)}: _playerCharacter and _playerCamera must be assigned.", this);
                enabled = false;
                return;
            }

            if (_moveAction == null || _lookAction == null || _jumpAction == null || _crouchAction == null)
            {
                Debug.LogError
                (
                    $"{nameof(Player)}: all four input action references must be assigned " +
                    "(Move, Look, Jump, Crouch on the Gameplay map). See INSTALL.md.",
                    this
                );
                enabled = false;
                return;
            }

            _playerCharacter.Initialize();
            _playerCamera.Initialize(_playerCharacter.GetCameraTarget());

            if (_cameraSpring != null)
                _cameraSpring.Initialize();
            if (_cameraLean != null)
                _cameraLean.Initialize();
        }

        private void OnEnable()
        {
            _moveAction?.action.Enable();
            _lookAction?.action.Enable();
            _jumpAction?.action.Enable();
            _crouchAction?.action.Enable();
        }

        private void OnDisable()
        {
            _moveAction?.action.Disable();
            _lookAction?.action.Disable();
            _jumpAction?.action.Disable();
            _crouchAction?.action.Disable();
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;

            // Get camera input and update its rotation
            var cameraInput = new CameraInput { Look = _lookAction.action.ReadValue<Vector2>() };
            _playerCamera.UpdateRotation(cameraInput);

            // Get character input and update its movement
            var characterInput = new CharacterInput
            {
                Rotation = _playerCamera.transform.rotation,
                Move = _moveAction.action.ReadValue<Vector2>(),
                Jump = _jumpAction.action.WasPressedThisFrame(),
                JumpSustain = _jumpAction.action.IsPressed(),
                Crouch = _crouchAction.action.WasPressedThisFrame() ? CrouchInput.Toggle : CrouchInput.None
            };
            _playerCharacter.UpdateInput(characterInput);
            _playerCharacter.UpdateBody(deltaTime);
        }

        private void LateUpdate()
        {
            var deltaTime = Time.deltaTime;
            var cameraTarget = _playerCharacter.GetCameraTarget();
            var state = _playerCharacter.GetState();

            _playerCamera.UpdatePosition(cameraTarget);

            if (_cameraSpring != null)
                _cameraSpring.UpdateSpring(deltaTime, cameraTarget.up);
            if (_cameraLean != null)
            {
                var leanMultiplier = state.Stance is Stance.Slide && state.Grounded ? _slideLeanMultiplier : 1f;
                _cameraLean.UpdateLean(deltaTime, state.Acceleration, cameraTarget.up, leanMultiplier);
            }
        }

        /// <summary>
        /// Teleports the player to a world position.
        /// </summary>
        public void Teleport(Vector3 position)
        {
            _playerCharacter.SetPosition(position);
        }
    }
}
