#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

// Exercises the real generated terrain/obstacles without moving scene objects.
public static class PopulationTerrainRegressionChecks
{
    public static string LastResult { get; private set; }
    [MenuItem("Tools/Population/Check current terrain movement (Play Mode)")]
    public static void BeginChecks()
    {
        LastResult = "Running";
        EditorApplication.delayCall += () =>
        {
            try { LastResult = RunChecks(); }
            catch (Exception error)
            {
                LastResult = error.ToString();
                Debug.LogError(LastResult);
                return;
            }
            Debug.Log(LastResult);
        };
    }

    public static string RunChecks()
    {
        var controller = UnityEngine.Object.FindFirstObjectByType<PopulationMovementController>();
        if (!Application.isPlaying || controller == null || controller.Populations.Count == 0)
            throw new InvalidOperationException("Enter Play Mode and create a population first.");
        WhiteboxPopulation population = controller.Populations[0];
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var leader = (PopulationMotion.Body)typeof(WhiteboxPopulation)
            .GetField("leader", flags).GetValue(population);
        var tile = (MapTileInstance)typeof(WhiteboxPopulation).GetField("tile", flags).GetValue(population);
        var grid = controller.GetComponent<MapGridManager>();
        if (grid.Navigation.IsUpdating) throw new InvalidOperationException("Wait for NavMesh update.");
        var surface = grid.Navigation.GetComponent<NavMeshSurface>();
        var space = new PopulationTileSpace(tile, new NavMeshQueryFilter
            { agentTypeID = surface.agentTypeID, areaMask = NavMesh.AllAreas });
        // This grassland segment previously planned successfully but failed at
        // a 2 cm move due to nearest-point horizontal snapping on the relief.
        if (tile.Coordinate == Vector2Int.zero && tile.Biome == BiomeType.Grassland)
        {
            Vector3 from = new Vector3(-2.70f, 0f, 3.93f), to = new Vector3(-6.10f, 0f, -3.54f);
            CheckReliefSegment(space, from, to, 0.8f);
            // Adjacent frames captured from the initial chicken: 1 cm of route
            // progress formerly caused a 19 cm billboard jump at this seam.
            CheckIllustrationSeam(space, new Vector3(-5.998695f, 0f, -2.701514f),
                new Vector3(-6.006747f, 0f, -2.707874f), 0.8f);
            CheckIllustrationSeam(space, new Vector3(-2.288032f, 0f, 0.0009858876f),
                new Vector3(-2.297387f, 0f, -0.0003318191f), 0.8f);
        }
        return "Real terrain movement checks:\n" + Simulate(space, leader, controller.Settings);
    }

    private static void CheckReliefSegment(PopulationTileSpace space, Vector3 from, Vector3 to, float size)
    {
        // Other map layouts may intentionally block this captured segment.
        if (!space.CanTraverse(from, to, size)) return;
        Vector3 previous = from;
        for (int i = 0; i <= 200; i++)
        {
            Vector3 point = Vector3.Lerp(from, to, i / 200f);
            if (!space.TryPose(point, size, out _, out _) || !space.CanTraverse(previous, point, size))
                throw new InvalidOperationException("A planned relief segment contains an invalid movement step.");
            previous = point;
        }
    }

    private static string Simulate(PopulationTileSpace space, PopulationMotion.Body source,
        PopulationMovementSettings settings)
    {
        var body = new PopulationMotion.Body
            { groundPosition = source.groundPosition, heading = source.heading, size = source.size };
        bool Clear(Vector3 from, Vector3 to) => space.CanTraverse(from, to, body.size);
        bool Plan(Vector3 from, Vector3 to, out Vector3[] path) =>
            space.TryPath(from, to, body.size, out path);
        float maxVisualDrift = 0f;
        bool Apply(Vector3 position, Vector3 heading)
        {
            if (!space.TryRepresentativePose(position, heading, body.size,
                out Vector3 ground, out Vector3 center, out Quaternion rotation)) return false;
            Vector3 image = center + rotation * Vector3.up * (body.size * 0.25f);
            float drift = Vector2.Distance(PopulationCollisionMath.Flat(image), PopulationCollisionMath.Flat(ground));
            maxVisualDrift = Mathf.Max(maxVisualDrift, drift);
            if (drift > 0.0001f)
                throw new InvalidOperationException("Billboard drifted sideways from the walked route.");
            body.groundPosition = ground; body.heading = heading;
            return true;
        }
        var motion = new PopulationMotion(body, settings, Clear, Plan, Apply);
        var random = new System.Random(817);
        float distance = 0f, stall = 0f, longestStall = 0f;
        int destinations = 0, completed = 0;
        const float dt = 0.04f;
        for (int tick = 0; tick < 3000; tick++)
        {
            float now = tick * dt;
            if (!motion.HasRoute)
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    Bounds bounds = space.Bounds;
                    var target = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, (float)random.NextDouble()),
                        0f, Mathf.Lerp(bounds.min.z, bounds.max.z, (float)random.NextDouble()));
                    if (Vector2.Distance(PopulationCollisionMath.Flat(target),
                        PopulationCollisionMath.Flat(body.groundPosition)) < 1f ||
                        !motion.SetTarget(target, now)) continue;
                    destinations++; break;
                }
            bool hadRoute = motion.HasRoute;
            Vector3 previous = body.groundPosition;
            motion.Tick(dt, now);
            if (!space.CanTraverse(body.groundPosition, body.groundPosition, body.size))
                throw new InvalidOperationException("Representative crossed invalid terrain.");
            float moved = Vector2.Distance(PopulationCollisionMath.Flat(previous),
                PopulationCollisionMath.Flat(body.groundPosition));
            distance += moved;
            stall = hadRoute && moved < 0.0001f ? stall + dt : 0f;
            longestStall = Mathf.Max(longestStall, stall);
            if (hadRoute && !motion.HasRoute) completed++;
            if (motion.BlockedSeconds >= settings.blockedRetargetSeconds) motion.CancelRoute();
        }
        if (longestStall > 10f || distance < 10f)
            throw new InvalidOperationException("Representative stalled: longest=" + longestStall.ToString("F2") +
                "s, travel=" + distance.ToString("F2") + "m.");
        return "One representative / 120 simulated seconds: " + distance.ToString("F1") +
            "m, reached " + completed + "/" + destinations + " targets, longest stop " +
            longestStall.ToString("F2") + "s, max sideways visual drift " + maxVisualDrift.ToString("F6") + "m.";
    }

    private static void CheckIllustrationSeam(PopulationTileSpace space, Vector3 from, Vector3 to, float size)
    {
        if (!space.CanTraverse(from, to, size)) return; // Another biome may block this captured route.
        Vector3 heading = (to - from).normalized;
        if (!space.TryRepresentativePose(from, heading, size, out Vector3 groundA, out Vector3 a, out Quaternion rotationA) ||
            !space.TryRepresentativePose(to, heading, size, out Vector3 groundB, out Vector3 b, out Quaternion rotationB))
            throw new InvalidOperationException("Captured illustration seam has no valid standing pose.");
        a += rotationA * Vector3.up * (size * 0.25f);
        b += rotationB * Vector3.up * (size * 0.25f);
        Vector2 routeStep = PopulationCollisionMath.Flat(groundB - groundA);
        Vector2 imageStep = PopulationCollisionMath.Flat(b - a);
        if (Vector2.Distance(routeStep, imageStep) > 0.0001f ||
            Vector3.Distance(a, b) > Vector3.Distance(groundA, groundB) * 4f + 0.0001f)
            throw new InvalidOperationException("Billboard jumped when crossing a captured terrain triangle seam.");
    }
}
#endif
