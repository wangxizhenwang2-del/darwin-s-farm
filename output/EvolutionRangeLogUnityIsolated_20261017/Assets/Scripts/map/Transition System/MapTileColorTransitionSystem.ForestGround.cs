using System.Collections.Generic;
using UnityEngine;

public partial class MapTileColorTransitionSystem
{
    [Header("Forest art pilot")]
    [Tooltip("Restore the forest's painted ground and authored planting layout. Disable to use the original WhiteBox appearance.")]
    [SerializeField] private bool preserveForestArtwork;
    [Tooltip("Forest rim color matched to the painted ground's average paper-lifted tone. Also used by neighboring tiles when blending into a forest.")]
    [SerializeField, ColorUsage(false)] private Color forestGroundBlendColor =
        new Color(0.356f, 0.430f, 0.284f, 1f);

    [System.Serializable]
    private struct BiomeGroundPalette
    {
        public BiomeType biome;
        public Color color;
    }

    [Header("Other biome artwork")]
    [SerializeField] private bool preserveOtherBiomeArtwork;
    [SerializeField] private List<BiomeGroundPalette> otherBiomeGroundColors = new List<BiomeGroundPalette>();

    private bool UsesBiomeArtwork(BiomeType biome) => BiomePrefab(biome) != null &&
        (biome == BiomeType.Forest ? preserveForestArtwork : preserveOtherBiomeArtwork);

    private bool TryArtworkPalette(MapTileInstance tile, out Color color)
    {
        color = default;
        BiomeType biome = VisibleBiome(tile);
        if (!UsesBiomeArtwork(biome)) return false;
        if (biome == BiomeType.Forest) { color = forestGroundBlendColor; return true; }
        foreach (BiomeGroundPalette entry in otherBiomeGroundColors)
            if (entry.biome == biome) { color = entry.color; return true; }
        return false;
    }

    // Only visual channels change. The existing mesh, collider, vertex colors,
    // terrain heights and navigation ownership remain identical.
    private sealed class BiomeGroundData
    {
        public Material material;
        public Vector2[] uv;
        public float[] coverage;
        public float[] heights;
        public Vector3[] sourceVertices;
        public int[] sourceTriangles;
        public float sourceMeanHeight;
    }

    private readonly Dictionary<GameObject, BiomeGroundData> groundArtCache =
        new Dictionary<GameObject, BiomeGroundData>();
    private static readonly int TileArtStrength = Shader.PropertyToID("_TileArtStrength");

    private BiomeGroundData GroundArtwork(BiomeType biome)
    {
        GameObject prefab = BiomePrefab(biome);
        if (!UsesBiomeArtwork(biome)) return null;
        if (!groundArtCache.TryGetValue(prefab, out BiomeGroundData artwork))
            groundArtCache.Add(prefab, artwork = MeasureBiomeGround(prefab, biome));
        return artwork;
    }

    private void ConfigureBiomeGround(MapTileInstance tile, SurfaceData surface)
    {
        BiomeType biome = VisibleBiome(tile);
        BiomeGroundData artwork = GroundArtwork(biome);
        surface.targetArtwork = artwork;
        surface.targetArtStrength = artwork == null ? 0f : 1f;
        if (surface.groundArtwork == null) surface.groundArtwork = artwork;
        // Keep the outgoing material/UVs through the first half of an art-to-art change.
        ApplyGroundArtworkUV(surface);
    }

    private static void ApplyGroundArtworkUV(SurfaceData surface)
    {
        if (surface.groundArtwork == null) return;
        int count = surface.mesh.vertexCount;
        var uv = new Vector2[count];
        var coverage = new Vector2[count];
        for (int i = 0; i < surface.groundArtwork.uv.Length; i++)
        {
            uv[i] = surface.groundArtwork.uv[i];
            coverage[i] = new Vector2(surface.groundArtwork.coverage[i], 0f);
        }
        // Cliff/wall vertices deliberately keep zero art coverage.
        surface.mesh.uv = uv;
        surface.mesh.uv2 = coverage;
    }

    private static void SetGroundArtworkSource(SurfaceData surface, BiomeGroundData artwork)
    {
        if (surface.groundArtwork == artwork) return;
        surface.groundArtwork = artwork;
        ApplyGroundArtworkUV(surface);
    }

    private void CommitGroundArtwork(SurfaceData surface)
    {
        SetGroundArtworkSource(surface, surface.targetArtwork);
        SetGroundArtStrength(surface, surface.targetArtStrength);
    }

    private void ApplyGroundArtworkTransition(SurfaceFeedback blend, float progress)
    {
        bool swap = blend.fromArtwork != null && blend.toArtwork != null && blend.fromArtwork != blend.toArtwork;
        if (swap && progress < .5f)
        {
            SetGroundArtworkSource(blend.surface, blend.fromArtwork);
            SetGroundArtStrength(blend.surface, blend.fromArtStrength * (1f - progress * 2f));
        }
        else
        {
            SetGroundArtworkSource(blend.surface, blend.toArtwork ?? blend.fromArtwork);
            float strength = swap ? blend.toArtStrength * (progress * 2f - 1f) :
                Mathf.Lerp(blend.fromArtStrength, blend.toArtStrength, progress);
            SetGroundArtStrength(blend.surface, strength);
        }
    }

    private void SetGroundArtStrength(SurfaceData surface, float strength)
    {
        surface.artStrength = strength;
        if (surface.groundArtwork == null || (strength <= 0f && surface.targetArtStrength <= 0f))
        {
            surface.groundArtwork = null;
            surface.renderer.sharedMaterial = vertexColorMaterial;
            if (surface.properties != null)
            {
                surface.renderer.GetPropertyBlock(surface.properties);
                surface.properties.SetFloat(TileArtStrength, 0f);
                surface.renderer.SetPropertyBlock(surface.properties);
            }
            return;
        }
        surface.renderer.sharedMaterial = surface.groundArtwork.material;
        if (surface.properties == null) surface.properties = new MaterialPropertyBlock();
        surface.renderer.GetPropertyBlock(surface.properties);
        surface.properties.SetFloat(TileArtStrength, strength);
        surface.renderer.SetPropertyBlock(surface.properties);
    }

