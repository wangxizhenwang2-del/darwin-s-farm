using DarwinFarm.Environment;
using System;
using System.Collections.Generic;
using System.Linq;

static class Program
{
    static int assertions;
    static readonly GridPosition Origin = new GridPosition(0, 0);
    static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }
    static void Near(double expected, double actual, string message)
    { Check(Math.Abs(expected - actual) < .00001, message + $": expected {expected}, got {actual}"); }
    static EnvironmentSnapshot Read(EnvironmentWorld world, int x = 0)
    { Check(world.TryRead(new GridPosition(x, 0), out var state), "missing tile"); return state; }
    static TopologyNode[] Line(params (int x, int z, TerrainKind kind)[] cells)
    {
        var positions = cells.Select(c => new GridPosition(c.x, 0)).ToHashSet();
        return cells.Select(c => new TopologyNode(new GridPosition(c.x, 0), c.z, c.kind,
            new[] { new GridPosition(c.x - 1, 0), new GridPosition(c.x + 1, 0) }.Where(positions.Contains))).ToArray();
    }
    static EnvironmentWorld Single(TerrainKind kind)
    {
        var world = new EnvironmentWorld();
        world.SynchronizeTopology(Line((0, EnvironmentRules.Defaults(kind).Elevation, kind))); return world;
    }
    static void Climate(EnvironmentWorld w, EnvironmentAttribute attr, int amount)
    { Check(w.TrySetClimate(Origin, attr, amount, out var error), error); }
    // Independent, declarative specification. Each legal combination must hit exactly one row.
    static IEnumerable<TerrainKind> MatchingRows(int z, int t, int h, int r)
    {
        if (z == 0 && t <= 14) yield return TerrainKind.Desert;
        if (z == 0 && t >= 15 && t <= 34 && h <= 34) yield return TerrainKind.Desert;
        if (z <= 1 && t >= 35 && h <= 34) yield return TerrainKind.Desert;
        if (z == 0 && t >= 15 && t <= 34 && h >= 35) yield return TerrainKind.Forest;
        if (z == 1 && t <= 34 && h >= 66 && r > 100000) yield return TerrainKind.Forest;
        if (z <= 1 && t >= 35 && t <= 49 && h >= 35 && r > 100000) yield return TerrainKind.Forest;
        if (z <= 1 && t >= 50 && t <= 65 && h >= 35 && r > 100000) yield return TerrainKind.Forest;
        if (z <= 1 && t >= 35 && h >= 35 && r <= 100000 &&
            !(z == 1 && t <= 65 && h <= 65 && r > 50000)) yield return TerrainKind.Grassland;
        if (z <= 1 && t >= 66 && h >= 35 && r > 100000) yield return TerrainKind.Rainforest;
        if (z == 1 && t <= 34 && h <= 65) yield return TerrainKind.Highland;
        if (z == 1 && t >= 35 && t <= 65 && h >= 35 && h <= 65 && r > 50000 && r <= 100000) yield return TerrainKind.Highland;
        if (z == 2 && t <= 34 && h <= 34 && r > 50000) yield return TerrainKind.Highland;
        if (z == 2 && t <= 34 && h >= 35 && r > 100000) yield return TerrainKind.Highland;
        if (z == 2 && t >= 35 && t <= 65) yield return TerrainKind.Highland;
        if (z == 2 && t >= 66 && r > 50000) yield return TerrainKind.Highland;
        if (z == 1 && t <= 34 && h >= 66 && r <= 100000) yield return TerrainKind.Tundra;
        if (z == 2 && t <= 34 && h >= 35 && r > 50000 && r <= 100000) yield return TerrainKind.Tundra;
        if (z == 2 && t <= 34 && r <= 50000) yield return TerrainKind.SnowMountain;
        if (z == 2 && t >= 66 && r <= 50000) yield return TerrainKind.Volcano;
    }
    static void Classification()
    {
        int[] recoveries = { 0, 1, 24999, 25000, 49999, 50000, 50001, 75000, 99999, 100000, 100001, 125000, 149999, 150000 };
        foreach (int z in new[] { 0, 1, 2 })
        for (int t = 0; t <= 100; t++)
        for (int h = 0; h <= 100; h++)
        foreach (int r in recoveries)
        {
            var hits = MatchingRows(z, t, h, r).ToArray();
            Check(hits.Length == 1, $"range gap/overlap {z},{t},{h},{r}");
            Check(hits[0] == EnvironmentRules.Classify(z, t, h, r), "classifier differs from spec");
        }
        foreach (TerrainKind kind in Enum.GetValues(typeof(TerrainKind)))
        {
            var d = EnvironmentRules.Defaults(kind); var w = Single(kind); var before = Read(w);
            Check(before.Terrain == kind, "deployment terrain mismatch");
            w.AdvanceToDay(300); var after = Read(w);
            Near(d.Temperature, after.Temperature.Value, "idle temperature");
            Near(d.Humidity, after.Humidity.Value, "idle humidity");
            Near(d.Recovery, after.Recovery.Value, "idle recovery");
            Check(after.Terrain == kind, "idle terrain drift");
        }
        Check(EnvironmentRules.Classify(1, 50, 50, 100000) == TerrainKind.Highland, "medium altitude highland default");
        Check(EnvironmentRules.Classify(0, 83, 50, 125000) == TerrainKind.Rainforest, "hot moderately wet rich plants");
    }
    static void TerrainGraph()
    {
        var kinds = (TerrainKind[])Enum.GetValues(typeof(TerrainKind));
        Check(kinds.Length == 8, "terrain graph expects eight kinds");
        int directedEdges = 0;
        var visited = new HashSet<TerrainKind> { TerrainKind.Grassland };
        var pending = new Queue<TerrainKind>();
        pending.Enqueue(TerrainKind.Grassland);
        foreach (TerrainKind kind in kinds)
        {
            var neighbors = TerrainTransitionGraph.Neighbors(kind);
            Check(neighbors.Count >= 2 && neighbors.Count <= 4, "terrain degree must be 2-4: " + kind);
            Check(neighbors.Distinct().Count() == neighbors.Count && !neighbors.Contains(kind), "invalid terrain neighbor: " + kind);
            foreach (TerrainKind neighbor in neighbors)
            {
                Check(TerrainTransitionGraph.AreAdjacent(neighbor, kind), "terrain edge must be symmetric");
                directedEdges++;
            }
        }
        while (pending.Count > 0)
            foreach (TerrainKind neighbor in TerrainTransitionGraph.Neighbors(pending.Dequeue()))
                if (visited.Add(neighbor)) pending.Enqueue(neighbor);
        Check(directedEdges == 26 && visited.Count == kinds.Length, "terrain graph must have 13 connected edges");
        Check(!TerrainTransitionGraph.AreAdjacent(TerrainKind.Desert, TerrainKind.Tundra), "graph must not invent edges");

        // The same terrain can exist at different elevations; the graph has no Z coordinate.
        Check(EnvironmentRules.Classify(0, 68, 68, 100000) == TerrainKind.Grassland &&
              EnvironmentRules.Classify(1, 68, 68, 100000) == TerrainKind.Grassland,
            "grassland can span elevations");
        Check(EnvironmentRules.Classify(0, 68, 17, 50000) == TerrainKind.Desert &&
              EnvironmentRules.Classify(1, 68, 17, 50000) == TerrainKind.Desert,
            "desert can span elevations");
    }
    static void Timelines()
    {
        var w = Single(TerrainKind.Highland); Climate(w, EnvironmentAttribute.Temperature, 25);
        Near(50, Read(w).Temperature.Value, "climate should not jump");
        w.AdvanceToDay(10); Near(60, Read(w).Temperature.Value, "linear entry");
        w.AdvanceToDay(25); Near(75, Read(w).Temperature.Value, "day25 target");
        Check(Read(w).Temperature.Phase == EffectPhase.Holding, "hold begins day25");
        w.AdvanceToDay(124); Near(75, Read(w).Temperature.Value, "100day hold");
        w.AdvanceToDay(125); Check(Read(w).Temperature.Phase == EffectPhase.Returning, "return begins day125");
        w.AdvanceToDay(150); Near(50, Read(w).Temperature.Value, "day150 default");
        Check(!Read(w).HasLocalClimate, "climate ends day150");
        Climate(w, EnvironmentAttribute.Temperature, 50); w.AdvanceToDay(160);
        Near(70, Read(w).Temperature.Value, "replacement entry");
        Climate(w, EnvironmentAttribute.Temperature, 25);
        Near(70, Read(w).Temperature.Value, "replace starts actual");
        w.AdvanceToDay(185); Near(75, Read(w).Temperature.Value, "replacement target");
        Check(w.TryCancelClimate(Origin, EnvironmentAttribute.Temperature, out _), "cancel held climate");
        w.AdvanceToDay(195); Near(65, Read(w).Temperature.Value, "early return slope");
        Climate(w, EnvironmentAttribute.Temperature, 50); w.AdvanceToDay(220);
        Near(100, Read(w).Temperature.Value, "replace during return");
        var clamped = Single(TerrainKind.Rainforest); Climate(clamped, EnvironmentAttribute.Humidity, 50);
        clamped.AdvanceToDay(25); Near(100, Read(clamped).Humidity.Value, "clamp at command creation");
        Check(!clamped.TrySetClimate(Origin, EnvironmentAttribute.Temperature, double.NaN, out _), "reject NaN");
        Check(!clamped.TrySetClimate(Origin, EnvironmentAttribute.Temperature, 5, out _), "reject arbitrary strength");
        var saved = Read(w); w.AdvanceToDay(230); Near(100, saved.Temperature.Value, "snapshot immutable");
        w.RebaseClock(0); w.AdvanceToDay(1);
        Near(100, Read(w).Temperature.Value, "clock reset preserves operation state");
    }
    static void RecoveryAndStock()
    {
        var w = Single(TerrainKind.Grassland);
        Check(w.TrySetRecovery(Origin, 25000, out _), "recovery command");
        var s = Read(w); Near(125000, s.Recovery.Value, "immediate recovery");
        Check(s.Terrain == TerrainKind.Rainforest, "recovery transformation");
        Near(125000, s.Recovery.Target, "return uses converted default");
        w.AdvanceToDay(100); Check(Read(w).Terrain == TerrainKind.Rainforest, "new terrain persists");
        Check(w.TrySetRecovery(Origin, -50000, out _), "recovery replacement");
        Near(75000, Read(w).Recovery.Value, "negative recovery immediate");
        Near(100000, Read(w).Recovery.Target, "new grassland return");
        w.AdvanceToDay(150); Near(87500, Read(w).Recovery.Value, "100day linear restore");
        Check(w.TrySetRecovery(Origin, 50000, out _), "interrupt recovery");
        Near(150000, Read(w).Recovery.Value, "replacement uses default not stacking");
        w.AdvanceToDay(250); Near(125000, Read(w).Recovery.Value, "replaced recovery return");
        Near(300000, EnvironmentRules.ChangeStock(50000, .25), "addition based on cap");
        Near(37500, EnvironmentRules.ChangeStock(50000, -.25), "reduction based on stock");
        Near(1000000, EnvironmentRules.ChangeStock(900000, .5), "stock cap");
        Near(0, EnvironmentRules.ChangeStock(0, -.5), "zero stock");
    }
    static void TopologyValidation()
    {
        var world = Single(TerrainKind.Grassland);
        int count = world.ReadAll().Count;
        try
        {
            world.SynchronizeTopology(new[] { new TopologyNode(Origin, 0,
                TerrainKind.Grassland, new[] { new GridPosition(1, 1) }) });
            Check(false, "invalid graph accepted");
        }
        catch (ArgumentException)
        { Check(world.ReadAll().Count == count, "invalid graph must not partially mutate"); }
    }
    static TopologyNode[] MixedLine(params bool[] water)
    {
        var result = new TopologyNode[water.Length];
        for (int i = 0; i < water.Length; i++)
        {
            var neighbors = new List<GridPosition>();
            if (i > 0) neighbors.Add(new GridPosition(i - 1, 0));
            if (i + 1 < water.Length) neighbors.Add(new GridPosition(i + 1, 0));
            result[i] = new TopologyNode(new GridPosition(i, 0), 0,
                TerrainKind.Grassland, neighbors, water[i]);
        }
        return result;
    }
    static void TechnologyTools()
    {
        Check(EnvironmentTechnologyCatalog.All.Length == 5, "player has five technology tools");
        foreach (EnvironmentTechnologyKind kind in EnvironmentTechnologyCatalog.All)
            Check(!string.IsNullOrWhiteSpace(EnvironmentTechnologyCatalog.Name(kind)),
                "technology needs a player-facing name");
        Check(!EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
            EnvironmentTechnologyKind.ElevationChange, 2)), "height has no large tier");
        Check(EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
            EnvironmentTechnologyKind.ElevationChange, -1)), "height accepts downward tier");
        foreach (int tier in new[] { 2, 1, -1, -2 })
        {
            Check(EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
                EnvironmentTechnologyKind.TemperatureChange, tier)), "temperature tier");
            Check(EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
                EnvironmentTechnologyKind.HumidityChange, tier)), "humidity tier");
            Check(EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
                EnvironmentTechnologyKind.PlantStockChange, tier)), "stock tier");
            Check(EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
                EnvironmentTechnologyKind.PlantRecoveryChange, tier)), "recovery tier");
            Check(EnvironmentTechnologyCatalog.SignedClimate(new EnvironmentTechnologyChoice(
                EnvironmentTechnologyKind.TemperatureChange, tier)) == tier * 25,
                "signed climate tier");
        }
        Check(!EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
            EnvironmentTechnologyKind.PlantStockChange, 0)), "stock rejects zero tier");
        Check(!EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
            EnvironmentTechnologyKind.PlantRecoveryChange, 0)), "recovery rejects zero tier");
        var w = new EnvironmentWorld();
        w.SynchronizeTopology(MixedLine(false, true, false));
        Check(!w.TrySetClimate(new GridPosition(1, 0), EnvironmentAttribute.Temperature,
            25, out _), "water rejects climate technology");

        var aquatic = new EnvironmentWorld();
        aquatic.SynchronizeTopology(MixedLine(true, true));
        Near(75000, Read(aquatic).Recovery.Value, "water default recovery");
        var members = new[] { new GridPosition(0, 0), new GridPosition(1, 0) };
        Check(aquatic.TrySetRecoveryBatch(members, 25000, false, out _),
            "whole-water catalyst");
        Near(100000, Read(aquatic).Recovery.Value, "water catalyst per cell");
        Near(100000, Read(aquatic, 1).Recovery.Value, "second water member catalyst");
        Check(!aquatic.TrySetRecoveryBatch(new[] { Origin, new GridPosition(9, 0) },
            50000, false, out _), "invalid water batch rejected");
        Near(100000, Read(aquatic).Recovery.Value, "failed batch leaves first member unchanged");
        Check(aquatic.TrySetRecoveryBatch(members, .5, true, out _),
            "suppression replaces catalyst");
        Near(50000, Read(aquatic).Recovery.Value, "suppression subtracts 50000 from actual R");
        Check(Read(aquatic).Recovery.Phase == EffectPhase.Holding,
            "water plant operation holds before return");
        Check(aquatic.TryCancelRecoveryBatch(members, out _), "cancel entire water R timeline");
        Check(!aquatic.TryCancelRecoveryBatch(members, out _),
            "repeated cancellation cannot indefinitely extend water recovery");
        aquatic.AdvanceToDay(50);
        Near(75000, Read(aquatic).Recovery.Value, "water R returns in 50 days");
        Near(425000, EnvironmentRules.ChangeStock(50000, 1500000, .25),
            "water seeding uses water capacity");
        Near(37500, EnvironmentRules.ChangeStock(50000, 1500000, -.25),
            "water suppression uses actual stock");
        aquatic.ReconcileWaterRecovery(members);
        Check(!Read(aquatic).HasRecoveryCommand &&
            Read(aquatic).Recovery.Phase == EffectPhase.None,
            "regroup clears water R timeline at actual value");
    }
    static void Main()
    {
        Classification(); TerrainGraph(); NewToolRules(); ConversionScenarios(); StabilitySweep(); CombinationSweep(); TopologyValidation(); TechnologyTools();
        Console.WriteLine($"PASS: {assertions:N0} environment assertions (ranges, terrain graph, tool slots, timelines, causality, topology, refunds)");
    }

    static void NewToolRules()
    {
        var climate = Single(TerrainKind.Grassland);
        Climate(climate, EnvironmentAttribute.Temperature, 25);
        climate.AdvanceToDay(25);
        Near(93, Read(climate).Temperature.Value, "25-day climate entry");
        Check(Read(climate).Temperature.Phase == EffectPhase.Holding, "climate holds after entry");
        climate.AdvanceToDay(174);
        Near(93, Read(climate).Temperature.Value, "150-day hold");
        climate.AdvanceToDay(175);
        Check(Read(climate).Temperature.Phase == EffectPhase.Returning, "return starts day175");
        climate.AdvanceToDay(325);
        Near(68, Read(climate).Temperature.Value, "150-day climate return");
        Check(!Read(climate).HasLocalTemperature, "climate slot released");

        var plant = Single(TerrainKind.Grassland);
        Check(plant.TrySetRecovery(Origin, 25000, out _), "plant starts");
        Near(125000, Read(plant).Recovery.Value, "plant recovery changes immediately");
        plant.AdvanceToDay(50);
        Near(125000, Read(plant).Recovery.Value, "plant holds for 50 days");
        Check(Read(plant).Recovery.Phase == EffectPhase.Returning, "plant return starts day50");
        plant.AdvanceToDay(100);
        Near(115750, Read(plant).Recovery.Value, "plant returns to the converted climate potential in 50 days");
        Check(!Read(plant).HasRecoveryCommand, "plant slot released");
        Check(plant.TrySetRecovery(Origin, 25000, out _), "plant reapplied");
        Check(plant.TrySetRecovery(Origin, -50000, out _), "plant switches tier");
        Near(90750, Read(plant).Recovery.Value, "reapplication uses current recovery");
        Near(125000, EnvironmentRules.MultiplyStock(100000, 1000000, .25),
            "positive tier multiplies current stock");
        Near(62500, EnvironmentRules.MultiplyStock(125000, 1000000, -.5),
            "negative tier multiplies current stock");

        var slots = Single(TerrainKind.Grassland);
        Climate(slots, EnvironmentAttribute.Temperature, 25);
        Climate(slots, EnvironmentAttribute.Humidity, -25);
        Check(slots.TrySetRecovery(Origin, 25000, out _), "third tool deploys");
        Check(Read(slots).Temperature.Phase == EffectPhase.Returning &&
            Read(slots).Humidity.Phase == EffectPhase.Entering &&
            Read(slots).Recovery.Phase == EffectPhase.Holding,
            "third tool displaces oldest; recovery tail takes no slot");
        Climate(slots, EnvironmentAttribute.Temperature, -25);
        Check(Read(slots).Humidity.Phase == EffectPhase.Returning &&
            Read(slots).Temperature.Phase == EffectPhase.Entering,
            "reapplying climate restarts from current and displaces oldest");

        var dry = Single(TerrainKind.Grassland);
        Climate(dry, EnvironmentAttribute.Humidity, -50);
        dry.AdvanceToDay(25);
        Check(Read(dry).Recovery.Value < 100000, "dry climate lowers potential recovery");
        dry.AdvanceToDay(140);
        Check(Read(dry).Terrain == TerrainKind.Desert && Read(dry).Recovery.Value < 100000,
            "climate drives recovery then terrain conversion within 240 days");
        dry.AdvanceToDay(480);
        Check(Read(dry).Terrain == TerrainKind.Desert &&
            Read(dry).NaturalDefaults.Recovery == EnvironmentRules.Defaults(TerrainKind.Desert).Recovery,
            "converted terrain keeps its own recovery baseline after technology ends");
    }

    static void ConversionScenarios()
    {
        var climate = Single(TerrainKind.Grassland);
        Climate(climate, EnvironmentAttribute.Humidity, -50);
        int desertDay = 0;
        for (int day = 1; day <= 240; day++)
        {
            climate.AdvanceToDay(day);
            if (desertDay == 0 && Read(climate).Terrain == TerrainKind.Desert) desertDay = day;
        }
        Check(desertDay > 0, "drying changes grassland to desert within the opening 240 days");
        climate.AdvanceToDay(480);
        var dry = Read(climate);
        Check(dry.Terrain == TerrainKind.Desert && dry.Defaults.Humidity == 17 &&
            dry.NaturalDefaults.Humidity == 17, "converted desert persists and owns its defaults");
        Near(17, dry.Humidity.Value, "humidity returns to the desert default");
        Climate(climate, EnvironmentAttribute.Humidity, 50);
        int grassDay = 0;
        for (int day = 481; day <= 720; day++)
        {
            climate.AdvanceToDay(day);
            if (grassDay == 0 && Read(climate).Terrain == TerrainKind.Grassland) grassDay = day;
        }
        Check(grassDay > 0, "reverse humidity tool restores grassland");
        climate.AdvanceToDay(1050);
        Check(Read(climate).Terrain == TerrainKind.Grassland &&
            Read(climate).NaturalDefaults.Humidity == 68,
            "reversed grassland remains stable after its command expires");

        var plant = Single(TerrainKind.Grassland);
        Check(plant.TrySetRecovery(Origin, 25000, out _), "plant conversion begins");
        Check(Read(plant).Terrain == TerrainKind.Rainforest &&
            Read(plant).NaturalDefaults.Recovery == 125000,
            "plant production converts and commits the rainforest baseline immediately");
        Near(68, Read(plant).Temperature.Value, "plant conversion does not change untouched temperature");
        Near(68, Read(plant).Humidity.Value, "plant conversion does not change untouched humidity");
        plant.AdvanceToDay(200);
        Check(Read(plant).Terrain == TerrainKind.Rainforest &&
            Read(plant).Recovery.Value > 100000,
            "plant-only conversion remains after its 50-day return");
        Near(68, Read(plant).Temperature.Value,
            "new terrain defaults do not move untouched temperature");
        Near(68, Read(plant).Humidity.Value,
            "new terrain defaults do not move untouched humidity");
        Check(plant.TrySetRecovery(Origin, -50000, out _), "reverse plant operation");
        Check(Read(plant).Terrain == TerrainKind.Grassland,
            "reverse plant operation can undo a plant-driven conversion");
        plant.AdvanceToDay(400);
        Check(Read(plant).Terrain == TerrainKind.Grassland,
            "grassland persists after reverse plant command ends");

        var parallel = Single(TerrainKind.Grassland);
        Check(parallel.TrySetRecovery(Origin, 25000, out _), "parallel plant command");
        double before = Read(parallel).Recovery.Value;
        Climate(parallel, EnvironmentAttribute.Humidity, -25);
        parallel.AdvanceToDay(10);
        Check(Read(parallel).Recovery.Value < before &&
            Read(parallel).Recovery.Phase == EffectPhase.Holding,
            "climate changes production while the plant command holds");

        var entering = Single(TerrainKind.Grassland);
        Climate(entering, EnvironmentAttribute.Humidity, -50);
        entering.AdvanceToDay(10);
        double oldTarget = Read(entering).Humidity.Target;
        int remaining = Read(entering).Humidity.RemainingDays;
        Check(entering.TrySetRecovery(Origin, 50000, out _), "terrain changes during climate entry");
        Check(Read(entering).Terrain != TerrainKind.Grassland &&
            Read(entering).Humidity.Target == oldTarget &&
            Read(entering).Humidity.RemainingDays == remaining,
            "conversion preserves an entering command's target and time");

        var returning = Single(TerrainKind.Grassland);
        Climate(returning, EnvironmentAttribute.Temperature, 25);
        returning.AdvanceToDay(180);
        Check(Read(returning).Temperature.Phase == EffectPhase.Returning,
            "temperature is returning before terrain switch");
        Check(returning.TrySetRecovery(Origin, 50000, out _),
            "plant operation converts terrain during temperature return");
        Check(Read(returning).Terrain == TerrainKind.Rainforest &&
            Read(returning).Temperature.RemainingDays == 150 &&
            Read(returning).Temperature.Target == 75,
            "terrain change restarts climate return toward the new default");

        Console.WriteLine($"SCENARIOS: grass→desert day {desertDay}; desert→grass day {grassDay}; plant conversion and parallel climate verified");
    }

    static void StabilitySweep()
    {
        int scenarios = 0, repeatedChanges = 0;
        foreach (TerrainKind kind in Enum.GetValues(typeof(TerrainKind)))
        foreach (EnvironmentAttribute attribute in new[]
            { EnvironmentAttribute.Temperature, EnvironmentAttribute.Humidity, EnvironmentAttribute.Recovery })
        foreach (int strength in new[] { -50, -25, 25, 50 })
        {
            var world = Single(kind);
            bool accepted = attribute == EnvironmentAttribute.Recovery
                ? world.TrySetRecovery(Origin, strength * 1000, out _)
                : world.TrySetClimate(Origin, attribute, strength, out _);
            Check(accepted, "sweep command accepted");
            TerrainKind settled = kind;
            TerrainKind previous = kind;
            int changes = 0;
            string timeline = kind.ToString();
            bool returnedToOriginal = false;
            for (int day = 1; day <= 600; day++)
            {
                world.AdvanceToDay(day);
                TerrainKind current = Read(world).Terrain;
                if (current != previous)
                {
                    changes++;
                    if (changes > 1 && current == kind) returnedToOriginal = true;
                    previous = current;
                    timeline += "→" + current + "@" + day;
                }
                if (day == 400) settled = Read(world).Terrain;
            }
            Check(!returnedToOriginal,
                "converted terrain returned to the original without a reverse command: " + timeline);
            if (changes > 1)
            {
                repeatedChanges++;
                if (repeatedChanges <= 12)
                    Console.WriteLine("REPEATED: " + kind + " " + attribute + " " + strength +
                        " changed " + changes + " times: " + timeline);
            }
            Check(Read(world).Terrain == settled,
                "terrain changed without player input after commands expired: " +
                kind + " " + attribute + " " + strength + " " + settled + "→" + Read(world).Terrain);
            scenarios++;
        }
        Console.WriteLine("STABILITY SWEEP: " + scenarios + " one-tool cases through day 600, " +
            repeatedChanges + " with multiple terrain changes");
    }

    static void CombinationSweep()
    {
        int scenarios = 0;
        foreach (TerrainKind kind in Enum.GetValues(typeof(TerrainKind)))
        for (int variant = 0; variant < 3; variant++)
        {
            var world = Single(kind);
            if (variant == 0)
            {
                Climate(world, EnvironmentAttribute.Temperature, 50);
                Climate(world, EnvironmentAttribute.Humidity, -50);
            }
            else if (variant == 1)
            {
                Climate(world, EnvironmentAttribute.Temperature, -50);
                Check(world.TrySetRecovery(Origin, 50000, out _), "temperature and plant combination");
            }
            else
            {
                Climate(world, EnvironmentAttribute.Humidity, -50);
                Check(world.TrySetRecovery(Origin, -50000, out _), "humidity and plant combination");
            }
            TerrainKind day400 = kind;
            for (int day = 1; day <= 600; day++)
            {
                world.AdvanceToDay(day);
                if (day == 400) day400 = Read(world).Terrain;
            }
            Check(Read(world).Terrain == day400,
                "combined effects changed terrain after settling: " + kind + " variant " + variant);
            scenarios++;
        }
        Console.WriteLine("COMBINATION SWEEP: " + scenarios + " two-tool cases through day 600");
    }
}
