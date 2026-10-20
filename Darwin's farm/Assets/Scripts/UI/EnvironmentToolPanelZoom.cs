using UnityEngine;

// Scale the complete tool paper uniformly; the fixed category rail is separate.
public sealed class EnvironmentToolPanelZoom : MonoBehaviour
{
    [SerializeField] private RectTransform target;
    private RectTransform mobileFrame;
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

    public void ConfigureMobileFrame(RectTransform frame)
    {
        mobileFrame = frame;
        baseScale = 1f;
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
            (mobileFrame != null ? mobileFrame.rect.width :
                target != null ? target.rect.width : EnvironmentToolPanelLayout.WideContentWidth) *
            baseScale * canvasScale);
    }

    public void DragResize(Vector2 screenPoint)
    {
        Vector2 delta = screenPoint - dragStart;
        // Horizontal drag drives diagonals; vertical drag works when it is stronger.
        float signed = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? delta.x : -delta.y;
        userScale = Mathf.Clamp(dragStartScale + signed / dragPixelsPerScale, .4f, 1.6f);
        Apply();
    }

    public void ResetFull()
    {
        userScale = 1f;
        Apply();
    }

    public void ZoomBy(float factor)
    {
        userScale = Mathf.Clamp(userScale * factor, .4f, 1.8f);
        Apply();
    }

    private void Apply()
    {
        if (target == null) return;
        if (mobileFrame != null)
        {
            target.localScale = Vector3.one;
            mobileFrame.localScale = Vector3.one * EffectiveScale;
            return;
        }
        target.pivot = Vector2.up;
        target.localScale = Vector3.one * EffectiveScale;
    }
}
