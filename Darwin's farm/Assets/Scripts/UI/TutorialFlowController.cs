using System;
using DarwinFarm.Environment;
using UnityEngine;

// Copy, day gates and unlock order live in TutorialFlow.json; actions come from game events.
public sealed class TutorialFlowController : MonoBehaviour
{
    [Serializable] private sealed class Flow
    {
        public int tutorialSeed;
        public Step[] steps;
    }
    [Serializable] private sealed class Step
    {
        public int day;
        public int temperatureAtLeast;
        public string lesson, text, unlocks, completion;
        public bool pause, lockSkip;
    }

    public enum Feature
    {
        Clock, FastClock, PopulationInfo, TileInfo, SpeciesMap,
        EnvironmentPreview, PlantRecovery, Temperature, Humidity, PlantStock,
        CancelTool, Build, AllToolTiers, Advanced
    }

    [Serializable] public sealed class SaveState
    {
        public bool active, satisfied, sawPause, colorUnlocked, startupCardClosed;
        public bool tutorialPaused, resumePending;
        public bool tutorialSeedApplied;
        public bool temperatureTargetSet;
        public int temperatureTargetX, temperatureTargetY;
        public bool firstTerrainHintSeen, firstTerrainHintPending;
        public string firstTerrainName;
        public int stepIndex;
    }

    public static TutorialFlowController Current { get; private set; }
    public static bool Allows(Feature feature) => Current == null || Current.AllowsHere(feature);
    public static bool RevealsSpeciesName(SpeciesData species) =>
        species != null && Current != null &&
        Current.HasReachedUnlock(Feature.SpeciesMap) &&
        !string.IsNullOrEmpty(species.SpeciesId) &&
        species.SpeciesId.Replace("-", "") == ColorChickenId;
    public static bool TryGetBubbleScreenRect(out Rect rect)
    {
        rect = default;
        return Current != null && Current.bubble != null && Current.bubble.TryGetScreenRect(out rect);
    }
    public static void Report(string action) { if (Current != null) Current.Complete(action); }
    public static bool HandlesFirstTerrainNews(TerrainKind kind) =>
        Current != null && Current.active &&
        (!Current.firstTerrainHintSeen ||
         (Current.firstTerrainHintPending || Current.showingTerrainHint) &&
         Current.firstTerrainName == EcologyNewsDetector.TerrainName(kind));
    public static void ReportTemperatureApplied(Vector2Int coordinate)
    {
        if (Current == null || !Current.active || Current.CurrentStep.completion != "temperature") return;
        Current.temperatureTarget = coordinate;
        Current.temperatureTargetSet = true;
        Current.Complete("temperature");
    }
    public static void ReportColorClue(SpeciesData species)
    {
        if (species != null && !string.IsNullOrEmpty(species.SpeciesId) &&
            species.SpeciesId.Replace("-", "") == ColorChickenId) Report("colorClue");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<WhiteboxBiowebTestSetup>() == null ||
            FindFirstObjectByType<DarwinJournalCanvas>() == null) return;
        new GameObject("TutorialFlowController").AddComponent<TutorialFlowController>();
    }

    private Flow flow;
    private DarwinJournalCanvas journal;
    private SimulationTime clock;
    private SimulationController simulation;
    private SimulationEnvironmentController environment;
    private GameInformationUI information;
    private EnvironmentToolSession tools;
    private SpeciesUnlockPresenter discovery;
    private SpeciesMapsUI maps;
    private TutorialBubbleView bubble;
    private int stepIndex;
    private float enteredAt;
    private bool active, satisfied, sawPause, colorUnlocked, startupCardClosed, restored;
    private bool ownedPause, resumePending;
    private bool tutorialSeedApplied, pendingTutorialSeed;
    private bool temperatureTargetSet;
    private Vector2Int temperatureTarget;
    private bool firstTerrainHintSeen, firstTerrainHintPending, showingTerrainHint;
    private string firstTerrainName;
    private const string ColorChickenId = "b59a4a829b4949c5813cf76caa760b47";
    private const string StarterChickenId = "7f1137399e3946cf95ad1054096acacb";

    private void Awake()
    {
        if (Current != null) { Destroy(gameObject); return; }
        Current = this;
        TextAsset source = Resources.Load<TextAsset>("TutorialFlow");
        flow = source == null ? null : JsonUtility.FromJson<Flow>(source.text);
        if (flow == null || flow.steps == null || flow.steps.Length == 0)
        {
            Debug.LogError("TutorialFlow.json is missing or empty.", this);
            Current = null;
            Destroy(gameObject);
            return;
        }
        journal = FindFirstObjectByType<DarwinJournalCanvas>();
        clock = FindFirstObjectByType<SimulationTime>();
        simulation = FindFirstObjectByType<SimulationController>();
        environment = FindFirstObjectByType<SimulationEnvironmentController>();
        information = FindFirstObjectByType<GameInformationUI>();
        tools = FindFirstObjectByType<EnvironmentToolSession>();
        discovery = FindFirstObjectByType<SpeciesUnlockPresenter>();
        maps = FindFirstObjectByType<SpeciesMapsUI>();
        if (journal != null) journal.GameEntered += Begin;
        if (clock != null) clock.OnDayChanged += DayChanged;
        if (environment != null) environment.Changed += EnvironmentChanged;
        if (environment != null) environment.TerrainUnlocked += TerrainUnlocked;
        if (simulation != null) simulation.OnSpeciesUnlocked += SpeciesUnlocked;
        if (information != null) information.SelectionChanged += SelectionChanged;
        bubble = TutorialBubbleView.Create(transform, Continue, Skip);
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        if (journal != null) journal.GameEntered -= Begin;
        if (clock != null) clock.OnDayChanged -= DayChanged;
        if (environment != null) environment.Changed -= EnvironmentChanged;
        if (environment != null) environment.TerrainUnlocked -= TerrainUnlocked;
        if (simulation != null) simulation.OnSpeciesUnlocked -= SpeciesUnlocked;
        if (information != null) information.SelectionChanged -= SelectionChanged;
    }

    private void Begin(bool loadedSave)
    {
        if (loadedSave && restored) { Refresh(); return; }
        // Saves created before this flow keep their existing access.
        active = !loadedSave && (journal == null || journal.NewGameTutorialEnabled);
        stepIndex = 0;
        satisfied = sawPause = colorUnlocked = startupCardClosed = false;
        tutorialSeedApplied = false;
        temperatureTargetSet = false;
        firstTerrainHintSeen = firstTerrainHintPending = showingTerrainHint = false;
        firstTerrainName = null;
        pendingTutorialSeed = active;
        if (active)
        {
            TryApplyTutorialSeed();
            EnterStep();
        }
        Refresh();
    }

    public SaveState CaptureSaveState() => new SaveState {
        active = active, stepIndex = stepIndex, satisfied = satisfied,
        sawPause = sawPause, colorUnlocked = colorUnlocked,
        startupCardClosed = startupCardClosed,
        tutorialPaused = ownedPause, resumePending = resumePending,
        tutorialSeedApplied = tutorialSeedApplied,
        temperatureTargetSet = temperatureTargetSet,
        temperatureTargetX = temperatureTarget.x, temperatureTargetY = temperatureTarget.y,
        firstTerrainHintSeen = firstTerrainHintSeen,
        firstTerrainHintPending = firstTerrainHintPending || showingTerrainHint,
        firstTerrainName = firstTerrainName
    };

