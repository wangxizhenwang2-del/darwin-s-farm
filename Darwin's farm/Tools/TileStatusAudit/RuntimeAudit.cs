using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DarwinFarm.Environment;
using UnityEngine;
using UnityEngine.SceneManagement;

// Run with the Unity Pipeline run_script command in a fresh WhiteBox Play session.
// All changes are confined to the Play session and an isolated temporary save folder.
public static class TileStatusRuntimeAudit
{
    private static readonly List<string> passed = new List<string>();

    public static async Task<string> Main()
    {
        Require(Application.isPlaying, "Run in Play mode.");
        int frame = Time.frameCount;
        await Task.Delay(250);
        Check(Time.frameCount > frame, "Player loop advances");
        var title = GameObject.Find("JournalCanvas").transform.Find("SafeArea/TitlePanel/NewGame");
        if (title.gameObject.activeInHierarchy) title.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        await DismissDiscoveries();
        var clock = UnityEngine.Object.FindFirstObjectByType<SimulationTime>();
        clock.Pause();
        var camera = Camera.main;
        Aim(camera, 0f);
        camera.fieldOfView = 60f;
        await Task.Delay(350);
        var presenter = UnityEngine.Object.FindFirstObjectByType<TileStatusPresenter>();
        Check(presenter.View.IsVisible && presenter.FocusedTile.Coordinate == Vector2Int.zero,
            "Centered tile appears automatically at the wider default 60-degree view");
        Check(UnityEngine.Object.FindObjectsByType<TileStatusView>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
            "Exactly one reusable status view");
        Check(presenter.View.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g => !g.raycastTarget) &&
            !presenter.View.GetComponent<CanvasGroup>().blocksRaycasts, "Status UI does not intercept map input");
        camera.fieldOfView = 63f; await Task.Delay(200);
        Check(presenter.View.IsVisible, "Zoom hysteresis retains a visible panel at 63 degrees");
        camera.fieldOfView = 75f; await Task.Delay(200);
        Check(!presenter.View.IsVisible, "Zooming out hides the panel");
        camera.fieldOfView = 63f; await Task.Delay(200);
        Check(!presenter.View.IsVisible, "Zoom hysteresis keeps a hidden panel hidden at 63 degrees");
        camera.fieldOfView = 60f; await Task.Delay(200);

