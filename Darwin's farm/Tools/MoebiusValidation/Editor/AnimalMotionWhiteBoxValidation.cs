using System;
using System.Collections;
using System.IO;
using System.Linq;
using Darwin.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class AnimalMotionWhiteBoxValidation
{
    const string Flag = "AnimalMotionWhiteBoxValidation";
    static AnimalMotionWhiteBoxValidation() => EditorApplication.update += Update;
    public static void Run()
    {
        if (!Application.isBatchMode || !File.Exists("AnimalMotionFixture.marker")) throw new Exception("Isolated fixture required.");
        ShaderUtil.allowAsyncCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity");
        SessionState.SetBool(Flag, true);
        EditorApplication.EnterPlaymode();
    }
    static void Update()
    {
        if (!EditorApplication.isPlaying || !SessionState.GetBool(Flag, false)) return;
        SessionState.SetBool(Flag, false);
        new GameObject("Motion VFX acceptance").AddComponent<AnimalMotionWhiteBoxProbe>();
    }
}

public sealed class AnimalMotionWhiteBoxProbe : MonoBehaviour
{
    const string Output = "Validation/AnimalMotion";
    IEnumerator Start()
    {
        Directory.CreateDirectory(Output);
        var checks = Checks();
        while (true)
        {
            bool more;
            try { more = checks.MoveNext(); }
            catch (Exception error)
            {
                File.WriteAllText(Output + "/Report.txt", "FAIL: " + error);
                Debug.LogException(error); EditorApplication.Exit(1); yield break;
            }
            if (!more) break;
            yield return checks.Current;
        }
        File.WriteAllText(Output + "/Report.txt", "PASS: real WhiteBox air/water populations receive shared motion VFX; land excluded; actual movement history, camera renders, stop fade, teleport reset, toggles and fixed mesh/material verified.");
        EditorApplication.Exit(0);
    }
    IEnumerator Checks()
    {
        yield return null;
        var journal = GameObject.Find("JournalCanvas");
        journal?.transform.Find("SafeArea/TitlePanel/NewGame")?.GetComponent<Button>()?.onClick.Invoke();
        FindFirstObjectByType<TutorialFlowController>()?.CompleteForAutomation();
        var grid = FindFirstObjectByType<MapGridManager>();
        float until = Time.realtimeSinceStartup + 20;
        while (grid == null || !grid.TryGetTile(Vector2Int.zero, out _))
        { Require(Time.realtimeSinceStartup < until, "Initial map missing."); yield return null; grid = FindFirstObjectByType<MapGridManager>(); }
        grid.GetComponent<SimulationTime>().Pause();
        grid.GetComponent<BioenergyAccount>().CostsEnabled = false;
        var river = AssetDatabase.LoadAssetAtPath<MapTileDefinition>("Assets/Scripts/map/data/横向水道.asset");
        Require(grid.TryPlaceTile(Vector2Int.right, river), "Could not place actual biome river.");
        grid.TryGetTile(Vector2Int.zero, out var origin);
        grid.TryGetTile(Vector2Int.right, out var water);
        var flyer = Draft("Assets/BiowebV46/Species/鹿飞-a61486cd.asset", EcologicalNiche.Air);
        var swimmer = Draft("Assets/BiowebV46/Species/鹿游-103a527e.asset", EcologicalNiche.Water);
        origin.Block.community.Add(flyer); water.Block.community.Add(swimmer);
        var presenter = grid.GetComponent<PopulationVisualPresenter>();
        presenter.SyncNow(); yield return null; presenter.SyncNow();
        until = Time.realtimeSinceStartup + 25;
        WhiteboxPopulation airView = null, waterView = null;
        while (!presenter.TryGetVisual(flyer, out airView) || !presenter.TryGetVisual(swimmer, out waterView))
        { Require(Time.realtimeSinceStartup < until, "Air or swimming visual missing."); yield return null; presenter.SyncNow(); }
        MoebiusAnimalMotionStrokes air = null, swim = null;
        while (air == null || swim == null || air.VisibleStrength < .1f || swim.VisibleStrength < .1f)
        {
            Require(Time.realtimeSinceStartup < until, "Motion effects did not follow live populations.");
            yield return null;
            air = airView.GetComponentInChildren<MoebiusAnimalMotionStrokes>(true);
            swim = waterView.GetComponentInChildren<MoebiusAnimalMotionStrokes>(true);
        }
        Require(!air.swimming && swim.swimming, "Wrong medium.");
        Require(air.LiveMeshVertices == 726 && swim.LiveMeshVertices == 726, "Unexpected mesh budget.");
        var material = Resources.Load<MoebiusAnimalMotionStyle>("Population/AnimalMotionVFX").material;
        Require(material != null && air.GetComponent<MeshRenderer>().sharedMaterial == material &&
            swim.GetComponent<MeshRenderer>().sharedMaterial == material, "Material is not shared.");
        foreach (var land in grid.GetComponent<PopulationMovementController>().Populations)
            if (!land.IsAir && !land.IsWater) Require(land.GetComponentInChildren<MoebiusAnimalMotionStrokes>(true) == null, "Land VFX attached.");
        var airMesh = air.GetComponent<MeshFilter>().sharedMesh;
        for (int i = 0; i < 10; i++) yield return null;
        Require(air.GetComponent<MeshFilter>().sharedMesh == airMesh, "Mesh recreated per frame.");
        foreach (var p in airMesh.vertices) Require(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z), "Invalid vertices.");
        Camera camera = Camera.main;
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)) canvas.enabled = false;
        camera.transform.position = new Vector3(22, 22, -27); camera.transform.LookAt(new Vector3(8, 1, 0));
        camera.orthographic = true; camera.orthographicSize = 14;
        yield return new WaitForSeconds(.5f);
        var with = Capture(camera, Output + "/WithEffects.png");
        air.GetComponent<MeshRenderer>().enabled = swim.GetComponent<MeshRenderer>().enabled = false;
        var without = Capture(camera, Output + "/WithoutEffects.png");
        int changed = 0;
        for (int i = 0; i < with.Length; i++)
            if (Mathf.Abs(with[i].r - without[i].r) + Mathf.Abs(with[i].g - without[i].g) + Mathf.Abs(with[i].b - without[i].b) > 8) changed++;
        Require(changed > 20, "Motion strokes are not visible in native render: " + changed);
        air.GetComponent<MeshRenderer>().enabled = swim.GetComponent<MeshRenderer>().enabled = true;
        Directory.CreateDirectory(Output + "/Frames");
        for (int i = 0; i < 24; i++)
        { Capture(camera, Output + "/Frames/" + i.ToString("D2") + ".png"); yield return new WaitForSeconds(.125f); }
        camera.orthographicSize = 3.5f;
        Vector3 focus = airView.Leader.position;
        camera.transform.position = focus + new Vector3(4, 4, -7); camera.transform.LookAt(focus);
        Capture(camera, Output + "/FlightCloseup.png");
        focus = waterView.Leader.position;
        camera.transform.position = focus + new Vector3(4, 5, -7); camera.transform.LookAt(focus);
        Capture(camera, Output + "/SwimmingCloseup.png");
        typeof(WhiteboxPopulation).GetMethod("PauseWandering", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(airView, new object[] { true });
        yield return new WaitForSeconds(3);
        Require(air.VisibleStrength < .08f, "Stopped flyer trail did not fade.");
        typeof(WhiteboxPopulation).GetMethod("PauseWandering", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(airView, new object[] { false });
        var settings = grid.GetComponent<PopulationMovementController>().Settings;
        settings.flightEffects = settings.swimmingEffects = false;
        yield return null;
        Require(!air.gameObject.activeSelf && !swim.gameObject.activeSelf, "Effect toggles ignored.");
        settings.flightEffects = settings.swimmingEffects = true; yield return null;
        Require(air.gameObject.activeSelf && swim.gameObject.activeSelf, "Effects did not resume.");
        air.Evaluate(airView.Leader.position + Vector3.one * 1000, 1.2f, camera, Time.time + .02f, 0);
        Require(air.HistoryCount == 1 && air.VisibleStrength < .01f, "Teleport left a long streak.");
        Debug.Log("MOTION VFX PASS: " + changed + " changed native-render pixels; two shared 726-vertex meshes.");
    }
    static PopulationData Draft(string path, EcologicalNiche niche)
    {
        var species = AssetDatabase.LoadAssetAtPath<SpeciesData>(path);
        Require(species != null, "Species missing: " + path);
        return new PopulationData { species = species, speciesId = species.SpeciesId, lineageName = species.SpeciesName,
            ecologicalNiche = niche, speciesAmount = 80, size = species.baseSize, movementAbility = species.baseMovementAbility,
            fertility = species.baseFertility, fitTemperature = species.baseFitTemperature, fitHumidity = species.baseFitHumidity,
            habitatNiche = 100, trophicLevel = species.trophicLevel, trophicLevelInitialized = true };
    }
    static Color32[] Capture(Camera camera, string path)
    {
        var target = RenderTexture.GetTemporary(1280, 800, 24);
        var previous = RenderTexture.active; var oldTarget = camera.targetTexture;
        var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG()); return image.GetPixels32();
        }
        finally { camera.targetTexture = oldTarget; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Destroy(image); }
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
