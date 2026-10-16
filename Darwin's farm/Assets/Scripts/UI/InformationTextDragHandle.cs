using UnityEngine;
using UnityEngine.EventSystems;

// Routes text gestures to a window or its nearest scrollable content.
public sealed class InformationTextDragHandle : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerDownHandler, IPointerUpHandler, IScrollHandler
{
    [SerializeField] private InformationWindow window;
    [SerializeField] private bool titleHandle;
    private Vector2 offset;
    private int activePointer = int.MinValue;
    private UnityEngine.UI.ScrollRect scroll;
    private PointerEventData dragEvent;

    public void Initialize(InformationWindow owner) => window = owner;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (window != null) window.OnPointerDown(eventData);
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        // Forward the initialization that this handler intercepts from ScrollRect.
        FindScroll(0f)?.OnInitializePotentialDrag(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (window == null || activePointer != int.MinValue ||
            eventData.button != PointerEventData.InputButton.Left) return;
        scroll = titleHandle ? null : FindScroll(eventData.delta.y);
        if (scroll != null && scroll.isActiveAndEnabled)
        {
            activePointer = eventData.pointerId;
            scroll.OnInitializePotentialDrag(eventData);
            scroll.OnBeginDrag(eventData);
        }
        else if ((!window.TitleOnlyDragging || titleHandle) && window.BeginTextDrag(eventData, out offset))
            activePointer = eventData.pointerId;
        if (activePointer != int.MinValue) dragEvent = eventData;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointer) return;
        dragEvent = eventData;
        if (scroll != null) scroll.OnDrag(eventData);
        else if (window != null) window.DragText(eventData, offset);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointer) return;
        if (scroll != null) scroll.OnEndDrag(eventData);
        else if (window != null) window.EndTextDrag(eventData);
        scroll = null; dragEvent = null; activePointer = int.MinValue;
    }

    public void OnPointerUp(PointerEventData eventData) => OnEndDrag(eventData);

    public void OnScroll(PointerEventData eventData) => FindScroll(-eventData.scrollDelta.y)?.OnScroll(eventData);

    private UnityEngine.UI.ScrollRect FindScroll(float direction)
    {
        // Desktop text keeps its original nested scroller. On mobile an inner
        // description that fits (or has reached its edge) must not trap the swipe.
        if (window == null || !window.TitleOnlyDragging)
            return GetComponentInParent<UnityEngine.UI.ScrollRect>();
        UnityEngine.UI.ScrollRect outer = null;
        foreach (var candidate in GetComponentsInParent<UnityEngine.UI.ScrollRect>())
        {
            if (!candidate.isActiveAndEnabled || candidate.content == null) continue;
            outer = candidate;
            RectTransform view = candidate.viewport != null ? candidate.viewport : (RectTransform)candidate.transform;
            float scale = Mathf.Abs(candidate.content.lossyScale.y / view.lossyScale.y);
            if (!candidate.vertical || candidate.content.rect.height * scale <= view.rect.height + 1f) continue;
            float position = candidate.verticalNormalizedPosition;
            if ((direction > 0f && position <= .001f) || (direction < 0f && position >= .999f)) continue;
            return candidate;
        }
        return outer;
    }
    private void CancelDrag() { if (dragEvent != null) OnEndDrag(dragEvent); }
    private void OnDisable() => CancelDrag();
    private void OnApplicationFocus(bool focused) { if (!focused) CancelDrag(); }
    private void OnApplicationPause(bool paused) { if (paused) CancelDrag(); }
}
