using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Darwin.Rendering;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Darwin.Rendering.Validation
{
    // This authoring / verification helper deliberately stays outside the main project's Assets.
    [InitializeOnLoad]
    public static class MoebiusRiverConnectionScenes
    {
        const string R = "Assets/Rendering/Moebius/Samples/RiverConnections/";
        const string B = "Assets/Rendering/Moebius/Samples/Biomes/";
        const string O = "Validation/RiverConnections";
        static readonly List<string> Notes = new List<string>(), Errors = new List<string>();
        static readonly string[] Cases = { "01_Connected_Rivers", "02_River_To_Land_Capsule", "03_Height_Difference_Waterfall", "04_Multiple_Water_Heights" };
        static MoebiusRiverConnectionScenes()
        {
            Application.logMessageReceived += (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(message); };
            EditorApplication.update += PlayTick;
        }
        static void Guard()
        {
            if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/Temp/") || !File.Exists("RiverConnections.marker")) throw new Exception("Use the marked isolated fixture.");
            Directory.CreateDirectory(O);
        }
        static void Check(bool value, string text) { if (!value) throw new Exception(text); Notes.Add("PASS: " + text); }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path.TrimEnd('/'))) return;
            path = path.TrimEnd('/'); var parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static void Save(Object value, string path)
        {
            Folder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing) { EditorUtility.CopySerialized(value, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(value); }
            else AssetDatabase.CreateAsset(value, path);
        }
        static Vector3[] Positions(Mesh mesh)
        {
            using (var array = MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var values = new NativeArray<Vector3>(array[0].vertexCount, Allocator.Temp)) { array[0].GetVertices(values); return values.ToArray(); }
        }
        static int[] Indices(Mesh mesh)
        {
            using (var array = MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var result = new List<int>();
                for (int slot = 0; slot < array[0].subMeshCount; slot++)
                {
                    var sub = array[0].GetSubMesh(slot); if (sub.topology != MeshTopology.Triangles) continue;
                    using (var values = new NativeArray<int>(sub.indexCount, Allocator.Temp)) { array[0].GetIndices(values, slot, true); result.AddRange(values.ToArray()); }
                }
                return result.ToArray();
            }
        }
        static Mesh WeldMainWater(Vector3[] source, int[] sourceIndices)
        {
            var lookup = new Dictionary<Vector3Int, int>(); var vertices = new List<Vector3>(); var remap = new int[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(source[i].x * 10000), 0, Mathf.RoundToInt(source[i].z * 10000));
                if (!lookup.TryGetValue(key, out int index)) { index = vertices.Count; lookup.Add(key, index); vertices.Add(new Vector3(source[i].x, 0, source[i].z)); }
                remap[i] = index;
            }
            var indices = sourceIndices.Select(i => remap[i]).ToArray(); var parent = Enumerable.Range(0, vertices.Count).ToArray();
            Func<int, int> find = null; find = i => parent[i] == i ? i : parent[i] = find(parent[i]);
            for (int i = 0; i < indices.Length; i += 3) { parent[find(indices[i + 1])] = find(indices[i]); parent[find(indices[i + 2])] = find(indices[i]); }
            var areas = new Dictionary<int, float>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                int part = find(indices[i]); areas.TryGetValue(part, out float area);
                areas[part] = area + Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]], vertices[indices[i + 2]] - vertices[indices[i]]).magnitude;
            }
            int largest = areas.OrderByDescending(p => p.Value).First().Key; var kept = new List<int>();
            for (int i = 0; i < indices.Length; i += 3) if (find(indices[i]) == largest) kept.AddRange(new[] { indices[i], indices[i + 1], indices[i + 2] });
            var mesh = new Mesh { name = "River Surface Source", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(kept, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        static RiverSurfaceProfile Profile(string name)
        {
            var root = PrefabUtility.LoadPrefabContents(B + "Prefabs/" + name + ".prefab");
            try
            {
                var bank = root.GetComponent<MeshRenderer>(); var surface = root.transform.Find("Flowing Water").GetComponent<MeshRenderer>();
                var center = new Vector3(bank.bounds.center.x, bank.bounds.min.y, bank.bounds.center.z);
                var source = surface.GetComponent<MeshFilter>().sharedMesh;
                var mesh = WeldMainWater(Positions(source).Select(p => surface.transform.TransformPoint(p) - center).ToArray(), Indices(source));
                string path = R + "Profiles/" + name + ".asset";
                var profile = AssetDatabase.LoadAssetAtPath<RiverSurfaceProfile>(path);
                if (!profile) { profile = ScriptableObject.CreateInstance<RiverSurfaceProfile>(); profile.name = name; Folder(R + "Profiles"); AssetDatabase.CreateAsset(profile, path); }
                if (profile.waterMesh) { EditorUtility.CopySerialized(mesh, profile.waterMesh); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(profile.waterMesh); }
                else { AssetDatabase.AddObjectToAsset(mesh, profile); profile.waterMesh = mesh; }
                profile.tileSize = 17; profile.bankHeight = bank.bounds.max.y - center.y; profile.waterMaterial = surface.sharedMaterial;
                var ports = new List<RiverPort>(); var positions = profile.waterMesh.vertices; var used = new HashSet<int>(profile.waterMesh.triangles);
                for (int side = 0; side < 4; side++)
                {
                    var port = new RiverPort { side = (RiverSide)side }; var n = port.Normal; var t = Vector3.Cross(Vector3.up, n);
                    var edge = used.Select(i => positions[i]).Where(p => Mathf.Abs(Vector3.Dot(p, n) - 8.5f) < .005f).Select(p => Vector3.Dot(p, t)).ToArray();
                    if (edge.Length < 2) continue;
                    port.width = edge.Max() - edge.Min(); if (port.width < .5f) continue;
                    var p = n * 8.5f + t * ((edge.Min() + edge.Max()) * .5f); port.center = new Vector2(p.x, p.z); ports.Add(port);
                    Notes.Add(name + ": port " + port.side + " centre=" + port.center + " width=" + port.width);
                }
                profile.ports = ports.ToArray(); profile.shoreRange = surface.sharedMaterial.GetFloat("_ShoreDistanceRange");
                profile.shoreRect = surface.sharedMaterial.GetVector("_ShoreWorldRect");
                var rect = profile.shoreRect; rect.x += surface.transform.position.x - center.x; rect.y += surface.transform.position.z - center.z; profile.shoreRect = rect;
                profile.shoreResolution = 256; profile.shoreDistances = new byte[256 * 256];
                string maskPath = AssetDatabase.GetAssetPath(surface.sharedMaterial.GetTexture("_ShorelineMask"));
                var pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false, true); pixels.LoadImage(File.ReadAllBytes(maskPath));
                for (int z = 0; z < 256; z++) for (int x = 0; x < 256; x++) profile.shoreDistances[z * 256 + x] = (byte)Mathf.RoundToInt(pixels.GetPixelBilinear((x + .5f) / 256, (z + .5f) / 256).r * 255);
                Object.DestroyImmediate(pixels); EditorUtility.SetDirty(profile);
                Check(ports.Count == (name == "L_New" ? 2 : 4), name + ": authored river mouths detected"); return profile;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static GameObject Wrapper(string name, RiverSurfaceProfile profile)
        {
            var wrapper = new GameObject(profile ? name : "Grassland Land");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(B + "Prefabs/" + (profile ? name : "Grassland_Moebius") + ".prefab"));
            model.transform.SetParent(wrapper.transform, false);
            var tile = wrapper.AddComponent<MoebiusRiverTile>(); tile.profile = profile; tile.waterLevel = .25f;
            if (profile)
            {
                tile.originalWater = model.transform.Find("Flowing Water").GetComponent<MeshRenderer>();
                // L_New's outer ledge is ~10cm above its lowest channel point.
                // Meet the real perimeter, rather than using bounds.min.y as a wall top.
                var bank = model.GetComponent<MeshFilter>();
                float half = profile.tileSize * .5f;
                var perimeter = Positions(bank.sharedMesh).Select(p => wrapper.transform.InverseTransformPoint(bank.transform.TransformPoint(p)))
                    .Where(p => Mathf.Abs(Mathf.Abs(p.x) - half) < .005f || Mathf.Abs(Mathf.Abs(p.z) - half) < .005f);
                tile.foundationTop = Mathf.Max(0, perimeter.Max(p => p.y));
            }
            else
            {
                var ground = model.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled && !r.forceRenderingOff)
                    .OrderByDescending(r => r.bounds.size.x * r.bounds.size.z).ThenByDescending(r => r.bounds.max.y).First();
                Notes.Add("Land source ground: " + ground.name + " bounds " + ground.bounds.ToString("F3"));
                model.transform.localScale *= 17 / Mathf.Max(ground.bounds.size.x, ground.bounds.size.z);
                var b = ground.bounds; model.transform.position -= new Vector3(b.center.x, b.max.y - 1.1f, b.center.z);
                tile.landSurfaceHeight = 1.1f; tile.foundationTop = Mathf.Max(0, ground.bounds.min.y);
                Notes.Add("Land aligned ground: " + ground.bounds.ToString("F3") + "; entire model " + BoundsOf(model.GetComponentsInChildren<MeshRenderer>()).ToString("F3"));
                var visible = BoundsOf(model.GetComponentsInChildren<MeshRenderer>());
                Check(visible.size.x < 18 && visible.size.z < 18 && new Vector2(visible.center.x, visible.center.z).magnitude < .5f, "Visible land model aligns with its 17 metre wrapper");
                PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
            }
            Folder(R + "Prefabs"); string path = R + "Prefabs/" + (profile ? name : "Grassland_Land") + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(wrapper, path); Object.DestroyImmediate(wrapper); return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        static Material Palette(string name, Color color)
        {
            var material = new Material(Shader.Find("Darwin/Moebius NPR")) { name = name };
            material.SetColor("_BaseColor", color); material.SetFloat("_UseHatching", 1); material.EnableKeyword("_USE_HATCHING");
            material.SetFloat("_UseTriplanar", 1); material.EnableKeyword("_USE_TRIPLANAR"); material.SetFloat("_HatchScale", 4);
            material.SetFloat("_HatchStrength", .45f); material.SetFloat("_UseStipple", 1); material.EnableKeyword("_USE_STIPPLE"); material.SetFloat("_StippleStrength", .025f);
            material.SetFloat("_HighlightStrength", .05f); material.SetFloat("_HighlightSize", .02f); material.SetFloat("_HighlightThreshold", .99f);
            string path = R + "Materials/" + name + ".mat"; Save(material, path); return AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        static MoebiusRiverTile Tile(GameObject prefab, Transform parent, string name, Vector3 position, float yaw = 0)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab); root.name = name; root.transform.SetParent(parent, false);
            root.transform.localPosition = position; root.transform.localRotation = Quaternion.Euler(0, yaw, 0); return root.GetComponent<MoebiusRiverTile>();
        }
        static Bounds BoundsOf(IEnumerable<MeshRenderer> renderers)
        { var list = renderers.Where(r => r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy).ToArray(); var b = list[0].bounds; foreach (var r in list.Skip(1)) b.Encapsulate(r.bounds); return b; }
        static Camera SetupView(Bounds bounds, Vector3? view = null)
        {
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.72f, .76f, .8f); RenderSettings.fog = false;
            var light = new GameObject("Directional Light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50, -30, 0); light.gameObject.AddComponent<UniversalAdditionalLightData>(); RenderSettings.sun = light;
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera"; camera.orthographic = true;
            camera.nearClipPlane = .1f; camera.farClipPlane = 300; camera.GetUniversalAdditionalCameraData().SetRenderer(1);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.45f, .75f, .79f);
            camera.transform.position = bounds.center + (view ?? new Vector3(-1.15f, 1.25f, -1.3f)) * 40; camera.transform.LookAt(bounds.center);
            float halfHeight = 0;
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            { var p = camera.transform.InverseTransformPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z))); halfHeight = Mathf.Max(halfHeight, Mathf.Abs(p.y), Mathf.Abs(p.x) / (1500f / 1050)); }
            camera.orthographicSize = halfHeight * 1.08f; return camera;
        }
        static Color32[] Capture(Camera camera, string name)
        {
            var active = RenderTexture.active; var target = camera.targetTexture;
            var rt = RenderTexture.GetTemporary(1500, 1050, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var texture = new Texture2D(1500, 1050, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1500, 1050), 0, 0); texture.Apply();
            File.WriteAllBytes(O + "/" + name + ".png", texture.EncodeToPNG()); var data = texture.GetPixels32(); Object.DestroyImmediate(texture);
            camera.targetTexture = target; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); return data;
        }
        static void Scene(int index, GameObject l, GameObject cross, GameObject land, Material bank, Material foundation)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Automatic River Network"); var network = root.AddComponent<MoebiusRiverNetwork>(); network.autoRefresh = false;
            network.bankMaterial = bank; network.foundationMaterial = foundation;
            network.waterfallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rendering/Moebius/Samples/Waterfall/MoebiusWaterfall.prefab");
            network.impactMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Samples/Waterfall/Materials/ImpactFoam.mat");
            if (index == 0) { Tile(l, root.transform, "River L", Vector3.zero); Tile(cross, root.transform, "River Cross", new Vector3(-17, 0, 0)); }
            else if (index == 1) { Tile(l, root.transform, "River L", Vector3.zero); Tile(land, root.transform, "Land closes west river mouth", new Vector3(-17, 0, 0)); }
            else if (index == 2) { Tile(l, root.transform, "Upper River L", new Vector3(0, 3, 0)); Tile(cross, root.transform, "Lower River Cross", new Vector3(-17, 0, 0)); }
            else
            {
                Tile(cross, root.transform, "Upper East River", new Vector3(17, 5.5f, 0));
                Tile(cross, root.transform, "Middle River", new Vector3(0, 2.5f, 0));
                Tile(l, root.transform, "Lower West River", new Vector3(-17, 0, 0), 180);
                Tile(l, root.transform, "Upper North River", new Vector3(0, 4.5f, 17));
            }
            Check(network.RebuildNow(), Cases[index] + ": network builds: " + network.LastError);
            Check(network.JoinedCount == (index == 0 ? 1 : 0), Cases[index] + ": expected level joins " + network.JoinedCount);
            Check(network.WaterfallCount == (index == 2 ? 1 : index == 3 ? 3 : 0), Cases[index] + ": expected waterfalls " + network.WaterfallCount);
            Check(network.ClosedCount > 0, Cases[index] + ": rounded ends generated " + network.ClosedCount);
            var camera = SetupView(BoundsOf(root.GetComponentsInChildren<MeshRenderer>()), index == 1 ? (Vector3?)new Vector3(.8f, 1.8f, -1.2f) : null);
            network.autoRefresh = true;
            string path = R + "Scenes/" + Cases[index] + ".unity"; Folder(R + "Scenes"); Check(EditorSceneManager.SaveScene(scene, path), Cases[index] + ": scene saved");
            Check(!File.ReadAllText(path).Contains("Generated River Connections"), Cases[index] + ": transient meshes are not serialized into the scene");
            Capture(camera, Cases[index]);
            if (index == 1)
            {
                var originalPosition = camera.transform.position; var originalRotation = camera.transform.rotation; float originalSize = camera.orthographicSize;
                var focus = new Vector3(-6.7f, .25f, 0);
                camera.transform.position = focus + new Vector3(0, 20, -2); camera.transform.LookAt(focus); camera.orthographicSize = 5;
                Capture(camera, "Capsule_Closeup"); camera.transform.SetPositionAndRotation(originalPosition, originalRotation); camera.orthographicSize = originalSize;
            }
            foreach (var shader in root.GetComponentsInChildren<MeshRenderer>().Where(r => r.sharedMaterial).Select(r => r.sharedMaterial.shader).Distinct())
                foreach (var diagnostic in ShaderUtil.GetShaderMessages(shader)) if (diagnostic.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) Errors.Add(diagnostic.message);
            int count = network.RebuildCount; network.SendMessage("LateUpdate"); Check(network.RebuildCount == count, Cases[index] + ": unchanged topology does not rebuild");
            EditorSceneManager.OpenScene(path); network = Object.FindFirstObjectByType<MoebiusRiverNetwork>();
            Check(network.RebuildNow(), Cases[index] + ": reload rebuilds from persistent profiles: " + network.LastError);
            Check(network.WaterfallCount == (index == 2 ? 1 : index == 3 ? 3 : 0), Cases[index] + ": waterfall count survives reload");
            foreach (var tile in network.GetComponentsInChildren<MoebiusRiverTile>()) if (tile.IsRiver)
                Check(tile.profile.waterMesh.isReadable && tile.originalWater && tile.originalWater.GetComponent<MeshFilter>().sharedMesh, tile.name + ": original meshes and runtime source retained");
            if (index == 0) CheckLiveEdits(network);
        }
        static void CheckLiveEdits(MoebiusRiverNetwork network)
        {
            var tiles = network.GetComponentsInChildren<MoebiusRiverTile>(); var l = tiles.First(t => t.name == "River L"); var other = tiles.First(t => t != l);
            var position = other.transform.position; var rotation = l.transform.rotation;
            l.transform.Rotate(0, 180, 0); network.SendMessage("LateUpdate"); Check(network.JoinedCount == 0 && network.ClosedCount == 6, "Rotating mismatched mouths automatically closes both sides");
            l.transform.rotation = rotation; other.transform.position += Vector3.right; network.SendMessage("LateUpdate"); Check(network.JoinedCount == 0, "A displaced tile no longer connects");
            other.transform.position = position + Vector3.up * 3; network.SendMessage("LateUpdate"); Check(network.WaterfallCount == 1, "Changing terrain elevation automatically creates a downhill waterfall");
            var sheet = network.GeneratedRoot.GetComponentsInChildren<MeshRenderer>().First(r => r.name == "Waterfall Sheet");
            var mesh = sheet.GetComponent<MeshFilter>().sharedMesh; var localTop = mesh.vertices.OrderByDescending(p => p.y).First(); var top = sheet.transform.TransformPoint(localTop);
            Check(Mathf.Abs(top.y - other.WorldWaterHeight) < .0001f, "Curved waterfall lip meets the upstream mean water level");
            Check(Vector3.Dot(sheet.transform.TransformDirection(mesh.normals.Last()), Vector3.up) > .999f, "Curved waterfall ends with a horizontal tangent");
            other.transform.position = position; network.SendMessage("LateUpdate"); int renderers = network.GeneratedRoot.GetComponentsInChildren<MeshRenderer>().Length;
            for (int i = 0; i < 4; i++) { other.transform.position += Vector3.up; network.SendMessage("LateUpdate"); other.transform.position = position; network.SendMessage("LateUpdate"); }
            Check(network.JoinedCount == 1 && network.WaterfallCount == 0 && network.GeneratedRoot.GetComponentsInChildren<MeshRenderer>().Length == renderers, "Repeated height edits restore the join without accumulating generated objects");
            network.enabled = false; Check(tiles.All(t => !t.originalWater.forceRenderingOff), "Disabling the network restores original water renderers");
            network.enabled = true; network.SendMessage("LateUpdate"); Check(network.JoinedCount == 1, "Re-enabling the network restores automatic connections");
        }
        static void CheckExistingWater()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Existing Water Comparison"); var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = Darwin.Rendering.Editor.MoebiusWaterSetup.CreateGrid(12, 12, 32);
            var renderer = go.AddComponent<MeshRenderer>(); var template = AssetDatabase.LoadAssetAtPath<Material>(B + "Materials/River/River_I_Water.mat");
            var a = new Material(template); var b = new Material(template); b.shader = Shader.Find("Darwin/Validation/Moebius Water Baseline");
            Check(b.shader, "Original pre-connection water shader available for comparison");
            foreach (var m in new[] { a, b })
            {
                foreach (string property in new[] { "_FlowSpeed", "_RippleSpeed", "_RippleWobbleSpeed", "_FoamSpeed", "_WaveSpeed", "_AnimationPhase", "_EnableWaterfallImpact", "_EnableShoreMotion" }) m.SetFloat(property, 0);
                m.SetFloat("_EnableShoreMask", 0); m.SetFloat("_EnableFoam", 0);
            }
            a.SetVector("_RiverWaveFadeWidths", Vector4.zero); var camera = SetupView(renderer.bounds); renderer.sharedMaterial = a;
            var after = Capture(camera, "ExistingWater_After"); renderer.sharedMaterial = b; var before = Capture(camera, "ExistingWater_Before");
            double error = 0;
            for (int i = 0; i < before.Length; i++) error += Math.Abs(before[i].r - after[i].r) + Math.Abs(before[i].g - after[i].g) + Math.Abs(before[i].b - after[i].b);
            error /= before.Length * 3.0 * 255;
            Check(error < .0002, "Existing water unchanged with river lip fade disabled; mean RGB difference=" + error);
            Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(filter.sharedMesh);
        }
        public static void Build()
        {
            try
            {
                Guard(); ShaderUtil.allowAsyncCompilation = false;
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var lp = Profile("L_New"); var cp = Profile("Cross_River"); AssetDatabase.SaveAssets();
                var l = Wrapper("L_New", lp); var cross = Wrapper("Cross_River", cp); var land = Wrapper("Grassland_Land", null);
                var bank = Palette("River_Closure_Grass", new Color(.44f, .51f, .35f)); var foundation = Palette("River_Cliff_Earth", new Color(.57f, .53f, .40f));
                CheckExistingWater();
                for (int i = 0; i < Cases.Length; i++) Scene(i, l, cross, land, bank, foundation);
                Check(Errors.Count == 0, "No Unity or shader errors: " + string.Join("; ", Errors)); AssetDatabase.SaveAssets();
                Notes.Add("Errors: 0"); File.WriteAllLines(O + "/Report.txt", Notes); EditorApplication.Exit(0);
            }
            catch (Exception e) { Notes.Add(e.ToString()); Notes.AddRange(Errors); File.WriteAllLines(O + "/Report.txt", Notes); EditorApplication.Exit(1); }
        }
        const string PlayKey = "MoebiusRiverConnections.Play", StepKey = "MoebiusRiverConnections.Step";
        static int playFrames, stableCount;
        static GameObject playLand;
        public static void RunPlay()
        {
            Guard(); File.WriteAllText(O + "/PlayReport.txt", "Native Play Mode interaction checks\n");
            EditorSceneManager.OpenScene(R + "Scenes/" + Cases[0] + ".unity");
            SessionState.SetBool(PlayKey, true); SessionState.SetInt(StepKey, 0); EditorApplication.EnterPlaymode();
        }
        static void PlayCheck(bool value, string message)
        {
            File.AppendAllText(O + "/PlayReport.txt", (value ? "PASS: " : "FAIL: ") + message + "\n");
            if (!value) throw new Exception(message);
        }
        static void PlayTick()
        {
            if (!SessionState.GetBool(PlayKey, false)) return;
            int step = SessionState.GetInt(StepKey, 0);
            if (!EditorApplication.isPlaying)
            {
                if (step == 99 && !EditorApplication.isPlayingOrWillChangePlaymode)
                { SessionState.SetBool(PlayKey, false); File.AppendAllText(O + "/PlayReport.txt", "Errors: 0\n"); EditorApplication.Exit(0); }
                return;
            }
            if (++playFrames < (step == 0 || step == 7 ? 30 : 6)) return;
            playFrames = 0;
            try
            {
                var network = Object.FindFirstObjectByType<MoebiusRiverNetwork>();
                var tiles = network.GetComponentsInChildren<MoebiusRiverTile>(true); var l = tiles.First(t => t.name == "River L"); var other = tiles.First(t => t.name == "River Cross");
                if (step == 0) { PlayCheck(network.JoinedCount == 1 && network.WaterfallCount == 0, "Initial reciprocal mouths join without an explicit rebuild call"); l.transform.rotation = Quaternion.Euler(0, 180, 0); }
                else if (step == 1) { PlayCheck(network.JoinedCount == 0, "Runtime rotation closes mismatched mouths"); l.transform.rotation = Quaternion.identity; other.transform.position = new Vector3(-16, 0, 0); }
                else if (step == 2) { PlayCheck(network.JoinedCount == 0, "Runtime displacement disconnects tiles"); other.transform.position = new Vector3(-17, 3, 0); }
                else if (step == 3) { PlayCheck(network.WaterfallCount == 1 && network.JoinedCount == 0, "Runtime elevation change creates the downhill waterfall"); Capture(Camera.main, "Play_Waterfall"); other.transform.position = new Vector3(-17, 0, 0); }
                else if (step == 4)
                {
                    PlayCheck(network.JoinedCount == 1 && network.WaterfallCount == 0, "Returning to equal height removes the waterfall and reconnects water");
                    other.gameObject.SetActive(false); playLand = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(R + "Prefabs/Grassland_Land.prefab"));
                    playLand.transform.SetParent(network.transform, false); playLand.transform.localPosition = new Vector3(-17, 0, 0);
                }
                else if (step == 5) { PlayCheck(network.JoinedCount == 0 && network.ClosedCount == 2, "Replacing a water neighbour with land produces capsule ends"); playLand.SetActive(false); other.gameObject.SetActive(true); }
                else if (step == 6) { PlayCheck(network.JoinedCount == 1, "Replacing land with a river reopens the mouth"); stableCount = network.RebuildCount; }
                else if (step == 7)
                {
                    PlayCheck(network.RebuildCount == stableCount, "Stationary topology does not rebuild during subsequent frames");
                    PlayCheck(Errors.Count == 0, "No runtime errors: " + string.Join("; ", Errors)); SessionState.SetInt(StepKey, 99); EditorApplication.ExitPlaymode(); return;
                }
                SessionState.SetInt(StepKey, step + 1);
            }
            catch (Exception e) { SessionState.SetBool(PlayKey, false); File.AppendAllText(O + "/PlayReport.txt", e + "\n" + string.Join("\n", Errors)); EditorApplication.Exit(1); }
        }
    }
}
