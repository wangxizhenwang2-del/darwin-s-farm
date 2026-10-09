using System.Collections.Generic;
using UnityEngine;

// 食物供给与捕食计算独立于模拟控制器的状态。
internal static class FoodWeb
{
    private const float PreyEnergyPerSize = 15f;
    private const float PredationEfficiency = 0.8f;
    public const int MaxTrophicLevel = 2;
    public const float PredatorReserveDays = 10f;
    private const float SharedPlantFraction = 0.1f;

    // 2026-10-09 13:49 +08:00: explicit species links take priority; the
    // V4.3 level fallback allows L2 to hunt either lower level.
    internal static bool CanEat(PopulationData predator, PopulationData prey)
    {
        if (predator == null || prey == null || predator == prey) return false;
        bool allowedLevels = predator.trophicLevel == 1 && prey.trophicLevel == 0 ||
            predator.trophicLevel == 2 &&
            (prey.trophicLevel == 0 || prey.trophicLevel == 1);
        if (!allowedLevels) return false;
        List<SpeciesData> links = predator.species != null ? predator.species.preySpecies : null;
        return links != null && links.Count > 0
            ? prey.species != null && links.Contains(prey.species)
            : true;
    }

    public static void GrowPlants(BlockInfo block)
    {
        float maximum = Mathf.Max(0f, block.maxPlantBiomass);
        float before = Mathf.Clamp(block.plantBiomass, 0f, maximum);
        float growth = SimulationController.CalculatePlantGrowth(before,
            maximum, block.habitatRecovery);
        block.plantBiomass = Mathf.Min(maximum, before + growth);
        block.plantGrowthToday = block.plantBiomass - before;
    }

    public static float FeedCommunity(BlockInfo block, float reproductionScale,
        float overCapacityDeathRate, float starvationDeathRate,
        float levelOnePreyFraction, float levelTwoPreyFraction)
    {
        float eaten = FeedHerbivores(block);
        FeedSharedPredators(block, reproductionScale, overCapacityDeathRate,
            starvationDeathRate, levelOnePreyFraction, levelTwoPreyFraction);
        return eaten;
    }

    public static void SpendPlants(BlockInfo block, float eaten)
    {
        block.consumedBiomassToday = eaten;
        block.plantBiomass = Mathf.Max(0f, block.plantBiomass - eaten);
    }

    public static float FoodPerDemand(BlockInfo block, int level,
        PopulationData consumer, float reproductionScale,
        float levelOnePreyFraction, float levelTwoPreyFraction)
    {
        if (block == null || block.community == null || consumer == null ||
            level < 0 || level > MaxTrophicLevel) return 0f;
        if (level > 0)
            return ProjectedPredatorIntakeRatio(block, consumer,
                levelOnePreyFraction, levelTwoPreyFraction);
        float food = 0f;
        float maximum = Mathf.Max(0f, block.maxPlantBiomass);
        float stock = Mathf.Clamp(block.plantBiomass, 0f, maximum);
        float projectedStock = stock + SimulationController.CalculatePlantGrowth(
            stock, maximum, block.habitatRecovery);
        food = Mathf.Min(PlantSupportFood(block, projectedStock),
            projectedStock * ForagingEfficiency(consumer));

        // 把同级竞争者算进去，才知道这个种群实际能分到多少。
        float demand = consumer.energyNeed * Mathf.Max(1, consumer.speciesAmount);
        foreach (PopulationData competitor in block.community)
        {
            if (competitor != consumer && competitor.speciesAmount > 0 &&
                competitor.trophicLevel == level)
                demand += competitor.energyNeed * competitor.speciesAmount;
        }
        return demand > 0f ? food / demand : 0f;
    }

