using System;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Darwin.Rendering.Editor.Paint
{
    public sealed class MoebiusPaintWindow : EditorWindow
    {
        public static MoebiusPaintWindow Instance {get;private set;}
        public MoebiusPaintSession Session {get;private set;}
        public MoebiusPaintMesh Surface {get;private set;}
        public int Slot {get;private set;}
        public bool SessionReady=>Session!=null && Session.TargetValid && Surface!=null && Surface.renderer && !EditorApplication.isPlaying;
        public float Size=.4f,Strength=.2f,Falloff=.85f,Spacing=.1f;
        public Color BrushColor=new Color(.85f,.45f,.65f);
        public bool Erase;
        MeshRenderer target;
        int resolution=1024;
        string message;
        Vector2 scroll;
        bool busy;
        [MenuItem("Tools/Moebius/Paint")]
        public static void Open(){GetWindow<MoebiusPaintWindow>("Moebius Paint").Show();}
        void OnEnable()
        {
            Instance=this;minSize=new Vector2(310,510);Selection.selectionChanged+=SelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload+=AutoSave;EditorApplication.quitting+=AutoSave;
            EditorSceneManager.sceneSaving+=SceneSaving;EditorApplication.playModeStateChanged+=PlayModeChanged;
            SelectionChanged();
        }
        void OnDisable()
        {
            AutoSave();Session?.Dispose();Session=null;
            Selection.selectionChanged-=SelectionChanged;AssemblyReloadEvents.beforeAssemblyReload-=AutoSave;
            EditorApplication.quitting-=AutoSave;EditorSceneManager.sceneSaving-=SceneSaving;
            EditorApplication.playModeStateChanged-=PlayModeChanged;
            if((ToolManager.activeToolType == typeof(MoebiusPaintTool)))ToolManager.RestorePreviousTool();
            if(Instance==this)Instance=null;
        }
        void SceneSaving(Scene scene,string path){if(target && target.gameObject.scene==scene)AutoSave();}
        void PlayModeChanged(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingEditMode){AutoSave();Session?.Dispose();Session=null;}}
        void AutoSave()
        {
            if(busy || Session==null)return;
            busy=true;
            try{FinishStroke();if(Session.Dirty)Session.Save();}
            catch(Exception exception)
            {
                try
                {
                    var recovery=Session.SaveRecovery();
                    Debug.LogWarning("Moebius Paint could not save to the material: "+exception.Message+" Paint was backed up to: "+AssetDatabase.GetAssetPath(recovery));
                }
                catch(Exception recoveryError){Debug.LogError("Moebius Paint could not autosave or create a recovery texture: "+recoveryError.Message);}
            }
            finally{busy=false;}
        }
        public void FinishStroke(){Session?.EndStroke();Repaint();}
        public void OpenSession()
        {
            if(Session==null && Surface!=null)Session=new MoebiusPaintSession(Surface,Slot,MoebiusPaintLayer.Color,resolution);
        }
        bool CloseSession(bool ask)
        {
            if(Session==null)return true;FinishStroke();
            if(ask && Session.Dirty)
            {
                int choice=EditorUtility.DisplayDialogComplex("Moebius Paint","You have unsaved paint changes.","Save","Cancel","Discard");
                if(choice==1)return false;if(choice==0)Session.Save();
            }
            Session.Dispose();Session=null;return true;
        }
        void SelectionChanged()
        {
            if(busy)return;
            var next=Selection.activeGameObject?Selection.activeGameObject.GetComponent<MeshRenderer>():null;
            if(next==target && (!next || Surface!=null))return;
            try
            {
                if(!CloseSession(true)){busy=true;Selection.activeGameObject=target?target.gameObject:null;busy=false;return;}
                target=next;Slot=0;Surface=null;message=null;
                if(target)Surface=new MoebiusPaintMesh(target);
                else if(Selection.activeGameObject)message="Select an object with a MeshFilter and MeshRenderer. Skinned meshes are not supported.";
            }
            catch(Exception exception){message=exception.Message;}
            Repaint();SceneView.RepaintAll();
        }
        void OnGUI()
        {
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("MOEBIUS PAINT",EditorStyles.boldLabel);
            if(EditorApplication.isPlaying){EditorGUILayout.HelpBox("Painting is available in Edit Mode only.",MessageType.Info);EditorGUILayout.EndScrollView();return;}
            EditorGUILayout.Space();EditorGUILayout.LabelField("Target",EditorStyles.boldLabel);
            using(new EditorGUI.DisabledScope(true))EditorGUILayout.ObjectField("Object",target,typeof(MeshRenderer),true);
            if(!target || Surface==null)
            {EditorGUILayout.HelpBox(message??"Select a mesh that uses a Moebius NPR material in the Hierarchy.",MessageType.Info);EditorGUILayout.EndScrollView();return;}
            var materials=target.sharedMaterials;var names=new string[materials.Length];
            if(Session!=null && !Session.TargetValid)
                EditorGUILayout.HelpBox("The mesh or material binding has changed. Restore the original unique binding to continue. Closing this window saves unsaved paint to a recovery texture.",MessageType.Warning);
            for(int i=0;i<names.Length;i++)names[i]="Element "+i+" — "+(materials[i]?materials[i].name:"None");
            if(materials.Length==0){EditorGUILayout.HelpBox("This object has no materials.",MessageType.Warning);EditorGUILayout.EndScrollView();return;}
            int nextSlot=EditorGUILayout.Popup("Material Slot",Mathf.Clamp(Slot,0,names.Length-1),names);
            if(nextSlot!=Slot && CloseSession(true))Slot=nextSlot;
            if(Slot>=materials.Length){EditorGUILayout.EndScrollView();return;}
            var material=materials[Slot];
            if(!MoebiusPaintAssets.IsMoebius(material))
            {EditorGUILayout.HelpBox("The selected material slot must use Darwin/Moebius NPR.",MessageType.Warning);EditorGUILayout.EndScrollView();return;}
            int users=MoebiusPaintAssets.MaterialUsers(material);
            if(!MoebiusPaintAssets.CanEdit(material))
            {
                if(Session!=null)
                {
                    EditorGUILayout.HelpBox("The working material is now shared. Paint is preserved in this session. Restore the original unique material before saving or painting.",MessageType.Warning);
                    EditorGUILayout.EndScrollView();return;
                }
                EditorGUILayout.HelpBox(users>1?"This material is shared. Create a unique paint material for this object first.":"Create a unique paint material for this object to protect the original material.",MessageType.Info);
                if(GUILayout.Button("Make Material Unique For This Object"))Run(()=>{MoebiusPaintAssets.MakeUnique(target,Slot);});
                EditorGUILayout.EndScrollView();return;
            }
            EditorGUILayout.LabelField("Paint Target","Albedo Color");
            if(Session==null)
                resolution=EditorGUILayout.IntPopup(new GUIContent("Resolution","Resolution for a new texture. Existing paint textures keep their size."),resolution,new[]{new GUIContent("512"),new GUIContent("1024"),new GUIContent("2048")},new[]{512,1024,2048});
            else EditorGUILayout.LabelField("Texture Size",Session.Current.width+" x "+Session.Current.height);
            if(Session==null)
            {
                if(GUILayout.Button("Create / Open Paint Texture"))Run(OpenSession);
            }
            EditorGUILayout.Space();EditorGUILayout.LabelField("Brush",EditorStyles.boldLabel);
            Size=Mathf.Max(.005f,EditorGUILayout.FloatField(new GUIContent("Size (world diameter)","Brush diameter in world units."),Size));
            Strength=EditorGUILayout.Slider(new GUIContent("Strength","Amount applied per stamp. Lower values build up gradually."),Strength,0,1);
            Falloff=EditorGUILayout.Slider(new GUIContent("Falloff","Width of the soft edge. Higher values create a softer brush."),Falloff,.01f,1);
            Spacing=EditorGUILayout.Slider(new GUIContent("Spacing","Distance between stamps as a fraction of the brush diameter. Lower values create smoother strokes."),Spacing,.02f,.5f);
            BrushColor=EditorGUILayout.ColorField(new GUIContent("Color","Albedo paint color. Overlap soft strokes of different colors to paint a gradient."),BrushColor,true,false,false);
            Erase=EditorGUILayout.Toggle("Erase (also Shift)",Erase);
            using(new EditorGUI.DisabledScope(!SessionReady))
            {
                if(GUILayout.Button((ToolManager.activeToolType == typeof(MoebiusPaintTool))?"Paint Tool Active — Esc to exit":"Activate Scene Paint Tool"))ToolManager.SetActiveTool<MoebiusPaintTool>();
                EditorGUILayout.Space();EditorGUILayout.LabelField("Actions",EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                if(GUILayout.Button("Save"))Run(()=>Session.Save());
                if(GUILayout.Button("Revert"))Run(()=>Session.Revert());
                if(GUILayout.Button("Clear Color"))Run(()=>Session.ClearLayer());
                EditorGUILayout.EndHorizontal();
            }
            if(Session!=null)EditorGUILayout.LabelField(Session.Dirty?"Working Paint - Unsaved Changes":"Saved Paint",EditorStyles.miniLabel);
            EditorGUILayout.HelpBox("Left-drag to paint. Hold Shift to erase. Alt, middle and right mouse retain Scene navigation. Ctrl+Z and Ctrl+Y undo and redo a stroke.",MessageType.Info);
            EditorGUILayout.HelpBox(Surface.Warning,MessageType.None);
            EditorGUILayout.HelpBox("Paint albedo gradients by overlapping colors with low Strength and high Falloff. Lighting, shadows, hatching and highlights remain controlled by the material and lights.",MessageType.None);
            if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.Warning);
            EditorGUILayout.EndScrollView();
        }
        void Run(Action action){try{action();message=null;}catch(Exception exception){message=exception.Message;}Repaint();SceneView.RepaintAll();}
    }
}
