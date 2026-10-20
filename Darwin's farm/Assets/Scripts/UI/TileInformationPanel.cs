using System.Collections.Generic;
using DarwinFarm.Environment;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Authored at a fixed paper aspect ratio. Resizing scales the whole composition,
// so the generated artwork, text, chart, and scroll controls never stretch.
[DisallowMultipleComponent]
public sealed class TileInformationPanel : MonoBehaviour
{
    public const float BoardWidth = 650f;
    public const float BoardHeight = 906f;
    public const float CompactHeight = 430f;
    private static readonly Color Ink = new Color(.12f, .18f, .12f);
    private static readonly Color Green = new Color(.31f, .47f, .25f);
    [SerializeField] private TileTrendChart chart;
    [SerializeField] private TextMeshProUGUI heading, temperature, humidity, plants, tools, logText;
    [SerializeField] private ScrollRect logScroll;
    [SerializeField] private Scrollbar logScrollbar;
    [SerializeField] private RectTransform grip;
    [SerializeField, Range(.55f, 1.45f)] private float userScale = 1f;
    [SerializeField, Range(.65f, 1.4f)] private float compactScale = .72f;
    [SerializeField] private bool compact;
    private bool lockFullLayout;
    private float startScale;
    private bool snappedDuringDrag;

    public void LockFullLayout(bool value)
    {
        lockFullLayout = value;
        if (value) SetCompact(false);
    }
    private RectTransform board;
    private RectTransform compactContents;
    private TextMeshProUGUI compactHeading, compactTemperature, compactHumidity, compactPlants, compactTools;
    private Canvas canvas;
    private BlockInfo lastBlock;
    private int lastLogCount = -1;

