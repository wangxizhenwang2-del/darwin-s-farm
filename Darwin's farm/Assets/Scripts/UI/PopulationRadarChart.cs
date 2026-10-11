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
    private readonly float[] values = new float[5];

    public void SetValues(float temperature, float humidity, float movement, float fertility, float fitness)
    {
        float[] next = { temperature, humidity, movement, fertility, fitness };
        bool changed = false;
        for (int i = 0; i < 5; i++)
        {
            changed |= !Mathf.Approximately(values[i], next[i]);
            values[i] = next[i];
        }
        if (changed) SetVerticesDirty();
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
    }

    private void Line(VertexHelper mesh, Vector2 from, Vector2 to, Color tint)
    {
        if ((to - from).sqrMagnitude < .0001f) return;
        Vector2 normal = new Vector2(-(to - from).y, (to - from).x).normalized * lineWidth * .5f;
        int start = mesh.currentVertCount;
        mesh.AddVert(from - normal, tint, Vector2.zero); mesh.AddVert(from + normal, tint, Vector2.zero);
        mesh.AddVert(to + normal, tint, Vector2.zero); mesh.AddVert(to - normal, tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
    }
}
