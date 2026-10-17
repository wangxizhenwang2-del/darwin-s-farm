using System;
using System.Collections.Generic;
using UnityEngine;

public partial class SimulationController
{
    // Separate from the serialized V6.1 switch. Never enabled by loading an
    // existing scene; candidate values can be supplied by a regression runner.
    private bool enableV62Mobility;
    private V62MobilityParameters v62Mobility = V62MobilityParameters.Candidate;

    internal void ConfigureV62Mobility(bool enabled, V62MobilityParameters candidate)
    {
        if (!candidate.IsValid) throw new ArgumentOutOfRangeException(nameof(candidate));
        v62Mobility = candidate;
        enableV62Mobility = enabled;
    }

    private struct V62NeighborForecast
    {
        internal BlockInfo target;
        internal int amount, elevationStep;
        internal float sourceValue, targetValue, normalizedGain;
        internal float foodAttraction, capacityCorrection;
        internal bool Valid => target != null && amount >= 10 && normalizedGain > 0f;
    }

    // One read-only counterfactual powers both long-term movement selection
    // and real migration admission. The candidate is the actual 10-100 head
    // cohort, competing with target residents through the existing food ledger.
    private V62NeighborForecast ForecastV62Neighbor(BlockInfo source,
        BlockInfo target, PopulationData population, LandTraits traits,
        int standardAmount, float reservedFood = 0f)
    {
        var forecast = new V62NeighborForecast();
        if (source == null || target == null || source == target ||
            population == null || population.ecologicalNiche != EcologicalNiche.Land ||
            source.waterCoverage != WaterCoverage.Land ||
            target.waterCoverage != WaterCoverage.Land ||
            source.Neighbors == null || target.Neighbors == null ||
            !V62HasNeighbor(source, target) ||
            !V62HasNeighbor(target, source) || !blockInfos.Contains(target) ||
            target.community == null || standardAmount < 10) return forecast;
        int step = Mathf.Abs(target.elevation - source.elevation);
        if (step > 1 || !LandMovementOpportunity.CanOfferNeighborSlot(
            LivingPopulationCount(target),
            HasSameSpeciesResident(target, population))) return forecast;

        PopulationData cohort = PopulationTransfer.CopyForMove(population,
            standardAmount);
        // Find the largest cohort whose adult maintenance is supported by
        // renewable food after current residents and reserved arrivals.
        int low = 0, high = standardAmount + 1;
        while (low + 1 < high)
        {
            int middle = low + (high - low) / 2;
            cohort.speciesAmount = middle;
            float food = ForecastV62Food(target, cohort, traits) - reservedFood;
            float adult = middle * LandLifeRules.AdultNeed(traits.s,
                traits.m, LandRules);
            if (food >= adult - Mathf.Max(0.01f, adult * 0.00001f))
                low = middle;
            else high = middle;
        }
        if (low < 10) return forecast;
        cohort.speciesAmount = low;
        float sourceValue = SustainableSurvivalValue(source, population, traits);
        float targetValue = SustainableSurvivalValue(target, cohort, traits,
            reservedFood);
        float difference = targetValue - sourceValue;
        float normalized = V62MobilityRules.NormalizedGain(difference, v62Mobility);
        if (targetValue - 0.5f < v62Mobility.minimumTargetGrowth ||
            normalized <= 0f) return forecast;
        float foodAtTarget = Mathf.Max(0f,
            ForecastV62Food(target, cohort, traits) - reservedFood);
        float adultNeed = low * LandLifeRules.AdultNeed(traits.s, traits.m,
            LandRules);
        float continuity = 1f - 0.2f * Mathf.Abs(source.temperature -
            target.temperature) / 100f - 0.2f * Mathf.Abs(source.humidity -
            target.humidity) / 100f;
        forecast.target = target;
        forecast.amount = low;
        forecast.elevationStep = step;
        forecast.sourceValue = sourceValue;
        forecast.targetValue = targetValue;
        forecast.normalizedGain = normalized;
        forecast.foodAttraction = Mathf.Clamp01(foodAtTarget /
            Mathf.Max(1f, foodAtTarget + adultNeed) * Mathf.Max(0f, continuity));
        forecast.capacityCorrection = (float)low / standardAmount;
        return forecast;
    }

