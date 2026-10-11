using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

internal static class Program
{
    private static SpeciesData Species(int level, int t = 50, int h = 50,
        int movement = 20, int size = 10, int fertility = 50)
        => new SpeciesData { trophicLevel = level,
            baseFitTemperature = t, baseFitHumidity = h,
            baseMovementAbility = movement, baseSize = size,
            baseFertility = fertility };

    private static PopulationData Pop(SpeciesData species, int count)
        => new PopulationData { species = species, speciesAmount = count,
            fitTemperature = species.baseFitTemperature,
            fitHumidity = species.baseFitHumidity,
            movementAbility = species.baseMovementAbility,
            size = species.baseSize, fertility = species.baseFertility,
            trophicLevel = species.trophicLevel,
            trophicLevelInitialized = true };

    private static BlockInfo Block(int recovery, float stock,
        params PopulationData[] populations) => new BlockInfo
    {
        temperature = 50, humidity = 50, habitatRecovery = recovery,
        plantBiomass = stock, maxPlantBiomass = 1000000,
        community = new List<PopulationData>(populations)
    };

    private static SimulationController Controller(params BlockInfo[] blocks)
    {
        var controller = new SimulationController();
        controller.SetMutationEnabled(false);
        controller.SetMigrationParameters(false, 7, 100, 0.8f, 0.5f, 0.2f, 7);
        controller.SetBlocks(new List<BlockInfo>(blocks));
        return controller;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    private static float Forecast(SimulationController simulation, BlockInfo block,
        PopulationData population, int temperature, int humidity, int movement,
        int size, int fertility)
    {
        Type traits = typeof(SimulationController).GetNestedType("LandTraits",
            BindingFlags.NonPublic);
        object values = Activator.CreateInstance(traits);
        foreach ((string name, float value) in new[] {
            ("t", (float)temperature), ("h", (float)humidity),
            ("m", (float)movement), ("s", (float)size),
            ("f", (float)fertility) })
            traits.GetField(name, BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance).SetValue(values, value);
        MethodInfo method = typeof(SimulationController).GetMethod(
            "ForecastLandGrowth", BindingFlags.NonPublic |
            BindingFlags.Instance);
        return (float)method.Invoke(simulation, new[] { block, population, values });
    }

    private static void Main(string[] args)
    {
        var defaults = new SimulationController();
        Check(Math.Abs(defaults.ReadLandRules().birthRate - 0.1f) < 1e-6f
            && defaults.ReadTuning().mutationInterval == 1
            && Math.Abs(defaults.ReadTuning().mutationStep - 1f) < 1e-6f,
            "requested defaults");

        SpeciesData chicken = Species(0, size: 8, fertility: 75);
        PopulationData hen = Pop(chicken, 5000);
        BlockInfo abundant = Block(125000, 1000000, hen);
        Controller(abundant).SimulateBlock(abundant);
        Check(hen.birthsToday >= 370 && hen.birthsToday <= 380,
            "birth rate 0.1 uses funded young demand");
        Check(abundant.consumedBiomassToday > 0 &&
            abundant.consumedBiomassToday <= 1000000 &&
            Math.Abs(abundant.plantBiomass + abundant.consumedBiomassToday
                - 1000000) < 0.1f, "one plant ledger conserves food");
        Check(Math.Abs(defaults.ReadLandRules().splitMinimumRenewalPressure
            - 0.9f) < 1e-6f, "split requires ninety percent of environmental K");

        var starving = Pop(Species(0), 100);
        BlockInfo noFood = Block(0, 0, starving);
        Controller(noFood).SimulateBlock(noFood);
        Check(starving.speciesAmount == 40 && starving.deathsToday == 60,
            "starvation counted once without overload deaths");
        var doomed = Pop(Species(0, t: 0, h: 0), 10);
        BlockInfo lethalBlock = Block(0, 0, doomed);
        var lethalSimulation = Controller(lethalBlock);
        var lethalRules = lethalSimulation.ReadLandRules();
        lethalRules.starvationDeathRate = 1f;
        lethalRules.climateMismatchDeathRate = 1f;
        lethalSimulation.ApplyLandRules(lethalRules);
        lethalSimulation.SimulateBlock(lethalBlock);
        Check(doomed.speciesAmount == 0 && doomed.deathRemainder < 1f,
            "lethal day cannot leave deferred deaths after all adults die");
        var shock = Pop(Species(0), 5000);
        BlockInfo drought = Block(0, 10000, shock);
        Controller(drought).SimulateBlock(drought);
        Check(drought.plantBiomass == 0f,
            "nonsteady drought can temporarily empty plant stock");
        drought.habitatRecovery = 100000;
        Controller(drought).SimulateBlock(drought);
        Check(drought.plantBiomass > 0f,
            "plant stock rebuilds after recovery resumes");

        var left = Pop(chicken, 1000);
        left.size = 10; left.fertility = 90;
        var right = Pop(chicken, 250);
        right.size = 38; right.fertility = 45;
        BlockInfo shared = Block(80000, 80000, left, right);
        Controller(shared).SimulateBlock(shared);
        Check(shared.consumedBiomassToday <= 80000.01f,
            "opposite strategies cannot duplicate plants");

        var splitSource = Pop(chicken, 5500);
        for (int i = 0; i < 10; i++) splitSource.foodSatisfactionHistory.Add(1f);
        BlockInfo splitBlock = Block(125000, 1000000, splitSource);
        var splitController = Controller(splitBlock);
        var splitRules = splitController.ReadLandRules();
        splitRules.splitMinimumRenewalPressure = 0.3f;
        splitController.ApplyLandRules(splitRules);
        int splitEvents = 0;
        splitController.OnLocalSplitResolved += _ => splitEvents++;
        splitController.SimulateDay(1);
        Check(splitEvents == 1 && splitBlock.community.Count == 2 &&
            splitBlock.community.Sum(p => p.speciesAmount)
                == 5500 + splitSource.birthsToday - splitSource.deathsToday,
            "local split creates two groups and conserves population");

        var currentGroup = Pop(Species(0, size: 10, fertility: 90), 630);
        var competingGroup = Pop(Species(0, size: 10, fertility: 50), 200);
        for (int i = 0; i < 10; i++)
            currentGroup.foodSatisfactionHistory.Add(1f);
        BlockInfo mixedBlock = Block(10000, 1000000,
            currentGroup, competingGroup);
        var mixedController = Controller(mixedBlock);
        var mixedRules = mixedController.ReadLandRules();
        mixedRules.splitHeadroomUse = 1f;
        mixedController.ApplyLandRules(mixedRules);
        int mixedSplits = 0;
        mixedController.OnLocalSplitResolved += _ => mixedSplits++;
        mixedController.SimulateDay(1);
        float mixedDemand = mixedBlock.community.Sum(p =>
            LandLifeRules.Demand(p, p.speciesAmount,
                LandLifeRules.Fitness(mixedBlock, p.fitTemperature,
                    p.fitHumidity), mixedRules));
        Check(mixedDemand >= 9000f && mixedSplits == 0 &&
            currentGroup.speciesAmount <
                .9f * currentGroup.environmentalCarryingCapacity,
            "other consumers cannot make current group reach ninety percent K");
        currentGroup.speciesAmount = 720;
        mixedController.SimulateDay(2);
        Check(mixedSplits == 1,
            "current group may split after its own count reaches ninety percent K");

        SpeciesData deer = Species(0, 40, 25, 75, 60, 30);
        chicken.evolutionTargets.Add(deer);
        var candidate = Pop(chicken, 200);
        candidate.fitTemperature = deer.baseFitTemperature;
        candidate.fitHumidity = deer.baseFitHumidity;
        candidate.movementAbility = deer.baseMovementAbility;
        candidate.size = deer.baseSize;
        candidate.fertility = deer.baseFertility;
        candidate.localSplitBranch = true;
        var sibling = Pop(chicken, 200);
        sibling.localSplitBranch = true;
        var conversionBlock = Block(1000000, 1000000, candidate, sibling);
        conversionBlock.temperature = 40;
        conversionBlock.humidity = 25;
        var evolution = Controller(conversionBlock);
        evolution.SetEvolutionNetworkSpecies(new[] { chicken, deer });
        int conversionEvents = 0;
        evolution.OnPresetEvolutionResolved += _ => conversionEvents++;
        for (int day = 1; day <= 5; day++) evolution.SimulateDay(day);
        Check(conversionEvents == 1 && conversionBlock.community.Count == 2
            && candidate.species == deer,
            "five qualified days convert an entire local branch");

        var incoming = Pop(chicken, 30);
        var residentA = Pop(chicken, 20); residentA.localSplitBranch = true;
        var residentB = Pop(chicken, 15); residentB.localSplitBranch = true;
        var unrelated = Pop(deer, 12);
        BlockInfo source = Block(10000, 10000, incoming);
        BlockInfo target = Block(10000, 10000, residentA, residentB, unrelated);
        var migration = Controller(source, target);
        migration.MovePopulation(source, target, incoming, 1);
        Check(target.community.Count == 2 && unrelated.speciesAmount == 12
            && target.community.Single(p => p.species == chicken).speciesAmount == 65,
            "arrival collapses only the migrant's same-species branches");

        var mover = Pop(chicken, 300);
        mover.movementAbility = 100;
        var residentChicken = Pop(chicken, 30);
        BlockInfo attractionSource = Block(100000, 1000000, mover);
        BlockInfo attractionTarget = Block(100000, 1000000, residentChicken);
        attractionSource.SetNeighbors(new List<BlockInfo> { attractionTarget });
        var attractionSimulation = Controller(attractionSource, attractionTarget);
        var attractionMethod = typeof(SimulationController).GetMethod(
            "BestNeighborAttraction",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        object[] attractionArgs = { attractionSource, mover, 0 };
        float occupiedAttraction = (float)attractionMethod.Invoke(
            attractionSimulation, attractionArgs);
        attractionTarget.community.Clear();
        float emptyAttraction = (float)attractionMethod.Invoke(
            attractionSimulation, attractionArgs);
        attractionTarget.community.Add(Pop(deer, 10));
        float otherSpeciesAttraction = (float)attractionMethod.Invoke(
            attractionSimulation, attractionArgs);
        Check(occupiedAttraction == 0f && emptyAttraction > 0.2f &&
            otherSpeciesAttraction > 0.2f,
            "same-species neighbor gives no movement attraction");
        mover.fitTemperature = 0;
        mover.fitHumidity = 0;
        float hostileAttraction = (float)attractionMethod.Invoke(
            attractionSimulation, attractionArgs);
        Check(hostileAttraction < otherSpeciesAttraction,
            "climate mismatch cannot increase migration attraction");
        mover.fitTemperature = chicken.baseFitTemperature;
        mover.fitHumidity = chicken.baseFitHumidity;
        attractionTarget.community.Clear();
        attractionTarget.community.Add(residentChicken);
        attractionSimulation.SetMigrationParameters(true, 1, 100, .8f,
            .5f, .2f, 0);
        int naturalArrivals = 0;
        attractionSimulation.OnMigrationResolved += _ => naturalArrivals++;
        attractionSimulation.SimulateDay(1);
        Check(naturalArrivals == 0 &&
            attractionTarget.community.Count == 1,
            "natural migration skips same-species destination");

        SpeciesData civet = Species(1, size: 20, fertility: 60);
        SpeciesData lion = Species(2, size: 80, fertility: 20);
        civet.preySpecies.Add(chicken);
        lion.preySpecies.Add(chicken);
        var victim = Pop(chicken, 100);
        var hunter1 = Pop(civet, 20);
        var hunter2 = Pop(lion, 10);
        var huntBlock = Block(10000, 10000, victim, hunter1, hunter2);
        Controller(huntBlock).SimulateBlock(huntBlock);
        Check(victim.predationDeathsToday <= 100 && victim.speciesAmount >= 0
            && victim.speciesAmount == 100 + victim.birthsToday - victim.deathsToday,
            "L2 and L1 consume one prey ledger before newborns arrive");

        var firstPrey = Pop(chicken, 1000);
        var firstHunter = Pop(civet, 20);
        var preyFirst = Block(100000, 1000000, firstPrey, firstHunter);
        var secondPrey = Pop(chicken, 1000);
        var secondHunter = Pop(civet, 20);
        var hunterFirst = Block(100000, 1000000, secondHunter, secondPrey);
        Controller(preyFirst).SimulateBlock(preyFirst);
        Controller(hunterFirst).SimulateBlock(hunterFirst);
        Check(Math.Abs(firstHunter.renewalSatisfactionHistory[^1] -
            secondHunter.renewalSatisfactionHistory[^1]) < 0.0001f,
            "prey renewal history is independent of community list order");

        var sharedPrey = Pop(chicken, 1000);
        sharedPrey.preyNetRenewalHistory.Add(20f);
        var lowerHunter = Pop(civet, 20);
        var upperHunter = Pop(lion, 10);
        var sharedPreyBlock = Block(100000, 1000000,
            sharedPrey, lowerHunter, upperHunter);
        float grossUpperSupply = 20f * LandLifeRules.PreyEnergy(upperHunter,
            sharedPrey);
        float upperSupply = LandLifeRules.AvailableSustainablePreyFood(
            sharedPreyBlock, upperHunter, defaults.ReadLandRules());
        Check(upperSupply < grossUpperSupply && upperSupply >= 0f,
            "L2 cannot claim prey renewal already needed by L1");

        var saturatedSmall = Pop(Species(0, 68, 68, 2, 8, 90), 9540);
        var saturatedLarge = Pop(Species(0, 68, 68, 2, 38, 60), 335);
        BlockInfo saturatedTile = Block(100000, 904890,
            saturatedSmall, saturatedLarge);
        saturatedTile.temperature = saturatedTile.humidity = 68;
        var saturatedSimulation = Controller(saturatedTile);
        saturatedSimulation.SimulateBlock(saturatedTile);
        float matched = Forecast(saturatedSimulation, saturatedTile,
            saturatedSmall, 68, 68, 2, 8, 90);
        float colder = Forecast(saturatedSimulation, saturatedTile,
            saturatedSmall, 67, 68, 2, 8, 90);
        float drier = Forecast(saturatedSimulation, saturatedTile,
            saturatedSmall, 68, 67, 2, 8, 90);
        Check(matched > colder && matched > drier,
            "saturated herbivore cannot gain by lowering climate fitness");
        saturatedSmall.fitTemperature = 64;
        float towardClimate = Forecast(saturatedSimulation, saturatedTile,
            saturatedSmall, 65, 68, 2, 8, 90);
        float awayFromClimate = Forecast(saturatedSimulation, saturatedTile,
            saturatedSmall, 63, 68, 2, 8, 90);
        Check(towardClimate > awayFromClimate,
            "mismatched herbivore favors local climate under food pressure");
        saturatedSmall.fitTemperature = 68;
        saturatedSimulation.SetMutationEnabled(true);
        for (int day = 1; day <= 120; day++)
            saturatedSimulation.SimulateBlock(saturatedTile);
        Check(saturatedSmall.fitTemperature == 68 &&
            saturatedSmall.fitHumidity == 68 &&
            saturatedLarge.fitTemperature == 68 &&
            saturatedLarge.fitHumidity == 68,
            "constant climate retains matching traits near carrying capacity");

        var huntingPrey = Pop(chicken, 300);
        var huntingPredator = Pop(civet, 20);
        BlockInfo huntingTile = Block(100000, 1000000,
            huntingPrey, huntingPredator);
        var huntingSimulation = Controller(huntingTile);
        huntingSimulation.SimulateBlock(huntingTile);
        float hunterBaseline = Forecast(huntingSimulation, huntingTile,
            huntingPredator, 50, 50, 20, 20, 60);
        float slowerHunter = Forecast(huntingSimulation, huntingTile,
            huntingPredator, 50, 50, 19, 20, 60);
        Check(hunterBaseline > slowerHunter,
            "predator forecast includes lost hunting from slower movement");

        // Re-run the supplied 19:12 report from its first exported population.
        var reportedChicken = Pop(Species(0, 64, 50, 20, 8, 75), 187);
        BlockInfo reportedTile = Block(100000, 395726.375f,
            reportedChicken);
        reportedTile.temperature = reportedTile.humidity = 68;
        var reportReplay = new SimulationController();
        reportReplay.SetBlocks(new List<BlockInfo> { reportedTile });
        reportReplay.SetMigrationParameters(false, 7, 100, .8f,
            .5f, .2f, 7);
        for (int day = 4; day <= 366; day++) reportReplay.SimulateDay(day);
        Check(reportedTile.community.Count == 2 &&
            reportedTile.community.All(p => p.fitTemperature == 68 &&
                p.fitHumidity == 68 &&
                p.environmentalFitness == Mathf.RoundToInt(100f *
                    LandLifeRules.Fitness(reportedTile,
                        p.fitTemperature, p.fitHumidity))) &&
            reportedTile.plantBiomass >= 100000f,
            "report replay adapts to stable 68/68 without reverse drift");

        var starter = Pop(chicken, 160);
        BlockInfo grass = Block(100000, 100000, starter);
        grass.temperature = 68; grass.humidity = 68;
        BlockInfo forest = Block(125000, 125000);
        forest.temperature = 58; forest.humidity = 50;
        grass.SetNeighbors(new List<BlockInfo> { forest });
        forest.SetNeighbors(new List<BlockInfo> { grass });
        var firstStage = new SimulationController();
        firstStage.SetBlocks(new List<BlockInfo> { grass, forest });
        firstStage.SetMigrationParameters(false, 7, 100, 0.8f, 0.5f, 0.2f, 7);
        int splitDay = 0;
        firstStage.OnLocalSplitResolved += result => splitDay = result.Day;
        for (int day = 1; day <= 200; day++) firstStage.SimulateDay(day);
        Console.WriteLine("STARTER day200 groups=" + grass.community.Count
            + " count=" + grass.community.Sum(p => p.speciesAmount)
            + " splitDay=" + splitDay + " stock=" + grass.plantBiomass
            + " traits=" + string.Join(";", grass.community.Select(p =>
                p.fitTemperature + "/" + p.fitHumidity + "/"
                + p.movementAbility + "/" + p.size + "/" + p.fertility)));

        // Reproduce the supplied day-192 whitebox case with its initial traits.
        foreach (float birthRate in new[] { 0.1f, 0.05f })
        {
            var sample = Pop(Species(0, 62, 51, 19, 10, 76), 160);
            BlockInfo tile = Block(100000, 100000, sample);
            tile.temperature = 68; tile.humidity = 68;
            var sim = new SimulationController();
            sim.SetBlocks(new List<BlockInfo> { tile });
            sim.SetMigrationParameters(false, 7, 100, .8f, .5f, .2f, 7);
            var rules = sim.ReadLandRules();
            rules.birthRate = birthRate;
            sim.ApplyLandRules(rules);
            int split = 0;
            sim.OnLocalSplitResolved += result => split = result.Day;
            float minimumAfterSplit = float.MaxValue;
            int largestDailyChanges = 0;
            for (int day = 1; day <= 192; day++)
            {
                var before = tile.community.ToDictionary(p => p.PopulationId,
                    p => new[] { p.fitTemperature, p.fitHumidity,
                        p.movementAbility, p.size, p.fertility });
                sim.SimulateDay(day);
                foreach (var p in tile.community)
                    if (before.TryGetValue(p.PopulationId, out int[] prior))
                    {
                        int[] now = { p.fitTemperature, p.fitHumidity,
                            p.movementAbility, p.size, p.fertility };
                        int changed = Enumerable.Range(0, 5).Count(i => now[i] != prior[i]);
                        largestDailyChanges = Math.Max(largestDailyChanges, changed);
                    }
                if (split > 0) minimumAfterSplit = Math.Min(minimumAfterSplit,
                    tile.plantBiomass);
                if (day == 40 || day == 60 || day == 80 || day == 120 ||
                    day == 160 || day == 192)
                    Console.WriteLine("WHITEBOX birth=" + birthRate + " day=" + day
                        + " stock=" + Math.Round(tile.plantBiomass)
                        + " " + string.Join(";", tile.community.Select(p =>
                            p.speciesAmount + "/" + p.size + "/" + p.fertility)));
            }
            Check(split > 0 && tile.community.Count == 2 &&
                largestDailyChanges <= 1 &&
                tile.community.Min(p => p.splitStrategy) == -1 &&
                tile.community.Max(p => p.splitStrategy) == 1,
                "whitebox split persists with one trait mutated per day");
            Check(minimumAfterSplit >= rules.minimumPlantReserveFraction *
                tile.maxPlantBiomass - 0.1f,
                "whitebox plant stock stays above reserve");
            PopulationData largeBranch = tile.community.Single(p =>
                p.splitStrategy > 0);
            PopulationData smallBranch = tile.community.Single(p =>
                p.splitStrategy < 0);
            Check(largeBranch.size > smallBranch.size &&
                largeBranch.fertility < smallBranch.fertility &&
                largeBranch.speciesAmount < smallBranch.speciesAmount / 3 &&
                !string.IsNullOrEmpty(largeBranch.lastMutationEffect),
                "whitebox branches retain different traits and plateaus");
            Console.WriteLine("WHITEBOX birth=" + birthRate + " split=" + split);
        }

        if (args.Contains("--route-smoke"))
        {
            SpeciesData routeChicken = Species(0, 61, 50, 20, 8, 75);
            SpeciesData routeDeer = Species(0, 40, 25, 75, 60, 30);
            routeChicken.evolutionTargets.Add(routeDeer);
            PopulationData founder = Pop(routeChicken, 160);
            BlockInfo home = Block(125000, 1000000, founder);
            home.temperature = 58; home.humidity = 50; home.elevation = 1;
            BlockInfo nearby = Block(125000, 1000000);
            nearby.temperature = 58; nearby.humidity = 50; nearby.elevation = 0;
            home.SetNeighbors(new List<BlockInfo> { nearby });
            nearby.SetNeighbors(new List<BlockInfo> { home });
            var route = new SimulationController();
            route.SetBlocks(new List<BlockInfo> { home, nearby });
            route.SetEvolutionNetworkSpecies(new[] { routeChicken, routeDeer });
            route.SetMigrationParameters(false, 7, 100, .8f,
                .5f, .2f, 7);
            var routeRules = route.ReadLandRules();
            int routeRecovery = 106250;
            int routeTemperature = 33, routeHumidity = 25;
            foreach (string option in args)
            {
                if (option.StartsWith("--climate="))
                    routeRules.climateMismatchDeathRate = float.Parse(
                        option.Substring(10),
                        System.Globalization.CultureInfo.InvariantCulture);
                if (option.StartsWith("--cold="))
                    routeRules.coldDeathRate = float.Parse(
                        option.Substring(7),
                        System.Globalization.CultureInfo.InvariantCulture);
                if (option.StartsWith("--recovery="))
                    routeRecovery = int.Parse(option.Substring(11),
                        System.Globalization.CultureInfo.InvariantCulture);
                if (option.StartsWith("--stress="))
                    routeRules.reproductionStressDeathRate = float.Parse(
                        option.Substring(9),
                        System.Globalization.CultureInfo.InvariantCulture);
                if (option.StartsWith("--temperature="))
                    routeTemperature = int.Parse(option.Substring(14),
                        System.Globalization.CultureInfo.InvariantCulture);
                if (option.StartsWith("--humidity="))
                    routeHumidity = int.Parse(option.Substring(11),
                        System.Globalization.CultureInfo.InvariantCulture);
            }
            route.ApplyLandRules(routeRules);
            int routeSplit = 0, routeDeerDay = 0;
            route.OnLocalSplitResolved += result => routeSplit = result.Day;
            route.OnPresetEvolutionResolved += result =>
            {
                if (result.TargetSpecies == routeDeer)
                    routeDeerDay = result.Day;
            };
            for (int day = 1; day <= 900 && routeDeerDay == 0; day++)
            {
                if (routeSplit > 0)
                {
                    int elapsed = Math.Min(25, day - routeSplit);
                    home.temperature = nearby.temperature = 58 -
                        Math.Min(58 - routeTemperature, elapsed);
                    home.humidity = nearby.humidity = 50 -
                        Math.Min(50 - routeHumidity, elapsed);
                    home.habitatRecovery = routeRecovery;
                }
                route.SimulateDay(day);
                if (day % 100 == 0)
                    Console.WriteLine("ROUTE day=" + day + " split=" + routeSplit
                        + " deer=" + routeDeerDay + " stock=" +
                        Math.Round(home.plantBiomass) + " " +
                        string.Join(";", home.community.Select(p =>
                            p.speciesAmount + ":" + p.fitTemperature + "/"
                            + p.fitHumidity + "/" + p.movementAbility + "/"
                            + p.size + "/" + p.fertility)));
            }
            Console.WriteLine("ROUTE RESULT split=" + routeSplit +
                " deer=" + routeDeerDay + " " +
                string.Join(";", home.community.Select(p =>
                    (p.species == routeDeer ? "deer" : "chicken") + ":"
                    + p.speciesAmount + "-" + p.fitTemperature + "/"
                    + p.fitHumidity + "/" + p.movementAbility + "/"
                    + p.size + "/" + p.fertility)));
            if (args.Contains("--expect-deer"))
                Check(routeDeerDay > 0 && routeDeerDay <= 900,
                    "configured chicken route produces deer");
            return;
        }
        if (!args.Contains("--sweep")) return;
        foreach (float rate in new[] { 0.1f, 0.02f, 0.005f })
        {
            var founder = Pop(chicken, 160);
            BlockInfo home = Block(125000, 125000, founder);
            home.temperature = 58; home.humidity = 50;
            home.elevation = 1;
            BlockInfo neighbor = Block(125000, 1000000);
            neighbor.temperature = 58; neighbor.humidity = 50;
            neighbor.elevation = 0;
            home.SetNeighbors(new List<BlockInfo> { neighbor });
            var sim = new SimulationController();
            sim.SetBlocks(new List<BlockInfo> { home, neighbor });
            sim.SetEvolutionNetworkSpecies(new[] { chicken, deer });
            sim.SetMigrationParameters(false, 7, 100, 0.8f, 0.5f, 0.2f, 7);
            var tuning = sim.ReadLandRules(); tuning.birthRate = rate;
            if (rate < 0.1f) tuning.splitMinimumRenewalPressure = 0.1f;
            sim.ApplyLandRules(tuning);
            int branching = 0, evolved = 0;
            sim.OnLocalSplitResolved += result => branching = result.Day;
            sim.OnPresetEvolutionResolved += result => evolved = result.Day;
            for (int day = 1; day <= 900; day++)
            {
                if (branching > 0)
                {
                    int elapsed = day - branching;
                    home.temperature = 58 - Math.Min(25, elapsed);
                    home.humidity = 50 - Math.Min(25, elapsed);
                    home.habitatRecovery = 106250;
                }
                sim.SimulateDay(day);
                if (day == 100 || day == 200 || day == 350 ||
                    day == 500 || day == 700 || day == 900)
                    Console.WriteLine("ROUTE rate=" + rate + " day=" + day
                        + " split=" + branching + " deer=" + evolved
                        + " stock=" + home.plantBiomass + " "
                        + string.Join(";", home.community.Select(p =>
                            (p.species == deer ? "deer" : "chicken") + ":"
                            + p.speciesAmount + "-" + p.fitTemperature + "/"
                            + p.fitHumidity + "/" + p.movementAbility
                            + "/" + p.size + "/" + p.fertility)));
            }
        }

        var largeTrial = Pop(chicken, 1146);
        largeTrial.size = 38; largeTrial.fertility = 45;
        largeTrial.movementAbility = 70; largeTrial.localSplitBranch = true;
        var smallTrial = Pop(chicken, 4354);
        smallTrial.size = 10; smallTrial.fertility = 90;
        smallTrial.movementAbility = 70; smallTrial.localSplitBranch = true;
        BlockInfo coldTrial = Block(106250, 1000000, largeTrial, smallTrial);
        coldTrial.temperature = 33; coldTrial.humidity = 25;
        coldTrial.elevation = 1;
        BlockInfo coldNeighbor = Block(106250, 1000000);
        coldNeighbor.elevation = 0;
        coldNeighbor.temperature = 33; coldNeighbor.humidity = 25;
        coldTrial.SetNeighbors(new List<BlockInfo> { coldNeighbor });
        var exactTrial = Controller(coldTrial);
        exactTrial.SetMutationEnabled(true);
        exactTrial.SetMutationParameters(5, 1f, 1f);
        var exactTuning = exactTrial.ReadLandRules(); exactTuning.birthRate = 0.005f;
        exactTrial.ApplyLandRules(exactTuning);
        exactTrial.SetEvolutionNetworkSpecies(new[] { chicken, deer });
        for (int day = 1; day <= 130; day++)
        {
            exactTrial.SimulateDay(day);
            if (day == 30 || day == 60 || day == 100 || day == 130)
                Console.WriteLine("EXACT day=" + day + " stock=" + coldTrial.plantBiomass
                    + " " + string.Join(";", coldTrial.community.Select(p =>
                        (p.species == deer ? "deer" : "chicken") + ":"
                        + p.speciesAmount + "-" + p.fitTemperature + "/"
                        + p.fitHumidity + "/" + p.movementAbility + "/"
                        + p.size + "/" + p.fertility)));
        }

        foreach (float rate in new[] { 0.1f, 0.02f, 0.005f })
        foreach (float cold in new[] { 0.005f, 0.02f, 0.05f, 0.1f })
        foreach (float opposite in new[] { 0.5f, 0.1f, 0f })
        foreach (float competition in new[] { 2f, 5f, 10f })
        foreach (int interval in new[] { 1, 5 })
        {
            SpeciesData routeChicken = Species(0, 61, 50, 20, 8, 75);
            routeChicken.evolutionTargets.Add(deer);
            PopulationData routeFounder = Pop(routeChicken, 160);
            BlockInfo routeHome = Block(125000, 1000000, routeFounder);
            routeHome.temperature = 58; routeHome.humidity = 50;
            routeHome.elevation = 1;
            BlockInfo routeNeighbor = Block(125000, 1000000);
            routeNeighbor.temperature = 58; routeNeighbor.humidity = 50;
            routeNeighbor.elevation = 0;
            routeHome.SetNeighbors(new List<BlockInfo> { routeNeighbor });
            var routeController = Controller(routeHome);
            routeController.SetMutationEnabled(true);
            routeController.SetMutationParameters(interval, 1f, 1f);
            routeController.SetEvolutionNetworkSpecies(new[] { routeChicken, deer });
            var rules = routeController.ReadLandRules();
            rules.birthRate = rate; rules.coldDeathRate = cold;
            rules.oppositeStrategyCompetition = opposite;
            rules.competitionPressure = competition;
            rules.splitMinimumRenewalPressure = 0.1f;
            routeController.ApplyLandRules(rules);
            int splitAt = 0, deerAt = 0;
            routeController.OnLocalSplitResolved += result => splitAt = result.Day;
            routeController.OnPresetEvolutionResolved += result => deerAt = result.Day;
            for (int day = 1; day <= 3600 && deerAt == 0; day++)
            {
                if (splitAt > 0)
                {
                    routeHome.temperature = 33;
                    routeHome.humidity = 25;
                    routeHome.habitatRecovery = day - splitAt < 100
                        ? 200000 : 90000;
                    routeNeighbor.temperature = 33;
                    routeNeighbor.humidity = 25;
                }
                routeController.SimulateDay(day);
                if (rate == 0.005f && cold == 0.1f && opposite == 0f
                    && competition == 10f && interval == 5 &&
                    (day == 200 || day == 300 || day == 500 || day == 550
                        || day == 600 || day == 700 || day == 900))
                    Console.WriteLine("SWEEPTRACE day=" + day + " split=" + splitAt
                        + " stock=" + routeHome.plantBiomass + " "
                        + string.Join(";", routeHome.community.Select(p =>
                            p.speciesAmount + "-" + p.fitTemperature + "/"
                            + p.fitHumidity + "/" + p.movementAbility + "/"
                            + p.size + "/" + p.fertility)));
            }
            if (deerAt > 0) Console.WriteLine("SWEEP deer=" + deerAt
                + " split=" + splitAt + " birth=" + rate + " cold=" + cold
                + " opposite=" + opposite + " competition=" + competition
                + " interval=" + interval);
        }
    }
}
