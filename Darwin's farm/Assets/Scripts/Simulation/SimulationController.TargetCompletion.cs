using System.Collections.Generic;
using UnityEngine;

public partial class SimulationController
{
    // Land and flight use five traits. A route involving water uses the three
    // traits checked by formal water evolution. Flight needs movement > 50,
    // rather than proximity to the flyer's authored movement value.
    private static bool IsTargetTraitRelevant(PopulationData population,
        SpeciesData target, int trait) => trait >= 2 ||
        population.ecologicalNiche != EcologicalNiche.Water &&
        target.baseEcologicalNiche != EcologicalNiche.Water;

    private bool IsTargetTraitReady(PopulationData population,
        SpeciesData target, int trait, int value)
    {
        if (target.baseEcologicalNiche == EcologicalNiche.Air && trait == 2)
            return value > 50;
        return Mathf.Abs(value - TargetTraitValue(target, trait)) <=
            LandRules.evolutionTraitTolerance;
    }

    private int FindFinalTraitGap(PopulationData population,
        SpeciesData target)
    {
        if (population == null || target == null) return -1;
        int gap = -1;
        for (int trait = 0; trait < 5; trait++)
        {
            if (!IsTargetTraitRelevant(population, target, trait) ||
                IsTargetTraitReady(population, target, trait,
                    TraitValue(population, trait))) continue;
            if (gap >= 0) return -1;
            gap = trait;
        }
        return gap;
    }

    private int TargetCompletionDistance(PopulationData population,
        SpeciesData target, int trait, out int direction)
    {
        int value = TraitValue(population, trait);
        if (target.baseEcologicalNiche == EcologicalNiche.Air && trait == 2)
        {
            direction = 1;
            return Mathf.Max(0, 51 - value);
        }
        int center = TargetTraitValue(target, trait);
        int tolerance = LandRules.evolutionTraitTolerance;
        if (value < center - tolerance)
        {
            direction = 1;
            return center - tolerance - value;
        }
        direction = -1;
        return Mathf.Max(0, value - center - tolerance);
    }

    private void OfferTargetCompletionAttraction(PopulationData population,
        List<ChanceMutation> choices)
    {
        SpeciesData target = population.trackedEvolutionTarget;
        int trait = FindFinalTraitGap(population, target);
        if (trait < 0) return;
        int distance = TargetCompletionDistance(population, target, trait,
            out int direction);
        if (distance <= 0) return;

        // While finishing the last trait, do not spend the mutation slot on
        // a conflicting candidate or push an already qualified trait out.
        choices.RemoveAll(option => option.trait == trait ||
            IsTargetTraitRelevant(population, target, option.trait) &&
            IsTargetTraitReady(population, target, option.trait,
                TraitValue(population, option.trait)) &&
            !IsTargetTraitReady(population, target, option.trait,
                TraitValue(population, option.trait) + option.direction));
        float probability = 0.35f + 0.45f *
            Mathf.Clamp01(distance / 30f);
        OfferChance(choices, population, trait, direction, probability,
            "target_completion", bypassProtection: true, weight: 4f);
    }

    private int TargetCompletionPaceDirection(PopulationData population,
        out int trait)
    {
        SpeciesData target = population.trackedEvolutionTarget;
        trait = FindFinalTraitGap(population, target);
        if (trait < 0)
        {
            population.movementPaceTarget = null;
            population.movementPaceTrait = -1;
            return 0;
        }
        int deadline = trait == 2 ? LandRules.movementTargetDeadlineDays :
            LandRules.targetCompletionDeadlineDays;
        if (deadline <= 0)
        {
            population.movementPaceTarget = null;
            population.movementPaceTrait = -1;
            return 0;
        }
        int distance = TargetCompletionDistance(population, target, trait,
            out int direction);
        if (distance <= 0) return 0;
        if (population.movementPaceTarget != target ||
            population.movementPaceTrait != trait ||
            currentSimulationDay < population.movementPaceStartDay)
        {
            population.movementPaceTarget = target;
            population.movementPaceTrait = trait;
            population.movementPaceStartDay = currentSimulationDay - 1;
            population.movementPaceInitialDistance = distance;
        }
        int elapsed = currentSimulationDay - population.movementPaceStartDay;
        int initial = population.movementPaceInitialDistance;
        int required = Mathf.CeilToInt((float)initial *
            Mathf.Min(elapsed, deadline) / deadline);
        int catchUp = Mathf.Min(distance,
            required - (initial - distance));
        return catchUp > 0 ? direction * catchUp : 0;
    }
}
