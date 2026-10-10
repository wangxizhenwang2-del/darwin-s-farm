using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Darwin.Rendering.Editor
{
    // This builder/validator is injected only into the isolated project by ValidateMoebiusWater.ps1.
    public static class MoebiusWaterValidation
    {
        const string Root = "Assets/Rendering/Moebius/Samples/Water";
        const string Results = "Validation/Water";
        static readonly List<string> checks = new List<string>(), errors = new List<string>();
        static Camera camera;
        static MeshRenderer water;
        static Material material;
        static RenderTexture liveTarget;
        static double liveStart, liveLast;
        static readonly List<double> liveTimes = new List<double>();
        public static void RunAutomaticWaterBatch()
        {
            Directory.CreateDirectory(Results);
            Application.logMessageReceived += (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
            try
            {
                Check(Application.isBatchMode && File.Exists("WaterValidation.marker"), "Isolated automatic-water validation");
                ShaderUtil.allowAsyncCompilation = false;
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                OpenWaterDemo();
                TestWaterShadows();
                OpenWaterDemo();
                TestRippleWobble();
                TestContinuousWaterSamples();
                var filters = MoebiusWaterSetup.CollectShoreMeshes(water, water.bounds, 0);
                Check(filters.Count == 7, "All seven visible shore meshes detected without a manual list");
                var ignore = new List<GameObject>();
                var transparent = GameObject.CreatePrimitive(PrimitiveType.Cube); ignore.Add(transparent);
                var transparentMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                transparentMaterial.renderQueue = 3000; transparent.GetComponent<Renderer>().sharedMaterial = transparentMaterial;
                var actor = GameObject.CreatePrimitive(PrimitiveType.Cube); actor.AddComponent<Animator>(); ignore.Add(actor);
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.AddComponent<Rigidbody>(); ignore.Add(body);
                var billboard = GameObject.CreatePrimitive(PrimitiveType.Quad); ignore.Add(billboard);
                var billboardMaterial = new Material(Shader.Find("Darwin/Moebius Billboard Character")); billboard.GetComponent<Renderer>().sharedMaterial = billboardMaterial;
                var hidden = GameObject.CreatePrimitive(PrimitiveType.Cube); hidden.GetComponent<Renderer>().enabled = false; ignore.Add(hidden);
                var scene = water.gameObject.scene;
                var foreign = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                GameObject.CreatePrimitive(PrimitiveType.Cube);
                Check(MoebiusWaterSetup.CollectShoreMeshes(water, water.bounds, 0).Count == 7, "Transparent, billboard, animated, physics, hidden and foreign-scene objects excluded");
                EditorSceneManager.CloseScene(foreign, true); UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                foreach (var go in ignore) UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(transparentMaterial); UnityEngine.Object.DestroyImmediate(billboardMaterial);
                string maskPath = Root + "/Textures/ShoreDistance.png", matPath = AssetDatabase.GetAssetPath(material);
                string maskGuid = AssetDatabase.AssetPathToGUID(maskPath), matGuid = AssetDatabase.AssetPathToGUID(matPath);
                var mask = MoebiusWaterSetup.BakeAutomatically(water, 512, 4, maskPath, out var bounds);
                MoebiusWaterSetup.AssignShoreline(material, mask, bounds, 4); AssetDatabase.SaveAssets();
                Check(AssetDatabase.AssetPathToGUID(maskPath) == maskGuid && AssetDatabase.AssetPathToGUID(matPath) == matGuid, "Updated demo shoreline/material retain original GUIDs");
                Check(bounds.size.x > water.bounds.size.x && bounds.size.z > water.bounds.size.z, "Mapping includes nearby real shores beyond water bounds");
                var demoMask = LoadMask(mask);
                foreach (var point in new[] { new Vector2(8.8f, 0), new Vector2(8.8f, 6.8f), new Vector2(8.8f, -6.8f) })
                    Check(DistanceAt(demoMask, bounds, point) > 0.5f, "Demo open edge has no artificial foam at " + point);
                Check(DistanceAt(demoMask, bounds, new Vector2(-8, 0)) < 0.01f, "Demo sand bank remains a detected shoreline");
                Check(DistanceAt(demoMask, bounds, new Vector2(1.4f, -1.8f)) < 0.01f, "Demo rock remains a detected shoreline");
                UnityEngine.Object.DestroyImmediate(demoMask);
                File.WriteAllLines(Results + "/AutomaticDelivery.txt", new[] { matPath, maskPath });
                Capture("AutomaticWaterOverview", 1600, 1100);
                TestAutomaticRegions();
                OpenWaterDemo();
                camera.transform.position = new Vector3(8, 5.5f, -9); camera.transform.LookAt(new Vector3(-0.2f, 0, -0.3f)); camera.orthographicSize = 5.5f;
                Capture("AutomaticWaterCloseup", 1600, 1100);
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors.Add(message.message);
                Check(errors.Count == 0, "Automatic bake, Terrain support and water shader compile without errors");
                // Actual successive Editor renders: URP uses realtimeSinceStartup here, no phase overrides.
                liveTarget = new RenderTexture(800, 550, 24, RenderTextureFormat.ARGB32); liveTarget.Create(); camera.targetTexture = liveTarget;
                // Warm the new target and finish the screenshot readback before measuring cadence.
                camera.Render(); Read(liveTarget, "LiveWaterStart");
                liveTimes.Clear();
                liveStart = liveLast = EditorApplication.timeSinceStartup;
                EditorApplication.update += RenderLiveFrame;
            }
            catch (Exception exception) { errors.Add(exception.ToString()); FinishAutomatic(1); }
        }
        static void OpenWaterDemo()
        {
            EditorSceneManager.OpenScene(Root + "/MoebiusWaterDemo.unity");
            water = GameObject.Find("Moebius Water").GetComponent<MeshRenderer>(); material = water.sharedMaterial; camera = Camera.main;
        }
        static void TestWaterShadows()
        {
            var saved = new Material(material);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = new Vector3(5,2,-3);
            cube.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            try
            {
                camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                camera.transform.position = new Vector3(5,14,-3); camera.transform.rotation = Quaternion.Euler(90,0,0); camera.orthographicSize = 3;
                water.receiveShadows = true;
                material.SetFloat("_EnableWaves",0); material.SetFloat("_EnableFlow",0); material.SetFloat("_EnableRipples",0);
                material.SetFloat("_EnableFoam",0); material.SetFloat("_EnableVariation",0);
                material.SetFloat("_ReceiveWaterShadows",0);
                var disabled = Capture("WaterShadowDisabled",800,550);
                material.SetFloat("_ReceiveWaterShadows",1); material.SetFloat("_WaterShadowStrength",0);
                Check(Difference(disabled,Capture(null,800,550))==0,"Zero shadow strength equals disabled shadow reception");
                material.SetFloat("_WaterShadowStrength",0.65f);
                var shadows = Capture("WaterShadowInk",800,550);
                Check(Difference(disabled,shadows)>100,"Actual URP main-light shadow map produces ink on the water");
                cube.transform.position += Vector3.right;
                var moved = Capture("WaterShadowCasterMoved",800,550);
                Check(Difference(shadows,moved)>100,"Water shadow follows a moving caster without a shoreline rebake");
                var sun = UnityEngine.Object.FindFirstObjectByType<Light>();
                sun.transform.rotation = Quaternion.Euler(52,35,0);
                Check(Difference(moved,Capture("WaterShadowLightTurned",800,550))>100,"Water shadow responds to main-light direction");
            }
            finally { material.CopyPropertiesFromMaterial(saved); UnityEngine.Object.DestroyImmediate(saved); UnityEngine.Object.DestroyImmediate(cube); }
        }
        static void TestRippleWobble()
        {
            var saved = new Material(material);
            var cameraData = camera.GetUniversalAdditionalCameraData();
            bool postProcessing = cameraData.renderPostProcessing;
            try
            {
                // Use the ordinary camera renderer to isolate surface ink from paper/outline animation.
                cameraData.SetRenderer(0); cameraData.renderPostProcessing = false;
                material.SetFloat("_EnableFlow", 0); material.SetFloat("_RippleSpeed", 0);
                material.SetFloat("_EnableWaves", 0); material.SetFloat("_EnableFoam", 0); material.SetFloat("_EnableVariation", 0);
                material.SetFloat("_RippleWobbleStrength", 0); material.SetFloat("_AnimationPhase", 0);
                var still = Capture(null, 800, 550);
                material.SetFloat("_AnimationPhase", 0.5f);
                Check(Difference(still, Capture(null, 800, 550)) == 0, "Zero wobble strength leaves stationary ripples exactly unchanged over time");
                material.SetFloat("_AnimationPhase", 0); material.SetFloat("_RippleWobbleStrength", 0.035f); material.SetFloat("_RippleWobbleSpeed", 3);
                var wobble = Capture("RippleWobbleStart", 800, 550);
                Check(Difference(still, wobble) > 50, "Wobble visibly deforms ripple curves independently of flow");
                material.SetFloat("_AnimationPhase", 0.5f);
                Check(Difference(wobble, Capture("RippleWobbleHalfSecond", 800, 550)) > 50, "Ripple shapes animate with continuous time while drift and flow are disabled");
            }
            finally { material.CopyPropertiesFromMaterial(saved); UnityEngine.Object.DestroyImmediate(saved); cameraData.SetRenderer(1); cameraData.renderPostProcessing = postProcessing; }
        }
        static void TestContinuousWaterSamples()
        {
            var saved = new Material(material);
            try
            {
                camera.transform.position = new Vector3(8,5.5f,-9); camera.transform.LookAt(new Vector3(-0.2f,0,-0.3f)); camera.orthographicSize = 5.5f;
                Capture(null,800,550); // Initialize camera globals before standalone surface draws.
                material.SetFloat("_EnableWaves",0); material.SetFloat("_EnableVariation",0); material.SetFloat("_ReceiveWaterShadows",0); material.SetFloat("_AnimationPhase",0);
                var rows = new List<string> { "effect,frame,timeSeconds,changedPixels" };
                foreach (bool foam in new[] { false,true })
                {
                    string effect = foam ? "Foam" : "Flow";
                    material.SetFloat("_EnableRipples",foam ? 0 : 1); material.SetFloat("_EnableFlow",foam ? 0 : 1); material.SetFloat("_EnableFoam",foam ? 1 : 0);
                    var previous = CaptureTimedWater(2,effect+"FixedTimeStart");
                    Check(previous.Any(pixel => pixel.g>20),effect+": timed draw contains visible water");
                    Check(Difference(previous,CaptureTimedWater(2,null))==0,effect+": identical pinned shader time reproduces identical pixels");
                    var deltas = new List<int>();
                    for (int frame=1;frame<=60;frame++)
                    {
                        float time = 2+frame/60f;
                        var next = CaptureTimedWater(time,frame==60 ? effect+"FixedTimeEnd" : null);
                        int change = Difference(previous,next); deltas.Add(change); previous=next;
                        rows.Add(effect+","+frame+","+time.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+","+change);
                    }
                    Check(deltas.Sum()>100,effect+": actual shader animation changes across one second");
                    checks.Add(effect+" pinned 1/60-second samples: changed-pixel min "+deltas.Min()+", mean "+deltas.Average().ToString("F1")+", max "+deltas.Max()+"; zero-change intervals "+deltas.Count(value=>value==0));
                }
                File.WriteAllLines(Results+"/ContinuousWaterSamples.csv",rows);
            }
            finally { material.CopyPropertiesFromMaterial(saved); UnityEngine.Object.DestroyImmediate(saved); }
        }
        static Color32[] CaptureTimedWater(float time,string name)
        {
            var target = new RenderTexture(800,550,24,RenderTextureFormat.ARGB32); target.Create();
            var commands = new CommandBuffer(); commands.SetRenderTarget(target); commands.ClearRenderTarget(true,true,Color.black);
            var projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix,true); var view = camera.worldToCameraMatrix;
            commands.SetViewProjectionMatrices(view,projection);
            commands.SetGlobalMatrix("unity_MatrixVP",projection*view);
            commands.SetGlobalMatrix("unity_MatrixV",view); commands.SetGlobalMatrix("glstate_matrix_projection",projection);
            commands.SetGlobalVector("_Time",new Vector4(time/20,time,time*2,time*3));
            commands.DrawMesh(water.GetComponent<MeshFilter>().sharedMesh,water.transform.localToWorldMatrix,material,0,material.FindPass("WaterForward"));
            Graphics.ExecuteCommandBuffer(commands); commands.Release();
            var pixels = Read(target,name); target.Release(); UnityEngine.Object.DestroyImmediate(target); return pixels;
        }
        static void TestAutomaticRegions()
        {
            const string folder = "Assets/Editor/WaterValidationData";
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var surface = MeshObject("Only water", MoebiusWaterSetup.CreateGrid(18, 14, 16), new Material(Shader.Find("Darwin/Moebius Water")));
            surface.transform.position = Vector3.up * 0.5f;
            var renderer = surface.GetComponent<MeshRenderer>();
            var mask = MoebiusWaterSetup.BakeAutomatically(renderer, 128, 4, folder + "/Region.png", out var bounds);
            var image = LoadMask(mask);
            Check(DistanceAt(image, bounds, Vector2.zero) > 0.99f && DistanceAt(image, bounds, new Vector2(-8.95f, 0)) > 0.99f, "Open water without land stays foam-free at the center and outer edge");
            UnityEngine.Object.DestroyImmediate(image);
            var neighbor = MeshObject("Adjacent water", MoebiusWaterSetup.CreateGrid(18, 14, 16), renderer.sharedMaterial);
            neighbor.transform.position = new Vector3(18, 0.5f, 0);
            mask = MoebiusWaterSetup.BakeAutomatically(renderer, 128, 4, folder + "/Adjacent.png", out bounds); image = LoadMask(mask);
            Check(DistanceAt(image, bounds, new Vector2(8.95f, 0)) > 0.9f, "Same-level adjacent water does not form a false shoreline at its seam");
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(neighbor);
            var surrounding = MeshObject("Surrounding water", MoebiusWaterSetup.CreateGrid(60, 60, 16), renderer.sharedMaterial);
            surrounding.transform.position = Vector3.up * 0.5f;
            mask = MoebiusWaterSetup.BakeAutomatically(renderer, 128, 4, folder + "/AllWater.png", out bounds); image = LoadMask(mask);
            Check(DistanceAt(image, bounds, new Vector2(8.95f, 0)) > 0.99f, "Entirely water-covered mapping produces a neutral distance field without artificial foam");
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(surrounding);
            var terrainData = new TerrainData { heightmapResolution = 33, size = new Vector3(8, 2, 8) };
            var heights = new float[33, 33];
            for (int z = 0; z < 33; z++) for (int x = 0; x < 33; x++) heights[z, x] = x / 32f;
            terrainData.SetHeights(0, 0, heights);
            var terrain = Terrain.CreateTerrainGameObject(terrainData); terrain.transform.position = new Vector3(-4, -0.5f, -4);
            mask = MoebiusWaterSetup.BakeAutomatically(renderer, 128, 4, folder + "/Terrain.png", out bounds); image = LoadMask(mask);
            Check(DistanceAt(image, bounds, new Vector2(-2, 0)) > 0.2f && DistanceAt(image, bounds, new Vector2(2, 0)) < 0.01f, "Unity Terrain automatically detected using its height, without colliders or a manual list");
            UnityEngine.Object.DestroyImmediate(image);
            var holes = new bool[32, 32];
            for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++) holes[z, x] = !(x >= 22 && x <= 26 && z >= 14 && z <= 18);
            terrainData.SetHoles(0, 0, holes);
            mask = MoebiusWaterSetup.BakeAutomatically(renderer, 128, 4, folder + "/TerrainHoles.png", out bounds); image = LoadMask(mask);
            Check(DistanceAt(image, bounds, new Vector2(2, 0)) > 0.02f, "Terrain holes do not become land");
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(terrain);
            var triangle = new Mesh { vertices = new[] { new Vector3(-2, 0, -2), new Vector3(2, 0, -2), new Vector3(-2, 0, 2) }, triangles = new[] { 0, 2, 1 } };
            triangle.RecalculateBounds(); surface.GetComponent<MeshFilter>().sharedMesh = triangle;
            mask = MoebiusWaterSetup.BakeAutomatically(renderer, 128, 4, folder + "/Triangle.png", out bounds); image = LoadMask(mask);
            Check(DistanceAt(image, bounds, new Vector2(1, 1)) > 0.99f && DistanceAt(image, bounds, new Vector2(-1, -1)) > 0.99f, "Nonrectangular water edges also stay foam-free without actual land");
            UnityEngine.Object.DestroyImmediate(image);
            // A real bank just outside the water must still generate foam at the edge.
            surface.GetComponent<MeshFilter>().sharedMesh = MoebiusWaterSetup.CreateGrid(18, 14, 16);
            var bank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bank.transform.position = new Vector3(-9.5f, 0.5f, 0);
            mask = MoebiusWaterSetup.BakeAutomatically(renderer, 128, 4, folder + "/OutsideBank.png", out bounds); image = LoadMask(mask);
            Check(DistanceAt(image, bounds, new Vector2(-8.95f, 0)) < 0.08f && DistanceAt(image, bounds, new Vector2(8.95f, 0)) > 0.99f, "Real bank outside the mesh creates shoreline foam while the opposite open edge stays clean");
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(bank);
        }
        static Texture2D LoadMask(Texture2D asset)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            image.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(asset))); return image;
        }
        static float DistanceAt(Texture2D image, Bounds bounds, Vector2 world)
            => image.GetPixelBilinear((world.x - bounds.min.x) / bounds.size.x, (world.y - bounds.min.z) / bounds.size.z).r;
        static void RenderLiveFrame()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - liveLast < 1.0 / 60) return;
            try
            {
                camera.Render();
                liveTimes.Add(EditorApplication.timeSinceStartup - liveStart);
                liveLast = now;
                if (now - liveStart < 2 || liveTimes.Count < 60) return;
                Read(liveTarget, "LiveWaterEnd");
                var gaps = liveTimes.Skip(1).Select((time, i) => time - liveTimes[i]).ToArray();
                checks.Add("Actual isolated Editor Game camera cadence: " + (1 / gaps.Average()).ToString("F1") + " renders/s; longest gap " + (gaps.Max() * 1000).ToString("F1") + " ms.");
                checks.Add("This is measured in the isolated project, not the user's active scene. The Water Setup window now has its own live cadence monitor.");
                File.WriteAllLines(Results + "/LiveWaterFrames.csv", new[] { "frame,renderedAtSeconds" }.Concat(liveTimes.Select((time, i) => i + "," + time.ToString("F6", System.Globalization.CultureInfo.InvariantCulture))));
                Check(errors.Count == 0, "Actual realtime water renders complete without errors");
                FinishAutomatic(0);
            }
            catch (Exception exception) { errors.Add(exception.ToString()); FinishAutomatic(1); }
        }
        static void FinishAutomatic(int exit)
        {
            EditorApplication.update -= RenderLiveFrame;
            if (camera) camera.targetTexture = null;
            if (liveTarget) { liveTarget.Release(); UnityEngine.Object.DestroyImmediate(liveTarget); }
            File.WriteAllLines(Results + "/AutomaticWaterReport.txt", checks.Concat(new[] { "Errors: " + errors.Count }).Concat(errors));
            EditorApplication.Exit(exit);
        }
        static void Check(bool condition, string message)
        {
            checks.Add((condition ? "PASS: " : "FAIL: ") + message);
            if (!condition) throw new Exception(message);
        }
        public static void RunMotionPreviewBatch()
        {
            int exit = 0;
            Directory.CreateDirectory(Results);
            try
            {
                Check(Application.isBatchMode && File.Exists("WaterValidation.marker"), "Isolated motion preview of the delivered scene");
                ShaderUtil.allowAsyncCompilation = false;
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                EditorSceneManager.OpenScene(Root + "/MoebiusWaterDemo.unity");
                water = GameObject.Find("Moebius Water").GetComponent<MeshRenderer>();
                material = new Material(water.sharedMaterial);
                water.sharedMaterial = material; // Preview-only clone; never save the source scene/material.
                camera = Camera.main;
                camera.transform.position = new Vector3(8, 5.5f, -9); camera.transform.LookAt(new Vector3(-0.2f, 0, -0.3f));
                camera.orthographicSize = 5.5f;
                material.SetFloat("_AnimationPhase", 0);
                var initial = Capture("MotionAfter0", 800, 550);
                material.SetFloat("_AnimationPhase", 0.5f);
                int currentChange = Difference(initial, Capture("MotionAfterHalfSecond", 800, 550));
                material.SetFloat("_AnimationPhase", 2);
                Capture("MotionAfterTwoSeconds", 800, 550);
                checks.Add("Current speeds: Flow 0.5 / Ripple 0.12 / Wave 1.4 / Foam 0.28; wave height remains 0.055m");
                Check(currentChange > 500, "Motion changes " + currentChange + " pixels over a 0.5-second animation phase interval");
                material.SetFloat("_FlowSpeed", 0.12f); material.SetFloat("_RippleSpeed", 0.025f);
                material.SetFloat("_WaveSpeed", 0.65f); material.SetFloat("_FoamSpeed", 0.09f);
                material.SetFloat("_AnimationPhase", 0);
                var previous = Capture("MotionBefore0", 800, 550);
                material.SetFloat("_AnimationPhase", 0.5f);
                int previousChange = Difference(previous, Capture("MotionBeforeHalfSecond", 800, 550));
                checks.Add("Previous speeds change " + previousChange + " pixels over the same phase interval (image difference, not a frame-rate measurement).");
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors.Add(message.message);
                Check(errors.Count == 0, "No water shader compile errors");
                UnityEngine.Object.DestroyImmediate(material);
            }
            catch (Exception exception) { errors.Add(exception.ToString()); exit = 1; }
            File.WriteAllLines(Results + "/MotionReport.txt", checks.Concat(new[] { "Errors: " + errors.Count }).Concat(errors));
            EditorApplication.Exit(exit);
        }
        public static void RunSmoothPreviewBatch()
        {
            int exit = 0;
            Directory.CreateDirectory(Results);
            Application.logMessageReceived += (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
            try
            {
                Check(Application.isBatchMode && File.Exists("WaterValidation.marker"), "Isolated continuous-motion sampling of the delivered scene");
                ShaderUtil.allowAsyncCompilation = false;
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                EditorSceneManager.OpenScene(Root + "/MoebiusWaterDemo.unity");
                water = GameObject.Find("Moebius Water").GetComponent<MeshRenderer>();
                material = new Material(water.sharedMaterial); water.sharedMaterial = material;
                var saved = new Material(material);
                camera = Camera.main;
                camera.transform.position = new Vector3(8, 5.5f, -9); camera.transform.LookAt(new Vector3(-0.2f, 0, -0.3f)); camera.orthographicSize = 5.5f;
                Capture("SmoothCloseup", 1600, 1100);
                // Hold geometry and shoreline still so only ink ripple motion can change the image.
                material.SetFloat("_EnableWaves", 0); material.SetFloat("_EnableFoam", 0); material.SetFloat("_EnableVariation", 0);
                material.SetFloat("_AnimationPhase", 0);
                var previous = Capture("SmoothRipples0", 800, 550);
                var deltas = new List<int>();
                for (int frame = 1; frame <= 60; frame++)
                {
                    material.SetFloat("_AnimationPhase", frame / 60f);
                    var next = Capture(frame == 60 ? "SmoothRipples1Second" : null, 800, 550);
                    deltas.Add(Difference(previous, next)); previous = next;
                }
                Check(deltas.All(d => d > 0), "All 60 consecutive 1/60-second ripple samples change: no held animation frames");
                checks.Add("Changed pixels per sample: min " + deltas.Min() + ", average " + deltas.Average().ToString("F1") + ", max " + deltas.Max());
                checks.Add("This samples shader animation phases, not the user's real-time Editor frame rate.");
                File.WriteAllLines(Results + "/SmoothMotionSamples.csv", new[] { "frame,phaseSeconds,changedPixels" }.Concat(deltas.Select((delta, i) => (i + 1) + "," + ((i + 1) / 60f).ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + "," + delta)));
                material.CopyPropertiesFromMaterial(saved);
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors.Add(message.message);
                Check(errors.Count == 0, "No shader compile or runtime errors");
                UnityEngine.Object.DestroyImmediate(saved); UnityEngine.Object.DestroyImmediate(material);
            }
            catch (Exception exception) { errors.Add(exception.ToString()); exit = 1; }
            File.WriteAllLines(Results + "/SmoothMotionReport.txt", checks.Concat(new[] { "Errors: " + errors.Count }).Concat(errors));
            EditorApplication.Exit(exit);
        }
        public static void RunBatch()
        {
            int exit = 0;
            Application.logMessageReceived += (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
            Directory.CreateDirectory(Results);
            try
            {
                Check(Application.isBatchMode && File.Exists("WaterValidation.marker"), "Isolated validation project");
                ShaderUtil.allowAsyncCompilation = false;
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                BuildDemo();
                Capture("Overview", 1600, 1100);
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    Check(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, "Water shader compile: " + message.message);
                camera.transform.position = new Vector3(8, 5.5f, -9); camera.transform.LookAt(new Vector3(-0.2f, 0, -0.3f));
                camera.orthographicSize = 5.5f;
                Capture("Closeup", 1600, 1100);
                TestFeatures();
                TestIndependentControls();
                TestDistanceField();
                var compatibility = typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityCode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (compatibility != null)
                    Check((int)compatibility.Invoke(null, new object[] { material.shader, 0 }) == 0, "Unity reports SRP Batcher compatible");
                foreach (var shader in new[] { material.shader, Shader.Find("Darwin/Moebius NPR"), Shader.Find("Hidden/Darwin/Moebius Outline") })
                    foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors.Add(message.message);
                Check(errors.Count == 0, "No shader compiler or runtime errors");
                var paths = AssetDatabase.GetDependencies(Root + "/MoebiusWaterDemo.unity", true)
                    .Concat(AssetDatabase.FindAssets("", new[] { Root, "Assets/Rendering/Moebius/Defaults/Materials" }).Select(AssetDatabase.GUIDToAssetPath))
                    .Where(p => p.StartsWith(Root + "/", StringComparison.Ordinal) || p == "Assets/Rendering/Moebius/Defaults/Materials/MoebiusWater.mat")
                    .Where(p => File.Exists(p)).Distinct().OrderBy(p => p);
                File.WriteAllLines(Results + "/Delivery.txt", paths);
            }
            catch (Exception exception) { errors.Add(exception.ToString()); exit = 1; }
            File.WriteAllLines(Results + "/Report.txt", checks.Concat(new[] { "Shader/runtime/test errors: " + errors.Count }).Concat(errors));
            EditorApplication.Exit(exit);
        }

        static void BuildDemo()
        {
            Directory.CreateDirectory(Root + "/Meshes"); Directory.CreateDirectory(Root + "/Materials"); Directory.CreateDirectory(Root + "/Textures");
            Directory.CreateDirectory("Assets/Rendering/Moebius/Defaults/Materials"); AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.gray; RenderSettings.fog = false;
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1;
            sun.transform.rotation = Quaternion.Euler(52, -35, 0); sun.shadows = LightShadows.Soft; RenderSettings.sun = sun;
            material = new Material(Shader.Find("Darwin/Moebius Water"));
            AssetDatabase.CreateAsset(new Material(material) { name = "MoebiusWater" }, "Assets/Rendering/Moebius/Defaults/Materials/MoebiusWater.mat");
            material.name = "Coastal Water"; AssetDatabase.CreateAsset(material, Root + "/Materials/CoastalWater.mat");
            var grid = MoebiusWaterSetup.CreateGrid(18, 14, 160); AssetDatabase.CreateAsset(grid, Root + "/Meshes/WaterGrid.asset");
            var surface = MeshObject("Moebius Water", grid, material); water = surface.GetComponent<MeshRenderer>();
            water.shadowCastingMode = ShadowCastingMode.Off; water.receiveShadows = true;
            var shoreMaterial = SurfaceMaterial("Sand", new Color(0.89f, 0.82f, 0.65f), false);
            var rockMaterial = SurfaceMaterial("Peach Stone", new Color(0.82f, 0.67f, 0.59f), true);
            var coolRock = SurfaceMaterial("Lavender Stone", new Color(0.68f, 0.67f, 0.79f), true);
            var shore = CreateShore(); AssetDatabase.CreateAsset(shore, Root + "/Meshes/Shore.asset");
            var land = MeshObject("Curved Sand Shore", shore, shoreMaterial);
            var sources = new List<MeshFilter> { land.GetComponent<MeshFilter>() };
            var rockMesh = CreateRock(); AssetDatabase.CreateAsset(rockMesh, Root + "/Meshes/Rock.asset");
            foreach (var layout in new[] {
                new Vector4(2.4f,-0.7f,1.1f,1.0f), new Vector4(1.4f,-1.8f,0.7f,0.6f),
                new Vector4(-3.8f,3.8f,1.3f,2.0f), new Vector4(-5.5f,4.2f,1.8f,3.2f),
                new Vector4(-6.5f,1.4f,1.3f,2.1f), new Vector4(4.2f,4.0f,1.1f,1.3f) })
            {
                var rock = MeshObject("Shore Rock", rockMesh, layout.y > 2 ? coolRock : rockMaterial);
                rock.transform.position = new Vector3(layout.x, -0.25f, layout.y);
                rock.transform.localScale = new Vector3(layout.z, layout.w, layout.z * 0.75f);
                sources.Add(rock.GetComponent<MeshFilter>());
            }
            var mask = MoebiusWaterSetup.Bake(sources, water.bounds, 0, 512, 4, Root + "/Textures/ShoreDistance.png");
            MoebiusWaterSetup.AssignShoreline(material, mask, water.bounds, 4);
            Check(mask && material.GetFloat("_EnableShoreMask") == 1, "Actual static shoreline mask baked and assigned");
            camera = new GameObject("Water Demo Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.76f, 0.86f, 0.84f);
            camera.orthographic = true; camera.orthographicSize = 9.5f; camera.nearClipPlane = 0.1f; camera.farClipPlane = 80;
            camera.transform.position = new Vector3(12, 13, -18); camera.transform.LookAt(new Vector3(0, 0.3f, 0));
            camera.GetUniversalAdditionalCameraData().SetRenderer(1);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(surface.scene, Root + "/MoebiusWaterDemo.unity");
            PrefabUtility.SaveAsPrefabAsset(surface, Root + "/MoebiusWaterSurface.prefab");
            Check(grid.vertexCount == 25921 && grid.bounds.size.y >= 1, "Subdivided mesh with persistent wave-safe bounds");
            Check(surface.GetComponents<MonoBehaviour>().Length == 0, "Animation requires no runtime MonoBehaviour");
        }
        static Material SurfaceMaterial(string name, Color color, bool hatch)
        {
            var result = new Material(Shader.Find("Darwin/Moebius NPR")) { name = name };
            result.SetColor("_BaseColor", color); result.SetFloat("_HighlightStrength", 0); result.SetFloat("_PaperLift", 0.04f);
            result.SetFloat("_CastShadowHatchStrength", 0.3f); result.SetFloat("_CastShadowTintStrength", 0.05f);
            if (hatch) { result.EnableKeyword("_USE_HATCHING"); result.EnableKeyword("_USE_TRIPLANAR"); result.SetFloat("_UseHatching", 1); result.SetFloat("_UseTriplanar", 1); result.SetFloat("_HatchScale", 12); result.SetFloat("_HatchStrength", 0.5f); }
            AssetDatabase.CreateAsset(result, Root + "/Materials/" + name.Replace(" ", "") + ".mat"); return result;
        }
        static GameObject MeshObject(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat; return go;
        }
        static Mesh CreateShore()
        {
            const int rows = 81, columns = 21;
            var vertices = new Vector3[rows * columns]; var uv = new Vector2[vertices.Length]; var triangles = new int[(rows - 1) * (columns - 1) * 6];
            for (int z = 0; z < rows; z++) for (int x = 0; x < columns; x++)
            {
                float zz = -7 + 14f * z / (rows - 1), t = (float)x / (columns - 1);
                float edge = -2.9f + Mathf.Sin(zz * 0.7f) * 0.65f + Mathf.Sin(zz * 1.4f) * 0.18f;
                vertices[z * columns + x] = new Vector3(Mathf.Lerp(-9, edge, t), 0.03f + (1 - t) * 0.55f, zz);
                uv[z * columns + x] = new Vector2(t, (float)z / (rows - 1));
            }
            int index = 0;
            for (int z = 0; z < rows - 1; z++) for (int x = 0; x < columns - 1; x++)
            { int a = z * columns + x; triangles[index++] = a; triangles[index++] = a + columns; triangles[index++] = a + 1; triangles[index++] = a + 1; triangles[index++] = a + columns; triangles[index++] = a + columns + 1; }
            var mesh = new Mesh { name = "Curved Shore" }; mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        static Mesh CreateRock()
        {
            const int sides = 9, rings = 5;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int ring = 0; ring < rings; ring++) for (int side = 0; side < sides; side++)
            {
                float angle = side * Mathf.PI * 2 / sides;
                float radius = (ring == 0 ? 0.8f : ring == 1 ? 1f : ring == 2 ? 0.87f : ring == 3 ? 0.58f : 0.18f) * (1 + 0.1f * Mathf.Sin(side * 4.7f + ring));
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, ring * 0.43f, Mathf.Sin(angle) * radius));
            }
            for (int ring = 0; ring < rings - 1; ring++) for (int side = 0; side < sides; side++)
            {
                int a = ring * sides + side, b = ring * sides + (side + 1) % sides, c = a + sides, d = b + sides;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            vertices.Add(new Vector3(0, 1.8f, 0));
            for (int side = 0; side < sides; side++) triangles.AddRange(new[] { vertices.Count - 1, (rings - 1) * sides + (side + 1) % sides, (rings - 1) * sides + side });
            // Flat faces give existing outline/hatching something to describe.
            var flat = triangles.Select(i => vertices[i]).ToArray();
            var mesh = new Mesh { name = "Low Poly Shore Rock" }; mesh.vertices = flat; mesh.triangles = Enumerable.Range(0, flat.Length).ToArray();
            mesh.uv = flat.Select(v => new Vector2(v.x, v.z) * 0.25f + Vector2.one * 0.5f).ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        static void TestFeatures()
        {
            var saved = new Material(material);
            material.SetFloat("_AnimationPhase", 0); var first = Capture("Time0", 800, 550);
            material.SetFloat("_AnimationPhase", 3); var second = Capture("Time3", 800, 550);
            Check(Difference(first, second) > 800, "Combined flow, waves and foam change with animation time");
            // Isolate visible motion: no vertex motion and no other animated feature can produce the difference.
            material.SetFloat("_EnableWaves", 0); material.SetFloat("_EnableFoam", 0); material.SetFloat("_RippleSpeed", 0);
            material.SetFloat("_AnimationPhase", 0); first = Capture("Flow0", 800, 550);
            material.SetFloat("_AnimationPhase", 4); second = Capture("Flow4", 800, 550);
            Check(Difference(first, second) > 250, "Flow actually moves visible ink strokes on an opaque single-color surface");
            material.CopyPropertiesFromMaterial(saved); material.SetFloat("_EnableWaves", 0); material.SetFloat("_EnableRipples", 0);
            material.SetFloat("_AnimationPhase", 0); first = Capture("Foam0", 800, 550);
            material.SetFloat("_AnimationPhase", 5); second = Capture("Foam5", 800, 550);
            Check(Difference(first, second) > 150, "Foam changes independently at the shoreline");
            material.CopyPropertiesFromMaterial(saved);
            foreach (var name in new[] { "Flow", "WaveHeight", "ShoreDistance", "Foam", "Normals" })
            { material.SetFloat("_DebugView", Array.IndexOf(new[] { "Flow", "WaveHeight", "ShoreDistance", "Foam", "Normals" }, name) + 1); Capture("Debug" + name, 800, 550); }
            material.CopyPropertiesFromMaterial(saved);
            // Direct GPU pass probes hold _Time fixed in a command buffer and use actual displaced geometry.
            foreach (string pass in new[] { "WaterForward", "DepthOnly", "DepthNormals", "ShadowCaster" })
            {
                material.SetFloat("_EnableWaves", 0); var flat = CapturePass(pass, pass + "Flat");
                material.SetFloat("_EnableWaves", 1); material.SetFloat("_WaveHeight", 0.3f);
                var waved = CapturePass(pass, pass + "Waves");
                Check(Difference(flat, waved) > 20, pass + ": actual GPU output changes with vertex displacement");
            }
            material.EnableKeyword("_GBUFFER_NORMALS_OCT"); CapturePass("DepthNormals", "DepthNormalsOct"); material.DisableKeyword("_GBUFFER_NORMALS_OCT");
            material.EnableKeyword("_CASTING_PUNCTUAL_LIGHT_SHADOW"); CapturePass("ShadowCaster", "PunctualShadow"); material.DisableKeyword("_CASTING_PUNCTUAL_LIGHT_SHADOW");
            material.CopyPropertiesFromMaterial(saved);
            material.SetFloat("_EnableWaves", 0); material.SetFloat("_EnableRipples", 0); material.SetFloat("_EnableFoam", 0); material.SetFloat("_EnableVariation", 0);
            first = Capture("AllExtrasOff", 800, 550);
            material.SetFloat("_AnimationPhase", 10); second = Capture("AllExtrasOffLater", 800, 550);
            Check(Difference(first, second) == 0, "All optional effects disabled gives a stable flat base color");
            material.CopyPropertiesFromMaterial(saved); UnityEngine.Object.DestroyImmediate(saved);
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(Root + "/MoebiusWaterDemo.unity");
            water = GameObject.Find("Moebius Water").GetComponent<MeshRenderer>(); material = water.sharedMaterial;
            Check(material.GetFloat("_WaveHeight") == 0.055f && material.GetFloat("_DebugView") == 0 && material.GetFloat("_AnimationPhase") == 0, "Saved scene/material reload with production defaults");
            Check(water.GetComponent<MeshFilter>().sharedMesh.bounds.size.y >= 1, "Wave culling bounds survive reload");
        }
        static void TestDistanceField()
        {
            const int size = 9; var land = new bool[size * size]; land[4 * size + 4] = true;
            var field = MoebiusWaterSetup.DistanceField(land, size, 0.4f, 0.7f);
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                float expected = Mathf.Max(0, Mathf.Sqrt(Mathf.Pow((x - 4) * 0.4f, 2) + Mathf.Pow((z - 4) * 0.7f, 2)) - 0.2f);
                if (Mathf.Abs(field[z * size + x] - expected) > 0.0001f) throw new Exception("Distance field error at " + x + "," + z);
            }
            Check(true, "Distance bake matches Euclidean distances with rectangular world pixels");
        }
        static void TestIndependentControls()
        {
            var saved = new Material(material);
            foreach (var pair in new[] {
                new[] { "_EnableFlow", "_FlowStrength" }, new[] { "_EnableWaves", "_WaveHeight" },
                new[] { "_EnableRipples", "_RippleStrength" }, new[] { "_EnableFoam", "_FoamIntensity" },
                new[] { "_EnableVariation", "_ColorVariation" }, new[] { "_EnableFoamInk", "_FoamInkStrength" },
                new[] { "_EnableFoamPatches", "_FoamPatchStrength" }, new[] { "_EnableShoreMotion", "_ShoreMotionStrength" } })
            {
                material.CopyPropertiesFromMaterial(saved); material.SetFloat(pair[0], 0);
                var disabled = CapturePass("WaterForward", pair[0] + "Off");
                material.CopyPropertiesFromMaterial(saved); material.SetFloat(pair[0], 1); material.SetFloat(pair[1], 0);
                var zero = CapturePass("WaterForward", pair[1] + "Zero");
                Check(Difference(disabled, zero) == 0, pair[0] + ": disabled and zero strength have identical GPU output");
            }
            material.CopyPropertiesFromMaterial(saved);
            var normals = CapturePass("DepthNormals", "NormalsBeforeColorControls");
            material.SetFloat("_EnableFlow", 0); material.SetFloat("_EnableRipples", 0); material.SetFloat("_EnableFoam", 0); material.SetFloat("_EnableVariation", 1);
            Check(Difference(normals, CapturePass("DepthNormals", "NormalsAfterColorControls")) == 0, "Color effects do not alter depth-normal geometry or outline data");
            material.CopyPropertiesFromMaterial(saved); UnityEngine.Object.DestroyImmediate(saved); AssetDatabase.SaveAssets();
        }
        static int Difference(Color32[] a, Color32[] b)
        { int count = 0; for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 5) count++; return count; }
        static Color32[] Capture(string name, int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
            camera.Render(); camera.Render(); var pixels = Read(target, name); camera.targetTexture = null;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); return pixels;
        }
        static Color32[] CapturePass(string passName, string name)
        {
            int pass = material.FindPass(passName); Check(pass >= 0, passName + " present");
            var target = new RenderTexture(400, 300, 24, RenderTextureFormat.ARGB32); target.Create();
            var commands = new CommandBuffer(); commands.SetRenderTarget(target); commands.ClearRenderTarget(true, true, Color.black);
            var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(new Vector3(0, 3, -10), Quaternion.Euler(18, 0, 0), Vector3.one).inverse;
            commands.SetViewProjectionMatrices(view, GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-10, 10, -4, 4, 0.1f, 40), true));
            commands.SetGlobalVector("_Time", new Vector4(0.1f, 2, 4, 6));
            commands.SetGlobalVector("_LightDirection", new Vector4(0, 1, -1, 0)); commands.SetGlobalVector("_LightPosition", new Vector4(0, 5, -3, 1));
            commands.SetGlobalVector("_ShadowBias", Vector4.zero);
            commands.DrawMesh(water.GetComponent<MeshFilter>().sharedMesh, water.transform.localToWorldMatrix, material, 0, pass);
            Graphics.ExecuteCommandBuffer(commands); commands.Release();
            Color32[] pixels;
            if (passName == "ShadowCaster")
            {
                var output = new RenderTexture(400, 300, 0, RenderTextureFormat.ARGB32); output.Create();
                var probe = new Material(Shader.Find("Hidden/Darwin/Water Validation Depth"));
                probe.SetTexture("_ProbeDepth", target, RenderTextureSubElement.Depth);
                Graphics.Blit(null, output, probe); pixels = Read(output, name);
                output.Release(); UnityEngine.Object.DestroyImmediate(output); UnityEngine.Object.DestroyImmediate(probe);
            }
            else pixels = Read(target, name);
            target.Release(); UnityEngine.Object.DestroyImmediate(target); return pixels;
        }
        static Color32[] Read(RenderTexture target, string name)
        {
            var previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            var pixels = image.GetPixels32();
            if (!string.IsNullOrEmpty(name)) File.WriteAllBytes(Results + "/" + name + ".png", image.EncodeToPNG());
            RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); return pixels;
        }
    }
}
