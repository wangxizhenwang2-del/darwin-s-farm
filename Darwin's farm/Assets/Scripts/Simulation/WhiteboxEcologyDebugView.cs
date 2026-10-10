using System.Collections.Generic;
using DarwinFarm.Environment;
using UnityEngine;

// View: screen-sized, non-interactive labels projected above their owning tiles.
public sealed class WhiteboxEcologyDebugView : MonoBehaviour
{
    [SerializeField] private WhiteboxEcologyDebugController controller;
    [SerializeField] private PopulationVisualPresenter populationVisuals;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private EnvironmentTechnologyController technology;
    [SerializeField] private SimulationTime clock;
    [SerializeField, Range(9, 18)] private int fontSize = 11;
    [SerializeField] private float heightAboveTile = 2.5f;
    private GUIStyle textStyle;
    private GUIStyle backgroundStyle;
    private GUIStyle wrappedStyle;
    private readonly Dictionary<Vector2Int, EnvironmentTechnologyKind> selections =
        new Dictionary<Vector2Int, EnvironmentTechnologyKind>();
    private readonly Dictionary<Vector2Int, int> tiers = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, int> windTemperatures = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, int> windHumidities = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, string> notices = new Dictionary<Vector2Int, string>();
    private Vector2Int? openTechnologyMenu;
    private Rect openMenuRect;
    private readonly List<Rect> interactiveRects = new List<Rect>();
    private static readonly int[] FourTiers = { 2, 1, -1, -2 };
    private static readonly string[] MonsoonNames = { "冷湿", "暖湿", "干热", "干冷" };
    private static readonly int[] MonsoonTemperatures = { -20, 20, 20, -20 };
    private static readonly int[] MonsoonHumidities = { 20, 20, -20, -20 };
    private static WhiteboxEcologyDebugView activeView;

    private void OnEnable() => activeView = this;
    private void OnDisable()
    { if (activeView == this) activeView = null; }
    public static bool IsPointerOverTestUi(Vector2 screenPosition)
    {
        if (activeView == null) return false;
        Vector2 guiPosition = new Vector2(screenPosition.x,
            Screen.height - screenPosition.y);
        foreach (Rect rect in activeView.interactiveRects)
            if (rect.Contains(guiPosition)) return true;
        return false;
    }

    private void Awake()
    {
        if (controller == null) controller = GetComponent<WhiteboxEcologyDebugController>();
        if (populationVisuals == null) populationVisuals = GetComponent<PopulationVisualPresenter>();
        if (viewCamera == null) viewCamera = Camera.main;
        if (technology == null) technology = GetComponent<EnvironmentTechnologyController>();
        if (clock == null) clock = GetComponent<SimulationTime>();
    }

