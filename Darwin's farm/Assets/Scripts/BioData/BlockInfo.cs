using System.Collections.Generic;
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
    };

public class BlockInfo : MonoBehaviour
{
    [SerializeField]List<BlockInfo> neighbors;
    [SerializeField]public int temperature;  //温度为0~100，温暖为50，极寒为0，极热为100
    [SerializeField]public int humidity;  //湿度为0~100
    [SerializeField]public int elevation; //海拔为0、1或2
    [SerializeField]public int plantBiomass;   //植物生物量为0~10000，会自然生长，会影响
    [SerializeField]public int habitatRecovery; //每日恢复的生物量
    [SerializeField]public List<PopulationData> community;    //群落，存储了当前地块的种群
    [SerializeField] public WaterCoverage waterCoverage;
}
