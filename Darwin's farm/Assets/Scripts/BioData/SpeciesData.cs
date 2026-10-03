using UnityEngine;

[CreateAssetMenu(menuName = "Darwin's Farm/Species")]
public class SpeciesData: ScriptableObject
{
    private string speciesName;
    private Sprite sprite;

    [Range(0, 2)] public int trophicLevel; //0 食草，1、2 分别捕食低一级，最多三级食物链
    private int baseMovementAbility;   //0~100 影响物种的移动距离、能量消耗
    private int baseHabitatNiche;  //0~100 50代表陆地生存，越靠近0越适应海洋，越靠近100越适应天空
    private int baseFitTemperature; //0~100 适宜温度
    private int baseFitHumidity;  //0~100 适宜湿度
    private int baseSize;   //0~100 体型，与运动能力共同决定进食周期、运动速度
    private int baseFertility;  //0~100 个体繁殖能力系数
}
