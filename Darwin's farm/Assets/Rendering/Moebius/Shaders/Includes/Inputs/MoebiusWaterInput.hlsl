#ifndef MOEBIUS_WATER_INPUT_INCLUDED
#define MOEBIUS_WATER_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNoise.hlsl"
TEXTURE2D(_ShorelineMask); SAMPLER(sampler_ShorelineMask);
CBUFFER_START(UnityPerMaterial)
half4 _WaterColor, _RippleColor, _FoamColor, _FoamInkColor, _WaterShadowColor;
float4 _FlowDirection, _WaveDirection, _ShoreWorldRect;
float _EnableVariation, _ColorVariation, _VariationScale;
float _EnableFlow, _FlowSpeed, _FlowScale, _FlowDistortion, _FlowStrength;
float _EnableWaves, _WaveHeight, _WaveLength, _WaveSpeed;
float _EnableRipples, _RippleStrength, _RippleDensity, _RippleLength;
float _RippleWidth, _RippleSpeed, _RippleDistortion;
float _RippleWobbleStrength, _RippleWobbleSpeed;
float _EnableFoam, _FoamWidth, _FoamIntensity, _FoamSpeed, _FoamNoiseScale;
float _FoamIrregularity, _EnableFoamInk, _FoamInkStrength, _FoamInkWidth;
float _EnableFoamPatches, _FoamPatchStrength, _EnableShoreMotion, _ShoreMotionStrength;
float _EnableShoreMask, _ShoreDistanceRange, _ShoreWaveFade;
float _ReceiveWaterShadows, _WaterShadowStrength, _WaterShadowDensity;
float _DebugView, _AnimationPhase;
CBUFFER_END
float2 WaterDirection(float2 direction)
{
    return dot(direction,direction)>0.0001 ? normalize(direction) : float2(1,0);
}
float WaterTime() { return _Time.y + _AnimationPhase; }
float2 ShoreUV(float2 positionXZ)
{
    return (positionXZ-_ShoreWorldRect.xy)/max(_ShoreWorldRect.zw,0.001);
}
float3 WaterShoreData(float2 positionXZ)
{
    float3 openWater = float3(max(_ShoreDistanceRange,0.01),0,0);
    if (_EnableShoreMask <= 0.5) return openWater;
    float2 uv = ShoreUV(positionXZ);
    // Outside the baked rectangle is open water, never a repeated shoreline.
    if (any(uv<0) || any(uv>1)) return openWater;
    float3 packed = SAMPLE_TEXTURE2D_LOD(_ShorelineMask,sampler_ShorelineMask,uv,0).rgb;
    return float3(packed.r*max(_ShoreDistanceRange,0.01),packed.gb*2-1);
}
float WaterShoreDistance(float2 positionXZ) { return WaterShoreData(positionXZ).x; }
#endif
