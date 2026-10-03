[System.Serializable]
public class PopulationData
{
    public int speciesAmount; //种群数量保持整数
    public SpeciesData species;

    //进化趋势
    public int movementAbility;   //0~100 影响物种的移动距离、能量消耗
    public int habitatNiche;  //0~100 50代表陆地生存，越靠近0越适应海洋，越靠近100越适应天空
    public int fitTemperature; //0~100 适宜温度
    public int fitHumidity;  //0~100 适宜湿度
    public int size;   //1~100 体型，与代谢能力共同决定进食周期、运动速度
    public int fertility;  //0~100 个体繁殖能力系数

    // 食性从物种资产继承一次，此后由各自种群独立演化。
    public int trophicLevel;
    public bool trophicLevelInitialized;

    // 已保留但不足一个整数单位的变异，避免小倍率性状永远无法变化。
    public float temperatureMutationRemainder;
    public float humidityMutationRemainder;
    public float movementMutationRemainder;
    public float sizeMutationRemainder;
    public float fertilityMutationRemainder;
    public float trophicMutationRemainder;

    public int mutationDaysElapsed;
    public int previousMutationPopulation;
    public bool mutationPopulationInitialized;

    //因变量
    public int environmentalFitness;    //0~100，越强越利于繁殖
    public float energyNeed;
    public float allocatedBiomass;
    public float carryingCapacity; //潜在资源份额可支撑的种群规模
    public float energySatisfactionToday; //资源分配后、数量变化前的能量满足率
    public float actualEnergySatisfactionToday; //当日实际摄食率，用于运动选择
    public int populationBeforePredation; //捕食前数量，避免捕食后分母缩小造成虚假盈余
    public float energyReserve; //捕食者吃下整只猎物后未用完的能量
    public int nextMigrationDay; //迁徙冷却结束后可再次参与检查的日期
    public float birthRemainder; //累计不足一个体的繁殖量
    public float deathRemainder; //累计不足一个体的死亡量
    public int birthsToday; //当天出生数量
    public int deathsToday; //当天死亡数量
}
