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
    [Min(0f)] public float climateMismatchDeathRate = 0.01f;
    [Min(0f)] public float reproductionStressDeathRate = 0.012f;
    [Min(0f)] public float movementFoodCost = 0.4f;
    [Min(0f)] public float youngLinearCost = 0.4f;
    [Min(0f)] public float youngQuadraticCost = 0.045f;
    [Min(0f)] public float competitionPressure = 2f;
    [Range(0f, 1f)] public float oppositeStrategyCompetition = 0.5f;
    [Min(0f)] public float movementForagingGain = 0.002f;
    [Range(0f, 1f)] public float minimumPlantReserveFraction = 0.1f;
    [Range(0f, 1f)] public float dailyPlantStockDrawFraction = 0.1f;
    [Range(0f, 1f)] public float densityBirthDampingStart = 0.5f;
    [Min(0f)] public float baseCaptureChance = 0.5f;
    [Min(0f)] public float chaseScale = 40f;
    [Min(0f)] public float preyEnergyPerSize = 30f;
    [Range(0f, 1f)] public float reserveHuntFraction = 0.1f;
    [Min(0f)] public float reserveDays = 10f;
    [Range(0f, 1f)] public float splitMinimumFoodSatisfaction = 0.95f;
    [Range(0f, 1f)] public float splitMinimumRenewalPressure = 0.9f;
    [Min(0f)] public float splitMaximumRenewalLoad = 1.2f;
    [Range(0f, 1f)] public float splitHeadroomUse = 0.5f;
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

    public LandRuleTuning Sanitize()
    {
        birthRate = NonNegative(birthRate, 0.1f);
        starvationDeathRate = Unit(starvationDeathRate, 0.6f);
        coldDeathRate = NonNegative(coldDeathRate, 0.005f);
        climateMismatchDeathRate = NonNegative(climateMismatchDeathRate, 0.01f);
        reproductionStressDeathRate = NonNegative(reproductionStressDeathRate,
            0.012f);
        movementFoodCost = NonNegative(movementFoodCost, 0.4f);
        youngLinearCost = NonNegative(youngLinearCost, 0.4f);
        youngQuadraticCost = NonNegative(youngQuadraticCost, 0.045f);
        competitionPressure = NonNegative(competitionPressure, 2f);
        oppositeStrategyCompetition = Unit(oppositeStrategyCompetition, 0.5f);
        movementForagingGain = NonNegative(movementForagingGain, 0.002f);
        minimumPlantReserveFraction = Unit(minimumPlantReserveFraction, 0.1f);
        dailyPlantStockDrawFraction = Unit(dailyPlantStockDrawFraction, 0.1f);
        densityBirthDampingStart = Unit(densityBirthDampingStart, 0.5f);
        baseCaptureChance = NonNegative(baseCaptureChance, 0.5f);
        chaseScale = NonNegative(chaseScale, 40f);
        preyEnergyPerSize = NonNegative(preyEnergyPerSize, 30f);
        reserveHuntFraction = Unit(reserveHuntFraction, 0.1f);
        reserveDays = NonNegative(reserveDays, 10f);
        splitMinimumFoodSatisfaction = Unit(splitMinimumFoodSatisfaction, 0.95f);
        splitMinimumRenewalPressure = Unit(splitMinimumRenewalPressure, 0.9f);
        splitMaximumRenewalLoad = NonNegative(splitMaximumRenewalLoad, 1.2f);
        splitHeadroomUse = Unit(splitHeadroomUse, 0.5f);
        splitStockDays = NonNegative(splitStockDays, 3f);
        splitCooldownDays = Mathf.Max(1, splitCooldownDays);
        evolutionTodaySatisfaction = Unit(evolutionTodaySatisfaction, 0.9f);
        evolutionRenewalSatisfaction = Unit(evolutionRenewalSatisfaction, 0.8f);
        evolutionPreyRenewalCoverage = Unit(evolutionPreyRenewalCoverage, 0.8f);
        evolutionRequiredDays = Mathf.Max(1, evolutionRequiredDays);
        evolutionRequiredDaysAtFour = Mathf.Max(1, evolutionRequiredDaysAtFour);
        evolutionTraitTolerance = Mathf.Max(0, evolutionTraitTolerance);
        evolutionCooldownDays = Mathf.Max(0, evolutionCooldownDays);
        neighborAttractionThreshold = Unit(neighborAttractionThreshold, 0.2f);
        attractionMovementGain = NonNegative(attractionMovementGain, 2f);
        flatMigrationThreshold = Mathf.Max(0, flatMigrationThreshold);
        slopeMigrationThreshold = Mathf.Max(0, slopeMigrationThreshold);
        flatAttractionCeiling = Mathf.Clamp(flatAttractionCeiling, 0, 100);
        slopeAttractionCeiling = Mathf.Clamp(slopeAttractionCeiling, 0, 100);
        fourEntryMigrationFactor = Unit(fourEntryMigrationFactor, 0.5f);
        return this;
    }

    private static float NonNegative(float value, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(0f, value);
    private static float Unit(float value, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp01(value);
}
