using System;
using System.Collections.Generic;
using UnityEngine;

public enum SimulationInterventionKind
{
    Temperature,
    Humidity,
    PlantBiomass,
    PlantRecovery,
    Elevation,
    WaterCoverage
}

[Serializable]
public struct SimulationInterventionRequest
{
    public SimulationInterventionKind kind;
    public Vector2Int center;
    public int radius;
    public float amount;
    // Legacy serialization only. Runtime duration is determined by the operation kind.
    public int durationDays;
    public WaterCoverage waterTarget;
}

[Serializable]
public struct SimulationEnvironmentEdit
{
    public int temperature, humidity, elevation, habitatRecovery;
    public float maxPlantBiomass, plantBiomass;
}

public sealed class SimulationInterventionTarget
{
    public Vector2Int Coordinate { get; internal set; }
    public string Before { get; internal set; }
    public string After { get; internal set; }
    internal BlockInfo Block;
    internal float NextValue;
}

public sealed class SimulationInterventionPreview
{
    public string Error { get; internal set; }
    public bool IsValid => Error == null && Targets.Count > 0;
    public List<SimulationInterventionTarget> Targets { get; } =
        new List<SimulationInterventionTarget>();
}

// A single mutation boundary for the temporary editor visual and later game UI.
// Cost calculation and payment belong to the future Simulation/Economy system.
public sealed class SimulationInterventionController : MonoBehaviour
{
    public event Action<Vector2Int, string> OperationCommitted;
    [SerializeField] private MapGridManager gridManager;
    [SerializeField] private MapSimulationBridge bridge;
    [SerializeField] private SimulationController simulationController;
    [SerializeField] private SimulationTime simulationTime;
    private SimulationEnvironmentController environment;

    private readonly List<LineRenderer> previewOutlines = new List<LineRenderer>();
    private readonly List<Vector2Int> previewCoordinates = new List<Vector2Int>();
    private readonly List<int> previewHeights = new List<int>();
    private Material previewMaterial;
    public int MaxRadius => 0;
    public int OngoingEffectCount
    {
        get
        {
            int count = 0;
            if (Environment != null)
                foreach (var item in Environment.ReadAll())
                    if (item.Environment.HasLocalClimate || item.Environment.HasMonsoon ||
                        item.Environment.Recovery.Phase != DarwinFarm.Environment.EffectPhase.None) count++;
            return count;
        }
    }
    private SimulationEnvironmentController Environment => environment != null ? environment :
        environment = GetComponent<SimulationEnvironmentController>();

    private void Awake()
    {
        if (gridManager == null) gridManager = GetComponent<MapGridManager>();
        if (bridge == null) bridge = GetComponent<MapSimulationBridge>();
        if (simulationController == null)
            simulationController = GetComponent<SimulationController>();
        if (simulationTime == null) simulationTime = GetComponent<SimulationTime>();
        if (gridManager == null || bridge == null ||
            simulationController == null || simulationTime == null)
        {
            Debug.LogError("Intervention controller requires the map simulation components.", this);
            enabled = false;
        }
    }

    private void OnDisable() => ClearPreview();

    public void ShowPreview(SimulationInterventionPreview preview)
    {
        if (preview == null || !preview.IsValid)
        {
            ClearPreview();
            return;
        }

        bool same = preview.Targets.Count == previewCoordinates.Count;
        if (same)
            for (int i = 0; i < preview.Targets.Count; i++)
            {
                Vector2Int coordinate = preview.Targets[i].Coordinate;
                if (!gridManager.TryGetTile(coordinate, out MapTileInstance tile) ||
                    previewCoordinates[i] != coordinate ||
                    previewHeights[i] != tile.HeightLevel)
                {
                    same = false;
                    break;
                }
            }
        if (same) return;

        ClearPreview();
        if (previewMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return;
            previewMaterial = new Material(shader);
        }
        foreach (SimulationInterventionTarget target in preview.Targets)
        {
            if (!gridManager.TryGetTile(target.Coordinate, out MapTileInstance tile))
                continue;
            GameObject outline = new GameObject("InterventionPreview");
            outline.transform.SetParent(transform, false);
            LineRenderer line = outline.AddComponent<LineRenderer>();
            line.sharedMaterial = previewMaterial;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 4;
            line.widthMultiplier = 0.16f;
            line.startColor = Color.yellow;
            line.endColor = Color.yellow;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            float half = MapGridManager.TileSize * 0.48f;
            Vector3 center = gridManager.GridToWorld(target.Coordinate) +
                Vector3.up * (tile.HeightLevel * MapGridManager.HeightStep + 2f);
            line.SetPosition(0, center + new Vector3(-half, 0f, -half));
            line.SetPosition(1, center + new Vector3(-half, 0f, half));
            line.SetPosition(2, center + new Vector3(half, 0f, half));
            line.SetPosition(3, center + new Vector3(half, 0f, -half));
            previewOutlines.Add(line);
            previewCoordinates.Add(target.Coordinate);
            previewHeights.Add(tile.HeightLevel);
        }
    }

    public void ClearPreview()
    {
        foreach (LineRenderer line in previewOutlines)
            if (line != null) Destroy(line.gameObject);
        previewOutlines.Clear();
        previewCoordinates.Clear();
        previewHeights.Clear();
    }

    private void OnDestroy()
    {
        if (previewMaterial != null) Destroy(previewMaterial);
    }