    private void OnGUI()
    {
        if (controller == null || viewCamera == null) return;
        if (technology == null) technology = GetComponent<EnvironmentTechnologyController>();
        interactiveRects.Clear();
        if (textStyle == null || textStyle.fontSize != fontSize)
        {
            textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize, alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.white }
            };
            backgroundStyle = new GUIStyle(GUI.skin.box);
            wrappedStyle = new GUIStyle(textStyle) { wordWrap = true };
        }
        string speed = clock == null ? "" : clock.IsPaused ? "暂停" : clock.Speed.ToString("F0") + "倍速";
        GUI.Label(new Rect(12f, 8f, 330f, 22f),
            $"第{controller.Day}天  {speed}  空格播放/暂停 · 2两倍速", textStyle);

        foreach (WhiteboxEcologyDebugController.TileLabel label in controller.Labels)
        {
            if (label.tile == null || label.lines == null) continue;
            Vector3 anchor = label.tile.transform.position + Vector3.up * heightAboveTile;
            Vector3 screen = viewCamera.WorldToScreenPoint(anchor);
            if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width ||
                screen.y < 0f || screen.y > Screen.height) continue;
            float blockY = Screen.height - screen.y - (fontSize + 4f) * 2f - 8f;
            DrawLines(screen.x, blockY, label.lines, 0, 2);
            DrawTechnologyPanel(label.tile.Coordinate, screen.x, blockY);
            if (label.populations == null) continue;
            for (int i = 0; i < label.populations.Length; i++)
            {
                int start = 2 + i * 3;
                if (start + 2 >= label.lines.Length) break;
                Vector3 populationScreen = Vector3.zero;
                WhiteboxPopulation visual = null;
                bool hasVisual = populationVisuals != null &&
                    populationVisuals.TryGetVisual(label.populations[i], out visual) &&
                    visual.Leader != null;
                if (hasVisual)
                    populationScreen = viewCamera.WorldToScreenPoint(
                        visual.Leader.position + Vector3.up * 1.2f);
                if (hasVisual && populationScreen.z > 0f)
                    DrawLines(populationScreen.x,
                        Screen.height - populationScreen.y - (fontSize + 4f) * 3f - 8f,
                        label.lines, start, 3);
                else
                    DrawLines(screen.x, blockY - (i + 1) * ((fontSize + 4f) * 3f + 8f),
                        label.lines, start, 3);
            }
        }
        DrawOpenTechnologyMenu();
    }

    // 2026-10-09 14:57 +08:00: the temporary View only holds selection state
    // and renders Controller previews; every write is routed through TryDeploy.
    private void DrawTechnologyPanel(Vector2Int coordinate, float anchorX, float anchorY)
    {
        if (technology == null) return;
        float width = 330f;
        float x = Mathf.Clamp(anchorX + 138f, 4f, Mathf.Max(4f, Screen.width - width - 4f));
        float y = Mathf.Clamp(anchorY, 30f, Mathf.Max(30f, Screen.height - 376f));
        Rect box = new Rect(x, y, width, 372f);
        interactiveRects.Add(box);
        GUI.Box(box, GUIContent.none, backgroundStyle);
        GUI.Label(new Rect(x + 5f, y + 3f, width - 10f, 72f),
            technology.CurrentApplied(coordinate), wrappedStyle);

        EnvironmentTechnologyKind kind = selections.TryGetValue(coordinate, out var selected)
            ? selected : EnvironmentTechnologyKind.TemperatureChange;
        int tier = tiers.TryGetValue(coordinate, out int rememberedTier) ? rememberedTier : 1;
        if (kind == EnvironmentTechnologyKind.MonsoonAnchor) tier = 1;
        if (kind == EnvironmentTechnologyKind.ElevationChange && (tier == 2 || tier == -2)) tier = 1;
        int windT = windTemperatures.TryGetValue(coordinate, out int selectedT) ? selectedT : 20;
        int windH = windHumidities.TryGetValue(coordinate, out int selectedH) ? selectedH : 20;
        Rect menuButton = new Rect(x + 5f, y + 78f, width - 10f, 23f);
        if (GUI.Button(menuButton, EnvironmentTechnologyCatalog.Name(kind) + "  ▾"))
        {
            openTechnologyMenu = openTechnologyMenu == coordinate ? (Vector2Int?)null : coordinate;
            openMenuRect = menuButton;
        }
        if (kind == EnvironmentTechnologyKind.MonsoonAnchor)
        {
            for (int i = 0; i < MonsoonNames.Length; i++)
            {
                float buttonX = x + (i % 2 == 0 ? 5f : 168f);
                float buttonY = y + (i < 2 ? 105f : 130f);
                int temperature = MonsoonTemperatures[i], humidity = MonsoonHumidities[i];
                if (GUI.Button(new Rect(buttonX, buttonY, 157f, 22f),
                    MonsoonNames[i] + (windT == temperature && windH == humidity ? " ✓" : "")))
                {
                    windTemperatures[coordinate] = windT = temperature;
                    windHumidities[coordinate] = windH = humidity;
                }
            }
        }
        else if (kind == EnvironmentTechnologyKind.ElevationChange)
        {
            if (GUI.Button(new Rect(x + 5f, y + 105f, 157f, 22f), "高1档" + (tier == 1 ? " ✓" : "")))
                tiers[coordinate] = tier = 1;
            if (GUI.Button(new Rect(x + 168f, y + 105f, 157f, 22f), "低1档" + (tier == -1 ? " ✓" : "")))
                tiers[coordinate] = tier = -1;
        }
        else
        {
            for (int i = 0; i < FourTiers.Length; i++)
            {
                int value = FourTiers[i];
                float buttonX = x + (i % 2 == 0 ? 5f : 168f);
                float buttonY = y + (i < 2 ? 105f : 130f);
                if (GUI.Button(new Rect(buttonX, buttonY, 157f, 22f),
                    TierLabel(value) + (tier == value ? " ✓" : "")))
                    tiers[coordinate] = tier = value;
            }
        }

        var choice = new EnvironmentTechnologyChoice(kind, tier, windT, windH);
        EnvironmentTechnologyPreview preview = technology.Preview(coordinate, choice);
        string effect = preview.IsValid ? preview.After : preview.Error;
        GUI.Label(new Rect(x + 5f, y + 157f, width - 10f, 72f),
            preview.Before ?? "", wrappedStyle);
        GUI.Label(new Rect(x + 5f, y + 232f, width - 10f, 65f), effect, wrappedStyle);
        GUI.Label(new Rect(x + 5f, y + 300f, width - 10f, 32f),
            preview.IsValid ? preview.Timing + " · 费用 0" : "不可投放", wrappedStyle);
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && preview.IsValid;
        if (GUI.Button(new Rect(x + 5f, y + 337f, 90f, 24f), "投放"))
        {
            bool success = technology.TryDeploy(coordinate, choice, out var committed);
            notices[coordinate] = success ? "已投放（费用 0）" : committed.Error;
            openTechnologyMenu = null;
        }
        GUI.enabled = enabled;
        GUI.enabled = enabled && technology.CanCancel(coordinate, kind);
        if (GUI.Button(new Rect(x + 100f, y + 337f, 80f, 24f), "撤销"))
            notices[coordinate] = technology.TryCancel(coordinate, kind, out string error)
                ? "已撤销" : error;
        GUI.enabled = enabled;
        if (notices.TryGetValue(coordinate, out string notice))
            GUI.Label(new Rect(x + 185f, y + 340f, width - 190f, 21f), notice, textStyle);
    }

    private void DrawOpenTechnologyMenu()
    {
        if (!openTechnologyMenu.HasValue) return;
        float width = openMenuRect.width;
        float height = EnvironmentTechnologyCatalog.All.Length * 22f + 8f;
        float y = openMenuRect.yMax + height > Screen.height
            ? Mathf.Max(4f, openMenuRect.y - height) : openMenuRect.yMax;
        Rect panel = new Rect(openMenuRect.x, y, width, height);
        interactiveRects.Add(panel);
        GUI.Box(panel, GUIContent.none, backgroundStyle);
        for (int i = 0; i < EnvironmentTechnologyCatalog.All.Length; i++)
            if (GUI.Button(new Rect(panel.x + 4f, panel.y + 4f + 22f * i,
                panel.width - 8f, 21f),
                EnvironmentTechnologyCatalog.Name(EnvironmentTechnologyCatalog.All[i])))
            {
                selections[openTechnologyMenu.Value] = EnvironmentTechnologyCatalog.All[i];
                notices.Remove(openTechnologyMenu.Value);
                openTechnologyMenu = null;
                break;
            }
    }

    private static string TierLabel(int tier) => (tier > 0 ? "高" : "低") +
        Mathf.Abs(tier) + "档";

    private void DrawLines(float centerX, float y, string[] lines, int start, int count)
    {
        float lineHeight = fontSize + 4f;
        float width = 260f;
        Rect rect = new Rect(centerX - width * 0.5f, y, width, count * lineHeight + 8f);
        GUI.Box(rect, GUIContent.none, backgroundStyle);
        for (int i = 0; i < count; i++)
            GUI.Label(new Rect(rect.x + 5f, rect.y + 4f + i * lineHeight,
                width - 10f, lineHeight), lines[start + i], textStyle);
    }
}
