using System.Collections.Generic;
using DarwinFarm.Environment;
using UnityEngine;

// Runtime, read-only daily snapshots. No Editor dependency and no simulation writes.
public sealed class TileTrendHistory : MonoBehaviour
{
    [SerializeField, Range(2, 365)] private int retainedDays = 120;
    private MapGridManager grid;
    private SimulationController simulation;
    private SimulationTime clock;
    private SimulationEnvironmentController environment;
    private bool subscribed, simulating;
    private readonly Dictionary<MapTileInstance, TileRecord> records = new Dictionary<MapTileInstance, TileRecord>();
    private readonly HashSet<MapTileInstance> present = new HashSet<MapTileInstance>();
    private readonly List<MapTileInstance> removed = new List<MapTileInstance>();
    public int RetainedDays => retainedDays;
    public int TileCount => records.Count;

    public sealed class Series
    {
        public readonly string Id;
        public string Name;
        public readonly Color Color;
        internal readonly float[] values;
        internal int lastAliveDay;
        internal Series(string id, int capacity, int index)
        {
            Id = id;
            Color = UnityEngine.Color.HSVToRGB((index * .618034f + .53f) % 1f, .7f, .72f);
            values = new float[capacity];
            for (int i = 0; i < capacity; i++) values[i] = float.NaN;
        }
    }

    public sealed class TileRecord
    {
        public readonly BlockInfo Source;
        private readonly int[] days;
        private readonly float[] plants;
        private readonly float[] temperatures, humidities, temperatureDefaults, humidityDefaults;
        private readonly Dictionary<string, Series> byId = new Dictionary<string, Series>();
        private readonly List<Series> series = new List<Series>();
        private int first, count, colorIndex;
        public int Count => count;
        public int Version { get; private set; }
        public IReadOnlyList<Series> Populations => series;
        public int Capacity => days.Length;
        public TileRecord(BlockInfo source, int capacity)
        {
            Source = source;
            days = new int[Mathf.Max(2, capacity)]; plants = new float[days.Length];
            temperatures = new float[days.Length]; humidities = new float[days.Length];
            temperatureDefaults = new float[days.Length]; humidityDefaults = new float[days.Length];
        }
        private int Slot(int index) => (first + index) % days.Length;
        public int Day(int index) => days[Slot(index)];
        public float Plants(int index) => plants[Slot(index)];
        public float Temperature(int index) => temperatures[Slot(index)];
        public float Humidity(int index) => humidities[Slot(index)];
        public float TemperatureDefault(int index) => temperatureDefaults[Slot(index)];
        public float HumidityDefault(int index) => humidityDefaults[Slot(index)];
        public float Amount(Series population, int index) => population.values[Slot(index)];

        public void Capture(int day, EnvironmentSnapshot climate = null)
        {
            if (Source == null) return;
            if (count > 0 && day < Day(count - 1))
            {
                first = count = colorIndex = 0; byId.Clear(); series.Clear();
            }
            // Keep an actual day window, including when the clock skips days.
            while (count > 0 && Day(0) < day - Capacity + 1)
            {
                first = (first + 1) % Capacity; count--;
            }
            int slot;
            if (count > 0 && Day(count - 1) == day) slot = Slot(count - 1);
            else
            {
                if (count == Capacity) { first = (first + 1) % Capacity; count--; }
                slot = Slot(count++);
            }
            days[slot] = day;
            plants[slot] = Source.plantBiomass;
            temperatures[slot] = climate != null ? (float)climate.Temperature.Value : float.NaN;
            humidities[slot] = climate != null ? (float)climate.Humidity.Value : float.NaN;
            temperatureDefaults[slot] = climate != null ? (float)climate.Defaults.Temperature : float.NaN;
            humidityDefaults[slot] = climate != null ? (float)climate.Defaults.Humidity : float.NaN;
            foreach (Series item in series) item.values[slot] = float.NaN;
            if (Source.community != null)
                foreach (PopulationData population in Source.community)
                {
                    if (population == null) continue;
                    string id = population.PopulationId;
                    if (!byId.TryGetValue(id, out Series item))
                    {
                        if (population.speciesAmount <= 0) continue;
                        item = new Series(id, Capacity, colorIndex++);
                        byId.Add(id, item); series.Add(item);
                    }
                    item.Name = population.species != null ? population.species.SpeciesName : population.lineageName;
                    item.values[slot] = Mathf.Max(0, population.speciesAmount);
                    if (population.speciesAmount > 0) item.lastAliveDay = day;
                }
            for (int i = series.Count - 1; i >= 0; i--)
                if (series[i].lastAliveDay < Day(0))
                {
                    byId.Remove(series[i].Id); series.RemoveAt(i);
                }
            Version++;
        }
    }

