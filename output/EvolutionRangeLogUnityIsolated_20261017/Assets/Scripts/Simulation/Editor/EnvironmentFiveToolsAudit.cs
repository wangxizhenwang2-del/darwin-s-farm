using System;
using System.IO;
using DarwinFarm.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Batch Play mode gate for the five player-facing environment tools.
[InitializeOnLoad]
public static class EnvironmentFiveToolsAudit
{
    private const string PendingKey = "DarwinFarm.FiveToolsAudit.Pending";
    private static double startedAt;
    private static bool completed;

    static EnvironmentFiveToolsAudit()
    { if (SessionState.GetBool(PendingKey, false)) Register(); }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in batch mode.");
        EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity");
        SessionState.SetBool(PendingKey, true);
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
        if (completed) return;
        if (EditorApplication.timeSinceStartup - startedAt > 60)
        { Finish(new Exception("Play mode scene initialization timed out.")); return; }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        var environment = UnityEngine.Object.FindFirstObjectByType<SimulationEnvironmentController>();
        var clock = UnityEngine.Object.FindFirstObjectByType<SimulationTime>();
        if (environment == null || clock == null ||
            !environment.TryRead(Vector2Int.zero, out EnvironmentReadSnapshot before)) return;
        clock.Pause();
        try { Audit(environment, before); Finish(null); }
        catch (Exception error) { Finish(error); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void Audit(SimulationEnvironmentController environment,
        EnvironmentReadSnapshot before)
    {
        var player = environment.GetComponent<EnvironmentTechnologyController>();
        var account = environment.GetComponent<BioenergyAccount>();
        Check(player != null && account != null && !before.Environment.IsWater,
            "Land player controller and account must be ready.");
        Check(EnvironmentTechnologyCatalog.All.Length == 5, "Exactly five tools.");
        var session = UnityEngine.Object.FindFirstObjectByType<EnvironmentToolSession>();
        Check(session != null, "The shared desktop/mobile tool session must exist.");
        for (int i = 0; i < EnvironmentTechnologyCatalog.All.Length; i++)
        {
            session.SelectKind(i);
            Check(session.Choice.Kind == EnvironmentTechnologyCatalog.All[i],
                "Tool button " + i + " must select the matching model operation.");
        }
        Check(!session.OptionEnabled(0) && session.OptionEnabled(1) &&
            session.OptionEnabled(2) && !session.OptionEnabled(3),
            "Elevation options must allow only one-level changes.");
        session.End();
        account.CostsEnabled = true;
        long balance = account.Balance;
        var simulation = environment.GetComponent<SimulationController>();
        Check(simulation != null, "Simulation controller must be ready.");
        string beforePreviewHash = simulation.CanonicalModelHash();
        foreach (EnvironmentTechnologyKind kind in EnvironmentTechnologyCatalog.All)
            Check(player.Preview(Vector2Int.zero,
                new EnvironmentTechnologyChoice(kind, 1)).IsValid,
                "Player preview must be valid: " + kind);
        Check(simulation.CanonicalModelHash() == beforePreviewHash &&
            account.Balance == balance,
            "Five player previews must not mutate ecology or currency.");
        var stock = new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.PlantStockChange, 1);
        var recovery = new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.PlantRecoveryChange, 1);
        Check(player.Preview(Vector2Int.zero, stock).Cost == 260 &&
            player.Preview(Vector2Int.zero, recovery).Cost == 260,
            "S and R each use the configured small-tier price.");
        Check(player.TryDeploy(Vector2Int.zero, stock, out var stockResult),
            "Stock deploy: " + stockResult.Error);
        Check(environment.TryRead(Vector2Int.zero, out var afterStock) &&
            Math.Abs(afterStock.PlantStock - Math.Min(before.PlantCapacity,
                before.PlantStock * 1.25f)) < .1f &&
            Math.Abs(afterStock.Environment.Recovery.Value -
                before.Environment.Recovery.Value) < .1f &&
            account.Balance == balance - 260,
            "Stock changes S only and pays once.");
        Check(player.TryDeploy(Vector2Int.zero, recovery, out var recoveryResult),
            "Recovery deploy: " + recoveryResult.Error);
        Check(environment.TryRead(Vector2Int.zero, out var afterRecovery) &&
            Math.Abs(afterRecovery.PlantStock - afterStock.PlantStock) < .1f &&
            Math.Abs(afterRecovery.Environment.Recovery.Value -
                afterStock.Environment.Recovery.Value) < .1f &&
            afterRecovery.Environment.Recovery.Phase == EffectPhase.Entering &&
            Math.Abs(afterRecovery.Environment.Recovery.Target -
                EnvironmentRules.Clamp(EnvironmentAttribute.Recovery,
                    afterStock.Environment.Recovery.Value + 25000)) < .1f &&
            account.Balance == balance - 520,
            "Recovery begins a 25-day R transition and pays separately without changing S.");
        Check(!player.CanCancel(Vector2Int.zero, EnvironmentTechnologyKind.PlantStockChange) &&
            player.CanCancel(Vector2Int.zero, EnvironmentTechnologyKind.PlantRecoveryChange),
            "Instant stock has no cancellation timeline; recovery does.");

