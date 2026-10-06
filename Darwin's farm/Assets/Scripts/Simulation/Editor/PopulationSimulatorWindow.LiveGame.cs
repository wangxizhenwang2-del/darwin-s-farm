using System.Collections.Generic;
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
    [SerializeField] private int livePage;
    [SerializeField] private int liveEditPage;
    private readonly List<PopulationInput> livePopulations = new List<PopulationInput>();
    private BlockInfo liveDraftBlock;

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
        {
            liveMessage = null;
            liveDraftBlock = null;
            liveEnvironmentBlock = null;
        }
        liveCoordinate = tile.Coordinate;
        hasLiveCoordinate = true;
    }

    private void DrawLiveGame()
    {
        if (!EditorApplication.isPlaying)
        {
            hasLiveCoordinate = false;
            liveDraftBlock = null;
            livePopulations.Clear();
            StopLiveRecording();
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

        EnsureLiveRecording();

        liveScroll = EditorGUILayout.BeginScrollView(liveScroll);
        EditorGUILayout.LabelField("地块 " + liveCoordinate + "    ·    Day " +
            liveTime.currentDay, EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("继续")) liveTime.Play();
        if (GUILayout.Button("暂停")) liveTime.Pause();
        if (GUILayout.Button("2 倍速")) liveTime.DoubleSpeed();
        if (GUILayout.Button("下一天")) liveTime.NextDay();
        EditorGUILayout.EndHorizontal();
        int nextPage = GUILayout.Toolbar(livePage, new[] { "地块概览", "编辑与干预", "趋势与每日输出" });
        if (nextPage != livePage)
        {
            livePage = nextPage;
            if (livePage != 1) liveInterventions.ClearPreview();
        }
        EditorGUILayout.Space(6f);
        if (livePage == 0)
        {
            DrawLiveOverview(block);
        }
        else if (livePage == 1)
        {
            int nextEditPage = GUILayout.Toolbar(liveEditPage,
                new[] { "环境", "种群", "固定变量", "范围干预" });
            if (nextEditPage != liveEditPage)
            {
                liveEditPage = nextEditPage;
                if (liveEditPage != 3) liveInterventions.ClearPreview();
            }
            EditorGUILayout.Space(4f);
            switch (liveEditPage)
            {
                case 0: DrawLiveEnvironment(block); break;
                case 1: DrawLiveCommunity(block); break;
                case 2: DrawLiveTuning(); break;
                case 3: DrawLiveIntervention(); break;
            }
        }
        else
        {
            DrawLiveChart(block);
            DrawLiveOutput(block);
        }
        if (!string.IsNullOrEmpty(liveMessage))
            EditorGUILayout.HelpBox(liveMessage, MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    private void DrawLiveOverview(BlockInfo block)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("环境数据", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("温度 " + block.temperature +
            "    湿度 " + block.humidity + "    高度 " + block.elevation);
        EditorGUILayout.LabelField("基础状态 " + block.waterCoverage);
        EditorGUILayout.LabelField("植物库存 " + block.plantBiomass.ToString("F1") +
            " / " + block.maxPlantBiomass.ToString("F1") +
            "    每日恢复 " + block.habitatRecovery);
        EditorGUILayout.LabelField("今日生长 " + block.plantGrowthToday.ToString("F1") +
            "    今日消耗 " + block.consumedBiomassToday.ToString("F1"));
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
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
        EditorGUILayout.EndVertical();
    }

    private void ReadLiveCommunity(BlockInfo block)
    {
        livePopulations.Clear();
        if (block.community != null)
            foreach (PopulationData population in block.community)
            {
                if (population == null) continue;
                livePopulations.Add(new PopulationInput
                {
                    name = population.species != null
                        ? population.species.SpeciesName : population.lineageName,
                    data = CopyPopulation(population),
                    runtimePopulation = population
                });
            }
        liveDraftBlock = block;
    }

    private void DrawLiveCommunity(BlockInfo block)
    {
        if (liveDraftBlock != block) ReadLiveCommunity(block);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("群落配置（待应用）", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("现有种群保留当前数量；初始数量只用于新增种群。",
            EditorStyles.miniLabel);
        int removeIndex = -1;
        for (int i = 0; i < livePopulations.Count; i++)
        {
            PopulationInput input = livePopulations[i];
            if (input.data == null) input.data = new PopulationData();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            input.expanded = EditorGUILayout.Foldout(input.expanded, input.name, true);
            if (GUILayout.Button("删除", GUILayout.Width(50f))) removeIndex = i;
            EditorGUILayout.EndHorizontal();
            if (input.expanded)
            {
                input.name = EditorGUILayout.TextField("名称（无物种资产时作同族标识）", input.name);
                DrawInitialTrophicLevel(input.data);
                if (input.runtimePopulation != null)
                    EditorGUILayout.LabelField("当前数量", input.runtimePopulation.speciesAmount.ToString());
                else
                    input.data.speciesAmount = Mathf.Max(0,
                        EditorGUILayout.IntField("新种群初始数量", input.data.speciesAmount));
                input.data.fitTemperature = EditorGUILayout.IntSlider("适宜温度", input.data.fitTemperature, 0, 100);
                input.data.fitHumidity = EditorGUILayout.IntSlider("适宜湿度", input.data.fitHumidity, 0, 100);
                input.data.size = EditorGUILayout.IntSlider("体型", input.data.size, 1, 100);
                input.data.movementAbility = EditorGUILayout.IntSlider("运动能力", input.data.movementAbility, 0, 100);
                input.data.fertility = EditorGUILayout.IntSlider("繁殖能力", input.data.fertility, 0, 100);
            }
            EditorGUILayout.EndVertical();
        }
        if (removeIndex >= 0) livePopulations.RemoveAt(removeIndex);
        if (GUILayout.Button("+ 添加种群"))
            livePopulations.Add(CreatePopulation("种群 " + (livePopulations.Count + 1)));
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("应用群落修改到游戏"))
        {
            List<SimulationPopulationEdit> edits = new List<SimulationPopulationEdit>();
            foreach (PopulationInput input in livePopulations)
                edits.Add(new SimulationPopulationEdit
                {
                    Resident = input.runtimePopulation,
                    Draft = CopyPopulationInput(input)
                });
            bool success = liveInterventions.TryApplyCommunity(liveCoordinate, edits,
                out string error);
            liveMessage = success ? "群落修改已应用" : error;
            if (success)
            {
                LogLive(block, "群落配置已修改");
                ReadLiveCommunity(block);
            }
        }
        if (GUILayout.Button("读取当前群落"))
        {
            ReadLiveCommunity(block);
            liveMessage = "已读取当前地块的群落";
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawLiveIntervention()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
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
                if (success && liveBridge.TryGetBlock(liveCoordinate, out BlockInfo site))
                    LogLive(site, "环境干预 " + request.kind +
                        "，影响 " + result.Targets.Count + " 格");
                liveInterventions.ClearPreview();
            }
        EditorGUILayout.EndVertical();
    }
}
