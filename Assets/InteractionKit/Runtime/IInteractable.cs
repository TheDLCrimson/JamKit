using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// Something the player's <see cref="Interactor"/> can aim at, hold, and commit to.
    ///
    /// This is the seam that moved input off the world objects. In v1 every interactable carried its
    /// own key, range, hold duration and prompt styling, so twelve identical values were authored
    /// per object and changing the interact key meant touching every scene. Here an object states
    /// only what is true about itself - its label, whether it is refusing right now, and what to do
    /// when committed - and the single Interactor on the player rig owns everything else.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>The transform the prompt floats over and distance is measured from.</summary>
        Transform transform { get; }

        /// <summary>Verb shown in the prompt, e.g. "Open Door". Never includes the key name.</summary>
        string Label { get; }

        /// <summary>
        /// Seconds this object needs to be held, or a negative value to accept the Interactor's
        /// default. Per-object only because some objects genuinely deserve a longer commit.
        /// </summary>
        float HoldSecondsOverride { get; }

        /// <summary>True while this object refuses interaction.</summary>
        bool Blocked { get; }

        /// <summary>Reason shown under the label while blocked, or empty to show the label alone.</summary>
        string BlockedMessage { get; }

        /// <summary>True when this object can currently be aimed at (active, enabled, in a live scene).</summary>
        bool IsAvailable { get; }

        /// <summary>Applies one committed interaction. Called by the Interactor, never by the object itself.</summary>
        void Interact(object source);
    }
}
