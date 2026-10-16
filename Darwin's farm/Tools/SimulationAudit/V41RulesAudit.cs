using System;
using System.Collections.Generic;
using UnityEngine;

static partial class Program
{
    static void V41RulesAudit()
    {
        // V4.1 grows at the full potential rate until the stock reaches capacity.
        if (SimulationController.CalculatePlantGrowth(600000f, 1000000f, 100000) != 100000f ||
            SimulationController.CalculatePlantGrowth(975000f, 1000000f, 100000) != 25000f ||
            SimulationController.CalculatePlantGrowth(1000000f, 1000000f, 100000) != 0f)
            throw new Exception("V4.1 plant growth still uses density slowdown");

        if (SimulationController.StandardMigrationAmount(9) != 0 ||
            SimulationController.StandardMigrationAmount(10) != 10 ||
            SimulationController.StandardMigrationAmount(50) != 10 ||
            SimulationController.StandardMigrationAmount(1000) != 100)
            throw new Exception("V4.1 natural migration amount is not 10%, clamped to 10-100");

        var population = Pop(0, 50);
        population.ecologicalNiche = EcologicalNiche.Land;
        var source = Block(0, 0f, population);
        var target = Block(10000, 10000f);
        source.SetNeighbors(new List<BlockInfo> { target });
        target.SetNeighbors(new List<BlockInfo> { source });
        var controller = Controller();
        controller.SetParameters(0f, 0f, 0f);
        controller.SetMigrationParameters(true, 7, 1f, 0.8f, 1f, 1f, 0);
        controller.SetBlocks(new List<BlockInfo> { source, target });
        UnityEngine.Random.InitState(1);
        for (int day = 1; day <= 6; day++) controller.SimulateDay(day);
        if (source.community[0].speciesAmount != 50 || target.community.Count != 0)
            throw new Exception("natural migration occurred before day seven");
        controller.SimulateDay(7);
        if (source.community[0].speciesAmount != 40 || target.community.Count != 1 ||
            target.community[0].speciesAmount != 10)
            throw new Exception("natural migration did not conserve the fixed founder count");
        Console.WriteLine("V41_RULES_AUDIT growth=ok migration_checkpoint=7 migration_count=ok");
    }
}
