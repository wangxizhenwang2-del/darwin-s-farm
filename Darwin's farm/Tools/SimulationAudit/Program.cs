using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

static class Program
{
    static PopulationData Pop(int level, int amount, int size = 10, int movement = 10, int fertility = 50)
        => new PopulationData
        {
            species = new SpeciesData { trophicLevel = level }, speciesAmount = amount,
            fitTemperature = 50, fitHumidity = 50, size = size,
            movementAbility = movement, fertility = fertility, habitatNiche = 50,
            trophicLevel = level, trophicLevelInitialized = true
        };

    static BlockInfo Block(int recovery, float stock, params PopulationData[] populations)
        => new BlockInfo
        {
            temperature = 50, humidity = 50, habitatRecovery = recovery,
            plantBiomass = stock, maxPlantBiomass = Math.Max(stock, 10000f),
            community = new List<PopulationData>(populations)
        };

    static SimulationController Controller(bool mutation = false)
    {
        var c = new SimulationController();
        c.SetParameters(0.05f, 0.1f, 0.6f);
        c.SetMutationParameters(7, mutation ? 2f : 0f, 1f);
        return c;
    }

    static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    static float[] Traits(PopulationData p) => new[]
    {
        p.fitTemperature + p.temperatureMutationRemainder,
        p.fitHumidity + p.humidityMutationRemainder,
        p.movementAbility + p.movementMutationRemainder,
        p.size + p.sizeMutationRemainder,
        p.fertility + p.fertilityMutationRemainder,
        p.trophicLevel + p.trophicMutationRemainder
    };

    static int ChangedTraits(float[] before, PopulationData after)
    {
        var values = Traits(after);
        return Enumerable.Range(0, values.Length)
            .Count(i => Math.Abs(values[i] - before[i]) > 0.0001f);
    }

    static void MigrationMergeTests()
    {
        var species = new SpeciesData { trophicLevel = 0 };
        var resident = Pop(0, 20);
        resident.species = species;
        resident.lineageName = "右侧名称";
        resident.fitTemperature = 41;
        resident.temperatureMutationRemainder = 0.4f;
        var migrant = Pop(0, 30);
        migrant.species = species;
        migrant.lineageName = "左侧名称";
        migrant.fitTemperature = 62;
        migrant.temperatureMutationRemainder = -0.25f;
        var left = Block(1000, 10000f, migrant);
        var right = Block(1000, 10000f, resident);
        var controller = Controller();
        controller.SetMigrationParameters(true, 1, 1f, 0.8f, 1f, 1f, 0);
        controller.SetBlocks(new List<BlockInfo> { left, right });
        controller.MovePopulation(left, right, migrant, 0);
        float expectedTemperature = (20f * 41.4f + 30f * 61.75f) / 50f;
        if (right.community.Count != 1 || right.community[0] != resident ||
            resident.speciesAmount != 50 || left.community.Count != 0 ||
            Math.Abs(resident.fitTemperature + resident.temperatureMutationRemainder
                - expectedTemperature) > 0.001f)
            throw new Exception("same asset did not merge count and precise weighted trait");

        resident = Pop(0, 20);
        resident.species = null;
        resident.lineageName = "同族";
        migrant = Pop(0, 30);
        migrant.species = null;
        migrant.lineageName = "同族";
        left = Block(1000, 10000f, migrant);
        right = Block(1000, 10000f, resident);
        controller = Controller();
        controller.MovePopulation(left, right, migrant, 0);
        if (right.community.Count != 1 || resident.speciesAmount != 50)
            throw new Exception("same unnamed-asset lineage did not merge");

        migrant = Pop(0, 30);
        migrant.species = null;
        migrant.lineageName = "另一族";
        left = Block(1000, 10000f, migrant);
        controller.MovePopulation(left, right, migrant, 0);
        if (right.community.Count != 2 || resident.speciesAmount != 50)
            throw new Exception("different lineage was incorrectly merged");

        var namedResident = Pop(0, 20);
        namedResident.lineageName = "同名";
        var namedMigrant = Pop(0, 30);
        namedMigrant.lineageName = "同名";
        left = Block(1000, 10000f, namedMigrant);
        right = Block(1000, 10000f, namedResident);
        controller.MovePopulation(left, right, namedMigrant, 0);
        if (right.community.Count != 2)
            throw new Exception("distinct species assets merged only because names matched");

        var sourcePopulation = Pop(0, 50);
        species = sourcePopulation.species;
        resident = Pop(0, 10);
        resident.species = species;
        left = Block(0, 0f, sourcePopulation);
        right = Block(10000, 10000f, resident);
        left.SetNeighbors(new List<BlockInfo> { right });
        right.SetNeighbors(new List<BlockInfo> { left });
        controller = Controller();
        controller.SetParameters(0f, 0f, 0f);
        controller.SetMigrationParameters(true, 1, 1f, 0.8f, 1f, 1f, 0);
        controller.SetBlocks(new List<BlockInfo> { left, right });
        controller.SimulateDay(1);
        if (left.community[0].speciesAmount != 50 || right.community[0].speciesAmount != 10)
            throw new Exception("overload migrated into an occupied same-species block");

        right.community.Clear();
        controller.SimulateDay(2);
        if (right.community.Count != 1 || right.community[0].speciesAmount != 50)
            throw new Exception("overload failed to found a new neighboring population");
        if (right.community[0].lastMigrationSource != left)
            throw new Exception("migrant did not remember its source block");

        right.temperature = 80;
        controller.SimulateDay(3);
        if (left.community.Count != 0)
            throw new Exception("migrant automatically returned to its source without an environment edit");
        controller.ApplyEnvironment(left, 51, 50, 0);
        if (right.community[0].lastMigrationSource != null)
            throw new Exception("environment edit did not clear return block");
        Console.WriteLine("MIGRATION_MERGE asset_and_name=merged weighted_traits=exact overload=empty_only return=blocked_until_environment_edit");
    }

