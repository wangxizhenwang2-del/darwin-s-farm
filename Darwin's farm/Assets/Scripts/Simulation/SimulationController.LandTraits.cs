using System.Collections.Generic;
using DarwinFarm.MigrationOpportunity;
using UnityEngine;

public partial class SimulationController
{
    // Neighbor opportunity must participate in actual movement selection in
    // normal play, not only in the core test profile. Otherwise an attractive
    // empty tile is displayed while movement has no way to evolve toward it.
    [SerializeField] private bool enableNeighborMovementFormula = true;
    public bool EnableNeighborMovementFormula
    {
        get => enableNeighborMovementFormula;
        set => enableNeighborMovementFormula = value;
    }
    private string GetLandTraitDirection(BlockInfo block, PopulationData p)
    {
        if (p == null) return "当前变异：无种群";
        if (!mutationEnabled || mutationStep <= 0f) return "当前变异：已关闭";
        return "当前变异：" + (string.IsNullOrEmpty(p.lastMutationEffect)
            ? "暂无记录" : p.lastMutationEffect);
    }

    private struct LandTraits
    {
        public float t, h, m, s, f;
    }

    private static LandTraits ReadLandTraits(PopulationData p) => new LandTraits
    { t = p.fitTemperature, h = p.fitHumidity, m = p.movementAbility,
      s = p.size, f = p.fertility };

    public struct LandSelectionSignals
    {
        public float stockDays, renewalCoverage, neighborAttraction;
        public float sizePressure, fertilityPressure, movementPressure;
        public float populationGrowth;
        public int movementCeiling;
    }

    public LandSelectionSignals ReadLandSelectionSignals(BlockInfo block,
        PopulationData p)
    {
        if (block == null || p == null || p.speciesAmount <= 0)
            return new LandSelectionSignals();
        float stockDays = LandLifeRules.AccessibleFoodStockDays(block, p, LandRules);
        float capacity = LandLifeRules.EnvironmentalCarryingCapacity(
            block, p, LandRules);
        float renewalCoverage = capacity / Mathf.Max(1, p.speciesAmount);
        int ceiling;
        float attraction;
        if (enableV62Mobility)
        {
            V62NeighborForecast best = BestV62Neighbor(block, p,
                ReadLandTraits(p));
            attraction = best.Valid ? best.normalizedGain : 0f;
            ceiling = best.Valid ? 100 : p.movementAbility;
        }
        else if (enableNeighborMovementFormula)
        {
            attraction = BestSustainableNeighborOpportunity(block, p,
                ReadLandTraits(p), out _, out _, out _);
            ceiling = attraction > 0f ? 100 : p.movementAbility;
        }
        else attraction = BestNeighborAttraction(block, p, out ceiling);
        bool neighborOpportunity = enableV62Mobility || enableNeighborMovementFormula
            ? attraction > 0f && p.movementAbility < 100
            : attraction >= LandRules.neighborAttractionThreshold &&
                p.movementAbility < ceiling;
        return new LandSelectionSignals
        {
            stockDays = stockDays,
            renewalCoverage = renewalCoverage,
            neighborAttraction = attraction,
            movementCeiling = ceiling,
            sizePressure = ResourcePressure(stockDays,
                LandRules.lowStockDays, LandRules.highStockDays),
            fertilityPressure = ResourcePressure(renewalCoverage,
                LandRules.lowRenewalCoverage, LandRules.highRenewalCoverage),
            movementPressure = neighborOpportunity ? attraction : -1f,
            populationGrowth = LandLifeRules.Average(p.populationGrowthHistory)
        };
    }

    private static float ResourcePressure(float value, float low, float high)
    {
        if (value < low)
            return -Mathf.Clamp01((low - value) / Mathf.Max(0.001f, low));
        if (value > high)
            return Mathf.Clamp01((value - high) / Mathf.Max(0.001f, high));
        return 0f;
    }

    public static bool IsDirectedTraitLocked(PopulationData p, int day) => false;

