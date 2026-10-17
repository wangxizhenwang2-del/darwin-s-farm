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
using Object = UnityEngine.Object;

namespace Darwin.Rendering.Validation
{
    // Offline authoring and validation only. Native ParticleSystems own all runtime animation.
    [InitializeOnLoad]
    public static class MoebiusMountainVFXSetup
    {
        const string Biomes = "Assets/Rendering/Moebius/Samples/Biomes/";
        const string VFX = "Assets/Rendering/Moebius/Content/VFX/";
        const string Results = "Validation/MountainRiverVFX";
        const string PlayKey = "Moebius.MountainVFX.Play";
        static readonly List<string> Notes = new List<string>(), Errors = new List<string>();
        static int playStage;
        static double deadline;
        static MoebiusMountainVFXSetup()
        {
            EditorApplication.update += PlayTick;
            Application.logMessageReceived += (m,s,t) => { if(t==LogType.Error || t==LogType.Exception || t==LogType.Assert) Errors.Add(m); };
        }
        static void Guard()
        {
            if(!Application.isBatchMode || !Application.dataPath.Replace('\\','/').Contains("/Temp/") || !File.Exists("MountainVFX.marker"))
                throw new InvalidOperationException("Run only in the isolated, marked Temp project.");
            Directory.CreateDirectory(Results); ShaderUtil.allowAsyncCompilation = false;
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline; QualitySettings.renderPipeline=pipeline;
        }
        static void Check(bool ok,string message)
        { Notes.Add((ok?"PASS: ":"FAIL: ")+message); if(!ok) throw new InvalidOperationException(message); }
        static void Folder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/'); Folder(parent); AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        static T Save<T>(T asset,string path) where T:Object
        {
            Folder(Path.GetDirectoryName(path).Replace('\\','/'));
            var existing=AssetDatabase.LoadAssetAtPath<T>(path);
            if(existing){EditorUtility.CopySerialized(asset,existing);Object.DestroyImmediate(asset);EditorUtility.SetDirty(existing);return existing;}
            AssetDatabase.CreateAsset(asset,path);return asset;
        }
        static GameObject Child(GameObject parent,string name)
        {var child=new GameObject(name);child.transform.SetParent(parent.transform,false);return child;}
        static Vector3[] Vertices(Mesh mesh)
        {
            using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))
            using(var v=new NativeArray<Vector3>(data[0].vertexCount,Allocator.Temp))
            {data[0].GetVertices(v);return v.ToArray();}
        }
        static Mesh CollisionMesh(MeshRenderer surface,Vector3 origin,string folder)
        {
            var source=surface.GetComponent<MeshFilter>().sharedMesh;var mesh=new Mesh{name="MountainCollision"};
            mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=Vertices(source).Select(surface.transform.TransformPoint).Select(p=>p-origin).ToArray();
            var indices=new List<int>();
            using(var data=MeshUtility.AcquireReadOnlyMeshData(source))
            for(int sub=0;sub<data[0].subMeshCount;sub++)
            using(var a=new NativeArray<int>(data[0].GetSubMesh(sub).indexCount,Allocator.Temp))
            {data[0].GetIndices(a,sub,true);indices.AddRange(a.ToArray());}
            mesh.triangles=indices.ToArray();mesh.RecalculateBounds();
            return Save(mesh,folder+"/Meshes/MountainCollision.asset");
        }
        static void Colliders(GameObject rig,MeshRenderer surface,MeshRenderer foundation,Vector3 origin,string folder)
        {
            var helpers=Child(rig,"Particle Collision (Ignore Raycast)"); helpers.layer=2;
            helpers.AddComponent<MeshCollider>().sharedMesh=CollisionMesh(surface,origin,folder);
            var ground=Child(helpers,"Foundation Collision");ground.layer=2;
            var box=ground.AddComponent<BoxCollider>();box.center=foundation.bounds.center-origin;box.size=foundation.bounds.size;
        }
        static ParticleSystem CreateSystem(GameObject root,string name,Material material,uint seed,int cap,bool mesh)
        {
            var go=Child(root,name);var ps=go.AddComponent<ParticleSystem>();ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.duration=12;main.loop=true;main.playOnAwake=true;main.prewarm=false;
            main.simulationSpace=ParticleSystemSimulationSpace.World;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
            main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;main.maxParticles=cap;main.startColor=Color.white;
            main.startSpeed=0;main.startLifetime=3;main.startSize=.2f;
            ps.useAutoRandomSeed=false;ps.randomSeed=seed;
            var emission=ps.emission;emission.rateOverTime=0;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
            renderer.renderMode=mesh?ParticleSystemRenderMode.Mesh:ParticleSystemRenderMode.Billboard;
            renderer.alignment=ParticleSystemRenderSpace.View;renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;renderer.enableGPUInstancing=false;renderer.maxParticleSize=1;
            renderer.sortMode=ParticleSystemSortMode.Distance;
            renderer.SetActiveVertexStreams(mesh?
                new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV}:
                new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV});
            return ps;
        }
        static void Cone(ParticleSystem ps,float radius,float angle)
        {
            var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Cone;shape.radius=radius;shape.angle=angle;
            shape.rotation=new Vector3(-90,0,0);shape.length=.15f;
        }
        static void NativeCollision(ParticleSystem ps)
        {
            var collision=ps.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.World;
            collision.mode=ParticleSystemCollisionMode.Collision3D;collision.collidesWith=1<<2;
            collision.quality=ParticleSystemCollisionQuality.High;collision.enableDynamicColliders=false;
            collision.radiusScale=.15f;collision.lifetimeLoss=1;collision.bounce=0;collision.dampen=1;
        }
        static void Shrink(ParticleSystem ps,params Keyframe[] frames)
        {var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(frames));}
        static void Fade(ParticleSystem ps,float fadeIn=.08f)
        {
            var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,fadeIn),new GradientAlphaKey(1,.7f),new GradientAlphaKey(0,1)});
            var color=ps.colorOverLifetime;color.enabled=true;color.color=g;
        }
        static Material NPR(string folder,string name,Color color,bool hatch)
        {
            var mat=new Material(Shader.Find("Darwin/Moebius NPR")){name=name};mat.SetColor("_BaseColor",color);
            mat.SetFloat("_UseHatching",hatch?1:0);if(hatch)mat.EnableKeyword("_USE_HATCHING");
            mat.SetFloat("_UseTriplanar",1);mat.EnableKeyword("_USE_TRIPLANAR");mat.SetFloat("_HatchScale",5);
            mat.SetFloat("_HatchStrength",.28f);mat.SetFloat("_HighlightStrength",.05f);mat.SetFloat("_SurfaceTintStrength",.16f);
            mat.SetFloat("_CastShadowHatchStrength",.3f);
            return Save(mat,folder+"/Materials/"+name+".mat");
        }
        static Mesh AshMesh(string folder)
        {
            var primitive=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mesh=Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(primitive);
            mesh.name="LobedAshCloud";var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)
            {
                var v=vertices[i];float wobble=1+.13f*Mathf.Sin(v.x*13+v.y*7)*Mathf.Cos(v.z*10-v.y*3);
                vertices[i]=Vector3.Scale(v*wobble,new Vector3(1.1f,.85f,1));
            }
            mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();return Save(mesh,folder+"/Meshes/LobedAshCloud.asset");
        }
        static Texture2D BombPalette(string folder)
        {
            // A tiny point-sampled material palette; face UVs select solid illustrated colors.
            var texture=new Texture2D(7,1,TextureFormat.RGBA32,false,false){name="LavaBombPalette",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            texture.SetPixels(new[]{new Color(.17f,.14f,.18f),new Color(.27f,.21f,.23f),new Color(.39f,.28f,.25f),
                new Color(.52f,.19f,.12f),new Color(1,.35f,.07f),new Color(1,.77f,.25f),new Color(.87f,.25f,.055f)});
            texture.Apply();return Save(texture,folder+"/Textures/LavaBombPalette.asset");
        }
        static Mesh BombMesh(string folder,int variant=0)
        {
            float golden=(1+Mathf.Sqrt(5))*.5f;
            var points=new[]{new Vector3(-1,golden,0),new Vector3(1,golden,0),new Vector3(-1,-golden,0),new Vector3(1,-golden,0),
                new Vector3(0,-1,golden),new Vector3(0,1,golden),new Vector3(0,-1,-golden),new Vector3(0,1,-golden),
                new Vector3(golden,0,-1),new Vector3(golden,0,1),new Vector3(-golden,0,-1),new Vector3(-golden,0,1)};
            int[] faces={0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
            for(int i=0;i<points.Length;i++)
            {
                float radial=1+.13f*Mathf.Sin(i*2.71f+variant*4.13f);
                points[i]=Vector3.Scale(points[i].normalized*.5f*radial,new Vector3(1+variant*.08f,.83f-variant*.025f,1.02f-variant*.08f));
            }
            var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();
            Action<Vector3,Vector3,Vector3,int> triangle=(a,b,c,color)=>
            {
                int start=v.Count;v.Add(a);v.Add(b);v.Add(c);t.AddRange(new[]{start,start+1,start+2});
                var sample=new Vector2((color+.5f)/7,.5f);uv.Add(sample);uv.Add(sample);uv.Add(sample);
            };
            Action<Vector3,Vector3,Vector3,int,int> patch=null;
            patch=(a,b,c,depth,crust)=>
            {
                if(depth>0)
                {
                    var ab=(a+b)*.5f;var bc=(b+c)*.5f;var ca=(c+a)*.5f;
                    patch(a,ab,ca,depth-1,crust);patch(ab,b,bc,depth-1,crust);patch(ca,bc,c,depth-1,crust);patch(ab,bc,ca,depth-1,crust);return;
                }
                var p=(a+b+c)/3;
                // Two continuous, bent fracture bands across the solid rock, rather than a wireframe on every facet.
                float cutA=Mathf.Abs(p.x*.9f+p.y*.35f+p.z*.12f+.035f*Mathf.Sin(p.z*15+variant));
                float cutB=Mathf.Abs(-p.x*.2f+p.y*.85f-p.z*.6f-.1f+.025f*Mathf.Sin(p.x*20+variant*2));
                float distance=Mathf.Min(cutA,cutB);
                int color=distance<.010f?5:distance<.028f?4:distance<.047f?3:crust;
                triangle(a,b,c,color);
            };
            Func<Vector3,Vector3,Vector3> rounded=(a,b)=>
            {
                var direction=(a+b).normalized;
                float radius=.5f*(1+.1f*Mathf.Sin(Vector3.Dot(direction,new Vector3(3.1f,5.7f,4.3f))+variant*2.3f));
                return Vector3.Scale(direction*radius,new Vector3(1+variant*.08f,.83f-variant*.025f,1.02f-variant*.08f));
            };
            for(int face=0;face<20;face++)
            {
                var a=points[faces[face*3]];var b=points[faces[face*3+1]];var c=points[faces[face*3+2]];var center=(a+b+c)/3;
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),center)<0){var swap=b;b=c;c=swap;}
                var ab=rounded(a,b);var bc=rounded(b,c);var ca=rounded(c,a);int crust=(face+variant)%3;
                patch(a,ab,ca,2,crust);patch(ab,b,bc,2,(crust+1)%3);patch(ca,bc,c,2,crust);patch(ab,bc,ca,2,crust);
            }
            string name=variant==0?"LavaBombShard":"LavaBombShard_"+(variant+1);
            var mesh=new Mesh{name=name};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();
            return Save(mesh,folder+"/Meshes/"+name+".asset");
        }
        static void RefineBombs(ParticleSystem bombs,string folder)
        {
            var mat=AssetDatabase.LoadAssetAtPath<Material>(folder+"/Materials/MoltenBombs.mat");
            mat.SetColor("_BaseColor",Color.white);mat.SetTexture("_BaseMap",BombPalette(folder));mat.SetFloat("_PaperLift",.015f);
            mat.SetFloat("_SurfaceTintStrength",.28f);EditorUtility.SetDirty(mat);
            var renderer=bombs.GetComponent<ParticleSystemRenderer>();
            renderer.SetMeshes(new[]{BombMesh(folder,0),BombMesh(folder,1),BombMesh(folder,2)});
            var main=bombs.main;main.startSize=new ParticleSystem.MinMaxCurve(.30f,.55f);
            main.startSpeed=new ParticleSystem.MinMaxCurve(5.4f,7.4f);main.gravityModifier=1;
            Cone(bombs,.32f,32);var emission=bombs.emission;
            emission.SetBursts(new[]{new ParticleSystem.Burst(1,16,22),new ParticleSystem.Burst(1.5f,6,9)});
        }
        static void BuildVolcano()
        {
            string folder=VFX+"Volcano",prefab=Biomes+"Prefabs/Volcano_Moebius.prefab";
            var root=PrefabUtility.LoadPrefabContents(prefab);GameObject rig=null;
            try
            {
                var old=root.transform.Find("Volcano VFX");if(old)Object.DestroyImmediate(old.gameObject);
                var foundation=root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Biome Foundation");
                var origin=foundation.bounds.center+Vector3.up*foundation.bounds.extents.y;
                var rock=root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Rock");
                var lava=root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Lava");
                var points=Vertices(lava.GetComponent<MeshFilter>().sharedMesh).Select(lava.transform.TransformPoint).ToArray();
                float highest=points.Max(p=>p.y);var hot=points.Where(p=>p.y>highest-.12f).ToArray();
                var vent=hot.Aggregate(Vector3.zero,(a,p)=>a+p)/hot.Length-origin+Vector3.up*.12f;
                Check(vent.y>4,"Eruption emitter fitted to the highest molten crater, not the prefab pivot");Notes.Add("Crater local position: "+vent);
                rig=new GameObject("Volcano VFX");
                Colliders(rig,rock,foundation,origin,folder);
                var bomb=NPR(folder,"MoltenBombs",new Color(1,.34f,.065f),false);
                var ash=NPR(folder,"IllustratedAsh",new Color(.48f,.46f,.50f),true);
                var ember=Save(new Material(AssetDatabase.LoadAssetAtPath<Material>(VFX+"Grassland/Materials/GrasslandMotes.mat")){name="VolcanicEmbers"},folder+"/Materials/VolcanicEmbers.mat");
                ember.SetColor("_Tint",new Color(1,.57f,.14f));ember.SetFloat("_Opacity",1);ember.SetFloat("_DepthFade",0);
                EditorUtility.SetDirty(ember);
                var bombs=CreateSystem(rig,"Lava Bombs (12s Cycle)",bomb,7613,64,true);bombs.transform.localPosition=vent;
                bombs.GetComponent<ParticleSystemRenderer>().mesh=BombMesh(folder);
                var main=bombs.main;main.startLifetime=new ParticleSystem.MinMaxCurve(2.3f,3.4f);main.startSize=new ParticleSystem.MinMaxCurve(.16f,.34f);
                main.startSpeed=new ParticleSystem.MinMaxCurve(5.5f,8);main.gravityModifier=.85f;main.startRotation3D=true;
                main.startRotationX=new ParticleSystem.MinMaxCurve(-3,3);main.startRotationY=new ParticleSystem.MinMaxCurve(-3,3);main.startRotationZ=new ParticleSystem.MinMaxCurve(-3,3);
                Cone(bombs,.35f,28);var emission=bombs.emission;
                emission.SetBursts(new[]{new ParticleSystem.Burst(1,30,38),new ParticleSystem.Burst(1.5f,10,16)});
                Shrink(bombs,new Keyframe(0,.45f),new Keyframe(.08f,1),new Keyframe(.75f,.8f),new Keyframe(1,0));NativeCollision(bombs);
                var spin=bombs.rotationOverLifetime;spin.enabled=true;spin.separateAxes=true;
                spin.x=new ParticleSystem.MinMaxCurve(-2,2);spin.y=new ParticleSystem.MinMaxCurve(-3,3);spin.z=new ParticleSystem.MinMaxCurve(-2,2);
                RefineBombs(bombs,folder);
                var clouds=CreateSystem(rig,"Ash Plume (12s Cycle)",ash,8819,32,true);clouds.transform.localPosition=vent+Vector3.up*.15f;
                clouds.GetComponent<ParticleSystemRenderer>().mesh=AshMesh(folder);
                main=clouds.main;main.startLifetime=new ParticleSystem.MinMaxCurve(4.3f,5.7f);main.startSize=new ParticleSystem.MinMaxCurve(.9f,1.5f);
                main.startSpeed=new ParticleSystem.MinMaxCurve(.8f,1.2f);Cone(clouds,.4f,13);
                emission=clouds.emission;emission.SetBursts(new[]{new ParticleSystem.Burst(1.15f,8,11),new ParticleSystem.Burst(1.85f,5,8)});
                var velocity=clouds.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.x=.28f;velocity.y=0;velocity.z=.12f;
                var noise=clouds.noise;noise.enabled=true;noise.strength=.22f;noise.frequency=.32f;noise.scrollSpeed=.18f;noise.octaveCount=1;
                Shrink(clouds,new Keyframe(0,.25f),new Keyframe(.2f,1.1f),new Keyframe(.55f,1.75f),new Keyframe(.78f,1.4f),new Keyframe(1,0));
                var sparks=CreateSystem(rig,"Rising Embers (12s Cycle)",ember,1153,128,false);sparks.transform.localPosition=vent;
                main=sparks.main;main.startLifetime=new ParticleSystem.MinMaxCurve(1.6f,3);main.startSize=new ParticleSystem.MinMaxCurve(.06f,.13f);
                main.startSpeed=new ParticleSystem.MinMaxCurve(4,7);main.gravityModifier=.3f;Cone(sparks,.4f,38);
                emission=sparks.emission;emission.SetBursts(new[]{new ParticleSystem.Burst(1.08f,45,65),new ParticleSystem.Burst(1.6f,20,30)});
                var sheet=sparks.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=2;sheet.numTilesY=2;sheet.frameOverTime=0;sheet.startFrame=new ParticleSystem.MinMaxCurve(0,1);
                Fade(sparks);Shrink(sparks,new Keyframe(0,.5f),new Keyframe(.12f,1),new Keyframe(1,0));NativeCollision(sparks);
                InstallRig(rig,root,origin,folder+"/Prefabs/VolcanoVFX.prefab");Object.DestroyImmediate(rig);rig=null;
                PrefabUtility.SaveAsPrefabAsset(root,prefab);Notes.Add("Volcano: 12-second native loops; bursts at 1.0–1.85s; no per-frame C# updates.");
            }
            finally{if(rig)Object.DestroyImmediate(rig);PrefabUtility.UnloadPrefabContents(root);}
        }
        static void BuildSnow()
        {
            string folder=VFX+"SnowMountain",prefab=Biomes+"Prefabs/SnowMountain_Moebius.prefab";
            var root=PrefabUtility.LoadPrefabContents(prefab);GameObject rig=null;
            try
            {
                var old=root.transform.Find("Snow Mountain VFX");if(old)Object.DestroyImmediate(old.gameObject);
                var foundation=root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Biome Foundation");
                var surface=root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="SnowMountain_Moebius");
                var origin=foundation.bounds.center+Vector3.up*foundation.bounds.extents.y;
                rig=new GameObject("Snow Mountain VFX");Colliders(rig,surface,foundation,origin,folder);
                var bounds=Child(rig,"Snowfall Boundary (Ignore Raycast)");bounds.layer=2;
                var box=bounds.AddComponent<BoxCollider>();box.isTrigger=true;box.center=new Vector3(0,6.5f,0);box.size=new Vector3(17,13,17);
                for(int layer=0;layer<2;layer++)
                {
                    string name=layer==0?"Fine Snowfall":"Larger Drawn Snowflakes";
                    var mat=new Material(AssetDatabase.LoadAssetAtPath<Material>(VFX+"Tundra/Materials/SnowFlakes.mat")){name=name.Replace(" ","")};
                    mat.SetColor("_Tint",new Color(.98f,.99f,1));mat.SetColor("_InkColor",new Color(.65f,.77f,.84f));mat.SetFloat("_Opacity",1);
                    mat.SetFloat("_IntersectionDistance",.09f);mat=Save(mat,folder+"/Materials/"+mat.name+".mat");
                    var ps=CreateSystem(rig,name,mat,(uint)(17041+layer*37),layer==0?480:96,false);ps.transform.localPosition=new Vector3(0,10.4f,0);
                    var main=ps.main;main.prewarm=true;main.startLifetime=new ParticleSystem.MinMaxCurve(8,11);
                    main.startSize=new ParticleSystem.MinMaxCurve(layer==0?.13f:.26f,layer==0?.24f:.39f);
                    main.startRotation=new ParticleSystem.MinMaxCurve(-Mathf.PI,Mathf.PI);
                    var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(15.2f,.18f,15.2f);
                    var emission=ps.emission;emission.rateOverTime=layer==0?42:7;
                    var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;
                    velocity.x=new ParticleSystem.MinMaxCurve(.12f,.25f);velocity.y=new ParticleSystem.MinMaxCurve(layer==0?-1.55f:-1.1f,layer==0?-1.05f:-.8f);velocity.z=new ParticleSystem.MinMaxCurve(-.08f,.08f);
                    var noise=ps.noise;noise.enabled=true;noise.strength=.13f;noise.frequency=.23f;noise.scrollSpeed=.16f;noise.octaveCount=1;noise.quality=ParticleSystemNoiseQuality.Low;
                    var spin=ps.rotationOverLifetime;spin.enabled=true;spin.z=new ParticleSystem.MinMaxCurve(-.45f,.45f);
                    var sheet=ps.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=2;sheet.numTilesY=2;sheet.frameOverTime=0;sheet.startFrame=new ParticleSystem.MinMaxCurve(0,1);
                    Fade(ps,.06f);NativeCollision(ps);
                    var trigger=ps.trigger;trigger.enabled=true;trigger.SetCollider(0,box);trigger.radiusScale=.05f;
                    trigger.inside=ParticleSystemOverlapAction.Ignore;trigger.outside=ParticleSystemOverlapAction.Kill;trigger.enter=ParticleSystemOverlapAction.Ignore;trigger.exit=ParticleSystemOverlapAction.Kill;
                }
                InstallRig(rig,root,origin,folder+"/Prefabs/SnowMountainVFX.prefab");Object.DestroyImmediate(rig);rig=null;
                PrefabUtility.SaveAsPrefabAsset(root,prefab);Notes.Add("Snow Mountain: two continuous snowfall layers, 49 particles/sec total; 576 particle cap; actual terrain collision and tile bounds.");
            }
            finally{if(rig)Object.DestroyImmediate(rig);PrefabUtility.UnloadPrefabContents(root);}
        }
        static void InstallRig(GameObject rig,GameObject target,Vector3 origin,string path)
        {
            var preview=rig.GetComponent<Darwin.Rendering.MoebiusParticlePreview>();if(!preview)preview=rig.AddComponent<Darwin.Rendering.MoebiusParticlePreview>();
            preview.particleSystems=rig.GetComponentsInChildren<ParticleSystem>(true);preview.previewWarmupSeconds=rig.name=="Volcano VFX"?1.8f:6;
            Folder(Path.GetDirectoryName(path).Replace('\\','/'));var prefab=PrefabUtility.SaveAsPrefabAsset(rig,path);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,target.transform);instance.name=rig.name;
            instance.transform.position=origin;instance.transform.rotation=Quaternion.identity;
            var scale=target.transform.lossyScale;instance.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
        }
        static void RiverFoam()
        {
            foreach(string kind in new[]{"I","T"})
            {
                var mat=AssetDatabase.LoadAssetAtPath<Material>(Biomes+"Materials/River/River_"+kind+"_Water.mat");
                Check(mat&&mat.GetTexture("_ShorelineMask"),kind+": existing baked shoreline retained");
                mat.SetFloat("_EnableFoam",1);mat.SetFloat("_EnableShoreMask",1);mat.SetFloat("_FoamWidth",.52f);mat.SetFloat("_FoamIntensity",1);
                mat.SetFloat("_FoamInkStrength",.45f);mat.SetFloat("_FoamPatchStrength",.95f);mat.SetFloat("_FoamSpeed",.38f);mat.SetFloat("_ShoreMotionStrength",.19f);
                mat.SetColor("_FoamColor",new Color(.94f,.98f,.94f));EditorUtility.SetDirty(mat);
            }
        }
        public static void RunBatch()
        {
            try
            {
                Guard();RiverFoam();BuildVolcano();BuildSnow();AssetDatabase.SaveAssets();
                foreach(string name in new[]{"Volcano","SnowMountain","River"})ValidateScene(name);
            }
            catch(Exception e){Errors.Add(e.ToString());}
            Finish("Report.txt");
        }
        public static void RefineAndValidate()
        {
            try
            {
                Guard();string folder=VFX+"Volcano";string path=folder+"/Prefabs/VolcanoVFX.prefab";
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    RefineBombs(root.GetComponentsInChildren<ParticleSystem>().First(p=>p.name.StartsWith("Lava Bombs")),folder);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
                AssetDatabase.SaveAssets();foreach(string name in new[]{"Volcano","SnowMountain","River"})ValidateScene(name);
            }
            catch(Exception e){Errors.Add(e.ToString());}
            Finish("RefinementReport.txt");
        }
        static void Simulate(IEnumerable<ParticleSystem> systems,float time)
        {
            foreach(var ps in systems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            Physics.SyncTransforms();int steps=Mathf.CeilToInt(time*60);
            for(int frame=0;frame<steps;frame++)foreach(var ps in systems)ps.Simulate(1f/60,false,frame==0,false);
        }
        static void ValidateScene(string name)
        {
            var scene=EditorSceneManager.OpenScene(Biomes+"Scenes/"+name+"_Moebius.unity");var systems=Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)
                .Where(p=>!p.GetComponentsInParent<Transform>().Any(t=>t.name=="Environment Details")).ToArray();
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,name+": no missing scripts on "+t.name);
            if(name=="River"){Capture(Camera.main,Results+"/RiverFoam.png");return;}
            Check(systems.Length==(name=="Volcano"?3:2),name+": expected native particle layer count");
            foreach(var ps in systems)
            {
                Check(Mathf.Abs(ps.transform.lossyScale.x-1)<.001f,name+": particle scale is metres despite imported prefab scaling");
                foreach(var mat in ps.GetComponent<ParticleSystemRenderer>().sharedMaterials)
                    Check(mat&&mat.shader&&mat.shader.isSupported,name+": supported particle material "+mat.name);
            }
            if(name=="Volcano")
            {
                foreach(float time in new[]{.5f,2.2f,4.5f,9.5f,14.2f})
                {
                    Simulate(systems,time);int count=systems.Sum(ps=>ps.particleCount);Notes.Add("Volcano t="+time+" particles="+count);
                    Check((time<1||time>8&&time<12)?count==0:count>0,"Volcano cycle at "+time+"s matches quiet/eruption phase");
                    if(time==2.2f||time==4.5f||time==9.5f)Capture(Camera.main,Results+"/Volcano"+time.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+".png");
                    if(time==2.2f)
                    {
                        var camera=Camera.main;var position=camera.transform.position;var rotation=camera.transform.rotation;float size=camera.orthographicSize;
                        var target=systems.First(ps=>ps.name.StartsWith("Lava Bombs")).transform.position+Vector3.up*1.5f;
                        var direction=(position-target).normalized;camera.transform.position=target+direction*20;camera.transform.LookAt(target);camera.orthographicSize=4.2f;
                        Capture(camera,Results+"/VolcanoBombsCloseup.png");camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=size;
                    }
                }
            }
            else
            {
                Simulate(systems,7);Check(systems.All(ps=>ps.particleCount>0),"Both snow layers emit and remain visible");
                Notes.Add("Snow at 7s: "+string.Join("/",systems.Select(ps=>ps.name+"="+ps.particleCount)));
                var a=new ParticleSystem.Particle[512];int below=0;
                foreach(var ps in systems)for(int i=0,n=ps.GetParticles(a);i<n;i++)if(a[i].position.y<-.05f)below++;
                Check(below==0,"Snow does not fall beneath the foundation");Capture(Camera.main,Results+"/SnowMountain.png");
            }
            foreach(var mat in systems.SelectMany(ps=>ps.GetComponent<ParticleSystemRenderer>().sharedMaterials).Distinct())
                foreach(var e in ShaderUtil.GetShaderMessages(mat.shader))if(e.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)Errors.Add(mat.shader.name+": "+e.message);
        }
        public static void RunPlay()
        {
            Guard();EditorSceneManager.OpenScene(Biomes+"Scenes/Volcano_Moebius.unity");
            SessionState.SetInt(PlayKey,1);EditorApplication.EnterPlaymode();
        }
        static void PlayTick()
        {
            int mode=SessionState.GetInt(PlayKey,0);if(mode==0)return;
            if(deadline==0)deadline=EditorApplication.timeSinceStartup+180;
            if(EditorApplication.timeSinceStartup>deadline){Errors.Add("Play Mode timeout");Finish("PlayReport.txt");return;}
            if(!EditorApplication.isPlaying||EditorApplication.isPaused)return;
            try
            {
                var systems=Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)
                    .Where(p=>!p.GetComponentsInParent<Transform>().Any(t=>t.name=="Environment Details")).ToArray();
                if(mode==1)
                {
                    if(playStage==0&&Time.time>2.1f){Check(systems.Sum(ps=>ps.particleCount)>0,"Native Play Mode eruption starts automatically");Capture(Camera.main,Results+"/VolcanoPlay.png");playStage=1;}
                    if(playStage==1&&Time.time>9){Check(systems.Sum(ps=>ps.particleCount)==0,"Native eruption settles between cycles");playStage=2;}
                    if(playStage==2&&Time.time>14.1f)
                    {
                        Check(systems.Sum(ps=>ps.particleCount)>0,"Native eruption repeats next cycle");
                        EditorSceneManager.LoadSceneInPlayMode(Biomes+"Scenes/SnowMountain_Moebius.unity",new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
                        SessionState.SetInt(PlayKey,2);playStage=0;
                    }
                }
                else if(mode==2&&Time.timeSinceLevelLoad>2)
                {
                    Check(systems.Length==2&&systems.All(ps=>ps.particleCount>0),"Native Play Mode snowfall runs automatically");
                    Capture(Camera.main,Results+"/SnowPlay.png");Finish("PlayReport.txt");
                }
            }
            catch(Exception e){Errors.Add(e.ToString());Finish("PlayReport.txt");}
        }
        static void Finish(string name)
        {
            SessionState.SetInt(PlayKey,0);Directory.CreateDirectory(Results);
            File.WriteAllLines(Results+"/"+name,Notes.Concat(new[]{"Errors: "+Errors.Count}).Concat(Errors));EditorApplication.Exit(Errors.Count==0?0:1);
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
