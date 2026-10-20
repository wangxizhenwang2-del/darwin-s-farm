using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Darwin.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Darwin.Rendering.Validation
{
    [InitializeOnLoad]
    public static class BillboardAnimalMotionPlayValidation
    {
        const string Key="BillboardMotion.Play",Output="Validation/AnimalMotion/";
        static readonly HashSet<Sprite> Seen=new HashSet<Sprite>();
        static readonly List<string> Errors=new List<string>();
        static float started;
        static int count;
        static Vector3 previous;
        static BillboardAnimalMotionPlayValidation()
        {
            EditorApplication.update+=Tick;
            Application.logMessageReceived+=(message,stack,type)=> {if(SessionState.GetInt(Key,0)>0&&(type==LogType.Error||type==LogType.Exception))Errors.Add(message);};
        }
        public static void Run()
        {
            if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/Temp/")||!File.Exists("AnimalMotion.marker"))throw new Exception("Use the marked isolated fixture.");
            EditorSceneManager.OpenScene(BillboardAnimalMotionSceneBuilder.ScenePath);
            SessionState.SetInt(Key,1);EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            int state=SessionState.GetInt(Key,0);if(state==0)return;
            if(state==3&&!EditorApplication.isPlayingOrWillChangePlaymode) {SessionState.SetInt(Key,0);EditorApplication.Exit(0);return;}
            if(!Application.isPlaying)return;
            try
            {
                var demo=Object.FindFirstObjectByType<BillboardAnimalMotionDemo>();
                if(!demo||demo.animals==null||demo.animals.Length!=4)throw new Exception("Scene motion bindings lost on reload");
                if(state==1)
                {
                    if(Time.time<.2f)return;
                    started=Time.time;previous=demo.animals[0].transform.position;
                    count=Resources.FindObjectsOfTypeAll<Mesh>().Count(m=>m.name=="Billboard motion strokes");
                    SessionState.SetInt(Key,2);return;
                }
                if(state!=2)return;
                Seen.Add(demo.animals[2].image.sprite);
                if(Time.time-started<1.3f)return;
                if(Seen.Count<3||Vector3.Distance(previous,demo.animals[0].transform.position)<.2f)throw new Exception("Native frame animation or swimming motion stopped");
                if(count!=Resources.FindObjectsOfTypeAll<Mesh>().Count(m=>m.name=="Billboard motion strokes"))throw new Exception("Effect meshes accumulate during playback");
                foreach(var actor in demo.animals)
                    if(actor.effects.GetComponent<MeshFilter>().sharedMesh==null)throw new Exception("Effect mesh failed to recreate after scene reload");
                if(Errors.Count>0)throw new Exception(string.Join(";",Errors));
                File.WriteAllText(Output+"PlayReport.txt","PASS: Reloaded saved scene in native Play Mode.\nPASS: Swimming position advances; flight displays "+Seen.Count+" distinct authored frames.\nPASS: Four effect meshes recreated and remain stable; no runtime errors.\n");
                SessionState.SetInt(Key,3);EditorApplication.ExitPlaymode();
            }
            catch(Exception e) {Directory.CreateDirectory(Output);File.WriteAllText(Output+"PlayFailure.txt",e.ToString());SessionState.SetInt(Key,0);Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
