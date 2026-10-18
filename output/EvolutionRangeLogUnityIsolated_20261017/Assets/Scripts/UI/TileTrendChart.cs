using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class TileTrendChart : MaskableGraphic
{
    [SerializeField] private Color gridColor = new Color(.16f, .24f, .21f, .25f);
    [SerializeField] private Color plantColor = new Color(.15f, .52f, .19f);
    [SerializeField, Range(.5f, 4f)] private float lineWidth = 2f;
    [SerializeField, Range(1, 32)] private int maximumVisiblePopulations = 8;
    [SerializeField] private bool autoPlantRange = true;
    [SerializeField] private TextMeshProUGUI leftTop, leftBottom, rightTop, rightBottom, firstDay, lastDay, legend, status;
    private TileTrendHistory.TileRecord record;
    private int version = -1;
    private float populationMax = 1f, plantMin, plantMax = 1f;
    public int MeshRefreshCount { get; private set; }

    public void BindLabels(TextMeshProUGUI populationTop, TextMeshProUGUI populationBottom,
        TextMeshProUGUI plantsTop, TextMeshProUGUI plantsBottom, TextMeshProUGUI start,
        TextMeshProUGUI end, TextMeshProUGUI key, TextMeshProUGUI message)
    {
        leftTop = populationTop; leftBottom = populationBottom;
        rightTop = plantsTop; rightBottom = plantsBottom;
        firstDay = start; lastDay = end; legend = key; status = message;
    }
    public void Show(TileTrendHistory.TileRecord next)
    {
        if (ReferenceEquals(record, next) && version == (next != null ? next.Version : -1)) return;
        record = next; version = next != null ? next.Version : -1;
        UpdatePresentation(); SetVerticesDirty();
    }
    protected override void OnEnable()
    {
        base.OnEnable(); UpdatePresentation();
    }
#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate(); UpdatePresentation();
    }
#endif
    private int Visible => record == null ? 0 : Mathf.Min(maximumVisiblePopulations, record.Populations.Count);
    private void UpdatePresentation()
    {
        if (leftTop == null) return;
        if (record == null || record.Count == 0)
        {
            leftTop.text = rightTop.text = "--"; leftBottom.text = rightBottom.text = "0";
            firstDay.text = lastDay.text = "Day --";
            legend.text = "种群：左轴    植物库存：右轴";
            status.text = "运行后开始记录地块趋势";
            return;
        }
        populationMax = 1f; plantMin = float.PositiveInfinity; plantMax = 0f;
        for (int i = 0; i < record.Count; i++)
        {
            float value = record.Plants(i);
            plantMin = Mathf.Min(plantMin, value); plantMax = Mathf.Max(plantMax, value);
            for (int s = 0; s < Visible; s++)
            {
                float amount = record.Amount(record.Populations[s], i);
                if (!float.IsNaN(amount)) populationMax = Mathf.Max(populationMax, amount);
            }
        }
        if (autoPlantRange)
        {
            float padding = Mathf.Max(1f, (plantMax - plantMin) * .1f);
            if (plantMax - plantMin < 1f) padding = Mathf.Max(padding, plantMax * .01f);
            plantMin = Mathf.Max(0f, plantMin - padding); plantMax += padding;
        }
        else { plantMin = 0f; plantMax = Mathf.Max(1f, plantMax); }
        leftTop.text = Format(populationMax); leftBottom.text = "0";
        rightTop.text = Format(plantMax); rightBottom.text = Format(plantMin);
        firstDay.text = "Day " + record.Day(0); lastDay.text = "Day " + record.Day(record.Count - 1);
        status.text = record.Count < 2 ? "已记录当前天；下一天后显示折线" : "";
        var text = new StringBuilder();
        for (int s = 0; s < Visible; s++)
        {
            TileTrendHistory.Series item = record.Populations[s];
            string name = string.IsNullOrWhiteSpace(item.Name) ? "种群" : item.Name;
            text.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(item.Color)).Append('>')
                .Append(name.Replace("<", "").Replace(">", "")).Append('·')
                .Append(item.Id.Substring(0, Mathf.Min(4, item.Id.Length))).Append("</color>  ");
        }
        text.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(plantColor)).Append(">植物（右轴")
            .Append(autoPlantRange ? "，自动范围" : "").Append("）</color>");
        if (Visible < record.Populations.Count)
            text.Append("  显示 ").Append(Visible).Append('/').Append(record.Populations.Count).Append(" 群");
        legend.text = text.ToString();
    }
    private static string Format(float value) => value >= 10000f ? (value / 10000f).ToString("0.#") + "万" : value.ToString("0");
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); MeshRefreshCount++;
        Rect plot = rectTransform.rect;
        for (int i = 0; i <= 4; i++)
            Line(mesh, new Vector2(plot.xMin, plot.yMin + plot.height * i / 4f),
                new Vector2(plot.xMax, plot.yMin + plot.height * i / 4f), gridColor, 1f);
        Line(mesh, new Vector2(plot.xMin, plot.yMin), new Vector2(plot.xMin, plot.yMax), gridColor, 1f);
        Line(mesh, new Vector2(plot.xMax, plot.yMin), new Vector2(plot.xMax, plot.yMax), gridColor, 1f);
        if (record == null || record.Count == 0) return;
        for (int s = 0; s < Visible; s++) DrawSeries(mesh, record.Populations[s], false);
        DrawSeries(mesh, null, true);
    }
    private void DrawSeries(VertexHelper mesh, TileTrendHistory.Series series, bool plants)
    {
        Rect plot = rectTransform.rect;
        bool previousValid = false; Vector2 previous = Vector2.zero;
        int start = record.Day(0), span = Mathf.Max(1, record.Day(record.Count - 1) - start);
        Color tint = plants ? plantColor : series.Color;
        for (int i = 0; i < record.Count; i++)
        {
            float amount = plants ? record.Plants(i) : record.Amount(series, i);
            if (float.IsNaN(amount) || float.IsInfinity(amount)) { previousValid = false; continue; }
            float ratio = plants ? (amount - plantMin) / Mathf.Max(1f, plantMax - plantMin) : amount / populationMax;
            Vector2 point = new Vector2(plot.xMin + plot.width * (record.Day(i) - start) / span,
                plot.yMin + plot.height * Mathf.Clamp01(ratio));
            if (previousValid) Line(mesh, previous, point, tint, lineWidth);
            else Line(mesh, point - Vector2.right * 1.5f, point + Vector2.right * 1.5f, tint, 3f);
            previous = point; previousValid = true;
        }
    }
    private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, Color tint, float width)
    {
        Vector2 delta = b - a;
        if (delta.sqrMagnitude < .000001f) return;
        Vector2 offset = new Vector2(-delta.y, delta.x).normalized * width * .5f;
        int index = mesh.currentVertCount;
        mesh.AddVert(a - offset, tint, Vector2.zero); mesh.AddVert(a + offset, tint, Vector2.zero);
        mesh.AddVert(b + offset, tint, Vector2.zero); mesh.AddVert(b - offset, tint, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
    }
}