    private float ForecastLandGrowth(BlockInfo block, PopulationData p, LandTraits v)
    {
        float fitness = LandLifeRules.Fitness(block, v.t, v.h);
        // Compare the whole group's candidate adults against the current
        // community. Young are not allowed to relax their own birth pressure;
        // adult body size and movement do change the real maintenance load.
        float birthFactor = 1f;
        float ratio;
        if (p.trophicLevel == 0)
            ratio = LandLifeRules.ForecastWholePlantSatisfaction(block, p,
                v.t, v.h, v.m, v.s, v.f, LandRules, out birthFactor);
        else
        {
            float demand = LandLifeRules.Demand(p.speciesAmount, v.s, v.m,
                v.f, fitness, LandRules);
            float food = Mathf.Clamp(p.allocatedBiomass +
                (PredatorPotential(block, p, v) -
                    PredatorPotential(block, p, ReadLandTraits(p)))
                    * p.speciesAmount, 0f, demand);
            ratio = demand > 0f ? Mathf.Clamp01(food / demand) : 1f;
        }
        float births = p.speciesAmount * LandRules.birthRate
            * v.f / 100f * fitness * ratio * birthFactor;
        float deaths = p.speciesAmount * LandRules.starvationDeathRate
            * (1f - ratio) + LandLifeRules.ColdDeaths(p.speciesAmount,
                block.temperature, LandRules)
            + LandRules.climateMismatchDeathRate * p.speciesAmount
                * (1f - fitness)
            + LandLifeRules.NaturalAdultDeaths(p.speciesAmount, v.s,
                LandRules)
            + LandLifeRules.ReproductionStressDeaths(p.speciesAmount,
                v.s, v.f, LandRules, birthFactor, ratio, fitness);
        float preyRisk = 0f;
        foreach (PopulationData hunter in block.community)
            if (hunter != null && hunter.speciesAmount > 0 &&
                FoodWeb.CanEat(hunter, p))
            {
                float chase = Mathf.Clamp(1f + (hunter.movementAbility - v.m)
                    / Mathf.Max(1f, LandRules.chaseScale), 0.1f, 1.5f);
                float sizeRatio = (hunter.size + 10f) / (v.s + 10f);
                preyRisk += hunter.speciesAmount
                    * Mathf.Min(1f, 10f / Mathf.Max(1f, v.s))
                    * Mathf.Clamp01(LandRules.baseCaptureChance * chase * Mathf.Clamp(sizeRatio
                        * sizeRatio, 0.1f, 1.5f));
            }
        return births - deaths - preyRisk;
    }

    private float PredatorPotential(BlockInfo block, PopulationData p,
        LandTraits traits)
    {
        float weighted = 0f, scoreSum = 0f;
        foreach (PopulationData prey in block.community)
        {
            if (prey == null || prey.speciesAmount <= 0 ||
                !FoodWeb.CanEat(p, prey)) continue;
            float chase = Mathf.Clamp(1f + (traits.m - prey.movementAbility)
                / Mathf.Max(1f, LandRules.chaseScale), 0.1f, 1.5f);
            float sizeRatio = (traits.s + 10f) / (prey.size + 10f);
            float chance = Mathf.Clamp01(LandRules.baseCaptureChance * chase
                * Mathf.Clamp(sizeRatio * sizeRatio, 0.1f, 1.5f));
            float attempts = Mathf.Min(1f, 10f / Mathf.Max(1f, prey.size));
            float energy = LandRules.preyEnergyPerSize * prey.size
                * Mathf.Min(1f, traits.s / (prey.size + 15f));
            float score = attempts * chance * energy
                * prey.speciesAmount / (prey.speciesAmount + 50f);
            weighted += score * score;
            scoreSum += score;
        }
        return scoreSum > 0f ? weighted / scoreSum : 0f;
    }

