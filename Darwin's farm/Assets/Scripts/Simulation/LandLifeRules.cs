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
        return (Mathf.Max(0f, size) +
            (rules != null ? rules.smallBodyMaintenance : 0f) /
            Mathf.Max(1f, size)) * (1f +
            (rules != null ? rules.movementFoodCost : 0.4f) * m * m);
    }

    internal static float YoungNeed(float size, LandRuleTuning rules = null) =>
        (rules != null ? rules.youngLinearCost : 0.4f) * size
        + (rules != null ? rules.youngQuadraticCost : 0.045f) * size * size;

    internal static float BodyMass(float size, LandRuleTuning rules = null) =>
        Mathf.Max(1f, size) * (rules != null ? rules.preyEnergyPerSize : 40f);

    internal static void EnsureBodyMass(PopulationData population,
        LandRuleTuning rules)
    {
        if (population.bodyBiomassInitialized) return;
        if (population.speciesAmount > 0)
            population.bodyBiomass = population.speciesAmount *
                BodyMass(population.size, rules);
        population.bodyBiomassInitialized = true;
    }

    internal static float ForagingAccess(float size, float movement,
        LandRuleTuning rules)
    {
        float reach = rules.forageSizeScale /
            (rules.forageSizeScale + Mathf.Max(1f, size));
        float mobility = 1f + Mathf.Clamp(movement, 0f, 100f) /
            Mathf.Max(1f, rules.forageMovementScale);
        return Mathf.Clamp01(1f - rules.forageAccessLossAtZero * reach /
            (mobility * mobility));
    }

    internal static float NaturalAdultDeaths(int count, float size,
        LandRuleTuning rules) => count * (rules.baselineAdultDeathRate
            * rules.adultSizeMortalityScale /
            (rules.adultSizeMortalityScale + Mathf.Max(1f, size)) +
            rules.smallBodyFrailtyRate /
            ((1f + Mathf.Max(1f, size)) *
             (1f + Mathf.Max(1f, size))));

    internal static float ColdDeaths(int count, float temperature,
        LandRuleTuning rules) => rules.coldDeathRate * count
            * Mathf.Max(0f, (50f - temperature) / 50f);

    internal static float ReproductionStressDeaths(int count, float size,
        float fertility, LandRuleTuning rules, float birthFactor = 1f,
        float satisfaction = 1f, float fitness = 1f)
    {
        float drive = Mathf.Clamp01(fertility / 100f);
        float realizedBirths = count * rules.birthRate * drive
            * Mathf.Clamp01(birthFactor) * Mathf.Clamp01(satisfaction)
            * Mathf.Clamp01(fitness);
        return realizedBirths * rules.reproductionStressPerBirth * drive
            * YoungNeed(size, rules) / Mathf.Max(1f, size);
    }

    internal static float Fitness(BlockInfo block, float temperature, float humidity) =>
        Mathf.Clamp01(1f - (Mathf.Abs(block.temperature - temperature)
            + Mathf.Abs(block.humidity - humidity)) / 90f);

    internal static float Demand(PopulationData p, int count, float fitness,
        LandRuleTuning rules = null, float birthFactor = 1f)
    {
        float births = count * (rules != null ? rules.birthRate : BirthRate)
            * p.fertility / 100f * fitness;
        return count * AdultNeed(p.size, p.movementAbility, rules)
            + births * Mathf.Clamp01(birthFactor) *
                (YoungNeed(p.size, rules) + BodyMass(p.size, rules));
    }

    internal static float Demand(int count, float size, float movement,
        float fertility, float fitness, LandRuleTuning rules = null,
        float birthFactor = 1f) =>
        count * AdultNeed(size, movement, rules)
        + count * (rules != null ? rules.birthRate : BirthRate)
            * fertility / 100f * fitness * Mathf.Clamp01(birthFactor)
            * (YoungNeed(size, rules) + BodyMass(size, rules));

    internal static float HerbivoreAdultDemand(BlockInfo block,
        LandRuleTuning rules)
    {
        if (block == null || block.community == null) return 0f;
        float demand = 0f;
        foreach (PopulationData p in block.community)
            if (p != null && p.trophicLevel == 0 && p.speciesAmount > 0)
                demand += p.speciesAmount * AdultNeed(p.size,
                    p.movementAbility, rules);
        return demand;
    }

    internal static float EnvironmentalCarryingCapacity(BlockInfo block,
        PopulationData target, LandRuleTuning rules)
    {
        if (block == null || target == null || block.community == null)
            return 0f;
        if (rules.useFoodCapacityForSelection && target.trophicLevel == 0)
            return FoodCarryingCapacity(block, target, rules);
        int niche = PlantNiche(target);
        float renewable = target.trophicLevel == 0
            ? Mathf.Max(0f, block.habitatRecovery) *
                (niche <= 0 ? rules.smallPlantNicheShare :
                    1f - rules.smallPlantNicheShare) *
                NicheEfficiency(target.size, niche, rules) *
                (niche == 0 ? rules.unspecializedPlantEfficiency : 1f)
            : AvailableSustainablePreyFood(block, target, rules);
        if (renewable <= 0f) return 0f;
        float competitorDemand = 0f;
        foreach (PopulationData other in block.community)
            if (other != null && other != target && other.speciesAmount > 0 &&
                target.trophicLevel == 0 && other.trophicLevel == 0 &&
                PlantNiche(other) == niche)
            {
                competitorDemand += other.speciesAmount *
                    AdultNeed(other.size, other.movementAbility, rules);
            }
        float perIndividual = AdultNeed(target.size, target.movementAbility,
            rules);
        return Mathf.Max(0f, renewable - competitorDemand) /
            Mathf.Max(0.0001f, perIndividual);
    }

    internal static float SustainablePlantSupply(BlockInfo block,
        LandRuleTuning rules) => Mathf.Min(Mathf.Max(0, block.habitatRecovery),
            Mathf.Max(0f, block.maxPlantBiomass) *
            (1f - Mathf.Min(0.5f, rules.minimumPlantReserveFraction)));

    // Counterfactual, read-only adult maintenance boundary. R is the
    // sustainable daily plant input; today's standing S is excluded.
    internal static float FoodCarryingCapacity(BlockInfo block,
        PopulationData target, LandRuleTuning rules)
    {
        float supply = SustainablePlantSupply(block, rules);
        if (supply <= Epsilon) return 0f;
        var groups = new List<PopulationData>(5);
        foreach (PopulationData p in block.community)
            if (p != null && p.trophicLevel == 0 &&
                (p.speciesAmount > 0 || p == target)) groups.Add(p);
        int index = groups.IndexOf(target);
        if (index < 0) return 0f;
        float perHead = AdultNeed(target.size, target.movementAbility, rules);
        int upper = Mathf.Max(1, Mathf.CeilToInt(supply /
            Mathf.Max(Epsilon, perHead)) + 1);
        int low = 0, high = upper;
        while (low + 1 < high)
        {
            int mid = low + (high - low) / 2;
            float[] food = AllocatePlants(groups, supply, block,
                rules: rules, countOverride: target, overrideCount: mid,
                adultsOnly: true);
            float adult = mid * perHead;
            if (food[index] >= adult -
                Mathf.Max(0.01f, adult * 0.00001f)) low = mid;
            else high = mid;
        }
        return low;
    }

    internal static float CounterfactualPlantIntake(BlockInfo block,
        PopulationData target, PopulationData changedCount, int proposedCount,
        LandRuleTuning rules)
    {
        if (block == null || target == null || changedCount == null ||
            proposedCount < 0) return 0f;
        var groups = new List<PopulationData>(5);
        foreach (PopulationData p in block.community)
            if (p != null && p.trophicLevel == 0 &&
                (p.speciesAmount > 0 || p == changedCount || p == target))
                groups.Add(p);
        int index = groups.IndexOf(target);
        if (index < 0) return 0f;
        // R, not a one-day draw on accumulated S, makes this a renewable
        // resource counterfactual. No model object is changed.
        return AllocatePlants(groups, SustainablePlantSupply(block, rules),
            block, rules: rules,
            countOverride: changedCount, overrideCount: proposedCount)[index];
    }

    internal static float PlantAllocationOrderDifference(BlockInfo block,
        LandRuleTuning rules)
    {
        var groups = new List<PopulationData>();
        foreach (PopulationData p in block.community)
            if (p != null && p.trophicLevel == 0 && p.speciesAmount > 0)
                groups.Add(p);
        float supply = SustainablePlantSupply(block, rules);
        float[] forward = AllocatePlants(groups, supply, block, rules: rules);
        groups.Reverse();
        float[] reverse = AllocatePlants(groups, supply, block, rules: rules);
        float difference = 0f;
        for (int i = 0; i < groups.Count; i++)
            difference = Mathf.Max(difference,
                Mathf.Abs(forward[i] - reverse[groups.Count - 1 - i]));
        return difference;
    }

    internal static void RefreshEnvironmentMetrics(
        BlockInfo block, LandRuleTuning rules)
    {
        if (block == null || block.community == null) return;
        foreach (PopulationData population in block.community)
            if (population != null)
            {
                population.environmentalFitness = Mathf.RoundToInt(100f *
                    Fitness(block, population.fitTemperature,
                        population.fitHumidity));
                population.environmentalCarryingCapacity =
                    EnvironmentalCarryingCapacity(block, population, rules);
            }
    }

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

    // Plant layers are access channels, not additional inventories.
    // Their weighted efficiencies compete for one physical S budget.
    // Adults settle first; reproduction uses the actual remainder.
    private static float[] AllocatePlants(List<PopulationData> groups, float food,
        BlockInfo block, PopulationData changed = null,
        float temperature = 0f, float humidity = 0f, float movement = 0f,
        float size = 0f, float fertility = 0f,
        LandRuleTuning rules = null, float birthFactor = 1f,
        PopulationData countOverride = null, int overrideCount = 0,
        bool adultsOnly = false)
    {
        int count = groups.Count;
        var adultNeed = new float[count];
        var birthNeed = new float[count];
        var access = new float[count];
        for (int i = 0; i < count; i++)
        {
            PopulationData p = groups[i];
            bool changedTraits = p == changed;
            float t = changedTraits ? temperature : p.fitTemperature;
            float h = changedTraits ? humidity : p.fitHumidity;
            float mov = changedTraits ? movement : p.movementAbility;
            float sz = changedTraits ? size : p.size;
            float fert = changedTraits ? fertility : p.fertility;
            int individuals = Mathf.Max(0,
                p == countOverride ? overrideCount : p.speciesAmount);
            float fitness = Fitness(block, t, h);
            adultNeed[i] = individuals * AdultNeed(sz, mov, rules);
            birthNeed[i] = individuals * rules.birthRate *
                Mathf.Clamp01(fert / 100f) * fitness *
                Mathf.Clamp01(birthFactor) *
                (YoungNeed(sz, rules) + BodyMass(sz, rules));
            float common = fitness * ForagingAccess(sz, mov, rules) *
                (1f + rules.movementForagingGain * mov);
            int preferred = PlantNiche(p) <= 0 ? 0 : 1;
            for (int channel = 0; channel < 2; channel++)
                access[i] += common *
                    (channel == 0 ? rules.smallPlantNicheShare :
                        1f - rules.smallPlantNicheShare) *
                    NicheEfficiency(sz, channel, rules) *
                    (channel == preferred ? 1f :
                        rules.unspecializedPlantEfficiency);
        }
        float remaining = Mathf.Max(0f, food);
        var allocation = new float[count];
        AllocatePlantStage(adultNeed, access, ref remaining, allocation);
        if (!adultsOnly)
            AllocatePlantStage(birthNeed, access, ref remaining, allocation);
        return allocation;
    }

    private static void AllocatePlantStage(float[] need, float[] access,
        ref float remaining, float[] totalAllocation)
    {
        int count = need.Length;
        var received = new float[count];
        var proposal = new float[count];
        for (int pass = 0; pass <= count && remaining > Epsilon; pass++)
        {
            float weight = 0f;
            for (int i = 0; i < count; i++)
                weight += access[i] * Mathf.Max(0f, need[i] - received[i]);
            if (weight <= Epsilon) break;
            float spent = 0f;
            for (int i = 0; i < count; i++)
            {
                float unmet = Mathf.Max(0f, need[i] - received[i]);
                proposal[i] = Mathf.Min(unmet,
                    remaining * access[i] * unmet / weight);
                spent += proposal[i];
            }
            for (int i = 0; i < count; i++)
            {
                received[i] += proposal[i];
                totalAllocation[i] += proposal[i];
            }
            remaining = Mathf.Max(0f, remaining - spent);
            if (spent <= Epsilon) break;
        }
    }

    private static int PlantNiche(PopulationData p)
    {
        if (p.splitStrategy != 0) return p.splitStrategy;
        // Preset large-bodied species start in the upper plant layer even
        // when created directly in the simulator without a local split.
        return p.species != null && p.species.baseSize >= 25 ? 1 : 0;
    }

    private static float NicheEfficiency(float size, int niche,
        LandRuleTuning rules)
    {
        float x = Mathf.Max(1f, size) / (niche <= 0
            ? rules.smallPlantSizeScale : rules.largePlantSizeScale);
        float x2 = x * x;
        float x4 = x2 * x2;
        return niche <= 0 ? 1f / (1f + x4) : x4 / (1f + x4);
    }

    // Growth can be eaten today, but the standing stock is drawn down gradually.
    // The floor is measured against capacity so a sudden population boom cannot
    // empty a tile in one update. Both simulation and mutation forecasts use this.
    internal static float PlantFoodBudget(BlockInfo block, float stockAfterGrowth,
        LandRuleTuning rules = null)
    {
        float floor = block.habitatRecovery > 0f
            ? Mathf.Min(Mathf.Max(0f, block.maxPlantBiomass) *
                (rules != null ? rules.minimumPlantReserveFraction : 0.1f),
                stockAfterGrowth * 0.5f)
            : 0f;
        float growth = Mathf.Clamp(block.plantGrowthToday, 0f, stockAfterGrowth);
        float standing = Mathf.Max(0f, stockAfterGrowth - growth);
        float draw = block.habitatRecovery <= 0f ? standing :
            Mathf.Min(Mathf.Max(0f, standing - floor), standing *
                (rules != null ? rules.dailyPlantStockDrawFraction : 0.1f));
        return Mathf.Max(0f, Mathf.Min(stockAfterGrowth - floor, growth + draw));
    }

    // Ten days of food that this group can actually draw from standing stock.
    // The plant path uses the same niche allocation and daily draw cap as feeding;
    // the hunter path uses food caught today plus its carried reserve.
    internal static float AccessibleFoodStockDays(BlockInfo block,
        PopulationData target, LandRuleTuning rules)
    {
        if (block == null || target == null || target.speciesAmount <= 0)
            return 0f;
        float adultDemand = target.speciesAmount * AdultNeed(target.size,
            target.movementAbility, rules);
        if (adultDemand <= 0f) return 0f;
        if (target.trophicLevel > 0)
            return Mathf.Max(0f, target.energyReserve +
                10f * target.huntingEnergyToday) / adultDemand;
        float stock = Mathf.Max(0f, block.plantBiomass);
        float floor = block.habitatRecovery > 0f
            ? Mathf.Min(Mathf.Max(0f, block.maxPlantBiomass) *
                rules.minimumPlantReserveFraction, stock * 0.5f) : 0f;
        float draw = block.habitatRecovery <= 0f ? stock :
            Mathf.Min(Mathf.Max(0f, stock - floor),
                stock * rules.dailyPlantStockDrawFraction);
        if (draw <= 0f) return 0f;
        var herbivores = new List<PopulationData>();
        foreach (PopulationData p in block.community)
            if (p != null && p.speciesAmount > 0 && p.trophicLevel == 0)
                herbivores.Add(p);
        int index = herbivores.IndexOf(target);
        if (index < 0) return 0f;
        float[] shares = AllocatePlants(herbivores, draw,
            block, rules: rules);
        return 10f * shares[index] / adultDemand;
    }

    internal static float ForecastPlantIntake(BlockInfo block, PopulationData target,
        float temperature, float humidity, float movement, float size,
        float fertility, bool renewalOnly, LandRuleTuning rules = null,
        float birthFactor = 1f)
    {
        var groups = new List<PopulationData>(5);
        foreach (PopulationData p in block.community)
            if (p != null && p.trophicLevel == 0 && p.speciesAmount > 0)
                groups.Add(p);
        int index = groups.IndexOf(target);
        if (index < 0) return 0f;
        float food = renewalOnly ? Mathf.Max(0, block.habitatRecovery)
            : PlantFoodBudget(block, block.plantBiomass +
                block.consumedBiomassToday, rules);
        return AllocatePlants(groups, food, block,
            target, temperature, humidity, movement, size, fertility, rules,
            birthFactor)[index];
    }

    internal static float ForecastWholePlantSatisfaction(BlockInfo block,
        PopulationData target, float temperature, float humidity,
        float movement, float size, float fertility, LandRuleTuning rules,
        out float birthFactor)
    {
        birthFactor = 1f;
        float demand = target.speciesAmount * AdultNeed(size, movement, rules);
        float food = ForecastPlantIntake(block, target, temperature, humidity,
            movement, size, fertility, false, rules, birthFactor);
        return demand > 0f ? Mathf.Clamp01(food / demand) : 1f;
    }

    internal static void ForecastEvolutionPlantSupport(BlockInfo block,
        PopulationData source, PopulationData candidate, int remainingSource,
        LandRuleTuning rules, out float demand, out float today,
        out float renewable)
    {
        var groups = new List<PopulationData>(5);
        foreach (PopulationData p in block.community)
        {
            if (p == null || p.trophicLevel != 0 || p.speciesAmount <= 0)
                continue;
            int individuals = p == source ? remainingSource : p.speciesAmount;
            if (individuals <= 0) continue;
            groups.Add(p);
        }
        groups.Add(candidate);
        float birthFactor = 1f;
        demand = Demand(candidate, candidate.speciesAmount,
            Fitness(block, candidate.fitTemperature, candidate.fitHumidity),
            rules, birthFactor);
        int index = groups.Count - 1;
        float currentBudget = PlantFoodBudget(block,
            block.plantBiomass + block.consumedBiomassToday, rules);
        today = AllocatePlants(groups, currentBudget,
            block, rules: rules, birthFactor: birthFactor,
            countOverride: source, overrideCount: remainingSource)[index];
        renewable = AllocatePlants(groups, Mathf.Max(0f,
            block.habitatRecovery), block,
            rules: rules, birthFactor: birthFactor,
            countOverride: source, overrideCount: remainingSource)[index];
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
        (prey.bodyBiomassInitialized && prey.speciesAmount > 0
            ? prey.bodyBiomass / prey.speciesAmount : BodyMass(prey.size, rules))
        * Mathf.Min(1f, hunter.size / (prey.size + 15f));

    internal static float HuntPreference(PopulationData hunter,
        PopulationData prey, LandRuleTuning rules)
    {
        if (hunter == null || prey == null || !FoodWeb.CanEat(hunter, prey))
            return 0f;
        return Mathf.Min(1f, 10f / Mathf.Max(1f, prey.size))
            * CaptureChance(hunter, prey, rules)
            * PreyEnergy(hunter, prey, rules);
    }

    // Hunting resolves whole animals. A group that can catch at least one prey
    // may take the final animal needed to cover its remaining energy target;
    // the unused energy stays in the existing reserve ledger. Fractional
    // capture ability is still accumulated by the victim's predation remainder.
    internal static float WholePreyClaim(float captureIntent,
        float targetEnergy, float preyEnergy)
    {
        if (captureIntent <= Epsilon || targetEnergy <= Epsilon ||
            preyEnergy <= Epsilon) return 0f;
        int preyNeeded = Mathf.Max(1, Mathf.CeilToInt(targetEnergy / preyEnergy));
        return Mathf.Min(captureIntent, preyNeeded);
    }

    // Allocate each competing hunter's demand across its available prey once.
    // The remaining renewable prey are then valued for this hunter.
    internal static float AvailableSustainablePreyFood(BlockInfo block,
        PopulationData hunter, LandRuleTuning rules,
        bool subtractSameLevel = true, PopulationData replaced = null,
        int replacedCount = 0)
    {
        if (block == null || block.community == null || hunter == null)
            return 0f;
        float total = 0f;
        foreach (PopulationData prey in block.community)
        {
            int preyCount = prey == replaced ? replacedCount :
                prey != null ? prey.speciesAmount : 0;
            if (preyCount <= 0 || !FoodWeb.CanEat(hunter, prey)) continue;
            float renewal = Mathf.Max(0f, Average(prey.preyNetRenewalHistory));
            if (prey == replaced && prey.speciesAmount > 0)
                renewal *= (float)preyCount / prey.speciesAmount;
            foreach (PopulationData other in block.community)
            {
                int otherCount = other == replaced ? replacedCount :
                    other != null ? other.speciesAmount : 0;
                if (other == hunter || otherCount <= 0 ||
                    other.trophicLevel <= 0 ||
                    !subtractSameLevel &&
                    other.trophicLevel == hunter.trophicLevel) continue;
                float preference = HuntPreference(other, prey, rules);
                if (preference <= 0f) continue;
                float totalPreference = 0f;
                foreach (PopulationData option in block.community)
                {
                    int optionCount = option == replaced ? replacedCount :
                        option != null ? option.speciesAmount : 0;
                    if (optionCount > 0)
                        totalPreference += HuntPreference(other, option,
                            rules);
                }
                if (totalPreference <= 0f) continue;
                float otherDemand = Demand(other, otherCount,
                    Fitness(block, other.fitTemperature, other.fitHumidity),
                    rules);
                renewal -= otherDemand * preference / totalPreference /
                    Mathf.Max(Epsilon, PreyEnergy(other, prey, rules));
            }
            total += Mathf.Max(0f, renewal)
                * PreyEnergy(hunter, prey, rules);
        }
        return total;
    }

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
            targetEnergy[i] = Mathf.Max(0f, adult + births *
                (YoungNeed(hunter.size, rules) + BodyMass(hunter.size, rules))
                + adult * rules.reserveHuntFraction - hunter.energyReserve);
            float totalScore = 0f;
            for (int j = 0; j < q; j++)
            {
                if (!FoodWeb.CanEat(hunter, prey[j])) continue;
                score[i, j] = HuntPreference(hunter, prey[j], rules);
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
                        claims[i] = WholePreyClaim(intent[i, j],
                            targetEnergy[i], PreyEnergy(hunters[i], victim, rules));
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
                float removedBody = victim.bodyBiomass * kills /
                    victim.speciesAmount;
                victim.speciesAmount -= kills;
                victim.bodyBiomass -= removedBody;
                victim.predationDeathsToday += kills;
                victim.deathsToday += kills;
                float remaining = kills;
                float transferredBody = 0f;
                for (int i = 0; i < h; i++)
                {
                    if (intent[i, j] <= 0f || targetEnergy[i] <= Epsilon) continue;
                    float share = i == lastEligible ? remaining
                        : kills * weights[i] / Mathf.Max(Epsilon, totalWeight);
                    share = Mathf.Min(remaining, share);
                    remaining -= share;
                    float energy = share * removedBody / kills *
                        Mathf.Min(1f, hunters[i].size / (victim.size + 15f));
                    transferredBody += energy;
                    hunters[i].huntingEnergyToday += energy;
                    hunters[i].energyReserve += energy;
                    targetEnergy[i] = Mathf.Max(0f, targetEnergy[i] - energy);
                }
                block.detritusBiomass += Mathf.Max(0f,
                    removedBody - transferredBody);
            }
    }

    internal static void Simulate(BlockInfo block, LandRuleTuning rules)
    {
        FoodWeb.GrowPlants(block);
        block.accountedBiomassSinkToday = 0f;
        var herbivores = new List<PopulationData>(5);
        foreach (PopulationData p in block.community)
        {
            EnsureBodyMass(p, rules);
            p.populationBeforePredation = p.speciesAmount;
            p.predationDeathsToday = p.birthsToday = p.deathsToday = 0;
            p.huntingEnergyToday = 0f;
            p.adultDemandToday = p.adultPaidToday = 0f;
            p.birthOpportunityToday = p.birthBudgetToday = 0f;
            p.newbornCostToday = p.unusedBirthFoodToday = 0f;
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
        float[] allocation = AllocatePlants(herbivores,
            PlantFoodBudget(block, block.plantBiomass, rules),
            block, rules: rules);
        float[] renewal = AllocatePlants(herbivores,
            Mathf.Max(0, block.habitatRecovery), block,
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
            }
            if (count <= 0)
            {
                p.actualEnergySatisfactionToday = p.energySatisfactionToday = 0f;
                AddHistory(p.foodSatisfactionHistory, 0f);
                AddHistory(p.preyNetRenewalHistory, 0f);
                AddHistory(p.populationGrowthHistory,
                    p.populationBeforePredation > 0 ? -1f : 0f);
                p.endangeredDays++;
                continue;
            }
            // The existing reserve stores food already removed from a real
            // plant/prey ledger. Settle adults before fully paid newborns;
            // retain the unspent balance across days, with the existing cap.
            float savedReserve = p.energyReserve;
            float availableResources = p.allocatedBiomass + savedReserve;
            float fitness = Fitness(block, p.fitTemperature, p.fitHumidity);
            float adultDemand = count * p.energyNeed;
            float satisfaction = adultDemand > 0f
                ? Mathf.Clamp01(availableResources / adultDemand) : 1f;
            p.actualEnergySatisfactionToday = p.energySatisfactionToday = satisfaction;
            p.carryingCapacity = p.energyNeed > 0f
                ? p.allocatedBiomass / p.energyNeed : 0f;
            float births = count * rules.birthRate * p.fertility / 100f
                * fitness * satisfaction;
            float deaths = rules.starvationDeathRate * count * (1f - satisfaction)
                + ColdDeaths(count, block.temperature, rules)
                + rules.climateMismatchDeathRate * count * (1f - fitness)
                + NaturalAdultDeaths(count, p.size, rules)
                + ReproductionStressDeaths(count, p.size, p.fertility, rules,
                    satisfaction: satisfaction, fitness: fitness);
            p.birthRemainder += births;
            // A day's lethal pressure cannot become a debt that kills later
            // migrants or newborns after every current adult has died.
            p.deathRemainder = Mathf.Min(count,
                p.deathRemainder + Mathf.Max(0f, deaths));
            int possibleBirths = Mathf.FloorToInt(p.birthRemainder);
            float bodyPerNewborn = BodyMass(p.size, rules);
            float newbornCost = YoungNeed(p.size, rules) + bodyPerNewborn;
            float availableForBirth = Mathf.Max(0f,
                availableResources - adultDemand);
            int born = Mathf.Min(possibleBirths,
                Mathf.FloorToInt(availableForBirth / newbornCost));
            p.adultDemandToday = count * p.energyNeed;
            p.adultPaidToday = Mathf.Min(availableResources, p.adultDemandToday);
            p.birthOpportunityToday = births;
            p.birthBudgetToday = availableForBirth;
            p.newbornCostToday = newbornCost;
            p.unusedBirthFoodToday = Mathf.Max(0f,
                availableForBirth - born * newbornCost);
            p.energyReserve = Mathf.Min(
                Mathf.Max(0f, availableResources - p.adultPaidToday -
                    born * newbornCost), adultDemand * rules.reserveDays);
            block.accountedBiomassSinkToday += Mathf.Max(0f,
                p.allocatedBiomass - born * bodyPerNewborn -
                (p.energyReserve - savedReserve));
            int died = Mathf.Min(count, Mathf.FloorToInt(p.deathRemainder));
            // Unfunded births are not a debt that can appear for free later.
            p.birthRemainder = Mathf.Min(0.999f,
                Mathf.Max(0f, p.birthRemainder - born));
            p.deathRemainder -= died;
            float deadBody = count > 0 ? p.bodyBiomass * died / count : 0f;
            p.bodyBiomass = Mathf.Max(0f,
                p.bodyBiomass - deadBody + born * bodyPerNewborn);
            block.detritusBiomass += deadBody;
            p.birthsToday = born;
            p.deathsToday += died;
            p.speciesAmount = count + born - died;
            AddHistory(p.foodSatisfactionHistory, satisfaction);
            AddHistory(p.preyNetRenewalHistory, born - died);
            AddHistory(p.populationGrowthHistory,
                (p.speciesAmount - p.populationBeforePredation) /
                (float)Mathf.Max(1, p.populationBeforePredation));
            p.endangeredDays = p.speciesAmount < 10 ? p.endangeredDays + 1 : 0;
        }
        // Prey net renewal must be complete before any hunter reads it;
        // community list order must not shift the hunter's history by a day.
        foreach (PopulationData p in block.community)
        {
            if (p.trophicLevel <= 0) continue;
            int settledCount = Mathf.Max(0, p.populationBeforePredation
                - p.predationDeathsToday);
            float demand = Demand(p, settledCount,
                Fitness(block, p.fitTemperature, p.fitHumidity), rules);
            float sustainableFood = AvailableSustainablePreyFood(block,
                p, rules);
            AddHistory(p.renewalSatisfactionHistory, demand > 0f
                ? Mathf.Clamp01(sustainableFood / demand) : 1f);
        }
    }

}
