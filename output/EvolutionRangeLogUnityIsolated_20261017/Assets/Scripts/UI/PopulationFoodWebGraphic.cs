using UnityEngine;
using UnityEngine.UI;

// Simple nodes and energy-direction arrows; relation names are separate live text objects.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PopulationFoodWebGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Vector2 center = rectTransform.rect.center;
        foreach (float x in new[] { 64f, 225f, 386f })
        {
            Vector2 point = new Vector2(rectTransform.rect.xMin + x, center.y);
            int start = mesh.currentVertCount;
            mesh.AddVert(point, new Color(.68f, .94f, .74f, .8f), Vector2.zero);
            for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI * 2f / 32f;
                mesh.AddVert(point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 12f,
                    new Color(.68f, .94f, .74f, .8f), Vector2.zero);
                mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 32);
            }
            for (int i = 0; i < 32; i++)
            {
                float a = i * Mathf.PI * 2f / 32f, b = (i + 1) * Mathf.PI * 2f / 32f;
                Line(mesh, point + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 12f,
                    point + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * 12f);
            }
        }
        foreach (float x in new[] { 100f, 261f })
        {
            Vector2 from = new Vector2(rectTransform.rect.xMin + x, center.y), end = from + Vector2.right * 78f;
            Line(mesh, from, end); Line(mesh, end, end + new Vector2(-10f, 8f));
            Line(mesh, end, end + new Vector2(-10f, -8f));
        }
    }
    private static void Line(VertexHelper mesh, Vector2 a, Vector2 b)
    {
        Vector2 n = new Vector2(-(b-a).y, (b-a).x).normalized;
        int i = mesh.currentVertCount;
        mesh.AddVert(a-n, Color.black, Vector2.zero); mesh.AddVert(a+n, Color.black, Vector2.zero);
        mesh.AddVert(b+n, Color.black, Vector2.zero); mesh.AddVert(b-n, Color.black, Vector2.zero);
        mesh.AddTriangle(i,i+1,i+2); mesh.AddTriangle(i,i+2,i+3);
    }
}
