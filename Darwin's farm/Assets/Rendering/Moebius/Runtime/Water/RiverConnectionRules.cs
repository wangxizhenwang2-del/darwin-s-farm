using UnityEngine;
namespace Darwin.Rendering
{
    public static class RiverConnectionRules
    {
        public static bool Match(MoebiusRiverTile a, RiverPort port, MoebiusRiverTile b,
            out RiverPort opposite, float tolerance = .06f)
        {
            opposite = default;
            if (!a || !b || a == b || !a.IsRiver || !b.IsRiver) return false;
            Vector3 normal = a.transform.TransformDirection(port.Normal);
            Vector3 delta = b.transform.position - a.transform.position - normal * a.profile.tileSize;
            delta.y = 0;
            if (delta.sqrMagnitude > tolerance * tolerance) return false;
            foreach (RiverPort other in b.profile.ports)
            {
                Vector3 gap = b.transform.TransformPoint(other.Center) - a.transform.TransformPoint(port.Center);
                gap.y = 0;
                bool broad = a.profile.fullTileWater || b.profile.fullTileWater;
                float tangentOffset = Vector3.Dot(gap, Vector3.Cross(Vector3.up, normal));
                bool aligned = broad ? Mathf.Abs(Vector3.Dot(gap, normal)) <= tolerance &&
                    Mathf.Abs(tangentOffset) + Mathf.Min(port.width, other.width) * .5f <= Mathf.Max(port.width, other.width) * .5f + tolerance
                    : gap.sqrMagnitude <= tolerance * tolerance && Mathf.Abs(port.width - other.width) <= .12f;
                if (Vector3.Dot(normal, b.transform.TransformDirection(other.Normal)) < -.999f && aligned)
                { opposite = other; return true; }
            }
            return false;
        }
    }
}
