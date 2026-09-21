using JamKit;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives the Demo_Data scene: fills an <see cref="InventoryModel"/> from authored
/// <see cref="ItemDefinition"/> assets, binds it to the on-canvas <see cref="InventoryView"/> (so slots
/// render icons + counts and drag-drop swaps them), and applies a composed item's effects on demand so
/// the polymorphic/nested-RandomChance effect graph is observable in the console.
/// </summary>
public class DemoDataController : MonoBehaviour
{
    [SerializeField] private InventoryView _view;
    [SerializeField] private int _slotCount = 4;
    [SerializeField] private ItemDefinition[] _startingItems;
    [SerializeField] private ItemDefinition _triggerItem;

    private InventoryModel _model;

    private void Start()
    {
        _model = new InventoryModel(_slotCount);
        if (_startingItems != null)
        {
            foreach (ItemDefinition item in _startingItems)
            {
                if (item != null)
                {
                    _model.AddItem(item, 1);
                }
            }
        }

        if (_view != null)
        {
            _view.Bind(_model);
        }
    }

    private void Update()
    {
        // Space applies the trigger item's effects (watch the console for the effect graph output).
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            TriggerEffects();
        }
    }

    /// <summary>Applies the trigger item's composed effects (also wired to the on-screen button).</summary>
    public void TriggerEffects()
    {
        if (_triggerItem != null)
        {
            _triggerItem.ApplyEffects();
        }
    }
}
