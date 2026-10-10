using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Darwin.Rendering.Editor
{
    public static class MoebiusBiomePreviewBuilder
    {
        const string Models = "Assets/Art/ImportedBiomes/Models";
        const string Output = "Assets/Rendering/Moebius/Samples/Biomes";
        [Serializable] class SourceDatabase { public SourceModel[] models; }
        [Serializable] class SourceModel { public string name; public int meshes; public int triangles; public SourceMaterial[] materials; public SourceTexture[] textures; }
        [Serializable] class SourceMaterial { public string name; public float[] rgb; public string type; }
        [Serializable] class SourceTexture { public string node; public string path; }
        static readonly string[] Names = {"Grassland","Desert","Forest","RainForest"};
        static readonly List<string> Notes = new List<string>();

        public static void RunBatch()
        {
            var errors = new List<string>();
            Application.logMessageReceived += (message,stack,type) =>
            { if (type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors.Add(message); };
            int exit = 0;
            try
            {
                if (!Application.isBatchMode || !File.Exists("BiomeValidation.marker")) throw new Exception("Run Tools/ValidateMoebiusBiomes.ps1 in an isolated project.");
                Directory.CreateDirectory(Output+"/Materials"); Directory.CreateDirectory(Output+"/Prefabs");
                Directory.CreateDirectory(Output+"/Scenes"); Directory.CreateDirectory("Validation");
                AssetDatabase.Refresh(); ShaderUtil.allowAsyncCompilation=false;
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline; QualitySettings.renderPipeline=pipeline;
                var database = JsonUtility.FromJson<SourceDatabase>(File.ReadAllText(Models+"/SourceMaterials.json"));
                foreach(var name in Names) CreatePrefab(database.models.First(m=>m.name==name));
                foreach(var name in Names)
                {
                    var camera=CreateScene(name);
                    Capture(camera,"Validation/"+name+".png");
                }
                Capture(CreateScene(null),"Validation/Overview.png");
                foreach(var shader in new[]{Shader.Find("Darwin/Moebius NPR"),Shader.Find("Hidden/Darwin/Moebius Outline")})
                    foreach(var message in ShaderUtil.GetShaderMessages(shader))
                        if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors.Add(message.message);
                AssetDatabase.SaveAssets();
                Notes.Add("Shader/runtime errors: "+errors.Count); Notes.AddRange(errors);
                File.WriteAllLines("Validation/Report.txt",Notes);
                if(errors.Count>0) exit=1;
            }
            catch(Exception exception) { Directory.CreateDirectory("Validation");File.WriteAllText("Validation/Report.txt",exception.ToString());exit=1; }
            EditorApplication.Exit(exit);
        }

        static string Key(string name) => name.Replace(" (Instance)","").Replace(":","_").Replace(" ","");
        static void CreatePrefab(SourceModel source)
        {
            string path=Models+"/"+source.name+".fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.importCameras=false; importer.importLights=false; importer.importAnimation=false;
            importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            importer.meshCompression=ModelImporterMeshCompression.Off;
            importer.SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(model==null) throw new Exception("Failed to import "+path);
            var root=new GameObject(source.name+" Moebius");
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(model); instance.transform.SetParent(root.transform,false);
            var renderers=instance.GetComponentsInChildren<MeshRenderer>(true);
            if(renderers.Length==0) throw new Exception(source.name+" has no renderers");
            var converted=new Dictionary<string,Material>();
            foreach(var renderer in renderers)
            {
                var materials=renderer.sharedMaterials;
                for(int slot=0;slot<materials.Length;slot++)
                {
                    var original=materials[slot]; string key=original!=null?Key(original.name):"Default";
                    if(!converted.TryGetValue(key,out var material))
                    {
                        string unprefixed=key.StartsWith(source.name+"-")?key.Substring(source.name.Length+1):key;
                        var definition=source.materials.FirstOrDefault(m=>Key(m.name)==unprefixed);
                        Color color=new Color(.65f,.65f,.65f,1);
                        if(definition!=null && definition.rgb.Length==3) color=new Color(definition.rgb[0],definition.rgb[1],definition.rgb[2],1);
                        else if(original!=null && original.HasProperty("_Color")) {color=original.GetColor("_Color");Notes.Add(source.name+": FBX color fallback for "+key);}
                        string safe=new string(key.Select(c=>char.IsLetterOrDigit(c)||c=='_'?c:'_').ToArray());
                        string matPath=Output+"/Materials/"+source.name+"_"+safe+".mat";
                        material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
                        if(material==null)
                        {
                            material=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Samples/Shared/Materials/Peach.mat"));
                            AssetDatabase.CreateAsset(material,matPath);
                        }
                        material.SetColor("_BaseColor",color); material.SetTexture("_BaseMap",null);
                        material.SetFloat("_UseHatching",1); material.EnableKeyword("_USE_HATCHING");
                        material.SetFloat("_HatchScale",125); material.SetFloat("_HatchStrength",.65f);
                        material.SetFloat("_StippleStrength",.06f); material.SetFloat("_StippleThreshold",.8f);
                        material.SetFloat("_DebugMode",0); EditorUtility.SetDirty(material);
                        converted.Add(key,material);
                    }
                    materials[slot]=material;
                }
                renderer.sharedMaterials=materials;
                renderer.shadowCastingMode=ShadowCastingMode.On; renderer.receiveShadows=true;
                // Source scenes contain variant tiles and modelling samples; keep full geometry in the FBX.
                if(source.name=="Forest") renderer.enabled &= InBranch(renderer.transform,"Forest_have_edge");
                if(source.name=="Grassland") renderer.enabled &= renderer.bounds.center.z < 8.5f;
                if(source.name=="Desert") renderer.enabled &= Mathf.Abs(renderer.bounds.center.x) < 8.5f;
                if(source.name=="RainForest") renderer.enabled &= renderer.bounds.center.z < -20;
            }
            Notes.Add(source.name+": representative preview renderers "+renderers.Count(r=>r.enabled && r.gameObject.activeInHierarchy)+"; full FBX retained.");
            var bounds=GetBounds(root);
            float factor=12/Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
            root.transform.localScale=Vector3.one*factor;
            bounds=GetBounds(root); root.transform.position=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z);
            // Bake presentation normalization into a wrapper; source FBX transforms stay untouched.
            var wrapper=new GameObject(source.name+" NPR Preview"); root.transform.SetParent(wrapper.transform,true);
            PrefabUtility.SaveAsPrefabAsset(wrapper,Output+"/Prefabs/"+source.name+"_Moebius.prefab");
            Notes.Add(source.name+": "+source.meshes+" source meshes; "+source.triangles+" triangles; "+renderers.Length+" Unity renderers; "+converted.Count+" NPR materials; preview scale "+factor);
            foreach(var texture in source.textures) Notes.Add(source.name+" source texture reference (not supplied): "+texture.path);
            UnityEngine.Object.DestroyImmediate(wrapper);
        }

        static Bounds GetBounds(GameObject root)
        {
            var renderers=root.GetComponentsInChildren<Renderer>().Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if(renderers.Length==0) throw new Exception("No visible renderers in "+root.name);
            var bounds=renderers[0].bounds; foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        static bool InBranch(Transform transform,string name)
        {
            while(transform!=null) {if(transform.name==name) return true;transform=transform.parent;}
            return false;
        }
        static Camera CreateScene(string single)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.65f,.7f,.75f);
            RenderSettings.fog=false;
            var light=new GameObject("Directional Light").AddComponent<Light>(); light.type=LightType.Directional;
            light.transform.rotation=Quaternion.Euler(50,-30,0);light.intensity=1;light.shadows=LightShadows.Soft;
            RenderSettings.sun=light;
            var group=new GameObject("Imported biome models");
            for(int i=0;i<Names.Length;i++)
            {
                if(single!=null && Names[i]!=single) continue;
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/Prefabs/"+Names[i]+"_Moebius.prefab"));
                instance.transform.SetParent(group.transform,false);
                if(single==null) instance.transform.position=new Vector3(i%2==0?-8:8,0,i<2?-8:8);
            }
            var bounds=GetBounds(group);
            var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.47f,.78f,.81f);
            camera.nearClipPlane=.1f;camera.farClipPlane=300;camera.fieldOfView=50;
            camera.GetUniversalAdditionalCameraData().SetRenderer(1);
            var focus=new GameObject("Camera focus");focus.transform.position=bounds.center;
            camera.orthographic=single==null;camera.orthographicSize=23;
            camera.transform.position=bounds.center+Quaternion.Euler(single==null?48:32,-30,0)*new Vector3(0,0,single==null?-38:-24);
            camera.transform.LookAt(bounds.center);
            EditorSceneManager.SaveScene(scene,Output+"/Scenes/"+(single??"AllBiomes")+"_Moebius.unity");
            return camera;
        }
        static void Capture(Camera camera,string path)
        {
            var target=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);
            var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            target.Create(); camera.targetTexture=target;camera.Render();camera.Render();
            var previous=RenderTexture.active;RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            RenderTexture.active=previous;camera.targetTexture=null;target.Release();
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
