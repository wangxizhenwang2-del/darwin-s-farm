using TMPro;
using UnityEngine;

public sealed class DesktopEnvironmentTools : MonoBehaviour
{
    [SerializeField] private EnvironmentToolSession session;
    [SerializeField] private MobileGameUI mobile;
    [SerializeField] private GameObject toolbar, body;
    [SerializeField] private UnityEngine.UI.Button[] kinds, options;
    [SerializeField] private UnityEngine.UI.Button apply, cancel, exit;
    [SerializeField] private TextMeshProUGUI preview, current, notice;
    [SerializeField] private EnvironmentToolPanelView redesigned;
    [SerializeField] private UnityEngine.UI.ScrollRect desktopScroll;
    [SerializeField] private EnvironmentToolPanelZoom zoom;
    private bool wasActive;
    private void Start()
    {
        SeparateFixedToolbar();
        session.RegisterPreviewControls(options, apply);
        preview.richText = true;
        for (int i = 0; i < kinds.Length; i++)
        {
            int index = i;
            kinds[i].onClick.AddListener(() => session.SelectKind(index));
            kinds[i].GetComponentInChildren<TextMeshProUGUI>(true).text =
                DarwinFarm.Environment.EnvironmentTechnologyCatalog.ShortName(
                    DarwinFarm.Environment.EnvironmentTechnologyCatalog.All[i]);
        }
        for (int i = 0; i < options.Length; i++)
        { int index = i; options[i].onClick.AddListener(() => session.SelectOption(index)); }
        apply.onClick.AddListener(session.Deploy);
        cancel.onClick.AddListener(session.CancelEffect);
        exit.onClick.AddListener(session.End);
    }

    private void SeparateFixedToolbar()
    {
        if (toolbar == null || body == null || desktopScroll == null) return;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var root = (RectTransform)transform;
        var rail = (RectTransform)toolbar.transform;
        // The rail belongs to the Canvas, not the detail window's scroll/drag
        // hierarchy. Moving or resizing the detail therefore cannot affect it.
        rail.SetParent(canvas.transform, false);
        rail.anchorMin = rail.anchorMax = rail.pivot = Vector2.up;
        rail.anchoredPosition = new Vector2(20f, -118f);
        rail.sizeDelta = new Vector2(EnvironmentToolPanelLayout.RailWidth, 363f);
        rail.localScale = Vector3.one;
        root.anchoredPosition = new Vector2(122f, -118f);
        var paper = (RectTransform)body.transform;
        paper.anchoredPosition = Vector2.zero;
        EnvironmentToolPanelLayout.Apply(paper, false);
        zoom?.Configure(paper);
        desktopScroll.StopMovement();
        desktopScroll.enabled = false;
        if (desktopScroll.viewport != null)
        {
            var mask = desktopScroll.viewport.GetComponent<UnityEngine.UI.RectMask2D>();
            if (mask != null) mask.enabled = false;
        }
        if (desktopScroll.verticalScrollbar != null)
            desktopScroll.verticalScrollbar.gameObject.SetActive(false);
        if (desktopScroll.horizontalScrollbar != null)
            desktopScroll.horizontalScrollbar.gameObject.SetActive(false);
    }
    private void LateUpdate()
    {
        if (session.Active && !wasActive)
        {
            if (zoom == null) zoom = GetComponent<EnvironmentToolPanelZoom>();
            zoom?.ResetFull();
        }
        wasActive = session.Active;
        if (desktopScroll != null)
        {
            var canvas = GetComponentInParent<Canvas>();
            var root = (RectTransform)transform;
            if (canvas != null)
            {
                if (zoom == null) zoom = GetComponent<EnvironmentToolPanelZoom>();
                float canvasScale = Mathf.Max(.01f, canvas.scaleFactor);
                float availableWidth = ((RectTransform)canvas.transform).rect.width -
                    root.anchoredPosition.x - 36f;
                float availableHeight = ((RectTransform)canvas.transform).rect.height +
                    root.anchoredPosition.y - 36f;
                float authoredWidth = EnvironmentToolPanelLayout.WideWidth;
                float authoredHeight = EnvironmentToolPanelLayout.WideHeight;
                if (zoom != null) zoom.SetBaseScale(Mathf.Max(.1f, Mathf.Min(
                    1f / canvasScale, availableWidth / authoredWidth,
                    availableHeight / authoredHeight)));
                float effective = zoom != null ? zoom.EffectiveScale : 1f;
                float contentWidth = session.Active ? authoredWidth * effective : 0f;
                float contentHeight = session.Active ? authoredHeight * effective : 0f;
                if (desktopScroll.content != null)
                    desktopScroll.content.sizeDelta = new Vector2(contentWidth, contentHeight);
                root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    Mathf.Clamp(availableWidth, 80f, contentWidth));
                float height = Mathf.Clamp(availableHeight, 60f, contentHeight);
                root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }
        }
        bool visible = mobile == null || !mobile.IsMobileActive;
        if (desktopScroll != null)
        {
            desktopScroll.enabled = false;
            var hitArea = desktopScroll.viewport != null ? desktopScroll.viewport.GetComponent<UnityEngine.UI.Image>() : null;
            if (hitArea != null) hitArea.raycastTarget = false;
        }
        toolbar.SetActive(visible &&
            TutorialFlowController.Allows(TutorialFlowController.Feature.EnvironmentPreview));
        body.SetActive(visible && session.Active);
        if (!visible) return;
        for (int i = 0; i < kinds.Length && i < DarwinFarm.Environment.EnvironmentTechnologyCatalog.All.Length; i++)
            kinds[i].gameObject.SetActive(TutorialFlowController.AllowsTool(
                DarwinFarm.Environment.EnvironmentTechnologyCatalog.All[i]));
        for (int i = 0; i < kinds.Length; i++)
        {
            kinds[i].targetGraphic.color = Color.white;
            var border = kinds[i].GetComponent<UnityEngine.UI.Outline>();
            if (border != null) border.enabled = session.Active &&
                i < DarwinFarm.Environment.EnvironmentTechnologyCatalog.All.Length &&
                DarwinFarm.Environment.EnvironmentTechnologyCatalog.All[i] == session.Choice.Kind;
        }
        if (!session.Active) return;
        for (int i = 0; i < options.Length; i++)
        {
            EnvironmentToolArtwork.RefreshOption(options[i], session, i);
        }
        var title = body.transform.Find("Title")?.GetComponent<TextMeshProUGUI>();
        if (title != null) title.text = DarwinFarm.Environment.EnvironmentTechnologyCatalog.Name(session.Choice.Kind);
        preview.text = session.PreviewText;
        current.text = session.CurrentApplied;
        notice.text = session.Notice;
        apply.interactable = session.CanDeploy;
        cancel.interactable = session.CanCancel;
        if (redesigned != null) redesigned.Refresh(session);
    }
}
