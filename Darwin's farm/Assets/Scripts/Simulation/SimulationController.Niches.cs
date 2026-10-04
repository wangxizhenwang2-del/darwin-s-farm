using System;
using System.Collections.Generic;
using UnityEngine;

public partial class SimulationController
{
    private const float NicheSplitFraction = 0.1f;
    private const int MinimumNicheSplit = 11;

    [Header("Ecological Niches (reserved; not connected to SimulateDay)")]
    [SerializeField] private bool ecologicalNichesEnabled = false;
    [Range(0f, 1f)] [SerializeField] private float landToWaterProbability = 0.02f;
    [Range(0f, 1f)] [SerializeField] private float landToAirProbability = 0.01f;
    [Range(0f, 1f)] [SerializeField] private float waterDownProbability = 0.05f;
    [Range(0f, 1f)] [SerializeField] private float waterUpProbability = 0.002f;
    [Range(0f, 1f)] [SerializeField] private float waterToLandProbability = 0.001f;

    public bool EcologicalNichesEnabled => ecologicalNichesEnabled;
    public event Action<BlockInfo, BlockInfo, PopulationData, PopulationData, int>
        OnNicheConverted;

    public void SetEcologicalNichesEnabled(bool enabled) => ecologicalNichesEnabled = enabled;

    public void SetEcologicalNicheProbabilities(float landToWater, float landToAir,
        float waterDown, float waterUp, float waterToLand)
    {
        landToWaterProbability = Mathf.Clamp01(landToWater);
        landToAirProbability = Mathf.Clamp01(landToAir);
        waterDownProbability = Mathf.Clamp01(waterDown);
        waterUpProbability = Mathf.Clamp01(waterUp);
        waterToLandProbability = Mathf.Clamp01(waterToLand);
    }

    public WaterRegionMap BuildWaterRegions() => WaterRegionMap.Build(blockInfos);

    public static float CalculateWaterFitness(WaterRegion region, PopulationData population)
    {
        if (region == null || population == null) return 0f;
        // 水生适应度只看水域平均温度；平均湿度可用于展示，不参与适应度。
        return Mathf.Clamp01(1f - Mathf.Abs(region.AverageTemperature -
            population.fitTemperature) / FitnessTolerance);
    }

    public static bool HasAirPopulation(BlockInfo block)
    {
        if (block == null || block.community == null) return false;
        foreach (PopulationData resident in block.community)
            if (resident != null && resident.speciesAmount > 0 &&
                resident.ecologicalNiche == EcologicalNiche.Air) return true;
        return false;
    }

    public static float WaterFoodPressure(WaterRegion region)
    {
        if (region == null) return 0f;
        float demand = 0f;
        foreach (BlockInfo block in region.Members)
        {
            if (block.community == null) continue;
            foreach (PopulationData population in block.community)
            {
                if (population == null || population.speciesAmount <= 0 ||
                    (population.ecologicalNiche != EcologicalNiche.Water &&
                     population.ecologicalNiche != EcologicalNiche.Air)) continue;
                demand += population.speciesAmount * EnergyNeedForNiche(population);
            }
        }
        if (demand <= 0f) return 0f;
        return region.AvailableFood <= 0f ? 1f :
            Mathf.Clamp01(demand / region.AvailableFood);
    }

    // 只在陆生种群已决定迁出后调用。返回 false 时由调用方继续原有陆地迁徙。
    // 独立入口目前没有接入 SimulateDay。
    public bool TryLandDepartureConversion(BlockInfo source, PopulationData population,
        int day, out PopulationData descendant)
    {
        descendant = null;
        if (!CanDepart(source, population, EcologicalNiche.Land, day) ||
            WaterRegionMap.IsWater(source) ||
            NicheSplitAmount(population.speciesAmount) == 0) return false;

        BlockInfo waterTarget = null;
        if (source.Neighbors != null)
            foreach (BlockInfo neighbor in source.Neighbors)
                if (WaterRegionMap.IsWater(neighbor) &&
                    (waterTarget == null || neighbor.algaeBiomass > waterTarget.algaeBiomass))
                    waterTarget = neighbor;
        if (waterTarget != null)
        {
            if (UnityEngine.Random.value < landToWaterProbability)
                return ConvertNiche(source, waterTarget, population, EcologicalNiche.Water,
                    day, out descendant);
            return false;
        }

        if (!HasAirPopulation(source) &&
            population.speciesAmount >= population.carryingCapacity &&
            UnityEngine.Random.value < landToAirProbability)
            return ConvertNiche(source, source, population, EcologicalNiche.Air,
                day, out descendant);
        return false;
    }

