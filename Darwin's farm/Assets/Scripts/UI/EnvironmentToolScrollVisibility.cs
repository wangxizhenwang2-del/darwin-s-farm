using UnityEngine;
using UnityEngine.UI;

// The bars indicate real overflow; dragging the paper still scrolls as usual.
[RequireComponent(typeof(ScrollRect))]
public sealed class EnvironmentToolScrollVisibility : MonoBehaviour
{
    private ScrollRect scroll;
    private RectTransform frame, close, grip;

    public void Configure(RectTransform closeButton, RectTransform resizeGrip)
    {
        frame = (RectTransform)transform;
        close = closeButton;
        grip = resizeGrip;
        if (scroll == null) scroll = GetComponent<ScrollRect>();
        var bar = scroll != null ? scroll.verticalScrollbar : null;
        if (bar == null) return;
        var hit = bar.GetComponent<Image>();
        if (hit != null) { hit.color = new Color(1f, 1f, 1f, .001f); hit.raycastTarget = true; }
        var handle = bar.handleRect;
        if (handle == null) return;
        handle.anchorMin = new Vector2(.65f, handle.anchorMin.y);
        handle.anchorMax = new Vector2(.95f, handle.anchorMax.y);
        handle.offsetMin = new Vector2(0f, handle.offsetMin.y);
        handle.offsetMax = new Vector2(0f, handle.offsetMax.y);
    }

    private void Awake() => scroll = GetComponent<ScrollRect>();

    private void LateUpdate()
    {
        if (scroll == null) scroll = GetComponent<ScrollRect>();
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;
        PlaceControls();
        if (!scroll.enabled)
        {
            if (scroll.verticalScrollbar != null) scroll.verticalScrollbar.gameObject.SetActive(false);
            if (scroll.horizontalScrollbar != null) scroll.horizontalScrollbar.gameObject.SetActive(false);
            return;
        }
        Bounds content = RectTransformUtility.CalculateRelativeRectTransformBounds(
            scroll.viewport, scroll.content);
        Rect visible = scroll.viewport.rect;
        bool verticalOverflow = content.size.y > visible.height + 1f;
        scroll.vertical = verticalOverflow;
        scroll.horizontal = false;
        if (scroll.verticalScrollbar != null)
            scroll.verticalScrollbar.gameObject.SetActive(verticalOverflow);
    }

    private void PlaceControls()
    {
        if (frame == null || scroll.content == null) return;
        Bounds paper = RectTransformUtility.CalculateRelativeRectTransformBounds(frame, scroll.content);
        float width = frame.rect.width, height = frame.rect.height;
        if (width < 80f || height < 80f) return;
        float left = Mathf.Clamp(paper.min.x, 0f, width - 80f);
        float right = Mathf.Clamp(paper.max.x, left + 80f, width);
        float top = Mathf.Clamp(paper.max.y, -height + 80f, 0f);
        float bottom = Mathf.Clamp(paper.min.y, -height, top - 80f);
        if (close != null)
        {
            close.anchorMin = close.anchorMax = Vector2.up;
            close.pivot = Vector2.one;
            close.anchoredPosition = new Vector2(right - 6f, top - 6f);
            close.localScale = Vector3.one;
            close.SetAsLastSibling();
        }
        if (grip != null)
        {
            grip.anchorMin = grip.anchorMax = Vector2.up;
            grip.pivot = new Vector2(1f, 0f);
            grip.anchoredPosition = new Vector2(right - 6f, bottom + 6f);
            grip.localScale = Vector3.one;
            grip.SetAsLastSibling();
        }
        if (scroll.verticalScrollbar != null)
        {
            var rect = (RectTransform)scroll.verticalScrollbar.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(right - 32f, top - 54f);
            rect.sizeDelta = new Vector2(32f, Mathf.Max(20f, top - bottom - 108f));
            rect.localScale = Vector3.one;
        }
    }
}
