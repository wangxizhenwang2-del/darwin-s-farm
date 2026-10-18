using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

public enum WaterCoverage
    {
        Land = 0,
        NorthWestRiver = 1,
        SouthWestRiver = 2,
        NorthEastRiver = 3,
        SouthEastRiver = 4,
        VerticalRiver = 5,
        horizontalRiver = 6,
        CrossingRiver = 7,
        Lake = 8,
        Sea = 9,
    };

public enum RiverBankSide
{
    North, East, South, West,
    NorthEast, SouthEast, SouthWest, NorthWest
}

[System.Serializable]
public class RiverBankInfo
{
    public RiverBankSide side;
    public BlockInfo landOwner;
}

public class BlockInfo : MonoBehaviour
{
    [SerializeField]List<BlockInfo> neighbors;
    public IReadOnlyList<BlockInfo> Neighbors => neighbors;
    public void SetNeighbors(List<BlockInfo> value) => neighbors = value;
    [SerializeField]public int temperature;  //温度为0~100，温暖为50，极寒为0，极热为100
    [SerializeField]public int humidity;  //湿度为0~100
    [SerializeField]public int elevation; //海拔为0、1或2
    [SerializeField]public float plantBiomass;   //植物库存为0~maxPlantBiomass
    [SerializeField]public float maxPlantBiomass = 10000f; //植物生物量上限
    [SerializeField]public int habitatRecovery; //每日恢复的生物量
    // Stable terrain reference for optional prey-density feedback. Rebuilt
    // from the tile definition on load; short-lived R tools do not alter it.
    [System.NonSerialized] public int predationReferenceRecovery;
    [SerializeField] public float algaeBiomass; //水域生产者库存；生态位系统启用后由水域共享
    [SerializeField] public float maxAlgaeBiomass = 10000f;
    [SerializeField] public int algaeRecovery;
    public float plantGrowthToday; //当天实际生长的植物量
    public float consumedBiomassToday; //当天被种群吃掉的植物量
    [System.NonSerialized] public float accountedBiomassSinkToday;
    public float detritusBiomass; //死亡和转化产生、尚未重新进入植物库存的物质
    [SerializeField]public List<PopulationData> community;    //群落，存储了当前地块的种群
    public int nextLocalSplitDay;
    // One recovery-tool transaction per tile; successful guided mutations
    // across all resident populations spend this shared quota.
    public RecoveryGuidanceTransaction recoveryGuidance =
        new RecoveryGuidanceTransaction();
    public StockGuidanceTransaction stockGuidance =
        new StockGuidanceTransaction();
    public StockGuidancePending stockGuidancePending =
        new StockGuidancePending();
    public int neighborOpportunityRevision;
    [SerializeField] public WaterCoverage waterCoverage;
    // 河岸只是陆地种群可到达的位置，资源和种群仍归 landOwner。
    [SerializeField] private List<RiverBankInfo> riverBanks = new List<RiverBankInfo>();
    public IReadOnlyList<RiverBankInfo> RiverBanks => riverBanks;
    public void SetRiverBanks(List<RiverBankInfo> banks) =>
        riverBanks = banks ?? new List<RiverBankInfo>();

    // 仅记录真正接通的河道；普通地块邻接不等于水路相通。
    [SerializeField] private List<BlockInfo> waterLinks = new List<BlockInfo>();
    public IReadOnlyList<BlockInfo> WaterLinks => waterLinks;
    public void SetWaterLinks(List<BlockInfo> links) =>
        waterLinks = links ?? new List<BlockInfo>();
}

[System.Serializable]
public sealed class RecoveryGuidanceTransaction
{
    public string key;
    public int serial;
    public int direction;
    public int firstDay;
    public int lastDay;
    public int used;
    public int cap;
    public float totalDelta;
    public float probability;
}

[System.Serializable]
public sealed class StockGuidanceClaim
{
    public string key;
    public int used;
}

[System.Serializable]
public sealed class StockGuidanceTransaction
{
    public string key;
    public int serial;
    public int direction;
    public int firstDay;
    public int lastDay;
    public int cap;
    public float startStock;
    public float totalDelta;
    public float memory;
    public int memoryDay;
    public float pmax;
    public float rho;
    public List<StockGuidanceClaim> claims = new List<StockGuidanceClaim>();
}

[System.Serializable]
public sealed class StockGuidancePending
{
    public bool active;
    public int day;
    public int tier;
    public float startStock;
    public float endStock;
    public List<string> populationIds = new List<string>();
}
