using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class PopulationSimulatorWindow : EditorWindow
{
    [Serializable]
    private class PopulationInput
    {
        public string name;
        public PopulationData data = new PopulationData();
        public bool expanded = true;
    }

    private class PopulationHistory
    {
        public string name;
        public Color color;
        public List<float> amounts = new List<float>();
    }

    [SerializeField] private int temperature = 50;
    [SerializeField] private int humidity = 50;
    [SerializeField] private int elevation;
    [SerializeField] private WaterCoverage waterCoverage;
    [SerializeField] private float plantBiomass = 1000f;
    [SerializeField] private float maxPlantBiomass = 10000f;
    [SerializeField] private int habitatRecovery = 100;

    [SerializeField] private float reproductionScale = 0.05f;
    [SerializeField] private float maxOverCapacityDeathRate = 0.1f;
    [SerializeField] private float maxStarvationDeathRate = 0.6f;
    [SerializeField] private bool mutationEnabled = true;
    [SerializeField] private int mutationInterval = 30;
    [SerializeField] private float mutationStep = 1f;
    [SerializeField] private float selectionStrength = 1f;
    [SerializeField] private float secondsPerDay = 1f;
    [SerializeField] private List<PopulationInput> populations = new List<PopulationInput>();
    [SerializeField] private bool initialized;

    private static readonly int[] speeds = { 1, 2, 4, 8 };
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

    [MenuItem("Darwin's Farm/单地块种群模拟器")]
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

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
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
        maxPlantBiomass = Mathf.Max(0f, EditorGUILayout.FloatField("植物生物量上限", maxPlantBiomass));
        plantBiomass = Mathf.Clamp(EditorGUILayout.FloatField("植物生物量", plantBiomass), 0f, maxPlantBiomass);
        habitatRecovery = Mathf.Max(0, EditorGUILayout.IntField("每日恢复量", habitatRecovery));
        elevation = EditorGUILayout.IntSlider("海拔", elevation, 0, 2);
        waterCoverage = (WaterCoverage)EditorGUILayout.EnumPopup("水域", waterCoverage);
        if (block != null && GUILayout.Button("应用环境变化到当前模拟"))
        {
            block.temperature = temperature;
            block.humidity = humidity;
            block.habitatRecovery = habitatRecovery;
            Repaint();
        }
        EditorGUILayout.HelpBox("温度、湿度和每日恢复量可通过上方按钮在运行中生效；植物初始库存等其他数据需要 Restart。海拔和水域目前不参与种群计算。", MessageType.Info);
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
        secondsPerDay = Mathf.Max(0.05f, EditorGUILayout.FloatField("每模拟日秒数", secondsPerDay));

        // 固定参数在下一次模拟日生效
        if (controller != null)
        {
            controller.SetParameters(reproductionScale,
                maxOverCapacityDeathRate, maxStarvationDeathRate);
            controller.SetMutationParameters(mutationInterval, mutationStep, selectionStrength);
            controller.SetMutationEnabled(mutationEnabled);
        }
        EditorGUILayout.LabelField("固定参数从下一模拟日起生效。", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("无明显选择压力时性状不变；体型最多偏离初始值 30%。", EditorStyles.miniLabel);
        EditorGUILayout.HelpBox("达到 K 后数量可以保持水平，但每天仍有出生和死亡；当天的两个整数显示在当前状态和每日输出中。", MessageType.Info);
        EditorGUILayout.HelpBox("单一种群时 K≈每日恢复量÷个体能耗。初始数量低于 K/2，才能看到先加速后减速的增长段；整数出生会使曲线呈阶梯状。", MessageType.Info);
        if (populations.Count == 1 && populations[0].data != null &&
            (populations[0].data.species == null || populations[0].data.species.trophicLevel == 0))
        {
            PopulationData input = populations[0].data;
            float energyNeed = Mathf.Max(1f, input.size
                + input.movementAbility * 0.02f);
            float fitness = Mathf.Clamp01(1f - (Mathf.Abs(temperature - input.fitTemperature)
                + Mathf.Abs(humidity - input.fitHumidity)) / 90f);
            float capacity = Mathf.Min(habitatRecovery, maxPlantBiomass)
                * fitness / energyNeed;
            EditorGUILayout.LabelField("当前输入预估：个体能耗 " + energyNeed.ToString("F1")
                + "    K " + capacity.ToString("F2") + "    K/2 " + (capacity / 2f).ToString("F2"));
        }
    }

    private void DrawPopulations()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("种群数据（初始值）", EditorStyles.boldLabel);
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
                input.name = EditorGUILayout.TextField("名称", input.name);
                input.data.species = (SpeciesData)EditorGUILayout.ObjectField(
                    "物种（营养级）", input.data.species, typeof(SpeciesData), false);
                EditorGUILayout.LabelField("营养级 " +
                    (input.data.species == null ? 0 : input.data.species.trophicLevel));
                input.data.speciesAmount = Mathf.Max(0,
                    EditorGUILayout.IntField("初始数量", input.data.speciesAmount));
                input.data.fitTemperature = EditorGUILayout.IntSlider("适宜温度", input.data.fitTemperature, 0, 100);
                input.data.fitHumidity = EditorGUILayout.IntSlider("适宜湿度", input.data.fitHumidity, 0, 100);
                input.data.size = EditorGUILayout.IntSlider("体型", input.data.size, 1, 100);
                input.data.movementAbility = EditorGUILayout.IntSlider("运动能力", input.data.movementAbility, 0, 100);
                input.data.fertility = EditorGUILayout.IntSlider("繁殖能力", input.data.fertility, 0, 100);
                input.data.habitatNiche = EditorGUILayout.IntSlider("生境生态位", input.data.habitatNiche, 0, 100);
                EditorGUILayout.LabelField("生境生态位目前不参与计算。", EditorStyles.miniLabel);
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
            EditorGUILayout.LabelField("生育 " + (population.fertility + population.fertilityMutationRemainder).ToString("F2")
                + "    食性 " + (population.trophicLevel + population.trophicMutationRemainder).ToString("F2")
                + "    生态位 " + population.habitatNiche);
        }
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
            int steps = 0;
            while (elapsedTime >= secondsPerDay && steps < 100)
            {
                elapsedTime -= secondsPerDay;
                SimulateOneDay();
                steps++;
            }
            Repaint();
        }
        lastUpdate = now;
    }

    private void RestartSimulation()
    {
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
        block.waterCoverage = waterCoverage;
        block.plantBiomass = plantBiomass;
        block.maxPlantBiomass = maxPlantBiomass;
        block.habitatRecovery = habitatRecovery;
        block.community = new List<PopulationData>();
        controller = runtimeObject.AddComponent<SimulationController>();
        controller.SetParameters(reproductionScale,
            maxOverCapacityDeathRate, maxStarvationDeathRate);
        controller.SetMutationParameters(mutationInterval, mutationStep, selectionStrength);
        controller.SetMutationEnabled(mutationEnabled);

        for (int i = 0; i < populations.Count; i++)
        {
            PopulationInput input = populations[i];
            block.community.Add(CopyPopulation(input.data));
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
        if (block == null)
        {
            return;
        }

        controller.SetParameters(reproductionScale,
            maxOverCapacityDeathRate, maxStarvationDeathRate);
        controller.SetMutationParameters(mutationInterval, mutationStep, selectionStrength);
        controller.SetMutationEnabled(mutationEnabled);
        controller.SimulateBlock(block);
        currentDay++;
        RecordDay();
        Repaint();
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

    private PopulationData CopyPopulation(PopulationData data)
    {
        if (data == null)
        {
            data = new PopulationData();
        }

        return new PopulationData
        {
            species = data.species,
            speciesAmount = Mathf.Max(0, data.speciesAmount),
            movementAbility = data.movementAbility,
            habitatNiche = data.habitatNiche,
            fitTemperature = data.fitTemperature,
            fitHumidity = data.fitHumidity,
            size = data.size,
            fertility = data.fertility,
            trophicLevel = data.species == null ? 0 : Mathf.Clamp(data.species.trophicLevel, 0, 2),
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
        controller = null;
    }
}
