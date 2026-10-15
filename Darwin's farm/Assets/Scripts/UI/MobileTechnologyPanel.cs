using DarwinFarm.Environment;
using TMPro;
using UnityEngine;

// UI selection and preview only. Deploy/cancel always go through the existing
// environment Controller and its terrain/water validation rules.
public sealed class MobileTechnologyPanel : MonoBehaviour
{
    [SerializeField] private EnvironmentTechnologyController controller;
    [SerializeField] private RectTransform tileContent;
    [SerializeField] private GameObject body;
    [SerializeField] private UnityEngine.UI.Button foldButton, deployButton, cancelButton;
    [SerializeField] private UnityEngine.UI.Button[] kindButtons, optionButtons;
    [SerializeField] private TextMeshProUGUI foldLabel, currentApplied, previewText, notice;
    [SerializeField] private EnvironmentTechnologyKind selectedKind;
    [SerializeField] private bool expanded;
    [SerializeField, Min(1f)] private float foldedContentHeight = 690f, expandedContentHeight = 1090f;
    [Header("Selection appearance")]
    [SerializeField] private Color selectedColor = new Color(.58f, .83f, .64f);
    [SerializeField] private Color normalColor = Color.white;
    private MapTileInstance target;
    private int tier = 1, windTemperature = 20, windHumidity = 20;
    private bool bound;

    public bool Expanded => expanded;
    private EnvironmentTechnologyChoice Choice => new EnvironmentTechnologyChoice(
        selectedKind, tier, windTemperature, windHumidity);

    public void Initialize()
    {
        if (bound) return;
        // The existing map bridge creates this Controller in Awake at runtime.
        if (controller == null) controller = FindFirstObjectByType<EnvironmentTechnologyController>();
        bound = true;
        foldButton.onClick.AddListener(ToggleExpanded);
        deployButton.onClick.AddListener(Deploy);
        cancelButton.onClick.AddListener(Cancel);
        for (int i = 0; i < kindButtons.Length; i++)
        { int index = i; kindButtons[i].onClick.AddListener(() => SelectKind(index)); }
        for (int i = 0; i < optionButtons.Length; i++)
        { int index = i; optionButtons[i].onClick.AddListener(() => SelectOption(index)); }
        ApplyFold();
    }

    public void SetTarget(MapTileInstance tile)
    {
        if (target != tile) { target = tile; notice.text = ""; }
        Refresh();
    }
    public void ToggleExpanded() { expanded = !expanded; ApplyFold(); Refresh(); }
    [ContextMenu("Refresh technology preview layout")]
    public void ApplyFold()
    {
        if (body != null) body.SetActive(expanded);
        if (tileContent != null) tileContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            expanded ? expandedContentHeight : foldedContentHeight);
        if (foldLabel != null) foldLabel.text = expanded ? "地块科技 · 收起" : "地块科技 · 展开";
    }

    public void SelectKind(int index)
    {
        if (index < 0 || index >= EnvironmentTechnologyCatalog.All.Length) return;
        selectedKind = EnvironmentTechnologyCatalog.All[index];
        if (selectedKind == EnvironmentTechnologyKind.MonsoonAnchor ||
            (selectedKind == EnvironmentTechnologyKind.ElevationChange && Mathf.Abs(tier) > 1)) tier = 1;
        notice.text = ""; Refresh();
    }

    public void SelectOption(int index)
    {
        if (index < 0 || index > 3) return;
        if (selectedKind == EnvironmentTechnologyKind.MonsoonAnchor)
        {
            tier = 1;
            windTemperature = index == 0 || index == 3 ? -20 : 20;
            windHumidity = index < 2 ? 20 : -20;
        }
        else tier = index == 0 ? 2 : index == 1 ? 1 : index == 2 ? -1 : -2;
        notice.text = ""; Refresh();
    }

    public void Refresh()
    {
        if (currentApplied == null) return;
        for (int i = 0; i < kindButtons.Length; i++)
        {
            var image = kindButtons[i].targetGraphic;
            if (image != null) image.color = i == (int)selectedKind ? selectedColor : normalColor;
        }
        for (int i = 0; i < optionButtons.Length; i++)
        {
            bool wind = selectedKind == EnvironmentTechnologyKind.MonsoonAnchor;
            int value = i == 0 ? 2 : i == 1 ? 1 : i == 2 ? -1 : -2;
            string label = wind ? (i == 0 ? "冷湿" : i == 1 ? "暖湿" : i == 2 ? "干热" : "干冷") :
                (value > 0 ? "高" : "低") + Mathf.Abs(value) + "档";
            bool selected = wind ? windTemperature == (i == 0 || i == 3 ? -20 : 20) &&
                windHumidity == (i < 2 ? 20 : -20) : tier == value;
            optionButtons[i].GetComponentInChildren<TextMeshProUGUI>(true).text = label;
            if (optionButtons[i].targetGraphic != null)
                optionButtons[i].targetGraphic.color = selected ? selectedColor : normalColor;
            optionButtons[i].interactable = selectedKind != EnvironmentTechnologyKind.ElevationChange || Mathf.Abs(value) == 1;
        }
        if (target == null || controller == null)
        {
            currentApplied.text = "长按选择地块后可查看科技";
            previewText.text = "选择科技和档位查看真实效果预览；投放与撤销调用现有生态规则。";
            deployButton.interactable = cancelButton.interactable = false;
            return;
        }
        currentApplied.text = controller.CurrentApplied(target.Coordinate);
        if (!expanded) return;
        EnvironmentTechnologyPreview preview = controller.Preview(target.Coordinate, Choice);
        previewText.text = EnvironmentTechnologyCatalog.Name(selectedKind) + "\n" +
            (preview.Before ?? "") + "\n" + (preview.IsValid ? preview.After + "\n" + preview.Timing +
            "\n费用：" + preview.Cost + "（经济未接入）" : preview.Error);
        deployButton.interactable = preview.IsValid;
        cancelButton.interactable = controller.CanCancel(target.Coordinate, selectedKind);
    }

    private void Deploy()
    {
        if (target == null || controller == null) return;
        bool success = controller.TryDeploy(target.Coordinate, Choice, out EnvironmentTechnologyPreview result);
        notice.text = success ? "已投放" : result.Error;
        Refresh();
    }
    private void Cancel()
    {
        if (target == null || controller == null) return;
        notice.text = controller.TryCancel(target.Coordinate, selectedKind, out string error) ? "已撤销" : error;
        Refresh();
    }
}
