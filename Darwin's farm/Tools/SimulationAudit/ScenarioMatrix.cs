using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

static partial class Program
{
    private static BlockInfo MatrixBlock(string name)
    {
        var a = Pop(0, name == "A" || name == "B-switch" ? 10 : 100, 5, 10, 80);
        var members = new List<PopulationData> { a };
        if (name == "B-two" || name == "B-switch" || name == "B-exclude" ||
            name == "D" || name == "E" || name == "F")
        {
            var b = name == "B-exclude" ? Pop(0, 5, 6, 10, 10)
                : Pop(0, name == "B-switch" || name == "B-two" ? 10 :
                    name == "F" ? 50 : 5,
                    name == "B-switch" ? 6 : 10, 20, 50);
            if (name == "D" || name == "E")
                b.fitTemperature = b.fitHumidity = 65;
            if (name == "B-exclude")
            {
                a.speciesAmount = 20;
                a.movementAbility = 30;
                b.fitTemperature = b.fitHumidity = 95;
            }
            members.Add(b);
        }
        if (name == "C" || name == "D" || name == "E" || name == "F")
            members.Add(Pop(1, name == "C" ? 5 : 8, 20, 30, 50));
        if (name == "E" || name == "F")
            members.Add(Pop(1, 8, 15, 35, 60));
        if (name == "F") members.Add(Pop(2, 10, 5, 40, 80));
        var block = Block(1000, 10000f, members.ToArray());
        block.maxPlantBiomass = 100000f;
        return block;
    }

    private static SimulationController MatrixController()
    {
        var controller = Controller(true);
        controller.SetParameters(0.1f, 0.1f, 0.6f);
        return controller;
    }

