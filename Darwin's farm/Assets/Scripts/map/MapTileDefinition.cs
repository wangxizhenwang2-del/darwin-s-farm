using UnityEngine;

public enum BiomeType
{
    Grass,
    Sand,
    Rock
}
//在project 中创建新资源，用来记录地块的相关信息
[CreateAssetMenu(
    fileName = "TileDefinition",
    menuName = "Map Whitebox/Tile Definition")]
public class MapTileDefinition : ScriptableObject
{
    public string displayName = "草地";

    public BiomeType biome = BiomeType.Grass;

    public Color mainColor = new Color(0.35f, 0.7f, 0.3f);

    [Range(0, 2)]
    public int heightLevel;//白盒最后一阶段，给每块地图增加高度信息

    [Header("Initial Ecology")]
    [Range(0, 100)] public int initialTemperature = 50;
    [Range(0, 100)] public int initialHumidity = 50;
    [Min(0f)] public float initialPlantBiomass = 5000f;
    [Min(0f)] public float maxPlantBiomass = 10000f;
    [Min(0)] public int habitatRecovery = 1000;
    public WaterCoverage initialWaterCoverage = WaterCoverage.Land;
}
