using UnityEngine;
using UnityEngine.UI;

// Mobile keeps readable text on narrow screens by moving the category rail above
// the panel. The enclosing ScrollRect supplies drag and shows bars on overflow.
public sealed class EnvironmentToolResponsiveLayout : MonoBehaviour
{
    [SerializeField] private RectTransform body, rail, categoryRow;
    [SerializeField] private Button[] categoryButtons;
    private bool? narrow;

    public void Configure(RectTransform panel, RectTransform sideRail,
        RectTransform topRow, Button[] buttons)
    { body = panel; rail = sideRail; categoryRow = topRow; categoryButtons = buttons; narrow = null; }

    public float Apply(float availableWidth)
    {
        bool compact = availableWidth < EnvironmentToolPanelLayout.WideContentWidth;
        if (narrow != compact)
        {
            narrow = compact;
            rail.gameObject.SetActive(!compact);
            categoryRow.gameObject.SetActive(compact);
            body.anchoredPosition = compact ? new Vector2(0, -64) : new Vector2(102, 0);
            if (!compact)
            {
                rail.anchoredPosition = Vector2.zero;
                rail.sizeDelta = new Vector2(EnvironmentToolPanelLayout.RailWidth, 363f);
            }
            int[] displayOrder = { 0, 2, 4, 3, 1 };
            for (int display = 0; display < displayOrder.Length; display++)
            {
                var button = categoryButtons[displayOrder[display]];
                RectTransform rect = (RectTransform)button.transform;
                rect.SetParent(compact ? categoryRow : rail, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
                rect.anchoredPosition = compact ? new Vector2(10 + display * 68, -4) :
                    new Vector2(15, -17 - display * 65);
                rect.sizeDelta = compact ? new Vector2(50, 50) : new Vector2(60, 60);
                rect.localScale = Vector3.one;
            }
        }
        EnvironmentToolPanelLayout.Apply(body, compact);
        // Keep the rail at its authored size; scale only the paper panel.
        float panelWidth = compact ? EnvironmentToolPanelLayout.CompactWidth :
            EnvironmentToolPanelLayout.WideWidth;
        float leftOffset = compact ? 0f : 102f;
        float scale = Mathf.Min(1f,
            Mathf.Max(.8f, (availableWidth - leftOffset) / panelWidth));
        var zoom = GetComponent<EnvironmentToolPanelZoom>();
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
        float width = compact ? Mathf.Max(332f,
            EnvironmentToolPanelLayout.CompactWidth * scale) :
            Mathf.Max(EnvironmentToolPanelLayout.RailWidth,
                102f + EnvironmentToolPanelLayout.WideWidth * scale);
        float height = compact ? 64f + EnvironmentToolPanelLayout.CompactHeight * scale :
            Mathf.Max(363f, EnvironmentToolPanelLayout.WideHeight * scale);
        ((RectTransform)transform).sizeDelta = new Vector2(width, height);
    }
}
