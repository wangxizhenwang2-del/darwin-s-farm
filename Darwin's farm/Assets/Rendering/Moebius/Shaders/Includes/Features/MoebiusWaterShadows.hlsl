#ifndef MOEBIUS_WATER_SHADOWS_INCLUDED
#define MOEBIUS_WATER_SHADOWS_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float WaterShadowStripe(float2 p)
{
    // Spatially anchored pen lines: no animation/frame seeds and no flow distortion.
    float row = floor(p.y+0.5);
    float seed = PrintHash(float2(row,13.7));
    float bend = 0.055*sin(p.x*0.72+seed*6.28);
    float d = abs(frac(p.y+bend+0.5)-0.5);
    float footprint = max(fwidth(p.y),0.0001);
    float coverage = saturate((min(0.2,footprint*0.45)-d)/footprint+0.5);
    return coverage*(1-smoothstep(0.38,0.75,footprint));
}

half3 ApplyWaterCastShadow(half3 color, float3 positionWS)
{
    if (_ReceiveWaterShadows<=0.5 || _WaterShadowStrength<=0) return color;
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
        float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(positionWS));
    #else
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    #endif
    Light light = GetMainLight(shadowCoord);
    float shadow = saturate(1-light.shadowAttenuation);
    float2 p = positionWS.xz*max(_WaterShadowDensity,0.1);
    float2 diagonal = float2(dot(p,float2(0.8,-0.6)),dot(p,float2(0.6,0.8)));
    float first = WaterShadowStripe(diagonal);
    float second = WaterShadowStripe(float2(diagonal.y,-diagonal.x)+7.1);
    float ink = 1-(1-first)*(1-second*smoothstep(0.65,0.95,shadow)*0.3);
    float strength = shadow*saturate(_WaterShadowStrength);
    // Keep the pastel base visible; the darkness is primarily expressed by ink.
    color *= 1-strength*0.06;
    return lerp(color,_WaterShadowColor.rgb,ink*strength);
}
#endif
