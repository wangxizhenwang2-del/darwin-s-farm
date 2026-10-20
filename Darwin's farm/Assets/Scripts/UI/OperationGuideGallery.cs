using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// The six image pages are kept as textures so the viewer can zoom into their
// original detail. Everything is attached to the existing tutorial Canvas.
public sealed class OperationGuideGallery : MonoBehaviour
{
    private static readonly string[] DesktopTitles =
    {
        "农场主画面", "信息面板", "环境工具与扩建",
        "进化网与目标", "食物网与教程", "PC 键位操作表"
    };
    private static readonly string[] MobileTitles =
    {
        "认识农场", "进化网与目标", "读懂信息面板",
        "使用环境工具", "建造地块", "管理小面板"
    };

    private RectTransform galleryRoot;
    private RectTransform[] cards;
    private GameObject viewerOverlay;
    private OperationGuideImageViewer viewer;
    private TMP_Text viewerTitle;
    private Texture2D[] pages;
    private string[] titles;

    public bool IsViewing => viewerOverlay != null && viewerOverlay.activeSelf;

    public void Build(RectTransform paper, RectTransform safeRoot, TMP_Text fontSource)
    {
        bool mobile = Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.IPhonePlayer;
        titles = mobile ? MobileTitles : DesktopTitles;
        string imagePrefix = mobile ? "手机教程-" : "PC教程-";
        pages = new Texture2D[titles.Length];
        galleryRoot = Rect("OperationGuideGallery", paper);
        cards = new RectTransform[titles.Length];
        for (int i = 0; i < titles.Length; i++)
        {
            int index = i;
            pages[i] = Resources.Load<Texture2D>(
                "DarwinJournal/OperationGuide/" + imagePrefix + (i + 1).ToString("00"));
            if (pages[i] == null)
                Debug.LogError("Operation guide image missing: " + imagePrefix + (i + 1), this);

            var card = Rect("Guide" + (i + 1), galleryRoot);
            cards[i] = card;
            var background = card.gameObject.AddComponent<Image>();
            background.color = new Color32(237, 236, 212, 255);
            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => Open(index));

            var preview = Rect("Preview", card);
            var raw = preview.gameObject.AddComponent<RawImage>();
            raw.texture = pages[i];
            raw.raycastTarget = false;
            var label = Text("Caption", card, fontSource);
            label.text = (i + 1).ToString("00") + "  " + titles[i];
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color32(43, 73, 55, 255);
            label.fontSize = 19;
        }

        var overlay = Rect("OperationGuideViewer", safeRoot);
        Stretch(overlay);
        viewerOverlay = overlay.gameObject;
        var shade = overlay.gameObject.AddComponent<Image>();
        shade.color = new Color(0.08f, 0.12f, 0.10f, 0.94f);
        var backdropTap = overlay.gameObject.AddComponent<OperationGuideBackdropTap>();

        viewerTitle = Text("ImageTitle", overlay, fontSource);
        viewerTitle.alignment = TextAlignmentOptions.Center;
        viewerTitle.fontSize = 29;
        viewerTitle.color = Color.white;
        viewerTitle.rectTransform.anchorMin = viewerTitle.rectTransform.anchorMax = new Vector2(.5f, 1f);
        viewerTitle.rectTransform.sizeDelta = new Vector2(800, 54);
        viewerTitle.rectTransform.anchoredPosition = new Vector2(0, -42);

        var close = Rect("CloseImage", overlay);
        close.anchorMin = close.anchorMax = new Vector2(1, 1);
        close.sizeDelta = new Vector2(68, 68);
        close.anchoredPosition = new Vector2(-56, -48);
        var closeImage = close.gameObject.AddComponent<Image>();
        closeImage.color = new Color32(246, 244, 225, 255);
        var closeButton = close.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeImage;
        closeButton.onClick.AddListener(() =>
        {
            if (viewer.CanTapClose) CloseViewer();
        });
        var cross = Text("Cross", close, fontSource);
        Stretch(cross.rectTransform);
        cross.text = "×";
        cross.fontSize = 42;
        cross.alignment = TextAlignmentOptions.Center;
        cross.color = new Color32(33, 68, 48, 255);
        cross.raycastTarget = false;

        var viewport = Rect("ImageViewport", overlay);
        viewport.anchorMin = new Vector2(.04f, .10f);
        viewport.anchorMax = new Vector2(.96f, .88f);
        viewport.offsetMin = viewport.offsetMax = Vector2.zero;
        var surface = viewport.gameObject.AddComponent<Image>();
        surface.color = new Color(1, 1, 1, .05f);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewer = viewport.gameObject.AddComponent<OperationGuideImageViewer>();
        var picture = Rect("GuideImage", viewport);
        var rawImage = picture.gameObject.AddComponent<RawImage>();
        rawImage.raycastTarget = false;
        viewer.Initialize(viewport, picture, rawImage,
            safeRoot.GetComponentInParent<Canvas>(), CloseViewer);
        backdropTap.Initialize(viewer, CloseViewer);

