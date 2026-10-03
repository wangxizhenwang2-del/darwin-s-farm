using System.Collections.Generic;
using UnityEngine;

// 先分植物，再逐级分猎物。食物规则只接收地块和繁殖倍率，不持有控制器状态。
internal static class FoodWeb
{
    private const float PreyEnergyPerSize = 15f;
    private const float PredationEfficiency = 0.8f;
    public const int MaxTrophicLevel = 2;
    public const float PredatorReserveDays = 10f;
    private const float MaxDailyPreyFraction = 0.1f;

    public static void GrowPlants(BlockInfo block)
    {
        float maximum = Mathf.Max(0f, block.maxPlantBiomass);
        float before = Mathf.Clamp(block.plantBiomass, 0f, maximum);
        block.plantBiomass = Mathf.Min(maximum,
            before + Mathf.Max(0, block.habitatRecovery));
        block.plantGrowthToday = block.plantBiomass - before;
    }

    public static float FeedCommunity(BlockInfo block, float reproductionScale)
    {
        float eaten = FeedHerbivores(block);
        int highestLevel = 0;
        foreach (PopulationData population in block.community)
            highestLevel = Mathf.Max(highestLevel, population.trophicLevel);
        for (int level = 1; level <= highestLevel; level++)
            FeedPredators(block, level, reproductionScale);
        return eaten;
    }

    public static void SpendPlants(BlockInfo block, float eaten)
    {
        block.consumedBiomassToday = eaten;
        block.plantBiomass = Mathf.Max(0f, block.plantBiomass - eaten);
    }

