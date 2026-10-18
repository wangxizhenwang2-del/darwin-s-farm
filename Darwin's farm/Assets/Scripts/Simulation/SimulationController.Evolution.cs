using System;
using System.Collections.Generic;
using UnityEngine;

// 预设物种的转化，以及本局中物种的解锁、灭绝和复兴状态。
public partial class SimulationController
{
    [Header("Preset Species Evolution")]
    [SerializeField] private bool presetEvolutionEnabled = true;
    private const int EvolutionTraitTolerance = 5;
    private const int EvolutionCooldownDays = 100;
    [SerializeField] private List<SpeciesData> evolutionNetworkSpecies =
        new List<SpeciesData>();

    private readonly HashSet<SpeciesData> knownSpecies = new HashSet<SpeciesData>();
    // Optional run boundary for focused ecology scenarios. Ordinary games leave it null.
    private HashSet<SpeciesData> allowedEvolutionSpecies;
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

    public void SetAllowedEvolutionSpecies(IEnumerable<SpeciesData> species)
    {
        allowedEvolutionSpecies = species == null ? null :
            new HashSet<SpeciesData>(species);
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

    private sealed class EvolutionWaitTrace
    {
        public BlockInfo block;
        public PopulationData ancestor;
        public SpeciesData target;
        public string reason;
        public int effectiveDays, requiredDays, founders;
        public int fitTemperature, fitHumidity, movement, size, fertility;
        public int sourceCount, nextEvolutionDay, tilePopulationCount;
        public bool targetPresent, planEvaluated;
        public StockFoodForecast stockFood;
    }

    // Consecutive qualified days replace the old probabilistic draw.
    private readonly Dictionary<PopulationData, Dictionary<SpeciesData, int>>
        evolutionTraitRangeStates =
            new Dictionary<PopulationData, Dictionary<SpeciesData, int>>();

    private static readonly string[] EvolutionTraitNames =
        { "适温", "适湿", "运动", "体型", "繁殖" };

    // Observe the same five inclusive intervals used by formal evolution.
    // This trace changes neither eligibility nor the model's random stream.
    private void ReportEvolutionTraitRange(BlockInfo block,
        PopulationData source, SpeciesData target, int day)
    {
        int tolerance = LandRules.evolutionTraitTolerance;
        int[] values = { source.fitTemperature, source.fitHumidity,
            source.movementAbility, source.size, source.fertility };
        int[] centers = { target.baseFitTemperature, target.baseFitHumidity,
            target.baseMovementAbility, target.baseSize, target.baseFertility };
        int mask = 0;
        var remaining = new List<string>(5);
        var protectedTraits = new List<string>(3);
        for (int i = 0; i < values.Length; i++)
        {
            int distance = Mathf.Abs(values[i] - centers[i]);
            int gap = Mathf.Max(0, distance - tolerance);
            if (gap == 0) mask |= 1 << i;
            else remaining.Add(EvolutionTraitNames[i] + "差" + gap);
            int protectionIndex = i - 2;
            if (gap == 0 && protectionIndex >= 0 &&
                source.trackedEvolutionTarget == target &&
                source.targetProtectionSince != null &&
                source.targetProtectionSince.Length > protectionIndex &&
                source.targetProtectionReleased != null &&
                source.targetProtectionReleased.Length > protectionIndex &&
                !source.targetProtectionReleased[protectionIndex] &&
                source.targetProtectionSince[protectionIndex] >= 0)
                protectedTraits.Add(EvolutionTraitNames[i] + "持续" +
                    (day - source.targetProtectionSince[protectionIndex] + 1) + "天");
        }
        if (!evolutionTraitRangeStates.TryGetValue(source,
            out Dictionary<SpeciesData, int> states))
        {
            states = new Dictionary<SpeciesData, int>();
            evolutionTraitRangeStates.Add(source, states);
        }
        int oldMask = states.TryGetValue(target, out int previous) ? previous : 0;
        states[target] = mask;
        if (mask == oldMask) return;
        var changes = new List<string>(5);
        for (int i = 0; i < values.Length; i++)
        {
            int bit = 1 << i;
            if ((mask & bit) == (oldMask & bit)) continue;
            changes.Add(EvolutionTraitNames[i] + "=" + values[i] +
                ((mask & bit) != 0 ? "进入" : "退出") + "目标区间[" +
                (centers[i] - tolerance) + "," +
                (centers[i] + tolerance) + "]");
        }
        LogEcology(day, block, source, "evolution_trait_range",
            "目标=" + target.SpeciesName + " 变化=" +
            string.Join("、", changes.ToArray()) + " 保护中=" +
            (protectedTraits.Count > 0 ? string.Join("、", protectedTraits.ToArray()) : "无") +
            " 其余性状=" +
            (remaining.Count > 0 ? string.Join("、", remaining.ToArray()) : "全部已在范围内") +
            " 容差=±" + tolerance);
    }

    private void RunPresetEvolution(int day)
    {
        if (!presetEvolutionEnabled || day <= 0) return;
        ObserveNeighborTraitClues();
        var plans = new List<EvolutionPlan>();
        var waiting = new List<EvolutionWaitTrace>();
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.community == null ||
                block.waterCoverage != WaterCoverage.Land) continue;
            foreach (PopulationData ancestor in new List<PopulationData>(block.community))
            {
                if (ancestor == null || ancestor.species == null ||
                    ancestor.ecologicalNiche != EcologicalNiche.Land) continue;
                SpeciesData selected = null;
                List<SpeciesData> targets = ConnectedTargets(ancestor.species);
                targets.Sort(CompareSpeciesOrder);
                foreach (SpeciesData target in targets)
                {
                    if (target == null || target.baseEcologicalNiche !=
                        EcologicalNiche.Land) continue;
                    ReportEvolutionTraitRange(block, ancestor, target, day);
                    EvolutionConversionRecord record = GetEvolutionRecord(ancestor,
                        target, true);
                    int required = LivingPopulationCount(block) == 4
                        ? LandRules.evolutionRequiredDaysAtFour
                        : LandRules.evolutionRequiredDays;
                    bool traitsReady = EachTraitWithinTolerance(ancestor, target);
                    bool eligible = EvaluateEvolutionTargetEligibility(block, ancestor,
                        target, day, out string gateReason, out int founders);
                    EvolutionWaitTrace trace = traitsReady ? new EvolutionWaitTrace
                    {
                        block = block, ancestor = ancestor, target = target,
                        reason = gateReason, requiredDays = required,
                        founders = founders,
                        fitTemperature = ancestor.fitTemperature,
                        fitHumidity = ancestor.fitHumidity,
                        movement = ancestor.movementAbility,
                        size = ancestor.size, fertility = ancestor.fertility,
                        sourceCount = ancestor.speciesAmount,
                        nextEvolutionDay = ancestor.nextEvolutionDay,
                        tilePopulationCount = LivingPopulationCount(block),
                        targetPresent = HasLivingSpecies(block, target),
                        planEvaluated = gateReason == "eligible" ||
                            gateReason == "founders_or_material" ||
                            gateReason == "food_support" ||
                            gateReason == "no_legal_prey_stock" ||
                            gateReason == "stock_food_coverage" ||
                            gateReason == "l2_stock_supply_ratio" ||
                            gateReason == "l2_survival_forecast_blocked"
                    } : null;
                    if (trace != null) waiting.Add(trace);
                    int diagnosticFounders = founders > 0 ? founders :
                        Mathf.Min(Mathf.Max(0, ancestor.speciesAmount - 1),
                            Mathf.FloorToInt(ancestor.speciesAmount * .1f + .5f));
                    if (trace != null && diagnosticFounders > 0 &&
                        target.trophicLevel > 0)
                        trace.stockFood = ForecastStockPredatorFood(block,
                            ancestor, TargetPopulation(target, diagnosticFounders),
                            diagnosticFounders);
                    if (!eligible)
                    {
                        record.effectiveDays = 0;
                        continue;
                    }
                    if (record.effectiveDays < int.MaxValue) record.effectiveDays++;
                    if (trace != null)
                    {
                        trace.effectiveDays = record.effectiveDays;
                        trace.reason = record.effectiveDays < required
                            ? "consecutive_days" : "ready_to_commit";
                    }
                    if (record.effectiveDays >= required &&
                        (selected == null || CompareSpeciesOrder(target, selected) < 0))
                        selected = target;
                }
                foreach (EvolutionWaitTrace trace in waiting)
                    if (trace.ancestor == ancestor && trace.reason == "ready_to_commit" &&
                        trace.target != selected)
                        trace.reason = "another_target_selected";
                if (selected != null)
                    plans.Add(new EvolutionPlan { block = block,
                        ancestor = ancestor, target = selected });
            }
        }
        var resolved = new bool[plans.Count];
        var completed = new List<EvolutionPlan>();
        for (int i = 0; i < plans.Count; i++)
        {
            if (resolved[i]) continue;
            EvolutionPlan winner = plans[i];
            for (int j = i; j < plans.Count; j++)
            {
                EvolutionPlan contender = plans[j];
                if (contender.block != plans[i].block ||
                    contender.target != plans[i].target) continue;
                resolved[j] = true;
                if (string.CompareOrdinal(contender.ancestor.PopulationId,
                    winner.ancestor.PopulationId) < 0) winner = contender;
            }
            if (IsEligibleEvolutionTarget(winner.block, winner.ancestor,
                    winner.target, day))
            {
                ConvertToPresetSpecies(winner.block, winner.ancestor,
                    winner.target, day);
                if (HasLivingSpecies(winner.block, winner.target))
                    completed.Add(winner);
            }
        }
        foreach (EvolutionWaitTrace trace in waiting)
        {
            if (trace.reason == "ready_to_commit")
            {
                trace.reason = completed.Exists(plan =>
                    plan.block == trace.block &&
                    plan.ancestor == trace.ancestor &&
                    plan.target == trace.target) ? "committed" :
                    HasLivingSpecies(trace.block, trace.target)
                        ? "target_filled_by_other" : "commit_recheck_failed";
            }
            LogEvolutionGate(day, trace);
            if (trace.reason != "committed") LogEvolutionWait(day, trace);
        }
    }

    private void ObserveNeighborTraitClues()
    {
        int tolerance = LandRules.evolutionTraitTolerance;
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.community == null) continue;
            foreach (PopulationData source in block.community)
            {
                if (source == null || source.species == null || source.speciesAmount <= 0) continue;
                int[] values = { source.fitTemperature, source.fitHumidity,
                    source.movementAbility, source.size, source.fertility };
                foreach (SpeciesData target in ConnectedTargets(source.species))
                {
                    int[] centers = { target.baseFitTemperature, target.baseFitHumidity,
                        target.baseMovementAbility, target.baseSize, target.baseFertility };
                    for (int trait = 0; trait < values.Length; trait++)
                        if (Mathf.Abs(values[trait] - centers[trait]) <= tolerance)
                            target.RevealTrait(trait);
                }
            }
        }
    }

    private void LogEvolutionGate(int day, EvolutionWaitTrace trace)
    {
        StockFoodForecast food = trace.stockFood;
        string detail = "target=" + trace.target.SpeciesName +
            " traitOK=true cooldownRemaining=" +
            Mathf.Max(0, trace.nextEvolutionDay - day) +
            " founders=" + trace.founders +
            " legalPreyStock=" + (food != null ? food.legalPreyStock : 0) +
            " preyEdges=" + (food != null ? food.preyEdges : "") +
            " expectedKills=" + (food != null ? food.expectedKills : 0f)
                .ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
            " competingHunters=" + (food != null ? food.competingHunters : 0) +
            " demand=" + (food != null ? food.demand : 0f).ToString("R",
                System.Globalization.CultureInfo.InvariantCulture) +
            " coverage=" + (food != null ? food.coverage : 0f).ToString("R",
                System.Globalization.CultureInfo.InvariantCulture) +
            " stockFoodOK=" + (food == null ? "n/a" :
                (food.coverage >= LandRules.evolutionTodaySatisfaction)
                    .ToString()) +
            " qualifiedDays=" + trace.effectiveDays +
            " requiredDays=" + trace.requiredDays +
            " result=" + (trace.reason == "committed" ? "committed" : "rejected") +
            " reason=" + trace.reason;
        LogEcology(day, trace.block, trace.ancestor, "evolution_gate", detail);
    }

    private void LogEvolutionWait(int day, EvolutionWaitTrace trace)
    {
        PopulationData source = trace.ancestor;
        string detail = "target=" + trace.target.SpeciesName +
            " reason=" + trace.reason +
            " traits=" + trace.fitTemperature + "/" + trace.fitHumidity +
            "/" + trace.movement + "/" + trace.size + "/" +
            trace.fertility + " effectiveDays=" + trace.effectiveDays +
            " requiredDays=" + trace.requiredDays +
            " nextEvolutionDay=" + trace.nextEvolutionDay +
            " cooldownRemaining=" + Mathf.Max(0, trace.nextEvolutionDay - day) +
            " planEvaluated=" + trace.planEvaluated +
            " plannedFounders=" + trace.founders +
            " sourceCount=" + trace.sourceCount +
            " targetPresent=" + trace.targetPresent +
            " tilePopulations=" + trace.tilePopulationCount;
        if ((trace.reason == "food_support" ||
             trace.reason == "stock_food_coverage" ||
             trace.reason == "no_legal_prey_stock" ||
             trace.reason == "l2_stock_supply_ratio" ||
             trace.reason == "l2_survival_forecast_blocked") &&
            trace.founders > 0)
        {
            PopulationData candidate = TargetPopulation(trace.target,
                trace.founders);
            if (candidate.trophicLevel > 0)
            {
                float demand = LandLifeRules.Demand(candidate, trace.founders,
                    LandLifeRules.Fitness(trace.block, candidate.fitTemperature,
                        candidate.fitHumidity), LandRules);
                StockFoodForecast stock = ForecastStockPredatorFood(
                    trace.block, source, candidate, trace.founders);
                detail += " stockFood=" + stock.energy.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) +
                    " predatorDemand=" + demand.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                int remaining = source.trophicLevel == 0
                    ? source.speciesAmount - trace.founders
                    : source.speciesAmount;
                LandLifeRules.ForecastEvolutionPlantSupport(trace.block, source,
                    candidate, remaining, LandRules, out float demand,
                    out float today, out float renewal);
                detail += " plantDemand=" + demand.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) +
                    " plantToday=" + today.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture) +
                    " plantRenewal=" + renewal.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        LogEcology(day, trace.block, source, "formal_evolution_waiting", detail);
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
            source.speciesAmount < 2 || day < source.nextEvolutionDay) return null;

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
                if (target != null && target != source &&
                    (allowedEvolutionSpecies == null ||
                     allowedEvolutionSpecies.Contains(target))) result.Add(target);
        // Imported assets encode some intended edges in reverse direction.
        // A connection is valid in either direction; template and food gates
        // still decide whether a conversion can occur.
        foreach (SpeciesData candidate in knownSpecies)
            if (candidate != null && candidate != source &&
                (allowedEvolutionSpecies == null ||
                 allowedEvolutionSpecies.Contains(candidate)) &&
                candidate.evolutionTargets != null &&
                candidate.evolutionTargets.Contains(source)) result.Add(candidate);
        return new List<SpeciesData>(result);
    }

    private static string StableSpeciesKey(SpeciesData species) =>
        !string.IsNullOrEmpty(species.SpeciesId) ? species.SpeciesId :
        !string.IsNullOrEmpty(species.SpeciesName) ? species.SpeciesName : species.name;

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
        => EvaluateEvolutionTargetEligibility(block, source, target, day,
            out _, out _);

    private bool EvaluateEvolutionTargetEligibility(BlockInfo block, PopulationData source,
        SpeciesData target, int day, out string reason, out int founders)
    {
        reason = "eligible";
        founders = 0;
        if (block == null || source == null || target == null ||
            target == source.species || source.species == null ||
            source.species.baseEcologicalNiche != target.baseEcologicalNiche ||
            target.baseEcologicalNiche != EcologicalNiche.Land)
        { reason = "invalid_target"; return false; }
        if (day < source.nextEvolutionDay)
        { reason = "cooldown"; return false; }
        if (!EachTraitWithinTolerance(source, target))
        { reason = "traits_out_of_range"; return false; }
        if (HasLivingSpecies(block, target))
        { reason = "target_already_present"; return false; }
        if (LivingPopulationCount(block) >= 5)
        { reason = "tile_full"; return false; }
        if (!TryPlanFormalEvolution(block, source, target,
                out founders, out _, out _))
        { reason = "founders_or_material"; return false; }
        if (!HasEvolutionFoodSupport(block, source, target, founders))
        {
            if (target.trophicLevel > 0)
            {
                StockFoodForecast stock = ForecastStockPredatorFood(block,
                    source, TargetPopulation(target, founders), founders);
                reason = stock.legalPreyStock <= 0 ? "no_legal_prey_stock" :
                    stock.coverage < LandRules.evolutionTodaySatisfaction
                        ? "stock_food_coverage" :
                        target.trophicLevel == 2 &&
                        stock.energy < founders * LandLifeRules.AdultNeed(
                            target.baseSize, target.baseMovementAbility,
                            LandRules) * (founders < 15 ? 1.50f : 1.08f)
                            ? "l2_stock_supply_ratio" :
                            "l2_survival_forecast_blocked";
            }
            else reason = "food_support";
            return false;
        }
        return true;
    }

    // Planning is read-only. The actual split is committed only after every
    // population, food and material constraint has passed.
    private bool TryPlanFormalEvolution(BlockInfo block, PopulationData source,
        SpeciesData target, out int amount, out float plantPayment,
        out float excessBody)
    {
        amount = 0; plantPayment = excessBody = 0f;
        if (source.speciesAmount < 2 || target == null) return false;
        int requested = Mathf.Min(source.speciesAmount - 1,
            Mathf.FloorToInt(source.speciesAmount * 0.1f + 0.5f));
        if (requested < 1) return false;
        float sourceBody = source.bodyBiomassInitialized ? source.bodyBiomass :
            source.speciesAmount * LandLifeRules.BodyMass(source.size, LandRules);
        float sourcePerHead = sourceBody / source.speciesAmount;
        float targetPerHead = LandLifeRules.BodyMass(target.baseSize, LandRules);
        float extraPerHead = Mathf.Max(0f, targetPerHead - sourcePerHead);
        float spendable = Mathf.Max(0f, block.plantBiomass);
        if (LandRules.useCompensatedEvolutionFoodGate)
        {
            float floor = Mathf.Min(spendable * 0.5f,
                Mathf.Max(0f, block.maxPlantBiomass) *
                LandRules.minimumPlantReserveFraction);
            spendable = Mathf.Max(0f, spendable - floor);
        }
        int affordable = extraPerHead > 0f
            ? Mathf.FloorToInt(spendable / extraPerHead)
            : requested;
        amount = Mathf.Min(requested, affordable);
        if (amount < 1 || target.trophicLevel == 2 && amount < 10)
        {
            if (target.trophicLevel == 2 &&
                (OnEcologyEvent != null || EnableEcologyConsoleLogs))
                LogEcology(currentSimulationDay, block, source,
                    "formal_evolution_rejected", "target=" + target.SpeciesName +
                    " founders=" + amount + " reason=founders_below_10");
            return false;
        }
        plantPayment = amount * extraPerHead;
        excessBody = amount * Mathf.Max(0f, sourcePerHead - targetPerHead);
        return true;
    }

    private sealed class StockFoodForecast
    {
        public float demand, energy, coverage;
        public int legalPreyStock, competingHunters;
        public float expectedKills;
        public string preyEdges = "";
    }

    // Read-only counterpart of HuntLevel's claims, whole-prey limit and
    // weighted shared catch. The input is the live stock at this gate.
    private StockFoodForecast ForecastStockPredatorFood(BlockInfo block,
        PopulationData source, PopulationData candidate, int founders)
    {
        var result = new StockFoodForecast();
        result.demand = LandLifeRules.Demand(candidate, founders,
            LandLifeRules.Fitness(block, candidate.fitTemperature,
                candidate.fitHumidity), LandRules);
        var hunters = new List<PopulationData>();
        var counts = new List<int>();
        foreach (PopulationData resident in block.community)
        {
            int count = resident == source ? resident.speciesAmount - founders :
                resident != null ? resident.speciesAmount : 0;
            if (resident != null && resident.trophicLevel > 0 && count > 0)
            { hunters.Add(resident); counts.Add(count); }
        }
        int focal = hunters.Count;
        hunters.Add(candidate);
        counts.Add(founders);
        var targets = new float[hunters.Count];
        var preferences = new float[hunters.Count];
        for (int i = 0; i < hunters.Count; i++)
        {
            targets[i] = ForecastEvolutionHuntTarget(block, hunters[i], counts[i]);
            foreach (PopulationData prey in block.community)
            {
                int available = prey == source ? prey.speciesAmount - founders :
                    prey != null ? prey.speciesAmount : 0;
                if (available > 0)
                    preferences[i] += LandLifeRules.HuntPreference(
                        hunters[i], prey, LandRules);
            }
            if (i != focal && preferences[i] > 0f)
                result.competingHunters++;
        }
        var edges = new List<string>();
        foreach (PopulationData prey in block.community)
        {
            int available = prey == source ? prey.speciesAmount - founders :
                prey != null ? prey.speciesAmount : 0;
            if (available <= 0 || !FoodWeb.CanEat(candidate, prey)) continue;
            result.legalPreyStock += available;
            float[] claims = new float[hunters.Count];
            float[] weights = new float[hunters.Count];
            float totalClaims = 0f, totalWeight = 0f;
            int lastEligible = -1;
            for (int i = 0; i < hunters.Count; i++)
            {
                if (preferences[i] <= 0f || targets[i] <= 0f ||
                    !FoodWeb.CanEat(hunters[i], prey)) continue;
                float intent = counts[i] * LandLifeRules.HuntPreference(
                    hunters[i], prey, LandRules) / preferences[i] *
                    Mathf.Min(1f, 10f / Mathf.Max(1f, prey.size)) *
                    LandLifeRules.CaptureChance(hunters[i], prey, LandRules) *
                    ForecastPreyDensity(block, prey, available);
                claims[i] = LandLifeRules.WholePreyClaim(intent, targets[i],
                    LandLifeRules.PreyEnergy(hunters[i], prey, LandRules));
                if (claims[i] <= 0f) continue;
                totalClaims += claims[i];
                lastEligible = i;
            }
            if (totalClaims <= 0f)
            { edges.Add(prey.speciesId + ":" + available + ":0"); continue; }
            for (int i = 0; i < hunters.Count; i++)
            {
                if (claims[i] <= 0f) continue;
                float pressure = claims[i];
                for (int k = 0; k < hunters.Count; k++)
                    if (k != i) pressure += claims[k] *
                        (HuntingStrategy(hunters[i]) *
                         HuntingStrategy(hunters[k]) == -1
                            ? LandRules.oppositeStrategyCompetition : 1f);
                weights[i] = claims[i] / (1f + LandRules.competitionPressure *
                    Mathf.Max(0f, pressure / Mathf.Max(1f, available) - 1f));
                totalWeight += weights[i];
            }
            int kills = Mathf.Min(available, Mathf.FloorToInt(Mathf.Min(
                available, totalClaims + prey.predationRemainder)));
            float remaining = kills;
            float focalKills = 0f;
            for (int i = 0; i < hunters.Count; i++)
            {
                if (claims[i] <= 0f || targets[i] <= 0f) continue;
                float share = i == lastEligible ? remaining :
                    kills * weights[i] / Mathf.Max(.0001f, totalWeight);
                share = Mathf.Min(remaining, share);
                remaining -= share;
                float energy = share * LandLifeRules.PreyEnergy(
                    hunters[i], prey, LandRules);
                targets[i] = Mathf.Max(0f, targets[i] - energy);
                if (i != focal) continue;
                focalKills = share;
                result.energy += energy;
            }
            result.expectedKills += focalKills;
            edges.Add((prey.species != null ?
                prey.species.SpeciesName : prey.speciesId) + ":" + available +
                ":" + focalKills.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        result.preyEdges = string.Join(",", edges.ToArray());
        result.coverage = result.demand > 0f
            ? Mathf.Min(result.demand, result.energy) / result.demand : 0f;
        return result;
    }

    private static int HuntingStrategy(PopulationData population)
    {
        float ratio = 100f * population.size /
            Mathf.Max(10f, population.fertility);
        return ratio < 30f ? -1 : ratio >= 60f ? 1 : 0;
    }

    private float ForecastPreyDensity(BlockInfo block, PopulationData prey,
        int available)
    {
        if (!LandRules.enablePreyDensityFeedback || prey.trophicLevel != 0)
            return 1f;
        float reference = block.predationReferenceRecovery > 0f
            ? block.predationReferenceRecovery : block.habitatRecovery;
        float adult = LandLifeRules.AdultNeed(prey.size,
            prey.movementAbility, LandRules);
        float half = Mathf.Max(1f,
            LandRules.preyDensityHalfSaturationScale *
            Mathf.Max(1f, reference) / Mathf.Max(.0001f, adult));
        float n = available;
        return n * n / (n * n + half * half);
    }

    private bool HasStockL2FounderSupport(BlockInfo block,
        PopulationData source, SpeciesData target, int amount,
        StockFoodForecast today)
    {
        // The historical ten-day net-renewal ledger cannot certify an L2
        // stock trajectory. Keep L2 closed until a read-only 90-day forecast
        // accounts for prey births, hunting and competitor depletion.
        float adult = amount * LandLifeRules.AdultNeed(target.baseSize,
            target.baseMovementAbility, LandRules);
        if (amount < 10 || adult <= 0f || today.energy / adult <
            (amount < 15 ? 1.50f : 1.08f)) return false;
        return false;
    }

    private bool HasEvolutionFoodSupport(BlockInfo block, PopulationData source,
        SpeciesData target, int amount)
    {
        if (target.trophicLevel > 0)
        {
            StockFoodForecast stock = ForecastStockPredatorFood(block, source,
                TargetPopulation(target, amount), amount);
            if (stock.legalPreyStock <= 0 || stock.coverage <
                LandRules.evolutionTodaySatisfaction) return false;
            if (target.trophicLevel == 2)
                return HasStockL2FounderSupport(block, source, target,
                    amount, stock);
            return true;
        }
        if (LandRules.useCompensatedEvolutionFoodGate)
            return HasCompensatedEvolutionFoodSupport(block, source,
                target, amount);
        PopulationData candidate = TargetPopulation(target, amount);
        int remaining = source.trophicLevel == 0
            ? source.speciesAmount - amount : source.speciesAmount;
        LandLifeRules.ForecastEvolutionPlantSupport(block, source,
            candidate, remaining, LandRules, out float demand,
            out float today, out float renewal);
        if (demand <= 0f) return false;
        return today / demand >= LandRules.evolutionTodaySatisfaction
            && renewal / demand >= LandRules.evolutionRenewalSatisfaction;
    }

    // A conservative, read-only L2 admission forecast. The supply is the
    // existing net renewable prey ledger after competing hunters' claims.
    // No population, resource, RNG or cooldown is touched on rejection.
    private bool HasSustainableL2FounderSupport(BlockInfo block,
        PopulationData source, SpeciesData target, int amount,
        out string rejection, out float sustainableFood)
    {
        rejection = "";
        sustainableFood = 0f;
        if (amount < 10)
        {
            rejection = "founders_below_10";
            return false;
        }
        PopulationData candidate = TargetPopulation(target, amount);
        bool hasKnownPrey = false;
        foreach (PopulationData prey in block.community)
            if (prey != null && prey.speciesAmount > 0 &&
                FoodWeb.CanEat(candidate, prey) &&
                prey.preyNetRenewalHistory != null &&
                prey.preyNetRenewalHistory.Count > 0)
                hasKnownPrey = true;
        if (!hasKnownPrey)
        {
            rejection = "prey_renewal_unknown";
            return false;
        }
        sustainableFood = LandLifeRules.AvailableSustainablePreyFood(
            block, candidate, LandRules, replaced: source,
            replacedCount: source.speciesAmount - amount);
        float adultPerHead = LandLifeRules.AdultNeed(candidate.size,
            candidate.movementAbility, LandRules);
        float adultAtStart = amount * adultPerHead;
        if (adultAtStart <= 0f || sustainableFood / adultAtStart <
            (amount < 15 ? 1.50f : 1.08f))
        {
            rejection = "sustainable_supply_ratio";
            return false;
        }

        float dailyFood = 0.85f * sustainableFood;
        float fitness = LandLifeRules.Fitness(block, candidate.fitTemperature,
            candidate.fitHumidity);
        float newbornCost = LandLifeRules.YoungNeed(candidate.size, LandRules)
            + LandLifeRules.BodyMass(candidate.size, LandRules);
        int count = amount, paidBirths = 0, endangeredDays = 0;
        float reserve = 0f, birthRemainder = 0f, deathRemainder = 0f;
        for (int day = 0; day < 90; day++)
        {
            float available = dailyFood + reserve;
            float adult = count * adultPerHead;
            float satisfaction = adult > 0f
                ? Mathf.Clamp01(available / adult) : 1f;
            float opportunity = count * LandRules.birthRate *
                candidate.fertility / 100f * fitness * satisfaction;
            birthRemainder += opportunity;
            int possible = Mathf.FloorToInt(birthRemainder);
            int affordable = Mathf.FloorToInt(Mathf.Max(0f,
                available - adult) / Mathf.Max(0.0001f, newbornCost));
            int born = Mathf.Min(possible, affordable);
            float deaths = LandRules.starvationDeathRate * count *
                (1f - satisfaction)
                + LandLifeRules.ColdDeaths(count, block.temperature, LandRules)
                + LandRules.climateMismatchDeathRate * count *
                    (1f - fitness)
                + LandLifeRules.NaturalAdultDeaths(count, candidate.size,
                    LandRules)
                + LandLifeRules.PaidBirthStressDeaths(born, candidate.size,
                    candidate.fertility, LandRules);
            deathRemainder = Mathf.Min(count,
                deathRemainder + Mathf.Max(0f, deaths));
            int died = Mathf.Min(count, Mathf.FloorToInt(deathRemainder));
            deathRemainder -= died;
            birthRemainder = Mathf.Min(0.999f,
                Mathf.Max(0f, birthRemainder - born));
            reserve = Mathf.Min(Mathf.Max(0f,
                available - Mathf.Min(available, adult) - born * newbornCost),
                adult * LandRules.reserveDays);
            paidBirths += born;
            count += born - died;
            endangeredDays = count < 10 ? endangeredDays + 1 : 0;
            if (endangeredDays >= 10 || count <= 0)
            {
                rejection = "forecast_extinction";
                return false;
            }
        }
        if (count < Mathf.Max(10, Mathf.FloorToInt(0.8f * amount)) ||
            paidBirths < 1)
        {
            rejection = "forecast_final_population_or_paid_birth";
            return false;
        }
        return true;
    }

    private bool HasCompensatedEvolutionFoodSupport(BlockInfo block,
        PopulationData source, SpeciesData target, int amount)
    {
        if (!TryPlanFormalEvolution(block, source, target,
                out int planned, out float plantPayment, out _) ||
            planned != amount) return false;
        PopulationData candidate = TargetPopulation(target, amount);
        int remaining = source.speciesAmount - amount;
        LandLifeRules.ForecastEvolutionPlantsAfterPayment(block, source,
            remaining, candidate, plantPayment, LandRules,
            out List<PopulationData> groups, out float[] now,
            out float[] renewable);
        int sourceIndex = groups.IndexOf(source);
        if (source.trophicLevel == 0)
        {
            if (sourceIndex < 0) return false;
            float sourceAdult = remaining * LandLifeRules.AdultNeed(
                source.size, source.movementAbility, LandRules);
            if (sourceAdult > 0f &&
                (now[sourceIndex] / sourceAdult < 0.95f ||
                 candidate.trophicLevel == 0 &&
                 renewable[sourceIndex] / sourceAdult < 0.95f))
                return false;
        }
        if (candidate.trophicLevel != 0)
            return HasCompensatedPredatorFoodSupport(block, source,
                candidate, remaining, plantPayment, out _);
        int childIndex = groups.IndexOf(candidate);
        if (childIndex < 0) return false;
        float adult = amount * LandLifeRules.AdultNeed(candidate.size,
            candidate.movementAbility, LandRules);
        if (adult <= 0f || now[childIndex] / adult < 0.95f ||
            renewable[childIndex] / adult < 0.95f) return false;
        float expectedPredation = ForecastEvolutionPredationDeaths(block,
            candidate);
        float netNow = ForecastEvolutionPaidNet(block, candidate, amount,
            now[childIndex], expectedPredation);
        float netRenewal = ForecastEvolutionPaidNet(block, candidate, amount,
            renewable[childIndex], expectedPredation);
        return netNow >= -0.001f * amount && netRenewal > 0f;
    }

    private sealed class PredatorFoodTrial
    {
        public int preyCount;
        public float preyRenewal, candidateCatches, candidateFood;
        public float candidateAdultCoverage, candidateNet;
        public bool pass;
    }

    // Formal-evolution-only counterfactual. No live population, stock,
    // remainder, or random source is changed by these six fixed trials.
    private bool HasCompensatedPredatorFoodSupport(BlockInfo block,
        PopulationData source, PopulationData candidate, int remaining,
        float plantPayment, out PredatorFoodTrial[] trials)
    {
        float[] densities = { 1f, 0.85f, 0.70f, 0.55f, 0.40f, 0.30f };
        trials = new PredatorFoodTrial[densities.Length];
        if (source.trophicLevel != 0 || remaining <= 0) return false;
        bool previousPassed = false;
        bool adjacentPassed = false;
        for (int t = 0; t < densities.Length; t++)
        {
            int trialCount = Mathf.Max(1, Mathf.FloorToInt(
                remaining * densities[t]));
            var sourceTrial = new PopulationData(false)
            {
                species = source.species,
                speciesAmount = trialCount,
                trophicLevel = source.trophicLevel,
                movementAbility = source.movementAbility,
                size = source.size,
                fertility = source.fertility,
                fitTemperature = source.fitTemperature,
                fitHumidity = source.fitHumidity,
                birthRemainder = source.birthRemainder,
                bodyBiomassInitialized = source.bodyBiomassInitialized,
                bodyBiomass = source.speciesAmount > 0
                    ? source.bodyBiomass * trialCount / source.speciesAmount
                    : 0f
            };
            LandLifeRules.ForecastEvolutionPlantsAfterPayment(block, source,
                trialCount, candidate, plantPayment, LandRules,
                out List<PopulationData> groups, out float[] now, out _);
            var trial = new PredatorFoodTrial { preyCount = trialCount };
            trials[t] = trial;
            float candidateTarget = ForecastEvolutionHuntTarget(block,
                candidate, candidate.speciesAmount);
            float candidatePreference = 0f;
            foreach (PopulationData prey in block.community)
            {
                if (prey == null || prey.speciesAmount <= 0) continue;
                PopulationData option = prey == source ? sourceTrial : prey;
                candidatePreference += LandLifeRules.HuntPreference(candidate,
                    option, LandRules);
            }
            if (candidatePreference <= 0f) continue;
            bool supported = true;
            foreach (PopulationData prey in block.community)
            {
                if (prey == null || prey.speciesAmount <= 0 ||
                    !FoodWeb.CanEat(candidate, prey)) continue;
                PopulationData option = prey == source ? sourceTrial : prey;
                // Higher-level renewal lacks a plant-budget counterfactual.
                if (option.trophicLevel != 0) { supported = false; break; }
                int index = groups.IndexOf(prey);
                if (index < 0) continue;
                float renewal = Mathf.Max(0f, ForecastEvolutionPaidNet(
                    block, option, option.speciesAmount, now[index], 0f));
                if (prey == source) trial.preyRenewal = renewal;
                int wholeRenewal = Mathf.FloorToInt(renewal);
                if (wholeRenewal <= 0) continue;
                float preference = LandLifeRules.HuntPreference(candidate,
                    option, LandRules);
                float density = ForecastEvolutionPreyDensity(block,
                    option);
                float chance = Mathf.Min(1f,
                    10f / Mathf.Max(1f, option.size)) *
                    LandLifeRules.CaptureChance(candidate, option,
                        LandRules) * density;
                float candidateIntent = candidate.speciesAmount *
                    preference / candidatePreference * chance;
                float candidateClaim = LandLifeRules.WholePreyClaim(
                    candidateIntent, candidateTarget,
                    LandLifeRules.PreyEnergy(candidate, option, LandRules));
                float existingClaims = 0f;
                foreach (PopulationData hunter in block.community)
                {
                    if (hunter == null || hunter == source ||
                        hunter.speciesAmount <= 0 ||
                        !FoodWeb.CanEat(hunter, option)) continue;
                    float totalPreference = 0f;
                    foreach (PopulationData possible in block.community)
                        if (possible != null && possible.speciesAmount > 0)
                            totalPreference += LandLifeRules.HuntPreference(
                                hunter, possible == source ? sourceTrial :
                                possible, LandRules);
                    if (totalPreference <= 0f) continue;
                    float hunterIntent = hunter.speciesAmount *
                        LandLifeRules.HuntPreference(hunter, option,
                            LandRules) / totalPreference * chance /
                        Mathf.Max(0.0001f, LandLifeRules.CaptureChance(
                            candidate, option, LandRules)) *
                        LandLifeRules.CaptureChance(hunter, option,
                            LandRules);
                    existingClaims += LandLifeRules.WholePreyClaim(
                        hunterIntent, ForecastEvolutionHuntTarget(block,
                            hunter, hunter.speciesAmount),
                        LandLifeRules.PreyEnergy(hunter, option, LandRules));
                }
                int available = Mathf.Max(0, wholeRenewal -
                    Mathf.CeilToInt(existingClaims));
                int catches = Mathf.Min(available,
                    Mathf.FloorToInt(candidateClaim));
                trial.candidateCatches += catches;
                trial.candidateFood += catches *
                    LandLifeRules.PreyEnergy(candidate, option, LandRules);
            }
            float adult = candidate.speciesAmount * LandLifeRules.AdultNeed(
                candidate.size, candidate.movementAbility, LandRules);
            trial.candidateAdultCoverage = adult > 0f
                ? trial.candidateFood / adult : 0f;
            trial.candidateNet = ForecastEvolutionPaidNet(block, candidate,
                candidate.speciesAmount, trial.candidateFood,
                ForecastEvolutionPredationDeaths(block, candidate));
            trial.pass = supported && trial.preyRenewal > 0f &&
                trial.candidateCatches > 0f &&
                trial.candidateAdultCoverage >= 0.95f &&
                trial.candidateNet >= 0.001f * candidate.speciesAmount;
            adjacentPassed |= previousPassed && trial.pass;
            previousPassed = trial.pass;
        }
        return adjacentPassed;
    }

    private float ForecastEvolutionHuntTarget(BlockInfo block,
        PopulationData hunter, int count)
    {
        float adult = count * LandLifeRules.AdultNeed(hunter.size,
            hunter.movementAbility, LandRules);
        float births = count * LandRules.birthRate * hunter.fertility / 100f *
            LandLifeRules.Fitness(block, hunter.fitTemperature,
                hunter.fitHumidity);
        return Mathf.Max(0f, adult + births *
            (LandLifeRules.YoungNeed(hunter.size, LandRules) +
             LandLifeRules.BodyMass(hunter.size, LandRules)) +
            adult * LandRules.reserveHuntFraction - hunter.energyReserve);
    }

    private float ForecastEvolutionPreyDensity(BlockInfo block,
        PopulationData prey)
    {
        if (!LandRules.enablePreyDensityFeedback || prey.trophicLevel != 0)
            return 1f;
        float reference = block.predationReferenceRecovery > 0f
            ? block.predationReferenceRecovery : block.habitatRecovery;
        float adult = LandLifeRules.AdultNeed(prey.size,
            prey.movementAbility, LandRules);
        float half = Mathf.Max(1f,
            LandRules.preyDensityHalfSaturationScale *
            Mathf.Max(1f, reference) / Mathf.Max(0.0001f, adult));
        float n = prey.speciesAmount;
        return n * n / (n * n + half * half);
    }

    private float ForecastEvolutionPaidNet(BlockInfo block,
        PopulationData population, int count, float realFood,
        float expectedPredationDeaths)
    {
        float adult = count * LandLifeRules.AdultNeed(population.size,
            population.movementAbility, LandRules);
        float satisfaction = adult > 0f ?
            Mathf.Clamp01(realFood / adult) : 1f;
        float fitness = LandLifeRules.Fitness(block,
            population.fitTemperature, population.fitHumidity);
        float opportunity = count * LandRules.birthRate *
            population.fertility / 100f * fitness * satisfaction;
        float newbornCost = LandLifeRules.YoungNeed(population.size,
            LandRules) + LandLifeRules.BodyMass(population.size, LandRules);
        int paidBirths = Mathf.Min(Mathf.FloorToInt(
                population.birthRemainder + opportunity),
            Mathf.FloorToInt(Mathf.Max(0f, realFood - adult) /
                Mathf.Max(0.0001f, newbornCost)));
        float baseDeaths = LandRules.starvationDeathRate * count *
            (1f - satisfaction) +
            LandLifeRules.ColdDeaths(count, block.temperature, LandRules) +
            LandRules.climateMismatchDeathRate * count * (1f - fitness) +
            LandLifeRules.NaturalAdultDeaths(count, population.size,
                LandRules);
        float stress = LandRules.enablePaidBirthStress ?
            LandLifeRules.PaidBirthStressDeaths(paidBirths,
                population.size, population.fertility, LandRules) :
            LandLifeRules.ReproductionStressDeaths(count,
                population.size, population.fertility, LandRules,
                satisfaction: satisfaction, fitness: fitness);
        return paidBirths - baseDeaths - stress - expectedPredationDeaths;
    }

    private float ForecastEvolutionPredationDeaths(BlockInfo block,
        PopulationData candidate)
    {
        float claims = 0f;
        foreach (PopulationData hunter in block.community)
        {
            if (hunter == null || hunter.speciesAmount <= 0 ||
                !FoodWeb.CanEat(hunter, candidate)) continue;
            float preference = LandLifeRules.HuntPreference(hunter,
                candidate, LandRules);
            float totalPreference = preference;
            foreach (PopulationData prey in block.community)
                if (prey != null && prey.speciesAmount > 0)
                    totalPreference += LandLifeRules.HuntPreference(hunter,
                        prey, LandRules);
            if (totalPreference <= 0f) continue;
            float demand = LandLifeRules.Demand(hunter,
                hunter.speciesAmount,
                LandLifeRules.Fitness(block, hunter.fitTemperature,
                    hunter.fitHumidity), LandRules);
            float intent = hunter.speciesAmount * preference /
                totalPreference *
                Mathf.Min(1f, 10f / Mathf.Max(1f, candidate.size)) *
                LandLifeRules.CaptureChance(hunter, candidate, LandRules) *
                LandLifeRules.PreyDensityFactor(block, candidate, LandRules);
            claims += LandLifeRules.WholePreyClaim(intent,
                Mathf.Max(0f, demand - hunter.energyReserve),
                LandLifeRules.PreyEnergy(hunter, candidate, LandRules));
        }
        return Mathf.Min(candidate.speciesAmount, claims);
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
        new PopulationData(false)
        {
            species = target, speciesAmount = count,
            ecologicalNiche = target.baseEcologicalNiche,
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
        EcologicalNiche surface = HabitatTopology.IsWater(block)
            ? EcologicalNiche.Water : EcologicalNiche.Land;
        int count = 0;
        foreach (PopulationData population in block.community)
            if (population != null && population.speciesAmount > 0 &&
                population.ecologicalNiche == surface) count++;
        return count;
    }

    private void ConvertToPresetSpecies(BlockInfo block, PopulationData ancestor,
        SpeciesData target, int day)
    {
        if (HasLivingSpecies(block, target) ||
            !TryPlanFormalEvolution(block, ancestor, target,
            out int amount, out float plantPayment, out float excessBody) ||
            !HasEvolutionFoodSupport(block, ancestor, target, amount)) return;
        CommitPresetSpecies(block, block, ancestor, target, day, amount,
            plantPayment, excessBody);
    }

    private void CommitPresetSpecies(BlockInfo sourceBlock, BlockInfo targetBlock,
        PopulationData ancestor, SpeciesData target, int day, int amount,
        float plantPayment, float excessBody)
    {
        if (sourceBlock == null || targetBlock == null ||
            sourceBlock.community == null || targetBlock.community == null ||
            !sourceBlock.community.Contains(ancestor) || amount < 1 ||
            ancestor.speciesAmount <= amount || HasLivingSpecies(targetBlock,
                target)) return;
        WaterRegion paymentRegion = null;
        float available = Mathf.Max(0f, sourceBlock.plantBiomass);
        if (HabitatTopology.IsWater(sourceBlock))
        {
            paymentRegion = BuildWaterRegions().GetRegion(sourceBlock);
            if (paymentRegion == null) return;
            available = 0f;
            foreach (BlockInfo cell in paymentRegion.Members)
                available += Mathf.Max(0f, cell.plantBiomass);
        }
        if (available + .01f < plantPayment) return;
        int sourceCountBefore = ancestor.speciesAmount;
        LandLifeRules.EnsureBodyMass(ancestor, LandRules);
        PopulationData descendant = PopulationTransfer.Split(ancestor, amount,
            sourceBlock, day, migrationCooldownDays);
        float remainingPayment = plantPayment;
        if (HabitatTopology.IsWater(sourceBlock))
        {
            if (paymentRegion != null)
                foreach (BlockInfo cell in paymentRegion.Members)
                {
                    float debit = Mathf.Min(Mathf.Max(0f, cell.plantBiomass),
                        remainingPayment);
                    cell.plantBiomass -= debit;
                    remainingPayment -= debit;
                }
        }
        else
        {
            sourceBlock.plantBiomass = Mathf.Max(0f,
                sourceBlock.plantBiomass - plantPayment);
            remainingPayment = 0f;
        }
        if (remainingPayment > .01f)
            throw new InvalidOperationException("Formal evolution payment failed");
        sourceBlock.detritusBiomass += excessBody;
        descendant.bodyBiomass = amount * LandLifeRules.BodyMass(
            target.baseSize, LandRules);
        descendant.bodyBiomassInitialized = true;
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
        float reserveBeforeCap = descendant.energyReserve;
        descendant.energyReserve = Mathf.Min(descendant.energyReserve,
            amount * LandLifeRules.AdultNeed(descendant.size,
                descendant.movementAbility, LandRules) * LandRules.reserveDays);
        sourceBlock.accountedBiomassSinkToday += reserveBeforeCap - descendant.energyReserve;
        descendant.nextEvolutionDay = day + LandRules.evolutionCooldownDays;

        descendant.localSplitBranch = false;
        descendant.directedSplitTarget = null;
        descendant.directedTraitUnlockDay = 0;
        descendant.sameSpeciesCoexistenceDays = 0;
        descendant.splitStrategy = descendant.trophicLevel == 0 &&
            target.baseSize >= 25 ? 1 : 0;
        descendant.mutationSelectionProgress = 0f;
        descendant.mutationSelectionCandidate = -1;
        descendant.birthRemainder = 0f;
        descendant.deathRemainder = 0f;
        descendant.predationRemainder = 0f;
        descendant.foodSatisfactionHistory.Clear();
        descendant.renewalSatisfactionHistory.Clear();
        descendant.preyNetRenewalHistory.Clear();
        descendant.populationGrowthHistory.Clear();
        descendant.directedInfluences.Clear();
        descendant.fertilityGuidance = new FertilityGuidancePulse();
        descendant.trackedEvolutionTarget = null;
        descendant.manualEvolutionTarget = null;
        ResetTargetProtection(descendant);
        descendant.lastMutationEffect = "转化为" + target.SpeciesName;
        RecordFormalTransfer(ancestor, target, amount);
        ancestor.nextEvolutionDay = day + LandRules.evolutionCooldownDays;
        ancestor.localSplitBranch = false;
        ancestor.directedSplitTarget = null;
        ancestor.directedTraitUnlockDay = 0;
        ancestor.sameSpeciesCoexistenceDays = 0;
        ancestor.mutationSelectionProgress = 0f;
        ancestor.mutationSelectionCandidate = -1;
        ancestor.evolutionConversions.Clear();
        ancestor.directedInfluences.Clear();
        ancestor.trackedEvolutionTarget = null;
        ancestor.manualEvolutionTarget = null;
        ResetTargetProtection(ancestor);
        targetBlock.community.Add(descendant);
        LandLifeRules.RefreshEnvironmentMetrics(targetBlock, LandRules);
        RegisterSpecies(target);
        OnPresetEvolutionResolved?.Invoke(new PopulationTransitionResult(
            PopulationTransitionKind.PresetEvolution, day, sourceBlock, targetBlock,
            ancestor, descendant, amount, sourceCountBefore,
            0, PopulationArrivalOutcome.Created, target));
        OnSpeciesEvolved?.Invoke(targetBlock, ancestor, descendant, target, amount);
        LogEcology(day, targetBlock, descendant, "preset_evolution",
            "target=" + target.SpeciesName + " amount=" + amount
            + " plantPayment=" + plantPayment.ToString("F2")
            + " excessBody=" + excessBody.ToString("F2")
            + " count=" + descendant.speciesAmount);
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
        if (population.populationGrowthHistory == null)
            population.populationGrowthHistory = new List<float>();
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
