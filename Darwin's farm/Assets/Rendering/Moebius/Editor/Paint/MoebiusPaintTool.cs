using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Darwin.Rendering.Editor.Paint
{
    [EditorTool("Moebius Paint")]
    public sealed class MoebiusPaintTool : EditorTool
    {
        Vector2 lastMouse;
        Vector3 lastPoint;
        int captured;
        internal MoebiusPaintMesh.Hit LastPaintHit {get;private set;}
        public override GUIContent toolbarIcon=>new GUIContent(EditorGUIUtility.IconContent("d_editicon.sml").image,"Moebius Paint");
        public override void OnActivated(){MoebiusPaintWindow.Open();}
        public override void OnWillBeDeactivated(){MoebiusPaintWindow.Instance?.FinishStroke();Release();}
        void Release(){if(captured!=0 && GUIUtility.hotControl==captured)GUIUtility.hotControl=0;captured=0;}
        public override void OnToolGUI(EditorWindow window)
        {
            if(!(window is SceneView view))return;
            var settings=MoebiusPaintWindow.Instance;var e=Event.current;
            if(!settings || !settings.SessionReady){if(captured!=0){settings?.FinishStroke();Release();}return;}
            var surface=settings.Surface;
            if(e.type==EventType.MouseUp && e.button==0 && captured!=0)
            {settings.FinishStroke();Release();e.Use();return;}
            if(e.type==EventType.KeyDown && e.keyCode==KeyCode.Escape)
            {settings.FinishStroke();Release();ToolManager.RestorePreviousTool();e.Use();return;}
            if(e.alt || e.button==1 || e.button==2 || Tools.viewToolActive)
            {if(captured!=0){settings.FinishStroke();Release();}return;}
            int id=GUIUtility.GetControlID(FocusType.Passive);
            bool hit=surface.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition),out var point);
            // Only take clicks over the selected paint surface. Empty space/other objects retain Scene selection.
            if(e.type==EventType.Layout && hit && point.slot==settings.Slot)HandleUtility.AddDefaultControl(id);
            if(hit)
            {
                bool correct=point.slot==settings.Slot;
                Handles.color=correct?(settings.Erase||e.shift?new Color(1,.65f,.3f):new Color(.2f,1,.7f)):Color.gray;
                Handles.DrawWireDisc(point.point+point.normal*.002f,point.normal,settings.Size*.5f);
                Handles.DrawWireDisc(point.point+point.normal*.002f,point.normal,settings.Size*.5f*(1-settings.Falloff));
                Handles.DrawLine(point.point,point.point+point.normal*settings.Size*.2f);
                Handles.DotHandleCap(0,point.point,Quaternion.identity,HandleUtility.GetHandleSize(point.point)*.025f,EventType.Repaint);
                if(!correct)Handles.Label(point.point,"Different material slot. Select the matching Material Slot to paint here.");
            }
            if(e.type==EventType.MouseMove){view.Repaint();return;}
            if(e.type==EventType.MouseDown && e.button==0 && hit && point.slot==settings.Slot && HandleUtility.nearestControl==id)
            {
                GUIUtility.hotControl=id;captured=id;lastMouse=e.mousePosition;lastPoint=point.point;
                settings.Session.BeginStroke();Paint(settings,point,e.shift);e.Use();return;
            }
            if(e.type==EventType.MouseDrag && e.button==0 && captured!=0)
            {
                if(hit && point.slot==settings.Slot)
                {
                    float distance=Vector3.Distance(lastPoint,point.point);
                    float step=Mathf.Max(settings.Size*settings.Spacing,.001f);
                    int count=Mathf.Min(128,Mathf.FloorToInt(distance/step));
                    Vector2 start=lastMouse;Vector2 end=e.mousePosition;
                    for(int i=1;i<=count;i++)
                    {
                        Vector2 mouse=Vector2.Lerp(start,end,Mathf.Min(1,i*step/Mathf.Max(distance,.0001f)));
                        if(surface.Raycast(HandleUtility.GUIPointToWorldRay(mouse),out var sample) && sample.slot==settings.Slot)
                        {Paint(settings,sample,e.shift);lastMouse=mouse;lastPoint=sample.point;}
                    }
                }
                e.Use();view.Repaint();
            }
        }
        void Paint(MoebiusPaintWindow settings,MoebiusPaintMesh.Hit hit,bool shift)
        {LastPaintHit=hit;settings.Session.Dab(hit.point,hit.normal,settings.Size*.5f,settings.Strength,settings.Falloff,settings.BrushColor,0,settings.Erase||shift);}
    }
}
