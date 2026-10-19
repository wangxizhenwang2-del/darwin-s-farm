using System;
using UnityEngine;

public partial class SimulationController
{
    public event Action<int, BlockInfo, PopulationData, string, string> OnEcologyEvent;
    [SerializeField] private bool enableEcologyConsoleLogs = false;
    public bool EnableEcologyConsoleLogs
    {
        get => enableEcologyConsoleLogs;
        set => enableEcologyConsoleLogs = value;
    }

    private void LogEcology(int day, BlockInfo block, PopulationData population,
        string kind, string detail)
    {
#if UNITY_5_3_OR_NEWER
        if (enableEcologyConsoleLogs || kind == "formal_evolution_waiting" ||
            kind == "evolution_gate")
            Debug.Log("[Ecology] day=" + day + " kind=" + kind + " population="
                + (population != null ? population.PopulationId : "") + " " + detail);
#endif
        OnEcologyEvent?.Invoke(day, block, population, kind, detail);
    }

    private void RemoveEndangered(int day)
    {
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.community == null ||
                block.waterCoverage != WaterCoverage.Land) continue;
            for (int i = block.community.Count - 1; i >= 0; i--)
            {
                PopulationData population = block.community[i];
                if (population == null ||
                    population.ecologicalNiche != EcologicalNiche.Land ||
                    population.endangeredDays < 10) continue;
                LogEcology(day, block, population, "local_extinction",
                    "species=" + (population.species != null
                        ? population.species.SpeciesName : population.lineageName)
                    + " count=" + population.speciesAmount
                    + " days=" + population.endangeredDays);
                RecordEndangeredRemoval(population);
                block.detritusBiomass += Mathf.Max(0f, population.bodyBiomass)
                    + Mathf.Max(0f, population.energyReserve);
                block.community.RemoveAt(i);
            }
        }
    }

    // Export compatibility: the retired same-species gate is never ready.
    public struct SplitGateInfo
    {
        public string reason;
        public SpeciesData target;
        public int founders;
        public float environmentalK, foodAverage10;
        public float demandBefore, demandAfter;
    }

    public SplitGateInfo ReadSplitGate(BlockInfo block,
        PopulationData source, int day) => new SplitGateInfo
    {
        reason = "retired_formal_species_evolution",
        environmentalK = block != null && source != null
            ? LandLifeRules.EnvironmentalCarryingCapacity(block, source, LandRules)
            : 0f,
        foodAverage10 = source != null && source.foodSatisfactionHistory != null
            ? LandLifeRules.Average(source.foodSatisfactionHistory) : 0f
    };
}
