using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MutationV41BoundaryChecks
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void RunStockBatch()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            BlockInfo block = NewBlock(scene, "stock", 0, 100000f, 100000);
            SimulationController sim = NewSimulation(scene, block);
            PopulationData first = NewL0();
            PopulationData second = NewL0();
            block.community.Add(first);
            block.community.Add(second);
            MethodInfo record = typeof(SimulationController).GetMethod(
                "RecordActualStockGuidance", Instance);
            MethodInfo finalize = typeof(SimulationController).GetMethod(
                "FinalizeStockGuidance", Instance);
            MethodInfo chance = typeof(SimulationController).GetMethod(
                "StockGuidanceProbability", Instance);
            Check(record != null && finalize != null && chance != null,
                "stock transaction methods missing");
            record.Invoke(sim, new object[] { block, 100000f, 125000f, 1, 10 });
            finalize.Invoke(sim, new object[] { block, 11 });
            object transaction = typeof(BlockInfo).GetField("stockGuidance")
                .GetValue(block);
            Check(Get<int>(transaction, "firstDay") == 11 &&
                Get<int>(transaction, "lastDay") == 130 &&
                Get<int>(transaction, "cap") == 30 &&
                Mathf.Abs(Get<float>(transaction, "pmax") - .85f) < .0001f,
                "low stock boundaries");
            block.plantBiomass = 100000f;
            float p1 = (float)chance.Invoke(sim, new object[] { block, first, 11 });
            float p2 = (float)chance.Invoke(sim, new object[] { block, second, 12 });
            Check(p1 > .8f && p2 > 0f && p2 < p1,
                "stock memory must survive a rapid real stock burn");
            record.Invoke(sim, new object[] { block, 100000f, 150000f, 2, 20 });
            finalize.Invoke(sim, new object[] { block, 21 });
            Check(Get<int>(transaction, "firstDay") == 11 &&
                Get<int>(transaction, "lastDay") == 130 &&
                Get<int>(transaction, "cap") == 55,
                "same direction upgrades cap without restarting time");
            record.Invoke(sim, new object[] { block, 150000f, 125000f, -1, 25 });
            record.Invoke(sim, new object[] { block, 125000f, 150000f, 1, 25 });
            finalize.Invoke(sim, new object[] { block, 26 });
            Check(Get<int>(transaction, "direction") == 1 &&
                Get<int>(transaction, "firstDay") == 11,
                "same-day reversal with net zero must not restart");
            record.Invoke(sim, new object[] { block, 150000f, 125000f, -1, 27 });
            finalize.Invoke(sim, new object[] { block, 28 });
            Check(Get<int>(transaction, "direction") == -1 &&
                Get<int>(transaction, "firstDay") == 28 &&
                Get<int>(transaction, "lastDay") == 147,
                "net reverse begins one new round");
            Check((float)chance.Invoke(sim, new object[] { block, first, 148 }) == 0f,
                "expired round probability must be zero");
            MethodInfo directed = typeof(SimulationController).GetMethod(
                "TryDirectedMutation", Instance);
            Check(directed != null, "unified directed draw missing");
            FieldInfo dayField = typeof(SimulationController).GetField(
                "currentSimulationDay", Instance);
            record.Invoke(sim, new object[] { block, 100000f, 125000f, 1, 40 });
            finalize.Invoke(sim, new object[] { block, 41 });
            transaction.GetType().GetField("cap").SetValue(transaction, 1);
            transaction.GetType().GetField("pmax").SetValue(transaction, 1f);
            transaction.GetType().GetField("rho").SetValue(transaction, 1f);
            var sibling = ScriptableObject.CreateInstance<SpeciesData>();
            sibling.baseSize = 20;
            first.trackedEvolutionTarget = sibling;
            first.size = 25;
            second.size = 20;
            for (int day = 41; day <= 50; day++)
            {
                dayField.SetValue(sim, day);
                directed.Invoke(sim, new object[] { block, first });
                directed.Invoke(sim, new object[] { block, second });
            }
            Check(first.size == 26 && second.size == 21,
                "each enrolled L0 gets one bounded size gain, even past target protection");
            Check(first.stockGuidance.used == 1 && second.stockGuidance.used == 1,
                "stock cap is redeemed per population");
            PopulationData immigrant = NewL0();
            block.community.Add(immigrant);
            dayField.SetValue(sim, 51);
            Check(!(bool)directed.Invoke(sim, new object[] { block, immigrant }) &&
                immigrant.size == 20, "later immigrant has no old stock claim");
            Type transfer = typeof(SimulationController).Assembly.GetType(
                "PopulationTransfer", true);
            PopulationData copy = (PopulationData)transfer.GetMethod("CopyForMove")
                .Invoke(null, new object[] { first, 10 });
            Check(copy.stockGuidance.claimKey == first.stockGuidance.claimKey &&
                copy.stockGuidance.used == first.stockGuidance.used,
                "migration preserves the stock claim and redeemed count");
            UnityEngine.Object.DestroyImmediate(sibling);
            Debug.Log("Mutation V4.1 stock boundary PASS");
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

    public static void RunMovementBatch()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            BlockInfo home = NewBlock(scene, "home", 0, 10000f, 1000);
            BlockInfo flat = NewBlock(scene, "flat", 0, 100000f, 100000);
            BlockInfo slope = NewBlock(scene, "slope", 1, 100000f, 100000);
            BlockInfo cliff = NewBlock(scene, "cliff", 2, 100000f, 100000);
            home.SetNeighbors(new List<BlockInfo> { flat });
            flat.SetNeighbors(new List<BlockInfo> { home });
            slope.SetNeighbors(new List<BlockInfo> { home });
            cliff.SetNeighbors(new List<BlockInfo> { home });
            SimulationController sim = NewSimulation(scene, home, flat, slope, cliff);
            PopulationData population = NewL0();
            population.speciesAmount = 100;
            home.community.Add(population);
            MethodInfo chance = typeof(SimulationController).GetMethod(
                "MovementGuidanceProbability", Instance);
            Check(chance != null, "V4.1 movement opportunity method missing");
            float flatChance = (float)chance.Invoke(sim,
                new object[] { home, population, 1 });
            Check(Mathf.Abs(flatChance - .65f) < .0001f,
                "flat shared edge must offer 65 percent");
            population.movementAbility = 40;
            Check((float)chance.Invoke(sim,
                new object[] { home, population, 2 }) == 0f,
                "flat guidance stops at 40");
            population.movementAbility = 10;
            home.SetNeighbors(new List<BlockInfo> { slope });
            float slopeChance = (float)chance.Invoke(sim,
                new object[] { home, population, 3 });
            Check(Mathf.Abs(slopeChance - .85f) < .0001f,
                "slope shared edge must offer 85 percent");
            population.movementAbility = 75;
            Check((float)chance.Invoke(sim,
                new object[] { home, population, 4 }) == 0f,
                "slope guidance stops at 75");
            home.SetNeighbors(new List<BlockInfo> { cliff });
            population.movementAbility = 10;
            Check((float)chance.Invoke(sim,
                new object[] { home, population, 5 }) == 0f,
                "cliff has no movement candidate");
            home.SetNeighbors(new List<BlockInfo> { flat });
            population.movementAbility = 10;
            Check((float)chance.Invoke(sim,
                new object[] { home, population, 181 }) == 0f,
                "finished flat edge cannot restart on a one-point dip");
            MethodInfo directed = typeof(SimulationController).GetMethod(
                "TryDirectedMutation", Instance);
            FieldInfo dayField = typeof(SimulationController).GetField(
                "currentSimulationDay", Instance);
            Check(directed != null && dayField != null,
                "movement daily draw entry points missing");
            home.community.Clear();
            PopulationData flatRunner = NewL0();
            typeof(PopulationData).GetMethod("AssignPopulationId", Instance)
                .Invoke(flatRunner, new object[] { "v41-flat-runner" });
            home.community.Add(flatRunner);
            for (int day = 1; day <= 180; day++)
            {
                dayField.SetValue(sim, day);
                int before = flatRunner.movementAbility;
                directed.Invoke(sim, new object[] { home, flatRunner });
                Check(flatRunner.movementAbility >= before &&
                    flatRunner.movementAbility <= before + 1,
                    "flat movement takes at most one upward slot per day");
            }
            Check(flatRunner.movementAbility == 40,
                "flat trajectory must pass 31 and reach 40 in 180 days");
            home.SetNeighbors(new List<BlockInfo> { slope });
            home.community.Clear();
            PopulationData slopeRunner = NewL0();
            typeof(PopulationData).GetMethod("AssignPopulationId", Instance)
                .Invoke(slopeRunner, new object[] { "v41-slope-runner" });
            home.community.Add(slopeRunner);
            for (int day = 1; day <= 180; day++)
            {
                dayField.SetValue(sim, day);
                int before = slopeRunner.movementAbility;
                directed.Invoke(sim, new object[] { home, slopeRunner });
                Check(slopeRunner.movementAbility >= before &&
                    slopeRunner.movementAbility <= before + 1,
                    "slope movement takes at most one upward slot per day");
            }
            Check(slopeRunner.movementAbility == 75,
                "slope trajectory must pass 61 and reach 75 in 180 days");
            home.SetNeighbors(new List<BlockInfo> { flat });
            home.community.Clear();
            PopulationData protectedRunner = NewL0();
            typeof(PopulationData).GetMethod("AssignPopulationId", Instance)
                .Invoke(protectedRunner, new object[] { "v41-protected-runner" });
            var protectedTarget = ScriptableObject.CreateInstance<SpeciesData>();
            protectedTarget.baseMovementAbility = 15;
            protectedRunner.trackedEvolutionTarget = protectedTarget;
            home.community.Add(protectedRunner);
            for (int day = 1; day <= 180; day++)
            {
                dayField.SetValue(sim, day);
                directed.Invoke(sim, new object[] { home, protectedRunner });
            }
            Check(protectedRunner.movementAbility == 20,
                "tracked sibling target protection must stop flat guidance at 20");
            UnityEngine.Object.DestroyImmediate(protectedTarget);
            Debug.Log("Mutation V4.1 movement boundary PASS");
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

    public static void RunUnionBatch()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            string first = RunUnionTrace(scene);
            string second = RunUnionTrace(scene);
            Check(first == second, "seed 240 directed replay differs");
            Debug.Log("Mutation V4.1 union boundary PASS");
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

    public static void RunCliffRecoveryBatch()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            BlockInfo home = NewBlock(scene, "cliff-home", 0, 10000f, 1000);
            BlockInfo neighbor = NewBlock(scene, "cliff-neighbor", 0,
                100000f, 100000);
            home.SetNeighbors(new List<BlockInfo> { neighbor });
            neighbor.SetNeighbors(new List<BlockInfo> { home });
            SimulationController sim = NewSimulation(scene, home, neighbor);
            PopulationData runner = NewL0();
            runner.movementAbility = 38;
            typeof(PopulationData).GetMethod("AssignPopulationId", Instance)
                .Invoke(runner, new object[] { "v411-cliff-runner" });
            var target = ScriptableObject.CreateInstance<SpeciesData>();
            target.baseMovementAbility = 35;
            runner.trackedEvolutionTarget = target;
            home.community.Add(runner);
            MethodInfo chance = typeof(SimulationController).GetMethod(
                "MovementGuidanceProbability", Instance);
            MethodInfo offer = typeof(SimulationController).GetMethod(
                "OfferCliffRecovery", Instance);
            MethodInfo directed = typeof(SimulationController).GetMethod(
                "TryDirectedMutation", Instance);
            FieldInfo dayField = typeof(SimulationController).GetField(
                "currentSimulationDay", Instance);
            Check(chance != null && offer != null && directed != null &&
                dayField != null, "cliff recovery entry points missing");
            chance.Invoke(sim, new object[] { home, runner, 278 });
            Check(runner.neighborMovementRounds.Count == 1,
                "flat attraction round must exist");
            runner.neighborMovementRounds[0].guided = true;
            runner.neighborMovementRounds[0].anchorMovement = 10;
            neighbor.elevation = 1;
            neighbor.elevation = 2;
            typeof(SimulationController).GetField("simulationSeed", Instance)
                .SetValue(sim, 240);
            dayField.SetValue(sim, 279);
            for (int day = 279; day <= 398; day++)
            {
                dayField.SetValue(sim, day);
                directed.Invoke(sim, new object[] { home, runner });
                if (runner.movementAbility == 35) break;
            }
            Check(runner.movementAbility == 35 &&
                !runner.cliffMovementRecovery.active,
                "m38 must reach exact tracked m35 within 120 days");
            runner.cliffMovementRecovery.active = false;
            runner.neighborMovementRounds[0].recoveryConsumed = false;
            neighbor.elevation = 0;
            dayField.SetValue(sim, 320);
            directed.Invoke(sim, new object[] { home, runner });
            Check(!runner.cliffMovementRecovery.active,
                "same-day 0 to 2 to 0 must not start recovery");
            home.community.Clear();
            PopulationData second = NewL0();
            second.movementAbility = 38;
            second.trackedEvolutionTarget = target;
            typeof(PopulationData).GetMethod("AssignPopulationId", Instance)
                .Invoke(second, new object[] { "v411-cliff-runner-241" });
            home.community.Add(second);
            typeof(SimulationController).GetField("simulationSeed", Instance)
                .SetValue(sim, 241);
            chance.Invoke(sim, new object[] { home, second, 278 });
            second.neighborMovementRounds[0].guided = true;
            second.neighborMovementRounds[0].anchorMovement = 10;
            neighbor.elevation = 2;
            for (int day = 279; day <= 398; day++)
            {
                dayField.SetValue(sim, day);
                directed.Invoke(sim, new object[] { home, second });
                if (second.movementAbility == 35) break;
            }
            Check(second.movementAbility == 35,
                "seed 241 must also reach exact tracked m35");
            home.community.Clear();
            PopulationData neverGuided = NewL0();
            neverGuided.movementAbility = 38;
            home.community.Add(neverGuided);
            neighbor.elevation = 0;
            chance.Invoke(sim, new object[] { home, neverGuided, 278 });
            neighbor.elevation = 2;
            dayField.SetValue(sim, 279);
            directed.Invoke(sim, new object[] { home, neverGuided });
            Check(!neverGuided.cliffMovementRecovery.active,
                "never-effective cliff must not trigger recovery");
            UnityEngine.Object.DestroyImmediate(target);
            Debug.Log("Mutation V4.1.1 cliff recovery boundary PASS");
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

    public static void RunStockGateBatch()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            BlockInfo block = NewBlock(scene, "stock-gate", 0,
                100000f, 1000);
            SimulationController sim = NewSimulation(scene, block);
            var preySpecies = ScriptableObject.CreateInstance<SpeciesData>();
            preySpecies.trophicLevel = 0;
            var hunterSpecies = ScriptableObject.CreateInstance<SpeciesData>();
            hunterSpecies.trophicLevel = 1;
            hunterSpecies.baseSize = 30;
            hunterSpecies.baseMovementAbility = 35;
            hunterSpecies.baseFertility = 40;
            hunterSpecies.preySpecies.Add(preySpecies);
            PopulationData source = NewL0();
            source.species = preySpecies;
            source.speciesAmount = 1000;
            source.preyNetRenewalHistory.Add(-1000f);
            block.community.Add(source);
            MethodInfo support = typeof(SimulationController).GetMethod(
                "HasEvolutionFoodSupport", Instance);
            MethodInfo forecast = typeof(SimulationController).GetMethod(
                "ForecastStockPredatorFood", Instance);
            MethodInfo target = typeof(SimulationController).GetMethod(
                "TargetPopulation", BindingFlags.Static | Instance);
            Check(support != null && forecast != null && target != null,
                "stock food gate entry points missing");
            PopulationData candidate = (PopulationData)target.Invoke(null,
                new object[] { hunterSpecies, 10 });
            string before = sim.CanonicalModelHash();
            object first = forecast.Invoke(sim,
                new object[] { block, source, candidate, 10 });
            float energy = Get<float>(first, "energy");
            Check(Get<int>(first, "legalPreyStock") == 990 &&
                energy > 0f &&
                (bool)support.Invoke(sim, new object[] {
                    block, source, hunterSpecies, 10 }) &&
                sim.CanonicalModelHash() == before,
                "negative history must not zero live edible stock");
            var competitor = (PopulationData)target.Invoke(null,
                new object[] { hunterSpecies, 200 });
            source.speciesAmount = 30;
            object scarce = forecast.Invoke(sim,
                new object[] { block, source, candidate, 10 });
            block.community.Add(competitor);
            object contested = forecast.Invoke(sim,
                new object[] { block, source, candidate, 10 });
            Check(Get<float>(contested, "energy") <
                Get<float>(scarce, "energy"),
                "shared predators must reduce candidate catch");
            block.community.Remove(competitor);
            source.speciesAmount = 10;
            object empty = forecast.Invoke(sim,
                new object[] { block, source, candidate, 10 });
            Check(Get<int>(empty, "legalPreyStock") == 0 &&
                !(bool)support.Invoke(sim, new object[] {
                    block, source, hunterSpecies, 10 }),
                "zero living prey must reject");
            UnityEngine.Object.DestroyImmediate(preySpecies);
            UnityEngine.Object.DestroyImmediate(hunterSpecies);
            Debug.Log("Mutation V4.1.1 stock gate boundary PASS");
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

    public static void RunShortEcologyBatch()
    {
        var species = ScriptableObject.CreateInstance<SpeciesData>();
        species.name = "v411-short-herbivore";
        species.trophicLevel = 0;
        try
        {
            foreach (int seed in new[] { 240, 241 })
            {
                string first = RunShortEcologyCase(seed, species);
                string second = RunShortEcologyCase(seed, species);
                Check(first == second,
                    "120-day ecological replay changed for seed " + seed);
            }
            Debug.Log("Mutation V4.1.1 short ecology boundary PASS");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
        finally { UnityEngine.Object.DestroyImmediate(species); }
    }

    private static string RunShortEcologyCase(int seed, SpeciesData species)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            BlockInfo home = NewBlock(scene, "short-home", 0,
                200000f, 80000);
            BlockInfo neighbor = NewBlock(scene, "short-neighbor", 0,
                200000f, 120000);
            home.temperature = neighbor.temperature = 55;
            home.humidity = neighbor.humidity = 55;
            home.SetNeighbors(new List<BlockInfo> { neighbor });
            neighbor.SetNeighbors(new List<BlockInfo> { home });
            SimulationController sim = NewSimulation(scene, home, neighbor);
            PopulationData population = NewL0();
            population.species = species;
            population.speciesAmount = 2500;
            typeof(PopulationData).GetMethod("AssignPopulationId", Instance)
                .Invoke(population, new object[] { "v411-short-" + seed });
            home.community.Add(population);
            sim.ConfigureDeterministicRun(seed);
            sim.EnableNeighborMovementFormula = true;
            sim.SetEconomyEnabled(false);
            sim.SetPresetEvolutionParameters(false, 5, .1f, 10);
            sim.SetMutationParameters(1, 1f, 1f);
            sim.SetMutationEnabled(true);
            sim.SetMigrationParameters(true, 7, 100f, .8f, .5f, .2f, 7);
            home.plantBiomass += 25000f;
            typeof(SimulationController).GetMethod(
                "RecordActualStockGuidance", Instance).Invoke(sim,
                    new object[] { home, 200000f, 225000f, 1, 0 });
            typeof(SimulationController).GetMethod(
                "RecordActualRecoveryGuidance", Instance).Invoke(sim,
                    new object[] { home, 25000f, 0 });
            var changes = new Dictionary<string, int>();
            int migrations = 0;
            sim.OnMigrationResolved += result =>
            { if (result.Kind == PopulationTransitionKind.Migration) migrations++; };
            sim.OnEcologyEvent += (day, tile, member, kind, detail) =>
            {
                if (kind != "mutation_choice" || member == null ||
                    !detail.Contains("occupiedSlot=true")) return;
                string key = day + ":" + member.PopulationId;
                changes.TryGetValue(key, out int count);
                Check(count == 0, "more than one daily Bar mutation");
                changes[key] = count + 1;
            };
            for (int day = 1; day <= 120; day++) sim.SimulateDay(day);
            Check(population.size >= 20 && population.fertility >= 50 &&
                population.fitTemperature >= 50 &&
                population.fitHumidity >= 50,
                "stock, recovery or climate regressed");
            Debug.Log("V4.1.1 short seed=" + seed + " size=" +
                population.size + " fertility=" + population.fertility +
                " climate=" + population.fitTemperature + "/" +
                population.fitHumidity + " migrations=" + migrations);
            return sim.CanonicalModelHash();
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static string RunUnionTrace(Scene scene)
    {
        BlockInfo home = NewBlock(scene, "union-home", 0, 12500f, 1000);
        BlockInfo flat = NewBlock(scene, "union-flat", 0, 100000f, 100000);
        home.SetNeighbors(new List<BlockInfo> { flat });
        flat.SetNeighbors(new List<BlockInfo> { home });
        SimulationController sim = NewSimulation(scene, home, flat);
        PopulationData population = NewL0();
        typeof(PopulationData).GetMethod("AssignPopulationId", Instance)
            .Invoke(population, new object[] { "v41-union-runner" });
        home.community.Add(population);
        typeof(SimulationController).GetField("simulationSeed", Instance)
            .SetValue(sim, 240);
        typeof(SimulationController).GetMethod("RecordActualStockGuidance", Instance)
            .Invoke(sim, new object[] { home, 10000f, 12500f, 1, 0 });
        typeof(SimulationController).GetMethod("RecordActualRecoveryGuidance", Instance)
            .Invoke(sim, new object[] { home, 25000f, 0 });
        MethodInfo directed = typeof(SimulationController).GetMethod(
            "TryDirectedMutation", Instance);
        FieldInfo dayField = typeof(SimulationController).GetField(
            "currentSimulationDay", Instance);
        string trace = "";
        for (int day = 1; day <= 30; day++)
        {
            dayField.SetValue(sim, day);
            int oldM = population.movementAbility;
            int oldS = population.size;
            int oldF = population.fertility;
            directed.Invoke(sim, new object[] { home, population });
            int changes = Mathf.Abs(population.movementAbility - oldM) +
                Mathf.Abs(population.size - oldS) +
                Mathf.Abs(population.fertility - oldF);
            Check(changes <= 1, "joint directed lottery spent multiple slots");
            trace += population.movementAbility + "," + population.size +
                "," + population.fertility + ";";
        }
        Check(population.movementAbility > 10 && population.size > 20 &&
            population.fertility > 50,
            "joint lottery did not expose all three eligible sources");
        return trace;
    }

    private static BlockInfo NewBlock(Scene scene, string name, int elevation,
        float stock, int recovery)
    {
        GameObject owner = new GameObject(name);
        SceneManager.MoveGameObjectToScene(owner, scene);
        BlockInfo block = owner.AddComponent<BlockInfo>();
        block.waterCoverage = WaterCoverage.Land;
        block.elevation = elevation;
        block.temperature = block.humidity = 50;
        block.maxPlantBiomass = 1000000f;
        block.plantBiomass = stock;
        block.habitatRecovery = recovery;
        block.community = new List<PopulationData>();
        return block;
    }

    private static SimulationController NewSimulation(Scene scene,
        params BlockInfo[] blocks)
    {
        GameObject owner = new GameObject("V4.1 simulation");
        SceneManager.MoveGameObjectToScene(owner, scene);
        SimulationController sim = owner.AddComponent<SimulationController>();
        sim.SetBlocks(new List<BlockInfo>(blocks));
        return sim;
    }

    private static PopulationData NewL0() => new PopulationData
    {
        speciesAmount = 100, ecologicalNiche = EcologicalNiche.Land,
        trophicLevel = 0, movementAbility = 10, size = 20, fertility = 50,
        fitTemperature = 50, fitHumidity = 50
    };

    private static T Get<T>(object target, string name) =>
        (T)target.GetType().GetField(name).GetValue(target);

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("Mutation V4.1: " + message); }
}