    // 2026-10-09 13:58 +08:00: prospective intake follows the same graph and
    // one-quota-per-prey split as the actual land food ledger.
    private static float ProjectedPredatorIntakeRatio(BlockInfo block,
        PopulationData consumer, float levelOnePreyFraction,
        float levelTwoPreyFraction)
    {
        float ownDemand = consumer.energyNeed * consumer.speciesAmount;
        if (ownDemand <= 0f) return 0f;
        bool present = block.community.Contains(consumer);
        float food = 0f;
        foreach (PopulationData prey in block.community)
        {
            if (prey == null || prey.speciesAmount <= 0 || prey.size <= 0f ||
                !CanEat(consumer, prey)) continue;
            float eligibleDemand = 0f;
            float totalEffort = 0f;
            float weightedCatch = 0f;
            float ownEffort = 0f;
            foreach (PopulationData hunter in block.community)
            {
                if (hunter == null || hunter.speciesAmount <= 0 ||
                    !CanEat(hunter, prey)) continue;
                float demand = hunter.energyNeed * hunter.speciesAmount;
                float effort = demand * ForagingEfficiency(hunter)
                    * Mathf.Clamp01(hunter.environmentalFitness / 100f)
                    * CaptureChance(hunter.movementAbility, hunter.size, prey);
                eligibleDemand += demand;
                totalEffort += effort;
                weightedCatch += effort * DailyPreyFraction(hunter.trophicLevel,
                    levelOnePreyFraction, levelTwoPreyFraction);
                if (hunter == consumer) ownEffort = effort;
            }
            if (!present)
            {
                ownEffort = ownDemand * ForagingEfficiency(consumer)
                    * Mathf.Clamp01(consumer.environmentalFitness / 100f)
                    * CaptureChance(consumer.movementAbility, consumer.size, prey);
                eligibleDemand += ownDemand;
                totalEffort += ownEffort;
                weightedCatch += ownEffort * DailyPreyFraction(consumer.trophicLevel,
                    levelOnePreyFraction, levelTwoPreyFraction);
            }
            if (eligibleDemand <= 0f || totalEffort <= 0f) continue;
            float expected = Mathf.Min(prey.speciesAmount,
                prey.speciesAmount * weightedCatch / eligibleDemand);
            food += expected * prey.size * PreyEnergyPerSize * PredationEfficiency
                * ownEffort / totalEffort;
        }
        return food / ownDemand;
    }

    internal static float CaptureEfficiency(float predatorMovement, float predatorSize,
        PopulationData prey)
        => CaptureEfficiency(predatorMovement, predatorSize,
            prey.movementAbility, prey.size, prey.trophicLevel);

    private static float CaptureEfficiency(float predatorMovement, float predatorSize,
        float preyMovement, float preySize, int preyLevel)
    {
        // 运动与体型影响捕获成功机会，不改变一只猎物包含的能量。
        float movementAndSize = Mathf.Clamp(1f +
            (predatorMovement - preyMovement) / 40f
            + (predatorSize - preySize) / 80f, 0.5f, 2f);
        // 一级捕食者过小便难以处理食草猎物；体型达到 10 后不再获额外收益。
        float handling = preyLevel == 0
            ? Mathf.Min(1f, predatorSize / 10f) : 1f;
        return movementAndSize * handling * handling;
    }

    private static float CaptureChance(float movement, float size, PopulationData prey) =>
        CaptureEfficiency(movement, size, prey) / 2f;

