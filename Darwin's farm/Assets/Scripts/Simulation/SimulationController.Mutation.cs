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

    // 候选性状的预期增长计算，与变异决策共用同一组参数。
    // 用现有的一步人口公式比较候选性状；只改变平均性状，不生成个体或新参数。
    private bool TryChooseForecastMutation(BlockInfo block, PopulationData population,
        out MutationDecision decision)
    {
        decision = new MutationDecision();
        if (population.speciesAmount <= 0 || population.energyNeed <= 0f ||
            mutationStep <= 0f || selectionStrength <= 0f) return false;

        float temperature = population.fitTemperature + population.temperatureMutationRemainder;
        float humidity = population.fitHumidity + population.humidityMutationRemainder;
        float movement = population.movementAbility + population.movementMutationRemainder;
        float size = population.size + population.sizeMutationRemainder;
        float fertility = population.fertility + population.fertilityMutationRemainder;
        float baseline = ForecastGrowth(block, population, temperature, humidity,
            movement, size, fertility);
        float reference = Mathf.Max(0.01f, population.speciesAmount
            * reproductionScale * mutationStep / 100f);
        MutationTrait bestTrait = MutationTrait.Temperature;
        float bestGain = 0f;
        int bestDirection = 0;
        bool nicheSwitch = false;

        for (int trait = 0; trait < 5; trait++)
        {
            // 体型与运动只在有捕食关系时参与普通选择；无捕食时仍可因竞争换赛道。
            if (((MutationTrait)trait == MutationTrait.Size ||
                 (MutationTrait)trait == MutationTrait.Movement) &&
                !HasTrophicInteraction(block, population)) continue;
            for (int direction = -1; direction <= 1; direction += 2)
            {
                float candidateTemperature = temperature;
                float candidateHumidity = humidity;
                float candidateMovement = movement;
                float candidateSize = size;
                float candidateFertility = fertility;
                switch ((MutationTrait)trait)
                {
                    case MutationTrait.Temperature:
                        candidateTemperature = Mathf.Clamp(temperature + direction * mutationStep, 0f, 100f); break;
                    case MutationTrait.Humidity:
                        candidateHumidity = Mathf.Clamp(humidity + direction * mutationStep, 0f, 100f); break;
                    case MutationTrait.Movement:
                        candidateMovement = Mathf.Clamp(movement + direction * mutationStep, 0f, 100f); break;
                    case MutationTrait.Size:
                        candidateSize = Mathf.Clamp(size + direction * mutationStep * 0.1f, 1f, 100f); break;
                    case MutationTrait.Fertility:
                        candidateFertility = Mathf.Clamp(fertility + direction * mutationStep * 0.5f, 0f, 100f); break;
                }
                if (candidateTemperature == temperature && candidateHumidity == humidity &&
                    candidateMovement == movement && candidateSize == size &&
                    candidateFertility == fertility) continue;

                float gain = ForecastGrowth(block, population, candidateTemperature,
                    candidateHumidity, candidateMovement, candidateSize,
                    candidateFertility) - baseline;
                if (gain <= bestGain + 0.00001f) continue;
                bestGain = gain;
                bestTrait = (MutationTrait)trait;
                bestDirection = direction;
                nicheSwitch = false;
            }
        }

        // 跨体型 8 的中间一步可能暂时不利，只有持续失势的少数种群才试算另一赛道。
        if (population.trophicLevel == 0 && IsDecliningSameTrackMinority(block, population))
        {
            float targetSize = size < 8f ? 10f : 6f;
            float alternativeCapacity = FoodWeb.ProjectedHerbivoreCapacity(block,
                population, targetSize, movement,
                ForecastFitness(block, temperature, humidity), fertility);
            if (alternativeCapacity > population.speciesAmount * 0.5f)
            {
                float gain = Mathf.Max(reference * 0.38f,
                    ForecastGrowth(block, population, temperature, humidity,
                        movement, targetSize, fertility) - baseline);
                if (gain > bestGain + 0.00001f)
                {
                    bestGain = gain;
                    bestTrait = MutationTrait.Size;
                    bestDirection = targetSize > size ? 1 : -1;
                    nicheSwitch = true;
                }
            }
        }

        // 一步增加 1 点生育能力所对应的预期出生量，作为无量纲评分单位。
        float score = Mathf.Min(1f, bestGain / reference * selectionStrength);
        if (bestDirection == 0 || score < MinimumMutationScore) return false;
        decision = new MutationDecision
        {
            population = population, trait = bestTrait,
            score = bestDirection * score, expectedGain = bestGain,
            nicheSwitch = nicheSwitch
        };
        return true;
    }

    public string GetExpectedEvolutionDirection(BlockInfo block, PopulationData population)
    {
        if (!mutationEnabled || mutationStep <= 0f) return "进化方向：已关闭";
        if (block == null || population == null || population.speciesAmount <= 0)
            return "进化方向：无存活个体";
        if (population.energyNeed <= 0f) return "进化方向：待首日计算";
        if (!TryChooseForecastMutation(block, population, out MutationDecision decision))
            return "进化方向：暂无线性优势";
        string name = decision.trait == MutationTrait.Temperature ? "适温" :
            decision.trait == MutationTrait.Humidity ? "适湿" :
            decision.trait == MutationTrait.Movement ? "运动" :
            decision.trait == MutationTrait.Size ? "体型" : "生育";
        return "进化方向：" + name + (decision.score > 0f ? " ↑" : " ↓")
            + "（预计净增长改善 +" + decision.expectedGain.ToString("F2") + "/日）";
    }

    private static float ForecastFitness(BlockInfo block, float temperature, float humidity) =>
        Mathf.Clamp01(1f - (Mathf.Abs(block.temperature - temperature)
            + Mathf.Abs(block.humidity - humidity)) / FitnessTolerance);

    private float ForecastGrowth(BlockInfo block, PopulationData population,
        float temperature, float humidity, float movement, float size,
        float fertility)
    {
        int count = population.speciesAmount;
        float fitness = ForecastFitness(block, temperature, humidity);
        float need = CalculateEnergyNeed(size, movement, fertility);
        float capacity = population.trophicLevel == 0
            ? FoodWeb.ProjectedHerbivoreCapacity(block, population, size,
                movement, fitness, fertility)
            : FoodWeb.ProjectedPredatorCapacity(block, population, size,
                movement, fitness, fertility, reproductionScale,
                maxOverCapacityDeathRate, maxStarvationDeathRate,
                levelOnePreyFraction, levelTwoPreyFraction);

        // 已有实际摄食率提供当天的起点，K 的变化提供候选性状对食物份额的影响。
        float intake = Mathf.Clamp01(population.actualEnergySatisfactionToday
            + (capacity - population.carryingCapacity)
            / Mathf.Max(1f, count));
        if (population.trophicLevel == 0 && block.plantBiomass >
            count * need / Mathf.Max(0.1f, fitness))
            intake = Mathf.Max(intake, population.actualEnergySatisfactionToday);
        EstimateDemography(count, fertility, fitness * 100f, need,
            intake * count * need, capacity, reproductionScale,
            maxOverCapacityDeathRate, maxStarvationDeathRate *
                (population.trophicLevel > 0 ? PredatorStarvationMultiplier : 1f),
            out float births, out float deaths);
        float hunted = FoodWeb.ExpectedPreyLoss(block, population, movement, size,
            levelOnePreyFraction, levelTwoPreyFraction);
        float densityDeaths = capacity > 0f ? births * count / capacity : 0f;
        deaths -= Mathf.Min(hunted, densityDeaths) * DensityPredationCompensation;
        return births - deaths - hunted;
    }

    private static bool IsDecliningSameTrackMinority(BlockInfo block,
        PopulationData population)
    {
        if (population.speciesAmount >= population.previousMutationPopulation)
            return false;
        float ownForaging = Mathf.Clamp01(population.environmentalFitness / 100f)
            * (0.75f + population.movementAbility * 0.0025f);
        foreach (PopulationData other in block.community)
            if (other != population && other.speciesAmount > population.speciesAmount &&
                other.trophicLevel == 0 &&
                (other.size < 8) == (population.size < 8) &&
                Mathf.Clamp01(other.environmentalFitness / 100f)
                    * (0.75f + other.movementAbility * 0.0025f)
                    > ownForaging + 0.001f)
                return true;
        return false;
    }

    private static bool HasTrophicInteraction(BlockInfo block, PopulationData population)
    {
        foreach (PopulationData other in block.community)
            if (other != population && other.speciesAmount > 0 &&
                Mathf.Abs(other.trophicLevel - population.trophicLevel) == 1)
                return true;
        return false;
    }
}
