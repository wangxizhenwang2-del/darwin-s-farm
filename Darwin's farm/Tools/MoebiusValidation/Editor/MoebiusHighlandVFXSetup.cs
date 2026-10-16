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
    // Offline authoring only. This file is intentionally outside Assets/Editor.
    // Run through BuildHighlandVFX.ps1 in the guarded Temp fixture.
    public static class MoebiusHighlandVFXSetup
    {
        const string ScenePath="Assets/Rendering/Moebius/Samples/Biomes/Scenes/Highland_Animals.unity";
        const string Output="Assets/Rendering/Moebius/Content/VFX/Highland";
        const string Report="Validation/HighlandVFX";
        static readonly List<string> Notes=new List<string>();
        static readonly List<string> Errors=new List<string>();
        struct Triangle { public Vector3 a,b,c; }
        static readonly List<Triangle> Ground=new List<Triangle>();

        public static void RunBatch()
        {
            int exit=0;
            Directory.CreateDirectory(Report);
            Application.logMessageReceived += LogError;
            try
            {
                if(!File.Exists("HighlandVFX.marker")) throw new Exception("Authoring is restricted to the marked Temp fixture.");
                EditorSceneManager.OpenScene(ScenePath);
                var old=Object.FindObjectsByType<MoebiusWindController>(FindObjectsSortMode.None);
                foreach(var wind in old)
                    if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(wind.gameObject)==Output+"/Prefabs/HighlandWindVFX.prefab")
                        Object.DestroyImmediate(wind.gameObject);
                var original=Object.FindObjectsByType<Component>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                    .ToDictionary(c=>c.GetInstanceID(), c=>EditorJsonUtility.ToJson(c));
                var camera=Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c=>c.CompareTag("MainCamera"));
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline; QualitySettings.renderPipeline=pipeline;
                MoebiusBillboardCameraGlobals.Initialize();
                var animals=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.name.Contains("2D Billboard")).ToArray();
                if(animals.Length!=5)throw new Exception("Expected five existing billboard animals; found "+animals.Length);
                foreach(var animal in animals)
                    if(!animal.sharedMaterial || !animal.sharedMaterial.GetTexture("_BaseMap"))throw new Exception("Missing animal artwork: "+animal.name);
                Notes.Add("Existing billboard animals with valid artwork: "+animals.Length);
                Capture(camera,Report+"/Before.png");
                var groundRenderers=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .Where(r=>r.enabled && r.gameObject.activeInHierarchy && r.name=="Ground - Paint Here").ToArray();
                if(groundRenderers.Length==0) throw new Exception("No named Highland ground surface found; refusing to guess a flat height.");
                Bounds bounds=groundRenderers[0].bounds;
                foreach(var r in groundRenderers.Skip(1)) bounds.Encapsulate(r.bounds);
                Notes.Add("Ground bounds: "+bounds+"; existing colliders: "+Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Length);
                foreach(string folder in new[]{"Textures","Materials","Meshes","Prefabs"}) Directory.CreateDirectory(Output+"/"+folder);
                GenerateTextures();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var dust=Material("GroundDust","Darwin/Moebius Dust","DustAtlas");
                dust.SetTexture("_NoiseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Output+"/Textures/DustNoise.png"));
                dust.SetFloat("_Opacity",0.46f);
                var debris=Material("PlantDebris","Darwin/Moebius Debris","DebrisAtlas");
                var strokes=Material("WindStrokes","Darwin/Moebius Wind","WindAtlas");
                strokes.SetFloat("_Opacity",0.9f);
                foreach(var material in new[]{dust,debris,strokes}) EditorUtility.SetDirty(material);
                Mesh colliderMesh=GroundMesh(groundRenderers);
                Mesh emissionMesh=EmissionMesh(bounds,groundRenderers,false);
                Mesh windEmission=EmissionMesh(bounds,groundRenderers,true);
                Mesh quad=Quad();
                var root=new GameObject("HighlandVFX");
                var controller=root.AddComponent<MoebiusWindController>();
                controller.animateEditModePreview=false;
                controller.baseWindSpeed=1.3f;
                controller.ambientDustRate=5.2f;controller.gustDustRate=3;
                controller.debrisRate=1.4f;controller.windStreakRate=.5f;
                controller.dustColor=new Color(.79f,.71f,.53f,1);
                controller.debrisColor=new Color(.58f,.47f,.27f,1);
                controller.windLineColor=new Color(.72f,.73f,.62f,1);
                controller.dustSizeRange=new Vector2(1.5f,2.9f);
                controller.debrisSizeRange=new Vector2(.35f,.65f);
                controller.windLengthRange=new Vector2(4.5f,7);
                controller.boundaryFade=1.5f;
                controller.localBounds=new Bounds(new Vector3(bounds.center.x,bounds.center.y+2,bounds.center.z),
                    new Vector3(bounds.size.x, bounds.size.y+5,bounds.size.z));
                var helpers=Child(root,"Surface And Bounds");
                var collision=Child(helpers,"Ground Collision"); collision.layer=2;
                collision.AddComponent<MeshCollider>().sharedMesh=colliderMesh;
                var volume=Child(helpers,"Particle Boundary"); volume.layer=2;
                var box=volume.AddComponent<BoxCollider>(); box.isTrigger=true;
                box.center=controller.localBounds.center; box.size=controller.localBounds.size;
                var dustGroup=Child(root,"GroundDust");
                controller.ambientDust=System(dustGroup,"AmbientDust",dust,emissionMesh,quad,0,box);
                controller.gustDust=System(dustGroup,"GustDust",dust,emissionMesh,quad,1,box);
                controller.floatingDebris=System(root,"FloatingDebris",debris,emissionMesh,quad,2,box);
                controller.windStreaks=System(root,"WindStreaks",strokes,windEmission,quad,3,box);
                controller.RefreshSettings();
                controller.animateEditModePreview=true;
                PrefabUtility.SaveAsPrefabAssetAndConnect(root,Output+"/Prefabs/HighlandWindVFX.prefab",InteractionMode.AutomatedAction);
                // Assert existing serialized components are unchanged, including the user's camera overrides.
                foreach(var pair in original)
                {
                    var component=EditorUtility.InstanceIDToObject(pair.Key) as Component;
                    if(!component || EditorJsonUtility.ToJson(component)!=pair.Value) Errors.Add("Existing scene component changed: "+component);
                }
                Notes.Add("Existing scene component snapshots checked: "+original.Count);
                EditorSceneManager.SaveScene(root.scene,ScenePath);
                AssetDatabase.SaveAssets();
                ValidateAndCapture(controller,camera);
                foreach(var shader in new[]{dust.shader,debris.shader,strokes.shader,Shader.Find("Darwin/Moebius NPR")})
                    foreach(var error in ShaderUtil.GetShaderMessages(shader))
                        if(error.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) Errors.Add(shader.name+": "+error.message);
                foreach(var material in new[]{dust,debris,strokes})
                    if(!material.GetTexture("_BaseMap")) Errors.Add("Missing texture on "+material.name);
            }
            catch(Exception exception){Errors.Add(exception.ToString());exit=1;}
            Notes.Add("Errors: "+Errors.Count); Notes.AddRange(Errors);
            File.WriteAllLines(Report+"/Report.txt",Notes);
            EditorApplication.Exit(Errors.Count==0?exit:1);
        }
        static void LogError(string message,string stack,LogType type)
        {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) Errors.Add(message);}
        static GameObject Child(GameObject parent,string name)
        {var child=new GameObject(name);child.transform.SetParent(parent.transform,false);return child;}
        static Material Material(string name,string shaderName,string texture)
        {
            string path=Output+"/Materials/"+name+".mat";
            var shader=Shader.Find(shaderName);if(!shader)throw new Exception("Missing shader "+shaderName);
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}else material.shader=shader;
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Output+"/Textures/"+texture+".png"));
            material.SetFloat("_AtlasColumns",2); material.SetFloat("_AtlasRows",2);
            return material;
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
            return SaveMesh("HighlandCollision",vertices,indices);
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
            const float cell=0.42f, inset=1.25f;
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
                    Vector3 target=new Vector3(cx+.8f,y+1.35f,cz+.24f);
                    Vector3 view=target-Camera.main.transform.position;
                    var ray=new Ray(Camera.main.transform.position,view.normalized);
                    float length=view.magnitude;
                    // Conservative visibility test for this fixed diorama camera. The
                    // transparent strokes still depth-test normally from every view.
                    if(obstacles.Any(r=>r.bounds.IntersectRay(ray,out float enter) && enter<length-.15f))continue;
                }
                if(obstacles.Any(r=>r.bounds.min.y<y+0.9f && Mathf.Abs(cx-r.bounds.center.x)<r.bounds.extents.x*0.6f+0.16f && Mathf.Abs(cz-r.bounds.center.z)<r.bounds.extents.z*0.6f+0.16f))continue;
                if(foreground && obstacles.Any(r=>r.name.Contains("2D Billboard") &&
                    Enumerable.Range(0,4).Any(step=>Mathf.Abs(cx+step*2-r.bounds.center.x)<r.bounds.extents.x+1.5f &&
                        Mathf.Abs(cz+step*.6f-r.bounds.center.z)<r.bounds.extents.z+1.5f)))continue;
                if(!Height(x,z,out float y0)||!Height(x+cell,z,out float y1)||!Height(x,z+cell,out float y2)||!Height(x+cell,z+cell,out float y3))continue;
                if(Mathf.Max(y0,y1,y2,y3)-Mathf.Min(y0,y1,y2,y3)>0.18f)continue;
                int offset=vertices.Count;
                vertices.Add(new Vector3(x,y0,z));vertices.Add(new Vector3(x+cell,y1,z));
                vertices.Add(new Vector3(x,y2,z+cell));vertices.Add(new Vector3(x+cell,y3,z+cell));
                indices.AddRange(new[]{offset,offset+2,offset+1,offset+1,offset+2,offset+3});
            }
            if(indices.Count<6)throw new Exception("No safe emission area after ground/obstacle filtering");
            Notes.Add((foreground?"Wind":"Surface")+" emission patches: "+vertices.Count/4+"; upward ground triangles: "+Ground.Count);
            return SaveMesh(foreground?"HighlandWindEmission":"HighlandEmission",vertices,indices);
        }
        static Mesh Quad()=>SaveMesh("WindAndDustQuad",new List<Vector3>{new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f)},new List<int>{0,2,1,1,2,3},new List<Vector2>{Vector2.zero,Vector2.right,Vector2.up,Vector2.one});

        static ParticleSystem System(GameObject parent,string name,Material material,Mesh emissionMesh,Mesh quad,int kind,BoxCollider boundary)
        {
            var gameObject=Child(parent,name);
            gameObject.transform.localPosition=Vector3.up*(kind<2?0.22f:kind==2?0.85f:1.35f);
            var ps=gameObject.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;
            main.duration=12;main.loop=true;main.prewarm=true;main.playOnAwake=true;
            main.maxParticles=kind==0?96:kind==1?48:kind==2?40:12;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            main.startSpeed=0;main.gravityModifier=0;
            main.startLifetime=new ParticleSystem.MinMaxCurve(kind<2?3.2f:kind==2?4.5f:3.5f,kind<2?4.8f:kind==2?6.2f:5);
            main.startColor=new ParticleSystem.MinMaxGradient(new Color(.93f,.94f,.84f,.8f),Color.white);
            if(kind==3)main.startColor=Color.white;
            main.startSize3D=kind!=2;
            if(kind!=2){main.startSizeY=1;main.startSizeZ=new ParticleSystem.MinMaxCurve(kind==3?1.6f:.32f,kind==3?2.5f:.6f);}
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
            collision.lifetimeLoss=kind<2?1:0;collision.minKillSpeed=0;collision.maxKillSpeed=100;
            var trigger=ps.trigger;trigger.enabled=true;trigger.SetCollider(0,boundary);
            trigger.inside=ParticleSystemOverlapAction.Ignore;trigger.outside=ParticleSystemOverlapAction.Kill;
            trigger.enter=ParticleSystemOverlapAction.Ignore;trigger.exit=ParticleSystemOverlapAction.Kill;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
            renderer.renderMode=kind==2?ParticleSystemRenderMode.Billboard:ParticleSystemRenderMode.Mesh;
            if(kind!=2)renderer.mesh=quad;
            renderer.enableGPUInstancing=false;
            renderer.alignment=ParticleSystemRenderSpace.World;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.sortMode=ParticleSystemSortMode.Distance;renderer.maxParticleSize=1;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV});
            return ps;
        }

        static void GenerateTextures()
        {
            Texture("DustAtlas",1024,512,(u,v,id)=>{
                float x=u*2-1,y=v*2-1;
                float taper=Mathf.Pow(Mathf.Clamp01(1-x*x),.7f);
                float center=.09f*Mathf.Sin(x*5+id*2)+.06f*Mathf.Sin(x*9+id);
                float width=(.21f+.07f*Mathf.Sin(x*8+id*1.7f)+.05f*Mathf.PerlinNoise(u*12,id+2))*taper;
                float coverage=Smooth(-.09f,.075f,width-Mathf.Abs(y-center));
                float tail=Smooth(-.035f,.035f,.035f*taper-Mathf.Abs(y+.27f+.06f*Mathf.Sin(x*4+id)));
                tail*=Smooth(-.9f,-.5f,x)*(1-Smooth(.15f,.6f,x))*.5f;
                float border=Smooth(0,.1f,u)*(1-Smooth(.9f,1,u));
                float grain=.78f+.22f*Mathf.PerlinNoise(u*28+id*13,v*11);
                return new Color(1,1,1,Mathf.Max(coverage,tail)*border*grain);
            });
            Texture("DebrisAtlas",512,512,(u,v,id)=>{
                Vector2 p=new Vector2(u,v);
                float distance;
                if(id==0)
                {
                    distance=Segment(p,new Vector2(.3f,.2f),new Vector2(.67f,.8f))-.018f;
                    distance=Mathf.Min(distance,Segment(p,new Vector2(.47f,.46f),new Vector2(.36f,.7f))-.013f);
                }
                else
                {
                    Vector2[] polygon=id==1?new[]{new Vector2(.28f,.16f),new Vector2(.35f,.57f),new Vector2(.64f,.86f),new Vector2(.68f,.51f),new Vector2(.49f,.23f)}:
                        id==2?new[]{new Vector2(.25f,.2f),new Vector2(.4f,.23f),new Vector2(.65f,.4f),new Vector2(.68f,.66f),new Vector2(.56f,.86f),new Vector2(.53f,.8f),new Vector2(.59f,.57f),new Vector2(.53f,.41f),new Vector2(.35f,.3f)}:
                        new[]{new Vector2(.27f,.24f),new Vector2(.33f,.67f),new Vector2(.45f,.8f),new Vector2(.5f,.67f),new Vector2(.69f,.75f),new Vector2(.62f,.38f),new Vector2(.45f,.21f)};
                    distance=Polygon(p,polygon);
                }
                float coverage=1-Smooth(-.003f,.005f,distance);
                float ink=Smooth(.002f,.014f,-distance);
                if(id==1)ink*=.8f+.2f*Smooth(.003f,.008f,Segment(p,new Vector2(.32f,.2f),new Vector2(.62f,.81f)));
                return new Color(ink,ink,ink,coverage);
            });
            var curves=new Vector2[4][];
            for(int id=0;id<4;id++)
            {
                curves[id]=new Vector2[65];
                for(int i=0;i<65;i++)
                {
                    float t=i/64f;
                    float x=Mathf.Lerp(.07f,.94f,t);
                    float y=.5f+.12f*Mathf.Sin(t*Mathf.PI*(id==2?2.1f:1.4f)+id*.8f)+.027f*Mathf.Sin(t*11+id);
                    if(id==3)y+=.11f*Mathf.Exp(-Mathf.Pow((t-.8f)*6,2));
                    curves[id][i]=new Vector2(x,y);
                }
            }
            Texture("WindAtlas",1024,512,(u,v,id)=>{
                Vector2 p=new Vector2(u,v*.5f);float distance=10;
                for(int i=1;i<curves[id].Length;i++)
                    distance=Mathf.Min(distance,Segment(p,Vector2.Scale(curves[id][i-1],new Vector2(1,.5f)),Vector2.Scale(curves[id][i],new Vector2(1,.5f))));
                float taper=Mathf.Pow(Mathf.Clamp01(Mathf.Sin(u*Mathf.PI)),.6f);
                float width=(.011f+.003f*Mathf.Sin(u*18+id))*taper;
                float alpha=1-Smooth(width-.0013f,width+.0013f,distance);
                return new Color(1,1,1,alpha);
            });
            var noiseTexture=new Texture2D(128,128,TextureFormat.RGBA32,false,true);
            var pixels=new Color[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                // Periodic sums make opposite edges join without a scrolling seam.
                float u=x/128f*Mathf.PI*2,v=y/128f*Mathf.PI*2;
                pixels[y*128+x]=new Color(.5f+.18f*Mathf.Sin(u+Mathf.Sin(v))+.12f*Mathf.Sin(v*2-u),
                    .5f+.17f*Mathf.Cos(v+Mathf.Cos(u*2))+.12f*Mathf.Sin(u*2+v),.5f,1);
            }
            noiseTexture.SetPixels(pixels);noiseTexture.Apply();File.WriteAllBytes(Output+"/Textures/DustNoise.png",noiseTexture.EncodeToPNG());Object.DestroyImmediate(noiseTexture);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(string name in new[]{"DustAtlas","DebrisAtlas","WindAtlas","DustNoise"})
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(Output+"/Textures/"+name+".png");
                importer.textureType=TextureImporterType.Default;importer.sRGBTexture=false;
                importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=name!="DustNoise";
                importer.wrapMode=name=="DustNoise"?TextureWrapMode.Repeat:TextureWrapMode.Clamp;
                importer.filterMode=FilterMode.Trilinear;importer.mipmapEnabled=true;
                importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.maxTextureSize=1024;
                importer.SaveAndReimport();
            }
        }
        static void Texture(string name,int width,int height,Func<float,float,int,Color> sample)
        {
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false,true);var pixels=new Color[width*height];
            int cellW=width/2,cellH=height/2;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)pixels[y*width+x]=sample((x%cellW+.5f)/cellW,(y%cellH+.5f)/cellH,(y/cellH)*2+x/cellW);
            texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(Output+"/Textures/"+name+".png",texture.EncodeToPNG());Object.DestroyImmediate(texture);
        }
        static float Smooth(float a,float b,float value)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,value));
        static float Segment(Vector2 p,Vector2 a,Vector2 b)
        {Vector2 delta=b-a;return (p-a-delta*Mathf.Clamp01(Vector2.Dot(p-a,delta)/Mathf.Max(delta.sqrMagnitude,1e-8f))).magnitude;}
        static float Polygon(Vector2 p,Vector2[] points)
        {
            float distance=10;bool inside=false;
            for(int i=0,j=points.Length-1;i<points.Length;j=i++)
            {
                distance=Mathf.Min(distance,Segment(p,points[j],points[i]));
                if((points[i].y>p.y)!=(points[j].y>p.y) && p.x<(points[j].x-points[i].x)*(p.y-points[i].y)/(points[j].y-points[i].y)+points[i].x)inside=!inside;
            }
            return inside?-distance:distance;
        }

        static void ValidateAndCapture(MoebiusWindController controller,Camera camera)
        {
            var systems=new[]{controller.ambientDust,controller.gustDust,controller.floatingDebris,controller.windStreaks};
            foreach(var ps in systems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            Physics.SyncTransforms();
            int underground=0,maxParticles=0;float largestJump=0;Vector3 previous=Vector3.zero;
            var particles=new ParticleSystem.Particle[128];
            for(int frame=0;frame<720;frame++)
            {
                float time=frame/60f;controller.UpdateWind(time);
                if(frame>0)largestJump=Mathf.Max(largestJump,(controller.CurrentWind-previous).magnitude);
                previous=controller.CurrentWind;
                foreach(var ps in systems)ps.Simulate(1f/60,false,frame==0,false);
                int count=systems.Sum(ps=>ps.particleCount);maxParticles=Mathf.Max(maxParticles,count);
                for(int i=0;i<2;i++)
                {
                    int n=systems[i].GetParticles(particles);
                    for(int p=0;p<n;p++)
                        if(Height(particles[p].position.x,particles[p].position.z,out float ground) && particles[p].position.y<ground-.04f)
                        {
                            if(underground<5)Notes.Add("Underground sample frame="+frame+" particle="+particles[p].position+" ground="+ground+" age="+(particles[p].startLifetime-particles[p].remainingLifetime));
                            underground++;
                        }
                }
                if(frame==239 || frame==479 || frame==719)
                {
                    Notes.Add("t="+time.ToString("F2")+": particles="+string.Join("/",systems.Select(ps=>ps.particleCount))+"; wind="+controller.CurrentWind);
                    Capture(camera,Report+"/Frame"+frame+".png");
                }
            }
            Notes.Add("Maximum simultaneous particles: "+maxParticles+" / configured cap "+systems.Sum(ps=>ps.main.maxParticles));
            Notes.Add("Dust samples below ground: "+underground+"; largest per-frame wind delta: "+largestJump);
            if(underground>0)Errors.Add("Dust passed below the ground collision surface");
            if(largestJump>0.1f)Errors.Add("Discontinuous shared wind");
            controller.windDirection=new Vector2(-1,-.3f);controller.UpdateWind(13);
            if(controller.CurrentWind.x>=0||controller.CurrentWind.z>=0)Errors.Add("Wind direction did not reverse");
            controller.windDirection=new Vector2(1,.3f);
            controller.globalVFXIntensity=0;controller.UpdateWind(13);
            if(systems.Any(ps=>ps.emission.rateOverTimeMultiplier>0))Errors.Add("Zero intensity failed to stop emission");
            Capture(camera,Report+"/IntensityZero.png");
            controller.globalVFXIntensity=1;controller.UpdateWind(13);
            // Scene was saved before simulation and before these temporary test values.
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
