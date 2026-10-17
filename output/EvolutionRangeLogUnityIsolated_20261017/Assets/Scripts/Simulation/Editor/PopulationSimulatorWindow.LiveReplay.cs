using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DarwinFarm.Environment;
using UnityEditor;
using UnityEngine;

public partial class PopulationSimulatorWindow
{
    [Serializable] private sealed class ReplayCommunityBatch
    {
        public List<ReplayCommunityEntry> entries = new List<ReplayCommunityEntry>();
    }

    [Serializable] private sealed class ReplayCommunityEntry
    {
        public string residentId, speciesId, lineageName, niche;
        public int count, level, fitTemperature, fitHumidity, size, movement, fertility;
    }

    private void ReplayExportFile()
    {
        if (liveExportWriter != null) { liveExportStatus = "请先结束当前日志记录"; return; }
        ResolveLiveGame();
        if (!EditorApplication.isPlaying || liveGrid == null || liveBridge == null ||
            liveSimulation == null || liveTime == null || liveInterventions == null ||
            !liveSimulation.IsDeterministicRunConfigured)
        { liveExportStatus = "请先重新进入 Whitebox Play 场景并等待种群初始化"; return; }
        string path = EditorUtility.OpenFilePanel("选择待重放日志", Application.persistentDataPath,
            "jsonl");
        if (string.IsNullOrEmpty(path)) return;
        liveExportPath = path;
        liveExportConfirmation = liveExportIntegrity = "";
        int checkedSnapshots = 0;
        try
        {
            VerifyExportFile(path, out string confirmation, out string integrity);
            int endDay = ReadExportEnd(path).day;
            exportGrid = liveGrid;
            exportBridge = liveBridge;
            exportInterventions = liveInterventions;
            exportSimulation = liveSimulation;
            exportTime = liveTime;
            exportTime.Pause();
            using (var reader = new StreamReader(path))
            {
                string headerLine = reader.ReadLine();
                ExportHeader header = JsonUtility.FromJson<ExportHeader>(headerLine);
                if (header.schema != 4 || !header.deterministic ||
                    header.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ||
                    header.unity != Application.unityVersion ||
                    header.seed != exportSimulation.SimulationSeed ||
                    header.randomVersion != SimulationController.ModelRandomVersion ||
                    header.buildHash != ExportBuildHash())
                    throw new InvalidDataException("场景、Unity/工程版本或随机种子与日志不一致");
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string type = JsonUtility.FromJson<ExportRecordType>(line).type;
                    if (type == "end") break;
                    if (type == "action")
                        ReplayAction(JsonUtility.FromJson<ExportAction>(line));
                    else if (type == "snapshot")
                    {
                        ExportSnapshot expected = JsonUtility.FromJson<ExportSnapshot>(line);
                        if (expected.day < exportTime.currentDay)
                            throw new InvalidDataException("日志天数倒退：Day " + expected.day);
                        while (exportTime.currentDay < expected.day) exportTime.NextDay();
                        string actual = CanonicalReplayRecord("snapshot",
                            JsonUtility.ToJson(CaptureExportSnapshot(expected.reason)));
                        string wanted = CanonicalReplayRecord("snapshot", line);
                        if (actual != wanted)
                        {
                            ExportSnapshot current = JsonUtility.FromJson<ExportSnapshot>(actual);
                            throw new InvalidDataException("首次分歧：第 " + (checkedSnapshots + 1) +
                                " 个快照，Day " + expected.day + " / " + expected.reason +
                                "；期望 modelHash=" + expected.modelHash +
                                "，实际=" + current.modelHash +
                                "；期望生物能=" + expected.bioenergyBalance +
                                "，实际=" + current.bioenergyBalance);
                        }
                        checkedSnapshots++;
                        if (checkedSnapshots % 25 == 0 &&
                            EditorUtility.DisplayCancelableProgressBar("重放测试日志",
                                "Day " + expected.day + " · 已核对 " + checkedSnapshots + " 个快照",
                                endDay > 0 ? Mathf.Clamp01((float)expected.day / endDay) : 0f))
                            throw new OperationCanceledException("用户已停止重放");
                    }
                    else if (type != "transition" && type != "ecology")
                        throw new InvalidDataException("未知重放记录：" + type);
                }
            }
            liveExportConfirmation = confirmation;
            liveExportIntegrity = integrity;
            liveExportStatus = "重放通过：" + checkedSnapshots + " 个快照，Day " +
                exportTime.currentDay;
        }
        catch (Exception exception)
        {
            liveExportStatus = "重放失败：" + exception.Message +
                "（已核对 " + checkedSnapshots + " 个快照）";
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            ClearExportSources();
            Repaint();
        }
    }

    private static void ReplayAction(ExportAction action)
    {
        if (action.source == "unity" || action.source == "map" ||
            action.operation == "checkpoint" ||
            action.operation == "speed" || action.operation == "next_day" ||
            action.operation == "rejected") return;
        if (action.day != exportTime.currentDay)
            throw new InvalidDataException("操作天数与重放时钟不一致：Day " + action.day);
        var coordinate = new Vector2Int(action.x, action.y);
        string detail = action.detail ?? string.Empty;
        string error = null;
        if (action.source == "intervention_controller")
        {
            if (detail.StartsWith("intervention:", StringComparison.Ordinal))
            {
                var request = JsonUtility.FromJson<SimulationInterventionRequest>(
                    detail.Substring("intervention:".Length));
                if (!exportInterventions.TryApply(request, out var preview))
                    throw new InvalidDataException("环境操作失败：" + preview.Error);
            }
            else if (detail.StartsWith("environment_edit:", StringComparison.Ordinal))
            {
                var edit = JsonUtility.FromJson<SimulationEnvironmentEdit>(
                    detail.Substring("environment_edit:".Length));
                if (!exportInterventions.TryApplyEnvironment(coordinate, edit, out error))
                    throw new InvalidDataException("环境编辑失败：" + error);
            }
            else if (detail.StartsWith("global_tuning:", StringComparison.Ordinal))
            {
                var tuning = JsonUtility.FromJson<SimulationTuning>(
                    detail.Substring("global_tuning:".Length));
                exportInterventions.ApplyTuning(tuning, exportTime.SecondsPerDay);
            }
            else if (detail.StartsWith("community_edit:", StringComparison.Ordinal))
                ReplayCommunity(coordinate, detail.Substring("community_edit:".Length));
            else throw new InvalidDataException("未知环境操作：" + detail);
            return;
        }
        if (action.source == "technology_controller")
        {
            EnvironmentTechnologyController technology =
                exportBridge.GetComponent<EnvironmentTechnologyController>();
            bool deploy = detail.StartsWith("technology_deploy:", StringComparison.Ordinal);
            string kindText = deploy
                ? detail.Substring("technology_deploy:".Length).Split(';')[0]
                : detail.StartsWith("technology_cancel:", StringComparison.Ordinal)
                    ? detail.Substring("technology_cancel:".Length) : null;
            if (kindText == null || !Enum.TryParse(kindText, out EnvironmentTechnologyKind kind))
                throw new InvalidDataException("未知科技操作：" + detail);
            if (deploy)
            {
                int tier = int.Parse(ReplayField(detail, "tier"));
                if (!technology.TryDeploy(coordinate,
                    new EnvironmentTechnologyChoice(kind, tier), out var preview))
                    throw new InvalidDataException("科技投放失败：" + preview.Error);
            }
            else if (!technology.TryCancel(coordinate, kind, out error))
                throw new InvalidDataException("科技撤销失败：" + error);
            return;
        }
        if (action.source == "player_economy")
        {
            ReplayEconomy(coordinate, detail);
            return;
        }
        if (action.source == "simulator_window" && action.operation == "manual_migration")
        {
            PopulationData population = ReplayPopulation(coordinate,
                ReplayField(detail, "population"), out BlockInfo source);
            Vector2Int targetCoordinate = ReplayCoordinate(ReplayField(detail, "to"));
            if (!exportBridge.TryGetBlock(targetCoordinate, out BlockInfo target))
                throw new InvalidDataException("迁徙目标地块不存在");
            int before = population.speciesAmount;
            exportSimulation.MovePopulation(source, target, population, exportTime.currentDay);
            if (before - population.speciesAmount != int.Parse(ReplayField(detail, "amount")))
                throw new InvalidDataException("手动迁徙数量不一致");
            return;
        }
        throw new InvalidDataException("暂不支持重放的操作：" + action.source + "/" + action.operation);
    }

    private static void ReplayEconomy(Vector2Int coordinate, string detail)
    {
        PlayerEconomyController economy = exportBridge.GetComponent<PlayerEconomyController>();
        string kind = detail.Split(';')[0];
        string error = null;
        if (kind == "expand" || kind == "reshape_water")
        {
            MapTileDefinition preset = ReplayPreset(ReplayField(detail, "preset"));
            int rotation = int.Parse(ReplayField(detail, "rotation"));
            bool success = kind == "expand"
                ? economy.TryExpand(coordinate, preset, rotation, out error)
                : economy.TryReshapeWater(coordinate, preset, rotation, out error);
            if (!success) throw new InvalidDataException("地块操作失败：" + error);
            return;
        }
        if (kind == "demolish")
        {
            if (!economy.TryDemolish(coordinate, out error))
                throw new InvalidDataException("拆除地块失败：" + error);
            return;
        }
        if (kind == "release")
        {
            if (!exportBridge.TryGetBlock(coordinate, out BlockInfo target) ||
                !economy.TryRelease(ReplayField(detail, "storage"), target, out error))
                throw new InvalidDataException("生态仓释放失败：" + error);
            return;
        }
        PopulationData population = ReplayPopulation(coordinate,
            ReplayField(detail, "population"), out BlockInfo source);
        if (kind == "harvest")
        {
            if (!economy.TryHarvest(source, population, out _, out _, out error))
                throw new InvalidDataException("收割失败：" + error);
        }
        else if (kind == "store")
        {
            if (!economy.TryStore(source, population, out string storageId, out error) ||
                storageId != ReplayField(detail, "storage"))
                throw new InvalidDataException("生态仓复制失败或编号不一致：" + error);
        }
        else if (kind == "migrate")
        {
            Vector2Int to = ReplayCoordinate(ReplayField(detail, "target"));
            if (!exportBridge.TryGetBlock(to, out BlockInfo target) ||
                !economy.TryMigrate(source, population, target, out error))
                throw new InvalidDataException("迁徙失败：" + error);
        }
        else throw new InvalidDataException("未知经济操作：" + kind);
    }

    private static void ReplayCommunity(Vector2Int coordinate, string json)
    {
        if (!exportBridge.TryGetBlock(coordinate, out BlockInfo block))
            throw new InvalidDataException("群落地块不存在");
        ReplayCommunityBatch batch = JsonUtility.FromJson<ReplayCommunityBatch>(json);
        if (batch == null || batch.entries == null)
            throw new InvalidDataException("群落操作缺少种群参数");
        var edits = new List<SimulationPopulationEdit>();
        foreach (ReplayCommunityEntry entry in batch.entries)
        {
            PopulationData resident = string.IsNullOrEmpty(entry.residentId) ? null :
                block.community.Find(p => p != null && p.PopulationId == entry.residentId);
            if (!string.IsNullOrEmpty(entry.residentId) && resident == null)
                throw new InvalidDataException("找不到原种群：" + entry.residentId);
            SpeciesData species = null;
            WhiteboxBiowebTestSetup setup =
                UnityEngine.Object.FindFirstObjectByType<WhiteboxBiowebTestSetup>();
            if (setup != null)
                foreach (SpeciesData candidate in setup.MapSpecies)
                    if (candidate != null && candidate.SpeciesId == entry.speciesId)
                    { species = candidate; break; }
            if (!string.IsNullOrEmpty(entry.speciesId) && species == null)
                throw new InvalidDataException("找不到物种：" + entry.speciesId);
            var draft = new PopulationData
            {
                species = species, speciesId = entry.speciesId,
                lineageName = entry.lineageName,
                ecologicalNiche = (EcologicalNiche)Enum.Parse(typeof(EcologicalNiche), entry.niche),
                speciesAmount = entry.count, trophicLevel = entry.level,
                trophicLevelInitialized = true,
                fitTemperature = entry.fitTemperature, fitHumidity = entry.fitHumidity,
                size = entry.size, movementAbility = entry.movement,
                fertility = entry.fertility
            };
            edits.Add(new SimulationPopulationEdit { Resident = resident, Draft = draft });
        }
        if (!exportInterventions.TryApplyCommunity(coordinate, edits, out string error))
            throw new InvalidDataException("群落编辑失败：" + error);
    }

    private static PopulationData ReplayPopulation(Vector2Int coordinate, string id,
        out BlockInfo block)
    {
        if (!exportBridge.TryGetBlock(coordinate, out block) || block.community == null)
            throw new InvalidDataException("操作来源地块不存在：" + coordinate);
        PopulationData result = block.community.Find(p => p != null && p.PopulationId == id);
        if (result == null) throw new InvalidDataException("操作来源种群不存在：" + id);
        return result;
    }

    private static MapTileDefinition ReplayPreset(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:MapTileDefinition"))
        {
            MapTileDefinition candidate = AssetDatabase.LoadAssetAtPath<MapTileDefinition>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (candidate != null && candidate.name == name) return candidate;
        }
        throw new InvalidDataException("找不到地块预设：" + name);
    }

    private static string ReplayField(string detail, string key)
    {
        Match match = Regex.Match(detail ?? string.Empty,
            @"(?:^|;)" + Regex.Escape(key) + @"=([^;]*)");
        if (!match.Success) throw new InvalidDataException("操作缺少参数：" + key);
        return match.Groups[1].Value;
    }

    private static Vector2Int ReplayCoordinate(string text)
    {
        Match match = Regex.Match(text ?? string.Empty, @"^\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)$");
        if (!match.Success) throw new InvalidDataException("地块坐标格式无效：" + text);
        return new Vector2Int(int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value));
    }
}
