#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class UnifiedMovementDeerRegression
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance |
        BindingFlags.NonPublic;

    public static void RunBatch()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            CheckNeighborCeilings(scene);
            CheckMovementPaceDeadlines(scene);
            SimulatePlayerControls(scene);
            SimulatePredatorPressure(scene);
            SimulateDeerToSwimmer(scene);
            Debug.Log("UNIFIED_MOVEMENT_DEER_PASS");
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

    private static void CheckNeighborCeilings(Scene scene)
    {
        BlockInfo home = Cell(scene, "movement-home", 0, 0, false, 0);
        BlockInfo flat = Cell(scene, "movement-flat", 1, 0, false, 0);
        BlockInfo slope = Cell(scene, "movement-slope", 0, 1, false, 1);
        home.plantBiomass = 30000f;
        home.habitatRecovery = 1000;
        flat.plantBiomass = slope.plantBiomass = 900000f;
        flat.habitatRecovery = slope.habitatRecovery = 200000;
        Link(home, flat); Link(home, slope);
        var pop = new PopulationData { ecologicalNiche = EcologicalNiche.Land,
            trophicLevel = 0, speciesAmount = 300, movementAbility = 40,
            size = 20, fertility = 50, fitTemperature = 50,
            fitHumidity = 50 };
        home.community.Add(pop);
        SimulationController sim = Controller(scene, home, flat, slope);
        MethodInfo method = typeof(SimulationController).GetMethod(
            "BestNeighborMovementChance", PrivateInstance);
        Require(method != null, "neighbor chance method missing");
        object[] args = { home, pop, false, 0 };
        float chance = (float)method.Invoke(sim, args);
        Require(chance > 0f && (int)args[3] == 80,
            "a capped flat neighbor must not hide the usable slope");
        pop.movementAbility = 80;
        args = new object[] { home, pop, false, 0 };
        Require((float)method.Invoke(sim, args) == 0f,
            "one-step slope guidance must stop at 80");
        Debug.Log("UNIFIED_MOVEMENT_CEILINGS_PASS slopeChance=" + chance);
    }

    private static void SimulateDeerToSwimmer(Scene scene)
    {
        SpeciesData deer = AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/BiowebV46/Species/鹿-fd1c574b.asset");
        SpeciesData swimmer = AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/BiowebV46/Species/鹿游-103a527e.asset");
        Require(deer != null && swimmer != null, "real deer assets missing");
        BlockInfo land = Cell(scene, "deer-land", 10, 0, false, 0);
        BlockInfo water = Cell(scene, "deer-water", 11, 0, true, 0);
        land.temperature = 30; land.humidity = 70;
        land.plantBiomass = water.plantBiomass = 900000f;
        land.habitatRecovery = water.habitatRecovery = 120000;
        Link(land, water);
        var ancestor = new PopulationData { species = deer,
            ecologicalNiche = EcologicalNiche.Land, trophicLevel = 0,
            speciesAmount = 200, movementAbility = deer.baseMovementAbility,
            size = swimmer.baseSize + 5, fertility = swimmer.baseFertility - 5,
            fitTemperature = deer.baseFitTemperature,
            fitHumidity = deer.baseFitHumidity };
        land.community.Add(ancestor);
        SimulationController sim = Controller(scene, land, water);
        sim.SetWaterEcologyEnabled(true);
        sim.SetEvolutionNetworkSpecies(new[] { deer, swimmer });
        sim.SetAllowedEvolutionSpecies(new[] { deer, swimmer });
        sim.SetMutationParameters(1, 1f, 1f);
        sim.SetMutationEnabled(true);
        sim.SetMigrationParameters(false, 7, 100f, .8f, .5f, .2f, 0);
        sim.ConfigureDeterministicRun(20261020);
        Require(sim.InspectCrossHabitatEvolutionRoute(land, ancestor, water,
            swimmer) == "ROUTE_OK", "deer to swimmer route missing");
        Require(sim.SetManualEvolutionTarget(ancestor, swimmer),
            "swimmer manual target rejected");
        int movementReadyDay = -1, evolutionDay = -1;
        for (int day = 1; day <= 150; day++)
        {
            sim.SimulateDay(day);
            if (movementReadyDay < 0 && ancestor.movementAbility <=
                swimmer.baseMovementAbility + 5) movementReadyDay = day;
            if (day % 10 == 0 || movementReadyDay == day)
                Debug.Log("UNIFIED_DEER_TRACE day=" + day + " m=" +
                    ancestor.movementAbility + " size=" + ancestor.size +
                    " fertility=" + ancestor.fertility + " N=" +
                    ancestor.speciesAmount + " waterN=" +
                    Count(water, swimmer));
            if (Count(water, swimmer) > 0)
            { evolutionDay = day; break; }
            Require(ancestor.speciesAmount > 1,
                "deer population died before movement recovery");
        }
        Require(movementReadyDay > 0,
            "deer movement did not reach swimmer tolerance by day 150");
        Require(evolutionDay > 0,
            "real deer did not formally evolve into swimmer by day 150");
        Debug.Log("UNIFIED_DEER_TO_SWIMMER_PASS readyDay=" +
            movementReadyDay + " evolutionDay=" + evolutionDay +
            " descendantCount=" + Count(water, swimmer));
    }

    private static void SimulatePlayerControls(Scene scene)
    {
        RunControlCase(scene, "flat", 20, 0, false, false);
        RunControlCase(scene, "slope", 20, 1, false, false);
        RunControlCase(scene, "island", 72, 2, false, false);
        RunControlCase(scene, "cliff-after-slope", 70, 1, true, false);
        RunControlCase(scene, "reopen-slope", 70, 1, true, true);
        RunMixedNeighborCase(scene);
    }

    private static void CheckMovementPaceDeadlines(Scene scene)
    {
        SpeciesData swimmer = AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/BiowebV46/Species/鹿游-103a527e.asset");
        Require(swimmer != null, "swimmer asset missing for pace check");
        foreach (int deadline in new[] { 50, 120, 0 })
        {
            SimulationController sim = Controller(scene);
            LandRuleTuning rules = sim.ReadLandRules();
            rules.movementTargetDeadlineDays = deadline;
            sim.ApplyLandRules(rules);
            var population = new PopulationData {
                movementAbility = 72, size = swimmer.baseSize,
                fertility = swimmer.baseFertility,
                trackedEvolutionTarget = swimmer
            };
            MethodInfo step = typeof(SimulationController).GetMethod(
                "MovementPaceDirection", PrivateInstance);
            FieldInfo currentDay = typeof(SimulationController).GetField(
                "currentSimulationDay", PrivateInstance);
            int firstStep = -1;
            for (int day = 1; day <= (deadline == 0 ? 150 : deadline);
                day++)
            {
                currentDay.SetValue(sim, day);
                int direction = (int)step.Invoke(sim,
                    new object[] { population });
                if (direction != 0)
                {
                    if (firstStep < 0) firstStep = day;
                    population.movementAbility += direction;
                }
            }
            if (deadline == 0)
                Require(population.movementAbility == 72,
                    "hardest pace must have no forced mutation");
            else Require(population.movementAbility ==
                swimmer.baseMovementAbility + rules.evolutionTraitTolerance,
                "pace failed its movement deadline");
            Debug.Log("MOVEMENT_PACE_RESULT deadline=" + deadline +
                " firstStep=" + firstStep + " final=" +
                population.movementAbility);
        }
        SpeciesData distant = ScriptableObject.CreateInstance<SpeciesData>();
        try
        {
            distant.baseEcologicalNiche = EcologicalNiche.Water;
            distant.baseMovementAbility = 0;
            distant.baseSize = 30;
            distant.baseFertility = 55;
            SimulationController sim = Controller(scene);
            LandRuleTuning rules = sim.ReadLandRules();
            rules.movementTargetDeadlineDays = 50;
            sim.ApplyLandRules(rules);
            var population = new PopulationData {
                movementAbility = 100, size = 30, fertility = 55,
                trackedEvolutionTarget = distant
            };
            MethodInfo step = typeof(SimulationController).GetMethod(
                "MovementPaceDirection", PrivateInstance);
            FieldInfo currentDay = typeof(SimulationController).GetField(
                "currentSimulationDay", PrivateInstance);
            int maxStep = 0;
            for (int day = 1; day <= 50; day++)
            {
                currentDay.SetValue(sim, day);
                int direction = (int)step.Invoke(sim,
                    new object[] { population });
                maxStep = Mathf.Max(maxStep, Mathf.Abs(direction));
                population.movementAbility += direction;
            }
            Require(population.movementAbility == 5 && maxStep == 2,
                "easy deadline must handle a distance larger than 50");
            Debug.Log("MOVEMENT_PACE_RESULT distantGap=95 deadline=50" +
                " maxStep=" + maxStep + " final=" +
                population.movementAbility);
        }
        finally { UnityEngine.Object.DestroyImmediate(distant); }
    }

    private static void RunControlCase(Scene scene, string label,
        int startMovement, int neighborElevation, bool cutOnDay31,
        bool reopenOnDay61)
    {
        int x = 100 + (label == "flat" ? 0 : label == "slope" ? 10 :
            label == "island" ? 20 : label == "cliff-after-slope" ? 30 : 40);
        BlockInfo home = Cell(scene, label + "-home", x, 0, false, 0);
        BlockInfo neighbor = Cell(scene, label + "-neighbor", x + 1, 0,
            false, neighborElevation);
        home.plantBiomass = 30000f;
        home.habitatRecovery = 100000;
        neighbor.plantBiomass = 900000f;
        neighbor.habitatRecovery = 300000;
        Link(home, neighbor);
        var population = new PopulationData {
            ecologicalNiche = EcologicalNiche.Land, trophicLevel = 0,
            speciesAmount = 150, movementAbility = startMovement,
            size = 20, fertility = 50, fitTemperature = 50,
            fitHumidity = 50
        };
        typeof(PopulationData).GetMethod("AssignPopulationId",
            PrivateInstance).Invoke(population,
                new object[] { "movement-control-" + label });
        home.community.Add(population);
        SimulationController sim = Controller(scene, home, neighbor);
        sim.SetMutationParameters(1, 1f, 1f);
        sim.SetMutationEnabled(true);
        sim.SetMigrationParameters(false, 7, 100f, .8f, .5f, .2f, 0);
        sim.ConfigureDeterministicRun(20261020);
        int firstUp = -1, firstDown = -1, firstReopenUp = -1,
            prior = startMovement;
        var snapshots = new List<string>();
        var offers = new List<string>();
        MethodInfo offerMethod = typeof(SimulationController).GetMethod(
            "BestNeighborMovementChance", PrivateInstance);
        for (int day = 1; day <= 90; day++)
        {
            if (cutOnDay31 && day == 31)
            {
                neighbor.elevation = 2;
                home.neighborOpportunityRevision++;
                neighbor.neighborOpportunityRevision++;
            }
            if (reopenOnDay61 && day == 61)
            {
                neighbor.elevation = 1;
                home.neighborOpportunityRevision++;
                neighbor.neighborOpportunityRevision++;
            }
            if (day == 1 || day == 7 || day == 14 || day == 20 ||
                day == 21 || day == 23 || day == 31 || day == 32)
            {
                object[] probe = { home, population, false, 0 };
                float chance = (float)offerMethod.Invoke(sim, probe);
                offers.Add(day + ":" + chance.ToString("F2") +
                    "/N" + population.speciesAmount);
            }
            sim.SimulateDay(day);
            int movement = population.movementAbility;
            if (firstUp < 0 && movement > prior) firstUp = day;
            if (firstDown < 0 && movement < prior) firstDown = day;
            if (reopenOnDay61 && day >= 61 && firstReopenUp < 0 &&
                movement > prior) firstReopenUp = day;
            if (day == 1 || day == 3 || day == 7 || day == 14 ||
                day == 30 || day == 31 || day == 37 || day == 45 ||
                day == 60 || day == 61 || day == 62 || day == 67 ||
                day == 75 || day == 90)
                snapshots.Add(day + ":" + movement);
            prior = movement;
            Require(population.speciesAmount > 1,
                label + " population died at day " + day);
        }
        Debug.Log("MOVEMENT_CONTROL_RESULT " + label + " firstUp=" +
            firstUp + " firstDown=" + firstDown + " firstReopenUp=" +
            firstReopenUp + " values=" +
            string.Join(",", snapshots) + " offers=" +
            string.Join(",", offers));
    }

    private static void RunMixedNeighborCase(Scene scene)
    {
        BlockInfo home = Cell(scene, "mixed-home", 160, 0, false, 0);
        BlockInfo flat = Cell(scene, "mixed-flat", 161, 0, false, 0);
        BlockInfo slope = Cell(scene, "mixed-slope", 160, 1, false, 1);
        home.plantBiomass = 30000f;
        home.habitatRecovery = 100000;
        flat.plantBiomass = slope.plantBiomass = 900000f;
        flat.habitatRecovery = slope.habitatRecovery = 300000;
        Link(home, flat); Link(home, slope);
        var population = new PopulationData {
            ecologicalNiche = EcologicalNiche.Land, trophicLevel = 0,
            speciesAmount = 150, movementAbility = 70,
            size = 20, fertility = 50, fitTemperature = 50,
            fitHumidity = 50
        };
        typeof(PopulationData).GetMethod("AssignPopulationId",
            PrivateInstance).Invoke(population,
                new object[] { "movement-control-mixed" });
        home.community.Add(population);
        SimulationController sim = Controller(scene, home, flat, slope);
        sim.SetMutationParameters(1, 1f, 1f);
        sim.SetMutationEnabled(true);
        sim.SetMigrationParameters(false, 7, 100f, .8f, .5f, .2f, 0);
        sim.ConfigureDeterministicRun(20261020);
        int firstDown = -1, prior = 70;
        var snapshots = new List<string>();
        for (int day = 1; day <= 90; day++)
        {
            if (day == 31)
            {
                slope.elevation = 2;
                home.neighborOpportunityRevision++;
                slope.neighborOpportunityRevision++;
            }
            sim.SimulateDay(day);
            if (day >= 31 && firstDown < 0 &&
                population.movementAbility < prior) firstDown = day;
            if (day == 30 || day == 31 || day == 37 || day == 45 ||
                day == 60 || day == 90)
                snapshots.Add(day + ":" + population.movementAbility);
            prior = population.movementAbility;
        }
        Require(population.movementAbility == 40,
            "flat neighbor recoil must stop at the flat ceiling");
        Debug.Log("MOVEMENT_CONTROL_RESULT mixed-neighbors firstDown=" +
            firstDown + " values=" + string.Join(",", snapshots));
    }

    private static void SimulatePredatorPressure(Scene scene)
    {
        SpeciesData deer = AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/BiowebV46/Species/鹿-fd1c574b.asset");
        SpeciesData wolf = AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/BiowebV46/Species/雪狼-fb2d8345.asset");
        Require(deer != null && wolf != null, "predator assets missing");
        BlockInfo home = Cell(scene, "predator-home", 180, 0, false, 0);
        home.temperature = deer.baseFitTemperature;
        home.humidity = deer.baseFitHumidity;
        home.plantBiomass = 900000f;
        home.habitatRecovery = 300000;
        var prey = new PopulationData { species = deer,
            ecologicalNiche = EcologicalNiche.Land, trophicLevel = 0,
            speciesAmount = 150, movementAbility = 40,
            size = deer.baseSize, fertility = deer.baseFertility,
            fitTemperature = deer.baseFitTemperature,
            fitHumidity = deer.baseFitHumidity };
        var hunter = new PopulationData { species = wolf,
            ecologicalNiche = EcologicalNiche.Land, trophicLevel = 2,
            speciesAmount = 150, movementAbility = wolf.baseMovementAbility,
            size = wolf.baseSize, fertility = wolf.baseFertility,
            fitTemperature = wolf.baseFitTemperature,
            fitHumidity = wolf.baseFitHumidity };
        typeof(PopulationData).GetMethod("AssignPopulationId",
            PrivateInstance).Invoke(prey,
                new object[] { "movement-predator-deer" });
        typeof(PopulationData).GetMethod("AssignPopulationId",
            PrivateInstance).Invoke(hunter,
                new object[] { "movement-predator-wolf" });
        home.community.Add(prey);
        home.community.Add(hunter);
        SimulationController sim = Controller(scene, home);
        sim.SetEvolutionNetworkSpecies(new[] { deer, wolf });
        sim.SetAllowedEvolutionSpecies(new[] { deer, wolf });
        sim.SetMutationParameters(1, 1f, 1f);
        sim.SetMutationEnabled(true);
        sim.ConfigureDeterministicRun(20261020);
        MethodInfo pressureMethod = typeof(SimulationController).GetMethod(
            "PredatorMovementChance", BindingFlags.Static |
                BindingFlags.NonPublic);
        float offer = (float)pressureMethod.Invoke(null,
            new object[] { home, prey });
        Require(offer > 0f, "wolf must pressure deer movement");
        MethodInfo evolve = typeof(SimulationController).GetMethod(
            "EvolveCommunityNewRules", PrivateInstance);
        FieldInfo currentDay = typeof(SimulationController).GetField(
            "currentSimulationDay", PrivateInstance);
        int firstUp = -1, previous = prey.movementAbility;
        var snapshots = new List<string>();
        for (int day = 1; day <= 30; day++)
        {
            currentDay.SetValue(sim, day);
            evolve.Invoke(sim, new object[] { home });
            if (firstUp < 0 && prey.movementAbility > previous)
                firstUp = day;
            if (day == 1 || day == 3 || day == 7 || day == 14 || day == 30)
                snapshots.Add(day + ":" + prey.movementAbility);
            previous = prey.movementAbility;
        }
        Debug.Log("MOVEMENT_PREDATOR_SELECTION_RESULT chance=" +
            offer.ToString("F2") + " firstUp=" + firstUp +
            " values=" + string.Join(",", snapshots));
        Require(firstUp > 0, "predator movement failed to respond");
    }

    private static BlockInfo Cell(Scene scene, string name, int x, int y,
        bool water, int elevation)
    {
        GameObject owner = new GameObject(name);
        SceneManager.MoveGameObjectToScene(owner, scene);
        MapTileInstance tile = owner.AddComponent<MapTileInstance>();
        var serialized = new SerializedObject(tile);
        serialized.FindProperty("coordinate").vector2IntValue =
            new Vector2Int(x, y);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        BlockInfo block = owner.AddComponent<BlockInfo>();
        block.waterCoverage = water ? WaterCoverage.Lake : WaterCoverage.Land;
        block.elevation = elevation;
        block.temperature = block.humidity = 50;
        block.maxPlantBiomass = 1000000f;
        block.plantBiomass = 1000f;
        block.community = new List<PopulationData>();
        block.SetNeighbors(new List<BlockInfo>());
        return block;
    }

    private static void Link(BlockInfo a, BlockInfo b)
    {
        a.SetNeighbors(new List<BlockInfo>(a.Neighbors) { b });
        b.SetNeighbors(new List<BlockInfo>(b.Neighbors) { a });
    }

    private static SimulationController Controller(Scene scene,
        params BlockInfo[] blocks)
    {
        var owner = new GameObject("Unified movement regression");
        SceneManager.MoveGameObjectToScene(owner, scene);
        var sim = owner.AddComponent<SimulationController>();
        sim.SetBlocks(new List<BlockInfo>(blocks));
        return sim;
    }

    private static int Count(BlockInfo block, SpeciesData species)
    {
        int count = 0;
        foreach (PopulationData pop in block.community)
            if (pop != null && pop.species == species) count += pop.speciesAmount;
        return count;
    }

    private static void Require(bool condition, string reason)
    { if (!condition) throw new Exception(reason); }
}
#endif
