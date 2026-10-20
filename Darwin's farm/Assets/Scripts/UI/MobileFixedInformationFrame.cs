using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The mask never scales. Only the authored board inside the scroll surface does.
public sealed class MobileFixedInformationFrame : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private InformationWindow window;
    private RectTransform content;
    private ScrollRect scroll;
    private Action resetSize;
    private Action<bool> setCompact;
    private RectTransform resizeGrip;
    private Button closeButton;
    private RectTransform closeVisual, gripVisual;
    private MobileLongPressClose longPress;
    private MobilePanelPinchZoom pinch;
    private Vector2 boardSize;
    private float fullHeight, compactHeight;
    private bool compact;
    private bool compactDragging;
    private Vector2 dragStartPointer, dragStartPosition, defaultPosition;
    private const float DefaultScale = .8f;
    private const float CompactThreshold = .58f;
    private const float ExpandThreshold = .73f;
    private float userScale = DefaultScale;
    private bool wasOpen;

    public float ContentScale => Mathf.Min(1f,
        Mathf.Max(1f, ((RectTransform)transform).rect.width - 12f) / boardSize.x) * userScale;
    public bool IsCompact => compact;

    public static MobileFixedInformationFrame BindSaved(RectTransform safe, InformationWindow panel,
        string name, Action close, Action reset, Vector2 size, float smallHeight,
        Action<bool> compactLayout, MapTouchInput mapInput)
    {
        var saved = safe.Find(name) as RectTransform;
        if (saved == null) { Debug.LogError($"{name} is missing from the saved scene."); return null; }
        var root = saved.gameObject;
        root.SetActive(true);
        var frame = (RectTransform)root.transform;
        if (frame.parent != safe) frame.SetParent(safe, false);
        frame.anchorMin = frame.anchorMax = frame.pivot = Vector2.up;
        frame.sizeDelta = new Vector2(550f, 600f);
        var hit = root.GetComponent<Image>();
        if (hit == null) { Debug.LogError($"{name} has no authored Image."); return null; }
        hit.color = new Color(1f, 1f, 1f, .001f);
        if (root.GetComponent<RectMask2D>() == null) { Debug.LogError($"{name} has no authored mask."); return null; }
        var board = (RectTransform)panel.transform;
        board.SetParent(frame, false);
        board.anchorMin = board.anchorMax = board.pivot = Vector2.up;
        board.anchoredPosition = Vector2.zero;
        board.sizeDelta = size;
        var controller = root.GetComponent<ScrollRect>();
        if (controller == null) { Debug.LogError($"{name} has no authored ScrollRect."); return null; }
        controller.viewport = frame;
        controller.content = board;
        controller.horizontal = false;
        controller.vertical = true;
        controller.movementType = ScrollRect.MovementType.Clamped;
        controller.inertia = true;
        controller.decelerationRate = .22f;
        controller.scrollSensitivity = 65f;
        controller.verticalScrollbar = frame.Find("VerticalScrollbar")?.GetComponent<Scrollbar>();
        controller.horizontalScrollbar = null;
        controller.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        controller.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        panel.SetScrollContentMode(true);
        panel.SetDragBounds(frame, Vector2.zero);
        var closeControl = frame.Find("Close")?.GetComponent<Button>();
        var grip = frame.Find("MobileResizeGrip") as RectTransform;
        if (closeControl == null || grip == null || controller.verticalScrollbar == null)
        { Debug.LogError($"{name} is missing its authored close, grip, or scrollbar."); return null; }
        closeControl.onClick.RemoveAllListeners();
        closeControl.onClick.AddListener(() => close());
        float previousFactor = 1f;
        grip.GetComponent<SnapPanelResizeHandle>().Initialize(delta =>
        {
            float factor = Mathf.Clamp(1f + delta / 260f, .4f, 2f);
            root.GetComponent<MobileFixedInformationFrame>()?.ZoomBy(factor / previousFactor);
            previousFactor = factor;
        }, () => previousFactor = 1f);
        var hold = root.GetComponent<MobileLongPressClose>();
        if (hold == null) { Debug.LogError($"{name} has no authored hold component."); return null; }
        hold.Configure(frame, close);
        // Child graphics receive pointer-down before the frame background.
        // Listen on the board as well, so a hold on the artwork is captured.
        var boardHold = panel.GetComponent<MobileLongPressClose>();
        if (boardHold == null) { Debug.LogError($"{name} paper has no authored hold component."); return null; }
        boardHold.Configure(frame, close);
        var pinch = root.GetComponent<MobilePanelPinchZoom>();
        if (pinch == null) { Debug.LogError($"{name} has no authored pinch component."); return null; }
        pinch.Configure(frame, factor => root.GetComponent<MobileFixedInformationFrame>()?.ZoomBy(factor), mapInput);
        var owner = root.GetComponent<MobileFixedInformationFrame>();
        if (owner == null) { Debug.LogError($"{name} has no authored frame controller."); return null; }
        owner.window = panel;
        owner.content = board;
        owner.scroll = controller;
        owner.resetSize = reset;
        owner.setCompact = compactLayout;
        owner.resizeGrip = grip;
        owner.closeButton = closeControl;
        owner.closeVisual = closeControl.transform.Find("Visual") as RectTransform;
        owner.gripVisual = grip.Find("Visual") as RectTransform;
        owner.longPress = hold;
        owner.pinch = pinch;
        owner.boardSize = size;
        owner.fullHeight = size.y;
        owner.compactHeight = smallHeight;
        owner.compact = false;
        owner.userScale = DefaultScale;
        owner.ApplyScale();
        return owner;
    }

    public void ZoomBy(float factor)
    {
        if (!window.IsOpen || factor <= 0f) return;
        userScale = Mathf.Clamp(userScale * factor, .45f, 1.6f);
        if (!compact && userScale <= CompactThreshold) SetCompact(true);
        else if (compact && userScale >= ExpandThreshold) SetCompact(false);
        ApplyScale();
    }

    private void SetCompact(bool value)
    {
        if (compact == value || setCompact == null || compactHeight <= 0f) return;
        compact = value;
        compactDragging = false;
        var hitArea = value ? content : (RectTransform)transform;
        longPress?.SetArea(hitArea);
        content.GetComponent<MobileLongPressClose>()?.SetArea(hitArea);
        pinch?.SetArea(hitArea);
        setCompact(value);
        boardSize.y = value ? compactHeight : fullHeight;
        content.anchoredPosition = Vector2.zero;
        scroll.StopMovement();
    }

    private void ApplyScale()
    {
        if (content == null || boardSize.x <= 0f) return;
        content.sizeDelta = boardSize;
        content.localScale = Vector3.one * ContentScale;
    }

    public void Place(float x, float y, float width, float height)
    {
        var frame = (RectTransform)transform;
        defaultPosition = new Vector2(x, -y);
        if (!compact || window == null || !window.IsOpen)
            frame.anchoredPosition = defaultPosition;
        frame.sizeDelta = new Vector2(width, height);
        ApplyScale();
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    { if (compact) eventData.useDragThreshold = false; }

    public void OnBeginDrag(PointerEventData eventData) => BeginCompactDrag(eventData);
    public void OnDrag(PointerEventData eventData) => DragCompact(eventData);
    public void OnEndDrag(PointerEventData eventData) => compactDragging = false;

    public void BeginCompactDrag(PointerEventData eventData)
    {
        if (!compact || window == null || !window.IsOpen ||
            eventData.button != PointerEventData.InputButton.Left) return;
        var parent = transform.parent as RectTransform;
        if (parent == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parent, eventData.pressPosition, eventData.pressEventCamera, out dragStartPointer)) return;
        dragStartPosition = ((RectTransform)transform).anchoredPosition;
        compactDragging = true;
    }

    public void DragCompact(PointerEventData eventData)
    {
        if (!compactDragging || !compact) return;
        var parent = transform.parent as RectTransform;
        if (parent == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parent, eventData.position, eventData.pressEventCamera, out Vector2 point)) return;
        float paperWidth = Mathf.Min(((RectTransform)transform).rect.width, boardSize.x * ContentScale);
        float paperHeight = Mathf.Min(((RectTransform)transform).rect.height, boardSize.y * ContentScale);
        float maxX = Mathf.Max(8f, parent.rect.width - paperWidth - 8f);
        float maxY = Mathf.Max(8f, parent.rect.height - paperHeight - 8f);
        Vector2 wanted = dragStartPosition + point - dragStartPointer;
        ((RectTransform)transform).anchoredPosition = new Vector2(
            Mathf.Clamp(wanted.x, 8f, maxX), Mathf.Clamp(wanted.y, -maxY, -8f));
    }

    public void EndCompactDrag() => compactDragging = false;

    private void LateUpdate()
    {
        if (window == null) return;
        // GameInformationUI may create its legacy population grip in Start,
        // after this frame was attached. Never let that grip or the old X
        // reappear on the scaled paper.
        HideAuthoredControl("Close");
        HideAuthoredControl("ResizeGrip");
        HideAuthoredControl("PopulationResizeGrip");
        bool open = window.IsOpen;
        if (open && !wasOpen)
        {
            transform.SetAsLastSibling();
            userScale = DefaultScale;
            SetCompact(false);
            resetSize?.Invoke();
            boardSize.y = fullHeight;
            ((RectTransform)transform).anchoredPosition = defaultPosition;
            content.anchoredPosition = Vector2.zero;
            ApplyScale();
            scroll.StopMovement();
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1f;
            scroll.horizontalNormalizedPosition = 0f;
        }
        wasOpen = open;
        var image = GetComponent<Image>();
        image.raycastTarget = open && !compact;
        scroll.enabled = open && !compact;
        if (longPress != null) longPress.enabled = open;
        if (pinch != null) pinch.enabled = open;
        if (open) PlaceControls();
        else
        {
            if (scroll.verticalScrollbar != null) scroll.verticalScrollbar.gameObject.SetActive(false);
            if (scroll.horizontalScrollbar != null) scroll.horizontalScrollbar.gameObject.SetActive(false);
        }
        if (resizeGrip != null) resizeGrip.gameObject.SetActive(open);
        if (closeButton != null) closeButton.gameObject.SetActive(open);
        if (open)
        {
            if (closeButton != null) closeButton.transform.SetAsLastSibling();
            if (resizeGrip != null) resizeGrip.SetAsLastSibling();
        }
    }

    private void HideAuthoredControl(string name)
    {
        var child = content != null ? content.Find(name) : null;
        if (child != null && child.gameObject.activeSelf) child.gameObject.SetActive(false);
    }

    // The paper moves and scales inside a fixed viewport. Keep the controls at
    // the currently visible paper edges, in unscaled viewport coordinates.
    private void PlaceControls()
    {
        var frame = (RectTransform)transform;
        if (content == null || frame.rect.width < 1f || frame.rect.height < 1f) return;
        var corners = new Vector3[4];
        content.GetWorldCorners(corners);
        Vector3 lower = frame.InverseTransformPoint(corners[0]);
        Vector3 upper = frame.InverseTransformPoint(corners[2]);
        float width = frame.rect.width, height = frame.rect.height;
        float left = Mathf.Clamp(lower.x, 0f, width);
        float right = Mathf.Clamp(upper.x, 0f, width);
        float bottom = Mathf.Clamp(lower.y, -height, 0f);
        float top = Mathf.Clamp(upper.y, -height, 0f);
        if (right - left < 40f || top - bottom < 40f) return;

        if (closeButton != null)
        {
            var rect = (RectTransform)closeButton.transform;
            rect.anchorMin = rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(right - 5f, top - 5f);
            rect.localScale = Vector3.one;
        }
        if (resizeGrip != null)
        {
            resizeGrip.anchorMin = resizeGrip.anchorMax = Vector2.up;
            resizeGrip.pivot = new Vector2(1f, 0f);
            resizeGrip.anchoredPosition = new Vector2(right - 5f, bottom + 5f);
            resizeGrip.localScale = Vector3.one;
        }

        bool verticalOverflow = upper.y > 1f || lower.y < -height - 1f;
        // A paper taller than the viewport needs its bar even at the top.
        verticalOverflow |= upper.y - lower.y > height + 1f;
        if (scroll.verticalScrollbar != null)
        {
            var bar = (RectTransform)scroll.verticalScrollbar.transform;
            bar.anchorMin = bar.anchorMax = Vector2.up;
            bar.pivot = Vector2.up;
            bar.anchoredPosition = new Vector2(right - 32f, top - 55f);
            bar.sizeDelta = new Vector2(32f, Mathf.Max(24f, top - bottom - 110f));
            bar.localScale = Vector3.one;
            bar.gameObject.SetActive(verticalOverflow && !compact);
        }
        if (closeVisual != null) closeVisual.localScale = Vector3.one * ContentScale;
        if (gripVisual != null) gripVisual.localScale = Vector3.one * ContentScale;
    }

