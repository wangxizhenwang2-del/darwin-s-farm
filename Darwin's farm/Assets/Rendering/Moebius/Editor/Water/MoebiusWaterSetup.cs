using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Darwin.Rendering.Editor
{
    /// <summary>Editor-only mesh creation and static world-space shoreline baking. No runtime component.</summary>
    public sealed class MoebiusWaterSetup : EditorWindow
    {
        [SerializeField] MeshRenderer water;
        [SerializeField] float width = 18, depth = 14, distanceRange = 4;
        [SerializeField] int subdivisions = 144, resolution = 512;
        double lastGameRender, frameDurationSum, longestFrame, lastMonitorRepaint;
        int previewFrames, monitoredCamera;

        [MenuItem("Tools/Moebius/Water Setup")]
        public static void Open() { GetWindow<MoebiusWaterSetup>("Moebius Water"); }
        void OnEnable()
        {
            RenderPipelineManager.endCameraRendering += RecordFrame;
            OnSelectionChange();
        }
        void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= RecordFrame;
        }
        void OnSelectionChange()
        {
            var selected = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<MeshRenderer>() : null;
            if (selected && selected.sharedMaterial && selected.sharedMaterial.shader.name == "Darwin/Moebius Water") water = selected;
            Repaint();
        }
        void RecordFrame(ScriptableRenderContext context, Camera camera)
        {
            if (!water || camera.cameraType != CameraType.Game || camera.gameObject.scene != water.gameObject.scene) return;
            if (monitoredCamera == 0) monitoredCamera = camera.GetInstanceID();
            if (monitoredCamera != camera.GetInstanceID()) return;
            double now = EditorApplication.timeSinceStartup;
            if (lastGameRender > 0)
            {
                double duration = now - lastGameRender;
                frameDurationSum += duration; longestFrame = Math.Max(longestFrame, duration); previewFrames++;
            }
            lastGameRender = now;
            if (now - lastMonitorRepaint > 1) { lastMonitorRepaint = now; Repaint(); }
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Subdivided Water Surface", EditorStyles.boldLabel);
            width = Mathf.Max(0.5f, EditorGUILayout.FloatField("Width (Metres)", width));
            depth = Mathf.Max(0.5f, EditorGUILayout.FloatField("Depth (Metres)", depth));
            subdivisions = EditorGUILayout.IntSlider("Subdivisions Per Axis", subdivisions, 8, 256);
            if (GUILayout.Button("Create Water Surface"))
            {
                string path = EditorUtility.SaveFilePanelInProject("Save Water Mesh", "WaterGrid", "asset", "Choose an asset folder.");
                if (!string.IsNullOrEmpty(path))
                {
                    var mesh = CreateGrid(width, depth, subdivisions);
                    AssetDatabase.CreateAsset(mesh, path);
                    var go = new GameObject("Moebius Water");
                    Undo.RegisterCreatedObjectUndo(go, "Create water surface");
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    water = go.AddComponent<MeshRenderer>();
                    var defaultMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Content/Defaults/Materials/MoebiusWater.mat");
                    var material = defaultMaterial ? new Material(defaultMaterial) : new Material(Shader.Find("Darwin/Moebius Water"));
                    string materialPath = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(path, ".mat"));
                    AssetDatabase.CreateAsset(material, materialPath);
                    water.sharedMaterial = material;
                    water.shadowCastingMode = ShadowCastingMode.Off;
                    water.receiveShadows = true;
                    Selection.activeGameObject = go;
                }
            }
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Automatic Shoreline", EditorStyles.boldLabel);
            water = (MeshRenderer)EditorGUILayout.ObjectField("Water Renderer", water, typeof(MeshRenderer), true);
            resolution = EditorGUILayout.IntPopup("Mask Resolution", resolution, new[] { "128", "256", "512", "1024" }, new[] { 128, 256, 512, 1024 });
            distanceRange = Mathf.Max(0.1f, EditorGUILayout.FloatField("Distance Range (Metres)", distanceRange));
            EditorGUILayout.HelpBox("Select a horizontal water surface. Foam follows actual ground and rocks found automatically in the same scene, including Terrain. Open water-mesh edges do not generate foam. Other water, billboard characters, transparent objects and animated/physics bodies are excluded. No Shore Objects list, UVs or colliders are needed. Update again after moving water or changing the ground.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!water || !water.sharedMaterial || EditorUtility.IsPersistent(water)))
            if (GUILayout.Button("Update Shoreline"))
            {
                    try
                    {
                        if (water.sharedMaterial.shader.name != "Darwin/Moebius Water") throw new InvalidOperationException("The water material must use Darwin/Moebius Water.");
                        const string folder = "Assets/Rendering/Moebius/Content/Water";
                        Directory.CreateDirectory(folder); AssetDatabase.Refresh();
                        string assetName = water.name;
                        foreach (char c in Path.GetInvalidFileNameChars()) assetName = assetName.Replace(c, '_');
                        string oldMaterialPath = AssetDatabase.GetAssetPath(water.sharedMaterial);
                        string oldMaskPath = AssetDatabase.GetAssetPath(water.sharedMaterial.GetTexture("_ShorelineMask"));
                        bool reuse = oldMaterialPath.StartsWith(folder + "/", StringComparison.Ordinal) && oldMaskPath.StartsWith(folder + "/", StringComparison.Ordinal) && UniqueMaterial(water.sharedMaterial);
                        string path = reuse ? oldMaskPath : AssetDatabase.GenerateUniqueAssetPath(folder + "/" + assetName + "_Shore.png");
                        string materialPath = reuse ? oldMaterialPath : AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(path, ".mat"));
                        var mask = BakeAutomatically(water, resolution, distanceRange, path, out var mappingBounds);
                        var material = reuse ? water.sharedMaterial : new Material(water.sharedMaterial);
                        if (reuse) Undo.RecordObject(material, "Update shoreline mapping");
                        else AssetDatabase.CreateAsset(material, materialPath);
                        AssignShoreline(material, mask, mappingBounds, distanceRange);
                        Undo.RecordObject(water, "Assign baked shoreline");
                        water.sharedMaterial = material;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(water);
                        EditorUtility.SetDirty(water);
                        AssetDatabase.SaveAssets();
                        ShowNotification(new GUIContent("Shoreline updated and assigned"));
                    }
                    catch (Exception exception) { EditorUtility.DisplayDialog("Shoreline Bake", exception.Message, "OK"); }
            }
            EditorGUILayout.Space(8);
            bool animate = EditorGUILayout.Toggle("Animate Edit Mode Preview", MoebiusWaterPreview.Enabled);
            if (animate != MoebiusWaterPreview.Enabled) MoebiusWaterPreview.Enabled = animate;
            EditorGUILayout.HelpBox("Loaded water scenes animate in Edit Mode even when this window is closed. Play Mode uses the game's own frame rate.", MessageType.Info);
            if (previewFrames > 0)
                EditorGUILayout.LabelField("Game camera cadence", (previewFrames / frameDurationSum).ToString("F1") + " FPS; longest gap " + (longestFrame * 1000).ToString("F1") + " ms");
            if (GUILayout.Button("Reset Frame Monitor")) { previewFrames = monitoredCamera = 0; frameDurationSum = longestFrame = lastGameRender = 0; }
        }

        static bool UniqueMaterial(Material material)
        {
            int users = 0;
            foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (EditorUtility.IsPersistent(renderer) || !renderer.gameObject.scene.IsValid()) continue;
                foreach (var assigned in renderer.sharedMaterials) if (assigned == material && ++users > 1) return false;
            }
            return users == 1;
        }

        public static Mesh CreateGrid(float width, float depth, int divisions)
        {
            divisions = Mathf.Clamp(divisions, 2, 256);
            int row = divisions + 1;
            var vertices = new Vector3[row * row];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[divisions * divisions * 6];
            for (int z = 0; z < row; z++) for (int x = 0; x < row; x++)
            {
                int i = z * row + x;
                uv[i] = new Vector2((float)x / divisions, (float)z / divisions);
                vertices[i] = new Vector3((uv[i].x - 0.5f) * width, 0, (uv[i].y - 0.5f) * depth);
                normals[i] = Vector3.up;
            }
            int index = 0;
            for (int z = 0; z < divisions; z++) for (int x = 0; x < divisions; x++)
            {
                int a = z * row + x;
                triangles[index++] = a; triangles[index++] = a + row; triangles[index++] = a + 1;
                triangles[index++] = a + 1; triangles[index++] = a + row; triangles[index++] = a + row + 1;
            }
            var mesh = new Mesh { name = "Moebius Water Grid", indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = triangles;
            // Conservative persistent bounds cover the shader's supported +/-0.5m waves at unit Y scale.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(width, 1.2f, depth));
            return mesh;
        }

        public static void AssignShoreline(Material material, Texture2D mask, Bounds bounds, float range)
        {
            material.SetTexture("_ShorelineMask", mask);
            material.SetVector("_ShoreWorldRect", new Vector4(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z));
            material.SetFloat("_ShoreDistanceRange", range);
            material.SetFloat("_EnableShoreMask", 1);
            EditorUtility.SetDirty(material);
        }

        public static Texture2D Bake(IEnumerable<MeshFilter> sources, Bounds bounds, float waterHeight, int size, float range, string path)
            => BakeCore(sources, Array.Empty<Terrain>(), bounds, waterHeight, size, range, path, null);

        public static Texture2D BakeAutomatically(MeshRenderer surface, int size, float range, string path, out Bounds mappingBounds)
        {
            if (!surface || !surface.gameObject.scene.IsValid() || !surface.gameObject.scene.isLoaded)
                throw new InvalidOperationException("Select a water surface in a loaded scene.");
            var filter = surface.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) throw new InvalidOperationException("The water region needs a MeshFilter.");
            size = Mathf.Clamp(size, 64, 1024);
            float height = GetWaterLevel(filter);
            mappingBounds = surface.bounds;
            // Include nearby real shores outside the mesh; an open mesh edge is not land.
            float padding = Mathf.Max(Mathf.Max(mappingBounds.size.x, mappingBounds.size.z) * 2 / (size - 4), range);
            mappingBounds.Expand(new Vector3(padding * 2, 0, padding * 2));
            var meshes = CollectShoreMeshes(surface, mappingBounds, height);
            var terrains = new List<Terrain>();
            foreach (var root in surface.gameObject.scene.GetRootGameObjects())
                foreach (var terrain in root.GetComponentsInChildren<Terrain>())
                    if (terrain.enabled && terrain.drawHeightmap && terrain.terrainData && terrain.gameObject.activeInHierarchy)
                        terrains.Add(terrain);
            return BakeCore(meshes, terrains, mappingBounds, height, size, range, path, null);
        }

        public static List<MeshFilter> CollectShoreMeshes(MeshRenderer waterSurface, Bounds bounds, float height)
        {
            var result = new List<MeshFilter>();
            foreach (var root in waterSurface.gameObject.scene.GetRootGameObjects())
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (!filter.sharedMesh || !renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy || renderer == waterSurface) continue;
                    if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly || filter.GetComponentInParent<Animator>() || filter.GetComponentInParent<Rigidbody>()) continue;
                    var b = renderer.bounds;
                    if (b.max.y < height || b.max.x < bounds.min.x || b.min.x > bounds.max.x || b.max.z < bounds.min.z || b.min.z > bounds.max.z) continue;
                    bool opaque = false, excluded = false;
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (!material || !material.shader) continue;
                        string shader = material.shader.name;
                        if (shader == "Darwin/Moebius Water" || shader.StartsWith("Darwin/Moebius Billboard", StringComparison.Ordinal)) excluded = true;
                        if (material.renderQueue <= 2500 && material.GetTag("RenderType", false, "Opaque") == "Opaque") opaque = true;
                    }
                    if (opaque && !excluded) result.Add(filter);
                }
            return result;
        }

        static float GetWaterLevel(MeshFilter filter)
        {
            using (var dataArray = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
            using (var vertices = new Unity.Collections.NativeArray<Vector3>(dataArray[0].vertexCount, Unity.Collections.Allocator.Temp))
            {
                if (vertices.Length == 0) throw new InvalidOperationException("Water mesh has no vertices.");
                dataArray[0].GetVertices(vertices);
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                foreach (var vertex in vertices)
                {
                    float y = filter.transform.TransformPoint(vertex).y;
                    min = Mathf.Min(min, y); max = Mathf.Max(max, y);
                }
                if (max - min > 0.01f) throw new InvalidOperationException("Use a flat horizontal water region.");
                return (min + max) * 0.5f;
            }
        }

        static Texture2D BakeCore(IEnumerable<MeshFilter> sources, IEnumerable<Terrain> terrains, Bounds bounds, float waterHeight, int size, float range, string path, bool[] initialLand)
        {
            size = Mathf.Clamp(size, 64, 1024);
            range = Mathf.Max(range, 0.1f);
            if (bounds.size.x <= 0 || bounds.size.z <= 0) throw new InvalidOperationException("Water bounds must have an XZ area.");
            var land = initialLand ?? new bool[size * size];
            var visited = new HashSet<MeshFilter>();
            foreach (var source in sources)
            {
                if (!source || !source.sharedMesh || !visited.Add(source)) continue;
                var renderer = source.GetComponent<Renderer>();
                if (renderer && Array.Exists(renderer.sharedMaterials, m => m && m.shader.name == "Darwin/Moebius Water")) continue;
                RasterMesh(source, bounds, waterHeight, size, land);
            }
            foreach (var terrain in terrains) RasterTerrain(terrain, bounds, waterHeight, size, land);
            int count = 0; foreach (bool value in land) if (value) count++;
            if (count == land.Length) throw new InvalidOperationException("The water region must contain open water. Check that the water is above the submerged ground and intersects the shore.");
            float dx = bounds.size.x / size, dz = bounds.size.z / size;
            float[] distance;
            if (count == 0) { distance = new float[land.Length]; for (int i = 0; i < distance.Length; i++) distance[i] = range; }
            else distance = DistanceField(land, size, dx, dz);
            var pixels = new Color[size * size];
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                int i = z * size + x;
                float gx = (distance[z * size + Mathf.Min(x + 1, size - 1)] - distance[z * size + Mathf.Max(x - 1, 0)]) / (2 * dx);
                float gz = (distance[Mathf.Min(z + 1, size - 1) * size + x] - distance[Mathf.Max(z - 1, 0) * size + x]) / (2 * dz);
                pixels[i] = new Color(Mathf.Clamp01(distance[i] / range), Mathf.Clamp(gx, -1, 1) * 0.5f + 0.5f, Mathf.Clamp(gz, -1, 1) * 0.5f + 0.5f, 1);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.SetPixels(pixels); texture.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false; importer.mipmapEnabled = false; importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = size; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void RasterMesh(MeshFilter source, Bounds bounds, float level, int size, bool[] land)
        {
            // Editor read-only data also supports imported meshes with runtime Read/Write disabled.
            using (var dataArray = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(source.sharedMesh))
            {
                var data = dataArray[0];
                using (var vertexStorage = new Unity.Collections.NativeArray<Vector3>(data.vertexCount, Unity.Collections.Allocator.Temp))
                {
                    var vertices = vertexStorage; // Writable handle to the same storage; the owner disposes it once.
                    data.GetVertices(vertices);
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = source.transform.TransformPoint(vertices[i]);
                    for (int sub = 0; sub < data.subMeshCount; sub++)
                    {
                        var descriptor = data.GetSubMesh(sub);
                        if (descriptor.topology != MeshTopology.Triangles) continue;
                        using (var indices = new Unity.Collections.NativeArray<int>(descriptor.indexCount, Unity.Collections.Allocator.Temp))
                        {
                            data.GetIndices(indices, sub, true);
                            for (int i = 0; i < indices.Length; i += 3)
                                RasterTriangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]], bounds, level, size, land);
                        }
                    }
                }
            }
        }

        static void RasterTerrain(Terrain terrain, Bounds bounds, float level, int size, bool[] land)
        {
            var data = terrain.terrainData; var origin = terrain.transform.position; var extent = data.size;
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                var point = new Vector3(bounds.min.x + (x + 0.5f) * bounds.size.x / size, 0, bounds.min.z + (z + 0.5f) * bounds.size.z / size);
                float u = (point.x - origin.x) / extent.x, v = (point.z - origin.z) / extent.z;
                if (u < 0 || u > 1 || v < 0 || v > 1) continue;
                int holes = data.holesResolution;
                if (holes > 0 && data.IsHole(Mathf.Min((int)(u * holes), holes - 1), Mathf.Min((int)(v * holes), holes - 1))) continue;
                if (terrain.SampleHeight(point) + origin.y >= level) land[z * size + x] = true;
            }
        }

        static void RasterTriangle(Vector3 a, Vector3 b, Vector3 c, Bounds bounds, float level, int size, bool[] land)
        {
            if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < level) return;
            Vector2 pa = new Vector2(a.x, a.z), ab = new Vector2(b.x - a.x, b.z - a.z), ac = new Vector2(c.x - a.x, c.z - a.z);
            float determinant = ab.x * ac.y - ab.y * ac.x;
            if (Mathf.Abs(determinant) < 1e-8f) return;
            float dx = bounds.size.x / size, dz = bounds.size.z / size;
            int minX = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - bounds.min.x) / dx), 0, size - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - bounds.min.x) / dx), 0, size - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - bounds.min.z) / dz), 0, size - 1);
            int maxZ = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - bounds.min.z) / dz), 0, size - 1);
            for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(bounds.min.x + (x + 0.5f) * dx, bounds.min.z + (z + 0.5f) * dz) - pa;
                float u = (p.x * ac.y - p.y * ac.x) / determinant;
                float v = (ab.x * p.y - ab.y * p.x) / determinant;
                if (u >= -0.0001f && v >= -0.0001f && u + v <= 1.0001f && a.y + u * (b.y - a.y) + v * (c.y - a.y) >= level)
                    land[z * size + x] = true;
            }
        }

        // Exact separable Euclidean distance transform, O(pixel count), supports rectangular world pixels.
        public static float[] DistanceField(bool[] land, int size, float dx, float dz)
        {
            var intermediate = new float[land.Length]; var result = new float[land.Length];
            var f = new float[size]; var d = new float[size];
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++) f[x] = land[z * size + x] ? 0 : 1e12f;
                TransformDistance(f, d, dx);
                for (int x = 0; x < size; x++) intermediate[z * size + x] = d[x];
            }
            for (int x = 0; x < size; x++)
            {
                for (int z = 0; z < size; z++) f[z] = intermediate[z * size + x];
                TransformDistance(f, d, dz);
                for (int z = 0; z < size; z++) result[z * size + x] = Mathf.Max(0, Mathf.Sqrt(d[z]) - Mathf.Min(dx, dz) * 0.5f);
            }
            return result;
        }
        static void TransformDistance(float[] f, float[] result, float spacing)
        {
            int n = f.Length, k = 0;
            var sites = new int[n]; var borders = new double[n + 1];
            borders[0] = double.NegativeInfinity; borders[1] = double.PositiveInfinity;
            double scale = spacing * spacing;
            for (int q = 1; q < n; q++)
            {
                double s;
                do
                {
                    int p = sites[k];
                    s = ((double)f[q] - f[p] + scale * (q * q - p * p)) / (2 * scale * (q - p));
                    if (s <= borders[k]) k--; else break;
                } while (k >= 0);
                k++; sites[k] = q; borders[k] = s; borders[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (borders[k + 1] < q) k++;
                int offset = q - sites[k];
                result[q] = (float)(scale * offset * offset + f[sites[k]]);
            }
        }
    }
}
