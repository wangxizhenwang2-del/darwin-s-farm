using System;
using System.Collections.Generic;
using DarwinFarm.Environment;
using UnityEngine;

public sealed class EnvironmentTechnologyPreview
{
    public bool IsValid => Error == null;
    public string Error { get; internal set; }
    public string Effect { get; internal set; }
    public string Before { get; internal set; }
    public string After { get; internal set; }
    public string Timing { get; internal set; }
    public IReadOnlyList<Vector2Int> Area { get; internal set; } = Array.Empty<Vector2Int>();
    public int Cost => 0;
}

// 2026-10-09 14:46 +08:00: player Controller validates the immutable V4.3
// technology choice, obtains a read-only preview, and commits through the
// environment boundary. The View never changes ecology or map state directly.
[DisallowMultipleComponent]
public sealed class EnvironmentTechnologyController : MonoBehaviour
{
    public event Action<Vector2Int, string> OperationCommitted;
    private SimulationEnvironmentController environment;
    private MapGridManager grid;
    private readonly Dictionary<Vector2Int, Dictionary<int, EnvironmentTechnologyChoice>>
        applied = new Dictionary<Vector2Int, Dictionary<int, EnvironmentTechnologyChoice>>();

    private void Awake()
    {
        environment = GetComponent<SimulationEnvironmentController>();
        grid = GetComponent<MapGridManager>();
    }

