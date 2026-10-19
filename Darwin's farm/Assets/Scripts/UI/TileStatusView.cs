using DarwinFarm.Environment;
using TMPro;
using UnityEngine;

// One reusable, non-interactive view. Its position is owned by TileStatusPresenter.
public sealed class TileStatusView : MonoBehaviour
{
    [System.Serializable]
    private sealed class ToolSlot
    {
        public RectTransform root;
        public UnityEngine.UI.Image icon, timeMask;
        public TextMeshProUGUI title, phase;
    }

    [SerializeField] private CanvasGroup visibility;
    [SerializeField] private RectTransform toolsRow, plantFill;
    [SerializeField] private TextMeshProUGUI plantText;
    [SerializeField] private ToolSlot[] slots;
    [Header("Plant stock bar")]
    [SerializeField, Min(120f)] private float plantBarWidth = 180f;
    [SerializeField, Range(0f, 1f), Tooltip("植物量条底色透明度：0 为全透明，1 为不透明")]
    private float plantBarBackgroundOpacity = .32f;
    [SerializeField, Range(0f, 12f), Tooltip("植物量条底色和绿色填充的边缘羽化宽度")]
    private float plantBarFeather = 4f;
    [Header("Applied tool cards")]
    [SerializeField, Range(0f, 1f), Tooltip("操作卡片底色透明度")]
    private float toolCardBackgroundOpacity = .32f;
    [SerializeField, Range(0f, 12f), Tooltip("操作卡片底色边缘的羽化宽度")]
    private float toolCardFeather = 4f;
    [SerializeField, Range(0f, 1f), Tooltip("操作图标本身的透明度")]
    private float toolIconOpacity = 1f;
    [SerializeField, Range(0f, 12f), Tooltip("操作图标边缘的柔化范围")]
    private float toolIconFeather = 3f;
    private static Sprite circleMaskSprite;
    private Sprite plantBarSprite, toolCardSprite;
    private float appliedPlantBarFeather = -1f, appliedToolCardFeather = -1f;
    private float appliedPlantBarWidth = -1f, appliedPlantBarOpacity = -1f;
    private float appliedToolCardOpacity = -1f, appliedToolIconOpacity = -1f,
        appliedToolIconFeather = -1f;
    public float PlantFraction { get; private set; }
    public int VisibleToolCount { get; private set; }
    public bool IsVisible => visibility != null && visibility.alpha > 0f;

