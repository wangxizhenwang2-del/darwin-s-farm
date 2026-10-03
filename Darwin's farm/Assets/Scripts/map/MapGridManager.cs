using System.Collections.Generic;
using UnityEngine;

public class MapGridManager : MonoBehaviour
{
    public const float TileSize = 17f;
    public const float CoreSize = 15f;

    public event System.Action TilesChanged;

    public const float HeightStep = 1f;

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.right,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.down
    };

    [Header("场景引用")]
    [SerializeField] private Transform placedTiles;
    [SerializeField] private RuntimeMapNavigation mapNavigation;
    [SerializeField] private Material tileMaterial;

    [Header("地块配置")]
    [SerializeField] private MapTileDefinition initialTile;

    [Header("运行时放置测试")]
    [SerializeField] private MapTileDefinition testTile;
    [SerializeField]
    private Vector2Int testCoordinate =
        new Vector2Int(1, 0);

    [Header("Scene窗口辅助显示")]
    [SerializeField] private bool showAvailablePositions = true;

    private readonly Dictionary<Vector2Int, MapTileInstance> tiles =
    new Dictionary<Vector2Int, MapTileInstance>();

    private int groundLayer;
    private bool initialized;

    private void Start()
    {
        groundLayer = LayerMask.NameToLayer("Ground");

        if (placedTiles == null ||
            mapNavigation == null ||
            tileMaterial == null ||
            initialTile == null ||
            groundLayer < 0)
        {
            Debug.LogError(
                "地图引用不完整，或没有名为Ground的Layer。",
                this);

            enabled = false;
            return;
        }

        initialized = true;

        TryPlaceTile(Vector2Int.zero, initialTile);
    }

    public IEnumerable<MapTileInstance> GetPlacedTiles()
    {
        return tiles.Values;
    }

    // 网格坐标转换成世界位置。
    public Vector3 GridToWorld(Vector2Int coordinate)
    {
        return placedTiles.position + new Vector3(
            coordinate.x * TileSize,
            0f,
            coordinate.y * TileSize);
    }

    // 世界位置转换成最近的网格坐标。
    public Vector2Int WorldToGrid(Vector3 worldPosition)
    {
        Vector3 offset = worldPosition - placedTiles.position;

        return new Vector2Int(
            Mathf.RoundToInt(offset.x / TileSize),
            Mathf.RoundToInt(offset.z / TileSize));
    }

    public bool CanPlaceAt(Vector2Int coordinate)
    {
        if (!initialized || tiles.ContainsKey(coordinate))
            return false;

        // 第一块只允许放在原点。
        if (tiles.Count == 0)
            return coordinate == Vector2Int.zero;

        // 只检查上下左右，不检查对角。
        foreach (Vector2Int direction in Directions)
        {
            if (tiles.ContainsKey(coordinate + direction))
                return true;
        }

        return false;
    }

    public bool TryPlaceTile(
    Vector2Int coordinate,
    MapTileDefinition definition,
    int rotationSteps = 0)
    {
        if (definition == null || !CanPlaceAt(coordinate))
            return false;

        GameObject tileRoot = new GameObject(
            $"Tile_{coordinate.x}_{coordinate.y}_{definition.displayName}");

        tileRoot.transform.SetParent(placedTiles, false);
        tileRoot.transform.position =
    GridToWorld(coordinate) +
    Vector3.up * definition.heightLevel * HeightStep;

        rotationSteps = ((rotationSteps % 4) + 4) % 4;

        tileRoot.transform.rotation =
            Quaternion.Euler(0f, rotationSteps * 90f, 0f);

        Color coreColor = definition.mainColor;

        // 暂时用较深颜色区分外围区域。
        Color borderColor = new Color(
            coreColor.r * 0.75f,
            coreColor.g * 0.75f,
            coreColor.b * 0.75f,
            1f);

        // 主体：15×15米。
        CreatePart(
            tileRoot.transform,
            "Core",
            new Vector3(0f, -0.25f, 0f),
            new Vector3(15f, 0.5f, 15f),
            coreColor);

        // 前后两条包含四角，长度为17米。
        CreatePart(
            tileRoot.transform,
            "Border_North",
            new Vector3(0f, -0.25f, 8f),
            new Vector3(17f, 0.5f, 1f),
            borderColor);

        CreatePart(
            tileRoot.transform,
            "Border_South",
            new Vector3(0f, -0.25f, -8f),
            new Vector3(17f, 0.5f, 1f),
            borderColor);

        // 左右两条长度为15米，避免重复覆盖四角。
        CreatePart(
            tileRoot.transform,
            "Border_East",
            new Vector3(8f, -0.25f, 0f),
            new Vector3(1f, 0.5f, 15f),
            borderColor);

        CreatePart(
            tileRoot.transform,
            "Border_West",
            new Vector3(-8f, -0.25f, 0f),
            new Vector3(1f, 0.5f, 15f),
            borderColor);

        CreatePart(
     tileRoot.transform,
     "DirectionMarker",
     new Vector3(0f, 0.12f, 4f),
     new Vector3(0.5f, 0.24f, 3f),
     Color.white);

        // 标记只显示方向，不参与碰撞。
        Collider markerCollider = tileRoot.transform
            .Find("DirectionMarker")
            .GetComponent<Collider>();

        markerCollider.enabled = false;
        Destroy(markerCollider);

        MapTileInstance instance =
     tileRoot.AddComponent<MapTileInstance>();

        // 此时主体和四边都已经创建完成。
        instance.Initialize(
     coordinate,
     definition,
     definition.heightLevel,
     rotationSteps);

        tiles.Add(coordinate, instance);

        // 先生成新的可见网格和碰撞体。
        TilesChanged?.Invoke();

        // 再请求导航更新。
        mapNavigation.RequestUpdate();

        return true;
    }

    private void CreatePart(
        Transform parent,
        string partName,
        Vector3 localPosition,
        Vector3 localScale,
        Color color)
    {
        GameObject part =
            GameObject.CreatePrimitive(PrimitiveType.Cube);

        part.name = partName;
        part.layer = groundLayer;

        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;

        Renderer partRenderer = part.GetComponent<Renderer>();
        partRenderer.sharedMaterial = tileMaterial;

        MaterialPropertyBlock properties =
            new MaterialPropertyBlock();

        // 兼容URP Lit与Built-in Standard的颜色属性。
        properties.SetColor("_BaseColor", color);
        properties.SetColor("_Color", color);

        partRenderer.SetPropertyBlock(properties);
    }

    public HashSet<Vector2Int> GetAvailablePositions()
    {
        HashSet<Vector2Int> positions =
            new HashSet<Vector2Int>();

        foreach (Vector2Int coordinate in tiles.Keys)
        {
            foreach (Vector2Int direction in Directions)
            {
                Vector2Int candidate = coordinate + direction;

                if (!tiles.ContainsKey(candidate))
                    positions.Add(candidate);
            }
        }

        return positions;
    }

    [ContextMenu("测试放置地块（运行时）")]
    private void TestPlaceTile()
    {
        if (!Application.isPlaying || !initialized)
        {
            Debug.LogWarning("请在地图初始化后的运行模式下测试。", this);
            return;
        }

        bool success = TryPlaceTile(testCoordinate, testTile);

        Debug.Log(
            success
                ? $"放置成功：{testCoordinate}"
                : $"放置失败：{testCoordinate}，检查配置或位置是否合法。",
            this);
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying ||
            !initialized ||
            !showAvailablePositions)
        {
            return;
        }

        Gizmos.color = Color.cyan;

        foreach (Vector2Int coordinate in GetAvailablePositions())
        {
            Gizmos.DrawWireCube(
                GridToWorld(coordinate) + Vector3.up * 0.05f,
                new Vector3(TileSize, 0.02f, TileSize));
        }
    }

    public bool TryGetTile(
    Vector2Int coordinate,
    out MapTileInstance tile)
    {
        return tiles.TryGetValue(coordinate, out tile);
    }

    public bool TryGetNeighbor(
        Vector2Int coordinate,
        Vector2Int direction,
        out MapTileInstance neighbor)
    {
        neighbor = null;

        // 只接受上下左右四种方向。
        if (direction != Vector2Int.up &&
            direction != Vector2Int.down &&
            direction != Vector2Int.left &&
            direction != Vector2Int.right)
        {
            return false;
        }

        return tiles.TryGetValue(
            coordinate + direction,
            out neighbor);
    }

    [ContextMenu("检查测试坐标的邻居（运行时）")]
    private void CheckTestNeighbors()
    {
        if (!Application.isPlaying || !initialized)
        {
            Debug.LogWarning("请在地图初始化后的运行模式下检查。", this);
            return;
        }

        if (!TryGetTile(testCoordinate, out MapTileInstance tile))
        {
            Debug.Log($"位置{testCoordinate}没有已放置地块。", this);
            return;
        }

        Debug.Log(
            $"检查地块：{tile.Coordinate}，" +
            $"Biome：{tile.Biome}，" +
            $"高度：{tile.HeightLevel}",
            tile);

        foreach (Vector2Int direction in Directions)
        {
            string directionName =
                direction == Vector2Int.up ? "上" :
                direction == Vector2Int.down ? "下" :
                direction == Vector2Int.left ? "左" : "右";

            if (TryGetNeighbor(
                    testCoordinate,
                    direction,
                    out MapTileInstance neighbor))
            {
                Debug.Log(
                    $"{directionName}侧邻居：{neighbor.Coordinate}，" +
                    $"Biome：{neighbor.Biome}，" +
                    $"高度：{neighbor.HeightLevel}",
                    neighbor);
            }
            else
            {
                Debug.Log($"{directionName}侧没有邻居。", tile);
            }
        }
    }
}