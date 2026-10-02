using System.Collections.Generic;
using UnityEngine;

public class BlockInfo : MonoBehaviour
{
    [SerializeField]List<BlockInfo> neighbors;
    [SerializeField]private int temperature;  //温度为0~100，温暖为50，极寒为0，极热为100
    [SerializeField]private int humidity;  //湿度为0~100
    [SerializeField]private int elevation; //海拔为0、1或2
    [SerializeField]private int waterCoverage;  //
    [SerializeField]private int plantBiomass;   //植物生物量为0~100，会自然生长，会影响
    [SerializeField]private List<PopulationData> community;    //群落，存储了当前地块的种群
}
