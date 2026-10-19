using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Authored sizes are in Canvas units. The desktop view uses the wider layout;
// portrait phones keep the compact layout and scroll vertically.
public static class EnvironmentToolPanelLayout
{
    public const float WideWidth = 430f, WideHeight = 700f;
    public const float CompactWidth = 350f, CompactHeight = 570f;
    public const float RailWidth = 90f, WideContentWidth = 532f;

    public static void Apply(RectTransform body, bool compact)
    {
        if (body == null) return;
        body.sizeDelta = new Vector2(compact ? CompactWidth : WideWidth,
            compact ? CompactHeight : WideHeight);
        var paper = body.GetComponent<Image>();
        if (paper != null) { paper.type = Image.Type.Simple; paper.preserveAspect = true; }

        if (compact) ApplyCompact(body);
        else ApplyWide(body);
    }

    private static void ApplyWide(RectTransform body)
    {
        Label(body, "Title", 28, 30, -17, 330, 39);
        Label(body, "ToolTitle", 28, 30, -17, 330, 39);
        Button(body, "×", 376, -10, 42, 42, 28);
        Grip(body, 392, -658, 27);
        Cards(body, false);
        Line(body, "TopDivider", 28, -190, 374);
        Label(body, "EffectCaption", 17, 28, -198, 70, 25);
        Label(body, "PredictedEffect", 19, 105, -196, 155, 26);
        Label(body, "EffectHint", 17, 263, -198, 137, 25);
        Label(body, "TerrainCaption", 17, 28, -228, 70, 25);
        Label(body, "PredictedTerrain", 18, 105, -226, 295, 26);
        Label(body, "ChartTitle", 22, 28, -264, 160, 32);
        Label(body, "DefaultValue", 15, 215, -270, 185, 23);
        Rect(body, "ForecastChartFrame", 28, -298, 374, 185);
        Chart(body, false);
        Legend(body, "HistoricalLegend", 30, -491, 16);
        Legend(body, "DefaultLegend", 220, -491, 16);
        Legend(body, "ForecastLegend", 30, -514, 16);
        Line(body, "StatusDivider", 28, -540, 374);
        Label(body, "AppliedTitle", 21, 28, -546, 310, 30);
        Label(body, "AppliedLine1", 18, 30, -576, 366, 26);
        Label(body, "AppliedLine2", 18, 30, -604, 366, 26);
        Line(body, "ActionDivider", 28, -633, 374);
        Button(body, "应用", 135, -643, 112, 38, 21);
        Button(body, "撤销效果", 255, -643, 112, 38, 21);
    }

    private static void ApplyCompact(RectTransform body)
    {
        Label(body, "Title", 25, 25, -14, 245, 34);
        Label(body, "ToolTitle", 25, 25, -14, 245, 34);
        Button(body, "×", 302, -8, 39, 39, 26);
        Grip(body, 316, -525, 27);
        Cards(body, true);
        Line(body, "TopDivider", 28, -162, 294);
        Label(body, "EffectCaption", 19, 28, -169, 72, 23);
        Label(body, "PredictedEffect", 20, 102, -167, 126, 24);
        Label(body, "EffectHint", 18, 230, -169, 92, 23);
        Label(body, "TerrainCaption", 19, 28, -193, 72, 23);
        Label(body, "PredictedTerrain", 20, 102, -191, 220, 24);
        Label(body, "ChartTitle", 22, 28, -222, 140, 29);
        Label(body, "DefaultValue", 16, 182, -228, 140, 21);
        Rect(body, "ForecastChartFrame", 28, -252, 294, 137);
        Chart(body, true);
        Legend(body, "HistoricalLegend", 30, -395, 17);
        Legend(body, "DefaultLegend", 174, -395, 17);
        Legend(body, "ForecastLegend", 30, -415, 17);
        Line(body, "StatusDivider", 28, -439, 294);
        Label(body, "AppliedTitle", 21, 28, -441, 260, 25);
        Label(body, "AppliedLine1", 19, 30, -468, 292, 22);
        Label(body, "AppliedLine2", 19, 30, -492, 292, 22);
        Line(body, "ActionDivider", 28, -516, 294);
        Button(body, "应用", 118, -523, 94, 32, 20);
        Button(body, "撤销效果", 220, -523, 94, 32, 20);
    }

