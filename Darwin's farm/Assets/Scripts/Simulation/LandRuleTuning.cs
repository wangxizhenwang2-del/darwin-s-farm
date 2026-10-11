using System;
using UnityEngine;

// Values used by the active land simulation. The existing export schema is
// deliberately independent of this model configuration.
[Serializable]
public sealed class LandRuleTuning
{
    [Min(0f)] public float birthRate = 0.1f;
    [Range(0f, 1f)] public float starvationDeathRate = 0.6f;
    [Min(0f)] public float coldDeathRate = 0.005f;
    [Min(0f)] public float movementFoodCost = 0.4f;
    [Min(0f)] public float youngLinearCost = 0.4f;
    [Min(0f)] public float youngQuadraticCost = 0.045f;
    [Min(0f)] public float competitionPressure = 2f;
    [Range(0f, 1f)] public float oppositeStrategyCompetition = 0.5f;
    [Min(0f)] public float movementForagingGain = 0.002f;
    [Min(0f)] public float baseCaptureChance = 0.5f;
    [Min(0f)] public float chaseScale = 40f;
    [Min(0f)] public float preyEnergyPerSize = 30f;
    [Range(0f, 1f)] public float reserveHuntFraction = 0.1f;
    [Min(0f)] public float reserveDays = 10f;
    [Range(0f, 1f)] public float splitMinimumFoodSatisfaction = 0.95f;
    [Range(0f, 1f)] public float splitMinimumRenewalPressure = 0.4f;
    [Min(0f)] public float splitMaximumRenewalLoad = 1.1f;
    [Min(0f)] public float splitStockDays = 3f;
    [Min(1)] public int splitCooldownDays = 300;
    [Range(0f, 1f)] public float evolutionTodaySatisfaction = 0.9f;
    [Range(0f, 1f)] public float evolutionRenewalSatisfaction = 0.8f;
    [Range(0f, 1f)] public float evolutionPreyRenewalCoverage = 0.8f;
    [Min(1)] public int evolutionRequiredDays = 5;
    [Min(1)] public int evolutionRequiredDaysAtFour = 10;
    [Min(0)] public int evolutionTraitTolerance = 5;
    [Min(0)] public int evolutionCooldownDays = 50;
    [Min(0f)] public float neighborAttractionThreshold = 0.2f;
    [Min(0f)] public float attractionMovementGain = 2f;
    [Min(0)] public int flatMigrationThreshold = 30;
    [Min(0)] public int slopeMigrationThreshold = 60;
    [Min(0)] public int flatAttractionCeiling = 40;
    [Min(0)] public int slopeAttractionCeiling = 70;
    [Range(0f, 1f)] public float fourEntryMigrationFactor = 0.5f;

    public LandRuleTuning Copy() => (LandRuleTuning)MemberwiseClone();
}
