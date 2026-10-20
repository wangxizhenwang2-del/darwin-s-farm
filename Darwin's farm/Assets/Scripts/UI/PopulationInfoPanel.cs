using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The journal is authored at 525 x 860. Its parent may scale uniformly, never reshape it.
public sealed class PopulationInfoPanel : MonoBehaviour
{
    public const float BoardWidth = 525f;
    public const float BoardHeight = 860f;
    public const float CompactHeight = 250f;
    private static readonly Color Ink = new Color(.17f, .17f, .13f);
    private static readonly Color SoftInk = new Color(.35f, .37f, .28f);
    [SerializeField] private int layoutVersion;
    [SerializeField] private RectTransform content;
    [SerializeField] private InformationWindow window;
    [SerializeField] private Image portrait;
    [SerializeField] private TextMeshProUGUI placeholder, speciesName, count, mutation, description, additional;
    [SerializeField] private TextMeshProUGUI sources, current, predators, trophic, relationshipTitle;
    [SerializeField] private TextMeshProUGUI[] labels;
    [SerializeField] private PopulationRadarChart radar;
    [SerializeField] private RectTransform foodNodes;
    private string foodSignature;
    private RectTransform compactContent;
    private TextMeshProUGUI compactName, compactCount;
    private TextMeshProUGUI[] compactLabels;
    private PopulationRadarChart compactRadar;
    private bool compact;

    public bool IsCompact => compact;

    public int LayoutVersion => layoutVersion;

    public void Initialize(InformationWindow owner)
    {
        window = owner;
        Image board = GetComponent<Image>();
        if (board != null)
        {
            Sprite journal = Resources.Load<Sprite>("GameUI/SpeciesJournal/JournalBoard");
            if (journal != null) board.sprite = journal;
            board.type = Image.Type.Simple;
            board.preserveAspect = true;
            board.raycastTarget = true;
        }
        RectTransform frame = (RectTransform)transform;
        frame.sizeDelta = new Vector2(BoardWidth, BoardHeight);
        if (layoutVersion == 2 && content != null)
        {
            ApplyTypography();
            if (Application.isPlaying) EnsureCompactContent();
            return;
        }
        RebuildJournal();
        if (Application.isPlaying) EnsureCompactContent();
    }

