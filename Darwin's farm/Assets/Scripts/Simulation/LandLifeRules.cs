using System.Collections.Generic;
using UnityEngine;

// The land food ledger. Every consumer of a resource is considered before it is spent.
internal static class LandLifeRules
{
    internal const float BirthRate = 0.1f;
    internal const float StarvationRate = 0.60f;
    internal const int HistoryDays = 10;
    private const float Epsilon = 0.0001f;

    internal static float AdultNeed(float size, float movement,
        LandRuleTuning rules = null)
    {
        float m = Mathf.Clamp(movement, 0f, 100f) / 100f;
        return Mathf.Max(0f, size) * (1f +
            (rules != null ? rules.movementFoodCost : 0.4f) * m * m);
    }

    internal static float YoungNeed(float size, LandRuleTuning rules = null) =>
        (rules != null ? rules.youngLinearCost : 0.4f) * size
        + (rules != null ? rules.youngQuadraticCost : 0.045f) * size * size;

    internal static float Fitness(BlockInfo block, float temperature, float humidity) =>
        Mathf.Clamp01(1f - (Mathf.Abs(block.temperature - temperature)
            + Mathf.Abs(block.humidity - humidity)) / 90f);

    internal static float Demand(PopulationData p, int count, float fitness,
        LandRuleTuning rules = null)
    {
        float births = count * (rules != null ? rules.birthRate : BirthRate)
            * p.fertility / 100f * fitness;
        return count * AdultNeed(p.size, p.movementAbility, rules)
            + births * YoungNeed(p.size, rules);
    }

    internal static float Demand(int count, float size, float movement,
        float fertility, float fitness, LandRuleTuning rules = null) =>
        count * AdultNeed(size, movement, rules)
        + count * (rules != null ? rules.birthRate : BirthRate)
            * fertility / 100f * fitness * YoungNeed(size, rules);

    internal static void AddHistory(List<float> history, float value)
    {
        if (history == null) return;
        history.Add(value);
        if (history.Count > HistoryDays) history.RemoveAt(0);
    }

    internal static float Average(List<float> history, float fallback = 0f)
    {
        if (history == null || history.Count == 0) return fallback;
        float sum = 0f;
        foreach (float value in history) sum += value;
        return sum / history.Count;
    }

    private static int Strategy(PopulationData p)
    {
        float ratio = 100f * p.size / Mathf.Max(10f, p.fertility);
        return ratio < 30f ? -1 : ratio >= 60f ? 1 : 0;
    }

    private static float Competition(PopulationData a, PopulationData b) =>
        Strategy(a) * Strategy(b) == -1 ? 0.5f : 1f;

    private static float ForagingWeight(PopulationData p, float adultDemand,
        float fitness, float pressure) => adultDemand * fitness
        * (1f + 0.002f * (p.movementAbility - 20f))
        / (1f + 2f * Mathf.Max(0f, pressure - 1f));

