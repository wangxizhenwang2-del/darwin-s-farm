using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PopulationSimulatorWindow : EditorWindow
{
    [Serializable]
    private class PopulationInput
    {
        public string name;
        public PopulationData data = new PopulationData();
        public bool expanded = true;
        [NonSerialized] public PopulationData runtimePopulation;
    }

    private class PopulationHistory
    {
        public string name;
        public Color color;
        public List<float> amounts = new List<float>();
    }

    [SerializeField] private int temperature = 50;
    [SerializeField] private int humidity = 50;
    [SerializeField] private float plantBiomass = 1000f;
    [SerializeField] private float maxPlantBiomass = 10000f;
    [SerializeField] private int habitatRecovery = 100;

    [SerializeField] private float reproductionScale = 0.05f;
    [SerializeField] private float maxOverCapacityDeathRate = 0.1f;
    [SerializeField] private float maxStarvationDeathRate = 0.6f;
    [SerializeField] private bool mutationEnabled = true;
    [SerializeField] private bool ecologicalNichesEnabled;
    [SerializeField] private int mutationInterval = 7;
    [SerializeField] private float mutationStep = 2f;
    [SerializeField] private float selectionStrength = 1f;
    [SerializeField] private float secondsPerDay = 1f;
    [SerializeField] private List<PopulationInput> populations = new List<PopulationInput>();
    [SerializeField] private bool initialized;

    private static readonly int[] speeds = { 1, 2, 4, 8 };
    private static readonly string[] traitNames = { "适温", "适湿", "运动", "体型", "生育", "食性" };
    private static readonly string[] trophicLevelNames =
        { "0 · 植食", "1 · 捕食 0 级", "2 · 捕食 1 级" };
    private GameObject runtimeObject;
    private BlockInfo block;
    private SimulationController controller;
    private readonly List<PopulationHistory> history = new List<PopulationHistory>();
    private readonly List<float> plantHistory = new List<float>();
    private readonly List<string> dailyOutput = new List<string>();
    private Vector2 scrollPosition;
    private Vector2 outputScroll;
    private double lastUpdate;
    private double elapsedTime;
    private int currentDay;
    private int speedIndex;
    private bool running;

    [MenuItem("Darwin's Farm/种群模拟器")]
    public static void Open()
    {
        PopulationSimulatorWindow window = GetWindow<PopulationSimulatorWindow>("种群数值模拟器");
        window.minSize = new Vector2(520f, 640f);
    }

    private void OnEnable()
    {
        if (populations == null)
        {
            populations = new List<PopulationInput>();
        }

        if (!initialized)
        {
            populations.Add(CreatePopulation("种群 A"));
            populations.Add(CreatePopulation("种群 B"));
            initialized = true;
        }

        lastUpdate = EditorApplication.timeSinceStartup;
        EditorApplication.update -= UpdateSimulation;
        EditorApplication.update += UpdateSimulation;
    }

    private void OnDisable()
    {
        EditorApplication.update -= UpdateSimulation;
        DestroyRuntime();
    }

    private void OnGUI()
    {
        DrawControls();

        bool nextMigrationEnabled = EditorGUILayout.Toggle("启用迁徙", migrationEnabled);
        if (nextMigrationEnabled != migrationEnabled)
        {
            migrationEnabled = nextMigrationEnabled;
            if (controller != null) RestartSimulation();
        }
        EditorGUILayout.LabelField("切换迁徙模式会按当前输入重新开始模拟。", EditorStyles.miniLabel);
        ecologicalNichesEnabled = EditorGUILayout.Toggle("启用生态位（预留）",
            ecologicalNichesEnabled);
        if (controller != null)
            controller.SetEcologicalNichesEnabled(ecologicalNichesEnabled);
        EditorGUILayout.LabelField("生态位规则尚未接入每日模拟。", EditorStyles.miniLabel);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        if (migrationEnabled)
        {
            DrawDualSimulator();
            EditorGUILayout.EndScrollView();
            return;
        }
        DrawEnvironment();
        DrawParameters();
        DrawPopulations();
        DrawCurrentState();
        DrawChart();
        DrawDailyOutput();
        EditorGUILayout.EndScrollView();
    }

    private void DrawControls()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Start", EditorStyles.toolbarButton))
        {
            if (block == null)
            {
                RestartSimulation();
            }
            running = true;
            lastUpdate = EditorApplication.timeSinceStartup;
        }

        if (GUILayout.Button("Pause", EditorStyles.toolbarButton))
        {
            running = false;
        }

        if (GUILayout.Button("Speed x" + speeds[speedIndex], EditorStyles.toolbarButton))
        {
            speedIndex = (speedIndex + 1) % speeds.Length;
        }

        if (GUILayout.Button("Restart", EditorStyles.toolbarButton))
        {
            RestartSimulation();
        }

        if (GUILayout.Button("Next Day", EditorStyles.toolbarButton))
        {
            if (block == null)
            {
                RestartSimulation();
            }
            SimulateOneDay();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawEnvironment()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("环境数据", EditorStyles.boldLabel);
        temperature = EditorGUILayout.IntSlider("温度", temperature, 0, 100);
        humidity = EditorGUILayout.IntSlider("湿度", humidity, 0, 100);
        elevation = EditorGUILayout.IntSlider("海拔", elevation, 0, 2);
        maxPlantBiomass = Mathf.Max(0f, EditorGUILayout.FloatField("植物生物量上限", maxPlantBiomass));
        plantBiomass = Mathf.Clamp(EditorGUILayout.FloatField("植物生物量", plantBiomass), 0f, maxPlantBiomass);
        habitatRecovery = Mathf.Max(0, EditorGUILayout.IntField("每日恢复量", habitatRecovery));
        if (block != null && GUILayout.Button("应用环境修改到模拟"))
        {
            bool changed = block.temperature != temperature || block.humidity != humidity ||
                block.habitatRecovery != habitatRecovery;
            bool otherChanged = block.elevation != elevation ||
                block.maxPlantBiomass != maxPlantBiomass || block.plantBiomass != plantBiomass;
            if (changed)
                dailyOutput.Add("Day " + currentDay + "    环境改变：温度 " + block.temperature
                    + "→" + temperature + "，湿度 " + block.humidity + "→" + humidity
                    + "，每日恢复 " + block.habitatRecovery + "→" + habitatRecovery);
            if (changed)
            {
                controller.ApplyEnvironment(block, temperature, humidity, habitatRecovery);
            }
            block.elevation = elevation;
            block.maxPlantBiomass = maxPlantBiomass;
            block.plantBiomass = Mathf.Clamp(plantBiomass, 0f, maxPlantBiomass);
            if (otherChanged) controller.NotifyEnvironmentChanged();
            Repaint();
        }
        EditorGUILayout.HelpBox("环境数值可在运行中应用；温度、湿度和恢复量改变会在下一模拟日触发变异评估。", MessageType.Info);
    }

    private void DrawParameters()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("固定关键变量", EditorStyles.boldLabel);
        reproductionScale = Mathf.Max(0f, EditorGUILayout.FloatField("繁殖系数", reproductionScale));
        maxOverCapacityDeathRate = EditorGUILayout.Slider("超载衰退上限", maxOverCapacityDeathRate, 0f, 1f);
        maxStarvationDeathRate = EditorGUILayout.Slider("最大饥饿死亡率", maxStarvationDeathRate, 0f, 1f);
        mutationEnabled = EditorGUILayout.Toggle("启用变异", mutationEnabled);
        mutationInterval = Mathf.Max(1, EditorGUILayout.IntField("变异间隔（天）", mutationInterval));
        mutationStep = Mathf.Max(0f, EditorGUILayout.FloatField("变异步长", mutationStep));
        selectionStrength = Mathf.Max(0f, EditorGUILayout.FloatField("选择强度", selectionStrength));
        migrationInterval = Mathf.Max(1, EditorGUILayout.IntField("迁徙间隔（天）", migrationInterval));
        migrationPopulationScale = Mathf.Max(0.01f,
            EditorGUILayout.FloatField("基础概率规模系数", migrationPopulationScale));
        EditorGUILayout.LabelField("高度差跨越概率：0 → 1，1 → 0.5，2 → 0",
            EditorStyles.miniLabel);
        overloadThreshold = EditorGUILayout.Slider("超载阈值", overloadThreshold, 0f, 0.99f);
        baseOverloadProbability = EditorGUILayout.Slider("基础超载概率",
            baseOverloadProbability, 0f, 1f);
        overloadMigrationFraction = EditorGUILayout.Slider("超载迁出比例",
            overloadMigrationFraction, 0f, 1f);
        migrationCooldownDays = Mathf.Max(0,
            EditorGUILayout.IntField("迁徙冷却（天）", migrationCooldownDays));
        secondsPerDay = Mathf.Max(0.05f, EditorGUILayout.FloatField("每模拟日秒数", secondsPerDay));

        EditorGUILayout.LabelField("基础概率 = 数量 ÷（数量 + 规模系数）", EditorStyles.miniLabel);
        if (controller != null && GUILayout.Button("应用固定数值修改到模拟"))
            ApplyFixedParameters();
        EditorGUILayout.LabelField("每周期只变异一个性状；推荐间隔 7 天、步长 2。", EditorStyles.miniLabel);
        EditorGUILayout.HelpBox("达到 K 后数量可以保持水平，但每天仍有出生和死亡；当天的两个整数显示在当前状态和每日输出中。", MessageType.Info);
        EditorGUILayout.HelpBox("植物库存低于上限 40% 时按最大恢复量生长，之后逐渐减速；食草者 K 仍由恢复上限和个体能耗决定。体型 <8 与 ≥8 使用不同觅食赛道；1 级每日捕获上限 8%，2 级为 10%。", MessageType.Info);
        if (populations.Count == 1 && populations[0].data != null &&
            (populations[0].data.trophicLevelInitialized
                ? populations[0].data.trophicLevel == 0
                : populations[0].data.species == null || populations[0].data.species.trophicLevel == 0))
        {
            PopulationData input = populations[0].data;
            float energyNeed = SimulationController.CalculateEnergyNeed(
                input.size, input.movementAbility);
            float fitness = Mathf.Clamp01(1f - (Mathf.Abs(temperature - input.fitTemperature)
                + Mathf.Abs(humidity - input.fitHumidity)) / 90f);
            float projectedStock = plantBiomass + SimulationController.CalculatePlantGrowth(
                plantBiomass, maxPlantBiomass, habitatRecovery);
            float supportFood = Mathf.Min(projectedStock,
                Mathf.Min(habitatRecovery, maxPlantBiomass));
            float availableFood = projectedStock
                * (0.75f + 0.0025f * Mathf.Clamp(input.movementAbility, 0, 100))
                * fitness;
            float capacity = Mathf.Min(supportFood * fitness, availableFood)
                / energyNeed;
            EditorGUILayout.LabelField("当前输入预估：个体能耗 " + energyNeed.ToString("F1")
                + "    K " + capacity.ToString("F2") + "    K/2 " + (capacity / 2f).ToString("F2"));
        }
    }

    private void DrawPopulations()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("群落配置（待应用）", EditorStyles.boldLabel);
        int removeIndex = -1;

        for (int i = 0; i < populations.Count; i++)
        {
            PopulationInput input = populations[i];
            if (input.data == null)
            {
                input.data = new PopulationData();
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            input.expanded = EditorGUILayout.Foldout(input.expanded, input.name, true);
            if (GUILayout.Button("删除", GUILayout.Width(50f)))
            {
                removeIndex = i;
            }
            EditorGUILayout.EndHorizontal();

            if (input.expanded)
            {
                input.name = EditorGUILayout.TextField("名称（无物种资产时作同族标识）", input.name);
                DrawInitialTrophicLevel(input.data);
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

        if (removeIndex >= 0)
        {
            populations.RemoveAt(removeIndex);
        }

        if (GUILayout.Button("+ 添加种群"))
        {
            populations.Add(CreatePopulation("种群 " + (populations.Count + 1)));
        }
        if (block != null && GUILayout.Button("应用群落修改到模拟"))
            ApplyLeftCommunity();
    }

    private void DrawCurrentState()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("当前状态", EditorStyles.boldLabel);
        if (block == null)
        {
            EditorGUILayout.LabelField("点击 Start 或 Next Day 开始模拟。");
            return;
        }

        EditorGUILayout.LabelField("Day " + currentDay + "    植物库存 " + block.plantBiomass.ToString("F2"));
        EditorGUILayout.LabelField("当前温度 " + block.temperature + "    当前湿度 " + block.humidity);
        EditorGUILayout.LabelField("恢复上限 " + block.habitatRecovery + "    当日生长 "
            + block.plantGrowthToday.ToString("F2") + "    当日摄食 " + block.consumedBiomassToday.ToString("F2"));
        for (int i = 0; i < history.Count; i++)
        {
            PopulationData population = block.community[i];
            if (currentDay == 0)
            {
                EditorGUILayout.LabelField(history[i].name + "    数量 " + population.speciesAmount);
            }
            else
            {
                EditorGUILayout.LabelField(history[i].name + "    数量 " + population.speciesAmount
                    + "    K " + population.carryingCapacity.ToString("F2")
                    + "    K/2 " + (population.carryingCapacity / 2f).ToString("F2"));
            }
            EditorGUILayout.LabelField("当日出生 " + population.birthsToday + "    当日死亡 " + population.deathsToday);
            EditorGUILayout.LabelField("适温 " + (population.fitTemperature + population.temperatureMutationRemainder).ToString("F2")
                + "    适湿 " + (population.fitHumidity + population.humidityMutationRemainder).ToString("F2")
                + "    运动 " + (population.movementAbility + population.movementMutationRemainder).ToString("F2")
                + "    体型 " + (population.size + population.sizeMutationRemainder).ToString("F2"));
            EditorGUILayout.LabelField("生育 " + (population.fertility + population.fertilityMutationRemainder).ToString("F2"));
            DrawLiveTrophicLevel(population, history[i].name, dailyOutput);
        }
    }

    private static void DrawInitialTrophicLevel(PopulationData data)
    {
        SpeciesData selected = (SpeciesData)EditorGUILayout.ObjectField(
            "物种", data.species, typeof(SpeciesData), false);
        if (selected != data.species || !data.trophicLevelInitialized)
        {
            data.species = selected;
            data.trophicLevel = selected == null ? 0 :
                Mathf.Clamp(selected.trophicLevel, 0, SimulationController.MaxTrophicLevel);
            data.trophicLevelInitialized = true;
        }
        data.trophicLevel = EditorGUILayout.Popup("初始营养级",
            Mathf.Clamp(data.trophicLevel, 0, SimulationController.MaxTrophicLevel), trophicLevelNames);
    }

    private void DrawLiveTrophicLevel(PopulationData population, string name,
        List<string> output)
    {
        int selected = EditorGUILayout.Popup("当前营养级（下日起生效）",
            Mathf.Clamp(population.trophicLevel, 0, SimulationController.MaxTrophicLevel), trophicLevelNames);
        if (SimulationController.SetTrophicLevel(population, selected))
            output.Add("Day " + currentDay + "    " + name + "    营养级改为 " + selected);
    }

    private void DrawChart()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("种群数量 / 植物库存变化", EditorStyles.boldLabel);
        Rect area = GUILayoutUtility.GetRect(100f, 250f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(area, new Color(0.13f, 0.14f, 0.17f));
        Rect plot = new Rect(area.x + 48f, area.y + 12f, area.width - 108f, area.height - 42f);

        float populationMaximum = 1f;
        foreach (PopulationHistory series in history)
        {
            foreach (float amount in series.amounts)
            {
                populationMaximum = Mathf.Max(populationMaximum, amount);
            }
        }
        populationMaximum *= 1.1f;

        float plantMinimum = float.MaxValue;
        float plantMaximum = 0f;
        foreach (float amount in plantHistory)
        {
            plantMinimum = Mathf.Min(plantMinimum, amount);
            plantMaximum = Mathf.Max(plantMaximum, amount);
        }
        if (plantHistory.Count == 0 || plantMaximum - plantMinimum < 1f)
        {
            plantMinimum = 0f;
            plantMaximum = Mathf.Max(1f, plantMaximum * 1.1f);
        }
        else
        {
            float padding = (plantMaximum - plantMinimum) * 0.1f;
            plantMinimum = Mathf.Max(0f, plantMinimum - padding);
            plantMaximum += padding;
        }

        // 左轴显示种群，右轴显示植物库存，两条曲线共用日期
        GUIStyle chartLabel = new GUIStyle(EditorStyles.miniLabel);
        chartLabel.normal.textColor = Color.white;
        Handles.BeginGUI();
        Handles.color = new Color(0.30f, 0.32f, 0.36f);
        for (int i = 0; i <= 4; i++)
        {
            float y = plot.yMax - plot.height * i / 4f;
            Handles.DrawLine(new Vector3(plot.x, y), new Vector3(plot.xMax, y));
            GUI.Label(new Rect(area.x + 2f, y - 9f, 44f, 18f),
                (populationMaximum * i / 4f).ToString("F0"), chartLabel);
            GUI.Label(new Rect(plot.xMax + 5f, y - 9f, 55f, 18f),
                (plantMinimum + (plantMaximum - plantMinimum) * i / 4f).ToString("F0"), chartLabel);
        }

        for (int i = 0; i < history.Count; i++)
        {
            PopulationHistory series = history[i];
            Handles.color = series.color;
            Vector3[] points = new Vector3[series.amounts.Count];
            for (int j = 0; j < points.Length; j++)
            {
                float x = plot.x + plot.width * j / Mathf.Max(1, points.Length - 1);
                float y = plot.yMax - plot.height * series.amounts[j] / populationMaximum;
                points[j] = new Vector3(x, y);
            }
            if (points.Length > 1)
            {
                Handles.DrawAAPolyLine(2f, points);
            }
        }

        if (plantHistory.Count > 1)
        {
            Handles.color = new Color(0.45f, 0.90f, 0.35f);
            Vector3[] points = new Vector3[plantHistory.Count];
            for (int i = 0; i < points.Length; i++)
            {
                float x = plot.x + plot.width * i / Mathf.Max(1, points.Length - 1);
                float y = plot.yMax - plot.height * (plantHistory[i] - plantMinimum)
                    / (plantMaximum - plantMinimum);
                points[i] = new Vector3(x, y);
            }
            Handles.DrawAAPolyLine(2f, points);
        }
        Handles.EndGUI();

        GUI.Label(new Rect(plot.x, plot.yMax + 3f, 60f, 20f), "Day 0", chartLabel);
        GUI.Label(new Rect(plot.xMax - 65f, plot.yMax + 3f, 65f, 20f), "Day " + currentDay, chartLabel);

        for (int i = 0; i < history.Count; i++)
        {
            if (i % 4 == 0)
            {
                EditorGUILayout.BeginHorizontal();
            }

            PopulationHistory series = history[i];
            GUIStyle style = new GUIStyle(EditorStyles.label);
            style.normal.textColor = series.color;
            GUILayout.Label("● " + series.name, style, GUILayout.Width(110f));

            if (i % 4 == 3 || i == history.Count - 1)
            {
                EditorGUILayout.EndHorizontal();
            }
        }
        GUIStyle plantStyle = new GUIStyle(EditorStyles.label);
        plantStyle.normal.textColor = new Color(0.45f, 0.90f, 0.35f);
        GUILayout.Label("● 植物库存（右轴）", plantStyle);
    }

    private void DrawDailyOutput()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("每日输出", EditorStyles.boldLabel);
        outputScroll = EditorGUILayout.BeginScrollView(outputScroll, GUILayout.Height(170f));
        for (int i = dailyOutput.Count - 1; i >= 0; i--)
        {
            EditorGUILayout.LabelField(dailyOutput[i]);
        }
        EditorGUILayout.EndScrollView();
    }

    private void UpdateSimulation()
    {
        // 编辑器时间驱动模拟，不要求进入游戏播放模式
        double now = EditorApplication.timeSinceStartup;
        if (running && block != null)
        {
            elapsedTime += (now - lastUpdate) * speeds[speedIndex];
            float dayLength = Mathf.Max(0.05f, appliedSecondsPerDay);
            int steps = 0;
            while (elapsedTime >= dayLength && steps < 100)
            {
                elapsedTime -= dayLength;
                SimulateOneDay();
                steps++;
            }
            Repaint();
        }
        lastUpdate = now;
    }

    private void RestartSimulation()
    {
        if (migrationEnabled)
        {
            RestartDualSimulation();
            return;
        }
        running = false;
        elapsedTime = 0;
        currentDay = 0;
        lastUpdate = EditorApplication.timeSinceStartup;
        history.Clear();
        plantHistory.Clear();
        dailyOutput.Clear();
        DestroyRuntime();

        // 临时对象只用于数值计算，不写入场景
        runtimeObject = new GameObject("Population Simulator Runtime");
        runtimeObject.hideFlags = HideFlags.HideAndDontSave;
        block = runtimeObject.AddComponent<BlockInfo>();
        block.temperature = temperature;
        block.humidity = humidity;
        block.elevation = elevation;
        block.plantBiomass = plantBiomass;
        block.maxPlantBiomass = maxPlantBiomass;
        block.habitatRecovery = habitatRecovery;
        block.community = new List<PopulationData>();
        controller = runtimeObject.AddComponent<SimulationController>();
        controller.SetParameters(reproductionScale,
            maxOverCapacityDeathRate, maxStarvationDeathRate);
        controller.SetMutationParameters(mutationInterval, mutationStep, selectionStrength);
        controller.SetMutationEnabled(mutationEnabled);
        controller.SetMigrationParameters(false, migrationInterval,
            migrationPopulationScale, overloadThreshold,
            baseOverloadProbability, overloadMigrationFraction, migrationCooldownDays);
        controller.SetBlocks(new List<BlockInfo> { block });
        CacheAppliedParameters();

        for (int i = 0; i < populations.Count; i++)
        {
            PopulationInput input = populations[i];
            PopulationData added = CopyPopulationInput(input);
            input.runtimePopulation = added;
            block.community.Add(added);
            history.Add(new PopulationHistory
            {
                name = string.IsNullOrWhiteSpace(input.name) ? "种群 " + (i + 1) : input.name,
                color = Color.HSVToRGB((i * 0.618034f + 0.53f) % 1f, 0.7f, 0.95f)
            });
        }
        RecordDay();
        Repaint();
    }

    private void SimulateOneDay()
    {
        if (migrationEnabled)
        {
            SimulateDualOneDay();
            return;
        }
        if (block == null)
        {
            return;
        }

        float[][] previousTraits = null;
        if (appliedMutationEnabled && appliedMutationStep > 0f)
        {
            for (int i = 0; i < block.community.Count; i++)
            {
                PopulationData population = block.community[i];
                if (population.mutationDaysElapsed + 1 >= appliedMutationInterval)
                {
                    if (previousTraits == null)
                        previousTraits = new float[block.community.Count][];
                    previousTraits[i] = TraitValues(population);
                }
            }
        }

        controller.SimulateBlock(block);
        currentDay++;
        RecordDay();
        if (previousTraits != null)
        {
            for (int i = 0; i < previousTraits.Length; i++)
            {
                if (previousTraits[i] == null) continue;
                float[] after = TraitValues(block.community[i]);
                for (int trait = 0; trait < after.Length; trait++)
                {
                    if (Mathf.Abs(after[trait] - previousTraits[i][trait]) < 0.001f) continue;
                    dailyOutput.Add("Day " + currentDay + "    " + history[i].name + " 进化："
                        + traitNames[trait] + " " + previousTraits[i][trait].ToString("F2")
                        + "→" + after[trait].ToString("F2"));
                }
            }
        }
        Repaint();
    }

    private static float[] TraitValues(PopulationData population)
    {
        return new[]
        {
            population.fitTemperature + population.temperatureMutationRemainder,
            population.fitHumidity + population.humidityMutationRemainder,
            population.movementAbility + population.movementMutationRemainder,
            population.size + population.sizeMutationRemainder,
            population.fertility + population.fertilityMutationRemainder,
            population.trophicLevel + population.trophicMutationRemainder
        };
    }

    private void RecordDay()
    {
        plantHistory.Add(block.plantBiomass);
        if (currentDay > 0)
        {
            dailyOutput.Add("Day " + currentDay + "    植物库存 " + block.plantBiomass.ToString("F2")
                + "    温度 " + block.temperature + "    湿度 " + block.humidity
                + "    生长 " + block.plantGrowthToday.ToString("F2")
                + "    摄食 " + block.consumedBiomassToday.ToString("F2"));
        }
        for (int i = 0; i < history.Count; i++)
        {
            float amount = block.community[i].speciesAmount;
            history[i].amounts.Add(amount);
            if (currentDay > 0)
            {
                PopulationData population = block.community[i];
                dailyOutput.Add("Day " + currentDay + "    " + history[i].name + "    " + population.speciesAmount
                    + "    出生 " + population.birthsToday + "    死亡 " + population.deathsToday);
            }
        }
    }

    private PopulationInput CreatePopulation(string name)
    {
        return new PopulationInput
        {
            name = name,
            data = new PopulationData
            {
                speciesAmount = 10,
                fitTemperature = 50,
                fitHumidity = 50,
                size = 10,
                movementAbility = 10,
                fertility = 50,
                habitatNiche = 50
            }
        };
    }

    private PopulationData CopyPopulationInput(PopulationInput input)
    {
        PopulationData copy = CopyPopulation(input.data);
        if (!string.IsNullOrWhiteSpace(input.name))
            copy.lineageName = input.name.Trim();
        return copy;
    }

    private PopulationData CopyPopulation(PopulationData data)
    {
        if (data == null)
        {
            data = new PopulationData();
        }

        return new PopulationData
        {
            species = data.species,
            lineageName = data.lineageName,
            speciesId = data.speciesId,
            ecologicalNiche = data.ecologicalNiche,
            speciesAmount = Mathf.Max(0, data.speciesAmount),
            movementAbility = data.movementAbility,
            habitatNiche = data.habitatNiche,
            fitTemperature = data.fitTemperature,
            fitHumidity = data.fitHumidity,
            size = data.size,
            fertility = data.fertility,
            trophicLevel = data.trophicLevelInitialized
                ? Mathf.Clamp(data.trophicLevel, 0, 2)
                : data.species == null ? 0 : Mathf.Clamp(data.species.trophicLevel, 0, 2),
            trophicLevelInitialized = true
        };
    }

    private void DestroyRuntime()
    {
        if (runtimeObject != null)
        {
            DestroyImmediate(runtimeObject);
        }
        runtimeObject = null;
        block = null;
        rightBlock = null;
        controller = null;
    }
}