    static void DirectedMutationTests()
    {
        var environmentPopulation = Pop(0, 50);
        var environment = Block(1000, 10000f, environmentPopulation);
        environment.temperature = 75;
        environment.humidity = 75;
        var controller = Controller(true);
        var start = Traits(environmentPopulation);
        for (int day = 0; day < 7; day++) controller.SimulateBlock(environment);
        if (ChangedTraits(start, environmentPopulation) != 1 ||
            environmentPopulation.fitTemperature <= 50 || environmentPopulation.fitHumidity != 50)
            throw new Exception("environmental shock changed multiple traits in one cycle");
        for (int day = 0; day < 7; day++) controller.SimulateBlock(environment);
        if (environmentPopulation.fitHumidity <= 50)
            throw new Exception("second environmental mismatch was ignored");
        Console.WriteLine($"DIRECTED_ENV first=temperature second=humidity temp={F(Traits(environmentPopulation)[0])} humidity={F(Traits(environmentPopulation)[1])}");

        var fastPrey = Pop(0, 500, 10, 60);
        var slowHunter = Pop(1, 5, 10, 10);
        var pursuit = Block(10000, 10000f, fastPrey, slowHunter);
        controller = Controller(true);
        start = Traits(slowHunter);
        for (int day = 0; day < 7; day++) controller.SimulateBlock(pursuit);
        if (ChangedTraits(start, slowHunter) != 1 || slowHunter.fertility <= 50)
            throw new Exception($"well-fed hunter with excess capacity did not select fertility: K/N={F(slowHunter.energySatisfactionToday)} actual={F(slowHunter.actualEnergySatisfactionToday)} traits={string.Join(',', Traits(slowHunter))}");
        Console.WriteLine($"DIRECTED_FERTILITY initial=50 final={F(Traits(slowHunter)[4])}");

        var scarcePrey = Pop(0, 500, 10, 60);
        var unfundedHunter = Pop(1, 5, 10, 10);
        var shortageBlock = Block(0, 10000f, scarcePrey, unfundedHunter);
        controller = Controller(true);
        controller.SetParameters(0f, 0f, 0f);
        for (int day = 0; day < 7; day++) controller.SimulateBlock(shortageBlock);
        if (unfundedHunter.movementAbility != 10 ||
            unfundedHunter.movementMutationRemainder != 0f)
            throw new Exception($"unsustainable movement increase was not rejected: {string.Join(',', Traits(unfundedHunter))} K={F(unfundedHunter.carryingCapacity)} actual={F(unfundedHunter.actualEnergySatisfactionToday)}");
        Console.WriteLine("MOVEMENT_ENERGY_GATE increase=rejected");

        var largePrey = Pop(0, 100, 30);
        var smallHunter = Pop(1, 5, 5);
        var sizeBlock = Block(10000, 10000f, largePrey, smallHunter);
        controller = Controller(true);
        for (int day = 0; day < 7; day++) controller.SimulateBlock(sizeBlock);
        if (smallHunter.size != 5 || smallHunter.sizeMutationRemainder != 0f)
            throw new Exception("costly size growth was selected despite weak capture benefit");
        Console.WriteLine("SIZE_COST_GATE costlyGrowth=rejected");

        var smallerPrey = Pop(0, 100, 10);
        var oversizedHunter = Pop(1, 5, 50);
        var sizeShortage = Block(0, 10000f, smallerPrey, oversizedHunter);
        controller = Controller(true);
        controller.SetParameters(0f, 0f, 0f);
        for (int day = 0; day < 7; day++) controller.SimulateBlock(sizeShortage);
        if (oversizedHunter.size >= 50)
            throw new Exception("oversized hungry hunter did not shrink");
        Console.WriteLine($"HUNGRY_OVERSIZED initial=50 final={F(Traits(oversizedHunter)[3])}");
        for (int day = 0; day < 140; day++) controller.SimulateBlock(sizeShortage);
        if (oversizedHunter.size >= 35)
            throw new Exception("old 30 percent body-size cap is still active");
        Console.WriteLine($"SIZE_UNBOUNDED initial=50 final={F(Traits(oversizedHunter)[3])}");

        var slowTarget = Block(0, 1000f, Pop(0, 20, 10, 0, 0), Pop(1, 100, 10, 50, 0));
        var fastTarget = Block(0, 1000f, Pop(0, 20, 10, 100, 0), Pop(1, 100, 10, 50, 0));
        controller = Controller();
        controller.SetParameters(0f, 0f, 0f);
        controller.SimulateBlock(slowTarget);
        controller.SimulateBlock(fastTarget);
        if (slowTarget.community[1].allocatedBiomass <=
            fastTarget.community[1].allocatedBiomass)
            throw new Exception("prey movement did not reduce hunter's energy gain");
        Console.WriteLine($"CAPTURE_SPEED slowPreyEnergy={F(slowTarget.community[1].allocatedBiomass)} fastPreyEnergy={F(fastTarget.community[1].allocatedBiomass)}");

        var smallHunterBlock = Block(0, 1000f, Pop(0, 20, 10, 10, 0), Pop(1, 100, 5, 10, 0));
        var largeHunterBlock = Block(0, 1000f, Pop(0, 20, 10, 10, 0), Pop(1, 100, 30, 10, 0));
        float smallChance = FoodWeb.CaptureEfficiency(10f, 5f,
            smallHunterBlock.community[0]);
        float largeChance = FoodWeb.CaptureEfficiency(10f, 30f,
            largeHunterBlock.community[0]);
        if (largeChance <= smallChance)
            throw new Exception("hunter size did not improve capture chance");
        Console.WriteLine($"CAPTURE_SIZE smallChance={F(smallChance)} largeChance={F(largeChance)}");

        var strandedHunter = Pop(1, 10);
        var plantAlternative = Block(1000, 10000f, strandedHunter);
        controller = Controller(true);
        controller.SetParameters(0f, 0f, 0f);
        controller.SetMutationParameters(1, 2f, 1f);
        for (int day = 0; day < 3; day++) controller.SimulateBlock(plantAlternative);
        if (strandedHunter.trophicLevel != 1)
            throw new Exception("starvation converted the entire predator population");
        Console.WriteLine("TROPHIC_ALTERNATIVE whole-population switch=disabled");

        var plantCompetitor = Pop(0, 500, 100);
        var wouldBeHunter = Pop(0, 10);
        var foodChoice = Block(10000, 10000f, plantCompetitor, wouldBeHunter);
        controller = Controller(true);
        controller.SetMutationParameters(1, 2f, 1f);
        controller.SimulateBlock(foodChoice);
        if (wouldBeHunter.trophicLevel != 0)
            throw new Exception("crowded herbivore switched the entire population");
        Console.WriteLine("TROPHIC_UP whole-population switch=disabled");

        var reversedCompetitor = Pop(0, 500, 100);
        var reversedHunter = Pop(0, 10);
        var reversedFoodChoice = Block(10000, 10000f, reversedHunter, reversedCompetitor);
        var reversedController = Controller(true);
        reversedController.SetMutationParameters(1, 2f, 1f);
        reversedController.SimulateBlock(reversedFoodChoice);
        float[] expectedTraits = Traits(wouldBeHunter);
        float[] reversedTraits = Traits(reversedHunter);
        if (expectedTraits.Where((value, index) =>
            Math.Abs(value - reversedTraits[index]) > 0.0001f).Any())
            throw new Exception("population list order changed the chosen mutation");
        Console.WriteLine("ORDER_INDEPENDENCE reversedPopulationList=sameTraits");

        var declining = Pop(0, 100);
        declining.previousMutationPopulation = 150;
        declining.mutationPopulationInitialized = true;
        var fertilityBlock = Block(2000, 10000f, declining);
        controller = Controller(true);
        controller.SetParameters(0f, 0f, 0f);
        controller.SetMutationParameters(1, 2f, 1f);
        controller.SimulateBlock(fertilityBlock);
        if (declining.fertility <= 50)
            throw new Exception("well-fed declining population did not increase fertility");
        Console.WriteLine($"FERTILITY_DECLINE initial=50 final={declining.fertility}");

        var smallFluctuation = Pop(0, 3);
        smallFluctuation.previousMutationPopulation = 4;
        smallFluctuation.mutationPopulationInitialized = true;
        var fluctuationBlock = Block(1000, 10000f, smallFluctuation);
        controller.SimulateBlock(fluctuationBlock);
        if (smallFluctuation.fertility != 50 ||
            smallFluctuation.fertilityMutationRemainder != 0f)
            throw new Exception("one-animal fluctuation changed fertility");
        Console.WriteLine("SMALL_POPULATION_FLUCTUATION fertility=unchanged");

        var growingUnderPressure = Pop(0, 100);
        growingUnderPressure.previousMutationPopulation = 50;
        growingUnderPressure.mutationPopulationInitialized = true;
        var fertilityPressureBlock = Block(100, 10000f, growingUnderPressure);
        controller = Controller(true);
        controller.SetParameters(0f, 0f, 0f);
        controller.SetMutationParameters(1, 2f, 1f);
        controller.SimulateBlock(fertilityPressureBlock);
        if (growingUnderPressure.fertility >= 50)
            throw new Exception("growing population under resource pressure did not reduce fertility");
        Console.WriteLine($"FERTILITY_GROWTH_PRESSURE initial=50 final={growingUnderPressure.fertility}");

        var changingPrey = Pop(0, 500, 10);
        var changingHunter = Pop(1, 5, 50);
        var recoveryChange = Block(10000, 10000f, changingPrey, changingHunter);
        controller = Controller(true);
        for (int day = 0; day < 28; day++) controller.SimulateBlock(recoveryChange);
        float sizeBeforeChange = Traits(changingHunter)[3];
        if (!controller.ApplyEnvironment(recoveryChange, recoveryChange.temperature,
            recoveryChange.humidity, 1000))
            throw new Exception("player environment change was ignored");
        if (controller.ApplyEnvironment(recoveryChange, recoveryChange.temperature,
            recoveryChange.humidity, 1000))
            throw new Exception("unchanged environment scheduled another mutation");
        controller.SimulateBlock(recoveryChange);
        if (Traits(changingHunter)[3] >= sizeBeforeChange)
            throw new Exception($"player recovery reduction did not select smaller hunter size: predatorN={changingHunter.speciesAmount} preyN={changingPrey.speciesAmount} preyK={F(changingPrey.carryingCapacity)} stock={F(recoveryChange.plantBiomass)} K/N={F(changingHunter.energySatisfactionToday)} actual={F(changingHunter.actualEnergySatisfactionToday)} size={F(Traits(changingHunter)[3])}");
        Console.WriteLine($"PLAYER_RECOVERY_CHANGE before={F(sizeBeforeChange)} after={F(Traits(changingHunter)[3])}");
    }

