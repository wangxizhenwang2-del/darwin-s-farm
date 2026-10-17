using System.Collections.Generic;
using UnityEngine;

// Selection is owned by information windows; this component only draws their boundaries.
[DisallowMultipleComponent]
public sealed class SelectionOutlinePresenter : MonoBehaviour
{
    [SerializeField] private Material lineMaterial, spriteMaterial;
    [SerializeField] private Color tileColor = new Color(1f,.93f,.08f), populationColor = new Color(.15f,1f,1f);
    [SerializeField] private Color areaColor = new Color(1f,.9f,.25f,.7f);
    [SerializeField, Range(1f, 12f)] private float linePixels = 6f;
    private GameInformationUI information;
    private EnvironmentToolSession tools;
    private MapGridManager grid;
    private Camera viewCamera;
    private readonly Dictionary<MapTileInstance, LineRenderer> lines = new Dictionary<MapTileInstance, LineRenderer>();
    private readonly Dictionary<MapTileInstance, LineRenderer> contrastLines = new Dictionary<MapTileInstance, LineRenderer>();
    private readonly HashSet<MapTileInstance> wanted = new HashSet<MapTileInstance>();
    private readonly List<MapTileInstance> expired = new List<MapTileInstance>();
    private readonly List<SpriteRenderer> sources = new List<SpriteRenderer>(), outlines = new List<SpriteRenderer>();
    private WhiteboxPopulation population;
    private Material runtimeSpriteMaterial;
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private readonly Vector3[] points = new Vector3[32];
    private float nextGeometry;
    public int HighlightedTileCount => lines.Count;
    public int HighlightedSpriteCount => outlines.Count;
    private void Awake()
    {
        information = GetComponent<GameInformationUI>(); tools = GetComponent<EnvironmentToolSession>();
        grid = FindFirstObjectByType<MapGridManager>(); viewCamera = Camera.main;
        if (spriteMaterial != null)
        { runtimeSpriteMaterial = new Material(spriteMaterial); runtimeSpriteMaterial.SetColor("_OutlineColor", populationColor); }
    }
    private void LateUpdate()
    {
        if (information == null || !information.isActiveAndEnabled) { Clear(); return; }
        wanted.Clear();
        if ((information.TileIsOpen || (tools != null && tools.Active)) && information.SelectedTile != null)
            wanted.Add(information.SelectedTile);
        if (tools != null && tools.Active && tools.Preview != null && tools.Preview.IsValid && grid != null)
            foreach (Vector2Int coordinate in tools.Preview.Area)
                if (grid.TryGetTile(coordinate, out var tile)) wanted.Add(tile);
        expired.Clear();
        foreach (var pair in lines) if (pair.Key == null || !wanted.Contains(pair.Key)) expired.Add(pair.Key);
        foreach (var tile in expired)
        { if (lines[tile] != null) Destroy(lines[tile].gameObject); lines.Remove(tile); contrastLines.Remove(tile); }
        bool rebuild = Time.unscaledTime >= nextGeometry;
        if (rebuild) nextGeometry = Time.unscaledTime + .2f;
        foreach (var tile in wanted)
        {
            bool created = !lines.TryGetValue(tile, out var line);
            if (created)
            {
                var go = new GameObject("SelectionBoundary"); go.transform.SetParent(tile.transform, false);
                line = go.AddComponent<LineRenderer>(); line.sharedMaterial = lineMaterial;
                line.useWorldSpace = true; line.loop = true; line.positionCount = points.Length;
                line.numCornerVertices = 3; line.numCapVertices = 2;
                line.sortingOrder = 2;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
                lines.Add(tile,line);
                var backing = new GameObject("BoundaryContrast"); backing.transform.SetParent(go.transform, false);
                var contrast = backing.AddComponent<LineRenderer>(); contrast.sharedMaterial = lineMaterial;
                contrast.useWorldSpace = true; contrast.loop = true; contrast.positionCount = points.Length;
                contrast.numCornerVertices = 3; contrast.numCapVertices = 2; contrast.sortingOrder = 1;
                contrast.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; contrast.receiveShadows = false;
                contrastLines.Add(tile, contrast);
            }
            var backingLine = contrastLines[tile];
            if (created || rebuild) { SetTileGeometry(tile,line); backingLine.SetPositions(points); }
            Color color = tile == information.SelectedTile ? tileColor : areaColor;
            line.startColor = line.endColor = color;
            backingLine.startColor = backingLine.endColor = new Color(.06f,.09f,.06f,
                tile == information.SelectedTile ? 1f : .55f);
            float worldPerPixel = viewCamera == null ? .03f : viewCamera.orthographic ?
                2f * viewCamera.orthographicSize / Mathf.Max(1,viewCamera.pixelHeight) :
                2f * Vector3.Distance(viewCamera.transform.position,tile.transform.position) *
                Mathf.Tan(viewCamera.fieldOfView * Mathf.Deg2Rad * .5f) / Mathf.Max(1,viewCamera.pixelHeight);
            float width = tile == information.SelectedTile ? linePixels : linePixels * .75f;
            line.startWidth = line.endWidth = worldPerPixel * width;
            backingLine.startWidth = backingLine.endWidth = worldPerPixel * (width + 4f);
        }
        var selected = information.SelectedPopulation;
        if (selected != population) { ClearSprites(); population = selected; if (population != null) CreateSprites(); }
        for (int i=0;i<sources.Count;i++)
        {
            var source = sources[i]; var outline = outlines[i];
            if (outline == null) continue;
            outline.enabled = source != null && source.enabled && source.gameObject.activeInHierarchy;
            if (source == null) continue;
            outline.sprite = source.sprite; outline.flipX = source.flipX; outline.flipY = source.flipY;
            outline.color = new Color(1,1,1,source.color.a);
            outline.sortingLayerID = source.sortingLayerID; outline.sortingOrder = source.sortingOrder + 1;
        }
    }
    private void SetTileGeometry(MapTileInstance tile, LineRenderer line)
    {
        float half = MapGridManager.TileSize * .5f - .12f;
        Vector3 center = tile.transform.position;
        for (int i=0;i<points.Length;i++)
        {
            int edge=i/8; float t=(i%8)/8f;
            Vector3 offset = edge==0 ? new Vector3(Mathf.Lerp(-half,half,t),0,-half) :
                edge==1 ? new Vector3(half,0,Mathf.Lerp(-half,half,t)) :
                edge==2 ? new Vector3(Mathf.Lerp(half,-half,t),0,half) : new Vector3(-half,0,Mathf.Lerp(half,-half,t));
            Vector3 point=center+offset;
            float y = tile.Core != null ? tile.Core.bounds.max.y : center.y;
            int count=Physics.RaycastNonAlloc(new Ray(point+Vector3.up*100,Vector3.down),hits,200,LayerMask.GetMask("Ground"),QueryTriggerInteraction.Ignore);
            bool found=false;
            for(int h=0;h<count;h++) if(hits[h].collider.GetComponentInParent<MapTileInstance>()==tile && (!found || hits[h].point.y>y))
            { y=hits[h].point.y; found=true; }
            point.y=y+.07f; points[i]=point;
        }
        line.SetPositions(points);
    }
    private void CreateSprites()
    {
        if (runtimeSpriteMaterial == null) return;
        foreach(var source in population.GetComponentsInChildren<SpriteRenderer>(true))
        {
            var go=new GameObject("SelectionSilhouette"); go.layer=source.gameObject.layer;
            go.transform.SetParent(source.transform,false);
            var outline=go.AddComponent<SpriteRenderer>(); outline.sharedMaterial=runtimeSpriteMaterial;
            sources.Add(source); outlines.Add(outline);
        }
    }
    private void ClearSprites()
    { foreach(var outline in outlines) if(outline!=null) Destroy(outline.gameObject); sources.Clear(); outlines.Clear(); population=null; }
    private void Clear()
    { foreach(var line in lines.Values) if(line!=null) Destroy(line.gameObject); lines.Clear(); contrastLines.Clear(); ClearSprites(); }
    private void OnDisable() => Clear();
    private void OnDestroy() { if(runtimeSpriteMaterial!=null) Destroy(runtimeSpriteMaterial); }
}
