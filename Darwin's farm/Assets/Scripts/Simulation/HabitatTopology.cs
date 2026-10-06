using System.Collections.Generic;
using UnityEngine;

// 地块负责地图位置；栖息地归属和水路连接在这里解释。
public static class HabitatTopology
{
    public static bool IsRiver(BlockInfo block)
    {
        if (block == null) return false;
        switch (block.waterCoverage)
        {
            case WaterCoverage.NorthWestRiver:
            case WaterCoverage.SouthWestRiver:
            case WaterCoverage.NorthEastRiver:
            case WaterCoverage.SouthEastRiver:
            case WaterCoverage.VerticalRiver:
            case WaterCoverage.horizontalRiver:
            case WaterCoverage.CrossingRiver:
                return true;
            default:
                return false;
        }
    }

    public static bool IsPureWater(BlockInfo block) => block != null &&
        (block.waterCoverage == WaterCoverage.Lake ||
         block.waterCoverage == WaterCoverage.Sea);

    public static bool IsWater(BlockInfo block) => IsRiver(block) || IsPureWater(block);

    public static bool AreAdjacent(BlockInfo left, BlockInfo right)
    {
        if (left == null || right == null || left == right) return false;
        return HasNeighbor(left.Neighbors, right) || HasNeighbor(right.Neighbors, left);
    }

    public static bool TryGetBankOwner(BlockInfo river, RiverBankSide side,
        out BlockInfo owner)
    {
        owner = null;
        if (!IsRiver(river) || river.RiverBanks == null) return false;
        bool found = false;
        foreach (RiverBankInfo bank in river.RiverBanks)
        {
            if (bank == null || bank.side != side) continue;
            if (found) return false; // 同一岸配置两次，归属不明确。
            owner = bank.landOwner;
            found = true;
        }
        if (!found || owner == null || owner.waterCoverage != WaterCoverage.Land ||
            !AreAdjacent(river, owner))
        {
            owner = null;
            return false;
        }
        return true;
    }

    public static bool HasBankOwnedBy(BlockInfo river, BlockInfo land)
    {
        if (!IsRiver(river) || land == null || river.RiverBanks == null) return false;
        foreach (RiverBankInfo bank in river.RiverBanks)
            if (bank != null && TryGetBankOwner(river, bank.side, out BlockInfo owner) &&
                owner == land) return true;
        return false;
    }

    public static bool CanLandEnterWater(BlockInfo land, BlockInfo water)
    {
        if (land == null || land.waterCoverage != WaterCoverage.Land ||
            !IsWater(water) || !AreAdjacent(land, water)) return false;
        return IsPureWater(water) || HasBankOwnedBy(water, land);
    }

    public static bool ChannelsConnect(BlockInfo left, BlockInfo right)
    {
        if (!IsWater(left) || !IsWater(right) || !AreAdjacent(left, right))
            return false;
        if (IsPureWater(left) && IsPureWater(right)) return true;
        // 河流地块可能肩并肩却互不相通，至少一端须声明这条水路。
        return HasNeighbor(left.WaterLinks, right) ||
            HasNeighbor(right.WaterLinks, left);
    }

    private static bool HasNeighbor(IReadOnlyList<BlockInfo> neighbors, BlockInfo target)
    {
        if (neighbors == null) return false;
        foreach (BlockInfo neighbor in neighbors)
            if (neighbor == target) return true;
        return false;
    }
}

public sealed class WaterRegion
{
    private readonly List<BlockInfo> members = new List<BlockInfo>();
    public IReadOnlyList<BlockInfo> Members => members;
    public int Elevation { get; private set; }

    internal WaterRegion(BlockInfo first)
    {
        Elevation = first.elevation;
    }

    internal void Add(BlockInfo block) => members.Add(block);
    public bool Contains(BlockInfo block) => members.Contains(block);

    public float AverageTemperature
    {
        get
        {
            float sum = 0f;
            foreach (BlockInfo block in members) sum += block.temperature;
            return members.Count == 0 ? 0f : sum / members.Count;
        }
    }

    public float AverageHumidity
    {
        get
        {
            float sum = 0f;
            foreach (BlockInfo block in members) sum += block.humidity;
            return members.Count == 0 ? 0f : sum / members.Count;
        }
    }

    public float AlgaeBiomass
    {
        get
        {
            float sum = 0f;
            foreach (BlockInfo block in members) sum += Mathf.Max(0f, block.algaeBiomass);
            return sum;
        }
    }

    public float MaxAlgaeBiomass
    {
        get
        {
            float sum = 0f;
            foreach (BlockInfo block in members) sum += Mathf.Max(0f, block.maxAlgaeBiomass);
            return sum;
        }
    }

    public float DailyAlgaeRecovery
    {
        get
        {
            float sum = 0f;
            foreach (BlockInfo block in members) sum += Mathf.Max(0, block.algaeRecovery);
            return sum;
        }
    }

    // 与陆地植物一样，生产者库存与每日恢复受各自地块上限约束。
    public float AvailableFood
    {
        get
        {
            float sum = 0f;
            foreach (BlockInfo block in members)
                sum += Mathf.Min(Mathf.Max(0f, block.maxAlgaeBiomass),
                    Mathf.Max(0f, block.algaeBiomass) + Mathf.Max(0, block.algaeRecovery));
            return sum;
        }
    }
}

public sealed class WaterRegionMap
{
    private readonly Dictionary<BlockInfo, WaterRegion> byBlock =
        new Dictionary<BlockInfo, WaterRegion>();
    private readonly List<WaterRegion> regions = new List<WaterRegion>();
    public IReadOnlyList<WaterRegion> Regions => regions;

    public WaterRegion GetRegion(BlockInfo block)
    {
        WaterRegion region;
        return block != null && byBlock.TryGetValue(block, out region) ? region : null;
    }

    public static bool IsWater(BlockInfo block) => HabitatTopology.IsWater(block);

    public static WaterRegionMap Build(IEnumerable<BlockInfo> blocks)
    {
        WaterRegionMap map = new WaterRegionMap();
        if (blocks == null) return map;
        HashSet<BlockInfo> allowed = new HashSet<BlockInfo>();
        foreach (BlockInfo block in blocks)
            if (IsWater(block)) allowed.Add(block);

        Dictionary<BlockInfo, List<BlockInfo>> connections =
            new Dictionary<BlockInfo, List<BlockInfo>>();
        foreach (BlockInfo block in allowed)
            connections.Add(block, new List<BlockInfo>());
        foreach (BlockInfo block in allowed)
        {
            if (block.Neighbors == null) continue;
            foreach (BlockInfo neighbor in block.Neighbors)
            {
                if (neighbor == null || !allowed.Contains(neighbor) ||
                    neighbor.elevation != block.elevation ||
                    !HabitatTopology.ChannelsConnect(block, neighbor)) continue;
                connections[block].Add(neighbor);
                connections[neighbor].Add(block);
            }
        }

        foreach (BlockInfo start in allowed)
        {
            if (map.byBlock.ContainsKey(start)) continue;
            WaterRegion region = new WaterRegion(start);
            map.regions.Add(region);
            Queue<BlockInfo> queue = new Queue<BlockInfo>();
            queue.Enqueue(start);
            map.byBlock.Add(start, region);
            while (queue.Count > 0)
            {
                BlockInfo current = queue.Dequeue();
                region.Add(current);
                foreach (BlockInfo neighbor in connections[current])
                {
                    if (map.byBlock.ContainsKey(neighbor)) continue;
                    map.byBlock.Add(neighbor, region);
                    queue.Enqueue(neighbor);
                }
            }
        }
        return map;
    }
}
