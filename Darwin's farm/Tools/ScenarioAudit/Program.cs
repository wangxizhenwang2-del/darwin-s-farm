using System;
using System.Collections.Generic;
using System.Diagnostics;
using DarwinFarm.Environment;
using UnityEngine;

internal static class Program
{
    private static void Main()
    {
        for (int seed = 1; seed <= 5; seed++) Run(seed);
    }

    private static void Run(int seed)
    {
        UnityEngine.Random.InitState(seed);
        var chicken = new SpeciesData
        {
            name = "鸡", trophicLevel = 0, baseFitTemperature = 61,
            baseFitHumidity = 81, baseMovementAbility = 35, baseSize = 6,
            baseFertility = 75
        };
        var deer = new SpeciesData
        {
            name = "鹿", trophicLevel = 0, baseFitTemperature = 54,
            baseFitHumidity = 34, baseMovementAbility = 55, baseSize = 35,
            baseFertility = 45
        };
        chicken.evolutionTargets.Add(deer);
        var population = new PopulationData
        {
            species = chicken, speciesAmount = 160, lineageName = "鸡",
            ecologicalNiche = EcologicalNiche.Land,
            fitTemperature = 61, fitHumidity = 81,
            movementAbility = 35, size = 6, fertility = 75,
            habitatNiche = 50, trophicLevel = 0, trophicLevelInitialized = true
        };
        var block = new BlockInfo
        {
            temperature = 68, humidity = 68, elevation = 0,
            habitatRecovery = 100000, plantBiomass = 100000,
            maxPlantBiomass = 1000000, waterCoverage = WaterCoverage.Land,
            community = new List<PopulationData> { population }
        };
        var simulation = new SimulationController();
        simulation.SetBlocks(new List<BlockInfo> { block });
        simulation.SetEvolutionNetworkSpecies(new[] { deer, chicken });
        var world = new EnvironmentWorld();
        var position = new GridPosition(0, 0);
        world.SynchronizeTopology(new[] { new TopologyNode(position, 0,
            TerrainKind.Grassland, Array.Empty<GridPosition>()) });
        int firstDesert = 0, firstDeer = 0;
        var timer = Stopwatch.StartNew();
        for (int day = 0; day <= SimulationLoopDays; day++)
        {
            if (day == 10 && !world.TrySetClimate(position, EnvironmentAttribute.Humidity,
                -50, out string dryError)) throw new Exception(dryError);
            if (day == 10)
            {
                world.TrySetRecovery(position, -50000, out _);
                block.plantBiomass = (float)EnvironmentRules.MultiplyStock(block.plantBiomass,
                    block.maxPlantBiomass, -.5);
            }
            if (day == 240)
            {
                world.TrySetClimate(position, EnvironmentAttribute.Temperature, -25, out _);
                world.TrySetClimate(position, EnvironmentAttribute.Humidity, -25, out _);
            }
            if (day > 0)
            {
                world.AdvanceToDay(day);
                world.TryRead(position, out EnvironmentSnapshot environment);
                block.temperature = EnvironmentRules.Round(environment.Temperature.Value);
                block.humidity = EnvironmentRules.Round(environment.Humidity.Value);
                block.habitatRecovery = EnvironmentRules.Round(environment.Recovery.Value);
                simulation.SimulateDay(day);
            }
            world.TryRead(position, out EnvironmentSnapshot state);
            if (firstDesert == 0 && state.Terrain == TerrainKind.Desert) firstDesert = day;
            if (firstDeer == 0 && simulation.IsSpeciesLiving(deer)) firstDeer = day;
            if (day == 0 || day == 25 || day == 100 || day == 140 || day == 240 ||
                day == 300 || day == 400 || day == 600 || day == SimulationLoopDays)
                Console.WriteLine($"seed={seed} day={day} biome={state.Terrain} T={state.Temperature.Value:F0} H={state.Humidity.Value:F0} R={state.Recovery.Value:F0} stock={block.plantBiomass:F0} chicken={population.speciesAmount} deer={DeerCount(block, deer)}");
        }
        timer.Stop();
        Console.WriteLine($"seed={seed} firstDesert={firstDesert} firstDeer={firstDeer} traits={population.fitTemperature}/{population.fitHumidity}/{population.movementAbility}/{population.size}/{population.fertility} elapsedMs={timer.ElapsedMilliseconds}");
    }

    private const int SimulationLoopDays = 1800;

    private static int DeerCount(BlockInfo block, SpeciesData deer)
    {
        int count = 0;
        foreach (PopulationData member in block.community)
            if (member != null && member.species == deer) count += member.speciesAmount;
        return count;
    }
}
