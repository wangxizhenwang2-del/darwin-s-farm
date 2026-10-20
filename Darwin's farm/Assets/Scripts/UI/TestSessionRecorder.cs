using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

// Records only while test mode is enabled. Unity objects are read on the main
// thread; compression and clipboard work happen only when the player exports.
public sealed class TestSessionRecorder : MonoBehaviour
{
    private sealed class Session
    {
        public int schema = 1;
        public string gameVersion, unityVersion, scene, modelVersion;
        public int seed, startDay, endDay;
        public string initialSave;
        public bool truncated;
        public List<Entry> entries = new List<Entry>();
    }
    private sealed class Entry
    {
        public string kind;
        public int day, x, y;
        public string source, operation, detail, modelHash;
        public long? balance;
        public List<State> changed;
        public List<string> removed;
    }
    private sealed class State
    {
        public string key, species, speciesId, niche;
        public int? count, births, deaths, fitT, fitH, size, movement, fertility;
        public float? fitness, foodSatisfaction, carryingCapacity;
        public int? temperature, humidity, recovery;
        public float? plants, algae;
    }

    private const int MaxEntries = 50000;
    private Session session;
    private SimulationTime clock;
    private SimulationController simulation;
    private MapGridManager grid;
    private SimulationInterventionController interventions;
    private EnvironmentTechnologyController technology;
    private PlayerEconomyController economy;
    private BioenergyAccount account;
    private readonly Dictionary<BlockInfo, Vector2Int> coordinates = new Dictionary<BlockInfo, Vector2Int>();
    private readonly Dictionary<string, State> previous = new Dictionary<string, State>();
    private readonly Dictionary<Vector2Int, string> previousMap = new Dictionary<Vector2Int, string>();
    private long previousBalance;
    public bool IsRecording => session != null;
    public bool IsTruncated => session != null && session.truncated;
    public int EntryCount => session != null ? session.entries.Count : 0;