    private float ForecastV62Food(BlockInfo target, PopulationData cohort,
        LandTraits traits) => cohort.trophicLevel == 0
        ? LandLifeRules.ForecastSustainablePlantIntake(target, cohort,
            traits.t, traits.h, traits.m, traits.s, traits.f, LandRules)
        : LandLifeRules.AvailableSustainablePreyFood(target, cohort, LandRules);

    private V62NeighborForecast BestV62Neighbor(BlockInfo source,
        PopulationData population, LandTraits traits)
    {
        var best = new V62NeighborForecast();
        int amount = StandardMigrationAmount(population.speciesAmount);
        if (amount < 10 || source == null || source.Neighbors == null)
            return best;
        foreach (BlockInfo neighbor in source.Neighbors)
        {
            V62NeighborForecast next = ForecastV62Neighbor(source, neighbor,
                population, traits, amount);
            if (!next.Valid) continue;
            float nextBonus = V62MobilityRules.OpportunityBonus(
                population.movementAbility, next.elevationStep,
                next.normalizedGain, v62Mobility);
            float bestBonus = best.Valid ? V62MobilityRules.OpportunityBonus(
                population.movementAbility, best.elevationStep,
                best.normalizedGain, v62Mobility) : 0f;
            if (!best.Valid || nextBonus > bestBonus ||
                nextBonus == bestBonus && string.CompareOrdinal(
                    StableTileKey(next.target), StableTileKey(best.target)) < 0)
                best = next;
        }
        return best;
    }

    private void AddV62MovementCandidates(List<MutationCandidate> choices,
        BlockInfo source, PopulationData population, LandTraits current,
        V62NeighborForecast best)
    {
        int movement = population.movementAbility;
        float here = best.sourceValue;
        float plus = float.NegativeInfinity;
        float minus = float.NegativeInfinity;
        if (movement < 100 &&
            !LeavesProtectedTarget(population, 2, movement + 1) &&
            InfluenceNet(population, 2, currentSimulationDay, true) >= 0f)
        {
            LandTraits changed = ChangedTrait(current, 2, movement + 1);
            plus = SustainableSurvivalValue(source, population, changed) - here +
                V62MobilityRules.OpportunityBonus(movement,
                    best.elevationStep, best.normalizedGain, v62Mobility);
        }
        if (movement > 0 &&
            !LeavesProtectedTarget(population, 2, movement - 1) &&
            InfluenceNet(population, 2, currentSimulationDay, true) <= 0f)
        {
            LandTraits changed = ChangedTrait(current, 2, movement - 1);
            minus = SustainableSurvivalValue(source, population, changed) - here -
                V62MobilityRules.OpportunityBonus(movement - 1,
                    best.elevationStep, best.normalizedGain, v62Mobility);
        }
        // A static opportunity cannot nominate opposite directions into the
        // same daily mutation pool. Both still compete with every other trait.
        int direction = plus >= minus ? 1 : -1;
        float gain = direction > 0 ? plus : minus;
        if (gain <= LandRules.mutationGainThreshold) return;
        AddMutationCandidate(choices, 2, direction,
            V62MobilityRules.CandidateWeight(gain, best.normalizedGain,
                v62Mobility) * selectionStrength, "v62_neighbor");
    }

    private float V62ReservedFood(BlockInfo target, PopulationData population,
        List<MovePlan> planned)
    {
        float reserved = 0f;
        foreach (MovePlan move in planned)
            if (move.target == target && move.population != null &&
                move.population.trophicLevel == population.trophicLevel)
                reserved += move.amount * LandLifeRules.AdultNeed(
                    move.population.size, move.population.movementAbility,
                    LandRules);
        return reserved;
    }

    private static bool V62HasNeighbor(BlockInfo source, BlockInfo target)
    {
        if (source?.Neighbors == null) return false;
        foreach (BlockInfo neighbor in source.Neighbors)
            if (neighbor == target) return true;
        return false;
    }
}
