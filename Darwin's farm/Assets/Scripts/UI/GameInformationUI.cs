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
    private InformationWindow tileWindow;
    private InformationWindow populationWindow;
    private Text tileText;
    private Text populationText;
    private Text dayText;
    private Text speedText;
    private MapTileInstance selectedTile;
    private WhiteboxPopulation selectedVisual;
    private string selectedPopulationId;
    private Vector2 pressPosition;
    private bool awaitingRelease;
    private bool dragged;
    private float nextRefresh;

    private void Awake()
    {
        canvasRect = (RectTransform)transform;
        if (viewCamera == null) viewCamera = Camera.main;
        if (grid == null) grid = FindFirstObjectByType<MapGridManager>();
        if (grid != null)
        {
            if (movement == null) movement = grid.GetComponent<PopulationMovementController>();
            if (simulation == null) simulation = grid.GetComponent<SimulationController>();
            if (clock == null) clock = grid.GetComponent<SimulationTime>();
            if (buildMode == null) buildMode = grid.GetComponent<MapBuildModeController>();
            environment = grid.GetComponent<SimulationEnvironmentController>();
        }
        font = Resources.Load<Font>("Fonts/NotoSansSC-MapLabels");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private void Start()
    {
        Canvas.ForceUpdateCanvases();
        BuildHud();
        BuildWindows();
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
        awaitingRelease = false;
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshSeconds;
            RefreshVisible();
        }
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
            awaitingRelease = alt && (buildMode == null || !buildMode.IsBuildMode) &&
                viewCamera.pixelRect.Contains(pressPosition) && !PointerOverUi(pressPosition);
        }
        if (!awaitingRelease) return;
        if (!alt) { awaitingRelease = false; return; }
        Vector2 position = mouse.position.ReadValue();
        if ((position - pressPosition).sqrMagnitude > clickDistancePixels * clickDistancePixels)
            dragged = true;
        if (!mouse.leftButton.wasReleasedThisFrame) return;
        awaitingRelease = false;
        if (!dragged && !PointerOverUi(position)) SelectSceneTarget(position);
    }

    private bool PointerOverUi(Vector2 position)
    {
        if (WhiteboxEcologyDebugView.IsPointerOverTestUi(position)) return true;
        if (EventSystem.current == null) return false;
        var pointer = new PointerEventData(EventSystem.current) { position = position };
        uiHits.Clear();
        EventSystem.current.RaycastAll(pointer, uiHits);
        foreach (RaycastResult hit in uiHits)
            if (hit.module is GraphicRaycaster) return true;
        return false;
    }

    private void SelectSceneTarget(Vector2 position)
    {
        // The existing population cubes intentionally have no colliders. Pick their
        // visible sprite rectangles before querying the terrain colliders below.
        WhiteboxPopulation visual = PickPopulation(position);
        if (visual != null)
        {
            if (populationWindow.IsOpen) return;
            selectedVisual = visual;
            selectedPopulationId = visual.EcologicalPopulation != null
                ? visual.EcologicalPopulation.PopulationId : visual.UniqueId;
            RefreshPopulation();
            populationWindow.Open();
            return;
        }

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
        if (tile == null || tileWindow.IsOpen) return;
        selectedTile = tile;
        RefreshTile();
        tileWindow.Open();
    }

    private WhiteboxPopulation PickPopulation(Vector2 position)
    {
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
                TestSprite(sprite, position, population, ref closest, ref depth);
            }
            if (hasSprite) continue;
            foreach (MeshRenderer cube in population.GetComponentsInChildren<MeshRenderer>())
                if (cube != null && cube.enabled && cube.gameObject.name != "PopulationCount")
                    TestBounds(cube.bounds, position, population, ref closest, ref depth);
        }
        return closest;
    }

    private void TestSprite(SpriteRenderer sprite, Vector2 position, WhiteboxPopulation owner,
        ref WhiteboxPopulation closest, ref float nearestDepth)
    {
        Bounds bounds = sprite.sprite.bounds;
        Vector3 min = bounds.min, max = bounds.max;
        Vector3 a = viewCamera.WorldToScreenPoint(sprite.transform.TransformPoint(min.x, min.y, 0f));
        Vector3 b = viewCamera.WorldToScreenPoint(sprite.transform.TransformPoint(max.x, min.y, 0f));
        Vector3 c = viewCamera.WorldToScreenPoint(sprite.transform.TransformPoint(max.x, max.y, 0f));
        Vector3 d = viewCamera.WorldToScreenPoint(sprite.transform.TransformPoint(min.x, max.y, 0f));
        if (a.z <= 0f || b.z <= 0f || c.z <= 0f || d.z <= 0f) return;
        if (!InsideQuad(position, a, b, c, d)) return;
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
        ref WhiteboxPopulation closest, ref float nearestDepth)
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
        const float padding = 7f;
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
        NewText("FundsPlaceholder", root, "生物能：未接入", 22, new Color(0.15f, 0.18f, 0.19f),
            new Vector2(24f, -12f), new Vector2(260f, 42f), false);
        Text news = NewText("NewsPlaceholder", root, "生态新闻：未接入", 22,
            new Color(0.15f, 0.18f, 0.19f), new Vector2(330f, -12f),
            new Vector2(490f, 42f), false);
        news.alignment = TextAnchor.MiddleCenter;
        dayText = NewText("Day", root, "Day 0", 23, new Color(0.15f, 0.18f, 0.19f),
            new Vector2(860f, -12f), new Vector2(160f, 42f), false);
        dayText.rectTransform.anchorMin = dayText.rectTransform.anchorMax = Vector2.one;
        dayText.rectTransform.pivot = Vector2.one;
        dayText.rectTransform.anchoredPosition = new Vector2(-155f, -12f);
        speedText = NewText("Speed", root, "1X", 23, new Color(0.15f, 0.18f, 0.19f),
            new Vector2(0f, 0f), new Vector2(95f, 42f), false);
        speedText.rectTransform.anchorMin = speedText.rectTransform.anchorMax = Vector2.one;
        speedText.rectTransform.pivot = Vector2.one;
        speedText.rectTransform.anchoredPosition = new Vector2(-24f, -12f);
    }

    private void BuildWindows()
    {
        float width = Mathf.Max(320f, canvasRect.rect.width);
        RectTransform tile = NewPanel("TileInformation", new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(Mathf.Min(790f, width * 0.57f), 366f),
            "GameUI/TilePanel", new Rect(180f, 455f, 1690f, 710f));
        tileWindow = tile.gameObject.AddComponent<InformationWindow>();
        tileText = NewText("TileDetails", tile, "", 23, Ink(),
            new Vector2(96f, -67f), new Vector2(tile.rect.width - 180f, tile.rect.height - 112f), true);
        tileText.GetComponent<InformationTextDragHandle>().Initialize(tileWindow);
        tileText.lineSpacing = 1.25f;
        NewCloseButton(tile, () => { tileWindow.Close(); selectedTile = null; });
        tileWindow.Initialize(canvasRect, new Vector2(28f, -139f));

        RectTransform population = NewPanel("PopulationInformation", new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(Mathf.Min(440f, width * 0.37f), 690f),
            "GameUI/PopulationPanel", new Rect(500f, 215f, 1050f, 1720f));
        populationWindow = population.gameObject.AddComponent<InformationWindow>();
        populationText = NewText("PopulationDetails", population, "", 22, Ink(),
            new Vector2(45f, -133f), new Vector2(population.rect.width - 90f, population.rect.height - 180f), true);
        populationText.GetComponent<InformationTextDragHandle>().Initialize(populationWindow);
        populationText.lineSpacing = 1.22f;
        NewCloseButton(population, () =>
        {
            populationWindow.Close(); selectedVisual = null; selectedPopulationId = null;
        }, 72f);
        populationWindow.Initialize(canvasRect, new Vector2(-26f, -130f));
    }

    private RectTransform NewPanel(string name, Vector2 anchor, Vector2 pivot,
        Vector2 size, string resource, Rect crop)
    {
        RectTransform rect = NewRect(name, canvasRect, anchor, pivot, size, Vector2.zero);
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

    private void NewCloseButton(RectTransform parent, UnityEngine.Events.UnityAction onClick,
        float topInset = 22f)
    {
        RectTransform buttonRect = NewRect("Close", parent, new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(42f, 42f), new Vector2(-23f, -topInset));
        UnityEngine.UI.Image image = buttonRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.16f, 0.21f, 0.22f, 0.9f);
        UnityEngine.UI.Button button = buttonRect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.onClick.AddListener(onClick);
        Text label = NewText("X", buttonRect, "×", 30, Color.white,
            new Vector2(0f, 0f), new Vector2(42f, 42f), false);
        label.alignment = TextAnchor.MiddleCenter;
    }

    private Text NewText(string name, RectTransform parent, string value, int size,
        Color color, Vector2 position, Vector2 dimensions, bool dragHandle)
    {
        RectTransform rect = NewRect(name, parent, new Vector2(0f, 1f),
            new Vector2(0f, 1f), dimensions, position);
        Text label = rect.gameObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = size;
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
        if (dayText != null) dayText.text = "Day " + (clock == null ? "--" : clock.currentDay.ToString());
        if (speedText != null) speedText.text = clock == null ? "--" :
            clock.IsPaused ? "暂停" : clock.Speed.ToString("0.#") + "X";
        if (tileWindow != null && tileWindow.IsOpen) RefreshTile();
        if (populationWindow != null && populationWindow.IsOpen) RefreshPopulation();
    }

    private void RefreshTile()
    {
        if (tileText == null) return;
        if (selectedTile == null)
        {
            tileText.text = "该地块已不在场景中";
            return;
        }
        BlockInfo block = selectedTile.Block;
        if (block == null) { tileText.text = "地块数据暂不可用"; return; }
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
        tileText.text = $"地块信息  ({selectedTile.Coordinate.x}, {selectedTile.Coordinate.y})\n" +
            $"地形：{terrain}    海拔：{elevation}\n" +
            $"温度：{temperature}    湿度：{humidity}\n" +
            $"植物库存：{plants} / {capacity}\n" +
            $"每日恢复：{recovery}    种群：{groups}\n" +
            $"水域类型：{block.waterCoverage}";
    }

    private void RefreshPopulation()
    {
        if (populationText == null) return;
        if (selectedVisual == null)
        {
            populationText.text = "该种群已不在场景中";
            return;
        }
        PopulationData data = selectedVisual.EcologicalPopulation;
        string currentId = data != null ? data.PopulationId : selectedVisual.UniqueId;
        if (currentId != selectedPopulationId)
        {
            populationText.text = "该种群已迁出或消失";
            return;
        }
        if (data == null)
        {
            populationText.text = $"测试种群\n\n种群 ID：{selectedVisual.UniqueId}\n" +
                $"所属地块：{selectedVisual.TileId}\n数量：{selectedVisual.Count}\n" +
                "生态数据：未接入";
            return;
        }
        string name = data.species != null ? data.species.SpeciesName :
            string.IsNullOrEmpty(data.lineageName) ? "未命名种群" : data.lineageName;
        populationText.text = $"{name}\n\n种群 ID：{data.PopulationId}\n" +
            $"所属地块：{selectedVisual.TileId}\n数量：{data.speciesAmount}\n" +
            $"今日出生 / 死亡：{data.birthsToday} / {data.deathsToday}\n" +
            $"生态位：{data.ecologicalNiche}    营养级：L{data.trophicLevel}\n" +
            $"适温 / 适湿：{data.fitTemperature} / {data.fitHumidity}\n" +
            $"体型 / 运动：{data.size} / {data.movementAbility}\n" +
            $"繁殖：{data.fertility}    适应度：{data.environmentalFitness}\n" +
            $"承载量 K：{data.carryingCapacity:F0}\n\n物种描述：未接入";
    }
}