    public void Initialize(MapGridManager map, SimulationController controller, SimulationTime time)
    {
        Unsubscribe();
        if (grid != map || simulation != controller) records.Clear();
        grid = map; simulation = controller; clock = time;
        environment = controller != null ? controller.GetComponent<SimulationEnvironmentController>() : null;
        if (environment == null) environment = FindFirstObjectByType<SimulationEnvironmentController>();
        Subscribe(); CaptureAll(clock != null ? clock.currentDay : 0);
    }
    private void OnEnable() => Subscribe();
    private void OnDisable() => Unsubscribe();
    private void Subscribe()
    {
        if (subscribed || grid == null || simulation == null) return;
        grid.TilesChanged += MapChanged;
        simulation.OnBeforeDaySimulated += BeforeDay;
        simulation.OnDaySimulated += AfterDay;
        simulation.OnBlockCommunityChanged += BlockChanged;
        simulation.OnBlockEnvironmentChanged += BlockChanged;
        if (clock != null) clock.OnDayReset += ResetHistory;
        subscribed = true;
    }
    private void Unsubscribe()
    {
        if (!subscribed) return;
        if (grid != null) grid.TilesChanged -= MapChanged;
        if (simulation != null)
        {
            simulation.OnBeforeDaySimulated -= BeforeDay;
            simulation.OnDaySimulated -= AfterDay;
            simulation.OnBlockCommunityChanged -= BlockChanged;
            simulation.OnBlockEnvironmentChanged -= BlockChanged;
        }
        if (clock != null) clock.OnDayReset -= ResetHistory;
        subscribed = false; simulating = false;
    }
    private void BeforeDay(int day) => simulating = true;
    private void AfterDay(int day) { simulating = false; CaptureAll(day); }
    private void ResetHistory(int day) { records.Clear(); simulating = false; CaptureAll(day); }
    private void MapChanged() { if (!simulating) CaptureAll(clock != null ? clock.currentDay : 0); }
    private void BlockChanged(BlockInfo block)
    {
        if (simulating || grid == null) return;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
            if (tile != null && tile.Block == block)
            {
                Record(tile, clock != null ? clock.currentDay : 0); break;
            }
    }
    private void Record(MapTileInstance tile, int day)
    {
        if (tile.Block == null) return;
        if (!records.TryGetValue(tile, out TileRecord record) || record.Source != tile.Block)
        {
            record = new TileRecord(tile.Block, retainedDays); records[tile] = record;
        }
        EnvironmentSnapshot climate = null;
        if (environment != null && environment.TryRead(tile.Coordinate, out var read))
            climate = read.Environment;
        record.Capture(day, climate);
    }
    private void CaptureAll(int day)
    {
        if (grid == null) return;
        present.Clear();
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            if (tile == null) continue;
            present.Add(tile); Record(tile, day);
        }
        removed.Clear();
        foreach (MapTileInstance tile in records.Keys)
            if (!present.Contains(tile)) removed.Add(tile);
        foreach (MapTileInstance tile in removed) records.Remove(tile);
    }
    public bool TryGet(MapTileInstance tile, out TileRecord record)
    {
        record = null;
        return tile != null && records.TryGetValue(tile, out record);
    }
}
