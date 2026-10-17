using System.IO;
using UnityEditor;

// Keep the original menu/API on the same calibration path, so it cannot restore
// the old multi-tile forest or overwrite the adapters without their VFX.
public static class WhiteBoxBiomeVisualCalibrator
{
    [MenuItem("Tools/Darwin's Farm/Calibrate WhiteBox Biome Visuals")]
    public static string CalibrateAll()
    {
        BiomeArtworkRolloutSetup.Run();
        return File.ReadAllText(".utmp/biome-rollout/calibration.md");
    }
}