    public EnvironmentTechnologyPreview Preview(Vector2Int coordinate,
        EnvironmentTechnologyChoice choice)
    {
        var result = new EnvironmentTechnologyPreview();
        string validationError;
        if (environment == null) environment = GetComponent<SimulationEnvironmentController>();
        if (grid == null) grid = GetComponent<MapGridManager>();
        if (environment == null || grid == null || !environment.isActiveAndEnabled ||
            !EnvironmentTechnologyCatalog.Validate(choice))
        { result.Error = "科技选择无效或环境控制器未就绪"; return result; }
        if (!environment.TryRead(coordinate, out EnvironmentReadSnapshot read))
        { result.Error = "目标地块不存在"; return result; }
        EnvironmentSnapshot state = read.Environment;
        bool water = state.IsWater;
        var area = new List<Vector2Int> { coordinate };
        if (water)
        {
            if (choice.Kind == EnvironmentTechnologyKind.TemperatureChange ||
                choice.Kind == EnvironmentTechnologyKind.HumidityChange ||
                choice.Kind == EnvironmentTechnologyKind.MonsoonAnchor)
            { result.Error = "温湿度科技和季风锚不能以水格为目标"; return result; }
            if (!environment.TryGetWaterArea(coordinate,
                out IReadOnlyList<Vector2Int> waterArea, out validationError))
            { result.Error = validationError; return result; }
            area = new List<Vector2Int>(waterArea);
        }
        result.Area = area.AsReadOnly();
        result.Effect = EnvironmentTechnologyCatalog.Name(choice.Kind);
        result.Before = water ? WaterSummary(area) : LandSummary(read);
        switch (choice.Kind)
        {
            case EnvironmentTechnologyKind.TemperatureChange:
            case EnvironmentTechnologyKind.HumidityChange:
                bool temperature = choice.Kind == EnvironmentTechnologyKind.TemperatureChange;
                if (!environment.TryPreview(coordinate, temperature ?
                    SimulationInterventionKind.Temperature : SimulationInterventionKind.Humidity,
                    EnvironmentTechnologyCatalog.SignedClimate(choice),
                    out EnvironmentOperationPreview climate, out validationError))
                { result.Error = validationError; return result; }
                result.After = (temperature ? "温度" : "湿度") + "目标 " +
                    climate.Target.ToString("F0") + "；预计地形 " + climate.EstimatedTerrain;
                result.Timing = "25天进入 · 150天维持 · 150天恢复";
                break;
            case EnvironmentTechnologyKind.PlantChange:
                float fraction = EnvironmentTechnologyCatalog.Fraction(choice);
                if (choice.Tier > 0)
                {
                    int growth = EnvironmentTechnologyCatalog.GrowthAmount(choice);
                    double capacity = (water ? EnvironmentRules.WaterPlantCapacity :
                        EnvironmentRules.PlantCapacity) * (double)area.Count;
                    double stock = water ? WaterStock(area) : read.PlantStock;
                    double nextStock = EnvironmentRules.MultiplyStock(stock, capacity, fraction);
                    if (water)
                        result.After = "整域库存 " + nextStock.ToString("F0") +
                            "；每格日增量 " + Math.Min(EnvironmentRules.MaximumRecovery,
                                state.Recovery.Value + growth).ToString("F0");
                    else
                    {
                        if (!environment.TryPreview(coordinate, SimulationInterventionKind.PlantRecovery,
                            growth, out EnvironmentOperationPreview increased, out validationError))
                        { result.Error = validationError; return result; }
                        result.After = "库存 " + nextStock.ToString("F0") +
                            "；日增量 " + increased.Target.ToString("F0") +
                            "；预计地形 " + increased.EstimatedTerrain;
                    }
                    result.Timing = "库存立即乘算 · 日增量立即提高并维持50天，再用50天恢复";
                }
                else
                {
                    float factor = 1f - fraction;
                    if (water)
                        result.After = "整域库存 " + (WaterStock(area) * factor).ToString("F0") +
                            "；每格日增量 " + Math.Max(0, state.Recovery.Value -
                                EnvironmentTechnologyCatalog.GrowthAmount(choice)).ToString("F0");
                    else
                    {
                        int recovery = EnvironmentRules.Round(Math.Max(0, state.Recovery.Value -
                            EnvironmentTechnologyCatalog.GrowthAmount(choice)));
                        TerrainKind terrain = EnvironmentRules.Classify(state.Elevation,
                            EnvironmentRules.Round(state.Temperature.Value),
                            EnvironmentRules.Round(state.Humidity.Value), recovery);
                        result.After = "库存 " + (read.PlantStock * factor).ToString("F0") +
                            "；日增量 " + recovery.ToString("F0") +
                            "；预计地形 " + terrain;
                    }
                    result.Timing = "库存立即乘算 · 日增量立即降低并维持50天，再用50天恢复";
                }
                break;
            case EnvironmentTechnologyKind.ElevationChange:
                int shift = choice.Tier;
                foreach (Vector2Int member in area)
                {
                    if (!grid.TryGetTile(member, out MapTileInstance tile) ||
                        tile.HeightLevel + shift < 0 || tile.HeightLevel + shift > 2)
                    { result.Error = "有地块会越过海拔 0～2 的边界"; return result; }
                }
                result.After = "海拔 " + (state.Elevation + shift) +
                    (water ? "；整片水域 " + area.Count + " 格同步改高" : "");
                if (!water)
                {
                    if (!environment.TryPreview(coordinate,
                        SimulationInterventionKind.Elevation, shift,
                        out EnvironmentOperationPreview height, out validationError))
                    { result.Error = validationError; return result; }
                    result.After += "；预计地形 " + height.EstimatedTerrain;
                }
                result.Timing = "立即生效 · 不回滚";
                break;
            case EnvironmentTechnologyKind.MonsoonAnchor:
                if (!environment.TryPreviewMonsoon(coordinate,
                    choice.TemperatureOffset, choice.HumidityOffset,
                    out IReadOnlyList<GridPosition> covered, out validationError))
                { result.Error = validationError; return result; }
                area.Clear();
                foreach (GridPosition member in covered) area.Add(new Vector2Int(member.X, member.Y));
                result.Area = area.AsReadOnly();
                result.After = EnvironmentTechnologyCatalog.MonsoonPatternName(choice) + "：覆盖 " +
                    area.Count + " 个陆格，温度 " + Signed(choice.TemperatureOffset) +
                    "，湿度 " + Signed(choice.HumidityOffset);
                result.Timing = "立即生效 · 持续至撤销或锚点拓扑变化";
                break;
            default: result.Error = "未知科技"; break;
        }
        return result;
    }

