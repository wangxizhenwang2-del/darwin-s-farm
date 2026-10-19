using UnityEngine;
using UnityEngine.EventSystems;

// Desktop previews on hover. Touch previews after a hold; releasing a hold does not cancel.
public sealed class EnvironmentToolCancelPreviewTrigger : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private EnvironmentToolSession session;
    [SerializeField] private bool touchPresentation;
    [SerializeField] private float holdSeconds = .5f;
    private bool pressed, held, hovering;
    private float pressedAt;

    public void Configure(EnvironmentToolSession owner, bool touch)
    { session = owner; touchPresentation = touch; }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
        if (!touchPresentation) session?.SetCancelPreview(true);
    }
    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        if (!touchPresentation) session?.SetCancelPreview(false);
    }
    public void OnPointerDown(PointerEventData eventData)
    {
        if (!touchPresentation) return;
        session?.BeginCancelPress();
        pressed = true; held = false; pressedAt = Time.unscaledTime;
    }
    private void Update()
    {
        if (!touchPresentation)
        {
            if (hovering) session?.SetCancelPreview(true);
            return;
        }
        if (!pressed || held || Time.unscaledTime - pressedAt < holdSeconds) return;
        held = true;
        session?.SetCancelPreview(true);
    }
    public void OnPointerUp(PointerEventData eventData)
    {
        if (!touchPresentation) return;
        if (held) session?.SuppressCancelClick();
        session?.SetCancelPreview(false);
        pressed = held = false;
    }
    private void OnDisable()
    {
        session?.SetCancelPreview(false);
        pressed = held = hovering = false;
    }
}
