#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Small production-loop checks. No reference-model food or mutation formula is used.
public static class CrossNicheV06SmokeChecks
{
    [MenuItem("Tools/Darwin Farm/Validation/Cross Niche v0.6/Run Short Smoke")]
    public static void RunBatch()
    {
        bool failed = false;
        try
        {
            WaterCellsDoNotShareFood();
            WaterMigrationIsPartialAndConservesCount();
            ThreeBarCrossEvolution();
            MutationUsesOneSlotAndSkipsWaterClimate();
            Debug.Log("CROSS_NICHE_V06_SHORT_SMOKE_PASS");
        }
        catch (Exception error)
        {
            failed = true;
            Debug.LogException(error);
            Debug.LogError("CROSS_NICHE_V06_SHORT_SMOKE_FAIL: " + error.Message);
        }
        if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
        else if (failed) throw new Exception("Cross niche v0.6 short smoke failed");
    }

    private static void WaterCellsDoNotShareFood()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            SpeciesData swimmer = Load("鹿游-103a527e");
            Check(swimmer != null, "missing water species asset");
            BlockInfo poor = Cell(scene, "poor", 0, 0, true);
            BlockInfo rich = Cell(scene, "rich", 1, 0, true);
            Link(poor, rich, true);
            poor.plantBiomass = 0f; poor.habitatRecovery = 0;
            rich.plantBiomass = 500000f; rich.habitatRecovery = 100000;
            PopulationData hungry = Population(swimmer, swimmer, 100);
            PopulationData fed = Population(swimmer, swimmer, 100);
            hungry.fitTemperature = hungry.fitHumidity = 0;
            poor.community.Add(hungry); rich.community.Add(fed);
            SimulationController simulation = Controller(scene, poor, rich);
            simulation.SetWaterEcologyEnabled(true);
            simulation.SetMutationEnabled(false);
            simulation.SetMigrationParameters(false, 7, 100f, .8f, .5f, .2f, 7);
            simulation.SetPresetEvolutionParameters(false, 5, .1f, 10);
            simulation.SimulateDay(1);
            Check(poor.plantBiomass == 0f && rich.plantBiomass < 500000f,
                "adjacent water cells shared plant inventory");
            Check(hungry.energySatisfactionToday < .01f &&
                fed.energySatisfactionToday > .5f,
                "adjacent water consumers shared food allocation");
            Check(hungry.environmentalFitness == 100,
                "water fitness still uses temperature or humidity");
            CheckLedger(simulation, "water isolation");
            Debug.Log("V06_WATER_ISOLATION_PASS poorS=" + poor.plantBiomass +
                " richS=" + rich.plantBiomass);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void WaterMigrationIsPartialAndConservesCount()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            SpeciesData swimmer = Load("鹿游-103a527e");
            BlockInfo source = Cell(scene, "source", 0, 0, true);
            BlockInfo target = Cell(scene, "target", 1, 0, true);
            Link(source, target, true);
            source.plantBiomass = 100000f;
            source.habitatRecovery = 1000;
            target.plantBiomass = 900000f;
            target.habitatRecovery = 150000;
            PopulationData parent = Population(swimmer, swimmer, 1000);
            source.community.Add(parent);
            SimulationController simulation = Controller(scene, source, target);
            simulation.SetWaterEcologyEnabled(true);
            simulation.SetMutationEnabled(false);
            simulation.SetPresetEvolutionParameters(false, 5, .1f, 10);
            simulation.SetMigrationParameters(true, 7, 100f, .8f, .5f, .2f, 7);
            simulation.SetCrossHabitatDebugProbabilities(0f, 0f, 1f);
            PopulationTransitionResult? move = null;
            simulation.OnMigrationResolved += result =>
            { if (result.Kind == PopulationTransitionKind.Migration) move = result; };
            simulation.SimulateDay(7);
            Check(move.HasValue && move.Value.ResultNiche == EcologicalNiche.Water,
                "day-seven water migration did not execute");
            Check(move.Value.Amount >= 10 && move.Value.Amount <= 100 &&
                move.Value.SourceCountAfter > 0 &&
                move.Value.TargetCountBefore == 0 &&
                move.Value.SourceCountBefore == move.Value.SourceCountAfter +
                    move.Value.Amount && move.Value.TargetCountAfter ==
                    move.Value.Amount,
                "water partial migration lost or created individuals");
            Check(parent.species == target.community[0].species,
                "migration changed species identity");
            CheckLedger(simulation, "water migration");
            Debug.Log("V06_WATER_MIGRATION_PASS amount=" + move.Value.Amount);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void ThreeBarCrossEvolution()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            SpeciesData deer = Load("鹿-fd1c574b");
            SpeciesData swimmer = Load("鹿游-103a527e");
            Check(deer != null && swimmer != null, "missing evolution assets");
            BlockInfo land = Cell(scene, "land", 0, 0, false);
            BlockInfo water = Cell(scene, "water", 1, 0, true);
            Link(land, water, false);
            PopulationData parent = Population(deer, swimmer, 1000);
            parent.fitTemperature = 0;
            parent.fitHumidity = 0;
            land.temperature = land.humidity = 0;
            land.community.Add(parent);
            SimulationController simulation = Controller(scene, land, water);
            simulation.SetWaterEcologyEnabled(true);
            simulation.SetMutationEnabled(false);
            simulation.SetMigrationParameters(false, 7, 100f, .8f, .5f, .2f, 7);
            simulation.SetPresetEvolutionParameters(true, 5, .1f, 10);
            for (int day = 1; day <= 5; day++)
            {
                water.plantGrowthToday = Mathf.Min(water.habitatRecovery,
                    water.maxPlantBiomass - water.plantBiomass);
                water.plantBiomass += water.plantGrowthToday;
                simulation.SimulateDay(day);
                CheckLedger(simulation, "cross evolution day " + day);
            }
            int child = Count(water, swimmer);
            Check(child > 0 && parent.speciesAmount > 0,
                "three-Bar land-to-water evolution did not preserve parent");
            Check(water.community.Exists(p => p != null &&
                p.species == swimmer && p.ecologicalNiche == EcologicalNiche.Water),
                "evolution did not create target water species");
            Debug.Log("V06_THREE_BAR_EVOLUTION_PASS founder=" + child);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void MutationUsesOneSlotAndSkipsWaterClimate()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            SpeciesData deer = Load("鹿-fd1c574b");
            SpeciesData swimmer = Load("鹿游-103a527e");
            BlockInfo land = Cell(scene, "mutationLand", 0, 0, false);
            BlockInfo water = Cell(scene, "mutationWater", 2, 0, true);
            land.temperature = land.humidity = 100;
            water.temperature = water.humidity = 100;
            PopulationData ground = Population(deer, deer, 100);
            PopulationData aquatic = Population(swimmer, swimmer, 100);
            ground.fitTemperature = ground.fitHumidity = 0;
            aquatic.fitTemperature = aquatic.fitHumidity = 0;
            land.community.Add(ground);
            water.community.Add(aquatic);
            SimulationController simulation = Controller(scene, land, water);
            simulation.SetWaterEcologyEnabled(true);
            simulation.SetMutationEnabled(true);
            simulation.SetMutationParameters(1, 1f, 1f);
            simulation.SetMigrationParameters(false, 7, 100f,
                .8f, .5f, .2f, 7);
            simulation.SetPresetEvolutionParameters(false, 5, .1f, 10);
            int[] groundBefore = Traits(ground);
            int[] waterBefore = Traits(aquatic);
            simulation.SimulateDay(1);
            Check(TraitSteps(groundBefore, Traits(ground)) == 1,
                "land mutation did not use exactly one shared climate slot");
            Check(TraitSteps(waterBefore, Traits(aquatic)) <= 1 &&
                aquatic.fitTemperature == 0 && aquatic.fitHumidity == 0,
                "water mutation used climate or multiple trait slots");
            CheckLedger(simulation, "mutation slot");
            Debug.Log("V06_MUTATION_SINGLE_SLOT_PASS");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static int[] Traits(PopulationData p) => new[]
    {
        p.fitTemperature, p.fitHumidity, p.movementAbility,
        p.size, p.fertility
    };

