using System.Collections.Generic;
using UnityEngine;

public class SimulationController : MonoBehaviour
{
    private const float PreyEnergyPerSize = 15f;
    private const float PredationEfficiency = 0.8f;
    private const int MaxTrophicLevel = 2;
    private const float PredatorReserveDays = 10f;
    private const float MaxDailyPreyFraction = 0.1f;
    private const float FitnessTolerance = 90f;
    private const float SizeEnergyWeight = 1f;
    private const float MovementEnergyWeight = 0.02f;
    private const float MigrationPopulationScale = 100f;
    private const float EnvironmentalPressureRange = 30f;
    private const float MaximumSelectionPressure = 0.9f;
    private const float MinimumMutationScore = 0.15f;

    [SerializeField] private SimulationTime simulationTime;
    [SerializeField] private List<BlockInfo> blockInfos = new List<BlockInfo>();

    [Header("Population")]
    [SerializeField] private float reproductionScale = 0.05f;
    [SerializeField] private float maxOverCapacityDeathRate = 0.1f;
    [SerializeField] private float maxStarvationDeathRate = 0.6f;

    [Header("Mutation")]
    [SerializeField] private bool mutationEnabled = true;
    [Min(1)] [SerializeField] private int mutationInterval = 7;
    [Min(0f)] [SerializeField] private float mutationStep = 2f;
    [Min(0f)] [SerializeField] private float selectionStrength = 1f;

    [Header("Migration")]
    [SerializeField] private bool migrationEnabled = false;
    [Min(1)] [SerializeField] private int migrationInterval = 7;

    private struct MigrationEvent
    {
        public BlockInfo source;
        public BlockInfo target;
        public PopulationData population;
        public int amount;
    }

    private enum MutationTrait { Temperature, Humidity, Movement, Size, Fertility, Trophic }

    private struct MutationDecision
    {
        public PopulationData population;
        public MutationTrait trait;
        public float score;
    }

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

