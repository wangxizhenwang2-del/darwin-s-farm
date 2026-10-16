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

    private void LogDirectedTraitUnlocks(int day)
    {
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.community == null) continue;
            foreach (PopulationData p in block.community)
                if (p != null && p.speciesAmount > 0 &&
                    p.directedSplitTarget != null &&
                    p.directedTraitUnlockDay == day)
                    LogEcology(day, block, p, "directed_trait_unlock",
                        "target=" + p.directedSplitTarget.SpeciesName +
                        " movement=" + p.movementAbility +
                        " size=" + p.size + " fertility=" + p.fertility);
        }
    }

    private void RunLocalSplits(int day)
    {
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.waterCoverage != WaterCoverage.Land ||
                block.community == null) continue;
            // Work from a snapshot; a new branch never branches again today.
            foreach (PopulationData source in new List<PopulationData>(block.community))
            {
                SplitGateInfo gate = ReadSplitGate(block, source, day);
                if (gate.reason != "ready") continue;
                int branchCount = gate.founders;
                SpeciesData target = gate.target;
                float before = gate.demandBefore, after = gate.demandAfter;
                float environmentalK = gate.environmentalK;
                int countBefore = source.speciesAmount;
                PopulationData branch = PopulationTransfer.Split(source,
                    branchCount, block, day, migrationCooldownDays);
                float approach = LandRules.directedSplitTraitFraction;
                branch.movementAbility = ApproachTrait(source.movementAbility,
                    target.baseMovementAbility, approach);
                branch.size = Mathf.Clamp(ApproachTrait(source.size,
                    target.baseSize, approach), 1, 100);
                branch.fertility = ApproachTrait(source.fertility,
                    target.baseFertility, approach);
                branch.movementMutationRemainder = 0f;
                branch.sizeMutationRemainder = 0f;
                branch.fertilityMutationRemainder = 0f;
                branch.directedSplitTarget = target;
                branch.directedTraitUnlockDay = day +
                    LandRules.directedSplitTraitLockDays;
                branch.splitStrategy = target.baseSize >= 25 ? 1 : 0;
                source.directedSplitTarget = null;
                source.directedTraitUnlockDay = 0;
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
                    + " approach=" + approach.ToString("F2")
                    + " traitUnlockDay=" + branch.directedTraitUnlockDay
                    + " founderFraction=" + LandRules.splitFounderFraction.ToString("F2")
                    + " minFood10=" + LandLifeRules.Average(source.foodSatisfactionHistory).ToString("F3")
                    + " environmentalK=" + environmentalK.ToString("F1")
                    + " demandBefore="
                    + before.ToString("F1") + " demandAfter=" + after.ToString("F1")
                    + " countToEnvironmentalK="
                    + (countBefore / environmentalK).ToString("F3"));
                break; // one split per tile per day, cooldown applies immediately
            }
        }
    }

    public struct SplitGateInfo
    {
        public string reason;
        public SpeciesData target;
        public int founders;
        public float environmentalK, foodAverage10;
        public float demandBefore, demandAfter;
    }

    public SplitGateInfo ReadSplitGate(BlockInfo block,
        PopulationData source, int day)
    {
        var gate = new SplitGateInfo { reason = "not_land" };
        if (block == null || block.waterCoverage != WaterCoverage.Land ||
            block.community == null || source == null ||
            !block.community.Contains(source)) return gate;
        gate.founders = Mathf.RoundToInt(source.speciesAmount *
            LandRules.splitFounderFraction);
        gate.environmentalK = LandLifeRules.EnvironmentalCarryingCapacity(
            block, source, LandRules);
        gate.foodAverage10 = source.foodSatisfactionHistory != null &&
            source.foodSatisfactionHistory.Count > 0
                ? LandLifeRules.Average(source.foodSatisfactionHistory) : 0f;
        if (LivingPopulationCount(block) > 3) gate.reason = "tile_group_limit";
        else if (day < block.nextLocalSplitDay) gate.reason = "split_cooldown";
        else if (source.species == null) gate.reason = "no_species";
        else if (source.speciesAmount <= 200) gate.reason = "population_200";
        else if (source.foodSatisfactionHistory == null ||
            source.foodSatisfactionHistory.Count < 10) gate.reason = "food_history_10";
        else if (gate.foodAverage10 < LandRules.splitMinimumFoodSatisfaction)
            gate.reason = "food_satisfaction";
        else if (HasSameSpeciesCompanion(block, source))
            gate.reason = "same_species_companion";
        else if (gate.environmentalK <= 0f ||
            source.speciesAmount < gate.environmentalK *
                LandRules.splitMinimumRenewalPressure)
            gate.reason = "below_split_K";
        else if (gate.founders < 10 ||
            source.speciesAmount - gate.founders < 10)
            gate.reason = "founder_under_10";
        else
        {
            gate.target = SelectLocalSplitTarget(block, source,
                gate.founders, out gate.demandBefore, out gate.demandAfter);
            gate.reason = gate.target != null ? "ready" :
                DescribeMissingSplitTarget(block, source, gate.founders);
        }
        return gate;
    }

    private static bool HasSameSpeciesCompanion(BlockInfo block,
        PopulationData source)
    {
        foreach (PopulationData other in block.community)
            if (other != null && other != source && other.speciesAmount > 0 &&
                SameSpecies(source, other)) return true;
        return false;
    }

    private string DescribeMissingSplitTarget(BlockInfo block,
        PopulationData source, int founders)
    {
        bool connected = false, hasRoom = false, sourceFood = false;
        foreach (SpeciesData target in ConnectedTargets(source.species))
        {
            if (target == null || target.baseEcologicalNiche != EcologicalNiche.Land)
                continue;
            connected = true;
            if (HasLivingSpecies(block, target)) continue;
            hasRoom = true;
            if (!CanSustainDirectedSplit(block, source, target, founders,
                out _, out _)) continue;
            sourceFood = true;
            if (HasEvolutionFoodSupport(block, source, target,
                founders, false)) return "ready";
        }
        return !connected ? "no_land_target" : !hasRoom
            ? "target_present" : !sourceFood
                ? "split_food_or_stock" : "target_food_unavailable";
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
            + LandLifeRules.Demand(branchCount,
                ApproachTrait(source.size, target.baseSize,
                    LandRules.directedSplitTraitFraction),
                ApproachTrait(source.movementAbility, target.baseMovementAbility,
                    LandRules.directedSplitTraitFraction),
                ApproachTrait(source.fertility, target.baseFertility,
                    LandRules.directedSplitTraitFraction), fitness,
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

    private static int ApproachTrait(int source, int target, float fraction) =>
        Mathf.RoundToInt(source + (target - source) * fraction);

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
                survivor.directedTraitUnlockDay = 0;
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