        var hint = Text("ZoomHint", overlay, fontSource);
        hint.text = Application.isMobilePlatform
            ? "双指缩放   ·   单指拖动   ·   轻点关闭"
            : "滚轮缩放   ·   拖动平移   ·   任意键关闭";
        hint.fontSize = 21;
        hint.alignment = TextAlignmentOptions.Center;
        hint.color = new Color32(238, 238, 217, 255);
        hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(.5f, 0);
        hint.rectTransform.sizeDelta = new Vector2(840, 44);
        hint.rectTransform.anchoredPosition = new Vector2(0, 42);

        viewerOverlay.SetActive(false);
        galleryRoot.gameObject.SetActive(false);
    }

    public void Layout(bool portrait)
    {
        if (galleryRoot == null) return;
        galleryRoot.sizeDelta = portrait ? new Vector2(596, 690) : new Vector2(650, 420);
        galleryRoot.anchoredPosition = portrait ? new Vector2(0, -102) : new Vector2(120, -60);
        for (int i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            card.sizeDelta = portrait ? new Vector2(282, 198) : new Vector2(304, 126);
            card.anchoredPosition = portrait
                ? new Vector2(i % 2 == 0 ? -148 : 148, 216 - (i / 2) * 216)
                : new Vector2(i % 2 == 0 ? -162 : 162, 138 - (i / 2) * 140);
            var preview = (RectTransform)card.Find("Preview");
            preview.sizeDelta = portrait ? new Vector2(264, 149) : new Vector2(186, 105);
            preview.anchoredPosition = portrait ? new Vector2(0, 17) : new Vector2(-54, 0);
            var label = ((TMP_Text)card.Find("Caption").GetComponent<TMP_Text>()).rectTransform;
            label.sizeDelta = portrait ? new Vector2(card.sizeDelta.x - 12, 30) : new Vector2(100, 80);
            label.anchoredPosition = portrait
                ? new Vector2(0, -card.sizeDelta.y / 2 + 18) : new Vector2(96, 0);
        }
    }

    public void SetVisible(bool visible)
    {
        if (galleryRoot != null) galleryRoot.gameObject.SetActive(visible);
        if (!visible) CloseViewer();
    }

    public void CloseViewer()
    {
        if (viewerOverlay != null) viewerOverlay.SetActive(false);
        if (viewer != null) viewer.ResetGesture();
    }

    private void Open(int index)
    {
        if (index < 0 || index >= pages.Length || pages[index] == null) return;
        viewerTitle.text = (index + 1).ToString("00") + "  " + titles[index];
        viewerOverlay.SetActive(true);
        viewerOverlay.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        viewer.Show(pages[index]);
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.layer = LayerMask.NameToLayer("UI");
        var rect = (RectTransform)obj.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        return rect;
    }

    private static TMP_Text Text(string name, Transform parent, TMP_Text source)
    {
        var rect = Rect(name, parent);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = source.font;
        label.enableAutoSizing = false;
        label.raycastTarget = false;
        return label;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}

