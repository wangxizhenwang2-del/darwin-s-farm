// Darwin's Farm – movement decision v2 (candidate; not Unity validated).
// This pure C# decision layer changes no population, habitat, or physics.
using System;

namespace DarwinFarm.MigrationOpportunity
{
    public static class NeighborMovementPolicyV2
    {
        // All coefficients are candidate tunings. See the independent tests.
        public const double NeighborGateHalfWeight = 0.045;
        public const double NeighborGateMaximum = 0.25;
        public const double MigrationAdvantageMultiplier = 3.0;
        public const double RecoveryStartMovement = 30.0;
        public const double RecoveryGrowthTolerance = 0.001;

        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        private static double Clamp01(double v) { return Math.Max(0.0, Math.Min(1.0, v)); }

        public static double RequiredMovement(int elevationDifference)
        {
            if (elevationDifference == 0) return 31.0;
            if (elevationDifference == 1) return 61.0;
            return double.PositiveInfinity;
        }

        // Neighbor induction is a bridge to a *physical capability*, not a permanent
        // reward for living next to a superior region. Prevents +1 every day to 100.
        public static bool NeedsMovementToMigrate(OpportunityResult opportunity, double movement)
        {
            if (!opportunity.Eligible || !Finite(movement)) return false;
            return !opportunity.AlreadyEligibleForPhysicalMigration;
        }

        // Real lottery gate, applied BEFORE adding a movement candidate to the
        // single daily ±1 mutation lottery. Weight never implies guaranteed mutation.
        public static double NeighborGate(double weight)
        {
            if (!Finite(weight) || weight <= 0.0) return 0.0;
            return Math.Min(NeighborGateMaximum, weight / (weight + NeighborGateHalfWeight));
        }

        public static bool MayCrossEvolutionProtection(OpportunityResult opportunity,
            double movement, double nextMovement)
        {
            return NeedsMovementToMigrate(opportunity, movement) &&
                nextMovement == movement + 1.0 && nextMovement <= 100.0;
        }

        // Physical migration and induced movement must agree that the destination
        // is *truly* better, using the identical full counterfactual forecast.
        public static bool CanChooseDestination(OpportunityResult opportunity)
        {
            return opportunity.Eligible && opportunity.AlreadyEligibleForPhysicalMigration;
        }

        public static double MigrationChanceMultiplier(OpportunityResult opportunity)
        {
            return CanChooseDestination(opportunity)
                ? Clamp01(opportunity.AdvantageStrength * MigrationAdvantageMultiplier) : 0.0;
        }

        // When no worthwhile neighbor exists, expensive movement can decay slowly.
        // Never decrease if local survival would decline materially from a -1 step.
        // Gate uses maintenance savings only as a *directional tie-breaker*, not
        // a manufactured birth or ecological benefit.
        public static double RecoveryGate(double movement, double ecologicalMovementFloor,
            double localGrowthAfterMinusOneMinusCurrent, double adultMaintenanceSavings)
        {
            if (!Finite(movement) || !Finite(localGrowthAfterMinusOneMinusCurrent) ||
                !Finite(adultMaintenanceSavings) || movement <= Math.Max(RecoveryStartMovement, ecologicalMovementFloor) ||
                adultMaintenanceSavings <= 0.0 ||
                localGrowthAfterMinusOneMinusCurrent < -RecoveryGrowthTolerance)
                return 0.0;
            double x = Clamp01(movement / 100.0);
            return Math.Min(0.16, 0.025 + 0.125 * x * x);
        }

        // Do not schedule two opposing migrations of the same species on the
        // same undirected edge in the same simulation day.
        public static bool IsReciprocalEdge(string fromA, string toA,
            string fromB, string toB, bool sameSpecies)
        {
            return sameSpecies && fromA == toB && toA == fromB;
        }
    }
}
