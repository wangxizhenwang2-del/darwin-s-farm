using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Darwin.Rendering.Validation
{
    // Read-only asset smoke test; this source is compiled only in the marked Temp fixture.
    public static class MoebiusRiverTransferValidation
    {
        const string PrefabPath = "Assets/Rendering/Moebius/Samples/Biomes/Prefabs/River.prefab";
        const string Output = "Validation/RiverTransfer";
        static readonly List<string> Notes = new List<string>(), Errors = new List<string>();
        static void Check(bool value, string message)
        {
            Notes.Add((value ? "PASS: " : "FAIL: ") + message);
            if (!value) throw new InvalidOperationException(message);
        }
        public static void RunBatch()
        {
            Directory.CreateDirectory(Output);
            Application.logMessageReceived += (message,stack,type) =>
            {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) Errors.Add(message);};
            try
            {
                Check(Application.isBatchMode && File.Exists("RiverTransfer.marker") && Application.dataPath.Replace('\\','/').Contains("/Temp/"), "Isolated fixture guard");
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                Check(pipeline,"Existing URP pipeline loads");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                ShaderUtil.allowAsyncCompilation=false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Check(prefab,"Transferred River prefab loads");
                var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var transforms=root.GetComponentsInChildren<Transform>(true);
                Check(transforms.All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"No missing scripts");
                var renderers=root.GetComponentsInChildren<MeshRenderer>(true);
                Check(renderers.Length>0,"Mesh renderers exist: "+renderers.Length);
                foreach(var renderer in renderers)
                {
                    var filter=renderer.GetComponent<MeshFilter>();
                    Check(filter && filter.sharedMesh,renderer.name+": mesh reference resolves");
                    Check(renderer.sharedMaterials.Length>=filter.sharedMesh.subMeshCount && renderer.sharedMaterials.All(m=>m && m.shader && m.shader.isSupported),renderer.name+": material and shader references resolve");
                    foreach(var material in renderer.sharedMaterials)
                        if(material.HasProperty("_UsePaintColor") && material.GetFloat("_UsePaintColor")>.5f)
                            Check(material.GetTexture("_PaintColorMap"),material.name+": original painted color texture resolves");
                }
                var materials=renderers.SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
                var fork=AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Content/Paint/Materials/River_Ground_YL.mat");
                Check(fork && materials.Contains(fork),"River uses its isolated YL ground material");
                var original=AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Content/Paint/Materials/Fprest_Block_Forest_Forest_openPBR_shader1_Paint 1.mat");
                Check(original && original!=fork && !materials.Contains(original),"Vincent's existing forest material is retained separately");
                var bounds=renderers[0].bounds;
                foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
                Check(bounds.size.sqrMagnitude>0 && !float.IsInfinity(bounds.size.sqrMagnitude),"Valid bounds: "+bounds);
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.7f,.74f,.78f);RenderSettings.fog=false;
                var light=new GameObject("Validation Light").AddComponent<Light>();
                light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(50,-30,0);light.intensity=1;light.shadows=LightShadows.Soft;
                light.gameObject.AddComponent<UniversalAdditionalLightData>();RenderSettings.sun=light;
                var camera=new GameObject("Validation Camera").AddComponent<Camera>();
                camera.GetUniversalAdditionalCameraData().SetRenderer(1);camera.orthographic=true;
                camera.orthographicSize=Mathf.Max(bounds.size.x,bounds.size.z)*.65f;
                float span=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.z,bounds.size.y));
                camera.nearClipPlane=Mathf.Max(.01f,span*.0001f);camera.farClipPlane=Mathf.Max(150,span*10);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.45f,.75f,.79f);
                camera.transform.position=bounds.center+new Vector3(-1,1.1f,-1.3f)*span;camera.transform.LookAt(bounds.center);
                Capture(camera,"Overview");
                camera.transform.position=bounds.center+new Vector3(1,1.1f,-1.3f)*span;camera.transform.LookAt(bounds.center);
                Capture(camera,"OtherSide");
                foreach(var shader in materials.Select(m=>m.shader).Concat(new[]{Shader.Find("Hidden/Darwin/Moebius Outline")}).Where(s=>s).Distinct())
                    foreach(var message in ShaderUtil.GetShaderMessages(shader))
                        if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)Errors.Add(shader.name+": "+message.message);
                Notes.Add("Unique materials: "+materials.Length);
                Notes.Add("Triangle count: "+renderers.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3));
                Notes.Add("No source assets or game scenes were saved by this validation.");
            }
            catch(Exception e){Errors.Add(e.ToString());}
            Notes.Add("Errors: "+Errors.Count);Notes.AddRange(Errors);
            File.WriteAllLines(Output+"/Report.txt",Notes);EditorApplication.Exit(Errors.Count==0?0:1);
        }
        static void Capture(Camera camera,string name)
        {
            var target=new RenderTexture(1600,1100,24,RenderTextureFormat.ARGB32);target.Create();
            var texture=new Texture2D(1600,1100,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            camera.targetTexture=target;camera.Render();camera.Render();RenderTexture.active=target;
            texture.ReadPixels(new Rect(0,0,1600,1100),0,0);texture.Apply();File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=previous;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);
        }
    }
}
