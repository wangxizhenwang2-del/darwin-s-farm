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
    private void Start()
    {
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
    private void LateUpdate()
    {
        if (desktopScroll != null)
        {
            var canvas = GetComponentInParent<Canvas>();
            var root = (RectTransform)transform;
            if (canvas != null)
            {
                if (zoom == null) zoom = GetComponent<EnvironmentToolPanelZoom>();
                float canvasScale = Mathf.Max(.01f, canvas.scaleFactor);
                float availableWidth = ((RectTransform)canvas.transform).rect.width -
                    root.anchoredPosition.x - 20f;
                float maximum = (availableWidth - 102f) / EnvironmentToolPanelLayout.WideWidth;
                if (zoom != null) zoom.SetBaseScale(Mathf.Min(1f / canvasScale,
                    Mathf.Max(.1f, maximum)));
                float effective = zoom != null ? zoom.EffectiveScale : 1f;
                float contentWidth = session.Active ?
                    Mathf.Max(EnvironmentToolPanelLayout.RailWidth,
                        102f + EnvironmentToolPanelLayout.WideWidth * effective) :
                    EnvironmentToolPanelLayout.RailWidth;
                float contentHeight = session.Active ?
                    Mathf.Max(363f, EnvironmentToolPanelLayout.WideHeight * effective) : 363f;
                if (desktopScroll.content != null)
                    desktopScroll.content.sizeDelta = new Vector2(contentWidth, contentHeight);
                root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    Mathf.Clamp(availableWidth, 80f, contentWidth));
                float availableHeight = ((RectTransform)canvas.transform).rect.height +
                    root.anchoredPosition.y - 20f;
                float height = Mathf.Clamp(availableHeight, 60f, contentHeight);
                root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }
        }
        bool visible = mobile == null || !mobile.IsMobileActive;
        if (desktopScroll != null)
        {
            desktopScroll.enabled = visible;
            var hitArea = desktopScroll.viewport != null ? desktopScroll.viewport.GetComponent<UnityEngine.UI.Image>() : null;
            if (hitArea != null) hitArea.raycastTarget = false;
        }
        toolbar.SetActive(visible && TutorialFlowController.Allows(TutorialFlowController.Feature.EnvironmentPreview));
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
