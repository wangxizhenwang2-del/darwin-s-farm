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
        var members = (PopulationFormationMotion.Body[])typeof(WhiteboxPopulation)
            .GetField("members", flags).GetValue(population);
        var currentMotion = (PopulationFormationMotion)typeof(WhiteboxPopulation)
            .GetField("motion", flags).GetValue(population);
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
            if (space.CanTraverse(from, to, 0.8f))
                for (int i = 0; i <= 200; i++)
                    if (!space.TryPose(Vector3.Lerp(from, to, i / 200f), 0.8f, out _, out _))
                        throw new InvalidOperationException("A planned segment contains an invalid terrain pose.");
        }
        string result = "Real terrain movement checks:";
        for (int count = 3; count <= 4; count++)
        {
            bool available = currentMotion != null && currentMotion.ActiveCount >= count;
            for (int i = 0; i < count; i++)
                available &= members[i].size > 0f && space.CanTraverse(members[i].groundPosition,
                    members[i].groundPosition, members[i].size);
            if (!available) { result += "\n" + count + " members: activate these members before testing."; continue; }
            result += "\n" + Simulate(space, members, count, controller.Settings);
        }
        return result;
    }

    private static string Simulate(PopulationTileSpace space, PopulationFormationMotion.Body[] source,
        int count, PopulationMovementSettings settings)
    {
        var bodies = new PopulationFormationMotion.Body[count];
        for (int i = 0; i < count; i++) bodies[i] = new PopulationFormationMotion.Body
            { groundPosition = source[i].groundPosition, heading = source[i].heading, size = source[i].size };
        PopulationFormationMotion motion = null;
        bool Clear(int index, Vector3 from, Vector3 to)
        {
            if (!space.CanTraverse(from, to, bodies[index].size)) return false;
            for (int i = 0; i < count; i++)
            {
                if (i == index) continue;
                float required = PopulationTileSpace.Clearance(bodies[index].size) +
                    PopulationTileSpace.Clearance(bodies[i].size) + settings.memberGap;
                if (!PopulationCollisionMath.AllowsMove(PopulationCollisionMath.Flat(from),
                    PopulationCollisionMath.Flat(to), PopulationCollisionMath.Flat(bodies[i].groundPosition), required))
                    return false;
            }
            return true;
        }
        bool Plan(int index, Vector3 from, Vector3 to, out Vector3[] path) =>
            space.TryPath(from, to, bodies[index].size, out path, (a, b) => motion.Allowed(index, a, b));
        bool Apply(int index, Vector3 position, Vector3 heading)
        {
            if (!space.TryPose(position, bodies[index].size, out _, out _) ||
                !space.TryGround(position, out RaycastHit ground)) return false;
            bodies[index].groundPosition = ground.point; bodies[index].heading = heading;
            return true;
        }
        motion = new PopulationFormationMotion(bodies, settings, Clear, Plan, Apply);
        var random = new System.Random(817);
        float distance = 0f, stall = 0f, longestStall = 0f;
        int destinations = 0, completed = 0;
        const float dt = 0.04f;
        for (int tick = 0; tick < 3000; tick++)
        {
            float now = tick * dt;
            if (!motion.HasLeaderRoute)
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    Bounds bounds = space.Bounds;
                    var target = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, (float)random.NextDouble()),
                        0f, Mathf.Lerp(bounds.min.z, bounds.max.z, (float)random.NextDouble()));
                    if (Vector2.Distance(PopulationCollisionMath.Flat(target),
                        PopulationCollisionMath.Flat(bodies[0].groundPosition)) < 1f ||
                        !motion.SetLeaderTarget(target, now)) continue;
                    destinations++; break;
                }
            bool hadRoute = motion.HasLeaderRoute;
            Vector3 previous = bodies[0].groundPosition;
            motion.Tick(dt, now);
            float moved = Vector2.Distance(PopulationCollisionMath.Flat(previous),
                PopulationCollisionMath.Flat(bodies[0].groundPosition));
            distance += moved;
            stall = hadRoute && moved < 0.0001f ? stall + dt : 0f;
            longestStall = Mathf.Max(longestStall, stall);
            if (hadRoute && !motion.HasLeaderRoute) completed++;
            if (!motion.WaitingForFollowers && bodies[0].blockedSeconds >= settings.blockedRetargetSeconds)
                motion.CancelLeaderRoute();
        }
        if (longestStall > 10f || distance < 10f)
            throw new InvalidOperationException(count + " members stalled: longest=" + longestStall.ToString("F2") +
                "s, travel=" + distance.ToString("F2") + "m.");
        return count + " members / 120 simulated seconds: " + distance.ToString("F1") +
            "m, reached " + completed + "/" + destinations + " targets, longest stop " +
            longestStall.ToString("F2") + "s.";
    }
}
#endif