    // 固定当天猎手的捕食努力，只比较候选猎物性状造成的风险变化；不掷随机数。
    // 2026-10-09 13:53 +08:00: forecast uses the same shared prey quota as
    // actual feeding, including L2 hunters of L0 prey and a hypothetical new
    // target population that has not yet been inserted into the community.
    internal static float ExpectedPreyLoss(BlockInfo block, PopulationData target,
        float targetMovement, float targetSize,
        float levelOnePreyFraction, float levelTwoPreyFraction)
    {
        if (block == null || block.community == null || target == null ||
            target.speciesAmount <= 0) return 0f;
        float expectedTargetKills = 0f;
        float availableEnergy = 0f;
        float totalDeficit = 0f;
        bool targetPresent = false;
        foreach (PopulationData hunter in block.community)
            if (hunter != null && hunter.speciesAmount > 0 && hunter.trophicLevel > 0)
            {
                bool hasFood = CanEat(hunter, target);
                if (!hasFood)
                    foreach (PopulationData prey in block.community)
                        if (prey != null && prey.speciesAmount > 0 &&
                            CanEat(hunter, prey)) { hasFood = true; break; }
                if (hasFood)
                    totalDeficit += Mathf.Max(0f,
                        hunter.energyNeed * hunter.speciesAmount - hunter.energyReserve);
            }
        foreach (PopulationData prey in block.community)
        {
            if (prey == null || prey.speciesAmount <= 0 || prey.size <= 0f) continue;
            bool isTarget = prey == target;
            targetPresent |= isTarget;
            float kills = ExpectedSharedKills(block, prey,
                isTarget ? targetMovement : prey.movementAbility,
                isTarget ? targetSize : prey.size,
                levelOnePreyFraction, levelTwoPreyFraction);
            availableEnergy += kills * (isTarget ? targetSize : prey.size)
                * PreyEnergyPerSize * PredationEfficiency;
            if (isTarget) expectedTargetKills = kills;
        }
        if (!targetPresent)
        {
            expectedTargetKills = ExpectedSharedKills(block, target,
                targetMovement, targetSize,
                levelOnePreyFraction, levelTwoPreyFraction);
            availableEnergy += expectedTargetKills * targetSize
                * PreyEnergyPerSize * PredationEfficiency;
        }
        float demandFraction = availableEnergy > 0f
            ? Mathf.Min(1f, totalDeficit / availableEnergy) : 0f;
        return Mathf.Min(target.speciesAmount, expectedTargetKills * demandFraction);
    }

    private static float ExpectedSharedKills(BlockInfo block, PopulationData prey,
        float movement, float size,
        float levelOnePreyFraction, float levelTwoPreyFraction)
    {
        float eligibleDemand = 0f;
        float weightedCatch = 0f;
        foreach (PopulationData hunter in block.community)
        {
            if (hunter == null || hunter.speciesAmount <= 0 ||
                !CanEat(hunter, prey)) continue;
            float demand = hunter.energyNeed * hunter.speciesAmount;
            eligibleDemand += demand;
            weightedCatch += demand * ForagingEfficiency(hunter)
                * Mathf.Clamp01(hunter.environmentalFitness / 100f)
                * CaptureEfficiency(hunter.movementAbility, hunter.size,
                    movement, size, prey.trophicLevel) / 2f
                * DailyPreyFraction(hunter.trophicLevel,
                    levelOnePreyFraction, levelTwoPreyFraction);
        }
        return eligibleDemand > 0f
            ? Mathf.Min(prey.speciesAmount,
                prey.speciesAmount * weightedCatch / eligibleDemand) : 0f;
    }

    private static float ForagingEfficiency(PopulationData population) =>
        ForagingEfficiency(population.movementAbility);

    private static float ForagingEfficiency(float movement) =>
        0.75f + 0.0025f * Mathf.Clamp(movement, 0f, 100f);

    private static float DailyPreyFraction(int predatorLevel,
        float levelOnePreyFraction, float levelTwoPreyFraction)
    {
        return predatorLevel == 1 ? levelOnePreyFraction : levelTwoPreyFraction;
    }

    private static float PlantSupportFood(BlockInfo block, float availableStock)
    {
        float maximum = Mathf.Max(0f, block.maxPlantBiomass);
        if (maximum <= 0f) return 0f;
        float stock = Mathf.Clamp(availableStock, 0f, maximum);
        float recovery = Mathf.Min(Mathf.Max(0, block.habitatRecovery), maximum);
        return Mathf.Min(stock, recovery);
    }

