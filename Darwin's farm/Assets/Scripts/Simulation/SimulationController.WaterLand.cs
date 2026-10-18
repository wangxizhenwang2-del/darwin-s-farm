// Read-only preflight for a future water/land formal-evolution transaction.
// It validates the source population's one actual tile, never its water region.
public partial class SimulationController
{
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
