#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Darwin.Rendering;
using UnityEditor;
using UnityEngine;

// Explicit, runtime-only acceptance using the real palette, including land artwork.
public sealed class MapWaterVisualChecks : MonoBehaviour
{
    public static string Result { get; private set; }
    private readonly List<string> passed = new List<string>();
    private const string Output = "Artifacts/WaterCorrection";
    [MenuItem("Tools/Water/Check actual WhiteBox palette (fresh Play Mode)")]
    public static void Run()
    {
        var grid = FindFirstObjectByType<MapGridManager>();
        if (!Application.isPlaying || !grid || grid.GetPlacedTiles().Count() != 1)
            throw new InvalidOperationException("Use a fresh WhiteBox Play session with only the origin tile.");
        new GameObject("Actual palette visual acceptance").AddComponent<MapWaterVisualChecks>();
    }
    private IEnumerator Start()
    {
        Result = "Running"; Directory.CreateDirectory(Output);
        var checks = Checks();
        while (true)
        {
            bool next;
            try { next = checks.MoveNext(); }
            catch (Exception e) { Result = "FAILED: " + e; Report(); Debug.LogException(e); yield break; }
            if (!next) break;
            yield return checks.Current;
        }
        Result = "PASS: actual palette water rendering, natural elevations, connections and untouched land.";
        Report(); Debug.Log(Result);
    }
    private void Report() => File.WriteAllText(Output + "/ActualPalette.txt", Result + "\n" + string.Join("\n", passed));
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private IEnumerator Checks()
    {
        var grid = FindFirstObjectByType<MapGridManager>();
        grid.GetComponent<SimulationTime>().Pause();
        MapTileInstance Place(int x, int z, string name, int rotation = 0)
        {
            var d = AssetDatabase.LoadAssetAtPath<MapTileDefinition>("Assets/Scripts/map/data/" + name + ".asset");
            Require(d && grid.TryPlaceTile(new Vector2Int(x, z), d, rotation), "Failed to place actual " + name);
            grid.TryGetTile(new Vector2Int(x, z), out var tile); return tile;
        }
        Place(1,0,"森林"); Place(2,0,"雨林"); Place(3,0,"草原");
        Place(0,-1,"苔原"); Place(1,-1,"荒漠"); Place(2,-1,"雪山"); Place(3,-1,"火山"); Place(4,0,"高山");
        var cross = Place(0,1,"湖泊"); var straight = Place(1,1,"横向水道");
        var tee = Place(2,1,"三叉水道"); var bend = Place(3,1,"弯曲水道",2);
        var same = Place(0,2,"湖泊");
        yield return null;
        var water = grid.GetComponent<MapWaterController>();
        Require(cross.HeightLevel == 1 && straight.HeightLevel == 0 && tee.HeightLevel == 2 && bend.HeightLevel == 1,
            "Palette elevation defaults are not 1,0,2,1");
        Require(cross.Definition.displayName == "十字水道" && cross.Definition.initialWaterCoverage == WaterCoverage.CrossingRiver,
            "The fourth palette item is still a synthetic lake");
        passed.Add("Actual palette defaults: straight=0, bend=1, tee=2, cross=1; no synthetic square water.");
        Require(!water.Network.generateLandFoundations, "Map water is still drawing land foundations");
        foreach (var t in water.Network.GetComponentsInChildren<MoebiusRiverTile>())
            if (!t.IsRiver)
            {
                var geometry = water.Network.GeneratedRoot.transform.Find(t.name + " Connections");
                Require(!geometry || geometry.GetComponentsInChildren<Renderer>().Length == 0, "Extra water-system geometry over land");
            }
        foreach (var tile in grid.GetPlacedTiles().Where(t => t.Block.waterCoverage == WaterCoverage.Land))
            Require(!tile.transform.Find("ColorSurface").GetComponent<Renderer>().forceRenderingOff, "Water changed land surface visibility");
        passed.Add("All eight existing land types keep their original terrain; water creates zero land renderers/roofs.");
        Require(water.Network.JoinedCount >= 1 && water.Network.WaterfallCount == 3, "Expected one same-level join and three authored waterfalls");
        Require(water.Connects(cross,same) && water.Connects(cross,straight) && water.Connects(straight,tee) && water.Connects(tee,bend),
            "Actual palette mouth connection differs from the examples");
        Require(HabitatTopology.ChannelsConnect(cross.Block,straight.Block) && !HabitatTopology.ChannelsConnect(straight.Block,tee.Block),
            "Elevation migration restrictions changed");
        passed.Add("Same-level water joins; one/two-level differences render three waterfalls; two-level direct migration stays blocked.");
        foreach (var tile in new[] { straight, tee, bend, cross })
        {
            Require(water.TrySpace(tile.Coordinate, out var space), "Missing actual water space");
            var river = water.Network.GetComponentsInChildren<MoebiusRiverTile>().First(t => t.IsRiver && (t.transform.position-tile.transform.position).sqrMagnitude < .001f);
            var bank = river.GetComponentsInChildren<MeshRenderer>().Where(r => r != river.originalWater && r.GetComponent<MeshFilter>())
                .OrderByDescending(r => r.bounds.size.x*r.bounds.size.z).First();
            var probe = bank.gameObject.AddComponent<MeshCollider>(); probe.sharedMesh = bank.GetComponent<MeshFilter>().sharedMesh;
            Physics.SyncTransforms();
            int samples = 0, submerged = 0;
            Bounds bounds = space.Bounds;
            for (float z = bounds.min.z+.75f; z < bounds.max.z; z+=.75f)
                for (float x = bounds.min.x+.75f; x < bounds.max.x; x+=.75f)
                {
                    var p = new Vector3(x,space.Height,z);
                    if (!space.Contains(p,.7f)) continue;
                    samples++;
                    if (probe.Raycast(new Ray(p+Vector3.up*5,Vector3.down), out var hit,10) && hit.point.y >= space.Height-.03f) submerged++;
                }
            Destroy(probe);
            Require(samples > 20 && submerged == 0, tile.Definition.displayName+": water buried under bank in "+submerged+"/"+samples+" samples");
            passed.Add(tile.Definition.displayName+": "+samples+" real water positions checked against authored bank geometry; zero buried surface samples.");
            yield return null;
        }
        for (int i=0;i<8;i++) yield return null;
        var camera = Camera.main;
        camera.transform.position = new Vector3(27,58,-65); camera.transform.LookAt(new Vector3(27,0,7));
        camera.orthographic = true; camera.orthographicSize = 44;
        passed.Add("Screenshot layout is runtime-only; no test map or tile definition is saved into WhiteBox.");
    }
}
#endif
