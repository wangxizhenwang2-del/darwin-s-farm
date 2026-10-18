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
    private readonly Dictionary<float, PopulationLocalNavigation> fineRoutes = new Dictionary<float, PopulationLocalNavigation>();
    private struct NavigationTriangle
    {
        public Vector3 a, b, c;
    }
    private readonly Dictionary<Vector2Int, List<NavigationTriangle>> navigationCells =
        new Dictionary<Vector2Int, List<NavigationTriangle>>();
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
        fineRoutes.Clear();
        navigationCells.Clear();
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
        CacheNavigationSurface();
    }

    // SamplePosition returns the nearest point in 3D. On a slope, voxel height
    // error moves that point sideways even when the requested XZ is walkable.
    // Cache the NavMesh footprint; query its detail height separately at fixed XZ.
    private void CacheNavigationSurface()
    {
        NavMeshTriangulation mesh = NavMesh.CalculateTriangulation();
        for (int i = 0; i < mesh.indices.Length; i += 3)
        {
            if ((filter.areaMask & (1 << mesh.areas[i / 3])) == 0) continue;
            var triangle = new NavigationTriangle
            {
                a = mesh.vertices[mesh.indices[i]],
                b = mesh.vertices[mesh.indices[i + 1]],
                c = mesh.vertices[mesh.indices[i + 2]]
            };
            Vector3 min = Vector3.Min(triangle.a, Vector3.Min(triangle.b, triangle.c));
            Vector3 max = Vector3.Max(triangle.a, Vector3.Max(triangle.b, triangle.c));
            if (max.x < Bounds.min.x || min.x > Bounds.max.x ||
                max.z < Bounds.min.z || min.z > Bounds.max.z) continue;
            Vector2Int low = NavigationCell(Vector3.Max(min, Bounds.min));
            Vector2Int high = NavigationCell(Vector3.Min(max, Bounds.max));
            for (int z = low.y; z <= high.y; z++)
                for (int x = low.x; x <= high.x; x++)
                {
                    var key = new Vector2Int(x, z);
                    if (!navigationCells.TryGetValue(key, out List<NavigationTriangle> triangles))
                        navigationCells.Add(key, triangles = new List<NavigationTriangle>());
                    triangles.Add(triangle);
                }
        }
    }

    private Vector2Int NavigationCell(Vector3 point) => new Vector2Int(
        Mathf.FloorToInt(point.x - Bounds.min.x), Mathf.FloorToInt(point.z - Bounds.min.z));

    private bool TryNavigationPoint(Vector3 groundPoint, out Vector3 point)
    {
        point = default;
        if (!navigationCells.TryGetValue(NavigationCell(groundPoint),
            out List<NavigationTriangle> triangles)) return false;
        float closest = 0.6f; // Allows actual bake height error, not polygon-plane error.
        bool found = false;
        foreach (NavigationTriangle triangle in triangles)
        {
            Vector2 ab = Flat(triangle.b - triangle.a), ac = Flat(triangle.c - triangle.a);
            Vector2 offset = Flat(groundPoint - triangle.a);
            float determinant = Cross(ab, ac);
            if (Mathf.Abs(determinant) < 0.000001f) continue;
            float u = Cross(offset, ac) / determinant;
            float v = Cross(ab, offset) / determinant;
            if (u < -0.00001f || v < -0.00001f || u + v > 1.00001f) continue;
            // Triangulation contains every agent type; verify against this filter.
            // A large polygon can bridge a relief valley while its detail mesh
            // follows the ground. Comparing the coarse plane with the ground
            // creates false holes inside otherwise valid movement segments.
            // Validate the actual detail height, retaining the footprint test
            // and the existing limit on horizontal snapping.
            if (!NavMesh.SamplePosition(groundPoint, out NavMeshHit hit, 0.6f, filter) ||
                Flat(hit.position - groundPoint).sqrMagnitude > 0.04f) continue;
            float difference = Mathf.Abs(hit.position.y - groundPoint.y);
            if (difference >= closest) continue;
            closest = difference;
            point = new Vector3(groundPoint.x, hit.position.y, groundPoint.z);
            found = true;
        }
        return found;
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
        if (!TryNavigationPoint(hit.point, out _)) return false;
        normal = hit.normal.normalized;
        center = hit.point + normal * (size * 0.5f);
        // A conservative sphere also covers all orientations while turning.
        foreach (Collider obstacle in Physics.OverlapSphere(center, size * 0.867f,
                     ~0, QueryTriggerInteraction.Ignore))
            if (IsObstacle(obstacle)) return false;
        return true;
    }

    // A billboard stands vertically above its route position. Offsetting it along
    // a triangle's normal makes it jump sideways whenever that normal changes.
    public bool TryRepresentativePose(Vector3 position, Vector3 heading, float size,
        out Vector3 groundPosition, out Vector3 center, out Quaternion rotation)
    {
        groundPosition = center = position;
        rotation = Quaternion.identity;
        if (!TryPose(position, size, out _, out _) || !TryGround(position, out RaycastHit groundHit))
            return false;
        groundPosition = groundHit.point;
        heading.y = 0f;
        rotation = Quaternion.LookRotation(heading.sqrMagnitude > 0.001f ? heading : Vector3.forward, Vector3.up);
        center = groundPosition + Vector3.up * (size * 0.5f);
        // Keep the upright proxy's entire footprint above the relief. Height
        // follows the continuous ground surface rather than its per-face normal.
        float support = 0f;
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                Vector3 bottom = center + rotation * new Vector3(x, -1f, z) * (size * 0.5f);
                if (!TryGround(bottom, out RaycastHit contact)) return false;
                support = Mathf.Max(support, contact.point.y - bottom.y);
            }
        center.y += support;
        return true;
    }

    private bool IsObstacle(Collider collider)
    {
        if (collider == null || !collider.enabled || collider.isTrigger) return false;
        if (collider.GetComponentInParent<WhiteboxPopulation>() != null) return false;
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
        if (!TryNavigationPoint(start.point, out Vector3 navStart) ||
            !TryNavigationPoint(end.point, out Vector3 navEnd) ||
            NavMesh.Raycast(navStart, navEnd, out _, filter)) return false;
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

    public void InvalidateRoutes() { routes.Clear(); fineRoutes.Clear(); }

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
        if (!navigation.TryPath(from, end.point, bodyMove, out corners))
        {
            // A valid body can stand between the coarse samples after a biome
            // changes around it. Retry a denser grid with identical safety checks.
            if (!fineRoutes.TryGetValue(size, out navigation))
            {
                navigation = new PopulationLocalNavigation(Bounds, 0.325f, (a, b) => CanTraverse(a, b, size));
                fineRoutes.Add(size, navigation);
            }
            if (!navigation.TryPath(from, end.point, bodyMove, out corners)) return false;
        }
        // Sampling does not supply height: every returned waypoint uses this tile.
        for (int i = 0; i < corners.Length; i++)
        {
            if (!TryGround(corners[i], out RaycastHit ground)) { corners = null; return false; }
            corners[i] = ground.point;
        }
        return true;
    }
}
