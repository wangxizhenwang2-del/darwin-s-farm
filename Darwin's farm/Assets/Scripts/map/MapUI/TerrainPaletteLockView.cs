using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Runtime view for the authored tile palette item. Terrain must be discovered
// before its preset can be used for construction.
public sealed class TerrainPaletteLockView : MonoBehaviour
{
    private MapTileDefinition definition;
    private MapGridManager grid;
    private SimulationEnvironmentController environment;
    private TMP_Text label;
    private Image preview;
    private GameObject dashedBorder;
    private Color labelColor;

    public void Bind(MapTileDefinition tile)
    {
        definition = tile;
        grid = FindFirstObjectByType<MapGridManager>();
        environment = FindFirstObjectByType<SimulationEnvironmentController>();
        label = transform.Find("NameText")?.GetComponent<TMP_Text>();
        preview = transform.Find("ColorPreview")?.GetComponent<Image>();
        if (label != null) labelColor = label.color;
        CreateDashedBorder();
        if (grid != null) grid.TilesChanged += Refresh;
        if (environment != null)
        {
            environment.TerrainUnlocked += OnTerrainUnlocked;
            environment.WaterUnlocked += Refresh;
            environment.UnlocksRestored += Refresh;
        }
        Refresh();
    }

    private void OnDestroy()
    {
        if (grid != null) grid.TilesChanged -= Refresh;
        if (environment != null)
        {
            environment.TerrainUnlocked -= OnTerrainUnlocked;
            environment.WaterUnlocked -= Refresh;
            environment.UnlocksRestored -= Refresh;
        }
    }

    private void OnTerrainUnlocked(DarwinFarm.Environment.TerrainKind _, Vector2Int __) => Refresh();

    private void Refresh()
    {
        if (definition == null) return;
        bool unlocked = grid != null && grid.HasPlacedTile &&
            environment != null && environment.IsUnlocked(definition);
        if (label != null)
        {
            label.text = unlocked ? definition.displayName : definition.displayName + "\n未解锁";
            label.color = unlocked ? labelColor : new Color(.72f, .73f, .70f);
            label.fontSize = unlocked ? 28f : 20f;
        }
        if (preview != null)
            preview.color = unlocked ? definition.mainColor :
                Color.Lerp(definition.mainColor, new Color(.23f, .23f, .23f), .72f);
        if (dashedBorder != null) dashedBorder.SetActive(!unlocked);
    }

    private void CreateDashedBorder()
    {
        dashedBorder = new GameObject("LockedDashedBorder", typeof(RectTransform),
            typeof(DashedBorderGraphic));
        dashedBorder.transform.SetParent(transform, false);
        var root = (RectTransform)dashedBorder.transform;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = new Vector2(4f, 4f);
        root.offsetMax = new Vector2(-4f, -4f);
        var border = dashedBorder.GetComponent<DashedBorderGraphic>();
        border.color = new Color(.78f, .79f, .75f, .9f);
        border.raycastTarget = false;
    }
}

// Draw the full dashed frame as one UI mesh instead of dozens of child images.
public sealed class DashedBorderGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect bounds = rectTransform.rect;
        const float dash = 10f;
        const float gap = 7f;
        const float thickness = 2f;
        for (float x = bounds.xMin; x < bounds.xMax; x += dash + gap)
        {
            float right = Mathf.Min(x + dash, bounds.xMax);
            Quad(helper, x, bounds.yMin, right, bounds.yMin + thickness);
            Quad(helper, x, bounds.yMax - thickness, right, bounds.yMax);
        }
        for (float y = bounds.yMin + gap; y < bounds.yMax - gap; y += dash + gap)
        {
            float top = Mathf.Min(y + dash, bounds.yMax - gap);
            Quad(helper, bounds.xMin, y, bounds.xMin + thickness, top);
            Quad(helper, bounds.xMax - thickness, y, bounds.xMax, top);
        }
    }

    private void Quad(VertexHelper helper, float left, float bottom, float right, float top)
    {
        int index = helper.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = new Vector3(left, bottom);
        helper.AddVert(vertex);
        vertex.position = new Vector3(left, top);
        helper.AddVert(vertex);
        vertex.position = new Vector3(right, top);
        helper.AddVert(vertex);
        vertex.position = new Vector3(right, bottom);
        helper.AddVert(vertex);
        helper.AddTriangle(index, index + 1, index + 2);
        helper.AddTriangle(index, index + 2, index + 3);
    }
}