    // 数值模拟器可直接调整这些固定参数
    public void SetParameters(float newReproductionScale, float newMaxOverCapacityDeathRate,
        float newMaxStarvationDeathRate)
    {
        reproductionScale = Mathf.Max(0f, newReproductionScale);
        maxOverCapacityDeathRate = Mathf.Clamp01(newMaxOverCapacityDeathRate);
        maxStarvationDeathRate = Mathf.Clamp01(newMaxStarvationDeathRate);
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

    public void ScheduleMutationAfterEnvironmentChange(BlockInfo block)
    {
        if (block == null || block.community == null) return;
        foreach (PopulationData population in block.community)
        {
            if (population != null)
                population.mutationDaysElapsed = Mathf.Max(1, mutationInterval) - 1;
        }
    }

    private void SimulateDay(int day)
    {
        foreach (BlockInfo block in blockInfos)
        {
            SimulateBlock(block);
        }
        if (migrationEnabled && day > 0 && day % Mathf.Max(1, migrationInterval) == 0)
        {
            SimulateMigration();
        }
    }

    public void SimulateBlock(BlockInfo block)
    {
        if (block == null || block.community == null)
        {
            return;
        }

        block.community.RemoveAll(population => population == null);

        foreach (PopulationData population in block.community)
        {
            if (population == null) continue;
            InitializeMutationState(population);
        }

        // 1. 环境资源恢复
        float maxBiomass = Mathf.Max(0f, block.maxPlantBiomass);
        float biomassBeforeGrowth = Mathf.Clamp(block.plantBiomass, 0f, maxBiomass);
        block.plantBiomass = Mathf.Min(maxBiomass,
            biomassBeforeGrowth + Mathf.Max(0, block.habitatRecovery));
        block.plantGrowthToday = block.plantBiomass - biomassBeforeGrowth;

        // 2. 先计算所有种群的能量需求
        foreach (PopulationData population in block.community)
        {
            // 个体环境适应度
            float fitness = CalculateFitness(block, population);
            population.environmentalFitness = Mathf.RoundToInt(fitness*100f);

            //个体能量需求：用体型和运动能力来体现系数
            population.energyNeed = Mathf.Max(
                1f, population.size * SizeEnergyWeight
                + population.movementAbility * MovementEnergyWeight);
            population.populationBeforePredation = Mathf.Max(0, population.speciesAmount);

            // 重置当天获得的资源
            population.allocatedBiomass = 0;
            population.carryingCapacity = 0;
            population.birthsToday = 0;
            population.deathsToday = 0;
        }

        // 3. 植物只供给食草种群，然后逐级捕食
        float consumedBiomass = DistributeBiomass(block);
        int highestLevel = 0;
        foreach (PopulationData population in block.community)
        {
            highestLevel = Mathf.Max(highestLevel, population.trophicLevel);
        }
        for (int level = 1; level <= highestLevel; level++)
        {
            DistributePrey(block, level);
        }

        // 4. 根据竞争后的 K 计算种群变化
        foreach (PopulationData population in block.community)
        {
            float originalDemand = population.energyNeed * population.populationBeforePredation;
            population.actualEnergySatisfactionToday = originalDemand > 0f
                ? Mathf.Clamp01(population.allocatedBiomass / originalDemand) : 0f;
            population.energySatisfactionToday = population.populationBeforePredation > 0
                ? population.carryingCapacity / population.populationBeforePredation : 0f;
            SimulatePopulation(population);
            population.energyReserve = Mathf.Min(population.energyReserve,
                population.speciesAmount * population.energyNeed * PredatorReserveDays);
        }

        // 5. 扣除真正被种群获得的 Biomass
        block.consumedBiomassToday = consumedBiomass;
        block.plantBiomass -= consumedBiomass;
        block.plantBiomass = Mathf.Max(0f, block.plantBiomass);

        List<MutationDecision> decisions = null;
        foreach (PopulationData population in block.community)
        {
            if (population == null) continue;
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
                TrySelectMutation(block, population, out MutationDecision decision))
            {
                if (decisions == null) decisions = new List<MutationDecision>();
                decisions.Add(decision);
            }
            population.previousMutationPopulation = population.speciesAmount;
        }
        // 先确定全群落的方向，再应用变异，避免列表顺序改变当天的食物评分。
        if (decisions != null)
        {
            foreach (MutationDecision decision in decisions)
                ApplySelectedMutation(decision.population, decision.trait, decision.score);
        }
    }

    private static float CalculateFitness(BlockInfo block, PopulationData population)
    {
        int temperatureDifference = Mathf.Abs(block.temperature - population.fitTemperature);
        int humidityDifference = Mathf.Abs(block.humidity - population.fitHumidity);
        return Mathf.Clamp01(1f -
            (temperatureDifference + humidityDifference) / FitnessTolerance);
    }

