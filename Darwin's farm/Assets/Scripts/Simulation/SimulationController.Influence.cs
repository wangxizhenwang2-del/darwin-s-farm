using System;
using System.Collections.Generic;
using DarwinFarm.Environment;
using UnityEngine;

public partial class SimulationController
{
    // These durations and weights are model tuning candidates, not UI costs.
    private const int InfluenceDays = 7;
    private const int ResidualDays = 5;
    private const float PrimaryInfluence = 3f;
    private const float SecondaryInfluence = 1.5f;

    // Called only after the real player technology transaction succeeds.
    // Existing physical environment tracks continue when a population clears
    // its old personal influence during formal evolution.
    public void RecordPlayerTechnology(BlockInfo block,
        EnvironmentTechnologyKind kind, int tier, int day)
    {
        // Preserve the public transaction hook. Recovery uses the separately
        // measured delta below. Stock probability is not yet frozen; climate
        // follows the actual environment in the natural phase. No old 7+5 day
        // primary/secondary pressure is issued for any player tool.
        // A new committed tile transaction may start a fresh movement round.
        if (block != null && block.waterCoverage == WaterCoverage.Land)
            block.neighborOpportunityRevision++;
    }

    internal void RecordActualRecoveryGuidance(BlockInfo block,
        float actualDelta, int day)
    {
        if (block == null || block.community == null ||
            block.waterCoverage != WaterCoverage.Land ||
            Mathf.Abs(actualDelta) < 0.0001f) return;
        int direction = actualDelta > 0f ? 1 : -1;
        RecoveryGuidanceTransaction pulse = block.recoveryGuidance;
        if (pulse == null)
            block.recoveryGuidance = pulse = new RecoveryGuidanceTransaction();
        if (pulse.direction != direction || day > pulse.lastDay)
        {
            pulse.serial++;
            pulse.key = StableTileKey(block) + ":recovery:" + pulse.serial;
            pulse.direction = direction;
            pulse.firstDay = day + 1;
            pulse.lastDay = day + 175;
            pulse.used = 0;
            pulse.totalDelta = 0f;
        }
        pulse.totalDelta = Mathf.Min(50000f,
            pulse.totalDelta + Mathf.Abs(actualDelta));
        pulse.cap = Mathf.Min(32,
            Mathf.CeilToInt(16f * pulse.totalDelta / 25000f));
        pulse.probability = Mathf.Min(0.60f,
            0.30f * pulse.totalDelta / 25000f);
        // Enrollment happens only at a successful land deployment. Later
        // immigrants do not inherit a tile command they never received.
        foreach (PopulationData population in block.community)
        {
            if (population == null || population.speciesAmount <= 0 ||
                population.trophicLevel != 0) continue;
            FertilityGuidancePulse redeemed = population.fertilityGuidance;
            if (redeemed == null)
                population.fertilityGuidance = redeemed =
                    new FertilityGuidancePulse();
            if (redeemed.transactionKey == pulse.key) continue;
            redeemed.transactionKey = pulse.key;
            redeemed.used = 0;
        }
    }

    internal void CancelRecoveryGuidance(BlockInfo block)
    {
        if (block != null && block.recoveryGuidance != null)
            block.recoveryGuidance.direction = 0;
    }

    private static void AddInfluence(PopulationData population,
        int trait, float weight, int day)
    {
        if (population.directedInfluences == null)
            population.directedInfluences = new List<DirectedInfluence>();
        population.directedInfluences.Add(new DirectedInfluence
        {
            trait = trait, weight = weight, firstDay = day + 1,
            lastDay = day + InfluenceDays,
            residualLastDay = day + InfluenceDays + ResidualDays
        });
    }

    private static void UpdateInfluences(PopulationData population, int day)
    {
        if (population.directedInfluences == null)
            population.directedInfluences = new List<DirectedInfluence>();
        population.directedInfluences.Clear();
    }

    private static float InfluenceNet(PopulationData population,
        int trait, int day, bool residual)
    {
        return 0f;
    }

    public bool SetManualEvolutionTarget(PopulationData population,
        SpeciesData target)
    {
        if (population == null || population.species == null) return false;
        if (target != null && !ConnectedTargets(population.species).Contains(target))
            return false;
        population.manualEvolutionTarget = target;
        population.trackedEvolutionTarget = null;
        ResetTargetProtection(population);
        return true;
    }

