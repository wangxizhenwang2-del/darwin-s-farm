using System.Collections.Generic;
using UnityEngine;

// 先分植物，再逐级分猎物。食物规则只接收地块和繁殖倍率，不持有控制器状态。
internal static class FoodWeb
{
    private const float PreyEnergyPerSize = 15f;
    private const float PredationEfficiency = 0.8f;
    public const int MaxTrophicLevel = 2;
    public const float PredatorReserveDays = 10f;
    private const float SharedPlantFraction = 0.1f;

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
        int highestLevel = 0;
        foreach (PopulationData population in block.community)
            highestLevel = Mathf.Max(highestLevel, population.trophicLevel);
        for (int level = 1; level <= highestLevel; level++)
            FeedPredators(block, level, reproductionScale,
                overCapacityDeathRate, starvationDeathRate,
                DailyPreyFraction(level, levelOnePreyFraction, levelTwoPreyFraction));
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
        if (level < 0 || level > MaxTrophicLevel) return 0f;
        float food = 0f;
        if (level == 0)
        {
            float maximum = Mathf.Max(0f, block.maxPlantBiomass);
            float stock = Mathf.Clamp(block.plantBiomass, 0f, maximum);
            float projectedStock = stock + SimulationController.CalculatePlantGrowth(
                stock, maximum, block.habitatRecovery);
            food = Mathf.Min(PlantSupportFood(block, projectedStock),
                projectedStock * ForagingEfficiency(consumer));
        }
        else
        {
            foreach (PopulationData prey in block.community)
            {
                if (prey == consumer || prey.speciesAmount <= 0 ||
                    prey.trophicLevel != level - 1) continue;
                food += prey.speciesAmount * DailyPreyFraction(level,
                        levelOnePreyFraction, levelTwoPreyFraction)
                    * CaptureChance(consumer.movementAbility, consumer.size, prey)
                    * prey.size * PreyEnergyPerSize * PredationEfficiency;
            }
        }

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
    internal static float ExpectedPreyLoss(BlockInfo block, PopulationData target,
        float targetMovement, float targetSize,
        float levelOnePreyFraction, float levelTwoPreyFraction)
    {
        int level = target.trophicLevel + 1;
        float demand = 0f, deficit = 0f, foraging = 0f;
        float movement = 0f, size = 0f;
        foreach (PopulationData predator in block.community)
        {
            if (predator.speciesAmount <= 0 || predator.trophicLevel != level) continue;
            float ownDemand = predator.energyNeed * predator.speciesAmount;
            demand += ownDemand;
            deficit += Mathf.Max(0f, ownDemand - predator.energyReserve);
            foraging += ownDemand * ForagingEfficiency(predator)
                * Mathf.Clamp01(predator.environmentalFitness / 100f);
            movement += ownDemand * predator.movementAbility;
            size += ownDemand * predator.size;
        }
        if (demand <= 0f || deficit <= 0f) return 0f;
        float efficiency = foraging / demand;
        float totalEnergy = 0f, targetKills = 0f;
        foreach (PopulationData prey in block.community)
        {
            if (prey.speciesAmount <= 0 || prey.trophicLevel != target.trophicLevel) continue;
            float chance = CaptureEfficiency(movement / demand, size / demand,
                prey.movementAbility, prey.size, prey.trophicLevel) / 2f;
            float kills = prey.speciesAmount * DailyPreyFraction(level,
                levelOnePreyFraction, levelTwoPreyFraction) * efficiency * chance;
            float preySize = prey == target ? targetSize : prey.size;
            // 猎手数量和预期尝试次数保持当前值；候选体型仍改变每只猎物的能量。
            totalEnergy += kills * preySize * PreyEnergyPerSize * PredationEfficiency;
            if (prey == target)
                targetKills = prey.speciesAmount * DailyPreyFraction(level,
                        levelOnePreyFraction, levelTwoPreyFraction)
                    * efficiency * CaptureEfficiency(movement / demand, size / demand,
                        targetMovement, targetSize, prey.trophicLevel) / 2f;
        }
        return targetKills * (totalEnergy > 0f ? Mathf.Min(1f, deficit / totalEnergy) : 0f);
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
        float reproductionScale, float overCapacityDeathRate, float starvationDeathRate)
    {
        if (prey.speciesAmount <= 0 || prey.carryingCapacity <= 0f) return 0f;
        SimulationController.EstimateDemography(prey, prey.speciesAmount,
            reproductionScale, overCapacityDeathRate, starvationDeathRate *
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

    internal static float ProjectedPredatorCapacity(BlockInfo block,
        PopulationData candidate, float targetSize, float targetMovement,
        float targetFitness, float targetFertility, float reproductionScale,
        float overCapacityDeathRate, float starvationDeathRate,
        float levelOnePreyFraction, float levelTwoPreyFraction)
    {
        float totalDemand = 0f, totalForaging = 0f;
        float weightedMovement = 0f, weightedSize = 0f, candidateWeight = 0f;
        float candidateNeed = SimulationController.CalculateEnergyNeed(targetSize,
            targetMovement, targetFertility);
        foreach (PopulationData predator in block.community)
        {
            if (predator.speciesAmount <= 0 ||
                predator.trophicLevel != candidate.trophicLevel) continue;
            float need = predator == candidate ? candidateNeed : predator.energyNeed;
            float movement = predator == candidate ? targetMovement : predator.movementAbility;
            float size = predator == candidate ? targetSize : predator.size;
            float fitness = Mathf.Clamp01(predator == candidate ? targetFitness
                : predator.environmentalFitness / 100f);
            float demand = need * predator.speciesAmount;
            float weight = demand * ForagingEfficiency(movement) * fitness;
            totalDemand += demand;
            totalForaging += weight;
            weightedMovement += demand * movement;
            weightedSize += demand * size;
            if (predator == candidate) candidateWeight = weight;
        }
        if (totalDemand <= 0f || totalForaging <= 0f) return 0f;
        float sustainableEnergy = 0f;
        foreach (PopulationData prey in block.community)
        {
            if (prey.speciesAmount <= 0 ||
                prey.trophicLevel != candidate.trophicLevel - 1) continue;
            float kills = prey.speciesAmount * DailyPreyFraction(candidate.trophicLevel,
                    levelOnePreyFraction, levelTwoPreyFraction)
                * totalForaging / totalDemand
                * CaptureChance(weightedMovement / totalDemand,
                    weightedSize / totalDemand, prey);
            sustainableEnergy += Mathf.Min(kills, SupportedPreyHarvest(prey,
                reproductionScale, overCapacityDeathRate, starvationDeathRate))
                * prey.size * PreyEnergyPerSize * PredationEfficiency;
        }
        return sustainableEnergy * candidateWeight / totalForaging / candidateNeed;
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

    private static void FeedPredators(BlockInfo block, int level, float reproductionScale,
        float overCapacityDeathRate, float starvationDeathRate, float dailyPreyFraction)
    {
        List<PopulationData> predators = new List<PopulationData>();
        List<PopulationData> prey = new List<PopulationData>();
        float totalDemand = 0f;
        float totalDeficit = 0f;
        float totalForagingWeight = 0f;
        float weightedMovement = 0f;
        float weightedSize = 0f;

        // 同一营养级先统一读取数量与需求，再进行任何扣减。
        foreach (PopulationData population in block.community)
        {
            if (population.speciesAmount <= 0)
            {
                continue;
            }
            int populationLevel = population.trophicLevel;
            if (populationLevel == level)
            {
                predators.Add(population);
                float demand = population.energyNeed * population.speciesAmount;
                totalDemand += demand;
                totalDeficit += Mathf.Max(0f, demand - population.energyReserve);
                weightedMovement += demand * population.movementAbility;
                weightedSize += demand * population.size;
                totalForagingWeight += demand * ForagingEfficiency(population)
                    * Mathf.Clamp01(population.environmentalFitness / 100f);
            }
            else if (populationLevel == level - 1 && population.size > 0)
            {
                prey.Add(population);
            }
        }
        if (totalDemand <= 0f)
        {
            return;
        }

        float hunterEfficiency = totalForagingWeight / totalDemand;
        float availableEnergy = 0f;
        float sustainableEnergy = 0f;
        int[] maxKills = new int[prey.Count];
        float[] energyPerPrey = new float[prey.Count];
        for (int i = 0; i < prey.Count; i++)
        {
            float expectedKills = prey[i].speciesAmount * dailyPreyFraction
                * hunterEfficiency * CaptureChance(weightedMovement / totalDemand,
                    weightedSize / totalDemand, prey[i]);
            maxKills[i] = Mathf.FloorToInt(expectedKills);
            if (UnityEngine.Random.value < expectedKills - maxKills[i]) maxKills[i]++;
            energyPerPrey[i] = prey[i].size * PreyEnergyPerSize
                * PredationEfficiency;
            availableEnergy += expectedKills * energyPerPrey[i];
            sustainableEnergy += Mathf.Min(expectedKills,
                SupportedPreyHarvest(prey[i], reproductionScale,
                    overCapacityDeathRate, starvationDeathRate)) * energyPerPrey[i];
        }

        // K 使用可持续期望捕获量；实际捕获的小数部分只影响当天能量。
        float dailyFood = sustainableEnergy;
        foreach (PopulationData predator in predators)
        {
            float weight = predator.energyNeed * predator.speciesAmount
                * ForagingEfficiency(predator)
                * Mathf.Clamp01(predator.environmentalFitness / 100f);
            predator.carryingCapacity = totalForagingWeight > 0f
                ? dailyFood * weight / totalForagingWeight / predator.energyNeed : 0f;
        }

        // 储备先支付当天能耗；不足时才捕猎，整只猎物多出的能量留作后续几天使用。
        float fraction = availableEnergy > 0f
            ? Mathf.Min(1f, totalDeficit / availableEnergy) : 0f;
        int[] kills = new int[prey.Count];
        float gainedEnergy = 0f;
        for (int i = 0; i < prey.Count; i++)
        {
            kills[i] = Mathf.FloorToInt(maxKills[i] * fraction);
            gainedEnergy += kills[i] * energyPerPrey[i];
        }

        while (gainedEnergy < totalDeficit)
        {
            int best = -1;
            float bestRemainder = -1f;
            for (int i = 0; i < prey.Count; i++)
            {
                if (kills[i] >= maxKills[i])
                {
                    continue;
                }
                float remainder = maxKills[i] * fraction - kills[i];
                if (remainder > bestRemainder)
                {
                    best = i;
                    bestRemainder = remainder;
                }
            }
            if (best < 0)
            {
                break;
            }
            kills[best]++;
            gainedEnergy += energyPerPrey[best];
        }

        for (int i = 0; i < prey.Count; i++)
        {
            prey[i].speciesAmount -= kills[i];
            prey[i].deathsToday += kills[i];
        }

        float[] energyByPredator = new float[predators.Count];
        float remainingEnergy = gainedEnergy;
        for (int i = 0; i < predators.Count; i++)
        {
            energyByPredator[i] = predators[i].energyReserve;
        }
        while (remainingEnergy > 0.001f)
        {
            float weightTotal = 0f;
            for (int i = 0; i < predators.Count; i++)
            {
                PopulationData predator = predators[i];
                float demand = predator.energyNeed * predator.speciesAmount;
                weightTotal += Mathf.Max(0f, demand - energyByPredator[i])
                    * ForagingEfficiency(predator)
                    * Mathf.Clamp01(predator.environmentalFitness / 100f);
            }
            if (weightTotal <= 0f) break;

            float roundEnergy = remainingEnergy;
            float distributed = 0f;
            for (int i = 0; i < predators.Count; i++)
            {
                PopulationData predator = predators[i];
                float demand = predator.energyNeed * predator.speciesAmount;
                float deficit = Mathf.Max(0f, demand - energyByPredator[i]);
                float weight = deficit * ForagingEfficiency(predator)
                    * Mathf.Clamp01(predator.environmentalFitness / 100f);
                float amount = Mathf.Min(deficit, roundEnergy * weight / weightTotal);
                energyByPredator[i] += amount;
                distributed += amount;
            }
            if (distributed <= 0f) break;
            remainingEnergy -= distributed;
        }
        for (int i = 0; i < predators.Count; i++)
        {
            PopulationData predator = predators[i];
            float demand = predator.energyNeed * predator.speciesAmount;
            float extraWeight = demand * ForagingEfficiency(predator)
                * Mathf.Clamp01(predator.environmentalFitness / 100f);
            float extra = totalForagingWeight > 0f
                ? remainingEnergy * extraWeight / totalForagingWeight : 0f;
            float energy = energyByPredator[i] + extra;
            predator.allocatedBiomass = Mathf.Min(demand, energy);
            predator.energyReserve = Mathf.Clamp(energy - demand, 0f,
                demand * PredatorReserveDays);
        }
    }
}