    private static int TraitSteps(int[] before, int[] after)
    {
        int total = 0;
        for (int i = 0; i < before.Length; i++)
            total += Math.Abs(before[i] - after[i]);
        return total;
    }

    private static SimulationController Controller(Scene scene, params BlockInfo[] blocks)
    {
        GameObject root = new GameObject("CrossNicheV06Smoke");
        SceneManager.MoveGameObjectToScene(root, scene);
        SimulationController simulation = root.AddComponent<SimulationController>();
        simulation.SetBlocks(new List<BlockInfo>(blocks));
        simulation.ConfigureDeterministicRun(20261018);
        return simulation;
    }

    private static SpeciesData Load(string stem) =>
        AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/BiowebV46/Species/" + stem + ".asset");

    private static PopulationData Population(SpeciesData source,
        SpeciesData traits, int count) => new PopulationData
    {
        species = source, speciesAmount = count,
        ecologicalNiche = source.baseEcologicalNiche,
        habitatNiche = source.baseHabitatNiche,
        trophicLevel = source.trophicLevel, trophicLevelInitialized = true,
        movementAbility = traits.baseMovementAbility,
        fitTemperature = traits.baseFitTemperature,
        fitHumidity = traits.baseFitHumidity,
        size = traits.baseSize, fertility = traits.baseFertility
    };

    private static BlockInfo Cell(Scene scene, string name, int x, int y,
        bool water)
    {
        GameObject root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        MapTileInstance tile = root.AddComponent<MapTileInstance>();
        SerializedObject serialized = new SerializedObject(tile);
        serialized.FindProperty("coordinate").vector2IntValue =
            new Vector2Int(x, y);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        BlockInfo block = root.AddComponent<BlockInfo>();
        block.waterCoverage = water ? WaterCoverage.Lake : WaterCoverage.Land;
        block.temperature = block.humidity = 50;
        block.plantBiomass = 500000f;
        block.maxPlantBiomass = 1000000f;
        block.habitatRecovery = 100000;
        block.community = new List<PopulationData>();
        block.SetNeighbors(new List<BlockInfo>());
        block.SetWaterLinks(new List<BlockInfo>());
        return block;
    }

    private static void Link(BlockInfo a, BlockInfo b, bool water)
    {
        a.SetNeighbors(new List<BlockInfo>(a.Neighbors) { b });
        b.SetNeighbors(new List<BlockInfo>(b.Neighbors) { a });
        if (!water) return;
        a.SetWaterLinks(new List<BlockInfo> { b });
        b.SetWaterLinks(new List<BlockInfo> { a });
    }

    private static int Count(BlockInfo block, SpeciesData species)
    {
        int count = 0;
        foreach (PopulationData population in block.community)
            if (population != null && population.species == species)
                count += population.speciesAmount;
        return count;
    }

    private static void CheckLedger(SimulationController simulation, string name)
    {
        DailyEcologyLedger ledger = simulation.LastEcologyLedger;
        Check(ledger != null && Math.Abs(ledger.massResidual) < 1.0,
            name + " mass residual=" + (ledger == null ? "null" :
                ledger.massResidual.ToString("R")));
        foreach (EcologySpeciesDailyAccount species in ledger.species)
            Check(species.Residual == 0,
                name + " population residual=" + species.Residual);
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
#endif