    private void RefreshTrackedTarget(BlockInfo block,
        PopulationData population, int day)
    {
        if (population.species == null) return;
        List<SpeciesData> targets = ConnectedTargets(population.species);
        SpeciesData manual = population.manualEvolutionTarget;
        if (manual != null && (!targets.Contains(manual) ||
            HasLivingSpecies(block, manual)))
            population.manualEvolutionTarget = manual = null;
        SpeciesData current = population.trackedEvolutionTarget;
        if (current != null && (!targets.Contains(current) ||
            HasLivingSpecies(block, current))) current = null;
        SpeciesData best = manual;
        int bestDistance = manual == null ? int.MaxValue :
            TraitDistance(population, manual);
        if (manual == null)
            foreach (SpeciesData target in targets)
            {
                if (target == null || HasLivingSpecies(block, target)) continue;
                int distance = TraitDistance(population, target);
                if (best == null || distance < bestDistance ||
                    distance == bestDistance && CompareSpeciesOrder(target, best) < 0)
                { best = target; bestDistance = distance; }
            }
        // Keep a valid current target unless the new one is clearly closer.
        if (manual == null && current != null &&
            TraitDistance(population, current) <= bestDistance + 2)
            best = current;
        if (best != population.trackedEvolutionTarget)
        {
            population.trackedEvolutionTarget = best;
            ResetTargetProtection(population);
        }
        UpdateTargetProtection(population, day);
    }

    private static int TraitDistance(PopulationData population, SpeciesData target) =>
        Mathf.Abs(population.fitTemperature - target.baseFitTemperature) +
        Mathf.Abs(population.fitHumidity - target.baseFitHumidity) +
        Mathf.Abs(population.movementAbility - target.baseMovementAbility) +
        Mathf.Abs(population.size - target.baseSize) +
        Mathf.Abs(population.fertility - target.baseFertility);

    private static void ResetTargetProtection(PopulationData population)
    {
        population.targetProtectionSince = new[] { -1, -1, -1 };
        population.targetProtectionReleased = new bool[3];
    }

    private void UpdateTargetProtection(PopulationData population, int day)
    {
        if (population.targetProtectionSince == null ||
            population.targetProtectionSince.Length != 3 ||
            population.targetProtectionReleased == null ||
            population.targetProtectionReleased.Length != 3)
            ResetTargetProtection(population);
        SpeciesData target = population.trackedEvolutionTarget;
        if (target == null) return;
        for (int i = 0; i < 3; i++)
        {
            int value = StrategyTrait(population, i);
            int center = StrategyTrait(target, i);
            bool inside = Mathf.Abs(value - center) <=
                LandRules.evolutionTraitTolerance;
            if (!inside)
            {
                population.targetProtectionSince[i] = -1;
                population.targetProtectionReleased[i] = false;
            }
            else if (population.targetProtectionSince[i] < 0 &&
                !population.targetProtectionReleased[i])
                population.targetProtectionSince[i] = day;
        }
    }

    private static int StrategyTrait(PopulationData population, int index) =>
        index == 0 ? population.movementAbility :
        index == 1 ? population.size : population.fertility;

    private static int StrategyTrait(SpeciesData target, int index) =>
        index == 0 ? target.baseMovementAbility :
        index == 1 ? target.baseSize : target.baseFertility;

    private bool LeavesProtectedTarget(PopulationData population,
        int trait, int proposed)
    {
        if (trait < 2 || population.trackedEvolutionTarget == null)
            return false;
        int index = trait - 2;
        if (population.targetProtectionReleased == null ||
            population.targetProtectionReleased[index]) return false;
        int center = StrategyTrait(population.trackedEvolutionTarget, index);
        return Mathf.Abs(StrategyTrait(population, index) - center) <=
            LandRules.evolutionTraitTolerance &&
            Mathf.Abs(proposed - center) > LandRules.evolutionTraitTolerance;
    }

    // Release at most one established target protection per declining day.
    // Once released, it stays released until its trait leaves the interval.
    private void ReleaseTargetProtectionFIFO(BlockInfo block,
        PopulationData population)
    {
        if (population.populationGrowthHistory == null ||
            population.populationGrowthHistory.Count < 10 ||
            LandLifeRules.Average(population.populationGrowthHistory) >= -0.01f ||
            population.trackedEvolutionTarget == null) return;
        int oldest = -1;
        for (int i = 0; i < 3; i++)
            if (!population.targetProtectionReleased[i] &&
                population.targetProtectionSince[i] >= 0 &&
                (oldest < 0 || population.targetProtectionSince[i] <
                    population.targetProtectionSince[oldest])) oldest = i;
        if (oldest < 0) return;
        // A legal compensated path within the protected region takes priority.
        LandTraits original = ReadLandTraits(population);
        float baseline = ForecastLandGrowth(block, population, original);
        for (int i = 0; i < 3; i++)
            for (int direction = -1; direction <= 1; direction += 2)
            {
                int value = StrategyTrait(population, i) + direction;
                int center = StrategyTrait(population.trackedEvolutionTarget, i);
                if (Mathf.Abs(value - center) >
                    LandRules.evolutionTraitTolerance) continue;
                LandTraits candidate = original;
                if (i == 0) candidate.m = value;
                else if (i == 1) candidate.s = value;
                else candidate.f = value;
                if (ForecastLandGrowth(block, population, candidate) >
                    baseline + LandRules.mutationGainThreshold *
                        population.speciesAmount) return;
            }
        population.targetProtectionReleased[oldest] = true;
        LogEcology(currentSimulationDay, block, population,
            "target_protection_fifo_release", "trait=" + (oldest + 2));
    }
}
