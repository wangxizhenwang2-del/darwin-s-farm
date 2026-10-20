using System.Collections.Generic;
using UnityEngine;

// Follows the nearest central tile independently of clicked information/tool targets.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Canvas))]
public sealed class TileStatusPresenter : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [SerializeField] private MapGridManager grid;
    [SerializeField] private TileStatusView view;
    [Header("Show when zoomed in (separate hide threshold prevents flicker)")]
    [SerializeField, Range(1f, 179f)] private float showFieldOfView = 60f;
    [SerializeField, Range(1f, 179f)] private float hideFieldOfView = 66f;
    [SerializeField, Min(.1f)] private float showOrthographicSize = 40f;
    [SerializeField, Min(.1f)] private float hideOrthographicSize = 45f;
    [Header("Distances are fractions of the camera viewport height")]
    [SerializeField, Range(.05f, .5f)] private float centerRadius = .4f;
    [SerializeField, Range(0f, .1f)] private float switchMargin = .025f;
    [SerializeField, Min(0f)] private float switchDelay = .15f;
    [SerializeField] private Vector2 screenOffset = new Vector2(0f, 26f);
    private readonly List<MapTileInstance> tiles = new List<MapTileInstance>();
    private SimulationEnvironmentController environment;
    private Canvas canvas;
    private RectTransform canvasRect;
    private MapTileInstance pendingTile;
    private float pendingSince, nextRefresh;
    private bool zoomVisible, dirty = true;
    public MapTileInstance FocusedTile { get; private set; }
    public TileStatusView View => view;

    private void Awake() { Resolve(); EnsureLayout(); }

    private void Resolve()
    {
        canvas = GetComponent<Canvas>();
        canvasRect = (RectTransform)transform;
        if (viewCamera == null) viewCamera = Camera.main;
        if (grid == null) grid = FindFirstObjectByType<MapGridManager>();
        if (grid != null) environment = grid.GetComponent<SimulationEnvironmentController>();
    }

    // The Editor installer saves this hierarchy; the information UI also calls it for other scenes.
    public void EnsureLayout()
    {
        Resolve();
        if (view == null) view = GetComponentInChildren<TileStatusView>(true);
        if (view == null)
        {
            var root = new GameObject("TileStatus", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            view = root.AddComponent<TileStatusView>();
        }
        view.EnsureLayout();
        view.SetVisible(false);
    }

    private void OnEnable()
    {
        Resolve();
        if (grid != null) grid.TilesChanged += RefreshTiles;
        if (environment != null) environment.Changed += EnvironmentChanged;
        RefreshTiles();
    }

    private void OnDisable()
    {
        if (grid != null) grid.TilesChanged -= RefreshTiles;
        if (environment != null) environment.Changed -= EnvironmentChanged;
        FocusedTile = pendingTile = null;
        zoomVisible = false;
        if (view != null) view.SetVisible(false);
    }

    private void RefreshTiles()
    {
        tiles.Clear();
        if (grid != null) foreach (MapTileInstance tile in grid.GetPlacedTiles()) if (tile != null) tiles.Add(tile);
        dirty = true;
    }

    private void EnvironmentChanged(IReadOnlyList<EnvironmentReadSnapshot> snapshots) { dirty = true; }

    private void LateUpdate()
    {
        if (view == null || viewCamera == null || environment == null || !viewCamera.isActiveAndEnabled)
        { if (view != null) view.SetVisible(false); return; }
        zoomVisible = viewCamera.orthographic ?
            viewCamera.orthographicSize <= (zoomVisible ? hideOrthographicSize : showOrthographicSize) :
            viewCamera.fieldOfView <= (zoomVisible ? hideFieldOfView : showFieldOfView);
        if (!zoomVisible) { ClearFocus(); return; }

        MapTileInstance nearest = null;
        float closest = float.PositiveInfinity;
        foreach (MapTileInstance tile in tiles)
        {
            if (!TryDistance(tile, out float distance) || distance >= closest) continue;
            nearest = tile; closest = distance;
        }
        bool retainCurrent = TryDistance(FocusedTile, out float currentDistance, true);
        if (nearest == null)
        {
            if (!retainCurrent) { ClearFocus(); return; }
            nearest = FocusedTile;
        }
        if (!retainCurrent) SetFocus(nearest);
        else if (nearest != FocusedTile && closest + switchMargin < currentDistance)
        {
            if (pendingTile != nearest) { pendingTile = nearest; pendingSince = Time.unscaledTime; }
            if (Time.unscaledTime - pendingSince >= switchDelay) SetFocus(nearest);
        }
        else pendingTile = null;

        if (FocusedTile == null) return;
        if (dirty || Time.unscaledTime >= nextRefresh)
        {
            if (!environment.TryRead(FocusedTile.Coordinate, out var snapshot)) { ClearFocus(); return; }
            view.Show(snapshot);
            dirty = false;
            nextRefresh = Time.unscaledTime + .25f;
        }
        Vector3 screen = viewCamera.WorldToScreenPoint(TileAnchor(FocusedTile));
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 offset = screenOffset * canvas.scaleFactor;
        bool positioned = RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,
            (Vector2)screen + offset, uiCamera, out Vector2 local);
        if (positioned) ((RectTransform)view.transform).anchoredPosition = local - canvasRect.rect.center;
        view.SetVisible(positioned && screen.z > viewCamera.nearClipPlane);
    }

    private void ClearFocus()
    {
        FocusedTile = pendingTile = null;
        dirty = true;
        view.SetVisible(false);
    }

    private void SetFocus(MapTileInstance tile)
    {
        FocusedTile = tile;
        pendingTile = null;
        dirty = true;
    }

    private bool TryDistance(MapTileInstance tile, out float distance, bool retaining = false)
    {
        distance = float.PositiveInfinity;
        if (tile == null || !tile.gameObject.activeInHierarchy || tile.Block == null) return false;
        Vector3 screen = viewCamera.WorldToScreenPoint(TileAnchor(tile));
        Rect viewport = viewCamera.pixelRect;
        if (screen.z <= viewCamera.nearClipPlane || screen.z >= viewCamera.farClipPlane ||
            viewport.height <= 0f || !viewport.Contains(screen)) return false;
        distance = Vector2.Distance(screen, viewport.center) / viewport.height;
        return distance <= centerRadius + (retaining ? switchMargin : 0f);
    }

    private static Vector3 TileAnchor(MapTileInstance tile)
    {
        Vector3 point = tile.transform.position;
        if (tile.Core != null) point.y = tile.Core.bounds.max.y;
        return point;
    }
}
