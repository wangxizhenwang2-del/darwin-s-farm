using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Darwin.Rendering.Editor
{
    // Optional authoring and GPU verification, outside Assets in the main project.
    public static class MoebiusLavaValidation
    {
        const string Output="Validation/Lava",Root="Assets/Rendering/Moebius/Samples/Lava";
        const string MaterialPath="Assets/Rendering/Moebius/Content/Lava/Materials/MoebiusLava.mat";
        const string ScenePath=Root+"/MoebiusLavaDemo.unity";
        static readonly List<string> checks=new List<string>(),errors=new List<string>(),warnings=new List<string>();
        static MeshRenderer lava;
        static Camera camera;
        public static void RunBatch()
        {
            Directory.CreateDirectory(Output);
            Application.logMessageReceived+=(message,stack,type)=>
            {
                if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);
                if(type==LogType.Warning)warnings.Add(message);
            };
            try
            {
                if(!Application.isBatchMode||!File.Exists("LavaValidation.marker"))throw new Exception("Use an isolated validation project.");
                ShaderUtil.allowAsyncCompilation=false;
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                CreateDemo();
                Check(camera.GetUniversalAdditionalCameraData().renderPostProcessing==false,"Demo renders without Bloom or post processing");
                Capture("Overview");
                var position=camera.transform.position;var rotation=camera.transform.rotation;float zoom=camera.orthographicSize;
                camera.transform.position=new Vector3(4,7,-6);camera.transform.LookAt(new Vector3(0,.2f,0));camera.orthographicSize=4.8f;Capture("Closeup");
                camera.transform.position=new Vector3(-9,6,-9);camera.transform.LookAt(Vector3.zero);camera.orthographicSize=7.5f;Capture("CameraLeft");
                camera.transform.position=new Vector3(0,16,0);camera.transform.rotation=Quaternion.Euler(90,0,0);camera.orthographicSize=6;Capture("TopView");
                camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=zoom;
                TestPatternAndMotion();TestControls();TestPasses();TestLightingAndShadows();
                foreach(string name in new[]{"Darwin/Moebius Lava","Darwin/Moebius NPR","Darwin/Moebius Water","Darwin/Moebius Waterfall","Darwin/Moebius Waterfall Splash"})
                {
                    var shader=Shader.Find(name);
                    Check(shader&&!ShaderUtil.ShaderHasError(shader),name+" compiles without errors");
                    foreach(var item in ShaderUtil.GetShaderMessages(shader))
                        if(item.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)errors.Add(name+": "+item.message);
                }
                var batcher=typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityCode",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                Check(batcher!=null&&Convert.ToInt32(batcher.Invoke(null,new object[]{lava.sharedMaterial.shader,0}))==0,"Lava maintains SRP Batcher compatible material layout");
                Check(lava.GetComponents<MonoBehaviour>().Length==0,"Lava animation requires no runtime controller or per-frame material writes");
                AssetDatabase.SaveAssets();
                EditorSceneManager.OpenScene(ScenePath);
                camera=Camera.main;lava=GameObject.Find("Moebius Lava Surface").GetComponent<MeshRenderer>();
                Check(lava.sharedMaterial&&lava.GetComponent<MeshFilter>().sharedMesh,"Saved lava scene reopens with valid material and mesh references");
                Capture("Reopened");
                EditorSceneManager.OpenScene("Assets/Rendering/Moebius/Samples/Waterfall/MoebiusWaterfallDemo.unity");
                Check(GameObject.Find("MoebiusWaterfall")!=null,"Existing waterfall scene still opens without rebuilding its resources");
                File.WriteAllText(Output+"/Warnings.txt",string.Join("\n",warnings.Distinct()));
            }
            catch(Exception exception){errors.Add(exception.ToString());}
            File.WriteAllText(Output+"/Report.txt",string.Join("\n",checks)+"\nErrors: "+errors.Count+"\n"+string.Join("\n",errors));
            EditorApplication.Exit(errors.Count==0?0:1);
        }
        static void Check(bool ok,string message){checks.Add((ok?"PASS: ":"FAIL: ")+message);if(!ok)errors.Add(message);}
        static Material NewMaterial(string path,string name)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));AssetDatabase.Refresh();
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);if(existing)return existing;
            var material=new Material(Shader.Find(name));MaterialEditor.ApplyMaterialPropertyDrawers(material);
            material.name=Path.GetFileNameWithoutExtension(path);AssetDatabase.CreateAsset(material,path);return material;
        }
        static void CreateDemo()
        {
            Directory.CreateDirectory(Root+"/Meshes");Directory.CreateDirectory(Root+"/Materials");AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var material=NewMaterial(MaterialPath,"Darwin/Moebius Lava");
            var rock=NewMaterial(Root+"/Materials/VolcanicRock.mat","Darwin/Moebius NPR");
            rock.SetColor("_BaseColor",new Color(.42f,.34f,.40f));rock.SetFloat("_UseHatching",1);rock.EnableKeyword("_USE_HATCHING");
            rock.SetFloat("_UseTriplanar",1);rock.EnableKeyword("_USE_TRIPLANAR");rock.SetFloat("_HatchScale",8);rock.SetFloat("_HatchStrength",.5f);rock.SetFloat("_HighlightStrength",0);
            var ground=NewMaterial(Root+"/Materials/AshPaperGround.mat","Darwin/Moebius NPR");
            ground.SetColor("_BaseColor",new Color(.73f,.64f,.52f));ground.SetFloat("_HighlightStrength",0);
            ground.SetFloat("_UseHatching",1);ground.EnableKeyword("_USE_HATCHING");ground.SetFloat("_UseTriplanar",1);ground.EnableKeyword("_USE_TRIPLANAR");ground.SetFloat("_HatchScale",7);ground.SetFloat("_HatchStrength",.16f);
            var pool=CreatePool();string meshPath=Root+"/Meshes/LavaPool.asset";
            var old=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(old){EditorUtility.CopySerialized(pool,old);UnityEngine.Object.DestroyImmediate(pool);pool=old;}else AssetDatabase.CreateAsset(pool,meshPath);
            var go=new GameObject("Moebius Lava Surface");go.transform.position=Vector3.up*.22f;
            go.AddComponent<MeshFilter>().sharedMesh=pool;lava=go.AddComponent<MeshRenderer>();lava.sharedMaterial=material;lava.shadowCastingMode=ShadowCastingMode.On;
            var baseObject=GameObject.CreatePrimitive(PrimitiveType.Cube);baseObject.name="Ash Plateau";baseObject.transform.position=new Vector3(0,-.24f,0);
            baseObject.transform.localScale=new Vector3(15,.45f,12);baseObject.GetComponent<Renderer>().sharedMaterial=ground;UnityEngine.Object.DestroyImmediate(baseObject.GetComponent<Collider>());
            for(int i=0;i<11;i++)
            {
                float angle=(float)i/11*Mathf.PI*2;
                var stone=GameObject.CreatePrimitive(PrimitiveType.Sphere);stone.name="Volcanic Bank "+(i+1);
                stone.transform.position=new Vector3(Mathf.Cos(angle)*6.05f,.35f+Mathf.Sin(i*2.7f)*.12f,Mathf.Sin(angle)*4.65f);
                stone.transform.localScale=new Vector3(1.35f+Mathf.Sin(i*1.7f)*.4f,.85f+Mathf.Cos(i*2.3f)*.3f,1.1f+Mathf.Sin(i)*.3f);
                stone.GetComponent<Renderer>().sharedMaterial=rock;UnityEngine.Object.DestroyImmediate(stone.GetComponent<Collider>());
            }
            var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(48,-35,0);
            camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";
            camera.transform.position=new Vector3(10,12,-13);camera.transform.LookAt(Vector3.zero);camera.orthographic=true;camera.orthographicSize=8.5f;
            camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.allowHDR=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.48f,.76f,.79f);
            var data=camera.GetUniversalAdditionalCameraData();data.SetRenderer(1);data.renderPostProcessing=false;
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);AssetDatabase.SaveAssets();
        }
        static Mesh CreatePool()
        {
            const int count=128;var vertices=new Vector3[count+1];var normals=new Vector3[count+1];var uv=new Vector2[count+1];var triangles=new int[count*3];
            normals[0]=Vector3.up;uv[0]=Vector2.one*.5f;
            for(int i=0;i<count;i++)
            {
                float angle=(float)i/count*Mathf.PI*2;float variation=1+.05f*Mathf.Sin(angle*3+1.2f)+.035f*Mathf.Sin(angle*7);
                vertices[i+1]=new Vector3(Mathf.Cos(angle)*5.8f*variation,0,Mathf.Sin(angle)*4.2f*variation);
                normals[i+1]=Vector3.up;uv[i+1]=new Vector2(vertices[i+1].x/12+.5f,vertices[i+1].z/9+.5f);
                triangles[i*3]=0;triangles[i*3+1]=(i+1)%count+1;triangles[i*3+2]=i+1;
            }
            var mesh=new Mesh{name="LavaPool",vertices=vertices,normals=normals,uv=uv,triangles=triangles};mesh.RecalculateBounds();return mesh;
        }
        static Color32[] Capture(string name,int width=1500,int height=1000)
        {
            // An sRGB output target matches the Game view; Half targets are linear-only and
            // would encode an incorrectly dark PNG without an explicit display conversion.
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;camera.Render();camera.Render();
            var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();var result=image.GetPixels32();
            File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());
            RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);return result;
        }
        static Color32[] Draw(float time,string pass="LavaForward",string name=null,bool oct=false,Mesh mesh=null)
        {
            const int width=640;var target=new RenderTexture(width,width,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();
            var testCamera=new GameObject("GPU test camera").AddComponent<Camera>();testCamera.transform.position=new Vector3(0,15,0);testCamera.transform.rotation=Quaternion.Euler(90,0,0);
            testCamera.orthographic=true;testCamera.orthographicSize=6.8f;testCamera.aspect=1;testCamera.nearClipPlane=.1f;testCamera.farClipPlane=50;
            var view=testCamera.worldToCameraMatrix;var projection=GL.GetGPUProjectionMatrix(testCamera.projectionMatrix,true);
            var command=new CommandBuffer();command.SetRenderTarget(target);command.ClearRenderTarget(true,true,new Color(1,0,1,0));
            command.SetViewProjectionMatrices(view,projection);command.SetGlobalMatrix("unity_MatrixVP",projection*view);command.SetGlobalMatrix("unity_MatrixV",view);command.SetGlobalMatrix("glstate_matrix_projection",projection);
            command.SetGlobalVector("_WorldSpaceCameraPos",testCamera.transform.position);command.SetGlobalVector("_ScaledScreenParams",new Vector4(width,width,1+1f/width,1+1f/width));
            command.SetGlobalVector("_Time",new Vector4(time/20,time,time*2,time*3));command.SetGlobalVector("_MainLightPosition",new Vector4(.2f,.9f,.3f,0));command.SetGlobalVector("_MainLightColor",Vector4.one);
            command.SetGlobalVector("_LightDirection",new Vector4(.2f,.9f,.3f,0));command.SetGlobalVector("_ShadowBias",Vector4.zero);
            foreach(string keyword in new[]{"_MAIN_LIGHT_SHADOWS","_MAIN_LIGHT_SHADOWS_CASCADE","_MAIN_LIGHT_SHADOWS_SCREEN"})command.DisableShaderKeyword(keyword);
            if(oct)command.EnableShaderKeyword("_GBUFFER_NORMALS_OCT");else command.DisableShaderKeyword("_GBUFFER_NORMALS_OCT");
            int passIndex=lava.sharedMaterial.FindPass(pass);if(passIndex<0)throw new Exception("Missing pass: "+pass);
            command.DrawMesh(mesh?mesh:lava.GetComponent<MeshFilter>().sharedMesh,lava.localToWorldMatrix,lava.sharedMaterial,0,passIndex);
            Graphics.ExecuteCommandBuffer(command);command.Release();
            var prior=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(width,width,TextureFormat.RGBA32,false,true);image.ReadPixels(new Rect(0,0,width,width),0,0);image.Apply();
            var result=image.GetPixels32();if(name!=null)File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());
            RenderTexture.active=prior;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(testCamera.gameObject);
            Shader.DisableKeyword("_GBUFFER_NORMALS_OCT");return result;
        }
        static int Difference(Color32[] a,Color32[] b){return a.Zip(b,(x,y)=>x.Equals(y)?0:1).Sum();}
        static void Restore(Material saved){EditorUtility.CopySerialized(saved,lava.sharedMaterial);UnityEngine.Object.DestroyImmediate(saved);}
        static void TestPatternAndMotion()
        {
            var material=lava.sharedMaterial;var saved=new Material(material);
            try
            {
                material.SetFloat("_DebugView",1);var crust=Draw(5,name:"CrustMask");
                float coverage=(float)crust.Where(c=>c.a>0).Average(c=>c.r/255f);
                Check(coverage>.4f&&coverage<.85f,"Readable crust and molten area coverage (crust "+coverage.ToString("P1")+")");
                Check(crust.Where(c=>c.a>0).Count(c=>c.r>245)>10000&&crust.Where(c=>c.a>0).Count(c=>c.r<10)>10000,"Both large solid crust regions and clear open molten channels exist");
                Check(crust.SequenceEqual(Draw(5)),"Crust pattern repeats exactly at fixed time");
                Check(Difference(crust,Draw(13,name:"CrustLater"))>3000,"Crust drifts/deforms visibly over eight seconds");
                var changes=new List<int>();var previous=crust;
                for(int i=1;i<=12;i++){var next=Draw(5+i/60f);changes.Add(Difference(previous,next));previous=next;}
                Check(changes.All(c=>c>0),"Crust changes continuously at every sampled 1/60 second");checks.Add("Crust changed pixels/frame: "+changes.Min()+" / mean "+changes.Average().ToString("F1")+" / max "+changes.Max());
                material.SetFloat("_CrustDriftSpeed",0);material.SetFloat("_CrustDeformationStrength",0);
                Check(Draw(5).SequenceEqual(Draw(13)),"Crust freezes independently of molten flow when drift and deformation are zero");
                material.SetFloat("_DebugView",4);var flow=Draw(5,name:"FlowMask");
                Check(Difference(flow,Draw(8))>5000,"Molten layer keeps flowing while crust is frozen");
                material.SetFloat("_LavaFlowSpeed",0);
                Check(Draw(5).SequenceEqual(Draw(8)),"Molten advection freezes independently when its flow speed is zero");
                material.SetFloat("_CrustDriftSpeed",saved.GetFloat("_CrustDriftSpeed"));material.SetFloat("_CrustDeformationStrength",saved.GetFloat("_CrustDeformationStrength"));material.SetFloat("_DebugView",1);
                Check(Difference(Draw(5),Draw(8))>1000,"Crust still evolves with molten advection disabled");
                material.SetFloat("_DebugView",4);material.SetFloat("_LavaFlowSpeed",.3f);material.SetVector("_FlowDirection",Vector4.zero);
                Check(Difference(Draw(5),Draw(8))>1000,"Zero direction has a stable default downstream direction");
                // World mapping requires no UVs: replacing the mesh's UV0 must not change composite pixels.
                EditorUtility.CopySerialized(saved,material);var source=lava.GetComponent<MeshFilter>().sharedMesh;var copy=UnityEngine.Object.Instantiate(source);copy.uv=new Vector2[copy.vertexCount];
                Check(Draw(5).SequenceEqual(Draw(5,mesh:copy)),"Default lava composite is independent of mesh UV0");UnityEngine.Object.DestroyImmediate(copy);
                var full=Draw(5,name:"CompositeStart");var intervals=new List<int>();previous=full;
                for(int i=1;i<=8;i++){var next=Draw(5+i/60f);intervals.Add(Difference(previous,next));previous=next;}
                Check(intervals.All(c=>c>0),"Composite animation has no held frames at sampled 60 Hz");Draw(13,name:"CompositeLater");
            }
            finally{Restore(saved);}
        }
        static void TestControls()
        {
            var material=lava.sharedMaterial;var saved=new Material(material);
            try
            {
                material.SetFloat("_DebugView",1);material.SetFloat("_CrustCoverage",.5f);float sparse=(float)Draw(5).Where(c=>c.a>0).Average(c=>c.r/255f);
                material.SetFloat("_CrustCoverage",.95f);float dense=(float)Draw(5).Where(c=>c.a>0).Average(c=>c.r/255f);
                Check(dense>sparse+.1f,"Crust Coverage controls island area monotonically");
                material.SetFloat("_CrustCoverage",saved.GetFloat("_CrustCoverage"));material.SetFloat("_CrackWidth",.03f);float thin=(float)Draw(5).Where(c=>c.a>0).Average(c=>c.r/255f);
                material.SetFloat("_CrackWidth",.3f);float thick=(float)Draw(5).Where(c=>c.a>0).Average(c=>c.r/255f);
                Check(thin>thick+.1f,"Crack Width opens wider molten channels");
                EditorUtility.CopySerialized(saved,material);material.SetFloat("_EnableEmission",0);var off=Draw(5,name:"EmissionOff");
                material.SetFloat("_EnableEmission",1);material.SetFloat("_EmissionIntensity",0);Check(off.SequenceEqual(Draw(5)),"Emission zero strength exactly matches disabled pixels");
                material.SetFloat("_EmissionIntensity",.8f);var on=Draw(5,name:"EmissionOn");Check(Difference(off,on)>1000,"Emission enhances hot regions without Bloom");
                material.SetFloat("_DebugView",1);var crust=Draw(5);int touchedCrust=Enumerable.Range(0,crust.Length).Count(i=>crust[i].a>0&&crust[i].r==255&&!off[i].Equals(on[i]));
                Check(touchedCrust==0,"Emission does not brighten fully cooled crust");
                EditorUtility.CopySerialized(saved,material);material.SetFloat("_DebugView",5);var hatch=Draw(5,name:"HatchingMask");
                Check(hatch.Any(c=>c.a>0&&c.r>8),"Shared hand-drawn hatch utility adds visible crust ink");
                int hotHatch=Enumerable.Range(0,crust.Length).Count(i=>crust[i].a>0&&crust[i].r==0&&hatch[i].r>0);Check(hotHatch==0,"Hatching is excluded from fully molten regions");
                material.SetFloat("_UseHatching",0);Check(Draw(5).Where(c=>c.a>0).All(c=>c.r==0),"Hatching toggle cleanly disables its mask");
                material.SetFloat("_DebugView",6);Check(Draw(5,name:"StippleMask").Any(c=>c.a>0&&c.r>2),"Shared stipple utility works on crust");
                material.SetFloat("_StippleStrength",0);Check(Draw(5).Where(c=>c.a>0).All(c=>c.r==0),"Zero stipple strength disables its mask");
            }
            finally{Restore(saved);}
        }
        static void TestPasses()
        {
            Color32[] forward=Draw(5),depth=Draw(5,"DepthOnly"),normal=Draw(5,"DepthNormals",name:"NormalsRaw"),oct=Draw(5,"DepthNormals",name:"NormalsOct",oct:true);
            Func<Color32,bool> visible=c=>c.r!=255||c.g!=0||c.b!=255;
            int mismatch=Enumerable.Range(0,forward.Length).Count(i=>(forward[i].a>0)!=visible(depth[i])||(forward[i].a>0)!=visible(normal[i])||(forward[i].a>0)!=visible(oct[i]));
            Check(mismatch==0,"Forward, depth, raw normals and oct normals use identical opaque geometry");
            Draw(5,"ShadowCaster");
            Check(lava.sharedMaterial.renderQueue<=2500&&lava.sharedMaterial.FindPass("ShadowCaster")>=0,"Opaque depth order and URP ShadowCaster pass are present");
            Check(normal.SequenceEqual(Draw(13,"DepthNormals")),"Surface motion never writes animated fake normals into the outline buffer");
            var saved=new Material(lava.sharedMaterial);
            try
            {
                lava.sharedMaterial.SetFloat("_LavaFlowSpeed",0);lava.sharedMaterial.SetFloat("_CrustDriftSpeed",0);lava.sharedMaterial.SetFloat("_CrustDeformationStrength",0);
                camera.GetUniversalAdditionalCameraData().SetRenderer(0);var off=Capture("OutlineOff",900,700);
                camera.GetUniversalAdditionalCameraData().SetRenderer(1);var on=Capture("OutlineOn",900,700);
                Check(Difference(off,on)>100,"Existing fullscreen outline renderer is active on lava demo geometry");
            }
            finally{Restore(saved);camera.GetUniversalAdditionalCameraData().SetRenderer(1);}
        }
        static void TestLightingAndShadows()
        {
            var saved=new Material(lava.sharedMaterial);var material=lava.sharedMaterial;
            var sun=GameObject.Find("Sun").GetComponent<Light>();var rotation=sun.transform.rotation;float intensity=sun.intensity;
            var sceneRenderers=UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);var casting=sceneRenderers.Select(r=>r.shadowCastingMode).ToArray();
            GameObject caster=null;
            try
            {
                material.SetFloat("_LavaFlowSpeed",0);material.SetFloat("_CrustDriftSpeed",0);material.SetFloat("_CrustDeformationStrength",0);material.SetFloat("_EnableEmission",0);
                sun.transform.rotation=Quaternion.Euler(25,120,0);Capture("LowAngleLight");sun.intensity=.15f;Capture("DimLight");sun.intensity=intensity;sun.transform.rotation=rotation;
                camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                foreach(var r in sceneRenderers)r.shadowCastingMode=ShadowCastingMode.Off;
                // Full-surface shadow reception must work with hot emission and without crust hatching.
                material.SetFloat("_UseHatching",0);material.SetFloat("_StippleStrength",0);
                material.SetFloat("_EnableEmission",1);material.SetFloat("_EmissionIntensity",saved.GetFloat("_EmissionIntensity"));
                material.SetFloat("_ReceiveLavaShadows",1);material.SetFloat("_LavaShadowStrength",.75f);
                caster=GameObject.CreatePrimitive(PrimitiveType.Cube);caster.name="Temporary shadow caster";var rCaster=caster.GetComponent<MeshRenderer>();rCaster.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
                caster.transform.position=new Vector3(0,.22f,0)-sun.transform.forward*3;caster.transform.localScale=new Vector3(2.8f,1.5f,2.4f);
                rCaster.enabled=false;var off=Capture("ReceiveShadowOff",900,700);rCaster.enabled=true;var on=Capture("ReceiveShadowOn",900,700);
                Check(Difference(off,on)>100,"Full lava surface receives real shadows with emission on and Crust Hatching off");
                material.SetFloat("_DebugView",1);var crust=Capture("ShadowCrustMask",900,700);
                material.SetFloat("_DebugView",2);var molten=Capture("ShadowMoltenMask",900,700);
                material.SetFloat("_DebugView",0);
                int crustChanges=Enumerable.Range(0,on.Length).Count(i=>crust[i].r>250&&molten[i].r<3&&!off[i].Equals(on[i]));
                int moltenChanges=Enumerable.Range(0,on.Length).Count(i=>molten[i].r>250&&crust[i].r<3&&!off[i].Equals(on[i]));
                Check(crustChanges>100,"Shadow affects fully cooled crust ("+crustChanges+" pixels)");
                Check(moltenChanges>100,"Shadow affects fully molten channels after emission ("+moltenChanges+" pixels)");
                material.SetFloat("_ReceiveLavaShadows",0);var disabled=Capture("ShadowDisabled",900,700);
                material.SetFloat("_ReceiveLavaShadows",1);material.SetFloat("_LavaShadowStrength",0);
                Check(disabled.SequenceEqual(Capture("ShadowStrengthZero",900,700)),"Shadow strength zero matches disabled shadow reception exactly");
                material.SetFloat("_ReceiveLavaShadows",0);rCaster.enabled=false;
                var disabledCasterOff=Capture("ShadowDisabledCasterOff",900,700);
                // Other NPR receivers still react to this caster. Compare only the lava,
                // identified by complementary grayscale debug masks, including their blend.
                int disabledChanges=Enumerable.Range(0,on.Length).Count(i=>
                    crust[i].r==crust[i].g&&crust[i].g==crust[i].b&&
                    molten[i].r==molten[i].g&&molten[i].g==molten[i].b&&
                    crust[i].r+molten[i].r>=250&&!disabled[i].Equals(disabledCasterOff[i]));
                Check(disabledChanges==0,"Disabled reception bypasses caster shadows on both lava regions ("+disabledChanges+" changed lava pixels)");
                rCaster.enabled=true;material.SetFloat("_ReceiveLavaShadows",1);material.SetFloat("_LavaShadowStrength",.75f);
                material.SetFloat("_LavaShadowDensity",4);var sparse=Capture("ShadowDensity4",900,700);
                material.SetFloat("_LavaShadowDensity",14);
                Check(Difference(sparse,Capture("ShadowDensity14",900,700))>100,"Shadow hatch density independently changes the receiving ink pattern");
                material.SetFloat("_LavaShadowDensity",8);
                rCaster.shadowCastingMode=ShadowCastingMode.On;rCaster.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/VolcanicRock.mat");
                Capture("ShadowExample",1500,1000);rCaster.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
                caster.transform.position+=Vector3.right*2;Check(Difference(on,Capture("ReceiveShadowMoved",900,700))>100,"Shadow ink follows a moving caster");
                UnityEngine.Object.DestroyImmediate(caster);caster=null;
                lava.enabled=false;var castOff=Capture("CastShadowOff",900,700);lava.enabled=true;lava.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
                var castOn=Capture("CastShadowOn",900,700);Check(Difference(castOff,castOn)>100,"Lava geometry casts real URP shadows on surrounding ground");
            }
            finally
            {
                if(caster)UnityEngine.Object.DestroyImmediate(caster);
                for(int i=0;i<sceneRenderers.Length;i++)sceneRenderers[i].shadowCastingMode=casting[i];lava.enabled=true;
                sun.transform.rotation=rotation;sun.intensity=intensity;camera.GetUniversalAdditionalCameraData().SetRenderer(1);Restore(saved);
            }
        }
    }
}
