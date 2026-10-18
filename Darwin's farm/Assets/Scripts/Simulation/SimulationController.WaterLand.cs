using System;
using System.Collections.Generic;
using UnityEngine;

// Read-only preflight for a future water/land formal-evolution transaction.
// It validates the source population's one actual tile, never its water region.
public partial class SimulationController
{
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
