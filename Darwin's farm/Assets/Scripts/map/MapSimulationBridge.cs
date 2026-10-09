using System.Collections.Generic;
using UnityEngine;

// Keeps the placed square grid and the simulation's block graph in sync.
public sealed class MapSimulationBridge : MonoBehaviour
{
    public event System.Action BlockGraphSynchronized;
    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down
    };

    [SerializeField] private MapGridManager gridManager;
    [SerializeField] private SimulationController simulationController;
    [SerializeField] private SimulationTime simulationTime;

    private void Awake()
    {
        if (gridManager == null) gridManager = GetComponent<MapGridManager>();
        if (simulationController == null)
            simulationController = GetComponent<SimulationController>();
        if (simulationTime == null) simulationTime = GetComponent<SimulationTime>();

        if (gridManager == null || simulationController == null || simulationTime == null)
        {
            Debug.LogError("Map simulation bridge requires grid, controller and time.", this);
            enabled = false;
            return;
        }

        simulationController.BindTime(simulationTime);
        // Runtime attachment also supports existing scenes without changing collaborators' map setup.
        if (GetComponent<SimulationEnvironmentController>() == null)
            gameObject.AddComponent<SimulationEnvironmentController>();
        if (GetComponent<EnvironmentTechnologyController>() == null)
            gameObject.AddComponent<EnvironmentTechnologyController>();
    }

    private void OnEnable()
    {
        if (gridManager != null) gridManager.TilesChanged += Synchronize;
    }

    private void Start() => Synchronize();

    private void OnDisable()
    {
        if (gridManager != null) gridManager.TilesChanged -= Synchronize;
    }

    public bool TryGetBlock(Vector2Int coordinate, out BlockInfo block)
    {
        block = null;
        if (gridManager == null ||
            !gridManager.TryGetTile(coordinate, out MapTileInstance tile))
            return false;
        block = tile.Block;
        return block != null;
    }

    // Rebuild from coordinates so every edge is symmetric and diagonals are excluded.
    public void Synchronize()
    {
        if (gridManager == null || simulationController == null) return;

        List<MapTileInstance> tiles = new List<MapTileInstance>(gridManager.GetPlacedTiles());
        List<BlockInfo> blocks = new List<BlockInfo>(tiles.Count);
        bool existingHeightChanged = false;

        foreach (MapTileInstance tile in tiles)
        {
            BlockInfo block = tile.Block;
            if (block == null)
            {
                block = tile.gameObject.AddComponent<BlockInfo>();
                InitializeBlock(block, tile.Definition, tile.HeightLevel);
            }
            else if (block.elevation != tile.HeightLevel)
            {
                block.elevation = tile.HeightLevel;
                existingHeightChanged = true;
            }

            if (block.community == null)
                block.community = new List<PopulationData>();
            blocks.Add(block);
        }

        foreach (MapTileInstance tile in tiles)
        {
            List<BlockInfo> neighbors = new List<BlockInfo>(4);
            foreach (Vector2Int direction in Directions)
                if (gridManager.TryGetNeighbor(tile.Coordinate, direction,
                        out MapTileInstance neighbor) && neighbor.Block != null)
                    neighbors.Add(neighbor.Block);
            tile.Block.SetNeighbors(neighbors);
        }

        simulationController.SetBlocks(blocks);
        if (existingHeightChanged)
            simulationController.NotifyEnvironmentChanged();
        BlockGraphSynchronized?.Invoke();
    }

    private static void InitializeBlock(BlockInfo block, MapTileDefinition preset,
        int elevation)
    {
        var defaults = SimulationEnvironmentController.InitialDefaults(preset, elevation);
        bool water = preset.initialWaterCoverage != WaterCoverage.Land;
        block.elevation = elevation;
        block.temperature = defaults.Temperature;
        block.humidity = defaults.Humidity;
        block.maxPlantBiomass = water ? DarwinFarm.Environment.EnvironmentRules.WaterPlantCapacity :
            DarwinFarm.Environment.EnvironmentRules.PlantCapacity;
        block.plantBiomass = water ? DarwinFarm.Environment.EnvironmentRules.WaterDefaultRecovery : defaults.Recovery;
        block.habitatRecovery = water ? DarwinFarm.Environment.EnvironmentRules.WaterDefaultRecovery : defaults.Recovery;
        block.waterCoverage = preset.initialWaterCoverage;
        block.community = new List<PopulationData>();
    }
}
