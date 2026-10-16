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
        Goal = next[next.Length - 1];
        replanAt = now;
        BlockedSeconds = 0f;
        return true;
    }

    public void CancelRoute()
    {
        path = null;
        BlockedSeconds = 0f;
    }

    public void ResetNavigation()
    {
        CancelRoute();
        Goal = body.groundPosition;
        replanAt = 0f;
    }

    public void Tick(float dt, float now)
    {
        if (path == null || dt <= 0f) return;
        while (corner < path.Length && Vector2.Distance(
            PopulationCollisionMath.Flat(body.groundPosition),
            PopulationCollisionMath.Flat(path[corner])) < 0.015f) corner++;
        if (corner >= path.Length) { CancelRoute(); return; }

        Vector3 delta = path[corner] - body.groundPosition;
        delta.y = 0f;
        Vector3 next = body.groundPosition + delta.normalized *
            Mathf.Min(delta.magnitude, Mathf.Max(0f, settings.speed) * dt);
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
        if (apply(next, heading)) BlockedSeconds = 0f;
        else BlockedSeconds += dt;
    }
}
