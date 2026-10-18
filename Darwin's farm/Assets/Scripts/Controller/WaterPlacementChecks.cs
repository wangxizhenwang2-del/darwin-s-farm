#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Checks the actual drag/confirm API and every synchronous TilesChanged subscriber,
// including terrain refreshes before BlockInfo has been initialized.
public static class WaterPlacementChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Output = "Artifacts/WaterPreviewFix";
    public static string Result { get; private set; }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    [MenuItem("Tools/Water/Check preview and first placement (fresh Play Mode)")]
    public static void Run()
    {
        var grid = UnityEngine.Object.FindFirstObjectByType<MapGridManager>();
        Require(Application.isPlaying && grid && grid.GetPlacedTiles().Count() == 1, "Use a fresh WhiteBox Play session.");
        Directory.CreateDirectory(Output);
        var report = new System.Collections.Generic.List<string>();
        var placement = UnityEngine.Object.FindFirstObjectByType<MapTilePlacementController>();
        var mode = UnityEngine.Object.FindFirstObjectByType<MapBuildModeController>();
        var palette = UnityEngine.Object.FindObjectsByType<TilePaletteUI>(FindObjectsInactive.Include, FindObjectsSortMode.None).First();
        grid.GetComponent<SimulationTime>().Pause(); grid.GetComponent<BioenergyAccount>().CostsEnabled = false;
        mode.SetBuildMode(true);
        var water = MapWaterController.GetOrCreate(grid);
        FieldInfo events = typeof(MapGridManager).GetField("TilesChanged", Private);
        var before = (Action)events.GetValue(grid);
        int checksBeforeBlock = 0;
        try
        {
            int index = 0;
            foreach (var definition in palette.Definitions.Where(d => d.initialWaterCoverage != WaterCoverage.Land))
            {
                Vector2Int coordinate = new Vector2Int(++index, 0);
                int networkRevision = water.Network.RebuildCount;
                placement.BeginTouchPlacement(definition);
                var preview = Preview(placement);
                Require(preview && preview.GetComponentInChildren<WaterPlacementPreview>(true), "Water drag still uses a land preview");
                Require(!preview.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("BiomePlacementPreview_")), "Biome preview present on a water drag");
                var surface = preview.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "PreviewWaterSurface");
                Require(surface.enabled && surface.sharedMaterial.shader.name.Contains("Water"), "Preview has no visible authored water");
                var river = preview.GetComponentInChildren<Darwin.Rendering.MoebiusRiverTile>(true);
                for (int rotation = 0; rotation < 4; rotation++)
                {
                    int mask = 0;
                    foreach (var port in river.profile.ports)
                    {
                        Vector3 normal = river.transform.TransformDirection(port.Normal);
                        mask |= normal.z > .5f ? 1 : normal.x > .5f ? 2 : normal.z < -.5f ? 4 : 8;
                    }
                    Require(mask == WaterBodyTopology.OpeningMask(definition, rotation), "Preview rotation does not match placement mouths");
                    placement.RotateTouchPreview();
                }
                preview.SetActive(true); preview.SetActive(false); preview.SetActive(true);
                Require(surface && surface.enabled && !surface.forceRenderingOff, "Hiding/showing the ghost loses water");
                Require(water.Network.RebuildCount == networkRevision, "Preview rebuilt the real river network");
                typeof(MapTilePlacementController).GetField("currentCoordinate", Private).SetValue(placement, coordinate);
                typeof(MapTilePlacementController).GetField("touchPositionSet", Private).SetValue(placement, true);
                events.SetValue(grid, (Action)(() =>
                {
                    foreach (Action subscriber in before.GetInvocationList())
                    {
                        subscriber();
                        if (!grid.TryGetTile(coordinate, out var tile)) continue;
                        if (!tile.Block) checksBeforeBlock++;
                        Require(!tile.GetComponentsInChildren<Transform>(true).Any(t =>
                            t.name.StartsWith("BiomeVisual_") || t.name.StartsWith("RetiringBiomeVisual_")),
                            "A transient land biome was created while committing water");
                    }
                }));
                Require(placement.ConfirmTouchPlacement(), "Real placement confirmation failed");
                events.SetValue(grid, before);
                Require(grid.TryGetTile(coordinate, out var placed) && placed.Block.waterCoverage == definition.initialWaterCoverage &&
                    water.TrySpace(coordinate, out _), "Water was not fully initialized when confirmation returned");
                report.Add(definition.displayName + ": authored water preview, four rotations, reactivation, synchronous placement and no transient land biome passed.");
            }
            Require(checksBeforeBlock > 0, "The pre-BlockInfo event ordering was not exercised");
            placement.BeginTouchPlacement(palette.Definitions.First(d => d.initialWaterCoverage == WaterCoverage.Land));
            Require(Preview(placement).GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("BiomePlacementPreview_")), "Land preview changed");
            Require(!Preview(placement).GetComponentInChildren<WaterPlacementPreview>(true), "Water preview leaked into a land drag");
            placement.CancelDrag();
            report.Add("Existing land preview preserved; terrain-before-BlockInfo ordering checked " + checksBeforeBlock + " times.");
            Result = "PASS: water drag previews and first-frame placement for all four real palette entries.";
            File.WriteAllText(Output + "/Report.txt", Result + "\n" + string.Join("\n", report));
            Debug.Log(Result);
            // Leave a runtime-only preview for an acceptance screenshot.
            ShowPreview();
        }
        catch (Exception error)
        {
            Result = "FAILED: " + error;
            File.WriteAllText(Output + "/Report.txt", Result + "\n" + string.Join("\n", report));
            throw;
        }
        finally { events.SetValue(grid, before); }
    }
    private static GameObject Preview(MapTilePlacementController placement) =>
        (GameObject)typeof(MapTilePlacementController).GetField("previewObject", Private).GetValue(placement);
    public static void ShowPreview()
    {
        var grid = UnityEngine.Object.FindFirstObjectByType<MapGridManager>();
        var placement = UnityEngine.Object.FindFirstObjectByType<MapTilePlacementController>();
        var definition = AssetDatabase.LoadAssetAtPath<MapTileDefinition>("Assets/Scripts/map/data/横向水道.asset");
        placement.BeginTouchPlacement(definition);
        Vector2Int coordinate = new Vector2Int(1, 1);
        typeof(MapTilePlacementController).GetField("currentCoordinate", Private).SetValue(placement, coordinate);
        typeof(MapTilePlacementController).GetField("touchPositionSet", Private).SetValue(placement, true);
        typeof(MapTilePlacementController).GetField("canPlace", Private).SetValue(placement, true);
        var preview = Preview(placement);
        preview.transform.position = grid.GridToWorld(coordinate) + Vector3.up * (definition.heightLevel + .06f);
        preview.SetActive(true);
        typeof(MapTilePlacementController).GetMethod("RefreshPreviewColor", Private).Invoke(placement, null);
        var camera = Camera.main;
        Vector3 target = grid.GridToWorld(coordinate);
        camera.transform.position = target + new Vector3(1, 18, -23); camera.transform.LookAt(target);
        camera.orthographic = true; camera.orthographicSize = 12;
    }
}
#endif
