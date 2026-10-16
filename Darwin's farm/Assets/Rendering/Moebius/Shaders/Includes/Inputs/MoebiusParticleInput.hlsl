#ifndef MOEBIUS_PARTICLE_INPUT_INCLUDED
#define MOEBIUS_PARTICLE_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
CBUFFER_START(UnityPerMaterial)
    half4 _Tint, _InkColor;
    float4 _BoundsXZ;
    float _Opacity, _EdgeSoftness, _DepthFade, _IntersectionDistance;
    float _Distortion, _AnimationSpeed, _AtlasColumns, _AtlasRows;
    float _Thickness, _BoundaryFade;
CBUFFER_END
struct ParticleAttributes
{
    float4 positionOS : POSITION;
    half4 color : COLOR;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct ParticleVaryings
{
    float4 positionCS : SV_POSITION;
    half4 color : COLOR;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float eyeDepth : TEXCOORD2;
    UNITY_VERTEX_OUTPUT_STEREO
};
ParticleVaryings MoebiusParticleVertex(ParticleAttributes input)
{
    ParticleVaryings output = (ParticleVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = position.positionCS;
    output.positionWS = position.positionWS;
    output.eyeDepth = -position.positionVS.z;
    output.uv = input.uv;
    output.color = input.color;
    return output;
}
#endif
