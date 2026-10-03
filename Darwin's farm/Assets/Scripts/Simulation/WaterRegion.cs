using System.Collections.Generic;
using UnityEngine;

// 水域只由实际列入地图的地块构成；连接、含水、同海拔三个条件缺一不可。
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

    public static bool IsWater(BlockInfo block) =>
        block != null && block.waterCoverage != WaterCoverage.Land;

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
                    neighbor.elevation != block.elevation) continue;
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
