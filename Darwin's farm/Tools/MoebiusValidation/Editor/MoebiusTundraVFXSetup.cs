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
    // Offline authoring only: no additional Editor menu or runtime asset scanning.
    public static class MoebiusTundraVFXSetup
    {
        const string ScenePath="Assets/Rendering/Moebius/Samples/Biomes/Scenes/Tundra_Colored.unity";
        const string Output="Assets/Rendering/Moebius/Content/VFX/Tundra";
        const string Shared="Assets/Rendering/Moebius/Content/VFX/Highland";
        static readonly List<string> Notes=new List<string>();
        static readonly List<string> Errors=new List<string>();
        const string Report="Validation/TundraVFX";
        struct Triangle {public Vector3 a,b,c;}
        static readonly List<Triangle> Ground=new List<Triangle>();
        static MeshRenderer[] surfaces;
        public static void RunBatch()
        {
            Directory.CreateDirectory(Report);Application.logMessageReceived+=LogError;
            try
            {
                if(!File.Exists("TundraVFX.marker"))throw new Exception("Use the marked isolated Temp fixture.");
                EditorSceneManager.OpenScene(ScenePath);
                foreach(var old in Object.FindObjectsByType<MoebiusWindController>(FindObjectsSortMode.None))
                    if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old.gameObject)==Output+"/Prefabs/TundraVFX.prefab")Object.DestroyImmediate(old.gameObject);
                var original=Object.FindObjectsByType<Component>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                    .ToDictionary(c=>c.GetInstanceID(),c=>EditorJsonUtility.ToJson(c));
                var camera=Camera.main;
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                ShaderUtil.allowAsyncCompilation=false;Capture(camera,Report+"/Before.png");
                surfaces=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .Where(r=>r.enabled && r.gameObject.activeInHierarchy && r.name=="Ground_Surface").ToArray();
                if(surfaces.Length!=1)throw new Exception("Expected the named Tundra Ground_Surface ground, found "+surfaces.Length);
                Bounds bounds=surfaces[0].bounds;Notes.Add("Target: "+ScenePath+"; ground: Ground_Surface; bounds: "+bounds);
                foreach(var folder in new[]{"Materials","Meshes","Prefabs","Textures"})Directory.CreateDirectory(Output+"/"+folder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                CreateSnowAtlas();
                var sand=Material("ColdGroundHaze","Darwin/Moebius Dust",Shared+"/Textures/DustAtlas.png");
                var gust=Material("SnowPatchSpindrift","Darwin/Moebius Dust",Shared+"/Textures/DustAtlas.png");
                foreach(var mat in new[]{sand,gust})
                {
                    mat.SetTexture("_NoiseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Shared+"/Textures/DustNoise.png"));
                    mat.SetFloat("_Distortion",.025f);mat.SetFloat("_AnimationSpeed",.12f);mat.SetFloat("_IntersectionDistance",.18f);
                }
                sand.SetFloat("_Opacity",.19f);gust.SetFloat("_Opacity",.44f);
                var grains=Material("SnowFlakes","Darwin/Moebius Grassland Leaves",Output+"/Textures/SnowflakesAtlas.png");
                grains.SetFloat("_Opacity",.95f);grains.SetColor("_InkColor",new Color(.64f,.75f,.8f,1));
                var strokes=Material("ColdWind","Darwin/Moebius Wind",Shared+"/Textures/WindAtlas.png");strokes.SetFloat("_Opacity",.84f);
                foreach(var mat in new[]{sand,gust,grains,strokes})EditorUtility.SetDirty(mat);
                Mesh ground=GroundMesh(surfaces);
                Mesh emission=EmissionMesh(bounds,0),powderEmission=EmissionMesh(bounds,1),snowEmission=EmissionMesh(bounds,2),windEmission=EmissionMesh(bounds,3);
                var quad=AssetDatabase.LoadAssetAtPath<Mesh>(Shared+"/Meshes/WindAndDustQuad.asset");if(!quad)throw new Exception("Missing shared particle quad");
                var root=new GameObject("TundraVFX");var controller=root.AddComponent<MoebiusWindController>();controller.animateEditModePreview=false;
                controller.baseWindSpeed=.85f;controller.gustStrength=.8f;controller.gustFrequency=.12f;controller.turbulenceStrength=.17f;
                controller.randomSeed=4507;controller.ambientDustRate=1.5f;controller.gustDustRate=2.4f;controller.debrisRate=8;controller.windStreakRate=.65f;
                controller.dustColor=new Color(.82f,.91f,.94f,1);controller.debrisColor=new Color(.96f,.98f,.97f,1);controller.windLineColor=new Color(.91f,.97f,.98f,1);
                controller.dustSizeRange=new Vector2(1.3f,2.4f);controller.debrisSizeRange=new Vector2(.14f,.26f);controller.windLengthRange=new Vector2(3.6f,5.1f);
                controller.boundaryFade=1.1f;controller.localBounds=new Bounds(bounds.center+Vector3.up*2,new Vector3(bounds.size.x-.2f,8,bounds.size.z-.2f));
                var helpers=Child(root,"Surface And Bounds");var collision=Child(helpers,"Ground Collision");collision.layer=2;collision.AddComponent<MeshCollider>().sharedMesh=ground;
                var volume=Child(helpers,"Particle Boundary");volume.layer=2;var box=volume.AddComponent<BoxCollider>();box.isTrigger=true;box.center=controller.localBounds.center;box.size=controller.localBounds.size;
                controller.ambientDust=System(root,"ColdGroundHaze",sand,emission,quad,0,box);
                controller.gustDust=System(root,"SnowPatchSpindrift",gust,powderEmission,quad,1,box);
                controller.floatingDebris=System(root,"SnowFlakes",grains,snowEmission,quad,2,box);
                controller.windStreaks=System(root,"ColdWindStreaks",strokes,windEmission,quad,3,box);
                controller.RefreshSettings();controller.animateEditModePreview=true;
                PrefabUtility.SaveAsPrefabAssetAndConnect(root,Output+"/Prefabs/TundraVFX.prefab",InteractionMode.AutomatedAction);
                // URP may lazily add default light data during the baseline camera render.
                // Do not serialize this unrelated addition into the source scene.
                foreach(var data in Object.FindObjectsByType<UniversalAdditionalLightData>(FindObjectsSortMode.None))
                    if(!original.ContainsKey(data.GetInstanceID()) && !data.transform.IsChildOf(root.transform))Object.DestroyImmediate(data);
                foreach(var pair in original)
                {
                    var c=EditorUtility.InstanceIDToObject(pair.Key) as Component;
                    if(!c||EditorJsonUtility.ToJson(c)!=pair.Value)Errors.Add("Original component changed: "+c);
                }
                Notes.Add("Unchanged original scene component snapshots: "+original.Count);
                EditorSceneManager.SaveScene(root.scene,ScenePath);AssetDatabase.SaveAssets();
                ValidateAndCapture(controller,camera,bounds);
                foreach(var mat in new[]{sand,gust,grains,strokes})
                {
                    if(!mat.GetTexture("_BaseMap"))Errors.Add("Missing texture: "+mat.name);
                    foreach(var message in ShaderUtil.GetShaderMessages(mat.shader))
                        if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)Errors.Add(mat.shader.name+": "+message.message);
                }
            }
            catch(Exception e){Errors.Add(e.ToString());}
            Notes.Add("Errors: "+Errors.Count);Notes.AddRange(Errors);File.WriteAllLines(Report+"/Report.txt",Notes);EditorApplication.Exit(Errors.Count==0?0:1);
        }
        static void LogError(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)Errors.Add(message);}
        static GameObject Child(GameObject parent,string name)
        {var obj=new GameObject(name);obj.transform.SetParent(parent.transform,false);return obj;}
        static Material Material(string name,string shaderName,string texture)
        {
            string path=Output+"/Materials/"+name+".mat";var shader=Shader.Find(shaderName);if(!shader)throw new Exception("Missing shader "+shaderName);
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}else mat.shader=shader;
            mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture));mat.SetFloat("_AtlasColumns",2);mat.SetFloat("_AtlasRows",2);EditorUtility.SetDirty(mat);return mat;
        }
        static Mesh SaveMesh(string name,List<Vector3> vertices,List<int> triangles,List<Vector2> uv=null)
        {
            string path=Output+"/Meshes/"+name+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
            mesh.name=name; mesh.indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
            if(uv!=null)mesh.SetUVs(0,uv);
            mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
        }
        static Mesh GroundMesh(MeshRenderer[] surfaces)
        {
            Ground.Clear();var vertices=new List<Vector3>();var indices=new List<int>();
            foreach(var surface in surfaces)
            {
                var mesh=surface.GetComponent<MeshFilter>().sharedMesh;
                var points=mesh.vertices;int offset=vertices.Count;
                foreach(var point in points) vertices.Add(surface.transform.TransformPoint(point));
                var triangles=mesh.triangles;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    int a=offset+triangles[i],b=offset+triangles[i+1],c=offset+triangles[i+2];
                    indices.Add(a);indices.Add(b);indices.Add(c);
                    if(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).normalized.y>0.45f)
                        Ground.Add(new Triangle{a=vertices[a],b=vertices[b],c=vertices[c]});
                }
            }
            if(Ground.Count==0)throw new Exception("Ground has no upward-facing triangles");
            foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.name.StartsWith("Rock_") || r.name=="Pond_ShallowWater"))
            {
                var m=r.GetComponent<MeshFilter>().sharedMesh;int offset=vertices.Count;
                vertices.AddRange(m.vertices.Select(v=>r.transform.TransformPoint(v)));
                indices.AddRange(m.triangles.Select(i=>i+offset));
            }
            return SaveMesh("TundraCollision",vertices,indices);
        }
        static bool Height(float x,float z,out float height)
        {
            height=float.NegativeInfinity;
            foreach(var t in Ground)
            {
                float denominator=(t.b.z-t.c.z)*(t.a.x-t.c.x)+(t.c.x-t.b.x)*(t.a.z-t.c.z);
                if(Mathf.Abs(denominator)<1e-7f)continue;
                float a=((t.b.z-t.c.z)*(x-t.c.x)+(t.c.x-t.b.x)*(z-t.c.z))/denominator;
                float b=((t.c.z-t.a.z)*(x-t.c.x)+(t.a.x-t.c.x)*(z-t.c.z))/denominator;
                float c=1-a-b;
                if(a>=-0.001f && b>=-0.001f && c>=-0.001f)height=Mathf.Max(height,a*t.a.y+b*t.b.y+c*t.c.y);
            }
            return !float.IsNegativeInfinity(height);
        }
        static Mesh EmissionMesh(Bounds bounds,int kind)
        {
            var vertices=new List<Vector3>();var indices=new List<int>();
            if(kind==1)
            {
                var snow=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .Where(r=>r.name.StartsWith("SnowPatch_") || r.name.EndsWith("_SnowCap")).ToArray();
                foreach(var r in snow)
                {
                    var mesh=r.GetComponent<MeshFilter>().sharedMesh;int offset=vertices.Count;
                    vertices.AddRange(mesh.vertices.Select(v=>r.transform.TransformPoint(v)));
                    indices.AddRange(mesh.triangles.Select(i=>i+offset));
                }
                Notes.Add("Spindrift emitters: "+snow.Length+" actual snow patches/caps");
            }
            else
            {
                var rocks=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .Where(r=>r.name.StartsWith("Rock_") && r.bounds.size.y>.65f).ToArray();
                var pond=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Single(r=>r.name=="Pond_ShallowWater");
                const float cell=.45f,inset=1.3f;
                for(float x=bounds.min.x+inset;x+cell<bounds.max.x-inset;x+=cell)
                for(float z=bounds.min.z+inset;z+cell<bounds.max.z-inset;z+=cell)
                {
                    float cx=x+cell*.5f,cz=z+cell*.5f;
                    if(!Height(cx,cz,out float y))continue;
                    if(kind!=2 && (rocks.Any(r=>Mathf.Abs(cx-r.bounds.center.x)<r.bounds.extents.x+.25f && Mathf.Abs(cz-r.bounds.center.z)<r.bounds.extents.z+.25f)
                        || (Mathf.Abs(cx-pond.bounds.center.x)<pond.bounds.extents.x+.2f && Mathf.Abs(cz-pond.bounds.center.z)<pond.bounds.extents.z+.2f)))continue;
                    int offset=vertices.Count;float h=kind==2?4.1f:y;
                    vertices.Add(new Vector3(x,h,z));vertices.Add(new Vector3(x+cell,h,z));
                    vertices.Add(new Vector3(x,h,z+cell));vertices.Add(new Vector3(x+cell,h,z+cell));
                    indices.AddRange(new[]{offset,offset+2,offset+1,offset+1,offset+2,offset+3});
                }
            }
            if(indices.Count<6)throw new Exception("No emission area for layer "+kind);
            Notes.Add("Emission layer "+kind+": "+indices.Count/3+" triangles");
            return SaveMesh(kind==0?"ColdHazeEmission":kind==1?"SnowPatchEmission":kind==2?"SnowfallEmission":"ColdWindEmission",vertices,indices);
        }
        static float Segment(Vector2 p,Vector2 a,Vector2 b)
        {var d=b-a;return (p-a-d*Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude)).magnitude;}
        static void CreateSnowAtlas()
        {
            const int size=512;var texture=new Texture2D(size,size,TextureFormat.RGBA32,true,true);
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                int id=(x/256)+(y/256)*2;var p=new Vector2((x%256+.5f)/256,(y%256+.5f)/256);
                float d;
                if(id==0 || id==3)
                {
                    // A few irregular ice chips, with a cool drawn border.
                    p-=new Vector2(.5f,.5f);p.x+=p.y*.2f;
                    d=(Mathf.Abs(p.x)/(.16f+id*.012f)+Mathf.Abs(p.y)/(.24f-id*.015f)-1)*.13f;
                }
                else
                {
                    d=Segment(p,new Vector2(.31f,.28f),new Vector2(.65f,.72f))-.018f;
                    d=Mathf.Min(d,Segment(p,new Vector2(.28f,.61f),new Vector2(.72f,.39f))-.015f);
                    if(id==2)d=Mathf.Min(d,Segment(p,new Vector2(.45f,.76f),new Vector2(.54f,.26f))-.012f);
                }
                float alpha=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.002f,.005f,d));
                float fill=Mathf.Lerp(.74f,1,Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.018f,-d)));
                pixels[y*size+x]=new Color(fill,fill,fill,alpha);
            }
            texture.SetPixels(pixels);texture.Apply();string path=Output+"/Textures/SnowflakesAtlas.png";
            File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=false;
            importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;
            importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;importer.mipmapEnabled=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        }
        static ParticleSystem System(GameObject parent,string name,Material material,Mesh emissionMesh,Mesh quad,int kind,BoxCollider boundary)
        {
            var gameObject=Child(parent,name);
            gameObject.transform.localPosition=Vector3.up*(kind<2?.22f:kind==2?0:.9f);
            var ps=gameObject.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;
            main.duration=12;main.loop=true;main.prewarm=true;main.playOnAwake=true;
            main.maxParticles=kind==0?24:kind==1?24:kind==2?64:8;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            main.startSpeed=0;main.gravityModifier=0;
            main.startLifetime=new ParticleSystem.MinMaxCurve(kind<2?3.5f:kind==2?6:3,kind<2?5:kind==2?8:4.5f);
            main.startColor=new ParticleSystem.MinMaxGradient(new Color(.94f,.97f,1,.9f),Color.white);
            if(kind==3)main.startColor=Color.white;
            main.startSize3D=kind!=2;
            if(kind!=2){main.startSizeY=1;main.startSizeZ=new ParticleSystem.MinMaxCurve(kind==3?.8f:.32f,kind==3?1.4f:.6f);}
            else main.startSize=new ParticleSystem.MinMaxCurve(.13f,.26f);
            main.startRotation3D=kind!=2;
            if(kind==2)main.startRotation=new ParticleSystem.MinMaxCurve(-Mathf.PI,Mathf.PI);
            else{main.startRotationX=0;main.startRotationZ=0;}
            var emission=ps.emission;emission.rateOverTime=1;
            var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Mesh;
            shape.mesh=emissionMesh;shape.meshShapeType=ParticleSystemMeshShapeType.Triangle;
            shape.randomDirectionAmount=0;shape.alignToDirection=false;
            var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;
            velocity.y=new ParticleSystem.MinMaxCurve(kind==2?-.7f:kind<2?.005f:-.015f,kind==2?-.48f:kind<2?.025f:.025f);
            var noise=ps.noise;noise.enabled=true;noise.strength=.05f;noise.frequency=.35f;
            noise.scrollSpeed=.15f;noise.octaveCount=1;noise.quality=ParticleSystemNoiseQuality.Low;noise.damping=true;
            var color=ps.colorOverLifetime;color.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.18f),new GradientAlphaKey(.85f,.65f),new GradientAlphaKey(0,1)});
            color.color=gradient;
            var size=ps.sizeOverLifetime;size.enabled=true;
            size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.8f),new Keyframe(.25f,1),new Keyframe(1,kind<2?1.25f:1)));
            if(kind==2){var rotation=ps.rotationOverLifetime;rotation.enabled=true;rotation.z=new ParticleSystem.MinMaxCurve(-.45f,.45f);}
            var sheet=ps.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=2;sheet.numTilesY=2;
            sheet.animation=ParticleSystemAnimationType.WholeSheet;sheet.frameOverTime=0;
            sheet.startFrame=new ParticleSystem.MinMaxCurve(0,1);sheet.cycleCount=1;
            var collision=ps.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.World;
            collision.mode=ParticleSystemCollisionMode.Collision3D;collision.collidesWith=1<<2;
            collision.enableDynamicColliders=false;collision.quality=ParticleSystemCollisionQuality.High;
            collision.radiusScale=kind<2?.025f:.15f;collision.dampen=.2f;collision.bounce=.04f;
            collision.lifetimeLoss=1;collision.minKillSpeed=0;collision.maxKillSpeed=100;
            var trigger=ps.trigger;trigger.enabled=true;trigger.radiusScale=.05f;trigger.SetCollider(0,boundary);
            trigger.inside=ParticleSystemOverlapAction.Ignore;trigger.outside=ParticleSystemOverlapAction.Kill;
            trigger.enter=ParticleSystemOverlapAction.Ignore;trigger.exit=ParticleSystemOverlapAction.Kill;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
            renderer.renderMode=kind==2?ParticleSystemRenderMode.Billboard:ParticleSystemRenderMode.Mesh;
            if(kind!=2)renderer.mesh=quad;
            renderer.enableGPUInstancing=false;
            renderer.alignment=kind==2?ParticleSystemRenderSpace.View:ParticleSystemRenderSpace.World;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.sortMode=ParticleSystemSortMode.Distance;renderer.maxParticleSize=1;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV});
            return ps;
        }

        static void ValidateAndCapture(MoebiusWindController controller,Camera camera,Bounds bounds)
        {
            var systems=new[]{controller.ambientDust,controller.gustDust,controller.floatingDebris,controller.windStreaks}.Where(ps=>ps).ToArray();
            foreach(var ps in systems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            Physics.SyncTransforms();int underground=0,outside=0,maxParticles=0;float maxJump=0;Vector3 previous=Vector3.zero;
            var particles=new ParticleSystem.Particle[160];var layerPeaks=new int[systems.Length];
            for(int frame=0;frame<900;frame++)
            {
                float time=frame/60f;controller.UpdateWind(time);
                if(frame>0)maxJump=Mathf.Max(maxJump,(controller.CurrentWind-previous).magnitude);previous=controller.CurrentWind;
                foreach(var ps in systems)ps.Simulate(1f/60,false,frame==0,false);
                maxParticles=Mathf.Max(maxParticles,systems.Sum(ps=>ps.particleCount));
                for(int i=0;i<systems.Length;i++)
                {
                    int n=systems[i].GetParticles(particles);layerPeaks[i]=Mathf.Max(layerPeaks[i],n);
                    for(int j=0;j<n;j++)
                    {
                        Vector3 pos=particles[j].position;
                        if(Height(pos.x,pos.z,out float y) && pos.y<y-.08f)underground++;
                        if(!surfaces.Any(r=>pos.x>=r.bounds.min.x-.2f && pos.x<=r.bounds.max.x+.2f && pos.z>=r.bounds.min.z-.2f && pos.z<=r.bounds.max.z+.2f)){if(outside<4)Notes.Add("Outside sample: "+systems[i].name+" "+pos);outside++;}
                    }
                }
                if(frame==239||frame==539||frame==899)
                {
                    Notes.Add("t="+time.ToString("F2")+" particle counts cold haze/snow spindrift/snowflakes/wind="+string.Join("/",systems.Select(ps=>ps.particleCount)));
                    Capture(camera,Report+"/Frame"+frame+".png");
                }
            }
            Notes.Add("Peak simultaneous particles: "+maxParticles+"; cap="+systems.Sum(ps=>ps.main.maxParticles));
            Notes.Add("Below-ground samples: "+underground+"; outside-tile samples: "+outside+"; maximum wind change/frame: "+maxJump);
            if(underground>0)Errors.Add("Particles penetrated terrain");if(outside>0)Errors.Add("Particles escaped the tundra tile bounds");
            Notes.Add("Layer peak counts: "+string.Join("/",layerPeaks));
            if(layerPeaks.Any(n=>n==0))Errors.Add("A VFX layer never emitted during the full 15-second run");
            if(maxJump>.1f)Errors.Add("Shared wind is discontinuous");
            var overviewObject=new GameObject("Temporary Validation Camera");var overview=overviewObject.AddComponent<Camera>();overview.CopyFrom(camera);
            overviewObject.AddComponent<UniversalAdditionalCameraData>().SetRenderer(1);
            overview.transform.position=bounds.center+new Vector3(bounds.size.x*.9f+8,bounds.size.z*.9f+9,-bounds.size.z-8);overview.transform.LookAt(bounds.center+Vector3.up*1.6f);
            overview.orthographic=true;overview.orthographicSize=Mathf.Max(9,bounds.size.z*.63f);Capture(overview,Report+"/Overview.png");
            // A close view verifies tiny particles without modifying the user's saved main camera.
            var tile=surfaces[0];
            overview.transform.position=tile.bounds.center+new Vector3(8,10,-12);overview.transform.LookAt(tile.bounds.center+Vector3.up*1.4f);
            overview.orthographicSize=6.8f;Capture(overview,Report+"/Closeup.png");Object.DestroyImmediate(overviewObject);
            controller.windDirection=new Vector2(-1,-.3f);controller.UpdateWind(16);
            if(controller.CurrentWind.x>=0||controller.CurrentWind.z>=0)Errors.Add("Wind direction failed to reverse");
            controller.windDirection=new Vector2(1,.3f);controller.globalVFXIntensity=0;controller.UpdateWind(16);
            if(systems.Any(ps=>ps.emission.rateOverTimeMultiplier>0))Errors.Add("Zero intensity failed to stop emission");
            // The existing pond animates with real editor time. Freeze only its procedural
            // layers via a temporary property block for an exact zero-VFX comparison.
            var pond=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Single(r=>r.name=="Pond_ShallowWater");
            var originalBlock=new MaterialPropertyBlock();pond.GetPropertyBlock(originalBlock);
            var freeze=new MaterialPropertyBlock();pond.GetPropertyBlock(freeze);
            foreach(var property in new[]{"_EnableFlow","_EnableRipples","_EnableWaves","_EnableVariation","_EnableFoam","_EnableWaterfallImpact"})freeze.SetFloat(property,0);
            pond.SetPropertyBlock(freeze);
            foreach(var ps in systems)ps.GetComponent<ParticleSystemRenderer>().enabled=false;
            Capture(camera,Report+"/ZeroReference.png");
            foreach(var ps in systems)ps.GetComponent<ParticleSystemRenderer>().enabled=true;
            Capture(camera,Report+"/IntensityZero.png");
            if(!File.ReadAllBytes(Report+"/ZeroReference.png").SequenceEqual(File.ReadAllBytes(Report+"/IntensityZero.png")))Errors.Add("Zero intensity differs from disabled VFX renderers");
            else Notes.Add("Zero intensity exactly matches disabled particle renderers (pond animation frozen only during this comparison)");
            pond.SetPropertyBlock(originalBlock);
            controller.globalVFXIntensity=1;controller.UpdateWind(16);
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
