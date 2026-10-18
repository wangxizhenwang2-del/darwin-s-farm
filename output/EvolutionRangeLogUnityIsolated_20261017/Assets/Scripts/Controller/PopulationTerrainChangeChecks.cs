#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

// Runs the captured height-change failure against the real terrain, navigation,
// population controller and collision guards. Run in a disposable Play session.
public sealed class PopulationTerrainChangeChecks : MonoBehaviour
{
    public static string Result { get; private set; } = "Not run";
    private PopulationMovementController controller;
    private MapGridManager grid;
    private SimulationEnvironmentController environment;
    private WhiteboxPopulation population;
    private string identity;
    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in Play mode.");
        new GameObject("PopulationTerrainChangeChecks").AddComponent<PopulationTerrainChangeChecks>();
    }
    private void Require(bool condition, string error)
    {
        if (condition) return;
        Result = "FAILED: " + error;
        Debug.LogError(Result);
        throw new InvalidOperationException(Result);
    }
    private IEnumerator Start()
    {
        Result = "Running: setup";
        grid = FindFirstObjectByType<MapGridManager>(); controller = grid.GetComponent<PopulationMovementController>();
        environment = grid.GetComponent<SimulationEnvironmentController>();
        var clock = grid.GetComponent<SimulationTime>(); if (!clock.IsPaused) clock.TogglePause();
        grid.TryGetTile(Vector2Int.zero, out var origin);
        var grass = UnityEditor.AssetDatabase.LoadAssetAtPath<MapTileDefinition>("Assets/Scripts/map/data/草原.asset");
        grid.TryPlaceTile(Vector2Int.right, grass);
        grid.TryPlaceTile(Vector2Int.down, origin.Definition);
        yield return Ready();
        float spawnDeadline = Time.realtimeSinceStartup + 10f;
        while (controller.Populations.Count == 0 && Time.realtimeSinceStartup < spawnDeadline) yield return null;
        Require(controller.Populations.Count > 0, "No initial population.");
        population = controller.Populations[0]; identity = population.UniqueId;
        SetCount(237);
        Require(environment.TryApply(Vector2Int.zero, SimulationInterventionKind.Elevation, 1, out string error), error);
        yield return Ready();
        // Exact coordinates captured from the user's stuck chicken on raised (0,0).
        SetPosition(new Vector3(4.368772f, 1.0443033f, -2.79704237f));
        population.PauseWandering(false);
        yield return Observe("captured raised-terrain position", 10f);
        foreach (int delta in new[] { 1, -1, -1 })
        {
            Require(environment.TryApply(Vector2Int.zero, SimulationInterventionKind.Elevation, delta, out error), error);
            yield return Ready();
            yield return Observe("height " + population.tile.HeightLevel, 6f);
        }
        // New terrain can introduce an obstacle under the representative.
        Require(environment.TryApply(Vector2Int.zero, SimulationInterventionKind.Elevation, 1, out error), error);
        yield return Ready();
        foreach (int count in new[] { 80, 600, 1200 })
        {
            SetCount(count);
            // Changing the count must retain one representative and its identity.
            var obstacle = Array.Find(population.tile.GetComponentsInChildren<Collider>(), c => c.name.StartsWith("BiomeObstacle_"));
            Require(obstacle != null, "Missing highland obstacle fixture.");
            SetPosition(obstacle.bounds.center);
            yield return Ready();
            yield return Observe("new obstacle / count " + count, 7f);
        }
        Require(population.UniqueId == identity, "Terrain recovery replaced the population identity.");
        Result = "PASS: captured height-change trap, 0/1/2 elevation round trip, new terrain obstacle recovery at counts 80/600/1200 with one representative, valid ground poses, collision separation and sustained movement.";
        Debug.Log(Result); Destroy(gameObject);
    }
    private void SetCount(int count)
    {
        if (population.EcologicalPopulation != null) population.EcologicalPopulation.speciesAmount = count;
        population.Count = count;
    }
    private void SetPosition(Vector3 position)
    {
        population.leader.groundPosition = position;
        population.leader.visual.gameObject.SetActive(true);
        population.NavigationChanged();
    }
    private IEnumerator Ready()
    {
        float until = Time.realtimeSinceStartup + 15f;
        while (grid.Navigation.IsUpdating && Time.realtimeSinceStartup < until) yield return null;
        Require(!grid.Navigation.IsUpdating, "Navigation bake did not finish.");
        // Allow the controller to consume the completed revision and repair poses.
        yield return null; yield return null;
    }
    private IEnumerator Observe(string stage, float duration)
    {
        Result = "Running: " + stage;
        float until = Time.realtimeSinceStartup + duration;
        Vector3 previous = population.Leader.position;
        float travelled = 0f, stall = 0f, longestStall = 0f;
        while (Time.realtimeSinceStartup < until)
        {
            yield return new WaitForSecondsRealtime(.1f);
            Require(population != null && population.UniqueId == identity, stage + ": lost entity");
            Vector3 current = population.Leader.position;
            float moved = Vector2.Distance(PopulationCollisionMath.Flat(previous), PopulationCollisionMath.Flat(current));
            travelled += moved; stall = moved < .003f ? stall + .1f : 0f;
            longestStall = Mathf.Max(longestStall, stall); previous = current;
            Require(population.ValidateMovement(out string problem), stage + ": " + problem);
            Require(population.transform.childCount == 2 && population.Leader.name == "Leader",
                stage + ": population no longer has exactly one representative and one count label");
            Require(population.space.TryGround(population.leader.groundPosition, out var hit) &&
                Mathf.Abs(hit.point.y - population.leader.groundPosition.y) < .03f, stage + ": stale ground height");
        }
        Require(travelled > 1f && longestStall < 5f,
            stage + ": stalled, travel=" + travelled.ToString("F2") + " longest stop=" + longestStall.ToString("F2"));
        Debug.Log("PASS " + stage + ": travel=" + travelled.ToString("F2") + "m, max stop=" + longestStall.ToString("F2") + "s");
    }
}
#endif
