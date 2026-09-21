using JamKit;
using UnityEngine;

// UnlockDoorEffect: Lockdown mechanic effect. Unlocks the LockdownDoor passed as target - a minimal example
// of a game-specific ItemEffect subclass, composed into the Key item's effects list via the polymorphic
// list drawer.
[System.Serializable]
public class UnlockDoorEffect : ItemEffect
{
    public override void ApplyEffect(GameObject caster = null, GameObject target = null)
    {
        if (target == null)
        {
            return;
        }

        LockdownDoor door = target.GetComponent<LockdownDoor>();
        if (door != null)
        {
            door.Unlock();
        }
    }
}
