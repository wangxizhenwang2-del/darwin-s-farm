using System.Collections.Generic;
using UnityEngine;

public partial class SimulationController
{
    // Movement has one direction in the shared daily mutation draw. A reachable
    // neighbor or predator supplies an upward reason; lost terrain capacity,
    // isolation, or a nearly qualified target supplies a downward reason.
    private void OfferUnifiedMovement(BlockInfo block, PopulationData population,
        List<ChanceMutation> choices)
    {
        int movement = population.movementAbility;
        float neighborChance = BestNeighborMovementChance(block, population,
            out bool hasReachableNeighbor, out int neighborCeiling);
        float predatorChance = PredatorMovementChance(block, population);
        float upward = Mathf.Max(neighborChance, predatorChance);

        SpeciesData target = population.trackedEvolutionTarget;
        bool targetPull = TargetWantsLowerMovement(population, target);
        bool targetPullUp = OtherTargetTraitsReady(population, target) &&
            population.movementAbility < target.baseMovementAbility -
                LandRules.evolutionTraitTolerance;
        int ecologicalFloor = neighborCeiling > 0 ? neighborCeiling : 30;
        bool recoil = movement > ecologicalFloor && predatorChance <= 0f;
        float downward = 0f;
        if ((targetPull || recoil) &&
            movement > 0 && !LeavesProtectedTarget(population, 2, movement - 1))
        {
            int floor = targetPull
                ? target.baseMovementAbility + LandRules.evolutionTraitTolerance
                : ecologicalFloor;
            if (targetPull || recoil && movement > floor)
            {
                LandTraits current = ReadLandTraits(population);
                LandTraits reduced = ChangedTrait(current, 2, movement - 1);
                float perHeadChange = (ForecastLandGrowth(block, population,
                    reduced) - ForecastLandGrowth(block, population, current)) /
                    Mathf.Max(1, population.speciesAmount);
                // Maintenance savings remain meaningful when abundant food
                // makes the predicted birth/death count exactly unchanged.
                if (perHeadChange >= -0.001f &&
                    LandLifeRules.AdultNeed(current.s, movement - 1, LandRules) <
                    LandLifeRules.AdultNeed(current.s, movement, LandRules))
                    downward = 0.8f;
            }
        }

        // Once other required traits are ready, the tracked evolution target
        // wins a conflict with a neighboring land attraction. Otherwise the
        // stronger ecological pressure supplies the sole movement candidate.
        if (targetPull && downward > 0f || downward > upward)
            OfferChance(choices, population, 2, -1, downward,
                targetPull ? "target_movement_recovery" :
                    hasReachableNeighbor ? "terrain_recoil" : "island_recovery",
                weight: 2f);
        else if (targetPullUp && movement < 100 &&
            !LeavesProtectedTarget(population, 2, movement + 1))
            OfferChance(choices, population, 2, 1, Mathf.Max(0.8f, upward),
                "target_movement_attraction", weight: 2f);
        else if (upward > 0f && movement < 100 &&
            !LeavesProtectedTarget(population, 2, movement + 1))
            OfferChance(choices, population, 2, 1, upward,
                predatorChance >= neighborChance ? "predator_movement" :
                    "neighbor_movement");
    }

    private float BestNeighborMovementChance(BlockInfo source,
        PopulationData population, out bool hasReachableNeighbor,
        out int bestCeiling)
    {
        hasReachableNeighbor = false;
        bestCeiling = 0;
        if (source?.Neighbors == null || population == null) return 0f;
        foreach (BlockInfo neighbor in source.Neighbors)
            if (IsReachableMovementNeighbor(source, neighbor, population,
                out _)) hasReachableNeighbor = true;
        if (!enableNeighborMovementFormula || population.speciesAmount < 95)
            return 0f;
        int migrants = StandardMigrationAmount(population.speciesAmount);
        if (migrants < 10) return 0f;
        float bestChance = 0f;
        foreach (BlockInfo neighbor in source.Neighbors)
        {
            if (!IsReachableMovementNeighbor(source, neighbor, population,
                out int elevationStep)) continue;
            hasReachableNeighbor = true;
            int ceiling = elevationStep == 0 ? 40 : 80;
            if (HasSameSpeciesResident(neighbor, population) ||
                !LandMovementOpportunity.CanOfferNeighborSlot(
                    LivingPopulationCount(neighbor), false) ||
                !MovementFoodScore(neighbor, population, out float targetScore) ||
                targetScore < 0.6f ||
                !MovementFoodForMigrants(neighbor, population, migrants)) continue;

            float quality;
            if (population.ecologicalNiche == EcologicalNiche.Land &&
                population.trophicLevel == 0)
            {
                float stock = neighbor.maxPlantBiomass > 0f
                    ? Mathf.Clamp01(neighbor.plantBiomass /
                        neighbor.maxPlantBiomass) : 0f;
                quality = LandLifeRules.Fitness(neighbor, population) * stock;
            }
            else quality = Mathf.Clamp01(targetScore);
            if (quality < 0.5f) continue;
            bestCeiling = Mathf.Max(bestCeiling, ceiling);
            if (population.movementAbility >= ceiling) continue;
            float chance = (elevationStep == 0 ? 0.65f : 0.85f) * quality;
            if (chance > bestChance)
            {
                bestChance = chance;
            }
        }
        return bestChance;
    }

