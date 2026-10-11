using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A view of one selected population. Static presentation is read from its SpeciesData.
public sealed class PopulationInfoPanel : MonoBehaviour
{
    [SerializeField] private Image portrait;
    [SerializeField] private TextMeshProUGUI placeholder, speciesName, count, mutation, description, additional;
    [SerializeField] private TextMeshProUGUI sources, current, predators, trophic, relationshipTitle;
    [SerializeField] private TextMeshProUGUI[] labels;
    [SerializeField] private PopulationRadarChart radar;
    [SerializeField] private PopulationFoodWebGraphic foodGraphic;
    [SerializeField] private Vector2[] radarLabelPositions = {
        new Vector2(160f, 0f), new Vector2(320f, -66f), new Vector2(283f, -170f),
        new Vector2(32f, -170f), new Vector2(0f, -66f) };
    [SerializeField] private RectTransform content;
    [SerializeField] private InformationWindow window;

    public void Initialize(InformationWindow owner)
    {
        window = owner;
        if (content != null) return;
        content = Rect("Content", (RectTransform)transform, Vector2.zero, new Vector2(450f, 665f));
        content.anchoredPosition = new Vector2(35f, -140f);
        float scale = Mathf.Min(1f, (((RectTransform)transform).rect.width - 70f) / 450f);
        content.localScale = Vector3.one * scale;
        RectTransform header = Rect("SpeciesHeader", content, Vector2.zero, new Vector2(450f, 235f));
        RectTransform picture = Rect("SpeciesPortrait", header, Vector2.zero, new Vector2(165f, 130f));
        portrait = picture.gameObject.AddComponent<Image>();
        portrait.preserveAspect = true; portrait.raycastTarget = false;
        placeholder = Text("PortraitPlaceholder", picture, "?", 64, Vector2.zero, new Vector2(165f, 130f));
        placeholder.alignment = TextAlignmentOptions.Center;
        speciesName = Text("SpeciesName", header, "", 32, new Vector2(0f, -133f), new Vector2(190f, 42f));
        count = Text("PopulationCount", header, "", 22, new Vector2(0f, -176f), new Vector2(200f, 31f));
        mutation = Text("MutationInfo", header, "变异：暂无记录", 19, new Vector2(0f, -211f), new Vector2(205f, 30f));
        description = ScrollText("SpeciesDescription", header, new Vector2(210f, -25f), new Vector2(240f, 207f), 21);

        RectTransform section = Rect("RadarChartSection", content, new Vector2(0f, -249f), new Vector2(450f, 205f));
        RectTransform chartRect = Rect("RadarChart", section, new Vector2(141f, -30f), new Vector2(168f, 168f));
        radar = chartRect.gameObject.AddComponent<PopulationRadarChart>(); radar.raycastTarget = false;
        labels = new TextMeshProUGUI[5];
        Vector2[] positions = radarLabelPositions;
        for (int i = 0; i < 5; i++)
            labels[i] = Text("Attribute" + i, section, "", 18, positions[i], new Vector2(130f, 32f));

        RectTransform food = Rect("FoodWebSection", content, new Vector2(0f, -461f), new Vector2(450f, 98f));
        relationshipTitle = Text("RelationshipScope", food, "当前地块食物关系", 17, Vector2.zero, new Vector2(450f, 24f));
        RectTransform nodes = Rect("FoodNodes", food, new Vector2(0f, -27f), new Vector2(450f, 30f));
        foodGraphic = nodes.gameObject.AddComponent<PopulationFoodWebGraphic>(); foodGraphic.raycastTarget = false;
        sources = ScrollText("FoodSources", food, new Vector2(0f, -58f), new Vector2(128f, 30f), 17);
        sources.alignment = TextAlignmentOptions.Top;
        current = Text("CurrentSpecies", food, "", 19, new Vector2(160f, -58f), new Vector2(130f, 30f));
        current.alignment = TextAlignmentOptions.Center;
        predators = ScrollText("Predators", food, new Vector2(322f, -58f), new Vector2(128f, 30f), 17);
        predators.alignment = TextAlignmentOptions.Top;
        trophic = Text("TrophicLevel", food, "", 20, new Vector2(135f, -89f), new Vector2(250f, 30f));
        additional = ScrollText("AdditionalInformation", content, new Vector2(0f, -584f), new Vector2(450f, 65f), 17);
    }

    public void ShowUnavailable(string message)
    {
        foodGraphic.enabled = false;
        speciesName.text = message; count.text = ""; mutation.text = "变异：暂无记录";
        portrait.enabled = true; portrait.sprite = null; portrait.color = new Color(.86f, .88f, .82f, .8f); placeholder.gameObject.SetActive(true);
        SetScrollText(description, "暂无物种介绍"); SetScrollText(additional, "目标已不可用");
        SetScrollText(sources, "--"); SetScrollText(predators, "--"); current.text = "--"; trophic.text = "营养级：--";
        radar.SetValues(0, 0, 0, 0, 0); foreach (var label in labels) label.text = "--";
    }

