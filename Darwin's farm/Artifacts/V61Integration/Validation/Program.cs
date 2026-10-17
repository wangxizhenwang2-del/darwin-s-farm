using System;
using DarwinFarm.Environment;

internal static class Program
{
    private static void Check(bool yes, string name)
    { if (!yes) throw new Exception(name); Console.WriteLine("PASS " + name); }

    private static EnvironmentWorld World(out GridPosition origin)
    {
        origin = new GridPosition(0, 0);
        var neighbor = new GridPosition(1, 0);
        var world = new EnvironmentWorld();
        world.SynchronizeTopology(new[] {
            new TopologyNode(origin, 0, TerrainKind.Grassland, new[] { neighbor }),
            new TopologyNode(neighbor, 0, TerrainKind.Grassland, new[] { origin }) });
        return world;
    }

    private static void Main()
    {
        var world = World(out var site);
        world.TryRead(site, out var before);
        double t0 = before.Temperature.Value, r0 = before.Recovery.Value;
        Check(world.TrySetClimate(site, EnvironmentAttribute.Temperature,
            25, out _), "climate accepted");
        Check(world.TrySetRecovery(site, 25000, out _), "recovery accepted");
        world.TryRead(site, out var now);
        Check(now.Temperature.Phase == EffectPhase.Entering &&
            now.Recovery.Phase == EffectPhase.Entering &&
            Math.Abs(now.Recovery.Value - r0) < .01,
            "both land tools enter without instant recovery jump");
        world.AdvanceToDay(25);
        world.TryRead(site, out now);
        Check(now.Temperature.Phase == EffectPhase.Holding &&
            now.Recovery.Phase == EffectPhase.Holding &&
            Math.Abs(now.Recovery.Value - (r0 + 25000)) < .01,
            "25-day entry and shared 150-day hold");
        world.AdvanceToDay(175);
        world.TryRead(site, out now);
        Check(now.Temperature.Phase == EffectPhase.Returning &&
            now.Recovery.Phase == EffectPhase.Returning,
            "return starts on day 175");
        world.AdvanceToDay(275);
        world.TryRead(site, out now);
        Check(Math.Abs(now.Temperature.Value - t0) < .01 &&
            Math.Abs(now.Recovery.Value - r0) < .01,
            "100-day return restores captured baselines");

        world = World(out site);
        world.TryRead(site, out before);
        r0 = before.Recovery.Value;
        Check(world.TrySetRecovery(site, -25000, out _),
            "negative recovery accepted");
        world.AdvanceToDay(10);
        Check(world.TryCancelRecovery(site, out _) &&
            world.TryCancelRecovery(site, out _),
            "repeated cancel is idempotent");
        world.AdvanceToDay(110);
        world.TryRead(site, out now);
        Check(Math.Abs(now.Recovery.Value - r0) < .01,
            "cancel from current actual returns to baseline in 100 days");
        var raised = new GridPosition(0, 0);
        var adjacent = new GridPosition(1, 0);
        world.SynchronizeTopology(new[] {
            new TopologyNode(raised, 1, TerrainKind.Grassland, new[] { adjacent }),
            new TopologyNode(adjacent, 0, TerrainKind.Grassland, new[] { raised }) });
        world.TryRead(site, out now);
        Check(now.Recovery.Target == now.Defaults.Recovery,
            "recovery follows a later habitat change after command return");
        Check(Math.Abs(EnvironmentRules.MultiplyStock(100000, 1000000, .5)
            - 150000) < .01, "stock plus 50 percent multiplies current stock");
        Check(Math.Abs(EnvironmentRules.MultiplyStock(100000, 1000000, -.25)
            - 75000) < .01, "stock minus 25 percent multiplies current stock");
        try
        {
            world.SynchronizeTopology(new[] {
                new TopologyNode(new GridPosition(0, 0), 0,
                    TerrainKind.Grassland, new[] { new GridPosition(1, 1) }),
                new TopologyNode(new GridPosition(1, 1), 0,
                    TerrainKind.Grassland, new[] { new GridPosition(0, 0) }) });
            throw new Exception("diagonal topology accepted");
        }
        catch (ArgumentException)
        { Console.WriteLine("PASS diagonal contact rejected"); }
    }
}