    static void EnvironmentGrid()
    {
        int survived = 0;
        int directed = 0;
        foreach (int temperatureChange in new[] { -25, 0, 25 })
        foreach (int humidityChange in new[] { -25, 0, 25 })
        foreach (int recovery in new[] { 100, 1000, 10000 })
        {
            var population = Pop(0, 10);
            var block = Block(recovery, 10000f, population);
            block.temperature += temperatureChange;
            block.humidity += humidityChange;
            var controller = Controller(true);
            for (int day = 0; day < 365; day++) controller.SimulateBlock(block);
            if (population.speciesAmount <= 0) continue;
            survived++;
            float[] traits = Traits(population);
            if (temperatureChange * (traits[0] - 50f) < 0f ||
                humidityChange * (traits[1] - 50f) < 0f ||
                population.size != 10 || population.movementAbility != 10 ||
                population.trophicLevel != 0)
                throw new Exception($"wrong evolution in grid: temp={temperatureChange}, humidity={humidityChange}, recovery={recovery}");
            if ((temperatureChange == 0 || Math.Abs(traits[0] - 50f) > 0.01f) &&
                (humidityChange == 0 || Math.Abs(traits[1] - 50f) > 0.01f)) directed++;
        }
        Console.WriteLine($"ENV_GRID cases=27 survived={survived} correctlyDirected={directed}");
        if (directed != survived)
            throw new Exception("some surviving grid populations did not follow environmental input");
    }

    static void CompetitionSweep()
    {
        int plantComparisons = 0;
        foreach (int fasterMovement in new[] { 25, 50, 75, 100 })
        {
            var slower = Pop(0, 10, 10, 0, 0);
            var faster = Pop(0, 10, 10, fasterMovement, 0);
            var block = Block(0, 100f, slower, faster);
            var controller = Controller();
            controller.SetParameters(0f, 0f, 0f);
            controller.SimulateBlock(block);
            if (faster.allocatedBiomass <= slower.allocatedBiomass)
                throw new Exception($"faster herbivore lost plant competition: movement={fasterMovement}");
            plantComparisons++;
        }

        float previousChance = float.MaxValue;
        int huntComparisons = 0;
        foreach (int preyMovement in new[] { 0, 25, 50, 75, 100 })
        {
            var prey = Pop(0, 20, 10, preyMovement, 0);
            float chance = FoodWeb.CaptureEfficiency(50f, 10f, prey);
            if (chance >= previousChance)
                throw new Exception($"faster prey did not lower capture chance: movement={preyMovement}");
            previousChance = chance;
            huntComparisons++;
        }
        Console.WriteLine($"COMPETITION_SWEEP plantComparisons={plantComparisons} huntSpeeds={huntComparisons} direction=consistent");
    }

    static void Run(string name, BlockInfo b, SimulationController c, int days, int[] checkpoints,
        Action<int, BlockInfo> beforeDay = null)
    {
        Console.WriteLine("CASE " + name);
        for (int day = 1; day <= days; day++)
        {
            beforeDay?.Invoke(day, b);
            float oldStock = b.plantBiomass;
            c.SimulateBlock(b);
            if (Math.Abs(b.plantBiomass - (Math.Min(b.maxPlantBiomass,
                Math.Clamp(oldStock, 0f, b.maxPlantBiomass) +
                    Math.Max(0, b.habitatRecovery) *
                    (b.maxPlantBiomass > 0f
                        ? 1f - Math.Clamp(oldStock, 0f, b.maxPlantBiomass)
                            / b.maxPlantBiomass : 0f))
                - b.consumedBiomassToday)) > 0.05f)
                Console.WriteLine("INVARIANT plant mass mismatch day=" + day);
            foreach (var p in b.community)
            {
                if (p.speciesAmount < 0 || p.trophicLevel < 0 || p.trophicLevel > 2 ||
                    float.IsNaN(p.allocatedBiomass) || float.IsNaN(p.carryingCapacity) ||
                    p.habitatNiche != 50)
                    Console.WriteLine("INVARIANT invalid population day=" + day);
            }
            if (checkpoints.Contains(day))
            {
                Console.WriteLine("day=" + day + " plant=" + F(b.plantBiomass) +
                    " growth=" + F(b.plantGrowthToday) + " consumed=" + F(b.consumedBiomassToday) +
                    " pops=" + string.Join(" | ", b.community.Select(p =>
                        $"N:{p.speciesAmount},K:{F(p.carryingCapacity)},birth:{p.birthsToday},death:{p.deathsToday}," +
                        $"intake:{F(p.allocatedBiomass)},sat:{F(p.energySatisfactionToday)}," +
                        $"temp:{F(p.fitTemperature + p.temperatureMutationRemainder)}," +
                        $"size:{F(p.size + p.sizeMutationRemainder)},trophic:{F(p.trophicLevel + p.trophicMutationRemainder)}")));
            }
        }
    }

    static void SelectionStrengthCheck(float strength, int days)
    {
        var population = Pop(0, 1);
        var block = Block(1000, 10000f, population);
        block.temperature = 100;
        var controller = Controller();
        controller.SetParameters(0f, 0f, 0f);
        controller.SetMutationParameters(1, 1f, strength);
        for (int day = 0; day < days; day++) controller.SimulateBlock(block);
        float[] traits = Traits(population);
        if (strength == 0f && (traits[0] != 50f || traits[1] != 50f ||
            traits[2] != 10f || traits[3] != 10f || traits[4] != 50f || traits[5] != 0f))
            throw new Exception("zero selection strength changed a trait");
        if (strength > 0f && traits[0] <= 50f)
            throw new Exception("positive selection strength did not adapt to heat");
        Console.WriteLine($"SELECTION_STRENGTH strength={strength} days={days} temperature={F(traits[0])}");
    }

    static void ChainEnsemble(bool threeLevels, int configurations, int days)
    {
        int allSurvive = 0;
        int preySurvive = 0;
        int middleSurvive = 0;
        int topSurvive = 0;
        int minimumFertility = 100;
        int maximumFertility = 0;
        for (int scenario = 0; scenario < configurations; scenario++)
        {
            var b = threeLevels
                ? Block(10000, 10000f, Pop(0, 450 + scenario * 2),
                    Pop(1, 18 + scenario % 5), Pop(2, 1 + scenario % 3))
                : Block(1000, 10000f, Pop(0, 45 + scenario % 11),
                    Pop(1, 2 + scenario % 4));
            b.temperature += scenario % 5 - 2;
            b.humidity += scenario / 5 % 5 - 2;
            var c = Controller(true);
            for (int day = 0; day < days; day++) c.SimulateBlock(b);
            if (b.community.All(p => p.speciesAmount > 0)) allSurvive++;
            if (b.community[0].speciesAmount > 0) preySurvive++;
            if (b.community[1].speciesAmount > 0) middleSurvive++;
            if (threeLevels && b.community[2].speciesAmount > 0) topSurvive++;
            if (threeLevels && b.community[2].speciesAmount == 0)
                Console.WriteLine($"CHAIN_TOP_EXTINCT scenario={scenario} prey0={450 + scenario * 2} middle0={18 + scenario % 5} top0={1 + scenario % 3} temperature={b.temperature} humidity={b.humidity}");
            foreach (PopulationData population in b.community)
            {
                minimumFertility = Math.Min(minimumFertility, population.fertility);
                maximumFertility = Math.Max(maximumFertility, population.fertility);
            }
        }
        Console.WriteLine($"CHAIN_SWEEP levels={(threeLevels ? 3 : 2)} configurations={configurations} days={days} allSurvive={allSurvive} preySurvive={preySurvive} middleSurvive={middleSurvive} topSurvive={topSurvive} fertilityRange={minimumFertility}..{maximumFertility}");
        if (allSurvive < configurations * 9 / 10)
            throw new Exception("food chain survival below 90% of configurations");
    }