#if UNITY_EDITOR
    public void CompleteForAutomation() { FinishTutorial(); }
#endif

    public void RestoreSaveState(SaveState state)
    {
        if (state == null) return;
        if (state.stepIndex < 0 || state.stepIndex >= flow.steps.Length)
            throw new ArgumentException("Invalid tutorial step in save.");
        active = state.active;
        stepIndex = state.stepIndex;
        satisfied = state.satisfied;
        sawPause = state.sawPause;
        colorUnlocked = state.colorUnlocked;
        startupCardClosed = state.startupCardClosed;
        ownedPause = state.tutorialPaused;
        resumePending = state.resumePending;
        tutorialSeedApplied = state.tutorialSeedApplied;
        temperatureTargetSet = state.temperatureTargetSet;
        temperatureTarget = new Vector2Int(state.temperatureTargetX, state.temperatureTargetY);
        firstTerrainHintSeen = state.firstTerrainHintSeen;
        firstTerrainHintPending = state.firstTerrainHintPending;
        firstTerrainName = state.firstTerrainName;
        showingTerrainHint = false;
        pendingTutorialSeed = active && !tutorialSeedApplied && clock != null && clock.currentDay == 0;
        restored = true;
        Refresh();
    }

    public void Skip()
    {
        if (active && CurrentStep.lockSkip && !satisfied) return;
        FinishTutorial();
    }

    private void FinishTutorial()
    {
        if (!active) return;
        active = false;
        firstTerrainHintPending = showingTerrainHint = false;
        satisfied = false;
        pendingTutorialSeed = false;
        if (ownedPause) { resumePending = true; ownedPause = false; }
        Refresh();
    }

    private void Update()
    {
        if (pendingTutorialSeed) TryApplyTutorialSeed();
        if (resumePending && journal != null && journal.IsGameplayVisible &&
            (discovery == null || !discovery.IsPresenting) &&
            (maps == null || !maps.IsOpen))
        {
            resumePending = false;
            if (clock != null && clock.IsPaused) clock.Play();
        }
        if (!active) return;
        // A restored save can already be on the target day without firing a new
        // OnDayChanged event. Reconcile day gates from the current clock as well.
        if (stepIndex + 1 < flow.steps.Length && clock != null &&
            NextStepReady())
        {
            if (satisfied) Advance();
            else if (CurrentStep.completion == "day") Complete("day");
        }
        if (!active) return;
        if (CurrentStep.completion == "startupCard" && Time.unscaledTime - enteredAt > 7f &&
            (discovery == null || (!discovery.IsPresenting && discovery.QueuedCount == 0)))
            Complete("startupCard");
        if (CurrentStep.completion == "toolPreview" && tools != null && tools.Active && tools.Target != null)
            Complete("toolPreview");
        if (CurrentStep.completion == "colorUnlocked" && colorUnlocked)
            Complete("colorUnlocked");
        bubble.SetVisible(journal != null && journal.IsGameplayVisible &&
            (discovery == null || !discovery.IsPresenting));
    }

    private Step CurrentStep => flow.steps[Mathf.Clamp(stepIndex, 0, flow.steps.Length - 1)];

    private void TryApplyTutorialSeed()
    {
        // The WhiteBox setup has already chosen its run seed and placed the
        // starting population. This only changes the ecology's keyed draws;
        // it never touches UnityEngine.Random or consumes a startup draw.
        if (!pendingTutorialSeed || simulation == null || clock == null ||
            !simulation.IsDeterministicRunConfigured) return;
        if (clock.currentDay != 0)
        {
            Debug.LogWarning("Tutorial seed was not applied before Day 1.", this);
            pendingTutorialSeed = false;
            return;
        }
        simulation.ConfigureDeterministicRun(flow.tutorialSeed);
        tutorialSeedApplied = true;
        pendingTutorialSeed = false;
    }

    private void DayChanged(int day)
    {
        if (active && CurrentStep.completion == "day" &&
            stepIndex + 1 < flow.steps.Length && day >= flow.steps[stepIndex + 1].day)
            satisfied = true;
        Advance();
    }

    private void EnvironmentChanged(System.Collections.Generic.IReadOnlyList<EnvironmentReadSnapshot> _)
    {
        if (active && satisfied) Advance();
    }

    private void TerrainUnlocked(TerrainKind kind, Vector2Int _)
    {
        if (!active || firstTerrainHintSeen) return;
        firstTerrainHintSeen = firstTerrainHintPending = true;
        firstTerrainName = EcologyNewsDetector.TerrainName(kind);
        Refresh();
    }

    private void Continue()
    {
        if (showingTerrainHint)
        {
            showingTerrainHint = false;
            Refresh();
            return;
        }
        Complete("continue");
    }

    private bool NextStepReady()
    {
        if (clock == null || stepIndex + 1 >= flow.steps.Length) return false;
        Step next = flow.steps[stepIndex + 1];
        if (clock.currentDay < next.day) return false;
        if (next.temperatureAtLeast <= 0) return true;
        Vector2Int coordinate = temperatureTargetSet ? temperatureTarget : Vector2Int.zero;
        return environment != null && environment.TryRead(coordinate, out EnvironmentReadSnapshot read) &&
            read.Environment.Temperature.Value >= next.temperatureAtLeast;
    }

    private void SelectionChanged(bool population)
    {
        if (information == null) return;
        if (population && information.SelectedPopulation != null) Complete("selectPopulation");
        else if (!population && information.TileIsOpen) Complete("selectTile");
    }

    private void SpeciesUnlocked(SpeciesData species)
    {
        if (species == null || string.IsNullOrEmpty(species.SpeciesId) ||
            species.SpeciesId.Replace("-", "") != ColorChickenId) return;
        colorUnlocked = true;
        Complete("colorUnlocked");
    }

    private void Complete(string action)
    {
        if (active && action == "startupCard") startupCardClosed = true;
        if (!active || CurrentStep.completion != action || satisfied) return;
        satisfied = true;
        Advance();
    }

    private void Advance()
    {
        if (!active || clock == null) return;
        while (satisfied && NextStepReady())
        {
            stepIndex++;
            satisfied = false;
            EnterStep();
            if (CurrentStep.completion == "startupCard" && startupCardClosed) satisfied = true;
            if (CurrentStep.completion == "colorUnlocked" && colorUnlocked) satisfied = true;
        }
        // A paused narrative row may hand off to a later day. Let the clock reach it.
        if (satisfied && stepIndex + 1 < flow.steps.Length && ownedPause &&
            !NextStepReady())
        { ownedPause = false; resumePending = true; }
        if (satisfied && stepIndex == flow.steps.Length - 1) FinishTutorial();
        Refresh();
    }

    private void EnterStep()
    {
        enteredAt = Time.unscaledTime;
        if (clock == null) return;
        if (CurrentStep.pause)
        {
            resumePending = false;
            if (!clock.IsPaused) { clock.Pause(); ownedPause = true; }
        }
        else if (ownedPause)
        {
            ownedPause = false;
            resumePending = true;
        }
    }

    private bool AllowsHere(Feature feature)
    {
        return !active || HasReachedUnlock(feature);
    }

    private bool HasReachedUnlock(Feature feature)
    {
        string key = feature.ToString();
        for (int i = 0; i <= stepIndex; i++)
        {
            string[] names = (flow.steps[i].unlocks ?? "").Split(',');
            foreach (string name in names)
                if (string.Equals(name.Trim(), key, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public static bool AllowsTool(EnvironmentTechnologyKind kind)
    {
        switch (kind)
        {
            case EnvironmentTechnologyKind.PlantRecoveryChange: return Allows(Feature.PlantRecovery);
            case EnvironmentTechnologyKind.TemperatureChange: return Allows(Feature.Temperature);
            case EnvironmentTechnologyKind.HumidityChange: return Allows(Feature.Humidity);
            case EnvironmentTechnologyKind.PlantStockChange: return Allows(Feature.PlantStock);
            default: return Allows(Feature.Advanced);
        }
    }

    public static bool AllowsChoice(EnvironmentTechnologyChoice choice) =>
        Current == null || Current.AllowsChoiceHere(choice);

    public static bool AllowsDeployment(MapTileInstance tile, EnvironmentTechnologyChoice choice) =>
        Current == null || Current.AllowsDeploymentHere(tile, choice);

    private bool AllowsChoiceHere(EnvironmentTechnologyChoice choice)
    {
        if (!AllowsTool(choice.Kind)) return false;
        return !active || AllowsHere(Feature.AllToolTiers) ||
            choice.Kind == EnvironmentTechnologyKind.TemperatureChange && choice.Tier == 1;
    }

    private bool AllowsDeploymentHere(MapTileInstance tile, EnvironmentTechnologyChoice choice)
    {
        if (!AllowsChoiceHere(choice)) return false;
        if (!active || CurrentStep.completion != "temperature") return true;
        if (tile == null || tile.Block == null || tile.Block.community == null) return false;
        foreach (PopulationData population in tile.Block.community)
            if (population != null && population.speciesAmount > 0 &&
                population.species != null &&
                string.Equals(population.species.SpeciesId?.Replace("-", ""),
                    StarterChickenId, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void Refresh()
    {
        if (information != null) information.RefreshTutorialLocks();
        if (bubble == null) return;
        bubble.SetVisible(active && journal != null && journal.IsGameplayVisible &&
            (discovery == null || !discovery.IsPresenting));
        if (!active) return;
        if (firstTerrainHintPending && (!CurrentStep.lockSkip || satisfied))
        {
            firstTerrainHintPending = false;
            showingTerrainHint = true;
        }
        if (showingTerrainHint)
        {
            bubble.SetLesson("新地形", "新地形【" + firstTerrainName + "】解锁了！",
                true, !CurrentStep.lockSkip || satisfied);
            return;
        }
        string message = CurrentStep.text;
        if (satisfied && stepIndex + 1 < flow.steps.Length)
        {
            Step next = flow.steps[stepIndex + 1];
            message += next.temperatureAtLeast > 0 ?
                "\n等待地块温度达到 " + next.temperatureAtLeast + " 度…" :
                "\n等待第 " + next.day + " 天…";
        }
        bool allowSkip = !CurrentStep.lockSkip || satisfied;
        if (!allowSkip) message += "\n完成当前操作后可跳过教程。";
        bubble.SetLesson("引导 " + (stepIndex + 1) + "/" + flow.steps.Length + " · " +
            CurrentStep.lesson, message, !satisfied && CurrentStep.completion == "continue",
            allowSkip);
    }
}
