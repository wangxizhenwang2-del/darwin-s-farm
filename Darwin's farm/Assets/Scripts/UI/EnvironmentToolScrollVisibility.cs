using UnityEngine;
using UnityEngine.UI;

// The bars indicate real overflow; dragging the paper still scrolls as usual.
[RequireComponent(typeof(ScrollRect))]
public sealed class EnvironmentToolScrollVisibility : MonoBehaviour
{
    private ScrollRect scroll;

    private void Awake() => scroll = GetComponent<ScrollRect>();

    private void LateUpdate()
    {
        if (scroll == null) scroll = GetComponent<ScrollRect>();
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;
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
        bool horizontalOverflow = content.size.x > visible.width + 1f;
        scroll.vertical = verticalOverflow;
        scroll.horizontal = horizontalOverflow;
        if (scroll.verticalScrollbar != null)
            scroll.verticalScrollbar.gameObject.SetActive(verticalOverflow);
        if (scroll.horizontalScrollbar != null)
            scroll.horizontalScrollbar.gameObject.SetActive(horizontalOverflow);
    }
}