    // V is a bounded daily net survival rate. One population per day of net
    // growth spans the full [0,1] scale around neutral 0.5. Both locations
    // use sustainable production and the same resident-competition forecast.
    private float SustainableSurvivalValue(BlockInfo block, PopulationData p,
        LandTraits traits, float reservedFood = 0f)
    {
        if (block == null || block.community == null || p == null ||
            p.speciesAmount <= 0) return 0f;
        int count = p.speciesAmount;
        float fitness = LandLifeRules.Fitness(block, traits.t, traits.h);
        float adult = count * LandLifeRules.AdultNeed(traits.s, traits.m,
            LandRules);
        float food = p.trophicLevel == 0
            ? LandLifeRules.ForecastSustainablePlantIntake(block, p,
                traits.t, traits.h, traits.m, traits.s, traits.f, LandRules)
            : LandLifeRules.AvailableSustainablePreyFood(block, p, LandRules);
        food = Mathf.Max(0f, food - reservedFood);
        float sat = adult > 0f ? Mathf.Clamp01(food / adult) : 1f;
        float opportunity = count * LandRules.birthRate * traits.f / 100f
            * fitness * sat;
        float newbornCost = LandLifeRules.YoungNeed(traits.s, LandRules) +
            LandLifeRules.BodyMass(traits.s, LandRules);
        float paidBirths = Mathf.Min(opportunity, Mathf.Max(0f, food - adult) /
            Mathf.Max(0.0001f, newbornCost));
        float deaths = LandRules.starvationDeathRate * count * (1f - sat)
            + LandLifeRules.ColdDeaths(count, block.temperature, LandRules)
            + LandRules.climateMismatchDeathRate * count * (1f - fitness)
            + LandLifeRules.NaturalAdultDeaths(count, traits.s, LandRules)
            + LandLifeRules.PaidBirthStressDeaths(Mathf.FloorToInt(paidBirths),
                traits.s, traits.f, LandRules);
        foreach (PopulationData hunter in block.community)
            if (hunter != null && hunter.speciesAmount > 0 &&
                FoodWeb.CanEat(hunter, p))
            {
                float chase = Mathf.Clamp(1f +
                    (hunter.movementAbility - traits.m) /
                    Mathf.Max(1f, LandRules.chaseScale), 0.1f, 1.5f);
                float sizeRatio = (hunter.size + 10f) / (traits.s + 10f);
                deaths += hunter.speciesAmount *
                    Mathf.Min(1f, 10f / Mathf.Max(1f, traits.s)) *
                    Mathf.Clamp01(LandRules.baseCaptureChance * chase *
                        Mathf.Clamp(sizeRatio * sizeRatio, 0.1f, 1.5f));
            }
        return Mathf.Clamp01(0.5f + (paidBirths - deaths) / count);
    }

    private float BestSustainableNeighborOpportunity(BlockInfo source,
        PopulationData p, LandTraits traits, out BlockInfo bestNeighbor,
        out float sourceValue, out float neighborValue)
    {
        bestNeighbor = null;
        neighborValue = 0f;
        sourceValue = SustainableSurvivalValue(source, p, traits);
        if (source == null || source.Neighbors == null ||
            p.ecologicalNiche != EcologicalNiche.Land) return 0f;
        float best = 0f;
        foreach (BlockInfo neighbor in source.Neighbors)
        {
            if (neighbor == null || neighbor == source ||
                neighbor.waterCoverage != WaterCoverage.Land ||
                neighbor.community == null || neighbor.Neighbors == null ||
                !blockInfos.Contains(neighbor)) continue;
            bool reciprocal = false;
            foreach (BlockInfo back in neighbor.Neighbors)
                if (back == source) { reciprocal = true; break; }
            if (!reciprocal) continue;
            int step = Mathf.Abs(neighbor.elevation - source.elevation);
            if (step > 1) continue;
            if (!LandMovementOpportunity.CanOfferNeighborSlot(
                LivingPopulationCount(neighbor), HasSameSpeciesResident(neighbor, p)))
                continue;
            float value = SustainableSurvivalValue(neighbor, p, traits);
            float opportunity = (step == 0 ? 1f : 0.5f) *
                Mathf.Max(0f, value - sourceValue);
            if (opportunity > best || opportunity == best &&
                opportunity > 0f && bestNeighbor != null &&
                string.CompareOrdinal(StableTileKey(neighbor),
                    StableTileKey(bestNeighbor)) < 0)
            {
                best = opportunity;
                bestNeighbor = neighbor;
                neighborValue = value;
            }
        }
        return best;
    }

