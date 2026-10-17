using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Darwin.Rendering.Validation
{
    [InitializeOnLoad]
    public static class MoebiusBiomeFinishing
    {
        const string B="Assets/Rendering/Moebius/Samples/Biomes/";
        const string Results="Validation/BiomeFinishing";
        static readonly List<string> notes=new List<string>(),errors=new List<string>();
        static double playDeadline;
        static bool early;
        static MoebiusBiomeFinishing()
        {
            EditorApplication.update+=Tick;
            Application.logMessageReceived+=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m);};
        }
        static void Check(bool ok,string message){notes.Add((ok?"PASS: ":"FAIL: ")+message);if(!ok)throw new InvalidOperationException(message);}
        static void Folder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            Folder(Path.GetDirectoryName(path).Replace('\\','/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));
        }
        static void Save(Object asset,string path)
        {
            Folder(Path.GetDirectoryName(path).Replace('\\','/'));
            var existing=AssetDatabase.LoadAssetAtPath<Object>(path);
            if(existing){EditorUtility.CopySerialized(asset,existing);Object.DestroyImmediate(asset);EditorUtility.SetDirty(existing);}
            else AssetDatabase.CreateAsset(asset,path);
        }
        static Bounds BoundsOf(IEnumerable<MeshRenderer> rs)
        {var a=rs.ToArray();var b=a[0].bounds;foreach(var r in a.Skip(1))b.Encapsulate(r.bounds);return b;}
        static Material Ground(string name,Color color)
        {
            var m=new Material(Shader.Find("Darwin/Moebius NPR")){name=name};
            m.SetColor("_BaseColor",color);m.SetFloat("_UseHatching",1);m.EnableKeyword("_USE_HATCHING");
            m.SetFloat("_HatchStrength",.45f);m.SetFloat("_HatchScale",4);m.SetFloat("_UseTriplanar",1);m.EnableKeyword("_USE_TRIPLANAR");
            m.SetFloat("_UseStipple",1);m.EnableKeyword("_USE_STIPPLE");m.SetFloat("_StippleStrength",.025f);
            Save(m,B+"Materials/Foundations/"+name+".mat");return AssetDatabase.LoadAssetAtPath<Material>(B+"Materials/Foundations/"+name+".mat");
        }
        static void Foundation(string name)
        {
            string path=B+"Prefabs/"+name+"_Moebius.prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var old=root.transform.Find("Biome Foundation");if(old)Object.DestroyImmediate(old.gameObject);
                var rs=root.GetComponentsInChildren<MeshRenderer>();var bounds=BoundsOf(rs);
                float top=name=="Volcano"?rs.First(r=>r.name=="Rock").bounds.min.y:bounds.min.y;
                var child=new GameObject("Biome Foundation");child.transform.SetParent(root.transform,false);
                child.transform.position=new Vector3(bounds.center.x,top-.15f,bounds.center.z);
                var s=root.transform.lossyScale;child.transform.localScale=new Vector3(17/s.x,.3f/s.y,17/s.z);
                child.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(B+"Meshes/Foundations/BiomeSlab.asset");
                var r=child.AddComponent<MeshRenderer>();
                Color c=name=="Volcano"?new Color(.48f,.45f,.44f):new Color(.88f,.91f,.92f);
                r.sharedMaterials=new[]{Ground(name+"_Foundation_Top",c),Ground(name+"_Foundation_Edge",c*.85f)};
                Check(Mathf.Abs(r.bounds.size.x-17)<.01f&&Mathf.Abs(r.bounds.size.z-17)<.01f,name+": 17 metre foundation, 0.3 metre slab");
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        static void CreateSlab()
        {
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var source=cube.GetComponent<MeshFilter>().sharedMesh;
            var m=Object.Instantiate(source);m.name="BiomeSlab";var tris=m.triangles;var normals=m.normals;
            var top=new List<int>();var sides=new List<int>();
            for(int i=0;i<tris.Length;i+=3){var target=normals[tris[i]].y>.5f?top:sides;target.AddRange(tris.Skip(i).Take(3));}
            m.subMeshCount=2;m.SetTriangles(top,0);m.SetTriangles(sides,1);
            Save(m,B+"Meshes/Foundations/BiomeSlab.asset");Object.DestroyImmediate(cube);
        }
        static void BuildRiver()
        {
            string path=B+"Prefabs/River.prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                // The imported I/T tiles measure 0.17m. Bring them to the existing 17m biome unit.
                var banks=root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="I"||r.name=="T").ToArray();
                if(banks[0].bounds.size.x<1)root.transform.localScale*=100;
                var total=BoundsOf(root.GetComponentsInChildren<MeshRenderer>());
                root.transform.position-=new Vector3(total.center.x,total.min.y,total.center.z);
                foreach(var bank in banks)
                {
                    var old=bank.transform.Find("Flowing Water");if(old)Object.DestroyImmediate(old.gameObject);
                    var source=bank.GetComponent<MeshFilter>().sharedMesh;
                    var v=Positions(source).Select(bank.transform.TransformPoint).ToArray();var indices=Indices(source);
                    // The channel floor is the horizontal level between the outer ledge and the banks.
                    var levels=v.Select(p=>p.y).OrderBy(y=>y).ToArray();
                    var unique=new List<float>();foreach(float y in levels)if(unique.Count==0||y-unique.Last()>.001f)unique.Add(y);
                    Check(unique.Count==4,bank.name+": separate bottom, ledge, riverbed and upper-bank levels");
                    float bed=bank.name=="I"?unique[2]:unique[1];
                    var floor=new List<int>();
                    for(int i=0;i<indices.Length;i+=3)
                        if(Enumerable.Range(0,3).All(j=>Mathf.Abs(v[indices[i+j]].y-bed)<.001f))floor.AddRange(indices.Skip(i).Take(3));
                    Check(floor.Count>0,bank.name+": real riverbed triangles found");
                    var b=bank.bounds;var origin=new Vector3(b.center.x,bed+.1f,b.center.z);
                    var verts=new List<Vector3>();var triangles=new List<int>();
                    // Extend open channel endpoints across the one metre perimeter ledge, not across banks.
                    Func<Vector3,Vector3> point=p=>{
                        var q=p-origin;q.y=0;
                        if(Mathf.Abs(Mathf.Abs(q.x)-7.5f)<.01f)q.x=Mathf.Sign(q.x)*8.5f;
                        if(bank.name=="T"&&Mathf.Abs(q.z-7.5f)<.01f)q.z=8.5f;
                        return q;};
                    for(int i=0;i<floor.Count;i+=3)
                    {
                        Vector3 a=point(v[floor[i]]),c=point(v[floor[i+1]]),d=point(v[floor[i+2]]);
                        if(Vector3.Cross(c-a,d-a).sqrMagnitude<.00001f)continue;
                        if(Vector3.Cross(c-a,d-a).y<0){var swap=c;c=d;d=swap;}
                        Subdivide(a,c,d,3,verts,triangles);
                    }
                    var mesh=new Mesh{name="River_"+bank.name+"_Water"};mesh.indexFormat=IndexFormat.UInt32;
                    mesh.SetVertices(verts);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                    var mb=mesh.bounds;mb.Expand(new Vector3(0,.25f,0));mesh.bounds=mb;
                    string meshPath=B+"Meshes/River/River_"+bank.name+"_Water.asset";Save(mesh,meshPath);
                    var water=new GameObject("Flowing Water");water.transform.SetParent(bank.transform,false);water.transform.position=origin;water.transform.rotation=Quaternion.identity;
                    var scale=bank.transform.lossyScale;water.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
                    water.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    var renderer=water.AddComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
                    var m=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Content/Defaults/Materials/MoebiusWater.mat")){name="River_"+bank.name+"_Water"};
                    m.SetColor("_WaterColor",new Color(.47f,.71f,.72f));m.SetColor("_FoamColor",new Color(.88f,.95f,.91f));
                    m.SetVector("_FlowDirection",new Vector4(1,.08f,0,0));m.SetVector("_WaveDirection",new Vector4(1,.08f,0,0));
                    m.SetFloat("_FlowSpeed",.65f);m.SetFloat("_RippleSpeed",.18f);m.SetFloat("_WaveHeight",.035f);
                    m.SetFloat("_FoamWidth",.24f);m.SetFloat("_FoamIntensity",.85f);m.SetFloat("_FoamInkStrength",.32f);
                    m.SetFloat("_EnableWaterfallImpact",0);m.SetFloat("_ReceiveWaterShadows",1);
                    string matPath=B+"Materials/River/"+m.name+".mat";Save(m,matPath);m=AssetDatabase.LoadAssetAtPath<Material>(matPath);renderer.sharedMaterial=m;
                    var mapBounds=b;mapBounds.Expand(new Vector3(4,0,4));
                    string texturePath=B+"Textures/River/River_"+bank.name+"_ShoreDistance.png";Folder(B+"Textures/River");
                    var mask=Darwin.Rendering.Editor.MoebiusWaterSetup.Bake(new[]{bank.GetComponent<MeshFilter>()},mapBounds,origin.y,512,3,texturePath);
                    mapBounds.center-=origin;
                    Darwin.Rendering.Editor.MoebiusWaterSetup.AssignShoreline(m,mask,mapBounds,3);
                    m.SetFloat("_ShoreObjectSpace",1);EditorUtility.SetDirty(m);
                    Check(renderer.bounds.size.x>16.9f,bank.name+": water extends to open tile connectors");
                    notes.Add(bank.name+": "+triangles.Count/3+" water triangles; level "+origin.y+"; shore baked from actual banks only");
                }
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        static void Subdivide(Vector3 a,Vector3 b,Vector3 c,int depth,List<Vector3> v,List<int> t)
        {
            if(depth==0){int i=v.Count;v.Add(a);v.Add(b);v.Add(c);t.Add(i);t.Add(i+1);t.Add(i+2);return;}
            var ab=(a+b)*.5f;var bc=(b+c)*.5f;var ca=(c+a)*.5f;
            Subdivide(a,ab,ca,depth-1,v,t);Subdivide(ab,b,bc,depth-1,v,t);Subdivide(ca,bc,c,depth-1,v,t);Subdivide(ab,bc,ca,depth-1,v,t);
        }
        static void Scene(string name)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            string prefab=name=="River"?"River":name+"_Moebius";
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(B+"Prefabs/"+prefab+".prefab"));
            var rs=root.GetComponentsInChildren<MeshRenderer>();var bounds=BoundsOf(rs);
            root.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);bounds=BoundsOf(rs);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.72f,.76f,.8f);RenderSettings.fog=false;
            var light=new GameObject("Directional Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.shadows=LightShadows.Soft;
            light.transform.rotation=Quaternion.Euler(50,-30,0);light.gameObject.AddComponent<UniversalAdditionalLightData>();RenderSettings.sun=light;
            var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.orthographic=true;
            camera.GetUniversalAdditionalCameraData().SetRenderer(1);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.45f,.75f,.79f);
            float span=Mathf.Max(bounds.size.x,bounds.size.z);camera.orthographicSize=name=="River"?span*.43f:span*.68f;
            camera.nearClipPlane=.1f;camera.farClipPlane=250;
            var direction=new Vector3(1.1f,1.2f,-1.4f);
            if(name=="Volcano"){var d=rs.First(r=>r.name=="LavaLake").bounds.center-bounds.center;d.y=0;d.Normalize();direction=d*1.8f+Vector3.up*1.1f;}
            camera.transform.position=bounds.center+direction*span;camera.transform.LookAt(bounds.center);
            Folder(B+"Scenes");EditorSceneManager.SaveScene(scene,B+"Scenes/"+name+"_Moebius.unity");
            Capture(camera,Results+"/"+name+".png");
            foreach(var r in rs)
            {
                Check(r.GetComponent<MeshFilter>().sharedMesh,name+": mesh resolves on "+r.name);
                foreach(var m in r.sharedMaterials)
                {Check(m&&m.shader&&m.shader.isSupported,name+": material supported on "+r.name);
                 foreach(var e in ShaderUtil.GetShaderMessages(m.shader))if(e.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)errors.Add(e.message);}
            }
        }
        public static void RunBatch()
        {
            try
            {
                Guard();ShaderUtil.allowAsyncCompilation=false;
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                CreateSlab();Foundation("SnowMountain");Foundation("Volcano");BuildRiver();AssetDatabase.SaveAssets();
                foreach(string name in new[]{"SnowMountain","Volcano","River"})Scene(name);
                AssetDatabase.SaveAssets();
                CheckShoreTransform();
            }
            catch(Exception e){errors.Add(e.ToString());}
            Finish("Report.txt");
        }
        public static void RunPlay()
        {
            Guard();ShaderUtil.allowAsyncCompilation=false;
            EditorSceneManager.OpenScene(B+"Scenes/River_Moebius.unity");
            SessionState.SetBool("BiomeFinishing.Play",true);EditorApplication.EnterPlaymode();
        }
        static void CheckShoreTransform()
        {
            // Render only the actual shoreline-distance shader output, with all animation disabled.
            var water=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).First(r=>r.name=="Flowing Water");
            foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))if(r!=water)r.enabled=false;
            var material=new Material(water.sharedMaterial);water.sharedMaterial=material;
            material.SetFloat("_EnableWaves",0);material.SetFloat("_DebugView",3);
            var camera=Camera.main;camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            camera.transform.position=water.bounds.center+Vector3.up*40;camera.transform.rotation=Quaternion.Euler(90,0,0);camera.orthographicSize=10;
            Capture(camera,Results+"/ShoreBeforeTransform.png");
            var parent=water.transform.parent;var position=parent.position;
            parent.RotateAround(position,Vector3.up,90);parent.position+=new Vector3(57,0,31);
            camera.transform.RotateAround(position,Vector3.up,90);camera.transform.position+=new Vector3(57,0,31);
            Capture(camera,Results+"/ShoreAfterTransform.png");
            var a=new Texture2D(2,2);var b=new Texture2D(2,2);
            a.LoadImage(File.ReadAllBytes(Results+"/ShoreBeforeTransform.png"));b.LoadImage(File.ReadAllBytes(Results+"/ShoreAfterTransform.png"));
            var ca=a.GetPixels32();var cb=b.GetPixels32();double error=0;
            for(int i=0;i<ca.Length;i++)error+=(Math.Abs(ca[i].r-cb[i].r)+Math.Abs(ca[i].g-cb[i].g)+Math.Abs(ca[i].b-cb[i].b))/(255.0*3);
            error/=ca.Length;notes.Add("Shoreline transform comparison mean RGB error: "+error);
            Check(error<.002,"Shoreline remains attached after translation and 90 degree yaw (rendered GPU comparison)");
            Object.DestroyImmediate(a);Object.DestroyImmediate(b);Object.DestroyImmediate(material);
        }
        static void Tick()
        {
            if(!SessionState.GetBool("BiomeFinishing.Play",false))return;
            if(playDeadline==0)playDeadline=EditorApplication.timeSinceStartup+120;
            if(EditorApplication.timeSinceStartup>playDeadline){errors.Add("Play Mode timeout");Finish("PlayReport.txt");return;}
            if(!EditorApplication.isPlaying||EditorApplication.isPaused)return;
            if(!early&&Time.time>.2f){Capture(Camera.main,Results+"/RiverEarly.png");early=true;}
            if(Time.time<3)return;
            try
            {
                Capture(Camera.main,Results+"/RiverLater.png");
                Check(!File.ReadAllBytes(Results+"/RiverEarly.png").SequenceEqual(File.ReadAllBytes(Results+"/RiverLater.png")),"Native Play Mode river animates with GPU time");
                var waters=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.name=="Flowing Water").ToArray();
                Check(waters.Length==2,"Two channel meshes, no per-frame time controller or extra render feature");
                // Check the local bake at the same points before/after moving and yaw-rotating an instance.
                foreach(var w in waters)
                {
                    var texture=(Texture2D)w.sharedMaterial.GetTexture("_ShorelineMask");Check(texture,"Shore mask retained on "+w.transform.parent.name);
                    Check(w.sharedMaterial.GetFloat("_ShoreObjectSpace")>.5f,"River shoreline follows prefab transforms");
                }
                notes.Add("Play Mode seconds: "+Time.time);
            }
            catch(Exception e){errors.Add(e.ToString());}
            Finish("PlayReport.txt");
        }
        static void Finish(string name)
        {SessionState.SetBool("BiomeFinishing.Play",false);Directory.CreateDirectory(Results);File.WriteAllLines(Results+"/"+name,notes.Concat(new[]{"Errors: "+errors.Count}).Concat(errors));EditorApplication.Exit(errors.Count==0?0:1);}
        static void Capture(Camera camera,string path)
        {
            var rt=new RenderTexture(1600,1100,24,RenderTextureFormat.ARGB32);rt.Create();
            var texture=new Texture2D(1600,1100,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            camera.targetTexture=rt;camera.Render();camera.Render();RenderTexture.active=rt;
            texture.ReadPixels(new Rect(0,0,1600,1100),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);
        }
        [Serializable] public class Geometry {public string name,path,material,paint;public Vector3[] vertices;public Vector2[] uv;public int[] triangles;}
        static void Guard()
        {
            if(!Application.isBatchMode || !File.Exists("BiomeFinishing.marker") || !Application.dataPath.Replace('\\','/').Contains("/Temp/"))
                throw new InvalidOperationException("Use the isolated fixture.");
            Directory.CreateDirectory(Results);
        }
        static Vector3[] Positions(Mesh mesh)
        {
            using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))
            using(var positions=new NativeArray<Vector3>(data[0].vertexCount,Allocator.Temp))
            {data[0].GetVertices(positions);return positions.ToArray();}
        }
        static int[] Indices(Mesh mesh)
        {
            using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var result=new List<int>();
                for(int sub=0;sub<data[0].subMeshCount;sub++)
                using(var indices=new NativeArray<int>(data[0].GetSubMesh(sub).indexCount,Allocator.Temp))
                {data[0].GetIndices(indices,sub,true);result.AddRange(indices.ToArray());}
                return result.ToArray();
            }
        }
        public static void Probe()
        {
            try
            {
                Guard();var notes=new List<string>();
                foreach(var name in new[]{"Tundra_Colored","Grassland_Moebius","SnowMountain_Moebius","Volcano_Moebius","River"})
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                    var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(B+"Prefabs/"+name+".prefab"));
                    notes.Add(name+" root="+root.transform.position+" scale="+root.transform.localScale);
                    foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if(name=="River" && renderer.name!="T" && renderer.name!="I")continue;
                        if(name=="Grassland_Moebius" && !renderer.name.Contains("Land"))continue;
                        if(name=="Tundra_Colored" && !renderer.name.StartsWith("Ground_"))continue;
                        notes.Add("  "+renderer.name+" bounds="+renderer.bounds+" scale="+renderer.transform.lossyScale+" materials="+string.Join(",",renderer.sharedMaterials.Select(m=>m?m.name:"MISSING")));
                        if(name!="River")continue;
                        var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;var vertices=Positions(mesh).Select(renderer.transform.TransformPoint).ToArray();
                        Vector2[] uv;
                        using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))using(var a=new NativeArray<Vector2>(data[0].vertexCount,Allocator.Temp))
                        {data[0].GetUVs(0,a);uv=a.ToArray();}
                        var material=renderer.sharedMaterial;
                        var geometry=new Geometry{name=renderer.name,path=AssetDatabase.GetAssetPath(mesh),material=AssetDatabase.GetAssetPath(material),
                            paint=AssetDatabase.GetAssetPath(material.GetTexture("_PaintColorMap")),vertices=vertices,uv=uv,triangles=Indices(mesh)};
                        File.WriteAllText(Results+"/River_"+renderer.name+".json",JsonUtility.ToJson(geometry,true));
                        notes.Add("  vertex levels="+string.Join(",",vertices.GroupBy(v=>Math.Round(v.y,7)).OrderBy(g=>g.Key).Select(g=>g.Key+":"+g.Count())));
                    }
                }
                File.WriteAllLines(Results+"/Probe.txt",notes);EditorApplication.Exit(0);
            }
            catch(Exception e){File.WriteAllText(Results+"/ProbeError.txt",e.ToString());EditorApplication.Exit(1);}
        }
    }
}
