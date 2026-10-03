using System.Collections.Generic;
using UnityEngine;

// 只管性状选择和小数变异余量。
public partial class SimulationController
{
    public const int MaxTrophicLevel = FoodWeb.MaxTrophicLevel;
    private const float EnvironmentalPressureRange = 30f;
    private const float MaximumSelectionPressure = 0.9f;
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
            if (!mutationEnabled)
            {
                population.mutationDaysElapsed = 0;
                population.previousMutationPopulation = population.speciesAmount;
                continue;
            }
            population.mutationDaysElapsed++;
            if (population.mutationDaysElapsed < Mathf.Max(1, mutationInterval)) continue;
            population.mutationDaysElapsed = 0;
            if (population.speciesAmount > 0 &&
                TryChooseMutation(block, population, out MutationDecision decision))
            {
                if (decisions == null) decisions = new List<MutationDecision>();
                decisions.Add(decision);
            }
            population.previousMutationPopulation = population.speciesAmount;
        }
        // 先看完整个群落，再动性状；这样先后顺序不会左右当天的选择。
        if (decisions == null) return;
        foreach (MutationDecision decision in decisions)
            ApplyMutationDecision(decision.population, decision.trait, decision.score);
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

    private bool TryChooseMutation(BlockInfo block, PopulationData population,
        out MutationDecision decision)
    {
        decision = new MutationDecision();
        if (mutationStep <= 0f || selectionStrength <= 0f) return false;

        float temperatureScore = Mathf.Clamp(
            (block.temperature - population.fitTemperature - population.temperatureMutationRemainder)
            / EnvironmentalPressureRange * selectionStrength,
            -MaximumSelectionPressure, MaximumSelectionPressure);
        float humidityScore = Mathf.Clamp(
            (block.humidity - population.fitHumidity - population.humidityMutationRemainder)
            / EnvironmentalPressureRange * selectionStrength,
            -MaximumSelectionPressure, MaximumSelectionPressure);
        MutationTrait selected = MutationTrait.Temperature;
        float score = temperatureScore;
        KeepStrongerMutation(ref selected, ref score, MutationTrait.Humidity, humidityScore);

        // 温湿度失配由玩家环境输入直接造成，先处理最强的一项。
        if (Mathf.Abs(score) >= MinimumMutationScore)
        {
            decision = new MutationDecision { population = population, trait = selected, score = score };
            return true;
        }

        score = 0f;
        float sustainableRatio = population.energySatisfactionToday;
        float actualRatio = population.actualEnergySatisfactionToday;
        float shortage = Mathf.Clamp01(1f - Mathf.Min(sustainableRatio, actualRatio));
        bool enoughEnergy = actualRatio >= 0.9f;
        foreach (PopulationData other in block.community)
        {
            if (other == population || other.speciesAmount <= 0 ||
                Mathf.Abs(other.trophicLevel - population.trophicLevel) != 1) continue;

            float movementGap = Mathf.Clamp((other.movementAbility - population.movementAbility)
                / EnvironmentalPressureRange, -1f, 1f);
            float sizeGap = Mathf.Clamp((other.size - population.size)
                / EnvironmentalPressureRange, -1f, 1f);
            if (movementGap > 0f && enoughEnergy &&
                sustainableRatio >= (population.energyNeed + mutationStep * MovementEnergyWeight)
                    / population.energyNeed)
                KeepStrongerMutation(ref selected, ref score, MutationTrait.Movement, movementGap);
            else if (movementGap < 0f && shortage > 0.1f)
                KeepStrongerMutation(ref selected, ref score, MutationTrait.Movement,
                    movementGap * shortage);

            if (sizeGap > 0f && enoughEnergy &&
                sustainableRatio >= (population.energyNeed + mutationStep * SizeEnergyWeight)
                    / population.energyNeed)
                KeepStrongerMutation(ref selected, ref score, MutationTrait.Size, sizeGap);
            else if (sizeGap < 0f && shortage > 0.1f)
                KeepStrongerMutation(ref selected, ref score, MutationTrait.Size, sizeGap * shortage);
        }

        if (actualRatio < 0.8f && sustainableRatio < 0.8f)
        {
            float currentFood = FoodWeb.FoodPerDemand(block, population.trophicLevel, population, reproductionScale);
            int lower = population.trophicLevel - 1;
            int higher = population.trophicLevel + 1;
            if (lower >= 0)
                KeepStrongerMutation(ref selected, ref score, MutationTrait.Trophic,
                    -FoodAdvantage(currentFood, FoodWeb.FoodPerDemand(block, lower, population, reproductionScale))
                    * shortage);
            if (higher <= MaxTrophicLevel)
                KeepStrongerMutation(ref selected, ref score, MutationTrait.Trophic,
                    FoodAdvantage(currentFood, FoodWeb.FoodPerDemand(block, higher, population, reproductionScale))
                    * shortage);
        }

        int populationChange = population.speciesAmount - population.previousMutationPopulation;
        // 小种群少一只的整数波动不应被当作持续衰退。
        float trendScore = Mathf.Clamp01(Mathf.Abs(populationChange)
            / Mathf.Max(15f, population.previousMutationPopulation * 0.2f));
        if (populationChange < 0 && enoughEnergy && sustainableRatio >= 0.9f)
            KeepStrongerMutation(ref selected, ref score, MutationTrait.Fertility,
                trendScore);
        else if (populationChange > 0 && shortage > 0.1f)
            KeepStrongerMutation(ref selected, ref score, MutationTrait.Fertility,
                -trendScore);

        score *= selectionStrength;
        if (Mathf.Abs(score) >= MinimumMutationScore)
        {
            decision = new MutationDecision { population = population, trait = selected, score = score };
            return true;
        }
        return false;
    }

    private static float FoodAdvantage(float current, float alternative)
    {
        return current + alternative > 0f
            ? Mathf.Max(0f, (alternative - current) / (current + alternative)) : 0f;
    }

    private static void KeepStrongerMutation(ref MutationTrait selected, ref float bestScore,
        MutationTrait candidate, float candidateScore)
    {
        if (Mathf.Abs(candidateScore) <= Mathf.Abs(bestScore)) return;
        selected = candidate;
        bestScore = candidateScore;
    }

    private void ApplyMutationDecision(PopulationData population, MutationTrait trait, float score)
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
                    1f, score, 1, 100); break;
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
