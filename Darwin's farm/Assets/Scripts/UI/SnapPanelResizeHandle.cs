using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Reports an immediate drag in canvas units. The panel owns its snap threshold.
public sealed class SnapPanelResizeHandle : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IInitializePotentialDragHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private Action<float> resize;
    private Action begin;
    private Vector2 start;
    private float scale = 1f;
    private bool pressed;

    public void Initialize(Action<float> onResize, Action onBegin = null)
    {
        resize = onResize;
        begin = onBegin;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        start = eventData.position;
        scale = Mathf.Max(.1f, GetComponentInParent<Canvas>()?.scaleFactor ?? 1f);
        pressed = true;
        begin?.Invoke();
    }

    public void OnInitializePotentialDrag(PointerEventData eventData) =>
        eventData.useDragThreshold = false;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (pressed || eventData.button != PointerEventData.InputButton.Left) return;
        start = eventData.pressPosition;
        scale = Mathf.Max(.1f, GetComponentInParent<Canvas>()?.scaleFactor ?? 1f);
        pressed = true;
        begin?.Invoke();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!pressed) return;
        Vector2 movement = eventData.position - start;
        float delta = (Mathf.Abs(movement.x) >= Mathf.Abs(movement.y) ?
            movement.x : -movement.y) / scale;
        resize?.Invoke(delta);
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