    private bool IsReachableMovementNeighbor(BlockInfo source,
        BlockInfo neighbor, PopulationData population, out int elevationStep)
    {
        elevationStep = 2;
        if (neighbor == null || neighbor == source ||
            neighbor.community == null || neighbor.Neighbors == null ||
            !blockInfos.Contains(neighbor) || !V62HasNeighbor(neighbor, source))
            return false;
        elevationStep = Mathf.Abs(neighbor.elevation - source.elevation);
        if (elevationStep > 1) return false;
        return population.ecologicalNiche == EcologicalNiche.Land
            ? neighbor.waterCoverage == WaterCoverage.Land
            : population.ecologicalNiche == EcologicalNiche.Water &&
                HabitatTopology.ChannelsConnect(source, neighbor);
    }

    private static float PredatorMovementChance(BlockInfo block,
        PopulationData population)
    {
        if (block?.community == null) return 0f;
        float pressure = 0f;
        foreach (PopulationData other in block.community)
        {
            if (other == null || other == population ||
                other.speciesAmount <= 0) continue;
            if (FoodWeb.CanEat(other, population))
                pressure = Mathf.Max(pressure, Mathf.Clamp01(
                    (other.movementAbility - population.movementAbility + 20f)
                    / 40f) * Mathf.Clamp01((float)other.speciesAmount /
                    Mathf.Max(1, population.speciesAmount)));
            else if (FoodWeb.CanEat(population, other))
                pressure = Mathf.Max(pressure, Mathf.Clamp01(
                    (other.movementAbility - population.movementAbility + 20f)
                    / 40f) * Mathf.Clamp01((float)other.speciesAmount /
                    Mathf.Max(1, population.speciesAmount)));
        }
        return 0.5f * pressure;
    }

    private bool TargetWantsLowerMovement(PopulationData population,
        SpeciesData target)
    {
        return OtherTargetTraitsReady(population, target) &&
            population.movementAbility > target.baseMovementAbility +
                LandRules.evolutionTraitTolerance;
    }

    private bool OtherTargetTraitsReady(PopulationData population,
        SpeciesData target)
    {
        if (target == null || target.baseEcologicalNiche ==
            EcologicalNiche.Air) return false;
        int tolerance = LandRules.evolutionTraitTolerance;
        bool sizeReady = Mathf.Abs(population.size - target.baseSize) <= tolerance;
        bool fertilityReady = Mathf.Abs(population.fertility -
            target.baseFertility) <= tolerance;
        if (population.ecologicalNiche == EcologicalNiche.Water ||
            target.baseEcologicalNiche == EcologicalNiche.Water)
            return sizeReady && fertilityReady;
        int otherReady = (sizeReady ? 1 : 0) + (fertilityReady ? 1 : 0) +
            (Mathf.Abs(population.fitTemperature -
                target.baseFitTemperature) <= tolerance ? 1 : 0) +
            (Mathf.Abs(population.fitHumidity -
                target.baseFitHumidity) <= tolerance ? 1 : 0);
        return otherReady >= 3;
    }

    // A probability of remainingDistance / deadline cannot guarantee a finish.
    // Reserve the mutation slot when the integer linear schedule falls behind.
    // A rare distance larger than the easy deadline uses a multi-point step.
    private int MovementPaceDirection(PopulationData population)
    {
        SpeciesData target = population.trackedEvolutionTarget;
        int deadline = LandRules.movementTargetDeadlineDays;
        if (deadline <= 0 || !OtherTargetTraitsReady(population, target))
        {
            population.movementPaceTarget = null;
            return 0;
        }
        int tolerance = LandRules.evolutionTraitTolerance;
        int lower = target.baseMovementAbility - tolerance;
        int upper = target.baseMovementAbility + tolerance;
        int distance = population.movementAbility < lower
            ? lower - population.movementAbility
            : population.movementAbility > upper
                ? population.movementAbility - upper : 0;
        if (distance == 0)
        {
            population.movementPaceTarget = null;
            return 0;
        }
        if (population.movementPaceTarget != target ||
            currentSimulationDay < population.movementPaceStartDay)
        {
            population.movementPaceTarget = target;
            population.movementPaceStartDay = currentSimulationDay - 1;
            population.movementPaceInitialDistance = distance;
        }
        int elapsed = currentSimulationDay - population.movementPaceStartDay;
        int initial = population.movementPaceInitialDistance;
        int requiredProgress = Mathf.CeilToInt((float)initial *
            Mathf.Min(elapsed, deadline) / deadline);
        int catchUp = Mathf.Min(distance,
            requiredProgress - (initial - distance));
        return catchUp > 0
            ? population.movementAbility < lower ? catchUp : -catchUp : 0;
    }
}
