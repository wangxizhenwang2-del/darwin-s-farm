using System.Collections.Generic;
using Darwin.Rendering;
using UnityEngine;

// Adapts authored water to the placed map. Ecology retains population ownership.
[DisallowMultipleComponent]
public sealed class MapWaterController : MonoBehaviour
{
    private sealed class Entry
    {
        public MapTileInstance tile;
        public MapTileDefinition definition;
        public MoebiusRiverTile river;
        public GameObject prefab;
        public int rotation, level;
        public WaterTileSpace space;
    }
    private readonly Dictionary<Vector2Int, Entry> entries = new Dictionary<Vector2Int, Entry>();
    private MapGridManager grid;
    private MapWaterAssets assets;
    public MoebiusRiverNetwork Network { get; private set; }
    public int Revision { get; private set; }
    public MapGridManager Grid => grid;
    // Cache the existing ecological area's membership; no second topology rule.
    private sealed class SwimmingArea
    {
        public List<MapTileInstance> tiles;
        public HashSet<Vector2Int> coordinates;
    }
    private readonly Dictionary<Vector2Int, SwimmingArea> swimmingAreas = new Dictionary<Vector2Int, SwimmingArea>();
    private int swimmingRevision = -1;

    private void EnsureSwimmingAreas()
    {
        if (swimmingRevision == Revision) return;
        swimmingAreas.Clear();
        foreach (var tiles in WaterBodyTopology.AllAreas(grid))
        {
            var area = new SwimmingArea { tiles = tiles, coordinates = new HashSet<Vector2Int>() };
            foreach (var tile in tiles) area.coordinates.Add(tile.Coordinate);
            foreach (var tile in tiles) swimmingAreas[tile.Coordinate] = area;
        }
        swimmingRevision = Revision;
    }

    public bool TrySwimmingArea(Vector2Int home, out IReadOnlyList<MapTileInstance> tiles)
    {
        EnsureSwimmingAreas();
        tiles = swimmingAreas.TryGetValue(home, out var area) ? area.tiles : null;
        return tiles != null;
    }

    public bool TrySwimmingPose(Vector2Int home, Vector3 position, float radius, out Vector3 surface)
    {
        EnsureSwimmingAreas();
        surface = default;
        return swimmingAreas.TryGetValue(home, out var area) &&
            TrySample(position, radius, out surface, out var tile) && area.coordinates.Contains(tile.Coordinate);
    }

    public bool TrySwimmingPath(Vector2Int home, Vector3 from, Vector3 to, float radius, out Vector3[] path)
    {
        EnsureSwimmingAreas();
        path = null;
        return swimmingAreas.TryGetValue(home, out var area) &&
            WaterPathfinding.TryPath(this, from, to, radius, out path, allowedTiles: area.coordinates);
    }

