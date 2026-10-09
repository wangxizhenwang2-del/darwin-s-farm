using System.Collections.Generic;
using UnityEngine;

// Map-facing water topology. Only reciprocal openings at equal height share
// a stock/recovery ledger; height-one edges remain separate water bodies.
internal static class WaterBodyTopology
{
    private static readonly Vector2Int[] Directions =
    { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    public static List<MapTileInstance> Area(MapGridManager grid, Vector2Int center)
    {
        var result = new List<MapTileInstance>();
        if (grid == null || !grid.TryGetTile(center, out MapTileInstance start) ||
            start.Block == null || !HabitatTopology.IsWater(start.Block)) return result;
        var visited = new HashSet<Vector2Int> { center };
        var queue = new Queue<MapTileInstance>(); queue.Enqueue(start);
        while (queue.Count > 0)
        {
            MapTileInstance tile = queue.Dequeue();
            result.Add(tile);
            foreach (Vector2Int direction in Directions)
            {
                Vector2Int nextPosition = tile.Coordinate + direction;
                if (visited.Contains(nextPosition) ||
                    !grid.TryGetTile(nextPosition, out MapTileInstance next) ||
                    next.Block == null || !HabitatTopology.IsWater(next.Block) ||
                    tile.HeightLevel != next.HeightLevel ||
                    !Open(tile, direction) || !Open(next, -direction)) continue;
                visited.Add(nextPosition); queue.Enqueue(next);
            }
        }
        result.Sort((a, b) => a.Coordinate.x != b.Coordinate.x
            ? a.Coordinate.x.CompareTo(b.Coordinate.x)
            : a.Coordinate.y.CompareTo(b.Coordinate.y));
        return result;
    }

    public static List<List<MapTileInstance>> AllAreas(MapGridManager grid)
    {
        var areas = new List<List<MapTileInstance>>();
        var visited = new HashSet<Vector2Int>();
        if (grid == null) return areas;
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            if (tile == null || tile.Block == null ||
                !HabitatTopology.IsWater(tile.Block) || visited.Contains(tile.Coordinate)) continue;
            List<MapTileInstance> area = Area(grid, tile.Coordinate);
            foreach (MapTileInstance member in area) visited.Add(member.Coordinate);
            areas.Add(area);
        }
        return areas;
    }

    private static bool Open(MapTileInstance tile, Vector2Int gridDirection)
    {
        WaterCoverage coverage = tile.Block.waterCoverage;
        if (coverage == WaterCoverage.Lake || coverage == WaterCoverage.Sea) return true;
        int mask = tile.Definition != null && tile.Definition.riverOpenings != 0
            ? tile.Definition.riverOpenings : ShapeMask(coverage);
        Vector2Int local = gridDirection;
        for (int i = 0; i < tile.RotationSteps; i++)
            local = new Vector2Int(-local.y, local.x);
        int bit = local == Vector2Int.up ? 1 : local == Vector2Int.right ? 2 :
            local == Vector2Int.down ? 4 : local == Vector2Int.left ? 8 : 0;
        return (mask & bit) != 0;
    }

    private static int ShapeMask(WaterCoverage coverage)
    {
        switch (coverage)
        {
            case WaterCoverage.NorthWestRiver: return 1 | 8;
            case WaterCoverage.SouthWestRiver: return 4 | 8;
            case WaterCoverage.NorthEastRiver: return 1 | 2;
            case WaterCoverage.SouthEastRiver: return 4 | 2;
            case WaterCoverage.VerticalRiver: return 1 | 4;
            case WaterCoverage.horizontalRiver: return 2 | 8;
            case WaterCoverage.CrossingRiver: return 15;
            default: return 0;
        }
    }
}