    static void ShiftEnsemble(int change, int configurations, int days)
    {
        int survivors = 0;
        long finalTotal = 0;
        for (int scenario = 0; scenario < configurations; scenario++)
        {
            var b = Block(800 + 100 * (scenario % 5), 10000f,
                Pop(0, 8 + scenario / 5 % 10));
            var c = Controller(true);
            for (int day = 1; day <= days; day++)
            {
                if (day == 100)
                {
                    b.temperature += change;
                    b.humidity += change;
                }
                c.SimulateBlock(b);
            }
            if (b.community[0].speciesAmount > 0) survivors++;
            finalTotal += b.community[0].speciesAmount;
        }
        Console.WriteLine($"SHIFT_SWEEP deltaEach={change} configurations={configurations} days={days} survivors={survivors} meanFinal={F(finalTotal / (float)configurations)}");
        if (change <= 25 && survivors < configurations * 9 / 10)
            throw new Exception("small environment shift caused too many extinctions");
        if (change >= 50 && survivors > configurations / 10)
            throw new Exception("large environment shift was not lethal");
    }

    static void DirectionEnsemble(int change, int configurations, int days)
    {
        int temperatureToward = 0, humidityToward = 0, survivors = 0;
        double temperatureChange = 0, humidityChange = 0;
        for (int scenario = 0; scenario < configurations; scenario++)
        {
            var population = Pop(0, 8 + scenario / 5 % 10);
            var block = Block(800 + 100 * (scenario % 5), 10000f, population);
            var controller = Controller(true);
            for (int day = 1; day <= days; day++)
            {
                if (day == 100)
                {
                    block.temperature += change;
                    block.humidity += change;
                }
                controller.SimulateBlock(block);
            }
            float temperatureDelta = population.fitTemperature
                + population.temperatureMutationRemainder - 50f;
            float humidityDelta = population.fitHumidity
                + population.humidityMutationRemainder - 50f;
            temperatureChange += temperatureDelta;
            humidityChange += humidityDelta;
            if (temperatureDelta * change > 0f) temperatureToward++;
            if (humidityDelta * change > 0f) humidityToward++;
            if (population.speciesAmount > 0) survivors++;
        }
        Console.WriteLine($"DIRECTION deltaEach={change} configurations={configurations} days={days} "
            + $"temperatureToward={temperatureToward} humidityToward={humidityToward} "
            + $"meanTemperatureChange={F((float)(temperatureChange / configurations))} "
            + $"meanHumidityChange={F((float)(humidityChange / configurations))} survivors={survivors}");
        if (temperatureToward < configurations * 9 / 10 ||
            humidityToward < configurations * 9 / 10)
            throw new Exception("environmental mutation direction was inconsistent");
    }

    static void LiveTrophicLevelTests()
    {
        var prey = Pop(0, 100);
        var predator = Pop(0, 5);
        var block = Block(10000, 10000f, prey, predator);
        var controller = Controller();
        controller.SetParameters(0f, 0f, 0f);

        controller.SimulateBlock(block);
        if (prey.deathsToday != 0)
            throw new Exception("same-level populations unexpectedly hunted");

        predator.trophicMutationRemainder = 0.3f;
        predator.energyReserve = 40f;
        predator.mutationDaysElapsed = 5;
        if (!SimulationController.SetTrophicLevel(predator, 1) ||
            predator.trophicMutationRemainder != 0f || predator.energyReserve != 0f ||
            predator.mutationDaysElapsed != 0)
            throw new Exception("manual trophic change did not reset old food state");
        controller.SimulateBlock(block);
        if (prey.deathsToday <= 0 || predator.trophicLevel != 1 ||
            predator.species.trophicLevel != 0)
            throw new Exception("live level 1 did not hunt level 0 independently of asset");

        SimulationController.SetTrophicLevel(predator, 0);
        controller.SimulateBlock(block);
        if (prey.deathsToday != 0)
            throw new Exception("returning to level 0 did not stop predation");

        SimulationController.SetTrophicLevel(prey, 1);
        SimulationController.SetTrophicLevel(predator, 2);
        controller.SimulateBlock(block);
        if (prey.deathsToday <= 0)
            throw new Exception("live level 2 did not hunt level 1");

        Console.WriteLine("LIVE TROPHIC 0/1/2 switching and predation=ok");
    }

    static void FoodChainBalanceTests()
    {
        var solitary = Pop(0, 10, 5, 10, 80);
        var solitaryBlock = Block(1000, 10000f, solitary);
        solitaryBlock.maxPlantBiomass = 100000f;
        var solitaryController = Controller();
        solitaryController.SetParameters(0.1f, 0.1f, 0.6f);
        float stockAtDay1000 = 0f;
        for (int day = 1; day <= 2000; day++)
        {
            solitaryController.SimulateBlock(solitaryBlock);
            if (solitary.carryingCapacity > 1000f / solitary.energyNeed + 0.01f)
                throw new Exception("solitary herbivore K counted plant reserves as renewal");
            if (day == 377 || day == 1000 || day == 2000)
                Console.WriteLine($"SINGLE_CHAIN day={day} plant={F(solitaryBlock.plantBiomass)} population={solitary.speciesAmount} K={F(solitary.carryingCapacity)} growth={F(solitaryBlock.plantGrowthToday)} eaten={F(solitaryBlock.consumedBiomassToday)}");
            if (day == 1000) stockAtDay1000 = solitaryBlock.plantBiomass;
        }
        if (solitary.speciesAmount <= 0 || solitaryBlock.plantBiomass <= 0f ||
            solitaryBlock.plantBiomass < stockAtDay1000 - 2000f)
            throw new Exception("solitary herbivore failed to stabilize with renewable plants");

        var lowPrey = Pop(0, 100, 6, 22, 80);
        var lowHunter = Pop(1, 12, 15, 30, 50);
        var low = Block(1000, 1000f, lowPrey, lowHunter);
        low.maxPlantBiomass = 100000f;
        var highPrey = Pop(0, 100, 6, 22, 80);
        var highHunter = Pop(1, 12, 15, 30, 50);
        var high = Block(1000, 77458f, highPrey, highHunter);
        high.maxPlantBiomass = 100000f;
        var controller = Controller();
        controller.SetParameters(0.1f, 0.1f, 0.6f);
        controller.SimulateBlock(low);
        controller.SimulateBlock(high);
        if (Math.Abs(highPrey.carryingCapacity - lowPrey.carryingCapacity) > 0.01f ||
            Math.Abs(highHunter.carryingCapacity - lowHunter.carryingCapacity) > 0.01f)
            throw new Exception("surplus plant stock inflated sustainable capacity");
        var recoveringDestination = Block(1000, 0f);
        if (controller.EstimateTargetCapacity(recoveringDestination, lowPrey) <= 0f)
            throw new Exception("empty plant stock hid next day's migration capacity");

        var herbivorePrey = Pop(0, 100, 10, 10, 0);
        var levelOne = Block(10000, 10000f, herbivorePrey,
            Pop(1, 1000, 15, 30, 0));
        var carnivorePrey = Pop(1, 20, 10, 10, 0);
        var levelTwo = Block(0, 0f, carnivorePrey,
            Pop(2, 1000, 15, 30, 0));
        controller.SetParameters(0f, 0f, 0f);
        controller.SimulateBlock(levelOne);
        controller.SimulateBlock(levelTwo);
        if (herbivorePrey.deathsToday > 8 || carnivorePrey.deathsToday > 2 ||
            herbivorePrey.deathsToday <= 0)
            throw new Exception("trophic catch limits exceeded the 8% ceiling");

        var prey = Pop(0, 185, 6, 22, 80);
        var hunter = Pop(1, 12, 15, 30, 50);
        var block = Block(1000, 10000f, prey, hunter);
        block.maxPlantBiomass = 100000f;
        controller = Controller(true);
        controller.SetParameters(0.1f, 0.1f, 0.6f);
        for (int day = 1; day <= 10000; day++)
        {
            controller.SimulateBlock(block);
            if (day == 217 || day == 365 || day == 2000 || day == 5000 || day == 10000)
                Console.WriteLine($"SCREENSHOT_CHAIN day={day} plant={F(block.plantBiomass)} prey={prey.speciesAmount} preyK={F(prey.carryingCapacity)} predator={hunter.speciesAmount} predatorK={F(hunter.carryingCapacity)} size={F(Traits(prey)[3])} movement={F(Traits(prey)[2])} fertility={F(Traits(prey)[4])}");
            if ((day == 217 && (prey.speciesAmount < 50 || hunter.speciesAmount < 5)) ||
                (day == 2000 && (prey.speciesAmount < 50 || hunter.speciesAmount < 5)))
                throw new Exception($"food chain failed to recover from the screenshot scenario on day {day}");
            if (day >= 365 && (prey.speciesAmount <= 0 || hunter.speciesAmount <= 0))
                throw new Exception($"screenshot-like food chain lost a population on day {day}");
        }
    }

