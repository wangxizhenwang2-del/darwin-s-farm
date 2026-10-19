#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Uses the actual WhiteBox clock -> environment -> SimulateDay event chain.
[InitializeOnLoad]
public static class CrossNicheV06WhiteBoxDayAudit
{
    private const string Pending = "DarwinFarm.V06WhiteBoxDay.Pending";
    private static double started;
    private static bool finished;

    static CrossNicheV06WhiteBoxDayAudit()
    { if (SessionState.GetBool(Pending, false)) Register(); }

    [MenuItem("Tools/Darwin Farm/Validation/Cross Niche v0.6/WhiteBox Water Day")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity");
        SessionState.SetBool(Pending, true);
        Register();
        EditorApplication.EnterPlaymode();
    }

    private static void Register()
    {
        started = EditorApplication.timeSinceStartup;
        finished = false;
        EditorApplication.update -= Wait;
        EditorApplication.update += Wait;
    }

    private static void Wait()
    {
        if (finished) return;
        if (EditorApplication.timeSinceStartup - started > 90)
        { Finish(new Exception("WhiteBox Play mode initialization timed out")); return; }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        SimulationEnvironmentController environment =
            UnityEngine.Object.FindFirstObjectByType<SimulationEnvironmentController>();
        if (environment == null ||
            !environment.TryRead(Vector2Int.zero, out _)) return;
        try { Audit(environment); Finish(null); }
        catch (Exception error) { Finish(error); }
    }

    private static void Audit(SimulationEnvironmentController environment)
    {
        MapGridManager grid = environment.GetComponent<MapGridManager>();
        SimulationController simulation =
            environment.GetComponent<SimulationController>();
        SimulationTime clock = environment.GetComponent<SimulationTime>();
        Check(grid != null && simulation != null && clock != null,
            "WhiteBox map/simulation/clock missing");
        clock.Pause();
        simulation.SetEconomyEnabled(false);
        simulation.EnableRandomEvents = false;
        simulation.SetWaterEcologyEnabled(true);
        simulation.SetAirEcologyEnabled(false);
        simulation.SetMigrationParameters(false, 7, 100f, .8f, .5f, .2f, 7);
        simulation.SetPresetEvolutionParameters(false, 5, .1f, 10);
        simulation.SetMutationEnabled(false);

        MapTileDefinition lake = AssetDatabase.LoadAssetAtPath<MapTileDefinition>(
            "Assets/Scripts/map/data/湖泊.asset");
        Check(lake != null && grid.TryPlaceTile(new Vector2Int(1, 0), lake) &&
            grid.TryPlaceTile(new Vector2Int(2, 0), lake),
            "could not place two WhiteBox lake cells");
        Vector2Int a = new Vector2Int(1, 0), b = new Vector2Int(2, 0);
        bool hasA = grid.TryGetTile(a, out MapTileInstance aTile);
        bool hasB = grid.TryGetTile(b, out MapTileInstance bTile);
        Check(hasA && hasB &&
            aTile.Block != null && bTile.Block != null,
            "lake blocks missing after bridge synchronization");
        BlockInfo first = aTile.Block, second = bTile.Block;
        Check(environment.TryGetWaterArea(a, out var firstArea, out _) &&
            firstArea.Count == 1 &&
            environment.TryGetWaterArea(b, out var secondArea, out _) &&
            secondArea.Count == 1, "water tools still use shared area");
        Check(simulation.ApplyPlantBiomass(first, 200000f) &&
            simulation.ApplyPlantBiomass(second, 200000f),
            "could not prepare real cell stocks");
        Check(environment.TryApplyWater(a,
                SimulationInterventionKind.PlantRecovery, 25000, out _) &&
            environment.TryApplyWater(b,
                SimulationInterventionKind.PlantRecovery, -50000, out _) &&
            environment.TryApplyWater(b,
                SimulationInterventionKind.PlantRecovery, -25000, out _),
            "could not set independent current R through water operation");
        Check(first.habitatRecovery == 100000 &&
            second.habitatRecovery == 0, "current water R did not publish");
        clock.NextDay();
        Check(Near(first.plantBiomass, 300000) &&
            Near(second.plantBiomass, 200000) &&
            Near(first.plantGrowthToday, 100000) &&
            Near(second.plantGrowthToday, 0),
            "day one did not grow each water cell exactly once");
        Check(environment.TryApplyWater(a,
            SimulationInterventionKind.PlantRecovery, 25000, out _),
            "could not change A recovery after day one");
        clock.NextDay();
        Check(Near(first.plantBiomass, 425000) &&
            Near(second.plantBiomass, 200000) &&
            Near(first.plantGrowthToday, 125000),
            "day two did not use current edited R independently");
        Check(simulation.ApplyPlantBiomass(first,
            first.maxPlantBiomass - 10000), "could not prepare cap boundary");
        clock.NextDay();
        Check(Near(first.plantBiomass, first.maxPlantBiomass) &&
            Near(first.plantGrowthToday, 10000) &&
            Near(second.plantBiomass, 200000),
            "water growth exceeded R or per-cell capacity");
        SpeciesData swimmer = AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/BiowebV46/Species/鹿游-103a527e.asset");
        Check(swimmer != null, "water consumer asset missing");
        Check(simulation.ApplyPlantBiomass(second, 100000),
            "could not prepare R=0 historical stock");
        second.community.Add(new PopulationData
        {
            species = swimmer, speciesAmount = 100,
            ecologicalNiche = EcologicalNiche.Water,
            habitatNiche = swimmer.baseHabitatNiche,
            trophicLevel = swimmer.trophicLevel,
            trophicLevelInitialized = true,
            movementAbility = swimmer.baseMovementAbility,
            size = swimmer.baseSize, fertility = swimmer.baseFertility,
            fitTemperature = 0, fitHumidity = 0
        });
        clock.NextDay();
        Check(Near(second.plantGrowthToday, 0) &&
            second.plantBiomass < 100000f &&
            second.consumedBiomassToday > 0f,
            "R=0 water consumer did not spend historical stock");
        DailyEcologyLedger ledger = simulation.LastEcologyLedger;
        Check(ledger != null && Math.Abs(ledger.massResidual) < 1,
            "WhiteBox day mass ledger residual");
        foreach (EcologySpeciesDailyAccount account in ledger.species)
            Check(account.Residual == 0,
                "WhiteBox day population ledger residual");
        Debug.Log("V06_WHITEBOX_WATER_DAY_PASS days=" + clock.currentDay);
    }

    private static bool Near(float left, float right) =>
        Math.Abs(left - right) < .2f;

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void Finish(Exception error)
    {
        finished = true;
        SessionState.SetBool(Pending, false);
        EditorApplication.update -= Wait;
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/V06WhiteBoxWaterDay.result.txt",
            error == null ? "PASS" : "FAIL: " + error);
        if (error == null) Debug.Log("V06_WHITEBOX_WATER_DAY_PASS");
        else Debug.LogException(error);
        if (Application.isBatchMode)
            EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
