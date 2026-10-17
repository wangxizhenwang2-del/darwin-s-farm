#if UNITY_EDITOR
// Legacy entry point. The current integration suite includes forest rotations,
// original native ground/material checks, previews, transitions and movement.
// The former pilot assumed a simplified ground with an identical legacy collider.
public static class ForestArtworkChecks
{
    public static string Result => BiomeArtworkRolloutChecks.Result;
    public static void Run() => BiomeArtworkRolloutChecks.Run();
}
#endif
