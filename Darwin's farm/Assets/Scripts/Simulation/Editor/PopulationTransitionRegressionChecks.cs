#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Deterministic settlement and event checks; fixtures live in a preview scene.
public static class PopulationTransitionRegressionChecks
{
    [MenuItem("Tools/Population/Run ecological event regression checks")]
    private static void RunFromMenu() => Debug.Log(RunChecks());

    public static void RunBatch()
    {
        try
        {
            Debug.Log(RunChecks());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    public static string RunChecks()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        SpeciesData ancestorSpecies = null;
        SpeciesData evolvedSpecies = null;
        try
        {
            BlockInfo source = Block(scene, "Source");
            BlockInfo target = Block(scene, "Target");
            GameObject controllerObject = new GameObject("Simulation");
            SceneManager.MoveGameObjectToScene(controllerObject, scene);
            SimulationController simulation = controllerObject.AddComponent<SimulationController>();
            simulation.SetBlocks(new List<BlockInfo> { source, target });
            PopulationTransitionResult? migration = null;
            PopulationTransitionResult? niche = null;
            PopulationTransitionResult? evolution = null;
            int legacyMigration = 0, legacyNiche = 0, legacyEvolution = 0;
            simulation.OnMigrationResolved += result => migration = result;
            simulation.OnNicheConversionResolved += result => niche = result;
            simulation.OnPresetEvolutionResolved += result => evolution = result;
            simulation.OnPopulationMigrated += (_, _, _, _) => legacyMigration++;
            simulation.OnNicheConverted += (_, _, _, _, _) => legacyNiche++;
            simulation.OnSpeciesEvolved += (_, _, _, _, _) => legacyEvolution++;

            PopulationData whole = Draft("Whole", 80);
            source.community.Add(whole);
            string wholeId = whole.PopulationId;
            simulation.MovePopulation(source, target, whole, 4);
            Require(migration.HasValue && migration.Value.Kind == PopulationTransitionKind.Migration &&
                migration.Value.Outcome == PopulationArrivalOutcome.Created &&
                migration.Value.Day == 4 && migration.Value.SourceBlock == source &&
                migration.Value.TargetBlock == target && migration.Value.Amount == 80 &&
                migration.Value.SourcePopulationId == wholeId &&
                migration.Value.ResultPopulationId == wholeId &&
                migration.Value.SourceCountBefore == 80 &&
                migration.Value.SourceCountAfter == 0 &&
                migration.Value.TargetCountBefore == 0 &&
                migration.Value.TargetCountAfter == 80 &&
                source.community.Count == 0 && target.community.Count == 1,
                "Whole migration to an empty tile did not publish a created result snapshot.");
            PopulationTransitionResult wholeSnapshot = migration.Value;

            migration = null;
            source.community.Add(Draft("Partial", 150));
            target.community.Clear();
            PopulationData partial = source.community[0];
            simulation.MovePopulation(source, target, partial, 5);
            Require(migration.HasValue && migration.Value.Outcome == PopulationArrivalOutcome.Created &&
                migration.Value.Amount == 100 && migration.Value.SourceCountBefore == 150 &&
                migration.Value.SourceCountAfter == 50 && migration.Value.TargetCountBefore == 0 &&
                migration.Value.TargetCountAfter == 100 &&
                migration.Value.SourcePopulationId != migration.Value.ResultPopulationId,
                "Partial migration did not publish the new branch and before/after counts.");

            migration = null;
            source.community.Clear();
            target.community.Clear();
            PopulationData merging = Draft("Merge", 80);
            PopulationData resident = Draft("Merge", 20);
            string residentId = resident.PopulationId;
            source.community.Add(merging);
            target.community.Add(resident);
            simulation.MovePopulation(source, target, merging, 6);
            Require(migration.HasValue && migration.Value.Outcome == PopulationArrivalOutcome.Merged &&
                migration.Value.SourcePopulationId == merging.PopulationId &&
                migration.Value.ResultPopulationId == residentId &&
                migration.Value.SourceCountBefore == 80 && migration.Value.SourceCountAfter == 0 &&
                migration.Value.TargetCountBefore == 20 && migration.Value.TargetCountAfter == 100 &&
                target.community.Count == 1 && target.community[0] == resident &&
                legacyMigration == 3 && wholeSnapshot.TargetCountAfter == 80,
                "Migration did not merge into the existing same-species resident.");

            migration = null;
            simulation.MovePopulation(source, source, merging, 7);
            Require(!migration.HasValue && legacyMigration == 3,
                "An invalid migration emitted a transition event.");

            source.community.Clear();
            target.community.Clear();
            PopulationData converting = Draft("Niche", 200);
            source.community.Add(converting);
            MethodInfo convertNiche = typeof(SimulationController).GetMethod("ConvertNiche",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(convertNiche != null, "Niche conversion entry was not found.");
            object[] nicheArguments = { source, target, converting, EcologicalNiche.Water, 8, null };
            bool converted = (bool)convertNiche.Invoke(simulation, nicheArguments);
            Require(converted && niche.HasValue &&
                niche.Value.Kind == PopulationTransitionKind.NicheConversion &&
                niche.Value.Outcome == PopulationArrivalOutcome.Created &&
                niche.Value.Day == 8 && niche.Value.Amount == 20 &&
                niche.Value.SourceCountBefore == 200 && niche.Value.SourceCountAfter == 180 &&
                niche.Value.TargetCountBefore == 0 && niche.Value.TargetCountAfter == 20 &&
                niche.Value.SourcePopulationId != niche.Value.ResultPopulationId &&
                niche.Value.ResultNiche == EcologicalNiche.Water &&
                !string.IsNullOrEmpty(niche.Value.ResultSpeciesId) && legacyNiche == 1,
                "Niche conversion did not publish the new species and population snapshot.");

            ancestorSpecies = Species("Ancestor");
            evolvedSpecies = Species("Evolved");
            MethodInfo evolve = typeof(SimulationController).GetMethod("ConvertToPresetSpecies",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(evolve != null, "Preset evolution entry was not found.");
            source.community.Clear();
            PopulationData ancestor = Draft("Ancestor", 200);
            ancestor.species = ancestorSpecies;
            source.community.Add(ancestor);
            evolve.Invoke(simulation, new object[] { source, ancestor, evolvedSpecies, 9 });
            Require(evolution.HasValue && evolution.Value.Kind == PopulationTransitionKind.PresetEvolution &&
                evolution.Value.Outcome == PopulationArrivalOutcome.Created &&
                evolution.Value.SourceBlock == source && evolution.Value.TargetBlock == source &&
                evolution.Value.Day == 9 && evolution.Value.Amount == 20 &&
                evolution.Value.SourceCountBefore == 200 && evolution.Value.SourceCountAfter == 180 &&
                evolution.Value.TargetCountBefore == 0 && evolution.Value.TargetCountAfter == 20 &&
                evolution.Value.SourcePopulationId != evolution.Value.ResultPopulationId &&
                evolution.Value.TargetSpecies == evolvedSpecies && legacyEvolution == 1,
                "Preset evolution did not publish a created target population snapshot.");

            evolution = null;
            source.community.Clear();
            ancestor = Draft("Ancestor", 200);
            ancestor.species = ancestorSpecies;
            resident = Draft("Evolved", 30);
            resident.species = evolvedSpecies;
            residentId = resident.PopulationId;
            source.community.Add(ancestor);
            source.community.Add(resident);
            evolve.Invoke(simulation, new object[] { source, ancestor, evolvedSpecies, 10 });
            Require(evolution.HasValue && evolution.Value.Outcome == PopulationArrivalOutcome.Merged &&
                evolution.Value.ResultPopulationId == residentId &&
                evolution.Value.TargetCountBefore == 30 && evolution.Value.TargetCountAfter == 50 &&
                source.community.Count == 2 && resident.speciesAmount == 50 &&
                legacyEvolution == 2,
                "Preset evolution did not report merging into its existing target species.");

            MethodInfo hasLivingSpecies = typeof(SimulationController).GetMethod(
                "HasLivingSpecies", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo livingPopulationCount = typeof(SimulationController).GetMethod(
                "LivingPopulationCount", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo eligibleEvolution = typeof(SimulationController).GetMethod(
                "IsEligibleEvolutionTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo matchingTraits = typeof(SimulationController).GetMethod(
                "EachTraitWithinTolerance", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo evolutionFood = typeof(SimulationController).GetMethod(
                "HasEvolutionFoodSupport", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(hasLivingSpecies != null && livingPopulationCount != null &&
                    eligibleEvolution != null && matchingTraits != null && evolutionFood != null,
                "Living-population evolution gates were not found.");
            PopulationData extinctTarget = Draft("Extinct evolved", 0);
            extinctTarget.species = evolvedSpecies;
            PopulationData eligibleAncestor = Draft("Eligible ancestor", 200);
            eligibleAncestor.species = ancestorSpecies;
            eligibleAncestor.trophicLevel = 0;
            eligibleAncestor.nextEvolutionDay = 0;
            evolvedSpecies.trophicLevel = 0;
            source.temperature = 50;
            source.humidity = 50;
            source.maxPlantBiomass = source.plantBiomass = 1000000f;
            source.habitatRecovery = 1000000;
            source.community = new List<PopulationData> { eligibleAncestor, extinctTarget };
            Require(!(bool)hasLivingSpecies.Invoke(null,
                    new object[] { source, evolvedSpecies }),
                "An extinct target population still blocked evolution.");
            Require((bool)matchingTraits.Invoke(simulation,
                    new object[] { eligibleAncestor, evolvedSpecies }),
                "The extinct-target fixture did not start with five matching traits.");
            Require((bool)evolutionFood.Invoke(simulation,
                    new object[] { source, eligibleAncestor, evolvedSpecies, 20, false }),
                "The extinct-target fixture did not provide enough renewable plant food.");
            Require((bool)eligibleEvolution.Invoke(simulation,
                    new object[] { source, eligibleAncestor, evolvedSpecies, 20 }),
                "Five matching traits were still blocked by an extinct target record.");
            extinctTarget.speciesAmount = 1;
            Require((bool)hasLivingSpecies.Invoke(null,
                    new object[] { source, evolvedSpecies }),
                "A living target population did not block duplicate evolution.");
            extinctTarget.speciesAmount = 0;
            Require((int)livingPopulationCount.Invoke(null, new object[] { source }) == 1,
                "Extinct population records still occupied a live community slot.");

            MethodInfo selectSplitTarget = typeof(SimulationController).GetMethod(
                "SelectLocalSplitTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(selectSplitTarget != null,
                "Directed local split target selection was not found.");
            ancestorSpecies.trophicLevel = 0;
            ancestorSpecies.evolutionTargets.Add(evolvedSpecies);
            source.community = new List<PopulationData> { eligibleAncestor };
            object[] targetArguments = { source, eligibleAncestor, 10, 0f, 0f };
            Require((SpeciesData)selectSplitTarget.Invoke(simulation,
                    targetArguments) == evolvedSpecies,
                "A sustainable adjacent land species was not selected for directed splitting.");

            MethodInfo runMerges = typeof(SimulationController).GetMethod(
                "RunSameSpeciesMerges", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(runMerges != null, "Same-species merge entry point was not found.");
            LandRuleTuning mergeRules = simulation.ReadLandRules();
            Require(Mathf.Approximately(mergeRules.splitMinimumRenewalPressure, 0.9f) &&
                    mergeRules.sameSpeciesMergeDays == 100 &&
                    mergeRules.postMergeSplitCooldownDays == 200,
                "Directed-split or same-species merge defaults did not match the design.");
            mergeRules.sameSpeciesMergeDays = 2;
            mergeRules.postMergeSplitCooldownDays = 7;
            simulation.ApplyLandRules(mergeRules);
            PopulationData majority = Draft("Merge timer", 80);
            majority.species = ancestorSpecies;
            majority.fitTemperature = 20;
            PopulationData minority = Draft("Merge timer", 20);
            minority.species = ancestorSpecies;
            minority.fitTemperature = 80;
            source.community = new List<PopulationData> { majority, minority };
            runMerges.Invoke(simulation, new object[] { 30 });
            Require(source.community.Count == 2 &&
                    majority.sameSpeciesCoexistenceDays == 1 &&
                    minority.sameSpeciesCoexistenceDays == 1,
                "Same-species populations did not wait for the configured duration.");
            runMerges.Invoke(simulation, new object[] { 31 });
            Require(source.community.Count == 1 &&
                    source.community[0].speciesAmount == 100 &&
                    source.community[0].fitTemperature == 32 &&
                    source.nextLocalSplitDay == 38,
                "Timed merge did not weight traits or apply the configured split cooldown.");

            return "Ecological event regression checks: 15/15 passed.";
        }
        finally
        {
            if (ancestorSpecies != null) UnityEngine.Object.DestroyImmediate(ancestorSpecies);
            if (evolvedSpecies != null) UnityEngine.Object.DestroyImmediate(evolvedSpecies);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static BlockInfo Block(Scene scene, string name)
    {
        GameObject root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        BlockInfo block = root.AddComponent<BlockInfo>();
        block.waterCoverage = WaterCoverage.Land;
        block.community = new List<PopulationData>();
        return block;
    }

    private static PopulationData Draft(string name, int amount) => new PopulationData
    {
        lineageName = name,
        speciesAmount = amount,
        ecologicalNiche = EcologicalNiche.Land,
        size = 50,
        fitTemperature = 50,
        fitHumidity = 50,
        movementAbility = 50,
        fertility = 50,
        habitatNiche = 50,
        trophicLevel = 1,
        trophicLevelInitialized = true
    };

    private static SpeciesData Species(string name)
    {
        SpeciesData species = ScriptableObject.CreateInstance<SpeciesData>();
        species.name = name;
        typeof(SpeciesData).GetField("speciesName", BindingFlags.Instance |
            BindingFlags.NonPublic).SetValue(species, name);
        species.baseEcologicalNiche = EcologicalNiche.Land;
        species.baseSize = 50;
        species.baseFitTemperature = 50;
        species.baseFitHumidity = 50;
        species.baseMovementAbility = 50;
        species.baseFertility = 50;
        species.trophicLevel = 1;
        return species;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
