using System.Collections.Generic;
using DarwinFarm.Environment;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public partial class MapTileColorTransitionSystem
{
    [Header("WhiteBox biome decorations (ColorSurface remains the ground)")]
    [SerializeField] private GameObject desertBiomeVisual;
    [SerializeField] private GameObject forestBiomeVisual;
    [SerializeField] private GameObject grasslandBiomeVisual;
    [SerializeField] private GameObject highlandBiomeVisual;

    private sealed class BiomeVisualData
    {
        public BiomeType biome;
        public GameObject root;
        public MapTileInstance tile;
        public readonly List<DecorationPose> decorations = new List<DecorationPose>();
        public float age;
        public bool retiring;
    }

    private readonly Dictionary<MapTileInstance, BiomeVisualData> biomeVisuals =
        new Dictionary<MapTileInstance, BiomeVisualData>();

    private BiomeType VisibleBiome(MapTileInstance tile)
    {
        if (tile.Block != null && tile.Block.waterCoverage != WaterCoverage.Land)
            return BiomeType.Rock; // Water has no matching decoration prefab.
        if (tile.Block != null && tile.Block.waterCoverage == WaterCoverage.Land &&
            liveTerrain.TryGetValue(tile.Coordinate, out TerrainKind kind))
            return SimulationEnvironmentController.BiomeFromTerrain(kind);
        return tile.Biome;
    }

    private GameObject BiomePrefab(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Desert: return desertBiomeVisual;
            case BiomeType.Forest: return forestBiomeVisual;
            case BiomeType.Grassland: return grasslandBiomeVisual;
            case BiomeType.Highland: return highlandBiomeVisual;
            default: return null;
        }
    }

    // Called after ColorSurface is rebuilt, before RuntimeMapNavigation.RequestUpdate.
    private bool SyncBiomeVisuals(bool surfaceRebuilt)
    {
        bool changed = false;
        var present = new HashSet<MapTileInstance>();
        foreach (MapTileInstance tile in gridManager.GetPlacedTiles())
        {
            if (tile == null || !surfaces.TryGetValue(tile, out SurfaceData surface)) continue;
            present.Add(tile);
            BiomeType biome = VisibleBiome(tile);
            GameObject prefab = BiomePrefab(biome);
            biomeVisuals.TryGetValue(tile, out BiomeVisualData current);
            if (current != null && current.biome == biome && (current.root != null || prefab == null))
            {
                if (surfaceRebuilt && current.root != null) ReprojectDecorations(current, surface.collider);
                continue;
            }
            bool animate = current != null && current.biome != biome && CanAnimateBiomeChanges;
            if (current != null)
            {
                if (animate) RetireDecorations(current);
                else RemoveBiomeVisual(current);
                biomeVisuals.Remove(tile);
                changed = true;
            }
            // Remember terrains without a prefab so their next conversion is
            // distinct from first-time tile placement.
            var next = new BiomeVisualData { biome = biome, tile = tile };
            biomeVisuals.Add(tile, next);
            if (prefab == null) continue;

            GameObject root = Instantiate(prefab, tile.transform);
            root.name = "BiomeVisual_" + biome;
            root.transform.localPosition = Vector3.zero;
            // The tile root already carries the selected 90-degree rotation.
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            // The calibrated prefab owns its own per-model scale, origin and orientation.
            SetVisualLayer(root.transform);
            ProjectDecorations(root, surface.collider, biome);
            next.root = root;
            CacheDecorationPoses(next, surface.collider);
            if (animate) GrowDecorations(next);
            changed = true;
        }
        foreach (MapTileInstance tile in new List<MapTileInstance>(biomeVisuals.Keys))
        {
            if (tile != null && present.Contains(tile)) continue;
            RemoveBiomeVisual(biomeVisuals[tile]);
            biomeVisuals.Remove(tile);
            changed = true;
        }
        return changed;
    }

    private static void RemoveBiomeVisual(BiomeVisualData data)
    {
        if (data.root == null) return;
        data.root.SetActive(false);
        if (Application.isPlaying) Destroy(data.root);
        else DestroyImmediate(data.root);
    }

    private static void SetVisualLayer(Transform root)
    {
        root.gameObject.layer = 2; // Ignore Raycast; only ColorSurface is selectable ground.
        foreach (Collider collider in root.GetComponents<Collider>()) collider.enabled = false;
        foreach (Transform child in root) SetVisualLayer(child);
    }

    private static bool IsGroundMesh(BiomeType biome, string name)
    {
        switch (biome)
        {
            case BiomeType.Desert: return name.StartsWith("pPlane");
            case BiomeType.Forest: return name == "Fprest_Block";
            case BiomeType.Grassland: return name == "Land1" || name.StartsWith("pPlane");
            case BiomeType.Highland: return name == "Ground - Paint Here";
            default: return false;
        }
    }

    private static bool IsMajorObstacle(BiomeType biome, Renderer renderer)
    {
        string name = renderer.name;
        Bounds b = renderer.bounds;
        switch (biome)
        {
            case BiomeType.Forest:
                return name.StartsWith("Tree") ||
                       (name.StartsWith("Stone") && b.size.x > 0.8f);
            case BiomeType.Grassland:
                return name.StartsWith("Trunk") || name.StartsWith("pCube") ||
                       name.StartsWith("pasted__tree");
            case BiomeType.Desert:
                return name.StartsWith("pasted__tree") ||
                       (name.StartsWith("pasted__polySurface") &&
                        b.size.x > 1.2f && b.size.z > 1.2f);
            case BiomeType.Highland:
                return name.StartsWith("Mountain") ||
                       (name.StartsWith("pasted__pasted__Cube") && b.size.x > 1.2f);
            default: return false;
        }
    }

    private static void ProjectDecorations(GameObject root, MeshCollider surface, BiomeType biome,
        bool createObstacles = true)
    {
        Transform oldObstacles = root.transform.Find("BiomeObstacles");
        if (oldObstacles != null)
        {
            oldObstacles.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(oldObstacles.gameObject);
            else DestroyImmediate(oldObstacles.gameObject);
        }
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (biome == BiomeType.Forest) ThinForestTrees(root, renderers);
        var grounds = new List<MeshCollider>();
        foreach (Renderer renderer in renderers)
        {
            if (!IsGroundMesh(biome, renderer.name)) continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            MeshCollider probe = renderer.gameObject.AddComponent<MeshCollider>();
            probe.sharedMesh = filter.sharedMesh;
            grounds.Add(probe);
        }

        GameObject obstacleRoot = null;
        if (createObstacles)
        {
            obstacleRoot = new GameObject("BiomeObstacles");
            obstacleRoot.transform.SetParent(root.transform.parent, false);
            obstacleRoot.transform.localPosition = Vector3.zero;
            obstacleRoot.transform.localRotation = Quaternion.identity;
        }
        var used = new List<Vector2>();
        int obstacleCount = 0;
        foreach (Renderer renderer in renderers)
        {
            if (IsGroundMesh(biome, renderer.name))
            {
                renderer.enabled = false;
                continue;
            }
            if (renderer.name.StartsWith("human"))
            {
                renderer.enabled = false;
                continue;
            }
            // Several sample prefabs contain extra demonstration tiles outside their center 15m tile.
            Vector3 local = root.transform.parent.InverseTransformPoint(renderer.bounds.center);
            if (Mathf.Abs(local.x) > 7.25f || Mathf.Abs(local.z) > 7.25f)
            {
                renderer.enabled = false;
                continue;
            }
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            Ray ray = new Ray(renderer.bounds.center + Vector3.up * 80f, Vector3.down);
            float sourceY = float.NegativeInfinity;
            foreach (MeshCollider ground in grounds)
                if (ground.Raycast(ray, out RaycastHit hit, 160f))
                    sourceY = Mathf.Max(sourceY, hit.point.y);
            if (surface.Raycast(ray, out RaycastHit target, 160f) &&
                sourceY > float.NegativeInfinity)
                renderer.transform.position += Vector3.up * (target.point.y - sourceY);

            if (!createObstacles || !IsMajorObstacle(biome, renderer) ||
                !surface.Raycast(ray, out target, 160f)) continue;
            Vector2 flat = new Vector2(local.x, local.z);
            bool duplicate = false;
            foreach (Vector2 point in used)
                if ((point - flat).sqrMagnitude < 0.49f) { duplicate = true; break; }
            if (duplicate) continue;
            used.Add(flat);

            float width = renderer.bounds.size.x;
            float depth = renderer.bounds.size.z;
            bool mountain = biome == BiomeType.Highland && renderer.name.StartsWith("Mountain");
            float diameter = mountain ? Mathf.Clamp(Mathf.Min(width, depth) * 0.55f, 1.2f, 3f) :
                renderer.name.StartsWith("Trunk") ? 0.55f :
                renderer.name.StartsWith("Tree") || renderer.name.StartsWith("pasted__tree") ? 0.7f :
                Mathf.Clamp(Mathf.Min(width, depth) * 0.42f, 0.55f, 1.3f);
            float height = mountain ? 3f : 2.2f;
            GameObject blocker = new GameObject("BiomeObstacle_" + obstacleCount++);
            blocker.layer = LayerMask.NameToLayer("Obstacle");
            blocker.transform.SetParent(obstacleRoot.transform, false);
            blocker.transform.localPosition =
                obstacleRoot.transform.InverseTransformPoint(target.point) + Vector3.up * (height * 0.5f);
            BoxCollider box = blocker.AddComponent<BoxCollider>();
            box.size = new Vector3(diameter, height, diameter);
            var volume = blocker.AddComponent<NavMeshModifierVolume>();
            volume.area = NavMesh.GetAreaFromName("Not Walkable");
            volume.size = new Vector3(diameter + 0.15f, height + 0.3f, diameter + 0.15f);
        }
        foreach (MeshCollider probe in grounds)
        {
            probe.enabled = false;
            if (Application.isPlaying) Destroy(probe);
            else DestroyImmediate(probe);
        }
        // Keep obstacle proxies as a sibling of the visual prefab so their scale stays 1.
        if (obstacleRoot != null) obstacleRoot.transform.SetParent(root.transform, true);
    }

    // Prefer the source model's mature trees; keep the same layout in build previews.
    private static void ThinForestTrees(GameObject root, Renderer[] renderers)
    {
        Transform tileSpace = root.transform.parent;
        var trees = new List<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            Vector3 position = tileSpace.InverseTransformPoint(renderer.bounds.center);
            if (Mathf.Abs(position.x) > 7.25f || Mathf.Abs(position.z) > 7.25f) continue;
            if (renderer.enabled && renderer.name.StartsWith("Tree")) trees.Add(renderer);
            bool inLane = Mathf.Abs(position.x) < 1.8f || Mathf.Abs(position.z) < 1.8f;
            // Bushes are visual ground cover, not navigation obstacles. Clear only the lanes.
            if (inLane && (renderer.name.StartsWith("Bush") ||
                (renderer.name.StartsWith("Stone") && renderer.bounds.size.x > 0.8f)))
                renderer.enabled = false;
        }
        trees.Sort((a, b) =>
        {
            int order = b.bounds.size.y.CompareTo(a.bounds.size.y);
            return order != 0 ? order : string.CompareOrdinal(a.name, b.name);
        });
        var kept = new List<Vector2>();
        foreach (Renderer tree in trees)
        {
            Vector3 local = tileSpace.InverseTransformPoint(tree.bounds.center);
            Vector2 point = new Vector2(local.x, local.z);
            bool mature = tree.bounds.size.y >= 3f;
            if (mature)
            {
                // Move mature trees slightly to the side rather than deleting them for a lane.
                Vector2 shifted = new Vector2(
                    Mathf.Abs(point.x) < 1.8f ? (point.x < 0f ? -2.1f : 2.1f) : point.x,
                    Mathf.Abs(point.y) < 1.8f ? (point.y < 0f ? -2.1f : 2.1f) : point.y);
                Vector3 offset = new Vector3(shifted.x - point.x, 0f, shifted.y - point.y);
                tree.transform.position += tileSpace.TransformVector(offset);
                point = shifted;
            }
            bool remove = !mature && (Mathf.Abs(point.x) < 1.8f || Mathf.Abs(point.y) < 1.8f);
            // Thin saplings more strongly; mature trees always take priority.
            float spacing = tree.bounds.size.y < 1.8f ? 1.9f : 1.25f;
            if (!mature)
                foreach (Vector2 other in kept)
                    if ((point - other).sqrMagnitude < spacing * spacing) { remove = true; break; }
            if (remove) tree.enabled = false;
            else kept.Add(point);
        }
    }
}
