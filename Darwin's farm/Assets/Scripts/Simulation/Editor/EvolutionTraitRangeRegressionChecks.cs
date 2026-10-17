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
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(siteObject);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
