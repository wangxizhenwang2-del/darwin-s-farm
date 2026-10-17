using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Darwin.Rendering.Validation
{
    public static class MoebiusHuisanRiverSetup
    {
        const string B = "Assets/Rendering/Moebius/Samples/Biomes/";
        const string Output = "Validation/HuisanRivers";
        static readonly List<string> Notes = new List<string>();
        static void Guard()
        {
            if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/Temp/") || !File.Exists("HuisanRivers.marker"))
                throw new InvalidOperationException("Use the marked isolated fixture.");
            Directory.CreateDirectory(Output);
        }
        static Vector3[] Vertices(Mesh mesh)
        {
            using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var positions = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
            { data[0].GetVertices(positions); return positions.ToArray(); }
        }
        static int[] Indices(Mesh mesh)
        {
            var result = new List<int>();
            using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
                for (int slot = 0; slot < data[0].subMeshCount; slot++)
                {
                    var sub = data[0].GetSubMesh(slot); if (sub.topology != MeshTopology.Triangles) continue;
                    using (var indices = new NativeArray<int>(sub.indexCount, Allocator.Temp))
                    { data[0].GetIndices(indices, slot, true); result.AddRange(indices.ToArray()); }
                }
            return result.ToArray();
        }
        static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            var bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds); return bounds;
        }
        static void Save(Object value, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing) { EditorUtility.CopySerialized(value, existing); Object.DestroyImmediate(value); EditorUtility.SetDirty(existing); }
            else AssetDatabase.CreateAsset(value, path);
        }
        static void Check(bool ok, string label)
        { if (!ok) throw new Exception(label); Notes.Add("PASS: " + label); }
        static void Subdivide(Vector3 a, Vector3 b, Vector3 c, int depth, List<Vector3> v, List<int> t)
        {
            if (depth == 0) { int i = v.Count; v.AddRange(new[] { a, b, c }); t.AddRange(new[] { i, i + 1, i + 2 }); return; }
            var ab = (a + b) * .5f; var bc = (b + c) * .5f; var ca = (c + a) * .5f;
            Subdivide(a, ab, ca, depth - 1, v, t); Subdivide(ab, b, bc, depth - 1, v, t);
            Subdivide(ca, bc, c, depth - 1, v, t); Subdivide(ab, bc, ca, depth - 1, v, t);
        }
        static void BuildTile(string name)
        {
            string path = B + "Prefabs/" + name + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var old = root.transform.Find("Flowing Water"); if (old) Object.DestroyImmediate(old.gameObject);
                var bank = root.GetComponent<MeshRenderer>(); var filter = root.GetComponent<MeshFilter>();
                root.transform.rotation = Quaternion.identity;
                root.transform.localScale *= 17 / Mathf.Max(bank.bounds.size.x, bank.bounds.size.z);
                var bounds = bank.bounds;
                root.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                bounds = bank.bounds;
                Check(Mathf.Abs(bounds.size.x - 17) < .03f && Mathf.Abs(bounds.size.z - 17) < .03f, name + ": normalized 17 metre footprint");
                var vertices = Vertices(filter.sharedMesh).Select(filter.transform.TransformPoint).ToArray(); var indices = Indices(filter.sharedMesh);
                var horizontal = new List<(float y, int index)>();
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var a = vertices[indices[i]]; var b = vertices[indices[i + 1]]; var c = vertices[indices[i + 2]];
                    if (Mathf.Abs(a.y - b.y) < .002f && Mathf.Abs(a.y - c.y) < .002f && Vector3.Cross(b - a, c - a).y > .00001f)
                        horizontal.Add(((a.y + b.y + c.y) / 3, i));
                }
                var levels = new List<float>();
                foreach (float y in horizontal.Select(p => p.y).OrderBy(y => y))
                    if (levels.Count == 0 || y - levels.Last() > .01f) levels.Add(y);
                Check(levels.Count == 3, name + ": three upward surface levels identified: " + string.Join(", ", levels.Select(y => y.ToString("F3"))));
                // L_New's channel is BELOW its perimeter ledge; Cross_River's is ABOVE it.
                // The diagnostic confirms these are respectively the L and cross-shaped polygons.
                float bed = name == "L_New" ? levels[0] : levels[1];
                float ledge = name == "L_New" ? levels[1] : levels[0];
                var floor = horizontal.Where(p => Mathf.Abs(p.y - bed) < .01f).Select(p => p.index).ToArray();
                var origin = new Vector3(bounds.center.x, Mathf.Max(bed + .12f, ledge + .1f), bounds.center.z);
                var points = new List<Vector3>(); var triangles = new List<int>();
                Func<Vector3, Vector3> point = p =>
                {
                    var q = p - origin; q.y = 0;
                    // Open connectors stop at the inset perimeter ledge in the authored bed.
                    // Continue just these terminal vertices to the outside of the 17m tile.
                    if (Mathf.Abs(Mathf.Abs(q.x) - 7.5f) < .025f) q.x = Mathf.Sign(q.x) * 8.5f;
                    if (Mathf.Abs(Mathf.Abs(q.z) - 7.5f) < .025f) q.z = Mathf.Sign(q.z) * 8.5f;
                    return q;
                };
                foreach (int i in floor) Subdivide(point(vertices[indices[i]]), point(vertices[indices[i + 1]]), point(vertices[indices[i + 2]]), 3, points, triangles);
                var mesh = new Mesh { name = name + "_Water", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(points); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var meshBounds = mesh.bounds; meshBounds.Expand(new Vector3(0, 1.2f, 0)); mesh.bounds = meshBounds;
                string meshPath = B + "Meshes/River/" + name + "_Water.asset"; Save(mesh, meshPath);
                var water = new GameObject("Flowing Water"); water.transform.SetParent(root.transform, false);
                water.transform.SetPositionAndRotation(origin, Quaternion.identity);
                var scale = root.transform.lossyScale; water.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
                water.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                var renderer = water.AddComponent<MeshRenderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                var material = new Material(AssetDatabase.LoadAssetAtPath<Material>(B + "Materials/River/River_I_Water.mat")) { name = name + "_Water" };
                material.SetFloat("_DebugView", 0); material.SetFloat("_EnableWaterfallImpact", 0); material.SetFloat("_AnimationPhase", name == "L_New" ? 0 : .8f);
                string materialPath = B + "Materials/River/" + name + "_Water.mat"; Save(material, materialPath);
                material = AssetDatabase.LoadAssetAtPath<Material>(materialPath); renderer.sharedMaterial = material;
                var mappingBounds = bounds; mappingBounds.Expand(new Vector3(6, 0, 6));
                string maskPath = B + "Textures/River/" + name + "_ShoreDistance.png";
                var mask = Darwin.Rendering.Editor.MoebiusWaterSetup.Bake(new[] { filter }, mappingBounds, origin.y, 512, 3, maskPath);
                mappingBounds.center -= origin;
                Darwin.Rendering.Editor.MoebiusWaterSetup.AssignShoreline(material, mask, mappingBounds, 3);
                material.SetFloat("_ShoreObjectSpace", 1); EditorUtility.SetDirty(material);
                Check(material.GetFloat("_EnableFoam") > .5f && material.GetFloat("_ReceiveWaterShadows") > .5f, name + ": existing animated water / foam / shadow settings retained");
                Check(points.Count > 0 && material.GetTexture("_ShorelineMask"), name + ": channel water mesh and independent shoreline mask created");
                Check(origin.y - bed > material.GetFloat("_WaveHeight") * 2, name + ": waves cannot intersect the riverbed");
                Notes.Add(name + ": bed=" + bed + ", water level=" + origin.y + ", water triangles=" + triangles.Count / 3 + ", water bounds=" + renderer.bounds.ToString("F3"));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static Camera CameraFor(Bounds bounds)
        {
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 250;
            camera.GetUniversalAdditionalCameraData().SetRenderer(1);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.45f, .75f, .79f);
            camera.transform.position = bounds.center + new Vector3(1, 1.35f, -1.35f) * 32; camera.transform.LookAt(bounds.center);
            FitCamera(camera, bounds); return camera;
        }
        static void FitCamera(Camera camera, Bounds bounds)
        {
            float halfHeight = 0;
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            {
                var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                var local = camera.transform.InverseTransformPoint(point);
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(local.y), Mathf.Abs(local.x) / (1500f / 1050));
            }
            camera.orthographicSize = halfHeight * 1.09f;
        }
        static Color32[] Capture(Camera camera, string name)
        {
            var previous = RenderTexture.active; var target = camera.targetTexture;
            var rt = RenderTexture.GetTemporary(1500, 1050, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var texture = new Texture2D(1500, 1050, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1500, 1050), 0, 0); texture.Apply(); File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());
            var pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture); camera.targetTexture = target; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt);
            return pixels;
        }
        static void VerifyVisibility(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(); var originals = renderers.Select(r => r.sharedMaterials).ToArray();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var black = new Material(shader); black.SetColor("_BaseColor", Color.black);
            var magenta = new Material(shader); magenta.SetColor("_BaseColor", Color.magenta);
            Camera camera = null;
            try
            {
                foreach (var r in renderers) r.sharedMaterials = Enumerable.Repeat(r.name == "Flowing Water" ? magenta : black, r.sharedMaterials.Length).ToArray();
                var bank = root.GetComponent<MeshRenderer>().bounds; camera = CameraFor(bank);
                camera.GetUniversalAdditionalCameraData().SetRenderer(0); camera.backgroundColor = Color.black;
                camera.transform.SetPositionAndRotation(bank.center + Vector3.up * 40, Quaternion.Euler(90, 0, 0)); camera.orthographicSize = 10;
                int pixels = Capture(camera, root.name + "_WaterVisibility").Count(p => p.r > 200 && p.b > 200 && p.g < 50);
                Check(pixels > 12000, root.name + ": water visibly above geometry in GPU depth test (" + pixels + " pixels)");
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterials = originals[i];
                if (camera) Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(black); Object.DestroyImmediate(magenta);
            }
        }
        public static void Build()
        {
            var errors = new List<string>();
            Application.logMessageReceived += (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
            try
            {
                Guard(); ShaderUtil.allowAsyncCompilation = false;
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                BuildTile("L_New"); BuildTile("Cross_River"); AssetDatabase.SaveAssets();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var roots = new List<GameObject>();
                foreach (string name in new[] { "L_New", "Cross_River" })
                {
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(B + "Prefabs/" + name + ".prefab"));
                    root.transform.position += new Vector3(name == "L_New" ? -10 : 10, 0, 0); roots.Add(root);
                }
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.72f, .76f, .8f); RenderSettings.fog = false;
                var light = new GameObject("Directional Light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1;
                light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(50, -30, 0); light.gameObject.AddComponent<UniversalAdditionalLightData>(); RenderSettings.sun = light;
                var bounds = BoundsOf(roots[0]); bounds.Encapsulate(BoundsOf(roots[1])); var camera = CameraFor(bounds);
                string scenePath = B + "Scenes/River_L_Cross_Moebius.unity";
                Check(EditorSceneManager.SaveScene(scene, scenePath), "Independent L / Cross demonstration scene saved");
                Capture(camera, "River_L_Cross");
                foreach (var root in roots)
                {
                    var b = BoundsOf(root); camera.transform.position = b.center + new Vector3(1, 1.35f, -1.35f) * 22; camera.transform.LookAt(b.center); FitCamera(camera, b);
                    foreach (var other in roots) if (other != root) other.SetActive(false);
                    var before = Capture(camera, root.name);
                    var waterMaterial = root.transform.Find("Flowing Water").GetComponent<MeshRenderer>().sharedMaterial;
                    float phase = waterMaterial.GetFloat("_AnimationPhase");
                    waterMaterial.SetFloat("_AnimationPhase", phase + 2.5f);
                    var after = Capture(camera, root.name + "_Later"); waterMaterial.SetFloat("_AnimationPhase", phase);
                    int changed = 0;
                    for (int i = 0; i < before.Length; i++) if (Math.Abs(before[i].r - after[i].r) + Math.Abs(before[i].g - after[i].g) + Math.Abs(before[i].b - after[i].b) > 10) changed++;
                    Check(changed > 200, root.name + ": water / foam animation changes rendered pixels (" + changed + ")");
                    VerifyVisibility(root);
                    foreach (var other in roots) other.SetActive(true);
                    Check(root.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), root.name + ": no missing scripts");
                    foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
                    {
                        Check(renderer.GetComponent<MeshFilter>().sharedMesh, root.name + ": mesh resolves on " + renderer.name);
                        foreach (var material in renderer.sharedMaterials)
                        {
                            if (!material || !material.shader || !material.shader.isSupported) throw new Exception("Unresolved material on " + renderer.name);
                            foreach (var diagnostic in ShaderUtil.GetShaderMessages(material.shader))
                                if (diagnostic.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors.Add(diagnostic.message);
                        }
                    }
                }
                EditorSceneManager.OpenScene(scenePath);
                Check(Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Count(r => r.name == "Flowing Water") == 2, "Scene reload retains both water surfaces");
                Check(errors.Count == 0, "No Unity / shader errors: " + string.Join("; ", errors));
                AssetDatabase.SaveAssets(); Notes.Add("Errors: 0"); File.WriteAllLines(Output + "/Report.txt", Notes); EditorApplication.Exit(0);
            }
            catch (Exception e) { Notes.Add(e.ToString()); Notes.AddRange(errors); File.WriteAllLines(Output + "/Report.txt", Notes); EditorApplication.Exit(1); }
        }
        public static void Probe()
        {
            try
            {
                Guard(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                foreach (string name in new[] { "L_New", "Cross_River" })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(B + "Prefabs/" + name + ".prefab");
                    if (!prefab) throw new Exception("Missing prefab " + name);
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    Notes.Add(name + " bounds: " + BoundsOf(root).ToString("F4") + " scale " + root.transform.localScale);
                    var bank = root.GetComponent<MeshFilter>();
                    if (!bank) throw new Exception("Missing root bank mesh " + name);
                    Notes.Add("Root mesh: " + bank.sharedMesh.name + " from " + AssetDatabase.GetAssetPath(bank.sharedMesh));
                    var v = Vertices(bank.sharedMesh).Select(bank.transform.TransformPoint).ToArray(); var t = Indices(bank.sharedMesh);
                    var levels = new SortedDictionary<float, float>();
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
                        var cross = Vector3.Cross(b - a, c - a);
                        if (Mathf.Abs(a.y - b.y) > .001f || Mathf.Abs(a.y - c.y) > .001f || cross.y <= 0) continue;
                        float y = Mathf.Round(a.y * 1000) / 1000;
                        levels.TryGetValue(y, out float area); levels[y] = area + cross.magnitude * .5f;
                    }
                    foreach (var entry in levels) Notes.Add("Upward plane y=" + entry.Key + " area=" + entry.Value);
                    foreach (var entry in levels)
                    {
                        var centroids = new List<Vector3>();
                        for (int i = 0; i < t.Length; i += 3)
                        {
                            var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
                            if (Mathf.Abs(a.y - b.y) < .001f && Mathf.Abs(a.y - c.y) < .001f && Mathf.Abs(a.y - entry.Key) < .001f && Vector3.Cross(b - a, c - a).y > 0)
                                centroids.Add((a + b + c) / 3);
                        }
                        Notes.Add("Plane " + entry.Key + " centroid samples: " + string.Join("; ", centroids.Take(25).Select(p => p.ToString("F3"))));
                    }
                    if (name == "L_New")
                    {
                        var clone = new Mesh { indexFormat = IndexFormat.UInt32 }; var localVertices = Vertices(bank.sharedMesh);
                        clone.vertices = localVertices;
                        var buckets = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
                        var heights = levels.Keys.ToArray();
                        for (int i = 0; i < t.Length; i += 3)
                        {
                            var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]]; int slot = 3;
                            if (Mathf.Abs(a.y - b.y) < .001f && Mathf.Abs(a.y - c.y) < .001f && Vector3.Cross(b - a, c - a).y > 0)
                                for (int j = 0; j < Mathf.Min(3, heights.Length); j++) if (Mathf.Abs(a.y - heights[j]) < .001f) { slot = j; break; }
                            buckets[slot].AddRange(new[] { t[i], t[i + 1], t[i + 2] });
                        }
                        clone.subMeshCount = 4; for (int i = 0; i < 4; i++) clone.SetTriangles(buckets[i], i);
                        bank.sharedMesh = clone;
                        var materials = new[] { Color.red, Color.blue, Color.green, Color.gray }.Select(c => { var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.color = c; return m; }).ToArray();
                        var renderer = bank.GetComponent<MeshRenderer>(); renderer.sharedMaterials = materials;
                        var childWater = root.transform.Find("Flowing Water"); if (childWater) childWater.gameObject.SetActive(false);
                        foreach (var r in root.GetComponentsInChildren<MeshRenderer>()) if (r != renderer) r.enabled = false;
                        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                        GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                        var camera = CameraFor(renderer.bounds); camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                        camera.transform.position = renderer.bounds.center + Vector3.up * 35; camera.transform.rotation = Quaternion.Euler(90, 0, 0); camera.orthographicSize = 10;
                        Capture(camera, "L_LevelDiagnostic"); Object.DestroyImmediate(camera.gameObject);
                    }
                    foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (!r.GetComponent<MeshFilter>().sharedMesh) throw new Exception("Unresolved mesh: " + r.name);
                        if (r.sharedMaterials.Any(m => !m)) throw new Exception("Unresolved material: " + r.name);
                    }
                    Notes.Add("Renderers: " + root.GetComponentsInChildren<MeshRenderer>().Length);
                    Object.DestroyImmediate(root);
                }
                File.WriteAllLines(Output + "/Probe.txt", Notes); EditorApplication.Exit(0);
            }
            catch (Exception e) { Notes.Add(e.ToString()); File.WriteAllLines(Output + "/Probe.txt", Notes); EditorApplication.Exit(1); }
        }
    }
}
