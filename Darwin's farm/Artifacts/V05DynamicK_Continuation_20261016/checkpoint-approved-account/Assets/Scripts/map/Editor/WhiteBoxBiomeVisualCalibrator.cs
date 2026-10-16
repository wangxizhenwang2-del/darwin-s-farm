using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Keeps imported biome samples untouched. The wrappers fit the 15-unit visual
// interior of a 17-unit WhiteBox tile, leaving the transition rim visible.
public static class WhiteBoxBiomeVisualCalibrator
{
    private const string SourceFolder = "Assets/Rendering/Moebius/Samples/Biomes/Prefabs/";
    private const string OutputFolder = "Assets/Rendering/WhiteBoxCalibratedBiomes";
    private const float VisualWidth = 15f;

    private struct Entry
    {
        public string source;
        public string output;
        public string ground;

        public Entry(string source, string output, string ground)
        { this.source = source; this.output = output; this.ground = ground; }
    }

    private static readonly Entry[] Entries =
    {
        new Entry("Desert_Moebius", "Desert_WhiteBoxVisual", "pPlane1"),
        new Entry("Forest_Moebius", "Forest_WhiteBoxVisual", "Fprest_Block"),
        new Entry("Grassland_Moebius", "Grassland_WhiteBoxVisual", "Land1"),
        new Entry("Highland_Colored", "Highland_WhiteBoxVisual", "Ground - Paint Here")
    };

    [MenuItem("Tools/Darwin's Farm/Calibrate WhiteBox Biome Visuals")]
    public static string CalibrateAll()
    {
        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder("Assets/Rendering", "WhiteBoxCalibratedBiomes");

        var report = new StringBuilder();
        report.AppendLine("# WhiteBox biome visual calibration");
        report.AppendLine();
        report.AppendLine("Target: 15 × 15 visual interior within the 17 × 17 tile. Bounds use enabled renderers. The lowest visible point is aligned to local Y=0. These wrappers are visual assets only; terrain collision and navigation remain in ColorSurface.");
        report.AppendLine();
        report.AppendLine("| Source | Original bounds X×Z | Original bottom Y | Scale factor | Calibrated bottom Y | Ground mesh top Y after scaling | Wrapper |");
        report.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | --- |");

        foreach (Entry entry in Entries)
        {
            string path = SourceFolder + entry.source + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Missing prefab: " + path);

            GameObject wrapper = new GameObject(entry.output);
            try
            {
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                model.transform.SetParent(wrapper.transform, false);
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                if (renderers.Length == 0)
                    throw new InvalidOperationException("No visible renderers: " + path);
                Bounds original = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1)) original.Encapsulate(renderer.bounds);
                Renderer ground = renderers.FirstOrDefault(r => r.name == entry.ground);
                if (ground == null)
                    throw new InvalidOperationException("Ground mesh missing: " + entry.ground);
                float originalGroundTop = ground.bounds.max.y;
                float factor = VisualWidth / Mathf.Max(original.size.x, original.size.z);

                model.transform.localScale *= factor;
                model.transform.localPosition = new Vector3(
                    -original.center.x * factor,
                    -original.min.y * factor,
                    -original.center.z * factor);

                Bounds calibrated = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1)) calibrated.Encapsulate(renderer.bounds);
                if (Mathf.Abs(calibrated.min.y) > 0.01f ||
                    Mathf.Max(calibrated.size.x, calibrated.size.z) > VisualWidth + 0.01f)
                    throw new InvalidOperationException("Calibration failed for " + entry.source);

                string output = OutputFolder + "/" + entry.output + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(wrapper, output);
                report.AppendLine("| " + entry.source + " | " + F(original.size.x) + " × " +
                    F(original.size.z) + " | " + F(original.min.y) + " | " + F(factor) +
                    " | " + F(calibrated.min.y) + " | " +
                    F((originalGroundTop - original.min.y) * factor) + " | " +
                    entry.output + ".prefab |");
            }
            finally { UnityEngine.Object.DestroyImmediate(wrapper); }
        }

        report.AppendLine();
        report.AppendLine("At runtime the tile root rotates the wrapper. Its source ground is sampled on a 0.5 m grid, smoothed, scaled with an Inspector-adjustable strength and height cap, and faded into the existing transition surface at its edge. The source ground renderer is hidden; ColorSurface remains the only walkable ground and collider. Decorations are projected onto that surface, while simplified obstacle proxies and Not Walkable volumes block large trees, rocks and mountains.");

        string reportPath = OutputFolder + "/CalibrationReport.md";
        System.IO.File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
        AssetDatabase.Refresh();
        return report.ToString();
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
