using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Binds the two authored WhiteBox panels. No map GameObjects are created at runtime.
[RequireComponent(typeof(Canvas))]
[DefaultExecutionOrder(50)]
public sealed class SpeciesMapsUI : MonoBehaviour
{
    [System.Serializable] private sealed class MapLink { public string from, to; }
    [System.Serializable] private sealed class MapLinks { public MapLink[] evolutionLinks, foodLinks; }
    [System.Serializable] private sealed class MapPage
    {
        public GameObject panel;
        public RectTransform viewport, content;
        public SpeciesMapGraphGraphic graph;
        public Button switchButton, closeButton;
        public SpeciesMapNodeView[] nodes;
        [System.NonSerialized] public float zoom;
    }

    [SerializeField] private GameObject overlay;
    [SerializeField] private MapPage[] pages = new MapPage[2];
    [SerializeField, Min(1f)] private float defaultZoomMultiplier = 1f;
    [SerializeField, Min(1f)] private float maxZoomMultiplier = 2.2f;
    [SerializeField] private float keyboardPanSpeed = 650f;

    private readonly Dictionary<SpeciesData, int> indices = new Dictionary<SpeciesData, int>();
    private readonly Dictionary<SpeciesData, HashSet<SpeciesData>> evolutionNeighbors =
        new Dictionary<SpeciesData, HashSet<SpeciesData>>();
    private readonly List<RaycastResult> hits = new List<RaycastResult>();
    private SimulationTime clock;
    private Canvas canvas;
    private bool open, dragging;
    private int activePage;
    private Vector2 lastPointer;
    private RectTransform cluePopup;
    private InformationWindow clueWindow;
    private Canvas clueCanvas;
    private TextMeshProUGUI clueTitle;
    private TextMeshProUGUI clueText;
    private int touchId = -1;
    private float pinchDistance;
    private float suppressNodeClicksUntil;
    private SpeciesMapNodeView clueNode;
    private float nextClueRefresh;
    private int displayedTraitMask = -1;
    private bool displayedSpeciesUnlocked;
    private Vector2 lastCanvasSize;
    public bool IsOpen => open;
    public bool SuppressNodeClicks => Time.unscaledTime < suppressNodeClicksUntil;

    private void Start()
    {
        canvas = GetComponent<Canvas>();
        clock = FindFirstObjectByType<SimulationTime>();
        WhiteboxBiowebTestSetup setup = FindFirstObjectByType<WhiteboxBiowebTestSetup>();
        if (setup == null || setup.MapSpecies == null || overlay == null || pages == null || pages.Length != 2 ||
            pages[0] == null || pages[1] == null)
        {
            Debug.LogError("Species map scene references are incomplete.", this);
            enabled = false;
            return;
        }
        for (int i = 0; i < setup.MapSpecies.Count; i++)
            if (setup.MapSpecies[i] != null) indices[setup.MapSpecies[i]] = i;

        TextAsset topologyAsset = Resources.Load<TextAsset>("BiowebMapLinks");
        MapLinks topology = topologyAsset != null ? JsonUtility.FromJson<MapLinks>(topologyAsset.text) : null;
        if (topology == null) Debug.LogError("BiowebMapLinks resource is missing.", this);
        var speciesById = new Dictionary<string, SpeciesData>();
        foreach (SpeciesData item in setup.MapSpecies)
            if (item != null) speciesById[item.SpeciesId.Replace("-", "")] = item;
        BuildEvolutionNeighbors(topology?.evolutionLinks, speciesById);

        Canvas.ForceUpdateCanvases();
        for (int map = 0; map < 2; map++)
        {
            MapPage page = pages[map];
            if (page.panel == null || page.viewport == null || page.content == null || page.graph == null ||
                page.switchButton == null || page.closeButton == null || page.nodes == null)
            {
                Debug.LogError("Species map panel references are incomplete.", this);
                enabled = false;
                return;
            }
            page.zoom = MinimumZoom(page) * defaultZoomMultiplier;
            page.content.localScale = Vector3.one * page.zoom;
            foreach (SpeciesMapNodeView node in page.nodes)
                if (node != null) node.Bind(Hovered, ShowClue);
            page.switchButton.onClick.AddListener(Switch);
            page.closeButton.onClick.AddListener(Close);
            ConfigureGraph(page, map == 1 ? topology?.foodLinks : topology?.evolutionLinks, speciesById);
        }
        CreateCluePopup();
        overlay.SetActive(false);
    }

