using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public partial class PopulationSimulatorWindow
{
    [SerializeField] private bool liveGameMode;
    [SerializeField] private SimulationInterventionKind liveKind;
    [SerializeField] private float liveAmount = 5f;
    [SerializeField] private int liveRadius;
    [SerializeField] private int liveDurationDays = 1;
    [SerializeField] private WaterCoverage liveWaterTarget = WaterCoverage.Lake;

    private MapGridManager liveGrid;
    private MapSimulationBridge liveBridge;
    private SimulationInterventionController liveInterventions;
    private SimulationController liveSimulation;
    private SimulationTime liveTime;
    private Vector2Int liveCoordinate;
    private bool hasLiveCoordinate;
    private string liveMessage;
    private Vector2 liveScroll;

    private void ResolveLiveGame()
    {
        if (liveBridge == null) liveBridge = Object.FindFirstObjectByType<MapSimulationBridge>();
        if (liveBridge == null) return;
        if (liveGrid == null) liveGrid = liveBridge.GetComponent<MapGridManager>();
        if (liveInterventions == null)
            liveInterventions = liveBridge.GetComponent<SimulationInterventionController>();
        if (liveSimulation == null)
            liveSimulation = liveBridge.GetComponent<SimulationController>();
        if (liveTime == null) liveTime = liveBridge.GetComponent<SimulationTime>();
    }

    private void UpdateLiveHover()
    {
        ResolveLiveGame();
        if (liveGrid == null || Mouse.current == null || Camera.main == null) return;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        if (mousePosition.x < 0f || mousePosition.y < 0f ||
            mousePosition.x >= Screen.width || mousePosition.y >= Screen.height)
            return;
        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject()) return;

        Ray ray = Camera.main.ScreenPointToRay(mousePosition);
        int groundMask = LayerMask.GetMask("Ground");
        if (groundMask == 0 || !Physics.Raycast(ray, out RaycastHit hit,
                1000f, groundMask, QueryTriggerInteraction.Ignore))
            return;

        MapTileInstance tile = hit.collider.GetComponentInParent<MapTileInstance>();
        if (tile == null || tile.Block == null) return;
        if (!hasLiveCoordinate || liveCoordinate != tile.Coordinate)
            liveMessage = null;
        liveCoordinate = tile.Coordinate;
        hasLiveCoordinate = true;
    }

    private void DrawLiveGame()
    {
        if (!EditorApplication.isPlaying)
        {
            hasLiveCoordinate = false;
            EditorGUILayout.HelpBox("进入 Play 模式后，把鼠标移到 Game 视图中的地块上。", MessageType.Info);
            return;
        }
        ResolveLiveGame();
        if (liveGrid == null || liveBridge == null ||
            liveSimulation == null || liveInterventions == null || liveTime == null)
        {
            EditorGUILayout.HelpBox("当前场景缺少地图、模拟器或干预控制器。", MessageType.Warning);
            return;
        }
        if (!hasLiveCoordinate ||
            !liveBridge.TryGetBlock(liveCoordinate, out BlockInfo block))
        {
            EditorGUILayout.HelpBox("把鼠标移到 Game 视图中的已放置地块上。", MessageType.Info);
            return;
        }

        liveScroll = EditorGUILayout.BeginScrollView(liveScroll);
        EditorGUILayout.LabelField("最近鼠标指向的地块 " + liveCoordinate +
            "    Day " + liveTime.currentDay, EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("继续")) liveTime.Play();
        if (GUILayout.Button("暂停")) liveTime.Pause();
        if (GUILayout.Button("2 倍速")) liveTime.DoubleSpeed();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("温度 " + block.temperature +
            "    湿度 " + block.humidity + "    高度 " + block.elevation);
        EditorGUILayout.LabelField("基础状态 " + block.waterCoverage);
        EditorGUILayout.LabelField("植物库存 " + block.plantBiomass.ToString("F1") +
            " / " + block.maxPlantBiomass.ToString("F1") +
            "    每日恢复 " + block.habitatRecovery);
        EditorGUILayout.LabelField("今日生长 " + block.plantGrowthToday.ToString("F1") +
            "    今日消耗 " + block.consumedBiomassToday.ToString("F1"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("当前种群", EditorStyles.boldLabel);
        bool anyPopulation = false;
        if (block.community != null)
            foreach (PopulationData population in block.community)
            {
                if (population == null || population.speciesAmount <= 0) continue;
                anyPopulation = true;
                string name = population.species != null
                    ? population.species.SpeciesName
                    : string.IsNullOrEmpty(population.lineageName)
                        ? "未命名种群" : population.lineageName;
                EditorGUILayout.LabelField(name + "  数量 " + population.speciesAmount +
                    "  K " + population.carryingCapacity.ToString("F1") +
                    "  适应 " + population.environmentalFitness);
                EditorGUILayout.LabelField("出生 " + population.birthsToday +
                    "  死亡 " + population.deathsToday +
                    "  适温/适湿 " + population.fitTemperature +
                    "/" + population.fitHumidity, EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    liveSimulation.GetExpectedEvolutionDirection(block, population),
                    EditorStyles.miniLabel);
            }
        if (!anyPopulation)
            EditorGUILayout.LabelField("本地块暂无存活种群");

        DrawLiveIntervention();
        EditorGUILayout.EndScrollView();
    }

    private void DrawLiveIntervention()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("环境干预（开发测试入口）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("经济系统尚未接入，本页暂不扣费。范围内只修改已放置地块。",
            MessageType.Info);

        liveKind = (SimulationInterventionKind)EditorGUILayout.EnumPopup("操作", liveKind);
        liveRadius = EditorGUILayout.IntSlider("范围半径（四邻距离）", liveRadius,
            0, liveInterventions.MaxRadius);
        bool terrain = liveKind == SimulationInterventionKind.Elevation ||
                       liveKind == SimulationInterventionKind.WaterCoverage;
        if (liveKind == SimulationInterventionKind.WaterCoverage)
        {
            liveWaterTarget = (WaterCoverage)EditorGUILayout.EnumPopup("目标状态", liveWaterTarget);
        }
        else
        {
            liveAmount = EditorGUILayout.FloatField("每次变化量（可为负）", liveAmount);
        }
        liveDurationDays = terrain ? 1 : EditorGUILayout.IntSlider("持续天数", liveDurationDays, 1, 30);

        SimulationInterventionRequest request = new SimulationInterventionRequest
        {
            kind = liveKind,
            center = liveCoordinate,
            radius = liveRadius,
            amount = liveAmount,
            durationDays = liveDurationDays,
            waterTarget = liveWaterTarget
        };
        SimulationInterventionPreview preview = liveInterventions.Preview(request);
        liveInterventions.ShowPreview(preview);
        if (!preview.IsValid)
            EditorGUILayout.HelpBox(preview.Error, MessageType.Warning);
        else
        {
            EditorGUILayout.LabelField("范围预览：" + preview.Targets.Count + " 个地块",
                EditorStyles.boldLabel);
            foreach (SimulationInterventionTarget target in preview.Targets)
                EditorGUILayout.LabelField(target.Coordinate + "    " +
                    target.Before + " → " + target.After);
            if (liveDurationDays > 1)
                EditorGUILayout.LabelField("首次立即生效，之后每个模拟日结算后继续作用。",
                    EditorStyles.miniLabel);
        }

        using (new EditorGUI.DisabledScope(!preview.IsValid))
            if (GUILayout.Button("应用到预览地块"))
            {
                bool success = liveInterventions.TryApply(request,
                    out SimulationInterventionPreview result);
                liveMessage = success
                    ? "已应用到 " + result.Targets.Count + " 个地块"
                    : result.Error;
                liveInterventions.ClearPreview();
            }
        if (!string.IsNullOrEmpty(liveMessage))
            EditorGUILayout.HelpBox(liveMessage, MessageType.Info);
    }
}