    private static void ScenarioMatrix()
    {
        string[] scenarios = { "A", "B-two", "B-switch", "B-exclude", "C", "D", "E", "F" };
        foreach (string scenario in scenarios)
        {
            int extinctionRuns = 0, stableRuns = 0, switchRuns = 0;
            for (int seed = 1; seed <= 12; seed++)
            {
                UnityEngine.Random.InitState(seed);
                BlockInfo block = MatrixBlock(scenario);
                SimulationController controller = MatrixController();
                float minimumStock = float.MaxValue, maximumStock = 0f;
                int[] minimumCounts = Enumerable.Repeat(int.MaxValue,
                    block.community.Count).ToArray();
                int[] maximumCounts = new int[block.community.Count];
                int[] initialMovement = block.community.Select(p => p.movementAbility).ToArray();
                int[] initialFertility = block.community.Select(p => p.fertility).ToArray();
                int switchDay = 0;
                int finalDay = scenario == "B-switch" ? 600 : 500;
                for (int day = 1; day <= finalDay; day++)
                {
                    controller.SimulateBlock(block);
                    if (scenario == "B-switch" && switchDay == 0 &&
                        block.community[0].size >= 8) switchDay = day;
                    if (day <= finalDay - 100) continue;
                    minimumStock = Math.Min(minimumStock, block.plantBiomass);
                    maximumStock = Math.Max(maximumStock, block.plantBiomass);
                    for (int i = 0; i < block.community.Count; i++)
                    {
                        minimumCounts[i] = Math.Min(minimumCounts[i],
                            block.community[i].speciesAmount);
                        maximumCounts[i] = Math.Max(maximumCounts[i],
                            block.community[i].speciesAmount);
                    }
                }
                int extinct = block.community.Count(p => p.speciesAmount == 0);
                if (scenario == "A" &&
                    (Math.Abs(Traits(block.community[0])[3] - 5f) > 0.001f ||
                     Math.Abs(Traits(block.community[0])[2] - 10f) > 0.001f ||
                     Traits(block.community[0])[4] > 83f))
                    throw new Exception($"solo herbivore traits drifted in seed {seed}");
                PopulationData extreme = block.community.FirstOrDefault(p => p.speciesAmount > 0 &&
                    (p.movementAbility + p.movementMutationRemainder > 95f ||
                     p.fertility + p.fertilityMutationRemainder > 95f ||
                     p.size + p.sizeMutationRemainder <= 1f));
                if (extreme != null)
                    throw new Exception($"trait ran to an extreme in {scenario}, seed {seed}: L{extreme.trophicLevel}, N{extreme.speciesAmount}, size={F(Traits(extreme)[3])}, move={F(Traits(extreme)[2])}, fert={F(Traits(extreme)[4])}");
                if (extinct > 0) extinctionRuns++;
                if (scenario == "B-switch" && switchDay > 0 && switchDay <= 500)
                    switchRuns++;
                bool stable = block.plantBiomass >= 10000f && block.plantBiomass <= 85000f &&
                    maximumStock - minimumStock < 10000f &&
                    Math.Abs(block.plantGrowthToday - block.consumedBiomassToday) < 100f &&
                    block.community.Select((p, i) =>
                        maximumCounts[i] - minimumCounts[i] <=
                            Math.Max(10f, p.speciesAmount * 0.35f)).All(value => value);
                if (stable) stableRuns++;
                if (seed == 1 || seed == 12)
                    Console.WriteLine($"MATRIX {scenario} seed={seed} stable={stable} stock={F(block.plantBiomass)} range={F(maximumStock - minimumStock)} " +
                        string.Join(" | ", block.community.Select((p, i) =>
                            $"L{p.trophicLevel}:N{p.speciesAmount}/K{F(p.carryingCapacity)} size{F(Traits(p)[3])} move{F(Traits(p)[2] - initialMovement[i]):+0.##;-0.##;0} fert{F(Traits(p)[4] - initialFertility[i]):+0.##;-0.##;0}")));
            }
            Console.WriteLine($"MATRIX_SUMMARY {scenario} stable={stableRuns}/12 extinction={extinctionRuns}/12 switch={switchRuns}/12");
            if ((scenario == "A" || scenario == "B-two" ||
                    scenario == "B-switch" || scenario == "B-exclude" ||
                    scenario == "C") && stableRuns != 12 ||
                (scenario == "D" || scenario == "E" || scenario == "F") &&
                    stableRuns < 8 ||
                scenario == "B-switch" && switchRuns != 12 ||
                scenario == "B-exclude" && extinctionRuns != 12 ||
                (scenario == "D" || scenario == "E" || scenario == "F") &&
                    (extinctionRuns == 0 || extinctionRuns == 12))
                throw new Exception($"A–F regression in {scenario}");
        }

        // G：分别修改温度、湿度、日恢复、库存，并通过迁入接口加入新物种。
        string[] interventions =
            { "temperature", "humidity", "recovery", "stock", "combined", "immigration", "no_plants" };
        foreach (string scenario in scenarios)
        foreach (string intervention in interventions)
        {
            int speciesLoss = 0;
            float meanStockChange = 0f;
            for (int seed = 1; seed <= 12; seed++)
            {
                UnityEngine.Random.InitState(seed);
                BlockInfo block = MatrixBlock(scenario);
                SimulationController controller = MatrixController();
                for (int day = 1; day <= 500; day++) controller.SimulateBlock(block);
                PopulationData[] originals = block.community.ToArray();
                int before = originals.Count(p => p.speciesAmount > 0);
                float oldStock = block.plantBiomass;
                switch (intervention)
                {
                    case "temperature": controller.ApplyEnvironment(block, 80, 50, 1000); break;
                    case "humidity": controller.ApplyEnvironment(block, 50, 80, 1000); break;
                    case "recovery": controller.ApplyEnvironment(block, 50, 50, 120); break;
                    case "stock": block.plantBiomass = 1000f; break;
                    case "combined":
                        controller.ApplyEnvironment(block, 80, 80, 120);
                        block.plantBiomass = 1000f;
                        break;
                    case "immigration":
                        PopulationData migrant = Pop(0, 80, 6, 30, 80);
                        BlockInfo source = Block(1000, 10000f, migrant);
                        controller.MovePopulation(source, block, migrant, 500);
                        break;
                    case "no_plants":
                        controller.ApplyEnvironment(block, 80, 80, 0);
                        block.plantBiomass = 0f;
                        break;
                }
                for (int day = 1; day <= 150; day++) controller.SimulateBlock(block);
                if (originals.Count(p => p.speciesAmount > 0) < before)
                    speciesLoss++;
                meanStockChange += block.plantBiomass - oldStock;
            }
            Console.WriteLine($"MATRIX_SHOCK {scenario} {intervention} loss={speciesLoss}/12 meanStockChange={F(meanStockChange / 12f)}");
            if (intervention == "no_plants" && speciesLoss != 12)
                throw new Exception($"complete resource loss did not collapse {scenario}");
        }
    }

    private static void ExtremeProbe()
    {
        foreach (string scenario in new[] { "C", "D", "E", "F" })
        {
            float maximumMovement = 0f, maximumFertility = 0f, minimumSize = 100f;
            int living = 0;
            for (int seed = 1; seed <= 12; seed++)
            {
                UnityEngine.Random.InitState(seed);
                BlockInfo block = MatrixBlock(scenario);
                SimulationController controller = MatrixController();
                for (int day = 1; day <= 5000; day++)
                {
                    controller.SimulateBlock(block);
                    foreach (PopulationData population in block.community)
                    {
                        if (population.speciesAmount <= 0) continue;
                        maximumMovement = Math.Max(maximumMovement, Traits(population)[2]);
                        maximumFertility = Math.Max(maximumFertility, Traits(population)[4]);
                        minimumSize = Math.Min(minimumSize, Traits(population)[3]);
                    }
                }
                living += block.community.Count(p => p.speciesAmount > 0);
            }
            Console.WriteLine($"EXTREME {scenario} living={living} maxMovement={F(maximumMovement)} maxFertility={F(maximumFertility)} minSize={F(minimumSize)}");
            if (maximumMovement > 95f || maximumFertility > 95f || minimumSize <= 1f)
                throw new Exception($"long-run trait extreme in {scenario}");
        }
    }

}
