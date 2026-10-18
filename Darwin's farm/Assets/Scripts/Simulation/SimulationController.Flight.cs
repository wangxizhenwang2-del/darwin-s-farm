using System;
using System.Collections.Generic;
using UnityEngine;

public partial class SimulationController
{
    [Header("Flight rules")]
    [SerializeField, Range(0f, 1f)] private float landToFlightChance = .05f;
    [SerializeField, Range(0f, 1f)] private float flightToLandChance = .005f;
    [SerializeField, Range(0f, 1f)] private float flightReverseFounderFraction = .1f;
    [SerializeField, Range(0f, 1f)] private float flightMigrationChanceMaximum = .05f;
    [SerializeField, Range(0f, 1f)] private float waterMigrationDebugChance;

    public void SetFlightCandidateParameters(float reverseFraction,
        int branchMaximum, float migrationChanceMaximum)
    {
        flightReverseFounderFraction = Mathf.Clamp(reverseFraction, .01f, .5f);
        flightMigrationChanceMaximum = Mathf.Clamp01(migrationChanceMaximum);
    }

    public void SetCrossHabitatDebugProbabilities(float landAir,
        float airLand, float withinWater)
    {
        landToFlightChance = Mathf.Clamp01(landAir);
        flightToLandChance = Mathf.Clamp01(airLand);
        waterMigrationDebugChance = Mathf.Clamp01(withinWater);
    }

    private static int GroundPopulationCount(BlockInfo block)
    {
        int result = 0;
        if (block == null || block.community == null) return 0;
        foreach (PopulationData population in block.community)
            if (population != null && population.speciesAmount > 0 &&
                population.ecologicalNiche == EcologicalNiche.Land) result++;
        return result;
    }

