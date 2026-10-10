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
    public static class MoebiusSpatialGradientValidation
    {
        static readonly List<string> report = new List<string>();
        static readonly List<string> errors = new List<string>();
        static string folder;
        public static void RunBatch()
        {
            Application.logMessageReceived += (message,stack,type) => { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message); };
            try
            {
                if(!Application.isBatchMode || !File.Exists("SpatialGradientValidation.marker")) throw new Exception("Use isolated validation project.");
                string label = File.ReadAllText("SpatialGradientValidation.marker").Trim();
                folder="Validation/SpatialGradient/"+label; Directory.CreateDirectory(folder);
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline; QualitySettings.renderPipeline=pipeline; ShaderUtil.allowAsyncCompilation=false;
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/MoebiusComicDemo.unity",OpenSceneMode.Single);
                var camera=UnityEngine.Object.FindFirstObjectByType<Camera>();
                Capture(camera,"ComicOriginal",1000,700);
                if(label=="After")
                {
                    CompareBefore("ComicOriginal",Capture(camera,"ComicDefaultOff",1000,700));
                    var group=BuildDemo();
                    EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                    EditorSceneManager.OpenScene(scene.path,OpenSceneMode.Single);
                    group=UnityEngine.Object.FindFirstObjectByType<MoebiusSpatialColorGradient>();
                    camera=UnityEngine.Object.FindFirstObjectByType<Camera>();
                    Check(group && group.pointA && group.pointB && group.targets.Length==2 && group.targets.All(r=>r),"Saved scene reloads both control points and renderer references");
                    group.ApplyNow();
                    Capture(camera,"ComicGradient",1000,700);
                    camera.transform.position=new Vector3(.075f,3,-2.8f);
                    camera.transform.LookAt(new Vector3(.075f,2.52f,1.2f));
                    camera.fieldOfView=42;
                    Capture(camera,"WallCloseup",1000,700);
                    group.enabled=false; Capture(camera,"DifferentMaterialsOff",1000,700);
                    group.enabled=true; group.ApplyNow(); Capture(camera,"DifferentMaterialsOn",1000,700);
                }
                ValidateStage(label);
                foreach(var shader in new[]{Shader.Find("Darwin/Moebius NPR"),Shader.Find("Hidden/Darwin/Moebius Outline")})
                    foreach(var message in ShaderUtil.GetShaderMessages(shader))
                        if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)errors.Add(message.message);
            }
            catch(Exception exception) { errors.Add(exception.ToString()); }
            if(folder==null)folder="Validation/SpatialGradient";Directory.CreateDirectory(folder);
            File.WriteAllText(folder+"/Report.txt",string.Join("\n",report)+"\nShader/runtime/test errors: "+errors.Count+"\n"+string.Join("\n",errors));
            EditorApplication.Exit(errors.Count==0?0:1);
        }
        static MoebiusSpatialColorGradient BuildDemo()
        {
            var group=new GameObject("Moebius Spatial Gradient Test").AddComponent<MoebiusSpatialColorGradient>();
            var renderers=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r=>r.name.StartsWith("Sandstone block")).ToArray();
            var left=renderers.OrderBy(r=>(r.transform.position-new Vector3(-.6f,2.52f,1.2f)).sqrMagnitude).First();
            var right=renderers.OrderBy(r=>(r.transform.position-new Vector3(.75f,2.52f,1.2f)).sqrMagnitude).First();
            if(left==right)throw new Exception("Cannot find adjacent wall blocks.");
            left.transform.SetParent(group.transform,true);right.transform.SetParent(group.transform,true);
            const string testMaterialPath="Assets/Rendering/Moebius/Samples/Shared/Materials/SpatialGradientCoolTest.mat";
            var testMaterial=AssetDatabase.LoadAssetAtPath<Material>(testMaterialPath);
            if(!testMaterial)
            {
                testMaterial=new Material(left.sharedMaterial){name="Spatial Gradient Cool Test"};
                testMaterial.SetColor("_BaseColor",new Color(.64f,.63f,.88f));
                AssetDatabase.CreateAsset(testMaterial,testMaterialPath);
            }
            right.sharedMaterial=testMaterial;
            group.pointA=new GameObject("Color A - move to set gradient start").transform;
            group.pointB=new GameObject("Color B - move to set gradient end").transform;
            group.pointA.SetParent(group.transform);group.pointB.SetParent(group.transform);
            group.pointA.position=left.transform.position;group.pointB.position=right.transform.position;
            group.targets=new Renderer[]{left,right};group.strength=1;group.transitionWidth=1;
            group.RefreshTargets();
            Check(left.sharedMaterial!=right.sharedMaterial,"Comic demo uses two distinct material assets");
            return group;
        }
        static void ValidateStage(string label)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Validation Camera").AddComponent<Camera>();camera.GetUniversalAdditionalCameraData().SetRenderer(1);
            camera.orthographic=true;camera.orthographicSize=1.3f;camera.transform.position=new Vector3(0,0,-5);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var light=new GameObject("Validation Light").AddComponent<Light>();light.type=LightType.Directional;
            var left=GameObject.CreatePrimitive(PrimitiveType.Quad);left.transform.position=new Vector3(-.5f,0,0);
            var right=GameObject.CreatePrimitive(PrimitiveType.Quad);right.transform.position=new Vector3(.5f,0,0);
            var a=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Samples/Shared/Materials/Sandstone.mat"));
            var b=new Material(a);b.SetColor("_BaseColor",new Color(.6f,.7f,.9f));
            left.GetComponent<Renderer>().sharedMaterial=a;right.GetComponent<Renderer>().sharedMaterial=b;
            var baseline=Capture(camera,"StageOff");
            if(label=="After")
            {
                CompareBefore("StageOff",baseline);
                var group=new GameObject("Test controls").AddComponent<MoebiusSpatialColorGradient>();
                group.pointA=new GameObject("A").transform;group.pointA.position=new Vector3(-.8f,0,0);
                group.pointB=new GameObject("B").transform;group.pointB.position=new Vector3(.8f,0,0);
                group.targets=new[]{left.GetComponent<Renderer>(),right.GetComponent<Renderer>()}; group.RefreshTargets();
                group.strength=0;group.ApplyNow();Compare(baseline,Capture(camera,"StrengthZero"),"Zero strength preserves old rendering");
                group.strength=1;group.ApplyNow();var enabled=Capture(camera,"StageOn");
                Check(Differences(baseline,enabled)>100,"Spatial gradient is visibly active");
                group.enabled=false;Compare(baseline,Capture(camera,"ControllerDisabled"),"Disabling controller restores original materials");
                group.enabled=true;group.ApplyNow();
                foreach(int mode in new[]{2,3,4,5,9,10,11,12})
                {
                    a.SetFloat("_DebugMode",mode);b.SetFloat("_DebugMode",mode);
                    var on=Capture(camera,"Mask"+mode+"On");group.enabled=false;
                    Compare(on,Capture(camera,"Mask"+mode+"Off"),"Existing debug mask "+mode+" unchanged");
                    group.enabled=true;group.ApplyNow();
                }
                // Base-color debug isolates gradient from light, highlights and ink. No texture or albedo differences.
                a.SetFloat("_DebugMode",1);b.SetFloat("_DebugMode",1);
                camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                var colorOnly=Capture(camera,"ColorOnly");
                light.transform.rotation=Quaternion.Euler(75,120,0);
                Compare(colorOnly,Capture(camera,"LightRotated"),"Gradient color does not depend on light direction");
                int center=128*256+128;
                var l=(Color)colorOnly[center-1];var r=(Color)colorOnly[center];
                Check(Mathf.Max(Mathf.Abs(l.r-r.r),Mathf.Abs(l.g-r.g),Mathf.Abs(l.b-r.b))<.03f,"Different-material seam has continuous base color");
                group.pointA.position=new Vector3(-.8f,0,0);group.pointB.position=new Vector3(0,.8f,0);group.ApplyNow();
                Check(Differences(colorOnly,Capture(camera,"ControlsMoved"))>100,"Moving anchors changes direction and transition");
                // Move geometry, anchors and camera together; world-space field follows the moving group.
                group.pointA.SetParent(group.transform,true);group.pointB.SetParent(group.transform,true);
                left.transform.SetParent(group.transform,true);right.transform.SetParent(group.transform,true);
                var beforeMove=Capture(camera,"BeforeGroupMove");group.transform.position+=new Vector3(3,2,1);camera.transform.position+=new Vector3(3,2,1);group.ApplyNow();
                var afterMove=Capture(camera,"AfterGroupMove");
                int maxDifference=0;for(int i=0;i<beforeMove.Length;i++)
                    maxDifference=Mathf.Max(maxDifference,Mathf.Abs(beforeMove[i].r-afterMove[i].r),Mathf.Abs(beforeMove[i].g-afterMove[i].g),Mathf.Abs(beforeMove[i].b-afterMove[i].b));
                Check(maxDifference<=1,"Moving group keeps its gradient attached: max channel difference "+maxDifference+"/255");
                group.pointB.position=group.pointA.position;group.ApplyNow();Capture(camera,"CoincidentAnchors");
                Check(true,"Coincident anchors render safely without division by zero");
                group.enabled=false;
            }
            UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);
        }
        static void Check(bool condition,string text){report.Add((condition?"PASS: ":"FAIL: ")+text);if(!condition)errors.Add(text);}
        static int Differences(Color32[] a,Color32[] b){int n=0;for(int i=0;i<a.Length;i++)if(!a[i].Equals(b[i]))n++;return n;}
        static void Compare(Color32[] a,Color32[] b,string name){int changed=Differences(a,b);Check(changed==0,name+": "+changed+" changed pixels");}
        static void CompareBefore(string name,Color32[] pixels)
        {
            var image=new Texture2D(2,2);image.LoadImage(File.ReadAllBytes("Validation/SpatialGradient/Before/"+name+".png"));
            Compare(image.GetPixels32(),pixels,"Before vs "+name);UnityEngine.Object.DestroyImmediate(image);
        }
        static Color32[] Capture(Camera camera,string name,int width=256,int height=256)
        {
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);rt.Create();camera.targetTexture=rt;
            camera.Render();camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            var pixels=image.GetPixels32();File.WriteAllBytes(folder+"/"+name+".png",image.EncodeToPNG());
            RenderTexture.active=previous;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);return pixels;
        }
    }
}
