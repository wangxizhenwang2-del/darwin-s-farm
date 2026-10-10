using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

// Owns only window positioning and presentation; simulation data stays elsewhere.
public sealed class InformationWindow : MonoBehaviour, IPointerDownHandler
{
    [SerializeField, Min(0.05f)] private float animationSeconds = 0.35f;
    private RectTransform rect;
    private RectTransform canvasRect;
    private CanvasGroup group;
    private Vector2 home;
    private Coroutine animation;
    private bool open;

    public bool IsOpen => open;

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
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        rect.anchoredPosition = Offscreen();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = true;
        if (animation != null) StopCoroutine(animation);
        animation = StartCoroutine(Animate(home, 1f, false));
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        group.interactable = false;
        group.blocksRaycasts = false;
        if (animation != null) StopCoroutine(animation);
        animation = StartCoroutine(Animate(Offscreen(), 0f, true));
    }

    public void OnPointerDown(PointerEventData eventData) => transform.SetAsLastSibling();

    public void BeginTextDrag(PointerEventData eventData, out Vector2 offset)
    {
        offset = Vector2.zero;
        if (!open || animation != null || canvasRect == null) return;
        transform.SetAsLastSibling();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 local))
            offset = (Vector2)rect.localPosition - local;
    }

    public void DragText(PointerEventData eventData, Vector2 offset)
    {
        if (!open || animation != null || canvasRect == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;
        Vector2 wanted = local + offset;
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
        animation = null;
        if (hide) gameObject.SetActive(false);
    }
}

