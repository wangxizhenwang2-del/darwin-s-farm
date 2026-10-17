Shader "Darwin/Moebius Wind"
{
    Properties
    {
        [NoScaleOffset] _BaseMap("Hand Drawn Wind Atlas", 2D) = "white" {}
        _Tint("Wind Stroke Color", Color) = (0.88,0.93,0.75,1)
        _Opacity("Opacity", Range(0,1)) = 0.72
        _Thickness("Stroke Thickness", Range(0.6,2)) = 1
        _Distortion("Hand Drawn Drift", Range(0,0.08)) = 0.012
        _AnimationSpeed("Shape Motion Speed", Range(0,2)) = 0.6
        _AtlasColumns("Atlas Columns", Float) = 2
        _AtlasRows("Atlas Rows", Float) = 2
        [HideInInspector] _BoundsXZ("World Bounds XZ", Vector) = (-10000,-10000,10000,10000)
        _BoundaryFade("Boundary Fade (Metres)", Float) = 0.7
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+2" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "WindStrokes"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MoebiusParticleVertex
            #pragma fragment WindFragment
            #pragma multi_compile_instancing
            #include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusParticleInput.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusParticleCommon.hlsl"
            half4 WindFragment(ParticleVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 localUV = ParticleCellUV(input.uv);
                float bend = sin(localUV.x*6.283 + _Time.y*_AnimationSpeed) * _Distortion;
                bend *= sin(saturate(localUV.x)*3.14159);
                float mask = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, ClampParticleCell(input.uv,float2(0,bend))).a;
                // Keep the filtered alpha coverage. Thresholding at 0.5 erases thin strokes
                // once their atlas mip becomes smaller than a screen pixel.
                mask = saturate(mask * max(_Thickness,0.6));
                float endFade = smoothstep(0.035,0.16,localUV.x) * (1-smoothstep(0.82,0.97,localUV.x));
                return half4(_Tint.rgb*input.color.rgb,mask*endFade*ParticleOpacity(input));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
