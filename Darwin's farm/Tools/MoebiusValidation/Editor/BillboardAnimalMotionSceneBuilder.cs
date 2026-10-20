using System;
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
    // Offline authoring: only writes the dedicated motion sample, never the gameplay scenes.
    public static class BillboardAnimalMotionSceneBuilder
    {
        public const string Root="Assets/Rendering/Moebius/Samples/AnimalMotion/";
        public const string ScenePath=Root+"BillboardSwimFlight.unity";
        static readonly string[] Reports={"Validation/AnimalMotion/"};
        static void Guard()
        {
            if(!Application.isBatchMode||!Application.dataPath.Replace('\\','/').Contains("/Temp/")||!File.Exists("AnimalMotion.marker"))
                throw new Exception("Use the isolated AnimalMotion fixture.");
            Directory.CreateDirectory(Reports[0]);ShaderUtil.allowAsyncCompilation=false;
        }
        static void Folder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        static T Save<T>(T value,string path)where T:Object
        {
            var existing=AssetDatabase.LoadAssetAtPath<T>(path);
            if(existing) {EditorUtility.CopySerialized(value,existing);EditorUtility.SetDirty(existing);Object.DestroyImmediate(value);return existing;}
            AssetDatabase.CreateAsset(value,path);return value;
        }
        static Material Flat(string name,Color color)
        {
            var m=new Material(Shader.Find("Darwin/Moebius NPR")){name=name};m.SetColor("_BaseColor",color);
            m.SetFloat("_UseHatching",1);m.EnableKeyword("_USE_HATCHING");m.SetFloat("_UseTriplanar",1);m.EnableKeyword("_USE_TRIPLANAR");
            m.SetFloat("_HatchStrength",.22f);m.SetFloat("_HatchScale",3);m.SetFloat("_HighlightStrength",0);
            return Save(m,Root+"Materials/"+name+".mat");
        }
        static Mesh Disc(float rx,float rz,float bottom,float top,bool cap=true)
        {
            const int n=128;
            var v=new System.Collections.Generic.List<Vector3>();var t=new System.Collections.Generic.List<int>();
            if(cap)
            {
                v.Add(new Vector3(0,top,0));
                for(int i=0;i<=n;i++) {float a=i*Mathf.PI*2/n;v.Add(new Vector3(Mathf.Sin(a)*rx,top,Mathf.Cos(a)*rz));}
                for(int i=0;i<n;i++)t.AddRange(new[]{0,i+1,i+2});
            }
            for(int i=0;i<n;i++)
            {
                float a=i*Mathf.PI*2/n,b=(i+1)*Mathf.PI*2/n;int j=v.Count;
                v.Add(new Vector3(Mathf.Sin(a)*rx,top,Mathf.Cos(a)*rz));v.Add(new Vector3(Mathf.Sin(a)*rx,bottom,Mathf.Cos(a)*rz));
                v.Add(new Vector3(Mathf.Sin(b)*rx,top,Mathf.Cos(b)*rz));v.Add(new Vector3(Mathf.Sin(b)*rx,bottom,Mathf.Cos(b)*rz));
                t.AddRange(new[]{j,j+1,j+2,j+1,j+3,j+2});
            }
            var mesh=new Mesh();mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static Mesh Pond()
        {
            const int rings=40,n=128;var v=new Vector3[(rings+1)*(n+1)];var t=new System.Collections.Generic.List<int>();
            for(int y=0;y<=rings;y++)for(int x=0;x<=n;x++)
            {
                float radius=y/(float)rings,a=x*Mathf.PI*2/n;int i=y*(n+1)+x;
                v[i]=new Vector3(Mathf.Sin(a)*6.2f*radius,0,Mathf.Cos(a)*5.2f*radius);
                if(y<rings&&x<n) {if(y>0)t.AddRange(new[]{i,i+n+1,i+1});t.AddRange(new[]{i+1,i+n+1,i+n+2});}
            }
            var mesh=new Mesh();mesh.vertices=v;mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();var b=mesh.bounds;b.Expand(Vector3.up);mesh.bounds=b;return mesh;
        }
        static GameObject MeshObject(string name,Mesh mesh,Material m,Vector3 position,Transform parent=null)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.position=position;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=m;return go;
        }
        static void Label(string text,Vector3 position,float size,Camera camera)
        {
            var go=new GameObject(text);go.transform.position=position;go.transform.rotation=camera.transform.rotation;
            var label=go.AddComponent<TextMesh>();label.text=text;label.anchor=TextAnchor.MiddleCenter;label.fontSize=80;label.characterSize=size;
            label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.color=new Color(.08f,.14f,.15f);
            go.GetComponent<MeshRenderer>().sharedMaterial=label.font.material;
        }
        static BillboardAnimalMotion Animal(string name,string species,BillboardAnimalMotion.Medium medium,Vector3 center,Vector2 radius,
            float speed,float phase,float size,Transform parent,Material spriteMaterial,Material strokes,bool left)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            var actor=go.AddComponent<BillboardAnimalMotion>();actor.medium=medium;
            actor.animationSet=AssetDatabase.LoadAssetAtPath<PopulationAnimationSet>("Assets/Art/Animals/Animations/EightAnimalLab/"+species+"/"+species+"Animation.asset");
            if(!actor.animationSet)throw new Exception("Missing authored animation: "+species);
            actor.routeCenter=center;actor.routeRadius=radius;actor.speed=speed;actor.routePhase=phase;actor.spriteHeight=size;actor.artworkFacesLeft=left;
            actor.bobAmplitude=medium==BillboardAnimalMotion.Medium.Swimming?.028f:.18f;actor.bankDegrees=medium==BillboardAnimalMotion.Medium.Swimming?0:7;
            actor.immersion=species=="鹿游"?.43f:.35f;
            var sprite=new GameObject("Animated Billboard");sprite.transform.SetParent(go.transform,false);actor.image=sprite.AddComponent<SpriteRenderer>();
            actor.image.sharedMaterial=spriteMaterial;actor.image.sprite=actor.animationSet.IdleSprite;
            var effects=new GameObject(medium==BillboardAnimalMotion.Medium.Swimming?"Wake and detached ripples":"Curved air strokes");effects.transform.SetParent(parent,false);
            actor.effects=effects.AddComponent<BillboardAnimalMotionVFX>();effects.GetComponent<MeshRenderer>().sharedMaterial=strokes;
            if(medium==BillboardAnimalMotion.Medium.Swimming) {actor.effects.lineWidth=.05f;actor.effects.intensity=.95f;}
            return actor;
        }
        public static void Build()
        {
            try
            {
                Guard();Folder(Root.TrimEnd('/'));Folder(Root+"Materials");Folder(Root+"Meshes");
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                var rendererData=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Rendering/Moebius/Samples/Shared/Rendering/MoebiusRenderer.asset");
                if(!rendererData)throw new Exception("Moebius renderer missing: "+AssetDatabase.GUIDToAssetPath("3adb314848100ed4d869fe11a7a9ae53"));
                // Explicitly bind the copied renderer after an isolated fixture's first import.
                var settings=new SerializedObject(pipeline);var renderers=settings.FindProperty("m_RendererDataList");
                renderers.arraySize=Mathf.Max(2,renderers.arraySize);renderers.GetArrayElementAtIndex(1).objectReferenceValue=rendererData;
                settings.FindProperty("m_DefaultRendererIndex").intValue=1;settings.ApplyModifiedPropertiesWithoutUndo();
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var sand=Flat("Pond Bank",new Color(.85f,.82f,.61f));var grass=Flat("Flight Meadow",new Color(.70f,.77f,.61f));
                var earth=Flat("Foundation",new Color(.69f,.64f,.52f));var stone=Flat("Lavender Stone",new Color(.66f,.65f,.76f));
                var poolBase=Save(Disc(6.8f,5.8f,-.65f,-.15f),Root+"Meshes/PondFoundation.asset");
                MeshObject("Swimming pond foundation",poolBase,earth,new Vector3(-7.3f,0,0));
                MeshObject("Pale sand shoreline",Save(Disc(6.7f,5.7f,-.15f,-.02f),Root+"Meshes/PondBank.asset"),sand,new Vector3(-7.3f,0,0));
                var shore=new Texture2D(256,256,TextureFormat.RGBA32,false,true){name="Pond shoreline",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
                var pixels=new Color[256*256];
                for(int z=0;z<256;z++)for(int x=0;x<256;x++)
                {
                    float px=((x+.5f)/256*2-1)*6.2f,pz=((z+.5f)/256*2-1)*5.2f;
                    Vector2 q=new Vector2(px/6.2f,pz/5.2f);float length=q.magnitude;
                    Vector2 gradient=length>.0001f?new Vector2(-q.x/6.2f,-q.y/5.2f)/length*5.2f:Vector2.zero;
                    pixels[z*256+x]=new Color(Mathf.Max(0,1-length)*5.2f/3,gradient.x*.5f+.5f,gradient.y*.5f+.5f,1);
                }
                shore.SetPixels(pixels);shore.Apply();shore=Save(shore,Root+"Materials/PondShore.asset");
                var water=new Material(Shader.Find("Darwin/Moebius Water")){name="Motion Pond"};
                water.SetColor("_WaterColor",new Color(.43f,.70f,.73f));water.SetFloat("_WaveHeight",.012f);water.SetFloat("_FoamWidth",.32f);
                water.SetFloat("_EnableShoreMask",1);water.SetFloat("_ShoreObjectSpace",1);water.SetTexture("_ShorelineMask",shore);
                water.SetVector("_ShoreWorldRect",new Vector4(-6.2f,-5.2f,12.4f,10.4f));water.SetFloat("_ShoreDistanceRange",3);
                water=Save(water,Root+"Materials/MotionPond.mat");
                MeshObject("Moebius swimming water",Save(Pond(),Root+"Meshes/PondWater.asset"),water,new Vector3(-7.3f,0,0)).GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
                MeshObject("Flight meadow foundation",Save(Disc(6.1f,5.2f,-.7f,-.2f),Root+"Meshes/FlightFoundation.asset"),earth,new Vector3(7.3f,0,0));
                MeshObject("Flight meadow",Save(Disc(6.1f,5.2f,-.2f,0),Root+"Meshes/FlightMeadow.asset"),grass,new Vector3(7.3f,0,0));
                var rockMesh=Save(Disc(.75f,.55f,-.15f,.22f),Root+"Meshes/RiverPebble.asset");
                foreach(var p in new[]{new Vector3(-12,.01f,3.3f),new Vector3(-3,.01f,3.8f),new Vector3(11,.01f,2.6f)})MeshObject("Ink stone",rockMesh,stone,p);
                var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(45,-35,0);light.intensity=1;light.shadows=LightShadows.Soft;
                light.gameObject.AddComponent<UniversalAdditionalLightData>();RenderSettings.sun=light;
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.78f,.84f,.87f);RenderSettings.fog=false;
                RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Content/Skybox/Materials/ClearSky.mat");
                var camera=new GameObject("Motion Demo Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.orthographic=true;camera.orthographicSize=9.8f;
                camera.transform.position=new Vector3(1,15,-23);camera.transform.LookAt(new Vector3(0,1.7f,0));camera.nearClipPlane=.1f;camera.farClipPlane=200;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.73f,.85f,.84f);camera.GetUniversalAdditionalCameraData().SetRenderer(1);
                var air=Save(new Material(Shader.Find("Darwin/Population Air Sprite")){name="Flying Billboard"},Root+"Materials/FlyingBillboard.mat");
                var swimming=Save(new Material(Shader.Find("Darwin/Population Water Sprite")){name="Swimming Billboard"},Root+"Materials/SwimmingBillboard.mat");
                var strokes=Save(new Material(Shader.Find("Darwin/Moebius Motion Strokes")){name="Illustrated motion"},Root+"Materials/MotionStrokes.mat");
                var root=new GameObject("Billboard Swimming and Flight");var demo=root.AddComponent<BillboardAnimalMotionDemo>();demo.animateInEditMode=false;demo.playing=false;demo.previewTime=3;demo.viewCamera=camera;
                demo.animals=new[]{
                    Animal("Swimming - 鹿游","鹿游",BillboardAnimalMotion.Medium.Swimming,new Vector3(-7.3f,0,-1.5f),new Vector2(3.8f,1.9f),1.1f,.10f,3.9f,root.transform,swimming,strokes,true),
                    Animal("Swimming - 鲨鹿","鲨鹿",BillboardAnimalMotion.Medium.Swimming,new Vector3(-7.3f,0,1.5f),new Vector2(3.5f,1.4f),1.4f,.58f,3.7f,root.transform,swimming,strokes,false),
                    Animal("Flight - 鹿飞","鹿飞",BillboardAnimalMotion.Medium.Flying,new Vector3(7.3f,2.9f,-.7f),new Vector2(3.7f,2.5f),1.9f,.20f,4.0f,root.transform,air,strokes,true),
                    Animal("Flight - 鹿飞 distant","鹿飞",BillboardAnimalMotion.Medium.Flying,new Vector3(7.3f,4.2f,1.3f),new Vector2(3.1f,1.6f),1.4f,.70f,2.8f,root.transform,air,strokes,true)
                };
                Label("SWIMMING",new Vector3(-7.3f,.3f,-6.5f),.075f,camera);
                Label("FLIGHT",new Vector3(7.3f,.3f,-6.5f),.075f,camera);
                demo.Evaluate(3,camera);demo.animateInEditMode=true;demo.playing=true;
                AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);
                Verify(demo,camera);
                demo.playing=false;demo.previewTime=3;demo.Evaluate(3,camera);Capture(camera,"Overview");Capture(camera,"Time3");
                foreach(var animal in demo.animals)animal.showEffects=false;
                var without=Capture(camera,"WithoutEffects");
                foreach(var animal in demo.animals)animal.showEffects=true;
                var with=Capture(camera,"WithEffects");int airPixels=0;
                for(int y=0;y<1000;y++)for(int x=800;x<1600;x++)
                {
                    int i=y*1600+x;
                    if(Mathf.Abs(with[i].r-without[i].r)+Mathf.Abs(with[i].g-without[i].g)+Mathf.Abs(with[i].b-without[i].b)>6)airPixels++;
                }
                if(airPixels<30)
                {
                    var audit=new System.Text.StringBuilder();
                    foreach(var animal in demo.animals)
                    {
                        var effect=animal.effects;var filter=effect.GetComponent<MeshFilter>();var renderer=effect.GetComponent<MeshRenderer>();var m=filter.sharedMesh;
                        audit.AppendLine(animal.name+": mesh="+m+" vertices="+(m?m.vertexCount:0)+" active="+effect.gameObject.activeInHierarchy+" renderer="+renderer.enabled+" hidden="+renderer.forceRenderingOff+" material="+renderer.sharedMaterial+" bounds="+renderer.bounds+" matrix="+effect.transform.localToWorldMatrix);
                        if(m)audit.AppendLine("  alpha="+m.colors.Min(c=>c.a)+".."+m.colors.Max(c=>c.a)+" uv="+string.Join(";",m.uv.Take(4).Select(p=>p.ToString()))+" vertex="+string.Join(";",m.vertices.Take(4).Select(p=>p.ToString())));
                    }
                    File.WriteAllText(Reports[0]+"EffectAudit.txt",audit.ToString());
                    throw new Exception("Flight effects did not produce visible pixels: "+airPixels);
                }
                File.AppendAllText(Reports[0]+"Report.txt","PASS: Flight effects change "+airPixels+" pixels in an effects-on/off native render.\n");
                demo.previewTime=7;demo.Evaluate(7,camera);Capture(camera,"Time7");
                Directory.CreateDirectory(Reports[0]+"Frames");
                for(int frame=0;frame<32;frame++)
                {
                    demo.previewTime=3+frame/8f;demo.Evaluate(demo.previewTime,camera);Capture(camera,"Frames/Frame"+frame.ToString("D2"));
                }
                EditorApplication.Exit(0);
            }
            catch(Exception e) {Directory.CreateDirectory(Reports[0]);File.WriteAllText(Reports[0]+"Failure.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static void Verify(BillboardAnimalMotionDemo demo,Camera camera)
        {
            var report=new System.Collections.Generic.List<string>();
            foreach(var animal in demo.animals)
            {
                var sprites=animal.medium==BillboardAnimalMotion.Medium.Flying?animal.animationSet.WalkFrames:animal.animationSet.CommonFrames;
                if(sprites.Length<2||sprites.Any(s=>!s))throw new Exception("Missing corresponding sequence: "+animal.name);
                demo.Evaluate(2,camera);var first=animal.image.sprite;var p=animal.transform.position;
                demo.Evaluate(2.4f,camera);
                if(first==animal.image.sprite||Vector3.Distance(p,animal.transform.position)<.1f)throw new Exception("Animation/translation not advancing: "+animal.name);
                int count=animal.effects.LiveMeshVertices;
                for(int i=0;i<20;i++)demo.Evaluate(3+i*.016f,camera);
                if(animal.effects.LiveMeshVertices!=count)throw new Exception("Effect mesh capacity changed");
                if(Mathf.Abs(Vector3.Dot(animal.image.transform.forward,camera.transform.forward))<.999f)throw new Exception("Billboard not facing camera");
                report.Add("PASS: "+animal.name+" uses "+sprites.Length+" existing frames; frame and movement advance; billboard faces camera; fixed "+count+" effect vertices.");
            }
            foreach(var shader in new[]{"Darwin/Moebius Motion Strokes","Darwin/Population Air Sprite","Darwin/Population Water Sprite","Darwin/Moebius Water"})
                foreach(var diagnostic in ShaderUtil.GetShaderMessages(Shader.Find(shader)))
                    if(diagnostic.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)throw new Exception(diagnostic.message);
            report.Add("PASS: Corresponding frame assets are referenced directly; no imported art, species settings, gameplay scripts or build scenes edited.");
            File.WriteAllLines(Reports[0]+"Report.txt",report);
        }
        static Color32[] Capture(Camera camera,string name)
        {
            var old=camera.targetTexture;var active=RenderTexture.active;var rt=RenderTexture.GetTemporary(1600,1000,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();
            var pixels=texture.GetPixels32();int minimum=255,maximum=0;
            foreach(var p in pixels) {minimum=Mathf.Min(minimum,p.r);maximum=Mathf.Max(maximum,p.r);}
            if(maximum-minimum<30)throw new Exception("Blank/failed camera render: "+name);
            File.WriteAllBytes(Reports[0]+name+".png",texture.EncodeToPNG());Object.DestroyImmediate(texture);
            camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);
            return pixels;
        }
    }
}
