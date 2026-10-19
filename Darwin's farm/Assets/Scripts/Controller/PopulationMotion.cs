using System;
using UnityEngine;

// One moving representative per population. Ecology owns the actual animal count.
public sealed class PopulationMotion
{
    public class Body
    {
        public Vector3 groundPosition;
        public Vector3 heading = Vector3.forward;
        public float size;
    }

    public delegate bool Route(Vector3 from, Vector3 target, out Vector3[] path);
    private readonly Body body;
    private readonly PopulationMovementSettings settings;
    private readonly Func<Vector3, Vector3, bool> clear;
    private readonly Route route;
    private readonly Func<Vector3, Vector3, bool> apply;
    private Vector3[] path;
    private int corner;
    private float replanAt;
    public bool HasRoute => path != null;
    public Vector3 Goal { get; private set; }
    public float BlockedSeconds { get; private set; }
    public float RouteLength { get; private set; }
    public float LastMovedDistance { get; private set; }

    public PopulationMotion(Body representative, PopulationMovementSettings parameters,
        Func<Vector3, Vector3, bool> canMove, Route plan,
        Func<Vector3, Vector3, bool> applyPose)
    {
        body = representative;
        settings = parameters;
        clear = canMove;
        route = plan;
        apply = applyPose;
        Goal = body.groundPosition;
    }

    public bool SetTarget(Vector3 target, float now)
    {
        if (!route(body.groundPosition, target, out Vector3[] next) ||
            next == null || next.Length < 2) return false;
        path = next;
        corner = 1;
        RouteLength = 0f;
        Vector3 previous = body.groundPosition;
        for (int i = 1; i < next.Length; i++)
        {
            RouteLength += Vector2.Distance(PopulationCollisionMath.Flat(previous),
                PopulationCollisionMath.Flat(next[i]));
            previous = next[i];
        }
        Goal = next[next.Length - 1];
        replanAt = now;
        BlockedSeconds = 0f;
        return true;
    }

    public void CancelRoute()
    {
        path = null;
        BlockedSeconds = 0f;
        RouteLength = 0f;
    }

    public void ResetNavigation()
    {
        CancelRoute();
        Goal = body.groundPosition;
        replanAt = 0f;
    }

    public Vector3 PositionAfter(float distance)
    {
        Vector3 position = body.groundPosition;
        if (path == null) return position;
        for (int i = corner; i < path.Length && distance > 0f; i++)
        {
            Vector3 delta = path[i] - position;
            delta.y = 0f;
            float length = delta.magnitude;
            if (length >= distance) return position + delta.normalized * distance;
            position += delta;
            distance -= length;
        }
        return position;
    }

    public void Tick(float dt, float now) => Tick(dt, now, settings.speed);

    public void Tick(float dt, float now, float speed)
    {
        LastMovedDistance = 0f;
        if (path == null || dt <= 0f) return;
        float distanceLeft = Mathf.Max(0f, speed) * dt;
        while (distanceLeft > 0f && path != null)
        {
            while (corner < path.Length && Vector2.Distance(
                PopulationCollisionMath.Flat(body.groundPosition),
                PopulationCollisionMath.Flat(path[corner])) < 0.0001f) corner++;
            if (corner >= path.Length) { CancelRoute(); return; }
            Vector3 delta = path[corner] - body.groundPosition;
            delta.y = 0f;
            float step = Mathf.Min(delta.magnitude, distanceLeft);
            Vector3 next = body.groundPosition + delta.normalized * step;
            if (!clear(body.groundPosition, next))
            {
                BlockedSeconds += dt;
                if (now >= replanAt)
                {
                    replanAt = now + Mathf.Max(0.1f, settings.replanSeconds);
                    if (route(body.groundPosition, Goal, out Vector3[] replacement) &&
                        replacement != null && replacement.Length >= 2)
                    { path = replacement; corner = 1; }
                }
                return;
            }
            Vector3 heading = PopulationCollisionMath.SmoothHeading(body.heading, delta.normalized,
                1f - Mathf.Exp(-Mathf.Max(0.1f, settings.turnSpeed) * dt));
            if (!apply(next, heading)) { BlockedSeconds += dt; return; }
            BlockedSeconds = 0f;
            LastMovedDistance += step;
            distanceLeft -= step;
            if (step >= delta.magnitude - 0.0001f) corner++;
            if (corner >= path.Length) CancelRoute();
        }
    }
}
