using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PopulationSimulatorWindow
{
    private class SideHistory
    {
        public PopulationData population;
        public string name;
        public Color color;
        public readonly List<float> amounts = new List<float>();
    }

    private struct TraitSnapshot
    {
        public PopulationData population;
        public bool leftSide;
        public string name;
        public float[] values;
    }

    [SerializeField] private bool migrationEnabled;
    [SerializeField] private int migrationInterval = 7;
    [SerializeField] private float migrationPopulationScale = 100f;
    [SerializeField] private float overloadThreshold = 0.8f;
    [SerializeField] private float baseOverloadProbability = 0.5f;
    [SerializeField] private float overloadMigrationFraction = 0.2f;
    [SerializeField] private int migrationCooldownDays = 7;
    [SerializeField] private int elevation;
    [SerializeField] private int rightTemperature = 50;
    [SerializeField] private int rightHumidity = 50;
    [SerializeField] private int rightElevation = 1;
    [SerializeField] private float rightPlantBiomass = 1000f;
    [SerializeField] private float rightMaxPlantBiomass = 10000f;
    [SerializeField] private int rightHabitatRecovery = 100;
    [SerializeField] private List<PopulationInput> rightPopulations = new List<PopulationInput>();

    private BlockInfo rightBlock;
    private readonly List<SideHistory> leftSideHistory = new List<SideHistory>();
    private readonly List<SideHistory> rightSideHistory = new List<SideHistory>();
    private readonly List<float> leftSidePlants = new List<float>();
    private readonly List<float> rightSidePlants = new List<float>();
    private readonly List<string> leftSideOutput = new List<string>();
    private readonly List<string> rightSideOutput = new List<string>();
    private Vector2 leftOutputScroll;
    private Vector2 rightOutputScroll;
    private bool appliedMutationEnabled;
    private int appliedMutationInterval;
    private float appliedMutationStep;
    private int appliedMigrationInterval;
    private float appliedMigrationPopulationScale;
    private float appliedOverloadThreshold;
    private float appliedBaseOverloadProbability;
    private float appliedOverloadMigrationFraction;
    private int appliedMigrationCooldownDays;
    private float appliedSecondsPerDay;

    private void CacheAppliedParameters()
    {
        appliedMutationEnabled = mutationEnabled;
        appliedMutationInterval = mutationInterval;
        appliedMutationStep = mutationStep;
        appliedMigrationInterval = migrationInterval;
        appliedMigrationPopulationScale = migrationPopulationScale;
        appliedOverloadThreshold = overloadThreshold;
        appliedBaseOverloadProbability = baseOverloadProbability;
        appliedOverloadMigrationFraction = overloadMigrationFraction;
        appliedMigrationCooldownDays = migrationCooldownDays;
        appliedSecondsPerDay = secondsPerDay;
    }

    private void ApplyFixedParameters(bool log = true)
    {
        controller.SetParameters(reproductionScale,
            maxOverCapacityDeathRate, maxStarvationDeathRate);
        controller.SetPredationFractions(levelOnePreyFraction, levelTwoPreyFraction);
        controller.SetMutationParameters(mutationInterval, mutationStep, selectionStrength);
        controller.SetMutationEnabled(mutationEnabled);
        controller.SetEcologicalNichesEnabled(ecologicalNichesEnabled);
        controller.SetMigrationParameters(migrationEnabled, migrationInterval,
            migrationPopulationScale, overloadThreshold,
            baseOverloadProbability, overloadMigrationFraction, migrationCooldownDays);
        CacheAppliedParameters();
        if (log)
        {
            dailyOutput.Add("Day " + currentDay + "    固定数值已应用");
            if (migrationEnabled)
            {
                leftSideOutput.Add("Day " + currentDay + "    共同固定数值已应用");
                rightSideOutput.Add("Day " + currentDay + "    共同固定数值已应用");
            }
        }
    }

    private void RestartDualSimulation()
    {
        running = false;
        elapsedTime = 0;
        currentDay = 0;
        lastUpdate = EditorApplication.timeSinceStartup;
        DestroyRuntime();
        leftSideHistory.Clear();
        rightSideHistory.Clear();
        leftSidePlants.Clear();
        rightSidePlants.Clear();
        leftSideOutput.Clear();
        rightSideOutput.Clear();
        runtimeObject = new GameObject("Population Simulator Runtime");
        runtimeObject.hideFlags = HideFlags.HideAndDontSave;
        block = runtimeObject.AddComponent<BlockInfo>();
        rightBlock = runtimeObject.AddComponent<BlockInfo>();
        ApplyBlockEnvironment(block, temperature, humidity, elevation,
            plantBiomass, maxPlantBiomass, habitatRecovery);
        ApplyBlockEnvironment(rightBlock, rightTemperature, rightHumidity, rightElevation,
            rightPlantBiomass, rightMaxPlantBiomass, rightHabitatRecovery);
        block.community = CopyCommunity(populations);
        rightBlock.community = CopyCommunity(rightPopulations);
        block.SetNeighbors(new List<BlockInfo> { rightBlock });
        rightBlock.SetNeighbors(new List<BlockInfo> { block });
        controller = runtimeObject.AddComponent<SimulationController>();
        controller.SetBlocks(new List<BlockInfo> { block, rightBlock });
        controller.OnPopulationMigrated += ReportMigration;
        ApplyFixedParameters(false);
        RecordDualDay();
        Repaint();
    }

    private static void ApplyBlockEnvironment(BlockInfo target, int temp, int humidityValue,
        int height, float stock, float maximum, int recovery)
    {
        target.temperature = temp;
        target.humidity = humidityValue;
        target.elevation = height;
        target.maxPlantBiomass = maximum;
        target.plantBiomass = Mathf.Clamp(stock, 0f, maximum);
        target.habitatRecovery = recovery;
    }

    private List<PopulationData> CopyCommunity(List<PopulationInput> inputs)
    {
        List<PopulationData> result = new List<PopulationData>();
        if (inputs == null) return result;
        foreach (PopulationInput input in inputs)
            if (input != null && input.data != null && input.data.speciesAmount > 0)
            {
                PopulationData added = CopyPopulationInput(input);
                input.runtimePopulation = added;
                result.Add(added);
            }
        return result;
    }

    private void DrawDualSimulator()
    {
        if (rightPopulations == null) rightPopulations = new List<PopulationInput>();
        DrawParameters();
        EditorGUILayout.HelpBox("同一物种资产，或未指定资产时同名，迁入后会按数量加权合并。超载迁徙只开拓无同族的地块；自动迁徙不会在环境未改变时迁回来源地。手动迁出跳过概率判定，每次至多 100 只。", MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        float width = Mathf.Max(470f, (position.width - 34f) / 2f);
        DrawDualColumn(true, width);
        DrawDualColumn(false, width);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawDualColumn(bool leftSide, float width)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(width));
        EditorGUILayout.LabelField(leftSide ? "左侧地块" : "右侧地块", EditorStyles.boldLabel);
        DrawDualEnvironment(leftSide);
        DrawDualCommunity(leftSide);
        DrawDualState(leftSide);
        DrawDualChart(leftSide);
        DrawDualOutput(leftSide);
        EditorGUILayout.EndVertical();
    }

    private void DrawDualEnvironment(bool leftSide)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("环境变量", EditorStyles.boldLabel);
        if (leftSide)
        {
            temperature = EditorGUILayout.IntSlider("温度", temperature, 0, 100);
            humidity = EditorGUILayout.IntSlider("湿度", humidity, 0, 100);
            elevation = EditorGUILayout.IntSlider("海拔", elevation, 0, 2);
            maxPlantBiomass = Mathf.Max(0f, EditorGUILayout.FloatField("植物生物量上限", maxPlantBiomass));
            plantBiomass = Mathf.Clamp(EditorGUILayout.FloatField("植物生物量", plantBiomass), 0f, maxPlantBiomass);
            habitatRecovery = Mathf.Max(0, EditorGUILayout.IntField("每日恢复量", habitatRecovery));
        }
        else
        {
            rightTemperature = EditorGUILayout.IntSlider("温度", rightTemperature, 0, 100);
            rightHumidity = EditorGUILayout.IntSlider("湿度", rightHumidity, 0, 100);
            rightElevation = EditorGUILayout.IntSlider("海拔", rightElevation, 0, 2);
            rightMaxPlantBiomass = Mathf.Max(0f, EditorGUILayout.FloatField("植物生物量上限", rightMaxPlantBiomass));
            rightPlantBiomass = Mathf.Clamp(EditorGUILayout.FloatField("植物生物量", rightPlantBiomass), 0f, rightMaxPlantBiomass);
            rightHabitatRecovery = Mathf.Max(0, EditorGUILayout.IntField("每日恢复量", rightHabitatRecovery));
        }
        if (GetSideBlock(leftSide) != null && GUILayout.Button("应用环境修改到模拟"))
        {
            BlockInfo target = GetSideBlock(leftSide);
            int temp = leftSide ? temperature : rightTemperature;
            int humidityValue = leftSide ? humidity : rightHumidity;
            int recovery = leftSide ? habitatRecovery : rightHabitatRecovery;
            bool changed = target.temperature != temp || target.humidity != humidityValue ||
                target.habitatRecovery != recovery;
            if (changed) controller.ApplyEnvironment(target, temp, humidityValue, recovery);
            ApplyBlockEnvironment(target, temp, humidityValue,
                leftSide ? elevation : rightElevation,
                leftSide ? plantBiomass : rightPlantBiomass,
                leftSide ? maxPlantBiomass : rightMaxPlantBiomass, recovery);
            SideOutput(leftSide).Add("Day " + currentDay + "    环境修改已应用");
            RefreshSideSnapshot(leftSide);
        }
    }

    private void DrawDualCommunity(bool leftSide)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("群落配置（待应用）", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("现有种群保留数量；初始数量只用于新加入的种群。", EditorStyles.miniLabel);
        List<PopulationInput> inputs = leftSide ? populations : rightPopulations;
        int removeIndex = -1;
        for (int i = 0; i < inputs.Count; i++)
        {
            PopulationInput input = inputs[i];
            if (input == null) continue;
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
                input.data.speciesAmount = Mathf.Max(0, EditorGUILayout.IntField("新种群初始数量", input.data.speciesAmount));
                input.data.fitTemperature = EditorGUILayout.IntSlider("适宜温度", input.data.fitTemperature, 0, 100);
                input.data.fitHumidity = EditorGUILayout.IntSlider("适宜湿度", input.data.fitHumidity, 0, 100);
                input.data.size = EditorGUILayout.IntSlider("体型", input.data.size, 1, 100);
                input.data.movementAbility = EditorGUILayout.IntSlider("运动能力", input.data.movementAbility, 0, 100);
                input.data.fertility = EditorGUILayout.IntSlider("繁殖能力", input.data.fertility, 0, 100);
            }
            EditorGUILayout.EndVertical();
        }
        if (removeIndex >= 0) inputs.RemoveAt(removeIndex);
        if (GUILayout.Button("+ 添加种群"))
            inputs.Add(CreatePopulation("种群 " + (inputs.Count + 1)));
        if (GetSideBlock(leftSide) != null)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("应用群落修改到模拟"))
            {
                ApplyCommunityChanges(GetSideBlock(leftSide), inputs);
                SideOutput(leftSide).Add("Day " + currentDay + "    群落配置已应用");
                RefreshSideSnapshot(leftSide);
            }
            if (GUILayout.Button("读取当前群落"))
            {
                inputs.Clear();
                foreach (PopulationData population in GetSideBlock(leftSide).community)
                    inputs.Add(new PopulationInput
                    {
                        name = SideName(leftSide, population), data = CopyPopulation(population),
                        runtimePopulation = population
                    });
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void ApplyLeftCommunity()
    {
        List<PopulationData> before = new List<PopulationData>(block.community);
        List<PopulationHistory> oldHistory = new List<PopulationHistory>(history);
        ApplyCommunityChanges(block, populations);
        history.Clear();
        for (int i = 0; i < block.community.Count; i++)
        {
            PopulationData population = block.community[i];
            int oldIndex = before.IndexOf(population);
            PopulationHistory series = oldIndex >= 0 && oldIndex < oldHistory.Count
                ? oldHistory[oldIndex] : new PopulationHistory
                {
                    color = Color.HSVToRGB((i * 0.618034f + 0.53f) % 1f, 0.7f, 0.95f)
                };
            series.name = population.lineageName;
            if (oldIndex < 0)
            {
                for (int day = 0; day < currentDay; day++) series.amounts.Add(0f);
                series.amounts.Add(population.speciesAmount);
            }
            history.Add(series);
        }
        dailyOutput.Add("Day " + currentDay + "    群落配置已应用");
        Repaint();
    }

    private void ApplyCommunityChanges(BlockInfo target, List<PopulationInput> inputs)
    {
        List<PopulationData> old = target.community;
        List<PopulationData> next = new List<PopulationData>();
        bool[] used = new bool[old.Count];
        if (inputs != null)
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                PopulationInput input = inputs[i];
                if (input == null || input.data == null) continue;
                PopulationData edited = CopyPopulationInput(input);
                int match = -1;
                if (input.runtimePopulation != null)
                    for (int j = 0; j < old.Count; j++)
                        if (!used[j] && old[j] == input.runtimePopulation)
                        { match = j; break; }
                if (match >= 0)
                {
                    used[match] = true;
                    PopulationData resident = old[match];
                    resident.species = edited.species;
                    resident.lineageName = edited.lineageName;
                    if (!string.IsNullOrWhiteSpace(edited.speciesId))
                        resident.speciesId = edited.speciesId;
                    resident.ecologicalNiche = edited.ecologicalNiche;
                    resident.fitTemperature = edited.fitTemperature;
                    resident.temperatureMutationRemainder = 0f;
                    resident.fitHumidity = edited.fitHumidity;
                    resident.humidityMutationRemainder = 0f;
                    resident.size = edited.size;
                    resident.sizeMutationRemainder = 0f;
                    resident.movementAbility = edited.movementAbility;
                    resident.movementMutationRemainder = 0f;
                    resident.fertility = edited.fertility;
                    resident.fertilityMutationRemainder = 0f;
                    SimulationController.SetTrophicLevel(resident, edited.trophicLevel);
                    input.runtimePopulation = resident;
                    next.Add(resident);
                }
                else if (edited.speciesAmount > 0)
                {
                    input.runtimePopulation = edited;
                    next.Add(edited);
                }
            }
        }
        target.community = next;
    }

    private void DrawDualState(bool leftSide)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("当前状态", EditorStyles.boldLabel);
        BlockInfo target = GetSideBlock(leftSide);
        BlockInfo opposite = GetSideBlock(!leftSide);
        if (target == null)
        {
            EditorGUILayout.LabelField("点击 Start 或 Next Day 开始模拟。");
            return;
        }
        EditorGUILayout.LabelField("Day " + currentDay + "    海拔 " + target.elevation
            + "    温度 " + target.temperature + "    湿度 " + target.humidity);
        EditorGUILayout.LabelField("植物 " + target.plantBiomass.ToString("F2")
            + "    生长 " + target.plantGrowthToday.ToString("F2")
            + "    摄食 " + target.consumedBiomassToday.ToString("F2"));
        PopulationData departing = null;
        foreach (PopulationData population in target.community)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(SideName(leftSide, population)
                + "    数量 " + population.speciesAmount);
            EditorGUI.BeginDisabledGroup(population.speciesAmount < 10 ||
                currentDay < population.nextMigrationDay);
            if (GUILayout.Button(leftSide ? "迁出 ≤100 →" : "← 迁出 ≤100",
                GUILayout.Width(105f))) departing = population;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("K " + population.carryingCapacity.ToString("F2")
                + "    出生 " + population.birthsToday + "    死亡 " + population.deathsToday);
            EditorGUILayout.LabelField("适温 " + population.fitTemperature
                + "    适湿 " + population.fitHumidity + "    运动 " + population.movementAbility);
            EditorGUILayout.LabelField("体型 " + population.size + "    生育 " + population.fertility);
            EditorGUILayout.LabelField(controller.GetExpectedEvolutionDirection(target, population));
            DrawLiveTrophicLevel(population, SideName(leftSide, population), SideOutput(leftSide));
            float here = SimulationController.CalculateFitness(target, population);
            float there = SimulationController.CalculateFitness(opposite, population);
            float difference = Mathf.Max(0f, there - here);
            int height = Mathf.Abs(target.elevation - opposite.elevation);
            float terrain = height == 0 ? 1f : height == 1 ? 0.5f : 0f;
            float scale = controller == null ? migrationPopulationScale : appliedMigrationPopulationScale;
            float basic = population.speciesAmount / (population.speciesAmount + scale);
            float capacity = controller.EstimateTargetCapacity(opposite, population);
            float correction = controller.TargetCapacityCorrection(opposite, population);
            float pressure = controller.CalculateOverloadPressure(population);
            bool returnBlocked = opposite == population.lastMigrationSource;
            bool sameSpeciesAlreadyThere = opposite.community.Exists(resident =>
                SimulationController.SameSpecies(resident, population));
            float normalChance = returnBlocked ? 0f : difference * terrain * basic * correction;
            float overloadChance = returnBlocked || sameSpeciesAlreadyThere ? 0f :
                pressure * appliedBaseOverloadProbability * terrain * correction;
            int normalAmount = returnBlocked ? 0 : SimulationController.LimitMigrationAmount(
                population.speciesAmount, difference);
            int overloadAmount = returnBlocked || sameSpeciesAlreadyThere ? 0 :
                SimulationController.LimitMigrationAmount(population.speciesAmount,
                    pressure * appliedOverloadMigrationFraction);
            EditorGUILayout.LabelField("适应度 本地 " + here.ToString("P0")
                + "    对侧 " + there.ToString("P0") + "    差值 " + difference.ToString("P0"));
            EditorGUILayout.LabelField("目标K " + capacity.ToString("F1")
                + "    容量修正 " + correction.ToString("P0")
                + "    基础 " + basic.ToString("P1") + "    地形 " + terrain.ToString("P0"));
            EditorGUILayout.LabelField("普通概率 " + normalChance.ToString("P1")
                + " / 数量 " + normalAmount + "    超载压力 " + pressure.ToString("P0"));
            EditorGUILayout.LabelField("超载概率 " + overloadChance.ToString("P1")
                + " / 数量 " + overloadAmount
                + (currentDay < population.nextMigrationDay
                    ? "    冷却至 Day " + population.nextMigrationDay : ""));
            EditorGUILayout.LabelField("超载阈值 " + appliedOverloadThreshold.ToString("P0")
                + "    冷却天数 " + appliedMigrationCooldownDays,
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }
        if (departing != null)
        {
            controller.MovePopulation(target, opposite, departing, currentDay);
            RefreshSideSnapshot(true);
            RefreshSideSnapshot(false);
            Repaint();
        }
    }

    private void SimulateDualOneDay()
    {
        if (block == null || rightBlock == null) return;
        List<TraitSnapshot> snapshots = new List<TraitSnapshot>();
        if (appliedMutationEnabled && appliedMutationStep > 0f)
        {
            CaptureTraits(true, snapshots);
            CaptureTraits(false, snapshots);
        }
        currentDay++;
        controller.SimulateDay(currentDay);
        RecordDualDay();
        foreach (TraitSnapshot snapshot in snapshots)
        {
            float[] after = TraitValues(snapshot.population);
            for (int i = 0; i < after.Length; i++)
                if (Mathf.Abs(after[i] - snapshot.values[i]) >= 0.001f)
                    SideOutput(snapshot.leftSide).Add("Day " + currentDay + "    "
                        + snapshot.name + " 进化：" + traitNames[i] + " "
                        + snapshot.values[i].ToString("F2") + "→" + after[i].ToString("F2"));
        }
        Repaint();
    }

    private void CaptureTraits(bool leftSide, List<TraitSnapshot> snapshots)
    {
        foreach (PopulationData population in GetSideBlock(leftSide).community)
            if (population.mutationDaysElapsed + 1 >= appliedMutationInterval)
                snapshots.Add(new TraitSnapshot
                {
                    population = population, leftSide = leftSide,
                    name = SideName(leftSide, population), values = TraitValues(population)
                });
    }

    private void ReportMigration(BlockInfo source, BlockInfo target, PopulationData population, int amount)
    {
        bool fromLeft = source == block;
        string name = SideName(fromLeft, population);
        SideOutput(fromLeft).Add("Day " + currentDay + "    " + name + " 迁出 " + amount);
        SideOutput(!fromLeft).Add("Day " + currentDay + "    " + name + " 迁入 " + amount);
    }

    private BlockInfo GetSideBlock(bool leftSide) => leftSide ? block : rightBlock;
    private List<SideHistory> SideSeries(bool leftSide) =>
        leftSide ? leftSideHistory : rightSideHistory;
    private List<float> SidePlants(bool leftSide) =>
        leftSide ? leftSidePlants : rightSidePlants;
    private List<string> SideOutput(bool leftSide) =>
        leftSide ? leftSideOutput : rightSideOutput;

    private static bool SameHistoryPopulation(PopulationData first, PopulationData second)
    {
        return first != null && second != null &&
            (first == second || first.ecologicalNiche == second.ecologicalNiche &&
                SimulationController.SameSpecies(first, second));
    }

    private string SideName(bool leftSide, PopulationData population)
    {
        SideHistory existing = SideSeries(leftSide).Find(item =>
            SameHistoryPopulation(item.population, population));
        return existing != null ? existing.name :
            !string.IsNullOrWhiteSpace(population.lineageName)
                ? population.lineageName : population.species != null
                    ? population.species.name : "种群";
    }

    private static void MergeDuplicateSeries(List<SideHistory> series)
    {
        for (int i = 0; i < series.Count; i++)
        {
            for (int j = series.Count - 1; j > i; j--)
            {
                if (!SameHistoryPopulation(series[i].population, series[j].population))
                    continue;
                List<float> retained = series[i].amounts;
                List<float> duplicate = series[j].amounts;
                while (retained.Count < duplicate.Count) retained.Add(0f);
                for (int day = 0; day < duplicate.Count; day++)
                    retained[day] += duplicate[day];
                series.RemoveAt(j);
            }
        }
    }

    private void EnsureSideSeries(bool leftSide)
    {
        BlockInfo target = GetSideBlock(leftSide);
        List<SideHistory> series = SideSeries(leftSide);
        MergeDuplicateSeries(series);
        for (int i = 0; i < target.community.Count; i++)
        {
            PopulationData population = target.community[i];
            SideHistory existing = series.Find(item =>
                SameHistoryPopulation(item.population, population));
            if (existing != null)
            {
                existing.population = population;
                if (!string.IsNullOrWhiteSpace(population.lineageName))
                    existing.name = population.lineageName;
                continue;
            }
            string name = !string.IsNullOrWhiteSpace(population.lineageName)
                ? population.lineageName : population.species != null
                    ? population.species.name : "种群 " + (i + 1);
            SideHistory item = new SideHistory
            {
                population = population, name = name,
                color = Color.HSVToRGB((series.Count * 0.618034f + 0.53f) % 1f, 0.7f, 0.95f)
            };
            for (int day = 0; day < currentDay; day++) item.amounts.Add(0f);
            series.Add(item);
        }
    }

    private static void CurrentSeriesStats(BlockInfo target, SideHistory item,
        out int amount, out int births, out int deaths)
    {
        amount = 0;
        births = 0;
        deaths = 0;
        foreach (PopulationData population in target.community)
        {
            if (!SameHistoryPopulation(item.population, population)) continue;
            amount += population.speciesAmount;
            births += population.birthsToday;
            deaths += population.deathsToday;
        }
    }

    private void RecordDualDay()
    {
        RecordSideDay(true);
        RecordSideDay(false);
    }

    private void RecordSideDay(bool leftSide)
    {
        BlockInfo target = GetSideBlock(leftSide);
        EnsureSideSeries(leftSide);
        SidePlants(leftSide).Add(target.plantBiomass);
        if (currentDay > 0)
            SideOutput(leftSide).Add("Day " + currentDay + "    植物 "
                + target.plantBiomass.ToString("F2") + "    生长 "
                + target.plantGrowthToday.ToString("F2") + "    摄食 "
                + target.consumedBiomassToday.ToString("F2"));
        foreach (SideHistory item in SideSeries(leftSide))
        {
            CurrentSeriesStats(target, item, out int amount, out int births, out int deaths);
            item.amounts.Add(amount);
            if (currentDay > 0 && amount > 0)
                SideOutput(leftSide).Add("Day " + currentDay + "    " + item.name
                    + "    数量 " + amount + "    出生 " + births
                    + "    死亡 " + deaths);
        }
    }

    private void RefreshSideSnapshot(bool leftSide)
    {
        BlockInfo target = GetSideBlock(leftSide);
        if (target == null) return;
        EnsureSideSeries(leftSide);
        List<float> plants = SidePlants(leftSide);
        while (plants.Count <= currentDay) plants.Add(0f);
        plants[currentDay] = target.plantBiomass;
        foreach (SideHistory item in SideSeries(leftSide))
        {
            while (item.amounts.Count <= currentDay) item.amounts.Add(0f);
            CurrentSeriesStats(target, item, out int amount, out _, out _);
            item.amounts[currentDay] = amount;
        }
    }

    private void DrawDualChart(bool leftSide)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("种群数量 / 植物库存变化", EditorStyles.boldLabel);
        Rect area = GUILayoutUtility.GetRect(100f, 220f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(area, new Color(0.13f, 0.14f, 0.17f));
        Rect plot = new Rect(area.x + 42f, area.y + 12f, area.width - 92f, area.height - 42f);
        float populationMaximum = 1f;
        foreach (SideHistory item in SideSeries(leftSide))
            foreach (float value in item.amounts)
                populationMaximum = Mathf.Max(populationMaximum, value);
        populationMaximum *= 1.1f;
        float plantMaximum = 1f;
        foreach (float value in SidePlants(leftSide))
            plantMaximum = Mathf.Max(plantMaximum, value);
        GUIStyle label = new GUIStyle(EditorStyles.miniLabel);
        label.normal.textColor = Color.white;
        Handles.BeginGUI();
        Handles.color = new Color(0.3f, 0.32f, 0.36f);
        for (int i = 0; i <= 4; i++)
        {
            float y = plot.yMax - plot.height * i / 4f;
            Handles.DrawLine(new Vector3(plot.x, y), new Vector3(plot.xMax, y));
            GUI.Label(new Rect(area.x + 2f, y - 9f, 38f, 18f),
                (populationMaximum * i / 4f).ToString("F0"), label);
            GUI.Label(new Rect(plot.xMax + 3f, y - 9f, 48f, 18f),
                (plantMaximum * i / 4f).ToString("F0"), label);
        }
        foreach (SideHistory item in SideSeries(leftSide))
        {
            Handles.color = item.color;
            Vector3[] points = new Vector3[item.amounts.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector3(plot.x + plot.width * i / Mathf.Max(1, points.Length - 1),
                    plot.yMax - plot.height * item.amounts[i] / populationMaximum);
            if (points.Length > 1) Handles.DrawAAPolyLine(2f, points);
        }
        List<float> plantValues = SidePlants(leftSide);
        if (plantValues.Count > 1)
        {
            Handles.color = new Color(0.45f, 0.9f, 0.35f);
            Vector3[] points = new Vector3[plantValues.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector3(plot.x + plot.width * i / Mathf.Max(1, points.Length - 1),
                    plot.yMax - plot.height * plantValues[i] / plantMaximum);
            Handles.DrawAAPolyLine(2f, points);
        }
        Handles.EndGUI();
        GUI.Label(new Rect(plot.x, plot.yMax + 2f, 60f, 20f), "Day 0", label);
        GUI.Label(new Rect(plot.xMax - 65f, plot.yMax + 2f, 65f, 20f),
            "Day " + currentDay, label);
        foreach (SideHistory item in SideSeries(leftSide))
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = item.color;
            GUILayout.Label("● " + item.name, style);
        }
        GUILayout.Label("● 植物库存（右轴）", EditorStyles.miniLabel);
    }

    private void DrawDualOutput(bool leftSide)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("每日输出", EditorStyles.boldLabel);
        Vector2 scroll = leftSide ? leftOutputScroll : rightOutputScroll;
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(170f));
        List<string> output = SideOutput(leftSide);
        for (int i = output.Count - 1; i >= 0; i--)
            GUILayout.Label(output[i], EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
        if (leftSide) leftOutputScroll = scroll;
        else rightOutputScroll = scroll;
    }
}
