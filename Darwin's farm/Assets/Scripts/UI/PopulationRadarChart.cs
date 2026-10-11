using UnityEngine;
using UnityEngine.UI;

// Display-only normalized polygon; it never writes back to PopulationData.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PopulationRadarChart : MaskableGraphic
{
    [SerializeField] private Color gridColor = new Color(.12f, .17f, .13f, .55f);
    [SerializeField] private Color fillColor = new Color(.25f, .85f, .50f, .65f);
    [SerializeField, Min(1f)] private float maximumValue = 100f;
    [SerializeField, Min(.5f)] private float lineWidth = 1.5f;
    [Header("Species initial attributes")]
    [SerializeField] private Color baselineColor = new Color(.85f, .12f, .12f, 1f);
    [SerializeField, Min(.5f)] private float baselineLineWidth = 2f;
    [SerializeField, Min(1f)] private float dashLength = 5f;
    [SerializeField, Min(1f)] private float dashGap = 3f;
    private readonly float[] values = new float[5];
    private readonly float[] baselineValues = new float[5];
    private bool hasBaseline;

    public void SetValues(float temperature, float humidity, float movement, float fertility, float size)
    {
        bool changed = Set(values, 0, temperature) | Set(values, 1, humidity) |
            Set(values, 2, movement) | Set(values, 3, fertility) | Set(values, 4, size);
        if (changed) SetVerticesDirty();
    }

    public void SetBaseline(SpeciesData species)
    {
        bool enabled = species != null;
        bool changed = hasBaseline != enabled;
        hasBaseline = enabled;
        if (enabled)
            changed |= Set(baselineValues, 0, species.baseFitTemperature) |
                Set(baselineValues, 1, species.baseFitHumidity) |
                Set(baselineValues, 2, species.baseMovementAbility) |
                Set(baselineValues, 3, species.baseFertility) |
                Set(baselineValues, 4, species.baseSize);
        if (changed) SetVerticesDirty();
    }

    private static bool Set(float[] target, int index, float value)
    {
        bool changed = !Mathf.Approximately(target[index], value);
        target[index] = value;
        return changed;
    }

    private Vector2 Point(int index, float ratio)
    {
        float angle = (90f - index * 72f) * Mathf.Deg2Rad;
        Rect rect = rectTransform.rect;
        float radius = Mathf.Min(rect.width, rect.height) * .46f;
        return rect.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * ratio;
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        for (int ring = 1; ring <= 4; ring++)
            for (int i = 0; i < 5; i++)
                Line(mesh, Point(i, ring / 4f), Point((i + 1) % 5, ring / 4f), gridColor);
        for (int i = 0; i < 5; i++) Line(mesh, rectTransform.rect.center, Point(i, 1f), gridColor);
        int start = mesh.currentVertCount;
        mesh.AddVert(rectTransform.rect.center, fillColor, Vector2.zero);
        for (int i = 0; i < 5; i++)
            mesh.AddVert(Point(i, Mathf.Clamp01(values[i] / maximumValue)), fillColor, Vector2.zero);
        for (int i = 0; i < 5; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 5);
        for (int i = 0; i < 5; i++)
        {
            Line(mesh, Point(i, 1f), Point((i + 1) % 5, 1f), Color.black);
            Line(mesh, Point(i, Mathf.Clamp01(values[i] / maximumValue)),
                Point((i + 1) % 5, Mathf.Clamp01(values[(i + 1) % 5] / maximumValue)), Color.black);
        }
        // Draw last so the initial outline stays visible above the current fill.
        if (hasBaseline)
            for (int i = 0; i < 5; i++)
                DashedLine(mesh, Point(i, Mathf.Clamp01(baselineValues[i] / maximumValue)),
                    Point((i + 1) % 5, Mathf.Clamp01(baselineValues[(i + 1) % 5] / maximumValue)));
    }

    private void DashedLine(VertexHelper mesh, Vector2 from, Vector2 to)
    {
        float distance = Vector2.Distance(from, to);
        if (distance < .001f) return;
        Vector2 direction = (to - from) / distance;
        float dash = Mathf.Max(1f, dashLength), gap = Mathf.Max(1f, dashGap);
        for (float offset = 0f; offset < distance; offset += dash + gap)
            Line(mesh, from + direction * offset,
                from + direction * Mathf.Min(distance, offset + dash), baselineColor, baselineLineWidth);
    }

    private void Line(VertexHelper mesh, Vector2 from, Vector2 to, Color tint, float width = -1f)
    {
        if ((to - from).sqrMagnitude < .0001f) return;
        Vector2 normal = new Vector2(-(to - from).y, (to - from).x).normalized * (width > 0f ? width : lineWidth) * .5f;
        int start = mesh.currentVertCount;
        mesh.AddVert(from - normal, tint, Vector2.zero); mesh.AddVert(from + normal, tint, Vector2.zero);
        mesh.AddVert(to + normal, tint, Vector2.zero); mesh.AddVert(to - normal, tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
    }
}
