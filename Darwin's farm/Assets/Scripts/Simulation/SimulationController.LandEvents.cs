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
        // Trait selection can happen for many populations every day. Keep it
        // available to listeners while reserving the Console for lifecycle events.
        if (kind != "trait_mutation")
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
                block.community == null || LivingPopulationCount(block) > 3 ||
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
                int branchCount = Mathf.RoundToInt(source.speciesAmount *
                    LandRules.splitFounderFraction);
                if (branchCount < 10 || source.speciesAmount - branchCount < 10)
                    continue;
                SpeciesData target = SelectLocalSplitTarget(block, source,
                    branchCount, out float before, out float after);
                if (target == null) continue;
                int countBefore = source.speciesAmount;
                PopulationData branch = PopulationTransfer.Split(source,
                    branchCount, block, day, migrationCooldownDays);
                branch.movementAbility = target.baseMovementAbility;
                branch.size = Mathf.Clamp(target.baseSize, 1, 100);
                branch.fertility = target.baseFertility;
                branch.movementMutationRemainder = 0f;
                branch.sizeMutationRemainder = 0f;
                branch.fertilityMutationRemainder = 0f;
                branch.directedSplitTarget = target;
                branch.splitStrategy = target.baseSize >= 25 ? 1 : 0;
                source.directedSplitTarget = null;
                source.localSplitBranch = branch.localSplitBranch = true;
                source.sameSpeciesCoexistenceDays = 0;
                branch.sameSpeciesCoexistenceDays = 0;
                source.mutationSelectionProgress = branch.mutationSelectionProgress = 0f;
                source.mutationSelectionCandidate = branch.mutationSelectionCandidate = -1;
                source.lastMutationEffect = "分化后保留原性状";
                branch.lastMutationEffect = "分化方向：" + target.SpeciesName;
                source.evolutionConversions.Clear();
                branch.evolutionConversions.Clear();
                block.community.Add(branch);
                LandLifeRules.RefreshEnvironmentMetrics(block, LandRules);
                block.nextLocalSplitDay = day + LandRules.splitCooldownDays;
                OnLocalSplitResolved?.Invoke(new PopulationTransitionResult(
                    PopulationTransitionKind.LocalSplit, day, block, block,
                    source, branch, branchCount, countBefore, 0,
                    PopulationArrivalOutcome.Created));
                LogEcology(day, block, branch, "local_split",
                    "source=" + source.PopulationId + " target=" + target.SpeciesName
                    + " branch=" + branchCount + " retained=" + source.speciesAmount
                    + " movement=" + branch.movementAbility
                    + " size=" + branch.size + " fertility=" + branch.fertility
                    + " demandBefore="
                    + before.ToString("F1") + " demandAfter=" + after.ToString("F1")
                    + " countToEnvironmentalK="
                    + (countBefore / environmentalK).ToString("F3"));
                break; // one split per tile per day, cooldown applies immediately
            }
        }
    }

    private SpeciesData SelectLocalSplitTarget(BlockInfo block, PopulationData source,
        int branchCount, out float demandBefore, out float demandAfter)
    {
        demandBefore = demandAfter = 0f;
        if (source.species == null) return null;
        SpeciesData best = null;
        int bestDistance = int.MaxValue;
        foreach (SpeciesData target in ConnectedTargets(source.species))
        {
            if (target == null || target.baseEcologicalNiche != EcologicalNiche.Land ||
                HasLivingSpecies(block, target) ||
                !CanSustainDirectedSplit(block, source, target, branchCount,
                    out float before, out float after) ||
                !HasEvolutionFoodSupport(block, source, target, branchCount, false))
                continue;
            int distance = Mathf.Abs(source.fitTemperature - target.baseFitTemperature) +
                Mathf.Abs(source.fitHumidity - target.baseFitHumidity);
            if (best != null && (distance > bestDistance ||
                distance == bestDistance && CompareSpeciesOrder(target, best) >= 0))
                continue;
            best = target;
            bestDistance = distance;
            demandBefore = before;
            demandAfter = after;
        }
        return best;
    }

    private bool CanSustainDirectedSplit(BlockInfo block, PopulationData source,
        SpeciesData target, int branchCount, out float before, out float after)
    {
        float fitness = LandLifeRules.Fitness(block,
            source.fitTemperature, source.fitHumidity);
        float sustainable = source.trophicLevel == 0
            ? Mathf.Max(0, block.habitatRecovery)
            : LandLifeRules.AvailableSustainablePreyFood(block,
                source, LandRules, subtractSameLevel: false);
        float birthFactor = source.trophicLevel == 0
            ? LandLifeRules.DensityBirthMultiplier(
                LandLifeRules.HerbivoreAdultDemand(block, LandRules),
                sustainable, LandRules) : 1f;
        before = 0f;
        foreach (PopulationData consumer in block.community)
            if (consumer != null && consumer.speciesAmount > 0 &&
                consumer.trophicLevel == source.trophicLevel)
                before += LandLifeRules.Demand(consumer,
                    consumer.speciesAmount, LandLifeRules.Fitness(block,
                        consumer.fitTemperature, consumer.fitHumidity),
                    LandRules, birthFactor);
        float original = LandLifeRules.Demand(source,
            source.speciesAmount, fitness, LandRules, birthFactor);
        int retainedCount = source.speciesAmount - branchCount;
        after = before - original
            + LandLifeRules.Demand(retainedCount, source.size,
                source.movementAbility, source.fertility, fitness,
                LandRules, birthFactor)
            + LandLifeRules.Demand(branchCount, target.baseSize,
                target.baseMovementAbility, target.baseFertility, fitness,
                LandRules, birthFactor);
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
        return sustainable > 0f &&
            after <= sustainable * LandRules.splitMaximumRenewalLoad &&
            stock >= after * LandRules.splitStockDays;
    }

    private void RunSameSpeciesMerges(int day)
    {
        int waitDays = LandRules.sameSpeciesMergeDays;
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.community == null) continue;
            var visited = new HashSet<PopulationData>();
            foreach (PopulationData first in new List<PopulationData>(block.community))
            {
                if (first == null || first.speciesAmount <= 0 || !visited.Add(first))
                    continue;
                var group = new List<PopulationData> { first };
                foreach (PopulationData other in block.community)
                    if (other != null && other != first && other.speciesAmount > 0 &&
                        other.ecologicalNiche == first.ecologicalNiche &&
                        PopulationTransfer.IsSameSpecies(first, other))
                    {
                        group.Add(other);
                        visited.Add(other);
                    }
                if (group.Count == 1)
                {
                    first.sameSpeciesCoexistenceDays = 0;
                    continue;
                }
                int elapsed = 0;
                foreach (PopulationData population in group)
                    elapsed = Mathf.Max(elapsed,
                        population.sameSpeciesCoexistenceDays);
                elapsed++;
                foreach (PopulationData population in group)
                    population.sameSpeciesCoexistenceDays = elapsed;
                if (elapsed < waitDays) continue;

                PopulationData survivor = group[0];
                foreach (PopulationData population in group)
                    if (population.speciesAmount > survivor.speciesAmount)
                        survivor = population;
                int groupsBefore = group.Count;
                int countBefore = 0;
                foreach (PopulationData population in group)
                {
                    countBefore += population.speciesAmount;
                    if (population == survivor) continue;
                    PopulationTransfer.Merge(survivor, population);
                    block.community.Remove(population);
                }
                survivor.localSplitBranch = false;
                survivor.splitStrategy = 0;
                survivor.directedSplitTarget = null;
                survivor.sameSpeciesCoexistenceDays = 0;
                survivor.evolutionConversions.Clear();
                survivor.mutationSelectionProgress = 0f;
                survivor.mutationSelectionCandidate = -1;
                survivor.lastMutationEffect = "同种群自动合并";
                block.nextLocalSplitDay = day +
                    LandRules.postMergeSplitCooldownDays;
                LandLifeRules.RefreshEnvironmentMetrics(block, LandRules);
                OnBlockCommunityChanged?.Invoke(block);
                LogEcology(day, block, survivor, "same_species_merge",
                    "groups=" + groupsBefore + " count=" + countBefore
                    + " nextSplitDay=" + block.nextLocalSplitDay);
            }
        }
    }
}
