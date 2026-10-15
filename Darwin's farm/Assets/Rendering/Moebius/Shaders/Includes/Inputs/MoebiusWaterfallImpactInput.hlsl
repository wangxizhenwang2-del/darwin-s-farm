#ifndef MOEBIUS_WATERFALL_IMPACT_INPUT_INCLUDED
#define MOEBIUS_WATERFALL_IMPACT_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNoise.hlsl"
CBUFFER_START(UnityPerMaterial)
half4 _SurfaceColor,_FoamColor,_InkLineColor;
float _FoamWidth,_FoamNoiseScale,_FoamNoiseStrength,_FoamMotionSpeed;
float _FoamEdgeInkWidth,_FoamEdgeInkStrength,_OutflowSpeed,_RippleDensity,_RippleInkWidth,_RippleStrength;
float _AnimationPhase,_DebugView;
#include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterfallImpactParameters.hlsl"
CBUFFER_END
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterfallFoam.hlsl"
// Optional legacy mesh material; the sample now renders impact directly in receiving water.
float3 WaterfallDeform(float3 p,float2 uv){return p;}
void WaterfallClip(float2 uv,float3 positionOS)
{
    float2 masks=WaterfallImpactMasks(TransformObjectToWorld(positionOS));
    clip(max(masks.x,masks.y)-0.005);
}
#endif