    // Water filling is capped by each group's full adult + young demand.
    private static float[] AllocatePlants(List<PopulationData> groups, float food,
        float renewal, BlockInfo block, PopulationData changed = null,
        float temperature = 0f, float humidity = 0f, float movement = 0f,
        float size = 0f, float fertility = 0f,
        LandRuleTuning rules = null)
    {
        int count = groups.Count;
        var demand = new float[count];
        var adult = new float[count];
        var weight = new float[count];
        var allocation = new float[count];
        for (int i = 0; i < count; i++)
        {
            PopulationData p = groups[i];
            bool overrideTrait = p == changed;
            float t = overrideTrait ? temperature : p.fitTemperature;
            float humid = overrideTrait ? humidity : p.fitHumidity;
            float mov = overrideTrait ? movement : p.movementAbility;
            float sz = overrideTrait ? size : p.size;
            float fert = overrideTrait ? fertility : p.fertility;
            float fitness = Fitness(block, t, humid);
            adult[i] = p.speciesAmount * AdultNeed(sz, mov, rules);
            demand[i] = Demand(p.speciesAmount, sz, mov, fert, fitness,
                rules);
            float pressure = adult[i];
            for (int j = 0; j < count; j++)
                if (i != j)
                {
                    PopulationData other = groups[j];
                    float otherSize = other == changed ? size : other.size;
                    float otherFertility = other == changed ? fertility : other.fertility;
                    int ownStrategy = Strategy(sz, fert);
                    int otherStrategy = Strategy(otherSize, otherFertility);
                    float competition = ownStrategy * otherStrategy == -1
                        ? (rules != null ? rules.oppositeStrategyCompetition : 0.5f) : 1f;
                    pressure += competition * other.speciesAmount
                        * AdultNeed(otherSize, other == changed ? movement : other.movementAbility,
                            rules);
                }
            weight[i] = adult[i] * fitness
                * (1f + (rules != null ? rules.movementForagingGain : 0.002f)
                    * (mov - 20f))
                / (1f + (rules != null ? rules.competitionPressure : 2f)
                    * Mathf.Max(0f, pressure / Mathf.Max(1f, renewal) - 1f));
        }
        for (int pass = 0; pass < count && food > Epsilon; pass++)
        {
            float total = 0f;
            for (int i = 0; i < count; i++)
                if (demand[i] - allocation[i] > Epsilon) total += weight[i];
            if (total <= Epsilon) break;
            float spent = 0f;
            for (int i = 0; i < count; i++)
            {
                float share = Mathf.Min(Mathf.Max(0f, demand[i] - allocation[i]),
                    food * weight[i] / total);
                allocation[i] += share;
                spent += share;
            }
            food -= spent;
            if (spent <= Epsilon) break;
        }
        return allocation;
    }

    private static int Strategy(float size, float fertility)
    {
        float ratio = 100f * size / Mathf.Max(10f, fertility);
        return ratio < 30f ? -1 : ratio >= 60f ? 1 : 0;
    }

    internal static float ForecastPlantIntake(BlockInfo block, PopulationData target,
        float temperature, float humidity, float movement, float size,
        float fertility, bool renewalOnly, LandRuleTuning rules = null)
    {
        var groups = new List<PopulationData>(5);
        foreach (PopulationData p in block.community)
            if (p != null && p.trophicLevel == 0 && p.speciesAmount > 0)
                groups.Add(p);
        int index = groups.IndexOf(target);
        if (index < 0) return 0f;
        float food = renewalOnly ? Mathf.Max(0, block.habitatRecovery)
            : block.plantBiomass + block.consumedBiomassToday;
        return AllocatePlants(groups, food, block.habitatRecovery, block,
            target, temperature, humidity, movement, size, fertility, rules)[index];
    }

    internal static float CaptureChance(PopulationData hunter, PopulationData prey,
        LandRuleTuning rules = null)
    {
        float chase = Mathf.Clamp(1f + (hunter.movementAbility
            - prey.movementAbility) / Mathf.Max(1f,
                rules != null ? rules.chaseScale : 40f), 0.1f, 1.5f);
        float size = (hunter.size + 10f) / (prey.size + 10f);
        return Mathf.Clamp01((rules != null ? rules.baseCaptureChance : 0.5f) * chase
            * Mathf.Clamp(size * size, 0.1f, 1.5f));
    }

    internal static float PreyEnergy(PopulationData hunter, PopulationData prey,
        LandRuleTuning rules = null) =>
        (rules != null ? rules.preyEnergyPerSize : 30f) * prey.size
        * Mathf.Min(1f, hunter.size / (prey.size + 15f));

