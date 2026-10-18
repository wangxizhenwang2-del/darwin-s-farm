using UnityEngine;

namespace Darwin.Rendering
{
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Moebius River Tile")]
    public sealed class MoebiusRiverTile : MonoBehaviour
    {
        [Tooltip("No profile means this is a land tile. River profiles contain the authored mouths and water geometry.")]
        public RiverSurfaceProfile profile;
        [Tooltip("Original prefab water renderer. The network temporarily hides it and draws a connected instance instead.")]
        public MeshRenderer originalWater;
        [Tooltip("Water level in metres above this tile's origin. Move the tile upward to raise the entire terrain.")]
        public float waterLevel = .25f;
        [Tooltip("Land surface height above the tile origin, used when closing a neighbouring river mouth.")]
        public float landSurfaceHeight = 1.1f;
        [Tooltip("Height of the underside of a land model above this wrapper, for the supporting terrain foundation.")]
        public float foundationTop;
        MoebiusRiverNetwork previousNetwork;

        public bool IsRiver => profile && profile.waterMesh && profile.waterMaterial;
        public float WorldWaterHeight => transform.position.y + waterLevel;
        public float WorldBankHeight => transform.position.y + (IsRiver ? profile.bankHeight : landSurfaceHeight);

        void OnEnable() => Request();
        void OnValidate() => Request();
        void OnTransformParentChanged() => Request();
        void OnDisable()
        {
            if (originalWater) originalWater.forceRenderingOff = false;
            Request();
        }
        void OnDestroy() => Request();
        void Request()
        {
            var network = GetComponentInParent<MoebiusRiverNetwork>();
            if (previousNetwork && previousNetwork != network) previousNetwork.RequestRebuild();
            if (network) network.RequestRebuild();
            previousNetwork = network;
        }
        void OnDrawGizmosSelected()
        {
            if (!IsRiver) return;
            Gizmos.color = new Color(.3f, .95f, 1);
            foreach (var port in profile.ports)
            {
                var center = transform.TransformPoint(port.Center); center.y = WorldWaterHeight;
                var normal = transform.TransformDirection(port.Normal);
                var tangent = Vector3.Cross(Vector3.up, normal);
                Gizmos.DrawLine(center - tangent * port.width * .5f, center + tangent * port.width * .5f);
                Gizmos.DrawRay(center, normal * .75f);
            }
        }
    }
}
