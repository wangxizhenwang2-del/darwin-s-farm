using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public partial class PopulationSimulatorWindow
{
    [Serializable]
    private sealed class ExportHeader
    {
        public string type = "header";
        public int schema = 1;
        public string createdAt;
        public string scene;
        public string unity;
        public string caseId;
        public string tester;
        public string sampling = "initial, after successful operation, and once per simulated day";
    }

    [Serializable]
    private sealed class ExportAction
    {
        public string type = "action";
        public int day;
        public string time;
        public string source;
        public string operation;
        public int x;
        public int y;
        public string detail;
    }

    [Serializable]
    private sealed class ExportTransition
    {
        public string type = "transition";
        public int day;
        public string kind;
        public string outcome;
        public int fromX, fromY, toX, toY;
        public string sourcePopulationId, resultPopulationId, resultSpeciesId, targetSpecies;
        public int amount, sourceBefore, sourceAfter, targetBefore, targetAfter;
    }

    [Serializable]
    private sealed class ExportPopulation
    {
        public string id, species, speciesId, niche;
        public int count, level, fitT, fitH, size, movement, fertility;
        public int births, deaths, predationDeaths, migrationReadyDay;
        public int mutationDays;
        public float k, fitness, foodSatisfaction, energyNeed, allocatedFood, energyReserve;
    }

    [Serializable]
    private sealed class ExportTile
    {
        public int x, y, height, temperature, humidity, recovery;
        public string preset, terrain, water;
        public string temperaturePhase, humidityPhase, recoveryPhase;
        public int temperatureDays, humidityDays, recoveryDays;
        public float temperatureTarget, humidityTarget, recoveryTarget;
        public float plants, plantCapacity, plantGrowth, plantConsumed, algae;
        public int algaeRecovery;
        public long monsoonId;
        public List<ExportPopulation> populations = new List<ExportPopulation>();
    }

    [Serializable]
    private sealed class ExportSnapshot
    {
        public string type = "snapshot";
        public string reason;
        public int day;
        public SimulationTuning tuning;
        public float secondsPerDay;
        public float speed;
        public bool paused;
        public List<ExportTile> tiles = new List<ExportTile>();
    }

    private StreamWriter liveExportWriter;
    [SerializeField] private string liveExportPath;
    [SerializeField] private string liveExportStatus = "尚未开始记录";
    [SerializeField] private string liveExportCase = "Whitebox";
    [SerializeField] private string liveExportTester = "";
    [SerializeField] private string liveExportNote = "";
    [SerializeField] private int liveExportDays, liveExportActions, liveExportTransitions;
    private bool liveMapRefreshQueued;
    private readonly Dictionary<Vector2Int, string> liveExportMap =
        new Dictionary<Vector2Int, string>();
    private EnvironmentTechnologyController liveExportTechnology;

    private void DrawLiveExport()
    {
        EditorGUILayout.LabelField("导出测试数据", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "记录全地图每日状态、成功操作、地图变化、迁徙/分化/进化和错误。只生成一个 JSONL 文件；不采集逐帧鼠标与键盘输入。请在测试前开始记录。",
            MessageType.Info);
        liveExportCase = EditorGUILayout.TextField("测试编号", liveExportCase);
        liveExportTester = EditorGUILayout.TextField("测试者", liveExportTester);
        liveExportNote = EditorGUILayout.TextField("操作/观察备注", liveExportNote);
        EditorGUILayout.LabelField("状态", liveExportStatus);
        EditorGUILayout.LabelField("每日快照", liveExportDays.ToString());
        EditorGUILayout.LabelField("操作", liveExportActions.ToString());
        EditorGUILayout.LabelField("生态事件", liveExportTransitions.ToString());
        if (liveExportWriter == null)
        {
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                if (GUILayout.Button("开始记录（选择单个文件）")) StartLiveExport();
        }
        else
        {
            if (GUILayout.Button("记录备注 / 检查点"))
            {
                WriteExportAction("tester", "checkpoint", liveCoordinate,
                    liveExportNote, false);
                liveExportNote = "";
            }
            if (GUILayout.Button("完成并导出")) StopLiveExport();
        }
        if (!string.IsNullOrEmpty(liveExportPath))
        {
            EditorGUILayout.SelectableLabel(liveExportPath,
                EditorStyles.textField, GUILayout.Height(38f));
            if (GUILayout.Button("在资源管理器中显示文件"))
            {
                liveExportWriter?.Flush();
                EditorUtility.RevealInFinder(liveExportPath);
            }
        }
        EditorGUILayout.LabelField("格式：每行一个 JSON 对象，type 为 header / action / transition / snapshot。",
            EditorStyles.wordWrappedMiniLabel);
    }

    private void StartLiveExport()
    {
        ResolveLiveGame();
        if (!EditorApplication.isPlaying || liveGrid == null || liveSimulation == null ||
            liveTime == null || liveInterventions == null)
        { liveExportStatus = "请先进入有地图与模拟器的 Play 模式"; return; }
        string name = "Darwin_Whitebox_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jsonl";
        string path = EditorUtility.SaveFilePanel("导出测试数据",
            Application.persistentDataPath, name, "jsonl");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            liveExportWriter = new StreamWriter(path, false, new UTF8Encoding(false));
            liveExportPath = path;
            liveExportDays = liveExportActions = liveExportTransitions = 0;
            liveExportMap.Clear();
            WriteExport(new ExportHeader
            {
                createdAt = DateTime.Now.ToString("O"),
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                unity = Application.unityVersion,
                caseId = liveExportCase, tester = liveExportTester
            });
            liveGrid.TilesChanged += OnExportTilesChanged;
            liveSimulation.OnDaySimulated += OnExportDay;
            liveSimulation.OnMigrationResolved += OnExportTransition;
            liveSimulation.OnNicheConversionResolved += OnExportTransition;
            liveSimulation.OnPresetEvolutionResolved += OnExportTransition;
            liveInterventions.OperationCommitted += OnExportIntervention;
            liveTime.OnSpeedChanged += OnExportSpeedChanged;
            liveExportTechnology = liveBridge.GetComponent<EnvironmentTechnologyController>();
            if (liveExportTechnology != null)
                liveExportTechnology.OperationCommitted += OnExportTechnology;
            Application.logMessageReceived += OnExportUnityLog;
            CaptureExportMap();
            WriteExportSnapshot("initial");
            liveExportWriter.Flush();
            liveExportStatus = "记录中";
        }
        catch (Exception exception)
        {
            StopLiveExport();
            liveExportStatus = "无法开始记录：" + exception.Message;
        }
    }

    private void StopLiveExport()
    {
        if (liveGrid != null) liveGrid.TilesChanged -= OnExportTilesChanged;
        if (liveSimulation != null)
        {
            liveSimulation.OnDaySimulated -= OnExportDay;
            liveSimulation.OnMigrationResolved -= OnExportTransition;
            liveSimulation.OnNicheConversionResolved -= OnExportTransition;
            liveSimulation.OnPresetEvolutionResolved -= OnExportTransition;
        }
        if (liveInterventions != null)
            liveInterventions.OperationCommitted -= OnExportIntervention;
        if (liveTime != null) liveTime.OnSpeedChanged -= OnExportSpeedChanged;
        if (liveExportTechnology != null)
            liveExportTechnology.OperationCommitted -= OnExportTechnology;
        liveExportTechnology = null;
        Application.logMessageReceived -= OnExportUnityLog;
        if (liveExportWriter == null) return;
        try { liveExportWriter.Flush(); liveExportWriter.Dispose(); }
        catch (Exception exception) { liveExportStatus = "关闭文件失败：" + exception.Message; }
        finally { liveExportWriter = null; }
        if (!liveExportStatus.StartsWith("关闭文件失败")) liveExportStatus = "已导出";
    }

    private void WriteExport(object record)
    {
        if (liveExportWriter == null) return;
        try { liveExportWriter.WriteLine(JsonUtility.ToJson(record)); }
        catch (IOException exception)
        {
            StopLiveExport();
            liveExportStatus = "写入中断：" + exception.Message;
        }
    }

    private void OnExportDay(int day)
    {
        WriteExportSnapshot("day_end");
        liveExportWriter?.Flush(); // One buffered write per day, no per-frame I/O.
        liveExportDays++;
    }

    private void WriteExportSnapshot(string reason)
    {
        if (liveExportWriter == null || liveGrid == null || liveTime == null) return;
        var snapshot = new ExportSnapshot
        {
            reason = reason, day = liveTime.currentDay,
            tuning = liveSimulation != null ? liveSimulation.ReadTuning() : default,
            secondsPerDay = liveTime.SecondsPerDay,
            speed = liveTime.Speed, paused = liveTime.IsPaused
        };
        SimulationEnvironmentController environment =
            liveBridge != null ? liveBridge.GetComponent<SimulationEnvironmentController>() : null;
        foreach (MapTileInstance tile in liveGrid.GetPlacedTiles())
        {
            if (tile == null || tile.Block == null) continue;
            BlockInfo block = tile.Block;
            var row = new ExportTile
            {
                x = tile.Coordinate.x, y = tile.Coordinate.y,
                preset = tile.Definition != null ? tile.Definition.name : "",
                height = block.elevation, water = block.waterCoverage.ToString(),
                temperature = block.temperature, humidity = block.humidity,
                recovery = block.habitatRecovery,
                plants = block.plantBiomass, plantCapacity = block.maxPlantBiomass,
                plantGrowth = block.plantGrowthToday,
                plantConsumed = block.consumedBiomassToday,
                algae = block.algaeBiomass, algaeRecovery = block.algaeRecovery
            };
            if (environment != null && environment.TryRead(tile.Coordinate, out var state))
            {
                row.terrain = state.Environment.Terrain.ToString();
                row.temperatureTarget = (float)state.Environment.Temperature.Target;
                row.humidityTarget = (float)state.Environment.Humidity.Target;
                row.recoveryTarget = (float)state.Environment.Recovery.Target;
                row.temperaturePhase = state.Environment.Temperature.Phase.ToString();
                row.humidityPhase = state.Environment.Humidity.Phase.ToString();
                row.recoveryPhase = state.Environment.Recovery.Phase.ToString();
                row.temperatureDays = state.Environment.Temperature.RemainingDays;
                row.humidityDays = state.Environment.Humidity.RemainingDays;
                row.recoveryDays = state.Environment.Recovery.RemainingDays;
                row.monsoonId = state.Environment.MonsoonId ?? 0;
            }
            if (block.community != null)
                foreach (PopulationData population in block.community)
                {
                    if (population == null || population.speciesAmount <= 0) continue;
                    row.populations.Add(new ExportPopulation
                    {
                        id = population.PopulationId,
                        species = population.species != null ? population.species.SpeciesName : population.lineageName,
                        speciesId = population.speciesId,
                        niche = population.ecologicalNiche.ToString(),
                        count = population.speciesAmount, level = population.trophicLevel,
                        fitT = population.fitTemperature, fitH = population.fitHumidity,
                        size = population.size, movement = population.movementAbility,
                        fertility = population.fertility,
                        births = population.birthsToday, deaths = population.deathsToday,
                        predationDeaths = population.predationDeathsToday,
                        migrationReadyDay = population.nextMigrationDay,
                        mutationDays = population.mutationDaysElapsed,
                        k = population.carryingCapacity,
                        fitness = population.environmentalFitness,
                        foodSatisfaction = population.actualEnergySatisfactionToday,
                        energyNeed = population.energyNeed,
                        allocatedFood = population.allocatedBiomass,
                        energyReserve = population.energyReserve
                    });
                }
            snapshot.tiles.Add(row);
        }
        WriteExport(snapshot);
    }

    private void WriteExportAction(string source, string operation,
        Vector2Int coordinate, string detail, bool snapshot = true)
    {
        if (liveExportWriter == null) return;
        WriteExport(new ExportAction
        {
            day = liveTime != null ? liveTime.currentDay : -1,
            time = DateTime.Now.ToString("O"), source = source,
            operation = operation, x = coordinate.x, y = coordinate.y,
            detail = detail
        });
        liveExportActions++;
        if (snapshot) WriteExportSnapshot("after_action");
    }

    private void OnExportIntervention(Vector2Int coordinate, string detail) =>
        WriteExportAction("intervention_controller",
            detail.StartsWith("rejected:") ? "rejected" : "commit", coordinate,
            detail, !detail.StartsWith("rejected:"));
    private void OnExportTechnology(Vector2Int coordinate, string detail) =>
        WriteExportAction("technology_controller",
            detail.StartsWith("rejected:") ? "rejected" : "commit", coordinate,
            detail, !detail.StartsWith("rejected:"));
    private void OnExportSpeedChanged(float speed) =>
        WriteExportAction("simulation_time", "speed", Vector2Int.zero, speed.ToString("F2"));
    private void OnExportUnityLog(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            WriteExportAction("unity", type.ToString(), Vector2Int.zero,
                message + (type == LogType.Exception ? " | " + stackTrace : ""));
    }

    private void OnExportTransition(PopulationTransitionResult result)
    {
        if (liveExportWriter == null) return;
        Vector2Int from = ExportCoordinate(result.SourceBlock);
        Vector2Int to = ExportCoordinate(result.TargetBlock);
        WriteExport(new ExportTransition
        {
            day = result.Day, kind = result.Kind.ToString(),
            outcome = result.Outcome.ToString(),
            fromX = from.x, fromY = from.y, toX = to.x, toY = to.y,
            sourcePopulationId = result.SourcePopulationId,
            resultPopulationId = result.ResultPopulationId,
            resultSpeciesId = result.ResultSpeciesId,
            targetSpecies = result.TargetSpecies != null ? result.TargetSpecies.SpeciesName : "",
            amount = result.Amount, sourceBefore = result.SourceCountBefore,
            sourceAfter = result.SourceCountAfter,
            targetBefore = result.TargetCountBefore,
            targetAfter = result.TargetCountAfter
        });
        liveExportTransitions++;
    }

    private Vector2Int ExportCoordinate(BlockInfo block)
    {
        if (liveGrid != null)
            foreach (MapTileInstance tile in liveGrid.GetPlacedTiles())
                if (tile != null && tile.Block == block) return tile.Coordinate;
        return new Vector2Int(int.MinValue, int.MinValue);
    }

    private void OnExportTilesChanged()
    {
        if (liveMapRefreshQueued) return;
        liveMapRefreshQueued = true;
        EditorApplication.delayCall += FinishExportMapChange;
    }

    private void FinishExportMapChange()
    {
        liveMapRefreshQueued = false;
        if (liveExportWriter == null || liveGrid == null) return;
        var next = new Dictionary<Vector2Int, string>();
        foreach (MapTileInstance tile in liveGrid.GetPlacedTiles())
        {
            if (tile == null) continue;
            string state = (tile.Definition != null ? tile.Definition.name : "") +
                ";height=" + tile.HeightLevel + ";rotation=" + tile.RotationSteps;
            next[tile.Coordinate] = state;
            if (!liveExportMap.TryGetValue(tile.Coordinate, out string before))
                WriteExportAction("map", "place_tile", tile.Coordinate, state);
            else if (before != state)
                WriteExportAction("map", "change_tile", tile.Coordinate,
                    "before=" + before + ";after=" + state);
        }
        foreach (var previous in liveExportMap)
            if (!next.ContainsKey(previous.Key))
                WriteExportAction("map", "remove_tile", previous.Key, previous.Value);
        liveExportMap.Clear();
        foreach (var current in next) liveExportMap.Add(current.Key, current.Value);
    }

    private void CaptureExportMap()
    {
        liveExportMap.Clear();
        if (liveGrid == null) return;
        foreach (MapTileInstance tile in liveGrid.GetPlacedTiles())
            if (tile != null)
                liveExportMap[tile.Coordinate] =
                    (tile.Definition != null ? tile.Definition.name : "") +
                    ";height=" + tile.HeightLevel + ";rotation=" + tile.RotationSteps;
    }
}
