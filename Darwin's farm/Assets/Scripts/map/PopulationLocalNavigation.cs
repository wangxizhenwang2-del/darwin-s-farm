using System;
using System.Collections.Generic;
using UnityEngine;

// A bounded local fallback over the existing terrain/NavMesh validators. Bounds
// only locate samples; the supplied validators define the actual (possibly
// concave) walkable region. Static checks are cached; moving bodies never are.
public sealed class PopulationLocalNavigation
{
    private readonly Vector3 origin;
    private readonly float step;
    private readonly int width, height;
    private readonly Func<Vector3, Vector3, bool> terrainMove;
    private readonly byte[] valid;
    private readonly Dictionary<long, bool> edges = new Dictionary<long, bool>();

    public PopulationLocalNavigation(Bounds bounds, float spacing,
        Func<Vector3, Vector3, bool> canTraverse)
    {
        origin = bounds.min;
        step = Mathf.Max(0.1f, spacing, Mathf.Max(bounds.size.x, bounds.size.z) / 63f);
        width = Mathf.Clamp(Mathf.CeilToInt(bounds.size.x / step) + 1, 1, 64);
        height = Mathf.Clamp(Mathf.CeilToInt(bounds.size.z / step) + 1, 1, 64);
        valid = new byte[width * height];
        terrainMove = canTraverse;
    }

    private Vector3 Point(int index) => new Vector3(
        origin.x + index % width * step, origin.y, origin.z + index / width * step);
    private static float Distance(Vector3 a, Vector3 b) => Vector2.Distance(
        PopulationCollisionMath.Flat(a), PopulationCollisionMath.Flat(b));

    private bool Valid(int index)
    {
        if (valid[index] == 0) valid[index] = (byte)(terrainMove(Point(index), Point(index)) ? 1 : 2);
        return valid[index] == 1;
    }

    private bool Edge(int a, int b)
    {
        long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
        if (!edges.TryGetValue(key, out bool value))
        { value = terrainMove(Point(a), Point(b)); edges.Add(key, value); }
        return value;
    }

    private List<int> Connections(Vector3 endpoint, Func<Vector3, Vector3, bool> bodyMove, bool entering)
    {
        var result = new List<int>();
        int cx = Mathf.RoundToInt((endpoint.x - origin.x) / step);
        int cz = Mathf.RoundToInt((endpoint.z - origin.z) / step);
        for (int z = Mathf.Max(0, cz - 2); z <= Mathf.Min(height - 1, cz + 2); z++)
            for (int x = Mathf.Max(0, cx - 2); x <= Mathf.Min(width - 1, cx + 2); x++)
            {
                int index = z * width + x;
                Vector3 point = Point(index);
                if (!Valid(index) || Distance(point, endpoint) > step * 2.1f ||
                    !terrainMove(endpoint, point) ||
                    !(entering ? bodyMove(endpoint, point) : bodyMove(point, endpoint))) continue;
                result.Add(index);
            }
        result.Sort((a, b) => Distance(Point(a), endpoint).CompareTo(Distance(Point(b), endpoint)));
        return result;
    }

    public bool TryPath(Vector3 from, Vector3 target, Func<Vector3, Vector3, bool> bodyMove,
        out Vector3[] path)
    {
        path = null;
        if (bodyMove == null) bodyMove = (a, b) => true;
        if (!terrainMove(from, from) || !terrainMove(target, target) || !bodyMove(target, target)) return false;
        if (terrainMove(from, target) && bodyMove(from, target))
        { path = new[] { from, target }; return true; }
        List<int> starts = Connections(from, bodyMove, true);
        List<int> ends = Connections(target, bodyMove, false);
        if (starts.Count == 0 || ends.Count == 0) return false;
        var finish = new HashSet<int>(ends);
        var cost = new float[valid.Length];
        var parent = new int[valid.Length];
        var closed = new bool[valid.Length];
        for (int i = 0; i < cost.Length; i++) { cost[i] = float.PositiveInfinity; parent[i] = -1; }
        var open = new MinHeap();
        foreach (int start in starts)
        {
            cost[start] = Distance(from, Point(start));
            open.Push(start, cost[start] + Distance(Point(start), target));
        }
        int reached = -1;
        // Each node is closed once: this loop cannot search beyond the bounded grid.
        while (open.TryPop(out int current))
        {
            if (closed[current]) continue;
            closed[current] = true;
            if (finish.Contains(current)) { reached = current; break; }
            int cx = current % width, cz = current / width;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || x >= width || z < 0 || z >= height) continue;
                    int next = z * width + x;
                    if (closed[next] || !Valid(next) || !Edge(current, next) ||
                        !bodyMove(Point(current), Point(next))) continue;
                    float candidate = cost[current] + Distance(Point(current), Point(next));
                    if (candidate >= cost[next]) continue;
                    cost[next] = candidate; parent[next] = current;
                    open.Push(next, candidate + Distance(Point(next), target));
                }
        }
        if (reached < 0) return false;
        var raw = new List<Vector3> { target };
        for (int index = reached; index >= 0; index = parent[index]) raw.Add(Point(index));
        raw.Add(from); raw.Reverse();
        // Smooth whole validated segments, rather than changing a sidestep each frame.
        var smooth = new List<Vector3> { from };
        for (int current = 0; current < raw.Count - 1;)
        {
            int next = raw.Count - 1;
            while (next > current + 1 && (!terrainMove(raw[current], raw[next]) ||
                !bodyMove(raw[current], raw[next]))) next--;
            smooth.Add(raw[next]); current = next;
        }
        path = smooth.ToArray();
        return true;
    }

    private sealed class MinHeap
    {
        private readonly List<(int index, float priority)> entries = new List<(int, float)>();
        public void Push(int index, float priority)
        {
            entries.Add((index, priority));
            int child = entries.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (entries[parent].priority <= priority) break;
                entries[child] = entries[parent]; child = parent;
            }
            entries[child] = (index, priority);
        }
        public bool TryPop(out int index)
        {
            index = -1;
            if (entries.Count == 0) return false;
            index = entries[0].index;
            var tail = entries[entries.Count - 1]; entries.RemoveAt(entries.Count - 1);
            if (entries.Count == 0) return true;
            int parent = 0;
            while (parent * 2 + 1 < entries.Count)
            {
                int child = parent * 2 + 1;
                if (child + 1 < entries.Count && entries[child + 1].priority < entries[child].priority) child++;
                if (entries[child].priority >= tail.priority) break;
                entries[parent] = entries[child]; parent = child;
            }
            entries[parent] = tail;
            return true;
        }
    }
}
