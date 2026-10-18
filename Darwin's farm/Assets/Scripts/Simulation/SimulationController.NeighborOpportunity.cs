using System.Collections.Generic;
using System.Globalization;
using DarwinFarm.MigrationOpportunity;
using UnityEngine;

public partial class SimulationController
{
    [SerializeField, Min(0f)] private float neighborOpportunityLambda = 3f;
    public float NeighborOpportunityLambda => neighborOpportunityLambda;

    // Only herbivores have the complete sustainable resident/competitor forecast.
    // L1+ remain BLOCKED; their physical migration keeps its existing branch.
    private OpportunityResult EvaluateRealNeighborOpportunity(BlockInfo source,
        BlockInfo target, PopulationData population, LandTraits traits,
        int migrants, bool emitTrace)
    {
        bool reciprocal = target != null && target.Neighbors != null &&
            V62HasNeighbor(target, source);
        int step = target == null || source == null ? 2 :
            Mathf.Abs(target.elevation - source.elevation);
        bool slot = target != null && target.community != null &&
            LandMovementOpportunity.CanOfferNeighborSlot(
                LivingPopulationCount(target), HasSameSpeciesResident(target, population));
        bool forecastAvailable = slot && population != null &&
            population.trophicLevel == 0;
        var forecast = new OpportunityForecast
        {
            DestinationKey = target == null ? "" : StableTileKey(target),
            IsSharedEdge = reciprocal && blockInfos.Contains(target),
            SourceIsLand = source != null && source.waterCoverage == WaterCoverage.Land,
            DestinationIsLand = target != null && target.waterCoverage == WaterCoverage.Land,
            AbsoluteElevationDifference = step,
            SourcePopulation = population == null ? 0 : population.speciesAmount,
            Migrants = migrants,
            Movement = traits.m,
            SourceGrowth = source != null && population != null
                ? SustainableSurvivalValue(source, population, traits) - 0.5f : 0f,
            IncludesExistingCompetitorsAndPredators = forecastAvailable,
            IncludesMigrantsInTargetResourceSharing = forecastAvailable
        };
        if (forecastAvailable && reciprocal && step <= 1 && migrants >= 10 &&
            forecast.SourceIsLand && forecast.DestinationIsLand)
        {
            PopulationData cohort = PopulationTransfer.CopyForMove(population, migrants);
            float growth = SustainableSurvivalValue(target, cohort, traits) - 0.5f;
            forecast.DestinationGrowth = growth;
            forecast.DestinationLongRunGrowth = growth;
        }
        OpportunityResult result = NeighborMovementOpportunity.Evaluate(
            forecast, neighborOpportunityLambda);
        if (emitTrace && (OnEcologyEvent != null || EnableEcologyConsoleLogs))
            LogEcology(currentSimulationDay, source, population, "neighbor_growth_opportunity",
                "source=" + StableTileKey(source) + " target=" + forecast.DestinationKey +
                " n=" + forecast.SourcePopulation + " migrants=" + migrants +
                " m=" + traits.m +
                " gSource=" + forecast.SourceGrowth.ToString("R", CultureInfo.InvariantCulture) +
                " gTarget=" + forecast.DestinationGrowth.ToString("R", CultureInfo.InvariantCulture) +
                " gLong=" + forecast.DestinationLongRunGrowth.ToString("R", CultureInfo.InvariantCulture) +
                " delta=" + result.GrowthAdvantage.ToString("R", CultureInfo.InvariantCulture) +
                " D=" + result.AdvantageStrength.ToString("R", CultureInfo.InvariantCulture) +
                " c=" + result.SlopeFactor.ToString("R", CultureInfo.InvariantCulture) +
                " lambda=" + neighborOpportunityLambda.ToString("R", CultureInfo.InvariantCulture) +
                " bonus=" + result.MutationWeightBonus.ToString("R", CultureInfo.InvariantCulture) +
                " eligible=" + result.Eligible + " reason=" +
                (slot ? result.Rejection.ToString() : "NoDestinationSlot"));
        return result;
    }

    private OpportunityResult BestRealNeighborOpportunity(BlockInfo source,
        PopulationData population, LandTraits traits,
        out float ecologicalMovementFloor)
    {
        ecologicalMovementFloor = 30f;
        var best = new OpportunityResult { Rejection = OpportunityRejectReason.NoPositiveWeight };
        if (source == null || source.Neighbors == null || population == null) return best;
        int migrants = Mathf.Min(population.speciesAmount,
            Mathf.Max(10, StandardMigrationAmount(population.speciesAmount)));
        foreach (BlockInfo target in source.Neighbors)
        {
            if (target == null || target == source) continue;
            OpportunityResult result = EvaluateRealNeighborOpportunity(source,
                target, population, traits, migrants, true);
            if (result.Eligible)
                ecologicalMovementFloor = Mathf.Max(ecologicalMovementFloor,
                    Mathf.Abs(target.elevation - source.elevation) == 0 ? 31f : 61f);
            // A sufficiently mobile population has no further reason to grow
            // movement because of *this* neighbor. Other, steeper neighbors may.
            if (NeighborMovementPolicyV2.NeedsMovementToMigrate(result, traits.m) &&
                (!best.Eligible || result.MutationWeightBonus > best.MutationWeightBonus))
                best = result;
        }
        return best;
    }
}