    public void RebuildJournal()
    {
        if (content != null)
        {
            content.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(content.gameObject);
            else DestroyImmediate(content.gameObject);
        }
        // Keep all positions in native board coordinates, including the transparent clip at its top.
        content = Rect("JournalContent", (RectTransform)transform, 0, 0, BoardWidth, BoardHeight);
        content.localScale = Vector3.one;
        var sticker = Resources.Load<Sprite>("GameUI/SpeciesJournal/SectionSticker");
        Section("NameSticker", "物种名称", 77, 145, 177, 62, 33, sticker, out speciesName);
        Section("IntroductionSticker", "简介", 252, 154, 113, 52, 25, sticker, out _);
        Section("EcologySticker", "生态信息", 76, 339, 183, 59, 25, sticker, out _);
        Section("AttributesSticker", "属性", 252, 339, 124, 59, 25, sticker, out _);
        Section("FoodSticker", "食物链", 76, 552, 150, 59, 25, sticker, out _);

        var portraitFrame = Rect("SpeciesPortrait", content, 88, 191, 150, 147);
        portrait = portraitFrame.gameObject.AddComponent<Image>();
        portrait.preserveAspect = true;
        portrait.raycastTarget = false;
        placeholder = Label("PortraitPlaceholder", portraitFrame, "?", 58, 0, 0, 150, 147, TextAlignmentOptions.Center);

        for (int i = 0; i < 5; i++)
            Rule("IntroductionRule" + i, 256, 224 + i * 25, 205, .55f);
        description = Label("Description", content, "暂无物种介绍", 18, 253, 197, 210, 144);
        description.lineSpacing = 1.5f;
        description.overflowMode = TextOverflowModes.Ellipsis;

        count = Label("PopulationCount", content, "数量：--", 26, 87, 388, 158, 36);
        count.fontStyle = FontStyles.Bold;
        mutation = Label("MutationInfo", content, "变异记录：暂无", 18, 87, 426, 158, 28);
        additional = Label("EcologyDetails", content, "", 18, 87, 459, 163, 91);
        additional.lineSpacing = 5f;
        for (int i = 0; i < 4; i++) Rule("EcologyRule" + i, 87, 424 + i * 31, 155, .6f);

        RectTransform chart = Rect("RadarChart", content, 303, 400, 128, 128);
        radar = chart.gameObject.AddComponent<PopulationRadarChart>();
        radar.raycastTarget = false;
        labels = new TextMeshProUGUI[5];
        labels[0] = Label("Fertility", content, "繁殖 --", 15, 339, 381, 100, 25, TextAlignmentOptions.Center);
        labels[1] = Label("Movement", content, "运动 --", 15, 428, 432, 77, 25);
        labels[2] = Label("Humidity", content, "适湿 --", 15, 423, 526, 83, 25);
        labels[3] = Label("Temperature", content, "适温 --", 15, 260, 526, 83, 25);
        labels[4] = Label("Size", content, "体型 --", 15, 252, 431, 75, 25);
        PlaceRadarLabels(labels, 303f, 400f);

        relationshipTitle = Label("RelationshipScope", content, "当前地块 · 捕食关系", 15, 302, 569, 168, 28,
            TextAlignmentOptions.Right);
        sources = Label("FoodSourceTitle", content, "食物来源", 17, 85, 617, 105, 26);
        current = Label("CurrentTitle", content, "当前物种", 17, 211, 617, 108, 26, TextAlignmentOptions.Center);
        predators = Label("PredatorTitle", content, "捕食者", 17, 356, 617, 95, 26, TextAlignmentOptions.Center);
        foodNodes = Rect("FoodNodes", content, 70, 644, 405, 105);
        Label("ArrowToSpecies", foodNodes, "→", 35, 109, 19, 52, 56, TextAlignmentOptions.Center);
        Label("ArrowToPredators", foodNodes, "→", 35, 256, 19, 52, 56, TextAlignmentOptions.Center);
        trophic = Label("TrophicLevel", content, "营养级：--", 16, 205, 754, 138, 27,
            TextAlignmentOptions.Center);
        layoutVersion = 2;
        foodSignature = null;
        ApplyTypography();
    }

    public void ApplyTypography()
    {
        if (layoutVersion != 2 || content == null) return;
        speciesName.fontSize = 36;
        speciesName.enableAutoSizing = true;
        speciesName.fontSizeMin = 21;
        speciesName.fontSizeMax = 36;
        // The title belongs in the painted part of the sticker, clear of its leaf.
        Place(speciesName.rectTransform, 13, 10, 140, 42);
        speciesName.alignment = TextAlignmentOptions.Center;
        description.fontSize = 18; // The longer introduction keeps its five ruled lines.
        description.textWrappingMode = TextWrappingModes.Normal;
        count.fontSize = 29;
        count.enableAutoSizing = true;
        count.fontSizeMin = 23;
        count.fontSizeMax = 29;
        mutation.fontSize = 20;
        additional.fontSize = 20;
        additional.lineSpacing = 3f;
        foreach (var label in labels) label.fontSize = 18;
        PlaceRadarLabels(labels, 303f, 400f);
        sources.fontSize = current.fontSize = predators.fontSize = 20;
        trophic.fontSize = 19;
        relationshipTitle.fontSize = 17;
        relationshipTitle.enableAutoSizing = true;
        relationshipTitle.fontSizeMin = 14;
        relationshipTitle.fontSizeMax = 17;
        SetSectionSize("IntroductionSticker", 27);
        SetSectionSize("EcologySticker", 27);
        SetSectionSize("AttributesSticker", 27);
        SetSectionSize("FoodSticker", 27);
        SetTop("FoodSticker", 570);
        relationshipTitle.rectTransform.anchoredPosition = new Vector2(302, -587);
        sources.rectTransform.anchoredPosition = new Vector2(85, -635);
        current.rectTransform.anchoredPosition = new Vector2(211, -635);
        predators.rectTransform.anchoredPosition = new Vector2(356, -635);
        foodNodes.anchoredPosition = new Vector2(70, -662);
        trophic.rectTransform.anchoredPosition = new Vector2(205, -772);
    }

    private void SetSectionSize(string name, float size)
    {
        var caption = content.Find(name + "/Caption")?.GetComponent<TextMeshProUGUI>();
        if (caption != null) caption.fontSize = size;
    }

