using System;
using System.Collections.Generic;
using UnityEngine;

// The same formation driver is used in game and in the multi-body regression
// simulation. Followers have no independent wander/yield destinations.
public sealed class PopulationFormationMotion
{
    public class Body
    {
        public Vector3 groundPosition;
        public Vector3 heading = Vector3.forward;
        public float size;
        public Vector3[] path;
        public int corner;
        public float replanAt, blockedSeconds;
        public Vector3 goal;
        internal float retreatDistance;
    }
    public delegate bool Route(int index, Vector3 from, Vector3 target, out Vector3[] path);
    private readonly Body[] bodies;
    private readonly PopulationMovementSettings settings;
    private readonly Func<int, Vector3, Vector3, bool> clear;
    private readonly Func<int, Vector3, Vector3, bool> apply;
    private readonly Route route;
    private readonly Trail[] trails;
    private float leaderProgress;
    public bool WaitingForFollowers { get; private set; }
    public bool HasLeaderRoute => bodies[0].path != null;
    public float MaxRadius
    {
        get
        {
            float length = Mathf.Max(0.1f, settings.formationSlack);
            for (int i = 1; i < bodies.Length; i++) length += Spacing(i);
            return length + Mathf.Max(0.1f, settings.formationSlack);
        }
    }

    public PopulationFormationMotion(Body[] members, PopulationMovementSettings parameters,
        Func<int, Vector3, Vector3, bool> canMove, Route plan,
        Func<int, Vector3, Vector3, bool> applyPose)
    {
        bodies = members; settings = parameters; clear = canMove; route = plan; apply = applyPose;
        trails = new Trail[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            Vector3 tail = i + 1 < bodies.Length ? bodies[i + 1].groundPosition : bodies[i].groundPosition;
            trails[i] = new Trail(tail, bodies[i].groundPosition);
        }
    }

    public float Spacing(int index) => Mathf.Max(settings.spacing,
        PopulationTileSpace.Clearance(bodies[index].size) + PopulationTileSpace.Clearance(bodies[index - 1].size) +
        Mathf.Max(0f, settings.memberGap) + 0.08f);

    private static float Distance(Vector3 a, Vector3 b) => Vector2.Distance(
        PopulationCollisionMath.Flat(a), PopulationCollisionMath.Flat(b));

    public bool Allowed(int index, Vector3 from, Vector3 to)
    {
        // A recovery move may bring an old scattered member closer, but can never
        // send it farther away. Normal members cannot leave the group radius.
        if (index > 0 && Distance(to, bodies[0].groundPosition) >
            Mathf.Max(MaxRadius, Distance(from, bodies[0].groundPosition)) + 0.0001f) return false;
        if (index > 0 && Distance(to, bodies[index - 1].groundPosition) >
            Mathf.Max(Spacing(index) + Mathf.Max(0.1f, settings.formationSlack),
                Distance(from, bodies[index - 1].groundPosition)) + 0.0001f) return false;
        return clear(index, from, to);
    }

    public bool SetLeaderTarget(Vector3 target, float now)
    {
        if (!route(0, bodies[0].groundPosition, target, out Vector3[] path)) return false;
        SetPath(bodies[0], path); bodies[0].replanAt = now;
        bodies[0].blockedSeconds = 0f;
        return true;
    }
    public void CancelLeaderRoute() { bodies[0].path = null; WaitingForFollowers = false; }

    public void Tick(float dt, float now)
    {
        // Tail follows the actual path taken by its predecessor, including turns
        // and avoidance. Each predecessor records its successful move immediately.
        for (int i = 1; i < bodies.Length; i++)
        {
            Body body = bodies[i];
            if (HasLeaderRoute && bodies[0].blockedSeconds >= 0.3f && !WaitingForFollowers) continue;
            Vector3 target = trails[i - 1].Behind(Spacing(i));
            if (Distance(target, bodies[i - 1].groundPosition) < Spacing(i) - 0.001f)
            {
                Vector3 away = body.groundPosition - bodies[i - 1].groundPosition; away.y = 0f;
                target = bodies[i - 1].groundPosition + away.normalized * Spacing(i);
            }
            if (Distance(body.groundPosition, target) < 0.025f) { body.path = null; body.blockedSeconds = 0f; continue; }
            if (Allowed(i, body.groundPosition, target)) SetPath(body, new[] { body.groundPosition, target });
            else if (now >= body.replanAt)
            {
                body.replanAt = now + Mathf.Max(0.1f, settings.replanSeconds);
                if (route(i, body.groundPosition, target, out Vector3[] path)) SetPath(body, path);
            }
            if (body.path != null) Advance(i, settings.followerSpeed, dt, now);
        }
        WaitingForFollowers = false;
        for (int i = 1; i < bodies.Length; i++)
            if (Distance(bodies[i].groundPosition, bodies[i - 1].groundPosition) >
                Spacing(i) + Mathf.Max(0.1f, settings.formationSlack)) WaitingForFollowers = true;
        Body leader = bodies[0];
        if (!HasLeaderRoute || WaitingForFollowers) return;
        Vector3 previous = leader.groundPosition;
        Advance(0, settings.speed, dt, now);
        leaderProgress += Distance(previous, leader.groundPosition);
        if (leaderProgress > 0.6f)
        {
            leaderProgress = 0f;
            foreach (Body body in bodies) body.retreatDistance = 0f;
        }
        if (leader.blockedSeconds < 0.3f) return;
        // Bounded queue retreat, tail first. This can open an exit at a corner;
        // it never selects an unrelated point or grows into repeated wandering.
        for (int i = bodies.Length - 1; i > 0; i--)
        {
            Body body = bodies[i];
            if (body.retreatDistance >= 0.6f) continue;
            Vector3 away = body.groundPosition - bodies[i - 1].groundPosition; away.y = 0f;
            float step = Mathf.Min(0.6f - body.retreatDistance, Mathf.Max(0f, settings.followerSpeed) * dt);
            Vector3 next = body.groundPosition + away.normalized * step;
            if (!Allowed(i, body.groundPosition, next) || !Commit(i, next, away.normalized, dt)) continue;
            body.retreatDistance += step; body.path = null;
        }
    }

