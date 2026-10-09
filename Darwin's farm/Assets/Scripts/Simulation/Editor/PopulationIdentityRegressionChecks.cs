#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Scene-independent checks for model identity across editing and transfer.
public static class PopulationIdentityRegressionChecks
{
    [MenuItem("Tools/Population/Run identity regression checks")]
    private static void RunFromMenu() => Debug.Log(RunChecks());

    public static string RunChecks()
    {
        int passed = 0;
        PopulationData first = Draft("A", 100);
        PopulationData second = Draft("B", 40);
        Require(first.PopulationId != second.PopulationId,
            "Different population instances share an ID.");
        passed++;
        PopulationData restored = JsonUtility.FromJson<PopulationData>(JsonUtility.ToJson(first));
        Require(restored.PopulationId == first.PopulationId,
            "Serializing and loading a resident changed its ID.");
        passed++;

        Type transfer = typeof(SimulationController).Assembly.GetType("PopulationTransfer");
        Require(transfer != null, "PopulationTransfer was not found.");
        MethodInfo split = transfer.GetMethod("Split", BindingFlags.Static | BindingFlags.Public);
        MethodInfo merge = transfer.GetMethod("Merge", BindingFlags.Static | BindingFlags.Public);
        Require(split != null && merge != null, "Population transfer methods were not found.");

        string firstId = first.PopulationId;
        PopulationData branch = (PopulationData)split.Invoke(null,
            new object[] { first, 30, null, 1, 7, true });
        Require(first.PopulationId == firstId && branch.PopulationId != firstId &&
            first.speciesAmount == 70 && branch.speciesAmount == 30,
            "Partial migration did not preserve the resident and create a new branch ID.");
        passed++;

        string secondId = second.PopulationId;
        PopulationData relocated = (PopulationData)split.Invoke(null,
            new object[] { second, 40, null, 1, 7, true });
        Require(second.speciesAmount == 0 && relocated.PopulationId == secondId,
            "Whole migration changed the surviving population ID.");
        passed++;

        string residentId = first.PopulationId;
        merge.Invoke(null, new object[] { first, branch });
        Require(first.PopulationId == residentId && first.speciesAmount == 100,
            "Merging changed the resident ID or population count.");
        passed++;

        GameObject fixture = new GameObject("Population identity regression fixture");
        GameObject targetFixture = new GameObject("Population identity target fixture");
        try
        {
            BlockInfo block = fixture.AddComponent<BlockInfo>();
            BlockInfo target = targetFixture.AddComponent<BlockInfo>();
            SimulationController simulation = fixture.AddComponent<SimulationController>();
            PopulationData duplicate = Draft("B", 20);
            FieldInfo idField = typeof(PopulationData).GetField("populationId",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(idField != null, "Serialized population ID field was not found.");
            idField.SetValue(duplicate, first.PopulationId);
            block.community = new List<PopulationData> { first, duplicate };
            simulation.SetBlocks(new List<BlockInfo> { block });
            Require(first.PopulationId == residentId &&
                duplicate.PopulationId != first.PopulationId,
                "Registering older/duplicated model data did not repair ID uniqueness.");
            passed++;

            string duplicateId = duplicate.PopulationId;
            var edits = new List<SimulationPopulationEdit>
            {
                new SimulationPopulationEdit { Resident = first, Draft = first },
                new SimulationPopulationEdit { Resident = duplicate, Draft = duplicate },
                new SimulationPopulationEdit { Draft = Draft("C", 5) }
            };
            Require(simulation.ApplyCommunityEdits(block, edits) &&
                first.PopulationId == residentId && duplicate.PopulationId == duplicateId &&
                block.community.Count == 3 &&
                block.community[2].PopulationId != first.PopulationId &&
                block.community[2].PopulationId != duplicateId,
                "Community editing changed resident IDs or reused one for a new population.");
            passed++;

            PopulationData whole = Draft("Whole", 80);
            string wholeId = whole.PopulationId;
            block.community = new List<PopulationData> { whole };
            target.community = new List<PopulationData>();
            simulation.SetBlocks(new List<BlockInfo> { block, target });
            simulation.MovePopulation(block, target, whole, 1);
            Require(block.community.Count == 0 && target.community.Count == 1 &&
                target.community[0].PopulationId == wholeId,
                "Actual whole-population migration did not retain its ID.");
            passed++;

            PopulationData partial = Draft("Partial", 150);
            string partialId = partial.PopulationId;
            block.community = new List<PopulationData> { partial };
            target.community.Clear();
            simulation.SetBlocks(new List<BlockInfo> { block, target });
            simulation.MovePopulation(block, target, partial, 1);
            Require(block.community.Count == 1 && partial.speciesAmount == 50 &&
                partial.PopulationId == partialId && target.community.Count == 1 &&
                target.community[0].speciesAmount == 100 &&
                target.community[0].PopulationId != partialId,
                "Actual partial migration reused the source ID for its new branch.");
            passed++;

            PopulationData merging = Draft("Merge", 80);
            PopulationData destination = Draft("Merge", 20);
            string destinationId = destination.PopulationId;
            block.community = new List<PopulationData> { merging };
            target.community = new List<PopulationData> { destination };
            simulation.SetBlocks(new List<BlockInfo> { block, target });
            simulation.MovePopulation(block, target, merging, 1);
            Require(block.community.Count == 0 && target.community.Count == 1 &&
                target.community[0] == destination && destination.speciesAmount == 100 &&
                destination.PopulationId == destinationId,
                "Actual migration merge changed the destination resident ID.");
            passed++;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(targetFixture);
            UnityEngine.Object.DestroyImmediate(fixture);
        }

        return $"Population identity regression checks: {passed}/10 passed.";
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
        trophicLevel = 1
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
