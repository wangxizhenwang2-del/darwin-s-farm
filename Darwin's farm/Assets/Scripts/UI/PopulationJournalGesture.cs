using UnityEngine;
using UnityEngine.EventSystems;

// The desktop journal itself is a drag handle. Its scrollbar and wheel retain
// scrolling, while the mobile drawer keeps its own touch scrolling.
[DisallowMultipleComponent]
public sealed class PopulationJournalGesture : MonoBehaviour, IBeginDragHandler,
    IDragHandler, IEndDragHandler, IPointerUpHandler
{
    [SerializeField] private InformationWindow window;
    private Vector2 offset;
    private bool dragging;

    public void Initialize(InformationWindow owner) => window = owner;

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = window != null && window.BeginTextDrag(eventData, out offset);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragging) window.DragText(eventData, offset);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (dragging) window.EndTextDrag(eventData);
        dragging = false;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (dragging) window.EndTextDrag(eventData);
        dragging = false;
    }

    private void OnDisable() => dragging = false;
}
