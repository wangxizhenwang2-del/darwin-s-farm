using System.Collections.Generic;
using DarwinFarm.MigrationOpportunity;
using UnityEngine;

public partial class SimulationController
{
    private bool MovementFoodScore(BlockInfo tile, PopulationData population,
        out float score)
    {
        score = 0f;
        if (tile == null || tile.community == null) return false;
        float supply, demand = 0f;
        if (population.trophicLevel == 0)
        {
            supply = LandLifeRules.SustainablePlantSupply(tile, LandRules);
            foreach (PopulationData resident in tile.community)
                if (resident != null && resident.speciesAmount > 0 &&
                    resident.trophicLevel == 0 &&
                    resident.ecologicalNiche == EcologicalNiche.Land)
                    demand += resident.speciesAmount * LandLifeRules.AdultNeed(
                        resident.size, resident.movementAbility, LandRules);
        }
        else
        {
            bool knownPrey = false;
            foreach (PopulationData prey in tile.community)
            {
                if (prey == null || prey.speciesAmount <= 0 ||
                    !FoodWeb.CanEat(population, prey)) continue;
                if (prey.preyNetRenewalHistory == null ||
                    prey.preyNetRenewalHistory.Count == 0) return false;
                knownPrey = true;
            }
            if (!knownPrey) return false;
            // This supply has already paid competing hunters' shares. Only
            // the focal resident's adult demand remains to be subtracted.
            supply = LandLifeRules.AvailableSustainablePreyFood(tile,
                population, LandRules);
            if (tile.community.Contains(population))
                demand = population.speciesAmount * LandLifeRules.AdultNeed(
                    population.size, population.movementAbility, LandRules);
        }
        float free = Mathf.Clamp01((supply - demand) /
            Mathf.Max(.0001f, supply));
        score = LandLifeRules.Fitness(tile, population.fitTemperature,
            population.fitHumidity) * (.3f + .7f * free);
        return true;
    }

    private bool MovementFoodForMigrants(BlockInfo target,
        PopulationData population, int migrants)
    {
        float adult = migrants * LandLifeRules.AdultNeed(population.size,
            population.movementAbility, LandRules);
        if (population.trophicLevel > 0)
            return LandLifeRules.AvailableSustainablePreyFood(target,
                population, LandRules) >= adult;
        // Read-only cohort: the real allocator sees existing residents and
        // the prospective migrants. No live stock or random state is spent.
        var cohort = new PopulationData(false)
        {
            species = population.species,
            lineageName = population.lineageName,
            speciesId = population.speciesId,
            ecologicalNiche = EcologicalNiche.Land,
            trophicLevel = 0,
            speciesAmount = migrants,
            movementAbility = population.movementAbility,
            size = population.size,
            fertility = population.fertility,
            fitTemperature = population.fitTemperature,
            fitHumidity = population.fitHumidity
        };
        return LandLifeRules.ForecastSustainablePlantIntake(target, cohort,
            cohort.fitTemperature, cohort.fitHumidity, cohort.movementAbility,
            cohort.size, cohort.fertility, LandRules) >= adult;
    }

    private float MovementGuidanceProbability(BlockInfo source,
        PopulationData population, int day)
    {
        BlockInfo best = BestMovementNeighbor(source, population);
        if (best == null) return 0f;
        bool slope = source.elevation != best.elevation;
        int goal = slope ? 75 : 40;
        if (population.neighborMovementRounds == null)
            population.neighborMovementRounds = new List<NeighborMovementRound>();
        string edge = StableTileKey(source) + "->" + StableTileKey(best);
        NeighborMovementRound round = population.neighborMovementRounds.Find(
            value => value != null && value.edgeKey == edge &&
                value.originSpeciesKey == MovementSpeciesKey(population) &&
                value.sourceRevision == source.neighborOpportunityRevision &&
                value.targetRevision == best.neighborOpportunityRevision &&
                value.sourceElevation == source.elevation &&
                value.targetElevation == best.elevation);
        if (round == null)
        {
            round = new NeighborMovementRound
            {
                edgeKey = edge,
                startDay = day,
                sourceRevision = source.neighborOpportunityRevision,
                targetRevision = best.neighborOpportunityRevision,
                sourceElevation = source.elevation,
                targetElevation = best.elevation,
                anchorMovement = population.movementAbility,
                originSpeciesKey = MovementSpeciesKey(population)
            };
            population.neighborMovementRounds.Add(round);
        }
        if (day >= round.startDay + 180 || population.movementAbility >= goal)
            round.finished = true;
        return round.finished ? 0f : slope ? .85f : .65f;
    }

    private BlockInfo BestMovementNeighbor(BlockInfo source,
        PopulationData population)
    {
        if (!enableNeighborMovementFormula || source == null ||
            source.Neighbors == null || source.waterCoverage != WaterCoverage.Land ||
            population == null || population.speciesAmount <= 0 ||
            population.ecologicalNiche != EcologicalNiche.Land ||
            !MovementFoodScore(source, population, out float homeScore))
            return null;
        int migrants = StandardMigrationAmount(population.speciesAmount);
        if (migrants < 10) return null;
        BlockInfo best = null;
        float bestAdvantage = .10f;
        foreach (BlockInfo target in source.Neighbors)
        {
            if (target == null || target == source ||
                target.waterCoverage != WaterCoverage.Land ||
                target.community == null || target.Neighbors == null ||
                !blockInfos.Contains(target) || !V62HasNeighbor(target, source) ||
                Mathf.Abs(target.elevation - source.elevation) > 1 ||
                !LandMovementOpportunity.CanOfferNeighborSlot(
                    LivingPopulationCount(target),
                    HasSameSpeciesResident(target, population)) ||
                !MovementFoodScore(target, population, out float targetScore) ||
                !MovementFoodForMigrants(target, population, migrants))
                continue;
            float advantage = targetScore - homeScore;
            if (advantage > bestAdvantage || best == null &&
                advantage >= bestAdvantage ||
                advantage == bestAdvantage && best != null &&
                string.CompareOrdinal(StableTileKey(target),
                    StableTileKey(best)) < 0)
            {
                bestAdvantage = advantage;
                best = target;
            }
        }
        return best;
    }

