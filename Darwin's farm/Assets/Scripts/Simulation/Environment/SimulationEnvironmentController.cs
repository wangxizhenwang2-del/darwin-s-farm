using System;
using System.Collections.Generic;
using UnityEngine;
using DarwinFarm.Environment;

public sealed class EnvironmentReadSnapshot
{
    public EnvironmentSnapshot Environment { get; }
    public float PlantStock { get; }
    public int PlantCapacity => Environment.IsWater ? EnvironmentRules.WaterPlantCapacity : EnvironmentRules.PlantCapacity;
    public EnvironmentReadSnapshot(EnvironmentSnapshot environment, float plantStock)
    { Environment = environment; PlantStock = plantStock; }
}

public sealed class EnvironmentOperationPreview
{
    public EnvironmentReadSnapshot Before { get; }
    public double Target { get; }
    public TerrainKind EstimatedTerrain { get; }
    public HabitatState EstimatedHabitat { get; }
    public int EnterDays { get; }
    public int HoldDays { get; }
    public int ReturnDays { get; }
    internal EnvironmentOperationPreview(EnvironmentReadSnapshot before, double target, HabitatState habitat,
        int enter, int hold, int returning)
    { Before = before; Target = target; EstimatedHabitat = habitat;
      EstimatedTerrain = EnvironmentRules.VisualTerrain(habitat);
      EnterDays = enter; HoldDays = hold; ReturnDays = returning; }
}

// Controller is the sole bridge between the pure environment Model and live ecology/map data.
// Visuals consume immutable snapshots.
[DisallowMultipleComponent]
public sealed class SimulationEnvironmentController : MonoBehaviour
{
    private MapGridManager grid;
    private MapSimulationBridge bridge;
    private SimulationController simulation;
    private SimulationTime clock;
    private EnvironmentWorld world;
    public EnvironmentWorld.SaveState CaptureSaveState() => world.CaptureState();
    public void RestoreSaveState(EnvironmentWorld.SaveState state)
    {
        Synchronize();
        world.RestoreState(state);
        Publish();
    }
    private readonly Dictionary<GridPosition, BlockInfo> blocks = new Dictionary<GridPosition, BlockInfo>();
    public event Action<IReadOnlyList<EnvironmentReadSnapshot>> Changed;
    public event Action<TerrainKind, Vector2Int> TerrainUnlocked;
    public event Action WaterUnlocked;
    public event Action UnlocksRestored;
    private readonly HashSet<TerrainKind> unlockedTerrain = new HashSet<TerrainKind> { TerrainKind.Desert };
    private bool waterUnlocked;
    private bool discoveryReady;
    private bool restoringSave;
    private const string DeerId = "fd1c574bfd1247b2933cdfbc54758cf8";
    private const string TameDeerId = "b2afc73aa25c41b18fd8f5ed4f3bc62a";
    public bool WaterIsUnlocked => waterUnlocked;
    public bool IsUnlocked(MapTileDefinition definition) => definition != null &&
        (definition.initialWaterCoverage != WaterCoverage.Land ? waterUnlocked :
            unlockedTerrain.Contains(TerrainFromPreset(definition.biome)));
    public List<TerrainKind> CaptureTerrainUnlocks() => new List<TerrainKind>(unlockedTerrain);
    public void BeginSaveRestore() => restoringSave = true;
    public void RestoreUnlocks(List<TerrainKind> terrain, bool water, bool legacy)
    {
        unlockedTerrain.Clear();
        unlockedTerrain.Add(TerrainKind.Desert);
        if (terrain != null) foreach (TerrainKind kind in terrain)
            if (Enum.IsDefined(typeof(TerrainKind), kind)) unlockedTerrain.Add(kind);
        if (legacy && grid != null) foreach (MapTileInstance tile in grid.GetPlacedTiles())
            if (tile.Block != null && !HabitatTopology.IsWater(tile.Block))
                unlockedTerrain.Add(TerrainFromPreset(tile.Biome));
        waterUnlocked = water;
        restoringSave = false;
        UnlocksRestored?.Invoke();
    }
    private void OnSpeciesUnlocked(SpeciesData species)
    {
        if (restoringSave || waterUnlocked || species == null ||
            (species.SpeciesId != DeerId && species.SpeciesId != TameDeerId)) return;
        waterUnlocked = true;
        WaterUnlocked?.Invoke();
    }

