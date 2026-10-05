using System.Collections.Generic;
using UnityEngine;

// 负责挑目的地并提交迁徙计划；个体的拆分与合并交给 PopulationTransfer。
public partial class SimulationController
{
    private const float TerrainStepProbability = 0.5f;

    [Header("Migration")]
    [SerializeField] private bool migrationEnabled = false;
    [Min(1)] [SerializeField] private int migrationInterval = 7;
    [Min(0.01f)] [SerializeField] private float migrationPopulationScale = 100f;
    [Range(0f, 0.99f)] [SerializeField] private float overloadThreshold = 0.8f;
    [Range(0f, 1f)] [SerializeField] private float baseOverloadProbability = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float overloadMigrationFraction = 0.2f;
    [Min(0)] [SerializeField] private int migrationCooldownDays = 7;

    private struct MovePlan
    {
        public BlockInfo source;
        public BlockInfo target;
        public PopulationData population;
        public int amount;
    }

    public event System.Action<BlockInfo, BlockInfo, PopulationData, int> OnPopulationMigrated;

    public void SetMigrationParameters(bool enabled, int interval, float populationScale,
        float threshold, float overloadProbability,
        float overloadFraction, int cooldownDays)
    {
        migrationEnabled = enabled;
        migrationInterval = Mathf.Max(1, interval);
        migrationPopulationScale = Mathf.Max(0.01f, populationScale);
        overloadThreshold = Mathf.Clamp(threshold, 0f, 0.99f);
        baseOverloadProbability = Mathf.Clamp01(overloadProbability);
        overloadMigrationFraction = Mathf.Clamp01(overloadFraction);
        migrationCooldownDays = Mathf.Max(0, cooldownDays);
    }

    public void NotifyEnvironmentChanged()
    {
        foreach (BlockInfo site in blockInfos)
        {
            if (site == null || site.community == null) continue;
            foreach (PopulationData population in site.community)
                if (population != null) population.lastMigrationSource = null;
        }
    }

    public void MovePopulation(BlockInfo source, BlockInfo target, PopulationData population,
        int day)
    {
        if (source == null || target == null || source == target || population == null ||
            source.community == null || !source.community.Contains(population) ||
            population.speciesAmount <= 0 || day < population.nextMigrationDay) return;
        // 生态位开启后，旧的陆地迁徙不能把陆生种群塞进河道地块。
        if (ecologicalNichesEnabled &&
            (source.waterCoverage != WaterCoverage.Land ||
             target.waterCoverage != WaterCoverage.Land ||
             population.ecologicalNiche != EcologicalNiche.Land)) return;
        int amount = LimitMigrationAmount(population.speciesAmount, 1f);
        if (amount == 0) return;
        ApplyMovePlans(new List<MovePlan>
        {
            new MovePlan
            {
                source = source, target = target, population = population,
                amount = amount
            }
        }, day);
    }

    public float EstimateTargetCapacity(BlockInfo target, PopulationData migrant)
    {
        if (target == null || migrant == null || target.community == null ||
            migrant.speciesAmount <= 0) return 0f;
        PopulationData resident = target.community.Find(population =>
            population != null && population.ecologicalNiche == migrant.ecologicalNiche &&
            SameSpecies(population, migrant));
        if (resident != null) return Mathf.Max(0f, resident.carryingCapacity);
        float fitness = CalculateFitness(target, migrant);
        return Mathf.Max(0f, migrant.speciesAmount
            * FoodWeb.FoodPerDemand(target, migrant.trophicLevel, migrant,
                reproductionScale, levelOnePreyFraction, levelTwoPreyFraction) * fitness);
    }

    public float TargetCapacityCorrection(BlockInfo target, PopulationData migrant)
    {
        float capacity = EstimateTargetCapacity(target, migrant);
        if (capacity <= 0f) return 0f;
        int residents = 0;
        foreach (PopulationData population in target.community)
            if (population != null && population.ecologicalNiche == migrant.ecologicalNiche &&
                SameSpecies(population, migrant))
                residents += Mathf.Max(0, population.speciesAmount);
        return Mathf.Clamp01(1f - residents / capacity);
    }

    public float CalculateOverloadPressure(PopulationData population)
    {
        if (population == null || population.speciesAmount <= 0) return 0f;
        if (population.carryingCapacity <= 0f) return 1f;
        float ratio = population.speciesAmount / population.carryingCapacity;
        return Mathf.Clamp01((ratio - overloadThreshold) / (1f - overloadThreshold));
    }

    public static int LimitMigrationAmount(int population, float proportion)
    {
        int amount = Mathf.Min(population, Mathf.RoundToInt(population * proportion));
        return amount < 10 ? 0 : Mathf.Min(amount, 100);
    }

    private struct MoveCandidate
    {
        public BlockInfo target;
        public float fitness;
        public float terrain;
        public float capacity;
    }

