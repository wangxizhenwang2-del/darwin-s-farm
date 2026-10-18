using System.Collections.Generic;
using UnityEngine;

public partial class MapTileColorTransitionSystem
{
    public GameObject CreatePlacementVisualPreview(Transform parent, MapTileDefinition definition,
        Material material, out Mesh groundMesh)
    {
        groundMesh = null;
        if (definition == null) return null;
        if (definition.initialWaterCoverage != WaterCoverage.Land)
            return WaterPlacementPreview.Create(parent, definition, material, out groundMesh);
        return CreatePlacementVisualPreview(parent, definition.biome, material, out groundMesh);
    }

    // Uses the same calibrated source and decoration projection as placed tiles.
    // This root stays outside MapGeometry and never owns terrain or obstacles.
    public GameObject CreatePlacementVisualPreview(Transform parent, BiomeType biome,
        Material material, out Mesh groundMesh)
    {
        groundMesh = null;
        GameObject prefab = BiomePrefab(biome);
        if (prefab == null || parent == null || material == null) return null;

        var root = new GameObject("BiomePlacementPreview_" + biome);
        root.transform.SetParent(parent, false);
        root.layer = 2;
        var surface = new GameObject("PreviewRelief");
        surface.transform.SetParent(root.transform, false);
        surface.layer = 2;
        groundMesh = BuildPlacementRelief(biome);
        surface.AddComponent<MeshFilter>().sharedMesh = groundMesh;
        surface.AddComponent<MeshRenderer>().sharedMaterial = material;
        // Direct raycasts during construction only; disable before returning.
        MeshCollider probe = surface.AddComponent<MeshCollider>();
        probe.sharedMesh = groundMesh;
        GameObject model = Instantiate(prefab, root.transform);
        model.name = "PreviewDecorations";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
        SetVisualLayer(model.transform);
        foreach (Behaviour component in model.GetComponentsInChildren<Behaviour>(true))
            component.enabled = false;
        foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }
        ProjectDecorations(model, probe, biome, false);
        probe.enabled = false;
        if (Application.isPlaying) Destroy(probe);
        else DestroyImmediate(probe);
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var materials = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        return root;
    }

    private Mesh BuildPlacementRelief(BiomeType biome)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        int row = Segments + 1;
        for (int z = 0; z <= Segments; z++)
            for (int x = 0; x <= Segments; x++)
            {
                float px = -HalfSize + x * MapGridManager.TileSize / Segments;
                float pz = -HalfSize + z * MapGridManager.TileSize / Segments;
                vertices.Add(new Vector3(px, SampleBiomeRelief(biome, px, pz), pz));
            }
        for (int z = 0; z < Segments; z++)
            for (int x = 0; x < Segments; x++)
            {
                int a = z * row + x, b = a + 1, c = a + row, d = c + 1;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
        Vector3[] corners =
        {
            new Vector3(-HalfSize, 0f, -HalfSize), new Vector3(-HalfSize, 0f, HalfSize),
            new Vector3(HalfSize, 0f, HalfSize), new Vector3(HalfSize, 0f, -HalfSize)
        };
        for (int i = 0; i < corners.Length; i++)
        {
            int index = vertices.Count;
            Vector3 a = corners[i], b = corners[(i + 1) % corners.Length];
            vertices.Add(a); vertices.Add(b);
            vertices.Add(a + Vector3.down * 0.5f); vertices.Add(b + Vector3.down * 0.5f);
            triangles.AddRange(new[] { index, index + 2, index + 1, index + 1, index + 2, index + 3 });
        }
        var mesh = new Mesh { name = "PlacementRelief_" + biome };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
