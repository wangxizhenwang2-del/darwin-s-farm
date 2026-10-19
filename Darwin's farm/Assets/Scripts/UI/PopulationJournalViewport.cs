using UnityEngine;
using UnityEngine.UI;

// The window stays fixed while its native-ratio journal scrolls inside it.
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

    public void BeginResize(Vector2 screenPoint)
    {
        resizeStart = screenPoint;
        resizeStartScale = userScale;
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        float factor = parentCanvas != null ? parentCanvas.scaleFactor : 1f;
        resizePixelsPerScale = Mathf.Max(180f, PopulationInfoPanel.BoardWidth *
            Mathf.Max(.1f, window != null ? window.localScale.x : 1f) * factor);
    }

    public void DragResize(Vector2 screenPoint)
    {
        Vector2 delta = screenPoint - resizeStart;
        userScale = Mathf.Clamp(resizeStartScale +
            (delta.x - delta.y) * .5f / resizePixelsPerScale, .72f, 1.4f);
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
        Fit();
    }

    private void OnEnable()
    {
        window = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>()?.transform as RectTransform;
    }

    private void LateUpdate() => Fit();

    private void Fit()
    {
        if (board == null || viewport == null || scroll == null || canvas == null ||
            canvas.rect.width < 1 || canvas.rect.height < 1) return;
        float availableWidth = Mathf.Max(180f, canvas.rect.width - 24f);
        float availableHeight = Mathf.Max(180f, canvas.rect.height - 16f);
        float baseScale = Mathf.Min(1f, availableWidth / PopulationInfoPanel.BoardWidth);
        float scale = Mathf.Min(baseScale * userScale,
            availableWidth / PopulationInfoPanel.BoardWidth);
        scale = Mathf.Max(.1f, scale);
        float visibleHeight = Mathf.Min(PopulationInfoPanel.BoardHeight, availableHeight / scale);
        bool overflow = visibleHeight < PopulationInfoPanel.BoardHeight - 1f;
        if (!Mathf.Approximately(window.localScale.x, scale))
            window.localScale = Vector3.one * scale;
        if (!Mathf.Approximately(window.rect.width, PopulationInfoPanel.BoardWidth))
            window.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, PopulationInfoPanel.BoardWidth);
        if (!Mathf.Approximately(window.rect.height, visibleHeight))
            window.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, visibleHeight);
        if (board.localScale != Vector3.one) board.localScale = Vector3.one;
        viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, PopulationInfoPanel.BoardWidth);
        viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, visibleHeight);
        if (scrollbar != null)
        {
            if (scrollbar.gameObject.activeSelf != overflow)
                scrollbar.gameObject.SetActive(overflow);
            if (overflow) scrollbar.size = visibleHeight / PopulationInfoPanel.BoardHeight;
        }
        scroll.vertical = overflow;
        if (!overflow && board.anchoredPosition.y != 0f)
            board.anchoredPosition = Vector2.zero;
    }
}