    public SimulationInterventionPreview Preview(SimulationInterventionRequest request)
    {
        SimulationInterventionPreview preview = new SimulationInterventionPreview();
        if (!isActiveAndEnabled || gridManager == null || bridge == null)
        {
            preview.Error = "干预控制器未就绪";
            return preview;
        }
        if (request.radius != 0 || float.IsNaN(request.amount) || float.IsInfinity(request.amount))
        {
            preview.Error = "单格操作只允许半径 0；季风通过发源点专用接口部署";
            return preview;
        }
        if (!bridge.TryGetBlock(request.center, out BlockInfo block))
        {
            preview.Error = "目标地块不存在";
            return preview;
        }
        float before, after;
        if (request.kind == SimulationInterventionKind.WaterCoverage)
        {
            preview.Error = "V4.1 不允许直接改变已建地块的陆水状态；请拆除后重建";
            return preview;
        }
        else
        {
            if (Environment == null)
            { preview.Error = "环境控制器未就绪"; return preview; }
            if (!Environment.TryPreview(request.center, request.kind, request.amount,
                out var target, out string error))
            {
                preview.Error = error;
                return preview;
            }
            var state = target.Before.Environment;
            before = request.kind == SimulationInterventionKind.Temperature ? (float)state.Temperature.Value :
                request.kind == SimulationInterventionKind.Humidity ? (float)state.Humidity.Value :
                request.kind == SimulationInterventionKind.PlantRecovery ? (float)state.Recovery.Value :
                request.kind == SimulationInterventionKind.Elevation ? state.Elevation : target.Before.PlantStock;
            after = (float)target.Target;
        }
        if (preview.Error == null)
            preview.Targets.Add(new SimulationInterventionTarget
            {
                Coordinate = request.center, Block = block, NextValue = after,
                Before = FormatValue(request.kind, before), After = FormatValue(request.kind, after)
            });
        return preview;
    }

    public bool TryApply(SimulationInterventionRequest request, out SimulationInterventionPreview result)
    {
        result = Preview(request);
        if (!result.IsValid)
        {
            OperationCommitted?.Invoke(request.center,
                "rejected:intervention:" + request.kind + ";amount=" + request.amount +
                ";reason=" + result.Error);
            return false;
        }
        bool applied = Environment.TryApply(request.center, request.kind, request.amount, out string error);
        if (!applied) result.Error = error;
        if (!applied) OperationCommitted?.Invoke(request.center,
            "rejected:intervention:" + request.kind + ";amount=" + request.amount +
            ";reason=" + error);
        else OperationCommitted?.Invoke(request.center,
            "intervention:" + request.kind + ";amount=" + request.amount);
        return applied;
    }

    public bool TryApplyCommunity(Vector2Int coordinate,
        IReadOnlyList<SimulationPopulationEdit> edits, out string error)
    {
        error = null;
        if (!isActiveAndEnabled || bridge == null || simulationController == null ||
            !bridge.TryGetBlock(coordinate, out BlockInfo block))
        {
            error = "目标地块已不存在";
            OperationCommitted?.Invoke(coordinate, "rejected:community_edit;reason=" + error);
            return false;
        }
        if (!simulationController.ApplyCommunityEdits(block, edits))
        {
            error = "群落输入无效、地块已改变，或纯水地块不能新增陆地种群";
            OperationCommitted?.Invoke(coordinate, "rejected:community_edit;reason=" + error);
            return false;
        }
        OperationCommitted?.Invoke(coordinate, "community_edit;entries=" + edits.Count);
        return true;
    }

    // Direct single-tile editor controls use the same runtime mutation boundary.
    public bool TryApplyEnvironment(Vector2Int coordinate,
        SimulationEnvironmentEdit edit, out string error)
    {
        error = null;
        if (!isActiveAndEnabled || Environment == null)
        {
            error = "环境控制器未就绪";
            OperationCommitted?.Invoke(coordinate, "rejected:environment_edit;reason=" + error);
            return false;
        }
        bool applied = Environment.TryEditEnvironment(coordinate, edit, out error);
        if (applied) OperationCommitted?.Invoke(coordinate,
            "environment_edit;T=" + edit.temperature + ";H=" + edit.humidity +
            ";Z=" + edit.elevation + ";R=" + edit.habitatRecovery +
            ";stock=" + edit.plantBiomass);
        else OperationCommitted?.Invoke(coordinate,
            "rejected:environment_edit;reason=" + error);
        return applied;
    }

    public void ApplyTuning(SimulationTuning tuning, float secondsPerDay)
    {
        simulationController.ApplyTuning(tuning);
        simulationTime.SetSecondsPerDay(secondsPerDay);
        OperationCommitted?.Invoke(Vector2Int.zero,
            "global_tuning;secondsPerDay=" + secondsPerDay +
            ";reproduction=" + tuning.reproductionScale +
            ";migration=" + tuning.migrationEnabled +
            ";mutation=" + tuning.mutationEnabled +
            ";niches=" + tuning.ecologicalNichesEnabled);
    }

    private static string FormatValue(SimulationInterventionKind kind, float value)
    {
        if (kind == SimulationInterventionKind.WaterCoverage)
            return ((WaterCoverage)(int)value).ToString();
        return kind == SimulationInterventionKind.PlantBiomass
            ? value.ToString("F1") : value.ToString("F0");
    }

}
