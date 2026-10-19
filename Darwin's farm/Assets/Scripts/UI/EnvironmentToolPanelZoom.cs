using UnityEngine;
// Only the paper panel scales. Its neighboring tool category rail stays fixed.
public sealed class EnvironmentToolPanelZoom : MonoBehaviour
{
    [SerializeField] private RectTransform target;
    [SerializeField] private float userScale = 1f;
    private float baseScale = 1f;
    private float dragStartScale, dragPixelsPerScale;
    private Vector2 dragStart;
    public float EffectiveScale => baseScale * userScale;

    public void Configure(RectTransform panel)
    {
        target = panel;
        Apply();
    }

    public void SetBaseScale(float value)
    {
        value = Mathf.Max(.1f, value);
        if (Mathf.Abs(baseScale - value) < .0001f) return;
        baseScale = value;
        Apply();
    }

    public void BeginResize(Vector2 screenPoint)
    {
        dragStart = screenPoint;
        dragStartScale = userScale;
        var canvas = target != null ? target.GetComponentInParent<Canvas>() : null;
        float canvasScale = canvas != null ? canvas.scaleFactor : 1f;
        dragPixelsPerScale = Mathf.Max(180f,
            (target != null ? target.rect.width : EnvironmentToolPanelLayout.WideContentWidth) *
            baseScale * canvasScale);
    }

    public void DragResize(Vector2 screenPoint)
    {
        Vector2 delta = screenPoint - dragStart;
        // Screen coordinates grow upward: left/up shrinks, right/down grows.
        userScale = Mathf.Clamp(dragStartScale + (delta.x - delta.y) * .5f /
            dragPixelsPerScale, .72f, 1.4f);
        Apply();
    }

    private void Apply()
    {
        if (target == null) return;
        target.pivot = Vector2.up;
        target.localScale = Vector3.one * EffectiveScale;
    }
}
