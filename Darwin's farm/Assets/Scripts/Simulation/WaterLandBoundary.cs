using System;

// Direct source-cell to target-cell water/land route; does not scan a water region.
public static class WaterLandBoundary
{
    public static bool IsLocalCrossHabitatEdge(BlockInfo source, BlockInfo target)
    {
        if (source == null || target == null || source == target ||
            (source.waterCoverage == WaterCoverage.Land) ==
            (target.waterCoverage == WaterCoverage.Land) ||
            Math.Abs(source.elevation - target.elevation) > 1 ||
            !Contains(source, target) || !Contains(target, source)) return false;

        MapTileInstance from = source.GetComponent<MapTileInstance>();
        MapTileInstance to = target.GetComponent<MapTileInstance>();
        return from != null && to != null &&
            Math.Abs(from.Coordinate.x - to.Coordinate.x) +
            Math.Abs(from.Coordinate.y - to.Coordinate.y) == 1;
    }

    private static bool Contains(BlockInfo owner, BlockInfo candidate)
    {
        if (owner.Neighbors == null) return false;
        foreach (BlockInfo neighbor in owner.Neighbors)
            if (neighbor == candidate) return true;
        return false;
    }
}
