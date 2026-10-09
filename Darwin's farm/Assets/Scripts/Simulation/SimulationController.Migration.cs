using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

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

    // V4.1 natural migration uses a fixed 10% split regardless of why it moved.
    public static int StandardMigrationAmount(int population) =>
        population < 10 ? 0 : Mathf.Min(population,
            Mathf.Clamp(Mathf.RoundToInt(population * 0.1f), 10, 100));

    private struct MoveCandidate
    {
        public BlockInfo target;
        public float chance;
        public float tieBreaker;
    }

    private void RunMigration(int day)
    {
        List<MovePlan> plans = new List<MovePlan>();
        foreach (BlockInfo source in blockInfos)
        {
            if (source == null || source.community == null || source.Neighbors == null) continue;
            if (source.waterCoverage != WaterCoverage.Land) continue;
            foreach (PopulationData population in source.community)
            {
                if (population == null || population.speciesAmount <= 0 ||
                    day < population.nextMigrationDay) continue;
                if (population.ecologicalNiche != EcologicalNiche.Land) continue;
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
        int amount = StandardMigrationAmount(population.speciesAmount);
        if (amount == 0) return false;
        float currentFitness = CalculateFitness(source, population);
        float pressure = CalculateOverloadPressure(population);
        float baseChance = population.speciesAmount /
            (population.speciesAmount + migrationPopulationScale);
        List<MoveCandidate> candidates = new List<MoveCandidate>();

        foreach (BlockInfo neighbor in source.Neighbors)
        {
            if (neighbor == null || neighbor == source ||
                neighbor == population.lastMigrationSource) continue;
            if (neighbor.waterCoverage != WaterCoverage.Land ||
                neighbor.community == null) continue;
            int heightDifference = Mathf.Abs(neighbor.elevation - source.elevation);
            if (heightDifference >= 2) continue;
            float terrain = heightDifference == 0 ? 1f : TerrainStepProbability;
            if (terrain <= 0f) continue;
            float capacity = TargetCapacityCorrection(neighbor, population);
            if (capacity <= 0f) continue;
            float fitness = CalculateFitness(neighbor, population);

            float normalChance = fitness > currentFitness
                ? (fitness - currentFitness) * terrain * baseChance * capacity : 0f;

            bool occupied = neighbor.community.Exists(resident =>
                resident != null &&
                resident.ecologicalNiche == population.ecologicalNiche &&
                SameSpecies(resident, population)) || planned.Exists(move =>
                move.target == neighbor &&
                move.population.ecologicalNiche == population.ecologicalNiche &&
                SameSpecies(move.population, population));
            float overloadChance = !occupied && pressure > 0f
                ? pressure * baseOverloadProbability * terrain * capacity : 0f;
            float chance = Mathf.Max(normalChance, overloadChance);
            if (chance <= 0f) continue;
            candidates.Add(new MoveCandidate
            {
                target = neighbor, chance = Mathf.Clamp01(chance),
                tieBreaker = Random.value
            });
        }

        candidates.Sort((left, right) =>
        {
            int byChance = right.chance.CompareTo(left.chance);
            return byChance != 0 ? byChance : right.tieBreaker.CompareTo(left.tieBreaker);
        });
        foreach (MoveCandidate candidate in candidates)
        {
            if (Random.value >= candidate.chance) continue;
            plan = new MovePlan
            {
                source = source, target = candidate.target,
                population = population, amount = amount
            };
            return true;
        }
        return false;
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

    // 海陆空生态位与河岸位置。
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
    public event Action<PopulationData, BlockInfo, RiverBankSide?> OnLandPositionChanged;

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

    // 河岸是陆地栖息地的一个位置，不拥有独立种群或食物库存。
    public bool CanMoveToRiverBank(BlockInfo home, BlockInfo river,
        RiverBankSide side, PopulationData population)
    {
        if (!ecologicalNichesEnabled || home == null || home.community == null ||
            population == null || population.speciesAmount <= 0 ||
            population.ecologicalNiche != EcologicalNiche.Land ||
            !home.community.Contains(population)) return false;
        return HabitatTopology.TryGetBankOwner(river, side, out BlockInfo owner) &&
            owner == home;
    }

    public bool TryMoveToRiverBank(BlockInfo home, BlockInfo river,
        RiverBankSide side, PopulationData population)
    {
        if (!CanMoveToRiverBank(home, river, side, population)) return false;
        if (population.landPositionBlock == river &&
            population.landPositionBank == side) return false;
        population.landPositionBlock = river;
        population.landPositionBank = side;
        OnLandPositionChanged?.Invoke(population, river, side);
        return true;
    }

    public bool TryReturnFromRiverBank(BlockInfo home, PopulationData population)
    {
        if (!ecologicalNichesEnabled || home == null || home.community == null ||
            population == null || population.ecologicalNiche != EcologicalNiche.Land ||
            population.speciesAmount <= 0 ||
            !home.community.Contains(population) ||
            population.landPositionBlock == null) return false;
        population.landPositionBlock = null;
        population.landPositionBank = default(RiverBankSide);
        OnLandPositionChanged?.Invoke(population, home, null);
        return true;
    }

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
        foreach (BlockInfo neighbor in blockInfos)
            if (HabitatTopology.CanLandEnterWater(source, neighbor) &&
                (population.landPositionBlock == null ||
                 population.landPositionBlock == neighbor) &&
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
            !WaterRegionsTouch(from, target) || target == population.lastMigrationSource)
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
        if (region == null || !WaterRegionTouchesLand(region, target) ||
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

    private static bool WaterRegionsTouch(WaterRegion region, BlockInfo target)
    {
        if (region == null || target == null) return false;
        foreach (BlockInfo block in region.Members)
            if (HabitatTopology.ChannelsConnect(block, target)) return true;
        return false;
    }

    private static bool WaterRegionTouchesLand(WaterRegion region, BlockInfo land)
    {
        if (region == null || land == null) return false;
        foreach (BlockInfo block in region.Members)
            if (HabitatTopology.CanLandEnterWater(land, block)) return true;
        return false;
    }

    private static float EnergyNeedForNiche(PopulationData population) =>
        CalculateEnergyNeed(population.size, population.movementAbility,
            population.fertility);

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

// 迁徙与生态位转化共用的种群拆分、复制和合并。
internal static class PopulationTransfer
{
    public static PopulationData CopyForMove(PopulationData source, int amount)
    {
        // 不复制河岸位置；迁入者落在新栖息地，留下的个体仍在原河岸。
        PopulationData copy = new PopulationData
        {
            species = source.species,
            lineageName = source.lineageName,
            speciesId = source.speciesId,
            ecologicalNiche = source.ecologicalNiche,
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
            nextMigrationDay = source.nextMigrationDay,
            nextEvolutionDay = source.nextEvolutionDay,
            previousMutationPopulation = amount,
            mutationPopulationInitialized = true,
            evolutionCheckReady = source.evolutionCheckReady
        };
        if (source.evolutionConversions != null)
            foreach (EvolutionConversionRecord record in source.evolutionConversions)
                if (record != null)
                    copy.evolutionConversions.Add(new EvolutionConversionRecord
                    {
                        target = record.target, day = record.day,
                        effectiveDays = record.effectiveDays,
                        failedDraws = record.failedDraws
                    });
        return copy;
    }

    // 迁徙和生态位分化都从这里拆分个体，储备与不足一个体的小数也按比例带走。
    public static PopulationData Split(PopulationData origin, int amount,
        BlockInfo source, int day, int cooldownDays)
    {
        float share = amount / (float)origin.speciesAmount;
        PopulationData branch = CopyForMove(origin, amount);
        branch.lastMigrationSource = source;
        origin.nextMigrationDay = Mathf.Max(origin.nextMigrationDay,
            day + cooldownDays + 1);
        branch.nextMigrationDay = origin.nextMigrationDay;
        branch.energyReserve = origin.energyReserve * share;
        branch.birthRemainder = origin.birthRemainder * share;
        branch.deathRemainder = origin.deathRemainder * share;
        origin.energyReserve -= branch.energyReserve;
        origin.birthRemainder -= branch.birthRemainder;
        origin.deathRemainder -= branch.deathRemainder;
        origin.speciesAmount -= amount;
        // 2026-10-09 13:47 +08:00: a newly split migration branch starts with
        // zero target progress; its inherited evolution cooldown still applies.
        if (origin.speciesAmount > 0) branch.evolutionConversions.Clear();
        return branch;
    }

    public static void Merge(PopulationData resident, PopulationData arrival)
    {
        int residentAmount = resident.speciesAmount;
        int arrivalAmount = arrival.speciesAmount;
        int total = residentAmount + arrivalAmount;
        MergeTrait(ref resident.movementAbility, ref resident.movementMutationRemainder,
            arrival.movementAbility, arrival.movementMutationRemainder,
            residentAmount, arrivalAmount, total);
        resident.habitatNiche = WeightedTrait(resident.habitatNiche, residentAmount,
            arrival.habitatNiche, arrivalAmount, total);
        MergeTrait(ref resident.fitTemperature, ref resident.temperatureMutationRemainder,
            arrival.fitTemperature, arrival.temperatureMutationRemainder,
            residentAmount, arrivalAmount, total);
        MergeTrait(ref resident.fitHumidity, ref resident.humidityMutationRemainder,
            arrival.fitHumidity, arrival.humidityMutationRemainder,
            residentAmount, arrivalAmount, total);
        MergeTrait(ref resident.size, ref resident.sizeMutationRemainder,
            arrival.size, arrival.sizeMutationRemainder,
            residentAmount, arrivalAmount, total);
        MergeTrait(ref resident.fertility, ref resident.fertilityMutationRemainder,
            arrival.fertility, arrival.fertilityMutationRemainder,
            residentAmount, arrivalAmount, total);
        MergeTrait(ref resident.trophicLevel, ref resident.trophicMutationRemainder,
            arrival.trophicLevel, arrival.trophicMutationRemainder,
            residentAmount, arrivalAmount, total);
        resident.energyReserve += arrival.energyReserve;
        resident.birthRemainder += arrival.birthRemainder;
        resident.deathRemainder += arrival.deathRemainder;
        resident.nextMigrationDay = Mathf.Max(resident.nextMigrationDay,
            arrival.nextMigrationDay);
        resident.nextEvolutionDay = Mathf.Max(resident.nextEvolutionDay,
            arrival.nextEvolutionDay);
        resident.lastMigrationSource = arrival.lastMigrationSource;
        resident.speciesAmount = total;
        resident.previousMutationPopulation = total;
        resident.mutationPopulationInitialized = true;
        resident.trophicLevelInitialized = true;
        resident.evolutionCheckReady |= arrival.evolutionCheckReady;
        // V4.3: the resident keeps its target records. Arrival records never add
        // progress to them; only the longer outstanding cooldown survives.
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

    private static void MergeTrait(ref int resident, ref float remainder,
        int arrival, float arrivalRemainder, int residentAmount, int arrivalAmount, int total)
    {
        float average = WeightedValue(resident + remainder, residentAmount,
            arrival + arrivalRemainder, arrivalAmount, total);
        resident = Mathf.RoundToInt(average);
        remainder = average - resident;
    }

    public static bool IsSameSpecies(PopulationData left, PopulationData right)
    {
        if (left == null || right == null) return false;
        if (!string.IsNullOrEmpty(left.speciesId) || !string.IsNullOrEmpty(right.speciesId))
            return !string.IsNullOrEmpty(left.speciesId) &&
                string.Equals(left.speciesId, right.speciesId,
                    System.StringComparison.Ordinal);
        if (left.species != null || right.species != null)
            return left.species != null && left.species == right.species;
        return !string.IsNullOrWhiteSpace(left.lineageName) &&
            string.Equals(left.lineageName, right.lineageName,
                System.StringComparison.Ordinal);
    }
}
