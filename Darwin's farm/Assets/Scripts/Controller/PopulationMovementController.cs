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
    public PopulationMovementSettings Settings => settings;
    public Camera ViewCamera => viewCamera;
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
                        if (population != null && population.gameObject.activeInHierarchy && population.members != null) return true;
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
                if (population == null || population.members == null) continue;
                foreach (WhiteboxPopulation.Member member in population.members)
                {
                    if (!member.visual.gameObject.activeInHierarchy) continue;
                    float required = radius + PopulationTileSpace.Clearance(member.size) +
                        Mathf.Max(0f, controller.settings.playerGap);
                    if (!PopulationCollisionMath.AllowsMove(PopulationCollisionMath.Flat(from),
                        PopulationCollisionMath.Flat(to), PopulationCollisionMath.Flat(member.groundPosition), required)) return false;
                }
            }
        }
        return true;
    }
    private bool NavigationReady => gridManager != null && gridManager.Navigation != null && !gridManager.Navigation.IsUpdating;

    public WhiteboxPopulation CreatePopulation(Vector2Int tileId, int initialCount)
    {
        if (!Application.isPlaying || !NavigationReady || !gridManager.TryGetTile(tileId, out MapTileInstance tile)) return null;
        NavMeshSurface surface = gridManager.Navigation.GetComponent<NavMeshSurface>();
        if (surface == null || surface.navMeshData == null) return null;
        Physics.SyncTransforms();
        var filter = new NavMeshQueryFilter { agentTypeID = surface.agentTypeID, areaMask = walkableAreaMask };
        var space = new PopulationTileSpace(tile, filter);
        if (!space.IsReady || settings.leaderSize <= 0f || settings.followerSize <= 0f) return null;
        // Bounded packing: no partial entities and no unchecked fallback positions.
        for (int attempt = 0; attempt < 64; attempt++)
        {
            Bounds bounds = space.Bounds;
            Vector3 leader = new Vector3(Random.Range(bounds.min.x, bounds.max.x), bounds.max.y,
                Random.Range(bounds.min.z, bounds.max.z));
            if (!space.CanTraverse(leader, leader, settings.leaderSize) || !SpawnSeparated(leader, settings.leaderSize)) continue;
            var positions = new Vector3[4]; positions[0] = leader;
            bool packed = false;
            float startingYaw = Random.Range(0f, Mathf.PI * 2f);
            for (int direction = 0; direction < 16; direction++)
            {
                float yaw = startingYaw + direction * Mathf.PI * 0.125f;
                Vector3 backward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                float distance = 0f;
                bool fits = true;
                for (int member = 1; member < positions.Length; member++)
                {
                    float predecessorSize = member == 1 ? settings.leaderSize : settings.followerSize;
                    distance += Mathf.Max(settings.spacing, PopulationTileSpace.Clearance(settings.followerSize) +
                        PopulationTileSpace.Clearance(predecessorSize) + Mathf.Max(0f, settings.memberGap) + 0.08f);
                    Vector3 candidate = leader + backward * distance;
                    if (!space.CanTraverse(candidate, candidate, settings.followerSize) ||
                        !SpawnSeparated(candidate, settings.followerSize) ||
                        !space.CanTraverse(positions[member - 1], candidate, settings.followerSize))
                    { fits = false; break; }
                    positions[member] = candidate;
                }
                if (fits) { packed = true; break; }
            }
            if (!packed) continue;
            GameObject root = new GameObject("Population"); root.transform.SetParent(transform, true);
            WhiteboxPopulation population = root.AddComponent<WhiteboxPopulation>();
            Color color = Color.HSVToRGB(Mathf.Repeat(populations.Count * 0.381966f + 0.05f, 1f), 0.7f, 1f);
            population.Initialize(this, tile, space, initialCount, positions, color);
            populations.Add(population);
            PopulationPlayerBarrier.ActivateAll();
            return population;
        }
        Debug.LogWarning($"No safe space for four population members on tile {tileId}; creation skipped.", this);
        return null;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    private bool SpawnSeparated(Vector3 point, float size)
    {
        foreach (PopulationPlayerBarrier player in PopulationPlayerBarrier.Players)
            if (player != null && player.isActiveAndEnabled &&
                HorizontalDistance(point, player.transform.position) <
                PopulationTileSpace.Clearance(size) + player.Radius + Mathf.Max(0f, settings.playerGap)) return false;
        foreach (WhiteboxPopulation population in populations)
            if (population != null && population.members != null)
                foreach (WhiteboxPopulation.Member member in population.members)
                    if (HorizontalDistance(point, member.groundPosition) <
                        PopulationTileSpace.Clearance(size) + PopulationTileSpace.Clearance(member.size) +
                        Mathf.Max(0f, settings.populationGap)) return false;
        return true;
    }

    internal bool HasSeparation(WhiteboxPopulation owner, WhiteboxPopulation.Member moving, Vector3 target)
        => HasSeparation(owner, moving, moving.groundPosition, target);

    internal bool HasSeparation(WhiteboxPopulation owner, WhiteboxPopulation.Member moving, Vector3 start, Vector3 target)
    {
        foreach (WhiteboxPopulation population in populations)
        {
            if (population == null || population.members == null) continue;
            foreach (WhiteboxPopulation.Member member in population.members)
            {
                if (member == moving || !member.visual.gameObject.activeInHierarchy) continue;
                float required = PopulationTileSpace.Clearance(moving.size) +
                    PopulationTileSpace.Clearance(member.size) + Mathf.Max(0f,
                        population == owner ? settings.memberGap : settings.populationGap);
                Vector2 from = new Vector2(start.x, start.z);
                Vector2 to = new Vector2(target.x, target.z);
                Vector2 other = new Vector2(member.groundPosition.x, member.groundPosition.z);
                if (!PopulationCollisionMath.AllowsMove(from, to, other, required)) return false;
            }
        }
        foreach (PopulationPlayerBarrier player in PopulationPlayerBarrier.Players)
        {
            if (player == null || !player.isActiveAndEnabled) continue;
            float required = PopulationTileSpace.Clearance(moving.size) + player.Radius + Mathf.Max(0f, settings.playerGap);
            if (!PopulationCollisionMath.AllowsMove(PopulationCollisionMath.Flat(start),
                PopulationCollisionMath.Flat(target), PopulationCollisionMath.Flat(player.transform.position), required)) return false;
        }
        return true;
    }

    private void Update()
    {
        if (!NavigationReady) { navigationWasUpdating = true; return; }
        if (navigationWasUpdating) { OnTilesChanged(); navigationWasUpdating = false; }
        for (int i = populations.Count - 1; i >= 0; i--)
        {
            WhiteboxPopulation population = populations[i];
            if (population == null) { populations.RemoveAt(i); continue; }
            if (!gridManager.TryGetTile(population.TileId, out MapTileInstance tile) || tile != population.tile)
            { DestroyPopulation(population); continue; }
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
        foreach (WhiteboxPopulation population in populations)
            if (population != null) population.NavigationChanged();
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
    [ContextMenu("Population Debug/Pause wandering and regroup (Play Mode)")]
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
    [ContextMenu("Population Debug/Validate live formation (Play Mode)")]
    private void ValidateDebugFormation()
    {
        if (!Application.isPlaying || !NavigationReady) return;
        int valid = 0;
        foreach (WhiteboxPopulation population in debugPopulations)
        {
            if (population == null) continue;
            if (population.ValidateFormation(out string problem)) valid++;
            else Debug.LogError($"{population.name}: {problem}", population);
        }
        Debug.Log($"Live formation: {valid}/{debugPopulations.Count} populations passed.", this);
    }
    [ContextMenu("Population Debug/Clear debug populations (Play Mode)")]
    private void ClearDebugPopulations()
    {
        if (!Application.isPlaying) return;
        foreach (WhiteboxPopulation population in debugPopulations.ToArray()) DestroyPopulation(population);
    }
}
