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
using Object = UnityEngine.Object;

namespace Darwin.Rendering.Validation
{
    // Offline authoring and regression checks, deliberately outside Assets/Editor.
    public static class MoebiusWoodlandVFXSetup
    {
        static bool rainforest;
        static string ScenePath => "Assets/Rendering/Moebius/Samples/Biomes/Scenes/"+(rainforest?"RainForest":"Forest_Moebius")+".unity";
        static string Output => "Assets/Rendering/Moebius/Content/VFX/"+Name;
        static string Name => rainforest?"Rainforest":"Forest";
        const string Shared="Assets/Rendering/Moebius/Content/VFX/Highland";
        const string Grass="Assets/Rendering/Moebius/Content/VFX/Grassland";
        static string Report => "Validation/"+Name+"VFX";
        static readonly List<string> Notes=new List<string>();
        static readonly List<string> Errors=new List<string>();
        struct Triangle { public Vector3 a,b,c; }
        static readonly List<Triangle> Ground=new List<Triangle>();
        static MeshRenderer[] surfaces;

        public static void RunForestBatch() { rainforest=false; RunBatch(); }
        public static void RunRainforestBatch() { rainforest=true; RunBatch(); }
        static void RunBatch()
        {
            Directory.CreateDirectory(Report);
            Application.logMessageReceived += LogError;
            try
            {
                if(!File.Exists("WoodlandVFX.marker"))throw new Exception("Requires the marked isolated Temp fixture.");
                EditorSceneManager.OpenScene(ScenePath);
                foreach(var old in Object.FindObjectsByType<WoodlandWindController>(FindObjectsSortMode.None))
                    if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old.gameObject)==Output+"/Prefabs/"+Name+"VFX.prefab")Object.DestroyImmediate(old.gameObject);
                var original=Object.FindObjectsByType<Component>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                    .ToDictionary(c=>c.GetInstanceID(),c=>EditorJsonUtility.ToJson(c));
                var camera=Camera.main;
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                ShaderUtil.allowAsyncCompilation=false;
                Capture(camera,Report+"/Before.png");
                surfaces=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .Where(r=>r.enabled && r.gameObject.activeInHierarchy && r.name.StartsWith("Fprest_Block")).OrderBy(r=>GlobalObjectId.GetGlobalObjectIdSlow(r).ToString(),StringComparer.Ordinal).ToArray();
                if(surfaces.Length==0)throw new Exception("No enabled woodland ground surfaces found in "+ScenePath);
                Bounds bounds=surfaces[0].bounds;foreach(var r in surfaces.Skip(1))bounds.Encapsulate(r.bounds);
                Notes.Add("Target: "+ScenePath+"; ground tiles: "+surfaces.Length);
                Notes.Add("Ground bounds: "+bounds);
                foreach(string folder in new[]{"Materials","Meshes","Prefabs"})Directory.CreateDirectory(Output+"/"+folder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var motes=Material(Name+"Motes","Darwin/Moebius Grassland Motes",Grass+"/Textures/MotesAtlas.png");
                var leaves=Material(Name+"Leaves","Darwin/Moebius Grassland Leaves",Grass+"/Textures/LeavesAtlas.png");
                var wisps=Material(Name+"WindWisps","Darwin/Moebius Grassland Wind",Shared+"/Textures/WindAtlas.png");
                Material mist=null;
                if(rainforest)
                {
                    mist=Material("RainforestLowMist","Darwin/Moebius Dust",Shared+"/Textures/DustAtlas.png");
                    mist.SetTexture("_NoiseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Shared+"/Textures/DustNoise.png"));
                    mist.SetFloat("_Opacity",.13f);mist.SetFloat("_Distortion",.018f);
                    mist.SetFloat("_AnimationSpeed",.07f);mist.SetFloat("_IntersectionDistance",.28f);EditorUtility.SetDirty(mist);
                }
                var ground=GroundMesh(surfaces);
                var moteEmission=EmissionMesh(0);var leafEmission=EmissionMesh(1);var wispEmission=EmissionMesh(2);
                var quad=AssetDatabase.LoadAssetAtPath<Mesh>(Shared+"/Meshes/WindAndDustQuad.asset");
                if(!quad)throw new Exception("Missing existing shared particle quad");
                var root=new GameObject(Name+"VFX");var controller=root.AddComponent<WoodlandWindController>();
                controller.animateEditModePreview=false;
                controller.baseWindSpeed=rainforest?.22f:.42f;controller.gustStrength=rainforest?.35f:.6f;
                controller.randomSeed=rainforest?12311:9219;
                controller.motesRate=rainforest?8:14;controller.leavesRate=rainforest?.9f:2.1f;controller.windWispsRate=rainforest?.24f:.5f;
                controller.motesColor=rainforest?new Color(.76f,.88f,.77f,1):new Color(.89f,.9f,.7f,1);
                controller.leavesColor=rainforest?new Color(.62f,.76f,.43f,1):new Color(.69f,.71f,.4f,1);
                controller.windWispsColor=rainforest?new Color(.68f,.83f,.76f,1):new Color(.83f,.87f,.74f,1);
                controller.mistRate=.65f;controller.boundaryFade=.9f;
                controller.localBounds=new Bounds(bounds.center+Vector3.up*2.5f,new Vector3(bounds.size.x,bounds.size.y+7,bounds.size.z));
                var helpers=Child(root,"Surface And Bounds");
                var collision=Child(helpers,"Ground Collision");collision.layer=2;collision.AddComponent<MeshCollider>().sharedMesh=ground;
                var boundaries=new List<BoxCollider>();
                foreach(var surface in surfaces)
                {
                    var volume=Child(helpers,surface.name+" Particle Bounds");volume.layer=2;
                    var box=volume.AddComponent<BoxCollider>();box.isTrigger=true;
                    box.center=surface.bounds.center+Vector3.up*3;
                    box.size=new Vector3(surface.bounds.size.x-.2f,surface.bounds.size.y+7,surface.bounds.size.z-.2f);
                    boundaries.Add(box);
                }
                controller.floatingMotes=System(root,"FloatingMotes",motes,moteEmission,quad,0,boundaries);
                controller.fallingLeaves=System(root,"FallingLeaves",leaves,leafEmission,quad,1,boundaries);
                controller.windWisps=System(root,Name+"WindWisps",wisps,wispEmission,quad,2,boundaries);
                if(rainforest)controller.lowMist=System(root,"LowMist",mist,EmissionMesh(3),quad,3,boundaries);
                controller.RefreshSettings();controller.animateEditModePreview=true;
                PrefabUtility.SaveAsPrefabAssetAndConnect(root,Output+"/Prefabs/"+Name+"VFX.prefab",InteractionMode.AutomatedAction);
                foreach(var pair in original)
                {
                    var component=EditorUtility.InstanceIDToObject(pair.Key) as Component;
                    if(!component || EditorJsonUtility.ToJson(component)!=pair.Value)Errors.Add("Original component changed: "+component);
                }
                Notes.Add("Unchanged existing scene component snapshots: "+original.Count);
                EditorSceneManager.SaveScene(root.scene,ScenePath);AssetDatabase.SaveAssets();
                ValidateAndCapture(controller,camera,bounds);
                foreach(var mat in new[]{motes,leaves,wisps,mist}.Where(m=>m))
                {
                    if(!mat.GetTexture("_BaseMap"))Errors.Add("Missing texture: "+mat.name);
                    foreach(var message in ShaderUtil.GetShaderMessages(mat.shader))
                        if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)Errors.Add(mat.shader.name+": "+message.message);
                }
            }
            catch(Exception e){Errors.Add(e.ToString());}
            Notes.Add("Errors: "+Errors.Count);Notes.AddRange(Errors);File.WriteAllLines(Report+"/Report.txt",Notes);
            EditorApplication.Exit(Errors.Count==0?0:1);
        }
        static void LogError(string message,string stack,LogType type)
        {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)Errors.Add(message);}
        static GameObject Child(GameObject parent,string name)
        {var child=new GameObject(name);child.transform.SetParent(parent.transform,false);return child;}
        static Material Material(string name,string shaderName,string texture)
        {
            var shader=Shader.Find(shaderName);if(!shader)throw new Exception("Missing shader "+shaderName);
            string path=Output+"/Materials/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}else mat.shader=shader;
            mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture));
            mat.SetFloat("_AtlasColumns",2);mat.SetFloat("_AtlasRows",2);EditorUtility.SetDirty(mat);return mat;
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
            return SaveMesh(Name+"Collision",vertices,indices);
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
        static void Patch(List<Vector3> vertices,List<int> indices,Vector3 center,float half)
        {
            int o=vertices.Count;
            vertices.Add(center+new Vector3(-half,0,-half));vertices.Add(center+new Vector3(half,0,-half));
            vertices.Add(center+new Vector3(-half,0,half));vertices.Add(center+new Vector3(half,0,half));
            indices.AddRange(new[]{o,o+2,o+1,o+1,o+2,o+3});
        }
        static Mesh EmissionMesh(int kind)
        {
            var vertices=new List<Vector3>();var indices=new List<int>();
            if(kind==1)
            {
                var trees=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.enabled && r.gameObject.activeInHierarchy &&
                    (r.name.IndexOf("tree",StringComparison.OrdinalIgnoreCase)>=0 || r.name.StartsWith("Hedra",StringComparison.OrdinalIgnoreCase)) && Mathf.Max(r.bounds.size.x,r.bounds.size.z)>.65f && r.bounds.size.y>.45f).OrderBy(r=>GlobalObjectId.GetGlobalObjectIdSlow(r).ToString(),StringComparer.Ordinal).ToArray();
                foreach(var tree in trees)
                {
                    Vector3 center=tree.bounds.center;
                    if(!Height(center.x,center.z,out float y))continue;
                    var tile=surfaces.FirstOrDefault(r=>r.bounds.min.x<=center.x && r.bounds.max.x>=center.x && r.bounds.min.z<=center.z && r.bounds.max.z>=center.z);
                    if(!tile)continue;
                    center.x=Mathf.Clamp(center.x,tile.bounds.min.x+.65f,tile.bounds.max.x-.65f);
                    center.z=Mathf.Clamp(center.z,tile.bounds.min.z+.65f,tile.bounds.max.z-.65f);
                    if(!Height(center.x,center.z,out y))continue;
                    center.y=Mathf.Max(y+.65f,tree.bounds.max.y-.28f);
                    Patch(vertices,indices,center,Mathf.Clamp(Mathf.Min(tree.bounds.extents.x,tree.bounds.extents.z)*.7f,.12f,.5f));
                }
                Notes.Add("Tree canopy leaf patches: "+vertices.Count/4);
            }
            else foreach(var surface in surfaces)
            {
                Bounds b=surface.bounds;
                const float cell=.6f,inset=.85f;
                for(float x=b.min.x+inset;x+cell<b.max.x-inset;x+=cell)
                for(float z=b.min.z+inset;z+cell<b.max.z-inset;z+=cell)
                {
                    if(!Height(x,z,out float y0)||!Height(x+cell,z,out float y1)||!Height(x,z+cell,out float y2)||!Height(x+cell,z+cell,out float y3))continue;
                    if(Mathf.Max(y0,y1,y2,y3)-Mathf.Min(y0,y1,y2,y3)>.45f)continue;
                    float y=Mathf.Max(y0,y1,y2,y3);
                    int layers=kind==0?3:1;
                    for(int layer=0;layer<layers;layer++)Patch(vertices,indices,new Vector3(x+cell*.5f,y+(kind==0?.5f+layer*.7f:kind==3?.5f:1.1f),z+cell*.5f),cell*.5f);
                }
            }
            if(indices.Count<6)throw new Exception("No safe emission area for layer "+kind);
            Notes.Add("Emission layer "+kind+": "+vertices.Count/4+" patches");
            return SaveMesh(kind==0?"MotesEmission":kind==1?"CanopyEmission":kind==3?"MistEmission":"WispsEmission",vertices,indices);
        }
        static ParticleSystem System(GameObject parent,string name,Material material,Mesh mesh,Mesh quad,int kind,List<BoxCollider> boundaries)
        {
            var obj=Child(parent,name);var ps=obj.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.duration=16;main.loop=true;main.prewarm=true;main.playOnAwake=true;
            main.maxParticles=kind==0?160:kind==1?32:kind==3?10:8;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;main.startSpeed=0;main.gravityModifier=0;
            main.startLifetime=new ParticleSystem.MinMaxCurve(kind==0?7:kind==1?4:kind==3?7:4,kind==0?11:kind==1?7:kind==3?10:6);
            main.startColor=kind==1?new ParticleSystem.MinMaxGradient(new Color(.82f,.9f,.69f,1),new Color(1,.94f,.7f,1)):
                new ParticleSystem.MinMaxGradient(new Color(.92f,.98f,.86f,.72f),Color.white);
            main.startSize3D=kind>=2;
            if(kind>=2){main.startSizeY=1;main.startSizeZ=new ParticleSystem.MinMaxCurve(.65f,1.15f);}
            main.startRotation3D=kind>=2;
            if(kind<2)main.startRotation=new ParticleSystem.MinMaxCurve(-Mathf.PI,Mathf.PI);
            var emission=ps.emission;emission.rateOverTime=1;
            var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Mesh;shape.mesh=mesh;
            shape.meshShapeType=ParticleSystemMeshShapeType.Triangle;shape.randomDirectionAmount=0;shape.alignToDirection=false;
            var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;
            velocity.x=new ParticleSystem.MinMaxCurve(0,0);velocity.z=new ParticleSystem.MinMaxCurve(0,0);
            velocity.y=new ParticleSystem.MinMaxCurve(kind==1?-.42f:-.015f,kind==1?-.24f:.045f);
            var noise=ps.noise;noise.enabled=true;noise.strength=.05f;noise.frequency=.28f;
            noise.scrollSpeed=.11f;noise.octaveCount=1;noise.quality=ParticleSystemNoiseQuality.Low;noise.damping=true;
            var color=ps.colorOverLifetime;color.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.2f),new GradientAlphaKey(.9f,.65f),new GradientAlphaKey(0,1)});color.color=gradient;
            if(kind==1){var rotation=ps.rotationOverLifetime;rotation.enabled=true;rotation.z=new ParticleSystem.MinMaxCurve(-1.1f,1.1f);}
            var sheet=ps.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=2;sheet.numTilesY=2;
            sheet.animation=ParticleSystemAnimationType.WholeSheet;sheet.frameOverTime=0;sheet.startFrame=new ParticleSystem.MinMaxCurve(0,1);sheet.cycleCount=1;
            var collision=ps.collision;collision.enabled=kind<2;collision.type=ParticleSystemCollisionType.World;collision.mode=ParticleSystemCollisionMode.Collision3D;
            collision.collidesWith=1<<2;collision.enableDynamicColliders=false;collision.quality=ParticleSystemCollisionQuality.High;
            collision.radiusScale=.12f;collision.lifetimeLoss=1;collision.minKillSpeed=0;collision.maxKillSpeed=100;
            var trigger=ps.trigger;trigger.enabled=true;trigger.radiusScale=.05f;for(int i=0;i<boundaries.Count;i++)trigger.SetCollider(i,boundaries[i]);
            trigger.inside=ParticleSystemOverlapAction.Ignore;trigger.outside=ParticleSystemOverlapAction.Kill;
            trigger.enter=ParticleSystemOverlapAction.Ignore;trigger.exit=ParticleSystemOverlapAction.Kill;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
            renderer.renderMode=kind>=2?ParticleSystemRenderMode.Mesh:ParticleSystemRenderMode.Billboard;
            if(kind>=2)renderer.mesh=quad;renderer.enableGPUInstancing=false;
            renderer.alignment=kind>=2?ParticleSystemRenderSpace.World:ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.sortMode=ParticleSystemSortMode.Distance;renderer.maxParticleSize=1;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV});
            return ps;
        }
        static void ValidateAndCapture(WoodlandWindController controller,Camera camera,Bounds bounds)
        {
            var systems=new[]{controller.floatingMotes,controller.fallingLeaves,controller.windWisps,controller.lowMist}.Where(ps=>ps).ToArray();
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
                    Notes.Add("t="+time.ToString("F2")+" particle counts motes/leaves/wisps/mist="+string.Join("/",systems.Select(ps=>ps.particleCount)));
                    Capture(camera,Report+"/Frame"+frame+".png");
                }
            }
            Notes.Add("Peak simultaneous particles: "+maxParticles+"; cap="+systems.Sum(ps=>ps.main.maxParticles));
            Notes.Add("Below-ground samples: "+underground+"; outside-tile samples: "+outside+"; maximum wind change/frame: "+maxJump);
            if(underground>0)Errors.Add("Particles penetrated terrain");if(outside>0)Errors.Add("Particles escaped the woodland tile bounds");
            Notes.Add("Layer peak counts: "+string.Join("/",layerPeaks));
            if(layerPeaks.Any(n=>n==0))Errors.Add("A VFX layer never emitted during the full 15-second run");
            if(maxJump>.1f)Errors.Add("Shared wind is discontinuous");
            var overviewObject=new GameObject("Temporary Validation Camera");var overview=overviewObject.AddComponent<Camera>();overview.CopyFrom(camera);
            overviewObject.AddComponent<UniversalAdditionalCameraData>().SetRenderer(1);
            overview.transform.position=bounds.center+new Vector3(bounds.size.x*.9f+8,bounds.size.z*.9f+9,-bounds.size.z-8);overview.transform.LookAt(bounds.center+Vector3.up*1.6f);
            overview.orthographic=true;overview.orthographicSize=Mathf.Max(9,bounds.size.z*.63f);Capture(overview,Report+"/Overview.png");
            // A close view verifies tiny particles without modifying the user's saved main camera.
            var tile=surfaces.OrderBy(r=>r.bounds.center.z).Last();
            overview.transform.position=tile.bounds.center+new Vector3(8,10,-12);overview.transform.LookAt(tile.bounds.center+Vector3.up*1.4f);
            overview.orthographicSize=6.8f;Capture(overview,Report+"/Closeup.png");Object.DestroyImmediate(overviewObject);
            controller.windDirection=new Vector2(-1,-.3f);controller.UpdateWind(16);
            if(controller.CurrentWind.x>=0||controller.CurrentWind.z>=0)Errors.Add("Wind direction failed to reverse");
            controller.windDirection=new Vector2(1,.3f);controller.globalVFXIntensity=0;controller.UpdateWind(16);
            if(systems.Any(ps=>ps.emission.rateOverTimeMultiplier>0))Errors.Add("Zero intensity failed to stop emission");
            Capture(camera,Report+"/IntensityZero.png");
            if(!File.ReadAllBytes(Report+"/Before.png").SequenceEqual(File.ReadAllBytes(Report+"/IntensityZero.png")))Errors.Add("Zero intensity changed the baseline render");
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
