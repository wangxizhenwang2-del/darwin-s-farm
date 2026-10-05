using System;
using System.Collections.Generic;
using UnityEngine;

// 预设物种的转化，以及本局中物种的解锁、灭绝和复兴状态。
public partial class SimulationController
{
    [Header("Preset Species Evolution")]
    [SerializeField] private bool presetEvolutionEnabled = true;
    [Min(0)] [SerializeField] private int evolutionTraitTolerance = 5;
    [Range(0f, 1f)] [SerializeField] private float evolutionSplitFraction = 0.1f;
    [Min(1)] [SerializeField] private int minimumEvolutionFounders = 5;

    private readonly HashSet<SpeciesData> knownSpecies = new HashSet<SpeciesData>();
    private readonly HashSet<SpeciesData> livingSpecies = new HashSet<SpeciesData>();
    public IReadOnlyCollection<SpeciesData> LivingSpecies => livingSpecies;
    public bool IsSpeciesLiving(SpeciesData species) =>
        species != null && livingSpecies.Contains(species);

    public event Action<BlockInfo, PopulationData, PopulationData, SpeciesData, int>
        OnSpeciesEvolved;
    public event Action<SpeciesData> OnSpeciesUnlocked;
    public event Action<SpeciesData> OnSpeciesExtinct;
    public event Action<SpeciesData> OnSpeciesRevived;

    public void SetPresetEvolutionParameters(bool enabled, int tolerance,
        float splitFraction, int minimumFounders)
    {
        presetEvolutionEnabled = enabled;
        evolutionTraitTolerance = Mathf.Max(0, tolerance);
        evolutionSplitFraction = Mathf.Clamp01(splitFraction);
        minimumEvolutionFounders = Mathf.Max(1, minimumFounders);
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

    private void RunPresetEvolution(int day,
        Dictionary<BlockInfo, HashSet<SpeciesData>> livingAtStart)
    {
        if (!presetEvolutionEnabled || !mutationEnabled || evolutionSplitFraction <= 0f)
            return;
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.community == null) continue;
            // 新生成的 B 不参加同一天的另一轮演化。
            List<PopulationData> candidates = new List<PopulationData>(block.community);
            foreach (PopulationData ancestor in candidates)
            {
                if (ancestor == null || !ancestor.evolutionCheckReady ||
                    ancestor.species == null || ancestor.speciesAmount <= 0 ||
                    ancestor.species.evolutionTargets == null) continue;
                SpeciesData target = ClosestEvolutionTarget(ancestor, block, day,
                    livingAtStart);
                if (target != null) ConvertToPresetSpecies(block, ancestor, target, day);
            }
        }
    }

    private SpeciesData ClosestEvolutionTarget(PopulationData source, BlockInfo block,
        int day, Dictionary<BlockInfo, HashSet<SpeciesData>> livingAtStart)
    {
        SpeciesData best = null;
        int bestDistance = int.MaxValue;
        foreach (SpeciesData target in source.species.evolutionTargets)
        {
            if (target == null || target == source.species) continue;
            int distance = TraitDistance(source, target);
            if (distance > evolutionTraitTolerance * 5 ||
                !EachTraitWithinTolerance(source, target)) continue;
            EvolutionConversionRecord record = source.evolutionConversions == null ? null :
                source.evolutionConversions.Find(item => item != null && item.target == target);
            if (record != null)
            {
                bool livingBefore = livingAtStart.TryGetValue(block,
                    out HashSet<SpeciesData> atStart) && atStart.Contains(target);
                if (livingBefore || HasLivingSpecies(block, target) ||
                    day < record.day + Mathf.Max(1, mutationInterval)) continue;
            }
            if (distance < bestDistance)
            {
                best = target;
                bestDistance = distance;
            }
        }
        return best;
    }

    private bool EachTraitWithinTolerance(PopulationData source, SpeciesData target) =>
        Mathf.Abs(source.movementAbility - target.baseMovementAbility) <= evolutionTraitTolerance &&
        Mathf.Abs(source.fitTemperature - target.baseFitTemperature) <= evolutionTraitTolerance &&
        Mathf.Abs(source.fitHumidity - target.baseFitHumidity) <= evolutionTraitTolerance &&
        Mathf.Abs(source.size - target.baseSize) <= evolutionTraitTolerance &&
        Mathf.Abs(source.fertility - target.baseFertility) <= evolutionTraitTolerance;

    private static int TraitDistance(PopulationData source, SpeciesData target) =>
        Mathf.Abs(source.movementAbility - target.baseMovementAbility) +
        Mathf.Abs(source.fitTemperature - target.baseFitTemperature) +
        Mathf.Abs(source.fitHumidity - target.baseFitHumidity) +
        Mathf.Abs(source.size - target.baseSize) +
        Mathf.Abs(source.fertility - target.baseFertility);

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
        int amount = Mathf.Max(minimumEvolutionFounders,
            Mathf.RoundToInt(ancestor.speciesAmount * evolutionSplitFraction));
        if (ancestor.speciesAmount - amount < minimumEvolutionFounders) return;
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
        if (ancestor.evolutionConversions == null)
            ancestor.evolutionConversions = new List<EvolutionConversionRecord>();
        EvolutionConversionRecord record = ancestor.evolutionConversions.Find(
            item => item != null && item.target == target);
        if (record == null)
            ancestor.evolutionConversions.Add(new EvolutionConversionRecord
            { target = target, day = day });
        else
        {
            record.day = day;
        }

        PopulationData resident = block.community.Find(population =>
            population != null && population != ancestor &&
            population.ecologicalNiche == descendant.ecologicalNiche &&
            PopulationTransfer.IsSameSpecies(population, descendant));
        if (resident == null) block.community.Add(descendant);
        else PopulationTransfer.Merge(resident, descendant);
        RegisterSpecies(target);
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

    private Dictionary<BlockInfo, HashSet<SpeciesData>> LivingSpeciesByBlock()
    {
        var result = new Dictionary<BlockInfo, HashSet<SpeciesData>>();
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.community == null) continue;
            var species = new HashSet<SpeciesData>();
            foreach (PopulationData population in block.community)
                if (population != null && population.species != null &&
                    population.speciesAmount > 0) species.Add(population.species);
            result[block] = species;
        }
        return result;
    }
}