    public bool TryDeploy(Vector2Int coordinate, EnvironmentTechnologyChoice choice,
        out EnvironmentTechnologyPreview result)
    {
        result = Preview(coordinate, choice);
        if (!result.IsValid)
        {
            OperationCommitted?.Invoke(coordinate,
                "rejected:technology_deploy:" + choice.Kind + ";reason=" + result.Error);
            return false;
        }
        bool water = environment.TryRead(coordinate, out EnvironmentReadSnapshot read) &&
            read.Environment.IsWater;
        bool success;
        string error;
        switch (choice.Kind)
        {
            case EnvironmentTechnologyKind.TemperatureChange:
            case EnvironmentTechnologyKind.HumidityChange:
                success = environment.TryApply(coordinate,
                    choice.Kind == EnvironmentTechnologyKind.TemperatureChange ?
                        SimulationInterventionKind.Temperature : SimulationInterventionKind.Humidity,
                    EnvironmentTechnologyCatalog.SignedClimate(choice), out error); break;
            case EnvironmentTechnologyKind.PlantChange:
                success = choice.Tier > 0 ? environment.TryApplyPlantIncrease(coordinate,
                    EnvironmentTechnologyCatalog.GrowthAmount(choice),
                    EnvironmentTechnologyCatalog.Fraction(choice), out error) :
                    environment.TryApplySuppression(coordinate,
                    EnvironmentTechnologyCatalog.Fraction(choice), out error); break;
            case EnvironmentTechnologyKind.ElevationChange:
                float shift = choice.Tier;
                success = water ? environment.TryApplyWater(coordinate,
                    SimulationInterventionKind.Elevation, shift, out error) :
                    environment.TryApply(coordinate, SimulationInterventionKind.Elevation,
                        shift, out error); break;
            case EnvironmentTechnologyKind.MonsoonAnchor:
                success = environment.TryDeployMonsoon(coordinate,
                    choice.TemperatureOffset, choice.HumidityOffset, out _, out error); break;
            default: success = false; error = "未知科技"; break;
        }
        if (!success)
        {
            result.Error = error;
            OperationCommitted?.Invoke(coordinate,
                "rejected:technology_deploy:" + choice.Kind + ";reason=" + error);
            return false;
        }
        if (choice.Kind != EnvironmentTechnologyKind.MonsoonAnchor)
            foreach (Vector2Int member in result.Area) Record(member, choice);
        OperationCommitted?.Invoke(coordinate, "technology_deploy:" + choice.Kind +
            ";tier=" + choice.Tier + ";temperatureOffset=" + choice.TemperatureOffset +
            ";humidityOffset=" + choice.HumidityOffset + ";affectedTiles=" + result.Area.Count);
        return true;
    }

    public string CurrentApplied(Vector2Int coordinate)
    {
        if (environment == null || !environment.TryRead(coordinate, out EnvironmentReadSnapshot read))
            return "当前科技：无";
        var labels = new List<string>();
        string recent = null;
        EnvironmentSnapshot state = read.Environment;
        if (applied.TryGetValue(coordinate, out var channels))
        {
            if (state.HasLocalTemperature) AddActive(channels, 0, state.Temperature.Phase, labels);
            if (state.HasLocalHumidity) AddActive(channels, 1, state.Humidity.Phase, labels);
            if (state.HasRecoveryCommand) AddActive(channels, 2, state.Recovery.Phase, labels);
            if (channels.TryGetValue(3, out var instant))
                recent = EnvironmentTechnologyCatalog.Name(instant.Kind) +
                    "(" + TierLabel(instant) + ")";
        }
        if (state.MonsoonId.HasValue)
            foreach (MonsoonSnapshot wind in environment.Monsoons)
                if (wind.Id == state.MonsoonId.Value)
                {
                    var windChoice = new EnvironmentTechnologyChoice(
                        EnvironmentTechnologyKind.MonsoonAnchor, 1,
                        wind.TemperatureOffset, wind.HumidityOffset);
                    labels.Add("季风工具 " + EnvironmentTechnologyCatalog.MonsoonPatternName(windChoice) +
                        " " + Signed(wind.TemperatureOffset) + "/" +
                        Signed(wind.HumidityOffset));
                    break;
                }
        return labels.Count > 0 ? "当前科技：" + string.Join("；", labels) :
            recent != null ? "最近投放：" + recent : "当前科技：无";
    }

    public bool CanCancel(Vector2Int coordinate, EnvironmentTechnologyKind kind)
    {
        if (environment == null || !environment.TryRead(coordinate,
            out EnvironmentReadSnapshot read)) return false;
        EnvironmentSnapshot state = read.Environment;
        bool ownsTemperature = HasRecorded(coordinate, 0, kind);
        bool ownsHumidity = HasRecorded(coordinate, 1, kind);
        bool ownsRecovery = HasRecorded(coordinate, 2, kind);
        switch (kind)
        {
            case EnvironmentTechnologyKind.TemperatureChange:
                return ownsTemperature && state.HasLocalTemperature &&
                    state.Temperature.Phase != EffectPhase.Returning;
            case EnvironmentTechnologyKind.HumidityChange:
                return ownsHumidity && state.HasLocalHumidity &&
                    state.Humidity.Phase != EffectPhase.Returning;
            case EnvironmentTechnologyKind.PlantChange:
                return ownsRecovery && state.CanCancelRecovery;
            case EnvironmentTechnologyKind.MonsoonAnchor:
                foreach (MonsoonSnapshot wind in environment.Monsoons)
                    if (wind.Source.X == coordinate.x && wind.Source.Y == coordinate.y) return true;
                return false;
            default: return false;
        }
    }

