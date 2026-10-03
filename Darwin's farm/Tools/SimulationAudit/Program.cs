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
        if (ChangedTraits(start, slowHunter) != 1 || slowHunter.movementAbility <= 10)
            throw new Exception($"well-fed slower hunter did not select movement: K/N={F(slowHunter.energySatisfactionToday)} actual={F(slowHunter.actualEnergySatisfactionToday)} traits={string.Join(',', Traits(slowHunter))}");
        Console.WriteLine($"DIRECTED_MOVEMENT initial=10 final={F(Traits(slowHunter)[2])}");

        var scarcePrey = Pop(0, 500, 10, 60);
        var unfundedHunter = Pop(1, 5, 10, 10);
        var shortageBlock = Block(0, 10000f, scarcePrey, unfundedHunter);
        controller = Controller(true);
        controller.SetParameters(0f, 0f, 0f);
        for (int day = 0; day < 7; day++) controller.SimulateBlock(shortageBlock);
        if (unfundedHunter.movementAbility != 10 ||
            unfundedHunter.movementMutationRemainder != 0f)
            throw new Exception("unsustainable movement increase was not rejected");
        Console.WriteLine("MOVEMENT_ENERGY_GATE increase=rejected");

        var largePrey = Pop(0, 100, 30);
        var smallHunter = Pop(1, 5, 5);
        var sizeBlock = Block(10000, 10000f, largePrey, smallHunter);
        controller = Controller(true);
        for (int day = 0; day < 7; day++) controller.SimulateBlock(sizeBlock);
        if (smallHunter.size <= 5)
            throw new Exception("well-fed smaller hunter did not select size");
        Console.WriteLine($"DIRECTED_SIZE initial=5 final={F(Traits(smallHunter)[3])}");

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
        controller.SimulateBlock(smallHunterBlock);
        controller.SimulateBlock(largeHunterBlock);
        if (largeHunterBlock.community[1].allocatedBiomass <=
            smallHunterBlock.community[1].allocatedBiomass)
            throw new Exception("hunter size did not improve capture efficiency");
        Console.WriteLine($"CAPTURE_SIZE smallHunterEnergy={F(smallHunterBlock.community[1].allocatedBiomass)} largeHunterEnergy={F(largeHunterBlock.community[1].allocatedBiomass)}");

        var strandedHunter = Pop(1, 10);
        var plantAlternative = Block(1000, 10000f, strandedHunter);
        controller = Controller(true);
        controller.SetParameters(0f, 0f, 0f);
        controller.SetMutationParameters(1, 2f, 1f);
        for (int day = 0; day < 3; day++) controller.SimulateBlock(plantAlternative);
        if (strandedHunter.trophicLevel != 0)
            throw new Exception("hungry hunter did not move toward accessible adjacent food");
        Console.WriteLine($"TROPHIC_ALTERNATIVE initial=1 final={strandedHunter.trophicLevel}");

        var plantCompetitor = Pop(0, 500, 100);
        var wouldBeHunter = Pop(0, 10);
        var foodChoice = Block(10000, 10000f, plantCompetitor, wouldBeHunter);
        controller = Controller(true);
        controller.SetMutationParameters(1, 2f, 1f);
        controller.SimulateBlock(foodChoice);
        if (wouldBeHunter.trophicLevel + wouldBeHunter.trophicMutationRemainder <= 0f)
            throw new Exception("crowded herbivore did not select accessible prey");
        Console.WriteLine($"TROPHIC_UP alternative={F(Traits(wouldBeHunter)[5])}");

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
        var fertilityBlock = Block(1000, 10000f, declining);
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
            throw new Exception($"player recovery reduction did not select smaller hunter size: predatorN={changingHunter.speciesAmount} preyN={changingPrey.speciesAmount} K/N={F(changingHunter.energySatisfactionToday)} actual={F(changingHunter.actualEnergySatisfactionToday)} size={F(Traits(changingHunter)[3])}");
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

        float previousHuntEnergy = float.MaxValue;
        int huntComparisons = 0;
        foreach (int preyMovement in new[] { 0, 25, 50, 75, 100 })
        {
            var prey = Pop(0, 20, 10, preyMovement, 0);
            var hunter = Pop(1, 100, 10, 50, 0);
            var block = Block(0, 1000f, prey, hunter);
            var controller = Controller();
            controller.SetParameters(0f, 0f, 0f);
            controller.SimulateBlock(block);
            if (hunter.allocatedBiomass >= previousHuntEnergy)
                throw new Exception($"faster prey did not lower hunting gain: movement={preyMovement}");
            previousHuntEnergy = hunter.allocatedBiomass;
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
                Math.Clamp(oldStock, 0f, b.maxPlantBiomass) + Math.Max(0, b.habitatRecovery))
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

    static void Main()
    {
        DirectedMutationTests();
        EnvironmentGrid();
        CompetitionSweep();
        for (int configuration = 0; configuration < 20; configuration++)
        {
            var stablePopulation = Pop(0, 60 + configuration);
            var stableBlock = Block(1000, 10000f, stablePopulation);
            var stableController = Controller(true);
            for (int day = 0; day < 2000; day++) stableController.SimulateBlock(stableBlock);
            if (stablePopulation.speciesAmount <= 0 || stablePopulation.size != 10 ||
                stablePopulation.sizeMutationRemainder != 0f ||
                stablePopulation.fertility != 50 ||
                stablePopulation.fertilityMutationRemainder != 0f ||
                stablePopulation.fitTemperature != 50 || stablePopulation.fitHumidity != 50 ||
                stablePopulation.movementAbility != 10 || stablePopulation.trophicLevel != 0)
                throw new Exception($"suitable environment drifted: configuration={configuration}");
        }
        Console.WriteLine("STABLE suitable environment configurations=20 days=2000 traits=unchanged");

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
