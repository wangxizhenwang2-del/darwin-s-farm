using System;
using UnityEngine;

// Values used by the active land simulation. The existing export schema is
// deliberately independent of this model configuration.
[Serializable]
public sealed class LandRuleTuning
{
    [SerializeField] private int ruleVersion = 3;
    [Min(0f)] public float birthRate = 0.1f;
    [Range(0f, 1f)] public float starvationDeathRate = 0.6f;
    [Min(0f)] public float coldDeathRate = 0.005f;
    [Min(0f)] public float climateMismatchDeathRate = 0.01f;
    [Min(0f)] public float reproductionStressPerBirth = 1.2f;
    [Min(0f)] public float baselineAdultDeathRate = 0.008f;
    [Min(0f)] public float smallBodyFrailtyRate = 0.5f;
    [Min(1f)] public float adultSizeMortalityScale = 20f;
    [Range(0f, 1f)] public float forageAccessLossAtZero = 0.08f;
    [Min(1f)] public float forageSizeScale = 20f;
    [Min(1f)] public float forageMovementScale = 10f;
    [Min(0f)] public float movementFoodCost = 0.4f;
    [Min(0f)] public float smallBodyMaintenance = 200f;
    [Min(0f)] public float youngLinearCost = 0.4f;
    [Min(0f)] public float youngQuadraticCost = 0.02f;
    [Min(0f)] public float competitionPressure = 2f;
    [Range(0f, 1f)] public float oppositeStrategyCompetition = 0.5f;
    [Range(0.1f, 0.9f)] public float smallPlantNicheShare = 0.7f;
    [Range(0.1f, 1f)] public float unspecializedPlantEfficiency = 0.7f;
    [Min(1f)] public float smallPlantSizeScale = 20f;
    [Min(1f)] public float largePlantSizeScale = 25f;
    [Min(0f)] public float movementForagingGain = 0.002f;
    [Range(0f, 1f)] public float minimumPlantReserveFraction = 0.1f;
    [Range(0f, 1f)] public float dailyPlantStockDrawFraction = 0.1f;
    [Range(0f, 1f)] public float densityBirthDampingStart = 0.85f;
    [Min(0f)] public float baseCaptureChance = 0.5f;
    [Min(0f)] public float chaseScale = 40f;
    [Min(0f)] public float preyEnergyPerSize = 30f;
    [Range(0f, 1f)] public float reserveHuntFraction = 0.1f;
    [Min(0f)] public float reserveDays = 10f;
    [Range(0f, 1f)] public float splitMinimumFoodSatisfaction = 0.95f;
    [Range(0f, 1f)] public float splitMinimumRenewalPressure = 0.95f;
    [Min(0f)] public float splitMaximumRenewalLoad = 1.2f;
    [Range(0f, 1f)] public float splitHeadroomUse = 0.5f;
    [Range(0f, 1f)] public float splitFounderFraction = 0.05f;
    [Min(0f)] public float splitStockDays = 3f;
    [Min(1)] public int splitCooldownDays = 300;
    [Min(1)] public int sameSpeciesMergeDays = 100;
    [Min(0)] public int postMergeSplitCooldownDays = 200;
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
    [Min(0f)] public float mutationGainThreshold = 0.00002f;
    [Min(0.0000001f)] public float mutationGainDailyScale = 0.0002f;

    public LandRuleTuning Copy() => (LandRuleTuning)MemberwiseClone();

