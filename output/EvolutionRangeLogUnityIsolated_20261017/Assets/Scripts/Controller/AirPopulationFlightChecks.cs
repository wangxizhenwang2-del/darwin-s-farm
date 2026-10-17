#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Run in a fresh WhiteBox Play session. Fixtures are runtime-only; exit Play
// afterwards to recover the original map and ecology.
public sealed class AirPopulationFlightChecks : MonoBehaviour
{
    public static string Result { get; private set; } = "Not run";
    private readonly List<string> passed = new List<string>();
    private static readonly string Output = Path.GetFullPath("Artifacts/AirPopulationFlight");

    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter WhiteBox Play Mode first.");
        if (FindFirstObjectByType<AirPopulationFlightChecks>() != null) return;
        new GameObject("AirPopulationFlightChecks").AddComponent<AirPopulationFlightChecks>();
    }

    private IEnumerator Start()
    {
        Result = "Running";
        IEnumerator checks = Check();
        while (true)
        {
            bool next;
            try { next = checks.MoveNext(); }
            catch (Exception error)
            {
                Result = "FAILED: " + error;
                WriteReport();
                Debug.LogException(error);
                yield break;
            }
            if (!next) break;
            yield return checks.Current;
        }
        Result = "PASS: " + passed.Count + " air population integration groups.";
        WriteReport();
        Debug.Log(Result);
        Destroy(gameObject);
    }

    private IEnumerator Check()
    {
        MapGridManager grid = FindFirstObjectByType<MapGridManager>();
        Require(grid != null && grid.TryGetTile(Vector2Int.zero, out _), "Missing WhiteBox origin.");
        grid.GetComponent<SimulationTime>().Pause();
        var simulation = grid.GetComponent<SimulationController>();
        var movement = grid.GetComponent<PopulationMovementController>();
        var presenter = grid.GetComponent<PopulationVisualPresenter>();
        var economy = grid.GetComponent<PlayerEconomyController>();
        grid.GetComponent<BioenergyAccount>().CostsEnabled = false;
        grid.TryGetTile(Vector2Int.zero, out MapTileInstance source);
        var forest = AssetDatabase.LoadAssetAtPath<MapTileDefinition>("Assets/Scripts/map/data/森林.asset");
        Require(grid.TryPlaceTile(Vector2Int.right, forest, 1), "Could not place forest.");
        Require(grid.TryPlaceTile(Vector2Int.right * 2, source.Definition, 2), "Could not place water fixture.");
        grid.TryGetTile(Vector2Int.right, out MapTileInstance target);
        grid.TryGetTile(Vector2Int.right * 2, out MapTileInstance water);
        water.Block.waterCoverage = WaterCoverage.Lake;
        foreach (MapTileInstance tile in grid.GetPlacedTiles()) tile.Block.community.Clear();
        var species = AssetDatabase.LoadAssetAtPath<SpeciesData>("Assets/BiowebV46/Species/鹿飞-a61486cd.asset");
        Require(species != null, "Missing 鹿飞 asset.");
        PopulationData air = Draft(species, 80);
        source.Block.community.Add(air);
        presenter.SyncNow();
        Require(presenter.TryGetVisual(air, out WhiteboxPopulation view) && view.IsAir,
            "Air population was not rendered.");
        Require(view.DisplaySprite == species.Sprite && view.Count == 80, "Wrong air appearance/count.");
        SpriteRenderer airImage = view.GetComponentsInChildren<SpriteRenderer>().First(r => r.name == "PopulationSprite");
        Require(airImage.color.a == 1f && airImage.sharedMaterial.shader.name == "Darwin/Population Air Sprite" &&
            airImage.sharedMaterial.renderQueue <= 2500, "Air body uses a transparent sprite material.");
        Require(movement.CreatePopulation(source.Coordinate, 20, true) == null,
            "Duplicate air visual accepted on an occupied tile.");
        string identity = air.PopulationId;
        passed.Add("鹿飞 renders with its own illustration/count; one air representative per tile.");

        // Sample many complete patrols, on every supported rotation, without
        // relying on a screenshot or a random target landing inside the bounds.
        foreach (MapTileInstance tile in new[] { source, target, water })
            for (int rotation = 0; rotation < 4; rotation++)
            {
                Quaternion before = tile.transform.rotation;
                tile.transform.rotation = Quaternion.Euler(0, rotation * 90, 0);
                var flight = new PopulationFlight(tile, movement.Settings);
                flight.SetSprite(species.Sprite);
                float minimumHeight = PopulationFlight.CruiseHeight(tile, movement.Settings) -
                    movement.Settings.flightBob - 0.01f;
                Vector3 previous = flight.Position;
                float travelled = 0f;
                for (int frame = 0; frame < 3000; frame++)
                {
                    flight.Tick(0.04f, false);
                    Require(flight.Contains(flight.Position), "Patrol crossed its owning tile boundary.");
                    Vector3 local = tile.transform.InverseTransformPoint(flight.Position);
                    Require(Mathf.Abs(local.x) < MapGridManager.TileSize * 0.5f - 0.8f &&
                        Mathf.Abs(local.z) < MapGridManager.TileSize * 0.5f - 0.8f, "Missing edge clearance.");
                    Require(flight.Position.y >= minimumHeight, "Flight entered terrain.");
                    if (tile == source)
                    {
                        Camera normalCamera = Camera.main;
                        Vector3 viewport = normalCamera.WorldToViewportPoint(flight.Position);
                        Vector3 labelViewport = normalCamera.WorldToViewportPoint(flight.Position +
                            Vector3.up * (movement.Settings.leaderSize * 0.5f + movement.Settings.labelHeight));
                        Require(viewport.z > 0f && viewport.x > 0.02f && viewport.x < 0.98f &&
                            viewport.y > 0.02f && viewport.y < 0.98f && labelViewport.y < 0.98f,
                            "Low patrol or count label left the normal game camera view.");
                    }
                    travelled += Vector3.Distance(previous, flight.Position);
                    previous = flight.Position;
                }
                Require(travelled > 150f, "Flight did not patrol for 120 simulated seconds.");
                tile.transform.rotation = before;
            }
        passed.Add("Land, rotated forest and water: 12 patrol/rotation combinations, 36,000 bounded low-flight samples; 12,000 origin samples and count labels remain in the unmodified game camera view.");

        // Ground navigation being absent must not freeze flight or require a bake.
        FieldInfo navigation = typeof(MapGridManager).GetField("mapNavigation", BindingFlags.NonPublic | BindingFlags.Instance);
        object savedNavigation = navigation.GetValue(grid);
        Vector3 liveStart = view.Leader.position;
        int firstFrame = Time.frameCount;
        navigation.SetValue(grid, null);
        try
        {
            float deadline = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                Require(view.ValidateMovement(out string problem), problem);
                yield return null;
            }
            Require(Time.frameCount > firstFrame + 2 && Vector3.Distance(liveStart, view.Leader.position) > 0.5f,
                "Live flight froze without ground navigation.");
            Require(air.PopulationId == identity && air.speciesAmount == 80, "Patrol changed ecological identity/count.");
        }
        finally { navigation.SetValue(grid, savedNavigation); }
        passed.Add("Live frames advance and air keeps flying without NavMesh; ecological identity/count unchanged.");

        PopulationData rival = Draft(species, 25);
        rival.species = null; rival.speciesId = "different-air-species"; rival.lineageName = "Other air";
        target.Block.community.Add(rival);
        Require(!economy.CanPlacePopulation(target.Block, air, source.Block, out _) &&
            !economy.TryMigrate(source.Block, air, target.Block, out _), "Different air species shared a tile.");
        simulation.SetEcologicalNichesEnabled(true);
        Require(!simulation.TryAirMigration(source.Block, target.Block, air, 100),
            "Natural migration bypassed occupied sky.");
        Require(source.Block.community.Contains(air) && air.speciesAmount == 80 && rival.speciesAmount == 25,
            "Rejected migration changed populations.");
        var invalidEdits = new List<SimulationPopulationEdit>
        {
            new SimulationPopulationEdit { Draft = Draft(species, 20) },
            new SimulationPopulationEdit { Draft = rival }
        };
        Require(!simulation.ApplyCommunityEdits(source.Block, invalidEdits) && source.Block.community.Contains(air),
            "Community editing bypassed sky ownership or modified data before rejection.");
        target.Block.community.Clear();
        passed.Add("Natural migration, manual migration and community edits reject a second flying species atomically.");

        // Force only the existing daily migration probability knobs, so the
        // normal planner must commit a partial air migration and publish it.
        air.trophicLevel = 0;
        air.fitTemperature = air.fitHumidity = 50;
        source.Block.temperature = target.Block.temperature = 50;
        source.Block.humidity = target.Block.humidity = 50;
        source.Block.plantBiomass = target.Block.plantBiomass = 100000f;
        source.Block.maxPlantBiomass = target.Block.maxPlantBiomass = 100000f;
        source.Block.habitatRecovery = target.Block.habitatRecovery = 100000;
        air.energyNeed = SimulationController.CalculateEnergyNeed(air.size, air.movementAbility, air.fertility);
        air.carryingCapacity = 0;
        simulation.SetMigrationParameters(true, 1, 0.01f, 0f, 1f, 0.5f, 0);
        int migrations = 0;
        simulation.OnMigrationResolved += _ => migrations++;
        typeof(SimulationController).GetMethod("RunMigration", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(simulation, new object[] { 100 });
        Require(migrations == 1 && air.speciesAmount == 40 && target.Block.community.Count == 1 &&
            target.Block.community[0].speciesAmount == 40, "Daily planner did not migrate an air population.");
        PopulationData arrival = target.Block.community[0];
        float transitionDeadline = Time.realtimeSinceStartup + 10f;
        int crossingFrames = 0;
        yield return null;
        Require(presenter.ActiveTransitionCount > 0, "Air migration has no flight transition.");
        while (presenter.ActiveTransitionCount > 0 && Time.realtimeSinceStartup < transitionDeadline)
        {
            foreach (PopulationTransitionVisual transition in FindObjectsByType<PopulationTransitionVisual>(FindObjectsSortMode.None))
            {
                foreach (SpriteRenderer image in transition.GetComponentsInChildren<SpriteRenderer>())
                    Require(image.sharedMaterial.shader.name == "Darwin/Population Air Sprite",
                        "Migration reverted the flying animal to transparent rendering.");
                Require(transition.transform.position.y > source.transform.position.y + 0.75f,
                    "Air migration followed the ground.");
                if (transition.transform.position.x > source.transform.position.x + MapGridManager.TileSize * 0.5f)
                    crossingFrames++;
            }
            if (presenter.TryGetVisual(arrival, out WhiteboxPopulation staged))
                Require(!staged.gameObject.activeSelf, "Arrival visible before flight finished.");
            yield return null;
        }
        Require(presenter.ActiveTransitionCount == 0 && crossingFrames > 0, "Flight never crossed the boundary/reached target.");
        Require(presenter.TryGetVisual(arrival, out WhiteboxPopulation targetView) && targetView.IsAir &&
            targetView.TileId == target.Coordinate && targetView.gameObject.activeSelf && targetView.Count == 40,
            "Arrival did not resume a target-owned patrol.");
        passed.Add("Daily natural migration splits 40/40, visibly flies across the boundary, then resumes target patrol.");

        Require(economy.TryMigrate(source.Block, air, target.Block, out string mergeError), mergeError);
        yield return null;
        transitionDeadline = Time.realtimeSinceStartup + 10f;
        while (presenter.ActiveTransitionCount > 0 && Time.realtimeSinceStartup < transitionDeadline) yield return null;
        Require(target.Block.community.Count == 1 && target.Block.community[0].speciesAmount == 80 &&
            source.Block.community.Count == 0 && presenter.ActiveTransitionCount == 0,
            "Same-species migration did not merge without duplication.");
        passed.Add("Manual same-species arrival merges into the existing sky resident; total count remains 80.");

        PopulationData merged = target.Block.community[0];
        Require(economy.CanPlacePopulation(water.Block, merged, target.Block, out string waterError), waterError);
        string movedId = merged.PopulationId;
        Require(economy.TryMigrate(target.Block, merged, water.Block, out waterError), waterError);
        yield return null;
        transitionDeadline = Time.realtimeSinceStartup + 10f;
        while (presenter.ActiveTransitionCount > 0 && Time.realtimeSinceStartup < transitionDeadline) yield return null;
        PopulationData waterAir = water.Block.community.Single();
        Require(waterAir.PopulationId == movedId && waterAir.speciesAmount == 80 &&
            presenter.TryGetVisual(waterAir, out WhiteboxPopulation waterView) && waterView.IsAir &&
            waterView.TileId == water.Coordinate && waterView.gameObject.activeSelf,
            "Whole manual move over water lost identity, count or flight.");
        Require(movement.Populations.Count(p => p != null && p.IsAir) == 1,
            "Departure left an obsolete air representative.");
        passed.Add("Whole manual migration to water retains population ID/count and removes the old patrol.");

        grid.TrySetTileHeight(water.Coordinate, 2);
        yield return new WaitForSecondsRealtime(0.8f);
        Require(presenter.TryGetVisual(waterAir, out WhiteboxPopulation raised) && raised.ValidateMovement(out _),
            "Raised terrain invalidated air patrol.");
        Require(grid.TryRemoveTile(water.Coordinate), "Could not remove test tile.");
        yield return null;
        presenter.SyncNow();
        yield return null;
        Require(!movement.Populations.Any(p => p != null && p.IsAir) && presenter.ActiveTransitionCount == 0,
            "Tile removal left a flying representative or transition.");
        passed.Add("Terrain elevation updates airspace; tile removal cleans flight objects and transitions.");

        var fixture = new GameObject("CompetingAirMigrations");
        try
        {
            SimulationController competing = fixture.AddComponent<SimulationController>();
            BlockInfo left = ModelBlock(fixture.transform, "Left");
            BlockInfo middle = ModelBlock(fixture.transform, "Middle");
            BlockInfo right = ModelBlock(fixture.transform, "Right");
            left.SetNeighbors(new List<BlockInfo> { middle });
            right.SetNeighbors(new List<BlockInfo> { middle });
            middle.SetNeighbors(new List<BlockInfo> { left, right });
            PopulationData first = Draft(species, 80), second = Draft(species, 80);
            second.species = null; second.speciesId = "competing-air-species";
            second.lineageName = "Competing air";
            foreach (PopulationData p in new[] { first, second })
            {
                p.trophicLevel = 0; p.fitTemperature = p.fitHumidity = 50;
                p.energyNeed = SimulationController.CalculateEnergyNeed(p.size, p.movementAbility, p.fertility);
            }
            left.community.Add(first); right.community.Add(second);
            competing.SetBlocks(new List<BlockInfo> { left, middle, right });
            first.carryingCapacity = second.carryingCapacity = 0;
            competing.SetEcologicalNichesEnabled(true);
            competing.SetMigrationParameters(true, 1, 0.01f, 0f, 1f, 0.5f, 0);
            typeof(SimulationController).GetMethod("RunMigration", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(competing, new object[] { 100 });
            Require(middle.community.Count == 1 && middle.community[0].speciesAmount == 40 &&
                left.community.Sum(p => p.speciesAmount) + middle.community.Sum(p => p.speciesAmount) +
                right.community.Sum(p => p.speciesAmount) == 160,
                "Competing same-day arrivals bypassed the single air species reservation or lost individuals.");
        }
        finally { Destroy(fixture); }
        passed.Add("Competing natural migrations reserve one sky slot; the other species remains home and all 160 individuals are conserved.");
    }

    private static BlockInfo ModelBlock(Transform parent, string name)
    {
        var root = new GameObject(name); root.transform.SetParent(parent);
        var block = root.AddComponent<BlockInfo>();
        block.community = new List<PopulationData>();
        block.waterCoverage = WaterCoverage.Land;
        block.temperature = block.humidity = 50;
        block.plantBiomass = block.maxPlantBiomass = 100000f;
        block.habitatRecovery = 100000;
        return block;
    }

    private static PopulationData Draft(SpeciesData species, int count) => new PopulationData
    {
        species = species, speciesId = species.SpeciesId, lineageName = species.SpeciesName,
        ecologicalNiche = EcologicalNiche.Air, speciesAmount = count,
        size = 55, movementAbility = 80, fertility = 40, fitTemperature = 40,
        fitHumidity = 45, habitatNiche = 100, trophicLevel = 2, trophicLevelInitialized = true
    };

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private void WriteReport()
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "integration-results.md"),
            "# Air population flight\n\n" + Result + "\n\n" +
            string.Join("\n", passed.Select(line => "- " + line)) + "\n");
    }
}
#endif
