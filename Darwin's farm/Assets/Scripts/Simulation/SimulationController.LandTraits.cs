using System.Collections.Generic;
using UnityEngine;

public partial class SimulationController
{
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
            * (1f - ratio) + LandRules.coldDeathRate * p.speciesAmount
                * Mathf.Max(0f, (50f - block.temperature) / 50f)
                * (1f - v.s / 100f)
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

    private void EvolveCommunityNewRules(BlockInfo block)
    {
        if (!mutationEnabled || mutationStep <= 0f || selectionStrength <= 0f) return;
        var decisions = new List<LandTraits>(block.community.Count);
        var subjects = new List<PopulationData>(block.community.Count);
        var effects = new List<string>(block.community.Count);
        var gains = new List<float>(block.community.Count);
        foreach (PopulationData p in block.community)
        {
            if (p == null || p.speciesAmount <= 0) continue;
            p.mutationDaysElapsed++;
            if (p.mutationDaysElapsed < Mathf.Max(1, mutationInterval)) continue;
            p.mutationDaysElapsed = 0;
            LandTraits current = ReadLandTraits(p);
            LandTraits next = current;
            float baseline = ForecastLandGrowth(block, p, current);
            float bestGain = LandRules.mutationGainThreshold;
            string effect = null;
            int selectedCandidate = -1;
            int ceiling;
            float attraction = BestNeighborAttraction(block, p, out ceiling);
            // A directed split has already committed movement, body size and
            // fertility to its graph neighbor. Only climate traits keep adapting.
            int mutableTraitCount = p.directedSplitTarget != null ? 2 : 5;
            for (int trait = 0; trait < mutableTraitCount; trait++)
            {
                for (int d = -1; d <= 1; d += 2)
                {
                    LandTraits candidate = current;
                    if (trait == 0) candidate.t = Mathf.Clamp(candidate.t + d * mutationStep, 0f, 100f);
                    if (trait == 1) candidate.h = Mathf.Clamp(candidate.h + d * mutationStep, 0f, 100f);
                    if (trait == 2) candidate.m = Mathf.Clamp(candidate.m + d * mutationStep, 0f, 100f);
                    if (trait == 3) candidate.s = Mathf.Clamp(candidate.s + d * mutationStep, 1f, 100f);
                    if (trait == 4) candidate.f = Mathf.Clamp(candidate.f + d * mutationStep, 10f, 90f);
                    float previous = trait == 0 ? current.t : trait == 1 ? current.h :
                        trait == 2 ? current.m : trait == 3 ? current.s : current.f;
                    float proposed = trait == 0 ? candidate.t : trait == 1 ? candidate.h :
                        trait == 2 ? candidate.m : trait == 3 ? candidate.s : candidate.f;
                    if (Mathf.Abs(proposed - previous) < 0.0001f) continue;
                    float score = ForecastLandGrowth(block, p, candidate);
                    if (p.trophicLevel > 0 && trait >= 2)
                        score += p.speciesAmount * 0.01f *
                            (PredatorPotential(block, p, candidate)
                            - PredatorPotential(block, p, current));
                    if (trait == 2 && d > 0 &&
                        attraction >= LandRules.neighborAttractionThreshold &&
                        current.m < ceiling)
                        score += p.speciesAmount *
                            LandRules.attractionMovementGain * attraction * 0.001f;
                    float gain = (score - baseline) * selectionStrength
                        / Mathf.Max(1, p.speciesAmount);
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        next = candidate;
                        selectedCandidate = trait * 2 + (d > 0 ? 1 : 0);
                        string name = trait == 0 ? "适温" : trait == 1 ? "适湿" :
                            trait == 2 ? "运动" : trait == 3 ? "体型" : "繁殖";
                        effect = name + (d > 0 ? "↑ " : "↓ ") +
                            Mathf.RoundToInt(previous) + "→" + Mathf.RoundToInt(proposed);
                    }
                }
            }
            if (selectedCandidate < 0)
            {
                p.mutationSelectionProgress = 0f;
                p.mutationSelectionCandidate = -1;
            }
            else
            {
                if (p.mutationSelectionCandidate != selectedCandidate)
                    p.mutationSelectionProgress = 0f;
                p.mutationSelectionCandidate = selectedCandidate;
                p.mutationSelectionProgress += Mathf.Clamp01(
                    (bestGain - LandRules.mutationGainThreshold) /
                    LandRules.mutationGainDailyScale);
                if (p.mutationSelectionProgress < 1f)
                {
                    next = current;
                    effect = null;
                }
                else
                {
                    p.mutationSelectionProgress -= 1f;
                    p.mutationSelectionCandidate = -1;
                }
            }
            decisions.Add(next);
            subjects.Add(p);
            effects.Add(effect);
            gains.Add(bestGain);
        }
        for (int i = 0; i < subjects.Count; i++)
        {
            PopulationData p = subjects[i];
            LandTraits next = decisions[i];
            p.fitTemperature = Mathf.RoundToInt(next.t);
            p.fitHumidity = Mathf.RoundToInt(next.h);
            p.movementAbility = Mathf.RoundToInt(next.m);
            p.size = Mathf.RoundToInt(next.s);
            p.fertility = Mathf.RoundToInt(next.f);
            if (effects[i] != null)
            {
                p.lastMutationEffect = effects[i];
                LogEcology(currentSimulationDay, block, p, "trait_mutation",
                    effects[i] + " perCapitaGain=" + gains[i].ToString("F7") +
                    " threshold=" + LandRules.mutationGainThreshold.ToString("F7") +
                    " stock=" + block.plantBiomass.ToString("F0") +
                    " count=" + p.speciesAmount);
            }
        }
    }
}
