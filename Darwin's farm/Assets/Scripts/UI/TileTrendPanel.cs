using TMPro;
using UnityEngine;

// Creates the initial editable layout once; saved layouts are reused without resetting style.
public sealed class TileTrendPanel : MonoBehaviour
{
    [SerializeField] private TileTrendChart chart;
    public TileTrendChart Chart => chart;
    public void Initialize()
    {
        if (chart != null) return;
        RectTransform panel = (RectTransform)transform;
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(750f, panel.rect.height));
        InformationWindow window = GetComponent<InformationWindow>();
        RectTransform root = Rect("TileTrend", panel, new Vector2(85f, -110f), new Vector2(panel.rect.width - 155f, 320f));
        float width = root.rect.width;
        Text("Title", root, "近期趋势 · 种群左轴 / 植物右轴", 21, Vector2.zero, new Vector2(width, 32f), window);
        RectTransform plot = Rect("Chart", root, new Vector2(60f, -52f), new Vector2(width - 128f, 188f));
        chart = plot.gameObject.AddComponent<TileTrendChart>(); chart.raycastTarget = false;
        TextMeshProUGUI leftTop = Text("PopulationMaximum", root, "--", 16, new Vector2(0f, -42f), new Vector2(57f, 26f), window);
        TextMeshProUGUI leftBottom = Text("PopulationZero", root, "0", 16, new Vector2(0f, -223f), new Vector2(57f, 26f), window);
        TextMeshProUGUI rightTop = Text("PlantMaximum", root, "--", 16, new Vector2(width - 65f, -42f), new Vector2(65f, 26f), window);
        TextMeshProUGUI rightBottom = Text("PlantMinimum", root, "0", 16, new Vector2(width - 65f, -223f), new Vector2(65f, 26f), window);
        TextMeshProUGUI start = Text("FirstDay", root, "Day --", 16, new Vector2(60f, -242f), new Vector2(100f, 25f), window);
        TextMeshProUGUI end = Text("LastDay", root, "Day --", 16, new Vector2(width - 168f, -242f), new Vector2(100f, 25f), window);
        end.alignment = TextAlignmentOptions.TopRight;
        TextMeshProUGUI legend = Text("Legend", root, "种群：左轴    植物库存：右轴", 16, new Vector2(0f, -275f), new Vector2(width, 46f), window);
        legend.richText = true;
        TextMeshProUGUI status = Text("Status", root, "运行后开始记录地块趋势", 18, new Vector2(72f, -120f), new Vector2(width - 150f, 64f), window);
        status.alignment = TextAlignmentOptions.Center;
        chart.BindLabels(leftTop, leftBottom, rightTop, rightBottom, start, end, legend, status);
    }
    public void Show(MapTileInstance tile, TileTrendHistory history)
    {
        TileTrendHistory.TileRecord record = null;
        if (history != null) history.TryGet(tile, out record);
        chart.Show(record);
    }
    private static TextMeshProUGUI Text(string name, RectTransform parent, string value, float size,
        Vector2 position, Vector2 dimensions, InformationWindow window)
    {
        var rect = Rect(name, parent, position, dimensions);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = GameUITheme.TMPFont; text.fontSize = size; text.text = value;
        text.color = new Color(.11f, .16f, .17f); text.richText = false;
        text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Truncate;
        text.raycastTarget = true;
        rect.gameObject.AddComponent<InformationTextDragHandle>().Initialize(window);
        return text;
    }
    private static RectTransform Rect(string name, RectTransform parent, Vector2 position, Vector2 dimensions)
    {
        var go = new GameObject(name, typeof(RectTransform)); var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position; rect.sizeDelta = dimensions;
        return rect;
    }
}
