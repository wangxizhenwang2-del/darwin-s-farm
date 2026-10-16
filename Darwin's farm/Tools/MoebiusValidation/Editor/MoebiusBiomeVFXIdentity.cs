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
    /// <summary>Updates only existing biome VFX rigs; leaves environment geometry and rendering intact.</summary>
    public static class MoebiusBiomeVFXIdentity
    {
        static string Name,Output,Report;
        static GrasslandWindController grass;
        static WoodlandWindController wood;
        static GameObject root;
        static ParticleSystem[] systems;
        static MeshRenderer[] surfaces;
        struct Triangle {public Vector3 a,b,c;}
        static readonly List<Triangle> Ground=new List<Triangle>();
        static readonly List<string> Notes=new List<string>(),Errors=new List<string>();
        static string ScenePath=>"Assets/Rendering/Moebius/Samples/Biomes/Scenes/"+(Name=="Grassland"?"Grassland_Moebius":Name=="Forest"?"Forest_Moebius":"RainForest")+".unity";
        static void Wind(float time){if(grass)grass.UpdateWind(time);else wood.UpdateWind(time);}
        static void Intensity(float value){if(grass)grass.globalVFXIntensity=value;else wood.globalVFXIntensity=value;}
        public static void RunBatch()
        {
            if(!File.Exists("BiomeVFXIdentity.marker"))throw new Exception("Use the marked isolated fixture.");
            Name=File.ReadAllText("BiomeVFXIdentityTarget.txt").Trim();
            if(Name!="Grassland"&&Name!="Forest"&&Name!="Rainforest")throw new Exception("Unknown biome "+Name);
            Output="Assets/Rendering/Moebius/Content/VFX/"+Name;Report="Validation/BiomeVFXIdentity/"+Name;
            Directory.CreateDirectory(Report);
            Application.logMessageReceived+=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)Errors.Add(message);};
            try
            {
                EditorSceneManager.OpenScene(ScenePath);root=GameObject.Find(Name+"VFX");if(!root)throw new Exception("Missing existing rig "+Name+"VFX");
                grass=root.GetComponent<GrasslandWindController>();wood=root.GetComponent<WoodlandWindController>();
                if(!grass&&!wood)throw new Exception("Missing existing wind controller");
                systems=grass?new[]{grass.floatingMotes,grass.fallingLeaves,grass.windWisps}:
                    new[]{wood.floatingMotes,wood.fallingLeaves,wood.windWisps,wood.lowMist}.Where(ps=>ps).ToArray();
                foreach(var ps in systems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                var original=Object.FindObjectsByType<Component>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                    .Where(c=>!c.transform.IsChildOf(root.transform)).ToDictionary(c=>c.GetInstanceID(),c=>EditorJsonUtility.ToJson(c));
                var camera=Camera.main;var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;ShaderUtil.allowAsyncCompilation=false;
                surfaces=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&
                    (grass?r.name=="Land1":r.name.StartsWith("Fprest_Block"))).OrderBy(r=>r.bounds.center.z).ToArray();
                if(surfaces.Length==0)throw new Exception("No enabled ground surfaces");ReadGround();
                Intensity(0);Wind(0);Capture(camera,Report+"/EnvironmentBefore.png");
                var previewObject=new GameObject("Temporary VFX Comparison Camera");var preview=previewObject.AddComponent<Camera>();preview.CopyFrom(camera);
                previewObject.AddComponent<UniversalAdditionalCameraData>().SetRenderer(1);
                var tile=surfaces.Last();preview.transform.position=tile.bounds.center+new Vector3(8,10,-12);preview.transform.LookAt(tile.bounds.center+Vector3.up*1.4f);
                preview.orthographic=true;preview.orthographicSize=6.8f;
                // Capture the existing look from precisely the same comparison camera.
                if(grass)grass.RefreshSettings();else wood.RefreshSettings();
                Intensity(1);for(int frame=0;frame<360;frame++){Wind(frame/60f);foreach(var ps in systems)ps.Simulate(1f/60,false,frame==0,false);}
                Capture(preview,Report+"/Before.png");
                foreach(var ps in systems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                Directory.CreateDirectory(Output+"/Textures");
                if(grass)ConfigureGrassland();else if(Name=="Forest")ConfigureForest();else ConfigureRainforest();
                if(grass)grass.RefreshSettings();else wood.RefreshSettings();
                // URP can add default light data during Camera.Render; keep this unrelated change out of the saved scene.
                foreach(var data in Object.FindObjectsByType<UniversalAdditionalLightData>(FindObjectsSortMode.None))
                    if(!original.ContainsKey(data.GetInstanceID())&&!data.transform.IsChildOf(root.transform))Object.DestroyImmediate(data);
                foreach(var pair in original)
                {
                    var c=EditorUtility.InstanceIDToObject(pair.Key) as Component;
                    if(!c||EditorJsonUtility.ToJson(c)!=pair.Value)Errors.Add("Environment component changed: "+c);
                }
                string prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if(prefab!=Output+"/Prefabs/"+Name+"VFX.prefab")throw new Exception("Unexpected rig prefab path "+prefab);
                PrefabUtility.SaveAsPrefabAssetAndConnect(root,prefab,InteractionMode.AutomatedAction);
                // The preview camera is temporary and must never enter the saved scene.
                Object.DestroyImmediate(previewObject);
                EditorSceneManager.SaveScene(root.scene,ScenePath);AssetDatabase.SaveAssets();
                previewObject=new GameObject("Temporary VFX Comparison Camera");preview=previewObject.AddComponent<Camera>();preview.CopyFrom(camera);
                previewObject.AddComponent<UniversalAdditionalCameraData>().SetRenderer(1);
                preview.transform.position=tile.bounds.center+new Vector3(8,10,-12);preview.transform.LookAt(tile.bounds.center+Vector3.up*1.4f);
                preview.orthographic=true;preview.orthographicSize=6.8f;
                Validate(camera,preview);
                Object.DestroyImmediate(previewObject);
                Notes.Add("Environment component snapshots unchanged: "+original.Count);
                foreach(var ps in systems)
                {
                    var mat=ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                    if(!mat.GetTexture("_BaseMap"))Errors.Add("Missing texture: "+mat.name);
                    foreach(var message in ShaderUtil.GetShaderMessages(mat.shader))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)Errors.Add(message.message);
                }
            }
            catch(Exception e){Errors.Add(e.ToString());}
            Notes.Add("Errors: "+Errors.Count);Notes.AddRange(Errors);File.WriteAllLines(Report+"/Report.txt",Notes);EditorApplication.Exit(Errors.Count==0?0:1);
        }
        static Material Mat(ParticleSystem ps)=>ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        static void Life(ParticleSystem ps,float min,float max)
        {var main=ps.main;main.startLifetime=new ParticleSystem.MinMaxCurve(min,max);main.gravityModifier=0;}
        static void Vertical(ParticleSystem ps,float min,float max)
        {var v=ps.velocityOverLifetime;v.y=new ParticleSystem.MinMaxCurve(min,max);}
        static void Rotate(ParticleSystem ps,float min,float max)
        {var r=ps.rotationOverLifetime;r.enabled=true;r.z=new ParticleSystem.MinMaxCurve(min,max);}
        static void TintVariation(ParticleSystem ps,Color a,Color b)
        {var m=ps.main;m.startColor=new ParticleSystem.MinMaxGradient(a,b);}
        static void ConfigureGrassland()
        {
            grass.globalVFXIntensity=grass.motesIntensity=grass.leavesIntensity=grass.windWispsIntensity=1;
            grass.baseWindSpeed=1.05f;grass.gustStrength=.8f;grass.gustFrequency=.13f;grass.turbulenceStrength=.2f;
            grass.motesRate=.35f;grass.leavesRate=4.1f;grass.windWispsRate=1.6f;
            grass.motesSizeRange=new Vector2(.055f,.1f);grass.leavesSizeRange=new Vector2(.22f,.4f);grass.windLengthRange=new Vector2(3.2f,4.8f);
            grass.motesColor=new Color(.85f,.86f,.64f,1);grass.leavesColor=new Color(.92f,.8f,.47f,1);grass.windWispsColor=new Color(.97f,.98f,.85f,1);
            var seeds=grass.fallingLeaves;seeds.name="GrassSeeds";SeedEmission(seeds);
            Life(seeds,3.8f,5.6f);Vertical(seeds,-.015f,.055f);Rotate(seeds,-.65f,.65f);
            TintVariation(seeds,new Color(.87f,.85f,.66f,1),Color.white);
            var atlas=NewAtlas("GrassSeedsAtlas",(u,v,id)=>{
                Vector2 p=new Vector2(u,v);float d=Segment(p,new Vector2(.48f,.15f),new Vector2(.53f,.84f))-.009f;
                float body=(new Vector2((u-.5f)/.065f,(v-.47f)/.2f)).magnitude-1;
                d=Mathf.Min(d,body*.065f);
                for(int j=0;j<(id==3?2:4);j++)
                {
                    float y=.32f+j*.1f,side=(j%2==0?-1:1);
                    d=Mathf.Min(d,Segment(p,new Vector2(.5f,y),new Vector2(.5f+side*(.13f+id*.012f),y+.23f))-.006f);
                }
                float a=1-Smooth(-.002f,.004f,d);return new Color(.8f,.8f,.8f,a);
            });
            Mat(seeds).SetTexture("_BaseMap",atlas);Mat(seeds).SetColor("_InkColor",new Color(.36f,.34f,.18f,1));Mat(seeds).SetFloat("_Opacity",.98f);EditorUtility.SetDirty(Mat(seeds));
            Mat(grass.windWisps).SetFloat("_Opacity",.92f);Mat(grass.windWisps).SetFloat("_Thickness",1.25f);EditorUtility.SetDirty(Mat(grass.windWisps));
            Life(grass.windWisps,2.8f,4.2f);var windMain=grass.windWisps.main;windMain.startSizeZ=new ParticleSystem.MinMaxCurve(1.2f,1.6f);windMain.maxParticles=8;var windCollision=grass.windWisps.collision;windCollision.enabled=true;windCollision.radiusScale=.025f;windCollision.lifetimeLoss=1;
            Notes.Add("Grassland: horizontal grass seeds + visible wind sweeps; sparse small pollen.");
        }
        static void ConfigureForest()
        {
            wood.globalVFXIntensity=wood.motesIntensity=wood.leavesIntensity=1;wood.windWispsIntensity=0;
            wood.baseWindSpeed=.24f;wood.gustStrength=.4f;wood.gustFrequency=.075f;wood.turbulenceStrength=.18f;
            wood.motesRate=1.4f;wood.leavesRate=5.2f;wood.windWispsRate=0;
            wood.motesSizeRange=new Vector2(.055f,.105f);wood.leavesSizeRange=new Vector2(.4f,.68f);
            wood.motesColor=new Color(.83f,.8f,.61f,1);wood.leavesColor=new Color(.82f,.68f,.35f,1);
            var leaves=wood.fallingLeaves;leaves.name="FallingBroadLeaves";Life(leaves,5.5f,8);Vertical(leaves,-.64f,-.37f);Rotate(leaves,-1.8f,1.8f);
            TintVariation(leaves,new Color(.65f,.79f,.47f,1),new Color(1,.85f,.56f,1));
            var atlas=NewAtlas("BroadLeavesAtlas",(u,v,id)=>{
                Vector2 p=new Vector2(u,v);float d;
                if(id==2)d=Polygon(p,new[]{new Vector2(.47f,.1f),new Vector2(.26f,.35f),new Vector2(.19f,.56f),new Vector2(.33f,.54f),new Vector2(.28f,.74f),new Vector2(.44f,.68f),new Vector2(.52f,.91f),new Vector2(.63f,.72f),new Vector2(.78f,.75f),new Vector2(.7f,.55f),new Vector2(.82f,.49f),new Vector2(.65f,.34f)});
                else
                {
                    float t=Mathf.Clamp01((v-.13f)/.75f),center=.48f+.06f*Mathf.Sin(t*3+id*.7f);
                    float width=(id==1?.18f:.28f)*Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*Mathf.PI)),.7f);
                    d=Mathf.Max(Mathf.Abs(u-center)-width,Mathf.Max(.13f-v,v-.88f));
                }
                float a=1-Smooth(-.003f,.004f,d);float fill=Smooth(.003f,.018f,-d);
                fill*=.85f+.15f*Smooth(.002f,.009f,Segment(p,new Vector2(.47f,.13f),new Vector2(.53f,.86f)));
                return new Color(fill,fill,fill,a);
            });
            Mat(leaves).SetTexture("_BaseMap",atlas);Mat(leaves).SetColor("_InkColor",new Color(.29f,.31f,.14f,1));Mat(leaves).SetFloat("_Opacity",.98f);EditorUtility.SetDirty(Mat(leaves));
            Notes.Add("Forest: broad olive/ochre leaves rotating down from canopies; no wind-line layer.");
        }
        static void ConfigureRainforest()
        {
            wood.globalVFXIntensity=wood.motesIntensity=wood.leavesIntensity=wood.mistIntensity=1;wood.windWispsIntensity=0;
            wood.baseWindSpeed=.13f;wood.gustStrength=.2f;wood.gustFrequency=.055f;wood.turbulenceStrength=.12f;
            wood.motesRate=.8f;wood.leavesRate=5.5f;wood.windWispsRate=0;wood.mistRate=1.05f;
            wood.motesSizeRange=new Vector2(.055f,.105f);wood.leavesSizeRange=new Vector2(.2f,.34f);wood.mistSizeRange=new Vector2(3.2f,4.8f);
            wood.motesColor=new Color(.67f,.83f,.76f,.8f);wood.leavesColor=new Color(.78f,.94f,.91f,1);wood.mistColor=new Color(.64f,.81f,.75f,1);
            var drips=wood.fallingLeaves;drips.name="CanopyDrips";Life(drips,2.1f,3.2f);Vertical(drips,-1.8f,-1.1f);
            var noise=drips.noise;noise.enabled=false;var rotation=drips.rotationOverLifetime;rotation.enabled=false;
            var main=drips.main;main.startRotation=0;main.startColor=Color.white;
            var collision=drips.collision;collision.lifetimeLoss=1;
            var atlas=NewAtlas("CanopyDripsAtlas",(u,v,id)=>{
                Vector2 p=new Vector2(u,v);float shift=(id-1.5f)*.02f;
                float ellipse=(new Vector2((u-.5f-shift)/.09f,(v-.3f)/.15f)).magnitude-1;
                float d=Mathf.Min(ellipse*.09f,Polygon(p,new[]{new Vector2(.41f+shift,.34f),new Vector2(.5f+shift,.88f),new Vector2(.59f+shift,.34f)}));
                float a=1-Smooth(-.002f,.004f,d);float fill=.8f+.2f*Smooth(.002f,.012f,-d);return new Color(fill,fill,fill,a);
            });
            Mat(drips).SetTexture("_BaseMap",atlas);Mat(drips).SetColor("_InkColor",new Color(.43f,.67f,.63f,1));Mat(drips).SetFloat("_Opacity",.85f);EditorUtility.SetDirty(Mat(drips));
            var mistAtlas=NewAtlas("HumidMistAtlas",(u,v,id)=>{
                float a=0;
                for(int l=0;l<4;l++)
                {
                    float x=.23f+l*.18f,y=.48f+.055f*Mathf.Sin(l*2.4f+id);
                    float dx=(u-x)/(.16f+.025f*Mathf.Sin(id+l)),dy=(v-y)/(.18f+.025f*Mathf.Cos(l+id));
                    a=Mathf.Max(a,Mathf.Exp(-(dx*dx+dy*dy)*1.6f));
                }
                a*=Smooth(.035f,.11f,u)*(1-Smooth(.89f,.965f,u))*Smooth(.08f,.2f,v)*(1-Smooth(.8f,.92f,v));
                return new Color(1,1,1,a);
            });
            var fog=wood.lowMist;Life(fog,8,12);Vertical(fog,-.005f,.012f);var fogMain=fog.main;fogMain.maxParticles=16;
            // A low, camera-facing ribbon reads between trunks without becoming a full-screen fog layer.
            var fogRenderer=fog.GetComponent<ParticleSystemRenderer>();fogRenderer.renderMode=ParticleSystemRenderMode.Billboard;fogRenderer.alignment=ParticleSystemRenderSpace.View;
            fogMain.startRotation3D=false;fogMain.startRotation=0;fogMain.startSizeY=new ParticleSystem.MinMaxCurve(.8f,1.15f);
            Mat(fog).SetTexture("_BaseMap",mistAtlas);Mat(fog).SetFloat("_Opacity",.3f);Mat(fog).SetFloat("_EdgeSoftness",.3f);Mat(fog).SetFloat("_AnimationSpeed",.045f);EditorUtility.SetDirty(Mat(fog));
            Notes.Add("Rainforest: low humid mist + fast thin canopy drips; sparse cool spores, no wind-line layer.");
        }
        static Texture2D NewAtlas(string name,Func<float,float,int,Color> sample)
        {
            Texture(name,512,512,sample);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string path=Output+"/Textures/"+name+".png";var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=false;importer.alphaSource=TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;importer.mipmapEnabled=true;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.maxTextureSize=512;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static void SeedEmission(ParticleSystem ps)
        {
            var vertices=new List<Vector3>();var indices=new List<int>();
            foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.name.Contains("Cone")))
            {
                Vector3 c=r.bounds.center;if(!Height(c.x,c.z,out float y))continue;c.y=Mathf.Max(y+.45f,r.bounds.max.y-.1f);
                int o=vertices.Count;foreach(var delta in new[]{new Vector3(-.25f,0,-.25f),new Vector3(.25f,0,-.25f),new Vector3(-.25f,0,.25f),new Vector3(.25f,0,.25f)})vertices.Add(ps.transform.InverseTransformPoint(c+delta));
                indices.AddRange(new[]{o,o+2,o+1,o+1,o+2,o+3});
            }
            if(indices.Count==0)throw new Exception("No grass-clump seed patches");
            string path=Output+"/Meshes/GrassSeedEmission.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
            mesh.name="GrassSeedEmission";mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();mesh.RecalculateNormals();EditorUtility.SetDirty(mesh);
            var shape=ps.shape;shape.mesh=mesh;shape.position=Vector3.zero;shape.rotation=Vector3.zero;shape.scale=Vector3.one;
            Notes.Add("Grass-clump seed patches: "+vertices.Count/4);
        }
        static void ReadGround()
        {
            Ground.Clear();foreach(var r in surfaces)
            {
                var mesh=r.GetComponent<MeshFilter>().sharedMesh;var v=mesh.vertices;var t=mesh.triangles;
                for(int i=0;i<t.Length;i+=3)
                {
                    Vector3 a=r.transform.TransformPoint(v[t[i]]),b=r.transform.TransformPoint(v[t[i+1]]),c=r.transform.TransformPoint(v[t[i+2]]);
                    if(Vector3.Cross(b-a,c-a).normalized.y>.45f)Ground.Add(new Triangle{a=a,b=b,c=c});
                }
            }
        }
        static void Validate(Camera camera,Camera preview)
        {
            foreach(var ps in systems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);Physics.SyncTransforms();
            var particles=new ParticleSystem.Particle[256];var peaks=new int[systems.Length];int peak=0,under=0,outside=0;
            for(int frame=0;frame<900;frame++)
            {
                Wind(frame/60f);for(int i=0;i<systems.Length;i++)systems[i].Simulate(1f/60,false,frame==0,false);
                peak=Mathf.Max(peak,systems.Sum(ps=>ps.particleCount));
                for(int i=0;i<systems.Length;i++)
                {
                    int n=systems[i].GetParticles(particles);peaks[i]=Mathf.Max(peaks[i],n);
                    for(int j=0;j<n;j++)
                    {
                        Vector3 p=particles[j].position;
                        if(Height(p.x,p.z,out float y)&&p.y<y-.08f){if(under<3)Notes.Add("Below-ground sample "+systems[i].name+" position="+p+" ground="+y);under++;}
                        if(!surfaces.Any(r=>p.x>=r.bounds.min.x-.2f&&p.x<=r.bounds.max.x+.2f&&p.z>=r.bounds.min.z-.2f&&p.z<=r.bounds.max.z+.2f))outside++;
                    }
                }
                if(frame==359||frame==599||frame==899){Capture(preview,Report+"/Frame"+frame+".png");if(frame==599)Capture(camera,Report+"/MainCamera.png");}
            }
            Notes.Add("Layer names: "+string.Join(" / ",systems.Select(ps=>ps.name)));Notes.Add("Layer peak counts: "+string.Join(" / ",peaks));
            Notes.Add("Peak total: "+peak+"; below-ground samples: "+under+"; outside tile samples: "+outside);
            if(under>0||outside>0)Errors.Add("Particle ground/boundary violation");
            int[] required=grass?new[]{1,2}:Name=="Forest"?new[]{1}:new[]{1,3};
            foreach(int i in required)if(peaks[i]==0)Errors.Add("Primary layer never emitted: "+systems[i].name);
            Intensity(0);Wind(16);Capture(camera,Report+"/IntensityZero.png");
            if(!File.ReadAllBytes(Report+"/EnvironmentBefore.png").SequenceEqual(File.ReadAllBytes(Report+"/IntensityZero.png")))Errors.Add("Environment render changed");
            Intensity(1);Wind(16);
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
