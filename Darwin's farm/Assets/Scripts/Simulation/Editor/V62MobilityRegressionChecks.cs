#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Scene-independent checks against the actual Model. Run in a batch Editor
// with -executeMethod V62MobilityRegressionChecks.RunBatch.
public static class V62MobilityRegressionChecks
{
    private static readonly BindingFlags Hidden = BindingFlags.Instance |
        BindingFlags.NonPublic;

    public static void RunBatch()
    {
        try
        {
            Debug.Log(RunChecks());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    public static string RunChecks()
    {
        int passed = 0;
        using (var world = new World(240))
        {
            Require(world.SourcePopulation.speciesAmount == 1000 &&
                !(bool)typeof(SimulationController).GetField(
                    "enableV62Mobility", Hidden).GetValue(world.Simulation),
                "V6.2 must be off by default.");
            passed++;
            world.EnableV62();
            string before = world.Simulation.CanonicalModelHash();
            var signals = world.Simulation.ReadLandSelectionSignals(world.Source,
                world.SourcePopulation);
            Require(signals.neighborAttraction > 0f &&
                signals.movementCeiling == 100 &&
                world.Simulation.CanonicalModelHash() == before,
                "Long-term neighboring opportunity is missing or mutates the ledger.");
            passed++;
            world.Target.elevation = 2;
            Require(world.Simulation.ReadLandSelectionSignals(world.Source,
                world.SourcePopulation).neighborAttraction == 0f,
                "A two-level cliff offered a movement opportunity.");
            passed++;
            world.Target.elevation = 1;
            world.Target.habitatRecovery = 0;
            Require(world.Simulation.ReadLandSelectionSignals(world.Source,
                world.SourcePopulation).neighborAttraction == 0f,
                "A target without renewable food offered a movement opportunity.");
            passed++;
            world.Target.habitatRecovery = 90000;
            world.SourcePopulation.movementAbility = 60;
            world.TryMigrationDays(1, 100);
            Require(world.Migrations.Count == 0 && world.Total() == 1000,
                "Movement 60 crossed a one-level slope or changed population.");
            passed++;
            world.SourcePopulation.movementAbility = 61;
            world.TryMigrationDays(101, 300);
            Require(world.Migrations.Count == 1 && world.Migrations[0].Amount >= 10 &&
                world.Migrations[0].Amount <= 100 && world.Total() == 1000,
                "Movement 61 failed to migrate 10-100 animals conservatively.");
            passed++;
        }
        using (var world = new World(242, true))
        {
            world.EnableV62();
            world.SourcePopulation.movementAbility = 61;
            string residentId = world.TargetResident.PopulationId;
            world.TryMigrationDays(1, 300);
            Require(world.Migrations.Count == 1 && world.Total() == 1030 &&
                world.Target.community.Count == 1 &&
                world.Target.community[0].PopulationId == residentId,
                "Same-species arrival did not merge conservatively.");
            passed++;
        }
        using (var world = new World(243))
        {
            world.EnableV62();
            world.Target.elevation = 0;
            world.SourcePopulation.movementAbility = 30;
            world.TryMigrationDays(1, 100);
            Require(world.Migrations.Count == 0 && world.Total() == 1000,
                "Movement 30 crossed the flat-land threshold.");
            world.SourcePopulation.movementAbility = 31;
            world.TryMigrationDays(101, 300);
            Require(world.Migrations.Count == 1 && world.Total() == 1000,
                "Movement 31 failed the flat-land conservation check.");
            passed++;
        }
        string first = ReplayFingerprint(241);
        string second = ReplayFingerprint(241);
        Require(first == second, "Same seed produced a different migration outcome.");
        passed++;
        return "V6.2 Model mobility checks: " + passed + "/9 passed; seed241=" + first;
    }

    private static string ReplayFingerprint(int seed)
    {
        using (var world = new World(seed))
        {
            world.EnableV62();
            world.SourcePopulation.movementAbility = 61;
            world.TryMigrationDays(1, 300);
            Require(world.Migrations.Count == 1 && world.Total() == 1000,
                "Fixed-seed run did not migrate conservatively.");
            return world.Migrations[0].Day + ":" + world.Migrations[0].Amount +
                ":" + world.Simulation.CanonicalModelHash();
        }
    }

    private sealed class World : IDisposable
    {
        private readonly GameObject sourceObject, targetObject;
        private readonly MethodInfo runMigration;
        internal readonly BlockInfo Source, Target;
        internal readonly PopulationData SourcePopulation;
        internal readonly PopulationData TargetResident;
        internal readonly SimulationController Simulation;
        internal readonly List<PopulationTransitionResult> Migrations =
            new List<PopulationTransitionResult>();

        internal World(int seed, bool sameSpeciesTarget = false)
        {
            sourceObject = new GameObject("V62_Source");
            targetObject = new GameObject("V62_Target");
            Source = sourceObject.AddComponent<BlockInfo>();
            Target = targetObject.AddComponent<BlockInfo>();
            Source.temperature = Target.temperature = 50;
            Source.humidity = Target.humidity = 50;
            Source.elevation = 0;
            Target.elevation = 1;
            Source.waterCoverage = Target.waterCoverage = WaterCoverage.Land;
            Source.habitatRecovery = 1000;
            Target.habitatRecovery = 90000;
            Source.maxPlantBiomass = Target.maxPlantBiomass = 1000000f;
            Source.plantBiomass = Target.plantBiomass = 90000f;
            Source.SetNeighbors(new List<BlockInfo> { Target });
            Target.SetNeighbors(new List<BlockInfo> { Source });
            SourcePopulation = new PopulationData
            {
                lineageName = "fixture-herbivore", speciesAmount = 1000,
                ecologicalNiche = EcologicalNiche.Land, trophicLevel = 0,
                trophicLevelInitialized = true, fitTemperature = 50,
                fitHumidity = 50, movementAbility = 10, size = 8,
                fertility = 70, habitatNiche = 50
            };
            Source.community = new List<PopulationData> { SourcePopulation };
            TargetResident = sameSpeciesTarget ? new PopulationData
            {
                lineageName = "fixture-herbivore", speciesAmount = 30,
                ecologicalNiche = EcologicalNiche.Land, trophicLevel = 0,
                trophicLevelInitialized = true, fitTemperature = 50,
                fitHumidity = 50, movementAbility = 61, size = 8,
                fertility = 70, habitatNiche = 50
            } : null;
            Target.community = TargetResident == null
                ? new List<PopulationData>()
                : new List<PopulationData> { TargetResident };
            Simulation = sourceObject.AddComponent<SimulationController>();
            Simulation.SetBlocks(new List<BlockInfo> { Source, Target });
            Simulation.ConfigureDeterministicRun(seed);
            Simulation.OnMigrationResolved += result =>
            {
                if (result.Kind != PopulationTransitionKind.Migration) return;
                Require(result.SourceCountBefore + result.TargetCountBefore ==
                    result.SourceCountAfter + result.TargetCountAfter,
                    "Migration event violates population conservation.");
                Migrations.Add(result);
            };
            runMigration = typeof(SimulationController).GetMethod("RunMigration", Hidden);
            Require(runMigration != null, "Migration planner was not found.");
        }

        internal void EnableV62()
        {
            Type parameters = typeof(SimulationController).Assembly.GetType(
                "V62MobilityParameters", true);
            object candidate = parameters.GetProperty("Candidate",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            typeof(SimulationController).GetMethod("ConfigureV62Mobility", Hidden)
                .Invoke(Simulation, new[] { (object)true, candidate });
        }

        internal void TryMigrationDays(int first, int last)
        {
            for (int day = first; day <= last && Migrations.Count == 0; day++)
                runMigration.Invoke(Simulation, new object[] { day });
        }

        internal int Total()
        {
            int count = 0;
            foreach (PopulationData population in Source.community)
                if (population != null) count += population.speciesAmount;
            foreach (PopulationData population in Target.community)
                if (population != null) count += population.speciesAmount;
            return count;
        }

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(targetObject);
            UnityEngine.Object.DestroyImmediate(sourceObject);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