        var grid = environment.GetComponent<MapGridManager>();
        var directions = new[] { Vector2Int.right, Vector2Int.left,
            Vector2Int.up, Vector2Int.down };
        Vector2Int water = default;
        bool found = false;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            foreach (Vector2Int direction in directions)
                if (grid.CanPlaceAt(tile.Coordinate + direction))
                { water = tile.Coordinate + direction; found = true; break; }
            if (found) break;
        }
        Check(found, "A neighboring location is available for a water test tile.");
        var lake = ScriptableObject.CreateInstance<MapTileDefinition>();
        lake.biome = BiomeType.Grassland;
        lake.initialWaterCoverage = WaterCoverage.Lake;
        lake.heightLevel = 0;
        lake.displayName = "five-tools-water";
        EnvironmentReadSnapshot waterBefore = null;
        Check(grid.TryPlaceTile(water, lake) &&
            environment.TryRead(water, out waterBefore) &&
            waterBefore.Environment.IsWater, "Water tile is connected to the runtime map.");
        account.CostsEnabled = false;
        Check(player.TryDeploy(water, stock, out var waterStockResult),
            "Water stock deploy: " + waterStockResult.Error);
        EnvironmentReadSnapshot waterAfterStock = null;
        Check(environment.TryRead(water, out waterAfterStock) &&
            Math.Abs(waterAfterStock.PlantStock - waterBefore.PlantStock * 1.25f) < .1f &&
            Math.Abs(waterAfterStock.Environment.Recovery.Value -
                waterBefore.Environment.Recovery.Value) < .1f,
            "Water stock tool changes stock without changing recovery.");
        Check(player.TryDeploy(water, recovery, out var waterRecoveryResult),
            "Water recovery deploy: " + waterRecoveryResult.Error);
        Check(environment.TryRead(water, out var waterAfterRecovery) &&
            Math.Abs(waterAfterRecovery.PlantStock - waterAfterStock.PlantStock) < .1f &&
            Math.Abs(waterAfterRecovery.Environment.Recovery.Value -
                waterAfterStock.Environment.Recovery.Value - 25000) < .1f,
            "Water recovery tool changes recovery without changing stock.");
    }

    private static void Finish(Exception error)
    {
        completed = true;
        SessionState.SetBool(PendingKey, false);
        EditorApplication.update -= WaitForScene;
        string message = error == null ? "PASS: Unity five-tools audit" : "FAIL: " + error;
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/EnvironmentFiveToolsAudit.result.txt", message);
        if (error == null) Debug.Log(message); else Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
