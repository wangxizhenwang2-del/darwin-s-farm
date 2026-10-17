using TMPro;
using UnityEngine;

public sealed class DesktopEnvironmentTools : MonoBehaviour
{
    private static readonly string[] kindLabels = { "温度", "湿度", "存量", "增量", "海拔" };
    [SerializeField] private EnvironmentToolSession session;
    [SerializeField] private MobileGameUI mobile;
    [SerializeField] private GameObject toolbar, body;
    [SerializeField] private UnityEngine.UI.Button[] kinds, options;
    [SerializeField] private UnityEngine.UI.Button apply, cancel, exit;
    [SerializeField] private TextMeshProUGUI preview, current, notice;
    private void Start()
    {
        for (int i = 0; i < kinds.Length; i++)
        {
            int index = i;
            kinds[i].onClick.AddListener(() => session.SelectKind(index));
            if (i < kindLabels.Length)
                kinds[i].GetComponentInChildren<TextMeshProUGUI>(true).text = kindLabels[i];
        }
        for (int i = 0; i < options.Length; i++)
        { int index = i; options[i].onClick.AddListener(() => session.SelectOption(index)); }
        apply.onClick.AddListener(session.Deploy);
        cancel.onClick.AddListener(session.CancelEffect);
        exit.onClick.AddListener(session.End);
    }
    private void LateUpdate()
    {
        bool visible = mobile == null || !mobile.IsMobileActive;
        toolbar.SetActive(visible);
        body.SetActive(visible && session.Active);
        if (!visible) return;
        for (int i = 0; i < kinds.Length; i++)
            kinds[i].targetGraphic.color = session.Active &&
                i < DarwinFarm.Environment.EnvironmentTechnologyCatalog.All.Length &&
                DarwinFarm.Environment.EnvironmentTechnologyCatalog.All[i] == session.Choice.Kind ?
                new Color(.58f,.83f,.64f) : Color.white;
        if (!session.Active) return;
        transform.SetAsLastSibling();
        for (int i = 0; i < options.Length; i++)
        {
            options[i].gameObject.SetActive(session.OptionEnabled(i));
            options[i].GetComponentInChildren<TextMeshProUGUI>(true).text = session.OptionLabel(i);
            options[i].targetGraphic.color = session.OptionSelected(i) ? new Color(.58f,.83f,.64f) : Color.white;
        }
        preview.text = session.PreviewText;
        current.text = session.CurrentApplied;
        notice.text = session.Notice;
        apply.interactable = session.CanDeploy;
        cancel.interactable = session.CanCancel;
    }
}
