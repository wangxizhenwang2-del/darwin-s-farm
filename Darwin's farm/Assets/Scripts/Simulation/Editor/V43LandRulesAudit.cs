using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 2026-10-09 13:56 +08:00: deterministic edit-mode checks for the V4.3 land
// rules touched by the daily-evolution and shared-food-web migration.
public static class V43LandRulesAudit
{
    private static readonly MethodInfo RunEvolution = typeof(SimulationController)
        .GetMethod("RunPresetEvolution", BindingFlags.Instance | BindingFlags.NonPublic);
    private static Scene auditScene;

    [MenuItem("Darwin's Farm/Run V4.3 Land Rules Audit")]
    public static void Run()
    {
        UnityEngine.Random.State priorRandomState = UnityEngine.Random.state;
        auditScene = EditorSceneManager.NewPreviewScene();
        try
        {
            CheckProgressAndCooldown();
            CheckSameDayTargetConflict();
            CheckPredatorFoodGate();
            CheckSharedPreyQuota();
            Debug.Log("V4.3 land rules audit passed.");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
        finally
        {
            UnityEngine.Random.state = priorRandomState;
            EditorSceneManager.ClosePreviewScene(auditScene);
        }
    }

    private static SpeciesData Species(string label, int level)
    {
        SpeciesData species = ScriptableObject.CreateInstance<SpeciesData>();
        species.name = label;
        typeof(SpeciesData).GetField("speciesName", BindingFlags.Instance |
            BindingFlags.NonPublic).SetValue(species, label);
        species.trophicLevel = level;
        species.baseEcologicalNiche = EcologicalNiche.Land;
        species.baseFitTemperature = 50;
        species.baseFitHumidity = 50;
        species.baseMovementAbility = 50;
        species.baseSize = 10;
        species.baseFertility = 50;
        return species;
    }

    private static PopulationData Population(SpeciesData species, int count) =>
        new PopulationData
        {
            species = species, speciesAmount = count,
            ecologicalNiche = EcologicalNiche.Land,
            trophicLevel = species.trophicLevel, trophicLevelInitialized = true,
            fitTemperature = 50, fitHumidity = 50,
            movementAbility = 50, size = 10, fertility = 50
        };

    private static BlockInfo Block(string label, params PopulationData[] populations)
    {
        var root = new GameObject(label);
        SceneManager.MoveGameObjectToScene(root, auditScene);
        var block = root.AddComponent<BlockInfo>();
        block.waterCoverage = WaterCoverage.Land;
        block.temperature = 50;
        block.humidity = 50;
        block.elevation = 0;
        block.maxPlantBiomass = 1000000f;
        block.plantBiomass = 100000f;
        block.habitatRecovery = 100000;
        block.community = new List<PopulationData>(populations);
        return block;
    }

    private static SimulationController Controller(BlockInfo block,
        params SpeciesData[] catalog)
    {
        var root = new GameObject("V43 Audit Controller");
        SceneManager.MoveGameObjectToScene(root, auditScene);
        var controller = root.AddComponent<SimulationController>();
        controller.SetMutationEnabled(false);
        controller.SetMigrationParameters(false, 7, 1f, 0f, 0f, 0f, 7);
        controller.SetEvolutionNetworkSpecies(catalog);
        controller.SetBlocks(new List<BlockInfo> { block });
        return controller;
    }

    private static void Evolve(SimulationController controller, int day)
    {
        if (RunEvolution == null) throw new Exception("Evolution entry point missing.");
        RunEvolution.Invoke(controller, new object[] { day });
    }

    private static void CheckProgressAndCooldown()
    {
        SpeciesData target = Species("audit-target", 0);
        SpeciesData source = Species("audit-source", 0);
        target.evolutionTargets.Add(source); // A single reverse edge must work.
        PopulationData ancestor = Population(source, 500);
        BlockInfo block = Block("audit-progress", ancestor);
        SimulationController controller = Controller(block, target, source);
        try
        {
            UnityEngine.Random.State state = default;
            bool foundFailingFirstDraw = false;
            for (int seed = 1; seed <= 1000; seed++)
            {
                UnityEngine.Random.InitState(seed);
                state = UnityEngine.Random.state;
                if (UnityEngine.Random.value < 0.01f) continue;
                foundFailingFirstDraw = true;
                break;
            }
            Check(foundFailingFirstDraw, "Could not find a deterministic failed draw.");
            UnityEngine.Random.state = state;
            Evolve(controller, 1);
            Check(ancestor.evolutionConversions.Count == 1 &&
                ancestor.evolutionConversions[0].effectiveDays == 1,
                "Day one must add one effective day on a reverse edge.");
            Evolve(controller, 2);
            EvolutionConversionRecord record = ancestor.evolutionConversions[0];
            Check(record.effectiveDays == 2 && record.failedDraws == 1,
                "Day two must make the first 1% draw.");
            block.plantBiomass = 0f;
            Evolve(controller, 3);
            Check(record.effectiveDays == 2 && record.failedDraws == 1,
                "Missing food must freeze progress, not reset it.");
            block.plantBiomass = 100000f;
            Evolve(controller, 4);
            Check(record.effectiveDays == 3, "Restored food must resume progress.");
            record.failedDraws = 99;
            Evolve(controller, 5);
            PopulationData daughter = block.community.Find(p => p.species == target);
            Check(daughter != null && daughter.speciesAmount == 50 &&
                ancestor.speciesAmount == 450, "Success must split 10% once.");
            Check(record.effectiveDays == 0 && record.failedDraws == 0 &&
                ancestor.nextEvolutionDay == 55 && daughter.nextEvolutionDay == 55,
                "Success must reset only its target and cool both groups for 50 days.");
            block.community.Remove(daughter);
            Evolve(controller, 54);
            Check(record.effectiveDays == 0, "Day D+49 is still cooling down.");
            Evolve(controller, 55);
            Check(record.effectiveDays == 1, "Day D+50 must be eligible again.");
        }
        finally { Clean(controller, block, target, source); }
    }

    private static void CheckSameDayTargetConflict()
    {
        SpeciesData target = Species("conflict-target", 0);
        SpeciesData source = Species("conflict-source", 0);
        target.evolutionTargets.Add(source);
        PopulationData first = Population(source, 500);
        PopulationData second = Population(source, 500);
        first.evolutionConversions.Add(new EvolutionConversionRecord
            { target = target, effectiveDays = 1, failedDraws = 99 });
        second.evolutionConversions.Add(new EvolutionConversionRecord
            { target = target, effectiveDays = 1, failedDraws = 99 });
        BlockInfo block = Block("audit-conflict", first, second);
        SimulationController controller = Controller(block, target, source);
        try
        {
            UnityEngine.Random.InitState(41);
            Evolve(controller, 1);
            Check(block.community.FindAll(p => p.species == target).Count == 1,
                "Only one daughter may occupy a contested target.");
            Check(first.speciesAmount + second.speciesAmount == 950,
                "Only the winning source may lose founders.");
            PopulationData loser = first.speciesAmount == 500 ? first : second;
            Check(loser.evolutionConversions[0].effectiveDays == 2 &&
                loser.evolutionConversions[0].failedDraws == 99,
                "Losing the target must retain accumulated progress.");
        }
        finally { Clean(controller, block, target, source); }
    }

    private static void CheckPredatorFoodGate()
    {
        SpeciesData target = Species("hungry-target", 2);
        SpeciesData source = Species("hungry-source", 2);
        SpeciesData herbivore = Species("possible-prey", 0);
        target.evolutionTargets.Add(source);
        PopulationData ancestor = Population(source, 500);
        BlockInfo block = Block("audit-food-gate", ancestor);
        SimulationController controller = Controller(block, target, source, herbivore);
        try
        {
            Evolve(controller, 1);
            Check(ancestor.evolutionConversions.Count == 0,
                "A predator target without prey cannot accumulate a day.");
            block.community.Add(Population(herbivore, 100));
            Evolve(controller, 2);
            Check(ancestor.evolutionConversions.Count == 1 &&
                ancestor.evolutionConversions[0].effectiveDays == 1,
                "An L2 target must accept an L0 prey when food is restored.");
        }
        finally { Clean(controller, block, target, source, herbivore); }
    }

    private static void CheckSharedPreyQuota()
    {
        SpeciesData food = Species("shared-prey", 0);
        SpeciesData middle = Species("first-hunter", 1);
        SpeciesData top = Species("second-hunter", 2);
        middle.preySpecies.Add(food);
        top.preySpecies.Add(food);
        BlockInfo first = Block("food-order-one", Population(food, 1000),
            Population(middle, 100), Population(top, 100));
        BlockInfo second = Block("food-order-two", Population(food, 1000),
            Population(top, 100), Population(middle, 100));
        SimulationController controller = Controller(first, food, middle, top);
        try
        {
            UnityEngine.Random.InitState(313);
            controller.SimulateBlock(first);
            int firstDeaths = first.community[0].predationDeathsToday;
            UnityEngine.Random.InitState(313);
            controller.SimulateBlock(second);
            int secondDeaths = second.community[0].predationDeathsToday;
            Check(firstDeaths == secondDeaths,
                "Shared prey deaths must not depend on hunter list order.");
            Check(firstDeaths <= 100,
                "L1 and L2 must not each take a separate full prey quota.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(second.gameObject);
            Clean(controller, first, food, middle, top);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("V4.3 audit: " + message);
    }

    private static void Clean(SimulationController controller, BlockInfo block,
        params SpeciesData[] species)
    {
        UnityEngine.Object.DestroyImmediate(controller.gameObject);
        UnityEngine.Object.DestroyImmediate(block.gameObject);
        foreach (SpeciesData entry in species) UnityEngine.Object.DestroyImmediate(entry);
    }
}
