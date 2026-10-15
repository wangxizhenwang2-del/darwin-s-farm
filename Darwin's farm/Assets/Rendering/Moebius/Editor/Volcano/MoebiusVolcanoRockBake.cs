using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Darwin.Rendering.Editor
{
    /// <summary>
    /// One-time UV bake for the placed Volcano (1). R is distance from the actual Lava mesh,
    /// G is terrace slope, B is summit height, A is fixed painted variation.
    /// No lava proximity or slope query runs in the player.
    /// </summary>
    public static class MoebiusVolcanoRockBake
    {
        const string ScenePath = "Assets/Scenes/New Scene.unity";
        const string Folder = "Assets/Rendering/Moebius/Content/Volcano";
        const string MaskPath = Folder + "/Volcano1_RockBakedMask.png";
        const string MaterialPath = Folder + "/Volcano1_Rock.mat";
        const string MeshPath = Folder + "/Volcano1_RockUniqueUV.asset";
        const string OutlineMeshPath = Folder + "/Volcano1_RockOutlineMesh.asset";
        const string OutlineMaterialPath = Folder + "/Volcano1_RockOutline.mat";
        const int Size = 1024;

        [MenuItem("Tools/Moebius/Volcano/Bake Rock from Lava Contact")]
        public static void Bake()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject volcano = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == "Volcano (1)") volcano = root;
                }
                if (!volcano) throw new InvalidOperationException("Volcano (1) was not found in New Scene.");
                var rock = volcano.transform.Find("Rock")?.GetComponent<MeshFilter>();
                var lava = volcano.transform.Find("Lava")?.GetComponent<MeshFilter>();
                if (!rock || !lava || !rock.sharedMesh || !lava.sharedMesh)
                    throw new InvalidOperationException("Volcano (1) must contain Rock and Lava MeshFilters.");

                // Importer read/write is enabled only for the editor bake, then restored.
                var importer = AssetImporter.GetAtPath("Assets/Building/Volcano.fbx") as ModelImporter;
                bool wasReadable = importer && importer.isReadable;
                if (importer && !wasReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
                try
                {
                    Directory.CreateDirectory(Folder);
                    AssetDatabase.Refresh();
                    var copy = UnityEngine.Object.Instantiate(rock.sharedMesh);
                    copy.name = "Volcano1_RockUniqueUV";
                    if (!Unwrapping.GenerateSecondaryUVSet(copy) || copy.uv2.Length != copy.vertexCount)
                        throw new InvalidOperationException("Unity could not generate a unique UV atlas for Rock.");
                    copy.uv = copy.uv2;
                    var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                    if (existingMesh)
                    {
                        EditorUtility.CopySerialized(copy, existingMesh);
                        UnityEngine.Object.DestroyImmediate(copy);
                        copy = existingMesh;
                        EditorUtility.SetDirty(copy);
                    }
                    else AssetDatabase.CreateAsset(copy, MeshPath);
                    Undo.RecordObject(rock, "Assign unique UV volcano mesh");
                    rock.sharedMesh = copy;
                    EditorUtility.SetDirty(rock);
                    BakeMesh(rock, lava);
                }
                finally
                {
                    if (importer && !wasReadable)
                    {
                        importer.isReadable = false;
                        importer.SaveAndReimport();
                    }
                }

                var shader = Shader.Find("Darwin/Moebius Volcano Rock Baked");
                if (!shader)
                    throw new InvalidOperationException("The new volcano rock shader is missing.");
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if (!material)
                {
                    material = new Material(shader) { name = "Volcano1_Rock" };
                    AssetDatabase.CreateAsset(material, MaterialPath);
                }
                ConfigureRockMaterial(material, shader);
                var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
                if (!mask) throw new InvalidOperationException("Baked texture was not imported.");
                material.SetTexture("_VolcanoMask", mask);
                EditorUtility.SetDirty(material);

                var rockRenderer = rock.GetComponent<MeshRenderer>();
                if (!rockRenderer) throw new InvalidOperationException("Rock MeshRenderer is missing.");
                Undo.RecordObject(rockRenderer, "Assign baked volcano rock");
                rockRenderer.sharedMaterial = material;
                EditorUtility.SetDirty(rockRenderer);
                CreateOutline(rock, rockRenderer);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Volcano Rock Bake] Applied {MaskPath} to {volcano.name}/Rock. Other objects were not modified.");
            }
            finally
            {
                if (opened && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void BakeMesh(MeshFilter rock, MeshFilter lava)
        {
            Mesh rockMesh = rock.sharedMesh;
            Mesh lavaMesh = lava.sharedMesh;
            var vertices = rockMesh.vertices;
            var uv = rockMesh.uv;
            var triangles = rockMesh.triangles;
            if (uv.Length != vertices.Length || uv.Length == 0)
                throw new InvalidOperationException("Rock requires unique UV0 coordinates for this bake.");
            if (lavaMesh.vertexCount == 0 || lavaMesh.triangles.Length == 0)
                throw new InvalidOperationException("Lava mesh is empty.");

            var rockRenderer = rock.GetComponent<Renderer>();
            float height = rockRenderer.bounds.size.y;
            if (height <= 0.001f) throw new InvalidOperationException("Rock world-space height is invalid.");
            float yMin = rockRenderer.bounds.min.y;
            float innerContact = height * 0.002f;
            float outerContact = height * 0.035f;

            var lavaSurface = new TriangleTree(lava);

            var pixels = new Color[Size * Size];
            var owner = new Vector3[Size * Size];
            var covered = new bool[Size * Size];
            int occupied = 0, conflict = 0, contactPixels = 0;
            {
                Matrix4x4 toWorld = rock.transform.localToWorldMatrix;
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    Vector2 ua = uv[a] * Size, ub = uv[b] * Size, uc = uv[c] * Size;
                    float denom = Cross(ub - ua, uc - ua);
                    if (Mathf.Abs(denom) < 0.00001f) continue;
                    int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(ua.x, Mathf.Min(ub.x, uc.x))) - 1, 0, Size - 1);
                    int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(ua.x, Mathf.Max(ub.x, uc.x))) + 1, 0, Size - 1);
                    int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(ua.y, Mathf.Min(ub.y, uc.y))) - 1, 0, Size - 1);
                    int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(ua.y, Mathf.Max(ub.y, uc.y))) + 1, 0, Size - 1);
                    Vector3 pa = toWorld.MultiplyPoint3x4(vertices[a]);
                    Vector3 pb = toWorld.MultiplyPoint3x4(vertices[b]);
                    Vector3 pc = toWorld.MultiplyPoint3x4(vertices[c]);
                    Vector3 normal = Vector3.Cross(pb - pa, pc - pa).normalized;
                    float terrace = Smooth(0.35f, 0.78f, Mathf.Abs(normal.y));
                    for (int y = minY; y <= maxY; y++)
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                        float wb = Cross(p - ua, uc - ua) / denom;
                        float wc = Cross(ub - ua, p - ua) / denom;
                        float wa = 1 - wb - wc;
                        if (wa < -0.0001f || wb < -0.0001f || wc < -0.0001f) continue;
                        Vector3 world = pa * wa + pb * wb + pc * wc;
                        int index = y * Size + x;
                        if (covered[index])
                        {
                            if ((owner[index] - world).magnitude > height * 0.02f) conflict++;
                            continue;
                        }
                        covered[index] = true;
                        owner[index] = world;
                        occupied++;
                        float distance = Mathf.Sqrt(lavaSurface.DistanceSquared(world));
                        float contact = 1 - Smooth(innerContact, outerContact, distance);
                        if (contact > 0.05f) contactPixels++;
                        float summit = Smooth(0.74f, 0.96f, (world.y - yMin) / height);
                        float brush = 0.5f + (Mathf.PerlinNoise(world.x * 0.37f, world.z * 0.37f) - 0.5f) * 0.58f
                            + (Mathf.PerlinNoise(world.x * 1.2f + 41, world.y * 1.2f + 19) - 0.5f) * 0.2f;
                        pixels[index] = new Color(contact, terrace, summit, Mathf.Clamp01(brush));
                    }
                }
                if (occupied < Size * Size * 0.02f)
                    throw new InvalidOperationException($"UV coverage too low: {occupied} pixels.");
                if (conflict > occupied * 0.005f)
                    throw new InvalidOperationException($"UV islands overlap in 3D: {conflict} conflicting pixels. Re-unwrap Rock before baking.");
                if (contactPixels < 30)
                    throw new InvalidOperationException($"Only {contactPixels} lava-contact pixels found. Check that Rock and Lava are touching.");

                // Extend island edges into padding so bilinear and mip sampling stay clean.
                for (int pass = 0; pass < 8; pass++)
                {
                    var next = (Color[])pixels.Clone();
                    var nextCovered = (bool[])covered.Clone();
                    for (int y = 1; y < Size - 1; y++)
                    for (int x = 1; x < Size - 1; x++)
                    {
                        int i = y * Size + x;
                        if (covered[i]) continue;
                        int[] neighbors = { i - 1, i + 1, i - Size, i + Size };
                        foreach (int n in neighbors)
                        if (covered[n]) { next[i] = pixels[n]; nextCovered[i] = true; break; }
                    }
                    pixels = next;
                    covered = nextCovered;
                }

                Directory.CreateDirectory(Folder);
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(MaskPath, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(MaskPath, ImportAssetOptions.ForceSynchronousImport);
                var textureImporter = AssetImporter.GetAtPath(MaskPath) as TextureImporter;
                if (textureImporter)
                {
                    textureImporter.sRGBTexture = false;
                    textureImporter.mipmapEnabled = true;
                    textureImporter.wrapMode = TextureWrapMode.Clamp;
                    textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                    textureImporter.SaveAndReimport();
                }
                Debug.Log($"[Volcano Rock Bake] UV pixels={occupied}, contact pixels={contactPixels}, overlap conflicts={conflict}, height={height:F3}m, contact outer radius={outerContact:F3}m.");
            }
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static void CreateOutline(MeshFilter rock, MeshRenderer rockRenderer)
        {
            var shader = Shader.Find("Darwin/Moebius Volcano Ink Outline");
            if (!shader) throw new InvalidOperationException("Volcano outline shader is missing.");
            var outlineMesh = UnityEngine.Object.Instantiate(rock.sharedMesh);
            outlineMesh.name = "Volcano1_RockOutlineMesh";
            var vertices = outlineMesh.vertices;
            var normals = outlineMesh.normals;
            var sums = new Dictionary<Vector3Int, Vector3>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var key = Rounded(vertices[i]);
                sums[key] = sums.TryGetValue(key, out var sum) ? sum + normals[i] : normals[i];
            }
            for (int i = 0; i < vertices.Length; i++) normals[i] = sums[Rounded(vertices[i])].normalized;
            outlineMesh.normals = normals;
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(OutlineMeshPath);
            if (existingMesh)
            {
                EditorUtility.CopySerialized(outlineMesh, existingMesh);
                UnityEngine.Object.DestroyImmediate(outlineMesh);
                outlineMesh = existingMesh;
                EditorUtility.SetDirty(outlineMesh);
            }
            else AssetDatabase.CreateAsset(outlineMesh, OutlineMeshPath);
            var outlineMaterial = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (!outlineMaterial)
            {
                outlineMaterial = new Material(shader) { name = "Volcano1_RockOutline" };
                AssetDatabase.CreateAsset(outlineMaterial, OutlineMaterialPath);
            }
            outlineMaterial.shader = shader;
            outlineMaterial.SetColor("_InkColor", new Color(0.055f, 0.065f, 0.085f));
            outlineMaterial.SetFloat("_Width", 0.018f);
            EditorUtility.SetDirty(outlineMaterial);
            var child = rock.transform.Find("RockOutline");
            if (!child)
            {
                var gameObject = new GameObject("RockOutline");
                Undo.RegisterCreatedObjectUndo(gameObject, "Create volcano outline");
                child = gameObject.transform;
                child.SetParent(rock.transform, false);
            }
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            var filter = child.GetComponent<MeshFilter>();
            if (!filter) filter = child.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = outlineMesh;
            var renderer = child.GetComponent<MeshRenderer>();
            if (!renderer) renderer = child.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = outlineMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(renderer);
        }

        static Vector3Int Rounded(Vector3 v) => new Vector3Int(
            Mathf.RoundToInt(v.x * 100000), Mathf.RoundToInt(v.y * 100000), Mathf.RoundToInt(v.z * 100000));

        // Exact closest distance to the original, non-convex Lava triangles. The BVH is
        // constructed and queried only by this editor bake; no collider or query remains.
        sealed class TriangleTree
        {
            struct Triangle
            {
                public Vector3 a, b, c, min, max, center;
            }
            struct Node
            {
                public Vector3 min, max;
                public int start, count, left, right;
            }
            readonly Triangle[] triangles;
            readonly int[] order;
            readonly List<Node> nodes = new List<Node>();

            public TriangleTree(MeshFilter filter)
            {
                Mesh mesh = filter.sharedMesh;
                var vertices = mesh.vertices;
                var indices = mesh.triangles;
                var matrix = filter.transform.localToWorldMatrix;
                triangles = new Triangle[indices.Length / 3];
                order = new int[triangles.Length];
                for (int i = 0; i < triangles.Length; i++)
                {
                    Vector3 a = matrix.MultiplyPoint3x4(vertices[indices[i * 3]]);
                    Vector3 b = matrix.MultiplyPoint3x4(vertices[indices[i * 3 + 1]]);
                    Vector3 c = matrix.MultiplyPoint3x4(vertices[indices[i * 3 + 2]]);
                    triangles[i] = new Triangle
                    {
                        a = a, b = b, c = c,
                        min = Vector3.Min(a, Vector3.Min(b, c)),
                        max = Vector3.Max(a, Vector3.Max(b, c)),
                        center = (a + b + c) / 3
                    };
                    order[i] = i;
                }
                Build(0, order.Length);
            }

            int Build(int start, int count)
            {
                Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                Vector3 cmin = min, cmax = max;
                for (int i = start; i < start + count; i++)
                {
                    var t = triangles[order[i]];
                    min = Vector3.Min(min, t.min); max = Vector3.Max(max, t.max);
                    cmin = Vector3.Min(cmin, t.center); cmax = Vector3.Max(cmax, t.center);
                }
                int index = nodes.Count;
                nodes.Add(new Node { min = min, max = max, start = start, count = count, left = -1, right = -1 });
                if (count <= 8) return index;
                Vector3 extent = cmax - cmin;
                int axis = extent.x >= extent.y && extent.x >= extent.z ? 0 : extent.y >= extent.z ? 1 : 2;
                Array.Sort(order, start, count, Comparer<int>.Create((a, b) => triangles[a].center[axis].CompareTo(triangles[b].center[axis])));
                int half = count / 2;
                int left = Build(start, half), right = Build(start + half, count - half);
                var node = nodes[index]; node.left = left; node.right = right; nodes[index] = node;
                return index;
            }

            public float DistanceSquared(Vector3 p)
            {
                float best = float.PositiveInfinity;
                Search(0, p, ref best);
                return best;
            }

            void Search(int index, Vector3 p, ref float best)
            {
                Node n = nodes[index];
                if (BoxDistanceSquared(p, n.min, n.max) >= best) return;
                if (n.left < 0)
                {
                    for (int i = n.start; i < n.start + n.count; i++)
                    {
                        var t = triangles[order[i]];
                        if (BoxDistanceSquared(p, t.min, t.max) >= best) continue;
                        best = Mathf.Min(best, TriangleDistanceSquared(p, t.a, t.b, t.c));
                    }
                    return;
                }
                Node left = nodes[n.left], right = nodes[n.right];
                float dl = BoxDistanceSquared(p, left.min, left.max);
                float dr = BoxDistanceSquared(p, right.min, right.max);
                if (dl < dr) { Search(n.left, p, ref best); Search(n.right, p, ref best); }
                else { Search(n.right, p, ref best); Search(n.left, p, ref best); }
            }

            static float BoxDistanceSquared(Vector3 p, Vector3 min, Vector3 max)
            {
                float x = Mathf.Max(0f, Mathf.Max(min.x - p.x, p.x - max.x));
                float y = Mathf.Max(0f, Mathf.Max(min.y - p.y, p.y - max.y));
                float z = Mathf.Max(0f, Mathf.Max(min.z - p.z, p.z - max.z));
                return x * x + y * y + z * z;
            }

            static float TriangleDistanceSquared(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 ab = b - a, ac = c - a, ap = p - a;
                float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
                if (d1 <= 0 && d2 <= 0) return ap.sqrMagnitude;
                Vector3 bp = p - b;
                float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
                if (d3 >= 0 && d4 <= d3) return bp.sqrMagnitude;
                float vc = d1 * d4 - d3 * d2;
                if (vc <= 0 && d1 >= 0 && d3 <= 0)
                    return (p - (a + ab * (d1 / (d1 - d3)))).sqrMagnitude;
                Vector3 cp = p - c;
                float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
                if (d6 >= 0 && d5 <= d6) return cp.sqrMagnitude;
                float vb = d5 * d2 - d1 * d6;
                if (vb <= 0 && d2 >= 0 && d6 <= 0)
                    return (p - (a + ac * (d2 / (d2 - d6)))).sqrMagnitude;
                float va = d3 * d6 - d5 * d4;
                if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
                    return (p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).sqrMagnitude;
                Vector3 normal = Vector3.Cross(ab, ac);
                float magnitude = normal.sqrMagnitude;
                return magnitude > 1e-12f ? Mathf.Pow(Vector3.Dot(ap, normal), 2) / magnitude
                    : Mathf.Min(ap.sqrMagnitude, Mathf.Min(bp.sqrMagnitude, cp.sqrMagnitude));
            }
        }
        static float Smooth(float a, float b, float value)
        {
            float t = Mathf.Clamp01((value - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        static void ConfigureRockMaterial(Material material, Shader shader)
        {
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_CliffColor", new Color(0.13f, 0.155f, 0.195f));
            material.SetColor("_TerraceColor", new Color(0.405f, 0.395f, 0.405f));
            material.SetColor("_SummitColor", new Color(0.43f, 0.21f, 0.175f));
            material.SetColor("_HeatColor", new Color(0.86f, 0.185f, 0.065f));
            material.SetFloat("_SummitStrength", 0.72f);
            material.SetFloat("_HeatStrength", 0.78f);
            material.SetFloat("_BrushStrength", 0.17f);
            material.SetColor("_ShadowColor", new Color(0.69f, 0.71f, 0.74f));
            material.SetColor("_DeepShadowColor", new Color(0.5f, 0.54f, 0.59f));
            material.SetColor("_HighlightColor", new Color(0.72f, 0.72f, 0.72f));
            material.SetColor("_InkColor", new Color(0.055f, 0.065f, 0.085f));
            material.SetFloat("_ShadowThreshold", 0.48f);
            material.SetFloat("_DeepShadowThreshold", 0.17f);
            material.SetFloat("_ShadowSoftness", 0.018f);
            material.SetFloat("_BrightThreshold", 0.81f);
            material.SetFloat("_PaperLift", 0.025f);
            material.SetFloat("_SurfaceTintStrength", 0.28f);
            material.SetFloat("_UseHatching", 1);
            material.EnableKeyword("_USE_HATCHING");
            material.SetFloat("_UseTriplanar", 1);
            material.EnableKeyword("_USE_TRIPLANAR");
            material.SetFloat("_HatchScale", 4.8f);
            material.SetFloat("_HatchStrength", 0.38f);
            material.SetFloat("_HatchContrast", 1);
            material.SetFloat("_HatchThreshold", 0.5f);
            material.SetFloat("_HatchRotation", -25);
            material.SetFloat("_HatchLineWidth", 1);
            material.SetFloat("_HatchIrregularity", 0.65f);
            material.SetFloat("_HatchCrossStrength", 0.22f);
            material.SetFloat("_HatchThirdStrength", 0.12f);
            material.SetFloat("_CastShadowHatchStrength", 0.6f);
            material.SetFloat("_CastShadowTintStrength", 0.2f);
            material.SetFloat("_HighlightStrength", 0);
            material.SetFloat("_UseStipple", 0);
            material.SetFloat("_UseVertexColor", 0);
            material.SetFloat("_UseRamp", 0);
            material.SetFloat("_DebugMode", 0);
        }
    }
}
