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
                float environmentalK = LandLifeRules.EnvironmentalCarryingCapacity(
                    block, source, LandRules);
                if (environmentalK <= 0f ||
                    source.speciesAmount <
                        environmentalK * LandRules.splitMinimumRenewalPressure)
                    continue;
                int largeSize = Mathf.Clamp(source.size + 30, 10, 90);
                int smallSize = Mathf.Min(source.size,
                    Mathf.Clamp(source.size - 30, 10, 90));
                int largeFertility = Mathf.Clamp(source.fertility - 30, 10, 90);
                int smallFertility = Mathf.Clamp(source.fertility + 30, 10, 90);
                float fitness = LandLifeRules.Fitness(block,
                    source.fitTemperature, source.fitHumidity);
                // Seed by inverse squared size, then keep the resulting
                // population under daily renewable supply at the split.
                int largeCount = Mathf.RoundToInt(source.speciesAmount *
                    LandRules.splitFounderFraction);
                float sustainable = source.trophicLevel == 0
                    ? Mathf.Max(0, block.habitatRecovery)
                    : LandLifeRules.AvailableSustainablePreyFood(block,
                        source, LandRules, subtractSameLevel: false);
                float birthFactor = source.trophicLevel == 0
                    ? LandLifeRules.DensityBirthMultiplier(
                        LandLifeRules.HerbivoreAdultDemand(block, LandRules),
                        sustainable, LandRules) : 1f;
                float smallDemand = LandLifeRules.Demand(source.speciesAmount,
                    smallSize, source.movementAbility, smallFertility,
                    fitness, LandRules, birthFactor);
                float additionalPerLarge = LandLifeRules.Demand(1, largeSize,
                    source.movementAbility, largeFertility, fitness, LandRules,
                    birthFactor)
                    - LandLifeRules.Demand(1, smallSize, source.movementAbility,
                        smallFertility, fitness, LandRules, birthFactor);
                float before = 0f;
                foreach (PopulationData consumer in block.community)
                    if (consumer != null && consumer.trophicLevel == source.trophicLevel)
                        before += LandLifeRules.Demand(consumer,
                            consumer.speciesAmount, LandLifeRules.Fitness(block,
                                consumer.fitTemperature, consumer.fitHumidity),
                            LandRules, birthFactor);
                float original = LandLifeRules.Demand(source,
                    source.speciesAmount, fitness, LandRules, birthFactor);
                if (additionalPerLarge > 0f)
                    largeCount = Mathf.Min(largeCount, Mathf.FloorToInt(
                        (sustainable - (before - original + smallDemand))
                        * LandRules.splitHeadroomUse
                        / additionalPerLarge));
                int smallCount = source.speciesAmount - largeCount;
                if (largeCount < 10 || smallCount < 10) continue;
                float after = before - original
                    + LandLifeRules.Demand(largeCount, largeSize,
                        source.movementAbility, largeFertility, fitness, LandRules,
                        birthFactor)
                    + LandLifeRules.Demand(smallCount, smallSize,
                        source.movementAbility, smallFertility, fitness, LandRules,
                        birthFactor);
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
                source.splitStrategy = -1;
                large.splitStrategy = 1;
                source.mutationSelectionProgress = large.mutationSelectionProgress = 0f;
                source.mutationSelectionCandidate = large.mutationSelectionCandidate = -1;
                source.lastMutationEffect = large.lastMutationEffect = "分化形成：体型 / 繁殖力";
                source.evolutionConversions.Clear();
                large.evolutionConversions.Clear();
                block.community.Add(large);
                LandLifeRules.RefreshEnvironmentMetrics(block, LandRules);
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
                    + before.ToString("F1") + " demandAfter=" + after.ToString("F1")
                    + " countToEnvironmentalK="
                    + (countBefore / environmentalK).ToString("F3"));
                break; // one split per tile per day, cooldown applies immediately
            }
        }
    }
}
