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
    public static class MoebiusMountainDressing
    {
        const string B="Assets/Rendering/Moebius/Samples/Biomes/";
        const string Results="Validation/MountainDressing";
        static readonly List<string> Notes=new List<string>(),Errors=new List<string>();
        static readonly Dictionary<string,Material> Materials=new Dictionary<string,Material>();
        static readonly Dictionary<string,GameObject> Props=new Dictionary<string,GameObject>();
        static readonly List<Vector3[]> RockTriangles=new List<Vector3[]>();
        static Vector3 origin;
        static Bounds protectedLava;
        static bool volcano;
        sealed class Geometry
        {
            public readonly List<Vector3> vertices=new List<Vector3>();
            public readonly List<Vector2> uv=new List<Vector2>();
            public readonly List<int>[] triangles;
            public Geometry(int slots){triangles=Enumerable.Range(0,slots).Select(_=>new List<int>()).ToArray();}
            public void Tri(Vector3 a,Vector3 b,Vector3 c,int slot=0)
            {
                if(Vector3.Cross(b-a,c-a).sqrMagnitude<1e-10f)return;
                int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);
                uv.Add(Vector2.zero);uv.Add(Vector2.right);uv.Add(Vector2.up);triangles[slot].AddRange(new[]{i,i+1,i+2});
            }
            public Mesh Save(string name)
            {
                var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.subMeshCount=triangles.Length;
                for(int i=0;i<triangles.Length;i++)mesh.SetTriangles(triangles[i],i);
                mesh.RecalculateNormals();mesh.RecalculateBounds();return SaveAsset(mesh,B+"Meshes/MountainDressing/"+name+".asset");
            }
        }
        static void Folder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        static T SaveAsset<T>(T asset,string path) where T:Object
        {
            Folder(Path.GetDirectoryName(path).Replace('\\','/'));var old=AssetDatabase.LoadAssetAtPath<T>(path);
            if(old){EditorUtility.CopySerialized(asset,old);Object.DestroyImmediate(asset);EditorUtility.SetDirty(old);return old;}
            AssetDatabase.CreateAsset(asset,path);return asset;
        }
        static Material Mat(string name,Color color,bool hatch=true)
        {
            var m=new Material(Shader.Find("Darwin/Moebius NPR")){name=name,enableInstancing=true};m.SetColor("_BaseColor",color);
            m.SetColor("_InkColor",new Color(.12f,.13f,.15f));m.SetFloat("_UseHatching",hatch?1:0);if(hatch)m.EnableKeyword("_USE_HATCHING");
            m.SetFloat("_UseTriplanar",1);m.EnableKeyword("_USE_TRIPLANAR");m.SetFloat("_HatchScale",6);m.SetFloat("_HatchStrength",.38f);
            m.SetFloat("_HighlightStrength",0);m.SetFloat("_PaperLift",.025f);m.SetFloat("_SurfaceTintStrength",.27f);m.SetFloat("_CastShadowHatchStrength",.65f);
            m=SaveAsset(m,B+"Materials/MountainDressing/"+name+".mat");Materials[name]=m;return m;
        }
        static void Palette()
        {
            Mat("Snow",new Color(.91f,.95f,.96f));Mat("SnowRock",new Color(.46f,.52f,.58f));
            Mat("Pine",new Color(.24f,.40f,.39f));Mat("Bark",new Color(.36f,.32f,.33f));
            Mat("Ice",new Color(.53f,.75f,.82f),false);Mat("IceLight",new Color(.69f,.85f,.89f),false);
            Mat("Basalt",new Color(.24f,.24f,.28f));Mat("BasaltTop",new Color(.38f,.35f,.37f));
            Mat("Ash",new Color(.49f,.44f,.43f));Mat("Charcoal",new Color(.23f,.18f,.19f));Mat("VentHeat",new Color(.94f,.40f,.12f),false);
        }
        static GameObject Prop(string name,Geometry geometry,params string[] materialNames)
        {
            var go=new GameObject(name);go.AddComponent<MeshFilter>().sharedMesh=geometry.Save(name);
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=materialNames.Select(n=>Materials[n]).ToArray();renderer.receiveShadows=true;
            Folder(B+"Prefabs/Props");var prefab=PrefabUtility.SaveAsPrefabAsset(go,B+"Prefabs/Props/"+name+".prefab");Object.DestroyImmediate(go);Props[name]=prefab;return prefab;
        }
        static Vector3[] Vertices(Mesh mesh)
        {
            using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))using(var a=new NativeArray<Vector3>(data[0].vertexCount,Allocator.Temp))
            {data[0].GetVertices(a);return a.ToArray();}
        }
        static int[] Indices(Mesh mesh)
        {
            var result=new List<int>();using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))
            for(int sub=0;sub<data[0].subMeshCount;sub++)using(var a=new NativeArray<int>(data[0].GetSubMesh(sub).indexCount,Allocator.Temp))
            {data[0].GetIndices(a,sub,true);result.AddRange(a.ToArray());}return result.ToArray();
        }
        static void BoulderProps()
        {
            var tundra=AssetDatabase.LoadAssetAtPath<GameObject>(B+"Prefabs/Tundra_Colored.prefab");
            for(int variant=0;variant<3;variant++)
            {
                var filter=tundra.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Rock_0"+(variant+1));
                var vertices=Vertices(filter.sharedMesh).Select(filter.transform.TransformPoint).ToArray();var bounds=new Bounds(vertices[0],Vector3.zero);
                foreach(var p in vertices)bounds.Encapsulate(p);
                var foot=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);float scale=1/Mathf.Max(bounds.size.x,bounds.size.z);
                vertices=vertices.Select(p=>(p-foot)*scale).ToArray();var indices=Indices(filter.sharedMesh);
                foreach(bool snow in new[]{true,false})
                {
                    var g=new Geometry(2);
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                        var normal=Vector3.Cross(b-a,c-a).normalized;
                        bool cap=normal.y>.24f&&(a.y+b.y+c.y)/3>bounds.size.y*scale*.44f;
                        g.Tri(a,b,c,cap?1:0);
                    }
                    Prop((snow?"SnowBoulder_":"BasaltBoulder_")+(variant+1),g,snow?"SnowRock":"Basalt",snow?"Snow":"BasaltTop");
                }
                Notes.Add("Reused Tundra Rock_0"+(variant+1)+" silhouette: "+indices.Length/3+" triangles");
            }
        }
        static void Tube(Geometry g,Vector3 a,Vector3 b,float ra,float rb,int slot,int sides=7,int capSlot=-1)
        {
            var axis=(b-a).normalized;var right=Vector3.Cross(axis,Mathf.Abs(axis.y)>.9f?Vector3.forward:Vector3.up).normalized;var forward=Vector3.Cross(axis,right);
            for(int i=0;i<sides;i++)
            {
                float x=i*Mathf.PI*2/sides,y=(i+1)*Mathf.PI*2/sides;
                var d=right*Mathf.Cos(x)+forward*Mathf.Sin(x);var e=right*Mathf.Cos(y)+forward*Mathf.Sin(y);
                var p=a+d*ra;var q=a+e*ra;var r=b+d*rb;var s=b+e*rb;
                g.Tri(p,q,r,slot);g.Tri(q,s,r,slot);g.Tri(b,r,s,capSlot<0?slot:capSlot);g.Tri(a,q,p,slot);
            }
        }
        static void Pine()
        {
            var g=new Geometry(3);Tube(g,Vector3.zero,new Vector3(.035f,1.7f,0),.10f,.025f,0);
            for(int layer=0;layer<3;layer++)
            {
                float y=.48f+layer*.47f,r=.70f-layer*.17f,h=.95f-layer*.08f;int count=10;
                var tip=new Vector3(.045f*Mathf.Sin(layer+2),y+h,.035f*Mathf.Cos(layer*3));
                for(int i=0;i<count;i++)
                {
                    float a=i*Mathf.PI*2/count,b=(i+1)*Mathf.PI*2/count;
                    Func<float,Vector3> bottom=t=>new Vector3(Mathf.Cos(t)*r*(1+.08f*Mathf.Sin(t*3+layer)),y+.025f*Mathf.Sin(t*4),Mathf.Sin(t)*r);
                    Func<float,Vector3> edge=t=>new Vector3(Mathf.Cos(t)*r*.79f,y+h*.20f+.035f*Mathf.Sin(t*3+layer),Mathf.Sin(t)*r*.79f);
                    var p=bottom(a);var q=bottom(b);var u=edge(a);var v=edge(b);
                    g.Tri(p,u,q,1);g.Tri(q,u,v,1);g.Tri(u,tip,v,2);g.Tri(new Vector3(0,y,0),p,q,1);
                }
            }
            Prop("SnowPine",g,"Bark","Pine","Snow");
        }
        static void Ice()
        {
            var g=new Geometry(2);
            for(int crystal=0;crystal<5;crystal++)
            {
                var center=new Vector3(Mathf.Sin(crystal*2.4f)*.35f,0,Mathf.Cos(crystal*2.4f)*.28f);
                float h=crystal==0?1.25f:.48f+crystal*.10f;float r=crystal==0?.18f:.12f;
                for(int i=0;i<6;i++)
                {
                    float a=i*Mathf.PI/3,b=(i+1)*Mathf.PI/3;
                    var p=center+new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r);var q=center+new Vector3(Mathf.Cos(b)*r,0,Mathf.Sin(b)*r);
                    var u=p+new Vector3(.05f,h*.74f,0);var v=q+new Vector3(.05f,h*.74f,0);var tip=center+new Vector3(.10f,h,.03f);
                    g.Tri(p,u,q,i%2);g.Tri(q,u,v,i%2);g.Tri(u,tip,v,(i+1)%2);
                }
            }
            Prop("IceOutcrop",g,"Ice","IceLight");
        }
        static void Oval(Geometry g,float rx,float rz,float height,int top,int side,int count=16)
        {
            var center=new Vector3(0,height,0);
            for(int i=0;i<count;i++)
            {
                float a=i*Mathf.PI*2/count,b=(i+1)*Mathf.PI*2/count;
                Func<float,Vector3> edge=t=>new Vector3(Mathf.Cos(t)*rx*(1+.09f*Mathf.Sin(t*3+1)),height*.35f,Mathf.Sin(t)*rz*(1+.08f*Mathf.Cos(t*5)));
                var p=edge(a);var q=edge(b);g.Tri(center,q,p,top);g.Tri(p,q,new Vector3(p.x,-.015f,p.z),side);g.Tri(q,new Vector3(q.x,-.015f,q.z),new Vector3(p.x,-.015f,p.z),side);
            }
        }
        static void GroundProps()
        {
            var drift=new Geometry(1);Oval(drift,1.1f,.65f,.15f,0,0);Prop("SnowDrift",drift,"Snow");
            var crust=new Geometry(2);
            var outline=new[]{new Vector2(-1,-.30f),new Vector2(-.65f,-.75f),new Vector2(-.08f,-.67f),new Vector2(.43f,-.56f),new Vector2(1,-.22f),new Vector2(.82f,.28f),new Vector2(.37f,.72f),new Vector2(-.24f,.61f),new Vector2(-.85f,.41f)};
            for(int i=0;i<outline.Length;i++)
            {
                var a=outline[i];var b=outline[(i+1)%outline.Length];var p=new Vector3(a.x,.035f,a.y);var q=new Vector3(b.x,.035f,b.y);
                crust.Tri(new Vector3(0,.045f,0),q,p,0);crust.Tri(p,q,new Vector3(p.x,-.01f,p.z),1);crust.Tri(q,new Vector3(q.x,-.01f,q.z),new Vector3(p.x,-.01f,p.z),1);
            }
            Prop("CooledCrustPlate",crust,"BasaltTop","Basalt");
            var ice=new Geometry(3);int count=18;
            for(int i=0;i<count;i++)
            {
                float a=i*Mathf.PI*2/count,b=(i+1)*Mathf.PI*2/count;
                Func<float,float,Vector3> ring=(t,r)=>new Vector3(Mathf.Cos(t)*r*(1+.12f*Mathf.Sin(t*3+1)),r>.9f?.04f:.055f,Mathf.Sin(t)*r*.72f*(1+.08f*Mathf.Cos(t*4)));
                var p=ring(a,.85f);var q=ring(b,.85f);var u=ring(a,1.08f);var v=ring(b,1.08f);
                ice.Tri(new Vector3(0,.025f,0),q,p,0);ice.Tri(p,q,u,2);ice.Tri(q,v,u,2);
            }
            var crack=new[]{new Vector3(-.65f,.063f,-.10f),new Vector3(-.22f,.063f,.04f),new Vector3(.08f,.063f,-.06f),new Vector3(.62f,.063f,.18f)};
            for(int i=0;i<crack.Length-1;i++)
            {
                var a=crack[i];var b=crack[i+1];var n=Vector3.Cross(Vector3.up,(b-a).normalized)*.012f;
                ice.Tri(a-n,b-n,a+n,2);ice.Tri(a+n,b-n,b+n,2);
            }
            var branchA=new Vector3(-.18f,.064f,.025f);var branchB=new Vector3(-.35f,.064f,.41f);var branchN=new Vector3(.009f,0,.003f);
            ice.Tri(branchA-branchN,branchB-branchN,branchA+branchN,2);ice.Tri(branchA+branchN,branchB-branchN,branchB+branchN,2);
            Prop("FrozenPool",ice,"Ice","IceLight","Snow");
        }
        static void VolcanicProps()
        {
            var columns=new Geometry(2);
            for(int i=0;i<7;i++)
            {
                float h=i==0?1.6f:.6f+(i%3)*.30f;var p=i==0?Vector3.zero:new Vector3(Mathf.Cos(i*Mathf.PI/3)*.49f,0,Mathf.Sin(i*Mathf.PI/3)*.49f);
                Tube(columns,p,p+new Vector3(.03f*Mathf.Sin(i),h,0),.285f,.24f,0,6,1);
            }
            Prop("BasaltColumns",columns,"Basalt","BasaltTop");
            var wood=new Geometry(2);Tube(wood,Vector3.zero,new Vector3(.09f,1.5f,.02f),.13f,.065f,0,7,1);
            Tube(wood,new Vector3(.04f,.65f,0),new Vector3(-.43f,1.12f,.12f),.075f,.025f,0,6,1);
            Tube(wood,new Vector3(.07f,1.05f,0),new Vector3(.41f,1.43f,-.11f),.06f,.028f,0,6,1);
            Prop("CharredSnag",wood,"Charcoal","Ash");
            var vent=new Geometry(3);int count=12;
            for(int i=0;i<count;i++)
            {
                float a=i*Mathf.PI*2/count,b=(i+1)*Mathf.PI*2/count;
                var p=new Vector3(Mathf.Cos(a)*.13f,.04f,Mathf.Sin(a)*.13f);var q=new Vector3(Mathf.Cos(b)*.13f,.04f,Mathf.Sin(b)*.13f);
                var u=new Vector3(Mathf.Cos(a)*.38f,.10f+.04f*Mathf.Sin(a*5),Mathf.Sin(a)*.38f);var v=new Vector3(Mathf.Cos(b)*.38f,.10f+.04f*Mathf.Sin(b*5),Mathf.Sin(b)*.38f);
                vent.Tri(new Vector3(0,.025f,0),q,p,2);vent.Tri(p,q,u,0);vent.Tri(q,v,u,1);
                vent.Tri(u,v,new Vector3(u.x,0,u.z),0);vent.Tri(v,new Vector3(v.x,0,v.z),new Vector3(u.x,0,u.z),0);
            }
            var prefab=Prop("SteamVent",vent,"Basalt","BasaltTop","VentHeat");
            var root=PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
            try
            {
                var child=new GameObject("Quiet Steam");child.transform.SetParent(root.transform,false);child.transform.localPosition=Vector3.up*.18f;
                var ps=child.AddComponent<ParticleSystem>();ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=6351;
                var main=ps.main;main.duration=8;main.loop=true;main.prewarm=true;main.playOnAwake=true;main.maxParticles=10;main.startSpeed=new ParticleSystem.MinMaxCurve(.25f,.45f);
                main.startLifetime=new ParticleSystem.MinMaxCurve(2,3);main.startSize=new ParticleSystem.MinMaxCurve(.5f,.85f);main.simulationSpace=ParticleSystemSimulationSpace.World;
                var emission=ps.emission;emission.rateOverTime=.7f;
                var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Cone;shape.radius=.08f;shape.angle=7;shape.rotation=new Vector3(-90,0,0);
                var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.x=.09f;velocity.y=.08f;velocity.z=.035f;
                var noise=ps.noise;noise.enabled=true;noise.strength=.06f;noise.frequency=.4f;noise.scrollSpeed=.15f;noise.octaveCount=1;
                var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.35f),new Keyframe(.6f,1.3f),new Keyframe(1,1.65f)));
                var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.5f,.2f),new GradientAlphaKey(.3f,.6f),new GradientAlphaKey(0,1)});
                var color=ps.colorOverLifetime;color.enabled=true;color.color=gradient;
                var sheet=ps.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=2;sheet.numTilesY=2;sheet.frameOverTime=0;sheet.startFrame=new ParticleSystem.MinMaxCurve(0,1);
                var mat=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Content/VFX/Highland/Materials/GroundDust.mat")){name="VentSteam"};
                mat.SetColor("_Tint",new Color(.80f,.81f,.78f));mat.SetFloat("_Opacity",.55f);mat.SetFloat("_EdgeSoftness",.12f);mat.SetFloat("_IntersectionDistance",.12f);
                var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=SaveAsset(mat,B+"Materials/MountainDressing/VentSteam.mat");renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=ShadowCastingMode.Off;
                renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV});
                Props["SteamVent"]=PrefabUtility.SaveAsPrefabAsset(root,AssetDatabase.GetAssetPath(prefab));
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        static void Terrain(MeshRenderer surface)
        {
            RockTriangles.Clear();var mesh=surface.GetComponent<MeshFilter>().sharedMesh;var v=Vertices(mesh).Select(surface.transform.TransformPoint).Select(p=>p-origin).ToArray();var t=Indices(mesh);
            for(int i=0;i<t.Length;i+=3)RockTriangles.Add(new[]{v[t[i]],v[t[i+1]],v[t[i+2]]});
        }
        static float Height(float x,float z)
        {
            float height=0;
            foreach(var t in RockTriangles)
            {
                var a=t[0];var b=t[1];var c=t[2];float det=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(det)<1e-8f)continue;
                float u=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/det;float v=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/det;float w=1-u-v;
                if(u>=0&&v>=0&&w>=0)height=Mathf.Max(height,u*a.y+v*b.y+w*c.y);
            }
            return height;
        }
        static GameObject Place(GameObject parent,string prop,float x,float z,Vector3 scale,float yaw,string label=null)
        {
            var world=origin+new Vector3(x,0,z);
            if(volcano&&world.x>protectedLava.min.x&&world.x<protectedLava.max.x&&world.z>protectedLava.min.z&&world.z<protectedLava.max.z)
            {Notes.Add("Skipped protected lava outlet: "+prop+" at "+x+","+z);return null;}
            var go=(GameObject)PrefabUtility.InstantiatePrefab(Props[prop],parent.transform);go.name=label??prop;
            go.transform.localPosition=new Vector3(x,Height(x,z)+.005f,z);go.transform.localRotation=Quaternion.Euler(0,yaw,0);go.transform.localScale=scale;
            return go;
        }
        static void Cluster(GameObject root,bool snow,float x,float z,float size,int seed)
        {
            string prefix=snow?"SnowBoulder_":"BasaltBoulder_";
            Place(root,prefix+(seed%3+1),x,z,new Vector3(size,size*.8f,size*.88f),seed*47);
            Place(root,prefix+((seed+1)%3+1),x+.6f*size,z-.33f*size,Vector3.one*size*.43f,seed*31);
            Place(root,prefix+((seed+2)%3+1),x-.36f*size,z-.38f*size,Vector3.one*size*.28f,seed*61);
        }
        static void Dress(string name)
        {
            string path=B+"Prefabs/"+name+"_Moebius.prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var old=root.transform.Find("Environment Details");if(old)Object.DestroyImmediate(old.gameObject);
                // Snapshot originals before adding details: model/material/VFX bindings are preserved.
                var originals=root.GetComponentsInChildren<MeshRenderer>(true).ToDictionary(r=>r.GetInstanceID(),r=>(r.sharedMaterials.Select(m=>m?m.GetInstanceID():0).ToArray(),r.GetComponent<MeshFilter>().sharedMesh.GetInstanceID()));
                var foundation=root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Biome Foundation");origin=foundation.bounds.center+Vector3.up*foundation.bounds.extents.y;
                volcano=name=="Volcano";var surface=root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name==(volcano?"Rock":"SnowMountain_Moebius"));Terrain(surface);
                if(volcano){protectedLava=root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="LavaLake").bounds;protectedLava.Expand(new Vector3(.8f,0,.8f));}
                var details=new GameObject("Environment Details");details.transform.SetParent(root.transform,false);details.transform.position=origin;details.transform.rotation=Quaternion.identity;
                var s=root.transform.lossyScale;details.transform.localScale=new Vector3(1/s.x,1/s.y,1/s.z);
                if(!volcano)
                {
                    Place(details,"SnowPine",5.8f,5.7f,Vector3.one*1.13f,18);Place(details,"SnowPine",6.7f,4.0f,Vector3.one*.78f,135);Place(details,"SnowPine",4.1f,6.5f,Vector3.one*.78f,78);
                    Place(details,"SnowPine",-6.1f,3.8f,Vector3.one*.95f,245);Place(details,"SnowPine",-6.7f,5.7f,Vector3.one*.70f,31);
                    Place(details,"FrozenPool",5.6f,-5.4f,new Vector3(1.35f,1,1.5f),-20);
                    Place(details,"IceOutcrop",6.6f,-1.6f,Vector3.one*.95f,18);Place(details,"IceOutcrop",-6.5f,-4.7f,Vector3.one*.73f,71);Place(details,"IceOutcrop",6.2f,-6.8f,Vector3.one*.55f,120);
                    Cluster(details,true,-5.6f,-5.7f,1.35f,1);Cluster(details,true,1.4f,-6.8f,.85f,2);Cluster(details,true,6.9f,1.2f,.88f,3);
                    Cluster(details,true,-6.6f,.1f,1.1f,4);Cluster(details,true,1.6f,6.5f,.85f,5);
                    Place(details,"SnowDrift",-2.7f,-5.9f,new Vector3(1.5f,1.2f,.8f),-25);Place(details,"SnowDrift",-5.3f,5.9f,new Vector3(1.1f,1,.8f),38);Place(details,"SnowDrift",5.4f,2.5f,new Vector3(1.2f,1,.8f),110);
                    Place(details,"SnowBoulder_2",4.2f,-6.4f,Vector3.one*.27f,16);Place(details,"SnowBoulder_3",4.6f,-6.1f,Vector3.one*.18f,142);
                    Place(details,"SnowBoulder_1",-3.9f,-6.8f,Vector3.one*.24f,72);Place(details,"SnowBoulder_2",-4.4f,-6.6f,Vector3.one*.16f,21);
                }
                else
                {
                    Place(details,"CooledCrustPlate",-6.0f,-6.0f,new Vector3(1.65f,1,1.25f),18);Place(details,"BasaltColumns",-6.2f,-6.1f,Vector3.one*1.25f,24);
                    Place(details,"CooledCrustPlate",5.7f,-5.9f,new Vector3(1.55f,1,1.2f),-20);Place(details,"BasaltColumns",5.9f,-6.1f,Vector3.one*1.05f,72);
                    Place(details,"CooledCrustPlate",6.1f,5.8f,new Vector3(1.55f,1,1.25f),35);Place(details,"BasaltColumns",6.5f,6.0f,Vector3.one*.8f,15);
                    Cluster(details,false,6.2f,5.1f,1.05f,1);Cluster(details,false,-6.4f,-4.1f,.95f,2);Cluster(details,false,4.1f,-6.8f,.9f,3);
                    Cluster(details,false,7.0f,1.4f,.8f,4);Cluster(details,false,-7.0f,.1f,.75f,5);
                    Place(details,"CooledCrustPlate",3.6f,6.8f,new Vector3(1.1f,1,.85f),45);Place(details,"CharredSnag",3.7f,6.8f,Vector3.one*.85f,65);
                    Place(details,"CharredSnag",-6.7f,-2.1f,Vector3.one*.65f,142);
                    Place(details,"SteamVent",-5.1f,-6.9f,Vector3.one,15);Place(details,"SteamVent",6.7f,3.4f,Vector3.one*.95f,140);
                    var preview=details.AddComponent<Darwin.Rendering.MoebiusParticlePreview>();preview.previewWarmupSeconds=3;preview.particleSystems=details.GetComponentsInChildren<ParticleSystem>(true);
                }
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    if(originals.TryGetValue(r.GetInstanceID(),out var snapshot))
                    {
                        if(!snapshot.Item1.SequenceEqual(r.sharedMaterials.Select(m=>m?m.GetInstanceID():0))||snapshot.Item2!=r.GetComponent<MeshFilter>().sharedMesh.GetInstanceID())throw new Exception("Original renderer changed: "+r.name);
                    }
                Notes.Add(name+": added "+details.transform.childCount+" prop instances; original "+originals.Count+" renderers preserved.");
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        public static void Build()
        {
            try
            {
                Guard();Palette();BoulderProps();Pine();Ice();GroundProps();VolcanicProps();Dress("SnowMountain");Dress("Volcano");AssetDatabase.SaveAssets();
                foreach(string name in new[]{"SnowMountain","Volcano"})
                {
                    var scene=EditorSceneManager.OpenScene(B+"Scenes/"+name+"_Moebius.unity");
                    var systems=Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
                    Physics.SyncTransforms();foreach(var ps in systems)ps.Simulate(name=="SnowMountain"?6:2.2f,false,true,true);
                    Capture(Camera.main,Results+"/"+name+"After.png");
                    var renderers=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);int triangles=0;
                    foreach(var r in renderers)
                    {
                        var filter=r.GetComponent<MeshFilter>();if(!filter||!filter.sharedMesh)throw new Exception("Missing mesh: "+r.name);
                        if(r.transform.GetComponentsInParent<Transform>().Any(t=>t.name=="Environment Details"))triangles+=Indices(filter.sharedMesh).Length/3;
                        foreach(var mat in r.sharedMaterials)
                        {
                            if(!mat||!mat.shader||!mat.shader.isSupported)throw new Exception("Unsupported material: "+r.name);
                            foreach(var error in ShaderUtil.GetShaderMessages(mat.shader))if(error.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)Errors.Add(error.message);
                        }
                    }
                    foreach(var go in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go.gameObject)>0)throw new Exception("Missing script: "+go.name);
                    Notes.Add(name+": added static triangle count="+triangles+"; native particle count="+systems.Sum(p=>p.particleCount)+"; scene not saved/overwritten.");
                }
            }
            catch(Exception e){Errors.Add(e.ToString());}
            Finish("Report.txt");
        }
        static void Guard()
        {
            if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/Temp/")||!File.Exists("MountainDressing.marker"))throw new Exception("Use the isolated fixture.");
            Directory.CreateDirectory(Results);ShaderUtil.allowAsyncCompilation=false;
            Application.logMessageReceived+=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)Errors.Add(m);};
            var rp=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");GraphicsSettings.defaultRenderPipeline=rp;QualitySettings.renderPipeline=rp;
        }
        public static void Probe()
        {
            try
            {
                Guard();
                foreach(var name in new[]{"Volcano","SnowMountain"})
                {
                    EditorSceneManager.OpenScene(B+"Scenes/"+name+"_Moebius.unity");
                    Notes.Add(name+" camera "+Camera.main.transform.position+" target direction "+Camera.main.transform.forward+" size="+Camera.main.orthographicSize);
                    foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))Notes.Add(r.name+" "+r.bounds+" material="+string.Join(",",r.sharedMaterials.Select(m=>m?m.name:"MISSING")));
                    Capture(Camera.main,Results+"/"+name+"Before.png");
                }
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(B+"Prefabs/Tundra_Colored.prefab");
                foreach(var r in asset.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Rock_")).Take(12))Notes.Add("Tundra source "+r.name+" "+r.GetComponent<MeshFilter>().sharedMesh.name);
            }
            catch(Exception e){Errors.Add(e.ToString());}
            Finish("Probe.txt");
        }
        static void Capture(Camera camera,string path)
        {
            var rt=new RenderTexture(1500,1050,24,RenderTextureFormat.ARGB32);rt.Create();var texture=new Texture2D(1500,1050,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;camera.targetTexture=rt;camera.Render();camera.Render();RenderTexture.active=rt;
            texture.ReadPixels(new Rect(0,0,1500,1050),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);
        }
        static void Finish(string name)
        {File.WriteAllLines(Results+"/"+name,Notes.Concat(new[]{"Errors: "+Errors.Count}).Concat(Errors));EditorApplication.Exit(Errors.Count==0?0:1);}
    }
}
