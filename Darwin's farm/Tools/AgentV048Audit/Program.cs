using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DarwinFarm.Environment;

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
        => new BlockInfo { name = "Tile_0_0", plantBiomass = stock,
            maxPlantBiomass = 10000000f,
            habitatRecovery = 1000000, temperature = 50, humidity = 50,
            community = new List<PopulationData> { population } };

    private static SimulationController Controller(BlockInfo block)
    {
        var simulation = new SimulationController();
        simulation.SetMutationEnabled(false);
        simulation.SetMigrationParameters(false, 7, 100, .8f, .5f, .2f, 7);
        simulation.SetBlocks(new List<BlockInfo> { block });
        simulation.ConfigureDeterministicRun(240);
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

    private static string Replay(int seed, bool changeDay48)
    {
        var species = Species("replay-grazer", 10);
        var population = Population(species, 400);
        population.movementAbility = 80;
        var source = Block(50000f, population);
        var destination = Block(50000f, population);
        destination.name = "Tile_1_0";
        destination.community = new List<PopulationData>();
        source.SetNeighbors(new List<BlockInfo> { destination });
        destination.SetNeighbors(new List<BlockInfo> { source });
        var simulation = new SimulationController();
        simulation.SetMigrationParameters(true, 7, 100, .8f, .5f, .2f, 7);
        simulation.SetBlocks(new List<BlockInfo> { destination, source });
        simulation.ConfigureDeterministicRun(seed);
        LandLifeRules.EnsureBodyMass(population, simulation.ReadLandRules());
        var daily = new StringBuilder();
        daily.AppendLine(simulation.CanonicalModelHash());
        for (int day = 1; day <= 120; day++)
        {
            if (day == 49 && changeDay48)
                simulation.ApplyPlantBiomass(source, source.plantBiomass * .75f);
            float before = Total(source, destination);
            simulation.SimulateDay(day);
            float after = Total(source, destination);
            if (after > before + source.plantGrowthToday +
                destination.plantGrowthToday + .5f)
                throw new Exception("Unfunded material at day " + day +
                    ": " + after + " > " + before);
            daily.AppendLine(simulation.CanonicalModelHash());
        }
        using (SHA256 sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(daily.ToString()));
            return System.Convert.ToHexString(bytes);
        }
    }

    private static float Total(params BlockInfo[] blocks)
    {
        float total = 0f;
        foreach (BlockInfo block in blocks)
        {
            total += block.plantBiomass + block.detritusBiomass;
            foreach (PopulationData population in block.community)
                if (population != null)
                    total += population.bodyBiomass + population.energyReserve;
        }
        return total;
    }

    private static void Main(string[] args)
    {
        string replay = Replay(240, false);
        Require(replay == Replay(240, false),
            "same seed and starting preset reproduce every daily model hash");
        Require(replay != Replay(240, true),
            "a changed Day 48 action changes the replay after its prefix");
        Console.WriteLine("REPLAY_DIGEST=" + replay);

        var motherSpecies = Species("mother", 10);
        var childSpecies = Species("child", 15);
        motherSpecies.evolutionTargets.Add(childSpecies);
        var mother = Population(motherSpecies, 4000);
        var block = Block(10000000f, mother);
        var simulation = Controller(block);
        var plan = Plan(simulation, block, mother, childSpecies);
        Require(plan.valid && plan.amount == 400 && plan.payment == 60000f,
            "4000 animals request 10 percent without a 100 cap");
        var thousand = Population(motherSpecies, 1000);
        var thousandBlock = Block(10000000f, thousand);
        Require(Plan(Controller(thousandBlock), thousandBlock,
            thousand, childSpecies).amount == 100,
            "1000 animals request exactly 100 founders");
        var tenThousand = Population(motherSpecies, 10000);
        var tenThousandBlock = Block(10000000f, tenThousand);
        Require(Plan(Controller(tenThousandBlock), tenThousandBlock,
            tenThousand, childSpecies).amount == 1000,
            "10000 animals request exactly 1000 founders");
        mother.directedSplitTarget = childSpecies;
        mother.mutationSelectionProgress = .5f;
        simulation.RecordPlayerTechnology(block,
            EnvironmentTechnologyKind.PlantRecoveryChange, -1, 4);
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
            child.evolutionConversions.Count == 0 &&
            mother.directedInfluences.Count == 0 &&
            child.directedInfluences.Count == 0,
            "formal evolution clears prior target and mutation pressure");
        Require(Math.Abs(block.plantBiomass - 9940000f) < .01f &&
            Math.Abs(mother.bodyBiomass + child.bodyBiomass - 1260000f) < .01f,
            "extra child body material is paid from real plant stock");
        simulation.RecordPlayerTechnology(block,
            EnvironmentTechnologyKind.PlantRecoveryChange, -1, 5);
        Require(mother.directedInfluences.Count == 3 &&
            child.directedInfluences.Count == 3 &&
            mother.directedInfluences[0].firstDay == 6,
            "a new player action after formal evolution reaches both groups next day");
        simulation.SetMutationEnabled(true);
        simulation.SetMutationParameters(1, 5, 0);
        int[] motherBefore = { mother.fitTemperature, mother.fitHumidity,
            mother.movementAbility, mother.size, mother.fertility };
        int[] childBefore = { child.fitTemperature, child.fitHumidity,
            child.movementAbility, child.size, child.fertility };
        simulation.SimulateDay(6);
        Require(ChangedTraits(mother, motherBefore) == 1 &&
            ChangedTraits(child, childBefore) == 1,
            "the next day gives each group one independent one-point trait change");

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

        var harvestedPopulation = Population(motherSpecies, 100);
        var harvestBlock = Block(10000f, harvestedPopulation);
        var harvestSimulation = Controller(harvestBlock);
        float bodyBeforeHarvest = harvestedPopulation.bodyBiomass;
        Require(harvestSimulation.HarvestPopulation(harvestBlock,
            harvestedPopulation, 50) &&
            harvestedPopulation.speciesAmount == 50 &&
            Math.Abs(harvestedPopulation.bodyBiomass -
                bodyBeforeHarvest * .5f) < .01f,
            "harvest removes the same fraction of real body material");

        var protectedSpecies = Species("protected", 100);
        protectedSpecies.baseMovementAbility = 0;
        protectedSpecies.baseFertility = 10;
        var protectedTarget = Species("protected-target", 100);
        protectedTarget.baseMovementAbility = 0;
        protectedTarget.baseFertility = 10;
        protectedSpecies.evolutionTargets.Add(protectedTarget);
        var protectedPopulation = Population(protectedSpecies, 100);
        protectedPopulation.populationGrowthHistory = new List<float>
        { -.02f, -.02f, -.02f, -.02f, -.02f,
          -.02f, -.02f, -.02f, -.02f, -.02f };
        var protectedBlock = Block(0f, protectedPopulation);
        protectedBlock.habitatRecovery = 0;
        var protectedSimulation = Controller(protectedBlock);
        typeof(SimulationController).GetMethod("RefreshTrackedTarget", Private)
            .Invoke(protectedSimulation,
                new object[] { protectedBlock, protectedPopulation, 5 });
        Require(protectedPopulation.trackedEvolutionTarget == protectedTarget &&
            protectedPopulation.targetProtectionSince[0] == 5 &&
            protectedPopulation.targetProtectionSince[1] == 5 &&
            protectedPopulation.targetProtectionSince[2] == 5,
            "a valid target tracks and protects each strategy interval");
        typeof(SimulationController).GetMethod("ReleaseTargetProtectionFIFO", Private)
            .Invoke(protectedSimulation,
                new object[] { protectedBlock, protectedPopulation });
        Require(protectedPopulation.targetProtectionReleased[0] &&
            !protectedPopulation.targetProtectionReleased[1] &&
            !protectedPopulation.targetProtectionReleased[2],
            "sustained uncompensated decline releases one oldest protection");
        typeof(SimulationController).GetMethod("RefreshTrackedTarget", Private)
            .Invoke(protectedSimulation,
                new object[] { protectedBlock, protectedPopulation, 6 });
        Require(protectedPopulation.targetProtectionReleased[0],
            "released target protection does not reattach inside its interval");

        var formalMotherSpecies = Species("formal-chicken", 8);
        formalMotherSpecies.baseFitTemperature = 55;
        formalMotherSpecies.baseFitHumidity = 35;
        formalMotherSpecies.baseMovementAbility = 10;
        formalMotherSpecies.baseFertility = 70;
        var formalChildSpecies = Species("formal-colored-chicken", 15);
        formalChildSpecies.baseFitTemperature = 70;
        formalChildSpecies.baseFitHumidity = 49;
        formalChildSpecies.baseMovementAbility = 15;
        formalChildSpecies.baseFertility = 75;
        formalMotherSpecies.evolutionTargets.Add(formalChildSpecies);
        var formalMother = Population(formalMotherSpecies, 2000);
        formalMother.fitTemperature = 70;
        formalMother.fitHumidity = 49;
        formalMother.movementAbility = 15;
        formalMother.size = 10;
        formalMother.fertility = 75;
        var formalBlock = Block(500000f, formalMother);
        formalBlock.habitatRecovery = 90000;
        formalBlock.temperature = 75;
        formalBlock.humidity = 55;
        var formalSimulation = Controller(formalBlock);
        var support = typeof(SimulationController).GetMethod(
            "HasEvolutionFoodSupport", Private);
        formalMother.speciesAmount = 500;
        var formalPlan = Plan(formalSimulation, formalBlock,
            formalMother, formalChildSpecies);
        Require((bool)support.Invoke(formalSimulation,
                new object[] { formalBlock, formalMother,
                    formalChildSpecies, formalPlan.amount }) &&
            (bool)eligible.Invoke(formalSimulation,
                new object[] { formalBlock, formalMother,
                    formalChildSpecies, 1 }),
            "a connected group inside all five intervals passes the funded food gate");
    }

    private static int ChangedTraits(PopulationData population, int[] before)
    {
        int[] after = { population.fitTemperature, population.fitHumidity,
            population.movementAbility, population.size, population.fertility };
        int changed = 0;
        for (int i = 0; i < after.Length; i++)
        {
            if (Math.Abs(after[i] - before[i]) > 1)
                throw new Exception("A trait jumped by more than one point.");
            if (after[i] != before[i]) changed++;
        }
        return changed;
    }
}
