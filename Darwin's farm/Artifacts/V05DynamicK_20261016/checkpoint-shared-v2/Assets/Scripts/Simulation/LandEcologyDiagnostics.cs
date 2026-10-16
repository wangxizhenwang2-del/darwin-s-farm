using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class LandEquilibriumEstimate
{
    public string status;
    public int lower, upper, crossings;
    public LandNetGrowthSample[] samples;
}

[Serializable]
public struct LandNetGrowthSample
{
    public int population, births, naturalDeaths, predationDeaths, net;
}

// Bounded, one-day counterfactuals on disposable copies. This never joins the
// world's block list, advances its clock, or calls the model random mechanism.
internal static class LandEcologyDiagnostics
{
    internal static LandEquilibriumEstimate Estimate(BlockInfo source,
        PopulationData target, LandRuleTuning rules)
    {
        float budget = LandLifeRules.SustainablePlantSupply(source, rules);
        if (target.trophicLevel > 0)
            foreach (PopulationData prey in source.community)
                if (prey != null && FoodWeb.CanEat(target, prey))
                    budget += Mathf.Max(0f, prey.bodyBiomass);
        int bound = Mathf.Clamp(Mathf.Max(target.speciesAmount * 4,
            Mathf.CeilToInt(budget / Mathf.Max(1f,
                LandLifeRules.AdultNeed(target.size,
                    target.movementAbility, rules))) * 2), 100, 100000);
        var counts = new SortedSet<int> { 1, 2, 5, 10, 20, 50, 100 };
        for (int i = 1; i <= 64; i++)
            counts.Add(Mathf.Max(1, bound * i / 64));
        var samples = new List<LandNetGrowthSample>();
        int crossings = 0, lower = 0, upper = 0;
        LandNetGrowthSample previous = default;
        foreach (int count in counts)
        {
            if (count > bound) continue;
            LandNetGrowthSample sample = Forecast(source, target, count, rules);
            samples.Add(sample);
            if (previous.population > 0 && previous.net > 0 && sample.net <= 0)
            {
                crossings++;
                lower = previous.population;
                upper = count;
            }
            previous = sample;
        }
        string status = crossings == 0 ? "NO_STABLE_SOLUTION_IN_BOUND" :
            crossings > 1 ? "UNCERTAIN_MULTIPLE_CROSSINGS" :
            "LOCAL_ONE_DAY_CANDIDATE";
        foreach (PopulationData resident in source.community)
            if (resident != null &&
                Mathf.Abs(LandLifeRules.Average(resident.populationGrowthHistory)) > 0.02f)
                status = "UNCERTAIN_CHANGING_COMMUNITY";
        if (crossings == 1)
            while (lower + 1 < upper)
            {
                int mid = lower + (upper - lower) / 2;
                LandNetGrowthSample sample = Forecast(source, target, mid, rules);
                samples.Add(sample);
                if (sample.net > 0) lower = mid; else upper = mid;
            }
        samples.Sort((a, b) => a.population.CompareTo(b.population));
        return new LandEquilibriumEstimate
        {
            status = status, lower = lower, upper = upper,
            crossings = crossings, samples = samples.ToArray()
        };
    }

    private static LandNetGrowthSample Forecast(BlockInfo source,
        PopulationData target, int proposedCount, LandRuleTuning rules)
    {
        var temporary = new GameObject("EcologyCounterfactual");
        temporary.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            BlockInfo copy = temporary.AddComponent<BlockInfo>();
            copy.temperature = source.temperature;
            copy.humidity = source.humidity;
            copy.maxPlantBiomass = source.maxPlantBiomass;
            copy.habitatRecovery = Mathf.FloorToInt(
                LandLifeRules.SustainablePlantSupply(source, rules));
            // At this stock, growth R and the real reserve floor leave exactly
            // R drawable every day. High transient S supplies no subsidy.
            copy.plantBiomass = Mathf.Min(copy.habitatRecovery,
                copy.maxPlantBiomass * rules.minimumPlantReserveFraction);
            copy.community = new List<PopulationData>();
            PopulationData candidate = null;
            foreach (PopulationData original in source.community)
            {
                if (original == null) continue;
                PopulationData cloned = JsonUtility.FromJson<PopulationData>(
                    JsonUtility.ToJson(original));
                cloned.species = original.species;
                if (cloned.foodSatisfactionHistory == original.foodSatisfactionHistory ||
                    cloned.preyNetRenewalHistory == original.preyNetRenewalHistory)
                    throw new InvalidOperationException("Counterfactual copy shares history.");
                if (original == target)
                {
                    candidate = cloned;
                    float ratio = original.speciesAmount > 0
                        ? (float)proposedCount / original.speciesAmount : 0f;
                    cloned.speciesAmount = proposedCount;
                    cloned.bodyBiomass = original.bodyBiomassInitialized
                        ? original.bodyBiomass * ratio
                        : proposedCount * LandLifeRules.BodyMass(original.size, rules);
                    cloned.bodyBiomassInitialized = true;
                    cloned.energyReserve = original.energyReserve * ratio;
                }
                copy.community.Add(cloned);
            }
            if (candidate == null)
                throw new InvalidOperationException("Counterfactual target missing.");
            LandLifeRules.Simulate(copy, rules);
            return new LandNetGrowthSample
            {
                population = proposedCount, births = candidate.birthsToday,
                naturalDeaths = candidate.deathsToday - candidate.predationDeathsToday,
                predationDeaths = candidate.predationDeathsToday,
                net = candidate.birthsToday - candidate.deathsToday
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(temporary); }
    }
}
