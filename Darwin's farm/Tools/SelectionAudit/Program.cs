using System;
using System.Collections.Generic;
using System.Reflection;

internal static class Program
{
    private static SpeciesData Species(string name, int t, int h, int m, int s, int f) =>
        new SpeciesData { name = name, baseFitTemperature = t,
            baseFitHumidity = h, baseMovementAbility = m, baseSize = s,
            baseFertility = f, trophicLevel = 0 };

    private static PopulationData Population(SpeciesData species, int count) =>
        new PopulationData { species = species, speciesAmount = count,
            fitTemperature = species.baseFitTemperature,
            fitHumidity = species.baseFitHumidity,
            movementAbility = species.baseMovementAbility,
            size = species.baseSize, fertility = species.baseFertility,
            trophicLevel = 0, trophicLevelInitialized = true };

    private static BlockInfo Block(int t, int h, int renewal, float stock,
        params PopulationData[] groups) => new BlockInfo {
            temperature = t, humidity = h, habitatRecovery = renewal,
            plantBiomass = stock, maxPlantBiomass = 1000000f,
            community = new List<PopulationData>(groups) };

    private static SimulationController Controller(BlockInfo block, float birthRate = .1f)
    {
        var controller = new SimulationController();
        controller.SetBlocks(new List<BlockInfo> { block });
        controller.SetMigrationParameters(false, 7, 100f, .8f, .5f, .2f, 7);
        var rules = controller.ReadLandRules();
        rules.birthRate = birthRate;
        controller.ApplyLandRules(rules);
        return controller;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static string Traits(PopulationData p) =>
        $"{p.fitTemperature}/{p.fitHumidity}/{p.movementAbility}/{p.size}/{p.fertility}";

    private static void StablePair(bool deer, float birthRate, bool stress)
    {
        var chickenSpecies = Species("鸡", 61, 50, 20, 8, 75);
        var chicken = Population(chickenSpecies, stress ? 50000 : deer ? 200 : 160);
        var deerPopulation = Population(Species("鹿", 40, 25, 75, 60, 30), 20);
        if (deer) chicken.splitStrategy = -1;
        var block = deer ? Block(35, 30, 100000, 1000000f, chicken, deerPopulation) :
            Block(68, 68, 100000, stress ? 50000f : 100000f, chicken);
        if (deer) block.nextLocalSplitDay = int.MaxValue;
        var controller = Controller(block, birthRate);
        int splitDay = 0;
        float splitRatio = 0f;
        int mutationEvents = 0;
        controller.OnLocalSplitResolved += result => splitDay = result.Day;
        controller.OnEcologyEvent += (day, tile, population, kind, detail) =>
        {
            if (kind == "trait_mutation")
            {
                mutationEvents++;
                Check(day > 0 && detail.Contains("perCapitaGain=") &&
                    detail.Contains("threshold="),
                    "mutation log lost its day or effect");
            }
            if (kind != "local_split") return;
            const string key = "countToEnvironmentalK=";
            int at = detail.IndexOf(key, StringComparison.Ordinal);
            if (at >= 0) float.TryParse(detail.Substring(at + key.Length),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out splitRatio);
        };
        float minimumStock = float.MaxValue;
        float stableStockLow = float.MaxValue;
        float stableStockHigh = 0f;
        int[] low = { int.MaxValue, int.MaxValue };
        int[] high = { 0, 0 };
        string[] fixedTraits = null;
        for (int day = 1; day <= 20000; day++)
        {
            controller.SimulateDay(day);
            minimumStock = Math.Min(minimumStock, block.plantBiomass);
            if (day == 5000)
            {
                Check(block.community.Count == 2, "expected two local populations by day 5000");
                fixedTraits = new[] { Traits(block.community[0]), Traits(block.community[1]) };
            }
            if (day > 5000)
            {
                stableStockLow = Math.Min(stableStockLow, block.plantBiomass);
                stableStockHigh = Math.Max(stableStockHigh, block.plantBiomass);
                for (int i = 0; i < 2; i++)
                {
                    Check(Traits(block.community[i]) == fixedTraits[i],
                        "trait drift after settled day 5000");
                    low[i] = Math.Min(low[i], block.community[i].speciesAmount);
                    high[i] = Math.Max(high[i], block.community[i].speciesAmount);
                }
            }
        }
        Check(minimumStock > 0f, "plant inventory emptied");
        Check(stableStockHigh - stableStockLow < 200f,
            "plant inventory failed to settle");
        Check(high[0] - low[0] < 8 && high[1] - low[1] < 8,
            "populations failed to settle");
        Check(low[0] > high[1] * 2, "small chicken population should be larger");
        Check(mutationEvents > 0, "actual mutation events were not logged");
        if (!deer) Check(splitDay > 0 && splitRatio >= .9f,
            "chicken split below 90% of environmental K");
        Console.WriteLine($"{(deer ? "chicken_deer" : "two_chickens")} birth={birthRate} stress={stress} " +
            $"split={splitDay} splitRatio={splitRatio:F3} mutations={mutationEvents} low={low[0]}/{low[1]} " +
            $"high={high[0]}/{high[1]} stock={block.plantBiomass:F0} " +
            $"traits={fixedTraits[0]}|{fixedTraits[1]}");
    }

    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--decline-probe")
        {
            var chicken = Population(Species("鸡", 55, 35, 10, 8, 70), 160);
            var rainforest = Block(75, 75, 130000, 130000f, chicken);
            var simulation = Controller(rainforest);
            for (int day = 1; day <= 100 && rainforest.community.Count > 0; day++)
            {
                simulation.SimulateDay(day);
                if (day % 10 == 0)
                    Console.WriteLine($"rainforest day={day} count={chicken.speciesAmount} traits={Traits(chicken)} trend={LandLifeRules.Average(chicken.populationGrowthHistory):F4} births={chicken.birthsToday} deaths={chicken.deathsToday} food={chicken.energySatisfactionToday:F3} fitness={chicken.environmentalFitness} K={chicken.environmentalCarryingCapacity:F0} plants={rainforest.plantBiomass:F0}");
                if (day == 30)
                    Check(chicken.fitTemperature > 55 && chicken.movementAbility >= 10 &&
                        chicken.size < 20, "declining chicken ignored growth-first selection");
            }
            Check(chicken.speciesAmount >= 100,
                "rainforest chicken collapsed despite growth-first selection");
            return;
        }
        var defaults = new SimulationController();
        var tuning = defaults.ReadTuning();
        var land = defaults.ReadLandRules();
        Check(Math.Abs(land.birthRate - .1f) < .000001f &&
            tuning.mutationInterval == 1 &&
            Math.Abs(tuning.mutationStep - 1f) < .000001f &&
            Math.Abs(land.mutationGainThreshold - .00002f) < .0000001f,
            "default birth and mutation tuning changed");
        var legacy = new LandRuleTuning { reproductionStressPerBirth = 0f,
            smallBodyMaintenance = 0f, densityBirthDampingStart = .5f };
        typeof(LandRuleTuning).GetField("ruleVersion",
            BindingFlags.Instance | BindingFlags.NonPublic).SetValue(legacy, 0);
        defaults.ApplyLandRules(legacy);
        var migrated = defaults.ReadLandRules();
        Check(migrated.reproductionStressPerBirth == 1.2f &&
            migrated.smallBodyMaintenance == 200f &&
            migrated.densityBirthDampingStart == .85f,
            "old Unity tuning did not adopt the new defaults");
        StablePair(false, .1f, false);
        StablePair(true, .1f, false);
        StablePair(false, .1f, true);
        StablePair(true, .05f, false);
        var slow = Block(68, 68, 100000, 100000f,
            Population(Species("鸡", 61, 50, 20, 8, 75), 160));
        var slowController = Controller(slow, .05f);
        for (int day = 1; day <= 5000; day++) slowController.SimulateDay(day);
        Check(slow.community.Count == 1 && slow.plantBiomass > 0f,
            "birth rate .05 should remain below the split gate");
        var drought = Block(68, 68, 0, 10000f,
            Population(Species("鸡", 68, 68, 20, 8, 75), 5000));
        Controller(drought).SimulateDay(1);
        Check(drought.plantBiomass < 10000f,
            "zero renewal should allow real stock depletion");
        var climber = Population(Species("鸡", 50, 50, 20, 8, 75), 1000);
        var changedClimate = Block(68, 68, 100000, 1000000f, climber);
        changedClimate.nextLocalSplitDay = int.MaxValue;
        var climateController = Controller(changedClimate);
        for (int day = 1; day <= 1000; day++) climateController.SimulateDay(day);
        Check(climber.fitTemperature > 50 && climber.fitHumidity > 50,
            "temperature and humidity traits should approach changed climate");
        Console.WriteLine($"climate_adaptation t={climber.fitTemperature} h={climber.fitHumidity}");
    }
}
