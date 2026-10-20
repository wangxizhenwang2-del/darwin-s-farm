using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The mask never scales. Only the authored board inside the scroll surface does.
public sealed class MobileFixedInformationFrame : MonoBehaviour
{
    private InformationWindow window;
    private RectTransform content;
    private ScrollRect scroll;
    private Action resetSize;
    private RectTransform resizeGrip;
    private bool wasOpen;

    public static MobileFixedInformationFrame Create(RectTransform safe, InformationWindow panel,
        string name, Action close, Action reset, Action<float> zoom, MapTouchInput mapInput)
    {
        var saved = safe.Find(name) as RectTransform;
        var root = saved != null ? saved.gameObject : new GameObject(name, typeof(RectTransform));
        var frame = (RectTransform)root.transform;
        if (frame.parent != safe) frame.SetParent(safe, false);
        frame.anchorMin = frame.anchorMax = frame.pivot = Vector2.up;
        frame.sizeDelta = new Vector2(550f, 600f);
        var hit = root.GetComponent<Image>();
        if (hit == null) hit = root.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, .001f);
        if (root.GetComponent<RectMask2D>() == null) root.AddComponent<RectMask2D>();
        var board = (RectTransform)panel.transform;
        board.SetParent(frame, false);
        board.anchorMin = board.anchorMax = board.pivot = Vector2.up;
        board.anchoredPosition = Vector2.zero;
        var controller = root.GetComponent<ScrollRect>();
        if (controller == null) controller = root.AddComponent<ScrollRect>();
        controller.viewport = frame;
        controller.content = board;
        controller.horizontal = controller.vertical = true;
        controller.movementType = ScrollRect.MovementType.Clamped;
        controller.scrollSensitivity = 35f;
        controller.verticalScrollbar = MakeScrollbar(frame, "VerticalScrollbar", true);
        controller.horizontalScrollbar = MakeScrollbar(frame, "HorizontalScrollbar", false);
        controller.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        controller.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        panel.SetScrollContentMode(true);
        panel.SetDragBounds(frame, Vector2.zero);
        MakeClose(frame, close);
        var grip = MakeGrip(frame, zoom);
        var hold = root.GetComponent<MobileLongPressClose>();
        if (hold == null) hold = root.AddComponent<MobileLongPressClose>();
        hold.Configure(frame, close);
        var pinch = root.GetComponent<MobilePanelPinchZoom>();
        if (pinch == null) pinch = root.AddComponent<MobilePanelPinchZoom>();
        pinch.Configure(frame, zoom, mapInput);
        var owner = root.GetComponent<MobileFixedInformationFrame>();
        if (owner == null) owner = root.AddComponent<MobileFixedInformationFrame>();
        owner.window = panel;
        owner.content = board;
        owner.scroll = controller;
        owner.resetSize = reset;
        owner.resizeGrip = grip;
        return owner;
    }

    public void Place(float x, float y, float width, float height)
    {
        var frame = (RectTransform)transform;
        frame.anchoredPosition = new Vector2(x, -y);
        frame.sizeDelta = new Vector2(width, height);
    }

    private void LateUpdate()
    {
        if (window == null) return;
        bool open = window.IsOpen;
        if (open && !wasOpen)
        {
            resetSize?.Invoke();
            content.anchoredPosition = Vector2.zero;
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1f;
            scroll.horizontalNormalizedPosition = 0f;
        }
        wasOpen = open;
        var image = GetComponent<Image>();
        image.raycastTarget = open;
        scroll.enabled = open;
        if (scroll.verticalScrollbar != null) scroll.verticalScrollbar.gameObject.SetActive(open);
        if (scroll.horizontalScrollbar != null) scroll.horizontalScrollbar.gameObject.SetActive(open);
        if (resizeGrip != null) resizeGrip.gameObject.SetActive(open);
        foreach (var button in GetComponentsInChildren<Button>(true))
            if (button.transform.parent == transform) button.gameObject.SetActive(open);
    }

    private static void MakeClose(RectTransform frame, Action close)
    {
        var obj = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
        var rect = (RectTransform)obj.transform;
        rect.SetParent(frame, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-16f, -16f);
        rect.sizeDelta = new Vector2(48f, 48f);
        var image = obj.GetComponent<Image>();
        image.color = new Color(.91f, .92f, .78f, .95f);
        obj.GetComponent<Button>().onClick.AddListener(() => close());
        var label = new GameObject("Mark", typeof(RectTransform), typeof(TextMeshProUGUI));
        var mark = (RectTransform)label.transform;
        mark.SetParent(rect, false);
        mark.anchorMin = Vector2.zero;
        mark.anchorMax = Vector2.one;
        mark.offsetMin = mark.offsetMax = Vector2.zero;
        var text = label.GetComponent<TextMeshProUGUI>();
        text.font = GameUITheme.HandwrittenTMPFont;
        text.text = "X";
        text.fontSize = 38f;
        text.color = new Color(.12f, .25f, .08f);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        rect.SetAsLastSibling();
    }

    private static RectTransform MakeGrip(RectTransform frame, Action<float> zoom)
    {
        var obj = new GameObject("MobileResizeGrip", typeof(RectTransform),
            typeof(Image), typeof(SnapPanelResizeHandle));
        var rect = (RectTransform)obj.transform;
        rect.SetParent(frame, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-20f, 20f);
        rect.sizeDelta = new Vector2(44f, 44f);
        var image = obj.GetComponent<Image>();
        image.sprite = Resources.Load<Sprite>("GameUI/SpeciesJournal/ResizeGrip");
        image.color = image.sprite != null ? Color.white : new Color(.3f, .48f, .26f);
        image.preserveAspect = true;
        image.raycastTarget = true;
        float previousFactor = 1f;
        obj.GetComponent<SnapPanelResizeHandle>().Initialize(delta =>
        {
            float factor = Mathf.Clamp(1f + delta / 500f, .4f, 2f);
            zoom(factor / previousFactor);
            previousFactor = factor;
        }, () => previousFactor = 1f);
        rect.SetAsLastSibling();
        return rect;
    }

    private static Scrollbar MakeScrollbar(RectTransform frame, string name, bool vertical)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        var rect = (RectTransform)root.transform;
        rect.SetParent(frame, false);
        if (vertical)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = Vector2.one;
            rect.offsetMin = new Vector2(-15f, 14f);
            rect.offsetMax = new Vector2(-4f, -68f);
        }
        else
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = Vector2.zero;
            rect.offsetMin = new Vector2(12f, 4f);
            rect.offsetMax = new Vector2(-20f, 15f);
        }
        root.GetComponent<Image>().color = new Color(.36f, .5f, .28f, .18f);
        var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        var handle = (RectTransform)handleObject.transform;
        handle.SetParent(rect, false);
        handle.anchorMin = Vector2.zero;
        handle.anchorMax = Vector2.one;
        handle.offsetMin = handle.offsetMax = Vector2.zero;
        var image = handleObject.GetComponent<Image>();
        image.color = new Color(.3f, .48f, .26f, .7f);
        var scrollbar = root.GetComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = image;
        scrollbar.direction = vertical ? Scrollbar.Direction.BottomToTop : Scrollbar.Direction.LeftToRight;
        return scrollbar;
    }
}
