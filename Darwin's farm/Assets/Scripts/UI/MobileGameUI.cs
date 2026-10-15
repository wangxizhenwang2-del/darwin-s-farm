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
    [SerializeField] private ScreenEdgeCamera mapCamera;
    [SerializeField] private MapBuildModeController buildMode;
    [SerializeField] private MapTilePlacementController placement;
    [SerializeField] private SimulationTime clock;
    [SerializeField] private WhiteboxEcologyDebugView debugView;
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
    [SerializeField] private GameObject buildPalette, placementControls;
    [SerializeField] private UnityEngine.UI.Button buildButton, pauseButton, speedButton, locateButton;
    [SerializeField] private UnityEngine.UI.Button environmentTabButton, populationTabButton, closeButton;
    [SerializeField] private UnityEngine.UI.Button rotateButton, confirmButton, cancelButton;
    [SerializeField] private UnityEngine.UI.Button[] tileButtons;
    [SerializeField] private MapTileDefinition[] tileDefinitions;
    [SerializeField] private TextMeshProUGUI dayLabel, pauseLabel, speedLabel, buildLabel, titleLabel, placementLabel;
    [Header("Mobile layout (Canvas units)")]
    [SerializeField, Min(320f)] private float maximumDrawerWidth = 520f;
    [SerializeField, Range(.3f, .6f)] private float drawerWidthFraction = .43f;
    [SerializeField] private Vector2 drawerDefaultPosition = new Vector2(-8f, -92f);
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
        populationPanel.Initialize(drawer);
        foreach (InformationTextDragHandle handle in GetComponentsInChildren<InformationTextDragHandle>(true))
            handle.Initialize(drawer);
        technologyPanel.Initialize();
        buildButton.onClick.AddListener(ToggleBuild);
        pauseButton.onClick.AddListener(() => { if (clock != null) clock.TogglePause(); });
        speedButton.onClick.AddListener(() => { if (clock != null) { if (clock.Speed == 2f) clock.Play(); else clock.DoubleSpeed(); } });
        locateButton.onClick.AddListener(() => { if (mapCamera != null) mapCamera.ReturnToPlayer(); });
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
        { if (lastTab != informationTab) ShowTab(); return; }
        if (!bound || !mobile) return;
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
            buildPalette.SetActive(true); placementControls.SetActive(true);
            ShowTab();
            return;
        }
        if (!mobile) drawer.HideImmediately();
        safeArea.gameObject.SetActive(mobile);
        if (desktopCanvas != null) desktopCanvas.enabled = true; // Keep world population markers.
        if (desktopHud != null) desktopHud.SetActive(!mobile);
        if (desktopBuildPanel != null) desktopBuildPanel.SetActive(!mobile && buildMode.IsBuildMode);
        if (debugView != null) debugView.PresentationEnabled = !mobile;
        information.SetMobilePresentation(mobile);
        if (touchInput != null) touchInput.CancelGesture();
        if (placement != null && placement.IsDragging) placement.CancelDrag();
        if (mobile) RefreshRuntime();
    }

    [ContextMenu("Refresh mobile layout preview")]
    public void RefreshLayout()
    {
        if (safeArea == null || Screen.width <= 0 || Screen.height <= 0) return;
        Rect safe = ReadSafeArea();
        safeArea.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
        safeArea.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        Canvas.ForceUpdateCanvases();
        float width = Mathf.Clamp(safeArea.rect.width * drawerWidthFraction, 320f, maximumDrawerWidth);
        float height = Mathf.Max(240f, safeArea.rect.height + drawerDefaultPosition.y - drawerBottomClearance);
        drawerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        drawerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        titleLabel.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width - 24f);
        if (!Application.isPlaying || !drawer.IsOpen) drawerRect.anchoredPosition = drawerDefaultPosition;
        if (Application.isPlaying && bound) drawer.SetDragBounds(safeArea, drawerDefaultPosition);
        float scale = Mathf.Min(1f, viewport.rect.width / 500f);
        tileContent.localScale = populationContent.localScale = Vector3.one * scale;
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
    private void RefreshRuntime()
    {
        if (!mobile || !bound) return;
        if (lastTab != informationTab) ShowTab();
        bool any = information.TileIsOpen || information.PopulationIsOpen;
        if (any && !drawer.IsOpen) { ShowTab(); drawer.Open(); }
        else if (!any && drawer.IsOpen) drawer.Close();
        bool tile = informationTab == InformationTab.Environment;
        titleLabel.text = tile ? "环境信息 · 拖标题移动，滑正文查看更多" : "生物信息 · 拖标题移动，滑正文查看更多";
        closeButton.interactable = tile ? information.TileIsOpen : information.PopulationIsOpen;
        if (tile)
        {
            tileDetails.text = information.TileIsOpen ? information.ReadTileDetails() : "长按地图地块查看环境信息";
            trendPanel.Show(information.TileIsOpen ? information.SelectedTile : null, history);
            technologyPanel.SetTarget(information.TileIsOpen ? information.SelectedTile : null);
        }
        else if (information.PopulationIsOpen) information.RefreshPopulationView(populationPanel);
        else populationPanel.ShowUnavailable("点击种群查看生物信息");
        dayLabel.text = clock == null ? "时间未接入" : "Day " + clock.currentDay;
        pauseLabel.text = clock != null && clock.IsPaused ? "继续模拟" : "暂停模拟";
        speedLabel.text = clock == null ? "倍速未接入" : clock.IsPaused ? "倍速：暂停" : "倍速 " + clock.Speed.ToString("0.#") + "X";
        bool building = buildMode != null && buildMode.IsBuildMode;
        buildLabel.text = building ? "退出建造" : "建造地块";
        buildPalette.SetActive(building);
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
            if (debugView != null) debugView.PresentationEnabled = true;
        }
    }
}
