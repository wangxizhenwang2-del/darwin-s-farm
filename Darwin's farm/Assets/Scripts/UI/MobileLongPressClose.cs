using System;
using UnityEngine;
using UnityEngine.InputSystem;

// A two-second stationary press closes even a panel whose X is off-screen.
public sealed class MobileLongPressClose : MonoBehaviour
{
    private RectTransform area;
    private Action close;
    private float started = -1f;
    private Vector2 origin;

    public void Configure(RectTransform target, Action closeAction)
    { area = target; close = closeAction; started = -1f; }

    private void Update()
    {
        if (area == null || close == null || !area.gameObject.activeInHierarchy)
        { started = -1f; return; }
        Vector2 point = default;
        bool pressed = false;
        if (Touchscreen.current != null)
            foreach (var touch in Touchscreen.current.touches)
                if (touch.press.isPressed) { point = touch.position.ReadValue(); pressed = true; break; }
        if (!pressed && Mouse.current != null && Mouse.current.leftButton.isPressed)
        { point = Mouse.current.position.ReadValue(); pressed = true; }
        Canvas canvas = area.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (!pressed || !RectTransformUtility.RectangleContainsScreenPoint(area, point, camera))
        { started = -1f; return; }
        // The board can contain its own raycast targets and scroll controls.
        // Holding anywhere inside its fixed frame should still close it.
        if (started < 0f) { started = Time.unscaledTime; origin = point; return; }
        if (Vector2.Distance(origin, point) > 18f) { started = -1f; return; }
        if (Time.unscaledTime - started < 2f) return;
        started = -1f;
        close();
    }

    private void OnDisable() => started = -1f;
}
