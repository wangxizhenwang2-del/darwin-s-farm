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
            AirCanReturnAfterThirtyDayCooldown();
            WaterAndAirMigrationAreRejected();
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

    // Keeps the unchanged land paths in the same short production day loop.
    public static void RunLandBaselineBatch()
    {
        bool failed = false;
        try
        {
            LandEvolutionAndMigrationStayLive();
            Debug.Log("V06_LAND_BASELINE_PASS");
        }
        catch (Exception error)
        { failed = true; Debug.LogException(error); }
        if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
        else if (failed) throw new Exception("Land baseline smoke failed");
    }

    private static void LandEvolutionAndMigrationStayLive()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            SpeciesData chicken = Load("彩鸡-b59a4a82");
            SpeciesData target = Load("鸡-7f113739");
            Check(chicken != null && target != null &&
                chicken.evolutionTargets.Contains(target),
                "ordinary land evolution edge missing");
            BlockInfo home = Cell(scene, "landEvolution", 0, 0, false);
            home.temperature = target.baseFitTemperature;
            home.humidity = target.baseFitHumidity;
            home.plantBiomass = 900000f;
            home.habitatRecovery = 150000;
            PopulationData parent = Population(chicken, target, 1000);
            home.community.Add(parent);
            SimulationController evolution = Controller(scene, home);
            evolution.SetMutationEnabled(false);
            evolution.SetMigrationParameters(false, 7, 100f, .8f, .5f,
                .2f, 7);
            evolution.SetPresetEvolutionParameters(true, 5, .1f, 10);
            for (int day = 1; day <= 5; day++)
            {
                evolution.SimulateDay(day);
                CheckLedger(evolution, "land evolution day " + day);
            }
            Check(Count(home, target) > 0 && parent.speciesAmount > 0,
                "ordinary five-Bar land evolution did not complete");

            SpeciesData deer = Load("鹿-fd1c574b");
            BlockInfo from = Cell(scene, "landMigrationA", 2, 0, false);
            BlockInfo to = Cell(scene, "landMigrationB", 3, 0, false);
            Link(from, to, false);
            from.temperature = to.temperature = deer.baseFitTemperature;
            from.humidity = to.humidity = deer.baseFitHumidity;
            from.plantBiomass = 500000f;
            from.habitatRecovery = 25000;
            to.plantBiomass = 900000f;
            to.habitatRecovery = 150000;
            from.community.Add(Population(deer, deer, 1000));
            SimulationController migration = Controller(scene, from, to);
            migration.SetMutationEnabled(false);
            migration.SetPresetEvolutionParameters(false, 5, .1f, 10);
            migration.SetMigrationParameters(true, 7, 100f, .8f, .5f,
                .2f, 7);
            bool moved = false;
            for (int day = 1; day <= 70 && !moved; day++)
            {
                migration.SimulateDay(day);
                moved = Count(to, deer) > 0;
            }
            Check(moved && Count(from, deer) > 0,
                "ordinary land partial migration did not complete");
            CheckLedger(migration, "land migration");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
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

    private static void AirCanReturnAfterThirtyDayCooldown()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            SpeciesData flyer = Load("鹿飞-a61486cd");
            SpeciesData prey = Load("彩鸡-b59a4a82");
            SpeciesData secondPrey = Load("孔雀-f0686f45");
            Check(flyer != null && prey != null && secondPrey != null,
                "missing flight or legal prey asset");
            BlockInfo a = Cell(scene, "flightReturnA", 0, 0, false);
            BlockInfo b = Cell(scene, "flightReturnB", 1, 0, false);
            Link(a, b, false);
            a.plantBiomass = b.plantBiomass = 3000000f;
            a.maxPlantBiomass = b.maxPlantBiomass = 5000000f;
            a.habitatRecovery = b.habitatRecovery = 1000000;
            PopulationData aPrey = Population(prey, prey, 5000);
            PopulationData bPrey = Population(prey, prey, 20000);
            PopulationData parent = Population(flyer, flyer, 100);
            a.community.Add(aPrey);
            a.community.Add(parent);
            b.community.Add(bPrey);
            SimulationController simulation = Controller(scene, a, b);
            simulation.SetAirEcologyEnabled(true);
            simulation.SetWaterEcologyEnabled(false);
            simulation.SetMutationEnabled(false);
            simulation.SetPresetEvolutionParameters(false, 5, .1f, 10);
            simulation.SetCrossHabitatDebugProbabilities(0f, 0f, 0f);
            simulation.SetFlightCandidateParameters(.1f, 50, 1f);
            simulation.SetMigrationParameters(true, 7, 100f,
                .8f, .5f, .2f, 7);
            int moves = 0;
            simulation.OnMigrationResolved += result =>
            {
                if (result.Kind == PopulationTransitionKind.Migration &&
                    result.ResultNiche == EcologicalNiche.Air) moves++;
            };
            int firstDay = -1;
            for (int day = 1; day <= 15 && firstDay < 0; day++)
            {
                simulation.SimulateDay(day);
                if (Count(b, flyer) > 0) firstDay = day;
            }
            Check(firstDay > 0 && moves == 1 && Count(a, flyer) == 0,
                "eligible first air move was not a whole-group move: day=" +
                firstDay + " moves=" + moves + " flyerA=" + Count(a, flyer) +
                " flyerB=" + Count(b, flyer) + " preyA=" + Count(a, prey) +
                " preyB=" + Count(b, prey));
            PopulationData arrival = b.community.Find(p =>
                p != null && p.species == flyer && p.speciesAmount > 0);
            Check(arrival != null && arrival.lastMigrationSource == a &&
                arrival.nextMigrationDay == firstDay + 30,
                "air move did not establish the 30-day cooldown");
            // Add a second legal prey group while preserving the first
            // group's real renewal history for the return food forecast.
            a.community.Add(Population(secondPrey, secondPrey, 500));
            a.plantBiomass = a.maxPlantBiomass;
            for (int day = firstDay + 1; day < firstDay + 30; day++)
            {
                simulation.SimulateDay(day);
                Check(moves == 1 && Count(a, flyer) == 0,
                    "air group migrated during cooldown on day " + day);
            }
            bool returned = false;
            for (int day = firstDay + 30; day <= firstDay + 45; day++)
            {
                simulation.SimulateDay(day);
                if (Count(a, flyer) <= 0) continue;
                returned = true;
                Check(moves == 2 && Count(b, flyer) == 0,
                    "air return created a branch or extra migration");
                PopulationData home = a.community.Find(p =>
                    p != null && p.species == flyer && p.speciesAmount > 0);
                Check(home != null && home.nextMigrationDay == day + 30,
                    "return migration did not restart cooldown");
                break;
            }
            Check(returned, "eligible previous source was never reconsidered: " +
                "moves=" + moves + " flyerA=" + Count(a, flyer) +
                " flyerB=" + Count(b, flyer) + " preyA=" + Count(a, prey) +
                " preyB=" + Count(b, prey) + " secondPreyA=" +
                Count(a, secondPrey) + " next=" +
                (arrival == null ? -1 : arrival.nextMigrationDay));
            CheckLedger(simulation, "air return");
            Debug.Log("V06_AIR_RETURN_COOLDOWN_PASS first=" + firstDay);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void WaterAndAirMigrationAreRejected()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            SpeciesData flyer = Load("鹿飞-a61486cd");
            SpeciesData prey = Load("彩鸡-b59a4a82");
            SpeciesData swimmer = Load("鹿游-103a527e");
            BlockInfo land = Cell(scene, "airBoundary", 0, 0, false);
            BlockInfo water = Cell(scene, "waterBoundary", 1, 0, true);
            Link(land, water, true);
            land.community.Add(Population(prey, prey, 5000));
            PopulationData airGroup = Population(flyer, flyer, 100);
            PopulationData waterGroup = Population(swimmer, swimmer, 100);
            land.community.Add(airGroup);
            water.community.Add(waterGroup);
            SimulationController simulation = Controller(scene, land, water);
            simulation.SetAirEcologyEnabled(true);
            simulation.SetWaterEcologyEnabled(true);
            simulation.SetMutationEnabled(false);
            simulation.SetPresetEvolutionParameters(false, 5, .1f, 10);
            simulation.SetCrossHabitatDebugProbabilities(0f, 0f, 0f);
            simulation.SetFlightCandidateParameters(.1f, 50, 1f);
            simulation.SetMigrationParameters(true, 7, 100f, .8f, .5f,
                .2f, 7);
            simulation.SimulateDay(1);
            Check(!simulation.CanLegalMigration(land, water, airGroup,
                    airGroup.speciesAmount) &&
                !simulation.CanLegalMigration(water, land, waterGroup, 10) &&
                !simulation.TryAirMigration(land, water, airGroup, 1) &&
                Count(water, flyer) == 0 && Count(land, swimmer) == 0,
                "water/air migration boundary was crossed");
            CheckLedger(simulation, "water-air prohibition");
            Debug.Log("V06_WATER_AIR_MIGRATION_BLOCK_PASS");
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
