using System.Collections.Generic;
using UnityEngine;

public class MapTileColorTransitionSystem : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private MapGridManager gridManager;
    [SerializeField] private Material vertexColorMaterial;

    // 17米 / 34段 = 每段0.5米。
    private const int Segments = 34;
    private const float HalfSize = 8.5f;

    private class SurfaceData
    {
        public Mesh mesh;
        public MeshRenderer renderer;
    }

    private readonly Dictionary<MapTileInstance, SurfaceData> surfaces =
        new Dictionary<MapTileInstance, SurfaceData>();

    private void OnEnable()
    {
        if (gridManager != null)
            gridManager.TilesChanged += RefreshAll;
    }

    private void Start()
    {
        if (gridManager == null || vertexColorMaterial == null)
        {
            Debug.LogError("颜色过渡系统的引用未设置。", this);
            enabled = false;
            return;
        }

        RefreshAll();
    }

    private void OnDisable()
    {
        if (gridManager != null)
            gridManager.TilesChanged -= RefreshAll;
    }

    private void RefreshAll()
    {
        if (gridManager == null || vertexColorMaterial == null)
            return;

        foreach (MapTileInstance tile in gridManager.GetPlacedTiles())
        {
            SurfaceData surface = GetOrCreateSurface(tile);

            UpdateSurface(tile, surface.mesh);

            surface.renderer.sharedMaterial = vertexColorMaterial;

            HideOriginalRenderers(tile);
        }
    }

    private SurfaceData GetOrCreateSurface(MapTileInstance tile)
    {
        if (surfaces.TryGetValue(tile, out SurfaceData existing))
            return existing;

        GameObject surfaceObject = new GameObject("ColorSurface");

        surfaceObject.layer = tile.Core.gameObject.layer;

        surfaceObject.transform.SetParent(tile.transform, false);
        surfaceObject.transform.localPosition = Vector3.zero;
        surfaceObject.transform.localRotation = Quaternion.identity;
        surfaceObject.transform.localScale = Vector3.one;

        MeshFilter filter = surfaceObject.AddComponent<MeshFilter>();
        MeshRenderer renderer =
            surfaceObject.AddComponent<MeshRenderer>();

        // 在运行时方法里创建Unity对象，不在字段初始化中创建。
        Mesh mesh = new Mesh();
        mesh.name = $"TileColorMesh_{tile.Coordinate}";

        filter.sharedMesh = mesh;
        renderer.sharedMaterial = vertexColorMaterial;

        SurfaceData surface = new SurfaceData
        {
            mesh = mesh,
            renderer = renderer
        };

        surfaces.Add(tile, surface);

        return surface;
    }

    private void UpdateSurface(MapTileInstance tile, Mesh mesh)
    {
        int rowSize = Segments + 1;

        List<Vector3> vertices = new List<Vector3>();
        List<Color> colors = new List<Color>();
        List<int> triangles = new List<int>();

        // 生成整块顶面。
        // 中间15×15米全部保持本地块颜色。
        for (int z = 0; z <= Segments; z++)
        {
            float localZ =
                -HalfSize + z * MapGridManager.TileSize / Segments;

            for (int x = 0; x <= Segments; x++)
            {
                float localX =
                    -HalfSize + x * MapGridManager.TileSize / Segments;

                vertices.Add(new Vector3(localX, 0f, localZ));

                colors.Add(ToVertexColor(
                    CalculateColor(tile, localX, localZ)));
            }
        }

        for (int z = 0; z < Segments; z++)
        {
            for (int x = 0; x < Segments; x++)
            {
                int a = z * rowSize + x;
                int b = a + 1;
                int c = a + rowSize;
                int d = c + 1;

                // 顶面朝上。
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);

                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(d);
            }
        }

        // 给地图外边缘补0.5米厚的侧面。
        // 有邻居的边不生成侧面。
        AddSideIfExposed(
            tile, Vector2Int.left,
            new Vector3(-HalfSize, 0f, -HalfSize),
            new Vector3(-HalfSize, 0f, HalfSize),
            vertices, colors, triangles);

        AddSideIfExposed(
            tile, Vector2Int.up,
            new Vector3(-HalfSize, 0f, HalfSize),
            new Vector3(HalfSize, 0f, HalfSize),
            vertices, colors, triangles);

        AddSideIfExposed(
            tile, Vector2Int.right,
            new Vector3(HalfSize, 0f, HalfSize),
            new Vector3(HalfSize, 0f, -HalfSize),
            vertices, colors, triangles);

        AddSideIfExposed(
            tile, Vector2Int.down,
            new Vector3(HalfSize, 0f, -HalfSize),
            new Vector3(-HalfSize, 0f, -HalfSize),
            vertices, colors, triangles);

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private Color CalculateColor(
        MapTileInstance tile,
        float localX,
        float localZ)
    {

        Vector2 gridOffset =
    tile.LocalToGridOffset(new Vector2(localX, localZ));

        localX = gridOffset.x;
        localZ = gridOffset.y;

        Color total = Color.black;
        float totalWeight = 0f;

        // 颜色计算包括对角地块；
        // 放置规则仍然只允许上下左右连接。
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Vector2Int coordinate =
                    tile.Coordinate + new Vector2Int(dx, dz);

                if (!gridManager.TryGetTile(
                        coordinate,
                        out MapTileInstance neighbor))
                {
                    continue;
                }

                float distanceX = Mathf.Abs(
                    localX - dx * MapGridManager.TileSize);

                float distanceZ = Mathf.Abs(
                    localZ - dz * MapGridManager.TileSize);

                float weightX = AxisWeight(distanceX);
                float weightZ = AxisWeight(distanceZ);

                float weight = weightX * weightZ;

                if (weight <= 0f)
                    continue;

                total.r += neighbor.MainColor.r * weight;
                total.g += neighbor.MainColor.g * weight;
                total.b += neighbor.MainColor.b * weight;

                totalWeight += weight;
            }
        }

        if (totalWeight <= 0f)
            return tile.MainColor;

        return new Color(
            total.r / totalWeight,
            total.g / totalWeight,
            total.b / totalWeight,
            1f);
    }

    private float AxisWeight(float distance)
    {
        // 距中心7.5米以内权重为1。
        // 从7.5米到9.5米平滑降低到0。
        float t = Mathf.Clamp01((9.5f - distance) / 2f);

        return t * t * (3f - 2f * t);
    }

    private Color ToVertexColor(Color color)
    {
        // Inspector颜色与材质颜色保持一致。
        return QualitySettings.activeColorSpace == ColorSpace.Linear
            ? color.linear
            : color;
    }

    private void AddSideIfExposed(
        MapTileInstance tile,
        Vector2Int direction,
        Vector3 start,
        Vector3 end,
        List<Vector3> vertices,
        List<Color> colors,
        List<int> triangles)
    {
        Vector2Int gridDirection =
    tile.LocalToGridDirection(direction);

        if (gridManager.TryGetNeighbor(
                tile.Coordinate,
                gridDirection,
                out MapTileInstance neighbor))
        {
            return;
        }

        for (int i = 0; i < Segments; i++)
        {
            Vector3 a = Vector3.Lerp(
                start, end, (float)i / Segments);

            Vector3 b = Vector3.Lerp(
                start, end, (float)(i + 1) / Segments);

            int index = vertices.Count;

            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(a + Vector3.down * 0.5f);
            vertices.Add(b + Vector3.down * 0.5f);

            Color colorA = ToVertexColor(
                CalculateColor(tile, a.x, a.z));

            Color colorB = ToVertexColor(
                CalculateColor(tile, b.x, b.z));

            colors.Add(colorA);
            colors.Add(colorB);
            colors.Add(colorA);
            colors.Add(colorB);

            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);

            triangles.Add(index + 1);
            triangles.Add(index + 3);
            triangles.Add(index + 2);
        }
    }

    private void HideOriginalRenderers(MapTileInstance tile)
    {
        if (tile.Core != null)
            tile.Core.enabled = false;

        if (tile.NorthBorder != null)
            tile.NorthBorder.enabled = false;

        if (tile.SouthBorder != null)
            tile.SouthBorder.enabled = false;

        if (tile.EastBorder != null)
            tile.EastBorder.enabled = false;

        if (tile.WestBorder != null)
            tile.WestBorder.enabled = false;
    }

    private void OnDestroy()
    {
        foreach (SurfaceData surface in surfaces.Values)
        {
            if (surface.mesh != null)
                Destroy(surface.mesh);
        }

        surfaces.Clear();
    }
}