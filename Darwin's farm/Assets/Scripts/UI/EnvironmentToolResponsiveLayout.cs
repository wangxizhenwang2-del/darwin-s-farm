using UnityEngine;
using UnityEngine.UI;

// The category rail retains one vertical layout at every screen size. Only the
// separate paper panel chooses its authored wide or compact composition.
public sealed class EnvironmentToolResponsiveLayout : MonoBehaviour
{
    [SerializeField] private RectTransform body, rail, categoryRow;
    [SerializeField] private Button[] categoryButtons;
    private bool? narrow;
    private bool keepWideLayout;
    private float lastAvailableWidth = WideDefault;
    private const float WideDefault = 532f;

    public void RefreshMode() => Apply(lastAvailableWidth);
    public void KeepDesktopPaper(bool value) { keepWideLayout = value; narrow = null; Apply(lastAvailableWidth); }

    public void Configure(RectTransform panel, RectTransform sideRail,
        RectTransform topRow, Button[] buttons)
    { body = panel; rail = sideRail; categoryRow = topRow; categoryButtons = buttons; narrow = null; }

    public float Apply(float availableWidth)
    {
        lastAvailableWidth = availableWidth;
        var zoom = GetComponent<EnvironmentToolPanelZoom>();
        bool compact = !keepWideLayout && availableWidth < EnvironmentToolPanelLayout.WideContentWidth;
        body.anchoredPosition = rail.IsChildOf(transform) ? new Vector2(102f, 0f) : Vector2.zero;
        if (narrow != compact)
        {
            narrow = compact;
            rail.gameObject.SetActive(true);
            if (categoryRow != null) categoryRow.gameObject.SetActive(false);
            rail.sizeDelta = new Vector2(EnvironmentToolPanelLayout.RailWidth, 363f);
            int[] displayOrder = { 0, 2, 4, 3, 1 };
            for (int display = 0; display < displayOrder.Length; display++)
            {
                var button = categoryButtons[displayOrder[display]];
                RectTransform rect = (RectTransform)button.transform;
                rect.SetParent(rail, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
                rect.anchoredPosition = new Vector2(15, -17 - display * 65);
                rect.sizeDelta = new Vector2(60, 60);
                rect.localScale = Vector3.one;
            }
        }
        EnvironmentToolPanelLayout.Apply(body, compact);
        // Keep the rail at its authored size; scale only the paper panel.
        float panelWidth = compact ? EnvironmentToolPanelLayout.CompactWidth :
            EnvironmentToolPanelLayout.WideWidth;
        float leftOffset = rail.IsChildOf(transform) ? 102f : 0f;
        float scale = keepWideLayout ? 1f : Mathf.Min(1f,
            Mathf.Max(.1f, (availableWidth - leftOffset) / panelWidth));
        if (zoom != null) zoom.SetBaseScale(scale);
        else body.localScale = Vector3.one * scale;
        ((RectTransform)transform).localScale = Vector3.one;
        RefreshContentBounds();
        return scale;
    }

    private void LateUpdate() => RefreshContentBounds();

    private void RefreshContentBounds()
    {
        if (body == null || narrow == null) return;
        bool compact = narrow.Value;
        float scale = body.localScale.x;
        float width = (rail.IsChildOf(transform) ? 102f : 0f) +
            (compact ? EnvironmentToolPanelLayout.CompactWidth :
            EnvironmentToolPanelLayout.WideWidth) * scale;
        float height = (compact ? EnvironmentToolPanelLayout.CompactHeight :
            EnvironmentToolPanelLayout.WideHeight) * scale;
        ((RectTransform)transform).sizeDelta = new Vector2(width, height);
    }
}
