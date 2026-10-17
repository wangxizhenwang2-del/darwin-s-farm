#ifndef MOEBIUS_GRASSLAND_PARTICLES_INCLUDED
#define MOEBIUS_GRASSLAND_PARTICLES_INCLUDED
#include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusParticleInput.hlsl"
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusParticleCommon.hlsl"

half4 GrasslandMotesFragment(ParticleVaryings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half coverage = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
    // Continuous, low-amplitude modulation, never frame-random sparkle.
    float phase = dot(input.positionWS, float3(.71,.39,.53));
    half brightness = 1 - saturate(_Distortion) * (.5 + .5*sin(phase + _Time.y*_AnimationSpeed));
    return half4(_Tint.rgb*input.color.rgb*brightness, coverage*ParticleOpacity(input)*ParticleIntersectionFade(input));
}
half4 GrasslandLeavesFragment(ParticleVaryings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half4 leaf = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
    half3 color = lerp(_InkColor.rgb, _Tint.rgb*input.color.rgb, leaf.r);
    // Preserve mip-filtered coverage instead of thresholding tiny leaves away.
    return half4(color, leaf.a*ParticleOpacity(input)*ParticleIntersectionFade(input));
}
half4 GrasslandWindFragment(ParticleVaryings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float2 uv = ParticleCellUV(input.uv);
    float bend = sin(uv.x*6.283 + _Time.y*_AnimationSpeed)*_Distortion*sin(saturate(uv.x)*3.14159);
    half coverage = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, ClampParticleCell(input.uv,float2(0,bend))).a;
    coverage = saturate(coverage*max(.6,_Thickness));
    float ends = smoothstep(.035,.16,uv.x)*(1-smoothstep(.82,.97,uv.x));
    return half4(_Tint.rgb*input.color.rgb, coverage*ends*ParticleOpacity(input)*ParticleIntersectionFade(input));
}
#endif