    private static float SupportedPreyHarvest(PopulationData prey,
        float reproductionScale, float overCapacityDeathRate, float starvationDeathRate,
        bool projectedFeeding = false)
    {
        if (prey.speciesAmount <= 0 || prey.carryingCapacity <= 0f) return 0f;
        float allocation = projectedFeeding
            ? Mathf.Min(prey.energyNeed * prey.speciesAmount,
                prey.carryingCapacity * prey.energyNeed)
            : prey.allocatedBiomass;
        SimulationController.EstimateDemography(prey.speciesAmount,
            prey.fertility, prey.environmentalFitness, prey.energyNeed,
            allocation, prey.carryingCapacity, reproductionScale,
            overCapacityDeathRate, starvationDeathRate *
                (prey.trophicLevel > 0 ?
                    SimulationController.PredatorStarvationMultiplier : 1f),
            out float births, out float deaths);
        // 捕食可替代密度死亡，不能替代饥饿和超载死亡。
        float densityDeaths = births * prey.speciesAmount / prey.carryingCapacity;
        return Mathf.Max(0f, births - deaths
            + densityDeaths * SimulationController.DensityPredationCompensation);
    }

    private static float FeedHerbivores(BlockInfo block)
    {
        List<PopulationData> herbivores = new List<PopulationData>();
        foreach (PopulationData population in block.community)
            if (population.speciesAmount > 0 && population.trophicLevel == 0)
                herbivores.Add(population);
        if (herbivores.Count == 0) return 0f;
        float bestForaging = 0f;
        float bestFitness = 0f;
        foreach (PopulationData population in herbivores)
        {
            float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
            bestForaging = Mathf.Max(bestForaging,
                ForagingEfficiency(population) * fitness);
            bestFitness = Mathf.Max(bestFitness, fitness);
        }
        float availableFood = block.plantBiomass * bestForaging;
        float capacityFood = Mathf.Min(PlantSupportFood(block, block.plantBiomass)
            * bestFitness, availableFood);
        float smallWeight = 0f;
        float largeWeight = 0f;
        float allWeight = 0f;
        foreach (PopulationData population in herbivores)
        {
            float weight = PlantWeight(population);
            smallWeight += weight * SmallTrack(population.size);
            largeWeight += weight * (1f - SmallTrack(population.size));
            allWeight += weight;
        }
        if (allWeight <= 0f) return 0f;
        float ownPool = capacityFood * (1f - SharedPlantFraction) / 2f;
        float sharedPool = capacityFood - 2f * ownPool;
        if (smallWeight <= 0f) { sharedPool += ownPool; ownPool = 0f; }
        float largePool = capacityFood * (1f - SharedPlantFraction) / 2f;
        if (largeWeight <= 0f) { sharedPool += largePool; largePool = 0f; }
        foreach (PopulationData population in herbivores)
        {
            float weight = PlantWeight(population);
            float small = SmallTrack(population.size);
            float share = sharedPool * weight / allWeight;
            if (smallWeight > 0f) share += ownPool * weight * small / smallWeight;
            if (largeWeight > 0f) share += largePool * weight * (1f - small) / largeWeight;
            population.carryingCapacity = share / population.energyNeed;
        }
        // 两条赛道只分配当天同一份草；无人使用的额度回到公共池。
        float foodPool = availableFood * (1f - SharedPlantFraction) / 2f;
        float unused = DistributePlantPool(herbivores, foodPool, 1);
        unused += DistributePlantPool(herbivores, foodPool, 2);
        DistributePlantPool(herbivores,
            availableFood * SharedPlantFraction + unused, 0);
        float eaten = 0f;
        foreach (PopulationData population in herbivores)
            eaten += population.allocatedBiomass;
        return eaten;
    }

    private static float SmallTrack(float size) => size < 8f ? 1f : 0f;

    private static float PlantWeight(PopulationData population) =>
        population.energyNeed * population.speciesAmount
        * Mathf.Clamp01(population.environmentalFitness / 100f)
        * ForagingEfficiency(population);

