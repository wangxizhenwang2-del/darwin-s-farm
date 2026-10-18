using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public partial class PopulationSimulatorWindow
{
    private const string CurrentMutationRulesVersion = "MutationV4.1.1-cliff-stockgate-L2BLOCKED-cd100-20261018";

    [Serializable]
    private class ExportHeader
    {
        public string type = "header";
        public int schema = 4;
        public string createdAt;
        public string scene;
        public string unity;
        public string caseId;
        public string tester;
        public string sampling = "initial, after successful operation, and once per simulated day";
        public LandRuleTuning landRules;
        public int seed;
        public string randomVersion;
        public string buildHash;
        public bool deterministic;
    }

    [Serializable]
    private sealed class VersionedExportHeader : ExportHeader
    {
        public string mutationRulesVersion;
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
    private sealed class ExportEnd
    {
        public string type = "end";
        public string reason;
        public int day;
        public string time;
        public int recordedDays;
        public string logSha256;
        public string replaySha256;
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
    private sealed class ExportEcology
    {
        public string type = "ecology";
        public int day, x, y;
        public string populationId, species, kind, detail;
    }

    [Serializable]
    private sealed class ExportPopulation
    {
        public string id, species, speciesId, niche;
        public int count, level, fitT, fitH, size, movement, fertility;
        public int births, deaths, predationDeaths, migrationReadyDay;
        public int mutationDays;
        public float k, fitness, foodSatisfaction, energyNeed, allocatedFood, energyReserve;
        public float environmentalK, foodAverage10, stockDays, renewalCoverage;
        public float sizePressure, fertilityPressure, movementPressure, neighborAttraction;
        public int splitFounders, directedTraitUnlockDay, coexistenceDays;
        public string splitGate, splitTarget, directedTarget;
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
        public int nextSplitDay;
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
        public string modelHash;
        public long bioenergyBalance;
        public List<ExportTile> tiles = new List<ExportTile>();
    }

    // The export session belongs to the editor, not to the visible window.
    // Maximizing Game View can disable or recreate PopulationSimulatorWindow.
    private static StreamWriter liveExportWriter;
    private static string liveExportPath;
    private static string liveExportStatus = "尚未开始记录";
    private static string liveExportConfirmation = "";
    private static string liveExportIntegrity = "";
    [SerializeField] private string liveExportCase = "Whitebox";
    [SerializeField] private string liveExportTester = "";
    [SerializeField] private string liveExportNote = "";
    private static int liveExportDays, liveExportActions, liveExportTransitions;
    private static bool liveMapRefreshQueued;
    private static readonly Dictionary<Vector2Int, string> liveExportMap =
        new Dictionary<Vector2Int, string>();
    private static MapGridManager exportGrid;
    private static MapSimulationBridge exportBridge;
    private static SimulationInterventionController exportInterventions;
    private static SimulationController exportSimulation;
    private static SimulationTime exportTime;
    private static EnvironmentTechnologyController liveExportTechnology;
    private static PlayerEconomyController liveExportEconomy;

    private void DrawLiveExport()
    {
        EditorGUILayout.LabelField("导出测试数据", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "开始测试后记录全地图每日状态、成功操作、地图变化、迁徙/分化/进化和错误。退出 Play 模式前会自动关闭 JSONL，并写入日志校验码与复现确认码。同一版本、初始状态、种子、操作和每日结果一致时，复现确认码相同。",
            MessageType.Info);
        liveExportCase = EditorGUILayout.TextField("测试编号", liveExportCase);
        liveExportTester = EditorGUILayout.TextField("测试者", liveExportTester);
        liveExportNote = EditorGUILayout.TextField("操作/观察备注", liveExportNote);
        EditorGUILayout.LabelField("状态", liveExportStatus);
        if (liveExportStatus.StartsWith("重放失败", StringComparison.Ordinal))
            EditorGUILayout.HelpBox(liveExportStatus, MessageType.Error);
        if (!string.IsNullOrEmpty(liveExportConfirmation))
            EditorGUILayout.SelectableLabel("复现确认码 SHA-256: " + liveExportConfirmation,
                EditorStyles.textField, GUILayout.Height(38f));
        if (!string.IsNullOrEmpty(liveExportIntegrity))
            EditorGUILayout.SelectableLabel("日志校验码 SHA-256: " + liveExportIntegrity,
                EditorStyles.textField, GUILayout.Height(38f));
        EditorGUILayout.LabelField("每日快照", liveExportDays.ToString());
        EditorGUILayout.LabelField("操作", liveExportActions.ToString());
        EditorGUILayout.LabelField("生态事件", liveExportTransitions.ToString());
        if (liveExportWriter == null)
        {
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                if (GUILayout.Button("开始测试（选择导出文件）")) StartLiveExport();
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                if (GUILayout.Button("在当前 Play 场景重放日志")) ReplayExportFile();
        }
        else
        {
            if (GUILayout.Button("记录备注 / 检查点"))
            {
                WriteExportAction("tester", "checkpoint", liveCoordinate,
                    liveExportNote, false);
                liveExportNote = "";
            }
            if (GUILayout.Button("提前结束记录并导出")) StopLiveExport("manual_finish");
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
            if (liveExportWriter == null && GUILayout.Button("校验导出日志"))
            {
                try
                {
                    string code, integrity;
                    VerifyExportFile(liveExportPath, out code, out integrity);
                    liveExportConfirmation = code;
                    liveExportIntegrity = integrity;
                    liveExportStatus = "校验通过";
                }
                catch (Exception exception) { liveExportStatus = "校验失败：" + exception.Message; }
            }
        }
        EditorGUILayout.LabelField("日志格式 v4；模型版本见 header.mutationRulesVersion。每行一个 JSON 对象；end 含 logSha256 和 replaySha256。先用日志校验码验文件，再用复现确认码比结果；Tools/ReplayLogAudit/verify.py 可批量定位首次分歧。",
            EditorStyles.wordWrappedMiniLabel);
    }

    private void StartLiveExport()
    {
        ResolveLiveGame();
        if (liveExportWriter != null) return;
        if (!EditorApplication.isPlaying || liveGrid == null || liveSimulation == null ||
            liveTime == null || liveInterventions == null)
        { liveExportStatus = "请先进入有地图与模拟器的 Play 模式"; return; }
        if (!liveSimulation.IsDeterministicRunConfigured)
        { liveExportStatus = "当前场景尚未初始化确定性测试种子"; return; }
        string name = "Darwin_Whitebox_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jsonl";
        string path = EditorUtility.SaveFilePanel("导出测试数据",
            Application.persistentDataPath, name, "jsonl");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            exportGrid = liveGrid;
            exportBridge = liveBridge;
            exportInterventions = liveInterventions;
            exportSimulation = liveSimulation;
            exportTime = liveTime;
            liveExportWriter = new StreamWriter(path, false, new UTF8Encoding(false));
            liveExportPath = path;
            liveExportDays = liveExportActions = liveExportTransitions = 0;
            liveExportConfirmation = "";
            liveExportIntegrity = "";
            liveExportMap.Clear();
            WriteExport(new VersionedExportHeader
            {
                mutationRulesVersion = CurrentMutationRulesVersion,
                createdAt = DateTime.Now.ToString("O"),
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                unity = Application.unityVersion,
                caseId = liveExportCase, tester = liveExportTester,
                landRules = exportSimulation.ReadLandRules(),
                seed = exportSimulation.SimulationSeed,
                randomVersion = SimulationController.ModelRandomVersion,
                buildHash = ExportBuildHash(),
                deterministic = true
            });
            exportGrid.TilesChanged += OnExportTilesChanged;
            exportSimulation.OnDaySimulated += OnExportDay;
            exportSimulation.OnMigrationResolved += OnExportTransition;
            exportSimulation.OnNicheConversionResolved += OnExportTransition;
            exportSimulation.OnPresetEvolutionResolved += OnExportTransition;
            exportSimulation.OnEcologyEvent += OnExportEcology;
            exportInterventions.OperationCommitted += OnExportIntervention;
            exportTime.OnSpeedChanged += OnExportSpeedChanged;
            liveExportTechnology = exportBridge.GetComponent<EnvironmentTechnologyController>();
            if (liveExportTechnology != null)
                liveExportTechnology.OperationCommitted += OnExportTechnology;
            liveExportEconomy = exportBridge.GetComponent<PlayerEconomyController>();
            if (liveExportEconomy != null)
                liveExportEconomy.OperationCommitted += OnExportEconomy;
            Application.logMessageReceived += OnExportUnityLog;
            AssemblyReloadEvents.beforeAssemblyReload += OnExportBeforeReload;
            EditorApplication.playModeStateChanged += OnLiveExportPlayModeChanged;
            CaptureExportMap();
            WriteExportSnapshot("initial");
            liveExportWriter.Flush();
            liveExportStatus = "记录中";
        }
        catch (Exception exception)
        {
            StopLiveExport("start_failed");
            liveExportStatus = "无法开始记录：" + exception.Message;
        }
    }

    private static void StopLiveExport(string reason = "manual_finish")
    {
        EditorApplication.playModeStateChanged -= OnLiveExportPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= OnExportBeforeReload;
        EditorApplication.delayCall -= FinishExportMapChange;
        liveMapRefreshQueued = false;
        if (exportGrid != null) exportGrid.TilesChanged -= OnExportTilesChanged;
        if (exportSimulation != null)
        {
            exportSimulation.OnDaySimulated -= OnExportDay;
            exportSimulation.OnMigrationResolved -= OnExportTransition;
            exportSimulation.OnNicheConversionResolved -= OnExportTransition;
            exportSimulation.OnPresetEvolutionResolved -= OnExportTransition;
            exportSimulation.OnEcologyEvent -= OnExportEcology;
        }
        if (exportInterventions != null)
            exportInterventions.OperationCommitted -= OnExportIntervention;
        if (exportTime != null) exportTime.OnSpeedChanged -= OnExportSpeedChanged;
        if (liveExportTechnology != null)
            liveExportTechnology.OperationCommitted -= OnExportTechnology;
        liveExportTechnology = null;
        if (liveExportEconomy != null)
            liveExportEconomy.OperationCommitted -= OnExportEconomy;
        liveExportEconomy = null;
        Application.logMessageReceived -= OnExportUnityLog;
        if (liveExportWriter == null)
        {
            ClearExportSources();
            return;
        }
        StreamWriter writer = liveExportWriter;
        liveExportWriter = null;
        try
        {
            // Close the exclusive writer before reopening the file for the
            // two hashes. Windows will reject a reader while it remains open.
            writer.Dispose();
            var end = new ExportEnd
            {
                reason = reason,
                day = exportTime != null ? exportTime.currentDay : -1,
                time = DateTime.Now.ToString("O"),
                recordedDays = liveExportDays
            };
            ComputeExportHashes(liveExportPath, end, out end.logSha256,
                out end.replaySha256);
            using (var endWriter = new StreamWriter(liveExportPath, true,
                new UTF8Encoding(false)))
                endWriter.WriteLine(JsonUtility.ToJson(end));
            liveExportConfirmation = end.replaySha256;
            liveExportIntegrity = end.logSha256;
        }
        catch (Exception exception) { liveExportStatus = "关闭文件失败：" + exception.Message; }
        finally
        {
            try { writer.Dispose(); }
            catch (Exception exception) { liveExportStatus = "关闭文件失败：" + exception.Message; }
        }
        if (!liveExportStatus.StartsWith("关闭文件失败")) liveExportStatus = "已导出";
        ClearExportSources();
    }

    private static void ClearExportSources()
    {
        exportGrid = null;
        exportBridge = null;
        exportInterventions = null;
        exportSimulation = null;
        exportTime = null;
        liveExportMap.Clear();
    }

    private static void OnLiveExportPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode || liveExportWriter == null) return;

        // ExitingPlayMode is the last editor callback where the live scene and
        // its runtime ecology still exist. Capture once before domain teardown,
        // then close the stream so stopping Play mode always produces a valid file.
        if (liveMapRefreshQueued)
        {
            EditorApplication.delayCall -= FinishExportMapChange;
            FinishExportMapChange();
        }
        WriteExportSnapshot("play_mode_exit");
        liveExportWriter?.Flush();
        StopLiveExport("play_mode_exit");
        if (!liveExportStatus.StartsWith("关闭文件失败") &&
            !liveExportStatus.StartsWith("写入中断"))
            liveExportStatus = "已在游戏结束前自动导出";
    }

    private static void OnExportBeforeReload()
    {
        if (liveExportWriter == null) return;
        WriteExportSnapshot("before_assembly_reload");
        StopLiveExport("assembly_reload");
    }

    private static void WriteExport(object record)
    {
        if (liveExportWriter == null) return;
        try { liveExportWriter.WriteLine(JsonUtility.ToJson(record)); }
        catch (IOException exception)
        {
            StopLiveExport("write_error");
            liveExportStatus = "写入中断：" + exception.Message;
        }
    }

    private static void OnExportDay(int day)
    {
        WriteExportSnapshot("day_end");
        liveExportWriter?.Flush(); // One buffered write per day, no per-frame I/O.
        liveExportDays++;
    }

    private static void WriteExportSnapshot(string reason)
    {
        if (liveExportWriter == null || exportGrid == null || exportTime == null) return;
        WriteExport(CaptureExportSnapshot(reason));
    }

    private static ExportSnapshot CaptureExportSnapshot(string reason)
    {
        if (exportGrid == null || exportTime == null)
            throw new InvalidOperationException("模拟器地图或时钟不可用");
        var snapshot = new ExportSnapshot
        {
            reason = reason, day = exportTime.currentDay,
            tuning = exportSimulation != null ? exportSimulation.ReadTuning() : default,
            modelHash = exportSimulation != null ? exportSimulation.CanonicalModelHash() : "",
            bioenergyBalance = exportBridge != null &&
                exportBridge.GetComponent<BioenergyAccount>() != null
                    ? exportBridge.GetComponent<BioenergyAccount>().Balance : 0,
            secondsPerDay = exportTime.SecondsPerDay,
            speed = exportTime.Speed, paused = exportTime.IsPaused
        };
        SimulationEnvironmentController environment =
            exportBridge != null ? exportBridge.GetComponent<SimulationEnvironmentController>() : null;
        foreach (MapTileInstance tile in exportGrid.GetPlacedTiles())
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
                algae = block.algaeBiomass, algaeRecovery = block.algaeRecovery,
                nextSplitDay = block.nextLocalSplitDay
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
            }
            if (block.community != null)
                foreach (PopulationData population in block.community)
                {
                    if (population == null || population.speciesAmount <= 0) continue;
                    SimulationController.SplitGateInfo split = exportSimulation.ReadSplitGate(
                        block, population, snapshot.day);
                    SimulationController.LandSelectionSignals signals =
                        exportSimulation.ReadLandSelectionSignals(block, population);
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
                        energyReserve = population.energyReserve,
                        environmentalK = split.environmentalK,
                        foodAverage10 = split.foodAverage10,
                        splitFounders = split.founders,
                        splitGate = split.reason,
                        splitTarget = split.target != null
                            ? split.target.SpeciesName : "",
                        directedTarget = population.directedSplitTarget != null
                            ? population.directedSplitTarget.SpeciesName : "",
                        directedTraitUnlockDay = population.directedTraitUnlockDay,
                        coexistenceDays = population.sameSpeciesCoexistenceDays,
                        stockDays = signals.stockDays,
                        renewalCoverage = signals.renewalCoverage,
                        neighborAttraction = signals.neighborAttraction,
                        sizePressure = signals.sizePressure,
                        fertilityPressure = signals.fertilityPressure,
                        movementPressure = signals.movementPressure
                    });
                }
            snapshot.tiles.Add(row);
        }
        return snapshot;
    }

    private static void WriteExportAction(string source, string operation,
        Vector2Int coordinate, string detail, bool snapshot = true)
    {
        if (liveExportWriter == null) return;
        WriteExport(new ExportAction
        {
            day = exportTime != null ? exportTime.currentDay : -1,
            time = DateTime.Now.ToString("O"), source = source,
            operation = operation, x = coordinate.x, y = coordinate.y,
            detail = detail
        });
        if (source != "map") liveExportActions++;
        if (snapshot) WriteExportSnapshot("after_action");
    }

    private static void OnExportIntervention(Vector2Int coordinate, string detail) =>
        WriteExportAction("intervention_controller",
            detail.StartsWith("rejected:") ? "rejected" : "commit", coordinate,
            detail, !detail.StartsWith("rejected:"));
    private static void OnExportTechnology(Vector2Int coordinate, string detail) =>
        WriteExportAction("technology_controller",
            detail.StartsWith("rejected:") ? "rejected" : "commit", coordinate,
            detail, !detail.StartsWith("rejected:"));
    private static void OnExportEconomy(Vector2Int coordinate, string detail) =>
        WriteExportAction("player_economy", "commit", coordinate, detail);
    private static void OnExportSpeedChanged(float speed) =>
        WriteExportAction("simulation_time", "speed", Vector2Int.zero,
            speed.ToString("F2"), false);
    private static void OnExportUnityLog(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            WriteExportAction("unity", type.ToString(), Vector2Int.zero,
                message + (type == LogType.Exception ? " | " + stackTrace : ""), false);
    }

    private static void OnExportTransition(PopulationTransitionResult result)
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

    private static void OnExportEcology(int day, BlockInfo block,
        PopulationData population, string kind, string detail)
    {
        if (liveExportWriter == null) return;
        Vector2Int coordinate = ExportCoordinate(block);
        WriteExport(new ExportEcology
        {
            day = day, x = coordinate.x, y = coordinate.y,
            populationId = population != null ? population.PopulationId : "",
            species = population != null && population.species != null
                ? population.species.SpeciesName : "",
            kind = kind, detail = detail
        });
        liveExportTransitions++;
    }

    private static Vector2Int ExportCoordinate(BlockInfo block)
    {
        if (exportGrid != null)
            foreach (MapTileInstance tile in exportGrid.GetPlacedTiles())
                if (tile != null && tile.Block == block) return tile.Coordinate;
        return new Vector2Int(int.MinValue, int.MinValue);
    }

    private static void OnExportTilesChanged()
    {
        // The grid event is raised at the mutation boundary. Record each
        // change now, before a later player action can reorder the trace.
        FinishExportMapChange();
    }

    private static void FinishExportMapChange()
    {
        liveMapRefreshQueued = false;
        if (liveExportWriter == null || exportGrid == null) return;
        var next = new Dictionary<Vector2Int, string>();
        foreach (MapTileInstance tile in exportGrid.GetPlacedTiles())
        {
            if (tile == null) continue;
            string state = (tile.Definition != null ? tile.Definition.name : "") +
                ";height=" + tile.HeightLevel + ";rotation=" + tile.RotationSteps;
            next[tile.Coordinate] = state;
        }
        var coordinates = new List<Vector2Int>(next.Keys);
        coordinates.Sort(CompareExportCoordinates);
        foreach (Vector2Int coordinate in coordinates)
        {
            string state = next[coordinate];
            if (!liveExportMap.TryGetValue(coordinate, out string before))
                WriteExportAction("map", "place_tile", coordinate, state, false);
            else if (before != state)
                WriteExportAction("map", "change_tile", coordinate,
                    "before=" + before + ";after=" + state, false);
        }
        var removed = new List<Vector2Int>(liveExportMap.Keys);
        removed.Sort(CompareExportCoordinates);
        foreach (Vector2Int coordinate in removed)
            if (!next.ContainsKey(coordinate))
                WriteExportAction("map", "remove_tile", coordinate,
                    liveExportMap[coordinate], false);
        liveExportMap.Clear();
        foreach (var current in next) liveExportMap.Add(current.Key, current.Value);
    }

    private static int CompareExportCoordinates(Vector2Int a, Vector2Int b)
    {
        int x = a.x.CompareTo(b.x);
        return x != 0 ? x : a.y.CompareTo(b.y);
    }

    private static void CaptureExportMap()
    {
        liveExportMap.Clear();
        if (exportGrid == null) return;
        foreach (MapTileInstance tile in exportGrid.GetPlacedTiles())
            if (tile != null)
                liveExportMap[tile.Coordinate] =
                    (tile.Definition != null ? tile.Definition.name : "") +
                    ";height=" + tile.HeightLevel + ";rotation=" + tile.RotationSteps;
    }

    // v4 confirmation: SHA-256 of UTF-8 JSONL records with LF separators.
    // The log digest also covers end metadata with its two digest fields blank.
    // The replay digest omits wall-clock/tester data and UI-only actions.
    private static void ComputeExportHashes(string path, ExportEnd end,
        out string logHash, out string replayHash, bool completed = false)
    {
        using (SHA256 log = SHA256.Create())
        using (SHA256 replay = SHA256.Create())
        using (var reader = new StreamReader(path, Encoding.UTF8))
        {
            bool headerSeen = false, snapshotSeen = false, endSeen = false;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    throw new InvalidDataException("日志包含空行");
                string type = JsonUtility.FromJson<ExportRecordType>(line).type;
                if (!headerSeen && type != "header")
                    throw new InvalidDataException("日志缺少开头记录");
                if (type == "end")
                {
                    if (!completed || endSeen || reader.ReadLine() != null)
                        throw new InvalidDataException("结束记录后不能继续追加数据");
                    endSeen = true;
                    break;
                }
                AppendHash(log, line);
                string canonical = CanonicalReplayRecord(type, line);
                if (canonical != null) AppendHash(replay, canonical);
                if (type == "header")
                {
                    if (headerSeen) throw new InvalidDataException("日志有多个开头记录");
                    headerSeen = true;
                }
                if (type == "snapshot") snapshotSeen = true;
            }
            if (!headerSeen || !snapshotSeen || (completed && !endSeen))
                throw new InvalidDataException("日志缺少开头或快照记录");
            string savedLog = end.logSha256, savedReplay = end.replaySha256;
            end.logSha256 = end.replaySha256 = "";
            AppendHash(log, JsonUtility.ToJson(end));
            end.logSha256 = savedLog;
            end.replaySha256 = savedReplay;
            log.TransformFinalBlock(new byte[0], 0, 0);
            replay.TransformFinalBlock(new byte[0], 0, 0);
            logHash = HexHash(log.Hash);
            replayHash = HexHash(replay.Hash);
        }
    }

    [Serializable]
    private sealed class ExportRecordType { public string type; }

    private static string CanonicalReplayRecord(string type, string line)
    {
        switch (type)
        {
            case "header":
                if (line.IndexOf("\"mutationRulesVersion\"",
                    StringComparison.Ordinal) >= 0)
                {
                    var versioned = JsonUtility.FromJson<VersionedExportHeader>(line);
                    if (versioned.schema != 4)
                        throw new InvalidDataException("不支持的日志版本");
                    versioned.createdAt = versioned.caseId = versioned.tester = "";
                    return JsonUtility.ToJson(versioned);
                }
                var header = JsonUtility.FromJson<ExportHeader>(line);
                if (header.schema != 4)
                    throw new InvalidDataException("不支持的日志版本");
                header.createdAt = header.caseId = header.tester = "";
                return JsonUtility.ToJson(header);
            case "snapshot":
                var snapshot = JsonUtility.FromJson<ExportSnapshot>(line);
                snapshot.secondsPerDay = snapshot.speed = 0f;
                snapshot.paused = false;
                if (snapshot.tiles == null)
                    throw new InvalidDataException("快照缺少地图");
                snapshot.tiles.Sort((a, b) =>
                {
                    int x = a.x.CompareTo(b.x);
                    return x != 0 ? x : a.y.CompareTo(b.y);
                });
                foreach (ExportTile tile in snapshot.tiles)
                    if (tile.populations != null)
                        tile.populations.Sort((a, b) =>
                            string.CompareOrdinal(a.id, b.id));
                return JsonUtility.ToJson(snapshot);
            case "action":
                var action = JsonUtility.FromJson<ExportAction>(line);
                if (action.operation == "checkpoint" || action.operation == "speed" ||
                    action.source == "unity" || action.source == "map") return null;
                action.time = "";
                return JsonUtility.ToJson(action);
            case "transition":
                return JsonUtility.ToJson(JsonUtility.FromJson<ExportTransition>(line));
            case "ecology":
                return JsonUtility.ToJson(JsonUtility.FromJson<ExportEcology>(line));
            default:
                throw new InvalidDataException("未知记录类型：" + type);
        }
    }

    private static void AppendHash(HashAlgorithm hash, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text + "\n");
        hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
    }

    private static string HexHash(byte[] bytes)
    {
        var result = new StringBuilder(bytes.Length * 2);
        foreach (byte value in bytes) result.Append(value.ToString("x2"));
        return result.ToString();
    }

    private static string ExportBuildHash()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        var files = new List<string>(Directory.GetFiles(
            Path.Combine(root, "Assets", "Scripts"), "*.cs",
            SearchOption.AllDirectories));
        files.Add(Path.Combine(root, "Packages", "manifest.json"));
        files.Add(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"));
        string resources = Path.Combine(root, "Assets", "Resources");
        if (Directory.Exists(resources))
            foreach (string file in Directory.GetFiles(resources, "*", SearchOption.AllDirectories))
                if (file.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    files.Add(file);
        string scenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        if (!string.IsNullOrEmpty(scenePath)) files.Add(Path.Combine(root, scenePath));
        foreach (string assetType in new[] { "SpeciesData", "MapTileDefinition" })
            foreach (string guid in AssetDatabase.FindAssets("t:" + assetType))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                    files.Add(Path.Combine(root, assetPath));
            }
        files = new List<string>(new HashSet<string>(files, StringComparer.OrdinalIgnoreCase));
        files.Sort((a, b) => string.CompareOrdinal(
            a.Substring(root.Length).Replace('\\', '/'),
            b.Substring(root.Length).Replace('\\', '/')));
        using (SHA256 sha = SHA256.Create())
        {
            foreach (string file in files)
            {
                if (!File.Exists(file))
                    throw new FileNotFoundException("缺少复现版本文件", file);
                string relative = file.Substring(root.Length).Replace('\\', '/');
                byte[] name = Encoding.UTF8.GetBytes(relative + "\n");
                sha.TransformBlock(name, 0, name.Length, name, 0);
                byte[] content = File.ReadAllBytes(file);
                sha.TransformBlock(content, 0, content.Length, content, 0);
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            return HexHash(sha.Hash);
        }
    }

    private static void VerifyExportFile(string path, out string confirmation,
        out string integrity)
    {
        ExportEnd end = ReadExportEnd(path);
        string logHash, replayHash;
        ComputeExportHashes(path, end, out logHash, out replayHash, true);
        if (!string.Equals(logHash, end.logSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(replayHash, end.replaySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("确认码不匹配，日志可能已修改");
        confirmation = replayHash;
        integrity = logHash;
    }

    private static ExportEnd ReadExportEnd(string path)
    {
        using (var reader = new StreamReader(path, Encoding.UTF8))
        {
            string line, last = null;
            while ((line = reader.ReadLine()) != null) last = line;
            if (last == null || JsonUtility.FromJson<ExportRecordType>(last).type != "end")
                throw new InvalidDataException("日志没有结束记录");
            return JsonUtility.FromJson<ExportEnd>(last);
        }
    }

}