    private void Awake()
    {
        grid = GetComponent<MapGridManager>(); bridge = GetComponent<MapSimulationBridge>();
        simulation = GetComponent<SimulationController>(); clock = GetComponent<SimulationTime>();
        world = new EnvironmentWorld(clock != null ? clock.currentDay : 0);
    }
    private void OnEnable()
    {
        if (bridge != null) bridge.BlockGraphSynchronized += Synchronize;
        if (simulation != null) simulation.OnBeforeDaySimulated += Advance;
        if (simulation != null) simulation.OnDaySimulated += NotifyReaders;
        if (clock != null) clock.OnDayReset += RebaseClock;
        if (simulation != null) simulation.OnSpeciesUnlocked += OnSpeciesUnlocked;
    }
    private void Start() { Synchronize(); discoveryReady = true; }
    private void OnDisable()
    {
        if (bridge != null) bridge.BlockGraphSynchronized -= Synchronize;
        if (simulation != null) simulation.OnBeforeDaySimulated -= Advance;
        if (simulation != null) simulation.OnDaySimulated -= NotifyReaders;
        if (clock != null) clock.OnDayReset -= RebaseClock;
        if (simulation != null) simulation.OnSpeciesUnlocked -= OnSpeciesUnlocked;
    }

    public static TerrainKind TerrainFromPreset(BiomeType type)
    {
        switch (type)
        {
            case BiomeType.Forest: return TerrainKind.Forest;
            case BiomeType.Rainforest: return TerrainKind.Rainforest;
            case BiomeType.Desert: case BiomeType.Sand: return TerrainKind.Desert;
            case BiomeType.Tundra: return TerrainKind.Tundra;
            case BiomeType.Highland: case BiomeType.Rock: return TerrainKind.Highland;
            case BiomeType.SnowMountain: return TerrainKind.SnowMountain;
            case BiomeType.Volcano: return TerrainKind.Volcano;
            default: return TerrainKind.Grassland;
        }
    }
    public static BiomeType BiomeFromTerrain(TerrainKind kind)
    {
        switch (kind)
        {
            case TerrainKind.Forest: return BiomeType.Forest;
            case TerrainKind.Rainforest: return BiomeType.Rainforest;
            case TerrainKind.Grassland: return BiomeType.Grassland;
            case TerrainKind.Desert: return BiomeType.Desert;
            case TerrainKind.Tundra: return BiomeType.Tundra;
            case TerrainKind.Highland: return BiomeType.Highland;
            case TerrainKind.SnowMountain: return BiomeType.SnowMountain;
            case TerrainKind.Volcano: return BiomeType.Volcano;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
    public static TerrainDefaults InitialDefaults(MapTileDefinition preset, int actualElevation)
    {
        var defaults = EnvironmentRules.Defaults(TerrainFromPreset(preset.biome));
        return EnvironmentRules.Defaults(EnvironmentRules.ClassifyHabitat(
            actualElevation, defaults.Temperature, defaults.Humidity));
    }

    public void Synchronize()
    {
        if (grid == null || bridge == null || simulation == null || world == null) return;
        var placed = new List<MapTileInstance>(grid.GetPlacedTiles());
        var byCoordinate = new Dictionary<GridPosition, MapTileInstance>();
        foreach (var tile in placed) if (tile.Block != null) byCoordinate.Add(Position(tile.Coordinate), tile);
        var graph = new List<TopologyNode>();
        foreach (var pair in byCoordinate)
        {
            var neighbors = new List<GridPosition>();
            foreach (var delta in Directions)
            {
                var neighbor = new GridPosition(pair.Key.X + delta.X, pair.Key.Y + delta.Y);
                if (byCoordinate.ContainsKey(neighbor)) neighbors.Add(neighbor);
            }
            graph.Add(new TopologyNode(pair.Key, pair.Value.HeightLevel,
                TerrainFromPreset(pair.Value.Definition.biome), neighbors,
                HabitatTopology.IsWater(pair.Value.Block)));
        }
        world.SynchronizeTopology(graph);
        if (clock != null)
        {
            if (clock.currentDay < world.CurrentDay) world.RebaseClock(clock.currentDay);
            world.AdvanceToDay(clock.currentDay);
        }
        blocks.Clear();
        foreach (var pair in byCoordinate)
        {
            var block = pair.Value.Block;
            blocks.Add(pair.Key, block);
            block.maxPlantBiomass = HabitatTopology.IsWater(block)
                ? EnvironmentRules.WaterPlantCapacity : EnvironmentRules.PlantCapacity;
            block.plantBiomass = Mathf.Clamp(block.plantBiomass, 0, block.maxPlantBiomass);
        }
        Publish();
    }
    private static readonly GridPosition[] Directions =
    { new GridPosition(1, 0), new GridPosition(-1, 0), new GridPosition(0, 1), new GridPosition(0, -1) };
    private static GridPosition Position(Vector2Int coordinate) => new GridPosition(coordinate.x, coordinate.y);
    private void Advance(int day)
    {
        world.AdvanceToDay(day);
        GrowWaterPlants();
        Publish(false);
    }
    private void GrowWaterPlants()
    {
        if (grid == null) return;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            if (!HabitatTopology.IsWater(tile.Block)) continue;
            float before = tile.Block.plantBiomass;
            double recovery = 0;
            if (world.TryRead(Position(tile.Coordinate), out EnvironmentSnapshot state))
                recovery = state.Recovery.Value;
            float capacity = tile.Block.maxPlantBiomass;
            float growth = (float)Math.Min(Math.Max(0, recovery),
                Math.Max(0, capacity - before));
            tile.Block.plantGrowthToday = growth;
            tile.Block.plantBiomass = before + growth;
        }
    }
    private void RebaseClock(int day) => world.RebaseClock(day);
    private void NotifyReaders(int day) => Changed?.Invoke(ReadAll());
    private void Publish(bool notifyReaders = true)
    {
        var snapshots = new List<EnvironmentReadSnapshot>();
        foreach (var state in world.ReadAll())
        {
            if (!blocks.TryGetValue(state.Position, out BlockInfo block)) continue;
            simulation.ApplyEnvironment(block, EnvironmentRules.Round(state.Temperature.Value),
                EnvironmentRules.Round(state.Humidity.Value), EnvironmentRules.Round(state.Recovery.Value));
            if (!state.IsWater && grid != null && grid.TryGetTile(
                    new Vector2Int(state.Position.X, state.Position.Y), out MapTileInstance tile))
            {
                BiomeType next = BiomeFromTerrain(state.Terrain);
                bool transformed = tile.Biome != next;
                tile.SetRuntimeBiome(next);
                if (transformed && discoveryReady && !restoringSave && unlockedTerrain.Add(state.Terrain))
                    TerrainUnlocked?.Invoke(state.Terrain, tile.Coordinate);
            }
            snapshots.Add(new EnvironmentReadSnapshot(state, block.plantBiomass));
        }
        if (notifyReaders) Changed?.Invoke(snapshots.AsReadOnly());
    }
    public bool TryRead(Vector2Int coordinate, out EnvironmentReadSnapshot snapshot)
    {
        snapshot = null;
        var position = Position(coordinate);
        if (world == null || !world.TryRead(position, out var state) || !blocks.TryGetValue(position, out var block)) return false;
        snapshot = new EnvironmentReadSnapshot(state, block.plantBiomass); return true;
    }
    public IReadOnlyList<EnvironmentReadSnapshot> ReadAll()
    {
        var result = new List<EnvironmentReadSnapshot>();
        if (world != null) foreach (var state in world.ReadAll())
            if (blocks.TryGetValue(state.Position, out var block)) result.Add(new EnvironmentReadSnapshot(state, block.plantBiomass));
        return result.AsReadOnly();
    }
    public bool TryPreview(Vector2Int coordinate, SimulationInterventionKind kind, float amount,
        out EnvironmentOperationPreview preview, out string error)
    {
        preview = null; error = null;
        if (!isActiveAndEnabled || !TryRead(coordinate, out var snapshot) || !EnvironmentRules.IsFinite(amount))
        { error = "环境控制器未就绪或目标地块不存在"; return false; }
        var state = snapshot.Environment;
        double target;
        int enter = 0, hold = 0, returning = 0;
        double t = state.Temperature.Value, h = state.Humidity.Value;
        int z = state.Elevation;
        switch (kind)
        {
            case SimulationInterventionKind.Temperature:
            case SimulationInterventionKind.Humidity:
                if (HabitatTopology.IsWater(blocks[state.Position])) { error = "水格不能进行温湿度干预"; return false; }
                if (!EnvironmentRules.IsClimateStrength(amount)) { error = "温湿度强度必须为 ±25 或 ±50"; return false; }
                var attribute = kind == SimulationInterventionKind.Temperature ? EnvironmentAttribute.Temperature : EnvironmentAttribute.Humidity;
                target = EnvironmentRules.Clamp(attribute,
                    (attribute == EnvironmentAttribute.Temperature ? state.Temperature.Value : state.Humidity.Value) + amount);
                if (attribute == EnvironmentAttribute.Temperature) t = target; else h = target;
                enter = EnvironmentRules.TransitionDays;
                hold = EnvironmentRules.ClimateHoldDays;
                returning = EnvironmentRules.ClimateReturnDays;
                break;
            case SimulationInterventionKind.PlantRecovery:
                if (HabitatTopology.IsWater(blocks[state.Position])) { error = "水域植物工程尚未接入，不能按陆地单格处理"; return false; }
                if (!EnvironmentRules.IsRecoveryStrength(amount)) { error = "增量强度必须为 ±25000 或 ±50000"; return false; }
                target = EnvironmentRules.Clamp(EnvironmentAttribute.Recovery, state.Recovery.Value + amount);
                enter = EnvironmentRules.LandRecoveryTransitionDays;
                hold = EnvironmentRules.LandRecoveryHoldDays;
                returning = EnvironmentRules.LandRecoveryReturnDays; break;
            case SimulationInterventionKind.PlantBiomass:
                if (HabitatTopology.IsWater(blocks[state.Position])) { error = "水域植物工程尚未接入，不能按陆地单格处理"; return false; }
                if (Math.Abs(amount) != .25f && Math.Abs(amount) != .5f) { error = "库存操作使用 ±0.25 或 ±0.5（25%／50%）"; return false; }
                target = EnvironmentRules.MultiplyStock(snapshot.PlantStock,
                    EnvironmentRules.PlantCapacity, amount); break;
            case SimulationInterventionKind.Elevation:
                if (HabitatTopology.IsWater(blocks[state.Position])) { error = "水域整域改高尚未接入，不能按单格处理"; return false; }
                if (Math.Abs(amount) != 1 || z + (int)amount < 0 || z + (int)amount > 2) { error = "海拔只能 ±1，且必须保持在 0–2"; return false; }
                target = z + (int)amount; z = (int)target; break;
            default: error = "该操作由原有专用入口处理"; return false;
        }
        preview = new EnvironmentOperationPreview(snapshot, target,
            EnvironmentRules.ClassifyHabitat(z, t, h), enter, hold, returning);
        return true;
    }

    public bool TryApply(Vector2Int coordinate, SimulationInterventionKind kind, float amount, out string error)
    {
        if (!TryPreview(coordinate, kind, amount, out var preview, out error)) return false;
        var position = Position(coordinate); bool applied;
        switch (kind)
        {
            case SimulationInterventionKind.Temperature:
            case SimulationInterventionKind.Humidity:
                applied = world.TrySetClimate(position, kind == SimulationInterventionKind.Temperature ?
                    EnvironmentAttribute.Temperature : EnvironmentAttribute.Humidity, amount, out error); break;
            case SimulationInterventionKind.PlantRecovery: applied = world.TrySetRecovery(position, amount, out error); break;
            case SimulationInterventionKind.PlantBiomass:
                simulation.ApplyPlantBiomass(blocks[position], (float)preview.Target); applied = true; break;
            case SimulationInterventionKind.Elevation:
                applied = grid.TrySetTileHeight(coordinate, (int)preview.Target);
                if (!applied) error = "地图高度修改失败"; break;
            default: error = "未知环境操作"; return false;
        }
        if (applied) Publish(); return applied;
    }

    public bool TryGetWaterArea(Vector2Int coordinate,
        out IReadOnlyList<Vector2Int> members, out string error)
    {
        members = Array.Empty<Vector2Int>(); error = null;
        if (!isActiveAndEnabled || grid == null || world == null)
        { error = "环境控制器未就绪"; return false; }
        if (!grid.TryGetTile(coordinate, out MapTileInstance tile) ||
            !HabitatTopology.IsWater(tile.Block))
        { error = "目标不是已建水格"; return false; }
        members = new[] { coordinate };
        return true;
    }

    // 2026-10-09 14:41 +08:00: player water tools operate on the complete
    // connected body. Validation precedes all writes; the map height path
    // then publishes one topology event for the entire height transaction.
    public bool TryApplyWater(Vector2Int coordinate, SimulationInterventionKind kind,
        float amount, out string error)
    {
        if (!TryGetWaterArea(coordinate, out IReadOnlyList<Vector2Int> area, out error)) return false;
        var positions = new List<GridPosition>(area.Count);
        foreach (Vector2Int member in area) positions.Add(Position(member));
        switch (kind)
        {
            case SimulationInterventionKind.PlantRecovery:
                if (!EnvironmentRules.IsRecoveryStrength(amount))
                { error = "日增量档位必须为 ±25000 或 ±50000"; return false; }
                if (!world.TrySetRecoveryBatch(positions, amount, false, out error)) return false;
                Publish(); return true;
            case SimulationInterventionKind.PlantBiomass:
                if (Math.Abs(amount) != .25f && Math.Abs(amount) != .5f)
                { error = "存量档位必须为 ±25% 或 ±50%"; return false; }
                double totalStock = 0;
                foreach (Vector2Int member in area) totalStock += blocks[Position(member)].plantBiomass;
                double capacity = (double)EnvironmentRules.WaterPlantCapacity * area.Count;
                float next = (float)(EnvironmentRules.MultiplyStock(totalStock, capacity, amount) / area.Count);
                foreach (Vector2Int member in area)
                    simulation.ApplyPlantBiomass(blocks[Position(member)], next);
                Publish(); return true;
            case SimulationInterventionKind.Elevation:
                if (amount != -1f && amount != 1f)
                { error = "海拔只能升降一级"; return false; }
                foreach (Vector2Int member in area)
                    if (!grid.TryGetTile(member, out MapTileInstance tile) ||
                        tile.HeightLevel + (int)amount < 0 || tile.HeightLevel + (int)amount > 2)
                    { error = "整片水域中有地块会越过海拔边界"; return false; }
                if (!grid.TryShiftTileHeights(area, (int)amount))
                { error = "整片水域海拔提交失败"; return false; }
                Publish(); return true;
            default: error = "该科技不能投放到水域"; return false;
        }
    }

    public bool TryCancel(Vector2Int coordinate, EnvironmentAttribute attribute, out string error)
    {
        error = null;
        if (!isActiveAndEnabled || world == null) { error = "环境控制器未就绪"; return false; }
        bool result;
        if (attribute == EnvironmentAttribute.Recovery &&
            TryRead(coordinate, out EnvironmentReadSnapshot snapshot) && snapshot.Environment.IsWater)
        {
            if (!TryGetWaterArea(coordinate, out IReadOnlyList<Vector2Int> area, out error)) return false;
            var positions = new List<GridPosition>(area.Count);
            foreach (Vector2Int member in area) positions.Add(Position(member));
            result = world.TryCancelRecoveryBatch(positions, out error);
        }
        else result = attribute == EnvironmentAttribute.Recovery ?
            world.TryCancelRecovery(Position(coordinate), out error) :
            world.TryCancelClimate(Position(coordinate), attribute, out error);
        if (result) Publish(); return result;
    }
    public bool TryEditEnvironment(Vector2Int coordinate, SimulationEnvironmentEdit edit, out string error)
    {
        error = null;
        if (!isActiveAndEnabled || !TryRead(coordinate, out _) || edit.temperature < 0 || edit.temperature > 100 ||
            edit.humidity < 0 || edit.humidity > 100 || edit.elevation < 0 || edit.elevation > 2 ||
            edit.habitatRecovery < 0 || edit.habitatRecovery > 150000 || edit.maxPlantBiomass != 1000000 ||
            !EnvironmentRules.IsFinite(edit.plantBiomass) || edit.plantBiomass < 0 || edit.plantBiomass > 1000000)
        { error = "环境输入超出游戏规则：库存上限固定100万，增量上限15万"; return false; }
        if (!grid.TryGetTile(coordinate, out var tile)) { error = "目标地块不存在"; return false; }
        if (tile.HeightLevel != edit.elevation && !grid.TrySetTileHeight(coordinate, edit.elevation))
        { error = "地图高度修改失败"; return false; }
        bool result = world.TryEditEnvironment(Position(coordinate), edit.temperature, edit.humidity, edit.habitatRecovery, out error);
        if (result) { simulation.ApplyPlantBiomass(blocks[Position(coordinate)], edit.plantBiomass); Publish(); }
        return result;
    }
}
