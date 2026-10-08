#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class PopulationFormationRegressionChecks
{
    [MenuItem("Tools/Population/Run formation and player regression checks")]
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
                PopulationFormationMotion motion = simulation.motions[group];
                if (!motion.HasLeaderRoute)
                {
                    float sign = group == 0 ? -1f : 1f;
                    int leg = simulation.legs[group] % 4;
                    Vector3 target = leg == 0 ? V(sign * 4f, 5.5f) : leg == 1 ? V(sign * 1f, 5.5f) :
                        leg == 2 ? V(sign * 5.5f, -3f) : V(sign * 4f, 3f);
                    if (motion.SetLeaderTarget(target, now)) { routes++; simulation.legs[group]++; }
                }
                bool following = motion.HasLeaderRoute;
                motion.Tick(0.04f, now);
                if (following && !motion.HasLeaderRoute) finished++;
                if (simulation.bodies[group][0].blockedSeconds > 1.5f) motion.CancelLeaderRoute();
            }
            simulation.AssertFormation();
        }
        string state = "";
        for (int group = 0; group < 2; group++)
        {
            state += $" group {group} wait={simulation.motions[group].WaitingForFollowers} route={simulation.motions[group].HasLeaderRoute}";
            foreach (PopulationFormationMotion.Body body in simulation.bodies[group])
                state += $" [{body.groundPosition.x:F2},{body.groundPosition.z:F2} goal={body.goal.x:F2},{body.goal.z:F2} blocked={body.blockedSeconds:F2}]";
        }
        Require(finished >= 12, "Two populations stopped finishing routes: " + finished + state);
        for (int group = 0; group < 2; group++)
            for (int member = 0; member < 4; member++)
                Require(simulation.travelled[group, member] > 25f, "A member stopped following: " + group + "/" + member);

        // Exercise the production driver's catch-up and stop behavior as well.
        for (int group = 0; group < 2; group++) simulation.motions[group].CancelLeaderRoute();
        Vector3 firstLeader = simulation.bodies[0][0].groundPosition;
        for (int tick = 0; tick < 250; tick++)
        {
            for (int group = 0; group < 2; group++) simulation.motions[group].Tick(0.04f, 240f + tick * 0.04f);
            simulation.AssertFormation();
        }
        Require(Distance(firstLeader, simulation.bodies[0][0].groundPosition) < 0.0001f, "Paused leader kept wandering.");
        for (int group = 0; group < 2; group++)
            for (int member = 1; member < 4; member++)
                Require(Distance(simulation.bodies[group][member].groundPosition, simulation.bodies[group][member - 1].groundPosition) <=
                    simulation.motions[group].Spacing(member) + 0.2f, "Followers did not regroup when the leader stopped.");

        // Drive a player-sized disc repeatedly through the settled first group.
        Vector3 center = simulation.bodies[0][0].groundPosition;
        Vector3 from = center, targetPlayer = center;
        const float playerRadius = 0.9f;
        bool playerStartFound = false;
        for (int ray = 0; ray < 16; ray++)
        {
            float yaw = ray * Mathf.PI * 0.125f;
            Vector3 direction = V(Mathf.Sin(yaw), Mathf.Cos(yaw));
            Vector3 start = center + direction * 3f;
            Vector3 end = center - direction;
            if (Mathf.Abs(start.x) > 7.25f || Mathf.Abs(start.z) > 7.25f ||
                Mathf.Abs(end.x) > 7.25f || Mathf.Abs(end.z) > 7.25f ||
                !simulation.PlayerMove(start, start, playerRadius)) continue;
            from = start; targetPlayer = end; playerStartFound = true; break;
        }
        Require(playerStartFound, "No valid player approach in the fixture.");
        int blocked = 0;
        for (int trial = 0; trial < 100; trial++)
        {
            float lower = 0f, upper = 1f;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                float middle = (lower + upper) * 0.5f;
                Vector3 proposed = Vector3.Lerp(from, targetPlayer, middle);
                if (simulation.PlayerMove(from, proposed, playerRadius)) lower = middle; else upper = middle;
            }
            Vector3 stopped = Vector3.Lerp(from, targetPlayer, lower);
            Require(simulation.PlayerMove(from, stopped, playerRadius), "Player clipping accepted an overlap.");
            Require(Distance(stopped, targetPlayer) > 0.5f, "Player passed through the formation.");
            blocked++;
        }
        return $"Formation regression passed: 6000 simultaneous ticks, {finished}/{routes} completed routes, all eight members moving, regrouping and {blocked} player crossing attempts.";
    }

    private static Vector3 V(float x, float z) => new Vector3(x, 0f, z);
    private static float Distance(Vector3 a, Vector3 b) => Vector2.Distance(
        PopulationCollisionMath.Flat(a), PopulationCollisionMath.Flat(b));
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    private sealed class Simulation
    {
        public readonly PopulationFormationMotion.Body[][] bodies = new PopulationFormationMotion.Body[2][];
        public readonly PopulationFormationMotion[] motions = new PopulationFormationMotion[2];
        public readonly int[] legs = new int[2];
        public readonly float[,] travelled = new float[2, 4];
        private readonly PopulationMovementSettings settings = new PopulationMovementSettings();
        private readonly PopulationLocalNavigation[] navigation = new PopulationLocalNavigation[2];

        public Simulation()
        {
            for (int group = 0; group < 2; group++)
            {
                bodies[group] = new PopulationFormationMotion.Body[4];
                float z = 3f;
                for (int member = 0; member < 4; member++)
                {
                    float size = member == 0 ? settings.leaderSize : settings.followerSize;
                    if (member > 0) z -= Mathf.Max(settings.spacing, PopulationTileSpace.Clearance(size) +
                        PopulationTileSpace.Clearance(bodies[group][member - 1].size) + settings.memberGap + 0.08f);
                    bodies[group][member] = new PopulationFormationMotion.Body { size = size, groundPosition = V(group == 0 ? -4 : 4, z) };
                }
                int owner = group;
                navigation[group] = new PopulationLocalNavigation(new Bounds(Vector3.zero, V(17, 17)), 0.5f, TerrainMove);
                motions[group] = new PopulationFormationMotion(bodies[group], settings,
                    (index, from, to) => TerrainMove(from, to) && BodyMove(owner, index, from, to),
                    (int index, Vector3 from, Vector3 to, out Vector3[] path) => navigation[owner].TryPath(from, to,
                        (a, b) => motions[owner].Allowed(index, a, b), out path),
                    (index, next, heading) =>
                    {
                        travelled[owner, index] += Distance(bodies[owner][index].groundPosition, next);
                        bodies[owner][index].groundPosition = next; bodies[owner][index].heading = heading;
                        return true;
                    });
            }
        }

        private static bool TerrainMove(Vector3 from, Vector3 to) =>
            Mathf.Abs(from.x) < 7.25f && Mathf.Abs(from.z) < 7.25f && Mathf.Abs(to.x) < 7.25f && Mathf.Abs(to.z) < 7.25f;
        private bool BodyMove(int owner, int index, Vector3 from, Vector3 to)
        {
            for (int group = 0; group < 2; group++)
                for (int member = 0; member < 4; member++)
                {
                    if (group == owner && member == index) continue;
                    float required = PopulationTileSpace.Clearance(bodies[owner][index].size) +
                        PopulationTileSpace.Clearance(bodies[group][member].size) + (group == owner ? settings.memberGap : settings.populationGap);
                    if (!PopulationCollisionMath.AllowsMove(PopulationCollisionMath.Flat(from), PopulationCollisionMath.Flat(to),
                        PopulationCollisionMath.Flat(bodies[group][member].groundPosition), required)) return false;
                }
            return true;
        }
        public bool PlayerMove(Vector3 from, Vector3 to, float radius)
        {
            foreach (PopulationFormationMotion.Body[] group in bodies)
                foreach (PopulationFormationMotion.Body member in group)
                    if (!PopulationCollisionMath.AllowsMove(PopulationCollisionMath.Flat(from), PopulationCollisionMath.Flat(to),
                        PopulationCollisionMath.Flat(member.groundPosition), radius + PopulationTileSpace.Clearance(member.size) + settings.playerGap)) return false;
            return true;
        }
        public void AssertFormation()
        {
            for (int group = 0; group < 2; group++)
                for (int member = 0; member < 4; member++)
                {
                    Require(TerrainMove(bodies[group][member].groundPosition, bodies[group][member].groundPosition), "Member crossed the tile.");
                    Require(BodyMove(group, member, bodies[group][member].groundPosition, bodies[group][member].groundPosition), "Members overlapped.");
                    if (member == 0) continue;
                    Require(Distance(bodies[group][member].groundPosition, bodies[group][member - 1].groundPosition) <=
                        motions[group].Spacing(member) + settings.formationSlack + 0.001f, "Follower scattered from its predecessor.");
                    Require(Distance(bodies[group][member].groundPosition, bodies[group][0].groundPosition) <=
                        motions[group].MaxRadius + 0.001f, "Follower left the group radius.");
                }
        }
    }
}
#endif
