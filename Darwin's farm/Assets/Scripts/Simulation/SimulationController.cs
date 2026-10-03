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
        float totalEnergyDemand = 0f;

        // 2. 先计算所有种群的能量需求
        foreach (PopulationData population in block.community)
        {
            //个体能量需求：用体型和运动能力来体现系数
            population.energyNeed = Mathf.Max(
                1f, population.size * sizeWeight 
                + population.movementAbility * movementWeight);

             //群落能力需求：求和每个种群的能量需求
            totalEnergyDemand +=
                population.energyNeed *
                population.speciesAmount;
        }

        // 3. 所有种群使用同一个资源环境进行计算
        foreach (PopulationData population in block.community)
        {
            SimulatePopulation(block,population);
        }

        // 4. 统一消耗资源
        int consumedBiomass = Mathf.RoundToInt(Mathf.Min(
            block.plantBiomass,totalEnergyDemand));

        block.plantBiomass -= consumedBiomass;
        block.plantBiomass = Mathf.Max(0, block.plantBiomass);
    }

    private void SimulatePopulation(BlockInfo block, PopulationData population){
        // 环境适应度 = 100-温差和湿度差的均值
        int tempDifference = Mathf.Abs(block.temperature - population.fitTemperature);
        int humidityDifference = Mathf.Abs(block.humidity - population.fitHumidity);
        
        float fitness = (100f - (tempDifference + humidityDifference)) / 100f;
        
        population.environmentalFitness = Mathf.RoundToInt(fitness*100f);

        float r = population.fertility / 100f * reproductionScale;

        float K = block.plantBiomass / population.energyNeed;
        K = Mathf.Max(0.01f, K);

        float N = population.speciesAmount;
        float populationChange;
        if (fitness >= 0f)
        {
            populationChange = r * N * (1f - N / K) * fitness;
        }
        else
        {
            populationChange = N * r * fitness;
        }
        
        population.speciesAmount += Mathf.RoundToInt(populationChange);
        population.speciesAmount = Mathf.Max(0,population.speciesAmount);
    }
}