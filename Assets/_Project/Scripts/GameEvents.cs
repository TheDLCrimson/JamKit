// Convention: every cross-system event struct for this game lives in this file, and only this file.
// Fire and subscribe through JamKit.EventBus<T>; do not add ScriptableObject event channels.

using JamKit;

/// <summary>
/// Fired by <c>KeyPickup</c> when the player interacts with the level's key.
/// </summary>
public struct KeyCollectedEvent : IGameEvent { }

/// <summary>
/// Fired by <c>LockdownDoor</c> when <c>UnlockDoorEffect</c> unlocks it.
/// </summary>
public struct DoorUnlockedEvent : IGameEvent { }

/// <summary>
/// Fired by <c>LockdownDoor</c> when the player reaches it after it has unlocked.
/// </summary>
public struct LockdownWonEvent : IGameEvent { }

/// <summary>
/// Fired by <c>LockdownController</c> when the level timer expires before the door is reached.
/// </summary>
public struct LockdownLostEvent : IGameEvent { }

/// <summary>
/// Fired by <c>WinLoseBlocker</c>'s Continue button. Routed through EventBus rather than a direct serialized
/// reference because <c>WinLoseBlocker</c> is a prefab asset instantiated at runtime by UIBlockerStack - it
/// cannot hold a pre-wired reference to a specific scene's LockdownController instance.
/// </summary>
public struct ContinueRequestedEvent : IGameEvent
{
    public bool Won;
}
