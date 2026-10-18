using System.Collections.Generic;
using UnityEngine;

public partial class MapTileColorTransitionSystem
{
    private static float SampleAuthoredHeight(BiomeGroundData artwork, float x, float z)
    {
        float gx = Mathf.Clamp((x + HalfSize) * Segments / MapGridManager.TileSize, 0f, Segments);
        float gz = Mathf.Clamp((z + HalfSize) * Segments / MapGridManager.TileSize, 0f, Segments);
        int ix = Mathf.Min(Mathf.FloorToInt(gx), Segments - 1);
        int iz = Mathf.Min(Mathf.FloorToInt(gz), Segments - 1);
        int a = iz * (Segments + 1) + ix;
        return Mathf.Lerp(Mathf.Lerp(artwork.heights[a], artwork.heights[a + 1], gx - ix),
            Mathf.Lerp(artwork.heights[a + Segments + 1], artwork.heights[a + Segments + 2], gx - ix), gz - iz);
    }

    private void BuildNativeGroundCollision(MapTileInstance tile, SurfaceData surface)
    {
        surface.fullTriangles = surface.mesh.triangles;
        surface.rimTriangles = null;
        BiomeGroundData artwork = GroundArtwork(VisibleBiome(tile));
        if (artwork == null)
        {
            if (surface.nativeCollision != null) surface.nativeCollision.Clear();
            surface.collider.sharedMesh = surface.mesh;
            return;
        }
        Vector3[] grid = surface.mesh.vertices;
        var rim = new List<int>();
        // Interior geometry is drawn and collided by the original mesh. Retain
        // the grid only for the one-metre joining strip and the outside walls.
        for (int i = 0; i < surface.fullTriangles.Length; i += 3)
        {
            int a = surface.fullTriangles[i], b = surface.fullTriangles[i + 1], c = surface.fullTriangles[i + 2];
            Vector3 center = (grid[a] + grid[b] + grid[c]) / 3f;
            bool wall = a >= (Segments + 1) * (Segments + 1) ||
                b >= (Segments + 1) * (Segments + 1) || c >= (Segments + 1) * (Segments + 1);
            if (wall || Mathf.Max(Mathf.Abs(center.x), Mathf.Abs(center.z)) >= ReliefHalfWidth - .01f)
            { rim.Add(a); rim.Add(b); rim.Add(c); }
        }
        surface.rimTriangles = rim.ToArray();
        var vertices = new List<Vector3>(grid);
        foreach (Vector3 point in artwork.sourceVertices)
        {
            Vector2 offset = tile.LocalToGridOffset(new Vector2(point.x, point.z));
            vertices.Add(new Vector3(point.x, SampleHeight(tile, offset) - BaseHeight(tile) +
                point.y - artwork.sourceMeanHeight, point.z));
        }
        foreach (int index in artwork.sourceTriangles) rim.Add(grid.Length + index);
        if (surface.nativeCollision == null)
            surface.nativeCollision = new Mesh { name = "NativeGroundCollision_" + tile.Coordinate,
                hideFlags = HideFlags.DontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        surface.collider.sharedMesh = null;
        surface.nativeCollision.Clear();
        surface.nativeCollision.SetVertices(vertices);
        surface.nativeCollision.SetTriangles(rim, 0);
        surface.nativeCollision.RecalculateBounds();
        surface.collider.sharedMesh = surface.nativeCollision;
    }

    private void ProjectNativeGround(BiomeVisualData data)
    {
        BiomeGroundData artwork = GroundArtwork(data.biome);
        if (artwork == null || data.root == null) return;
        BiomeNativeGround native = data.root.GetComponent<BiomeNativeGround>();
        if (native == null) native = data.root.AddComponent<BiomeNativeGround>();
        MapTileInstance tile = data.tile;
        native.Project(tile.transform, name => IsGroundMesh(data.biome, name), point =>
        {
            Vector2 offset = tile.LocalToGridOffset(new Vector2(point.x, point.z));
            point.y += SampleHeight(tile, offset) - BaseHeight(tile) - artwork.sourceMeanHeight;
            return point;
        });
    }

    private void ShowNativeGround(SurfaceData surface, bool show)
    {
        if (surface.rimTriangles == null) return;
        surface.mesh.SetTriangles(show ? surface.rimTriangles : surface.fullTriangles, 0);
    }

    private void SetNativeGroundVisible(BiomeVisualData data, bool visible)
    {
        if (data.root == null) return;
        BiomeNativeGround native = data.root.GetComponent<BiomeNativeGround>();
        if (native == null) return;
        visible &= UsesBiomeArtwork(data.biome);
        native.SetVisible(visible);
        if (data.tile != null && surfaces.TryGetValue(data.tile, out SurfaceData surface))
            ShowNativeGround(surface, visible);
    }
}
