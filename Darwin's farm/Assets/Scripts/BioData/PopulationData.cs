public class PopulationData
{
    private int speciesAmount;
    private SpeciesData species;

    //进化趋势
    private int movementAbility;   //0~100 影响物种的移动距离、能量消耗
    private int habitatNiche;  //0~100 50代表陆地生存，越靠近0越适应海洋，越靠近100越适应天空
    private int fitTemperature; //0~100 适宜温度
    private int fitHumidity;  //0~100 适宜湿度
    private int size;   //0~100 体型，与代谢能力共同决定进食周期、运动速度
    private int fertility;  //0~100 个体繁殖能力系数
}
