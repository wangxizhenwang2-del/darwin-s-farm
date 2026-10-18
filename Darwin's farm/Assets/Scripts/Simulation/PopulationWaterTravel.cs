using UnityEngine;

// Geometry is optional for standalone simulation fixtures, mandatory for mapped populations.
public static class PopulationWaterTravel
{
    public static bool CanTravel(BlockInfo source, BlockInfo target, PopulationData population = null)
    {
        if (!source || !target || !HabitatTopology.IsWater(source) || !HabitatTopology.IsWater(target)) return false;
        var a = source.GetComponent<MapTileInstance>(); var b = target.GetComponent<MapTileInstance>();
        if (!a || !b) return true;
        var map = a.GridOwner;
        var water = map ? map.GetComponent<MapWaterController>() : null;
        var movement = map ? map.GetComponent<PopulationMovementController>() : null;
        if (!water || !water.TrySpace(a.Coordinate, out var fromSpace) || !water.TrySpace(b.Coordinate, out var toSpace)) return false;
        float radius = movement ? movement.Settings.leaderSize * movement.Settings.swimClearanceMultiplier : .64f;
        if (movement && population != null && population.species)
            radius = PopulationSpriteDisplay.WaterRadius(movement.Settings.leaderSize, movement.Settings.swimClearanceMultiplier,
                population.species.Sprite, movement.ViewCamera ? movement.ViewCamera : Camera.main, movement.Settings.submergedFraction);
        if (!fromSpace.TrySpawn(radius, out Vector3 from)) return false;
        var presenter = map.GetComponent<PopulationVisualPresenter>();
        if (presenter && population != null && presenter.TryGetVisual(population, out var visual) && visual.IsWater)
        { from = visual.leader.groundPosition; radius = visual.SwimRadius; }
        return toSpace.TrySpawn(radius, out Vector3 to, from) && WaterPathfinding.TryPath(water, from, to, radius, out _);
    }
}
