using System.Collections.Generic;
using UnityEngine;

// Controller: reads the simulation model and prepares compact, read-only labels.
public sealed class WhiteboxEcologyDebugController : MonoBehaviour
{
    public sealed class TileLabel
    {
        public MapTileInstance tile;
        public string[] lines;
        public PopulationData[] populations;
    }

    [SerializeField] private MapGridManager grid;
    [SerializeField] private SimulationController simulation;
    [SerializeField] private SimulationTime clock;

    private readonly List<TileLabel> labels = new List<TileLabel>();
    private readonly Dictionary<BlockInfo, float> plantsBefore = new Dictionary<BlockInfo, float>();
    private readonly Dictionary<BlockInfo, float> plantDelta = new Dictionary<BlockInfo, float>();
    private readonly Dictionary<PopulationData, int> populationsBefore = new Dictionary<PopulationData, int>();
    private readonly Dictionary<PopulationData, int> populationDelta = new Dictionary<PopulationData, int>();
    private float nextRefresh;

    public IReadOnlyList<TileLabel> Labels => labels;
    public int Day => clock != null ? clock.currentDay : 0;

    private void Awake()
    {
        if (grid == null) grid = GetComponent<MapGridManager>();
        if (simulation == null) simulation = GetComponent<SimulationController>();
        if (clock == null) clock = GetComponent<SimulationTime>();
    }

    private void OnEnable()
    {
        if (simulation != null)
        {
            simulation.OnBeforeDaySimulated += CaptureBeforeDay;
            simulation.OnDaySimulated += CaptureAfterDay;
        }
        if (clock != null) clock.OnDayReset += ResetDeltas;
    }

    private void OnDisable()
    {
        if (simulation != null)
        {
            simulation.OnBeforeDaySimulated -= CaptureBeforeDay;
            simulation.OnDaySimulated -= CaptureAfterDay;
        }
        if (clock != null) clock.OnDayReset -= ResetDeltas;
    }

    private void CaptureBeforeDay(int day)
    {
        plantsBefore.Clear();
        populationsBefore.Clear();
        if (grid == null) return;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            if (tile == null || tile.Block == null) continue;
            BlockInfo block = tile.Block;
            plantsBefore[block] = block.plantBiomass;
            if (block.community == null) continue;
            foreach (PopulationData population in block.community)
                if (population != null) populationsBefore[population] = population.speciesAmount;
        }
    }

    private void CaptureAfterDay(int day)
    {
        plantDelta.Clear();
        populationDelta.Clear();
        if (grid == null) return;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            if (tile == null || tile.Block == null) continue;
            BlockInfo block = tile.Block;
            plantDelta[block] = block.plantBiomass -
                (plantsBefore.TryGetValue(block, out float before) ? before : block.plantBiomass);
            if (block.community == null) continue;
            foreach (PopulationData population in block.community)
                if (population != null)
                    populationDelta[population] = population.speciesAmount -
                        (populationsBefore.TryGetValue(population, out int oldCount) ? oldCount : 0);
        }
        nextRefresh = 0f;
    }

    private void ResetDeltas(int day)
    {
        plantsBefore.Clear(); plantDelta.Clear();
        populationsBefore.Clear(); populationDelta.Clear();
        nextRefresh = 0f;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextRefresh) return;
        // Forecast labels are diagnostic; refreshing once per second avoids
        // repeated food-web projections between simulation days.
        nextRefresh = Time.unscaledTime + 1f;
        labels.Clear();
        if (grid == null) return;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            if (tile == null || tile.Block == null) continue;
            BlockInfo block = tile.Block;
            var lines = new List<string>();
            var members = new List<PopulationData>();
            string terrain = tile.Definition != null ? tile.Definition.displayName : tile.Biome.ToString();
            lines.Add($"{terrain}/{WaterName(block.waterCoverage)}  温{block.temperature} 湿{block.humidity} 海{block.elevation}");
            plantDelta.TryGetValue(block, out float growth);
            lines.Add($"植物{block.plantBiomass:F0}  Δ{Signed(growth)}");
            if (block.community != null)
                foreach (PopulationData population in block.community)
                {
                    if (population == null || population.speciesAmount <= 0) continue;
                    members.Add(population);
                    string name = population.species != null ? population.species.SpeciesName :
                        string.IsNullOrEmpty(population.lineageName) ? "未命名" : population.lineageName;
                    populationDelta.TryGetValue(population, out int change);
                    lines.Add($"{name} {population.speciesAmount}  Δ{Signed(change)}  L{population.trophicLevel}  K{population.carryingCapacity:F0}");
                    lines.Add($"适温{population.fitTemperature} 适湿{population.fitHumidity}  体{population.size} 动{population.movementAbility} 繁{population.fertility}");
                    string mutation = simulation != null ? simulation.GetExpectedEvolutionDirection(block, population) : "";
                    int colon = mutation.IndexOf('：');
                    if (colon >= 0) mutation = mutation.Substring(colon + 1);
                    int explanation = mutation.IndexOf('（');
                    if (explanation >= 0) mutation = mutation.Substring(0, explanation);
                    if (mutation == "暂无线性优势") mutation = "暂无";
                    else if (mutation == "已关闭") mutation = "关闭";
                    else if (mutation == "待首日计算") mutation = "待计算";
                    else if (mutation == "无存活个体") mutation = "暂无";
                    string final = "变异：" + mutation;
                    SpeciesData target = simulation != null ? simulation.GetLikelyEvolutionTarget(block, population, Day) : null;
                    if (target != null) final += "  进化：" + target.SpeciesName;
                    lines.Add(final);
                }
            labels.Add(new TileLabel
            {
                tile = tile, lines = lines.ToArray(), populations = members.ToArray()
            });
        }
    }

    private static string Signed(float value) => value >= 0f ? "+" + value.ToString("F0") : value.ToString("F0");
    private static string Signed(int value) => value >= 0 ? "+" + value : value.ToString();
    private static string WaterName(WaterCoverage water) => water == WaterCoverage.Land ? "陆" :
        water == WaterCoverage.Lake ? "湖" : water == WaterCoverage.Sea ? "海" : "河";
}
