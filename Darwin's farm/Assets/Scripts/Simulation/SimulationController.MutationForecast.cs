using UnityEngine;

public partial class SimulationController
{
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
