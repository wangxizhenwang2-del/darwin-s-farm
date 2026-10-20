using UnityEngine;
using UnityEngine.SceneManagement;

// One breeding target per game. Match the simulation's ConnectedTargets links.
public static class SpeciesTrackingState
{
    public static SpeciesData Target { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Target = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Target = null;

    public static void Toggle(SpeciesData species) => Target = Target == species ? null : species;

    public static void Restore(SpeciesData species) => Target = species;

    public static bool IsDirectSource(SpeciesData species) => Connected(species, Target);

    public static bool Connected(SpeciesData source, SpeciesData target) =>
        source != null && target != null && source != target &&
        (source.evolutionTargets != null && source.evolutionTargets.Contains(target) ||
         target.evolutionTargets != null && target.evolutionTargets.Contains(source));
}
