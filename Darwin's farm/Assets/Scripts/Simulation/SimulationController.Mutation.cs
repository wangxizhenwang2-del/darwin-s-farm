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
            / EnvironmentalPressureRange,
            -MaximumSelectionPressure, MaximumSelectionPressure);
        float humidityScore = Mathf.Clamp(
            (block.humidity - population.fitHumidity - population.humidityMutationRemainder)
            / EnvironmentalPressureRange,
            -MaximumSelectionPressure, MaximumSelectionPressure);
        MutationTrait selected = MutationTrait.Temperature;
        float score = temperatureScore;
        KeepStrongerMutation(ref selected, ref score, MutationTrait.Humidity, humidityScore);

        // 环境与生态压力一起比较，避免温湿度适应把换赛道永久排在后面。
        float sustainableRatio = population.energySatisfactionToday;
        float actualRatio = population.actualEnergySatisfactionToday;
        float shortage = Mathf.Clamp01(1f - Mathf.Min(sustainableRatio, actualRatio));
        bool enoughEnergy = actualRatio >= 0.9f;
        int populationChange = population.speciesAmount - population.previousMutationPopulation;
        // 小种群少一只的整数波动不应被当作持续衰退。
        float trendScore = Mathf.Clamp01(Mathf.Abs(populationChange)
            / Mathf.Max(15f, population.previousMutationPopulation * 0.2f));
        if (population.trophicLevel == 0 && enoughEnergy &&
            population.fertility < 100 && block.habitatRecovery > block.consumedBiomassToday)
        {
            float surplus = (block.habitatRecovery - block.consumedBiomassToday)
                / Mathf.Max(1f, block.habitatRecovery);
            bool hunted = false;
            foreach (PopulationData other in block.community)
                if (other.speciesAmount > 0 && other.trophicLevel == 1)
                    hunted = true;
            if (hunted && population.carryingCapacity > 0f)
            {
                float room = Mathf.Clamp01(1f - population.speciesAmount
                    / population.carryingCapacity);
                KeepStrongerMutation(ref selected, ref score, MutationTrait.Fertility,
                    room * (0.35f + 0.7f * surplus));
            }
            if (populationChange < 0)
                KeepStrongerMutation(ref selected, ref score, MutationTrait.Fertility,
                    Mathf.Clamp01(trendScore * (1f + surplus)));
        }
        if (population.trophicLevel == 0)
        {
            bool strongerSameTrackCompetitor = false;
            bool decliningMinority = false;
            float ownForaging = Mathf.Clamp01(population.environmentalFitness / 100f)
                * (0.75f + population.movementAbility * 0.0025f);
            foreach (PopulationData other in block.community)
                if (other != population && other.speciesAmount > 0 &&
                    other.trophicLevel == 0 &&
                    (other.size < 8) == (population.size < 8) &&
                    Mathf.Clamp01(other.environmentalFitness / 100f)
                        * (0.75f + other.movementAbility * 0.0025f)
                        > ownForaging + 0.001f)
                {
                    strongerSameTrackCompetitor = true;
                    if (population.speciesAmount < other.speciesAmount &&
                        population.speciesAmount < population.previousMutationPopulation)
                        decliningMinority = true;
                }
            if (strongerSameTrackCompetitor)
            {
                int targetSize = population.size < 8 ? 10 : 6;
                float alternative = FoodWeb.ProjectedHerbivoreCapacity(block,
                    population, targetSize);
                float gain = (alternative - population.carryingCapacity)
                    / Mathf.Max(1f, alternative);
                float nicheScore = Mathf.Clamp(gain, 0f, 0.6f);
                // 已持续失势的少数种群可提前换赛道，不必等原赛道 K 降到更低。
                if (decliningMinority && alternative > population.speciesAmount * 0.5f)
                    nicheScore = Mathf.Max(nicheScore, 0.38f);
                if (nicheScore > 0f)
                    KeepStrongerMutation(ref selected, ref score, MutationTrait.Size,
                        Mathf.Sign(targetSize - population.size) * nicheScore);
            }
        }
        foreach (PopulationData other in block.community)
        {
            if (other == population || other.speciesAmount <= 0 ||
                Mathf.Abs(other.trophicLevel - population.trophicLevel) != 1) continue;

            float movementGap = Mathf.Clamp((other.movementAbility - population.movementAbility)
                / EnvironmentalPressureRange, -1f, 1f);
            float sizeGap = Mathf.Clamp((other.size - population.size)
                / EnvironmentalPressureRange, -1f, 1f);
            if (movementGap > 0f && enoughEnergy &&
                sustainableRatio >= CalculateEnergyNeed(population.size,
                    population.movementAbility + mutationStep)
                    / population.energyNeed &&
                MovementBenefitExceedsEnergyCost(population, other))
                KeepStrongerMutation(ref selected, ref score, MutationTrait.Movement,
                    movementGap * 0.55f);
            else if (movementGap < 0f && shortage > 0.1f &&
                MovementBenefitExceedsEnergyCost(population, other, false))
                KeepStrongerMutation(ref selected, ref score, MutationTrait.Movement,
                    movementGap * shortage);

            if (sizeGap > 0f && enoughEnergy &&
                sustainableRatio >= (population.energyNeed + mutationStep * SizeEnergyWeight)
                    / population.energyNeed &&
                SizeBenefitExceedsEnergyCost(population, other))
                KeepStrongerMutation(ref selected, ref score, MutationTrait.Size, sizeGap);
            else if (sizeGap < 0f && shortage > 0.1f)
                KeepStrongerMutation(ref selected, ref score, MutationTrait.Size, sizeGap * shortage);
        }

        if (population.trophicLevel > 0 && enoughEnergy &&
            sustainableRatio >= 0.8f && sustainableRatio < 1.25f)
        {
            foreach (PopulationData prey in block.community)
            {
                if (prey.speciesAmount <= 0 ||
                    prey.trophicLevel != population.trophicLevel - 1 ||
                    !MovementBenefitExceedsEnergyCost(population, prey)) continue;
                float before = FoodWeb.CaptureEfficiency(
                    population.movementAbility, population.size, prey);
                float after = Mathf.Min(1.5f,
                    before + Mathf.Min(mutationStep,
                        100f - population.movementAbility) / 100f);
                float gain = (after - before) / before
                    - (CalculateEnergyNeed(population.size,
                        population.movementAbility + mutationStep)
                        - population.energyNeed) / population.energyNeed;
                KeepStrongerMutation(ref selected, ref score,
                    MutationTrait.Movement, Mathf.Clamp01(gain * 24f));
            }
        }

        // 营养级整群跳变会抹掉捕食链；食性仍可由界面直接调整。
        if (population.trophicLevel > 0 && enoughEnergy &&
            population.carryingCapacity > population.speciesAmount &&
            population.fertility < 100)
            KeepStrongerMutation(ref selected, ref score, MutationTrait.Fertility,
                Mathf.Clamp01(1f - population.speciesAmount
                    / population.carryingCapacity));
        else if (populationChange < 0 && enoughEnergy && sustainableRatio >= 0.9f &&
            population.trophicLevel > 0)
            KeepStrongerMutation(ref selected, ref score, MutationTrait.Fertility,
                trendScore);
        else if (populationChange > 0 && shortage > 0.1f)
            KeepStrongerMutation(ref selected, ref score, MutationTrait.Fertility,
                -trendScore);

        score *= selectionStrength;
        if (selected == MutationTrait.Temperature || selected == MutationTrait.Humidity)
            score = Mathf.Clamp(score, -MaximumSelectionPressure,
                MaximumSelectionPressure);
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

    private bool MovementBenefitExceedsEnergyCost(PopulationData population,
        PopulationData opponent, bool increase = true)
    {
        float step = increase ? Mathf.Min(mutationStep, 100f - population.movementAbility)
            : Mathf.Min(mutationStep, population.movementAbility);
        if (step <= 0f || population.energyNeed <= 0f) return false;
        bool predator = population.trophicLevel > opponent.trophicLevel;
        PopulationData prey = predator ? opponent : population;
        float before = FoodWeb.CaptureEfficiency(
            predator ? population.movementAbility : opponent.movementAbility,
            predator ? population.size : opponent.size, prey);
        float direction = predator ? 1f : -1f;
        float after = Mathf.Clamp(before + direction * (increase ? step : -step) / 100f,
            0.5f, 1.5f);
        float captureChange = Mathf.Abs(after - before) / before;
        float energyChange = Mathf.Abs(CalculateEnergyNeed(population.size,
            population.movementAbility + (increase ? step : -step))
            - population.energyNeed) / population.energyNeed;
        return increase
            ? captureChange > energyChange * (predator ? 1f : 1.1f)
            : energyChange > captureChange;
    }

    private bool SizeBenefitExceedsEnergyCost(PopulationData population,
        PopulationData opponent)
    {
        float growth = Mathf.Min(mutationStep, 100f - population.size);
        if (growth <= 0f || population.energyNeed <= 0f) return false;

        bool isPredator = population.trophicLevel > opponent.trophicLevel;
        float predatorMovement = isPredator
            ? population.movementAbility : opponent.movementAbility;
        float predatorSize = isPredator ? population.size : opponent.size;
        PopulationData prey = isPredator ? opponent : population;
        float before = FoodWeb.CaptureEfficiency(predatorMovement, predatorSize, prey);
        float after = isPredator
            ? FoodWeb.CaptureEfficiency(predatorMovement, predatorSize + growth, prey)
            : Mathf.Clamp(1f + (predatorMovement - prey.movementAbility
                + predatorSize - prey.size - growth) / 100f, 0.5f, 1.5f);
        float captureBenefit = Mathf.Abs(after - before) / before;
        float energyCost = growth * SizeEnergyWeight / population.energyNeed;
        return captureBenefit > energyCost;
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
