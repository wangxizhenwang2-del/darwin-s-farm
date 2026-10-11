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
        float demand = LandLifeRules.Demand(p.speciesAmount, v.s, v.m, v.f,
            fitness, LandRules);
        float food = p.trophicLevel == 0
            ? LandLifeRules.ForecastPlantIntake(block, p, v.t, v.h, v.m,
                v.s, v.f, false, LandRules)
            : Mathf.Min(demand, p.allocatedBiomass +
                Mathf.Max(0f, PredatorPotential(block, p, v) -
                    PredatorPotential(block, p, ReadLandTraits(p)))
                    * p.speciesAmount);
        float ratio = demand > 0f ? Mathf.Clamp01(food / demand) : 1f;
        float births = p.speciesAmount * LandRules.birthRate
            * v.f / 100f * fitness * ratio;
        if (p.trophicLevel == 0)
        {
            float totalDemand = demand;
            foreach (PopulationData other in block.community)
                if (other != p && other != null && other.trophicLevel == 0 &&
                    other.speciesAmount > 0)
                    totalDemand += LandLifeRules.Demand(other,
                        other.speciesAmount, LandLifeRules.Fitness(block,
                            other.fitTemperature, other.fitHumidity), LandRules);
            births *= LandLifeRules.DensityBirthMultiplier(totalDemand,
                block.habitatRecovery, LandRules);
        }
        float deaths = p.speciesAmount * LandRules.starvationDeathRate
            * (1f - ratio) + LandRules.coldDeathRate * p.speciesAmount
                * Mathf.Max(0f, (50f - block.temperature) / 50f)
                * (1f - v.s / 100f);
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
        return (scoreSum > 0f ? weighted / scoreSum : 0f)
            - LandLifeRules.AdultNeed(traits.s, traits.m, LandRules);
    }

    private float BestNeighborAttraction(BlockInfo source, PopulationData p,
        out int ceiling)
    {
        float best = 0f;
        ceiling = p.movementAbility;
        if (source.Neighbors == null) return 0f;
        int migrants = StandardMigrationAmount(p.speciesAmount);
        if (migrants <= 0) return 0f;
        float migrantDemand = LandLifeRules.Demand(migrants, p.size,
            p.movementAbility, p.fertility,
            LandLifeRules.Fitness(source, p.fitTemperature, p.fitHumidity),
            LandRules);
        foreach (BlockInfo neighbor in source.Neighbors)
        {
            if (neighbor == null || neighbor.waterCoverage != WaterCoverage.Land ||
                neighbor.community == null ||
                HasSameSpeciesResident(neighbor, p)) continue;
            int height = Mathf.Abs(neighbor.elevation - source.elevation);
            if (height > 1) continue;
            float free = NeighborFreeFood(neighbor, p);
            float attraction = free / Mathf.Max(1f, free + migrantDemand)
                * (1f - 0.2f * Mathf.Abs(source.temperature - neighbor.temperature)
                    / 100f - 0.2f * Mathf.Abs(source.humidity - neighbor.humidity)
                    / 100f);
            if (attraction > best)
            { best = Mathf.Clamp01(attraction); ceiling = height == 0
                ? LandRules.flatAttractionCeiling : LandRules.slopeAttractionCeiling; }
        }
        return best;
    }

    private float NeighborFreeFood(BlockInfo neighbor, PopulationData migrant)
    {
        float food = migrant.trophicLevel == 0
            ? Mathf.Max(0f, neighbor.plantBiomass +
                Mathf.Min(neighbor.habitatRecovery,
                    neighbor.maxPlantBiomass - neighbor.plantBiomass)) : 0f;
        if (migrant.trophicLevel > 0)
            foreach (PopulationData prey in neighbor.community)
                if (prey != null && prey.speciesAmount > 0 &&
                    FoodWeb.CanEat(migrant, prey))
                    food += prey.speciesAmount
                        * LandLifeRules.PreyEnergy(migrant, prey, LandRules);
        foreach (PopulationData resident in neighbor.community)
            if (resident != null && resident.speciesAmount > 0 &&
                resident.trophicLevel == migrant.trophicLevel)
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
        foreach (PopulationData p in block.community)
        {
            if (p == null || p.speciesAmount <= 0) continue;
            p.mutationDaysElapsed++;
            if (p.mutationDaysElapsed < Mathf.Max(1, mutationInterval)) continue;
            p.mutationDaysElapsed = 0;
            LandTraits current = ReadLandTraits(p);
            LandTraits next = current;
            float baseline = ForecastLandGrowth(block, p, current);
            float bestGain = 0.00001f;
            string effect = null;
            int ceiling;
            float attraction = BestNeighborAttraction(block, p, out ceiling);
            for (int trait = 0; trait < 5; trait++)
            {
                for (int d = -1; d <= 1; d += 2)
                {
                    LandTraits candidate = current;
                    if (trait == 0) candidate.t = Mathf.Clamp(candidate.t + d * mutationStep, 0f, 100f);
                    if (trait == 1) candidate.h = Mathf.Clamp(candidate.h + d * mutationStep, 0f, 100f);
                    if (trait == 2) candidate.m = Mathf.Clamp(candidate.m + d * mutationStep, 0f, 100f);
                    if (trait == 3) candidate.s = Mathf.Clamp(candidate.s + d * mutationStep, 10f, 90f);
                    if (trait == 4) candidate.f = Mathf.Clamp(candidate.f + d * mutationStep, 10f, 90f);
                    if (p.localSplitBranch && p.splitStrategy > 0 &&
                        (candidate.s < p.splitSizeAnchor ||
                         candidate.f > p.splitFertilityAnchor)) continue;
                    if (p.localSplitBranch && p.splitStrategy < 0 &&
                        (candidate.s > p.splitSizeAnchor ||
                         candidate.f < p.splitFertilityAnchor)) continue;
                    float previous = trait == 0 ? current.t : trait == 1 ? current.h :
                        trait == 2 ? current.m : trait == 3 ? current.s : current.f;
                    float proposed = trait == 0 ? candidate.t : trait == 1 ? candidate.h :
                        trait == 2 ? candidate.m : trait == 3 ? candidate.s : candidate.f;
                    if (Mathf.Abs(proposed - previous) < 0.0001f) continue;
                    float score = ForecastLandGrowth(block, p, candidate);
                    if (p.trophicLevel > 0 && trait >= 2)
                        score += p.speciesAmount * 0.01f * Mathf.Max(0f,
                            PredatorPotential(block, p, candidate)
                            - PredatorPotential(block, p, current));
                    if (trait == 2 && d > 0 &&
                        attraction >= LandRules.neighborAttractionThreshold &&
                        current.m < ceiling)
                        score += p.speciesAmount *
                            LandRules.attractionMovementGain * attraction * 0.001f;
                    float gain = (score - baseline) * selectionStrength;
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        next = candidate;
                        string name = trait == 0 ? "适温" : trait == 1 ? "适湿" :
                            trait == 2 ? "运动" : trait == 3 ? "体型" : "繁殖";
                        effect = name + (d > 0 ? "↑ " : "↓ ") +
                            Mathf.RoundToInt(previous) + "→" + Mathf.RoundToInt(proposed);
                    }
                }
            }
            decisions.Add(next);
            subjects.Add(p);
            effects.Add(effect);
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
            if (effects[i] != null) p.lastMutationEffect = effects[i];
        }
    }
}
