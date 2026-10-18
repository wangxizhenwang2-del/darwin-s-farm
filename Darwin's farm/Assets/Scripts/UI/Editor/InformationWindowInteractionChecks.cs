using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

// Isolated Play Mode fixtures; does not change the authored UI or simulation state.
public static class InformationWindowInteractionChecks
{
    [MenuItem("Tools/Darwin's Farm/UI/Check information window gestures (Play Mode)")]
    private static void RunFromMenu() => Debug.Log(RunChecks());

    public static string RunChecks()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in Play Mode.");
        var root = new GameObject("WindowGestureChecks", typeof(RectTransform), typeof(Canvas));
        try
        {
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            RectTransform parent = (RectTransform)root.transform;
            RectTransform safe = Rect("SafeArea", parent, new Vector2(900, 600));
            safe.pivot = Vector2.one;
            safe.localPosition = new Vector3(450, 300, 0);
            RectTransform panel = Rect("Window", parent, new Vector2(260, 180));
            panel.gameObject.AddComponent<UnityEngine.UI.Image>();
            InformationWindow window = panel.gameObject.AddComponent<InformationWindow>();
            Canvas.ForceUpdateCanvases();
            window.Initialize(safe, Vector2.zero);
            window.Open();

            PointerEventData Pointer(int id) => new PointerEventData(EventSystem.current)
            {
                pointerId = id, button = PointerEventData.InputButton.Left,
                position = new Vector2(Screen.width / 2f, Screen.height / 2f),
                pressPosition = new Vector2(Screen.width / 2f, Screen.height / 2f),
                pointerPressRaycast = new RaycastResult { gameObject = panel.gameObject }
            };
            var mouse = Pointer(-1);
            Require(window.BeginTextDrag(mouse, out Vector2 offset), "Opening animation rejected an immediate drag.");
            Vector2 start = panel.localPosition;
            mouse.position += new Vector2(60, 40);
            window.DragText(mouse, offset);
            Require(Vector2.Distance((Vector2)panel.localPosition, start + new Vector2(60, 40)) < .1f,
                "Drag offset changed when safe bounds used a different pivot.");
            var finger = Pointer(7);
            Require(!window.BeginTextDrag(finger, out _), "A second pointer stole the window.");
            window.OnBeginDrag(finger);
            window.OnPointerUp(mouse);

            start = panel.localPosition;
            window.OnBeginDrag(finger);
            finger.position += new Vector2(-40, 20);
            window.OnDrag(finger);
            Require(Vector2.Distance((Vector2)panel.localPosition, start + new Vector2(-40, 20)) < .1f,
                "The panel background did not drag after pointer-up recovery.");
            finger.position += Vector2.one * 10000f;
            window.OnDrag(finger);
            Require(Mathf.Abs(panel.localPosition.x - (450 - 130 - 8)) < .1f &&
                Mathf.Abs(panel.localPosition.y - (300 - 90 - 8)) < .1f, "Safe-area clamping failed.");
            window.SendMessage("OnApplicationFocus", false);
            Require(window.BeginTextDrag(mouse, out _), "Focus loss left the pointer locked.");
            window.EndTextDrag(mouse);

            var text = Rect("PlainText", panel, new Vector2(100, 40));
            var handle = text.gameObject.AddComponent<InformationTextDragHandle>();
            handle.Initialize(window);
            handle.OnBeginDrag(mouse);
            handle.enabled = false;
            Require(window.BeginTextDrag(finger, out _), "Disabling a text handle left the window locked.");
            window.EndTextDrag(finger);

            // The threshold-crossing event is also the first drag event.
            mouse = Pointer(-1); mouse.position += Vector2.left * 24;
            start = panel.localPosition;
            window.OnBeginDrag(mouse); window.OnDrag(mouse); window.OnPointerUp(mouse);
            Require(panel.localPosition.x < start.x - 20, "The first movement was lost at the drag threshold.");

            var settings = new SerializedObject(window);
            settings.FindProperty("titleOnlyDragging").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            start = panel.localPosition;
            window.OnBeginDrag(mouse); mouse.position += Vector2.left * 50; window.OnDrag(mouse);
            Require(Vector2.Distance(panel.localPosition, start) < .1f, "Mobile body drag moved the window.");
            CheckNestedScroll(window, panel, mouse);
            return "Information window checks passed: animation interruption, background drag, mouse/touch ownership, " +
                "pointer-up and focus recovery, disabled handles, safe-area bounds, mobile body and nested scroll routing.";
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void CheckNestedScroll(InformationWindow window, RectTransform panel, PointerEventData pointer)
    {
        RectTransform outer = Rect("OuterScroll", panel, new Vector2(240, 150));
        var scroll = outer.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        scroll.horizontal = false; scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.viewport = outer;
        scroll.content = Rect("Content", outer, new Vector2(240, 600));
        RectTransform inner = Rect("InnerScroll", scroll.content, new Vector2(200, 80));
        var innerScroll = inner.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        innerScroll.horizontal = false; innerScroll.viewport = inner;
        innerScroll.content = Rect("ShortDescription", inner, new Vector2(200, 40));
        var handle = innerScroll.content.gameObject.AddComponent<InformationTextDragHandle>();
        handle.Initialize(window);
        Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1f;
        pointer.delta = Vector2.up * 12;
        handle.OnInitializePotentialDrag(pointer); handle.OnBeginDrag(pointer);
        Vector2 start = scroll.content.anchoredPosition;
        pointer.position += Vector2.up * 35;
        handle.OnDrag(pointer); handle.OnPointerUp(pointer);
        Require(scroll.content.anchoredPosition.y > start.y + 30,
            "A short nested description trapped the mobile body's swipe.");
    }

    private static RectTransform Rect(string name, RectTransform parent, Vector2 size)
    {
        var result = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        result.SetParent(parent, false); result.sizeDelta = size;
        return result;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
