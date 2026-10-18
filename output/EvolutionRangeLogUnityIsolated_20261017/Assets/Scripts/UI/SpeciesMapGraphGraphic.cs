using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Drawn below the node images. Food edges point from prey to predator.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SpeciesMapGraphGraphic : MaskableGraphic
{
    public struct Edge
    {
        public Vector2 from, to;
        public int fromIndex, toIndex;
    }

    private readonly List<Edge> edges = new List<Edge>();
    private bool food;
    private int hovered = -1;
    private static readonly Color Quiet = new Color(.47f, .51f, .54f, .70f);
    private static readonly Color Hot = new Color(1f, .52f, .12f, 1f);

    public void Configure(List<Edge> values, bool foodWeb)
    {
        edges.Clear();
        edges.AddRange(values);
        food = foodWeb;
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void SetHovered(int index)
    {
        if (hovered == index) return;
        hovered = index;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        foreach (Edge edge in edges)
        {
            Vector2 direction = edge.to - edge.from;
            if (direction.sqrMagnitude < 1f) continue;
            direction.Normalize();
            bool active = food && (hovered == edge.fromIndex || hovered == edge.toIndex);
            Color tint = active ? Hot : Quiet;
            // Keep both the shaft and the tip outside the circular portraits.
            Vector2 start = food ? edge.from + direction * 60f : edge.from;
            Vector2 end = food ? edge.to - direction * 60f : edge.to;
            if ((end - start).sqrMagnitude < 100f) continue;
            Stroke(mesh, start, end, active ? 3.8f : 2.2f, tint);
            if (food)
            {
                Vector2 normal = new Vector2(-direction.y, direction.x);
                Vector2 basePoint = end - direction * 15f;
                Triangle(mesh, end, basePoint + normal * 7f, basePoint - normal * 7f, tint);
            }
        }
    }

    private static void Stroke(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * (width * .5f);
        int i = mesh.currentVertCount;
        mesh.AddVert(a - n, color, Vector2.zero); mesh.AddVert(a + n, color, Vector2.zero);
        mesh.AddVert(b + n, color, Vector2.zero); mesh.AddVert(b - n, color, Vector2.zero);
        mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
    }

    private static void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        int i = mesh.currentVertCount;
        mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero);
        mesh.AddVert(c, color, Vector2.zero); mesh.AddTriangle(i, i + 1, i + 2);
    }
}
