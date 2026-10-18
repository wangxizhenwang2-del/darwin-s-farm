using System;
using System.Collections.Generic;
using Darwin.Rendering;
using UnityEngine;

// Queries the actual capped, generated water mesh, including disconnected islands.
public sealed class WaterTileSpace
{
    private struct Triangle { public Vector2 a, b, c; }
    private struct Edge { public Vector2 a, b; public int count; }
    private readonly Dictionary<Vector2Int, List<Triangle>> buckets = new Dictionary<Vector2Int, List<Triangle>>();
    private readonly List<Edge> banks = new List<Edge>();
    private readonly List<Edge> boundary = new List<Edge>();
    private readonly MoebiusRiverTile river;
    public Bounds Bounds { get; }
    public MeshRenderer Renderer { get; }
    public float Height { get; }
    private static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);
    private static Vector2Int Bucket(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    public WaterTileSpace(MeshRenderer renderer, MoebiusRiverTile river)
    {
        this.river = river;
        Renderer = renderer; Bounds = renderer.bounds; Height = river.WorldWaterHeight;
        Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] vertices = mesh.vertices; int[] indices = mesh.triangles;
        var edges = new Dictionary<(Vector2Int, Vector2Int), Edge>();
        for (int i = 0; i < vertices.Length; i++) vertices[i] = renderer.transform.TransformPoint(vertices[i]);
        for (int i = 0; i < indices.Length; i += 3)
        {
            var t = new Triangle { a = Flat(vertices[indices[i]]), b = Flat(vertices[indices[i + 1]]), c = Flat(vertices[indices[i + 2]]) };
            if (Mathf.Abs(Cross(t.b - t.a, t.c - t.a)) < .0000001f) continue;
            Vector2Int min = Bucket(Vector2.Min(t.a, Vector2.Min(t.b, t.c))), max = Bucket(Vector2.Max(t.a, Vector2.Max(t.b, t.c)));
            for (int z = min.y; z <= max.y; z++) for (int x = min.x; x <= max.x; x++)
            {
                var key = new Vector2Int(x, z);
                if (!buckets.TryGetValue(key, out List<Triangle> list)) buckets[key] = list = new List<Triangle>();
                list.Add(t);
            }
            AddEdge(t.a, t.b); AddEdge(t.b, t.c); AddEdge(t.c, t.a);
        }
        foreach (Edge edge in edges.Values)
        {
            if (edge.count != 1) continue;
            boundary.Add(edge); banks.Add(edge);
        }
        void AddEdge(Vector2 a, Vector2 b)
        {
            Vector2Int qa = Vector2Int.RoundToInt(a * 10000), qb = Vector2Int.RoundToInt(b * 10000);
            bool order = qa.x < qb.x || qa.x == qb.x && qa.y <= qb.y;
            var key = order ? (qa, qb) : (qb, qa);
            edges.TryGetValue(key, out Edge value);
            value.a = a; value.b = b; value.count++; edges[key] = value;
        }
    }
    public void ConfigurePortals(Func<Vector3, Vector3, bool> passable)
    {
        banks.Clear();
        foreach (Edge edge in boundary)
        {
            Vector3 normal = Vector3.zero;
            foreach (RiverPort port in river.profile.ports)
            {
                Vector3 n = river.transform.TransformDirection(port.Normal);
                Vector2 center = Flat(river.transform.TransformPoint(port.Center));
                if (Mathf.Abs(Vector2.Dot(edge.a - center, Flat(n))) < .005f &&
                    Mathf.Abs(Vector2.Dot(edge.b - center, Flat(n))) < .005f) { normal = n; break; }
            }
            if (normal == Vector3.zero) { banks.Add(edge); continue; }
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(edge.a, edge.b) / .15f));
            int closedStart = -1;
            for (int i = 0; i < steps; i++)
            {
                Vector2 a = Vector2.Lerp(edge.a, edge.b, i / (float)steps), b = Vector2.Lerp(edge.a, edge.b, (i + 1f) / steps);
                Vector2 mid = (a + b) * .5f;
                bool open = passable(normal, new Vector3(mid.x, Height, mid.y));
                if (!open && closedStart < 0) closedStart = i;
                if (closedStart >= 0 && (open || i == steps - 1))
                {
                    banks.Add(new Edge { a = Vector2.Lerp(edge.a, edge.b, closedStart / (float)steps), b = open ? a : b, count = 1 });
                    closedStart = -1;
                }
            }
        }
    }
    public bool Contains(Vector3 world, float radius)
    {
        Vector2 p = Flat(world);
        if (!buckets.TryGetValue(Bucket(p), out List<Triangle> list)) return false;
        bool inside = false;
        foreach (Triangle t in list)
        {
            float a = Cross(t.b - t.a, p - t.a), b = Cross(t.c - t.b, p - t.b), c = Cross(t.a - t.c, p - t.c);
            if (a >= -.00001f && b >= -.00001f && c >= -.00001f || a <= .00001f && b <= .00001f && c <= .00001f)
            { inside = true; break; }
        }
        if (!inside) return false;
        foreach (Edge edge in banks)
        {
            Vector2 d = edge.b - edge.a;
            Vector2 closest = edge.a + d * Mathf.Clamp01(Vector2.Dot(p - edge.a, d) / Mathf.Max(.0000001f, d.sqrMagnitude));
            if ((closest - p).sqrMagnitude < radius * radius) return false;
        }
        return true;
    }
    public bool TrySpawn(float radius, out Vector3 point, Vector3? hint = null, Func<Vector3, bool> allowed = null)
    {
        point = default; float best = float.PositiveInfinity;
        Vector3 center = hint ?? Bounds.center;
        for (float z = Bounds.min.z + .25f; z < Bounds.max.z; z += .5f)
            for (float x = Bounds.min.x + .25f; x < Bounds.max.x; x += .5f)
            {
                var p = new Vector3(x, Height, z);
                if (!Contains(p, radius) || allowed != null && !allowed(p)) continue;
                float distance = (p - center).sqrMagnitude;
                if (distance < best) { best = distance; point = p; }
            }
        return !float.IsPositiveInfinity(best);
    }
}