    internal static float ProjectedHerbivoreCapacity(BlockInfo block,
        PopulationData candidate, float targetSize) =>
        ProjectedHerbivoreCapacity(block, candidate, targetSize,
            candidate.movementAbility, candidate.environmentalFitness / 100f,
            candidate.fertility + candidate.fertilityMutationRemainder);

    internal static float ProjectedHerbivoreCapacity(BlockInfo block,
        PopulationData candidate, float targetSize, float targetMovement,
        float targetFitness, float targetFertility)
    {
        if (candidate.speciesAmount <= 0 || targetSize <= 0f) return 0f;
        float smallWeight = 0f;
        float largeWeight = 0f;
        float allWeight = 0f;
        float candidateWeight = 0f;
        float candidateSmall = SmallTrack(targetSize);
        float bestFitness = 0f;
        float bestForaging = 0f;
        bool candidatePresent = block.community.Contains(candidate);
        foreach (PopulationData population in block.community)
        {
            if (population.speciesAmount <= 0 || population.trophicLevel != 0) continue;
            float fitness = Mathf.Clamp01(population == candidate
                ? targetFitness : population.environmentalFitness / 100f);
            float foraging = ForagingEfficiency(population == candidate
                ? targetMovement : population.movementAbility);
            bestFitness = Mathf.Max(bestFitness, fitness);
            bestForaging = Mathf.Max(bestForaging, fitness * foraging);
            float small = population == candidate ? candidateSmall : SmallTrack(population.size);
            float energy = population == candidate
                ? SimulationController.CalculateEnergyNeed(targetSize,
                    targetMovement, targetFertility)
                : population.energyNeed;
            float weight = energy * population.speciesAmount * fitness
                * foraging;
            smallWeight += weight * small;
            largeWeight += weight * (1f - small);
            allWeight += weight;
            if (population == candidate) candidateWeight = weight;
        }
        if (!candidatePresent)
        {
            // A target species is scored before it is inserted into the tile.
            float fitness = Mathf.Clamp01(targetFitness);
            float foraging = ForagingEfficiency(targetMovement);
            float energy = SimulationController.CalculateEnergyNeed(targetSize,
                targetMovement, targetFertility);
            candidateWeight = energy * candidate.speciesAmount * fitness * foraging;
            smallWeight += candidateWeight * candidateSmall;
            largeWeight += candidateWeight * (1f - candidateSmall);
            allWeight += candidateWeight;
            bestFitness = Mathf.Max(bestFitness, fitness);
            bestForaging = Mathf.Max(bestForaging, fitness * foraging);
        }
        if (candidateWeight <= 0f || allWeight <= 0f) return 0f;
        float food = Mathf.Min(PlantSupportFood(block, block.plantBiomass)
            * bestFitness, block.plantBiomass * bestForaging);
        float ownPool = food * (1f - SharedPlantFraction) / 2f;
        float sharedPool = food * SharedPlantFraction;
        if (smallWeight <= 0f) sharedPool += ownPool;
        if (largeWeight <= 0f) sharedPool += ownPool;
        float share = sharedPool * candidateWeight / allWeight;
        if (smallWeight > 0f)
            share += ownPool * candidateWeight * candidateSmall / smallWeight;
        if (largeWeight > 0f)
            share += ownPool * candidateWeight * (1f - candidateSmall) / largeWeight;
        float candidateEnergy = SimulationController.CalculateEnergyNeed(targetSize,
            targetMovement, targetFertility);
        return share / candidateEnergy;
    }

