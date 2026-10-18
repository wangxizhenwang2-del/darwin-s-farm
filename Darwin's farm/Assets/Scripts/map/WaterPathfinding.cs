using System.Collections.Generic;
using UnityEngine;

// A bounded mesh-constrained raster search. Exact endpoints and every segment are checked.
public static class WaterPathfinding
{
    public static bool Segment(MapWaterController water, Vector3 from, Vector3 to, float radius, Vector2Int? onlyTile = null,
        HashSet<Vector2Int> allowedTiles = null)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(new Vector2(to.x - from.x, to.z - from.z).magnitude / .12f));
        MapTileInstance previous = null;
        for (int i = 0; i <= steps; i++)
        {
            if (!water.TrySample(Vector3.Lerp(from, to, i / (float)steps), radius, out _, out MapTileInstance tile) ||
                onlyTile.HasValue && tile.Coordinate != onlyTile.Value ||
                allowedTiles != null && !allowedTiles.Contains(tile.Coordinate)) return false;
            if (previous && previous != tile && (Mathf.Abs(previous.HeightLevel - tile.HeightLevel) > 1 || !water.Connects(previous, tile))) return false;
            previous = tile;
        }
        return true;
    }
    public static bool TryPath(MapWaterController water, Vector3 from, Vector3 to, float radius,
        out Vector3[] path, Vector2Int? onlyTile = null, HashSet<Vector2Int> allowedTiles = null)
    {
        path = null;
        if (!water || !water.TrySample(from, radius, out from, out var sourceTile) ||
            !water.TrySample(to, radius, out to, out var targetTile) ||
            onlyTile.HasValue && (sourceTile.Coordinate != onlyTile.Value || targetTile.Coordinate != onlyTile.Value) ||
            allowedTiles != null && (!allowedTiles.Contains(sourceTile.Coordinate) || !allowedTiles.Contains(targetTile.Coordinate))) return false;
        if (Segment(water, from, to, radius, onlyTile, allowedTiles))
        { path = FollowSurface(water, new[] { from, to }, radius); return true; }
        Vector3 origin = water.Grid.GridToWorld(Vector2Int.zero);
        Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.RoundToInt((p.x - origin.x - .25f) * 2), Mathf.RoundToInt((p.z - origin.z - .25f) * 2));
        Vector3 Point(Vector2Int k) => new Vector3(origin.x + k.x * .5f + .25f, 0, origin.z + k.y * .5f + .25f);
        var positions = new Dictionary<Vector2Int, Vector3>();
        var invalid = new HashSet<Vector2Int>();
        var costs = new Dictionary<Vector2Int, float>();
        var parents = new Dictionary<Vector2Int, Vector2Int>();
        var closed = new HashSet<Vector2Int>();
        var open = new SortedSet<(float score, int serial, Vector2Int key)>(
            Comparer<(float score, int serial, Vector2Int key)>.Create((a, b) =>
                a.score != b.score ? a.score.CompareTo(b.score) : a.serial.CompareTo(b.serial)));
        int serial = 0;
        bool Sample(Vector2Int k, out Vector3 p)
        {
            if (positions.TryGetValue(k, out p)) return true;
            if (invalid.Contains(k)) return false;
            if (!water.TrySample(Point(k), radius, out p, out MapTileInstance tile) || onlyTile.HasValue && tile.Coordinate != onlyTile.Value ||
                allowedTiles != null && !allowedTiles.Contains(tile.Coordinate))
            { invalid.Add(k); return false; }
            positions[k] = p; return true;
        }
        Vector2Int startKey = Key(from);
        // Several anchors avoid projecting through the bank or into another island.
        for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
        {
            var k = startKey + new Vector2Int(x, z);
            if (!Sample(k, out Vector3 p) || !Segment(water, from, p, radius, onlyTile, allowedTiles)) continue;
            float cost = Vector3.Distance(from, p); costs[k] = cost;
            open.Add((cost + Vector3.Distance(p, to), serial++, k));
        }
        int budget = Mathf.Min(65536, Mathf.Max(2048, water.Network ? water.Network.transform.childCount * 1300 : 2048));
        while (open.Count > 0 && closed.Count < budget)
        {
            var first = open.Min; open.Remove(first); Vector2Int key = first.key;
            if (!closed.Add(key)) continue;
            Vector3 current = positions[key];
            if ((new Vector2(current.x - to.x, current.z - to.z)).sqrMagnitude < 1f && Segment(water, current, to, radius, onlyTile, allowedTiles))
            {
                var route = new List<Vector3> { to, current };
                while (parents.TryGetValue(key, out Vector2Int parent)) { key = parent; route.Add(positions[key]); }
                route.Add(from); route.Reverse(); path = FollowSurface(water, route, radius); return true;
            }
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && z == 0) continue;
                var next = key + new Vector2Int(x, z);
                if (closed.Contains(next) || !Sample(next, out Vector3 p) || !Segment(water, current, p, radius, onlyTile, allowedTiles)) continue;
                float cost = costs[key] + Vector3.Distance(current, p);
                if (costs.TryGetValue(next, out float old) && old <= cost) continue;
                costs[next] = cost; parents[next] = key;
                open.Add((cost + Vector3.Distance(p, to), serial++, next));
            }
        }
        return false;
    }
    private static Vector3[] FollowSurface(MapWaterController water, IList<Vector3> route, float radius)
    {
        var result = new List<Vector3> { route[0] };
        for (int i = 1; i < route.Count; i++)
        {
            Vector3 a = route[i - 1], b = route[i];
            int steps = Mathf.Max(1, Mathf.CeilToInt(new Vector2(b.x - a.x, b.z - a.z).magnitude / .2f));
            for (int step = 1; step <= steps; step++)
                if (water.TrySample(Vector3.Lerp(a, b, step / (float)steps), radius, out var point, out _)) result.Add(point);
        }
        return result.ToArray();
    }
}