    private static void Cards(RectTransform body, bool compact)
    {
        for (int i = 0; i < 4; i++)
        {
            var card = body.Find("TierCard" + i) as RectTransform;
            if (card == null) continue;
            if (compact) Rect(card, 29 + 75 * i, -57, 70, 100);
            else Rect(card, 26 + 100 * i, -66, 90, 114);
            var option = card.GetComponentInChildren<Button>(true);
            if (option != null)
            {
                var rect = (RectTransform)option.transform;
                if (compact) Rect(rect, 10, -3, 50, 50);
                else Rect(rect, 15, -5, 60, 60);
            }
            if (compact)
            {
                Label(card, "Value", 22, 4, -55, 62, 24);
                Rect(card, "TierTag", 4, -79, 62, 19);
                Label(card.Find("TierTag"), "Name", 18, 0, 0, 62, 19);
            }
            else
            {
                Label(card, "Value", 22, 6, -67, 78, 24);
                Rect(card, "TierTag", 6, -92, 78, 21);
                Label(card.Find("TierTag"), "Name", 18, 0, 0, 78, 21);
            }
        }
    }

    private static void Chart(RectTransform body, bool compact)
    {
        var frame = body.Find("ForecastChartFrame");
        if (frame == null) return;
        var annotations = frame.Find("ForecastAnnotations");
        if (annotations != null)
        {
            SetText(annotations, "Enter", "生效25日");
            SetText(annotations, "Hold", compact ? "维持期" : "维持150日");
            SetText(annotations, "Return", compact ? "恢复期" : "恢复100日");
            float axis = compact ? 15 : 15;
            Label(annotations, "Axis100", axis, 2, compact ? -30 : -39, 30, 19);
            Label(annotations, "Axis50", axis, 2, compact ? -64 : -85, 30, 19);
            Label(annotations, "Axis0", axis, 2, compact ? -101 : -137, 30, 19);
            float phase = compact ? 17 : 16;
            Label(annotations, "Enter", phase, compact ? 89 : 111, -5, compact ? 73 : 92, 21);
            Label(annotations, "Hold", phase, compact ? 157 : 206, -5, compact ? 69 : 83, 21);
            Label(annotations, "Return", phase, compact ? 223 : 289, -5, compact ? 70 : 82, 21);
            Label(annotations, "PastDay", compact ? 15 : 15, compact ? 29 : 31,
                compact ? -115 : -159, 40, 20);
            Label(annotations, "NowDay", compact ? 15 : 15, compact ? 79 : 99,
                compact ? -115 : -159, 48, 20);
            Label(annotations, "EnterDay", compact ? 15 : 15, compact ? 129 : 163,
                compact ? -115 : -159, 48, 20);
            Label(annotations, "HoldDay", compact ? 15 : 15, compact ? 211 : 269,
                compact ? -115 : -159, 53, 20);
            Label(annotations, "ReturnDay", compact ? 15 : 15, compact ? 254 : 328,
                compact ? -115 : -159, 53, 20);
        }
        Label(frame, "EmptyPrompt", compact ? 21 : 24, compact ? 28 : 38,
            compact ? -55 : -72, compact ? 238 : 298, 33);
        Label(frame, "EmptySubtitle", compact ? 16 : 18, compact ? 28 : 38,
            compact ? -83 : -107, compact ? 238 : 298, 26);
    }

    private static void Legend(RectTransform body, string name, float x, float y, float size)
    {
        Rect(body, name + "Mark", x, y - 6, name == "ForecastLegend" ? 10 : 22, 3);
        if (name == "ForecastLegend") Rect(body, name + "Mark2", x + 14, y - 6, 9, 3);
        Label(body, name, size, x + 28, y - 3, 144, size < 18 ? 21 : 23);
    }

    private static void Grip(RectTransform body, float x, float y, float size) =>
        Rect(body, "ResizeGrip", x, y, size, size);

    private static void SetText(Transform root, string name, string value)
    {
        var label = root.Find(name)?.GetComponent<TextMeshProUGUI>();
        if (label != null) label.text = value;
    }

    private static void Button(RectTransform body, string caption,
        float x, float y, float w, float h, float size)
    {
        foreach (var button in body.GetComponentsInChildren<Button>(true))
        {
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null || label.text != caption) continue;
            Rect((RectTransform)button.transform, x, y, w, h);
            label.fontSize = size;
            break;
        }
    }

    private static void Line(Transform root, string name, float x, float y, float width) =>
        Rect(root, name, x, y, width, 1);

    private static void Label(Transform root, string name, float size,
        float x, float y, float w, float h)
    {
        var rect = root == null ? null : root.Find(name) as RectTransform;
        if (rect == null) return;
        Rect(rect, x, y, w, h);
        var label = rect.GetComponent<TextMeshProUGUI>();
        if (label != null)
        {
            label.fontSize = size;
            label.enableAutoSizing = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    private static void Rect(Transform root, string name,
        float x, float y, float w, float h)
    {
        if (root != null && root.Find(name) is RectTransform rect)
            Rect(rect, x, y, w, h);
    }

    private static void Rect(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(w, h);
    }
}
