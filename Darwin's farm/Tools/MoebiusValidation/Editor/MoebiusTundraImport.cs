using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Darwin.Rendering.Validation
{
    // Offline asset authoring only. Installed outside Assets; runs in a guarded Temp project.
    public static class MoebiusTundraImport
    {
        const string Model = "Assets/Art/ImportedTundra/Tundra_Rocks_v2.fbx";
        const string Content = "Assets/Rendering/Moebius/Content/Biomes/Tundra";
        const string Prefab = "Assets/Rendering/Moebius/Samples/Biomes/Prefabs/Tundra_Colored.prefab";
        const string Scene = "Assets/Rendering/Moebius/Samples/Biomes/Scenes/Tundra_Colored.unity";
        const string Results = "Validation/TundraImport";
        static readonly List<string> Notes = new List<string>();
        static readonly List<string> Errors = new List<string>();
        static readonly Dictionary<string, Color> Palette = new Dictionary<string, Color>
        {
            {"Ground_Moss", C(.72f,.75f,.55f)}, {"Ground_Sage", C(.65f,.71f,.55f)},
            {"Ground_Lichen", C(.80f,.81f,.63f)}, {"Ground_Frost", C(.75f,.79f,.67f)},
            {"Ground_Soil", C(.79f,.74f,.65f)}, {"Slab_Edge", C(.73f,.73f,.57f)},
            {"Rock_Slate", C(.64f,.66f,.69f)}, {"Rock_Light", C(.78f,.77f,.73f)},
            {"Rock_Lavender", C(.61f,.60f,.70f)}, {"Rock_Weathered", C(.71f,.70f,.66f)},
            {"Snow_Ivory", C(.95f,.96f,.92f)},
            {"Shrub_Sage", C(.59f,.67f,.52f)}, {"Shrub_Olive", C(.64f,.70f,.53f)},
            {"Shrub_Frost", C(.71f,.76f,.61f)},
            {"Grass_Straw", C(.85f,.77f,.53f)}, {"Grass_Ochre", C(.72f,.63f,.42f)},
            {"Water_Mist", C(.63f,.77f,.80f)}, {"Shore_Mud", C(.72f,.71f,.62f)},
            {"Ink_Charcoal", C(.24f,.26f,.27f)}
        };
        static Color C(float r, float g, float b) => new Color(r,g,b,1);
        static void Check(bool value, string message)
        {
            Notes.Add((value ? "PASS: " : "FAIL: ") + message);
            if (!value) throw new InvalidOperationException(message);
        }
        static void Guard()
        {
            if (!Application.isBatchMode || !File.Exists("TundraImport.marker") ||
                !Application.dataPath.Replace('\\','/').Contains("/Temp/"))
                throw new InvalidOperationException("Use the isolated Tundra import fixture.");
            Directory.CreateDirectory(Results);
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    Errors.Add(message);
            };
            ShaderUtil.allowAsyncCompilation = false;
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Check(pipeline, "Existing PC URP pipeline is available");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
        }
        public static void RunBatch()
        {
            try
            {
                Guard();
                Directory.CreateDirectory(Content + "/Materials");
                AssetDatabase.Refresh();
                var importer = AssetImporter.GetAtPath(Model) as ModelImporter;
                Check(importer, "Exported Maya FBX is recognized by Unity");
                importer.importCameras = false; importer.importLights = false; importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.importNormals = ModelImporterNormals.Import;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.isReadable = true;
                importer.SaveAndReimport();
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
                Check(source, "Source FBX imports");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("Tundra_Colored");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                instance.transform.SetParent(root.transform, false);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                Check(renderers.Length == 100, "All 100 source mesh renderers are preserved");
                var template = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Samples/Shared/Materials/Peach.mat");
                Check(template, "Existing Moebius NPR material template is available");
                var materials = new Dictionary<string, Material>();
                foreach (var renderer in renderers)
                {
                    var bindings = renderer.sharedMaterials;
                    for (int slot = 0; slot < bindings.Length; slot++)
                    {
                        Check(bindings[slot], renderer.name + ": source material slot " + slot + " exists");
                        string name = bindings[slot].name;
                        Check(Palette.ContainsKey(name), "Recognized original material: " + name);
                        if (!materials.TryGetValue(name, out var material))
                        {
                            material = name == "Water_Mist" ? CreateWater() : CreateSurface(template, name);
                            AssetDatabase.CreateAsset(material, Content + "/Materials/" + material.name + ".mat");
                            materials.Add(name, material);
                        }
                        bindings[slot] = material;
                    }
                    renderer.sharedMaterials = bindings;
                    bool ink = renderer.name.StartsWith("Ink_");
                    renderer.shadowCastingMode = ink || renderer.name == "Pond_ShallowWater" ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    renderer.receiveShadows = !ink;
                }
                BakeGroundColor(renderers.Single(r => r.name == "Ground_Surface"));
                Bounds bounds = BoundsOf(root);
                Check(Mathf.Abs(bounds.size.x - 17) < .02f && Mathf.Abs(bounds.size.z - 17) < .02f,
                    "Original 17 x 17 metre scale is preserved: " + bounds);
                int triangles = renderers.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);
                Check(triangles == 10626, "All 10,626 original triangles are preserved, including snow and fracture geometry");
                Check(materials.Count == 19, "19 original material regions mapped to independent Moebius materials");
                Check(renderers.All(r => r.sharedMaterials.Length == r.GetComponent<MeshFilter>().sharedMesh.subMeshCount),
                    "Every submesh has its corresponding material");
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, Prefab, InteractionMode.AutomatedAction);
                SetupView(bounds);
                EditorSceneManager.SaveScene(scene, Scene);
                AssetDatabase.SaveAssets();
                Capture(Camera.main, "Overview");
                Camera.main.orthographicSize = 5.2f;
                Camera.main.transform.position = new Vector3(-10,9,-7);
                Camera.main.transform.LookAt(new Vector3(-2,1,0));
                Capture(Camera.main, "Detail");
                EditorSceneManager.OpenScene(Scene);
                ValidateLoaded();
                Notes.Add("Material palette: " + string.Join(", ", materials.Keys.OrderBy(x => x)));
                File.WriteAllLines(Results + "/GeneratedAssets.txt", AssetDatabase.GetDependencies(new[] {Scene, Prefab}, true)
                    .Where(p => p.StartsWith(Content + "/") || p.StartsWith("Assets/Art/ImportedTundra/") || p == Scene || p == Prefab));
            }
            catch (Exception e) { Errors.Add(e.ToString()); }
            Finish();
        }
        public static void VerifyReload()
        {
            try { Guard(); EditorSceneManager.OpenScene(Scene); ValidateLoaded(); Capture(Camera.main, "Reloaded"); }
            catch (Exception e) { Errors.Add(e.ToString()); }
            Finish("ReloadReport.txt");
        }
        static Material CreateSurface(Material template, string name)
        {
            var m = new Material(template) { name = "Tundra_" + name, enableInstancing = true };
            m.shaderKeywords = Array.Empty<string>();
            m.SetColor("_BaseColor", Palette[name]);
            m.SetTexture("_BaseMap", null); m.SetTexture("_ControlMap", null);
            m.SetFloat("_UsePaintColor", 0); m.SetFloat("_EnableSpatialGradient", 0); m.SetFloat("_EnableColorRamp", 0);
            bool ink = name.StartsWith("Ink_");
            bool snow = name.StartsWith("Snow_");
            float hatch = name.StartsWith("Rock_") ? .52f : name.StartsWith("Shrub_") ? .38f : .25f;
            if (snow) hatch = .12f;
            m.SetFloat("_UseHatching", ink ? 0 : 1);
            if (!ink) m.EnableKeyword("_USE_HATCHING");
            m.SetFloat("_HatchStrength", hatch); m.SetFloat("_HatchScale", 125);
            m.SetFloat("_HatchLineWidth", .72f); m.SetFloat("_HatchCrossStrength", .18f);
            m.SetFloat("_HatchThirdStrength", .06f);
            m.SetFloat("_UseStipple", !ink && !snow ? 1 : 0);
            if (!ink && !snow) m.EnableKeyword("_USE_STIPPLE");
            m.SetFloat("_StippleStrength", .035f); m.SetFloat("_StippleScale", 85);
            m.SetFloat("_HighlightStrength", 0);
            m.SetFloat("_PaperLift", ink ? 0 : .035f);
            m.SetFloat("_SurfaceTintStrength", ink ? 0 : .12f);
            m.SetFloat("_CastShadowHatchStrength", ink ? 0 : .55f);
            m.SetFloat("_CastShadowTintStrength", ink ? 0 : .12f);
            m.SetColor("_InkColor", Palette["Ink_Charcoal"]);
            return m;
        }
        static Material CreateWater()
        {
            var m = new Material(Shader.Find("Darwin/Moebius Water")) {name = "Tundra_Water_Mist", enableInstancing = true};
            m.SetColor("_WaterColor", Palette["Water_Mist"]);
            m.SetColor("_RippleColor", C(.43f,.58f,.61f));
            m.SetFloat("_EnableWaves", 0); // Preserve the authored shallow pond boundary.
            m.SetFloat("_EnableFoam", 0); m.SetFloat("_EnableShoreMask", 0);
            m.SetFloat("_EnableFoamPatches", 0); m.SetFloat("_EnableShoreMotion", 0);
            m.SetFloat("_EnableWaterfallImpact", 0);
            m.SetFloat("_EnableFlow", 1); m.SetFloat("_FlowSpeed", .12f); m.SetFloat("_FlowStrength", .18f);
            m.SetFloat("_RippleDensity", 2.2f); m.SetFloat("_RippleStrength", .24f);
            m.SetFloat("_RippleLength", .45f); m.SetFloat("_RippleWidth", .011f);
            m.SetFloat("_RippleSpeed", .045f); m.SetFloat("_RippleWobbleStrength", .012f);
            m.SetFloat("_RippleWobbleSpeed", 1.1f);
            m.SetFloat("_ReceiveWaterShadows", 1); m.SetFloat("_WaterShadowStrength", .45f);
            return m;
        }
        static float Cross(Vector2 a, Vector2 b) => a.x*b.y-a.y*b.x;
        static void BakeGroundColor(MeshRenderer renderer)
        {
            // Bake directly into the supplied UV0. Runtime uses the existing albedo paint path.
            const int size = 1024;
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var uv = mesh.uv; var vertices = mesh.vertices; var indices = mesh.triangles;
            Check(uv.Length == vertices.Length && uv.All(u => u.x >= 0 && u.y >= 0 && u.x <= 1 && u.y <= 1),
                "Original ground UV0 is usable for the existing paint system");
            var pixels = new Color[size*size];
            for (int i = 0; i < indices.Length; i += 3)
            {
                int ia=indices[i], ib=indices[i+1], ic=indices[i+2];
                Vector2 a=uv[ia]*size, b=uv[ib]*size, c=uv[ic]*size;
                float area=Cross(b-a,c-a);
                if (Mathf.Abs(area)<.00001f) continue;
                int minX=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x,Mathf.Min(b.x,c.x))),0,size-1);
                int maxX=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x,Mathf.Max(b.x,c.x))),0,size-1);
                int minY=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y,Mathf.Min(b.y,c.y))),0,size-1);
                int maxY=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y,Mathf.Max(b.y,c.y))),0,size-1);
                for (int y=minY; y<=maxY; y++) for (int x=minX; x<=maxX; x++)
                {
                    var q=new Vector2(x+.5f,y+.5f)-a;
                    float v=Cross(q,c-a)/area, w=Cross(b-a,q)/area, u=1-v-w;
                    if (u<-.0001f || v<-.0001f || w<-.0001f) continue;
                    var p=renderer.transform.TransformPoint(vertices[ia]*u+vertices[ib]*v+vertices[ic]*w);
                    float broad=Mathf.PerlinNoise(p.x*.43f+31,p.z*.43f+17);
                    float broken=Mathf.PerlinNoise(p.x*2.1f+81,p.z*2.1f+51);
                    float fleck=Mathf.PerlinNoise(p.x*7.5f+12,p.z*7.5f+67);
                    float moss=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.44f,.57f,broad+(broken-.5f)*.34f+(fleck-.5f)*.09f));
                    var color=Color.Lerp(Palette["Ground_Lichen"],Palette["Ground_Sage"],moss*.78f);
                    float soil=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.58f,.68f,Mathf.PerlinNoise(p.x*.29f+93,p.z*.29f+14)));
                    color=Color.Lerp(color,Palette["Ground_Soil"],soil*.75f);
                    color*=1+(fleck-.5f)*.035f; color.a=1;
                    pixels[y*size+x]=color;
                }
            }
            // Pad UV island borders so bilinear/mip filtering never exposes unpainted seams.
            for (int pass=0;pass<8;pass++)
            {
                var padded=(Color[])pixels.Clone();
                for (int y=1;y<size-1;y++) for (int x=1;x<size-1;x++)
                {
                    int i=y*size+x; if(pixels[i].a>0)continue;
                    foreach(int j in new[]{i-1,i+1,i-size,i+size})
                        if(pixels[j].a>0){padded[i]=pixels[j];break;}
                }
                pixels=padded;
            }
            string path=Content+"/Textures/Tundra_Ground_Color.png";
            Directory.CreateDirectory(Content+"/Textures");
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
            texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.FromInput;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=true;
            importer.SaveAndReimport();
            var saved=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            foreach(var material in renderer.sharedMaterials)
            {
                material.SetTexture("_PaintColorMap",saved);material.SetFloat("_UsePaintColor",1);
                EditorUtility.SetDirty(material);
            }
            // Exercise the actual tool's mesh validation rather than relying only on UV bounds.
            new Darwin.Rendering.Editor.Paint.MoebiusPaintMesh(renderer);
            Check(saved, "Fine moss/lichen albedo is saved as an editable paint texture on original ground UV0");
        }
        static void SetupView(Bounds bounds)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = C(.72f,.76f,.80f); RenderSettings.fog = false;
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1;
            light.transform.rotation = Quaternion.Euler(50,-32,0);
            light.shadows = LightShadows.Soft; light.shadowBias = .035f; light.shadowNormalBias = .12f;
            light.gameObject.AddComponent<UniversalAdditionalLightData>(); RenderSettings.sun = light;
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 10.2f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 150;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = C(.43f,.75f,.79f);
            camera.GetUniversalAdditionalCameraData().SetRenderer(1);
            camera.transform.position = new Vector3(-19,17,-23);
            camera.transform.LookAt(bounds.center + Vector3.down * .7f);
        }
        static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            return bounds;
        }
        static void ValidateLoaded()
        {
            var root = GameObject.Find("Tundra_Colored");
            Check(root, "Saved colored prefab instance reloads");
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            Check(renderers.Length == 100, "Saved scene retains all source renderers");
            Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == Prefab, "Scene uses the reusable colored prefab");
            Check(renderers.All(r => r.GetComponent<MeshFilter>().sharedMesh && r.sharedMaterials.All(m => m && m.shader && m.shader.isSupported)),
                "All saved mesh, material and shader bindings resolve");
            Check(root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "Tile needs no new runtime controllers");
            Capture(Camera.main, "Validation");
            foreach (var name in new[] {"Darwin/Moebius NPR", "Darwin/Moebius Water", "Hidden/Darwin/Moebius Outline"})
            {
                var shader = Shader.Find(name);
                Check(shader, "Existing shader loads: " + name);
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        Errors.Add(name + ": " + message.message);
            }
        }
        static void Capture(Camera camera, string name)
        {
            var rt = new RenderTexture(1600,1100,24,RenderTextureFormat.ARGB32);
            rt.Create(); var texture = new Texture2D(1600,1100,TextureFormat.RGB24,false);
            var previous = RenderTexture.active;
            camera.targetTexture = rt; camera.Render(); camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0,0,1600,1100),0,0); texture.Apply();
            File.WriteAllBytes(Results + "/" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null; rt.Release();
            UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(texture);
        }
        static void Finish(string report = "Report.txt")
        {
            Directory.CreateDirectory(Results);
            File.WriteAllText(Results + "/" + report, string.Join("\n", Notes) + "\nErrors: " + Errors.Count + "\n" + string.Join("\n", Errors));
            EditorApplication.Exit(Errors.Count == 0 ? 0 : 1);
        }
    }
}
