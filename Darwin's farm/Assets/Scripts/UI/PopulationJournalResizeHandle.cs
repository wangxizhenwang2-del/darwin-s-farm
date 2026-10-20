using UnityEngine;
using UnityEngine.EventSystems;

public sealed class PopulationJournalResizeHandle : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IInitializePotentialDragHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private PopulationJournalViewport viewport;
    private Vector2 start;
    private bool pressed;

    public void Initialize(PopulationJournalViewport owner) => viewport = owner;
    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        start = eventData.position;
        pressed = true;
        viewport?.BeginResize(start);
    }
    public void OnInitializePotentialDrag(PointerEventData eventData) =>
        eventData.useDragThreshold = false;
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (pressed || eventData.button != PointerEventData.InputButton.Left) return;
        pressed = true;
        start = eventData.pressPosition;
        viewport?.BeginResize(start);
    }
    public void OnDrag(PointerEventData eventData)
    {
        if (pressed) viewport?.DragResize(eventData.position);
    }
    public void OnEndDrag(PointerEventData eventData) => OnDrag(eventData);
    public void OnPointerUp(PointerEventData eventData)
    {
        if (!pressed || eventData.button != PointerEventData.InputButton.Left) return;
        if ((eventData.position - start).sqrMagnitude > 16f)
            OnDrag(eventData);
        pressed = false;
    }
    private void OnDisable() => pressed = false;
}
