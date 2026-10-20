using DarwinFarm.Environment;
using UnityEngine;
using UnityEngine.UI;

// Compact, resolution-independent 0..100 plot used by both tool canvases.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class EnvironmentForecastGraphic : MaskableGraphic
{
    public static Rect PlotRect(Rect bounds) => Rect.MinMaxRect(
        bounds.xMin + 30, bounds.yMin + 18, bounds.xMax - 7, bounds.yMax - 21);
    public enum Scenario { None, Apply, Cancel }
    private TileTrendHistory.TileRecord history;
    private bool temperature, available;
    private Scenario scenario;
    private AttributeSnapshot live;
    private float current, baseline, target, futureBaseline;
    private readonly Color grid = new Color(.55f, .59f, .52f, .35f);
    private readonly Color observed = new Color(.30f, .52f, .64f);
    private readonly Color forecast = new Color(.73f, .25f, .20f);
    private readonly Color defaultColor = new Color(.08f, .09f, .08f);

    public void Show(TileTrendHistory.TileRecord record, bool useTemperature,
        float currentValue, float defaultValue, float targetValue, float estimatedDefault,
        AttributeSnapshot liveAttribute, Scenario mode, bool hasData)
    {
        history = record; temperature = useTemperature;
        current = Mathf.Clamp(currentValue, 0, 100);
        baseline = Mathf.Clamp(defaultValue, 0, 100);
        target = Mathf.Clamp(targetValue, 0, 100);
        futureBaseline = Mathf.Clamp(estimatedDefault, 0, 100);
        live = liveAttribute; scenario = mode; available = hasData;
        SetVerticesDirty();
    }

    private static float Fraction(float day)
    {
        if (day <= 0) return .27f * (day + 30f) / 30f;
        if (day <= 25) return .27f + .18f * day / 25f;
        if (day <= 175) return .45f + .30f * (day - 25f) / 150f;
        return .75f + .25f * (day - 175f) / 100f;
    }
    private Vector2 Point(Rect plot, float day, float value) =>
        new Vector2(plot.xMin + plot.width * Fraction(day),
            plot.yMin + plot.height * Mathf.Clamp01(value / 100f));
    private float DefaultAt(float day) => Mathf.Clamp(day <= 25 ? baseline :
        day >= 65 ? futureBaseline : Mathf.Lerp(baseline, futureBaseline, (day - 25f) / 40f), 0, 100);
    private float ForecastAt(float day)
    {
        if (scenario == Scenario.Cancel)
            return Mathf.Clamp(Mathf.Lerp(current, target, Mathf.Clamp01(day / 100f)), 0, 100);
        float value = day <= 25 ? Mathf.Lerp(current, target, day / 25f) :
            day <= 175 ? target : Mathf.Lerp(target, DefaultAt(day), (day - 175f) / 100f);
        return Mathf.Clamp(value, 0, 100);
    }
    private float NaturalAt(float day)
    {
        float destination = Mathf.Clamp((float)live.Target, 0, 100);
        float remaining = Mathf.Max(1, live.RemainingDays);
        switch (live.Phase)
        {
            case EffectPhase.Entering:
                if (day <= remaining) return Mathf.Lerp(current, destination, day / remaining);
                if (day <= remaining + 150f) return destination;
                return Mathf.Lerp(destination, baseline,
                    Mathf.Clamp01((day - remaining - 150f) / 100f));
            case EffectPhase.Holding:
                return day <= remaining ? current : Mathf.Lerp(current, baseline,
                    Mathf.Clamp01((day - remaining) / 100f));
            case EffectPhase.Returning:
            case EffectPhase.Automatic:
                return Mathf.Lerp(current, destination, Mathf.Clamp01(day / remaining));
            default: return current;
        }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var r = rectTransform.rect;
        Rect plot = PlotRect(r);
        for (int i = 0; i <= 4; i++)
        {
            float y = plot.yMin + plot.height * i / 4f;
            Line(mesh, new Vector2(plot.xMin, y), new Vector2(plot.xMax, y), grid, .65f);
        }
        foreach (float day in scenario == Scenario.Apply ? new[] { 0f, 25f, 175f } :
            scenario == Scenario.Cancel ? new[] { 0f, 100f } : new[] { 0f })
            Dashed(mesh, Point(plot, day, 0), Point(plot, day, 100), grid, 1f);
        if (!available) return;

        // Historical values are recorded by TileTrendHistory; no fabricated pre-selection series.
        if (history != null && history.Count > 0)
        {
            int now = history.Day(history.Count - 1);
            Vector2? previous = null, previousDefault = null;
            for (int i = 0; i < history.Count; i++)
            {
                float day = history.Day(i) - now;
                if (day < -30) continue;
                float value = temperature ? history.Temperature(i) : history.Humidity(i);
                float normal = temperature ? history.TemperatureDefault(i) : history.HumidityDefault(i);
                if (float.IsNaN(value)) continue;
                Vector2 at = Point(plot, day, value);
                if (previous.HasValue) Line(mesh, previous.Value, at, observed, 1.8f);
                previous = at;
                if (float.IsNaN(normal)) continue;
                Vector2 atDefault = Point(plot, day, normal);
                if (previousDefault.HasValue) Line(mesh, previousDefault.Value, atDefault, defaultColor, 2.4f);
                previousDefault = atDefault;
            }
        }
        Line(mesh, Point(plot, -30, baseline), Point(plot, 0, baseline), defaultColor, 2.4f);
        for (int day = 0; day < 275; day += 5)
        {
            int next = Mathf.Min(275, day + 5);
            float da = scenario == Scenario.None ? baseline : DefaultAt(day);
            float db = scenario == Scenario.None ? baseline : DefaultAt(next);
            if (scenario != Scenario.None)
            {
                float a = ForecastAt(day), b = ForecastAt(next);
                var tint = a + b > da + db ? new Color(.73f, .25f, .20f, .22f) :
                    new Color(.27f, .57f, .36f, .23f);
                Quad(mesh, Point(plot, day, a), Point(plot, next, b),
                    Point(plot, next, db), Point(plot, day, da), tint);
                if (day % 10 == 0)
                    Line(mesh, Point(plot, day, a), Point(plot, next, b),
                        scenario == Scenario.Cancel ? new Color(.25f, .50f, .31f) : forecast, 2f);
            }
            Line(mesh, Point(plot, day, NaturalAt(day)), Point(plot, next, NaturalAt(next)),
                new Color(.30f, .52f, .64f, .75f), 1.5f);
            Line(mesh, Point(plot, day, da), Point(plot, next, db), defaultColor, 2.4f);
        }
    }

    private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int index = mesh.currentVertCount;
        mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero);
        mesh.AddVert(c, color, Vector2.zero); mesh.AddVert(d, color, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index, index + 2, index + 3);
    }
    private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, Color color, float width)
    {
        Vector2 delta = b - a;
        if (delta.sqrMagnitude < .000001f) return;
        Vector2 offset = new Vector2(-delta.y, delta.x).normalized * width * .5f;
        Quad(mesh, a - offset, b - offset, b + offset, a + offset, color);
    }
    private static void Dashed(VertexHelper mesh, Vector2 a, Vector2 b, Color color, float width)
    {
        for (float t = 0; t < 1; t += .08f)
            Line(mesh, Vector2.Lerp(a, b, t), Vector2.Lerp(a, b, Mathf.Min(1f, t + .04f)), color, width);
    }
}
