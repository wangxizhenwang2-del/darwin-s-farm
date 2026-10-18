using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Darwin.Rendering.Validation
{
    // Guarded offline merge acceptance. Each imported check gets a fresh WhiteBox session.
    [InitializeOnLoad]
    public static class MoebiusBranchMergeValidation
    {
        const string Key="Vincent.XizhenMerge.", Output="Validation/BranchMerge/";
        static readonly Type[] Checks={typeof(MapWaterVisualChecks),typeof(WaterPopulationIntegrationChecks),typeof(WaterRoamingChecks),typeof(WaterPlacementChecks)};
        static MoebiusBranchMergeValidation() { EditorApplication.update+=Tick; }
        static void Guard()
        {
            if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/Temp/")||!File.Exists("RiverConnections.marker"))
                throw new Exception("Use the isolated validation project.");
            Directory.CreateDirectory(Output);
        }
        public static void Run()
        {
            try
            {
                Guard(); ShaderUtil.allowAsyncCompilation=false;
                File.WriteAllText(Output+"Report.txt","Merged project compiled.\n"+V62MobilityRegressionChecks.RunChecks()+"\n");
                SessionState.SetInt(Key+"index",0); Next();
            }
            catch(Exception e) { Fail(e); }
        }
        static void Next()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity");
            SessionState.SetInt(Key+"frames",0);SessionState.SetInt(Key+"state",1);
            SessionState.SetFloat(Key+"deadline",(float)EditorApplication.timeSinceStartup+240);
            EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            int state=SessionState.GetInt(Key+"state",0);if(state==0)return;
            try
            {
                if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"deadline",0)) throw new Exception("Acceptance timed out");
                int index=SessionState.GetInt(Key+"index",0);
                if(state==3&&!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    if(++index==Checks.Length) { SessionState.SetInt(Key+"state",0);EditorApplication.Exit(0);return; }
                    SessionState.SetInt(Key+"index",index); Next();return;
                }
                if(!Application.isPlaying)return;
                if(state==1)
                {
                    int frames=SessionState.GetInt(Key+"frames",0)+1;SessionState.SetInt(Key+"frames",frames);
                    if(frames<25)return;
                    SessionState.SetInt(Key+"state",2);
                    Checks[index].GetMethod("Run",BindingFlags.Static|BindingFlags.Public).Invoke(null,null);return;
                }
                if(state!=2)return;
                string result=Checks[index].GetProperty("Result",BindingFlags.Static|BindingFlags.Public).GetValue(null) as string;
                if(string.IsNullOrEmpty(result)||result=="Running")return;
                if(!result.StartsWith("PASS"))throw new Exception(Checks[index].Name+": "+result);
                File.AppendAllText(Output+"Report.txt",Checks[index].Name+": "+result+"\n");
                if(index==0) Capture();
                SessionState.SetInt(Key+"state",3);EditorApplication.ExitPlaymode();
            }
            catch(Exception e) {Fail(e);}
        }
        static void Capture()
        {
            var camera=Camera.main;var old=camera.targetTexture;var active=RenderTexture.active;
            var rt=RenderTexture.GetTemporary(1500,1000,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(1500,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1500,1000),0,0);image.Apply();
            File.WriteAllBytes(Output+"WhiteBox.png",image.EncodeToPNG());UnityEngine.Object.Destroy(image);
            camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);
        }
        static void Fail(Exception e)
        {
            SessionState.SetInt(Key+"state",0);Directory.CreateDirectory(Output);
            File.AppendAllText(Output+"Report.txt","FAIL: "+e+"\n");Debug.LogException(e);EditorApplication.Exit(1);
        }
    }
}
