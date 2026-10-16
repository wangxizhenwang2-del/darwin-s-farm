using System;
using System.Collections.Generic;
using System.IO;
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
        public Tile[] tiles;
        public Action[] actions;
    }

    [Serializable] private sealed class Tile
    {
        public int x, y;
        public string preset;
        public Population[] populations;
    }

    [Serializable] private sealed class Population
    {
        public string species;
        public int count;
    }

    [Serializable] private sealed class Action
    {
        public int day, sequence, x, y, tier;
        public string kind;
        public int temperatureOffset, humidityOffset;
    }

    [Serializable] private sealed class DayRecord
    {
        public int day;
        public string modelHash;
        public string environmentHash;
        public long balance;
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
        EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity");
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
        if (EditorApplication.timeSinceStartup - startedAt > 120)
        { Finish(new Exception("Play scene initialization timed out.")); return; }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        var clock = UnityEngine.Object.FindFirstObjectByType<SimulationTime>();
        var environment = UnityEngine.Object.FindFirstObjectByType<SimulationEnvironmentController>();
        if (clock == null || environment == null ||
            !environment.TryRead(Vector2Int.zero, out _)) return;
        clock.Pause();
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
        var account = environment.GetComponent<BioenergyAccount>();
        if (grid == null || bridge == null || simulation == null ||
            player == null || account == null)
            throw new InvalidOperationException("Live gameplay components are missing.");
        account.CostsEnabled = !scenario.disableCosts;
        foreach (Tile tile in scenario.tiles)
        {
            var coordinate = new Vector2Int(tile.x, tile.y);
            MapTileDefinition preset = AssetDatabase.LoadAssetAtPath<MapTileDefinition>(tile.preset);
            if (preset == null || !grid.TryPlaceTile(coordinate, preset) ||
                !bridge.TryGetBlock(coordinate, out BlockInfo block))
                throw new InvalidOperationException("Cannot place preset at " + coordinate);
            block.community.Clear();
            if (tile.populations == null) continue;
            foreach (Population entry in tile.populations)
            {
                SpeciesData species = AssetDatabase.LoadAssetAtPath<SpeciesData>(entry.species);
                if (species == null || entry.count < 1)
                    throw new InvalidOperationException("Invalid starting population " + entry.species);
                block.community.Add(new PopulationData
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
                });
            }
        }
        simulation.RefreshSpeciesStatuses();
        simulation.ConfigureDeterministicRun(scenario.seed);
        var actions = new List<Action>(scenario.actions ?? Array.Empty<Action>());
        actions.Sort((a, b) =>
        {
            int byDay = a.day.CompareTo(b.day);
            return byDay != 0 ? byDay : a.sequence.CompareTo(b.sequence);
        });
        for (int i = 1; i < actions.Count; i++)
            if (actions[i].day == actions[i - 1].day &&
                actions[i].sequence == actions[i - 1].sequence)
                throw new InvalidOperationException("Duplicate action sequence.");
        foreach (Action action in actions)
            if (action.day < 0 || action.day > scenario.endDay)
                throw new InvalidOperationException("Action day outside run.");

        var trace = new StringBuilder();
        trace.AppendLine("scenario=" + scenario.scenarioId + " seed=" + scenario.seed +
            " random=" + SimulationController.ModelRandomVersion +
            " unity=" + Application.unityVersion +
            " costs=" + account.CostsEnabled +
            " phase=after-day-step");
        WriteDay(trace, clock.currentDay, simulation, environment, grid, account);
        int next = 0;
        for (int day = 0; day <= scenario.endDay; day++)
        {
            while (next < actions.Count && actions[next].day == day)
            {
                Action action = actions[next++];
                if (!Enum.TryParse(action.kind, out EnvironmentTechnologyKind kind) ||
                    !Enum.IsDefined(typeof(EnvironmentTechnologyKind), kind))
                    throw new InvalidOperationException("Unknown player action " + action.kind);
                var choice = new EnvironmentTechnologyChoice(kind, action.tier,
                    action.temperatureOffset == 0 ? 20 : action.temperatureOffset,
                    action.humidityOffset == 0 ? 20 : action.humidityOffset);
                if (!player.TryDeploy(new Vector2Int(action.x, action.y),
                    choice, out EnvironmentTechnologyPreview preview))
                    throw new InvalidOperationException("Action rejected on Day " + day +
                        " at (" + action.x + "," + action.y + "): " + preview.Error);
                trace.AppendLine("action day=" + day + " sequence=" + action.sequence +
                    " tile=" + action.x + "," + action.y + " kind=" + kind +
                    " tier=" + action.tier + " cost=" + preview.Cost);
                WriteDay(trace, day, simulation, environment, grid, account);
            }
            if (day == scenario.endDay) break;
            clock.NextDay();
            if (clock.currentDay != day + 1)
                throw new InvalidOperationException("Clock skipped a day.");
            WriteDay(trace, clock.currentDay, simulation, environment, grid, account);
        }
        File.WriteAllText(output, trace.ToString(), Encoding.UTF8);
        Debug.Log("V048_REPLAY_OK " + scenario.scenarioId + " " + output);
    }

    private static void WriteDay(StringBuilder trace, int day,
        SimulationController simulation, SimulationEnvironmentController environment,
        MapGridManager grid, BioenergyAccount account)
    {
        var state = new StringBuilder();
        var tiles = new List<MapTileInstance>(grid.GetPlacedTiles());
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
                .Append(':').Append(value.Temperature.Value)
                .Append(',').Append(value.Temperature.Target)
                .Append(',').Append(value.Temperature.Phase)
                .Append(',').Append(value.Temperature.RemainingDays)
                .Append(',').Append(value.Humidity.Value)
                .Append(',').Append(value.Humidity.Target)
                .Append(',').Append(value.Humidity.Phase)
                .Append(',').Append(value.Humidity.RemainingDays)
                .Append(',').Append(value.Recovery.Value)
                .Append(',').Append(value.Recovery.Target)
                .Append(',').Append(value.Recovery.Phase)
                .Append(',').Append(value.Recovery.RemainingDays)
                .Append(';');
        }
        using (SHA256 sha = SHA256.Create())
        {
            byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(state.ToString()));
            var hash = new StringBuilder(digest.Length * 2);
            foreach (byte part in digest) hash.Append(part.ToString("x2"));
            trace.AppendLine(JsonUtility.ToJson(new DayRecord
            {
                day = day, modelHash = simulation.CanonicalModelHash(),
                environmentHash = hash.ToString(), balance = account.Balance
            }));
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
