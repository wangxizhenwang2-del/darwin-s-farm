using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Darwin.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Darwin.Rendering.Validation
{
    [InitializeOnLoad]
    public static class MoebiusMountainPreviewSetup
    {
        const string Results="Validation/MountainPreview";
        static readonly List<string> notes=new List<string>(),errors=new List<string>();
        static string sceneName;
        static double opened;
        static float firstTime;
        static bool sampling;
        static int phase;
        static MoebiusMountainPreviewSetup(){EditorApplication.update+=Tick;}
        static void Guard()
        {
            if(!File.Exists("MountainVFX.marker")||!Application.dataPath.Replace('\\','/').Contains("/Temp/"))throw new Exception("Use the isolated fixture.");
            Directory.CreateDirectory(Results);
            Application.logMessageReceived+=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m);};
        }
        public static void Attach()
        {
            try
            {
                Guard();
                foreach(string name in new[]{"Volcano","SnowMountain"})
                {
                    string path="Assets/Rendering/Moebius/Content/VFX/"+name+"/Prefabs/"+name+"VFX.prefab";
                    var root=PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var preview=root.GetComponent<MoebiusParticlePreview>();if(!preview)preview=root.AddComponent<MoebiusParticlePreview>();
                        preview.animateEditModePreview=true;preview.previewWarmupSeconds=name=="Volcano"?1.8f:6;
                        preview.particleSystems=root.GetComponentsInChildren<ParticleSystem>(true);
                        PrefabUtility.SaveAsPrefabAsset(root,path);
                        notes.Add(name+": editor preview enabled; warmup="+preview.previewWarmupSeconds+"; systems="+preview.particleSystems.Length);
                    }
                    finally{PrefabUtility.UnloadPrefabContents(root);}
                }
                AssetDatabase.SaveAssets();
            }
            catch(Exception e){errors.Add(e.ToString());}
            Finish("AttachReport.txt");
        }
        public static void RunEditorPreview()
        {
            Guard();
            if(Application.isBatchMode)throw new Exception("This check must run in actual non-batch Edit Mode.");
            ShaderUtil.allowAsyncCompilation=false;
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            sampling=true;Open("Volcano");
        }
        static void Open(string name)
        {
            sceneName=name;EditorSceneManager.OpenScene("Assets/Rendering/Moebius/Samples/Biomes/Scenes/"+name+"_Moebius.unity");
            Selection.activeObject=null;opened=EditorApplication.timeSinceStartup;phase=0;
        }
        static void Check(bool value,string message){notes.Add((value?"PASS: ":"FAIL: ")+message);if(!value)throw new Exception(message);}
        static void Tick()
        {
            if(!sampling)return;
            try
            {
                double age=EditorApplication.timeSinceStartup-opened;
                var rig=Object.FindFirstObjectByType<MoebiusParticlePreview>();
                if(age>25)throw new Exception("Editor preview check timeout for "+sceneName);
                if(age<1)return;
                if(phase==0)
                {
                    // Opening/compiling the scene can block the first editor update for over a second.
                    if(!rig||!rig.particleSystems.All(p=>p&&p.time>0))return;
                    Check(!EditorApplication.isPlaying,"Actual Edit Mode, without Play or particle selection");
                    Check(rig&&rig.particleSystems.All(p=>p&&p.time>0),sceneName+": automatic editor callback advances simulation");
                    Check(rig.particleSystems.Sum(p=>p.particleCount)>0,sceneName+": visible particles without selection");
                    firstTime=rig.particleSystems[0].time;phase=1;opened=EditorApplication.timeSinceStartup;
                    Capture(Camera.main,Results+"/"+sceneName+"EditMode.png");
                }
                else if(phase==1&&age>2)
                {
                    Check(rig.particleSystems[0].time>firstTime+.1f,sceneName+": preview advances continuously");
                    Check(!rig.gameObject.scene.isDirty,sceneName+": simulation leaves scene clean");
                    rig.animateEditModePreview=false;firstTime=rig.particleSystems[0].time;phase=2;opened=EditorApplication.timeSinceStartup;
                }
                else if(phase==2)
                {
                    Check(Mathf.Abs(rig.particleSystems[0].time-firstTime)<.001f,sceneName+": preview toggle pauses simulation");
                    if(sceneName=="Volcano")Open("SnowMountain");else{sampling=false;Finish("EditModeReport.txt");}
                }
            }
            catch(Exception e){sampling=false;errors.Add(e.ToString());Finish("EditModeReport.txt");}
        }
        static void Capture(Camera camera,string path)
        {
            var rt=new RenderTexture(1200,825,24,RenderTextureFormat.ARGB32);rt.Create();var tex=new Texture2D(1200,825,TextureFormat.RGB24,false);
            var old=RenderTexture.active;camera.targetTexture=rt;camera.Render();camera.Render();RenderTexture.active=rt;
            tex.ReadPixels(new Rect(0,0,1200,825),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);
        }
        static void Finish(string name)
        {Directory.CreateDirectory(Results);File.WriteAllLines(Results+"/"+name,notes.Concat(new[]{"Errors: "+errors.Count}).Concat(errors));EditorApplication.Exit(errors.Count==0?0:1);}
    }
}
