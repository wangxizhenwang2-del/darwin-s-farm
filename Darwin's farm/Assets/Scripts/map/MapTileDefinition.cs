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
}