#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Reproducible algorithm checks: no scene objects, assets or configuration writes.
public static class PopulationMovementRegressionChecks
{
    [MenuItem("Tools/Population/Run movement regression checks")]
    private static void RunFromMenu() => Debug.Log(RunChecks());

    public static string RunChecks()
    {
        int passed = 0;
        Require(WhiteboxPopulation.VisibleMemberCount(0) == 1 &&
            WhiteboxPopulation.VisibleMemberCount(100) == 1 &&
            WhiteboxPopulation.VisibleMemberCount(101) == 2 &&
            WhiteboxPopulation.VisibleMemberCount(500) == 2 &&
            WhiteboxPopulation.VisibleMemberCount(501) == 3 &&
            WhiteboxPopulation.VisibleMemberCount(1000) == 3 &&
            WhiteboxPopulation.VisibleMemberCount(1001) == 4,
            "Population display count thresholds changed unexpectedly.");
        passed++;
        Vector3 heading = Vector3.forward;
        float previousYaw = 0f;
        for (int i = 0; i < 100; i++)
        {
            heading = PopulationCollisionMath.SmoothHeading(heading, Vector3.back, 0.12f);
            float yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
            Require(Mathf.Abs(heading.y) < 0.00001f && Mathf.Abs(heading.magnitude - 1f) < 0.0001f,
                "180-degree heading tilted or collapsed.");
            Require(Mathf.DeltaAngle(previousYaw, yaw) >= -0.0001f, "180-degree heading oscillated.");
            previousYaw = yaw;
        }
        Require(Vector3.Distance(heading, Vector3.back) < 0.001f, "180-degree turn did not converge.");
        passed++;

        var concave = new Region(new[] { V(0, 0), V(8, 0), V(8, 3), V(3, 3), V(3, 8), V(0, 8) }, 0.35f);
        var concaveGrid = new PopulationLocalNavigation(new Bounds(V(4, 4), V(8, 8)), 0.4f, concave.Move);
        Require(!concave.Move(V(1, 6.5f), V(6.5f, 1)), "Concave shortcut was legal in the fixture.");
        Require(concaveGrid.TryPath(V(1, 6.5f), V(6.5f, 1), null, out Vector3[] concavePath),
            "No route around a concave corner.");
        CheckPath(concavePath, concave.Move, (a, b) => true);
        Require(concavePath.Length > 2, "Concave route shortcut crossed the missing region.");
        passed++;

        Region tile = Square(10f, PopulationTileSpace.Clearance(0.8f));
        var grid = new PopulationLocalNavigation(new Bounds(Vector3.zero, V(20, 20)), 0.4f, tile.Move);
        var followers = new List<Vector3> { V(0, -2), V(0, -4), V(0, -6) };
        Func<Vector3, Vector3, bool> bodies = (a, b) => BodiesMove(a, b, followers, 1.87f);
        Require(!bodies(V(0, 0), V(0, -8)), "Reverse fixture did not block the old direct path.");
        Require(grid.TryPath(V(0, 0), V(0, -8), bodies, out Vector3[] reversePath),
            "No complete reverse route around three followers.");
        CheckPath(reversePath, tile.Move, bodies);
        FollowPath(reversePath, tile.Move, bodies);
        passed++;

        // Re-use the static cache after the dynamic formation has moved.
        followers.Clear(); followers.Add(V(3, -4));
        Require(grid.TryPath(V(0, 0), V(0, -8), bodies, out Vector3[] movedPath), "Dynamic cache retained an old blockage.");
        CheckPath(movedPath, tile.Move, bodies);
        Require(movedPath.Length == 2, "Clear reverse route retained obsolete sidesteps.");
        passed++;

        // A player click across a population needs a complete detour, then a
        // straight route again once the moving population clears the crossing.
        var playerBlockers = new List<Vector3> { V(0f, 0f), V(0f, 1.2f) };
        Func<Vector3, Vector3, bool> playerMove = (a, b) => BodiesMove(a, b, playerBlockers, 1.5f);
        Vector3 playerStart = V(-5f, 0f), playerEnd = V(5f, 0f);
        Require(!playerMove(playerStart, playerEnd), "Player crossing fixture did not block the direct route.");
        Require(grid.TryPath(playerStart, playerEnd, playerMove, out Vector3[] playerDetour),
            "Player could not route around a population.");
        CheckPath(playerDetour, tile.Move, playerMove);
        Require(playerDetour.Length > 2, "Player detour still crossed the population.");
        playerBlockers.Clear();
        Require(grid.TryPath(playerStart, playerEnd, playerMove, out Vector3[] playerClearPath) &&
            playerClearPath.Length == 2, "Player route did not recover after the population moved.");
        passed++;

        Region corner = Square(8f, PopulationTileSpace.Clearance(0.8f));
        var cornerGrid = new PopulationLocalNavigation(new Bounds(Vector3.zero, V(16, 16)), 0.4f, corner.Move);
        var blockers = new List<Vector3> { V(6, 3.8f), V(3.8f, 6) };
        Func<Vector3, Vector3, bool> cornerBodies = (a, b) => BodiesMove(a, b, blockers, 1.87f);
        Require(!cornerGrid.TryPath(V(6, 6), V(0, 0), cornerBodies, out _),
            "Corner trap was crossed instead of waiting for a yield.");
        passed++;
        Require(PopulationCollisionMath.AllowsMove(F(blockers[0]), F(V(4.5f, 2.3f)), F(V(6, 6)), 1.87f) &&
            PopulationCollisionMath.AllowsMove(F(blockers[1]), F(V(2.3f, 4.5f)), F(V(6, 6)), 1.87f),
            "Yield positions crossed the stationary leader.");
        blockers[0] = V(4.5f, 2.3f); blockers[1] = V(2.3f, 4.5f);
        Require(cornerGrid.TryPath(V(6, 6), V(0, 0), cornerBodies, out Vector3[] escape),
            "Corner failed to recover after followers yielded.");
        CheckPath(escape, corner.Move, cornerBodies); FollowPath(escape, corner.Move, cornerBodies);
        passed++;

        Require(cornerGrid.TryPath(V(-6.7f, 6.7f), V(0, 0), (a, b) => true, out Vector3[] inward),
            "Valid boundary position could not reach the interior.");
        CheckPath(inward, corner.Move, (a, b) => true); FollowPath(inward, corner.Move, (a, b) => true);
        passed++;

        Region tiny = Square(0.5f, 0.6f);
        var tinyGrid = new PopulationLocalNavigation(new Bounds(Vector3.zero, V(1, 1)), 0.4f, tiny.Move);
        Require(!tinyGrid.TryPath(Vector3.zero, V(0.1f, 0), null, out _), "No-space case did not stop safely.");
        passed++;

        // Boundary-to-boundary routes with a changing obstacle in the same cache.
        var random = new System.Random(1729);
        for (int i = 0; i < 32; i++)
        {
            blockers.Clear(); blockers.Add(V((float)random.NextDouble() * 4f - 2f, (float)random.NextDouble() * 4f - 2f));
            Require(cornerGrid.TryPath(V(-6.6f, 6.6f), V(6.6f, -6.6f), cornerBodies, out Vector3[] route),
                "Boundary route failed for obstacle scenario " + i);
            CheckPath(route, corner.Move, cornerBodies); FollowPath(route, corner.Move, cornerBodies);
        }
        passed++;
        return $"Population movement regression checks: {passed}/11 passed (including display thresholds and 32 changing-obstacle routes).";
    }

