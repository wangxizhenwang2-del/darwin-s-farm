using UnityEngine;

// Shared by player movement and all four population members. Sweeping a disc
// includes rotation clearance, so even a complete reversal cannot cut through it.
public static class PopulationCollisionMath
{
    public static bool AllowsMove(Vector2 from, Vector2 to, Vector2 obstacle,
        float requiredDistance)
    {
        float radiusSquared = requiredDistance * requiredDistance;
        Vector2 offset = from - obstacle;
        if (offset.sqrMagnitude < radiusSquared)
        {
            // Recover from an external teleport/old overlap only by moving away.
            // Squared distance along the segment must be monotonically increasing.
            Vector2 delta = to - from;
            return Vector2.Dot(offset, delta) >= 0f &&
                (to - obstacle).sqrMagnitude > offset.sqrMagnitude + 0.000001f;
        }
        return PopulationTileSpace.SegmentDistanceSquared(from, to, obstacle, obstacle) >= radiusSquared;
    }

    public static Vector2 Flat(Vector3 position) => new Vector2(position.x, position.z);

    public static Vector3 SmoothHeading(Vector3 from, Vector3 to, float blend)
    {
        // Antipodal Vector3.Slerp can select a vertical rotation axis. Interpolate
        // planar yaw explicitly so a 180-degree turn has a stable, readable side.
        float current = Mathf.Atan2(from.x, from.z) * Mathf.Rad2Deg;
        float target = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float yaw = Mathf.LerpAngle(current, target, Mathf.Clamp01(blend)) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
    }
}
