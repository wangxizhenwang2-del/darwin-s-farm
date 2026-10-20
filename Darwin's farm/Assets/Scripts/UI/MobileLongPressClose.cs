using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// A two-second stationary press closes even a panel whose X is off-screen.
public sealed class MobileLongPressClose : MonoBehaviour, IPointerDownHandler,
    IPointerUpHandler
{
    private RectTransform area;
    private Action close;
    private float started = -1f;
    private Vector2 origin;
    private float duration = 2f;
    private bool movedDuringPress;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        origin = eventData.position;
        started = Time.unscaledTime;
        movedDuringPress = false;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        started = -1f;
        movedDuringPress = false;
    }

    public void Configure(RectTransform target, Action closeAction, float holdSeconds = 2f)
    { area = target; close = closeAction; duration = holdSeconds; started = -1f; movedDuringPress = false; }
    public void SetArea(RectTransform target)
    { area = target; started = -1f; movedDuringPress = false; }

    private void Update()
    {
        if (area == null || close == null || !area.gameObject.activeInHierarchy)
        { started = -1f; return; }
        Canvas canvas = area.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        bool pressed = false;
        Vector2 point = origin;
        int touchCount = 0;
        if (Touchscreen.current != null)
            foreach (var touch in Touchscreen.current.touches)
            {
                if (!touch.press.isPressed) continue;
                touchCount++;
                Vector2 candidate = touch.position.ReadValue();
                if (!RectTransformUtility.RectangleContainsScreenPoint(area, candidate, camera)) continue;
                if (!pressed || Vector2.Distance(candidate, origin) < Vector2.Distance(point, origin))
                { point = candidate; pressed = true; }
            }
        if (touchCount > 1) { started = -1f; movedDuringPress = true; return; }
        // In the Editor a simulated touch can coexist with the mouse. A touch
        // outside the panel must not prevent a held mouse inside it from closing.
        if (!pressed && Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            Vector2 candidate = Mouse.current.position.ReadValue();
            if (RectTransformUtility.RectangleContainsScreenPoint(area, candidate, camera))
            { point = candidate; pressed = true; }
        }
        bool mousePressed = Mouse.current != null && Mouse.current.leftButton.isPressed;
        if (touchCount == 0 && !mousePressed)
        { started = -1f; movedDuringPress = false; return; }
        if (!pressed) { started = -1f; movedDuringPress = true; return; }
        if (movedDuringPress) return;
        // The board can contain its own raycast targets and scroll controls.
        // Holding anywhere inside its fixed frame should still close it.
        if (started < 0f) { started = Time.unscaledTime; origin = point; return; }
        if (Vector2.Distance(origin, point) > 18f)
        { started = -1f; movedDuringPress = true; return; }
        if (Time.unscaledTime - started < duration) return;
        started = -1f;
        movedDuringPress = true;
        close();
    }

    private void OnDisable() { started = -1f; movedDuringPress = false; }
}
