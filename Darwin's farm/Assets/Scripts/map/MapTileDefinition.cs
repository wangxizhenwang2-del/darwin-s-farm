using UnityEngine;

public enum BiomeType
{
    // Legacy IDs are retained for existing assets. The live model has eight terrain kinds.
    Grass = 0,
    Sand = 1,
    Rock = 2,
    Forest = 3,
    Rainforest = 4,
    Grassland = 5,
    Desert = 6,
    Tundra = 7,
    Highland = 8,
    SnowMountain = 9,
    Volcano = 10
}
//在project 中创建新资源，用来记录地块的相关信息
[CreateAssetMenu(
    fileName = "TileDefinition",
    menuName = "Map Whitebox/Tile Definition")]
public class MapTileDefinition : ScriptableObject
{
    public string displayName = "草地";

    public BiomeType biome = BiomeType.Grassland;

    public Color mainColor = new Color(0.35f, 0.7f, 0.3f);

    [Range(0, 2)]
    public int heightLevel;//白盒最后一阶段，给每块地图增加高度信息

    [Header("Initial Ecology")]
    [Range(0, 100)] public int initialTemperature = 68;
    [Range(0, 100)] public int initialHumidity = 68;
    [Min(0f)] public float initialPlantBiomass = 100000f;
    [Min(0f)] public float maxPlantBiomass = 1000000f;
    [Min(0)] public int habitatRecovery = 100000;
    public WaterCoverage initialWaterCoverage = WaterCoverage.Land;
    // Optional river opening override: north, east, south, west bits; 0 derives from shape.
    [Range(0, 15)] public int riverOpenings;
}
