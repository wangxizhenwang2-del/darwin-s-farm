using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using DarwinFarm.Environment;

public class MapTileColorTransitionSystem : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private MapGridManager gridManager;
    [SerializeField] private Material vertexColorMaterial;
    private SimulationEnvironmentController environment;
    private readonly Dictionary<Vector2Int, TerrainKind> liveTerrain =
        new Dictionary<Vector2Int, TerrainKind>();
    private readonly Dictionary<BiomeType, Color> terrainColors =
        new Dictionary<BiomeType, Color>();

    private const int Segments = 34;
    private const float HalfSize = 8.5f;

    private static readonly float[] Knots =
    {
        -8.5f, -7.5f, 7.5f, 8.5f
    };

    private class SurfaceData
    {
        public Mesh mesh;
        public MeshRenderer renderer;
        public MeshCollider collider;
        public Transform blockers;
    }

    private readonly Dictionary<MapTileInstance, SurfaceData> surfaces =
        new Dictionary<MapTileInstance, SurfaceData>();

    private readonly Dictionary<MapTileInstance, float[,]> heightControls =
        new Dictionary<MapTileInstance, float[,]>();

    private void OnEnable()
    {
        if (gridManager != null)
        {
            gridManager.TilesChanged += RefreshAll;
            environment = gridManager.GetComponent<SimulationEnvironmentController>();
            if (environment != null) environment.Changed += HandleEnvironmentChanged;
        }
    }

    private void Start()
    {
        if (gridManager == null || vertexColorMaterial == null)
        {
            Debug.LogError("地形过渡系统的引用未设置。", this);
            enabled = false;
            return;
        }

        if (environment != null) CacheTerrain(environment.ReadAll());
        RefreshAll();
    }

    private void OnDisable()
    {
        if (gridManager != null)
            gridManager.TilesChanged -= RefreshAll;
        if (environment != null) environment.Changed -= HandleEnvironmentChanged;
    }

    // 2026-10-09 13:54 +08:00: land colors follow reclassified terrain.
    // Recolor existing meshes only when terrain kind changes; keep geometry
    // and navigation colliders untouched during ordinary climate updates.
    private void HandleEnvironmentChanged(IReadOnlyList<EnvironmentReadSnapshot> snapshots)
    {
        if (CacheTerrain(snapshots)) RefreshColors();
    }

    private void RefreshColors()
    {
        foreach (KeyValuePair<MapTileInstance, SurfaceData> item in surfaces)
        {
            if (item.Key == null || item.Value.mesh == null) continue;
            Vector3[] vertices = item.Value.mesh.vertices;
            var colors = new List<Color>(vertices.Length);
            foreach (Vector3 vertex in vertices)
            {
                Vector2 offset = item.Key.LocalToGridOffset(
                    new Vector2(vertex.x, vertex.z));
                colors.Add(VertexColor(CalculateColor(item.Key, offset)));
            }
            item.Value.mesh.SetColors(colors);
        }
    }

    private bool CacheTerrain(IReadOnlyList<EnvironmentReadSnapshot> snapshots)
    {
        bool changed = false;
        foreach (EnvironmentReadSnapshot snapshot in snapshots)
        {
            Vector2Int coordinate = new Vector2Int(snapshot.Environment.Position.X,
                snapshot.Environment.Position.Y);
            TerrainKind kind = snapshot.Environment.Terrain;
            if (!liveTerrain.TryGetValue(coordinate, out TerrainKind previous) || previous != kind)
            { liveTerrain[coordinate] = kind; changed = true; }
        }
        return changed;
    }

    private void RefreshAll()
    {
        if (gridManager == null || vertexColorMaterial == null)
            return;

        heightControls.Clear();
        terrainColors.Clear();
        foreach (MapTileInstance tile in gridManager.GetPlacedTiles())
            if (tile != null && tile.Definition != null &&
                !terrainColors.ContainsKey(tile.Biome))
                terrainColors.Add(tile.Biome, tile.MainColor);

        // 先统一计算所有地块的高度接口。
        foreach (MapTileInstance tile in gridManager.GetPlacedTiles())
            heightControls.Add(tile, CreateHeightControls(tile));

        foreach (MapTileInstance tile in gridManager.GetPlacedTiles())
        {
            SurfaceData surface = GetOrCreateSurface(tile);

            ClearBlockers(surface.blockers);

            // 修改网格前，解除旧碰撞网格引用。
            surface.collider.sharedMesh = null;

            BuildMesh(tile, surface);

            surface.renderer.sharedMaterial = vertexColorMaterial;
            surface.collider.sharedMesh = surface.mesh;

            DisableOldParts(tile);
        }
    }

    private SurfaceData GetOrCreateSurface(MapTileInstance tile)
    {
        if (surfaces.TryGetValue(tile, out SurfaceData existing))
            return existing;

        GameObject surfaceObject = new GameObject("ColorSurface");
        surfaceObject.layer = tile.Core.gameObject.layer;
        surfaceObject.transform.SetParent(tile.transform, false);

        MeshFilter filter = surfaceObject.AddComponent<MeshFilter>();
        MeshRenderer renderer =
            surfaceObject.AddComponent<MeshRenderer>();

        MeshCollider collider =
            surfaceObject.AddComponent<MeshCollider>();

        collider.convex = false;

        Mesh mesh = new Mesh();
        mesh.name = $"TileTerrain_{tile.Coordinate}";
        filter.sharedMesh = mesh;

        GameObject blockerRoot = new GameObject("CliffBlockers");
        blockerRoot.layer = surfaceObject.layer;
        blockerRoot.transform.SetParent(tile.transform, false);

        SurfaceData data = new SurfaceData
        {
            mesh = mesh,
            renderer = renderer,
            collider = collider,
            blockers = blockerRoot.transform
        };

        surfaces.Add(tile, data);
        return data;
    }

    private float BaseHeight(MapTileInstance tile)
    {
        return gridManager.GridToWorld(tile.Coordinate).y +
               tile.HeightLevel * MapGridManager.HeightStep;
    }

    private float[,] CreateHeightControls(MapTileInstance tile)
    {
        float[,] values = new float[4, 4];

        for (int x = 0; x < 4; x++)
        {
            for (int z = 0; z < 4; z++)
            {
                bool outerX = x == 0 || x == 3;
                bool outerZ = z == 0 || z == 3;

                int sx = x == 0 ? -1 : 1;
                int sz = z == 0 ? -1 : 1;

                if (outerX && outerZ)
                {
                    values[x, z] = CornerHeight(tile, sx, sz);
                }
                else if (outerX)
                {
                    values[x, z] = EdgeHeight(
                        tile, new Vector2Int(sx, 0));
                }
                else if (outerZ)
                {
                    values[x, z] = EdgeHeight(
                        tile, new Vector2Int(0, sz));
                }
                else
                {
                    // 主体四角保持本地块高度。
                    values[x, z] = BaseHeight(tile);
                }
            }
        }

        return values;
    }

    private float EdgeHeight(
        MapTileInstance tile,
        Vector2Int gridDirection)
    {
        if (!gridManager.TryGetNeighbor(
                tile.Coordinate,
                gridDirection,
                out MapTileInstance neighbor))
        {
            return BaseHeight(tile);
        }

        int difference =
            Mathf.Abs(tile.HeightLevel - neighbor.HeightLevel);

        // 悬崖两侧分别保持各自的高度。
        if (difference >= 2)
            return BaseHeight(tile);

        // 平面或坡面：共享边使用相同高度。
        return (BaseHeight(tile) + BaseHeight(neighbor)) * 0.5f;
    }

    private float CornerHeight(
        MapTileInstance tile,
        int sx,
        int sz)
    {
        Vector2Int[] candidates =
        {
            tile.Coordinate,
            tile.Coordinate + new Vector2Int(sx, 0),
            tile.Coordinate + new Vector2Int(0, sz),
            tile.Coordinate + new Vector2Int(sx, sz)
        };

        HashSet<Vector2Int> reached = new HashSet<Vector2Int>();
        Queue<MapTileInstance> pending = new Queue<MapTileInstance>();

        reached.Add(tile.Coordinate);
        pending.Enqueue(tile);

        float total = 0f;
        int count = 0;

        // 同一个角点附近，通过差0或1级的边连接的地块，
        // 使用同一个角点高度。
        while (pending.Count > 0)
        {
            MapTileInstance current = pending.Dequeue();

            total += BaseHeight(current);
            count++;

            foreach (Vector2Int coordinate in candidates)
            {
                if (reached.Contains(coordinate))
                    continue;

                Vector2Int delta = coordinate - current.Coordinate;

                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                    continue;

                if (!gridManager.TryGetTile(
                        coordinate,
                        out MapTileInstance neighbor))
                {
                    continue;
                }

                if (Mathf.Abs(
                        current.HeightLevel - neighbor.HeightLevel) > 1)
                {
                    continue;
                }

                reached.Add(coordinate);
                pending.Enqueue(neighbor);
            }
        }

        return total / count;
    }

    private int FindBand(float position)
    {
        if (position < -7.5f)
            return 0;

        if (position <= 7.5f)
            return 1;

        return 2;
    }

    private float SampleHeight(
        MapTileInstance tile,
        Vector2 gridOffset)
    {
        float[,] values = heightControls[tile];

        int x = FindBand(gridOffset.x);
        int z = FindBand(gridOffset.y);

        float tx = Mathf.InverseLerp(
            Knots[x], Knots[x + 1], gridOffset.x);

        float tz = Mathf.InverseLerp(
            Knots[z], Knots[z + 1], gridOffset.y);

        float lower = Mathf.Lerp(
            values[x, z], values[x + 1, z], tx);

        float upper = Mathf.Lerp(
            values[x, z + 1], values[x + 1, z + 1], tx);

        return Mathf.Lerp(lower, upper, tz);
    }

    private void BuildMesh(
        MapTileInstance tile,
        SurfaceData surface)
    {
        int rowSize = Segments + 1;

        List<Vector3> vertices = new List<Vector3>();
        List<Color> colors = new List<Color>();
        List<int> triangles = new List<int>();

        for (int z = 0; z <= Segments; z++)
        {
            float localZ =
                -HalfSize + z * MapGridManager.TileSize / Segments;

            for (int x = 0; x <= Segments; x++)
            {
                float localX =
                    -HalfSize + x * MapGridManager.TileSize / Segments;

                Vector2 offset = tile.LocalToGridOffset(
                    new Vector2(localX, localZ));

                float localY =
                    SampleHeight(tile, offset) - BaseHeight(tile);

                vertices.Add(new Vector3(localX, localY, localZ));
                colors.Add(VertexColor(CalculateColor(tile, offset)));
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

                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);

                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(d);
            }
        }

        BuildEdge(tile, surface, Vector2Int.left,
            new Vector3(-HalfSize, 0f, -HalfSize),
            new Vector3(-HalfSize, 0f, HalfSize),
            vertices, colors, triangles);

        BuildEdge(tile, surface, Vector2Int.up,
            new Vector3(-HalfSize, 0f, HalfSize),
            new Vector3(HalfSize, 0f, HalfSize),
            vertices, colors, triangles);

        BuildEdge(tile, surface, Vector2Int.right,
            new Vector3(HalfSize, 0f, HalfSize),
            new Vector3(HalfSize, 0f, -HalfSize),
            vertices, colors, triangles);

        BuildEdge(tile, surface, Vector2Int.down,
            new Vector3(HalfSize, 0f, -HalfSize),
            new Vector3(-HalfSize, 0f, -HalfSize),
            vertices, colors, triangles);

        surface.mesh.Clear();
        surface.mesh.SetVertices(vertices);
        surface.mesh.SetColors(colors);
        surface.mesh.SetTriangles(triangles, 0);
        surface.mesh.RecalculateNormals();
        surface.mesh.RecalculateBounds();
    }

    private void BuildEdge(
        MapTileInstance tile,
        SurfaceData surface,
        Vector2Int localDirection,
        Vector3 start,
        Vector3 end,
        List<Vector3> vertices,
        List<Color> colors,
        List<int> triangles)
    {
        Vector2Int gridDirection =
            tile.LocalToGridDirection(localDirection);

        bool hasNeighbor = gridManager.TryGetNeighbor(
            tile.Coordinate, gridDirection, out MapTileInstance neighbor);

        if (hasNeighbor)
        {
            int difference = tile.HeightLevel - neighbor.HeightLevel;

            // 平面和坡面不需要墙。
            // 悬崖只由较高一侧生成，避免重复。
            if (difference < 2)
                return;

            CreateCliffBlocker(
                tile, neighbor, localDirection, surface.blockers);
        }

        for (int i = 0; i < Segments; i++)
        {
            Vector3 a = Vector3.Lerp(
                start, end, (float)i / Segments);

            Vector3 b = Vector3.Lerp(
                start, end, (float)(i + 1) / Segments);

            Vector2 offsetA = tile.LocalToGridOffset(
                new Vector2(a.x, a.z));

            Vector2 offsetB = tile.LocalToGridOffset(
                new Vector2(b.x, b.z));

            float topA = SampleHeight(tile, offsetA);
            float topB = SampleHeight(tile, offsetB);

            float bottomA;
            float bottomB;

            if (hasNeighbor)
            {
                Vector2 neighborShift = new Vector2(
                    gridDirection.x * MapGridManager.TileSize,
                    gridDirection.y * MapGridManager.TileSize);

                bottomA = SampleHeight(
                    neighbor, offsetA - neighborShift);

                bottomB = SampleHeight(
                    neighbor, offsetB - neighborShift);
            }
            else
            {
                bottomA = topA - 0.5f;
                bottomB = topB - 0.5f;
            }

            float baseHeight = BaseHeight(tile);

            int index = vertices.Count;

            vertices.Add(new Vector3(a.x, topA - baseHeight, a.z));
            vertices.Add(new Vector3(b.x, topB - baseHeight, b.z));
            vertices.Add(new Vector3(a.x, bottomA - baseHeight, a.z));
            vertices.Add(new Vector3(b.x, bottomB - baseHeight, b.z));

            Color colorA = CalculateColor(tile, offsetA);
            Color colorB = CalculateColor(tile, offsetB);

            colors.Add(VertexColor(colorA));
            colors.Add(VertexColor(colorB));
            colors.Add(VertexColor(colorA));
            colors.Add(VertexColor(colorB));

            // 墙面朝向地块外侧。
            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 1);

            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
        }
    }

    private void CreateCliffBlocker(
        MapTileInstance tile,
        MapTileInstance neighbor,
        Vector2Int localDirection,
        Transform parent)
    {
        GameObject blocker = new GameObject("Cliff_NotWalkable");
        blocker.layer = tile.Core.gameObject.layer;
        blocker.transform.SetParent(parent, false);

        NavMeshModifierVolume volume =
            blocker.AddComponent<NavMeshModifierVolume>();

        float high = BaseHeight(tile);
        float low = BaseHeight(neighbor);

        volume.center = new Vector3(
            localDirection.x * HalfSize,
            (high + low) * 0.5f - high,
            localDirection.y * HalfSize);

        volume.size = localDirection.x != 0
            ? new Vector3(0.6f, high - low + 4f, 17.2f)
            : new Vector3(17.2f, high - low + 4f, 0.6f);

        volume.area = NavMesh.GetAreaFromName("Not Walkable");
    }

    private void ClearBlockers(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            GameObject child = root.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
    }

    private Color CalculateColor(
        MapTileInstance tile,
        Vector2 gridOffset)
    {
        Color total = Color.black;
        float totalWeight = 0f;

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Vector2Int coordinate =
                    tile.Coordinate + new Vector2Int(dx, dz);

                if (!gridManager.TryGetTile(
                        coordinate, out MapTileInstance neighbor))
                {
                    continue;
                }

                float weightX = AxisWeight(Mathf.Abs(
                    gridOffset.x - dx * MapGridManager.TileSize));

                float weightZ = AxisWeight(Mathf.Abs(
                    gridOffset.y - dz * MapGridManager.TileSize));

                float weight = weightX * weightZ;

                Color neighborColor = LiveColor(neighbor);
                total.r += neighborColor.r * weight;
                total.g += neighborColor.g * weight;
                total.b += neighborColor.b * weight;

                totalWeight += weight;
            }
        }

        if (totalWeight <= 0f)
            return LiveColor(tile);

        return new Color(
            total.r / totalWeight,
            total.g / totalWeight,
            total.b / totalWeight,
            1f);
    }

    private Color LiveColor(MapTileInstance tile)
    {
        if (tile.Block == null || tile.Block.waterCoverage != WaterCoverage.Land ||
            !liveTerrain.TryGetValue(tile.Coordinate, out TerrainKind kind))
            return tile.MainColor;
        BiomeType biome = (BiomeType)((int)kind + (int)BiomeType.Forest);
        if (terrainColors.TryGetValue(biome, out Color color)) return color;
        // Match shipped assets even when that terrain preset is not placed.
        switch (kind)
        {
            case TerrainKind.Forest: return new Color(0.20392157f, 0.54509804f, 0.33333333f);
            case TerrainKind.Rainforest: return new Color(0.09019608f, 0.47843137f, 0.34509804f);
            case TerrainKind.Grassland: return new Color(0.51372549f, 0.72549020f, 0.34509804f);
            case TerrainKind.Desert: return new Color(0.78823529f, 0.79607843f, 0.55294118f);
            case TerrainKind.Tundra: return new Color(0.64705882f, 0.79607843f, 0.64705882f);
            case TerrainKind.Highland: return new Color(0.53725490f, 0.65098039f, 0.61176471f);
            case TerrainKind.SnowMountain: return new Color(0.86666667f, 0.92549020f, 0.90980392f);
            case TerrainKind.Volcano: return new Color(0.41960784f, 0.47450980f, 0.43137255f);
            default: return tile.MainColor;
        }
    }

    private float AxisWeight(float distance)
    {
        float t = Mathf.Clamp01((9.5f - distance) / 2f);
        return t * t * (3f - 2f * t);
    }

    private Color VertexColor(Color color)
    {
        return QualitySettings.activeColorSpace == ColorSpace.Linear
            ? color.linear
            : color;
    }

    private void DisableOldParts(MapTileInstance tile)
    {
        Renderer[] parts =
        {
            tile.Core,
            tile.NorthBorder,
            tile.SouthBorder,
            tile.EastBorder,
            tile.WestBorder
        };

        foreach (Renderer part in parts)
        {
            if (part == null)
                continue;

            part.enabled = false;

            Collider collider = part.GetComponent<Collider>();

            if (collider != null)
                collider.enabled = false;
        }
    }

    private void OnDestroy()
    {
        foreach (SurfaceData surface in surfaces.Values)
        {
            if (surface.mesh != null)
                Destroy(surface.mesh);
        }

        surfaces.Clear();
        heightControls.Clear();
    }
}