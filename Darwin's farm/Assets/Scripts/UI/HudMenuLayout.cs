using UnityEngine;

// Align the menu with either HUD even when their Canvas scales differ.
[ExecuteAlways, DefaultExecutionOrder(200), DisallowMultipleComponent]
public sealed class HudMenuLayout : MonoBehaviour
{
    [SerializeField] private RectTransform menu;
    [SerializeField] private RectTransform desktopSpeed;
    [SerializeField] private RectTransform mobileSpeed;
    [SerializeField] private MobileGameUI mobileUI;
    [SerializeField, Min(0f)] private float gap = 8f;
    private readonly Vector3[] corners = new Vector3[4];

    private void LateUpdate() => RefreshLayout();

    public void RefreshLayout()
    {
        if (menu == null || !(menu.parent is RectTransform parent)) return;
        bool mobile = mobileUI != null && (Application.isPlaying
            ? mobileUI.IsMobileActive : mobileUI.Mode == MobileGameUI.DisplayMode.Mobile);
        RectTransform speed = mobile ? mobileSpeed : desktopSpeed;
        if (speed == null) return;
        Canvas source = speed.GetComponentInParent<Canvas>();
        Canvas destination = menu.GetComponentInParent<Canvas>();
        Camera sourceCamera = source != null && source.renderMode != RenderMode.ScreenSpaceOverlay
            ? source.worldCamera : null;
        Camera destinationCamera = destination != null && destination.renderMode != RenderMode.ScreenSpaceOverlay
            ? destination.worldCamera : null;
        speed.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 corner in corners)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(sourceCamera, corner);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, destinationCamera, out Vector2 point))
                return;
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        if (max.x <= min.x || max.y <= min.y) return;
        Vector2 size = max - min;
        Vector2 position = new Vector2(max.x - parent.rect.xMax,
            min.y - gap - parent.rect.yMax);
        if (menu.anchorMin == Vector2.one && menu.anchorMax == Vector2.one &&
            menu.pivot == Vector2.one && menu.localScale == Vector3.one &&
            (menu.sizeDelta - size).sqrMagnitude < .0001f &&
            (menu.anchoredPosition - position).sqrMagnitude < .0001f) return;
        menu.anchorMin = menu.anchorMax = menu.pivot = Vector2.one;
        menu.localScale = Vector3.one;
        menu.sizeDelta = size;
        menu.anchoredPosition = position;
    }
}
