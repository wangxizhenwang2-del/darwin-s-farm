using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Darwin.Rendering.Validation
{
    [InitializeOnLoad]
    public static class MoebiusYeMergeValidation
    {
        const string Results="Validation/YeMerge";
        const string Biomes="Assets/Rendering/Moebius/Samples/Biomes/";
        const string PlayKey="Darwin.YeMerge.Play";
        static readonly List<string> Notes=new List<string>(),Errors=new List<string>();
        static double deadline;
        static bool firstFrame;
        static MoebiusYeMergeValidation()
        {
            EditorApplication.update+=Tick;
            Application.logMessageReceived+=(m,s,t)=>
            {if(t==LogType.Error || t==LogType.Exception || t==LogType.Assert)Errors.Add(m);};
        }
        static void Check(bool value,string message)
        {Notes.Add((value?"PASS: ":"FAIL: ")+message);if(!value)throw new InvalidOperationException(message);}
        static void Guard()
        {
            Check(Application.isBatchMode && File.Exists("YeMerge.marker") && Application.dataPath.Replace('\\','/').Contains("/Temp/"),"Isolated full-project merge fixture");
            Directory.CreateDirectory(Results);ShaderUtil.allowAsyncCompilation=false;
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Check(pipeline,"Main URP pipeline loads");GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
        }
        public static void RunBatch()
        {
            try
            {
                Guard();
                foreach(var name in new[]{"SnowMountain","Volcano"})
                {
                    var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                    var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Biomes+"Prefabs/"+name+"_Moebius.prefab");
                    Check(asset,name+": main biome prefab loads");
                    var root=(GameObject)PrefabUtility.InstantiatePrefab(asset);CheckReferences(root,name);
                    var renderers=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();
                    Check(renderers.Length>0,name+": visible renderers exist");
                    var bounds=BoundsOf(renderers);
                    Notes.Add(name+" original bounds: "+bounds);
                    root.transform.position+=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z);
                    bounds=BoundsOf(renderers);SetupView(bounds);
                    EditorSceneManager.SaveScene(scene,Biomes+"Scenes/"+name+"_Moebius.unity");
                    Capture(Camera.main,Results+"/"+name+".png");
                    foreach(var shader in renderers.SelectMany(r=>r.sharedMaterials).Select(m=>m.shader).Distinct())
                        foreach(var error in ShaderUtil.GetShaderMessages(shader))
                            if(error.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)Errors.Add(shader.name+": "+error.message);
                }
                var template=AssetDatabase.LoadAssetAtPath<GameObject>(Biomes+"Prefabs/SnowMountain_Template.prefab");
                Check(template,"SnowMountain setup template remains available");
                var river=AssetDatabase.LoadAssetAtPath<GameObject>(Biomes+"Prefabs/River.prefab");
                Check(river,"Previous River transfer is preserved");
                CheckReferences(river,"River");
                foreach(var path in new[]{"Assets/Scenes/New Scene.unity","Assets/Scenes/WhiteBox.unity"})
                {
                    var scene=EditorSceneManager.OpenScene(path);
                    foreach(var root in scene.GetRootGameObjects())
                        Check(root.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),path+": no missing scripts under "+root.name);
                    foreach(var filter in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MeshFilter>(true)))
                        Check(filter.sharedMesh,path+": mesh resolves on "+filter.name);
                    Notes.Add("Loaded game scene: "+path);
                }
                AssetDatabase.SaveAssets();
            }
            catch(Exception e){Errors.Add(e.ToString());}
            Finish("Report.txt");
        }
        static void CheckReferences(GameObject root,string label)
        {
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,label+": script references on "+t.name);
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter=r.GetComponent<MeshFilter>();Check(filter && filter.sharedMesh,label+": mesh on "+r.name);
                Check(r.sharedMaterials.All(m=>m && m.shader && m.shader.isSupported),label+": materials/shaders on "+r.name);
                foreach(var material in r.sharedMaterials)
                {
                    if(material.HasProperty("_UsePaintColor") && material.GetFloat("_UsePaintColor")>.5f)Check(material.GetTexture("_PaintColorMap"),label+": paint texture "+material.name);
                    if(material.HasProperty("_VolcanoMask"))Check(material.GetTexture("_VolcanoMask"),label+": rock contact mask");
                    if(material.HasProperty("_FlowInk"))Check(material.GetTexture("_FlowInk"),label+": flowing ink texture");
                    if(material.HasProperty("_LakeMode") && material.GetFloat("_LakeMode")>.5f)Check(material.GetTexture("_LakeFlowField"),label+": lava lake flow field");
                }
            }
        }
        static Bounds BoundsOf(MeshRenderer[] renderers)
        {var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);return bounds;}
        static void SetupView(Bounds bounds)
        {
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.72f,.76f,.80f);RenderSettings.fog=false;
            var light=new GameObject("Directional Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;
            light.transform.rotation=Quaternion.Euler(50,-30,0);light.shadows=LightShadows.Soft;light.gameObject.AddComponent<UniversalAdditionalLightData>();RenderSettings.sun=light;
            float span=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.z,bounds.size.y));
            var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.orthographic=true;
            camera.GetUniversalAdditionalCameraData().SetRenderer(1);camera.orthographicSize=span*.68f;
            camera.nearClipPlane=Mathf.Max(.001f,span*.0001f);camera.farClipPlane=Mathf.Max(150,span*8);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.45f,.75f,.79f);
            camera.transform.position=bounds.center+new Vector3(1.1f,1.0f,-1.4f)*span;camera.transform.LookAt(bounds.center);
        }
        public static void RunPlay()
        {
            Guard();EditorSceneManager.OpenScene(Biomes+"Scenes/Volcano_Moebius.unity");
            // Frame the side containing the lava outlet, rather than the back of the rock.
            var renderers=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            var bounds=BoundsOf(renderers);var lake=renderers.First(r=>r.name=="LavaLake");
            var direction=lake.bounds.center-bounds.center;direction.y=0;
            if(direction.sqrMagnitude<.001f)direction=new Vector3(-1.1f,0,1.4f);
            direction.Normalize();float span=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
            Camera.main.transform.position=bounds.center+direction*span*1.8f+Vector3.up*span*1.1f;
            Camera.main.transform.LookAt(bounds.center);
            EditorSceneManager.SaveScene(Camera.main.gameObject.scene,Biomes+"Scenes/Volcano_Moebius.unity");
            Capture(Camera.main,Results+"/Volcano.png");
            SessionState.SetBool(PlayKey,true);EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            if(!SessionState.GetBool(PlayKey,false))return;
            if(deadline==0)deadline=EditorApplication.timeSinceStartup+90;
            if(EditorApplication.timeSinceStartup>deadline){Errors.Add("Play Mode timed out");Finish("RuntimeReport.txt");return;}
            if(!EditorApplication.isPlaying || EditorApplication.isPaused)return;
            if(!firstFrame && Time.time>.2f){Capture(Camera.main,Results+"/LavaEarly.png");firstFrame=true;}
            if(Time.time<4)return;
            try
            {
                Capture(Camera.main,Results+"/LavaLater.png");
                Check(!File.ReadAllBytes(Results+"/LavaEarly.png").SequenceEqual(File.ReadAllBytes(Results+"/LavaLater.png")),"Lava changes continuously during native Play Mode");
                Notes.Add("Native Play Mode seconds: "+Time.time);
            }
            catch(Exception e){Errors.Add(e.ToString());}
            Finish("RuntimeReport.txt");
        }
        static void Finish(string report)
        {
            Directory.CreateDirectory(Results);SessionState.SetBool(PlayKey,false);
            File.WriteAllLines(Results+"/"+report,Notes.Concat(new[]{"Errors: "+Errors.Count}).Concat(Errors));
            EditorApplication.Exit(Errors.Count==0?0:1);
        }
        static void Capture(Camera camera,string path)
        {
            var rt=new RenderTexture(1600,1100,24,RenderTextureFormat.ARGB32);rt.Create();
            var texture=new Texture2D(1600,1100,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            camera.targetTexture=rt;camera.Render();camera.Render();RenderTexture.active=rt;
            texture.ReadPixels(new Rect(0,0,1600,1100),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);
        }
    }
}
