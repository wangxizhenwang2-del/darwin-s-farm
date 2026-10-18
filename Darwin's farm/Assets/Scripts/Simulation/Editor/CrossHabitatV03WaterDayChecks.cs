#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// A real SimulateDay fixture with the project's imported aquatic food graph.
public static class CrossHabitatV03WaterDayChecks
{
    public static void RunBatch()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        bool failed = false;
        try
        {
            BlockInfo w1 = Cell(scene, "w1", 1, 0, 220000f);
            BlockInfo w2 = Cell(scene, "w2", 2, 0, 80000f);
            BlockInfo w3 = Cell(scene, "w3", 1, 1, 140000f);
            Link(w1, w2); Link(w1, w3);
            SpeciesData swimmer = AssetDatabase.LoadAssetAtPath<SpeciesData>(
                "Assets/BiowebV46/Species/鹿游-103a527e.asset");
            SpeciesData shrimp = AssetDatabase.LoadAssetAtPath<SpeciesData>(
                "Assets/BiowebV46/Species/虾兔-c60ed314.asset");
            SpeciesData shark = AssetDatabase.LoadAssetAtPath<SpeciesData>(
                "Assets/BiowebV46/Species/鲨母-6b5c947b.asset");
            Require(swimmer != null && shrimp != null && shark != null,
                "Aquatic assets missing");
            Require(shark.preySpecies.Contains(shrimp) &&
                !shark.preySpecies.Contains(swimmer), "Real food graph differs");
            PopulationData a = Population(swimmer, 100);
            PopulationData b = Population(shrimp, 500);
            PopulationData hunter = Population(shark, 20);
            w1.community.Add(a); w3.community.Add(b); w2.community.Add(hunter);
            GameObject root = new GameObject("CrossHabitatV03 water day");
            SceneManager.MoveGameObjectToScene(root, scene);
            SimulationController simulation = root.AddComponent<SimulationController>();
            simulation.SetBlocks(new List<BlockInfo> { w1, w2, w3 });
            simulation.SetWaterEcologyEnabled(true);
            simulation.SetPresetEvolutionParameters(false, 5, .1f, 10);
            simulation.SetMigrationParameters(false, 7, 100f, .8f, .5f, .2f, 7);
            float before = w1.plantBiomass + w2.plantBiomass + w3.plantBiomass;
            for (int day = 1; day <= 3; day++)
            {
                if (day == 2 || day == 3)
                {
                    float stockAtChange = w1.plantBiomass + w2.plantBiomass +
                        w3.plantBiomass;
                    if (day == 2)
                    {
                        w1.SetWaterLinks(new List<BlockInfo> { w2 });
                        w3.SetWaterLinks(new List<BlockInfo>());
                    }
                    else LinkWater(w1, w3);
                    WaterRegionMap map = simulation.BuildWaterRegions();
                    Require((map.GetRegion(w1) == map.GetRegion(w3)) ==
                        (day == 3), "Water route split/reconnect failed");
                    Require(Mathf.Abs(stockAtChange - (w1.plantBiomass +
                        w2.plantBiomass + w3.plantBiomass)) < .1f,
                        "Topology edit changed physical stock");
                    Debug.Log("CROSS_HABITAT_TOPOLOGY day=" + day +
                        " operation=" + (day == 2 ? "split" : "reconnect") +
                        " stock=" + stockAtChange + " w3HasShrimp=" +
                        w3.community.Contains(b));
                }
                foreach (BlockInfo cell in new[] { w1, w2, w3 })
                {
                    cell.plantGrowthToday = Mathf.Min(cell.habitatRecovery,
                        Mathf.Max(0f, cell.maxPlantBiomass - cell.plantBiomass));
                    cell.plantBiomass += cell.plantGrowthToday;
                }
                float grown = w1.plantGrowthToday + w2.plantGrowthToday +
                    w3.plantGrowthToday;
                float start = w1.plantBiomass + w2.plantBiomass + w3.plantBiomass;
                simulation.SimulateDay(day);
                float consumed = w1.consumedBiomassToday +
                    w2.consumedBiomassToday + w3.consumedBiomassToday;
                float end = w1.plantBiomass + w2.plantBiomass +
                    w3.plantBiomass;
                Require(Mathf.Abs(start - consumed - end) < 1f,
                    "Basin physical stock did not settle exactly once");
                Require(w1.community.Contains(a) && w3.community.Contains(b) &&
                    w2.community.Contains(hunter),
                    "Population moved without a migration event");
                Require(a.allocatedBiomass + b.allocatedBiomass <= consumed + 1f,
                    "Consumers received duplicate basin food");
                Require(Math.Abs(simulation.LastEcologyLedger.massResidual) < 2f,
                    "World material residual exceeds float tolerance");
                Debug.Log("CROSS_HABITAT_WATER_DAY seed=20261018 day=" + day +
                    " cells=w1+w2+w3 stockBefore=" + start +
                    " growth=" + grown + " consumed=" + consumed +
                    " stockAfter=" + end + " N=" + a.speciesAmount + "/" +
                    b.speciesAmount + "/" + hunter.speciesAmount +
                    " huntDeaths=" + b.predationDeathsToday +
                    " body=" + a.bodyBiomass + "/" + b.bodyBiomass +
                    "/" + hunter.bodyBiomass + " reserve=" + a.energyReserve +
                    "/" + b.energyReserve + "/" + hunter.energyReserve +
                    " residual=" + simulation.LastEcologyLedger.massResidual +
                    " modelHash=" + simulation.LastEcologyLedger.modelHash);
            }
            Require(before > 0f, "invalid fixture");
            Debug.Log("CROSS_HABITAT_WATER_DAY_PASS seed=20261018 days=3");
            foreach (BlockInfo cell in new[] { w1, w2, w3 })
            {
                cell.habitatRecovery = 0;
                cell.plantGrowthToday = 0f;
            }
            float stored = w1.plantBiomass + w2.plantBiomass + w3.plantBiomass;
            simulation.SimulateDay(4);
            float spent = w1.consumedBiomassToday + w2.consumedBiomassToday +
                w3.consumedBiomassToday;
            Require(spent > 0f && Mathf.Abs(stored - spent -
                (w1.plantBiomass + w2.plantBiomass + w3.plantBiomass)) < 1f,
                "R=0 must spend old stock exactly once");
            foreach (BlockInfo cell in new[] { w1, w2, w3 })
                cell.plantBiomass = 0f;
            int populationBeforeStarvation = a.speciesAmount + b.speciesAmount;
            for (int day = 5; day <= 14; day++)
            {
                simulation.SimulateDay(day);
                Require(w1.consumedBiomassToday + w2.consumedBiomassToday +
                    w3.consumedBiomassToday <= .001f,
                    "S=R=0 must not create plant food");
            }
            Require(a.speciesAmount + b.speciesAmount < populationBeforeStarvation,
                "S=R=0 must cause herbivore decline");
            Debug.Log("CROSS_HABITAT_ZERO_RESOURCE_PASS days=4..14" +
                " spentOldStock=" + spent + " herbivores=" +
                populationBeforeStarvation + "->" +
                (a.speciesAmount + b.speciesAmount));
        }
        catch (Exception error)
        {
            failed = true;
            Debug.LogException(error);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
        else if (failed) throw new Exception("Water day checks failed");
    }

