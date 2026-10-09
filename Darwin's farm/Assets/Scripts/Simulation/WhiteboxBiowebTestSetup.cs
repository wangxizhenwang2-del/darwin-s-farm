using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Whitebox-only controller: seeds the eight terrain cases and keeps one visual
// queue bound to each living ecological population.
public sealed class WhiteboxBiowebTestSetup : MonoBehaviour
{
    [SerializeField] private MapGridManager grid;
    [SerializeField] private MapSimulationBridge bridge;
    [SerializeField] private SimulationController simulation;
    [SerializeField] private PopulationMovementController movement;
    [Header("Terrain assets")]
    [SerializeField] private MapTileDefinition forest, rainforest, desert, highland,
        volcano, tundra, snowMountain;
    [Header("JSON order: deer, chicken, fierce deer, civet, sparrow lion")]
    [SerializeField] private SpeciesData[] species = new SpeciesData[5];
    [SerializeField] private Color[] speciesColors = new Color[5];
    [SerializeField, Min(1)] private int herbivoresPerTile = 160;
    [SerializeField, Min(1)] private int firstPredatorsPerTile = 25;
    [SerializeField, Min(1)] private int topPredatorsPerTile = 8;

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
            species == null || species.Length != 5 || speciesColors == null ||
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
        Place(new Vector2Int(1, 0), forest);
        Place(new Vector2Int(2, 0), rainforest);
        Place(new Vector2Int(0, 1), desert);
        Place(new Vector2Int(1, 1), highland);
        Place(new Vector2Int(2, 1), volcano);
        Place(new Vector2Int(0, 2), tundra);
        Place(new Vector2Int(1, 2), snowMountain);
        bridge.Synchronize();
        foreach (MapTileInstance tile in grid.GetPlacedTiles()) Seed(tile);
        nextSync = 0f;
    }

    private void Place(Vector2Int coordinate, MapTileDefinition terrain)
    {
        if (terrain == null)
        {
            Debug.LogError($"Missing terrain asset at {coordinate}.", this);
            return;
        }
        if (!grid.TryGetTile(coordinate, out _) && !grid.TryPlaceTile(coordinate, terrain))
            Debug.LogError($"Could not place test terrain at {coordinate}.", this);
    }

    private void Seed(MapTileInstance tile)
    {
        if (tile == null || tile.Block == null) return;
        BlockInfo block = tile.Block;
        var edits = new List<SimulationPopulationEdit>();
        if (block.community != null)
            foreach (PopulationData existing in block.community)
                if (existing != null)
                    edits.Add(new SimulationPopulationEdit { Resident = existing, Draft = existing });
        foreach (SpeciesData type in species)
        {
            if (block.community != null && block.community.Exists(p => p != null && p.species == type))
                continue;
            int amount = type.trophicLevel == 0 ? herbivoresPerTile :
                type.trophicLevel == 1 ? firstPredatorsPerTile : topPredatorsPerTile;
            var population = new PopulationData
            {
                species = type,
                speciesAmount = amount,
                lineageName = type.SpeciesName,
                ecologicalNiche = EcologicalNiche.Land,
                fitTemperature = type.baseFitTemperature,
                fitHumidity = type.baseFitHumidity,
                size = type.baseSize,
                movementAbility = type.baseMovementAbility,
                fertility = type.baseFertility,
                habitatNiche = type.baseHabitatNiche,
                trophicLevel = type.trophicLevel,
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
                if (index < 0) continue;
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
                visual.BindEcologicalPopulation(data, speciesColors[index]);
                visual.name = $"{data.species.SpeciesName}_{tile.Coordinate.x}_{tile.Coordinate.y}";
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