    private float BestNeighborAttraction(BlockInfo source, PopulationData p,
        out int ceiling)
    {
        float best = 0f;
        ceiling = p.movementAbility;
        if (source.Neighbors == null) return 0f;
        int migrants = StandardMigrationAmount(p.speciesAmount);
        if (migrants <= 0) return 0f;
        foreach (BlockInfo neighbor in source.Neighbors)
        {
            if (neighbor == null || neighbor.waterCoverage != WaterCoverage.Land ||
                neighbor.community == null ||
                HasSameSpeciesResident(neighbor, p)) continue;
            int height = Mathf.Abs(neighbor.elevation - source.elevation);
            if (height > 1) continue;
            float attraction = NeighborAttraction(source, neighbor, p, migrants);
            if (attraction > best)
            { best = Mathf.Clamp01(attraction); ceiling = height == 0
                ? LandRules.flatAttractionCeiling : LandRules.slopeAttractionCeiling; }
        }
        return best;
    }

    private float NeighborFreeFood(BlockInfo neighbor, PopulationData migrant)
    {
        if (migrant.trophicLevel > 0)
            return LandLifeRules.AvailableSustainablePreyFood(neighbor,
                migrant, LandRules);
        float food = Mathf.Max(0f, neighbor.plantBiomass +
            Mathf.Min(neighbor.habitatRecovery,
                neighbor.maxPlantBiomass - neighbor.plantBiomass));
        foreach (PopulationData resident in neighbor.community)
            if (resident != null && resident.speciesAmount > 0 &&
                resident.trophicLevel == 0)
                food -= LandLifeRules.Demand(resident, resident.speciesAmount,
                    LandLifeRules.Fitness(neighbor, resident.fitTemperature,
                        resident.fitHumidity), LandRules);
        return Mathf.Max(0f, food);
    }

    private struct MutationCandidate
    {
        public int trait, direction;
        public float weight;
        public string sources;
    }

    private static int TraitValue(PopulationData population, int trait)
    {
        switch (trait)
        {
            case 0: return population.fitTemperature;
            case 1: return population.fitHumidity;
            case 2: return population.movementAbility;
            case 3: return population.size;
            default: return population.fertility;
        }
    }

    private static int TraitLimit(int trait, int value) =>
        Mathf.Clamp(value, trait == 3 ? 1 : trait == 4 ? 10 : 0,
            trait == 4 ? 90 : 100);

    private static LandTraits ChangedTrait(LandTraits value, int trait, int next)
    {
        if (trait == 0) value.t = next;
        else if (trait == 1) value.h = next;
        else if (trait == 2) value.m = next;
        else if (trait == 3) value.s = next;
        else value.f = next;
        return value;
    }

    private static void AddMutationCandidate(List<MutationCandidate> choices,
        int trait, int direction, float weight, string source)
    {
        if (weight <= 0f) return;
        for (int i = 0; i < choices.Count; i++)
            if (choices[i].trait == trait &&
                choices[i].direction == direction)
            {
                MutationCandidate existing = choices[i];
                existing.weight += weight;
                existing.sources += "+" + source;
                choices[i] = existing;
                return;
            }
        choices.Add(new MutationCandidate
        {
            trait = trait, direction = direction,
            weight = weight, sources = source
        });
    }

    private bool TryApplyRecoveryGuidance(BlockInfo block,
        PopulationData population)
    {
        RecoveryGuidanceTransaction transaction = block.recoveryGuidance;
        FertilityGuidancePulse redeemed = population.fertilityGuidance;
        int day = currentSimulationDay;
        if (population.trophicLevel != 0 || transaction == null ||
            transaction.direction == 0 || day < transaction.firstDay ||
            day > transaction.lastDay || transaction.used >= transaction.cap ||
            redeemed == null || redeemed.transactionKey != transaction.key)
            return false;
        int current = population.fertility;
        if (transaction.direction > 0 ? current >= 85 : current <= 15)
            return false;
        int next = current + transaction.direction;
        float roll = ModelRandom01(day, "mutation.recovery.guidance",
            block, population);
        if (LeavesProtectedTarget(population, 4, next) ||
            roll >= transaction.probability) return false;
        transaction.used++;
        redeemed.used++;
        population.fertility = next;
        population.lastMutationEffect = "4" +
            (transaction.direction > 0 ? "↑" : "↓") +
            " " + current + "→" + next;
        population.mutationSelectionProgress = 0f;
        population.mutationSelectionCandidate = -1;
        LogV4Mutation(block, population, "recovery_guidance", 4,
            transaction.direction, transaction.probability, roll, 0f,
            current, next);
        return true;
    }

