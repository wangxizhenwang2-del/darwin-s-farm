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

            LandTraits original = ReadLandTraits(population);
            float baseline = ForecastLandGrowth(block, population, original);
            V62NeighborForecast v62Best = enableV62Mobility &&
                selectionStrength > 0f ? BestV62Neighbor(block, population,
                    original) : default;
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
                        if ((enableNeighborMovementFormula || v62Best.Valid) &&
                            trait == 2)
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

            if (v62Best.Valid)
                AddV62MovementCandidates(choices, block, population,
                    original, v62Best);
            else if (!enableV62Mobility && enableNeighborMovementFormula &&
                selectionStrength > 0f)
            {
                OpportunityResult opportunity = BestRealNeighborOpportunity(
                    block, population, original, out float ecologicalMovementFloor);
                float valueHere = SustainableSurvivalValue(block, population, original);
                // Keep ordinary local movement selection when ecology supports it;
                // never let it bypass the target-protection rule.
                if (population.movementAbility < 100)
                {
                    int next = TraitLimit(2, population.movementAbility + 1);
                    LandTraits plus = ChangedTrait(original, 2, next);
                    float localGain = SustainableSurvivalValue(block, population, plus) - valueHere;
                    if (localGain > LandRules.mutationGainThreshold &&
                        !LeavesProtectedTarget(population, 2, next) &&
                        InfluenceNet(population, 2, currentSimulationDay, true) >= 0f &&
                        ModelRandom01(currentSimulationDay, "mutation.neighbor.localplus",
                            block, population) < naturalChance)
                        AddMutationCandidate(choices, 2, 1,
                            Mathf.Clamp(localGain * selectionStrength * 100f,
                                0.0001f, 5f), "sustainable");
                }

                if (opportunity.Eligible &&
                    InfluenceNet(population, 2, currentSimulationDay, true) >= 0f &&
                    (!LeavesProtectedTarget(population, 2,
                        population.movementAbility + 1) ||
                     NeighborMovementPolicyV2.MayCrossEvolutionProtection(
                        opportunity, population.movementAbility,
                        population.movementAbility + 1)))
                {
                    float probability = (float)NeighborMovementPolicyV2.NeighborGate(
                        opportunity.MutationWeightBonus);
                    float roll = ModelRandom01(currentSimulationDay,
                        "mutation.neighbor.v2.qualify", block, population);
                    if (roll < probability)
                        // B governs qualification probability, never a second
                        // microscopic relative weight that loses to climate.
                        AddMutationCandidate(choices, 2, 1,
                            selectionStrength, "neighbor_growth");
                    if (OnEcologyEvent != null || EnableEcologyConsoleLogs)
                        LogEcology(currentSimulationDay, block, population,
                            "neighbor_mutation_gate", "p=" +
                            probability.ToString("R") + " draw=" +
                            roll.ToString("R") + " accepted=" +
                            (roll < probability) + " target=" +
                            opportunity.DestinationKey);
                }

                if (population.movementAbility > 0 &&
                    InfluenceNet(population, 2, currentSimulationDay, true) <= 0f &&
                    !LeavesProtectedTarget(population, 2,
                        population.movementAbility - 1))
                {
                    LandTraits minus = ChangedTrait(original, 2,
                        population.movementAbility - 1);
                    float growthGain = SustainableSurvivalValue(block, population, minus) -
                        valueHere;
                    float maintenanceSaving = LandLifeRules.AdultNeed(original.s,
                        original.m, LandRules) - LandLifeRules.AdultNeed(
                            minus.s, minus.m, LandRules);
                    float probability = (float)NeighborMovementPolicyV2.RecoveryGate(
                        population.movementAbility, ecologicalMovementFloor,
                        growthGain, maintenanceSaving);
                    float roll = ModelRandom01(currentSimulationDay,
                        "mutation.neighbor.v2.recover", block, population);
                    if (roll < probability)
                        AddMutationCandidate(choices, 2, -1, 0.1f, "movement_cost_recovery");
                    else if (growthGain > LandRules.mutationGainThreshold &&
                        ModelRandom01(currentSimulationDay,
                            "mutation.neighbor.v2.naturalminus", block, population)
                            < naturalChance)
                        AddMutationCandidate(choices, 2, -1,
                            Mathf.Clamp(growthGain * selectionStrength * 100f,
                                0.0001f, 5f), "sustainable");
                }
            }

            for (int trait = 2; trait < 5; trait++)
            {
                float net = InfluenceNet(population, trait,
                    currentSimulationDay, false);
                if (net == 0f) continue;
                int direction = net > 0f ? 1 : -1;
                int next = TraitLimit(trait,
                    TraitValue(population, trait) + direction);
                if (next != TraitValue(population, trait) &&
                    !LeavesProtectedTarget(population, trait, next))
                    AddMutationCandidate(choices, trait, direction,
                        Mathf.Abs(net), "player");
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
                if (winner.sources.Contains("player") &&
                    population.directedInfluences != null)
                    foreach (DirectedInfluence influence in
                        population.directedInfluences)
                        if (influence != null &&
                            influence.trait == winner.trait &&
                            influence.weight * winner.direction > 0f)
                            influence.potential *= 0.5f;
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