    private void SimulateAirHabitats()
    {
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.community == null ||
                HabitatTopology.IsWater(block) || !HasAirPopulation(block))
                continue;
            float landConsumed = block.consumedBiomassToday;
            float landSink = block.accountedBiomassSinkToday;
            var preyDeathsBefore = new Dictionary<PopulationData, int>();
            foreach (PopulationData population in block.community)
                if (population != null && population.speciesAmount > 0 &&
                    population.ecologicalNiche == EcologicalNiche.Land)
                    preyDeathsBefore[population] =
                        population.predationDeathsToday;
            LandLifeRules.Simulate(block, LandRules, EcologicalNiche.Air,
                growPlants: false);
            foreach (var entry in preyDeathsBefore)
            {
                PopulationData prey = entry.Key;
                int extraDeaths = prey.predationDeathsToday - entry.Value;
                if (extraDeaths <= 0 || prey.preyNetRenewalHistory == null ||
                    prey.preyNetRenewalHistory.Count == 0) continue;
                int last = prey.preyNetRenewalHistory.Count - 1;
                prey.preyNetRenewalHistory[last] -= extraDeaths;
            }
            block.consumedBiomassToday = landConsumed;
            block.accountedBiomassSinkToday += landSink;
        }
    }

    private void RunFlightEvolution(int day)
    {
        if (day <= 0) return;
        var sources = new List<(BlockInfo block, PopulationData population)>();
        foreach (BlockInfo block in blockInfos)
            if (block != null && block.community != null &&
                !HabitatTopology.IsWater(block))
                foreach (PopulationData population in block.community)
                    if (population != null && population.species != null &&
                        population.speciesAmount > 0 &&
                        (population.ecologicalNiche == EcologicalNiche.Land ||
                         population.ecologicalNiche == EcologicalNiche.Air))
                        sources.Add((block, population));
        foreach (var entry in sources)
        {
            BlockInfo block = entry.block;
            PopulationData origin = entry.population;
            if (!block.community.Contains(origin) ||
                day < origin.nextEvolutionDay) continue;
            List<SpeciesData> targets = ConnectedTargets(origin.species);
            targets.Sort((left, right) =>
            {
                if (left == right) return 0;
                if (left == origin.manualEvolutionTarget) return -1;
                if (right == origin.manualEvolutionTarget) return 1;
                return CompareSpeciesOrder(left, right);
            });
            foreach (SpeciesData target in targets)
            {
                if (target == null) continue;
                if (origin.ecologicalNiche == EcologicalNiche.Land &&
                    target.baseEcologicalNiche == EcologicalNiche.Air)
                {
                    if (!CanLandEvolveToFlight(block, origin, target,
                        out float payment, out float excess) ||
                        ModelRandom01(day, "formal.land-air", block, origin) >=
                        landToFlightChance) continue;
                    CommitPresetSpecies(block, block, origin, target, day, 50,
                        payment, excess);
                    break;
                }
                if (origin.ecologicalNiche == EcologicalNiche.Air &&
                    target.baseEcologicalNiche == EcologicalNiche.Land)
                {
                    if (!CanFlightEvolveToLand(block, origin, target,
                        out int amount, out float payment, out float excess) ||
                        ModelRandom01(day, "formal.air-land", block, origin) >=
                        flightToLandChance) continue;
                    CommitPresetSpecies(block, block, origin, target, day,
                        amount, payment, excess);
                    break;
                }
            }
        }
    }

    private bool CanLandEvolveToFlight(BlockInfo block,
        PopulationData source, SpeciesData target, out float payment,
        out float excess)
    {
        payment = excess = 0f;
        if (source.speciesAmount <= 50 || HasAirPopulation(block) ||
            target.trophicLevel != 2 || source.movementAbility <= 50 ||
            Mathf.Abs(source.fitTemperature - target.baseFitTemperature) > 5 ||
            Mathf.Abs(source.fitHumidity - target.baseFitHumidity) > 5 ||
            Mathf.Abs(source.size - target.baseSize) > 5 ||
            Mathf.Abs(source.fertility - target.baseFertility) > 5 ||
            !ConnectedTargets(source.species).Contains(target) ||
            !CrossTargetFoodSupportedHere(block, target, 50)) return false;
        return TryPlanFlightPayment(block, source, target, 50,
            out payment, out excess);
    }

    private bool CanFlightEvolveToLand(BlockInfo block,
        PopulationData source, SpeciesData target, out int amount,
        out float payment, out float excess)
    {
        amount = 0; payment = excess = 0f;
        if (source.trophicLevel != 2 || GroundPopulationCount(block) >= 5 ||
            HasLivingSpecies(block, target) ||
            !ConnectedTargets(source.species).Contains(target)) return false;
        amount = Mathf.Min(source.speciesAmount - 1, Mathf.FloorToInt(
            source.speciesAmount * flightReverseFounderFraction + .5f));
        if (amount < 1 || !CrossTargetFoodSupportedHere(block, target,
            amount)) return false;
        return TryPlanFlightPayment(block, source, target, amount,
            out payment, out excess);
    }

    private bool TryPlanFlightPayment(BlockInfo block, PopulationData source,
        SpeciesData target, int amount, out float payment, out float excess)
    {
        payment = excess = 0f;
        if (amount <= 0 || source.speciesAmount <= amount) return false;
        float body = source.bodyBiomassInitialized ? source.bodyBiomass :
            source.speciesAmount * LandLifeRules.BodyMass(source.size, LandRules);
        float perHead = body / source.speciesAmount;
        float targetBody = LandLifeRules.BodyMass(target.baseSize, LandRules);
        payment = amount * Mathf.Max(0f, targetBody - perHead);
        excess = amount * Mathf.Max(0f, perHead - targetBody);
        return block.plantBiomass + .001f >= payment;
    }

    private bool FlightTargetCanFeed(BlockInfo target,
        PopulationData flyer, out float freeFood, out float need)
    {
        freeFood = need = 0f;
        if (flyer == null || flyer.trophicLevel != 2 || target == null ||
            HabitatTopology.IsWater(target) || HasAirPopulation(target)) return false;
        var candidate = new PopulationData(false)
        {
            species = flyer.species,
            ecologicalNiche = EcologicalNiche.Air,
            trophicLevel = 2,
            speciesAmount = flyer.speciesAmount,
            fitTemperature = flyer.fitTemperature,
            fitHumidity = flyer.fitHumidity,
            movementAbility = flyer.movementAbility,
            size = flyer.size,
            fertility = flyer.fertility
        };
        // Evaluate available prey at the destination without crediting the
        // flyer's stored reserve as if it were local food there.
        StockFoodForecast forecast = ForecastStockPredatorFood(target, null,
            candidate, candidate.speciesAmount);
        need = flyer.speciesAmount * LandLifeRules.AdultNeed(
            flyer.size, flyer.movementAbility, LandRules);
        freeFood = LandLifeRules.AvailableSustainablePreyFood(target,
            candidate, LandRules);
        return need > 0f && forecast.legalPreyStock > 0 &&
            forecast.coverage >= LandRules.evolutionTodaySatisfaction &&
            freeFood >= need * 1.08f;
    }

    private void RunFlightHabitatMigration(int day)
    {
        var sources = new List<(BlockInfo block, PopulationData population)>();
        foreach (BlockInfo block in blockInfos)
            if (block != null && block.community != null)
                foreach (PopulationData population in block.community)
                    if (population != null && population.speciesAmount > 0 &&
                        population.ecologicalNiche == EcologicalNiche.Air &&
                        population.trophicLevel == 2 &&
                        day >= population.nextMigrationDay)
                        sources.Add((block, population));
        foreach (var entry in sources)
        {
            BlockInfo source = entry.block;
            PopulationData population = entry.population;
            if (!source.community.Contains(population) ||
                source.Neighbors == null) continue;
            var neighbors = new List<BlockInfo>(source.Neighbors);
            neighbors.Sort((a, b) => string.CompareOrdinal(StableTileKey(a),
                StableTileKey(b)));
            BlockInfo best = null;
            float bestAdvantage = 0f;
            float sourceFood = LandLifeRules.AvailableSustainablePreyFood(
                source, population, LandRules);
            foreach (BlockInfo target in neighbors)
            {
                MapTileInstance fromTile = source.GetComponent<MapTileInstance>();
                MapTileInstance toTile = target == null ? null :
                    target.GetComponent<MapTileInstance>();
                if (fromTile == null || toTile == null ||
                    Mathf.Abs(fromTile.Coordinate.x - toTile.Coordinate.x) +
                    Mathf.Abs(fromTile.Coordinate.y - toTile.Coordinate.y) != 1)
                    continue;
                if (target == population.lastMigrationSource ||
                    !AreNeighbors(source, target) ||
                    !FlightTargetCanFeed(target, population,
                        out float food, out float need)) continue;
                float advantage = Mathf.Clamp01((food - sourceFood) /
                    Mathf.Max(1f, food, need));
                if (advantage > bestAdvantage)
                { best = target; bestAdvantage = advantage; }
            }
            if (best != null)
                TryFlightMigrationOnce(source, best, population, day, out _);
        }
    }

    private bool TryFlightMigrationOnce(BlockInfo source, BlockInfo target,
        PopulationData population, int day, out bool moved)
    {
        moved = false;
        if (!CanDepart(source, population, EcologicalNiche.Air, day) ||
            target == population.lastMigrationSource ||
            !AreNeighbors(source, target)) return false;
        MapTileInstance a = source.GetComponent<MapTileInstance>();
        MapTileInstance b = target.GetComponent<MapTileInstance>();
        if (a == null || b == null ||
            Mathf.Abs(a.Coordinate.x - b.Coordinate.x) +
            Mathf.Abs(a.Coordinate.y - b.Coordinate.y) != 1 ||
            !FlightTargetCanFeed(target, population,
                out float freeFood, out float need)) return false;
        // Source and target use the same real legal-prey renewal forecast.
        float sourceFood = LandLifeRules.AvailableSustainablePreyFood(
            source, population, LandRules);
        float attraction = Mathf.Clamp01((freeFood - sourceFood) /
            Mathf.Max(1f, freeFood, need));
        if (attraction <= 0f) return false;
        float opportunity = flightMigrationChanceMaximum * attraction;
        if (ModelRandom01(day, "flight.move.chance", source,
            population, target) >= opportunity) return true;
        ApplyMovePlans(new List<MovePlan> { new MovePlan {
            source = source, target = target,
            population = population, amount = population.speciesAmount } }, day);
        moved = true;
        return true;
    }

    private void RunWaterHabitatMigration(int day)
    {
        if (day <= 0 || day % Mathf.Max(1, migrationInterval) != 0) return;
        var sources = new List<(BlockInfo block, PopulationData population)>();
        foreach (BlockInfo block in blockInfos)
            if (block != null && block.community != null &&
                HabitatTopology.IsWater(block))
                foreach (PopulationData population in block.community)
                    if (population != null && population.speciesAmount > 0 &&
                        population.ecologicalNiche == EcologicalNiche.Water &&
                        day >= population.nextMigrationDay)
                        sources.Add((block, population));
        foreach (var entry in sources)
        {
            BlockInfo source = entry.block;
            PopulationData population = entry.population;
            if (!source.community.Contains(population) ||
                source.Neighbors == null) continue;
            var neighbors = new List<BlockInfo>(source.Neighbors);
            neighbors.Sort((a, b) => string.CompareOrdinal(StableTileKey(a),
                StableTileKey(b)));
            foreach (BlockInfo target in neighbors)
            {
                if (TryWaterMigration(source, target, population,
                    null, day)) break;
            }
        }
    }
}
