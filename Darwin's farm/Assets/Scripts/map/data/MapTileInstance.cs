using UnityEngine;

public class MapTileInstance : MonoBehaviour
{
    [Header("运行时地块数据")]
    [SerializeField] private Vector2Int coordinate;
    [SerializeField] private MapTileDefinition definition;
    [SerializeField] private BiomeType runtimeBiome;
    [SerializeField] private int heightLevel;

    [SerializeField, Range(0, 3)]
    private int rotationSteps;

    public Vector2Int Coordinate => coordinate;
    public MapTileDefinition Definition => definition;
    public int HeightLevel => heightLevel;
    public BlockInfo Block => GetComponent<BlockInfo>();

    public int RotationSteps => rotationSteps;
    public float RotationDegrees => rotationSteps * 90f;

    public BiomeType Biome => runtimeBiome;
    public Color MainColor => definition.mainColor;

    public Renderer Core { get; private set; }
    public Renderer NorthBorder { get; private set; }
    public Renderer SouthBorder { get; private set; }
    public Renderer EastBorder { get; private set; }
    public Renderer WestBorder { get; private set; }

    public void Initialize(
        Vector2Int gridCoordinate,
        MapTileDefinition tileDefinition,
        int level,
        int rotation = 0)
    {
        coordinate = gridCoordinate;
        definition = tileDefinition;
        runtimeBiome = tileDefinition.biome;
        heightLevel = level;

        rotationSteps = ((rotation % 4) + 4) % 4;

        Core = FindRenderer("Core");
        NorthBorder = FindRenderer("Border_North");
        SouthBorder = FindRenderer("Border_South");
        EastBorder = FindRenderer("Border_East");
        WestBorder = FindRenderer("Border_West");
    }

    // Change terrain on this instance without mutating its construction preset.
    internal void SetRuntimeBiome(BiomeType biome)
    {
        runtimeBiome = biome;
    }

    // Change the instance height without mutating the tile preset asset.
    internal void SetHeightLevel(int level, Vector3 gridOrigin)
    {
        heightLevel = level;
        transform.position = gridOrigin + Vector3.up * level * MapGridManager.HeightStep;
    }

    private Renderer FindRenderer(string childName)
    {
        Transform child = transform.Find(childName);

        if (child == null)
        {
            Debug.LogError($"地块缺少子物体：{childName}", this);
            return null;
        }

        return child.GetComponent<Renderer>();
    }

    // 地块自身的方向，转换成地图方向。
    public Vector2Int LocalToGridDirection(Vector2Int direction)
    {
        for (int i = 0; i < rotationSteps; i++)
            direction = new Vector2Int(direction.y, -direction.x);

        return direction;
    }

    // 地块自身的平面位置偏移，转换成地图方向的偏移。
    public Vector2 LocalToGridOffset(Vector2 offset)
    {
        for (int i = 0; i < rotationSteps; i++)
            offset = new Vector2(offset.y, -offset.x);

        return offset;
    }

    // 参数是地图里的上下左右方向。
    public Renderer GetBorder(Vector2Int direction)
    {
        // 先转换回地块自身方向。
        for (int i = 0; i < rotationSteps; i++)
            direction = new Vector2Int(-direction.y, direction.x);

        if (direction == Vector2Int.up)
            return NorthBorder;

        if (direction == Vector2Int.down)
            return SouthBorder;

        if (direction == Vector2Int.right)
            return EastBorder;

        if (direction == Vector2Int.left)
            return WestBorder;

        return null;
    }
}