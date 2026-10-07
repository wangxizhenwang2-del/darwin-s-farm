using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PopulationSimulatorWindow
{
    private sealed class LiveSeries
    {
        public string name;
        public Color color;
        public readonly List<float> amounts = new List<float>();
    }

    private sealed class LiveTileHistory
    {
        public readonly List<int> days = new List<int>();
        public readonly List<float> plants = new List<float>();
        public readonly Dictionary<PopulationData, LiveSeries> populations =
            new Dictionary<PopulationData, LiveSeries>();
        public readonly List<string> output = new List<string>();
    }

    private readonly Dictionary<BlockInfo, LiveTileHistory> liveHistory =
        new Dictionary<BlockInfo, LiveTileHistory>();
    private SimulationController liveRecordingSource;
    private BlockInfo liveEnvironmentBlock;
    private SimulationEnvironmentEdit liveEnvironment;
    private SimulationController liveTuningSource;
    private SimulationTuning liveTuning;
    private float liveSecondsPerDay;
    private bool liveFixedExpanded = true;
    private Vector2 liveOutputScroll;

    private void EnsureLiveRecording()
    {
        if (liveRecordingSource == liveSimulation) return;
        StopLiveRecording();
        liveRecordingSource = liveSimulation;
        liveRecordingSource.OnDaySimulated += RecordLiveDay;
        RecordLiveDay(liveTime.currentDay);
    }

    private void StopLiveRecording()
    {
        if (liveRecordingSource != null)
            liveRecordingSource.OnDaySimulated -= RecordLiveDay;
        liveRecordingSource = null;
        liveTuningSource = null;
        liveEnvironmentBlock = null;
        liveHistory.Clear();
    }

    private void RecordLiveDay(int day)
    {
        if (liveGrid == null) return;
        foreach (MapTileInstance tile in liveGrid.GetPlacedTiles())
        {
            BlockInfo site = tile.Block;
            if (site == null) continue;
            if (!liveHistory.TryGetValue(site, out LiveTileHistory history))
            {
                history = new LiveTileHistory();
                liveHistory.Add(site, history);
            }
            if (history.days.Count > 0 && history.days[history.days.Count - 1] == day)
                continue;
            history.days.Add(day);
            history.plants.Add(site.plantBiomass);
            foreach (LiveSeries series in history.populations.Values)
                series.amounts.Add(0f);
            if (site.community != null)
                foreach (PopulationData population in site.community)
                {
                    if (population == null) continue;
                    if (!history.populations.TryGetValue(population, out LiveSeries series))
                    {
                        int index = history.populations.Count;
                        series = new LiveSeries
                        {
                            color = Color.HSVToRGB((index * 0.618034f + 0.53f) % 1f,
                                0.7f, 0.95f)
                        };
                        for (int i = 0; i < history.days.Count; i++)
                            series.amounts.Add(0f);
                        history.populations.Add(population, series);
                    }
                    series.name = population.species != null
                        ? population.species.SpeciesName : population.lineageName;
                    series.amounts[series.amounts.Count - 1] = population.speciesAmount;
                }
            if (day == 0) continue;
            history.output.Add("Day " + day + "  植物 " + site.plantBiomass.ToString("F1") +
                "  生长 " + site.plantGrowthToday.ToString("F1") +
                "  摄食 " + site.consumedBiomassToday.ToString("F1") +
                "  温/湿 " + site.temperature + "/" + site.humidity);
            if (site.community != null)
                foreach (PopulationData population in site.community)
                {
                    if (population == null) continue;
                    history.output.Add("Day " + day + "  " +
                        (population.species != null ? population.species.SpeciesName :
                            population.lineageName) + "  数量 " + population.speciesAmount +
                        "  出生 " + population.birthsToday +
                        "  死亡 " + population.deathsToday);
                }
        }
        Repaint();
    }

    private void LogLive(BlockInfo block, string message)
    {
        if (block == null || !liveHistory.TryGetValue(block, out LiveTileHistory history))
            return;
        history.output.Add("Day " + liveTime.currentDay + "  " + message);
        if (history.days.Count == 0 ||
            history.days[history.days.Count - 1] != liveTime.currentDay) return;
        int last = history.days.Count - 1;
        history.plants[last] = block.plantBiomass;
        foreach (LiveSeries series in history.populations.Values)
            series.amounts[last] = 0f;
        if (block.community == null) return;
        foreach (PopulationData population in block.community)
        {
            if (population == null) continue;
            if (!history.populations.TryGetValue(population, out LiveSeries series))
            {
                int index = history.populations.Count;
                series = new LiveSeries
                {
                    color = Color.HSVToRGB((index * 0.618034f + 0.53f) % 1f,
                        0.7f, 0.95f)
                };
                for (int i = 0; i < history.days.Count; i++)
                    series.amounts.Add(0f);
                history.populations.Add(population, series);
            }
            series.name = population.species != null
                ? population.species.SpeciesName : population.lineageName;
            series.amounts[last] = population.speciesAmount;
        }
    }

    private void ReadLiveEnvironment(BlockInfo block)
    {
        liveEnvironment = new SimulationEnvironmentEdit
        {
            temperature = block.temperature,
            humidity = block.humidity,
            elevation = block.elevation,
            habitatRecovery = block.habitatRecovery,
            maxPlantBiomass = block.maxPlantBiomass,
            plantBiomass = block.plantBiomass
        };
        liveEnvironmentBlock = block;
    }

    private void DrawLiveEnvironment(BlockInfo block)
    {
        if (liveEnvironmentBlock != block) ReadLiveEnvironment(block);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("环境数据 · 单格直接调节", EditorStyles.boldLabel);
        liveEnvironment.temperature = EditorGUILayout.IntSlider("温度",
            liveEnvironment.temperature, 0, 100);
        liveEnvironment.humidity = EditorGUILayout.IntSlider("湿度",
            liveEnvironment.humidity, 0, 100);
        liveEnvironment.elevation = EditorGUILayout.IntSlider("海拔",
            liveEnvironment.elevation, 0, 2);
        liveEnvironment.maxPlantBiomass = DarwinFarm.Environment.EnvironmentRules.PlantCapacity;
        EditorGUILayout.LabelField("植物生物量上限", "1,000,000（固定）");
        liveEnvironment.plantBiomass = Mathf.Clamp(
            EditorGUILayout.FloatField("植物生物量", liveEnvironment.plantBiomass),
            0f, liveEnvironment.maxPlantBiomass);
        liveEnvironment.habitatRecovery = Mathf.Clamp(
            EditorGUILayout.IntField("每日恢复量", liveEnvironment.habitatRecovery), 0,
            DarwinFarm.Environment.EnvironmentRules.MaximumRecovery);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("应用环境修改"))
        {
            bool success = liveInterventions.TryApplyEnvironment(liveCoordinate,
                liveEnvironment, out string error);
            liveMessage = success ? "环境修改已应用" : error;
            if (success)
            {
                LogLive(block, "环境数据已修改");
                ReadLiveEnvironment(block);
            }
        }
        if (GUILayout.Button("读取当前环境")) ReadLiveEnvironment(block);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawLiveTuning()
    {
        if (liveTuningSource != liveSimulation)
        {
            liveTuning = liveSimulation.ReadTuning();
            liveSecondsPerDay = liveTime.SecondsPerDay;
            liveTuningSource = liveSimulation;
        }
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        liveFixedExpanded = EditorGUILayout.Foldout(liveFixedExpanded,
            "固定关键变量 · 全局模拟", true);
        if (liveFixedExpanded)
        {
            liveTuning.reproductionScale = Mathf.Max(0f,
                EditorGUILayout.FloatField("繁殖系数", liveTuning.reproductionScale));
            liveTuning.overCapacityDeathRate = EditorGUILayout.Slider("超载衰退上限",
                liveTuning.overCapacityDeathRate, 0f, 1f);
            liveTuning.starvationDeathRate = EditorGUILayout.Slider("最大饥饿死亡率",
                liveTuning.starvationDeathRate, 0f, 1f);
            liveTuning.levelOnePreyFraction = EditorGUILayout.Slider("一级捕食比例 (%)",
                liveTuning.levelOnePreyFraction * 100f, 0f, 100f) / 100f;
            liveTuning.levelTwoPreyFraction = EditorGUILayout.Slider("二级捕食比例 (%)",
                liveTuning.levelTwoPreyFraction * 100f, 0f, 100f) / 100f;
            liveTuning.mutationEnabled = EditorGUILayout.Toggle("启用变异",
                liveTuning.mutationEnabled);
            liveTuning.mutationInterval = Mathf.Max(1,
                EditorGUILayout.IntField("变异间隔（天）", liveTuning.mutationInterval));
            liveTuning.mutationStep = Mathf.Max(0f,
                EditorGUILayout.FloatField("变异步长", liveTuning.mutationStep));
            liveTuning.selectionStrength = Mathf.Max(0f,
                EditorGUILayout.FloatField("选择强度", liveTuning.selectionStrength));
            liveTuning.ecologicalNichesEnabled = EditorGUILayout.Toggle("启用生态位",
                liveTuning.ecologicalNichesEnabled);
            liveTuning.migrationEnabled = EditorGUILayout.Toggle("启用迁徙",
                liveTuning.migrationEnabled);
            liveTuning.migrationInterval = Mathf.Max(1,
                EditorGUILayout.IntField("迁徙间隔（天）", liveTuning.migrationInterval));
            liveTuning.migrationPopulationScale = Mathf.Max(0.01f,
                EditorGUILayout.FloatField("基础概率规模系数", liveTuning.migrationPopulationScale));
            liveTuning.overloadThreshold = EditorGUILayout.Slider("超载阈值",
                liveTuning.overloadThreshold, 0f, 0.99f);
            liveTuning.baseOverloadProbability = EditorGUILayout.Slider("基础超载概率",
                liveTuning.baseOverloadProbability, 0f, 1f);
            liveTuning.overloadMigrationFraction = EditorGUILayout.Slider("超载迁出比例",
                liveTuning.overloadMigrationFraction, 0f, 1f);
            liveTuning.migrationCooldownDays = Mathf.Max(0,
                EditorGUILayout.IntField("迁徙冷却（天）", liveTuning.migrationCooldownDays));
            liveSecondsPerDay = Mathf.Max(0.05f,
                EditorGUILayout.FloatField("每模拟日秒数", liveSecondsPerDay));
            EditorGUILayout.LabelField("这些参数作用于整张地图。", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("应用固定数值"))
            {
                liveInterventions.ApplyTuning(liveTuning, liveSecondsPerDay);
                liveMessage = "全局固定数值已应用";
                if (liveBridge.TryGetBlock(liveCoordinate, out BlockInfo block))
                    LogLive(block, "全局固定数值已修改");
            }
            if (GUILayout.Button("读取当前参数"))
            {
                liveTuning = liveSimulation.ReadTuning();
                liveSecondsPerDay = liveTime.SecondsPerDay;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawLiveChart(BlockInfo block)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("种群数量 / 植物库存变化", EditorStyles.boldLabel);
        if (!liveHistory.TryGetValue(block, out LiveTileHistory history) ||
            history.days.Count == 0)
        {
            EditorGUILayout.LabelField("暂无记录");
            EditorGUILayout.EndVertical();
            return;
        }
        EditorGUILayout.LabelField("从窗口连接游戏时开始记录", EditorStyles.miniLabel);
        Rect area = GUILayoutUtility.GetRect(100f, 245f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(area, new Color(0.13f, 0.14f, 0.17f));
        Rect plot = new Rect(area.x + 46f, area.y + 14f,
            Mathf.Max(10f, area.width - 100f), area.height - 44f);
        float maxPopulation = 1f, maxPlant = 1f;
        foreach (LiveSeries series in history.populations.Values)
            foreach (float amount in series.amounts)
                maxPopulation = Mathf.Max(maxPopulation, amount);
        foreach (float amount in history.plants)
            maxPlant = Mathf.Max(maxPlant, amount);
        GUIStyle label = new GUIStyle(EditorStyles.miniLabel);
        label.normal.textColor = Color.white;
        Handles.BeginGUI();
        Handles.color = new Color(0.32f, 0.34f, 0.38f);
        for (int i = 0; i <= 4; i++)
        {
            float y = plot.yMax - i * plot.height / 4f;
            Handles.DrawLine(new Vector3(plot.x, y), new Vector3(plot.xMax, y));
            GUI.Label(new Rect(area.x + 2f, y - 9f, 42f, 18f),
                (maxPopulation * i / 4f).ToString("F0"), label);
            GUI.Label(new Rect(plot.xMax + 3f, y - 9f, 50f, 18f),
                (maxPlant * i / 4f).ToString("F0"), label);
        }
        foreach (LiveSeries series in history.populations.Values)
            DrawLiveLine(series.amounts, maxPopulation, plot, series.color);
        DrawLiveLine(history.plants, maxPlant, plot,
            new Color(0.45f, 0.9f, 0.35f));
        Handles.EndGUI();
        GUI.Label(new Rect(plot.x, plot.yMax + 3f, 75f, 18f),
            "Day " + history.days[0], label);
        GUI.Label(new Rect(plot.xMax - 75f, plot.yMax + 3f, 75f, 18f),
            "Day " + history.days[history.days.Count - 1], label);
        EditorGUILayout.BeginHorizontal();
        foreach (LiveSeries series in history.populations.Values)
        {
            GUIStyle legend = new GUIStyle(EditorStyles.miniLabel);
            legend.normal.textColor = series.color;
            GUILayout.Label("● " + series.name, legend);
        }
        GUIStyle plantLegend = new GUIStyle(EditorStyles.miniLabel);
        plantLegend.normal.textColor = new Color(0.45f, 0.9f, 0.35f);
        GUILayout.Label("● 植物（右轴）", plantLegend);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private static void DrawLiveLine(List<float> values, float maximum,
        Rect plot, Color color)
    {
        if (values.Count < 2) return;
        Vector3[] points = new Vector3[values.Count];
        for (int i = 0; i < points.Length; i++)
            points[i] = new Vector3(plot.x + i * plot.width / (points.Length - 1),
                plot.yMax - plot.height * values[i] / maximum);
        Handles.color = color;
        Handles.DrawAAPolyLine(2f, points);
    }

    private void DrawLiveOutput(BlockInfo block)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("每日输出", EditorStyles.boldLabel);
        liveOutputScroll = EditorGUILayout.BeginScrollView(liveOutputScroll,
            GUILayout.Height(220f));
        if (liveHistory.TryGetValue(block, out LiveTileHistory history))
            for (int i = history.output.Count - 1; i >= 0; i--)
                EditorGUILayout.LabelField(history.output[i]);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }
}
