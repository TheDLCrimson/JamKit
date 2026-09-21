using System;
using System.Collections.Generic;
using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Composite effect that rolls a chance and applies one of two nested effect lists.
    /// </summary>
    /// <remarks>
    /// A <c>Random.value &lt;= chance</c> roll runs <see cref="_successEffects"/>; otherwise it runs
    /// <see cref="_failureEffects"/>. Both lists are themselves <c>[SerializeReference]</c> polymorphic
    /// lists, so effects nest arbitrarily in the inspector.
    /// <para>
    /// Extraction note: the donor (NineTails) declared the chance field <c>[Range] private float</c>
    /// <b>without</b> <c>[SerializeField]</c>, so it never serialized and every roll was silently 50/50.
    /// A <c>[Range]</c>/<c>[Tooltip]</c> attribute does not serialize a field - only <c>public</c> or
    /// <c>[SerializeField] private</c> does. The chance field below is correctly serialized.
    /// </remarks>
    [Serializable]
    public class RandomChanceEffect : ItemEffect
    {
        [SerializeField, Range(0f, 1f)] private float _chance = 0.5f;
        [SerializeReference] private List<ItemEffect> _successEffects = new List<ItemEffect>();
        [SerializeReference] private List<ItemEffect> _failureEffects = new List<ItemEffect>();

        /// <inheritdoc />
        public override void ApplyEffect(GameObject caster = null, GameObject target = null)
        {
            List<ItemEffect> chosen = UnityEngine.Random.value <= _chance ? _successEffects : _failureEffects;
            if (chosen == null)
            {
                return;
            }

            foreach (ItemEffect effect in chosen)
            {
                effect?.ApplyEffect(caster, target);
            }
        }
    }
}
