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
    public int durationDays;
    public WaterCoverage waterTarget;
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
    private sealed class OngoingEffect
    {
        public Vector2Int coordinate;
        public BlockInfo block;
        public SimulationInterventionKind kind;
        public float amount;
        public int remainingDays;
        public int lastAppliedDay;
    }

    [SerializeField] private MapGridManager gridManager;
    [SerializeField] private MapSimulationBridge bridge;
    [SerializeField] private SimulationController simulationController;
    [SerializeField] private SimulationTime simulationTime;
    [SerializeField, Range(0, 4)] private int maxRadius = 2;

    private readonly List<OngoingEffect> ongoing = new List<OngoingEffect>();
    private readonly List<LineRenderer> previewOutlines = new List<LineRenderer>();
    private readonly List<Vector2Int> previewCoordinates = new List<Vector2Int>();
    private readonly List<int> previewHeights = new List<int>();
    private Material previewMaterial;
    public int MaxRadius => maxRadius;
    public int OngoingEffectCount => ongoing.Count;

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

    private void OnEnable()
    {
        if (simulationController != null)
            simulationController.OnDaySimulated += ApplyOngoingEffects;
    }

    private void OnDisable()
    {
        if (simulationController != null)
            simulationController.OnDaySimulated -= ApplyOngoingEffects;
        ClearPreview();
    }

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
        if (request.radius < 0 || request.radius > maxRadius ||
            request.durationDays < 1 || request.durationDays > 30 ||
            float.IsNaN(request.amount) || float.IsInfinity(request.amount) ||
            Mathf.Abs(request.amount) > 1000000f)
        {
            preview.Error = "范围、持续天数或变化量不合法";
            return preview;
        }
        bool terrain = request.kind == SimulationInterventionKind.Elevation ||
                       request.kind == SimulationInterventionKind.WaterCoverage;
        if (terrain && request.durationDays != 1)
        {
            preview.Error = "地貌改造只能一次完成";
            return preview;
        }
        if (!bridge.TryGetBlock(request.center, out _))
        {
            preview.Error = "鼠标位置没有已放置地块";
            return preview;
        }

        for (int dx = -request.radius; dx <= request.radius; dx++)
        for (int dy = -request.radius; dy <= request.radius; dy++)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dy) > request.radius) continue;
            Vector2Int coordinate = request.center + new Vector2Int(dx, dy);
            if (!bridge.TryGetBlock(coordinate, out BlockInfo block)) continue;
            if (!TryPreviewTarget(request, coordinate, block,
                    out SimulationInterventionTarget target, out string error))
            {
                preview.Error = error;
                preview.Targets.Clear();
                return preview;
            }
            if (target != null) preview.Targets.Add(target);
        }

        if (preview.Targets.Count == 0)
            preview.Error = "目标地块的数值不会改变";
        return preview;
    }

    public bool TryApply(SimulationInterventionRequest request,
        out SimulationInterventionPreview result)
    {
        result = Preview(request);
        if (!result.IsValid) return false;

        // Validate the full range before starting any changes.
        foreach (SimulationInterventionTarget target in result.Targets)
        {
            if (!ApplyValue(request.kind, target.Block, target.Coordinate,
                    target.NextValue))
            {
                result.Error = "提交时地块状态已改变，请重新预览";
                return false;
            }
        }

        if (request.durationDays > 1)
            foreach (SimulationInterventionTarget target in result.Targets)
                ongoing.Add(new OngoingEffect
                {
                    coordinate = target.Coordinate,
                    block = target.Block,
                    kind = request.kind,
                    amount = request.amount,
                    remainingDays = request.durationDays - 1,
                    lastAppliedDay = simulationTime.currentDay
                });
        return true;
    }

    private static bool TryPreviewTarget(SimulationInterventionRequest request,
        Vector2Int coordinate, BlockInfo block,
        out SimulationInterventionTarget target, out string error)
    {
        target = null;
        error = null;
        if ((request.kind == SimulationInterventionKind.PlantBiomass ||
             request.kind == SimulationInterventionKind.PlantRecovery) &&
            HabitatTopology.IsPureWater(block))
        {
            error = "范围内有纯水地块，不能进行陆地植物干预";
            return false;
        }
        float before;
        float after;
        switch (request.kind)
        {
            case SimulationInterventionKind.Temperature:
                before = block.temperature;
                after = Mathf.Clamp(block.temperature + Mathf.RoundToInt(request.amount), 0, 100);
                break;
            case SimulationInterventionKind.Humidity:
                before = block.humidity;
                after = Mathf.Clamp(block.humidity + Mathf.RoundToInt(request.amount), 0, 100);
                break;
            case SimulationInterventionKind.PlantBiomass:
                before = block.plantBiomass;
                after = Mathf.Clamp(before + request.amount, 0f,
                    Mathf.Max(0f, block.maxPlantBiomass));
                break;
            case SimulationInterventionKind.PlantRecovery:
                before = block.habitatRecovery;
                after = Mathf.Max(0, block.habitatRecovery + Mathf.RoundToInt(request.amount));
                break;
            case SimulationInterventionKind.Elevation:
                before = block.elevation;
                after = block.elevation + Mathf.RoundToInt(request.amount);
                if (after < 0 || after > 2)
                {
                    error = "范围内有地块的目标高度超出 0–2";
                    return false;
                }
                break;
            case SimulationInterventionKind.WaterCoverage:
                if (request.waterTarget != WaterCoverage.Land &&
                    request.waterTarget != WaterCoverage.Lake)
                {
                    error = "P1 仅支持陆地与湖水基础状态";
                    return false;
                }
                if (block.community != null)
                    foreach (PopulationData population in block.community)
                        if (population != null && population.speciesAmount > 0)
                        {
                            error = "范围内有存活种群，不能直接改变水陆状态";
                            return false;
                        }
                before = (int)block.waterCoverage;
                after = (int)request.waterTarget;
                break;
            default:
                error = "未知干预类型";
                return false;
        }

        if (Mathf.Abs(after - before) < 0.0001f) return true;
        target = new SimulationInterventionTarget
        {
            Coordinate = coordinate,
            Block = block,
            NextValue = after,
            Before = FormatValue(request.kind, before),
            After = FormatValue(request.kind, after)
        };
        return true;
    }

    private static string FormatValue(SimulationInterventionKind kind, float value)
    {
        if (kind == SimulationInterventionKind.WaterCoverage)
            return ((WaterCoverage)(int)value).ToString();
        return kind == SimulationInterventionKind.PlantBiomass
            ? value.ToString("F1") : value.ToString("F0");
    }

    private bool ApplyValue(SimulationInterventionKind kind, BlockInfo block,
        Vector2Int coordinate, float value)
    {
        switch (kind)
        {
            case SimulationInterventionKind.Temperature:
                return simulationController.ApplyEnvironment(block, (int)value,
                    block.humidity, block.habitatRecovery);
            case SimulationInterventionKind.Humidity:
                return simulationController.ApplyEnvironment(block, block.temperature,
                    (int)value, block.habitatRecovery);
            case SimulationInterventionKind.PlantBiomass:
                return simulationController.ApplyPlantBiomass(block, value);
            case SimulationInterventionKind.PlantRecovery:
                return simulationController.ApplyEnvironment(block, block.temperature,
                    block.humidity, (int)value);
            case SimulationInterventionKind.Elevation:
                return gridManager.TrySetTileHeight(coordinate, (int)value);
            case SimulationInterventionKind.WaterCoverage:
                return simulationController.ApplyWaterCoverage(block,
                    (WaterCoverage)(int)value);
            default:
                return false;
        }
    }

    private void ApplyOngoingEffects(int day)
    {
        for (int i = ongoing.Count - 1; i >= 0; i--)
        {
            OngoingEffect effect = ongoing[i];
            if (day <= effect.lastAppliedDay) continue;
            if (!bridge.TryGetBlock(effect.coordinate, out BlockInfo currentBlock) ||
                currentBlock != effect.block)
            {
                ongoing.RemoveAt(i);
                continue;
            }
            effect.lastAppliedDay = day;
            SimulationInterventionRequest step = new SimulationInterventionRequest
            {
                kind = effect.kind,
                amount = effect.amount,
                durationDays = 1
            };
            if (TryPreviewTarget(step, effect.coordinate, effect.block,
                    out SimulationInterventionTarget target, out _) && target != null)
                ApplyValue(effect.kind, effect.block, effect.coordinate, target.NextValue);
            effect.remainingDays--;
            if (effect.remainingDays <= 0) ongoing.RemoveAt(i);
        }
    }
}
