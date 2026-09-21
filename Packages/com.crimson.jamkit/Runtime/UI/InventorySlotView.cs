using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JamKit
{
    /// <summary>
    /// One inventory slot widget: renders an item icon and a stack-count badge, and reports click and
    /// drag-drop gestures back to its owner (typically <see cref="InventoryView"/>) via callbacks.
    /// </summary>
    /// <remarks>
    /// Drag is visual-only (the icon follows the pointer, then snaps back); the actual slot swap is
    /// performed by the owner on drop. Dragging is canvas-scale aware, so it tracks correctly under a
    /// scaled Canvas Scaler.
    /// <para>
    /// For the duration of the drag the icon is re-parented onto the root canvas (and drawn last), so it
    /// renders on top of every slot whatever its source slot or drag direction. This avoids the two
    /// alternatives that break under a typical inventory layout: sibling promotion reorders a
    /// <c>LayoutGroup</c>, and an override-sorting <c>Canvas</c> on the slot drops the slot out of the
    /// parent <c>GraphicRaycaster</c> (making it un-droppable). While flying, the icon's
    /// <c>raycastTarget</c> is disabled so it never intercepts the drop raycast to the slot beneath it.
    /// The icon's parent, sibling index, position, raycast state, and tint are all restored on drag end.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CanvasGroup))]
    public class InventorySlotView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        [SerializeField] private Image _iconImage;
        [SerializeField] private TMP_Text _countText;
        [SerializeField, Range(0f, 1f)] private float _dragAlpha = 0.6f;

        private int _slotIndex;
        private bool _hasItem;
        private Action<int> _onClick;
        private Action<int> _onBeginDrag;
        private Action<int> _onDrop;

        private Canvas _canvas;
        private RectTransform _iconRect;
        private CanvasGroup _canvasGroup;
        private Vector2 _iconHomePosition;

        // Icon state captured while it is lifted onto the root canvas for the drag, restored on drag end.
        private bool _isDraggingIcon;
        private Transform _iconOriginalParent;
        private int _iconOriginalSiblingIndex;
        private bool _iconOriginalRaycastTarget;
        private Color _iconOriginalColor;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvas = GetComponentInParent<Canvas>();
            if (_iconImage != null)
            {
                _iconRect = _iconImage.rectTransform;
                _iconHomePosition = _iconRect.anchoredPosition;
            }
        }

        /// <summary>Binds this slot to an index and the owner's gesture callbacks.</summary>
        public void Initialize(int index, Action<int> onClick, Action<int> onBeginDrag, Action<int> onDrop)
        {
            _slotIndex = index;
            _onClick = onClick;
            _onBeginDrag = onBeginDrag;
            _onDrop = onDrop;
        }

        /// <summary>Updates the visual to show <paramref name="item"/> stacked <paramref name="count"/> times (null/0 = empty).</summary>
        public void SetSlot(ItemDefinition item, int count)
        {
            _hasItem = item != null && count > 0;

            if (_iconImage != null)
            {
                _iconImage.sprite = _hasItem ? item.Icon : null;
                _iconImage.enabled = _hasItem && item.Icon != null;
            }

            if (_countText != null)
            {
                bool showCount = _hasItem && count > 1;
                _countText.enabled = showCount;
                _countText.text = showCount ? count.ToString() : string.Empty;
            }
        }

        /// <inheritdoc />
        public void OnPointerClick(PointerEventData eventData)
        {
            _onClick?.Invoke(_slotIndex);
        }

        /// <inheritdoc />
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!_hasItem)
            {
                return;
            }

            if (_iconRect != null)
            {
                _iconHomePosition = _iconRect.anchoredPosition;
            }

            _canvasGroup.alpha = _dragAlpha;
            _canvasGroup.blocksRaycasts = false;
            LiftIconForDrag();
            _onBeginDrag?.Invoke(_slotIndex);
        }

        /// <inheritdoc />
        public void OnDrag(PointerEventData eventData)
        {
            if (!_hasItem || _iconRect == null)
            {
                return;
            }

            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            _iconRect.anchoredPosition += eventData.delta / scale;
        }

        /// <inheritdoc />
        public void OnEndDrag(PointerEventData eventData)
        {
            DropIconAfterDrag();

            if (_iconRect != null)
            {
                _iconRect.anchoredPosition = _iconHomePosition;
            }

            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
        }

        // Re-parents the icon onto the root canvas and draws it last so it renders above every slot,
        // whatever the drag direction; disables its raycast so it can't block the drop beneath it.
        private void LiftIconForDrag()
        {
            if (_iconRect == null || _canvas == null)
            {
                _isDraggingIcon = false;
                return;
            }

            _iconOriginalParent = _iconRect.parent;
            _iconOriginalSiblingIndex = _iconRect.GetSiblingIndex();
            _iconRect.SetParent(_canvas.transform, true);
            _iconRect.SetAsLastSibling();

            if (_iconImage != null)
            {
                _iconOriginalRaycastTarget = _iconImage.raycastTarget;
                _iconImage.raycastTarget = false;

                _iconOriginalColor = _iconImage.color;
                Color dimmed = _iconOriginalColor;
                dimmed.a *= _dragAlpha;
                _iconImage.color = dimmed;
            }

            _isDraggingIcon = true;
        }

        // Restores the icon's parent, sibling order, raycast state, and tint on every drag-termination
        // path (drop, release outside a slot, cancel). Its anchored position is reset by the caller.
        private void DropIconAfterDrag()
        {
            if (!_isDraggingIcon)
            {
                return;
            }

            _isDraggingIcon = false;

            if (_iconRect != null && _iconOriginalParent != null)
            {
                _iconRect.SetParent(_iconOriginalParent, false);
                _iconRect.SetSiblingIndex(_iconOriginalSiblingIndex);
            }

            if (_iconImage != null)
            {
                _iconImage.raycastTarget = _iconOriginalRaycastTarget;
                _iconImage.color = _iconOriginalColor;
            }
        }

        /// <inheritdoc />
        public void OnDrop(PointerEventData eventData)
        {
            _onDrop?.Invoke(_slotIndex);
        }
    }
}
