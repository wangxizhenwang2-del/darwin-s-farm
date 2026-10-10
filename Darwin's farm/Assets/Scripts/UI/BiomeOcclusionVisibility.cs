using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Presentation only: biome obstacle colliders and NavMesh modifiers are never changed.
public sealed class BiomeOcclusionVisibility : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float scanInterval = 0.12f;
    [SerializeField, Min(0.1f)] private float fadeSeconds = 0.3f;
    [SerializeField, Range(0.05f, 0.8f)] private float occludedOpacity = 0.22f;
    [SerializeField, Min(0f)] private float markerHeight = 1.35f;

    private sealed class Decoration
    {
        public Renderer renderer;
        public Material[] original;
        public Material[] faded;
        public float opacity = 1f;
        public bool blocksView;
        public bool usingFade;
    }

    private sealed class Marker
    {
        public RectTransform rect;
        public Text text;
        public WhiteboxPopulation population;
    }

    private readonly List<Decoration> decorations = new List<Decoration>();
    private readonly Dictionary<WhiteboxPopulation, Marker> markers =
        new Dictionary<WhiteboxPopulation, Marker>();
    private readonly HashSet<WhiteboxPopulation> blockedPopulations =
        new HashSet<WhiteboxPopulation>();
    private readonly HashSet<WhiteboxPopulation> livePopulations =
        new HashSet<WhiteboxPopulation>();
    private readonly List<WhiteboxPopulation> staleMarkers =
        new List<WhiteboxPopulation>();
    private Camera viewCamera;
    private MapGridManager grid;
    private PopulationMovementController movement;
    private RectTransform canvasRect;
    private Font font;
    private Shader transparentShader;
    private float nextScan;
    private float nextCache;

    public void Initialize(Camera camera, MapGridManager map,
        PopulationMovementController populations, RectTransform canvas, Font labelFont)
    {
        viewCamera = camera;
        grid = map;
        movement = populations;
        canvasRect = canvas;
        font = labelFont;
        transparentShader = Shader.Find("Universal Render Pipeline/Unlit");
        RebuildDecorationCache();
    }

    private void Update()
    {
        if (viewCamera == null || grid == null || movement == null || canvasRect == null) return;
        if (Time.unscaledTime >= nextCache)
        {
            nextCache = Time.unscaledTime + 1f;
            RebuildDecorationCache();
        }
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + scanInterval;
            ScanSightLines();
        }
        float step = Time.unscaledDeltaTime / Mathf.Max(0.1f, fadeSeconds);
        foreach (Decoration item in decorations)
        {
            if (item.renderer == null) continue;
            float target = item.blocksView ? occludedOpacity : 1f;
            float previous = item.opacity;
            item.opacity = Mathf.MoveTowards(previous, target, step);
            if (item.opacity >= 0.999f)
            {
                if (item.usingFade)
                {
                    item.renderer.sharedMaterials = item.original;
                    item.usingFade = false;
                }
            }
            else if (transparentShader != null)
            {
                EnsureFadeMaterials(item);
                if (item.faded == null) continue;
                if (!item.usingFade)
                {
                    item.renderer.sharedMaterials = item.faded;
                    item.usingFade = true;
                }
                foreach (Material material in item.faded)
                {
                    if (material == null) continue;
                    Color color = material.GetColor("_BaseColor");
                    color.a = item.opacity;
                    material.SetColor("_BaseColor", color);
                }
            }
        }
        UpdateMarkers();
    }

    private void RebuildDecorationCache()
    {
        if (grid == null) return;
        var found = new HashSet<Renderer>();
        foreach (MapTileInstance tile in grid.GetPlacedTiles())
        {
            if (tile == null) continue;
            foreach (Transform child in tile.transform)
            {
                if (!child.name.StartsWith("BiomeVisual_")) continue;
                foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>(true))
                    if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                        found.Add(renderer);
            }
        }
        for (int i = decorations.Count - 1; i >= 0; i--)
        {
            Decoration item = decorations[i];
            if (item.renderer != null && found.Remove(item.renderer)) continue;
            Release(item);
            decorations.RemoveAt(i);
        }
        foreach (Renderer renderer in found)
            decorations.Add(new Decoration { renderer = renderer, original = renderer.sharedMaterials });
    }

    private void ScanSightLines()
    {
        blockedPopulations.Clear();
        foreach (Decoration item in decorations) item.blocksView = false;
        foreach (WhiteboxPopulation population in movement.Populations)
        {
            if (population == null || !population.gameObject.activeInHierarchy ||
                population.Leader == null || !population.Leader.gameObject.activeInHierarchy) continue;
            Vector3 target = population.Leader.position + Vector3.up * 0.45f;
            Vector3 screen = viewCamera.WorldToScreenPoint(target);
            if (screen.z <= 0f || !viewCamera.pixelRect.Contains(screen)) continue;
            Vector3 direction = target - viewCamera.transform.position;
            float distance = direction.magnitude;
            if (distance < 0.01f) continue;
            Ray ray = new Ray(viewCamera.transform.position, direction / distance);
            bool hidden = false;
            foreach (Decoration item in decorations)
            {
                if (item.renderer == null || !item.renderer.enabled) continue;
                if (!item.renderer.bounds.IntersectRay(ray, out float hit) ||
                    hit < 0f || hit >= distance - 0.15f) continue;
                item.blocksView = true;
                hidden = true;
            }
            if (hidden) blockedPopulations.Add(population);
        }
    }

    private void EnsureFadeMaterials(Decoration item)
    {
        if (item.faded != null) return;
        item.faded = new Material[item.original.Length];
        for (int i = 0; i < item.original.Length; i++)
        {
            Material source = item.original[i];
            if (source == null) continue;
            Material fade = new Material(transparentShader) { name = source.name + " (occlusion fade)" };
            Texture texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") :
                source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
            if (texture != null) fade.SetTexture("_BaseMap", texture);
            Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            fade.SetColor("_BaseColor", color);
            fade.SetFloat("_Surface", 1f);
            fade.SetFloat("_Blend", 0f);
            fade.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            fade.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            fade.SetFloat("_ZWrite", 0f);
            fade.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            fade.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            item.faded[i] = fade;
        }
    }

    private void UpdateMarkers()
    {
        livePopulations.Clear();
        foreach (WhiteboxPopulation population in movement.Populations)
        {
            if (population == null || !population.gameObject.activeInHierarchy ||
                population.Leader == null || !population.Leader.gameObject.activeInHierarchy) continue;
            livePopulations.Add(population);
            if (!markers.TryGetValue(population, out Marker marker))
                markers.Add(population, marker = CreateMarker(population));
            bool show = blockedPopulations.Contains(population);
            Vector3 screen = viewCamera.WorldToScreenPoint(population.Leader.position + Vector3.up * markerHeight);
            show &= screen.z > 0f && viewCamera.pixelRect.Contains(screen);
            marker.rect.gameObject.SetActive(show);
            if (!show) continue;
            marker.text.text = "◆ " + population.Count;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen,
                null, out Vector2 local);
            Rect rect = canvasRect.rect;
            marker.rect.anchoredPosition = new Vector2(
                Mathf.Clamp(local.x, rect.xMin + 55f, rect.xMax - 55f),
                Mathf.Clamp(local.y + 24f, rect.yMin + 20f, rect.yMax - 20f));
        }
        staleMarkers.Clear();
        foreach (KeyValuePair<WhiteboxPopulation, Marker> pair in markers)
            if (pair.Key == null || !livePopulations.Contains(pair.Key)) staleMarkers.Add(pair.Key);
        foreach (WhiteboxPopulation population in staleMarkers)
        {
            Marker marker = markers[population];
            if (marker.rect != null) Destroy(marker.rect.gameObject);
            markers.Remove(population);
        }
    }

    private Marker CreateMarker(WhiteboxPopulation population)
    {
        var root = new GameObject("OccludedPopulationMarker", typeof(RectTransform));
        root.transform.SetParent(canvasRect, false);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(110f, 32f);
        Text text = root.AddComponent<Text>();
        text.font = font;
        text.fontSize = 19;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(1f, 0.94f, 0.42f, 1f);
        text.raycastTarget = false;
        Outline outline = root.AddComponent<Outline>();
        outline.effectColor = new Color(0.08f, 0.1f, 0.08f, 0.95f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return new Marker { rect = rect, text = text, population = population };
    }

    public bool TryPickMarker(Vector2 screenPosition, out WhiteboxPopulation population)
    {
        population = null;
        if (canvasRect == null) return false;
        foreach (Marker marker in markers.Values)
        {
            if (marker.rect == null || !marker.rect.gameObject.activeInHierarchy ||
                marker.population == null) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(marker.rect, screenPosition)) continue;
            population = marker.population;
            return true;
        }
        return false;
    }

    private static void Release(Decoration item)
    {
        if (item.renderer != null && item.usingFade)
            item.renderer.sharedMaterials = item.original;
        if (item.faded == null) return;
        foreach (Material material in item.faded)
            if (material != null) Destroy(material);
        item.faded = null;
    }

    private void OnDestroy()
    {
        foreach (Decoration item in decorations) Release(item);
    }
}
