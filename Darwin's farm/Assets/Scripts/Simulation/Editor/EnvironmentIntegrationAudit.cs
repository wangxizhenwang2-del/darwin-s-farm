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
        Check(grid.TryGetTile(Vector2Int.zero, out MapTileInstance initialTile) &&
            initialTile.Definition != null, "Initial scene tile must have a preset.");
        TerrainKind initialTerrain = SimulationEnvironmentController.TerrainFromPreset(
            initialTile.Definition.biome);
        Check(Read(c, 0).Environment.Terrain == initialTerrain,
            "Initial terrain must match the scene preset.");
        Check(Read(c, 0).Environment.Habitat ==
            EnvironmentRules.PlacementHabitat(initialTerrain),
            "Initial habitat must match the scene preset.");
        Check(Read(c, 0).PlantStock == initialTile.Definition.initialPlantBiomass,
            "Initial stock must match the scene preset.");
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
        double temperatureBeforeDay = Read(c, 6).Environment.Temperature.Value;
        Apply(c, 6, SimulationInterventionKind.Temperature, 50);
        clock.NextDay();
        Check(bridge.TryGetBlock(new Vector2Int(6, 0), out var highland) &&
            Read(c, 6).Environment.Temperature.Value > temperatureBeforeDay &&
            highland.temperature == EnvironmentRules.Round(
                Read(c, 6).Environment.Temperature.Value),
            "Environment must publish before the ecological day.");
        Check(highland.plantGrowthToday > 0, "Ecology must still grow plants.");
        string error;
        Apply(c, 0, SimulationInterventionKind.Temperature, 25);
        for (int day = 0; day < 25; day++) clock.NextDay();
        double enteredTemperature = Math.Min(100,
            initialTile.Definition.initialTemperature + 25);
        Check(Read(c, 0).Environment.Temperature.Value == enteredTemperature,
            "Runtime 25day climate entry.");
        int oldDay = clock.currentDay;
        clock.ResetDay(); clock.NextDay();
        Check(clock.currentDay == 1 && oldDay > 1 &&
            Read(c, 0).Environment.Temperature.Value == enteredTemperature,
            "Clock reset preserves effects.");
        Check(c.TryCancel(Vector2Int.zero, EnvironmentAttribute.Temperature, out error), error);
        for (int day = 0; day < 150; day++) clock.NextDay();
        Check(!Read(c, 0).Environment.HasLocalTemperature,
            "Runtime climate command completes after recovery.");
        float stock = Read(c, 0).PlantStock;
        Apply(c, 0, SimulationInterventionKind.PlantBiomass, -.25f);
        Check(Math.Abs(Read(c, 0).PlantStock - stock * .75f) < .1f, "Runtime percentage stock basis.");
        stock = Read(c, 0).PlantStock;
        Apply(c, 0, SimulationInterventionKind.PlantRecovery, 25000);
        Check(Read(c, 0).Environment.Recovery.Phase == EffectPhase.Entering &&
            Read(c, 0).PlantStock == stock,
            "Plant recovery enters its timeline without replacing stock.");
        var grass = AssetDatabase.LoadAssetAtPath<MapTileDefinition>("Assets/Scripts/map/data/草原.asset");
        Check(grid.TryPlaceTile(new Vector2Int(9, 0), grass) && grid.TryPlaceTile(new Vector2Int(10, 0), grass), "Extend real map.");
        stock = Read(c, 9).PlantStock;
        Apply(c, 9, SimulationInterventionKind.Elevation, 1);
        Check(Read(c, 9).Environment.Elevation == 1 && Read(c, 9).PlantStock == stock,
            "Height change preserves stock.");
        Check(c.GetComponent<SimulationInterventionController>().TryApply(new SimulationInterventionRequest
        { center = new Vector2Int(6, 0), kind = SimulationInterventionKind.Humidity, amount = 25, radius = 0 }, out _),
            "Legacy editor adapter must use new controller.");
        AuditPlayerTechnologies(c, grid, grass);
        AuditEconomy(c, bridge);
    }

    private static void AuditEconomy(SimulationEnvironmentController environment,
        MapSimulationBridge bridge)
    {
        var account = environment.GetComponent<BioenergyAccount>();
        var operations = environment.GetComponent<PlayerEconomyController>();
        BlockInfo source = null;
        Check(account != null && operations != null &&
              bridge.TryGetBlock(new Vector2Int(11, 0), out source),
            "Economy operations are attached to the live map");
        var chicken = AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/TestBioweb/Species/鸡.asset");
        Check(chicken != null, "Chicken value asset exists");
        var population = new PopulationData
        {
            species = chicken, speciesAmount = 100, lineageName = chicken.SpeciesName,
            ecologicalNiche = EcologicalNiche.Land, trophicLevel = 0,
            fitTemperature = 100, fitHumidity = 100, size = 6,
            movementAbility = 35, fertility = 75
        };
        source.community.Add(population);
        Check(operations.TryPreviewHarvest(source, population, out var preview,
            out var error) && preview.Harvested == 50 && preview.Remaining == 50 &&
            preview.Earned == 391, "Harvest preview uses actual removed count: " + error);
        long beforeHarvest = account.Balance;
        Check(operations.TryHarvest(source, population, out int harvested,
            out long earned, out error) && harvested == 50 && earned == 391 &&
            population.speciesAmount == 50 && account.Balance == beforeHarvest + 391,
            "Harvest removes residents and credits once: " + error);
        long beforeNoop = account.Balance;
        Check(!operations.TryDirectedEvolution(source, population,
            DirectedTrait.Temperature, 25, DirectedTrait.Humidity, 25, out _) &&
            account.Balance == beforeNoop, "Clipped zero-change evolution is rejected free");
        account.CostsEnabled = false;
        Check(operations.TryStore(source, population, out string backupId, out error) &&
            source.community.Contains(population) && population.speciesAmount == 50 &&
            account.Balance == beforeNoop && operations.Storage.Count == 1,
            "Disabled restrictions make storage free without consuming source: " + error);
        Check(operations.TryRelease(backupId, source, out error) &&
            population.speciesAmount == 100 && operations.Storage.Count == 0,
            "Storage releases once and merges with the resident: " + error);
        Check(!operations.CanDemolish(new Vector2Int(10, 0), out _),
            "Demolition cannot disconnect the later tiles");
    }

    // 2026-10-09 15:04 +08:00: exercise the actual player Controller and
    // map bridge, including atomic two-cell water operations, in Play mode.
    private static void AuditPlayerTechnologies(SimulationEnvironmentController c,
        MapGridManager grid, MapTileDefinition grass)
    {
        var player = c.GetComponent<EnvironmentTechnologyController>();
        var account = c.GetComponent<BioenergyAccount>();
        Check(account != null, "Bioenergy account is available");
        account.CostsEnabled = false;
        Check(player != null && grid.TryPlaceTile(new Vector2Int(11, 0), grass),
            "Player technology controller and land target");
        var land = new Vector2Int(11, 0);
        var choices = new[]
        {
            new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.TemperatureChange, 2),
            new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.TemperatureChange, -2),
            new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.HumidityChange, 2),
            new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.HumidityChange, -2),
            new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.PlantStockChange, 2),
            new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.PlantStockChange, -2),
            new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.PlantRecoveryChange, 2),
            new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.PlantRecoveryChange, -2),
            new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.ElevationChange, 1),
            new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.ElevationChange, -1)
        };
        foreach (var choice in choices)
        {
            var before = Read(c, land.x);
            Check(player.TryDeploy(land, choice, out var preview) && preview.Cost == 0,
                "Zero-cost land technology " + choice.Kind + " " + choice.Tier + ": " + preview.Error);
            if (choice.Kind == EnvironmentTechnologyKind.PlantStockChange)
            {
                var after = Read(c, land.x);
                float fraction = EnvironmentTechnologyCatalog.Fraction(choice);
                double expectedStock = EnvironmentRules.MultiplyStock(before.PlantStock,
                    EnvironmentRules.PlantCapacity, choice.Tier > 0 ? fraction : -fraction);
                Check(Math.Abs(after.PlantStock - expectedStock) < .1f &&
                      Math.Abs(after.Environment.Recovery.Value -
                          before.Environment.Recovery.Value) < .1f,
                    "Stock tier changes only stock");
            }
            if (choice.Kind == EnvironmentTechnologyKind.PlantRecoveryChange)
            {
                var after = Read(c, land.x);
                double expectedRecovery = EnvironmentRules.Clamp(EnvironmentAttribute.Recovery,
                    before.Environment.Recovery.Value + Math.Sign(choice.Tier) *
                    EnvironmentTechnologyCatalog.GrowthAmount(choice));
                Check(Math.Abs(after.PlantStock - before.PlantStock) < .1f &&
                      Math.Abs(after.Environment.Recovery.Value -
                          before.Environment.Recovery.Value) < .1f &&
                      after.Environment.Recovery.Phase == EffectPhase.Entering &&
                      Math.Abs(after.Environment.Recovery.Target - expectedRecovery) < .1f,
                    "Recovery tier changes its timeline target without changing stock");
            }
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
            DarwinFarm.Environment.EnvironmentTechnologyKind.TemperatureChange)).IsValid,
            "Water rejects climate technology");
        Check(player.TryDeploy(water, new DarwinFarm.Environment.EnvironmentTechnologyChoice(
            DarwinFarm.Environment.EnvironmentTechnologyKind.PlantRecoveryChange), out _),
            "Whole-water positive recovery tier");
        Check(Read(c, 12).Environment.Recovery.Value == 100000 &&
            Read(c, 13).Environment.Recovery.Value == 100000 &&
            Read(c, 12).PlantStock + Read(c, 13).PlantStock == 150000,
            "Water recovery tier reaches both cells without changing stock");
        Check(player.TryDeploy(water, new DarwinFarm.Environment.EnvironmentTechnologyChoice(
            DarwinFarm.Environment.EnvironmentTechnologyKind.PlantStockChange), out _),
            "Whole-water positive stock tier");
        Check(Read(c, 12).PlantStock + Read(c, 13).PlantStock == 187500,
            "Whole-water stock tier multiplies existing stock");
        Check(player.TryDeploy(water, new DarwinFarm.Environment.EnvironmentTechnologyChoice(
            DarwinFarm.Environment.EnvironmentTechnologyKind.PlantRecoveryChange, -1), out _),
            "Whole-water negative recovery tier");
        Check(Read(c, 12).Environment.Recovery.Value == 75000 &&
            Read(c, 12).PlantStock + Read(c, 13).PlantStock == 187500,
            "Whole-water recovery reduction leaves stock unchanged");
        Check(player.TryDeploy(water, new DarwinFarm.Environment.EnvironmentTechnologyChoice(
            DarwinFarm.Environment.EnvironmentTechnologyKind.PlantStockChange, -1), out _),
            "Whole-water negative stock tier");
        Check(Read(c, 12).Environment.Recovery.Value == 75000 &&
            Read(c, 12).PlantStock + Read(c, 13).PlantStock == 140625,
            "Whole-water stock reduction leaves recovery unchanged");
        Check(player.TryDeploy(water, new DarwinFarm.Environment.EnvironmentTechnologyChoice(
            DarwinFarm.Environment.EnvironmentTechnologyKind.ElevationChange), out _),
            "Whole-water height transaction");
        Check(Read(c, 12).Environment.Elevation == 1 &&
            Read(c, 13).Environment.Elevation == 1, "All water heights committed");
        long freeBalance = account.Balance;
        Check(player.TryDeploy(land, new EnvironmentTechnologyChoice(
            EnvironmentTechnologyKind.HumidityChange, 1), out var freePreview) &&
            freePreview.Cost == 0 && account.Balance == freeBalance,
            "Disabled economy restriction must not charge");
        account.CostsEnabled = true;
        long paidBalance = account.Balance;
        Check(player.TryDeploy(land, new EnvironmentTechnologyChoice(
            EnvironmentTechnologyKind.HumidityChange, -1), out var paidPreview) &&
            paidPreview.Cost == 220 && account.Balance == paidBalance - 220,
            "Enabled economy restriction charges the configured tier price");
        SimulationTime time = c.GetComponent<SimulationTime>();
        time.Play(); time.DoubleSpeed();
        Check(time.Speed == 2f, "2x speed command");
        time.CycleSpeed(); Check(time.Speed == 4f, "SpeedHUD cycles 2x to 4x");
        time.CycleSpeed(); Check(time.IsPaused, "SpeedHUD cycles 4x to pause");
        time.CycleSpeed(); Check(time.Speed == 1f, "SpeedHUD resumes at 1x");
        time.CycleSpeed(); Check(time.Speed == 2f, "SpeedHUD cycles 1x to 2x");
        time.CycleSpeed(); Check(time.Speed == 4f, "Shared speed input cycles 2x to 4x");
        time.CycleSpeed(); Check(time.IsPaused, "Shared speed input cycles 4x to pause");
        time.CycleSpeed(); Check(time.Speed == 1f, "Shared speed input resumes at 1x");
        time.Pause(); time.CycleSpeed();
        Check(time.Speed == 1f, "SpeedHUD resumes at 1x while paused");
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
