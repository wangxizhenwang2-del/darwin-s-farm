#ifndef MOEBIUS_WATERFALL_INPUT_INCLUDED
#define MOEBIUS_WATERFALL_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNoise.hlsl"
CBUFFER_START(UnityPerMaterial)
half4 _BaseColor,_ShadowColor,_HighlightColor,_FlowLineColor,_InkLineColor;
float4 _SheetSize;
float _SurfaceLength,_LipStartV;
half4 _UpstreamColor;
float _FlowSpeed,_FlowScale,_DistortionStrength,_DistortionScale;
float _VerticalLineDensity,_VerticalLineWidth,_FlowLineStrength,_InkLineStrength,_BandStrength;
float _EdgeBreakupStrength,_EdgeBreakupScale,_SheetWobbleStrength,_SheetWobbleSpeed;
float _AnimationPhase,_DebugView;
half4 _WaterShadowColor;
float _ReceiveWaterShadows,_WaterShadowStrength,_WaterShadowDensity;
CBUFFER_END
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterfallFlow.hlsl"
#endif
