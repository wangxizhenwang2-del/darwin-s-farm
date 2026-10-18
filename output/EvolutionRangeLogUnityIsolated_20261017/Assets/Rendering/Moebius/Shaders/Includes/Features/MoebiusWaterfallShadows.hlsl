#ifndef MOEBIUS_WATERFALL_SHADOWS_INCLUDED
#define MOEBIUS_WATERFALL_SHADOWS_INCLUDED
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterShadows.hlsl"
half3 ApplyWaterfallCastShadow(half3 color,float3 positionWS,float3 normalWS)
{
    if(_ReceiveWaterShadows<=0.5||_WaterShadowStrength<=0)return color;
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord=ComputeScreenPos(TransformWorldToHClip(positionWS));
    #else
    float4 shadowCoord=TransformWorldToShadowCoord(positionWS);
    #endif
    float shadow=saturate(1-GetMainLight(shadowCoord).shadowAttenuation);
    float3 n=abs(normalWS);
    // Dominant-plane coordinates also work on the vertical waterfall sheet.
    float2 plane=n.z>=n.x&&n.z>=n.y?positionWS.xy:(n.x>=n.y?positionWS.zy:positionWS.xz);
    float2 p=plane*max(_WaterShadowDensity,0.1);
    float2 diagonal=float2(dot(p,float2(0.8,-0.6)),dot(p,float2(0.6,0.8)));
    float ink=WaterShadowStripe(diagonal);
    ink=1-(1-ink)*(1-WaterShadowStripe(float2(diagonal.y,-diagonal.x)+7.1)*smoothstep(0.65,0.95,shadow)*0.3);
    float strength=shadow*saturate(_WaterShadowStrength);
    return lerp(color*(1-strength*0.06),_WaterShadowColor.rgb,ink*strength);
}
#endif
