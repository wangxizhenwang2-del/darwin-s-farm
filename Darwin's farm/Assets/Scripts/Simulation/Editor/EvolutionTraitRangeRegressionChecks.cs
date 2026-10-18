#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Run in a batch Editor with -executeMethod EvolutionTraitRangeRegressionChecks.RunBatch.
public static class EvolutionTraitRangeRegressionChecks
{
    public static void RunBatch()
    {
        try
        {
            RunChecks();
            Debug.Log("EVOLUTION_TRAIT_RANGE_REGRESSION_OK");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    private static void RunChecks()
    {
        GameObject siteObject = new GameObject("EvolutionRangeSite");
        SpeciesData target = ScriptableObject.CreateInstance<SpeciesData>();
        try
        {
            typeof(SpeciesData).GetField("speciesName", BindingFlags.Instance |
                BindingFlags.NonPublic).SetValue(target, "目标物种");
            target.baseFitTemperature = target.baseFitHumidity = 50;
            target.baseMovementAbility = 40;
            target.baseSize = 20;
            target.baseFertility = 60;
            BlockInfo block = siteObject.AddComponent<BlockInfo>();
            var simulation = siteObject.AddComponent<SimulationController>();
            simulation.SetBlocks(new List<BlockInfo> { block });
            var source = new PopulationData
            {
                fitTemperature = 50, fitHumidity = 60,
                movementAbility = 40, size = 20, fertility = 60,
                trackedEvolutionTarget = target,
                targetProtectionSince = new[] { 1, 1, 1 },
                targetProtectionReleased = new bool[3]
            };
            block.community = new List<PopulationData> { source };
            var details = new List<string>();
            simulation.OnEcologyEvent += (day, site, population, kind, detail) =>
            {
                if (kind == "evolution_trait_range") details.Add(detail);
            };
            MethodInfo report = typeof(SimulationController).GetMethod(
                "ReportEvolutionTraitRange", BindingFlags.Instance |
                BindingFlags.NonPublic);
            if (report == null) throw new InvalidOperationException("Range reporter missing.");
            report.Invoke(simulation, new object[] { block, source, target, 1 });
            Require(details.Count == 1 && details[0].Contains("目标=目标物种") &&
                details[0].Contains("适温=50进入") &&
                details[0].Contains("适湿差5") &&
                details[0].Contains("运动持续1天"),
                "First observed entry omitted target, gap or protection duration.");
            simulation.SetBlocks(new List<BlockInfo> { block });
            report.Invoke(simulation, new object[] { block, source, target, 2 });
            Require(details.Count == 1, "Staying inside emitted a duplicate.");
            source.movementAbility = 46;
            report.Invoke(simulation, new object[] { block, source, target, 3 });
            Require(details.Count == 2 && details[1].Contains("运动=46退出") &&
                details[1].Contains("运动差1") &&
                details[1].Contains("体型持续3天"),
                "Exit omitted the remaining gap or ongoing protection.");
            source.movementAbility = 45;
            report.Invoke(simulation, new object[] { block, source, target, 4 });
            Require(details.Count == 3 && details[2].Contains("运动=45进入"),
                "Re-entry was not reported exactly once.");
            CheckReadyWaitHistory();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(siteObject);
        }
    }

    private static void CheckReadyWaitHistory()
    {
        GameObject siteObject = new GameObject("EvolutionWaitSite");
        SpeciesData sourceSpecies = ScriptableObject.CreateInstance<SpeciesData>();
        SpeciesData target = ScriptableObject.CreateInstance<SpeciesData>();
        var console = new List<string>();
        Application.LogCallback onLog = (message, stack, type) =>
        {
            if (message.Contains("kind=formal_evolution_waiting"))
                console.Add(message);
        };
        try
        {
            typeof(SpeciesData).GetField("speciesName", BindingFlags.Instance |
                BindingFlags.NonPublic).SetValue(sourceSpecies, "待进化源");
            typeof(SpeciesData).GetField("speciesName", BindingFlags.Instance |
                BindingFlags.NonPublic).SetValue(target, "待进化目标");
            sourceSpecies.baseEcologicalNiche = EcologicalNiche.Land;
            target.baseEcologicalNiche = EcologicalNiche.Land;
            sourceSpecies.evolutionTargets.Add(target);
            sourceSpecies.baseFitTemperature = target.baseFitTemperature = 50;
            sourceSpecies.baseFitHumidity = target.baseFitHumidity = 50;
            sourceSpecies.baseMovementAbility = target.baseMovementAbility = 40;
            sourceSpecies.baseSize = target.baseSize = 20;
            sourceSpecies.baseFertility = target.baseFertility = 60;
            BlockInfo block = siteObject.AddComponent<BlockInfo>();
            block.waterCoverage = WaterCoverage.Land;
            block.temperature = block.humidity = 50;
            block.community = new List<PopulationData>();
            var source = new PopulationData
            {
                species = sourceSpecies, speciesAmount = 100,
                ecologicalNiche = EcologicalNiche.Land,
                trophicLevel = 0, trophicLevelInitialized = true,
                fitTemperature = 50, fitHumidity = 50,
                movementAbility = 40, size = 20, fertility = 60,
                nextEvolutionDay = 20
            };
            block.community.Add(source);
            var simulation = siteObject.AddComponent<SimulationController>();
            simulation.SetBlocks(new List<BlockInfo> { block });
            var events = new List<string>();
            simulation.OnEcologyEvent += (day, tile, population, kind, detail) =>
            {
                if (kind == "formal_evolution_waiting")
                    events.Add(day + " " + detail);
            };
            Application.logMessageReceived += onLog;
            MethodInfo run = typeof(SimulationController).GetMethod(
                "RunPresetEvolution", BindingFlags.Instance |
                BindingFlags.NonPublic);
            run.Invoke(simulation, new object[] { 10 });
            run.Invoke(simulation, new object[] { 11 });
            Require(events.Count == 2 && console.Count == 2 &&
                events[0].Contains("reason=cooldown") &&
                events[0].Contains("nextEvolutionDay=20") &&
                events[1].StartsWith("11 "),
                "Every ready but cooling day must reach JSONL and Debug.Log.");
            source.nextEvolutionDay = 0;
            block.plantBiomass = block.habitatRecovery = 0;
            run.Invoke(simulation, new object[] { 12 });
            Require(events.Count == 3 && console.Count == 3 &&
                events[2].Contains("reason=food_support") &&
                events[2].Contains("plantDemand="),
                "Ready traits with no renewable food need a measured refusal.");
            block.maxPlantBiomass = 1000000f;
            block.plantBiomass = 100000f;
            block.habitatRecovery = 100000;
            for (int day = 13; day <= 16; day++)
                run.Invoke(simulation, new object[] { day });
            Require(events.Count == 7 && console.Count == 7 &&
                events[6].Contains("reason=consecutive_days") &&
                events[6].Contains("effectiveDays=4") &&
                events[6].Contains("requiredDays=5"),
                "Every qualifying day before conversion needs streak progress.");
            run.Invoke(simulation, new object[] { 17 });
            PopulationData descendant = block.community.Find(p => p.species == target);
            Require(events.Count == 7 && console.Count == 7 &&
                descendant != null && source.nextEvolutionDay == 117 &&
                descendant.nextEvolutionDay == 117,
                "A successful conversion must not be reported as waiting.");
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(sourceSpecies);
            UnityEngine.Object.DestroyImmediate(siteObject);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