    private void SimulateMigration()
    {
        List<MigrationEvent> events = new List<MigrationEvent>();
        foreach (BlockInfo source in blockInfos)
        {
            if (source == null || source.community == null || source.Neighbors == null) continue;
            foreach (PopulationData population in source.community)
            {
                if (population == null || population.speciesAmount <= 0) continue;

                float currentFitness = CalculateFitness(source, population);
                BlockInfo target = null;
                float targetFitness = currentFitness;
                float terrainProbability = 0f;
                foreach (BlockInfo neighbor in source.Neighbors)
                {
                    if (neighbor == null || neighbor == source) continue;
                    int heightDifference = Mathf.Abs(neighbor.elevation - source.elevation);
                    if (heightDifference >= 2) continue;
                    float fitness = CalculateFitness(neighbor, population);
                    if (fitness > targetFitness)
                    {
                        target = neighbor;
                        targetFitness = fitness;
                        terrainProbability = heightDifference == 0 ? 1f : 0.5f;
                    }
                }
                if (target == null) continue;

                float difference = targetFitness - currentFitness;
                float baseProbability = population.speciesAmount /
                    (population.speciesAmount + MigrationPopulationScale);
                float probability = difference * terrainProbability * baseProbability;
                if (Random.value >= probability) continue;

                int amount = Mathf.Min(population.speciesAmount,
                    Mathf.RoundToInt(population.speciesAmount * difference));
                if (amount <= 0) continue;
                events.Add(new MigrationEvent
                {
                    source = source,
                    target = target,
                    population = population,
                    amount = amount
                });
            }
        }

        // 全部结果确定后先迁出、再迁入；新迁入的种群不会在本日再次出发。
        List<PopulationData> arrivals = new List<PopulationData>(events.Count);
        foreach (MigrationEvent migration in events)
        {
            PopulationData origin = migration.population;
            int originalAmount = origin.speciesAmount;
            float share = migration.amount / (float)originalAmount;
            PopulationData arrival = CopyMigrant(origin, migration.amount);
            arrival.energyReserve = origin.energyReserve * share;
            origin.energyReserve -= arrival.energyReserve;
            arrival.birthRemainder = origin.birthRemainder * share;
            arrival.deathRemainder = origin.deathRemainder * share;
            origin.birthRemainder -= arrival.birthRemainder;
            origin.deathRemainder -= arrival.deathRemainder;
            origin.speciesAmount -= migration.amount;
            if (origin.speciesAmount == 0)
            {
                migration.source.community.Remove(origin);
            }
            arrivals.Add(arrival);
        }

        for (int i = 0; i < events.Count; i++)
        {
            BlockInfo target = events[i].target;
            if (target.community == null) target.community = new List<PopulationData>();
            PopulationData arrival = arrivals[i];
            PopulationData resident = null;
            if (arrival.species != null)
            {
                resident = target.community.Find(population =>
                    population != null && population.species == arrival.species);
            }
            if (resident == null)
            {
                target.community.Add(arrival);
            }
            else
            {
                InitializeMutationState(resident);
                MergeMigrants(resident, arrival);
            }
        }
    }

    private static PopulationData CopyMigrant(PopulationData source, int amount)
    {
        return new PopulationData
        {
            species = source.species,
            speciesAmount = amount,
            movementAbility = source.movementAbility,
            habitatNiche = source.habitatNiche,
            fitTemperature = source.fitTemperature,
            fitHumidity = source.fitHumidity,
            size = source.size,
            fertility = source.fertility,
            trophicLevel = source.trophicLevel,
            trophicLevelInitialized = true,
            temperatureMutationRemainder = source.temperatureMutationRemainder,
            humidityMutationRemainder = source.humidityMutationRemainder,
            movementMutationRemainder = source.movementMutationRemainder,
            sizeMutationRemainder = source.sizeMutationRemainder,
            fertilityMutationRemainder = source.fertilityMutationRemainder,
            trophicMutationRemainder = source.trophicMutationRemainder,
            mutationDaysElapsed = source.mutationDaysElapsed,
            previousMutationPopulation = amount,
            mutationPopulationInitialized = true
        };
    }

