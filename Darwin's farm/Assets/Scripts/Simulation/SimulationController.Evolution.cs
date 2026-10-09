using System;
using System.Collections.Generic;
using UnityEngine;

// 预设物种的转化，以及本局中物种的解锁、灭绝和复兴状态。
public partial class SimulationController
{
    [Header("Preset Species Evolution")]
    [SerializeField] private bool presetEvolutionEnabled = true;
    private const int EvolutionTraitTolerance = 5;
    private const int EvolutionCooldownDays = 50;
    [SerializeField] private List<SpeciesData> evolutionNetworkSpecies =
        new List<SpeciesData>();

    private readonly HashSet<SpeciesData> knownSpecies = new HashSet<SpeciesData>();
    private readonly HashSet<SpeciesData> livingSpecies = new HashSet<SpeciesData>();
    public IReadOnlyCollection<SpeciesData> LivingSpecies => livingSpecies;
    public bool IsSpeciesLiving(SpeciesData species) =>
        species != null && livingSpecies.Contains(species);

    public event Action<BlockInfo, PopulationData, PopulationData, SpeciesData, int>
        OnSpeciesEvolved;
    public event Action<PopulationTransitionResult> OnPresetEvolutionResolved;
    public event Action<SpeciesData> OnSpeciesUnlocked;
    public event Action<SpeciesData> OnSpeciesExtinct;
    public event Action<SpeciesData> OnSpeciesRevived;

    public void SetPresetEvolutionParameters(bool enabled, int tolerance,
        float splitFraction, int minimumFounders)
    {
        presetEvolutionEnabled = enabled;
        // Retain the prototype API, but V4.1 fixes the tolerance, chance and
        // founder count rather than allowing those parameters to change rules.
    }

    // Include the whole graph when a starting species only has incoming edges.
    public void SetEvolutionNetworkSpecies(IEnumerable<SpeciesData> species)
    {
        evolutionNetworkSpecies = species == null ? new List<SpeciesData>() :
            new List<SpeciesData>(species);
        if (evolutionNetworkSpecies != null)
            foreach (SpeciesData entry in evolutionNetworkSpecies) RegisterSpecies(entry);
    }

    // 供玩家手动增删种群后同步解锁/灭绝状态。
    public void RefreshSpeciesStatuses() => UpdateSpeciesStatuses();

    private void RegisterSpecies(SpeciesData species)
    {
        if (species == null || !knownSpecies.Add(species)) return;
        if (species.evolutionTargets == null) return;
        foreach (SpeciesData target in species.evolutionTargets)
            RegisterSpecies(target);
    }