    private void Advance(int index, float speed, float dt, float now)
    {
        Body body = bodies[index];
        if (body.path == null) return;
        while (body.corner < body.path.Length && Distance(body.groundPosition, body.path[body.corner]) < 0.015f) body.corner++;
        if (body.corner >= body.path.Length) { body.path = null; body.blockedSeconds = 0f; return; }
        Vector3 delta = body.path[body.corner] - body.groundPosition; delta.y = 0f;
        Vector3 next = body.groundPosition + delta.normalized * Mathf.Min(delta.magnitude, Mathf.Max(0f, speed) * dt);
        // Check proposed leader position as well, preventing it from leaving a
        // follower behind between the cohesion check and the actual move.
        if (index + 1 < bodies.Length && Distance(next, bodies[index + 1].groundPosition) >
            Spacing(index + 1) + Mathf.Max(0.1f, settings.formationSlack))
        { if (index == 0) WaitingForFollowers = true; return; }
        if (!Allowed(index, body.groundPosition, next))
        {
            body.blockedSeconds += dt;
            if (now >= body.replanAt)
            {
                body.replanAt = now + Mathf.Max(0.1f, settings.replanSeconds);
                if (route(index, body.groundPosition, body.goal, out Vector3[] path))
                { body.path = path; body.corner = 1; }
            }
            return;
        }
        if (Commit(index, next, delta.normalized, dt)) body.blockedSeconds = 0f;
        else body.blockedSeconds += dt;
    }

    private bool Commit(int index, Vector3 next, Vector3 direction, float dt)
    {
        Vector3 heading = PopulationCollisionMath.SmoothHeading(bodies[index].heading, direction,
            1f - Mathf.Exp(-Mathf.Max(0.1f, settings.turnSpeed) * dt));
        if (!apply(index, next, heading)) return false;
        trails[index].Record(bodies[index].groundPosition, MaxRadius + 2f);
        return true;
    }
    private static void SetPath(Body body, Vector3[] path)
    { body.path = path; body.corner = 1; body.goal = path[path.Length - 1]; }

    private sealed class Trail
    {
        private readonly List<Vector3> points = new List<Vector3>();
        private float length;
        public Trail(Vector3 tail, Vector3 head) { points.Add(tail); points.Add(head); length = Distance(tail, head); }
        public void Record(Vector3 position, float keepLength)
        {
            float added = Distance(points[points.Count - 1], position);
            if (added < 0.00001f) return;
            points.Add(position); length += added;
            while (points.Count > 2 && length - Distance(points[0], points[1]) > keepLength)
            { length -= Distance(points[0], points[1]); points.RemoveAt(0); }
        }
        public Vector3 Behind(float distance)
        {
            float minimum = distance;
            Vector3 head = points[points.Count - 1];
            for (int i = points.Count - 1; i > 0; i--)
            {
                float segment = Distance(points[i], points[i - 1]);
                if (segment >= distance && segment > 0f)
                {
                    Vector3 candidate = Vector3.Lerp(points[i], points[i - 1], distance / segment);
                    if (Distance(candidate, head) >= minimum - 0.0001f) return candidate;
                    // A U-turn/short retreat folds the trail back over itself.
                    // Find the older circle exit instead of targeting an occupied
                    // point immediately beside the predecessor.
                    for (int older = i - 1; older >= 0; older--)
                    {
                        Vector3 end = points[older];
                        if (Distance(end, head) >= minimum)
                        {
                            Vector2 offset = PopulationCollisionMath.Flat(candidate - head);
                            Vector2 delta = PopulationCollisionMath.Flat(end - candidate);
                            float a = delta.sqrMagnitude;
                            float b = 2f * Vector2.Dot(offset, delta);
                            float c = offset.sqrMagnitude - minimum * minimum;
                            float t = a < 0.000001f ? 1f : (-b + Mathf.Sqrt(Mathf.Max(0f, b * b - 4f * a * c))) / (2f * a);
                            return Vector3.Lerp(candidate, end, Mathf.Clamp01(t));
                        }
                        candidate = end;
                    }
                    return points[0];
                }
                distance -= segment;
            }
            return points[0];
        }
    }
}