        var grid = UnityEngine.Object.FindFirstObjectByType<MapGridManager>();
        grid.TryGetTile(Vector2Int.zero, out var origin);
        Require(grid.TryPlaceTile(Vector2Int.right, origin.Definition), "Could not place adjacent tile.");
        Aim(camera, MapGridManager.TileSize); await Task.Delay(350);
        Check(presenter.View.IsVisible && presenter.FocusedTile.Coordinate == Vector2Int.right,
            "Panning switches to the new central tile without clicking");
        Aim(camera, 0f); await Task.Delay(350);
        Aim(camera, MapGridManager.TileSize * .505f); await Task.Delay(350);
        Check(presenter.FocusedTile.Coordinate == Vector2Int.zero, "Near-equal tile distances do not flicker");
        Aim(camera, 10000f); await Task.Delay(200);
        Check(!presenter.View.IsVisible && presenter.FocusedTile == null, "No central tile hides the panel");
        Aim(camera, 0f);
        camera.orthographic = true; camera.orthographicSize = 35f; await Task.Delay(200);
        Check(presenter.View.IsVisible, "Wider orthographic view supported at size 35");
        camera.orthographicSize = 60f; await Task.Delay(200);
        Check(!presenter.View.IsVisible, "Orthographic zoom-out supported");
        camera.orthographic = false; camera.fieldOfView = 35f; await Task.Delay(250);
        var simulation = UnityEngine.Object.FindFirstObjectByType<SimulationController>();
        Require(simulation.ApplyPlantBiomass(origin.Block, origin.Block.maxPlantBiomass * .3f), "Stock preparation failed.");
        await Task.Delay(350);
        Check(Mathf.Abs(presenter.View.PlantFraction - .3f) < .0001f, "Stock fill reads the actual plant inventory");
        Check(presenter.View.VisibleToolCount == 0, "No applied tools leaves only the stock bar");
        var technology = UnityEngine.Object.FindFirstObjectByType<EnvironmentTechnologyController>();
        Require(technology.TryDeploy(Vector2Int.zero, new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.TemperatureChange, -1), out var result), result.Error);
        Require(technology.TryDeploy(Vector2Int.zero, new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.HumidityChange, 1), out result), result.Error);
        await Task.Delay(300);
        Check(presenter.View.VisibleToolCount == 2, "Two applied tools display together");
        var environment = UnityEngine.Object.FindFirstObjectByType<SimulationEnvironmentController>();
        environment.TryRead(Vector2Int.zero, out var beforePause);
        await Task.Delay(450);
        environment.TryRead(Vector2Int.zero, out var paused);
        Check(beforePause.Environment.Temperature.RemainingDays == paused.Environment.Temperature.RemainingDays,
            "Paused simulation keeps the tool time still");
        // Advance through the real SimulationTime event path, including ecology and environment.
        for (int i = 0; i < EnvironmentRules.TransitionDays + 75; i++) clock.NextDay();
        await Task.Delay(350);
        environment.TryRead(Vector2Int.zero, out var half);
        var mask = presenter.View.transform.Find("ActiveTools/Tool0/Icon/TimeMask").GetComponent<UnityEngine.UI.Image>();
        Check(half.Environment.Temperature.Phase == EffectPhase.Holding && Mathf.Abs(mask.fillAmount - .5f) < .0001f,
            "Rendered mask reaches 50 percent at half of the hold duration");
        Check(mask.sprite != mask.transform.parent.GetComponent<UnityEngine.UI.Image>().sprite &&
            mask.sprite.texture.GetPixel(32, 32).a > .99f && mask.sprite.texture.GetPixel(0, 0).a < .01f &&
            mask.color.a >= .6f, "Timer uses a visible solid circular face independently of transparent tool artwork");
        var expectedTier = half.Environment.Temperature.ToolTier;
        var expectedDays = half.Environment.Temperature.RemainingDays;
        var expectedStock = half.PlantStock;
        var expectedHash = simulation.CanonicalModelHash();
        string originalFolder = GameSaveService.TestFolderOverride;
        GameSaveService.TestFolderOverride = Path.Combine(Application.temporaryCachePath, "TileStatusAudit_" + DateTime.UtcNow.Ticks);
        try
        {
            Require(GameSaveService.TrySave(1, out string error), "Isolated save: " + error);
            Require(GameSaveService.TryPrepareLoad(1, out error), "Prepare load: " + error);
            SceneManager.LoadScene("WhiteBox");
            await Task.Delay(1500);
            camera = Camera.main; Aim(camera, 0f); camera.fieldOfView = 35f;
            clock = UnityEngine.Object.FindFirstObjectByType<SimulationTime>(); clock.Pause();
            await Task.Delay(350);
            presenter = UnityEngine.Object.FindFirstObjectByType<TileStatusPresenter>();
            environment = UnityEngine.Object.FindFirstObjectByType<SimulationEnvironmentController>();
            environment.TryRead(Vector2Int.zero, out var loaded);
            simulation = UnityEngine.Object.FindFirstObjectByType<SimulationController>();
            Check(!GameSaveService.HasPending && loaded.Environment.Temperature.ToolTier == expectedTier &&
                loaded.Environment.Temperature.RemainingDays == expectedDays && Mathf.Abs(loaded.PlantStock - expectedStock) < .01f &&
                simulation.CanonicalModelHash() == expectedHash, "Disk save and scene reload preserve ecology, tool identity and remaining days");
            mask = presenter.View.transform.Find("ActiveTools/Tool0/Icon/TimeMask").GetComponent<UnityEngine.UI.Image>();
            Check(presenter.View.IsVisible && presenter.View.VisibleToolCount == 2 && Mathf.Abs(mask.fillAmount - .5f) < .0001f,
                "Reloaded scene displays the same half-time mask automatically");
        }
        finally { GameSaveService.TestFolderOverride = originalFolder; }
        await DismissDiscoveries();
        clock.Pause();
        var mobile = UnityEngine.Object.FindFirstObjectByType<MobileGameUI>();
        var originalMode = mobile.Mode;
        mobile.Mode = MobileGameUI.DisplayMode.Mobile; await Task.Delay(350);
        Check(presenter.View.IsVisible, "Shared status view remains visible in mobile presentation");
        mobile.Mode = originalMode; await Task.Delay(200);
        technology = UnityEngine.Object.FindFirstObjectByType<EnvironmentTechnologyController>();
        Require(technology.TryCancel(Vector2Int.zero, EnvironmentTechnologyKind.TemperatureChange, out string cancelError), cancelError);
        await Task.Delay(350);
        mask = presenter.View.transform.Find("ActiveTools/Tool0/Icon/TimeMask").GetComponent<UnityEngine.UI.Image>();
        Check(presenter.View.VisibleToolCount == 2 && mask.fillAmount == 1f, "Cancelled tool becomes a covered recovery icon");
        for (int i = 0; i < EnvironmentRules.ClimateReturnDays; i++) clock.NextDay();
        await Task.Delay(350);
        Check(presenter.View.VisibleToolCount == 1, "Expired recovery icon disappears independently");
        clock.QuadrupleSpeed();
        int day = clock.currentDay;
        await Task.Delay(1100);
        clock.Pause();
        Check(clock.currentDay >= day + 3, "4x simulation advances tool time through the shared game clock");
        string report = "TILE_STATUS_RUNTIME_PASS: " + passed.Count + " checks\n" + string.Join("\n", passed);
        Directory.CreateDirectory(Path.Combine(Application.temporaryCachePath, "TileStatusAudit"));
        File.WriteAllText(Path.Combine(Application.temporaryCachePath, "TileStatusAudit", "runtime-report.txt"), report);
        Debug.Log(report);
        return report;
    }

    private static void Aim(Camera camera, float x)
    { camera.transform.position = new Vector3(x, 30f, -30f); camera.transform.rotation = Quaternion.Euler(45f, 0f, 0f); }
    private static async Task DismissDiscoveries()
    {
        for (int i = 0; i < 80; i++)
        {
            var unlock = UnityEngine.Object.FindFirstObjectByType<SpeciesUnlockPresenter>();
            if (unlock == null || !unlock.IsPresenting) return;
            unlock.Continue();
            await Task.Delay(100);
        }
    }
    private static void Check(bool condition, string name)
    { Require(condition, name); passed.Add(name); }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Tile status runtime check: " + message); }
}
