using UnityEngine;
using UnityEngine.EventSystems;

// Attach only to plain text. Buttons and future charts keep their own gestures.
public sealed class InformationTextDragHandle : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
{
    private InformationWindow window;
    private Vector2 offset;

    public void Initialize(InformationWindow owner) => window = owner;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (window != null) window.OnPointerDown(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (window != null) window.BeginTextDrag(eventData, out offset);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (window != null) window.DragText(eventData, offset);
    }

    public void OnEndDrag(PointerEventData eventData) { }
}