    // 同一 WaterRegion 内的位置调整是整个种群搬迁，不进行随机迁徙判定。
    public bool MoveWithinWaterRegion(BlockInfo source, BlockInfo target,
        PopulationData population, WaterRegionMap regions, int day)
    {
        if (!CanDepart(source, population, EcologicalNiche.Water, day) ||
            source == target || regions == null ||
            regions.GetRegion(source) == null ||
            regions.GetRegion(source) != regions.GetRegion(target)) return false;
        ApplyNicheMigration(source, target, population, population.speciesAmount, day);
        return true;
    }

    // 不同水域只允许相邻、海拔差为 1 的迁徙，且只分出一部分，不改变物种。
    public bool TryWaterMigration(BlockInfo source, BlockInfo target,
        PopulationData population, WaterRegionMap regions, int day)
    {
        if (!CanDepart(source, population, EcologicalNiche.Water, day) ||
            regions == null) return false;
        WaterRegion from = regions.GetRegion(source);
        WaterRegion to = regions.GetRegion(target);
        if (from == null || to == null || from == to ||
            !RegionsTouch(from, target) || target == population.lastMigrationSource)
            return false;
        int difference = to.Elevation - from.Elevation;
        if (Mathf.Abs(difference) != 1 || to.AvailableFood <= 0f ||
            WaterFoodPressure(to) >= 1f) return false;
        int amount = LimitMigrationAmount(population.speciesAmount,
            overloadMigrationFraction);
        float chance = (difference < 0 ? waterDownProbability : waterUpProbability)
            * WaterFoodPressure(from);
        if (amount == 0 || UnityEngine.Random.value >= chance) return false;
        ApplyNicheMigration(source, target, population, amount, day);
        return true;
    }

    public bool TryWaterToLandConversion(BlockInfo source, BlockInfo target,
        PopulationData population, WaterRegionMap regions, int day,
        out PopulationData descendant)
    {
        descendant = null;
        if (!CanDepart(source, population, EcologicalNiche.Water, day) ||
            regions == null || target == null || WaterRegionMap.IsWater(target) ||
            NicheSplitAmount(population.speciesAmount) == 0) return false;
        WaterRegion region = regions.GetRegion(source);
        if (region == null || !RegionsTouch(region, target) ||
            WaterFoodPressure(region) < 1f ||
            UnityEngine.Random.value >= waterToLandProbability) return false;
        return ConvertNiche(source, target, population, EcologicalNiche.Land,
            day, out descendant);
    }

    // 空中使用陆地的适应度、食物空间和部分迁徙规则，但忽略海拔。
    public bool TryAirMigration(BlockInfo source, BlockInfo target,
        PopulationData population, int day)
    {
        if (!CanDepart(source, population, EcologicalNiche.Air, day) ||
            !AreNeighbors(source, target) || target == population.lastMigrationSource ||
            HasAirPopulation(target) ||
            target.community == null) return false;
        float capacity = AirTargetCapacityCorrection(target, population);
        if (capacity <= 0f) return false;
        float gain = CalculateFitness(target, population) -
            CalculateFitness(source, population);
        float baseChance = population.speciesAmount /
            (population.speciesAmount + migrationPopulationScale);
        float pressure = CalculateOverloadPressure(population);
        float chance = gain > 0f ? gain * baseChance * capacity :
            pressure * baseOverloadProbability * capacity;
        float fraction = gain > 0f ? gain :
            pressure * overloadMigrationFraction;
        int amount = LimitMigrationAmount(population.speciesAmount, fraction);
        if (amount == 0 || UnityEngine.Random.value >= chance) return false;
        ApplyNicheMigration(source, target, population, amount, day);
        return true;
    }

