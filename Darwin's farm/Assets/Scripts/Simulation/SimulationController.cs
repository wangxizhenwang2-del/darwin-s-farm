using System.Collections.Generic;
using UnityEngine;

// 只安排每天的执行顺序；具体规则放在相应脚本里。
public partial class SimulationController : MonoBehaviour
{
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
        // 生态位开关仍是预留项，新的海陆空入口尚未排进每日流程。
        foreach (BlockInfo block in blockInfos)
        {
            SimulateBlock(block);
        }
        if (migrationEnabled && day > 0 && day % Mathf.Max(1, migrationInterval) == 0)
        {
            RunMigration(day);
        }
    }

    public void SimulateBlock(BlockInfo block)
    {
        if (block == null || block.community == null) return;

        block.community.RemoveAll(population => population == null);
        foreach (PopulationData population in block.community)
            InitializeMutationState(population);

        FoodWeb.GrowPlants(block);
        PreparePopulations(block);
        float eaten = FoodWeb.FeedCommunity(block, reproductionScale);
        UpdatePopulations(block);
        FoodWeb.SpendPlants(block, eaten);
        EvolveCommunity(block);
    }
}
