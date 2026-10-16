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
            baseEcologicalNiche = EcologicalNiche.Land, trophicLevel = 0
        };
        var b = new SpeciesData
        {
            baseMovementAbility = 20, baseFitTemperature = 70,
            baseFitHumidity = 70, baseSize = 20, baseFertility = 70,
            baseEcologicalNiche = EcologicalNiche.Land, trophicLevel = 0
        };
        a.evolutionTargets.Add(b);
        var source = Pop(0, 100, 20, 20, 70);
        source.species = a;
        source.fitTemperature = source.fitHumidity = 70;
        source.ecologicalNiche = EcologicalNiche.Land;
        var block = Block(10000, 10000f, source);
        var controller = Controller();
        controller.SetParameters(0f, 0f, 0f);
        controller.SetMutationParameters(1, 0f, 1f);
        controller.SetBlocks(new List<BlockInfo> { block });

        UnityEngine.Random.InitState(7);
        int evolved = 0;
        controller.OnSpeciesEvolved += (_, ancestor, _, target, amount) =>
        {
            if (ancestor != source || target != b || amount != 10)
                throw new Exception("wrong evolution target or founder count");
            evolved++;
        };
        controller.SimulateDay(7);
        if (evolved != 0) throw new Exception("one-way evolution edge was accepted");

        b.evolutionTargets.Add(a);
        source.ecologicalNiche = EcologicalNiche.Water;
        controller.SimulateDay(14);
        if (evolved != 0) throw new Exception("cross-niche edge was used as land speciation");
        source.ecologicalNiche = EcologicalNiche.Land;
        controller.SimulateDay(15);
        if (evolved != 0) throw new Exception("speciation rolled outside the seven-day checkpoint");

        int firstDay = 0;
        for (int day = 21; day <= 7021; day += 7)
        {
            controller.SimulateDay(day);
            if (evolved == 0) continue;
            firstDay = day;
            break;
        }
        PopulationData child = block.community.Find(p => p.species == b);
        if (firstDay == 0 || firstDay % 7 != 0 || evolved != 1 ||
            child == null || child.speciesAmount != 10 || source.speciesAmount != 90 ||
            !b.HasEverUnlocked || b.IsExtinct)
            throw new Exception("fixed-checkpoint 1% speciation or count conservation failed");

        MatchTarget(source, b);
        controller.SimulateDay(firstDay + 7);
        if (evolved != 1) throw new Exception("occupied target species evolved again");

        child.speciesAmount = 0;
        MatchTarget(source, b);
        for (int day = firstDay + 14; day <= firstDay + 7014; day += 7)
        {
            controller.SimulateDay(day);
            if (evolved == 2) break;
        }
        if (evolved != 2 || child.speciesAmount != 10 || source.speciesAmount != 80 ||
            b.IsExtinct)
            throw new Exception("extinct local target did not become eligible again");

        PopulationData migrant = PopulationTransfer.CopyForMove(source, 10);
        if (migrant.evolutionConversions.Count != source.evolutionConversions.Count ||
            migrant.evolutionConversions[0] == source.evolutionConversions[0])
            throw new Exception("migration did not copy conversion history independently");
        Console.WriteLine("EVOLUTION_AUDIT checkpoint=7 probability=1% reciprocal_edge=ok founders=10 local_recovery=ok");
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
