using System.Collections.Generic;
using UnityEngine;

public class SimulationController : MonoBehaviour
{
    [SerializeField] private SimulationTime simulationTime;
    [SerializeField] private List<BlockInfo> blockInfos = new List<BlockInfo>();

    [Header("Energy")]
    [SerializeField] private float sizeWeight = 1f;
    [SerializeField] private float movementWeight = 1f;

    [Header("Population")]
    [SerializeField] private float reproductionScale = 0.05f;
    [SerializeField] private float maxFoodDeathRate = 0.1f;

    private void OnEnable()
    {
        simulationTime.OnDayChanged += SimulateDay;
    }

    private void OnDisable()
    {
        simulationTime.OnDayChanged -= SimulateDay;
    }

    private void SimulateDay(int day)
    {
        foreach (BlockInfo block in blockInfos)
        {
            SimulateBlock(block);
        }
    }

    private void SimulateBlock(BlockInfo block)
    {
        // 1. 环境资源恢复
        block.plantBiomass += block.habitatRecovery;

        // 2. 先计算所有种群的能量需求
        foreach (PopulationData population in block.community)
        {
            // 个体环境适应度
            int tempDifference = Mathf.Abs(block.temperature - population.fitTemperature);
            int humidityDifference = Mathf.Abs(block.humidity - population.fitHumidity);
            float fitness = Mathf.Clamp01((100f - (tempDifference + humidityDifference)) / 100f);
            population.environmentalFitness = Mathf.RoundToInt(fitness*100f);

            //个体能量需求：用体型和运动能力来体现系数
            population.energyNeed = Mathf.Max(
                1f, population.size * sizeWeight 
                + population.movementAbility * movementWeight);

            // 重置当天获得的资源
            population.allocatedBiomass = 0;
            population.carryingCapacity = 0;
        }

        // 3. 多种群竞争 Biomass
        float consumedBiomass = DistributeBiomass(block);

        // 4. 根据竞争后的 K 计算种群变化
        foreach (PopulationData population in block.community)
        {
            SimulatePopulation(population);
        }

        // 5. 扣除真正被种群获得的 Biomass
        block.plantBiomass -= consumedBiomass;
        block.plantBiomass = Mathf.Max(0f, block.plantBiomass);
    }

    private void SimulatePopulation(PopulationData population){
        float fitness = population.environmentalFitness / 100f;

        float r = population.fertility / 100f * reproductionScale;
        float N = population.speciesAmount;
        if (N <= 0f)
        {
            return;
        }

        // 用潜在资源计算增长空间，避免实际消耗的上限锁死增长
        float resourceRatio = population.carryingCapacity / N;
        float growthRate = 0f;
        if (resourceRatio > 1f)
        {
            growthRate = r * fitness * (1f - 1f / resourceRatio);
        }

        // 缺粮死亡率有上限，避免种群一天归零
        float foodDeathRate = Mathf.Clamp01(maxFoodDeathRate) * Mathf.Max(0f, 1f - resourceRatio);

        population.speciesAmount = Mathf.Max(0f, N * (1f + growthRate - foodDeathRate));
    }

    private float DistributeBiomass(BlockInfo block)
    {
        float remainingBiomass = block.plantBiomass;
        float totalConsumed = 0f;

        List<PopulationData> competitors = new List<PopulationData>();

        // 所有有能量需求的种群进入竞争
        foreach (PopulationData population in block.community)
        {
            if (population.speciesAmount > 0)
            {
                competitors.Add(population);
            }
        }

        // 潜在份额不受现有个体需求限制，用来计算各物种的承载量
        float totalPotentialWeight = 0f;
        foreach (PopulationData population in competitors)
        {
            float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
            totalPotentialWeight += population.energyNeed * population.speciesAmount * fitness;
        }

        if (totalPotentialWeight > 0f)
        {
            foreach (PopulationData population in competitors)
            {
                float fitness = Mathf.Clamp01(population.environmentalFitness / 100f);
                float competitionWeight = population.energyNeed * population.speciesAmount * fitness;
                float potentialBiomass = remainingBiomass * competitionWeight / totalPotentialWeight;
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
                float competitionWeight =remainingDemand * fitness;
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
                float competitionWeight = remainingDemand * fitness;
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
}