    public static float FoodPerDemand(BlockInfo block, int level,
        PopulationData consumer, float reproductionScale)
    {
        if (level < 0 || level > MaxTrophicLevel) return 0f;
        float food = 0f;
        if (level == 0)
        {
            food = Mathf.Min(Mathf.Max(0f, block.habitatRecovery),
                Mathf.Max(0f, block.maxPlantBiomass));
        }
        else
        {
            foreach (PopulationData prey in block.community)
            {
                if (prey == consumer || prey.speciesAmount <= 0 ||
                    prey.trophicLevel != level - 1) continue;
                food += SustainablePreyGrowth(prey, reproductionScale) * prey.size
                    * PreyEnergyPerSize * PredationEfficiency
                    * CaptureEfficiency(consumer.movementAbility, consumer.size, prey);
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

    private static float CaptureEfficiency(float predatorMovement, float predatorSize,
        PopulationData prey)
    {
        // 较快、较大的捕食者获取同一猎物所需的追逐成本更低。
        return Mathf.Clamp(1f + (predatorMovement - prey.movementAbility
            + predatorSize - prey.size) / 200f, 0.5f, 1.5f);
    }

    private static float ForagingEfficiency(PopulationData population)
    {
        return 0.75f + 0.0025f * Mathf.Clamp(population.movementAbility, 0, 100);
    }

    private static float SustainablePreyGrowth(PopulationData prey, float reproductionScale)
    {
        if (prey.speciesAmount <= 0 || prey.carryingCapacity <= 0f) return 0f;
        float r = prey.fertility / 100f * reproductionScale;
        float fitness = prey.environmentalFitness / 100f;
        float demand = prey.energyNeed * prey.populationBeforePredation;
        float intake = demand > 0f ? Mathf.Clamp01(prey.allocatedBiomass / demand) : 0f;
        return Mathf.Min(r * prey.carryingCapacity / 4f, r * prey.speciesAmount)
            * fitness * intake;
    }

    private static float FeedHerbivores(BlockInfo block)
    {
        float foodLeft = block.plantBiomass;
        float eaten = 0f;

        List<PopulationData> herbivores = new List<PopulationData>();

        // 只有 0 级种群吃植物，其余种群稍后捕食。
        foreach (PopulationData population in block.community)
        {
            if (population.speciesAmount > 0 &&
                population.trophicLevel == 0)
            {
                herbivores.Add(population);
            }
        }

        float bestForaging = 0f;
        float bestFitness = 0f;
        foreach (PopulationData population in herbivores)
        {
            float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
            bestForaging = Mathf.Max(bestForaging,
                ForagingEfficiency(population) * fitness);
            bestFitness = Mathf.Max(bestFitness, fitness);
        }
        foodLeft *= bestForaging;

        // 库存能救急；长期能养活多少个体，仍要看每天长回多少。
        float totalCapacityWeight = 0f;
        foreach (PopulationData population in herbivores)
        {
            float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
            totalCapacityWeight += population.energyNeed * population.speciesAmount
                * fitness * ForagingEfficiency(population);
        }

        float dailyRecovery = Mathf.Min(Mathf.Max(0, block.habitatRecovery),
            Mathf.Max(0f, block.maxPlantBiomass)) * bestFitness;
        if (totalCapacityWeight > 0f)
        {
            foreach (PopulationData population in herbivores)
            {
                float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
                float competitionWeight = population.energyNeed * population.speciesAmount
                    * fitness * ForagingEfficiency(population);
                float dailyShare = dailyRecovery * competitionWeight / totalCapacityWeight;
                population.carryingCapacity = dailyShare / population.energyNeed;
            }
        }

        while (foodLeft > 0.01f &&
               herbivores.Count > 0)
        {
            float totalWeight = 0f;

            // 这一轮的份额取决于还缺多少、是否适应环境、会不会觅食。
            foreach (PopulationData population in herbivores)
            {
                float totalDemand = population.energyNeed * population.speciesAmount;
                float remainingDemand = totalDemand - population.allocatedBiomass;
                float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
                float competitionWeight = remainingDemand * fitness * ForagingEfficiency(population);
                totalWeight += competitionWeight;
            }

            // 谁都没法利用这些食物，就留在地块里。
            if (totalWeight <= 0f)
            {
                break;
            }

            // 都按同一份剩余食物计算，避免先遍历的种群占便宜。
            float foodThisRound = foodLeft;
            List<PopulationData> satisfied = new List<PopulationData>();
            float eatenThisRound = 0f;

            // 每个种群最多吃到自己的剩余需求。
            foreach (PopulationData population in herbivores)
            {
                float totalDemand = population.energyNeed * population.speciesAmount;
                float remainingDemand = totalDemand - population.allocatedBiomass;
                float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
                float competitionWeight = remainingDemand * fitness * ForagingEfficiency(population);
                float share = competitionWeight / totalWeight;
                float allocated = foodThisRound * share;

                float received = Mathf.Min(allocated, remainingDemand);
                population.allocatedBiomass += received;
                eatenThisRound += received;

                if (population.allocatedBiomass >= totalDemand - 0.01f)
                {
                    satisfied.Add(population);
                }
            }

            foodLeft -= eatenThisRound;
            eaten += eatenThisRound;

            // 吃饱的退出，剩余食物再分给还饿着的。
            foreach (PopulationData population in satisfied)
            {
                herbivores.Remove(population);
            }

            // 没人吃饱说明这一轮已分完可用食物，不必空转。
            if (satisfied.Count == 0)
            {
                break;
            }
        }

        return eaten;
    }

    private static void FeedPredators(BlockInfo block, int level, float reproductionScale)
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
            maxKills[i] = Mathf.FloorToInt(prey[i].speciesAmount * MaxDailyPreyFraction);
            energyPerPrey[i] = prey[i].size * PreyEnergyPerSize
                * PredationEfficiency * hunterEfficiency
                * CaptureEfficiency(weightedMovement / totalDemand,
                    weightedSize / totalDemand, prey[i]);
            availableEnergy += maxKills[i] * energyPerPrey[i];
            sustainableEnergy += SustainablePreyGrowth(prey[i], reproductionScale) * energyPerPrey[i];
        }

        // 捕食者承载量由猎物的可持续日生产量决定，而不是现存全部猎物。
        float dailyFood = Mathf.Min(availableEnergy, sustainableEnergy);
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

        while (gainedEnergy < totalDeficit && gainedEnergy < availableEnergy)
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