    private static void MergeMigrants(PopulationData resident, PopulationData arrival)
    {
        int residentAmount = resident.speciesAmount;
        int arrivalAmount = arrival.speciesAmount;
        int total = residentAmount + arrivalAmount;
        resident.movementAbility = WeightedTrait(resident.movementAbility, residentAmount,
            arrival.movementAbility, arrivalAmount, total);
        resident.habitatNiche = WeightedTrait(resident.habitatNiche, residentAmount,
            arrival.habitatNiche, arrivalAmount, total);
        resident.fitTemperature = WeightedTrait(resident.fitTemperature, residentAmount,
            arrival.fitTemperature, arrivalAmount, total);
        resident.fitHumidity = WeightedTrait(resident.fitHumidity, residentAmount,
            arrival.fitHumidity, arrivalAmount, total);
        resident.size = WeightedTrait(resident.size, residentAmount,
            arrival.size, arrivalAmount, total);
        resident.fertility = WeightedTrait(resident.fertility, residentAmount,
            arrival.fertility, arrivalAmount, total);
        resident.trophicLevel = WeightedTrait(resident.trophicLevel, residentAmount,
            arrival.trophicLevel, arrivalAmount, total);
        resident.temperatureMutationRemainder = WeightedValue(resident.temperatureMutationRemainder,
            residentAmount, arrival.temperatureMutationRemainder, arrivalAmount, total);
        resident.humidityMutationRemainder = WeightedValue(resident.humidityMutationRemainder,
            residentAmount, arrival.humidityMutationRemainder, arrivalAmount, total);
        resident.movementMutationRemainder = WeightedValue(resident.movementMutationRemainder,
            residentAmount, arrival.movementMutationRemainder, arrivalAmount, total);
        resident.sizeMutationRemainder = WeightedValue(resident.sizeMutationRemainder,
            residentAmount, arrival.sizeMutationRemainder, arrivalAmount, total);
        resident.fertilityMutationRemainder = WeightedValue(resident.fertilityMutationRemainder,
            residentAmount, arrival.fertilityMutationRemainder, arrivalAmount, total);
        resident.trophicMutationRemainder = WeightedValue(resident.trophicMutationRemainder,
            residentAmount, arrival.trophicMutationRemainder, arrivalAmount, total);
        resident.energyReserve += arrival.energyReserve;
        resident.birthRemainder += arrival.birthRemainder;
        resident.deathRemainder += arrival.deathRemainder;
        resident.speciesAmount = total;
        resident.previousMutationPopulation = total;
        resident.mutationPopulationInitialized = true;
        resident.trophicLevelInitialized = true;
    }

    private static int WeightedTrait(int resident, int residentAmount, int arrival,
        int arrivalAmount, int total)
    {
        return Mathf.RoundToInt(WeightedValue(resident, residentAmount,
            arrival, arrivalAmount, total));
    }

