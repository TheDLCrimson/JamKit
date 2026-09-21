using System.Collections.Generic;
using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Immutable ScriptableObject describing one kind of item: display metadata, stack size, and the
    /// composable effects it applies. It is content/config only - it holds no runtime state and is not
    /// a spawnable (there is deliberately no prefab field).
    /// </summary>
    [CreateAssetMenu(fileName = "New Item", menuName = "JamKit/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string _displayName = "New Item";
        [SerializeField, TextArea] private string _description = "";
        [SerializeField] private Sprite _icon;
        [SerializeField, Min(1)] private int _maxStackSize = 1;
        [SerializeReference] private List<ItemEffect> _effects = new List<ItemEffect>();

        /// <summary>Human-readable item name.</summary>
        public string DisplayName => _displayName;

        /// <summary>Longer description text.</summary>
        public string Description => _description;

        /// <summary>Inventory icon sprite.</summary>
        public Sprite Icon => _icon;

        /// <summary>Maximum count that can occupy a single inventory slot (always &gt;= 1).</summary>
        public int MaxStackSize => Mathf.Max(1, _maxStackSize);

        /// <summary>The composable effects this item applies, authored via the polymorphic list drawer.</summary>
        public IReadOnlyList<ItemEffect> Effects => _effects;

        /// <summary>Applies every effect on this item in order.</summary>
        public void ApplyEffects(GameObject caster = null, GameObject target = null)
        {
            if (_effects == null)
            {
                return;
            }

            foreach (ItemEffect effect in _effects)
            {
                effect?.ApplyEffect(caster, target);
            }
        }
    }
}
