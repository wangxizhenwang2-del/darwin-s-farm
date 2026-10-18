using System;
using UnityEngine;

namespace Darwin.Rendering
{
    public enum RiverSide { North, East, South, West }
    public enum WaterDebugView { Composite, Flow, WaveHeight, ShoreDistance, Foam, Normals, ImpactFoam, ImpactRipples }

    [Serializable]
    public struct RiverPort
    {
        public RiverSide side;
        public Vector2 center;
        public float width;
        public Vector3 Normal => side == RiverSide.North ? Vector3.forward : side == RiverSide.East ? Vector3.right : side == RiverSide.South ? Vector3.back : Vector3.left;
        public Vector3 Center => new Vector3(center.x, 0, center.y);
    }

    /// <summary>Immutable, editor-baked source data. No imported model Read/Write changes are needed.</summary>
    public sealed class RiverSurfaceProfile : ScriptableObject
    {
        public float tileSize = 17;
        [Tooltip("A lake/sea occupies the full tile, with mouths that can receive narrower rivers.")]
        public bool fullTileWater;
        public float bankHeight = 1;
        public Mesh waterMesh;
        public Material waterMaterial;
        public RiverPort[] ports = Array.Empty<RiverPort>();
        public Vector4 shoreRect;
        public float shoreRange = 3;
        public int shoreResolution = 256;
        [HideInInspector] public byte[] shoreDistances = Array.Empty<byte>();
    }
}
