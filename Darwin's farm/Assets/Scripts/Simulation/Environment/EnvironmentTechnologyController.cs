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
    public int Cost { get; internal set; }
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
    private BioenergyAccount account;
    private readonly Dictionary<Vector2Int, Dictionary<int, EnvironmentTechnologyChoice>>
        applied = new Dictionary<Vector2Int, Dictionary<int, EnvironmentTechnologyChoice>>();
    [Serializable] public sealed class SavedChoice { public int x,y,channel,kind,tier; }
    public List<SavedChoice> CaptureSaveState()
    {
        var result=new List<SavedChoice>();
        foreach(var tile in applied)
            foreach(var operation in tile.Value)
                result.Add(new SavedChoice { x=tile.Key.x, y=tile.Key.y,
                    channel=operation.Key,kind=(int)operation.Value.Kind, tier=operation.Value.Tier });
        result.Sort((a,b)=>a.x!=b.x?a.x.CompareTo(b.x):a.y!=b.y?a.y.CompareTo(b.y):a.channel.CompareTo(b.channel));
        return result;
    }
    public void RestoreSaveState(List<SavedChoice> state)
    {
        if(state==null)throw new ArgumentException("Missing technology history.");
        applied.Clear();
        foreach(var saved in state)
        {
            var choice=new EnvironmentTechnologyChoice((EnvironmentTechnologyKind)saved.kind,saved.tier);
            if(!EnvironmentTechnologyCatalog.Validate(choice)||saved.channel<0||saved.channel>3)
                throw new ArgumentException("Invalid saved technology choice.");
            var coordinate=new Vector2Int(saved.x,saved.y);
            if(grid==null || !grid.TryGetTile(coordinate,out _))throw new ArgumentException("Saved technology tile missing.");
            if(!applied.TryGetValue(coordinate,out var choices))
                applied[coordinate]=choices=new Dictionary<int,EnvironmentTechnologyChoice>();
            choices[saved.channel]=choice;
        }
    }

    private void Awake()
    {
        environment = GetComponent<SimulationEnvironmentController>();
        grid = GetComponent<MapGridManager>();
        account = GetComponent<BioenergyAccount>();
    }

    public EnvironmentTechnologyPreview Preview(Vector2Int coordinate,
        EnvironmentTechnologyChoice choice)
    {
        var result = new EnvironmentTechnologyPreview();
        string validationError;
        if (environment == null) environment = GetComponent<SimulationEnvironmentController>();
        if (grid == null) grid = GetComponent<MapGridManager>();
        if (account == null) account = GetComponent<BioenergyAccount>();
        if (environment == null || grid == null || !environment.isActiveAndEnabled ||
            !EnvironmentTechnologyCatalog.Validate(choice))
        { result.Error = "科技选择无效或环境控制器未就绪"; return result; }
        if (!environment.TryRead(coordinate, out EnvironmentReadSnapshot read))
        { result.Error = "目标地块不存在"; return result; }
        result.Cost = account != null && account.CostsEnabled ?
            EnvironmentTechnologyCatalog.Price(choice) : 0;
        EnvironmentSnapshot state = read.Environment;
        bool water = state.IsWater;
        var area = new List<Vector2Int> { coordinate };
        if (water)
        {
            if (choice.Kind == EnvironmentTechnologyKind.TemperatureChange ||
                choice.Kind == EnvironmentTechnologyKind.HumidityChange)
            { result.Error = "温湿度科技不能以水格为目标"; return result; }
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
                    climate.Target.ToString("F0") + "；预计生境 " +
                    EnvironmentRules.Profile(climate.EstimatedHabitat).Title;
                result.Timing = "25天进入 · 150天维持 · 100天恢复";
                break;
            case EnvironmentTechnologyKind.PlantStockChange:
                double capacity = (water ? EnvironmentRules.WaterPlantCapacity :
                    EnvironmentRules.PlantCapacity) * (double)area.Count;
                double stock = water ? WaterStock(area) : read.PlantStock;
                double nextStock = EnvironmentRules.MultiplyStock(stock, capacity,
                    Math.Sign(choice.Tier) * EnvironmentTechnologyCatalog.Fraction(choice));
                result.After = (water ? "整域库存 " : "库存 ") + nextStock.ToString("F0") +
                    "；日增量不变";
                result.Timing = "库存立即乘算 · 无维持或恢复阶段";
                break;
            case EnvironmentTechnologyKind.PlantRecoveryChange:
                int growth = Math.Sign(choice.Tier) *
                    EnvironmentTechnologyCatalog.GrowthAmount(choice);
                if (water)
                    result.After = "每格日增量 " + EnvironmentRules.Clamp(
                        EnvironmentAttribute.Recovery, state.Recovery.Value + growth).ToString("F0") +
                        "；库存不即时改变";
                else
                {
                    if (!environment.TryPreview(coordinate, SimulationInterventionKind.PlantRecovery,
                        growth, out EnvironmentOperationPreview increased, out validationError))
                    { result.Error = validationError; return result; }
                    result.After = "日增量 " + increased.Target.ToString("F0") +
                        "；库存不即时改变";
                }
                result.Timing = water ? "水域：立即变化 · 维持50天 · 恢复50天" :
                    "25天进入 · 150天维持 · 100天恢复";
                break;
            case EnvironmentTechnologyKind.ElevationChange:
                int shift = choice.Tier;
                foreach (Vector2Int member in area)
                {
                    if (!grid.TryGetTile(member, out MapTileInstance tile) ||
                        tile.HeightLevel + shift < 0 || tile.HeightLevel + shift > 2)
                    { result.Error = "有地块会越过海拔 0至2 的边界"; return result; }
                }
                result.After = "海拔 " + (state.Elevation + shift) +
                    (water ? "；整片水域 " + area.Count + " 格同步改高" : "");
                if (!water)
                {
                    if (!environment.TryPreview(coordinate,
                        SimulationInterventionKind.Elevation, shift,
                        out EnvironmentOperationPreview height, out validationError))
                    { result.Error = validationError; return result; }
                    result.After += "；预计生境 " + EnvironmentRules.Profile(height.EstimatedHabitat).Title;
                }
                result.Timing = "立即生效 · 不回滚";
                break;
            default: result.Error = "未知科技"; break;
        }
        return result;
    }

    public bool TryDeploy(Vector2Int coordinate, EnvironmentTechnologyChoice choice,
        out EnvironmentTechnologyPreview result)
    {
        if (!TutorialFlowController.AllowsTool(choice.Kind))
        {
            result = new EnvironmentTechnologyPreview { Error = "该工具将在教程后续阶段解锁" };
            return false;
        }
        if (!TutorialFlowController.AllowsChoice(choice))
        {
            result = new EnvironmentTechnologyPreview { Error = "教程当前只允许温度提高一档" };
            return false;
        }
        if (grid == null) grid = GetComponent<MapGridManager>();
        if (!TutorialFlowController.AllowsDeployment(
                grid != null && grid.TryGetTile(coordinate, out MapTileInstance tile) ? tile : null,
                choice))
        {
            result = new EnvironmentTechnologyPreview { Error = "请在有鸽鸽的地块应用温度提高一档" };
            return false;
        }
        result = Preview(coordinate, choice);
        if (!result.IsValid)
        {
            OperationCommitted?.Invoke(coordinate,
                "rejected:technology_deploy:" + choice.Kind + ";reason=" + result.Error);
            return false;
        }
        if (account == null) account = GetComponent<BioenergyAccount>();
        if (account == null) { result.Error = "经济账户未就绪"; return false; }
        bool water = environment.TryRead(coordinate, out EnvironmentReadSnapshot read) &&
            read.Environment.IsWater;
        var recoveryBefore = new Dictionary<Vector2Int, double>();
        var stockBefore = new Dictionary<Vector2Int, double>();
        if (choice.Kind == EnvironmentTechnologyKind.PlantRecoveryChange)
            foreach (Vector2Int member in result.Area)
                if (environment.TryRead(member, out EnvironmentReadSnapshot before))
                    recoveryBefore[member] = before.Environment.Recovery.Value;
        if (choice.Kind == EnvironmentTechnologyKind.PlantStockChange)
            foreach (Vector2Int member in result.Area)
                if (environment.TryRead(member, out EnvironmentReadSnapshot before))
                    stockBefore[member] = before.PlantStock;
        bool success;
        string error;
        bool Commit(string receiptId, long paid)
        {
          switch (choice.Kind)
          {
            case EnvironmentTechnologyKind.TemperatureChange:
            case EnvironmentTechnologyKind.HumidityChange:
                success = environment.TryApply(coordinate,
                    choice.Kind == EnvironmentTechnologyKind.TemperatureChange ?
                        SimulationInterventionKind.Temperature : SimulationInterventionKind.Humidity,
                    EnvironmentTechnologyCatalog.SignedClimate(choice), out error); break;
            case EnvironmentTechnologyKind.PlantStockChange:
                float fraction = Math.Sign(choice.Tier) *
                    EnvironmentTechnologyCatalog.Fraction(choice);
                success = water ? environment.TryApplyWater(coordinate,
                    SimulationInterventionKind.PlantBiomass, fraction, out error) :
                    environment.TryApply(coordinate,
                        SimulationInterventionKind.PlantBiomass, fraction, out error); break;
            case EnvironmentTechnologyKind.PlantRecoveryChange:
                float growthAmount = Math.Sign(choice.Tier) *
                    EnvironmentTechnologyCatalog.GrowthAmount(choice);
                success = water ? environment.TryApplyWater(coordinate,
                    SimulationInterventionKind.PlantRecovery, growthAmount, out error) :
                    environment.TryApply(coordinate,
                        SimulationInterventionKind.PlantRecovery, growthAmount, out error); break;
            case EnvironmentTechnologyKind.ElevationChange:
                float shift = choice.Tier;
                success = water ? environment.TryApplyWater(coordinate,
                    SimulationInterventionKind.Elevation, shift, out error) :
                    environment.TryApply(coordinate, SimulationInterventionKind.Elevation,
                        shift, out error); break;
            default: success = false; error = "未知科技"; break;
          }
          return success;
        }
        success = false; error = null;
        bool paidAndApplied = account.TryExecute(EnvironmentTechnologyCatalog.Price(choice),
            "环境科技:" + choice.Kind, Commit, out string accountError);
        if (!paidAndApplied)
        {
            result.Error = error ?? accountError;
            OperationCommitted?.Invoke(coordinate,
                "rejected:technology_deploy:" + choice.Kind + ";reason=" + result.Error);
            return false;
        }
        foreach (Vector2Int member in result.Area) Record(member, choice);
        SimulationController ecology = GetComponent<SimulationController>();
        SimulationTime clock = GetComponent<SimulationTime>();
        if (ecology != null && clock != null)
            foreach (Vector2Int member in result.Area)
                if (grid.TryGetTile(member, out MapTileInstance affected))
                {
                    ecology.RecordPlayerTechnology(affected.Block, choice.Kind,
                        choice.Tier, clock.currentDay);
                    if (choice.Kind == EnvironmentTechnologyKind.PlantRecoveryChange &&
                        recoveryBefore.TryGetValue(member, out double oldRecovery) &&
                        environment.TryRead(member, out EnvironmentReadSnapshot after))
                        ecology.RecordActualRecoveryGuidance(affected.Block,
                            (float)(after.Environment.Recovery.Target - oldRecovery),
                            clock.currentDay);
                    if (choice.Kind == EnvironmentTechnologyKind.PlantStockChange &&
                        stockBefore.TryGetValue(member, out double oldStock) &&
                        environment.TryRead(member, out EnvironmentReadSnapshot afterStock))
                        ecology.RecordActualStockGuidance(affected.Block,
                            (float)oldStock, (float)afterStock.PlantStock,
                            choice.Tier, clock.currentDay);
                }
        OperationCommitted?.Invoke(coordinate, "technology_deploy:" + choice.Kind +
            ";tier=" + choice.Tier + ";affectedTiles=" + result.Area.Count);
        if (choice.Tier > 0)
        {
            if (choice.Kind == EnvironmentTechnologyKind.PlantRecoveryChange)
                TutorialFlowController.Report("plantRecovery");
            else if (choice.Kind == EnvironmentTechnologyKind.TemperatureChange && choice.Tier == 1)
                TutorialFlowController.ReportTemperatureApplied(coordinate);
            else if (choice.Kind == EnvironmentTechnologyKind.HumidityChange)
                TutorialFlowController.Report("humidity");
        }
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
            if (state.HasLocalTemperature) AddActive(channels, 0, state.Temperature, labels);
            if (state.HasLocalHumidity) AddActive(channels, 1, state.Humidity, labels);
            if (state.HasRecoveryCommand) AddActive(channels, 2, state.Recovery, labels);
            if (channels.TryGetValue(3, out var instant))
                recent = EnvironmentTechnologyCatalog.Name(instant.Kind) +
                    "(" + TierLabel(instant) + ")";
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
            case EnvironmentTechnologyKind.PlantRecoveryChange:
                return ownsRecovery && state.CanCancelRecovery;
            default: return false;
        }
    }

    public bool TryCancel(Vector2Int coordinate, EnvironmentTechnologyKind kind,
        out string error)
    {
        error = null;
        if (!TutorialFlowController.Allows(TutorialFlowController.Feature.CancelTool))
        { error = "撤销工具尚未解锁"; return false; }
        if (!CanCancel(coordinate, kind))
        {
            error = "这里没有可撤销的对应科技";
            OperationCommitted?.Invoke(coordinate,
                "rejected:technology_cancel:" + kind + ";reason=" + error);
            return false;
        }
        EnvironmentAttribute attribute = kind == EnvironmentTechnologyKind.TemperatureChange
            ? EnvironmentAttribute.Temperature : kind == EnvironmentTechnologyKind.HumidityChange
                ? EnvironmentAttribute.Humidity : EnvironmentAttribute.Recovery;
        bool result = environment.TryCancel(coordinate, attribute, out error);
        if (result && kind == EnvironmentTechnologyKind.PlantRecoveryChange)
        {
            SimulationController ecology = GetComponent<SimulationController>();
            if (ecology != null && grid.TryGetTile(coordinate,
                out MapTileInstance affected))
                ecology.CancelRecoveryGuidance(affected.Block);
        }
        if (result) OperationCommitted?.Invoke(coordinate, "technology_cancel:" + kind);
        return result;
    }

    private static void AddActive(Dictionary<int, EnvironmentTechnologyChoice> channels,
        int channel, AttributeSnapshot attribute, List<string> labels)
    {
        EffectPhase phase = attribute.Phase;
        if (phase == EffectPhase.None || phase == EffectPhase.Automatic ||
            !channels.TryGetValue(channel, out var choice)) return;
        labels.Add(EnvironmentTechnologyCatalog.Name(choice.Kind) + " " + TierLabel(choice) +
            (phase == EffectPhase.Entering ? "(进入)" : phase == EffectPhase.Holding ?
                "(维持)" : "(恢复)") + " 剩余" + attribute.RemainingDays +
            "天，目标" + attribute.Target.ToString("F0"));
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
            choice.Kind == EnvironmentTechnologyKind.PlantRecoveryChange ? 2 : 3;
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
        "地形 " + read.Environment.Terrain +
        "；温 " + read.Environment.Temperature.Value.ToString("F0") +
        " 湿 " + read.Environment.Humidity.Value.ToString("F0") +
        " 海 " + read.Environment.Elevation +
        " 库存 " + read.PlantStock.ToString("F0") +
        " 增量 " + read.Environment.Recovery.Value.ToString("F0") +
        "；生境 " + read.Environment.HabitatTitle +
        "；默认温/湿/增量 " + read.Environment.Defaults.Temperature + "/" +
        read.Environment.Defaults.Humidity + "/" + read.Environment.Defaults.Recovery;
    private static string TierLabel(EnvironmentTechnologyChoice choice) =>
        (choice.Tier > 0 ? "高" : "低") + Math.Abs(choice.Tier) + "档";
}
