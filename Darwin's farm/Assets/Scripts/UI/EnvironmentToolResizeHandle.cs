using UnityEngine;
using UnityEngine.EventSystems;

public sealed class EnvironmentToolResizeHandle : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private EnvironmentToolPanelZoom zoom;

    public void Configure(EnvironmentToolPanelZoom controller) => zoom = controller;
    public void OnBeginDrag(PointerEventData eventData) => zoom?.BeginResize(eventData.position);
    public void OnDrag(PointerEventData eventData) => zoom?.DragResize(eventData.position);
    public void OnEndDrag(PointerEventData eventData) => zoom?.DragResize(eventData.position);
}
