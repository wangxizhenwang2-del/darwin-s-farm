using System;
using DarwinFarm.Environment;

internal static class Program
{
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }

    private static void Main()
    {
        Check(EnvironmentTechnologyCatalog.All.Length == 5, "exactly five player tools");
        Check(Enum.GetValues(typeof(EnvironmentTechnologyKind)).Length == 5,
            "removed kind is unavailable even to serialized input");
        Check(!EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
            (EnvironmentTechnologyKind)4)), "retired numeric kind is rejected");
        Check(EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
            EnvironmentTechnologyKind.PlantStockChange, -2)), "stock tier accepted");
        Check(EnvironmentTechnologyCatalog.Validate(new EnvironmentTechnologyChoice(
            EnvironmentTechnologyKind.PlantRecoveryChange, 2)), "recovery tier accepted");
        Check(EnvironmentTechnologyCatalog.Price(new EnvironmentTechnologyChoice(
            EnvironmentTechnologyKind.PlantStockChange, 1)) ==
            EnvironmentTechnologyCatalog.Price(new EnvironmentTechnologyChoice(
                EnvironmentTechnologyKind.PlantRecoveryChange, 1)),
            "stock and recovery use the same per-operation tier price");
        Check(EnvironmentTechnologyCatalog.Price(new EnvironmentTechnologyChoice(
            EnvironmentTechnologyKind.ElevationChange)) == 500, "remaining prices work");

        var origin = new GridPosition(0, 0);
        var adjacent = new GridPosition(1, 0);
        var world = new EnvironmentWorld();
        world.SynchronizeTopology(new[] {
            new TopologyNode(origin, 0, TerrainKind.Grassland, new[] { adjacent }),
            new TopologyNode(adjacent, 0, TerrainKind.Grassland, new[] { origin })
        });
        Check(world.TrySetClimate(origin, EnvironmentAttribute.Temperature, 25, out _),
            "temperature works");
        Check(world.TrySetClimate(origin, EnvironmentAttribute.Humidity, -25, out _),
            "humidity works");
        Check(world.TrySetRecovery(origin, 25000, out _), "plant tool works");
        Check(world.TryRead(origin, out var local) &&
            local.Temperature.Phase == EffectPhase.Returning &&
            local.Humidity.Phase == EffectPhase.Entering &&
            local.Recovery.Phase == EffectPhase.Holding,
            "third local tool displaces oldest and keeps two slots");
        Check(world.TryRead(adjacent, out var other) &&
            other.Temperature.Phase == EffectPhase.None &&
            other.Humidity.Phase == EffectPhase.None,
            "neighbor has no regional climate effect");
        world.AdvanceToDay(25);
        Check(world.TryRead(origin, out local) &&
            local.Humidity.Phase == EffectPhase.Holding,
            "time progression keeps remaining climate effect");
        Console.WriteLine("V5.0 environment smoke passed");
    }
}