    private static Vector3 V(float x, float z) => new Vector3(x, 0f, z);
    private static Vector2 F(Vector3 position) => PopulationCollisionMath.Flat(position);
    private static Region Square(float half, float clearance) => new Region(
        new[] { V(-half, -half), V(half, -half), V(half, half), V(-half, half) }, clearance);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static bool BodiesMove(Vector3 from, Vector3 to, List<Vector3> bodies, float radius)
    {
        foreach (Vector3 body in bodies)
            if (!PopulationCollisionMath.AllowsMove(F(from), F(to), F(body), radius)) return false;
        return true;
    }

    private static void CheckPath(Vector3[] path, Func<Vector3, Vector3, bool> terrain,
        Func<Vector3, Vector3, bool> bodies)
    {
        Require(path != null && path.Length >= 2, "Empty route.");
        for (int i = 1; i < path.Length; i++)
            Require(terrain(path[i - 1], path[i]) && bodies(path[i - 1], path[i]),
                "Route crossed a boundary or member.");
    }

    private static void FollowPath(Vector3[] path, Func<Vector3, Vector3, bool> terrain,
        Func<Vector3, Vector3, bool> bodies)
    {
        Vector3 position = path[0];
        int corner = 1;
        for (int tick = 0; tick < 2000 && corner < path.Length; tick++)
        {
            Vector3 delta = path[corner] - position;
            Vector3 next = position + delta.normalized * Mathf.Min(delta.magnitude, 1.8f * 0.04f);
            Require(terrain(position, next) && bodies(position, next), "Following a planned route got stuck/crossed a body.");
            position = next;
            if ((position - path[corner]).sqrMagnitude < 0.000001f) corner++;
        }
        Require(corner == path.Length, "Planned route did not finish within the step budget.");
    }

    private sealed class Region
    {
        private readonly Vector3[] polygon;
        private readonly float clearance;
        public Region(Vector3[] vertices, float radius) { polygon = vertices; clearance = radius; }
        private bool Contains(Vector3 position)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector3 a = polygon[i], b = polygon[j];
                if ((a.z > position.z) != (b.z > position.z) &&
                    position.x < (b.x - a.x) * (position.z - a.z) / (b.z - a.z) + a.x) inside = !inside;
            }
            return inside;
        }
        public bool Move(Vector3 from, Vector3 to)
        {
            if (!Contains(from) || !Contains(to)) return false;
            for (int i = 0; i < polygon.Length; i++)
                if (PopulationTileSpace.SegmentDistanceSquared(F(from), F(to), F(polygon[i]), F(polygon[(i + 1) % polygon.Length]))
                    <= clearance * clearance) return false;
            return true;
        }
    }
}
#endif
