#ifndef MOEBIUS_INPUT_INCLUDED
#define MOEBIUS_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_ControlMap); SAMPLER(sampler_ControlMap);
TEXTURE2D(_LightingRamp); SAMPLER(sampler_LightingRamp);
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor, _ShadowColor, _DeepShadowColor, _HighlightColor, _InkColor;
float _ShadowThreshold, _DeepShadowThreshold, _ShadowSoftness, _BrightThreshold;
float _UseRamp, _UseVertexColor, _DebugMode;
float _HatchScale, _HatchStrength, _HatchContrast, _HatchRotation, _HatchThreshold;
float _HatchLineWidth, _HatchIrregularity, _HatchCrossStrength, _PaperLift, _SurfaceTintStrength;
float _HatchThirdStrength;
float _CastShadowHatchStrength, _CastShadowTintStrength;
float _UseHatchTexture, _StippleStrength, _StippleScale, _StippleThreshold;
float _HighlightThreshold, _HighlightSize, _HighlightStrength, _HighlightNoiseStrength;
float _HighlightViewInfluence, _HighlightInkWidth, _HighlightInkStrength;
CBUFFER_END
struct NPRAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct NPRVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    half4 color : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
NPRVaryings NPRVertex(NPRAttributes input)
{
    NPRVaryings output = (NPRVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = p.positionCS;
    output.positionWS = p.positionWS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.color = input.color;
    return output;
}
#endif
