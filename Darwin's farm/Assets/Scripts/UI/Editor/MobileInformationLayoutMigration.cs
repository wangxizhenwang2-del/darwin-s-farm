using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// One-time scene migration. It preserves the existing desktop-authored boards
// and the edited environment tool paper while removing their old mobile shells.
public static class MobileInformationLayoutMigration
{
    private const string ScenePath = "Assets/Scenes/WhiteBox.unity";

    [MenuItem("Tools/Darwin Farm/UI/Release Mobile Information Panels", false, 309)]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var ui = UnityEngine.Object.FindFirstObjectByType<MobileGameUI>(FindObjectsInactive.Include);
        if (ui == null) throw new InvalidOperationException("MobileGameUI is missing from WhiteBox.");
        var saved = new SerializedObject(ui);
        var safe = saved.FindProperty("safeArea").objectReferenceValue as RectTransform;
        var technology = saved.FindProperty("technologyPanel").objectReferenceValue as MobileTechnologyPanel;
        var scroll = saved.FindProperty("toolsScroll").objectReferenceValue as ScrollRect;
        var content = saved.FindProperty("toolsContent").objectReferenceValue as RectTransform;
        if (safe == null || technology == null || scroll == null || content == null)
            throw new InvalidOperationException("The authored mobile tool panel is incomplete.");

        var rail = content.Find("ToolCategoryRail") as RectTransform;
        if (rail == null) rail = safe.Find("ToolCategoryRail") as RectTransform;
        if (rail == null) throw new InvalidOperationException("ToolCategoryRail is missing.");
        rail.SetParent(safe, false);
        rail.anchorMin = rail.anchorMax = rail.pivot = Vector2.up;
        rail.anchoredPosition = new Vector2(8f, -86f);
        rail.sizeDelta = new Vector2(EnvironmentToolPanelLayout.RailWidth, 363f);
        rail.localScale = Vector3.one;

        var scrollRect = (RectTransform)scroll.transform;
        scrollRect.SetParent(safe, false);
        scrollRect.name = "EnvironmentToolScroll";
        scrollRect.anchorMin = scrollRect.anchorMax = scrollRect.pivot = Vector2.up;
        scrollRect.anchoredPosition = new Vector2(106f, -86f);
        scrollRect.sizeDelta = new Vector2(454f, 626f);
        scrollRect.localScale = Vector3.one;
        scroll.horizontal = scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.gameObject.SetActive(false);

        var techSaved = new SerializedObject(technology);
        var close = techSaved.FindProperty("foldButton").objectReferenceValue as Button;
        if (close != null)
        {
            var rect = (RectTransform)close.transform;
            rect.SetParent(scrollRect, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-20f, -20f);
            rect.sizeDelta = new Vector2(44f, 44f);
            rect.SetAsLastSibling();
            var label = close.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = "×";
                label.fontSize = 36f;
                label.color = new Color(.12f, .25f, .08f);
                label.alignment = TextAlignmentOptions.Center;
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            }
        }
        var grip = content.GetComponentInChildren<EnvironmentToolResizeHandle>(true);
        if (grip == null) grip = scroll.GetComponentInChildren<EnvironmentToolResizeHandle>(true);
        if (grip != null)
        {
            var rect = (RectTransform)grip.transform;
            rect.SetParent(scrollRect, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-20f, 20f);
            rect.sizeDelta = new Vector2(34f, 34f);
            rect.SetAsLastSibling();
        }
        var responsive = content.GetComponent<EnvironmentToolResponsiveLayout>();
        if (responsive != null) responsive.KeepDesktopPaper(true);

        DestroyChild(safe, "EnvironmentToolDrawer");
        DestroyChild(safe, "InformationDrawer");
        DestroyChild(safe, "GestureHelp");
        var actions = safe.Find("MobileActions");
        if (actions != null) DestroyChild(actions, "EnvironmentToolsButton");
        else DestroyChild(safe, "EnvironmentToolsButton");

        foreach (var key in new[] {
            "drawerRect", "viewport", "drawer", "informationBackground", "informationScroll",
            "tileContent", "populationContent", "trendPanel", "tileDetails", "populationPanel",
            "environmentTabButton", "populationTabButton", "closeButton", "titleLabel",
            "gestureHelp", "environmentToolsButton", "toolsDrawer", "toolsDrawerRect"
        })
        {
            var property = saved.FindProperty(key);
            if (property != null) property.objectReferenceValue = null;
        }
        saved.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);
        EditorUtility.SetDirty(technology);
        EditorUtility.SetDirty(scroll);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("WhiteBox save failed.");
        Debug.Log("Mobile information boards and tool rail released from the old drawers.");
    }

    private static void DestroyChild(Transform parent, string name)
    {
        if (parent == null) return;
        var child = parent.Find(name);
        if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
    }
}