    private void LogV4Mutation(BlockInfo block, PopulationData population,
        string reason, int trait, int direction, float probability,
        float hitRoll, float choiceRoll, int previous, int next)
    {
        if (OnEcologyEvent == null && !EnableEcologyConsoleLogs) return;
        string target = population.trackedEvolutionTarget == null ? "" :
            StableSpeciesKey(population.trackedEvolutionTarget);
        LogEcology(currentSimulationDay, block, population,
            "mutation_choice", "reason=" + reason + " candidate=" +
            trait + (direction > 0 ? "+" : "-") +
            " probability=" + probability.ToString("R",
                System.Globalization.CultureInfo.InvariantCulture) +
            " hitRoll=" + hitRoll.ToString("R",
                System.Globalization.CultureInfo.InvariantCulture) +
            " choiceRoll=" + choiceRoll.ToString("R",
                System.Globalization.CultureInfo.InvariantCulture) +
            " seed=" + SimulationSeed + " before=" + previous +
            " after=" + next + " target=" + target +
            " protected=false occupiedSlot=true");
    }

    private struct ChanceMutation
    {
        public int trait, direction;
        public float probability;
        public string source;
        public bool bypassProtection;
    }

    private static void OfferChance(List<ChanceMutation> choices,
        PopulationData population, int trait, int direction,
        float probability, string source = null,
        bool bypassProtection = false)
    {
        int current = TraitValue(population, trait);
        if (probability <= 0f ||
            TraitLimit(trait, current + direction) == current) return;
        choices.Add(new ChanceMutation { trait = trait, direction = direction,
            probability = Mathf.Clamp01(probability), source = source,
            bypassProtection = bypassProtection });
    }

    private bool ChooseChanceMutation(BlockInfo block, PopulationData population,
        List<ChanceMutation> choices, string phase)
    {
        if (choices.Count == 0) return false;
        float noHit = 1f, total = 0f;
        foreach (ChanceMutation option in choices)
        {
            noHit *= 1f - option.probability;
            total += option.probability;
        }
        float hitRoll = ModelRandom01(currentSimulationDay,
            phase + ".hit", block, population);
        if (hitRoll >= 1f - noHit) return false;
        float choiceRoll = ModelRandom01(currentSimulationDay,
            phase + ".choose", block, population);
        float draw = choiceRoll * total;
        ChanceMutation selected = choices[choices.Count - 1];
        foreach (ChanceMutation option in choices)
        {
            draw -= option.probability;
            if (draw < 0f) { selected = option; break; }
        }
        int previous = TraitValue(population, selected.trait);
        int next = previous + selected.direction;
        if (!selected.bypassProtection &&
            LeavesProtectedTarget(population, selected.trait, next))
            return false;
        StockGuidanceClaim stockClaim = selected.source == "stock_guidance"
            ? StockClaim(block, population) : null;
        if (selected.source == "stock_guidance" && stockClaim == null)
            return false;
        if (selected.source == "neighbor_guidance")
            MarkNeighborMovementHit(block, population, previous);
        if (selected.trait == 2) population.movementAbility = next;
        else if (selected.trait == 3) population.size = next;
        else if (selected.trait == 4) population.fertility = next;
        else return false;
        if (selected.source == "stock_guidance")
        {
            stockClaim.used++;
            population.stockGuidance.used = stockClaim.used;
        }
        else if (selected.source == "recovery_guidance")
        {
            block.recoveryGuidance.used++;
            population.fertilityGuidance.used++;
        }
        else if (selected.source == "cliff_recovery" &&
            population.cliffMovementRecovery != null &&
            population.movementAbility <=
                (population.trackedEvolutionTarget != null &&
                 !HasLivingSpecies(block, population.trackedEvolutionTarget)
                    ? population.trackedEvolutionTarget.baseMovementAbility
                    : population.cliffMovementRecovery.anchorMovement))
            StopCliffRecovery(block, population, "goal_reached");
        population.lastMutationEffect = selected.trait +
            (selected.direction > 0 ? "↑" : "↓") +
            " " + previous + "→" + next;
        population.mutationSelectionProgress = 0f;
        population.mutationSelectionCandidate = -1;
        LogV4Mutation(block, population,
            selected.source ?? phase, selected.trait,
            selected.direction, selected.probability, hitRoll,
            choiceRoll, previous, next);
        return true;
    }

