using System;
using System.Collections.Generic;
using System.Linq;
using Darwin.Rendering;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Generates adapters and baked query data; never modifies the artists' source assets.
public static class MapWaterInstaller
{
    private const string Output = "Assets/Resources/Water";
    private const string Sample = "Assets/Rendering/Moebius/Samples/";
    [MenuItem("Tools/Water/Install map assets")]
    public static void Install()
    {
        if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder("Assets/Resources", "Water");
        var config = AssetDatabase.LoadAssetAtPath<MapWaterAssets>(Output + "/MapWaterAssets.asset");
        if (!config) { config = ScriptableObject.CreateInstance<MapWaterAssets>(); AssetDatabase.CreateAsset(config, Output + "/MapWaterAssets.asset"); }
        var shapes = new List<MapWaterAssets.Shape>();
        foreach (string name in new[] { "L_New", "Cross_River" })
        {
            var prefab = Load<GameObject>(Sample + "RiverConnections/Prefabs/" + name + ".prefab");
            int mask = prefab.GetComponent<MoebiusRiverTile>().profile.ports.Aggregate(0, (m, p) => m | (1 << (int)p.side));
            shapes.Add(new MapWaterAssets.Shape { openings = mask, prefab = prefab });
        }
        shapes.Add(BakeBranch("I")); shapes.Add(BakeBranch("T"));
        config.shapes = shapes.ToArray();
        // The fourth palette entry uses the authored cross, never a synthetic water square.
        config.fullWaterPrefab = Load<GameObject>(Sample + "RiverConnections/Prefabs/Cross_River.prefab");
        config.bankMaterial = Load<Material>(Sample + "RiverConnections/Materials/River_Closure_Grass.mat");
        config.foundationMaterial = Load<Material>(Sample + "RiverConnections/Materials/River_Cliff_Earth.mat");
        config.waterfallPrefab = Load<GameObject>(Sample + "Waterfall/MoebiusWaterfall.prefab");
        config.impactMaterial = Load<Material>(Sample + "Waterfall/Materials/ImpactFoam.mat");
        EditorUtility.SetDirty(config);
        var shader = Shader.Find("Darwin/Population Water Sprite");
        if (!shader) throw new Exception("Water sprite shader has not imported.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(Output + "/WaterPopulationSprite.mat");
        if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, Output + "/WaterPopulationSprite.mat"); }
        material.shader = shader; EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        Debug.Log("Water assets installed: " + string.Join(", ", shapes.Select(s => s.openings.ToString())));
    }
    private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new Exception("Missing asset " + path);
    private static MapWaterAssets.Shape BakeBranch(string branch)
    {
        GameObject source = PrefabUtility.LoadPrefabContents(Sample + "Biomes/Prefabs/River.prefab");
        var wrapper = new GameObject("River " + branch);
        try
        {
            Transform model = source.transform.Find(branch);
            var bank = model.GetComponent<MeshRenderer>();
            Vector3 center = new Vector3(bank.bounds.center.x, bank.bounds.min.y, bank.bounds.center.z);
            model.SetParent(wrapper.transform, true); model.position -= center;
            // I/T source meshes have a 1 m solid plinth beneath their riverbed.
            // Match the L/cross scene convention: bed Y=0, banks Y=1, water Y=.25.
            model.position -= Vector3.up;
            var surface = model.GetComponentsInChildren<MeshRenderer>().First(r => r.name == "Flowing Water");
            Mesh original = surface.GetComponent<MeshFilter>().sharedMesh;
            var mesh = new Mesh { name = "Authored " + branch + " Water", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = original.vertices.Select(p => { var q = surface.transform.TransformPoint(p); q.y = 0; return q; }).ToArray();
            mesh.triangles = original.triangles; mesh.RecalculateBounds(); mesh.RecalculateNormals();
            var profile = SaveProfile(branch, mesh, surface.sharedMaterial, bank.bounds.max.y);
            AlignBank(bank.GetComponent<MeshFilter>(), profile, wrapper.transform);
            var river = wrapper.AddComponent<MoebiusRiverTile>(); river.profile = profile; river.originalWater = surface; river.waterLevel = .25f;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, Output + "/River_" + branch + ".prefab");
            return new MapWaterAssets.Shape { openings = profile.ports.Aggregate(0, (m, p) => m | (1 << (int)p.side)), prefab = prefab };
        }
        finally { Object.DestroyImmediate(wrapper); PrefabUtility.UnloadPrefabContents(source); }
    }
    private static void AlignBank(MeshFilter filter, RiverSurfaceProfile profile, Transform wrapper)
    {
        var source = filter.sharedMesh;
        var mesh = new Mesh { name = "Aligned Authored Bank", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        using (var data = MeshUtility.AcquireReadOnlyMeshData(source))
        {
            var item = data[0];
            using (var vertices = new NativeArray<Vector3>(item.vertexCount, Allocator.Temp))
            {
                item.GetVertices(vertices);
                var writable = vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 p = wrapper.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
                    p.y = Mathf.Max(0, p.y);
                    writable[i] = filter.transform.InverseTransformPoint(wrapper.TransformPoint(p));
                }
                mesh.SetVertices(vertices);
            }
            using (var normals = new NativeArray<Vector3>(item.vertexCount, Allocator.Temp))
            { item.GetNormals(normals); mesh.SetNormals(normals); }
            for (int channel = 0; channel < 2; channel++)
                if (item.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel))
                    using (var uv = new NativeArray<Vector2>(item.vertexCount, Allocator.Temp))
                    { item.GetUVs(channel, uv); mesh.SetUVs(channel, uv); }
            if (item.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
                using (var colors = new NativeArray<Color>(item.vertexCount, Allocator.Temp))
                { item.GetColors(colors); mesh.SetColors(colors); }
            mesh.subMeshCount = item.subMeshCount;
            for (int submesh = 0; submesh < item.subMeshCount; submesh++)
                using (var indices = new NativeArray<int>(item.GetSubMesh(submesh).indexCount, Allocator.Temp))
                { item.GetIndices(indices, submesh, true); mesh.SetIndices(indices.ToArray(), item.GetSubMesh(submesh).topology, submesh); }
        }
        mesh.RecalculateBounds();
        string path = AssetDatabase.GetAssetPath(profile);
        var saved = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => m.name == mesh.name);
        if (saved) { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
        else { saved = mesh; AssetDatabase.AddObjectToAsset(saved, profile); }
        filter.sharedMesh = saved;
    }
    private static RiverSurfaceProfile SaveProfile(string name, Mesh mesh, Material material, float bankHeight)
    {
        string path = Output + "/" + name + "Profile.asset";
        var p = AssetDatabase.LoadAssetAtPath<RiverSurfaceProfile>(path);
        if (!p) { p = ScriptableObject.CreateInstance<RiverSurfaceProfile>(); AssetDatabase.CreateAsset(p, path); }
        if (p.waterMesh) { EditorUtility.CopySerialized(mesh, p.waterMesh); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(p.waterMesh); }
        else { p.waterMesh = mesh; AssetDatabase.AddObjectToAsset(mesh, p); }
        p.waterMaterial = material; p.bankHeight = bankHeight;
        var vertices = p.waterMesh.vertices; var ports = new List<RiverPort>();
        for (int side = 0; side < 4; side++)
        {
            var port = new RiverPort { side = (RiverSide)side }; Vector3 n = port.Normal, tangent = Vector3.Cross(Vector3.up, n);
            float[] edge = vertices.Where(v => Mathf.Abs(Vector3.Dot(v, n) - 8.5f) < .015f).Select(v => Vector3.Dot(v, tangent)).ToArray();
            if (edge.Length < 2 || edge.Max() - edge.Min() < .5f) continue;
            Vector3 c = n * 8.5f + tangent * ((edge.Min() + edge.Max()) * .5f);
            port.center = new Vector2(c.x, c.z); port.width = edge.Max() - edge.Min(); ports.Add(port);
        }
        p.ports = ports.ToArray(); p.shoreRect = new Vector4(-8.5f, -8.5f, 17, 17); p.shoreRange = 3; p.shoreResolution = 128;
        // Bake geometric distance, independent of imported texture readability.
        var edges = new Dictionary<(Vector2Int, Vector2Int), (Vector2 a, Vector2 b, int count)>();
        int[] tris = p.waterMesh.triangles;
        for (int i = 0; i < tris.Length; i += 3) for (int j = 0; j < 3; j++)
        {
            Vector3 a = vertices[tris[i + j]], b = vertices[tris[i + (j + 1) % 3]];
            Vector2 aa = new Vector2(a.x, a.z), bb = new Vector2(b.x, b.z);
            Vector2Int qa = Vector2Int.RoundToInt(aa * 10000), qb = Vector2Int.RoundToInt(bb * 10000);
            var key = qa.x < qb.x || qa.x == qb.x && qa.y <= qb.y ? (qa, qb) : (qb, qa);
            edges.TryGetValue(key, out var old); edges[key] = (aa, bb, old.count + 1);
        }
        var boundary = edges.Values.Where(e => e.count == 1).ToArray();
        p.shoreDistances = new byte[128 * 128];
        for (int z = 0; z < 128; z++) for (int x = 0; x < 128; x++)
        {
            Vector2 point = new Vector2(-8.5f + (x + .5f) * 17 / 128, -8.5f + (z + .5f) * 17 / 128);
            float distance = 3;
            foreach (var edge in boundary)
            {
                Vector2 d = edge.b - edge.a;
                Vector2 q = edge.a + d * Mathf.Clamp01(Vector2.Dot(point - edge.a, d) / Mathf.Max(.000001f, d.sqrMagnitude));
                distance = Mathf.Min(distance, Vector2.Distance(point, q));
            }
            p.shoreDistances[z * 128 + x] = (byte)Mathf.RoundToInt(distance / 3 * 255);
        }
        EditorUtility.SetDirty(p); return p;
    }
}