    public bool TryCancel(Vector2Int coordinate, EnvironmentTechnologyKind kind,
        out string error)
    {
        error = null;
        if (!CanCancel(coordinate, kind))
        {
            error = "这里没有可撤销的对应科技";
            OperationCommitted?.Invoke(coordinate,
                "rejected:technology_cancel:" + kind + ";reason=" + error);
            return false;
        }
        if (kind == EnvironmentTechnologyKind.MonsoonAnchor)
        {
            foreach (MonsoonSnapshot wind in environment.Monsoons)
                if (wind.Source.X == coordinate.x && wind.Source.Y == coordinate.y)
                {
                    bool canceled = environment.TryCancelMonsoon(wind.Id, out error);
                    if (canceled) OperationCommitted?.Invoke(coordinate,
                        "technology_cancel:" + kind + ";monsoonId=" + wind.Id);
                    return canceled;
                }
            error = "季风已不存在"; return false;
        }
        EnvironmentAttribute attribute = kind == EnvironmentTechnologyKind.TemperatureChange
            ? EnvironmentAttribute.Temperature : kind == EnvironmentTechnologyKind.HumidityChange
                ? EnvironmentAttribute.Humidity : EnvironmentAttribute.Recovery;
        bool result = environment.TryCancel(coordinate, attribute, out error);
        if (result) OperationCommitted?.Invoke(coordinate, "technology_cancel:" + kind);
        return result;
    }

    private static void AddActive(Dictionary<int, EnvironmentTechnologyChoice> channels,
        int channel, EffectPhase phase, List<string> labels)
    {
        if (phase == EffectPhase.None || phase == EffectPhase.Automatic ||
            !channels.TryGetValue(channel, out var choice)) return;
        labels.Add(EnvironmentTechnologyCatalog.Name(choice.Kind) + " " + TierLabel(choice) +
            (phase == EffectPhase.Entering ? "(进入)" : phase == EffectPhase.Holding ?
                "(维持)" : "(恢复)"));
    }
    private bool HasRecorded(Vector2Int coordinate, int channel,
        EnvironmentTechnologyKind kind) =>
        applied.TryGetValue(coordinate, out var channels) &&
        channels.TryGetValue(channel, out var choice) && choice.Kind == kind;
    private void Record(Vector2Int coordinate, EnvironmentTechnologyChoice choice)
    {
        if (!applied.TryGetValue(coordinate, out var channels))
        { channels = new Dictionary<int, EnvironmentTechnologyChoice>(); applied.Add(coordinate, channels); }
        int channel = choice.Kind == EnvironmentTechnologyKind.TemperatureChange ? 0 :
            choice.Kind == EnvironmentTechnologyKind.HumidityChange ? 1 :
            choice.Kind == EnvironmentTechnologyKind.PlantChange ? 2 : 3;
        channels[channel] = choice;
        channels[3] = choice;
    }
    private string WaterSummary(IReadOnlyList<Vector2Int> area)
    {
        environment.TryRead(area[0], out EnvironmentReadSnapshot read);
        return "水域 " + area.Count + " 格；库存 " + WaterStock(area).ToString("F0") +
            "；每格增量 " + read.Environment.Recovery.Value.ToString("F0") +
            "；海拔 " + read.Environment.Elevation;
    }
    private double WaterStock(IReadOnlyList<Vector2Int> area)
    {
        double total = 0;
        foreach (Vector2Int member in area)
            if (environment.TryRead(member, out EnvironmentReadSnapshot read)) total += read.PlantStock;
        return total;
    }
    private static string LandSummary(EnvironmentReadSnapshot read) =>
        "温 " + read.Environment.Temperature.Value.ToString("F0") +
        " 湿 " + read.Environment.Humidity.Value.ToString("F0") +
        " 海 " + read.Environment.Elevation +
        " 库存 " + read.PlantStock.ToString("F0") +
        " 增量 " + read.Environment.Recovery.Value.ToString("F0");
    private static string Signed(int value) => value > 0 ? "+" + value : value.ToString();
    private static string TierLabel(EnvironmentTechnologyChoice choice) =>
        choice.Kind == EnvironmentTechnologyKind.MonsoonAnchor ?
            EnvironmentTechnologyCatalog.MonsoonPatternName(choice) :
            (choice.Tier > 0 ? "高" : "低") + Math.Abs(choice.Tier) + "档";
}
