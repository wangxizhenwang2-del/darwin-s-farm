using System;
using DarwinFarm.Environment;
using UnityEditor;
using UnityEngine;

// These checks exercise the actual timelines and serialized model used by the HUD.
public static class TileStatusUIChecks
{
    [MenuItem("Tools/Darwin Farm/UI/Check Tile Status Timelines", false, 306)]
    public static void Run()
    {
        var position = new GridPosition(0, 0);
        var world = World(false);
        Require(world.TrySetClimate(position, EnvironmentAttribute.Temperature, -50, out string error), error);
        var state = Read(world);
        Require(state.Temperature.Phase == EffectPhase.Entering && state.Temperature.ToolTier == -2,
            "A negative large tool must retain its applied tier while entering.");
        Near(TileStatusView.CoveredFraction(EnvironmentTechnologyKind.TemperatureChange, state.Temperature, false), 0f);
        world.AdvanceToDay(EnvironmentRules.TransitionDays);
        state = Read(world);
        Require(state.Temperature.Phase == EffectPhase.Holding &&
            state.Temperature.RemainingDays == EnvironmentRules.ClimateHoldDays, "Hold must start at full duration.");
        Near(TileStatusView.CoveredFraction(EnvironmentTechnologyKind.TemperatureChange, state.Temperature, false), 0f);
        world.AdvanceToDay(EnvironmentRules.TransitionDays + 75);
        state = Read(world);
        Near(TileStatusView.CoveredFraction(EnvironmentTechnologyKind.TemperatureChange, state.Temperature, false), .5f);
        var restored = World(false);
        restored.RestoreState(JsonUtility.FromJson<EnvironmentWorld.SaveState>(JsonUtility.ToJson(world.CaptureState())));
        var afterLoad = Read(restored);
        Require(afterLoad.Temperature.ToolTier == -2 && afterLoad.Temperature.RemainingDays == 75,
            "Save/load must preserve tool artwork and the actual remaining hold time.");
        Near(TileStatusView.CoveredFraction(EnvironmentTechnologyKind.TemperatureChange, afterLoad.Temperature, false), .5f);
        int returnDay = EnvironmentRules.TransitionDays + EnvironmentRules.ClimateHoldDays;
        restored.AdvanceToDay(returnDay);
        state = Read(restored);
        Require(state.Temperature.Phase == EffectPhase.Returning && state.Temperature.ToolTier == -2,
            "Returning must retain the applied tool identity.");
        Near(TileStatusView.CoveredFraction(EnvironmentTechnologyKind.TemperatureChange, state.Temperature, false), 1f);
        restored.AdvanceToDay(returnDay + EnvironmentRules.ClimateReturnDays);
        Require(!Read(restored).HasLocalTemperature, "Completed tools must disappear.");

        world = World(false);
        Require(world.TrySetRecovery(position, 25000, out error), error);
        world.AdvanceToDay(EnvironmentRules.LandRecoveryTransitionDays + 75);
        state = Read(world);
        Require(state.Recovery.ToolTier == 1 && state.Recovery.Phase == EffectPhase.Holding, "Land recovery tier/phase.");
        Near(TileStatusView.CoveredFraction(EnvironmentTechnologyKind.PlantRecoveryChange, state.Recovery, false), .5f);
        restored = World(false);
        restored.RestoreState(JsonUtility.FromJson<EnvironmentWorld.SaveState>(JsonUtility.ToJson(world.CaptureState())));
        Require(Read(restored).Recovery.ToolTier == 1, "Plant recovery artwork must survive save/load.");

        world = World(true);
        Require(world.TrySetRecovery(position, -25000, out error), error);
        world.AdvanceToDay(EnvironmentRules.RecoveryHoldDays / 2);
        state = Read(world);
        Require(state.Recovery.ToolTier == -1 && state.Recovery.Phase == EffectPhase.Holding, "Water recovery tier/phase.");
        Near(TileStatusView.CoveredFraction(EnvironmentTechnologyKind.PlantRecoveryChange, state.Recovery, true), .5f);

        world = World(false);
        Require(world.TrySetClimate(position, EnvironmentAttribute.Humidity, 25, out error), error);
        world.AdvanceToDay(5);
        Require(world.TryCancelClimate(position, EnvironmentAttribute.Humidity, out error), error);
        state = Read(world);
        Require(state.Humidity.Phase == EffectPhase.Returning && state.Humidity.ToolTier == 1,
            "An early cancellation must retain the tool and enter recovery immediately.");
        Near(TileStatusView.CoveredFraction(EnvironmentTechnologyKind.HumidityChange, state.Humidity, false), 1f);
        // Old saves have no tier metadata; zero must remain a valid, readable fallback.
        var legacy = world.CaptureState();
        legacy.tiles[0].locals[1].tier = 0;
        restored = World(false); restored.RestoreState(legacy);
        Require(Read(restored).Humidity.ToolTier == 0, "Legacy saves must remain readable.");
        Debug.Log("TILE_STATUS_TIMELINES_PASS: entry, hold, half-time, recovery, expiry, cancellation, land/water durations and save/load.");
    }

    private static EnvironmentWorld World(bool water)
    {
        var world = new EnvironmentWorld();
        world.SynchronizeTopology(new[] { new TopologyNode(new GridPosition(0, 0), 0,
            TerrainKind.Grassland, Array.Empty<GridPosition>(), water) });
        return world;
    }
    private static EnvironmentSnapshot Read(EnvironmentWorld world)
    {
        Require(world.TryRead(new GridPosition(0, 0), out var snapshot), "Missing test tile.");
        return snapshot;
    }
    private static void Near(float actual, float expected) => Require(Mathf.Abs(actual - expected) < .0001f,
        "Expected mask " + expected + ", got " + actual);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Tile status check: " + message); }
}
