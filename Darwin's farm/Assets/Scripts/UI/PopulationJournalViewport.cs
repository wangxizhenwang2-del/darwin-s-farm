using UnityEngine;
using UnityEngine.UI;

// The authored journal and its controls scale together as one composition.
[ExecuteAlways, DisallowMultipleComponent]
public sealed class PopulationJournalViewport : MonoBehaviour
{
    [SerializeField] private RectTransform board, viewport;
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private Scrollbar scrollbar;
    [SerializeField, Range(.72f, 1.4f)] private float userScale = 1f;
    private RectTransform window;
    private RectTransform canvas;
    private Vector2 resizeStart;
    private float resizeStartScale;
    private float resizePixelsPerScale;
    private float resizeSnapPixels;
    private bool snappedDuringDrag;
    private PopulationInfoPanel panel;
    private RectTransform closeButton;
    private Vector2 fullClosePosition;
    private bool compact;

    public void BeginResize(Vector2 screenPoint)
    {
        snappedDuringDrag = false;
        resizeStart = screenPoint;
        resizeStartScale = userScale;
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        float factor = parentCanvas != null ? parentCanvas.scaleFactor : 1f;
        resizeSnapPixels = Mathf.Max(30f, 76f * factor);
        resizePixelsPerScale = Mathf.Max(180f, PopulationInfoPanel.BoardWidth *
            Mathf.Max(.1f, window != null ? window.localScale.x : 1f) * factor);
    }

    public void DragResize(Vector2 screenPoint)
    {
        if (snappedDuringDrag) return;
        Vector2 delta = screenPoint - resizeStart;
        float signed = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? delta.x : -delta.y;
        if (!compact && signed < -resizeSnapPixels) { SetCompact(true); snappedDuringDrag = true; return; }
        if (compact)
        {
            if (signed > resizeSnapPixels) { SetCompact(false); snappedDuringDrag = true; return; }
            userScale = Mathf.Clamp(resizeStartScale + signed / resizePixelsPerScale, .65f, 1.4f);
            Fit();
            return;
        }
        userScale = Mathf.Clamp(resizeStartScale +
            signed / resizePixelsPerScale, .72f, 1.4f);
        Fit();
    }

    public void ResetToFull() { userScale = 1f; SetCompact(false); }

    private void SetCompact(bool value)
    {
        if (panel == null && board != null) panel = board.GetComponent<PopulationInfoPanel>();
        if (panel == null) return;
        if (closeButton == null)
            foreach (Button button in GetComponentsInChildren<Button>(true))
                if (button.transform.parent == transform)
                {
                    closeButton = (RectTransform)button.transform;
                    fullClosePosition = closeButton.anchoredPosition;
                    break;
                }
        if (compact != value) userScale = 1f;
        compact = value;
        panel.SetCompact(value);
        if (closeButton != null)
            closeButton.anchoredPosition = value ? new Vector2(-20f, -18f) : fullClosePosition;
        Fit();
    }

    public void Initialize(RectTransform journal, RectTransform visible, ScrollRect controller, Scrollbar bar)
    {
        board = journal;
        viewport = visible;
        scroll = controller;
        scrollbar = bar;
        window = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>()?.transform as RectTransform;
        panel = board != null ? board.GetComponent<PopulationInfoPanel>() : null;
        if (Application.isPlaying) PlaceResizeGrip();
        foreach (Button button in GetComponentsInChildren<Button>(true))
            if (button.transform.parent == transform)
            {
                closeButton = (RectTransform)button.transform;
                fullClosePosition = closeButton.anchoredPosition;
                break;
            }
        if (Application.isPlaying) Fit();
    }

    private void OnEnable()
    {
        window = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>()?.transform as RectTransform;
        if (Application.isPlaying) PlaceResizeGrip();
    }

    private void PlaceResizeGrip()
    {
        var grip = transform.Find("ResizeGrip") as RectTransform;
        if (grip == null) return;
        grip.anchorMin = grip.anchorMax = grip.pivot = new Vector2(1f, 0f);
        grip.anchoredPosition = new Vector2(19f, -19f);
        grip.sizeDelta = new Vector2(46f, 46f);
        grip.SetAsLastSibling();
    }

    private void LateUpdate() { if (Application.isPlaying) Fit(); }

    private void Fit()
    {
        if (board == null || viewport == null || scroll == null || canvas == null ||
            canvas.rect.width < 1 || canvas.rect.height < 1) return;
        float availableWidth = Mathf.Max(180f, canvas.rect.width - 24f);
        float availableHeight = Mathf.Max(180f, canvas.rect.height - 16f);
        float boardHeight = compact ? PopulationInfoPanel.CompactHeight : PopulationInfoPanel.BoardHeight;
        float baseScale = Mathf.Min(1f, availableWidth / PopulationInfoPanel.BoardWidth,
            availableHeight / boardHeight);
        float scale = Mathf.Clamp(baseScale * userScale, .1f,
            Mathf.Min(availableWidth / PopulationInfoPanel.BoardWidth, availableHeight / boardHeight));
        if (!Mathf.Approximately(window.localScale.x, scale))
            window.localScale = Vector3.one * scale;
        if (!Mathf.Approximately(window.rect.width, PopulationInfoPanel.BoardWidth))
            window.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, PopulationInfoPanel.BoardWidth);
        if (!Mathf.Approximately(window.rect.height, boardHeight))
            window.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, boardHeight);
        if (board.localScale != Vector3.one) board.localScale = Vector3.one;
        viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, PopulationInfoPanel.BoardWidth);
        viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, boardHeight);
        if (scrollbar != null && scrollbar.gameObject.activeSelf)
            scrollbar.gameObject.SetActive(false);
        scroll.vertical = false;
        if (board.anchoredPosition.y != 0f)
            board.anchoredPosition = Vector2.zero;
    }
}