    private static PopulationData Population(SpeciesData species, int count)
    {
        return new PopulationData
        {
            species = species, ecologicalNiche = EcologicalNiche.Water,
            trophicLevel = species.trophicLevel, speciesAmount = count,
            trophicLevelInitialized = true,
            habitatNiche = species.baseHabitatNiche,
            fitTemperature = species.baseFitTemperature,
            fitHumidity = species.baseFitHumidity,
            movementAbility = species.baseMovementAbility,
            size = species.baseSize, fertility = species.baseFertility
        };
    }

    private static BlockInfo Cell(Scene scene, string name, int x, int y,
        float stock)
    {
        GameObject root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        MapTileInstance tile = root.AddComponent<MapTileInstance>();
        SerializedObject serialized = new SerializedObject(tile);
        serialized.FindProperty("coordinate").vector2IntValue =
            new Vector2Int(x, y);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        BlockInfo block = root.AddComponent<BlockInfo>();
        block.waterCoverage = WaterCoverage.Lake;
        block.elevation = 0;
        block.temperature = block.humidity = 50;
        block.plantBiomass = stock;
        block.maxPlantBiomass = 1500000f;
        block.habitatRecovery = 16000;
        block.community = new List<PopulationData>();
        block.SetNeighbors(new List<BlockInfo>());
        block.SetWaterLinks(new List<BlockInfo>());
        return block;
    }

    private static void Link(BlockInfo left, BlockInfo right)
    {
        left.SetNeighbors(new List<BlockInfo>(left.Neighbors) { right });
        right.SetNeighbors(new List<BlockInfo>(right.Neighbors) { left });
        left.SetWaterLinks(new List<BlockInfo>(left.WaterLinks) { right });
        right.SetWaterLinks(new List<BlockInfo>(right.WaterLinks) { left });
    }

    private static void LinkWater(BlockInfo left, BlockInfo right)
    {
        left.SetWaterLinks(new List<BlockInfo>(left.WaterLinks) { right });
        right.SetWaterLinks(new List<BlockInfo>(right.WaterLinks) { left });
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
#endif
