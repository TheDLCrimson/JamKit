using JamKit;
using UnityEngine;

// LockdownDoor: blocks the exit until UnlockDoorEffect unlocks it, then fires LockdownWonEvent when the
// player reaches it. Plain Assembly-CSharp, no namespace (jam-code convention).
public class LockdownDoor : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Collider _blocker; // solid collider that blocks the exit while locked

    private bool _isUnlocked;

    /// <summary>Called by UnlockDoorEffect when the level's key resolves.</summary>
    public void Unlock()
    {
        if (_isUnlocked)
        {
            return;
        }

        _isUnlocked = true;

        if (_blocker != null)
        {
            _blocker.enabled = false;
        }

        EventBus.Fire(new DoorUnlockedEvent());
    }

    /// <summary>Called by LockdownController when resetting the room for a retry or the next level.</summary>
    public void ReLock()
    {
        _isUnlocked = false;

        if (_blocker != null)
        {
            _blocker.enabled = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_isUnlocked || !other.CompareTag(GameConstants.TagPlayer))
        {
            return;
        }

        EventBus.Fire(new LockdownWonEvent());
    }
}
