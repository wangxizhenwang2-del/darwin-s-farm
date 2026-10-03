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
            movementAbility = movement, fertility = fertility, habitatNiche = 50
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
        c.SetMutationParameters(30, mutation ? 1f : 0f, 1f);
        return c;
    }

    static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

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

    static void MutationEnsemble(float strength, int seeds, int days)
    {
        double temp = 0, humidity = 0, size = 0, trophic = 0;
        for (int seed = 0; seed < seeds; seed++)
        {
            UnityEngine.Random.InitState(seed);
            var p = Pop(0, 1);
            var b = Block(1000, 10000f, p);
            b.temperature = 100;
            b.humidity = 50;
            var c = Controller();
        c.SetParameters(0, 0, 0);
            c.SetMutationParameters(1, 1, strength);
            for (int day = 0; day < days; day++) c.SimulateBlock(b);
            if (strength == 0f && (p.fitTemperature != 50 || p.fitHumidity != 50 ||
                p.size != 10 || p.movementAbility != 10 || p.fertility != 50 ||
                p.trophicLevel != 0))
                throw new Exception("zero selection strength changed a trait");
            temp += p.fitTemperature + p.temperatureMutationRemainder;
            humidity += p.fitHumidity + p.humidityMutationRemainder;
            size += p.size + p.sizeMutationRemainder;
            trophic += p.trophicLevel + p.trophicMutationRemainder;
        }
        Console.WriteLine($"ENSEMBLE strength={strength} seeds={seeds} days={days} meanTemp={F((float)(temp/seeds))} meanHumidity={F((float)(humidity/seeds))} meanSize={F((float)(size/seeds))} meanTrophic={F((float)(trophic/seeds))}");
    }

    static void ChainEnsemble(bool threeLevels, int seeds, int days)
    {
        int allSurvive = 0;
        int preySurvive = 0;
        int middleSurvive = 0;
        int topSurvive = 0;
        for (int seed = 0; seed < seeds; seed++)
        {
            UnityEngine.Random.InitState(seed);
            var b = threeLevels
                ? Block(10000, 10000f, Pop(0, 500), Pop(1, 20), Pop(2, 2))
                : Block(1000, 10000f, Pop(0, 50), Pop(1, 3));
            var c = Controller(true);
            for (int day = 0; day < days; day++) c.SimulateBlock(b);
            if (b.community.All(p => p.speciesAmount > 0)) allSurvive++;
            if (b.community[0].speciesAmount > 0) preySurvive++;
            if (b.community[1].speciesAmount > 0) middleSurvive++;
            if (threeLevels && b.community[2].speciesAmount > 0) topSurvive++;
        }
        Console.WriteLine($"CHAIN_ENSEMBLE levels={(threeLevels ? 3 : 2)} seeds={seeds} days={days} allSurvive={allSurvive} preySurvive={preySurvive} middleSurvive={middleSurvive} topSurvive={topSurvive}");
        if (allSurvive < seeds * 9 / 10)
            throw new Exception("food chain survival below 90% of seeds");
    }

    static void ShiftEnsemble(int change, int seeds, int days)
    {
        int survivors = 0;
        long finalTotal = 0;
        for (int seed = 0; seed < seeds; seed++)
        {
            UnityEngine.Random.InitState(seed);
            var b = Block(1000, 10000f, Pop(0, 10));
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
        Console.WriteLine($"SHIFT_ENSEMBLE deltaEach={change} seeds={seeds} days={days} survivors={survivors} meanFinal={F(finalTotal / (float)seeds)}");
        if (change <= 25 && survivors < seeds * 9 / 10)
            throw new Exception("small environment shift caused too many extinctions");
        if (change >= 50 && survivors > seeds / 10)
            throw new Exception("large environment shift was not lethal");
    }

    static void DirectionEnsemble(int change, int seeds, int days)
    {
        int temperatureToward = 0, humidityToward = 0, survivors = 0;
        double temperatureChange = 0, humidityChange = 0;
        for (int seed = 0; seed < seeds; seed++)
        {
            UnityEngine.Random.InitState(seed);
            var population = Pop(0, 10);
            var block = Block(1000, 10000f, population);
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
        Console.WriteLine($"DIRECTION deltaEach={change} seeds={seeds} days={days} "
            + $"temperatureToward={temperatureToward} humidityToward={humidityToward} "
            + $"meanTemperatureChange={F((float)(temperatureChange / seeds))} "
            + $"meanHumidityChange={F((float)(humidityChange / seeds))} survivors={survivors}");
        if (temperatureToward < seeds * 9 / 10 || humidityToward < seeds * 9 / 10)
            throw new Exception("environmental mutation direction too random");
    }

    static void Main()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            UnityEngine.Random.InitState(seed);
            var stablePopulation = Pop(0, 100);
            var stableBlock = Block(1000, 10000f, stablePopulation);
            var stableController = Controller(true);
            for (int day = 0; day < 2000; day++) stableController.SimulateBlock(stableBlock);
            if (stablePopulation.speciesAmount <= 0 || stablePopulation.size != 10 ||
                stablePopulation.sizeMutationRemainder != 0f ||
                stablePopulation.fertility != 50 ||
                stablePopulation.fertilityMutationRemainder != 0f ||
                stablePopulation.fitTemperature != 50 || stablePopulation.fitHumidity != 50 ||
                stablePopulation.movementAbility != 10 || stablePopulation.trophicLevel != 0)
                throw new Exception($"suitable environment drifted: seed={seed}");
        }
        Console.WriteLine("STABLE suitable environment seeds=20 days=2000 traits=unchanged");

        UnityEngine.Random.InitState(42);
        var hungryPopulation = Pop(0, 100);
        var hungryBlock = Block(100, 100f, hungryPopulation);
        var hungryController = Controller(true);
        hungryController.SetParameters(0f, 0f, 0f);
        hungryController.SetMutationParameters(1, 10f, 1f);
        for (int day = 0; day < 200; day++) hungryController.SimulateBlock(hungryBlock);
        if (hungryPopulation.size < 7 || hungryPopulation.size > 10)
            throw new Exception("body size exceeded initial-size mutation bound");
        Console.WriteLine($"SIZE_BOUND initial=10 final={hungryPopulation.size} range=7..10");

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
        MutationEnsemble(0, 100, 1000);
        MutationEnsemble(1, 100, 1000);

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
            Pop(0, 20, 10, 10, 0), Pop(1, 10, 10, 0, 0), Pop(1, 10, 10, 100, 0));
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
