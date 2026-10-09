using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Whitebox-only controller: seeds one chicken population on the initial tile
// and keeps visual queues bound to living ecological populations.
public sealed class WhiteboxBiowebTestSetup : MonoBehaviour
{
    [SerializeField] private MapGridManager grid;
    [SerializeField] private MapSimulationBridge bridge;
    [SerializeField] private SimulationController simulation;
    [SerializeField] private PopulationMovementController movement;
    [Header("JSON order: deer, chicken, fierce deer, civet, sparrow lion")]
    [SerializeField] private SpeciesData[] species = new SpeciesData[5];
    [SerializeField] private Color[] speciesColors = new Color[5];
    [SerializeField] private SpeciesData initialChickenSpecies;
    [SerializeField, Min(1)] private int initialChickenCount = 160;

    private readonly Dictionary<PopulationData, WhiteboxPopulation> visuals =
        new Dictionary<PopulationData, WhiteboxPopulation>();
    private readonly Dictionary<PopulationData, float> retryAt =
        new Dictionary<PopulationData, float>();
    private float nextSync;

    public bool TryGetVisual(PopulationData data, out WhiteboxPopulation visual) =>
        visuals.TryGetValue(data, out visual) && visual != null;

    private void Awake()
    {
        if (grid == null) grid = GetComponent<MapGridManager>();
        if (bridge == null) bridge = GetComponent<MapSimulationBridge>();
        if (simulation == null) simulation = GetComponent<SimulationController>();
        if (movement == null) movement = GetComponent<PopulationMovementController>();
    }

    private IEnumerator Start()
    {
        // MapGridManager creates the origin tile in Start; the bridge then
        // registers it with the simulation before this coroutine resumes.
        yield return null;
        if (grid == null || bridge == null || simulation == null || movement == null ||
            initialChickenSpecies == null || species == null || species.Length != 5 || speciesColors == null ||
            speciesColors.Length != 5)
        {
            Debug.LogError("Whitebox bioweb test setup has missing references.", this);
            enabled = false;
            yield break;
        }
        for (int i = 0; i < species.Length; i++)
            if (species[i] == null)
            {
                Debug.LogError("Whitebox bioweb test species are incomplete.", this);
                enabled = false;
                yield break;
            }

        simulation.SetEvolutionNetworkSpecies(species);
        if (!grid.TryGetTile(Vector2Int.zero, out MapTileInstance initialTile) ||
            initialTile.Block == null)
        {
            Debug.LogError("Whitebox initial grassland tile is unavailable.", this);
            enabled = false;
            yield break;
        }
        Seed(initialTile, initialChickenSpecies);
        nextSync = 0f;
    }

    private void Seed(MapTileInstance tile, SpeciesData chicken)
    {
        if (tile == null || tile.Block == null) return;
        BlockInfo block = tile.Block;
        var edits = new List<SimulationPopulationEdit>();
        if (block.community != null)
            foreach (PopulationData existing in block.community)
                if (existing != null)
                    edits.Add(new SimulationPopulationEdit { Resident = existing, Draft = existing });
        if (block.community == null || !block.community.Exists(p => p != null && p.species == chicken))
        {
            var population = new PopulationData
            {
                species = chicken,
                speciesAmount = initialChickenCount,
                lineageName = chicken.SpeciesName,
                ecologicalNiche = EcologicalNiche.Land,
                fitTemperature = chicken.baseFitTemperature,
                fitHumidity = chicken.baseFitHumidity,
                size = chicken.baseSize,
                movementAbility = chicken.baseMovementAbility,
                fertility = chicken.baseFertility,
                habitatNiche = chicken.baseHabitatNiche,
                trophicLevel = chicken.trophicLevel,
                trophicLevelInitialized = true
            };
            edits.Add(new SimulationPopulationEdit { Draft = population });
        }
        if (!simulation.ApplyCommunityEdits(block, edits))
            Debug.LogError($"Could not seed community at {tile.Coordinate}.", this);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextSync || grid == null || movement == null) return;
        nextSync = Time.unscaledTime + 0.25f;
        var living = new HashSet<PopulationData>();
        int creations = 0;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            if (tile == null || tile.Block == null || tile.Block.community == null) continue;
            foreach (PopulationData data in tile.Block.community)
            {
                if (data == null || data.speciesAmount <= 0) continue;
                int index = System.Array.IndexOf(species, data.species);
                living.Add(data);
                if (visuals.TryGetValue(data, out WhiteboxPopulation visual) && visual != null &&
                    visual.TileId == tile.Coordinate)
                {
                    if (visual.Count != data.speciesAmount) visual.Count = data.speciesAmount;
                    continue;
                }
                if (visual != null) movement.DestroyPopulation(visual);
                visuals.Remove(data);
                if (creations >= 2 || retryAt.TryGetValue(data, out float time) &&
                    Time.unscaledTime < time) continue;
                creations++;
                visual = movement.CreatePopulation(tile.Coordinate, data.speciesAmount);
                if (visual == null) { retryAt[data] = Time.unscaledTime + 3f; continue; }
                visual.BindEcologicalPopulation(data,
                    index >= 0 ? speciesColors[index] : Color.white);
                string name = data.species != null ? data.species.SpeciesName : data.lineageName;
                visual.name = $"{name}_{tile.Coordinate.x}_{tile.Coordinate.y}";
                visuals[data] = visual;
                retryAt.Remove(data);
            }
        }
        foreach (PopulationData data in new List<PopulationData>(visuals.Keys))
            if (!living.Contains(data))
            {
                movement.DestroyPopulation(visuals[data]);
                visuals.Remove(data);
                retryAt.Remove(data);
            }
    }

    private void OnDestroy()
    {
        if (movement == null) return;
        foreach (WhiteboxPopulation visual in visuals.Values)
            if (visual != null) movement.DestroyPopulation(visual);
        visuals.Clear();
    }
}
