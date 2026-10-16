#ifndef MOEBIUS_LAVA_INPUT_INCLUDED
#define MOEBIUS_LAVA_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNoise.hlsl"
TEXTURE2D(_ControlMap); SAMPLER(sampler_ControlMap);
CBUFFER_START(UnityPerMaterial)
half4 _CrustColor,_SecondaryCrustColor,_CoolLavaColor,_WarmLavaColor,_HotLavaColor,_InkColor;
float4 _FlowDirection;
float _CrustCoverage,_CrustScale,_CrustDistortion,_CrackWidth,_CrackSoftness;
float _CrustDriftSpeed,_CrustDeformationStrength,_LavaFlowSpeed,_FlowDistortion,_FlowDetailScale;
float _HeatContrast,_ColorBanding,_HotRegionThreshold,_EnableEmission,_EmissionIntensity;
float _UseHatching,_HatchStrength,_HatchScale,_HatchRotation,_HatchLineWidth,_HatchIrregularity;
float _HatchCrossStrength,_StippleStrength,_StippleScale,_ContourWidth,_ContourStrength,_NPRLightingInfluence;
float _UseControlMap,_UseVertexColor,_AnimationPhase,_DebugView;
half4 _LavaShadowColor;
float _ReceiveLavaShadows,_LavaShadowStrength,_LavaShadowDensity;
CBUFFER_END
struct LavaAttributes
{
    float4 positionOS:POSITION;
    float3 normalOS:NORMAL;
    float2 uv:TEXCOORD0;
    half4 color:COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct LavaVaryings
{
    float4 positionCS:SV_POSITION;
    float3 positionWS:TEXCOORD0;
    half3 normalWS:TEXCOORD1;
    float2 uv:TEXCOORD2;
    half4 color:TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
LavaVaryings LavaVertex(LavaAttributes input)
{
    LavaVaryings output=(LavaVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS=p.positionCS;output.positionWS=p.positionWS;
    output.normalWS=TransformObjectToWorldNormal(input.normalOS);
    output.uv=input.uv;output.color=input.color;
    return output;
}
#endif