#if UNITY_EDITOR
    private static Button MakeClose(RectTransform frame, Action close)
    {
        var obj = new GameObject("Close", typeof(RectTransform), typeof(Canvas),
            typeof(GraphicRaycaster), typeof(Image), typeof(Button));
        var rect = (RectTransform)obj.transform;
        rect.SetParent(frame, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-16f, -16f);
        rect.sizeDelta = new Vector2(64f, 64f);
        var image = obj.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, .001f);
        var canvas = obj.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;
        var button = obj.GetComponent<Button>();
        button.onClick.AddListener(() => close());
        var visualObject = new GameObject("Visual", typeof(RectTransform), typeof(Image));
        var visual = (RectTransform)visualObject.transform;
        visual.SetParent(rect, false);
        visual.anchorMin = visual.anchorMax = visual.pivot = Vector2.one;
        visual.anchoredPosition = Vector2.zero;
        visual.sizeDelta = new Vector2(48f, 48f);
        var visualImage = visualObject.GetComponent<Image>();
        visualImage.color = new Color(.91f, .92f, .78f, .95f);
        visualImage.raycastTarget = false;
        var label = new GameObject("Mark", typeof(RectTransform), typeof(TextMeshProUGUI));
        var mark = (RectTransform)label.transform;
        mark.SetParent(visual, false);
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
        return button;
    }

    // Called only by the WhiteBox scene upgrader. The saved hierarchy is then
    // bound at runtime without creating visual objects.
    public static void InstallAuthoredParts(RectTransform frame, InformationWindow paper)
    {
        if (frame.GetComponent<Image>() == null) frame.gameObject.AddComponent<Image>();
        var hit = frame.GetComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, .001f);
        if (frame.GetComponent<RectMask2D>() == null) frame.gameObject.AddComponent<RectMask2D>();
        var scroll = frame.GetComponent<ScrollRect>();
        if (scroll == null) scroll = frame.gameObject.AddComponent<ScrollRect>();
        // The artwork is attached only for mobile Play; an enabled ScrollRect
        // with no content logs an editor error while authoring WhiteBox.
        scroll.enabled = false;
        scroll.viewport = frame;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = .22f;
        scroll.scrollSensitivity = 65f;
        scroll.horizontalScrollbar = null;
        var bar = frame.Find("VerticalScrollbar")?.GetComponent<Scrollbar>();
        if (bar == null) bar = MakeScrollbar(frame, "VerticalScrollbar", true);
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        if (frame.Find("Close") == null) MakeClose(frame, () => { });
        if (frame.Find("MobileResizeGrip") == null) MakeGrip(frame, _ => { });
        if (frame.GetComponent<MobileLongPressClose>() == null) frame.gameObject.AddComponent<MobileLongPressClose>();
        if (frame.GetComponent<MobilePanelPinchZoom>() == null) frame.gameObject.AddComponent<MobilePanelPinchZoom>();
        if (frame.GetComponent<MobileFixedInformationFrame>() == null) frame.gameObject.AddComponent<MobileFixedInformationFrame>();
        if (paper.GetComponent<MobileLongPressClose>() == null) paper.gameObject.AddComponent<MobileLongPressClose>();
    }

    private static RectTransform MakeGrip(RectTransform frame, Action<float> zoom)
    {
        var obj = new GameObject("MobileResizeGrip", typeof(RectTransform),
            typeof(Image), typeof(SnapPanelResizeHandle));
        var rect = (RectTransform)obj.transform;
        rect.SetParent(frame, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-20f, 20f);
        rect.sizeDelta = new Vector2(64f, 64f);
        var image = obj.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, .001f);
        image.raycastTarget = true;
        var visualObject = new GameObject("Visual", typeof(RectTransform), typeof(Image));
        var visual = (RectTransform)visualObject.transform;
        visual.SetParent(rect, false);
        visual.anchorMin = visual.anchorMax = visual.pivot = new Vector2(1f, 0f);
        visual.anchoredPosition = Vector2.zero;
        visual.sizeDelta = new Vector2(44f, 44f);
        var visualImage = visualObject.GetComponent<Image>();
        visualImage.sprite = Resources.Load<Sprite>("GameUI/SpeciesJournal/ResizeGrip");
        visualImage.color = visualImage.sprite != null ? Color.white : new Color(.3f, .48f, .26f);
        visualImage.preserveAspect = true;
        visualImage.raycastTarget = false;
        float previousFactor = 1f;
        obj.GetComponent<SnapPanelResizeHandle>().Initialize(delta =>
        {
            float factor = Mathf.Clamp(1f + delta / 260f, .4f, 2f);
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
        root.GetComponent<Image>().color = new Color(1f, 1f, 1f, .001f);
        var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        var handle = (RectTransform)handleObject.transform;
        handle.SetParent(rect, false);
        handle.anchorMin = new Vector2(.65f, 0f);
        handle.anchorMax = new Vector2(.95f, 1f);
        handle.offsetMin = handle.offsetMax = Vector2.zero;
        var image = handleObject.GetComponent<Image>();
        image.color = new Color(.3f, .48f, .26f, .7f);
        image.raycastTarget = false;
        var scrollbar = root.GetComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = image;
        scrollbar.direction = vertical ? Scrollbar.Direction.BottomToTop : Scrollbar.Direction.LeftToRight;
        return scrollbar;
    }
#endif
}
