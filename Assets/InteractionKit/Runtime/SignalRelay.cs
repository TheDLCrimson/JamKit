using System;
using UnityEngine;
using UnityEngine.Events;

namespace JamKit.InteractionKit
{
    /// <summary>UnityEvent carrying the node's new state. Concrete subclass so Unity serializes it.</summary>
    [Serializable]
    public class BoolEvent : UnityEvent<bool> { }

    /// <summary>
    /// The escape hatch. A node that does nothing itself and hands its state to a UnityEvent, so any
    /// component or game-side script can be driven from the signal graph without the kit knowing
    /// anything about it.
    ///
    /// This is what keeps the component list short: audio, lights, animation, objectives, the HUD,
    /// and anything unforeseen hang off one of these rather than earning their own node type.
    /// </summary>
    [AddComponentMenu("JamKit/Interaction/Signal Relay")]
    public class SignalRelay : SignalNode
    {
        [Tooltip("Invoked with the new state whenever this node changes.")]
        [SerializeField] private BoolEvent _stateChanged = new BoolEvent();

        protected override void OnStateChanged()
        {
            _stateChanged.Invoke(_state);
        }
    }
}
