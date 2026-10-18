using System;
using System.Collections.Generic;

public enum WaterCoverage { Land, NorthWestRiver, SouthWestRiver, NorthEastRiver,
    SouthEastRiver, VerticalRiver, horizontalRiver, CrossingRiver, Lake, Sea }
public enum RiverBankSide { North, East, South, West }
public sealed class RiverBankInfo { public RiverBankSide side; public BlockInfo landOwner; }
namespace UnityEngine
{
    public static class Mathf
    {
        public static int Abs(int n) => Math.Abs(n);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
    }
}
public readonly record struct Point(int x, int y);
public sealed class MapTileInstance { public Point Coordinate { get; set; } }
public sealed class BlockInfo
{
    public WaterCoverage waterCoverage;
    public int elevation;
    public int temperature, humidity;
    public MapTileInstance Tile;
    public readonly List<BlockInfo> Neighbors = new();
    public readonly List<BlockInfo> WaterLinks = new();
    public readonly List<RiverBankInfo> RiverBanks = new();
    public float algaeBiomass, maxAlgaeBiomass;
    public int algaeRecovery;
    public T GetComponent<T>() where T : class => Tile as T;
}

static class Program
{
    static int checks;
    static void Assert(bool condition, string name)
    {
        checks++;
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
    static BlockInfo Cell(int x, int y, bool water, int z = 0) => new()
    {
        waterCoverage = water ? WaterCoverage.Lake : WaterCoverage.Land,
        elevation = z,
        Tile = new MapTileInstance { Coordinate = new Point(x, y) }
    };
    static void Link(BlockInfo a, BlockInfo b) { a.Neighbors.Add(b); b.Neighbors.Add(a); }
    static void LinkWater(BlockInfo a, BlockInfo b) { a.WaterLinks.Add(b); b.WaterLinks.Add(a); }
    static void Main()
    {
        // A only touches w1. w3 belongs to the same water component via w1.
        var a = Cell(0, 0, false); var w1 = Cell(1, 0, true);
        var w3 = Cell(1, 1, true); var b = Cell(0, 1, false);
        Link(a, w1); Link(w1, w3); Link(w3, b);
        LinkWater(w1, w3);
        Assert(!WaterLandBoundary.IsLocalCrossHabitatEdge(w3, a), "T01 w3 cannot evolve to w1 shore A");
        Assert(WaterLandBoundary.IsLocalCrossHabitatEdge(w1, a), "T02 w1 can reach A after relocation");
        Assert(WaterLandBoundary.IsLocalCrossHabitatEdge(w3, b), "T03 w3 directly reaches B");
        Assert(WaterLandBoundary.IsLocalCrossHabitatEdge(a, w1), "T14 A reaches w1");
        Assert(!WaterLandBoundary.IsLocalCrossHabitatEdge(a, w3), "T14 A cannot reach w3");
        Link(a, w3);
        Assert(!WaterLandBoundary.IsLocalCrossHabitatEdge(w3, a), "T04 diagonal rejected despite listed neighbors");
        w3.Neighbors.Remove(a);
        Assert(!WaterLandBoundary.IsLocalCrossHabitatEdge(a, w3), "T04 missing reciprocal neighbor rejected");
        b.elevation = 2;
        Assert(!WaterLandBoundary.IsLocalCrossHabitatEdge(w3, b), "T04 height two rejected");
        b.elevation = 1;
        Assert(WaterLandBoundary.IsLocalCrossHabitatEdge(w3, b), "T04 height one accepted");
        b.Tile = null;
        Assert(!WaterLandBoundary.IsLocalCrossHabitatEdge(w3, b), "missing map position rejected");
        w1.algaeBiomass = 500; w3.algaeBiomass = 300;
        var whole = WaterRegionMap.Build(new[] { w1, w3 });
        Assert(whole.GetRegion(w1) == whole.GetRegion(w3), "T05 reciprocal channel shares region");
        w3.WaterLinks.Clear();
        var split = WaterRegionMap.Build(new[] { w1, w3 });
        Assert(split.GetRegion(w1) != split.GetRegion(w3), "T05 unilateral channel splits region");
        Assert(split.GetRegion(w1)!.AlgaeBiomass + split.GetRegion(w3)!.AlgaeBiomass == 800,
            "T05 topology split conserves per-cell stock");
        w3.WaterLinks.Add(w1); w3.elevation = 1;
        var slope = WaterRegionMap.Build(new[] { w1, w3 });
        Assert(slope.GetRegion(w1) != slope.GetRegion(w3) &&
            HabitatTopology.ChannelsConnect(w1, w3), "T06 height one is a cross-region channel");
        w3.elevation = 2;
        Assert(!HabitatTopology.ChannelsConnect(w1, w3), "T07 height two blocks channel");
        Console.WriteLine($"WATER_LAND_BOUNDARY_PASS checks={checks} seed=20261018");
    }
}