    private static void HuntLevel(BlockInfo block, int level, LandRuleTuning rules)
    {
        var hunters = new List<PopulationData>(5);
        var prey = new List<PopulationData>(5);
        foreach (PopulationData p in block.community)
        {
            if (p.speciesAmount <= 0) continue;
            if (p.trophicLevel == level) hunters.Add(p);
            if (p.trophicLevel < level) prey.Add(p);
        }
        if (hunters.Count == 0 || prey.Count == 0) return;
        int h = hunters.Count, q = prey.Count;
        var intent = new float[h, q];
        var score = new float[h, q];
        var targetEnergy = new float[h];
        for (int i = 0; i < h; i++)
        {
            PopulationData hunter = hunters[i];
            float adult = hunter.speciesAmount * AdultNeed(hunter.size,
                hunter.movementAbility, rules);
            float births = hunter.speciesAmount * rules.birthRate
                * hunter.fertility / 100f
                * Fitness(block, hunter.fitTemperature, hunter.fitHumidity);
            targetEnergy[i] = Mathf.Max(0f, adult + births * YoungNeed(hunter.size,
                rules) + adult * rules.reserveHuntFraction - hunter.energyReserve);
            float totalScore = 0f;
            for (int j = 0; j < q; j++)
            {
                if (!FoodWeb.CanEat(hunter, prey[j])) continue;
                score[i, j] = Mathf.Min(1f, 10f / Mathf.Max(1f, prey[j].size))
                    * CaptureChance(hunter, prey[j], rules)
                    * PreyEnergy(hunter, prey[j], rules);
                totalScore += score[i, j];
            }
            if (totalScore <= 0f) continue;
            for (int j = 0; j < q; j++)
                intent[i, j] = hunter.speciesAmount * score[i, j] / totalScore
                    * Mathf.Min(1f, 10f / Mathf.Max(1f, prey[j].size))
                    * CaptureChance(hunter, prey[j], rules);
        }
        // Each submitted attempt is committed once against the live prey ledger.
        for (int j = 0; j < q; j++)
            {
                PopulationData victim = prey[j];
                if (victim.speciesAmount <= 0) continue;
                float total = 0f;
                float totalWeight = 0f;
                var claims = new float[h];
                var weights = new float[h];
                int lastEligible = -1;
                for (int i = 0; i < h; i++)
                    if (targetEnergy[i] > Epsilon && intent[i, j] > 0f)
                    {
                        claims[i] = Mathf.Min(intent[i, j], targetEnergy[i]
                            / Mathf.Max(1f, PreyEnergy(hunters[i], victim, rules)));
                        total += claims[i];
                        lastEligible = i;
                    }
                if (total <= Epsilon) continue;
                for (int i = 0; i < h; i++)
                {
                    if (claims[i] <= 0f) continue;
                    float pressure = claims[i];
                    for (int k = 0; k < h; k++)
                        if (k != i) pressure += claims[k]
                            * (Strategy(hunters[i]) * Strategy(hunters[k]) == -1
                                ? rules.oppositeStrategyCompetition : 1f);
                    weights[i] = claims[i] / (1f + rules.competitionPressure
                        * Mathf.Max(0f, pressure
                            / Mathf.Max(1f, victim.speciesAmount) - 1f));
                    totalWeight += weights[i];
                }
                float expected = Mathf.Min(victim.speciesAmount,
                    total + victim.predationRemainder);
                int kills = Mathf.Min(victim.speciesAmount, Mathf.FloorToInt(expected));
                victim.predationRemainder = expected - kills;
                if (kills <= 0) continue;
                victim.speciesAmount -= kills;
                victim.predationDeathsToday += kills;
                victim.deathsToday += kills;
                float remaining = kills;
                for (int i = 0; i < h; i++)
                {
                    if (intent[i, j] <= 0f || targetEnergy[i] <= Epsilon) continue;
                    float share = i == lastEligible ? remaining
                        : kills * weights[i] / Mathf.Max(Epsilon, totalWeight);
                    share = Mathf.Min(remaining, share);
                    remaining -= share;
                    float energy = share * PreyEnergy(hunters[i], victim, rules);
                    hunters[i].huntingEnergyToday += energy;
                    hunters[i].energyReserve += energy;
                    targetEnergy[i] = Mathf.Max(0f, targetEnergy[i] - energy);
                }
            }
    }

