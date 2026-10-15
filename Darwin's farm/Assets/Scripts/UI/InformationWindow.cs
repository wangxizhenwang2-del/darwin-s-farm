using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

// Owns only window positioning and presentation; simulation data stays elsewhere.
public sealed class InformationWindow : MonoBehaviour, IPointerDownHandler
{
    [SerializeField, Min(0.05f)] private float animationSeconds = 0.35f;
    [SerializeField] private bool titleOnlyDragging;
    private RectTransform rect;
    private RectTransform canvasRect;
    private CanvasGroup group;
    private Vector2 home;
    private Coroutine windowAnimation;
    private bool open;
    private bool presentationVisible = true;

    public bool IsOpen => open;
    public bool TitleOnlyDragging => titleOnlyDragging;
    public void SetDragBounds(RectTransform bounds, Vector2 defaultPosition)
    { canvasRect = bounds; home = defaultPosition; if (open) ClampToBounds(); }
    public void HideImmediately()
    {
        if (windowAnimation != null) StopCoroutine(windowAnimation);
        windowAnimation = null; open = false;
        if (group != null) { group.alpha = 0f; group.interactable = group.blocksRaycasts = false; }
        gameObject.SetActive(false);
    }

    // An occupied desktop window can be represented by the mobile drawer.
    // Hiding its presentation must not release or replace its selected target.
    public void SetPresentationVisible(bool visible)
    {
        if (presentationVisible == visible) return;
        presentationVisible = visible;
        if (windowAnimation != null) StopCoroutine(windowAnimation);
        windowAnimation = null;
        if (group == null) return;
        group.alpha = open && visible ? 1f : 0f;
        group.interactable = open && visible;
        group.blocksRaycasts = open && visible;
        gameObject.SetActive(open && visible);
        if (open && visible) rect.anchoredPosition = home;
    }

    public void Initialize(RectTransform canvas, Vector2 defaultPosition)
    {
        rect = (RectTransform)transform;
        canvasRect = canvas;
        home = defaultPosition;
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        gameObject.SetActive(false);
    }

    public void Open()
    {
        if (open) return;
        open = true;
        if (!presentationVisible) return;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        rect.anchoredPosition = Offscreen();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = true;
        if (windowAnimation != null) StopCoroutine(windowAnimation);
        windowAnimation = StartCoroutine(Animate(home, 1f, false));
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        if (!presentationVisible) return;
        group.interactable = false;
        group.blocksRaycasts = false;
        if (windowAnimation != null) StopCoroutine(windowAnimation);
        windowAnimation = StartCoroutine(Animate(Offscreen(), 0f, true));
    }

    public void OnPointerDown(PointerEventData eventData) => transform.SetAsLastSibling();

    public bool BeginTextDrag(PointerEventData eventData, out Vector2 offset)
    {
        offset = Vector2.zero;
        if (!open || windowAnimation != null || canvasRect == null) return false;
        transform.SetAsLastSibling();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 local))
        { offset = (Vector2)rect.localPosition - local; return true; }
        return false;
    }

    public void DragText(PointerEventData eventData, Vector2 offset)
    {
        if (!open || windowAnimation != null || canvasRect == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;
        Vector2 wanted = local + offset;
        SetClampedPosition(wanted);
    }
    private void ClampToBounds() => SetClampedPosition(rect.localPosition);
    private void SetClampedPosition(Vector2 wanted)
    {
        Rect bounds = canvasRect.rect;
        Rect window = rect.rect;
        float xMin = bounds.xMin - window.xMin + 8f;
        float xMax = bounds.xMax - window.xMax - 8f;
        float yMin = bounds.yMin - window.yMin + 8f;
        float yMax = bounds.yMax - window.yMax - 8f;
        rect.localPosition = new Vector3(
            xMin <= xMax ? Mathf.Clamp(wanted.x, xMin, xMax) : (xMin + xMax) * 0.5f,
            yMin <= yMax ? Mathf.Clamp(wanted.y, yMin, yMax) : (yMin + yMax) * 0.5f,
            rect.localPosition.z);
    }

    private Vector2 Offscreen() => new Vector2(home.x, -canvasRect.rect.height - rect.rect.height);

    private IEnumerator Animate(Vector2 target, float targetAlpha, bool hide)
    {
        Vector2 start = rect.anchoredPosition;
        float alpha = group.alpha;
        float elapsed = 0f;
        while (elapsed < animationSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / animationSeconds);
            t = t * t * (3f - 2f * t);
            rect.anchoredPosition = Vector2.LerpUnclamped(start, target, t);
            group.alpha = Mathf.Lerp(alpha, targetAlpha, t);
            yield return null;
        }
        rect.anchoredPosition = target;
        group.alpha = targetAlpha;
        group.interactable = !hide;
        windowAnimation = null;
        if (hide) gameObject.SetActive(false);
    }
}

