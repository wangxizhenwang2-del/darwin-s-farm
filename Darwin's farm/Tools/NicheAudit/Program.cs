using System;
using System.Collections.Generic;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static BlockInfo Block(int elevation, bool water, float food = 0f)
    {
        return new BlockInfo
        {
            elevation = elevation,
            waterCoverage = water ? WaterCoverage.Lake : WaterCoverage.Land,
            temperature = 50,
            humidity = 50,
            algaeBiomass = food,
            maxAlgaeBiomass = 10000f,
            plantBiomass = food,
            maxPlantBiomass = 10000f,
            habitatRecovery = (int)food,
            community = new List<PopulationData>()
        };
    }

    private static PopulationData Population(int amount, EcologicalNiche niche,
        string lineage = "ancestor")
    {
        return new PopulationData
        {
            speciesAmount = amount,
            ecologicalNiche = niche,
            lineageName = lineage,
            habitatNiche = niche == EcologicalNiche.Water ? 0 :
                niche == EcologicalNiche.Air ? 100 : 50,
            size = 10,
            movementAbility = 10,
            fitTemperature = 50,
            fitHumidity = 50,
            trophicLevelInitialized = true,
            carryingCapacity = 100f,
            energyNeed = 10.2f
        };
    }

    private static SimulationController Controller(params BlockInfo[] blocks)
    {
        SimulationController controller = new SimulationController();
        controller.SetBlocks(new List<BlockInfo>(blocks));
        controller.SetEcologicalNicheProbabilities(1f, 1f, 1f, 1f, 1f);
        controller.SetMigrationParameters(true, 1, 100f, 0.8f, 1f, 0.5f, 0);
        return controller;
    }

    private static void TopologyAndFitness()
    {
        BlockInfo a = Block(0, true, 100f);
        BlockInfo b = Block(0, true, 200f);
        BlockInfo high = Block(1, true, 300f);
        BlockInfo isolated = Block(0, true, 400f);
        a.temperature = 20;
        b.temperature = 40;
        a.humidity = 0;
        b.humidity = 100;
        a.SetNeighbors(new List<BlockInfo> { b });
        b.SetNeighbors(new List<BlockInfo> { high });
        high.SetNeighbors(new List<BlockInfo>());
        WaterRegionMap map = WaterRegionMap.Build(new[] { a, b, high, isolated });
        WaterRegion region = map.GetRegion(a);
        Check(map.Regions.Count == 3 && region == map.GetRegion(b) &&
            region != map.GetRegion(high), "water topology");
        Check(region.AlgaeBiomass == 300f && region.AvailableFood == 300f &&
            region.AverageTemperature == 30f && region.AverageHumidity == 50f,
            "water aggregation");
        PopulationData fish = Population(20, EcologicalNiche.Water);
        fish.fitTemperature = 30;
        fish.fitHumidity = 0;
        float fitness = SimulationController.CalculateWaterFitness(region, fish);
        fish.fitHumidity = 100;
        Check(fitness == 1f && SimulationController.CalculateWaterFitness(region, fish) == fitness,
            "humidity changed aquatic fitness");
    }

    private static void ConversionAndSwitch()
    {
        BlockInfo land = Block(0, false, 100f);
        BlockInfo sea = Block(0, true, 1000f);
        land.SetNeighbors(new List<BlockInfo> { sea });
        sea.SetNeighbors(new List<BlockInfo> { land });
        PopulationData ancestor = Population(106, EcologicalNiche.Land);
        ancestor.species = new SpeciesData();
        land.community.Add(ancestor);
        SimulationController controller = Controller(land, sea);
        Check(!controller.TryLandDepartureConversion(land, ancestor, 1, out _),
            "disabled switch allowed conversion");
        controller.SetEcologicalNichesEnabled(true);
        Check(controller.TryLandDepartureConversion(land, ancestor, 1,
            out PopulationData fish), "land to water failed");
        Check(ancestor.speciesAmount == 95 && fish.speciesAmount == 11 &&
            sea.community.Contains(fish) && fish.ecologicalNiche == EcologicalNiche.Water &&
            !SimulationController.SameSpecies(ancestor, fish) && fish.species == null &&
            !string.IsNullOrEmpty(fish.speciesId), "conversion split or species identity");

        PopulationData tooSmall = Population(100, EcologicalNiche.Land, "small");
        land.community.Add(tooSmall);
        Check(!controller.TryLandDepartureConversion(land, tooSmall, 1, out _) &&
            tooSmall.speciesAmount == 100, "ten individuals were converted");

        BlockInfo skyGround = Block(0, false, 100f);
        PopulationData flyerAncestor = Population(200, EcologicalNiche.Land, "flyer");
        skyGround.community.Add(flyerAncestor);
        SimulationController airController = Controller(skyGround);
        airController.SetEcologicalNichesEnabled(true);
        Check(airController.TryLandDepartureConversion(skyGround, flyerAncestor, 1,
            out PopulationData flyer) && flyer.speciesAmount == 20 &&
            skyGround.community.Count == 2, "land to air split");
        PopulationData second = Population(200, EcologicalNiche.Land, "second");
        skyGround.community.Add(second);
        Check(!airController.TryLandDepartureConversion(skyGround, second, 1, out _),
            "air slot accepted a second population");
    }

    private static void WaterAndAirMoves()
    {
        BlockInfo a = Block(0, true, 100f);
        BlockInfo b = Block(0, true, 100f);
        BlockInfo high = Block(1, true, 10000f);
        BlockInfo shore = Block(0, false, 100f);
        a.SetNeighbors(new List<BlockInfo> { b });
        b.SetNeighbors(new List<BlockInfo> { a, high, shore });
        high.SetNeighbors(new List<BlockInfo> { b });
        shore.SetNeighbors(new List<BlockInfo> { b });
        PopulationData fish = Population(200, EcologicalNiche.Water, "fish");
        a.community.Add(fish);
        SimulationController controller = Controller(a, b, high, shore);
        controller.SetEcologicalNichesEnabled(true);
        WaterRegionMap regions = controller.BuildWaterRegions();
        Check(regions.GetRegion(a) == regions.GetRegion(b), "same region missing");
        Check(!controller.TryWaterMigration(a, b, fish, regions, 1),
            "ordinary migration happened within a region");
        Check(controller.MoveWithinWaterRegion(a, b, fish, regions, 1) &&
            a.community.Count == 0 && b.community[0].speciesAmount == 200,
            "whole population did not move within region");
        PopulationData moved = b.community[0];
        Check(controller.TryWaterMigration(b, high, moved, regions, 2) &&
            b.community[0].speciesAmount == 100 && high.community[0].speciesAmount == 100 &&
            SimulationController.SameSpecies(b.community[0], high.community[0]),
            "cross-region split changed identity or number");
        // 向陆地转换继续遵守 10% 且 >10 的门槛。
        Check(!controller.TryWaterToLandConversion(b, shore, b.community[0],
            regions, 3, out _), "water to land accepted a ten-individual split");
        PopulationData largeFish = Population(200, EcologicalNiche.Water, "large-fish");
        b.community.Add(largeFish);
        Check(controller.TryWaterToLandConversion(b, shore, largeFish, regions, 3,
            out PopulationData landChild) && landChild.speciesAmount == 20 &&
            largeFish.speciesAmount == 180 &&
            landChild.ecologicalNiche == EcologicalNiche.Land &&
            !SimulationController.SameSpecies(largeFish, landChild),
            "water to land conversion");

        BlockInfo airSource = Block(0, false, 100f);
        BlockInfo airTarget = Block(2, true, 10000f);
        airTarget.plantBiomass = 0f;
        airTarget.habitatRecovery = 0;
        airSource.SetNeighbors(new List<BlockInfo> { airTarget });
        airTarget.SetNeighbors(new List<BlockInfo> { airSource });
        PopulationData bird = Population(100, EcologicalNiche.Air, "bird");
        bird.carryingCapacity = 0f;
        airSource.community.Add(bird);
        SimulationController airController = Controller(airSource, airTarget);
        airController.SetEcologicalNichesEnabled(true);
        Check(airController.TryAirMigration(airSource, airTarget, bird, 1) &&
            bird.speciesAmount == 50 && airTarget.community[0].speciesAmount == 50 &&
            SimulationController.SameSpecies(bird, airTarget.community[0]),
            "air migration failed across elevation difference two");
    }

    private static void Main()
    {
        TopologyAndFitness();
        ConversionAndSwitch();
        WaterAndAirMoves();
        Console.WriteLine("NICHE_AUDIT passed: topology, resources, fitness, switch, conversions and migrations");
    }
}
