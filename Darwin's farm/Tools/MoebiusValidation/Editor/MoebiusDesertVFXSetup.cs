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
    public static class MoebiusDesertVFXSetup
    {
        const string ScenePath="Assets/Rendering/Moebius/Samples/Biomes/Scenes/Desert_Moebius.unity";
        const string Output="Assets/Rendering/Moebius/Content/VFX/Desert";
        const string Shared="Assets/Rendering/Moebius/Content/VFX/Highland";
        static readonly List<string> Notes=new List<string>();
        static readonly List<string> Errors=new List<string>();
        const string Report="Validation/DesertVFX";
        struct Triangle {public Vector3 a,b,c;}
        static readonly List<Triangle> Ground=new List<Triangle>();
        static MeshRenderer[] surfaces;
        public static void RunBatch()
        {
            Directory.CreateDirectory(Report);Application.logMessageReceived+=LogError;
            try
            {
                if(!File.Exists("DesertVFX.marker"))throw new Exception("Use the marked isolated Temp fixture.");
                EditorSceneManager.OpenScene(ScenePath);
                foreach(var old in Object.FindObjectsByType<MoebiusWindController>(FindObjectsSortMode.None))
                    if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old.gameObject)==Output+"/Prefabs/DesertVFX.prefab")Object.DestroyImmediate(old.gameObject);
                var original=Object.FindObjectsByType<Component>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                    .ToDictionary(c=>c.GetInstanceID(),c=>EditorJsonUtility.ToJson(c));
                var camera=Camera.main;
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                ShaderUtil.allowAsyncCompilation=false;Capture(camera,Report+"/Before.png");
                surfaces=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .Where(r=>r.enabled && r.gameObject.activeInHierarchy && r.name=="pPlane1").ToArray();
                if(surfaces.Length!=1)throw new Exception("Expected the named Desert pPlane1 ground, found "+surfaces.Length);
                Bounds bounds=surfaces[0].bounds;Notes.Add("Target: "+ScenePath+"; ground: pPlane1; bounds: "+bounds);
                foreach(var folder in new[]{"Materials","Meshes","Prefabs"})Directory.CreateDirectory(Output+"/"+folder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var sand=Material("SurfaceSand","Darwin/Moebius Dust",Shared+"/Textures/DustAtlas.png");
                var gust=Material("GustSand","Darwin/Moebius Dust",Shared+"/Textures/DustAtlas.png");
                foreach(var mat in new[]{sand,gust})
                {
                    mat.SetTexture("_NoiseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Shared+"/Textures/DustNoise.png"));
                    mat.SetFloat("_Distortion",.025f);mat.SetFloat("_AnimationSpeed",.12f);mat.SetFloat("_IntersectionDistance",.18f);
                }
                sand.SetFloat("_Opacity",.32f);gust.SetFloat("_Opacity",.43f);
                var grains=Material("AirborneSand","Darwin/Moebius Grassland Motes","Assets/Rendering/Moebius/Content/VFX/Grassland/Textures/MotesAtlas.png");
                grains.SetFloat("_Opacity",.65f);grains.SetFloat("_Distortion",0);
                var strokes=Material("DesertWind","Darwin/Moebius Wind",Shared+"/Textures/WindAtlas.png");strokes.SetFloat("_Opacity",.65f);
                foreach(var mat in new[]{sand,gust,grains,strokes})EditorUtility.SetDirty(mat);
                Mesh ground=GroundMesh(surfaces),emission=EmissionMesh(bounds,surfaces,false),windEmission=EmissionMesh(bounds,surfaces,true);
                var quad=AssetDatabase.LoadAssetAtPath<Mesh>(Shared+"/Meshes/WindAndDustQuad.asset");if(!quad)throw new Exception("Missing shared particle quad");
                var root=new GameObject("DesertVFX");var controller=root.AddComponent<MoebiusWindController>();controller.animateEditModePreview=false;
                controller.baseWindSpeed=.95f;controller.gustStrength=1;controller.gustFrequency=.13f;controller.turbulenceStrength=.12f;
                controller.randomSeed=4507;controller.ambientDustRate=6;controller.gustDustRate=3.5f;controller.debrisRate=5.5f;controller.windStreakRate=.5f;
                controller.dustColor=new Color(.82f,.66f,.41f,1);controller.debrisColor=new Color(.9f,.77f,.49f,1);controller.windLineColor=new Color(.91f,.8f,.6f,1);
                controller.dustSizeRange=new Vector2(1.3f,2.3f);controller.debrisSizeRange=new Vector2(.09f,.18f);controller.windLengthRange=new Vector2(2.6f,4);
                controller.boundaryFade=1.1f;controller.localBounds=new Bounds(bounds.center+Vector3.up*2,new Vector3(bounds.size.x-.2f,bounds.size.y+6,bounds.size.z-.2f));
                var helpers=Child(root,"Surface And Bounds");var collision=Child(helpers,"Ground Collision");collision.layer=2;collision.AddComponent<MeshCollider>().sharedMesh=ground;
                var volume=Child(helpers,"Particle Boundary");volume.layer=2;var box=volume.AddComponent<BoxCollider>();box.isTrigger=true;box.center=controller.localBounds.center;box.size=controller.localBounds.size;
                controller.ambientDust=System(root,"SurfaceSand",sand,emission,quad,0,box);
                controller.gustDust=System(root,"GustSand",gust,emission,quad,1,box);
                controller.floatingDebris=System(root,"AirborneSand",grains,emission,quad,2,box);
                controller.windStreaks=System(root,"DesertWindStreaks",strokes,windEmission,quad,3,box);
                controller.RefreshSettings();controller.animateEditModePreview=true;
                PrefabUtility.SaveAsPrefabAssetAndConnect(root,Output+"/Prefabs/DesertVFX.prefab",InteractionMode.AutomatedAction);
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
            return SaveMesh("DesertCollision",vertices,indices);
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
        static Mesh EmissionMesh(Bounds bounds,MeshRenderer[] surfaces,bool foreground)
        {
            var obstacles=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r=>r.enabled && r.gameObject.activeInHierarchy && !surfaces.Contains(r) && r.bounds.size.y>0.75f).ToArray();
            var vertices=new List<Vector3>();var indices=new List<int>();
            const float cell=0.24f, inset=1.0f;
            for(float x=bounds.min.x+inset;x+cell<bounds.max.x-inset;x+=cell)
            for(float z=bounds.min.z+inset;z+cell<bounds.max.z-inset;z+=cell)
            {
                float cx=x+cell/2,cz=z+cell/2;
                if(foreground)
                {
                    Vector3 towardCamera=Camera.main.transform.position-bounds.center;towardCamera.y=0;towardCamera.Normalize();
                    if(Vector3.Dot(new Vector3(cx-bounds.center.x,0,cz-bounds.center.z),towardCamera)<0.5f)continue;
                }
                if(!Height(cx,cz,out float y))continue;
                if(foreground)
                {
                    Vector3 target=new Vector3(cx+.6f,y+.8f,cz+.18f);
                    Vector3 view=target-Camera.main.transform.position;
                    var ray=new Ray(Camera.main.transform.position,view.normalized);
                    float length=view.magnitude;
                    // Conservative visibility test for this fixed diorama camera. The
                    // transparent strokes still depth-test normally from every view.
                    if(obstacles.Any(r=>r.bounds.IntersectRay(ray,out float enter) && enter<length-.15f))continue;
                }
                if(obstacles.Any(r=>r.bounds.min.y<y+0.9f && Mathf.Abs(cx-r.bounds.center.x)<r.bounds.extents.x*0.6f+0.16f && Mathf.Abs(cz-r.bounds.center.z)<r.bounds.extents.z*0.6f+0.16f))continue;
                if(!Height(x,z,out float y0)||!Height(x+cell,z,out float y1)||!Height(x,z+cell,out float y2)||!Height(x+cell,z+cell,out float y3))continue;
                if(Mathf.Max(y0,y1,y2,y3)-Mathf.Min(y0,y1,y2,y3)>0.18f)continue;
                int offset=vertices.Count;
                vertices.Add(new Vector3(x,y0,z));vertices.Add(new Vector3(x+cell,y1,z));
                vertices.Add(new Vector3(x,y2,z+cell));vertices.Add(new Vector3(x+cell,y3,z+cell));
                indices.AddRange(new[]{offset,offset+2,offset+1,offset+1,offset+2,offset+3});
            }
            if(indices.Count<6)throw new Exception("No safe emission area after ground/obstacle filtering");
            Notes.Add((foreground?"Wind":"Surface")+" emission patches: "+vertices.Count/4+"; upward ground triangles: "+Ground.Count);
            return SaveMesh(foreground?"DesertWindEmission":"DesertSandEmission",vertices,indices);
        }
        static ParticleSystem System(GameObject parent,string name,Material material,Mesh emissionMesh,Mesh quad,int kind,BoxCollider boundary)
        {
            var gameObject=Child(parent,name);
            gameObject.transform.localPosition=Vector3.up*(kind<2?0.18f:kind==2?0.65f:.8f);
            var ps=gameObject.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;
            main.duration=12;main.loop=true;main.prewarm=true;main.playOnAwake=true;
            main.maxParticles=kind==0?96:kind==1?48:kind==2?48:8;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            main.startSpeed=0;main.gravityModifier=0;
            main.startLifetime=new ParticleSystem.MinMaxCurve(kind<2?3.2f:kind==2?4.5f:3.5f,kind<2?4.8f:kind==2?6.2f:5);
            main.startColor=new ParticleSystem.MinMaxGradient(new Color(.93f,.94f,.84f,.8f),Color.white);
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
            velocity.y=new ParticleSystem.MinMaxCurve(kind<2?0.004f:-0.04f,kind<2?0.018f:0.06f);
            var noise=ps.noise;noise.enabled=true;noise.strength=.05f;noise.frequency=.35f;
            noise.scrollSpeed=.15f;noise.octaveCount=1;noise.quality=ParticleSystemNoiseQuality.Low;noise.damping=true;
            var color=ps.colorOverLifetime;color.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.18f),new GradientAlphaKey(.85f,.65f),new GradientAlphaKey(0,1)});
            color.color=gradient;
            var size=ps.sizeOverLifetime;size.enabled=true;
            size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.8f),new Keyframe(.25f,1),new Keyframe(1,kind<2?1.25f:1)));
            if(kind==2){var rotation=ps.rotationOverLifetime;rotation.enabled=true;rotation.z=new ParticleSystem.MinMaxCurve(-1.2f,1.2f);}
            var sheet=ps.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=2;sheet.numTilesY=2;
            sheet.animation=ParticleSystemAnimationType.WholeSheet;sheet.frameOverTime=0;
            sheet.startFrame=new ParticleSystem.MinMaxCurve(0,1);sheet.cycleCount=1;
            var collision=ps.collision;collision.enabled=kind<3;collision.type=ParticleSystemCollisionType.World;
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
                    Notes.Add("t="+time.ToString("F2")+" particle counts surface sand/gust sand/airborne grains/wind="+string.Join("/",systems.Select(ps=>ps.particleCount)));
                    Capture(camera,Report+"/Frame"+frame+".png");
                }
            }
            Notes.Add("Peak simultaneous particles: "+maxParticles+"; cap="+systems.Sum(ps=>ps.main.maxParticles));
            Notes.Add("Below-ground samples: "+underground+"; outside-tile samples: "+outside+"; maximum wind change/frame: "+maxJump);
            if(underground>0)Errors.Add("Particles penetrated terrain");if(outside>0)Errors.Add("Particles escaped the desert tile bounds");
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
