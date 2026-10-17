using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

[DisallowMultipleComponent]
public sealed class PopulationMovementController : MonoBehaviour
{
    private static readonly List<PopulationMovementController> controllers = new List<PopulationMovementController>();
    [SerializeField] private MapGridManager gridManager;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private Sprite fallbackSprite;
    [SerializeField] private PopulationMovementSettings settings = new PopulationMovementSettings();
    [Header("Opt-in runtime debug")]
    [SerializeField] private Vector2Int debugTile = Vector2Int.zero;
    [SerializeField, Min(0)] private int firstDebugCount = 37;
    [SerializeField, Min(0)] private int secondDebugCount = 123;
    [SerializeField, Min(0)] private int editedDebugCount = 68;
    [SerializeField] private int walkableAreaMask = NavMesh.AllAreas;
    private readonly List<WhiteboxPopulation> populations = new List<WhiteboxPopulation>();
    private readonly List<WhiteboxPopulation> debugPopulations = new List<WhiteboxPopulation>();
    private bool navigationWasUpdating;
    private int navigationRevision = -1;
    public PopulationMovementSettings Settings => settings;
    public Camera ViewCamera => viewCamera;
    public Sprite FallbackSprite => fallbackSprite;
    public IReadOnlyList<WhiteboxPopulation> Populations => populations.AsReadOnly();

    // Explicit attachment: no display objects are created on normal startup.
    public static PopulationMovementController GetOrCreate(MapGridManager grid)
    {
        if (grid == null || !Application.isPlaying) return null;
        PopulationMovementController controller = grid.GetComponent<PopulationMovementController>();
        if (controller == null) controller = grid.gameObject.AddComponent<PopulationMovementController>();
        controller.gridManager = grid;
        return controller;
    }