    public void EnsureLayout()
    {
        if (visibility == null) visibility = GetComponent<CanvasGroup>();
        if (visibility == null) visibility = gameObject.AddComponent<CanvasGroup>();
        visibility.interactable = false;
        visibility.blocksRaycasts = false;
        if (plantText != null && toolsRow != null && slots != null && slots.Length == 3)
        { EnsureToolCards(); return; }

        var root = (RectTransform)transform;
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        root.pivot = new Vector2(.5f, 0f);
        root.sizeDelta = new Vector2(300f, 140f);
        toolsRow = Rect("ActiveTools", root, new Vector2(300f, 100f), new Vector2(0f, 38f));
        var row = toolsRow.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.LowerCenter;
        row.spacing = 6f;
        row.childControlWidth = row.childControlHeight = false;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        slots = new ToolSlot[3];
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = new ToolSlot();
            slot.root = Rect("Tool" + i, toolsRow, new Vector2(94f, 100f), Vector2.zero);
            slot.icon = Image("Icon", slot.root, new Vector2(54f, 54f), new Vector2(0f, 44f), Color.white);
            slot.icon.preserveAspect = true;
            slot.timeMask = Image("TimeMask", slot.icon.rectTransform, new Vector2(54f, 54f), Vector2.zero,
                new Color(.08f, .12f, .1f, .68f));
            slot.timeMask.rectTransform.anchorMin = slot.timeMask.rectTransform.anchorMax = new Vector2(.5f, .5f);
            slot.timeMask.rectTransform.pivot = new Vector2(.5f, .5f);
            slot.timeMask.preserveAspect = true;
            slot.timeMask.type = UnityEngine.UI.Image.Type.Filled;
            slot.timeMask.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            slot.timeMask.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
            slot.timeMask.fillClockwise = true;
            slot.title = Label("ToolName", slot.root, new Vector2(94f, 24f), new Vector2(0f, 22f), 20f);
            slot.phase = Label("TimeRemaining", slot.root, new Vector2(94f, 22f), Vector2.zero, 18f);
            slots[i] = slot;
            slot.root.gameObject.SetActive(false);
        }
        var stock = Image("PlantStock", root, new Vector2(plantBarWidth, 32f), Vector2.zero,
            new Color(.94f, .97f, .87f, plantBarBackgroundOpacity));
        var track = Rect("Track", stock.rectTransform, Vector2.zero, Vector2.zero);
        track.anchorMin = Vector2.zero; track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(3f, 3f); track.offsetMax = new Vector2(-3f, -3f);
        plantFill = Image("Fill", track, Vector2.zero, Vector2.zero, new Color(.48f, .74f, .43f, 1f)).rectTransform;
        plantFill.anchorMin = Vector2.zero; plantFill.anchorMax = Vector2.one;
        plantFill.offsetMin = plantFill.offsetMax = Vector2.zero;
        plantText = Label("StockText", stock.rectTransform,
            new Vector2(plantBarWidth - 8f, 32f), Vector2.zero, 16f);
        EnsureToolCards();
        SetVisible(false);
    }

    private void EnsureToolCards()
    {
        ConfigurePlantBar();
        ConfigureToolCards();
    }

    private void ConfigureToolCards()
    {
        Sprite backgroundSprite = ToolCardSprite();
        foreach (ToolSlot slot in slots)
        {
            var background = slot.root.GetComponent<UnityEngine.UI.Image>();
            if (background == null) background = slot.root.gameObject.AddComponent<UnityEngine.UI.Image>();
            background.color = new Color(.94f, .97f, .87f, toolCardBackgroundOpacity);
            background.sprite = backgroundSprite;
            background.type = UnityEngine.UI.Image.Type.Sliced;
            background.raycastTarget = false;
            var oldOutline = slot.root.GetComponent<UnityEngine.UI.Outline>();
            if (oldOutline != null) oldOutline.enabled = false;
            EnsureBorder(slot.root);
            var iconFeather = slot.icon.GetComponent<UnityEngine.UI.Outline>();
            if (iconFeather == null) iconFeather = slot.icon.gameObject.AddComponent<UnityEngine.UI.Outline>();
            iconFeather.enabled = toolIconFeather > 0f;
            iconFeather.effectColor = new Color(1f, 1f, 1f,
                Mathf.Clamp01(toolIconFeather / 12f) * .22f);
            iconFeather.effectDistance = new Vector2(toolIconFeather * .45f,
                -toolIconFeather * .45f);
            iconFeather.useGraphicAlpha = true;
            slot.timeMask.color = new Color(.06f, .08f, .07f, .68f);
        }
        appliedToolCardOpacity = toolCardBackgroundOpacity;
        appliedToolIconOpacity = toolIconOpacity;
        appliedToolIconFeather = toolIconFeather;
    }

    private void ConfigurePlantBar()
    {
        var stock = plantText.transform.parent.GetComponent<UnityEngine.UI.Image>();
        if (stock != null)
        {
            stock.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, plantBarWidth);
            stock.color = new Color(.94f, .97f, .87f, plantBarBackgroundOpacity);
            stock.sprite = PlantBarSprite();
            stock.type = UnityEngine.UI.Image.Type.Sliced;
            var fill = plantFill.GetComponent<UnityEngine.UI.Image>();
            fill.sprite = stock.sprite;
            fill.type = UnityEngine.UI.Image.Type.Sliced;
            plantText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                plantBarWidth - 8f);
            plantText.enableAutoSizing = true;
            plantText.fontSizeMin = 12f;
            plantText.fontSizeMax = 16f;
            var oldOutline = stock.GetComponent<UnityEngine.UI.Outline>();
            if (oldOutline != null) oldOutline.enabled = false;
            EnsureBorder(stock.rectTransform);
            appliedPlantBarWidth = plantBarWidth;
            appliedPlantBarOpacity = plantBarBackgroundOpacity;
        }
    }

    private void LateUpdate()
    {
        if (plantText != null && (!Mathf.Approximately(appliedPlantBarWidth, plantBarWidth) ||
            !Mathf.Approximately(appliedPlantBarOpacity, plantBarBackgroundOpacity) ||
            !Mathf.Approximately(appliedPlantBarFeather, plantBarFeather)))
            ConfigurePlantBar();
        if (slots != null && slots.Length == 3 &&
            (!Mathf.Approximately(appliedToolCardOpacity, toolCardBackgroundOpacity) ||
             !Mathf.Approximately(appliedToolCardFeather, toolCardFeather) ||
             !Mathf.Approximately(appliedToolIconOpacity, toolIconOpacity) ||
             !Mathf.Approximately(appliedToolIconFeather, toolIconFeather)))
            ConfigureToolCards();
    }

    private static void EnsureBorder(RectTransform parent)
    {
        var frame = parent.Find("Border") as RectTransform;
        if (frame == null) frame = Rect("Border", parent, Vector2.zero, Vector2.zero);
        frame.anchorMin = Vector2.zero;
        frame.anchorMax = Vector2.one;
        frame.offsetMin = frame.offsetMax = Vector2.zero;
        frame.SetAsLastSibling();
        BorderEdge(frame, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -2f), Vector2.zero);
        BorderEdge(frame, "Bottom", Vector2.zero, new Vector2(1f, 0f),
            Vector2.zero, new Vector2(0f, 2f));
        BorderEdge(frame, "Left", Vector2.zero, new Vector2(0f, 1f),
            Vector2.zero, new Vector2(2f, 0f));
        BorderEdge(frame, "Right", new Vector2(1f, 0f), Vector2.one,
            new Vector2(-2f, 0f), Vector2.zero);
    }

    private static void BorderEdge(RectTransform frame, string name, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var edge = frame.Find(name) as RectTransform;
        if (edge == null) edge = Image(name, frame, Vector2.zero, Vector2.zero,
            new Color(.15f, .24f, .18f, .9f)).rectTransform;
        edge.anchorMin = anchorMin;
        edge.anchorMax = anchorMax;
        edge.offsetMin = offsetMin;
        edge.offsetMax = offsetMax;
    }

    public void SetVisible(bool visible)
    {
        if (visibility != null) visibility.alpha = visible ? 1f : 0f;
    }

    public void Show(EnvironmentReadSnapshot snapshot)
    {
        float dailyGrowth = Mathf.Min((float)snapshot.Environment.Recovery.Value,
            Mathf.Max(0f, snapshot.PlantCapacity - snapshot.PlantStock));
        float total = snapshot.PlantStock + dailyGrowth;
        PlantFraction = snapshot.PlantCapacity > 0 ? Mathf.Clamp01(total / snapshot.PlantCapacity) : 0f;
        plantFill.anchorMax = new Vector2(PlantFraction, 1f);
        plantText.text = "植物量：" + (PlantFraction * 100f).ToString("0.#") + "%";
        VisibleToolCount = 0;
        var state = snapshot.Environment;
        AddTool(state.HasLocalTemperature, EnvironmentTechnologyKind.TemperatureChange, state.Temperature, state.IsWater);
        AddTool(state.HasLocalHumidity, EnvironmentTechnologyKind.HumidityChange, state.Humidity, state.IsWater);
        AddTool(state.HasRecoveryCommand, EnvironmentTechnologyKind.PlantRecoveryChange, state.Recovery, state.IsWater);
        for (int i = VisibleToolCount; i < slots.Length; i++) slots[i].root.gameObject.SetActive(false);
        toolsRow.gameObject.SetActive(VisibleToolCount > 0);
    }

    private void AddTool(bool applied, EnvironmentTechnologyKind kind, AttributeSnapshot state, bool water)
    {
        if (!applied || (state.Phase != EffectPhase.Entering && state.Phase != EffectPhase.Holding &&
            state.Phase != EffectPhase.Returning)) return;
        ToolSlot slot = slots[VisibleToolCount++];
        slot.root.gameObject.SetActive(true);
        int tier = state.ToolTier;
        int option = tier >= 2 ? 0 : tier >= 0 ? 1 : tier == -1 ? 2 : 3;
        Sprite sprite = EnvironmentToolArtwork.Current != null ? EnvironmentToolArtwork.Current.Option(kind, option) : null;
        slot.icon.sprite = sprite;
        slot.icon.enabled = sprite != null;
        slot.title.text = EnvironmentTechnologyCatalog.ShortName(kind) +
            (tier == 0 ? "" : tier > 0 ? (tier == 2 ? "++" : "+") : (tier == -2 ? "--" : "-"));
        // Tool artwork is mostly transparent linework. Tinting that sprite cannot
        // shade the icon face, so the timer uses its own opaque circular silhouette.
        slot.timeMask.sprite = CircleMaskSprite();
        slot.timeMask.fillAmount = CoveredFraction(kind, state, water);
        slot.icon.color = new Color(1f, 1f, 1f,
            toolIconOpacity * (state.Phase == EffectPhase.Returning ? .55f : 1f));
        string phase = state.Phase == EffectPhase.Entering ? "生效" : state.Phase == EffectPhase.Holding ? "维持" : "恢复";
        slot.phase.text = phase + " " + Mathf.Max(0, state.RemainingDays) + "天";
    }

    public static float CoveredFraction(EnvironmentTechnologyKind kind, AttributeSnapshot state, bool water)
    {
        if (state.Phase == EffectPhase.Returning) return 1f;
        if (state.Phase != EffectPhase.Holding) return 0f;
        int duration = kind == EnvironmentTechnologyKind.PlantRecoveryChange ?
            (water ? EnvironmentRules.RecoveryHoldDays : EnvironmentRules.LandRecoveryHoldDays) : EnvironmentRules.ClimateHoldDays;
        // AttributeSnapshot.Progress describes the transition track, not the hold timer.
        return 1f - Mathf.Clamp01((float)state.RemainingDays / duration);
    }

    private Sprite PlantBarSprite()
    {
        if (plantBarSprite != null && Mathf.Approximately(appliedPlantBarFeather, plantBarFeather))
            return plantBarSprite;
        if (plantBarSprite != null)
        {
            Destroy(plantBarSprite.texture);
            Destroy(plantBarSprite);
        }
        plantBarSprite = CreateFeatherSprite(plantBarFeather, "Plant Bar Feather");
        appliedPlantBarFeather = plantBarFeather;
        return plantBarSprite;
    }

    private Sprite ToolCardSprite()
    {
        if (toolCardSprite != null && Mathf.Approximately(appliedToolCardFeather, toolCardFeather))
            return toolCardSprite;
        if (toolCardSprite != null)
        {
            Destroy(toolCardSprite.texture);
            Destroy(toolCardSprite);
        }
        toolCardSprite = CreateFeatherSprite(toolCardFeather, "Tool Card Feather");
        appliedToolCardFeather = toolCardFeather;
        return toolCardSprite;
    }

    private static Sprite CreateFeatherSprite(float feather, string name)
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
            name = name, hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float distance = Mathf.Min(x + .5f, y + .5f, size - x - .5f, size - y - .5f);
            byte alpha = (byte)Mathf.RoundToInt((feather <= 0f ? 1f :
                Mathf.Clamp01(distance / feather)) * 255f);
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(12f, 12f, 12f, 12f));
        sprite.name = name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private void OnDestroy()
    {
        if (plantBarSprite != null)
        {
            Destroy(plantBarSprite.texture);
            Destroy(plantBarSprite);
        }
        if (toolCardSprite != null)
        {
            Destroy(toolCardSprite.texture);
            Destroy(toolCardSprite);
        }
    }

    private static Sprite CircleMaskSprite()
    {
        if (circleMaskSprite != null) return circleMaskSprite;
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
            name = "Tile Tool Timer Disc", hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        float center = (size - 1) * .5f;
        float radius = size * .5f - 2f;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
            byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(radius + .5f - distance) * 255f);
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        circleMaskSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(.5f, .5f));
        circleMaskSprite.name = "Tile Tool Timer Disc";
        circleMaskSprite.hideFlags = HideFlags.HideAndDontSave;
        return circleMaskSprite;
    }

    private static RectTransform Rect(string name, RectTransform parent, Vector2 size, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
        rect.pivot = new Vector2(.5f, 0f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private static UnityEngine.UI.Image Image(string name, RectTransform parent, Vector2 size, Vector2 position, Color color)
    {
        var rect = Rect(name, parent, size, position);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI Label(string name, RectTransform parent, Vector2 size, Vector2 position, float fontSize)
    {
        var rect = Rect(name, parent, size, position);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = GameUITheme.TMPFont;
        label.fontSize = fontSize;
        label.color = new Color(.1f, .19f, .13f, 1f);
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }
}
