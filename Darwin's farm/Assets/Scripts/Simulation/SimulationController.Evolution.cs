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
    public SpeciesData FindSpeciesByEconomyId(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (SpeciesData species in evolutionNetworkSpecies) RegisterSpecies(species);
        foreach (SpeciesData species in knownSpecies)
            if (species != null && species.EconomyId == key) return species;
        return null;
    }

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

    // Consecutive qualified days replace the old probabilistic draw.
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
                if (ancestor == null || ancestor.species == null) continue;
                SpeciesData selected = null;
                foreach (SpeciesData target in ConnectedTargets(ancestor.species))
                {
                    EvolutionConversionRecord record = GetEvolutionRecord(ancestor,
                        target, true);
                    if (ancestor.directedSplitTarget != null &&
                        target != ancestor.directedSplitTarget)
                    { record.effectiveDays = 0; continue; }
                    if (!IsEligibleEvolutionTarget(block, ancestor, target, day))
                    { record.effectiveDays = 0; continue; }
                    if (record.effectiveDays < int.MaxValue) record.effectiveDays++;
                    bool wholeBranch = IsLocalSplitPair(block, ancestor);
                    int required = LivingPopulationCount(block) == 4 && !wholeBranch
                        ? LandRules.evolutionRequiredDaysAtFour
                        : LandRules.evolutionRequiredDays;
                    if (record.effectiveDays >= required &&
                        (selected == null || CompareSpeciesOrder(target, selected) < 0))
                        selected = target;
                }
                if (selected != null)
                    plans.Add(new EvolutionPlan { block = block,
                        ancestor = ancestor, target = selected });
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
            source.speciesAmount < 10 || day < source.nextEvolutionDay) return null;

        SpeciesData best = null;
        float bestGrowth = float.NegativeInfinity;
        int bestDays = -1;
        foreach (SpeciesData target in ConnectedTargets(source.species))
        {
            if (!IsEligibleEvolutionTarget(block, source, target, day)) continue;
            float growth = 0f;
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
        // Imported assets encode some intended edges in reverse direction.
        // A connection is valid in either direction; template and food gates
        // still decide whether a conversion can occur.
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
        if (block == null || source == null || target == null ||
            target == source.species || source.species == null ||
            source.species.baseEcologicalNiche != target.baseEcologicalNiche ||
            target.baseEcologicalNiche != EcologicalNiche.Land ||
            day < source.nextEvolutionDay ||
            !EachTraitWithinTolerance(source, target) ||
            HasLivingSpecies(block, target)) return false;
        bool wholeBranch = IsLocalSplitPair(block, source);
        if (!wholeBranch && (LivingPopulationCount(block) >= 5 ||
            source.speciesAmount <= 10)) return false;
        int amount = wholeBranch ? source.speciesAmount :
            Mathf.Clamp(Mathf.RoundToInt(source.speciesAmount * 0.1f), 10, 100);
        if (!wholeBranch && amount >= source.speciesAmount) return false;
        return HasEvolutionFoodSupport(block, source, target, amount, wholeBranch);
    }

    private bool HasEvolutionFoodSupport(BlockInfo block, PopulationData source,
        SpeciesData target, int amount, bool wholeBranch)
    {
        PopulationData candidate = TargetPopulation(target, amount);
        float fitness = LandLifeRules.Fitness(block,
            candidate.fitTemperature, candidate.fitHumidity);
        if (candidate.trophicLevel == 0)
        {
            int remaining = source.trophicLevel == 0
                ? source.speciesAmount - amount : source.speciesAmount;
            LandLifeRules.ForecastEvolutionPlantSupport(block, source,
                candidate, remaining, LandRules, out float demand,
                out float today, out float renewal);
            if (demand <= 0f) return false;
            return today / demand >= LandRules.evolutionTodaySatisfaction
                && renewal / demand >= LandRules.evolutionRenewalSatisfaction;
        }
        float predatorDemand = LandLifeRules.Demand(candidate, amount, fitness,
            LandRules);
        if (predatorDemand <= 0f) return false;
        float expectedEnergy = 0f;
        float totalPreference = 0f;
        foreach (PopulationData prey in block.community)
        {
            if (prey == null || prey.speciesAmount <= 0 ||
                !FoodWeb.CanEat(candidate, prey)) continue;
            int available = prey.speciesAmount;
            if (prey == source) available = wholeBranch ? 0 :
                Mathf.Max(0, available - amount);
            if (available <= 0) continue;
            totalPreference += LandLifeRules.HuntPreference(candidate,
                prey, LandRules);
        }
        if (totalPreference <= 0f) return false;
        foreach (PopulationData prey in block.community)
        {
            if (prey == null || prey.speciesAmount <= 0 ||
                !FoodWeb.CanEat(candidate, prey)) continue;
            int available = prey == source
                ? (wholeBranch ? 0 : Mathf.Max(0, prey.speciesAmount - amount))
                : prey.speciesAmount;
            if (available <= 0) continue;
            float energy = LandLifeRules.PreyEnergy(candidate, prey,
                LandRules);
            float attempt = Mathf.Min(1f,
                10f / Mathf.Max(1f, prey.size))
                * LandLifeRules.CaptureChance(candidate, prey, LandRules);
            float candidateKills = amount *
                LandLifeRules.HuntPreference(candidate, prey, LandRules)
                / totalPreference * attempt;
            float claim = LandLifeRules.WholePreyClaim(candidateKills,
                Mathf.Max(0f, predatorDemand - expectedEnergy), energy);
            float expected = Mathf.Min(available,
                claim + prey.predationRemainder);
            int wholeCatches = Mathf.Min(available,
                Mathf.FloorToInt(expected));
            expectedEnergy += wholeCatches * energy;
        }
        float sustainableEnergy = LandLifeRules.AvailableSustainablePreyFood(
            block, candidate, LandRules, replaced: source,
            replacedCount: wholeBranch ? 0 : source.speciesAmount - amount);
        return Mathf.Min(predatorDemand, expectedEnergy) / predatorDemand
                >= LandRules.evolutionTodaySatisfaction
            && Mathf.Min(predatorDemand, sustainableEnergy) / predatorDemand
                >= LandRules.evolutionRenewalSatisfaction
            && sustainableEnergy >= LandRules.evolutionPreyRenewalCoverage
                * predatorDemand;
    }

    private static bool IsLocalSplitPair(BlockInfo block, PopulationData source)
    {
        if (!source.localSplitBranch) return false;
        foreach (PopulationData other in block.community)
            if (other != source && other != null && other.localSplitBranch &&
                PopulationTransfer.IsSameSpecies(source, other)) return true;
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

    private bool EachTraitWithinTolerance(PopulationData source, SpeciesData target) =>
        Mathf.Abs(source.movementAbility - target.baseMovementAbility) <= LandRules.evolutionTraitTolerance &&
        Mathf.Abs(source.fitTemperature - target.baseFitTemperature) <= LandRules.evolutionTraitTolerance &&
        Mathf.Abs(source.fitHumidity - target.baseFitHumidity) <= LandRules.evolutionTraitTolerance &&
        Mathf.Abs(source.size - target.baseSize) <= LandRules.evolutionTraitTolerance &&
        Mathf.Abs(source.fertility - target.baseFertility) <= LandRules.evolutionTraitTolerance;

    private static bool HasLivingSpecies(BlockInfo block, SpeciesData species)
    {
        foreach (PopulationData population in block.community)
            if (population != null && population.speciesAmount > 0 &&
                population.species == species) return true;
        return false;
    }

    internal static int LivingPopulationCount(BlockInfo block)
    {
        if (block == null || block.community == null) return 0;
        int count = 0;
        foreach (PopulationData population in block.community)
            if (population != null && population.speciesAmount > 0) count++;
        return count;
    }

    private void ConvertToPresetSpecies(BlockInfo block, PopulationData ancestor,
        SpeciesData target, int day)
    {
        bool wholeBranch = IsLocalSplitPair(block, ancestor);
        int amount = wholeBranch ? ancestor.speciesAmount :
            Mathf.Clamp(Mathf.RoundToInt(ancestor.speciesAmount * 0.1f), 10, 100);
        if (!wholeBranch && ancestor.speciesAmount - amount < 1) return;
        int sourceCountBefore = ancestor.speciesAmount;
        PopulationData descendant = wholeBranch ? ancestor :
            PopulationTransfer.Split(ancestor, amount, block,
                day, migrationCooldownDays);
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
        descendant.energyReserve = Mathf.Min(descendant.energyReserve,
            amount * LandLifeRules.AdultNeed(descendant.size,
                descendant.movementAbility, LandRules) * LandRules.reserveDays);
        descendant.nextEvolutionDay = day + LandRules.evolutionCooldownDays;

        descendant.localSplitBranch = false;
        descendant.directedSplitTarget = null;
        descendant.directedTraitUnlockDay = 0;
        descendant.sameSpeciesCoexistenceDays = 0;
        descendant.splitStrategy = descendant.trophicLevel == 0 &&
            target.baseSize >= 25 ? 1 : 0;
        descendant.mutationSelectionProgress = 0f;
        descendant.mutationSelectionCandidate = -1;
        descendant.lastMutationEffect = "转化为" + target.SpeciesName;
        if (!wholeBranch)
        {
            ancestor.nextEvolutionDay = day + LandRules.evolutionCooldownDays;
            EvolutionConversionRecord record = GetEvolutionRecord(ancestor, target, true);
            record.effectiveDays = 0;
            record.failedDraws = 0;
            record.day = day;
        }

        PopulationData resident = block.community.Find(population =>
            population != null && population != ancestor &&
            population.ecologicalNiche == descendant.ecologicalNiche &&
            PopulationTransfer.IsSameSpecies(population, descendant));
        int targetCountBefore = resident == null ? 0 : resident.speciesAmount;
        if (resident == null && !wholeBranch) block.community.Add(descendant);
        else if (resident != null) PopulationTransfer.Merge(resident, descendant);
        LandLifeRules.RefreshEnvironmentMetrics(block, LandRules);
        RegisterSpecies(target);
        OnPresetEvolutionResolved?.Invoke(new PopulationTransitionResult(
            PopulationTransitionKind.PresetEvolution, day, block, block,
            ancestor, resident ?? descendant, amount, sourceCountBefore,
            targetCountBefore, resident == null ? PopulationArrivalOutcome.Created :
                PopulationArrivalOutcome.Merged, target));
        OnSpeciesEvolved?.Invoke(block, ancestor, resident ?? descendant, target, amount);
        LogEcology(day, block, descendant, "preset_evolution",
            "target=" + target.SpeciesName + " amount=" + amount
            + " wholeBranch=" + wholeBranch + " count=" + descendant.speciesAmount);
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
    [Header("Mutation")]
    [SerializeField] private bool mutationEnabled = true;
    [Min(1)] [SerializeField] private int mutationInterval = 1;
    [Min(0f)] [SerializeField] private float mutationStep = 1f;
    [Min(0f)] [SerializeField] private float selectionStrength = 1f;

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

    public string GetExpectedEvolutionDirection(BlockInfo block, PopulationData population)
    {
        return GetLandTraitDirection(block, population);
    }

}
