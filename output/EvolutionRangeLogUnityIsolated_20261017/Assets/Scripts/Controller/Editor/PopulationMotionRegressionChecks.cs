#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class PopulationMotionRegressionChecks
{
    [MenuItem("Tools/Population/Run representative and player regression checks")]
    private static void RunFromMenu() => Debug.Log(RunChecks());

    public static string RunChecks()
    {
        var simulation = new Simulation();
        int routes = 0, finished = 0;
        for (int tick = 0; tick < 6000; tick++)
        {
            float now = tick * 0.04f;
            for (int group = 0; group < 2; group++)
            {
                PopulationMotion motion = simulation.motions[group];
                if (!motion.HasRoute)
                {
                    float sign = group == 0 ? -1f : 1f;
                    int leg = simulation.legs[group] % 4;
                    Vector3 target = leg == 0 ? V(sign * 4f, 5.5f) : leg == 1 ? V(sign * 1.5f, 5.5f) :
                        leg == 2 ? V(sign * 5.5f, -3f) : V(sign * 4f, 3f);
                    if (motion.SetTarget(target, now)) { routes++; simulation.legs[group]++; }
                }
                bool hadRoute = motion.HasRoute;
                motion.Tick(0.04f, now);
                if (hadRoute && !motion.HasRoute) finished++;
                if (motion.BlockedSeconds > 1.5f) motion.CancelRoute();
            }
            simulation.AssertMovement();
        }
        Require(finished >= 12, "Populations stopped finishing routes: " + finished);
        for (int group = 0; group < 2; group++)
            Require(simulation.travelled[group] > 25f, "A representative stopped moving: " + group);

        for (int group = 0; group < 2; group++) simulation.motions[group].CancelRoute();
        Vector3 stopped = simulation.bodies[0].groundPosition;
        for (int tick = 0; tick < 250; tick++)
            simulation.motions[0].Tick(0.04f, 240f + tick * 0.04f);
        Require(Distance(stopped, simulation.bodies[0].groundPosition) < 0.0001f,
            "A cancelled representative kept wandering.");

        // Insert a moving population into an already planned route. The production
        // driver must stop before contact and request a complete detour.
        var body = new PopulationMotion.Body { size = 0.8f, groundPosition = V(0f, -4f) };
        var blockers = new List<Vector3>();
        bool Clear(Vector3 from, Vector3 to)
        {
            foreach (Vector3 blocker in blockers)
                if (!PopulationCollisionMath.AllowsMove(PopulationCollisionMath.Flat(from),
                    PopulationCollisionMath.Flat(to), PopulationCollisionMath.Flat(blocker), 1.87f)) return false;
            return true;
        }
        var navigation = new PopulationLocalNavigation(new Bounds(Vector3.zero, V(17f, 17f)), 0.4f,
            (a, b) => Mathf.Abs(a.x) < 7.25f && Mathf.Abs(a.z) < 7.25f &&
                Mathf.Abs(b.x) < 7.25f && Mathf.Abs(b.z) < 7.25f);
        int replans = 0;
        bool Plan(Vector3 from, Vector3 to, out Vector3[] path)
        { replans++; return navigation.TryPath(from, to, Clear, out path); }
        var recovery = new PopulationMotion(body, new PopulationMovementSettings(), Clear, Plan,
            (next, heading) =>
            {
                Require(Clear(body.groundPosition, next), "A movement step crossed the new obstacle.");
                body.groundPosition = next; body.heading = heading; return true;
            });
        Require(recovery.SetTarget(V(0f, 4f), 0f), "Could not plan the initial clear route.");
        blockers.Add(Vector3.zero);
        for (int tick = 0; tick < 600 && recovery.HasRoute; tick++) recovery.Tick(0.04f, tick * 0.04f);
        Require(!recovery.HasRoute && Distance(body.groundPosition, V(0f, 4f)) < 0.02f && replans > 1,
            "A representative failed to replan around a newly arrived population.");

        Vector3 endpoint = body.groundPosition;
        Require(recovery.SetTarget(V(0f, -4f), 30f), "Could not request a return route.");
        recovery.Tick(0f, 30f);
        Require(Distance(endpoint, body.groundPosition) < 0.0001f, "Zero delta time moved the representative.");
        recovery.ResetNavigation();
        Require(!recovery.HasRoute && recovery.BlockedSeconds == 0f &&
            Distance(recovery.Goal, body.groundPosition) < 0.0001f,
            "A terrain revision retained a stale route or target.");

        // A player-sized disc must stop before crossing the single visible animal.
        Vector3 center = simulation.bodies[0].groundPosition;
        const float playerRadius = 0.9f;
        Vector3 start = center - Vector3.right * 4f, targetPlayer = center + Vector3.right;
        Require(simulation.PlayerMove(start, start, playerRadius), "Invalid player approach fixture.");
        Require(!simulation.PlayerMove(start, targetPlayer, playerRadius), "Player passed through the representative.");
        for (int trial = 0; trial < 100; trial++)
        {
            float lower = 0f, upper = 1f;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                float middle = (lower + upper) * 0.5f;
                if (simulation.PlayerMove(start, Vector3.Lerp(start, targetPlayer, middle), playerRadius)) lower = middle;
                else upper = middle;
            }
            Vector3 safe = Vector3.Lerp(start, targetPlayer, lower);
            Require(simulation.PlayerMove(start, safe, playerRadius) && Distance(safe, targetPlayer) > 0.5f,
                "Player clipping accepted an overlap.");
        }
        return $"Representative regression passed: 6000 simultaneous ticks, {finished}/{routes} completed routes, two moving representatives, pause/reset, dynamic obstacle replanning and 100 player crossing attempts.";
    }

    private static Vector3 V(float x, float z) => new Vector3(x, 0f, z);
    private static float Distance(Vector3 a, Vector3 b) => Vector2.Distance(
        PopulationCollisionMath.Flat(a), PopulationCollisionMath.Flat(b));
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    private sealed class Simulation
    {
        public readonly PopulationMotion.Body[] bodies = new PopulationMotion.Body[2];
        public readonly PopulationMotion[] motions = new PopulationMotion[2];
        public readonly int[] legs = new int[2];
        public readonly float[] travelled = new float[2];
        private readonly PopulationMovementSettings settings = new PopulationMovementSettings();

        public Simulation()
        {
            for (int group = 0; group < 2; group++)
            {
                bodies[group] = new PopulationMotion.Body
                { size = settings.leaderSize, groundPosition = V(group == 0 ? -4f : 4f, 3f) };
                int owner = group;
                var navigation = new PopulationLocalNavigation(new Bounds(Vector3.zero, V(17, 17)), 0.5f, TerrainMove);
                motions[group] = new PopulationMotion(bodies[group], settings,
                    (from, to) => TerrainMove(from, to) && BodyMove(owner, from, to),
                    (Vector3 from, Vector3 to, out Vector3[] path) =>
                        navigation.TryPath(from, to, (a, b) => BodyMove(owner, a, b), out path),
                    (next, heading) =>
                    {
                        travelled[owner] += Distance(bodies[owner].groundPosition, next);
                        bodies[owner].groundPosition = next; bodies[owner].heading = heading;
                        return true;
                    });
            }
        }

        private static bool TerrainMove(Vector3 from, Vector3 to) =>
            Mathf.Abs(from.x) < 7.25f && Mathf.Abs(from.z) < 7.25f &&
            Mathf.Abs(to.x) < 7.25f && Mathf.Abs(to.z) < 7.25f;
        private bool BodyMove(int owner, Vector3 from, Vector3 to)
        {
            int other = 1 - owner;
            float required = PopulationTileSpace.Clearance(bodies[owner].size) +
                PopulationTileSpace.Clearance(bodies[other].size) + settings.populationGap;
            return PopulationCollisionMath.AllowsMove(PopulationCollisionMath.Flat(from),
                PopulationCollisionMath.Flat(to), PopulationCollisionMath.Flat(bodies[other].groundPosition), required);
        }
        public bool PlayerMove(Vector3 from, Vector3 to, float radius)
        {
            foreach (PopulationMotion.Body body in bodies)
                if (!PopulationCollisionMath.AllowsMove(PopulationCollisionMath.Flat(from), PopulationCollisionMath.Flat(to),
                    PopulationCollisionMath.Flat(body.groundPosition),
                    radius + PopulationTileSpace.Clearance(body.size) + settings.playerGap)) return false;
            return true;
        }
        public void AssertMovement()
        {
            for (int group = 0; group < 2; group++)
            {
                Require(TerrainMove(bodies[group].groundPosition, bodies[group].groundPosition), "Representative crossed the tile.");
                Require(BodyMove(group, bodies[group].groundPosition, bodies[group].groundPosition), "Populations overlapped.");
            }
        }
    }
}
#endif