    private void OfferRecoveryGuidance(BlockInfo block,
        PopulationData population, List<ChanceMutation> choices)
    {
        RecoveryGuidanceTransaction transaction = block.recoveryGuidance;
        FertilityGuidancePulse redeemed = population.fertilityGuidance;
        int day = currentSimulationDay;
        if (population.trophicLevel != 0 || transaction == null ||
            transaction.direction == 0 || day < transaction.firstDay ||
            day > transaction.lastDay || transaction.used >= transaction.cap ||
            redeemed == null || redeemed.transactionKey != transaction.key)
            return;
        if (transaction.direction > 0 ? population.fertility >= 85 :
            population.fertility <= 15) return;
        int next = population.fertility + transaction.direction;
        if (LeavesProtectedTarget(population, 4, next)) return;
        OfferChance(choices, population, 4, transaction.direction,
            transaction.probability, "recovery_guidance");
    }

    private void OfferStockGuidance(BlockInfo block,
        PopulationData population, List<ChanceMutation> choices)
    {
        float probability = StockGuidanceProbability(block, population,
            currentSimulationDay);
        if (probability > 0f)
            OfferChance(choices, population, 3,
                block.stockGuidance.direction, probability,
                "stock_guidance", true);
    }

    private bool TryDirectedMutation(BlockInfo block,
        PopulationData population)
    {
        var choices = new List<ChanceMutation>(7);
        OfferCliffRecovery(block, population, choices);
        OfferRecoveryGuidance(block, population, choices);
        OfferStockGuidance(block, population, choices);
        OfferPredatorDirected(block, population, choices);
        OfferNeighborMovement(block, population, choices);
        return ChooseChanceMutation(block, population, choices,
            "mutation.directed");
    }

    private bool TryPredatorDirected(BlockInfo block,
        PopulationData population)
    {
        var choices = new List<ChanceMutation>(2);
        OfferPredatorDirected(block, population, choices);
        return ChooseChanceMutation(block, population, choices,
            "mutation.predator.directed");
    }

    private void OfferPredatorDirected(BlockInfo block,
        PopulationData population, List<ChanceMutation> choices)
    {
        if (population.trophicLevel <= 0 ||
            !FoodWeb.HasLandPredatorDiet(population)) return;
        int sizeGoal = population.size;
        bool knownPrey = false;
        foreach (PopulationData prey in block.community)
        {
            if (prey == null || prey.speciesAmount <= 0 ||
                !FoodWeb.CanEat(population, prey)) continue;
            sizeGoal = Mathf.Max(sizeGoal, Mathf.CeilToInt(
                Mathf.Sqrt(1.5f) * (prey.size + 10f) - 10f));
            if (prey.preyNetRenewalHistory != null &&
                prey.preyNetRenewalHistory.Count > 0) knownPrey = true;
        }
        // The supplied rules do not define s_escape numerically. Legal
        // hunters remain observable, but no escape-size candidate is invented.
        sizeGoal = Mathf.Clamp(sizeGoal, 1, 100);
        float satisfaction = population.actualEnergySatisfactionToday;
        if (sizeGoal > population.size)
            OfferChance(choices, population, 3, 1,
                0.25f * Mathf.Clamp01((sizeGoal - population.size) / 25f)
                * Mathf.Clamp01((satisfaction - 0.6f) / 0.35f));
        if (knownPrey)
        {
            float sustainable = LandLifeRules.AvailableSustainablePreyFood(
                block, population, LandRules);
            float adult = population.speciesAmount * LandLifeRules.AdultNeed(
                population.size, population.movementAbility, LandRules);
            float ratio = adult > 0f ? sustainable / adult : 0f;
            float probability = 0.25f * Mathf.Clamp01(
                (Mathf.Abs(ratio - 1f) - 0.10f) / 0.65f);
            int direction = ratio > 1.1f ? 1 : ratio < 0.9f ? -1 : 0;
            if (direction != 0 && !LeavesProtectedTarget(population, 4,
                population.fertility + direction))
                OfferChance(choices, population, 4, direction, probability);
        }
        else if (OnEcologyEvent != null || EnableEcologyConsoleLogs)
            LogEcology(currentSimulationDay, block, population,
                "mutation_supply_unknown", "prey renewal history unavailable");
        choices.RemoveAll(option => LeavesProtectedTarget(population,
            option.trait, TraitValue(population, option.trait) +
                option.direction));
        // The unified directed lottery owns the only mutation slot.
    }

