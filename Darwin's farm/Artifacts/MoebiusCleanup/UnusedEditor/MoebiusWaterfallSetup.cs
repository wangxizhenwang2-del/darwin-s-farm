using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Darwin.Rendering.Editor
{
    /// <summary>Creates reusable illustrated meshes and a safe sample. No runtime animator is required.</summary>
    public static class MoebiusWaterfallSetup
    {
        public const string Root="Assets/Rendering/Moebius/Samples/Waterfall";
        public const string PrefabPath=Root+"/MoebiusWaterfall.prefab";
        public const string ScenePath=Root+"/MoebiusWaterfallDemo.unity";
        public const float SampleLipRadius=.75f;

        [MenuItem("Tools/Moebius/Waterfall/Create Waterfall in Scene")]
        public static void CreateInScene()
        {
            var prefab=EnsurePrefab();
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance,"Create Moebius waterfall");
            Selection.activeGameObject=instance;
        }

        [MenuItem("Tools/Moebius/Waterfall/Open Demo Scene")]
        static void OpenDemo()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            if(!File.Exists(ScenePath))CreateDemoScene();else EditorSceneManager.OpenScene(ScenePath);
        }

        public static GameObject EnsurePrefab()
        {
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);if(existing)return existing;
            foreach(string path in new[]{Root,Root+"/Materials",Root+"/Meshes"})Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
            var sheet=SaveMesh("WaterfallSheet",CreateSheet(5.8f,6,64,96,SampleLipRadius));
            var impact=SaveMesh("ImpactPlane",CreatePlane(2,2,4));
            var body=MaterialAsset("WaterfallBody","Darwin/Moebius Waterfall");
            ConfigureCurvedLip(body,5.8f,6,SampleLipRadius);
            var contact=MaterialAsset("ImpactFoam","Darwin/Moebius Waterfall Impact");
            var downstream=MaterialAsset("DownstreamRipples","Darwin/Moebius Waterfall Impact");
            downstream.SetFloat("_FoamWidth",.08f);downstream.SetFloat("_FoamEdgeInkStrength",.22f);downstream.SetFloat("_RippleDensity",6);
            downstream.SetFloat("_FoamNoiseStrength",.08f);downstream.SetFloat("_AnimationPhase",1.3f);
            var root=new GameObject("Moebius Illustrated Waterfall");
            MeshObject("Waterfall Sheet",root.transform,sheet,body,new Vector3(0,3.2f,0),Vector3.one);
            MeshObject("Impact Foam",root.transform,impact,contact,new Vector3(0,.15f,-.45f),new Vector3(3.65f,1,2.6f)).gameObject.SetActive(false);
            MeshObject("Downstream Outflow",root.transform,impact,downstream,new Vector3(.1f,.12f,-3.4f),new Vector3(3.1f,1,3.6f)).gameObject.SetActive(false);
            ConfigureSplashLayers(root);
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);UnityEngine.Object.DestroyImmediate(root);AssetDatabase.SaveAssets();return prefab;
        }

        // Authoring only: refresh the four existing sample meshes/materials without replacing the sheet.
        public static void RebuildSampleSplash()
        {
            EnsurePrefab();
            var root=PrefabUtility.LoadPrefabContents(PrefabPath);
            try{ConfigureSplashLayers(root);PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);}
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
        }

        // Update only the lip assets, preserving scene/prefab transforms and all splash settings.
        public static void RebuildSampleLip()
        {
            EnsurePrefab();
            SaveMesh("WaterfallSheet",CreateSheet(5.8f,6,64,96,SampleLipRadius));
            ConfigureCurvedLip(MaterialAsset("WaterfallBody","Darwin/Moebius Waterfall"),5.8f,6,SampleLipRadius);
            SaveMesh("UpperWaterGrid",CreateUpperWater(10.8f,4.8f,5.8f,SampleLipRadius-.15f,32));
            var upper=MaterialAsset("UpperWater","Darwin/Moebius Water");
            // The flat inlet meets the pinned lip exactly; shader flow/ripples still animate.
            upper.SetFloat("_WaveHeight",0);upper.SetVector("_FlowDirection",new Vector4(0,-1,0,0));EditorUtility.SetDirty(upper);
            AssetDatabase.SaveAssets();
        }

        static void ConfigureCurvedLip(Material body,float width,float height,float radius)
        {
            // Initialize newly added property defaults/drawers before assigning authoring values.
            MaterialEditor.ApplyMaterialPropertyDrawers(body);
            var upper=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/UpperWater.mat");
            var upstream=upper?upper.GetColor("_WaterColor"):new Color(.49f,.72f,.74f);
            float length=height-radius+radius*Mathf.PI*.5f;
            body.SetVector("_SheetSize",new Vector4(width,height,0,0));
            body.SetFloat("_SurfaceLength",length);
            body.SetFloat("_LipStartV",(height-radius)/length);
            body.SetColor("_UpstreamColor",upstream);
            EditorUtility.SetDirty(body);
        }

        static void ConfigureSplashLayers(GameObject root)
        {
            // Two swept upward accents, one compressed hero mass, one horizontal surface fan.
            var positions=new[]{new Vector3(-1.8f,.08f,-.08f),new Vector3(1.35f,.08f,-.16f),new Vector3(.05f,.07f,-.60f),new Vector3(-.15f,.035f,-1.35f)};
            var scales=new[]{new Vector3(1.25f,1.2f,.75f),new Vector3(1.1f,1.05f,.65f),new Vector3(3.05f,1.05f,1.3f),new Vector3(3.5f,.26f,1.55f)};
            var fills=new[]{new Color(.73f,.88f,.89f),new Color(.80f,.92f,.91f),new Color(.97f,.98f,.95f),new Color(.88f,.96f,.94f)};
            var bases=new[]{new Color(.55f,.76f,.82f),new Color(.61f,.81f,.84f),new Color(.74f,.89f,.90f),new Color(.67f,.87f,.89f)};
            for(int i=0;i<4;i++)
            {
                // Preserve asset GUIDs and child names while replacing the old circle-union silhouettes.
                var cloud=SaveMesh("SplashCloud"+i,CreateSplashShape(i));
                var mat=MaterialAsset("SplashLayer"+i,"Darwin/Moebius Waterfall Splash");
                mat.SetColor("_SplashColorA",fills[i]);mat.SetColor("_SplashColorB",bases[i]);
                mat.SetFloat("_AnimationPhase",i*1.7f);mat.SetFloat("_LayerBlendStrength",i==3?.22f:.28f);
                mat.SetFloat("_SplashMotionRole",i<2?0:i==2?1:2);
                mat.SetFloat("_PulseSpeed",i<2?.85f:i==2?1.3f:1.1f);
                mat.SetFloat("_PulseAmplitude",i<2?.018f:i==2?.045f:.055f);
                mat.SetFloat("_WobbleStrength",i<2?.022f:i==2?.09f:.055f);
                mat.SetFloat("_WobbleSpeed",i<2?.8f:1.15f);
                mat.SetFloat("_SplashInkStrength",i==3?.2f:.4f);
                mat.SetFloat("_InternalPatternStrength",0);EditorUtility.SetDirty(mat);
                string name="Splash Cloud Layer "+(i+1);var child=root.transform.Find(name);
                if(!child)MeshObject(name,root.transform,cloud,mat,positions[i],scales[i]);
                else
                {
                    child.localPosition=positions[i];child.localScale=scales[i];child.localRotation=Quaternion.identity;
                    child.GetComponent<MeshFilter>().sharedMesh=cloud;child.GetComponent<MeshRenderer>().sharedMaterial=mat;
                }
            }
        }

        public static void CreateDemoScene()
        {
            var prefab=EnsurePrefab();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab);instance.transform.position=new Vector3(0,0,1.7f);
            var lower=MaterialAsset("LowerWater","Darwin/Moebius Water");
            lower.SetFloat("_WaveHeight",.025f);lower.SetFloat("_EnableFoam",0);lower.SetFloat("_EnableShoreMask",0);
            var waterMesh=SaveMesh("LowerWaterGrid",CreatePlane(21,20,64));
            var receiving=MeshObject("Lower Moebius Water",null,waterMesh,lower,new Vector3(0,0,-2),Vector3.one);
            lower.SetVector("_FlowDirection",new Vector4(0,-1,0,0));
            instance.transform.Find("Impact Foam").gameObject.SetActive(false);
            instance.transform.Find("Downstream Outflow").gameObject.SetActive(false);
            MoebiusWaterfallImpactSetup.Bind(instance.transform.Find("Waterfall Sheet").GetComponent<MeshRenderer>(),receiving);
            lower.SetFloat("_ImpactPhase",5);
            var upper=MaterialAsset("UpperWater","Darwin/Moebius Water");
            upper.SetFloat("_WaveHeight",0);upper.SetFloat("_EnableFoam",0);upper.SetFloat("_EnableShoreMask",0);
            upper.SetVector("_FlowDirection",new Vector4(0,-1,0,0));
            var upperMesh=SaveMesh("UpperWaterGrid",CreateUpperWater(10.8f,4.8f,5.8f,SampleLipRadius-.15f,32));
            MeshObject("Upper Moebius Water",null,upperMesh,upper,new Vector3(0,6.2f,4.25f),Vector3.one);
            var cliff=RockMaterial("PeachCliff",new Color(.82f,.65f,.53f));
            var lavender=RockMaterial("LavenderRocks",new Color(.69f,.72f,.82f));
            Cube("Cliff Platform",new Vector3(0,3.02f,4.5f),new Vector3(11.6f,6,4.8f),cliff);
            Cube("Left Cliff Shoulder",new Vector3(-5.4f,3.2f,2),new Vector3(2,6.4f,3.5f),cliff);
            Cube("Right Cliff Shoulder",new Vector3(5.3f,2.5f,2.4f),new Vector3(2,5,3.6f),cliff);
            Rock("Left River Rock",new Vector3(-5.5f,.55f,-1),new Vector3(2.9f,1.2f,2),lavender);
            Rock("Right River Rock",new Vector3(5.2f,.45f,-3),new Vector3(2.5f,1,1.8f),lavender);
            var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;
            light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(50,-30,0);
            var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";
            camera.transform.position=new Vector3(11,8.5f,-16);camera.transform.LookAt(new Vector3(0,2.5f,1));
            camera.orthographic=true;camera.orthographicSize=8.5f;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.48f,.76f,.79f);
            camera.GetUniversalAdditionalCameraData().SetRenderer(1);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);
            AssetDatabase.SaveAssets();Selection.activeGameObject=instance;
        }

        static Material MaterialAsset(string name,string shaderName)
        {
            string path=Root+"/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(material)return material;
            var shader=Shader.Find(shaderName);if(!shader)throw new InvalidOperationException("Missing shader: "+shaderName);
            material=new Material(shader){name=name};AssetDatabase.CreateAsset(material,path);return material;
        }
        static Material RockMaterial(string name,Color color)
        {
            var mat=MaterialAsset(name,"Darwin/Moebius NPR");mat.SetColor("_BaseColor",color);
            mat.SetFloat("_UseHatching",1);mat.EnableKeyword("_USE_HATCHING");mat.SetFloat("_HatchStrength",.5f);
            mat.SetFloat("_UseTriplanar",1);mat.EnableKeyword("_USE_TRIPLANAR");mat.SetFloat("_HatchScale",12);
            mat.SetFloat("_HighlightStrength",0);return mat;
        }
        static Mesh SaveMesh(string name,Mesh mesh)
        {
            string path=Root+"/Meshes/"+name+".asset";mesh.name=name;
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;}
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static MeshRenderer MeshObject(string name,Transform parent,Mesh mesh,Material material,Vector3 position,Vector3 scale)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;return renderer;
        }
        static void Cube(string name,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.position=position;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=material;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        static void Rock(string name,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=name;go.transform.position=position;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=material;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        static Mesh CreateSheet(float width,float height,int columns,int rows,float lipRadius)
        {
            float radius=Mathf.Clamp(lipRadius,.01f,height*.4f);
            float straight=height-radius,arc=radius*Mathf.PI*.5f,length=straight+arc;
            var mesh=Grid(columns,rows,(u,v)=>
            {
                float distance=v*length;
                if(distance<=straight)return new Vector3((u-.5f)*width,distance-height*.5f,0);
                float angle=(distance-straight)/radius;
                return new Vector3((u-.5f)*width,height*.5f-radius+radius*Mathf.Sin(angle),radius*(1-Mathf.Cos(angle)));
            });
            // Exact endpoint tangents avoid a normal-buffer outline at either end of the arc.
            var normals=mesh.normals;
            for(int y=0;y<=rows;y++)
            {
                float angle=Mathf.Clamp(((float)y/rows*length-straight)/radius,0,Mathf.PI*.5f);
                var normal=new Vector3(0,Mathf.Sin(angle),-Mathf.Cos(angle));
                for(int x=0;x<=columns;x++)normals[y*(columns+1)+x]=normal;
            }
            mesh.normals=normals;
            return mesh;
        }

        static Mesh CreateUpperWater(float width,float depth,float openingWidth,float setback,int segments)
        {
            // Three strips leave a rectangular opening for the curved lip: no overlapping faces.
            var pieces=new CombineInstance[3];
            float[] edges={-width*.5f,-openingWidth*.5f,openingWidth*.5f,width*.5f};
            for(int i=0;i<3;i++)
            {
                float front=-depth*.5f+(i==1?setback:0);
                int index=i;
                var strip=Grid(segments,segments,(u,v)=>new Vector3(Mathf.Lerp(edges[index],edges[index+1],u),0,Mathf.Lerp(front,depth*.5f,v)));
                // Grid's XZ winding and upward normals match the horizontal lip endpoint.
                pieces[i]=new CombineInstance{mesh=strip,transform=Matrix4x4.identity};
            }
            var mesh=new Mesh();mesh.CombineMeshes(pieces,true,false);mesh.RecalculateBounds();
            var bounds=mesh.bounds;bounds.Expand(1);mesh.bounds=bounds;
            foreach(var piece in pieces)UnityEngine.Object.DestroyImmediate(piece.mesh);
            return mesh;
        }
        static Mesh CreatePlane(float width,float depth,int segments)
        {
            return Grid(segments,segments,(u,v)=>new Vector3((u-.5f)*width,0,(v-.5f)*depth));
        }
        static Mesh Grid(int columns,int rows,Func<float,float,Vector3> point)
        {
            var vertices=new Vector3[(columns+1)*(rows+1)];var uv=new Vector2[vertices.Length];var triangles=new int[columns*rows*6];
            for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++){int i=y*(columns+1)+x;uv[i]=new Vector2((float)x/columns,(float)y/rows);vertices[i]=point(uv[i].x,uv[i].y);}
            int k=0;for(int y=0;y<rows;y++)for(int x=0;x<columns;x++){int a=y*(columns+1)+x,b=a+columns+1;triangles[k++]=a;triangles[k++]=b;triangles[k++]=a+1;triangles[k++]=a+1;triangles[k++]=b;triangles[k++]=b+1;}
            var mesh=new Mesh{vertices=vertices,uv=uv,triangles=triangles};mesh.RecalculateNormals();mesh.RecalculateBounds();
            var bounds=mesh.bounds;bounds.Expand(1);mesh.bounds=bounds;return mesh;
        }
        // Authored asymmetric contours, not rounded particles or randomly placed cloud lobes.
        static Mesh CreateSplashShape(int role)
        {
            Vector2[] profile;
            if(role==0)
                profile=new[]{new Vector2(-1.00f,-.06f),new Vector2(-.89f,.19f),new Vector2(-.70f,.40f),new Vector2(-.80f,.69f),new Vector2(-1.00f,.93f),new Vector2(-.81f,1.13f),new Vector2(-.54f,1.17f),new Vector2(-.28f,1.02f),new Vector2(-.24f,.81f),new Vector2(-.37f,.66f),new Vector2(-.05f,.61f),new Vector2(.22f,.42f),new Vector2(.29f,.23f),new Vector2(.59f,.16f),new Vector2(.86f,.01f),new Vector2(.53f,-.05f),new Vector2(-.2f,-.08f)};
            else if(role==1)
                profile=new[]{new Vector2(-.96f,-.05f),new Vector2(-.72f,.18f),new Vector2(-.49f,.27f),new Vector2(-.32f,.49f),new Vector2(-.05f,.56f),new Vector2(.14f,.83f),new Vector2(.45f,1.00f),new Vector2(.79f,1.04f),new Vector2(.98f,.89f),new Vector2(.72f,.77f),new Vector2(.64f,.57f),new Vector2(.84f,.35f),new Vector2(.98f,.27f),new Vector2(1.10f,.10f),new Vector2(1.05f,.01f),new Vector2(.55f,-.07f),new Vector2(-.1f,-.08f)};
            else if(role==2)
                profile=new[]{new Vector2(-1.13f,-.04f),new Vector2(-1.10f,.09f),new Vector2(-.94f,.19f),new Vector2(-.80f,.32f),new Vector2(-.58f,.33f),new Vector2(-.47f,.53f),new Vector2(-.32f,.58f),new Vector2(-.17f,.48f),new Vector2(-.01f,.63f),new Vector2(.21f,.73f),new Vector2(.40f,.64f),new Vector2(.45f,.43f),new Vector2(.67f,.47f),new Vector2(.87f,.33f),new Vector2(.89f,.20f),new Vector2(1.09f,.15f),new Vector2(1.20f,.03f),new Vector2(.94f,-.05f),new Vector2(.42f,-.09f),new Vector2(-.18f,-.07f),new Vector2(-.70f,-.08f)};
            else
                // Horizontal XZ contour: stretched downstream tips with broad flatter connecting edges.
                profile=new[]{new Vector2(-1.06f,.15f),new Vector2(-.93f,.50f),new Vector2(-.55f,.59f),new Vector2(-.09f,.49f),new Vector2(.33f,.60f),new Vector2(.75f,.44f),new Vector2(1.00f,.20f),new Vector2(1.14f,-.11f),new Vector2(.87f,-.18f),new Vector2(.79f,-.55f),new Vector2(.52f,-.72f),new Vector2(.36f,-.64f),new Vector2(.04f,-.97f),new Vector2(-.12f,-.87f),new Vector2(-.53f,-.96f),new Vector2(-.67f,-.70f),new Vector2(-1.03f,-.67f),new Vector2(-.88f,-.39f),new Vector2(-1.18f,-.14f)};
            const int rings=16;
            int segments=profile.Length*6;
            bool surface=role==3;
            var center=surface?new Vector2(0,-.12f):new Vector2(0,role<2?.38f:.24f);
            var mesh=Grid(segments,rings,(u,v)=>
            {
                float index=u*profile.Length;
                int n=Mathf.FloorToInt(index)%profile.Length;
                float t=index-Mathf.Floor(index);
                var p0=profile[(n+profile.Length-1)%profile.Length];var p1=profile[n];
                var p2=profile[(n+1)%profile.Length];var p3=profile[(n+2)%profile.Length];
                // Mild Hermite tangents preserve broad flat stretches and directional tips.
                var m1=(p2-p0)*.35f;var m2=(p3-p1)*.35f;
                float t2=t*t,t3=t2*t;
                var outline=(2*t3-3*t2+1)*p1+(t3-2*t2+t)*m1+(-2*t3+3*t2)*p2+(t3-t2)*m2;
                float latitude=(v-.5f)*Mathf.PI;
                float c=Mathf.Cos(latitude),s=Mathf.Sin(latitude);
                var p=center+(outline-center)*c;
                // Closed shallow volume remains readable at oblique camera angles.
                if(surface)return new Vector3(p.x,s,p.y);
                float depth=role<2?.38f:.46f;
                return new Vector3(p.x,p.y,s*depth);
            });
            var vertices=mesh.vertices;var triangles=mesh.triangles;
            double volume=0;
            for(int i=0;i<triangles.Length;i+=3)
                volume+=Vector3.Dot(vertices[triangles[i]],Vector3.Cross(vertices[triangles[i+1]],vertices[triangles[i+2]]));
            if(volume<0)for(int i=0;i<triangles.Length;i+=3){int old=triangles[i];triangles[i]=triangles[i+1];triangles[i+1]=old;}
            mesh.triangles=triangles;mesh.RecalculateNormals();
            var normals=mesh.normals;
            for(int y=0;y<=rings;y++){int a=y*(segments+1),b=a+segments;var n=(normals[a]+normals[b]).normalized;normals[a]=normals[b]=n;}
            for(int x=0;x<=segments;x++){normals[x]=surface?Vector3.down:Vector3.back;normals[rings*(segments+1)+x]=surface?Vector3.up:Vector3.forward;}
            mesh.normals=normals;return mesh;
        }
    }
}
