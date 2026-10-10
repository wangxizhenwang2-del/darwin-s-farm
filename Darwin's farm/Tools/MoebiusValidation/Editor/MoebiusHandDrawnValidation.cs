using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Darwin.Rendering.Editor
{
    // Reads existing scenes and materials; never rebuilds or saves the user's model scenes.
    public static class MoebiusHandDrawnValidation
    {
        public static void RunBatch()
        {
            int exit=0;var errors=new List<string>();var checks=new List<string>();
            Application.logMessageReceived+=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);};
            try
            {
                if(!Application.isBatchMode || !File.Exists("HandDrawnValidation.marker")) throw new Exception("Use isolated validation project.");
                string label=File.ReadAllText("HandDrawnValidation.marker").Trim();
                string folder="Validation/HandDrawn/"+label;Directory.CreateDirectory(folder);
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;ShaderUtil.allowAsyncCompilation=false;
                foreach(string name in new[]{"RainForest","Forest","Grassland","Desert"})
                {
                    EditorSceneManager.OpenScene("Assets/Rendering/Moebius/Samples/Biomes/Scenes/"+name+"_Moebius.unity",OpenSceneMode.Single);
                    var camera=UnityEngine.Object.FindFirstObjectByType<Camera>();
                    // A temporary receiving surface tests cast shadows; no source scene is saved.
                    var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Validation receiving ground";
                    ground.transform.position=new Vector3(0,-.04f,0);ground.transform.localScale=new Vector3(2,1,2);
                    var material=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Samples/Shared/Materials/Peach.mat"));
                    material.SetColor("_BaseColor",new Color(.83f,.75f,.62f));material.SetFloat("_UseHatching",0);material.DisableKeyword("_USE_HATCHING");
                    material.SetFloat("_CastShadowTintStrength",.05f);
                    ground.GetComponent<Renderer>().sharedMaterial=material;
                    Capture(camera,folder+"/"+name+".png");
                    if(name=="RainForest")
                    {
                        var first=Capture(camera,folder+"/RainForest_StationaryA.png");
                        var second=Capture(camera,folder+"/RainForest_StationaryB.png");
                        int changed=0;for(int i=0;i<first.Length;i++)if(!first[i].Equals(second[i]))changed++;
                        checks.Add("Stationary repeated render: "+changed+" changed pixels / "+first.Length);
                        if(changed>first.Length*.001f)errors.Add("Stationary image changes unexpectedly.");
                        var cameraPosition=camera.transform.position;var cameraRotation=camera.transform.rotation;
                        var focus=GameObject.Find("Camera focus").transform.position;
                        for(int frame=0;frame<4;frame++)
                        {
                            camera.transform.position=focus+Quaternion.Euler(0,frame*8,0)*(cameraPosition-focus);
                            camera.transform.LookAt(focus);
                            Capture(camera,folder+"/RainForest_Orbit"+frame+".png");
                        }
                        camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
                        var model=GameObject.Find("Imported biome models").transform;
                        var modelPosition=model.position;var modelRotation=model.rotation;
                        model.position+=new Vector3(.7f,0,0);model.Rotate(0,12,0,Space.World);
                        Capture(camera,folder+"/RainForest_ObjectMoved.png");
                        model.SetPositionAndRotation(modelPosition,modelRotation);
                        Capture(camera,folder+"/RainForest_LowResolution.png",700,500);
                        material.SetFloat("_UseTriplanar",1);material.EnableKeyword("_USE_TRIPLANAR");material.SetFloat("_HatchScale",4);
                        Capture(camera,folder+"/RainForest_WorldHatching.png");
                        material.SetFloat("_UseTriplanar",0);material.DisableKeyword("_USE_TRIPLANAR");material.SetFloat("_HatchScale",125);
                    }
                    camera.transform.position+=new Vector3(.08f,0,.04f);
                    Capture(camera,folder+"/"+name+"_Moved.png");
                    UnityEngine.Object.DestroyImmediate(ground);UnityEngine.Object.DestroyImmediate(material);
                }
                EditorSceneManager.OpenScene("Assets/Rendering/Moebius/Samples/Billboard/Scenes/MoebiusBillboardTest.unity",OpenSceneMode.Single);
                Capture(UnityEngine.Object.FindFirstObjectByType<Camera>(),folder+"/Billboard.png");
                // A temporary smooth sphere makes the white highlight/border visible independently of low-poly trees.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var testCamera=new GameObject("Highlight validation camera").AddComponent<Camera>();
                testCamera.GetUniversalAdditionalCameraData().SetRenderer(1);
                testCamera.transform.position=new Vector3(0,1,-8);testCamera.transform.LookAt(Vector3.zero);
                testCamera.backgroundColor=new Color(.45f,.77f,.8f);testCamera.clearFlags=CameraClearFlags.SolidColor;
                var testLight=new GameObject("Highlight validation light").AddComponent<Light>();
                testLight.type=LightType.Directional;testLight.transform.rotation=Quaternion.Euler(35,-30,0);
                testLight.shadows=LightShadows.Soft;
                var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.transform.localScale=Vector3.one*3;
                var sphereMaterial=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Samples/Shared/Materials/Blue.mat"));
                sphereMaterial.SetFloat("_HighlightThreshold",.94f);sphereMaterial.SetFloat("_HighlightSize",.1f);
                sphereMaterial.SetFloat("_HighlightNoiseStrength",.002f);sphere.GetComponent<Renderer>().sharedMaterial=sphereMaterial;
                Capture(testCamera,folder+"/HighlightSphere.png");
                testCamera.transform.position=new Vector3(2,1,-8);testCamera.transform.LookAt(Vector3.zero);
                Capture(testCamera,folder+"/HighlightSphere_CameraMoved.png");
                sphere.transform.Rotate(0,25,0);Capture(testCamera,folder+"/HighlightSphere_ObjectRotated.png");
                UnityEngine.Object.DestroyImmediate(sphereMaterial);
                foreach(var shader in new[]{Shader.Find("Darwin/Moebius NPR"),Shader.Find("Hidden/Darwin/Moebius Outline"),Shader.Find("Darwin/Moebius Billboard Character")})
                    foreach(var message in ShaderUtil.GetShaderMessages(shader))
                        if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)errors.Add(message.message);
                File.WriteAllText(folder+"/Report.txt","Actual scene renders: four biomes, receiving ground, camera orbit, translated/rotated models and realtime cast shadows, billboard cutout, 700x500 resolution, world-triplanar ground shadow hatching.\n"+string.Join("\n",checks)+"\nShader/runtime errors: "+errors.Count+"\n"+string.Join("\n",errors));
                if(errors.Count>0)exit=1;
            }
            catch(Exception exception){Directory.CreateDirectory("Validation/HandDrawn");File.WriteAllText("Validation/HandDrawn/Failure.txt",exception.ToString());exit=1;}
            EditorApplication.Exit(exit);
        }
        static Color32[] Capture(Camera camera,string path,int width=1400,int height=1000)
        {
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            camera.Render();camera.Render();var old=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            var pixels=image.GetPixels32();File.WriteAllBytes(path,image.EncodeToPNG());RenderTexture.active=old;camera.targetTexture=null;target.Release();
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
            return pixels;
        }
    }
}
