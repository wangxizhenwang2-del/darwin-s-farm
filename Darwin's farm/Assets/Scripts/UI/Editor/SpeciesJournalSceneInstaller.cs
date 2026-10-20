using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

// Upgrades the two saved WhiteBox presentations when invoked from the Editor menu.
// Existing authored objects are retained unless this installer is explicitly invoked.
// The existing PopulationPanel.png asset is deliberately left untouched.
public static class SpeciesJournalSceneInstaller
{
    private const string ScenePath = "Assets/Scenes/WhiteBox.unity";
    private const string ArtPath = "Assets/Resources/GameUI/SpeciesJournal/";
    private const string FontPath = ArtPath + "HandwrittenJournal.asset";
    private const string GrayPath = ArtPath + "GrayscalePortrait.mat";

    // Installation is explicit. Saving a scene or leaving Play must never restyle authored UI.

    [MenuItem("Tools/Darwin's Farm/UI/Apply species journal to open WhiteBox scene")]
    public static void InstallActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new InvalidOperationException("Open WhiteBox.unity before applying the journal.");
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        ImportArt("JournalBoard.png");
        ImportArt("SectionSticker.png");
        ImportArt("CloseSticker.png");
        ImportArt("ResizeGrip.png");
        EnsureFont();
        EnsureGrayMaterial();
        Sprite closeSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "CloseSticker.png");
        var panels = UnityEngine.Object.FindObjectsByType<PopulationInfoPanel>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int applied = 0;
        foreach (var panel in panels)
        {
            if (panel == null || panel.gameObject.scene.path != ScenePath) continue;
            bool desktop = panel.name == "PopulationInformation";
            bool mobile = panel.name == "PopulationContent";
            if (!desktop && !mobile) continue;
            var rect = (RectTransform)panel.transform;
            rect.sizeDelta = new Vector2(PopulationInfoPanel.BoardWidth, PopulationInfoPanel.BoardHeight);
            InformationWindow owner = desktop ? WrapDesktopPanel(panel) :
                panel.GetComponentInParent<InformationWindow>(true);
            var image = panel.GetComponent<Image>();
            if (image == null) image = panel.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "JournalBoard.png");
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = true;
            panel.Initialize(owner);
            panel.ShowUnavailable("点击种群查看");
            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(rect);
            EditorUtility.SetDirty(image);
            applied++;
        }
        var desktopJournal = UnityEngine.Object.FindFirstObjectByType<PopulationJournalViewport>(
            FindObjectsInactive.Include);
        if (desktopJournal != null) ConfigureInteraction(desktopJournal);
        var desktopUI = UnityEngine.Object.FindFirstObjectByType<GameInformationUI>(
            FindObjectsInactive.Include);
        if (desktopUI != null)
        {
            var serialized = new SerializedObject(desktopUI);
            var button = serialized.FindProperty("populationCloseButton").objectReferenceValue as Button;
            if (button != null)
            {
                var journalWindow = UnityEngine.Object.FindFirstObjectByType<PopulationJournalViewport>(
                    FindObjectsInactive.Include);
                if (journalWindow != null)
                {
                    button.transform.SetParent(journalWindow.transform, false);
                    serialized.FindProperty("populationWindow").objectReferenceValue =
                        journalWindow.GetComponent<InformationWindow>();
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                StyleClose(button, closeSprite, true);
            }
        }
        var mobileUI = UnityEngine.Object.FindFirstObjectByType<MobileGameUI>(
            FindObjectsInactive.Include);
        if (mobileUI != null)
        {
            var serialized = new SerializedObject(mobileUI);
            var button = serialized.FindProperty("closeButton").objectReferenceValue as Button;
            if (button != null) StyleClose(button, closeSprite, false);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save WhiteBox scene.");
        AssetDatabase.SaveAssets();
        Debug.Log("Species journal saved to desktop and mobile WhiteBox scene (" + applied + " panels).");
    }

    private static void ConfigureInteraction(PopulationJournalViewport journal)
    {
        var visible = journal.transform.Find("JournalViewport") as RectTransform;
        var board = visible != null ? visible.Find("PopulationInformation") : null;
        var owner = journal.GetComponent<InformationWindow>();
        if (board == null || owner == null) return;
        var gesture = board.GetComponent<PopulationJournalGesture>();
        if (gesture == null) gesture = board.gameObject.AddComponent<PopulationJournalGesture>();
        gesture.Initialize(owner);
        var grip = journal.transform.Find("ResizeGrip") as RectTransform;
        if (grip == null)
        {
            var gripObject = new GameObject("ResizeGrip", typeof(RectTransform), typeof(Image));
            gripObject.layer = board.gameObject.layer;
            grip = (RectTransform)gripObject.transform;
            grip.SetParent(journal.transform, false);
        }
        grip.anchorMin = grip.anchorMax = grip.pivot = new Vector2(1f, 0f);
        grip.anchoredPosition = new Vector2(19f, -19f);
        grip.sizeDelta = new Vector2(46f, 46f);
        grip.SetAsLastSibling();
        var image = grip.GetComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "ResizeGrip.png");
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = true;
        var handle = grip.GetComponent<PopulationJournalResizeHandle>();
        if (handle == null) handle = grip.gameObject.AddComponent<PopulationJournalResizeHandle>();
        handle.Initialize(journal);
        var track = journal.transform.Find("JournalScrollbar") as RectTransform;
        if (track != null)
        {
            track.offsetMin = new Vector2(-12f, 40f);
            track.offsetMax = new Vector2(0f, -4f);
        }
        EditorUtility.SetDirty(gesture);
        EditorUtility.SetDirty(journal);
        EditorUtility.SetDirty(grip);
        EditorUtility.SetDirty(handle);
    }

    private static InformationWindow WrapDesktopPanel(PopulationInfoPanel panel)
    {
        var existing = panel.GetComponentInParent<PopulationJournalViewport>(true);
        if (existing != null) return existing.GetComponent<InformationWindow>();
        var board = (RectTransform)panel.transform;
        var oldWindow = panel.GetComponent<InformationWindow>();
        var detached = UnityEngine.Object.FindFirstObjectByType<PopulationJournalViewport>(
            FindObjectsInactive.Include);
        if (detached != null && detached.gameObject.scene == panel.gameObject.scene)
        {
            var detachedViewport = detached.transform.Find("JournalViewport") as RectTransform;
            var detachedScroll = detachedViewport != null ? detachedViewport.GetComponent<ScrollRect>() : null;
            if (detachedViewport != null && detachedScroll != null)
            {
                board.SetParent(detachedViewport, false);
                board.anchorMin = board.anchorMax = board.pivot = new Vector2(0f, 1f);
                board.anchoredPosition = Vector2.zero;
                detachedScroll.content = board;
                if (oldWindow != null) UnityEngine.Object.DestroyImmediate(oldWindow);
                var detachedBar = detached.GetComponentInChildren<Scrollbar>(true);
                detached.Initialize(board, detachedViewport, detachedScroll, detachedBar);
                return detached.GetComponent<InformationWindow>();
            }
        }
        var wrapper = new GameObject("PopulationJournalWindow", typeof(RectTransform));
        wrapper.layer = panel.gameObject.layer;
        var outer = (RectTransform)wrapper.transform;
        outer.SetParent(board.parent, false);
        outer.SetSiblingIndex(board.GetSiblingIndex());
        outer.anchorMin = outer.anchorMax = outer.pivot = Vector2.one;
        outer.anchoredPosition = new Vector2(-14f, -8f);
        outer.sizeDelta = new Vector2(PopulationInfoPanel.BoardWidth, 720f);

        var viewportObject = new GameObject("JournalViewport", typeof(RectTransform),
            typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        viewportObject.layer = panel.gameObject.layer;
        var visible = (RectTransform)viewportObject.transform;
        visible.SetParent(outer, false);
        visible.anchorMin = visible.anchorMax = visible.pivot = new Vector2(0f, 1f);
        visible.anchoredPosition = Vector2.zero;
        visible.sizeDelta = outer.sizeDelta;
        var hit = viewportObject.GetComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        board.SetParent(visible, false);
        board.anchorMin = board.anchorMax = board.pivot = new Vector2(0f, 1f);
        board.anchoredPosition = Vector2.zero;
        board.localScale = Vector3.one;
        if (oldWindow != null) UnityEngine.Object.DestroyImmediate(oldWindow);
        var scroll = viewportObject.GetComponent<ScrollRect>();
        scroll.content = board;
        scroll.viewport = visible;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 36f;

        var trackObject = new GameObject("JournalScrollbar", typeof(RectTransform), typeof(Image),
            typeof(Scrollbar));
        trackObject.layer = panel.gameObject.layer;
        var track = (RectTransform)trackObject.transform;
        track.SetParent(outer, false);
        track.anchorMin = new Vector2(1f, 0f);
        track.anchorMax = track.pivot = Vector2.one;
        track.offsetMin = new Vector2(-12f, 0f);
        track.offsetMax = Vector2.zero;
        trackObject.GetComponent<Image>().color = new Color(.45f, .49f, .33f, .20f);
        var handleObject = new GameObject("PencilHandle", typeof(RectTransform), typeof(Image));
        handleObject.layer = panel.gameObject.layer;
        var handleRect = (RectTransform)handleObject.transform;
        handleRect.SetParent(track, false);
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = new Vector2(2f, 2f);
        handleRect.offsetMax = new Vector2(-2f, -2f);
        var handleImage = handleObject.GetComponent<Image>();
        handleImage.color = new Color(.39f, .44f, .31f, .75f);
        var bar = trackObject.GetComponent<Scrollbar>();
        bar.handleRect = handleRect;
        bar.targetGraphic = handleImage;
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.value = 1f;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        var newWindow = wrapper.AddComponent<InformationWindow>();
        var fitter = wrapper.AddComponent<PopulationJournalViewport>();
        fitter.Initialize(board, visible, scroll, bar);
        return newWindow;
    }

    private static void StyleClose(Button button, Sprite sprite, bool desktop)
    {
        var rect = (RectTransform)button.transform;
        if (desktop)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-30f, -94f);
            rect.sizeDelta = new Vector2(40f, 40f);
        }
        else
        {
            rect.sizeDelta = new Vector2(50f, 50f);
            var element = button.GetComponent<LayoutElement>();
            if (element != null) element.minWidth = element.preferredWidth = 50f;
        }
        var image = button.GetComponent<Image>();
        if (image == null) image = button.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
        image.type = Image.Type.Simple;
        button.targetGraphic = image;
        foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
            label.gameObject.SetActive(false);
        foreach (var label in button.GetComponentsInChildren<Text>(true))
            label.gameObject.SetActive(false);
        EditorUtility.SetDirty(button);
        EditorUtility.SetDirty(rect);
        EditorUtility.SetDirty(image);
    }

    private static void ImportArt(string file)
    {
        string path = ArtPath + file;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new IOException("Missing species journal art: " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static void EnsureFont()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font != null) return;
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/font/乐米拾光手札体.ttf");
        if (source == null) throw new IOException("Missing 乐米拾光手札体 font.");
        font = TMP_FontAsset.CreateFontAsset(source, 64, 6, GlyphRenderMode.SDFAA,
            2048, 2048, AtlasPopulationMode.Dynamic, true);
        font.name = "乐米拾光手札体 · 生物手账";
        AssetDatabase.CreateAsset(font, FontPath);
        if (font.atlasTextures != null && font.atlasTextures.Length > 0 && font.atlasTextures[0] != null)
            AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
        if (font.material != null) AssetDatabase.AddObjectToAsset(font.material, font);
        EditorUtility.SetDirty(font);
    }

    private static void EnsureGrayMaterial()
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(GrayPath) != null) return;
        AssetDatabase.ImportAsset(ArtPath + "GrayscalePortrait.shader", ImportAssetOptions.ForceUpdate);
        Shader shader = Shader.Find("UI/SpeciesJournalGrayscale");
        if (shader == null) throw new IOException("Species journal grayscale shader was not imported.");
        var material = new Material(shader) { name = "GrayscalePortrait" };
        AssetDatabase.CreateAsset(material, GrayPath);
    }
}