    private static BiomeGroundData MeasureBiomeGround(GameObject prefab, BiomeType biome)
    {
        GameObject sample = Instantiate(prefab);
        sample.name = "BiomeGroundUVProbe_" + biome;
        sample.hideFlags = HideFlags.HideAndDontSave;
        sample.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var colliders = new List<MeshCollider>();
        try
        {
            Renderer ground = null;
            foreach (Renderer renderer in sample.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && IsGroundMesh(biome, renderer.name))
                { ground = renderer; break; }
            MeshFilter filter = ground == null ? null : ground.GetComponent<MeshFilter>();
            Material source = ground == null ? null : ground.sharedMaterial;
            if (filter == null || filter.sharedMesh == null || source == null ||
                source.shader.name != "Darwin/Moebius NPR") return null;
            if (!filter.sharedMesh.isReadable)
                throw new System.InvalidOperationException("Rebuild the readable ground adapter for " + biome);
            MeshCollider collider = ground.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            colliders.Add(collider);
            // Sampling can run before the first physics update after importing
            // or rotating a scaled model. Do not cache an empty/stale footprint.
            Physics.SyncTransforms();
            int row = Segments + 1, count = row * row;
            var uv = new Vector2[count];
            var heights = new float[count];
            float heightSum = 0f;
            var valid = new List<int>();
            var invalid = new List<int>();
            float step = MapGridManager.TileSize / Segments;
            for (int z = 0; z < row; z++)
                for (int x = 0; x < row; x++)
                {
                    int i = z * row + x;
                    var ray = new Ray(new Vector3(-HalfSize + x * step, 80f,
                        -HalfSize + z * step), Vector3.down);
                    if (collider.Raycast(ray, out RaycastHit hit, 160f) && hit.normal.y > 0.15f)
                    { uv[i] = hit.textureCoord; heights[i] = hit.point.y; heightSum += hit.point.y; valid.Add(i); }
                    else invalid.Add(i);
                }
            if (valid.Count == 0)
                throw new System.InvalidOperationException("No authored ground samples for " + biome);
            var coverage = new float[count];
            foreach (int i in valid)
            {
                // Only the one-metre seam blends to its neighbour. The authored
                // interior uses its original mesh, normals and material slots.
                float edge = HalfSize - Mathf.Max(Mathf.Abs(-HalfSize + i % row * step),
                    Mathf.Abs(-HalfSize + i / row * step));
                float t = Mathf.Clamp01(edge);
                coverage[i] = t * t * (3f - 2f * t);
            }
            foreach (int i in invalid)
            {
                int nearest = valid[0];
                float distance = float.PositiveInfinity;
                foreach (int candidate in valid)
                {
                    float d = GridDistance(i, candidate, row);
                    if (d < distance) { distance = d; nearest = candidate; }
                }
                uv[i] = uv[nearest];
                heights[i] = heights[nearest];
                float edge = HalfSize - Mathf.Max(Mathf.Abs(-HalfSize + i % row * step),
                    Mathf.Abs(-HalfSize + i / row * step));
                coverage[i] = Mathf.Clamp01(edge);
            }
            float mean = heightSum / valid.Count;
            for (int i = 0; i < count; i++)
            {
                float edge = HalfSize - Mathf.Max(Mathf.Abs(-HalfSize + i % row * step),
                    Mathf.Abs(-HalfSize + i / row * step));
                heights[i] = (heights[i] - mean) * Mathf.Clamp01(edge);
            }
            Vector3[] sourceVertices = filter.sharedMesh.vertices;
            for (int i = 0; i < sourceVertices.Length; i++)
                sourceVertices[i] = ground.transform.TransformPoint(sourceVertices[i]);
            int[] sourceTriangles = filter.sharedMesh.triangles;
            // Preserve front-facing collision when baking mirrored hierarchies.
            if (ground.transform.localToWorldMatrix.determinant < 0f)
                for (int i = 0; i < sourceTriangles.Length; i += 3)
                {
                    int swap = sourceTriangles[i + 1];
                    sourceTriangles[i + 1] = sourceTriangles[i + 2];
                    sourceTriangles[i + 2] = swap;
                }
            // Clone at runtime; the technical artist's shared material is never edited.
            var material = new Material(source) { name = source.name + " (WhiteBox " + biome + ")",
                hideFlags = HideFlags.DontSave };
            material.shader = Shader.Find("Darwin/WhiteBox Moebius Rim");
            material.SetFloat("_UseTilePaletteBlend", 1f);
            return new BiomeGroundData { material = material, uv = uv, coverage = coverage,
                heights = heights, sourceMeanHeight = mean, sourceVertices = sourceVertices,
                sourceTriangles = sourceTriangles };
        }
        finally
        {
            foreach (MeshCollider collider in colliders) collider.enabled = false;
            sample.SetActive(false);
            if (Application.isPlaying) Destroy(sample);
            else DestroyImmediate(sample);
        }
    }

    private static float GridDistance(int first, int second, int row)
    {
        int x = first % row - second % row, z = first / row - second / row;
        return x * x + z * z;
    }

    private void ReleaseBiomeGround()
    {
        foreach (BiomeGroundData artwork in groundArtCache.Values)
            if (artwork != null && artwork.material != null) Destroy(artwork.material);
        groundArtCache.Clear();
    }
}
