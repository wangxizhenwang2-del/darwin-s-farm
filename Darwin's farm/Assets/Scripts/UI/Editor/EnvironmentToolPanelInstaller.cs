#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

// Rebuilds only the two existing environment-tool presentations in WhiteBox.
public static class EnvironmentToolPanelInstaller
{
    private const string Generated = "Assets/Resources/GameUI/ToolArt/Generated/";
    private const string FontPath = "Assets/Resources/GameUI/Editable/HandwrittenToolUI.asset";
    private static TMP_FontAsset handwriting;
    private static readonly Color Ink = new Color(.20f, .23f, .19f);
    private static readonly Color Muted = new Color(.40f, .43f, .36f);
    private static readonly Color Red = new Color(.74f, .25f, .20f);
    private static readonly int[] DisplayOrder = { 0, 2, 4, 3, 1 };
    private static readonly string[] CategoryFiles =
        { "temperature", "elevation", "humidity", "plant-increment", "plant-stock" };

    [MenuItem("Tools/Darwin Farm/Apply Confirmed Environment Tool Panels")]
    public static void ApplyToScene()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play mode first.");
        var desktop = Object.FindFirstObjectByType<DesktopEnvironmentTools>(FindObjectsInactive.Include);
        var mobile = Object.FindFirstObjectByType<MobileTechnologyPanel>(FindObjectsInactive.Include);
        if (desktop == null || mobile == null)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity");
            desktop = Object.FindFirstObjectByType<DesktopEnvironmentTools>(FindObjectsInactive.Include);
            mobile = Object.FindFirstObjectByType<MobileTechnologyPanel>(FindObjectsInactive.Include);
        }
        if (desktop == null || mobile == null) throw new System.InvalidOperationException("Tool panels missing in open scene.");
        var desktopState = new SerializedObject(desktop);
        var mobileState = new SerializedObject(mobile);
        if (desktopState.FindProperty("redesigned").objectReferenceValue != null &&
            mobileState.FindProperty("redesigned").objectReferenceValue != null)
        {
            handwriting = HandwrittenFont();
            ImportGeneratedSprites();
            Reflow(desktop, mobile);
            EditorSceneManager.MarkSceneDirty(desktop.gameObject.scene);
            EditorSceneManager.SaveScene(desktop.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Readable environment tool panels reflowed in both canvases.");
            return;
        }
        handwriting = HandwrittenFont();
        ImportGeneratedSprites();
        BuildDesktop(desktop);
        BuildMobile(mobile);
        Reflow(desktop, mobile);
        EditorSceneManager.MarkSceneDirty(desktop.gameObject.scene);
        EditorSceneManager.SaveScene(desktop.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Confirmed environment tool layout applied to desktop and Android canvases.");
    }

    private static void Reflow(DesktopEnvironmentTools desktop, MobileTechnologyPanel mobile)
    {
        var desktopState = new SerializedObject(desktop);
        var root = (RectTransform)desktop.transform;
        var body = ((GameObject)desktopState.FindProperty("body").objectReferenceValue).GetComponent<RectTransform>();
        var toolbar = ((GameObject)desktopState.FindProperty("toolbar").objectReferenceValue).GetComponent<RectTransform>();
        Place(root, 20, -118, EnvironmentToolPanelLayout.WideContentWidth,
            EnvironmentToolPanelLayout.WideHeight);
        var desktopContent = root.Find("ToolViewport/ToolContent") as RectTransform;
        if (desktopContent != null)
            Place(desktopContent, 0, 0, EnvironmentToolPanelLayout.WideContentWidth,
                EnvironmentToolPanelLayout.WideHeight);
        Place(toolbar, 0, 0, EnvironmentToolPanelLayout.RailWidth, 363);
        Place(body, 102, 0, EnvironmentToolPanelLayout.WideWidth,
            EnvironmentToolPanelLayout.WideHeight);
        ReflowRail(Buttons(desktopState.FindProperty("kinds")), toolbar);
        InstallScrollBlocker(toolbar);
        InstallZoom(root, body);
        InstallCancelPreview((Button)desktopState.FindProperty("cancel").objectReferenceValue, false);
        InstallScrollbars(root.GetComponent<ScrollRect>());
        EnvironmentToolPanelLayout.Apply(body, false);
        desktopState.FindProperty("zoom").objectReferenceValue = root.GetComponent<EnvironmentToolPanelZoom>();
        desktopState.ApplyModifiedPropertiesWithoutUndo();

        var mobileState = new SerializedObject(mobile);
        var mobileContent = (RectTransform)mobile.transform;
        var mobileBody = ((GameObject)mobileState.FindProperty("body").objectReferenceValue).GetComponent<RectTransform>();
        var rail = mobileContent.Find("ToolCategoryRail") as RectTransform;
        Place(mobileContent, 0, 0, EnvironmentToolPanelLayout.WideContentWidth,
            EnvironmentToolPanelLayout.WideHeight);
        Place(rail, 0, 0, EnvironmentToolPanelLayout.RailWidth, 363);
        Place(mobileBody, 102, 0, EnvironmentToolPanelLayout.WideWidth,
            EnvironmentToolPanelLayout.WideHeight);
        ReflowRail(Buttons(mobileState.FindProperty("kindButtons")), rail);
        InstallScrollBlocker(rail);
        InstallScrollBlocker(mobileContent.Find("ToolCategoryTopRow"));
        InstallZoom(mobileContent, mobileBody);
        InstallCancelPreview((Button)mobileState.FindProperty("cancelButton").objectReferenceValue, true);
        var gameUI = Object.FindFirstObjectByType<MobileGameUI>(FindObjectsInactive.Include);
        var mobileScroll = gameUI != null ?
            new SerializedObject(gameUI).FindProperty("toolsScroll").objectReferenceValue as ScrollRect : null;
        InstallScrollbars(mobileScroll);
        EnvironmentToolPanelLayout.Apply(mobileBody, false);
        var responsive = mobileContent.GetComponent<EnvironmentToolResponsiveLayout>();
        if (responsive != null) responsive.Apply(560f);
        mobileState.FindProperty("expandedContentHeight").floatValue = EnvironmentToolPanelLayout.WideHeight;
        mobileState.FindProperty("foldedContentHeight").floatValue = EnvironmentToolPanelLayout.WideHeight;
        mobileState.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ReflowRail(Button[] buttons, RectTransform rail)
    {
        if (rail == null) return;
        var art = rail.GetComponent<Image>();
        if (art != null)
        {
            art.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Generated + "ToolbarBlank.png");
            art.type = Image.Type.Simple;
            art.preserveAspect = true;
            art.raycastTarget = true;
        }
        for (int display = 0; display < DisplayOrder.Length; display++)
            Place((RectTransform)buttons[DisplayOrder[display]].transform,
                15, -17 - display * 65, 60, 60);
    }

    private static void InstallScrollBlocker(Transform rail)
    {
        if (rail != null && rail.GetComponent<EnvironmentToolRailScrollBlocker>() == null)
            rail.gameObject.AddComponent<EnvironmentToolRailScrollBlocker>();
    }

    private static void InstallCancelPreview(Button button, bool touch)
    {
        if (button == null) return;
        var trigger = button.GetComponent<EnvironmentToolCancelPreviewTrigger>();
        if (trigger == null) trigger = button.gameObject.AddComponent<EnvironmentToolCancelPreviewTrigger>();
        trigger.Configure(Object.FindFirstObjectByType<EnvironmentToolSession>(FindObjectsInactive.Include), touch);
    }

    private static void InstallZoom(RectTransform root, RectTransform body)
    {
        var grip = Rect(body, "ResizeGrip");
        Place(grip, 392, -658, 27, 27);
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Generated + "ResizeGrip.png");
        if (sprite == null) throw new System.InvalidOperationException("Resize grip sprite missing.");
        var picture = Picture(grip, sprite, Color.white);
        picture.preserveAspect = true;
        picture.raycastTarget = true;
        var zoom = root.GetComponent<EnvironmentToolPanelZoom>();
        if (zoom == null) zoom = root.gameObject.AddComponent<EnvironmentToolPanelZoom>();
        zoom.Configure(body);
        var handle = grip.GetComponent<EnvironmentToolResizeHandle>();
        if (handle == null) handle = grip.gameObject.AddComponent<EnvironmentToolResizeHandle>();
        handle.Configure(zoom);
    }

    private static void InstallScrollbars(ScrollRect scroll)
    {
        if (scroll == null) return;
        scroll.verticalScrollbar = MakeScrollbar(scroll, "ToolVerticalScrollbar", true);
        scroll.horizontalScrollbar = MakeScrollbar(scroll, "ToolHorizontalScrollbar", false);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        if (scroll.GetComponent<EnvironmentToolScrollVisibility>() == null)
            scroll.gameObject.AddComponent<EnvironmentToolScrollVisibility>();
        scroll.verticalScrollbar.gameObject.SetActive(false);
        scroll.horizontalScrollbar.gameObject.SetActive(false);
    }

    private static Scrollbar MakeScrollbar(ScrollRect scroll, string name, bool vertical)
    {
        var bar = Rect((RectTransform)scroll.transform, name);
        bar.anchorMin = vertical ? new Vector2(1, 0) : Vector2.zero;
        bar.anchorMax = vertical ? Vector2.one : new Vector2(1, 0);
        bar.pivot = vertical ? new Vector2(1, .5f) : new Vector2(.5f, 0);
        bar.anchoredPosition = vertical ? new Vector2(-3, 0) : new Vector2(0, 3);
        bar.sizeDelta = vertical ? new Vector2(10, -22) : new Vector2(-22, 10);
        var track = Picture(bar, null, new Color(.35f, .39f, .31f, .2f));
        track.raycastTarget = true;
        var sliding = Rect(bar, "SlidingArea");
        Stretch(sliding, new Vector2(1, 1), new Vector2(-1, -1));
        var handle = Rect(sliding, "Handle");
        Stretch(handle, Vector2.zero, Vector2.zero);
        var handleImage = Picture(handle, null, new Color(.28f, .42f, .31f, .86f));
        handleImage.raycastTarget = true;
        var scrollbar = bar.GetComponent<Scrollbar>();
        if (scrollbar == null) scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = vertical ? Scrollbar.Direction.BottomToTop : Scrollbar.Direction.LeftToRight;
        scrollbar.size = .35f;
        return scrollbar;
    }

    private static TMP_FontAsset HandwrittenFont()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font != null && font.atlasTextures != null && font.atlasTextures.Length > 0 &&
            font.atlasTextures[0] != null && font.material != null) return font;
        if (font != null) AssetDatabase.DeleteAsset(FontPath);
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/font/乐米拾光手札体.ttf");
        if (source == null) throw new System.InvalidOperationException("Handwritten font missing.");
        font = TMP_FontAsset.CreateFontAsset(source, 64, 6,
            GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        font.name = "乐米拾光手札体 工具面板";
        AssetDatabase.CreateAsset(font, FontPath);
        // TMP's factory creates these in memory. Persist both sub-assets so the
        // dynamic handwritten atlas survives an Editor restart and player build.
        if (font.atlasTextures != null && font.atlasTextures.Length > 0 &&
            font.atlasTextures[0] != null)
            AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
        if (font.material != null)
            AssetDatabase.AddObjectToAsset(font.material, font);
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        return font;
    }

    private static void ImportGeneratedSprites()
    {
        foreach (var name in new[] { "PanelShell", "Category-temperature", "Category-elevation",
            "Category-humidity", "Category-plant-increment", "Category-plant-stock",
            "ToolbarBlank", "ResizeGrip" })
        {
            string path = Generated + name + ".png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new System.InvalidOperationException("Missing image: " + path);
            if (importer.textureType == TextureImporterType.Sprite) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
    }

    private static void BuildDesktop(DesktopEnvironmentTools desktop)
    {
        var saved = new SerializedObject(desktop);
        var root = (RectTransform)desktop.transform;
        var body = ((GameObject)saved.FindProperty("body").objectReferenceValue).GetComponent<RectTransform>();
        var toolbar = ((GameObject)saved.FindProperty("toolbar").objectReferenceValue).GetComponent<RectTransform>();
        var options = Buttons(saved.FindProperty("options"));
        var kinds = Buttons(saved.FindProperty("kinds"));
        var exit = (Button)saved.FindProperty("exit").objectReferenceValue;
        var apply = (Button)saved.FindProperty("apply").objectReferenceValue;
        var cancel = (Button)saved.FindProperty("cancel").objectReferenceValue;

        // The root is a clipped wheel-scroll viewport only when the screen is short.
        Place(root, 20, -118, 442, 570);
        var viewport = Rect(root, "ToolViewport"); Stretch(viewport, Vector2.zero, Vector2.zero);
        var viewportImage = Picture(viewport, null, Color.clear); viewportImage.raycastTarget = true;
        if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect(viewport, "ToolContent"); Place(content, 0, 0, 442, 570);
        toolbar.SetParent(content, false); body.SetParent(content, false);
        ConfigureRail(toolbar, kinds);
        Place(toolbar, 0, -74, 80, 322);
        Place(body, 92, 0, 350, 570);
        var scroll = root.GetComponent<ScrollRect>();
        if (scroll == null) scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 40;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.verticalScrollbar = null;
        saved.FindProperty("desktopScroll").objectReferenceValue = scroll;

        var title = body.Find("Title").GetComponent<TextMeshProUGUI>();
        var view = BuildBody(body, title, exit, options, apply, cancel);
        saved.FindProperty("redesigned").objectReferenceValue = view;
        HideOld(saved, "current", "preview", "notice");
        saved.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildMobile(MobileTechnologyPanel mobile)
    {
        var saved = new SerializedObject(mobile);
        var content = (RectTransform)mobile.transform;
        var body = ((GameObject)saved.FindProperty("body").objectReferenceValue).GetComponent<RectTransform>();
        var options = Buttons(saved.FindProperty("optionButtons"));
        var kinds = Buttons(saved.FindProperty("kindButtons"));
        var close = (Button)saved.FindProperty("foldButton").objectReferenceValue;
        var apply = (Button)saved.FindProperty("deployButton").objectReferenceValue;
        var cancel = (Button)saved.FindProperty("cancelButton").objectReferenceValue;
        var rail = Rect(content, "ToolCategoryRail");
        ConfigureRail(rail, kinds);
        Place(rail, 0, -74, 80, 322);
        var topRow = Rect(content, "ToolCategoryTopRow");
        Place(topRow, 0, 0, 350, 60); topRow.gameObject.SetActive(false);
        body.SetParent(content, false); Place(body, 92, 0, 350, 570);
        close.transform.SetParent(body, false);
        var title = Label(body, "ToolTitle", "温度变化工具", 20, 30, -17, 260, 31, Ink);
        var view = BuildBody(body, title, close, options, apply, cancel);
        saved.FindProperty("redesigned").objectReferenceValue = view;
        saved.FindProperty("expandedContentHeight").floatValue = 570;
        saved.FindProperty("foldedContentHeight").floatValue = 570;
        HideOld(saved, "currentApplied", "previewText", "notice");
        saved.ApplyModifiedPropertiesWithoutUndo();

        var responsive = content.GetComponent<EnvironmentToolResponsiveLayout>();
        if (responsive == null) responsive = content.gameObject.AddComponent<EnvironmentToolResponsiveLayout>();
        responsive.Configure(body, rail, topRow, kinds);
        responsive.Apply(500f);

        var window = content.GetComponentInParent<InformationWindow>();
        if (window != null)
        {
            var background = window.GetComponent<Image>();
            if (background != null) { background.color = Color.clear; background.raycastTarget = false; }
            var windowTitle = window.transform.Find("Title");
            if (windowTitle != null) windowTitle.gameObject.SetActive(false);
            var scroll = window.GetComponentInChildren<ScrollRect>(true);
            if (scroll != null)
            {
                scroll.vertical = true; scroll.horizontal = false;
                scroll.scrollSensitivity = 40; scroll.verticalScrollbar = null;
                scroll.movementType = ScrollRect.MovementType.Clamped;
            }
        }
    }

    private static EnvironmentToolPanelView BuildBody(RectTransform body, TextMeshProUGUI title,
        Button close, Button[] options, Button apply, Button cancel)
    {
        var shell = AssetDatabase.LoadAssetAtPath<Sprite>(Generated + "PanelShell.png");
        if (shell == null) throw new System.InvalidOperationException("Generated panel sprite missing.");
        var paper = Picture(body, shell, Color.white);
        paper.type = Image.Type.Simple; paper.preserveAspect = true; paper.raycastTarget = true;
        Place(title.rectTransform, 30, -17, 260, 31);
        Font(title, 20, Ink);
        close.transform.SetParent(body, false);
        Place((RectTransform)close.transform, 302, -10, 40, 40);
        var closeLabel = close.GetComponentInChildren<TextMeshProUGUI>(true);
        if (closeLabel != null) { closeLabel.text = "×"; Font(closeLabel, 26, Ink); }
        var oldRow = options[0].transform.parent;
        if (oldRow.TryGetComponent<HorizontalLayoutGroup>(out var layout)) layout.enabled = false;
        var values = new TextMeshProUGUI[options.Length];
        var names = new TextMeshProUGUI[options.Length];
        var cards = new Image[options.Length];
        for (int i = 0; i < options.Length; i++)
        {
            var card = Rect(body, "TierCard" + i); Place(card, 29 + 75 * i, -58, 70, 98);
            cards[i] = Picture(card, null, new Color(.96f, .93f, .81f, .92f));
            var border = card.GetComponent<Outline>();
            if (border == null) border = card.gameObject.AddComponent<Outline>();
            border.effectColor = new Color(.37f, .57f, .36f); border.effectDistance = new Vector2(2, -2);
            border.enabled = i == 1;
            options[i].transform.SetParent(card, false);
            Place((RectTransform)options[i].transform, 10, -3, 50, 50);
            var buttonImage = options[i].targetGraphic as Image;
            if (buttonImage != null) { buttonImage.type = Image.Type.Simple; buttonImage.preserveAspect = true; }
            var marker = options[i].transform.Find("SelectedMarker");
            if (marker != null) marker.gameObject.SetActive(false);
            values[i] = Label(card, "Value", "+25", 15, 4, -51, 62, 20, Ink, TextAlignmentOptions.Center);
            var tag = Rect(card, "TierTag"); Place(tag, 4, -76, 62, 17);
            Picture(tag, null, i < 2 ? new Color(.86f,.83f,.68f,.8f) : new Color(.78f,.87f,.88f,.8f));
            names[i] = Label(tag, "Name", "小幅升温", 11, 0, 0, 62, 17, Ink, TextAlignmentOptions.Center);
        }
        oldRow.gameObject.SetActive(false);
        Line(body, "TopDivider", 28, -162, 294);
        Label(body, "EffectCaption", "预计效果", 12, 28, -170, 58, 20, Muted);
        var effect = Label(body, "PredictedEffect", "环境温度 +25", 14, 94, -168, 107, 22, Ink);
        var hint = Label(body, "EffectHint", "适温可能提升", 12, 199, -170, 123, 20, Red);
        Label(body, "TerrainCaption", "预计地形", 12, 28, -195, 58, 20, Muted);
        var terrain = Label(body, "PredictedTerrain", "选择地块后计算", 13, 94, -193, 228, 22, Ink);
        var chartTitle = Label(body, "ChartTitle", "温度趋势", 16, 28, -225, 110, 25, Ink);
        var defaultValue = Label(body, "DefaultValue", "等待选择地块", 11, 188, -230, 134, 18, Muted, TextAlignmentOptions.Right);
        var chartFrame = Rect(body, "ForecastChartFrame"); Place(chartFrame, 28, -256, 294, 145);
        var frameImage = Picture(chartFrame, null, new Color(.97f, .97f, .92f, .9f));
        frameImage.raycastTarget = false;
        var frameBorder = chartFrame.GetComponent<Outline>();
        if (frameBorder == null) frameBorder = chartFrame.gameObject.AddComponent<Outline>();
        frameBorder.effectColor = new Color(.55f,.59f,.51f,.55f); frameBorder.effectDistance = new Vector2(1,-1);
        var graphRect = Rect(chartFrame, "Plot"); Stretch(graphRect, Vector2.zero, Vector2.zero);
        var graph = graphRect.GetComponent<EnvironmentForecastGraphic>();
        if (graph == null) graph = graphRect.gameObject.AddComponent<EnvironmentForecastGraphic>();
        graph.raycastTarget = false;
        var annotations = Rect(chartFrame, "ForecastAnnotations"); Stretch(annotations, Vector2.zero, Vector2.zero);
        Label(annotations, "Axis100", "100", 10, 2, -32, 28, 14, Muted);
        Label(annotations, "Axis50", "50", 10, 2, -71, 28, 14, Muted);
        Label(annotations, "Axis0", "0", 10, 2, -111, 28, 14, Muted);
        Label(annotations, "Enter", "生效25日", 10, 102, -7, 56, 15, Ink, TextAlignmentOptions.Center);
        Label(annotations, "Hold", "维持150日", 10, 160, -7, 66, 15, Ink, TextAlignmentOptions.Center);
        Label(annotations, "Return", "恢复100日", 10, 225, -7, 66, 15, Ink, TextAlignmentOptions.Center);
        Label(annotations, "PastDay", "−30", 10, 29, -123, 32, 15, Muted);
        Label(annotations, "NowDay", "现在", 10, 79, -123, 35, 15, Muted);
        Label(annotations, "EnterDay", "+25", 10, 129, -123, 36, 15, Muted);
        Label(annotations, "HoldDay", "+175", 10, 211, -123, 40, 15, Muted);
        Label(annotations, "ReturnDay", "+275", 10, 254, -123, 40, 15, Muted);
        var prompt = Label(chartFrame, "EmptyPrompt", "请点击地块以继续", 19, 28, -58, 238, 26, Ink, TextAlignmentOptions.Center);
        var subtitle = Label(chartFrame, "EmptySubtitle", "选择后显示历史与预测趋势", 12, 28, -84, 238, 20, Muted, TextAlignmentOptions.Center);
        Legend(body, "HistoricalLegend", "历史/自然", 30, -409, new Color(.30f,.52f,.64f), false);
        Legend(body, "DefaultLegend", "地块默认", 174, -409, new Color(.08f,.09f,.08f), false);
        Legend(body, "ForecastLegend", "工具预测", 30, -426, Red, true);
        Line(body, "StatusDivider", 28, -450, 294);
        Label(body, "AppliedTitle", "当前已施加工具", 16, 28, -455, 200, 24, Ink);
        var first = Label(body, "AppliedLine1", "选择地块后查看", 13, 30, -478, 285, 22, Ink);
        var second = Label(body, "AppliedLine2", "", 13, 30, -501, 285, 22, Ink);
        Line(body, "ActionDivider", 28, -526, 294);
        ConfigureAction(apply, body, 118, "应用", new Color(.79f,.86f,.73f));
        ConfigureAction(cancel, body, 220, "撤销效果", new Color(.93f,.92f,.84f));

        var view = body.GetComponent<EnvironmentToolPanelView>();
        if (view == null) view = body.gameObject.AddComponent<EnvironmentToolPanelView>();
        view.Configure(title, effect, hint, terrain, defaultValue, prompt, subtitle,
            chartTitle, first, second, values, names, cards, annotations.gameObject, graph);
        return view;
    }

    private static void ConfigureRail(RectTransform rail, Button[] kinds)
    {
        var image = Picture(rail,
            AssetDatabase.LoadAssetAtPath<Sprite>(Generated + "ToolbarBlank.png"), Color.white);
        image.type = Image.Type.Simple; image.preserveAspect = true; image.raycastTarget = true;
        for (int display = 0; display < DisplayOrder.Length; display++)
        {
            int index = DisplayOrder[display];
            var button = kinds[index]; button.transform.SetParent(rail, false);
            Place((RectTransform)button.transform, 12, -19 - display * 57, 55, 55);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Generated + "Category-" + CategoryFiles[index] + ".png");
            var buttonImage = button.targetGraphic as Image;
            if (buttonImage != null)
            {
                buttonImage.sprite = sprite; buttonImage.type = Image.Type.Simple;
                buttonImage.preserveAspect = true; buttonImage.color = Color.white;
            }
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.gameObject.SetActive(false);
            if (!button.TryGetComponent<Outline>(out var outline))
                outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.31f, .55f, .33f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.enabled = false;
        }
    }
    private static void ConfigureAction(Button button, RectTransform parent, float x, string caption, Color color)
    {
        button.transform.SetParent(parent, false);
        Place((RectTransform)button.transform, x, -535, 94, 31);
        var image = button.targetGraphic as Image;
        if (image != null) { image.sprite = null; image.type = Image.Type.Simple; image.color = color; }
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) { label.gameObject.SetActive(true); label.text = caption; Font(label, 16, Ink); }
    }
    private static void Legend(RectTransform parent, string name, string value,
        float x, float y, Color color, bool dashed)
    {
        var line = Rect(parent, name + "Mark"); Place(line, x, y - 6, dashed ? 10 : 20, 2);
        Picture(line, null, color).raycastTarget = false;
        if (dashed)
        {
            var second = Rect(parent, name + "Mark2"); Place(second, x + 13, y - 6, 8, 2);
            Picture(second, null, color).raycastTarget = false;
        }
        Label(parent, name, value, 11, x + 25, y - 1, 95, 17, Muted);
    }
    private static void Line(RectTransform parent, string name, float x, float y, float width)
    {
        var rect = Rect(parent, name); Place(rect, x, y, width, 1);
        Picture(rect, null, new Color(.62f,.64f,.55f,.55f)).raycastTarget = false;
    }
    private static Button[] Buttons(SerializedProperty array)
    {
        var result = new Button[array.arraySize];
        for (int i = 0; i < result.Length; i++)
            result[i] = (Button)array.GetArrayElementAtIndex(i).objectReferenceValue;
        return result;
    }
    private static void HideOld(SerializedObject saved, params string[] names)
    {
        foreach (var name in names)
        {
            var label = saved.FindProperty(name).objectReferenceValue as TextMeshProUGUI;
            if (label != null) label.gameObject.SetActive(false);
        }
    }
    private static RectTransform Rect(Transform parent, string name)
    {
        var existing = parent.Find(name) as RectTransform;
        if (existing != null) return existing;
        var child = new GameObject(name, typeof(RectTransform));
        child.layer = LayerMask.NameToLayer("UI");
        var rect = (RectTransform)child.transform; rect.SetParent(parent, false);
        return rect;
    }
    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
        rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }
    private static void Stretch(RectTransform rect, Vector2 minimum, Vector2 maximum)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = minimum; rect.offsetMax = maximum; rect.localScale = Vector3.one;
    }
    private static Image Picture(RectTransform rect, Sprite sprite, Color color)
    {
        var image = rect.GetComponent<Image>();
        if (image == null) image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite; image.type = Image.Type.Simple; image.color = color;
        return image;
    }
    private static void Font(TextMeshProUGUI label, float size, Color color)
    {
        label.font = handwriting; label.fontSize = size; label.enableAutoSizing = false;
        label.color = color; label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }
    private static TextMeshProUGUI Label(RectTransform parent, string name, string value,
        float size, float x, float y, float width, float height, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var rect = Rect(parent, name); Place(rect, x, y, width, height);
        var label = rect.GetComponent<TextMeshProUGUI>();
        if (label == null) label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = value; label.alignment = alignment; Font(label, size, color);
        return label;
    }
}
#endif

