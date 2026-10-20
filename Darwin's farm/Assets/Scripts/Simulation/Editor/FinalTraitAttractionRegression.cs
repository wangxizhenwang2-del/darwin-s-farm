#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class FinalTraitAttractionRegression
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void RunBatch()
    {
        var owner = new GameObject("Final trait attraction regression");
        var land = ScriptableObject.CreateInstance<SpeciesData>();
        var air = ScriptableObject.CreateInstance<SpeciesData>();
        var water = ScriptableObject.CreateInstance<SpeciesData>();
        try
        {
            var simulation = owner.AddComponent<SimulationController>();
            land.baseEcologicalNiche = EcologicalNiche.Land;
            land.baseFitTemperature = land.baseFitHumidity = 50;
            land.baseMovementAbility = land.baseSize = land.baseFertility = 50;
            air.baseEcologicalNiche = EcologicalNiche.Air;
            air.baseFitTemperature = air.baseFitHumidity = 50;
            air.baseMovementAbility = 80;
            air.baseSize = air.baseFertility = 50;
            water.baseEcologicalNiche = EcologicalNiche.Water;
            water.baseMovementAbility = 45;
            water.baseSize = 30;
            water.baseFertility = 55;

            var find = typeof(SimulationController).GetMethod(
                "FindFinalTraitGap", PrivateInstance);
            Require(find != null, "final-trait rule is missing");
            var population = new PopulationData {
                ecologicalNiche = EcologicalNiche.Land,
                fitTemperature = 70, fitHumidity = 50,
                movementAbility = 50, size = 50, fertility = 50
            };
            Require((int)find.Invoke(simulation, new object[] { population, land }) == 0,
                "land target should attract its remaining climate trait");
            population.fitTemperature = 50;
            population.fitHumidity = 70;
            population.movementAbility = 57;
            Require((int)find.Invoke(simulation, new object[] { population, air }) == 1,
                "flight movement above 50 must count as ready");
            population.fitHumidity = 50;
            population.movementAbility = 62;
            population.size = 30;
            population.fertility = 55;
            Require((int)find.Invoke(simulation, new object[] { population, water }) == 2,
                "water route should ignore climate traits");
            population.size = 70;
            Require((int)find.Invoke(simulation, new object[] { population, water }) == -1,
                "two unmet traits must not trigger final-trait attraction");

            var pace = typeof(SimulationController).GetMethod(
                "TargetCompletionPaceDirection", PrivateInstance);
            Require(pace != null, "target-completion pace is missing");
            var deadline = typeof(LandRuleTuning).GetField("targetCompletionDeadlineDays");
            Require(deadline != null, "general difficulty deadline is missing");
            var dayField = typeof(SimulationController).GetField(
                "currentSimulationDay", PrivateInstance);
            Require(dayField != null, "simulation day field is missing");

            population = new PopulationData {
                ecologicalNiche = EcologicalNiche.Land,
                fitTemperature = 50, fitHumidity = 50,
                movementAbility = 50, size = 70, fertility = 50,
                trackedEvolutionTarget = land
            };
            LandRuleTuning rules = simulation.ReadLandRules();
            deadline.SetValue(rules, 120);
            simulation.ApplyLandRules(rules);
            for (int day = 1; day <= 120; day++)
            {
                dayField.SetValue(simulation, day);
                object[] args = { population, -1 };
                int step = (int)pace.Invoke(simulation, args);
                Require((int)args[1] == 3, "size must be selected as final trait");
                population.size += step;
            }
            Require(population.size == 55,
                "general difficulty must finish the final size trait by day 120");
            Debug.Log("FINAL_TRAIT_ATTRACTION_PASS");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(owner);
            UnityEngine.Object.DestroyImmediate(land);
            UnityEngine.Object.DestroyImmediate(air);
            UnityEngine.Object.DestroyImmediate(water);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
#endif