    private bool CanDepart(BlockInfo source, PopulationData population,
        EcologicalNiche niche, int day) =>
        ecologicalNichesEnabled && source != null && source.community != null &&
        source.community.Contains(population) && population != null &&
        population.speciesAmount > 0 && population.ecologicalNiche == niche &&
        day >= population.nextMigrationDay;

    private static bool AreNeighbors(BlockInfo left, BlockInfo right)
    {
        if (left == null || right == null || left == right ||
            left.Neighbors == null) return false;
        foreach (BlockInfo neighbor in left.Neighbors)
            if (neighbor == right) return true;
        return false;
    }

    private static bool RegionsTouch(WaterRegion region, BlockInfo target)
    {
        if (region == null || target == null) return false;
        foreach (BlockInfo block in region.Members)
            if (AreNeighbors(block, target) || AreNeighbors(target, block)) return true;
        return false;
    }

    private static float EnergyNeedForNiche(PopulationData population) =>
        CalculateEnergyNeed(population.size, population.movementAbility);

    private float AirTargetCapacityCorrection(BlockInfo target, PopulationData migrant)
    {
        if (!WaterRegionMap.IsWater(target) || migrant.trophicLevel != 0)
            return TargetCapacityCorrection(target, migrant);
        // 空中种群在水面地块取用该 Block 的藻类，并与当地水生生产者消费者竞争。
        float food = Mathf.Min(Mathf.Max(0f, target.maxAlgaeBiomass),
            Mathf.Max(0f, target.algaeBiomass) + Mathf.Max(0, target.algaeRecovery));
        if (food <= 0f || target.community == null) return 0f;
        float demand = EnergyNeedForNiche(migrant) * migrant.speciesAmount;
        foreach (PopulationData competitor in target.community)
            if (competitor != null && competitor.speciesAmount > 0 &&
                competitor.trophicLevel == 0)
                demand += EnergyNeedForNiche(competitor) * competitor.speciesAmount;
        float capacity = migrant.speciesAmount * food / demand *
            CalculateFitness(target, migrant);
        return capacity > 0f ? 1f : 0f; // 空中槽位为空，目标地没有同槽位居民。
    }

    private static int NicheSplitAmount(int population)
    {
        int amount = Mathf.Min(population,
            Mathf.RoundToInt(population * NicheSplitFraction));
        return amount >= MinimumNicheSplit ? amount : 0;
    }

    private bool ConvertNiche(BlockInfo source, BlockInfo target,
        PopulationData ancestor, EcologicalNiche niche, int day,
        out PopulationData descendant)
    {
        descendant = null;
        int amount = NicheSplitAmount(ancestor.speciesAmount);
        if (amount == 0) return false;
        if (niche == EcologicalNiche.Air && HasAirPopulation(target)) return false;
        descendant = PopulationTransfer.Split(ancestor, amount, source, day,
            migrationCooldownDays);
        // 派生种使用独立 ID，不复用祖先物种资产；可随后保存成正式 SpeciesData。
        string id = Guid.NewGuid().ToString("N");
        descendant.species = null;
        descendant.speciesId = id;
        descendant.lineageName = (string.IsNullOrEmpty(ancestor.lineageName)
            ? "Species" : ancestor.lineageName) + "-" + id.Substring(0, 8);
        descendant.ecologicalNiche = niche;
        descendant.habitatNiche = niche == EcologicalNiche.Water ? 0 :
            niche == EcologicalNiche.Air ? 100 : 50;
        if (target.community == null) target.community = new List<PopulationData>();
        target.community.Add(descendant);
        OnNicheConverted?.Invoke(source, target, ancestor, descendant, amount);
        return true;
    }

    private void ApplyNicheMigration(BlockInfo source, BlockInfo target,
        PopulationData population, int amount, int day)
    {
        ApplyMovePlans(new List<MovePlan>
        {
            new MovePlan
            {
                source = source, target = target, population = population, amount = amount
            }
        }, day);
    }
}
