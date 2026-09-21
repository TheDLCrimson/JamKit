using JamKit;
using UnityEngine;
using UnityEngine.InputSystem;

// PlayerMove: minimal sample movement - flat speed, no acceleration curve, no camera-relative input, no
// juice. Deliberately simple so the Lockdown sample has a working player without prescribing a feel; swap
// in your own controller or a genre module (see Modules~) for real gameplay. Feel-tuning is human-owned
// (core mechanic feel is never delegated) - this script intentionally does none. Plain Assembly-CSharp, no
// namespace.
[RequireComponent(typeof(CharacterController))]
public class PlayerMove : MonoBehaviour
{
    [Header("Tuning (sample defaults - not feel-tuned)")]
    [SerializeField] private float _speed = 5f;

    [Header("Wiring")]
    [Tooltip("Gameplay/Move action from the shared JamActions asset.")]
    [SerializeField] private InputActionReference _moveAction;

    private CharacterController _controller;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        if (_moveAction != null && _moveAction.action != null)
        {
            _moveAction.action.Enable();
        }
    }

    private void Update()
    {
        if (_moveAction == null || _moveAction.action == null || _controller == null)
        {
            return;
        }

        Vector2 input = _moveAction.action.ReadValue<Vector2>();
        Vector3 move = new Vector3(input.x, 0f, input.y) * (_speed * Time.deltaTime);
        _controller.Move(move);
    }
}
