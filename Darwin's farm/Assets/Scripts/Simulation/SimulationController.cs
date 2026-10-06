using System.Collections.Generic;
using UnityEngine;

// 只安排每天的执行顺序；具体规则放在相应脚本里。
public partial class SimulationController : MonoBehaviour
{
    // 库存低于上限的 40% 时维持完整日恢复量；更密集时逐步减速。
    private const float FullPlantGrowthUntilFraction = 0.4f;

    [SerializeField] private SimulationTime simulationTime;
    [SerializeField] private List<BlockInfo> blockInfos = new List<BlockInfo>();

    private void OnEnable()
    {
        if (simulationTime != null)
        {
            simulationTime.OnDayChanged += SimulateDay;
        }
    }

    private void OnDisable()
    {
        if (simulationTime != null)
        {
            simulationTime.OnDayChanged -= SimulateDay;
        }
    }

    public void SetBlocks(List<BlockInfo> blocks)
    {
        blockInfos = blocks ?? new List<BlockInfo>();
        UpdateSpeciesStatuses();
    }

    public static float CalculatePlantGrowth(float stock, float maximum, int recovery)
    {
        maximum = Mathf.Max(0f, maximum);
        if (maximum <= 0f) return 0f;
        stock = Mathf.Clamp(stock, 0f, maximum);
        float remaining = maximum - stock;
        float densityFactor = Mathf.Clamp01(remaining /
            (maximum * (1f - FullPlantGrowthUntilFraction)));
        return Mathf.Min(remaining, Mathf.Max(0, recovery) * densityFactor);
    }

    public static bool SetTrophicLevel(PopulationData population, int level)
    {
        if (population == null) return false;
        level = Mathf.Clamp(level, 0, MaxTrophicLevel);
        if (population.trophicLevelInitialized && population.trophicLevel == level) return false;

        population.trophicLevel = level;
        population.trophicLevelInitialized = true;
        population.trophicMutationRemainder = 0f;
        population.energyReserve = 0f;
        population.mutationDaysElapsed = 0;
        return true;
    }

    public bool ApplyEnvironment(BlockInfo block, int temperature, int humidity, int recovery)
    {
        if (block == null) return false;
        temperature = Mathf.Clamp(temperature, 0, 100);
        humidity = Mathf.Clamp(humidity, 0, 100);
        recovery = Mathf.Max(0, recovery);
        if (block.temperature == temperature && block.humidity == humidity &&
            block.habitatRecovery == recovery) return false;

        block.temperature = temperature;
        block.humidity = humidity;
        block.habitatRecovery = recovery;
        NotifyEnvironmentChanged();
        RetryMutationSoon(block);
        return true;
    }

    public void SimulateDay(int day)
    {
        var livingAtStart = LivingSpeciesByBlock();
        // 生态位开关仍是预留项，新的海陆空入口尚未排进每日流程。
        foreach (BlockInfo block in blockInfos)
        {
            SimulateBlock(block);
        }
        RunPresetEvolution(day, livingAtStart);
        if (migrationEnabled && day > 0 && day % Mathf.Max(1, migrationInterval) == 0)
        {
            RunMigration(day);
        }
        UpdateSpeciesStatuses();
    }

    public void SimulateBlock(BlockInfo block)
    {
        if (block == null || block.community == null) return;

        block.community.RemoveAll(population => population == null);
        foreach (PopulationData population in block.community)
            InitializeMutationState(population);

        FoodWeb.GrowPlants(block);
        PreparePopulations(block);
        float eaten = FoodWeb.FeedCommunity(block, reproductionScale,
            maxOverCapacityDeathRate, maxStarvationDeathRate,
            levelOnePreyFraction, levelTwoPreyFraction);
        UpdatePopulations(block);
        FoodWeb.SpendPlants(block, eaten);
        EvolveCommunity(block);
    }

