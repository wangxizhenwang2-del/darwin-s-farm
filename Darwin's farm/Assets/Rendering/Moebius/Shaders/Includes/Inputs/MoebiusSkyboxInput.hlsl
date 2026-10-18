#ifndef MOEBIUS_SKYBOX_INPUT_INCLUDED
#define MOEBIUS_SKYBOX_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNoise.hlsl"
CBUFFER_START(UnityPerMaterial)
half4 _ZenithColor, _MidColor, _LowerColor, _HorizonColor, _BelowColor;
float _HorizonHeight, _BelowFade, _LowerTransition, _MidTransition, _ZenithTransition, _TransitionSoftness;
float _Exposure, _Saturation, _GradientSteps, _PigmentStrength, _PigmentScale, _PaperStrength, _FogColorMatch;
half4 _CloudColor, _CloudShadeColor, _CloudInkColor;
float4 _CloudElevationRange, _CloudWindDirection;
float _EnableClouds, _CloudDensity, _CloudScale, _CloudShapeVariation, _CloudCoverage, _CloudRotation;
float _CloudSpeed, _CloudEdgeSoftness, _CloudInkStrength, _CloudInkWidth, _CloudShadeStrength;
half4 _SunColor, _HaloColor, _SunInkColor;
float4 _SunDirection;
float _EnableSun, _UseMainLightSun, _SunRadius, _SunSoftness, _SunBrightness;
float _HaloRadius, _HaloStrength, _SunHorizonInfluence, _AnimationPhase;
float _SunInkStrength, _SunInkWidth;
float _SunHazeStrength, _SunHazeHeight, _SunHazeWidth;
CBUFFER_END
#endif
