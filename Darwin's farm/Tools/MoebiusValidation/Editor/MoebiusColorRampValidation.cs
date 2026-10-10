using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Darwin.Rendering.Editor
{
    // Isolated CLI validation only. Clones one existing material; never saves scenes or materials.
    public static class MoebiusColorRampValidation
    {
        static readonly List<string> Checks = new List<string>();
        static readonly List<string> Errors = new List<string>();
        static string folder;
        public static void RunBatch()
        {
            Application.logMessageReceived += (message, stack, type) =>
            { if(type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(message); };
            try
            {
                if(!Application.isBatchMode || !File.Exists("ColorRampValidation.marker")) throw new Exception("Use isolated validation project.");
                string label = File.ReadAllText("ColorRampValidation.marker").Trim();
                folder = "Validation/ColorRamp/" + label; Directory.CreateDirectory(folder);
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
                ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("Validation camera").AddComponent<Camera>();
                var cameraData = camera.GetUniversalAdditionalCameraData(); cameraData.SetRenderer(1);
                camera.transform.position = new Vector3(0, 1, -8); camera.transform.LookAt(Vector3.zero);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.45f,.77f,.8f);
                var light = new GameObject("Validation light").AddComponent<Light>(); light.type = LightType.Directional;
                light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(35,-30,0);
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.transform.localScale = Vector3.one * 3;
                var material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Samples/Shared/Materials/Ivory.mat"));
                sphere.GetComponent<Renderer>().sharedMaterial = material;
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.position = new Vector3(0,-1.55f,0);
                ground.transform.localScale = Vector3.one * .7f; ground.GetComponent<Renderer>().sharedMaterial = material;
                var originalColor = material.GetColor("_BaseColor");
                var baseline = Capture(camera,"Default");
                if(label == "After")
                {
                    CompareBefore("Default", baseline);
                    Check(material.GetFloat("_EnableColorRamp") == 0,"Existing material defaults to ramp Off");
                    ConfigureRamp(material);
                    material.SetFloat("_EnableColorRamp",0);
                    CompareBefore("Default",Capture(camera,"OffConfigured"));
                    material.SetFloat("_EnableColorRamp",1); material.SetFloat("_RampStrength",0);
                    CompareBefore("Default",Capture(camera,"StrengthZero"));
                    material.SetFloat("_RampStrength",.4f); var enabled = Capture(camera,"Enabled");
                    Check(Differences(baseline,enabled) > 100,"Enabled ramp changes visible colors");
                    light.transform.rotation = Quaternion.Euler(35,55,0);
                    Check(Differences(enabled,Capture(camera,"LightRotated")) > 100,"Ramp responds to main-light rotation");
                    light.transform.rotation = Quaternion.Euler(35,-30,0);
                    // Flat cube faces visibly change NdotL when rotated; same cloned material.
                    sphere.SetActive(false); var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.transform.localScale = Vector3.one * 2.3f; cube.GetComponent<Renderer>().sharedMaterial = material;
                    var cubeBefore = Capture(camera,"Cube"); cube.transform.rotation = Quaternion.Euler(15,45,0);
                    Check(Differences(cubeBefore,Capture(camera,"ObjectRotated")) > 100,"Rotated object renders with ramp");
                    cube.SetActive(false); sphere.SetActive(true);
                    material.SetFloat("_EnableColorRamp",0);
                }
                // Existing debug channels expose masks independently of ramp tinting.
                foreach(int mode in new[]{2,3,4,5,9,10,11,12})
                {
                    material.SetFloat("_DebugMode",mode);
                    var original = Capture(camera,"Debug"+mode+"Off");
                    if(label == "After")
                    {
                        CompareBefore("Debug"+mode+"Off",original);
                        material.SetFloat("_EnableColorRamp",1); material.SetFloat("_RampStrength",.4f);
                        Compare(original,Capture(camera,"Debug"+mode+"On"),"Ramp preserves debug mask "+mode);
                        material.SetFloat("_EnableColorRamp",0);
                    }
                }
                material.SetFloat("_DebugMode",0);
                var outline = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Samples/Shared/Materials/InkOutline.mat");
                float oldDebug = outline.GetFloat("_DebugMode"); outline.SetFloat("_DebugMode",5);
                var outlinePixels = Capture(camera,"OutlineOff");
                if(label == "After")
                {
                    CompareBefore("OutlineOff",outlinePixels); material.SetFloat("_EnableColorRamp",1);
                    Compare(outlinePixels,Capture(camera,"OutlineOn"),"Outline mask unchanged with ramp On");
                    material.SetFloat("_EnableColorRamp",0);
                }
                outline.SetFloat("_DebugMode",oldDebug);
                // Directly render the existing data passes: test-only targets, no new runtime RenderTexture/pass.
                foreach(string passName in new[]{"ShadowCaster","DepthOnly","DepthNormals"})
                {
                    var pixels = CapturePass(sphere,material,passName);
                    if(passName != "ShadowCaster")
                    {
                        int drawn=0;foreach(var pixel in pixels)if(pixel.r!=0 || pixel.g!=0 || pixel.b!=0)drawn++;
                        Check(drawn>100,passName+" test renders actual geometry: "+drawn+" nonblack pixels");
                    }
                    if(label == "After")
                    {
                        CompareBefore(passName,pixels); material.SetFloat("_EnableColorRamp",1);
                        Compare(pixels,CapturePass(sphere,material,passName+"On",passName),passName+" unaffected by ramp");
                        material.SetFloat("_EnableColorRamp",0);
                    }
                }
                Check(material.GetColor("_BaseColor") == originalColor,"Original albedo parameter unchanged");
                if(label == "After") ValidateColorEndpoints(camera,light,sphere,ground,material);
                foreach(var shader in new[]{material.shader,outline.shader})
                    foreach(var message in ShaderUtil.GetShaderMessages(shader))
                        if(message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) Errors.Add(message.message);
                UnityEngine.Object.DestroyImmediate(material);
            }
            catch(Exception exception) { Errors.Add(exception.ToString()); }
            if(folder == null) folder = "Validation/ColorRamp"; Directory.CreateDirectory(folder);
            File.WriteAllText(folder+"/Report.txt",string.Join("\n",Checks)+"\nShader/runtime/test errors: "+Errors.Count+"\n"+string.Join("\n",Errors));
            EditorApplication.Exit(Errors.Count == 0 ? 0 : 1);
        }
        static void ConfigureRamp(Material material)
        {
            material.SetFloat("_EnableColorRamp",1);
            material.SetColor("_RampShadowColor",new Color(.9f,.85f,1));
            material.SetColor("_RampLightColor",new Color(1,.94f,.87f));
            material.SetFloat("_RampShadowThreshold",.4f); material.SetFloat("_RampLightThreshold",.55f);
            material.SetFloat("_RampStrength",.4f);
        }
        static void ValidateColorEndpoints(Camera camera,Light light,GameObject sphere,GameObject ground,Material material)
        {
            // Isolate only the tint mathematically; uses the same material and shader, not a new implementation.
            sphere.SetActive(false); ground.SetActive(false); camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.localScale = Vector3.one * 3; quad.GetComponent<Renderer>().sharedMaterial = material;
            camera.transform.position = new Vector3(0,0,-5); camera.transform.rotation = Quaternion.identity;
            material.DisableKeyword("_USE_HATCHING"); material.DisableKeyword("_USE_STIPPLE");
            material.SetFloat("_UseRamp",0); material.SetFloat("_SurfaceTintStrength",0);
            material.SetFloat("_PaperLift",0); material.SetFloat("_CastShadowHatchStrength",0); material.SetFloat("_HighlightStrength",0);
            material.SetColor("_BaseColor",new Color(.6f,.7f,.8f)); material.SetFloat("_EnableColorRamp",0);
            light.transform.rotation = Quaternion.Euler(0,180,0); var original = Capture(camera,"EndpointBase");
            material.SetFloat("_EnableColorRamp",1); material.SetFloat("_RampStrength",1);
            var low = Capture(camera,"EndpointLow");
            light.transform.rotation = Quaternion.identity; var high = Capture(camera,"EndpointHigh");
            int index = 128*256+128;
            var expectedLow = ((Color)original[index]).linear;
            var expectedHigh = expectedLow;
            expectedLow *= material.GetColor("_RampShadowColor").linear;
            expectedHigh *= material.GetColor("_RampLightColor").linear;
            Check(ColorError(((Color)low[index]).linear,expectedLow) < .015f,"Low NdotL produces Shadow Ramp Color tint");
            Check(ColorError(((Color)high[index]).linear,expectedHigh) < .015f,"High NdotL produces Light Ramp Color tint");
            quad.transform.rotation = Quaternion.Euler(0,70,0);
            var rotated = Capture(camera,"EndpointObjectRotated");
            Check(ColorError(((Color)rotated[index]).linear,expectedLow) < .015f,"Object rotation changes isolated tint from Light to Shadow");
            material.SetFloat("_RampShadowThreshold",1); material.SetFloat("_RampLightThreshold",0);
            Capture(camera,"InvalidThresholds");
            MoebiusRampThresholdDrawer.Normalize(material);
            Check(material.GetFloat("_RampShadowThreshold") < material.GetFloat("_RampLightThreshold"),"Inspector normalization enforces shadow < light");
        }
        static float ColorError(Color a,Color b) => Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b));
        static void Check(bool condition,string message) { Checks.Add((condition?"PASS: ":"FAIL: ")+message); if(!condition) Errors.Add(message); }
        static int Differences(Color32[] a,Color32[] b)
        { int count=0; for(int i=0;i<a.Length;i++) if(!a[i].Equals(b[i])) count++; return count; }
        static void Compare(Color32[] a,Color32[] b,string label) { int changed=Differences(a,b); Check(changed==0,label+": "+changed+" changed pixels"); }
        static void CompareBefore(string name,Color32[] pixels)
        {
            var image=new Texture2D(2,2,TextureFormat.RGBA32,false); image.LoadImage(File.ReadAllBytes("Validation/ColorRamp/Before/"+name+".png"));
            Compare(image.GetPixels32(),pixels,"Before vs "+name); UnityEngine.Object.DestroyImmediate(image);
        }
        static Color32[] Capture(Camera camera,string name)
        {
            var target = new RenderTexture(256,256,24,RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture=target;
            camera.Render(); camera.Render(); var pixels=Read(target,name); camera.targetTexture=null;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); return pixels;
        }
        static Color32[] CapturePass(GameObject sphere,Material material,string outputName,string passName=null)
        {
            int pass=material.FindPass(passName ?? outputName); if(pass<0) throw new Exception("Missing pass "+outputName);
            var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);target.Create();
            var commands=new CommandBuffer(); commands.SetRenderTarget(target);commands.ClearRenderTarget(true,true,Color.black);
            var view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(new Vector3(0,0,-8),Quaternion.identity,Vector3.one).inverse;
            commands.SetViewProjectionMatrices(view,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-2,2,-2,2,.1f,20),true));
            commands.SetGlobalVector("_LightDirection",new Vector4(0,0,-1,0));commands.SetGlobalVector("_ShadowBias",Vector4.zero);
            commands.DrawMesh(sphere.GetComponent<MeshFilter>().sharedMesh,sphere.transform.localToWorldMatrix,material,0,pass);
            Graphics.ExecuteCommandBuffer(commands);commands.Release();var pixels=Read(target,outputName);
            target.Release();UnityEngine.Object.DestroyImmediate(target);return pixels;
        }
        static Color32[] Read(RenderTexture target,string name)
        {
            var old=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(256,256,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();var pixels=image.GetPixels32();
            File.WriteAllBytes(folder+"/"+name+".png",image.EncodeToPNG());RenderTexture.active=old;
            UnityEngine.Object.DestroyImmediate(image);return pixels;
        }
    }
}
