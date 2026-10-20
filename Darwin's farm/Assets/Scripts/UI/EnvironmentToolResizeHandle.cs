using UnityEngine;
using UnityEngine.EventSystems;

// Press and drag immediately. This tool panel has no small-panel mode.
public sealed class EnvironmentToolResizeHandle : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IInitializePotentialDragHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private EnvironmentToolPanelZoom zoom;
    private bool pressed;
    private Vector2 start;

    public void Configure(EnvironmentToolPanelZoom controller) => zoom = controller;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        pressed = true;
        start = eventData.position;
        zoom?.BeginResize(start);
    }

    public void OnInitializePotentialDrag(PointerEventData eventData) =>
        eventData.useDragThreshold = false;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (pressed || eventData.button != PointerEventData.InputButton.Left) return;
        pressed = true;
        start = eventData.pressPosition;
        zoom?.BeginResize(start);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (pressed) zoom?.DragResize(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData) => OnDrag(eventData);

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!pressed || eventData.button != PointerEventData.InputButton.Left) return;
        if ((eventData.position - start).sqrMagnitude > 16f)
            zoom?.DragResize(eventData.position);
        pressed = false;
    }

    private void OnDisable() => pressed = false;
}