    // 种群适应度、出生与死亡。
    private const float FitnessTolerance = 90f;
    private const float SizeEnergyWeight = 1f;
    private const float SmallSizeEnergyWeight = 2f;
    private const float MovementEnergyWeight = 0.01f;
    private const float HighSpeedEnergyWeight = 0.00001f;
    private const float HighFertilityEnergyWeight = 0.0002f;
    private const float ExtremeTraitEnergyWeight = 0.02f;
    internal const float DensityPredationCompensation = 0.5f;
    internal const float PredatorStarvationMultiplier = 0.5f;
    public const float DefaultLevelOnePreyFraction = 0.08f;
    public const float DefaultLevelTwoPreyFraction = 0.1f;

    [Header("Population")]
    [SerializeField] private float reproductionScale = 0.05f;
    [SerializeField] private float maxOverCapacityDeathRate = 0.1f;
    [SerializeField] private float maxStarvationDeathRate = 0.6f;
    [SerializeField] private float levelOnePreyFraction = DefaultLevelOnePreyFraction;
    [SerializeField] private float levelTwoPreyFraction = DefaultLevelTwoPreyFraction;

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

    public void SetPredationFractions(float levelOne, float levelTwo)
    {
        levelOnePreyFraction = Mathf.Clamp01(levelOne);
        levelTwoPreyFraction = Mathf.Clamp01(levelTwo);
    }

    public static float CalculateEnergyNeed(float size, float movement)
        => CalculateEnergyNeed(size, movement, 80f);

    public static float CalculateEnergyNeed(float size, float movement, float fertility)
    {
        float speed = Mathf.Clamp(movement, 0f, 100f);
        float highSpeed = Mathf.Max(0f, speed - 50f);
        float extremeSpeed = Mathf.Max(0f, speed - 90f);
        float highFertility = Mathf.Max(0f, fertility - 80f);
        float extremeFertility = Mathf.Max(0f, fertility - 90f);
        // 小体型的基础生理成本抵消无限缩小带来的节能收益。
        float smallSize = Mathf.Max(0f, 5f - size);
        return Mathf.Max(1f, size * SizeEnergyWeight
            + smallSize * smallSize * SmallSizeEnergyWeight
            + speed * MovementEnergyWeight * (1f + speed / 40f)
            + highSpeed * highSpeed * highSpeed * HighSpeedEnergyWeight
            + extremeSpeed * extremeSpeed * extremeSpeed * ExtremeTraitEnergyWeight
            + highFertility * highFertility * HighFertilityEnergyWeight
            + extremeFertility * extremeFertility * extremeFertility
                * ExtremeTraitEnergyWeight);
    }

    private static void PreparePopulations(BlockInfo block)
    {
        foreach (PopulationData population in block.community)
        {
            population.environmentalFitness = Mathf.RoundToInt(
                CalculateFitness(block, population) * 100f);
            population.energyNeed = CalculateEnergyNeed(population.size,
                population.movementAbility, population.fertility
                    + population.fertilityMutationRemainder);
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
            maxOverCapacityDeathRate, maxStarvationDeathRate *
                (population.trophicLevel > 0 ? PredatorStarvationMultiplier : 1f),
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
        EstimateDemography(count, population.fertility, population.environmentalFitness,
            population.energyNeed, population.allocatedBiomass,
            population.carryingCapacity, reproductionScale, overCapacityDeathRate,
            starvationDeathRate, out births, out deaths);
    }

    internal static void EstimateDemography(int count, float fertility, float fitness,
        float energyNeed, float allocatedBiomass, float capacity,
        float reproductionScale, float overCapacityDeathRate, float starvationDeathRate,
        out float births, out float deaths)
    {
        births = 0f;
        deaths = 0f;
        if (count <= 0 || energyNeed <= 0f) return;

        float intake = Mathf.Clamp01(allocatedBiomass / (count * energyNeed));
        births = capacity > 0f ? count * (fertility / 100f)
            * reproductionScale * fitness / 100f * intake : 0f;
        float densityDeaths = capacity > 0f ? births * count / capacity : 0f;
        float overCapacityDeaths = count * overCapacityDeathRate
            * Mathf.Max(0f, 1f - capacity / count);
        float starvationDeaths = count * starvationDeathRate * (1f - intake);
        deaths = densityDeaths + overCapacityDeaths + starvationDeaths;
    }
}