    private void RunMigration(int day)
    {
        List<MovePlan> plans = new List<MovePlan>();
        foreach (BlockInfo source in blockInfos)
        {
            if (source == null || source.community == null || source.Neighbors == null) continue;
            if (ecologicalNichesEnabled && source.waterCoverage != WaterCoverage.Land) continue;
            foreach (PopulationData population in source.community)
            {
                if (population == null || population.speciesAmount <= 0 ||
                    day < population.nextMigrationDay) continue;
                if (ecologicalNichesEnabled &&
                    population.ecologicalNiche != EcologicalNiche.Land) continue;
                if (TryPlanMove(source, population, plans, out MovePlan plan))
                    plans.Add(plan);
            }
        }
        ApplyMovePlans(plans, day);
    }

    private bool TryPlanMove(BlockInfo source, PopulationData population,
        List<MovePlan> planned, out MovePlan plan)
    {
        plan = new MovePlan();
        float currentFitness = CalculateFitness(source, population);
        MoveCandidate normal = new MoveCandidate { fitness = currentFitness };
        MoveCandidate overload = new MoveCandidate { fitness = -1f };

        foreach (BlockInfo neighbor in source.Neighbors)
        {
            if (neighbor == null || neighbor == source ||
                neighbor == population.lastMigrationSource) continue;
            if (ecologicalNichesEnabled &&
                neighbor.waterCoverage != WaterCoverage.Land) continue;
            int heightDifference = Mathf.Abs(neighbor.elevation - source.elevation);
            if (heightDifference >= 2) continue;
            float terrain = heightDifference == 0 ? 1f : TerrainStepProbability;
            if (terrain <= 0f) continue;
            float capacity = TargetCapacityCorrection(neighbor, population);
            if (capacity <= 0f) continue;
            float fitness = CalculateFitness(neighbor, population);

            if (fitness > currentFitness &&
                (fitness > normal.fitness ||
                 (fitness == normal.fitness && capacity > normal.capacity)))
                normal = new MoveCandidate
                {
                    target = neighbor, fitness = fitness, terrain = terrain,
                    capacity = capacity
                };

            bool occupied = neighbor.community.Exists(resident =>
                resident != null &&
                resident.ecologicalNiche == population.ecologicalNiche &&
                SameSpecies(resident, population)) || planned.Exists(move =>
                move.target == neighbor &&
                move.population.ecologicalNiche == population.ecologicalNiche &&
                SameSpecies(move.population, population));
            if (!occupied && (fitness > overload.fitness ||
                (fitness == overload.fitness && capacity > overload.capacity)))
                overload = new MoveCandidate
                {
                    target = neighbor, fitness = fitness, terrain = terrain,
                    capacity = capacity
                };
        }

        BlockInfo target = null;
        int amount = 0;
        float baseChance = population.speciesAmount /
            (population.speciesAmount + migrationPopulationScale);
        if (normal.target != null)
        {
            float gain = normal.fitness - currentFitness;
            float chance = gain * normal.terrain * baseChance * normal.capacity;
            if (Random.value < chance)
            {
                amount = LimitMigrationAmount(population.speciesAmount, gain);
                if (amount > 0) target = normal.target;
            }
        }
        if (target == null && overload.target != null)
        {
            float pressure = CalculateOverloadPressure(population);
            float chance = pressure * baseOverloadProbability *
                overload.terrain * overload.capacity;
            if (pressure > 0f && Random.value < chance)
            {
                amount = LimitMigrationAmount(population.speciesAmount,
                    pressure * overloadMigrationFraction);
                if (amount > 0) target = overload.target;
            }
        }

        if (target == null) return false;
        plan = new MovePlan
        {
            source = source, target = target, population = population, amount = amount
        };
        return true;
    }

    private void ApplyMovePlans(List<MovePlan> plans, int day)
    {
        // 先让所有种群离开，再统一落地，新来者就不会在同一天连续迁徙。
        List<PopulationData> arrivals = new List<PopulationData>(plans.Count);
        foreach (MovePlan plan in plans)
        {
            PopulationData origin = plan.population;
            PopulationData arrival = PopulationTransfer.Split(origin, plan.amount,
                plan.source, day, migrationCooldownDays);
            if (origin.speciesAmount == 0)
            {
                plan.source.community.Remove(origin);
            }
            arrivals.Add(arrival);
        }

        for (int i = 0; i < plans.Count; i++)
        {
            BlockInfo target = plans[i].target;
            if (target.community == null) target.community = new List<PopulationData>();
            PopulationData arrival = arrivals[i];
            PopulationData resident = target.community.Find(population =>
                population != null && population.ecologicalNiche == arrival.ecologicalNiche &&
                SameSpecies(population, arrival));
            if (resident == null)
            {
                target.community.Add(arrival);
            }
            else
            {
                InitializeMutationState(resident);
                PopulationTransfer.Merge(resident, arrival);
            }
            OnPopulationMigrated?.Invoke(plans[i].source, target,
                plans[i].population, plans[i].amount);
        }
    }

    public static bool SameSpecies(PopulationData left, PopulationData right) =>
        PopulationTransfer.IsSameSpecies(left, right);
}
