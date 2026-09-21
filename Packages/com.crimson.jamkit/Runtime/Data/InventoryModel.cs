using System;

namespace JamKit
{
    /// <summary>
    /// One inventory slot: an <see cref="ItemDefinition"/> and how many of it are stacked here.
    /// An empty slot has <see cref="Item"/> == null and <see cref="Count"/> == 0.
    /// </summary>
    public struct InventorySlot
    {
        /// <summary>The item in this slot, or null if empty.</summary>
        public ItemDefinition Item;

        /// <summary>How many of <see cref="Item"/> are stacked here (0 when empty).</summary>
        public int Count;

        /// <summary>True when the slot holds no item.</summary>
        public bool IsEmpty => Item == null || Count <= 0;
    }

    /// <summary>
    /// Plain-C# slot inventory: a fixed number of slots, each holding a counted stack of one
    /// <see cref="ItemDefinition"/> up to that item's <see cref="ItemDefinition.MaxStackSize"/>.
    /// </summary>
    /// <remarks>
    /// Merges the two donor halves - CarGoesAround's counted stacks live inside NineTails' positional,
    /// swappable slots. This class is deliberately free of MonoBehaviour, Unity scene state, and input
    /// so it is unit-testable in EditMode; bind it to UI with <c>InventoryView</c>. Slot count is
    /// parameterized (the donor hardcoded 3).
    /// </remarks>
    public class InventoryModel
    {
        private readonly InventorySlot[] _slots;

        /// <summary>Raised with the changed slot index whenever a single slot's contents change.</summary>
        public event Action<int> SlotChanged;

        /// <summary>Raised once after any operation that changed the inventory.</summary>
        public event Action InventoryChanged;

        /// <summary>Number of slots in this inventory.</summary>
        public int SlotCount => _slots.Length;

        /// <summary>Creates an inventory with <paramref name="slotCount"/> empty slots (minimum 1).</summary>
        public InventoryModel(int slotCount)
        {
            _slots = new InventorySlot[Math.Max(1, slotCount)];
        }

        /// <summary>Returns a copy of the slot at <paramref name="index"/> (empty if out of range).</summary>
        public InventorySlot GetSlot(int index)
        {
            if (index < 0 || index >= _slots.Length)
            {
                return default;
            }

            return _slots[index];
        }

        /// <summary>
        /// True when the inventory can accept no more items at all: every slot is occupied and every
        /// stack is at its item's max size. A partial stack (room to add more of that same item) or any
        /// empty slot means it is not full.
        /// </summary>
        public bool IsFull
        {
            get
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].IsEmpty || _slots[i].Count < _slots[i].Item.MaxStackSize)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Adds up to <paramref name="quantity"/> of <paramref name="item"/>, stacking into existing
        /// matching slots first, then empty slots, clamped to the item's max stack size.
        /// Returns the leftover quantity that did not fit (0 if all fit).
        /// </summary>
        public int AddItem(ItemDefinition item, int quantity = 1)
        {
            if (item == null || quantity <= 0)
            {
                return Math.Max(0, quantity);
            }

            int remaining = quantity;
            int maxStack = item.MaxStackSize;

            // First pass: top up existing stacks of the same item.
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i].Item == item && _slots[i].Count < maxStack)
                {
                    remaining = FillSlot(i, item, maxStack, remaining);
                }
            }

            // Second pass: place into empty slots.
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i].IsEmpty)
                {
                    remaining = FillSlot(i, item, maxStack, remaining);
                }
            }

            if (remaining != quantity)
            {
                InventoryChanged?.Invoke();
            }

            return remaining;
        }

        /// <summary>
        /// Removes up to <paramref name="quantity"/> from the slot at <paramref name="index"/>,
        /// clearing it if the count reaches zero. Returns the amount actually removed.
        /// </summary>
        public int RemoveItem(int index, int quantity = 1)
        {
            if (index < 0 || index >= _slots.Length || quantity <= 0 || _slots[index].IsEmpty)
            {
                return 0;
            }

            int removed = Math.Min(quantity, _slots[index].Count);
            _slots[index].Count -= removed;
            if (_slots[index].Count <= 0)
            {
                _slots[index] = default;
            }

            SlotChanged?.Invoke(index);
            InventoryChanged?.Invoke();
            return removed;
        }

        /// <summary>Swaps the contents of two slots (used by drag-drop). No-op if either index is invalid.</summary>
        public void SwapSlots(int a, int b)
        {
            if (a == b || a < 0 || a >= _slots.Length || b < 0 || b >= _slots.Length)
            {
                return;
            }

            (_slots[a], _slots[b]) = (_slots[b], _slots[a]);
            SlotChanged?.Invoke(a);
            SlotChanged?.Invoke(b);
            InventoryChanged?.Invoke();
        }

        /// <summary>Empties every slot.</summary>
        public void Clear()
        {
            bool changed = false;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].IsEmpty)
                {
                    _slots[i] = default;
                    SlotChanged?.Invoke(i);
                    changed = true;
                }
            }

            if (changed)
            {
                InventoryChanged?.Invoke();
            }
        }

        // Writes as much of remaining as fits into slot i, fires SlotChanged, returns the new remaining.
        private int FillSlot(int i, ItemDefinition item, int maxStack, int remaining)
        {
            int current = _slots[i].IsEmpty ? 0 : _slots[i].Count;
            int space = maxStack - current;
            if (space <= 0)
            {
                return remaining;
            }

            int added = Math.Min(space, remaining);
            _slots[i].Item = item;
            _slots[i].Count = current + added;
            SlotChanged?.Invoke(i);
            return remaining - added;
        }
    }
}
