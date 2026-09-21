using System;
using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Abstract base for a composable, designer-authored item/ability effect.
    /// </summary>
    /// <remarks>
    /// Derives from <see cref="SerializedPolymorphic"/>, so a <c>[SerializeReference] List&lt;ItemEffect&gt;</c>
    /// field is edited with JamKit's polymorphic list drawer (pick a concrete effect from a dropdown,
    /// nest effects, delete elements). Concrete effects are game-specific and live in game code;
    /// <see cref="RandomChanceEffect"/> is the one composite the toolkit ships.
    /// </remarks>
    [Serializable]
    public abstract class ItemEffect : SerializedPolymorphic
    {
        /// <summary>Called when the owning item is first obtained. Optional to override.</summary>
        public virtual void OnObtained() { }

        /// <summary>Called when the owning item is removed. Optional to override.</summary>
        public virtual void OnRemoved() { }

        /// <summary>Applies the effect. <paramref name="caster"/>/<paramref name="target"/> are optional context.</summary>
        public abstract void ApplyEffect(GameObject caster = null, GameObject target = null);
    }

    /// <summary>
    /// Implemented by an <see cref="ItemEffect"/> that should auto-trigger when its condition is met
    /// (as opposed to being fired explicitly).
    /// </summary>
    public interface IConditionalEffect
    {
        /// <summary>True when the effect's trigger condition currently holds.</summary>
        bool ConditionMet();
    }
}
