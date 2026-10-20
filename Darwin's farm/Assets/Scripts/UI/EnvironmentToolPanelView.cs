using DarwinFarm.Environment;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The same authored presentation is used inside the desktop and Android canvases.
public sealed class EnvironmentToolPanelView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI title, effect, effectHint, terrain, defaultValue;
    [SerializeField] private TextMeshProUGUI chartPrompt, chartSubtitle, chartTitle;
    [SerializeField] private TextMeshProUGUI appliedFirst, appliedSecond;
    [SerializeField] private TextMeshProUGUI[] tierValues, tierNames;
    [SerializeField] private Image[] tierCards;
    [SerializeField] private GameObject chartAnnotations;
    [SerializeField] private EnvironmentForecastGraphic chart;
    private SimulationEnvironmentController environment;
    private TileTrendHistory history;
    private static readonly Color Paper = new Color(.96f, .93f, .81f, .92f);
    private static readonly Color Selected = new Color(.82f, .91f, .75f, .92f);

    public void Configure(TextMeshProUGUI toolTitle, TextMeshProUGUI effectLabel,
        TextMeshProUGUI hintLabel, TextMeshProUGUI terrainLabel, TextMeshProUGUI defaultLabel,
        TextMeshProUGUI prompt, TextMeshProUGUI subtitle, TextMeshProUGUI graphTitle,
        TextMeshProUGUI firstApplied, TextMeshProUGUI secondApplied,
        TextMeshProUGUI[] values, TextMeshProUGUI[] names, Image[] cards,
        GameObject annotations, EnvironmentForecastGraphic graph)
    {
        title = toolTitle; effect = effectLabel; effectHint = hintLabel;
        terrain = terrainLabel; defaultValue = defaultLabel;
        chartPrompt = prompt; chartSubtitle = subtitle; chartTitle = graphTitle;
        appliedFirst = firstApplied; appliedSecond = secondApplied;
        tierValues = values; tierNames = names; tierCards = cards;
        chartAnnotations = annotations; chart = graph;
    }

    public void Refresh(EnvironmentToolSession session)
    {
        if (session == null || title == null) return;
        if (environment == null) environment = FindFirstObjectByType<SimulationEnvironmentController>();
        if (history == null) history = FindFirstObjectByType<TileTrendHistory>();
        var choice = session.Choice;
        title.text = EnvironmentTechnologyCatalog.Name(choice.Kind);
        for (int i = 0; i < tierValues.Length; i++)
        {
            bool showCard = choice.Kind != EnvironmentTechnologyKind.ElevationChange || i == 1 || i == 2;
            tierCards[i].gameObject.SetActive(showCard);
            if (!showCard) continue;
            int tier = i == 0 ? 2 : i == 1 ? 1 : i == 2 ? -1 : -2;
            var item = new EnvironmentTechnologyChoice(choice.Kind, tier);
            tierValues[i].text = Value(item);
            tierNames[i].text = TierName(item);
            tierCards[i].color = session.OptionSelected(i) ? Selected : Paper;
            var border = tierCards[i].GetComponent<Outline>();
            if (border != null) border.enabled = session.OptionSelected(i);
        }
        effect.text = session.HasPendingOption ? Effect(choice) : "请选择档位预览变化";
        effectHint.text = session.HasPendingOption ? Hint(choice) : "";
        chartTitle.text = choice.Kind == EnvironmentTechnologyKind.TemperatureChange ? "温度趋势" :
            choice.Kind == EnvironmentTechnologyKind.HumidityChange ? "湿度趋势" :
            choice.Kind == EnvironmentTechnologyKind.ElevationChange ? "地形变化" : "植物存量";
        var target = session.Target;
        EnvironmentSnapshot state = null;
        if (target != null && environment != null &&
            environment.TryRead(target.Coordinate, out var read)) state = read.Environment;
        if (state == null)
        {
            terrain.text = "选择地块后计算";
            defaultValue.text = "等待选择地块";
            chartPrompt.gameObject.SetActive(true);
            chartPrompt.text = "请点击地块以继续";
            chartSubtitle.gameObject.SetActive(true);
            chartSubtitle.text = "选择后显示历史与预测趋势";
            chartAnnotations.SetActive(false);
            SetChartLabels(EnvironmentForecastGraphic.Scenario.None);
            chart.Show(null, true, 0, 0, 0, 0, default,
                EnvironmentForecastGraphic.Scenario.None, false);
            appliedFirst.text = "选择地块后查看";
            appliedSecond.text = "";
            return;
        }
        var prediction = session.Preview;
        string futureTerrain = prediction != null && prediction.IsValid &&
            prediction.EstimatedHabitat.HasValue ?
            EnvironmentRules.Profile(prediction.EstimatedHabitat.Value).Title : state.HabitatTitle;
        bool temperature = choice.Kind == EnvironmentTechnologyKind.TemperatureChange;
        bool humidity = choice.Kind == EnvironmentTechnologyKind.HumidityChange;
        bool climate = temperature || humidity;
        if (climate)
        {
            bool cancel = session.CancelPreviewActive;
            bool apply = !cancel && session.HasPendingOption &&
                prediction != null && prediction.IsValid && prediction.TargetValue.HasValue;
            var mode = cancel ? EnvironmentForecastGraphic.Scenario.Cancel :
                apply ? EnvironmentForecastGraphic.Scenario.Apply :
                EnvironmentForecastGraphic.Scenario.None;
            float normal = temperature ? state.Defaults.Temperature : state.Defaults.Humidity;
            float current = temperature ? (float)state.Temperature.Value : (float)state.Humidity.Value;
            float goal = apply ? (float)prediction.TargetValue.Value :
                cancel ? (float)(temperature ? state.TemperatureCancelTarget : state.HumidityCancelTarget) :
                current;
            goal = Mathf.Clamp(goal, 0, 100);
            if (cancel)
            {
                var habitat = EnvironmentRules.ClassifyHabitat(state.Elevation,
                    temperature ? goal : state.Temperature.Value,
                    temperature ? state.Humidity.Value : goal);
                futureTerrain = EnvironmentRules.Profile(habitat).Title;
                effect.text = "撤销后预计 " + (temperature ? "温度 " : "湿度 ") +
                    current.ToString("0") + " → " + goal.ToString("0");
                effectHint.text = "立即进入恢复期";
            }
            var futureDefaults = cancel ? EnvironmentRules.Defaults(
                    EnvironmentRules.ClassifyHabitat(state.Elevation,
                        temperature ? goal : state.Temperature.Value,
                        temperature ? state.Humidity.Value : goal)) :
                apply && prediction.EstimatedHabitat.HasValue ?
                    EnvironmentRules.Defaults(prediction.EstimatedHabitat.Value) : state.Defaults;
            float later = temperature ? futureDefaults.Temperature : futureDefaults.Humidity;
            defaultValue.text = "默认" + (temperature ? "温度 " : "湿度 ") + normal.ToString("0") + " 到 " + later.ToString("0");
            TileTrendHistory.TileRecord record = null;
            if (history != null) history.TryGet(target, out record);
            chart.Show(record, temperature, current, normal, goal, later,
                temperature ? state.Temperature : state.Humidity, mode, true);
            chartPrompt.gameObject.SetActive(session.HasPendingOption && !cancel && !apply);
            chartPrompt.text = prediction != null && !prediction.IsValid ? prediction.Error : "等待预测";
            chartSubtitle.gameObject.SetActive(false);
            chartAnnotations.SetActive(true);
            SetChartLabels(mode);
        }
        else
        {
            if (session.CancelPreviewActive && choice.Kind == EnvironmentTechnologyKind.PlantRecoveryChange)
            {
                effect.text = "撤销后预计 · 日增量回落";
                effectHint.text = "立即进入恢复期";
            }
            defaultValue.text = choice.Kind == EnvironmentTechnologyKind.ElevationChange ?
                (session.HasPendingOption ?
                    "海拔 " + state.Elevation + " 到 " + (state.Elevation + choice.Tier) :
                    "海拔 " + state.Elevation) : "不模拟动物影响";
            chart.Show(null, false, 0, 0, 0, 0, default,
                EnvironmentForecastGraphic.Scenario.None, false);
            chartAnnotations.SetActive(false);
            SetChartLabels(EnvironmentForecastGraphic.Scenario.None);
            chartPrompt.gameObject.SetActive(true);
            chartPrompt.text = choice.Kind == EnvironmentTechnologyKind.ElevationChange ?
                "海拔与地形：" + (session.HasPendingOption ? defaultValue.text : "请选择档位") :
                session.CancelPreviewActive && choice.Kind == EnvironmentTechnologyKind.PlantRecoveryChange ?
                "撤销后日增量目标 " + state.RecoveryCancelTarget.ToString("F0") :
                choice.Kind == EnvironmentTechnologyKind.PlantStockChange ?
                "库存单次变化：" + (prediction != null && prediction.IsValid ? prediction.After : "等待预测") :
                "日增量变化：" + (prediction != null && prediction.IsValid ? prediction.After : "等待预测");
            chartSubtitle.gameObject.SetActive(true);
            chartSubtitle.text = choice.Kind == EnvironmentTechnologyKind.ElevationChange ?
                "温湿度默认值随预计地形调整" : "植物库存未来走势受生物消耗影响";
        }
        terrain.text = !session.HasPendingOption && !session.CancelPreviewActive ?
            "当前地形：" + state.HabitatTitle :
            state.HabitatTitle == futureTerrain ? state.HabitatTitle + "（预计保持）" :
                state.HabitatTitle + "变为" + futureTerrain;
        Applied(state, appliedFirst, appliedSecond);
        if (!string.IsNullOrEmpty(session.Notice)) appliedSecond.text = session.Notice;
    }

    private void SetChartLabels(EnvironmentForecastGraphic.Scenario mode)
    {
        var enter = chartAnnotations.transform.Find("Enter")?.GetComponent<TextMeshProUGUI>();
        var hold = chartAnnotations.transform.Find("Hold")?.GetComponent<TextMeshProUGUI>();
        var returning = chartAnnotations.transform.Find("Return")?.GetComponent<TextMeshProUGUI>();
        if (enter != null) enter.text = mode == EnvironmentForecastGraphic.Scenario.Cancel ?
            "立即恢复" : mode == EnvironmentForecastGraphic.Scenario.Apply ? "生效25日" : "当前走势";
        if (hold != null) hold.text = mode == EnvironmentForecastGraphic.Scenario.Cancel ?
            "约100日" : mode == EnvironmentForecastGraphic.Scenario.Apply ? "维持150日" : "";
        if (returning != null) returning.text = mode == EnvironmentForecastGraphic.Scenario.Apply ?
            "恢复100日" : "";
        bool showScenario = mode != EnvironmentForecastGraphic.Scenario.None;
        var legend = transform.Find("ForecastLegend")?.GetComponent<TextMeshProUGUI>();
        if (legend != null)
        {
            legend.gameObject.SetActive(showScenario);
            legend.text = mode == EnvironmentForecastGraphic.Scenario.Cancel ? "撤销预测" : "工具预测";
        }
        foreach (string name in new[] { "ForecastLegendMark", "ForecastLegendMark2" })
        {
            var mark = transform.Find(name)?.GetComponent<Image>();
            if (mark == null) continue;
            mark.gameObject.SetActive(showScenario);
            mark.color = mode == EnvironmentForecastGraphic.Scenario.Cancel ?
                new Color(.25f, .50f, .31f) : new Color(.74f, .25f, .20f);
        }
    }

    private static string Value(EnvironmentTechnologyChoice choice)
    {
        int tier = choice.Tier;
        string sign = tier > 0 ? "+" : "−";
        switch (choice.Kind)
        {
            case EnvironmentTechnologyKind.TemperatureChange:
            case EnvironmentTechnologyKind.HumidityChange: return sign + Mathf.Abs(EnvironmentTechnologyCatalog.SignedClimate(choice));
            case EnvironmentTechnologyKind.PlantStockChange: return sign + (Mathf.Abs(tier) * 25) + "%";
            case EnvironmentTechnologyKind.PlantRecoveryChange: return sign + (Mathf.Abs(tier) * 25000 / 10000f).ToString("0.#") + "万";
            default: return sign + "1档";
        }
    }
    private static string TierName(EnvironmentTechnologyChoice choice)
    {
        string amount = Mathf.Abs(choice.Tier) == 2 ? "大幅" : "小幅";
        bool up = choice.Tier > 0;
        switch (choice.Kind)
        {
            case EnvironmentTechnologyKind.TemperatureChange: return amount + (up ? "升温" : "降温");
            case EnvironmentTechnologyKind.HumidityChange: return amount + (up ? "增湿" : "减湿");
            case EnvironmentTechnologyKind.ElevationChange: return up ? "升一档" : "降一档";
            default: return amount + (up ? "增加" : "减少");
        }
    }
    private static string Effect(EnvironmentTechnologyChoice choice) =>
        (choice.Kind == EnvironmentTechnologyKind.TemperatureChange ? "环境温度 " :
         choice.Kind == EnvironmentTechnologyKind.HumidityChange ? "环境湿度 " :
         choice.Kind == EnvironmentTechnologyKind.PlantStockChange ? "植物存量 " :
         choice.Kind == EnvironmentTechnologyKind.PlantRecoveryChange ? "日增量 " : "海拔 ") + Value(choice);
    private static string Hint(EnvironmentTechnologyChoice choice)
    {
        return choice.Kind == EnvironmentTechnologyKind.TemperatureChange ? "适温可能改变" :
            choice.Kind == EnvironmentTechnologyKind.HumidityChange ? "适湿可能改变" :
            choice.Kind == EnvironmentTechnologyKind.ElevationChange ? "性状可能改变" :
            "植物生长可能改变";
    }
    private static void Applied(EnvironmentSnapshot state, TextMeshProUGUI first, TextMeshProUGUI second)
    {
        first.text = second.text = "";
        int count = 0;
        void Add(string name, AttributeSnapshot attribute)
        {
            string phase = attribute.Phase == EffectPhase.Entering ? "生效中" :
                attribute.Phase == EffectPhase.Holding ? "维持中" :
                attribute.Phase == EffectPhase.Returning ? "恢复中" : "已生效";
            string amount = attribute.ToolTier == 0 ? "" :
                "·" + (attribute.ToolTier > 0 ? "升" : "降") +
                (Mathf.Abs(attribute.ToolTier) == 2 ? "大档" : "小档");
            string line = name + amount + "  " + phase + "  余" + attribute.RemainingDays + "日";
            if (count++ == 0) first.text = line; else if (count == 2) second.text = line;
        }
        if (state.HasLocalTemperature) Add("温度", state.Temperature);
        if (state.HasLocalHumidity) Add("湿度", state.Humidity);
        if (state.HasRecoveryCommand) Add("植物增量", state.Recovery);
        if (count == 0) first.text = "当前地块无已施加工具";
        else if (count > 2) second.text += " · 另有植物工具";
    }
}