    internal static void Simulate(BlockInfo block, LandRuleTuning rules)
    {
        FoodWeb.GrowPlants(block);
        var herbivores = new List<PopulationData>(5);
        foreach (PopulationData p in block.community)
        {
            p.populationBeforePredation = p.speciesAmount;
            p.predationDeathsToday = p.birthsToday = p.deathsToday = 0;
            p.huntingEnergyToday = 0f;
            p.reserveAtDayStart = p.energyReserve;
            p.allocatedBiomass = p.carryingCapacity = 0f;
            p.environmentalFitness = Mathf.RoundToInt(100f
                * Fitness(block, p.fitTemperature, p.fitHumidity));
            p.energyNeed = AdultNeed(p.size, p.movementAbility, rules);
        }
        HuntLevel(block, 2, rules);
        HuntLevel(block, 1, rules);
        foreach (PopulationData p in block.community)
            if (p.trophicLevel == 0 && p.speciesAmount > 0) herbivores.Add(p);
        float[] allocation = AllocatePlants(herbivores, block.plantBiomass,
            block.habitatRecovery, block, rules: rules);
        float[] renewal = AllocatePlants(herbivores,
            Mathf.Max(0, block.habitatRecovery), block.habitatRecovery, block,
            rules: rules);
        float eaten = 0f;
        for (int i = 0; i < herbivores.Count; i++)
        {
            PopulationData p = herbivores[i];
            p.allocatedBiomass = allocation[i];
            eaten += allocation[i];
            float demand = Demand(p, p.speciesAmount,
                Fitness(block, p.fitTemperature, p.fitHumidity), rules);
            AddHistory(p.renewalSatisfactionHistory, demand > 0f
                ? Mathf.Clamp01(renewal[i] / demand) : 1f);
        }
        FoodWeb.SpendPlants(block, eaten);
        foreach (PopulationData p in block.community)
        {
            int count = p.speciesAmount; // after predation, before births
            if (p.trophicLevel > 0)
            {
                float demand = Demand(p, count,
                    Fitness(block, p.fitTemperature, p.fitHumidity), rules);
                p.allocatedBiomass = Mathf.Min(demand, p.energyReserve);
                p.energyReserve = Mathf.Max(0f, p.energyReserve - p.allocatedBiomass);
                float adult = count * AdultNeed(p.size, p.movementAbility, rules);
                p.energyReserve = Mathf.Min(p.energyReserve, adult * rules.reserveDays);
                float sustainableFood = SustainablePreyFood(block, p, rules);
                AddHistory(p.renewalSatisfactionHistory, demand > 0f
                    ? Mathf.Clamp01(sustainableFood / demand) : 1f);
            }
            if (count <= 0)
            {
                p.actualEnergySatisfactionToday = p.energySatisfactionToday = 0f;
                AddHistory(p.foodSatisfactionHistory, 0f);
                AddHistory(p.preyNetRenewalHistory, 0f);
                p.endangeredDays++;
                continue;
            }
            float fitness = Fitness(block, p.fitTemperature, p.fitHumidity);
            float demandToday = Demand(p, count, fitness, rules);
            float satisfaction = demandToday > 0f
                ? Mathf.Clamp01(p.allocatedBiomass / demandToday) : 1f;
            p.actualEnergySatisfactionToday = p.energySatisfactionToday = satisfaction;
            p.carryingCapacity = p.energyNeed > 0f
                ? p.allocatedBiomass / p.energyNeed : 0f;
            float births = count * rules.birthRate * p.fertility / 100f
                * fitness * satisfaction;
            float deaths = rules.starvationDeathRate * count * (1f - satisfaction)
                + rules.coldDeathRate * count * Mathf.Max(0f, (50f - block.temperature) / 50f)
                    * (1f - p.size / 100f);
            p.birthRemainder += births;
            p.deathRemainder += deaths;
            int born = Mathf.FloorToInt(p.birthRemainder);
            int died = Mathf.Min(count, Mathf.FloorToInt(p.deathRemainder));
            p.birthRemainder -= born;
            p.deathRemainder -= died;
            p.birthsToday = born;
            p.deathsToday += died;
            p.speciesAmount = count + born - died;
            AddHistory(p.foodSatisfactionHistory, satisfaction);
            AddHistory(p.preyNetRenewalHistory, born - died);
            p.endangeredDays = p.speciesAmount < 10 ? p.endangeredDays + 1 : 0;
        }
    }

    internal static float SustainablePreyFood(BlockInfo block, PopulationData hunter,
        LandRuleTuning rules = null)
    {
        float total = 0f;
        foreach (PopulationData prey in block.community)
            if (prey != null && FoodWeb.CanEat(hunter, prey))
                total += Mathf.Max(0f, Average(prey.preyNetRenewalHistory))
                    * PreyEnergy(hunter, prey, rules);
        return total;
    }
}
