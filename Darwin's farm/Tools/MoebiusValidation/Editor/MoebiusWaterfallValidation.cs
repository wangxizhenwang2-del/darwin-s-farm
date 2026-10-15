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
    public static class MoebiusWaterfallValidation
    {
        const string Output="Validation/Waterfall";
        static readonly List<string> checks=new List<string>(),errors=new List<string>();
        public static void RunBatch()
        {
            Directory.CreateDirectory(Output);
            Application.logMessageReceived+=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);};
            try
            {
                if(!Application.isBatchMode||!File.Exists("WaterfallValidation.marker"))throw new Exception("Use an isolated validation project.");
                ShaderUtil.allowAsyncCompilation=false;
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                MoebiusWaterfallSetup.RebuildSampleSplash();
                // Keep legacy overlays inactive; update existing cloud assets without replacing the sheet.
                var contents=PrefabUtility.LoadPrefabContents(MoebiusWaterfallSetup.PrefabPath);
                contents.transform.Find("Impact Foam").gameObject.SetActive(false);
                contents.transform.Find("Downstream Outflow").gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(contents,MoebiusWaterfallSetup.PrefabPath);PrefabUtility.UnloadPrefabContents(contents);
                MoebiusWaterfallSetup.CreateDemoScene();
                var root=GameObject.Find("MoebiusWaterfall");
                Check(root.GetComponentsInChildren<MeshRenderer>().Length==5,"Sheet and four clouds retained; both legacy overlay renderers are inactive (two fewer draw calls)");
                Check(root.GetComponentsInChildren<MonoBehaviour>().Length==0,"Sheet and splash animation remains GPU-only; only the receiving water tracks impact placement");
                var camera=Camera.main;
                Capture(camera,"Overview",1500,1000);
                camera.transform.position=new Vector3(7.5f,4.8f,-9);camera.transform.LookAt(new Vector3(0,1.5f,1.5f));camera.orthographicSize=5;
                Capture(camera,"Closeup",1500,1000);
                camera.transform.position=new Vector3(-8,5,-10);camera.transform.LookAt(new Vector3(0,1.5f,1.5f));
                Capture(camera,"CameraLeft",1500,1000);
                camera.transform.position=new Vector3(8,2.5f,-12);camera.transform.LookAt(new Vector3(0,1.5f,1.5f));
                Capture(camera,"CameraLow",1500,1000);
                camera.orthographic=false;camera.fieldOfView=48;
                Capture(camera,"CameraPerspective",1500,1000);
                camera.orthographic=true;camera.transform.position=new Vector3(7.5f,4.8f,-9);camera.transform.LookAt(new Vector3(0,1.5f,1.5f));
                checks.Add("Camera images captured: overview, closeup, opposite side, low angle and perspective; visual review required");
                var receiving=GameObject.Find("Lower Moebius Water").GetComponent<MeshRenderer>();
                TestImpactTracking(root,receiving,camera);
                TestSplashComposition(root);
                TestIntegratedImpact(receiving);
                foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())TestRenderer(renderer);
                TestDownwardFlow(root.transform.Find("Waterfall Sheet").GetComponent<MeshRenderer>());
                TestReceivingShadows(root,camera);
                foreach(string shaderName in new[]{"Darwin/Moebius Waterfall","Darwin/Moebius Waterfall Splash","Darwin/Moebius Waterfall Impact","Darwin/Moebius Water"})
                {
                    var shader=Shader.Find(shaderName);
                    foreach(var item in ShaderUtil.GetShaderMessages(shader))if(item.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)errors.Add(shaderName+": "+item.message);
                    Check(!ShaderUtil.ShaderHasError(shader),shaderName+" compiles without errors");
                    var batcher=typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityCode",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
                    if(batcher!=null)Check(Convert.ToInt32(batcher.Invoke(null,new object[]{shader,0}))==0,shaderName+" is SRP Batcher compatible");
                    else checks.Add("SRP Batcher Inspector query unavailable in this Unity version; UnityPerMaterial layout retained: "+shaderName);
                }
                EditorSceneManager.OpenScene(MoebiusWaterfallSetup.ScenePath);
                Check(GameObject.Find("MoebiusWaterfall").GetComponentsInChildren<MeshRenderer>().All(r=>r.sharedMaterial&&r.GetComponent<MeshFilter>().sharedMesh),"Saved scene reopens with all mesh/material references");
                Capture(Camera.main,"Reopened",1500,1000);
                File.WriteAllLines(Output+"/Delivery.txt",Directory.GetFiles(MoebiusWaterfallSetup.Root,"*",SearchOption.AllDirectories)
                    .Where(p=>!p.EndsWith(".meta")&&!p.EndsWith(".md")).Select(p=>p.Replace('\\','/')));
            }
            catch(Exception exception){errors.Add(exception.ToString());}
            if(errors.Count>0){Finish();return;}
            // Only the isolated fixture changes Editor settings. Retain the report through play entry.
            EditorSettings.enterPlayModeOptionsEnabled=true;
            EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged+=BeginPlayValidation;
            EditorApplication.EnterPlaymode();
        }
        static MeshRenderer playSheet,playWater;
        static Vector4 playSavedA,playSavedB;
        static int playStartFrame;
        static double playStartTime;
        static void BeginPlayValidation(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode)return;
            EditorApplication.playModeStateChanged-=BeginPlayValidation;
            try
            {
                playWater=GameObject.Find("Lower Moebius Water").GetComponent<MeshRenderer>();
                playSheet=playWater.GetComponent<MoebiusWaterfallImpactBinding>().waterfallSheet;
                playSavedA=playWater.sharedMaterial.GetVector("_ImpactPointA");playSavedB=playWater.sharedMaterial.GetVector("_ImpactPointB");
                playSheet.transform.localPosition+=new Vector3(.9f,.1f,-.7f);
                playSheet.transform.localRotation=Quaternion.Euler(0,18,0);
                playStartFrame=Time.frameCount;playStartTime=EditorApplication.timeSinceStartup;
                EditorApplication.update+=CheckPlayValidation;
            }
            catch(Exception exception){errors.Add(exception.ToString());Finish();}
        }
        static void CheckPlayValidation()
        {
            if(Time.frameCount<=playStartFrame+3)
            {
                if(EditorApplication.timeSinceStartup-playStartTime>30){errors.Add("Play Mode did not advance frames");EditorApplication.update-=CheckPlayValidation;Finish();}
                return;
            }
            EditorApplication.update-=CheckPlayValidation;
            try
            {
                var block=new MaterialPropertyBlock();playWater.GetPropertyBlock(block,0);var size=playSheet.sharedMaterial.GetVector("_SheetSize");
                var a=playSheet.transform.TransformPoint(new Vector3(-size.x*.5f,-size.y*.5f,0));
                var b=playSheet.transform.TransformPoint(new Vector3(size.x*.5f,-size.y*.5f,0));
                Check(Application.isPlaying&&Vector3.Distance(block.GetVector("_ImpactPointA"),a)<.00001f&&Vector3.Distance(block.GetVector("_ImpactPointB"),b)<.00001f,"Actual Play Mode frames automatically track sheet motion without manual sync");
                Check(playWater.sharedMaterial.GetVector("_ImpactPointA")==playSavedA&&playWater.sharedMaterial.GetVector("_ImpactPointB")==playSavedB,"Play Mode tracking does not modify material asset endpoint data");
                Capture(Camera.main,"PlayModeTracking",1500,1000);
            }
            catch(Exception exception){errors.Add(exception.ToString());}
            Finish();
        }
        static void Finish()
        {
            File.WriteAllText(Output+"/Report.txt",string.Join("\n",checks)+"\nShader/runtime/test errors: "+errors.Count+"\n"+string.Join("\n",errors));
            EditorApplication.Exit(errors.Count==0?0:1);
        }
        static void Check(bool ok,string message){checks.Add((ok?"PASS: ":"FAIL: ")+message);if(!ok)errors.Add(message);}
        static Color32[] TimedDraw(MeshRenderer renderer,float time,string passName,string imageName=null,bool oct=false,Material overrideMaterial=null)
        {
            var camera=new GameObject("Validation camera").AddComponent<Camera>();
            var bounds=renderer.bounds;bool horizontal=renderer.sharedMaterial.shader.name=="Darwin/Moebius Waterfall Impact"||renderer.sharedMaterial.shader.name=="Darwin/Moebius Water"||
                (renderer.sharedMaterial.HasProperty("_SplashMotionRole")&&renderer.sharedMaterial.GetFloat("_SplashMotionRole")==2);
            camera.transform.position=bounds.center+(horizontal?new Vector3(0,15,0):new Vector3(0,0,-10));
            camera.transform.LookAt(bounds.center);camera.orthographic=true;camera.orthographicSize=Mathf.Max(bounds.size.x,bounds.size.y)*.65f;
            if(horizontal)camera.transform.rotation=Quaternion.Euler(90,0,0);
            var target=new RenderTexture(640,640,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();
            var command=new CommandBuffer();command.SetRenderTarget(target);command.ClearRenderTarget(true,true,new Color(1,0,1,0));
            var view=camera.worldToCameraMatrix;var projection=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true);
            command.SetViewProjectionMatrices(view,projection);command.SetGlobalMatrix("unity_MatrixVP",projection*view);
            command.SetGlobalMatrix("unity_MatrixV",view);command.SetGlobalMatrix("glstate_matrix_projection",projection);
            command.SetGlobalVector("_WorldSpaceCameraPos",camera.transform.position);
            command.SetGlobalVector("_Time",new Vector4(time/20,time,time*2,time*3));
            if(oct)command.EnableShaderKeyword("_GBUFFER_NORMALS_OCT");else command.DisableShaderKeyword("_GBUFFER_NORMALS_OCT");
            var drawMaterial=overrideMaterial?overrideMaterial:renderer.sharedMaterial;
            bool shadowProperty=drawMaterial.HasProperty("_ReceiveWaterShadows");float shadowValue=shadowProperty?drawMaterial.GetFloat("_ReceiveWaterShadows"):0;
            if(shadowProperty)drawMaterial.SetFloat("_ReceiveWaterShadows",0);
            int pass=drawMaterial.FindPass(passName);if(pass<0)throw new Exception("Missing pass: "+passName);
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,0);
            if(block.isEmpty)renderer.GetPropertyBlock(block);
            command.DrawMesh(renderer.GetComponent<MeshFilter>().sharedMesh,renderer.localToWorldMatrix,drawMaterial,0,pass,block);
            Graphics.ExecuteCommandBuffer(command);command.Release();
            var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(640,640,TextureFormat.RGBA32,false,true);
            image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();var pixels=image.GetPixels32();
            if(imageName!=null)File.WriteAllBytes(Output+"/"+imageName+".png",image.EncodeToPNG());
            RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if(shadowProperty)drawMaterial.SetFloat("_ReceiveWaterShadows",shadowValue);
            Shader.DisableKeyword("_GBUFFER_NORMALS_OCT");return pixels;
        }
        static void TestRenderer(MeshRenderer renderer)
        {
            string name=renderer.name.Replace(' ','_');
            var first=TimedDraw(renderer,2,"WaterfallForward",name+"_Start");
            Check(first.Any(p=>p.a>0),renderer.name+" renders visible opaque geometry");
            Check(first.SequenceEqual(TimedDraw(renderer,2,"WaterfallForward")),renderer.name+" repeated time produces identical pixels (no random flicker)");
            var intervals=new List<int>();var prior=first;
            for(int i=1;i<=12;i++){var next=TimedDraw(renderer,2+i/60f,"WaterfallForward");intervals.Add(prior.Zip(next,(a,b)=>a.Equals(b)?0:1).Sum());prior=next;}
            Check(intervals.All(n=>n>0),renderer.name+" animates at every sampled 1/60 second interval");
            checks.Add(renderer.name+" changed pixels/frame: min="+intervals.Min()+" mean="+intervals.Average().ToString("F1")+" max="+intervals.Max());
            TimedDraw(renderer,2.5f,"WaterfallForward",name+"_End");
            if(renderer.sharedMaterial.HasProperty("_SplashMotionRole")&&renderer.sharedMaterial.GetFloat("_SplashMotionRole")>=1&&renderer.sharedMaterial.GetFloat("_SplashMotionRole")<3)
            {
                var later=TimedDraw(renderer,3.5f,"WaterfallForward",name+"_MotionLater");
                int edgeChanges=first.Zip(later,(a,b)=>(a.a>0)!=(b.a>0)?1:0).Sum();
                Check(edgeChanges>200,renderer.name+" has a visibly evolving vertex silhouette over 1.5 seconds ("+edgeChanges+" edge pixels)");
            }
            var depth=TimedDraw(renderer,2,"DepthOnly");
            var normals=TimedDraw(renderer,2,"DepthNormals");
            var oct=TimedDraw(renderer,2,"DepthNormals",null,true);
            // DepthOnly writes R only; clear alpha remains zero. Raw normals alpha is zero too.
            Func<Color32,bool> visible=p=>p.r!=255||p.g!=0||p.b!=255;
            int mismatches=0;
            for(int i=0;i<first.Length;i++)if((first[i].a>0)!=visible(depth[i])||(first[i].a>0)!=visible(normals[i])||(first[i].a>0)!=visible(oct[i]))mismatches++;
            Check(mismatches<20,renderer.name+" forward/depth/raw-normal/oct-normal silhouettes agree ("+mismatches+" mismatches)");
            TimedDraw(renderer,2,"ShadowCaster");
            Check(renderer.sharedMaterial.renderQueue<=2500,renderer.name+" uses opaque/cutout depth ordering");
        }
        static void TestImpactTracking(GameObject root,MeshRenderer water,Camera camera)
        {
            var sheet=root.transform.Find("Waterfall Sheet").GetComponent<MeshRenderer>();
            var binding=water.GetComponent<MoebiusWaterfallImpactBinding>();
            Check(binding&&binding.waterfallSheet==sheet,"Sample saves an automatic impact binding to the correct sheet");
            if(!binding)return;
            var material=water.sharedMaterial;var source=sheet.sharedMaterial;
            var size=source.GetVector("_SheetSize");var a=material.GetVector("_ImpactPointA");var b=material.GetVector("_ImpactPointB");
            var position=root.transform.position;var rotation=root.transform.rotation;var scale=root.transform.localScale;
            var localPosition=sheet.transform.localPosition;
            float debug=material.GetFloat("_DebugView");
            var other=new GameObject("Unbound shared water").AddComponent<MeshRenderer>();other.sharedMaterial=material;
            var block=new MaterialPropertyBlock();
            Func<bool> matches=()=>
            {
                water.GetPropertyBlock(block,0);var s=source.GetVector("_SheetSize");
                return Vector3.Distance(block.GetVector("_ImpactPointA"),sheet.transform.TransformPoint(new Vector3(-s.x*.5f,-s.y*.5f,0)))<.00001f&&
                    Vector3.Distance(block.GetVector("_ImpactPointB"),sheet.transform.TransformPoint(new Vector3(s.x*.5f,-s.y*.5f,0)))<.00001f;
            };
            try
            {
                binding.SynchronizeNow();material.SetFloat("_DebugView",7);
                var baseline=TimedDraw(water,5,"WaterForward");
                root.transform.position+=new Vector3(2,0,-1.3f);
                root.transform.rotation=Quaternion.Euler(0,27,0);root.transform.localScale=new Vector3(1.15f,.9f,1.1f);
                // Exercise the production render callback, not just an explicit test-only sync call.
                Capture(camera,"ImpactTrackingParent",900,700);
                Check(matches(),"Impact automatically follows parent translation, rotation and nonuniform scale before camera rendering");
                Check(!baseline.SequenceEqual(TimedDraw(water,5,"WaterForward")),"Rendered GPU impact moves with live endpoints, with no material asset writes");
                source.SetVector("_SheetSize",new Vector4(size.x*.8f,size.y*1.1f,0,0));
                sheet.transform.localPosition+=new Vector3(.4f,.2f,0);
                Capture(camera,"ImpactTrackingSize",900,700);
                Check(matches(),"Impact automatically follows direct sheet movement and edited Sheet Size");
                Check(material.GetVector("_ImpactPointA")==a&&material.GetVector("_ImpactPointB")==b,"Automatic tracking preserves saved material endpoint values");
                other.GetPropertyBlock(block,0);Check(block.isEmpty,"A second renderer sharing the material receives no live impact overrides");
                material.SetFloat("_DebugView",debug);Capture(camera,"ImpactTrackingMoved",1500,1000);
                binding.enabled=false;water.GetPropertyBlock(block,0);
                Check(block.isEmpty,"Disabling the binding restores the original renderer property block");
                binding.enabled=true;Check(matches(),"Re-enabling tracking restores live endpoints immediately");
            }
            finally
            {
                root.transform.SetPositionAndRotation(position,rotation);root.transform.localScale=scale;
                sheet.transform.localPosition=localPosition;source.SetVector("_SheetSize",size);material.SetFloat("_DebugView",debug);
                binding.enabled=true;binding.SynchronizeNow();UnityEngine.Object.DestroyImmediate(other.gameObject);
            }
        }
        static void TestSplashComposition(GameObject root)
        {
            var main=root.transform.Find("Splash Cloud Layer 3").GetComponent<MeshRenderer>();
            var front=root.transform.Find("Splash Cloud Layer 4").GetComponent<MeshRenderer>();
            Check(main.bounds.size.x>main.bounds.size.y*5,"Hero splash mass is compressed and broad (width/height="+(main.bounds.size.x/main.bounds.size.y).ToString("F1")+")");
            Check(front.bounds.size.z>front.bounds.size.y*3,"Foreground foam has a horizontal footprint instead of an upright cloud silhouette");
            Check(front.bounds.min.y<0&&front.bounds.max.y>.1f,"Foreground foam intersects the waterline rather than floating above it");
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r!=root.transform.Find("Waterfall Sheet").GetComponent<MeshRenderer>()))
            {
                var mesh=r.GetComponent<MeshFilter>().sharedMesh;var vertices=mesh.vertices;var triangles=mesh.triangles;
                double volume=0;for(int i=0;i<triangles.Length;i+=3)volume+=Vector3.Dot(vertices[triangles[i]],Vector3.Cross(vertices[triangles[i+1]],vertices[triangles[i+2]]));
                Check(volume>0&&mesh.normals.All(n=>!float.IsNaN(n.x)&&n.sqrMagnitude>.5f),r.name+" replacement mesh has outward winding and finite normals");
            }
        }
        static void TestIntegratedImpact(MeshRenderer water)
        {
            var material=water.sharedMaterial;var saved=new Material(material);
            try
            {
                material.SetFloat("_EnableWaves",0);material.SetFloat("_EnableVariation",0);material.SetFloat("_EnableFlow",0);
                material.SetFloat("_EnableRipples",0);material.SetFloat("_EnableFoam",0);material.SetFloat("_DebugView",0);
                material.SetFloat("_EnableWaterfallImpact",0);
                var baseline=TimedDraw(water,3,"WaterForward","ImpactDisabled");
                material.SetFloat("_EnableWaterfallImpact",1);material.SetFloat("_ImpactTurbulence",0);material.SetFloat("_ImpactRippleIntensity",0);
                Check(baseline.SequenceEqual(TimedDraw(water,3,"WaterForward")),"Zero turbulence and ripple intensity preserve water pixels exactly");
                material.SetFloat("_ImpactTurbulence",saved.GetFloat("_ImpactTurbulence"));material.SetFloat("_ImpactRippleIntensity",saved.GetFloat("_ImpactRippleIntensity"));
                var live=TimedDraw(water,5,"WaterForward","IntegratedImpact");
                Check(!baseline.SequenceEqual(live),"Impact foam and ripples render in the original receiving water mesh");
                Check(live.SequenceEqual(TimedDraw(water,5,"WaterForward")),"World-space impact repeats exactly at fixed time");
                var intervals=new List<int>();var previous=live;
                for(int i=1;i<=12;i++){var next=TimedDraw(water,5+i/60f,"WaterForward");intervals.Add(previous.Zip(next,(a,b)=>a.Equals(b)?0:1).Sum());previous=next;}
                Check(intervals.All(n=>n>0),"Integrated turbulence and ripples change at every sampled 1/60 second");
                checks.Add("Integrated impact changed pixels/frame: min="+intervals.Min()+" mean="+intervals.Average().ToString("F1"));
                material.SetFloat("_DebugView",7);material.SetFloat("_ImpactTurbulence",0);material.SetFloat("_ImpactPhase",0);
                var beforeBirth=TimedDraw(water,8.8f-.0001f,"WaterForward");var afterBirth=TimedDraw(water,8.8f+.0001f,"WaterForward");
                int maxJump=beforeBirth.Zip(afterBirth,(a,b)=>Mathf.Abs(a.r-b.r)).Max();
                Check(maxJump<=4,"Periodic ripple queue has no abrupt birth/removal jump (max channel delta="+maxJump+")");
                material.SetFloat("_ImpactRippleCount",1);material.SetFloat("_ImpactSpawnInterval",4);material.SetFloat("_ImpactLifetime",4);
                material.SetFloat("_ImpactRippleSpeed",1);material.SetFloat("_ImpactDistortion",0);material.SetFloat("_ImpactRippleIntensity",1);
                var early=TimedDraw(water,1.2f,"WaterForward","RippleEarly");var later=TimedDraw(water,2.2f,"WaterForward","RippleLater");
                var probe=new Material(Shader.Find("Hidden/Darwin/Validation Waterfall World Probe"));
                var positions=TimedDraw(water,0,"WorldProbe",null,false,probe);UnityEngine.Object.DestroyImmediate(probe);
                var a=material.GetVector("_ImpactPointA");var b=material.GetVector("_ImpactPointB");
                var start=new Vector2(a.x,a.z);var end=new Vector2(b.x,b.z);var ab=end-start;
                Func<int,float> distance=index=>
                {
                    var packed=positions[index];var p=new Vector2((packed.r/255f-.5f)*32,(packed.g/255f-.5f)*32);
                    float u=Mathf.Clamp01(Vector2.Dot(p-start,ab)/ab.sqrMagnitude);return Vector2.Distance(p,start+ab*u);
                };
                Func<Color32[],double> meanRadius=pixels=>
                {
                    double weight=0,sum=0;for(int i=0;i<pixels.Length;i++)if(positions[i].a>0){weight+=pixels[i].r;sum+=pixels[i].r*distance(i);}return sum/Math.Max(weight,1);
                };
                double r1=meanRadius(early),r2=meanRadius(later);
                Check(r2-r1>.65&&r2-r1<1.35,"GPU wave propagates outward about one metre in one second ("+r1.ToString("F2")+" -> "+r2.ToString("F2")+")");
                int ringPixels=0,arcPixels=0;float expected=material.GetFloat("_ImpactRadius")*.28f+2.2f;
                for(int i=0;i<later.Length;i++)if(positions[i].a>0&&Mathf.Abs(distance(i)-expected)<.18f){ringPixels++;if(later[i].r>3)arcPixels++;}
                float coverage=(float)arcPixels/Math.Max(ringPixels,1);
                Check(coverage>.005f&&coverage<.6f,"Ripple consists of broken arc coverage rather than a complete ring ("+coverage.ToString("P1")+")");
                var endOfLife=TimedDraw(water,3.9999f,"WaterForward");var nextBirth=TimedDraw(water,4,"WaterForward");
                Check(endOfLife.Where((p,i)=>positions[i].a>0).All(p=>p.r<2)&&nextBirth.Where((p,i)=>positions[i].a>0).All(p=>p.r<2),"Old wave fades to zero before new wave is born");
            }
            finally{EditorUtility.CopySerialized(saved,material);UnityEngine.Object.DestroyImmediate(saved);}
        }
        static void TestReceivingShadows(GameObject root,Camera camera)
        {
            var renderers=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            var materials=renderers.Select(r=>r.sharedMaterial).Where(m=>m).Distinct().ToArray();
            var saved=materials.Select(m=>new Material(m)).ToArray();
            foreach(var mat in materials)
                foreach(string property in new[]{"_FlowSpeed","_SheetWobbleStrength","_PulseAmplitude","_WobbleStrength","_WaveHeight","_EnableFlow","_RippleSpeed","_RippleWobbleStrength","_EnableWaterfallImpact","_EnableFoam"})
                    if(mat.HasProperty(property))mat.SetFloat(property,0);
            camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            var light=GameObject.Find("Sun").GetComponent<Light>();var caster=GameObject.CreatePrimitive(PrimitiveType.Cube);
            caster.name="Temporary shadow test caster";caster.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.ShadowsOnly;
            try
            {
                foreach(string name in new[]{"Waterfall Sheet","Splash Cloud Layer 4"})
                {
                    var receiver=root.transform.Find(name).GetComponent<MeshRenderer>();var mat=receiver.sharedMaterial;
                    caster.transform.position=receiver.bounds.center-light.transform.forward*3;
                    caster.transform.localScale=new Vector3(1.6f,1.2f,1.5f);
                    mat.SetFloat("_ReceiveWaterShadows",0);var off=Capture(camera,name.Replace(' ','_')+"_ShadowOff",900,700);
                    mat.SetFloat("_ReceiveWaterShadows",1);mat.SetFloat("_WaterShadowStrength",0);
                    Check(off.SequenceEqual(Capture(camera,name.Replace(' ','_')+"_ShadowZero",900,700)),name+" shadow strength zero matches disabled pixels");
                    mat.SetFloat("_WaterShadowStrength",.65f);var on=Capture(camera,name.Replace(' ','_')+"_ShadowOn",900,700);
                    int changed=off.Zip(on,(a,b)=>a.Equals(b)?0:1).Sum();
                    Check(changed>40,name+" receives real main-light cast shadows ("+changed+" pixels)");
                    caster.transform.position+=Vector3.right*1.6f;
                    Check(!on.SequenceEqual(Capture(camera,name.Replace(' ','_')+"_ShadowMoved",900,700)),name+" shadow follows moving caster");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(caster);
                for(int i=0;i<materials.Length;i++){EditorUtility.CopySerialized(saved[i],materials[i]);UnityEngine.Object.DestroyImmediate(saved[i]);}
                camera.GetUniversalAdditionalCameraData().SetRenderer(1);
            }
        }
        static void TestDownwardFlow(MeshRenderer renderer)
        {
            var material=renderer.sharedMaterial;float speed=material.GetFloat("_FlowSpeed");float scale=material.GetFloat("_FlowScale");
            const float dt=.25f;const float y=2;
            float before=y*scale+2*speed*scale;
            float displaced=(y-speed*dt)*scale+(2+dt)*speed*scale;
            Check(Mathf.Abs(before-displaced)<.00001f&&speed>0,"Positive sheet flow follows a feature towards lower local Y");
        }
        static Color32[] Capture(Camera camera,string name,int width,int height)
        {
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;camera.Render();camera.Render();
            var old=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());
            var pixels=image.GetPixels32();camera.targetTexture=null;RenderTexture.active=old;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);return pixels;
        }
    }
}