    private void OnDestroy()
    {
        DarwinAudioDispatch.SetMenuOpen("SpeciesMaps", false);
        if (open && clock != null) clock.ReleaseModalPause();
    }

    private void OnDisable()
    {
        HideClue();
        if (!open) return;
        open = dragging = false;
        DarwinAudioDispatch.SetMenuOpen("SpeciesMaps", false);
        if (overlay != null) overlay.SetActive(false);
        if (clock != null) clock.ReleaseModalPause();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.mKey.wasPressedThisFrame)
        {
            if (open) Close(); else Open();
        }
        if (!open) return;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }
        MapPage page = pages[activePage];
        Mouse mouse = Mouse.current;
        bool touching = UpdateTouches(page);
        if (mouse != null && !touching)
        {
            Vector2 pointer = mouse.position.ReadValue();
            if (RectTransformUtility.RectangleContainsScreenPoint(page.viewport, pointer, canvas.worldCamera))
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > .01f) SetZoom(page, page.zoom * (scroll > 0f ? 1.1f : 1f / 1.1f));
                bool started = mouse.middleButton.wasPressedThisFrame ||
                    (mouse.leftButton.wasPressedThisFrame && IsBlank(pointer));
                if (started) { dragging = true; lastPointer = pointer; }
            }
            if (!mouse.middleButton.isPressed && !mouse.leftButton.isPressed) dragging = false;
            if (dragging)
            {
                page.content.anchoredPosition += (pointer - lastPointer) / Mathf.Max(.01f, canvas.scaleFactor);
                lastPointer = pointer;
                Clamp(page);
            }
        }
        if (keyboard != null)
        {
            Vector2 direction = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            if (direction.sqrMagnitude > 0f)
            {
                page.content.anchoredPosition += direction.normalized * keyboardPanSpeed * Time.unscaledDeltaTime;
                Clamp(page);
            }
        }
    }

    private void LateUpdate()
    {
        if (clueWindow != null && clueWindow.IsOpen)
        {
            RectTransform bounds = (RectTransform)transform;
            if (bounds.rect.size != lastCanvasSize)
            {
                lastCanvasSize = bounds.rect.size;
                clueWindow.SetDragBounds(bounds, cluePopup.anchoredPosition);
            }
            if (clueNode != null)
                RefreshOpenClue(false);
        }
        if (!open) return;
        MapPage page = pages[activePage];
        float minimum = MinimumZoom(page);
        if (page.zoom < minimum || page.zoom > minimum * Mathf.Max(1f, maxZoomMultiplier))
            SetZoom(page, page.zoom);
        else
            Clamp(page);
    }

    public void Open()
    {
        if (overlay == null || open || !enabled) return;
        if (!TutorialFlowController.Allows(TutorialFlowController.Feature.SpeciesMap)) return;
        var journal = FindFirstObjectByType<DarwinJournalCanvas>();
        var discovery = FindFirstObjectByType<SpeciesUnlockPresenter>();
        if ((journal != null && !journal.IsGameplayVisible) || (discovery != null && discovery.IsPresenting)) return;
        var placement = FindFirstObjectByType<MapTilePlacementController>();
        if (placement != null && placement.IsDragging) placement.CancelDrag();
        FindFirstObjectByType<MapTouchInput>()?.CancelGesture();
        open = true;
        TutorialFlowController.Report("openMap");
        DarwinAudioDispatch.SetMenuOpen("SpeciesMaps", true);
        activePage = 0;
        overlay.SetActive(true);
        // Unity initializes an inactive nested Canvas on first activation.
        Canvas modalCanvas = overlay.GetComponent<Canvas>();
        if (modalCanvas != null) { modalCanvas.overrideSorting = true; modalCanvas.sortingOrder = 300; }
        if (clueCanvas != null) clueCanvas.sortingOrder = 310;
        pages[0].panel.SetActive(true);
        pages[1].panel.SetActive(false);
        foreach (MapPage page in pages)
            foreach (SpeciesMapNodeView node in page.nodes)
                if (node != null) node.Refresh();
        if (clock != null) clock.AcquireModalPause();
        Clamp(pages[0]);
    }

    public void Close()
    {
        if (!open) return;
        open = dragging = false;
        TutorialFlowController.Report("closeMap");
        DarwinAudioDispatch.SetMenuOpen("SpeciesMaps", false);
        touchId = -1;
        pinchDistance = 0f;
        overlay.SetActive(false);
        if (clueCanvas != null) clueCanvas.sortingOrder = 210;
        if (clock != null) clock.ReleaseModalPause();
    }

    public void Toggle() { if (open) Close(); else Open(); }

    // Touch positions are in screen pixels, matching the desktop pointer path.
    private bool UpdateTouches(MapPage page)
    {
        Touchscreen screen = Touchscreen.current;
        if (screen == null) return false;
        UnityEngine.InputSystem.Controls.TouchControl first = null, second = null;
        foreach (var contact in screen.touches)
        {
            if (!contact.press.isPressed) continue;
            if (first == null) first = contact;
            else { second = contact; break; }
        }
        if (first == null) { touchId = -1; pinchDistance = 0f; return false; }
        Vector2 position = first.position.ReadValue();
        if (second != null)
        {
            Vector2 other = second.position.ReadValue();
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(page.viewport, position, canvas.worldCamera) &&
                RectTransformUtility.RectangleContainsScreenPoint(page.viewport, other, canvas.worldCamera);
            float distance = Vector2.Distance(position, other);
            if (inside && pinchDistance > 1f) SetZoom(page, page.zoom * distance / pinchDistance);
            pinchDistance = inside ? distance : 0f;
            touchId = -1; dragging = false;
            suppressNodeClicksUntil = Time.unscaledTime + .3f;
            return true;
        }
        if (pinchDistance > 0f) { pinchDistance = 0f; touchId = -1; }
        int id = first.touchId.ReadValue();
        if (touchId != id)
        {
            touchId = id; lastPointer = position;
            dragging = first.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Began &&
                RectTransformUtility.RectangleContainsScreenPoint(page.viewport, position, canvas.worldCamera) && IsBlank(position);
        }
        if (dragging)
        {
            page.content.anchoredPosition += (position - lastPointer) / Mathf.Max(.01f, canvas.scaleFactor);
            lastPointer = position;
            Clamp(page);
        }
        return true;
    }

    private void Switch()
    {
        pages[activePage].graph.SetHovered(-1);
        pages[activePage].panel.SetActive(false);
        activePage = 1 - activePage;
        pages[activePage].panel.SetActive(true);
        dragging = false;
        Clamp(pages[activePage]);
    }

    private void Hovered(SpeciesData species)
    {
        pages[activePage].graph.SetHovered(species != null && indices.TryGetValue(species, out int index) ? index : -1);
    }

    private void CreateCluePopup()
    {
        var root = new GameObject("SpeciesCluePopup", typeof(RectTransform), typeof(Canvas),
            typeof(GraphicRaycaster), typeof(Image), typeof(InformationWindow));
        cluePopup = (RectTransform)root.transform;
        cluePopup.SetParent(transform, false);
        cluePopup.anchorMin = cluePopup.anchorMax = new Vector2(.5f, .5f);
        cluePopup.sizeDelta = new Vector2(720f, 480f);
        clueCanvas = root.GetComponent<Canvas>();
        clueCanvas.overrideSorting = true;
        clueCanvas.sortingOrder = open ? 310 : 210;
        clueWindow = root.GetComponent<InformationWindow>();
        Image paper = root.GetComponent<Image>();
        paper.sprite = Resources.Load<Sprite>("GameUI/SpeciesCluePaper");
        paper.type = Image.Type.Simple;
        paper.color = paper.sprite != null ? Color.white : new Color(.96f, .93f, .82f);
        paper.raycastTarget = true;

        var titleObject = new GameObject("ClueTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform titleRect = (RectTransform)titleObject.transform;
        titleRect.SetParent(cluePopup, false);
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = Vector2.one;
        titleRect.pivot = new Vector2(.5f, 1f);
        titleRect.offsetMin = new Vector2(76f, -112f);
        titleRect.offsetMax = new Vector2(-76f, -58f);
        clueTitle = titleObject.GetComponent<TextMeshProUGUI>();
        clueTitle.font = GameUITheme.HandwrittenTMPFont;
        clueTitle.fontSize = 34f;
        clueTitle.color = new Color(.12f, .17f, .13f);
        clueTitle.alignment = TextAlignmentOptions.Center;
        clueTitle.text = "？？？的培育线索";
        clueTitle.raycastTarget = false;

        var closeObject = new GameObject("CloseClue", typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform closeRect = (RectTransform)closeObject.transform;
        closeRect.SetParent(cluePopup, false);
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one;
        closeRect.anchoredPosition = new Vector2(-35f, -29f);
        closeRect.sizeDelta = new Vector2(52f, 52f);
        Image closeBackground = closeObject.GetComponent<Image>();
        closeBackground.color = new Color(.96f, .92f, .78f, .9f);
        Button closeButton = closeObject.GetComponent<Button>();
        closeButton.targetGraphic = closeBackground;
        closeButton.onClick.AddListener(HideClue);
        var closeGlyph = new GameObject("Cross", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform crossRect = (RectTransform)closeGlyph.transform;
        crossRect.SetParent(closeRect, false);
        crossRect.anchorMin = Vector2.zero;
        crossRect.anchorMax = Vector2.one;
        crossRect.offsetMin = crossRect.offsetMax = Vector2.zero;
        TextMeshProUGUI crossText = closeGlyph.GetComponent<TextMeshProUGUI>();
        crossText.font = GameUITheme.TMPFont;
        crossText.fontSize = 38f;
        crossText.alignment = TextAlignmentOptions.Center;
        crossText.color = new Color(.22f, .28f, .18f);
        crossText.text = "×";
        crossText.raycastTarget = false;

        var textObject = new GameObject("BreedingClue", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.SetParent(cluePopup, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(76f, 64f);
        textRect.offsetMax = new Vector2(-76f, -130f);
        clueText = textObject.GetComponent<TextMeshProUGUI>();
        clueText.font = GameUITheme.HandwrittenTMPFont;
        clueText.fontSize = GameUITheme.ReadableFontSize(22);
        clueText.enableAutoSizing = true;
        clueText.fontSizeMin = 22f;
        clueText.fontSizeMax = 28f;
        clueText.color = new Color(.12f, .17f, .13f);
        clueText.textWrappingMode = TextWrappingModes.Normal;
        clueText.overflowMode = TextOverflowModes.Truncate;
        clueText.alignment = TextAlignmentOptions.MidlineLeft;
        clueText.richText = true;
        clueText.raycastTarget = false;
        clueWindow.Initialize((RectTransform)transform, Vector2.zero);
    }

    private static string ClueTitle(SpeciesData species) =>
        (species != null && species.HasEverUnlocked ? species.SpeciesName : "？？？") + "的培育线索";

    private void RefreshOpenClue(bool force)
    {
        if (clueNode == null || clueNode.Species == null ||
            (!force && Time.unscaledTime < nextClueRefresh)) return;
        nextClueRefresh = Time.unscaledTime + .5f;
        SpeciesData species = clueNode.Species;
        int traitMask = species.RevealedTraitMask;
        bool unlocked = species.HasEverUnlocked;
        if (force || traitMask != displayedTraitMask)
        {
            clueText.text = species.FormattedBreedingClue;
            displayedTraitMask = traitMask;
        }
        if (force || unlocked != displayedSpeciesUnlocked)
        {
            clueTitle.text = ClueTitle(species);
            displayedSpeciesUnlocked = unlocked;
        }
    }

    private void ShowClue(SpeciesMapNodeView node)
    {
        if (!open || node == null || node.Species == null || cluePopup == null) return;
        if (!CanShowClue(node.Species)) { HideClue(); return; }
        TutorialFlowController.ReportColorClue(node.Species);
        bool newNode = clueNode != node || !clueWindow.IsOpen;
        clueNode = node;
        RefreshOpenClue(newNode);
        RectTransform parentRect = (RectTransform)transform;
        Vector2 anchorOrigin = parentRect.rect.center;
        Rect bounds = new Rect(-parentRect.rect.size * .5f, parentRect.rect.size);
        float width = Mathf.Min(720f, bounds.width - 32f);
        float textHeight = clueText.GetPreferredValues(clueText.text, width - 152f, 0f).y;
        cluePopup.sizeDelta = new Vector2(width,
            Mathf.Min(Mathf.Max(width * 2f / 3f, textHeight + 194f), bounds.height - 32f));
        Vector2 local = (Vector2)parentRect.InverseTransformPoint(node.transform.position) - anchorOrigin;
        Vector2 half = cluePopup.sizeDelta * .5f;
        float x = local.x + (local.x < 0f ? half.x + 70f : -half.x - 70f);
        Vector2 position = new Vector2(
            Mathf.Clamp(x, bounds.xMin + half.x, bounds.xMax - half.x),
            Mathf.Clamp(local.y, bounds.yMin + half.y, bounds.yMax - half.y));
        if (TutorialFlowController.TryGetBubbleScreenRect(out Rect bubbleScreen))
        {
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect,
                bubbleScreen.min, uiCamera, out Vector2 bubbleMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect,
                bubbleScreen.max, uiCamera, out Vector2 bubbleMax);
            bubbleMin -= anchorOrigin;
            bubbleMax -= anchorOrigin;
            Rect bubble = Rect.MinMaxRect(bubbleMin.x, bubbleMin.y, bubbleMax.x, bubbleMax.y);
            Rect proposed = Rect.MinMaxRect(position.x - half.x, position.y - half.y,
                position.x + half.x, position.y + half.y);
            if (proposed.Overlaps(bubble))
            {
                // The tutorial uses a separate, higher Canvas. Keep the clue above it.
                const float gap = 16f;
                float top = bounds.yMax - gap;
                float bottom = Mathf.Max(bounds.yMin + gap, bubble.yMax + gap);
                float availableHeight = Mathf.Max(0f, top - bottom);
                cluePopup.sizeDelta = new Vector2(width, Mathf.Min(cluePopup.sizeDelta.y, availableHeight));
                position.y = top - cluePopup.sizeDelta.y * .5f;
            }
        }
        if (newNode)
        {
            clueWindow.HideImmediately();
            clueWindow.Initialize(parentRect, position);
            lastCanvasSize = parentRect.rect.size;
            clueWindow.Open();
        }
        cluePopup.SetAsLastSibling();
    }

    private void HideClue()
    {
        if (clueWindow != null) clueWindow.HideImmediately();
        clueNode = null;
        displayedTraitMask = -1;
    }

    private void BuildEvolutionNeighbors(MapLink[] links, Dictionary<string, SpeciesData> speciesById)
    {
        evolutionNeighbors.Clear();
        if (links == null) return;
        foreach (MapLink link in links)
        {
            if (link == null || string.IsNullOrEmpty(link.from) || string.IsNullOrEmpty(link.to) ||
                !speciesById.TryGetValue(link.from.Replace("-", ""), out SpeciesData source) ||
                !speciesById.TryGetValue(link.to.Replace("-", ""), out SpeciesData target) ||
                source == target) continue;
            if (!evolutionNeighbors.TryGetValue(source, out HashSet<SpeciesData> from))
                evolutionNeighbors[source] = from = new HashSet<SpeciesData>();
            if (!evolutionNeighbors.TryGetValue(target, out HashSet<SpeciesData> to))
                evolutionNeighbors[target] = to = new HashSet<SpeciesData>();
            from.Add(target);
            to.Add(source);
        }
    }

    private bool CanShowClue(SpeciesData species)
    {
        if (species == null || !evolutionNeighbors.TryGetValue(species, out HashSet<SpeciesData> neighbors))
            return false;
        foreach (SpeciesData neighbor in neighbors)
            if (neighbor != null && neighbor.HasEverUnlocked) return true;
        return false;
    }

    private void ConfigureGraph(MapPage page, MapLink[] links, Dictionary<string, SpeciesData> speciesById)
    {
        var positions = new Dictionary<SpeciesData, Vector2>();
        foreach (SpeciesMapNodeView node in page.nodes)
            if (node != null && node.Species != null) positions[node.Species] = node.NodePosition;
        var edges = new List<SpeciesMapGraphGraphic.Edge>();
        if (links != null)
            foreach (MapLink link in links)
            {
                if (link == null || string.IsNullOrEmpty(link.from) || string.IsNullOrEmpty(link.to) ||
                    !speciesById.TryGetValue(link.from.Replace("-", ""), out SpeciesData source) ||
                    !speciesById.TryGetValue(link.to.Replace("-", ""), out SpeciesData target) ||
                    !positions.TryGetValue(source, out Vector2 start) ||
                    !positions.TryGetValue(target, out Vector2 end)) continue;
                edges.Add(new SpeciesMapGraphGraphic.Edge { from = start, to = end,
                    fromIndex = indices[source], toIndex = indices[target] });
            }
        page.graph.Configure(edges, page == pages[1]);
    }

    private bool IsBlank(Vector2 pointer)
    {
        if (EventSystem.current == null) return true;
        hits.Clear();
        var eventData = new PointerEventData(EventSystem.current) { position = pointer };
        EventSystem.current.RaycastAll(eventData, hits);
        foreach (RaycastResult hit in hits)
            if (hit.gameObject.GetComponentInParent<SpeciesMapNodeView>() != null ||
                cluePopup != null && hit.gameObject.transform.IsChildOf(cluePopup) ||
                hit.gameObject.GetComponentInParent<Button>() != null) return false;
        return true;
    }

    private void SetZoom(MapPage page, float value)
    {
        float minimum = MinimumZoom(page);
        page.zoom = Mathf.Clamp(value, minimum, minimum * Mathf.Max(1f, maxZoomMultiplier));
        page.content.localScale = Vector3.one * page.zoom;
        Clamp(page);
    }

    private float MinimumZoom(MapPage page)
    {
        Vector2 imageSize = page.content.rect.size;
        Vector2 screenSize = page.viewport.rect.size;
        if (imageSize.x <= 0f || imageSize.y <= 0f || screenSize.x <= 0f || screenSize.y <= 0f)
            return 1f;
        return Mathf.Min(screenSize.x / imageSize.x, screenSize.y / imageSize.y);
    }

    private static void Clamp(MapPage page)
    {
        if (page == null || page.viewport == null) return;
        Vector2 excess = Vector2.Max(Vector2.zero,
            (page.content.rect.size * page.zoom - page.viewport.rect.size) * .5f);
        Vector2 position = page.content.anchoredPosition;
        position.x = Mathf.Clamp(position.x, -excess.x, excess.x);
        position.y = Mathf.Clamp(position.y, -excess.y, excess.y);
        page.content.anchoredPosition = position;
    }
}