    private void OfferNeighborMovement(BlockInfo block,
        PopulationData population, List<ChanceMutation> choices)
    {
        float probability = MovementGuidanceProbability(block, population,
            currentSimulationDay);
        if (probability > 0f &&
            !LeavesProtectedTarget(population, 2,
                population.movementAbility + 1))
            OfferChance(choices, population, 2, 1, probability,
                "neighbor_guidance");
    }

    private void MarkNeighborMovementHit(BlockInfo block,
        PopulationData population, int previousMovement)
    {
        BlockInfo best = BestMovementNeighbor(block, population);
        if (best == null || population.neighborMovementRounds == null) return;
        string edge = StableTileKey(block) + "->" + StableTileKey(best);
        NeighborMovementRound round = population.neighborMovementRounds.FindLast(
            value => value != null && value.edgeKey == edge &&
                value.originSpeciesKey == MovementSpeciesKey(population) &&
                value.sourceElevation == block.elevation &&
                value.targetElevation == best.elevation && !value.finished);
        if (round != null)
        {
            if (!round.guided) round.anchorMovement = previousMovement;
            round.guided = true;
        }
    }

    private void StopCliffRecovery(BlockInfo block, PopulationData population,
        string reason)
    {
        CliffMovementRecovery recovery = population.cliffMovementRecovery;
        if (recovery == null || !recovery.active) return;
        recovery.active = false;
        LogEcology(currentSimulationDay, block, population,
            "cliff_recovery", "event=stop reason=" + reason +
            " edge=" + recovery.edgeKey);
    }

    private static string MovementSpeciesKey(PopulationData population) =>
        population.species != null ? StableSpeciesKey(population.species) :
            population.lineageName ?? string.Empty;

    private void OfferCliffRecovery(BlockInfo block,
        PopulationData population, List<ChanceMutation> choices)
    {
        CliffMovementRecovery recovery = population.cliffMovementRecovery;
        if (recovery == null)
            population.cliffMovementRecovery = recovery = new CliffMovementRecovery();
        if (recovery.active && recovery.originSpeciesKey !=
            MovementSpeciesKey(population))
        { StopCliffRecovery(block, population, "species_changed"); return; }
        if (recovery.active && block.Neighbors != null)
            foreach (BlockInfo neighbor in block.Neighbors)
                if (neighbor != null && neighbor.waterCoverage ==
                    WaterCoverage.Land && V62HasNeighbor(neighbor, block) &&
                    Mathf.Abs(neighbor.elevation - block.elevation) <= 1 &&
                    recovery.edgeKey == StableTileKey(block) + "->" +
                        StableTileKey(neighbor))
                { StopCliffRecovery(block, population, "reconnected"); return; }
        if (MovementGuidanceProbability(block, population,
            currentSimulationDay) > 0f)
        {
            StopCliffRecovery(block, population, "attraction_restored");
            return;
        }
        if (!recovery.active && block.Neighbors != null &&
            population.neighborMovementRounds != null)
        {
            string home = StableTileKey(block) + "->";
            foreach (NeighborMovementRound round in population.neighborMovementRounds)
            {
                if (round == null || !round.guided || round.recoveryConsumed ||
                    round.originSpeciesKey != MovementSpeciesKey(population) ||
                    !round.edgeKey.StartsWith(home, System.StringComparison.Ordinal))
                    continue;
                BlockInfo former = null;
                foreach (BlockInfo neighbor in block.Neighbors)
                    if (neighbor != null && round.edgeKey == home +
                        StableTileKey(neighbor)) { former = neighbor; break; }
                if (former == null || former.elevation == round.targetElevation &&
                    block.elevation == round.sourceElevation) continue;
                round.recoveryConsumed = true;
                recovery.edgeKey = round.edgeKey;
                recovery.firstDay = currentSimulationDay;
                recovery.lastDay = currentSimulationDay + 119;
                recovery.anchorMovement = round.anchorMovement;
                recovery.originSpeciesKey = round.originSpeciesKey;
                recovery.active = true;
                LogEcology(currentSimulationDay, block, population,
                    "cliff_recovery", "event=start edge=" + round.edgeKey +
                    " anchor=" + recovery.anchorMovement);
                break;
            }
        }
        if (!recovery.active) return;
        if (currentSimulationDay > recovery.lastDay)
        { StopCliffRecovery(block, population, "expired"); return; }
        SpeciesData target = population.trackedEvolutionTarget;
        int goal = target != null && !HasLivingSpecies(block, target)
            ? target.baseMovementAbility : recovery.anchorMovement;
        if (population.movementAbility <= goal)
        { StopCliffRecovery(block, population, "goal_reached"); return; }
        OfferChance(choices, population, 2, -1, .80f,
            "cliff_recovery", true);
        LogEcology(currentSimulationDay, block, population,
            "cliff_recovery", "event=roll target=" + goal +
            " probability=0.8 edge=" + recovery.edgeKey);
    }
}
