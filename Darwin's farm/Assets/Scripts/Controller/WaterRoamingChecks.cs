#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Explicit acceptance in a fresh session. Uses real palette assets, never saves the scene.
public sealed class WaterRoamingChecks : MonoBehaviour
{
    public static string Result { get; private set; }
    private readonly List<string> passed = new List<string>();
    private const string Output = "Artifacts/WaterRoaming";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Water/Check shared water roaming and speed controls (fresh Play Mode)")]
    public static void Run()
    {
        var grid = FindFirstObjectByType<MapGridManager>();
        if (!Application.isPlaying || !grid || grid.GetPlacedTiles().Count() != 1)
            throw new InvalidOperationException("Use a fresh WhiteBox Play session with only its origin tile.");
        new GameObject("Shared water roaming acceptance").AddComponent<WaterRoamingChecks>();
    }

    private IEnumerator Start()
    {
        Result = "Running";
        Directory.CreateDirectory(Output);
        var checks = Checks();
        while (true)
        {
            bool next;
            try { next = checks.MoveNext(); }
            catch (Exception e) { Result = "FAILED: " + e; Report(); Debug.LogException(e); yield break; }
            if (!next) break;
            yield return checks.Current;
        }
        Result = "PASS: " + passed.Count + " shared-water and speed-control groups.";
        Report(); Debug.Log(Result);
    }
    private void Report() => File.WriteAllText(Output + "/Report.txt", Result + "\n" + string.Join("\n", passed));
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    private IEnumerator Checks()
    {
        yield return null;
        var journal = FindFirstObjectByType<DarwinJournalCanvas>();
        if (journal)
            ((Button)typeof(DarwinJournalCanvas).GetField("titleNew", Private).GetValue(journal)).onClick.Invoke();
        var grid = FindFirstObjectByType<MapGridManager>();
        var clock = grid.GetComponent<SimulationTime>();
        var simulation = grid.GetComponent<SimulationController>();
        var presenter = grid.GetComponent<PopulationVisualPresenter>();
        var movement = grid.GetComponent<PopulationMovementController>();
        var water = grid.GetComponent<MapWaterController>();
        clock.Pause();
        var definition = AssetDatabase.LoadAssetAtPath<MapTileDefinition>("Assets/Scripts/map/data/横向水道.asset");
        MapTileInstance Place(int x, int z)
        {
            Require(grid.TryPlaceTile(new Vector2Int(x, z), definition), "Place actual straight river");
            grid.TryGetTile(new Vector2Int(x, z), out var tile); return tile;
        }
        var a = Place(1, 0); var b = Place(2, 0);
        var c = Place(3, 0); grid.TrySetTileHeight(c.Coordinate, 1);
        var d = Place(4, 0); grid.TrySetTileHeight(d.Coordinate, 2);
        var mismatch = Place(1, 1);
        yield return null;
        Vector3 Point(MapTileInstance tile)
        {
            Require(water.TrySpace(tile.Coordinate, out var space) && space.TrySpawn(.64f, out _), "Safe water point");
            space.TrySpawn(.64f, out var p); return p;
        }
        Require(water.TrySwimmingArea(a.Coordinate, out var area) && area.Count == 2 && area.Contains(b), "Same-height area membership");
        Require(water.TrySwimmingPath(a.Coordinate, Point(a), Point(b), .64f, out _), "Same-height route blocked");
        Require(!water.TrySwimmingPath(a.Coordinate, Point(a), Point(c), .64f, out _) &&
            !water.TrySwimmingPath(a.Coordinate, Point(a), Point(mismatch), .64f, out _), "Ordinary swimming crossed a waterfall or mismatched mouth");
        Require(WaterPathfinding.TryPath(water, Point(a), Point(d), .64f, out _), "Legal 0-1-2 migration chain blocked");
        passed.Add("Real straight rivers share a roaming area; waterfalls and mismatched mouths are excluded from ordinary swimming; 0-1-2 migration path remains legal.");

        var species = AssetDatabase.LoadAssetAtPath<SpeciesData>("Assets/BiowebV46/Species/鹿游-103a527e.asset");
        var population = new PopulationData { species = species, ecologicalNiche = EcologicalNiche.Water,
            speciesAmount = 80, size = 30, movementAbility = 45, fertility = 55, fitTemperature = 50, fitHumidity = 50 };
        a.Block.community.Add(population);
        presenter.SyncNow(); yield return null; presenter.SyncNow();
        Require(presenter.TryGetVisual(population, out var view), "Aquatic visual not created");
        while (!movement.NavigationReady) yield return null;
        yield return null; view.Tick(.04f);
        var choose = typeof(WhiteboxPopulation).GetMethod("ChooseTarget", Private);
        var randomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(7241);
        bool choseNeighbour = false;
        for (int i = 0; i < 80; i++)
        {
            choose.Invoke(view, null);
            if (grid.WorldToGrid(view.TargetPosition) == b.Coordinate) { choseNeighbour = true; break; }
        }
        UnityEngine.Random.state = randomState;
        Require(choseNeighbour, "Ordinary random target selection never chose the connected neighbour");
        bool crossed = false;
        for (int i = 0; i < 1200; i++)
        {
            view.Tick(.04f);
            Require(view.ValidateMovement(out var problem), problem);
            if (grid.WorldToGrid(view.leader.groundPosition) == b.Coordinate) { crossed = true; break; }
        }
        Require(crossed && view.TileId == a.Coordinate && a.Block.community.Contains(population) &&
            !b.Block.community.Contains(population) && population.speciesAmount == 80, "Roaming moved the ledger/count or stayed on its home tile");
        yield return null; presenter.SyncNow();
        Require(presenter.TryGetVisual(population, out var sameView) && sameView == view &&
            grid.WorldToGrid(view.leader.groundPosition) == b.Coordinate, "Presenter reset a roaming swimmer to its home tile");
        passed.Add("Autonomous target selection and normal movement cross the seam; swept-body checks pass; presenter keeps the same visual, identity, home ledger and 80 animals.");

        int migrations = 0;
        Action<PopulationTransitionResult> onMigration = r => { if (r.ResultNiche == EcologicalNiche.Water) migrations++; };
        simulation.OnMigrationResolved += onMigration;
        simulation.SetEcologicalNicheProbabilities(0, 0, 0, 0, 0);
        typeof(SimulationController).GetMethod("RunMigration", Private).Invoke(simulation, new object[] { 7 });
        simulation.OnMigrationResolved -= onMigration;
        Require(migrations == 0 && population.speciesAmount == 80 && a.Block.community.Contains(population), "Same-height swim generated a timed migration");
        passed.Add("Day-7 migration dispatch does not relocate equal-height residents or emit migration events; zero waterfall chances are respected.");

        // Break the link while the animal is on the neighbouring tile.
        Require(grid.TryChangeWaterShape(b.Coordinate, definition, 1), "Rotate connected river");
        yield return null;
        view.Tick(.04f);
        Require(water.TrySwimmingArea(a.Coordinate, out area) && area.Count == 1 &&
            view.ValidateMovement(out _) && grid.WorldToGrid(view.leader.groundPosition) == a.Coordinate &&
            population.speciesAmount == 80, "Stale route survived rotation or swimmer was lost");
        grid.TryChangeWaterShape(b.Coordinate, definition, 0);
        Require(water.TrySwimmingArea(a.Coordinate, out area) && area.Count == 2, "Rotation did not reconnect area");
        grid.TrySetTileHeight(b.Coordinate, 2);
        Require(water.Connects(a, b) && !water.TrySwimmingPath(a.Coordinate, Point(a), Point(b), .64f, out _) &&
            !PopulationWaterTravel.CanTravel(a.Block, b.Block, population), "Two-level drop permitted direct swimming/migration");
        grid.TrySetTileHeight(b.Coordinate, 0);
        Require(grid.TryRemoveTile(b.Coordinate), "Remove neighbour");
        yield return null; view.Tick(.04f);
        Require(water.TrySwimmingArea(a.Coordinate, out area) && area.Count == 1 && view.ValidateMovement(out _), "Deleted river remains in roaming area");
        b = Place(2, 0); yield return null;
        passed.Add("Rotation, height change and deletion invalidate area/path caches immediately; stranded display safely returns to its home area without losing count; two-level drop remains impassable.");

        // The only waterfall is at the far end of the shared source area,
        // not next to the population's ledger tile.
        while (!movement.NavigationReady) yield return null;
        yield return null; view.Tick(.04f);
        randomState = UnityEngine.Random.state; UnityEngine.Random.InitState(7241);
        for (int i = 0; i < 80; i++)
        {
            choose.Invoke(view, null);
            if (grid.WorldToGrid(view.TargetPosition) == b.Coordinate) break;
        }
        UnityEngine.Random.state = randomState;
        for (int i = 0; i < 1200 && grid.WorldToGrid(view.leader.groundPosition) != b.Coordinate; i++) view.Tick(.04f);
        Require(grid.WorldToGrid(view.leader.groundPosition) == b.Coordinate, "Could not swim to the far side of the source area");
        var departure = view.Leader.position;
        foreach (var member in simulation.BuildWaterRegions().GetRegion(a.Block).Members)
        { member.algaeBiomass = 0; member.algaeRecovery = 0; }
        c.Block.algaeBiomass = 100000; c.Block.maxAlgaeBiomass = 1000000; c.Block.algaeRecovery = 0;
        simulation.SetEcologicalNicheProbabilities(0, 0, 0, 1, 0);
        migrations = 0; simulation.OnMigrationResolved += onMigration;
        typeof(SimulationController).GetMethod("RunMigration", Private).Invoke(simulation, new object[] { 14 });
        simulation.OnMigrationResolved -= onMigration;
        Require(migrations == 1 && c.Block.community.Any(p => p.ecologicalNiche == EcologicalNiche.Water && p.speciesAmount > 0) &&
            new[] { a, b, c, d }.Sum(t => t.Block.community.Where(p => p.ecologicalNiche == EcologicalNiche.Water).Sum(p => p.speciesAmount)) == 80,
            "Migration did not use the remote area boundary or lost animals");
        typeof(PopulationVisualPresenter).GetMethod("StartPendingTransitions", Private).Invoke(presenter, null);
        var transition = FindObjectsByType<PopulationTransitionVisual>(FindObjectsSortMode.None).FirstOrDefault();
        Require(transition && Vector3.Distance(transition.transform.position, departure) < .05f,
            "Waterfall migration snapped a roaming swimmer back to its ledger tile");
        passed.Add("A one-level migration can depart through the remote end of the shared area; it retains the upstream probability, conserves 80 animals and animates from the swimmer's actual position.");

        // Exercise the actual PC button callback, including its visible label.
        var desktop = FindFirstObjectByType<GameInformationUI>();
        var speedText = (Text)typeof(GameInformationUI).GetField("speedText", Private).GetValue(desktop);
        var desktopButton = speedText.transform.parent.GetComponent<Button>();
        clock.Play();
        foreach (float expected in new[] { 2f, 4f, 0f, 1f })
        {
            desktopButton.onClick.Invoke();
            Require(clock.Speed == expected && speedText.text == (expected == 0 ? "暂停" : expected.ToString("0") + "X"), "PC speed button/label mismatch");
        }
        clock.DoubleSpeed(); clock.TogglePause(); clock.TogglePause();
        Require(clock.Speed == 2f, "General pause toggle no longer restores previous speed");
        clock.QuadrupleSpeed(); clock.ToggleSpacePause();
        Require(clock.IsPaused, "Space did not pause 4x speed");
        clock.ToggleSpacePause();
        Require(clock.Speed == 1f, "Space did not resume at 1x");
        clock.DoubleSpeed(); clock.ToggleSpacePause(); clock.ToggleSpacePause();
        Require(clock.Speed == 1f, "Space did not resume 2x speed at 1x");
        clock.DoubleSpeed();
        clock.AcquireModalPause(); desktopButton.onClick.Invoke();
        Require(clock.IsPaused, "Speed click released a modal pause");
        clock.ReleaseModalPause(); Require(clock.Speed == 2f, "Modal did not restore previous speed");
        passed.Add("PC button cycles 1x -> 2x -> 4x -> pause -> 1x; Space pauses and resumes at 1x; modal pause restores the prior speed.");

        // The mobile handler is private and its panel is inactive on PC.
        // Bind its existing clock reference only in this runtime fixture.
        var mobile = FindFirstObjectByType<MobileGameUI>(FindObjectsInactive.Include);
        typeof(MobileGameUI).GetField("clock", Private).SetValue(mobile, clock);
        var mobileCycle = typeof(MobileGameUI).GetMethod("CycleMobileSimulationSpeed", Private);
        clock.Play();
        foreach (float expected in new[] { 2f, 4f, 0f, 1f })
        {
            mobileCycle.Invoke(mobile, null);
            Require(clock.Speed == expected, "Mobile speed cycle changed");
        }
        clock.Pause();
        passed.Add("Existing mobile speed callback retains 1x -> 2x -> 4x -> pause -> 1x.");
        var camera = Camera.main;
        camera.transform.position = new Vector3(34, 35, -32); camera.transform.LookAt(new Vector3(34, 0, 0));
        camera.orthographic = true; camera.orthographicSize = 19;
        yield return null;
    }
}
#endif
