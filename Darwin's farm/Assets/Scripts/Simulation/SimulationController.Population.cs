using System.Collections.Generic;
using UnityEngine;

// 管每个种群的适应度、出生和死亡。
public partial class SimulationController
{
    private const float FitnessTolerance = 90f;
    private const float SizeEnergyWeight = 1f;
    private const float MovementEnergyWeight = 0.02f;

    [Header("Population")]
    [SerializeField] private float reproductionScale = 0.05f;
    [SerializeField] private float maxOverCapacityDeathRate = 0.1f;
    [SerializeField] private float maxStarvationDeathRate = 0.6f;

    // 数值模拟器可直接调整这些固定参数
    public void SetParameters(float newReproductionScale, float newMaxOverCapacityDeathRate,
        float newMaxStarvationDeathRate)
    {
        reproductionScale = Mathf.Max(0f, newReproductionScale);
        maxOverCapacityDeathRate = Mathf.Clamp01(newMaxOverCapacityDeathRate);
        maxStarvationDeathRate = Mathf.Clamp01(newMaxStarvationDeathRate);
    }

    public static float CalculateFitness(BlockInfo block, PopulationData population)
    {
        int temperatureDifference = Mathf.Abs(block.temperature - population.fitTemperature);
        int humidityDifference = Mathf.Abs(block.humidity - population.fitHumidity);
        return Mathf.Clamp01(1f -
            (temperatureDifference + humidityDifference) / FitnessTolerance);
    }

    private static void PreparePopulations(BlockInfo block)
    {
        foreach (PopulationData population in block.community)
        {
            population.environmentalFitness = Mathf.RoundToInt(
                CalculateFitness(block, population) * 100f);
            population.energyNeed = Mathf.Max(1f,
                population.size * SizeEnergyWeight +
                population.movementAbility * MovementEnergyWeight);
            population.populationBeforePredation = Mathf.Max(0, population.speciesAmount);
            population.allocatedBiomass = 0f;
            population.carryingCapacity = 0f;
            population.birthsToday = 0;
            population.deathsToday = 0;
        }
    }

    private void UpdatePopulations(BlockInfo block)
    {
        foreach (PopulationData population in block.community)
        {
            float demand = population.energyNeed * population.populationBeforePredation;
            population.actualEnergySatisfactionToday = demand > 0f
                ? Mathf.Clamp01(population.allocatedBiomass / demand) : 0f;
            population.energySatisfactionToday = population.populationBeforePredation > 0
                ? population.carryingCapacity / population.populationBeforePredation : 0f;
            UpdatePopulation(population);
            population.energyReserve = Mathf.Min(population.energyReserve,
                population.speciesAmount * population.energyNeed * FoodWeb.PredatorReserveDays);
        }
    }

    private void UpdatePopulation(PopulationData population)
    {
        float fitness = population.environmentalFitness / 100f;

        float birthRate = population.fertility / 100f * reproductionScale;
        int count = population.speciesAmount;
        if (count <= 0)
        {
            return;
        }

        float intakeRatio = Mathf.Clamp01(population.allocatedBiomass /
            (count * population.energyNeed));
        float capacity = population.carryingCapacity;

        // 食物刚好养得起所有个体时，出生与密度死亡会相互抵消。
        float expectedBirths = capacity > 0f
            ? count * birthRate * fitness * intakeRatio : 0f;
        float densityDeaths = capacity > 0f
            ? expectedBirths * count / capacity : 0f;

        float overCapacityDeaths = count * maxOverCapacityDeathRate
            * Mathf.Max(0f, 1f - capacity / count);
        float starvationDeaths = count * maxStarvationDeathRate * (1f - intakeRatio);
        float expectedDeaths = densityDeaths + overCapacityDeaths + starvationDeaths;

        // 小数先存起来，等凑够一个个体再改变数量。
        if (Mathf.Abs(capacity - count) < 0.0001f && intakeRatio >= 0.9999f)
        {
            population.deathRemainder = population.birthRemainder;
        }
        population.birthRemainder += expectedBirths;
        population.deathRemainder += expectedDeaths;
        int births = Mathf.FloorToInt(population.birthRemainder);
        int calculatedDeaths = Mathf.FloorToInt(population.deathRemainder);
        int deaths = Mathf.Min(count, calculatedDeaths);
        population.birthRemainder -= births;
        population.deathRemainder -= calculatedDeaths;
        population.birthsToday = births;
        population.deathsToday += deaths;
        population.speciesAmount = Mathf.Max(0, count + births - deaths);
    }
}
