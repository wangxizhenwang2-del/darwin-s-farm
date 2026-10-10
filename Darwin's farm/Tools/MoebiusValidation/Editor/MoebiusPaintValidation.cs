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
    public static class MoebiusPaintValidation
    {
        static readonly List<string> results=new List<string>(),errors=new List<string>();
        static string folder;
        public static void RunBatch()
        {
            Application.logMessageReceived+=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);};
            try
            {
                if(!Application.isBatchMode || !File.Exists("PaintValidation.marker"))throw new Exception("Use isolated validation project.");
                var label=File.ReadAllText("PaintValidation.marker").Trim();folder="Validation/Paint/"+label;Directory.CreateDirectory(folder);
                ShaderUtil.allowAsyncCompilation=false;
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                if(label=="Sphere"){TestSphere();Finish();return;}
                if(label=="Reload"){TestReload();Finish();return;}
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var camera=new GameObject("Paint demo camera").AddComponent<Camera>();camera.GetUniversalAdditionalCameraData().SetRenderer(1);
                camera.transform.position=new Vector3(0,0,-2);camera.orthographic=true;camera.orthographicSize=.7f;
                camera.backgroundColor=new Color(.45f,.77f,.8f);camera.clearFlags=CameraClearFlags.SolidColor;
                var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(25,-30,0);
                var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.name="Moebius Paint Surface";UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
                var renderer=quad.GetComponent<MeshRenderer>();renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Samples/Shared/Materials/Ivory.mat");
                var baseline=Capture(camera,"Original");
                if(label=="After")
                {
                    MoebiusPaintAssets.EnsureDefaultTexture();
                    CompareBefore("Original",baseline);
                    var emptyMaterial=new Material(renderer.sharedMaterial);renderer.sharedMaterial=emptyMaterial;
                    emptyMaterial.SetFloat("_UsePaintColor",1);
                    Check(Same(baseline,Capture(camera,"NoPaintTexture")),"Enabled paint without assigned texture uses transparent default");
                    renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Samples/Shared/Materials/Ivory.mat");UnityEngine.Object.DestroyImmediate(emptyMaterial);
                    var surface=new MoebiusPaintMesh(renderer);
                    Check(surface.Raycast(new Ray(new Vector3(.1f,.2f,-2),Vector3.forward),out var hit),"Mesh picking without MeshCollider");
                    Check(Vector2.Distance(hit.uv,new Vector2(.6f,.7f))<.001f,"Ray hit barycentrics recover UV0");
                    bool blocked=false;try{using(var bad=new MoebiusPaintSession(surface,0,MoebiusPaintLayer.Color,512)){} }catch(InvalidOperationException){blocked=true;}
                    Check(blocked,"Original/shared material is protected before explicit Make Unique");
                    var foreign=new Material(Shader.Find("Universal Render Pipeline/Lit"));Check(!MoebiusPaintAssets.CanEdit(foreign),"Non-Moebius material is rejected");UnityEngine.Object.DestroyImmediate(foreign);
                    var unique=MoebiusPaintAssets.MakeUnique(renderer,0);
                    var other=GameObject.CreatePrimitive(PrimitiveType.Quad);other.name="Shared material check";other.GetComponent<Renderer>().sharedMaterial=unique;other.SetActive(false);
                    Check(!MoebiusPaintAssets.CanEdit(unique),"Shared painting material is blocked even on inactive object");
                    UnityEngine.Object.DestroyImmediate(other);
                    using(var session=new MoebiusPaintSession(surface,0,MoebiusPaintLayer.Color,512))
                    {
                        Check(Same(baseline,Capture(camera,"EmptyPaint")),"Empty color mask preserves original render exactly");
                        var before=session.ReadPng();session.BeginStroke();
                        for(int i=0;i<5;i++)session.Dab(new Vector3(-.25f+i*.1f,.2f,0),Vector3.back,.14f,.6f,.7f,new Color(.9f,.25f,.4f),0,false);
                        session.EndStroke();var painted=session.ReadPng();
                        Check(!before.SequenceEqual(painted),"GPU stroke updates working paint texture");
                        var image=Decode(painted);var center=image.GetPixel((int)(.5f*image.width),(int)(.7f*image.height));
                        var opposite=image.GetPixel((int)(.5f*image.width),(int)(.3f*image.height));
                        Check(center.a>.5f && opposite.a<.01f,"GPU UV raster is aligned and does not mirror vertically");
                        UnityEngine.Object.DestroyImmediate(image);
                        Check(!Same(baseline,Capture(camera,"ColorPaint")),"Color painting immediately changes NPR surface rendering");
                        Undo.PerformUndo();Check(session.ReadPng().SequenceEqual(before),"Undo restores an entire multi-dab stroke");
                        Undo.PerformRedo();Check(session.ReadPng().SequenceEqual(painted),"Redo restores entire stroke");
                        var saved=session.Save();string path=AssetDatabase.GetAssetPath(saved);
                        var import=(TextureImporter)AssetImporter.GetAtPath(path);Check(import.sRGBTexture,"Color PNG is imported as sRGB");
                        Check(File.Exists(path) && unique.GetTexture("_PaintColorMap")==saved,"Save creates persistent PNG and material reference");
                        session.ClearLayer();Check(Same(baseline,Capture(camera,"ColorCleared")),"Clear color removes coverage without changing original style");
                        session.Revert();Check(session.ReadPng().SequenceEqual(painted),"Revert restores saved paint");
                    }
                    using(var reopened=new MoebiusPaintSession(surface,0,MoebiusPaintLayer.Color,512))
                    {var image=Decode(reopened.ReadPng());Check(image.GetPixel(image.width/2,(int)(image.height*.7f)).a>.5f,"Reopening session loads saved painting");UnityEngine.Object.DestroyImmediate(image);}
                    TestBrush(surface);
                    using(var control=new MoebiusPaintSession(surface,0,MoebiusPaintLayer.Hatching,512))
                    {
                        control.BeginStroke();control.Dab(new Vector3(0,-.2f,0),Vector3.back,.2f,1,.5f,Color.white,0,false);control.EndStroke();
                        var image=Decode(control.ReadPng());var pixel=image.GetPixel(image.width/2,(int)(image.height*.3f));
                        Check(pixel.r<.05f && pixel.g>.99f && pixel.b>.99f && pixel.a>.99f,"Control R paints hatching without damaging G/B/A");UnityEngine.Object.DestroyImmediate(image);
                        for(int channel=2;channel<=4;channel++)
                        {
                            control.layer=(MoebiusPaintLayer)channel;control.BeginStroke();
                            control.Dab(new Vector3(0,-.2f,0),Vector3.back,.2f,1,.5f,Color.white,.25f,false);control.EndStroke();
                        }
                        image=Decode(control.ReadPng());pixel=image.GetPixel(image.width/2,(int)(image.height*.3f));
                        Check(Mathf.Abs(pixel.g-.25f)<.02f && Mathf.Abs(pixel.b-.25f)<.02f && Mathf.Abs(pixel.a-.25f)<.02f,"Existing G/B/A channels accept independently painted values");UnityEngine.Object.DestroyImmediate(image);
                        control.layer=MoebiusPaintLayer.Hatching;control.ClearLayer();image=Decode(control.ReadPng());pixel=image.GetPixel(image.width/2,(int)(image.height*.3f));
                        Check(pixel.r>.99f && Mathf.Abs(pixel.g-.25f)<.02f && Mathf.Abs(pixel.a-.25f)<.02f,"Clear/erase restores R default 1 and preserves other channels");UnityEngine.Object.DestroyImmediate(image);
                        var saved=control.Save();Check(!((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(saved))).sRGBTexture,"Control PNG uses linear channel data");
                    }
                    var withoutUV=new Mesh{vertices=new[]{Vector3.zero,Vector3.right,Vector3.up},triangles=new[]{0,1,2}};
                    var filter=quad.GetComponent<MeshFilter>();var originalMesh=filter.sharedMesh;filter.sharedMesh=withoutUV;
                    blocked=false;try{new MoebiusPaintMesh(renderer);}catch(ArgumentException){blocked=true;}
                    Check(blocked,"Missing UV is reported without modifying model");filter.sharedMesh=originalMesh;UnityEngine.Object.DestroyImmediate(withoutUV);
                    TestSlots(renderer);
                    // Save a small dedicated authoring demo, rather than editing formal scenes.
                    const string demo="Assets/Rendering/Moebius/Samples/Paint";Directory.CreateDirectory(demo);AssetDatabase.Refresh();
                    EditorSceneManager.SaveScene(SceneManagerCompat(),demo+"/MoebiusPaintDemo.unity");
                    Selection.activeGameObject=quad;MoebiusPaintWindow.Open();
                    Check(MoebiusPaintWindow.Instance && MoebiusPaintWindow.Instance.Surface!=null,"Paint window opens and identifies selected mesh");
                    ToolManager.SetActiveTool<MoebiusPaintTool>();Check((ToolManager.activeToolType == typeof(MoebiusPaintTool)),"Native EditorTool activates");
                    TestSceneInput();
                    ToolManager.RestorePreviousTool();Check(!(ToolManager.activeToolType == typeof(MoebiusPaintTool)),"Tool deactivates and restores previous tool");
                    MoebiusPaintWindow.Instance.Close();Capture(camera,"SavedDemo");
                    foreach(var shader in new[]{unique.shader,Shader.Find("Hidden/Darwin/Moebius Paint Brush")})
                        foreach(var item in ShaderUtil.GetShaderMessages(shader))if(item.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)errors.Add(item.message);
                    File.WriteAllText(folder+"/DemoMaterial.txt",AssetDatabase.GetAssetPath(unique));
                    File.WriteAllLines(folder+"/DemoAssets.txt",AssetDatabase.GetDependencies(demo+"/MoebiusPaintDemo.unity",true)
                        .Where(p=>p.StartsWith(MoebiusPaintAssets.Folder+"/") || p.StartsWith(demo+"/")));
                }
            }
            catch(Exception exception){errors.Add(exception.ToString());}
            Finish();
        }
        static void Finish()
        {
            if(folder==null)folder="Validation/Paint";Directory.CreateDirectory(folder);
            File.WriteAllText(folder+"/Report.txt",string.Join("\n",results)+"\nShader/runtime/test errors: "+errors.Count+"\n"+string.Join("\n",errors));
            EditorApplication.Exit(errors.Count==0?0:1);
        }
        static UnityEngine.SceneManagement.Scene SceneManagerCompat()=>UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        static void TestSceneInput()
        {
            var window=MoebiusPaintWindow.Instance;window.OpenSession();window.Size=.2f;window.Strength=.8f;window.BrushColor=Color.cyan;
            var activeTool=Resources.FindObjectsOfTypeAll<MoebiusPaintTool>().First();
            var before=window.Session.ReadPng();var view=EditorWindow.GetWindow<SceneView>();view.Show();
            view.LookAt(Vector3.zero,Quaternion.identity,.7f,true,true);
            Vector2 start=default,end=default,startUV=default,endUV=default,eraseUV=default;int guiCalls=0;bool preview=false,positionReady=false;var guiLog=new List<string>();
            Action<SceneView> capture=v=>{if(v==view){
                // SendEvent uses window coordinates; Handles use the nested Scene viewport below its toolbar.
                var localStart=HandleUtility.WorldToGUIPoint(new Vector3(-.2f,-.05f,0));
                if(!positionReady && Event.current.type==EventType.Repaint)
                {
                    start=GUIUtility.GUIToScreenPoint(localStart)-view.position.position;
                    end=GUIUtility.GUIToScreenPoint(HandleUtility.WorldToGUIPoint(new Vector3(.2f,-.05f,0)))-view.position.position;positionReady=true;
                }
                guiCalls++;
                if(Event.current.type==EventType.Repaint)
                {
                    var mouse=Event.current.mousePosition;Event.current.mousePosition=localStart;
                    preview=window.Surface.Raycast(HandleUtility.GUIPointToWorldRay(localStart),out var _);
                    Resources.FindObjectsOfTypeAll<MoebiusPaintTool>().First().OnToolGUI(view);
                    Event.current.mousePosition=mouse;
                }
                window.Surface.Raycast(HandleUtility.GUIPointToWorldRay(Event.current.mousePosition),out var pick);
                if(Event.current.type==EventType.MouseDown){if(Event.current.shift)eraseUV=pick.uv;else startUV=pick.uv;}
                if(Event.current.type==EventType.MouseDrag && !Event.current.alt)endUV=pick.uv;
                guiLog.Add(Event.current.type+" mouse="+Event.current.mousePosition+" hitUV="+pick.uv+" shift="+Event.current.shift+" stroke="+window.Session.InStroke);}};
            SceneView.duringSceneGui+=capture;
            view.SendEvent(new Event{type=EventType.Layout});view.SendEvent(new Event{type=EventType.Repaint});
            File.WriteAllText(folder+"/SceneGUI.txt","GUI callbacks="+guiCalls+", start="+start+", end="+end+", viewport="+view.position);
            view.SendEvent(new Event{type=EventType.MouseMove,mousePosition=start});view.SendEvent(new Event{type=EventType.Layout,mousePosition=start});
            view.SendEvent(new Event{type=EventType.Repaint,mousePosition=start});
            Check(window.Session.ReadPng().SequenceEqual(before),"Scene Handles preview leaves working paint pixels unchanged");
            view.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=start});
            startUV=activeTool.LastPaintHit.uv;
            view.SendEvent(new Event{type=EventType.MouseDrag,button=0,mousePosition=end,delta=end-start});
            endUV=activeTool.LastPaintHit.uv;
            view.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=end});
            File.WriteAllBytes(folder+"/NativeStroke.png",window.Session.ReadPng());
            Check(preview,"Scene View repaint executes surface-oriented brush preview");
            Check(guiCalls>0 && !window.Session.ReadPng().SequenceEqual(before),"Scene View mouse down/drag/up creates a continuous native-tool stroke");
            var painted=Decode(window.Session.ReadPng());var middleUV=(startUV+endUV)*.5f;
            Check(Vector2.Distance(startUV,endUV)>.3f && painted.GetPixel((int)(painted.width*middleUV.x),(int)(painted.height*middleUV.y)).a>.5f,"Brush spacing fills the middle of a dragged stroke");UnityEngine.Object.DestroyImmediate(painted);
            Check(!window.Session.InStroke,"Mouse up commits native tool stroke");
            if(window.Session.Dirty)Undo.PerformUndo();
            Check(window.Session.ReadPng().SequenceEqual(before),"Scene View drag is one Undo operation");
            view.SendEvent(new Event{type=EventType.Layout,mousePosition=start});
            view.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=start});
            view.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=start});
            var beforeErase=Decode(window.Session.ReadPng());
            view.SendEvent(new Event{type=EventType.Layout,mousePosition=start,modifiers=EventModifiers.Shift});
            view.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=start,modifiers=EventModifiers.Shift});
            eraseUV=activeTool.LastPaintHit.uv;
            view.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=start,modifiers=EventModifiers.Shift});
            painted=Decode(window.Session.ReadPng());int eraseX=(int)(painted.width*eraseUV.x),eraseY=(int)(painted.height*eraseUV.y);
            float oldAlpha=beforeErase.GetPixel(eraseX,eraseY).a,newAlpha=painted.GetPixel(eraseX,eraseY).a;
            guiLog.Add("Shift erase alpha: "+oldAlpha+" -> "+newAlpha+" strength="+window.Strength);
            var priorPixels=beforeErase.GetPixels32();var afterPixels=painted.GetPixels32();int maxDelta=0,maxIndex=0;
            for(int i=0;i<priorPixels.Length;i++){int delta=priorPixels[i].a-afterPixels[i].a;if(delta>maxDelta){maxDelta=delta;maxIndex=i;}}
            guiLog.Add("Erase centerUV="+eraseUV+" point="+activeTool.LastPaintHit.point+" maxAlphaDelta="+maxDelta+" atUV="+new Vector2((float)(maxIndex%painted.width)/painted.width,(float)(maxIndex/painted.width)/painted.height));
            Check(oldAlpha>.1f && newAlpha<oldAlpha*.9f,"Scene View Shift shortcut erases painted coverage");
            UnityEngine.Object.DestroyImmediate(painted);UnityEngine.Object.DestroyImmediate(beforeErase);
            Undo.PerformUndo();Undo.PerformUndo();
            var prior=window.Session.ReadPng();
            view.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=start,modifiers=EventModifiers.Alt});
            view.SendEvent(new Event{type=EventType.MouseDrag,button=0,mousePosition=end,delta=end-start,modifiers=EventModifiers.Alt});
            view.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=end,modifiers=EventModifiers.Alt});
            Check(window.Session.ReadPng().SequenceEqual(prior),"Alt Scene navigation does not paint");
            SceneView.duringSceneGui-=capture;File.AppendAllText(folder+"/SceneGUI.txt","\n"+string.Join("\n",guiLog));
        }
        static void TestReload()
        {
            EditorSceneManager.OpenScene("Assets/Rendering/Moebius/Samples/Paint/MoebiusPaintDemo.unity");
            var renderer=GameObject.Find("Moebius Paint Surface").GetComponent<MeshRenderer>();var material=renderer.sharedMaterial;
            Check(material.GetFloat("_UsePaintColor")==1 && AssetDatabase.Contains(material.GetTexture("_PaintColorMap")),"New Unity process restores material's saved paint texture");
            using(var session=new MoebiusPaintSession(new MoebiusPaintMesh(renderer),0,MoebiusPaintLayer.Color,512))
            {var image=Decode(session.ReadPng());Check(image.GetPixel(image.width/2,(int)(image.height*.7f)).a>.5f,"New Unity process restores color coverage");UnityEngine.Object.DestroyImmediate(image);}
            using(var session=new MoebiusPaintSession(new MoebiusPaintMesh(renderer),0,MoebiusPaintLayer.Detail,512))
            {var image=Decode(session.ReadPng());var pixel=image.GetPixel(image.width/2,(int)(image.height*.3f));Check(Mathf.Abs(pixel.g-.25f)<.02f && pixel.r>.99f,"New Unity process restores painted control channels");UnityEngine.Object.DestroyImmediate(image);}
            var actual=Capture(Camera.main?Camera.main:UnityEngine.Object.FindFirstObjectByType<Camera>(),"SavedDemo");
            var expected=Decode(File.ReadAllBytes("Validation/Paint/After/SavedDemo.png"));Check(Same(expected.GetPixels32(),actual),"Saved scene render survives Unity restart exactly");UnityEngine.Object.DestroyImmediate(expected);
            var player=UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player);
            Check(!player.SelectMany(a=>a.sourceFiles).Any(p=>p.Replace('\\','/').Contains("/Editor/Paint/")),"Player assemblies exclude all Scene painting/editor code");
        }
        static void TestSphere()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);var mesh=sphere.GetComponent<MeshFilter>().sharedMesh;
            var allUV=mesh.uv;var min=Vector2.one*float.PositiveInfinity;var max=Vector2.one*float.NegativeInfinity;
            foreach(var value in allUV){min=Vector2.Min(min,value);max=Vector2.Max(max,value);}
            results.Add("Unity Sphere UV0: min="+min.ToString("F6")+" max="+max.ToString("F6")+" vertices="+allUV.Length);
            File.WriteAllLines(folder+"/SphereUV.txt",allUV.Select((u,i)=>i+" "+mesh.vertices[i].ToString("F6")+" UV="+u.ToString("F6")));
            UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());
            var renderer=sphere.GetComponent<MeshRenderer>();renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Moebius/Samples/Shared/Materials/Ivory.mat");
            var surface=new MoebiusPaintMesh(renderer);
            Check(surface.Raycast(new Ray(new Vector3(0,0,-2),Vector3.forward),out var hit),"Unity built-in Sphere is accepted and can be picked");
            Check(mesh.uv.SequenceEqual(allUV),"Accepting border imprecision does not modify the source mesh UVs");
            MoebiusPaintAssets.MakeUnique(renderer,0);
            var camera=new GameObject("Sphere test camera").AddComponent<Camera>();camera.GetUniversalAdditionalCameraData().SetRenderer(1);
            camera.transform.position=new Vector3(0,0,-2);camera.orthographic=true;camera.orthographicSize=.7f;camera.clearFlags=CameraClearFlags.SolidColor;
            var light=new GameObject("Sphere test light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(25,-30,0);
            var original=Capture(camera,"SphereBefore");
            surface.Raycast(new Ray(new Vector3(-2,.075f,-.0001f),Vector3.right),out var edge);
            Check(edge.uv.x<.01f,"Boundary test hits the sphere's negative-U seam side");
            using(var session=new MoebiusPaintSession(surface,0,MoebiusPaintLayer.Color,512))
            {
                session.BeginStroke();session.Dab(hit.point,hit.normal,.12f,1,.65f,Color.red,0,false);session.EndStroke();var front=session.ReadPng();
                var image=Decode(front);Check(Sample(image,hit.uv).a>.8f,"GPU color painting works on the curved sphere surface");UnityEngine.Object.DestroyImmediate(image);
                Check(!Same(original,Capture(camera,"SpherePainted")),"Sphere painting appears through the existing NPR shader");
                camera.transform.position=new Vector3(-2,.075f,-.0001f);camera.transform.rotation=Quaternion.LookRotation(Vector3.right);
                var seamBefore=Capture(camera,"SeamBefore");
                session.BeginStroke();session.Dab(edge.point,edge.normal,.12f,1,.65f,Color.cyan,0,false);session.EndStroke();var seam=session.ReadPng();
                image=Decode(seam);Check(Sample(image,edge.uv).a>.8f,"Painting reaches the clamped UV border");UnityEngine.Object.DestroyImmediate(image);
                Check(!Same(seamBefore,Capture(camera,"SeamPainted")),"Border paint is visible with the original sphere UVs in the runtime shader");
                Undo.PerformUndo();Check(session.ReadPng().SequenceEqual(front),"Undo restores the sphere's previous stroke");
                Undo.PerformRedo();Check(session.ReadPng().SequenceEqual(seam),"Redo restores the sphere's border stroke");session.Save();
            }
            using(var session=new MoebiusPaintSession(surface,0,MoebiusPaintLayer.Color,512))
            {var image=Decode(session.ReadPng());Check(Sample(image,edge.uv).a>.8f,"Saved sphere border paint reopens correctly");UnityEngine.Object.DestroyImmediate(image);}
            using(var session=new MoebiusPaintSession(surface,0,MoebiusPaintLayer.Hatching,512))
            {
                session.BeginStroke();session.Dab(edge.point,edge.normal,.12f,1,.65f,Color.white,0,false);session.EndStroke();
                var image=Decode(session.ReadPng());var pixel=Sample(image,edge.uv);
                Check(pixel.r<.2f && pixel.g>.99f && pixel.b>.99f && pixel.a>.99f,"Sphere border mask painting preserves unrelated control channels");UnityEngine.Object.DestroyImmediate(image);
            }
            var invalid=UnityEngine.Object.Instantiate(mesh);var badUV=invalid.uv;badUV[0].x=-.05f;invalid.uv=badUV;sphere.GetComponent<MeshFilter>().sharedMesh=invalid;
            bool rejected=false;try{new MoebiusPaintMesh(renderer);}catch(ArgumentException){rejected=true;}
            Check(rejected,"Substantial out-of-range UVs are still rejected");sphere.GetComponent<MeshFilter>().sharedMesh=mesh;UnityEngine.Object.DestroyImmediate(invalid);
            Selection.activeGameObject=sphere;MoebiusPaintWindow.Open();MoebiusPaintWindow.Instance.OpenSession();
            Check(MoebiusPaintWindow.Instance.SessionReady,"Selecting a built-in Sphere opens a usable paint workspace");MoebiusPaintWindow.Instance.Close();
            Check(mesh.uv.SequenceEqual(allUV),"Painting and saving preserve the original sphere mesh UVs");
        }
        static Color Sample(Texture2D image,Vector2 uv)=>image.GetPixel(Mathf.Clamp((int)(uv.x*image.width),0,image.width-1),Mathf.Clamp((int)(uv.y*image.height),0,image.height-1));
        static void TestBrush(MoebiusPaintMesh surface)
        {
            using(var session=new MoebiusPaintSession(surface,0,MoebiusPaintLayer.Color,512))
            {
                session.ClearLayer();session.BeginStroke();session.Dab(Vector3.zero,Vector3.back,.2f,.5f,1,Color.red,0,false);session.EndStroke();
                var image=Decode(session.ReadPng());var center=image.GetPixel(image.width/2,image.height/2).a;
                var edge=image.GetPixel((int)(image.width*.65f),image.height/2).a;var outside=image.GetPixel((int)(image.width*.75f),image.height/2).a;
                Check(Mathf.Abs(center-.5f)<.02f && center>edge && edge>0 && outside==0,"World-space radius, strength and smooth falloff behave correctly");UnityEngine.Object.DestroyImmediate(image);
                session.BeginStroke();session.Dab(Vector3.zero,Vector3.back,.2f,1,.5f,Color.white,0,true);session.EndStroke();
                image=Decode(session.ReadPng());Check(image.GetPixel(image.width/2,image.height/2).a==0,"Color eraser restores zero coverage");UnityEngine.Object.DestroyImmediate(image);
                var other=GameObject.CreatePrimitive(PrimitiveType.Quad);other.GetComponent<Renderer>().sharedMaterial=session.material;
                bool blocked=false;try{session.Save();}catch(InvalidOperationException){blocked=true;}
                Check(blocked,"Save is blocked if material becomes shared during painting");UnityEngine.Object.DestroyImmediate(other);
            }
        }
        static void TestSlots(MeshRenderer renderer)
        {
            var filter=renderer.GetComponent<MeshFilter>();var original=filter.sharedMesh;
            var mesh=new Mesh{vertices=new[]{new Vector3(-1,-.5f,0),new Vector3(0,-.5f,0),new Vector3(-1,.5f,0),new Vector3(0,.5f,0),new Vector3(1,-.5f,0),new Vector3(1,.5f,0)},
                uv=new[]{Vector2.zero,new Vector2(.5f,0),Vector2.up,new Vector2(.5f,1),Vector2.right,Vector2.one},subMeshCount=2};
            mesh.SetTriangles(new[]{0,2,1,1,2,3},0);mesh.SetTriangles(new[]{1,3,4,4,3,5},1);mesh.RecalculateNormals();
            filter.sharedMesh=mesh;var materials=renderer.sharedMaterials;
            var rightMaterial=new Material(materials[0]);renderer.sharedMaterials=new[]{materials[0],rightMaterial};var surface=new MoebiusPaintMesh(renderer);
            bool left=surface.Raycast(new Ray(new Vector3(-.5f,0,-2),Vector3.forward),out var a);bool right=surface.Raycast(new Ray(new Vector3(.5f,0,-2),Vector3.forward),out var b);
            Check(left && right && a.slot==0 && b.slot==1,"Picking identifies correct submesh/material slot");
            using(var session=new MoebiusPaintSession(surface,0,MoebiusPaintLayer.Hatching,512))
            {
                session.BeginStroke();session.Dab(new Vector3(-.5f,0,0),Vector3.back,3,1,.5f,Color.white,0,false);session.EndStroke();
                var image=Decode(session.ReadPng());
                Check(image.GetPixel(image.width/4,image.height/2).r<.01f && image.GetPixel(image.width*3/4,image.height/2).r>.99f,"GPU painting only writes selected submesh, even with large radius");UnityEngine.Object.DestroyImmediate(image);
            }
            renderer.sharedMaterials=materials;filter.sharedMesh=original;UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(rightMaterial);
        }
        static Texture2D Decode(byte[] png){var image=new Texture2D(2,2,TextureFormat.RGBA32,false,true);image.LoadImage(png);return image;}
        static void Check(bool ok,string name){results.Add((ok?"PASS: ":"FAIL: ")+name);if(!ok)errors.Add(name);}
        static bool Same(Color32[] a,Color32[] b)=>a.SequenceEqual(b);
        static void CompareBefore(string name,Color32[] pixels)
        {var image=Decode(File.ReadAllBytes("Validation/Paint/Before/"+name+".png"));Check(Same(image.GetPixels32(),pixels),"Shader without painting equals pre-change pixels");UnityEngine.Object.DestroyImmediate(image);}
        static Color32[] Capture(Camera camera,string name)
        {
            var target=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;camera.Render();camera.Render();
            var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(512,512,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();File.WriteAllBytes(folder+"/"+name+".png",image.EncodeToPNG());
            var pixels=image.GetPixels32();RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);return pixels;
        }
    }
}
