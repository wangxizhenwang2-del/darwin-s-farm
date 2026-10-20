using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class TileInformationSceneInstaller
{
    private const string ArtPath = "Assets/Resources/GameUI/TileInformationExtended.png";
    private const string CompactArtPath = "Assets/Resources/GameUI/TileInformationCompact.png";

    [MenuItem("Tools/Darwin Farm/UI/Install Illustrated Tile Information", false, 306)]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Exit Play mode before editing the scene.");
        var manager = Object.FindFirstObjectByType<GameInformationUI>(FindObjectsInactive.Include);
        if (manager == null) throw new System.InvalidOperationException("Open WhiteBox first.");
        ImportArt(ArtPath);
        ImportArt(CompactArtPath);

        InformationWindow window = null;
        foreach (var candidate in manager.GetComponentsInChildren<InformationWindow>(true))
            if (candidate.name == "TileInformation") { window = candidate; break; }
        if (window == null) throw new System.InvalidOperationException("TileInformation window is missing.");
        Undo.RegisterFullObjectHierarchyUndo(manager.gameObject, "Install illustrated tile information");
        var root = (RectTransform)window.transform;
        var existing = root.GetComponent<TileInformationPanel>();
        if (existing == null)
        {
            var oldPanel = root.GetComponent<TileTrendPanel>();
            if (oldPanel != null) Undo.DestroyObjectImmediate(oldPanel);
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child.name != "Close") Undo.DestroyObjectImmediate(child.gameObject);
            }
            existing = Undo.AddComponent<TileInformationPanel>(root.gameObject);
            existing.Build();
        }
        var rightAxis = root.Find("PopulationAndPlantsChart/RightAxisTitle");
        if (rightAxis != null)
        {
            var label = rightAxis.GetComponent<TextMeshProUGUI>();
            if (label != null) label.text = "植\n物\n量";
        }
        existing.EnsureCompactLayout();
        existing.ApplyStyle();
        var chartFrame = root.Find("PopulationAndPlantsChart") as RectTransform;
        if (chartFrame != null) chartFrame.anchoredPosition = new Vector2(73f, -119f);
        var title = root.Find("TileHeading") as RectTransform;
        if (title != null) title.anchoredPosition = new Vector2(88f, -58f);
        var titleRule = root.Find("TitleRule") as RectTransform;
        if (titleRule != null) titleRule.anchoredPosition = new Vector2(81f, -107f);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = new Vector2(12f, -24f);
        var close = root.Find("Close") as RectTransform;
        if (close != null)
        {
            close.anchorMin = close.anchorMax = close.pivot = Vector2.one;
            close.anchoredPosition = new Vector2(-33f, -20f);
            close.sizeDelta = new Vector2(44f, 44f);
            var image = close.GetComponent<Image>();
            if (image != null) image.color = new Color(1f, 1f, 1f, .001f);
            var oldText = close.GetComponentInChildren<Text>(true);
            if (oldText != null) oldText.enabled = false;
            var mark = close.Find("CloseMark") as RectTransform;
            if (mark == null)
            {
                var markObject = new GameObject("CloseMark", typeof(RectTransform), typeof(TextMeshProUGUI));
                mark = (RectTransform)markObject.transform;
                mark.SetParent(close, false);
            }
            mark.anchorMin = Vector2.zero;
            mark.anchorMax = Vector2.one;
            mark.offsetMin = mark.offsetMax = Vector2.zero;
            var cross = mark.GetComponent<TextMeshProUGUI>();
            cross.font = Resources.Load<TMP_FontAsset>("GameUI/SpeciesJournal/HandwrittenJournal") ??
                GameUITheme.HandwrittenTMPFont;
            cross.fontSize = 46;
            cross.color = new Color(.12f, .28f, .09f);
            cross.alignment = TextAlignmentOptions.Center;
            cross.raycastTarget = false;
            cross.text = "×";
            close.SetAsLastSibling();
        }
        var log = manager.GetComponent<TileInformationLogHistory>();
        if (log == null) Undo.AddComponent<TileInformationLogHistory>(manager.gameObject);
        var info = new SerializedObject(manager);
        info.FindProperty("tileInformationPanel").objectReferenceValue = existing;
        info.FindProperty("tileLogHistory").objectReferenceValue =
            manager.GetComponent<TileInformationLogHistory>();
        info.FindProperty("tileTrendPanel").objectReferenceValue = null;
        info.FindProperty("tileText").objectReferenceValue = null;
        info.ApplyModifiedPropertiesWithoutUndo();
        window.SetDragBounds((RectTransform)manager.transform, root.anchoredPosition);
        foreach (Component component in manager.GetComponentsInChildren<Component>(true))
            if (component != null) EditorUtility.SetDirty(component);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        EditorSceneManager.SaveScene(manager.gameObject.scene);
        Debug.Log("Illustrated tile information installed in " + manager.gameObject.scene.path);
    }

    private static void ImportArt(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new System.InvalidOperationException("Tile background image is missing: " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();
    }
}
