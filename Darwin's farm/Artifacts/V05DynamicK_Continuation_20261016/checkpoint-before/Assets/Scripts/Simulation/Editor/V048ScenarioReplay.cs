using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DarwinFarm.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run in a fresh batch Editor with DF_V048_SCENARIO and DF_V048_OUTPUT set.
// Actions are committed after the named day has completed, while the clock
// displays that day. They first affect ecology on the following day.
[InitializeOnLoad]
public static class V048ScenarioReplay
{
    [Serializable] private sealed class Scenario
    {
        public string scenarioId;
        public int seed;
        public int endDay;
        public bool disableCosts;
        public bool restrictedV05;
        public bool mechanismFixture;
        public bool fixtureFreezeTraits;
        public bool fixtureDisableMigration;
        public bool fixtureFixedEnvironment;
        public bool fixtureSkipDiagnostics;
        public bool useFoodKForSelection;
        public bool overridePreyEncounter;
        public float preyEncounterHalfSaturation;
        public string counterfactualTarget;
        public string counterfactualChanged;
        public int[] counterfactualCounts;
        public float originInitialStock;
        public int originInitialChickenCount;
        public string sourceGraph;
        public Tile[] tiles;
        public Action[] actions;
    }

    [Serializable] private sealed class Tile
    {
        public int x, y;
        public string preset;
        public float initialStock;
        public bool overrideEnvironment;
        public int temperature, humidity, recovery;
        public Population[] populations;
    }

    [Serializable] private sealed class Population
    {
        public string species;
        public int count;
        public bool overrideTraits;
        public int fitT, fitH, movement, size, fertility;
    }

    [Serializable] private sealed class Action
    {
        public int day, sequence, x, y, tier;
        public string kind, preset;
        public string species;
        public int amount, recovery;
        public int temperatureOffset, humidityOffset;
        [NonSerialized] public int inputIndex;
    }

    [Serializable] private sealed class DayRecord
    {
        public int day;
        public string modelHash;
        public string environmentHash;
        public long balance;
        public double totalMaterial;
        public float plantGrowthToday;
        public float accountedBiomassSinkToday;
        public TileState[] tiles;
    }

    [Serializable] private sealed class TileState
    {
        public int x, y, elevation;
        public float stock, detritus;
        public float plantGrowth, consumedPlants;
        public int recovery;
        public float temperature, humidity;
        public string temperaturePhase, humidityPhase;
        public int temperatureRemainingDays, humidityRemainingDays;
        public PopulationState[] populations;
    }

    [Serializable] private sealed class PopulationState
    {
        public string id, species;
        public int count, fitT, fitH, movement, size, fertility;
        public int births, deaths, predationDeaths;
        public float body, reserve, carryingCapacity, environmentalK;
        public float foodK;
        public float allocatedFood, adultNeedPerHead, energySatisfaction;
        public float huntingEnergyToday;
        public float birthRemainder, deathRemainder;
        public float adultDemand, adultPaid, birthOpportunity;
        public float birthBudget, newbornCost, unusedBirthFood;
        public int populationBeforePredation, endangeredDays;
    }

    [Serializable] private sealed class EcologyDiagnostic
    {
        public int day, x, y;
        public string species;
        public float foodK;
        public LandEquilibriumEstimate equilibrium;
    }

    private const string Pending = "DarwinFarm.V048Replay.Pending";
    private static double startedAt;
    private static bool complete;

    static V048ScenarioReplay()
    {
        if (SessionState.GetBool(Pending, false)) Register();
    }

