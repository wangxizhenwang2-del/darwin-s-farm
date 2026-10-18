using System;
using UnityEngine;

public partial class SimulationController
{
    [Serializable] public sealed class SaveState
    {
        public int day, seed, communityEditSerial;
        public bool deterministic;
        public string tuning, landRules;
        public bool airEcology, waterEcology, randomEvents, neighborMovement;
        public float neighborLambda;
    }
    public SaveState CaptureSaveState() => new SaveState {
        day=currentSimulationDay, seed=simulationSeed,
        communityEditSerial=communityEditSerial,
        deterministic=IsDeterministicRunConfigured,
        tuning=JsonUtility.ToJson(ReadTuning()),landRules=JsonUtility.ToJson(ReadLandRules()),
        airEcology=enableAirEcology,waterEcology=enableWaterEcology,
        randomEvents=enableRandomEvents,neighborMovement=enableNeighborMovementFormula,
        neighborLambda=neighborOpportunityLambda
    };
    public void RestoreSaveState(SaveState state)
    {
        if (state == null || state.day < 0 || state.communityEditSerial < 0)
            throw new ArgumentException("Invalid saved simulation state.");
        if (string.IsNullOrWhiteSpace(state.tuning)||string.IsNullOrWhiteSpace(state.landRules))
            throw new ArgumentException("Missing saved simulation tuning.");
        SimulationTuning tuning=JsonUtility.FromJson<SimulationTuning>(state.tuning);
        ApplyTuning(tuning);
        ApplyLandRules(JsonUtility.FromJson<LandRuleTuning>(state.landRules));
        SetAirEcologyEnabled(state.airEcology);
        SetWaterEcologyEnabled(state.waterEcology);
        enableRandomEvents=state.randomEvents;
        enableNeighborMovementFormula=state.neighborMovement;
        neighborOpportunityLambda=state.neighborLambda;
        currentSimulationDay=state.day;
        simulationSeed=state.seed;
        communityEditSerial=state.communityEditSerial;
        IsDeterministicRunConfigured=state.deterministic;
        EnsurePopulationIds();
        UpdateSpeciesStatuses();
    }
}
