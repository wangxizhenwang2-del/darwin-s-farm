using DarwinFarm.Environment;
using UnityEngine;
using UnityEngine.InputSystem;

// Shared interaction state for desktop and touch. Only the existing controller writes ecology.
[DisallowMultipleComponent]
public sealed class EnvironmentToolSession : MonoBehaviour
{
    private GameInformationUI information;
    private EnvironmentTechnologyController controller;
    private MapBuildModeController buildMode;
    private BioenergyAccount account;
    private SimulationEnvironmentController environment;
    private MapTileInstance lastTarget;
    private float nextRefresh;
    public bool Active { get; private set; }
    public EnvironmentTechnologyChoice Choice { get; private set; } =
        new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.TemperatureChange);
    public EnvironmentTechnologyPreview Preview { get; private set; }
    public string Notice { get; private set; } = "";
    public string CurrentApplied { get; private set; } = "";
    public bool CanCancel { get; private set; }
    public bool CanDeploy => Active && Preview != null && Preview.IsValid && Target != null &&
        (account == null || account.Balance >= Preview.Cost);
    public string SlotNotice { get; private set; } = "";
    public MapTileInstance Target => information != null ? information.SelectedTile : null;
    public event System.Action Changed;

    private void Awake() { information = GetComponent<GameInformationUI>(); }
    private void OnEnable()
    {
        if (information == null) information = GetComponent<GameInformationUI>();
        if (information != null) information.SelectionChanged += SelectionChanged;
    }
    private void SelectionChanged(bool population)
    { if (!population) { lastTarget = Target; Notice = ""; Refresh(); } }
    private void Update()
    {
        if (buildMode == null) buildMode = FindFirstObjectByType<MapBuildModeController>();
        if (Active && ((buildMode != null && buildMode.IsBuildMode) ||
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))) End();
        if (Target != lastTarget) { lastTarget = Target; Notice = ""; Refresh(); }
        else if (Active && Time.unscaledTime >= nextRefresh) Refresh();
    }
    public void Begin()
    {
        if (buildMode == null) buildMode = FindFirstObjectByType<MapBuildModeController>();
        if (buildMode != null && buildMode.IsBuildMode) buildMode.SetBuildMode(false);
        Active = true; Refresh();
    }
    public void End() { Active = false; Preview = null; Notice = ""; Changed?.Invoke(); }
    public void Toggle() { if (Active) End(); else Begin(); }
    public void SelectKind(int index)
    {
        if (index < 0 || index >= EnvironmentTechnologyCatalog.All.Length) return;
        var kind = EnvironmentTechnologyCatalog.All[index];
        int tier = kind == EnvironmentTechnologyKind.ElevationChange ?
            (Choice.Tier > 0 ? 1 : -1) : Choice.Tier;
        Choice = new EnvironmentTechnologyChoice(kind, tier);
        Notice = ""; Begin();
    }
    public void SelectOption(int index)
    {
        if (index < 0 || index > 3 || !OptionEnabled(index)) return;
        Choice = new EnvironmentTechnologyChoice(Choice.Kind, Tier(index));
        Notice = ""; Begin();
    }
    private static int Tier(int index) => index == 0 ? 2 : index == 1 ? 1 : index == 2 ? -1 : -2;
    public bool OptionEnabled(int index) => Choice.Kind != EnvironmentTechnologyKind.ElevationChange || index == 1 || index == 2;
    public string OptionLabel(int index) => Choice.Kind == EnvironmentTechnologyKind.ElevationChange ?
        (Tier(index) > 0 ? "升一级" : "降一级") : (Tier(index) > 0 ? "提高" : "降低") + (Mathf.Abs(Tier(index)) == 1 ? "小档" : "大档");
    public bool OptionSelected(int index) => Choice.Tier == Tier(index);
    public void Refresh()
    {
        nextRefresh = Time.unscaledTime + .2f;
        if (controller == null) controller = FindFirstObjectByType<EnvironmentTechnologyController>();
        if (account == null) account = FindFirstObjectByType<BioenergyAccount>();
        if (environment == null) environment = FindFirstObjectByType<SimulationEnvironmentController>();
        Preview = Active && Target != null && controller != null ? controller.Preview(Target.Coordinate, Choice) : null;
        CurrentApplied = Target != null && controller != null ? controller.CurrentApplied(Target.Coordinate) : "请选择地块";
        CanCancel = Target != null && controller != null && controller.CanCancel(Target.Coordinate, Choice.Kind);
        SlotNotice = "";
        if (Target != null && environment != null && environment.TryRead(Target.Coordinate, out var read))
        {
            var state = read.Environment;
            bool temperature = state.HasLocalTemperature && state.Temperature.Phase != EffectPhase.Returning;
            bool humidity = state.HasLocalHumidity && state.Humidity.Phase != EffectPhase.Returning;
            bool plants = state.HasRecoveryCommand && state.Recovery.Phase != EffectPhase.Returning;
            int locals = (temperature ? 1 : 0) + (humidity ? 1 : 0) + (plants ? 1 : 0);
            SlotNotice = "生效名额 " + locals + "/2";
            bool same = Choice.Kind == EnvironmentTechnologyKind.TemperatureChange ? temperature :
                Choice.Kind == EnvironmentTechnologyKind.HumidityChange ? humidity :
                Choice.Kind == EnvironmentTechnologyKind.PlantRecoveryChange && plants;
            if ((Choice.Kind == EnvironmentTechnologyKind.TemperatureChange ||
                Choice.Kind == EnvironmentTechnologyKind.HumidityChange ||
                Choice.Kind == EnvironmentTechnologyKind.PlantRecoveryChange) &&
                !same && locals >= 2)
                SlotNotice += " · 较早的单格工具将进入恢复";
        }
        Changed?.Invoke();
    }
    public string PreviewText => Preview == null || Target == null ? "点选地图地块，然后选择工具和档位。\n拖动、缩放地图仍可使用。" :
        "目标地块 " + Target.Coordinate + " · " + EnvironmentTechnologyCatalog.Name(Choice.Kind) + "\n" +
        (Preview.Before ?? "") + "\n" + (Preview.IsValid ? Preview.After + "\n" + Preview.Timing +
        "\n影响 " + Preview.Area.Count + " 格 · 费用 " + Preview.Cost + " 生物能" +
        (account != null && account.Balance < Preview.Cost ? "（余额不足）" : "") + "\n" + SlotNotice : Preview.Error);
    public void Deploy()
    {
        if (!Active || Target == null || controller == null) return;
        Notice = controller.TryDeploy(Target.Coordinate, Choice, out var result) ? "已应用" : result.Error;
        Refresh();
    }
    public void CancelEffect()
    {
        if (!Active || Target == null || controller == null) return;
        Notice = controller.TryCancel(Target.Coordinate, Choice.Kind, out var error) ? "已撤销效果" : error;
        Refresh();
    }
    private void OnDisable()
    {
        if (information != null) information.SelectionChanged -= SelectionChanged;
        Active = false; Preview = null;
    }
}
