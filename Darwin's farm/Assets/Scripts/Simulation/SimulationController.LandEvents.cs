using System;
using System.Collections.Generic;
using UnityEngine;

public partial class SimulationController
{
    public event Action<PopulationTransitionResult> OnLocalSplitResolved;
    public event Action<int, BlockInfo, PopulationData, string, string> OnEcologyEvent;

    private void LogEcology(int day, BlockInfo block, PopulationData population,
        string kind, string detail)
    {
#if UNITY_5_3_OR_NEWER
        Debug.Log("[Ecology] day=" + day + " kind=" + kind + " population="
            + (population != null ? population.PopulationId : "") + " " + detail);
#endif
        OnEcologyEvent?.Invoke(day, block, population, kind, detail);
    }

    private void RemoveEndangered(int day)
    {
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.community == null) continue;
            for (int i = block.community.Count - 1; i >= 0; i--)
            {
                PopulationData p = block.community[i];
                if (p == null || p.endangeredDays < 10) continue;
                LogEcology(day, block, p, "local_extinction",
                    "species=" + (p.species != null ? p.species.SpeciesName : p.lineageName)
                    + " count=" + p.speciesAmount + " days=" + p.endangeredDays);
                block.community.RemoveAt(i);
            }
        }
    }

    private void RunLocalSplits(int day)
    {
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.waterCoverage != WaterCoverage.Land ||
                block.community == null || block.community.Count > 3 ||
                day < block.nextLocalSplitDay) continue;
            // Work from a snapshot; a new branch never branches again today.
            foreach (PopulationData source in new List<PopulationData>(block.community))
            {
                if (source == null || source.speciesAmount <= 200 ||
                    source.foodSatisfactionHistory == null ||
                    source.foodSatisfactionHistory.Count < 10 ||
                    LandLifeRules.Average(source.foodSatisfactionHistory)
                        < LandRules.splitMinimumFoodSatisfaction)
                    continue;
                bool alreadyBranched = false;
                foreach (PopulationData other in block.community)
                    if (other != source && other != null &&
                        SameSpecies(source, other)) { alreadyBranched = true; break; }
                if (alreadyBranched) continue;
                int largeSize = Mathf.Clamp(source.size + 30, 10, 90);
                int smallSize = Mathf.Clamp(source.size - 30, 10, 90);
                int largeFertility = Mathf.Clamp(source.fertility - 30, 10, 90);
                int smallFertility = Mathf.Clamp(source.fertility + 30, 10, 90);
                int largeCount = Mathf.RoundToInt(source.speciesAmount
                    * smallSize / (float)(largeSize + smallSize));
                int smallCount = source.speciesAmount - largeCount;
                if (largeCount < 10 || smallCount < 10) continue;
                float fitness = LandLifeRules.Fitness(block,
                    source.fitTemperature, source.fitHumidity);
                float before = 0f;
                foreach (PopulationData consumer in block.community)
                    if (consumer != null && consumer.trophicLevel == source.trophicLevel)
                        before += LandLifeRules.Demand(consumer,
                            consumer.speciesAmount, LandLifeRules.Fitness(block,
                                consumer.fitTemperature, consumer.fitHumidity),
                            LandRules);
                float original = LandLifeRules.Demand(source,
                    source.speciesAmount, fitness, LandRules);
                float after = before - original
                    + LandLifeRules.Demand(largeCount, largeSize,
                        source.movementAbility, largeFertility, fitness, LandRules)
                    + LandLifeRules.Demand(smallCount, smallSize,
                        source.movementAbility, smallFertility, fitness, LandRules);
                float sustainable = source.trophicLevel == 0
                    ? Mathf.Max(0, block.habitatRecovery)
                    : LandLifeRules.SustainablePreyFood(block, source, LandRules);
                float stock = block.plantBiomass;
                if (source.trophicLevel > 0)
                {
                    stock = 0f;
                    foreach (PopulationData prey in block.community)
                        if (prey != null && prey.speciesAmount > 0 &&
                            FoodWeb.CanEat(source, prey))
                            stock += prey.speciesAmount
                                * LandLifeRules.PreyEnergy(source, prey, LandRules);
                }
                if (sustainable <= 0f ||
                    before < sustainable * LandRules.splitMinimumRenewalPressure ||
                    after > sustainable * LandRules.splitMaximumRenewalLoad ||
                    stock < after * LandRules.splitStockDays)
                    continue;
                int countBefore = source.speciesAmount;
                PopulationData large = PopulationTransfer.Split(source,
                    largeCount, block, day, migrationCooldownDays);
                source.size = smallSize;
                source.fertility = smallFertility;
                large.size = largeSize;
                large.fertility = largeFertility;
                source.localSplitBranch = large.localSplitBranch = true;
                source.evolutionConversions.Clear();
                large.evolutionConversions.Clear();
                block.community.Add(large);
                block.nextLocalSplitDay = day + LandRules.splitCooldownDays;
                OnLocalSplitResolved?.Invoke(new PopulationTransitionResult(
                    PopulationTransitionKind.LocalSplit, day, block, block,
                    source, large, largeCount, countBefore, 0,
                    PopulationArrivalOutcome.Created));
                LogEcology(day, block, large, "local_split",
                    "source=" + source.PopulationId + " large=" + largeCount
                    + " size=" + largeSize + " fertility=" + largeFertility
                    + " small=" + smallCount + " size=" + smallSize
                    + " fertility=" + smallFertility + " demandBefore="
                    + before.ToString("F1") + " demandAfter=" + after.ToString("F1"));
                break; // one split per tile per day, cooldown applies immediately
            }
        }
    }
}
