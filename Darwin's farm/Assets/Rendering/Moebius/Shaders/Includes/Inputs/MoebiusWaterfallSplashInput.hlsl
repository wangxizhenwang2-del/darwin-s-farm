#ifndef MOEBIUS_WATERFALL_SPLASH_INPUT_INCLUDED
#define MOEBIUS_WATERFALL_SPLASH_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNoise.hlsl"
CBUFFER_START(UnityPerMaterial)
half4 _SplashColorA,_SplashColorB,_InkLineColor;
float _LayerBlendStrength,_PulseSpeed,_PulseAmplitude,_WobbleStrength,_WobbleSpeed,_DistortionScale;
float _InternalPatternStrength,_SplashInkWidth,_SplashInkStrength,_AnimationPhase,_DebugView;
float _SplashMotionRole;
half4 _WaterShadowColor;
float _ReceiveWaterShadows,_WaterShadowStrength,_WaterShadowDensity;
CBUFFER_END
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterfallSplashMotion.hlsl"
#endif