    public bool Begin(out string error)
    {
        Stop();
        error = null;
        try
        {
            clock = FindFirstObjectByType<SimulationTime>();
            simulation = FindFirstObjectByType<SimulationController>();
            grid = FindFirstObjectByType<MapGridManager>();
            interventions = FindFirstObjectByType<SimulationInterventionController>();
            technology = FindFirstObjectByType<EnvironmentTechnologyController>();
            economy = FindFirstObjectByType<PlayerEconomyController>();
            account = FindFirstObjectByType<BioenergyAccount>();
            if (clock == null || simulation == null || grid == null)
                throw new InvalidOperationException("地图或模拟器尚未准备好。");
            string initial = GameSaveService.CaptureShareSnapshot();
            session = new Session {
                gameVersion = Application.version,
                unityVersion = Application.unityVersion,
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                modelVersion = SimulationController.ModelRandomVersion,
                seed = simulation.SimulationSeed,
                startDay = clock.currentDay,
                endDay = clock.currentDay,
                initialSave = initial
            };
            RebuildCoordinates();
            CaptureMap(false);
            CaptureDay(true);
            simulation.OnDaySimulated += OnDay;
            grid.TilesChanged += OnTilesChanged;
            if (interventions != null) interventions.OperationCommitted += OnIntervention;
            if (technology != null) technology.OperationCommitted += OnTechnology;
            if (economy != null) economy.OperationCommitted += OnEconomy;
            simulation.OnMigrationResolved += OnTransition;
            simulation.OnNicheConversionResolved += OnTransition;
            simulation.OnPresetEvolutionResolved += OnTransition;
            simulation.OnEcologyEvent += OnEcology;
            Application.logMessageReceived += OnLog;
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        if (simulation != null) simulation.OnDaySimulated -= OnDay;
        if (grid != null) grid.TilesChanged -= OnTilesChanged;
        if (interventions != null) interventions.OperationCommitted -= OnIntervention;
        if (technology != null) technology.OperationCommitted -= OnTechnology;
        if (economy != null) economy.OperationCommitted -= OnEconomy;
        if (simulation != null)
        {
            simulation.OnMigrationResolved -= OnTransition;
            simulation.OnNicheConversionResolved -= OnTransition;
            simulation.OnPresetEvolutionResolved -= OnTransition;
            simulation.OnEcologyEvent -= OnEcology;
        }
        Application.logMessageReceived -= OnLog;
        session = null;
        previous.Clear();
        previousMap.Clear();
        previousBalance = 0;
        coordinates.Clear();
    }

    private void OnDestroy() => Stop();

    public Task<string> CreateCodeAsync()
    {
        if (session == null) throw new InvalidOperationException("请先在游戏中开启测试模式。");
        // Include the latest state if the player exports between simulation days.
        CaptureDay(false);
        // Entry objects are immutable after insertion. Copy the list before
        // serializing it off the main thread while the settings menu is paused.
        var captured = new Session {
            schema = session.schema, gameVersion = session.gameVersion,
            unityVersion = session.unityVersion, scene = session.scene,
            modelVersion = session.modelVersion, seed = session.seed,
            startDay = session.startDay, endDay = session.endDay,
            initialSave = session.initialSave, truncated = session.truncated,
            entries = new List<Entry>(session.entries)
        };
        return Task.Run(() => {
            string json = JsonConvert.SerializeObject(captured, Formatting.None,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            return TestShareCodeCodec.Encode(json);
        });
    }

    private void Add(Entry entry)
    {
        if (session == null || session.truncated) return;
        if (session.entries.Count >= MaxEntries) { session.truncated = true; return; }
        session.entries.Add(entry);
        session.endDay = Math.Max(session.endDay, entry.day);
    }

    private void OnDay(int day) => CaptureDay(false);
    private void OnTilesChanged()
    {
        if (session == null || session.truncated) return;
        RebuildCoordinates();
        CaptureMap(true);
    }
    private void OnIntervention(Vector2Int point, string detail) => Action("intervention", point, detail);
    private void OnTechnology(Vector2Int point, string detail) => Action("technology", point, detail);
    private void OnEconomy(Vector2Int point, string detail) => Action("economy", point, detail);
    public void RecordTestDifficulty(float value) => Add(new Entry {
        kind = "action", day = clock.currentDay, source = "settings",
        operation = "test_difficulty",
        detail = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
    });
    public void RecordMovementDifficulty(float value) => Add(new Entry {
        kind = "action", day = clock.currentDay, source = "settings",
        operation = "movement_difficulty",
        detail = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
    });
    private void Action(string source, Vector2Int point, string detail) => Add(new Entry {
        kind = "action", day = clock.currentDay, source = source,
        operation = detail != null && detail.StartsWith("rejected:", StringComparison.Ordinal) ? "rejected" : "commit",
        x = point.x, y = point.y, detail = detail
    });
    private void OnTransition(PopulationTransitionResult result)
    {
        Vector2Int from = Point(result.SourceBlock), to = Point(result.TargetBlock);
        Add(new Entry { kind = "transition", day = result.Day, x = from.x, y = from.y,
            source = result.Kind.ToString(), operation = result.Outcome.ToString(),
            detail = "to=" + to.x + "," + to.y + ";source=" + result.SourcePopulationId +
                ";result=" + result.ResultPopulationId + ";species=" + result.ResultSpeciesId +
                ";amount=" + result.Amount });
    }
    private void OnEcology(int day, BlockInfo block, PopulationData population, string kind, string detail)
    {
        Vector2Int point = Point(block);
        Add(new Entry { kind = "ecology", day = day, x = point.x, y = point.y,
            source = population != null ? population.PopulationId : null,
            operation = kind, detail = detail });
    }
    private void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        Add(new Entry { kind = "error", day = clock.currentDay, operation = type.ToString(),
            detail = message + (type == LogType.Exception ? " | " + stack : "") });
    }
    private Vector2Int Point(BlockInfo block) => block != null && coordinates.TryGetValue(block, out var point)
        ? point : new Vector2Int(int.MinValue, int.MinValue);
    private void RebuildCoordinates()
    {
        coordinates.Clear();
        if (grid == null) return;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
            if (tile != null && tile.Block != null) coordinates[tile.Block] = tile.Coordinate;
    }
    private void CaptureMap(bool record)
    {
        if (grid == null) return;
        var next = new Dictionary<Vector2Int, string>();
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
            if (tile != null)
                next[tile.Coordinate] = (tile.Definition != null ? tile.Definition.name : "") +
                    ";height=" + tile.HeightLevel + ";rotation=" + tile.RotationSteps;
        if (record && session != null)
        {
            foreach (var pair in next.OrderBy(pair => pair.Key.x).ThenBy(pair => pair.Key.y))
                if (!previousMap.TryGetValue(pair.Key, out string old) || old != pair.Value)
                    Add(new Entry { kind = "map", day = clock.currentDay,
                        x = pair.Key.x, y = pair.Key.y,
                        operation = old == null ? "place" : "change", detail = pair.Value });
            foreach (var pair in previousMap.OrderBy(pair => pair.Key.x).ThenBy(pair => pair.Key.y))
                if (!next.ContainsKey(pair.Key))
                    Add(new Entry { kind = "map", day = clock.currentDay,
                        x = pair.Key.x, y = pair.Key.y, operation = "remove", detail = pair.Value });
        }
        previousMap.Clear();
        foreach (var pair in next) previousMap.Add(pair.Key, pair.Value);
    }

    private void CaptureDay(bool initial)
    {
        if (session == null || session.truncated || grid == null || clock == null) return;
        var changed = new List<State>();
        var seen = new HashSet<string>();
        foreach (MapTileInstance tile in grid.GetPlacedTiles().Where(t => t != null)
                     .OrderBy(t => t.Coordinate.x).ThenBy(t => t.Coordinate.y))
        {
            if (tile == null || tile.Block == null) continue;
            BlockInfo block = tile.Block;
            string tileKey = "t:" + tile.Coordinate.x + "," + tile.Coordinate.y;
            Track(new State { key = tileKey, temperature = block.temperature,
                humidity = block.humidity, recovery = block.habitatRecovery,
                plants = block.plantBiomass, algae = block.algaeBiomass }, changed, seen);
            if (block.community == null) continue;
            foreach (PopulationData population in block.community)
            {
                if (population == null) continue;
                Track(new State { key = tileKey + ":" + population.PopulationId,
                    species = population.species != null ? population.species.SpeciesName : population.lineageName,
                    speciesId = population.speciesId, niche = population.ecologicalNiche.ToString(),
                    count = population.speciesAmount, births = population.birthsToday,
                    deaths = population.deathsToday, fitT = population.fitTemperature,
                    fitH = population.fitHumidity, size = population.size,
                    movement = population.movementAbility, fertility = population.fertility,
                    fitness = population.environmentalFitness,
                    foodSatisfaction = population.actualEnergySatisfactionToday,
                    carryingCapacity = population.carryingCapacity }, changed, seen);
            }
        }
        var removed = previous.Keys.Where(key => !seen.Contains(key)).ToList();
        foreach (string key in removed) previous.Remove(key);
        long balance = account != null ? account.Balance : 0;
        if (initial || changed.Count > 0 || removed.Count > 0 || balance != previousBalance ||
            clock.currentDay != session.endDay)
            Add(new Entry { kind = initial ? "initial" : "day", day = clock.currentDay,
                changed = changed, removed = removed, balance = balance,
                modelHash = initial || clock.currentDay % 25 == 0 ? simulation.CanonicalModelHash() : null });
        previousBalance = balance;
    }
    private void Track(State state, List<State> changed, HashSet<string> seen)
    {
        seen.Add(state.key);
        if (!previous.TryGetValue(state.key, out State before) || !Same(before, state))
        { previous[state.key] = state; changed.Add(state); }
    }
    private static bool Same(State a, State b) =>
        a.species == b.species && a.speciesId == b.speciesId && a.niche == b.niche &&
        a.count == b.count && a.births == b.births && a.deaths == b.deaths &&
        a.fitT == b.fitT && a.fitH == b.fitH && a.size == b.size &&
        a.movement == b.movement && a.fertility == b.fertility &&
        a.fitness == b.fitness && a.foodSatisfaction == b.foodSatisfaction &&
        a.carryingCapacity == b.carryingCapacity && a.temperature == b.temperature &&
        a.humidity == b.humidity && a.recovery == b.recovery &&
        a.plants == b.plants && a.algae == b.algae;
}
