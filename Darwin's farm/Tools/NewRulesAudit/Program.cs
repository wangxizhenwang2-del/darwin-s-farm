using System;
using System.Collections.Generic;
using System.Linq;
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

    private static void Main()
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

        var starving = Pop(Species(0), 100);
        BlockInfo noFood = Block(0, 0, starving);
        Controller(noFood).SimulateBlock(noFood);
        Check(starving.speciesAmount == 40 && starving.deathsToday == 60,
            "starvation counted once without overload deaths");

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
        int splitEvents = 0;
        splitController.OnLocalSplitResolved += _ => splitEvents++;
        splitController.SimulateDay(1);
        Check(splitEvents == 1 && splitBlock.community.Count == 2 &&
            splitBlock.community.Sum(p => p.speciesAmount)
                == 5500 + splitSource.birthsToday - splitSource.deathsToday,
            "local split creates two groups and conserves population");

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
    }
}