    static void EcologyScenarioTests()
    {
        UnityEngine.Random.InitState(7);
        var solo = Pop(0, 10, 5, 10, 80);
        var soloBlock = Block(1000, 10000f, solo);
        soloBlock.maxPlantBiomass = 100000f;
        var controller = Controller(true);
        controller.SetParameters(0.1f, 0.1f, 0.6f);
        int day20 = 0, day50 = 0, day100 = 0;
        float soloStock500 = 0f;
        for (int day = 1; day <= 1200; day++)
        {
            controller.SimulateBlock(soloBlock);
            if (day == 20) day20 = solo.speciesAmount;
            if (day == 50) day50 = solo.speciesAmount;
            if (day == 100) day100 = solo.speciesAmount;
            if (day == 500) soloStock500 = soloBlock.plantBiomass;
        }
        if (day20 >= day50 || day50 >= day100 ||
            Math.Abs(solo.speciesAmount - solo.carryingCapacity) > 4f ||
            soloStock500 < 30000f || soloStock500 > 60000f ||
            soloBlock.plantBiomass < 30000f || soloBlock.plantBiomass > 60000f ||
            Math.Abs(soloBlock.plantGrowthToday - soloBlock.consumedBiomassToday) > 20f)
            throw new Exception("single herbivore lost its saturating growth curve");
        Console.WriteLine($"ECOLOGY_SOLO N20={day20} N50={day50} N100={day100} final={solo.speciesAmount} K={F(solo.carryingCapacity)} stock={F(soloBlock.plantBiomass)}");

        var small = Pop(0, 10, 5, 10, 80);
        var large = Pop(0, 10, 10, 20, 50);
        var dual = Block(1000, 10000f, small, large);
        dual.maxPlantBiomass = 100000f;
        controller = Controller(true);
        controller.SetParameters(0.1f, 0.1f, 0.6f);
        float dualStock500 = 0f;
        for (int day = 1; day <= 2000; day++)
        {
            controller.SimulateBlock(dual);
            if (day == 500) dualStock500 = dual.plantBiomass;
        }
        if (Math.Abs(small.speciesAmount - small.carryingCapacity) > 4f ||
            Math.Abs(large.speciesAmount - large.carryingCapacity) > 4f ||
            small.speciesAmount < 50 || large.speciesAmount < 25 ||
            dualStock500 < 30000f || dualStock500 > 60000f ||
            dual.plantBiomass < 30000f || dual.plantBiomass > 60000f ||
            Math.Abs(dual.plantGrowthToday - dual.consumedBiomassToday) > 20f)
            throw new Exception("separate herbivore tracks did not coexist near both K values");
        Console.WriteLine($"ECOLOGY_TWO_TRACKS small={small.speciesAmount} large={large.speciesAmount} stock={F(dual.plantBiomass)}");

        var switcher = Pop(0, 10, 5, 10, 80);
        var incumbent = Pop(0, 10, 6, 20, 50);
        var changing = Block(1000, 10000f, switcher, incumbent);
        changing.maxPlantBiomass = 100000f;
        controller = Controller(true);
        controller.SetParameters(0.1f, 0.1f, 0.6f);
        float minimumStock = float.MaxValue, maximumStock = 0f;
        int switchDay = 0;
        float changingStock500 = 0f;
        for (int day = 1; day <= 5000; day++)
        {
            controller.SimulateBlock(changing);
            if (switchDay == 0 && switcher.size >= 8) switchDay = day;
            if (day == 500) changingStock500 = changing.plantBiomass;
            if (day <= 4500) continue;
            minimumStock = Math.Min(minimumStock, changing.plantBiomass);
            maximumStock = Math.Max(maximumStock, changing.plantBiomass);
        }
        if (switchDay == 0 || switchDay > 550 || incumbent.size >= 8 ||
            switcher.speciesAmount < 20 || incumbent.speciesAmount < 20 ||
            Math.Abs(switcher.speciesAmount - switcher.carryingCapacity) > 5f ||
            Math.Abs(incumbent.speciesAmount - incumbent.carryingCapacity) > 5f ||
            changingStock500 < 30000f || changingStock500 > 60000f ||
            changing.plantBiomass < 30000f || changing.plantBiomass > 60000f ||
            maximumStock - minimumStock > 3000f)
            throw new Exception("weak herbivore failed to switch tracks and stabilize");
        Console.WriteLine($"ECOLOGY_TRACK_SWITCH day={switchDay} newSize={switcher.size} small={incumbent.speciesAmount} large={switcher.speciesAmount} stock={F(changing.plantBiomass)}");

        var disadvantaged = Pop(0, 10, 6, 10, 50);
        disadvantaged.fitTemperature = 70;
        disadvantaged.fitHumidity = 70;
        var dominant = Pop(0, 20, 5, 30, 80);
        var recoverable = Block(1000, 10000f, dominant, disadvantaged);
        recoverable.maxPlantBiomass = 100000f;
        controller = Controller(true);
        controller.SetParameters(0.1f, 0.1f, 0.6f);
        int disadvantagedSwitchDay = 0;
        for (int day = 1; day <= 1000; day++)
        {
            controller.SimulateBlock(recoverable);
            if (disadvantagedSwitchDay == 0 && disadvantaged.size >= 8)
                disadvantagedSwitchDay = day;
        }
        if (disadvantagedSwitchDay == 0 || disadvantagedSwitchDay > 500 ||
            disadvantaged.speciesAmount < 20 || dominant.speciesAmount < 20 ||
            recoverable.plantBiomass < 30000f || recoverable.plantBiomass > 70000f)
            throw new Exception("recoverable competitor failed to change tracks");
        Console.WriteLine($"ECOLOGY_COMPETITOR_ESCAPE day={disadvantagedSwitchDay} dominant={dominant.speciesAmount} survivor={disadvantaged.speciesAmount} stock={F(recoverable.plantBiomass)}");

        var winner = Pop(0, 20, 5, 30, 80);
        var loser = Pop(0, 10, 6, 10, 50);
        loser.fitTemperature = 80;
        loser.fitHumidity = 80;
        var exclusion = Block(1000, 10000f, winner, loser);
        exclusion.maxPlantBiomass = 100000f;
        controller = Controller(true);
        controller.SetParameters(0.1f, 0.1f, 0.6f);
        float exclusionStock500 = 0f;
        for (int day = 1; day <= 2000; day++)
        {
            controller.SimulateBlock(exclusion);
            if (day == 500) exclusionStock500 = exclusion.plantBiomass;
            if (day == 500 || day == 1000 || day == 2000)
                Console.WriteLine($"ECOLOGY_EXCLUSION_DAY day={day} winner={winner.speciesAmount} loser={loser.speciesAmount} stock={F(exclusion.plantBiomass)} growth={F(exclusion.plantGrowthToday)} eaten={F(exclusion.consumedBiomassToday)}");
        }
        if (loser.speciesAmount != 0 || winner.speciesAmount < 140 ||
            Math.Abs(winner.speciesAmount - winner.carryingCapacity) > 5f ||
            exclusionStock500 < 30000f || exclusionStock500 > 70000f ||
            Math.Abs(exclusion.plantBiomass - exclusionStock500) > 5000f ||
            exclusion.plantBiomass < 30000f || exclusion.plantBiomass > 70000f)
            throw new Exception("clear competitive disadvantage did not cause exclusion");
        Console.WriteLine($"ECOLOGY_EXCLUSION winner={winner.speciesAmount} loser={loser.speciesAmount} stock={F(exclusion.plantBiomass)}");

        var prey = Pop(0, 100, 5, 10, 80);
        var predator = Pop(1, 5, 20, 30, 50);
        var chain = Block(1000, 10000f, prey, predator);
        chain.maxPlantBiomass = 100000f;
        controller = Controller(true);
        controller.SetParameters(0.1f, 0.1f, 0.6f);
        minimumStock = float.MaxValue;
        maximumStock = 0f;
        float preySpeedAt500 = 0f, hunterSpeedAt500 = 0f, fertilityAt500 = 0f;
        float chainStock500 = 0f, chainBalance500 = 0f;
        for (int day = 1; day <= 2000; day++)
        {
            controller.SimulateBlock(chain);
            if (day == 500)
            {
                preySpeedAt500 = Traits(prey)[2];
                hunterSpeedAt500 = Traits(predator)[2];
                fertilityAt500 = Traits(prey)[4];
                chainStock500 = chain.plantBiomass;
                chainBalance500 = Math.Abs(chain.plantGrowthToday
                    - chain.consumedBiomassToday);
            }
            if (day <= 1500) continue;
            minimumStock = Math.Min(minimumStock, chain.plantBiomass);
            maximumStock = Math.Max(maximumStock, chain.plantBiomass);
        }
        if (predator.speciesAmount <= 5 || prey.speciesAmount < 75 ||
            preySpeedAt500 < 16f || hunterSpeedAt500 < 40f ||
            fertilityAt500 <= 82f || fertilityAt500 >= 100f ||
            chainStock500 < 30000f || chainStock500 > 80000f ||
            chainBalance500 > 30f ||
            chain.plantBiomass < 30000f || chain.plantBiomass > 80000f ||
            maximumStock - minimumStock > 5000f)
            throw new Exception("predation did not sustain coevolution and plant balance");
        Console.WriteLine($"ECOLOGY_PREDATION prey={prey.speciesAmount} hunter={predator.speciesAmount} speed500={F(preySpeedAt500)}/{F(hunterSpeedAt500)} fertility500={F(fertilityAt500)} stock={F(chain.plantBiomass)}");

        var tinyPrey = Pop(0, 10, 5, 10, 0);
        var persistentHunter = Pop(1, 100, 20, 30, 0);
        var fractional = Block(1000, 10000f, tinyPrey, persistentHunter);
        fractional.maxPlantBiomass = 100000f;
        controller = Controller();
        controller.SetParameters(0f, 0f, 0f);
        int caught = 0;
        for (int day = 0; day < 100; day++)
        {
            tinyPrey.speciesAmount = 10;
            controller.SimulateBlock(fractional);
            if (tinyPrey.deathsToday > 1)
                throw new Exception("fractional capture exceeded the small-prey daily cap");
            caught += tinyPrey.deathsToday;
        }
        if (caught <= 0 || caught >= 100)
            throw new Exception("fractional capture recreated the 20-prey hard threshold");
        Console.WriteLine($"ECOLOGY_FRACTIONAL_HUNT prey=10 catchDays={caught}/100");

        var sparsePrey = Pop(0, 10, 5, 10, 80);
        var excessHunters = Pop(1, 100, 20, 30, 50);
        var impossible = Block(1000, 10000f, sparsePrey, excessHunters);
        impossible.maxPlantBiomass = 100000f;
        controller = Controller(true);
        controller.SetParameters(0.1f, 0.1f, 0.6f);
        for (int day = 1; day <= 100; day++) controller.SimulateBlock(impossible);
        if (excessHunters.speciesAmount > 5 || sparsePrey.speciesAmount <= 0)
            throw new Exception("100 hunters on 10 prey did not collapse appropriately");
        Console.WriteLine($"ECOLOGY_OVERSUBSCRIBED prey={sparsePrey.speciesAmount} hunters={excessHunters.speciesAmount}");
        UnityEngine.Random.InitState(1);
    }

