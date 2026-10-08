using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// The projected top-face union, rather than GridToWorld/TileSize, owns movement.
// A swept disc enclosing a tilted cube must clear every edge of that union.
public sealed class PopulationTileSpace
{
    private struct Edge
    {
        public Vector2 a, b;
        public int count;
    }

    private readonly List<Edge> boundary = new List<Edge>();
    private readonly List<Collider> ground = new List<Collider>();
    private readonly MapTileInstance tile;
    private readonly NavMeshQueryFilter filter;
    private readonly Dictionary<float, PopulationLocalNavigation> routes = new Dictionary<float, PopulationLocalNavigation>();
    public Bounds Bounds { get; private set; }
    public bool IsReady => ground.Count > 0 && boundary.Count > 0;

    public PopulationTileSpace(MapTileInstance owner, NavMeshQueryFilter navigationFilter)
    {
        tile = owner;
        filter = navigationFilter;
        Rebuild();
    }

    public void Rebuild()
    {
        ground.Clear();
        boundary.Clear();
        routes.Clear();
        if (tile == null) return;
        var edges = new Dictionary<(Vector2Int, Vector2Int), Edge>();
        bool first = true;
        int layer = LayerMask.NameToLayer("Ground");
        foreach (Collider collider in tile.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger || collider.gameObject.layer != layer)
                continue;
            // Buildings nested under a tile are obstacles, never terrain ownership.
            if (collider.name != "ColorSurface" && collider.name != "Core" &&
                !collider.name.StartsWith("Border_")) continue;
            ground.Add(collider);
            Bounds bounds = Bounds;
            if (first) { bounds = collider.bounds; first = false; }
            else bounds.Encapsulate(collider.bounds);
            Bounds = bounds;
            if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
            {
                Mesh mesh = meshCollider.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = collider.transform.TransformPoint(vertices[indices[i]]);
                    Vector3 b = collider.transform.TransformPoint(vertices[indices[i + 1]]);
                    Vector3 c = collider.transform.TransformPoint(vertices[indices[i + 2]]);
                    if (Vector3.Cross(b - a, c - a).y <= 0.00001f) continue;
                    AddEdge(edges, a, b); AddEdge(edges, b, c); AddEdge(edges, c, a);
                }
            }
            else if (collider is BoxCollider box)
            {
                Vector3 half = box.size * 0.5f;
                Vector3 a = box.transform.TransformPoint(box.center + new Vector3(-half.x, half.y, -half.z));
                Vector3 b = box.transform.TransformPoint(box.center + new Vector3(-half.x, half.y, half.z));
                Vector3 c = box.transform.TransformPoint(box.center + new Vector3(half.x, half.y, half.z));
                Vector3 d = box.transform.TransformPoint(box.center + new Vector3(half.x, half.y, -half.z));
                AddEdge(edges, a, b); AddEdge(edges, b, c);
                AddEdge(edges, c, d); AddEdge(edges, d, a);
            }
        }
        foreach (Edge edge in edges.Values)
            if (edge.count == 1) boundary.Add(edge);
    }

    private static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

    private static void AddEdge(Dictionary<(Vector2Int, Vector2Int), Edge> edges,
        Vector3 start, Vector3 end)
    {
        Vector2 a = Flat(start), b = Flat(end);
        Vector2Int ka = new Vector2Int(Mathf.RoundToInt(a.x * 10000f), Mathf.RoundToInt(a.y * 10000f));
        Vector2Int kb = new Vector2Int(Mathf.RoundToInt(b.x * 10000f), Mathf.RoundToInt(b.y * 10000f));
        if (ka.x > kb.x || (ka.x == kb.x && ka.y > kb.y))
        { Vector2Int swap = ka; ka = kb; kb = swap; }
        var key = (ka, kb);
        edges.TryGetValue(key, out Edge edge);
        edge.a = a; edge.b = b; edge.count++;
        edges[key] = edge;
    }

    public bool TryGround(Vector3 position, out RaycastHit hit)
    {
        hit = default;
        if (!IsReady) return false;
        Ray ray = new Ray(new Vector3(position.x, Bounds.max.y + 2f, position.z), Vector3.down);
        bool found = false;
        foreach (Collider collider in ground)
        {
            if (collider != null && collider.enabled &&
                collider.Raycast(ray, out RaycastHit candidate, Bounds.size.y + 4f) &&
                candidate.normal.y > 0.0001f && (!found || candidate.point.y > hit.point.y))
            { hit = candidate; found = true; }
        }
        return found;
    }

    // Encloses the cube even after alignment to a slope and normal-height offset.
    public static float Clearance(float size) => size * 1.4f + 0.02f;

    public bool TryPose(Vector3 position, float size, out Vector3 center, out Vector3 normal)
    {
        center = position; normal = Vector3.up;
        if (!TryGround(position, out RaycastHit hit) ||
            !ClearsBoundary(position, position, Clearance(size))) return false;
        if (!NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 0.2f, filter) ||
            Vector2.Distance(Flat(navHit.position), Flat(hit.point)) > 0.08f) return false;
        normal = hit.normal.normalized;
        center = hit.point + normal * (size * 0.5f);
        // A conservative sphere also covers all orientations while turning.
        foreach (Collider obstacle in Physics.OverlapSphere(center, size * 0.867f,
                     ~0, QueryTriggerInteraction.Ignore))
            if (IsObstacle(obstacle)) return false;
        return true;
    }

    private bool IsObstacle(Collider collider)
    {
        if (collider == null || !collider.enabled || collider.isTrigger) return false;
        if (collider.GetComponentInParent<WhiteboxPopulation>() != null) return false;
        // Players are moving discs handled bidirectionally by the controller.
        // They must not invalidate a stationary member's terrain pose/hide it.
        if (collider.GetComponentInParent<PopulationPlayerBarrier>() != null ||
            collider.GetComponentInParent<ClickToMove>() != null) return false;
        // Other terrain is handled by the owning boundary and NavMesh.
        MapTileInstance owner = collider.GetComponentInParent<MapTileInstance>();
        return owner == null || (collider.name != "ColorSurface" && collider.name != "Core" &&
            !collider.name.StartsWith("Border_"));
    }

    public bool CanTraverse(Vector3 from, Vector3 to, float size)
    {
        if (!ClearsBoundary(from, to, Clearance(size)) ||
            !TryPose(from, size, out _, out _) || !TryPose(to, size, out _, out _) ||
            !TryGround(from, out RaycastHit start) || !TryGround(to, out RaycastHit end)) return false;
        if (NavMesh.Raycast(start.point, end.point, out _, filter)) return false;
        // Sweep an enclosing vertical capsule across the WHOLE segment. Its height
        // spans the tile, so even a thin building between samples is not skipped.
        Vector3 low = new Vector3(from.x, Bounds.min.y + 0.05f, from.z);
        Vector3 high = new Vector3(from.x, Bounds.max.y + size * 2f, from.z);
        Vector3 delta = new Vector3(to.x - from.x, 0f, to.z - from.z);
        foreach (Collider obstacle in Physics.OverlapCapsule(low, high, Clearance(size),
                     ~0, QueryTriggerInteraction.Ignore))
            if (IsObstacle(obstacle)) return false;
        if (delta.sqrMagnitude > 0.000001f)
            foreach (RaycastHit hit in Physics.CapsuleCastAll(low, high, Clearance(size),
                         delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (IsObstacle(hit.collider)) return false;
        return true;
    }

    private bool ClearsBoundary(Vector3 from, Vector3 to, float radius)
    {
        if (!TryGround(from, out _) || !TryGround(to, out _)) return false;
        foreach (Edge edge in boundary)
            if (SegmentDistanceSquared(Flat(from), Flat(to), edge.a, edge.b) <= radius * radius)
                return false;
        return true;
    }

    public static float SegmentDistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        Vector2 ab = b - a, cd = d - c;
        float denominator = Cross(ab, cd);
        if (Mathf.Abs(denominator) > 0.0000001f)
        {
            float t = Cross(c - a, cd) / denominator;
            float u = Cross(c - a, ab) / denominator;
            if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return 0f;
        }
        return Mathf.Min(PointDistance(a, c, d), PointDistance(b, c, d),
            PointDistance(c, a, b), PointDistance(d, a, b));
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    private static float PointDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 delta = b - a;
        float t = delta.sqrMagnitude < 0.0000001f ? 0f :
            Mathf.Clamp01(Vector2.Dot(p - a, delta) / delta.sqrMagnitude);
        return (p - a - delta * t).sqrMagnitude;
    }

    public bool CanWanderAt(Vector3 position, float size, float edgeMargin) =>
        ClearsBoundary(position, position, Clearance(size) + Mathf.Max(0f, edgeMargin));

    public Vector3 BoundaryInward(Vector3 position)
    {
        Vector2 point = Flat(position);
        float nearest = float.PositiveInfinity;
        Vector2 inward = Vector2.zero;
        foreach (Edge edge in boundary)
        {
            Vector2 delta = edge.b - edge.a;
            float t = delta.sqrMagnitude < 0.000001f ? 0f :
                Mathf.Clamp01(Vector2.Dot(point - edge.a, delta) / delta.sqrMagnitude);
            Vector2 away = point - edge.a - delta * t;
            float distance = away.magnitude;
            if (distance < nearest - 0.05f) { nearest = distance; inward = away.normalized; }
            else if (Mathf.Abs(distance - nearest) <= 0.05f) inward += away.normalized;
        }
        return new Vector3(inward.x, 0f, inward.y).normalized;
    }

    public void InvalidateRoutes() => routes.Clear();

    public bool TryPath(Vector3 from, Vector3 target, float size, out Vector3[] corners,
        System.Func<Vector3, Vector3, bool> bodyMove = null)
    {
        corners = null;
        if (!TryGround(target, out RaycastHit end)) return false;
        if (!routes.TryGetValue(size, out PopulationLocalNavigation navigation))
        {
            navigation = new PopulationLocalNavigation(Bounds, 0.65f, (a, b) => CanTraverse(a, b, size));
            routes.Add(size, navigation);
        }
        if (!navigation.TryPath(from, end.point, bodyMove, out corners)) return false;
        // Sampling does not supply height: every returned waypoint uses this tile.
        for (int i = 0; i < corners.Length; i++)
        {
            if (!TryGround(corners[i], out RaycastHit ground)) { corners = null; return false; }
            corners[i] = ground.point;
        }
        return true;
    }
}
