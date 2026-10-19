using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A small, screen-safe overlay using the journal's existing paper and button art.
public sealed class TutorialBubbleView : MonoBehaviour
{
    private Canvas canvas;
    private RectTransform panel, viewport, content;
    private ScrollRect scroll;
    private TextMeshProUGUI title, body;
    private Button continueButton;
    private Button skipButton;
    private string currentTitle = "", currentBody = "";
    private bool canContinue, canSkip = true;
    private int lastWidth, lastHeight;
    private Rect lastSafeArea;

    public static TutorialBubbleView Create(Transform parent, Action onContinue, Action onSkip)
    {
        var root = new GameObject("Tutorial bubble", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(parent, false);
        var view = root.AddComponent<TutorialBubbleView>();
        view.canvas = root.GetComponent<Canvas>();
        view.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        view.canvas.overrideSorting = true;
        view.canvas.sortingOrder = 1000;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = .5f;

        view.panel = NewRect("Paper bubble", root.transform);
        var background = view.panel.gameObject.AddComponent<Image>();
        background.sprite = Resources.Load<Sprite>("DarwinJournal/PaperPanel");
        background.type = Image.Type.Sliced;
        background.raycastTarget = true;
        view.panel.anchorMin = view.panel.anchorMax = new Vector2(.5f, 0f);
        view.panel.pivot = new Vector2(.5f, 0f);

        view.title = NewText("Lesson title", view.panel, 23, FontStyles.Bold,
            new Color(.18f, .32f, .22f));
        view.title.rectTransform.anchorMin = new Vector2(0f, 1f);
        view.title.rectTransform.anchorMax = new Vector2(1f, 1f);
        view.title.rectTransform.pivot = new Vector2(0f, 1f);
        view.title.rectTransform.offsetMin = new Vector2(29f, -57f);
        view.title.rectTransform.offsetMax = new Vector2(-58f, -20f);
        view.title.enableWordWrapping = false;
        view.title.enableAutoSizing = true;
        view.title.fontSizeMin = 16f;
        view.title.fontSizeMax = 23f;

        view.viewport = NewRect("Text viewport", view.panel);
        view.viewport.anchorMin = Vector2.zero;
        view.viewport.anchorMax = Vector2.one;
        var viewportImage = view.viewport.gameObject.AddComponent<Image>();
        viewportImage.color = Color.clear;
        view.viewport.gameObject.AddComponent<RectMask2D>();
        view.scroll = view.viewport.gameObject.AddComponent<ScrollRect>();
        view.scroll.horizontal = false;
        view.scroll.vertical = true;
        view.scroll.movementType = ScrollRect.MovementType.Clamped;
        view.scroll.viewport = view.viewport;
        view.content = NewRect("Text content", view.viewport);
        view.content.anchorMin = new Vector2(0f, 1f);
        view.content.anchorMax = new Vector2(1f, 1f);
        view.content.pivot = new Vector2(.5f, 1f);
        view.scroll.content = view.content;
        view.body = NewText("Lesson text", view.content, 22, FontStyles.Normal,
            new Color(.23f, .29f, .21f));
        view.body.rectTransform.anchorMin = Vector2.zero;
        view.body.rectTransform.anchorMax = Vector2.one;
        view.body.rectTransform.offsetMin = view.body.rectTransform.offsetMax = Vector2.zero;
        view.body.enableWordWrapping = true;
        view.body.richText = true;

        view.skipButton = NewButton("Skip tutorial", view.panel, "×", onSkip, new Vector2(42f, 38f),
            new Vector2(-12f, -10f), true);
        view.continueButton = NewButton("Continue lesson", view.panel, "继续", onContinue,
            new Vector2(110f, 39f), new Vector2(-20f, 12f), false);
        view.gameObject.SetActive(false);
        return view;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, int size,
        FontStyles style, Color color)
    {
        var text = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = GameUITheme.HandwrittenTMPFont;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.raycastTarget = false;
        text.enableAutoSizing = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        return text;
    }

    private static Button NewButton(string name, RectTransform parent, string caption,
        Action callback, Vector2 size, Vector2 offset, bool top)
    {
        var rect = NewRect(name, parent);
        rect.anchorMin = rect.anchorMax = top ? new Vector2(1f, 1f) : new Vector2(1f, 0f);
        rect.pivot = top ? new Vector2(1f, 1f) : new Vector2(1f, 0f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = Resources.Load<Sprite>("DarwinJournal/PaperButton");
        image.type = Image.Type.Sliced;
        var button = rect.gameObject.AddComponent<Button>();
        button.onClick.AddListener(() => callback?.Invoke());
        var text = NewText("Label", rect, top ? 28 : 21, FontStyles.Bold,
            new Color(.16f, .35f, .23f));
        text.alignment = TextAlignmentOptions.Center;
        text.text = caption;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        return button;
    }

    public void SetLesson(string heading, string message, bool showContinue, bool allowSkip)
    {
        if (currentTitle == (heading ?? "") && currentBody == (message ?? "") &&
            canContinue == showContinue && canSkip == allowSkip) return;
        currentTitle = heading ?? "";
        currentBody = message ?? "";
        canContinue = showContinue;
        canSkip = allowSkip;
        title.text = currentTitle;
        body.text = currentBody;
        continueButton.gameObject.SetActive(canContinue);
        skipButton.interactable = canSkip;
        Resize();
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf == visible) return;
        gameObject.SetActive(visible);
        if (visible) { Canvas.ForceUpdateCanvases(); Resize(); }
    }

    public bool TryGetScreenRect(out Rect rect)
    {
        rect = default;
        if (panel == null || !gameObject.activeInHierarchy) return false;
        Canvas.ForceUpdateCanvases();
        var corners = new Vector3[4];
        panel.GetWorldCorners(corners);
        Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        Vector2 topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        rect = Rect.MinMaxRect(bottomLeft.x, bottomLeft.y, topRight.x, topRight.y);
        return true;
    }

    private void Update()
    {
        if (Screen.width != lastWidth || Screen.height != lastHeight ||
            Screen.safeArea != lastSafeArea) Resize();
    }

    private void Resize()
    {
        if (canvas == null || panel == null) return;
        lastWidth = Screen.width;
        lastHeight = Screen.height;
        lastSafeArea = Screen.safeArea;
        float scale = Mathf.Max(.01f, canvas.scaleFactor);
        Rect safe = Screen.safeArea;
        float safeWidth = safe.width / scale;
        float safeHeight = safe.height / scale;
        float maxWidth = Mathf.Min(820f, safeWidth - 24f);
        float naturalWidth = Mathf.Max(title.GetPreferredValues(currentTitle, 10000f, 0f).x,
            body.GetPreferredValues(currentBody, 10000f, 0f).x) + 94f;
        float width = Mathf.Clamp(naturalWidth, Mathf.Min(340f, maxWidth), maxWidth);
        float textWidth = Mathf.Max(100f, width - 58f);
        float bodyHeight = body.GetPreferredValues(currentBody, textWidth, 0f).y + 8f;
        float footer = canContinue ? 55f : 21f;
        float maxHeight = Mathf.Max(130f, safeHeight * .58f);
        float height = Mathf.Min(maxHeight, 58f + bodyHeight + footer);
        panel.sizeDelta = new Vector2(width, height);
        panel.anchoredPosition = new Vector2(
            (safe.center.x - Screen.width * .5f) / scale, safe.yMin / scale + 15f);
        viewport.offsetMin = new Vector2(29f, footer);
        viewport.offsetMax = new Vector2(-29f, -58f);
        content.sizeDelta = new Vector2(0f, bodyHeight);
        scroll.verticalNormalizedPosition = 1f;
    }
}
