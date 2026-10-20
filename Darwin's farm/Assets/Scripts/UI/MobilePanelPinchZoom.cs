using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Pinch only when both fingers are on the visible paper. The existing grips
// remain the single-finger resize controls for their respective panels.
public sealed class MobilePanelPinchZoom : MonoBehaviour
{
    private RectTransform touchArea;
    private Action<float> zoom;
    private MapTouchInput mapInput;
    private float previousDistance;

    public void Configure(RectTransform area, Action<float> zoomBy, MapTouchInput input = null)
    {
        touchArea = area;
        zoom = zoomBy;
        mapInput = input;
        previousDistance = 0f;
    }

    private void Update()
    {
        if (touchArea == null || zoom == null || !touchArea.gameObject.activeInHierarchy)
        { previousDistance = 0f; return; }
        Touchscreen screen = Touchscreen.current;
        if (screen == null) { previousDistance = 0f; return; }
        UnityEngine.InputSystem.Controls.TouchControl first = null, second = null;
        foreach (var contact in screen.touches)
        {
            if (!contact.press.isPressed) continue;
            if (first == null) first = contact;
            else { second = contact; break; }
        }
        if (second == null) { previousDistance = 0f; return; }
        Canvas canvas = touchArea.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        Vector2 a = first.position.ReadValue(), b = second.position.ReadValue();
        if (!RectTransformUtility.RectangleContainsScreenPoint(touchArea, a, camera) ||
            !RectTransformUtility.RectangleContainsScreenPoint(touchArea, b, camera))
        { previousDistance = 0f; return; }
        float distance = Vector2.Distance(a, b);
        mapInput?.CancelGesture();
        if (previousDistance > 1f && distance > 1f)
            zoom(Mathf.Clamp(distance / previousDistance, .8f, 1.25f));
        previousDistance = distance;
    }

    private void OnDisable() => previousDistance = 0f;
}