    public bool SwimmingSegment(Vector2Int home, Vector3 from, Vector3 to, float radius)
    {
        EnsureSwimmingAreas();
        return swimmingAreas.TryGetValue(home, out var area) &&
            WaterPathfinding.Segment(this, from, to, radius, allowedTiles: area.coordinates);
    }
    public static MapWaterController GetOrCreate(MapGridManager grid)
    {
        var water = grid.GetComponent<MapWaterController>();
        if (!water) water = grid.gameObject.AddComponent<MapWaterController>();
        return water;
    }
    private void Awake()
    {
        grid = GetComponent<MapGridManager>();
        assets = Resources.Load<MapWaterAssets>("Water/MapWaterAssets");
        if (!assets) { Debug.LogError("Missing Water/MapWaterAssets; run Tools/Water/Install map assets.", this); return; }
        var root = new GameObject("Map River Network"); root.transform.SetParent(transform, false);
        Network = root.AddComponent<MoebiusRiverNetwork>(); Network.autoRefresh = false;
        Network.generateLandFoundations = false;
        Network.bankMaterial = assets.bankMaterial; Network.foundationMaterial = assets.foundationMaterial;
        Network.waterfallPrefab = assets.waterfallPrefab; Network.impactMaterial = assets.impactMaterial;
    }
    private void OnEnable() { if (grid) grid.TilesChanged += Refresh; }
    private void OnDisable() { if (grid) grid.TilesChanged -= Refresh; }
    private void Start() => Refresh();
    public void Refresh()
    {
        if (!assets || !Network || !grid) return;
        bool changed = false;
        var present = new HashSet<Vector2Int>();
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            if (!tile || tile.Definition == null) continue;
            present.Add(tile.Coordinate);
            bool water = tile.Block ? HabitatTopology.IsWater(tile.Block) : tile.Definition.initialWaterCoverage != WaterCoverage.Land;
            int extraRotation = 0;
            GameObject prefab = water ? assets.Resolve(tile.Definition, out extraRotation) : null;
            entries.TryGetValue(tile.Coordinate, out Entry entry);
            if (entry != null && (entry.tile != tile || entry.prefab != prefab))
            { Release(entry.river.gameObject); entries.Remove(tile.Coordinate); entry = null; changed = true; }
            if (entry == null)
            {
                GameObject root = prefab ? Instantiate(prefab, Network.transform) : new GameObject("Land bank boundary");
                if (!prefab) root.transform.SetParent(Network.transform, false);
                var river = root.GetComponent<MoebiusRiverTile>();
                if (!river) river = root.AddComponent<MoebiusRiverTile>();
                entry = new Entry { tile = tile, river = river, prefab = prefab, rotation = -1, level = -1 };
                entries.Add(tile.Coordinate, entry); changed = true;
                foreach (Collider collider in root.GetComponentsInChildren<Collider>()) collider.enabled = false;
                foreach (Transform child in root.GetComponentsInChildren<Transform>()) child.gameObject.layer = 2;
                if (water && !prefab) Debug.LogError("No authored water asset for " + tile.Definition.name, tile);
            }
            int rotation = tile.RotationSteps + extraRotation;
            if (entry.rotation != rotation || entry.level != tile.HeightLevel || entry.definition != tile.Definition ||
                entry.river.transform.position != tile.transform.position)
            {
                entry.river.transform.SetPositionAndRotation(tile.transform.position, Quaternion.Euler(0, rotation * 90, 0));
                entry.rotation = rotation; entry.level = tile.HeightLevel; entry.definition = tile.Definition; changed = true;
            }
        }
        foreach (Vector2Int id in new List<Vector2Int>(entries.Keys))
            if (!present.Contains(id)) { Release(entries[id].river.gameObject); entries.Remove(id); changed = true; }
        if (!changed) return;
        if (!Network.RebuildChanged()) { Debug.LogError(Network.LastError, this); return; }
        Revision++;
        var updated = new List<Entry>();
        foreach (Entry entry in entries.Values)
        {
            if (Network.TryGetWater(entry.river, out MeshRenderer renderer))
            {
                if (entry.space == null || entry.space.Renderer != renderer)
                { entry.space = new WaterTileSpace(renderer, entry.river); updated.Add(entry); }
            }
            else entry.space = null;
            Transform surface = entry.tile.transform.Find("ColorSurface");
            if (surface && entry.river.IsRiver) surface.GetComponent<Renderer>().forceRenderingOff = true;
        }
        foreach (Entry entry in updated)
            entry.space.ConfigurePortals((normal, point) => HasPassableNeighbour(entry.tile, normal, point));
    }
    private bool HasPassableNeighbour(MapTileInstance tile, Vector3 normal, Vector3 point)
    {
        var direction = new Vector2Int(Mathf.RoundToInt(normal.x), Mathf.RoundToInt(normal.z));
        return grid.TryGetTile(tile.Coordinate + direction, out MapTileInstance other) &&
            Mathf.Abs(tile.HeightLevel - other.HeightLevel) <= 1 && Connects(tile, other) &&
            TrySpace(other.Coordinate, out var space) && space.Contains(point + normal * .03f, 0);
    }
    public bool Connects(MapTileInstance a, MapTileInstance b)
    {
        if (!a || !b || !entries.TryGetValue(a.Coordinate, out Entry left) ||
            !entries.TryGetValue(b.Coordinate, out Entry right) || !left.river.IsRiver || !right.river.IsRiver) return false;
        foreach (RiverPort port in left.river.profile.ports)
            if (RiverConnectionRules.Match(left.river, port, right.river, out _)) return true;
        return false;
    }
    public bool TrySpace(Vector2Int coordinate, out WaterTileSpace space)
    {
        space = entries.TryGetValue(coordinate, out Entry entry) ? entry.space : null;
        return space != null;
    }
    public bool TrySample(Vector3 position, float radius, out Vector3 surface, out MapTileInstance tile)
    {
        surface = default; tile = null;
        Vector2Int id = grid.WorldToGrid(position);
        if (!entries.TryGetValue(id, out Entry entry) || entry.space == null || !entry.space.Contains(position, radius)) return false;
        tile = entry.tile; surface = position; surface.y = entry.river.WorldWaterHeight; return true;
    }
    private static void Release(GameObject go) { go.SetActive(false); Destroy(go); }
    private void OnDestroy() { if (Network) Release(Network.gameObject); }
}
