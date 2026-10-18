#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

// Scene-independent run of the actual SimulationController and LandLifeRules.
public static class NeighborOpportunityUnityRegression
{
    private const int Seeds = 80, Days = 1500;

    public static void RunBatch()
    {
        try
        {
            string folder = Environment.GetEnvironmentVariable("DF_MIGRATION_OUT");
            if (string.IsNullOrEmpty(folder)) throw new InvalidOperationException("DF_MIGRATION_OUT missing");
            string requestedSeeds = Environment.GetEnvironmentVariable("DF_MIGRATION_SEEDS");
            int seedCount = string.IsNullOrEmpty(requestedSeeds) ? Seeds :
                Math.Max(1, Math.Min(Seeds, int.Parse(requestedSeeds,
                    CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(folder);
            string detailPath = Path.Combine(folder, "events.csv");
            string summaryPath = Path.Combine(folder, "summary.csv");
            using (var events = new StreamWriter(detailPath, false))
            using (var summary = new StreamWriter(summaryPath, false))
            {
                events.WriteLine("scenario,seed,day,source,population,kind,detail");
                summary.WriteLine("scenario,seed,reachedDay,migrationDay,migrationCount,migrationAmount,finalMovement,finalPopulation,finalStock,finalHash");
                for (int slope = 0; slope <= 1; slope++)
                    for (int seed = 1; seed <= seedCount; seed++)
                    {
                        RunCase(slope, seed, events, summary);
                        if (seed % 10 == 0) { events.Flush(); summary.Flush();
                            Debug.Log("NeighborOpportunity " + (slope == 0 ? "flat" : "slope") +
                                " seed " + seed + "/" + seedCount); }
                    }
            }
            Debug.Log("NEIGHBOR_OPPORTUNITY_UNITY_REGRESSION_COMPLETE " + folder);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void RunCase(int slope, int seed, StreamWriter events,
        StreamWriter summary)
    {
        var sourceObject = new GameObject("opportunity-source-" + seed);
        var targetObject = new GameObject("opportunity-target-" + seed);
        var species = ScriptableObject.CreateInstance<SpeciesData>();
        try
        {
            BlockInfo source = sourceObject.AddComponent<BlockInfo>();
            BlockInfo target = targetObject.AddComponent<BlockInfo>();
            source.temperature = target.temperature = 55;
            source.humidity = target.humidity = 55;
            source.elevation = 0; target.elevation = slope;
            source.habitatRecovery = 80000;
            target.habitatRecovery = 120000;
            source.maxPlantBiomass = target.maxPlantBiomass = 1000000f;
            source.plantBiomass = target.plantBiomass = 200000f;
            source.SetNeighbors(new List<BlockInfo> { target });
            target.SetNeighbors(new List<BlockInfo> { source });
            species.name = "opportunity-herbivore";
            species.trophicLevel = 0;
            species.baseMovementAbility = 15;
            species.baseFitTemperature = species.baseFitHumidity = 55;
            species.baseSize = 14; species.baseFertility = 65;
            var population = new PopulationData
            {
                species = species, lineageName = species.name,
                speciesAmount = 2500, trophicLevel = 0,
                trophicLevelInitialized = true,
                ecologicalNiche = EcologicalNiche.Land,
                fitTemperature = 55, fitHumidity = 55,
                movementAbility = 15, size = 14, fertility = 65
            };
            source.community = new List<PopulationData> { population };
            target.community = new List<PopulationData>();
            SimulationController simulation = sourceObject.AddComponent<SimulationController>();
            simulation.SetBlocks(new List<BlockInfo> { source, target });
            simulation.ConfigureDeterministicRun(seed);
            simulation.EnableNeighborMovementFormula = true;
            simulation.SetEconomyEnabled(false);
            simulation.SetPresetEvolutionParameters(false, 5, 0.9f, 5);
            simulation.SetMutationParameters(1, 1f, 1f);
            simulation.SetMutationEnabled(true);
            simulation.SetMigrationParameters(true, 7, 100f, .8f, .5f, .2f, 7);
            string scenario = slope == 0 ? "flat" : "slope";
            int reached = 0, migrated = 0, migrations = 0, moved = 0;
            simulation.OnEcologyEvent += (day, tile, member, kind, detail) =>
            {
                if (kind != "neighbor_growth_opportunity" &&
                    kind != "neighbor_mutation_gate" &&
                    kind != "migration_forecast" &&
                    kind != "mutation_choice" && kind != "migration_attempt") return;
                events.WriteLine(Csv(scenario) + "," + seed + "," + day + "," +
                    Csv(tile == source ? "source" : "target") + "," +
                    Csv(member == null ? "" : member.PopulationId) + "," +
                    Csv(kind) + "," + Csv(detail));
            };
            simulation.OnMigrationResolved += result =>
            {
                if (result.Kind != PopulationTransitionKind.Migration) return;
                if (result.SourceCountBefore + result.TargetCountBefore !=
                    result.SourceCountAfter + result.TargetCountAfter)
                    throw new InvalidOperationException("Migration count not conserved");
                migrations++;
                moved += result.Amount;
                if (migrated == 0) migrated = result.Day;
                if (result.Amount < 10 || result.Amount > 100)
                    throw new InvalidOperationException("Migration amount outside 10-100");
            };
            for (int day = 1; day <= Days; day++)
            {
                simulation.SimulateDay(day);
                if (reached == 0 && population.movementAbility >
                    (slope == 0 ? 30 : 60)) reached = day;
            }
            summary.WriteLine(scenario + "," + seed + "," + reached + "," +
                migrated + "," + migrations + "," + moved + "," +
                population.movementAbility + "," + population.speciesAmount + "," +
                source.plantBiomass.ToString("R", CultureInfo.InvariantCulture) + "," +
                simulation.CanonicalModelHash());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(species);
            UnityEngine.Object.DestroyImmediate(targetObject);
            UnityEngine.Object.DestroyImmediate(sourceObject);
        }
    }

    private static string Csv(string value) => "\"" +
        (value ?? "").Replace("\"", "\"\"") + "\"";
}
#endif
