Shader "Darwin/Moebius Debris"
{
    Properties
    {
        [NoScaleOffset] _BaseMap("Plant Fragment Atlas", 2D) = "white" {}
        _Tint("Debris Color", Color) = (0.64,0.59,0.32,1)
        _InkColor("Drawn Edge Color", Color) = (0.24,0.25,0.16,1)
        _Opacity("Opacity", Range(0,1)) = 0.95
        _EdgeSoftness("Edge Softness", Range(0.01,0.3)) = 0.04
        _AtlasColumns("Atlas Columns", Float) = 2
        _AtlasRows("Atlas Rows", Float) = 2
        [HideInInspector] _BoundsXZ("World Bounds XZ", Vector) = (-10000,-10000,10000,10000)
        _BoundaryFade("Boundary Fade (Metres)", Float) = 0.7
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+1" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Debris"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MoebiusParticleVertex
            #pragma fragment DebrisFragment
            #pragma multi_compile_instancing
            #include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusParticleInput.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusParticleCommon.hlsl"
            half4 DebrisFragment(ParticleVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 fragment = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float aa = max(fwidth(fragment.a), _EdgeSoftness);
                half coverage = smoothstep(0.5-aa, 0.5+aa, fragment.a);
                half3 color = lerp(_InkColor.rgb, _Tint.rgb * input.color.rgb, fragment.r);
                return half4(color, coverage * ParticleOpacity(input));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
