using System;
using DarwinFarm.Environment;

static class Program
{
    static int checks;
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static void Near(double actual, double expected, string message)
    { Check(Math.Abs(actual - expected) < .00001, message + ": " + actual + " != " + expected); }
    static EnvironmentSnapshot Read(EnvironmentWorld world)
    { Check(world.TryRead(new GridPosition(0, 0), out var result), "missing tile"); return result; }
    static EnvironmentWorld Place(TerrainKind visual)
    {
        var world = new EnvironmentWorld();
        world.SynchronizeTopology(new[] { new TopologyNode(new GridPosition(0, 0),
            EnvironmentRules.Defaults(visual).Elevation, visual, Array.Empty<GridPosition>()) });
        return world;
    }
    static void Main()
    {
        Check(Enum.GetValues(typeof(HabitatState)).Length == 27, "27 habitats");
        for (int z = 0; z <= 2; z++)
        for (int t = 0; t <= 100; t++)
        for (int h = 0; h <= 100; h++)
        {
            var state = EnvironmentRules.ClassifyHabitat(z, t, h);
            int ti = t <= 40 ? 0 : t < 60 ? 1 : 2;
            int hi = h <= 40 ? 0 : h < 60 ? 1 : 2;
            Check((int)state == z * 9 + ti * 3 + hi, "classification bands");
            var defaults = EnvironmentRules.Defaults(state);
            Check(EnvironmentRules.ClassifyHabitat(defaults.Elevation,
                defaults.Temperature, defaults.Humidity) == state, "stable defaults");
            Check(EnvironmentRules.Classify(z, t, h, 0) ==
                EnvironmentRules.Classify(z, t, h, 150000), "plant amount cannot reclassify");
        }
        foreach (TerrainKind visual in Enum.GetValues(typeof(TerrainKind)))
        {
            var world = Place(visual);
            var before = Read(world);
            Check(before.Terrain == visual, "preset visual");
            Check(before.Habitat == EnvironmentRules.PlacementHabitat(visual), "preset habitat");
            world.AdvanceToDay(350);
            var after = Read(world);
            Check(after.Habitat == before.Habitat, "idle habitat stable");
            Near(after.Recovery.Value, before.Defaults.Recovery, "idle recovery");
        }
        var grass = Place(TerrainKind.Grassland);
        Check(grass.TrySetRecovery(new GridPosition(0, 0), 25000, out _), "plant command");
        Check(Read(grass).Habitat == HabitatState.Z0HotMiddle, "plant cannot change habitat");
        Near(Read(grass).Recovery.Value, 115000, "plant command amount");
        grass.AdvanceToDay(100);
        Near(Read(grass).Recovery.Value, 90000, "return to current habitat amount");
        var tundra = Place(TerrainKind.Tundra);
        Check(tundra.TrySetClimate(new GridPosition(0, 0), EnvironmentAttribute.Humidity, 25, out _), "climate command");
        tundra.AdvanceToDay(25);
        Check(Read(tundra).Habitat == HabitatState.Z2ColdMiddle, "same visual habitat switch");
        Check(Read(tundra).Terrain == TerrainKind.SnowMountain, "visual follows habitat");
        Near(Read(tundra).Defaults.Recovery, 40000, "new recovery target");
        var forest = Place(TerrainKind.Forest);
        Check(forest.TrySetClimate(new GridPosition(0, 0), EnvironmentAttribute.Temperature, 25, out _),
            "forest climate command");
        forest.AdvanceToDay(25);
        Check(Read(forest).Habitat == HabitatState.Z1MiddleWet, "same visual state changes");
        Check(Read(forest).Terrain == TerrainKind.Forest, "same visual stays forest");
        Near(Read(forest).Defaults.Recovery, 100000, "same visual new plant target");
        forest.AdvanceToDay(75);
        Near(Read(forest).Recovery.Value, 100000, "plant recovery follows state");
        Console.WriteLine("V4.6 habitat audit passed: " + checks + " checks");
    }
}
