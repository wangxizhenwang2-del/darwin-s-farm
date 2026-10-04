using System.Collections.Generic;
using UnityEngine;

// 管每个种群的适应度、出生和死亡。
public partial class SimulationController
{
    private const float FitnessTolerance = 90f;
    private const float SizeEnergyWeight = 1f;
    private const float MovementEnergyWeight = 0.02f;
    internal const float DensityPredationCompensation = 0.5f;

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

    public static float CalculateEnergyNeed(float size, float movement)
    {
        float speed = Mathf.Clamp(movement, 0f, 100f);
        return Mathf.Max(1f, size * SizeEnergyWeight
            + speed * MovementEnergyWeight * (1f + speed / 40f));
    }

    private static void PreparePopulations(BlockInfo block)
    {
        foreach (PopulationData population in block.community)
        {
            population.environmentalFitness = Mathf.RoundToInt(
                CalculateFitness(block, population) * 100f);
            population.energyNeed = CalculateEnergyNeed(population.size,
                population.movementAbility);
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
        int count = population.speciesAmount;
        if (count <= 0)
        {
            return;
        }

        float intakeRatio = Mathf.Clamp01(population.allocatedBiomass /
            (count * population.energyNeed));
        float capacity = population.carryingCapacity;
        int beforeHunt = population.populationBeforePredation;
        int hunted = population.deathsToday;
        EstimateDemography(population, beforeHunt, reproductionScale,
            maxOverCapacityDeathRate, maxStarvationDeathRate,
            out float expectedBirths, out float expectedDeaths);
        float densityDeaths = capacity > 0f
            ? expectedBirths * beforeHunt / capacity : 0f;
        expectedDeaths -= Mathf.Min(hunted, densityDeaths)
            * DensityPredationCompensation;

        // 小数先存起来，等凑够一个个体再改变数量。
        if (hunted == 0 && Mathf.Abs(capacity - count) < 0.0001f &&
            intakeRatio >= 0.9999f)
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

    // 捕食者的 K 与猎物自身的出生死亡使用同一套公式。
    internal static void EstimateDemography(PopulationData population, int count,
        float reproductionScale, float overCapacityDeathRate, float starvationDeathRate,
        out float births, out float deaths)
    {
        births = 0f;
        deaths = 0f;
        if (count <= 0 || population.energyNeed <= 0f) return;

        float intake = Mathf.Clamp01(population.allocatedBiomass /
            (count * population.energyNeed));
        float capacity = population.carryingCapacity;
        births = capacity > 0f ? count * (population.fertility / 100f)
            * reproductionScale * population.environmentalFitness / 100f * intake : 0f;
        float densityDeaths = capacity > 0f ? births * count / capacity : 0f;
        float overCapacityDeaths = count * overCapacityDeathRate
            * Mathf.Max(0f, 1f - capacity / count);
        float starvationDeaths = count * starvationDeathRate * (1f - intake);
        deaths = densityDeaths + overCapacityDeaths + starvationDeaths;
    }
}
