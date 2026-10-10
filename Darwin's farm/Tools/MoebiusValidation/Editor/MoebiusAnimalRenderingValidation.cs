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
    public static class MoebiusAnimalRenderingValidation
    {
        const string Root = "Assets/Rendering/Moebius/Samples/Billboard/Animals";
        const string Results = "Validation/AnimalRendering";
        static readonly string[] Names = { "MushroomFox", "Wolf", "PeacockSnakeLion", "OceanDeer", "ForestDeer" };
        static readonly List<string> Checks = new List<string>();
        static readonly List<string> Errors = new List<string>();

        static void Check(bool condition, string message)
        {
            Checks.Add((condition ? "PASS: " : "FAIL: ") + message);
            if (!condition) throw new Exception(message);
        }

        public static void RunBatch()
        {
            int exit = 0;
            Application.logMessageReceived += (message, stack, type) => {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(message);
            };
            try
            {
                Check(Application.isBatchMode && File.Exists("AnimalRendering.marker"), "Run in an isolated validation project");
                Directory.CreateDirectory(Results);
                ShaderUtil.allowAsyncCompilation = false;
                MoebiusBillboardCameraGlobals.Initialize();
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
                var shader = Shader.Find("Darwin/Moebius Billboard Character");
                Check(shader && shader.isSupported, "Billboard shader imports");
                var probe = new Material(shader);
                Check(probe.FindPass("ShadowCaster") >= 0, "Billboard has an alpha-tested ShadowCaster pass");
                UnityEngine.Object.DestroyImmediate(probe);
                TestShadowsAndStyle();
                TestDeliveredScene();
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) Errors.Add(message.message);
                Check(Errors.Count == 0, "No shader or runtime errors");
            }
            catch (Exception exception) { Errors.Add(exception.ToString()); exit = 1; }
            Directory.CreateDirectory(Results);
            File.WriteAllLines(Results + "/Report.txt", Checks.Concat(new[] {"Errors: " + Errors.Count}).Concat(Errors));
            EditorApplication.Exit(exit);
        }

        static void TestShadowsAndStyle()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.gray * 0.35f;
            var light = new GameObject("Shadow test sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1;
            light.shadows = LightShadows.Soft;
            light.shadowBias = 0.03f;
            light.shadowNormalBias = 0.15f;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            RenderSettings.sun = light;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            var receiver = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            receiver.SetColor("_BaseColor", new Color(0.85f, 0.8f, 0.7f));
            ground.GetComponent<Renderer>().sharedMaterial = receiver;
            ground.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var camera = new GameObject("Validation camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.76f, 0.82f, 0.85f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 40;
            camera.orthographic = true;
            camera.orthographicSize = 3.8f;
            camera.transform.position = new Vector3(4, 5, -8);
            camera.transform.LookAt(new Vector3(0, 0.7f, 0));
            camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            camera.GetUniversalAdditionalCameraData().renderShadows = true;
            foreach (string name in Names)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + name + ".prefab");
                var animal = UnityEngine.Object.Instantiate(prefab);
                animal.transform.position = new Vector3(0, 0.015f, 0);
                var renderer = animal.GetComponent<MeshRenderer>();
                Check(renderer.shadowCastingMode == ShadowCastingMode.On && renderer.receiveShadows, name + ": prefab casts and receives shadows");
                var material = new Material(renderer.sharedMaterial);
                renderer.sharedMaterial = material;
                float style = material.GetFloat("_MoebiusStyleStrength");
                Check(style > 0, name + ": style enabled on delivered material");
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                var shadowed = Capture(camera, name + "_SilhouetteShadow");
                // Hide the illustration in both captures so only the actual received shadow differs.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.enabled = false;
                var empty = Capture(camera, name + "_EmptyGround");
                renderer.enabled = true;
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                int darker = DarkenedPixels(empty, shadowed);
                Check(darker > 80, name + ": actual ground shadow darkens " + darker + " pixels");
                material.SetFloat("_Cutoff", 1.01f);
                var clipped = Capture(camera, name + "_ClippedGround");
                int clippedPixels = DarkenedPixels(empty, clipped);
                Check(clippedPixels < 10, name + ": discarded alpha leaves no ground shadow (" + clippedPixels + " darkened pixels)");
                material.SetFloat("_Cutoff", 0.3f);
                renderer.shadowCastingMode = ShadowCastingMode.On;
                material.SetFloat("_MoebiusStyleStrength", 0);
                var original = Capture(camera, name + "_Original");
                material.SetFloat("_MoebiusStyleStrength", style);
                var styled = Capture(camera, name + "_Moebius");
                Check(DifferentPixels(original, styled) > 150, name + ": filter changes illustration pixels");
                if (name == "Wolf")
                {
                    foreach (bool orthographic in new[] {true, false})
                    {
                        camera.orthographic = orthographic;
                        camera.transform.position = new Vector3(-6, 4.5f, -7);
                        camera.transform.LookAt(new Vector3(0, 0.8f, 0));
                        renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                        var rotatedShadow = Capture(camera, "Wolf_" + (orthographic ? "Orthographic" : "Perspective") + "_RotatedShadow");
                        renderer.enabled = false;
                        var rotatedEmpty = Capture(camera, null);
                        renderer.enabled = true;
                        Check(DarkenedPixels(rotatedEmpty, rotatedShadow) > 50, "Camera orbit: shadow persists for " + (orthographic ? "orthographic" : "perspective") + " camera");
                        Vector4 forward = Shader.GetGlobalVector("_MoebiusBillboardCameraForward");
                        Check(Vector3.Distance(new Vector3(forward.x, forward.y, forward.z), camera.transform.forward) < 0.001f, "Shadow and forward passes share the rendering camera direction");
                    }
                    camera.orthographic = true;
                    camera.transform.position = new Vector3(4, 5, -8);
                    camera.transform.LookAt(new Vector3(0, 0.7f, 0));
                }
                UnityEngine.Object.DestroyImmediate(animal);
                UnityEngine.Object.DestroyImmediate(material);
            }
            UnityEngine.Object.DestroyImmediate(receiver);
        }

        static void TestDeliveredScene()
        {
            EditorSceneManager.OpenScene("Assets/Rendering/Moebius/Samples/Biomes/Scenes/Highland_Animals.unity");
            var animals = GameObject.Find("2D Billboard Animals").GetComponentsInChildren<MeshRenderer>();
            Check(animals.Length == 5, "Delivered Highland scene retains five animals");
            Check(animals.All(r => r.shadowCastingMode == ShadowCastingMode.On), "All five scene animals cast shadows");
            Check(animals.All(r => r.sharedMaterial.GetFloat("_MoebiusStyleStrength") > 0), "All five scene animals use the filter");
            var withAnimalShadows = Capture(Camera.main, "HighlandAfter", 1600, 1050);
            foreach (var animal in animals) animal.shadowCastingMode = ShadowCastingMode.Off;
            var withoutAnimalShadows = Capture(Camera.main, "HighlandStyleWithoutShadows", 1600, 1050);
            int groundShadowPixels = DarkenedPixels(withoutAnimalShadows, withAnimalShadows);
            Check(groundShadowPixels > 60, "Actual Highland scene: animal shadows darken " + groundShadowPixels + " pixels on existing terrain");
            foreach (var animal in animals)
            {
                animal.shadowCastingMode = ShadowCastingMode.Off;
                var material = new Material(animal.sharedMaterial);
                material.SetFloat("_MoebiusStyleStrength", 0);
                animal.sharedMaterial = material;
            }
            Capture(Camera.main, "HighlandBefore", 1600, 1050);
            // No scene, prefab, original image or material is saved by this validator.
        }

        static Color32[] Capture(Camera camera, string name, int width = 800, int height = 600)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            var previous = RenderTexture.active;
            var oldTarget = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            if (name != null) File.WriteAllBytes(Results + "/" + name + ".png", image.EncodeToPNG());
            var pixels = image.GetPixels32();
            RenderTexture.active = previous;
            camera.targetTexture = oldTarget;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
            return pixels;
        }

        static int DifferentPixels(Color32[] a, Color32[] b)
        {
            return a.Zip(b, (x, y) => Math.Abs(x.r-y.r)+Math.Abs(x.g-y.g)+Math.Abs(x.b-y.b) > 8).Count(v => v);
        }

        static int DarkenedPixels(Color32[] a, Color32[] b)
        {
            return a.Zip(b, (x, y) => (int)x.r+x.g+x.b-((int)y.r+y.g+y.b) > 18).Count(v => v);
        }
    }
}
