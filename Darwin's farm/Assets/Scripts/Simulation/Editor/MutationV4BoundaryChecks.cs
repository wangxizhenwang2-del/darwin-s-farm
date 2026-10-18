using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Small deterministic checks for the new admission and recovery boundaries.
public static class MutationV4BoundaryChecks
{
    private const BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;

    public static void RunBatch()
    {
        Type exporter = typeof(PopulationSimulatorWindow);
        Type legacyHeader = exporter.GetNestedType("ExportHeader",
            BindingFlags.NonPublic);
        Type versionedHeader = exporter.GetNestedType("VersionedExportHeader",
            BindingFlags.NonPublic);
        MethodInfo canonicalHeader = exporter.GetMethod("CanonicalReplayRecord",
            BindingFlags.NonPublic | BindingFlags.Static);
        Check(legacyHeader != null && versionedHeader != null &&
            canonicalHeader != null, "Export header entry points missing");
        object newHeader = Activator.CreateInstance(versionedHeader, true);
        versionedHeader.GetField("mutationRulesVersion").SetValue(newHeader,
            "MutationV4-P0P1P2-partial");
        string newHeaderJson = JsonUtility.ToJson(newHeader);
        Check(newHeaderJson.Contains("\"mutationRulesVersion\":\"MutationV4-P0P1P2-partial\"") &&
            ((string)canonicalHeader.Invoke(null,
                new object[] { "header", newHeaderJson })).Contains(
                    "\"mutationRulesVersion\":\"MutationV4-P0P1P2-partial\""),
            "New exported header must carry the model version");
        string oldHeaderJson = JsonUtility.ToJson(
            Activator.CreateInstance(legacyHeader, true));
        Check(!((string)canonicalHeader.Invoke(null,
                new object[] { "header", oldHeaderJson })).Contains(
                    "mutationRulesVersion"),
            "Legacy header canonicalization must remain compatible");
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject tile = new GameObject("V4 boundary tile");
            SceneManager.MoveGameObjectToScene(tile, scene);
            BlockInfo block = tile.AddComponent<BlockInfo>();
            block.waterCoverage = WaterCoverage.Land;
            block.community = new List<PopulationData>();
            block.maxPlantBiomass = 1000000f;
            block.plantBiomass = 100000f;
            block.habitatRecovery = 100000;
            block.temperature = block.humidity = 50;
            GameObject owner = new GameObject("V4 boundary simulation");
            SceneManager.MoveGameObjectToScene(owner, scene);
            SimulationController simulation = owner.AddComponent<SimulationController>();
            simulation.SetBlocks(new List<BlockInfo> { block });
            var first = new PopulationData { speciesAmount = 100,
                trophicLevel = 0, ecologicalNiche = EcologicalNiche.Land,
                fertility = 50, size = 20, fitTemperature = 50,
                fitHumidity = 50 };
            var second = new PopulationData { speciesAmount = 100,
                trophicLevel = 0, ecologicalNiche = EcologicalNiche.Land,
                fertility = 50, size = 20, fitTemperature = 50,
                fitHumidity = 50 };
            block.community.Add(first);
            block.community.Add(second);
            MethodInfo deploy = typeof(SimulationController).GetMethod(
                "RecordActualRecoveryGuidance", PrivateInstance);
            MethodInfo apply = typeof(SimulationController).GetMethod(
                "TryApplyRecoveryGuidance", PrivateInstance);
            MethodInfo cancel = typeof(SimulationController).GetMethod(
                "CancelRecoveryGuidance", PrivateInstance);
            FieldInfo dayField = typeof(SimulationController).GetField(
                "currentSimulationDay", PrivateInstance);
            Check(deploy != null && apply != null && cancel != null &&
                dayField != null, "Recovery entry points missing");
            deploy.Invoke(simulation, new object[] { block, 25000f, 10 });
            RecoveryGuidanceTransaction tx = block.recoveryGuidance;
            Check(tx.firstDay == 11 && tx.lastDay == 185 &&
                tx.cap == 16 && Mathf.Abs(tx.probability - .30f) < .00001f,
                "Low tier boundary");
            string key = tx.key;
            deploy.Invoke(simulation, new object[] { block, 25000f, 20 });
            Check(tx.key == key && tx.lastDay == 185 &&
                tx.cap == 32 && Mathf.Abs(tx.probability - .60f) < .00001f,
                "Same-direction merge must not extend time");
            deploy.Invoke(simulation, new object[] { block, 0f, 21 });
            Check(tx.key == key && tx.lastDay == 185,
                "Zero actual delta must not change transaction");
            dayField.SetValue(simulation, 22);
            tx.cap = 1;
            tx.probability = 1f;
            Check((bool)apply.Invoke(simulation, new object[] { block, first }),
                "First paid mutation");
            Check(!(bool)apply.Invoke(simulation, new object[] { block, second }) &&
                tx.used == 1 && first.fertilityGuidance.used == 1 &&
                second.fertilityGuidance.used == 0,
                "Tile quota shared by populations");
            var newcomer = new PopulationData { speciesAmount = 10,
                trophicLevel = 0, fertility = 50 };
            block.community.Add(newcomer);
            tx.cap = 2;
            Check(!(bool)apply.Invoke(simulation, new object[] { block, newcomer }),
                "Later newcomer must not claim an earlier tile transaction");
            block.community.Remove(newcomer);
            deploy.Invoke(simulation, new object[] { block, -25000f, 23 });
            Check(tx.key != key && tx.firstDay == 24 &&
                tx.lastDay == 198 && tx.used == 0 && tx.cap == 16,
                "Reverse deployment starts new transaction");
            cancel.Invoke(simulation, new object[] { block });
            Check(tx.direction == 0, "Cancellation stops future guidance");
            deploy.Invoke(simulation, new object[] { block, 25000f, 40 });
            tx.probability = 1f;
            dayField.SetValue(simulation, 41);
            first.fertility = 85;
            Check(!(bool)apply.Invoke(simulation, new object[] { block, first }),
                "Upper guidance soft boundary");
            first.fertility = 84;
            Check((bool)apply.Invoke(simulation, new object[] { block, first }) &&
                first.fertility == 85, "Guidance stops exactly at 85");
            first.fertility = 84;
            dayField.SetValue(simulation, 215);
            Check((bool)apply.Invoke(simulation, new object[] { block, first }),
                "Day +175 is included");
            first.fertility = 84;
            dayField.SetValue(simulation, 216);
            Check(!(bool)apply.Invoke(simulation, new object[] { block, first }),
                "Day +176 is excluded");
            string finalDayKey = tx.key;
            deploy.Invoke(simulation, new object[] { block, 1000f, 215 });
            Check(tx.key == finalDayKey && tx.lastDay == 215,
                "Same-direction deployment on final day keeps original deadline");
            deploy.Invoke(simulation, new object[] { block, 1000f, 216 });
            Check(tx.key != finalDayKey && tx.firstDay == 217,
                "Deployment after expiry starts a new transaction");
            var rules = new LandRuleTuning { evolutionCooldownDays = 50 };
            rules.Sanitize();
            Check(rules.evolutionCooldownDays == 100,
                "New formal evolution cooldown");
            Type transfer = typeof(SimulationController).Assembly.GetType(
                "PopulationTransfer", true);
            MethodInfo copyForMove = transfer.GetMethod("CopyForMove",
                BindingFlags.Static | BindingFlags.Public);
            MethodInfo merge = transfer.GetMethod("Merge",
                BindingFlags.Static | BindingFlags.Public);
            var cooling = new PopulationData { speciesAmount = 20,
                nextEvolutionDay = 345 };
            var migrant = (PopulationData)copyForMove.Invoke(null,
                new object[] { cooling, 10 });
            Check(migrant.nextEvolutionDay == 345,
                "Migration must inherit the absolute cooldown date");
            var resident = new PopulationData { speciesAmount = 10,
                nextEvolutionDay = 400 };
            merge.Invoke(null, new object[] { resident, migrant });
            Check(resident.nextEvolutionDay == 400,
                "Same-species merge must preserve the later deadline");
            var l2 = ScriptableObject.CreateInstance<SpeciesData>();
            l2.trophicLevel = 2;
            l2.baseEcologicalNiche = EcologicalNiche.Land;
            l2.baseSize = 20;
            l2.baseFertility = 50;
            MethodInfo plan = typeof(SimulationController).GetMethod(
                "TryPlanFormalEvolution", PrivateInstance);
            MethodInfo admit = typeof(SimulationController).GetMethod(
                "HasSustainableL2FounderSupport", PrivateInstance);
            Check(plan != null && admit != null, "L2 admission entry points missing");
            first.speciesAmount = 94;
            object[] small = { block, first, l2, 0, 0f, 0f };
            Check(!(bool)plan.Invoke(simulation, small),
                "Nine L2 founders must be rejected");
            first.speciesAmount = 95;
            object[] sufficient = { block, first, l2, 0, 0f, 0f };
            Check((bool)plan.Invoke(simulation, sufficient) &&
                (int)sufficient[3] == 10,
                "Ten L2 founders must pass the size gate");
            string beforeAdmission = simulation.CanonicalModelHash();
            object[] unknownPrey = { block, first, l2, 10, null, 0f };
            Check(!(bool)admit.Invoke(simulation, unknownPrey) &&
                (string)unknownPrey[4] == "prey_renewal_unknown" &&
                simulation.CanonicalModelHash() == beforeAdmission,
                "Unknown prey supply rejection must be read-only");
            UnityEngine.Object.DestroyImmediate(l2);
            var parent = ScriptableObject.CreateInstance<SpeciesData>();
            var sibling = ScriptableObject.CreateInstance<SpeciesData>();
            parent.trophicLevel = sibling.trophicLevel = 1;
            parent.baseEcologicalNiche = sibling.baseEcologicalNiche =
                EcologicalNiche.Land;
            parent.evolutionTargets.Add(sibling);
            sibling.baseFitTemperature = sibling.baseFitHumidity = 50;
            sibling.baseMovementAbility = 20;
            sibling.baseSize = 60;
            sibling.baseFertility = 80;
            first.species = parent;
            first.trophicLevel = 1;
            first.trackedEvolutionTarget = sibling;
            first.fitTemperature = first.fitHumidity = 50;
            first.movementAbility = 70;
            MethodInfo siblingDraw = typeof(SimulationController).GetMethod(
                "TrySiblingAttraction", PrivateInstance);
            MethodInfo naturalRecovery = typeof(SimulationController).GetMethod(
                "TryNaturalRecovery", PrivateInstance);
            Check(siblingDraw != null && naturalRecovery != null,
                "P2 attraction entry points missing");
            bool up = false, down = false, movementDown = false;
            for (int day = 1; day <= 1000 && !up; day++)
            {
                dayField.SetValue(simulation, day);
                first.size = 20;
                first.fertility = 40;
                up = (bool)siblingDraw.Invoke(simulation,
                    new object[] { block, first });
                if (up) Check(first.size == 21 && first.fertility == 40 ||
                    first.size == 20 && first.fertility == 41,
                    "Sibling attraction must take one upward slot");
            }
            sibling.baseSize = 10;
            sibling.baseFertility = 20;
            for (int day = 1; day <= 1000 && !down; day++)
            {
                dayField.SetValue(simulation, day);
                first.size = 60;
                first.fertility = 80;
                down = (bool)siblingDraw.Invoke(simulation,
                    new object[] { block, first });
                if (down) Check(first.size == 59 && first.fertility == 80 ||
                    first.size == 60 && first.fertility == 79,
                    "Sibling attraction must also work downward");
            }
            first.trophicLevel = 0;
            parent.trophicLevel = sibling.trophicLevel = 0;
            for (int day = 1; day <= 1000 && !movementDown; day++)
            {
                dayField.SetValue(simulation, day);
                first.movementAbility = 70;
                movementDown = (bool)naturalRecovery.Invoke(simulation,
                    new object[] { block, first });
                if (movementDown)
                    Check(first.movementAbility == 69,
                        "Movement sibling recovery is downward only");
            }
            Check(up && down && movementDown,
                "P2 low-frequency boundary draws did not occur");
            UnityEngine.Object.DestroyImmediate(parent);
            UnityEngine.Object.DestroyImmediate(sibling);
            var l0Source = ScriptableObject.CreateInstance<SpeciesData>();
            var l0Target = ScriptableObject.CreateInstance<SpeciesData>();
            l0Source.baseEcologicalNiche = l0Target.baseEcologicalNiche =
                EcologicalNiche.Land;
            l0Source.trophicLevel = l0Target.trophicLevel = 0;
            l0Source.baseMovementAbility = l0Target.baseMovementAbility = 50;
            l0Source.baseFitTemperature = l0Target.baseFitTemperature = 50;
            l0Source.baseFitHumidity = l0Target.baseFitHumidity = 50;
            l0Source.baseSize = l0Target.baseSize = 10;
            l0Source.baseFertility = l0Target.baseFertility = 50;
            l0Target.evolutionTargets.Add(l0Source);
            GameObject l0Tile = new GameObject("V4 L0 regression tile");
            SceneManager.MoveGameObjectToScene(l0Tile, scene);
            BlockInfo l0Block = l0Tile.AddComponent<BlockInfo>();
            l0Block.waterCoverage = WaterCoverage.Land;
            l0Block.temperature = l0Block.humidity = 50;
            l0Block.maxPlantBiomass = 1000000f;
            l0Block.plantBiomass = 1000000f;
            l0Block.habitatRecovery = 100000;
            var l0Population = new PopulationData { species = l0Source,
                speciesAmount = 500, ecologicalNiche = EcologicalNiche.Land,
                trophicLevel = 0, trophicLevelInitialized = true,
                movementAbility = 50, size = 10, fertility = 50,
                fitTemperature = 50, fitHumidity = 50 };
            l0Block.community = new List<PopulationData> { l0Population };
            GameObject l0Owner = new GameObject("V4 L0 regression simulation");
            SceneManager.MoveGameObjectToScene(l0Owner, scene);
            SimulationController l0Simulation =
                l0Owner.AddComponent<SimulationController>();
            l0Simulation.SetEvolutionNetworkSpecies(new[] { l0Target, l0Source });
            l0Simulation.SetBlocks(new List<BlockInfo> { l0Block });
            MethodInfo eligible = typeof(SimulationController).GetMethod(
                "IsEligibleEvolutionTarget", PrivateInstance);
            object[] l0Plan = { l0Block, l0Population, l0Target, 0, 0f, 0f };
            bool l0PlanPass = (bool)plan.Invoke(l0Simulation, l0Plan);
            MethodInfo food = typeof(SimulationController).GetMethod(
                "HasEvolutionFoodSupport", PrivateInstance);
            bool l0FoodPass = l0PlanPass && (bool)food.Invoke(l0Simulation,
                new object[] { l0Block, l0Population, l0Target,
                    (int)l0Plan[3] });
            Debug.Log("V4 L0 eligibility diagnostic plan=" + l0PlanPass +
                " amount=" + l0Plan[3] + " food=" + l0FoodPass);
            Check(eligible != null && (bool)eligible.Invoke(l0Simulation,
                new object[] { l0Block, l0Population, l0Target, 1 }),
                "L0 formal evolution eligibility regression");
            UnityEngine.Object.DestroyImmediate(l0Source);
            UnityEngine.Object.DestroyImmediate(l0Target);
            Debug.Log("Mutation V4 boundary checks PASS");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("Mutation V4: " + message); }
}