    public void Build()
    {
        board = (RectTransform)transform;
        board.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, BoardWidth);
        board.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, BoardHeight);
        var background = GetComponent<Image>();
        if (background == null) background = gameObject.AddComponent<Image>();
        background.sprite = Resources.Load<Sprite>("GameUI/TileInformationExtended");
        background.color = Color.white;
        background.type = Image.Type.Simple;
        background.preserveAspect = true;
        background.raycastTarget = true;

        RectTransform chartFrame = Rect("PopulationAndPlantsChart", board, 73, 119, 546, 229);
        Frame(chartFrame);
        Label("LeftAxisTitle", chartFrame, "现存\n种群\n数量", 17, 8, 69, 39, 86,
            TextAlignmentOptions.Center);
        Label("RightAxisTitle", chartFrame, "植\n物\n量", 17, 506, 81, 32, 78,
            TextAlignmentOptions.Center);
        RectTransform plot = Rect("Chart", chartFrame, 82, 38, 423, 129);
        chart = plot.gameObject.AddComponent<TileTrendChart>();
        chart.raycastTarget = false;
        var leftTop = Label("PopulationMaximum", chartFrame, "--", 14, 43, 30, 38, 24,
            TextAlignmentOptions.Right);
        var leftBottom = Label("PopulationZero", chartFrame, "0", 14, 43, 155, 38, 24,
            TextAlignmentOptions.Right);
        var rightTop = Label("PlantMaximum", chartFrame, "--", 14, 505, 30, 40, 24);
        var rightBottom = Label("PlantMinimum", chartFrame, "0", 14, 505, 155, 40, 24);
        var first = Label("FirstDay", chartFrame, "Day --", 14, 82, 171, 95, 25);
        var last = Label("LastDay", chartFrame, "Day --", 14, 411, 171, 95, 25,
            TextAlignmentOptions.Right);
        var legend = Label("SeriesLegend", chartFrame, "种群：左轴    植物量：右轴", 13,
            69, 198, 450, 27, TextAlignmentOptions.Center);
        legend.richText = true;
        var status = Label("ChartStatus", chartFrame, "", 16, 92, 83, 410, 36,
            TextAlignmentOptions.Center);
        chart.BindLabels(leftTop, leftBottom, rightTop, rightBottom, first, last, legend, status);

        heading = Label("TileHeading", board, "地块信息", 29, 88, 58, 500, 46,
            TextAlignmentOptions.Center);
        Rule("TitleRule", board, 81, 107, 534);

        RectTransform metrics = Rect("CurrentState", board, 78, 373, 536, 102);
        Frame(metrics);
        temperature = Label("Temperature", metrics, "温度\n--", 25, 16, 8, 162, 86,
            TextAlignmentOptions.Center);
        humidity = Label("Humidity", metrics, "湿度\n--", 25, 187, 8, 162, 86,
            TextAlignmentOptions.Center);
        plants = Label("Plants", metrics, "植物量\n--", 25, 356, 8, 162, 86,
            TextAlignmentOptions.Center);
        Rule("MetricSplit1", metrics, 177, 10, 1, 80);
        Rule("MetricSplit2", metrics, 351, 10, 1, 80);

        Label("ToolsHeading", board, "应用中", 23, 90, 485, 158, 36);
        Rule("ToolsRule", board, 241, 506, 369);
        RectTransform toolFrame = Rect("AppliedTools", board, 79, 523, 535, 102);
        Frame(toolFrame);
        tools = Label("ToolRows", toolFrame, "暂无正在应用的工具", 16, 16, 5, 505, 91);

        Label("LogHeading", board, "日志", 23, 90, 632, 158, 36);
        Rule("LogRule", board, 209, 652, 400);
        RectTransform logFrame = Rect("LogFrame", board, 79, 670, 535, 191);
        Frame(logFrame);
        RectTransform viewport = Rect("LogViewport", logFrame, 13, 10, 486, 171);
        var viewportHit = viewport.gameObject.AddComponent<Image>();
        viewportHit.color = new Color(1, 1, 1, .001f);
        viewportHit.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = Rect("LogContent", viewport, 0, 0, 476, 171);
        logText = Label("LogEntries", content, "暂无日志", 15, 0, 0, 476, 171);
        logText.textWrappingMode = TextWrappingModes.NoWrap;
        logText.overflowMode = TextOverflowModes.Overflow;
        logScroll = viewport.gameObject.AddComponent<ScrollRect>();
        logScroll.viewport = viewport;
        logScroll.content = content;
        logScroll.horizontal = false;
        logScroll.vertical = true;
        logScroll.movementType = ScrollRect.MovementType.Clamped;
        logScroll.scrollSensitivity = 28;
        RectTransform track = Rect("LogScrollbar", logFrame, 505, 10, 12, 171);
        track.gameObject.AddComponent<Image>().color = new Color(.48f, .62f, .40f, .36f);
        RectTransform thumb = Rect("Handle", track, 1, 1, 10, 34);
        var thumbImage = thumb.gameObject.AddComponent<Image>();
        thumbImage.color = new Color(.35f, .53f, .29f, .91f);
        logScrollbar = track.gameObject.AddComponent<Scrollbar>();
        logScrollbar.handleRect = thumb;
        logScrollbar.targetGraphic = thumbImage;
        logScrollbar.direction = Scrollbar.Direction.BottomToTop;
        logScrollbar.value = 1f;
        logScroll.verticalScrollbar = logScrollbar;
        logScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        grip = Rect("ResizeGrip", board, BoardWidth - 47, BoardHeight - 48, 39, 39);
        var gripImage = grip.gameObject.AddComponent<Image>();
        gripImage.sprite = Resources.Load<Sprite>("GameUI/SpeciesJournal/ResizeGrip");
        gripImage.color = gripImage.sprite == null ? Green : Color.white;
        gripImage.preserveAspect = true;
        gripImage.raycastTarget = true;
        grip.gameObject.AddComponent<SnapPanelResizeHandle>();
        grip.SetAsLastSibling();
        EnsureCompactLayout();
        ApplyStyle();
        BindGrip();
        SetCompact(compact);
        FitToCanvas();
    }

    public void ApplyStyle()
    {
        foreach (var label in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.font = Resources.Load<TMP_FontAsset>("GameUI/SpeciesJournal/HandwrittenJournal") ??
                GameUITheme.HandwrittenTMPFont;
            switch (label.name)
            {
                case "TileHeading": case "CompactHeading": label.fontSize = 37; break;
                case "Temperature": case "Humidity": case "Plants":
                case "CompactTemperature": case "CompactHumidity": case "CompactPlants":
                    label.fontSize = 32; break;
                case "ToolsHeading": case "LogHeading": label.fontSize = 29; break;
                case "ToolRows": case "CompactToolRows": label.fontSize = 20; break;
                case "LogEntries": label.fontSize = 18; break;
                case "PopulationMaximum": case "PopulationZero":
                case "PlantMaximum": case "PlantMinimum":
                case "FirstDay": case "LastDay": label.fontSize = 18; break;
                case "LeftAxisTitle": case "RightAxisTitle": label.fontSize = 20; break;
                case "SeriesLegend": case "ChartStatus": label.fontSize = 18; break;
            }
        }
        foreach (string name in new[] { "PopulationAndPlantsChart", "CurrentState", "AppliedTools", "LogFrame",
            "CompactContents/CompactState", "CompactContents/CompactAppliedTools" })
        {
            var frame = transform.Find(name);
            var image = frame != null ? frame.GetComponent<Image>() : null;
            if (image != null) image.color = new Color(.86f, .89f, .72f, .035f);
        }
    }

    private void OnEnable()
    {
        EnsureCompactLayout();
        ApplyStyle();
        if (Application.isPlaying) EnsureCloseMark();
        BindGrip(); SetCompact(compact); FitToCanvas();
    }
    private void LateUpdate() => FitToCanvas();
    public void EnsureCompactLayout()
    {
        if (board == null) board = (RectTransform)transform;
        compactContents = board.Find("CompactContents") as RectTransform;
        if (compactContents == null)
        {
            compactContents = Rect("CompactContents", board, 0, 0, BoardWidth, CompactHeight);
            compactHeading = Label("CompactHeading", compactContents, "地块信息", 37,
                87, 53, 500, 50, TextAlignmentOptions.Center);
            Rule("CompactTitleRule", compactContents, 80, 113, 534);
            var metrics = Rect("CompactState", compactContents, 78, 129, 536, 111);
            Frame(metrics);
            compactTemperature = Label("CompactTemperature", metrics, "温度\n--", 32,
                16, 8, 162, 93, TextAlignmentOptions.Center);
            compactHumidity = Label("CompactHumidity", metrics, "湿度\n--", 32,
                187, 8, 162, 93, TextAlignmentOptions.Center);
            compactPlants = Label("CompactPlants", metrics, "植物量\n--", 32,
                356, 8, 162, 93, TextAlignmentOptions.Center);
            Rule("CompactSplit1", metrics, 177, 11, 1, 88);
            Rule("CompactSplit2", metrics, 351, 11, 1, 88);
            Label("CompactToolsHeading", compactContents, "应用中", 29, 89, 253, 145, 40);
            Rule("CompactToolsRule", compactContents, 243, 276, 367);
            var toolFrame = Rect("CompactAppliedTools", compactContents, 79, 300, 535, 96);
            Frame(toolFrame);
            compactTools = Label("CompactToolRows", toolFrame, "暂无正在应用的工具", 20,
                16, 14, 505, 70);
        }
        else
        {
            compactHeading = compactContents.Find("CompactHeading")?.GetComponent<TextMeshProUGUI>();
            compactTemperature = compactContents.Find("CompactState/CompactTemperature")?.GetComponent<TextMeshProUGUI>();
            compactHumidity = compactContents.Find("CompactState/CompactHumidity")?.GetComponent<TextMeshProUGUI>();
            compactPlants = compactContents.Find("CompactState/CompactPlants")?.GetComponent<TextMeshProUGUI>();
            compactTools = compactContents.Find("CompactAppliedTools/CompactToolRows")?.GetComponent<TextMeshProUGUI>();
        }
        compactContents.gameObject.SetActive(compact);
    }

    private void SetCompact(bool value)
    {
        if (lockFullLayout) value = false;
        compact = value;
        if (board == null) board = (RectTransform)transform;
        EnsureCompactLayout();
        board.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, BoardWidth);
        board.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, compact ? CompactHeight : BoardHeight);
        var background = GetComponent<Image>();
        if (background != null)
        {
            background.sprite = Resources.Load<Sprite>(compact ?
                "GameUI/TileInformationCompact" : "GameUI/TileInformationExtended");
            background.preserveAspect = true;
        }
        foreach (Transform child in board)
            if (child != compactContents && child != grip && child.name != "Close")
                child.gameObject.SetActive(!compact);
        compactContents.gameObject.SetActive(compact);
        if (grip == null) grip = board.Find("ResizeGrip") as RectTransform;
        if (grip != null)
        {
            grip.anchoredPosition = new Vector2(BoardWidth - 47,
                -(compact ? CompactHeight : BoardHeight) + 48);
            grip.SetAsLastSibling();
        }
        var close = board.Find("Close");
        if (close != null)
        {
            PositionClose((RectTransform)close);
            close.SetAsLastSibling();
        }
        FitToCanvas();
        GetComponent<InformationWindow>()?.KeepWithinBounds();
    }

    private static string Metric(string label, string current, string baseline) =>
        label + "\n<size=110%><b>" + current + "</b></size><size=68%>/" + baseline + "</size>";
    private void BindGrip()
    {
        if (grip == null) return;
        grip.GetComponent<SnapPanelResizeHandle>()?.Initialize(delta => {
            if (snappedDuringDrag) return;
            if (!lockFullLayout && !compact && startScale + delta / BoardWidth < .70f)
            { userScale = .70f; snappedDuringDrag = true; SetCompact(true); return; }
            if (compact && delta > 75f)
            { userScale = Mathf.Max(.70f, compactScale); snappedDuringDrag = true; SetCompact(false); return; }
            if (compact) compactScale = Mathf.Clamp(startScale + delta / BoardWidth, .65f, 1.4f);
            else userScale = Mathf.Clamp(startScale + delta / BoardWidth, .70f, 1.45f);
            FitToCanvas();
        }, () => { startScale = compact ? compactScale : userScale; snappedDuringDrag = false; });
    }
    public void ZoomBy(float factor)
    {
        if (compact) compactScale = Mathf.Clamp(compactScale * factor, .55f, 1.6f);
        else userScale = Mathf.Clamp(userScale * factor, .55f, 1.6f);
        FitToCanvas();
        GetComponent<InformationWindow>()?.KeepWithinBounds();
    }
    private void EnsureCloseMark()
    {
        var close = transform.Find("Close") as RectTransform;
        if (close == null) return;
        PositionClose(close);
        var image = close.GetComponent<Image>();
        if (image != null) image.color = new Color(1f, 1f, 1f, .001f);
        var legacy = close.GetComponentInChildren<Text>(true);
        if (legacy != null) legacy.enabled = false;
        var mark = close.Find("CloseMark") as RectTransform;
        if (mark == null)
        {
            mark = Rect("CloseMark", close, 0, 0, 44, 44);
            var label = mark.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = Resources.Load<TMP_FontAsset>("GameUI/SpeciesJournal/HandwrittenJournal") ??
                GameUITheme.HandwrittenTMPFont;
            label.fontSize = 46;
            label.color = new Color(.12f, .28f, .09f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.text = "×";
        }
        close.SetAsLastSibling();
    }
    private void PositionClose(RectTransform close)
    {
        close.anchorMin = close.anchorMax = close.pivot = Vector2.one;
        // Each illustration has its paper tab at a slightly different x/y.
        // Keep the visible cross and its hit area centered on that tab.
        close.anchoredPosition = compact ? new Vector2(-66f, -23f) :
            new Vector2(-78f, -15f);
        close.sizeDelta = new Vector2(44f, 44f);
    }
    private void FitToCanvas()
    {
        if (board == null) board = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null || canvasRect.rect.width <= 0 || canvasRect.rect.height <= 0) return;
        float height = compact ? CompactHeight : BoardHeight;
        float selectedScale = compact ? compactScale : userScale;
        float fit = Mathf.Min(1f, (canvasRect.rect.width - 24f) / BoardWidth,
            (canvasRect.rect.height - 24f) / height);
        bool touchZoom = GetComponent<MobilePanelPinchZoom>()?.enabled == true;
        float scale = Mathf.Min(fit * selectedScale, fit * (touchZoom ? 1.6f : 1f));
        if (!Mathf.Approximately(board.localScale.x, scale)) board.localScale = Vector3.one * scale;
    }

    public void Show(MapTileInstance tile, TileTrendHistory history,
        TileInformationLogHistory logs, SimulationEnvironmentController environment)
    {
        if (tile == null || tile.Block == null) return;
        BlockInfo block = tile.Block;
        TileTrendHistory.TileRecord record = null;
        if (history != null) history.TryGet(tile, out record);
        chart?.Show(record);
        EnvironmentReadSnapshot read = null;
        if (environment != null) environment.TryRead(tile.Coordinate, out read);
        TerrainDefaults defaults = read == null && tile.Definition != null ?
            SimulationEnvironmentController.InitialDefaults(tile.Definition, block.elevation) : default;
        string terrain = read != null ? read.Environment.HabitatTitle : tile.Biome.ToString();
        int elevation = read != null ? read.Environment.Elevation : block.elevation;
        heading.text = "(" + tile.Coordinate.x + "," + tile.Coordinate.y + ") " +
            "<b>" + terrain + "</b>  海拔 " + elevation;
        compactHeading.text = heading.text;
        string temperatureValue = Metric("温度", read != null ? read.Environment.Temperature.Value.ToString("0") :
            block.temperature.ToString(), read != null ? read.Environment.Defaults.Temperature.ToString("0") :
            tile.Definition != null ? defaults.Temperature.ToString() : "--");
        string humidityValue = Metric("湿度", read != null ? read.Environment.Humidity.Value.ToString("0") :
            block.humidity.ToString(), read != null ? read.Environment.Defaults.Humidity.ToString("0") :
            tile.Definition != null ? defaults.Humidity.ToString() : "--");
        temperature.text = compactTemperature.text = temperatureValue;
        humidity.text = compactHumidity.text = humidityValue;
        float stock = read != null ? read.PlantStock : block.plantBiomass;
        float capacity = read != null ? read.PlantCapacity : block.maxPlantBiomass;
        plants.text = "植物量\n" + (capacity > 0 ? 100f * stock / capacity : 0f).ToString("0") + "%";
        compactPlants.text = plants.text;
        tools.text = read == null ? "暂无正在应用的工具" : ActiveTools(read.Environment);
        compactTools.text = tools.text;
        var journal = logs != null ? logs.For(block) : null;
        int count = journal != null ? journal.Count : 0;
        if (lastBlock != block || lastLogCount != count)
        {
            logText.text = LogLines(journal);
            int lines = Mathf.Max(7, count * 2);
            RectTransform content = logScroll.content;
            float height = Mathf.Max(171f, lines * 24f);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            logText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            if (lastBlock != block) logScroll.verticalNormalizedPosition = 1f;
            lastBlock = block; lastLogCount = count;
        }
    }

    private static string ActiveTools(EnvironmentSnapshot state)
    {
        var list = new List<string>();
        AddTool(list, state.HasLocalTemperature, state.Temperature, "温");
        AddTool(list, state.HasLocalHumidity, state.Humidity, "湿");
        AddTool(list, state.HasRecoveryCommand, state.Recovery, "植物恢复");
        return list.Count == 0 ? "暂无正在应用的工具" : string.Join("\n", list);
    }
    private static void AddTool(List<string> list, bool applied, AttributeSnapshot state, string name)
    {
        if (!applied) return;
        string direction = state.ToolTier >= 0 ? "增" : "降";
        string tier = Mathf.Abs(state.ToolTier) >= 2 ? "大档" : "小档";
        string toolName = name == "植物恢复" ? "植物恢复工具" : direction + name + "工具";
        list.Add(toolName + tier + "  |  " +
            (state.Phase == EffectPhase.Returning ? "恢复期" : "应用中") +
            "  目标 " + state.Target.ToString("0") + "  剩余 " + state.RemainingDays + " 天");
    }
    private static string LogLines(IReadOnlyList<TileInformationLogHistory.Entry> entries)
    {
        if (entries == null || entries.Count == 0) return "暂无日志；模拟推进后在此记录种群变化。";
        var text = new System.Text.StringBuilder();
        foreach (var e in entries)
        {
            text.Append('【').Append(e.Name).Append("】 数量 ").Append(e.Count);
            if (e.Change != 0) text.Append('(').Append(e.Change > 0 ? "+" : "").Append(e.Change).Append(')');
            if (!string.IsNullOrEmpty(e.Mutation)) text.Append("  变异：").Append(e.Mutation);
            text.Append("  day").Append(e.Day).Append('\n');
            text.Append("    生育 ").Append(e.Fertility).Append("  体型 ").Append(e.Size)
                .Append("  运动 ").Append(e.Movement).Append("  适温 ").Append(e.Temperature)
                .Append("  适湿 ").Append(e.Humidity).Append('\n');
        }
        return text.ToString();
    }

    private static RectTransform Rect(string name, RectTransform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }
    private static TextMeshProUGUI Label(string name, RectTransform parent, string value,
        float size, float x, float y, float w, float h,
        TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
    {
        var rect = Rect(name, parent, x, y, w, h);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = Resources.Load<TMP_FontAsset>("GameUI/SpeciesJournal/HandwrittenJournal") ??
            GameUITheme.HandwrittenTMPFont;
        label.fontSize = size;
        label.color = Ink;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Truncate;
        label.raycastTarget = false;
        label.text = value;
        return label;
    }
    private static void Frame(RectTransform rect)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(.55f, .68f, .45f, .13f);
        image.raycastTarget = false;
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = Green;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
    }
    private static void Rule(string name, RectTransform parent, float x, float y, float width,
        float height = 2f)
    {
        var rect = Rect(name, parent, x, y, width, height);
        rect.gameObject.AddComponent<Image>().color = new Color(.35f, .52f, .27f, .8f);
    }
}