public sealed class OperationGuideImageViewer : MonoBehaviour, IScrollHandler,
    IDragHandler, IPointerDownHandler, IPointerClickHandler
{
    private RectTransform viewport, picture;
    private RawImage image;
    private Canvas canvas;
    private float zoom = 1f;
    private float lastPinchDistance;
    private Vector2 lastPinchCenter;
    private bool pinching;
    private bool pinchInProgress;
    private bool tapCanceled;
    private int tapPointerId;
    private Vector2 tapStartPosition;
    private float tapStartTime;
    private float lastPinchTime = -10f;
    private System.Action closeViewer;
    private Vector2 lastViewportSize;
    public bool IsPinchInProgress => pinchInProgress;
    public bool CanTapClose => !pinchInProgress &&
        Time.unscaledTime - lastPinchTime > .3f;

    public void Initialize(RectTransform visibleArea, RectTransform imageRect,
        RawImage rawImage, Canvas rootCanvas, System.Action closeAction)
    {
        viewport = visibleArea;
        picture = imageRect;
        image = rawImage;
        canvas = rootCanvas;
        closeViewer = closeAction;
    }

    public void Show(Texture2D texture)
    {
        image.texture = texture;
        zoom = 1f;
        pinching = false;
        pinchInProgress = false;
        tapCanceled = false;
        lastPinchTime = -10f;
        picture.localScale = Vector3.one;
        picture.anchoredPosition = Vector2.zero;
        Fit();
    }

    public void ResetGesture()
    {
        pinching = false;
        pinchInProgress = false;
        tapCanceled = false;
    }

    private void LateUpdate()
    {
        if (image == null || image.texture == null) return;
        if (lastViewportSize != viewport.rect.size) Fit();
        UpdatePinch();
    }

    private void Fit()
    {
        if (image == null || image.texture == null || viewport.rect.width <= 0) return;
        var size = viewport.rect.size;
        float fit = Mathf.Min(size.x / image.texture.width, size.y / image.texture.height);
        picture.sizeDelta = new Vector2(image.texture.width * fit, image.texture.height * fit);
        lastViewportSize = size;
        ClampPosition();
    }

    public void OnScroll(PointerEventData eventData)
    {
        float factor = Mathf.Pow(1.15f, eventData.scrollDelta.y);
        ChangeZoom(factor, eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (pinching || TwoTouchesDown() ||
            eventData.button != PointerEventData.InputButton.Left) return;
        tapCanceled = true;
        picture.anchoredPosition += eventData.delta / Mathf.Max(0.01f, canvas.scaleFactor);
        ClampPosition();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!Application.isMobilePlatform) return;
        tapPointerId = eventData.pointerId;
        tapStartPosition = eventData.position;
        tapStartTime = Time.unscaledTime;
        tapCanceled = TwoTouchesDown() || !CanTapClose;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!Application.isMobilePlatform || tapCanceled || !CanTapClose ||
            eventData.pointerId != tapPointerId ||
            Time.unscaledTime - tapStartTime > .6f ||
            Vector2.Distance(eventData.position, tapStartPosition) > 24f)
            return;
        closeViewer?.Invoke();
    }

    private static bool TwoTouchesDown()
    {
#if ENABLE_INPUT_SYSTEM
        var screen = Touchscreen.current;
        if (screen == null) return false;
        int count = 0;
        for (int i = 0; i < screen.touches.Count; i++)
            if (screen.touches[i].press.isPressed && ++count >= 2) return true;
        return false;
#else
        return Input.touchCount >= 2;
#endif
    }

    private void ChangeZoom(float factor, Vector2 screenCenter)
    {
        float next = Mathf.Clamp(zoom * factor, 1f, 5f);
        float ratio = next / zoom;
        if (Mathf.Approximately(ratio, 1f)) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screenCenter,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
            out Vector2 localCenter);
        picture.anchoredPosition = localCenter +
            (picture.anchoredPosition - localCenter) * ratio;
        zoom = next;
        picture.localScale = Vector3.one * zoom;
        ClampPosition();
    }

    private void UpdatePinch()
    {
        Vector2 first = default, second = default;
        int count = 0;
#if ENABLE_INPUT_SYSTEM
        var screen = Touchscreen.current;
        if (screen != null)
            for (int i = 0; i < screen.touches.Count && count < 2; i++)
                if (screen.touches[i].press.isPressed)
                {
                    if (count++ == 0) first = screen.touches[i].position.ReadValue();
                    else second = screen.touches[i].position.ReadValue();
                }
#else
        count = Mathf.Min(2, Input.touchCount);
        if (count > 0) first = Input.GetTouch(0).position;
        if (count > 1) second = Input.GetTouch(1).position;
#endif
        if (count < 2)
        {
            pinching = false;
            if (count == 0) pinchInProgress = false;
            return;
        }
        pinchInProgress = true;
        tapCanceled = true;
        lastPinchTime = Time.unscaledTime;
        float distance = Vector2.Distance(first, second);
        Vector2 center = (first + second) * .5f;
        if (pinching && lastPinchDistance > 1f)
        {
            picture.anchoredPosition += (center - lastPinchCenter) /
                Mathf.Max(.01f, canvas.scaleFactor);
            ChangeZoom(distance / lastPinchDistance, center);
        }
        pinching = true;
        lastPinchDistance = distance;
        lastPinchCenter = center;
    }

    private void ClampPosition()
    {
        if (picture == null || viewport == null) return;
        var limit = new Vector2(
            Mathf.Max(0, (picture.sizeDelta.x * zoom - viewport.rect.width) * .5f),
            Mathf.Max(0, (picture.sizeDelta.y * zoom - viewport.rect.height) * .5f));
        var position = picture.anchoredPosition;
        picture.anchoredPosition = new Vector2(
            Mathf.Clamp(position.x, -limit.x, limit.x),
            Mathf.Clamp(position.y, -limit.y, limit.y));
    }
}

public sealed class OperationGuideBackdropTap : MonoBehaviour, IPointerDownHandler,
    IPointerClickHandler, IDragHandler
{
    private OperationGuideImageViewer viewer;
    private System.Action closeViewer;
    private Vector2 startPosition;
    private int pointerId;
    private bool dragged;

    public void Initialize(OperationGuideImageViewer imageViewer, System.Action closeAction)
    {
        viewer = imageViewer;
        closeViewer = closeAction;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pointerId = eventData.pointerId;
        startPosition = eventData.position;
        dragged = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        dragged = true;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!Application.isMobilePlatform || dragged || !viewer.CanTapClose ||
            eventData.pointerId != pointerId ||
            Vector2.Distance(eventData.position, startPosition) > 24f)
            return;
        closeViewer?.Invoke();
    }
}
