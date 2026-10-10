using System;
using System.IO;
using DarwinFarm.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Batch-only integration gate. Exercises real component lifecycle and map events in Play mode.
// Run Unity with -batchmode -nographics -executeMethod EnvironmentIntegrationAudit.Run (without -quit).
[InitializeOnLoad]
public static class EnvironmentIntegrationAudit
{
    private const string PendingKey = "DarwinFarm.EnvironmentAudit.Pending";
    private static double startedAt;
    private static bool completed;
    static EnvironmentIntegrationAudit()
    {
        if (SessionState.GetBool(PendingKey, false)) Register();
    }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run this audit in a batch editor.");
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
        var clock = UnityEngine.Object.FindFirstObjectByType<SimulationTime>();
        if (clock != null) clock.Pause();
        var controller = UnityEngine.Object.FindFirstObjectByType<SimulationEnvironmentController>();
        if (clock == null || controller == null || !controller.TryRead(Vector2Int.zero, out _)) return;
        try { Audit(controller, clock); Finish(null); }
        catch (Exception error) { Finish(error); }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static EnvironmentReadSnapshot Read(SimulationEnvironmentController c, int x)
    { Check(c.TryRead(new Vector2Int(x, 0), out var state), "Missing runtime tile " + x); return state; }
    private static void Apply(SimulationEnvironmentController c, int x, SimulationInterventionKind kind, float amount)
    { Check(c.TryApply(new Vector2Int(x, 0), kind, amount, out var error), error); }
    private static void Audit(SimulationEnvironmentController c, SimulationTime clock)
    {
        var grid = c.GetComponent<MapGridManager>();
        var bridge = c.GetComponent<MapSimulationBridge>();
        Check(Math.Abs(clock.SecondsPerDay - SimulationTime.DefaultSecondsPerDay) < .0001f &&
              SimulationTime.StandardLoopDays * SimulationTime.DefaultSecondsPerDay /
              SimulationTime.StandardLoopSpeed == 900f,
            "The standard loop must cover 1800 days in 15 minutes at 2x.");
        Check(Read(c, 0).Environment.Terrain == TerrainKind.Grassland, "Initial terrain must be canonical grassland.");
        Check(c.GetTerrainNeighbors(Vector2Int.zero).Count == 4 &&
              TerrainTransitionGraph.AreAdjacent(TerrainKind.Grassland, TerrainKind.Desert),
            "Runtime terrain graph must expose the grassland neighbors.");
        Check(Read(c, 0).PlantStock == 100000, "Initial stock must equal default increment.");
        string[] assets = { "森林", "雨林", "草原", "荒漠", "苔原", "高山", "雪山", "火山" };
        for (int i = 0; i < assets.Length; i++)
        {
            var preset = AssetDatabase.LoadAssetAtPath<MapTileDefinition>("Assets/Scripts/map/data/" + assets[i] + ".asset");
            Check(preset != null && grid.TryPlaceTile(new Vector2Int(i + 1, 0), preset), "Preset placement " + assets[i]);
            var state = Read(c, i + 1); var d = state.Environment.Defaults;
            Check(state.Environment.Terrain == SimulationEnvironmentController.TerrainFromPreset(preset.biome), "Deployment type " + assets[i]);
            Check(state.Environment.Temperature.Value == d.Temperature && state.Environment.Humidity.Value == d.Humidity &&
                state.Environment.Recovery.Value == d.Recovery && state.PlantStock == d.Recovery &&
                state.PlantCapacity == 1000000 && state.Environment.Elevation == d.Elevation, "Deployment defaults " + assets[i]);
            Check(preset.initialTemperature == d.Temperature && preset.initialHumidity == d.Humidity &&
                preset.habitatRecovery == d.Recovery && preset.initialPlantBiomass == d.Recovery && preset.maxPlantBiomass == 1000000,
                "Serialized preset differs from canonical rules " + assets[i]);
        }
        Check(c.TryEditEnvironment(new Vector2Int(3, 0), new SimulationEnvironmentEdit
        {
            temperature = 68, humidity = 17, elevation = 0,
            habitatRecovery = 50000, maxPlantBiomass = 1000000,
            plantBiomass = 100000
        }, out string terrainError), terrainError);
        Check(Read(c, 3).Environment.Terrain == TerrainKind.Desert &&
            grid.TryGetTile(new Vector2Int(3, 0), out MapTileInstance desertTile) &&
            desertTile.Biome == BiomeType.Desert,
            "Runtime tile terrain must follow environment reclassification.");
        Apply(c, 6, SimulationInterventionKind.Temperature, 50);
        clock.NextDay();
        Check(bridge.TryGetBlock(new Vector2Int(6, 0), out var highland) && highland.temperature == 52,
            "Environment must publish before the ecological day.");
        Check(highland.plantGrowthToday > 0, "Ecology must still grow plants.");
        Check(c.TryDeployMonsoon(Vector2Int.zero, 20, 20, out long wind, out var error), error);
        Apply(c, 0, SimulationInterventionKind.Temperature, 25);
        Check(Read(c, 0).Environment.HasMonsoon && !Read(c, 0).Environment.MonsoonApplied, "Runtime local masks monsoon.");
        for (int day = 0; day < 25; day++) clock.NextDay();
        Check(Read(c, 0).Environment.Temperature.Value == 93, "Runtime 25day entry.");
        int oldDay = clock.currentDay;
        clock.ResetDay(); clock.NextDay();
        Check(clock.currentDay == 1 && oldDay > 1 && Read(c, 0).Environment.Temperature.Value == 93, "Clock reset preserves effects.");
        Check(c.TryCancel(Vector2Int.zero, EnvironmentAttribute.Temperature, out error), error);
        for (int day = 0; day < 25; day++) clock.NextDay();
        Check(Read(c, 0).Environment.MonsoonApplied && Read(c, 0).Environment.Temperature.Value == 88, "Runtime monsoon resumes.");
        float stock = Read(c, 0).PlantStock;
        Apply(c, 0, SimulationInterventionKind.PlantBiomass, -.25f);
        Check(Math.Abs(Read(c, 0).PlantStock - stock * .75f) < .1f, "Runtime percentage stock basis.");
        stock = Read(c, 0).PlantStock;
        Apply(c, 0, SimulationInterventionKind.PlantRecovery, 25000);
        Check(Read(c, 0).Environment.Terrain == TerrainKind.Rainforest && Read(c, 0).PlantStock == stock,
            "Transformation must preserve stock.");
        Check(c.TryCancelMonsoon(wind, out error), error);
        var grass = AssetDatabase.LoadAssetAtPath<MapTileDefinition>("Assets/Scripts/map/data/草原.asset");
        Check(grid.TryPlaceTile(new Vector2Int(9, 0), grass) && grid.TryPlaceTile(new Vector2Int(10, 0), grass), "Extend real map.");
        Check(c.TryDeployMonsoon(new Vector2Int(9, 0), 20, -20, out _, out error, new EnvironmentPayment("unity-a", 11)), error);
        Check(c.TryDeployMonsoon(new Vector2Int(10, 0), -20, 20, out _, out error, new EnvironmentPayment("unity-b", 23)), error);
        decimal refunds = 0;
        foreach (var settlement in c.PendingSettlements) refunds += settlement.RefundAmount;
        Check(c.Monsoons.Count == 0 && refunds == 34, "Real graph overlap cancellation and exact settlement.");
        Check(c.TryDeployMonsoon(new Vector2Int(9, 0), 20, 20, out _, out error), error);
        Check(c.TryDeployMonsoon(Vector2Int.zero, -20, -20, out _, out error), error);
        stock = Read(c, 9).PlantStock;
        Apply(c, 9, SimulationInterventionKind.Elevation, 1);
        Check(c.Monsoons.Count == 0 && Read(c, 9).Environment.Elevation == 1 && Read(c, 9).PlantStock == stock,
            "Real source height change cancels all without regenerating stock.");
        Check(c.GetComponent<SimulationInterventionController>().TryApply(new SimulationInterventionRequest
        { center = new Vector2Int(6, 0), kind = SimulationInterventionKind.Humidity, amount = 25, radius = 0 }, out _),
            "Legacy editor adapter must use new controller.");
        AuditPlayerTechnologies(c, grid, grass);
    }

