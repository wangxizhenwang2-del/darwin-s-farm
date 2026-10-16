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
    public static class MoebiusBiomeVFXIdentityPlay
    {
        const string Key="MoebiusBiomeVFXIdentity.PlayValidation";
        static string Name => File.ReadAllText("BiomeVFXIdentityTarget.txt").Trim();
        static string Output => "Validation/BiomeVFXIdentity/"+Name+"/";
        static readonly List<string> Errors=new List<string>();
        static double deadline;
        static int frames;
        static WoodlandWindController controller;
        static GrasslandWindController grass;
        static MoebiusBiomeVFXIdentityPlay()
        {
            EditorApplication.update+=Tick;
            Application.logMessageReceived+=(message,stack,type)=>{
                if(SessionState.GetBool(Key,false) && (type==LogType.Error || type==LogType.Exception || type==LogType.Assert))Errors.Add(message);
            };
        }
        public static void RunBatch()
        {
            if(!File.Exists("BiomeVFXIdentity.marker"))throw new Exception("Use the isolated marked fixture.");
            EditorSceneManager.OpenScene("Assets/Rendering/Moebius/Samples/Biomes/Scenes/"+(Name=="Grassland"?"Grassland_Moebius":Name=="Forest"?"Forest_Moebius":"RainForest")+".unity");
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            SessionState.SetBool(Key,true);
            EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false))return;
            if(deadline==0)deadline=EditorApplication.timeSinceStartup+90;
            if(EditorApplication.timeSinceStartup>deadline){Finish("Timed out entering/running Play Mode");return;}
            if(!EditorApplication.isPlaying || EditorApplication.isPaused)return;
            frames++;
            if(Name=="Grassland"){if(!grass)grass=Object.FindFirstObjectByType<GrasslandWindController>();}
            else if(!controller)controller=Object.FindFirstObjectByType<WoodlandWindController>();
            if(Time.time<7.5f)return;
            try
            {
                if(!controller&&!grass)throw new Exception("Scene lost its wind controller after reload");
                var systems=grass?new[]{grass.floatingMotes,grass.fallingLeaves,grass.windWisps}:new[]{controller.floatingMotes,controller.fallingLeaves,controller.windWisps,controller.lowMist}.Where(ps=>ps).ToArray();
                var notes=new List<string>{"Actual Play Mode after saved-scene reload", "Native simulation time: "+Time.time.ToString("F3"),"Editor/player updates observed: "+frames};
                foreach(var ps in systems)
                {
                    if(!ps || !ps.isPlaying)Errors.Add("Particle system failed to play: "+ps);
                    else notes.Add(ps.name+": playing; particles="+ps.particleCount+"; seed="+ps.randomSeed);
                }
                if(systems.Sum(ps=>ps?ps.particleCount:0)==0)Errors.Add("No particles during native Play Mode");
                notes.Add("Current native wind: "+(grass?grass.CurrentWind:controller.CurrentWind));
                Capture(Camera.main,Output+"PlayMode.png");
                Camera.main.orthographic=true;Camera.main.orthographicSize=18;
                Capture(Camera.main,Output+"Orthographic.png");
                foreach(var ps in systems)
                {
                    var material=ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                    foreach(var error in ShaderUtil.GetShaderMessages(material.shader))
                        if(error.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)Errors.Add(error.message);
                }
                notes.Add("Errors: "+Errors.Count);notes.AddRange(Errors);
                File.WriteAllLines(Output+"RuntimeReport.txt",notes);
                SessionState.SetBool(Key,false);
                EditorApplication.Exit(Errors.Count==0?0:1);
            }
            catch(Exception exception){Finish(exception.ToString());}
        }
        static void Finish(string error)
        {
            SessionState.SetBool(Key,false);
            File.WriteAllText(Output+"RuntimeReport.txt","Errors: 1\n"+error);
            EditorApplication.Exit(1);
        }
        static void Capture(Camera camera,string path)
        {
            var target=new RenderTexture(1600,1100,24,RenderTextureFormat.ARGB32);target.Create();
            var image=new Texture2D(1600,1100,TextureFormat.RGB24,false);
            camera.targetTexture=target;camera.Render();camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1600,1100),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            RenderTexture.active=previous;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);
        }
    }
}
