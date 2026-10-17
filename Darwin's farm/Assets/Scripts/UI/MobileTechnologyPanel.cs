using TMPro;
using UnityEngine;

// Saved mobile view of the shared desktop/touch tool session.
public sealed class MobileTechnologyPanel : MonoBehaviour
{
    [SerializeField] private EnvironmentTechnologyController controller;
    [SerializeField] private RectTransform tileContent;
    [SerializeField] private GameObject body;
    [SerializeField] private UnityEngine.UI.Button foldButton, deployButton, cancelButton;
    [SerializeField] private UnityEngine.UI.Button[] kindButtons, optionButtons;
    [SerializeField] private TextMeshProUGUI foldLabel, currentApplied, previewText, notice;
    [SerializeField] private bool expanded;
    [SerializeField, Min(1f)] private float foldedContentHeight = 690f, expandedContentHeight = 1090f;
    [SerializeField] private Color selectedColor = new Color(.58f, .83f, .64f);
    [SerializeField] private Color normalColor = Color.white;
    private EnvironmentToolSession session;
    private bool bound;
    public bool Expanded => expanded;
    public void Initialize()
    {
        if (bound) return;
        session = FindFirstObjectByType<EnvironmentToolSession>();
        if (session == null) return;
        bound = true;
        foldButton.onClick.AddListener(ToggleExpanded);
        deployButton.onClick.AddListener(session.Deploy);
        cancelButton.onClick.AddListener(session.CancelEffect);
        for (int i = 0; i < kindButtons.Length; i++)
        { int index = i; kindButtons[i].onClick.AddListener(() => SelectKind(index)); }
        for (int i = 0; i < optionButtons.Length; i++)
        { int index = i; optionButtons[i].onClick.AddListener(() => SelectOption(index)); }
        Refresh();
    }
    public void SetTarget(MapTileInstance tile) => Refresh();
    public void ToggleExpanded() { if (session != null) session.Toggle(); Refresh(); }
    public void SelectKind(int index) { session.SelectKind(index); Refresh(); }
    public void SelectOption(int index) { session.SelectOption(index); Refresh(); }
    [ContextMenu("Refresh technology preview layout")]
    public void ApplyFold()
    {
        if (body != null) body.SetActive(expanded);
        if (tileContent != null) tileContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            expanded ? expandedContentHeight : foldedContentHeight);
        ((RectTransform)transform).anchoredPosition = new Vector2(12f, expanded ? -8f : -614f);
        if (foldLabel != null) foldLabel.text = expanded ? "地块科技 · 退出操作" : "地块科技 · 开始操作";
    }
    public void Refresh()
    {
        if (session == null) return;
        expanded = session.Active; ApplyFold();
        for (int i = 0; i < kindButtons.Length; i++)
            kindButtons[i].targetGraphic.color = i == (int)session.Choice.Kind ? selectedColor : normalColor;
        for (int i = 0; i < optionButtons.Length; i++)
        {
            optionButtons[i].GetComponentInChildren<TextMeshProUGUI>(true).text = session.OptionLabel(i);
            optionButtons[i].targetGraphic.color = session.OptionSelected(i) ? selectedColor : normalColor;
            optionButtons[i].gameObject.SetActive(session.OptionEnabled(i));
        }
        currentApplied.text = session.CurrentApplied;
        previewText.text = session.PreviewText;
        notice.text = session.Notice;
        deployButton.interactable = session.CanDeploy;
        cancelButton.interactable = session.Active && session.CanCancel;
    }
}
