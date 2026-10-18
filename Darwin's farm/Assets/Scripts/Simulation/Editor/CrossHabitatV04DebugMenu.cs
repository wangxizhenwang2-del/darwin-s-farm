#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class CrossHabitatV04DebugMenu
{
    private static SimulationController Current()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Enter Play mode before changing cross habitat debug values.");
            return null;
        }
        SimulationController simulation =
            Object.FindFirstObjectByType<SimulationController>();
        if (simulation == null)
            Debug.LogWarning("No SimulationController in the active scene.");
        return simulation;
    }

    [MenuItem("Tools/Darwin Farm/Debug/Cross Habitat/Enable Water And Air")]
    private static void Enable()
    {
        SimulationController simulation = Current();
        if (simulation == null) return;
        simulation.SetWaterEcologyEnabled(true);
        simulation.SetAirEcologyEnabled(true);
        simulation.EnableRandomEvents = false;
        Debug.Log("Cross habitat water and flight enabled for this scene instance.");
    }

    [MenuItem("Tools/Darwin Farm/Debug/Cross Habitat/Force Land To Air Roll")]
    private static void ForceLandAir() => SetRolls(1f, 0f, 0f);

    [MenuItem("Tools/Darwin Farm/Debug/Cross Habitat/Force Air To Land Roll")]
    private static void ForceAirLand() => SetRolls(0f, 1f, 0f);

    [MenuItem("Tools/Darwin Farm/Debug/Cross Habitat/Force Eligible Water Migration Roll")]
    private static void ForceWaterMove() => SetRolls(0f, 0f, 1f);

    [MenuItem("Tools/Darwin Farm/Debug/Cross Habitat/Increase Flight Move Opportunity")]
    private static void FlightMove()
    {
        SimulationController simulation = Current();
        if (simulation == null) return;
        simulation.SetFlightCandidateParameters(.1f, 50, 1f);
        Debug.Log("Flight migration opportunity maximum set to 1 for this scene instance.");
    }

    [MenuItem("Tools/Darwin Farm/Debug/Cross Habitat/Restore Candidate Chances")]
    private static void Restore()
    {
        SimulationController simulation = Current();
        if (simulation == null) return;
        simulation.SetCrossHabitatDebugProbabilities(.05f, .005f, 0f);
        simulation.SetFlightCandidateParameters(.1f, 50, .05f);
        simulation.SetEcologicalNicheProbabilities(.02f, .01f, .05f,
            .002f, .001f);
        Debug.Log("Cross habitat candidate probabilities restored.");
    }

    private static void SetRolls(float landAir, float airLand, float waterMove)
    {
        SimulationController simulation = Current();
        if (simulation == null) return;
        simulation.SetCrossHabitatDebugProbabilities(landAir,
            airLand, waterMove);
        Debug.Log("Cross habitat debug probabilities: land-air=" + landAir +
            " air-land=" + airLand + " eligible-water=" + waterMove);
    }
}
#endif