    // 2026-10-09 13:54 +08:00: projected K shares each prey quota with every
    // hunter that can eat it, matching the actual food-web settlement.
    internal static float ProjectedPredatorCapacity(BlockInfo block,
        PopulationData candidate, float targetSize, float targetMovement,
        float targetFitness, float targetFertility, float reproductionScale,
        float overCapacityDeathRate, float starvationDeathRate,
        float levelOnePreyFraction, float levelTwoPreyFraction)
    {
        if (candidate == null || candidate.speciesAmount <= 0) return 0f;
        float candidateNeed = SimulationController.CalculateEnergyNeed(targetSize,
            targetMovement, targetFertility);
        float candidateDemand = candidateNeed * candidate.speciesAmount;
        float capacity = 0f;
        bool candidatePresent = block.community.Contains(candidate);
        foreach (PopulationData prey in block.community)
        {
            if (prey == null || prey.speciesAmount <= 0 || prey.size <= 0f ||
                !CanEat(candidate, prey)) continue;
            float eligibleDemand = 0f;
            float totalEffort = 0f;
            float weightedCatch = 0f;
            float candidateEffort = 0f;
            foreach (PopulationData hunter in block.community)
            {
                if (hunter == null || hunter.speciesAmount <= 0 ||
                    !CanEat(hunter, prey)) continue;
                bool isCandidate = hunter == candidate;
                float hunterDemand = isCandidate ? candidateDemand :
                    hunter.energyNeed * hunter.speciesAmount;
                float movement = isCandidate ? targetMovement : hunter.movementAbility;
                float size = isCandidate ? targetSize : hunter.size;
                float fitness = isCandidate ? targetFitness :
                    hunter.environmentalFitness / 100f;
                float effort = hunterDemand * ForagingEfficiency(movement)
                    * Mathf.Clamp01(fitness)
                    * CaptureChance(movement, size, prey);
                eligibleDemand += hunterDemand;
                totalEffort += effort;
                weightedCatch += effort * DailyPreyFraction(hunter.trophicLevel,
                    levelOnePreyFraction, levelTwoPreyFraction);
                if (isCandidate) candidateEffort = effort;
            }
            if (!candidatePresent)
            {
                candidateEffort = candidateDemand * ForagingEfficiency(targetMovement)
                    * Mathf.Clamp01(targetFitness)
                    * CaptureChance(targetMovement, targetSize, prey);
                eligibleDemand += candidateDemand;
                totalEffort += candidateEffort;
                weightedCatch += candidateEffort * DailyPreyFraction(candidate.trophicLevel,
                    levelOnePreyFraction, levelTwoPreyFraction);
            }
            if (eligibleDemand <= 0f || totalEffort <= 0f) continue;
            float expected = Mathf.Min(prey.speciesAmount,
                prey.speciesAmount * weightedCatch / eligibleDemand);
            float sustainable = Mathf.Min(expected, SupportedPreyHarvest(prey,
                reproductionScale, overCapacityDeathRate, starvationDeathRate,
                prey.trophicLevel > 0));
            capacity += sustainable * prey.size * PreyEnergyPerSize
                * PredationEfficiency * candidateEffort / totalEffort / candidateNeed;
        }
        return capacity;
    }

    private static float DistributePlantPool(List<PopulationData> herbivores,
        float pool, int track)
    {
        while (pool > 0.01f)
        {
            float totalWeight = 0f;
            foreach (PopulationData population in herbivores)
            {
                float remaining = Mathf.Max(0f, population.energyNeed
                    * population.speciesAmount - population.allocatedBiomass);
                float affinity = track == 1 ? SmallTrack(population.size)
                    : track == 2 ? 1f - SmallTrack(population.size) : 1f;
                totalWeight += remaining * affinity
                    * Mathf.Clamp01(population.environmentalFitness / 100f)
                    * ForagingEfficiency(population);
            }
            if (totalWeight <= 0f) break;
            float distributed = 0f;
            foreach (PopulationData population in herbivores)
            {
                float remaining = Mathf.Max(0f, population.energyNeed
                    * population.speciesAmount - population.allocatedBiomass);
                float affinity = track == 1 ? SmallTrack(population.size)
                    : track == 2 ? 1f - SmallTrack(population.size) : 1f;
                float weight = remaining * affinity
                    * Mathf.Clamp01(population.environmentalFitness / 100f)
                    * ForagingEfficiency(population);
                float received = Mathf.Min(remaining, pool * weight / totalWeight);
                population.allocatedBiomass += received;
                distributed += received;
            }
            pool -= distributed;
            if (distributed <= 0.01f) break;
        }
        return pool;
    }