    public void Refresh(WhiteboxPopulation visual, PopulationData data, BlockInfo block)
    {
        foodGraphic.enabled = data != null && data.ecologicalNiche == EcologicalNiche.Land;
        SpeciesData species = data != null ? data.species : null;
        string name = species != null ? species.SpeciesName :
            data != null && !string.IsNullOrWhiteSpace(data.lineageName) ? data.lineageName : "测试种群";
        if (string.IsNullOrWhiteSpace(name)) name = "未命名物种";
        speciesName.text = name;
        Sprite image = species != null ? species.Portrait : null;
        portrait.sprite = image; portrait.enabled = true; portrait.color = image != null ? Color.white : new Color(.86f, .88f, .82f, .8f);
        placeholder.gameObject.SetActive(image == null);
        count.text = "数量：" + (data != null ? data.speciesAmount : visual.Count);
        mutation.text = "变异：" + (data != null &&
            !string.IsNullOrEmpty(data.lastMutationEffect)
            ? data.lastMutationEffect : "暂无记录");
        SetScrollText(description, species != null ? species.Description : "暂无物种介绍");
        if (data == null)
        {
            radar.SetValues(0, 0, 0, 0, 0); foreach (var label in labels) label.text = "未接入";
            SetScrollText(additional, "所属地块：" + visual.TileId + "\n种群 ID：" + visual.UniqueId + "\n生态数据：未接入");
            relationshipTitle.text = "生态关系待接入"; SetScrollText(sources, "未接入");
            SetScrollText(predators, "未接入"); current.text = name; trophic.text = "营养级：未接入";
            return;
        }
        radar.SetValues(data.fitTemperature, data.fitHumidity, data.movementAbility, data.fertility, data.environmentalFitness);
        int[] values = { data.fitTemperature, data.fitHumidity, data.movementAbility, data.fertility, data.environmentalFitness };
        string[] names = { "适温", "适湿", "运动", "繁殖", "适应度" };
        for (int i = 0; i < 5; i++) labels[i].text = names[i] + " " + values[i];
        SetScrollText(additional, "所属地块：" + visual.TileId + "   生态位：" + data.ecologicalNiche +
            "\n今日出生 / 死亡：" + data.birthsToday + " / " + data.deathsToday +
            "\n体型：" + data.size + "   环境容纳量 K：" +
                data.environmentalCarryingCapacity.ToString("F0") +
            "\n当日食物可供养：" + data.carryingCapacity.ToString("F0") +
            "\n种群 ID：" + data.PopulationId);
        current.text = name; trophic.text = "营养级：L" + data.trophicLevel;
        var foods = new List<string>(); var hunters = new List<string>();
        if (block != null && block.community != null && data.ecologicalNiche == EcologicalNiche.Land)
        {
            relationshipTitle.text = "当前地块食物关系（滚轮查看更多）";
            if (data.trophicLevel == 0) foods.Add("植物");
            foreach (PopulationData other in block.community)
            {
                if (other == null || other.speciesAmount <= 0 || other.ecologicalNiche != EcologicalNiche.Land) continue;
                string otherName = other.species != null ? other.species.SpeciesName : other.lineageName;
                if (FoodWeb.CanEat(data, other) && !foods.Contains(otherName)) foods.Add(otherName);
                if (FoodWeb.CanEat(other, data) && !hunters.Contains(otherName)) hunters.Add(otherName);
            }
            SetScrollText(sources, foods.Count > 0 ? string.Join("\n", foods) : "当前无食物源");
            SetScrollText(predators, hunters.Count > 0 ? string.Join("\n", hunters) : "当前无捕食者");
        }
        else
        {
            relationshipTitle.text = "生态关系待接入";
            SetScrollText(sources, "未接入"); SetScrollText(predators, "未接入");
        }
    }

    private TextMeshProUGUI Text(string name, RectTransform parent, string value, float size, Vector2 position, Vector2 dimensions)
    {
        RectTransform rect = Rect(name, parent, position, dimensions);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = GameUITheme.TMPFont; label.fontSize = size; label.color = new Color(.11f, .16f, .17f);
        label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Truncate;
        label.richText = false; label.text = value; label.raycastTarget = true;
        rect.gameObject.AddComponent<InformationTextDragHandle>().Initialize(window);
        return label;
    }

    private TextMeshProUGUI ScrollText(string name, RectTransform parent, Vector2 position, Vector2 dimensions, float size)
    {
        RectTransform root = Rect(name, parent, position, dimensions);
        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;
        RectTransform viewport = Rect("Viewport", root, Vector2.zero, dimensions);
        var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();
        TextMeshProUGUI text = Text("Content", viewport, "", size, Vector2.zero, dimensions);
        text.overflowMode = TextOverflowModes.Overflow;
        scroll.viewport = viewport; scroll.content = text.rectTransform;
        return text;
    }

    private static void SetScrollText(TextMeshProUGUI text, string value)
    {
        if (text.text == value) return;
        text.text = value;
        RectTransform rect = text.rectTransform;
        float height = text.GetPreferredValues(value, rect.rect.width, float.PositiveInfinity).y;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(((RectTransform)rect.parent).rect.height, height + 4f));
    }

    private static RectTransform Rect(string name, RectTransform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)); var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = position; rect.sizeDelta = size;
        return rect;
    }
}
