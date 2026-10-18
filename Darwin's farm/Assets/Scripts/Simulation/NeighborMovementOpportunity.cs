// Candidate implementation. Must be wired into the project's actual LandTraits/Migration code.
// Does NOT grant mutations or migrate anything by itself. Pure, side-effect-free calculation.
// Compatible with Unity C# and standalone .NET projects (uses only System/Collections.Generic).
using System;
using System.Collections.Generic;

namespace DarwinFarm.MigrationOpportunity
{
    public enum OpportunityRejectReason
    {
        None,
        NotAdjacentLand,
        CliffDisconnection,
        PopulationBelowMinimum,
        InvalidCounterfactualMigrants,
        MissingFullEcologyForecast,
        InvalidForecast,
        UnsustainableDestination,
        InsufficientGrowthAdvantage,
        AtMaxMovement,
        NoPositiveWeight
    }

    public struct OpportunityForecast
    {
        // These inputs must be derived from actual, read-only, counterfactual simulation.
        public string DestinationKey;
        public bool IsSharedEdge;
        public bool SourceIsLand;
        public bool DestinationIsLand;
        public int AbsoluteElevationDifference;
        public int SourcePopulation;
        public int Migrants;
        public double Movement; // current movement Bar, [0,100]

        // g = expected per-capita daily population growth (N_after-N_before)/N_before.
        // SourceGrowth: migrant lineage if it stayed in source, with existing competitors/predators.
        // DestinationGrowth: migrants in target, with existing residents/predators and additional
        // competitor/predator consumption, after moving a real 10-100 animals.
        // DestinationLongRunGrowth: expected per-capita daily growth over a sustainable window.
        public double SourceGrowth;
        public double DestinationGrowth;
        public double DestinationLongRunGrowth;
        public bool IncludesExistingCompetitorsAndPredators;
        public bool IncludesMigrantsInTargetResourceSharing;
    }

    public struct OpportunityResult
    {
        public bool Eligible;
        public OpportunityRejectReason Rejection;
        public string DestinationKey;
        public double GrowthAdvantage;
        public double AdvantageStrength;
        public double SlopeFactor;
        public double MutationWeightBonus; // NOT an independent mutation probability!
        public bool AlreadyEligibleForPhysicalMigration;
    }

    public static class NeighborMovementOpportunity
    {
        // Candidate values, not Unity-accepted/tuned constants.
        public const double MinimumAdvantagePerDay = 0.002;
        public const double MinimumLongRunDestinationGrowthPerDay = -0.01;
        public const double AdvantageScalePerDay = 0.04;
        public const double ProposedLambda = 3.0;
        public const double FlatPhysicalMigrationThreshold = 30.0;
        public const double SlopePhysicalMigrationThreshold = 60.0;

        private static bool Finite(double x) { return !(double.IsNaN(x) || double.IsInfinity(x)); }
        private static double Clamp01(double x) { return Math.Max(0.0, Math.Min(1.0, x)); }

        // Does not check physical movement threshold. That gate belongs ONLY to real migration.
        public static OpportunityResult Evaluate(OpportunityForecast input, double lambda = ProposedLambda)
        {
            OpportunityResult r = new OpportunityResult();
            r.DestinationKey = input.DestinationKey;
            r.Rejection = OpportunityRejectReason.None;
            if (!input.IsSharedEdge || !input.SourceIsLand || !input.DestinationIsLand)
                return Reject(r, OpportunityRejectReason.NotAdjacentLand);
            if (input.AbsoluteElevationDifference < 0 || input.AbsoluteElevationDifference >= 2)
                return Reject(r, OpportunityRejectReason.CliffDisconnection);
            if (input.SourcePopulation < 10)
                return Reject(r, OpportunityRejectReason.PopulationBelowMinimum);
            if (input.Migrants < 10 || input.Migrants > 100 || input.Migrants > input.SourcePopulation)
                return Reject(r, OpportunityRejectReason.InvalidCounterfactualMigrants);
            if (!input.IncludesExistingCompetitorsAndPredators || !input.IncludesMigrantsInTargetResourceSharing)
                return Reject(r, OpportunityRejectReason.MissingFullEcologyForecast);
            if (!Finite(input.Movement) || input.Movement < 0.0 || input.Movement > 100.0 ||
                !Finite(input.SourceGrowth) || !Finite(input.DestinationGrowth) ||
                !Finite(input.DestinationLongRunGrowth) || !Finite(lambda) || lambda < 0.0)
                return Reject(r, OpportunityRejectReason.InvalidForecast);
            if (input.DestinationLongRunGrowth < MinimumLongRunDestinationGrowthPerDay)
                return Reject(r, OpportunityRejectReason.UnsustainableDestination);

            r.GrowthAdvantage = input.DestinationGrowth - input.SourceGrowth;
            if (r.GrowthAdvantage <= MinimumAdvantagePerDay)
                return Reject(r, OpportunityRejectReason.InsufficientGrowthAdvantage);
            // Full movement cannot be induced further, but may still migrate.
            // Do not reject the genuine destination opportunity at movement=100.
            bool atMaxMovement = input.Movement >= 100.0;

            r.AdvantageStrength = 1.0 - Math.Exp(
                -Math.Max(0.0, r.GrowthAdvantage - MinimumAdvantagePerDay) / AdvantageScalePerDay);
            r.SlopeFactor = input.AbsoluteElevationDifference == 0
                ? 1.0 : 0.6 + 0.4 * Clamp01(input.Movement / 100.0);
            r.MutationWeightBonus = lambda * r.AdvantageStrength * r.SlopeFactor
                * (1.0 - Clamp01(input.Movement / 100.0)) / 100.0;
            r.AlreadyEligibleForPhysicalMigration = input.AbsoluteElevationDifference == 0
                ? input.Movement > FlatPhysicalMigrationThreshold
                : input.Movement > SlopePhysicalMigrationThreshold;
            if (r.MutationWeightBonus <= 0.0 && !atMaxMovement)
                return Reject(r, OpportunityRejectReason.NoPositiveWeight);
            r.Eligible = true;
            return r;
        }

        public static OpportunityResult Best(IList<OpportunityForecast> forecasts, double lambda = ProposedLambda)
        {
            OpportunityResult best = new OpportunityResult();
            best.Rejection = OpportunityRejectReason.NoPositiveWeight;
            if (forecasts == null) return best;
            for (int i = 0; i < forecasts.Count; i++)
            {
                OpportunityResult candidate = Evaluate(forecasts[i], lambda);
                if (candidate.Eligible && (!best.Eligible || candidate.MutationWeightBonus > best.MutationWeightBonus))
                    best = candidate;
            }
            return best; // use best neighbor, do NOT sum multiple destinations as separate daily tickets
        }

        private static OpportunityResult Reject(OpportunityResult r, OpportunityRejectReason reason)
        {
            r.Eligible = false;
            r.Rejection = reason;
            r.MutationWeightBonus = 0.0;
            return r;
        }
    }
}
