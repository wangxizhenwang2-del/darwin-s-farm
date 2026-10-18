#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using DarwinFarm.MigrationOpportunity;
using UnityEditor;
using UnityEngine;

public static class NeighborOpportunityUnityBoundary
{
    private static readonly BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void RunBatch()
    {
        try
        {
            string path = Environment.GetEnvironmentVariable("DF_MIGRATION_BOUNDARY");
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("DF_MIGRATION_BOUNDARY missing");
            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine("case,inductionEligible,inductionReason,directEligible,directReason,delta,bonus,physical,gate,canMigrate,hashUnchanged");
                var srcObject = new GameObject("boundary-source");
                var dstObject = new GameObject("boundary-target");
                var species = ScriptableObject.CreateInstance<SpeciesData>();
                try
                {
                    BlockInfo src = srcObject.AddComponent<BlockInfo>();
                    BlockInfo dst = dstObject.AddComponent<BlockInfo>();
                    src.temperature = dst.temperature = 55;
                    src.humidity = dst.humidity = 55;
                    src.habitatRecovery = 80000;
                    dst.habitatRecovery = 120000;
                    src.maxPlantBiomass = dst.maxPlantBiomass = 1000000f;
                    src.plantBiomass = dst.plantBiomass = 200000f;
                    src.SetNeighbors(new List<BlockInfo> { dst });
                    dst.SetNeighbors(new List<BlockInfo> { src });
                    species.name = "boundary-herbivore";
                    species.trophicLevel = 0;
                    var pop = new PopulationData { species = species,
                        speciesAmount = 2500, trophicLevel = 0,
                        trophicLevelInitialized = true,
                        ecologicalNiche = EcologicalNiche.Land,
                        fitTemperature = 55, fitHumidity = 55,
                        movementAbility = 15, size = 14, fertility = 65 };
                    src.community = new List<PopulationData> { pop };
                    dst.community = new List<PopulationData>();
                    SimulationController sim = srcObject.AddComponent<SimulationController>();
                    sim.SetBlocks(new List<BlockInfo> { src, dst });
                    sim.ConfigureDeterministicRun(1);
                    Record(writer, "flat-m15", sim, src, pop);
                    dst.elevation = 1;
                    Record(writer, "slope-m15", sim, src, pop);
                    dst.elevation = 2;
                    Record(writer, "cliff", sim, src, pop);
                    dst.elevation = 0;
                    dst.SetNeighbors(new List<BlockInfo>());
                    Record(writer, "nonreciprocal", sim, src, pop);
                    dst.SetNeighbors(new List<BlockInfo> { src });
                    dst.habitatRecovery = 0;
                    Record(writer, "no-renewal", sim, src, pop);
                    dst.habitatRecovery = 120000;
                    dst.community.Add(new PopulationData { species = species,
                        speciesAmount = 1800, trophicLevel = 0,
                        trophicLevelInitialized = true,
                        ecologicalNiche = EcologicalNiche.Land,
                        fitTemperature = 55, fitHumidity = 55,
                        movementAbility = 15, size = 14, fertility = 65 });
                    Record(writer, "same-species-resident", sim, src, pop);
                    dst.community.Clear();
                    pop.trophicLevel = 1;
                    Record(writer, "predator-history-unavailable", sim, src, pop);
                    pop.trophicLevel = 0;
                    foreach (int n in new[] { 9, 10, 100, 1001 })
                    { pop.speciesAmount = n; Record(writer, "N" + n, sim, src, pop); }
                    pop.speciesAmount = 2500;
                    foreach (int m in new[] { 0, 15, 30, 31, 60, 61, 100 })
                    { pop.movementAbility = m; Record(writer, "m" + m, sim, src, pop); }
                    dst.elevation = 1;
                    foreach (int m in new[] { 30, 31, 60, 61 })
                    { pop.movementAbility = m; Record(writer, "slope-m" + m, sim, src, pop); }
                    // A real movement-100 population must still be allowed to migrate.
                    dst.elevation = 0;
                    pop.movementAbility = 100;
                    pop.speciesAmount = 2500;
                    int actualMigrations = 0;
                    sim.EnableNeighborMovementFormula = true;
                    sim.SetEconomyEnabled(false);
                    sim.SetMutationEnabled(false);
                    sim.SetMigrationParameters(true, 7, 100f, .8f, .5f, .2f, 7);
                    sim.OnMigrationResolved += transition => {
                        if (transition.Kind == PopulationTransitionKind.Migration) {
                            actualMigrations++;
                            if (transition.SourceCountBefore + transition.TargetCountBefore !=
                                transition.SourceCountAfter + transition.TargetCountAfter)
                                throw new InvalidOperationException("movement-100 migration count mismatch");
                        }
                    };
                    for (int day = 1; day <= 140 && actualMigrations == 0; day++)
                        sim.SimulateDay(day);
                    writer.WriteLine("actual-m100-migration," + (actualMigrations > 0) +
                        ",None," + (actualMigrations > 0) + ",None,0,0,true,0," +
                        (actualMigrations > 0) + ",true");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(species);
                    UnityEngine.Object.DestroyImmediate(dstObject);
                    UnityEngine.Object.DestroyImmediate(srcObject);
                }
                foreach (double delta in new[] { 0.002, 0.00201, 0.04 })
                {
                    var input = new OpportunityForecast {
                        DestinationKey = "pure", IsSharedEdge = true,
                        SourceIsLand = true, DestinationIsLand = true,
                        AbsoluteElevationDifference = 0,
                        SourcePopulation = 100, Migrants = 10,
                        Movement = 15, SourceGrowth = 0,
                        DestinationGrowth = delta,
                        DestinationLongRunGrowth = delta,
                        IncludesExistingCompetitorsAndPredators = true,
                        IncludesMigrantsInTargetResourceSharing = true
                    };
                    OpportunityResult result = NeighborMovementOpportunity.Evaluate(input, 3.0);
                    writer.WriteLine("math-delta-" + delta.ToString("R", CultureInfo.InvariantCulture) +
                        "," + result.Eligible + "," + result.Rejection + "," +
                        result.Eligible + "," + result.Rejection + "," +
                        result.GrowthAdvantage.ToString("R", CultureInfo.InvariantCulture) +
                        "," + result.MutationWeightBonus.ToString("R", CultureInfo.InvariantCulture) +
                        "," + result.AlreadyEligibleForPhysicalMigration + "," +
                        NeighborMovementPolicyV2.NeighborGate(result.MutationWeightBonus)
                            .ToString("R", CultureInfo.InvariantCulture) + "," +
                        NeighborMovementPolicyV2.CanChooseDestination(result) + ",true");
                }
                var original = new PopulationData {
                    speciesAmount = 1000, bodyBiomassInitialized = true,
                    bodyBiomass = 10000f, energyReserve = 500f,
                    birthRemainder = .8f, deathRemainder = .4f,
                    predationRemainder = .2f
                };
                float body = original.bodyBiomass, energy = original.energyReserve;
                float birth = original.birthRemainder, death = original.deathRemainder;
                float predation = original.predationRemainder;
                Type transferType = typeof(SimulationController).Assembly.GetType(
                    "PopulationTransfer", true);
                PopulationData branch = (PopulationData)transferType.GetMethod(
                    "Split", BindingFlags.Public | BindingFlags.Static).Invoke(
                    null, new object[] { original, 100, null, 1, 7, false });
                if (original.speciesAmount + branch.speciesAmount != 1000 ||
                    Math.Abs(original.bodyBiomass + branch.bodyBiomass - body) > .001f ||
                    Math.Abs(original.energyReserve + branch.energyReserve - energy) > .001f ||
                    Math.Abs(original.birthRemainder + branch.birthRemainder - birth) > .0001f ||
                    Math.Abs(original.deathRemainder + branch.deathRemainder - death) > .0001f ||
                    Math.Abs(original.predationRemainder + branch.predationRemainder - predation) > .0001f)
                    throw new InvalidOperationException("Migration split did not conserve people, body, energy and remainders");
                writer.WriteLine("transfer-100,true,None,true,None,0,0,false,0,false,true");
            }
            Debug.Log("NEIGHBOR_OPPORTUNITY_BOUNDARY_COMPLETE " + path);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static void Record(StreamWriter writer, string label,
        SimulationController simulation, BlockInfo source, PopulationData population)
    {
        Type traitsType = typeof(SimulationController).GetNestedType("LandTraits", BindingFlags.NonPublic);
        object traits = Activator.CreateInstance(traitsType);
        traitsType.GetField("t", BindingFlags.Public | BindingFlags.Instance).SetValue(traits, (float)population.fitTemperature);
        traitsType.GetField("h", BindingFlags.Public | BindingFlags.Instance).SetValue(traits, (float)population.fitHumidity);
        traitsType.GetField("m", BindingFlags.Public | BindingFlags.Instance).SetValue(traits, (float)population.movementAbility);
        traitsType.GetField("s", BindingFlags.Public | BindingFlags.Instance).SetValue(traits, (float)population.size);
        traitsType.GetField("f", BindingFlags.Public | BindingFlags.Instance).SetValue(traits, (float)population.fertility);
        string before = simulation.CanonicalModelHash();
        OpportunityResult result = (OpportunityResult)typeof(SimulationController)
            .GetMethod("BestRealNeighborOpportunity", Hidden)
            .Invoke(simulation, new[] { (object)source, population, traits, (object)0f });
        BlockInfo target = source.Neighbors != null && source.Neighbors.Count > 0
            ? source.Neighbors[0] : null;
        int migrants = Math.Min(population.speciesAmount,
            Math.Max(10, SimulationController.StandardMigrationAmount(population.speciesAmount)));
        OpportunityResult direct = (OpportunityResult)typeof(SimulationController)
            .GetMethod("EvaluateRealNeighborOpportunity", Hidden)
            .Invoke(simulation, new object[] { source, target, population, traits,
                migrants, false });
        bool unchanged = before == simulation.CanonicalModelHash();
        if (!unchanged) throw new InvalidOperationException("Counterfactual mutated live ecology: " + label);
        writer.WriteLine(label + "," + result.Eligible + "," + result.Rejection +
            "," + direct.Eligible + "," + direct.Rejection +
            "," + direct.GrowthAdvantage.ToString("R", CultureInfo.InvariantCulture) +
            "," + direct.MutationWeightBonus.ToString("R", CultureInfo.InvariantCulture) +
            "," + direct.AlreadyEligibleForPhysicalMigration + "," +
            NeighborMovementPolicyV2.NeighborGate(direct.MutationWeightBonus)
                .ToString("R", CultureInfo.InvariantCulture) + "," +
            NeighborMovementPolicyV2.CanChooseDestination(direct) + "," + unchanged);
    }
}
#endif