    public static void Run()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Batch Editor required.");
        string input = Environment.GetEnvironmentVariable("DF_V048_SCENARIO");
        string output = Environment.GetEnvironmentVariable("DF_V048_OUTPUT");
        if (string.IsNullOrEmpty(input) || !File.Exists(input) ||
            string.IsNullOrEmpty(output))
            throw new InvalidOperationException("Set DF_V048_SCENARIO and DF_V048_OUTPUT.");
        Scenario selected = JsonUtility.FromJson<Scenario>(File.ReadAllText(input));
        EditorSceneManager.OpenScene(selected != null && selected.restrictedV05
            ? "Assets/Scenes/V05FiveSpeciesSandbox.unity" :
            "Assets/Scenes/WhiteBox.unity");
        SessionState.SetBool(Pending, true);
        Register();
        EditorApplication.EnterPlaymode();
    }

    private static void Register()
    {
        startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update -= WaitForScene;
        EditorApplication.update += WaitForScene;
    }

    private static void WaitForScene()
    {
        if (complete) return;
        if (EditorApplication.timeSinceStartup - startedAt > 300)
        {
            Finish(new Exception("Play scene initialization timed out; playing=" +
                EditorApplication.isPlaying + " compiling=" +
                EditorApplication.isCompiling));
            return;
        }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        var clock = UnityEngine.Object.FindFirstObjectByType<SimulationTime>();
        var environment = UnityEngine.Object.FindFirstObjectByType<SimulationEnvironmentController>();
        if (clock == null || environment == null ||
            !environment.TryRead(Vector2Int.zero, out _)) return;
        clock.Pause();
        string input = Environment.GetEnvironmentVariable("DF_V048_SCENARIO");
        if (!string.IsNullOrEmpty(input) && File.Exists(input))
        {
            Scenario pending = JsonUtility.FromJson<Scenario>(File.ReadAllText(input));
            if (pending != null && pending.restrictedV05)
            {
                var grid = environment.GetComponent<MapGridManager>();
                if (grid == null || !grid.TryGetTile(Vector2Int.zero,
                    out MapTileInstance initial) || initial.Block == null ||
                    initial.Block.community == null ||
                    initial.Block.community.Count != 1 ||
                    initial.Block.community[0] == null ||
                    initial.Block.community[0].speciesAmount != 160) return;
            }
        }
        try { Execute(clock, environment); Finish(null); }
        catch (Exception error) { Finish(error); }
    }

    private static void Execute(SimulationTime clock,
        SimulationEnvironmentController environment)
    {
        string input = Environment.GetEnvironmentVariable("DF_V048_SCENARIO");
        string output = Environment.GetEnvironmentVariable("DF_V048_OUTPUT");
        Scenario scenario = JsonUtility.FromJson<Scenario>(File.ReadAllText(input));
        if (scenario == null || string.IsNullOrEmpty(scenario.scenarioId) ||
            scenario.endDay < 1 || scenario.endDay > 10000 ||
            scenario.tiles == null || clock.currentDay != 0)
            throw new InvalidOperationException("Invalid scenario or starting clock.");

        var grid = environment.GetComponent<MapGridManager>();
        var bridge = environment.GetComponent<MapSimulationBridge>();
        var simulation = environment.GetComponent<SimulationController>();
        var player = environment.GetComponent<EnvironmentTechnologyController>();
        var economy = environment.GetComponent<PlayerEconomyController>();
        var account = environment.GetComponent<BioenergyAccount>();
        if (grid == null || bridge == null || simulation == null ||
            player == null || economy == null || account == null)
            throw new InvalidOperationException("Live gameplay components are missing.");
        if (scenario.restrictedV05)
        {
            ValidateRestrictedV05(scenario, grid, simulation);
            grid.TryGetTile(Vector2Int.zero, out MapTileInstance origin);
            if (scenario.originInitialStock > 50000f)
            {
                if (!simulation.ApplyPlantBiomass(origin.Block,
                        scenario.originInitialStock))
                    throw new InvalidOperationException("Cannot set V05 initial stock.");
            }
            if (scenario.originInitialChickenCount > 160)
            {
                PopulationData chicken = origin.Block.community[0];
                float bodyPerChicken = Mathf.Max(1f, chicken.size) *
                    simulation.ReadLandRules().preyEnergyPerSize;
                float extraBody = (scenario.originInitialChickenCount - 160) *
                    bodyPerChicken;
                if (origin.Block.plantBiomass < extraBody ||
                    !simulation.ApplyPlantBiomass(origin.Block,
                        origin.Block.plantBiomass - extraBody))
                    throw new InvalidOperationException(
                        "V05 initial stock cannot fund the additional chicken bodies.");
                chicken.speciesAmount = scenario.originInitialChickenCount;
                chicken.bodyBiomass = scenario.originInitialChickenCount *
                    bodyPerChicken;
                chicken.bodyBiomassInitialized = true;
            }
            if (scenario.mechanismFixture)
                origin.Block.community.Clear();
        }
        account.CostsEnabled = !scenario.disableCosts;
        foreach (Tile tile in scenario.tiles)
        {
            var coordinate = new Vector2Int(tile.x, tile.y);
            MapTileDefinition preset = AssetDatabase.LoadAssetAtPath<MapTileDefinition>(tile.preset);
            if (preset == null || !grid.TryPlaceTile(coordinate, preset) ||
                !bridge.TryGetBlock(coordinate, out BlockInfo block))
                throw new InvalidOperationException("Cannot place preset at " + coordinate);
            block.community.Clear();
            if (tile.initialStock > 0f &&
                !simulation.ApplyPlantBiomass(block, tile.initialStock))
                throw new InvalidOperationException("Cannot set starting plant stock.");
            if (tile.overrideEnvironment)
            {
                if (!scenario.mechanismFixture)
                    throw new InvalidOperationException("Environment overrides are fixture-only.");
                grid.TryGetTile(coordinate, out MapTileInstance placed);
                if (!environment.TryEditEnvironment(coordinate,
                    new SimulationEnvironmentEdit
                    {
                        temperature = tile.temperature, humidity = tile.humidity,
                        habitatRecovery = tile.recovery,
                        elevation = placed.HeightLevel, maxPlantBiomass = 1000000f,
                        plantBiomass = tile.initialStock
                    }, out string fixtureError))
                    throw new InvalidOperationException("Fixture environment: " + fixtureError);
            }
            if (tile.populations == null) continue;
            foreach (Population entry in tile.populations)
            {
                SpeciesData species = AssetDatabase.LoadAssetAtPath<SpeciesData>(entry.species);
                if (species == null || entry.count < 1)
                    throw new InvalidOperationException("Invalid starting population " + entry.species);
                var population = new PopulationData
                {
                    species = species, speciesAmount = entry.count,
                    lineageName = species.SpeciesName,
                    ecologicalNiche = species.baseEcologicalNiche,
                    habitatNiche = species.baseHabitatNiche,
                    trophicLevel = species.trophicLevel,
                    trophicLevelInitialized = true,
                    fitTemperature = species.baseFitTemperature,
                    fitHumidity = species.baseFitHumidity,
                    movementAbility = species.baseMovementAbility,
                    size = species.baseSize, fertility = species.baseFertility
                };
                if (entry.overrideTraits)
                {
                    population.fitTemperature = entry.fitT;
                    population.fitHumidity = entry.fitH;
                    population.movementAbility = entry.movement;
                    population.size = entry.size;
                    population.fertility = entry.fertility;
                }
                population.bodyBiomass = entry.count *
                    Mathf.Max(1f, population.size) *
                    simulation.ReadLandRules().preyEnergyPerSize;
                population.bodyBiomassInitialized = true;
                block.community.Add(population);
            }
        }
        simulation.RefreshSpeciesStatuses();
        LandRuleTuning selectedRules = simulation.ReadLandRules();
        selectedRules.useFoodCapacityForSelection = scenario.useFoodKForSelection;
        if (scenario.overridePreyEncounter)
            throw new InvalidOperationException(
                "Encounter experiments require the archived experiment-encounter-v3 code.");
        simulation.ApplyLandRules(selectedRules);
        if (scenario.mechanismFixture)
        {
            SimulationTuning fixtureTuning = simulation.ReadTuning();
            if (scenario.fixtureFreezeTraits) fixtureTuning.mutationEnabled = false;
            if (scenario.fixtureDisableMigration) fixtureTuning.migrationEnabled = false;
            simulation.ApplyTuning(fixtureTuning);
            if (scenario.fixtureFixedEnvironment)
                simulation.OnBeforeDaySimulated += _ =>
                {
                    foreach (Tile tile in scenario.tiles)
                    {
                        if (!tile.overrideEnvironment) continue;
                        var coordinate = new Vector2Int(tile.x, tile.y);
                        grid.TryGetTile(coordinate, out MapTileInstance placed);
                        bridge.TryGetBlock(coordinate, out BlockInfo block);
                        if (!environment.TryEditEnvironment(coordinate,
                            new SimulationEnvironmentEdit
                            {
                                temperature = tile.temperature,
                                humidity = tile.humidity,
                                habitatRecovery = tile.recovery,
                                elevation = placed.HeightLevel,
                                maxPlantBiomass = 1000000f,
                                plantBiomass = block.plantBiomass
                            }, out string error))
                            throw new InvalidOperationException(error);
                    }
                };
        }
        simulation.ConfigureDeterministicRun(scenario.seed);
        var actions = new List<Action>(scenario.actions ?? Array.Empty<Action>());
        for (int i = 0; i < actions.Count; i++) actions[i].inputIndex = i;
        actions.Sort((a, b) =>
        {
            int byDay = a.day.CompareTo(b.day);
            return byDay != 0 ? byDay : scenario.restrictedV05
                ? a.inputIndex.CompareTo(b.inputIndex) :
                a.sequence.CompareTo(b.sequence);
        });
        for (int i = 1; i < actions.Count; i++)
            if (!scenario.restrictedV05 && actions[i].day == actions[i - 1].day &&
                actions[i].sequence == actions[i - 1].sequence)
                throw new InvalidOperationException("Duplicate action sequence.");
        foreach (Action action in actions)
        {
            if (action.day < 0 || action.day > scenario.endDay)
                throw new InvalidOperationException("Action day outside run.");
            if (scenario.restrictedV05 &&
                (action.x < -1 || action.x > 1 || action.y < -1 || action.y > 1 ||
                 action.kind == "MonsoonAnchor" ||
                 !(action.kind == "PlaceTile" ||
                   scenario.mechanismFixture &&
                       (action.kind == "FixtureRecovery" || action.kind == "FixtureCull") ||
                   action.kind == "CancelTemperatureChange" ||
                   action.kind == "CancelHumidityChange" ||
                   Enum.TryParse(action.kind, out EnvironmentTechnologyKind kind) &&
                   Enum.IsDefined(typeof(EnvironmentTechnologyKind), kind))))
                throw new InvalidOperationException("Action outside V05 sandbox.");
            if (scenario.restrictedV05 && action.kind == "PlaceTile" &&
                (action.tier != 0 || string.IsNullOrEmpty(action.preset) ||
                 !action.preset.StartsWith("Assets/Scripts/map/data/",
                     StringComparison.Ordinal) ||
                 !action.preset.EndsWith(".asset", StringComparison.Ordinal)))
                throw new InvalidOperationException("Invalid V05 land placement preset.");
        }

        var trace = new StringBuilder();
        void RecordTransition(PopulationTransitionResult result)
        {
            trace.AppendLine("event day=" + result.Day + " kind=" + result.Kind +
                " source=" + result.SourceBlock.name +
                " target=" + result.TargetBlock.name +
                " sourcePopulation=" + result.SourcePopulationId +
                " resultPopulation=" + result.ResultPopulationId +
                " species=" + (result.TargetSpecies == null ? "" :
                    result.TargetSpecies.SpeciesName) +
                " amount=" + result.Amount +
                " sourceBefore=" + result.SourceCountBefore +
                " sourceAfter=" + result.SourceCountAfter +
                " targetBefore=" + result.TargetCountBefore +
                " targetAfter=" + result.TargetCountAfter +
                " outcome=" + result.Outcome);
        }
        simulation.OnMigrationResolved += RecordTransition;
        simulation.OnPresetEvolutionResolved += RecordTransition;
        simulation.OnEcologyEvent += (day, block, population, kind, detail) =>
        {
            if (kind == "trait_mutation" || kind == "mutation_choice") return;
            trace.AppendLine("ecology day=" + day + " tile=" + block.name +
                " kind=" + kind + " population=" +
                (population != null ? population.PopulationId : "") +
                " detail=" + detail);
        };
        var bindingFiles = new List<string>
        {
            scenario.restrictedV05 ?
                "Assets/Scenes/V05FiveSpeciesSandbox.unity" :
                "Assets/Scenes/WhiteBox.unity",
            "Assets/Scripts/Simulation/SimulationController.cs",
            "Assets/Scripts/Simulation/SimulationController.Determinism.cs",
            "Assets/Scripts/Simulation/SimulationController.Influence.cs",
            "Assets/Scripts/Simulation/Editor/V048ScenarioReplay.cs",
            "Assets/Scripts/Simulation/SimulationController.Evolution.cs",
            "Assets/Scripts/Simulation/SimulationController.LandTraits.cs",
            "Assets/Scripts/Simulation/SimulationController.Migration.cs",
            "Assets/Scripts/Simulation/LandLifeRules.cs",
            "Assets/Scripts/Simulation/LandRuleTuning.cs",
            "Assets/Scripts/Simulation/Environment/EnvironmentTechnologyController.cs",
            "Assets/Scripts/Simulation/Economy/PlayerEconomyController.cs"
        };
        foreach (Tile tile in scenario.tiles)
        {
            bindingFiles.Add(tile.preset);
            if (tile.populations != null)
                foreach (Population population in tile.populations)
                    bindingFiles.Add(population.species);
        }
        foreach (Action action in actions)
            if (action.kind == "PlaceTile") bindingFiles.Add(action.preset);
        foreach (string folder in new[] { "Assets/Scripts/Simulation",
            "Assets/Scripts/BioData", "Assets/Scripts/map" })
            foreach (string file in Directory.GetFiles(folder, "*.cs",
                SearchOption.AllDirectories))
                bindingFiles.Add(file.Replace('\\', '/'));
        foreach (string file in Directory.GetFiles("Assets/BiowebV46/Species",
            "*.asset", SearchOption.TopDirectoryOnly))
            bindingFiles.Add(file.Replace('\\', '/'));
        if (scenario.restrictedV05) bindingFiles.Add(scenario.sourceGraph);
        trace.AppendLine("scenario=" + scenario.scenarioId + " seed=" + scenario.seed +
            " mechanismFixture=" + scenario.mechanismFixture +
            " random=" + SimulationController.ModelRandomVersion +
            " unity=" + Application.unityVersion +
            " costs=" + account.CostsEnabled +
            " originInitialStock=" +
                (scenario.originInitialStock > 0f ? scenario.originInitialStock : 50000f)
                    .ToString("R", CultureInfo.InvariantCulture) +
            " originInitialChickenCount=" +
                (scenario.originInitialChickenCount > 0 ?
                    scenario.originInitialChickenCount : 160) +
            " phase=after-day-step" +
            " scenarioSha256=" + HashFiles(new List<string> { input }) +
            " bindingSha256=" + HashFiles(bindingFiles));
        trace.AppendLine("landRules=" + JsonUtility.ToJson(simulation.ReadLandRules()));
        trace.AppendLine("simulationTuning=" + JsonUtility.ToJson(simulation.ReadTuning()));
        WriteDay(trace, clock.currentDay, simulation, environment, grid, account);
        if (scenario.mechanismFixture && !scenario.fixtureSkipDiagnostics)
            WriteDiagnostics(trace, 0, simulation, grid);
        if (scenario.mechanismFixture && scenario.counterfactualCounts != null &&
            scenario.counterfactualCounts.Length > 0)
        {
            foreach (MapTileInstance tile in grid.GetPlacedTiles())
            {
                PopulationData target = tile.Block.community.Find(p =>
                    p.species != null &&
                    p.species.SpeciesName == scenario.counterfactualTarget);
                PopulationData changed = tile.Block.community.Find(p =>
                    p.species != null &&
                    p.species.SpeciesName == scenario.counterfactualChanged);
                if (target == null || changed == null) continue;
                foreach (int proposed in scenario.counterfactualCounts)
                    trace.AppendLine("counterfactual day=0 tile=" +
                        tile.Coordinate.x + "," + tile.Coordinate.y +
                        " target=" + scenario.counterfactualTarget +
                        " changed=" + scenario.counterfactualChanged +
                        " proposedCount=" + proposed +
                        " renewableIntake=" +
                        simulation.ReadCounterfactualPlantIntake(tile.Block,
                            target, changed, proposed)
                            .ToString("R", CultureInfo.InvariantCulture));
            }
        }
        int next = 0;
        int rejected = 0;
        for (int day = 0; day <= scenario.endDay; day++)
        {
            while (next < actions.Count && actions[next].day == day)
            {
                Action action = actions[next++];
                bool accepted;
                int cost;
                string error;
                var coordinate = new Vector2Int(action.x, action.y);
                double materialBeforeAction = TotalMaterial(grid);
                if (scenario.mechanismFixture && action.kind == "FixtureRecovery")
                {
                    Tile fixtureTile = Array.Find(scenario.tiles,
                        t => t.x == action.x && t.y == action.y &&
                            t.overrideEnvironment);
                    if (fixtureTile == null || action.recovery < 0 ||
                        action.recovery > 150000)
                        throw new InvalidOperationException("Invalid fixture R change.");
                    fixtureTile.recovery = action.recovery;
                    grid.TryGetTile(coordinate, out MapTileInstance placed);
                    bridge.TryGetBlock(coordinate, out BlockInfo block);
                    accepted = environment.TryEditEnvironment(coordinate,
                        new SimulationEnvironmentEdit
                        {
                            temperature = fixtureTile.temperature,
                            humidity = fixtureTile.humidity,
                            habitatRecovery = fixtureTile.recovery,
                            elevation = placed.HeightLevel,
                            maxPlantBiomass = 1000000f,
                            plantBiomass = block.plantBiomass
                        }, out error);
                    cost = 0;
                }
                else if (scenario.mechanismFixture && action.kind == "FixtureCull")
                {
                    bridge.TryGetBlock(coordinate, out BlockInfo block);
                    PopulationData victim = block.community.Find(p =>
                        p.species != null && p.species.SpeciesName == action.species);
                    accepted = simulation.HarvestPopulation(block, victim, action.amount);
                    cost = 0;
                    error = accepted ? null : "Fixture harvest rejected.";
                }
                else if (action.kind == "PlaceTile")
                {
                    MapTileDefinition preset = AssetDatabase.LoadAssetAtPath<MapTileDefinition>(
                        action.preset);
                    if (preset == null || preset.initialWaterCoverage != WaterCoverage.Land)
                        throw new InvalidOperationException("V05 placement requires a land preset.");
                    if (scenario.restrictedV05 &&
                        new List<MapTileInstance>(grid.GetPlacedTiles()).Count >= 5)
                        throw new InvalidOperationException("V05 placement exceeds five tiles.");
                    accepted = economy.TryExpand(coordinate, preset, 0, out error);
                    cost = 0;
                }
                else if (action.kind == "CancelTemperatureChange" ||
                    action.kind == "CancelHumidityChange")
                {
                    EnvironmentTechnologyKind cancelKind =
                        action.kind == "CancelTemperatureChange" ?
                        EnvironmentTechnologyKind.TemperatureChange :
                        EnvironmentTechnologyKind.HumidityChange;
                    accepted = player.TryCancel(coordinate, cancelKind,
                        out error);
                    cost = 0;
                }
                else
                {
                    if (!Enum.TryParse(action.kind,
                        out EnvironmentTechnologyKind kind) ||
                        !Enum.IsDefined(typeof(EnvironmentTechnologyKind), kind))
                        throw new InvalidOperationException(
                            "Unknown player action " + action.kind);
                    var choice = new EnvironmentTechnologyChoice(kind,
                        action.tier,
                        action.temperatureOffset == 0 ? 20 : action.temperatureOffset,
                        action.humidityOffset == 0 ? 20 : action.humidityOffset);
                    accepted = player.TryDeploy(coordinate, choice,
                        out EnvironmentTechnologyPreview preview);
                    cost = preview.Cost;
                    error = preview.Error;
                }
                if (!accepted) rejected++;
                trace.AppendLine("action day=" + day + " inputIndex=" + action.inputIndex +
                    " sequence=" + action.sequence +
                    " tile=" + action.x + "," + action.y + " kind=" + action.kind +
                    (action.kind == "PlaceTile" ? " preset=" + action.preset : "") +
                    " tier=" + action.tier + " cost=" + cost +
                    " species=" + action.species +
                    " amount=" + action.amount + " recovery=" + action.recovery +
                    " materialDelta=" + (TotalMaterial(grid) - materialBeforeAction)
                        .ToString("R", CultureInfo.InvariantCulture) +
                    " accepted=" + accepted +
                    (accepted ? "" : " error=" + error));
                if (scenario.restrictedV05) ValidateRestrictedV05World(grid);
                WriteDay(trace, day, simulation, environment, grid, account);
            }
            if (day == scenario.endDay) break;
            double beforeMaterial = TotalMaterial(grid);
            clock.NextDay();
            if (clock.currentDay != day + 1)
                throw new InvalidOperationException("Clock skipped a day.");
            if (scenario.restrictedV05)
                ValidateRestrictedV05World(grid);
            double afterMaterial = TotalMaterial(grid);
            float growth = PlantGrowth(grid);
            if (afterMaterial > beforeMaterial + growth +
                Math.Max(1f, beforeMaterial * 0.00001))
                throw new InvalidOperationException("Unfunded material on Day " +
                    clock.currentDay + ": " + afterMaterial + " > " +
                    (beforeMaterial + growth));
            WriteDay(trace, clock.currentDay, simulation, environment, grid, account);
        }
        if (scenario.mechanismFixture && !scenario.fixtureSkipDiagnostics)
            WriteDiagnostics(trace, scenario.endDay, simulation, grid);
        trace.AppendLine("result completedDays=" + scenario.endDay +
            " rejectedActions=" + rejected);
        File.WriteAllText(output, trace.ToString(), Encoding.UTF8);
        Debug.Log("V048_REPLAY_COMPLETE " + scenario.scenarioId +
            " rejectedActions=" + rejected);
        if (rejected > 0)
            throw new InvalidOperationException(rejected +
                " player actions were rejected; inspect the saved trace: " + output);
        Debug.Log("V048_REPLAY_OK " + scenario.scenarioId + " " + output);
    }

    private static void ValidateRestrictedV05(Scenario scenario,
        MapGridManager grid, SimulationController simulation)
    {
        if (!scenario.disableCosts ||
            (!scenario.mechanismFixture && scenario.tiles.Length != 0) ||
            string.IsNullOrEmpty(scenario.sourceGraph) ||
            !File.Exists(scenario.sourceGraph) ||
            float.IsNaN(scenario.originInitialStock) ||
            float.IsInfinity(scenario.originInitialStock) ||
            scenario.originInitialStock < 0f ||
            scenario.originInitialStock > 1000000f ||
            (scenario.originInitialStock > 0f &&
             scenario.originInitialStock < 50000f) ||
            (scenario.originInitialChickenCount != 0 &&
             scenario.originInitialChickenCount != 1000) ||
            (scenario.originInitialChickenCount == 1000 &&
             scenario.originInitialStock < 300000f))
            throw new InvalidOperationException("V05 requires the normal origin, no fixture overrides, and currency disabled.");
        ValidateRestrictedV05World(grid);
        if (!grid.TryGetTile(Vector2Int.zero, out MapTileInstance tile) ||
            tile.Block.community.Count != 1)
            throw new InvalidOperationException("V05 requires only the initial chicken population.");
        if (AssetDatabase.GetAssetPath(tile.Definition) !=
            "Assets/Scripts/map/data/荒漠.asset" ||
            tile.Block.temperature != 55 || tile.Block.humidity != 35 ||
            Mathf.Abs(tile.Block.plantBiomass - 50000f) > 0.01f ||
            tile.Block.habitatRecovery != 50000)
            throw new InvalidOperationException("V05 origin is not the 55/35/50000 desert.");
        PopulationData chicken = tile.Block.community[0];
        if (chicken == null || chicken.species == null ||
            chicken.species.SpeciesId != "7f1137399e3946cf95ad1054096acacb" ||
            chicken.speciesAmount != 160 ||
            chicken.fitTemperature != chicken.species.baseFitTemperature ||
            chicken.fitHumidity != chicken.species.baseFitHumidity ||
            chicken.movementAbility != chicken.species.baseMovementAbility ||
            chicken.size != chicken.species.baseSize ||
            chicken.fertility != chicken.species.baseFertility)
            throw new InvalidOperationException("V05 initial chicken is not the project default.");
        string[] ids = { "7f1137399e3946cf95ad1054096acacb",
            "761468a5c8a547f184caf7004ed6d4ae",
            "fd1c574bfd1247b2933cdfbc54758cf8",
            "b59a4a829b4949c5813cf76caa760b47",
            "f0686f4597f64629824aede9582a7ee1" };
        var five = new List<SpeciesData>(5);
        foreach (string id in ids)
        {
            SpeciesData species = simulation.FindSpeciesByEconomyId(id);
            if (species == null || species.SpeciesId != id ||
                species.baseEcologicalNiche != EcologicalNiche.Land)
                throw new InvalidOperationException("V05 source species missing: " + id);
            five.Add(species);
        }
        simulation.SetAllowedEvolutionSpecies(five);
        int[,] edges = { { 0, 1 }, { 1, 2 }, { 0, 3 }, { 3, 4 } };
        for (int i = 0; i < edges.GetLength(0); i++)
        if (!five[1].preySpecies.Contains(five[0]) ||
            !five[1].preySpecies.Contains(five[3]) ||
            !five[4].preySpecies.Contains(five[0]) ||
            five[2].trophicLevel != 0)
            throw new InvalidOperationException("V05 food graph differs from imported assets.");
        Debug.Log("V05_GRAPH_OK four evolution routes and five-species food relationships");
    }

    private static void ValidateRestrictedV05World(MapGridManager grid)
    {
        int count = 0;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            count++;
            if (tile == null || tile.Coordinate.x < -1 || tile.Coordinate.x > 1 ||
                tile.Coordinate.y < -1 || tile.Coordinate.y > 1 ||
                tile.Block == null || tile.Block.waterCoverage != WaterCoverage.Land)
                throw new InvalidOperationException("V05 tile outside the 3x3 land sandbox.");
        }
        if (count < 1 || count > 5)
            throw new InvalidOperationException("V05 supports one initial and at most four added tiles.");
    }

    private static void WriteDay(StringBuilder trace, int day,
        SimulationController simulation, SimulationEnvironmentController environment,
        MapGridManager grid, BioenergyAccount account)
    {
        var state = new StringBuilder();
        var tiles = new List<MapTileInstance>(grid.GetPlacedTiles());
        var tileStates = new List<TileState>();
        tiles.Sort((a, b) =>
        {
            int byX = a.Coordinate.x.CompareTo(b.Coordinate.x);
            return byX != 0 ? byX : a.Coordinate.y.CompareTo(b.Coordinate.y);
        });
        foreach (MapTileInstance tile in tiles)
        {
            if (!environment.TryRead(tile.Coordinate, out EnvironmentReadSnapshot read))
                throw new InvalidOperationException("Missing environment tile " + tile.Coordinate);
            EnvironmentSnapshot value = read.Environment;
            state.Append(tile.Coordinate.x).Append(',').Append(tile.Coordinate.y)
                .Append(':').Append(tile.HeightLevel).Append(',')
                .Append(value.Temperature.Value.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(value.Temperature.Target.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(value.Temperature.Phase)
                .Append(',').Append(value.Temperature.RemainingDays)
                .Append(',').Append(value.Humidity.Value.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(value.Humidity.Target.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(value.Humidity.Phase)
                .Append(',').Append(value.Humidity.RemainingDays)
                .Append(',').Append(value.Recovery.Value.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(value.Recovery.Target.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(value.Recovery.Phase)
                .Append(',').Append(value.Recovery.RemainingDays)
                .Append(';');
            BlockInfo block = tile.Block;
            var populations = new List<PopulationState>();
            if (block.community != null)
                foreach (PopulationData population in block.community)
                    if (population != null && population.speciesAmount > 0)
                        populations.Add(new PopulationState
                        {
                            id = population.PopulationId,
                            species = population.species != null
                                ? population.species.SpeciesName : population.lineageName,
                            count = population.speciesAmount,
                            fitT = population.fitTemperature,
                            fitH = population.fitHumidity,
                            movement = population.movementAbility,
                            size = population.size,
                            fertility = population.fertility,
                            births = population.birthsToday,
                            deaths = population.deathsToday,
                            predationDeaths = population.predationDeathsToday,
                            body = population.bodyBiomass,
                            reserve = population.energyReserve,
                            carryingCapacity = population.carryingCapacity,
                            environmentalK = population.environmentalCarryingCapacity,
                            foodK = population.trophicLevel == 0
                                ? simulation.ReadFoodCarryingCapacity(block, population)
                                : -1f,
                            allocatedFood = population.allocatedBiomass,
                            adultNeedPerHead = population.energyNeed,
                            energySatisfaction = population.energySatisfactionToday,
                            huntingEnergyToday = population.huntingEnergyToday,
                            birthRemainder = population.birthRemainder,
                            deathRemainder = population.deathRemainder,
                            adultDemand = population.adultDemandToday,
                            adultPaid = population.adultPaidToday,
                            birthOpportunity = population.birthOpportunityToday,
                            birthBudget = population.birthBudgetToday,
                            newbornCost = population.newbornCostToday,
                            unusedBirthFood = population.unusedBirthFoodToday,
                            populationBeforePredation = population.populationBeforePredation,
                            endangeredDays = population.endangeredDays
                        });
            populations.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            tileStates.Add(new TileState
            {
                x = tile.Coordinate.x, y = tile.Coordinate.y,
                elevation = tile.HeightLevel,
                stock = block.plantBiomass, recovery = block.habitatRecovery,
                detritus = block.detritusBiomass,
                plantGrowth = block.plantGrowthToday,
                consumedPlants = block.consumedBiomassToday,
                temperature = (float)value.Temperature.Value,
                humidity = (float)value.Humidity.Value,
                temperaturePhase = value.Temperature.Phase.ToString(),
                humidityPhase = value.Humidity.Phase.ToString(),
                temperatureRemainingDays = value.Temperature.RemainingDays,
                humidityRemainingDays = value.Humidity.RemainingDays,
                populations = populations.ToArray()
            });
        }
        using (SHA256 sha = SHA256.Create())
        {
            byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(state.ToString()));
            var hash = new StringBuilder(digest.Length * 2);
            foreach (byte part in digest) hash.Append(part.ToString("x2"));
            trace.AppendLine(JsonUtility.ToJson(new DayRecord
            {
                day = day, modelHash = simulation.CanonicalModelHash(),
                environmentHash = hash.ToString(), balance = account.Balance,
                totalMaterial = TotalMaterial(grid),
                plantGrowthToday = PlantGrowth(grid),
                accountedBiomassSinkToday = AccountedBiomassSink(grid),
                tiles = tileStates.ToArray()
            }));
        }
    }

    private static double TotalMaterial(MapGridManager grid)
    {
        double total = 0d;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            BlockInfo block = tile.Block;
            total += block.plantBiomass + block.algaeBiomass +
                block.detritusBiomass;
            if (block.community == null) continue;
            foreach (PopulationData population in block.community)
                if (population != null)
                    total += population.bodyBiomass + population.energyReserve;
        }
        return total;
    }

    private static float PlantGrowth(MapGridManager grid)
    {
        float growth = 0f;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
            growth += tile.Block.plantGrowthToday;
        return growth;
    }

    private static float AccountedBiomassSink(MapGridManager grid)
    {
        float total = 0f;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
            total += tile.Block.accountedBiomassSinkToday;
        return total;
    }

    private static void WriteDiagnostics(StringBuilder trace, int day,
        SimulationController simulation, MapGridManager grid)
    {
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            trace.AppendLine("plantAllocationAudit day=" + day + " tile=" +
                tile.Coordinate.x + "," + tile.Coordinate.y +
                " reverseOrderMaxAbsDifference=" +
                simulation.ReadPlantAllocationOrderDifference(tile.Block)
                    .ToString("R", CultureInfo.InvariantCulture));
            foreach (PopulationData population in tile.Block.community)
                if (population != null && population.speciesAmount > 0)
                    trace.AppendLine("diagnostic=" + JsonUtility.ToJson(
                        new EcologyDiagnostic
                        {
                            day = day, x = tile.Coordinate.x, y = tile.Coordinate.y,
                            species = population.species != null
                                ? population.species.SpeciesName : population.lineageName,
                            foodK = population.trophicLevel == 0
                                ? simulation.ReadFoodCarryingCapacity(tile.Block, population)
                                : -1f,
                            equilibrium = simulation.ReadLocalEquilibriumEstimate(
                                tile.Block, population)
                        }));
        }
    }

    private static string HashFiles(List<string> files)
    {
        files.Sort(StringComparer.Ordinal);
        using (SHA256 sha = SHA256.Create())
        {
            var bytes = new List<byte>();
            foreach (string file in files)
            {
                if (!File.Exists(file))
                    throw new InvalidOperationException("Replay binding is missing: " + file);
                bytes.AddRange(Encoding.UTF8.GetBytes(file));
                bytes.Add(0);
                bytes.AddRange(File.ReadAllBytes(file));
                bytes.Add(0);
            }
            byte[] digest = sha.ComputeHash(bytes.ToArray());
            var text = new StringBuilder(digest.Length * 2);
            foreach (byte part in digest) text.Append(part.ToString("x2"));
            return text.ToString();
        }
    }

    private static void Finish(Exception error)
    {
        complete = true;
        SessionState.SetBool(Pending, false);
        EditorApplication.update -= WaitForScene;
        if (error != null) Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
