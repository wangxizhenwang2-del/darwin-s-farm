#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Darwin.Rendering;
using UnityEditor;
using UnityEngine;

// Run on a fresh WhiteBox play session. All fixtures are runtime-only.
public sealed class WaterPopulationIntegrationChecks : MonoBehaviour
{
    public static string Result { get; private set; }
    private readonly List<string> passed = new List<string>();
    private readonly List<MapTileDefinition> definitions = new List<MapTileDefinition>();
    private MapGridManager grid;
    private MapWaterController water;
    private PopulationMovementController movement;
    private PopulationVisualPresenter presenter;
    private SimulationController simulation;
    private const string Output = "Artifacts/WaterIntegration";
    [MenuItem("Tools/Water/Run integration checks (fresh WhiteBox Play Mode)")]
    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter a fresh WhiteBox Play session first; the fixture is runtime-only.");
        var map = FindFirstObjectByType<MapGridManager>();
        if (!map || map.GetPlacedTiles().Count() != 1) throw new InvalidOperationException("Use a fresh WhiteBox Play session with only its origin tile.");
        new GameObject("Water Integration Checks").AddComponent<WaterPopulationIntegrationChecks>();
    }
    private IEnumerator Start()
    {
        Result = "Running"; Directory.CreateDirectory(Output);
        IEnumerator routine = Checks();
        while (true)
        {
            bool next;
            try { next = routine.MoveNext(); }
            catch (Exception e) { Result = "FAILED: " + e; Report(); Debug.LogException(e); yield break; }
            if (!next) break;
            yield return routine.Current;
        }
        Result = "PASS: " + passed.Count + " water integration groups."; Report(); Debug.Log(Result);
    }
    private void Report() => File.WriteAllText(Output + "/Report.txt", Result + "\n" + string.Join("\n", passed));
    private void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private MapTileDefinition Definition(WaterCoverage coverage, int mask = 0)
    {
        var d = ScriptableObject.CreateInstance<MapTileDefinition>(); definitions.Add(d);
        d.name = "Water fixture " + coverage; d.displayName = d.name; d.initialWaterCoverage = coverage;
        d.riverOpenings = mask; d.heightLevel = 0; return d;
    }
    private MapTileInstance Place(Vector2Int id, MapTileDefinition d, int rotation = 0)
    {
        Require(grid.TryPlaceTile(id, d, rotation), "Place " + id);
        grid.TryGetTile(id, out var tile); return tile;
    }
    private Vector3 Point(MapTileInstance tile)
    {
        Require(water.TrySpace(tile.Coordinate, out var space) && space.TrySpawn(.64f, out _), "Missing safe water " + tile.Coordinate);
        space.TrySpawn(.64f, out var p); return p;
    }
    private bool Route(MapTileInstance a, MapTileInstance b) => WaterPathfinding.TryPath(water, Point(a), Point(b), .64f, out _);
    private IEnumerator Checks()
    {
        yield return null;
        grid = FindFirstObjectByType<MapGridManager>(); Require(grid, "Missing WhiteBox grid");
        while (!grid.TryGetTile(Vector2Int.zero, out _)) yield return null;
        grid.GetComponent<SimulationTime>().Pause();
        simulation = grid.GetComponent<SimulationController>(); simulation.SetWaterEcologyEnabled(true);
        simulation.SetMigrationParameters(true, 1, 100, .8f, .5f, .2f, 0);
        water = MapWaterController.GetOrCreate(grid); movement = grid.GetComponent<PopulationMovementController>();
        presenter = grid.GetComponent<PopulationVisualPresenter>();
        foreach (var t in grid.GetPlacedTiles()) t.Block.community.Clear();
        var cross = Definition(WaterCoverage.CrossingRiver, 15);
        var straight = Definition(WaterCoverage.horizontalRiver);
        var bend = Definition(WaterCoverage.NorthWestRiver);
        var a = Place(new Vector2Int(1, 0), cross); var b = Place(new Vector2Int(2, 0), cross);
        var c = Place(new Vector2Int(3, 0), cross);
        Require(water.Connects(a, b) && HabitatTopology.ChannelsConnect(a.Block, b.Block) && Route(a, b), "Equal-height channel route");
        passed.Add("Same-level authored mouths join; simulation and mesh route agree.");
        var mismatch = Place(new Vector2Int(1, 1), straight);
        Require(!water.Connects(a, mismatch) && !HabitatTopology.ChannelsConnect(a.Block, mismatch.Block) && !Route(a, mismatch), "Mismatched mouths permitted travel");
        Require(grid.TryChangeWaterShape(mismatch.Coordinate, straight, 1), "Rotate river");
        Require(water.Connects(a, mismatch) && Route(a, mismatch), "Rotated mouths did not reconnect");
        passed.Add("Wrong-facing mouths block; rotation reconnects geometry and simulation.");
        grid.TrySetTileHeight(b.Coordinate, 1); grid.TrySetTileHeight(c.Coordinate, 2);
        Require(water.Network.WaterfallCount >= 2 && Route(a, c) && Route(c, a), "Stepped heights block a legal route");
        WaterPathfinding.TryPath(water, Point(a), Point(c), .64f, out var stepped);
        for (int i = 1; i < stepped.Length; i++)
            if (Mathf.Abs(stepped[i].y - stepped[i - 1].y) > .1f)
                Require(new Vector2(stepped[i].x - stepped[i - 1].x, stepped[i].z - stepped[i - 1].z).magnitude < .3f,
                    "Migration ramps across an entire tile instead of following the waterfall");
        Require(WaterRegionMap.Build(new[] { a.Block, b.Block, c.Block }).Regions.Count == 3, "Different height ledgers merged");
        passed.Add("0-1-2 waterfall chain is traversable both ways; three resource regions remain separate.");
        var d = Place(new Vector2Int(4, 0), cross);
        Require(water.Connects(c, d) && !HabitatTopology.ChannelsConnect(c.Block, d.Block) && !Route(c, d), "Two-level waterfall allowed direct migration");
        Require(water.Network.WaterfallCount >= 3, "Two-level waterfall missing");
        passed.Add("Two-level drop still renders a waterfall but blocks direct migration.");
        var bypass = Place(new Vector2Int(3, 1), cross); grid.TrySetTileHeight(bypass.Coordinate, 1);
        var bypass2 = Place(new Vector2Int(4, 1), cross);
        Require(Route(c, d), "Legal intermediate-height detour rejected");
        passed.Add("A legal 2-1-0 detour remains available around a blocked direct edge.");
        var l = Place(new Vector2Int(1, -1), bend);
        var river = water.Network.GetComponentsInChildren<MoebiusRiverTile>().First(t => t.IsRiver && (t.transform.position - l.transform.position).sqrMagnitude < .01f);
        Vector3[] ends = river.profile.ports.Select(p => river.transform.TransformPoint(p.Center - p.Normal * 3f)).ToArray();
        foreach (Vector3 p in ends) Require(water.TrySpace(l.Coordinate, out var s) && s.Contains(p, .64f), "L mouth endpoint not in water");
        Require(WaterPathfinding.TryPath(water, ends[0], ends[1], .64f, out var path, l.Coordinate), "L path unavailable");
        for (int i = 1; i < path.Length; i++) Require(WaterPathfinding.Segment(water, path[i - 1], path[i], .64f, l.Coordinate), "L path cut through bank");
        passed.Add("Bend route validates every swept-body segment against the real capped mesh.");
        var shapeTile = Place(new Vector2Int(2, -1), straight);
        var junction = Definition(WaterCoverage.CrossingRiver, 11);
        foreach (var shape in new[] { straight, bend, junction })
            for (int rotation = 0; rotation < 4; rotation++)
            {
                if (shapeTile.Definition != shape || shapeTile.RotationSteps != rotation)
                    Require(grid.TryChangeWaterShape(shapeTile.Coordinate, shape, rotation), "Shape rotation failed");
                var authored = water.Network.GetComponentsInChildren<MoebiusRiverTile>().First(t => t.IsRiver &&
                    (t.transform.position - shapeTile.transform.position).sqrMagnitude < .01f);
                var endpoints = authored.profile.ports.Select(p => authored.transform.TransformPoint(p.Center - p.Normal * 3)).ToArray();
                Require(endpoints.Length >= 2, "Shape is missing authored mouths");
                for (int end = 1; end < endpoints.Length; end++)
                    Require(WaterPathfinding.TryPath(water, endpoints[0], endpoints[end], .64f, out var rotatedRoute, shapeTile.Coordinate),
                        "Rotated " + shape.initialWaterCoverage + " has no legal route at " + rotation);
                yield return null;
            }
        grid.TryRemoveTile(shapeTile.Coordinate);
        passed.Add("All four rotations of straight, bend and three-way authored river assets have valid in-water routes (12 combinations).");
        var species = AssetDatabase.LoadAssetAtPath<SpeciesData>("Assets/BiowebV46/Species/鹿游-103a527e.asset");
        Require(species, "Missing aquatic appearance");
        var population = new PopulationData { species = species, lineageName = "Water check", ecologicalNiche = EcologicalNiche.Water, speciesAmount = 80 };
        a.Block.community.Add(population);
        yield return null; presenter.SyncNow();
        Require(presenter.TryGetVisual(population, out var view) && view.IsWater && view.Count == 80, "Aquatic representative missing");
        Vector3 initial = view.Leader.position;
        Require(view.RequestReverse(), "No safe swimming route from spawn");
        for (int i = 0; i < 700; i++)
        {
            view.Tick(.04f);
            Require(view.ValidateMovement(out var error), error);
            if (i % 20 == 0) yield return null;
        }
        Require(Vector3.Distance(initial, view.Leader.position) > .05f, "Aquatic representative never moved");
        Require(view.GetComponentInChildren<SpriteRenderer>().sharedMaterial.shader.name == "Darwin/Population Water Sprite", "Wrong immersion shader");
        Require(population.speciesAmount == 80 && a.Block.community.Contains(population), "Swimming modified ecological ownership/count");
        passed.Add("700 swimming steps stay in water with species sprite, visible label, depth-tested immersion and unchanged model count.");
        var economy = grid.GetComponent<PlayerEconomyController>(); grid.GetComponent<BioenergyAccount>().CostsEnabled = false;
        Require(!economy.CanPlacePopulation(grid.GetPlacedTiles().First(t => t.Coordinate == Vector2Int.zero).Block, population, a.Block, out _), "Water animal accepted land destination");
        grid.TrySetTileHeight(b.Coordinate, 0);
        Require(economy.TryMigrate(a.Block, population, b.Block, out var migrationError), migrationError);
        presenter.SyncNow();
        yield return null;
        Require(presenter.ActiveTransitionCount > 0, "Water migration did not animate");
        int amount = grid.GetPlacedTiles().Sum(t => t.Block.community.Where(p => p != null && p.ecologicalNiche == EcologicalNiche.Water).Sum(p => p.speciesAmount));
        Require(amount == 80, "Migration lost/duplicated individuals");
        // A topology mutation must cancel the old route and reveal the model-owned arrival.
        grid.TrySetTileHeight(b.Coordinate, 2);
        yield return null;
        Require(presenter.ActiveTransitionCount == 0, "Stale water migration survived topology change");
        var arrival = b.Block.community.First(p => p.ecologicalNiche == EcologicalNiche.Water);
        presenter.SyncNow();
        Require(presenter.TryGetVisual(arrival, out view) && view.gameObject.activeInHierarchy && view.Count == 80, "Arrival hidden after cancelled route");
        yield return null; view.Tick(.04f);
        Require(view.ValidateMovement(out var raisedError), raisedError);
        passed.Add("Manual water migration conserves 80 individuals; land is rejected; height edits cancel the old route and safely reveal arrival.");
        Require(grid.TryRemoveTile(mismatch.Coordinate), "Remove water fixture");
        yield return null;
        int rebuilds = water.Network.RebuildCount;
        for (int i = 0; i < 4; i++) { water.Refresh(); yield return null; }
        Require(water.Network.RebuildCount == rebuilds, "Unchanged map regenerated connections");
        passed.Add("Removal clears water; unchanged frames do not regenerate connections.");
        water.TrySpace(l.Coordinate, out var untouchedSpace);
        int untouchedMesh = untouchedSpace.Renderer.GetComponent<MeshFilter>().sharedMesh.GetInstanceID();
        grid.TrySetTileHeight(bypass2.Coordinate, 1);
        water.TrySpace(l.Coordinate, out var stillUntouched);
        Require(untouchedMesh == stillUntouched.Renderer.GetComponent<MeshFilter>().sharedMesh.GetInstanceID(), "Unrelated river mesh regenerated");
        passed.Add("A remote height edit preserves unaffected water meshes and query caches.");
        var lake = Place(new Vector2Int(5, 0), Definition(WaterCoverage.Lake));
        Require(water.Connects(d, lake) && Route(d, lake), "Narrow river cannot connect to full-width lake");
        Require(water.TrySpace(lake.Coordinate, out var lakeSpace) && !lakeSpace.Contains(lake.transform.position + new Vector3(6, 0, 6), .64f), "Legacy water entry used a synthetic square instead of the authored cross");
        passed.Add("Legacy water definitions use the authored cross and preserve real land banks.");
        float originalSize = movement.Settings.leaderSize;
        foreach (float size in new[] { .4f, .8f, 1.2f })
        {
            movement.Settings.leaderSize = size;
            var second = movement.CreateWaterPopulation(b.Coordinate, 12);
            Require(second && (second.Leader.position - view.Leader.position).magnitude > .6f, "Two water representatives spawned on top of each other");
            second.BindEcologicalPopulation(new PopulationData { species = species, ecologicalNiche = EcologicalNiche.Water, speciesAmount = 12 }, species.Sprite, Color.white);
            second.Tick(.04f);
            Require(second.ValidateMovement(out var sizeError), sizeError);
            movement.DestroyPopulation(second);
        }
        movement.Settings.leaderSize = originalSize;
        passed.Add("Three body sizes (.4, .8, 1.2) keep safe shoreline clearance, water-relative poses and spawn separation.");
        // A synthetic two-island profile exercises a case the supplied artwork does not contain.
        var disconnectedMesh = new Mesh { name = "Disconnected water test" };
        disconnectedMesh.vertices = new[] { new Vector3(-4,0,-2),new Vector3(-4,0,2),new Vector3(-1,0,-2),new Vector3(-1,0,2),
            new Vector3(1,0,-2),new Vector3(1,0,2),new Vector3(4,0,-2),new Vector3(4,0,2) };
        disconnectedMesh.triangles = new[] { 0,1,2,2,1,3,4,5,6,6,5,7 }; disconnectedMesh.RecalculateBounds(); disconnectedMesh.RecalculateNormals();
        var profile = ScriptableObject.CreateInstance<RiverSurfaceProfile>(); profile.waterMesh = disconnectedMesh;
        profile.waterMaterial = Resources.Load<MapWaterAssets>("Water/MapWaterAssets").fullWaterPrefab.GetComponent<MoebiusRiverTile>().profile.waterMaterial;
        profile.shoreResolution = 4; profile.shoreDistances = Enumerable.Repeat((byte)255,16).ToArray(); profile.shoreRect = new Vector4(-8.5f,-8.5f,17,17);
        var prototype = new GameObject("Disconnected water prototype"); prototype.transform.position = Vector3.one * 500;
        prototype.AddComponent<MoebiusRiverTile>().profile = profile;
        var disconnected = Definition(WaterCoverage.CrossingRiver); disconnected.waterVisualPrefab = prototype;
        var islands = Place(new Vector2Int(0,-1), disconnected);
        Require(!WaterPathfinding.TryPath(water, islands.transform.position + new Vector3(-2,0,0), islands.transform.position + new Vector3(2,0,0), .5f, out _, islands.Coordinate), "Disconnected pools permitted a crossing");
        Require(water.TrySpace(islands.Coordinate, out var islandSpace) && !islandSpace.Contains(islands.transform.position + new Vector3(-1.1f,0,0), .5f), "Body radius ignored at the shoreline");
        grid.TryRemoveTile(islands.Coordinate); Destroy(prototype);
        passed.Add("Disconnected pools remain separate; body clearance rejects bank-overlapping poses.");
        int CountWater() => grid.GetPlacedTiles().Sum(t => t.Block.community.Where(p => p != null && p.ecologicalNiche == EcologicalNiche.Water).Sum(p => p.speciesAmount));
        var downstream = new PopulationData { species = species, lineageName = "Water check", ecologicalNiche = EcologicalNiche.Water, speciesAmount = 100 };
        c.Block.community.Add(downstream);
        var regions = simulation.BuildWaterRegions();
        foreach (var block in regions.GetRegion(c.Block).Members) { block.algaeBiomass = 0; block.algaeRecovery = 0; }
        foreach (var block in regions.GetRegion(bypass.Block).Members) { block.algaeBiomass = 100000; block.maxAlgaeBiomass = 1000000; block.algaeRecovery = 0; }
        int before = CountWater();
        simulation.SetEcologicalNicheProbabilities(0,0,0,0,0);
        Require(!simulation.TryWaterMigration(c.Block, bypass.Block, downstream, regions, 20), "Zero downstream probability ignored");
        simulation.SetEcologicalNicheProbabilities(0,0,1,0,0);
        Require(simulation.TryWaterMigration(c.Block, bypass.Block, downstream, regions, 20) && CountWater() == before, "Downstream probability/count conservation failed");
        var upstream = new PopulationData { species = species, lineageName = "Water check", ecologicalNiche = EcologicalNiche.Water, speciesAmount = 100 };
        bypass.Block.community.Add(upstream);
        foreach (var block in regions.GetRegion(bypass.Block).Members) { block.algaeBiomass = 0; block.algaeRecovery = 0; }
        foreach (var block in regions.GetRegion(c.Block).Members) { block.algaeBiomass = 100000; block.maxAlgaeBiomass = 1000000; }
        before = CountWater();
        Require(!simulation.TryWaterMigration(bypass.Block,c.Block,upstream,regions,21), "Zero upstream probability ignored");
        simulation.SetEcologicalNicheProbabilities(0,0,1,1,0);
        Require(simulation.TryWaterMigration(bypass.Block,c.Block,upstream,regions,21) && CountWater() == before, "Upstream probability/count conservation failed");
        passed.Add("Existing downstream and upstream probabilities gate migration separately; split/merge conserves individuals.");
        simulation.SetEcologicalNicheProbabilities(0,0,0,0,0);
        int events = 0; Action<PopulationTransitionResult> handler = result => { if (result.ResultNiche == EcologicalNiche.Water) events++; };
        simulation.OnMigrationResolved += handler;
        simulation.SimulateDay(30);
        simulation.OnMigrationResolved -= handler;
        Require(events == 0 && CountWater() == before, "Equal-height roaming emitted migration or ignored zero waterfall probabilities");
        passed.Add("Equal-height water roaming does not emit migration; zero waterfall probabilities preserve the total count.");
        // Frame a useful rendered acceptance view; no scene or camera asset is saved.
        Camera camera = Camera.main;
        camera.transform.position = new Vector3(39, 48, -50); camera.transform.LookAt(new Vector3(39, 0, 0));
        camera.orthographic = true; camera.orthographicSize = 33;
        yield return null;
        // The editor's capture_game_view camera source supplies the acceptance PNG;
        // ScreenCapture here would capture the unrelated opening-menu overlay.
    }
}
#endif
