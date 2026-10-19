#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CrossHabitatV04SmokeChecks
{
    [MenuItem("Tools/Darwin Farm/Validation/Cross Habitat/Water Migration Short Smoke")]
    public static void RunWaterMigrationBatch()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        bool failed = false;
        try
        {
            SpeciesData swimmer = Load("鹿游-103a527e");
            Check(swimmer != null, "water migration asset");
            BlockInfo high = Cell(scene, "highWater", 0, 0, true);
            BlockInfo low = Cell(scene, "lowWater", 1, 0, true);
            high.elevation = 1;
            low.elevation = 0;
            high.plantBiomass = 1f;
            high.habitatRecovery = 0;
            low.plantBiomass = 500000f;
            low.habitatRecovery = 100000;
            Link(high, low);
            high.SetWaterLinks(new List<BlockInfo> {low});
            low.SetWaterLinks(new List<BlockInfo> {high});
            PopulationData parent = Population(swimmer, swimmer, 1000);
            high.community.Add(parent);
            GameObject root = new GameObject("CrossHabitatV04 water migration");
            SceneManager.MoveGameObjectToScene(root, scene);
            SimulationController simulation = root.AddComponent<SimulationController>();
            simulation.SetBlocks(new List<BlockInfo> {high, low});
            simulation.ConfigureDeterministicRun(20261018);
            simulation.SetWaterEcologyEnabled(true);
            simulation.SetMutationEnabled(false);
            simulation.SetPresetEvolutionParameters(false, 5, .1f, 10);
            simulation.SetMigrationParameters(true, 7, 100f, .8f, .5f, .2f, 0);
            simulation.SetEcologicalNicheProbabilities(0f, 0f, 1f, 1f, 0f);
            int start = parent.speciesAmount;
            for (int day = 1; day <= 3 && Count(low, swimmer) == 0; day++)
            {
                high.plantGrowthToday = 0f;
                low.plantGrowthToday = Mathf.Min(low.habitatRecovery,
                    low.maxPlantBiomass - low.plantBiomass);
                low.plantBiomass += low.plantGrowthToday;
                simulation.SimulateDay(day);
            }
            Check(Count(low, swimmer) > 0, "cross basin split missing");
            Check(parent.speciesAmount > 0, "source group must remain");
            Debug.Log("WATER_MIGRATION_V04_SMOKE_PASS start=" + start +
                " source=" + parent.speciesAmount + " target=" +
                Count(low, swimmer));
        }
        catch (Exception error)
        { failed = true; Debug.LogException(error); }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
        else if (failed) throw new Exception("Water migration smoke failed");
    }

    [MenuItem("Tools/Darwin Farm/Validation/Cross Habitat/Flight Short Smoke")]
    public static void RunFlightBatch()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        bool failed = false;
        try
        {
            SpeciesData deer = Load("鹿-fd1c574b");
            SpeciesData flyer = Load("鹿飞-a61486cd");
            SpeciesData chicken = Load("彩鸡-b59a4a82");
            SpeciesData plainChicken = Load("鸡-7f113739");
            SpeciesData peacock = Load("孔雀-f0686f45");
            Check(deer != null && flyer != null && chicken != null &&
                plainChicken != null && peacock != null, "flight assets");
            BlockInfo a = Cell(scene, "flightA", 0, 0, false);
            BlockInfo b = Cell(scene, "flightB", 1, 0, false);
            foreach (BlockInfo cell in new[] {a, b})
            { cell.plantBiomass = 3000000f;
              cell.maxPlantBiomass = 5000000f;
              cell.habitatRecovery = 1000000; }
            Link(a, b);
            PopulationData landParent = Population(deer, flyer, 200);
            PopulationData airParent = Population(flyer, flyer, 100);
            airParent.manualEvolutionTarget = deer;
            a.community.Add(landParent);
            a.community.Add(Population(chicken, chicken, 5000));
            a.community.Add(Population(plainChicken, plainChicken, 100));
            b.community.Add(airParent);
            b.community.Add(Population(chicken, chicken, 5000));
            b.community.Add(Population(plainChicken, plainChicken, 100));
            b.community.Add(Population(peacock, peacock, 100));
            GameObject root = new GameObject("CrossHabitatV04 flight smoke");
            SceneManager.MoveGameObjectToScene(root, scene);
            SimulationController simulation = root.AddComponent<SimulationController>();
            simulation.SetBlocks(new List<BlockInfo> {a, b});
            simulation.ConfigureDeterministicRun(20261018);
            simulation.SetAirEcologyEnabled(true);
            simulation.SetWaterEcologyEnabled(false);
            simulation.SetMutationEnabled(false);
            simulation.SetPresetEvolutionParameters(false, 5, .1f, 10);
            simulation.SetMigrationParameters(false, 7, 100f, .8f, .5f, .2f, 0);
            simulation.SetCrossHabitatDebugProbabilities(1f, 1f, 0f);
            simulation.SetFlightCandidateParameters(.1f, 50, 1f);
            simulation.SimulateDay(1);
            int createdAir = Count(a, flyer);
            int createdLand = Count(b, deer);
            int legalHuntDeaths = 0;
            foreach (PopulationData prey in b.community)
                if (prey != null && prey.species != null &&
                    flyer.preySpecies.Contains(prey.species))
                    legalHuntDeaths += prey.predationDeathsToday;
            Debug.Log("FLIGHT_V04_DAY1 air=" + createdAir +
                " reverseLand=" + createdLand + " airParent=" +
                airParent.speciesAmount + " landParent=" +
                landParent.speciesAmount + " residual=" +
                simulation.LastEcologyLedger.massResidual +
                " newAirNextMove=" + a.community.Find(p =>
                    p != null && p.species == flyer).nextMigrationDay +
                " newAirLastSource=" +
                (a.community.Find(p => p != null && p.species == flyer)
                    .lastMigrationSource == null ? "none" :
                    a.community.Find(p => p != null && p.species == flyer)
                        .lastMigrationSource.name));
            Check(createdAir == 50 && landParent.speciesAmount > 0,
                "land->air fixed 50");
            Check(createdLand > 0 && airParent.speciesAmount > 0,
                "air->land true reverse species");
            Check(legalHuntDeaths > 0, "air must actually hunt legal ground prey");
            b.community.Remove(airParent);
            simulation.SetCrossHabitatDebugProbabilities(0f, 0f, 0f);
            simulation.SetMigrationParameters(true, 7, 100f, .8f, .5f, .2f, 0);
            bool moved = false;
            for (int day = 2; day <= 15; day++)
            {
                simulation.SimulateDay(day);
                if (Count(b, flyer) <= 0) continue;
                moved = true;
                Debug.Log("FLIGHT_V04_MOVE day=" + day +
                    " sourceAir=" + Count(a, flyer) +
                    " targetAir=" + Count(b, flyer));
                break;
            }
            Check(moved, "flight migration opportunity did not trigger");
            Debug.Log("FLIGHT_V04_SMOKE_PASS");
        }
        catch (Exception error)
        { failed = true; Debug.LogException(error); }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
        else if (failed) throw new Exception("Flight smoke failed");
    }

    private static SpeciesData Load(string stem) =>
        AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/BiowebV46/Species/" + stem + ".asset");

    [MenuItem("Tools/Darwin Farm/Validation/Cross Habitat/Water Land Short Smoke")]
    public static void RunBatch()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        bool failed = false;
        try
        {
            SpeciesData deer = AssetDatabase.LoadAssetAtPath<SpeciesData>(
                "Assets/BiowebV46/Species/鹿-fd1c574b.asset");
            SpeciesData swimmer = AssetDatabase.LoadAssetAtPath<SpeciesData>(
                "Assets/BiowebV46/Species/鹿游-103a527e.asset");
            Check(deer != null && swimmer != null, "real evolution assets");
            BlockInfo landA = Cell(scene, "landA", 0, 0, false);
            BlockInfo waterA = Cell(scene, "waterA", 1, 0, true);
            BlockInfo waterB = Cell(scene, "waterB", 3, 0, true);
            BlockInfo landB = Cell(scene, "landB", 4, 0, false);
            Link(landA, waterA); Link(waterB, landB);
            PopulationData parentLand = Population(deer, swimmer, 200);
            PopulationData parentWater = Population(swimmer, deer, 200);
            landA.community.Add(parentLand);
            waterB.community.Add(parentWater);
            GameObject root = new GameObject("CrossHabitatV04 smoke");
            SceneManager.MoveGameObjectToScene(root, scene);
            SimulationController simulation = root.AddComponent<SimulationController>();
            simulation.SetBlocks(new List<BlockInfo> {landA, waterA, waterB, landB});
            simulation.ConfigureDeterministicRun(20261018);
            simulation.SetWaterEcologyEnabled(true);
            simulation.SetAirEcologyEnabled(false);
            simulation.SetMutationEnabled(false);
            simulation.SetMigrationParameters(false, 7, 100f, .8f, .5f, .2f, 7);
            simulation.SetPresetEvolutionParameters(true, 5, .1f, 10);
            for (int day = 1; day <= 12; day++)
            {
                foreach (BlockInfo cell in new[] {waterA, waterB})
                {
                    cell.plantGrowthToday = Mathf.Min(cell.habitatRecovery,
                        Mathf.Max(0f, cell.maxPlantBiomass - cell.plantBiomass));
                    cell.plantBiomass += cell.plantGrowthToday;
                }
                simulation.SimulateDay(day);
                Debug.Log("CROSS_V04_DAY day=" + day + " deer=" +
                    parentLand.speciesAmount + " swimmer=" +
                    parentWater.speciesAmount + " landToWater=" +
                    Count(waterA, swimmer) + " waterToLand=" +
                    Count(landB, deer) + " residual=" +
                    simulation.LastEcologyLedger.massResidual);
            }
            Check(Count(waterA, swimmer) > 0, "land->water formal evolution");
            Check(Count(landB, deer) > 0, "water->land formal evolution");
            Check(parentLand.speciesAmount > 0 && parentWater.speciesAmount > 0,
                "parents must remain");
            Debug.Log("CROSS_HABITAT_V04_SMOKE_PASS days=12");
        }
        catch (Exception error)
        { failed = true; Debug.LogException(error); }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
        else if (failed) throw new Exception("Cross habitat smoke failed");
    }

    private static int Count(BlockInfo block, SpeciesData species)
    {
        int count = 0;
        foreach (PopulationData p in block.community)
            if (p != null && p.species == species) count += p.speciesAmount;
        return count;
    }

    private static PopulationData Population(SpeciesData source,
        SpeciesData target, int count) => new PopulationData
    {
        species = source, speciesAmount = count,
        ecologicalNiche = source.baseEcologicalNiche,
        habitatNiche = source.baseHabitatNiche,
        trophicLevel = source.trophicLevel, trophicLevelInitialized = true,
        fitTemperature = target.baseFitTemperature,
        fitHumidity = target.baseFitHumidity,
        movementAbility = target.baseMovementAbility,
        size = target.baseSize, fertility = target.baseFertility
    };

    private static BlockInfo Cell(Scene scene, string name, int x, int y,
        bool water)
    {
        GameObject root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        MapTileInstance tile = root.AddComponent<MapTileInstance>();
        SerializedObject so = new SerializedObject(tile);
        so.FindProperty("coordinate").vector2IntValue = new Vector2Int(x,y);
        so.ApplyModifiedPropertiesWithoutUndo();
        BlockInfo block = root.AddComponent<BlockInfo>();
        block.waterCoverage = water ? WaterCoverage.Lake : WaterCoverage.Land;
        block.elevation = 0;
        block.temperature = block.humidity = 50;
        block.plantBiomass = 500000f;
        block.maxPlantBiomass = 1000000f;
        block.habitatRecovery = 100000;
        block.community = new List<PopulationData>();
        block.SetNeighbors(new List<BlockInfo>());
        block.SetWaterLinks(new List<BlockInfo>());
        return block;
    }

    private static void Link(BlockInfo a, BlockInfo b)
    {
        a.SetNeighbors(new List<BlockInfo>(a.Neighbors) {b});
        b.SetNeighbors(new List<BlockInfo>(b.Neighbors) {a});
    }

    private static void Check(bool okay, string what)
    { if (!okay) throw new Exception(what); }
}
#endif
