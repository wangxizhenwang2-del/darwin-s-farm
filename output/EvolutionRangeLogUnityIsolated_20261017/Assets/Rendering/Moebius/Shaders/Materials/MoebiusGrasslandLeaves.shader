Shader "Darwin/Moebius Grassland Leaves"
{
    Properties
    {
        [NoScaleOffset] _BaseMap("Shape Atlas", 2D) = "white" {}
        _Tint("Particle Tint", Color) = (.61,.68,.32,1)
        _Opacity("Opacity", Range(0,1)) = .93
        _InkColor("Drawn Leaf Edge", Color) = (.24,.31,.15,1)
        [Toggle] _DepthFade("Soft Intersections", Float) = 1
        _IntersectionDistance("Intersection Fade (Metres)", Range(.01,1)) = .12
        _AtlasColumns("Atlas Columns", Float) = 2
        _AtlasRows("Atlas Rows", Float) = 2
        [HideInInspector] _BoundsXZ("World Bounds XZ", Vector) = (-10000,-10000,10000,10000)
        _BoundaryFade("Boundary Fade (Metres)", Float) = .7
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+1" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "GrasslandLeaves"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MoebiusParticleVertex
            #pragma fragment GrasslandLeavesFragment
            #pragma multi_compile_instancing
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusGrasslandParticles.hlsl"
            ENDHLSL
        }
    }
    Fallback Off
}
