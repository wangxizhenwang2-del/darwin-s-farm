using System.Reflection;

// Small deterministic probes of the production rules. This command does not
// write the historical design-audit reports or claim route reachability.
internal static class RuleChangeSmoke
{
    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    private static PopulationData Population(SpeciesData species, int count) => new()
    {
        species = species, speciesAmount = count, trophicLevel = species.trophicLevel,
        trophicLevelInitialized = true, fitTemperature = species.baseFitTemperature,
        fitHumidity = species.baseFitHumidity,
        movementAbility = species.baseMovementAbility, size = species.baseSize,
        fertility = species.baseFertility
    };

    public static void Run()
    {
        var original = new SpeciesData
        {
            baseFitTemperature = 55, baseFitHumidity = 35,
            baseMovementAbility = 10, baseSize = 8, baseFertility = 70,
            baseEcologicalNiche = EcologicalNiche.Land
        };
        var target = new SpeciesData
        {
            baseFitTemperature = 70, baseFitHumidity = 49,
            baseMovementAbility = 15, baseSize = 15, baseFertility = 75,
            baseEcologicalNiche = EcologicalNiche.Land
        };
        original.evolutionTargets.Add(target);
        var source = Population(original, 300);
        for (int i = 0; i < 10; i++) source.foodSatisfactionHistory.Add(1f);
        var block = new BlockInfo
        {
            waterCoverage = WaterCoverage.Land,
            temperature = 55, humidity = 35,
            maxPlantBiomass = 1000000f, plantBiomass = 1000000f,
            habitatRecovery = 21800,
            community = new List<PopulationData> { source }
        };
        var simulation = new SimulationController();
        simulation.SetEvolutionNetworkSpecies(new[] { original, target });
        simulation.SetBlocks(new List<BlockInfo> { block });
        LandRuleTuning rules = simulation.ReadLandRules();
        Require(rules.directedSplitTraitFraction == 1f &&
            rules.directedSplitTraitLockDays == 50 &&
            rules.sameSpeciesMergeDays == 100 &&
            rules.postMergeSplitCooldownDays == 200 &&
            rules.splitMinimumRenewalPressure == 0.9f,
            "default split, unlock, merge and retry timings");
        var ready = simulation.ReadSplitGate(block, source, 1);
        Require(ready.reason == "ready" && ready.target == target &&
            ready.founders == 15 &&
            source.speciesAmount >= ready.environmentalK * 0.9f,
            "90% K, 5% founders and target food gate");
        var desertChicken = Population(original, 693);
        desertChicken.size = 12;
        desertChicken.movementAbility = 8;
        desertChicken.fertility = 68;
        for (int i = 0; i < 10; i++)
            desertChicken.foodSatisfactionHistory.Add(0.985f);
        var desert = new BlockInfo
        {
            waterCoverage = WaterCoverage.Land,
            temperature = 55, humidity = 35,
            maxPlantBiomass = 1000000f, plantBiomass = 980000f,
            habitatRecovery = 50000,
            community = new List<PopulationData> { desertChicken }
        };
        var desertSimulation = new SimulationController();
        desertSimulation.SetEvolutionNetworkSpecies(new[] { original, target });
        desertSimulation.SetBlocks(new List<BlockInfo> { desert });
        var desertGate = desertSimulation.ReadSplitGate(desert, desertChicken, 281);
        Require(desertChicken.speciesAmount >= desertGate.environmentalK * 0.9f &&
            desertChicken.speciesAmount < desertGate.environmentalK * 0.95f &&
            desertGate.reason == "ready" && desertGate.target == target,
            "logged 693-chicken plateau can now split toward colorful chicken");
        LandRuleTuning formerRules = desertSimulation.ReadLandRules();
        formerRules.splitMinimumRenewalPressure = 0.95f;
        desertSimulation.ApplyLandRules(formerRules);
        Require(desertSimulation.ReadSplitGate(desert, desertChicken, 281).reason ==
            "below_split_K", "the former 95% setting blocks the same chicken group");
        var serializedRules = new LandRuleTuning();
        typeof(LandRuleTuning).GetField("ruleVersion",
            BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(serializedRules, 4);
        serializedRules.splitMinimumRenewalPressure = 0.95f;
        serializedRules.Sanitize();
        Require(serializedRules.splitMinimumRenewalPressure == 0.9f,
            "version-4 scene rules migrate from 95% to 90%");
        desertSimulation.ApplyLandRules(serializedRules);
        typeof(SimulationController).GetMethod("RunLocalSplits",
            BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(desertSimulation, new object[] { 281 });
        Require(desert.community.Count == 2 &&
            desert.community[1].directedSplitTarget == target,
            "logged desert chicken group creates its colorful-chicken branch");
        block.nextLocalSplitDay = 2;
        Require(simulation.ReadSplitGate(block, source, 1).reason ==
            "split_cooldown", "cooldown reason is inspectable");
        block.nextLocalSplitDay = 0;
        typeof(SimulationController).GetMethod("RunLocalSplits",
            BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(simulation, new object[] { 1 });
        Require(block.community.Count == 2, "directed branch created");
        PopulationData branch = block.community[1];
        Require(branch.movementAbility == 15 && branch.size == 15 &&
            branch.fertility == 75 && branch.directedTraitUnlockDay == 51,
            "100% approach and 50-day lock deadline");
        Require(SimulationController.IsDirectedTraitLocked(branch, 50) &&
            !SimulationController.IsDirectedTraitLocked(branch, 51),
            "only first 50 simulation days lock physical traits");
        int unlockEvents = 0;
        simulation.OnEcologyEvent += (day, tile, population, kind, detail) =>
        {
            if (kind == "directed_trait_unlock") unlockEvents++;
        };
        MethodInfo unlock = typeof(SimulationController).GetMethod(
            "LogDirectedTraitUnlocks", BindingFlags.NonPublic |
            BindingFlags.Instance)!;
        unlock.Invoke(simulation, new object[] { 50 });
        unlock.Invoke(simulation, new object[] { 51 });
        Require(unlockEvents == 1, "unlock emits one ecology event on day 51");
        rules.directedSplitTraitFraction = 0.6f;
        simulation.ApplyLandRules(rules);
        Require(simulation.ReadLandRules().directedSplitTraitFraction == 0.6f,
            "approach remains adjustable");

        var signalPopulation = Population(original, 100);
        block.community = new List<PopulationData> { signalPopulation };
        block.habitatRecovery = 5000;
        block.plantBiomass = 5000f;
        var scarce = simulation.ReadLandSelectionSignals(block, signalPopulation);
        block.habitatRecovery = 20000;
        block.plantBiomass = 1000000f;
        var abundant = simulation.ReadLandSelectionSignals(block, signalPopulation);
        Require(scarce.sizePressure < 0f && abundant.sizePressure > 0f &&
            scarce.fertilityPressure < 0f &&
            abundant.fertilityPressure > 0f,
            "accessible stock and sustainable supply give opposite trait cues");
        Require(abundant.movementPressure < 0f,
            "no available neighbor favors lower movement");
        var neighbor = new BlockInfo
        {
            waterCoverage = WaterCoverage.Land,
            temperature = 55, humidity = 35,
            plantBiomass = 1000000f, maxPlantBiomass = 1000000f,
            habitatRecovery = 20000,
            community = new List<PopulationData>()
        };
        block.SetNeighbors(new List<BlockInfo> { neighbor });
        var expansion = simulation.ReadLandSelectionSignals(block, signalPopulation);
        Require(expansion.neighborAttraction > rules.neighborAttractionThreshold &&
            expansion.movementPressure > 0f,
            "reachable empty neighbor favors higher movement");
        block.SetNeighbors(null);
        simulation.SetMutationParameters(1, 1f, 1f);
        simulation.SetMutationEnabled(true);
        MethodInfo selectTraits = typeof(SimulationController).GetMethod(
            "EvolveCommunityNewRules", BindingFlags.NonPublic |
            BindingFlags.Instance)!;
        for (int day = 0; day < 30; day++)
            selectTraits.Invoke(simulation, new object[] { block });
        Require(signalPopulation.size > original.baseSize &&
            signalPopulation.fertility > original.baseFertility,
            "abundant obtainable stock and renewal raise size and fertility");
        var lowRenewal = Population(original, 100);
        block.community = new List<PopulationData> { lowRenewal };
        block.habitatRecovery = 5000;
        block.plantBiomass = 1000000f;
        for (int day = 0; day < 30; day++)
            selectTraits.Invoke(simulation, new object[] { block });
        Require(lowRenewal.fertility < original.baseFertility,
            "low sustainable renewal reduces fertility despite stocked food");
        var large = new SpeciesData
        {
            baseFitTemperature = 55, baseFitHumidity = 35,
            baseMovementAbility = 50, baseSize = 50, baseFertility = 35,
            baseEcologicalNiche = EcologicalNiche.Land
        };
        var lowStock = Population(large, 100);
        block.community = new List<PopulationData> { lowStock };
        block.habitatRecovery = 20000;
        block.plantBiomass = 5000f;
        for (int day = 0; day < 30; day++)
            selectTraits.Invoke(simulation, new object[] { block });
        Require(lowStock.size < large.baseSize,
            "low obtainable stock reduces large-herbivore size");
        rules.resourceTraitGain = 0f;
        simulation.ApplyLandRules(rules);
        var stay = Population(large, 100);
        stay.movementAbility = 30;
        block.community = new List<PopulationData> { stay };
        block.plantBiomass = 1000000f;
        for (int day = 0; day < 20; day++)
            selectTraits.Invoke(simulation, new object[] { block });
        Require(stay.movementAbility < 30,
            "movement falls without reachable empty neighbors");
        var moving = Population(large, 100);
        moving.movementAbility = 30;
        block.community = new List<PopulationData> { moving };
        block.SetNeighbors(new List<BlockInfo> { neighbor });
        for (int day = 0; day < 20; day++)
            selectTraits.Invoke(simulation, new object[] { block });
        Require(moving.movementAbility > 30,
            "reachable empty neighbor raises movement");

        var sibling = Population(original, 20);
        signalPopulation.speciesAmount = 80;
        signalPopulation.sameSpeciesCoexistenceDays = 98;
        sibling.sameSpeciesCoexistenceDays = 98;
        block.community = new List<PopulationData> { signalPopulation, sibling };
        typeof(SimulationController).GetMethod("RunSameSpeciesMerges",
            BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(simulation, new object[] { 100 });
        Require(block.community.Count == 2, "same species wait through day 99");
        typeof(SimulationController).GetMethod("RunSameSpeciesMerges",
            BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(simulation, new object[] { 101 });
        Require(block.community.Count == 1 && block.nextLocalSplitDay == 301,
            "100-day forced merge starts 200-day split cooldown");
    }
}
