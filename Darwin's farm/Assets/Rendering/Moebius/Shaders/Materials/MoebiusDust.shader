Shader "Darwin/Moebius Dust"
{
    Properties
    {
        [NoScaleOffset] _BaseMap("Dust Silhouette Atlas", 2D) = "white" {}
        [NoScaleOffset] _NoiseMap("Continuous Dust Noise", 2D) = "gray" {}
        _Tint("Dust Color", Color) = (0.76,0.75,0.5,1)
        _Opacity("Opacity", Range(0,1)) = 0.34
        _EdgeSoftness("Edge Softness", Range(0.01,0.5)) = 0.18
        _Distortion("Shape Drift", Range(0,0.1)) = 0.025
        _AnimationSpeed("Internal Motion Speed", Range(0,1)) = 0.1
        [Toggle] _DepthFade("Scene Depth Intersection Fade", Float) = 1
        _IntersectionDistance("Intersection Fade (Metres)", Range(0.01,1)) = 0.12
        _AtlasColumns("Atlas Columns", Float) = 2
        _AtlasRows("Atlas Rows", Float) = 2
        [HideInInspector] _BoundsXZ("World Bounds XZ", Vector) = (-10000,-10000,10000,10000)
        _BoundaryFade("Boundary Fade (Metres)", Float) = 0.7
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Dust"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MoebiusParticleVertex
            #pragma fragment DustFragment
            #pragma multi_compile_instancing
            #include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusParticleInput.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusParticleCommon.hlsl"
            half4 DustFragment(ParticleVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 drift = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap,
                    input.positionWS.xz * 0.33 + _Time.y * _AnimationSpeed * float2(0.27,0.13)).rg - 0.5;
                float2 uv = ClampParticleCell(input.uv, drift * _Distortion);
                float coverage = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a;
                float softness = max(fwidth(coverage), _EdgeSoftness);
                float mask = smoothstep(0.26 - softness, 0.62 + softness, coverage);
                half alpha = mask * ParticleOpacity(input) * ParticleIntersectionFade(input);
                return half4(_Tint.rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
