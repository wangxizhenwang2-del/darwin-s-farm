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
    private SimulationEnvironmentController terrainUnlocks;
    private BioenergyAccount bioenergy;
    private bool previousToolMode;

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
        if (this == null || !isActiveAndEnabled || Application.isPlaying ||
            safeArea == null || drawer == null || informationScroll == null) return;
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
        drawer.Initialize(safeArea, drawerDefaultPosition);
        if (toolsDrawer != null) toolsDrawer.Initialize(safeArea, ToolsDrawerHome);
        populationPanel.Initialize(drawer);
        foreach (InformationTextDragHandle handle in GetComponentsInChildren<InformationTextDragHandle>(true))
            handle.Initialize(toolsDrawer != null && handle.transform.IsChildOf(toolsDrawer.transform) ? toolsDrawer : drawer);
        technologyPanel.Initialize();
        toolSession = information.GetComponent<EnvironmentToolSession>();
        terrainUnlocks = FindFirstObjectByType<SimulationEnvironmentController>();
        bioenergy = FindFirstObjectByType<BioenergyAccount>();
        if (bioenergyLabel == null)
        {
            Transform label = statusBar != null ? statusBar.Find("Economy/Function") : null;
            if (label != null) bioenergyLabel = label.GetComponent<TextMeshProUGUI>();
        }
        if (environmentToolsButton != null) environmentToolsButton.onClick.AddListener(() => toolSession.Toggle());
        buildButton.onClick.AddListener(ToggleBuild);
        if (speciesMapButton != null && speciesMaps != null) speciesMapButton.onClick.AddListener(speciesMaps.Toggle);
        speedButton.onClick.AddListener(CycleMobileSimulationSpeed);
        environmentTabButton.onClick.AddListener(() => SelectTab(InformationTab.Environment));
        populationTabButton.onClick.AddListener(() => SelectTab(InformationTab.Population));
        closeButton.onClick.AddListener(CloseActiveInformation);
        rotateButton.onClick.AddListener(() => placement.RotateTouchPreview());
        confirmButton.onClick.AddListener(() => placement.ConfirmTouchPlacement());
        cancelButton.onClick.AddListener(() => placement.CancelDrag());
        for (int i = 0; i < tileButtons.Length && i < tileDefinitions.Length; i++)
        { int index = i; tileButtons[i].onClick.AddListener(() => placement.BeginTouchPlacement(tileDefinitions[index])); }
        information.SelectionChanged += OnSelectionChanged;
    }
    private void Update()
    {
        if (safeArea == null) return;
        if (Application.isPlaying && !bound) BindRuntime();
        ApplyMode();
        Vector2 screen = new Vector2(Screen.width, Screen.height);
        if (screen != lastScreen || ReadSafeArea() != lastSafeArea)
        { lastScreen = screen; lastSafeArea = ReadSafeArea(); RefreshLayout(); }
        if (!Application.isPlaying)
        { if (mobile && lastTab != informationTab) ShowTab(); return; }
        if (!bound || !mobile) return;
        bool toolMode = toolSession != null && toolSession.Active;
        if (toolMode != previousToolMode)
        {
            previousToolMode = toolMode;
            technologyPanel.Refresh();
            RefreshLayout();
            if (toolMode && toolsScroll != null)
            { toolsScroll.StopMovement(); toolsScroll.verticalNormalizedPosition = 1f; }
            RefreshRuntime();
        }
        if (environmentToolsButton != null)
            environmentToolsButton.GetComponentInChildren<TextMeshProUGUI>().text = "环境工具";
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
            if (desktopCanvas != null) desktopCanvas.enabled = !mobile;
            drawer.gameObject.SetActive(true);
            if (toolsDrawer != null) toolsDrawer.gameObject.SetActive(false);
            buildPalette.SetActive(true); placementControls.SetActive(true);
            if (mobile) ShowTab();
            return;
        }
        if (!mobile) drawer.HideImmediately();
        if (!mobile && toolsDrawer != null) toolsDrawer.HideImmediately();
        safeArea.gameObject.SetActive(mobile);
        if (desktopCanvas != null) desktopCanvas.enabled = true; // Keep world population markers.
        if (desktopHud != null) desktopHud.SetActive(!mobile);
        if (desktopBuildPanel != null) desktopBuildPanel.SetActive(!mobile && buildMode.IsBuildMode);
        information.SetMobilePresentation(mobile);
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
        Rect safe = ReadSafeArea();
        safeArea.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
        safeArea.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        Canvas.ForceUpdateCanvases();
        LayoutMobileControls();
        float width = Mathf.Clamp(safeArea.rect.width * drawerWidthFraction, 320f, maximumDrawerWidth);
        float height = Mathf.Max(240f, safeArea.rect.height + drawerDefaultPosition.y - drawerBottomClearance);
        drawerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        drawerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        titleLabel.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width - 24f);
        if (!Application.isPlaying || !drawer.IsOpen) drawerRect.anchoredPosition = drawerDefaultPosition;
        if (Application.isPlaying && bound) drawer.SetDragBounds(safeArea, drawerDefaultPosition);
        float scale = Mathf.Min(1f, viewport.rect.width / 500f);
        tileContent.localScale = populationContent.localScale = Vector3.one * scale;
        if (toolsDrawerRect != null)
        {
            toolsDrawerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            toolsDrawerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Clamp(safeArea.rect.height + drawerDefaultPosition.y - 20f, 180f, 612f));
            if (!Application.isPlaying || !toolsDrawer.IsOpen) toolsDrawerRect.anchoredPosition = ToolsDrawerHome;
            if (Application.isPlaying && bound) toolsDrawer.SetDragBounds(safeArea, ToolsDrawerHome);
            if (toolsContent != null && toolsViewport != null)
                toolsContent.localScale = Vector3.one * Mathf.Min(1f, Mathf.Max(1f, width - 40f) / 500f);
        }
    }
    private void LayoutMobileControls()
    {
        if (statusBar == null || actionColumn == null || gestureHelp == null) return;
        float statusHeight = Mathf.Clamp(safeArea.rect.height * .086f, 44f, 62f);
        float hintHeight = Mathf.Clamp(safeArea.rect.height * .1f, 44f, 72f);
        float buttonHeight = Mathf.Clamp((safeArea.rect.height - statusHeight - hintHeight - 32f) / 2f, 44f, 64f);
        void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }
        statusBar.anchorMin=Vector2.up;statusBar.anchorMax=Vector2.one;statusBar.pivot=Vector2.up;
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
        float drawerWidth = Mathf.Clamp(safeArea.rect.width * drawerWidthFraction,320f,maximumDrawerWidth);
        Place(gestureHelp,new Vector2(8,-statusHeight-16),
            new Vector2(Mathf.Clamp(safeArea.rect.width-drawerWidth-32,200,620),hintHeight));
        if (pauseButton != null) pauseButton.gameObject.SetActive(false);
        Place(actionColumn,new Vector2(8,-statusHeight-hintHeight-24),new Vector2(168,buttonHeight*3+16));
        var buttons = new[] { environmentToolsButton, buildButton, speciesMapButton };
        for (int i=0;i<buttons.Length;i++)
            if (buttons[i]!=null) Place((RectTransform)buttons[i].transform,new Vector2(0,-i*(buttonHeight+8)),new Vector2(168,buttonHeight));
        if (placementControls != null)
        {
            float actionBottom=statusHeight+hintHeight+24+buttonHeight*3+16;
            float placementLeft=actionBottom+4>safeArea.rect.height-276 ? 196 : 8;
            var controls=(RectTransform)placementControls.transform;
            controls.anchorMin=controls.anchorMax=controls.pivot=Vector2.zero;
            controls.anchoredPosition=new Vector2(placementLeft,144);
            controls.sizeDelta=new Vector2(Mathf.Min(600,safeArea.rect.width-placementLeft-8),132);
            if (buildPalette != null)
            {
                var palette=(RectTransform)buildPalette.transform;
                float paletteLeft=actionBottom+4>safeArea.rect.height-140 ? 196 : 8;
                palette.offsetMin=new Vector2(paletteLeft,8);
                palette.offsetMax=new Vector2(-8,140);
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
        if (informationScroll.content != null)
            scrollPositions[(int)informationTab] = informationScroll.verticalNormalizedPosition;
        informationTab = tab;
        ShowTab();
        if (Application.isPlaying) RefreshRuntime();
    }
    private void ShowTab()
    {
        if (informationBackground != null)
            informationBackground.sprite = informationTab == InformationTab.Environment ? environmentBackground : populationBackground;
        informationScroll.StopMovement();
        tileContent.gameObject.SetActive(informationTab == InformationTab.Environment);
        populationContent.gameObject.SetActive(informationTab == InformationTab.Population);
        informationScroll.content = informationTab == InformationTab.Environment ? tileContent : populationContent;
        Canvas.ForceUpdateCanvases();
        informationScroll.verticalNormalizedPosition = scrollPositions[(int)informationTab];
        lastTab = informationTab;
    }
    private void OnSelectionChanged(bool population)
    {
        if (!mobile) return;
        InformationTab tab = population ? InformationTab.Population : InformationTab.Environment;
        if ((population && information.PopulationIsOpen) || (!population && information.TileIsOpen))
        {
            SelectTab(tab);
            scrollPositions[(int)tab] = 1f;
            informationScroll.StopMovement();
            informationScroll.verticalNormalizedPosition = 1f;
        }
        else
        {
            if (information.TileIsOpen) SelectTab(InformationTab.Environment);
            else if (information.PopulationIsOpen) SelectTab(InformationTab.Population);
            else RefreshRuntime();
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
        if (lastTab != informationTab) ShowTab();
        bool operating = toolSession != null && toolSession.Active;
        bool any = information.TileIsOpen || information.PopulationIsOpen;
        drawer.SetPresentationVisible(true);
        if (any && !drawer.IsOpen) { ShowTab(); drawer.Open(); }
        else if (!any && drawer.IsOpen) drawer.Close();
        if (toolsDrawer != null)
        {
            if (operating && !toolsDrawer.IsOpen) toolsDrawer.Open();
            else if (!operating && toolsDrawer.IsOpen) toolsDrawer.Close();
        }
        bool tile = informationTab == InformationTab.Environment;
        titleLabel.text = tile ? "环境信息 · 拖动标题移动" : "生物信息 · 拖动标题移动";
        closeButton.interactable = tile ? information.TileIsOpen : information.PopulationIsOpen;
        if (tile)
        {
            trendPanel.Chart.transform.parent.gameObject.SetActive(true);
            tileDetails.gameObject.SetActive(true);
            tileDetails.text = information.TileIsOpen ? information.ReadTileDetails() : "长按地图地块查看环境信息";
            trendPanel.Show(information.TileIsOpen ? information.SelectedTile : null, history);
        }
        else if (information.PopulationIsOpen) information.RefreshPopulationView(populationPanel);
        else populationPanel.ShowUnavailable("点击种群查看生物信息");
        technologyPanel.Refresh();
        if (buildButton != null)
            buildButton.gameObject.SetActive(TutorialFlowController.Allows(TutorialFlowController.Feature.Build));
        if (environmentToolsButton != null)
            environmentToolsButton.gameObject.SetActive(TutorialFlowController.Allows(TutorialFlowController.Feature.EnvironmentPreview));
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
        if (building && terrainUnlocks != null)
            for (int i = 0; i < tileButtons.Length && i < tileDefinitions.Length; i++)
            {
                bool unlocked = terrainUnlocks.IsUnlocked(tileDefinitions[i]);
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
