using System.Collections.Generic;
using UnityEngine;

// 只管性状选择和小数变异余量。
public partial class SimulationController
{
    public const int MaxTrophicLevel = FoodWeb.MaxTrophicLevel;
    private const float MinimumMutationScore = 0.15f;

    [Header("Mutation")]
    [SerializeField] private bool mutationEnabled = true;
    [Min(1)] [SerializeField] private int mutationInterval = 7;
    [Min(0f)] [SerializeField] private float mutationStep = 2f;
    [Min(0f)] [SerializeField] private float selectionStrength = 1f;

    private enum MutationTrait { Temperature, Humidity, Movement, Size, Fertility, Trophic }

    private struct MutationDecision
    {
        public PopulationData population;
        public MutationTrait trait;
        public float score;
        public float expectedGain;
        public bool nicheSwitch;
    }

    public void SetMutationParameters(int interval, float step, float strength)
    {
        mutationInterval = Mathf.Max(1, interval);
        mutationStep = Mathf.Max(0f, step);
        selectionStrength = Mathf.Max(0f, strength);
    }

    public void SetMutationEnabled(bool enabled)
    {
        mutationEnabled = enabled;
    }

    private void RetryMutationSoon(BlockInfo block)
    {
        if (block.community == null) return;
        foreach (PopulationData population in block.community)
            if (population != null)
                population.mutationDaysElapsed = Mathf.Max(1, mutationInterval) - 1;
    }

    private void EvolveCommunity(BlockInfo block)
    {
        List<MutationDecision> decisions = null;
        foreach (PopulationData population in block.community)
        {
            population.evolutionCheckReady = false;
            if (!mutationEnabled)
            {
                population.mutationDaysElapsed = 0;
                population.previousMutationPopulation = population.speciesAmount;
                continue;
            }
            population.mutationDaysElapsed++;
            if (population.mutationDaysElapsed < Mathf.Max(1, mutationInterval)) continue;
            population.mutationDaysElapsed = 0;
            population.evolutionCheckReady = population.speciesAmount > 0;
            if (population.speciesAmount > 0 &&
                TryChooseForecastMutation(block, population, out MutationDecision decision))
            {
                if (decisions == null) decisions = new List<MutationDecision>();
                decisions.Add(decision);
            }
            population.previousMutationPopulation = population.speciesAmount;
        }
        // 先看完整个群落，再动性状；这样先后顺序不会左右当天的选择。
        if (decisions == null) return;
        foreach (MutationDecision decision in decisions)
            ApplyMutationDecision(decision.population, decision.trait, decision.score,
                decision.nicheSwitch);
    }

    private static void InitializeMutationState(PopulationData population)
    {
        population.size = Mathf.Clamp(population.size, 1, 100);
        if (!population.trophicLevelInitialized)
        {
            population.trophicLevel = population.species == null ? 0 :
                Mathf.Clamp(population.species.trophicLevel, 0, MaxTrophicLevel);
            population.trophicLevelInitialized = true;
        }
        population.trophicLevel = Mathf.Clamp(population.trophicLevel, 0, MaxTrophicLevel);
        if (!population.mutationPopulationInitialized)
        {
            population.previousMutationPopulation = population.speciesAmount;
            population.mutationPopulationInitialized = true;
        }
    }

    private void ApplyMutationDecision(PopulationData population, MutationTrait trait,
        float score, bool nicheSwitch)
    {
        switch (trait)
        {
            case MutationTrait.Temperature:
                ChangeTrait(ref population.fitTemperature, ref population.temperatureMutationRemainder,
                    1f, score, 0, 100); break;
            case MutationTrait.Humidity:
                ChangeTrait(ref population.fitHumidity, ref population.humidityMutationRemainder,
                    1f, score, 0, 100); break;
            case MutationTrait.Movement:
                ChangeTrait(ref population.movementAbility, ref population.movementMutationRemainder,
                    1f, score, 0, 100); break;
            case MutationTrait.Size:
                ChangeTrait(ref population.size, ref population.sizeMutationRemainder,
                    nicheSwitch ? 1f : 0.1f, score, 1, 100); break;
            case MutationTrait.Fertility:
                ChangeTrait(ref population.fertility, ref population.fertilityMutationRemainder,
                    0.5f, score, 0, 100); break;
            case MutationTrait.Trophic:
                ChangeTrait(ref population.trophicLevel, ref population.trophicMutationRemainder,
                    0.2f, score, Mathf.Max(0, population.trophicLevel - 1),
                    Mathf.Min(MaxTrophicLevel, population.trophicLevel + 1)); break;
        }
    }

    private void ChangeTrait(ref int trait, ref float remainder, float multiplier,
        float score, int minimum, int maximum)
    {
        float change = Mathf.Sign(score) * mutationStep * multiplier * Mathf.Clamp01(Mathf.Abs(score));
        float value = Mathf.Clamp(trait + remainder + change, minimum, maximum);
        trait = Mathf.RoundToInt(value);
        remainder = value - trait;
    }
}
