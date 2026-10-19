using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// WhiteBox composition root for two read-only information windows.
// Scene input and presentation live here; ecological state remains in the existing controllers.
[RequireComponent(typeof(Canvas))]
public sealed class GameInformationUI : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [SerializeField] private MapGridManager grid;
    [SerializeField] private PopulationMovementController movement;
    [SerializeField] private SimulationController simulation;
    [SerializeField] private SimulationTime clock;
    [SerializeField] private MapBuildModeController buildMode;
    [SerializeField, Min(1f)] private float clickDistancePixels = 8f;
    [SerializeField, Min(0.05f)] private float refreshSeconds = 0.25f;

    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private readonly RaycastHit[] groundHits = new RaycastHit[32];
    private RectTransform canvasRect;
    private SimulationEnvironmentController environment;
    private Font font;
    [Header("Scene UI references (editable in the Hierarchy)")]
    [SerializeField] private InformationWindow tileWindow;
    [SerializeField] private InformationWindow populationWindow;
    [SerializeField] private Text tileText;
    [SerializeField] private PopulationInfoPanel populationPanel;
    [SerializeField] private Text dayText;
    [SerializeField] private Text speedText;
    [SerializeField] private Text bioenergyText;
    [SerializeField] private UnityEngine.UI.Button tileCloseButton;
    [SerializeField] private UnityEngine.UI.Button populationCloseButton;
    [SerializeField] private TileTrendHistory tileHistory;
    [SerializeField] private TileTrendPanel tileTrendPanel;
    private MapTileInstance selectedTile;
    private WhiteboxPopulation selectedVisual;
    private string selectedPopulationId;
    private Vector2 pressPosition;
    private bool awaitingRelease;
    private bool dragged;
    private float nextRefresh;
    private BiomeOcclusionVisibility biomeVisibility;
    private MapTouchInput touchInput;
    private EnvironmentToolSession tools;
    private BioenergyAccount bioenergy;
    private bool mobilePresentation;
    public bool ToolModeActive => tools != null && tools.Active;
    public WhiteboxPopulation SelectedPopulation
    {
        get
        {
            if (!PopulationIsOpen || selectedVisual == null || !selectedVisual.gameObject.activeInHierarchy) return null;
            var data = selectedVisual.EcologicalPopulation;
            return (data != null ? data.PopulationId : selectedVisual.UniqueId) == selectedPopulationId &&
                (data == null || data.speciesAmount > 0) ? selectedVisual : null;
        }
    }
    public MapTileInstance SelectedTile => selectedTile;
    public bool TileIsOpen => tileWindow != null && tileWindow.IsOpen;
    public bool PopulationIsOpen => populationWindow != null && populationWindow.IsOpen;
    public event System.Action<bool> SelectionChanged;
    public void SetMobilePresentation(bool mobile)
    {
        mobilePresentation = mobile;
        if (ToolModeActive && tileWindow != null) tileWindow.Close();
        if (tileWindow != null) tileWindow.SetPresentationVisible(!mobile);
        if (populationWindow != null) populationWindow.SetPresentationVisible(!mobile);
    }

    private void Awake()
    {
        tools = GetComponent<EnvironmentToolSession>();
        canvasRect = (RectTransform)transform;
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera != null) touchInput = viewCamera.GetComponent<MapTouchInput>();
        if (grid == null) grid = FindFirstObjectByType<MapGridManager>();
        if (grid != null)
        {
            if (movement == null) movement = grid.GetComponent<PopulationMovementController>();
            if (simulation == null) simulation = grid.GetComponent<SimulationController>();
            if (clock == null) clock = grid.GetComponent<SimulationTime>();
            if (buildMode == null) buildMode = grid.GetComponent<MapBuildModeController>();
            environment = grid.GetComponent<SimulationEnvironmentController>();
        }
        font = GameUITheme.Font;
    }

    private void Start()
    {
        Canvas.ForceUpdateCanvases();
        EnsureLayout();
        if (bioenergyText == null)
        {
            Transform label = transform.Find("InformationHUD/EconomyHUD/BioenergyBalance");
            if (label == null) label = transform.Find("InformationHUD/EconomyHUD/FundsPlaceholder");
            if (label != null) bioenergyText = label.GetComponent<Text>();
        }
        if (grid != null) bioenergy = grid.GetComponent<BioenergyAccount>();
        if (bioenergy == null) bioenergy = FindFirstObjectByType<BioenergyAccount>();
        if (bioenergy != null) bioenergy.BalanceChanged += RefreshBioenergy;
        RefreshBioenergy(bioenergy != null ? bioenergy.Balance : 0);
        BindWindows();
        tileHistory.Initialize(grid, simulation, clock);
        biomeVisibility = gameObject.GetComponent<BiomeOcclusionVisibility>();
        if (biomeVisibility == null) biomeVisibility = gameObject.AddComponent<BiomeOcclusionVisibility>();
        biomeVisibility.Initialize(viewCamera, grid, movement, canvasRect, font);
    }

    // Used once by the Editor installation tool; existing authored objects are retained.
    public void EnsureLayout()
    {
        canvasRect = (RectTransform)transform;
        font = GameUITheme.Font;
        var tileStatus = GetComponent<TileStatusPresenter>();
        if (tileStatus == null) tileStatus = gameObject.AddComponent<TileStatusPresenter>();
        tileStatus.EnsureLayout();
        if (dayText == null && speedText == null) BuildHud();
        BindSpeedControl();
        if (tileWindow == null && populationWindow == null) BuildWindows();
        if (tileHistory == null)
        {
            tileHistory = GetComponent<TileTrendHistory>();
            if (tileHistory == null) tileHistory = gameObject.AddComponent<TileTrendHistory>();
        }
        if (tileTrendPanel == null)
        {
            tileTrendPanel = tileWindow.GetComponent<TileTrendPanel>();
            if (tileTrendPanel == null) tileTrendPanel = tileWindow.gameObject.AddComponent<TileTrendPanel>();
            tileTrendPanel.Initialize();
            // Only the initial installation moves the existing detail text below the chart.
            Vector2 position = tileText.rectTransform.anchoredPosition;
            position.y = -454f;
            tileText.rectTransform.anchoredPosition = position;
            tileText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 236f);
        }
    }

    private void BindSpeedControl()
    {
        if (speedText == null) return;
        var panel = speedText.transform.parent;
        var image = panel.GetComponent<UnityEngine.UI.Image>();
        if (image == null) return;
        image.raycastTarget = true;
        var button = panel.GetComponent<UnityEngine.UI.Button>();
        if (button == null) button = panel.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        button.onClick.RemoveListener(CycleSimulationSpeed);
        button.onClick.AddListener(CycleSimulationSpeed);
    }

    private void CycleSimulationSpeed()
    {
        if (clock == null) return;
        clock.CycleSpeed();
        RefreshVisible();
    }

    private void BindWindows()
    {
        tileWindow.Initialize(canvasRect, ((RectTransform)tileWindow.transform).anchoredPosition);
        populationWindow.Initialize(canvasRect, ((RectTransform)populationWindow.transform).anchoredPosition);
        populationPanel.Initialize(populationWindow);
        foreach (InformationTextDragHandle handle in GetComponentsInChildren<InformationTextDragHandle>(true))
            // Initialize() hides windows before binding; include inactive parents.
            handle.Initialize(handle.GetComponentInParent<InformationWindow>(true));
        tileCloseButton.onClick.AddListener(CloseTile);
        populationCloseButton.onClick.AddListener(ClosePopulation);
    }

    public void CloseTile() { tileWindow.Close(); selectedTile = null; SelectionChanged?.Invoke(false); }
    public void ClosePopulation()
    {
        populationWindow.Close(); selectedVisual = null; selectedPopulationId = null;
        SelectionChanged?.Invoke(true);
    }

    private void OnEnable()
    {
        if (grid != null) grid.TilesChanged += RefreshVisible;
        if (simulation != null)
        {
            simulation.OnDaySimulated += RefreshAfterDay;
            simulation.OnBlockCommunityChanged += RefreshCommunity;
        }
        if (environment != null) environment.Changed += RefreshEnvironment;
        if (bioenergy != null) bioenergy.BalanceChanged += RefreshBioenergy;
    }

    private void OnDisable()
    {
        if (grid != null) grid.TilesChanged -= RefreshVisible;
        if (simulation != null)
        {
            simulation.OnDaySimulated -= RefreshAfterDay;
            simulation.OnBlockCommunityChanged -= RefreshCommunity;
        }
        if (environment != null) environment.Changed -= RefreshEnvironment;
        if (bioenergy != null) bioenergy.BalanceChanged -= RefreshBioenergy;
        awaitingRelease = false;
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshSeconds;
            RefreshVisible();
        }
        if (touchInput != null && touchInput.SuppressesMouseInput)
        { awaitingRelease = false; return; }
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        bool alt = keyboard != null &&
                   (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
        if (!Application.isFocused || mouse == null || viewCamera == null || grid == null)
        {
            awaitingRelease = false;
            return;
        }
        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressPosition = mouse.position.ReadValue();
            dragged = false;
            awaitingRelease = (alt || ToolModeActive) && (buildMode == null || !buildMode.IsBuildMode) &&
                viewCamera.pixelRect.Contains(pressPosition) && !PointerOverUi(pressPosition);
        }
        if (!awaitingRelease) return;
        if (!alt && !ToolModeActive) { awaitingRelease = false; return; }
        Vector2 position = mouse.position.ReadValue();
        if ((position - pressPosition).sqrMagnitude > clickDistancePixels * clickDistancePixels)
            dragged = true;
        if (!mouse.leftButton.wasReleasedThisFrame) return;
        awaitingRelease = false;
        if (!dragged && !PointerOverUi(position)) SelectSceneTarget(position);
    }

    private bool PointerOverUi(Vector2 position)
    {
        if (EconomyOperationsView.IsPointerOverUi(position)) return true;
        if (EventSystem.current == null) return false;
        var pointer = new PointerEventData(EventSystem.current) { position = position };
        uiHits.Clear();
        EventSystem.current.RaycastAll(pointer, uiHits);
        foreach (RaycastResult hit in uiHits)
            if (hit.module is GraphicRaycaster) return true;
        return false;
    }

    private void SelectSceneTarget(Vector2 position) => TrySelectSceneAt(position);

    public bool TrySelectSceneAt(Vector2 position, float populationPaddingPixels = 0f)
    {
        if (ToolModeActive) return TrySelectTileAt(position);
        if (TrySelectPopulationAt(position, populationPaddingPixels)) return true;
        return TrySelectTileAt(position);
    }

    private bool CanSelectAt(Vector2 position) => isActiveAndEnabled &&
        viewCamera != null && grid != null && tileWindow != null && populationWindow != null &&
        (buildMode == null || !buildMode.IsBuildMode) && viewCamera.pixelRect.Contains(position) &&
        !PointerOverUi(position);

    // Selection replaces the corresponding window's target on both platforms.
    public bool TrySelectPopulationAt(Vector2 position, float paddingPixels = 0f)
    {
        if (!CanSelectAt(position)) return false;
        // The existing population cubes intentionally have no colliders. Pick their
        // visible sprite rectangles before querying the terrain colliders below.
        WhiteboxPopulation visual = PickPopulation(position, Mathf.Max(0f, paddingPixels));
        if (visual == null) return false;
        selectedVisual = visual;
        selectedPopulationId = visual.EcologicalPopulation != null
            ? visual.EcologicalPopulation.PopulationId : visual.UniqueId;
        RefreshPopulation();
        populationWindow.Open();
        SelectionChanged?.Invoke(true);
        return true;
    }

    public bool TrySelectTileAt(Vector2 position)
    {
        if (!CanSelectAt(position)) return false;
        Ray ray = viewCamera.ScreenPointToRay(position);
        int count = Physics.RaycastNonAlloc(ray, groundHits, 1000f,
            LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore);
        MapTileInstance tile = null;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            MapTileInstance candidate = groundHits[i].collider == null ? null :
                groundHits[i].collider.GetComponentInParent<MapTileInstance>();
            if (candidate == null || groundHits[i].distance >= nearest) continue;
            tile = candidate;
            nearest = groundHits[i].distance;
        }
        if (tile == null) return false;
        selectedTile = tile;
        RefreshTile();
        // Tool targeting selects a tile independently of either platform's
        // information window. Only an information request opens that window.
        if (ToolModeActive) tileWindow.Close();
        else tileWindow.Open();
        SelectionChanged?.Invoke(false);
        return true;
    }

    private WhiteboxPopulation PickPopulation(Vector2 position, float paddingPixels = 0f)
    {
        if (biomeVisibility != null && biomeVisibility.TryPickMarker(position, out WhiteboxPopulation marked))
            return marked;
        if (movement == null) return null;
        WhiteboxPopulation closest = null;
        float depth = float.PositiveInfinity;
        foreach (WhiteboxPopulation population in movement.Populations)
        {
            if (population == null || !population.gameObject.activeInHierarchy) continue;
            SpriteRenderer[] sprites = population.GetComponentsInChildren<SpriteRenderer>();
            bool hasSprite = false;
            foreach (SpriteRenderer sprite in sprites)
            {
                if (sprite == null || !sprite.enabled || sprite.sprite == null) continue;
                hasSprite = true;
                TestSprite(sprite, position, population, ref closest, ref depth, paddingPixels);
            }
            if (hasSprite) continue;
            foreach (MeshRenderer cube in population.GetComponentsInChildren<MeshRenderer>())
                if (cube != null && cube.enabled && cube.gameObject.name != "PopulationCount")
                    TestBounds(cube.bounds, position, population, ref closest, ref depth, paddingPixels);
        }
        return closest;
    }

    private void TestSprite(SpriteRenderer sprite, Vector2 position, WhiteboxPopulation owner,
        ref WhiteboxPopulation closest, ref float nearestDepth, float paddingPixels)
    {
        Bounds bounds = sprite.sprite.bounds;
        Vector3 min = bounds.min, max = bounds.max;
        Vector3 a = viewCamera.WorldToScreenPoint(sprite.transform.TransformPoint(min.x, min.y, 0f));
        Vector3 b = viewCamera.WorldToScreenPoint(sprite.transform.TransformPoint(max.x, min.y, 0f));
        Vector3 c = viewCamera.WorldToScreenPoint(sprite.transform.TransformPoint(max.x, max.y, 0f));
        Vector3 d = viewCamera.WorldToScreenPoint(sprite.transform.TransformPoint(min.x, max.y, 0f));
        if (a.z <= 0f || b.z <= 0f || c.z <= 0f || d.z <= 0f) return;
        if (!InsideQuad(position, a, b, c, d) &&
            (paddingPixels <= 0f || Mathf.Min(
                PopulationTileSpace.SegmentDistanceSquared(position, position, a, b),
                PopulationTileSpace.SegmentDistanceSquared(position, position, b, c),
                PopulationTileSpace.SegmentDistanceSquared(position, position, c, d),
                PopulationTileSpace.SegmentDistanceSquared(position, position, d, a)) >
                paddingPixels * paddingPixels)) return;
        float candidateDepth = viewCamera.WorldToScreenPoint(sprite.bounds.center).z;
        if (candidateDepth >= nearestDepth) return;
        nearestDepth = candidateDepth;
        closest = owner;
    }

    private static bool InsideQuad(Vector2 point, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float ab = Cross(b - a, point - a);
        float bc = Cross(c - b, point - b);
        float cd = Cross(d - c, point - c);
        float da = Cross(a - d, point - d);
        return (ab >= 0f && bc >= 0f && cd >= 0f && da >= 0f) ||
               (ab <= 0f && bc <= 0f && cd <= 0f && da <= 0f);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private void TestBounds(Bounds bounds, Vector2 position, WhiteboxPopulation owner,
        ref WhiteboxPopulation closest, ref float nearestDepth, float paddingPixels)
    {
        Vector3 min = bounds.min, max = bounds.max;
        float left = float.PositiveInfinity, right = float.NegativeInfinity;
        float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
        {
            Vector3 point = viewCamera.WorldToScreenPoint(new Vector3(
                x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z));
            if (point.z <= 0f) return;
            left = Mathf.Min(left, point.x); right = Mathf.Max(right, point.x);
            bottom = Mathf.Min(bottom, point.y); top = Mathf.Max(top, point.y);
        }
        float padding = Mathf.Max(7f, paddingPixels);
        if (position.x < left - padding || position.x > right + padding ||
            position.y < bottom - padding || position.y > top + padding) return;
        float candidateDepth = viewCamera.WorldToScreenPoint(bounds.center).z;
        if (candidateDepth >= nearestDepth) return;
        nearestDepth = candidateDepth;
        closest = owner;
    }

    private void BuildHud()
    {
        RectTransform root = NewRect("InformationHUD", canvasRect, Vector2.zero,
            Vector2.one, Vector2.zero, Vector2.zero);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        RectTransform funds = NewPanel("EconomyHUD", new Vector2(0f, 1f), Vector2.up,
            new Vector2(300f, 98f), "GameUI/经济ui", new Rect(155f, 482f, 1453f, 473f), root);
        funds.anchoredPosition = new Vector2(24f, -14f);
        funds.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        bioenergyText = NewText("BioenergyBalance", funds, "0", 22, Ink(),
            new Vector2(116f, -22f), new Vector2(168f, 62f), false);
        bioenergyText.alignment = TextAnchor.MiddleCenter;
        RectTransform time = NewPanel("TimeHUD", Vector2.one, Vector2.one,
            new Vector2(230f, 92f), "GameUI/时间ui", new Rect(320f, 1315f, 1317f, 528f), root);
        time.anchoredPosition = new Vector2(-138f, -14f);
        time.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        dayText = NewText("Day", time, "Day 0", 26, Ink(),
            new Vector2(40f, -16f), new Vector2(150f, 52f), false);
        dayText.alignment = TextAnchor.MiddleCenter;
        RectTransform speed = NewPanel("SpeedHUD", Vector2.one, Vector2.one,
            new Vector2(100f, 56f), "GameUI/倍速ui", new Rect(502f, 764f, 1048f, 586f), root);
        speed.anchoredPosition = new Vector2(-24f, -30f);
        speed.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        speedText = NewText("Speed", speed, "1X", 24, Ink(),
            new Vector2(13f, -9f), new Vector2(74f, 38f), false);
        speedText.alignment = TextAnchor.MiddleCenter;
    }

    private void BuildWindows()
    {
        float width = Mathf.Max(320f, canvasRect.rect.width);
        RectTransform tile = NewPanel("TileInformation", new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(Mathf.Min(790f, width * 0.57f), 366f),
            "GameUI/环境信息ui", new Rect(180f, 455f, 1690f, 710f));
        tileWindow = tile.gameObject.AddComponent<InformationWindow>();
        tileText = NewText("TileDetails", tile, "", 23, Ink(),
            new Vector2(96f, -67f), new Vector2(tile.rect.width - 180f, tile.rect.height - 112f), true);
        tileText.GetComponent<InformationTextDragHandle>().Initialize(tileWindow);
        tileText.lineSpacing = 1.25f;
        tileCloseButton = NewCloseButton(tile);
        tile.anchoredPosition = new Vector2(28f, -139f);
        tileWindow.Initialize(canvasRect, new Vector2(28f, -139f));

        RectTransform population = NewPanel("PopulationInformation", new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(Mathf.Min(520f, width * 0.42f), 840f),
            "GameUI/生物信息ui", new Rect(500f, 215f, 1050f, 1720f));
        populationWindow = population.gameObject.AddComponent<InformationWindow>();
        populationPanel = population.gameObject.AddComponent<PopulationInfoPanel>();
        populationPanel.Initialize(populationWindow);
        populationCloseButton = NewCloseButton(population, 72f);
        population.anchoredPosition = new Vector2(-26f, -130f);
        populationWindow.Initialize(canvasRect, new Vector2(-26f, -130f));
    }

    private RectTransform NewPanel(string name, Vector2 anchor, Vector2 pivot,
        Vector2 size, string resource, Rect crop, RectTransform parent = null)
    {
        RectTransform rect = NewRect(name, parent != null ? parent : canvasRect, anchor, pivot, size, Vector2.zero);
        UnityEngine.UI.Image image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        Texture2D texture = Resources.Load<Texture2D>(resource);
        if (texture != null)
        {
            Rect safe = new Rect(Mathf.Clamp(crop.x, 0, texture.width - 1),
                Mathf.Clamp(crop.y, 0, texture.height - 1),
                Mathf.Min(crop.width, texture.width - crop.x),
                Mathf.Min(crop.height, texture.height - crop.y));
            image.sprite = Sprite.Create(texture, safe, new Vector2(0.5f, 0.5f), 100f);
        }
        else image.color = new Color(0.84f, 0.94f, 0.86f, 0.96f);
        image.raycastTarget = true;
        return rect;
    }

    private UnityEngine.UI.Button NewCloseButton(RectTransform parent,
        float topInset = 22f)
    {
        RectTransform buttonRect = NewRect("Close", parent, new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(42f, 42f), new Vector2(-23f, -topInset));
        UnityEngine.UI.Image image = buttonRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.16f, 0.21f, 0.22f, 0.9f);
        UnityEngine.UI.Button button = buttonRect.gameObject.AddComponent<UnityEngine.UI.Button>();
        Text label = NewText("X", buttonRect, "×", 30, Color.white,
            new Vector2(0f, 0f), new Vector2(42f, 42f), false);
        label.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    private Text NewText(string name, RectTransform parent, string value, int size,
        Color color, Vector2 position, Vector2 dimensions, bool dragHandle)
    {
        RectTransform rect = NewRect(name, parent, new Vector2(0f, 1f),
            new Vector2(0f, 1f), dimensions, position);
        Text label = rect.gameObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = GameUITheme.ReadableFontSize(size);
        label.color = color;
        label.alignment = TextAnchor.UpperLeft;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.text = value;
        label.raycastTarget = dragHandle;
        if (dragHandle)
            rect.gameObject.AddComponent<InformationTextDragHandle>();
        return label;
    }

    private static RectTransform NewRect(string name, RectTransform parent, Vector2 anchor,
        Vector2 pivot, Vector2 size, Vector2 position)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)obj.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private static Color Ink() => new Color(0.11f, 0.16f, 0.17f);

    private void RefreshAfterDay(int day) => RefreshVisible();
    private void RefreshCommunity(BlockInfo block) => RefreshVisible();
    private void RefreshEnvironment(IReadOnlyList<EnvironmentReadSnapshot> snapshots)
        => RefreshVisible();

    private void RefreshVisible()
    {
        if (bioenergy != null) RefreshBioenergy(bioenergy.Balance);
        if (dayText != null) dayText.text = "Day " + (clock == null ? "--" : clock.currentDay.ToString());
        if (speedText != null) speedText.text = clock == null ? "--" :
            clock.IsPaused ? "暂停" : clock.Speed.ToString("0.#") + "X";
        if (tileWindow != null && tileWindow.IsOpen) RefreshTile();
        if (populationWindow != null && populationWindow.IsOpen) RefreshPopulation();
    }

    private void RefreshBioenergy(long balance)
    {
        if (bioenergyText != null) bioenergyText.text = balance.ToString("N0");
    }

    private void RefreshTile()
    {
        if (tileText == null) return;
        if (tileTrendPanel != null) tileTrendPanel.Show(selectedTile, tileHistory);
        tileText.text = ReadTileDetails();
    }

    public string ReadTileDetails()
    {
        if (selectedTile == null) return "该地块已不在场景中";
        BlockInfo block = selectedTile.Block;
        if (block == null) return "地块数据暂不可用";
        string terrain = selectedTile.Biome.ToString();
        string elevation = block.elevation.ToString();
        string temperature = block.temperature.ToString();
        string humidity = block.humidity.ToString();
        string plants = block.plantBiomass.ToString("F0");
        string capacity = block.maxPlantBiomass.ToString("F0");
        string recovery = block.habitatRecovery.ToString();
        if (environment != null && environment.TryRead(selectedTile.Coordinate, out var snapshot))
        {
            terrain = snapshot.Environment.Terrain.ToString();
            elevation = snapshot.Environment.Elevation.ToString();
            temperature = snapshot.Environment.Temperature.Value.ToString("F0");
            humidity = snapshot.Environment.Humidity.Value.ToString("F0");
            plants = snapshot.PlantStock.ToString("F0");
            capacity = snapshot.PlantCapacity.ToString();
            recovery = snapshot.Environment.Recovery.Value.ToString("F0");
        }
        int groups = 0;
        if (block.community != null)
            foreach (PopulationData item in block.community)
                if (item != null && item.speciesAmount > 0) groups++;
        return $"地块信息  ({selectedTile.Coordinate.x}, {selectedTile.Coordinate.y})\n" +
            $"地形：{terrain}    海拔：{elevation}\n" +
            $"温度：{temperature}    湿度：{humidity}\n" +
            $"植物库存：{plants} / {capacity}\n" +
            $"每日恢复：{recovery}    种群：{groups}\n" +
            $"水域类型：{block.waterCoverage}";
    }

    private void RefreshPopulation() => RefreshPopulationView(populationPanel);
    public void RefreshPopulationView(PopulationInfoPanel view)
    {
        if (view == null) return;
        if (selectedVisual == null) { view.ShowUnavailable("该种群已不在场景中"); return; }
        PopulationData data = selectedVisual.EcologicalPopulation;
        string id = data != null ? data.PopulationId : selectedVisual.UniqueId;
        if (id != selectedPopulationId) { view.ShowUnavailable("该种群已迁出或消失"); return; }
        BlockInfo block = null;
        if (grid != null && grid.TryGetTile(selectedVisual.TileId, out MapTileInstance tile)) block = tile.Block;
        view.Refresh(selectedVisual, data, block);
    }
}
