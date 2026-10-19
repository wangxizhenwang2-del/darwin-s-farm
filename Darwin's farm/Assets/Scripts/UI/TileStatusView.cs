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
    private static Sprite circleMaskSprite;
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
        var stock = Image("PlantStock", root, new Vector2(300f, 32f), Vector2.zero,
            new Color(.94f, .97f, .87f, .96f));
        var outline = stock.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(.15f, .24f, .18f, .9f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        var track = Rect("Track", stock.rectTransform, Vector2.zero, Vector2.zero);
        track.anchorMin = Vector2.zero; track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(3f, 3f); track.offsetMax = new Vector2(-3f, -3f);
        plantFill = Image("Fill", track, Vector2.zero, Vector2.zero, new Color(.48f, .74f, .43f, 1f)).rectTransform;
        plantFill.anchorMin = Vector2.zero; plantFill.anchorMax = Vector2.one;
        plantFill.offsetMin = plantFill.offsetMax = Vector2.zero;
        plantText = Label("StockText", stock.rectTransform, new Vector2(300f, 32f), Vector2.zero, 20f);
        EnsureToolCards();
        SetVisible(false);
    }

    private void EnsureToolCards()
    {
        foreach (ToolSlot slot in slots)
        {
            var background = slot.root.GetComponent<UnityEngine.UI.Image>();
            if (background == null) background = slot.root.gameObject.AddComponent<UnityEngine.UI.Image>();
            background.color = new Color(.94f, .97f, .87f, .95f);
            background.raycastTarget = false;
            var outline = slot.root.GetComponent<UnityEngine.UI.Outline>();
            if (outline == null) outline = slot.root.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new Color(.15f, .24f, .18f, .7f);
            outline.effectDistance = new Vector2(1f, -1f);
            slot.timeMask.color = new Color(.06f, .08f, .07f, .68f);
        }
    }

    public void SetVisible(bool visible)
    {
        if (visibility != null) visibility.alpha = visible ? 1f : 0f;
    }

    public void Show(EnvironmentReadSnapshot snapshot)
    {
        PlantFraction = snapshot.PlantCapacity > 0 ? Mathf.Clamp01(snapshot.PlantStock / snapshot.PlantCapacity) : 0f;
        plantFill.anchorMax = new Vector2(PlantFraction, 1f);
        plantText.text = "植物 " + Amount(snapshot.PlantStock) + " / " + Amount(snapshot.PlantCapacity);
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
        slot.icon.color = state.Phase == EffectPhase.Returning ? new Color(1f, 1f, 1f, .55f) : Color.white;
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

    private static string Amount(float value) => value >= 10000f ? (value / 10000f).ToString("0.#") + "万" : value.ToString("0");

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