    private static int TargetTraitValue(SpeciesData target, int trait) =>
        trait == 0 ? target.baseFitTemperature :
        trait == 1 ? target.baseFitHumidity :
        trait == 2 ? target.baseMovementAbility :
        trait == 3 ? target.baseSize : target.baseFertility;

    private float OtherTraitDistance(PopulationData population,
        SpeciesData target, int excluded)
    {
        float distance = 0f;
        for (int trait = 0; trait < 5; trait++)
            if (trait != excluded)
                distance += Mathf.Abs(TraitValue(population, trait) -
                    TargetTraitValue(target, trait));
        return distance / 4f;
    }

    private bool TrySiblingAttraction(BlockInfo block,
        PopulationData population)
    {
        if (population.trophicLevel <= 0) return false;
        SpeciesData target = population.trackedEvolutionTarget;
        if (target == null || HasLivingSpecies(block, target)) return false;
        var choices = new List<ChanceMutation>(2);
        for (int trait = 3; trait <= 4; trait++)
        {
            int delta = TargetTraitValue(target, trait) -
                TraitValue(population, trait);
            if (Mathf.Abs(delta) <= 5) continue;
            int direction = delta > 0 ? 1 : -1;
            if (LeavesProtectedTarget(population, trait,
                TraitValue(population, trait) + direction)) continue;
            float distance = OtherTraitDistance(population, target, trait);
            OfferChance(choices, population, trait, direction,
                0.02f + 0.08f * Mathf.Max(0f, 1f - distance / 30f));
        }
        return ChooseChanceMutation(block, population, choices,
            "mutation.sibling.sizefertility");
    }

    private bool TryNaturalRecovery(BlockInfo block,
        PopulationData population)
    {
        var choices = new List<ChanceMutation>(2);
        SpeciesData target = population.trackedEvolutionTarget;
        if (target != null && !HasLivingSpecies(block, target) &&
            population.movementAbility > target.baseMovementAbility + 5 &&
            !LeavesProtectedTarget(population, 2,
                population.movementAbility - 1))
        {
            float distance = OtherTraitDistance(population, target, 2);
            OfferChance(choices, population, 2, -1,
                0.02f + (population.trophicLevel == 0 ? .53f : .38f)
                    * Mathf.Max(0f, 1f - distance / 30f));
        }
        if (population.trophicLevel > 0 &&
            FoodWeb.HasLandPredatorDiet(population) && population.size > 1 &&
            LandLifeRules.AdultNeed(population.size - 1,
                population.movementAbility, LandRules) <
            LandLifeRules.AdultNeed(population.size,
                population.movementAbility, LandRules) &&
            !LeavesProtectedTarget(population, 3, population.size - 1))
            OfferChance(choices, population, 3, -1,
                0.15f * Mathf.Clamp01((0.95f -
                    population.actualEnergySatisfactionToday) / 0.35f));
        return ChooseChanceMutation(block, population, choices,
            "mutation.natural.recovery");
    }

