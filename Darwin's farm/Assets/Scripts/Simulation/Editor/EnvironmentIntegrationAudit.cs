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
        Check(Read(c, 0).Environment.Terrain == TerrainKind.Grassland, "Initial terrain must be canonical grassland.");
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