    public LandRuleTuning Sanitize()
    {
        // Existing Unity scenes carry version 0 and do not contain the new
        // physiology, plant-layer or selection fields. Give them the tested
        // defaults once, while preserving deliberate zeroes in version 2.
        if (ruleVersion < 2)
        {
            reproductionStressPerBirth = 1.2f;
            baselineAdultDeathRate = 0.008f;
            smallBodyFrailtyRate = 0.5f;
            adultSizeMortalityScale = 20f;
            forageAccessLossAtZero = 0.08f;
            forageSizeScale = 20f;
            forageMovementScale = 10f;
            smallBodyMaintenance = 200f;
            youngQuadraticCost = 0.02f;
            smallPlantNicheShare = 0.7f;
            unspecializedPlantEfficiency = 0.7f;
            smallPlantSizeScale = 20f;
            largePlantSizeScale = 25f;
            splitFounderFraction = 0.05f;
            densityBirthDampingStart = 0.85f;
            mutationGainThreshold = 0.00002f;
            mutationGainDailyScale = 0.0002f;
            ruleVersion = 2;
        }
        if (ruleVersion < 3)
        {
            splitMinimumRenewalPressure = 0.95f;
            sameSpeciesMergeDays = 100;
            postMergeSplitCooldownDays = 200;
            ruleVersion = 3;
        }
        birthRate = NonNegative(birthRate, 0.1f);
        starvationDeathRate = Unit(starvationDeathRate, 0.6f);
        coldDeathRate = NonNegative(coldDeathRate, 0.005f);
        climateMismatchDeathRate = NonNegative(climateMismatchDeathRate, 0.01f);
        reproductionStressPerBirth = NonNegative(reproductionStressPerBirth,
            1.2f);
        baselineAdultDeathRate = NonNegative(baselineAdultDeathRate, 0.008f);
        smallBodyFrailtyRate = NonNegative(smallBodyFrailtyRate, 0.5f);
        adultSizeMortalityScale = Mathf.Max(1f,
            NonNegative(adultSizeMortalityScale, 20f));
        forageAccessLossAtZero = Unit(forageAccessLossAtZero, 0.08f);
        forageSizeScale = Mathf.Max(1f, NonNegative(forageSizeScale, 20f));
        forageMovementScale = Mathf.Max(1f, NonNegative(forageMovementScale, 10f));
        movementFoodCost = NonNegative(movementFoodCost, 0.4f);
        smallBodyMaintenance = NonNegative(smallBodyMaintenance, 200f);
        youngLinearCost = NonNegative(youngLinearCost, 0.4f);
        youngQuadraticCost = NonNegative(youngQuadraticCost, 0.02f);
        competitionPressure = NonNegative(competitionPressure, 2f);
        oppositeStrategyCompetition = Unit(oppositeStrategyCompetition, 0.5f);
        smallPlantNicheShare = Mathf.Clamp(Unit(smallPlantNicheShare, 0.7f), 0.1f, 0.9f);
        unspecializedPlantEfficiency = Mathf.Clamp(Unit(unspecializedPlantEfficiency, 0.7f), 0.1f, 1f);
        smallPlantSizeScale = Mathf.Max(1f, NonNegative(smallPlantSizeScale, 20f));
        largePlantSizeScale = Mathf.Max(1f, NonNegative(largePlantSizeScale, 25f));
        movementForagingGain = NonNegative(movementForagingGain, 0.002f);
        minimumPlantReserveFraction = Unit(minimumPlantReserveFraction, 0.1f);
        dailyPlantStockDrawFraction = Unit(dailyPlantStockDrawFraction, 0.1f);
        densityBirthDampingStart = Unit(densityBirthDampingStart, 0.85f);
        baseCaptureChance = NonNegative(baseCaptureChance, 0.5f);
        chaseScale = NonNegative(chaseScale, 40f);
        preyEnergyPerSize = NonNegative(preyEnergyPerSize, 30f);
        reserveHuntFraction = Unit(reserveHuntFraction, 0.1f);
        reserveDays = NonNegative(reserveDays, 10f);
        splitMinimumFoodSatisfaction = Unit(splitMinimumFoodSatisfaction, 0.95f);
        splitMinimumRenewalPressure = Unit(splitMinimumRenewalPressure, 0.95f);
        splitMaximumRenewalLoad = NonNegative(splitMaximumRenewalLoad, 1.2f);
        splitHeadroomUse = Unit(splitHeadroomUse, 0.5f);
        splitFounderFraction = Unit(splitFounderFraction, 0.05f);
        splitStockDays = NonNegative(splitStockDays, 3f);
        splitCooldownDays = Mathf.Max(1, splitCooldownDays);
        sameSpeciesMergeDays = Mathf.Max(1, sameSpeciesMergeDays);
        postMergeSplitCooldownDays = Mathf.Max(0, postMergeSplitCooldownDays);
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
        mutationGainThreshold = NonNegative(mutationGainThreshold, 0.00002f);
        mutationGainDailyScale = Mathf.Max(0.0000001f,
            NonNegative(mutationGainDailyScale, 0.0002f));
        return this;
    }

    private static float NonNegative(float value, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(0f, value);
    private static float Unit(float value, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp01(value);
}
