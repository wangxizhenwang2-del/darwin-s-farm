using System.Collections.Generic;

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
