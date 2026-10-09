using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Whitebox-only setup: configures the test food web and seeds the initial chicken.
public sealed class WhiteboxBiowebTestSetup : MonoBehaviour
{
    [SerializeField] private MapGridManager grid;
    [SerializeField] private MapSimulationBridge bridge;
    [SerializeField] private SimulationController simulation;
    [Header("JSON order: deer, chicken, fierce deer, civet, sparrow lion")]
    [SerializeField] private SpeciesData[] species = new SpeciesData[5];
    [SerializeField] private SpeciesData initialChickenSpecies;
    [SerializeField, Min(1)] private int initialChickenCount = 160;

    private void Awake()
    {
        if (grid == null) grid = GetComponent<MapGridManager>();
        if (bridge == null) bridge = GetComponent<MapSimulationBridge>();
        if (simulation == null) simulation = GetComponent<SimulationController>();
    }

    private IEnumerator Start()
    {
        // MapGridManager creates the origin tile in Start; the bridge then
        // registers it with the simulation before this coroutine resumes.
        yield return null;
        if (grid == null || bridge == null || simulation == null ||
            initialChickenSpecies == null || species == null || species.Length != 5)
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
}
