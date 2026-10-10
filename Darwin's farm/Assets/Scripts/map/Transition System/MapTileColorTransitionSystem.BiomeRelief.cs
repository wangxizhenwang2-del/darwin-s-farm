using System.Collections.Generic;
using UnityEngine;

public partial class MapTileColorTransitionSystem
{
    // Each calibrated model supplies relief within its 15 m interior. The
    // remaining 1 m rim on each side belongs entirely to the existing blend.
    private const int ReliefSamples = 31;
    private const float ReliefHalfWidth = 7.5f;
    private const float ReliefStep = 0.5f;

    [Header("Biome ground relief")]
    [SerializeField, Range(0f, 1f)] private float biomeReliefStrength = 0.55f;
    [SerializeField, Range(0f, 1.5f)] private float biomeReliefLimit = 0.65f;
    [SerializeField, Range(0.5f, 4f)] private float biomeReliefEdgeBlend = 2f;

    private readonly Dictionary<GameObject, float[,]> biomeRelief =
        new Dictionary<GameObject, float[,]>();

    private float SampleBiomeRelief(MapTileInstance tile, float localX, float localZ)
    {
        GameObject prefab = BiomePrefab(VisibleBiome(tile));
        if (prefab == null || biomeReliefStrength <= 0f || biomeReliefLimit <= 0f)
            return 0f;

        float distanceToEdge = ReliefHalfWidth -
            Mathf.Max(Mathf.Abs(localX), Mathf.Abs(localZ));
        if (distanceToEdge <= 0f) return 0f;

        if (!biomeRelief.TryGetValue(prefab, out float[,] heights))
        {
            heights = MeasureBiomeRelief(prefab, VisibleBiome(tile));
            biomeRelief.Add(prefab, heights);
        }
        if (heights == null) return 0f;

        // Both terrain vertices and the prefab are in the rotated tile root's
        // local space, so their X/Z coordinates already match.
        int x = Mathf.Clamp(Mathf.RoundToInt((localX + ReliefHalfWidth) / ReliefStep),
            0, ReliefSamples - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt((localZ + ReliefHalfWidth) / ReliefStep),
            0, ReliefSamples - 1);
        float blend = Mathf.Clamp01(distanceToEdge / biomeReliefEdgeBlend);
        blend = blend * blend * (3f - 2f * blend);
        return Mathf.Clamp(heights[x, z] * biomeReliefStrength,
            -biomeReliefLimit, biomeReliefLimit) * blend;
    }

    private static float[,] MeasureBiomeRelief(GameObject prefab, BiomeType biome)
    {
        GameObject probeRoot = Instantiate(prefab);
        probeRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var grounds = new List<MeshCollider>();
        try
        {
            foreach (Renderer renderer in probeRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsGroundMesh(biome, renderer.name)) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                MeshCollider probe = renderer.gameObject.AddComponent<MeshCollider>();
                probe.sharedMesh = filter.sharedMesh;
                grounds.Add(probe);
            }
            if (grounds.Count == 0)
            {
                Debug.LogWarning("Biome relief has no ground mesh: " + prefab.name);
                return null;
            }

            var raw = new float[ReliefSamples, ReliefSamples];
            float sum = 0f;
            int count = 0;
            for (int x = 0; x < ReliefSamples; x++)
                for (int z = 0; z < ReliefSamples; z++)
                {
                    float px = -ReliefHalfWidth + x * ReliefStep;
                    float pz = -ReliefHalfWidth + z * ReliefStep;
                    Ray ray = new Ray(new Vector3(px, 80f, pz), Vector3.down);
                    float height = float.NegativeInfinity;
                    foreach (MeshCollider ground in grounds)
                        if (ground.Raycast(ray, out RaycastHit hit, 160f))
                            height = Mathf.Max(height, hit.point.y);
                    raw[x, z] = height;
                    if (height > float.NegativeInfinity) { sum += height; count++; }
                }
            if (count == 0)
            {
                Debug.LogWarning("Biome relief could not be sampled: " + prefab.name);
                return null;
            }
            float fallback = sum / count;
            var smooth = new float[ReliefSamples, ReliefSamples];
            float smoothSum = 0f;
            for (int x = 0; x < ReliefSamples; x++)
                for (int z = 0; z < ReliefSamples; z++)
                {
                    float weighted = 0f;
                    float weightSum = 0f;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int sx = Mathf.Clamp(x + dx, 0, ReliefSamples - 1);
                            int sz = Mathf.Clamp(z + dz, 0, ReliefSamples - 1);
                            float weight = (dx == 0 ? 2f : 1f) *
                                           (dz == 0 ? 2f : 1f);
                            float h = raw[sx, sz];
                            weighted += (h > float.NegativeInfinity ? h : fallback) * weight;
                            weightSum += weight;
                        }
                    smooth[x, z] = weighted / weightSum;
                    smoothSum += smooth[x, z];
                }
            float mean = smoothSum / (ReliefSamples * ReliefSamples);
            for (int x = 0; x < ReliefSamples; x++)
                for (int z = 0; z < ReliefSamples; z++)
                    smooth[x, z] -= mean;
            return smooth;
        }
        finally
        {
            probeRoot.SetActive(false);
            if (Application.isPlaying) Destroy(probeRoot);
            else DestroyImmediate(probeRoot);
        }
    }
}
