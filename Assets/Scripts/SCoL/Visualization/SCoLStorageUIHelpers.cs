using UnityEngine;
using UnityEngine.EventSystems;

namespace SCoL.Visualization
{
    public sealed class SCoLUIDraggableWindow : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public RectTransform dragTarget;

        Vector2 _pointerOffset;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (dragTarget == null)
                dragTarget = transform as RectTransform;
            if (dragTarget == null)
                return;

            RectTransform parentRect = dragTarget.parent as RectTransform;
            if (parentRect == null)
                return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out var localPoint))
                _pointerOffset = dragTarget.anchoredPosition - localPoint;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (dragTarget == null)
                dragTarget = transform as RectTransform;
            if (dragTarget == null)
                return;

            RectTransform parentRect = dragTarget.parent as RectTransform;
            if (parentRect == null)
                return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out var localPoint))
                dragTarget.anchoredPosition = localPoint + _pointerOffset;
        }
    }

    public sealed class SCoLStorageUIDragSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public SCoLUIToolkitHUD hud;
        public int slotIndex = -1;
        public bool fromChest;

        public void OnBeginDrag(PointerEventData eventData)
        {
            hud?.BeginStorageDrag(fromChest, slotIndex, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            hud?.UpdateStorageDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            hud?.EndStorageDrag(eventData);
        }
    }

    public sealed class SCoLStorageUIDropZone : MonoBehaviour, IDropHandler
    {
        public SCoLUIToolkitHUD hud;
        public bool dropToChest;

        public void OnDrop(PointerEventData eventData)
        {
            hud?.HandleStorageDrop(dropToChest);
        }
    }
}
