#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;

// Uses real environment edits, terrain meshes, obstacle proxies and navigation.
// The preview and checks modify only a disposable Play session.
public sealed class BiomeTransitionChecks : MonoBehaviour
{
    public static string Result { get; private set; } = "Not run";
    public static string PreviewDirectory => Path.GetFullPath(".utmp/biome-feedback");
    private SimulationEnvironmentController environment;
    private MapGridManager grid;
    private MapTileColorTransitionSystem visuals;
    private MapTileInstance tile;
    private int frames;
    private int capture;
    private bool previewOnly;

    [MenuItem("Tools/Darwin's Farm/Animation/Preview Biome Feedback (Play Mode)")]
    public static void Preview() => Begin(true);

    public static void Run() => Begin(false);

    private static void Begin(bool previewOnly)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        if (FindFirstObjectByType<BiomeTransitionChecks>() != null) return;
        var check = new GameObject("BiomeTransitionChecks").AddComponent<BiomeTransitionChecks>();
        check.previewOnly = previewOnly;
    }

    private static void Require(bool valid, string message)
    {
        if (valid) return;
        Result = "FAILED: " + message;
        throw new InvalidOperationException(Result);
    }

    private IEnumerator Start()
    {
        Result = "Running: setup";
        grid = FindFirstObjectByType<MapGridManager>();
        environment = grid.GetComponent<SimulationEnvironmentController>();
        visuals = grid.GetComponent<MapTileColorTransitionSystem>();
        FindFirstObjectByType<SimulationTime>().Pause();
        yield return new WaitForSecondsRealtime(0.5f);
        Require(grid.TryGetTile(Vector2Int.zero, out tile), "Missing origin tile.");
        yield return Ready();
        if (!previewOnly)
        {
            Require(!visuals.IsBiomeTransitioning, "Initial loading played a biome transition.");
            // A new neighbor must not make existing decorations replay their growth.
            Transform original = tile.transform.Find("BiomeVisual_Desert");
            var grass = AssetDatabase.LoadAssetAtPath<MapTileDefinition>("Assets/Scripts/map/data/草原.asset");
            Require(grid.TryPlaceTile(Vector2Int.right, grass), "Could not place the neighbor fixture.");
            yield return Ready();
            Require(tile.transform.Find("BiomeVisual_Desert") == original && !visuals.IsBiomeTransitioning,
                "Placing a tile replaced or animated unaffected decorations.");
        }

        // Keep the camera on the changing tile while capturing the live frames.
        var camera = grid.GetComponent<PopulationMovementController>().ViewCamera;
        if (camera == null) camera = Camera.main;
        var controls = camera.GetComponent<ScreenEdgeCamera>();
        if (controls != null) controls.enabled = false;
        camera.transform.position = tile.transform.position + Vector3.up - camera.transform.forward * 25f;
        camera.fieldOfView = 52f;
        if (!previewOnly) Directory.CreateDirectory(PreviewDirectory);
        else
        {
            Edit(55, 35);
            yield return new WaitForSecondsRealtime(1.3f);
        }

        yield return ObserveConversion(55, 55, BiomeType.Grassland, !previewOnly);
        yield return ObserveConversion(35, 75, BiomeType.Forest, !previewOnly);

        Result = "Running: same terrain update and independent neighbor";
        Transform forest = tile.transform.Find("BiomeVisual_Forest");
        Edit(36, 75);
        Require(!visuals.IsBiomeTransitioning && tile.transform.Find("BiomeVisual_Forest") == forest,
            "An ordinary climate update replayed feedback.");
        yield return ObserveConversion(55, 35, BiomeType.Desert, !previewOnly);
        if (previewOnly)
        {
            Result = "Preview complete: desert to grassland to forest to desert. Exit Play mode to restore the scene.";
            Debug.Log(Result);
            Destroy(gameObject);
            yield break;
        }

        Result = "Running: interrupted transitions";
        Edit(55, 55);
        yield return new WaitForSecondsRealtime(0.22f);
        Edit(35, 75);
        yield return new WaitForSecondsRealtime(0.17f);
        Edit(55, 35);
        yield return Ready();
        int revision = grid.Navigation.Revision;
        yield return new WaitForSecondsRealtime(1.3f);
        Require(!visuals.IsBiomeTransitioning && RetiringCount() == 0 && tile.Biome == BiomeType.Desert,
            "Interrupted transitions left old decorations or incomplete feedback.");
        Require(grid.Navigation.Revision == revision, "Visual animation kept rebuilding navigation.");

        // No prefab exists for every terrain. Their color and cleanup still work.
        Edit(55, 75);
        yield return new WaitForSecondsRealtime(1.3f);
        Require(tile.Biome == BiomeType.Rainforest && !visuals.IsBiomeTransitioning && RetiringCount() == 0,
            "Transition to a terrain without decorations did not finish.");
        Edit(55, 55);
        yield return new WaitForSecondsRealtime(1.3f);
        Require(tile.transform.Find("BiomeVisual_Grassland") != null && !visuals.IsBiomeTransitioning,
            "Transition from undecorated terrain did not grow the new decorations.");

        Edit(35, 75);
        visuals.enabled = false;
        Require(!visuals.IsBiomeTransitioning && RetiringCount() == 0,
            "Disabling the renderer left incomplete feedback.");
        visuals.enabled = true;
        Edit(55, 35);
        yield return new WaitForSecondsRealtime(1.3f);
        Result = $"PASS: {frames} observed frames; gradual colors, staggered growth, fixed obstacle sizes, stable population XZ, initial/unchanged tiles, ordinary updates, interruptions, missing prefabs, disable cleanup and no per-frame NavMesh rebuild. Preview: {PreviewDirectory}";
        Debug.Log(Result);
        Destroy(gameObject);
    }

    private void Edit(int temperature, int humidity)
    {
        BlockInfo block = tile.Block;
        Require(environment.TryEditEnvironment(tile.Coordinate, new SimulationEnvironmentEdit
        {
            temperature = temperature, humidity = humidity, elevation = tile.HeightLevel,
            habitatRecovery = block.habitatRecovery, maxPlantBiomass = 1000000f,
            plantBiomass = block.plantBiomass
        }, out string error), error);
    }

    private IEnumerator ObserveConversion(int temperature, int humidity, BiomeType expected, bool record)
    {
        Result = "Running: converting to " + expected;
        MeshCollider ground = tile.transform.Find("ColorSurface").GetComponent<MeshCollider>();
        int center = 17 * 35 + 17;
        Color before = ground.sharedMesh.colors[center];
        Edit(temperature, humidity);
        Require(tile.Biome == expected && visuals.IsBiomeTransitioning, "No feedback for " + expected);
        Require(ColorDistance(before, ground.sharedMesh.colors[center]) < 0.001f, "Ground color jumped immediately.");
        Transform decorationRoot = tile.transform.Find("BiomeVisual_" + expected);
        Require(decorationRoot != null, "Missing target decorations.");
        BoxCollider[] obstacles = decorationRoot.Find("BiomeObstacles").GetComponentsInChildren<BoxCollider>();
        var sizes = new Vector3[obstacles.Length];
        for (int i = 0; i < obstacles.Length; i++) sizes[i] = obstacles[i].bounds.size;
        Color middle = before;
        float nextCapture = 0f;
        float began = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - began < 1.5f)
        {
            yield return null;
            frames++;
            float age = Time.realtimeSinceStartup - began;
            if (age > 0.45f && age < 0.7f) middle = ground.sharedMesh.colors[center];
            for (int i = 0; i < obstacles.Length; i++)
                Require((obstacles[i].bounds.size - sizes[i]).sqrMagnitude < 0.00001f,
                    "A decoration animation scaled its navigation obstacle.");
            foreach (WhiteboxPopulation population in grid.GetComponent<PopulationMovementController>().Populations)
            {
                if (population.Leader == null) continue;
                Vector3 offset = population.Leader.position - population.leader.groundPosition;
                Require(Mathf.Abs(offset.x) + Mathf.Abs(offset.z) < 0.0001f, "Feedback moved an animal off its route.");
            }
            if (record && age >= nextCapture)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(PreviewDirectory, $"frame-{capture++:D3}.png"));
                nextCapture += 0.1f;
            }
        }
        Color after = ground.sharedMesh.colors[center];
        Require(ColorDistance(before, after) > 0.01f && ColorDistance(middle, before) > 0.001f &&
            ColorDistance(middle, after) > 0.001f, "No intermediate ground color was displayed.");
        Require(!visuals.IsBiomeTransitioning && RetiringCount() == 0, "Feedback failed to finish.");
    }

    private IEnumerator Ready()
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (grid.Navigation.IsUpdating && Time.realtimeSinceStartup < deadline) yield return null;
        Require(!grid.Navigation.IsUpdating, "Navigation update timed out.");
        yield return null;
    }

    private int RetiringCount()
    {
        int count = 0;
        foreach (Transform child in tile.transform)
            if (child.name.StartsWith("RetiringBiomeVisual_") && child.gameObject.activeSelf) count++;
        return count;
    }

    private static float ColorDistance(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
}
#endif