    private static float WeightedValue(float resident, int residentAmount, float arrival,
        int arrivalAmount, int total)
    {
        return (resident * residentAmount + arrival * arrivalAmount) / total;
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

    private bool TrySelectMutation(BlockInfo block, PopulationData population,
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
        ChooseMutation(ref selected, ref score, MutationTrait.Humidity, humidityScore);

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
                ChooseMutation(ref selected, ref score, MutationTrait.Movement, movementGap);
            else if (movementGap < 0f && shortage > 0.1f)
                ChooseMutation(ref selected, ref score, MutationTrait.Movement,
                    movementGap * shortage);

            if (sizeGap > 0f && enoughEnergy &&
                sustainableRatio >= (population.energyNeed + mutationStep * SizeEnergyWeight)
                    / population.energyNeed)
                ChooseMutation(ref selected, ref score, MutationTrait.Size, sizeGap);
            else if (sizeGap < 0f && shortage > 0.1f)
                ChooseMutation(ref selected, ref score, MutationTrait.Size, sizeGap * shortage);
        }

        if (actualRatio < 0.8f && sustainableRatio < 0.8f)
        {
            float currentFood = FoodAvailability(block, population.trophicLevel, population);
            int lower = population.trophicLevel - 1;
            int higher = population.trophicLevel + 1;
            if (lower >= 0)
                ChooseMutation(ref selected, ref score, MutationTrait.Trophic,
                    -FoodAdvantage(currentFood, FoodAvailability(block, lower, population))
                    * shortage);
            if (higher <= MaxTrophicLevel)
                ChooseMutation(ref selected, ref score, MutationTrait.Trophic,
                    FoodAdvantage(currentFood, FoodAvailability(block, higher, population))
                    * shortage);
        }

        float trend = population.previousMutationPopulation > 0
            ? (population.speciesAmount - population.previousMutationPopulation)
                / (float)population.previousMutationPopulation : 0f;
        if (trend < 0f && enoughEnergy && sustainableRatio >= 0.9f)
            ChooseMutation(ref selected, ref score, MutationTrait.Fertility,
                Mathf.Clamp01(-trend * 5f));
        else if (trend > 0f && shortage > 0.1f)
            ChooseMutation(ref selected, ref score, MutationTrait.Fertility,
                -Mathf.Clamp01(trend * 5f));

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

    private static void ChooseMutation(ref MutationTrait selected, ref float bestScore,
        MutationTrait candidate, float candidateScore)
    {
        if (Mathf.Abs(candidateScore) <= Mathf.Abs(bestScore)) return;
        selected = candidate;
        bestScore = candidateScore;
    }

    private void ApplySelectedMutation(PopulationData population, MutationTrait trait, float score)
    {
        switch (trait)
        {
            case MutationTrait.Temperature:
                ApplyMutation(ref population.fitTemperature, ref population.temperatureMutationRemainder,
                    1f, score, 0, 100); break;
            case MutationTrait.Humidity:
                ApplyMutation(ref population.fitHumidity, ref population.humidityMutationRemainder,
                    1f, score, 0, 100); break;
            case MutationTrait.Movement:
                ApplyMutation(ref population.movementAbility, ref population.movementMutationRemainder,
                    1f, score, 0, 100); break;
            case MutationTrait.Size:
                ApplyMutation(ref population.size, ref population.sizeMutationRemainder,
                    1f, score, 1, 100); break;
            case MutationTrait.Fertility:
                ApplyMutation(ref population.fertility, ref population.fertilityMutationRemainder,
                    0.5f, score, 0, 100); break;
            case MutationTrait.Trophic:
                ApplyMutation(ref population.trophicLevel, ref population.trophicMutationRemainder,
                    0.2f, score, Mathf.Max(0, population.trophicLevel - 1),
                    Mathf.Min(MaxTrophicLevel, population.trophicLevel + 1)); break;
        }
    }

    private float FoodAvailability(BlockInfo block, int level, PopulationData consumer)
    {
        if (level < 0 || level > MaxTrophicLevel) return 0f;
        float food = 0f;
        if (level == 0)
        {
            food = Mathf.Min(Mathf.Max(0f, block.habitatRecovery),
                Mathf.Max(0f, block.maxPlantBiomass));
        }
        else
        {
            foreach (PopulationData prey in block.community)
            {
                if (prey == consumer || prey.speciesAmount <= 0 ||
                    prey.trophicLevel != level - 1) continue;
                food += SustainablePreyProduction(prey) * prey.size
                    * PreyEnergyPerSize * PredationEfficiency
                    * CaptureEfficiency(consumer.movementAbility, consumer.size, prey);
            }
        }

        // 用同级总需求分摊食物，比较的是该种群可取得的能量，不是整块地的总量。
        float demand = consumer.energyNeed * Mathf.Max(1, consumer.speciesAmount);
        foreach (PopulationData competitor in block.community)
        {
            if (competitor != consumer && competitor.speciesAmount > 0 &&
                competitor.trophicLevel == level)
                demand += competitor.energyNeed * competitor.speciesAmount;
        }
        return demand > 0f ? food / demand : 0f;
    }

    private static float CaptureEfficiency(float predatorMovement, float predatorSize,
        PopulationData prey)
    {
        // 较快、较大的捕食者获取同一猎物所需的追逐成本更低。
        return Mathf.Clamp(1f + (predatorMovement - prey.movementAbility
            + predatorSize - prey.size) / 200f, 0.5f, 1.5f);
    }

    private static float ForagingEfficiency(PopulationData population)
    {
        return 0.75f + 0.0025f * Mathf.Clamp(population.movementAbility, 0, 100);
    }

    private float SustainablePreyProduction(PopulationData prey)
    {
        if (prey.speciesAmount <= 0 || prey.carryingCapacity <= 0f) return 0f;
        float r = prey.fertility / 100f * reproductionScale;
        float fitness = prey.environmentalFitness / 100f;
        float demand = prey.energyNeed * prey.populationBeforePredation;
        float intake = demand > 0f ? Mathf.Clamp01(prey.allocatedBiomass / demand) : 0f;
        return Mathf.Min(r * prey.carryingCapacity / 4f, r * prey.speciesAmount)
            * fitness * intake;
    }

    private void ApplyMutation(ref int trait, ref float remainder, float multiplier,
        float score, int minimum, int maximum)
    {
        float change = Mathf.Sign(score) * mutationStep * multiplier * Mathf.Clamp01(Mathf.Abs(score));
        float value = Mathf.Clamp(trait + remainder + change, minimum, maximum);
        trait = Mathf.RoundToInt(value);
        remainder = value - trait;
    }

    private void SimulatePopulation(PopulationData population){
        float fitness = population.environmentalFitness / 100f;

        float r = population.fertility / 100f * reproductionScale;
        int N = population.speciesAmount;
        if (N <= 0)
        {
            return;
        }

        float intakeRatio = Mathf.Clamp01(population.allocatedBiomass / (N * population.energyNeed));
        float K = population.carryingCapacity;

        // 出生与死亡分别计算；N=K 时两者相等，净增长为零
        float expectedBirths = K > 0f ? N * r * fitness * intakeRatio : 0f;
        float densityDeaths = K > 0f ? expectedBirths * N / K : 0f;

        // 超载和实际缺食会额外增加死亡
        float overCapacityDeaths = N * maxOverCapacityDeathRate
            * Mathf.Max(0f, 1f - K / N);
        float starvationDeaths = N * maxStarvationDeathRate * (1f - intakeRatio);
        float expectedDeaths = densityDeaths + overCapacityDeaths + starvationDeaths;

        // 小数只作为内部余量累计，出生和死亡都以整个体发生
        if (Mathf.Abs(K - N) < 0.0001f && intakeRatio >= 0.9999f)
        {
            population.deathRemainder = population.birthRemainder;
        }
        population.birthRemainder += expectedBirths;
        population.deathRemainder += expectedDeaths;
        int births = Mathf.FloorToInt(population.birthRemainder);
        int calculatedDeaths = Mathf.FloorToInt(population.deathRemainder);
        int deaths = Mathf.Min(N, calculatedDeaths);
        population.birthRemainder -= births;
        population.deathRemainder -= calculatedDeaths;
        population.birthsToday = births;
        population.deathsToday += deaths;
        population.speciesAmount = Mathf.Max(0, N + births - deaths);
    }

    private float DistributeBiomass(BlockInfo block)
    {
        float remainingBiomass = block.plantBiomass;
        float totalConsumed = 0f;

        List<PopulationData> competitors = new List<PopulationData>();

        // 没有物种资产的旧数据按 0 级处理
        foreach (PopulationData population in block.community)
        {
            if (population.speciesAmount > 0 &&
                population.trophicLevel == 0)
            {
                competitors.Add(population);
            }
        }

        float bestForagingEfficiency = 0f;
        float bestEnvironmentalFitness = 0f;
        foreach (PopulationData population in competitors)
        {
            float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
            bestForagingEfficiency = Mathf.Max(bestForagingEfficiency,
                ForagingEfficiency(population) * fitness);
            bestEnvironmentalFitness = Mathf.Max(bestEnvironmentalFitness, fitness);
        }
        remainingBiomass *= bestForagingEfficiency;

        // 长期承载量由每日植物恢复量决定，而不是已有植物库存
        float totalPotentialWeight = 0f;
        foreach (PopulationData population in competitors)
        {
            float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
            totalPotentialWeight += population.energyNeed * population.speciesAmount
                * fitness * ForagingEfficiency(population);
        }

        float dailyRecovery = Mathf.Min(Mathf.Max(0, block.habitatRecovery),
            Mathf.Max(0f, block.maxPlantBiomass)) * bestEnvironmentalFitness;
        if (totalPotentialWeight > 0f)
        {
            foreach (PopulationData population in competitors)
            {
                float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
                float competitionWeight = population.energyNeed * population.speciesAmount
                    * fitness * ForagingEfficiency(population);
                float potentialBiomass = dailyRecovery * competitionWeight / totalPotentialWeight;
                population.carryingCapacity = potentialBiomass / population.energyNeed;
            }
        }

        while (remainingBiomass > 0.01f &&
               competitors.Count > 0)
        {
            float totalCompetitionWeight = 0f;

            // 1. 计算这一轮竞争权重
            foreach (PopulationData population in competitors)
            {
                float totalDemand = population.energyNeed * population.speciesAmount;
                float remainingDemand = totalDemand - population.allocatedBiomass;
                float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
                float competitionWeight = remainingDemand * fitness * ForagingEfficiency(population);
                totalCompetitionWeight += competitionWeight;
            }

            // 所有剩余种群都完全不适应环境
            if (totalCompetitionWeight <= 0f)
            {
                break;
            }

            // 注意：
            // 本轮分配必须使用同一个资源快照
            float biomassThisRound = remainingBiomass;
            List<PopulationData> satisfiedPopulations = new List<PopulationData>();
            float consumedThisRound = 0f;

            // 2. 按权重分配
            foreach (PopulationData population in competitors)
            {
                float totalDemand = population.energyNeed * population.speciesAmount;
                float remainingDemand = totalDemand - population.allocatedBiomass;
                float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
                float competitionWeight = remainingDemand * fitness * ForagingEfficiency(population);
                float resourceShare = competitionWeight / totalCompetitionWeight;
                float allocated = biomassThisRound * resourceShare;

                // 不能超过该种群自己的剩余需求
                float actualAllocated = Mathf.Min(allocated, remainingDemand);
                population.allocatedBiomass += actualAllocated;
                consumedThisRound += actualAllocated;

                // 已经吃饱
                if (population.allocatedBiomass >= totalDemand - 0.01f)
                {
                    satisfiedPopulations.Add(population);
                }
            }

            remainingBiomass -= consumedThisRound;
            totalConsumed += consumedThisRound;

            // 3. 吃饱的种群退出竞争
            foreach (PopulationData population in satisfiedPopulations)
            {
                competitors.Remove(population);
            }

            // 理论上资源已经全部按比例分完，
            // 如果没人吃饱就没必要继续下一轮
            if (satisfiedPopulations.Count == 0)
            {
                break;
            }
        }

        return totalConsumed;
    }

    private void DistributePrey(BlockInfo block, int level)
    {
        List<PopulationData> predators = new List<PopulationData>();
        List<PopulationData> prey = new List<PopulationData>();
        float totalDemand = 0f;
        float totalDeficit = 0f;
        float totalForagingWeight = 0f;
        float weightedMovement = 0f;
        float weightedSize = 0f;

        // 同一营养级先统一读取数量与需求，再进行任何扣减。
        foreach (PopulationData population in block.community)
        {
            if (population.speciesAmount <= 0)
            {
                continue;
            }
            int populationLevel = population.trophicLevel;
            if (populationLevel == level)
            {
                predators.Add(population);
                float demand = population.energyNeed * population.speciesAmount;
                totalDemand += demand;
                totalDeficit += Mathf.Max(0f, demand - population.energyReserve);
                weightedMovement += demand * population.movementAbility;
                weightedSize += demand * population.size;
                totalForagingWeight += demand * ForagingEfficiency(population)
                    * Mathf.Clamp01(population.environmentalFitness / 100f);
            }
            else if (populationLevel == level - 1 && population.size > 0)
            {
                prey.Add(population);
            }
        }
        if (totalDemand <= 0f)
        {
            return;
        }

        float hunterEfficiency = totalForagingWeight / totalDemand;
        float availableEnergy = 0f;
        float sustainableEnergy = 0f;
        int[] maxKills = new int[prey.Count];
        float[] energyPerPrey = new float[prey.Count];
        for (int i = 0; i < prey.Count; i++)
        {
            maxKills[i] = Mathf.FloorToInt(prey[i].speciesAmount * MaxDailyPreyFraction);
            energyPerPrey[i] = prey[i].size * PreyEnergyPerSize
                * PredationEfficiency * hunterEfficiency
                * CaptureEfficiency(weightedMovement / totalDemand,
                    weightedSize / totalDemand, prey[i]);
            availableEnergy += maxKills[i] * energyPerPrey[i];
            sustainableEnergy += SustainablePreyProduction(prey[i]) * energyPerPrey[i];
        }

        // 捕食者承载量由猎物的可持续日生产量决定，而不是现存全部猎物。
        float dailyFood = Mathf.Min(availableEnergy, sustainableEnergy);
        foreach (PopulationData predator in predators)
        {
            float weight = predator.energyNeed * predator.speciesAmount
                * ForagingEfficiency(predator)
                * Mathf.Clamp01(predator.environmentalFitness / 100f);
            predator.carryingCapacity = totalForagingWeight > 0f
                ? dailyFood * weight / totalForagingWeight / predator.energyNeed : 0f;
        }

        // 储备先支付当天能耗；不足时才捕猎，整只猎物多出的能量留作后续几天使用。
        float fraction = availableEnergy > 0f
            ? Mathf.Min(1f, totalDeficit / availableEnergy) : 0f;
        int[] kills = new int[prey.Count];
        float gainedEnergy = 0f;
        for (int i = 0; i < prey.Count; i++)
        {
            kills[i] = Mathf.FloorToInt(maxKills[i] * fraction);
            gainedEnergy += kills[i] * energyPerPrey[i];
        }

        while (gainedEnergy < totalDeficit && gainedEnergy < availableEnergy)
        {
            int best = -1;
            float bestRemainder = -1f;
            for (int i = 0; i < prey.Count; i++)
            {
                if (kills[i] >= maxKills[i])
                {
                    continue;
                }
                float remainder = maxKills[i] * fraction - kills[i];
                if (remainder > bestRemainder)
                {
                    best = i;
                    bestRemainder = remainder;
                }
            }
            if (best < 0)
            {
                break;
            }
            kills[best]++;
            gainedEnergy += energyPerPrey[best];
        }

        for (int i = 0; i < prey.Count; i++)
        {
            prey[i].speciesAmount -= kills[i];
            prey[i].deathsToday += kills[i];
        }

        float[] energyByPredator = new float[predators.Count];
        float remainingEnergy = gainedEnergy;
        for (int i = 0; i < predators.Count; i++)
        {
            energyByPredator[i] = predators[i].energyReserve;
        }
        while (remainingEnergy > 0.001f)
        {
            float weightTotal = 0f;
            for (int i = 0; i < predators.Count; i++)
            {
                PopulationData predator = predators[i];
                float demand = predator.energyNeed * predator.speciesAmount;
                weightTotal += Mathf.Max(0f, demand - energyByPredator[i])
                    * ForagingEfficiency(predator)
                    * Mathf.Clamp01(predator.environmentalFitness / 100f);
            }
            if (weightTotal <= 0f) break;

            float roundEnergy = remainingEnergy;
            float distributed = 0f;
            for (int i = 0; i < predators.Count; i++)
            {
                PopulationData predator = predators[i];
                float demand = predator.energyNeed * predator.speciesAmount;
                float deficit = Mathf.Max(0f, demand - energyByPredator[i]);
                float weight = deficit * ForagingEfficiency(predator)
                    * Mathf.Clamp01(predator.environmentalFitness / 100f);
                float amount = Mathf.Min(deficit, roundEnergy * weight / weightTotal);
                energyByPredator[i] += amount;
                distributed += amount;
            }
            if (distributed <= 0f) break;
            remainingEnergy -= distributed;
        }
        for (int i = 0; i < predators.Count; i++)
        {
            PopulationData predator = predators[i];
            float demand = predator.energyNeed * predator.speciesAmount;
            float extraWeight = demand * ForagingEfficiency(predator)
                * Mathf.Clamp01(predator.environmentalFitness / 100f);
            float extra = totalForagingWeight > 0f
                ? remainingEnergy * extraWeight / totalForagingWeight : 0f;
            float energy = energyByPredator[i] + extra;
            predator.allocatedBiomass = Mathf.Min(demand, energy);
            predator.energyReserve = Mathf.Clamp(energy - demand, 0f,
                demand * PredatorReserveDays);
        }
    }
}
