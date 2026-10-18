using System;
using System.Collections.Generic;
using UnityEngine;

// Water/land formal evolution and basin settlement. Routes use the source
// population's one actual tile, never its entire water region.
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
                if (species == null || species.baseEcologicalNiche ==
                    ancestor.ecologicalNiche ||
                    species.baseEcologicalNiche == EcologicalNiche.Air) continue;
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
        float stock = 0f, capacity = 0f;
        if (HabitatTopology.IsWater(source))
        {
            WaterRegion region = BuildWaterRegions().GetRegion(source);
            if (region == null) return false;
            foreach (BlockInfo cell in region.Members)
            { stock += Mathf.Max(0f, cell.plantBiomass);
              capacity += Mathf.Max(0f, cell.maxPlantBiomass); }
        }
        else
        { stock = Mathf.Max(0f, source.plantBiomass);
          capacity = Mathf.Max(0f, source.maxPlantBiomass); }
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
        amount = Mathf.Min(requested, extra > 0f
            ? Mathf.FloorToInt(spendable / extra) : requested);
        if (amount < 1 || target.trophicLevel == 2 && amount < 10)
            return false;
        payment = amount * extra;
        excess = amount * Mathf.Max(0f, sourcePerHead - targetPerHead);
        return CrossTargetFoodSupported(targetBlock, target, amount);
    }

    private bool CrossTargetFoodSupported(BlockInfo targetBlock,
        SpeciesData target, int amount)
    {
        if (!HabitatTopology.IsWater(targetBlock))
            return CrossTargetFoodSupportedHere(targetBlock, target, amount);
        WaterRegion region = BuildWaterRegions().GetRegion(targetBlock);
        if (region == null) return false;
        List<PopulationData> oldCommunity = targetBlock.community;
        float oldStock = targetBlock.plantBiomass;
        float oldCapacity = targetBlock.maxPlantBiomass;
        int oldRecovery = targetBlock.habitatRecovery;
        float oldConsumed = targetBlock.consumedBiomassToday;
        int oldTemperature = targetBlock.temperature;
        int oldHumidity = targetBlock.humidity;
        var basin = new List<PopulationData>();
        float stock = 0f, capacity = 0f;
        int recovery = 0, temperature = 0, humidity = 0;
        foreach (BlockInfo cell in region.Members)
        {
            stock += Mathf.Max(0f, cell.plantBiomass);
            capacity += Mathf.Max(0f, cell.maxPlantBiomass);
            recovery += Mathf.Max(0, cell.habitatRecovery);
            temperature += cell.temperature;
            humidity += cell.humidity;
            if (cell.community != null)
                foreach (PopulationData p in cell.community)
                    if (p != null && p.speciesAmount > 0 &&
                        p.ecologicalNiche == EcologicalNiche.Water) basin.Add(p);
        }
        try
        {
            targetBlock.community = basin;
            targetBlock.plantBiomass = stock;
            targetBlock.maxPlantBiomass = capacity;
            targetBlock.habitatRecovery = recovery;
            targetBlock.consumedBiomassToday = 0f;
            targetBlock.temperature = Mathf.RoundToInt(temperature /
                (float)region.Members.Count);
            targetBlock.humidity = Mathf.RoundToInt(humidity /
                (float)region.Members.Count);
            return CrossTargetFoodSupportedHere(targetBlock, target, amount);
        }
        finally
        {
            targetBlock.community = oldCommunity;
            targetBlock.plantBiomass = oldStock;
            targetBlock.maxPlantBiomass = oldCapacity;
            targetBlock.habitatRecovery = oldRecovery;
            targetBlock.consumedBiomassToday = oldConsumed;
            targetBlock.temperature = oldTemperature;
            targetBlock.humidity = oldHumidity;
        }
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

    // Settle every connected, equal-height water component once. The first
    // physical cell temporarily presents the basin to the existing life
    // allocator; population objects remain owned by their original cells.
    private void SimulateWaterBasins(int day)
    {
        foreach (WaterRegion region in BuildWaterRegions().Regions)
        {
            var cells = new List<BlockInfo>(region.Members);
            cells.Sort((left, right) =>
            {
                MapTileInstance a = left.GetComponent<MapTileInstance>();
                MapTileInstance b = right.GetComponent<MapTileInstance>();
                if (a != null && b != null)
                {
                    int x = a.Coordinate.x.CompareTo(b.Coordinate.x);
                    if (x != 0) return x;
                    int y = a.Coordinate.y.CompareTo(b.Coordinate.y);
                    if (y != 0) return y;
                }
                return string.CompareOrdinal(left.name, right.name);
            });
            if (cells.Count == 0) continue;
            var community = new List<PopulationData>();
            float stock = 0f, capacity = 0f, growth = 0f;
            int recovery = 0;
            int temperature = 0, humidity = 0;
            foreach (BlockInfo cell in cells)
            {
                stock += Mathf.Max(0f, cell.plantBiomass);
                capacity += Mathf.Max(0f, cell.maxPlantBiomass);
                growth += Mathf.Max(0f, cell.plantGrowthToday);
                recovery += Mathf.Max(0, cell.habitatRecovery);
                temperature += cell.temperature;
                humidity += cell.humidity;
                cell.consumedBiomassToday = 0f;
                cell.accountedBiomassSinkToday = 0f;
                if (cell.community == null) continue;
                foreach (PopulationData population in cell.community)
                    if (population != null && population.speciesAmount > 0 &&
                        population.ecologicalNiche == EcologicalNiche.Water)
                    {
                        InitializeMutationState(population);
                        community.Add(population);
                    }
            }
            if (community.Count == 0) continue;
            BlockInfo basin = cells[0];
            List<PopulationData> originalCommunity = basin.community;
            float originalStock = basin.plantBiomass;
            float originalCapacity = basin.maxPlantBiomass;
            int originalRecovery = basin.habitatRecovery;
            float originalGrowth = basin.plantGrowthToday;
            int originalTemperature = basin.temperature;
            int originalHumidity = basin.humidity;
            bool physicalStockRestored = false;
            try
            {
                basin.community = community;
                basin.plantBiomass = stock;
                basin.maxPlantBiomass = capacity;
                basin.habitatRecovery = recovery;
                basin.plantGrowthToday = growth;
                basin.temperature = Mathf.RoundToInt(temperature /
                    (float)cells.Count);
                basin.humidity = Mathf.RoundToInt(humidity /
                    (float)cells.Count);
                LandLifeRules.Simulate(basin, LandRules, EcologicalNiche.Water,
                    growPlants: false);
                LandLifeRules.RefreshEnvironmentMetrics(basin, LandRules);
                float consumed = basin.consumedBiomassToday;
                if (consumed < -0.01f || consumed > stock + 0.01f)
                    throw new InvalidOperationException("Water basin debit exceeds stock");
                basin.plantBiomass = originalStock;
                physicalStockRestored = true;
                float remaining = Mathf.Max(0f, consumed);
                foreach (BlockInfo cell in cells)
                {
                    float paid = Mathf.Min(cell.plantBiomass, remaining);
                    cell.plantBiomass -= paid;
                    cell.consumedBiomassToday = paid;
                    remaining -= paid;
                }
                if (remaining > 0.1f)
                    throw new InvalidOperationException("Water basin debit did not settle");
            }
            finally
            {
                basin.community = originalCommunity;
                // The representative cell was temporarily assigned aggregate
                // S/R. Its physical S was already debited in the loop above.
                if (!physicalStockRestored)
                    basin.plantBiomass = originalStock;
                basin.maxPlantBiomass = originalCapacity;
                basin.habitatRecovery = originalRecovery;
                basin.plantGrowthToday = originalGrowth;
                basin.temperature = originalTemperature;
                basin.humidity = originalHumidity;
            }
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
        if (!WaterLandBoundary.IsLocalCrossHabitatEdge(source, target))
            return "NO_LOCAL_EDGE";
        bool fromWater = source.waterCoverage != WaterCoverage.Land;
        if (ancestor.ecologicalNiche != (fromWater ? EcologicalNiche.Water :
                EcologicalNiche.Land) ||
            targetSpecies.baseEcologicalNiche != (fromWater ? EcologicalNiche.Land :
                EcologicalNiche.Water)) return "INVALID_NICHE";
        if (!ConnectedTargets(ancestor.species).Contains(targetSpecies))
            return "NO_GRAPH_EDGE";
        if (HasLivingSpecies(target, targetSpecies))
            return "TARGET_ALREADY_PRESENT";
        if (LivingPopulationCount(target) >= 5) return "TARGET_FULL";
        return "ROUTE_OK";
    }
}
