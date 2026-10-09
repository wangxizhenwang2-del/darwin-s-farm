using System;
using System.Collections.Generic;
using UnityEngine;

// Read-only view of the living land populations in placed tiles.
// Ecology owns PopulationData; this component owns only WhiteboxPopulation objects.
[DisallowMultipleComponent]
public sealed partial class PopulationVisualPresenter : MonoBehaviour
{
    [SerializeField] private MapGridManager grid;
    [SerializeField] private PopulationMovementController movement;
    [SerializeField] private SimulationController simulation;
    [Header("Optional colors for known species")]
    [SerializeField] private SpeciesData[] presetSpecies = new SpeciesData[0];
    [SerializeField] private Color[] presetColors = new Color[0];
    [SerializeField, Min(0.05f)] private float syncInterval = 0.25f;
    [SerializeField, Min(1)] private int maxCreationsPerSync = 2;
    [SerializeField, Min(0.1f)] private float failedCreationRetry = 3f;

    private readonly Dictionary<string, WhiteboxPopulation> visuals =
        new Dictionary<string, WhiteboxPopulation>(StringComparer.Ordinal);
    private readonly Dictionary<string, float> retryAt =
        new Dictionary<string, float>(StringComparer.Ordinal);
    private readonly HashSet<string> living = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> stale = new List<string>();
    private float nextSync;

    public bool TryGetVisual(PopulationData data, out WhiteboxPopulation visual)
    {
        if (data != null && visuals.TryGetValue(data.PopulationId, out visual) &&
            visual != null && visual.EcologicalPopulation == data)
            return true;
        visual = null;
        return false;
    }

    private void Awake()
    {
        if (grid == null) grid = GetComponent<MapGridManager>();
        if (movement == null) movement = GetComponent<PopulationMovementController>();
        if (simulation == null) simulation = GetComponent<SimulationController>();
    }

    private void OnEnable()
    {
        if (simulation != null) simulation.OnDaySimulated += OnDaySimulated;
        SubscribeTransitions();
        nextSync = 0f;
    }

    private void OnDisable()
    {
        if (simulation != null) simulation.OnDaySimulated -= OnDaySimulated;
        UnsubscribeTransitions();
        ClearTransitions();
        ClearVisuals();
    }

    private void OnDaySimulated(int day)
    {
        StartPendingTransitions();
        SyncNow();
    }

    private void Update()
    {
        StartPendingTransitions();
        CheckTransitions();
        if (Time.unscaledTime < nextSync) return;
        SyncNow();
    }

    public void SyncNow()
    {
        if (!Application.isPlaying || grid == null || movement == null) return;
        nextSync = Time.unscaledTime + Mathf.Max(0.05f, syncInterval);
        living.Clear();
        int creations = 0;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            if (tile == null || tile.Block == null || tile.Block.community == null) continue;
            foreach (PopulationData data in tile.Block.community)
            {
                if (data == null || data.speciesAmount <= 0 ||
                    data.ecologicalNiche != EcologicalNiche.Land) continue;
                string id = data.PopulationId;
                if (!living.Add(id)) continue;

                if (visuals.TryGetValue(id, out WhiteboxPopulation visual) && visual != null &&
                    visual.TileId == tile.Coordinate)
                {
                    if (visual.EcologicalPopulation != data)
                        visual.BindEcologicalPopulation(data, ColorFor(data));
                    if (transitionHolds.TryGetValue(id, out TransitionHold hold) &&
                        hold.CountBefore == 0) continue;
                    int count = hold == null ? data.speciesAmount : hold.CountBefore;
                    if (visual.Count != count) visual.Count = count;
                    continue;
                }

                if (visual != null) movement.DestroyPopulation(visual);
                visuals.Remove(id);
                if (creations >= Mathf.Max(1, maxCreationsPerSync) ||
                    retryAt.TryGetValue(id, out float retryTime) && Time.unscaledTime < retryTime)
                    continue;

                creations++;
                bool held = transitionHolds.TryGetValue(id, out TransitionHold pending);
                int initialCount = held && pending.CountBefore > 0 ?
                    pending.CountBefore : data.speciesAmount;
                visual = movement.CreatePopulation(tile.Coordinate, initialCount);
                if (visual == null)
                {
                    retryAt[id] = Time.unscaledTime + Mathf.Max(0.1f, failedCreationRetry);
                    continue;
                }

                visual.BindEcologicalPopulation(data, ColorFor(data));
                if (held && pending.CountBefore == 0)
                {
                    visual.PauseWandering(true);
                    visual.gameObject.SetActive(false);
                    pending.StagedView = visual;
                }
                string name = data.species != null ? data.species.SpeciesName : data.lineageName;
                visual.name = $"{(string.IsNullOrEmpty(name) ? "Population" : name)}_{tile.Coordinate.x}_{tile.Coordinate.y}";
                visuals[id] = visual;
                retryAt.Remove(id);
            }
        }

        stale.Clear();
        foreach (string id in visuals.Keys)
            if (!living.Contains(id)) stale.Add(id);
        foreach (string id in stale)
        {
            movement.DestroyPopulation(visuals[id]);
            visuals.Remove(id);
        }
        stale.Clear();

        // A removed population must not retain a retry record indefinitely.
        foreach (string id in retryAt.Keys)
            if (!living.Contains(id)) stale.Add(id);
        foreach (string id in stale) retryAt.Remove(id);
        stale.Clear();
    }

    private Color ColorFor(PopulationData data)
    {
        int limit = Math.Min(presetSpecies == null ? 0 : presetSpecies.Length,
            presetColors == null ? 0 : presetColors.Length);
        for (int i = 0; i < limit; i++)
            if (data.species != null && data.species == presetSpecies[i]) return presetColors[i];

        // Derived species have no preset asset. Hash their persistent species ID so
        // their color does not change when population views are created or destroyed.
        string key = !string.IsNullOrEmpty(data.speciesId) ? data.speciesId :
            data.species != null ? data.species.name : data.lineageName;
        if (string.IsNullOrEmpty(key)) key = "Population";
        uint hash = 2166136261;
        foreach (char character in key)
            hash = (hash ^ character) * 16777619;
        return Color.HSVToRGB(hash / (float)uint.MaxValue, 0.7f, 1f);
    }

    private void ClearVisuals()
    {
        if (movement != null)
            foreach (WhiteboxPopulation visual in visuals.Values)
                if (visual != null) movement.DestroyPopulation(visual);
        visuals.Clear();
        retryAt.Clear();
        living.Clear();
        stale.Clear();
    }
}
