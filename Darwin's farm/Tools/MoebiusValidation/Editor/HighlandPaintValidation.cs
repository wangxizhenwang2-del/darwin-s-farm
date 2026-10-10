using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Darwin.Rendering.Editor.Paint
{
    // Builds a dedicated import/paint demo in an isolated project. Never edits the source FBX.
    public static class HighlandPaintValidation
    {
        const string Output="Assets/Rendering/Moebius/Samples/Paint/Highland";
        const string Materials="Assets/Rendering/Moebius/Content/Paint/Materials/Highland";
        const string Model="Assets/Art/ImportedHighland/Highland.fbx";
        const string Results="Validation/HighlandPaint";
        static string captureFolder=Results;
        static readonly List<string> notes=new List<string>(),errors=new List<string>();
        static void Check(bool condition,string description)
        {notes.Add((condition?"PASS: ":"FAIL: ")+description);if(!condition)throw new Exception(description);}

        public static void RunBatch()
        {
            Application.logMessageReceived+=(message,stack,type)=>
            {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message);};
            try
            {
                if(!Application.isBatchMode || !File.Exists("HighlandPaint.marker"))throw new Exception("Use Tools/ValidateHighlandPaint.ps1 in its isolated project.");
                Directory.CreateDirectory(Results);Directory.CreateDirectory(Output+"/Meshes");Directory.CreateDirectory(Materials);AssetDatabase.Refresh();
                ShaderUtil.allowAsyncCompilation=false;MoebiusPaintAssets.EnsureDefaultTexture();
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var importer=(ModelImporter)AssetImporter.GetAtPath(Model);
                importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;
                importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
                importer.meshCompression=ModelImporterMeshCompression.Off;importer.importNormals=ModelImporterNormals.Import;
                importer.SaveAndReimport();
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
                Check(source!=null,"Maya FBX imports successfully");
                var wrapper=new GameObject("Highland - Paintable Model");
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(source);
                instance.transform.SetParent(wrapper.transform,false);
                // This prefab is a prepared derivative; the imported FBX remains untouched.
                PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                var renderers=instance.GetComponentsInChildren<MeshRenderer>(true);
                Check(renderers.Length==38,"All 38 source mesh renderers are imported");
                var rawUV=new Dictionary<Mesh,Vector2[]>();
                MeshRenderer ground=null;int mountain=0,slots=0;
                for(int i=0;i<renderers.Length;i++)
                {
                    var renderer=renderers[i];var filter=renderer.GetComponent<MeshFilter>();var original=filter.sharedMesh;
                    rawUV[original]=original.uv.ToArray();
                    var mesh=UnityEngine.Object.Instantiate(original);mesh.name="Highland_PaintMesh_"+i.ToString("D2");
                    var parameters=new UnwrapParam();UnwrapParam.SetDefaults(out parameters);
                    parameters.packMargin=8f/1024;parameters.angleError=8;parameters.areaError=15;parameters.hardAngle=88;
                    Check(Unwrapping.GenerateSecondaryUVSet(mesh,parameters),"Unique paint UV unwrap succeeds: "+renderer.name);
                    mesh.uv=mesh.uv2;mesh.uv2=Array.Empty<Vector2>();
                    Check(mesh.uv.Length==mesh.vertexCount && mesh.uv.All(v=>v.x>=0 && v.y>=0 && v.x<=1 && v.y<=1),"Paint UV0 lies within 0-1: mesh "+i);
                    Check(mesh.triangles.Length==original.triangles.Length && mesh.bounds==original.bounds,"Paint derivative preserves geometry: mesh "+i);
                    AssetDatabase.CreateAsset(mesh,Output+"/Meshes/"+mesh.name+".asset");filter.sharedMesh=mesh;
                    if(renderer.name=="pPlane2"){renderer.name="Ground - Paint Here";ground=renderer;}
                    else if(renderer.name.Contains("Mountain"))renderer.name="Mountain "+(++mountain)+" - Paintable";
                    var bindings=renderer.sharedMaterials;
                    for(int slot=0;slot<bindings.Length;slot++)
                    {
                        var sourceMaterial=bindings[slot];
                        var material=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Samples/Shared/Materials/Peach.mat"));
                        material.name="Highland_"+i.ToString("D2")+"_Slot"+slot;
                        var color=sourceMaterial && sourceMaterial.HasProperty("_Color")?sourceMaterial.GetColor("_Color"):new Color(.68f,.67f,.62f);
                        material.SetColor("_BaseColor",color);material.SetTexture("_BaseMap",null);material.SetTexture("_ControlMap",null);
                        material.SetFloat("_UsePaintColor",0);material.SetFloat("_EnableSpatialGradient",0);material.SetFloat("_EnableColorRamp",0);
                        material.SetFloat("_UseHatching",1);material.EnableKeyword("_USE_HATCHING");
                        material.SetFloat("_HatchStrength",.65f);material.SetFloat("_StippleStrength",.06f);material.SetFloat("_StippleThreshold",.8f);
                        AssetDatabase.CreateAsset(material,Materials+"/"+material.name+".mat");bindings[slot]=material;slots++;
                    }
                    renderer.sharedMaterials=bindings;renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
                }
                Check(ground!=null && mountain==2,"Ground and both originally UV-less mountains are prepared");
                var bounds=BoundsOf(wrapper);wrapper.transform.localScale=Vector3.one*(17/Mathf.Max(bounds.size.x,bounds.size.z));
                bounds=BoundsOf(wrapper);wrapper.transform.position=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z);
                foreach(var renderer in renderers)
                {
                    new MoebiusPaintMesh(renderer);
                    foreach(var material in renderer.sharedMaterials)Check(MoebiusPaintAssets.CanEdit(material),"Material is ready for isolated albedo painting: "+material.name);
                }
                foreach(var entry in rawUV)Check(entry.Key.uv.SequenceEqual(entry.Value),"Imported source mesh UVs remain unchanged: "+entry.Key.name);
                PrefabUtility.SaveAsPrefabAsset(wrapper,Output+"/Highland_Paintable.prefab");
                bounds=BoundsOf(wrapper);
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.65f,.7f,.75f);RenderSettings.fog=false;
                var light=new GameObject("Directional Light").AddComponent<Light>();light.type=LightType.Directional;
                light.transform.rotation=Quaternion.Euler(50,-30,0);light.intensity=1;light.shadows=LightShadows.Soft;RenderSettings.sun=light;
                var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.47f,.78f,.81f);
                camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.orthographic=true;camera.orthographicSize=12;
                camera.GetUniversalAdditionalCameraData().SetRenderer(1);
                camera.transform.position=bounds.center+new Vector3(15,16,-21);camera.transform.LookAt(bounds.center);
                EditorSceneManager.SaveScene(scene,Output+"/Highland_PaintTest.unity");
                var before=Capture(camera,"Before");
                // Keep the clean scene/prefab clean. Only the separate example uses a painted material.
                var exampleMaterial=new Material(ground.sharedMaterial){name="Highland_Ground_GradientExample"};
                AssetDatabase.CreateAsset(exampleMaterial,Materials+"/Highland_Ground_GradientExample.mat");ground.sharedMaterial=exampleMaterial;
                var surface=new MoebiusPaintMesh(ground);
                var ray=new Ray(new Vector3(-1.5f,20,-5.5f),Vector3.down);
                Check(surface.Raycast(ray,out var hit),"Imported ground can be picked without adding a MeshCollider");
                var originalControl=exampleMaterial.GetTexture("_ControlMap");
                byte[] painted;string texturePath;
                using(var session=new MoebiusPaintSession(surface,0,MoebiusPaintLayer.Color,1024))
                {
                    var empty=session.ReadPng();
                    session.BeginStroke();
                    // Two overlapping soft color strokes. All lighting remains in the existing NPR shader.
                    foreach(var x in new[]{-3f,-2.5f,-2f,-1.5f,-1f,-.5f,.0f,.5f,1f})
                    {
                        if(surface.Raycast(new Ray(new Vector3(x,20,-5.5f),Vector3.down),out var stamp))
                            session.Dab(stamp.point,stamp.normal,2,.65f,.95f,new Color(.96f,.57f,.42f),0,false);
                    }
                    foreach(var x in new[]{-1f,-.5f,0,.5f,1f,1.5f,2f,2.5f,3f})
                    {
                        if(surface.Raycast(new Ray(new Vector3(x,20,-5.5f),Vector3.down),out var stamp))
                            session.Dab(stamp.point,stamp.normal,2,.65f,.95f,new Color(.60f,.57f,.86f),0,false);
                    }
                    session.EndStroke();painted=session.ReadPng();
                    Check(!empty.SequenceEqual(painted),"GPU brush paints soft two-color strokes on the imported ground");
                    Check(!before.SequenceEqual(Capture(camera,"Gradient")),"Painted albedo gradient appears through the existing NPR renderer");
                    var decoded=Decode(painted);var pixels=decoded.GetPixels();
                    Check(pixels.Any(p=>p.a>.8f && p.r>p.b+.15f) && pixels.Any(p=>p.a>.8f && p.b>p.r+.1f),"Warm and cool albedo regions are both present");
                    Check(pixels.Any(p=>p.a>.5f && Mathf.Abs(p.r-p.b)<.07f),"Soft strokes contain blended intermediate colors");
                    UnityEngine.Object.DestroyImmediate(decoded);
                    Undo.PerformUndo();Check(empty.SequenceEqual(session.ReadPng()),"Undo restores the unpainted imported ground");
                    Undo.PerformRedo();Check(painted.SequenceEqual(session.ReadPng()),"Redo restores the complete gradient stroke");
                    var texture=session.Save();texturePath=AssetDatabase.GetAssetPath(texture);
                    Check(File.Exists(texturePath),"Gradient saves to a persistent paint PNG");
                    session.ClearLayer();session.Revert();Check(painted.SequenceEqual(session.ReadPng()),"Revert restores the saved imported-model gradient");
                }
                using(var reopened=new MoebiusPaintSession(surface,0,MoebiusPaintLayer.Color,1024))
                    Check(painted.SequenceEqual(reopened.ReadPng()),"Reopening the imported-model paint session preserves the gradient");
                Check(exampleMaterial.GetTexture("_ControlMap")==originalControl,"Albedo painting does not change hatching/highlight/shadow masks");
                EditorSceneManager.SaveScene(scene,Output+"/Highland_GradientExample.unity");
                var saved=Capture(camera,"SavedGradient");
                light.transform.rotation=Quaternion.Euler(30,100,0);
                Check(!saved.SequenceEqual(Capture(camera,"DifferentLight")),"The painted model still responds to a changed light direction");
                Check(exampleMaterial.GetTexture("_PaintColorMap")!=null,"Light rotation preserves the albedo paint texture");
                light.transform.rotation=Quaternion.Euler(50,-30,0);
                // Exercise the user's actual window and EditorTool on the imported ground.
                Selection.activeGameObject=ground.gameObject;MoebiusPaintWindow.Open();MoebiusPaintWindow.Instance.OpenSession();
                Check(MoebiusPaintWindow.Instance.SessionReady,"Paint editor opens a ready-to-paint session on Highland");
                ToolManager.SetActiveTool<MoebiusPaintTool>();Check(ToolManager.activeToolType==typeof(MoebiusPaintTool),"Native Scene painting tool activates on Highland");
                ToolManager.RestorePreviousTool();MoebiusPaintWindow.Instance.Close();
                AssetDatabase.SaveAssets();
                // Reload the scene from disk, rather than trusting the in-memory session.
                EditorSceneManager.OpenScene(Output+"/Highland_GradientExample.unity");
                var loadedGround=GameObject.Find("Ground - Paint Here").GetComponent<MeshRenderer>();
                using(var reopened=new MoebiusPaintSession(new MoebiusPaintMesh(loadedGround),0,MoebiusPaintLayer.Color,1024))
                    Check(painted.SequenceEqual(reopened.ReadPng()),"Saved scene reloads the actual imported-model paint");
                Check(saved.SequenceEqual(Capture(Camera.main,"ReloadedGradient")),"Scene reload preserves the rendered gradient exactly");
                foreach(var shader in new[]{Shader.Find("Darwin/Moebius NPR"),Shader.Find("Hidden/Darwin/Moebius Outline"),Shader.Find("Hidden/Darwin/Moebius Paint Brush")})
                    foreach(var message in ShaderUtil.GetShaderMessages(shader))
                        if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)errors.Add(message.message);
                var dependencies=AssetDatabase.GetDependencies(new[]{Output+"/Highland_PaintTest.unity",Output+"/Highland_GradientExample.unity",Output+"/Highland_Paintable.prefab"},true);
                File.WriteAllLines(Results+"/GeneratedAssets.txt",dependencies.Where(p=>p.StartsWith(Output+"/") || p.StartsWith(Materials+"/") || p==texturePath || p==Model));
                notes.Add("Source renderers: "+renderers.Length+"; unique material slots: "+slots+"; paint UV derivative meshes: "+rawUV.Count);
            }
            catch(Exception exception){errors.Add(exception.ToString());}
            Directory.CreateDirectory(Results);
            File.WriteAllText(Results+"/Report.txt",string.Join("\n",notes)+"\nShader/runtime/test errors: "+errors.Count+"\n"+string.Join("\n",errors));
            EditorApplication.Exit(errors.Count==0?0:1);
        }

        // Cold-process smoke test of the delivered assets, in a project or its isolated mirror.
        public static void VerifyInstalled()
        {
            captureFolder="Artifacts/HighlandPaint/Installed";Directory.CreateDirectory(captureFolder);
            Application.logMessageReceived+=(message,stack,type)=>
            {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message);};
            try
            {
                if(!Application.isBatchMode)throw new Exception("Use batch mode for the installed-asset smoke test.");
                ShaderUtil.allowAsyncCompilation=false;
                Check(AssetDatabase.LoadAssetAtPath<GameObject>(Model)!=null,"Delivered original Highland FBX imports successfully");
                Check(AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/Highland_Paintable.prefab")!=null,"Delivered paintable prefab loads successfully");
                foreach(var name in new[]{"Highland_PaintTest","Highland_GradientExample"})
                {
                    var scene=EditorSceneManager.OpenScene(Output+"/"+name+".unity");
                    var model=GameObject.Find("Highland - Paintable Model");var renderers=model.GetComponentsInChildren<MeshRenderer>();
                    Check(renderers.Length==38,name+": all 38 renderers load");
                    foreach(var renderer in renderers)
                    {
                        new MoebiusPaintMesh(renderer);
                        Check(renderer.sharedMaterials.All(MoebiusPaintAssets.CanEdit),name+": material/mesh binding is paint-ready on "+renderer.name);
                    }
                    var ground=GameObject.Find("Ground - Paint Here");Selection.activeGameObject=ground;
                    MoebiusPaintWindow.Open();MoebiusPaintWindow.Instance.OpenSession();
                    Check(MoebiusPaintWindow.Instance.SessionReady,name+": actual paint window opens successfully");
                    Check(MoebiusPaintWindow.Instance.Session.layer==MoebiusPaintLayer.Color,name+": workspace targets albedo only");
                    MoebiusPaintWindow.Instance.Close();
                    var material=ground.GetComponent<MeshRenderer>().sharedMaterial;
                    if(name=="Highland_PaintTest")Check(material.GetFloat("_UsePaintColor")==0,"Clean scene remains unpainted");
                    else Check(material.GetFloat("_UsePaintColor")==1 && material.GetTexture("_PaintColorMap")!=null,"Example scene references its saved gradient texture");
                    Capture(Camera.main,name);
                    Check(!scene.isDirty,name+": inspection does not dirty the scene");
                }
                foreach(var shader in new[]{Shader.Find("Darwin/Moebius NPR"),Shader.Find("Hidden/Darwin/Moebius Outline"),Shader.Find("Hidden/Darwin/Moebius Paint Brush")})
                    foreach(var message in ShaderUtil.GetShaderMessages(shader))
                        if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)errors.Add(message.message);
            }
            catch(Exception exception){errors.Add(exception.ToString());}
            File.WriteAllText(captureFolder+"/Report.txt",string.Join("\n",notes)+"\nShader/runtime/test errors: "+errors.Count+"\n"+string.Join("\n",errors));
            EditorApplication.Exit(errors.Count==0?0:1);
        }
        static Bounds BoundsOf(GameObject root)
        {
            var renderers=root.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
            foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);return bounds;
        }
        static Texture2D Decode(byte[] png)
        {var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);texture.LoadImage(png);return texture;}
        static byte[] Capture(Camera camera,string name)
        {
            var target=new RenderTexture(1400,1000,24,RenderTextureFormat.ARGB32);target.Create();
            var previous=RenderTexture.active;var original=camera.targetTexture;
            camera.targetTexture=target;camera.Render();camera.Render();RenderTexture.active=target;
            var image=new Texture2D(1400,1000,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1400,1000),0,0);image.Apply();var png=image.EncodeToPNG();
            File.WriteAllBytes(captureFolder+"/"+name+".png",png);
            RenderTexture.active=previous;camera.targetTexture=original;target.Release();
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);return png;
        }
    }
}
