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
    static long Wind(EnvironmentWorld w, int source, int t = 20, int h = 20, EnvironmentPayment payment = null)
    { Check(w.TryDeployMonsoon(new GridPosition(source, 0), t, h, payment, out var id, out var error), error); return id; }

    // Independent, declarative specification. Each legal combination must hit exactly one row.
    static IEnumerable<TerrainKind> MatchingRows(int z, int t, int h, int r)
    {
        if (z == 0 && t <= 14) yield return TerrainKind.Desert;
        if (z == 0 && t >= 15 && t <= 34 && h <= 34) yield return TerrainKind.Desert;
        if (z <= 1 && t >= 35 && h <= 34 && r <= 50000) yield return TerrainKind.Desert;
        if (z == 0 && t >= 15 && t <= 34 && h >= 35) yield return TerrainKind.Forest;
        if (z == 1 && t <= 34 && h >= 35 && r > 100000) yield return TerrainKind.Forest;
        if (z <= 1 && t >= 35 && t <= 49 && h >= 35 && r > 100000) yield return TerrainKind.Forest;
        if (z <= 1 && t >= 50 && t <= 65 && h >= 35 && h <= 65 && r > 100000) yield return TerrainKind.Forest;
        if (z <= 1 && t >= 35 && h <= 34 && r > 50000) yield return TerrainKind.Grassland;
        if (z <= 1 && t >= 35 && h >= 35 && r <= 100000 &&
            !(z == 1 && t <= 65 && h <= 65 && r > 50000)) yield return TerrainKind.Grassland;
        if (z <= 1 && t >= 50 && h >= 66 && r > 100000) yield return TerrainKind.Rainforest;
        if (z <= 1 && t >= 66 && h >= 35 && h <= 65 && r > 100000) yield return TerrainKind.Rainforest;
        if (z == 1 && t <= 34 && h <= 34) yield return TerrainKind.Highland;
        if (z == 1 && t >= 35 && t <= 65 && h >= 35 && h <= 65 && r > 50000 && r <= 100000) yield return TerrainKind.Highland;
        if (z == 2 && t <= 34 && h <= 34 && r > 50000) yield return TerrainKind.Highland;
        if (z == 2 && t <= 34 && h >= 35 && r > 100000) yield return TerrainKind.Highland;
        if (z == 2 && t >= 35 && t <= 65) yield return TerrainKind.Highland;
        if (z == 2 && t >= 66 && r > 50000) yield return TerrainKind.Highland;
        if (z == 1 && t <= 34 && h >= 35 && r <= 100000) yield return TerrainKind.Tundra;
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
    static void WindMasking()
    {
        var w = Single(TerrainKind.Grassland); long id = Wind(w, 0);
        w.AdvanceToDay(25); Near(88, Read(w).Temperature.Value, "wind entry");
        w.AdvanceToDay(200); Near(88, Read(w).Temperature.Value, "wind permanent");
        Climate(w, EnvironmentAttribute.Temperature, -25);
        Check(Read(w).HasMonsoon && !Read(w).MonsoonApplied, "masked wind retains membership");
        w.AdvanceToDay(225); Near(68, Read(w).Humidity.Value, "temperature masks both wind fields");
        w.AdvanceToDay(350); Check(Read(w).MonsoonApplied, "wind resumes after local");
        Near(88, Read(w).Temperature.Value, "resume wind without stacking");
        Climate(w, EnvironmentAttribute.Temperature, -25); w.AdvanceToDay(375);
        Check(w.TryCancelClimate(Origin, EnvironmentAttribute.Temperature, out _), "local return");
        w.AdvanceToDay(380); double current = Read(w).Temperature.Value;
        Check(w.TryCancelMonsoon(id, out _), "cancel wind during local return");
        Near(current, Read(w).Temperature.Value, "wind cancel should not jump return");
        Near(Read(w).Defaults.Temperature, Read(w).Temperature.Target, "no stale wind target");
        w.AdvanceToDay(405); Check(!Read(w).HasLocalClimate && !Read(w).HasMonsoon, "cancel completes");
        Near(Read(w).Defaults.Temperature, Read(w).Temperature.Value, "default after cancelled wind");
        Check(w.ReadSettlements().Single().RefundAmount == 0, "manual cancellation no refund");
        var two = Single(TerrainKind.Grassland); Wind(two, 0);
        Climate(two, EnvironmentAttribute.Temperature, 25); Climate(two, EnvironmentAttribute.Humidity, -25);
        two.AdvanceToDay(25);
        Near(93, Read(two).Temperature.Value, "temperature and humidity coexist");
        Near(43, Read(two).Humidity.Value, "humidity independent target");
        two.AdvanceToDay(150);
        Near(88, Read(two).Temperature.Value, "both return to wind together");
        Near(88, Read(two).Humidity.Value, "second field returns to wind together");
    }
    static void TopologyAndRefunds()
    {
        var w = new EnvironmentWorld();
        w.SynchronizeTopology(Line((0, 0, TerrainKind.Grassland), (1, 1, TerrainKind.Forest), (2, 0, TerrainKind.Grassland)));
        long a = Wind(w, 0, payment: new EnvironmentPayment("a", 37.5m));
        long b = Wind(w, 2, payment: new EnvironmentPayment("b", 62m));
        Check(w.ReadMonsoons().All(m => m.Area.Count == 1), "same elevation contiguous only");
        w.SynchronizeTopology(Line((0, 0, TerrainKind.Grassland), (1, 0, TerrainKind.Forest), (2, 0, TerrainKind.Grassland)));
        Check(w.ReadMonsoons().Count == 0, "non-source height merges overlapping groups");
        var settlements = w.ReadSettlements();
        Check(settlements.Count == 2 && settlements.All(s => s.Reason == MonsoonExitReason.Overlap), "overlap settles both");
        Check(settlements.Sum(s => s.RefundAmount) == 99.5m, "refund exact paid amount");
        w.SynchronizeTopology(Line((0, 0, TerrainKind.Grassland), (1, 0, TerrainKind.Forest), (2, 0, TerrainKind.Grassland)));
        Check(w.ReadSettlements().Count == 2, "settlement not duplicated");
        Check(w.AcknowledgeSettlement(a) && !w.AcknowledgeSettlement(a), "ack exactly once");
        Check(!w.TryDeployMonsoon(Origin, 20, 20, new EnvironmentPayment("a", 37.5m), out _, out _), "receipt reuse rejected");
        Wind(w, 0); Wind(w, 2); Check(w.ReadMonsoons().Count == 0, "deployment overlap immediately cancels both");
        var global = new EnvironmentWorld();
        global.SynchronizeTopology(Line((0, 0, TerrainKind.Grassland), (1, 1, TerrainKind.Forest), (2, 0, TerrainKind.Grassland)));
        Wind(global, 0, payment: new EnvironmentPayment("g1", 10));
        Wind(global, 2, payment: new EnvironmentPayment("g2", 20));
        global.SynchronizeTopology(Line((0, 1, TerrainKind.Grassland), (1, 1, TerrainKind.Forest), (2, 0, TerrainKind.Grassland)));
        Check(global.ReadMonsoons().Count == 0, "changing any source cancels all");
        Check(global.ReadSettlements().All(s => s.Reason == MonsoonExitReason.SourceChanged && s.RefundAmount == 0), "source change no refund");
        var expansion = Single(TerrainKind.Grassland); Wind(expansion, 0);
        expansion.SynchronizeTopology(Line((0, 0, TerrainKind.Grassland), (1, 0, TerrainKind.Grassland)));
        Check(expansion.ReadMonsoons().Count == 0, "source adjacency changed by placement cancels all");
        var ext = new EnvironmentWorld();
        ext.SynchronizeTopology(Line((0, 0, TerrainKind.Grassland), (1, 0, TerrainKind.Grassland)));
        Wind(ext, 0); ext.AdvanceToDay(25);
        ext.SynchronizeTopology(Line((0, 0, TerrainKind.Grassland), (1, 0, TerrainKind.Grassland), (2, 0, TerrainKind.Grassland)));
        Near(68, Read(ext, 2).Temperature.Value, "new member enters from actual");
        Near(88, Read(ext, 1).Temperature.Value, "unchanged member no restart");
        ext.AdvanceToDay(50); Near(88, Read(ext, 2).Temperature.Value, "new member 25day entry");
        ext.SynchronizeTopology(Line((0, 0, TerrainKind.Grassland), (1, 1, TerrainKind.Grassland), (2, 0, TerrainKind.Grassland)));
        Check(!Read(ext, 2).HasMonsoon && Read(ext).HasMonsoon, "non-source disconnect membership");
        ext.AdvanceToDay(75); Near(68, Read(ext, 2).Temperature.Value, "departed member 25day return");
        int count = ext.ReadAll().Count;
        try
        {
            ext.SynchronizeTopology(new[] { new TopologyNode(Origin, 0, TerrainKind.Grassland, new[] { new GridPosition(1, 1) }) });
            Check(false, "invalid graph accepted");
        }
        catch (ArgumentException) { Check(ext.ReadAll().Count == count, "invalid graph must not partially mutate"); }
        var elevation = Single(TerrainKind.Desert);
        elevation.SynchronizeTopology(Line((0, 2, TerrainKind.Desert)));
        Check(Read(elevation).Elevation == 2 && Read(elevation).Terrain == TerrainKind.Volcano, "height transforms without resetting temperature");
        Near(68, Read(elevation).Temperature.Value, "height operation no climate jump");
        var many = new EnvironmentWorld();
        many.SynchronizeTopology(Line((0, 0, TerrainKind.Grassland), (1, 1, TerrainKind.Forest),
            (2, 0, TerrainKind.Grassland), (3, 1, TerrainKind.Forest), (4, 0, TerrainKind.Grassland)));
        Wind(many, 0, payment: new EnvironmentPayment("m1", 3));
        Wind(many, 2, payment: new EnvironmentPayment("m2", 7));
        Wind(many, 4, payment: new EnvironmentPayment("m3", 13));
        many.SynchronizeTopology(Line((0, 0, TerrainKind.Grassland), (1, 0, TerrainKind.Forest),
            (2, 0, TerrainKind.Grassland), (3, 0, TerrainKind.Forest), (4, 0, TerrainKind.Grassland)));
        Check(many.ReadMonsoons().Count == 0 && many.ReadSettlements().Count == 3, "all overlapping groups cancel atomically");
        Check(many.ReadSettlements().Sum(s => s.RefundAmount) == 23, "multi-source refunds actual total");
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
    static void V43Technologies()
    {
        Check(EnvironmentTechnologyCatalog.All.Length == 10, "V4.3 has ten technologies");
        foreach (EnvironmentTechnologyKind kind in EnvironmentTechnologyCatalog.All)
            Check(!string.IsNullOrWhiteSpace(EnvironmentTechnologyCatalog.Name(kind)),
                "technology needs a player-facing name");
        Check(!EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
            EnvironmentTechnologyKind.CrustUplift, 2)), "height has no large tier");
        Check(!EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
            EnvironmentTechnologyKind.MonsoonAnchor, 1, 0, 20)),
            "wind needs two nonzero signed choices");
        var w = new EnvironmentWorld();
        w.SynchronizeTopology(MixedLine(false, true, false));
        Check(!w.TrySetClimate(new GridPosition(1, 0), EnvironmentAttribute.Temperature,
            25, out _), "water rejects climate technology");
        Check(!w.TryDeployMonsoon(new GridPosition(1, 0), 20, 20,
            null, out _, out _), "water rejects monsoon source");
        Check(w.TryPreviewMonsoon(Origin, 20, -40, out var area, out _)
            && area.Count == 2 && !area.Contains(new GridPosition(1, 0)),
            "one water tile bridges two land cells without receiving climate");
        long wind = Wind(w, 0, 20, -40);
        Check(w.ReadMonsoons().Single().Area.Count == 2, "wind stores land-only area");
        Check(!Read(w, 1).HasMonsoon && Read(w, 2).HasMonsoon,
            "water has no wind effect");
        Check(w.TryCancelMonsoon(wind, out _), "wind cleanup");
        var twoWater = new EnvironmentWorld();
        twoWater.SynchronizeTopology(MixedLine(false, true, true, false));
        Check(twoWater.TryPreviewMonsoon(Origin, 20, 20, out var blocked, out _)
            && blocked.Count == 1, "two consecutive water tiles cannot bridge");

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
        Near(50000, Read(aquatic).Recovery.Value, "suppression multiplies actual R");
        Near(75000, Read(aquatic).Recovery.Target, "water R returns to water default");
        Check(aquatic.TryCancelRecoveryBatch(members, out _), "cancel entire water R timeline");
        Check(!aquatic.TryCancelRecoveryBatch(members, out _),
            "repeated cancellation cannot indefinitely extend water recovery");
        aquatic.AdvanceToDay(100);
        Near(75000, Read(aquatic).Recovery.Value, "water R returns in 100 days");
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
        Classification(); TerrainGraph(); Timelines(); RecoveryAndStock(); WindMasking(); TopologyAndRefunds(); V43Technologies();
        Console.WriteLine($"PASS: {assertions:N0} environment assertions (ranges, terrain graph, timelines, ten technologies, water, topology, refunds)");
    }
}