    static void Explore(string name, BlockInfo block, int days)
    {
        var controller = Controller(true);
        controller.SetParameters(0.1f, 0.1f, 0.6f);
        foreach (int day in Enumerable.Range(1, days))
        {
            controller.SimulateBlock(block);
            if (day != 50 && day != 100 && day != 300 && day != 500 &&
                day != 1000 && day != 2000 && day != 5000) continue;
            Console.WriteLine($"EXPLORE {name} day={day} stock={F(block.plantBiomass)} growth={F(block.plantGrowthToday)} eaten={F(block.consumedBiomassToday)} " +
                string.Join(" | ", block.community.Select(p => $"N={p.speciesAmount},K={F(p.carryingCapacity)},size={F(Traits(p)[3])},move={F(Traits(p)[2])},fert={F(Traits(p)[4])},level={p.trophicLevel}")));
        }
    }

    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "ecology-probe")
        {
            for (int seed = 1; seed <= 12; seed++)
            {
                UnityEngine.Random.InitState(seed);
                var a = Pop(0, 10, 5, 10, 80);
                var b = Pop(0, 10, 6, 20, 50);
                var track = Block(1000, 10000f, a, b);
                track.maxPlantBiomass = 100000f;
                var c = Controller(true);
                c.SetParameters(0.1f, 0.1f, 0.6f);
                int switched = 0;
                for (int day = 1; day <= 1000; day++)
                {
                    c.SimulateBlock(track);
                    if (switched == 0 && a.size >= 8) switched = day;
                }
                if (switched == 0 || switched > 550 || a.speciesAmount < 20 ||
                    b.speciesAmount < 20 || track.plantBiomass < 30000f ||
                    track.plantBiomass > 70000f)
                    throw new Exception($"track switch failed for seed {seed}");
                Console.WriteLine($"PROBE_TRACK seed={seed} day={switched} A={a.speciesAmount} B={b.speciesAmount} ASize={F(Traits(a)[3])} stock={F(track.plantBiomass)}");

                UnityEngine.Random.InitState(seed);
                a = Pop(0, 20, 5, 30, 80);
                b = Pop(0, 10, 6, 10, 50);
                b.fitTemperature = 70;
                b.fitHumidity = 70;
                var contest = Block(1000, 10000f, a, b);
                contest.maxPlantBiomass = 100000f;
                c = Controller(true);
                c.SetParameters(0.1f, 0.1f, 0.6f);
                int extinct = 0, changed = 0;
                for (int day = 1; day <= 1000; day++)
                {
                    c.SimulateBlock(contest);
                    if (extinct == 0 && b.speciesAmount == 0) extinct = day;
                    if (changed == 0 && b.size >= 8) changed = day;
                }
                if (extinct != 0 || changed == 0 || changed > 500 ||
                    a.speciesAmount < 20 || b.speciesAmount < 20 ||
                    contest.plantBiomass < 30000f || contest.plantBiomass > 70000f)
                    throw new Exception($"recoverable competition failed for seed {seed}");
                Console.WriteLine($"PROBE_CONTEST seed={seed} extinct={extinct} switch={changed} A={a.speciesAmount} B={b.speciesAmount} BFit={b.fitTemperature}/{b.fitHumidity} stock={F(contest.plantBiomass)}");

                UnityEngine.Random.InitState(seed);
                a = Pop(0, 100, 5, 10, 80);
                b = Pop(1, 5, 20, 30, 50);
                var chain = Block(1000, 10000f, a, b);
                chain.maxPlantBiomass = 100000f;
                c = Controller(true);
                c.SetParameters(0.1f, 0.1f, 0.6f);
                for (int day = 1; day <= 500; day++)
                    c.SimulateBlock(chain);
                if (a.speciesAmount < 75 || b.speciesAmount < 5 ||
                    Traits(a)[2] < 16f || Traits(b)[2] < 40f ||
                    Traits(a)[4] <= 82f || Traits(a)[4] >= 100f ||
                    chain.plantBiomass < 30000f || chain.plantBiomass > 80000f)
                    throw new Exception($"coevolution failed for seed {seed}");
                Console.WriteLine($"PROBE_CHAIN seed={seed} prey={a.speciesAmount} hunter={b.speciesAmount} speed={F(Traits(a)[2])}/{F(Traits(b)[2])} fertility={F(Traits(a)[4])}/{F(Traits(b)[4])} stock={F(chain.plantBiomass)}");
            }
            return;
        }
        if (args.Length > 0 && args[0] == "scan")
        {
            foreach (int weakFit in new[] { 70, 75, 80, 85, 90, 95 })
            foreach (int weakFertility in new[] { 10, 30, 50 })
            {
                var strong = Pop(0, 20, 5, 30, 80);
                var weak = Pop(0, 10, 6, 10, weakFertility);
                weak.fitTemperature = weakFit;
                weak.fitHumidity = weakFit;
                var block = Block(1000, 10000f, strong, weak);
                block.maxPlantBiomass = 100000f;
                var controller = Controller(true);
                controller.SetParameters(0.1f, 0.1f, 0.6f);
                for (int day = 0; day < 2000; day++) controller.SimulateBlock(block);
                Console.WriteLine($"SCAN fit={weakFit} fertility={weakFertility} strong={strong.speciesAmount} weak={weak.speciesAmount} weakSize={F(Traits(weak)[3])} stock={F(block.plantBiomass)}");
            }
            return;
        }
        if (args.Length > 0 && args[0] == "explore")
        {
            var solo = Block(1000, 10000f, Pop(0, 10, 5, 10, 80));
            solo.maxPlantBiomass = 100000f;
            Explore("solo", solo, 2000);
            var competition = Block(1000, 10000f, Pop(0, 10, 5, 10, 80), Pop(0, 10, 6, 20, 50));
            competition.maxPlantBiomass = 100000f;
            Explore("same-track", competition, 5000);
            var dual = Block(1000, 10000f, Pop(0, 10, 5, 10, 80), Pop(0, 10, 10, 20, 50));
            dual.maxPlantBiomass = 100000f;
            Explore("two-tracks", dual, 2000);
            var exclusion = Block(1000, 10000f, Pop(0, 20, 5, 80, 80), Pop(0, 2, 6, 0, 5));
            exclusion.maxPlantBiomass = 100000f;
            Explore("exclusion", exclusion, 2000);
            var chain = Block(1000, 10000f, Pop(0, 100, 5, 10, 80), Pop(1, 100, 20, 30, 50));
            chain.maxPlantBiomass = 100000f;
            Explore("chain", chain, 2000);
            var invasion = Block(1000, 10000f, Pop(0, 100, 5, 10, 80), Pop(1, 5, 20, 30, 50));
            invasion.maxPlantBiomass = 100000f;
            Explore("invasion", invasion, 2000);
            return;
        }
        MigrationMergeTests();
        LiveTrophicLevelTests();
        DirectedMutationTests();
        FoodChainBalanceTests();
        EcologyScenarioTests();
        EnvironmentGrid();
        CompetitionSweep();
        int stableMaxFertility = 0;
        for (int configuration = 0; configuration < 20; configuration++)
        {
            var stablePopulation = Pop(0, 60 + configuration);
            var stableBlock = Block(1000, 10000f, stablePopulation);
            var stableController = Controller(true);
            for (int day = 0; day < 2000; day++) stableController.SimulateBlock(stableBlock);
            stableMaxFertility = Math.Max(stableMaxFertility, stablePopulation.fertility);
            if (stablePopulation.speciesAmount <= 0 || stablePopulation.size != 10 ||
                stablePopulation.sizeMutationRemainder != 0f ||
                stablePopulation.fitTemperature != 50 || stablePopulation.fitHumidity != 50 ||
                stablePopulation.movementAbility != 10 || stablePopulation.trophicLevel != 0)
                throw new Exception($"suitable environment drifted: configuration={configuration} N={stablePopulation.speciesAmount} K={F(stablePopulation.carryingCapacity)} stock={F(stableBlock.plantBiomass)} traits={string.Join(',', Traits(stablePopulation))}");
        }
        if (stableMaxFertility > 60)
            throw new Exception($"suitable environment drove excessive fertility: {stableMaxFertility}");
        Console.WriteLine($"STABLE suitable environment configurations=20 days=2000 maxFertility={stableMaxFertility}");

        UnityEngine.Random.InitState(42);
        var hungryPopulation = Pop(0, 100);
        var hungryBlock = Block(100, 100f, hungryPopulation);
        var hungryController = Controller(true);
        hungryController.SetParameters(0f, 0f, 0f);
        hungryController.SetMutationParameters(1, 10f, 1f);
        for (int day = 0; day < 200; day++) hungryController.SimulateBlock(hungryBlock);
        if (hungryPopulation.size != 10 || hungryPopulation.movementAbility != 10 ||
            hungryPopulation.fertility != 50)
            throw new Exception("starvation alone changed unrelated traits");
        Console.WriteLine("HUNGER_ALONE traits=unchanged");

        UnityEngine.Random.InitState(42);
        var mutationSwitchPopulation = Pop(0, 1);
        var mutationSwitchBlock = Block(1000, 10000f, mutationSwitchPopulation);
        mutationSwitchBlock.temperature = 100;
        var mutationSwitchController = Controller(true);
        mutationSwitchController.SetParameters(0f, 0f, 0f);
        mutationSwitchController.SetMutationParameters(1, 1f, 1f);
        mutationSwitchController.SetMutationEnabled(false);
        for (int day = 0; day < 100; day++)
            mutationSwitchController.SimulateBlock(mutationSwitchBlock);
        if (mutationSwitchPopulation.fitTemperature != 50 ||
            mutationSwitchPopulation.temperatureMutationRemainder != 0f ||
            mutationSwitchPopulation.mutationDaysElapsed != 0)
            throw new Exception("disabled mutation changed a trait or advanced its timer");
        mutationSwitchController.SetMutationEnabled(true);
        for (int day = 0; day < 100; day++)
            mutationSwitchController.SimulateBlock(mutationSwitchBlock);
        float adaptedTemperature = mutationSwitchPopulation.fitTemperature
            + mutationSwitchPopulation.temperatureMutationRemainder;
        if (adaptedTemperature <= 50f)
            throw new Exception("enabled mutation did not respond to the environment");
        mutationSwitchController.SetMutationEnabled(false);
        for (int day = 0; day < 100; day++)
            mutationSwitchController.SimulateBlock(mutationSwitchBlock);
        if (Math.Abs(mutationSwitchPopulation.fitTemperature
            + mutationSwitchPopulation.temperatureMutationRemainder - adaptedTemperature) > 0.0001f)
            throw new Exception("mutation continued after disabling it again");
        Console.WriteLine($"MUTATION_SWITCH disabled=unchanged enabledTemp={F(adaptedTemperature)} disabledAgain=unchanged");
        Run("single-herbivore", Block(100, 1000f, Pop(0, 10)), Controller(), 365,
            new[] { 1, 7, 30, 100, 365 });
        Run("herbivore-predator", Block(1000, 10000f, Pop(0, 50), Pop(1, 3)), Controller(), 100,
            new[] { 1, 5, 10, 20, 50, 100 });
        Run("two-level-long", Block(1000, 10000f, Pop(0, 50), Pop(1, 3)), Controller(), 2000,
            new[] { 365, 1000, 2000 });
        Run("two-level-with-mutation", Block(1000, 10000f, Pop(0, 50), Pop(1, 3)),
            Controller(true), 2000, new[] { 365, 1000, 2000 });
        Run("three-level", Block(2000, 10000f, Pop(0, 100), Pop(1, 10), Pop(2, 2)), Controller(), 100,
            new[] { 1, 5, 10, 20, 50, 100 });
        Run("one-large-prey", Block(100, 1000f, Pop(0, 1, 100, 0, 0), Pop(1, 1, 10, 10, 0)),
            Controller(), 1, new[] { 1 });
        Run("no-resource", Block(0, 0f, Pop(0, 100)), Controller(), 20,
            new[] { 1, 2, 3, 5, 10, 20 });
        var changing = Block(1000, 10000f, Pop(0, 5));
        var changingController = Controller(true);
        changingController.SetParameters(0, 0, 0);
        Run("environment-shift", changing, changingController, 2000,
            new[] { 1, 99, 100, 130, 500, 1000, 2000 },
            (day, b) => { if (day == 100) { b.temperature = 100; b.humidity = 100; } });
        var fatalShift = Block(1000, 10000f, Pop(0, 5));
        Run("environment-shift-default-mortality", fatalShift, Controller(true), 200,
            new[] { 99, 100, 110, 120, 130, 160, 200 },
            (day, b) => { if (day == 100) { b.temperature = 100; b.humidity = 100; } });
        foreach (int change in new[] { 10, 25, 50 })
        {
            UnityEngine.Random.InitState(42);
            var b = Block(1000, 10000f, Pop(0, 10));
            var c = Controller(true);
            int extinctionDay = 0;
            int atShift = 0;
            for (int day = 1; day <= 1000; day++)
            {
                if (day == 100)
                {
                    atShift = b.community[0].speciesAmount;
                    b.temperature += change;
                    b.humidity += change;
                }
                c.SimulateBlock(b);
                if (extinctionDay == 0 && b.community[0].speciesAmount == 0)
                    extinctionDay = day;
            }
            Console.WriteLine($"SHIFT deltaEach={change} N_before={atShift} extinctDay={extinctionDay} N_day1000={b.community[0].speciesAmount} adaptedTemp={F(b.community[0].fitTemperature + b.community[0].temperatureMutationRemainder)}");
        }
        ShiftEnsemble(10, 50, 365);
        ShiftEnsemble(25, 50, 365);
        ShiftEnsemble(50, 50, 365);
        DirectionEnsemble(10, 50, 730);
        DirectionEnsemble(25, 50, 730);
        DirectionEnsemble(-25, 50, 730);
        SelectionStrengthCheck(0, 1000);
        SelectionStrengthCheck(1, 1000);

        foreach (int recovery in new[] { 1000, 10000 })
        foreach (int predatorCount in new[] { 1, 3 })
        {
            UnityEngine.Random.InitState(42);
            int initialPrey = recovery / 40;
            var chain = Block(recovery, 10000f, Pop(0, initialPrey), Pop(1, predatorCount));
            var ctrl = Controller();
            int preyExtinct = 0, predatorExtinct = 0, peakPredators = predatorCount;
            for (int day = 1; day <= 365; day++)
            {
                ctrl.SimulateBlock(chain);
                if (preyExtinct == 0 && chain.community[0].speciesAmount == 0) preyExtinct = day;
                if (predatorExtinct == 0 && chain.community[1].speciesAmount == 0) predatorExtinct = day;
                peakPredators = Math.Max(peakPredators, chain.community[1].speciesAmount);
            }
            Console.WriteLine($"SWEEP recovery={recovery} prey0={initialPrey} predator0={predatorCount} preyExtinctDay={preyExtinct} predatorExtinctDay={predatorExtinct} peakPredators={peakPredators} finalPrey={chain.community[0].speciesAmount} finalPredators={chain.community[1].speciesAmount}");
        }
        Run("three-level-productive", Block(10000, 10000f,
                Pop(0, 500), Pop(1, 20), Pop(2, 1)), Controller(), 2000,
            new[] { 1, 30, 100, 365, 1000, 2000 });
        Run("three-level-productive-with-mutation", Block(10000, 10000f,
                Pop(0, 500), Pop(1, 20), Pop(2, 1)), Controller(true), 2000,
            new[] { 365, 1000, 2000 });
        ChainEnsemble(false, 50, 2000);
        ChainEnsemble(true, 50, 2000);

        var plantForaging = Block(0, 100f,
            Pop(0, 10, 10, 0, 0), Pop(0, 10, 10, 100, 0));
        var movementController = Controller();
        movementController.SetParameters(0, 0, 0);
        Run("movement-plant-competition", plantForaging, movementController, 1,
            new[] { 1 });
        if (plantForaging.community[1].actualEnergySatisfactionToday <=
            plantForaging.community[0].actualEnergySatisfactionToday)
            throw new Exception("movement did not improve plant foraging");
        var preyForaging = Block(0, 1000f,
            Pop(0, 20, 10, 10, 0), Pop(1, 20, 10, 0, 0), Pop(1, 20, 10, 100, 0));
        Run("movement-hunt-competition", preyForaging, movementController, 1,
            new[] { 1 });
        if (preyForaging.community[2].actualEnergySatisfactionToday <=
            preyForaging.community[1].actualEnergySatisfactionToday)
            throw new Exception("movement did not improve hunting");

        var stressRandom = new System.Random(20261003);
        int extinctCommunities = 0;
        for (int scenario = 0; scenario < 200; scenario++)
        {
            var members = new List<PopulationData>();
            int count = stressRandom.Next(1, 6);
            for (int i = 0; i < count; i++)
            {
                var p = Pop(stressRandom.Next(0, 4), stressRandom.Next(0, 201),
                    stressRandom.Next(0, 101), stressRandom.Next(0, 101), stressRandom.Next(0, 101));
                p.fitTemperature = stressRandom.Next(0, 101);
                p.fitHumidity = stressRandom.Next(0, 101);
                members.Add(p);
            }
            int recovery = stressRandom.Next(0, 10001);
            var b = Block(recovery, stressRandom.Next(0, 10001), members.ToArray());
            b.temperature = stressRandom.Next(0, 101);
            b.humidity = stressRandom.Next(0, 101);
            var c = Controller(true);
            for (int day = 0; day < 365; day++)
            {
                c.SimulateBlock(b);
                if (!float.IsFinite(b.plantBiomass) || b.plantBiomass < -0.01f ||
                    b.plantBiomass > b.maxPlantBiomass + 0.01f)
                    throw new Exception($"plant invariant: scenario={scenario}, day={day}");
                foreach (var p in members)
                {
                    if (p.speciesAmount < 0 || p.fitTemperature < 0 || p.fitTemperature > 100 ||
                        p.fitHumidity < 0 || p.fitHumidity > 100 || p.size < 0 || p.size > 100 ||
                        p.movementAbility < 0 || p.movementAbility > 100 ||
                        p.fertility < 0 || p.fertility > 100 ||
                        p.trophicLevel < 0 || p.trophicLevel > 2 ||
                        !float.IsFinite(p.allocatedBiomass) || !float.IsFinite(p.carryingCapacity))
                        throw new Exception($"population invariant: scenario={scenario}, day={day}");
                }
            }
            if (members.All(p => p.speciesAmount == 0)) extinctCommunities++;
        }
        Console.WriteLine($"STRESS scenarios=200 days=365 invalid=0 extinctCommunities={extinctCommunities}");

        var huge = Block(int.MaxValue, 3000000000f, Pop(0, 1000000000, 0, 0, 100));
        huge.maxPlantBiomass = 3000000000f;
        var hugeController = Controller();
        hugeController.SetParameters(1.5f, 0, 0);
        Run("large-population", huge, hugeController, 1, new[] { 1 });
    }
}
