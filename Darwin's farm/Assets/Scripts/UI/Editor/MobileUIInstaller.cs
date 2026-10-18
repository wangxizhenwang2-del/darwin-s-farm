using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Creates authored scene objects once. Re-running does not replace edited art.
public static class MobileUIInstaller
{
    [MenuItem("Tools/Darwin's Farm/UI/Install editable mobile UI")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode before installing mobile UI.");
        MobileGameUI existing = Object.FindFirstObjectByType<MobileGameUI>();
        if (existing != null) { Selection.activeGameObject = existing.gameObject; return; }
        GameInformationUI information = Object.FindFirstObjectByType<GameInformationUI>();
        MapBuildModeController build = Object.FindFirstObjectByType<MapBuildModeController>();
        MapTilePlacementController placement = Object.FindFirstObjectByType<MapTilePlacementController>();
        if (information == null || build == null || placement == null)
            throw new System.InvalidOperationException("Open the existing WhiteBox scene first.");
        var root = new GameObject("MobileUICanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(root, "Install editable mobile UI");
        root.layer = LayerMask.NameToLayer("UI");
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 1f;
        var ui = root.AddComponent<MobileGameUI>();
        var saved = new SerializedObject(ui);
        Canvas.ForceUpdateCanvases();
        RectTransform safe = Rect("SafeArea", (RectTransform)root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Stretch(safe, Vector2.zero, Vector2.zero);
        Set(saved, "safeArea", safe);
        Set(saved, "information", information);
        Set(saved, "desktopCanvas", information.GetComponent<Canvas>());
        Set(saved, "desktopHud", information.transform.Find("InformationHUD").gameObject);
        Set(saved, "touchInput", Object.FindFirstObjectByType<MapTouchInput>());
        Set(saved, "buildMode", build); Set(saved, "placement", placement);
        Set(saved, "clock", Object.FindFirstObjectByType<SimulationTime>());
        Set(saved, "history", information.GetComponent<TileTrendHistory>());
        var buildSettings = new SerializedObject(build);
        Set(saved, "desktopBuildPanel", buildSettings.FindProperty("buildPanel").objectReferenceValue);

        RectTransform hud = Rect("MobileHUD", safe, Vector2.up, Vector2.up, new Vector2(0, 68), new Vector2(8, -8));
        hud.anchorMax = Vector2.one; hud.offsetMax = new Vector2(-8, -8); hud.offsetMin = new Vector2(8, -76);
        Row(hud, 8f);
        var economy = Panel("Economy", hud, new Vector2(190, 68));
        CopyArt(information.transform.Find("InformationHUD/EconomyHUD"), economy);
        Layout(economy, 190f, false);
        var economyLabel = Label("Function", economy, "生物能：未接入", 18, Vector2.zero, new Vector2(190, 68), false, true);
        economyLabel.rectTransform.offsetMin = new Vector2(64, 0);
        economyLabel.rectTransform.offsetMax = new Vector2(-8, 0);
        var day = Panel("Day", hud, new Vector2(140, 68));
        CopyArt(information.transform.Find("InformationHUD/TimeHUD"), day);
        Layout(day, 140f, false);
        Set(saved, "dayLabel", Label("Value", day, "Day 0", 22, Vector2.zero, new Vector2(140, 68), false, true));
        var speed = Button("Speed", hud, "倍速 1X", new Vector2(145, 68));
        CopyArt(information.transform.Find("InformationHUD/SpeedHUD"), (RectTransform)speed.transform);
        ConfigureSpeedLabel(speed.GetComponentInChildren<TextMeshProUGUI>());
        BindButton(saved, "speedButton", "speedLabel", speed, 145f, false);
        BindButton(saved, "pauseButton", "pauseLabel", Button("Pause", hud, "暂停模拟", new Vector2(120, 68)), 120f, false);
        BindButton(saved, "buildButton", "buildLabel", Button("BuildMode", hud, "建造地块", new Vector2(130, 68)), 130f, false);
        var help = Panel("GestureHelp", safe, new Vector2(620, 48)); help.anchoredPosition = new Vector2(8, -84);
        Label("FunctionLabel", help, "长按地块查看环境 · 点击种群查看生物 · 拖动地图 · 双指缩放", 18,
            new Vector2(8, -4), new Vector2(604, 40), false);

        RectTransform drawerRect = Panel("InformationDrawer", safe, new Vector2(520, 476));
        drawerRect.anchorMin = drawerRect.anchorMax = drawerRect.pivot = Vector2.one;
        drawerRect.anchoredPosition = new Vector2(-8, -92);
        drawerRect.gameObject.AddComponent<CanvasGroup>();
        ConfigureInformationBackground(saved, information, drawerRect);
        var drawer = drawerRect.gameObject.AddComponent<InformationWindow>();
        var drawerSettings = new SerializedObject(drawer);
        drawerSettings.FindProperty("titleOnlyDragging").boolValue = true; drawerSettings.ApplyModifiedPropertiesWithoutUndo();
        Set(saved, "drawerRect", drawerRect); Set(saved, "drawer", drawer);
        var tabs = Rect("Tabs", drawerRect, Vector2.up, Vector2.up, new Vector2(0, 50), new Vector2(8, -6));
        tabs.anchorMax = Vector2.one; tabs.offsetMin = new Vector2(8, -56); tabs.offsetMax = new Vector2(-8, -6);
        Row(tabs, 6);
        BindButton(saved, "environmentTabButton", null, Button("EnvironmentTab", tabs, "环境信息", new Vector2(190, 50)), 190);
        BindButton(saved, "populationTabButton", null, Button("PopulationTab", tabs, "生物信息", new Vector2(190, 50)), 190);
        foreach (var item in tabs.GetComponentsInChildren<UnityEngine.UI.LayoutElement>()) item.minWidth = 60f;
        var close = Button("CloseCurrent", tabs, "X 关闭", new Vector2(78, 50));
        Layout((RectTransform)close.transform, 78, false); Set(saved, "closeButton", close);
        var title = Label("TitleDragArea", drawerRect, "环境信息 · 拖动此标题可移动", 20,
            new Vector2(12, -62), new Vector2(480, 44), true);
        var titleHandle = title.gameObject.AddComponent<InformationTextDragHandle>(); titleHandle.Initialize(drawer);
        var titleSettings = new SerializedObject(titleHandle);
        titleSettings.FindProperty("titleHandle").boolValue = true; titleSettings.ApplyModifiedPropertiesWithoutUndo();
        Set(saved, "titleLabel", title);

        RectTransform scrollRoot = Rect("InformationScroll", drawerRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Stretch(scrollRoot, new Vector2(12, 12), new Vector2(-12, -116));
        var scroll = scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;
        RectTransform viewport = Rect("Viewport", scrollRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Stretch(viewport, new Vector2(20, 0), new Vector2(-20, 0));
        Image(viewport, Color.clear); viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        scroll.viewport = viewport; Set(saved, "viewport", viewport); Set(saved, "informationScroll", scroll);
        ConfigureInformationScrollbar(scrollRoot, scroll);

        RectTransform tile = Rect("EnvironmentContent", viewport, Vector2.up, Vector2.up, new Vector2(500, 690), Vector2.zero);
        var trend = tile.gameObject.AddComponent<TileTrendPanel>(); trend.Initialize();
        ConfigureTrend(tile);
        Set(saved, "trendPanel", trend); Set(saved, "tileContent", tile);
        var detailRect = Rect("TileDetails", tile, Vector2.up, Vector2.up, new Vector2(476, 220), new Vector2(12, -356));
        var details = detailRect.gameObject.AddComponent<UnityEngine.UI.Text>();
        details.font = GameUITheme.Font; details.fontSize = GameUITheme.ReadableFontSize(23); details.lineSpacing = 1.12f;
        details.color = new Color(.11f, .16f, .17f); details.raycastTarget = true;
        details.text = "地块信息（运行时绑定）\n地形 / 海拔\n温度 / 湿度\n植物库存 / 每日恢复\n种群数量 / 水域类型";
        details.gameObject.AddComponent<InformationTextDragHandle>().Initialize(drawer); Set(saved, "tileDetails", details);
        var technology = BuildTechnology(tile, drawer, Object.FindFirstObjectByType<EnvironmentTechnologyController>());
        Set(saved, "technologyPanel", technology);

        RectTransform population = Rect("PopulationContent", viewport, Vector2.up, Vector2.up, new Vector2(500, 690), Vector2.zero);
        var panel = population.gameObject.AddComponent<PopulationInfoPanel>(); panel.Initialize(drawer);
        RectTransform content = (RectTransform)population.Find("Content");
        content.anchoredPosition = new Vector2(12, -8); content.localScale = Vector3.one;
        panel.ShowUnavailable("点击种群查看生物信息");
        content.Find("SpeciesHeader/SpeciesName").GetComponent<TextMeshProUGUI>().text = "物种名称";
        content.Find("SpeciesHeader/PopulationCount").GetComponent<TextMeshProUGUI>().text = "数量：--";
        content.Find("SpeciesHeader/SpeciesDescription/Viewport/Content").GetComponent<TextMeshProUGUI>().text = "物种介绍（运行时读取物种资料）";
        string[] attributes = { "适温 --", "适湿 --", "运动 --", "繁殖 --", "体型 --" };
        for (int i = 0; i < 5; i++) content.Find("RadarChartSection/Attribute" + i).GetComponent<TextMeshProUGUI>().text = attributes[i];
        Set(saved, "populationPanel", panel); Set(saved, "populationContent", population);
        population.gameObject.SetActive(false); scroll.content = tile;

        BuildPalette(saved, safe, placement);
        saved.ApplyModifiedPropertiesWithoutUndo();
        EnvironmentToolsInstaller.ConfigureToolPanels(information, ui);
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(root.scene);
        ui.RefreshLayout();
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("Mobile UI saved. Select MobileUICanvas / Mobile Game UI and set Display Mode to Mobile to preview/edit.");
    }

    // Also used to upgrade the already authored drawer without replacing its contents.
    public static void ConfigureInformationBackground(SerializedObject saved, GameInformationUI information, RectTransform drawer)
    {
        var background = drawer.Find("InformationBackground") as RectTransform;
        if (background == null) background = Panel("InformationBackground", drawer, Vector2.zero);
        background.SetAsFirstSibling();
        Stretch(background, Vector2.zero, new Vector2(0, -56));
        var image = background.GetComponent<UnityEngine.UI.Image>();
        image.color = Color.white; image.raycastTarget = false; image.preserveAspect = false;
        var source = new SerializedObject(information);
        Sprite Picture(string key) => ((Component)source.FindProperty(key).objectReferenceValue).GetComponent<UnityEngine.UI.Image>().sprite;
        Sprite environment = Picture("tileWindow"), population = Picture("populationWindow");
        image.sprite = saved.FindProperty("informationTab").enumValueIndex == 0 ? environment : population;
        Set(saved, "informationBackground", image);
        Set(saved, "environmentBackground", environment); Set(saved, "populationBackground", population);
    }

    public static void ConfigureInformationScrollbar(RectTransform scrollRoot, UnityEngine.UI.ScrollRect scroll)
    {
        var track = scrollRoot.Find("VerticalScrollbar") as RectTransform;
        if (track == null) track = Panel("VerticalScrollbar", scrollRoot, Vector2.zero);
        track.anchorMin = new Vector2(1, 0); track.anchorMax = Vector2.one; track.pivot = Vector2.one;
        track.sizeDelta = new Vector2(14, 0); track.anchoredPosition = new Vector2(-1, 0);
        track.GetComponent<UnityEngine.UI.Image>().color = new Color(.28f, .32f, .22f, .22f);
        var slide = track.Find("SlidingArea") as RectTransform;
        if (slide == null) slide = Rect("SlidingArea", track, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Stretch(slide, new Vector2(2, 2), new Vector2(-2, -2));
        var handle = slide.Find("Handle") as RectTransform;
        if (handle == null) handle = Panel("Handle", slide, Vector2.zero);
        Stretch(handle, Vector2.zero, Vector2.zero);
        var image = handle.GetComponent<UnityEngine.UI.Image>(); image.color = new Color(.34f, .4f, .23f, .95f);
        var bar = track.GetComponent<UnityEngine.UI.Scrollbar>();
        if (bar == null) bar = track.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
        bar.handleRect = handle; bar.targetGraphic = image; bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.Permanent;
    }

    private static MobileTechnologyPanel BuildTechnology(RectTransform tile, InformationWindow drawer, EnvironmentTechnologyController controller)
    {
        var root = Rect("TechnologySection", tile, Vector2.up, Vector2.up, new Vector2(476, 448), new Vector2(12, -614));
        var panel = root.gameObject.AddComponent<MobileTechnologyPanel>(); var saved = new SerializedObject(panel);
        var fold = Button("ExpandTechnology", root, "地块科技 ▼ 展开", new Vector2(476, 48));
        Set(saved, "foldButton", fold); Set(saved, "foldLabel", fold.GetComponentInChildren<TextMeshProUGUI>());
        Set(saved, "tileContent", tile); Set(saved, "controller", controller);
        var body = Rect("TechnologyBody", root, Vector2.up, Vector2.up, new Vector2(476, 394), new Vector2(0, -56));
        Set(saved, "body", body.gameObject);
        var kindRow = Rect("TechnologyKinds", body, Vector2.up, Vector2.up, new Vector2(476, 48), Vector2.zero);
        Row(kindRow, 5); string[] kinds = { "温度", "海拔", "湿度", "植物增量", "植物存量" };
        var kindButtons = new UnityEngine.UI.Button[kinds.Length];
        for (int i = 0; i < kinds.Length; i++) { kindButtons[i] = Button("Kind" + i, kindRow, kinds[i], new Vector2(88, 48)); Layout((RectTransform)kindButtons[i].transform, 88); }
        Array(saved, "kindButtons", kindButtons);
        var optionRow = Rect("EffectOptions", body, Vector2.up, Vector2.up, new Vector2(476, 48), new Vector2(0, -56));
        Row(optionRow, 5); string[] options = { "高2档", "高1档", "低1档", "低2档" };
        var optionButtons = new UnityEngine.UI.Button[4];
        for (int i = 0; i < 4; i++) { optionButtons[i] = Button("Option" + i, optionRow, options[i], new Vector2(110, 48)); Layout((RectTransform)optionButtons[i].transform, 110); }
        Array(saved, "optionButtons", optionButtons);
        Set(saved, "currentApplied", Label("CurrentTechnology", body, "当前科技：运行时读取", 18, new Vector2(0, -112), new Vector2(476, 48), true));
        Set(saved, "previewText", Label("EffectPreview", body, "选择科技与档位查看效果、持续时间和费用", 18, new Vector2(0, -164), new Vector2(476, 160), true));
        foreach (var text in body.GetComponentsInChildren<TextMeshProUGUI>())
            if (text.raycastTarget) text.gameObject.AddComponent<InformationTextDragHandle>().Initialize(drawer);
        var actions = Rect("DeployAndCancel", body, Vector2.up, Vector2.up, new Vector2(476, 54), new Vector2(0, -332));
        Row(actions, 8);
        var deploy = Button("Deploy", actions, "投放科技", new Vector2(142, 54)); Layout((RectTransform)deploy.transform, 142); Set(saved, "deployButton", deploy);
        var cancel = Button("CancelTechnology", actions, "撤销科技", new Vector2(142, 54)); Layout((RectTransform)cancel.transform, 142); Set(saved, "cancelButton", cancel);
        var notice = Label("ResultNotice", actions, "操作结果", 18, Vector2.zero, new Vector2(176, 54), false);
        Layout(notice.rectTransform, 176); Set(saved, "notice", notice);
        saved.ApplyModifiedPropertiesWithoutUndo(); panel.ApplyFold();
        return panel;
    }

    private static void BuildPalette(SerializedObject saved, RectTransform safe, MapTilePlacementController placement)
    {
        var palette = Panel("BuildPalette", safe, new Vector2(0, 132));
        palette.anchorMin = Vector2.zero; palette.anchorMax = Vector2.right; palette.pivot = Vector2.zero;
        palette.offsetMin = new Vector2(8, 8); palette.offsetMax = new Vector2(-8, 140);
        Set(saved, "buildPalette", palette.gameObject);
        Label("Function", palette, "左右滑动选地块 → 点击地图定位预览 → 旋转 → 确认", 19, new Vector2(12, -8), new Vector2(1080, 32), false);
        var scrollRoot = Rect("TileList", palette, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Stretch(scrollRoot, new Vector2(12, 8), new Vector2(-12, -42));
        var scroll = scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>(); scroll.horizontal = true; scroll.vertical = false;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        var viewport = Rect("Viewport", scrollRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Stretch(viewport, Vector2.zero, Vector2.zero); Image(viewport, Color.clear); viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var content = Rect("Content", viewport, Vector2.up, Vector2.up, new Vector2(0, 78), Vector2.zero);
        Row(content, 8); var fit = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fit.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content; scroll.viewport = viewport;
        var oldPalette = Object.FindFirstObjectByType<TilePaletteUI>(FindObjectsInactive.Include);
        var definitionsProperty = new SerializedObject(oldPalette).FindProperty("definitions");
        var definitions = new MapTileDefinition[definitionsProperty.arraySize];
        var buttons = new UnityEngine.UI.Button[definitions.Length];
        for (int i = 0; i < definitions.Length; i++)
        {
            definitions[i] = (MapTileDefinition)definitionsProperty.GetArrayElementAtIndex(i).objectReferenceValue;
            buttons[i] = Button("TileChoice_" + i, content, definitions[i].displayName, new Vector2(140, 78));
            Layout((RectTransform)buttons[i].transform, 140, false);
        }
        Array(saved, "tileDefinitions", definitions); Array(saved, "tileButtons", buttons);
        var controls = Panel("PlacementControls", safe, new Vector2(600, 98));
        controls.anchorMin = controls.anchorMax = controls.pivot = Vector2.zero; controls.anchoredPosition = new Vector2(8, 148);
        Set(saved, "placementControls", controls.gameObject);
        Set(saved, "placementLabel", Label("PreviewStatus", controls, "选地块后，点击地图定位预览", 18, new Vector2(12, -6), new Vector2(576, 28), false));
        var actions = Rect("Actions", controls, Vector2.up, Vector2.up, new Vector2(576, 54), new Vector2(12, -38)); Row(actions, 8);
        BindButton(saved, "rotateButton", null, Button("RotatePreview", actions, "旋转 90°", new Vector2(180, 54)), 180);
        BindButton(saved, "confirmButton", null, Button("ConfirmPlacement", actions, "确认放置", new Vector2(180, 54)), 180);
        BindButton(saved, "cancelButton", null, Button("CancelPlacement", actions, "取消预览", new Vector2(180, 54)), 180);
    }

    private static void ConfigureTrend(RectTransform tile)
    {
        var root = (RectTransform)tile.Find("TileTrend"); root.anchoredPosition = new Vector2(12, -12); root.sizeDelta = new Vector2(476, 320);
        ((RectTransform)root.Find("Chart")).sizeDelta = new Vector2(348, 188);
        foreach (string name in new[] { "PlantMaximum", "PlantMinimum" }) ((RectTransform)root.Find(name)).anchoredPosition = new Vector2(411, name == "PlantMaximum" ? -42 : -223);
        ((RectTransform)root.Find("LastDay")).anchoredPosition = new Vector2(308, -242);
        ((RectTransform)root.Find("Title")).sizeDelta = new Vector2(476, 32);
        ((RectTransform)root.Find("Legend")).sizeDelta = new Vector2(476, 46);
        ((RectTransform)root.Find("Status")).sizeDelta = new Vector2(326, 64);
        tile.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 690);
    }
    // Center the visible glyphs instead of the font's ascender/descender line box.
    public static void ConfigureSpeedLabel(TextMeshProUGUI label)
    {
        // Image.preserveAspect uses the parent pivot to position its drawing rectangle.
        ((RectTransform)label.transform.parent).pivot = new Vector2(.5f, .5f);
        label.alignment = TextAlignmentOptions.Midline;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.margin = Vector4.zero;
        label.rectTransform.pivot = new Vector2(.5f, .5f);
        Stretch(label.rectTransform, new Vector2(4f, 0f), new Vector2(-4f, 0f));
    }

    private static void Set(SerializedObject settings, string name, Object value) => settings.FindProperty(name).objectReferenceValue = value;
    private static void Array(SerializedObject settings, string name, Object[] values)
    { var array = settings.FindProperty(name); array.arraySize = values.Length; for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
    private static void BindButton(SerializedObject saved, string field, string labelField, UnityEngine.UI.Button button, float width, bool flexible = true)
    { Set(saved, field, button); Layout((RectTransform)button.transform, width, flexible); if (labelField != null) Set(saved, labelField, button.GetComponentInChildren<TextMeshProUGUI>()); }
    private static void CopyArt(Transform source, RectTransform target)
    { if (source == null) return; var image = source.GetComponent<UnityEngine.UI.Image>(); if (image != null) { var copy = target.GetComponent<UnityEngine.UI.Image>(); copy.sprite = image.sprite; copy.preserveAspect = true; copy.color = image.color; } }
    private static RectTransform Panel(string name, RectTransform parent, Vector2 size)
    { var rect = Rect(name, parent, Vector2.up, Vector2.up, size, Vector2.zero); Image(rect, new Color(.94f, .97f, .9f, .97f)); return rect; }
    private static void Image(RectTransform rect, Color color)
    { var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = true; }
    private static UnityEngine.UI.Button Button(string name, RectTransform parent, string caption, Vector2 size)
    { var rect = Panel(name, parent, size); var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = rect.GetComponent<UnityEngine.UI.Image>(); var label = Label("FunctionLabel", rect, caption, 20, Vector2.zero, size, false, true); label.rectTransform.offsetMin = new Vector2(4, 0); label.rectTransform.offsetMax = new Vector2(-4, 0); return button; }
    private static TextMeshProUGUI Label(string name, RectTransform parent, string text, float size, Vector2 position, Vector2 dimensions, bool raycast, bool centered = false)
    { var rect = Rect(name, parent, Vector2.up, Vector2.up, dimensions, position); var label = rect.gameObject.AddComponent<TextMeshProUGUI>(); label.font = GameUITheme.TMPFont; label.fontSize = GameUITheme.ReadableFontSize(size); label.text = text; label.color = new Color(.11f, .16f, .17f); label.raycastTarget = raycast; label.textWrappingMode = TextWrappingModes.Normal; if (centered) { label.alignment = TextAlignmentOptions.Center; Stretch(rect, Vector2.zero, Vector2.zero); } return label; }
    private static RectTransform Rect(string name, RectTransform parent, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
    { var go = new GameObject(name, typeof(RectTransform)); go.layer = LayerMask.NameToLayer("UI"); var rect = (RectTransform)go.transform; rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.sizeDelta = size; rect.anchoredPosition = position; return rect; }
    private static void Stretch(RectTransform rect, Vector2 minimum, Vector2 maximum)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = minimum; rect.offsetMax = maximum; }
    private static void Row(RectTransform rect, float spacing)
    { var layout = rect.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>(); layout.spacing = spacing; layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = true; layout.childForceExpandWidth = false; }
    private static void Layout(RectTransform rect, float width, bool flexible = true)
    { var layout = rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>(); layout.minWidth = layout.preferredWidth = width; layout.flexibleWidth = flexible ? 1 : 0; }
}
