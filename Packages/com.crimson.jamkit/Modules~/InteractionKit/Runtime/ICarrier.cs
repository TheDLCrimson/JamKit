using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// Whatever is currently able to pick a physics prop up, from the kit's point of view.
    ///
    /// This is the second seam, and it exists for one reason: <see cref="Socket"/> has to be able to
    /// take a prop out of the player's hands before seating it, because a carry implementation keeps
    /// writing velocity to whatever it holds and would otherwise fight the seated pose. In v1 that
    /// was a direct <c>PlayerCarry</c> field, which was the kit's only dependency on game code and
    /// the thing that stopped it being a module at all.
    /// </summary>
    /// <remarks>
    /// The implementation stays game-side on purpose. A carry is direct rigidbody physics against a
    /// specific character controller - the shipped one calls into the KCC motor so a grabbed prop is
    /// also removed from its ground probes - and a module that referenced the KCC module would break
    /// the rule that modules never reference each other. So the kit states what it needs and the
    /// game supplies it.
    /// </remarks>
    public interface ICarrier
    {
        /// <summary>True while a prop is held.</summary>
        bool IsCarrying { get; }

        /// <summary>The held rigidbody, or null.</summary>
        Rigidbody Held { get; }

        /// <summary>The prop a grab would pick up right now, or null. Refreshed each frame while empty-handed.</summary>
        Rigidbody HoverTarget { get; }

        /// <summary>Display name of the grab/drop binding, e.g. "F". Shown by the prompt so the label can never name a key that is not the one being polled.</summary>
        string CarryKeyDisplay { get; }

        /// <summary>Releases the held prop at rest.</summary>
        void Drop();
    }
}
