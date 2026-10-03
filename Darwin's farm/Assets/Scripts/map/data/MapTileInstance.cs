using UnityEngine;

public class MapTileInstance : MonoBehaviour
{
    [Header("运行时地块数据")]
    [SerializeField] private Vector2Int coordinate;
    [SerializeField] private MapTileDefinition definition;
    [SerializeField] private int heightLevel;

    public Vector2Int Coordinate => coordinate;
    public MapTileDefinition Definition => definition;
    public int HeightLevel => heightLevel;

    public BiomeType Biome => definition.biome;
    public Color MainColor => definition.mainColor;

    public Renderer Core { get; private set; }

    public Renderer NorthBorder { get; private set; }
    public Renderer SouthBorder { get; private set; }
    public Renderer EastBorder { get; private set; }
    public Renderer WestBorder { get; private set; }

    public void Initialize(
        Vector2Int gridCoordinate,
        MapTileDefinition tileDefinition,
        int level)
    {
        coordinate = gridCoordinate;
        definition = tileDefinition;
        heightLevel = level;

        Core = FindRenderer("Core");

        NorthBorder = FindRenderer("Border_North");
        SouthBorder = FindRenderer("Border_South");
        EastBorder = FindRenderer("Border_East");
        WestBorder = FindRenderer("Border_West");
    }

    private Renderer FindRenderer(string childName)
    {
        Transform child = transform.Find(childName);

        if (child == null)
        {
            Debug.LogError(
                $"地块缺少子物体：{childName}",
                this);

            return null;
        }

        return child.GetComponent<Renderer>();
    }

    // 通过方向取得对应的外围区域。
    public Renderer GetBorder(Vector2Int direction)
    {
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