using System.Collections.Generic;
using UnityEngine;

public class SimulationController : MonoBehaviour
{   
    [SerializeField] private SimulationTime simulationTime;
    [SerializeField] private List<BlockInfo> blockInfos = new List<BlockInfo>();

    void OnEnable()
    {
        simulationTime.OnDayChanged += SimulateDay;
    }

    void OnDisable()
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
        foreach (PopulationData population in block.community)
        {
            SimulatePopulation(block, population);
        }
    }

    private void SimulatePopulation(BlockInfo block, PopulationData population)
    {
        
    }
}
