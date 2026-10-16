using System;
using System.Collections.Generic;
using System.Reflection;

internal static class Program
{
    private static readonly BindingFlags Private = BindingFlags.Instance |
        BindingFlags.NonPublic;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    private static SpeciesData Species(string name, int size)
    {
        var species = new SpeciesData { name = name, trophicLevel = 0,
            baseSize = size, baseMovementAbility = 20,
            baseFitTemperature = 50, baseFitHumidity = 50,
            baseFertility = 50 };
        typeof(SpeciesData).GetField("speciesName", Private)
            .SetValue(species, name);
        return species;
    }

    private static PopulationData Population(SpeciesData species, int count)
        => new PopulationData { species = species, speciesAmount = count,
            size = species.baseSize, movementAbility = species.baseMovementAbility,
            fitTemperature = species.baseFitTemperature,
            fitHumidity = species.baseFitHumidity,
            fertility = species.baseFertility,
            trophicLevelInitialized = true };

    private static BlockInfo Block(float stock, PopulationData population)
        => new BlockInfo { plantBiomass = stock, maxPlantBiomass = 10000000f,
            habitatRecovery = 1000000, temperature = 50, humidity = 50,
            community = new List<PopulationData> { population } };

    private static SimulationController Controller(BlockInfo block)
    {
        var simulation = new SimulationController();
        simulation.SetMutationEnabled(false);
        simulation.SetMigrationParameters(false, 7, 100, .8f, .5f, .2f, 7);
        simulation.SetBlocks(new List<BlockInfo> { block });
        return simulation;
    }

    private static (bool valid, int amount, float payment, float excess)
        Plan(SimulationController simulation, BlockInfo block,
            PopulationData source, SpeciesData target)
    {
        var method = typeof(SimulationController).GetMethod(
            "TryPlanFormalEvolution", Private);
        object[] arguments = { block, source, target, 0, 0f, 0f };
        bool valid = (bool)method.Invoke(simulation, arguments);
        return (valid, (int)arguments[3], (float)arguments[4],
            (float)arguments[5]);
    }

    private static void Convert(SimulationController simulation, BlockInfo block,
        PopulationData source, SpeciesData target)
        => typeof(SimulationController).GetMethod("ConvertToPresetSpecies", Private)
            .Invoke(simulation, new object[] { block, source, target, 5 });

    private static void Main()
    {
        var motherSpecies = Species("mother", 10);
        var childSpecies = Species("child", 15);
        motherSpecies.evolutionTargets.Add(childSpecies);
        var mother = Population(motherSpecies, 4000);
        var block = Block(10000000f, mother);
        var simulation = Controller(block);
        var plan = Plan(simulation, block, mother, childSpecies);
        Require(plan.valid && plan.amount == 400 && plan.payment == 60000f,
            "4000 animals request 10 percent without a 100 cap");
        mother.directedSplitTarget = childSpecies;
        mother.mutationSelectionProgress = .5f;
        mother.evolutionConversions.Add(new EvolutionConversionRecord
            { target = childSpecies, effectiveDays = 5 });
        Convert(simulation, block, mother, childSpecies);
        Require(block.community.Count == 2 && mother.speciesAmount == 3600 &&
            block.community[1].speciesAmount == 400,
            "formal evolution commits one 10 percent child population");
        var child = block.community[1];
        Require(mother.size == 10 && child.size == 15 &&
            child.movementAbility == childSpecies.baseMovementAbility,
            "mother retains traits and child receives the target template");
        Require(mother.directedSplitTarget == null &&
            mother.mutationSelectionProgress == 0f &&
            mother.evolutionConversions.Count == 0 &&
            child.evolutionConversions.Count == 0,
            "formal evolution clears prior target and mutation pressure");
        Require(Math.Abs(block.plantBiomass - 9940000f) < .01f &&
            Math.Abs(mother.bodyBiomass + child.bodyBiomass - 1260000f) < .01f,
            "extra child body material is paid from real plant stock");

        var scarceMother = Population(motherSpecies, 1000);
        var scarceBlock = Block(1600f, scarceMother);
        var scarceSimulation = Controller(scarceBlock);
        var scarcePlan = Plan(scarceSimulation, scarceBlock,
            scarceMother, childSpecies);
        Require(scarcePlan.valid && scarcePlan.amount == 10 &&
            scarcePlan.payment == 1500f,
            "limited stock reduces the actual child cohort");
        scarceBlock.plantBiomass = 149f;
        var rejected = Plan(scarceSimulation, scarceBlock,
            scarceMother, childSpecies);
        Require(!rejected.valid && scarceMother.speciesAmount == 1000 &&
            scarceBlock.community.Count == 1 && scarceBlock.plantBiomass == 149f,
            "unaffordable conversion leaves the original state unchanged");

        var existing = Population(childSpecies, 20);
        block.community.Add(existing);
        var eligible = typeof(SimulationController).GetMethod(
            "IsEligibleEvolutionTarget", Private);
        Require(!(bool)eligible.Invoke(simulation,
            new object[] { block, mother, childSpecies, 6 }),
            "a local target species cannot be created twice");

        var grazer = Population(motherSpecies, 160);
        var grass = Block(50000f, grazer);
        grass.habitatRecovery = 50000;
        var grassSimulation = Controller(grass);
        LandLifeRules.EnsureBodyMass(grazer, grassSimulation.ReadLandRules());
        float initialTotal = grass.plantBiomass + grazer.bodyBiomass;
        grassSimulation.SimulateBlock(grass);
        float finalTotal = grass.plantBiomass + grazer.bodyBiomass +
            grass.detritusBiomass + grazer.energyReserve;
        Require(grazer.birthsToday >= 0 &&
            finalTotal <= initialTotal + grass.plantGrowthToday + .1f &&
            grass.plantBiomass >= 0f,
            "daily births do not create body material beyond real intake");

        var prey = Population(motherSpecies, 500);
        var predatorSpecies = Species("predator", 15);
        predatorSpecies.trophicLevel = 1;
        predatorSpecies.preySpecies.Add(motherSpecies);
        var predator = Population(predatorSpecies, 20);
        predator.trophicLevel = 1;
        var huntingGround = Block(100000f, prey);
        huntingGround.community.Add(predator);
        var huntingSimulation = Controller(huntingGround);
        LandLifeRules.EnsureBodyMass(prey, huntingSimulation.ReadLandRules());
        LandLifeRules.EnsureBodyMass(predator, huntingSimulation.ReadLandRules());
        float preHuntTotal = huntingGround.plantBiomass + prey.bodyBiomass +
            predator.bodyBiomass;
        huntingSimulation.SimulateBlock(huntingGround);
        float postHuntTotal = huntingGround.plantBiomass + prey.bodyBiomass +
            predator.bodyBiomass + predator.energyReserve +
            huntingGround.detritusBiomass;
        Require(prey.predationDeathsToday > 0 &&
            postHuntTotal <= preHuntTotal + huntingGround.plantGrowthToday + .1f,
            "predation transfers only material removed from real prey");
    }
}
