using UnityEngine;
using UnityEngine.EventSystems;

// Attach only to plain text. Buttons and future charts keep their own gestures.
public sealed class InformationTextDragHandle : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IScrollHandler
{
    [SerializeField] private InformationWindow window;
    [SerializeField] private bool titleHandle;
    private Vector2 offset;
    private int activePointer = int.MinValue;
    private UnityEngine.UI.ScrollRect scroll;

    public void Initialize(InformationWindow owner) => window = owner;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (window != null) window.OnPointerDown(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (window == null || activePointer != int.MinValue) return;
        if (window.TitleOnlyDragging && !titleHandle)
        {
            scroll = FindScroll(eventData.delta.y);
            if (scroll == null || !scroll.isActiveAndEnabled) return;
            activePointer = eventData.pointerId;
            scroll.OnBeginDrag(eventData);
        }
        else if (window.BeginTextDrag(eventData, out offset)) activePointer = eventData.pointerId;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointer) return;
        if (scroll != null) scroll.OnDrag(eventData);
        else if (window != null) window.DragText(eventData, offset);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointer) return;
        if (scroll != null) scroll.OnEndDrag(eventData);
        scroll = null; activePointer = int.MinValue;
    }

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
    private void OnDisable() { scroll = null; activePointer = int.MinValue; }
}
