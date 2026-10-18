using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Darwin.Rendering.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Darwin.Rendering.Validation
{
    // Run only inside the existing isolated Temp fixture. Never opens or saves a user's live scene.
    [InitializeOnLoad]
    public static class MoebiusSkyboxValidation
    {
        const string Root="Assets/Rendering/Moebius/";
        const string Materials=Root+"Content/Skybox/Materials/";
        const string Sample=Root+"Samples/Skybox/";
        const string Output="Validation/Skybox/";
        static readonly List<string> Notes=new List<string>();
        static readonly string[] Names={"ClearSky"};
        const string PlayKey="MoebiusSkybox.NativePlayValidation";
        static int frames;
        static MoebiusSkyboxValidation() { EditorApplication.update+=PlayTick; }
        static void Check(bool condition,string text) { if(!condition)throw new Exception(text); Notes.Add("PASS: "+text); }
        static void Folder(string path)
        {
            path=path.TrimEnd('/'); if(AssetDatabase.IsValidFolder(path))return;
            var parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        static void Guard()
        {
            if(!Application.isBatchMode || !Application.dataPath.Replace('\\','/').Contains("/Temp/") || !File.Exists("RiverConnections.marker"))throw new Exception("Use the marked isolated validation project.");
            Directory.CreateDirectory(Output); ShaderUtil.allowAsyncCompilation=false;
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline; QualitySettings.renderPipeline=pipeline;
        }
        public static void Build()
        {
            try
            {
                Guard(); var shader=Shader.Find(MoebiusSkyboxPresets.ShaderName); Check(shader,"Skybox shader imported");
                Folder(Materials); Folder(Sample);
                if (!AssetDatabase.LoadAssetAtPath<Material>(Materials+"ClearSky.mat") && AssetDatabase.LoadAssetAtPath<Material>(Materials+"IllustratedDaylight.mat"))
                    Check(AssetDatabase.MoveAsset(Materials+"IllustratedDaylight.mat",Materials+"ClearSky.mat")=="","Preserved original daylight material GUID");
                foreach(string old in new[]{"GoldenHour","PastelOvercast","IllustratedTwilight"})
                    if(AssetDatabase.LoadAssetAtPath<Material>(Materials+old+".mat")) AssetDatabase.DeleteAsset(Materials+old+".mat");
                for(int i=0;i<Names.Length;i++)
                {
                    string path=Materials+Names[i]+".mat";
                    var m=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(!m) {m=new Material(shader){name=Names[i]};AssetDatabase.CreateAsset(m,path);}
                    MoebiusSkyboxPresets.ApplyClearSky(m);
                }
                AssetDatabase.SaveAssets();
                string scenePath=Sample+"MoebiusSkyboxDemo.unity";
                if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath))
                    Check(AssetDatabase.CopyAsset(Root+"Samples/Biomes/Scenes/Forest_Moebius.unity",scenePath),"Created a separate copy of the Forest scene");
                var scene=EditorSceneManager.OpenScene(scenePath);
                var camera=Camera.main; Check(camera,"Representative camera exists");
                var renderers=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.enabled&&!r.forceRenderingOff).ToArray();
                Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                float size=Mathf.Max(bounds.size.x,bounds.size.z);
                Notes.Add("Scene bounds: "+bounds.ToString("F3"));
                Vector3 target=new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*.30f,bounds.center.z);
                camera.transform.position=target+new Vector3(-.52f,.22f,-.70f)*size;
                camera.transform.LookAt(target+Vector3.up*size*.20f);
                camera.orthographic=false;camera.fieldOfView=64;camera.nearClipPlane=.1f;camera.farClipPlane=Mathf.Max(500,size*15);
                camera.clearFlags=CameraClearFlags.Skybox;camera.GetUniversalAdditionalCameraData().SetRenderer(1);
                var oldAmbient=RenderSettings.ambientMode;var oldAmbientColor=RenderSettings.ambientLight;
                var oldIntensity=RenderSettings.reflectionIntensity;var oldFog=RenderSettings.fog;
                RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>(Materials+Names[0]+".mat");
                Check(RenderSettings.ambientMode==oldAmbient && RenderSettings.ambientLight==oldAmbientColor && RenderSettings.reflectionIntensity==oldIntensity && RenderSettings.fog==oldFog,"Assigning sky preserves scene ambient, reflections and fog");
                EditorSceneManager.SaveScene(scene,scenePath);
                Render(camera,"Forest_Daylight",1500,1000);
                foreach(string name in Names)
                {
                    RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>(Materials+name+".mat");
                    Render(camera,"Forest_"+name,1500,1000);
                }
                ValidateSky(camera,shader);
                foreach(var message in ShaderUtil.GetShaderMessages(shader))
                    Check(message.severity.ToString()!="Error" && message.severity.ToString()!="Warning", "Shader diagnostic: "+message.message);
                Check(shader.isSupported,"Skybox is supported by the current GPU");
                Check(shader.passCount==1,"Skybox uses one rendering pass");
                File.WriteAllLines(Output+"Report.txt",Notes);
                EditorApplication.Exit(0);
            }
            catch(Exception e){File.WriteAllText(Output+"Failure.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
        }

        public static void RunPlay()
        {
            Guard(); EditorSceneManager.OpenScene(Sample+"MoebiusSkyboxDemo.unity");
            SessionState.SetInt(PlayKey,1);EditorApplication.EnterPlaymode();
        }
        static void PlayTick()
        {
            int state=SessionState.GetInt(PlayKey,0);if(state==0)return;
            if(state==2 && !EditorApplication.isPlayingOrWillChangePlaymode) {SessionState.SetInt(PlayKey,0);EditorApplication.Exit(0);return;}
            if(state!=1 || !Application.isPlaying || ++frames<20)return;
            try
            {
                Check(Time.time>0,"Play Mode time advances");
                Check(RenderSettings.skybox && RenderSettings.skybox.shader.name==MoebiusSkyboxPresets.ShaderName,"Sky material persists in Play Mode");
                Render(Camera.main,"PlayMode",1200,800);
                foreach(var m in ShaderUtil.GetShaderMessages(RenderSettings.skybox.shader)) Check(m.severity.ToString()!="Error","Play Mode shader: "+m.message);
                File.WriteAllLines(Output+"PlayReport.txt",Notes);
                SessionState.SetInt(PlayKey,2);EditorApplication.ExitPlaymode();
            }
            catch(Exception e){File.WriteAllText(Output+"PlayFailure.txt",e.ToString());SessionState.SetInt(PlayKey,0);Debug.LogException(e);EditorApplication.Exit(1);}
        }

        static void ValidateSky(Camera camera,Shader shader)
        {
            var material=new Material(AssetDatabase.LoadAssetAtPath<Material>(Materials+Names[0]+".mat"));
            material.SetFloat("_CloudSpeed",0);material.SetFloat("_UseMainLightSun",0);
            RenderSettings.skybox=material;camera.cullingMask=0;camera.orthographic=false;camera.fieldOfView=85;
            camera.transform.SetPositionAndRotation(Vector3.zero,Quaternion.Euler(-12,0,0));
            var baseline=Render(camera,"Sky_Daylight",1200,800);
            camera.transform.position=new Vector3(1200,350,-800);
            var translated=Render(camera,null,1200,800);
            Check(Error(baseline,translated)<.0001,"Sky is independent of camera translation; mean RGB error="+Error(baseline,translated));
            camera.transform.position=Vector3.zero;camera.transform.rotation=Quaternion.Euler(-12,360,0);
            Check(Error(baseline,Render(camera,null,1200,800))<.0002,"360 degree rotation returns to the same sky");
            foreach(int yaw in new[]{0,90,180,270}) {camera.transform.rotation=Quaternion.Euler(-12,yaw,0);Render(camera,"Sky_Yaw"+yaw,1000,700);}
            camera.transform.rotation=Quaternion.Euler(-90,0,0);Render(camera,"Sky_Zenith",800,800);
            camera.transform.rotation=Quaternion.Euler(90,0,0);Render(camera,"Sky_Nadir",800,800);
            camera.transform.rotation=Quaternion.Euler(-12,0,0);
            for(int i=0;i<Names.Length;i++)
            {
                MoebiusSkyboxPresets.ApplyClearSky(material);material.SetFloat("_UseMainLightSun",0);material.SetFloat("_CloudSpeed",0);
                Render(camera,"Sky_"+Names[i],1200,800);
            }
            var viewRotation=camera.transform.rotation;
            camera.transform.rotation=Quaternion.LookRotation(material.GetVector("_SunDirection"),Vector3.up);
            material.SetFloat("_EnableClouds",0);
            var haze=Render(camera,"Sun_CloudShelf",800,800);
            material.SetFloat("_SunHazeStrength",0);
            var clearDisc=Render(camera,"Sun_WithoutShelf",800,800);
            int lowerSun=(800/2-50)*800+800/2;
            int sunCenter=(800/2)*800+800/2;
            Check(ColorDifference(haze[lowerSun],clearDisc[lowerSun])>.04 && ColorDifference(haze[sunCenter],clearDisc[sunCenter])<.01,
                "Sun cloud shelf washes the lower disc while preserving the upper disc");
            camera.transform.rotation=viewRotation;
            MoebiusSkyboxPresets.ApplyClearSky(material);material.SetFloat("_UseMainLightSun",0);
            material.SetFloat("_CloudSpeed",.15f);material.SetFloat("_AnimationPhase",0);
            var t0=Render(camera,null,1000,700);material.SetFloat("_AnimationPhase",1/60f);
            var t1=Render(camera,null,1000,700);material.SetFloat("_AnimationPhase",20);
            var t20=Render(camera,"Sky_Clouds_Later",1000,700);
            Check(Error(t0,t1)<.003 && Error(t0,t20)>Error(t0,t1),"Clouds move continuously: frame difference="+Error(t0,t1)+", 20-second difference="+Error(t0,t20));
            material.SetFloat("_CloudSpeed",0);material.SetFloat("_EnableClouds",0);
            material.SetFloat("_PigmentStrength",0);material.SetFloat("_PaperStrength",0);material.SetFloat("_HaloStrength",0);material.SetFloat("_SunHorizonInfluence",0);
            var light=RenderSettings.sun;
            Check(light && light.type==LightType.Directional,"Scene main Directional Light available");
            Quaternion rotation=light.transform.rotation;
            // A zero camera culling mask also excludes the scene light from URP's visible lights.
            camera.cullingMask=-1;camera.transform.position=new Vector3(1200,350,-800);
            material.SetFloat("_UseMainLightSun",1);
            camera.transform.rotation=Quaternion.LookRotation(-light.transform.forward,Vector3.up);
            var aligned=Render(camera,"Sun_Aligned",400,400);
            light.transform.Rotate(0,80,0,Space.World);var turned=Render(camera,"Sun_Turned",400,400);
            int center=(400/2)*400+400/2;
            Check(ColorDifference(aligned[center],turned[center])>.05,"Sun follows main Directional Light rotation");
            light.transform.rotation=rotation;
            material.SetFloat("_UseMainLightSun",0);material.SetVector("_SunDirection",-light.transform.forward);
            var manual=Render(camera,null,400,400);
            Check(ColorDifference(aligned[center],manual[center])<.01,"Manual and light-synchronized sun directions agree");
            material.SetFloat("_EnableSun",0);var disabled=Render(camera,null,400,400);
            Check(ColorDifference(manual[center],disabled[center])>.05,"Sun can be disabled independently");
            camera.transform.rotation=Quaternion.Euler(-12,0,0);camera.orthographic=true;camera.orthographicSize=8;
            Render(camera,"Sky_Orthographic",1000,700);camera.orthographic=false;
            material.SetFloat("_EnableSun",1);material.SetFloat("_EnableClouds",1);material.SetFloat("_GradientSteps",6);Render(camera,"Sky_Stepped",1000,700);
            material.SetFloat("_FogColorMatch",1);RenderSettings.fogColor=new Color(.8f,.7f,.8f);Render(camera,"Sky_FogMatch",1000,700);
            camera.GetUniversalAdditionalCameraData().SetRenderer(0);Render(camera,"Sky_StandardRenderer",1000,700);
            Object.DestroyImmediate(material);
        }
        static double ColorDifference(Color32 a,Color32 b)=>(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b))/(3.0*255);
        static double Error(Color32[] a,Color32[] b) {double error=0;for(int i=0;i<a.Length;i++)error+=ColorDifference(a[i],b[i]);return error/a.Length;}
        static Color32[] Render(Camera camera,string name,int width,int height)
        {
            var previous=RenderTexture.active;var oldTarget=camera.targetTexture;
            var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            var texture=new Texture2D(width,height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
            if(name!=null)File.WriteAllBytes(Output+name+".png",texture.EncodeToPNG());var pixels=texture.GetPixels32();
            Object.DestroyImmediate(texture);camera.targetTexture=oldTarget;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);return pixels;
        }
    }
}
