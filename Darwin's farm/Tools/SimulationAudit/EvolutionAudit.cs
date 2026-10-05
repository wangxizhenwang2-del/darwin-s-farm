using System;
using System.Collections.Generic;
using UnityEngine;

static partial class Program
{
    static void PresetEvolutionTests()
    {
        var a = new SpeciesData
        {
            baseMovementAbility = 10, baseFitTemperature = 50,
            baseFitHumidity = 50, baseSize = 10, baseFertility = 50,
            baseHabitatNiche = 50, baseEcologicalNiche = EcologicalNiche.Land,
            trophicLevel = 0
        };
        var b = new SpeciesData
        {
            baseMovementAbility = 20, baseFitTemperature = 70,
            baseFitHumidity = 70, baseSize = 20, baseFertility = 70,
            baseHabitatNiche = 100, baseEcologicalNiche = EcologicalNiche.Air,
            trophicLevel = 0
        };
        a.evolutionTargets.Add(b);
        var source = Pop(1, 100, 20, 20, 70);
        source.species = a;
        source.fitTemperature = 70;
        source.fitHumidity = 70;
        source.ecologicalNiche = EcologicalNiche.Water;
        source.habitatNiche = 0;
        var block = Block(10000, 10000f, source);
        var controller = Controller();
        controller.SetParameters(0f, 0f, 0f);
        controller.SetMutationParameters(1, 0f, 1f);
        controller.SetBlocks(new List<BlockInfo> { block });
        if (!a.HasEverUnlocked || a.IsExtinct || b.HasEverUnlocked || b.IsExtinct ||
            !controller.IsSpeciesLiving(a) || controller.IsSpeciesLiving(b))
            throw new Exception("initial species status is incorrect");

        int evolved = 0, extinct = 0, revived = 0;
        controller.OnSpeciesEvolved += (site, ancestor, descendant, target, amount) =>
        {
            if (site != block || ancestor != source || target != b) throw new Exception("wrong evolution event");
            evolved++;
        };
        controller.OnSpeciesExtinct += species => { if (species == b) extinct++; };
        controller.OnSpeciesRevived += species => { if (species == b) revived++; };
        controller.SimulateDay(1);
        PopulationData child = block.community.Find(p => p.species == b);
        if (child == null || child.speciesAmount != 10 || source.speciesAmount != 90 ||
            evolved != 1 || !b.HasEverUnlocked || b.IsExtinct ||
            source.fitTemperature != 60 || source.fitHumidity != 60 ||
            source.movementAbility != 15 || source.size != 15 || source.fertility != 60 ||
            source.trophicLevel != 0 || source.ecologicalNiche != EcologicalNiche.Land ||
            source.habitatNiche != 50 || child.trophicLevel != 0 ||
            child.ecologicalNiche != EcologicalNiche.Air || child.habitatNiche != 100)
            throw new Exception("conversion, identity, status or ancestor reset is incorrect");

        // 即使 A 再次接近 B，只要当地 B 存活，就不应连续转化。
        MatchTarget(source, b);
        controller.SimulateDay(2);
        if (evolved != 1 || source.speciesAmount != 90 || child.speciesAmount != 10)
            throw new Exception("living local B did not suppress repeated conversion");

        // 当地 B 灭绝，另一地块的 B 仍存活，物种状态保持存活。
        var distantB = Pop(0, 5, 20, 20, 70);
        distantB.species = b;
        var remote = Block(10000, 10000f, distantB);
        controller.SetBlocks(new List<BlockInfo> { block, remote });
        child.speciesAmount = 0;
        source.fitTemperature = 50; // 此日不触发恢复，先完成灭绝检查。
        controller.SimulateDay(3);
        if (b.IsExtinct || extinct != 0)
            throw new Exception("local loss was mistaken for global extinction");
        MatchTarget(source, b);
        controller.SimulateDay(4);
        if (evolved != 2 || child.speciesAmount != 9 || source.speciesAmount != 81)
            throw new Exception("local B did not re-evolve while remote B survived");

        child.speciesAmount = 0;
        distantB.speciesAmount = 0;
        source.fitTemperature = 50;
        controller.SimulateDay(5);
        if (!b.HasEverUnlocked || !b.IsExtinct || extinct != 1 ||
            controller.IsSpeciesLiving(b))
            throw new Exception("global extinction did not preserve unlock history");
        MatchTarget(source, b);
        controller.SimulateDay(6);
        if (b.IsExtinct || revived != 1 || evolved != 3 || child.speciesAmount != 8 ||
            !controller.IsSpeciesLiving(b))
            throw new Exception("extinct B was not revived with the same species asset");

        PopulationData migrant = PopulationTransfer.CopyForMove(source, 10);
        if (migrant.evolutionConversions.Count != source.evolutionConversions.Count ||
            migrant.evolutionConversions[0] == source.evolutionConversions[0])
            throw new Exception("migration failed to copy conversion history independently");
        Console.WriteLine("EVOLUTION_AUDIT conversion=ok local_recovery=ok global_extinction=ok revival=ok migration_history=ok");
    }

    static void MatchTarget(PopulationData population, SpeciesData target)
    {
        population.movementAbility = target.baseMovementAbility;
        population.fitTemperature = target.baseFitTemperature;
        population.fitHumidity = target.baseFitHumidity;
        population.size = target.baseSize;
        population.fertility = target.baseFertility;
    }
}