    private void EvolveCommunityNewRules(BlockInfo block)
    {
        if (!mutationEnabled || mutationStep <= 0f) return;
        foreach (PopulationData population in block.community)
        {
            if (population == null || population.speciesAmount <= 0 ||
                population.ecologicalNiche != EcologicalNiche.Land) continue;
            population.mutationDaysElapsed++;
            if (population.mutationDaysElapsed < Mathf.Max(1, mutationInterval))
                continue;
            population.mutationDaysElapsed = 0;
            UpdateInfluences(population, currentSimulationDay);
            RefreshTrackedTarget(block, population, currentSimulationDay);
            ReleaseTargetProtectionFIFO(block, population);
            if (TryDirectedMutation(block, population)) continue;
            if (TrySiblingAttraction(block, population)) continue;
            if (TryNaturalRecovery(block, population)) continue;

            LandTraits original = ReadLandTraits(population);
            float baseline = ForecastLandGrowth(block, population, original);
            var choices = new List<MutationCandidate>(10);
            for (int trait = 0; trait < 2; trait++)
            {
                int current = TraitValue(population, trait);
                int climate = trait == 0 ? block.temperature : block.humidity;
                if (current == climate) continue;
                int direction = climate > current ? 1 : -1;
                AddMutationCandidate(choices, trait, direction,
                    1f + Mathf.Abs(climate - current) / 20f, "climate");
            }

            float capacity = LandLifeRules.EnvironmentalCarryingCapacity(
                block, population, LandRules);
            float ratio = capacity > 0f
                ? population.speciesAmount / capacity : 1f;
            float naturalChance = 0.5f - 0.495f *
                Mathf.Clamp01(ratio / 0.95f);
            if (selectionStrength > 0f)
                for (int trait = 2; trait < 5; trait++)
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        // V4 movement-up probability is still unspecified.
                        // Old V2 neighbor gain is not a natural mutation source.
                        if (trait == 2 && direction > 0)
                            continue;
                        int next = TraitLimit(trait,
                            TraitValue(population, trait) + direction);
                        if (next == TraitValue(population, trait) ||
                            LeavesProtectedTarget(population, trait, next) ||
                            InfluenceNet(population, trait,
                                currentSimulationDay, true) * direction < 0f)
                            continue;
                        LandTraits changed = ChangedTrait(original, trait, next);
                        float perHeadGain = (ForecastLandGrowth(block,
                            population, changed) - baseline) /
                            Mathf.Max(1, population.speciesAmount);
                        if (perHeadGain <= LandRules.mutationGainThreshold ||
                            ModelRandom01(currentSimulationDay,
                                "mutation.natural", block, population,
                                null, trait * 2 + (direction > 0 ? 1 : 0))
                                >= naturalChance) continue;
                        AddMutationCandidate(choices, trait, direction,
                            Mathf.Clamp(perHeadGain * selectionStrength * 100f,
                                0.05f, 5f), "sustainable");
                    }

            float totalWeight = 0f;
            bool emitMutationTrace = OnEcologyEvent != null ||
                EnableEcologyConsoleLogs;
            var details = emitMutationTrace ?
                new System.Text.StringBuilder() : null;
            foreach (MutationCandidate candidate in choices)
            {
                totalWeight += candidate.weight;
                if (emitMutationTrace)
                {
                    if (details.Length > 0) details.Append(',');
                    details.Append(candidate.trait).Append(':')
                        .Append(candidate.direction > 0 ? '+' : '-')
                        .Append(':').Append(candidate.sources)
                        .Append(':').Append(candidate.weight.ToString("F3"));
                }
            }
            float draw = ModelRandom01(currentSimulationDay,
                "mutation.choose", block, population) * totalWeight;
            int selected = -1;
            for (int i = 0; i < choices.Count; i++)
            {
                draw -= choices[i].weight;
                if (draw < 0f) { selected = i; break; }
            }
            if (selected < 0 && choices.Count > 0) selected = choices.Count - 1;
            population.mutationSelectionProgress = 0f;
            population.mutationSelectionCandidate = -1;
            if (selected >= 0)
            {
                MutationCandidate winner = choices[selected];
                int previous = TraitValue(population, winner.trait);
                int next = previous + winner.direction;
                if (winner.trait == 0) population.fitTemperature = next;
                else if (winner.trait == 1) population.fitHumidity = next;
                else if (winner.trait == 2) population.movementAbility = next;
                else if (winner.trait == 3) population.size = next;
                else population.fertility = next;
                population.lastMutationEffect = winner.trait +
                    (winner.direction > 0 ? "↑" : "↓") +
                    " " + previous + "→" + next;
            }
            if (emitMutationTrace)
                LogEcology(currentSimulationDay, block, population,
                    "mutation_choice", "candidates=[" + details +
                    "] totalWeight=" + totalWeight.ToString("R",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    " draw=" + ModelRandom01(currentSimulationDay,
                        "mutation.choose", block, population).ToString("F6") +
                    " selected=" + (selected >= 0
                        ? population.lastMutationEffect : "none") +
                    " occupiedSlot=" + (selected >= 0));
        }
    }
}
