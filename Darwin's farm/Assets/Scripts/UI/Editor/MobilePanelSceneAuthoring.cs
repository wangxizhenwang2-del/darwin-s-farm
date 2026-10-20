using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Idempotent WhiteBox upgrade: UI controls live in the scene; Play only binds them.
public static class MobilePanelSceneAuthoring
{
    private const string ScenePath = "Assets/Scenes/WhiteBox.unity";

    [MenuItem("Tools/Darwin Farm/UI/Save Mobile Panel Controls In WhiteBox", false, 310)]
    public static void Upgrade()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open WhiteBox first.");
        var ui = UnityEngine.Object.FindFirstObjectByType<MobileGameUI>(FindObjectsInactive.Include);
        var info = UnityEngine.Object.FindFirstObjectByType<GameInformationUI>(FindObjectsInactive.Include);
        if (ui == null || info == null || info.TileWindow == null || info.PopulationWindow == null)
            throw new InvalidOperationException("WhiteBox mobile UI or information boards are missing.");
        var saved = new SerializedObject(ui);
        var safe = saved.FindProperty("safeArea").objectReferenceValue as RectTransform;
        if (safe == null) throw new InvalidOperationException("WhiteBox SafeArea is missing.");
        InstallFrame(safe, "TileInformationFrame", info.TileWindow);
        InstallFrame(safe, "PopulationInformationFrame", info.PopulationWindow);
        if (info.PopulationWindow.transform.Find("PopulationResizeGrip") == null)
        {
            var grip = new GameObject("PopulationResizeGrip", typeof(RectTransform),
                typeof(Image), typeof(SnapPanelResizeHandle));
            grip.layer = info.PopulationWindow.gameObject.layer;
            var rect = (RectTransform)grip.transform;
            rect.SetParent(info.PopulationWindow.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(10f, -10f);
            rect.sizeDelta = new Vector2(46f, 46f);
            var image = grip.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>("GameUI/SpeciesJournal/ResizeGrip");
            image.color = image.sprite == null ? new Color(.25f, .34f, .22f) : Color.white;
            image.raycastPadding = new Vector4(-16f, -16f, -16f, -16f);
            image.maskable = false;
        }

        var toolScroll = saved.FindProperty("toolsScroll").objectReferenceValue as ScrollRect;
        var toolPaper = saved.FindProperty("toolsContent").objectReferenceValue as RectTransform;
        if (toolScroll == null || toolPaper == null)
            throw new InvalidOperationException("WhiteBox environment tool panel is missing.");
        Ensure<MobilePanelPinchZoom>(toolScroll.gameObject);
        Ensure<MobileLongPressClose>(toolScroll.gameObject);
        Ensure<MobileLongPressClose>(toolPaper.gameObject);
        toolScroll.horizontal = false;
        toolScroll.horizontalScrollbar = null;
        var close = toolScroll.transform.Find("ExpandTechnology");
        if (close == null) throw new InvalidOperationException("Environment close control is missing.");
        Ensure<Canvas>(close.gameObject).overrideSorting = true;
        close.GetComponent<Canvas>().sortingOrder = 100;
        Ensure<GraphicRaycaster>(close.gameObject);
        var mark = close.GetComponentInChildren<TextMeshProUGUI>(true);
        if (mark == null)
        {
            var go = new GameObject("MobileCloseMark", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(close, false);
            mark = go.GetComponent<TextMeshProUGUI>();
        }
        mark.text = "X";
        mark.font = GameUITheme.HandwrittenTMPFont;
        mark.fontSize = 36f;
        mark.alignment = TextAlignmentOptions.Center;
        mark.raycastTarget = false;
        mark.rectTransform.anchorMin = Vector2.zero;
        mark.rectTransform.anchorMax = Vector2.one;
        mark.rectTransform.offsetMin = mark.rectTransform.offsetMax = Vector2.zero;

        var build = saved.FindProperty("buildButton").objectReferenceValue as Button;
        if (build != null) Ensure<MobileLongPressClose>(build.gameObject);
        var journal = UnityEngine.Object.FindFirstObjectByType<DarwinJournalCanvas>(FindObjectsInactive.Include);
        if (journal != null)
        {
            var menu = new SerializedObject(journal).FindProperty("gameMenuButton").objectReferenceValue as GameObject;
            if (menu != null)
            {
                Ensure<Canvas>(menu).overrideSorting = true;
                menu.GetComponent<Canvas>().sortingOrder = 400;
                Ensure<GraphicRaycaster>(menu);
            }
        }
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("WhiteBox scene save failed.");
        Debug.Log("WhiteBox mobile panel masks, scrollbars, close buttons, grips and gestures are saved in the scene.");
    }

    public static void BatchUpgrade()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Upgrade();
    }

    private static void InstallFrame(RectTransform safe, string name, InformationWindow paper)
    {
        var frame = safe.Find(name) as RectTransform;
        if (frame == null) throw new InvalidOperationException(name + " is missing.");
        MobileFixedInformationFrame.InstallAuthoredParts(frame, paper);
        SetLayer(frame, frame.gameObject.layer);
        frame.Find("Close")?.gameObject.SetActive(false);
        frame.Find("MobileResizeGrip")?.gameObject.SetActive(false);
        frame.Find("VerticalScrollbar")?.gameObject.SetActive(false);
        EditorUtility.SetDirty(frame.gameObject);
    }

    private static void SetLayer(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root) SetLayer(child, layer);
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        var component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }
}