    // 2026-10-09 15:04 +08:00: exercise the actual player Controller and
    // map bridge, including atomic two-cell water operations, in Play mode.
    private static void AuditPlayerTechnologies(SimulationEnvironmentController c,
        MapGridManager grid, MapTileDefinition grass)
    {
        var player = c.GetComponent<EnvironmentTechnologyController>();
        Check(player != null && grid.TryPlaceTile(new Vector2Int(11, 0), grass),
            "Player technology controller and land target");
        var land = new Vector2Int(11, 0);
        var kinds = new[]
        {
            DarwinFarm.Environment.EnvironmentTechnologyKind.HeatInjection,
            DarwinFarm.Environment.EnvironmentTechnologyKind.RadiativeCooling,
            DarwinFarm.Environment.EnvironmentTechnologyKind.CloudSeeding,
            DarwinFarm.Environment.EnvironmentTechnologyKind.VaporRecovery,
            DarwinFarm.Environment.EnvironmentTechnologyKind.GrowthCatalyst,
            DarwinFarm.Environment.EnvironmentTechnologyKind.EcologicalSeeding,
            DarwinFarm.Environment.EnvironmentTechnologyKind.EcologicalSuppression,
            DarwinFarm.Environment.EnvironmentTechnologyKind.CrustUplift,
            DarwinFarm.Environment.EnvironmentTechnologyKind.StrataSubsidence
        };
        foreach (var kind in kinds)
        {
            var choice = new DarwinFarm.Environment.EnvironmentTechnologyChoice(kind,
                kind == DarwinFarm.Environment.EnvironmentTechnologyKind.CrustUplift ||
                kind == DarwinFarm.Environment.EnvironmentTechnologyKind.StrataSubsidence ? 1 : 2);
            Check(player.TryDeploy(land, choice, out var preview) && preview.Cost == 0,
                "Zero-cost land technology " + kind + ": " + preview.Error);
        }
        var lake = ScriptableObject.CreateInstance<MapTileDefinition>();
        lake.biome = BiomeType.Grassland;
        lake.initialWaterCoverage = WaterCoverage.Lake;
        lake.heightLevel = 0;
        lake.displayName = "audit-water";
        Check(grid.TryPlaceTile(new Vector2Int(12, 0), lake) &&
            grid.TryPlaceTile(new Vector2Int(13, 0), lake), "Two connected water cells");
        var water = new Vector2Int(12, 0);
        Check(!player.Preview(water, new DarwinFarm.Environment.EnvironmentTechnologyChoice(
            DarwinFarm.Environment.EnvironmentTechnologyKind.HeatInjection)).IsValid,
            "Water rejects climate technology");
        Check(player.TryDeploy(water, new DarwinFarm.Environment.EnvironmentTechnologyChoice(
            DarwinFarm.Environment.EnvironmentTechnologyKind.GrowthCatalyst), out _),
            "Whole-water catalyst");
        Check(Read(c, 12).Environment.Recovery.Value == 100000 &&
            Read(c, 13).Environment.Recovery.Value == 100000, "Water catalyst reaches both cells");
        Check(player.TryDeploy(water, new DarwinFarm.Environment.EnvironmentTechnologyChoice(
            DarwinFarm.Environment.EnvironmentTechnologyKind.EcologicalSeeding), out _),
            "Whole-water seeding");
        Check(Read(c, 12).PlantStock + Read(c, 13).PlantStock == 900000,
            "Whole-water seeding uses total capacity");
        Check(player.TryDeploy(water, new DarwinFarm.Environment.EnvironmentTechnologyChoice(
            DarwinFarm.Environment.EnvironmentTechnologyKind.EcologicalSuppression), out _),
            "Whole-water suppression");
        Check(Read(c, 12).Environment.Recovery.Value == 75000 &&
            Read(c, 12).PlantStock + Read(c, 13).PlantStock == 675000,
            "Whole-water suppression changes R and S together");
        Check(player.TryDeploy(water, new DarwinFarm.Environment.EnvironmentTechnologyChoice(
            DarwinFarm.Environment.EnvironmentTechnologyKind.CrustUplift), out _),
            "Whole-water height transaction");
        Check(Read(c, 12).Environment.Elevation == 1 &&
            Read(c, 13).Environment.Elevation == 1, "All water heights committed");
        Check(player.TryDeploy(land, new DarwinFarm.Environment.EnvironmentTechnologyChoice(
            DarwinFarm.Environment.EnvironmentTechnologyKind.MonsoonAnchor, 1, 20, -40), out _),
            "Monsoon anchor is tenth technology");
        Check(c.Monsoons.Count == 1 && player.CurrentApplied(land).Contains("季风锚"),
            "Current technology readback includes wind");
        SimulationTime time = c.GetComponent<SimulationTime>();
        time.Play(); time.DoubleSpeed();
        Check(time.Speed == 2f, "2x speed command");
        time.TogglePause(); Check(time.IsPaused, "space pauses the day clock");
        time.TogglePause(); Check(time.Speed == 2f, "space resumes the previous speed");
        time.Pause();
    }
    private static void Finish(Exception error)
    {
        completed = true; SessionState.SetBool(PendingKey, false);
        EditorApplication.update -= WaitForScene;
        string message = error == null ? "PASS: Unity Play mode environment integration audit" : "FAIL: " + error;
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/EnvironmentUnityAudit.result.txt", message);
        if (error == null) Debug.Log(message); else Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
