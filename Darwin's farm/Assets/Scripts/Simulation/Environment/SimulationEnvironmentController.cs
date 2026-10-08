using System;
using System.Collections.Generic;
using UnityEngine;
using DarwinFarm.Environment;

public sealed class EnvironmentReadSnapshot
{
    public EnvironmentSnapshot Environment { get; }
    public float PlantStock { get; }
    public int PlantCapacity => EnvironmentRules.PlantCapacity;
    public EnvironmentReadSnapshot(EnvironmentSnapshot environment, float plantStock)
    { Environment = environment; PlantStock = plantStock; }
}

public sealed class EnvironmentOperationPreview
{
    public EnvironmentReadSnapshot Before { get; }
    public double Target { get; }
    public TerrainKind EstimatedTerrain { get; }
    public int EnterDays { get; }
    public int HoldDays { get; }
    public int ReturnDays { get; }
    internal EnvironmentOperationPreview(EnvironmentReadSnapshot before, double target, TerrainKind terrain,
        int enter, int hold, int returning)
    { Before = before; Target = target; EstimatedTerrain = terrain; EnterDays = enter; HoldDays = hold; ReturnDays = returning; }
}

// Controller is the sole bridge between the pure environment Model and live ecology/map data.
// Visuals consume immutable snapshots; an economy adapter can consume settlement receipts later.
[DisallowMultipleComponent]
public sealed class SimulationEnvironmentController : MonoBehaviour
{
    private MapGridManager grid;
    private MapSimulationBridge bridge;
    private SimulationController simulation;
    private SimulationTime clock;
    private EnvironmentWorld world;
    private readonly Dictionary<GridPosition, BlockInfo> blocks = new Dictionary<GridPosition, BlockInfo>();
    private readonly HashSet<long> notifiedSettlements = new HashSet<long>();
    public event Action<IReadOnlyList<EnvironmentReadSnapshot>> Changed;
    public event Action<MonsoonSettlement> SettlementAvailable;
    public IReadOnlyList<MonsoonSnapshot> Monsoons => world?.ReadMonsoons() ?? Array.Empty<MonsoonSnapshot>();
    public IReadOnlyList<MonsoonSettlement> PendingSettlements => world?.ReadSettlements() ?? Array.Empty<MonsoonSettlement>();

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
    }
    private void Start() => Synchronize();
    private void OnDisable()
    {
        if (bridge != null) bridge.BlockGraphSynchronized -= Synchronize;
        if (simulation != null) simulation.OnBeforeDaySimulated -= Advance;
        if (simulation != null) simulation.OnDaySimulated -= NotifyReaders;
        if (clock != null) clock.OnDayReset -= RebaseClock;
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
    public static TerrainDefaults InitialDefaults(MapTileDefinition preset, int actualElevation)
    {
        var defaults = EnvironmentRules.Defaults(TerrainFromPreset(preset.biome));
        return EnvironmentRules.Defaults(EnvironmentRules.Classify(actualElevation, defaults.Temperature, defaults.Humidity, defaults.Recovery));
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
            graph.Add(new TopologyNode(pair.Key, pair.Value.HeightLevel, TerrainFromPreset(pair.Value.Definition.biome), neighbors));
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
            block.maxPlantBiomass = EnvironmentRules.PlantCapacity;
            block.plantBiomass = Mathf.Clamp(block.plantBiomass, 0, EnvironmentRules.PlantCapacity);
        }
        Publish();
    }
    private static readonly GridPosition[] Directions =
    { new GridPosition(1, 0), new GridPosition(-1, 0), new GridPosition(0, 1), new GridPosition(0, -1) };
    private static GridPosition Position(Vector2Int coordinate) => new GridPosition(coordinate.x, coordinate.y);
    private void Advance(int day)
    {
        world.AdvanceToDay(day);
        Publish(false);
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
            snapshots.Add(new EnvironmentReadSnapshot(state, block.plantBiomass));
        }
        if (notifyReaders) Changed?.Invoke(snapshots.AsReadOnly());
        foreach (var settlement in world.ReadSettlements())
            if (notifiedSettlements.Add(settlement.MonsoonId)) SettlementAvailable?.Invoke(settlement);
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
    public bool AcknowledgeSettlement(long id) => world != null && world.AcknowledgeSettlement(id);

    public bool TryPreview(Vector2Int coordinate, SimulationInterventionKind kind, float amount,
        out EnvironmentOperationPreview preview, out string error)
    {
        preview = null; error = null;
        if (!isActiveAndEnabled || !TryRead(coordinate, out var snapshot) || !EnvironmentRules.IsFinite(amount))
        { error = "环境控制器未就绪或目标地块不存在"; return false; }
        var state = snapshot.Environment;
        double target;
        int enter = 0, hold = 0, returning = 0;
        int t = EnvironmentRules.Round(state.Temperature.Value), h = EnvironmentRules.Round(state.Humidity.Value),
            r = EnvironmentRules.Round(state.Recovery.Value), z = state.Elevation;
        switch (kind)
        {
            case SimulationInterventionKind.Temperature:
            case SimulationInterventionKind.Humidity:
                if (HabitatTopology.IsWater(blocks[state.Position])) { error = "水格不能进行温湿度干预"; return false; }
                if (!EnvironmentRules.IsClimateStrength(amount)) { error = "温湿度强度必须为 ±25 或 ±50"; return false; }
                var attribute = kind == SimulationInterventionKind.Temperature ? EnvironmentAttribute.Temperature : EnvironmentAttribute.Humidity;
                target = EnvironmentRules.Clamp(attribute, state.Defaults.Get(attribute) + amount);
                if (attribute == EnvironmentAttribute.Temperature) t = EnvironmentRules.Round(target); else h = EnvironmentRules.Round(target);
                enter = 25; hold = 100; returning = 25;
                break;
            case SimulationInterventionKind.PlantRecovery:
                if (HabitatTopology.IsWater(blocks[state.Position])) { error = "水域植物工程尚未接入，不能按陆地单格处理"; return false; }
                if (!EnvironmentRules.IsRecoveryStrength(amount)) { error = "增量强度必须为 ±25000 或 ±50000"; return false; }
                target = EnvironmentRules.Clamp(EnvironmentAttribute.Recovery, state.Defaults.Recovery + amount);
                r = EnvironmentRules.Round(target); returning = 100; break;
            case SimulationInterventionKind.PlantBiomass:
                if (HabitatTopology.IsWater(blocks[state.Position])) { error = "水域植物工程尚未接入，不能按陆地单格处理"; return false; }
                if (Math.Abs(amount) != .25f && Math.Abs(amount) != .5f) { error = "库存操作使用 ±0.25 或 ±0.5（25%／50%）"; return false; }
                target = EnvironmentRules.ChangeStock(snapshot.PlantStock, amount); break;
            case SimulationInterventionKind.Elevation:
                if (HabitatTopology.IsWater(blocks[state.Position])) { error = "水域整域改高尚未接入，不能按单格处理"; return false; }
                if (Math.Abs(amount) != 1 || z + (int)amount < 0 || z + (int)amount > 2) { error = "海拔只能 ±1，且必须保持在 0–2"; return false; }
                target = z + (int)amount; z = (int)target; break;
            default: error = "该操作由原有专用入口处理"; return false;
        }
        preview = new EnvironmentOperationPreview(snapshot, target, EnvironmentRules.Classify(z, t, h, r), enter, hold, returning);
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
    public bool TryCancel(Vector2Int coordinate, EnvironmentAttribute attribute, out string error)
    {
        error = null;
        if (!isActiveAndEnabled || world == null) { error = "环境控制器未就绪"; return false; }
        bool result = attribute == EnvironmentAttribute.Recovery ? world.TryCancelRecovery(Position(coordinate), out error) :
            world.TryCancelClimate(Position(coordinate), attribute, out error);
        if (result) Publish(); return result;
    }
    public bool TryPreviewMonsoon(Vector2Int source, int temperatureOffset, int humidityOffset,
        out IReadOnlyList<GridPosition> area, out string error)
    {
        area = Array.Empty<GridPosition>(); error = null;
        if (!isActiveAndEnabled || world == null) { error = "环境控制器未就绪"; return false; }
        return world.TryPreviewMonsoon(Position(source), temperatureOffset, humidityOffset, out area, out error);
    }
    // Payment is supplied by a future trusted economy adapter, never inferred from UI input.
    public bool TryDeployMonsoon(Vector2Int source, int temperatureOffset, int humidityOffset,
        out long id, out string error, EnvironmentPayment payment = null)
    {
        id = 0; error = null;
        if (!isActiveAndEnabled || world == null) { error = "环境控制器未就绪"; return false; }
        bool result = world.TryDeployMonsoon(Position(source), temperatureOffset, humidityOffset, payment, out id, out error);
        if (result) Publish(); return result;
    }
    public bool TryCancelMonsoon(long id, out string error)
    {
        error = null;
        if (!isActiveAndEnabled || world == null) { error = "环境控制器未就绪"; return false; }
        bool result = world.TryCancelMonsoon(id, out error); if (result) Publish(); return result;
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
