[System.Serializable]
public class PopulationData
{
    public int speciesAmount;
    public SpeciesData species;

    //进化趋势
    public int movementAbility;   //0~100 影响物种的移动距离、能量消耗
    public int habitatNiche;  //0~100 50代表陆地生存，越靠近0越适应海洋，越靠近100越适应天空
    public int fitTemperature; //0~100 适宜温度
    public int fitHumidity;  //0~100 适宜湿度
    public int size;   //0~100 体型，与代谢能力共同决定进食周期、运动速度
    public int fertility;  //0~100 个体繁殖能力系数

    //因变量
    public int environmentalFitness;    //0~100，越强越利于繁殖
    public float energyNeed;
}
