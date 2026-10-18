#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Real SimulationController/land ecology, isolated from the old neighbor after disconnection.
public static class NeighborMovementV2RecoveryUnity
{
    public static void RunBatch()
    {
        try
        {
            string folder = Environment.GetEnvironmentVariable("DF_V2_RECOVERY_OUT");
            if (string.IsNullOrEmpty(folder)) throw new InvalidOperationException("DF_V2_RECOVERY_OUT missing");
            Directory.CreateDirectory(folder);
            using (var daily = new StreamWriter(Path.Combine(folder, "daily.csv")))
            using (var summary = new StreamWriter(Path.Combine(folder, "summary.csv")))
            {
                daily.WriteLine("seed,day,movement,count,stock");
                summary.WriteLine("seed,finalMovement,minCount,neighborGateEvents,recoveryCandidates,recoverySelections,finalHash");
                for (int seed = 1; seed <= 80; seed++) RunCase(seed, daily, summary);
            }
            Debug.Log("NEIGHBOR_MOVEMENT_V2_RECOVERY_COMPLETE " + folder);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static void RunCase(int seed, StreamWriter daily, StreamWriter summary)
    {
        var sourceObject = new GameObject("disconnected-source-" + seed);
        var species = ScriptableObject.CreateInstance<SpeciesData>();
        try
        {
            BlockInfo source = sourceObject.AddComponent<BlockInfo>();
            source.temperature = 55;
            source.humidity = 55;
            source.habitatRecovery = 80000;
            source.maxPlantBiomass = 1000000f;
            source.plantBiomass = 200000f;
            source.SetNeighbors(new List<BlockInfo>());
            species.name = "disconnected-herbivore";
            species.trophicLevel = 0;
            var population = new PopulationData {
                species = species, lineageName = species.name,
                speciesAmount = 2500, trophicLevel = 0,
                trophicLevelInitialized = true,
                ecologicalNiche = EcologicalNiche.Land,
                fitTemperature = 55, fitHumidity = 55,
                movementAbility = 100, size = 14, fertility = 65
            };
            source.community = new List<PopulationData> { population };
            SimulationController simulation = sourceObject.AddComponent<SimulationController>();
            simulation.SetBlocks(new List<BlockInfo> { source });
            simulation.ConfigureDeterministicRun(seed);
            simulation.EnableNeighborMovementFormula = true;
            simulation.SetEconomyEnabled(false);
            simulation.SetPresetEvolutionParameters(false, 5, 0.9f, 5);
            simulation.SetMutationParameters(1, 1f, 1f);
            simulation.SetMutationEnabled(true);
            simulation.SetMigrationParameters(true, 7, 100f, .8f, .5f, .2f, 7);
            int gate = 0, candidates = 0, selections = 0, minCount = population.speciesAmount;
            simulation.OnEcologyEvent += (day, tile, member, kind, detail) => {
                if (kind == "neighbor_mutation_gate") gate++;
                if (kind == "mutation_choice" && detail.Contains("movement_cost_recovery")) {
                    candidates++;
                    if (detail.Contains("selected=2↓")) selections++;
                }
            };
            for (int day = 1; day <= 245; day++) {
                simulation.SimulateDay(day);
                minCount = Math.Min(minCount, population.speciesAmount);
                daily.WriteLine(seed + "," + day + "," + population.movementAbility + "," +
                    population.speciesAmount + "," + source.plantBiomass.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            summary.WriteLine(seed + "," + population.movementAbility + "," + minCount +
                "," + gate + "," + candidates + "," + selections + "," +
                simulation.CanonicalModelHash());
        }
        finally {
            UnityEngine.Object.DestroyImmediate(species);
            UnityEngine.Object.DestroyImmediate(sourceObject);
        }
    }
}
#endif