    private void SetTop(string name, float y)
    {
        var section = content.Find(name) as RectTransform;
        if (section != null) section.anchoredPosition = new Vector2(section.anchoredPosition.x, -y);
    }

    public void ShowUnavailable(string message)
    {
        if (layoutVersion != 2) Initialize(window);
        speciesName.text = message;
        portrait.sprite = null;
        portrait.enabled = false;
        placeholder.gameObject.SetActive(true);
        count.text = "数量：--";
        mutation.text = "变异记录：暂无";
        additional.text = "地块：--\n生/死：--\n容量 K：--";
        description.text = "暂无物种介绍";
        radar.SetValues(0, 0, 0, 0, 0);
        radar.SetBaseline(null);
        foreach (var label in labels) label.text = "--";
        trophic.text = "营养级：--";
        relationshipTitle.text = "当前地块 · 捕食关系";
        ClearDynamicNodes();
        if (Application.isPlaying)
        {
            EnsureCompactContent();
            compactName.text = message;
            compactCount.text = "数量：--";
            compactRadar.SetBaseline(null);
            compactRadar.SetTrackedTarget(null);
            compactRadar.SetValues(0, 0, 0, 0, 0);
            foreach (var label in compactLabels) label.text = "--";
        }
    }

    public void Refresh(WhiteboxPopulation visual, PopulationData data, BlockInfo block)
    {
        if (layoutVersion != 2) Initialize(window);
        SpeciesData species = data != null ? data.species : null;
        string name = species != null ? species.SpeciesName :
            data != null && !string.IsNullOrWhiteSpace(data.lineageName) ? data.lineageName : "测试种群";
        speciesName.text = string.IsNullOrWhiteSpace(name) ? "未命名物种" : name;
        Sprite image = species != null ? species.Portrait : null;
        portrait.sprite = image;
        portrait.enabled = image != null;
        placeholder.gameObject.SetActive(image == null);
        count.text = "数量：" + (data != null ? data.speciesAmount : visual.Count);
        EnsureCompactContent();
        compactName.text = speciesName.text;
        compactCount.text = count.text;
        mutation.text = "变异记录：暂无";
        description.text = species != null ? species.EcologicalTrivia : "暂无物种介绍";
        if (data == null)
        {
            additional.text = "地块：" + visual.TileId + "\n生态数据：未接入";
            radar.SetValues(0, 0, 0, 0, 0);
            radar.SetBaseline(null);
            foreach (var label in labels) label.text = "--";
            trophic.text = "营养级：--";
            ClearDynamicNodes();
            compactRadar.SetBaseline(null);
            compactRadar.SetTrackedTarget(null);
            compactRadar.SetValues(0, 0, 0, 0, 0);
            foreach (var label in compactLabels) label.text = "--";
            return;
        }
        additional.text = "地块：" + visual.TileId +
            "\n生/死：" + data.birthsToday + "/" + data.deathsToday +
            "\n容量 K：" + data.carryingCapacity.ToString("F0");
        radar.SetBaseline(species);
        radar.SetTrackedTarget(SpeciesTrackingState.IsDirectSource(species) ? SpeciesTrackingState.Target : null);
        radar.SetValues(data.fitTemperature, data.fitHumidity, data.movementAbility, data.fertility, data.size);
        compactRadar.SetBaseline(species);
        compactRadar.SetTrackedTarget(SpeciesTrackingState.IsDirectSource(species) ? SpeciesTrackingState.Target : null);
        compactRadar.SetValues(data.fitTemperature, data.fitHumidity, data.movementAbility, data.fertility, data.size);
        int[] values = { data.fertility, data.movementAbility, data.fitHumidity, data.fitTemperature, data.size };
        string[] names = { "繁殖", "运动", "适湿", "适温", "体型" };
        SpeciesData target = SpeciesTrackingState.IsDirectSource(species) ? SpeciesTrackingState.Target : null;
        int[] targetValues = target != null ? new[] { target.baseFertility, target.baseMovementAbility,
            target.baseFitHumidity, target.baseFitTemperature, target.baseSize } : null;
        int[] traitIndices = { 4, 2, 1, 0, 3 };
        int tolerance = FindFirstObjectByType<SimulationController>()?.ReadLandRules()?.evolutionTraitTolerance ?? 5;
        for (int i = 0; i < 5; i++)
        {
            string hint = "";
            if (target != null && target.HasRevealedTrait(traitIndices[i]) &&
                !(data.ecologicalNiche == EcologicalNiche.Water && (i == 2 || i == 3)))
            {
                int difference = values[i] - targetValues[i];
                hint = difference < -tolerance ? "<color=#BF302A>需提升</color>" :
                    difference > tolerance ? "<color=#378047>需降低</color>" :
                    "<color=#3269A0>需保持</color>";
            }
            string value = names[i] + " " + values[i];
            labels[i].text = value + (hint.Length == 0 ? "" : i == 0 ? "  " + hint : "\n" + hint);
            compactLabels[i].text = value + (hint.Length == 0 ? "" : i == 0 ? "  " + hint : "\n" + hint);
            labels[i].alignment = compactLabels[i].alignment = i == 0 ?
                TextAlignmentOptions.Top : i == 3 || i == 4 ?
                TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
            labels[i].rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, hint.Length == 0 || i == 0 ? 26f : 48f);
        }
        trophic.text = "营养级：L" + data.trophicLevel;
        UpdateFoodChain(species, data, block);
    }

    public void SetCompact(bool value)
    {
        EnsureCompactContent();
        compact = value;
        content.gameObject.SetActive(!value);
        compactContent.gameObject.SetActive(value);
        var frame = (RectTransform)transform;
        frame.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, BoardWidth);
        frame.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, value ? CompactHeight : BoardHeight);
        Image board = GetComponent<Image>();
        if (board != null)
        {
            board.sprite = value ? PanelParchmentArt.CroppedPaper :
                Resources.Load<Sprite>("GameUI/SpeciesJournal/JournalBoard");
            board.type = value && board.sprite != null && board.sprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced : Image.Type.Simple;
            board.preserveAspect = !value;
        }
    }

    private void EnsureCompactContent()
    {
        if (compactContent != null) return;
        compactContent = Rect("CompactJournal", (RectTransform)transform, 0, 0, BoardWidth, CompactHeight);
        var nameSticker = Rect("CompactNameSticker", compactContent, 49, 67, 183, 65);
        var nameBrush = nameSticker.gameObject.AddComponent<Image>();
        nameBrush.sprite = Resources.Load<Sprite>("GameUI/SpeciesJournal/SectionSticker");
        nameBrush.preserveAspect = true;
        nameBrush.raycastTarget = false;
        compactName = Label("CompactName", compactContent, "物种名称", 34, 59, 72, 144, 51,
            TextAlignmentOptions.Center);
        compactName.fontStyle = FontStyles.Bold;
        compactName.enableAutoSizing = true;
        compactName.fontSizeMin = 22;
        compactName.fontSizeMax = 34;
        compactCount = Label("CompactCount", compactContent, "数量：--", 25, 62, 151, 163, 42,
            TextAlignmentOptions.Center);
        var divider = Rect("CompactDivider", compactContent, 244, 30, 1, 190);
        var dividerImage = divider.gameObject.AddComponent<Image>();
        dividerImage.color = new Color(.38f, .43f, .3f, .3f);
        dividerImage.raycastTarget = false;
        var chart = Rect("CompactRadar", compactContent, 306, 59, 128, 128);
        compactRadar = chart.gameObject.AddComponent<PopulationRadarChart>();
        compactRadar.raycastTarget = false;
        compactLabels = new TextMeshProUGUI[5];
        compactLabels[0] = Label("CompactFertility", compactContent, "繁殖 --", 17, 298, 24, 144, 32,
            TextAlignmentOptions.Top);
        compactLabels[1] = Label("CompactMovement", compactContent, "运动 --", 17, 434, 88, 86, 48);
        compactLabels[2] = Label("CompactHumidity", compactContent, "适湿 --", 17, 413, 174, 106, 48);
        compactLabels[3] = Label("CompactTemperature", compactContent, "适温 --", 17, 234, 174, 94, 48,
            TextAlignmentOptions.TopRight);
        compactLabels[4] = Label("CompactSize", compactContent, "体型 --", 17, 228, 88, 78, 48,
            TextAlignmentOptions.TopRight);
        PlaceRadarLabels(compactLabels, 306f, 59f);
        compactContent.gameObject.SetActive(compact);
    }

    // The chart has a 128-unit square and vertices at 46% of its half-width.
    // Each label's inner edge stays outside its adjacent vertex, including
    // the second line shown while following an evolution target.
    private static void PlaceRadarLabels(TextMeshProUGUI[] traitLabels, float chartX, float chartY)
    {
        if (traitLabels == null || traitLabels.Length != 5) return;
        bool small = chartY < 100f;
        float top = chartY - (small ? 37f : 36f);
        Place(traitLabels[0].rectTransform, chartX - 24f, top, 176f, 30f);
        Place(traitLabels[1].rectTransform, chartX + 128f, chartY + (small ? 29f : 27f),
            80f, 48f);
        Place(traitLabels[2].rectTransform, chartX + 107f, chartY + 114f,
            94f, 48f);
        Place(traitLabels[3].rectTransform, chartX - 69f, chartY + 114f, 90f, 48f);
        Place(traitLabels[4].rectTransform, chartX - 78f, chartY + (small ? 29f : 27f),
            78f, 48f);
        traitLabels[0].alignment = TextAlignmentOptions.Top;
        traitLabels[1].alignment = traitLabels[2].alignment = TextAlignmentOptions.TopLeft;
        traitLabels[3].alignment = traitLabels[4].alignment = TextAlignmentOptions.TopRight;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private void UpdateFoodChain(SpeciesData species, PopulationData data, BlockInfo block)
    {
        var prey = new List<SpeciesData>();
        var hunters = new List<SpeciesData>();
        var setup = FindFirstObjectByType<WhiteboxBiowebTestSetup>();
        if (species != null)
        {
            if (species.preySpecies != null)
                foreach (var item in species.preySpecies)
                    if (item != null && !prey.Contains(item)) prey.Add(item);
            if (setup != null && setup.MapSpecies != null)
                foreach (var candidate in setup.MapSpecies)
                    if (candidate != null && candidate.preySpecies != null &&
                        candidate.preySpecies.Contains(species) && !hunters.Contains(candidate))
                        hunters.Add(candidate);
        }
        if (block != null && block.community != null)
            foreach (var other in block.community)
            {
                if (other == null || other.species == null || other.species == species) continue;
                if (FoodWeb.CanEat(data, other) && !prey.Contains(other.species)) prey.Add(other.species);
                if (FoodWeb.CanEat(other, data) && !hunters.Contains(other.species)) hunters.Add(other.species);
            }
        var signature = new StringBuilder(species != null ? species.GetInstanceID().ToString() : "none");
        foreach (var item in prey) signature.Append("/p").Append(item.GetInstanceID())
            .Append(item.HasEverUnlocked).Append(IsPresent(item, block));
        foreach (var item in hunters) signature.Append("/h").Append(item.GetInstanceID())
            .Append(item.HasEverUnlocked).Append(IsPresent(item, block));
        string key = signature.ToString();
        if (key == foodSignature) return;
        ClearDynamicNodes();
        foodSignature = key;
        relationshipTitle.text = block != null ? "当前地块 · 捕食关系" : "食物关系";
        if (prey.Count == 0) MakeNode("Plant", 28, 16, 49, null, true, false, "草");
        else DrawGroup(prey, block, 0, 106, "Prey");
        MakeNode("CurrentSpecies", 163, 13, 55, species, true, false, "?");
        DrawGroup(hunters, block, 306, 102, "Predator");
    }

    private void DrawGroup(List<SpeciesData> species, BlockInfo block, float x, float width, string prefix)
    {
        int count = Mathf.Min(5, species.Count);
        if (count == 0) { MakeNode(prefix + "None", x + width / 2 - 24, 17, 48, null, false, true, "—"); return; }
        // Three form a pyramid; five interleave as two above three.
        for (int i = 0; i < count; i++)
        {
            int row, column, columns;
            if (count == 1) { row = 0; column = 0; columns = 1; }
            else if (count == 2) { row = 0; column = i; columns = 2; }
            else if (count == 3) { row = i == 0 ? 0 : 1; column = i == 0 ? 0 : i - 1; columns = row == 0 ? 1 : 2; }
            else if (count == 4) { row = i / 2; column = i % 2; columns = 2; }
            else { row = i < 2 ? 0 : 1; column = i < 2 ? i : i - 2; columns = row == 0 ? 2 : 3; }
            float diameter = count >= 5 ? 39 : count >= 3 ? 44 : 49;
            float gap = 3;
            float rowWidth = columns * diameter + (columns - 1) * gap;
            float px = x + (width - rowWidth) / 2 + column * (diameter + gap);
            float py = count >= 3 ? 4 + row * 47 : 16;
            SpeciesData item = species[i];
            MakeNode(prefix + i, px, py, diameter, item,
                item.HasEverUnlocked && IsPresent(item, block), !item.HasEverUnlocked, "?");
        }
        if (species.Count > 5)
            Label(prefix + "More", foodNodes, "+" + (species.Count - 5), 13, x + width - 18, 84, 23, 21);
    }

    private static bool IsPresent(SpeciesData species, BlockInfo block)
    {
        if (block == null || block.community == null) return false;
        foreach (var population in block.community)
            if (population != null && population.species == species && population.speciesAmount > 0) return true;
        return false;
    }

    private void MakeNode(string name, float x, float y, float diameter, SpeciesData species,
        bool present, bool locked, string fallback)
    {
        var node = Rect(name, foodNodes, x, y, diameter, diameter);
        Sprite circle = Resources.Load<Sprite>("GameUI/SpeciesCircle");
        var maskImage = node.gameObject.AddComponent<Image>();
        maskImage.sprite = circle;
        maskImage.color = new Color(.91f, .91f, .80f);
        maskImage.raycastTarget = false;
        var mask = node.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;
        if (!locked && species != null && species.Portrait != null)
        {
            var picture = Rect("HeadPortrait", node, -diameter * .13f, -diameter * .02f,
                diameter * 1.26f, diameter * 1.75f);
            var image = picture.gameObject.AddComponent<Image>();
            image.sprite = species.Portrait;
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (!present)
            {
                Material gray = Resources.Load<Material>("GameUI/SpeciesJournal/GrayscalePortrait");
                if (gray != null) image.material = gray;
                image.color = new Color(.77f, .77f, .77f);
            }
        }
        else
        {
            var question = Label("Symbol", node, fallback, locked ? 27 : 25, 0, 0, diameter, diameter,
                TextAlignmentOptions.Center);
            question.color = SoftInk;
        }
        var outline = Rect("PencilOutline", foodNodes, x - 1, y - 1, diameter + 2, diameter + 2);
        var ring = outline.gameObject.AddComponent<Image>();
        ring.sprite = circle;
        ring.type = Image.Type.Simple;
        ring.color = new Color(.28f, .31f, .23f, .18f);
        ring.raycastTarget = false;
        outline.SetSiblingIndex(node.GetSiblingIndex());
    }

    private void ClearDynamicNodes()
    {
        foodSignature = null;
        if (foodNodes == null) return;
        for (int i = foodNodes.childCount - 1; i >= 0; i--)
        {
            Transform child = foodNodes.GetChild(i);
            if (child.name.StartsWith("Arrow")) continue;
            child.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }
    }

    private void Section(string id, string caption, float x, float y, float w, float h, int size,
        Sprite sticker, out TextMeshProUGUI label)
    {
        var rect = Rect(id, content, x, y, w, h);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sticker;
        image.preserveAspect = true;
        image.raycastTarget = false;
        label = Label("Caption", rect, caption, size, 16, 9, w - 29, h - 15);
    }

    private void Rule(string id, float x, float y, float width, float height)
    {
        var rule = Rect(id, content, x, y, width, height);
        var image = rule.gameObject.AddComponent<Image>();
        image.color = new Color(.66f, .49f, .34f, .32f);
        image.raycastTarget = false;
    }

    private TextMeshProUGUI Label(string id, RectTransform parent, string value, float size,
        float x, float y, float w, float h, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
    {
        var rect = Rect(id, parent, x, y, w, h);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = Resources.Load<TMP_FontAsset>("GameUI/SpeciesJournal/HandwrittenJournal") ??
            GameUITheme.HandwrittenTMPFont;
        text.fontSize = size;
        text.color = Ink;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Truncate;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    private static RectTransform Rect(string id, RectTransform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(id, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }
}