    private void Awake() { if (gridManager == null) gridManager = GetComponent<MapGridManager>(); }
    private void OnEnable()
    {
        if (!controllers.Contains(this)) controllers.Add(this);
        if (gridManager != null) gridManager.TilesChanged += OnTilesChanged;
    }
    private void OnDisable()
    {
        if (gridManager != null) gridManager.TilesChanged -= OnTilesChanged;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetControllers() => controllers.Clear();
    internal static bool HasActivePopulations
    {
        get
        {
            foreach (PopulationMovementController controller in controllers)
                if (controller != null)
                    foreach (WhiteboxPopulation population in controller.populations)
                        if (population != null && !population.IsAir && population.gameObject.activeInHierarchy && population.leader != null) return true;
            return false;
        }
    }

    internal static bool AllowsPlayerMove(Vector3 from, Vector3 to, float radius)
    {
        foreach (PopulationMovementController controller in controllers)
        {
            if (controller == null) continue;
            foreach (WhiteboxPopulation population in controller.populations)
            {
                if (population == null || population.IsAir || population.leader == null) continue;
                var leader = population.leader;
                if (!leader.visual.gameObject.activeInHierarchy) continue;
                float required = radius + PopulationTileSpace.Clearance(leader.size) + Mathf.Max(0f, controller.settings.playerGap);
                if (!PopulationCollisionMath.AllowsMove(PopulationCollisionMath.Flat(from), PopulationCollisionMath.Flat(to),
                    PopulationCollisionMath.Flat(leader.groundPosition), required)) return false;
            }
        }
        return true;
    }
    internal bool NavigationReady => gridManager != null && gridManager.Navigation != null && !gridManager.Navigation.IsUpdating;

    public WhiteboxPopulation CreatePopulation(Vector2Int tileId, int initialCount, bool airborne = false)
    {
        if (airborne)
        {
            if (!Application.isPlaying || gridManager == null ||
                !gridManager.TryGetTile(tileId, out MapTileInstance airTile)) return null;
            foreach (WhiteboxPopulation existing in populations)
                if (existing != null && existing.IsAir && existing.TileId == tileId) return null;
            GameObject airRoot = new GameObject("AirPopulation");
            airRoot.transform.SetParent(transform, false);
            WhiteboxPopulation air = airRoot.AddComponent<WhiteboxPopulation>();
            air.Initialize(this, airTile, null, initialCount, Vector3.zero, Color.white, true);
            populations.Add(air);
            return air;
        }
        if (!Application.isPlaying || !NavigationReady || !gridManager.TryGetTile(tileId, out MapTileInstance tile)) return null;
        NavMeshSurface surface = gridManager.Navigation.GetComponent<NavMeshSurface>();
        if (surface == null || surface.navMeshData == null) return null;
        Physics.SyncTransforms();
        var filter = new NavMeshQueryFilter { agentTypeID = surface.agentTypeID, areaMask = walkableAreaMask };
        var space = new PopulationTileSpace(tile, filter);
        if (!space.IsReady || settings.leaderSize <= 0f) return null;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            Bounds bounds = space.Bounds;
            Vector3 leader = new Vector3(Random.Range(bounds.min.x, bounds.max.x), bounds.max.y,
                Random.Range(bounds.min.z, bounds.max.z));
            if (!space.CanTraverse(leader, leader, settings.leaderSize) ||
                !SpawnSeparated(leader, settings.leaderSize)) continue;
            GameObject root = new GameObject("Population"); root.transform.SetParent(transform, true);
            WhiteboxPopulation population = root.AddComponent<WhiteboxPopulation>();
            Color color = Color.HSVToRGB(Mathf.Repeat(populations.Count * 0.381966f + 0.05f, 1f), 0.7f, 1f);
            population.Initialize(this, tile, space, initialCount, leader, color);
            populations.Add(population);
            PopulationPlayerBarrier.ActivateAll();
            return population;
        }
        Debug.LogWarning($"No safe space for a population representative on tile {tileId}; creation skipped.", this);
        return null;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    private bool SpawnSeparated(Vector3 point, float size, WhiteboxPopulation ignore = null)
    {
        foreach (PopulationPlayerBarrier player in PopulationPlayerBarrier.Players)
            if (player != null && player.isActiveAndEnabled &&
                HorizontalDistance(point, player.transform.position) <
                PopulationTileSpace.Clearance(size) + player.Radius + Mathf.Max(0f, settings.playerGap)) return false;
        foreach (WhiteboxPopulation population in populations)
        {
            if (population == null || population.IsAir || population == ignore || population.leader == null) continue;
            var leader = population.leader;
            if (leader.visual.gameObject.activeInHierarchy && HorizontalDistance(point, leader.groundPosition) <
                PopulationTileSpace.Clearance(size) + PopulationTileSpace.Clearance(leader.size) +
                Mathf.Max(0f, settings.populationGap)) return false;
        }
        return true;
    }

    // Only terrain changes may relocate the representative to a safe pose on its
    // own tile. Its ecological identity and count remain intact.
    internal bool TryRecoverPosition(WhiteboxPopulation owner, out Vector3 position)
    {
        position = default;
        if (owner == null || owner.leader == null || !owner.space.IsReady) return false;
        var space = owner.space;
        Vector3 hint = owner.leader.groundPosition;
        var candidates = new List<Vector3> { hint };
        Bounds bounds = space.Bounds;
        for (float z = bounds.min.z + 1f; z < bounds.max.z; z += 1.25f)
            for (float x = bounds.min.x + 1f; x < bounds.max.x; x += 1.25f)
                candidates.Add(new Vector3(x, bounds.max.y, z));
        candidates.Sort((a, b) => HorizontalDistance(a, hint).CompareTo(HorizontalDistance(b, hint)));
        foreach (Vector3 candidate in candidates)
        {
            if (!space.CanTraverse(candidate, candidate, owner.leader.size) ||
                !SpawnSeparated(candidate, owner.leader.size, owner)) continue;
            // Reject isolated legal poses with no room to start wandering.
            for (int direction = 0; direction < 8; direction++)
            {
                float yaw = direction * Mathf.PI * .25f;
                Vector3 target = candidate + new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * 2f;
                if (!space.TryPath(candidate, target, owner.leader.size, out _,
                    (a, b) => HasSeparation(owner, a, b))) continue;
                position = candidate;
                return true;
            }
        }
        return false;
    }

    internal bool HasSeparation(WhiteboxPopulation owner, Vector3 target) =>
        HasSeparation(owner, owner.leader.groundPosition, target);

    internal bool HasSeparation(WhiteboxPopulation owner, Vector3 start, Vector3 target)
    {
        if (owner.IsAir) return true;
        float clearance = PopulationTileSpace.Clearance(owner.leader.size);
        Vector2 from = PopulationCollisionMath.Flat(start), to = PopulationCollisionMath.Flat(target);
        foreach (WhiteboxPopulation population in populations)
        {
            if (population == null || population.IsAir || population == owner || population.leader == null) continue;
            var other = population.leader;
            if (!other.visual.gameObject.activeInHierarchy) continue;
            float required = clearance + PopulationTileSpace.Clearance(other.size) + Mathf.Max(0f, settings.populationGap);
            if (!PopulationCollisionMath.AllowsMove(from, to, PopulationCollisionMath.Flat(other.groundPosition), required))
                return false;
        }
        foreach (PopulationPlayerBarrier player in PopulationPlayerBarrier.Players)
        {
            if (player == null || !player.isActiveAndEnabled) continue;
            float required = clearance + player.Radius + Mathf.Max(0f, settings.playerGap);
            if (!PopulationCollisionMath.AllowsMove(from, to, PopulationCollisionMath.Flat(player.transform.position), required))
                return false;
        }
        return true;
    }

    private void Update()
    {
        bool navigationReady = NavigationReady;
        if (!navigationReady) navigationWasUpdating = true;
        if (navigationReady && (navigationWasUpdating || navigationRevision != gridManager.Navigation.Revision))
        {
            foreach (WhiteboxPopulation population in populations)
                if (population != null) population.NavigationChanged();
            navigationRevision = gridManager.Navigation.Revision;
            navigationWasUpdating = false;
        }
        for (int i = populations.Count - 1; i >= 0; i--)
        {
            WhiteboxPopulation population = populations[i];
            if (population == null) { populations.RemoveAt(i); continue; }
            if (!gridManager.TryGetTile(population.TileId, out MapTileInstance tile) || tile != population.tile)
            { DestroyPopulation(population); continue; }
            if (!population.gameObject.activeInHierarchy || (!population.IsAir && !navigationReady)) continue;
            float remaining = Mathf.Min(Time.deltaTime, 0.2f);
            while (remaining > 0f)
            {
                float step = Mathf.Min(remaining, 0.04f);
                population.Tick(step); remaining -= step;
            }
        }
    }
    private void OnTilesChanged()
    {
        // TilesChanged precedes the bake. Rebuild local caches only after the
        // completed navigation revision, never against the previous mesh.
        navigationWasUpdating = true;
    }
    public void DestroyPopulation(WhiteboxPopulation population)
    {
        if (population == null || !populations.Contains(population)) return;
        Unregister(population); population.gameObject.SetActive(false); Destroy(population.gameObject);
    }
    internal void Unregister(WhiteboxPopulation population)
    { populations.Remove(population); debugPopulations.Remove(population); }
    private void OnDestroy()
    {
        controllers.Remove(this);
        foreach (WhiteboxPopulation population in populations.ToArray())
            if (population != null) { population.gameObject.SetActive(false); Destroy(population.gameObject); }
        populations.Clear(); debugPopulations.Clear();
    }

    public void CreateDebugPopulations(Vector2Int tileId) { debugTile = tileId; CreateDebugPopulations(); }
    [ContextMenu("Population Debug/Create two populations (Play Mode)")]
    private void CreateDebugPopulations()
    {
        if (!Application.isPlaying || !NavigationReady)
        { Debug.LogWarning("Enter Play Mode and wait for the map NavMesh to finish, then retry.", this); return; }
        // Retry can complete a partially successful pair without duplicating it.
        while (debugPopulations.Count < 2)
        {
            int count = debugPopulations.Count == 0 ? firstDebugCount : secondDebugCount;
            WhiteboxPopulation population = CreatePopulation(debugTile, count);
            if (population == null) break;
            debugPopulations.Add(population);
        }
        Debug.Log($"Created {debugPopulations.Count}/2 debug populations on {debugTile}.", this);
    }
    [ContextMenu("Population Debug/Change first count (Play Mode)")]
    private void ChangeDebugCount()
    {
        if (Application.isPlaying && debugPopulations.Count > 0 && debugPopulations[0] != null)
            debugPopulations[0].Count = editedDebugCount;
    }
    [ContextMenu("Population Debug/Reverse first leader (Play Mode)")]
    private void ReverseDebugLeader()
    {
        if (!Application.isPlaying || debugPopulations.Count == 0 || debugPopulations[0] == null) return;
        if (!debugPopulations[0].RequestReverse())
            Debug.LogWarning("No terrain-valid reverse target here; retry away from the tile boundary.", this);
    }
    [ContextMenu("Population Debug/Pause wandering (Play Mode)")]
    private void PauseDebugWandering()
    {
        if (!Application.isPlaying) return;
        foreach (WhiteboxPopulation population in debugPopulations)
            if (population != null) population.PauseWandering(true);
    }
    [ContextMenu("Population Debug/Resume wandering (Play Mode)")]
    private void ResumeDebugWandering()
    {
        if (!Application.isPlaying) return;
        foreach (WhiteboxPopulation population in debugPopulations)
            if (population != null) population.PauseWandering(false);
    }
    [ContextMenu("Population Debug/Validate live movement (Play Mode)")]
    private void ValidateDebugMovement()
    {
        if (!Application.isPlaying || !NavigationReady) return;
        int valid = 0;
        foreach (WhiteboxPopulation population in debugPopulations)
        {
            if (population == null) continue;
            if (population.ValidateMovement(out string problem)) valid++;
            else Debug.LogError($"{population.name}: {problem}", population);
        }
        Debug.Log($"Live movement: {valid}/{debugPopulations.Count} populations passed.", this);
    }
    [ContextMenu("Population Debug/Clear debug populations (Play Mode)")]
    private void ClearDebugPopulations()
    {
        if (!Application.isPlaying) return;
        foreach (WhiteboxPopulation population in debugPopulations.ToArray()) DestroyPopulation(population);
    }
}
