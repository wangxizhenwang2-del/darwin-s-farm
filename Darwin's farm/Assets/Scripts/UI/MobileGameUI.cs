using TMPro;
using UnityEngine;

// Saved mobile presentation. No UI objects are created at runtime.
[ExecuteAlways, DefaultExecutionOrder(100), DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public sealed class MobileGameUI : MonoBehaviour
{
    public enum DisplayMode { Auto, Desktop, Mobile }
    public enum InformationTab { Environment, Population }
    [Header("Preview in Edit Mode; Auto selects Android or touch simulation at runtime")]
    [SerializeField] private DisplayMode displayMode;
    [SerializeField] private InformationTab informationTab;
    [Header("Existing controllers")]
    [SerializeField] private GameInformationUI information;
    [SerializeField] private MapTouchInput touchInput;
    [SerializeField] private MapBuildModeController buildMode;
    [SerializeField] private MapTilePlacementController placement;
    [SerializeField] private SimulationTime clock;
    [SerializeField] private TileTrendHistory history;
    [Header("Saved desktop presentation")]
    [SerializeField] private Canvas desktopCanvas;
    [SerializeField] private GameObject desktopHud, desktopBuildPanel;
    [Header("Saved mobile hierarchy")]
    [SerializeField] private RectTransform safeArea, drawerRect, viewport;
    [SerializeField] private InformationWindow drawer;
    [SerializeField] private UnityEngine.UI.Image informationBackground;
    [SerializeField] private Sprite environmentBackground, populationBackground;
    [SerializeField] private UnityEngine.UI.ScrollRect informationScroll;
    [SerializeField] private RectTransform tileContent, populationContent;
    [SerializeField] private TileTrendPanel trendPanel;
    [SerializeField] private UnityEngine.UI.Text tileDetails;
    [SerializeField] private PopulationInfoPanel populationPanel;
    [SerializeField] private MobileTechnologyPanel technologyPanel;
    [Header("Independent environment tool window")]
    [SerializeField] private InformationWindow toolsDrawer;
    [SerializeField] private RectTransform toolsDrawerRect, toolsViewport, toolsContent;
    [SerializeField] private UnityEngine.UI.ScrollRect toolsScroll;
    [SerializeField] private GameObject buildPalette, placementControls;
    [SerializeField] private UnityEngine.UI.Button buildButton, pauseButton, speedButton;
    [SerializeField] private UnityEngine.UI.Button speciesMapButton;
    [SerializeField] private SpeciesMapsUI speciesMaps;
    [SerializeField] private UnityEngine.UI.Button environmentTabButton, populationTabButton, closeButton;
    [SerializeField] private UnityEngine.UI.Button rotateButton, confirmButton, cancelButton;
    [SerializeField] private UnityEngine.UI.Button[] tileButtons;
    [SerializeField] private MapTileDefinition[] tileDefinitions;
    [SerializeField] private TextMeshProUGUI dayLabel, pauseLabel, speedLabel, buildLabel, titleLabel, placementLabel;
    [SerializeField] private TextMeshProUGUI bioenergyLabel;
    [Header("Mobile layout (Canvas units)")]
    [SerializeField] private RectTransform statusBar, actionColumn, gestureHelp;
    [SerializeField, Min(320f)] private float maximumDrawerWidth = 520f;
    [SerializeField, Range(.3f, .6f)] private float drawerWidthFraction = .43f;
    [SerializeField] private Vector2 drawerDefaultPosition = new Vector2(-8f, -92f);
    private Vector2 ToolsDrawerHome => drawerDefaultPosition + new Vector2(-48f, -48f);
    [SerializeField, Min(100f)] private float drawerBottomClearance = 152f;
    [SerializeField, Min(.05f)] private float refreshSeconds = .25f;
    [Tooltip("Editor preview only: left, top, right, bottom in 720-height Canvas units")]
    [SerializeField] private Vector4 editorSafeInsets;

    private Canvas mobileCanvas;
    private UnityEngine.UI.GraphicRaycaster raycaster;
    private bool applied, mobile, bound;
    private Rect lastSafeArea;
    private Vector2 lastScreen;
    private float nextRefresh;
    private InformationTab lastTab;
    private readonly float[] scrollPositions = { 1f, 1f };
    [SerializeField] private UnityEngine.UI.Button environmentToolsButton;
    private EnvironmentToolSession toolSession;
    private MapGridManager grid;
    private SimulationEnvironmentController environment;
    private BioenergyAccount bioenergy;
    private bool previousToolMode;
    private bool populationCompact;
    private float populationDrawerScale = 1f, populationResizeStartScale = 1f;
    private bool populationResizeSnapped;
    private RectTransform populationResize;
    private RectTransform fixedToolRail;
    private Transform desktopTileParent, desktopPopulationParent;
    private Vector2 desktopTilePosition, desktopPopulationPosition;
    private bool informationPanelsAttached;

    public bool IsMobileActive => isActiveAndEnabled && mobile;
    public DisplayMode Mode
    {
        get => displayMode;
        set
        {
            displayMode = value; applied = false;
#if UNITY_EDITOR
            QueueEditorPreview();
#endif
        }
    }
    private bool ResolveMobile => displayMode == DisplayMode.Mobile ||
        (displayMode == DisplayMode.Auto && Application.isPlaying &&
         (Application.isMobilePlatform || (touchInput != null && touchInput.IsEditorTouchRequested)));

    private void OnEnable()
    {
        mobileCanvas = GetComponent<Canvas>();
        raycaster = GetComponent<UnityEngine.UI.GraphicRaycaster>();
        applied = false;
        if (Application.isPlaying && bound && information != null)
            information.SelectionChanged += OnSelectionChanged;
    }
#if UNITY_EDITOR
    private void OnValidate() { lastScreen = Vector2.zero; QueueEditorPreview(); }
    private void QueueEditorPreview()
    {
        UnityEditor.EditorApplication.update -= RefreshEditorPreview;
        UnityEditor.EditorApplication.update += RefreshEditorPreview;
    }
    private void RefreshEditorPreview()
    {
        UnityEditor.EditorApplication.update -= RefreshEditorPreview;
        if (this == null || !isActiveAndEnabled || Application.isPlaying || safeArea == null) return;
        mobileCanvas = GetComponent<Canvas>();
        raycaster = GetComponent<UnityEngine.UI.GraphicRaycaster>();
        applied = false;
        ApplyMode();
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
    }
#endif
    private void Start()
    {
        if (safeArea == null) return;
        if (Application.isPlaying) BindRuntime();
        ApplyMode();
    }
    private void BindRuntime()
    {
        if (bound || safeArea == null || information == null) return;
        bound = true;
        Canvas.ForceUpdateCanvases();
        PrepareToolSurface();
        technologyPanel.Initialize();
        toolSession = information.GetComponent<EnvironmentToolSession>();
        grid = FindFirstObjectByType<MapGridManager>();
        environment = grid != null ? grid.GetComponent<SimulationEnvironmentController>() : null;
        bioenergy = FindFirstObjectByType<BioenergyAccount>();
        if (bioenergyLabel == null)
        {
            Transform label = statusBar != null ? statusBar.Find("Economy/Function") : null;
            if (label != null) bioenergyLabel = label.GetComponent<TextMeshProUGUI>();
        }
        buildButton.onClick.AddListener(ToggleBuild);
        if (speciesMapButton != null && speciesMaps != null) speciesMapButton.onClick.AddListener(speciesMaps.Toggle);
        speedButton.onClick.AddListener(CycleMobileSimulationSpeed);
        rotateButton.onClick.AddListener(() => placement.RotateTouchPreview());
        confirmButton.onClick.AddListener(() => placement.ConfirmTouchPlacement());
        cancelButton.onClick.AddListener(() => placement.CancelDrag());
        for (int i = 0; i < tileButtons.Length && i < tileDefinitions.Length; i++)
        { int index = i; tileButtons[i].onClick.AddListener(() => placement.BeginTouchPlacement(tileDefinitions[index])); }
        information.SelectionChanged += OnSelectionChanged;
    }
    private void PrepareToolSurface()
    {
        if (toolsContent == null || safeArea == null) return;
        fixedToolRail = toolsContent.Find("ToolCategoryRail") as RectTransform;
        if (fixedToolRail == null) fixedToolRail = safeArea.Find("ToolCategoryRail") as RectTransform;
        if (fixedToolRail != null)
        {
            if (fixedToolRail.parent != safeArea) fixedToolRail.SetParent(safeArea, false);
            fixedToolRail.anchorMin = fixedToolRail.anchorMax = fixedToolRail.pivot = Vector2.up;
            fixedToolRail.sizeDelta = new Vector2(EnvironmentToolPanelLayout.RailWidth, 363f);
            fixedToolRail.localScale = Vector3.one;
        }
        if (toolsScroll != null && toolsScroll.transform.parent != safeArea)
            toolsScroll.transform.SetParent(safeArea, false);
        if (toolsDrawer != null)
        {
            var obsolete = toolsDrawer.gameObject;
            toolsDrawer = null; toolsDrawerRect = null;
            Destroy(obsolete);
        }
        foreach (var handle in toolsContent.GetComponentsInChildren<InformationTextDragHandle>(true))
            handle.enabled = false;
        var responsive = toolsContent.GetComponent<EnvironmentToolResponsiveLayout>();
        if (responsive != null) responsive.KeepDesktopPaper(true);
        if (toolsViewport != null)
        {
            var mask = toolsViewport.GetComponent<UnityEngine.UI.RectMask2D>();
            if (mask != null) mask.enabled = true;
        }
        if (toolsScroll != null)
        {
            var frame = (RectTransform)toolsScroll.transform;
            var grip = toolsScroll.GetComponentInChildren<EnvironmentToolResizeHandle>(true);
            if (grip != null)
            {
                var rect = (RectTransform)grip.transform;
                rect.SetParent(frame, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
                rect.anchoredPosition = new Vector2(-20f, 20f);
                rect.sizeDelta = new Vector2(34f, 34f);
                rect.SetAsLastSibling();
            }
            var close = toolsScroll.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            foreach (var button in close)
                if (button.name == "ExpandTechnology")
                {
                    var rect = (RectTransform)button.transform;
                    rect.SetParent(frame, false);
                    rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
                    rect.anchoredPosition = new Vector2(-20f, -20f);
                    rect.sizeDelta = new Vector2(44f, 44f);
                    EnsureCloseMark(button);
                    rect.SetAsLastSibling();
                    break;
                }
            toolsScroll.vertical = toolsScroll.horizontal = true;
            var zoom = toolsContent.GetComponent<EnvironmentToolPanelZoom>();
            if (zoom != null)
            {
                zoom.ConfigureMobileFrame((RectTransform)toolsScroll.transform);
                toolsScroll.gameObject.AddComponent<MobilePanelPinchZoom>().Configure(
                    (RectTransform)toolsScroll.transform, zoom.ZoomBy, touchInput);
            }
            toolsScroll.gameObject.SetActive(false);
        }
    }

    private void AttachInformationPanels()
    {
        if (information == null || safeArea == null || informationPanelsAttached) return;
        var tile = information.TileWindow;
        var population = information.PopulationWindow;
        if (tile == null || population == null) return;
        var tileRect = (RectTransform)tile.transform;
        var populationRect = (RectTransform)population.transform;
        desktopTileParent = tileRect.parent;
        desktopPopulationParent = populationRect.parent;
        desktopTilePosition = tileRect.anchoredPosition;
        desktopPopulationPosition = populationRect.anchoredPosition;
        tileRect.SetParent(safeArea, false);
        populationRect.SetParent(safeArea, false);
        var tilePanel = tile.GetComponent<TileInformationPanel>();
        if (tilePanel != null)
        {
            tilePanel.LockFullLayout(true);
            EnsureCloseMark(tile.GetComponentInChildren<UnityEngine.UI.Button>(true));
            var pinch = tile.GetComponent<MobilePanelPinchZoom>();
            if (pinch == null) pinch = tile.gameObject.AddComponent<MobilePanelPinchZoom>();
            pinch.Configure(tileRect, tilePanel.ZoomBy, touchInput);
            pinch.enabled = true;
        }
        var journal = population.GetComponent<PopulationJournalViewport>();
        if (journal != null)
        {
            journal.LockFullLayout(true);
            foreach (var button in population.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                if (button.transform.parent == population.transform)
                { EnsureCloseMark(button); break; }
            var pinch = population.GetComponent<MobilePanelPinchZoom>();
            if (pinch == null) pinch = population.gameObject.AddComponent<MobilePanelPinchZoom>();
            pinch.Configure(populationRect, journal.ZoomBy, touchInput);
            pinch.enabled = true;
        }
        informationPanelsAttached = true;
        LayoutInformationPanels();
    }

    private void RestoreInformationPanels()
    {
        if (!informationPanelsAttached || information == null) return;
        RestorePanel(information.TileWindow, desktopTileParent, desktopTilePosition);
        RestorePanel(information.PopulationWindow, desktopPopulationParent, desktopPopulationPosition);
        informationPanelsAttached = false;
    }

    private void RestorePanel(InformationWindow window, Transform parent, Vector2 position)
    {
        if (window == null || parent == null) return;
        var rect = (RectTransform)window.transform;
        var pinch = window.GetComponent<MobilePanelPinchZoom>();
        if (pinch != null) pinch.enabled = false;
        window.GetComponent<TileInformationPanel>()?.LockFullLayout(false);
        window.GetComponent<PopulationJournalViewport>()?.LockFullLayout(false);
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        window.SetDragBounds((RectTransform)parent, position);
    }

    private static void EnsureCloseMark(UnityEngine.UI.Button button)
    {
        if (button == null) return;
        var close = (RectTransform)button.transform;
        var existing = button.GetComponentInChildren<TextMeshProUGUI>(true);
        var mark = existing != null ? existing.rectTransform :
            close.Find("MobileCloseMark") as RectTransform;
        if (mark == null)
        {
            var label = new GameObject("MobileCloseMark", typeof(RectTransform), typeof(TextMeshProUGUI));
            mark = (RectTransform)label.transform;
            mark.SetParent(close, false);
        }
        mark.anchorMin = Vector2.zero;
        mark.anchorMax = Vector2.one;
        mark.offsetMin = mark.offsetMax = Vector2.zero;
        var text = mark.GetComponent<TextMeshProUGUI>();
        text.font = GameUITheme.HandwrittenTMPFont;
        text.fontSize = 36f;
        text.color = new Color(.12f, .25f, .08f);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.text = "×";
        mark.SetAsLastSibling();
    }

    private void LayoutInformationPanels()
    {
        if (information == null || safeArea.rect.width < 1f) return;
        var tile = information.TileWindow;
        var population = information.PopulationWindow;
        if (tile != null)
        {
            var rect = (RectTransform)tile.transform;
            float x = Mathf.Max(8f, safeArea.rect.width - rect.rect.width * rect.localScale.x - 16f);
            Vector2 home = new Vector2(x, -86f);
            if (!tile.IsOpen) rect.anchoredPosition = home;
            tile.SetDragBounds(safeArea, home);
        }
        if (population != null)
        {
            var rect = (RectTransform)population.transform;
            Vector2 home = new Vector2(-16f, -88f);
            if (!population.IsOpen) rect.anchoredPosition = home;
            population.SetDragBounds(safeArea, home);
        }
    }

    private float ToolRailTop()
    {
        float statusHeight = Mathf.Clamp(safeArea.rect.height * .086f, 44f, 62f);
        return statusHeight + 24f;
    }
    private void Update()
    {
        if (safeArea == null) return;
        if (Application.isPlaying && !bound) BindRuntime();
        ApplyMode();
        Vector2 screen = new Vector2(Screen.width, Screen.height);
        if (screen != lastScreen || ReadSafeArea() != lastSafeArea)
        { lastScreen = screen; lastSafeArea = ReadSafeArea(); RefreshLayout(); }
        if (!Application.isPlaying) return;
        if (!bound || !mobile) return;
        bool toolMode = toolSession != null && toolSession.Active;
        if (toolMode != previousToolMode)
        {
            previousToolMode = toolMode;
            if (toolMode && toolsContent != null)
                toolsContent.GetComponent<EnvironmentToolPanelZoom>()?.ResetFull();
            technologyPanel.Refresh();
            RefreshLayout();
            if (toolMode && toolsScroll != null)
            { toolsScroll.StopMovement(); toolsScroll.verticalNormalizedPosition = 1f; }
            RefreshRuntime();
        }
        // The desktop build Controller owns its mode; mobile only supplies a view.
        if (desktopBuildPanel != null && desktopBuildPanel.activeSelf) desktopBuildPanel.SetActive(false);
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + refreshSeconds;
        RefreshRuntime();
    }
    private void ApplyMode()
    {
        bool wanted = ResolveMobile;
        if (applied && mobile == wanted) return;
        applied = true; mobile = wanted;
        if (mobileCanvas != null) mobileCanvas.enabled = mobile;
        if (raycaster != null) raycaster.enabled = mobile;
        Canvas.ForceUpdateCanvases();
        RefreshLayout();
        if (!Application.isPlaying)
        {
            // Canvas visibility previews do not delete or deactivate authored objects.
            if (desktopCanvas != null) desktopCanvas.enabled = true;
            if (drawer != null) drawer.gameObject.SetActive(false);
            if (toolsDrawer != null) toolsDrawer.gameObject.SetActive(false);
            buildPalette.SetActive(true); placementControls.SetActive(true);
            return;
        }
        if (mobile) AttachInformationPanels();
        else RestoreInformationPanels();
        safeArea.gameObject.SetActive(mobile);
        if (desktopCanvas != null) desktopCanvas.enabled = true; // Keep world population markers.
        if (desktopHud != null) desktopHud.SetActive(!mobile);
        if (desktopBuildPanel != null) desktopBuildPanel.SetActive(!mobile && buildMode.IsBuildMode);
        information.SetMobilePresentation(false);
        if (touchInput != null) touchInput.CancelGesture();
        if (placement != null && placement.IsDragging) placement.CancelDrag();
        if (mobile) RefreshRuntime();
    }

    [ContextMenu("Refresh mobile layout preview")]
    public void RefreshLayout()
    {
        // Disabled canvases report zero-sized layout bounds in the Editor.
        // Keep their authored sizes until this presentation is enabled again.
        if (safeArea == null || Screen.width <= 0 || Screen.height <= 0 ||
            (mobileCanvas != null && !mobileCanvas.enabled)) return;
        if (fixedToolRail == null) fixedToolRail = safeArea.Find("ToolCategoryRail") as RectTransform;
        Rect safe = ReadSafeArea();
        safeArea.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
        safeArea.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        Canvas.ForceUpdateCanvases();
        LayoutMobileControls();
        if (Application.isPlaying && informationPanelsAttached)
            LayoutInformationPanels();
        if (toolsScroll != null)
        {
            float railTop = ToolRailTop();
            float toolLeft = (fixedToolRail != null ? EnvironmentToolPanelLayout.RailWidth + 16f : 8f);
            var root = (RectTransform)toolsScroll.transform;
            root.anchorMin = root.anchorMax = root.pivot = Vector2.up;
            root.anchoredPosition = new Vector2(toolLeft, -railTop);
            root.sizeDelta = new Vector2(Mathf.Max(180f, Mathf.Min(
                    safeArea.rect.width - toolLeft - 8f, EnvironmentToolPanelLayout.WideWidth + 24f)),
                Mathf.Max(180f, safeArea.rect.height - railTop - 8f));
            if (toolsViewport != null)
            {
                toolsViewport.anchorMin = Vector2.zero;
                toolsViewport.anchorMax = Vector2.one;
                toolsViewport.offsetMin = toolsViewport.offsetMax = Vector2.zero;
            }
            if (fixedToolRail != null)
            {
                fixedToolRail.anchorMin = fixedToolRail.anchorMax = fixedToolRail.pivot = Vector2.up;
                fixedToolRail.anchoredPosition = new Vector2(8f, -railTop);
            }
            if (toolsContent != null && toolsViewport != null)
            {
                var responsive = toolsContent.GetComponent<EnvironmentToolResponsiveLayout>();
                if (responsive != null) responsive.Apply(root.rect.width);
            }
        }
        if (fixedToolRail != null) fixedToolRail.gameObject.SetActive(mobile &&
            TutorialFlowController.Allows(TutorialFlowController.Feature.EnvironmentPreview));
    }
    private void LayoutMobileControls()
    {
        if (statusBar == null || actionColumn == null) return;
        // In landscape the cutout is usually at mid-height on one side. Keep
        // mid-screen actions inside the safe rect, but let top and bottom UI
        // use the unobstructed corners instead of leaving a full-height strip.
        Rect safe = ReadSafeArea();
        float unitsPerPixel = safe.width > 0f ? safeArea.rect.width / safe.width : 0f;
        float leftInset = Screen.width > Screen.height ? safe.xMin * unitsPerPixel : 0f;
        float rightInset = Screen.width > Screen.height ? (Screen.width - safe.xMax) * unitsPerPixel : 0f;
        float fullWidth = safeArea.rect.width + leftInset + rightInset;
        float statusHeight = Mathf.Clamp(safeArea.rect.height * .086f, 44f, 62f);
        void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }
        float safeWidth = Mathf.Max(1f, safeArea.rect.width);
        statusBar.anchorMin=new Vector2(-leftInset/safeWidth,1f);
        statusBar.anchorMax=new Vector2(1f+rightInset/safeWidth,1f);
        statusBar.pivot=Vector2.up;
        statusBar.anchoredPosition=new Vector2(8,-8);statusBar.sizeDelta=new Vector2(-16,statusHeight);
        statusBar.localScale = Vector3.one;
        var economy = statusBar.Find("Economy") as RectTransform;
        var day = statusBar.Find("Day") as RectTransform;
        var speed = speedButton != null ? speedButton.transform as RectTransform : null;
        if (economy != null) Place(economy,Vector2.zero,new Vector2(190,statusHeight));
        if (day != null)
        {
            Place(day,new Vector2(-153,0),new Vector2(140,statusHeight));
            day.anchorMin=day.anchorMax=day.pivot=Vector2.one;
        }
        if (speed != null)
        {
            Place(speed,Vector2.zero,new Vector2(145,statusHeight));
            speed.anchorMin=speed.anchorMax=speed.pivot=Vector2.one;
        }
        if (gestureHelp != null) gestureHelp.gameObject.SetActive(false);
        if (environmentToolsButton != null) environmentToolsButton.gameObject.SetActive(false);
        if (pauseButton != null) pauseButton.gameObject.SetActive(false);
        Place(actionColumn,new Vector2(206-leftInset,-8),new Vector2(344,statusHeight));
        var buttons = new[] { buildButton, speciesMapButton };
        for (int i=0;i<buttons.Length;i++)
            if (buttons[i]!=null) Place((RectTransform)buttons[i].transform,new Vector2(i*176,0),new Vector2(168,statusHeight));
        if (placementControls != null)
        {
            float actionBottom=statusHeight+24f;
            float placementLeft=actionBottom+4>safeArea.rect.height-276 ? 196 : 8;
            var controls=(RectTransform)placementControls.transform;
            controls.anchorMin=controls.anchorMax=controls.pivot=Vector2.zero;
            controls.anchoredPosition=new Vector2(placementLeft-leftInset,144);
            controls.sizeDelta=new Vector2(Mathf.Min(600,fullWidth-placementLeft-8),132);
            if (buildPalette != null)
            {
                var palette=(RectTransform)buildPalette.transform;
                float paletteLeft=actionBottom+4>safeArea.rect.height-140 ? 196 : 8;
                palette.offsetMin=new Vector2(paletteLeft-leftInset,8);
                palette.offsetMax=new Vector2(rightInset-8,140);
            }
        }
    }
    private Rect ReadSafeArea()
    {
        Rect safe = Screen.safeArea;
#if UNITY_EDITOR
        float scale = Screen.height / 720f;
        float left = Mathf.Max(0f, editorSafeInsets.x) * scale, top = Mathf.Max(0f, editorSafeInsets.y) * scale;
        float right = Mathf.Max(0f, editorSafeInsets.z) * scale, bottom = Mathf.Max(0f, editorSafeInsets.w) * scale;
        safe = Rect.MinMaxRect(safe.xMin + left, safe.yMin + bottom,
            Mathf.Max(safe.xMin + left + 1f, safe.xMax - right), Mathf.Max(safe.yMin + bottom + 1f, safe.yMax - top));
#endif
        return safe;
    }
    public void SelectTab(InformationTab tab)
    {
        if (informationScroll == null) return;
        if (informationScroll.content != null)
            scrollPositions[(int)informationTab] = informationScroll.verticalNormalizedPosition;
        informationTab = tab;
        ShowTab();
        if (Application.isPlaying) RefreshRuntime();
    }
    private void ShowTab()
    {
        if (Application.isPlaying && bound) RefreshLayout();
        if (informationBackground != null)
        {
            informationBackground.enabled = informationTab == InformationTab.Environment;
            informationBackground.sprite = environmentBackground;
        }
        informationScroll.StopMovement();
        tileContent.gameObject.SetActive(informationTab == InformationTab.Environment);
        populationContent.gameObject.SetActive(informationTab == InformationTab.Population);
        informationScroll.content = informationTab == InformationTab.Environment ? tileContent : populationContent;
        if (informationScroll.verticalScrollbar != null)
            informationScroll.verticalScrollbarVisibility =
                UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        Canvas.ForceUpdateCanvases();
        informationScroll.verticalNormalizedPosition = scrollPositions[(int)informationTab];
        lastTab = informationTab;
    }
    private void OnSelectionChanged(bool population)
    {
        if (mobile) RefreshRuntime();
    }
    private void CreatePopulationResize()
    {
        if (populationResize != null || drawerRect == null) return;
        var go = new GameObject("PopulationResize", typeof(RectTransform),
            typeof(UnityEngine.UI.Image), typeof(SnapPanelResizeHandle));
        populationResize = (RectTransform)go.transform;
        populationResize.SetParent(drawerRect, false);
        populationResize.anchorMin = populationResize.anchorMax = populationResize.pivot = new Vector2(1f, 0f);
        populationResize.anchoredPosition = new Vector2(-16f, 16f);
        populationResize.sizeDelta = new Vector2(48f, 48f);
        var image = go.GetComponent<UnityEngine.UI.Image>();
        image.sprite = Resources.Load<Sprite>("GameUI/SpeciesJournal/ResizeGrip");
        image.color = image.sprite == null ? new Color(.25f, .34f, .22f) : Color.white;
        image.raycastTarget = true;
        go.GetComponent<SnapPanelResizeHandle>().Initialize(delta =>
        {
            if (populationResizeSnapped) return;
            if (!populationCompact && delta < -76f)
            { populationResizeSnapped = true; SetPopulationCompact(true); return; }
            if (populationCompact && delta > 76f)
            { populationResizeSnapped = true; SetPopulationCompact(false); return; }
            populationDrawerScale = Mathf.Clamp(populationResizeStartScale + delta / 500f, .65f, 1.25f);
            RefreshLayout();
        }, () => { populationResizeStartScale = populationDrawerScale; populationResizeSnapped = false; });
        populationResize.SetAsLastSibling();
        populationResize.gameObject.SetActive(informationTab == InformationTab.Population);
    }

    private void SetPopulationCompact(bool value)
    {
        if (populationPanel == null) return;
        populationCompact = value;
        populationDrawerScale = 1f;
        populationPanel.SetCompact(value);
        RefreshLayout();
        if (informationScroll != null)
        {
            informationScroll.StopMovement();
            informationScroll.verticalNormalizedPosition = 1f;
        }
    }

    private void CloseActiveInformation()
    {
        if (informationTab == InformationTab.Environment) information.CloseTile();
        else information.ClosePopulation();
    }
    private void ToggleBuild()
    {
        if (placement != null) placement.CancelDrag();
        buildMode.SetBuildMode(!buildMode.IsBuildMode);
        RefreshRuntime();
    }
    private void CycleMobileSimulationSpeed()
    {
        if (clock == null) return;
        clock.CycleSpeed();
        RefreshRuntime();
    }
    private void RefreshRuntime()
    {
        if (!mobile || !bound) return;
        bool operating = toolSession != null && toolSession.Active;
        if (toolsScroll != null && toolsScroll.gameObject.activeSelf != operating)
            toolsScroll.gameObject.SetActive(operating);
        if (fixedToolRail != null) fixedToolRail.gameObject.SetActive(
            TutorialFlowController.Allows(TutorialFlowController.Feature.EnvironmentPreview));
        technologyPanel.Refresh();
        if (buildButton != null)
            buildButton.gameObject.SetActive(grid != null && grid.HasPlacedTile);
        if (speciesMapButton != null)
            speciesMapButton.gameObject.SetActive(TutorialFlowController.Allows(TutorialFlowController.Feature.SpeciesMap));
        if (speedButton != null)
            speedButton.gameObject.SetActive(TutorialFlowController.Allows(TutorialFlowController.Feature.Clock));
        if (bioenergyLabel != null)
            bioenergyLabel.text = bioenergy != null ? bioenergy.Balance.ToString("N0") : "0";
        dayLabel.text = clock == null ? "时间未接入" : "Day " + clock.currentDay;
        speedLabel.text = clock == null ? "倍速未接入" : clock.IsPaused ? "倍速：暂停" : "倍速 " + clock.Speed.ToString("0.#") + "X";
        bool building = buildMode != null && buildMode.IsBuildMode;
        buildLabel.text = building ? "退出建造" : "建造地块";
        buildPalette.SetActive(building);
        if (building && grid != null)
            for (int i = 0; i < tileButtons.Length && i < tileDefinitions.Length; i++)
            {
                bool unlocked = grid.HasPlacedTile && environment != null &&
                    environment.IsUnlocked(tileDefinitions[i]);
                tileButtons[i].interactable = unlocked;
                var label = tileButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null && tileDefinitions[i] != null)
                    label.text = unlocked ? tileDefinitions[i].displayName :
                        tileDefinitions[i].displayName + "\n未解锁";
            }
        bool placing = placement != null && placement.IsTouchPlacement;
        placementControls.SetActive(placing);
        confirmButton.interactable = placing && placement.CanConfirmTouchPlacement;
        if (placing) placementLabel.text = placement.TouchPreviewStatus;
    }
    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= RefreshEditorPreview;
#endif
        if (bound && information != null) information.SelectionChanged -= OnSelectionChanged;
        if (Application.isPlaying) RestoreInformationPanels();
        if (desktopCanvas != null) desktopCanvas.enabled = true;
        if (Application.isPlaying)
        {
            if (mobileCanvas != null) mobileCanvas.enabled = false;
            if (raycaster != null) raycaster.enabled = false;
            if (safeArea != null) safeArea.gameObject.SetActive(false);
            if (information != null) information.SetMobilePresentation(false);
            if (desktopHud != null) desktopHud.SetActive(true);
            if (desktopBuildPanel != null && buildMode != null) desktopBuildPanel.SetActive(buildMode.IsBuildMode);
        }
    }
}