    // 2026-10-09 13:51 +08:00: one quota per prey group across all linked
    // hunters and levels. Read every hunter/prey before committing any death;
    // distribute each actual prey energy pool only to hunters that can eat it.
    private static void FeedSharedPredators(BlockInfo block, float reproductionScale,
        float overCapacityDeathRate, float starvationDeathRate,
        float levelOnePreyFraction, float levelTwoPreyFraction)
    {
        var hunters = new List<PopulationData>();
        var prey = new List<PopulationData>();
        foreach (PopulationData population in block.community)
        {
            if (population == null || population.speciesAmount <= 0) continue;
            if (population.trophicLevel > 0 && population.energyNeed > 0f)
                hunters.Add(population);
            if (population.size > 0) prey.Add(population);
        }
        int hunterCount = hunters.Count;
        if (hunterCount == 0) return;
        int preyCount = prey.Count;
        var demand = new float[hunterCount];
        var deficit = new float[hunterCount];
        var gained = new float[hunterCount];
        var effort = new float[hunterCount, preyCount];
        var expected = new float[preyCount];
        var energyPerPrey = new float[preyCount];
        var maximumKills = new int[preyCount];
        float totalDeficit = 0f;
        float availableEnergy = 0f;
        for (int i = 0; i < hunterCount; i++)
        {
            demand[i] = hunters[i].energyNeed * hunters[i].speciesAmount;
            deficit[i] = Mathf.Max(0f, demand[i] - hunters[i].energyReserve);
            totalDeficit += deficit[i];
        }
        for (int j = 0; j < preyCount; j++)
        {
            float eligibleDemand = 0f;
            float weightedCatch = 0f;
            for (int i = 0; i < hunterCount; i++)
            {
                if (!CanEat(hunters[i], prey[j])) continue;
                float weight = demand[i] * ForagingEfficiency(hunters[i])
                    * Mathf.Clamp01(hunters[i].environmentalFitness / 100f)
                    * CaptureChance(hunters[i].movementAbility,
                        hunters[i].size, prey[j]);
                effort[i, j] = weight;
                eligibleDemand += demand[i];
                weightedCatch += weight * DailyPreyFraction(hunters[i].trophicLevel,
                    levelOnePreyFraction, levelTwoPreyFraction);
            }
            if (eligibleDemand <= 0f) continue;
            expected[j] = Mathf.Min(prey[j].speciesAmount,
                prey[j].speciesAmount * weightedCatch / eligibleDemand);
            energyPerPrey[j] = prey[j].size * PreyEnergyPerSize * PredationEfficiency;
            availableEnergy += expected[j] * energyPerPrey[j];
            maximumKills[j] = Mathf.FloorToInt(expected[j]);
            if (UnityEngine.Random.value < expected[j] - maximumKills[j])
                maximumKills[j]++;
            maximumKills[j] = Mathf.Min(maximumKills[j], prey[j].speciesAmount);
        }

        // K uses the same shared expected quotas. L1 capacity is computed
        // before L2 asks for the sustainable production of an L1 prey group.
        for (int level = 1; level <= MaxTrophicLevel; level++)
            for (int j = 0; j < preyCount; j++)
            {
                if (expected[j] <= 0f) continue;
                float sustainable = Mathf.Min(expected[j], SupportedPreyHarvest(prey[j],
                    reproductionScale, overCapacityDeathRate, starvationDeathRate,
                    prey[j].trophicLevel > 0));
                float totalEffort = 0f;
                for (int i = 0; i < hunterCount; i++) totalEffort += effort[i, j];
                if (totalEffort <= 0f) continue;
                for (int i = 0; i < hunterCount; i++)
                    if (hunters[i].trophicLevel == level && effort[i, j] > 0f)
                        hunters[i].carryingCapacity += sustainable * energyPerPrey[j]
                            * effort[i, j] / totalEffort / hunters[i].energyNeed;
            }

        float huntFraction = availableEnergy > 0f
            ? Mathf.Min(1f, totalDeficit / availableEnergy) : 0f;
        var kills = new int[preyCount];
        float plannedEnergy = 0f;
        for (int j = 0; j < preyCount; j++)
        {
            kills[j] = Mathf.FloorToInt(maximumKills[j] * huntFraction);
            plannedEnergy += kills[j] * energyPerPrey[j];
        }
        while (plannedEnergy < totalDeficit)
        {
            int best = -1;
            float remainder = -1f;
            for (int j = 0; j < preyCount; j++)
            {
                if (kills[j] >= maximumKills[j]) continue;
                float candidate = maximumKills[j] * huntFraction - kills[j];
                if (candidate > remainder) { best = j; remainder = candidate; }
            }
            if (best < 0) break;
            kills[best]++;
            plannedEnergy += energyPerPrey[best];
        }
        for (int j = 0; j < preyCount; j++)
        {
            float eligibleNeed = 0f;
            for (int i = 0; i < hunterCount; i++)
                if (effort[i, j] > 0f)
                    eligibleNeed += Mathf.Max(0f, deficit[i] - gained[i]);
            int actual = energyPerPrey[j] > 0f
                ? Mathf.Min(kills[j], Mathf.CeilToInt(eligibleNeed / energyPerPrey[j])) : 0;
            actual = Mathf.Min(actual, prey[j].speciesAmount);
            if (actual <= 0) continue;
            prey[j].speciesAmount -= actual;
            prey[j].deathsToday += actual;
            prey[j].predationDeathsToday += actual;
            DistributePreyEnergy(effort, j, demand, deficit, gained,
                actual * energyPerPrey[j]);
        }
        for (int i = 0; i < hunterCount; i++)
        {
            PopulationData hunter = hunters[i];
            float energy = hunter.energyReserve + gained[i];
            hunter.huntingEnergyToday = gained[i];
            hunter.allocatedBiomass = Mathf.Min(demand[i], energy);
            hunter.energyReserve = Mathf.Clamp(energy - demand[i], 0f,
                demand[i] * PredatorReserveDays);
        }
    }

