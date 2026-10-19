using UnityEngine;
using UnityEngine.EventSystems;

public sealed class PopulationJournalResizeHandle : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private PopulationJournalViewport viewport;

    public void Initialize(PopulationJournalViewport owner) => viewport = owner;
    public void OnBeginDrag(PointerEventData eventData) => viewport?.BeginResize(eventData.position);
    public void OnDrag(PointerEventData eventData) => viewport?.DragResize(eventData.position);
    public void OnEndDrag(PointerEventData eventData) => viewport?.DragResize(eventData.position);
}
