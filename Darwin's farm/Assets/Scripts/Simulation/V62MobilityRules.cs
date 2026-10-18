using System;
using UnityEngine;

// Candidate coefficients from the independent V6.2 study. Runtime-only:
// no scene, prefab or saved tuning schema is changed before Unity acceptance.
internal struct V62MobilityParameters
{
    public float opportunityScale, gainTimeScale, minimumGain;
    public float minimumTargetGrowth, slopeBase, slopeMovementShare;
    public float weightScale, weightReference, weightBase, weightGain;
    public float minimumWeight, maximumWeight;

    internal static V62MobilityParameters Candidate => new V62MobilityParameters
    {
        opportunityScale = 3f,
        gainTimeScale = 0.04f,
        minimumGain = 0.002f,
        minimumTargetGrowth = -0.01f,
        slopeBase = 0.6f,
        slopeMovementShare = 0.4f,
        weightScale = 0.35f,
        weightReference = 0.003f,
        weightBase = 0.22f,
        weightGain = 0.28f,
        minimumWeight = 0.05f,
        maximumWeight = 2f
    };

    internal bool IsValid => opportunityScale >= 0f && gainTimeScale > 0f &&
        minimumGain >= 0f && slopeBase >= 0f && slopeMovementShare >= 0f &&
        weightScale >= 0f && weightReference > 0f && weightBase >= 0f &&
        weightGain >= 0f && minimumWeight >= 0f && maximumWeight >= minimumWeight &&
        Finite(opportunityScale) && Finite(gainTimeScale) &&
        Finite(minimumGain) && Finite(minimumTargetGrowth) &&
        Finite(slopeBase) && Finite(slopeMovementShare) &&
        Finite(weightScale) && Finite(weightReference) &&
        Finite(weightBase) && Finite(weightGain) &&
        Finite(minimumWeight) && Finite(maximumWeight);

    private static bool Finite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);
}

internal static class V62MobilityRules
{
    internal static float NormalizedGain(float difference,
        V62MobilityParameters settings) => difference <= settings.minimumGain ? 0f :
        1f - Mathf.Exp(-(difference - settings.minimumGain) /
            settings.gainTimeScale);

    internal static float OpportunityBonus(int movement, int elevationStep,
        float normalizedGain, V62MobilityParameters settings)
    {
        if (movement >= 100 || elevationStep > 1 || normalizedGain <= 0f)
            return 0f;
        float connection = elevationStep == 0 ? 1f :
            settings.slopeBase + settings.slopeMovementShare * movement / 100f;
        return settings.opportunityScale * normalizedGain * connection *
            (1f - movement / 100f) / 100f;
    }

    internal static float CandidateWeight(float utilityGain,
        float normalizedGain, V62MobilityParameters settings) =>
        Mathf.Clamp(settings.weightScale * utilityGain /
            settings.weightReference * (settings.weightBase +
            settings.weightGain * normalizedGain), settings.minimumWeight,
            settings.maximumWeight);
}