    private void UpdateSpeciesStatuses()
    {
        foreach (SpeciesData entry in evolutionNetworkSpecies) RegisterSpecies(entry);
        HashSet<SpeciesData> living = new HashSet<SpeciesData>();
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.community == null) continue;
            foreach (PopulationData population in block.community)
            {
                if (population == null || population.species == null) continue;
                RegisterSpecies(population.species);
                if (population.speciesAmount > 0) living.Add(population.species);
            }
        }
        livingSpecies.Clear();
        foreach (SpeciesData species in living) livingSpecies.Add(species);
        foreach (SpeciesData species in knownSpecies)
        {
            bool unlocked = species.HasEverUnlocked;
            bool extinct = species.IsExtinct;
            species.SetLivingPopulation(living.Contains(species));
            if (!unlocked && species.HasEverUnlocked) OnSpeciesUnlocked?.Invoke(species);
            else if (!extinct && species.IsExtinct) OnSpeciesExtinct?.Invoke(species);
            else if (extinct && !species.IsExtinct) OnSpeciesRevived?.Invoke(species);
        }
    }

    private sealed class EvolutionPlan
    {
        public BlockInfo block;
        public PopulationData ancestor;
        public SpeciesData target;
    }

    // 2026-10-09 13:48 +08:00: V4.3 land evolution uses daily, per-target
    // progress. Plan all successes before committing so list order cannot win
    // a contested target species by accident.
    private void RunPresetEvolution(int day)
    {
        if (!presetEvolutionEnabled || day <= 0) return;
        var plans = new List<EvolutionPlan>();
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.community == null ||
                block.waterCoverage != WaterCoverage.Land) continue;
            foreach (PopulationData ancestor in new List<PopulationData>(block.community))
            {
                SpeciesData target = SelectEvolutionTarget(block, ancestor, day);
                if (target == null) continue;
                EvolutionConversionRecord record = GetEvolutionRecord(ancestor, target, true);
                if (record.effectiveDays < int.MaxValue) record.effectiveDays++;
                if (record.effectiveDays % 2 != 0) continue;
                float chance = Mathf.Min(1f, (record.failedDraws + 1) / 100f);
                if (UnityEngine.Random.value >= chance)
                {
                    if (record.failedDraws < 99) record.failedDraws++;
                    continue;
                }
                plans.Add(new EvolutionPlan { block = block, ancestor = ancestor,
                    target = target });
            }
        }
        var resolved = new bool[plans.Count];
        for (int i = 0; i < plans.Count; i++)
        {
            if (resolved[i]) continue;
            EvolutionPlan winner = plans[i];
            int contenders = 0;
            for (int j = i; j < plans.Count; j++)
            {
                EvolutionPlan contender = plans[j];
                if (contender.block != plans[i].block ||
                    contender.target != plans[i].target) continue;
                resolved[j] = true;
                if (UnityEngine.Random.Range(0, ++contenders) == 0) winner = contender;
            }
            if (IsEligibleEvolutionTarget(winner.block, winner.ancestor,
                    winner.target, day))
                ConvertToPresetSpecies(winner.block, winner.ancestor,
                    winner.target, day);
        }
    }

    // Read-only forecast: uses exactly the daily selection, without changing
    // records or consuming random numbers.
    public SpeciesData GetLikelyEvolutionTarget(BlockInfo block, PopulationData source, int day)
        => SelectEvolutionTarget(block, source, day);

    private SpeciesData SelectEvolutionTarget(BlockInfo block, PopulationData source, int day)
    {
        if (!presetEvolutionEnabled || block == null || source == null ||
            block.community == null || !block.community.Contains(source) ||
            block.waterCoverage != WaterCoverage.Land ||
            source.ecologicalNiche != EcologicalNiche.Land ||
            source.species == null || source.species.baseEcologicalNiche != EcologicalNiche.Land ||
            source.speciesAmount < 11 || day < source.nextEvolutionDay) return null;

        SpeciesData best = null;
        float bestGrowth = float.NegativeInfinity;
        int bestDays = -1;
        foreach (SpeciesData target in ConnectedTargets(source.species))
        {
            if (!IsEligibleEvolutionTarget(block, source, target, day)) continue;
            float growth = ProjectedTargetGrowth(block, source, target);
            EvolutionConversionRecord record = GetEvolutionRecord(source, target, false);
            int days = record == null ? 0 : record.effectiveDays;
            if (best == null || growth > bestGrowth + 0.00001f ||
                Mathf.Abs(growth - bestGrowth) <= 0.00001f &&
                (days > bestDays || days == bestDays &&
                 CompareSpeciesOrder(target, best) < 0))
            {
                best = target; bestGrowth = growth; bestDays = days;
            }
        }
        return best;
    }

    private List<SpeciesData> ConnectedTargets(SpeciesData source)
    {
        var result = new HashSet<SpeciesData>();
        if (source.evolutionTargets != null)
            foreach (SpeciesData target in source.evolutionTargets)
                if (target != null && target != source) result.Add(target);
        foreach (SpeciesData candidate in knownSpecies)
            if (candidate != null && candidate != source &&
                candidate.evolutionTargets != null &&
                candidate.evolutionTargets.Contains(source)) result.Add(candidate);
        return new List<SpeciesData>(result);
    }

    private static string StableSpeciesKey(SpeciesData species) =>
        string.IsNullOrEmpty(species.SpeciesName) ? species.name : species.SpeciesName;

    private int CompareSpeciesOrder(SpeciesData left, SpeciesData right)
    {
        int leftId = evolutionNetworkSpecies == null ? -1 :
            evolutionNetworkSpecies.IndexOf(left);
        int rightId = evolutionNetworkSpecies == null ? -1 :
            evolutionNetworkSpecies.IndexOf(right);
        if (leftId >= 0 && rightId >= 0 && leftId != rightId)
            return leftId.CompareTo(rightId);
        int byName = string.CompareOrdinal(StableSpeciesKey(left), StableSpeciesKey(right));
        return byName != 0 ? byName : string.CompareOrdinal(left.name, right.name);
    }

    private static EvolutionConversionRecord GetEvolutionRecord(PopulationData source,
        SpeciesData target, bool create)
    {
        if (source.evolutionConversions == null)
        {
            if (!create) return null;
            source.evolutionConversions = new List<EvolutionConversionRecord>();
        }
        EvolutionConversionRecord record = source.evolutionConversions.Find(item =>
            item != null && item.target == target);
        if (record != null || !create) return record;
        record = new EvolutionConversionRecord { target = target };
        source.evolutionConversions.Add(record);
        return record;
    }

    private bool IsEligibleEvolutionTarget(BlockInfo block, PopulationData source,
        SpeciesData target, int day)
    {
        if (target == null || target == source.species ||
            target.baseEcologicalNiche != EcologicalNiche.Land ||
            day < source.nextEvolutionDay ||
            Mathf.Clamp(Mathf.RoundToInt(source.speciesAmount * 0.1f), 10, 100)
                >= source.speciesAmount ||
            !EachTraitWithinTolerance(source, target) ||
            HasLivingSpecies(block, target)) return false;
        var candidate = TargetPopulation(target, 10);
        if (candidate.trophicLevel == 0) return block.plantBiomass > 0f;
        foreach (PopulationData prey in block.community)
            if (prey != null && prey.speciesAmount > 0 &&
                FoodWeb.CanEat(candidate, prey)) return true;
        return false;
    }

    private static PopulationData TargetPopulation(SpeciesData target, int count) =>
        new PopulationData
        {
            species = target, speciesAmount = count,
            ecologicalNiche = EcologicalNiche.Land,
            trophicLevel = target.trophicLevel, trophicLevelInitialized = true,
            fitTemperature = target.baseFitTemperature,
            fitHumidity = target.baseFitHumidity,
            movementAbility = target.baseMovementAbility,
            size = target.baseSize, fertility = target.baseFertility
        };

    private float ProjectedTargetGrowth(BlockInfo block, PopulationData source,
        SpeciesData target)
    {
        int founders = Mathf.Clamp(Mathf.RoundToInt(source.speciesAmount * 0.1f), 10, 100);
        PopulationData candidate = TargetPopulation(target, founders);
        float fitness = ForecastFitness(block, candidate.fitTemperature, candidate.fitHumidity);
        float need = CalculateEnergyNeed(candidate.size, candidate.movementAbility,
            candidate.fertility);
        candidate.energyNeed = need;
        candidate.environmentalFitness = Mathf.RoundToInt(fitness * 100f);
        float foodRatio = FoodWeb.FoodPerDemand(block, candidate.trophicLevel, candidate,
            reproductionScale, levelOnePreyFraction, levelTwoPreyFraction);
        float capacity = candidate.trophicLevel == 0
            ? FoodWeb.ProjectedHerbivoreCapacity(block, candidate, candidate.size,
                candidate.movementAbility, fitness, candidate.fertility)
            : FoodWeb.ProjectedPredatorCapacity(block, candidate,
                candidate.size, candidate.movementAbility, fitness,
                candidate.fertility, reproductionScale, maxOverCapacityDeathRate,
                maxStarvationDeathRate, levelOnePreyFraction, levelTwoPreyFraction);
        float intake = Mathf.Clamp01(foodRatio);
        EstimateDemography(founders, candidate.fertility, fitness * 100f, need,
            intake * founders * need, capacity, reproductionScale,
            maxOverCapacityDeathRate, maxStarvationDeathRate *
                (candidate.trophicLevel > 0 ? PredatorStarvationMultiplier : 1f),
            out float births, out float deaths);
        float hunted = FoodWeb.ExpectedPreyLoss(block, candidate,
            candidate.movementAbility, candidate.size,
            levelOnePreyFraction, levelTwoPreyFraction);
        float densityDeaths = capacity > 0f ? births * founders / capacity : 0f;
        deaths -= Mathf.Min(hunted, densityDeaths) * DensityPredationCompensation;
        return births - deaths - hunted;
    }

    private bool EachTraitWithinTolerance(PopulationData source, SpeciesData target) =>
        Mathf.Abs(source.movementAbility - target.baseMovementAbility) <= EvolutionTraitTolerance &&
        Mathf.Abs(source.fitTemperature - target.baseFitTemperature) <= EvolutionTraitTolerance &&
        Mathf.Abs(source.fitHumidity - target.baseFitHumidity) <= EvolutionTraitTolerance &&
        Mathf.Abs(source.size - target.baseSize) <= EvolutionTraitTolerance &&
        Mathf.Abs(source.fertility - target.baseFertility) <= EvolutionTraitTolerance;

    private static bool HasLivingSpecies(BlockInfo block, SpeciesData species)
    {
        foreach (PopulationData population in block.community)
            if (population != null && population.species == species &&
                population.speciesAmount > 0) return true;
        return false;
    }

    private void ConvertToPresetSpecies(BlockInfo block, PopulationData ancestor,
        SpeciesData target, int day)
    {
        int amount = Mathf.Clamp(Mathf.RoundToInt(ancestor.speciesAmount * 0.1f), 10, 100);
        if (ancestor.speciesAmount - amount < 1) return;
        int sourceCountBefore = ancestor.speciesAmount;
        PopulationData descendant = PopulationTransfer.Split(ancestor, amount, block,
            day, migrationCooldownDays);
        SpeciesData origin = ancestor.species;
        descendant.species = target;
        descendant.speciesId = string.Empty;
        descendant.lineageName = target.SpeciesName;
        descendant.evolutionConversions.Clear();
        descendant.ecologicalNiche = target.baseEcologicalNiche;
        descendant.habitatNiche = target.baseHabitatNiche;
        descendant.trophicLevel = Mathf.Clamp(target.trophicLevel, 0, MaxTrophicLevel);
        descendant.trophicLevelInitialized = true;
        descendant.movementAbility = target.baseMovementAbility;
        descendant.fitTemperature = target.baseFitTemperature;
        descendant.fitHumidity = target.baseFitHumidity;
        descendant.size = Mathf.Clamp(target.baseSize, 1, 100);
        descendant.fertility = target.baseFertility;
        ClearMutationRemainders(descendant);
        descendant.energyReserve = 0f;
        descendant.nextEvolutionDay = day + EvolutionCooldownDays;

        ancestor.movementAbility = AverageTrait(ancestor.movementAbility,
            origin.baseMovementAbility, 0, 100);
        ancestor.fitTemperature = AverageTrait(ancestor.fitTemperature,
            origin.baseFitTemperature, 0, 100);
        ancestor.fitHumidity = AverageTrait(ancestor.fitHumidity,
            origin.baseFitHumidity, 0, 100);
        ancestor.size = AverageTrait(ancestor.size, origin.baseSize, 1, 100);
        ancestor.fertility = AverageTrait(ancestor.fertility,
            origin.baseFertility, 0, 100);
        ancestor.habitatNiche = origin.baseHabitatNiche;
        ancestor.ecologicalNiche = origin.baseEcologicalNiche;
        ancestor.trophicLevel = Mathf.Clamp(origin.trophicLevel, 0, MaxTrophicLevel);
        ancestor.trophicLevelInitialized = true;
        ClearMutationRemainders(ancestor);
        ancestor.energyReserve = 0f;
        ancestor.nextEvolutionDay = day + EvolutionCooldownDays;
        EvolutionConversionRecord record = GetEvolutionRecord(ancestor, target, true);
        record.effectiveDays = 0;
        record.failedDraws = 0;
        record.day = day;

        PopulationData resident = block.community.Find(population =>
            population != null && population != ancestor &&
            population.ecologicalNiche == descendant.ecologicalNiche &&
            PopulationTransfer.IsSameSpecies(population, descendant));
        int targetCountBefore = resident == null ? 0 : resident.speciesAmount;
        if (resident == null) block.community.Add(descendant);
        else PopulationTransfer.Merge(resident, descendant);
        RegisterSpecies(target);
        OnPresetEvolutionResolved?.Invoke(new PopulationTransitionResult(
            PopulationTransitionKind.PresetEvolution, day, block, block,
            ancestor, resident ?? descendant, amount, sourceCountBefore,
            targetCountBefore, resident == null ? PopulationArrivalOutcome.Created :
                PopulationArrivalOutcome.Merged, target));
        OnSpeciesEvolved?.Invoke(block, ancestor, resident ?? descendant, target, amount);
    }

    private static int AverageTrait(int current, int baseline, int minimum, int maximum) =>
        Mathf.Clamp(Mathf.RoundToInt((current + baseline) * 0.5f), minimum, maximum);

    private static void ClearMutationRemainders(PopulationData population)
    {
        population.temperatureMutationRemainder = 0f;
        population.humidityMutationRemainder = 0f;
        population.movementMutationRemainder = 0f;
        population.sizeMutationRemainder = 0f;
        population.fertilityMutationRemainder = 0f;
        population.trophicMutationRemainder = 0f;
    }

    // 性状变异、候选收益预测和小数余量。
    public const int MaxTrophicLevel = FoodWeb.MaxTrophicLevel;
    private const float MinimumMutationScore = 0.15f;

    [Header("Mutation")]
    [SerializeField] private bool mutationEnabled = true;
    [Min(1)] [SerializeField] private int mutationInterval = 7;
    [Min(0f)] [SerializeField] private float mutationStep = 2f;
    [Min(0f)] [SerializeField] private float selectionStrength = 1f;

    private enum MutationTrait { Temperature, Humidity, Movement, Size, Fertility, Trophic }

    private struct MutationDecision
    {
        public PopulationData population;
        public MutationTrait trait;
        public float score;
        public float expectedGain;
        public bool nicheSwitch;
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

    private void RetryMutationSoon(BlockInfo block)
    {
        if (block.community == null) return;
        foreach (PopulationData population in block.community)
            if (population != null)
                population.mutationDaysElapsed = Mathf.Max(1, mutationInterval) - 1;
    }

    private void EvolveCommunity(BlockInfo block)
    {
        List<MutationDecision> decisions = null;
        foreach (PopulationData population in block.community)
        {
            population.evolutionCheckReady = false;
            if (!mutationEnabled)
            {
                population.mutationDaysElapsed = 0;
                population.previousMutationPopulation = population.speciesAmount;
                continue;
            }
            population.mutationDaysElapsed++;
            if (population.mutationDaysElapsed < Mathf.Max(1, mutationInterval)) continue;
            population.mutationDaysElapsed = 0;
            population.evolutionCheckReady = population.speciesAmount > 0;
            if (population.speciesAmount > 0 &&
                TryChooseForecastMutation(block, population, out MutationDecision decision))
            {
                if (decisions == null) decisions = new List<MutationDecision>();
                decisions.Add(decision);
            }
            population.previousMutationPopulation = population.speciesAmount;
        }
        // 先看完整个群落，再动性状；这样先后顺序不会左右当天的选择。
        if (decisions == null) return;
        foreach (MutationDecision decision in decisions)
            ApplyMutationDecision(decision.population, decision.trait, decision.score,
                decision.nicheSwitch);
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

    private void ApplyMutationDecision(PopulationData population, MutationTrait trait,
        float score, bool nicheSwitch)
    {
        switch (trait)
        {
            case MutationTrait.Temperature:
                ChangeTrait(ref population.fitTemperature, ref population.temperatureMutationRemainder,
                    1f, score, 0, 100); break;
            case MutationTrait.Humidity:
                ChangeTrait(ref population.fitHumidity, ref population.humidityMutationRemainder,
                    1f, score, 0, 100); break;
            case MutationTrait.Movement:
                ChangeTrait(ref population.movementAbility, ref population.movementMutationRemainder,
                    1f, score, 0, 100); break;
            case MutationTrait.Size:
                ChangeTrait(ref population.size, ref population.sizeMutationRemainder,
                    nicheSwitch ? 1f : 0.1f, score, 1, 100); break;
            case MutationTrait.Fertility:
                ChangeTrait(ref population.fertility, ref population.fertilityMutationRemainder,
                    0.5f, score, 0, 100); break;
            case MutationTrait.Trophic:
                ChangeTrait(ref population.trophicLevel, ref population.trophicMutationRemainder,
                    0.2f, score, Mathf.Max(0, population.trophicLevel - 1),
                    Mathf.Min(MaxTrophicLevel, population.trophicLevel + 1)); break;
        }
    }

    private void ChangeTrait(ref int trait, ref float remainder, float multiplier,
        float score, int minimum, int maximum)
    {
        float change = Mathf.Sign(score) * mutationStep * multiplier * Mathf.Clamp01(Mathf.Abs(score));
        float value = Mathf.Clamp(trait + remainder + change, minimum, maximum);
        trait = Mathf.RoundToInt(value);
        remainder = value - trait;
    }

    // 候选性状的预期增长计算，与变异决策共用同一组参数。
    // 用现有的一步人口公式比较候选性状；只改变平均性状，不生成个体或新参数。
    private bool TryChooseForecastMutation(BlockInfo block, PopulationData population,
        out MutationDecision decision)
    {
        decision = new MutationDecision();
        if (population.speciesAmount <= 0 || population.energyNeed <= 0f ||
            mutationStep <= 0f || selectionStrength <= 0f) return false;

        float temperature = population.fitTemperature + population.temperatureMutationRemainder;
        float humidity = population.fitHumidity + population.humidityMutationRemainder;
        float movement = population.movementAbility + population.movementMutationRemainder;
        float size = population.size + population.sizeMutationRemainder;
        float fertility = population.fertility + population.fertilityMutationRemainder;
        float baseline = ForecastGrowth(block, population, temperature, humidity,
            movement, size, fertility);
        float reference = Mathf.Max(0.01f, population.speciesAmount
            * reproductionScale * mutationStep / 100f);
        MutationTrait bestTrait = MutationTrait.Temperature;
        float bestGain = 0f;
        int bestDirection = 0;
        bool nicheSwitch = false;

        for (int trait = 0; trait < 5; trait++)
        {
            // 体型与运动只在有捕食关系时参与普通选择；无捕食时仍可因竞争换赛道。
            if (((MutationTrait)trait == MutationTrait.Size ||
                 (MutationTrait)trait == MutationTrait.Movement) &&
                !HasTrophicInteraction(block, population)) continue;
            for (int direction = -1; direction <= 1; direction += 2)
            {
                float candidateTemperature = temperature;
                float candidateHumidity = humidity;
                float candidateMovement = movement;
                float candidateSize = size;
                float candidateFertility = fertility;
                switch ((MutationTrait)trait)
                {
                    case MutationTrait.Temperature:
                        candidateTemperature = Mathf.Clamp(temperature + direction * mutationStep, 0f, 100f); break;
                    case MutationTrait.Humidity:
                        candidateHumidity = Mathf.Clamp(humidity + direction * mutationStep, 0f, 100f); break;
                    case MutationTrait.Movement:
                        candidateMovement = Mathf.Clamp(movement + direction * mutationStep, 0f, 100f); break;
                    case MutationTrait.Size:
                        candidateSize = Mathf.Clamp(size + direction * mutationStep * 0.1f, 1f, 100f); break;
                    case MutationTrait.Fertility:
                        candidateFertility = Mathf.Clamp(fertility + direction * mutationStep * 0.5f, 0f, 100f); break;
                }
                if (candidateTemperature == temperature && candidateHumidity == humidity &&
                    candidateMovement == movement && candidateSize == size &&
                    candidateFertility == fertility) continue;

                float gain = ForecastGrowth(block, population, candidateTemperature,
                    candidateHumidity, candidateMovement, candidateSize,
                    candidateFertility) - baseline;
                if (gain <= bestGain + 0.00001f) continue;
                bestGain = gain;
                bestTrait = (MutationTrait)trait;
                bestDirection = direction;
                nicheSwitch = false;
            }
        }

        // 跨体型 8 的中间一步可能暂时不利，只有持续失势的少数种群才试算另一赛道。
        if (population.trophicLevel == 0 && IsDecliningSameTrackMinority(block, population))
        {
            float targetSize = size < 8f ? 10f : 6f;
            float alternativeCapacity = FoodWeb.ProjectedHerbivoreCapacity(block,
                population, targetSize, movement,
                ForecastFitness(block, temperature, humidity), fertility);
            if (alternativeCapacity > population.speciesAmount * 0.5f)
            {
                float gain = Mathf.Max(reference * 0.38f,
                    ForecastGrowth(block, population, temperature, humidity,
                        movement, targetSize, fertility) - baseline);
                if (gain > bestGain + 0.00001f)
                {
                    bestGain = gain;
                    bestTrait = MutationTrait.Size;
                    bestDirection = targetSize > size ? 1 : -1;
                    nicheSwitch = true;
                }
            }
        }

        // 一步增加 1 点生育能力所对应的预期出生量，作为无量纲评分单位。
        float score = Mathf.Min(1f, bestGain / reference * selectionStrength);
        if (bestDirection == 0 || score < MinimumMutationScore) return false;
        decision = new MutationDecision
        {
            population = population, trait = bestTrait,
            score = bestDirection * score, expectedGain = bestGain,
            nicheSwitch = nicheSwitch
        };
        return true;
    }

    public string GetExpectedEvolutionDirection(BlockInfo block, PopulationData population)
    {
        if (!mutationEnabled || mutationStep <= 0f) return "进化方向：已关闭";
        if (block == null || population == null || population.speciesAmount <= 0)
            return "进化方向：无存活个体";
        if (population.energyNeed <= 0f) return "进化方向：待首日计算";
        if (!TryChooseForecastMutation(block, population, out MutationDecision decision))
            return "进化方向：暂无线性优势";
        string name = decision.trait == MutationTrait.Temperature ? "适温" :
            decision.trait == MutationTrait.Humidity ? "适湿" :
            decision.trait == MutationTrait.Movement ? "运动" :
            decision.trait == MutationTrait.Size ? "体型" : "生育";
        return "进化方向：" + name + (decision.score > 0f ? " ↑" : " ↓")
            + "（预计净增长改善 +" + decision.expectedGain.ToString("F2") + "/日）";
    }

    private static float ForecastFitness(BlockInfo block, float temperature, float humidity) =>
        Mathf.Clamp01(1f - (Mathf.Abs(block.temperature - temperature)
            + Mathf.Abs(block.humidity - humidity)) / FitnessTolerance);

    private float ForecastGrowth(BlockInfo block, PopulationData population,
        float temperature, float humidity, float movement, float size,
        float fertility)
    {
        int count = population.speciesAmount;
        float fitness = ForecastFitness(block, temperature, humidity);
        float need = CalculateEnergyNeed(size, movement, fertility);
        float capacity = population.trophicLevel == 0
            ? FoodWeb.ProjectedHerbivoreCapacity(block, population, size,
                movement, fitness, fertility)
            : FoodWeb.ProjectedPredatorCapacity(block, population, size,
                movement, fitness, fertility, reproductionScale,
                maxOverCapacityDeathRate, maxStarvationDeathRate,
                levelOnePreyFraction, levelTwoPreyFraction);

        // 已有实际摄食率提供当天的起点，K 的变化提供候选性状对食物份额的影响。
        float intake = Mathf.Clamp01(population.actualEnergySatisfactionToday
            + (capacity - population.carryingCapacity)
            / Mathf.Max(1f, count));
        if (population.trophicLevel == 0 && block.plantBiomass >
            count * need / Mathf.Max(0.1f, fitness))
            intake = Mathf.Max(intake, population.actualEnergySatisfactionToday);
        EstimateDemography(count, fertility, fitness * 100f, need,
            intake * count * need, capacity, reproductionScale,
            maxOverCapacityDeathRate, maxStarvationDeathRate *
                (population.trophicLevel > 0 ? PredatorStarvationMultiplier : 1f),
            out float births, out float deaths);
        float hunted = FoodWeb.ExpectedPreyLoss(block, population, movement, size,
            levelOnePreyFraction, levelTwoPreyFraction);
        float densityDeaths = capacity > 0f ? births * count / capacity : 0f;
        deaths -= Mathf.Min(hunted, densityDeaths) * DensityPredationCompensation;
        return births - deaths - hunted;
    }

    private static bool IsDecliningSameTrackMinority(BlockInfo block,
        PopulationData population)
    {
        if (population.speciesAmount >= population.previousMutationPopulation)
            return false;
        float ownForaging = Mathf.Clamp01(population.environmentalFitness / 100f)
            * (0.75f + population.movementAbility * 0.0025f);
        foreach (PopulationData other in block.community)
            if (other != population && other.speciesAmount > population.speciesAmount &&
                other.trophicLevel == 0 &&
                (other.size < 8) == (population.size < 8) &&
                Mathf.Clamp01(other.environmentalFitness / 100f)
                    * (0.75f + other.movementAbility * 0.0025f)
                    > ownForaging + 0.001f)
                return true;
        return false;
    }

    private static bool HasTrophicInteraction(BlockInfo block, PopulationData population)
    {
        foreach (PopulationData other in block.community)
            if (other != population && other.speciesAmount > 0 &&
                (FoodWeb.CanEat(other, population) ||
                 FoodWeb.CanEat(population, other)))
                return true;
        return false;
    }
}
