namespace Darwin.Rendering.Editor
{
    // Shared inspector values for Moebius surface debug shaders.
    // Keep these numeric values aligned with the shader's _DebugMode branches.
    public enum MoebiusSurfaceDebugView
    {
        Composite = 0,
        BaseColor = 1,
        WorldNormal = 2,
        NdotL = 3,
        Shadow = 4,
        DeepShadow = 5,
        HatchR = 6,
        HatchG = 7,
        HatchB = 8,
        CombinedHatch = 9,
        Stipple = 10,
        Highlight = 11,
        CastShadow = 12
    }
}
