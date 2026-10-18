using System;
using System.Collections.Generic;
using UnityEngine;

// Water/land formal evolution and per-cell water settlement.
public partial class SimulationController
{
    // Formal cross habitat evolution uses the same 10% body/payment transfer
    // as land evolution, while keeping its source and destination tiles distinct.
    private void RunCrossHabitatEvolution(int day)
    {
        if (!presetEvolutionEnabled || day <= 0) return;
        var sources = new List<(BlockInfo block, PopulationData population)>();
        foreach (BlockInfo block in blockInfos)
            if (block != null && block.community != null)
                foreach (PopulationData population in block.community)
                    if (population != null && population.species != null &&
                        population.speciesAmount > 0 &&
                        (population.ecologicalNiche == EcologicalNiche.Land ||
                         population.ecologicalNiche == EcologicalNiche.Water))
                        sources.Add((block, population));
        foreach (var entry in sources)
        {
            BlockInfo source = entry.block;
            PopulationData ancestor = entry.population;
            if (!source.community.Contains(ancestor) ||
                ancestor.speciesAmount <= 0) continue;
            List<SpeciesData> targets = ConnectedTargets(ancestor.species);
            targets.Sort(CompareSpeciesOrder);
            foreach (SpeciesData species in targets)
            {
                if (species == null ||
                    species.baseEcologicalNiche == EcologicalNiche.Air ||
                    ancestor.ecologicalNiche == EcologicalNiche.Land &&
                    species.baseEcologicalNiche == EcologicalNiche.Land) continue;
                EvolutionConversionRecord record = GetEvolutionRecord(ancestor,
                    species, true);
                BlockInfo destination = null;
                int amount = 0;
                float payment = 0f, excess = 0f;
                foreach (BlockInfo candidate in blockInfos)
                    if (CanCrossHabitatEvolve(source, ancestor, candidate,
                            species, day, out int planned, out float paid,
                            out float surplus) &&
                        (destination == null || string.CompareOrdinal(
                            StableTileKey(candidate), StableTileKey(destination)) < 0))
                    {
                        destination = candidate;
                        amount = planned;
                        payment = paid;
                        excess = surplus;
                    }
                if (destination == null)
                { record.effectiveDays = 0; continue; }
                if (record.effectiveDays < int.MaxValue) record.effectiveDays++;
                int required = LivingPopulationCount(destination) == 4
                    ? LandRules.evolutionRequiredDaysAtFour
                    : LandRules.evolutionRequiredDays;
                if (record.effectiveDays < required) continue;
                if (!CanCrossHabitatEvolve(source, ancestor, destination,
                    species, day, out amount, out payment, out excess))
                { record.effectiveDays = 0; continue; }
                CommitPresetSpecies(source, destination, ancestor, species,
                    day, amount, payment, excess);
                break;
            }
        }
    }

    private bool CanCrossHabitatEvolve(BlockInfo source,
        PopulationData ancestor, BlockInfo targetBlock, SpeciesData target,
        int day, out int amount, out float payment, out float excess)
    {
        amount = 0; payment = excess = 0f;
        if (InspectCrossHabitatEvolutionRoute(source, ancestor, targetBlock,
                target) != "ROUTE_OK" ||
            day < ancestor.nextEvolutionDay ||
            !EachTraitWithinTolerance(ancestor, target) ||
            ancestor.speciesAmount < 2 ||
            ancestor.energySatisfactionToday <
                LandRules.evolutionTodaySatisfaction)
            return false;
        int requested = Mathf.Min(ancestor.speciesAmount - 1,
            Mathf.FloorToInt(ancestor.speciesAmount * .1f + .5f));
        float stock = Mathf.Max(0f, source.plantBiomass);
        float capacity = Mathf.Max(0f, source.maxPlantBiomass);
        float spendable = stock;
        if (LandRules.useCompensatedEvolutionFoodGate)
            spendable -= Mathf.Min(spendable * .5f,
                capacity * LandRules.minimumPlantReserveFraction);
        float sourceBody = ancestor.bodyBiomassInitialized
            ? ancestor.bodyBiomass : ancestor.speciesAmount *
                LandLifeRules.BodyMass(ancestor.size, LandRules);
        float sourcePerHead = sourceBody / ancestor.speciesAmount;
        float targetPerHead = LandLifeRules.BodyMass(target.baseSize, LandRules);
        float extra = Mathf.Max(0f, targetPerHead - sourcePerHead);
        amount = requested;
        if (spendable + .01f < amount * extra) return false;
        if (amount < 1 || target.trophicLevel == 2 && amount < 10)
            return false;
        payment = amount * extra;
        excess = amount * Mathf.Max(0f, sourcePerHead - targetPerHead);
        return CrossTargetFoodSupported(targetBlock, target, amount);
    }

