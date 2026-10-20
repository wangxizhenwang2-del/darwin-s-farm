using DarwinFarm.Environment;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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
    private bool pendingOption, cancelPreview;
    private float suppressCancelUntil = -1f;
    private readonly List<RectTransform> protectedControls = new List<RectTransform>();
    private readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
    public bool Active { get; private set; }
    public bool HasPendingOption => pendingOption;
    public bool CancelPreviewActive => cancelPreview && CanCancel;
    public EnvironmentTechnologyChoice Choice { get; private set; } =
        new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.TemperatureChange);
    public EnvironmentTechnologyPreview Preview { get; private set; }
    public string Notice { get; private set; } = "";
    public string CurrentApplied { get; private set; } = "";
    public bool CanCancel { get; private set; }
    public bool CanDeploy => Active && pendingOption && TutorialFlowController.AllowsDeployment(Target, Choice) &&
        Preview != null && Preview.IsValid && Target != null &&
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
        if (Active && pendingOption && Pointer.current != null &&
            Pointer.current.press.wasPressedThisFrame &&
            !IsProtectedClick(Pointer.current.position.ReadValue())) ClearOption();
        if (Target != lastTarget) { lastTarget = Target; Notice = ""; Refresh(); }
        else if (Active && Time.unscaledTime >= nextRefresh) Refresh();
    }
    public void Begin()
    {
        if (!TutorialFlowController.Allows(TutorialFlowController.Feature.EnvironmentPreview)) return;
        if (buildMode == null) buildMode = FindFirstObjectByType<MapBuildModeController>();
        if (buildMode != null && buildMode.IsBuildMode) buildMode.SetBuildMode(false);
        Active = true; Refresh();
    }
    public void End()
    { Active = false; pendingOption = cancelPreview = false; Preview = null; Notice = ""; Changed?.Invoke(); }
    public void Toggle() { if (Active) End(); else Begin(); }
    public void SelectKind(int index)
    {
        if (index < 0 || index >= EnvironmentTechnologyCatalog.All.Length) return;
        var kind = EnvironmentTechnologyCatalog.All[index];
        if (!TutorialFlowController.AllowsTool(kind))
        { Notice = "这个工具尚未解锁"; Changed?.Invoke(); return; }
        int tier = kind == EnvironmentTechnologyKind.ElevationChange ?
            (Choice.Tier > 0 ? 1 : -1) : Choice.Tier;
        Choice = new EnvironmentTechnologyChoice(kind, tier);
        pendingOption = cancelPreview = false;
        Notice = ""; Begin();
    }
    public void SelectOption(int index)
    {
        if (index < 0 || index > 3 || !OptionEnabled(index)) return;
        Choice = new EnvironmentTechnologyChoice(Choice.Kind, Tier(index));
        pendingOption = true; cancelPreview = false;
        Notice = ""; Begin();
    }
    public void RegisterPreviewControls(Button[] options, Button apply)
    {
        if (options != null) foreach (var button in options)
            if (button != null && !protectedControls.Contains((RectTransform)button.transform))
                protectedControls.Add((RectTransform)button.transform);
        if (apply != null && !protectedControls.Contains((RectTransform)apply.transform))
            protectedControls.Add((RectTransform)apply.transform);
    }
    private bool IsProtectedClick(Vector2 position)
    {
        if (EventSystem.current == null) return false;
        pointerHits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, pointerHits);
        if (pointerHits.Count == 0) return false;
        var hit = pointerHits[0];
        foreach (var control in protectedControls)
            if (control != null && hit.gameObject.transform.IsChildOf(control)) return true;
        return false;
    }
    public void ClearOption()
    {
        if (!pendingOption) return;
        pendingOption = false; Notice = ""; Refresh();
    }
    public void SetCancelPreview(bool active)
    {
        active = active && CanCancel;
        if (cancelPreview == active) return;
        cancelPreview = active; Changed?.Invoke();
    }
    public void SuppressCancelClick() => suppressCancelUntil = Time.unscaledTime + .25f;
    public void BeginCancelPress() => suppressCancelUntil = -1f;
    private static int Tier(int index) => index == 0 ? 2 : index == 1 ? 1 : index == 2 ? -1 : -2;
    public bool OptionEnabled(int index) => index >= 0 && index <= 3 &&
        (Choice.Kind != EnvironmentTechnologyKind.ElevationChange || index == 1 || index == 2) &&
        TutorialFlowController.AllowsChoice(new EnvironmentTechnologyChoice(Choice.Kind, Tier(index)));
    public string OptionLabel(int index) => Choice.Kind == EnvironmentTechnologyKind.ElevationChange ?
        (Tier(index) > 0 ? "升一级" : "降一级") : (Tier(index) > 0 ? "提高" : "降低") + (Mathf.Abs(Tier(index)) == 1 ? "小档" : "大档");
    public bool OptionSelected(int index) => pendingOption && Choice.Tier == Tier(index);
    public void Refresh()
    {
        nextRefresh = Time.unscaledTime + .2f;
        if (controller == null) controller = FindFirstObjectByType<EnvironmentTechnologyController>();
        if (account == null) account = FindFirstObjectByType<BioenergyAccount>();
        if (environment == null) environment = FindFirstObjectByType<SimulationEnvironmentController>();
        Preview = Active && pendingOption && Target != null && controller != null ?
            controller.Preview(Target.Coordinate, Choice) : null;
        CurrentApplied = Target != null && controller != null ? controller.CurrentApplied(Target.Coordinate) : "请选择地块";
        CanCancel = TutorialFlowController.Allows(TutorialFlowController.Feature.CancelTool) &&
            Target != null && controller != null && controller.CanCancel(Target.Coordinate, Choice.Kind);
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
    public string PreviewText
    {
        get
        {
            if (Preview == null || Target == null) return "先点选一块地，再选择工具和档位。";
            string title = EnvironmentTechnologyCatalog.ShortName(Choice.Kind) +
                (Choice.Tier > 0 ? "+" : "-") + (Mathf.Abs(Choice.Tier) == 2 ?
                (Choice.Tier > 0 ? "+" : "-") : "");
            if (!Preview.IsValid) return title + " · 地块 " + Target.Coordinate + "\n" + Preview.Error;
            EnvironmentSnapshot state = null;
            if (environment != null && environment.TryRead(Target.Coordinate, out var read))
                state = read.Environment;
            return title + " · 地块 " + Target.Coordinate + "\n" +
                "状态：" + StatusText(state) + "\n" +
                "预计：" + Preview.After + "\n" +
                "用途：" + TraitForecast(state) + "\n" +
                "费用：" + Preview.Cost + " 生物能" +
                (account != null && account.Balance < Preview.Cost ? "（余额不足）" : "") +
                (string.IsNullOrEmpty(SlotNotice) ? "" : "\n" + SlotNotice);
        }
    }
    private string TraitForecast(EnvironmentSnapshot state)
    {
        if (state == null || Target.Block == null) return "暂无预测";
        if (state.IsWater) return "水域暂无明确预测";
        var community = Target.Block.community;
        if (community == null) return "暂无陆生动物";
        if (Choice.Kind == EnvironmentTechnologyKind.ElevationChange)
        {
            foreach (PopulationData population in community)
                if (population != null && population.speciesAmount > 0 &&
                    population.ecologicalNiche == EcologicalNiche.Land)
                    return "<color=#C9362B>可能改变生物的性状（因种群而异）</color>";
            return "暂无陆生动物";
        }
        int upward = 0, downward = 0, residents = 0;
        bool climate = Choice.Kind == EnvironmentTechnologyKind.TemperatureChange ||
            Choice.Kind == EnvironmentTechnologyKind.HumidityChange;
        bool temperature = Choice.Kind == EnvironmentTechnologyKind.TemperatureChange;
        double target = 0;
        if (climate)
        {
            var attribute = temperature ? EnvironmentAttribute.Temperature :
                EnvironmentAttribute.Humidity;
            target = EnvironmentRules.Clamp(attribute,
                (temperature ? state.Temperature.Value : state.Humidity.Value) +
                EnvironmentTechnologyCatalog.SignedClimate(Choice));
        }
        foreach (PopulationData population in community)
        {
            if (population == null || population.speciesAmount <= 0 ||
                population.ecologicalNiche != EcologicalNiche.Land ||
                (!climate && population.trophicLevel != 0)) continue;
            residents++;
            if (climate)
            {
                int current = temperature ? population.fitTemperature : population.fitHumidity;
                if (target > current) upward++;
                else if (target < current) downward++;
            }
            else if (Choice.Tier > 0) upward++;
            else downward++;
        }
        if (residents == 0) return "该工具暂无可引导的动物种群";
        string trait = temperature ? "适温" :
            Choice.Kind == EnvironmentTechnologyKind.HumidityChange ? "适湿" :
            Choice.Kind == EnvironmentTechnologyKind.PlantRecoveryChange ? "生育" : "体型";
        if (upward > 0 && downward > 0)
            return "<color=#C9362B>可能提高或降低生物的" + trait + "（因种群而异）</color>";
        if (upward > 0) return "<color=#C9362B>可能提高生物的" + trait + "</color>";
        if (downward > 0) return "<color=#C9362B>可能降低生物的" + trait + "</color>";
        return trait + "接近预计目标";
    }
    private string StatusText(EnvironmentSnapshot state)
    {
        if (Choice.Kind == EnvironmentTechnologyKind.PlantStockChange ||
            Choice.Kind == EnvironmentTechnologyKind.ElevationChange)
            return "投放后立即生效";
        bool waterRecovery = Choice.Kind == EnvironmentTechnologyKind.PlantRecoveryChange &&
            state != null && state.IsWater;
        if (state == null) return "待投放";
        AttributeSnapshot attribute = Choice.Kind == EnvironmentTechnologyKind.TemperatureChange ?
            state.Temperature : Choice.Kind == EnvironmentTechnologyKind.HumidityChange ?
            state.Humidity : state.Recovery;
        bool applied = Choice.Kind == EnvironmentTechnologyKind.TemperatureChange ?
            state.HasLocalTemperature : Choice.Kind == EnvironmentTechnologyKind.HumidityChange ?
            state.HasLocalHumidity : state.HasRecoveryCommand;
        if (!applied || attribute.Phase == EffectPhase.None ||
            attribute.Phase == EffectPhase.Automatic)
            return waterRecovery ? "待投放（投放后立即生效）" :
                "待投放（投放后约" + EnvironmentRules.TransitionDays + "天达到预计目标）";
        int days = Mathf.Max(0, attribute.RemainingDays);
        string target = attribute.Target.ToString("F0");
        switch (attribute.Phase)
        {
            case EffectPhase.Entering:
                return (attribute.Target >= attribute.Value ? "缓慢提升中" : "缓慢降低中") +
                    "（约" + days + "天后达到" + target + "）";
            case EffectPhase.Holding:
                return "维持中（剩余" + days + "天）";
            case EffectPhase.Returning:
                return "恢复中（约" + days + "天后回到" + target + "）";
            default:
                return "待投放";
        }
    }
    public void Deploy()
    {
        if (!CanDeploy || controller == null) return;
        bool success = controller.TryDeploy(Target.Coordinate, Choice, out var result);
        Notice = success ? "已应用" : result.Error;
        if (success) pendingOption = false;
        Refresh();
    }
    public void CancelEffect()
    {
        if (suppressCancelUntil >= 0f && Time.unscaledTime <= suppressCancelUntil)
        { suppressCancelUntil = -1f; return; }
        if (!Active || Target == null || controller == null) return;
        cancelPreview = false;
        Notice = controller.TryCancel(Target.Coordinate, Choice.Kind, out var error) ? "已撤销效果" : error;
        Refresh();
    }
    private void OnDisable()
    {
        if (information != null) information.SelectionChanged -= SelectionChanged;
        Active = pendingOption = cancelPreview = false; Preview = null;
    }
}
