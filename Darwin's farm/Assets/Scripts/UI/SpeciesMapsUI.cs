using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Binds the two authored WhiteBox panels. No map GameObjects are created at runtime.
[RequireComponent(typeof(Canvas))]
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
    [SerializeField, Range(.35f, 1f)] private float defaultZoom = .85f;
    [SerializeField] private float minZoom = .5f, maxZoom = 1.6f;
    [SerializeField] private float keyboardPanSpeed = 650f;

    private readonly Dictionary<SpeciesData, int> indices = new Dictionary<SpeciesData, int>();
    private readonly List<RaycastResult> hits = new List<RaycastResult>();
    private SimulationTime clock;
    private Canvas canvas;
    private bool open, dragging;
    private int activePage;
    private Vector2 lastPointer;

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
            page.zoom = defaultZoom;
            page.content.localScale = Vector3.one * page.zoom;
            foreach (SpeciesMapNodeView node in page.nodes)
                if (node != null) node.Bind(Hovered);
            page.switchButton.onClick.AddListener(Switch);
            page.closeButton.onClick.AddListener(Close);
            ConfigureGraph(page, map == 1 ? topology?.foodLinks : topology?.evolutionLinks, speciesById);
        }
        overlay.SetActive(false);
    }

    private void OnDestroy()
    {
        if (open && clock != null) clock.ReleaseModalPause();
    }

    private void OnDisable()
    {
        if (!open) return;
        open = dragging = false;
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
        if (mouse != null)
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

    private void LateUpdate() { if (open) Clamp(pages[activePage]); }

    private void Open()
    {
        if (overlay == null) return;
        open = true;
        activePage = 0;
        overlay.SetActive(true);
        pages[0].panel.SetActive(true);
        pages[1].panel.SetActive(false);
        foreach (MapPage page in pages)
            foreach (SpeciesMapNodeView node in page.nodes)
                if (node != null) node.Refresh();
        if (clock != null) clock.AcquireModalPause();
        Clamp(pages[0]);
    }

    private void Close()
    {
        open = dragging = false;
        overlay.SetActive(false);
        if (clock != null) clock.ReleaseModalPause();
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
                hit.gameObject.GetComponentInParent<Button>() != null) return false;
        return true;
    }

    private void SetZoom(MapPage page, float value)
    {
        page.zoom = Mathf.Clamp(value, minZoom, maxZoom);
        page.content.localScale = Vector3.one * page.zoom;
        Clamp(page);
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