    private bool CrossTargetFoodSupported(BlockInfo targetBlock,
        SpeciesData target, int amount)
    {
        return CrossTargetFoodSupportedHere(targetBlock, target, amount);
    }

    private bool CrossTargetFoodSupportedHere(BlockInfo block,
        SpeciesData target, int amount)
    {
        PopulationData candidate = TargetPopulation(target, amount);
        if (target.trophicLevel > 0)
        {
            StockFoodForecast stock = ForecastStockPredatorFood(block, null,
                candidate, amount);
            if (stock.legalPreyStock <= 0 || stock.coverage <
                LandRules.evolutionTodaySatisfaction) return false;
            if (target.trophicLevel == 2)
            {
                float need = amount * LandLifeRules.AdultNeed(target.baseSize,
                    target.baseMovementAbility, LandRules);
                return LandLifeRules.AvailableSustainablePreyFood(block,
                    candidate, LandRules) >= need * 1.08f;
            }
            return true;
        }
        LandLifeRules.ForecastEvolutionPlantSupport(block, null, candidate,
            0, LandRules, out float demand, out float today,
            out float renewable);
        return demand > 0f &&
            today / demand >= LandRules.evolutionTodaySatisfaction &&
            renewable / demand >= LandRules.evolutionRenewalSatisfaction;
    }

    // Each physical water cell owns its complete food web and resource ledger.
    private void SimulateWaterCells(int day)
    {
        foreach (BlockInfo cell in blockInfos)
        {
            if (cell == null || !HabitatTopology.IsWater(cell) ||
                cell.community == null) continue;
            bool occupied = false;
            foreach (PopulationData population in cell.community)
                if (population != null && population.speciesAmount > 0 &&
                    population.ecologicalNiche == EcologicalNiche.Water)
                { InitializeMutationState(population); occupied = true; }
            if (!occupied) continue;
            cell.consumedBiomassToday = 0f;
            cell.accountedBiomassSinkToday = 0f;
            LandLifeRules.Simulate(cell, LandRules, EcologicalNiche.Water,
                growPlants: false);
            EvolveCommunityNewRules(cell);
            LandLifeRules.RefreshEnvironmentMetrics(cell, LandRules);
        }
    }

    public string InspectCrossHabitatEvolutionRoute(BlockInfo source,
        PopulationData ancestor, BlockInfo target, SpeciesData targetSpecies)
    {
        if (!EnableWaterEcology) return "WATER_ECOLOGY_OFF";
        if (source == null || ancestor == null || source.community == null ||
            !blockInfos.Contains(source) || !source.community.Contains(ancestor) ||
            ancestor.speciesAmount <= 0 || ancestor.species == null)
            return "INVALID_SOURCE";
        if (target == null || targetSpecies == null || target.community == null ||
            !blockInfos.Contains(target)) return "INVALID_TARGET";
        bool waterToWater = ancestor.ecologicalNiche == EcologicalNiche.Water &&
            targetSpecies.baseEcologicalNiche == EcologicalNiche.Water;
        if (waterToWater ? source != target :
            !WaterLandBoundary.IsLocalCrossHabitatEdge(source, target))
            return "NO_LOCAL_EDGE";
        bool fromWater = source.waterCoverage != WaterCoverage.Land;
        if (ancestor.ecologicalNiche != (fromWater ? EcologicalNiche.Water :
                EcologicalNiche.Land) ||
            targetSpecies.baseEcologicalNiche != (waterToWater ? EcologicalNiche.Water :
                fromWater ? EcologicalNiche.Land :
                EcologicalNiche.Water)) return "INVALID_NICHE";
        if (!ConnectedTargets(ancestor.species).Contains(targetSpecies))
            return "NO_GRAPH_EDGE";
        if (HasLivingSpecies(target, targetSpecies))
            return "TARGET_ALREADY_PRESENT";
        if (LivingPopulationCount(target) >= 5) return "TARGET_FULL";
        return "ROUTE_OK";
    }
}
