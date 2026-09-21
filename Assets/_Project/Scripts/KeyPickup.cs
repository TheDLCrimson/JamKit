using JamKit;
using UnityEngine;
using UnityEngine.InputSystem;

// KeyPickup: Lockdown mechanic. Applies the wired key ItemDefinition's effects to the wired door when the
// player interacts while in range, then disables itself. Plain Assembly-CSharp, no namespace.
public class KeyPickup : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private ItemDefinition _keyItem;
    [SerializeField] private LockdownDoor _door;
    [Tooltip("Gameplay/Interact action from the shared JamActions asset.")]
    [SerializeField] private InputActionReference _interactAction;

    private bool _playerInRange;
    private bool _collected;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(GameConstants.TagPlayer))
        {
            _playerInRange = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(GameConstants.TagPlayer))
        {
            _playerInRange = false;
        }
    }

    private void Update()
    {
        if (_collected || !_playerInRange || _interactAction == null || _interactAction.action == null)
        {
            return;
        }

        if (_interactAction.action.WasPressedThisFrame())
        {
            Collect();
        }
    }

    private void Collect()
    {
        _collected = true;

        if (_keyItem != null && _door != null)
        {
            _keyItem.ApplyEffects(caster: gameObject, target: _door.gameObject);
        }

        EventBus.Fire(new KeyCollectedEvent());
        gameObject.SetActive(false);
    }

    /// <summary>Called by LockdownController when resetting the room for a retry or the next level.</summary>
    public void ResetPickup()
    {
        _collected = false;
        _playerInRange = false;
        gameObject.SetActive(true);
    }
}