    private static void DistributePreyEnergy(float[,] effort, int preyIndex,
        float[] demand, float[] deficit, float[] gained, float pool)
    {
        int hunterCount = demand.Length;
        for (int round = 0; round < hunterCount && pool > 0.0001f; round++)
        {
            float totalWeight = 0f;
            for (int i = 0; i < hunterCount; i++)
                totalWeight += effort[i, preyIndex] *
                    Mathf.Max(0f, deficit[i] - gained[i]) / Mathf.Max(1f, demand[i]);
            if (totalWeight <= 0f) break;
            float allocated = 0f;
            for (int i = 0; i < hunterCount; i++)
            {
                float need = Mathf.Max(0f, deficit[i] - gained[i]);
                float weight = effort[i, preyIndex] * need / Mathf.Max(1f, demand[i]);
                float share = Mathf.Min(need, pool * weight / totalWeight);
                gained[i] += share;
                allocated += share;
            }
            if (allocated <= 0.0001f) break;
            pool -= allocated;
        }
        // A final whole prey can exceed today's deficit; its extra energy is
        // shared among eligible hunters and then capped by their reserve limit.
        float totalEffort = 0f;
        for (int i = 0; i < hunterCount; i++) totalEffort += effort[i, preyIndex];
        if (totalEffort > 0f)
            for (int i = 0; i < hunterCount; i++)
                gained[i] += pool * effort[i, preyIndex] / totalEffort;
    }
}
