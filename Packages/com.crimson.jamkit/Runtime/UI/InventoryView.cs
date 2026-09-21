using System;
using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Binds an <see cref="InventoryModel"/> to a row of <see cref="InventorySlotView"/> widgets:
    /// pushes model changes into the views and turns drag-drop between slots into a model swap.
    /// </summary>
    /// <remarks>
    /// A plain MonoBehaviour with no pause/HUD coupling, so it works equally as a persistent HUD or
    /// hosted inside a <c>UIBlockerBase</c> panel. Assign the slot views in the inspector; the number of
    /// views defines the model's slot count unless you call <see cref="Bind"/> with your own model.
    /// Raises <see cref="SlotClicked"/> so callers can drive a details panel.
    /// </remarks>
    public class InventoryView : MonoBehaviour
    {
        [SerializeField] private InventorySlotView[] _slots = Array.Empty<InventorySlotView>();

        private InventoryModel _model;
        private int _dragSourceIndex = -1;

        /// <summary>Raised with the slot index when a slot is clicked.</summary>
        public event Action<int> SlotClicked;

        /// <summary>The bound model (null until <see cref="Bind"/> or auto-binding in <see cref="Start"/>).</summary>
        public InventoryModel Model => _model;

        private void Start()
        {
            InitializeSlots();
            if (_model == null)
            {
                // Auto-create a model sized to the assigned slot views if none was bound.
                Bind(new InventoryModel(_slots.Length));
            }
        }

        private void OnDestroy()
        {
            Unsubscribe(_model);
        }

        /// <summary>
        /// Binds a model to this view, refreshing all slots and re-subscribing to its events.
        /// </summary>
        /// <remarks>
        /// The view displays only its preconfigured <c>InventorySlotView[]</c>: it never creates or
        /// destroys slot widgets. If <paramref name="model"/> has more slots than there are assigned
        /// views, the surplus model slots are not shown (the model itself is untouched); if it has fewer,
        /// the surplus views render empty. A count mismatch logs a warning.
        /// </remarks>
        public void Bind(InventoryModel model)
        {
            Unsubscribe(_model);
            _model = model;
            WarnOnSlotCountMismatch(model);
            InitializeSlots();
            Subscribe(_model);
            RefreshAll();
        }

        // The view only ever displays its preconfigured slot views, so a model sized differently than the
        // assigned views is a configuration mistake worth surfacing (the model is left intact either way).
        private void WarnOnSlotCountMismatch(InventoryModel model)
        {
            if (model == null)
            {
                return;
            }

            int viewCount = UsableSlotViewCount();
            if (model.SlotCount != viewCount)
            {
                Debug.LogWarning(
                    $"InventoryView.Bind: bound model has {model.SlotCount} slot(s) but this view has {viewCount} assigned slot view(s). " +
                    "Only the configured views are displayed; the model is not modified. Assign a matching number of InventorySlotViews.",
                    this);
            }
        }

        private int UsableSlotViewCount()
        {
            int count = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private void InitializeSlots()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] == null)
                {
                    continue;
                }

                int index = i; // capture per slot
                _slots[i].Initialize(index, OnSlotClicked, OnSlotBeginDrag, OnSlotDrop);
            }
        }

        private void Subscribe(InventoryModel model)
        {
            if (model == null)
            {
                return;
            }

            model.SlotChanged += RefreshSlot;
        }

        private void Unsubscribe(InventoryModel model)
        {
            if (model == null)
            {
                return;
            }

            model.SlotChanged -= RefreshSlot;
        }

        private void RefreshAll()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                RefreshSlot(i);
            }
        }

        private void RefreshSlot(int index)
        {
            if (_model == null || index < 0 || index >= _slots.Length || _slots[index] == null)
            {
                return;
            }

            InventorySlot slot = _model.GetSlot(index);
            _slots[index].SetSlot(slot.Item, slot.Count);
        }

        private void OnSlotClicked(int index)
        {
            SlotClicked?.Invoke(index);
        }

        private void OnSlotBeginDrag(int index)
        {
            _dragSourceIndex = index;
        }

        private void OnSlotDrop(int targetIndex)
        {
            if (_model != null && _dragSourceIndex != -1 && _dragSourceIndex != targetIndex)
            {
                _model.SwapSlots(_dragSourceIndex, targetIndex);
            }

            _dragSourceIndex = -1;
        }
    }
}
