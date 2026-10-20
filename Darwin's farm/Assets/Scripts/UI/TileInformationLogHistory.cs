using System.Collections.Generic;
using UnityEngine;

// Read-only snapshots for the tile journal. Values are captured on simulation days,
// rather than reconstructing old traits from the current population state.
public sealed class TileInformationLogHistory : MonoBehaviour
{
    public sealed class Entry
    {
        public int Day;
        public string PopulationId;
        public string Name;
        public int Count, Change, Fertility, Size, Movement, Temperature, Humidity;
        public string Mutation;
    }

    private struct Previous
    {
        public int Count, Fertility, Size, Movement, Temperature, Humidity;
    }

    private readonly Dictionary<BlockInfo, List<Entry>> entries = new Dictionary<BlockInfo, List<Entry>>();
    private readonly Dictionary<BlockInfo, Dictionary<string, Previous>> previous =
        new Dictionary<BlockInfo, Dictionary<string, Previous>>();
    private MapGridManager grid;
    private SimulationController simulation;
    private SimulationTime clock;

    public void Initialize(MapGridManager map, SimulationController controller, SimulationTime time)
    {
        Unsubscribe();
        grid = map; simulation = controller; clock = time;
        entries.Clear(); previous.Clear();
        Capture(clock != null ? clock.currentDay : 0);
        Subscribe();
    }

    private void OnEnable() => Subscribe();
    private void OnDisable() => Unsubscribe();
    private void Subscribe()
    {
        if (simulation != null)
        {
            simulation.OnDaySimulated -= Capture;
            simulation.OnDaySimulated += Capture;
        }
        if (clock != null)
        {
            clock.OnDayReset -= Reset;
            clock.OnDayReset += Reset;
        }
    }
    private void Unsubscribe()
    {
        if (simulation != null) simulation.OnDaySimulated -= Capture;
        if (clock != null) clock.OnDayReset -= Reset;
    }
    private void Reset(int day)
    {
        entries.Clear(); previous.Clear(); Capture(day);
    }

    private void Capture(int day)
    {
        if (grid == null) return;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            BlockInfo block = tile != null ? tile.Block : null;
            if (block == null || block.community == null) continue;
            if (!entries.TryGetValue(block, out List<Entry> journal))
                entries.Add(block, journal = new List<Entry>());
            if (!previous.TryGetValue(block, out Dictionary<string, Previous> lastByPopulation))
                previous.Add(block, lastByPopulation = new Dictionary<string, Previous>());
            foreach (PopulationData population in block.community)
            {
                if (population == null || population.speciesAmount <= 0) continue;
                string id = population.PopulationId;
                lastByPopulation.TryGetValue(id, out Previous before);
                int count = population.speciesAmount;
                var current = new Previous {
                    Count = count, Fertility = population.fertility, Size = population.size,
                    Movement = population.movementAbility, Temperature = population.fitTemperature,
                    Humidity = population.fitHumidity
                };
                string mutation = Difference(before, current);
                journal.Insert(0, new Entry {
                    Day = day, PopulationId = id,
                    Name = population.species != null ? population.species.SpeciesName : population.lineageName,
                    Count = count, Change = before.Count == 0 ? 0 : count - before.Count,
                    Fertility = current.Fertility, Size = current.Size, Movement = current.Movement,
                    Temperature = current.Temperature, Humidity = current.Humidity,
                    Mutation = mutation
                });
                lastByPopulation[id] = current;
            }
            if (journal.Count > 240) journal.RemoveRange(240, journal.Count - 240);
        }
    }

    private static string Difference(Previous a, Previous b)
    {
        if (a.Count == 0) return "";
        if (a.Movement != b.Movement) return "运动 " + Signed(b.Movement - a.Movement);
        if (a.Size != b.Size) return "体型 " + Signed(b.Size - a.Size);
        if (a.Fertility != b.Fertility) return "繁殖 " + Signed(b.Fertility - a.Fertility);
        if (a.Temperature != b.Temperature) return "适温 " + Signed(b.Temperature - a.Temperature);
        if (a.Humidity != b.Humidity) return "适湿 " + Signed(b.Humidity - a.Humidity);
        return "";
    }
    private static string Signed(int value) => value > 0 ? "▲+" + value : "▼" + value;
    public IReadOnlyList<Entry> For(BlockInfo block) =>
        block != null && entries.TryGetValue(block, out List<Entry> journal) ? journal : null;
}
